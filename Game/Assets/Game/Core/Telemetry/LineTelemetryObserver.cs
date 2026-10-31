using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;

namespace BirimizKovulacak.Telemetry
{
    /// <summary>What the host voice system reports about one played (or failed) recording pick.</summary>
    public struct TelemetryRecordingInfo
    {
        public int Candidates;
        /// <summary>0-based rank of the played candidate in the engine's list, -1 when none played.</summary>
        public int ChosenRank;
        public int SpeechMs, SnippetMs, CallLengthMs;
        /// <summary>none, no_candidates, no_speech, disabled, voice_unavailable.</summary>
        public string Failure;
    }

    /// <summary>
    /// Host-side translator from engine output (drained <see cref="LineEvent"/> batches of every audience, plus
    /// read-only <see cref="ILineEngine"/> views) to analytics events. It never calls Apply/Tick/DrainEvents, never
    /// infers intent (no lie detection), reads durations and rules from <see cref="ILineEngine.Config"/>, and never
    /// throws into the host: every public entry point is failure-isolated.
    /// </summary>
    public sealed partial class LineTelemetryObserver
    {
        const double StuckThresholdSeconds = 5.0;
        const double DefensePollSeconds = 0.5;
        const int Seats = LineProtocol.MaxPlayers;

        sealed class PlayerRound
        {
            public bool Eligible;
            public bool Submitted;
            public double FirstSubmitAt = -1;
            public int Revisions, LastCategory = -1;
            public int Texts, Emails, Attempts, BusyFails, Unanswered;
            public int GroupMask;
            public double VoiceContactAt = -1, TextContactAt = -1;
            public double Blocked;
            public readonly List<double> Instants = new List<double>();
            public readonly List<double[]> Offline = new List<double[]>();
            public void Reset()
            {
                Eligible = Submitted = false;
                FirstSubmitAt = VoiceContactAt = TextContactAt = -1;
                Revisions = 0; LastCategory = -1;
                Texts = Emails = Attempts = BusyFails = Unanswered = 0;
                GroupMask = 0; Blocked = 0;
                Instants.Clear(); Offline.Clear();
            }
        }

        readonly IGameTelemetry _t;
        readonly ILineEngine _e;
        readonly LineConfig _cfg;
        readonly List<int> _ids = new List<int>();
        readonly bool[] _isBot = new bool[Seats];
        readonly bool[] _connected = new bool[Seats];
        readonly double[] _disconnectedAt = new double[Seats];
        readonly PlayerRound[] _pr = new PlayerRound[Seats];
        readonly CallIntervalTracker _calls = new CallIntervalTracker();
        readonly IEnumerable<string> _experiments;

        bool _started, _ended;
        double _matchStart;
        LinePhase _phase = LinePhase.None;
        double _phaseEnter, _phasePlanned = -1;
        int _phaseSeq;
        bool _stuckReported;
        bool _meetingIsFinal;
        double _meetingStart, _discussionStart;
        int _discussionChat, _discussionPresentations;
        bool _abandonReported, _resultsEmitted;

        // round
        bool _inRound;
        double _roundStart, _roundPlannedEnd;
        int _roundNumber = -1;
        TaskKind _roundKind;
        readonly Dictionary<int, int[]> _groupsByKind = new Dictionary<int, int[]>();
        readonly Dictionary<int, double[]> _early = new Dictionary<int, double[]>();

        // day / economy
        int _dayOutput, _dayMax;
        readonly int[] _blockIncome = new int[Seats];
        readonly int[] _blame = new int[Seats];
        readonly int[] _warnDelta = new int[Seats];
        readonly int[] _remedialBlame = new int[Seats];
        readonly int[] _purchases = new int[Seats];
        readonly Dictionary<int, double> _incidentTime = new Dictionary<int, double>();
        int _lastPresenter = -1;
        double _lastPresentTime = -100;

        public bool IsStarted { get { return _started; } }
        public bool IsEnded { get { return _ended; } }
        public int FailureCount { get; private set; }

        public LineTelemetryObserver(IGameTelemetry telemetry, ILineEngine engine, IEnumerable<string> experiments = null)
        {
            if (telemetry == null) throw new ArgumentNullException("telemetry");
            if (engine == null) throw new ArgumentNullException("engine");
            _t = telemetry;
            _e = engine;
            _cfg = engine.Config ?? LineConfig.CreateDefault();
            _experiments = experiments;
            for (int i = 0; i < Seats; i++) _pr[i] = new PlayerRound();
        }

        // ---- plumbing ---------------------------------------------------------------------------

        TelemetryEventBuilder B(string name, double t, string key)
        {
            return _t.Begin(name).At(t).Key(key);
        }

        static string Ms(double t) { return ((long)Math.Round(t * 1000)).ToString(System.Globalization.CultureInfo.InvariantCulture); }

        void Fail(Exception ex)
        {
            FailureCount++;
            try { _t.Begin("technical_failure").Enum("category", "engine_exception").Id("type", ex.GetType().Name).Emit(); }
            catch (Exception) { }
        }

        LineView PublicView(double now)
        {
            if (_ids.Count == 0) return null;
            try { return _e.BuildView(_ids[0], now); }
            catch (Exception ex) { Fail(ex); return null; }
        }

        LineView ViewOf(int player, double now)
        {
            if (!_ids.Contains(player)) return null;
            try { return _e.BuildView(player, now); }
            catch (Exception ex) { Fail(ex); return null; }
        }

        static LinePlayerView PlayerIn(LineView v, int id)
        {
            if (v == null) return null;
            for (int i = 0; i < v.Players.Count; i++) if (v.Players[i].Id == id) return v.Players[i];
            return null;
        }

        static bool Valid(int p) { return p >= 0 && p < Seats; }

        bool IsGroupPartner(int a, int b) { return Valid(a) && Valid(b) && (_pr[a].GroupMask & (1 << b)) != 0; }

        // ---- lifecycle --------------------------------------------------------------------------

        /// <summary>Call once after the engine was created and before its first events are drained.</summary>
        public void Start(double now)
        {
            if (_started) return;
            try
            {
                _started = true;
                _matchStart = now;
                var setup = _e.Setup;
                bool bots = false;
                foreach (var p in setup.Players)
                {
                    if (!Valid(p.Id)) continue;
                    _ids.Add(p.Id);
                    _isBot[p.Id] = p.IsBot;
                    _connected[p.Id] = true;
                    bots |= p.IsBot;
                }
                _t.Context.BeginMatch(setup.MatchId, setup.WeekId, _ids.Count, bots, _experiments);
                var v = PublicView(now);
                if (v != null)
                {
                    foreach (var pv in v.Players) if (Valid(pv.Id)) { _blame[pv.Id] = pv.Blame; _connected[pv.Id] = pv.Connected; }
                }
                string key = "start";
                B("week_started", now, key).Int("days", v != null ? v.DayCount : 0).Int("tasks", v != null ? v.Company.TasksScheduled : 0).Emit();
                foreach (int id in _ids)
                {
                    var pv = ViewOf(id, now);
                    if (pv == null || pv.Mine == null || pv.Mine.Objective == null || pv.Mine.Objective.Kind == ObjectiveKind.None) continue;
                    B("objective_assigned", now, "objassign|" + id).Slot("player", id).Enum("kind", pv.Mine.Objective.Kind).Emit();
                }
            }
            catch (Exception ex) { Fail(ex); }
        }

        /// <summary>Feed every drained batch (all audiences, including Host events) in order.</summary>
        public void OnEvents(List<LineEvent> events, double now)
        {
            if (!_started || _ended || events == null || !_t.IsEnabled) return;
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                if (e == null) continue;
                try { Dispatch(e, now); }
                catch (Exception ex) { Fail(ex); }
            }
        }

        void Dispatch(LineEvent e, double now)
        {
            switch (e.Type)
            {
                case LineEventType.PhaseChanged: OnPhaseChanged(e); break;
                case LineEventType.CallIncoming: OnCallIncoming(e); break;
                case LineEventType.CallConnected: OnCallConnected(e); break;
                case LineEventType.CallEnded: OnCallEnded(e); break;
                case LineEventType.CallBusy: OnCallBusy(e.AudiencePlayer, e.Target, e.Time); break;
                case LineEventType.MissedCall: OnMissedCall(e); break;
                case LineEventType.TaskSubmitted: OnTaskSubmitted(e); break;
                case LineEventType.TrickHit: OnTrickHit(e); break;
                case LineEventType.InternetDown: OnInternetDown(e); break;
                case LineEventType.VoteCast: OnVoteCast(e); break;
                case LineEventType.WarningGiven: OnWarning(e); break;
                case LineEventType.Fired: OnFired(e); break;
                case LineEventType.BlameChanged: OnBlameChanged(e); break;
                case LineEventType.TaskResolved: OnTaskResolved(e); break;
                case LineEventType.TransferMade: OnTransfer(e); break;
                case LineEventType.PurchaseMade: OnPurchase(e); break;
                case LineEventType.EvidencePresented: OnEvidence(e); break;
                case LineEventType.IncidentExposed: OnIncidentExposed(e); break;
                case LineEventType.RemedialRequested: OnRemedialRequested(e); break;
                case LineEventType.RemedialResolved: OnRemedialResolved(e); break;
                case LineEventType.ProductivityChanged: OnProductivity(e); break;
                case LineEventType.CompanyClosed: B("company_failed", e.Time, "closed").Int("final_permille", e.A).Emit(); break;
                case LineEventType.Presence: SetConnection(e.Target, e.A == 1, e.Time); break;
                case LineEventType.HostChoice: OnHostChoice(e); break;
                case LineEventType.HostIncome: OnHostIncome(e); break;
                case LineEventType.HostIncident: OnHostIncident(e); break;
                case LineEventType.HostEarlySubmit: _early[e.C] = new[] { e.A / 1000.0, e.B / 1000.0 }; break;
                case LineEventType.HostObjective: OnHostObjective(e); break;
                case LineEventType.HostBallot: OnBallot(e); break;
                case LineEventType.HostRecordingPick: OnRecordingPick(e); break;
                case LineEventType.HostMessage: OnHostMessage(e); break;
                case LineEventType.HostMissedTask: OnHostMissedTask(e); break;
            }
        }

        /// <summary>Once per host frame: stuck-phase watchdog and final-defense turn tracking.</summary>
        public void Tick(double now)
        {
            if (!_started || _ended || !_t.IsEnabled) return;
            try
            {
                if (!_stuckReported && _phase != LinePhase.None && _phase != LinePhase.WeekResults && _phase != LinePhase.Abandoned && !_e.IsFinished)
                {
                    double end = _e.PhaseEndTime;
                    if (end > 0 && now > end + StuckThresholdSeconds && _e.Phase == _phase)
                    {
                        _stuckReported = true;
                        B("stuck_phase", now, "stuck|" + _phaseSeq).Enum("phase", TelemetryNames.Of(_phase)).Float("seconds_over", now - end).Emit();
                    }
                }
                if (_phase == LinePhase.FinalDefense && now >= _nextDefensePoll) PollDefense(now);
            }
            catch (Exception ex) { Fail(ex); }
        }

        /// <summary>Presence from the host side (also derived from Presence events; repeated reports of the same state are ignored).</summary>
        public void PlayerConnection(int slot, bool connected, double now)
        {
            if (!_started || _ended) return;
            try { SetConnection(slot, connected, now); }
            catch (Exception ex) { Fail(ex); }
        }

        void SetConnection(int slot, bool connected, double t)
        {
            if (!Valid(slot) || !_ids.Contains(slot) || _connected[slot] == connected) return;
            _connected[slot] = connected;
            string ph = TelemetryNames.Of(_phase == LinePhase.None ? LinePhase.Morning : _phase);
            if (!connected)
            {
                _disconnectedAt[slot] = t;
                B("player_disconnected", t, "dc|" + slot + "|" + Ms(t)).Slot("player", slot).Enum("phase", ph).Emit();
                var closed = new List<CallInterval>();
                _calls.CensorPlayer(slot, t, closed);
                foreach (var c in closed) EmitCallFinished(c, CallEndReason.Disconnected, t);
                if (_inRound) _pr[slot].Offline.Add(new[] { t, double.PositiveInfinity });
            }
            else
            {
                double down = _disconnectedAt[slot] > 0 ? t - _disconnectedAt[slot] : -1;
                var b = B("player_reconnected", t, "rc|" + slot + "|" + Ms(t)).Slot("player", slot).Enum("phase", ph);
                if (down >= 0) b.Float("downtime_seconds", down);
                b.Emit();
                var off = _pr[slot].Offline;
                if (off.Count > 0 && double.IsPositiveInfinity(off[off.Count - 1][1])) off[off.Count - 1][1] = t;
            }
        }

        /// <summary>The match is over. reason: results, abandoned, host_closed. Releases the private buffer.</summary>
        public void MatchEnded(string reason, double now)
        {
            if (!_started || _ended) return;
            try
            {
                string r = reason == "abandoned" || reason == "host_closed" ? reason : "results";
                var closed = new List<CallInterval>();
                _calls.CloseAll(now, true, closed);
                foreach (var c in closed) EmitCallFinished(c, CallEndReason.PhaseEnd, now);
                if (_phase != LinePhase.None && _phase != LinePhase.Abandoned)
                {
                    if (_phase == LinePhase.RoundPlay && _inRound) SummarizeRound(now);
                    ExitPhase(now, r == "results" ? (_e.IsFinished ? "host_continue" : "timer") : "abandoned");
                }
                if (r == "abandoned" && !_abandonReported) ReportAbandon(now);
                if (!_resultsEmitted && r == "results") EmitResults(now);
                B("week_finished", now, "wf").Enum("completion_reason", r).Float("duration_seconds", Math.Max(0, now - _matchStart)).Emit();
            }
            catch (Exception ex) { Fail(ex); }
            finally
            {
                _ended = true;
                try { _t.ReleasePrivate(); } catch (Exception) { }
            }
        }

        public void TechnicalFailure(string category, Exception e)
        {
            try
            {
                var b = _t.Begin("technical_failure");
                string cat = category == "engine_exception" || category == "rpc_decode" || category == "voice_exception" ? category : "engine_exception";
                b.Enum("category", cat).Id("type", e != null ? e.GetType().Name : "Exception").Emit();
            }
            catch (Exception) { }
        }

        // ---- phases ------------------------------------------------------------------------------

        static readonly Dictionary<LinePhase, LinePhase[]> Flow = new Dictionary<LinePhase, LinePhase[]>
        {
            { LinePhase.None, new[] { LinePhase.Morning } },
            { LinePhase.Morning, new[] { LinePhase.RoundIntro } },
            { LinePhase.RoundIntro, new[] { LinePhase.RoundPlay } },
            { LinePhase.RoundPlay, new[] { LinePhase.RoundReveal } },
            { LinePhase.RoundReveal, new[] { LinePhase.Break } },
            { LinePhase.Break, new[] { LinePhase.RoundIntro, LinePhase.MeetingDossier } },
            { LinePhase.MeetingDossier, new[] { LinePhase.MeetingDiscussion } },
            { LinePhase.MeetingDiscussion, new[] { LinePhase.MeetingVote, LinePhase.FinalDefense, LinePhase.FinalVerdict } },
            { LinePhase.MeetingVote, new[] { LinePhase.MeetingVerdict } },
            { LinePhase.MeetingVerdict, new[] { LinePhase.Morning } },
            { LinePhase.FinalDefense, new[] { LinePhase.FinalVote } },
            { LinePhase.FinalVote, new[] { LinePhase.FinalVerdict } },
            { LinePhase.FinalVerdict, new[] { LinePhase.WeekResults } },
        };

        static bool LegalTransition(LinePhase from, LinePhase to)
        {
            if (to == LinePhase.Abandoned) return true;
            LinePhase[] next;
            if (!Flow.TryGetValue(from, out next)) return false;
            return Array.IndexOf(next, to) >= 0;
        }

        static string CompletionReason(PhaseEndReason r)
        {
            switch (r)
            {
                case PhaseEndReason.TimerShortened: return "timer_shortened";
                case PhaseEndReason.HostContinue: return "host_continue";
                case PhaseEndReason.Abandoned: return "abandoned";
                case PhaseEndReason.Skipped: return "skipped";
                default: return "timer";
            }
        }

        void OnPhaseChanged(LineEvent e)
        {
            var next = (LinePhase)e.A;
            var reason = (PhaseEndReason)e.B;
            double t = e.Time;
            if (_phase != LinePhase.None && next == _phase) return; // repeated report of the same phase
            if (_phase != LinePhase.None || reason != PhaseEndReason.Start)
            {
                if (!LegalTransition(_phase, next))
                    B("unexpected_transition", t, "ut|" + _phaseSeq + "|" + (int)next).Enum("from", TelemetryNames.Of(_phase)).Enum("to", TelemetryNames.Of(next)).Emit();
            }
            if (_phase != LinePhase.None)
            {
                if (_phase == LinePhase.RoundPlay && _inRound) SummarizeRound(t);
                ExitPhase(t, CompletionReason(reason));
            }
            EnterPhase(next, t, reason);
        }

        void ExitPhase(double t, string completion)
        {
            var leaving = _phase;
            double dur = Math.Max(0, t - _phaseEnter);
            var b = B("phase_exited", t, "px|" + _phaseSeq).Float("duration_seconds", dur).Enum("completion_reason", completion);
            if (_phasePlanned >= 0)
            {
                b.Float("planned_duration_seconds", _phasePlanned);
                if (dur > _phasePlanned) b.Float("overrun_seconds", dur - _phasePlanned);
            }
            b.Emit();
            switch (leaving)
            {
                case LinePhase.MeetingDiscussion:
                    B("meeting_discussion_finished", t, "mdf|" + _phaseSeq).Float("duration_seconds", dur).Int("chat_messages", _discussionChat).Int("presentations", _discussionPresentations).Emit();
                    break;
                case LinePhase.MeetingVerdict:
                case LinePhase.FinalVerdict:
                    B("meeting_finished", t, "mf|" + _phaseSeq).Float("duration_seconds", Math.Max(0, t - _meetingStart)).Emit();
                    break;
                case LinePhase.FinalDefense:
                    EndDefender(t);
                    break;
            }
            CloseRoundWindow(leaving);
        }

        void CloseRoundWindow(LinePhase leaving)
        {
            if (leaving == LinePhase.RoundPlay) _inRound = false;
        }

        void EnterPhase(LinePhase next, double t, PhaseEndReason reason)
        {
            _phaseSeq++;
            _phase = next;
            _phaseEnter = t;
            _stuckReported = false;
            double end = _e.PhaseEndTime;
            _phasePlanned = _e.Phase == next && end > t ? end - t : -1;
            var v = PublicView(t);
            UpdateContext(next, v);
            var b = B("phase_entered", t, "pe|" + _phaseSeq);
            if (_phasePlanned >= 0) b.Float("planned_duration_seconds", _phasePlanned);
            b.Emit();
            switch (next)
            {
                case LinePhase.Morning:
                    for (int i = 0; i < Seats; i++) _blockIncome[i] = 0;
                    if (v != null && v.TodayTasks.Count > 0)
                    {
                        string a = LineTasks.Id(v.TodayTasks[0]);
                        string c = LineTasks.Id(v.TodayTasks.Count > 1 ? v.TodayTasks[1] : v.TodayTasks[0]);
                        B("day_started", t, "ds|" + v.Day).Enum("task_a", a).Enum("task_b", c).Emit();
                    }
                    break;
                case LinePhase.RoundPlay: BeginRound(t, v); break;
                case LinePhase.Break: EmitRecovery(t); break;
                case LinePhase.MeetingDossier: BeginMeeting(t, v); break;
                case LinePhase.MeetingDiscussion:
                    _discussionStart = t; _discussionChat = 0; _discussionPresentations = 0;
                    B("meeting_discussion_started", t, "mds|" + _phaseSeq).Emit();
                    if (reason == PhaseEndReason.Skipped) { }
                    break;
                case LinePhase.MeetingVerdict: FlushVotes(t, false); break;
                case LinePhase.FinalDefense:
                    B("final_started", t, "fs").Int("roster", RosterSize(v)).Bool("company_closed", false).Emit();
                    _defender = LineIds.None; _nextDefensePoll = 0;
                    break;
                case LinePhase.FinalVote: _voteQueue.Clear(); break;
                case LinePhase.FinalVerdict:
                    if (reason == PhaseEndReason.Skipped)
                        B("final_started", t, "fs").Int("roster", RosterSize(v)).Bool("company_closed", true).Emit();
                    FlushVotes(t, true);
                    break;
                case LinePhase.WeekResults:
                    {
                        var result = _e.Result ?? (v != null ? v.Result : null);
                        bool closed = result != null ? result.CompanyClosed : (v != null && v.Company.Closed);
                        int fired = result != null ? result.Fired : LineIds.None;
                        B("week_completed", t, "wc").Float("duration_seconds", Math.Max(0, t - _matchStart)).Bool("company_closed", closed).Bool("fired", fired >= 0).Emit();
                        EmitResults(t);
                        try { _t.ReleasePrivate(); } catch (Exception) { }
                        break;
                    }
                case LinePhase.Abandoned: ReportAbandon(t); break;
            }
        }

        int RosterSize(LineView v)
        {
            if (v != null && v.Meeting != null)
            {
                if (v.Meeting.Roster.Count > 0) return v.Meeting.Roster.Count;
                if (v.Meeting.DefenseOrder.Count > 0) return v.Meeting.DefenseOrder.Count;
            }
            int n = 0;
            foreach (int id in _ids) if (_connected[id]) n++;
            return n;
        }

        void ReportAbandon(double t)
        {
            if (_abandonReported) return;
            _abandonReported = true;
            int connected = 0;
            foreach (int id in _ids) if (_connected[id]) connected++;
            // the phase the match was in when it was abandoned = the one before Abandoned
            B("match_abandoned", t, "ab").Enum("phase", TelemetryNames.Of(_lastNonTerminalPhase())).Int("connected", connected).Emit();
        }

        LinePhase _prevPhase = LinePhase.None;
        LinePhase _lastNonTerminalPhase() { return _prevPhase == LinePhase.None || _prevPhase == LinePhase.Abandoned ? LinePhase.Morning : _prevPhase; }

        void UpdateContext(LinePhase next, LineView v)
        {
            _prevPhase = _phaseBefore;
            _phaseBefore = next;
            TaskKind k = TaskKind.None;
            int ver = -1, number = -1, round = -1, day = _e.Day;
            if (v != null)
            {
                day = v.Day; round = v.RoundIndex; number = v.TaskNumber;
                if (v.Task != null) k = v.Task.Kind;
                else if (round >= 0 && round < v.TodayTasks.Count) k = v.TodayTasks[round];
            }
            if (k != TaskKind.None)
                foreach (var tc in _cfg.Tasks) if (tc.Kind == k) { ver = tc.Version; break; }
            _t.Context.SetPosition(TelemetryNames.Of(next), day, round, number, k == TaskKind.None ? "" : LineTasks.Id(k), ver);
        }

        LinePhase _phaseBefore = LinePhase.None;
    }
}
