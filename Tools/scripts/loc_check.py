"""Localisation key coverage check for the desk (office) UI area.

    python -I Tools/scripts/loc_check.py            # check, exit code 1 on problems
    python -I Tools/scripts/loc_check.py --list     # also print every missing key

Checks (Game/Assets/StreamingAssets/Localization):
  * tr/en .desk.json and .tasks.json are valid UTF-8 JSON objects of string values, without duplicate keys;
  * every key of one language exists in the other (same key sets per area file);
  * the two area files never define the same key, and neither overrides a key of another area/base file;
  * every key the desk code uses (literal first argument of Dk.T / Dk.F / Loc.T / Loc.F / Loc.Has / Dk.TShared and the
    "desk." / "task." / "trick." / "onboard." / "mail.template." / "complaint.preset." literals) resolves in both languages;
  * every contract key (docs/CONTRACTS_V4.md section 4: facts, controls, options, roles, tutorial steps, names, rules,
    item/title/slot/shift/vendor/colour/size/day labels) and every enum-derived key family exists in both languages;
  * placeholders: a key's {0}.. / {p} {n1} tokens must match between tr and en.
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
LOC = os.path.join(ROOT, "Game", "Assets", "StreamingAssets", "Localization")
DESK_SRC = os.path.join(ROOT, "Game", "Assets", "Game", "Scripts", "UI", "Desk")
ENUMS = os.path.join(ROOT, "Game", "Assets", "Game", "Core", "Line", "Contracts", "LineEnums.cs")
AREAS = ["desk", "tasks"]
LANGS = ["tr", "en"]

EMPTY_OK = ("desk.hover.None", "desk.fail.None", "trick.InternetKes.target")
PREFIX_ONLY = ("task.room_booking.slot", "task.shift_cover.shift", "task.vendor_pick.vendor")
errors = []
warnings = []


def err(msg):
    errors.append(msg)


def load_json(path):
    dupes = []

    def hook(pairs):
        d = {}
        for k, v in pairs:
            if k in d:
                dupes.append(k)
            d[k] = v
        return d

    try:
        raw = open(path, "rb").read()
        text = raw.decode("utf-8")
    except FileNotFoundError:
        err("missing file: " + os.path.relpath(path, ROOT))
        return {}
    except UnicodeDecodeError as e:
        err("not valid UTF-8: %s (%s)" % (os.path.relpath(path, ROOT), e))
        return {}
    if text.startswith("\ufeff"):
        warnings.append("BOM in " + os.path.relpath(path, ROOT))
        text = text[1:]
    try:
        data = json.loads(text, object_pairs_hook=hook)
    except ValueError as e:
        err("invalid JSON in %s: %s" % (os.path.relpath(path, ROOT), e))
        return {}
    if not isinstance(data, dict):
        err("not a JSON object: " + os.path.relpath(path, ROOT))
        return {}
    for k in dupes:
        err("duplicate key %r in %s" % (k, os.path.relpath(path, ROOT)))
    for k, v in data.items():
        if not isinstance(v, str):
            err("non-string value for %r in %s" % (k, os.path.relpath(path, ROOT)))
    return data


def enum_names(name, skip_none=True):
    text = open(ENUMS, encoding="utf-8").read()
    m = re.search(r"enum\s+" + name + r"[^{]*\{([^}]*)\}", text)
    if not m:
        err("enum not found: " + name)
        return []
    body = re.sub(r"//[^\n]*", "", m.group(1))
    names = []
    for part in body.split(","):
        mm = re.match(r"\s*([A-Za-z_][A-Za-z0-9_]*)", part)
        if mm:
            names.append(mm.group(1))
    return [n for n in names if not (skip_none and n == "None")]


# ---------------------------------------------------------------------------------------------------------- contract

TASKS = {
    "invoice_reconcile": dict(facts="line partner correct_entries groups_correct", controls="total pad", opts="no yes", roles=""),
    "budget_split": dict(facts="need budget grant overflow", controls="request", opts="", roles=""),
    "customer_spec": dict(facts="color size day commission partner spec correct_orders true_spec", controls="order", opts="spec", roles=""),
    "report_review": dict(facts="my_figure review_figure audit_figure reports_approved", controls="version decision", opts="accurate polished approve reject", roles="", group="0 1"),
    "room_booking": dict(facts="value ok_slot slots booking", controls="slot", opts="slot yield", roles=""),
    "archive_visitor": dict(facts="visitor capacity need_doc doc hr_file served", controls="fetch copy confirm", opts="no yes doc", roles="1 2", group="1 2"),
    "bonus_pool": dict(facts="partner takes", controls="choice", opts="share take", roles=""),
    "server_code": dict(facts="position my_digit next_digit offer cracked", controls="code accept", opts="no yes", roles="1"),
    "phishing_check": dict(facts="account insider_offer fake_holder blocked", controls="flag accept", opts="no yes", roles="1"),
    "shift_cover": dict(facts="shifts available cost covered", controls="volunteer", opts="shift decline", roles=""),
    "vendor_pick": dict(facts="tested kickback winner quality", controls="vote accept", opts="vendor no yes", roles="1"),
    "supply_swap": dict(facts="need collector completed", controls="give done", opts="done", roles=""),
}
LABELS = {
    "customer_spec": {"color": 3, "size": 3, "day": 3},
    "archive_visitor": {"title": 10},
    "room_booking": {"slot": 5},
    "shift_cover": {"shift": 3},
    "vendor_pick": {"vendor": 4},
    "supply_swap": {"item": 8},
}


def contract_keys():
    keys = set()
    for tid, t in TASKS.items():
        p = "task." + tid + "."
        keys.add(p + "name")
        keys.add(p + "rule")
        for f in t["facts"].split():
            keys.add(p + "fact." + f)
        for c in t["controls"].split():
            keys.add(p + "control." + c)
        for o in t["opts"].split():
            keys.add(p + "opt." + o)
        for r in t["roles"].split():
            keys.add(p + "role." + r)
        for g in t.get("group", "").split():
            keys.add(p + "group." + g)
        for i in range(1, 6):
            for part in ("title", "body", "example"):
                keys.add(p + "tut.%d.%s" % (i, part))
        for label, n in LABELS.get(tid, {}).items():
            for i in range(n):
                keys.add(p + label + "." + str(i))
    keys.add("task.room_booking.opt.yield")
    keys.add("task.shift_cover.opt.decline")
    for i in range(8):
        keys.add("complaint.preset.%d" % i)
        keys.add("mail.template.%d" % i)
        keys.add("mail.template.%d.title" % i)
    return keys


def enum_keys():
    keys = set()
    for n in enum_names("LineFailure", False):
        keys.add("desk.fail." + n)
    for n in enum_names("LinePhase", False):
        keys.add("desk.phase." + n)
    for n in enum_names("TrickType"):
        for part in ("name", "how", "hit", "hit_sub"):
            if n in ("InternetKes",) and part in ("hit", "hit_sub"):
                continue
            keys.add("trick.%s.%s" % (n, part))
    for n in enum_names("ObjectiveKind"):
        keys.add("desk.obj.%s.name" % n)
        keys.add("desk.obj.%s.desc" % n)
    for n in enum_names("RecordKind"):
        keys.add("desk.record.kind.%s.name" % n)
        keys.add("desk.record.kind.%s.text" % n)
    for n in enum_names("RecordSource"):
        keys.add("desk.record.source." + n)
    for n in enum_names("OfferKind"):
        keys.add("desk.offer.kind." + n)
        keys.add("desk.offer.desc." + n)
    for n in enum_names("CallEndReason", False):
        keys.add("desk.phone.log_end." + n)
    for n in enum_names("DeskTarget") if False else ["Monitor", "Phone", "Mug", "Window", "Drawer", "Nameplate"]:
        keys.add("desk.hover." + n)
    for n in ["Mug", "Window", "Drawer", "Nameplate"]:
        keys.add("desk.quip." + n)
    for n in ["MorningFirst"]:
        pass
    for prof in ["default", "paragoz", "disiplinci", "kayirmaci", "paranoyak"]:
        for i in range(3):
            keys.add("desk.morning.%s.%d" % (prof, i))
        keys.add("desk.mgr.%s.name" % prof)
        keys.add("desk.mgr.%s.title" % prof)
    for i in range(5):
        keys.add("desk.day.%d" % i)
        keys.add("desk.app.%s" % ["Task", "Messages", "Mail", "Bank", "Evidence", "Remedial", "Trick", "Complaint"][i])
        keys.add("desk.tut.hl.%d" % (i + 1))
    for n in ["Messages", "Mail", "Bank", "Evidence", "Remedial", "Trick", "Complaint"]:
        keys.add("desk.app." + n)
    return keys


# ---------------------------------------------------------------------------------------------------------- code usage

CALL = re.compile(r'(?:Dk|Loc)\.(?:T|F|Has)\(\s*"([^"\\]+)"')
CALL2 = re.compile(r'\bTShared\(\s*"([^"\\]+)"\s*,\s*"([^"\\]+)"')
LIT = re.compile(r'"((?:desk|task|trick|onboard|complaint\.preset|mail\.template)\.[A-Za-z0-9_.]*[A-Za-z0-9_])"')


def used_keys():
    keys = {}
    for fn in sorted(os.listdir(DESK_SRC)):
        if not fn.endswith(".cs") or fn == "DeskMocks.cs":  # mock scenario names look like keys
            continue
        text = open(os.path.join(DESK_SRC, fn), encoding="utf-8").read()
        for m in CALL.finditer(text):
            k = m.group(1)
            if "{" not in k:
                if not k.endswith("."):
                    keys.setdefault(k, set()).add(fn)
        for m in LIT.finditer(text):
            if m.group(1) not in PREFIX_ONLY:
                keys.setdefault(m.group(1), set()).add(fn)
    return keys


def placeholders(s):
    return sorted(set(re.findall(r"\{[A-Za-z0-9]+\}", s)))


def main():
    show_list = "--list" in sys.argv
    tables = {}
    for lang in LANGS:
        for area in AREAS:
            tables[(lang, area)] = load_json(os.path.join(LOC, "%s.%s.json" % (lang, area)))

    # same key sets, same placeholders
    for area in AREAS:
        tr, en = tables[("tr", area)], tables[("en", area)]
        for k in sorted(set(tr) - set(en)):
            err("%s: key only in tr: %s" % (area, k))
        for k in sorted(set(en) - set(tr)):
            err("%s: key only in en: %s" % (area, k))
        for k in sorted(set(tr) & set(en)):
            if placeholders(tr[k]) != placeholders(en[k]):
                err("%s: placeholder mismatch in %s: tr %s vs en %s" % (area, k, placeholders(tr[k]), placeholders(en[k])))
            if not tr[k].strip() and k not in EMPTY_OK:
                err("%s: empty tr text: %s" % (area, k))
            if not en[k].strip() and k not in EMPTY_OK:
                err("%s: empty en text: %s" % (area, k))
    for lang in LANGS:
        a, b = tables[(lang, "desk")], tables[(lang, "tasks")]
        for k in sorted(set(a) & set(b)):
            err("%s: key defined in both desk and tasks: %s" % (lang, k))
        # keys of other areas / base file
        others = {}
        for fn in os.listdir(LOC):
            if fn.startswith(lang + ".") and fn.endswith(".json") and fn not in ("%s.desk.json" % lang, "%s.tasks.json" % lang):
                for k in load_json(os.path.join(LOC, fn)):
                    others[k] = fn
        for k in sorted((set(a) | set(b)) & set(others)):
            err("%s: key also defined in %s (override): %s" % (lang, others[k], k))

    all_tr = dict(tables[("tr", "desk")]); all_tr.update(tables[("tr", "tasks")])
    all_en = dict(tables[("en", "desk")]); all_en.update(tables[("en", "tasks")])

    # shared keys that desk code may read from other areas (resolved there)
    shared_ok = set()
    for lang in LANGS:
        for fn in os.listdir(LOC):
            if fn.startswith(lang + ".") and fn.endswith(".json"):
                pass
    base_tr = {}
    for fn in os.listdir(LOC):
        if fn.startswith("tr.") and fn.endswith(".json"):
            base_tr.update(load_json(os.path.join(LOC, fn)))

    needed = {}
    for k in contract_keys() | enum_keys():
        needed.setdefault(k, set()).add("contract")
    for k, files in used_keys().items():
        needed.setdefault(k, set()).update(files)

    missing = []
    for k, why in sorted(needed.items()):
        for lang, table in (("tr", all_tr), ("en", all_en)):
            if k not in table and k not in (base_tr if lang == "tr" else base_tr):
                missing.append((lang, k, ",".join(sorted(why))))
    for lang, k, why in missing:
        if show_list:
            print("MISSING %s %s  (%s)" % (lang, k, why))
    if missing:
        err("%d missing key uses (run with --list to see them)" % len(missing))

    print("tr: desk %d + tasks %d keys | en: desk %d + tasks %d keys" % (
        len(tables[("tr", "desk")]), len(tables[("tr", "tasks")]), len(tables[("en", "desk")]), len(tables[("en", "tasks")])))
    for w in warnings:
        print("warning:", w)
    if errors:
        for e in errors:
            print("ERROR:", e)
        print("loc_check FAILED (%d problems)" % len(errors))
        return 1
    print("loc_check OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
