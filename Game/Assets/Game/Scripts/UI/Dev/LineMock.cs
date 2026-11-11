using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// UI mock: drives <see cref="LineClient"/> with hand-made views so screens can be built and screenshotted
    /// without a running engine or network. Launch with <c>-uimock &lt;name|prefix*|all&gt; -uimockout &lt;dir&gt;</c>;
    /// each scenario plays its steps, a screenshot is saved after each step marked <see cref="LineMockStep.Shot"/>
    /// (and after the last step), then the next scenario runs; the process quits at the end.
    /// UI tracks register scenarios from their own files with [RuntimeInitializeOnLoadMethod].
    /// The builders below produce valid v5 views (docs/V5_ENGINE_SPEC.md §11); the "v5.*" scenarios at the end cover
    /// the play screen phase by phase, the how-to pages and two English samples.
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

        // ------------------------------------------------------------------ base view

        /// <summary>
        /// A valid mid-match v5 view with <paramref name="players"/> colleagues, Me = 0. Call inside the scenario step
        /// (so <see cref="Now"/> is the running mock clock). Features follow the phase; no task, meeting or final is
        /// attached (see <see cref="TaskView"/>, <see cref="MeetingView"/>, <see cref="FinalView"/>).
        /// </summary>
        public static LineView BaseView(LinePhase phase, int players = 4, int day = 0, double phaseLeft = 40)
        {
            players = Mathf.Clamp(players, LineProtocol.MinPlayers, LineProtocol.MaxPlayers);
            var v = new LineView
            {
                WeekId = 1, StateVersion = ++_version, Me = 0, Now = Now, Phase = phase, PhaseStartTime = Now - 10, PhaseEndTime = Now + phaseLeft,
                Day = day, RulesetVersion = LineProtocol.RulesetVersion, MatchId = "mock",
            };
            for (int i = 0; i < players; i++)
                v.Players.Add(new LinePlayerView { Id = i, Name = NamesTr[i], ColorIndex = i, Connected = true, IsBot = i >= 2, Blame = (i * 3) % 5 });
            var f = v.Features;
            bool calls = LineClient.IsCallsPhase(phase);
            f.RecordingsEnabled = true;
            f.CallsOpen = calls;
            f.OpenVoice = !calls;
            f.TransfersOpen = phase >= LinePhase.Intro && phase <= LinePhase.FinalVote;
            f.TextsOpen = phase >= LinePhase.Intro && phase <= LinePhase.FinalVerdict;
            f.MeetingChatOpen = LineClient.IsMeetingPhase(phase);
            f.LeakOpen = phase == LinePhase.MeetingDossier || phase == LinePhase.MeetingDiscussion || phase == LinePhase.FinalDiscussion;
            f.InvestigationOpen = day == 0 && (phase == LinePhase.MeetingDossier || phase == LinePhase.MeetingDiscussion);
            f.VoteOpen = phase == LinePhase.MeetingVote || phase == LinePhase.CrisisVote || phase == LinePhase.FinalVote;
            var m = v.Mine;
            m.Prim = 9;
            m.RecorderUsesLeft = 1;
            m.TextsLeft = 58;
            m.MeetingChatLeft = 30;
            m.Transfers.Add(new LineTransferView { From = 1, To = 0, Amount = 2, Time = Now - 50, Phase = LinePhase.Break });
            m.CallLog.Add(new LineCallRecordView { CallId = 3, Other = 1, Outgoing = true, Connected = true, Start = Now - 120, End = Now - 75, EndReason = CallEndReason.HungUp });
            m.CallLog.Add(new LineCallRecordView { CallId = 4, Other = 2, Outgoing = false, Connected = false, Start = Now - 60, End = Now - 48, EndReason = CallEndReason.Missed });
            if (calls) v.LineBoard.Add(new LineBoardCall { CallId = 7, Caller = 1, Callee = 3 % players, Status = CallStatus.Connected, Start = Now - 20 });
            return v;
        }

        /// <summary>Applies Turkish or English colleague names.</summary>
        public static void SetNames(LineView v, bool en)
        {
            var names = en ? NamesEn : NamesTr;
            for (int i = 0; i < v.Players.Count; i++) v.Players[i].Name = names[i];
        }

        // ------------------------------------------------------------------ data builders

        /// <summary>A message with the next sequence number. Apply with <c>LineClient.I.ApplyMessages(weekId, list)</c>.</summary>
        public static LineMessage Msg(MessageKind kind, int from, int to, string text, bool toAll = false, double ago = 30)
        {
            return new LineMessage { Seq = ++_seq, Id = _msgId++, Kind = kind, From = from, To = toAll ? LineIds.None : to, ToAll = toAll, Text = text, Time = Now - ago };
        }

        /// <summary>Marks the view as knowing about the given messages (no resync request in mocks).</summary>
        public static void SyncMessageSeq(LineView v) { v.Mine.MessageSeq = _seq; }

        /// <summary>Player 0's task view (pair with player 1, or everyone for Weekend Shift).</summary>
        public static LineTaskView TaskView(TaskKind kind, int number, int players, bool revealed, int myChoice = 0, bool submitted = true)
        {
            var t = new LineTaskView { Kind = kind, Number = number, Family = LineTasks.Family(kind), PlayEndTime = Now + 30, Submitted = submitted, MyChoice = myChoice, Revealed = revealed };
            if (kind == TaskKind.WeekendShift) { for (int i = 0; i < players; i++) t.Group.Add(i); t.Needed = LineConfig.ShiftNeeded(players); }
            else { t.Group.Add(0); t.Group.Add(1); if (players == 3) t.Group.Add(2); }
            if (!revealed) return t;
            foreach (int id in t.Group)
            {
                int choice = id == 0 ? myChoice : id == 1 ? LineChoice.Take : 0;
                t.Choices.Add(new LineChoiceView { TaskNumber = number, Task = kind, Player = id, Choice = choice, Payout = id == 0 ? (choice == 1 ? 5 : 0) : -1 });
                if (kind == TaskKind.WeekendShift && choice == LineChoice.Volunteer) t.Roster.Add(id);
            }
            t.MyPayout = myChoice == 1 ? 5 : 0;
            if (kind == TaskKind.ClientBonus) t.Takers = 2;
            return t;
        }

        public static LineMeetingView MeetingView(int index, int voteable)
        {
            var mt = new LineMeetingView { Index = index, VoteableCount = voteable };
            if (index == 0)
            {
                mt.FailedTasks.Add(TaskKind.ClientBonus); mt.FailedTasks.Add(TaskKind.PettyCash);
                mt.Dossier.Add(new LineDossierEntry { Kind = DossierKind.TaskFailed, Task = TaskKind.ClientBonus, N1 = 0 });
                mt.Dossier.Add(new LineDossierEntry { Kind = DossierKind.BonusTakers, N1 = 2 });
                mt.Dossier.Add(new LineDossierEntry { Kind = DossierKind.Whistle, Player = 2, Other = 3, N1 = 3 });
                mt.Dossier.Add(new LineDossierEntry { Kind = DossierKind.CashMissing, N1 = 3, N2 = 1 });
                mt.Dossier.Add(new LineDossierEntry { Kind = DossierKind.Investigation, Player = 1, Other = 0, N1 = 0 });
                mt.Dossier.Add(new LineDossierEntry { Kind = DossierKind.Leak, Player = 2, Other = 0, N1 = 1, N2 = 3 });
                mt.Recordings.Add(new LineRecordingPick { Index = 0, PlayAt = Now + 2, Seconds = 7, Caller = 1, Callee = 3, Leaker = 2, CallStart = Now - 300 });
                mt.Recordings[0].CandidateCallIds.Add(5);
            }
            return mt;
        }

        public static LineFinalView FinalView(int players, int round, bool revote)
        {
            var f = new LineFinalView { Round = round, Revote = revote, FiringsTotal = players >= 5 ? 2 : 1 };
            f.Crisis.Add(new LineBlameGainView { Player = 2, Amount = 4 });
            f.Candidates.Add(2); f.Candidates.Add(1);
            f.DefenseOrder.Add(2); f.DefenseOrder.Add(1);
            if (players >= 5) { f.Candidates.Add(3); f.DefenseOrder.Add(3); }
            f.Options.AddRange(f.Candidates);
            return f;
        }
    }

    /// <summary>
    /// v5 scenarios for the play screen: <c>-uimock "v5.*"</c>. Each covers one phase's focus card (task choice, whistle,
    /// meeting actions, ballots with a leak playing, crisis, candidate with a sealed desk, defence, final vote, desk
    /// reveal, results summary and its detail tabs), the how-to pages, and two English samples.
    /// </summary>
    public static class LineMockV5Scenarios
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Register()
        {
            // office phases (v5 step 1): the task card lives on the office computer
            LineMock.Register("v5.task", () => new LineMockScenario().Do(0f, () => Show(Task(TaskKind.ClientBonus, 0, 4, LinePhase.TaskPlay))).Do(0.3f, Messages, true)
                .Do(1.0f, () => OfficeUi.I?.OpenComputer(OfficeComputer.App.Task), true));
            LineMock.Register("v5.task_shift", () => new LineMockScenario().Do(0f, () =>
            {
                var v = Task(TaskKind.WeekendShift, 2, 6, LinePhase.TaskPlay);
                v.Task.Submitted = true; v.Task.MyChoice = LineChoice.Volunteer;
                v.Mine.Phone.Status = CallStatus.Connected; v.Mine.Phone.CallId = 9; v.Mine.Phone.Since = LineMock.Now - 23;
                v.Players[3].Submitted = true; v.Players[4].Submitted = true;
                Show(v);
            }).Do(1.0f, () => OfficeUi.I?.OpenComputer(OfficeComputer.App.Task), true));
            LineMock.Register("v5.reveal", () => new LineMockScenario().Do(0f, () =>
            {
                var v = Task(TaskKind.PettyCash, 1, 4, LinePhase.Break);
                v.Features.WhistleOpen = true; v.Mine.WitnessOf.Add(1);
                v.Mine.Phone.Status = CallStatus.Connected; v.Mine.Phone.CallId = 9; v.Mine.Phone.Since = LineMock.Now - 41;
                Show(v);
            }).Do(1.0f, () => OfficeUi.I?.OpenComputer(OfficeComputer.App.Task), true));
            LineMock.Register("v5.meeting", () => new LineMockScenario().Do(0f, () =>
            {
                var v = LineMock.BaseView(LinePhase.MeetingDiscussion, 5);
                v.Meeting = LineMock.MeetingView(0, -1);
                v.Mine.InvestigationTargets.Add(1);
                v.Mine.Recorded.Add(new LineRecordedCallView { CallId = 3, Other = 1, Start = LineMock.Now - 200 });
                Show(v);
            }));
            LineMock.Register("v5.meeting_vote", () => new LineMockScenario().Do(0f, () =>
            {
                var v = LineMock.BaseView(LinePhase.MeetingVote, 5);
                v.Meeting = LineMock.MeetingView(0, 2);
                v.Meeting.Recordings[0].PlayAt = LineMock.Now - 2; // the leak is playing while the ballots are open
                v.Mine.Ballots.Add(3); v.Mine.Ballots.Add(-1);
                v.Mine.InvestigationUsed = true;
                v.Mine.Recorded.Add(new LineRecordedCallView { CallId = 3, Other = 1, Start = LineMock.Now - 200, Leaked = true });
                v.Players[1].Voted = true; v.Players[3].Voted = true;
                Show(v);
            }));
            LineMock.Register("v5.crisis_vote", () => new LineMockScenario().Do(0f, () =>
            {
                var v = LineMock.BaseView(LinePhase.CrisisVote, 6, 2);
                v.Meeting = new LineMeetingView { Index = 2 };
                v.Final = new LineFinalView { FiringsTotal = 2 };
                v.Mine.Ballots.Add(-1);
                v.Players[2].Voted = true;
                Show(v);
            }));
            LineMock.Register("v5.crisis_verdict", () => new LineMockScenario().Do(0f, () =>
            {
                var v = LineMock.BaseView(LinePhase.CrisisVerdict, 4, 2);
                v.Meeting = new LineMeetingView { Index = 2 };
                v.Final = Final(new[] { 2, 1 }, 1);
                v.Final.Crisis.Add(new LineBlameGainView { Player = 2, Amount = 4 });
                Candidates(v);
                Show(v);
            }));
            LineMock.Register("v5.sealed", () => new LineMockScenario().Do(0f, () =>
            {
                var v = LineMock.BaseView(LinePhase.FinalDiscussion, 4, 2);
                foreach (var p in v.Players) p.Blame = p.Id == 0 ? 7 : p.Id == 2 ? 9 : 2;
                v.Meeting = new LineMeetingView { Index = 2 };
                v.Final = Final(new[] { 2, 0 }, 1);
                v.Final.Crisis.Add(new LineBlameGainView { Player = 0, Amount = 4 });
                Candidates(v);
                v.Mine.Recorded.Add(new LineRecordedCallView { CallId = 11, Other = 3, Start = LineMock.Now - 95 });
                v.Mine.Transfers.Add(new LineTransferView { From = 3, To = 0, Amount = 3, Time = LineMock.Now - 5, Phase = LinePhase.FinalDiscussion });
                Show(v);
            }));
            LineMock.Register("v5.defense", () => new LineMockScenario().Do(0f, () =>
            {
                var v = LineMock.BaseView(LinePhase.FinalDefense, 5, 2);
                v.Meeting = new LineMeetingView { Index = 2 };
                v.Final = Final(new[] { 2, 1, 3 }, 2);
                v.Final.Defender = 1; v.Final.DefenderEndTime = LineMock.Now + 9;
                Candidates(v);
                Show(v);
            }));
            LineMock.Register("v5.final", () => new LineMockScenario().Do(0f, () =>
            {
                var v = LineMock.BaseView(LinePhase.FinalVote, 5, 2);
                v.Meeting = new LineMeetingView { Index = 2 };
                v.Final = Final(new[] { 2, 1, 3 }, 2);
                v.Final.Round = 1;
                v.Final.Firings.Add(Firing(2, 11, FiringDecision.Plurality, new[] { 0, 3, 4 }));
                v.Final.Options.Remove(2);
                Candidates(v);
                v.Mine.Ballots.Add(-1);
                v.Players[3].Voted = true;
                Show(v);
            }));
            LineMock.Register("v5.verdict", () => new LineMockScenario().Do(0f, () =>
            {
                var v = LineMock.BaseView(LinePhase.FinalVerdict, 6, 2);
                v.Meeting = new LineMeetingView { Index = 2 };
                v.Final = Final(new[] { 2, 1, 3 }, 2);
                v.Final.Firings.Add(Firing(2, 11, FiringDecision.Plurality, new[] { 0, 3, 4 }));
                Candidates(v);
                Show(v);
            }));
            LineMock.Register("v5.verdict_tie", () => new LineMockScenario().Do(0f, () =>
            {
                var v = LineMock.BaseView(LinePhase.FinalVerdict, 5, 2);
                v.Meeting = new LineMeetingView { Index = 2 };
                v.Final = Final(new[] { 2, 1, 3 }, 2);
                v.Final.Round = 1;
                v.Final.Firings.Add(Firing(2, 11, FiringDecision.Plurality, new[] { 0, 3, 4 }));
                v.Final.Firings.Add(Firing(1, 7, FiringDecision.HigherBlame, new int[0]));
                Candidates(v);
                Show(v);
            }));
            LineMock.Register("v5.results", () => new LineMockScenario().Do(0f, () => Show(Results(4))));
            LineMock.Register("v5.results_tabs", () => new LineMockScenario()
                .Do(0f, () => Show(Results(6)))
                .Do(0.2f, () => PlayUi.MockResultsTab = "choices", true)
                .Do(0.4f, () => PlayUi.MockResultsTab = "transfers", true)
                .Do(0.6f, () => PlayUi.MockResultsTab = "firings", true)
                .Do(0.8f, () => PlayUi.MockResultsTab = "incidents", true));
            LineMock.Register("v5.howto", () =>
            {
                var s = new LineMockScenario();
                for (int i = 0; i < HowToPlayScreen.Pages; i++)
                {
                    int page = i;
                    s.Do(0.1f * i, () => HowToPlayScreen.OpenAt(page), true);
                }
                return s;
            });
            LineMock.Register("v5.en_task", () => new LineMockScenario()
                .Do(0f, () => English())
                .Do(0.3f, () => { var v = Task(TaskKind.PettyCash, 1, 4, LinePhase.TaskPlay); LineMock.SetNames(v, true); Show(v); }));
            LineMock.Register("v5.en_verdict", () => new LineMockScenario()
                .Do(0f, () => English())
                .Do(0.3f, () =>
                {
                    var v = LineMock.BaseView(LinePhase.FinalVerdict, 6, 2);
                    LineMock.SetNames(v, true);
                    v.Meeting = new LineMeetingView { Index = 2 };
                    v.Final = Final(new[] { 2, 1, 3 }, 2);
                    v.Final.Firings.Add(Firing(2, 11, FiringDecision.Revote, new[] { 0, 3, 4 }));
                    Candidates(v);
                    Show(v);
                }));
        }

        /// <summary>Player 0's view of a task phase: Client Bonus / Petty Cash with player 1, Weekend Shift with everyone.</summary>
        static LineView Task(TaskKind kind, int number, int players, LinePhase phase)
        {
            bool revealed = phase != LinePhase.TaskPlay;
            var v = LineMock.BaseView(phase, players);
            v.TaskNumber = number;
            v.Task = LineMock.TaskView(kind, number, players, revealed, 0, revealed);
            v.Players[1].Submitted = true;
            if (!revealed)
            {
                v.Mine.Phone.Status = CallStatus.Ringing;
                v.Mine.Phone.Other = 2;
                v.Mine.Phone.RingUntil = LineMock.Now + 10;
            }
            else v.Mine.Phone.Other = 2;
            return v;
        }

        static LineFinalView Final(int[] candidates, int firings)
        {
            var f = new LineFinalView { FiringsTotal = firings };
            f.Candidates.AddRange(candidates);
            f.DefenseOrder.AddRange(candidates);
            f.Options.AddRange(candidates);
            return f;
        }

        /// <summary>Marks the final's candidates and fired players on the board.</summary>
        static void Candidates(LineView v)
        {
            foreach (var p in v.Players)
            {
                p.Candidate = v.Final.Candidates.Contains(p.Id);
                p.Fired = v.Final.Firings.Exists(f => f.Player == p.Id);
            }
        }

        static LineFiringView Firing(int player, int desk, FiringDecision decision, int[] voters)
        {
            var f = new LineFiringView { Player = player, Decision = decision, Votes = voters.Length, Desk = desk };
            f.Recipients.AddRange(voters);
            f.Share = voters.Length > 0 ? desk / voters.Length : 0;
            f.Removed = desk - f.Share * voters.Length;
            return f;
        }

        static LineView Results(int players)
        {
            var v = LineMock.BaseView(LinePhase.Results, players, 2);
            var r = new LineWeekResult { WeekId = 1, MatchId = "mock", PlayerCount = players, ResultHash = 1234567 };
            int[] prim = { 14, 9, 0, 11, 6, 0 };
            int[] blame = { 3, 8, 11, 2, 7, 9 };
            for (int i = 0; i < players; i++)
            {
                bool fired = i == 2 || (players >= 5 && i == 5);
                r.Players.Add(new LineWeekPlayerResult { Player = i, Prim = prim[i], Blame = blame[i], Fired = fired, Winner = i == 0,
                    WasCandidate = i == 1 || i == 2 || (players >= 5 && i == 5), Recorded = i == 1 ? 1 : 0, Leaked = i == 1 ? 1 : 0 });
            }
            r.Winners.Add(0); r.Fired.Add(2);
            r.Firings.Add(Firing(2, 11, FiringDecision.Plurality, players >= 5 ? new[] { 0, 3, 4 } : new[] { 0, 3 }));
            if (players >= 5) { r.Fired.Add(5); r.Firings.Add(Firing(5, 6, FiringDecision.Revote, new[] { 1, 3 })); }
            for (int i = 0; i < players; i++)
            {
                r.Ballots.Add(new LineBallotView { Kind = BallotKind.Meeting, Round = 0, Voter = i, Index = 0, Target = (i + 2) % players });
                r.Ballots.Add(new LineBallotView { Kind = BallotKind.Meeting, Round = 0, Voter = i, Index = 1, Target = i == 3 ? 3 : (i + 1) % players, SelfAssigned = i == 3 });
            }
            for (int i = 0; i < players; i++) r.Ballots.Add(new LineBallotView { Kind = BallotKind.Crisis, Voter = i, Target = i == 2 ? 1 : 2 });
            for (int i = 0; i < players; i++)
                if (i != 2) r.Ballots.Add(new LineBallotView { Kind = BallotKind.Final, Round = 0, Voter = i, Target = i == 1 ? -1 : 2 });
            r.Transfers.Add(new LineTransferView { From = 1, To = 0, Amount = 2, Phase = LinePhase.Break });
            r.Transfers.Add(new LineTransferView { From = 3, To = 2, Amount = 4, Phase = LinePhase.CrisisCalls });
            for (int t = 0; t < 3; t++)
            {
                var kind = t == 0 ? TaskKind.ClientBonus : t == 1 ? TaskKind.PettyCash : TaskKind.WeekendShift;
                for (int i = 0; i < players; i++)
                {
                    int choice = (i + t) % 3 == 0 ? 1 : 0;
                    r.Choices.Add(new LineChoiceView { TaskNumber = t, Task = kind, Player = i, Choice = choice, Payout = kind == TaskKind.WeekendShift ? (choice == 1 ? 0 : 2) : choice == 1 ? 5 : 2 });
                }
            }
            r.Incidents.Add(new LineDossierEntry { Kind = DossierKind.Whistle, Player = 3, Other = 2, N1 = 3 });
            r.Incidents.Add(new LineDossierEntry { Kind = DossierKind.Investigation, Player = 1, Other = 0, N1 = 0 });
            r.Incidents.Add(new LineDossierEntry { Kind = DossierKind.Leak, Player = 1, Other = 0, N1 = 2, N2 = 3 });
            v.Results = r;
            return v;
        }

        static void Show(LineView v)
        {
            LineMock.SyncMessageSeq(v);
            LineClient.I.ApplyView(v);
        }

        /// <summary>English UI for this scenario (not saved; the driver switches back before the next one).</summary>
        static void English()
        {
            Loc.Load("en");
            if (UIRoot.I != null) UIRoot.I.Rebuild();
        }

        static void Messages()
        {
            var list = new List<LineMessage>
            {
                LineMock.Msg(MessageKind.Text, 1, 0, "Paylaşalım, ben de paylaşacağım."),
                LineMock.Msg(MessageKind.Text, 0, 1, "Anlaştık."),
                LineMock.Msg(MessageKind.Text, 2, 0, "Sarı'ya güvenme.", ago: 10),
            };
            LineClient.I.ApplyMessages(1, list);
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

        /// <summary>Like <see cref="Do(float, Action, bool)"/> but shot <paramref name="shotDelay"/> seconds after the step
        /// (mid-animation shots: the pencil writing, the notes in the hand).</summary>
        public LineMockScenario Do(float atSeconds, Action action, float shotDelay)
        {
            Steps.Add(new LineMockStep { At = atSeconds, Action = action, Shot = true, ShotDelay = shotDelay });
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
        /// <summary>Seconds between the step and its screenshot (entrance motion settles in the default 1.2 s).</summary>
        public float ShotDelay = 1.2f;
    }

    /// <summary>Added by <see cref="GameBootstrap"/> when <c>-uimock</c> is present.</summary>
    public sealed class LineMockDriver : MonoBehaviour
    {
        public static bool Active { get; private set; }
        string _pattern = "", _outDir = "", _lang = "";

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
            _lang = Loc.Language;
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
            // every scenario starts from the same shell: no how-to overlay, the start language (English samples switch it)
            HowToPlayScreen.Close();
            if (Loc.Language != _lang)
            {
                Loc.Load(_lang);
                if (UIRoot.I != null) UIRoot.I.Rebuild();
                yield return null;
            }
            var client = LineClient.I;
            client.ClearWeek();    // the UI hears "week closed" (phase None) like after a real match
            client.ResetSession(); // also drops the previous scenario's messages
            client.SetIdentity(s.Me, s.Config);
            // two frames without a week: the office closes what was open and the world forgets the last room
            yield return null;
            yield return null;
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
                    yield return new WaitForSecondsRealtime(st.ShotDelay); // let entrance motion settle
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
