using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v4 UI mock: drives <see cref="LineClient"/> with hand-made views so screens can be built and screenshotted
    /// without a running engine or network. Launch with <c>-uimock &lt;name|prefix*|all&gt; -uimockout &lt;dir&gt;</c>;
    /// each scenario plays its steps, a screenshot is saved after each step marked <see cref="LineMockStep.Shot"/>
    /// (and after the last step), then the next scenario runs; the process quits at the end.
    /// UI tracks register scenarios from their own files with [RuntimeInitializeOnLoadMethod].
    /// The public builders below (BaseView, TaskView, RevealView, Msg, Rec, Offer, ...) produce valid v4 data
    /// (shapes follow docs/CONTRACTS_V4.md section 4) so every track can start from the same ground truth.
    /// </summary>
    public static class LineMock
    {
        static readonly SortedDictionary<string, Func<LineMockScenario>> _scenarios = new SortedDictionary<string, Func<LineMockScenario>>(StringComparer.Ordinal);

        public static void Register(string name, Func<LineMockScenario> build) { _scenarios[name] = build; }
        public static IEnumerable<string> Names => _scenarios.Keys;
        public static bool TryBuild(string name, out LineMockScenario s)
        {
            Func<LineMockScenario> f;
            s = _scenarios.TryGetValue(name, out f) ? f() : null;
            return s != null;
        }

        /// <summary>Mock host clock (seconds). Views in scenarios use times relative to it (start = 1000).</summary>
        public static double Now;

        public static readonly string[] NamesTr = { "Mavi", "Sarı", "Pembe", "Yeşil", "Turuncu", "Mor" };
        public static readonly string[] NamesEn = { "Alex", "Sam", "Jo", "Kim", "Lee", "Max" };

        static long _version = 1;
        static long _seq;
        static int _msgId = 1;
        static int _recId = 1;

        // ------------------------------------------------------------------ base view

        /// <summary>
        /// A valid mid-match v4 view with <paramref name="players"/> colleagues, Me = 0. Call inside the scenario step
        /// (so <see cref="Now"/> is the running mock clock). Office phases get RoundIndex/TaskNumber, open features and
        /// a live company productivity; tasks are not attached (use <see cref="TaskView"/>).
        /// </summary>
        public static LineView BaseView(LinePhase phase, int players = 6, int day = 1, double phaseLeft = 90)
        {
            players = Mathf.Clamp(players, 3, 6);
            var v = new LineView
            {
                WeekId = 1, StateVersion = ++_version, Me = 0, Now = Now, Phase = phase, PhaseStartTime = Now - 10, PhaseEndTime = Now + phaseLeft,
                Day = day, DayCount = 5, Length = WeekLength.Full, FirstWeek = false, ManagerProfileId = LineManagerIds.Paragoz,
                RulesetVersion = LineProtocol.RulesetVersion, MatchId = "mock",
            };
            v.TodayTasks.Add(TaskKind.InvoiceReconcile); v.TodayTasks.Add(TaskKind.CustomerSpec);
            bool inRound = phase == LinePhase.RoundIntro || phase == LinePhase.RoundPlay || phase == LinePhase.RoundReveal;
            if (inRound) { v.RoundIndex = 0; v.TaskNumber = day * 2; }
            for (int i = 0; i < players; i++)
                v.Players.Add(new LinePlayerView { Id = i, Name = NamesTr[i], ColorIndex = i, Connected = true, Blame = 5 + (i * 3) % 4, Warnings = i == 2 ? 1 : 0, SessionPoints = i });
            var f = v.Features;
            bool office = LineClient.IsOfficePhase(phase);
            f.TricksUnlocked = true; f.RecordingsEnabled = true;
            f.CallsOpen = office; f.TransfersOpen = office; f.EvidenceOpen = office; f.ComplaintsOpen = true;
            f.RemedialOpen = phase == LinePhase.Morning || phase == LinePhase.RoundReveal || phase == LinePhase.Break;
            f.IsLastDay = day == 4;
            v.Company = new LineCompanyView
            {
                ProvisionalPermille = 720, TargetPercent = 65, ResolvedOutput = 144, ResolvedMax = 200, ScheduledMax = 1000,
                TasksResolved = 2, TasksScheduled = 10, LastChange = CompanyChange.TaskResolved, LastTask = TaskKind.InvoiceReconcile, LastDelta = 74,
            };
            var m = v.Mine;
            m.Money = 4; m.BlockIncome = 3;
            m.Objective = new LineObjectiveView { Kind = ObjectiveKind.Saver, Threshold = 12, Progress = 4 };
            m.Trick = TrickType.KahveDok; m.TricksLeftThisWeek = 1;
            m.TextsLeftThisBlock = 60; m.EmailsLeftThisBlock = 8; m.ComplaintsLeftThisBlock = 1; m.MeetingChatLeft = 30;
            m.PurchasesLeftThisBlock = 1; m.PurchasesLeftThisMatch = 3; m.RemedialsLeftThisBlock = 1; m.RemedialsLeftThisMatch = 2;
            m.PresentationsLeft = 2;
            m.Offers.Add(Offer(100 + day, OfferKind.BankLedger, day, -1, TaskKind.None, 3, false, true));
            m.Offers.Add(Offer(200 + 1, OfferKind.TaskAudit, 0, 1, TaskKind.CustomerSpec, 3, false, true));
            m.Offers.Add(Offer(300 + day, OfferKind.ITLog, day, -1, TaskKind.None, 4, false, true));
            m.Offers.Add(Offer(400 + day, OfferKind.BadgeLog, day, -1, TaskKind.None, 4, true, true));
            m.Records.Add(Rec(RecordKind.Call, RecordSource.PhoneLog, 0, 1, 34, -1, -1, -1, TaskKind.None, -1));
            m.Records.Add(Rec(RecordKind.Transfer, RecordSource.Bank, 0, 3, 2, -1, -1, -1, TaskKind.None, -1));
            m.Records.Add(Rec(RecordKind.TaskEntry, RecordSource.TaskSystem, 1, 0, 48, -1, -1, 0, TaskKind.InvoiceReconcile, -1));
            m.Records.Add(Rec(RecordKind.TaskOutcome, RecordSource.TaskSystem, LineIds.None, LineIds.None, 74, 100, -1, 0, TaskKind.InvoiceReconcile, -1));
            return v;
        }

        /// <summary>Applies Turkish or English colleague names.</summary>
        public static void SetNames(LineView v, bool en)
        {
            var names = en ? NamesEn : NamesTr;
            for (int i = 0; i < v.Players.Count; i++) v.Players[i].Name = names[i];
        }

        // ------------------------------------------------------------------ data builders

        public static LineFact Fact(string key, int scope = 0, int p = LineIds.None, int n1 = -1, int n2 = -1, int n3 = -1)
        {
            return new LineFact { Key = key, Scope = scope, P = p, N1 = n1, N2 = n2, N3 = n3 };
        }

        public static LineOption Opt(string key, int value, bool sabotage = false, int n1 = -1, int n2 = -1, int p = LineIds.None)
        {
            return new LineOption { Key = key, Value = value, N1 = n1, N2 = n2, P = p, Sabotage = sabotage };
        }

        public static LineControlView Control(ControlKind kind, TaskField field, string key, bool required = true, params LineOption[] options)
        {
            var c = new LineControlView { Kind = kind, Field = field, Key = key, Required = required };
            c.Options.AddRange(options);
            return c;
        }

        public static LineOfferView Offer(int id, OfferKind kind, int day, int taskNumber, TaskKind task, int price, bool purchased, bool available)
        {
            return new LineOfferView { Id = id, Kind = kind, Day = day, TaskNumber = taskNumber, Task = task, Price = price, Purchased = purchased, Available = available };
        }

        public static LineRecordView Rec(RecordKind kind, RecordSource source, int actor, int subject, int n1, int n2, int n3, int taskNumber, TaskKind task, int proofOf, bool presented = false, double ago = 20)
        {
            return new LineRecordView
            {
                Id = _recId++, Kind = kind, Source = source, Time = Now - ago, Day = 1, TaskNumber = taskNumber, Task = task,
                Actor = actor, Subject = subject, N1 = n1, N2 = n2, N3 = n3, ProofOf = proofOf, Presented = presented,
            };
        }

        /// <summary>A message with the next sequence number. Apply with <c>LineClient.I.ApplyMessages(weekId, list)</c>.</summary>
        public static LineMessage Msg(MessageKind kind, int from, int to, string text, int replyTo = -1, int thread = -1, bool toAll = false, double ago = 30, int template = -1)
        {
            int id = _msgId++;
            return new LineMessage
            {
                Seq = ++_seq, Id = id, Kind = kind, ThreadId = thread >= 0 ? thread : id, ReplyTo = replyTo, From = from, To = toAll ? LineIds.None : to, ToAll = toAll,
                Text = text, TemplateId = template, Time = Now - ago, Day = 1,
            };
        }

        /// <summary>Marks the view as knowing about the given messages (no resync request in mocks).</summary>
        public static void SyncMessageSeq(LineView v) { v.Mine.MessageSeq = _seq; }

        // ------------------------------------------------------------------ task builders (CONTRACTS_V4 section 4)

        /// <summary>The partner (or first group member) a pair task would give player 0 at this head count.</summary>
        public static int PartnerOf(int players) { return players > 3 ? 3 : 1; }

        /// <summary>
        /// A play-state task view for player 0 with realistic facts and controls. role: T6 1 visitor / 2 requester,
        /// T8 1 offered, T9 1 insider, T11 1 insider.
        /// </summary>
        public static LineTaskView TaskView(TaskKind kind, int players = 4, int role = 0, bool submitted = false)
        {
            players = Mathf.Clamp(players, 3, 6);
            var t = new LineTaskView { Kind = kind, Version = 1, Family = LineTasks.Family(kind), Index = 0, Number = 0, PlayEndTime = Now + 60, Role = role };
            int partner = PartnerOf(players);
            var F = t.Facts; var P = t.PublicFacts; var C = t.Controls;
            switch (kind)
            {
                case TaskKind.InvoiceReconcile:
                    AddGroup(t, players, partner);
                    F.Add(Fact("line", 0, n1: 1, n2: 7)); F.Add(Fact("line", 0, n1: 2, n2: 4)); F.Add(Fact("partner", 1, p: partner));
                    C.Add(Number("total", TaskField.A, 0, 99, 0, true));
                    C.Add(Control(ControlKind.Choice, TaskField.B, "pad", false, Opt("no", 0), Opt("yes", 1, true)));
                    C[1].Sabotage = true;
                    break;
                case TaskKind.BudgetSplit:
                    AllOthers(t, players);
                    int b = 2 * players + 2;
                    F.Add(Fact("need", n1: 3)); P.Add(Fact("budget", 2, n1: b, n2: players));
                    C.Add(Number("request", TaskField.A, 0, b, 0, true));
                    break;
                case TaskKind.CustomerSpec:
                    AddGroup(t, players, partner);
                    F.Add(Fact("color", n1: 1)); F.Add(Fact("size", n1: 2)); F.Add(Fact("commission", n1: 3)); F.Add(Fact("partner", 1, p: partner));
                    var order = Control(ControlKind.Choice, TaskField.A, "order", true);
                    int[] specs = { 1 * 9 + 2 * 3 + 0, 1 * 9 + 2 * 3 + 1, 1 * 9 + 0 * 3 + 2, 0 * 9 + 2 * 3 + 2, 2 * 9 + 1 * 3 + 0, 2 * 9 + 2 * 3 + 1 };
                    for (int i = 0; i < 6; i++) { P.Add(Fact("spec", 2, n1: i, n2: specs[i])); order.Options.Add(Opt("spec", i, false, specs[i])); }
                    C.Add(order);
                    break;
                case TaskKind.ReportReview:
                    t.Group.Add(Mathf.Min(1, players - 1)); t.Group.Add(Mathf.Min(2, players - 1));
                    F.Add(Fact("my_figure", n1: 60, n2: 72)); F.Add(Fact("review_figure", 1, p: t.Group[0], n1: submitted ? 48 : -1)); F.Add(Fact("audit_figure", 0, p: t.Group[1], n1: 64));
                    C.Add(Control(ControlKind.Choice, TaskField.A, "version", true, Opt("accurate", 0), Opt("polished", 1, true)));
                    C[0].Locks = true; C[0].Sabotage = true;
                    C.Add(Control(ControlKind.Choice, TaskField.B, "decision", true, Opt("approve", 0), Opt("reject", 1)));
                    C[1].WaitingFor = submitted ? LineIds.None : t.Group[0];
                    break;
                case TaskKind.RoomBooking:
                    AllOthers(t, players);
                    int s = players - 1;
                    F.Add(Fact("value", n1: 4)); F.Add(Fact("ok_slot", n1: 1)); F.Add(Fact("ok_slot", n1: 2)); P.Add(Fact("slots", 2, n1: s));
                    var slot = Control(ControlKind.Choice, TaskField.A, "slot", true);
                    for (int i = 0; i < s; i++) slot.Options.Add(Opt("slot", i, false, i));
                    slot.Options.Add(Opt("yield", 9));
                    C.Add(slot);
                    break;
                case TaskKind.ArchiveVisitor:
                    int visitor = role == 1 ? 0 : Mathf.Min(2, players - 1);
                    int k = Mathf.Max(1, players - 2);
                    P.Add(Fact("visitor", 2, p: visitor)); P.Add(Fact("capacity", 2, n1: k));
                    if (role == 1)
                    {
                        for (int i = 1; i < players; i++) t.Group.Add(i);
                        int[] titles = { 3, 7, 1, 9, 4, 0 };
                        var fetch = new LineControlView { Kind = ControlKind.Multi, Field = TaskField.A, Key = "fetch", Required = true, MaxPicks = k };
                        for (int i = 0; i < 6; i++) { F.Add(Fact("doc", n1: i, n2: titles[i])); fetch.Options.Add(Opt("doc", i, false, i, titles[i])); }
                        F.Add(Fact("hr_file", n1: 5, p: Mathf.Min(1, players - 1)));
                        C.Add(fetch);
                        C.Add(Control(ControlKind.Choice, TaskField.B, "copy", false, Opt("no", 0), Opt("yes", 1, true)));
                        C[1].Sabotage = true;
                    }
                    else
                    {
                        t.Role = 2; t.Group.Add(visitor);
                        F.Add(Fact("need_doc", n1: 7));
                        var confirm = Control(ControlKind.Choice, TaskField.A, "confirm", true);
                        int[] ts = { 3, 7, 1, 9, 4, 0 };
                        for (int i = 0; i < 6; i++) confirm.Options.Add(Opt("doc", i, false, i, ts[i]));
                        C.Add(confirm);
                    }
                    break;
                case TaskKind.BonusPool:
                    AddGroup(t, players, partner);
                    F.Add(Fact("partner", 1, p: partner));
                    C.Add(Control(ControlKind.Choice, TaskField.A, "choice", true, Opt("share", 0), Opt("take", 1)));
                    break;
                case TaskKind.ServerCode:
                    AllOthers(t, players);
                    for (int i = 0; i < players; i++) P.Add(Fact("position", 2, p: i, n1: (i + 2) % players));
                    F.Add(Fact("my_digit", n1: 2, n2: 7)); F.Add(Fact("next_digit", n1: 3, n2: 1));
                    if (role == 1) F.Add(Fact("offer", 0, n1: 4));
                    C.Add(Number("code", TaskField.A, 0, (int)Mathf.Pow(10, players) - 1, players, true));
                    if (role == 1) { C.Add(Control(ControlKind.Choice, TaskField.B, "accept", false, Opt("no", 0), Opt("yes", 1, true))); C[1].Sabotage = true; }
                    break;
                case TaskKind.PhishingCheck:
                    AllOthers(t, players);
                    F.Add(Fact("account", n1: 4412));
                    if (role == 1) F.Add(Fact("insider_offer", 0, n1: 8035, n2: 4));
                    C.Add(new LineControlView { Kind = ControlKind.Player, Field = TaskField.Target, Key = "flag", Required = true, AllowSelf = true });
                    if (role == 1) { C.Add(Control(ControlKind.Choice, TaskField.B, "accept", false, Opt("no", 0), Opt("yes", 1, true))); C[1].Sabotage = true; }
                    break;
                case TaskKind.ShiftCover:
                    AllOthers(t, players);
                    int ks = (players + 1) / 2;
                    P.Add(Fact("shifts", 2, n1: ks)); F.Add(Fact("available", n1: 0)); F.Add(Fact("available", n1: ks - 1)); F.Add(Fact("cost", n1: 1));
                    var vol = Control(ControlKind.Choice, TaskField.A, "volunteer", true);
                    for (int i = 0; i < ks; i++) vol.Options.Add(Opt("shift", i, false, i));
                    vol.Options.Add(Opt("decline", 9));
                    C.Add(vol);
                    break;
                case TaskKind.VendorPick:
                    AllOthers(t, players);
                    F.Add(Fact("tested", n1: 2, n2: 7));
                    if (role == 1) F.Add(Fact("kickback", 0, n1: 0, n2: 3));
                    var vote = Control(ControlKind.Choice, TaskField.A, "vote", true);
                    for (int i = 0; i < 4; i++) vote.Options.Add(Opt("vendor", i, false, i));
                    C.Add(vote);
                    if (role == 1) { C.Add(Control(ControlKind.Choice, TaskField.B, "accept", false, Opt("no", 0), Opt("yes", 1, true))); C[1].Sabotage = true; }
                    break;
                case TaskKind.SupplySwap:
                    AllOthers(t, players);
                    F.Add(Fact("need", n1: 2)); F.Add(Fact("need", n1: 4)); F.Add(Fact("collector", n1: 1));
                    t.Items.Add(0); t.Items.Add(3);
                    C.Add(new LineControlView { Kind = ControlKind.Items, Field = TaskField.None, Key = "give", Required = false });
                    C.Add(Control(ControlKind.Choice, TaskField.A, "done", false, Opt("done", 1)));
                    break;
            }
            t.Submitted = submitted;
            if (submitted) { t.SubmittedFields = TaskField.A; t.MyA = 1; }
            return t;
        }

        static LineControlView Number(string key, TaskField field, int min, int max, int digits, bool required)
        {
            return new LineControlView { Kind = ControlKind.Number, Field = field, Key = key, Min = min, Max = max, Digits = digits, Required = required };
        }

        static void AddGroup(LineTaskView t, int players, int partner)
        {
            t.Group.Add(partner);
            if (players == 3) t.Group.Add(partner == 1 ? 2 : 1);
        }

        static void AllOthers(LineTaskView t, int players) { for (int i = 1; i < players; i++) t.Group.Add(i); }

        /// <summary>A public reveal for a resolved task (output, public facts, ballots where the task has them).</summary>
        public static LineTaskReveal RevealView(TaskKind kind, int players = 4, int output = 70, bool withMissed = false)
        {
            var r = new LineTaskReveal { Kind = kind, Index = 0, Number = 0, Output = output, MaxOutput = 100 };
            var F = r.PublicFacts;
            switch (kind)
            {
                case TaskKind.InvoiceReconcile: F.Add(Fact("correct_entries", 2, n1: 2, n2: players)); F.Add(Fact("groups_correct", 2, n1: 1, n2: (players + 1) / 2)); break;
                case TaskKind.BudgetSplit:
                    F.Add(Fact("overflow", 2, n1: 15, n2: 2 * players + 2));
                    for (int i = 0; i < players; i++) { F.Add(Fact("grant", 2, p: i, n1: 3 + i % 2, n2: 3, n3: i % 3 == 2 ? 0 : 1)); r.Votes.Add(new LineVoteView { Voter = i, Target = 3 + i % 2 }); }
                    break;
                case TaskKind.CustomerSpec: F.Add(Fact("correct_orders", 2, n1: 2, n2: players)); F.Add(Fact("true_spec", 2, n1: 1 * 9 + 2 * 3 + 1)); break;
                case TaskKind.ReportReview: F.Add(Fact("reports_approved", 2, n1: players - 1, n2: players)); break;
                case TaskKind.RoomBooking:
                    for (int i = 0; i < players; i++) { int slot = i == 3 ? 9 : i % (players - 1); F.Add(Fact("booking", 2, p: i, n1: slot, n2: i == 1 ? 0 : 1)); r.Votes.Add(new LineVoteView { Voter = i, Target = slot }); }
                    break;
                case TaskKind.ArchiveVisitor: F.Add(Fact("served", 2, n1: Mathf.Max(1, players - 2) - 1, n2: Mathf.Max(1, players - 2))); break;
                case TaskKind.BonusPool: F.Add(Fact("takes", 2, n1: 1)); break;
                case TaskKind.ServerCode: F.Add(Fact("cracked", 2, n1: 1, n2: players - 1)); break;
                case TaskKind.PhishingCheck:
                    F.Add(Fact("fake_holder", 2, p: 2)); F.Add(Fact("blocked", 2, n1: 1));
                    for (int i = 0; i < players; i++) r.Votes.Add(new LineVoteView { Voter = i, Target = i % 2 == 0 ? 2 : 1 });
                    break;
                case TaskKind.ShiftCover:
                    F.Add(Fact("covered", 2, n1: 1, n2: (players + 1) / 2));
                    for (int i = 0; i < players; i++) r.Votes.Add(new LineVoteView { Voter = i, Target = i == 1 ? 0 : 9 });
                    break;
                case TaskKind.VendorPick:
                    F.Add(Fact("winner", 2, n1: 2, n2: 7));
                    for (int i = 0; i < 4; i++) F.Add(Fact("quality", 2, n1: i, n2: new[] { 3, 5, 7, 9 }[i]));
                    for (int i = 0; i < players; i++) r.Votes.Add(new LineVoteView { Voter = i, Target = i % 3 });
                    break;
                case TaskKind.SupplySwap: F.Add(Fact("completed", 2, n1: players - 1, n2: players)); break;
            }
            if (withMissed) { r.Missed.Add(players - 1); r.Deltas.Add(new LineBlameDelta { Player = 0, Delta = 1, Reason = BlameReason.MissedTask, Task = kind, Day = 1 }); }
            return r;
        }

        /// <summary>Public ballot values of a task reveal use these option/value rules: see DeskReveal.</summary>
        public static LineView WithTask(LineView v, TaskKind kind, int role = 0, bool submitted = false)
        {
            v.TodayTasks[0] = kind;
            v.Task = TaskView(kind, v.Players.Count, role, submitted);
            v.Task.PlayEndTime = v.PhaseEndTime;
            if (v.RoundIndex < 0) { v.RoundIndex = 0; v.TaskNumber = v.Day * 2; }
            v.Task.Number = v.TaskNumber; v.Task.Index = v.RoundIndex;
            return v;
        }
    }

    public sealed class LineMockScenario
    {
        public int Me;
        public LineConfig Config = LineConfig.CreateDefault();
        public readonly List<LineMockStep> Steps = new List<LineMockStep>();
        /// <summary>Optional: called for every request the UI sends (log it, or mutate the next view).</summary>
        public Action<LineAction> OnSend;

        public LineMockScenario Step(float atSeconds, LineView view, bool shot = false, params LineEvent[] events)
        {
            Steps.Add(new LineMockStep { At = atSeconds, View = view, Shot = shot, Events = new List<LineEvent>(events) });
            return this;
        }

        public LineMockScenario Do(float atSeconds, Action action, bool shot = false)
        {
            Steps.Add(new LineMockStep { At = atSeconds, Action = action, Shot = shot });
            return this;
        }
    }

    public sealed class LineMockStep
    {
        public float At;
        public LineView View;
        public List<LineEvent> Events;
        public Action Action;
        public bool Shot;
    }

    /// <summary>Added by <see cref="GameBootstrap"/> when <c>-uimock</c> is present.</summary>
    public sealed class LineMockDriver : MonoBehaviour
    {
        public static bool Active { get; private set; }
        string _pattern = "", _outDir = "";

        void Awake()
        {
            Active = true;
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++)
            {
                if (a[i] == "-uimock") _pattern = a[i + 1];
                if (a[i] == "-uimockout") _outDir = a[i + 1];
            }
            if (string.IsNullOrEmpty(_outDir)) _outDir = Path.Combine(Application.persistentDataPath, "uimock");
            _outDir = Path.GetFullPath(_outDir);
            Directory.CreateDirectory(_outDir);
            double start = Time.realtimeSinceStartupAsDouble;
            LineClient.Clock = () => 1000.0 + (Time.realtimeSinceStartupAsDouble - start);
        }

        IEnumerator Start()
        {
            yield return null;
            yield return null; // UIRoot builds its screens in Start
            var names = new List<string>();
            foreach (var n in LineMock.Names)
                if (_pattern == "all" || n == _pattern || (_pattern.EndsWith("*") && n.StartsWith(_pattern.Substring(0, _pattern.Length - 1), StringComparison.Ordinal)))
                    names.Add(n);
            if (names.Count == 0) Debug.LogWarning("[LineMock] no scenario matches '" + _pattern + "'. Known: " + string.Join(", ", LineMock.Names));
            foreach (var name in names)
            {
                LineMockScenario s;
                if (!LineMock.TryBuild(name, out s)) continue;
                yield return Run(name, s);
            }
            Debug.Log("[LineMock] done: " + names.Count + " scenario(s) → " + _outDir);
            yield return new WaitForSecondsRealtime(0.3f);
            Application.Quit();
        }

        IEnumerator Run(string name, LineMockScenario s)
        {
            var client = LineClient.I;
            client.ResetSession(); // also drops the previous scenario's messages
            client.ClearWeek();
            client.SetIdentity(s.Me, s.Config);
            LineClient.Transport = req => { MatchLog.Write("[mock] send " + req); s.OnSend?.Invoke(req); };
            float t0 = Time.realtimeSinceStartup;
            int shot = 0;
            for (int i = 0; i < s.Steps.Count; i++)
            {
                var st = s.Steps[i];
                while (Time.realtimeSinceStartup - t0 < st.At) yield return null;
                LineMock.Now = client.Now;
                st.Action?.Invoke();
                if (st.View != null)
                {
                    st.View.Now = client.Now;
                    client.ApplyView(st.View);
                }
                if (st.Events != null && st.Events.Count > 0) client.ApplyEvents(st.Events);
                if (st.Shot || i == s.Steps.Count - 1)
                {
                    yield return new WaitForSecondsRealtime(1.2f); // let entrance motion settle
                    yield return new WaitForEndOfFrame();
                    string file = Path.Combine(_outDir, name + "_" + (shot++).ToString("00") + ".png");
                    ScreenCapture.CaptureScreenshot(file);
                    yield return null;
                }
            }
            yield return new WaitForSecondsRealtime(0.3f);
        }
    }
}
