#!/usr/bin/env python3
"""Synthetic telemetry fixtures for "Birimiz Kovulacak" (stdlib only).

Generates realistic JSONL following docs/ANALYTICS_ARCHITECTURE.md (section 2 envelope, section 5 events) for several friend
groups: host + client files, rotated files, duplicates across files, a truncated line, an abandoned match, a company-closed
match, a no-call round, failed/missed calls, a consent-suppressed player and bots.

EVERYTHING HERE IS SYNTHETIC: environment "development", test_cohort "synthetic_fixture". Never use for design conclusions.

    python -I Tools/scripts/telemetry_fixtures.py --out docs/analytics/fixtures [--seed 7]
"""
import argparse
import csv
import datetime
import json
import os
import random

TASKS = ["invoice_reconcile", "budget_split", "customer_spec", "report_review", "room_booking", "archive_visitor", "bonus_pool",
         "server_code", "phishing_check", "shift_cover", "vendor_pick", "supply_swap"]
FAMILY = {"invoice_reconcile": "P", "budget_split": "A", "customer_spec": "P", "report_review": "R", "room_booking": "A",
          "archive_visitor": "R", "bonus_pool": "P", "server_code": "A", "phishing_check": "A", "shift_cover": "A",
          "vendor_pick": "A", "supply_swap": "A"}
PLAY = {3: 65, 4: 75, 5: 85, 6: 95}
T0 = datetime.datetime(2026, 11, 1, 20, 0, 0, tzinfo=datetime.timezone.utc)


def fnv1a64(s):
    h = 0xCBF29CE484222325
    for b in s.encode("utf-8"):
        h ^= b
        h = (h * 0x100000001B3) & 0xFFFFFFFFFFFFFFFF
    return "%016x" % h


class Match:
    def __init__(self, rng, group, session, mid, n, humans_consent, bots, ruleset, build, start_offset, exps, scenario=None):
        """humans_consent: list of bool for the human slots 0..h-1; bots: number of bot slots after the humans."""
        self.rng, self.group, self.session, self.mid = rng, group, session, mid
        self.h_count = len(humans_consent)
        self.n = n
        assert self.h_count + bots == n
        self.consent = {i: bool(c) for i, c in enumerate(humans_consent)}
        self.bots = list(range(self.h_count, n))
        self.pid = {i: "p-%s" % fnv1a64("%s|%s|%d" % (group, "player", i)) + fnv1a64("x%s%d" % (group, i)) for i in range(n)}
        self.ruleset, self.build, self.exps = ruleset, build, exps
        self.sc = scenario or {}
        self.t_off = start_offset
        self.mono0 = 100.0
        self.host, self.client = [], {i: [] for i in range(self.h_count)}
        self.counter = 0
        self.call_id = 0
        self.fault = {i: 5 for i in range(n)}
        self.warn = {i: 0 for i in range(n)}
        self.income = {i: 0 for i in range(n)}
        self.quality_sum = {i: [] for i in range(n)}
        self.output_total = 0
        self.max_total = 0
        self.incidents = []

    # -- envelope -------------------------------------------------------------------------------------------------
    def wall(self, t):
        return (T0 + datetime.timedelta(seconds=self.t_off + t)).strftime("%Y-%m-%dT%H:%M:%S.") + "%03dZ" % int((t % 1) * 1000)

    def _env(self, name, authority, t, params, slot, day, rnd, phase, task, cov, with_match=True):
        e = {"schema_version": 1, "event": name, "event_version": 1, "authority": authority}
        self.counter += 1
        if authority == "host":
            e["event_id"] = "h-" + fnv1a64("%s|%s|%d" % (self.mid, name, self.counter))
        else:
            e["event_id"] = "c-%032x" % self.rng.getrandbits(128)
        e["timestamp_utc"] = self.wall(t)
        e["monotonic_elapsed_seconds"] = round(self.mono0 + t, 3)
        e["environment"] = "development"
        e["session_id"] = self.session
        if with_match:
            e["match_id"] = self.mid
            e["week_id"] = 1
        e["anonymous_group_id"] = self.group
        if slot is not None:
            e["anonymous_player_id"] = self.pid[slot]
            e["player_slot"] = slot
            e["is_bot"] = slot in self.bots
        e["player_count"] = self.n
        if day is not None:
            e["day_index"] = day
        if rnd is not None:
            e["round_index"] = rnd
        if phase:
            e["phase"] = phase
        if task:
            e["task_id"] = task
            e["task_version"] = 1
        e["game_version"] = "0.4.0"
        e["build_version"] = self.build
        e["ruleset_version"] = self.ruleset
        e["test_cohort"] = "synthetic_fixture"
        e["experiments"] = dict(self.exps)
        if cov is not None:
            e["observation_coverage"] = round(cov, 3)
        e["params"] = params
        return e

    @property
    def coverage(self):
        return sum(1 for c in self.consent.values() if c) / float(self.h_count)

    def h(self, name, t, params, slot=None, day=None, rnd=None, phase=None, task=None, agg=False, private_slot=None):
        """Host event. private_slot: consent S gate for the subject; suppressed when the subject did not consent."""
        if not agg:
            for k in ("player", "defender", "buyer", "actor", "sender", "voter", "slot", "requester", "cosigner", "caller", "callee",
                      "from", "to", "exposed_by", "recipient", "victim"):
                v = params.get(k)
                if isinstance(v, int) and not isinstance(v, bool) and v < self.h_count and not self.consent[v]:
                    return
            tv = params.get("target")
            if isinstance(tv, int) and not isinstance(tv, bool) and tv < self.h_count and not self.consent[tv]:
                if name in ("meeting_vote_submitted", "final_vote_submitted"):
                    params = dict(params, target="unconsented")
                else:
                    return
        gate = private_slot if private_slot is not None else slot
        if gate is not None and gate < self.h_count and not self.consent[gate]:
            return
        if gate is not None and gate in self.bots:
            return
        self.host.append(self._env(name, "host", t, params, slot, day, rnd, phase, task, self.coverage if agg else None))

    def c(self, slot, name, t, params, day=None, rnd=None, phase=None, task=None, with_match=True):
        if not self.consent[slot]:
            return
        self.client[slot].append(self._env(name, "client", t, params, slot, day, rnd, phase, task, None, with_match))

    # -- simulation -----------------------------------------------------------------------------------------------
    def phase(self, name, t, dur, planned, day, rnd, task, reason="timer", extra=None):
        self.h("phase_entered", t, {"planned_duration_seconds": float(planned)}, day=day, rnd=rnd, phase=name, task=task, agg=True)
        p = {"duration_seconds": round(dur, 2), "planned_duration_seconds": float(planned), "completion_reason": reason,
             "overrun_seconds": round(max(dur - planned, 0.0), 2)}
        if extra:
            p.update(extra)
        self.h("phase_exited", t + dur, p, day=day, rnd=rnd, phase=name, task=task, agg=True)
        return t + dur

    def run(self):
        rng, n, hc = self.rng, self.n, self.h_count
        humans = list(range(hc))
        sc = self.sc
        t = 0.0
        # client / lobby side
        for s in humans:
            t_join = -90 + rng.uniform(0, 40)
            self.c(s, "session_started", t_join - 5, {"platform": "windows", "language": rng.choice(["tr", "en"]), "fullscreen": True, "first_launch": rng.random() < .3}, with_match=False)
            self.c(s, "lobby_join_attempted", t_join, {"kind": "manual"}, with_match=False)
            if sc.get("join_fail") and s == 1:
                self.c(s, "lobby_join_failed", t_join + 2, {"reason": "net.connect_failed"}, with_match=False)
                self.c(s, "lobby_join_attempted", t_join + 8, {"kind": "manual"}, with_match=False)
            self.c(s, "lobby_joined", t_join + 4, {"reconnect": False, "wait_seconds": round(rng.uniform(1.5, 6), 2)}, with_match=False)
        self.h("lobby_created", -95, {"length": "short", "recordings_enabled": self.exps.get("recording_awareness", "on") == "on"}, agg=True)
        for s in humans:
            self.h("player_ready_changed", -40 + s * 4, {"slot": s, "ready": True, "seconds_since_join": round(rng.uniform(5, 40), 1)}, slot=s)
        self.h("match_start_attempted", -2, {"humans": hc, "bots": len(self.bots), "ready": hc}, agg=True)
        self.h("match_started", 0, {"humans": hc, "bots": len(self.bots), "length": "short", "manager": "none",
                                    "recordings_enabled": self.exps.get("recording_awareness", "on") == "on",
                                    "lobby_fill_seconds": round(rng.uniform(40, 150), 1), "ready_seconds": round(rng.uniform(8, 40), 1)}, agg=True)
        for s in range(n):
            if s < hc and not self.consent[s]:
                continue
            self.h("player_slot_map", 0, {"slot": s, "anonymous_player_id": self.pid[s], "is_bot": s in self.bots})
        self.h("week_started", 0.1, {"days": 3, "tasks": 6}, agg=True)
        order = rng.sample(TASKS, 6)
        abandon = sc.get("abandon")  # (day, round)
        for day in range(3):
            self.h("day_started", t, {"task_a": order[day * 2], "task_b": order[day * 2 + 1]}, day=day, agg=True)
            t = self.phase("Morning", t, 12 if day else 40, 12 if day else 40, day, None, None)
            for rnd in range(2):
                task = order[day * 2 + rnd]
                t = self.phase("RoundIntro", t, 25 if day == 0 and rnd == 0 else 12, 25 if day == 0 and rnd == 0 else 12, day, rnd, task)
                t, carried = self.play_round(day, rnd, task, t, abandon == (day, rnd))
                if t is None:
                    return self.finish_abandoned(day, rnd)
                t = self.phase("RoundReveal", t, 10, 10, day, rnd, task)
                s_ = rng.choice(humans)
                self.h("recovery_opportunities", t, {"players_with_options": len([p for p in humans if self.fault[p] > 5]), "players": hc}, day=day, rnd=rnd, phase="Break", agg=True)
                t = self.phase("Break", t, 15 + carried, 15 + carried, day, rnd, None)
            if day == 0 and rng.random() < .8:  # a remedial attempt for the blamed
                req, cos = rng.sample(humans, 2)
                self.h("remedial_requested", t - 10, {"requester": req, "cosigner": cos}, day=day, phase="Break")
                acc = rng.random() < .6
                self.h("remedial_resolved", t - 5, {"requester": req, "cosigner": cos, "accepted": acc, "output_added": 10 if acc else 0, "blame_delta": -2 if acc else 0}, day=day, phase="Break")
                if acc:
                    self.max_total += 0
                    self.output_total += 10
            t = self.meeting(day, t, last=(day == 2))
        # final
        closed = bool(sc.get("closed"))
        permille = int(1000 * self.output_total / max(self.max_total, 1))
        if closed:
            permille = min(permille, 520)
        else:
            permille = max(permille, 700)
        self.h("final_started", t, {"roster": hc, "company_closed": closed}, day=2, phase="FinalDefense", agg=True)
        fired = None
        if not closed:
            for i, s in enumerate(sorted(humans, key=lambda x: (-self.fault[x], -self.warn[x]))):
                self.h("defense_started", t, {"defender": s, "order_index": i}, day=2, phase="FinalDefense", private_slot=s)
                t += 15
                self.h("defense_finished", t, {"defender": s, "order_index": i, "duration_seconds": 15.0}, day=2, phase="FinalDefense", private_slot=s)
            for s in humans:
                tgt = rng.choice(["fire_nobody", "fire_nobody", max(humans, key=lambda x: self.fault[x]), "abstain"])
                self.h("final_vote_submitted", t + rng.uniform(1, 18), {"voter": s, "target": tgt, "changed": False}, day=2, phase="FinalVote", private_slot=s)
                if sc.get("force_fire") and s != max(humans, key=lambda x: self.fault[x]):
                    pass
            top = max(humans, key=lambda x: self.fault[x])
            votes = sum(1 for e in self.host if e["event"] == "final_vote_submitted" and e["params"]["target"] == top)
            fn = sum(1 for e in self.host if e["event"] == "final_vote_submitted" and e["params"]["target"] == "fire_nobody")
            needed = hc // 2 + 1
            fired = top if votes >= needed else None
            self.h("final_vote_resolved", t + 25, {"outcome": "fired" if fired is not None else "no_majority", "roster": hc, "majority_needed": needed,
                                                  "top_votes": votes, "fire_nobody_votes": fn}, day=2, phase="FinalVote", agg=True)
            t += 35
            if fired is not None:
                self.h("player_fired", t, {"player": fired, "blame": self.fault[fired], "warnings": self.warn[fired], "was_blame_leader": True}, day=2, phase="FinalVerdict")
        else:
            self.h("final_vote_resolved", t + 5, {"outcome": "company_closed", "roster": hc, "majority_needed": hc // 2 + 1, "top_votes": 0, "fire_nobody_votes": 0}, day=2, phase="FinalVote", agg=True)
            self.h("company_failed", t + 6, {"final_permille": permille}, day=2, phase="FinalVerdict", agg=True)
            t += 15
        t += 10
        self.h("company_result", t, {"final_permille": permille, "target_percent": 65, "closed": closed, "exposed_incidents": sum(1 for i in self.incidents if i[2]),
                                     "incidents": len(self.incidents)}, day=2, phase="WeekResults", agg=True)
        self.results(t, closed, fired)
        self.h("week_finished", t + 40, {"completion_reason": "results", "duration_seconds": round(t + 40, 1)}, phase="WeekResults", agg=True)
        self.h("week_completed", t + 41, {"duration_seconds": round(t + 41, 1), "company_closed": closed, "fired": fired is not None}, phase="WeekResults", agg=True)
        # rematch / session end (client)
        for s in humans:
            self.c(s, "rematch_prompt_shown", t + 5, {}, phase="WeekResults")
            if sc.get("rematch_requested") and s in sc["rematch_requested"]:
                self.c(s, "rematch_requested", t + 12 + s, {"seconds_after_results": round(12 + s, 1)}, phase="WeekResults")
            if s != sc.get("crash_player"):
                self.c(s, "session_ended", t + 60, {"duration_seconds": round(t + 150, 1), "reason": "quit"}, with_match=False)
        return self

    def finish_abandoned(self, day, rnd):
        t = self.sc["abandon_t"]
        self.h("match_abandoned", t, {"phase": "RoundPlay", "connected": self.h_count - 1}, day=day, rnd=rnd, phase="RoundPlay", agg=True)
        for s in range(self.h_count):
            if s == 2:
                self.c(s, "session_interrupted", t, {"reason": "host_lost", "phase": "RoundPlay", "mid_match": True}, day=day, rnd=rnd, phase="RoundPlay")
        return self

    def play_round(self, day, rnd, task, t, abandon_here):
        rng, n, hc = self.rng, self.n, self.h_count
        humans = list(range(hc))
        planned = PLAY[n]
        start = t
        self.h("phase_entered", t, {"planned_duration_seconds": float(planned)}, day=day, rnd=rnd, phase="RoundPlay", task=task, agg=True)
        self.h("round_started", t, {"family": FAMILY[task], "play_seconds": float(planned), "first_time": day == 0, }, day=day, rnd=rnd, phase="RoundPlay", task=task, agg=True)
        for s in humans:
            self.c(s, "choice_ui_opened", t + 1, {"task_id": task, "seconds_into_phase": 1.0}, day=day, rnd=rnd, phase="RoundPlay", task=task)
        if abandon_here:
            self.sc["abandon_t"] = start + 30
            return None, 0
        no_calls = self.sc.get("no_call_round") == (day, rnd)
        busy_until = {s: -1 for s in humans}
        calls = []
        intents = []
        if not no_calls:
            for s in humans:
                for _ in range(rng.choice([0, 1, 1, 1, 2])):
                    intents.append((start + rng.uniform(4, planned - 28), s, rng.choice([x for x in humans if x != s])))
        intents.sort()
        attempted = {s: 0 for s in humans}
        unans = {s: 0 for s in humans}
        busyf = {s: 0 for s in humans}
        for t0, a, b in intents:
            attempted[a] += 1
            self.h("phone_call_requested", t0, {"caller": a, "callee": b, "partners_in_task": FAMILY[task] == "P"}, day=day, rnd=rnd, phase="RoundPlay", task=task)
            roll = rng.random()
            if busy_until[a] > t0 or busy_until[b] > t0:
                busyf[a] += 1
                self.h("phone_call_failed", t0 + 0.2, {"caller": a, "callee": b, "reason": "busy"}, day=day, rnd=rnd, phase="RoundPlay", task=task)
            elif roll < 0.74:
                ring = rng.uniform(2, 6)
                dur = min(rng.uniform(8, 38), start + planned - (t0 + ring) - 1)
                if dur <= 2:
                    continue
                self.call_id += 1
                cid = self.call_id
                self.h("phone_call_connected", t0 + ring, {"call_id": cid, "caller": a, "callee": b, "ring_seconds": round(ring, 2), "partners_in_task": FAMILY[task] == "P"}, day=day, rnd=rnd, phase="RoundPlay", task=task)
                self.h("phone_call_finished", t0 + ring + dur, {"call_id": cid, "caller": a, "callee": b, "duration_seconds": round(dur, 2), "end_reason": "hangup", "censored": False}, day=day, rnd=rnd, phase="RoundPlay", task=task)
                busy_until[a] = busy_until[b] = t0 + ring + dur
                calls.append((t0 + ring, t0 + ring + dur, a, b))
            elif roll < 0.86:
                unans[a] += 1
                self.h("phone_call_missed", t0 + 20, {"caller": a, "callee": b}, day=day, rnd=rnd, phase="RoundPlay", task=task)
            elif roll < 0.93:
                unans[a] += 1
                self.h("phone_call_rejected", t0 + 3, {"caller": a, "callee": b, "ring_seconds": 3.0}, day=day, rnd=rnd, phase="RoundPlay", task=task)
            else:
                self.h("phone_call_failed", t0 + 0.5, {"caller": a, "callee": b, "reason": rng.choice(["dead_line", "absent"])}, day=day, rnd=rnd, phase="RoundPlay", task=task)
        # per player
        early = rng.random() < 0.5
        latest = planned - (22 if early else 4)
        subs = {}
        for s in humans:
            if rng.random() < 0.05:
                subs[s] = None
            else:
                subs[s] = start + rng.uniform(16, latest)
        all_sub = all(v is not None for v in subs.values())
        end = start + planned
        reason = "timer"
        if all_sub and max(subs.values()) + 8 < start + planned:
            end = max(subs.values()) + rng.uniform(2, 8)
            reason = "timer_shortened"
        dur = end - start
        saved = planned - dur
        carried = min(saved, 45.0) if reason == "timer_shortened" else 0.0
        part = 0
        obs = 0
        outs = 0
        maxo = 100
        summaries = {}
        for s in humans:
            ints = [(a_, b_) for (a_, b_, x, y) in calls if s in (x, y)]
            ints = [(a_, min(b_, end)) for a_, b_ in ints if a_ < end]
            partners = {(y if x == s else x) for (a_, b_, x, y) in calls if s in (x, y)}
            first = min([a_ for a_, _ in ints]) - start if ints else None
            texts = rng.choice([0, 0, 1, 2]) if rng.random() < .6 else 0
            for _k in range(texts):
                self.h("message_sent", start + rng.uniform(5, dur - 2), {"sender": s, "channel": "text", "scope": "one", "recipient": rng.choice([x for x in humans if x != s]),
                                                                         "length_bucket": "short", "is_reply": False, "template": False, "preset": False}, day=day, rnd=rnd, phase="RoundPlay", task=task)
            conn = bool(ints)
            mode = "mixed" if conn and texts else "voice" if conn else "text" if texts else "none"
            # gaps
            merged = []
            for a_, b_ in sorted(ints):
                if merged and a_ <= merged[-1][1]:
                    merged[-1][1] = max(merged[-1][1], b_)
                else:
                    merged.append([a_, b_])
            cur, gap = start, 0.0
            for a_, b_ in merged:
                gap = max(gap, a_ - cur)
                cur = b_
            gap = max(gap, end - cur)
            union = sum(b_ - a_ for a_, b_ in merged)
            sub_t = subs[s]
            q = min(100, rng.randint(40, 70) + (20 if conn else 0) + (5 if texts else 0)) if sub_t is not None else 0
            if sub_t is not None:
                self.h("choice_submitted", sub_t, {"player": s, "field": "main", "category": rng.randint(0, 3), "latency_seconds": round(sub_t - start, 2),
                                                   "after_contact": bool(first is not None and start + first < sub_t), "contact_channel": "voice" if conn else "none",
                                                   "sabotage_option": False}, slot=s, day=day, rnd=rnd, phase="RoundPlay", task=task)
                if rng.random() < .18:
                    self.h("choice_changed", sub_t + 3, {"player": s, "field": "main", "category": rng.randint(0, 3), "latency_seconds": round(sub_t + 3 - start, 2),
                                                         "after_contact": conn, "contact_channel": "voice" if conn else "none", "sabotage_option": False, "previous_category": 0}, slot=s, day=day, rnd=rnd, phase="RoundPlay", task=task)
            else:
                self.h("choice_timed_out", end, {"player": s, "reason": "missed"}, slot=s, day=day, rnd=rnd, phase="RoundPlay", task=task)
                self.fault[s] += 1
                self.h("fault_points_changed", end + 1, {"player": s, "delta": 1, "reason": "missed_task", "blame_after": self.fault[s]}, slot=s, day=day, rnd=rnd, phase="RoundPlay", task=task)
            income = (2 if q >= 60 else 0)
            self.income[s] += income
            self.quality_sum[s].append(q)
            self.h("choice_outcome_resolved", end + 2, {"player": s, "quality": q, "income": income, "cooperation_credit": conn and q >= 60, "cooperation_opportunity": FAMILY[task] != "A" or conn}, slot=s, day=day, rnd=rnd, phase="RoundReveal", task=task)
            if income:
                self.h("income_credited", end + 2, {"player": s, "amount": income, "raw_amount": income, "reason": "task", "block_total": self.income[s]}, slot=s, day=day, rnd=rnd, phase="RoundReveal", task=task)
            if self.consent[s]:
                obs += 1
                part += 1 if conn else 0
            outs += q
            summaries[s] = {"player": s, "connected_seconds": round(union, 2), "calls_attempted": attempted[s], "calls_connected": len([1 for c_ in calls if s in (c_[2], c_[3])]),
                            "unique_partners": len(partners), "texts_sent": texts, "emails_sent": 0, "submitted": sub_t is not None, "revisions": 0,
                            "longest_gap_seconds": round(gap, 2), "post_submit_wait_seconds": round(end - sub_t, 2) if sub_t is not None else 0.0,
                            "busy_failures": busyf[s], "unanswered_attempts": unans[s], "comm_mode": mode, "blocked_seconds": 0.0}
            if first is not None:
                summaries[s]["first_contact_latency_seconds"] = round(first, 2)
        for s, p in summaries.items():
            self.h("round_player_summary", end + 2, p, slot=s, day=day, rnd=rnd, phase="RoundReveal", task=task)
        self.h("phone_round_summary", end + 2, {"eligible": hc, "observed": obs, "participating": part,
                                                "participation_rate": round(part / obs, 3) if obs else "not_measured", "attempts": sum(attempted.values()),
                                                "connected": len(calls), "distinct_pairs": len({tuple(sorted((c_[2], c_[3]))) for c_ in calls}),
                                                "connected_seconds_union": round(sum(p["connected_seconds"] for p in summaries.values()) / 2, 2)},
               day=day, rnd=rnd, phase="RoundReveal", task=task, agg=True)
        out = min(maxo, int(outs / max(hc, 1)))
        loss = 0
        if (rng.random() < 0.15 or self.sc.get("sab") == (day, rnd)) and not self.sc.get("no_sabotage"):
            actor = rng.choice(humans)
            loss = rng.choice([15, 30])
            exposed = rng.random() < .5 or self.sc.get("sab") == (day, rnd)
            self.incidents.append((actor, task, exposed))
            self.h("incident_created", end + 1, {"actor": actor, "kind": rng.choice(["InvoicePadding", "StockSkim"]), "victim": rng.choice([x for x in humans if x != actor])}, slot=actor, day=day, rnd=rnd, phase="RoundPlay", task=task)
            if exposed:
                by = rng.choice([x for x in humans if x != actor])
                self.h("sabotage_verified", end + 40, {"actor": actor, "kind": "InvoicePadding", "exposed_by": by, "seconds_since_incident": 39.0}, slot=actor, day=day, rnd=rnd, phase="Break", task=task)
                self.fault[actor] += 2
                self.h("fault_points_changed", end + 41, {"player": actor, "delta": 2, "reason": "exposed_sabotage", "blame_after": self.fault[actor]}, slot=actor, day=day, rnd=rnd, phase="Break")
        out = max(0, out - loss)
        if self.sc.get("low_output"):
            out = int(out * 0.5)
        self.output_total += out
        self.max_total += maxo
        self.h("task_output_resolved", end + 2, {"output": out, "max_output": maxo, "sabotage_loss": loss, "groups": 1, "groups_full": 1 if out == maxo else 0}, day=day, rnd=rnd, phase="RoundReveal", task=task, agg=True)
        self.h("round_finished", end, {"output": out, "max_output": maxo, "submitted": sum(1 for v in subs.values() if v is not None), "connected": hc,
                                       "early_submit": reason == "timer_shortened", "saved_seconds": round(saved, 2), "carried_seconds": round(carried, 2)}, day=day, rnd=rnd, phase="RoundPlay", task=task, agg=True)
        self.h("phase_exited", end, {"duration_seconds": round(dur, 2), "planned_duration_seconds": float(planned), "completion_reason": reason,
                                     "overrun_seconds": 0.0}, day=day, rnd=rnd, phase="RoundPlay", task=task, agg=True)
        if rng.random() < .3 and hc > 2:
            s = rng.choice(humans)
            self.h("evidence_purchased", end + 20, {"buyer": s, "offer_kind": "bank_ledger", "price": 2, "purchase_index": 1}, slot=s, day=day, rnd=rnd, phase="Break")
        if rng.random() < .25 and hc > 2:
            a, b = rng.sample(humans, 2)
            self.h("money_transferred", end + 21, {"from": a, "to": b, "amount": 1, "phase": "Break"}, day=day, rnd=rnd, phase="Break")
        for s in humans:
            if rng.random() < .35:
                self.c(s, "ui_action_rejected", start + rng.uniform(3, dur), {"verb": "call", "failure": "busy"}, day=day, rnd=rnd, phase="RoundPlay", task=task)
            if rng.random() < .4 and dur > 55:
                self.c(s, "voice_quality_summary", start + 60, {"loss_pct": round(rng.uniform(0, 4), 2), "jitter_ms": round(rng.uniform(5, 40), 1), "underruns": rng.choice([0, 0, 1])}, day=day, rnd=rnd, phase="RoundPlay", task=task)
            if rng.random() < .05:
                self.c(s, "moment_marked", end + 5, {"category": rng.choice(["funny", "betrayal", "accusation", "surprise"]), "source": "player"}, day=day, rnd=rnd, phase="RoundReveal", task=task)
        if self.sc.get("tech") == (day, rnd):
            self.c(0, "technical_failure", start + 10, {"category": "voice_exception", "type": "InvalidOperationException"}, day=day, rnd=rnd, phase="RoundPlay", task=task)
        return end, carried

    def meeting(self, day, t, last):
        rng, hc = self.rng, self.h_count
        humans = list(range(hc))
        nrec = 0 if self.exps.get("recording_awareness") == "off" else rng.choice([1, 2])
        self.h("meeting_started", t, {"is_final": last, "recordings_scheduled": nrec, "complaints": rng.choice([0, 1]), "incident_effects": 0}, day=day, phase="MeetingDossier", agg=True)
        t0 = t
        for k in range(nrec):
            a, b = rng.sample(humans, 2)
            ok = rng.random() < .88
            ctx = rng.choice(["invoice_reconcile", "budget_split", "none"]) if self.exps.get("rec_select") == "context" else rng.choice(["none", "none", "budget_split"])
            self.h("recording_playback_started", t + 3 + 9 * k, {"pick_index": k, "candidates": rng.randint(2, 9), "chosen_rank": 0, "speech_ms": rng.randint(2000, 6500), "snippet_ms": 7000,
                                                                "call_length_ms": rng.randint(9000, 38000), "caller": a, "callee": b, "context_task": ctx, "same_group": True,
                                                                "prior_exposures": rng.choice([0, 0, 1]), "selection": self.exps.get("rec_select", "legacy")}, day=day, phase="MeetingDossier")
            self.h("recording_playback_finished", t + 10 + 9 * k, {"pick_index": k, "played": ok, "actual_duration_seconds": 7.0 if ok else 0.0,
                                                                   "failure": "none" if ok else "no_speech"}, day=day, phase="MeetingDossier", agg=True)
        t = self.phase("MeetingDossier", t, 15 + 9 * nrec, 15 + 9 * nrec, day, None, None)
        self.h("meeting_discussion_started", t, {}, day=day, phase="MeetingDiscussion", agg=True)
        t = self.phase("MeetingDiscussion", t, 50 if not last else 75, 50 if not last else 75, day, None, None)
        self.h("meeting_discussion_finished", t, {"duration_seconds": 50.0, "chat_messages": rng.randint(0, 6), "presentations": rng.randint(0, 2)}, day=day, phase="MeetingDiscussion", agg=True)
        leader = max(humans, key=lambda x: (self.fault[x], -x))
        votes = {}
        for s in humans:
            r = rng.random()
            tgt = leader if r < .35 else rng.choice([x for x in humans if x != s]) if r < .6 else "no_action" if r < .9 else "abstain"
            votes[s] = tgt
            self.h("meeting_vote_submitted", t + rng.uniform(2, 18), {"voter": s, "target": tgt, "changed": False}, day=day, phase="MeetingVote", private_slot=s)
        cnt = {}
        for v in votes.values():
            if isinstance(v, int):
                cnt[v] = cnt.get(v, 0) + 1
        noact = sum(1 for v in votes.values() if v == "no_action")
        top = max(cnt.items(), key=lambda kv: kv[1]) if cnt else (None, 0)
        warn_target = None
        outcome = "no_action"
        if top[0] is not None and top[1] >= 2 and top[1] > noact and list(cnt.values()).count(top[1]) == 1:
            if self.warn[top[0]] >= 2:
                outcome = "nomination_capped"
            else:
                outcome = "warning"
                warn_target = top[0]
        self.h("meeting_vote_resolved", t + 20, {"outcome": outcome, "top_votes": top[1], "no_action_votes": noact, "eligible": hc,
                                                "voted": sum(1 for v in votes.values() if v != "abstain"), "target_was_blame_leader": warn_target == leader if warn_target is not None else False}, day=day, phase="MeetingVote", agg=True)
        t = t + 20
        if warn_target is not None:
            before = self.fault[warn_target]
            self.fault[warn_target] += 3
            self.warn[warn_target] += 1
            self.h("warning_assigned", t + 1, {"target": warn_target, "blame_before": before, "blame_after": self.fault[warn_target], "warnings_after": self.warn[warn_target]}, day=day, phase="MeetingVerdict")
            self.h("fault_points_changed", t + 1, {"player": warn_target, "delta": 3, "reason": "warning", "blame_after": self.fault[warn_target]}, slot=warn_target, day=day, phase="MeetingVerdict")
        t += 6
        self.h("meeting_finished", t, {"duration_seconds": round(t - t0, 1)}, day=day, phase="MeetingVerdict", agg=True)
        self.h("day_finished", t, {"day_output": 0, "day_max": 200, "provisional_permille": int(1000 * self.output_total / max(self.max_total, 1))}, day=day, agg=True)
        return t

    def results(self, t, closed, fired):
        hc = self.h_count
        humans = list(range(hc))
        totals = {}
        for s in humans:
            qs = self.quality_sum[s]
            qual = int(sum(qs) / max(len(qs), 1))
            obj = self.rng.random() < .4
            self.h("objective_assigned", 1.0, {"player": s, "kind": self.rng.choice(["quiet_saver", "collector"])}, slot=s)
            self.h("objective_completed", t, {"player": s, "kind": "quiet_saver", "completed": obj}, slot=s, phase="WeekResults")
            not_fired = 0 if (closed or fired == s) else 100
            target = 0 if closed else 75
            pen = -50 * sum(1 for i in self.incidents if i[0] == s and i[2])
            totals[s] = (not_fired, target, 60 if obj and not closed else 0, 0, pen, qual)
        eotm = None if closed else max(humans, key=lambda x: totals[x][5])
        if eotm is not None and fired != eotm:
            a = totals[eotm]
            totals[eotm] = (a[0], a[1], a[2], 40, a[4], a[5])
        sums = {s: sum(v[:5]) for s, v in totals.items()}
        for s in humans:
            rank = 1 + sum(1 for x in humans if sums[x] > sums[s])
            v = totals[s]
            self.h("career_breakdown", t, {"player": s, "not_fired": v[0], "company_target": v[1], "objective": v[2], "eotm": v[3], "sabotage_penalty": v[4],
                                           "total": sums[s], "rank": rank, "money_end": self.income[s], "quality": v[5]}, slot=s, phase="WeekResults")


# =====================================================================================================================
# scenarios
# =====================================================================================================================
def write_jsonl(path, events, truncate_last=False, split=None):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    lines = [json.dumps(e, separators=(",", ":")) for e in events]
    if split:
        k = len(lines) // 2
        parts = [lines[:k], lines[k:]]
        base = path[:-len("_0.jsonl")]
        for i, pl in enumerate(parts):
            with open("%s_%d.jsonl" % (base, i), "w", encoding="utf-8", newline="\n") as f:
                f.write("\n".join(pl) + "\n")
        return
    body = "\n".join(lines) + "\n"
    if truncate_last:
        body = body[:-1]
        body = body[: body.rfind("{") + 25]
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(body)


def generate(out_dir, seed=7):
    rng = random.Random(seed)
    BUILD_A, BUILD_B = "202611012000-a1b2c3", "202611021200-d4e5f6"
    plans = [
        # group, session, [(match scenario...)], n, consent list, bots, ruleset, build, exps
        ("G01", "s-fx01", 4, [True] * 4, 0, "v4.0-hybrid", BUILD_A, {"rec_select": "context", "early_submit": "carry", "recording_awareness": "on"}),
        ("G02", "s-fx02", 3, [True] * 3, 0, "v4.0-hybrid", BUILD_A, {"rec_select": "legacy", "early_submit": "carry", "recording_awareness": "on"}),
        ("G03", "s-fx03", 5, [True, True, True, True, False], 0, "v4.0-hybrid", BUILD_A, {"rec_select": "context", "early_submit": "shorten", "recording_awareness": "off"}),
        ("G04", "s-fx04", 6, [True] * 4, 2, "v4.1-hybrid", BUILD_B, {"rec_select": "legacy", "early_submit": "carry", "recording_awareness": "on"}),
    ]
    summary = []
    offset = 0.0
    for gi, (group, session, n, consent, bots, ruleset, build, exps) in enumerate(plans):
        def mk(idx, scen, off):
            return Match(rng, group, session, "m-%s-%d" % (group.lower(), idx), n, consent, bots, ruleset, build, off, exps, scen)
        matches = []
        if group == "G01":
            m1 = mk(1, {"rematch_requested": [0, 1, 2], "no_sabotage": False, "join_fail": True, "tech": (1, 0), "crash_player": 3, "sab": (0, 1)}, offset).run()
            m2 = mk(2, {"rematch_requested": [0, 2]}, offset + 2400).run()
            m2.h("rematch_started", -1, {"previous_match_id": m1.mid, "returning_humans": 4, "auto": False, "requests": 3}, agg=True)
            matches = [m1, m2]
        elif group == "G02":
            matches = [mk(1, {"abandon": (1, 0)}, offset).run()]
        elif group == "G03":
            matches = [mk(1, {"closed": True, "low_output": True, "no_sabotage": True, "rematch_requested": [0]}, offset).run()]
        else:
            matches = [mk(1, {"no_call_round": (0, 1), "rematch_requested": [1]}, offset).run()]
        offset += 4800
        for mi, m in enumerate(matches):
            hostdir = os.path.join(out_dir, "jsonl", group, "host")
            hf = os.path.join(hostdir, "events_%s_m%d_0.jsonl" % (session, mi))
            hevents = sorted(m.host, key=lambda e: e["timestamp_utc"])
            if group == "G01" and mi == 0:
                write_jsonl(hf, hevents, split=True)  # rotated host log
                reimp = os.path.join(out_dir, "jsonl", group, "reimport")
                write_jsonl(os.path.join(reimp, "events_%s_m%d_copy.jsonl" % (session, mi)), hevents)  # duplicate re-import
            else:
                write_jsonl(hf, hevents)
            for slot, evs in m.client.items():
                if evs:
                    write_jsonl(os.path.join(out_dir, "jsonl", group, "client_p%d" % slot, "events_%s_m%d_0.jsonl" % (session, mi)),
                                sorted(evs, key=lambda e: e["timestamp_utc"]), truncate_last=(group == "G02" and slot == 0))  # killed while writing
            summary.append((group, m.mid, n, len(m.host), sum(len(v) for v in m.client.values())))
    # observer + feedback fixtures (synthetic)
    ocols = ["anonymous_group_id", "build_version", "ruleset_version", "player_count", "match_id", "day_index", "round_index", "phase", "moment_timestamp",
             "natural_accusation_occurred", "accusation_referenced_game_evidence", "recording_was_intelligible", "recording_was_relevant", "recording_lacked_context",
             "recording_created_reaction", "recording_was_referenced_later", "players_reconsidered_accusation", "players_appeared_confused", "players_appeared_excluded",
             "meeting_felt_too_short", "meeting_felt_too_long", "funny_betrayal", "unexpected_accusation", "surprising_outcome", "notable_funny_moment",
             "players_requested_rematch", "observer_id", "coding_confidence", "moderator_required_repeat"]
    rows = [
        ["G01", BUILD_A, "v4.0-hybrid", 4, "m-g01-1", 0, 1, "MeetingDiscussion", "00:09:40", "yes", "yes", "yes", "yes", "no", "yes", "yes", "yes", "no", "no", "no", "no", "yes", "no", "no", "yes", "yes", "O1", "high", "no"],
        ["G01", BUILD_A, "v4.0-hybrid", 4, "m-g01-1", 2, 0, "MeetingDiscussion", "00:21:05", "yes", "no", "yes", "no", "yes", "no", "no", "no", "yes", "no", "no", "yes", "no", "yes", "no", "no", "yes", "O1", "medium", "no"],
        ["G01", BUILD_A, "v4.0-hybrid", 4, "m-g01-2", 1, 0, "MeetingDiscussion", "00:08:10", "no", "", "yes", "yes", "no", "yes", "no", "", "no", "no", "no", "no", "no", "no", "no", "no", "no", "O1", "high", "no"],
        ["G02", BUILD_A, "v4.0-hybrid", 3, "m-g02-1", 0, 0, "RoundPlay", "00:03:50", "no", "", "", "", "", "", "", "", "yes", "yes", "no", "no", "no", "no", "no", "no", "no", "O2", "medium", "no"],
        ["G03", BUILD_A, "v4.0-hybrid", 5, "m-g03-1", 1, 1, "MeetingDiscussion", "00:11:30", "yes", "yes", "no", "no", "yes", "no", "no", "no", "no", "no", "yes", "no", "no", "no", "yes", "no", "no", "O2", "medium", "no"],
        ["G04", BUILD_B, "v4.1-hybrid", 6, "m-g04-1", 0, 1, "RoundPlay", "00:05:00", "no", "", "yes", "yes", "no", "yes", "yes", "", "no", "no", "no", "no", "no", "no", "no", "no", "yes", "O3", "high", "no"],
    ]
    with open(os.path.join(out_dir, "observer_fixture.csv"), "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(ocols)
        w.writerows(rows)
    fcols = ["anonymous_group_id", "anonymous_player_id", "match_id", "player_count", "ruleset_version", "build_version", "q_contact_helped", "q_reach_people",
             "q_too_much_waiting", "q_recording_awareness", "q_accusations_game_events", "q_recover_bad_start", "q_play_again", "q_wanted_to_share", "q_hear_over_music"]
    ans = ["Yes", "Somewhat", "No", "Not sure"]
    frng = random.Random(seed + 1)
    with open(os.path.join(out_dir, "feedback_fixture.csv"), "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(fcols)
        for group, mid, n, build, rs in (("G01", "m-g01-1", 4, BUILD_A, "v4.0-hybrid"), ("G03", "m-g03-1", 5, BUILD_A, "v4.0-hybrid"), ("G04", "m-g04-1", 6, BUILD_B, "v4.1-hybrid")):
            for i in range(min(n, 4)):
                w.writerow([group, "", mid, n, rs, build] + [frng.choice(ans) for _ in range(9)])
    return summary


def main(argv=None):
    ap = argparse.ArgumentParser(description="Generate SYNTHETIC telemetry fixtures (not playtest evidence).")
    ap.add_argument("--out", default=os.path.join("docs", "analytics", "fixtures"))
    ap.add_argument("--seed", type=int, default=7)
    a = ap.parse_args(argv)
    summary = generate(a.out, a.seed)
    for g, m, n, hc, cc in summary:
        print("%s %s players=%d host_events=%d client_events=%d" % (g, m, n, hc, cc))
    print("wrote fixtures to", a.out, "(SYNTHETIC - not playtest evidence)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
