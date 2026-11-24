using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    /// <summary>
    /// v5 rules engine core: seats, construction and validation, the absolute-deadline Tick loop, the phase machine,
    /// action dispatch, presence and the prim/suç ledgers. Rules per area live in the other partial files
    /// (Office: phone, messages, transfers, recorder; Tasks; Meeting; Final; Files: the stakes and the files; Views). Normative spec:
    /// docs/V5_ENGINE_SPEC.md.
    /// </summary>
    internal sealed partial class LineEngine : ILineEngine
    {
        private sealed class Seat
        {
            internal LinePlayerInfo Info;
            internal bool Connected = true;
            internal bool HasRequest;
            internal uint LastRequest;
            internal int Prim;
            internal int Blame;
            internal bool Fired;
            internal bool Candidate;
            internal bool InvestigationUsed;
            internal int RecorderUses;
            internal int Recorded;
            internal int Leaked;
            internal int Texts;
            internal int Chats;
            internal int Call = -1;
            internal double NextMessage;
            internal double NextTransfer;
            internal long MessageSeq;
            /// <summary>Current vote ballots (-1 empty); empty array when not voting.</summary>
            internal int[] Ballots = new int[0];
            internal LinePhoneView Phone = new LinePhoneView();
            internal readonly List<LineMessage> Messages = new List<LineMessage>();
            internal readonly List<LineCallRecordView> Log = new List<LineCallRecordView>();
            internal readonly List<LineTransferView> Transfers = new List<LineTransferView>();
        }

        private readonly LineSetup setup;
        private readonly LineConfig c;
        private readonly LineRandom rng;
        private readonly Seat[] seats = new Seat[LineProtocol.MaxPlayers];
        private readonly List<int> ids = new List<int>();
        private List<LineEvent> events = new List<LineEvent>();
        private double now;
        private double phaseStart;
        private double insufficientSince = -1;
        private PhaseEndReason endReason = PhaseEndReason.Start;
        private LineWeekResult result;

        // Full-disclosure ledgers (Results only).
        private readonly List<LinePrimChange> primLog = new List<LinePrimChange>();
        private readonly List<LineBlameChange> blameLog = new List<LineBlameChange>();
        private readonly List<LineBallotView> ballotLog = new List<LineBallotView>();
        private readonly List<LineTransferView> transferLog = new List<LineTransferView>();
        private readonly List<LineDossierEntry> incidents = new List<LineDossierEntry>();

        public LineConfig Config { get { return LineCodec.Clone(c); } }
        public LineSetup Setup { get { return LineCodec.Clone(setup); } }
        public LinePhase Phase { get; private set; }
        public double PhaseEndTime { get; private set; }
        public int Day { get; private set; }
        public long StateVersion { get; private set; }
        public bool IsFinished { get; private set; }
        public LineWeekResult Result { get { return result == null ? null : LineCodec.Clone(result); } }

        private int PlayerCount { get { return ids.Count; } }
        private bool RecordingsOn { get { return c.RecordingsEnabled && setup.RecordingsEnabled; } }
        private bool CallsOpen
        {
            get
            {
                return Phase == LinePhase.Intro || Phase == LinePhase.TaskPlay || Phase == LinePhase.TaskReveal ||
                       Phase == LinePhase.Break || Phase == LinePhase.CrisisCalls;
            }
        }
        private bool TransfersOpen { get { return Phase >= LinePhase.Intro && Phase <= LinePhase.FinalVote; } }
        private bool TextsOpen { get { return Phase >= LinePhase.Intro && Phase <= LinePhase.FinalVerdict; } }
        private bool ChatOpen
        {
            get
            {
                return Phase >= LinePhase.MeetingDossier && Phase <= LinePhase.MeetingVerdict ||
                       Phase >= LinePhase.CrisisVote && Phase <= LinePhase.FinalVerdict;
            }
        }
        private bool HasSeat(int id) { return id >= 0 && id < seats.Length && seats[id] != null; }
        private void Changed() { StateVersion++; }

        internal LineEngine(LineSetup input, LineConfig config)
        {
            if (input == null || input.Players == null || input.Players.Count < LineProtocol.MinPlayers ||
                input.Players.Count > LineProtocol.MaxPlayers || !Finite(input.StartTime) || input.StartTime < 0)
                throw new ArgumentException("Invalid match setup.");
            bool[] present = new bool[LineProtocol.MaxPlayers];
            foreach (var p in input.Players)
            {
                if (p == null || p.Id < 0 || p.Id >= LineProtocol.MaxPlayers || present[p.Id]) throw new ArgumentException("Invalid or duplicate seat.");
                present[p.Id] = true;
            }
            var h = input.History;
            if (h != null && (h.Wins == null || h.TimesFired == null || h.WeeksPlayed < 0)) throw new ArgumentException("Invalid history.");
            if (!LineTalkTime.Valid(input.TalkTime)) throw new ArgumentException("Invalid talk time.");
            try
            {
                setup = LineCodec.Clone(input);
                c = LineCodec.Clone(config ?? LineConfig.CreateDefault());
            }
            catch (FormatException ex) { throw new ArgumentException("Invalid setup/config schema.", ex); }
            ValidateConfig(input.Players.Count);
            // lobby option "Konuşma süresi": the talking phases of this match (Config returns the scaled durations)
            c.Durations.ScaleTalk(c.TalkFactor(setup.TalkTime));
            // "Haftanın gündemi": this match's levels and amounts (Config returns them applied)
            DrawStakes();
            rng = new LineRandom(setup.Seed);
            for (int id = 0; id < LineProtocol.MaxPlayers; id++)
            {
                if (!present[id]) continue;
                ids.Add(id);
                seats[id] = new Seat
                {
                    Info = setup.Players.Find(p => p.Id == id),
                    Prim = c.StartPrim,
                    RecorderUses = c.RecorderUses,
                };
            }
            ChooseTasks();
            now = setup.StartTime;
            Enter(LinePhase.Intro, now, c.Durations.Intro);
        }

        private static bool Finite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }

        private void ValidateConfig(int players)
        {
            if (c.ConfigVersion != LineConfig.CurrentVersion || c.Durations == null || c.CandidatesByPlayers == null || c.FiringsByPlayers == null ||
                c.CandidatesByPlayers.Count != 4 || c.FiringsByPlayers.Count != 4)
                throw new ArgumentException("Invalid config structure.");
            // Every numeric tunable, including ones added in future revisions: finite, 0..1e6.
            ValidateNumbers(c);
            ValidateNumbers(c.Durations);
            if (c.Legacy != null) ValidateNumbers(c.Legacy);
            if (c.StakesScheme != LineStakes.Fixed && c.StakesScheme != LineStakes.Permutation) throw new ArgumentException("Invalid stakes scheme.");
            var d = c.Durations;
            if (d.TaskPlay <= 0 || d.FinalDefense <= 0 || d.FinalVote <= 0 || d.FinalRevote <= 0 || d.Ring <= 0 ||
                c.AbandonSeconds <= 0 || c.PairingTries < 1 || c.PairingTries > 100000 || c.MessagesKeptPerPlayer < 1 ||
                c.TextMaxChars > 512 || c.MeetingChatMaxChars > 512 || c.PlaneNoteMaxChars > 512 || c.PatienceCapacity < 1 ||
                c.TalkTimeShort <= 0 || c.TalkTimeLong <= 0 || c.DeclareMax > 9999)
                throw new ArgumentException("Invalid config range.");
            for (int n = LineProtocol.MinPlayers; n <= LineProtocol.MaxPlayers; n++)
            {
                int candidates = c.CandidatesFor(n), firings = c.FiringsFor(n);
                if (candidates < 1 || candidates > n || firings < 1 || firings > candidates || firings >= n)
                    throw new ArgumentException("Invalid candidates/firings for " + n + " players.");
            }
        }

        private static void ValidateNumbers(object value)
        {
            foreach (var f in value.GetType().GetFields())
            {
                if (f.FieldType == typeof(int))
                {
                    int n = (int)f.GetValue(value);
                    if (n < 0 || n > 1000000) throw new ArgumentException("Invalid " + f.Name);
                }
                if (f.FieldType == typeof(float))
                {
                    float n = (float)f.GetValue(value);
                    if (!Finite(n) || n < 0 || n > 1000000) throw new ArgumentException("Invalid " + f.Name);
                }
            }
        }

        // ---- events and ledgers

        private void Emit(LineEventType type, double time, int actor = -1, int target = -1, int a = -1, int b = -1, int recipient = -1, int extra = -1)
        {
            events.Add(new LineEvent
            {
                Type = type, Time = time, Actor = actor, Target = target, A = a, B = b, C = extra,
                Audience = recipient < 0 ? LineAudience.All : LineAudience.Player, AudiencePlayer = recipient,
            });
        }

        /// <summary>Changes prim (never below 0) and logs it; returns the applied delta.</summary>
        private int AddPrim(int id, int delta, PrimReason reason, double time, int other = -1)
        {
            var s = seats[id];
            int applied = Math.Max(-s.Prim, delta);
            if (applied == 0) return 0;
            s.Prim += applied;
            primLog.Add(new LinePrimChange { Player = id, Delta = applied, Reason = reason, Other = other, Time = time });
            Changed();
            return applied;
        }

        private void AddBlame(int id, int delta, BlameReason reason, double time)
        {
            if (delta <= 0) return;
            seats[id].Blame += delta;
            blameLog.Add(new LineBlameChange { Player = id, Delta = delta, Reason = reason, Time = time });
            Emit(LineEventType.BlameAdded, time, target: id, a: delta, b: (int)reason);
            Changed();
        }

        /// <summary>Lowers suç by up to <paramref name="amount"/> (never below 0); logs and announces it as a negative change.</summary>
        private void RemoveBlame(int id, int amount, BlameReason reason, double time)
        {
            int applied = Math.Min(amount, seats[id].Blame);
            if (applied <= 0) return;
            seats[id].Blame -= applied;
            blameLog.Add(new LineBlameChange { Player = id, Delta = -applied, Reason = reason, Time = time });
            Emit(LineEventType.BlameAdded, time, target: id, a: -applied, b: (int)reason);
            Changed();
        }

        // ---- phase machine

        private void Enter(LinePhase phase, double time, double duration)
        {
            Phase = phase;
            phaseStart = time;
            PhaseEndTime = time + Math.Max(0, duration);
            PrankPhase();
            Changed();
            if (!CallsOpen) EndAllCalls(time, CallEndReason.PhaseEnd);
            Emit(LineEventType.PhaseChanged, time, a: (int)phase, b: (int)endReason);
            endReason = PhaseEndReason.Timer;
        }

        /// <summary>The meeting index changes: per-segment message allowances reset.</summary>
        private void SetDay(int day)
        {
            if (Day == day) return;
            Day = day;
            foreach (int id in ids) { seats[id].Texts = 0; seats[id].Chats = 0; }
        }

        public void Tick(double time)
        {
            if (!Finite(time) || time < now) throw new ArgumentOutOfRangeException(nameof(time), "Host seconds must be finite and monotonic.");
            while (!IsFinished)
            {
                double due = PhaseEndTime;
                if (insufficientSince >= 0) due = Math.Min(due, insufficientSince + c.AbandonSeconds);
                if (Phase == LinePhase.FinalDefense && defenderIndex + 1 < final.DefenseOrder.Count)
                    due = Math.Min(due, DefenderEnd(defenderIndex));
                if (Phase == LinePhase.FinalDiscussion && DeclarationsPending) due = Math.Min(due, final.DeclareUntil);
                foreach (int id in ids)
                    if (seats[id].Phone.Status == CallStatus.Dialing) due = Math.Min(due, seats[id].Phone.RingUntil);
                if (due > time) break;
                now = due;
                if (insufficientSince >= 0 && insufficientSince + c.AbandonSeconds <= due)
                {
                    endReason = PhaseEndReason.Abandoned;
                    Enter(LinePhase.Abandoned, due, 0);
                    picks.Clear();
                    IsFinished = true;
                    break;
                }
                foreach (int id in ids)
                {
                    var s = seats[id];
                    if (s.Phone.Status == CallStatus.Dialing && s.Phone.RingUntil <= due) EndCall(id, due, CallEndReason.Missed);
                }
                // the declaration deadline: whatever was declared is revealed now (before the defence, if both fall together)
                if (Phase == LinePhase.FinalDiscussion && DeclarationsPending && final.DeclareUntil <= due) RevealDeclarations(due);
                if (due >= PhaseEndTime) Advance(due);
                else if (Phase == LinePhase.FinalDefense && DefenderEnd(defenderIndex) <= due) { defenderIndex++; Changed(); }
            }
            now = time;
        }

        private void Advance(double time)
        {
            switch (Phase)
            {
                case LinePhase.Intro: StartTask(0, time); break;
                case LinePhase.TaskPlay:
                    if (shortened) endReason = PhaseEndReason.TimerShortened;
                    ResolveTask(time);
                    Enter(LinePhase.TaskReveal, time, c.Durations.TaskReveal);
                    break;
                case LinePhase.TaskReveal:
                    if (taskIndex < 2) Enter(LinePhase.Break, time, c.Durations.Break);
                    // v5.2: no meeting 2; everything the shift does landed at its reveal. With ShiftMeeting (v5.0 / v5.1) an
                    // unstaffed shift is followed by meeting 2 and a staffed one skips it.
                    else if (c.ShiftMeeting && tasks[2].Failed) StartMeeting(1, time);
                    else { if (c.ShiftMeeting) endReason = PhaseEndReason.Skipped; StartCrisis(time); }
                    break;
                case LinePhase.Break:
                    if (taskIndex == 0) StartTask(1, time);
                    else StartMeeting(0, time);
                    break;
                case LinePhase.MeetingDossier: Enter(LinePhase.MeetingDiscussion, time, c.Durations.MeetingDiscussion); break;
                case LinePhase.MeetingDiscussion:
                    meeting.Voteable = CountVoteable();
                    if (meeting.Voteable == 0) { endReason = PhaseEndReason.Skipped; EndMeeting(time); }
                    else StartMeetingVote(time);
                    break;
                case LinePhase.MeetingVote: TallyMeetingVote(time); Enter(LinePhase.MeetingVerdict, time, c.Durations.MeetingVerdict); break;
                case LinePhase.MeetingVerdict: EndMeeting(time); break;
                case LinePhase.CrisisCalls: ExpireFiles(time); StartCrisisVote(time); break;
                case LinePhase.CrisisVote: ResolveCrisis(time); Enter(LinePhase.CrisisVerdict, time, c.Durations.CrisisVerdict); break;
                case LinePhase.CrisisVerdict:
                    // nobody has suç (CandidateMinBlame): no candidates, nobody is fired, straight to the results
                    if (final.Candidates.Count == 0 || final.FiringsTotal <= 0) { endReason = PhaseEndReason.Skipped; ToResults(time); }
                    else StartFinalDiscussion(time);
                    break;
                case LinePhase.FinalDiscussion: RevealDeclarations(time); StartDefense(time); break;
                case LinePhase.FinalDefense:
                    if (final.FiringsTotal <= 0 || Remaining().Count == 0) ToResults(time);
                    else StartFinalVote(time, 0, Remaining(), false);
                    break;
                case LinePhase.FinalVote: ResolveFinalVote(time); break;
                case LinePhase.FinalVerdict: AfterVerdict(time); break;
                case LinePhase.Results: IsFinished = true; Changed(); break;
            }
        }

        private void EndMeeting(double time)
        {
            ClearBallots();
            if (meeting.Index == 0) { SetDay(1); StartTask(2, time); }
            else StartCrisis(time);
        }

        private void ClearBallots()
        {
            foreach (int id in ids) seats[id].Ballots = new int[0];
        }

        private void NewBallots(int count, bool firedVote)
        {
            foreach (int id in ids)
            {
                var s = seats[id];
                s.Ballots = firedVote && s.Fired ? new int[0] : new int[count];
                for (int i = 0; i < s.Ballots.Length; i++) s.Ballots[i] = -1;
            }
        }

        // ---- actions

        public LineActionResult Apply(int player, LineAction action, double time)
        {
            Tick(time);
            if (action == null) return LineActionResult.Fail(0, LineFailure.BadParam);
            LineFailure fail;
            if (action.WeekId != setup.WeekId) fail = LineFailure.StaleWeek;
            else if (!HasSeat(player)) fail = LineFailure.InvalidTarget;
            else
            {
                var s = seats[player];
                if (s.HasRequest && action.RequestId <= s.LastRequest) fail = LineFailure.Duplicate;
                else
                {
                    s.HasRequest = true;
                    s.LastRequest = action.RequestId;
                    Changed();
                    if (!s.Connected) fail = LineFailure.Absent;
                    else if (action.Verb == LineVerb.HostContinue) fail = LineFailure.HostOnly;
                    else if (IsFinished) fail = LineFailure.WrongPhase;
                    else fail = Act(player, action, time);
                }
            }
            if (fail != LineFailure.None) return LineActionResult.Fail(action.RequestId, fail);
            Changed();
            return LineActionResult.Ok(action.RequestId);
        }

        private LineFailure Act(int id, LineAction a, double time)
        {
            switch (a.Verb)
            {
                case LineVerb.Call:
                case LineVerb.Answer:
                case LineVerb.Decline:
                case LineVerb.HangUp: return PhoneAction(id, a, time);
                case LineVerb.SubmitTask: return SubmitTask(id, a, time);
                case LineVerb.Whistle: return Whistle(id, a, time);
                case LineVerb.Investigate: return Investigate(id, a, time);
                case LineVerb.SendText:
                case LineVerb.SendMeetingChat: return MessageAction(id, a, time);
                case LineVerb.Transfer: return Transfer(id, a, time);
                case LineVerb.Record: return Record(id, time);
                case LineVerb.Leak: return Leak(id, a, time);
                case LineVerb.Vote: return Vote(id, a, time);
                case LineVerb.Prank: return Prank(id, a, time);
                case LineVerb.Declare: return Declare(id, a, time);
                case LineVerb.FileOpen:
                case LineVerb.FileShred: return FileAction(id, a, time);
                default: return LineFailure.BadParam;
            }
        }

        private LineFailure TargetFailure(int actor, int target)
        {
            if (!HasSeat(target)) return LineFailure.InvalidTarget;
            return target == actor ? LineFailure.Self : LineFailure.None;
        }

        private LineFailure Vote(int id, LineAction a, double time)
        {
            var s = seats[id];
            if (Phase != LinePhase.MeetingVote && Phase != LinePhase.CrisisVote && Phase != LinePhase.FinalVote) return LineFailure.WrongPhase;
            if (s.Fired) return LineFailure.Fired;
            int index = Phase == LinePhase.MeetingVote ? a.A : Math.Max(0, a.A);
            if (index < 0 || index >= s.Ballots.Length) return LineFailure.BadParam;
            if (a.Target != -1)
            {
                var f = TargetFailure(id, a.Target);
                if (f != LineFailure.None) return f;
                if (Phase == LinePhase.FinalVote && !final.Options.Contains(a.Target)) return LineFailure.InvalidTarget;
            }
            s.Ballots[index] = a.Target;
            Emit(LineEventType.VoteCast, time, actor: id);
            return LineFailure.None;
        }

        // ---- presence

        public void SetConnected(int player, bool connected, double time)
        {
            Tick(time);
            if (!HasSeat(player)) throw new ArgumentException("Unknown seat.");
            var s = seats[player];
            if (s.Connected == connected || IsFinished) return;
            s.Connected = connected;
            Changed();
            Emit(LineEventType.Presence, time, target: player, a: connected ? 1 : 0);
            if (!connected) EndCall(player, time, CallEndReason.Disconnected);
            int count = 0;
            foreach (int id in ids) if (seats[id].Connected) count++;
            if (count < 2) { if (insufficientSince < 0) insufficientSince = time; }
            else insufficientSince = -1;
            if (Phase == LinePhase.TaskPlay) CheckEarlyFinish(time);
        }

        public List<LineEvent> DrainEvents()
        {
            var drained = events;
            events = new List<LineEvent>();
            return drained;
        }

        public void HostContinue(double time)
        {
            Tick(time);
            if (Phase != LinePhase.Results || IsFinished) return;
            endReason = PhaseEndReason.HostContinue;
            IsFinished = true;
            Changed();
            Emit(LineEventType.PhaseChanged, time, a: (int)Phase, b: (int)endReason);
        }

        public LineSessionHistory NextHistory()
        {
            var h = setup.History == null ? new LineSessionHistory() : LineCodec.Clone(setup.History);
            while (h.Wins.Count < LineProtocol.MaxPlayers) h.Wins.Add(0);
            while (h.TimesFired.Count < LineProtocol.MaxPlayers) h.TimesFired.Add(0);
            if (result == null) return h;
            h.WeeksPlayed++;
            foreach (int id in result.Winners) h.Wins[id]++;
            foreach (int id in result.Fired) h.TimesFired[id]++;
            return h;
        }
    }
}
