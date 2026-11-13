"""Checks a multi-process run directory (Tools/run_local4.ps1): every process completed the same matches, received
identical result hashes, saw a valid v5 phase sequence, and logged no exceptions. Exit code 0 = pass.

v5 log lines (LineClient): "phase X -> Y day=D task=T week=W" and "results week=W hash=H fired=a,b winners=c players=N".
Phase grammar (docs/V5_ENGINE_SPEC.md section 3):
    Intro
    TaskPlay TaskReveal Break  TaskPlay TaskReveal Break        (tasks 1 and 2)
    MeetingDossier MeetingDiscussion [MeetingVote MeetingVerdict] (meeting 1; vote skipped without vote-able failures)
    TaskPlay TaskReveal                                          (task 3)
    [MeetingDossier MeetingDiscussion MeetingVote MeetingVerdict] (meeting 2 only when task 3 failed)
    CrisisCalls CrisisVote CrisisVerdict
    FinalDiscussion FinalDefense (FinalVote [FinalVote] FinalVerdict) x firings (1 for 3-4 players, 2 for 5-6; a tie adds one revote)
    Results

v5 bot talk (docs/V5_BOT_TALK.md): each bot logs at Results "bottalk line ... policy=P ... truthful=yes|no",
"bottalk check ... ok=yes|no" (every claim compared with the results) and "bottalk summary ...". The script adds them up
per policy and fails when an Honest bot lied or a claim's truth flag did not match the results. Runs without bots pass.

v5 Yaramazlıklar (docs/V5_YARAMAZLIKLAR.md): at Results the host logs "host: pranks week=W enabled=.. total=N tutanak=T
burned=B" and "host: prank i=.. kind=K actor=A target=T ... tutanak=yes|no culprit=C"; each bot logs "bottalk prank seat=S
policy=P kind=K target=T ... cost=C tutanak=yes|no culprit=C sealed=yes|no prim_after=N" (cakes as they land) and
"bottalk pranks seat=S policy=P made=.. tutanak=.. spent=.. final_prim=..". The script reports pranks per kind and per
policy and the tutanak count, and fails on negative prim, a sealed candidate spending prim, an Honest bot targeting a
colleague (the results' cake excepted: everyone may cake the winner), or a bot overflowing the patience itself (only
Greedy's phone ring may, and its tutanak lands on the target).
"""
import glob
import os
import re
import sys

TOKENS = {
    "Intro": "I", "TaskPlay": "P", "TaskReveal": "R", "Break": "B",
    "MeetingDossier": "D", "MeetingDiscussion": "S", "MeetingVote": "V", "MeetingVerdict": "E",
    "CrisisCalls": "C", "CrisisVote": "K", "CrisisVerdict": "X",
    "FinalDiscussion": "F", "FinalDefense": "G", "FinalVote": "O", "FinalVerdict": "Z", "Results": "Q",
}


def grammar(players):
    firings = 2 if players >= 5 else 1
    return re.compile(r"^I PRB PRB DS(?:VE)? PR (?:DSVE)? CKX FG (?:OO?Z){%d} Q$".replace(" ", "") % firings)


def describe(seq):
    counts = {}
    for p in seq:
        counts[p] = counts.get(p, 0) + 1
    meeting2 = "yes" if counts.get("MeetingDossier", 0) >= 2 else "no"
    votes = counts.get("MeetingVote", 0)
    return "meetings=%d meeting2=%s meetingVotes=%d finalVotes=%d firings=%d" % (
        counts.get("MeetingDossier", 0), meeting2, votes, counts.get("FinalVote", 0), counts.get("FinalVerdict", 0))


SUMMARY = re.compile(r"bottalk summary seat=(\d+) policy=(\w+) lines=(\d+) truthful=(\d+) lies=(\d+) calls=(\d+) answered=(\d+) "
                     r"refused=(\d+) answers=(\d+) chat=(\d+) paid=(\d+) broken=(\d+) checks=(\d+) mismatches=(\d+)")
FIELDS = ["lines", "truthful", "lies", "calls", "answered", "refused", "answers", "chat", "paid", "broken", "checks", "mismatches"]


def bot_talk(logs):
    """Bot talk totals per policy; False when an Honest line was a lie or a claim did not match the results."""
    per = {}
    bad = []
    phone = chat = 0
    for path in logs:
        text = open(path, encoding="utf-8", errors="replace").read()
        for m in SUMMARY.finditer(text):
            row = per.setdefault(m.group(2), dict((f, 0) for f in FIELDS + ["bots"]))
            row["bots"] += 1
            for i, f in enumerate(FIELDS):
                row[f] += int(m.group(3 + i))
        for m in re.finditer(r"bottalk line [^\r\n]*", text):
            line = m.group(0)
            if "policy=Honest" in line and "truthful=no" in line:
                bad.append(os.path.basename(path) + ": " + line[:180])
            if "via=phone" in line:
                phone += 1
            elif "via=chat" in line:
                chat += 1
        for m in re.finditer(r"bottalk check [^\r\n]* ok=no", text):
            bad.append(os.path.basename(path) + ": " + m.group(0)[:180])
    if not per:
        print("bot talk: none logged")
        return True
    for policy in sorted(per):
        r = per[policy]
        print("bot talk %-6s bots=%d lines=%d (truthful %d, lies %d) calls=%d answered=%d refused=%d answers=%d chat=%d paid=%d broken=%d checks=%d mismatches=%d"
              % (policy, r["bots"], r["lines"], r["truthful"], r["lies"], r["calls"], r["answered"], r["refused"], r["answers"], r["chat"],
                 r["paid"], r["broken"], r["checks"], r["mismatches"]))
    print("bot talk: %d phone lines, %d chat lines" % (phone, chat))
    for b in bad[:10]:
        print("   BOT TALK PROBLEM:", b)
    return not bad


PRANK_BOT = re.compile(r"bottalk prank seat=(\d+) policy=(\w+) kind=(\w+) target=(-?\d+) session=(-?\d+) phase=(\w+) cost=(\d+) "
                       r"patience=(-?\d+) tutanak=(yes|no) culprit=(-?\d+) sealed=(yes|no) prim_after=(-?\d+)")
PRANK_SUMMARY = re.compile(r"bottalk pranks seat=(\d+) policy=(\w+) made=(\d+) tried=(\d+) tutanak=(\d+) spent=(\d+) reactions=(\d+) final_prim=(-?\d+)")
PRANK_HOST = re.compile(r"host: prank i=(\d+) kind=(\w+) actor=(-?\d+) target=(-?\d+) session=(-?\d+) phase=(\w+) cost=(\d+) patience=(-?\d+) "
                        r"after=(-?\d+) tutanak=(yes|no) culprit=(-?\d+)")


def pranks(logs):
    """Yaramazlıklar totals per kind and policy; False on a rule a bot (or the engine) must never break."""
    bad = []
    per_policy = {}
    per_kind = {}
    host_total = host_tutanak = host_burned = 0
    hosts = 0
    reactions = 0
    for path in logs:
        name = os.path.basename(path)
        text = open(path, encoding="utf-8", errors="replace").read()
        seen = set()
        for m in PRANK_BOT.finditer(text):
            seat, policy, kind, target = int(m.group(1)), m.group(2), m.group(3), int(m.group(4))
            cost, tutanak, culprit, sealed, prim = int(m.group(7)), m.group(9) == "yes", int(m.group(10)), m.group(11) == "yes", int(m.group(12))
            key = m.group(0)
            if key in seen:
                continue
            seen.add(key)
            row = per_policy.setdefault(policy, {"pranks": 0, "tutanak": 0, "spent": 0, "kinds": {}})
            row["pranks"] += 1
            row["spent"] += cost
            row["kinds"][kind] = row["kinds"].get(kind, 0) + 1
            if tutanak:
                row["tutanak"] += 1
            if prim < 0 and prim != -1:
                bad.append("%s: negative prim after a prank: %s" % (name, key[:160]))
            if sealed and cost > 0:
                bad.append("%s: a sealed candidate spent prim: %s" % (name, key[:160]))
            if policy == "Honest" and target >= 0 and kind != "Cake":
                bad.append("%s: an Honest bot targeted a colleague: %s" % (name, key[:160]))
            if tutanak and not (policy == "Greedy" and kind == "PhoneRing"):
                bad.append("%s: a bot overflowed the patience itself: %s" % (name, key[:160]))
            if tutanak and culprit == seat:
                bad.append("%s: a bot brought itself a tutanak: %s" % (name, key[:160]))
        for m in PRANK_SUMMARY.finditer(text):
            reactions += int(m.group(7))
            if int(m.group(8)) < 0 and int(m.group(8)) != -1:
                bad.append("%s: negative final prim: %s" % (name, m.group(0)[:160]))
        host_lines = PRANK_HOST.findall(text)
        if host_lines or "host: pranks week=" in text:
            hosts += 1
            for h in host_lines:
                host_total += 1
                per_kind[h[1]] = per_kind.get(h[1], 0) + 1
                host_burned += int(h[6])
                if h[9] == "yes":
                    host_tutanak += 1
    if not per_policy and not hosts:
        print("pranks: none logged")
        return True
    if hosts:
        print("pranks (host): total=%d tutanak=%d burned=%d by kind: %s" % (
            host_total, host_tutanak, host_burned, ", ".join("%s %d" % kv for kv in sorted(per_kind.items())) or "-"))
    for policy in sorted(per_policy):
        r = per_policy[policy]
        print("pranks %-6s %d (tutanak %d, prim burned %d): %s" % (
            policy, r["pranks"], r["tutanak"], r["spent"], ", ".join("%s %d" % kv for kv in sorted(r["kinds"].items()))))
    print("pranks: %d bot reactions out loud" % reactions)
    for b in bad[:10]:
        print("   PRANK PROBLEM:", b)
    return not bad


def main(run):
    logs = sorted(f for f in glob.glob(os.path.join(run, "*.log")) if not os.path.basename(f).startswith("unity_"))
    if not logs:
        print("FAIL: no game logs in", run)
        return 1
    ok = True
    results = {}   # week -> {log: (hash, fired, winners, players)}
    for path in logs:
        name = os.path.basename(path)
        text = open(path, encoding="utf-8", errors="replace").read()
        phases = {}
        for m in re.finditer(r"phase (\w+) -> (\w+) day=-?\d+ task=-?\d+ week=(\d+)", text):
            phases.setdefault(int(m.group(3)), []).append(m.group(2))
        players_of = {}
        for m in re.finditer(r"(?<!host: )results week=(\d+) hash=(\w+) fired=([\d,]*) winners=([\d,]*) players=(\d+)", text):
            week = int(m.group(1))
            results.setdefault(week, {})[name] = (m.group(2), m.group(3), m.group(4), int(m.group(5)))
            players_of[week] = int(m.group(5))
        errors = re.findall(r"unity-(?:Exception|Error) .*", text)
        print("%-22s matches=%s errors=%d" % (name, sorted(phases), len(errors)))
        for week, seq in sorted(phases.items()):
            players = players_of.get(week)
            tokens = "".join(TOKENS.get(p, "?") for p in seq)
            if players is None:
                ok = False
                print("   match %d: no results line (phases: %s)" % (week, " ".join(seq)))
                continue
            if not grammar(players).match(tokens):
                ok = False
                print("   match %d: invalid phase sequence for %d players: %s" % (week, players, " ".join(seq)))
            else:
                print("   match %d: %d players, %s" % (week, players, describe(seq)))
        for e in errors[:5]:
            print("   ", e[:200])
        if errors:
            ok = False
    for week, per_log in sorted(results.items()):
        hashes = {v[0] for v in per_log.values()}
        sample = next(iter(per_log.values()))
        print("match %d: %d processes reported results, hashes=%s fired=%s winners=%s players=%d"
              % (week, len(per_log), sorted(hashes), sample[1] or "-", sample[2] or "-", sample[3]))
        if len(hashes) != 1 or len(per_log) != len(logs):
            ok = False
            print("   MISMATCH: not every process received the same result")
    if not bot_talk(logs):
        ok = False
    if not pranks(logs):
        ok = False
    print("PASS" if ok and results else "FAIL")
    return 0 if ok and results else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1]))
