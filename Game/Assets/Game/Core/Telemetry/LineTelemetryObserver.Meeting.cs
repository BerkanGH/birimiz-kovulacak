using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;

namespace BirimizKovulacak.Telemetry
{
    public sealed partial class LineTelemetryObserver
    {
        struct PendingVote { public int Voter, Target; public bool Changed; public double Time; }

        readonly List<PendingVote> _voteQueue = new List<PendingVote>();
        readonly Dictionary<int, int> _lastVote = new Dictionary<int, int>();
        readonly Dictionary<int, double[]> _picks = new Dictionary<int, double[]>(); // pick index -> candidates, selection
        readonly Dictionary<int, int> _exposures = new Dictionary<int, int>();
        readonly Dictionary<int, int> _playingByCall = new Dictionary<int, int>();
        int _defender = LineIds.None;
        double _defenderStart, _nextDefensePoll;
        int _defenderIndex;

        // ---- meetings ----------------------------------------------------------------------------

        void BeginMeeting(double t, LineView v)
        {
            _meetingStart = t;
            _meetingIsFinal = v != null && v.Meeting != null && v.Meeting.IsFinal;
            _voteQueue.Clear();
            _lastVote.Clear();
            var df = B("day_finished", t, "dayfin|" + (v != null ? v.Day : _e.Day)).Int("day_output", _dayOutput).Int("day_max", _dayMax);
            if (v != null && v.Company != null && v.Company.ProvisionalPermille >= 0) df.Int("provisional_permille", v.Company.ProvisionalPermille);
            df.Emit();
            _dayOutput = 0; _dayMax = 0;
            int complaints = 0, effects = 0;
            if (v != null && v.Meeting != null)
            {
                foreach (var d in v.Meeting.Dossier)
                {
                    if (d.Kind == DossierKind.Complaints) complaints += Math.Max(0, d.N1);
                    else if (d.Kind == DossierKind.IncidentEffect) effects++;
                }
                if (complaints == 0) complaints = v.Meeting.Complaints.Count;
            }
            int picks = 0;
            try { picks = _e.RecordingPicks != null ? _e.RecordingPicks.Count : 0; } catch (Exception) { }
            B("meeting_started", t, "ms|" + _phaseSeq).Bool("is_final", _meetingIsFinal).Int("recordings_scheduled", picks).Int("complaints", complaints).Int("incident_effects", effects).Emit();
        }

        void OnVoteCast(LineEvent e)
        {
            int voter = e.Actor;
            if (!Valid(voter)) return;
            if (_phase != LinePhase.MeetingVote && _phase != LinePhase.FinalVote) return;
            var v = ViewOf(voter, e.Time);
            int target = v != null && v.Meeting != null ? v.Meeting.MyVote : LineIds.None;
            int prev;
            bool changed = _lastVote.TryGetValue(voter, out prev) && prev != target;
            _lastVote[voter] = target;
            // held in memory until the votes become public (verdict): the host log never shows ballots mid-vote
            _voteQueue.Add(new PendingVote { Voter = voter, Target = target, Changed = changed, Time = e.Time });
        }

        void FlushVotes(double t, bool final)
        {
            string name = final ? "final_vote_submitted" : "meeting_vote_submitted";
            string prefix = final ? "fvote|" : "vote|";
            foreach (var p in _voteQueue)
                B(name, p.Time, prefix + _t.Context.DayIndex + "|" + p.Voter + "|" + Ms(p.Time)).Slot("voter", p.Voter).Ballot("target", p.Target).Bool("changed", p.Changed).Emit();
            _voteQueue.Clear();
        }

        void OnBallot(LineEvent e)
        {
            var v = PublicView(e.Time);
            var outcome = (VoteOutcome)e.B;
            string o = TelemetryNames.Of(outcome);
            bool final = _meetingIsFinal || _phase == LinePhase.FinalVote || _phase == LinePhase.FinalVerdict;
            if (final)
            {
                int roster = v != null && v.Meeting != null ? v.Meeting.Roster.Count : 0;
                B("final_vote_resolved", e.Time, "fballot").Enum("outcome", o).Int("roster", roster)
                    .Int("majority_needed", v != null && v.Meeting != null ? v.Meeting.MajorityNeeded : 0).Int("top_votes", e.A).Int("fire_nobody_votes", Math.Max(0, e.C)).Emit();
                return;
            }
            int eligible = 0, voted = 0;
            bool leader = false;
            if (v != null)
            {
                int maxBlame = int.MinValue;
                foreach (var p in v.Players) { if (p.Connected) eligible++; if (p.Voted) voted++; if (p.Blame > maxBlame) maxBlame = p.Blame; }
                var nominee = PlayerIn(v, e.Target);
                leader = nominee != null && nominee.Blame == maxBlame;
            }
            var b = B("meeting_vote_resolved", e.Time, "ballot|" + _t.Context.DayIndex).Enum("outcome", o).Int("top_votes", e.A).Int("no_action_votes", Math.Max(0, e.C)).Int("eligible", eligible).Int("voted", voted);
            if (e.Target >= 0) b.Bool("target_was_blame_leader", leader);
            b.Emit();
        }

        void OnWarning(LineEvent e)
        {
            int target = e.Target;
            if (!Valid(target)) return;
            var v = PublicView(e.Time);
            var pv = PlayerIn(v, target);
            int after = pv != null ? pv.Blame : _blame[target];
            var b = B("warning_assigned", e.Time, "warn|" + _t.Context.DayIndex + "|" + target).Slot("target", target).Int("blame_after", after).Int("warnings_after", pv != null ? pv.Warnings : 0);
            if (_warnDelta[target] != 0) b.Int("blame_before", after - _warnDelta[target]);
            b.Emit();
            _warnDelta[target] = 0;
        }

        void OnBlameChanged(LineEvent e)
        {
            int p = e.Target;
            if (!Valid(p)) return;
            _blame[p] += e.A;
            var reason = (BlameReason)e.B;
            if (reason == BlameReason.Warning) _warnDelta[p] = e.A;
            if (reason == BlameReason.Remedial) _remedialBlame[p] = e.A;
            if (reason == BlameReason.None) return;
            B("fault_points_changed", e.Time, "blame|" + p + "|" + e.B + "|" + Ms(e.Time) + "|" + e.A).Slot("player", p).Int("delta", e.A).Enum("reason", reason).Int("blame_after", _blame[p]).Emit();
        }

        void OnFired(LineEvent e)
        {
            int p = e.Target;
            if (!Valid(p)) return;
            var v = PublicView(e.Time);
            var pv = PlayerIn(v, p);
            int blame = pv != null ? pv.Blame : _blame[p];
            bool leader = true;
            if (v != null) foreach (var o in v.Players) if (o.Blame > blame) leader = false;
            B("player_fired", e.Time, "fired").Slot("player", p).Int("blame", blame).Int("warnings", pv != null ? pv.Warnings : 0).Bool("was_blame_leader", leader).Emit();
        }

        void OnEvidence(LineEvent e)
        {
            int actor = e.Actor;
            if (!Valid(actor)) return;
            _lastPresenter = actor;
            _lastPresentTime = e.Time;
            if (_phase == LinePhase.MeetingDiscussion) _discussionPresentations++;
            var v = ViewOf(actor, e.Time);
            if (v == null || v.Meeting == null) return;
            LinePresentationView found = null;
            foreach (var p in v.Meeting.Presentations) if (p.Presenter == actor && p.Record != null && p.Record.Id == e.A) found = p;
            if (found == null) return;
            B("evidence_presented", e.Time, "evid|" + actor + "|" + e.A + "|" + Ms(e.Time)).Slot("presenter", actor)
                .Enum("record_kind", found.Record.Kind).Enum("record_source", found.Record.Source)
                .Bool("exposed", found.ExposedIncident >= 0).Bool("repeat", found.Repeat).Float("record_age_seconds", Math.Max(0, e.Time - found.Record.Time)).Emit();
        }

        void OnIncidentExposed(LineEvent e)
        {
            int actor = e.Target;
            if (!Valid(actor)) return;
            var b = B("sabotage_verified", e.Time, "exposed|" + e.A).Slot("actor", actor).Enum("kind", (IncidentKind)e.B);
            if (Valid(_lastPresenter) && Math.Abs(e.Time - _lastPresentTime) < 1.0) b.Slot("exposed_by", _lastPresenter);
            double at;
            if (_incidentTime.TryGetValue(e.A, out at)) b.Float("seconds_since_incident", Math.Max(0, e.Time - at));
            b.Emit();
        }

        // ---- economy -----------------------------------------------------------------------------

        void OnHostIncome(LineEvent e)
        {
            int p = e.Actor;
            if (!Valid(p)) return;
            _blockIncome[p] += e.A;
            B("income_credited", e.Time, "inc|" + p + "|" + Ms(e.Time) + "|" + e.B).Slot("player", p).Int("amount", e.A).Int("raw_amount", e.C >= 0 ? e.C : e.A)
                .Enum("reason", (MoneyReason)e.B).Int("block_total", _blockIncome[p]).Emit();
        }

        void OnHostIncident(LineEvent e)
        {
            _incidentTime[e.B] = e.Time;
            var b = B("incident_created", e.Time, "incident|" + e.B).Slot("actor", e.Actor).Enum("kind", (IncidentKind)e.A);
            if (Valid(e.Target)) b.Slot("victim", e.Target);
            b.Emit();
        }

        void OnTransfer(LineEvent e)
        {
            B("money_transferred", e.Time, "xfer|" + e.Actor + "|" + e.Target + "|" + e.A + "|" + Ms(e.Time)).Slot("from", e.Actor).Slot("to", e.Target).Int("amount", e.A)
                .Enum("phase", TelemetryNames.Of(_phase == LinePhase.None ? LinePhase.Morning : _phase)).Emit();
        }

        void OnPurchase(LineEvent e)
        {
            int buyer = e.AudiencePlayer;
            if (!Valid(buyer)) return;
            int idx = ++_purchases[buyer];
            B("evidence_purchased", e.Time, "buy|" + buyer + "|" + e.A + "|" + Ms(e.Time)).Slot("buyer", buyer).Enum("offer_kind", (OfferKind)(e.A / 100)).Int("price", e.B).Int("purchase_index", idx).Emit();
        }

        void OnRemedialRequested(LineEvent e)
        {
            B("remedial_requested", e.Time, "remreq|" + e.Actor + "|" + e.AudiencePlayer + "|" + e.A).Slot("requester", e.Actor).Slot("cosigner", e.AudiencePlayer).Emit();
        }

        void OnRemedialResolved(LineEvent e)
        {
            var b = B("remedial_resolved", e.Time, "remres|" + e.Actor + "|" + e.Target + "|" + e.B).Slot("requester", e.Actor).Slot("cosigner", e.Target).Bool("accepted", e.A == 1);
            if (e.A == 1 && Valid(e.Actor) && _remedialBlame[e.Actor] != 0) b.Int("blame_delta", _remedialBlame[e.Actor]);
            if (e.A == 1 && e.C >= 0) b.Int("output_added", e.C);
            b.Emit();
        }

        void OnHostObjective(LineEvent e)
        {
            if (!Valid(e.Actor)) return;
            var kind = (ObjectiveKind)e.A;
            if (kind == ObjectiveKind.None) return;
            B("objective_completed", e.Time, "obj|" + e.Actor).Slot("player", e.Actor).Enum("kind", kind).Bool("completed", e.B == 1).Emit();
        }

        // ---- results -----------------------------------------------------------------------------

        void EmitResults(double t)
        {
            if (_resultsEmitted) return;
            var r = _e.Result;
            if (r == null) { var v = PublicView(t); r = v != null ? v.Result : null; }
            if (r == null) return;
            _resultsEmitted = true;
            foreach (var p in r.Players)
            {
                if (!Valid(p.Player)) continue;
                if (p.Objective != ObjectiveKind.None)
                    B("objective_completed", t, "obj|" + p.Player).Slot("player", p.Player).Enum("kind", p.Objective).Bool("completed", p.ObjectiveDone).Emit();
                if (r.CompanyClosed) continue; // closed company: no career lines, no rank (never zero-filled)
                int notFired = 0, target = 0, objective = 0, eotm = 0, sabotage = 0;
                foreach (var c in p.Career)
                {
                    switch (c.Item)
                    {
                        case CareerItem.NotFired: notFired += c.Points; break;
                        case CareerItem.CompanyTarget: target += c.Points; break;
                        case CareerItem.SecretObjective: objective += c.Points; break;
                        case CareerItem.EmployeeOfMonth: eotm += c.Points; break;
                        case CareerItem.ProvenSabotage: sabotage += Math.Abs(c.Points); break;
                    }
                }
                var b = B("career_breakdown", t, "career|" + p.Player).Slot("player", p.Player).Int("not_fired", notFired).Int("company_target", target)
                    .Int("objective", objective).Int("eotm", eotm).Int("sabotage_penalty", sabotage).Int("total", p.CareerTotal).Int("rank", p.Rank).Int("money_end", p.Money);
                if (p.ContributionQuality >= 0) b.Int("quality", p.ContributionQuality);
                b.Emit();
            }
            B("company_result", t, "company").Int("final_permille", r.FinalProductivityPermille).Int("target_percent", r.TargetPercent).Bool("closed", r.CompanyClosed)
                .Int("exposed_incidents", r.Exposed.Count).Int("incidents", r.IncidentsTotal).Emit();
        }

        // ---- final defense (polled) --------------------------------------------------------------

        void PollDefense(double now)
        {
            _nextDefensePoll = now + DefensePollSeconds;
            var v = PublicView(now);
            if (v == null || v.Meeting == null) return;
            int d = v.Meeting.Defender;
            if (d == _defender) return;
            EndDefender(now);
            _defender = d;
            if (Valid(d))
            {
                _defenderStart = now;
                _defenderIndex = v.Meeting.DefenseOrder.IndexOf(d);
                B("defense_started", now, "defs|" + d).Slot("defender", d).Int("order_index", Math.Max(0, _defenderIndex)).Emit();
            }
        }

        void EndDefender(double t)
        {
            if (!Valid(_defender)) return;
            B("defense_finished", t, "deff|" + _defender).Slot("defender", _defender).Int("order_index", Math.Max(0, _defenderIndex)).Float("duration_seconds", Math.Max(0, t - _defenderStart)).Emit();
            _defender = LineIds.None;
        }

        // ---- recordings (voice bridge) -----------------------------------------------------------

        void OnRecordingPick(LineEvent e) { _picks[e.A] = new double[] { e.B, e.C }; }

        /// <summary>The host voice system handled one pick (chosenCallId = -1 when nothing played).</summary>
        public void RecordingStarted(LineRecordingPick pick, int chosenCallId, TelemetryRecordingInfo info, double now)
        {
            if (!_started || _ended || pick == null) return;
            try
            {
                string failure = info.Failure;
                if (chosenCallId < 0 || info.ChosenRank < 0)
                {
                    string f = failure == "no_candidates" || failure == "no_speech" || failure == "disabled" || failure == "voice_unavailable" ? failure : (pick.CandidateCallIds.Count == 0 ? "no_candidates" : "no_speech");
                    B("recording_playback_finished", now, "recfin|" + pick.Index).Int("pick_index", pick.Index).Bool("played", false).Enum("failure", f).Emit();
                    return;
                }
                int caller = pick.Caller, callee = pick.Callee;
                int ci = _calls.IndexOf(chosenCallId);
                if ((!Valid(caller) || !Valid(callee)) && ci >= 0) { caller = _calls.Intervals[ci].A; callee = _calls.Intervals[ci].B; }
                int prior;
                _exposures.TryGetValue(chosenCallId, out prior);
                _exposures[chosenCallId] = prior + 1;
                _playingByCall[chosenCallId] = pick.Index;
                bool same = false;
                int[] masks;
                if (Valid(caller) && Valid(callee) && _groupsByKind.TryGetValue((int)pick.Context, out masks)) same = (masks[caller] & (1 << callee)) != 0;
                double[] meta;
                int selection = _picks.TryGetValue(pick.Index, out meta) ? (int)meta[1] : 0;
                int candidates = info.Candidates > 0 ? info.Candidates : pick.CandidateCallIds.Count;
                var b = B("recording_playback_started", now, "recstart|" + pick.Index).Int("pick_index", pick.Index).Int("candidates", candidates).Int("chosen_rank", info.ChosenRank)
                    .Int("speech_ms", info.SpeechMs).Int("snippet_ms", info.SnippetMs).Int("call_length_ms", info.CallLengthMs)
                    .Slot("caller", caller).Slot("callee", callee).Bool("same_group", same).Int("prior_exposures", prior)
                    .Enum("selection", selection == (int)RecordingSelection.Context ? "context" : "legacy");
                if (pick.Context != TaskKind.None) b.Enum("context_task", LineTasks.Id(pick.Context));
                b.Emit();
            }
            catch (Exception ex) { Fail(ex); }
        }

        public void RecordingFinished(int callId, double actualSeconds, bool stoppedEarly, double now)
        {
            if (!_started || _ended) return;
            try
            {
                int pickIndex;
                if (!_playingByCall.TryGetValue(callId, out pickIndex)) return;
                _playingByCall.Remove(callId);
                B("recording_playback_finished", now, "recfin|" + pickIndex).Int("pick_index", pickIndex).Bool("played", true).Float("actual_duration_seconds", Math.Max(0, actualSeconds))
                    .Enum("failure", stoppedEarly ? "stopped" : "none").Emit();
            }
            catch (Exception ex) { Fail(ex); }
        }
    }
}
