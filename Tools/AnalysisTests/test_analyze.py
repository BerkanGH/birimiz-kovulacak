"""Unit tests for Tools/scripts/analyze_playtests.py with hand-computed fixtures.

Run:  python -I -m unittest discover -s Tools/AnalysisTests
"""
import csv
import importlib.util
import itertools
import json
import os
import shutil
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPT = os.path.join(os.path.dirname(os.path.dirname(HERE)), "Scripts", "analyze_playtests.py")
if not os.path.exists(SCRIPT):
    SCRIPT = os.path.join(os.path.dirname(HERE), "scripts", "analyze_playtests.py")
_spec = importlib.util.spec_from_file_location("analyze_playtests", SCRIPT)
A = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(A)

_ids = itertools.count(1)


def _read(path):
    with open(path, encoding="utf-8") as f:
        return f.read()


def ev(name, t=0.0, auth="host", match="m-1", group="g-1", session="s-1", slot=None, pid=None, day=None, rnd=None, phase=None,
       task=None, cohort="friends_wave1", env="playtest", ruleset="v4.0", build="b1", pc=4, cov=None, eid=None, mono=None, extra=None, **params):
    params = dict(params, **(extra or {}))
    e = {"schema_version": 1, "event": name, "event_version": 1, "event_id": eid or ("t-%06d" % next(_ids)), "authority": auth,
         "timestamp_utc": "2026-11-01T20:%02d:%06.3fZ" % (int(t // 60), t % 60), "monotonic_elapsed_seconds": (100.0 + t) if mono is None else mono,
         "environment": env, "session_id": session, "anonymous_group_id": group, "player_count": pc, "build_version": build,
         "ruleset_version": ruleset, "test_cohort": cohort, "params": params}
    if match:
        e["match_id"] = match
    if slot is not None:
        e["player_slot"] = slot
    if pid:
        e["anonymous_player_id"] = pid
    if day is not None:
        e["day_index"] = day
    if rnd is not None:
        e["round_index"] = rnd
    if phase:
        e["phase"] = phase
    if task:
        e["task_id"] = task
    if cov is not None:
        e["observation_coverage"] = cov
    return e


def write(path, events, newline=True, extra_lines=()):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    lines = [json.dumps(e) for e in events] + list(extra_lines)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + ("\n" if newline else ""))


def get(rows, mid, grouping="all", gv="all", part="", strat=("b1", "v4.0")):
    for r in rows:
        if r["metric_id"] == mid and r["grouping"] == grouping and r["group_value"] == gv and r["partition"] == part \
                and (r["build_version"], r["ruleset_version"]) == strat:
            return {k: A._fmt(v) for k, v in r.items()}   # as written to the CSV
    raise AssertionError("row not found: %s %s=%s part=%s %s" % (mid, grouping, gv, part, strat))


def match_events(match="m-1", group="g-1", completed=True, abandoned_phase=None, **kw):
    out = [ev("match_started", 0, match=match, group=group, humans=4, bots=0, length="short", manager="none", recordings_enabled=True,
              lobby_fill_seconds=60.0, ready_seconds=20.0, **kw)]
    if completed:
        out.append(ev("week_completed", 900, match=match, group=group, duration_seconds=900.0, company_closed=False, fired=False, **kw))
    if abandoned_phase:
        out.append(ev("match_abandoned", 300, match=match, group=group, extra={"phase": abandoned_phase, "connected": 3}, **kw))
    return out


class Base(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.mkdtemp(prefix="analysis_test_")
        self.addCleanup(shutil.rmtree, self.dir, True)

    def run_an(self, events_by_file, include_synthetic=False, observer=(), feedback=(), catalog=None):
        for rel, evs in events_by_file.items():
            write(os.path.join(self.dir, "logs", rel), evs)
        S, rows = A.run_analysis([os.path.join(self.dir, "logs")], observer, feedback, os.path.join(self.dir, "out"), include_synthetic, catalog)
        return S, rows


class TestLoading(Base):
    def test_dedupe_across_host_client_and_reimport(self):
        evs = match_events()
        e2 = ev("moment_marked", 5, auth="client", slot=1, pid="p-a", eid="c-same", category="funny", source="player")
        S, rows = self.run_an({"host/events_s-1_0.jsonl": evs + [e2], "reimport/copy.jsonl": evs + [e2], "client/events_s-1_0.jsonl": [e2]})
        self.assertEqual(S.load.accepted, 3)
        self.assertEqual(S.load.duplicates, 4)
        self.assertEqual(get(rows, "B.match_completion")["denominator"], "1")

    def test_truncated_last_line_and_corrupt_line(self):
        evs = match_events()
        path = os.path.join(self.dir, "logs", "events_s-1_0.jsonl")
        os.makedirs(os.path.dirname(path))
        body = "\n".join(json.dumps(e) for e in evs[:1]) + "\nNOT JSON AT ALL\n" + json.dumps(evs[1]) + "\n" + json.dumps(ev("phase_exited", 5))[:40]
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(body)
        events, st = A.load_events([os.path.join(self.dir, "logs")])
        self.assertEqual((st.accepted, st.corrupt, st.truncated), (2, 1, 1))

    def test_envelope_validation_rejects(self):
        good = ev("match_started", 0, humans=4, bots=0)
        bad_schema = dict(good, event_id="x1", schema_version=2)
        missing = {k: v for k, v in dict(good, event_id="x2").items() if k != "authority"}
        bad_type = dict(good, event_id="x3", player_slot="two")
        bad_auth = dict(good, event_id="x4", authority="server")
        write(os.path.join(self.dir, "a.jsonl"), [good, bad_schema, missing, bad_type, bad_auth])
        _events, st = A.load_events([self.dir])
        self.assertEqual(st.accepted, 1)
        self.assertEqual(sum(st.rejected.values()), 4)
        self.assertEqual(st.rejected["unsupported_schema_version"], 1)
        self.assertEqual(st.rejected["missing_authority"], 1)

    def test_catalog_param_type_validation(self):
        cat = os.path.join(self.dir, "cat.json")
        with open(cat, "w", encoding="utf-8") as f:
            json.dump({"events": [{"name": "match_started", "params": {"humans": "i", "bots": "i"}}]}, f)
        write(os.path.join(self.dir, "a.jsonl"), [ev("match_started", 0, humans="four", bots=0, surplus=1), ev("brand_new_event", 1)])
        _events, st = A.load_events([self.dir], A.load_catalog(cat))
        self.assertEqual(st.param_issues["bad_type:match_started.humans"], 1)
        self.assertEqual(st.param_issues["unknown_param:match_started.surplus"], 1)
        self.assertEqual(st.unknown_events["brand_new_event"], 1)


class TestMath(unittest.TestCase):
    def test_interval_union_and_clip(self):
        self.assertEqual(A.interval_union([(0, 10), (5, 20)]), 20)
        self.assertEqual(A.interval_union([(0, 10), (10, 20), (30, 35)]), 25)
        self.assertEqual(A.interval_union([]), 0.0)
        self.assertEqual(A.interval_union(A.clip_intervals([(0, 10), (5, 20)], 0, 15)), 15)

    def test_wilson_and_hhi_gini(self):
        lo, hi = A.wilson(5, 10)
        self.assertAlmostEqual(lo, 0.2366, 3)
        self.assertAlmostEqual(hi, 0.7634, 3)
        lo, hi = A.wilson(0, 1)
        self.assertAlmostEqual(lo, 0.0, 6)
        self.assertAlmostEqual(hi, 0.7935, 3)
        self.assertEqual(A.wilson(0, 0), (None, None))
        self.assertAlmostEqual(A.hhi([1, 1]), 0.5)
        self.assertAlmostEqual(A.hhi([3, 1]), 0.625)
        self.assertAlmostEqual(A.gini([1, 1, 1]), 0.0)
        self.assertAlmostEqual(A.gini([0, 0, 0, 4]), 0.75)


def round_frame(day=0, rnd=0, planned=75.0, task="invoice_reconcile"):
    return [ev("phase_entered", 10, day=day, rnd=rnd, phase="RoundPlay", task=task, planned_duration_seconds=planned),
            ev("phase_exited", 85, day=day, rnd=rnd, phase="RoundPlay", task=task, duration_seconds=75.0, planned_duration_seconds=planned,
               completion_reason="timer", overrun_seconds=0.0)]


def call(cid, a, b, start_t, dur, day=0, rnd=0):
    return [ev("phone_call_requested", start_t - 2, day=day, rnd=rnd, caller=a, callee=b, partners_in_task=True),
            ev("phone_call_connected", start_t, day=day, rnd=rnd, call_id=cid, caller=a, callee=b, ring_seconds=2.0, partners_in_task=True),
            ev("phone_call_finished", start_t + dur, day=day, rnd=rnd, call_id=cid, caller=a, callee=b, duration_seconds=float(dur), end_reason="hangup", censored=False)]


def rps(slot, day=0, rnd=0, **kw):
    p = dict(player=slot, connected_seconds=0.0, calls_attempted=0, calls_connected=0, unique_partners=0, texts_sent=0, emails_sent=0,
             submitted=True, revisions=0, longest_gap_seconds=30.0, post_submit_wait_seconds=10.0, busy_failures=0, unanswered_attempts=0,
             comm_mode="none", blocked_seconds=0.0)
    p.update(kw)
    return ev("round_player_summary", 90, slot=slot, day=day, rnd=rnd, **p)


class TestPhone(Base):
    def test_derived_connected_seconds_union_and_clipping(self):
        # window RoundPlay: mono 110..185 (enter t=10, duration 75). Player 0: calls 120-130 and 125-140 -> union 20
        evs = match_events() + round_frame()
        evs += call(1, 0, 1, 20, 10) + call(2, 0, 2, 25, 15)   # t -> mono 100+t: 120-130 and 125-140
        evs += call(3, 3, 1, 80, 20)                            # mono 180-200, clipped at 185 -> 5 s for player 3
        S, _ = self.run_an({"h.jsonl": evs})
        d = A.derived_round_connected(S.matches[0])
        self.assertEqual(d[((0, 0), 0)], 20)
        self.assertEqual(d[((0, 0), 1)], 10 + 5)       # call1 10 s + call3 clipped 5 s
        self.assertEqual(d[((0, 0), 2)], 15)
        self.assertEqual(d[((0, 0), 3)], 5)

    def test_failed_missed_rejected_calls_do_not_count_for_participation(self):
        evs = match_events() + round_frame()
        evs += call(1, 0, 1, 20, 10)  # only players 0 and 1 connect
        evs += [ev("phone_call_requested", 30, day=0, rnd=0, caller=2, callee=3, partners_in_task=True),
                ev("phone_call_failed", 30.2, day=0, rnd=0, caller=2, callee=3, reason="busy"),
                ev("phone_call_requested", 40, day=0, rnd=0, caller=3, callee=2, partners_in_task=True),
                ev("phone_call_missed", 60, day=0, rnd=0, caller=3, callee=2),
                ev("phone_call_requested", 45, day=0, rnd=0, caller=2, callee=0, partners_in_task=True),
                ev("phone_call_rejected", 48, day=0, rnd=0, caller=2, callee=0, ring_seconds=3.0)]
        evs += [rps(s) for s in range(4)]
        S, rows = self.run_an({"h.jsonl": evs})
        r = get(rows, "C.phone_participation")
        self.assertEqual((r["numerator"], r["denominator"]), ("2", "4"))
        self.assertEqual(r["value"], "0.5")
        self.assertEqual(get(rows, "D.busy_failure_rate")["value"], "%.6g" % (1 / 4))
        self.assertEqual(get(rows, "D.unanswered_rate")["numerator"], "2")
        # players 2 and 3 attempted but never connected (2 of 3 attempters: 0 connected, 2, 3)
        r2 = get(rows, "D.attempt_never_connect")
        self.assertEqual((r2["numerator"], r2["denominator"]), ("2", "3"))

    def test_phone_round_summary_denominator_is_observed_players(self):
        evs = match_events() + round_frame()
        evs.append(ev("phone_round_summary", 90, day=0, rnd=0, cov=0.75, eligible=4, observed=3, participating=2, participation_rate=0.667,
                      attempts=3, connected=1, distinct_pairs=1, connected_seconds_union=10.0))
        evs.append(ev("phone_round_summary", 90, day=0, rnd=1, cov=0.0, eligible=4, observed=0, participating=0, participation_rate="not_measured",
                      attempts=0, connected=0, distinct_pairs=0, connected_seconds_union=0.0))
        S, rows = self.run_an({"h.jsonl": evs})
        r = get(rows, "C.phone_participation")
        self.assertEqual((r["numerator"], r["denominator"]), ("2", "3"))   # the 0-observed round is skipped, not 0%
        o = get(rows, "C.observed_share")
        self.assertEqual((o["numerator"], o["denominator"]), ("3", "8"))

    def test_no_contact_player_rounds_are_excluded_from_latency_not_zero(self):
        evs = match_events() + round_frame()
        evs += [rps(0, first_contact_latency_seconds=10.0, comm_mode="voice"), rps(1, first_contact_latency_seconds=20.0, comm_mode="voice"),
                rps(2, first_contact_latency_seconds=30.0, comm_mode="voice"), rps(3)]
        S, rows = self.run_an({"h.jsonl": evs})
        r = get(rows, "D.first_contact_latency")
        self.assertEqual((r["numerator"], r["denominator"], r["value"]), ("60", "3", "20"))
        nc = get(rows, "D.no_contact_share")
        self.assertEqual((nc["numerator"], nc["denominator"]), ("1", "4"))

    def test_partner_hhi_uses_connected_seconds(self):
        evs = match_events() + round_frame()
        evs += call(1, 0, 1, 20, 10) + call(2, 0, 2, 40, 30)   # player 0: partner shares 10/40 and 30/40
        S, rows = self.run_an({"h.jsonl": evs})
        r = get(rows, "D.partner_hhi")   # only player 0 has >= 2 connected calls
        self.assertEqual(r["denominator"], "1")
        self.assertAlmostEqual(float(r["value"]), 0.25 ** 2 + 0.75 ** 2, 6)


class TestMissingData(Base):
    def test_missing_events_are_not_measured_never_zero(self):
        S, rows = self.run_an({"h.jsonl": match_events()})
        for mid in ("D.first_contact_latency", "C.phone_participation", "H.playback_success", "K.reconnect_success", "F.longest_gap"):
            r = get(rows, mid)
            self.assertEqual(r["status"], "not_measured", mid)
            self.assertEqual(r["value"], "", mid)
        # never-implemented layers of the shareable-moment KPI
        self.assertEqual(get(rows, "M.export")["status"], "not_measured")
        self.assertEqual(get(rows, "M.external_share")["status"], "not_measured")
        self.assertEqual(get(rows, "H.speaking_time_per_player")["status"], "not_measured")

    def test_unknown_end_is_not_counted_as_abandonment_or_completion(self):
        evs = match_events("m-1", "g-1") + match_events("m-2", "g-2", completed=False)   # m-2 has no end event
        S, rows = self.run_an({"h.jsonl": evs})
        c = get(rows, "B.match_completion")
        self.assertEqual((c["numerator"], c["denominator"]), ("1", "1"))
        u = get(rows, "B.match_unknown_end")
        self.assertEqual((u["numerator"], u["denominator"]), ("1", "2"))


class TestGroupLevel(Base):
    def test_n_groups_vs_n_matches_and_completion(self):
        evs = (match_events("m-1", "g-1") + match_events("m-2", "g-1", completed=False, abandoned_phase="RoundPlay")
               + match_events("m-3", "g-2"))
        S, rows = self.run_an({"h.jsonl": evs})
        r = get(rows, "B.match_completion")
        self.assertEqual((r["numerator"], r["denominator"], r["n_groups"], r["n_matches"]), ("2", "3", "2", "3"))
        self.assertEqual(r["status"], "ok")
        ab = get(rows, "B.abandon_phase", part="RoundPlay")
        self.assertEqual((ab["numerator"], ab["denominator"]), ("1", "3"))
        # a single group never reaches status ok
        one = [e for e in evs if e["anonymous_group_id"] == "g-2"]
        write(os.path.join(self.dir, "single", "h.jsonl"), one)
        _S, rows3 = A.run_analysis([os.path.join(self.dir, "single")], (), (), os.path.join(self.dir, "out3"))
        r3 = get(rows3, "B.match_completion")
        self.assertEqual((r3["n_groups"], r3["status"]), ("1", "insufficient_data"))

    def test_rulesets_are_never_pooled(self):
        evs = match_events("m-1", "g-1") + match_events("m-2", "g-2", ruleset="v4.1", build="b2")
        S, rows = self.run_an({"h.jsonl": evs})
        a = get(rows, "B.match_completion", strat=("b1", "v4.0"))
        b = get(rows, "B.match_completion", strat=("b2", "v4.1"))
        self.assertEqual((a["denominator"], b["denominator"]), ("1", "1"))
        self.assertFalse(any(r["metric_id"] == "B.match_completion" and r["build_version"] == "unknown" for r in rows))

    def test_group_binary_uses_wilson_over_groups(self):
        def grp(g, mid, voluntary, auto=False):
            out = match_events(mid, g) + [ev("rematch_prompt_shown", 905, auth="client", match=mid, group=g, slot=0, phase="WeekResults")]
            if voluntary or auto:
                out.append(ev("rematch_started", 1000, match=mid + "n", group=g, previous_match_id=mid, returning_humans=4, auto=auto, requests=0 if auto else 3))
            return out
        evs = grp("g-1", "m-1", True) + grp("g-2", "m-2", False, auto=True) + grp("g-3", "m-3", False)
        S, rows = self.run_an({"h.jsonl": evs})
        r = get(rows, "L.voluntary_rematch")
        self.assertEqual((r["numerator"], r["denominator"]), ("1", "2"))    # auto restart group is excluded
        lo, hi = A.wilson(1, 2)
        self.assertAlmostEqual(float(r["ci_low"]), lo, 4)
        self.assertEqual(r["ci_method"], "wilson_groups")

    def test_rematch_intent_is_not_actual_rematch(self):
        mid, g = "m-1", "g-1"
        evs = match_events(mid, g) + [ev("rematch_prompt_shown", 905, auth="client", match=mid, group=g, slot=0, phase="WeekResults"),
                                      ev("rematch_requested", 910, auth="client", match=mid, group=g, slot=0, seconds_after_results=10.0)]
        S, rows = self.run_an({"h.jsonl": evs})
        self.assertEqual(get(rows, "L.rematch_intent")["numerator"], "1")
        self.assertEqual(get(rows, "L.voluntary_rematch")["numerator"], "0")


class TestSyntheticAndPurge(Base):
    def test_synthetic_excluded_by_default_and_bannered_when_included(self):
        evs = match_events("m-1", "g-1", cohort="synthetic_fixture", env="development")
        evs = [dict(e, test_cohort="synthetic_fixture", environment="development") for e in evs]
        S, rows = self.run_an({"h.jsonl": evs})
        self.assertEqual(len(S.matches), 0)
        self.assertEqual(S.excluded["fixture"], 1)
        md = _read(os.path.join(self.dir, "out", "PLAYTEST_SUMMARY.md"))
        self.assertNotIn(A.BANNER, md)
        self.assertIn(get(rows, "B.match_completion", strat=("none", "none"))["status"], ("insufficient_data", "not_measured"))
        S2, rows2 = A.run_analysis([os.path.join(self.dir, "logs")], (), (), os.path.join(self.dir, "out_syn"), True, None)
        md2 = _read(os.path.join(self.dir, "out_syn", "PLAYTEST_SUMMARY.md"))
        self.assertTrue(md2.startswith("# " + A.BANNER))
        self.assertEqual(len(S2.matches), 1)
        charts = os.listdir(os.path.join(self.dir, "out_syn", "charts"))
        self.assertTrue(charts)
        svg = _read(os.path.join(self.dir, "out_syn", "charts", charts[0]))
        self.assertIn(A.BANNER, svg)

    def test_bots_and_bot_cohort_are_synthetic(self):
        bot_match = [dict(e, test_cohort="bot_sim") for e in match_events("m-b", "g-b")]
        with_bots = [ev("match_started", 0, match="m-w", group="g-w", humans=3, bots=1, length="short", manager="none", recordings_enabled=True,
                        lobby_fill_seconds=1.0, ready_seconds=1.0), ev("week_completed", 9, match="m-w", group="g-w", duration_seconds=9.0, company_closed=False, fired=False)]
        S, _ = self.run_an({"h.jsonl": bot_match + with_bots + match_events("m-h", "g-h")})
        self.assertEqual([c.key for c in S.matches], ["m-h"])
        self.assertEqual(S.excluded["bot_sim"], 1)
        self.assertEqual(S.excluded["human_bots"], 1)

    def test_purge_removes_subject_and_slot_references(self):
        evs = [ev("player_slot_map", 0, slot=0, anonymous_player_id="p-aaa", is_bot=False),
               ev("player_slot_map", 0, slot=1, anonymous_player_id="p-bbb", is_bot=False),
               ev("choice_submitted", 5, slot=0, pid="p-aaa", player=0, field="main", category=1, latency_seconds=3.0, after_contact=False, contact_channel="none", sabotage_option=False),
               ev("choice_submitted", 6, slot=1, pid="p-bbb", player=1, field="main", category=1, latency_seconds=4.0, after_contact=False, contact_channel="none", sabotage_option=False),
               ev("phone_call_requested", 7, caller=0, callee=1, partners_in_task=True),
               ev("phone_call_requested", 8, caller=1, callee=1 + 1, partners_in_task=True),
               ev("moment_marked", 9, auth="client", slot=0, pid="p-aaa", category="funny", source="player"),
               ev("moment_marked", 9, auth="client", slot=1, pid="p-bbb", category="funny", source="player")]
        write(os.path.join(self.dir, "in", "host", "a_0.jsonl"), evs)
        out = os.path.join(self.dir, "purged")
        res = A.purge_player([os.path.join(self.dir, "in")], "p-aaa", out)
        self.assertEqual(res["removed"], 4)   # slot map entry, own choice, own client event, call naming slot 0
        self.assertEqual(res["kept"], 4)
        text = _read(os.path.join(out, "in", "host", "a_0.jsonl"))
        self.assertNotIn("p-aaa", text)
        self.assertIn("p-bbb", text)
        self.assertIn('"caller": 1', text)
        # source untouched
        self.assertIn("p-aaa", _read(os.path.join(self.dir, "in", "host", "a_0.jsonl")))

    def test_purge_cli(self):
        write(os.path.join(self.dir, "in", "a.jsonl"), [ev("moment_marked", 1, auth="client", slot=0, pid="p-zzz", category="funny", source="player"), ev("match_started", 0, humans=1, bots=0)])
        rc = A.main([os.path.join(self.dir, "in"), "--purge-player", "p-zzz", "--purge-out", os.path.join(self.dir, "o")])
        self.assertEqual(rc, 0)
        lines = _read(os.path.join(self.dir, "o", "in", "a.jsonl")).strip().splitlines()
        self.assertEqual(len(lines), 1)


class TestEconomyAndObserver(Base):
    def test_rank1_is_richest_with_chance_baseline(self):
        evs = match_events()
        evs += [ev("career_breakdown", 900, slot=i, player=i, not_fired=100, company_target=75, objective=0, eotm=0, sabotage_penalty=0, total=t, rank=r, money_end=m, quality=q)
                for i, (t, r, m, q) in enumerate([(300, 1, 10, 50), (200, 2, 4, 90), (100, 3, 2, 10), (100, 3, 4, 20)])]
        evs += [ev("player_slot_map", 0, slot=i, anonymous_player_id="p-%d" % i, is_bot=False) for i in range(4)]
        S, rows = self.run_an({"h.jsonl": evs})
        r = get(rows, "J.rank1_is_richest")
        self.assertEqual((r["numerator"], r["denominator"]), ("1", "1"))
        self.assertEqual(get(rows, "J.rank1_is_richest_baseline")["value"], "0.25")
        self.assertEqual(get(rows, "J.rank1_is_best_quality")["numerator"], "0")   # best quality is slot 1, rank 1 is slot 0
        self.assertEqual(get(rows, "J.rank1_is_best_quality_baseline")["value"], "0.25")

    def test_income_concentration_ignores_transfers(self):
        # Regression (lead, bot runs): transfers netted into income gave a 340% top share and a Gini of 3.5.
        evs = match_events()
        evs += [ev("player_slot_map", 0, slot=i, anonymous_player_id="p-%d" % i, is_bot=False) for i in range(4)]
        evs += [ev("income_credited", 100, player=0, amount=4, raw_amount=4, reason="task_income", block_total=4),
                ev("income_credited", 101, player=1, amount=4, raw_amount=4, reason="task_income", block_total=4),
                ev("income_credited", 102, player=0, amount=-3, raw_amount=-3, reason="transfer_out", block_total=1),
                ev("income_credited", 102, player=1, amount=3, raw_amount=3, reason="transfer_in", block_total=7)]
        S, rows = self.run_an({"h.jsonl": evs})
        self.assertEqual(get(rows, "J.income_top_share")["value"], "0.5")
        self.assertLessEqual(float(get(rows, "J.income_gini")["value"]), 1.0)

    def test_client_session_end_pairs_across_adopted_session(self):
        # Regression (lead, bot runs): a client adopts the host session id, so its end event is in another context.
        evs = [ev("session_started", 0, auth="client", match=None, session="s-own", pid="p-a", platform="windows", language="tr",
                  fullscreen=True, first_launch=False),
               ev("session_ended", 900, auth="client", match=None, session="s-host", pid="p-a", duration_seconds=900.0, reason="quit")]
        S, rows = self.run_an({"c.jsonl": evs})
        self.assertEqual(get(rows, "K.possible_crash")["numerator"], "0")

    def test_observation_coverage_is_capped_per_round(self):
        # Regression (lead, bot runs): a player who dropped mid-round still has a summary -> coverage above 100%.
        evs = match_events()
        rf = ev("round_finished", 100, output=80, max_output=100, submitted=3, connected=3, early_submit=False)
        rf["task_number"] = 0
        evs.append(rf)
        for i in range(4):
            e = ev("round_player_summary", 100, slot=i, player=i, connected_seconds=60.0, calls_attempted=0, calls_connected=0,
                   unique_partners=0, texts_sent=0, emails_sent=0, submitted=True, revisions=0, longest_gap_seconds=5.0,
                   post_submit_wait_seconds=1.0, busy_failures=0, unanswered_attempts=0, comm_mode="none", blocked_seconds=0.0)
            e["task_number"] = 0
            evs.append(e)
        S, rows = self.run_an({"h.jsonl": evs})
        r = get(rows, "F.observation_coverage")
        self.assertEqual((r["numerator"], r["denominator"]), ("3", "3"))

    def test_observer_blank_is_missing_and_notable_groups(self):
        obs = os.path.join(self.dir, "obs.csv")
        cols = ["anonymous_group_id", "build_version", "ruleset_version", "player_count", "match_id", "day_index", "round_index", "phase",
                "moment_timestamp", "natural_accusation_occurred", "accusation_referenced_game_evidence", "funny_betrayal", "unexpected_accusation",
                "surprising_outcome", "notable_funny_moment", "notes"]
        rows_ = [["g-1", "b1", "v4.0", 4, "m-1", 0, 0, "MeetingDiscussion", "00:09:00", "yes", "yes", "yes", "no", "no", "no", "should be ignored"],
                 ["g-1", "b1", "v4.0", 4, "m-1", 1, 0, "MeetingDiscussion", "00:15:00", "yes", "", "no", "no", "no", "no", ""],
                 ["g-2", "b1", "v4.0", 4, "m-2", 0, 0, "MeetingDiscussion", "00:09:00", "no", "", "no", "no", "no", "no", ""]]
        with open(obs, "w", newline="", encoding="utf-8") as f:
            w = csv.writer(f)
            w.writerow(cols)
            w.writerows(rows_)
        evs = match_events("m-1", "g-1") + match_events("m-2", "g-2")
        S, rows = self.run_an({"h.jsonl": evs}, observer=[obs])
        g = get(rows, "M.groups_notable")
        self.assertEqual((g["numerator"], g["denominator"], g["n_groups"]), ("1", "2", "2"))
        e = get(rows, "O.accusation_evidence")     # blank answer is not counted as No
        self.assertEqual((e["numerator"], e["denominator"]), ("1", "1"))
        self.assertEqual(get(rows, "M.notable_per_match")["numerator"], "1")
        self.assertEqual(S.observer[0].get("notes"), None)   # free text is never parsed


class TestPacingAndValue(Base):
    def test_call_counted_once_globally_and_once_per_participant(self):
        evs = match_events() + round_frame() + call(1, 0, 1, 20, 10) + call(2, 2, 3, 40, 30)
        S, rows = self.run_an({"h.jsonl": evs})
        g = get(rows, "D.call_duration")
        self.assertEqual((g["numerator"], g["denominator"], g["value"]), ("40", "2", "20"))
        p = get(rows, "D.call_duration_participant")
        self.assertEqual((p["numerator"], p["denominator"]), ("80", "4"))

    def test_completion_reasons_and_carried_break_seconds(self):
        evs = match_events() + [
            ev("phase_exited", 50, day=0, rnd=0, phase="RoundPlay", task="t1", duration_seconds=40.0, planned_duration_seconds=75.0, completion_reason="timer_shortened", overrun_seconds=0.0),
            ev("phase_exited", 150, day=0, rnd=1, phase="RoundPlay", task="t2", duration_seconds=75.0, planned_duration_seconds=75.0, completion_reason="timer", overrun_seconds=2.0),
            ev("round_finished", 50, day=0, rnd=0, phase="RoundPlay", task="t1", output=80, max_output=100, submitted=4, connected=4, early_submit=True, saved_seconds=35.0, carried_seconds=35.0),
            ev("round_finished", 150, day=0, rnd=1, phase="RoundPlay", task="t2", output=70, max_output=100, submitted=3, connected=4, early_submit=False, saved_seconds=0.0, carried_seconds=0.0)]
        S, rows = self.run_an({"h.jsonl": evs})
        t = get(rows, "B.timer_ending_share")
        self.assertEqual((t["numerator"], t["denominator"]), ("1", "2"))
        e = get(rows, "B.early_submit_share")
        self.assertEqual((e["numerator"], e["denominator"]), ("1", "2"))
        c = get(rows, "B.carried_seconds")
        self.assertEqual((c["value"], c["denominator"]), ("35", "1"))      # only early-submit rounds carry time
        o = get(rows, "B.overrun_rate", "phase", "RoundPlay")
        self.assertEqual((o["numerator"], o["denominator"]), ("1", "2"))   # 2.0 s overrun counts; 0 does not
        r = get(rows, "B.completion_reason", part="timer_shortened")
        self.assertEqual((r["numerator"], r["denominator"]), ("1", "2"))

    def test_quality_by_comm_mode_and_dominance_needs_enough_samples(self):
        evs = match_events() + round_frame()
        for slot, mode, q in ((0, "none", 40), (1, "voice", 80), (2, "voice", 60), (3, "text", 50)):
            evs.append(rps(slot, comm_mode=mode))
            evs.append(ev("choice_outcome_resolved", 92, slot=slot, day=0, rnd=0, player=slot, quality=q, income=1, cooperation_credit=mode != "none", cooperation_opportunity=True))
        S, rows = self.run_an({"h.jsonl": evs})
        self.assertEqual(get(rows, "E.quality", part="voice")["value"], "70")
        self.assertEqual(get(rows, "E.quality", part="none")["value"], "40")
        self.assertEqual(get(rows, "E.cooperation_rate", part="voice")["value"], "1")
        self.assertEqual(get(rows, "E.cooperation_rate", part="none")["value"], "0")
        self.assertEqual(get(rows, "E.silent_not_dominant")["status"], "insufficient_data")  # < 3 samples per mode: no verdict (events exist, so not "not_measured")
        self.assertEqual(get(rows, "E.silent_not_dominant")["value"], "")


if __name__ == "__main__":
    unittest.main()
