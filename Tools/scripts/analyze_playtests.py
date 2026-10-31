#!/usr/bin/env python3
"""Offline playtest analyzer for "Birimiz Kovulacak" (stdlib only, no cloud).

Reads telemetry JSONL (docs/ANALYTICS_ARCHITECTURE.md section 2), optional observer CSV and player feedback CSV and writes
Reports/PLAYTEST_SUMMARY.md, PLAYTEST_METRICS.csv, PLAYTEST_GROUP_COMPARISON.csv, PLAYTEST_FUNNELS.csv and charts/*.svg.

Run with isolated mode:  python -I Tools/scripts/analyze_playtests.py <dirs/files...> [options]

Principles (binding):
 * missing data never becomes zero (status not_measured / insufficient_data);
 * the unit of analysis is the anonymous friend GROUP; confidence intervals resample groups;
 * different build_version / ruleset_version are never pooled (every row carries both);
 * synthetic data (fixtures, bot sims, bot matches) is excluded unless --include-synthetic and then bannered;
 * KPIs are associations and descriptions, never causal claims.
"""
import argparse
import csv
import datetime
import json
import math
import os
import random
import sys
import zlib
from collections import Counter, defaultdict

SCHEMA_VERSION = 1
START_BLAME = 5          # GAMEPLAY_REDESIGN 7 (assumption: default config; flagged in metric notes)
WARNING_CAP = 2          # MaxWarningsPerPlayer
MIN_GROUPS_OK = 2        # fewer groups -> status insufficient_data (value still shown)
BOOT_REPS = 400
BANNER = "SYNTHETIC — not playtest evidence"
EXPLORATORY = ("10–20 friend groups are **exploratory evidence**: they can show where to look and which numbers are "
               "implausible, not that a mechanic works. Intervals resample whole groups; players of one group are not "
               "independent and repeated matches of one group are not new groups.")

REQUIRED_ENVELOPE = (("schema_version", int), ("event", str), ("event_version", int), ("event_id", str),
                     ("authority", str), ("timestamp_utc", str), ("environment", str))
OPTIONAL_TYPES = (("monotonic_elapsed_seconds", (int, float)), ("player_slot", int), ("player_count", int),
                  ("is_bot", bool), ("day_index", int), ("round_index", int), ("params", dict), ("experiments", dict),
                  ("observation_coverage", (int, float)))
SLOT_KEYS = ("player", "caller", "callee", "slot", "buyer", "from", "to", "actor", "victim", "exposed_by", "presenter",
             "defender", "voter", "target", "requester", "cosigner", "recipient", "sender")

# section 5 event names (fallback when no catalog is present)
KNOWN_EVENTS = set("""session_started session_ended session_interrupted lobby_created lobby_join_attempted lobby_joined
lobby_join_failed lobby_left player_ready_changed match_start_attempted match_started match_start_failed match_abandoned
rematch_requested rematch_started week_started day_started phase_entered phase_exited round_started round_finished day_finished
week_finished week_completed phone_call_requested phone_call_connected phone_call_rejected phone_call_missed phone_call_failed
phone_call_finished phone_round_summary secret_info_presented choice_ui_opened choice_submitted choice_changed choice_timed_out
choice_outcome_resolved round_player_summary meeting_started recording_playback_started recording_playback_finished
meeting_discussion_started meeting_discussion_finished evidence_presented meeting_vote_submitted meeting_vote_resolved
warning_assigned meeting_finished fault_points_changed final_started final_candidate_selected defense_started defense_finished
final_vote_submitted final_vote_resolved player_fired task_output_resolved productivity_changed recovery_opportunities
remedial_requested remedial_resolved income_credited money_transferred evidence_purchased incident_created sabotage_verified
objective_assigned objective_completed career_breakdown company_result company_failed message_sent message_rejected
message_thread_opened ui_action_rejected tutorial_step_viewed tutorial_closed onboarding_intro_completed settings_changed
player_disconnected player_reconnected reconnect_attempted reconnect_succeeded reconnect_failed host_departed
voice_quality_summary stuck_phase unexpected_transition technical_failure telemetry_queue_dropped telemetry_sink_error
moment_marked music_cue_changed rematch_prompt_shown player_slot_map""".split())

TRUTHY = {"yes", "y", "true", "1", "ja", "evet", "x"}
FALSY = {"no", "n", "false", "0", "hayir", "hayır"}


# =====================================================================================================================
# statistics
# =====================================================================================================================
def wilson(k, n, z=1.96):
    """Wilson score interval for k successes in n trials (here: n = number of GROUPS)."""
    if n <= 0:
        return (None, None)
    p = k / n
    d = 1 + z * z / n
    c = p + z * z / (2 * n)
    h = z * math.sqrt(p * (1 - p) / n + z * z / (4 * n * n))
    return ((c - h) / d, (c + h) / d)


def percentile(xs, q):
    if not xs:
        return None
    s = sorted(xs)
    if len(s) == 1:
        return s[0]
    pos = (len(s) - 1) * q
    lo = int(math.floor(pos))
    hi = int(math.ceil(pos))
    return s[lo] + (s[hi] - s[lo]) * (pos - lo)


def median(xs):
    return percentile(xs, 0.5)


def mean(xs):
    return sum(xs) / len(xs) if xs else None


def _rng(key):
    return random.Random(zlib.crc32(str(key).encode("utf-8")) ^ 0x5EED)


def boot_ratio_ci(group_pairs, key):
    """Cluster bootstrap of sum(num)/sum(den) over groups. group_pairs: [(num, den)] one per group."""
    if len(group_pairs) < 3:
        return (None, None)
    rng = _rng(key)
    g = len(group_pairs)
    vals = []
    for _ in range(BOOT_REPS):
        n = d = 0.0
        for _i in range(g):
            a, b = group_pairs[rng.randrange(g)]
            n += a
            d += b
        if d > 0:
            vals.append(n / d)
    if len(vals) < BOOT_REPS // 2:
        return (None, None)
    return (percentile(vals, 0.025), percentile(vals, 0.975))


def boot_sample_ci(group_lists, key, stat="mean"):
    """Cluster bootstrap of a statistic of samples pooled across resampled groups."""
    lists = [l for l in group_lists if l]
    if len(lists) < 3:
        return (None, None)
    rng = _rng(key)
    g = len(lists)
    reps = BOOT_REPS if stat == "mean" else BOOT_REPS // 2
    sums = [(sum(l), len(l)) for l in lists]
    vals = []
    for _ in range(reps):
        idx = [rng.randrange(g) for _ in range(g)]
        if stat == "mean":
            s = sum(sums[i][0] for i in idx)
            n = sum(sums[i][1] for i in idx)
            vals.append(s / n)
        else:
            pool = []
            for i in idx:
                pool.extend(lists[i])
            vals.append(percentile(pool, 0.5 if stat == "median" else 0.9))
    return (percentile(vals, 0.025), percentile(vals, 0.975))


def interval_union(intervals):
    """Total length of the union of [start, end] intervals (overlap counted once)."""
    iv = sorted((a, b) for a, b in intervals if b is not None and a is not None and b > a)
    total = 0.0
    cs = ce = None
    for a, b in iv:
        if cs is None:
            cs, ce = a, b
        elif a <= ce:
            ce = max(ce, b)
        else:
            total += ce - cs
            cs, ce = a, b
    if cs is not None:
        total += ce - cs
    return total


def clip_intervals(intervals, lo, hi):
    out = []
    for a, b in intervals:
        if a is None or b is None:
            continue
        a2, b2 = max(a, lo), min(b, hi)
        if b2 > a2:
            out.append((a2, b2))
    return out


def gini(values):
    xs = sorted(values)
    n = len(xs)
    tot = sum(xs)
    if n < 2 or tot <= 0:
        return None
    cum = sum((i + 1) * x for i, x in enumerate(xs))
    return (2 * cum) / (n * tot) - (n + 1) / n


def hhi(weights):
    tot = sum(weights)
    if tot <= 0:
        return None
    return sum((w / tot) ** 2 for w in weights)


def yn(v):
    """'yes'->True, 'no'->False, blank/unknown->None (missing is not False)."""
    s = str(v).strip().lower() if v is not None else ""
    if s in TRUTHY:
        return True
    if s in FALSY:
        return False
    return None


def parse_ts(s):
    try:
        return datetime.datetime.fromisoformat(s.replace("Z", "+00:00")).timestamp()
    except Exception:
        return None


# =====================================================================================================================
# loading
# =====================================================================================================================
class LoadStats:
    def __init__(self):
        self.files = 0
        self.lines = 0
        self.corrupt = 0
        self.truncated = 0
        self.rejected = Counter()
        self.duplicates = 0
        self.accepted = 0
        self.unknown_events = Counter()
        self.param_issues = Counter()
        self.by_authority = Counter()
        self.catalog = None
        self.file_list = []


def discover(paths):
    out = []
    for p in paths:
        if os.path.isfile(p):
            out.append(p)
        elif os.path.isdir(p):
            for root, _d, files in os.walk(p):
                for f in files:
                    if f.endswith(".jsonl"):
                        out.append(os.path.join(root, f))
    return sorted(set(os.path.abspath(x) for x in out))


def load_catalog(path):
    """Tolerant catalog loader: {'events':[{'name','params':{k:type}|[{'name','type'}]}]} or {'events':{name:{...}}}."""
    if not path or not os.path.isfile(path):
        return None
    try:
        with open(path, "r", encoding="utf-8") as f:
            raw = json.load(f)
    except Exception:
        return None
    evs = raw.get("events", raw) if isinstance(raw, dict) else raw
    cat = {}
    if isinstance(evs, dict):
        items = list(evs.items())
    else:
        items = [(e.get("name") or e.get("event"), e) for e in evs if isinstance(e, dict)]
    for name, e in items:
        if not name or not isinstance(e, dict):
            continue
        params = e.get("params", e.get("parameters", {}))
        pm = {}
        if isinstance(params, dict):
            for k, v in params.items():
                pm[k] = v.get("type") if isinstance(v, dict) else v
        elif isinstance(params, list):
            for p in params:
                if isinstance(p, dict) and p.get("name"):
                    pm[p["name"]] = p.get("type")
        cat[name] = {"params": pm, "unsupported": bool(e.get("unsupported"))}
    return cat or None


def _type_ok(t, v):
    t = (str(t) if t is not None else "").lower().split("|")[0].split(" ")[0]
    if t in ("i", "int", "long", "integer", "int32", "int64"):
        return isinstance(v, int) and not isinstance(v, bool)
    if t in ("f", "float", "double", "number", "seconds"):
        return (isinstance(v, (int, float)) and not isinstance(v, bool)) or v == "not_measured"
    if t in ("b", "bool", "boolean"):
        return isinstance(v, bool)
    if t in ("e", "enum", "id", "string", "str"):
        return isinstance(v, str)
    if t == "slot":
        return isinstance(v, (int, str)) and not isinstance(v, bool)
    return True


def validate_envelope(ev):
    if not isinstance(ev, dict):
        return "not_object"
    for k, t in REQUIRED_ENVELOPE:
        if k not in ev:
            return "missing_" + k
        if isinstance(ev[k], bool) or not isinstance(ev[k], t):
            return "type_" + k
    if ev["schema_version"] != SCHEMA_VERSION:
        return "unsupported_schema_version"
    if ev["authority"] not in ("host", "client"):
        return "bad_authority"
    if parse_ts(ev["timestamp_utc"]) is None:
        return "bad_timestamp"
    for k, t in OPTIONAL_TYPES:
        if k in ev and ev[k] is not None:
            if t is not bool and isinstance(ev[k], bool):
                return "type_" + k
            if not isinstance(ev[k], t):
                return "type_" + k
    return None


def read_jsonl(path, st):
    with open(path, "rb") as f:
        data = f.read()
    if data.startswith(b"\xef\xbb\xbf"):
        data = data[3:]
    ends_nl = data.endswith(b"\n") or data == b""
    lines = data.split(b"\n")
    for i, raw in enumerate(lines):
        raw = raw.strip()
        if not raw:
            continue
        st.lines += 1
        try:
            obj = json.loads(raw.decode("utf-8", errors="replace"))
            if not isinstance(obj, dict):
                raise ValueError("not an object")
        except Exception:
            if i == len(lines) - 1 and not ends_nl:
                st.truncated += 1
            else:
                st.corrupt += 1
            continue
        yield obj


def load_events(paths, catalog=None):
    st = LoadStats()
    st.catalog = catalog
    seen = set()
    events = []
    for path in discover(paths):
        st.files += 1
        st.file_list.append(path)
        for ev in read_jsonl(path, st):
            why = validate_envelope(ev)
            if why:
                st.rejected[why] += 1
                continue
            eid = ev["event_id"]
            if eid in seen:
                st.duplicates += 1
                continue
            seen.add(eid)
            name = ev["event"]
            known = (name in catalog) if catalog else (name in KNOWN_EVENTS)
            if not known:
                st.unknown_events[name] += 1
            elif catalog:
                for k, v in (ev.get("params") or {}).items():
                    t = catalog[name]["params"].get(k, "__unknown__")
                    if t == "__unknown__":
                        st.param_issues["unknown_param:%s.%s" % (name, k)] += 1
                    elif not _type_ok(t, v):
                        st.param_issues["bad_type:%s.%s" % (name, k)] += 1
            ev["_ts"] = parse_ts(ev["timestamp_utc"])
            st.accepted += 1
            st.by_authority[ev["authority"]] += 1
            events.append(ev)
    events.sort(key=lambda e: (e["_ts"], e.get("monotonic_elapsed_seconds") or 0.0))
    return events, st


# =====================================================================================================================
# purge (participant deletion support)
# =====================================================================================================================
def _is_slot(v):
    return isinstance(v, int) and not isinstance(v, bool)


def purge_player(paths, pid, out_dir):
    """Rewrite files into out_dir without events of `pid`: events whose subject is the player, plus host events of the
    same match that name the player's slot in a slot-typed param (calls, votes, transfers ...). Aggregate (H) events cannot
    be un-aggregated; they stay. Never modifies the source files."""
    files = discover(paths)
    slots = defaultdict(set)  # match_id -> slots of pid
    st = LoadStats()
    for p in files:
        for ev in read_jsonl(p, st):
            params = ev.get("params") or {}
            if ev.get("event") == "player_slot_map" and params.get("anonymous_player_id") == pid and _is_slot(params.get("slot")):
                slots[ev.get("match_id")].add(params["slot"])
            if ev.get("anonymous_player_id") == pid and _is_slot(ev.get("player_slot")):
                slots[ev.get("match_id")].add(ev["player_slot"])
    removed = kept = 0
    bases = {}
    for root in paths:
        base = os.path.dirname(os.path.abspath(root)) if os.path.isdir(root) else os.path.dirname(os.path.abspath(root))
        for f in discover([root]):
            bases.setdefault(f, base)
    for p in files:
        rel = os.path.relpath(p, bases.get(p, os.path.dirname(p)))
        dst = os.path.join(out_dir, rel)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        with open(p, "rb") as f:
            data = f.read()
        out = []
        for raw in data.split(b"\n"):
            s = raw.strip()
            if not s:
                continue
            try:
                ev = json.loads(s.decode("utf-8", errors="replace"))
            except Exception:
                out.append(s)  # unparseable bytes cannot be judged; kept untouched
                kept += 1
                continue
            if isinstance(ev, dict) and _purge_hit(ev, pid, slots):
                removed += 1
                continue
            out.append(s)
            kept += 1
        with open(dst, "wb") as f:
            f.write(b"\n".join(out) + (b"\n" if out else b""))
    return {"files": len(files), "removed": removed, "kept": kept, "slots": {str(k): sorted(v) for k, v in slots.items()}}


def _purge_hit(ev, pid, slots):
    if ev.get("anonymous_player_id") == pid:
        return True
    params = ev.get("params") or {}
    if params.get("anonymous_player_id") == pid:
        return True
    sl = slots.get(ev.get("match_id"))
    if sl and ev.get("authority") == "host":
        for k in SLOT_KEYS:
            if _is_slot(params.get(k)) and params[k] in sl:
                return True
    return False


# =====================================================================================================================
# contexts (match / session) and classification
# =====================================================================================================================
def P(ev):
    return ev.get("params") or {}


def islot(v):
    return v if (isinstance(v, int) and not isinstance(v, bool)) else None


class Ctx:
    """One match (match_id) or, for events without a match_id, one hosting/client session."""

    def __init__(self, key, kind):
        self.key = key
        self.kind = kind
        self.events = defaultdict(list)
        self.all = []
        self.attr = {k: Counter() for k in ("group", "session", "build", "ruleset", "cohort", "env")}
        self.pcs = []
        self.exps = {}
        self.bots = False
        self.humans0 = False
        self.slotmap = {}
        self.covs = []
        # finalized
        self.group = self.build = self.ruleset = self.cohort = self.env = None
        self.pc = None
        self.klass = "human"

    def add(self, ev):
        self.all.append(ev)
        self.events[ev["event"]].append(ev)
        for k, f in (("group", "anonymous_group_id"), ("session", "session_id"), ("build", "build_version"),
                     ("ruleset", "ruleset_version"), ("cohort", "test_cohort"), ("env", "environment")):
            if ev.get(f):
                self.attr[k][ev[f]] += 1
        if ev.get("player_count"):
            self.pcs.append(ev["player_count"])
        if isinstance(ev.get("experiments"), dict):
            self.exps.update({k: str(v) for k, v in ev["experiments"].items()})
        if ev.get("is_bot") is True or ev.get("has_bots") is True:
            self.bots = True
        if isinstance(ev.get("observation_coverage"), (int, float)):
            self.covs.append(float(ev["observation_coverage"]))
        n = ev["event"]
        p = P(ev)
        if n == "player_slot_map" and islot(p.get("slot")) is not None:
            self.slotmap[p["slot"]] = (p.get("anonymous_player_id"), bool(p.get("is_bot")))
            if p.get("is_bot"):
                self.bots = True
        if n == "match_started":
            if (p.get("bots") or 0) > 0:
                self.bots = True
            if p.get("humans") == 0:
                self.humans0 = True

    def e(self, name):
        return self.events.get(name, [])

    def has(self, name):
        return name in self.events

    @property
    def coverage(self):
        return mean(self.covs) if self.covs else None

    @property
    def strat(self):
        return (self.build or "unknown", self.ruleset or "unknown")

    @property
    def is_match(self):
        return self.kind == "match"

    def dims(self):
        d = {}
        if self.pc:
            d["player_count"] = str(self.pc)
        if self.cohort:
            d["cohort"] = self.cohort
        d["data_class"] = self.klass
        for k, v in sorted(self.exps.items()):
            d["exp." + k] = v
        return d


def _top(counter, default=None):
    return counter.most_common(1)[0][0] if counter else default


def classify(ctx):
    if ctx.cohort == "synthetic_fixture":
        return "fixture"
    if ctx.cohort == "bot_sim":
        return "bot_sim"
    if ctx.humans0 or (ctx.slotmap and all(b for _p, b in ctx.slotmap.values())):
        return "bot_sim"
    if ctx.bots:
        return "human_bots"
    return "human"


def is_synthetic_class(k):
    return k != "human"


def build_contexts(events):
    ctxs = {}
    sess = defaultdict(lambda: {k: Counter() for k in ("group", "build", "ruleset", "cohort", "env")})
    for ev in events:
        s = ev.get("session_id")
        if s:
            for k, f in (("group", "anonymous_group_id"), ("build", "build_version"), ("ruleset", "ruleset_version"),
                         ("cohort", "test_cohort"), ("env", "environment")):
                if ev.get(f):
                    sess[s][k][ev[f]] += 1
        if ev.get("match_id"):
            key, kind = ev["match_id"], "match"
        elif s:
            key, kind = "session:" + s, "session"
        else:
            key, kind = "orphan", "session"
        c = ctxs.get(key)
        if c is None:
            c = ctxs[key] = Ctx(key, kind)
        c.add(ev)
    for c in ctxs.values():
        sid = _top(c.attr["session"])
        sd = sess.get(sid) if sid else None
        for k in ("group", "build", "ruleset", "cohort", "env"):
            v = _top(c.attr[k])
            if v is None and sd:
                v = _top(sd[k])
            setattr(c, k, v)
        if not c.group:
            c.group = "sess:" + sid if sid else "ungrouped"
        c.pc = max(c.pcs) if c.pcs else None
        c.klass = classify(c)
    # session contexts inherit the player count of the matches of their session (for slicing only)
    by_sess = defaultdict(list)
    for c in ctxs.values():
        if c.is_match and c.pc:
            by_sess[_top(c.attr["session"])].append(c.pc)
    for c in ctxs.values():
        if not c.is_match and not c.pc and by_sess.get(_top(c.attr["session"])):
            c.pc = max(by_sess[_top(c.attr["session"])])
    return ctxs


# =====================================================================================================================
# metric registry, collector, aggregation
# =====================================================================================================================
class Spec:
    def __init__(self, mid, kpi, name, kind, unit, requires, part, note, min_cov):
        self.id, self.kpi, self.name, self.kind, self.unit = mid, kpi, name, kind, unit
        self.requires, self.part, self.note, self.min_cov = requires, part, note, min_cov


SPECS = {}


def reg(mid, kpi, name, kind, unit="", requires=(), part=None, note="", min_cov=None):
    """kind: ratio (pooled num/den, cluster bootstrap) | gbin (one 0/1 per GROUP, Wilson) | sample (mean/median/p90)."""
    SPECS[mid] = Spec(mid, kpi, name, kind, unit, tuple(requires), part, note, min_cov)


class Rec:
    __slots__ = ("ctx", "a", "b", "dims")

    def __init__(self, ctx, a, b, dims):
        self.ctx, self.a, self.b, self.dims = ctx, a, b, dims


class Collector:
    def __init__(self):
        self.rec = defaultdict(list)

    def ratio(self, mid, ctx, num, den, **dims):
        if mid not in SPECS:
            raise KeyError(mid)
        self.rec[mid].append(Rec(ctx, num, den, {k: str(v) for k, v in dims.items() if v is not None}))

    def sample(self, mid, ctx, x, **dims):
        if mid not in SPECS:
            raise KeyError(mid)
        if x is None or isinstance(x, bool):
            return
        self.rec[mid].append(Rec(ctx, float(x), None, {k: str(v) for k, v in dims.items() if v is not None}))


def _fmt(x):
    if x is None:
        return ""
    if isinstance(x, float):
        return "%.6g" % x
    return str(x)


def slice_stats(spec, recs, ckey):
    """Statistics of one slice. Returns dict(num, den, value, n_groups, n_matches, ci_low, ci_high, ci_method, coverage,
    extra) for ratio/gbin or sample (mean) plus median/p90 in 'extra'."""
    groups = defaultdict(list)
    ctxs = {}
    for r in recs:
        groups[r.ctx.group].append(r)
        ctxs[r.ctx.key] = r.ctx
    out = {"ci_low": None, "ci_high": None, "ci_method": "", "value": None, "num": None, "den": None, "extra": {}}
    contributing_groups = set()
    contributing_matches = set()
    contrib_ctx = set()
    if spec.kind in ("ratio", "gbin"):
        pairs = []
        num = den = 0.0
        for g, rs in groups.items():
            n = sum(r.a for r in rs)
            d = sum(r.b for r in rs)
            if d > 0:
                contributing_groups.add(g)
                contributing_matches.update(r.ctx.key for r in rs if r.ctx.is_match and r.b > 0)
                contrib_ctx.update(r.ctx.key for r in rs if r.b > 0)
            pairs.append((n, d))
            num += n
            den += d
        out["num"], out["den"] = num, den
        if den > 0:
            out["value"] = num / den
            gp = [p for p in pairs if p[1] > 0]
            if spec.kind == "gbin":
                out["ci_low"], out["ci_high"] = wilson(num, den)
                out["ci_method"] = "wilson_groups"
            else:
                out["ci_low"], out["ci_high"] = boot_ratio_ci(gp, (spec.id, ckey))
                out["ci_method"] = "cluster_bootstrap_groups" if out["ci_low"] is not None else "n_groups<3"
    else:
        lists = []
        allx = []
        for g, rs in groups.items():
            xs = [r.a for r in rs]
            lists.append(xs)
            allx.extend(xs)
            contributing_groups.add(g)
            contributing_matches.update(r.ctx.key for r in rs if r.ctx.is_match)
            contrib_ctx.update(r.ctx.key for r in rs)
        out["num"], out["den"] = (sum(allx) if allx else None), len(allx)
        if allx:
            out["value"] = mean(allx)
            lo, hi = boot_sample_ci(lists, (spec.id, ckey, "mean"), "mean")
            out["ci_low"], out["ci_high"] = lo, hi
            out["ci_method"] = "cluster_bootstrap_groups" if lo is not None else "n_groups<3"
            for stat, q in (("median", 0.5), ("p90", 0.9)):
                lo2, hi2 = boot_sample_ci(lists, (spec.id, ckey, stat), stat)
                out["extra"][stat] = (percentile(allx, q), lo2, hi2)
    cov = [ctxs[k].coverage for k in contrib_ctx if ctxs[k].coverage is not None]
    out["coverage"] = mean(cov) if cov else None
    out["n_groups"] = len(contributing_groups)
    out["n_matches"] = len(contributing_matches)
    return out


def status_for(spec, st, have_events):
    if "__never__" in spec.requires:
        return "not_measured"
    if st["den"] in (None, 0):
        if spec.requires and not all(any(x in have_events for x in r.split("|")) for r in spec.requires):
            return "not_measured"
        return "insufficient_data"
    if st["n_groups"] < MIN_GROUPS_OK:
        return "insufficient_data"
    if spec.min_cov is not None and st["coverage"] is not None and st["coverage"] < spec.min_cov:
        return "insufficient_data"
    return "ok"


METRIC_COLUMNS = ["metric_id", "kpi", "metric", "build_version", "ruleset_version", "partition", "grouping", "group_value",
                  "numerator", "denominator", "value", "unit", "n_groups", "n_matches", "coverage", "status", "ci_low",
                  "ci_high", "ci_method", "note"]


def aggregate(S):
    """Rows for every spec x stratum x partition x grouping. Strata are never pooled."""
    rows = []
    for spec in SPECS.values():
        recs_all = S.C.rec.get(spec.id, [])
        for strat in S.strata:
            have = S.strat_events.get(strat, set())
            srecs = [r for r in recs_all if r.ctx.strat == strat]
            parts = sorted({r.dims.get(spec.part) for r in srecs if r.dims.get(spec.part) is not None}) if spec.part else [""]
            if spec.part and not parts:
                parts = [""]
            for pv in parts:
                precs = [r for r in srecs if (not spec.part or r.dims.get(spec.part) == pv)] if pv != "" else srecs
                slices = defaultdict(list)
                slices[("all", "all")] = list(precs)
                for r in precs:
                    seen = set()
                    for d, v in list(r.dims.items()) + list(r.ctx.dims().items()):
                        if d == spec.part or v is None:
                            continue
                        k = (d, str(v))
                        if k in seen:
                            continue
                        seen.add(k)
                        slices[k].append(r)
                if "day" in {d for (d, _v) in slices} and "round" in {d for (d, _v) in slices}:
                    for r in precs:
                        if "day" in r.dims and "round" in r.dims:
                            slices[("day_round", "d%s_r%s" % (r.dims["day"], r.dims["round"]))].append(r)
                for (gt, gv) in sorted(slices):
                    rs = slices[(gt, gv)]
                    if gt in ("cohort", "data_class") or gt.startswith("exp."):
                        if len(rs) == len(precs) and (gt, gv) != ("all", "all") and len({r.ctx.dims().get(gt) for r in precs}) == 1:
                            continue  # single-valued context dimension: identical to the 'all' row
                    st = slice_stats(spec, rs, (strat, pv, gt, gv))
                    status = status_for(spec, st, have)
                    base = {"metric_id": spec.id, "kpi": spec.kpi, "metric": spec.name, "build_version": strat[0],
                            "ruleset_version": strat[1], "partition": pv, "grouping": gt, "group_value": gv,
                            "numerator": st["num"], "denominator": st["den"], "value": st["value"], "unit": spec.unit,
                            "n_groups": st["n_groups"], "n_matches": st["n_matches"], "coverage": st["coverage"],
                            "status": status, "ci_low": st["ci_low"], "ci_high": st["ci_high"],
                            "ci_method": st["ci_method"], "note": spec.note}
                    if spec.kind == "sample":
                        base["note"] = (spec.note + " " if spec.note else "") + "value=mean; numerator=sum; denominator=n samples"
                    if status == "not_measured":
                        base["value"] = base["numerator"] = base["ci_low"] = base["ci_high"] = None
                    rows.append(base)
                    if spec.kind == "sample" and st["extra"]:
                        for stat, (v, lo, hi) in st["extra"].items():
                            r2 = dict(base)
                            r2.update({"metric_id": spec.id + "." + stat, "value": v, "ci_low": lo, "ci_high": hi,
                                       "ci_method": "cluster_bootstrap_groups" if lo is not None else "n_groups<3",
                                       "numerator": None, "metric": spec.name + " (" + stat + ")"})
                            rows.append(r2)
    return rows


def write_metrics_csv(rows, path):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    with open(path, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(METRIC_COLUMNS)
        for r in rows:
            w.writerow([_fmt(r.get(c)) for c in METRIC_COLUMNS])


# =====================================================================================================================
# analysis state
# =====================================================================================================================
class Analysis:
    def __init__(self, events, load_stats, include_synthetic=False, observer=None, feedback=None):
        self.events = events
        self.load = load_stats
        self.include_synthetic = include_synthetic
        self.all_ctx = build_contexts(events)
        self.excluded = Counter()
        self.ctxs = []
        for c in sorted(self.all_ctx.values(), key=lambda c: (c.group, c.key)):
            if is_synthetic_class(c.klass) and not include_synthetic:
                self.excluded[c.klass] += 1
            else:
                self.ctxs.append(c)
        self.matches = [c for c in self.ctxs if c.is_match]
        self.has_synthetic = any(is_synthetic_class(c.klass) for c in self.ctxs)
        self.strata = sorted({c.strat for c in self.ctxs}) or [("none", "none")]
        self.strat_events = defaultdict(set)
        for c in self.ctxs:
            self.strat_events[c.strat].update(c.events.keys())
        self.groups = sorted({c.group for c in self.ctxs})
        self.group_classes = defaultdict(set)
        for c in self.all_ctx.values():
            self.group_classes[c.group].add(c.klass)
        self.C = Collector()
        self.observer = observer or []
        self.feedback = feedback or []
        self.obs_ctx = {}

    def universe(self, name, param, matches_only=True):
        vals = set()
        for c in (self.matches if matches_only else self.ctxs):
            for e in c.e(name):
                v = P(e).get(param)
                if v is not None and not isinstance(v, bool):
                    vals.add(str(v))
        return sorted(vals)


def pseudo_ctx(S, key, group, build=None, ruleset=None, pc=None, cohort=None, linked=None):
    """Lightweight Ctx for observer/feedback rows; reuses the linked log context when one exists."""
    if linked is not None:
        return linked
    ck = ("pseudo", key)
    if ck in S.obs_ctx:
        return S.obs_ctx[ck]
    c = Ctx(key, "match")
    c.group, c.build, c.ruleset, c.cohort = group or "ungrouped", build or "unknown", ruleset or "unknown", cohort
    c.pc = pc
    c.klass = "human"
    for gk in S.group_classes.get(c.group, ()):
        if gk != "human":
            c.klass = gk
    S.obs_ctx[ck] = c
    return c


# =====================================================================================================================
# metric registry (id, KPI, name, kind, unit, required events ["a|b" = any of], partition dimension, note, min coverage)
# =====================================================================================================================
PHASES = ["Lobby", "Morning", "RoundIntro", "RoundPlay", "RoundReveal", "Break", "MeetingDossier", "MeetingDiscussion",
          "MeetingVote", "MeetingVerdict", "FinalDefense", "FinalVote", "FinalVerdict", "WeekResults"]
REASONS_PHASE = ["timer", "timer_shortened", "host_continue", "abandoned", "skipped"]
COMM_MODES = ["none", "voice", "text", "mixed"]
MECHANICAL = ["sabotage_verified", "player_fired", "company_failed"]
CAREER_COMPONENTS = ["not_fired", "company_target", "objective", "eotm", "sabotage_penalty", "total"]
FB_QUESTIONS = [
    ("q_contact_helped", "E", "Did contacting others help you decide?"),
    ("q_reach_people", "F", "Could you reach the people you needed?"),
    ("q_too_much_waiting", "F", "Did you spend too much time waiting?"),
    ("q_recording_awareness", "H", "Did recording awareness affect your negotiation?"),
    ("q_accusations_game_events", "I", "Did accusations relate to game events?"),
    ("q_recover_bad_start", "I", "Could you recover from a bad start?"),
    ("q_play_again", "L", "Would you voluntarily play again?"),
    ("q_wanted_to_share", "M", "Was there a moment you wanted to share?"),
    ("q_hear_over_music", "H", "Could you clearly hear calls and recording reveals over the music?"),
]
FB_ANSWERS = ["Yes", "Somewhat", "No", "Not sure"]
OBS_FIELDS = [  # observer flag -> (metric id, KPI, label, denominator flag or None)
    ("natural_accusation_occurred", "O.natural_accusation", "I", "Rows with a natural accusation"),
    ("accusation_referenced_game_evidence", "O.accusation_evidence", "I", "Accusations that referenced game evidence"),
    ("players_reconsidered_accusation", "O.accusation_reconsidered", "I", "Accusations players reconsidered"),
    ("players_appeared_confused", "O.confused", "G", "Rows where players appeared confused"),
    ("players_appeared_excluded", "O.excluded", "F", "Rows where players appeared excluded"),
    ("meeting_felt_too_short", "O.meeting_too_short", "H", "Meetings that felt too short"),
    ("meeting_felt_too_long", "O.meeting_too_long", "H", "Meetings that felt too long"),
    ("funny_betrayal", "O.funny_betrayal", "M", "Rows with a funny betrayal"),
    ("unexpected_accusation", "O.unexpected_accusation", "M", "Rows with an unexpected accusation"),
    ("surprising_outcome", "O.surprising_outcome", "M", "Rows with a surprising outcome"),
    ("players_requested_rematch", "O.rematch_requested", "L", "Rows where players asked for a rematch"),
    ("recording_created_reaction", "M.recording_reaction", "H", "Recording reveals that created a reaction"),
    ("recording_was_referenced_later", "M.recording_referenced_later", "H", "Recording reveals referenced later"),
    ("recording_was_intelligible", "M.recording_intelligible", "H", "Recording reveals judged intelligible"),
    ("recording_was_relevant", "M.recording_relevant", "H", "Recording reveals judged relevant"),
    ("recording_lacked_context", "M.recording_contextless", "H", "Recording reveals lacking context"),
]

_OBS_NOTE = "observer-coded rows; blank answers are missing, not No"
for _f, _id, _k, _n in OBS_FIELDS:
    reg(_id, _k, _n + " (share of rows where the field was coded)", "ratio", "share", ["observer"], note=_OBS_NOTE)
for _qid, _k, _txt in FB_QUESTIONS:
    reg("FB." + _qid, _k, "Feedback: " + _txt, "ratio", "share", ["feedback"], part="answer",
        note="self-report, fixed choices; blank = missing; respondents of one group are not independent")

reg("A.join_success", "A", "Join attempts that reached the lobby", "ratio", "share", ["lobby_join_attempted"], note="per attempt; retries count as new attempts; client logs only")
reg("A.join_failure_reason", "A", "Join attempts failing, by reason", "ratio", "share", ["lobby_join_attempted"], part="reason")
reg("A.join_wait_seconds", "A", "Seconds from join to lobby entry", "sample", "s", ["lobby_joined"])
reg("A.lobby_fill_seconds", "A", "Lobby fill time", "sample", "s", ["match_started"])
reg("A.ready_seconds", "A", "Ready time", "sample", "s", ["match_started"])
reg("A.time_to_start_seconds", "A", "Lobby fill + ready time", "sample", "s", ["match_started"])
reg("A.start_failure_rate", "A", "Start attempts that failed", "ratio", "share", ["match_start_attempted"])
reg("A.start_failure_reason", "A", "Start failures by reason", "ratio", "share", ["match_start_attempted"], part="reason")
reg("A.departure_per_match", "A", "Mid-match client interruptions per started match, by phase", "ratio", "per match", ["match_started", "session_interrupted"], part="phase", note="client logs required; a match without client logs is skipped, not zero")
reg("B.phase_duration", "B", "Actual phase duration", "sample", "s", ["phase_exited"])
reg("B.overrun_rate", "B", "Phase exits that overran their plan", "ratio", "share", ["phase_exited"], note="overrun_seconds > 0.5")
reg("B.overrun_seconds", "B", "Overrun seconds per phase exit", "sample", "s", ["phase_exited"])
reg("B.round_completion", "B", "Rounds started that finished", "ratio", "share", ["round_started"])
reg("B.day_completion", "B", "Days started that finished", "ratio", "share", ["day_started"])
reg("B.match_completion", "B", "Matches with an observed end that completed", "ratio", "share", ["match_started"], note="completed / (completed + abandoned); unknown ends excluded and reported separately")
reg("B.match_unknown_end", "B", "Started matches with no observed end (unknown, not failure)", "ratio", "share", ["match_started"])
reg("B.abandon_phase", "B", "Started matches abandoned in phase", "ratio", "share", ["match_started"], part="phase")
reg("B.completion_reason", "B", "Phase exits by completion reason", "ratio", "share", ["phase_exited"], part="reason")
reg("B.timer_ending_share", "B", "RoundPlay phases that ended on the timer (not all-submitted)", "ratio", "share", ["phase_exited"])
reg("B.early_submit_share", "B", "Rounds that ended early because everyone submitted", "ratio", "share", ["round_finished"])
reg("B.saved_seconds", "B", "Seconds saved by early submission", "sample", "s", ["round_finished"])
reg("B.carried_seconds", "B", "Seconds carried into the following break", "sample", "s", ["round_finished"])
reg("C.phone_participation", "C", "Observed players with >=1 connected call overlapping the round", "ratio", "share", ["phone_round_summary|round_player_summary"], min_cov=0.5, note="failed/rejected/missed calls do not count; denominator = eligible OBSERVED players")
reg("C.observed_share", "C", "Eligible players that were observed (consent coverage)", "ratio", "share", ["phone_round_summary"])
reg("D.first_contact_latency", "D", "Seconds to first successful contact (rounds with contact only)", "sample", "s", ["round_player_summary"], note="no-call player-rounds are excluded here (see D.no_contact_share), never counted as 0")
reg("D.no_contact_share", "D", "Observed player-rounds with no successful contact", "ratio", "share", ["round_player_summary"])
reg("D.call_duration", "D", "Connected call duration (each call once)", "sample", "s", ["phone_call_finished"], note="connected time is not speaking time")
reg("D.call_duration_participant", "D", "Connected call duration per participant (each call once per participant)", "sample", "s", ["phone_call_finished"])
reg("D.connected_seconds", "D", "Connected seconds per player-round (interval union, host summary)", "sample", "s", ["round_player_summary"])
reg("D.connected_seconds_derived", "D", "Connected seconds per player-round recomputed from call intervals clipped to RoundPlay", "sample", "s", ["phone_call_connected"])
reg("D.unique_partners", "D", "Unique call partners per player-round", "sample", "partners", ["round_player_summary"])
reg("D.partner_hhi", "D", "Partner concentration (HHI of connected seconds) per player with >=2 connected calls in a match", "sample", "HHI", ["phone_call_finished"], note="1 = a single partner")
reg("D.busy_failure_rate", "D", "Call requests that failed busy", "ratio", "share", ["phone_call_requested"])
reg("D.call_failure_reason", "D", "Call requests that failed, by reason", "ratio", "share", ["phone_call_requested"], part="reason")
reg("D.unanswered_rate", "D", "Call requests missed or rejected", "ratio", "share", ["phone_call_requested"])
reg("D.attempt_never_connect", "D", "Players who attempted a call but never connected in the match", "ratio", "share", ["phone_call_requested"])
reg("E.mode_share", "E", "Player-rounds by communication mode", "ratio", "share", ["round_player_summary"], part="comm_mode")
reg("E.quality", "E", "Choice quality by communication mode", "sample", "quality", ["choice_outcome_resolved", "round_player_summary"], part="comm_mode", note="association only; confounders: task, role, player skill, partner availability, group, selection of who talks")
reg("E.income", "E", "Income per player-round by communication mode", "sample", "money", ["choice_outcome_resolved", "round_player_summary"], part="comm_mode", note="association only")
reg("E.cooperation_rate", "E", "Cooperation credits per opportunity by communication mode", "ratio", "share", ["choice_outcome_resolved", "round_player_summary"], part="comm_mode", note="association only")
reg("E.revisions", "E", "Decision revisions per player-round by communication mode", "sample", "count", ["round_player_summary"], part="comm_mode")
reg("E.career_total", "E", "Career total by player communication profile (>=50% rounds silent vs not)", "sample", "points", ["career_breakdown", "round_player_summary"], part="profile", note="survived matches only; association only")
reg("E.silent_not_dominant", "E", "Groups where no-communication mean quality exceeds communicating mean quality", "gbin", "share of groups", ["choice_outcome_resolved", "round_player_summary"], note="needs >=3 player-rounds per mode in the group; H1 expects a LOW share")
reg("F.longest_gap", "F", "Longest observable interaction gap per player-round", "sample", "s", ["round_player_summary"], note="gap = time without a call, message or incoming contact; reading/listening is not boredom")
reg("F.post_submit_wait", "B/F", "Seconds waiting after own submission", "sample", "s", ["round_player_summary"])
reg("F.post_submit_wait_over20", "F", "Submitted player-rounds waiting >= 20 s after submitting", "ratio", "share", ["round_player_summary"], note="20 s is a design target, not a benchmark")
reg("F.busy_failures", "F", "Busy-partner failures per player-round", "sample", "count", ["round_player_summary"])
reg("F.blocked_seconds", "F", "Technically blocked seconds per player-round", "sample", "s", ["round_player_summary"])
reg("F.blocked_share", "F", "Player-rounds with any technical blocking", "ratio", "share", ["round_player_summary"])
reg("F.observation_coverage", "F", "Player-rounds with a summary / connected players at round end", "ratio", "share", ["round_player_summary", "round_finished"], note="the remainder is UNKNOWN, not good service")
reg("F.worst_served_gap", "F", "Per match: the largest mean longest-gap of any single observed player", "sample", "s", ["round_player_summary"])
reg("G.latency", "G", "Submission latency", "sample", "s", ["choice_submitted"])
reg("G.revisions_per_submit", "G", "Choice changes per submission", "ratio", "per submit", ["choice_submitted"])
reg("G.after_contact_share", "G", "Submissions made after contact", "ratio", "share", ["choice_submitted"])
reg("G.timeout_share", "G", "Missed submissions (timeouts) by reason", "ratio", "share", ["choice_submitted"], part="reason")
reg("G.invalid_actions", "G", "Rejected UI actions + rejected messages per observed player-round", "ratio", "per player-round", ["ui_action_rejected|message_rejected", "round_player_summary"], note="client logs only")
reg("G.category_share", "G", "Categorical choice distribution per task", "ratio", "share", ["choice_submitted"], part="category")
reg("G.blame_delta", "G", "Blame change per fault-point event, by reason", "sample", "points", ["fault_points_changed"], part="reason")
reg("H.playback_success", "H", "Recording reveals that played", "ratio", "share", ["recording_playback_finished"])
reg("H.playback_failure", "H", "Recording reveals by failure reason", "ratio", "share", ["recording_playback_finished"], part="failure")
reg("H.actual_duration", "H", "Actual duration of played recording reveals", "sample", "s", ["recording_playback_finished"])
reg("H.context_share", "H", "Reveals with a source task context", "ratio", "share", ["recording_playback_started"])
reg("H.prior_exposure_share", "H", "Reveals whose speakers were already exposed earlier", "ratio", "share", ["recording_playback_started"])
reg("H.same_group_share", "H", "Reveals with same_group flag", "ratio", "share", ["recording_playback_started"])
reg("H.speech_density", "H", "Speech share of the reveal snippet (speech_ms / snippet_ms)", "sample", "share", ["recording_playback_started"], note="per-snippet metadata only; per-player speaking time is NOT measured")
reg("H.participant_coverage", "H", "Players appearing in >=1 played reveal per match / player count", "ratio", "share", ["recording_playback_started"])
reg("H.repeat_exposed_players", "H", "Exposed players heard in >=2 reveals", "ratio", "share", ["recording_playback_started"])
reg("H.vote_participation", "H", "Eligible players who voted in warning meetings", "ratio", "share", ["meeting_vote_resolved"])
reg("H.abstain_share", "H", "Warning-meeting voters whose final ballot was abstain", "ratio", "share", ["meeting_vote_submitted"])
reg("H.no_action_vote_share", "H", "Cast votes for No action in warning meetings", "ratio", "share", ["meeting_vote_resolved"])
reg("H.vote_outcome", "H", "Warning meetings by outcome", "ratio", "share", ["meeting_vote_resolved"], part="outcome")
reg("H.discussion_duration", "H", "Actual meeting discussion duration", "sample", "s", ["meeting_discussion_finished"])
reg("H.final_participation", "H", "Final roster members with a non-abstain ballot", "ratio", "share", ["final_vote_resolved", "final_vote_submitted"])
reg("H.final_abstain_share", "H", "Final roster members whose last ballot was abstain", "ratio", "share", ["final_vote_resolved", "final_vote_submitted"])
reg("H.final_fire_nobody_share", "H", "Final roster votes for Fire nobody", "ratio", "share", ["final_vote_resolved"])
reg("H.final_outcome", "H", "Final votes by outcome", "ratio", "share", ["final_vote_resolved"], part="outcome")
reg("H.speaking_time_per_player", "H", "Speaking time per player", "ratio", "share", ["__never__"], note="no event provides per-player speaking time")
reg("I.blame_spread", "I", "Blame spread (max - min) at day end", "sample", "points", ["fault_points_changed", "day_finished"], note="start blame assumed " + str(START_BLAME) + "; concentration is not proof of unfairness")
reg("I.blame_top_share", "I", "Blame leader's share of total blame at day end", "sample", "share", ["fault_points_changed", "day_finished"])
reg("I.blame_top_share_baseline", "I", "Even-split baseline (1/players) for the leader share", "sample", "share", ["fault_points_changed", "day_finished"])
reg("I.change_reason", "I", "Blame changes by reason", "ratio", "share", ["fault_points_changed"], part="reason")
reg("I.recovery_use", "I", "Remedial requests per recovery opportunity (players with options)", "ratio", "share", ["recovery_opportunities"])
reg("I.recovery_accept", "I", "Remedial requests accepted", "ratio", "share", ["remedial_resolved"])
reg("I.recovery_output_added", "I", "Output added per accepted remedial", "sample", "points", ["remedial_resolved"])
reg("I.repeat_warning", "I", "Meetings that gave a repeat warning / meetings where a repeat was possible", "ratio", "share", ["meeting_vote_resolved", "warning_assigned"], note="eligible = at least one player already had 1 warning (cap " + str(WARNING_CAP) + ")")
reg("I.nomination_capped", "I", "Meetings recording a capped nomination / meetings where a player was at the cap", "ratio", "share", ["meeting_vote_resolved"], note="relies on an outcome value containing 'cap'")
reg("I.target_was_blame_leader", "I", "Resolved meetings whose target already led blame", "ratio", "share", ["meeting_vote_resolved"])
reg("I.warning_top_share", "I", "Most-warned player's share of all warnings in a match (>=2 warnings)", "sample", "share", ["warning_assigned"])
reg("I.warning_top_share_baseline", "I", "Even-split baseline (1/players) for warning concentration", "sample", "share", ["warning_assigned"])
reg("J.income_gini", "J", "Income Gini across consenting players per match", "sample", "gini", ["income_credited"], note="private events: only after match end and consenting players")
reg("J.income_top_share", "J", "Top earner's share of income", "sample", "share", ["income_credited"])
reg("J.income_top_share_baseline", "J", "Even-split baseline (1/players)", "sample", "share", ["income_credited"])
reg("J.evidence_gini", "J", "Evidence purchase Gini across players", "sample", "gini", ["evidence_purchased"])
reg("J.evidence_top_share", "J", "Top buyer's share of evidence purchases", "sample", "share", ["evidence_purchased"])
reg("J.evidence_purchases", "J", "Evidence purchases per match (consenting players)", "sample", "count", ["player_slot_map"])
reg("J.evidence_spend", "J", "Money spent on evidence per match", "sample", "money", ["player_slot_map"])
reg("J.transfers", "J", "Money transfers per match", "sample", "count", ["player_slot_map"])
reg("J.transfer_amount", "J", "Amount per money transfer", "sample", "money", ["money_transferred"])
reg("J.sabotage_verified_rate", "J", "Verified sabotage incidents / incidents created, by kind", "ratio", "share", ["incident_created"], part="kind", note="created incidents of consenting actors only")
reg("J.sabotage_verify_delay", "J", "Seconds from incident to verification", "sample", "s", ["sabotage_verified"])
reg("J.exposed_incident_share", "J", "Exposed incidents / incidents (company_result)", "ratio", "share", ["company_result"])
reg("J.sabotage_loss_share", "J", "Output lost to sabotage / max output", "ratio", "share", ["task_output_resolved"])
reg("J.remedial_recovery_share", "J", "Remedial output added / output lost", "ratio", "share", ["task_output_resolved", "remedial_resolved"])
reg("J.objective_completion", "J", "Secret objectives completed, by kind", "ratio", "share", ["objective_completed"], part="kind")
reg("J.career_component", "J", "Career points by component", "sample", "points", ["career_breakdown"], part="component")
reg("J.rank1_is_richest", "J", "Matches where a rank-1 player also had the most money", "ratio", "share", ["career_breakdown"], note="compare with J.rank1_is_richest_baseline (chance); closed companies excluded")
reg("J.rank1_is_richest_baseline", "J", "Chance baseline: share of players holding max money", "ratio", "share", ["career_breakdown"])
reg("J.rank1_is_best_quality", "J", "Matches where a rank-1 player also had the best quality", "ratio", "share", ["career_breakdown"])
reg("J.rank1_is_best_quality_baseline", "J", "Chance baseline: share of players holding max quality", "ratio", "share", ["career_breakdown"])
reg("J.company_closed_rate", "J", "Matches whose company closed (final productivity under target)", "ratio", "share", ["company_result"])
reg("J.final_productivity", "J", "Final company productivity (per-mille)", "sample", "permille", ["company_result"])
reg("K.technical_failure_match", "K", "Matches with >=1 technical failure, by category", "ratio", "share", [], part="category", note="only caught failures are logged; unknown exits stay unknown")
reg("K.reconnect_success", "K", "Reconnect attempts that succeeded", "ratio", "share", ["reconnect_attempted|reconnect_succeeded|reconnect_failed"])
reg("K.reconnect_downtime", "K", "Downtime of reconnected players", "sample", "s", ["player_reconnected"])
reg("K.phone_failure_nonbusy", "K", "Call requests failing for dead_line / absent / wrong_phase", "ratio", "share", ["phone_call_requested"])
reg("K.voice_loss", "K", "Voice packet loss", "sample", "pct", ["voice_quality_summary"])
reg("K.voice_jitter", "K", "Voice jitter", "sample", "ms", ["voice_quality_summary"])
reg("K.voice_underruns", "K", "Voice underruns per 60 s window", "sample", "count", ["voice_quality_summary"])
reg("K.stuck_phase", "K", "stuck_phase events per match", "sample", "count", ["match_started"])
reg("K.unexpected_transition", "K", "unexpected_transition events per match", "sample", "count", ["match_started"])
reg("K.possible_crash", "K", "Client sessions started with no session_ended / interruption (possible crash or kill; unknown)", "ratio", "share", ["session_started"], note="best effort; truncated files also produce this")
reg("L.voluntary_rematch", "L", "Groups with a voluntary rematch / completed groups with a genuine opportunity", "gbin", "share of groups", ["rematch_prompt_shown|week_completed"], note="excludes auto restarts and moderator-required repeats; intent is separate")
reg("L.rematch_intent", "L", "Groups with a rematch request / groups with a genuine opportunity", "gbin", "share of groups", ["rematch_prompt_shown|week_completed"])
reg("L.intent_to_rematch", "L", "Groups that rematched / groups that asked for one", "gbin", "share of groups", ["rematch_requested|rematch_started"])
reg("M.mechanical_events", "M", "Automatic mechanical events per completed match", "sample", "count", ["week_completed"], part="event", note="(1) automatic; not confirmed notable moments")
reg("M.player_marked_groups", "M", "(2) Groups with >=1 player-marked moment / groups with client observation", "gbin", "share of groups", ["moment_marked"], note="player-marked candidates, not confirmed")
reg("M.player_marked_per_match", "M", "(2) Player-marked moments per match with client logs", "sample", "count", ["moment_marked"])
reg("M.player_marked_category", "M", "(2) Player-marked moments by category", "ratio", "share", ["moment_marked"], part="category")
reg("M.groups_notable", "M", "(3) Groups with >=1 observer-confirmed notable moment / groups with adequate observation", "gbin", "share of groups", ["observer"], note="adequate = observer rows covering >=50% of the group's logged matches (or any rows if no logs)")
reg("M.notable_per_match", "M", "(3) Observer-confirmed notable moments per observed match", "sample", "count", ["observer"])
reg("M.notable_category", "M", "(3) Notable moments by category", "ratio", "share", ["observer"], part="category")
reg("M.notable_day", "M", "(3) Notable moments by day", "ratio", "share", ["observer"], part="day")
reg("M.notable_phase", "M", "(3) Notable moments by phase", "ratio", "share", ["observer"], part="phase")
reg("M.share_interest", "M", "(4) Players wanting to share a moment (feedback)", "ratio", "share", ["feedback"], part="answer")
reg("M.observation_coverage", "M", "Logged human matches with observer rows", "ratio", "share", ["match_started"])
reg("M.export", "M", "(5) Actual exports of a moment", "ratio", "share", ["__never__"], note="no implementation in the game")
reg("M.external_share", "M", "(6) Actual external sharing", "ratio", "share", ["__never__"], note="no implementation in the game")


# =====================================================================================================================
# helpers
# =====================================================================================================================
def rd(e):
    d = {}
    if e.get("day_index") is not None:
        d["day"] = e["day_index"]
    if e.get("round_index") is not None:
        d["round"] = e["round_index"]
    if e.get("task_id"):
        d["task_id"] = e["task_id"]
    return d


def rkey(e):
    return (e.get("day_index"), e.get("round_index"))


def mono(e):
    return e.get("monotonic_elapsed_seconds")


def num(v):
    return float(v) if isinstance(v, (int, float)) and not isinstance(v, bool) else None


def has_client(ctx):
    return any(e["authority"] == "client" for e in ctx.all)


def private_flushed(ctx):
    return ctx.has("career_breakdown") or ctx.has("week_completed") or ctx.has("match_abandoned")


def company_closed(ctx):
    for e in ctx.e("company_result"):
        if P(e).get("closed") is True:
            return True
    for e in ctx.e("week_completed"):
        if P(e).get("company_closed") is True:
            return True
    return False


def derive_calls(ctx):
    calls = {}
    for e in ctx.e("phone_call_connected"):
        p = P(e)
        cid = p.get("call_id")
        if cid is None:
            continue
        calls[cid] = {"id": cid, "a": islot(p.get("caller")), "b": islot(p.get("callee")), "start": mono(e), "dur": None, "cens": False}
    for e in ctx.e("phone_call_finished"):
        p = P(e)
        cid = p.get("call_id")
        if cid is None:
            continue
        c = calls.get(cid)
        if c is None:
            c = calls[cid] = {"id": cid, "a": islot(p.get("caller")), "b": islot(p.get("callee")), "start": None, "dur": None, "cens": False}
        c["dur"] = num(p.get("duration_seconds"))
        c["cens"] = bool(p.get("censored"))
        if c["start"] is None and mono(e) is not None and c["dur"] is not None:
            c["start"] = mono(e) - c["dur"]
    return list(calls.values())


def round_windows(ctx):
    ent, w = {}, {}
    for e in ctx.e("phase_entered"):
        if e.get("phase") == "RoundPlay" and mono(e) is not None:
            ent[rkey(e)] = mono(e)
    for e in ctx.e("phase_exited"):
        if e.get("phase") == "RoundPlay":
            k = rkey(e)
            s = ent.get(k)
            if s is None:
                continue
            end = mono(e)
            dur = num(P(e).get("duration_seconds"))
            if dur is not None:
                end = s + dur
            if end is not None and end > s:
                w[k] = (s, end)
    return w


def player_round_intervals(ctx, calls):
    """slot -> [(start, end)] connected intervals (each call counts once per participant)."""
    iv = defaultdict(list)
    for c in calls:
        if c["start"] is None or c["dur"] is None or c["dur"] <= 0:
            continue
        for s in (c["a"], c["b"]):
            if s is not None:
                iv[s].append((c["start"], c["start"] + c["dur"]))
    return iv


def derived_round_connected(ctx):
    """{(round_key, slot): union seconds clipped to RoundPlay}. Exposed for tests."""
    calls = derive_calls(ctx)
    iv = player_round_intervals(ctx, calls)
    out = {}
    for k, (lo, hi) in round_windows(ctx).items():
        for slot, ints in iv.items():
            out[(k, slot)] = interval_union(clip_intervals(ints, lo, hi))
    return out


def roster_slots(ctx):
    r = set(ctx.slotmap)
    if not r:
        for name in ("income_credited", "fault_points_changed", "career_breakdown"):
            for e in ctx.e(name):
                s = islot(P(e).get("player"))
                if s is not None:
                    r.add(s)
    return sorted(r)


def group_rep(S, per_group):
    """per_group: {(group, strat): [ctx,...]} -> iterate with representative ctx."""
    for (g, st), cs in sorted(per_group.items()):
        yield g, st, cs[0], cs


# =====================================================================================================================
# KPI computations
# =====================================================================================================================
def kpi_A(S):
    C = S.C
    for ctx in S.ctxs:
        att = ctx.e("lobby_join_attempted")
        if att:
            C.ratio("A.join_success", ctx, len(ctx.e("lobby_joined")), len(att))
            fails = Counter(str(P(e).get("reason")) for e in ctx.e("lobby_join_failed"))
            for r in sorted(set(fails) | set(S.universe("lobby_join_failed", "reason", False))):
                C.ratio("A.join_failure_reason", ctx, fails.get(r, 0), len(att), reason=r)
        for e in ctx.e("lobby_joined"):
            p = P(e)
            if not p.get("reconnect") and num(p.get("wait_seconds")) is not None:
                C.sample("A.join_wait_seconds", ctx, p["wait_seconds"])
        for e in ctx.e("match_started"):
            p = P(e)
            f, r = num(p.get("lobby_fill_seconds")), num(p.get("ready_seconds"))
            C.sample("A.lobby_fill_seconds", ctx, f)
            C.sample("A.ready_seconds", ctx, r)
            if f is not None and r is not None:
                C.sample("A.time_to_start_seconds", ctx, f + r)
        attempts = len(ctx.e("match_start_attempted"))
        if attempts:
            failed = Counter(str(P(e).get("reason")) for e in ctx.e("match_start_failed"))
            C.ratio("A.start_failure_rate", ctx, sum(failed.values()), attempts)
            for r in sorted(set(failed) | {"lobby.need_players", "lobby.need_ready", "lobby.pool_too_small", "engine_error"}):
                C.ratio("A.start_failure_reason", ctx, failed.get(r, 0), attempts, reason=r)
        if ctx.is_match and ctx.has("match_started") and has_client(ctx):
            ints = Counter(str(P(e).get("phase")) for e in ctx.e("session_interrupted") if P(e).get("mid_match"))
            for ph in sorted(set(PHASES) | set(ints)):
                C.ratio("A.departure_per_match", ctx, ints.get(ph, 0), len(ctx.e("match_started")), phase=ph)


def kpi_B(S):
    C = S.C
    for ctx in S.matches:
        if ctx.has("match_started"):
            comp = ctx.has("week_completed")
            ab = ctx.e("match_abandoned")
            term = comp or bool(ab)
            if term:
                C.ratio("B.match_completion", ctx, 1 if comp else 0, 1)
            C.ratio("B.match_unknown_end", ctx, 0 if term else 1, 1)
            phs = {str(P(e).get("phase")) for e in ab}
            for ph in sorted(set(PHASES) | phs):
                C.ratio("B.abandon_phase", ctx, 1 if ph in phs else 0, 1, phase=ph)
        fin = {rkey(e) for e in ctx.e("round_finished")}
        for e in ctx.e("round_started"):
            C.ratio("B.round_completion", ctx, 1 if rkey(e) in fin else 0, 1, **rd(e))
        dfin = {e.get("day_index") for e in ctx.e("day_finished")}
        for e in ctx.e("day_started"):
            C.ratio("B.day_completion", ctx, 1 if e.get("day_index") in dfin else 0, 1, day=e.get("day_index"))
        for e in ctx.e("phase_exited"):
            p = P(e)
            ph = e.get("phase")
            dims = dict(rd(e), phase=ph)
            dur, plan = num(p.get("duration_seconds")), num(p.get("planned_duration_seconds"))
            C.sample("B.phase_duration", ctx, dur, **dims)
            over = num(p.get("overrun_seconds"))
            if over is None and dur is not None and plan is not None:
                over = dur - plan
            if over is not None:
                C.ratio("B.overrun_rate", ctx, 1 if over > 0.5 else 0, 1, **dims)
                C.sample("B.overrun_seconds", ctx, max(over, 0.0), **dims)
            reason = p.get("completion_reason")
            if reason is not None:
                for r in sorted(set(REASONS_PHASE) | {str(reason)}):
                    C.ratio("B.completion_reason", ctx, 1 if r == reason else 0, 1, reason=r, **dims)
                if ph == "RoundPlay":
                    C.ratio("B.timer_ending_share", ctx, 1 if reason == "timer" else 0, 1, **rd(e))
        for e in ctx.e("round_finished"):
            p = P(e)
            es = p.get("early_submit")
            if isinstance(es, bool):
                C.ratio("B.early_submit_share", ctx, 1 if es else 0, 1, **rd(e))
                if es:
                    C.sample("B.saved_seconds", ctx, num(p.get("saved_seconds")), **rd(e))
                    C.sample("B.carried_seconds", ctx, num(p.get("carried_seconds")), **rd(e))


def kpi_C_D(S):
    C = S.C
    for ctx in S.matches:
        rps = defaultdict(list)
        for e in ctx.e("round_player_summary"):
            rps[rkey(e)].append(e)
        psum = {rkey(e): e for e in ctx.e("phone_round_summary")}
        windows = round_windows(ctx)
        calls = derive_calls(ctx)
        iv = player_round_intervals(ctx, calls)
        for k in sorted(set(rps) | set(psum), key=lambda x: (x[0] if x[0] is not None else -1, x[1] if x[1] is not None else -1)):
            ref = psum.get(k) or rps[k][0]
            dims = rd(ref)
            if k in psum:
                p = P(psum[k])
                obs, part, elig = p.get("observed"), p.get("participating"), p.get("eligible")
                if islot(obs) is not None and islot(part) is not None and obs > 0:
                    C.ratio("C.phone_participation", ctx, part, obs, **dims)
                if islot(elig) is not None and islot(obs) is not None and elig > 0:
                    C.ratio("C.observed_share", ctx, obs, elig, **dims)
            elif k in rps:
                obs_slots = [islot(P(e).get("player")) for e in rps[k] if islot(P(e).get("player")) is not None]
                if obs_slots:
                    if k in windows and calls:
                        lo, hi = windows[k]
                        n = sum(1 for s in obs_slots if clip_intervals(iv.get(s, []), lo, hi))
                    else:
                        known = [P(e).get("calls_connected") for e in rps[k] if islot(P(e).get("player")) is not None]
                        if any(isinstance(x, int) for x in known):
                            n = sum(1 for x in known if isinstance(x, int) and x > 0)
                        else:
                            continue
                    C.ratio("C.phone_participation", ctx, n, len(obs_slots), **dims)
        # derived connected seconds
        if windows and calls:
            for (k, slot), sec in derived_round_connected(ctx).items():
                if any(islot(P(e).get("player")) == slot for e in rps.get(k, [])):
                    C.sample("D.connected_seconds_derived", ctx, sec, day=k[0], round=k[1])
        for k, es in rps.items():
            for e in es:
                p = P(e)
                d = rd(e)
                if islot(p.get("player")) is None:
                    continue
                lat = num(p.get("first_contact_latency_seconds"))
                C.sample("D.first_contact_latency", ctx, lat, **d)
                C.ratio("D.no_contact_share", ctx, 1 if lat is None else 0, 1, **d)
                C.sample("D.connected_seconds", ctx, num(p.get("connected_seconds")), **d)
                C.sample("D.unique_partners", ctx, num(p.get("unique_partners")), **d)
        # calls (each call once; each call once per participant)
        for c in calls:
            if c["dur"] is not None:
                C.sample("D.call_duration", ctx, c["dur"])
                for s in (c["a"], c["b"]):
                    if s is not None:
                        C.sample("D.call_duration_participant", ctx, c["dur"])
        w = defaultdict(lambda: defaultdict(float))
        cnt = defaultdict(lambda: defaultdict(int))
        for c in calls:
            if c["a"] is None or c["b"] is None:
                continue
            for me, other in ((c["a"], c["b"]), (c["b"], c["a"])):
                cnt[me][other] += 1
                w[me][other] += c["dur"] or 0.0
        for me, per in cnt.items():
            if sum(per.values()) >= 2:
                weights = list(w[me].values()) if sum(w[me].values()) > 0 else list(per.values())
                C.sample("D.partner_hhi", ctx, hhi(weights))
        req = ctx.e("phone_call_requested")
        if req:
            n = len(req)
            failed = ctx.e("phone_call_failed")
            reasons = Counter(str(P(e).get("reason")) for e in failed)
            C.ratio("D.busy_failure_rate", ctx, reasons.get("busy", 0), n)
            for r in sorted(set(reasons) | {"busy", "dead_line", "absent", "wrong_phase"}):
                C.ratio("D.call_failure_reason", ctx, reasons.get(r, 0), n, reason=r)
            C.ratio("D.unanswered_rate", ctx, len(ctx.e("phone_call_missed")) + len(ctx.e("phone_call_rejected")), n)
            C.ratio("K.phone_failure_nonbusy", ctx, sum(v for r, v in reasons.items() if r != "busy"), n)
            attempted = {islot(P(e).get("caller")) for e in req} - {None}
            connected = set()
            for c in calls:
                connected.update(s for s in (c["a"], c["b"]) if s is not None)
            for e in ctx.e("phone_call_connected"):
                connected.update(s for s in (islot(P(e).get("caller")), islot(P(e).get("callee"))) if s is not None)
            if attempted:
                C.ratio("D.attempt_never_connect", ctx, len(attempted - connected), len(attempted))


def kpi_E_F_G(S):
    C = S.C
    quality = defaultdict(lambda: defaultdict(list))  # (group,strat) -> mode -> [q]
    reps = {}
    for ctx in S.matches:
        outcomes = {(rkey(e), islot(P(e).get("player"))): P(e) for e in ctx.e("choice_outcome_resolved")}
        modes = defaultdict(list)
        for e in ctx.e("round_player_summary"):
            p = P(e)
            slot = islot(p.get("player"))
            d = rd(e)
            if slot is None:
                continue
            for mid, f, kind in (("F.longest_gap", "longest_gap_seconds", "s"), ("F.busy_failures", "busy_failures", "s"),
                                 ("F.blocked_seconds", "blocked_seconds", "s")):
                C.sample(mid, ctx, num(p.get(f)), **d)
            if num(p.get("blocked_seconds")) is not None:
                C.ratio("F.blocked_share", ctx, 1 if p["blocked_seconds"] > 0 else 0, 1, **d)
            if p.get("submitted") is True and num(p.get("post_submit_wait_seconds")) is not None:
                C.sample("F.post_submit_wait", ctx, p["post_submit_wait_seconds"], **d)
                C.ratio("F.post_submit_wait_over20", ctx, 1 if p["post_submit_wait_seconds"] >= 20 else 0, 1, **d)
            mode = p.get("comm_mode")
            if isinstance(mode, str):
                modes[slot].append(mode)
                for m in sorted(set(COMM_MODES) | {mode}):
                    C.ratio("E.mode_share", ctx, 1 if m == mode else 0, 1, comm_mode=m, **d)
                C.sample("E.revisions", ctx, num(p.get("revisions")), comm_mode=mode, **d)
                o = outcomes.get((rkey(e), slot))
                if o:
                    q = num(o.get("quality"))
                    C.sample("E.quality", ctx, q, comm_mode=mode, **d)
                    C.sample("E.income", ctx, num(o.get("income")), comm_mode=mode, **d)
                    if o.get("cooperation_opportunity") is True:
                        C.ratio("E.cooperation_rate", ctx, 1 if o.get("cooperation_credit") is True else 0, 1, comm_mode=mode, **d)
                    if q is not None:
                        gk = (ctx.group, ctx.strat)
                        quality[gk]["none" if mode == "none" else "comm"].append(q)
                        reps.setdefault(gk, ctx)
        by_round = defaultdict(list)
        for e in ctx.e("round_player_summary"):
            s = islot(P(e).get("player"))
            if s is not None:
                by_round[s].append(e)
        gaps = []
        for s, es in by_round.items():
            g = [num(P(e).get("longest_gap_seconds")) for e in es if num(P(e).get("longest_gap_seconds")) is not None]
            if g:
                gaps.append(mean(g))
        if gaps:
            C.sample("F.worst_served_gap", ctx, max(gaps))
        # coverage of summaries vs connected players
        conn = sum(num(P(e).get("connected")) or 0 for e in ctx.e("round_finished"))
        if conn > 0 and ctx.has("round_player_summary"):
            C.ratio("F.observation_coverage", ctx, len(ctx.e("round_player_summary")), conn)
        # career by communication profile
        if not company_closed(ctx):
            for e in ctx.e("career_breakdown"):
                p = P(e)
                slot = islot(p.get("player"))
                if slot in modes and modes[slot] and num(p.get("total")) is not None:
                    silent = sum(1 for m in modes[slot] if m == "none") / len(modes[slot])
                    C.sample("E.career_total", ctx, p["total"], profile="mostly_silent" if silent >= 0.5 else "communicating")
        # G
        subs = ctx.e("choice_submitted")
        for e in subs:
            p = P(e)
            C.sample("G.latency", ctx, num(p.get("latency_seconds")), after_contact=p.get("after_contact"),
                     contact_channel=p.get("contact_channel"), **rd(e))
        for t in sorted({e.get("task_id") for e in subs if e.get("task_id")}):
            ts = [e for e in subs if e.get("task_id") == t]
            ch = [e for e in ctx.e("choice_changed") if e.get("task_id") == t]
            C.ratio("G.revisions_per_submit", ctx, len(ch), len(ts), task_id=t)
        if subs:
            ac = [P(e).get("after_contact") for e in subs if isinstance(P(e).get("after_contact"), bool)]
            if ac:
                C.ratio("G.after_contact_share", ctx, sum(1 for x in ac if x), len(ac))
            to = Counter(str(P(e).get("reason")) for e in ctx.e("choice_timed_out"))
            for r in sorted(set(to) | {"missed", "blocked", "absent"}):
                C.ratio("G.timeout_share", ctx, to.get(r, 0), len(subs) + sum(to.values()), reason=r)
            for t in sorted({e.get("task_id") for e in subs if e.get("task_id")}):
                ts = [P(e).get("category") for e in subs if e.get("task_id") == t and isinstance(P(e).get("category"), int)]
                for cat in sorted(set(ts) | set(S.cat_universe.get(t, ()))):
                    C.ratio("G.category_share", ctx, sum(1 for x in ts if x == cat), len(ts), task_id=t, category=cat)
        if has_client(ctx) and ctx.has("round_player_summary"):
            C.ratio("G.invalid_actions", ctx, len(ctx.e("ui_action_rejected")) + len(ctx.e("message_rejected")), len(ctx.e("round_player_summary")))
        for e in ctx.e("fault_points_changed"):
            p = P(e)
            C.sample("G.blame_delta", ctx, num(p.get("delta")), reason=p.get("reason"))
    for gk, c in reps.items():
        q = quality[gk]
        if len(q["none"]) >= 3 and len(q["comm"]) >= 3:
            C.ratio("E.silent_not_dominant", c, 1 if mean(q["none"]) > mean(q["comm"]) else 0, 1)


def kpi_H(S):
    C = S.C
    for ctx in S.matches:
        starts = ctx.e("recording_playback_started")
        for e in starts:
            p = P(e)
            ct = p.get("context_task")
            C.ratio("H.context_share", ctx, 1 if ct not in (None, "", "none") else 0, 1)
            if num(p.get("prior_exposures")) is not None:
                C.ratio("H.prior_exposure_share", ctx, 1 if p["prior_exposures"] > 0 else 0, 1)
            if isinstance(p.get("same_group"), bool):
                C.ratio("H.same_group_share", ctx, 1 if p["same_group"] else 0, 1)
            if num(p.get("snippet_ms")) and num(p.get("speech_ms")) is not None and p["snippet_ms"] > 0:
                C.sample("H.speech_density", ctx, p["speech_ms"] / p["snippet_ms"])
        fins = ctx.e("recording_playback_finished")
        fr = Counter(str(P(e).get("failure")) for e in fins)
        for e in fins:
            p = P(e)
            if isinstance(p.get("played"), bool):
                C.ratio("H.playback_success", ctx, 1 if p["played"] else 0, 1)
                if p["played"]:
                    C.sample("H.actual_duration", ctx, num(p.get("actual_duration_seconds")))
        for f in sorted(set(fr) | {"none", "no_candidates", "no_speech", "disabled", "voice_unavailable", "stopped"}):
            if fins:
                C.ratio("H.playback_failure", ctx, fr.get(f, 0), len(fins), failure=f)
        if starts and ctx.pc:
            cnt = Counter()
            for e in starts:
                for k in ("caller", "callee"):
                    s = islot(P(e).get(k))
                    if s is not None:
                        cnt[s] += 1
            if cnt:
                C.ratio("H.participant_coverage", ctx, len(cnt), ctx.pc)
                C.ratio("H.repeat_exposed_players", ctx, sum(1 for v in cnt.values() if v >= 2), len(cnt))
        # warning meetings
        last = {}
        for e in ctx.e("meeting_vote_submitted"):
            last[(islot(P(e).get("voter")), e.get("day_index"))] = P(e).get("target")
        for tgt in last.values():
            C.ratio("H.abstain_share", ctx, 1 if tgt == "abstain" else 0, 1)
        res = ctx.e("meeting_vote_resolved")
        outs = S.out_universe
        for e in res:
            p = P(e)
            el, vo = num(p.get("eligible")), num(p.get("voted"))
            if el and vo is not None:
                C.ratio("H.vote_participation", ctx, vo, el)
            if vo:
                C.ratio("H.no_action_vote_share", ctx, num(p.get("no_action_votes")) or 0, vo)
            o = p.get("outcome")
            if o is not None:
                for oo in sorted(set(outs) | {str(o)}):
                    C.ratio("H.vote_outcome", ctx, 1 if oo == o else 0, 1, outcome=oo, **{"day": e.get("day_index")} if e.get("day_index") is not None else {})
        for e in ctx.e("meeting_discussion_finished"):
            C.sample("H.discussion_duration", ctx, num(P(e).get("duration_seconds")), **({"day": e.get("day_index")} if e.get("day_index") is not None else {}))
        # final
        fres = ctx.e("final_vote_resolved")
        flast = {}
        for e in ctx.e("final_vote_submitted"):
            flast[islot(P(e).get("voter"))] = P(e).get("target")
        for e in fres:
            p = P(e)
            roster = num(p.get("roster"))
            if roster:
                C.ratio("H.final_fire_nobody_share", ctx, num(p.get("fire_nobody_votes")) or 0, roster)
                if flast:
                    C.ratio("H.final_participation", ctx, sum(1 for t in flast.values() if t != "abstain"), roster)
                    C.ratio("H.final_abstain_share", ctx, sum(1 for t in flast.values() if t == "abstain"), roster)
            o = p.get("outcome")
            if o is not None:
                for oo in sorted({"fired", "no_majority", "no_votes", "company_closed", str(o)}):
                    C.ratio("H.final_outcome", ctx, 1 if oo == o else 0, 1, outcome=oo)


def kpi_I(S):
    C = S.C
    for ctx in S.matches:
        roster = roster_slots(ctx)
        n = len(roster)
        # blame snapshots at day end
        state = {s: START_BLAME for s in roster}
        for e in ctx.all:
            nm = e["event"]
            if nm == "fault_points_changed":
                p = P(e)
                s = islot(p.get("player"))
                if s is not None and num(p.get("blame_after")) is not None:
                    state[s] = p["blame_after"]
                    if s not in roster:
                        roster = sorted(set(roster) | {s})
                        n = len(roster)
            elif nm == "day_finished" and ctx.has("fault_points_changed") and n >= 2:
                vals = [state.get(s, START_BLAME) for s in roster]
                d = {"day": e.get("day_index")} if e.get("day_index") is not None else {}
                C.sample("I.blame_spread", ctx, max(vals) - min(vals), **d)
                if sum(vals) > 0:
                    C.sample("I.blame_top_share", ctx, max(vals) / sum(vals), **d)
                    C.sample("I.blame_top_share_baseline", ctx, 1.0 / n, **d)
        fc = ctx.e("fault_points_changed")
        if fc:
            cnt = Counter(str(P(e).get("reason")) for e in fc)
            for r in sorted(set(cnt) | set(S.universe("fault_points_changed", "reason"))):
                C.ratio("I.change_reason", ctx, cnt.get(r, 0), len(fc), reason=r)
        opp = ctx.e("recovery_opportunities")
        if opp:
            withopt = sum(num(P(e).get("players_with_options")) or 0 for e in opp)
            if withopt > 0:
                C.ratio("I.recovery_use", ctx, len(ctx.e("remedial_requested")), withopt)
        res = ctx.e("remedial_resolved")
        for e in res:
            p = P(e)
            if isinstance(p.get("accepted"), bool):
                C.ratio("I.recovery_accept", ctx, 1 if p["accepted"] else 0, 1)
                if p["accepted"]:
                    C.sample("I.recovery_output_added", ctx, num(p.get("output_added")))
        # repeat warnings and cap
        counts = Counter()
        meet = None
        meetings = []
        for e in ctx.all:
            nm = e["event"]
            if nm == "meeting_vote_resolved":
                if meet:
                    meetings.append(meet)
                p = P(e)
                o = str(p.get("outcome", ""))
                meet = {"elig": any(v == 1 for v in counts.values()), "repeat": False, "capok": any(v >= WARNING_CAP for v in counts.values()),
                        "capped": "cap" in o, "day": e.get("day_index")}
                if isinstance(p.get("target_was_blame_leader"), bool):
                    C.ratio("I.target_was_blame_leader", ctx, 1 if p["target_was_blame_leader"] else 0, 1)
            elif nm == "warning_assigned":
                p = P(e)
                t = islot(p.get("target"))
                prior = counts.get(t, 0)
                counts[t] = max(prior + 1, int(p.get("warnings_after") or 0))
                if meet is not None and prior >= 1:
                    meet["repeat"] = True
        if meet:
            meetings.append(meet)
        for m in meetings:
            d = {"day": m["day"]} if m["day"] is not None else {}
            if m["elig"]:
                C.ratio("I.repeat_warning", ctx, 1 if m["repeat"] else 0, 1, **d)
            if m["capok"]:
                C.ratio("I.nomination_capped", ctx, 1 if m["capped"] else 0, 1, **d)
        tot = sum(counts.values())
        if tot >= 2 and n >= 2:
            C.sample("I.warning_top_share", ctx, max(counts.values()) / tot)
            C.sample("I.warning_top_share_baseline", ctx, 1.0 / n)


def kpi_J(S):
    C = S.C
    for ctx in S.matches:
        roster = roster_slots(ctx)
        n = len(roster)
        flushed = private_flushed(ctx)
        inc = defaultdict(float)
        for e in ctx.e("income_credited"):
            s = islot(P(e).get("player"))
            if s is not None and num(P(e).get("amount")) is not None:
                inc[s] += P(e)["amount"]
        if inc and n >= 2:
            vals = [inc.get(s, 0.0) for s in roster]
            g = gini(vals)
            C.sample("J.income_gini", ctx, g)
            if sum(vals) > 0:
                C.sample("J.income_top_share", ctx, max(vals) / sum(vals))
                C.sample("J.income_top_share_baseline", ctx, 1.0 / n)
        buys = Counter()
        spend = 0.0
        for e in ctx.e("evidence_purchased"):
            s = islot(P(e).get("buyer"))
            if s is not None:
                buys[s] += 1
                spend += num(P(e).get("price")) or 0
        if flushed and ctx.slotmap:
            C.sample("J.evidence_purchases", ctx, sum(buys.values()))
            C.sample("J.evidence_spend", ctx, spend)
            C.sample("J.transfers", ctx, len(ctx.e("money_transferred")))
            if buys and n >= 2:
                vals = [buys.get(s, 0) for s in roster]
                C.sample("J.evidence_gini", ctx, gini(vals))
                C.sample("J.evidence_top_share", ctx, max(vals) / sum(vals))
        for e in ctx.e("money_transferred"):
            C.sample("J.transfer_amount", ctx, num(P(e).get("amount")))
        if flushed:
            created = Counter(str(P(e).get("kind")) for e in ctx.e("incident_created"))
            ver = Counter(str(P(e).get("kind")) for e in ctx.e("sabotage_verified"))
            for k in sorted(set(created) | set(ver) | set(S.universe("incident_created", "kind"))):
                if created.get(k, 0) or ver.get(k, 0):
                    C.ratio("J.sabotage_verified_rate", ctx, ver.get(k, 0), created.get(k, 0), kind=k)
        for e in ctx.e("sabotage_verified"):
            C.sample("J.sabotage_verify_delay", ctx, num(P(e).get("seconds_since_incident")))
        for e in ctx.e("company_result"):
            p = P(e)
            if num(p.get("incidents")) is not None and p["incidents"] > 0:
                C.ratio("J.exposed_incident_share", ctx, num(p.get("exposed_incidents")) or 0, p["incidents"])
            if isinstance(p.get("closed"), bool):
                C.ratio("J.company_closed_rate", ctx, 1 if p["closed"] else 0, 1)
            C.sample("J.final_productivity", ctx, num(p.get("final_permille")))
        outs = ctx.e("task_output_resolved")
        mx = sum(num(P(e).get("max_output")) or 0 for e in outs)
        if mx > 0:
            C.ratio("J.sabotage_loss_share", ctx, sum(num(P(e).get("sabotage_loss")) or 0 for e in outs), mx)
            lost = sum((num(P(e).get("max_output")) or 0) - (num(P(e).get("output")) or 0) for e in outs)
            if lost > 0 and ctx.has("remedial_resolved"):
                added = sum(num(P(e).get("output_added")) or 0 for e in ctx.e("remedial_resolved") if P(e).get("accepted") is True)
                C.ratio("J.remedial_recovery_share", ctx, added, lost)
        for e in ctx.e("objective_completed"):
            if isinstance(P(e).get("completed"), bool):
                C.ratio("J.objective_completion", ctx, 1 if P(e)["completed"] else 0, 1, kind=P(e).get("kind"))
        cb = ctx.e("career_breakdown")
        for e in cb:
            for comp in CAREER_COMPONENTS:
                C.sample("J.career_component", ctx, num(P(e).get(comp)), component=comp)
        rows = [P(e) for e in cb if isinstance(P(e).get("rank"), int) and not isinstance(P(e).get("rank"), bool)]
        if len(rows) >= 2 and not company_closed(ctx):
            r1 = {islot(p.get("player")) for p in rows if p["rank"] == 1}
            for key, mid in (("money_end", "J.rank1_is_richest"), ("quality", "J.rank1_is_best_quality")):
                vals = {islot(p.get("player")): num(p.get(key)) for p in rows if num(p.get(key)) is not None}
                if len(vals) >= 2:
                    top = {s for s, v in vals.items() if v == max(vals.values())}
                    C.ratio(mid, ctx, 1 if (r1 & top) else 0, 1)
                    C.ratio(mid + "_baseline", ctx, len(top) / len(vals), 1)


def kpi_K(S):
    C = S.C
    cats = ["engine_exception", "rpc_decode", "voice_exception", "sink_error", "ui_exception"]
    for ctx in S.ctxs:
        if ctx.is_match and (ctx.has("match_started") or has_client(ctx)):
            seen = {str(P(e).get("category")) for e in ctx.e("technical_failure")}
            for cat in sorted(set(cats) | seen):
                C.ratio("K.technical_failure_match", ctx, 1 if cat in seen else 0, 1, category=cat)
        att = len(ctx.e("reconnect_attempted"))
        ok, bad = len(ctx.e("reconnect_succeeded")), len(ctx.e("reconnect_failed"))
        if ok + bad > 0:
            C.ratio("K.reconnect_success", ctx, ok, ok + bad)
        elif att > 0:
            pass  # attempts without an observed outcome stay unknown
        for e in ctx.e("player_reconnected"):
            C.sample("K.reconnect_downtime", ctx, num(P(e).get("downtime_seconds")))
        for e in ctx.e("voice_quality_summary"):
            p = P(e)
            C.sample("K.voice_loss", ctx, num(p.get("loss_pct")))
            C.sample("K.voice_jitter", ctx, num(p.get("jitter_ms")))
            C.sample("K.voice_underruns", ctx, num(p.get("underruns")))
        if ctx.is_match and ctx.has("match_started"):
            C.sample("K.stuck_phase", ctx, len(ctx.e("stuck_phase")))
            C.sample("K.unexpected_transition", ctx, len(ctx.e("unexpected_transition")))
        if ctx.has("session_started"):
            ended = ctx.has("session_ended") or ctx.has("session_interrupted")
            C.ratio("K.possible_crash", ctx, 0 if ended else 1, 1)


def kpi_L_M(S):
    C = S.C
    by_gs = defaultdict(list)
    for c in S.matches:
        by_gs[(c.group, c.strat)].append(c)
    moderator = set()
    for r in S.observer:
        if yn(r.get("moderator_required_repeat")) is True and r.get("match_id"):
            moderator.add(r["match_id"])
    nxt = {}
    for c in S.matches:
        for e in c.e("rematch_started"):
            p = P(e)
            if p.get("previous_match_id"):
                nxt[p["previous_match_id"]] = (bool(p.get("auto")), int(num(p.get("requests")) or 0))
    group_has_prompt = {g: any(c.has("rematch_prompt_shown") for c in cs) for (g, _s), cs in by_gs.items()}
    for g, st, rep, cs in group_rep(S, by_gs):
        opp = []
        intent = False
        vol = False
        for c in cs:
            if c.key in moderator:
                continue
            nx = nxt.get(c.key)
            if nx and nx[0]:
                continue  # auto restart: no genuine choice
            genuine = c.has("rematch_prompt_shown")
            if not genuine and not group_has_prompt.get(g):
                humans = max([num(P(e).get("humans")) or 0 for e in c.e("match_started")] or [0])
                genuine = c.has("week_completed") and (humans >= 3 or (humans == 0 and (c.pc or 0) >= 3))
            if not genuine:
                continue
            opp.append(c)
            if c.has("rematch_requested") or (nx and nx[1] >= 1):
                intent = True
            if nx and not nx[0] and nx[1] >= 1:
                vol = True
        if opp:
            C.ratio("L.voluntary_rematch", rep, 1 if vol else 0, 1)
            C.ratio("L.rematch_intent", rep, 1 if intent else 0, 1)
            if intent:
                C.ratio("L.intent_to_rematch", rep, 1 if vol else 0, 1)
    # M (1) mechanical, (2) player-marked
    for c in S.matches:
        if c.has("week_completed"):
            for ev in MECHANICAL:
                C.sample("M.mechanical_events", c, len(c.e(ev)), event=ev)
    marks_by = defaultdict(list)
    for c in S.matches:
        if has_client(c):
            marks = c.e("moment_marked")
            C.sample("M.player_marked_per_match", c, len(marks))
            marks_by[(c.group, c.strat)].append(c)
            cats = Counter(str(P(e).get("category")) for e in marks)
            if marks:
                for cat in sorted(set(cats) | {"funny", "betrayal", "accusation", "surprise", "reveal", "other"}):
                    C.ratio("M.player_marked_category", c, cats.get(cat, 0), len(marks), category=cat)
    for g, st, rep, cs in group_rep(S, marks_by):
        C.ratio("M.player_marked_groups", rep, 1 if any(c.has("moment_marked") for c in cs) else 0, 1)
    for c in S.matches:
        if c.has("match_started"):
            observed = c.key in S.observed_matches
            C.ratio("M.observation_coverage", c, 1 if observed else 0, 1)


def kpi_observer_feedback(S):
    C = S.C
    # observer rows
    per_group = defaultdict(list)
    for r, ctx in S.obs_pairs:
        per_group[(ctx.group, ctx.strat)].append((r, ctx))
        for flag, mid, _k, _n in OBS_FIELDS:
            v = yn(r.get(flag))
            if v is None:
                continue
            if mid in ("O.accusation_evidence", "O.accusation_reconsidered"):
                if yn(r.get("natural_accusation_occurred")) is not True:
                    continue
            C.ratio(mid, ctx, 1 if v else 0, 1)
    by_match = defaultdict(list)
    for r, ctx in S.obs_pairs:
        by_match[ctx.key].append((r, ctx))

    def notable(r):
        return any(yn(r.get(f)) is True for f in ("funny_betrayal", "unexpected_accusation", "surprising_outcome", "notable_funny_moment"))
    for _k, lst in by_match.items():
        C.sample("M.notable_per_match", lst[0][1], sum(1 for r, _c in lst if notable(r)))
    cat_names = ["funny_betrayal", "unexpected_accusation", "surprising_outcome", "notable_funny_moment"]
    for r, ctx in S.obs_pairs:
        if not notable(r):
            continue
        for cat in cat_names:
            C.ratio("M.notable_category", ctx, 1 if yn(r.get(cat)) is True else 0, 1, category=cat)
        day = str(r.get("day_index")) if not blank(r.get("day_index")) else None
        phase = r.get("phase") if not blank(r.get("phase")) else None
        if day:
            for dd in sorted(S.obs_days | {day}):
                C.ratio("M.notable_day", ctx, 1 if dd == day else 0, 1, day=dd)
        if phase:
            for pp in sorted(S.obs_phases | {phase}):
                C.ratio("M.notable_phase", ctx, 1 if pp == phase else 0, 1, phase=pp)
    for (g, st), lst in sorted(per_group.items()):
        rep = lst[0][1]
        obs_matches = {c.key for _r, c in lst}
        logged = [c for c in S.matches if c.group == g and c.strat == st and c.has("match_started")]
        adequate = (len([c for c in logged if c.key in obs_matches]) / len(logged) >= 0.5) if logged else True
        if adequate:
            C.ratio("M.groups_notable", rep, 1 if any(notable(r) for r, _c in lst) else 0, 1)
    # feedback
    for r, ctx in S.fb_pairs:
        for qid, _k, _t in FB_QUESTIONS:
            a = (r.get(qid) or "").strip()
            if not a:
                continue
            for ans in sorted(set(FB_ANSWERS) | {a}):
                C.ratio("FB." + qid, ctx, 1 if ans == a else 0, 1, answer=ans)
            if qid == "q_wanted_to_share":
                for ans in sorted(set(FB_ANSWERS) | {a}):
                    C.ratio("M.share_interest", ctx, 1 if ans == a else 0, 1, answer=ans)


def prepare(S):
    """Derived universes and observer/feedback linkage; must run before the kpi_* functions."""
    cu = defaultdict(set)
    for c in S.matches:
        for e in c.e("choice_submitted"):
            if e.get("task_id") and isinstance(P(e).get("category"), int):
                cu[e["task_id"]].add(P(e)["category"])
    S.cat_universe = cu
    outs = set()
    for c in S.matches:
        for e in c.e("meeting_vote_resolved"):
            if P(e).get("outcome") is not None:
                outs.add(str(P(e)["outcome"]))
    S.out_universe = sorted(outs)
    S.obs_pairs, S.fb_pairs = [], []
    S.observed_matches = set()
    S.obs_days, S.obs_phases = set(), set()
    for r in S.observer:
        ctx = link_row(S, r)
        if ctx is None:
            continue
        S.obs_pairs.append((r, ctx))
        S.observed_matches.add(ctx.key)
        S.strat_events[ctx.strat].add("observer")
        if not blank(r.get("day_index")):
            S.obs_days.add(str(r["day_index"]))
        if not blank(r.get("phase")):
            S.obs_phases.add(r["phase"])
    for r in S.feedback:
        ctx = link_row(S, r)
        if ctx is None:
            continue
        S.fb_pairs.append((r, ctx))
        S.strat_events[ctx.strat].add("feedback")
    for st in {c.strat for _r, c in S.obs_pairs + S.fb_pairs}:
        if st not in S.strata:
            S.strata.append(st)
    if S.strata and ("none", "none") in S.strata and len(S.strata) > 1:
        S.strata.remove(("none", "none"))
    S.strata.sort()


def link_row(S, r):
    """Join an observer/feedback row to a log context via match_id, else group (+ day/round) else a pseudo context."""
    mid = (r.get("match_id") or "").strip()
    linked = S.all_ctx.get(mid) if mid else None
    if linked is None and not mid:
        cands = [c for c in S.all_ctx.values() if c.is_match and c.group == (r.get("anonymous_group_id") or "").strip()]
        if len(cands) == 1:
            linked = cands[0]
    if linked is not None:
        if is_synthetic_class(linked.klass) and not S.include_synthetic:
            return None
        return linked
    g = (r.get("anonymous_group_id") or "").strip() or "ungrouped"
    try:
        pc = int(r.get("player_count")) if not blank(r.get("player_count")) else None
    except ValueError:
        pc = None
    c = pseudo_ctx(S, mid or "obs:" + g, g, (r.get("build_version") or "").strip() or None,
                   (r.get("ruleset_version") or "").strip() or None, pc)
    if is_synthetic_class(c.klass) and not S.include_synthetic:
        return None
    return c


def run_kpis(S):
    prepare(S)
    for fn in (kpi_A, kpi_B, kpi_C_D, kpi_E_F_G, kpi_H, kpi_I, kpi_J, kpi_K, kpi_L_M, kpi_observer_feedback):
        fn(S)


# =====================================================================================================================
# observer / feedback CSV
# =====================================================================================================================
OBSERVER_COLUMNS = ["anonymous_group_id", "build_version", "ruleset_version", "player_count", "match_id", "day_index",
                    "round_index", "phase", "moment_timestamp", "natural_accusation_occurred",
                    "accusation_referenced_game_evidence", "recording_was_intelligible", "recording_was_relevant",
                    "recording_lacked_context", "recording_created_reaction", "recording_was_referenced_later",
                    "players_reconsidered_accusation", "players_appeared_confused", "players_appeared_excluded",
                    "meeting_felt_too_short", "meeting_felt_too_long", "funny_betrayal", "unexpected_accusation",
                    "surprising_outcome", "notable_funny_moment", "players_requested_rematch"]
OBSERVER_EXTRA = ["observer_id", "coding_confidence", "moderator_required_repeat"]
FEEDBACK_COLUMNS = ["anonymous_group_id", "anonymous_player_id", "match_id", "player_count", "ruleset_version", "build_version"] + [q[0] for q in FB_QUESTIONS]


def blank(v):
    return v is None or str(v).strip() == ""


def read_csv_rows(paths, allowed, warnings, label):
    rows = []
    for p in paths:
        try:
            with open(p, "r", encoding="utf-8-sig", newline="") as f:
                rd_ = csv.DictReader(f)
                head = [(h or "").strip().lower() for h in (rd_.fieldnames or [])]
                dropped = [h for h in head if h and h not in allowed]
                if dropped:
                    warnings.append("%s %s: ignored %d unknown column(s) (free text is never parsed): %s" % (label, os.path.basename(p), len(dropped), ", ".join(dropped[:6])))
                for raw in rd_:
                    row = {(k or "").strip().lower(): (v or "").strip() for k, v in raw.items() if k and (k or "").strip().lower() in allowed}
                    if any(v for v in row.values()):
                        rows.append(row)
        except OSError as ex:
            warnings.append("%s %s unreadable: %s" % (label, p, ex))
    return rows


# =====================================================================================================================
# group comparison, funnels
# =====================================================================================================================
def _recs(S, mid, g, st):
    return [r for r in S.C.rec.get(mid, []) if r.ctx.group == g and r.ctx.strat == st]


def gval(S, mid, g, st, stat="value"):
    rs = _recs(S, mid, g, st)
    if not rs:
        return None
    sp = SPECS[mid]
    if sp.kind == "sample":
        xs = [r.a for r in rs]
        return {"value": mean(xs), "median": median(xs), "n": len(xs)}.get(stat) if stat != "n" else len(xs)
    d = sum(r.b for r in rs)
    if stat == "n":
        return d
    return sum(r.a for r in rs) / d if d > 0 else None


GROUP_COLUMNS = ["anonymous_group_id", "build_version", "ruleset_version", "data_class", "player_counts", "n_matches_logged",
                 "matches_completed", "matches_abandoned", "matches_unknown_end", "completion_rate", "phone_participation",
                 "phone_participation_n", "no_contact_share", "first_contact_latency_median_s", "longest_gap_mean_s",
                 "post_submit_wait_mean_s", "worst_served_gap_s", "voluntary_rematch", "rematch_intent", "company_closed_matches",
                 "final_productivity_permille_mean", "technical_failure_matches", "observer_rows", "notable_moments_confirmed",
                 "feedback_rows", "observation_coverage_mean", "status"]


def group_rows(S):
    out = []
    keys = sorted({(c.group, c.strat) for c in S.ctxs if c.is_match} | {(c.group, c.strat) for c, _ in [(c, 0) for _r, c in S.obs_pairs + S.fb_pairs]})
    for g, st in keys:
        cs = [c for c in S.matches if c.group == g and c.strat == st]
        started = [c for c in cs if c.has("match_started")]
        comp = sum(1 for c in started if c.has("week_completed"))
        ab = sum(1 for c in started if not c.has("week_completed") and c.has("match_abandoned"))
        unk = len(started) - comp - ab
        orows = [r for r, c in S.obs_pairs if c.group == g and c.strat == st]
        notable = sum(1 for r in orows if any(yn(r.get(f)) is True for f in ("funny_betrayal", "unexpected_accusation", "surprising_outcome", "notable_funny_moment")))
        fbrows = sum(1 for _r, c in S.fb_pairs if c.group == g and c.strat == st)
        covs = [c.coverage for c in cs if c.coverage is not None]
        pcs = sorted({c.pc for c in cs if c.pc})
        klass = sorted({c.klass for c in cs}) or ["human"]
        tf = sum(1 for c in cs if c.has("technical_failure"))
        row = {"anonymous_group_id": g, "build_version": st[0], "ruleset_version": st[1], "data_class": "+".join(klass),
               "player_counts": "/".join(str(p) for p in pcs), "n_matches_logged": len(cs), "matches_completed": comp,
               "matches_abandoned": ab, "matches_unknown_end": unk,
               "completion_rate": (comp / (comp + ab)) if comp + ab > 0 else None,
               "phone_participation": gval(S, "C.phone_participation", g, st), "phone_participation_n": gval(S, "C.phone_participation", g, st, "n"),
               "no_contact_share": gval(S, "D.no_contact_share", g, st),
               "first_contact_latency_median_s": gval(S, "D.first_contact_latency", g, st, "median"),
               "longest_gap_mean_s": gval(S, "F.longest_gap", g, st), "post_submit_wait_mean_s": gval(S, "F.post_submit_wait", g, st),
               "worst_served_gap_s": gval(S, "F.worst_served_gap", g, st),
               "voluntary_rematch": gval(S, "L.voluntary_rematch", g, st), "rematch_intent": gval(S, "L.rematch_intent", g, st),
               "company_closed_matches": gval(S, "J.company_closed_rate", g, st, "n") and round(gval(S, "J.company_closed_rate", g, st) * gval(S, "J.company_closed_rate", g, st, "n")),
               "final_productivity_permille_mean": gval(S, "J.final_productivity", g, st),
               "technical_failure_matches": tf if cs else None, "observer_rows": len(orows) if S.observer else None,
               "notable_moments_confirmed": notable if orows else None, "feedback_rows": fbrows if S.feedback else None,
               "observation_coverage_mean": mean(covs) if covs else None,
               "status": "ok" if len(started) >= 1 else "insufficient_data"}
        out.append(row)
    return out


def write_group_csv(rows, path):
    with open(path, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(GROUP_COLUMNS)
        for r in rows:
            w.writerow([_fmt(r.get(c)) for c in GROUP_COLUMNS])


FUNNEL_COLUMNS = ["funnel", "build_version", "ruleset_version", "stage_order", "stage", "unit", "count", "share_of_first", "share_of_previous", "n_groups"]


def funnels(S):
    out = []
    for st in S.strata:
        ms = [c for c in S.matches if c.strat == st]
        stages = [("match_started", lambda c: c.has("match_started")), ("round_finished (>=1)", lambda c: c.has("round_finished")),
                  ("meeting_started (>=1)", lambda c: c.has("meeting_started")), ("final_started", lambda c: c.has("final_started")),
                  ("week_completed", lambda c: c.has("week_completed"))]
        prev = first = None
        for i, (name, fn) in enumerate(stages):
            sel = [c for c in ms if fn(c)]
            n = len(sel)
            first = n if first is None else first
            out.append({"funnel": "match_progress", "build_version": st[0], "ruleset_version": st[1], "stage_order": i, "stage": name,
                        "unit": "matches", "count": n, "share_of_first": (n / first) if first else None,
                        "share_of_previous": (n / prev) if prev else None, "n_groups": len({c.group for c in sel})})
            prev = n
        sc = [c for c in S.ctxs if c.strat == st]
        lob = [("lobby_join_attempted", sum(len(c.e("lobby_join_attempted")) for c in sc)),
               ("lobby_joined", sum(len(c.e("lobby_joined")) for c in sc)),
               ("player_ready (ready=true)", sum(1 for c in sc for e in c.e("player_ready_changed") if P(e).get("ready") is True)),
               ("humans in match_started", sum(int(num(P(e).get("humans")) or 0) for c in sc for e in c.e("match_started")))]
        prev = first = None
        for i, (name, n) in enumerate(lob):
            avail = {"lobby_join_attempted": "lobby_join_attempted", "lobby_joined": "lobby_joined"}
            if i < 2 and not any(c.has(avail[name]) for c in sc):
                n = None
            first = n if (first is None and n is not None) else first
            out.append({"funnel": "access", "build_version": st[0], "ruleset_version": st[1], "stage_order": i, "stage": name,
                        "unit": "attempts/players (client logs; not comparable across stages)", "count": n,
                        "share_of_first": (n / first) if (n is not None and first) else None,
                        "share_of_previous": (n / prev) if (n is not None and prev) else None,
                        "n_groups": len({c.group for c in sc})})
            prev = n if n is not None else prev
        rs = [(c, e) for c in ms for e in c.e("round_started")]
        fin = sum(1 for c, e in rs if any(rkey(f) == rkey(e) for f in c.e("round_finished")))
        out.append({"funnel": "rounds", "build_version": st[0], "ruleset_version": st[1], "stage_order": 0, "stage": "round_started",
                    "unit": "rounds", "count": len(rs), "share_of_first": 1.0 if rs else None, "share_of_previous": None,
                    "n_groups": len({c.group for c, _e in rs})})
        out.append({"funnel": "rounds", "build_version": st[0], "ruleset_version": st[1], "stage_order": 1, "stage": "round_finished",
                    "unit": "rounds", "count": fin if rs else None, "share_of_first": (fin / len(rs)) if rs else None,
                    "share_of_previous": (fin / len(rs)) if rs else None, "n_groups": len({c.group for c, e in rs if any(rkey(f) == rkey(e) for f in c.e("round_finished"))})})
    return out


def write_funnel_csv(rows, path):
    with open(path, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(FUNNEL_COLUMNS)
        for r in rows:
            w.writerow([_fmt(r.get(c)) for c in FUNNEL_COLUMNS])


# =====================================================================================================================
# SVG charts (standalone, no dependencies)
# =====================================================================================================================
def esc(s):
    return str(s).replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace('"', "&quot;")


def svg_bars(title, subtitle, items, unit_fmt, banner=None, refs=None, max_value=None):
    """Horizontal bar chart. items: [(label, value, lo, hi, note)]; refs: {label: baseline} drawn as a tick."""
    left, right, rowh, top = 190, 90, 26, 74 + (22 if banner else 0)
    width = 760
    h = top + rowh * max(len(items), 1) + 40
    vmax = max_value or max([i[3] if i[3] is not None else i[1] for i in items if i[1] is not None] + [(refs or {}).get(i[0], 0) for i in items] + [1e-9])
    if not max_value:
        vmax = vmax * 1.08 if vmax > 0 else 1
    pw = width - left - right
    o = ['<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d" viewBox="0 0 %d %d" font-family="Segoe UI, Arial, sans-serif">' % (width, h, width, h),
         '<rect width="100%" height="100%" fill="#ffffff"/>']
    y0 = 26
    if banner:
        o.append('<rect x="0" y="0" width="%d" height="24" fill="#b91c1c"/><text x="12" y="17" font-size="13" font-weight="700" fill="#ffffff">%s</text>' % (width, esc(banner)))
        y0 = 50
    o.append('<text x="12" y="%d" font-size="16" font-weight="700" fill="#111827">%s</text>' % (y0, esc(title)))
    o.append('<text x="12" y="%d" font-size="11" fill="#4b5563">%s</text>' % (y0 + 17, esc(subtitle)))
    for t in range(0, 5):
        x = left + pw * t / 4
        o.append('<line x1="%.1f" y1="%d" x2="%.1f" y2="%d" stroke="#e5e7eb"/>' % (x, top - 6, x, top + rowh * len(items)))
        o.append('<text x="%.1f" y="%d" font-size="10" fill="#6b7280" text-anchor="middle">%s</text>' % (x, top + rowh * len(items) + 14, esc(unit_fmt(vmax * t / 4))))
    if not items:
        o.append('<text x="%d" y="%d" font-size="12" fill="#6b7280">no data (not measured or insufficient)</text>' % (left, top + 18))
    for i, (label, v, lo, hi, note) in enumerate(items):
        y = top + i * rowh
        o.append('<text x="%d" y="%d" font-size="11" fill="#1f2937" text-anchor="end">%s</text>' % (left - 8, y + 15, esc(label)))
        if v is None:
            o.append('<text x="%d" y="%d" font-size="11" fill="#9ca3af">n/a</text>' % (left + 4, y + 15))
            continue
        w = max(pw * v / vmax, 1)
        o.append('<rect x="%d" y="%d" width="%.1f" height="16" rx="2" fill="#2563eb"/>' % (left, y + 4, w))
        if lo is not None and hi is not None:
            xl, xh = left + pw * lo / vmax, left + pw * hi / vmax
            o.append('<line x1="%.1f" y1="%d" x2="%.1f" y2="%d" stroke="#111827" stroke-width="1.5"/>' % (xl, y + 12, xh, y + 12))
            for xx in (xl, xh):
                o.append('<line x1="%.1f" y1="%d" x2="%.1f" y2="%d" stroke="#111827" stroke-width="1.5"/>' % (xx, y + 7, xx, y + 17))
        if refs and label in refs and refs[label] is not None:
            xr = left + pw * refs[label] / vmax
            o.append('<line x1="%.1f" y1="%d" x2="%.1f" y2="%d" stroke="#d97706" stroke-width="2.5"/>' % (xr, y + 1, xr, y + 23))
        xe = left + pw * (hi if hi is not None else v) / vmax + 6
        o.append('<text x="%.1f" y="%d" font-size="11" fill="#111827">%s %s</text>' % (min(xe, width - 150), y + 16, esc(unit_fmt(v)), esc(note)))
    o.append('<text x="12" y="%d" font-size="10" fill="#6b7280">Whiskers: group-level interval where n_groups &gt;= 3 (cluster bootstrap / Wilson). Orange tick: baseline. Exploratory evidence only.</text>' % (h - 8))
    o.append("</svg>")
    return "\n".join(o)


def _pct(v):
    return "%.0f%%" % (100 * v)


def _num(v):
    return "%.1f" % v


def get_row(index, mid, st, part="", gt="all", gv="all"):
    return index.get((mid, st, part, gt, gv))


def index_rows(rows):
    ix = {}
    for r in rows:
        ix[(r["metric_id"], (r["build_version"], r["ruleset_version"]), r["partition"], r["grouping"], r["group_value"])] = r
    return ix


def items_for(rows, mid, st, grouping, part="", fmt_note=True, order=None):
    its = []
    for r in rows:
        if r["metric_id"] == mid and (r["build_version"], r["ruleset_version"]) == st and r["grouping"] == grouping and r["partition"] == part and r["status"] != "not_measured" and r["value"] is not None:
            its.append((("%s players" % r["group_value"]) if grouping == "player_count" else str(r["group_value"]), r["value"], r["ci_low"], r["ci_high"], "(n=%g, g=%d)" % (r["denominator"], r["n_groups"])))
    if order:
        its.sort(key=order)
    else:
        its.sort(key=lambda i: (len(i[0]), i[0]))
    return its


def write_charts(S, rows, funnel_rows, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    banner = BANNER if S.has_synthetic else None
    written = []
    multi = len(S.strata) > 1
    for si, st in enumerate(S.strata):
        suf = ("_s%d" % (si + 1)) if multi else ""
        sub = "build %s | ruleset %s%s" % (st[0], st[1], "" if not S.has_synthetic else " | SYNTHETIC")

        def save(name, svg):
            p = os.path.join(out_dir, name + suf + ".svg")
            with open(p, "w", encoding="utf-8") as f:
                f.write(svg)
            written.append(p)
        fr = [r for r in funnel_rows if r["funnel"] == "match_progress" and (r["build_version"], r["ruleset_version"]) == st]
        its = [(r["stage"], r["share_of_first"], None, None, "(%s of %s)" % (r["count"], fr[0]["count"])) for r in fr if r["share_of_first"] is not None]
        save("funnel_match_progress", svg_bars("Match progress funnel", sub + " | share of started matches", its, _pct, banner, max_value=1.0))
        save("completion_by_player_count", svg_bars("Match completion by player count", sub + " | completed / (completed + abandoned)",
                                                    items_for(rows, "B.match_completion", st, "player_count"), _pct, banner, max_value=1.0))
        its = items_for(rows, "C.phone_participation", st, "day_round", order=lambda i: (i[0]))
        save("phone_participation_by_round", svg_bars("Phone participation by round", sub + " | observed players with a connected call", its, _pct, banner, max_value=1.0))
        save("phone_participation_by_player_count", svg_bars("Phone participation by player count", sub, items_for(rows, "C.phone_participation", st, "player_count"), _pct, banner, max_value=1.0))
        save("first_contact_latency_median_by_player_count", svg_bars("First successful contact (median seconds)", sub + " | rounds with contact only",
                                                                       items_for(rows, "D.first_contact_latency.median", st, "player_count"), _num, banner))
        save("longest_gap_by_task", svg_bars("Longest interaction gap by task (mean seconds)", sub, items_for(rows, "F.longest_gap", st, "task_id", order=lambda i: -i[1]), _num, banner))
        save("post_submit_wait_by_player_count", svg_bars("Waiting after own submission (mean seconds)", sub, items_for(rows, "F.post_submit_wait", st, "player_count"), _num, banner))
        its = items_for(rows, "H.vote_outcome", st, "all", part="", order=None)
        vo = [(r["partition"], r["value"], r["ci_low"], r["ci_high"], "(n=%g)" % r["denominator"]) for r in rows
              if r["metric_id"] == "H.vote_outcome" and (r["build_version"], r["ruleset_version"]) == st and r["grouping"] == "all" and r["value"] is not None]
        save("warning_meeting_outcomes", svg_bars("Warning meeting outcomes", sub, vo, _pct, banner, max_value=1.0))
        bt = items_for(rows, "I.blame_top_share", st, "day")
        base = {i[0]: get_row(index_rows(rows), "I.blame_top_share_baseline", st, "", "day", i[0]) for i in bt}
        refs = {k: (v["value"] if v else None) for k, v in base.items()}
        save("blame_top_share_by_day", svg_bars("Blame leader share by day (orange = even split)", sub, bt, _pct, banner, refs=refs, max_value=1.0))
        gr = [(r["anonymous_group_id"], r["phone_participation"], None, None, "(n=%g)" % (r["phone_participation_n"] or 0)) for r in S.group_table
              if (r["build_version"], r["ruleset_version"]) == st and r["phone_participation"] is not None]
        save("phone_participation_by_group", svg_bars("Phone participation per friend group", sub, gr, _pct, banner, max_value=1.0))
        lr = [(r["metric_id"].split(".")[1], r["value"], r["ci_low"], r["ci_high"], "(groups=%g)" % r["denominator"]) for r in rows
              if r["metric_id"] in ("L.rematch_intent", "L.voluntary_rematch", "M.groups_notable") and (r["build_version"], r["ruleset_version"]) == st
              and r["grouping"] == "all" and r["value"] is not None]
        save("rematch_and_notable_groups", svg_bars("Group-level rematch and notable-moment shares", sub + " | Wilson interval over groups", lr, _pct, banner, max_value=1.0))
    return written


# =====================================================================================================================
# KPI documentation and hypothesis plan (single source of truth for docs/PLAYTEST_KPI_GUIDE.md)
# =====================================================================================================================
def _K(title, question, numerator, denominator, population, window, consent, authority, dedupe, missing, limits):
    return {"title": title, "question": question, "numerator": numerator, "denominator": denominator, "population": population,
            "window": window, "consent": consent, "authority": authority, "dedupe": dedupe, "missing": missing, "limits": limits}


KPI_DOCS = {
    "A": _K("Access", "Can friends get into a lobby, fill it, ready up and start without friction, and where do people leave?",
            "join: lobby_joined; start failures: match_start_failed; departures: session_interrupted(mid_match) and match_abandoned by phase; times: lobby_fill_seconds, ready_seconds, wait_seconds",
            "join: lobby_join_attempted; start failure: match_start_attempted; departures: match_started (matches)",
            "Consenting clients (join) and consenting host (start/match events); human, non-bot matches of one build+ruleset stratum",
            "lobby open -> match_started (and the match for departures)",
            "Client events need the client's own consent (self); host aggregate events need host consent only. Departure rows are skipped when a match has no client logs.",
            "client: lobby_join_attempted, lobby_joined, lobby_join_failed, session_interrupted; host: match_start_attempted, match_started, match_start_failed, match_abandoned",
            "event_id (client random id, host deterministic hash); the analyzer keeps the first copy across host/client/re-imported files",
            "No client log -> metric not_measured/insufficient_data, never a 0% success rate. Retries count as new attempts; unknown reasons stay in their own bucket.",
            "Per attempt, not per person or group. Host-only logs cannot see failed joins that never reached the host. Lobby times depend on how the facilitator coordinated the group."),
    "B": _K("Completion and pacing", "Do matches, days and rounds complete, how long do phases really last, and why do rounds end?",
            "completed matches/rounds/days; phase exits with overrun_seconds > 0.5; RoundPlay exits ended by timer; early_submit rounds",
            "terminal-observed matches (completed + abandoned); round_started; day_started; phase_exited",
            "Started matches with an observed host log; every phase exit of the stratum",
            "match_started -> week_completed / match_abandoned; per phase entered -> exited",
            "Host aggregate (H): host consent only, counts and durations, no per-player ids",
            "host: phase_exited, round_started, round_finished, day_started, day_finished, week_completed, match_abandoned",
            "event_id; one phase_exited per phase per match (duplicate files collapse)",
            "Matches with neither week_completed nor match_abandoned are 'unknown end' (B.match_unknown_end) and excluded from the completion denominator; missing durations are skipped, not zeroed.",
            "Durations come from the host monotonic clock. A host crash loses private events and can look like an unknown end. Carried break seconds are only visible for early_submit rounds."),
    "C": _K("Phone participation per round", "In each round, what share of observed players had at least one successful connected call?",
            "distinct eligible OBSERVED players with >=1 successful connected call overlapping the round (phone_round_summary.participating, or derived from call intervals vs the RoundPlay window)",
            "eligible observed players (phone_round_summary.observed, or players with a round_player_summary)",
            "Consenting, non-bot, connected players of a round",
            "RoundPlay window of the round (phase_entered -> phase_exited)",
            "all_participants: a call needs the consent of caller and callee; host aggregate summary needs host consent and carries observation_coverage",
            "host: phone_round_summary (primary), phone_call_connected/finished (derivation), round_player_summary (fallback)",
            "a call is identified by (match_id, call_id) and counts once per participant; round summaries by (match, day, round)",
            "Failed, rejected and missed calls never count as participation. A round without observed players is skipped (not 0%). participation_rate='not_measured' rounds are skipped.",
            "Participation is a floor: unconsented players are outside the denominator. It measures a connected call, not conversation quality or speaking."),
    "D": _K("Communication exposure", "How quickly and how widely do players reach each other, and where does calling fail?",
            "first-contact seconds (samples); calls and connected seconds; HHI of partner shares; busy/unanswered counts; players who attempted but never connected",
            "player-rounds with contact (latency); call requests (failure rates); players who attempted (never-connect)",
            "Observed player-rounds and calls with both participants' consent",
            "RoundPlay; call intervals are clipped to phases; connected time = interval union per player",
            "all_participants for calls; self for round_player_summary",
            "host: round_player_summary, phone_call_requested/connected/finished/failed/missed/rejected",
            "each call once globally (match_id, call_id), once per participant for per-player figures; overlapping calls of one player are unioned",
            "A player-round without contact has no latency (excluded and counted by D.no_contact_share) - never 0 s. Calls without a finished event are censored/unknown duration.",
            "Connected time is not speaking time. Short latency can mean a dominant caller. HHI is undefined with fewer than two connected calls."),
    "E": _K("Communication value", "Do outcomes, contribution, decision revisions and career results differ between no-communication, voice-only, text-only and mixed rounds?",
            "mean/rate of quality, income, cooperation credits, revisions, career total within each comm_mode",
            "player-rounds (or cooperation opportunities / players) in that mode",
            "Observed player-rounds with a comm_mode and a resolved outcome; survived matches for career totals",
            "one round; career totals per match",
            "self (private_until_match_end events written at results; suppressed without consent)",
            "host: round_player_summary(comm_mode), choice_outcome_resolved, choice_changed, career_breakdown",
            "(match, day, round, player slot) joins; duplicates removed by event_id",
            "Player-rounds lacking a comm_mode or an outcome are dropped from that mode, never counted as 'none'. Groups with <3 samples per mode are skipped for the dominance check.",
            "ASSOCIATIONS ONLY. Confounders: task type and role (some tasks need contact), partner availability, player skill, group, who chooses to talk, ordering effects. No causal claims."),
    "F": _K("Waiting and exclusion", "Who waits or is left out: longest interaction gaps, post-submission waiting, busy partners, technical blocking?",
            "gap/wait seconds (samples); player-rounds with wait >= 20 s; player-rounds blocked > 0",
            "observed (submitted) player-rounds",
            "Observed players; worst-served = the single player with the largest mean gap per match",
            "RoundPlay",
            "self (round_player_summary)",
            "host: round_player_summary(longest_gap_seconds, post_submit_wait_seconds, busy_failures, blocked_seconds)",
            "(match, day, round, slot)",
            "Players without a summary are UNKNOWN (F.observation_coverage shows the known share); unknown periods are never reported as zero waiting.",
            "Reading, thinking or listening is not boredom or exclusion. A long gap can be a deliberate choice. Pair with observer players_appeared_excluded."),
    "G": _K("Decisions", "How fast and how stable are decisions, before vs after contact, and what do they produce?",
            "latency samples; choice_changed per submission; submissions after contact; timeouts; rejected actions; category counts; blame deltas",
            "choice_submitted (per task); submissions + timeouts; observed player-rounds",
            "Observed submissions of a task",
            "RoundPlay",
            "self (M events at results); invalid actions are client events",
            "host: choice_submitted, choice_changed, choice_timed_out, fault_points_changed; client: ui_action_rejected, message_rejected",
            "event_id; one submitted event per (player, field) final plus changes",
            "Missing latency is skipped. Timeouts are denominated by submissions + timeouts of the observed rounds. Categories are task specific (compare only within a task).",
            "Short latency can mean carelessness or a solved puzzle; revisions can mean learning. Do not rank tasks without difficulty control."),
    "H": _K("Meetings and recordings", "Do 7-second reveals play, how long do they run, where do they come from, who is exposed, and how do votes resolve?",
            "reveals played; context-bearing reveals; repeat exposures; voters; abstentions; outcome counts; discussion seconds",
            "recording_playback_finished; recording_playback_started; eligible voters; resolved meetings",
            "Meetings with recordings enabled (lobby toggle) of human matches",
            "meeting dossier -> verdict; final: defense -> final verdict",
            "recording playback: all_participants (both speakers); votes: voter; outcomes: host aggregate",
            "host: recording_playback_started/finished, meeting_vote_submitted/resolved, final_vote_submitted/resolved, meeting_discussion_finished",
            "(match, pick_index) for reveals; last ballot per voter per meeting",
            "A vote target without consent is 'unconsented'. Speaking time per player is NOT measured (no event) -> not_measured. Matches without recordings enabled are not counted as 0 reveals.",
            "Playback success is technical, not intelligibility; pair with observer recording_was_intelligible / relevant / lacked_context. speech_ms is per-snippet metadata."),
    "I": _K("Blame and fairness", "How is blame distributed and recovered, and do warnings keep landing on the same people?",
            "blame spread/top share per day end; changes by reason; remedial requests / accepted; meetings with a repeat warning; capped nominations; warnings on the previous blame leader",
            "players (even split baseline); recovery opportunities; meetings where a repeat warning was possible; meetings where a player was at the warning cap",
            "Consenting players of human matches",
            "day boundaries; per meeting",
            "self (fault_points_changed, warning_assigned); host aggregate for recovery counts",
            "host: fault_points_changed, warning_assigned, meeting_vote_resolved, recovery_opportunities, remedial_requested/resolved",
            "event_id; blame_after is the authoritative state",
            "Start blame is assumed " + str(START_BLAME) + " for players without events; the warning cap is " + str(WARNING_CAP) + ". Missing outcome strings leave the cap metric empty.",
            "Concentration is not proof of unfairness (a saboteur may truly deserve blame). The cap prevents unlimited repeats, so compare repeat warnings only against eligible meetings."),
    "J": _K("Economy and hybrid balance", "Is income/evidence access concentrated, does sabotage get verified, and is productivity or wealth a near-automatic route to rank 1?",
            "income/evidence Gini and top share; transfers; spend; verified/created incidents; sabotage loss; remedial recovery; objectives completed; career points; matches where a rank-1 player is also richest/best",
            "players (even split baseline); created incidents; max output; objective events; matches with ranks",
            "Consenting players of surviving (for ranks) human matches",
            "whole match (private events are flushed at results)",
            "self (income, purchases, incidents), all_participants (transfers), host aggregate (company result)",
            "host: income_credited, evidence_purchased, money_transferred, incident_created, sabotage_verified, task_output_resolved, remedial_resolved, objective_completed, career_breakdown, company_result",
            "event_id",
            "Private events exist only after the match ended and only for consenting players; matches that never flushed are skipped, not zero. Ranks are skipped when the company closed.",
            "Rank-1 coincidence must be compared with its chance baseline (J.*_baseline). Small groups make top shares high by construction (1/N)."),
    "K": _K("Technical", "Which known failures recur, do reconnects work, how bad is phone/voice quality, and which exits are unexplained?",
            "matches with a technical_failure of a category; reconnect successes; phone failures other than busy; voice loss/jitter/underruns; stuck_phase; sessions with no end event",
            "matches with logs; reconnect outcomes; call requests; voice summaries; started sessions",
            "Any consenting client or host with logs",
            "match or session",
            "self (client), host aggregate",
            "client+host: technical_failure, reconnect_*, voice_quality_summary, session_started/ended/interrupted; host: stuck_phase, unexpected_transition, phone_call_failed",
            "event_id",
            "Only caught failures are logged; a session with no end event is a 'possible crash' (unknown), not a confirmed crash; reconnect attempts without outcome stay unknown.",
            "Truncated or missing files look like crashes. Latency/loss are client-reported summaries."),
    "L": _K("Voluntary rematch", "Do groups choose to play again?",
            "groups whose genuine opportunity was followed by a voluntary rematch_started (not auto, requests >= 1)",
            "completed groups with an observed genuine opportunity (rematch_prompt_shown, or results reached with >=3 humans when no prompt events exist), excluding auto restarts and moderator-required repeats",
            "Groups of human matches of one stratum",
            "from WeekResults until the next match_started of the group",
            "self for client rematch events; host aggregate (H) for rematch_started and week_completed",
            "client: rematch_requested, rematch_prompt_shown; host: rematch_started, week_completed",
            "event_id; rematch_started.previous_match_id links matches",
            "Intent (rematch_requested) and actual rematch are separate metrics; groups with no observed opportunity are excluded, not counted as 'no'.",
            "Facilitators can bias the choice. A group is one data point however many matches it plays; exploratory only."),
    "M": _K("Shareable moments", "Do matches produce moments that observers confirm as notable and players want to share? Six layers are kept apart.",
            "(1) mechanical events per match; (2) player moment_marked; (3) groups with an observer-confirmed notable moment; (4) players wanting to share (feedback); (5) exports and (6) external shares",
            "completed matches; groups with client logs; groups with adequate observation; answered feedback; (5)(6) none",
            "Observed human matches; adequate = observer rows covering >=50% of the group's logged matches",
            "match",
            "self for moment_marked and feedback; observer CSV needs no telemetry consent but no personal data",
            "client: moment_marked; host: sabotage_verified, player_fired, company_failed; observer CSV; feedback CSV",
            "event_id; observer rows keyed by (group, match, day, round, moment_timestamp)",
            "(5) and (6) have no implementation in the game -> not_measured. Observer fields left blank are missing.",
            "A shareable moment is not predicted virality. Player-marked and mechanical events are candidates only; only observer confirmation counts as 'notable'."),
}

HYPOTHESES = [
    {"id": "H1", "hypothesis": "Information exchange and negotiation improve outcomes; having no communication does not reliably dominate.",
     "metrics": "E.quality/E.income/E.cooperation_rate/E.revisions by comm_mode, E.silent_not_dominant, D.no_contact_share, G.latency (after_contact)",
     "events": "round_player_summary(comm_mode), choice_outcome_resolved, choice_changed, career_breakdown",
     "observation": "players_reconsidered_accusation, notes outside telemetry about whether contact was needed",
     "criteria": "Design targets, not industry benchmarks: in <= 1/3 of groups the no-communication mean quality exceeds the communicating mean; communicating rounds are at least as good in most information tasks.",
     "min_data": ">= 10 groups, >= 3 player-rounds per mode and group; report per task",
     "confounders": "task needs contact by design, role, skill, partner availability, self-selection of talkers",
     "experiment": "none declared (observational)"},
    {"id": "H2", "hypothesis": "At 3-6 players everyone has useful opportunities to act and communicate without excessive exclusion or waiting.",
     "metrics": "C.phone_participation, F.longest_gap, F.post_submit_wait(_over20), F.blocked_share, D.busy_failure_rate, B.early_submit_share, F.worst_served_gap by player_count",
     "events": "phone_round_summary, round_player_summary, phone_call_failed, round_finished",
     "observation": "players_appeared_excluded, meeting_felt_too_short/long",
     "criteria": "Design targets, not industry benchmarks: phone participation >= 80% of observed players per round; <= 20% of submitted player-rounds waiting >= 20 s; worst-served mean gap not more than 2x the group median.",
     "min_data": ">= 3 groups per player count (3/4/5/6) before comparing counts",
     "confounders": "task mix, group familiarity, call-handling habits, early-submit policy",
     "experiment": "early_submit (shorten|carry): compare waiting and carried break seconds"},
    {"id": "H3", "hypothesis": "Seven-second reveals are understandable and relevant often enough.",
     "metrics": "H.playback_success, H.context_share, H.speech_density, H.participant_coverage, H.repeat_exposed_players, M.recording_intelligible/relevant/contextless/reaction/referenced_later",
     "events": "recording_playback_started/finished",
     "observation": "recording_was_intelligible, recording_was_relevant, recording_lacked_context, recording_created_reaction, recording_was_referenced_later",
     "criteria": "Design targets, not industry benchmarks: >= 90% playback success; >= 60% of observed reveals coded relevant and >= 70% intelligible; contextless reveals < 30%.",
     "min_data": ">= 30 observed reveals over >= 8 groups",
     "confounders": "speaker volume, music ducking, room noise, observer coding drift",
     "experiment": "rec_select (legacy|context): compare relevance/context share"},
    {"id": "H4", "hypothesis": "Recording awareness does not eliminate candid negotiation.",
     "metrics": "C.phone_participation, D.call_duration, D.no_contact_share, E.mode_share, FB.q_recording_awareness by exp.recording_awareness",
     "events": "phone_*, round_player_summary, lobby_created/match_started(recordings_enabled)",
     "observation": "natural_accusation_occurred, players_appeared_confused",
     "criteria": "Design targets, not industry benchmarks: recordings-on phone participation and call duration at least 70% of the recordings-off level; most players answer 'No' or 'Somewhat' to the awareness question.",
     "min_data": ">= 5 groups per arm; assign the arm per group, never per player",
     "confounders": "novelty of the first match, group size, order of arms",
     "experiment": "recording_awareness (on|off) via the lobby recordings toggle - declared experiment"},
    {"id": "H5", "hypothesis": "Accusations refer to observable events and evidence rather than consistently the same disliked player.",
     "metrics": "O.accusation_evidence, O.accusation_reconsidered, I.target_was_blame_leader, I.warning_top_share vs baseline, I.repeat_warning, I.nomination_capped, H.vote_outcome",
     "events": "meeting_vote_resolved, warning_assigned, evidence_presented, fault_points_changed",
     "observation": "natural_accusation_occurred, accusation_referenced_game_evidence, players_reconsidered_accusation, unexpected_accusation",
     "criteria": "Design targets, not industry benchmarks: >= 60% of observed accusations reference game evidence; the same player is not the warning target in more than half of meetings in most groups.",
     "min_data": ">= 10 groups with observers; >= 3 meetings per match",
     "confounders": "friendship dynamics, evidence availability, observer subjectivity",
     "experiment": "none declared"},
    {"id": "H6", "hypothesis": "A bad early blame outcome has a credible recovery path.",
     "metrics": "I.recovery_use, I.recovery_accept, I.recovery_output_added, I.blame_spread/top_share by day, J.remedial_recovery_share, FB.q_recover_bad_start",
     "events": "recovery_opportunities, remedial_requested/resolved, fault_points_changed",
     "observation": "players_appeared_confused (about recovery)",
     "criteria": "Design targets, not industry benchmarks: >= 30% of players with options request remedial work; >= 50% of requests accepted; blame leader share does not stay above 2x the even split through the last day.",
     "min_data": ">= 10 groups; matches with at least one early warning",
     "confounders": "partner willingness, task output already at max, cap rules",
     "experiment": "none declared"},
    {"id": "H7", "hypothesis": "Productivity creates cooperation pressure without making the hardest worker or richest player the automatic winner.",
     "metrics": "J.rank1_is_richest vs baseline, J.rank1_is_best_quality vs baseline, J.career_component, J.company_closed_rate, J.final_productivity, J.income_top_share vs baseline",
     "events": "career_breakdown, company_result, income_credited",
     "observation": "surprising_outcome",
     "criteria": "Design targets, not industry benchmarks: rank-1 coincides with the richest (or best-quality) player no more than ~2x the chance baseline; company closes in roughly 10-40% of matches (neither always nor never).",
     "min_data": ">= 20 surviving matches over >= 10 groups",
     "confounders": "ties, objective luck, sabotage exposure, group skill",
     "experiment": "none declared (CareerTargetPercent is a possible future experiment)"},
    {"id": "H8", "hypothesis": "Matches produce observer-confirmed funny betrayals, unexpected accusations or surprising outcomes, and some players want to share them.",
     "metrics": "M.groups_notable, M.notable_per_match, M.notable_category/day/phase, M.player_marked_*, M.share_interest, M.mechanical_events, M.observation_coverage; M.export and M.external_share are not_measured",
     "events": "moment_marked, sabotage_verified, player_fired, company_failed",
     "observation": "funny_betrayal, unexpected_accusation, surprising_outcome, notable_funny_moment",
     "criteria": "Design targets, not industry benchmarks: >= 60% of adequately observed groups have >= 1 confirmed notable moment; >= 0.5 confirmed moments per observed match; a clear minority of players answer Yes to wanting to share.",
     "min_data": ">= 10 adequately observed groups",
     "confounders": "observer eagerness, group extroversion, drink/venue",
     "experiment": "none declared"},
    {"id": "H9", "hypothesis": "Music supports tension and comedy without impairing conversation, recording intelligibility or concentration.",
     "metrics": "FB.q_hear_over_music, M.recording_intelligible, K.voice_loss/underruns, D.call_duration, G.latency",
     "events": "music_cue_changed, voice_quality_summary, recording_playback_*",
     "observation": "recording_was_intelligible, players_appeared_confused",
     "criteria": "Design targets, not industry benchmarks: >= 80% of players answer Yes/Somewhat to hearing calls and reveals over music; intelligibility coding does not drop in ducked-off cues.",
     "min_data": ">= 10 groups with feedback; >= 20 observed reveals",
     "confounders": "speakers/headphones, volume settings, room noise",
     "experiment": "none declared (a music on/off arm would need to be added)"},
]


def render_kpi_guide():
    o = ["# Playtest KPI guide and executable test plan", "",
         "Generated by `python -I Tools/scripts/analyze_playtests.py --write-kpi-guide docs/PLAYTEST_KPI_GUIDE.md` from the same definitions the analyzer uses. "
         "Event names follow docs/ANALYTICS_ARCHITECTURE.md section 5. Unit of analysis: the anonymous friend group.", "",
         "Common rules: missing data is `not_measured` or `insufficient_data`, never zero; strata (build_version + ruleset_version) are never pooled; "
         "synthetic data (fixtures, bot sims, matches with bots) is excluded unless `--include-synthetic` and then bannered `" + BANNER + "`; " + EXPLORATORY, "",
         "Metric statuses: `ok` (>= %d groups, denominator > 0, coverage requirement met), `insufficient_data` (value shown if any, but too few groups/observations/coverage), `not_measured` (required event never present or no implementation)." % MIN_GROUPS_OK, ""]
    o.append("## KPI definitions A-M")
    for k in sorted(KPI_DOCS):
        d = KPI_DOCS[k]
        o += ["", "### KPI %s - %s" % (k, d["title"]), ""]
        for f, label in (("question", "Question"), ("numerator", "Numerator"), ("denominator", "Denominator"), ("population", "Eligible population"),
                         ("window", "Observation window"), ("consent", "Consent / coverage requirement"), ("authority", "Authority source (events)"),
                         ("dedupe", "Deduplication"), ("missing", "Missing-data handling"), ("limits", "Interpretation limits")):
            o.append("- **%s:** %s" % (label, d[f]))
        ms = [s for s in SPECS.values() if k in s.kpi.split("/")]
        o += ["", "Implemented metrics: " + ", ".join("`%s`" % s.id for s in ms)]
    o += ["", "## Test and observation plan H1-H9", "",
          "All success criteria below are **design targets, not industry benchmarks**. They tell the team where to look, not that a mechanic works or fails.",
          "Declared experiments available in the build: `rec_select` (legacy|context), `early_submit` (shorten|carry), `recording_awareness` (on|off)."]
    for h in HYPOTHESES:
        o += ["", "### %s" % h["id"], "", "- **Hypothesis:** %s" % h["hypothesis"], "- **Metrics / events used:** %s; events: %s" % (h["metrics"], h["events"]),
              "- **Observation fields:** %s" % h["observation"], "- **Provisional success criteria:** %s" % h["criteria"],
              "- **Minimum data:** %s" % h["min_data"], "- **Confounders:** %s" % h["confounders"], "- **Declared experiment:** %s" % h["experiment"]]
    o += ["", "## Metric dictionary", "", "| metric_id | KPI | kind | unit | requires | partition | note |", "|---|---|---|---|---|---|---|"]
    for s in SPECS.values():
        o.append("| `%s` | %s | %s | %s | %s | %s | %s |" % (s.id, s.kpi, s.kind, s.unit, ", ".join(s.requires) or "-", s.part or "-", (s.note or "").replace("|", "/")))
    return "\n".join(o) + "\n"


# =====================================================================================================================
# summary
# =====================================================================================================================
def fm(r, pct=None):
    if r is None:
        return "no row"
    if r["status"] == "not_measured":
        return "**not_measured**"
    if r["value"] is None:
        return "insufficient data (no observations)"
    unit = r["unit"]
    isp = pct if pct is not None else unit in ("share", "share of groups")
    f = (lambda v: "%.0f%%" % (100 * v)) if isp else (lambda v: "%.1f" % v if abs(v) >= 10 else "%.2f" % v)
    s = f(r["value"])
    if r["ci_low"] is not None and r["ci_high"] is not None:
        s += " (%s–%s)" % (f(r["ci_low"]), f(r["ci_high"]))
    s += " [n=%g, groups=%d, matches=%d" % (r["denominator"] or 0, r["n_groups"], r["n_matches"])
    s += "]" if r["status"] == "ok" else ", **%s**]" % r["status"]
    return s


def build_summary(S, rows, grows, frows, charts, warnings_, args_desc):
    ix = index_rows(rows)
    o = []
    if S.has_synthetic:
        o += ["# " + BANNER, "", "> **" + BANNER + "** - this report contains synthetic data (fixtures, bot simulations or bot matches). Do not use it for design conclusions.", ""]
    o += ["# Playtest summary", "", "Generated: " + datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%d %H:%M UTC"), "", EXPLORATORY, ""]
    ld = S.load
    o += ["## 1. Data and coverage", "",
          "- Files read: %d (recursive, rotated `events_<session>_<n>.jsonl` included); lines %d; accepted events %d (host %d, client %d)" % (ld.files, ld.lines, ld.accepted, ld.by_authority.get("host", 0), ld.by_authority.get("client", 0)),
          "- Duplicate events removed (same event_id across host/client/re-imported files): %d" % ld.duplicates,
          "- Truncated final lines: %d; corrupt lines: %d; rejected by envelope validation: %d%s" % (ld.truncated, ld.corrupt, sum(ld.rejected.values()), (" (" + ", ".join("%s=%d" % kv for kv in ld.rejected.most_common(5)) + ")") if ld.rejected else ""),
          "- Event catalog: %s" % ("loaded (%d events), schema issues: %d" % (len(ld.catalog), sum(ld.param_issues.values())) if ld.catalog else "not found - envelope validation + built-in section 5 list only"),
          "- Unknown event names: %s" % (", ".join("%s(%d)" % kv for kv in ld.unknown_events.most_common(8)) or "none"),
          "- Contexts analysed: %d matches, %d groups; strata (build | ruleset): %s" % (len(S.matches), len(S.groups), "; ".join("%s | %s" % st for st in S.strata)),
          "- Excluded as synthetic (not human playtest data): %s" % (", ".join("%s=%d" % kv for kv in S.excluded.items()) or "none"),
          "- Observer rows linked: %d; feedback rows linked: %d" % (len(S.obs_pairs), len(S.fb_pairs))]
    for w in warnings_:
        o.append("- Warning: " + w)
    if not S.matches and not S.obs_pairs:
        o += ["", "**No analysable human matches.** Every metric below is `insufficient_data` or `not_measured`; nothing is zero.", ""]
    sizes = Counter((c.pc or 0) for c in S.matches)
    o += ["", "Matches by player count: " + (", ".join("%s players: %d" % (k or "unknown", v) for k, v in sorted(sizes.items())) or "none"), ""]
    for si, st in enumerate(S.strata):
        o += ["## Stratum %d: build `%s`, ruleset `%s`" % (si + 1, st[0], st[1]), "",
              "Strata are never pooled. Charts: `charts/*" + (("_s%d" % (si + 1)) if len(S.strata) > 1 else "") + ".svg`.", ""]
        o += _stratum_findings(S, rows, ix, grows, frows, st)
    o += ["## Next-playtest hypotheses (investigation suggestions, not verdicts)", ""]
    o += _hypotheses_text(S, ix)
    o += ["", "## Reference", "", "- Metrics: `PLAYTEST_METRICS.csv` (one row per metric x stratum x partition x grouping). Per-group: `PLAYTEST_GROUP_COMPARISON.csv`. Funnels: `PLAYTEST_FUNNELS.csv`.",
          "- KPI definitions and the H1-H9 plan: `docs/PLAYTEST_KPI_GUIDE.md`. Statuses: ok / insufficient_data / not_measured.",
          "- Charts: " + ", ".join("`charts/%s`" % os.path.basename(c) for c in charts[:40]), "- Invocation: " + args_desc]
    return "\n".join(o) + "\n"


def _top_rows(rows, mid, st, grouping, n=3, reverse=False, part=""):
    rs = [r for r in rows if r["metric_id"] == mid and (r["build_version"], r["ruleset_version"]) == st and r["grouping"] == grouping
          and r["partition"] == part and r["value"] is not None and r["status"] != "not_measured"]
    rs.sort(key=lambda r: r["value"], reverse=reverse)
    return rs[:n]


def _bullets(label, rs, pct=None):
    if not rs:
        return ["- %s: no data" % label]
    return ["- %s:" % label] + ["  - `%s=%s`: %s" % (r["grouping"], r["group_value"], fm(r, pct)) for r in rs]


def _stratum_findings(S, rows, ix, grows, frows, st):
    G = lambda mid, gt="all", gv="all", part="": ix.get((mid, st, part, gt, gv))
    o = []
    o += ["### Largest completion losses", ""]
    fr = [r for r in frows if r["funnel"] == "match_progress" and (r["build_version"], r["ruleset_version"]) == st]
    if fr and fr[0]["count"]:
        o.append("| stage | matches | share of started | groups |\n|---|---|---|---|")
        for r in fr:
            o.append("| %s | %s | %s | %s |" % (r["stage"], r["count"], _pct(r["share_of_first"]) if r["share_of_first"] is not None else "-", r["n_groups"]))
        drops = [(fr[i - 1]["count"] - fr[i]["count"], fr[i - 1]["stage"], fr[i]["stage"]) for i in range(1, len(fr))]
        d = max(drops)
        o.append("\nLargest drop: %d match(es) between `%s` and `%s`." % d if d[0] > 0 else "\nNo drop between observed stages.")
    else:
        o.append("No started matches observed (funnel empty).")
    o.append("")
    o.append("- Match completion: " + fm(G("B.match_completion")) + "; unknown ends: " + fm(G("B.match_unknown_end")))
    ab = [r for r in rows if r["metric_id"] == "B.abandon_phase" and (r["build_version"], r["ruleset_version"]) == st and r["grouping"] == "all" and r["value"]]
    o.append("- Abandonment by phase: " + ("; ".join("%s %s" % (r["partition"], fm(r)) for r in sorted(ab, key=lambda r: -r["value"])[:4]) or "no abandoned matches observed"))
    o += _bullets("Lowest completion by player count", _top_rows(rows, "B.match_completion", st, "player_count", 4))
    o += _bullets("Rounds finishing least often by task", _top_rows(rows, "B.round_completion", st, "task_id", 3))
    o += ["", "### Lowest communication participation", ""]
    o.append("- Phone participation overall: " + fm(G("C.phone_participation")) + "; observed share of eligible: " + fm(G("C.observed_share")))
    o += _bullets("Lowest by round", _top_rows(rows, "C.phone_participation", st, "day_round", 3))
    o += _bullets("Lowest by task", _top_rows(rows, "C.phone_participation", st, "task_id", 3))
    o.append("- Observed player-rounds with no successful contact: " + fm(G("D.no_contact_share")) + "; first-contact latency median: " + fm(G("D.first_contact_latency.median")))
    o.append("- Busy failures / unanswered requests: " + fm(G("D.busy_failure_rate")) + " / " + fm(G("D.unanswered_rate")) + "; attempted but never connected: " + fm(G("D.attempt_never_connect")))
    o += ["", "### Longest interaction gaps and waiting", ""]
    o.append("- Longest gap mean/p90: " + fm(G("F.longest_gap")) + " / " + fm(G("F.longest_gap.p90")) + " (unknown periods are not zero; observation coverage " + fm(G("F.observation_coverage")) + ")")
    o += _bullets("Longest by task", _top_rows(rows, "F.longest_gap", st, "task_id", 3, True))
    o.append("- Waiting after submission: " + fm(G("F.post_submit_wait")) + "; >= 20 s: " + fm(G("F.post_submit_wait_over20")) + "; blocked: " + fm(G("F.blocked_share")))
    wg = sorted([g for g in grows if (g["build_version"], g["ruleset_version"]) == st and g["worst_served_gap_s"] is not None], key=lambda g: -g["worst_served_gap_s"])[:3]
    o.append("- Worst-served player per group (largest mean gap): " + ("; ".join("%s %.1fs" % (g["anonymous_group_id"], g["worst_served_gap_s"]) for g in wg) or "not measured"))
    o += ["", "### Player-count differences (exploratory)", ""]
    pcs = sorted({r["group_value"] for r in rows if r["grouping"] == "player_count" and (r["build_version"], r["ruleset_version"]) == st})
    if pcs:
        mets = [("B.match_completion", "completion"), ("C.phone_participation", "phone participation"), ("D.first_contact_latency.median", "first contact median s"),
                ("F.post_submit_wait", "post-submit wait s"), ("F.longest_gap", "longest gap s")]
        o.append("| metric | " + " | ".join("%s players" % p for p in pcs) + " |\n|---|" + "---|" * len(pcs))
        for mid, lab in mets:
            cells = []
            for p in pcs:
                r = G(mid, "player_count", p)
                cells.append(fm(r) if r else "no row")
            o.append("| %s | %s |" % (lab, " | ".join(cells)))
        o.append("\nDifferences with overlapping intervals or `insufficient_data` are not evidence of a player-count effect.")
    else:
        o.append("No player-count slices.")
    o += ["", "### Recurring technical problems", ""]
    tf = [r for r in rows if r["metric_id"] == "K.technical_failure_match" and (r["build_version"], r["ruleset_version"]) == st and r["grouping"] == "all" and r["value"]]
    o.append("- Matches with a technical failure: " + ("; ".join("%s %s" % (r["partition"], fm(r)) for r in tf) or "none observed (only caught failures are logged)"))
    o.append("- Reconnect success: " + fm(G("K.reconnect_success")) + "; non-busy phone failures: " + fm(G("K.phone_failure_nonbusy")) + "; possible crashes (unknown): " + fm(G("K.possible_crash")))
    o.append("- stuck_phase per match: " + fm(G("K.stuck_phase")) + "; voice loss %: " + fm(G("K.voice_loss")) + "; start failures: " + fm(G("A.start_failure_rate")) + "; join success: " + fm(G("A.join_success")))
    if S.load.corrupt or S.load.truncated:
        o.append("- Data-quality: %d corrupt and %d truncated lines were skipped." % (S.load.corrupt, S.load.truncated))
    o += ["", "### Slow or confusing decisions", ""]
    o += _bullets("Slowest submissions by task (median latency)", _top_rows(rows, "G.latency.median", st, "task_id", 3, True))
    o += _bullets("Most revisions per submission by task", _top_rows(rows, "G.revisions_per_submit", st, "task_id", 3, True))
    o.append("- Timeouts (missed): " + fm(G("G.timeout_share", part="missed")) + "; rejected actions per player-round: " + fm(G("G.invalid_actions")))
    o += _bullets("Phases overrunning most often", _top_rows(rows, "B.overrun_rate", st, "phase", 3, True))
    o.append("- Observer: players appeared confused " + fm(G("O.confused")) + "; meeting felt too short " + fm(G("O.meeting_too_short")) + "; too long " + fm(G("O.meeting_too_long")))
    o += ["", "### Potential blame and economy imbalances (concentration is not proof of unfairness)", ""]
    bl = [r for r in rows if r["metric_id"] == "I.blame_top_share" and r["grouping"] == "all" and (r["build_version"], r["ruleset_version"]) == st]
    bb = G("I.blame_top_share_baseline")
    o.append("- Blame leader share at day end: " + (fm(bl[0]) if bl else "no row") + " vs even-split baseline " + (fm(bb) if bb else "n/a"))
    o.append("- Blame spread: " + fm(G("I.blame_spread")) + "; repeat warnings when possible: " + fm(G("I.repeat_warning")) + "; capped nominations: " + fm(G("I.nomination_capped")))
    o.append("- Warnings on the prior blame leader: " + fm(G("I.target_was_blame_leader")) + "; recovery use / accept: " + fm(G("I.recovery_use")) + " / " + fm(G("I.recovery_accept")))
    o.append("- Income top share: " + fm(G("J.income_top_share")) + " vs baseline " + fm(G("J.income_top_share_baseline")) + "; income Gini " + fm(G("J.income_gini")) + "; evidence Gini " + fm(G("J.evidence_gini")))
    o.append("- Rank 1 is richest: " + fm(G("J.rank1_is_richest")) + " (chance " + fm(G("J.rank1_is_richest_baseline")) + "); rank 1 is best quality: " + fm(G("J.rank1_is_best_quality")) + " (chance " + fm(G("J.rank1_is_best_quality_baseline")) + ")")
    o.append("- Sabotage verified/created: " + ("; ".join("%s %s" % (r["partition"], fm(r)) for r in rows if r["metric_id"] == "J.sabotage_verified_rate" and r["grouping"] == "all" and (r["build_version"], r["ruleset_version"]) == st) or "not_measured (no incidents created by consenting actors)") + "; company closed: " + fm(G("J.company_closed_rate")))
    o += ["", "### Voluntary rematches", ""]
    o.append("- Voluntary rematch (groups): " + fm(G("L.voluntary_rematch")) + "; intent: " + fm(G("L.rematch_intent")) + "; intent -> actual: " + fm(G("L.intent_to_rematch")))
    o.append("- Observer-coded rematch requests: " + fm(G("O.rematch_requested")) + "; feedback 'play again = Yes': " + fm(G("FB.q_play_again", part="Yes")) + ". Auto restarts and moderator-required repeats are excluded; intent is not a rematch.")
    o += ["", "### Recording quality", ""]
    o.append("- Playback success: " + fm(G("H.playback_success")) + "; actual duration: " + fm(G("H.actual_duration")) + "; with source context: " + fm(G("H.context_share")))
    o.append("- Prior exposure share: " + fm(G("H.prior_exposure_share")) + "; participant coverage: " + fm(G("H.participant_coverage")) + "; speech density: " + fm(G("H.speech_density")) + "; per-player speaking time: " + fm(G("H.speaking_time_per_player")))
    o.append("- Observer: intelligible " + fm(G("M.recording_intelligible")) + "; relevant " + fm(G("M.recording_relevant")) + "; lacked context " + fm(G("M.recording_contextless")) + "; reaction " + fm(G("M.recording_created_reaction") if False else G("M.recording_reaction")))
    o.append("- Meetings: vote participation " + fm(G("H.vote_participation")) + "; abstain " + fm(G("H.abstain_share")) + "; final Fire-nobody share " + fm(G("H.final_fire_nobody_share")))
    o += ["", "### Notable and shareable moments (six layers kept apart)", ""]
    o.append("- (1) Automatic mechanical events per completed match: " + "; ".join("%s %s" % (r["partition"], fm(r)) for r in rows if r["metric_id"] == "M.mechanical_events" and r["grouping"] == "all" and (r["build_version"], r["ruleset_version"]) == st))
    o.append("- (2) Player-marked moments: groups with >= 1 " + fm(G("M.player_marked_groups")) + "; per match " + fm(G("M.player_marked_per_match")))
    o.append("- (3) Observer-confirmed: groups with >= 1 notable " + fm(G("M.groups_notable")) + "; per observed match " + fm(G("M.notable_per_match")) + "; observation coverage " + fm(G("M.observation_coverage")))
    cats = [r for r in rows if r["metric_id"] == "M.notable_category" and r["grouping"] == "all" and (r["build_version"], r["ruleset_version"]) == st and r["value"] is not None]
    o.append("- (3) Categories: " + ("; ".join("%s %s" % (r["partition"], _pct(r["value"])) for r in cats) or "no confirmed moments"))
    o.append("- (4) Players wanting to share (feedback Yes): " + fm(G("M.share_interest", part="Yes")))
    o.append("- (5) Actual export: " + fm(G("M.export")) + "; (6) external sharing: " + fm(G("M.external_share")) + " - no implementation in the game.")
    o.append("- A shareable moment is not predicted virality.")
    o.append("")
    return o


def _hypotheses_text(S, ix):
    def any_status(mids):
        bad = 0
        for st in S.strata:
            for m in mids:
                r = ix.get((m, st, "", "all", "all"))
                if r is None or r["status"] != "ok":
                    bad += 1
        return bad

    def val(mid, part=""):
        for st in S.strata:
            r = ix.get((mid, st, part, "all", "all"))
            if r and r["value"] is not None:
                return r["value"]
        return None
    cand = []

    def add(hid, base, mids, text, flag=None):
        score = base + (2 if any_status(mids) else 0) + (3 if flag else 0)
        ev = "evidence: " + (flag if flag else "metrics %s not yet ok (insufficient or not measured) - gather data first" % ", ".join(mids[:2]) if any_status(mids) else "no flag raised; confirm with a larger sample")
        cand.append((score, hid, text, ev))
    pp, wait, ctx_, ps, sil = val("C.phone_participation"), val("F.post_submit_wait_over20"), val("H.context_share"), val("H.playback_success"), val("E.silent_not_dominant")
    add("H2", 5, ["C.phone_participation", "F.post_submit_wait_over20"], "Investigate whether players at 3, 4, 5 and 6 get comparable chances to call and act: compare phone participation, waiting after submission and worst-served gaps by player count, and check whether the early_submit carry setting changes waiting.",
        "phone participation %s" % _pct(pp) if pp is not None and pp < 0.8 else ("share waiting >= 20 s is %s" % _pct(wait) if wait is not None and wait > 0.2 else None))
    add("H3", 5, ["H.playback_success", "H.context_share"], "Investigate whether the seven-second reveals are intelligible and relevant: pair playback telemetry with observer coding of relevance/context, and compare rec_select legacy vs context across groups.",
        "context share %s" % _pct(ctx_) if ctx_ is not None and ctx_ < 0.6 else ("playback success %s" % _pct(ps) if ps is not None and ps < 0.9 else None))
    add("H1", 4, ["E.silent_not_dominant", "E.quality"], "Investigate whether rounds with no communication look as good as rounds with contact, per task, while listing confounders (task design, role, skill).",
        "no-communication mean quality exceeds communicating in %s of groups" % _pct(sil) if sil is not None and sil > 0.33 else None)
    add("H4", 4, ["C.phone_participation"], "Investigate whether telling players that recordings are on changes how often and how long they call: run the recording_awareness on/off lobby toggle with whole groups per arm.", None)
    add("H5", 4, ["I.target_was_blame_leader", "O.accusation_evidence"], "Investigate whether accusations in meetings point to observable evidence or repeatedly to the same person, using observer coding together with warning-target concentration.",
        "warnings on the prior blame leader %s" % _pct(val("I.target_was_blame_leader")) if val("I.target_was_blame_leader") is not None and val("I.target_was_blame_leader") > 0.6 else None)
    add("H6", 3, ["I.recovery_use", "I.recovery_accept"], "Investigate whether a player hit by early blame actually finds and uses remedial work, and whether blame concentration persists to the last day.",
        "recovery use %s" % _pct(val("I.recovery_use")) if val("I.recovery_use") is not None and val("I.recovery_use") < 0.2 else None)
    r1 = val("J.rank1_is_richest")
    add("H7", 3, ["J.rank1_is_richest", "J.company_closed_rate"], "Investigate whether the richest or highest-quality player tends to finish first more often than chance, and whether the company closes at a plausible rate.",
        "rank 1 is richest in %s of matches" % _pct(r1) if r1 is not None and r1 > 0.6 else None)
    add("H8", 3, ["M.groups_notable", "M.share_interest"], "Investigate whether observers can confirm funny betrayals, unexpected accusations or surprising outcomes in most groups, and whether players say they would share them; export and sharing remain unmeasured.", None)
    add("H9", 2, ["FB.q_hear_over_music"], "Investigate whether players can hear calls and reveals over the music (feedback plus observer intelligibility coding), and whether ducking cues line up with reveals.", None)
    cand.sort(key=lambda c: (-c[0], c[1]))
    return ["%d. **%s** - %s (%s)" % (i + 1, c[1], c[2], c[3]) for i, c in enumerate(cand[:5])]


# =====================================================================================================================
# CLI
# =====================================================================================================================
def run_analysis(paths, observer_paths=(), feedback_paths=(), out_dir="Reports", include_synthetic=False, catalog_path=None, quiet=False):
    warnings_ = []
    catalog = load_catalog(catalog_path) if catalog_path else None
    events, st = load_events(paths, catalog)
    obs = read_csv_rows(observer_paths, set(OBSERVER_COLUMNS + OBSERVER_EXTRA), warnings_, "observer")
    fb = read_csv_rows(feedback_paths, set(FEEDBACK_COLUMNS), warnings_, "feedback")
    S = Analysis(events, st, include_synthetic, obs, fb)
    run_kpis(S)
    rows = aggregate(S)
    S.group_table = group_rows(S)
    frows = funnels(S)
    os.makedirs(out_dir, exist_ok=True)
    write_metrics_csv(rows, os.path.join(out_dir, "PLAYTEST_METRICS.csv"))
    write_group_csv(S.group_table, os.path.join(out_dir, "PLAYTEST_GROUP_COMPARISON.csv"))
    write_funnel_csv(frows, os.path.join(out_dir, "PLAYTEST_FUNNELS.csv"))
    charts = write_charts(S, rows, frows, os.path.join(out_dir, "charts"))
    desc = "`python -I Tools/scripts/analyze_playtests.py " + " ".join(os.path.basename(p.rstrip("/\\")) or p for p in paths) + (" --include-synthetic" if include_synthetic else "") + "`"
    md = build_summary(S, rows, S.group_table, frows, charts, warnings_, desc)
    with open(os.path.join(out_dir, "PLAYTEST_SUMMARY.md"), "w", encoding="utf-8", newline="\n") as f:
        f.write(md)
    return S, rows


def main(argv=None):
    root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    ap = argparse.ArgumentParser(description="Offline analyzer for Birimiz Kovulacak playtest telemetry (stdlib only).")
    ap.add_argument("paths", nargs="*", help="directories or .jsonl files (searched recursively)")
    ap.add_argument("--observer", action="append", default=[], help="observer CSV (Tools/playtest/observer_template.csv format)")
    ap.add_argument("--feedback", action="append", default=[], help="player feedback CSV")
    ap.add_argument("--out", default="Reports")
    ap.add_argument("--include-synthetic", action="store_true", help="include fixtures/bot sims (reports get the SYNTHETIC banner)")
    ap.add_argument("--catalog", default=os.path.join(root, "docs", "analytics", "event_catalog.json"))
    ap.add_argument("--purge-player", help="anonymous_player_id to remove (writes cleaned copies to --purge-out)")
    ap.add_argument("--purge-out")
    ap.add_argument("--write-kpi-guide", metavar="PATH", help="write docs/PLAYTEST_KPI_GUIDE.md from the built-in KPI definitions and exit")
    a = ap.parse_args(argv)
    if a.write_kpi_guide:
        with open(a.write_kpi_guide, "w", encoding="utf-8", newline="\n") as f:
            f.write(render_kpi_guide())
        print("wrote", a.write_kpi_guide)
        return 0
    if a.purge_player:
        if not a.purge_out or not a.paths:
            ap.error("--purge-player needs input paths and --purge-out")
        res = purge_player(a.paths, a.purge_player, a.purge_out)
        print("purged %s: %d files, %d events removed, %d kept -> %s" % (a.purge_player, res["files"], res["removed"], res["kept"], a.purge_out))
        print("note: host aggregate events cannot be un-aggregated; originals are untouched - delete them after verifying.")
        return 0
    if not a.paths:
        ap.error("give at least one directory or .jsonl file")
    S, rows = run_analysis(a.paths, a.observer, a.feedback, a.out, a.include_synthetic, a.catalog if os.path.isfile(a.catalog) else None)
    ok = sum(1 for r in rows if r["status"] == "ok")
    print("events accepted %d, duplicates %d, truncated %d, corrupt %d, rejected %d" % (S.load.accepted, S.load.duplicates, S.load.truncated, S.load.corrupt, sum(S.load.rejected.values())))
    print("matches %d, groups %d, strata %d, excluded synthetic %s" % (len(S.matches), len(S.groups), len(S.strata), dict(S.excluded) or "none"))
    print("metric rows %d (ok %d); reports in %s" % (len(rows), ok, a.out))
    return 0


if __name__ == "__main__":
    sys.exit(main())
