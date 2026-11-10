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
    print("PASS" if ok and results else "FAIL")
    return 0 if ok and results else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1]))
