"""Localisation check for the v5 play UI (UI/Play: PlayUi, PlayText, PlayBlocks, TapeDeck; UI/Office: the office layer).

    python -I Tools/scripts/loc_check.py            # check, exit code 1 on problems

Checks (Game/Assets/StreamingAssets/Localization):
  * tr.v5.json / en.v5.json are JSON objects of strings with identical key sets and no empty values;
  * every {0}.. placeholder of a key appears in both languages;
  * every key literal used by UI/Play and UI/Office (PlayText.T/F prefix "v5.", Loc.T/F literals) resolves in both languages
    (merged like Loc.Load: <lang>.json + every <lang>.<area>.json);
  * enum-derived families exist for every enum value: phase./hint. (LinePhase), fail. (LineFailure), verb. (LineVerb),
    decision./fire.decision. (FiringDecision), blame. (BlameReason), phone.log_missed. (CallEndReason),
    task.<id>.name/choice.0/choice.1, vote.todo.<vote phase>.
"""
import glob
import json
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
LOC = os.path.join(ROOT, "Game", "Assets", "StreamingAssets", "Localization")
PLAY = [os.path.join(ROOT, "Game", "Assets", "Game", "Scripts", "UI", d) for d in ("Play", "Office")]
ENUMS = os.path.join(ROOT, "Game", "Assets", "Game", "Core", "Line", "Contracts", "LineEnums.cs")
EMPTY_OK = {"v5.hint.None"}
TASK_IDS = ["client_bonus", "petty_cash", "weekend_shift"]
# office keys built from an enum / id: office.use.<DeskTarget>, office.app.<app id>, office.task_note.<task id>
OFFICE_FAMILIES = {"office.use.": ["Monitor", "Phone", "Board", "Chair", "Mug", "Drawer", "Nameplate"],
                   "office.app.": ["task", "transfer", "messages", "calls", "rules"],
                   "office.task_note.": ["petty_cash", "weekend_shift", "whistle"]}
FAMILIES = {"phase.": "LinePhase", "hint.": "LinePhase", "fail.": "LineFailure", "verb.": "LineVerb",
            "decision.": "FiringDecision", "fire.decision.": "FiringDecision", "blame.": "BlameReason",
            "phone.log_missed.": "CallEndReason"}
# Loc keys of the shared tables (not "v5.") that the play UI uses on purpose
SHARED = {"common.nobody", "line.system.name"}


def load(path):
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    if not isinstance(data, dict) or not all(isinstance(v, str) for v in data.values()):
        raise ValueError(path + ": not an object of strings")
    return data


def merged(lang):
    table = load(os.path.join(LOC, lang + ".json"))
    for p in sorted(glob.glob(os.path.join(LOC, lang + ".*.json"))):
        table.update(load(p))
    return table


def enum_values(name):
    src = open(ENUMS, encoding="utf-8").read()
    m = re.search(r"enum %s\s*:\s*\w+\s*\{(.*?)\}" % name, src, re.S)
    body = re.sub(r"///.*", "", m.group(1))
    return re.findall(r"^\s*(\w+)\s*=", body, re.M)


def main():
    problems = []
    tr, en = load(os.path.join(LOC, "tr.v5.json")), load(os.path.join(LOC, "en.v5.json"))
    for a, b, la, lb in ((tr, en, "tr", "en"), (en, tr, "en", "tr")):
        for k in sorted(set(a) - set(b)):
            problems.append("only in %s.v5.json: %s" % (la, k))
    for k in sorted(set(tr) & set(en)):
        if (not tr[k] or not en[k]) and k not in EMPTY_OK:
            problems.append("empty value: " + k)
        if set(re.findall(r"\{\d+\}", tr[k])) != set(re.findall(r"\{\d+\}", en[k])):
            problems.append("placeholders differ: " + k)
    mt, me = merged("tr"), merged("en")
    used = set()
    for f in [g for d in PLAY for g in glob.glob(os.path.join(d, "*.cs"))]:
        s = open(f, encoding="utf-8").read()
        for m in re.finditer(r'(Loc\.)?\b(?:T|F|TOr|Has)\(\s*"([a-z][a-z0-9_.]*)"', s):
            key = m.group(2)
            if key.endswith("."):
                continue
            used.add(key if m.group(1) else "v5." + key)
        # keys chosen by a ternary inside PlayText.T/F calls: T(cond ? "a.b" : "a.c")
        for m in re.finditer(r'\b(?:T|F)\([^;]*?\?\s*"([a-z][a-z0-9_.]*)"\s*:\s*"([a-z][a-z0-9_.]*)"', s):
            used.update("v5." + g for g in m.groups())
    for k in sorted(used):
        if k in SHARED or k.startswith("v5.") or k.startswith("rec."):
            for lang, table in (("tr", mt), ("en", me)):
                if k not in table:
                    problems.append("missing key (%s): %s" % (lang, k))
    for prefix, enum in FAMILIES.items():
        for v in enum_values(enum):
            k = "v5." + prefix + v
            if k not in tr or k not in en:
                problems.append("missing enum key: " + k)
    for ph in ("MeetingVote", "CrisisVote", "FinalVote"):
        k = "v5.vote.todo." + ph
        if k not in tr or k not in en:
            problems.append("missing vote key: " + k)
    for prefix, ids in OFFICE_FAMILIES.items():
        for i in ids:
            k = "v5." + prefix + i
            if k not in tr or k not in en:
                problems.append("missing office key: " + k)
    for t in TASK_IDS:
        for suffix in ("name", "choice.0", "choice.1"):
            k = "v5.task.%s.%s" % (t, suffix)
            if k not in tr or k not in en:
                problems.append("missing task key: " + k)
    for p in problems:
        print("FAIL", p)
    print("loc_check: %d keys per language, %d used literals, %s" % (len(tr), len(used), "OK" if not problems else "%d problem(s)" % len(problems)))
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
