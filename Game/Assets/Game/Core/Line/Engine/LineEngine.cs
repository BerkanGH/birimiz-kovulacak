using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    internal sealed partial class LineEngine : ILineEngine
    {
        private sealed class Seat
        {
            internal LinePlayerInfo Info;
            internal bool Connected = true, HasRequest, Submitted, Voted, TrickUsed, TipUsed, Erased;
            internal uint LastRequest;
            internal int Blame, Ihtars, Agenda, TricksUsed, TipsUsed, MailsSent, Target = -1, A = -1, Vote = -1, Attempts;
            internal TrickType Trick, BlockedBy;
            internal double BlockedUntil, CoffeeUntil, OfflineUntil, DeadUntil, TapUntil;
            internal int Call = -1;
            internal int TapTarget = -1;
            internal LinePhoneView Phone = new LinePhoneView();
            internal readonly List<LineMailView> Inbox = new List<LineMailView>();
            internal readonly List<LineMailView> PendingMail = new List<LineMailView>();
            internal readonly List<LineCallRecordView> Log = new List<LineCallRecordView>();
        }
        private readonly LineSetup setup;
        private readonly LineConfig c;
        private readonly LineProfile profile;
        private readonly LineRandom rng;
        private readonly Seat[] seats = new Seat[6];
        private readonly List<int> ids = new List<int>();
        private readonly List<RoundType> schedule = new List<RoundType>();
        private readonly List<RoundType> seen = new List<RoundType>();
        private readonly List<int> usedTopics = new List<int>();
        private readonly List<LineRoundReveal> reveals = new List<LineRoundReveal>();
        private List<LineEvent> events = new List<LineEvent>();
        private readonly bool firstWeek;
        private readonly int days;
        private double now, phaseStart, insufficientSince = -1;
        private int roundIndex = -1, favourite = -1, defenderIndex;
        private bool firstTimeRound;
        private LineMeetingView meeting;
        private LineWeekResult result;

        public LineConfig Config { get { return LineCodec.Clone(c); } }
        public LineSetup Setup { get { return LineCodec.Clone(setup); } }
        public LinePhase Phase { get; private set; }
        public double PhaseEndTime { get; private set; }
        public int Day { get; private set; }
        public long StateVersion { get; private set; }
        public bool IsFinished { get; private set; }
        public LineWeekResult Result { get { return LineCodec.Clone(result); } }
        private bool Office { get { return Phase >= LinePhase.Morning && Phase <= LinePhase.RoundReveal; } }
        private bool InRound { get { return Phase >= LinePhase.RoundIntro && Phase <= LinePhase.RoundReveal; } }
        private bool Unlocked { get { return !firstWeek || Day >= c.UnlockDayFirstWeek; } }
        // LineDurations.Scale scales RoundPlay but cannot scale the separate head-count list.
        private double RoundPlayLength { get { return c.RoundPlayByPlayers[ids.Count - 3] * (c.Durations.RoundPlay / 150.0); } }

        internal LineEngine(LineSetup input, LineConfig config)
        {
            if (input == null || input.Players == null || input.Players.Count < 3 || input.Players.Count > 6)
                throw new ArgumentException("A week requires three to six distinct seats.", nameof(input));
            bool[] present = new bool[6];
            for (int i = 0; i < input.Players.Count; i++)
            {
                var p = input.Players[i];
                if (p == null || p.Id < 0 || p.Id >= 6 || present[p.Id]) throw new ArgumentException("Invalid or duplicate seat.", nameof(input));
                present[p.Id] = true;
            }
            // Public timers use zero/-1 for inactive values, so host seconds must start at or above zero.
            if (double.IsNaN(input.StartTime) || double.IsInfinity(input.StartTime) || input.StartTime < 0)
                throw new ArgumentException("Start time must be finite and nonnegative.", nameof(input));
            if (input.Length != WeekLength.Short && input.Length != WeekLength.Full)
                throw new ArgumentException("Unknown week length.", nameof(input));
            if (input.History != null && (input.History.SessionPoints == null || input.History.PastManagers == null ||
                input.History.SeenRounds == null || input.History.UsedTopics == null || input.History.TimesFired == null ||
                input.History.HiddenRoles == null || input.History.RoundUses == null))
                throw new ArgumentException("History collections cannot be null.", nameof(input));
            setup = LineCodec.Clone(input);
            c = LineCodec.Clone(config ?? LineConfig.CreateDefault());
            ValidateConfig();
            rng = new LineRandom(setup.Seed);
            firstWeek = setup.History == null || setup.History.WeeksPlayed == 0;
            days = c.DaysFor(setup.Length);
            for (int id = 0; id < 6; id++) if (present[id])
            {
                LinePlayerInfo info = null;
                for (int i = 0; i < setup.Players.Count; i++) if (setup.Players[i].Id == id) info = setup.Players[i];
                ids.Add(id); seats[id] = new Seat { Info = info, Blame = c.StartBlame };
            }
            if (setup.History != null)
            {
                seen.AddRange(setup.History.SeenRounds);
                usedTopics.AddRange(setup.History.UsedTopics);
            }
            var profiles = new List<LineProfile>();
            string last = setup.History != null && setup.History.PastManagers.Count > 0 ? setup.History.PastManagers[setup.History.PastManagers.Count - 1] : "";
            for (int i = 0; i < c.Profiles.Count; i++) if (c.Profiles[i].Id != last) profiles.Add(c.Profiles[i]);
            if (profiles.Count == 0) profiles.AddRange(c.Profiles);
            string manager = setup.ManagerProfileId;
            if (string.IsNullOrEmpty(manager) && setup.History != null) manager = setup.History.NextManager;
            profile = string.IsNullOrEmpty(manager) ? profiles[rng.Index(profiles.Count)] : c.Profile(manager);
            int leader = -1, best = int.MinValue, leaders = 0;
            foreach (int id in ids)
            {
                int points = PriorPoints(id);
                if (points > best) { best = points; leader = id; leaders = 1; }
                else if (points == best) leaders++;
            }
            if (leaders == 1) seats[leader].Blame += c.LeaderStartBonus;
            AssignSecrets();
            MakeSchedule();
            now = setup.StartTime;
            StartMorning(now);
        }

        private void ValidateConfig()
        {
            if (c.Durations == null || c.Rounds == null || c.Profiles == null || c.Profiles.Count == 0 ||
                c.RoundsPerDay != 2 || c.ShortWeekDays < 1 || c.FullWeekDays < 1 ||
                c.TopicCategories < 2 || c.TopicsPerCategory < 1 || c.MailTemplates < 1 || c.Rounds.ComplaintReasons < 1 ||
                c.RoundPlayByPlayers == null || c.RoundPlayByPlayers.Count != 4 || c.StartBlame < 0 || c.LeaderStartBonus < 0 ||
                c.FinalShortlistSmall < 2 || c.FinalShortlistSmall > 3 || c.FinalShortlistLarge < 2 || c.FinalShortlistLarge > 5 ||
                c.MaxIhtarsPerPlayerPerWeek < 1 || c.TricksPerWeekShort < 0 || c.TricksPerWeekFull < 0 || c.TipsPerWeek < 0)
                throw new ArgumentException("Invalid line config.");
            var d = c.Durations;
            float[] lengths = { d.Morning, d.MorningFirst, d.RoundIntro, d.RoundIntroFirst, d.RoundPlay, d.AllSubmittedRemaining,
                d.RoundReveal, d.Dossier, d.DossierPerRecording, d.Discussion, d.FinalDiscussion, d.Vote, d.Verdict,
                d.DefensePerPlayer, d.FinalVote, d.FinalVerdict, d.WeekResults, d.Ring, c.CoffeeSeconds,
                c.PhoneDeadSeconds, c.InternetSeconds, c.WiretapSeconds, c.RecordingMinCallSeconds, c.RecordingSnippetSeconds };
            for (int i = 0; i < lengths.Length; i++) if (float.IsNaN(lengths[i]) || float.IsInfinity(lengths[i]) || lengths[i] < 0)
                throw new ArgumentException("Durations must be finite and nonnegative.");
            foreach (float length in c.RoundPlayByPlayers)
                if (float.IsNaN(length) || float.IsInfinity(length) || length < 0) throw new ArgumentException("Invalid head-count duration.");
            if (c.RecordingCandidates < 0 || c.MailsPerDay < 0 || float.IsNaN(c.RecordingFocusWeight) ||
                float.IsInfinity(c.RecordingFocusWeight) || c.RecordingFocusWeight < 0)
                throw new ArgumentException("Invalid line limits or recording weight.");
            for (int i = 0; i < c.Profiles.Count; i++)
            {
                var p = c.Profiles[i];
                if (p == null || string.IsNullOrEmpty(p.Id) || !ValidChance(p.TraceChance) || !ValidChance(p.LeakChance) || p.Recordings < 0)
                    throw new ArgumentException("Invalid manager profile.");
            }
        }

        private static bool ValidChance(float value)
        { return !float.IsNaN(value) && value >= 0 && value <= 1; }

        private void AssignSecrets()
        {
            var targets = new List<int>();
            bool valid;
            do
            {
                targets.Clear(); valid = true; int[] targetCounts = new int[6];
                foreach (int id in ids)
                {
                    int target;
                    do { target = ids[rng.Index(ids.Count)]; } while (target == id);
                    targets.Add(target);
                    if (++targetCounts[target] > 2) valid = false;
                }
            } while (!valid);
            var tricks = new List<TrickType> { TrickType.SahteEposta, TrickType.KahveDok, TrickType.FisCek, TrickType.InternetKes, TrickType.HatDinle };
            rng.Shuffle(tricks);
            for (int i = 0; i < ids.Count; i++)
            {
                int id = ids[i];
                seats[id].Agenda = setup.ForceAgendas != null && setup.ForceAgendas.Count > id ? setup.ForceAgendas[id] : targets[i];
                seats[id].Trick = setup.ForceTricks != null && setup.ForceTricks.Count > id ? setup.ForceTricks[id] : tricks[i < tricks.Count ? i : rng.Index(tricks.Count)];
                if (!HasSeat(seats[id].Agenda) || seats[id].Agenda == id || seats[id].Trick < TrickType.None || seats[id].Trick >= TrickType.KayitSil)
                    throw new ArgumentException("Invalid forced secret.");
            }
        }

        private void MakeSchedule()
        {
            int[] counts = new int[7]; int hiddenRounds = 0;
            if (setup.History != null)
                for (int k = 1; k <= 6 && k < setup.History.RoundUses.Count; k++) counts[k] = setup.History.RoundUses[k];
            for (int day = 0; day < days; day++)
            {
                bool[] today = new bool[7];
                for (int r = 0; r < c.RoundsPerDay; r++)
                {
                    RoundType type;
                    int flat = day * c.RoundsPerDay + r;
                    if (setup.ForceRounds != null && setup.ForceRounds.Count > flat) type = setup.ForceRounds[flat];
                    else if (firstWeek && day == 0) type = r == 0 ? RoundType.PrimHavuzu : RoundType.FarkliEposta;
                    else
                    {
                        var candidates = new List<RoundType>(); int minimum = int.MaxValue;
                        for (int k = 1; k <= 6; k++)
                        {
                            var candidate = (RoundType)k;
                            bool deal = candidate == RoundType.PrimHavuzu || candidate == RoundType.OrtakSifre;
                            if (today[k] || deal != (r == 0) ||
                                (candidate == RoundType.KayipRapor && ids.Count < Math.Max(4, c.Rounds.KayipRaporMinPlayers)) ||
                                (candidate == RoundType.IhbarKutusu && ids.Count < c.Rounds.ComplaintMinPlayers) ||
                                (IsHiddenRound(candidate) && hiddenRounds >= ids.Count)) continue;
                            if (counts[k] < minimum) { minimum = counts[k]; candidates.Clear(); }
                            if (counts[k] == minimum) candidates.Add(candidate);
                        }
                        type = candidates[rng.Index(candidates.Count)];
                    }
                    if (type < RoundType.PrimHavuzu || type > RoundType.OrtakSifre ||
                        (type == RoundType.KayipRapor && ids.Count < Math.Max(4, c.Rounds.KayipRaporMinPlayers)) ||
                        (type == RoundType.IhbarKutusu && ids.Count < c.Rounds.ComplaintMinPlayers)) throw new ArgumentException("Invalid forced round.");
                    today[(int)type] = true; counts[(int)type]++; schedule.Add(type);
                    if (IsHiddenRound(type)) hiddenRounds++;
                }
            }
        }

        private static bool IsHiddenRound(RoundType type)
        { return type == RoundType.FarkliEposta || type == RoundType.KayipRapor || type == RoundType.OrtakSifre; }

        private bool HasSeat(int id) { return id >= 0 && id < seats.Length && seats[id] != null; }
        private void Changed() { StateVersion++; }
        private void Emit(LineEventType type, double time, int actor = -1, int target = -1, int a = -1, int b = -1, int recipient = -1)
        {
            events.Add(new LineEvent { Type = type, Time = time, Actor = actor, Target = target, A = a, B = b,
                Audience = recipient < 0 ? LineAudience.All : LineAudience.Player, AudiencePlayer = recipient });
        }
        private void Enter(LinePhase phase, double time, double duration)
        {
            Phase = phase; phaseStart = time; PhaseEndTime = time + duration; Changed();
            if (!Office) EndAllCalls(time, CallEndReason.PhaseEnd);
            Emit(LineEventType.PhaseChanged, time, a: (int)phase);
        }

        private void StartMorning(double time)
        {
            roundIndex = -1; round = null; meeting = null; reveals.Clear(); traces.Clear(); tips.Clear(); complaints.Clear(); calls.Clear();
            foreach (int id in ids)
            {
                var s = seats[id]; s.TrickUsed = s.TipUsed = s.Erased = s.Submitted = s.Voted = false;
                s.MailsSent = s.Attempts = 0; s.Target = s.A = s.Vote = -1;
            }
            favourite = -1;
            if (profile.HasFavourite)
            {
                int max = -1, leaders = 0;
                foreach (int id in ids) { if (seats[id].Blame > max) { max = seats[id].Blame; leaders = 1; } else if (seats[id].Blame == max) leaders++; }
                var options = new List<int>();
                foreach (int id in ids) if (leaders != 1 || seats[id].Blame != max) options.Add(id);
                favourite = options[rng.Index(options.Count)];
            }
            Enter(LinePhase.Morning, time, firstWeek && Day == 0 ? c.Durations.MorningFirst : c.Durations.Morning);
            if (favourite >= 0) Emit(LineEventType.FavouriteNamed, time, target: favourite);
            int memoId = nextMailId++;
            foreach (int id in ids) DeliverMail(id, MailKind.Manager, LineIds.Manager, 0, -1, true, time, memoId);
        }

        public void Tick(double time)
        {
            if (double.IsNaN(time) || double.IsInfinity(time) || time < now) throw new ArgumentOutOfRangeException(nameof(time), "Host time must be finite and monotonic.");
            if (IsFinished) { now = time; return; }
            while (!IsFinished)
            {
                double due = PhaseEndTime;
                if (insufficientSince >= 0) due = Math.Min(due, insufficientSince + 60);
                if (Phase == LinePhase.FinalDefense && defenderIndex + 1 < meeting.DefenseOrder.Count)
                    due = Math.Min(due, phaseStart + (defenderIndex + 1) * c.Durations.DefensePerPlayer);
                foreach (int id in ids)
                {
                    var s = seats[id];
                    if (s.Phone.Status == CallStatus.Dialing) due = Math.Min(due, s.Phone.RingUntil);
                    if (s.BlockedUntil > 0) due = Math.Min(due, s.BlockedUntil);
                    if (s.CoffeeUntil > 0) due = Math.Min(due, s.CoffeeUntil);
                    if (s.OfflineUntil > 0) due = Math.Min(due, s.OfflineUntil);
                    if (s.DeadUntil > 0) due = Math.Min(due, s.DeadUntil);
                    if (s.TapUntil > 0) due = Math.Min(due, s.TapUntil);
                }
                if (due > time) break;
                now = due;
                if (insufficientSince >= 0 && insufficientSince + 60 <= due)
                {
                    Enter(LinePhase.Abandoned, due, 0); IsFinished = true; result = null; break;
                }
                Expire(due);
                if (due >= PhaseEndTime) Advance(due);
                else if (Phase == LinePhase.FinalDefense)
                {
                    // Compare the very same absolute boundary used above, avoiding floating-point division
                    // rounding a scaled boundary back into the previous defender (and an endless due loop).
                    if (phaseStart + (defenderIndex + 1) * c.Durations.DefensePerPlayer <= due)
                    { defenderIndex++; Changed(); }
                }
            }
            now = time;
        }

        private void Expire(double time)
        {
            foreach (int id in ids)
            {
                var s = seats[id];
                if (s.Phone.Status == CallStatus.Dialing && s.Phone.RingUntil <= time) EndCall(id, time, CallEndReason.Missed);
                if (s.CoffeeUntil > 0 && s.CoffeeUntil <= time) { s.CoffeeUntil = 0; RefreshBlock(s); Changed(); }
                if (s.OfflineUntil > 0 && s.OfflineUntil <= time)
                {
                    s.OfflineUntil = 0; RefreshBlock(s); Changed();
                    for (int i = 0; i < s.PendingMail.Count; i++) { s.Inbox.Add(s.PendingMail[i]); Emit(LineEventType.MailReceived, time, a: s.PendingMail[i].Id, recipient: id); }
                    s.PendingMail.Clear();
                }
                if (s.DeadUntil > 0 && s.DeadUntil <= time) { s.DeadUntil = 0; Changed(); }
                if (s.TapUntil > 0 && s.TapUntil <= time)
                { Emit(LineEventType.Wiretap, time, target: s.TapTarget, a: 0, recipient: id); s.TapUntil = 0; s.TapTarget = -1; Changed(); }
            }
        }

        private void Advance(double time)
        {
            switch (Phase)
            {
                case LinePhase.Morning: roundIndex = 0; StartRound(time); break;
                case LinePhase.RoundIntro: Enter(LinePhase.RoundPlay, time, RoundPlayLength); break;
                case LinePhase.RoundPlay: RevealRound(time); Enter(LinePhase.RoundReveal, time, c.Durations.RoundReveal); break;
                case LinePhase.RoundReveal:
                    if (++roundIndex < c.RoundsPerDay) StartRound(time); else { roundIndex = -1; StartDossier(time); } break;
                case LinePhase.MeetingDossier: Enter(LinePhase.MeetingDiscussion, time, Day == days - 1 ? c.Durations.FinalDiscussion : c.Durations.Discussion); break;
                case LinePhase.MeetingDiscussion:
                    if (Day == days - 1) StartDefense(time); else Enter(LinePhase.MeetingVote, time, c.Durations.Vote); break;
                case LinePhase.MeetingVote: DailyVerdict(time); Enter(LinePhase.MeetingVerdict, time, c.Durations.Verdict); break;
                case LinePhase.MeetingVerdict: Day++; StartMorning(time); break;
                case LinePhase.FinalDefense: Enter(LinePhase.FinalVote, time, c.Durations.FinalVote); break;
                case LinePhase.FinalVote: FinalVerdict(time); Enter(LinePhase.FinalVerdict, time, c.Durations.FinalVerdict); break;
                case LinePhase.FinalVerdict: FinishResult(); Enter(LinePhase.WeekResults, time, c.Durations.WeekResults); break;
                case LinePhase.WeekResults: IsFinished = true; Changed(); break;
            }
        }

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
                    s.HasRequest = true; s.LastRequest = action.RequestId;
                    Changed(); // Consuming a fresh request changes replay/deduplication state even on rejection.
                    if (!s.Connected) fail = LineFailure.Absent;
                    else if (action.Verb == LineVerb.HostContinue) fail = LineFailure.HostOnly;
                    else if (IsFinished) fail = LineFailure.WrongPhase;
                    else fail = Act(player, action, time);
                }
            }
            if (fail != LineFailure.None) return LineActionResult.Fail(action.RequestId, fail);
            Changed(); return LineActionResult.Ok(action.RequestId);
        }

        private LineFailure Act(int id, LineAction a, double time)
        {
            switch (a.Verb)
            {
                case LineVerb.Call: case LineVerb.Answer: case LineVerb.Decline: case LineVerb.HangUp: return PhoneAction(id, a, time);
                case LineVerb.SubmitChoice: case LineVerb.Attempt: return RoundAction(id, a, time);
                case LineVerb.UseTrick: case LineVerb.TipOff: return TrickAction(id, a, time);
                case LineVerb.SendMail: return MailAction(id, a, time);
                case LineVerb.PickNextManager: return PickNextManager(id, a, time);
                case LineVerb.Vote:
                    if (Phase != LinePhase.MeetingVote && Phase != LinePhase.FinalVote) return LineFailure.WrongPhase;
                    var valid = TargetFailure(id, a.Target, false, true); if (valid != LineFailure.None) return valid;
                    if (Phase == LinePhase.FinalVote && a.Target != -1 && !meeting.Shortlist.Contains(a.Target)) return LineFailure.InvalidTarget;
                    seats[id].Voted = true; seats[id].Vote = a.Target; Emit(LineEventType.VoteCast, time, actor: id); return LineFailure.None;
                default: return LineFailure.BadParam;
            }
        }

        private LineFailure TargetFailure(int actor, int target, bool present = true, bool abstain = false)
        {
            if (abstain && target == -1) return LineFailure.None;
            if (!HasSeat(target)) return LineFailure.InvalidTarget;
            if (target == actor) return LineFailure.Self;
            return present && !seats[target].Connected ? LineFailure.Absent : LineFailure.None;
        }

        public void SetConnected(int player, bool connected, double time)
        {
            Tick(time);
            if (!HasSeat(player)) throw new ArgumentException("Unknown seat.", nameof(player));
            var s = seats[player]; if (s.Connected == connected || IsFinished) return;
            s.Connected = connected; Changed(); Emit(LineEventType.Presence, time, target: player, a: connected ? 1 : 0);
            if (!connected)
            {
                EndCall(player, time, CallEndReason.Disconnected);
                s.Submitted = s.Voted = false; s.Target = s.A = s.Vote = -1;
            }
            int count = 0; foreach (int id in ids) if (seats[id].Connected) count++;
            if (count < 2) { if (insufficientSince < 0) insufficientSince = time; } else insufficientSince = -1;
            if (Phase == LinePhase.RoundPlay) Shorten(time);
        }

        public List<LineEvent> DrainEvents() { var drained = events; events = new List<LineEvent>(); return drained; }
        public void HostContinue(double time) { Tick(time); if (Phase == LinePhase.WeekResults && !IsFinished) { IsFinished = true; Changed(); } }
    }
}
