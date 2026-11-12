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
    print("PASS" if ok and results else "FAIL")
    return 0 if ok and results else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1]))
