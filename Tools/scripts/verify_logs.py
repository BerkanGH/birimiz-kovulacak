"""Checks a multi-process run directory (Tools/run_local4.ps1): every process completed the same matches, received
identical result hashes, saw a valid v5 phase sequence, and logged no exceptions. Exit code 0 = pass.

v5 log lines (LineClient): "phase X -> Y day=D task=T week=W" and "results week=W hash=H fired=a,b winners=c players=N".
Phase grammar (docs/V5_ENGINE_SPEC.md section 3):
    Intro
    TaskPlay TaskReveal Break  TaskPlay TaskReveal Break        (tasks 1 and 2)
    MeetingDossier MeetingDiscussion [MeetingVote MeetingVerdict] (meeting 1; vote skipped without vote-able failures)
    TaskPlay TaskReveal                                          (task 3)
    [MeetingDossier MeetingDiscussion [MeetingVote MeetingVerdict]] (meeting 2: v5.0 / v5.1 only, when task 3 failed; v5.1:
                                                                  no vote; v5.2: never, the crisis follows the shift)
    CrisisCalls CrisisVote CrisisVerdict
    [FinalDiscussion FinalDefense (FinalVote [FinalVote] FinalVerdict) x firings] (1..2: at most the candidates, who need suç;
                                                                                  none at all without a candidate)
    Results

v5 bot talk (docs/V5_BOT_TALK.md): each bot logs at Results "bottalk line ... policy=P ... truthful=yes|no",
"bottalk check ... ok=yes|no" (every claim compared with the results) and "bottalk summary ...". The script adds them up
per policy and fails when an Honest bot lied or a claim's truth flag did not match the results. Runs without bots pass.
Votes: each bot also logs every ballot it cast, "bottalk vote seat=S policy=P style=Richest|MostBlame ballot=Meeting|Crisis|Final
round=R revote=.. index=I target=T target_prim=N richest=a,b richest_prim=M hit=yes|no" (the true balances when that vote
started, from the results' prim log). The script reports per policy and ballot how many votes named a truly richest option
(information only: bots estimate hidden balances, a miss is not an error).

v5.1 rules (docs/V5_DESIGN.md §6, §8.2): at Results the host logs "host: rules week=W ruleset=.. whistles=N investigations=N
caught=N shift=staffed|short volunteers=V/K cleaned=C skip_blamed=S skip_blame=B candidates=C firings=F/P transfers=T". The script
reports them and fails when a short shift did not blame its skippers (or a staffed one did), meeting 2 was held for a staffed
shift (or skipped for a short one), or the firings exceed the candidates / the planned number. Silence deals (docs/V5_BOT_TALK.md
§4.1): each bot logs "bottalk hush seat=S policy=P kind=K to=T amount=1 via=phone|text|none punished=yes|no returned=N" per
silence payment it made and "bottalk punish seat=S policy=P kind=whistle|investigation target=T mode=honest|greedy paid=N
returned=N punished=yes|no by_other=yes|no spent=yes|no outcome=none|refused|kept|betrayed" per colleague it could punish. The
script reports offers and outcomes per policy and fails when an Honest bot kept silence money, an Honest-mode bot did not
punish a colleague it could (nobody else did, its one investigation was not spent), or a Greedy-mode bot punished unpaid.

v5.2 (docs/V5_ENGINE_SPEC.md section 22): no meeting 2 (a v5.2 match that holds one fails; a short shift must still have
blamed its skippers). Prim declarations: the host logs "host: declarations week=W enabled=yes|no candidates=C made=M
truthful=T understated=U list=seat:declared/actual,... fired=seat:declared/desk:true|lie|none,..." and each candidate bot
"bottalk declare seat=S policy=P declared=X had=Y actual=Z truthful=yes|no|none" (had = its prim when it declared). The
script reports them per policy and fails when an Honest bot's declaration was not its prim, a bot's line disagrees with
the host's, or more declarations than candidates were made. Results story (LineStory): "host: awards week=W count=N
list=id:seat,..." and "host: headlines week=W count=N list=id:seat,..."; each award owner bot logs "bottalk award seat=S
policy=P key=bot.award.<id>". The script reports the awards and fails on more than 4 awards / 5 headlines, an award
given twice, or a bot saying a line for an award the host did not give it.

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
    # v5.1: meeting 2 has no vote (a v5.0 config may still hold one); v5.2: no meeting 2 at all (checked per ruleset in
    # rules()); firings ≤ candidates (who need suç): 1..firings rounds, or no final at all when nobody qualifies
    return re.compile(r"^I PRB PRB DS(?:VE)? PR (?:DS(?:VE)?)? CKX (?:FG (?:OO?Z){1,%d})? Q$".replace(" ", "") % firings)


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


VOTE = re.compile(r"bottalk vote seat=(\d+) policy=(\w+) style=(\w+) ballot=(\w+) round=(-?\d+) revote=(yes|no) index=(-?\d+) target=(-?\d+) "
                  r"target_prim=(-?\d+) richest=([\d,]*) richest_prim=(-?\d+) hit=(yes|no)")


def votes(logs):
    """Ballots that named a truly richest option (when the vote started), per policy and ballot kind. Information only."""
    per = {}
    styles = set()
    for path in logs:
        text = open(path, encoding="utf-8", errors="replace").read()
        seen = set()
        for m in VOTE.finditer(text):
            key = m.group(0)
            if key in seen:
                continue
            seen.add(key)
            policy, style, ballot, hit = m.group(2), m.group(3), m.group(4), m.group(12) == "yes"
            styles.add(style)
            row = per.setdefault(policy, {})
            cell = row.setdefault(ballot, [0, 0])
            cell[1] += 1
            if hit:
                cell[0] += 1
    if not per:
        print("votes: none logged")
        return
    for policy in sorted(per):
        row = per[policy]
        hits = sum(c[0] for c in row.values())
        total = sum(c[1] for c in row.values())
        print("votes %-6s to the truly richest %d/%d: %s" % (policy, hits, total, ", ".join(
            "%s %d/%d" % (b, row[b][0], row[b][1]) for b in ("Meeting", "Crisis", "Final") if b in row)))
    print("votes: style %s" % ", ".join(sorted(styles)))


PRANK_BOT = re.compile(r"bottalk prank seat=(\d+) policy=(\w+) kind=(\w+) target=(-?\d+) session=(-?\d+) phase=(\w+) cost=(\d+) "
                       r"patience=(-?\d+) tutanak=(yes|no) culprit=(-?\d+) sealed=(yes|no) prim_after=(-?\d+)(?: t=([\d.]+))?")
PRANK_SUMMARY = re.compile(r"bottalk pranks seat=(\d+) policy=(\w+) made=(\d+) tried=(\d+) tutanak=(\d+) spent=(\d+) reactions=(\d+) final_prim=(-?\d+)")
PRANK_HOST = re.compile(r"host: prank i=(\d+) kind=(\w+) actor=(-?\d+) target=(-?\d+) session=(-?\d+) phase=(\w+) cost=(\d+) patience=(-?\d+) "
                        r"after=(-?\d+) tutanak=(yes|no) culprit=(-?\d+)")


RULES = re.compile(r"host: rules week=(\d+) ruleset=(\S+) whistles=(\d+) investigations=(\d+) caught=(\d+) shift=(staffed|short) "
                   r"volunteers=(\d+)/(\d+) cleaned=(\d+) skip_blamed=(\d+) skip_blame=(\d+) candidates=(\d+) firings=(\d+)/(\d+) transfers=(\d+)")


def ruleset_at_least(ruleset, minor):
    """ "v5.2-kayit" >= v5.<minor>."""
    m = re.match(r"v5\.(\d+)", ruleset)
    return bool(m) and int(m.group(1)) >= minor


def rules(logs, meeting2):
    """v5.1 / v5.2 rules per match (host line): punishments, the shift and the candidates. False on a broken rule."""
    bad = []
    seen = 0
    for path in logs:
        text = open(path, encoding="utf-8", errors="replace").read()
        for m in RULES.finditer(text):
            seen += 1
            week = int(m.group(1))
            short = m.group(6) == "short"
            skipped, cand, fired, planned = int(m.group(10)), int(m.group(12)), int(m.group(13)), int(m.group(14))
            print("rules match %d (%s): whistles=%s investigations=%s caught=%s shift=%s volunteers=%s/%s cleaned=%s skip_blamed=%s (+%s) "
                  "candidates=%d firings=%d/%d transfers=%s"
                  % (week, m.group(2), m.group(3), m.group(4), m.group(5), m.group(6), m.group(7), m.group(8), m.group(9), skipped,
                     m.group(11), cand, fired, planned, m.group(15)))
            if short and skipped == 0 and int(m.group(7)) < int(m.group(8)) and ruleset_at_least(m.group(2), 1):
                bad.append("match %d: a short shift but no skipper got suç" % week)
            if not short and skipped > 0:
                bad.append("match %d: a staffed shift blamed its skippers" % week)
            if fired > cand or fired > planned:
                bad.append("match %d: %d firings for %d candidates (planned %d)" % (week, fired, cand, planned))
            held = meeting2.get(week)
            if ruleset_at_least(m.group(2), 2):
                # v5.2: one meeting before the final; the short shift's suç lands at its reveal
                if held:
                    bad.append("match %d: meeting 2 held in a %s match" % (week, m.group(2)))
            elif held is not None and held != short:
                bad.append("match %d: meeting 2 %s for a %s shift" % (week, "held" if held else "skipped", m.group(6)))
    if not seen:
        print("rules: none logged")
        return True
    for b in bad[:10]:
        print("   RULES PROBLEM:", b)
    return not bad


HUSH = re.compile(r"bottalk hush seat=(\d+) policy=(\w+) kind=(\w+) to=(\d+) amount=(\d+) via=(\w+) punished=(yes|no) returned=(\d+)")
PUNISH = re.compile(r"bottalk punish seat=(\d+) policy=(\w+) kind=(\w+) target=(\d+) mode=(\w+) paid=(\d+) returned=(\d+) punished=(yes|no) "
                    r"by_other=(yes|no) spent=(yes|no) outcome=(\w+)")


def deals(logs):
    """Silence deals per policy (bot logs at Results). False when a bot broke its punish / deal rule."""
    bad = []
    offers = {}
    outcomes = {}
    punished = {}
    for path in logs:
        name = os.path.basename(path)
        text = open(path, encoding="utf-8", errors="replace").read()
        seen = set()
        for m in HUSH.finditer(text):
            if m.group(0) in seen:
                continue
            seen.add(m.group(0))
            row = offers.setdefault(m.group(2), {"made": 0, "phone": 0, "text": 0, "none": 0, "then_punished": 0})
            row["made"] += 1
            row[m.group(6) if m.group(6) in ("phone", "text") else "none"] += 1
            if m.group(7) == "yes":
                row["then_punished"] += 1
            if m.group(2) == "Honest":
                bad.append("%s: an Honest bot paid for silence: %s" % (name, m.group(0)[:160]))
        for m in PUNISH.finditer(text):
            if m.group(0) in seen:
                continue
            seen.add(m.group(0))
            policy, kind, mode = m.group(2), m.group(3), m.group(5)
            paid, back, did = int(m.group(6)), int(m.group(7)), m.group(8) == "yes"
            other, spent, outcome = m.group(9) == "yes", m.group(10) == "yes", m.group(11)
            row = outcomes.setdefault(policy, {"none": 0, "refused": 0, "kept": 0, "betrayed": 0})
            row[outcome] = row.get(outcome, 0) + 1
            p = punished.setdefault(policy, {"whistle": [0, 0], "investigation": [0, 0]})
            p[kind][1] += 1
            if did:
                p[kind][0] += 1
            if policy == "Honest" and back < paid:
                bad.append("%s: an Honest bot kept silence money: %s" % (name, m.group(0)[:200]))
            if mode == "honest" and not did and not other and not spent:
                bad.append("%s: an Honest-mode bot did not punish: %s" % (name, m.group(0)[:200]))
            if mode == "greedy" and paid == 0 and did:
                bad.append("%s: a Greedy-mode bot punished unpaid: %s" % (name, m.group(0)[:200]))
            if mode == "greedy" and back > 0:
                bad.append("%s: a Greedy-mode bot returned silence money: %s" % (name, m.group(0)[:200]))
    if not offers and not outcomes:
        print("deals: none logged")
        return True
    for policy in sorted(offers):
        r = offers[policy]
        print("deals %-6s silence payments %d (phone %d, text %d, not delivered %d), then punished anyway %d"
              % (policy, r["made"], r["phone"], r["text"], r["none"], r["then_punished"]))
    for policy in sorted(outcomes):
        r = outcomes[policy]
        p = punished[policy]
        print("deals %-6s as victim / witness: punished whistle %d/%d, investigation %d/%d; paid deals refused %d, kept %d, betrayed %d (unpaid %d)"
              % (policy, p["whistle"][0], p["whistle"][1], p["investigation"][0], p["investigation"][1], r["refused"], r["kept"], r["betrayed"], r["none"]))
    for b in bad[:10]:
        print("   DEAL PROBLEM:", b)
    return not bad


DECL_HOST = re.compile(r"host: declarations week=(\d+) enabled=(yes|no) candidates=(\d+) made=(\d+) truthful=(\d+) understated=(\d+) "
                       r"list=(\S+) fired=(\S+)")
DECL_BOT = re.compile(r"bottalk declare seat=(\d+) policy=(\w+) declared=(-?\d+) had=(-?\d+) actual=(-?\d+) truthful=(yes|no|none)")
AWARDS_HOST = re.compile(r"host: awards week=(\d+) count=(\d+) list=(\S+)")
HEADS_HOST = re.compile(r"host: headlines week=(\d+) count=(\d+) list=(\S+)")
AWARD_BOT = re.compile(r"bottalk award seat=(\d+) policy=(\w+) key=bot\.award\.(\w+)")


def pairs(text):
    """ "a:1,b:2" -> [("a", "1"), ("b", "2")] ("-" = empty)."""
    return [] if text == "-" else [tuple(x.split(":")) for x in text.split(",")]


def story(logs):
    """v5.2: prim declarations and the results' awards / headlines. False when a bot or the host broke a rule."""
    bad = []
    host_decl = {}     # seat -> (declared, actual) of the last match in the logs (one match per run directory by default)
    host_awards = {}   # seat -> [award ids]
    per = {}
    seen_host = False
    for path in logs:
        text = open(path, encoding="utf-8", errors="replace").read()
        for m in DECL_HOST.finditer(text):
            seen_host = True
            week, cand, made, true_n, low = int(m.group(1)), int(m.group(3)), int(m.group(4)), int(m.group(5)), int(m.group(6))
            print("declarations match %d: enabled=%s candidates=%d declared=%d (truthful %d, understated %d) %s; fired %s"
                  % (week, m.group(2), cand, made, true_n, low, m.group(7), m.group(8)))
            if made > cand:
                bad.append("match %d: %d declarations for %d candidates" % (week, made, cand))
            if m.group(2) == "no" and made > 0:
                bad.append("match %d: declarations made although they are off" % week)
            for item in pairs(m.group(7)):
                seat, rest = int(item[0]), item[1]
                declared, actual = rest.split("/")
                host_decl[(week, seat)] = (-1 if declared == "-" else int(declared), int(actual))
        for m in AWARDS_HOST.finditer(text):
            week, count, items = int(m.group(1)), int(m.group(2)), pairs(m.group(3))
            print("awards match %d: %s" % (week, ", ".join("%s -> seat %s" % (a[0], a[1]) for a in items) or "none"))
            kinds = [a[0] for a in items]
            if count != len(items) or count > 4:
                bad.append("match %d: %d awards (at most 4)" % (week, count))
            if len(set(kinds)) != len(kinds):
                bad.append("match %d: an award was given twice: %s" % (week, m.group(3)))
            for a in items:
                host_awards.setdefault((week, int(a[1])), []).append(a[0])
        for m in HEADS_HOST.finditer(text):
            week, count, items = int(m.group(1)), int(m.group(2)), pairs(m.group(3))
            print("headlines match %d: %s" % (week, ", ".join(h[0] for h in items) or "none"))
            if count != len(items) or count > 5:
                bad.append("match %d: %d headlines (at most 5)" % (week, count))
    weeks = sorted({k[0] for k in host_decl} | {k[0] for k in host_awards})
    for path in logs:
        name = os.path.basename(path)
        text = open(path, encoding="utf-8", errors="replace").read()
        seen = set()
        for m in DECL_BOT.finditer(text):
            if m.group(0) in seen:
                continue
            seen.add(m.group(0))
            seat, policy, declared, had, actual, truthful = int(m.group(1)), m.group(2), int(m.group(3)), int(m.group(4)), int(m.group(5)), m.group(6)
            row = per.setdefault(policy, {"candidates": 0, "declared": 0, "truthful": 0, "understated": 0, "none": 0, "awards": 0})
            row["candidates"] += 1
            if truthful == "none":
                row["none"] += 1
            else:
                row["declared"] += 1
                if truthful == "yes":
                    row["truthful"] += 1
                elif declared < had:
                    row["understated"] += 1
            if policy == "Honest" and truthful == "no":
                bad.append("%s: an Honest bot declared %d with %d prim: %s" % (name, declared, had, m.group(0)[:160]))
            # the host's list of the same match (runs hold one match per log unless -Matches was given: any week matches)
            if weeks and not any(host_decl.get((w, seat)) == (declared, actual) for w in weeks):
                bad.append("%s: the host lists another declaration for seat %d: %s" % (name, seat, m.group(0)[:160]))
        for m in AWARD_BOT.finditer(text):
            if m.group(0) in seen:
                continue
            seen.add(m.group(0))
            seat, policy, award = int(m.group(1)), m.group(2), m.group(3)
            per.setdefault(policy, {"candidates": 0, "declared": 0, "truthful": 0, "understated": 0, "none": 0, "awards": 0})["awards"] += 1
            if weeks and not any(award in host_awards.get((w, seat), []) for w in weeks):
                bad.append("%s: a bot said the line of an award the host did not give it: %s" % (name, m.group(0)[:160]))
    if not seen_host and not per:
        print("declarations / awards: none logged")
        return True
    for policy in sorted(per):
        r = per[policy]
        print("declarations %-6s candidates=%d declared=%d (truthful %d, understated %d, none %d); award lines said %d"
              % (policy, r["candidates"], r["declared"], r["truthful"], r["understated"], r["none"], r["awards"]))
    for b in bad[:10]:
        print("   STORY PROBLEM:", b)
    return not bad


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
            key = m.group(0)  # includes t=, so two cakes at the same colleague both count
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
    meeting2 = {}  # week -> meeting 2 held (from any process's phase log)
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
            meeting2[week] = seq.count("MeetingDossier") >= 2
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
    votes(logs)
    if not rules(logs, meeting2):
        ok = False
    if not deals(logs):
        ok = False
    if not story(logs):
        ok = False
    if not pranks(logs):
        ok = False
    print("PASS" if ok and results else "FAIL")
    return 0 if ok and results else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1]))
