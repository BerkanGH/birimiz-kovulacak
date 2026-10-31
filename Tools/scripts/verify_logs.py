"""Checks a multi-process run directory: every process completed the same matches, received identical
result hashes, saw the full phase sequence, and logged no exceptions. Exit code 0 = pass."""
import glob
import os
import re
import sys

PHASES = ["Briefing", "Work1", "Raid", "Work2", "FinalInspection", "MeetingDiscussion", "MeetingDefense1",
          "MeetingDefense2", "MeetingVote", "MeetingVerdict", "Results"]
# v3 weeks ("phase X -> Y day=D round=R week=W", "results week=W hash=H fired=F manager=M days=N")
PHASES_V3 = ["Morning", "RoundIntro", "RoundPlay", "RoundReveal", "MeetingDossier", "MeetingDiscussion", "MeetingVote",
             "MeetingVerdict", "FinalDefense", "FinalVote", "FinalVerdict", "WeekResults"]
# v4 matches add Break; a closed company skips FinalDefense/FinalVote (post-mortem), and a one-block match has no daily vote.
PHASES_V4 = ["Morning", "RoundIntro", "RoundPlay", "RoundReveal", "Break", "MeetingDossier", "MeetingDiscussion",
             "FinalVerdict", "WeekResults"]
PHASES_V4_OPEN = ["FinalDefense", "FinalVote"]

run = sys.argv[1]
logs = sorted(f for f in glob.glob(os.path.join(run, "*.log")) if not os.path.basename(f).startswith("unity_"))
if not logs:
    print("FAIL: no game logs in", run)
    sys.exit(1)
ok = True
results = {}   # match -> {log: hash}
for path in logs:
    name = os.path.basename(path)
    text = open(path, encoding="utf-8", errors="replace").read()
    phases = {}
    for m in re.finditer(r"phase (\w+) -> (\w+) match=(\d+)", text):
        phases.setdefault(int(m.group(3)), []).append(m.group(2))
    for m in re.finditer(r"results match=(\d+) hash=(\w+) fired=(-?\d+) company=(\w+) quota=([\d.]+)/([\d.]+) informant=(-?\d+) informantWon=(\w+) incidents=(\d+) winners=([\d,]*)", text):
        results.setdefault(int(m.group(1)), {})[name] = m.groups()[1:]
    v3 = False
    for m in re.finditer(r"phase (\w+) -> (\w+) day=\d+ round=-?\d+ week=(\d+)", text):
        v3 = True
        phases.setdefault(int(m.group(3)), []).append(m.group(2))
    closed = {}
    for m in re.finditer(r"(?<!host: )results week=(\d+) hash=(\w+) fired=(-?\d+) manager=(\w*) days=(\d+)(?: closed=(\w+) productivity=(-?\d+))?", text):
        g = m.groups()
        results.setdefault(int(g[0]), {})[name] = (g[1], g[2], g[5] or "-", g[6] or "-", "-", g[3], "-", "-", g[4])
        closed[int(g[0])] = g[5] == "True"
    v4 = "-> Break " in text
    errors = re.findall(r"unity-(?:Exception|Error) .*", text)
    print("%-22s matches=%s errors=%d" % (name, sorted(phases), len(errors)))
    for match, seq in sorted(phases.items()):
        expected = (PHASES_V4 + ([] if closed.get(match) else PHASES_V4_OPEN)) if v4 else PHASES_V3 if v3 else PHASES
        missing = [p for p in expected if p not in seq]
        if missing:
            ok = False
            print("   match %d missing phases: %s" % (match, missing))
    for e in errors[:5]:
        print("   ", e[:200])
    if errors:
        ok = False
for match, per_log in sorted(results.items()):
    hashes = {v[0] for v in per_log.values()}
    sample = next(iter(per_log.values()))
    print("match %d: %d processes reported results, hashes=%s fired=%s company=%s quota=%s/%s informant=%s informantWon=%s incidents=%s winners=%s"
          % (match, len(per_log), sorted(hashes), sample[1], sample[2], sample[3], sample[4], sample[5], sample[6], sample[7], sample[8]))
    if len(hashes) != 1 or len(per_log) != len(logs):
        ok = False
        print("   MISMATCH: not every process received the same result")
print("PASS" if ok and results else "FAIL")
sys.exit(0 if ok and results else 1)
