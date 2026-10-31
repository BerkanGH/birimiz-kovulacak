using System;
using System.Collections.Generic;
using System.Linq;
using BirimizKovulacak.Core;

namespace BirimizKovulacak.Telemetry.Tests
{
    /// <summary>Collects every batch it receives (kind Local: routed like the JSONL sink).</summary>
    public sealed class CaptureSink : ITelemetrySink
    {
        public readonly List<TelemetryEvent> Events = new List<TelemetryEvent>();
        public bool Throw;
        public int WriteCalls;
        public string Name { get { return "local_jsonl"; } }
        public TelemetrySinkKind Kind { get; set; }
        public CaptureSink(TelemetrySinkKind kind = TelemetrySinkKind.Local) { Kind = kind; }
        public void Write(IReadOnlyList<TelemetryEvent> batch)
        {
            WriteCalls++;
            if (Throw) throw new System.IO.IOException("disk full");
            Events.AddRange(batch);
        }
        public void Flush() { }
        public void Dispose() { }
        public IEnumerable<TelemetryEvent> Named(string name) { return Events.Where(e => e.Name == name); }
        public int Count(string name) { return Events.Count(e => e.Name == name); }
    }

    public sealed class Harness
    {
        public readonly GameTelemetryService Service;
        public readonly CaptureSink Sink = new CaptureSink();
        public double Clock = 100;
        public ulong Seed = 1;
        public TelemetryConfiguration Config;

        public Harness(Action<TelemetryConfiguration> tweak = null, int localConsent = 3, bool host = false)
        {
            Config = new TelemetryConfiguration { Environment = "development", Cloud = true };
            if (tweak != null) tweak(Config);
            Service = new GameTelemetryService(Config, new TelemetrySessionContext(), () => Clock, () => new DateTime(2026, 11, 1, 20, 0, 0, DateTimeKind.Utc).AddSeconds(Clock), () => Seed++ * 0x9E3779B97F4A7C15UL, Sink);
            Service.HostMode = host;
            Service.SetLocalConsent(localConsent);
        }

        public List<TelemetryEvent> Flushed()
        {
            Service.Flush();
            return Sink.Events;
        }
    }

    /// <summary>Hand-scripted engine: views and state are set by the test, nothing is simulated.</summary>
    public sealed class FakeEngine : ILineEngine
    {
        public LineConfig Config { get; set; }
        public LineSetup Setup { get; set; }
        public LinePhase Phase { get; set; }
        public double PhaseEndTime { get; set; }
        public int Day { get; set; }
        public long StateVersion { get; set; }
        public bool IsFinished { get; set; }
        public LineWeekResult Result { get; set; }
        public List<LineRecordingPick> Picks = new List<LineRecordingPick>();
        public IReadOnlyList<LineRecordingPick> RecordingPicks { get { return Picks; } }
        public Func<int, double, LineView> ViewFn;
        public int ViewCalls;

        public FakeEngine(int players = 4, string matchId = "m-test0001")
        {
            Config = LineConfig.CreateDefault();
            Setup = new LineSetup { WeekId = 1, MatchId = matchId, Length = WeekLength.Short };
            for (int i = 0; i < players; i++) Setup.Players.Add(new LinePlayerInfo { Id = i, Name = "P" + i });
            ViewFn = (p, now) => DefaultView(p, now);
        }

        public int RoundIndex = -1, TaskNumber = -1;
        public List<TaskKind> Today = new List<TaskKind> { TaskKind.InvoiceReconcile, TaskKind.ServerCode };
        public Dictionary<int, List<int>> Groups = new Dictionary<int, List<int>>();
        public Dictionary<int, int> Votes = new Dictionary<int, int>();
        public bool[] Disconnected = new bool[8];
        public int[] Blame = { 5, 5, 5, 5, 5, 5 };
        public int Defender = LineIds.None;
        public LineMeetingView Meeting;
        public LineCompanyView Company = new LineCompanyView { TasksScheduled = 6, ProvisionalPermille = 700, TargetPercent = 65 };
        public List<LineTaskReveal> Reveals = new List<LineTaskReveal>();

        public LineView DefaultView(int player, double now)
        {
            ViewCalls++;
            var v = new LineView { WeekId = 1, MatchId = Setup.MatchId, Me = player, Now = now, Phase = Phase, PhaseEndTime = PhaseEndTime, Day = Day, DayCount = 3, RoundIndex = RoundIndex, TaskNumber = TaskNumber, Company = Company, Meeting = Meeting };
            v.TodayTasks = Today;
            v.TodayReveals = Reveals;
            v.Features = new LineFeaturesView { FirstTimeTask = true };
            foreach (var p in Setup.Players)
                v.Players.Add(new LinePlayerView { Id = p.Id, Connected = !Disconnected[p.Id], IsBot = p.IsBot, Blame = Blame[p.Id], Voted = Votes.ContainsKey(p.Id) });
            if (RoundIndex >= 0 && Phase >= LinePhase.RoundIntro && Phase <= LinePhase.Break)
            {
                var g = new List<int>();
                List<int> gl;
                if (Groups.TryGetValue(player, out gl)) g = gl;
                v.Task = new LineTaskView { Kind = Today[RoundIndex % Today.Count], Version = 1, Family = TaskFamily.Pair, Number = TaskNumber, Group = g, Role = 0, PlayEndTime = PhaseEndTime };
                v.Task.Facts.Add(new LineFact { Key = "line" });
                v.Task.Facts.Add(new LineFact { Key = "partner" });
            }
            v.Mine.Objective = new LineObjectiveView { Kind = (ObjectiveKind)(1 + player % 9) };
            int mv;
            if (Meeting != null) { var m = Meeting; m.MyVote = Votes.TryGetValue(player, out mv) ? mv : LineIds.None; m.Defender = Defender; }
            v.Result = Result;
            return v;
        }

        /// <summary>Like the real engine: time never goes backwards (BuildView ticks too).</summary>
        public double LastTick = double.MinValue;
        /// <summary>Telemetry must never advance the engine beyond the host's frame time.</summary>
        public double FrameTime = double.MaxValue;
        public void Tick(double now)
        {
            if (now < LastTick) throw new System.ArgumentOutOfRangeException(nameof(now), "Host time must be finite and monotonic.");
            if (now > FrameTime) throw new System.InvalidOperationException("telemetry advanced the engine past the host frame time");
            LastTick = now;
        }
        public LineActionResult Apply(int player, LineAction action, double now) { throw new NotSupportedException(); }
        public void SetConnected(int player, bool connected, double now) { Disconnected[player] = !connected; }
        public LineView BuildView(int player, double now) { Tick(now); return ViewFn(player, now); }
        public List<LineEvent> DrainEvents() { return new List<LineEvent>(); }
        public LineSessionHistory NextHistory() { return new LineSessionHistory(); }
        public List<LineMessage> MessagesFor(int player, long afterSeq) { return new List<LineMessage>(); }
        public int ConnectedCallId(int player) { return -1; }
        public int CallPartner(int player) { return -1; }
        public int TappersOf(int speaker, int[] buffer) { return 0; }
        public bool MaySpeak(int player) { return true; }
        public void ReportRecording(int pickIndex, int chosenCallId, double now) { }
        public void HostContinue(double now) { }
    }

    public static class Ev
    {
        public static LineEvent Phase(double t, LinePhase to, PhaseEndReason reason = PhaseEndReason.Timer)
        {
            return new LineEvent { Type = LineEventType.PhaseChanged, Time = t, A = (int)to, B = (int)reason };
        }
        public static LineEvent Incoming(double t, int caller, int callee) { return new LineEvent { Type = LineEventType.CallIncoming, Time = t, Actor = caller, Audience = LineAudience.Player, AudiencePlayer = callee }; }
        public static LineEvent Connected(double t, int caller, int callee, int id, int audience) { return new LineEvent { Type = LineEventType.CallConnected, Time = t, Actor = caller, Target = callee, A = id, Audience = LineAudience.Player, AudiencePlayer = audience }; }
        public static LineEvent Ended(double t, int audience, int other, CallEndReason r, int id) { return new LineEvent { Type = LineEventType.CallEnded, Time = t, Actor = other, A = (int)r, B = id, Audience = LineAudience.Player, AudiencePlayer = audience }; }
        public static LineEvent Busy(double t, int caller, int callee) { return new LineEvent { Type = LineEventType.CallBusy, Time = t, Target = callee, Audience = LineAudience.Player, AudiencePlayer = caller }; }
        public static LineEvent Host(LineEventType type, double t, int actor = -1, int target = -1, int a = -1, int b = -1, int c = -1)
        {
            return new LineEvent { Type = type, Time = t, Actor = actor, Target = target, A = a, B = b, C = c, Audience = LineAudience.Host };
        }
        public static LineEvent Pub(LineEventType type, double t, int actor = -1, int target = -1, int a = -1, int b = -1, int c = -1)
        {
            return new LineEvent { Type = type, Time = t, Actor = actor, Target = target, A = a, B = b, C = c };
        }
    }

    /// <summary>A started match with all seats consenting at level 3.</summary>
    public sealed class MatchRig
    {
        public readonly Harness H;
        public readonly FakeEngine Engine;
        public readonly HostTelemetryCoordinator Host;
        public LineTelemetryObserver Obs { get { return Host.Observer; } }
        public double Now = 1000;

        public MatchRig(int players = 4, int consent = 3, Action<TelemetryConfiguration> tweak = null, int[] consents = null, bool bots = false)
        {
            H = new Harness(tweak, 3, true);
            Engine = new FakeEngine(players);
            Host = new HostTelemetryCoordinator(H.Service, () => H.Clock, () => 77);
            Host.LobbyCreated("s-lobby0001", "G07", WeekLength.Short, true);
            for (int i = 0; i < players; i++)
            {
                int c = consents != null ? consents[i] : consent;
                Host.PlayerConsent(i, c, c >= 2 ? "p-" + (0xA0000000UL + (ulong)i).ToString("x16") : "", false);
            }
            Engine.Phase = LinePhase.Morning;
            Engine.PhaseEndTime = Now + 12;
            Host.MatchStarted(Engine, Engine.Setup, new[] { "rec_select=context" }, Now);
        }

        public void Feed(params LineEvent[] events)
        {
            Host.EngineEvents(Engine, new List<LineEvent>(events), Now);
        }

        /// <summary>Moves to a phase: sets the engine state then feeds the PhaseChanged event.</summary>
        public void Go(double t, LinePhase to, double planned, PhaseEndReason reason = PhaseEndReason.Timer, int roundIndex = -2, int taskNumber = -2, int day = -1)
        {
            Now = t;
            Engine.Phase = to;
            Engine.PhaseEndTime = t + planned;
            if (roundIndex != -2) Engine.RoundIndex = roundIndex;
            if (taskNumber != -2) Engine.TaskNumber = taskNumber;
            if (day >= 0) Engine.Day = day;
            Feed(Ev.Phase(t, to, reason));
        }

        public List<TelemetryEvent> Events { get { return H.Flushed(); } }
        public IEnumerable<TelemetryEvent> Named(string n) { return Events.Where(e => e.Name == n); }
    }
}
