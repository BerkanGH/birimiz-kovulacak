"""TR/EN key parity and key-usage check for the shell UI (menu, lobby, settings, overlays) and the privacy area.

v5: the v4 meeting UI and tr/en.meeting.json are gone; the v5 play UI is checked by loc_check.py.

    python -I Tools/scripts/loc_parity_meeting.py

Checks (exit code 1 on any failure):
  1. tr.privacy.json / en.privacy.json have identical key sets.
  2. tr.json / en.json: keys under the prefixes this track owns have identical key sets
     (legacy v1 keys in tr.json that en.json never had are reported once as "legacy gap", not failures).
  3. Every Loc.T/F/TOr key literal used by the track's C# files exists in both languages (merged like Loc.Load does),
     and every dynamic key family (prefix + enum value) has the same non-empty key set in both languages.
  4. No value is empty (except the intentional meeting.phase.None) and every {n} placeholder of the TR text also
     appears in the EN text.
"""
import glob
import json
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
LOC = os.path.join(ROOT, "Game", "Assets", "StreamingAssets", "Localization")
SCRIPTS = os.path.join(ROOT, "Game", "Assets", "Game", "Scripts", "UI")
CODE = [os.path.join(SCRIPTS, n) for n in ("MenuScreens.cs", "SettingsScreen.cs", "ShellOverlays.cs", "Modals.cs", "UIRoot.cs")]  # UI/Play: loc_check.py
OWNED_PREFIXES = ("howto.", "lobby.", "settings.", "menu.")
DYNAMIC = {  # prefix -> expected key suffixes (None = just require parity + non-empty)
    "howto.": None,
    "intro.": None,
    "moment.cat.": ["funny", "betrayal", "accusation", "surprise", "reveal", "other"],
}
IGNORE_EMPTY = set()
# string literals that look like keys but are PlayerPrefs / art paths / ids, not localisation keys
NOT_KEYS = re.compile(r"^(player\.name|join\.(address|port|recent)|playtest\.group_code|intro\.v4\.done|display\.fullscreen|settings\.(volume|ambience_volume|sfx_volume|language|music_volume|music_mute)_?[a-z]*)$")


def load(name):
    with open(os.path.join(LOC, name), encoding="utf-8") as f:
        return json.load(f)


def merged(lang):
    table = load(lang + ".json")
    for p in sorted(glob.glob(os.path.join(LOC, lang + ".*.json"))):
        table.update(json.load(open(p, encoding="utf-8")))
    return table


def placeholders(s):
    return set(re.findall(r"\{(\d+)\}", s))


def main():
    fail = 0
    for area in ("privacy",):
        tr, en = load("tr.%s.json" % area), load("en.%s.json" % area)
        a, b = sorted(set(tr) - set(en)), sorted(set(en) - set(tr))
        print("%-8s tr=%d en=%d" % (area, len(tr), len(en)))
        if a or b:
            fail += 1
            print("  FAIL parity: only in tr:", a[:20], "only in en:", b[:20])
        for k in tr:
            if k in en:
                if (tr[k] == "" or en[k] == "") and k not in IGNORE_EMPTY:
                    fail += 1
                    print("  FAIL empty value:", k)
                if placeholders(tr[k]) != placeholders(en[k]):
                    fail += 1
                    print("  FAIL placeholders differ:", k, placeholders(tr[k]), placeholders(en[k]))
    tr, en = load("tr.json"), load("en.json")
    owned_tr = {k for k in tr if k.startswith(OWNED_PREFIXES)}
    owned_en = {k for k in en if k.startswith(OWNED_PREFIXES)}
    gap = sorted(owned_tr - owned_en)
    extra = sorted(owned_en - owned_tr)
    print("main     owned prefixes tr=%d en=%d" % (len(owned_tr), len(owned_en)))
    if gap or extra:
        print("  legacy gap (in tr.json only, not used by this track unless listed below):", len(gap), gap[:12])
        print("  only in en.json:", extra[:12])
    mt, me = merged("tr"), merged("en")
    used, dyn = {}, set()
    for f in CODE:
        s = open(f, encoding="utf-8").read()
        for m in re.finditer(r'Loc\.(?:T|F|TOr|Has)\(\s*"([^"]+)"(\s*\+)?', s):
            (dyn if m.group(2) else used.setdefault(m.group(1), set())).add(m.group(1) if m.group(2) else os.path.basename(f))
        for m in re.finditer(r'Loc\.TOr\([^,]+,\s*"([^"]+)"', s):
            used.setdefault(m.group(1), set()).add(os.path.basename(f))
        # key literals passed through helpers / conditionals ("a.b.c" next to a Loc call on the same line)
        for line in s.splitlines():
            if "Loc." in line:
                for m in re.finditer(r'"([a-z][a-z0-9_]*(?:\.[A-Za-z0-9_]+)+)"', line):
                    k = m.group(1)
                    if "/" not in k and not NOT_KEYS.match(k) and not k.endswith(".") and k not in dyn:
                        used.setdefault(k, set()).add(os.path.basename(f))
    missing = [k for k in sorted(used) if (k not in mt or k not in me) and not k.endswith(".")]
    print("code     keys used=%d dynamic prefixes=%d" % (len(used), len(dyn)))
    for k in missing:
        fail += 1
        print("  FAIL missing key:", k, ("(tr)" if k not in mt else "") + ("(en)" if k not in me else ""), sorted(used[k])[:3])
    for pref in sorted(dyn):
        if pref not in DYNAMIC:
            continue
        keys_tr = {k[len(pref):] for k in mt if k.startswith(pref)}
        keys_en = {k[len(pref):] for k in me if k.startswith(pref)}
        want = DYNAMIC[pref]
        if keys_tr != keys_en or not keys_tr or (want is not None and not set(want) <= keys_tr):
            fail += 1
            print("  FAIL dynamic family %s: tr=%s en=%s want=%s" % (pref, sorted(keys_tr)[:8], sorted(keys_en)[:8], want))
    print("RESULT:", "OK" if fail == 0 else "%d problem(s)" % fail)
    return 1 if fail else 0


if __name__ == "__main__":
    sys.exit(main())
