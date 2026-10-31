using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;

namespace BirimizKovulacak.Telemetry
{
    public sealed partial class LineTelemetryObserver
    {
        readonly Dictionary<int, double> _pending = new Dictionary<int, double>();
        readonly Dictionary<int, double> _busyAt = new Dictionary<int, double>();

        // Measurement is gated at the time activity occurs: a player whose consent flipped during the window is only
        // partially observed, so nothing accumulated before the grant may be reported (summary omitted, excluded from aggregates).
        readonly int[] _verAtRound = new int[Seats];
        readonly HashSet<int> _unobservedCalls = new HashSet<int>();
        bool Stable(int p) { return Valid(p) && _t.ConsentVersion(p) == _verAtRound[p]; }

        static int PairKey(int caller, int callee) { return caller * 8 + callee; }

        // ---- round window ------------------------------------------------------------------------

        void BeginRound(double t, LineView v)
        {
            _inRound = true;
            _roundStart = t;
            _roundPlannedEnd = _e.PhaseEndTime > t ? _e.PhaseEndTime : t;
            _roundNumber = v != null ? v.TaskNumber : -1;
            _roundKind = v != null && v.Task != null ? v.Task.Kind : TaskKind.None;
            var masks = new int[Seats];
            foreach (int id in _ids)
            {
                var pr = _pr[id];
                pr.Reset();
                _verAtRound[id] = _t.ConsentVersion(id);
                var pv = ViewOf(id, t);
                var me = PlayerIn(v, id);
                pr.Eligible = !_isBot[id] && (me != null ? me.Connected : _connected[id]);
                if (pv != null && pv.Task != null)
                {
                    foreach (int g in pv.Task.Group) if (Valid(g)) pr.GroupMask |= 1 << g;
                    masks[id] = pr.GroupMask;
                    B("secret_info_presented", t, "info|" + _roundNumber + "|" + id).Slot("player", id).Int("role", pv.Task.Role).Int("facts", pv.Task.Facts.Count).Emit();
                }
                if (!_connected[id]) pr.Offline.Add(new[] { t, double.PositiveInfinity });
            }
            if (_roundKind != TaskKind.None) _groupsByKind[(int)_roundKind] = masks;
            if (v != null && v.Task != null)
                B("round_started", t, "rs|" + _roundNumber).Enum("family", TelemetryNames.Of(v.Task.Family)).Float("play_seconds", _roundPlannedEnd - t).Bool("first_time", v.Features != null && v.Features.FirstTimeTask).Emit();
        }

        // ---- phone -------------------------------------------------------------------------------

        void OnCallIncoming(LineEvent e)
        {
            int caller = e.Actor, callee = e.AudiencePlayer;
            if (!Valid(caller) || !Valid(callee)) return;
            int key = PairKey(caller, callee);
            if (_pending.ContainsKey(key)) return; // repeated report
            _pending[key] = e.Time;
            EmitRequested(caller, callee, e.Time);
        }

        void EmitRequested(int caller, int callee, double t)
        {
            if (_inRound) _pr[caller].Attempts++;
            B("phone_call_requested", t, "req|" + caller + "|" + callee + "|" + Ms(t)).Slot("caller", caller).Slot("callee", callee).Bool("partners_in_task", IsGroupPartner(caller, callee)).Emit();
        }

        void OnCallConnected(LineEvent e)
        {
            int caller = e.Actor, callee = e.Target, id = e.A;
            if (!Valid(caller) || !Valid(callee)) return;
            if (!_calls.Open(id, caller, callee, e.Time)) return;
            if (_t.ConsentOfSlot(caller) < 2 || _t.ConsentOfSlot(callee) < 2) _unobservedCalls.Add(id); // started while someone was not consenting
            double ring = -1;
            int key = PairKey(caller, callee);
            double reqAt;
            if (_pending.TryGetValue(key, out reqAt)) { ring = e.Time - reqAt; _pending.Remove(key); }
            var b = B("phone_call_connected", e.Time, "conn|" + id).Int("call_id", id).Slot("caller", caller).Slot("callee", callee);
            if (ring >= 0) b.Float("ring_seconds", ring);
            b.Bool("partners_in_task", IsGroupPartner(caller, callee)).Emit();
            if (_inRound && IsGroupPartner(caller, callee))
            {
                if (_pr[caller].VoiceContactAt < 0) _pr[caller].VoiceContactAt = e.Time;
                if (_pr[callee].VoiceContactAt < 0) _pr[callee].VoiceContactAt = e.Time;
            }
        }

        void OnCallEnded(LineEvent e)
        {
            int self = e.AudiencePlayer, other = e.Actor, callId = e.B;
            var reason = (CallEndReason)e.A;
            if (callId >= 0 && _calls.IndexOf(callId) >= 0)
            {
                CallInterval closed;
                if (_calls.Close(callId, e.Time, reason == CallEndReason.Disconnected, out closed)) EmitCallFinished(closed, reason, e.Time);
                return; // second report of the pair, or already finished by a censoring
            }
            if (!Valid(self) || !Valid(other)) return;
            int k1 = PairKey(self, other), k2 = PairKey(other, self);
            int caller, callee;
            double reqAt;
            if (_pending.TryGetValue(k1, out reqAt)) { caller = self; callee = other; _pending.Remove(k1); }
            else if (_pending.TryGetValue(k2, out reqAt)) { caller = other; callee = self; _pending.Remove(k2); }
            else return;
            double ring = Math.Max(0, e.Time - reqAt);
            string key = "end|" + caller + "|" + callee + "|" + Ms(reqAt);
            switch (reason)
            {
                case CallEndReason.Declined:
                    if (_inRound) _pr[caller].Unanswered++;
                    B("phone_call_rejected", e.Time, key).Slot("caller", caller).Slot("callee", callee).Float("ring_seconds", ring).Emit();
                    break;
                case CallEndReason.LineCut: EmitFailed(caller, callee, e.Time, key, "dead_line"); break;
                case CallEndReason.PhaseEnd: EmitFailed(caller, callee, e.Time, key, "wrong_phase"); break;
                case CallEndReason.Disconnected: EmitFailed(caller, callee, e.Time, key, "absent"); break;
                default: // Missed, HungUp while ringing, None
                    if (_inRound) _pr[caller].Unanswered++;
                    B("phone_call_missed", e.Time, key).Slot("caller", caller).Slot("callee", callee).Emit();
                    break;
            }
        }

        void EmitFailed(int caller, int callee, double t, string key, string reason)
        {
            B("phone_call_failed", t, key).Slot("caller", caller).Slot("callee", callee).Enum("reason", reason).Emit();
        }

        void OnCallBusy(int caller, int callee, double t)
        {
            if (!Valid(caller) || !Valid(callee)) return;
            int k = PairKey(caller, callee);
            double last;
            if (_busyAt.TryGetValue(k, out last) && Math.Abs(t - last) < 1.0) return;
            _busyAt[k] = t;
            EmitRequested(caller, callee, t);
            if (_inRound) _pr[caller].BusyFails++;
            var cv = ViewOf(callee, t);
            bool dead = cv != null && cv.Mine != null && cv.Mine.PhoneDeadUntil > t;
            EmitFailed(caller, callee, t, "busy|" + caller + "|" + callee + "|" + Ms(t), dead ? "dead_line" : "busy");
        }

        void OnMissedCall(LineEvent e)
        {
            // the busy / dead-line callee's notice of the same attempt the caller got as CallBusy
            OnCallBusy(e.Actor, e.AudiencePlayer, e.Time);
        }

        void EmitCallFinished(CallInterval c, CallEndReason reason, double t)
        {
            if (_unobservedCalls.Contains(c.CallId)) return; // its start was never observed: do not report its length later
            string endName = TelemetryNames.Of(reason);
            if (endName.Length == 0) endName = "none";
            B("phone_call_finished", t, "fin|" + c.CallId).Int("call_id", c.CallId).Slot("caller", c.A).Slot("callee", c.B)
                .Float("duration_seconds", Math.Max(0, c.End - c.Start)).Enum("end_reason", endName).Bool("censored", c.Censored).Emit();
        }

        // ---- messages / submissions --------------------------------------------------------------

        void OnHostMessage(LineEvent e)
        {
            int sender = e.Actor, recipient = e.Target;
            var kind = (MessageKind)e.A;
            string channel;
            switch (kind)
            {
                case MessageKind.Text: channel = "text"; break;
                case MessageKind.Email: channel = (e.C & 8) != 0 ? "fake_email" : "email"; break;
                case MessageKind.MeetingChat: channel = "meeting_chat"; break;
                case MessageKind.Complaint: channel = "complaint"; break;
                default: return;
            }
            string bucket = e.B <= 0 ? "short" : e.B == 1 ? "medium" : "long";
            var b = B("message_sent", e.Time, "msg|" + sender + "|" + Ms(e.Time) + "|" + e.A + "|" + recipient)
                .Slot("sender", sender).Enum("channel", channel).Enum("scope", recipient >= 0 ? "one" : "all")
                .Enum("length_bucket", bucket).Bool("is_reply", (e.C & 1) != 0).Bool("template", (e.C & 2) != 0).Bool("preset", (e.C & 4) != 0);
            if (recipient >= 0) b.Slot("recipient", recipient);
            b.Emit();
            if (kind == MessageKind.MeetingChat) { if (_phase == LinePhase.MeetingDiscussion) _discussionChat++; return; }
            if (!_inRound || !Valid(sender)) return;
            var ps = _pr[sender];
            if (kind == MessageKind.Text) ps.Texts++;
            else if (kind == MessageKind.Email) ps.Emails++;
            ps.Instants.Add(e.Time);
            if (Valid(recipient))
            {
                _pr[recipient].Instants.Add(e.Time);
                if (IsGroupPartner(sender, recipient))
                {
                    if (ps.TextContactAt < 0) ps.TextContactAt = e.Time;
                    if (_pr[recipient].TextContactAt < 0) _pr[recipient].TextContactAt = e.Time;
                }
            }
        }

        void OnTaskSubmitted(LineEvent e)
        {
            if (!_inRound || !Valid(e.Actor)) return;
            var pr = _pr[e.Actor];
            if (!pr.Submitted) { pr.Submitted = true; pr.FirstSubmitAt = e.Time; }
            pr.Instants.Add(e.Time);
        }

        static string FieldName(int mask)
        {
            int bits = 0;
            for (int m = mask & 7; m != 0; m &= m - 1) bits++;
            if (bits != 1) return "multiple";
            return (mask & (int)TaskField.Target) != 0 ? "target" : (mask & (int)TaskField.A) != 0 ? "a" : "b";
        }

        void OnHostChoice(LineEvent e)
        {
            int p = e.Actor;
            if (!Valid(p)) return;
            var pr = _pr[p];
            bool changed = e.C == 1;
            double latency = Math.Max(0, e.Time - _roundStart);
            bool voice = pr.VoiceContactAt >= 0 && pr.VoiceContactAt <= e.Time;
            bool text = pr.TextContactAt >= 0 && pr.TextContactAt <= e.Time;
            string channel = voice && text ? "both" : voice ? "voice" : text ? "text" : "none";
            bool sabotage = IsSabotageChoice(p, e.Time);
            string key = "choice|" + p + "|" + e.B + "|" + Ms(e.Time);
            var b = B(changed ? "choice_changed" : "choice_submitted", e.Time, key)
                .Slot("player", p).Enum("field", FieldName(e.Target)).Int("category", e.A).Float("latency_seconds", latency)
                .Bool("after_contact", voice || text).Enum("contact_channel", channel).Bool("sabotage_option", sabotage);
            if (changed && pr.LastCategory >= 0) b.Int("previous_category", pr.LastCategory);
            b.Emit();
            if (changed) pr.Revisions++;
            pr.LastCategory = e.A;
        }

        bool IsSabotageChoice(int player, double t)
        {
            var v = ViewOf(player, t);
            if (v == null || v.Task == null) return false;
            foreach (var c in v.Task.Controls)
            {
                if (c.Kind != ControlKind.Choice) continue;
                int mine = c.Field == TaskField.A ? v.Task.MyA : c.Field == TaskField.B ? v.Task.MyB : v.Task.MyTarget;
                foreach (var o in c.Options) if (o.Sabotage && o.Value == mine && (v.Task.SubmittedFields & c.Field) != 0) return true;
            }
            return false;
        }

        readonly System.Collections.Generic.Dictionary<int, int[]> _pendingTaskOut = new System.Collections.Generic.Dictionary<int, int[]>();
        readonly System.Collections.Generic.Dictionary<int, double> _pendingTaskOutTime = new System.Collections.Generic.Dictionary<int, double>();
        readonly System.Collections.Generic.List<int> _flushKeys = new System.Collections.Generic.List<int>();

        // HostTaskOutcome may arrive before or after TaskResolved: cache each half by task number and join in either order.
        readonly System.Collections.Generic.Dictionary<int, int[]> _taskDetail = new System.Collections.Generic.Dictionary<int, int[]>();

        void EmitTaskOutput(int number, double time, int output, int max, int[] detail)
        {
            var b = B("task_output_resolved", time, "taskres|" + number).Int("output", output).Int("max_output", max);
            if (detail != null)
            {
                int groups = detail[1] >= 0 ? detail[1] / 10 : -1, full = detail[1] >= 0 ? detail[1] % 10 : -1;
                if (detail[0] >= 0) b.Int("sabotage_loss", detail[0]);
                if (groups > 0) b.Int("groups", groups).Int("groups_full", full);
            }
            b.Emit();
        }

        void OnHostTaskOutcome(LineEvent e)
        {
            int number = e.A;
            int[] p;
            if (_pendingTaskOut.TryGetValue(number, out p))
            {
                EmitTaskOutput(number, _pendingTaskOutTime[number], p[0], p[1], new[] { e.B, e.C });
                _pendingTaskOut.Remove(number);
                _pendingTaskOutTime.Remove(number);
            }
            else _taskDetail[number] = new[] { e.B, e.C }; // detail first: TaskResolved joins it
        }

        void FlushPendingTaskOutputs()
        {
            if (_pendingTaskOut.Count == 0) return;
            _flushKeys.Clear();
            _flushKeys.AddRange(_pendingTaskOut.Keys);
            _flushKeys.Sort();
            foreach (int number in _flushKeys)
            {
                var p = _pendingTaskOut[number];
                EmitTaskOutput(number, _pendingTaskOutTime[number], p[0], p[1], null);
            }
            _pendingTaskOut.Clear();
            _pendingTaskOutTime.Clear();
        }

        /// <summary>Per-participant outcome of a resolved task (private until the match ends).</summary>
        void OnHostOutcome(LineEvent e)
        {
            if (!Valid(e.Actor)) return;
            B("choice_outcome_resolved", e.Time, "outcome|" + e.Actor + "|" + e.Target).Slot("player", e.Actor)
                .Int("quality", e.A).Int("income", Math.Max(0, e.B))
                .Bool("cooperation_credit", (e.C & 2) != 0).Bool("cooperation_opportunity", (e.C & 1) != 0).Emit();
        }

        void OnHostMissedTask(LineEvent e)
        {
            if (!Valid(e.Actor)) return;
            string reason = e.B == 2 ? "blocked" : e.B == 3 ? "absent" : "missed";
            B("choice_timed_out", e.Time, "missed|" + e.Actor + "|" + e.A).Slot("player", e.Actor).Enum("reason", reason).Emit();
        }

        void OnTrickHit(LineEvent e)
        {
            if (!_inRound || !Valid(e.AudiencePlayer)) return;
            if ((TrickType)e.A == TrickType.KahveDok)
                _pr[e.AudiencePlayer].Blocked += Math.Max(0, Math.Min(_cfg.CoffeeSeconds, _roundPlannedEnd - e.Time));
        }

        void OnInternetDown(LineEvent e)
        {
            if (!_inRound) return;
            double s = Math.Max(0, Math.Min(_cfg.InternetSeconds, _roundPlannedEnd - e.Time));
            foreach (int id in _ids) _pr[id].Blocked += s;
        }

        // ---- task resolution ---------------------------------------------------------------------

        void OnTaskResolved(LineEvent e)
        {
            int number = e.A, output = e.B;
            var v = PublicView(e.Time);
            int max = -1;
            if (v != null) foreach (var r in v.TodayReveals) if (r.Number == number) { max = r.MaxOutput; break; }
            if (max < 0)
            {
                foreach (var tc in _cfg.Tasks) if (tc.Kind == _roundKind) { max = tc.MaxOutput; break; }
            }
            if (max < 0) max = output;
            _dayOutput += output; _dayMax += max;
            // Written together with HostTaskOutcome (same batch) so sabotage loss and group counts can be attached.
            int[] early0;
            if (_taskDetail.TryGetValue(number, out early0)) { _taskDetail.Remove(number); EmitTaskOutput(number, e.Time, output, max, early0); }
            else { _pendingTaskOut[number] = new[] { output, max, 0 }; _pendingTaskOutTime[number] = e.Time; }
            int submitted = 0, connected = 0;
            foreach (int id in _ids) { if (_pr[id].Submitted) submitted++; if (_connected[id]) connected++; }
            double[] early;
            bool hasEarly = _early.TryGetValue(number, out early);
            var b = B("round_finished", e.Time, "rf|" + number).Int("output", output).Int("max_output", max).Int("submitted", submitted).Int("connected", connected).Bool("early_submit", hasEarly);
            if (hasEarly) b.Float("saved_seconds", early[0]).Float("carried_seconds", early[1]);
            b.Emit();
        }

        void OnProductivity(LineEvent e)
        {
            if (e.A < 0) return; // still measuring: nothing to report (never zero)
            var v = PublicView(e.Time);
            bool remedial = v != null && v.Company != null && v.Company.LastChange == CompanyChange.Remedial;
            B("productivity_changed", e.Time, "prod|" + Ms(e.Time) + "|" + e.A).Int("provisional_permille", e.A).Int("delta_permille", e.B).Enum("change", remedial ? "remedial" : "task").Emit();
        }

        void EmitRecovery(double t)
        {
            int with = 0, players = 0;
            foreach (int id in _ids)
            {
                if (!_connected[id]) continue;
                players++;
                var v = ViewOf(id, t);
                if (v != null && v.Mine != null && v.Mine.RemedialOptions != null && v.Mine.RemedialOptions.Count > 0) with++;
            }
            if (players > 0) B("recovery_opportunities", t, "rec|" + _phaseSeq).Int("players_with_options", with).Int("players", players).Emit();
        }

        // ---- summaries ---------------------------------------------------------------------------

        void SummarizeRound(double end)
        {
            double start = _roundStart;
            if (end < start) end = start;
            int eligible = 0, observed = 0, participating = 0, attempts = 0;
            var pairs = new HashSet<int>();
            var connectedCalls = new HashSet<int>();
            Func<int, bool> isObserved = p => Valid(p) && _pr[p].Eligible && _t.ConsentOfSlot(p) >= 2 && Stable(p);
            foreach (int id in _ids)
            {
                var pr = _pr[id];
                if (!pr.Eligible) continue;
                eligible++;
                if (!isObserved(id)) continue;
                observed++;
                attempts += pr.Attempts;
                if (_calls.HasConnectedOverlap(id, start, end, end)) participating++;
            }
            foreach (var c in _calls.Intervals)
            {
                double ce = c.IsOpen ? end : c.End;
                if (c.Start > end || ce < start) continue;
                if (!isObserved(c.A) && !isObserved(c.B)) continue;
                connectedCalls.Add(c.CallId);
                pairs.Add(Math.Min(c.A, c.B) * 8 + Math.Max(c.A, c.B));
            }
            double union = _calls.UnionSeconds(isObserved, start, end, end);
            var sum = B("phone_round_summary", end, "phonesum|" + _roundNumber).Int("eligible", eligible).Int("observed", observed).Int("participating", participating)
                .Int("attempts", attempts).Int("connected", connectedCalls.Count).Int("distinct_pairs", pairs.Count).Float("connected_seconds_union", union);
            if (observed > 0) sum.Float("participation_rate", (double)participating / observed);
            sum.Emit();

            foreach (int id in _ids)
            {
                if (!Stable(id)) continue; // consent changed during the window: partially observed, no per-player summary
                var pr = _pr[id];
                int partners;
                int connected = _calls.CountOverlap(id, start, end, end, out partners);
                double connSec = _calls.UnionSeconds(id, start, end, end);
                var offline = new List<double[]>();
                foreach (var o in pr.Offline) offline.Add(new[] { o[0], double.IsPositiveInfinity(o[1]) ? end : o[1] });
                double gap = _calls.LongestGap(id, start, end, end, pr.Instants, offline);
                double firstCall = _calls.FirstStart(id, start, end);
                double firstContact = -1;
                if (firstCall >= 0) firstContact = firstCall;
                if (pr.TextContactAt >= 0 && (firstContact < 0 || pr.TextContactAt < firstContact)) firstContact = pr.TextContactAt;
                bool text = pr.Texts + pr.Emails > 0, voice = connected > 0;
                string mode = voice && text ? "mixed" : voice ? "voice" : text ? "text" : "none";
                var b = B("round_player_summary", end, "pps|" + _roundNumber + "|" + id).Slot("player", id)
                    .Float("connected_seconds", connSec).Int("calls_attempted", pr.Attempts).Int("calls_connected", connected).Int("unique_partners", partners)
                    .Int("texts_sent", pr.Texts).Int("emails_sent", pr.Emails).Bool("submitted", pr.Submitted).Int("revisions", pr.Revisions)
                    .Float("longest_gap_seconds", gap).Int("busy_failures", pr.BusyFails).Int("unanswered_attempts", pr.Unanswered)
                    .Enum("comm_mode", mode).Float("blocked_seconds", pr.Blocked);
                if (firstContact >= 0) b.Float("first_contact_latency_seconds", Math.Max(0, firstContact - start));
                if (pr.Submitted && pr.FirstSubmitAt >= 0) b.Float("post_submit_wait_seconds", Math.Max(0, end - pr.FirstSubmitAt));
                b.Emit();
            }
        }
    }
}
