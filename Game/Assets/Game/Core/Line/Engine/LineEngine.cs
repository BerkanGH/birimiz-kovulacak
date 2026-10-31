using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    internal sealed partial class LineEngine : ILineEngine
    {
        private sealed class Seat
        {
            internal LinePlayerInfo Info;
            internal bool Connected = true, HasRequest, TrickUsed;
            internal uint LastRequest;
            internal int Blame, Warnings, Money, RawIncome, Purchases, BlockPurchases, Presentations, Remedials, BlockRemedials;
            internal int TricksUsed, Texts, Emails, Complaints, Chats, Received, Quality, TasksConnected, Credits, Opportunities;
            internal int Vote = -1, TapTarget = -1, Call = -1, RoleUses;
            internal readonly int[] Paid = new int[6];
            internal ObjectiveKind Objective;
            internal int ObjectiveTarget = -1;
            internal bool ObjectiveDone;
            internal TrickType Trick;
            internal double CoffeeUntil, OfflineUntil, DeadUntil, TapUntil, NextMessage, NextTransfer;
            internal long MessageSeq;
            internal LinePhoneView Phone = new LinePhoneView();
            internal readonly List<LineMessage> Messages = new List<LineMessage>();
            internal readonly List<LineCallRecordView> Log = new List<LineCallRecordView>();
            internal readonly HashSet<int> Access = new HashSet<int>();
            internal readonly HashSet<int> ArchiveAccess = new HashSet<int>();
            internal readonly HashSet<int> Bought = new HashSet<int>();
            internal readonly HashSet<int> Presented = new HashSet<int>();
        }
        private readonly LineSetup setup;
        private readonly LineConfig c;
        private readonly LineProfile profile;
        private readonly LineRandom rng;
        private readonly Seat[] seats = new Seat[6];
        private readonly List<int> ids = new List<int>();
        private readonly List<TaskKind> schedule = new List<TaskKind>();
        private readonly List<TaskState> tasks = new List<TaskState>();
        private readonly List<TaskKind> seen = new List<TaskKind>();
        private List<LineEvent> events = new List<LineEvent>();
        private readonly bool firstWeek;
        private readonly int days;
        private double now, phaseStart, insufficientSince = -1, carry;
        private float carried;
        private int roundIndex = -1, favourite = -1, defenderIndex;
        private bool firstTimeTask, shortened, closed;
        private int finalProductivity = -1;
        private CompanyChange lastChange;
        private TaskKind lastTask;
        private int lastDelta;
        private TaskState task;
        private LineMeetingView meeting;
        private LineWeekResult result;
        private PhaseEndReason endReason = PhaseEndReason.Start;

        public LineConfig Config { get { return LineCodec.Clone(c); } }
        public LineSetup Setup { get { return LineCodec.Clone(setup); } }
        public LinePhase Phase { get; private set; }
        public double PhaseEndTime { get; private set; }
        public int Day { get; private set; }
        public long StateVersion { get; private set; }
        public bool IsFinished { get; private set; }
        public LineWeekResult Result { get { return LineCodec.Clone(result); } }
        private bool Office { get { return InTask || Phase == LinePhase.Morning || Phase == LinePhase.Break; } }
        private bool InTask { get { return Phase >= LinePhase.RoundIntro && Phase <= LinePhase.RoundReveal; } }
        private bool IsMeeting { get { return Phase >= LinePhase.MeetingDossier && Phase <= LinePhase.FinalVerdict; } }
        private bool EvidenceOpen { get { return Office || PresentOpen; } }
        private bool PresentOpen { get { return Phase == LinePhase.MeetingDossier || Phase == LinePhase.MeetingDiscussion; } }
        private bool TransfersOpen { get { return Office || PresentOpen || (Phase == LinePhase.MeetingVote && Day < days - 1); } }
        private bool RemedialOpen { get { return Phase == LinePhase.Morning || Phase == LinePhase.RoundReveal || Phase == LinePhase.Break; } }
        private bool Unlocked { get { return !firstWeek || Day >= c.UnlockDayFirstWeek; } }
        private int TrickAllowance { get { return setup.Length == WeekLength.Short ? c.TricksPerWeekShort : c.TricksPerWeekFull; } }
        private bool HasSeat(int id) { return id >= 0 && id < 6 && seats[id] != null; }
        private int Prior(List<int> values, int id) { return values != null && id < values.Count ? values[id] : 0; }
        private int PriorPoints(int id) { return setup.History == null ? 0 : Prior(setup.History.SessionPoints, id); }
        private void Changed() { StateVersion++; }

        internal LineEngine(LineSetup input, LineConfig config)
        {
            if (input == null || input.Players == null || input.Players.Count < 3 || input.Players.Count > 6 ||
                !Finite(input.StartTime) || input.StartTime < 0 || !Enum.IsDefined(typeof(WeekLength), input.Length))
                throw new ArgumentException("Invalid match setup.");
            bool[] present = new bool[6];
            foreach (var p in input.Players)
            {
                if (p == null || p.Id < 0 || p.Id >= 6 || present[p.Id]) throw new ArgumentException("Invalid or duplicate seat.");
                present[p.Id] = true;
            }
            var h = input.History;
            if (h != null && (h.SessionPoints == null || h.PastManagers == null || h.SeenTasks == null || h.TaskUses == null ||
                h.TimesFired == null || h.RoleUses == null || h.WeeksPlayed < 0)) throw new ArgumentException("Invalid history.");
            try { setup = LineCodec.Clone(input); c = LineCodec.Clone(config ?? LineConfig.CreateDefault()); }
            catch (FormatException ex) { throw new ArgumentException("Invalid setup/config schema.", ex); }
            ValidateConfig();
            rng = new LineRandom(setup.Seed);
            firstWeek = h == null || h.WeeksPlayed == 0;
            days = c.DaysFor(setup.Length);
            for (int id = 0; id < 6; id++) if (present[id])
            {
                LinePlayerInfo info = setup.Players.Find(p => p.Id == id);
                ids.Add(id); seats[id] = new Seat { Info = info, Blame = c.StartBlame, Money = c.StartMoney };
            }
            if (h != null) seen.AddRange(h.SeenTasks);
            string manager = setup.ManagerProfileId;
            if (string.IsNullOrEmpty(manager) && h != null) manager = h.NextManager;
            if (string.IsNullOrEmpty(manager))
            {
                string last = h != null && h.PastManagers.Count > 0 ? h.PastManagers[h.PastManagers.Count - 1] : "";
                var options = c.Profiles.FindAll(p => p.Id != last);
                if (options.Count == 0) options.AddRange(c.Profiles);
                profile = options[rng.Index(options.Count)];
            }
            else
            {
                profile = c.Profiles.Find(p => p.Id == manager);
                if (profile == null) throw new ArgumentException("Unknown manager.");
            }
            if (!LineSchedule.Validate(c, ids.Count, setup.Length, profile.Id, out string reason)) throw new ArgumentException(reason);
            int leader = -1, best = int.MinValue, ties = 0;
            foreach (int id in ids) { int points = PriorPoints(id); if (points > best) { best = points; leader = id; ties = 1; } else if (points == best) ties++; }
            if (ties == 1) seats[leader].Blame += c.LeaderStartBonus;
            if (setup.ForceRoles != null) foreach (int role in setup.ForceRoles)
                if (role < -1 || role >= 0 && !HasSeat(role)) throw new ArgumentException("Invalid forced role.");
            AssignSecrets(); MakeSchedule(); now = setup.StartTime; StartMorning(now);
        }

        private static bool Finite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }
        private void ValidateConfig()
        {
            if (c.ConfigVersion != 4 || c.Durations == null || c.Tasks == null || c.TaskRules == null || c.Profiles == null || c.Profiles.Count == 0 ||
                c.RoundsPerDay != 2 || c.ShortWeekDays < 1 || c.FullWeekDays < 1 || c.MinSpareTasks < 0 || c.RoundPlayByPlayers == null || c.RoundPlayByPlayers.Count != 4)
                throw new ArgumentException("Invalid config structure.");
            // Validate every numeric tunable, including ones added to the public config in future revisions.
            ValidateNumbers(c); ValidateNumbers(c.Durations); ValidateNumbers(c.TaskRules);
            if (c.CompanyTargetPercent > 100 || c.CareerTargetPercent > 100 || c.BlockIncomeCap < c.BlockIncomeFull ||
                c.RivalGuardianSameTarget > 1 || c.RecordingExposureDecay > 1 || c.MessagesKeptPerPlayer < 1 || c.RecordingCandidates > 4096 ||
                c.TextMaxChars > 512 || c.EmailMaxChars > 512 || c.ComplaintMaxChars > 512 || c.MeetingChatMaxChars > 512 ||
                c.Durations.RoundPlay <= 0 || c.Durations.DefensePerPlayer <= 0 || c.Durations.BreakMax < c.Durations.Break || c.Durations.BreakMax < c.Durations.BreakClosing)
                throw new ArgumentException("Invalid config range.");
            foreach (float v in c.RoundPlayByPlayers) if (!Finite(v) || v <= 0) throw new ArgumentException("Invalid play duration.");
            var kinds = new HashSet<TaskKind>();
            foreach (var t in c.Tasks)
                if (t == null || t.Kind == TaskKind.None || !kinds.Add(t.Kind) || t.Version != 1 || t.MaxOutput < 1 || t.MaxOutput > 100000 ||
                    t.PlayFactor < 1 || t.PlayFactor > 1000 || t.MinPlayers < 3 || t.MaxPlayers > 6 || t.MaxPlayers < t.MinPlayers)
                    throw new ArgumentException("Invalid task config.");
            var managers = new HashSet<string>();
            foreach (var p in c.Profiles)
            {
                if (p == null || Array.IndexOf(LineManagerIds.All, p.Id) < 0 || !managers.Add(p.Id) || p.TaskWeights == null) throw new ArgumentException("Invalid profile.");
                ValidateNumbers(p); foreach (int w in p.TaskWeights) if (w < 0 || w > 100000) throw new ArgumentException("Invalid task weight.");
            }
            if (c.TaskRules.BonusPairOutput == null || c.TaskRules.BonusPairOutput.Count != 3 || c.TaskRules.BonusTrioOutput == null || c.TaskRules.BonusTrioOutput.Count != 4)
                throw new ArgumentException("Invalid bonus table.");
            foreach (int x in c.TaskRules.BonusPairOutput) if (x < 0 || x > 100) throw new ArgumentException("Invalid output.");
            foreach (int x in c.TaskRules.BonusTrioOutput) if (x < 0 || x > 100) throw new ArgumentException("Invalid output.");
        }
        private static void ValidateNumbers(object value)
        {
            foreach (var f in value.GetType().GetFields())
            {
                if (f.FieldType == typeof(int)) { int n = (int)f.GetValue(value); if (n < 0 || n > 1000000) throw new ArgumentException("Invalid " + f.Name); }
                if (f.FieldType == typeof(float)) { float n = (float)f.GetValue(value); if (!Finite(n) || n < 0 || n > 1000000) throw new ArgumentException("Invalid " + f.Name); }
            }
        }
        private void AssignSecrets()
        {
            var objectives = new List<ObjectiveKind>(); for (int k = 1; k <= 9; k++) objectives.Add((ObjectiveKind)k); rng.Shuffle(objectives);
            var tricks = new List<TrickType>(); for (int k = 1; k <= 5; k++) tricks.Add((TrickType)k); rng.Shuffle(tricks);
            var dealt = new HashSet<ObjectiveKind>();
            foreach (int id in ids)
            {
                var s = seats[id]; int i = ids.IndexOf(id);
                s.Objective = setup.ForceObjectives != null && setup.ForceObjectives.Count > id ? setup.ForceObjectives[id] : ObjectiveKind.None;
                if (s.Objective != ObjectiveKind.None && !dealt.Add(s.Objective)) throw new ArgumentException("Duplicate objective.");
                s.Trick = setup.ForceTricks != null && setup.ForceTricks.Count > id ? setup.ForceTricks[id] : tricks[i < 5 ? i : rng.Index(5)];
            }
            foreach (int id in ids) if (seats[id].Objective == ObjectiveKind.None)
            {
                var kind = objectives.Find(k => !dealt.Contains(k)); seats[id].Objective = kind; dealt.Add(kind);
            }
            int rival = ids.Find(id => seats[id].Objective == ObjectiveKind.Rival), guardian = ids.Find(id => seats[id].Objective == ObjectiveKind.Guardian);
            bool hasRival = ids.Exists(id => seats[id].Objective == ObjectiveKind.Rival), hasGuardian = ids.Exists(id => seats[id].Objective == ObjectiveKind.Guardian);
            foreach (int id in ids)
            {
                var s = seats[id]; if (s.Objective != ObjectiveKind.Rival && s.Objective != ObjectiveKind.Guardian) continue;
                var targets = ids.FindAll(p => p != id); s.ObjectiveTarget = targets[rng.Index(targets.Count)];
            }
            if (hasRival && hasGuardian)
            {
                if (rng.Unit() < c.RivalGuardianSameTarget)
                {
                    var common = ids.FindAll(id => id != rival && id != guardian);
                    int target = common[rng.Index(common.Count)]; seats[rival].ObjectiveTarget = seats[guardian].ObjectiveTarget = target;
                }
                else
                {
                    var distinct = ids.FindAll(id => id != guardian && id != seats[rival].ObjectiveTarget);
                    seats[guardian].ObjectiveTarget = distinct[rng.Index(distinct.Count)];
                }
            }
            foreach (int id in ids) if (setup.ForceObjectiveTargets != null && setup.ForceObjectiveTargets.Count > id)
            {
                var s = seats[id]; if (s.Objective != ObjectiveKind.Rival && s.Objective != ObjectiveKind.Guardian) continue;
                s.ObjectiveTarget = setup.ForceObjectiveTargets[id];
                if (!HasSeat(s.ObjectiveTarget) || s.ObjectiveTarget == id) throw new ArgumentException("Invalid objective target.");
            }
        }
        private void MakeSchedule()
        {
            if (setup.ForceTasks != null && setup.ForceTasks.Count > days * 2) throw new ArgumentException("Too many forced tasks.");
            for (int i = 0; i < days * 2; i++)
            {
                TaskKind kind;
                if (setup.ForceTasks != null && i < setup.ForceTasks.Count) kind = setup.ForceTasks[i];
                else if (firstWeek && i < 2 && !schedule.Contains(i == 0 ? TaskKind.BonusPool : TaskKind.InvoiceReconcile) &&
                    LineSchedule.Eligible(c.Task(i == 0 ? TaskKind.BonusPool : TaskKind.InvoiceReconcile), ids.Count, profile.Id))
                    kind = i == 0 ? TaskKind.BonusPool : TaskKind.InvoiceReconcile;
                else
                {
                    var pool = c.Tasks.FindAll(t => LineSchedule.Eligible(t, ids.Count, profile.Id) && !schedule.Contains(t.Kind));
                    int minimum = int.MaxValue;
                    foreach (var t in pool) minimum = Math.Min(minimum, setup.History == null ? 0 : Prior(setup.History.TaskUses, (int)t.Kind));
                    pool.RemoveAll(t => (setup.History == null ? 0 : Prior(setup.History.TaskUses, (int)t.Kind)) != minimum);
                    if (i % 2 == 1)
                    {
                        bool wantAll = LineTasks.Family(schedule[i - 1]) != TaskFamily.All;
                        var preferred = pool.FindAll(t => (LineTasks.Family(t.Kind) == TaskFamily.All) == wantAll);
                        if (preferred.Count > 0) pool = preferred;
                    }
                    int total = 0; foreach (var t in pool) total += Weight(t.Kind);
                    int draw = rng.Index(Math.Max(1, total)); kind = pool[0].Kind;
                    foreach (var t in pool) { draw -= Weight(t.Kind); if (draw < 0) { kind = t.Kind; break; } }
                }
                if (schedule.Contains(kind) || !LineSchedule.Eligible(c.Task(kind), ids.Count, profile.Id)) throw new ArgumentException("Invalid/repeated forced task.");
                schedule.Add(kind);
            }
        }
        private int Weight(TaskKind kind) { return (int)kind < profile.TaskWeights.Count ? profile.TaskWeights[(int)kind] : 1; }
        private int Role(int number, int exclude = -1, bool forced = true)
        {
            int pick = forced && setup.ForceRoles != null && number < setup.ForceRoles.Count ? setup.ForceRoles[number] : -1;
            if (pick >= 0 && (!HasSeat(pick) || pick == exclude)) throw new ArgumentException("Invalid forced role.");
            if (pick < 0)
            {
                var pool = ids.FindAll(id => id != exclude); int min = int.MaxValue, history = int.MaxValue;
                foreach (int id in pool) min = Math.Min(min, seats[id].RoleUses);
                pool.RemoveAll(id => seats[id].RoleUses != min);
                foreach (int id in pool) history = Math.Min(history, setup.History == null ? 0 : Prior(setup.History.RoleUses, id));
                pool.RemoveAll(id => (setup.History == null ? 0 : Prior(setup.History.RoleUses, id)) != history);
                pick = pool[rng.Index(pool.Count)];
            }
            seats[pick].RoleUses++; return pick;
        }
        private void Emit(LineEventType type, double time, int actor = -1, int target = -1, int a = -1, int b = -1, int recipient = -1, int extra = -1, bool host = false)
        {
            events.Add(new LineEvent { Type = type, Time = time, Actor = actor, Target = target, A = a, B = b, C = extra,
                Audience = host ? LineAudience.Host : recipient < 0 ? LineAudience.All : LineAudience.Player, AudiencePlayer = recipient });
        }
        private void Enter(LinePhase phase, double time, double duration)
        {
            Phase = phase; phaseStart = time; PhaseEndTime = time + duration; Changed();
            if (!Office) { EndAllCalls(time, CallEndReason.PhaseEnd); EndRemedials(time); }
            Emit(LineEventType.PhaseChanged, time, a: (int)phase, b: (int)endReason); endReason = PhaseEndReason.Timer;
        }
        private void StartMorning(double time)
        {
            task = null; roundIndex = -1; meeting = null; calls.Clear(); picks.Clear(); complaints.Clear(); carried = 0;
            foreach (int id in ids)
            {
                var s = seats[id]; s.TrickUsed = false; s.RawIncome = s.BlockPurchases = s.BlockRemedials = s.Texts = s.Emails = s.Complaints = s.Chats = s.Presentations = 0; s.Vote = -1;
            }
            favourite = -1;
            if (profile.HasFavourite)
            {
                int max = -1, count = 0; foreach (int id in ids) { if (seats[id].Blame > max) { max = seats[id].Blame; count = 1; } else if (seats[id].Blame == max) count++; }
                var options = ids.FindAll(id => count != 1 || seats[id].Blame != max); favourite = options[rng.Index(options.Count)];
            }
            Enter(LinePhase.Morning, time, firstWeek && Day == 0 ? c.Durations.MorningFirst : c.Durations.Morning);
            if (favourite >= 0) Emit(LineEventType.FavouriteNamed, time, target: favourite);
            Deliver(MessageKind.Manager, LineIds.Manager, -1, "", 0, -1, -1, time);
        }
        public void Tick(double time)
        {
            if (!Finite(time) || time < now) throw new ArgumentOutOfRangeException(nameof(time), "Host seconds must be finite and monotonic.");
            while (!IsFinished)
            {
                double due = PhaseEndTime;
                if (insufficientSince >= 0) due = Math.Min(due, insufficientSince + 60);
                if (Phase == LinePhase.FinalDefense && defenderIndex + 1 < meeting.DefenseOrder.Count) due = Math.Min(due, phaseStart + (defenderIndex + 1) * c.Durations.DefensePerPlayer);
                foreach (int id in ids)
                {
                    var s = seats[id]; if (s.Phone.Status == CallStatus.Dialing) due = Math.Min(due, s.Phone.RingUntil);
                    if (s.CoffeeUntil > 0) due = Math.Min(due, s.CoffeeUntil); if (s.OfflineUntil > 0) due = Math.Min(due, s.OfflineUntil);
                    if (s.DeadUntil > 0) due = Math.Min(due, s.DeadUntil); if (s.TapUntil > 0) due = Math.Min(due, s.TapUntil);
                }
                foreach (var r in remedials) due = Math.Min(due, r.ExpiresAt);
                if (due > time) break; now = due;
                if (insufficientSince >= 0 && insufficientSince + 60 <= due)
                {
                    endReason = PhaseEndReason.Abandoned; FreezeCompany(); Enter(LinePhase.Abandoned, due, 0); IsFinished = true; break;
                }
                Expire(due);
                if (due >= PhaseEndTime) Advance(due);
                else if (Phase == LinePhase.FinalDefense && phaseStart + (defenderIndex + 1) * c.Durations.DefensePerPlayer <= due) { defenderIndex++; Changed(); }
            }
            now = time;
        }
        private void Expire(double time)
        {
            foreach (int id in ids)
            {
                var s = seats[id]; if (s.Phone.Status == CallStatus.Dialing && s.Phone.RingUntil <= time) EndCall(id, time, CallEndReason.Missed);
                if (s.CoffeeUntil > 0 && s.CoffeeUntil <= time) { s.CoffeeUntil = 0; Changed(); }
                if (s.OfflineUntil > 0 && s.OfflineUntil <= time) { s.OfflineUntil = 0; Changed(); }
                if (s.DeadUntil > 0 && s.DeadUntil <= time) { s.DeadUntil = 0; Changed(); }
                if (s.TapUntil > 0 && s.TapUntil <= time) { Emit(LineEventType.Wiretap, time, target: s.TapTarget, a: 0, recipient: id); s.TapUntil = 0; s.TapTarget = -1; Changed(); }
            }
            for (int i = remedials.Count - 1; i >= 0; i--) if (remedials[i].ExpiresAt <= time) ResolveRemedial(i, false, time);
        }
        private void Advance(double time)
        {
            switch (Phase)
            {
                case LinePhase.Morning: roundIndex = 0; StartTask(time); break;
                case LinePhase.RoundIntro: task.PlayEnd = time + PlayLength(task.Kind); Enter(LinePhase.RoundPlay, time, PlayLength(task.Kind)); break;
                case LinePhase.RoundPlay:
                    if (shortened) endReason = PhaseEndReason.TimerShortened;
                    ResolveTask(time); Enter(LinePhase.RoundReveal, time, c.Durations.RoundReveal); break;
                case LinePhase.RoundReveal:
                    if (roundIndex == 0 || Day == days - 1) StartBreak(time); else { roundIndex = -1; task = null; StartDossier(time); } break;
                case LinePhase.Break:
                    carried = 0;
                    if (roundIndex == 0) { roundIndex = 1; StartTask(time); } else { roundIndex = -1; task = null; StartDossier(time); } break;
                case LinePhase.MeetingDossier: Enter(LinePhase.MeetingDiscussion, time, Day == days - 1 ? c.Durations.FinalDiscussion : c.Durations.Discussion); break;
                case LinePhase.MeetingDiscussion:
                    if (Day < days - 1) Enter(LinePhase.MeetingVote, time, c.Durations.Vote);
                    else if (closed) { FinalVerdict(time); endReason = PhaseEndReason.Skipped; Enter(LinePhase.FinalVerdict, time, c.Durations.FinalVerdict); }
                    else StartDefense(time); break;
                case LinePhase.MeetingVote: DailyVerdict(time); Enter(LinePhase.MeetingVerdict, time, c.Durations.Verdict); break;
                case LinePhase.MeetingVerdict: Day++; StartMorning(time); break;
                case LinePhase.FinalDefense: StartFinalVote(time); break;
                case LinePhase.FinalVote: FinalVerdict(time); Enter(LinePhase.FinalVerdict, time, c.Durations.FinalVerdict); break;
                case LinePhase.FinalVerdict: FinishResult(time); Enter(LinePhase.WeekResults, time, c.Durations.WeekResults); calls.Clear(); picks.Clear(); break;
                case LinePhase.WeekResults: IsFinished = true; Changed(); break;
            }
        }
        private void StartBreak(double time)
        {
            double baseline = roundIndex == 0 ? c.Durations.Break : c.Durations.BreakClosing;
            carried = (float)Math.Min(carry, Math.Max(0, c.Durations.BreakMax - baseline)); carry = 0;
            Enter(LinePhase.Break, time, baseline + carried);
        }
        public LineActionResult Apply(int player, LineAction action, double time)
        {
            Tick(time); if (action == null) return LineActionResult.Fail(0, LineFailure.BadParam);
            LineFailure fail;
            if (action.WeekId != setup.WeekId) fail = LineFailure.StaleWeek;
            else if (!HasSeat(player)) fail = LineFailure.InvalidTarget;
            else
            {
                var s = seats[player];
                if (s.HasRequest && action.RequestId <= s.LastRequest) fail = LineFailure.Duplicate;
                else
                {
                    s.HasRequest = true; s.LastRequest = action.RequestId; Changed();
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
                case LineVerb.SubmitTask: case LineVerb.GiveItem: return TaskAction(id, a, time);
                case LineVerb.UseTrick: return TrickAction(id, a, time);
                case LineVerb.SendText: case LineVerb.SendEmail: case LineVerb.SendMeetingChat: case LineVerb.FileComplaint: return MessageAction(id, a, time);
                case LineVerb.Transfer: case LineVerb.BuyEvidence: case LineVerb.PresentRecord: case LineVerb.StartRemedial: case LineVerb.RespondRemedial: return EvidenceAction(id, a, time);
                case LineVerb.PickNextManager: return PickNextManager(id, a, time);
                case LineVerb.Vote:
                    if (Phase != LinePhase.MeetingVote && Phase != LinePhase.FinalVote) return LineFailure.WrongPhase;
                    if (Phase == LinePhase.FinalVote && !meeting.Roster.Contains(id)) return LineFailure.NotAvailable;
                    if (a.Target != -1 && a.Target != LineIds.Nobody) { var f = TargetFailure(id, a.Target, false); if (f != LineFailure.None) return f; }
                    seats[id].Vote = a.Target; Emit(LineEventType.VoteCast, time, actor: id); return LineFailure.None;
                default: return LineFailure.BadParam;
            }
        }
        private LineFailure TargetFailure(int actor, int target, bool present = true)
        {
            if (!HasSeat(target)) return LineFailure.InvalidTarget; if (target == actor) return LineFailure.Self;
            return present && !seats[target].Connected ? LineFailure.Absent : LineFailure.None;
        }
        public void SetConnected(int player, bool connected, double time)
        {
            Tick(time); if (!HasSeat(player)) throw new ArgumentException("Unknown seat.");
            var s = seats[player]; if (s.Connected == connected || IsFinished) return;
            s.Connected = connected; if (connected && (Phase == LinePhase.RoundIntro || Phase == LinePhase.RoundPlay)) task.Participated[player] = true; Changed(); Emit(LineEventType.Presence, time, target: player, a: connected ? 1 : 0);
            if (!connected) { EndCall(player, time, CallEndReason.Disconnected); if (Phase != LinePhase.FinalVote) s.Vote = -1; }
            int count = 0; foreach (int id in ids) if (seats[id].Connected) count++;
            if (count < 2) { if (insufficientSince < 0) insufficientSince = time; } else insufficientSince = -1;
            if (Phase == LinePhase.RoundPlay) Shorten(time);
        }
        public List<LineEvent> DrainEvents() { var drained = events; events = new List<LineEvent>(); return drained; }
        public void HostContinue(double time) { Tick(time); if (Phase == LinePhase.WeekResults && !IsFinished) { endReason = PhaseEndReason.HostContinue; IsFinished = true; Changed(); Emit(LineEventType.PhaseChanged, time, a: (int)Phase, b: (int)endReason); } }
    }
}
