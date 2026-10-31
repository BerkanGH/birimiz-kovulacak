using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// UI mock scenarios of the meeting + shell track (<c>-uimock "meeting.*" | "final.*" | "results.*" | "menu.*" | "lobby.*" | "howto.*" | "match.*"</c>),
    /// each in Turkish and English. Views are built when their step runs (times relative to <see cref="LineMock.Now"/>).
    /// A scenario sets everything global it relies on (language, host, voice, room, telemetry privacy). Helpers live here
    /// (the desk track owns LineMock): <see cref="Base"/>, <see cref="Meeting"/>, <see cref="Show"/>.
    /// </summary>
    public static class MeetingMocks
    {
        static int _week = 100;
        static long _version = 1;
        static bool _hostile;

        static readonly string[] NamesTr = { "Ayşe", "Burak", "Ece", "Kaan", "Zeynep", "Mert" };
        static readonly string[] NamesEn = { "Alex", "Sam", "Jordan", "Riley", "Casey", "Morgan" };
        /// <summary>Markup in a name must render literally (rich text is off).</summary>
        const string HostileName = "<size=300>Mert";
        static readonly int[] Blames = { 6, 9, 12, 5, 8, 4 };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Register()
        {
            foreach (var en in new[] { false, true })
            {
                string L = en ? ".en" : ".tr";
                int n3 = en ? 3 : 4, n6 = 6;
                // ---- dossier (projector slides)
                LineMock.Register("meeting.dossier_cover" + L, () => Dossier(en, n6, DossierShot.Cover));
                LineMock.Register("meeting.dossier_company" + L, () => Dossier(en, en ? 6 : 4, DossierShot.Company));
                LineMock.Register("meeting.dossier_log" + L, () => Dossier(en, n6, DossierShot.Log));
                LineMock.Register("meeting.dossier_complaints" + L, () => Dossier(en, n6, DossierShot.Complaints));
                LineMock.Register("meeting.dossier_tape" + L, () => Dossier(en, n6, DossierShot.Tape));
                LineMock.Register("meeting.dossier_board" + L, () => Dossier(en, n3, DossierShot.Board));
                // ---- discussion: evidence presentation, chat
                LineMock.Register("meeting.present" + L, () => Present(en, n6));
                LineMock.Register("meeting.chat" + L, () => Chat(en, n6));
                LineMock.Register("meeting.discussion" + L, () => Discussion(en, n3, false));
                LineMock.Register("meeting.room" + L, () => Discussion(en, n6, true));
                // ---- daily ballot and verdicts
                LineMock.Register("meeting.vote" + L, () => Vote(en, n6));
                LineMock.Register("meeting.verdict_warning" + L, () => Verdict(en, n6, VoteOutcome.WarningGiven));
                LineMock.Register("meeting.verdict_capped" + L, () => Verdict(en, n6, VoteOutcome.Capped));
                LineMock.Register("meeting.verdict_tie" + L, () => Verdict(en, n6, VoteOutcome.Tie));
                LineMock.Register("meeting.verdict_below" + L, () => Verdict(en, n6, VoteOutcome.BelowMinimum));
                LineMock.Register("meeting.verdict_noaction" + L, () => Verdict(en, n6, VoteOutcome.NoActionWon));
                LineMock.Register("meeting.verdict_novotes" + L, () => Verdict(en, n6, VoteOutcome.NoVotes));
                LineMock.Register("meeting.pause" + L, () => Pause(en));
                // ---- final
                LineMock.Register("final.postmortem" + L, () => FinalDiscussion(en, n6, true));
                LineMock.Register("final.discussion" + L, () => FinalDiscussion(en, n3, false));
                LineMock.Register("final.defense" + L, () => Defense(en, n6));
                LineMock.Register("final.vote" + L, () => FinalVote(en, n6));
                LineMock.Register("final.verdict_fired" + L, () => FinalVerdict(en, n6, VoteOutcome.Fired));
                LineMock.Register("final.verdict_nobody" + L, () => FinalVerdict(en, n3, VoteOutcome.NoMajority));
                LineMock.Register("final.verdict_closed" + L, () => FinalVerdict(en, n6, VoteOutcome.CompanyClosed));
                // ---- results
                LineMock.Register("results.survived" + L, () => Results(en, n6, false, false));
                LineMock.Register("results.survived_4p" + L, () => Results(en, 4, false, false));
                LineMock.Register("results.closed" + L, () => Results(en, n6, true, false));
                LineMock.Register("results.nobody" + L, () => Results(en, n3, false, true));
                LineMock.Register("results.pick" + L, () => Results(en, 4, false, false, true));
                LineMock.Register("results.abandoned" + L, () => Abandoned(en));
                // ---- shell
                LineMock.Register("menu.main" + L, () => Menu(en, 0));
                LineMock.Register("menu.join" + L, () => Menu(en, 1));
                LineMock.Register("menu.settings" + L, () => Menu(en, 2));
                LineMock.Register("menu.settings_privacy" + L, () => Menu(en, 5));
                LineMock.Register("menu.consent" + L, () => Menu(en, 6));
                LineMock.Register("menu.intro" + L, () => Menu(en, 7));
                LineMock.Register("howto.pages" + L, () => Menu(en, 3));
                LineMock.Register("lobby.host" + L, () => Lobby(en, en ? 3 : 6, true, ""));
                LineMock.Register("lobby.client" + L, () => Lobby(en, 4, false, "lobby.need_ready"));
                LineMock.Register("lobby.pool_error" + L, () => Lobby(en, 3, false, "lobby.pool_too_small"));
                LineMock.Register("lobby.need_players" + L, () => Lobby(en, 2, true, "lobby.need_players"));
                LineMock.Register("match.moment" + L, () => Moment(en));
            }
            LineMock.Register("menu.language", () => Menu(false, 4));
        }

        // ================================================================== helpers

        sealed class FakeVoice : IVoiceUi
        {
            public readonly HashSet<int> Speaking = new HashSet<int>();
            public bool Available => true;
            public bool IsSpeaking(int id) { return Speaking.Contains(id); }
            public float MicLevel => 0.3f;
            public bool Transmitting => false;
            public string PttKeyLabel => "V";
            public bool Muted => false;
            public bool PushToTalk => true;
        }

        sealed class MockPrivacy : ITelemetryPrivacy
        {
            public int Consent { get; private set; } = 1;
            public bool NeedsConsentPrompt { get; set; }
            public bool CloudAvailable => true;
            public void SetConsent(int level) { Consent = level; NeedsConsentPrompt = false; }
            public bool DeleteLocalData() { return true; }
            public void ResetAnonymousId() { }
            public string AnonymousId => Consent >= 2 ? "mock-3f9a1c" : "";
            public string LogDirectory => "C:/Users/Player/AppData/LocalLow/Leonidas/telemetry";
            public void OpenLogFolder() { }
            public string ExportLogs() { return "C:/Users/Player/AppData/LocalLow/Leonidas/telemetry/export_mock.zip"; }
            public bool MomentMarkerAvailable { get; set; } = true;
        }

        static string[] Names(bool en)
        {
            var n = (string[])(en ? NamesEn : NamesTr).Clone();
            if (_hostile) n[5] = HostileName;
            return n;
        }

        /// <summary>Resets globals and switches the language (the UI rebuilds next frame).</summary>
        static void Setup(LineMockScenario s, bool en, bool host = false, IVoiceUi voice = null, bool room = false, int me = 0, MockPrivacy privacy = null, bool hostile = false)
        {
            s.Me = me;
            s.Do(0f, () =>
            {
                _hostile = hostile;
                PauseMenu.Close();
                if (MainMenuScreen.Instance != null && MainMenuScreen.Instance.JoinOpen) MainMenuScreen.Instance.ShowJoin(false);
                SettingsScreen.Close();
                HowToPlayScreen.Close();
                IntroScreen.Close();
                ConsentScreen.Close();
                Modal.CloseAll();
                HostLineController.Current = host ? new HostLineController() : null;
                UiServices.Override = voice;
                TelemetryHooks.Privacy = privacy;
                MockRoom.Set(room);
                if (Loc.Language != (en ? "en" : "tr")) Loc.SetLanguage(en ? "en" : "tr");
            });
        }

        static void Show(LineMockScenario s, float at, Func<LineView> make, bool shot = false)
        {
            s.Do(at, () =>
            {
                var v = make();
                v.Now = LineMock.Now;
                v.StateVersion = ++_version;
                LineClient.I.ApplyView(v);
            }, shot);
        }

        static void Wait(LineMockScenario s, float at) { s.Do(at, () => { }, true); }

        /// <summary>A 3..6 player view of <paramref name="phase"/>, Me = 0, a healthy company, rich private state.</summary>
        static LineView Base(bool en, int players, LinePhase phase, double elapsed, double left, int day = 1, int week = 0)
        {
            double now = LineMock.Now;
            var v = new LineView
            {
                WeekId = week != 0 ? week : _week, StateVersion = 1, Me = 0, Now = now, Phase = phase,
                PhaseStartTime = now - elapsed, PhaseEndTime = now + left, Day = day, DayCount = 5, Length = WeekLength.Full,
                ManagerProfileId = LineManagerIds.Paranoyak, Favourite = 3, MatchId = "mock",
            };
            var names = Names(en);
            int[] warnings = { 0, 1, 2, 0, 1, 0 };
            for (int i = 0; i < players; i++)
                v.Players.Add(new LinePlayerView
                {
                    Id = i, Name = names[i], ColorIndex = i, Connected = true, IsBot = i == 4 && players >= 5, Blame = Blames[i], Warnings = warnings[i],
                    SessionPoints = new[] { 215, 100, 135, 235, 75, 175 }[i],
                });
            v.TodayTasks.Add(TaskKind.InvoiceReconcile);
            v.TodayTasks.Add(TaskKind.ReportReview);
            v.Features.RecordingsEnabled = true;
            v.Features.TricksUnlocked = true;
            v.Company = new LineCompanyView
            {
                ProvisionalPermille = 725, TargetPercent = 65, ResolvedOutput = 290, ResolvedMax = 400, ScheduledMax = 1000, TasksResolved = 4, TasksScheduled = 10,
                LastChange = CompanyChange.TaskResolved, LastTask = TaskKind.ReportReview, LastDelta = 70,
            };
            v.Mine.Money = 5;
            v.Mine.PresentationsLeft = 2;
            v.Mine.MeetingChatLeft = 24;
            v.Mine.Objective = new LineObjectiveView { Kind = ObjectiveKind.Broker, Progress = 3, Threshold = 6 };
            AddRecords(v, players);
            return v;
        }

        static void AddRecords(LineView v, int players)
        {
            double now = LineMock.Now;
            v.Mine.Records.Clear();
            v.Mine.Records.Add(new LineRecordView { Id = 11, Kind = RecordKind.Call, Source = RecordSource.PhoneLog, Time = now - 420, Day = v.Day, Actor = 0, Subject = 1, N1 = 42 });
            v.Mine.Records.Add(new LineRecordView { Id = 12, Kind = RecordKind.Call, Source = RecordSource.PhoneLog, Time = now - 380, Day = v.Day, Actor = 3, Subject = 0, N1 = 18 });
            v.Mine.Records.Add(new LineRecordView { Id = 13, Kind = RecordKind.TaskOutcome, Source = RecordSource.TaskSystem, Time = now - 300, Day = v.Day, Task = TaskKind.InvoiceReconcile, TaskNumber = 2, N1 = 100, N2 = 100, Actor = LineIds.None });
            v.Mine.Records.Add(new LineRecordView { Id = 14, Kind = RecordKind.DealPayment, Source = RecordSource.Bank, Time = now - 260, Day = v.Day, Actor = 3, Subject = LineIds.None, N1 = 3, N2 = (int)IncidentKind.InvoicePadding, ProofOf = 7 });
            v.Mine.Records.Add(new LineRecordView { Id = 15, Kind = RecordKind.TrickEffect, Source = RecordSource.ITLog, Time = now - 200, Day = v.Day, Subject = 1, N1 = (int)TrickType.KahveDok });
            v.Mine.Records.Add(new LineRecordView { Id = 16, Kind = RecordKind.Transfer, Source = RecordSource.Bank, Time = now - 150, Day = v.Day, Actor = 0, Subject = 2, N1 = 2 });
        }

        enum DossierShot { Cover, Company, Log, Complaints, Tape, Board }

        static LineMeetingView Meeting(LineView v, bool final, bool closed = false)
        {
            double now = LineMock.Now;
            var m = new LineMeetingView { IsFinal = final, CompanyClosed = closed };
            v.Meeting = m;
            v.Features.MeetingChatOpen = true;
            v.Features.PresentationOpen = true;
            int n = v.Players.Count;
            m.Dossier.Add(new LineDossierEntry { Kind = DossierKind.Productivity, N1 = v.Company.ProvisionalPermille, N2 = 65 });
            m.Dossier.Add(new LineDossierEntry { Kind = DossierKind.TaskOutcome, Task = TaskKind.InvoiceReconcile, TaskNumber = v.Day * 2, N1 = 100, N2 = 100 });
            m.Dossier.Add(new LineDossierEntry { Kind = DossierKind.TaskOutcome, Task = TaskKind.ReportReview, TaskNumber = v.Day * 2 + 1, N1 = 30, N2 = 100 });
            m.Dossier.Add(new LineDossierEntry { Kind = DossierKind.IncidentEffect, Trick = TrickType.KahveDok, Victim = 1, Time = now - 400 });
            m.Dossier.Add(new LineDossierEntry { Kind = DossierKind.IncidentEffect, Trick = TrickType.InternetKes, Victim = LineIds.None, Time = now - 330 });
            m.Dossier.Add(new LineDossierEntry { Kind = DossierKind.MissedTask, Player = Math.Min(3, n - 1), TaskNumber = v.Day * 2 + 1 });
            m.Dossier.Add(new LineDossierEntry { Kind = DossierKind.Remedial, Player = 2, Victim = Math.Min(4, n - 1), TaskNumber = v.Day * 2, N1 = 10 });
            m.Dossier.Add(new LineDossierEntry { Kind = DossierKind.Favourite, Player = 3 });
            m.Complaints.Add(new LineComplaintView { Target = 1, Preset = 0 });
            m.Complaints.Add(new LineComplaintView { Target = 1, Preset = 3 });
            m.Complaints.Add(new LineComplaintView { Target = 2, Preset = -1, Text = Loc.Language == "en" ? "Never reads the shared drive. <b>Ever.</b>" : "Ortak diski hiç okumuyor. <b>Hiç.</b>" });
            m.Dossier.Add(new LineDossierEntry { Kind = DossierKind.Complaints, N1 = m.Complaints.Count });
            return m;
        }

        static void AddRecording(LineView v, double elapsed)
        {
            double now = LineMock.Now;
            v.Meeting.Recordings.Add(new LineRecordingPick
            {
                Index = 0, PlayAt = now - elapsed + 4, Seconds = 7, ChosenCallId = 3, Caller = 1, Callee = Math.Min(3, v.Players.Count - 1), CallStart = now - 500,
                Context = TaskKind.CustomerSpec,
            });
        }

        static void AddPresentations(LineView v, bool exposed, bool repeat)
        {
            double now = LineMock.Now;
            var m = v.Meeting;
            m.Presentations.Clear();
            m.Presentations.Add(new LinePresentationView { Presenter = 0, Time = now - 40, Record = v.Mine.Records[0] });
            if (exposed)
            {
                var proof = new LineRecordView { Id = 14, Kind = RecordKind.DealPayment, Source = RecordSource.Bank, Time = now - 260, Day = v.Day, Actor = 3, N1 = 3, N2 = (int)IncidentKind.InvoicePadding, ProofOf = 7, Presented = true };
                m.Presentations.Add(new LinePresentationView { Presenter = 2, Time = now - 20, Record = proof, ExposedIncident = 7, ExposedKind = IncidentKind.InvoicePadding });
                if (repeat) m.Presentations.Add(new LinePresentationView { Presenter = Math.Min(4, v.Players.Count - 1), Time = now - 6, Record = proof, Repeat = true });
            }
        }

        // ================================================================== meeting

        static LineMockScenario Dossier(bool en, int players, DossierShot shot)
        {
            var s = new LineMockScenario();
            Setup(s, en, false, null, false, 0, null, shot == DossierShot.Complaints);
            _week++;
            int week = _week;
            Show(s, 0.3f, () =>
            {
                double elapsed = shot == DossierShot.Tape ? 6 : shot == DossierShot.Cover ? 1 : 8;
                var v = Base(en, players, LinePhase.MeetingDossier, elapsed, 40, 1, week);
                Meeting(v, false);
                if (shot == DossierShot.Tape) AddRecording(v, elapsed);
                return v;
            }, false);
            s.Do(1.0f, () =>
            {
                var ui = MeetingUi.Current;
                if (ui != null) ui.JumpTo(shot == DossierShot.Cover ? 0 : shot == DossierShot.Company ? 1 : shot == DossierShot.Log ? 2 : shot == DossierShot.Complaints ? 3 : shot == DossierShot.Tape ? 4 : 99);
            }, true);
            return s;
        }

        static LineMockScenario Present(bool en, int players)
        {
            var s = new LineMockScenario();
            var voice = new FakeVoice();
            Setup(s, en, false, voice);
            _week++;
            int week = _week;
            Show(s, 0.3f, () =>
            {
                var v = Base(en, players, LinePhase.MeetingDiscussion, 12, 38, 1, week);
                Meeting(v, false);
                return v;
            }, false);
            s.Do(0.9f, () => { MeetingUi.Current?.ToggleScreenTo(true); }, true); // empty slate + the records picker is on the right
            Show(s, 1.4f, () =>
            {
                var v = Base(en, players, LinePhase.MeetingDiscussion, 16, 34, 1, week);
                Meeting(v, false);
                AddPresentations(v, false, false);
                v.Mine.PresentationsLeft = 1;
                return v;
            }, false);
            Wait(s, 2.6f);
            Show(s, 3.2f, () =>
            {
                var v = Base(en, players, LinePhase.MeetingDiscussion, 22, 28, 1, week);
                Meeting(v, false);
                AddPresentations(v, true, false);
                v.Mine.PresentationsLeft = 0;
                return v;
            }, false);
            Wait(s, 4.6f);
            Show(s, 5.2f, () =>
            {
                var v = Base(en, players, LinePhase.MeetingDiscussion, 30, 20, 1, week);
                Meeting(v, false);
                AddPresentations(v, true, true);
                v.Mine.PresentationsLeft = 0;
                return v;
            }, false);
            Wait(s, 6.4f);
            return s;
        }

        static LineMockScenario Chat(bool en, int players)
        {
            var s = new LineMockScenario();
            var voice = new FakeVoice();
            voice.Speaking.Add(2);
            Setup(s, en, false, voice, false, 0, null, true);
            _week++;
            int week = _week;
            Func<LineView> make = () =>
            {
                var v = Base(en, players, LinePhase.MeetingDiscussion, 14, 36, 2, week);
                Meeting(v, false);
                v.Mine.MeetingChatLeft = 21;
                v.Mine.NextMessageAt = LineMock.Now + 1.0;
                return v;
            };
            Show(s, 0.3f, make, false);
            s.Do(0.8f, () =>
            {
                var names = Names(en);
                string[] lines = en
                    ? new[] { "Who touched the invoice file?", "I only did what the report said.", "Reading the HR record. Does that prove anything?", "<color=red>Not</color> proof. Present it if you have it.", "My mic is broken, typing instead." }
                    : new[] { "Fatura dosyasına kim dokundu?", "Ben sadece raporda yazanı yaptım.", "İK kaydını okuyorum. Bu bir şey kanıtlıyor mu?", "<color=red>Kanıt</color> değil. Elindeyse sun.", "Mikrofonum bozuk, yazıyorum." };
                var batch = new List<LineMessage>();
                long seq = LineClient.I.LastMessageSeq;
                for (int i = 0; i < lines.Length; i++)
                    batch.Add(new LineMessage { Seq = ++seq, Id = 500 + i, Kind = MessageKind.MeetingChat, From = new[] { 1, 3, 2, 5, 0 }[Math.Min(i, 4)] % players, To = LineIds.None, ToAll = true, Text = lines[i], Time = LineMock.Now - 30 + i * 6 });
                LineClient.I.ApplyMessages(week, batch);
            }, false);
            s.Do(1.3f, () => MeetingUi.Current?.ToggleScreenTo(true), false);
            s.Do(1.6f, () => { }, true);
            return s;
        }

        static LineMockScenario Discussion(bool en, int players, bool room)
        {
            var s = new LineMockScenario();
            var voice = new FakeVoice();
            voice.Speaking.Add(1);
            Setup(s, en, false, voice, room);
            _week++;
            int week = _week;
            Show(s, 0.3f, () =>
            {
                var v = Base(en, players, LinePhase.MeetingDiscussion, 18, 32, 1, week);
                Meeting(v, false);
                return v;
            }, true);
            if (room)
            {
                s.Do(1.8f, () => MeetingUi.Current?.ToggleScreenTo(true), true);
            }
            return s;
        }

        static LineMockScenario Vote(bool en, int players)
        {
            var s = new LineMockScenario();
            Setup(s, en);
            _week++;
            int week = _week;
            Show(s, 0.3f, () =>
            {
                var v = Base(en, players, LinePhase.MeetingVote, 3, 17, 1, week);
                Meeting(v, false);
                v.Players[2].Voted = true; v.Players[3].Voted = true;
                return v;
            }, true);
            Show(s, 2.4f, () =>
            {
                var v = Base(en, players, LinePhase.MeetingVote, 8, 12, 1, week);
                Meeting(v, false);
                v.Meeting.MyVote = LineIds.Nobody; // "No action"
                v.Players[0].Voted = true; v.Players[2].Voted = true; v.Players[3].Voted = true; v.Players[1].Voted = true;
                return v;
            }, true);
            return s;
        }

        static LineMockScenario Verdict(bool en, int players, VoteOutcome outcome)
        {
            var s = new LineMockScenario();
            Setup(s, en);
            _week++;
            int week = _week;
            Show(s, 0.3f, () =>
            {
                var v = Base(en, players, LinePhase.MeetingVerdict, 1, 5, 1, week);
                var m = Meeting(v, false);
                m.Outcome = outcome;
                switch (outcome)
                {
                    case VoteOutcome.WarningGiven:
                        Cast(m, 0, 1); Cast(m, 2, 1); Cast(m, 3, 1); Cast(m, 4, LineIds.Nobody); Cast(m, 5, 2);
                        m.WarningTo = 1; v.Players[1].Warnings += 1; v.Players[1].Blame += MeetingText.Profile(v).WarningBlame;
                        break;
                    case VoteOutcome.Capped:
                        Cast(m, 0, 2); Cast(m, 1, 2); Cast(m, 3, 2); Cast(m, 4, LineIds.Nobody);
                        break;
                    case VoteOutcome.Tie:
                        Cast(m, 0, 1); Cast(m, 2, 1); Cast(m, 3, 3); Cast(m, 4, 3); Cast(m, 1, LineIds.Nobody);
                        break;
                    case VoteOutcome.BelowMinimum:
                        Cast(m, 0, 3); Cast(m, 1, LineIds.None); Cast(m, 2, LineIds.None);
                        break;
                    case VoteOutcome.NoActionWon:
                        Cast(m, 0, LineIds.Nobody); Cast(m, 1, LineIds.Nobody); Cast(m, 2, LineIds.Nobody); Cast(m, 3, 1); Cast(m, 4, 1);
                        break;
                }
                return v;
            }, false);
            s.Do(2.6f, () => { }, true);
            return s;
        }

        static void Cast(LineMeetingView m, int voter, int target) { m.Votes.Add(new LineVoteView { Voter = voter, Target = target }); }

        static LineMockScenario Pause(bool en)
        {
            var s = new LineMockScenario();
            Setup(s, en, true);
            _week++;
            int week = _week;
            Show(s, 0.3f, () => { var v = Base(en, 4, LinePhase.MeetingDiscussion, 10, 40, 1, week); Meeting(v, false); return v; }, false);
            s.Do(0.8f, PauseMenu.Open, true);
            return s;
        }

        // ================================================================== final

        static LineMockScenario FinalDiscussion(bool en, int players, bool closed)
        {
            var s = new LineMockScenario();
            Setup(s, en, false, null, false, 0, null, closed);
            _week++;
            int week = _week;
            Show(s, 0.3f, () =>
            {
                var v = Base(en, players, LinePhase.MeetingDiscussion, 20, 55, 4, week);
                var m = Meeting(v, true, closed);
                if (closed)
                {
                    v.Company.Closed = true;
                    v.Company.FinalPermille = 540;
                    v.Company.ProvisionalPermille = 540;
                }
                else v.Company.FinalPermille = 725;
                AddPresentations(v, true, false);
                return v;
            }, false);
            s.Do(1.0f, () => MeetingUi.Current?.JumpTo(1), true);
            return s;
        }

        static LineMockScenario Defense(bool en, int players)
        {
            var s = new LineMockScenario();
            var voice = new FakeVoice();
            voice.Speaking.Add(2);
            Setup(s, en, false, voice);
            _week++;
            int week = _week;
            Func<int, LineView> make = defender =>
            {
                var v = Base(en, players, LinePhase.FinalDefense, 6, 80, 4, week);
                var m = Meeting(v, true);
                m.Roster.Clear();
                for (int i = 0; i < players; i++) m.Roster.Add(i);
                m.DefenseOrder.Clear();
                foreach (int id in new[] { 2, 1, 4, 3, 5, 0 }) if (id < players) m.DefenseOrder.Add(id);
                m.Defender = defender;
                m.DefenderEndTime = LineMock.Now + 11;
                return v;
            };
            Show(s, 0.3f, () => make(2), true);
            Show(s, 2.4f, () => make(0), true); // my own turn
            return s;
        }

        static LineMockScenario FinalVote(bool en, int players)
        {
            var s = new LineMockScenario();
            Setup(s, en);
            _week++;
            int week = _week;
            Func<bool, LineView> make = voted =>
            {
                var v = Base(en, players, LinePhase.FinalVote, 6, 19, 4, week);
                var m = Meeting(v, true);
                for (int i = 0; i < players; i++) m.Roster.Add(i);
                m.MajorityNeeded = players / 2 + 1;
                v.Players[1].Voted = true; v.Players[3].Voted = true;
                if (voted) { m.MyVote = LineIds.Nobody; v.Players[0].Voted = true; v.Players[4 % players].Voted = true; }
                return v;
            };
            Show(s, 0.3f, () => make(false), true);
            Show(s, 2.4f, () => make(true), true);
            return s;
        }

        static LineMockScenario FinalVerdict(bool en, int players, VoteOutcome outcome)
        {
            var s = new LineMockScenario();
            Setup(s, en, false, null, false, 0, null, outcome == VoteOutcome.CompanyClosed);
            _week++;
            int week = _week;
            Show(s, 0.3f, () =>
            {
                var v = Base(en, players, LinePhase.FinalVerdict, 1, 9, 4, week);
                var m = Meeting(v, true, outcome == VoteOutcome.CompanyClosed);
                for (int i = 0; i < players; i++) m.Roster.Add(i);
                m.MajorityNeeded = players / 2 + 1;
                m.Outcome = outcome;
                switch (outcome)
                {
                    case VoteOutcome.Fired:
                        for (int i = 0; i < players; i++) Cast(m, i, i == 2 ? 1 : 2);
                        m.Votes[players - 1].Target = LineIds.Nobody;
                        m.Fired = 2;
                        break;
                    case VoteOutcome.NoMajority:
                        Cast(m, 0, 1); Cast(m, 1, 2); Cast(m, 2, LineIds.Nobody);
                        m.Fired = LineIds.Nobody;
                        break;
                    case VoteOutcome.CompanyClosed:
                        v.Company.Closed = true; v.Company.FinalPermille = 540;
                        m.Fired = LineIds.None;
                        break;
                }
                return v;
            }, false);
            s.Do(4.0f, () => { }, true);
            return s;
        }

        // ================================================================== results

        static LineWeekResult MakeResult(bool en, int players, bool closed, bool nobody, bool pickPending, int me)
        {
            var cfg = LineConfig.CreateDefault();
            var r = new LineWeekResult
            {
                WeekId = _week, MatchId = "mock", ManagerProfileId = LineManagerIds.Paranoyak, Days = 5, CompanyClosed = closed, TargetPercent = 65,
                FinalProductivityPermille = closed ? 540 : 725, IncidentsTotal = 4,
                Fired = closed ? LineIds.None : nobody ? LineIds.Nobody : 2,
                FinalOutcome = closed ? VoteOutcome.CompanyClosed : nobody ? VoteOutcome.NoMajority : VoteOutcome.Fired,
                NextManagerPick = "",
            };
            r.Exposed.Add(new LineIncidentView { Id = 7, Kind = IncidentKind.InvoicePadding, Actor = 3, Day = 1, TaskNumber = 2, ExposedBy = 2, Time = LineMock.Now - 300 });
            var objectives = new[] { ObjectiveKind.Broker, ObjectiveKind.Guardian, ObjectiveKind.CleanRecord, ObjectiveKind.Rival, ObjectiveKind.Saver, ObjectiveKind.Presenter };
            var done = new[] { true, true, false, true, false, true };
            int[] qual = { 91, 84, 62, 70, 55, 78 };
            var list = new List<LineWeekPlayerResult>();
            int eotm = nobody ? -1 : 1;
            for (int i = 0; i < players; i++)
            {
                var p = new LineWeekPlayerResult
                {
                    Player = i, Blame = Blames[i], Warnings = i == 1 ? 1 : i == 2 ? 2 : 0, Fired = closed || (!nobody && i == 2), Objective = objectives[i],
                    ObjectiveTarget = objectives[i] == ObjectiveKind.Guardian ? 4 % players : objectives[i] == ObjectiveKind.Rival ? 1 : -1, ObjectiveDone = done[i],
                    EmployeeOfMonth = !closed && i == eotm, Money = 3 + i * 2, ContributionQuality = qual[i], TasksConnected = 10, ExposedIncidents = i == 3 ? 1 : 0,
                };
                if (!closed)
                {
                    if (!p.Fired) p.Career.Add(new LineCareerLine { Item = CareerItem.NotFired, Points = cfg.CareerNotFired });
                    p.Career.Add(new LineCareerLine { Item = CareerItem.CompanyTarget, Points = cfg.CareerCompanyTarget });
                    if (p.ObjectiveDone) p.Career.Add(new LineCareerLine { Item = CareerItem.SecretObjective, Points = cfg.CareerObjective });
                    if (p.EmployeeOfMonth) p.Career.Add(new LineCareerLine { Item = CareerItem.EmployeeOfMonth, Points = cfg.CareerEmployeeOfMonth });
                }
                if (p.ExposedIncidents > 0) p.Career.Add(new LineCareerLine { Item = CareerItem.ProvenSabotage, Points = -cfg.CareerSabotagePenalty * p.ExposedIncidents, Count = p.ExposedIncidents });
                foreach (var c in p.Career) p.CareerTotal += c.Points;
                p.SessionPoints = new[] { 215, 100, 135, 235, 75, 175 }[i] + p.CareerTotal;
                list.Add(p);
            }
            // competition ranks (1, 1, 3); none when the company closed
            if (!closed)
            {
                var order = new List<LineWeekPlayerResult>(list);
                order.Sort((a, b) => b.CareerTotal.CompareTo(a.CareerTotal));
                for (int i = 0; i < order.Count; i++) order[i].Rank = i > 0 && order[i].CareerTotal == order[i - 1].CareerTotal ? order[i - 1].Rank : i + 1;
            }
            r.Players.AddRange(list);
            return r;
        }

        static LineMockScenario Results(bool en, int players, bool closed, bool nobody, bool pick = false)
        {
            var s = new LineMockScenario();
            Setup(s, en, true, null, false, pick ? 2 : 0, null, closed);
            _week++;
            int week = _week;
            int me = pick ? 2 : 0;
            Func<string, LineView> make = picked =>
            {
                var v = Base(en, players, LinePhase.WeekResults, 5, 40, 4, week);
                v.Me = me;
                v.Result = MakeResult(en, players, closed, nobody, pick, me);
                v.Result.NextManagerPick = picked;
                if (closed) v.Company.Closed = true;
                v.Company.FinalPermille = v.Result.FinalProductivityPermille;
                return v;
            };
            Show(s, 0.3f, () => make(""), true);
            s.Do(1.0f, () =>
            {
                // rematch intents on the lobby view (shown as badges next to "Play again")
                var lobby = new LineLobbyView { WeeksPlayed = 1, Length = WeekLength.Short };
                for (int i = 0; i < LineProtocol.MaxPlayers; i++) lobby.Slots.Add(new LineLobbySlot { Slot = i, Occupied = i < players, Connected = i < players, Name = Names(en)[i], WantsRematch = i == 1 || i == 3 });
                LineClient.I.ApplyLobby(lobby);
            }, false);
            Wait(s, 4.2f);
            if (pick) Show(s, 6.0f, () => make(LineManagerIds.Disiplinci), true);
            return s;
        }

        static LineMockScenario Abandoned(bool en)
        {
            var s = new LineMockScenario();
            Setup(s, en, true);
            _week++;
            int week = _week;
            Show(s, 0.3f, () =>
            {
                var v = Base(en, 4, LinePhase.Abandoned, 1, 0, 2, week);
                v.Meeting = null;
                return v;
            }, true);
            return s;
        }

        // ================================================================== shell

        static LineMockScenario Menu(bool en, int page)
        {
            var s = new LineMockScenario();
            Setup(s, en, false, null, false, 0, page >= 5 ? new MockPrivacy { NeedsConsentPrompt = page == 6 } : null);
            s.Do(0.05f, () => LineClient.I.ResetSession());
            string saved = null;
            switch (page)
            {
                case 0: Wait(s, 0.6f); break;
                case 1:
                    s.Do(0.6f, () =>
                    {
                        saved = PlayerPrefs.GetString("join.recent", "");
                        PlayerPrefs.SetString("join.recent", "192.168.1.24:7777|100.84.12.7:7777|kova-ofis.local:7777");
                        MainMenuScreen.Instance?.ShowJoin(true);
                    }, true);
                    s.Do(2.2f, () => PlayerPrefs.SetString("join.recent", saved ?? ""));
                    break;
                case 2:
                    s.Do(0.6f, () => SettingsScreen.Open((int)SettingsScreen.Tab.Audio), true);
                    s.Do(2.2f, () => SettingsScreen.Open((int)SettingsScreen.Tab.Display), true);
                    s.Do(3.8f, () => SettingsScreen.Open((int)SettingsScreen.Tab.Controls), true);
                    break;
                case 3:
                    for (int p = 0; p < HowToPlayScreen.Pages; p++)
                    {
                        int page2 = p;
                        s.Do(0.6f + p * 1.6f, () => HowToPlayScreen.OpenAt(page2), true);
                    }
                    break;
                case 5:
                    s.Do(0.6f, () => SettingsScreen.Open((int)SettingsScreen.Tab.Privacy), true);
                    break;
                case 6:
                    s.Do(0.6f, () => ConsentScreen.Open(), true);
                    s.Do(2.0f, ConsentScreen.Close);
                    break;
                case 7:
                    for (int p = 0; p < IntroScreen.Cards; p++)
                    {
                        int card = p;
                        s.Do(0.6f + p * 1.6f, () => IntroScreen.OpenAt(card), true);
                    }
                    s.Do(5.6f, IntroScreen.Close);
                    break;
                default:
                    s.Do(0.6f, MainMenuScreen.ShowLanguagePicker, true);
                    break;
            }
            return s;
        }

        static LineMockScenario Lobby(bool en, int seats, bool host, string statusKey)
        {
            var s = new LineMockScenario();
            Setup(s, en, host, null, false, host ? 0 : 2);
            s.Do(0.1f, () =>
            {
                LineClient.I.ClearWeek();
                var names = en ? NamesEn : NamesTr;
                var lobby = new LineLobbyView
                {
                    Length = host ? WeekLength.Short : WeekLength.Full, RecordingsEnabled = true, WeeksPlayed = host ? 0 : 2, StatusKey = statusKey, GroupId = host ? "" : "g-7f3a",
                };
                if (!host) { lobby.Experiments.Add("rec_select=context"); lobby.Experiments.Add("early_submit=carry"); }
                for (int i = 0; i < LineProtocol.MaxPlayers; i++)
                {
                    bool occ = i < seats;
                    lobby.Slots.Add(new LineLobbySlot
                    {
                        Slot = i, Occupied = occ, Connected = occ && (i != 3 || host), Ready = occ && (host ? seats >= 6 || i != 2 : (i == 1 || i == 4 || i == 2)), IsHost = i == 0,
                        IsBot = occ && i == 4, Name = occ ? (i == 4 ? Loc.T("player.bot.4") : names[i]) : "", SessionPoints = host ? 0 : new[] { 215, 100, 135, 235, 75, 175 }[i],
                        WantsRematch = !host && (i == 1 || i == 3),
                    });
                }
                LineClient.I.ApplyLobby(lobby);
                var hc = HostLineController.Current;
                if (hc != null)
                    foreach (var ls in lobby.Slots)
                    {
                        var hs = hc.Slots[ls.Slot];
                        hs.Occupied = ls.Occupied; hs.Connected = ls.Connected; hs.Ready = ls.Ready || ls.IsHost; hs.IsHost = ls.IsHost; hs.Virtual = ls.IsBot; hs.Name = ls.Name;
                    }
            });
            Wait(s, 0.9f);
            return s;
        }

        /// <summary>The playtest bookmark: flag button and category picker in a meeting.</summary>
        static LineMockScenario Moment(bool en)
        {
            var s = new LineMockScenario();
            Setup(s, en, false, null, false, 0, new MockPrivacy());
            _week++;
            int week = _week;
            Show(s, 0.3f, () => { var v = Base(en, 4, LinePhase.MeetingDiscussion, 12, 38, 1, week); Meeting(v, false); return v; }, true);
            s.Do(1.0f, MomentMarker.Toggle, true);
            s.Do(2.4f, () => Modal.CloseAll());
            return s;
        }
    }

    /// <summary>
    /// Mock 3D room for screenshots: a flat stand-in drawn under the UI (wall, table, seats, the projector screen with
    /// the real RenderTexture) so the "in the room" layout and the projector texture can be checked without the world.
    /// </summary>
    public sealed class MockRoom : IRoomPresenter
    {
        static MockRoom _i;
        VisualElement _layer, _screen;
        PovMode _mode = PovMode.Meeting;
        Texture _tex;

        public static void Set(bool on)
        {
            if (!on)
            {
                if (_i != null) { _i._layer?.RemoveFromHierarchy(); _i = null; }
                if (Rooms.Presenter is MockRoom) Rooms.Presenter = null;
                return;
            }
            if (_i == null) _i = new MockRoom();
            Rooms.Presenter = _i;
            _i.Draw();
        }

        static void Fill(VisualElement e)
        {
            e.style.position = Position.Absolute;
            e.style.left = 0; e.style.top = 0; e.style.right = 0; e.style.bottom = 0;
        }

        public static void PressTab()
        {
            // what the player's Tab does (MeetingUi listens to GameInput.Phone): emulate through the presenter mode
            if (_i == null) return;
            MeetingUi.Current?.ToggleScreen();
        }

        void Draw()
        {
            var root = UIRoot.I != null ? UIRoot.I.RootElement : null;
            if (root == null) return;
            _layer?.RemoveFromHierarchy();
            _layer = Box("mock-room").NoPick();
            Fill(_layer);
            _layer.style.backgroundColor = (Color)new Color32(0x3A, 0x45, 0x5E, 255);
            root.Insert(0, _layer);
            _screen = Box("mock-room-screen").NoPick().AddTo(_layer);
            _screen.style.position = Position.Absolute;
            _screen.style.left = Length.Percent(27); _screen.style.width = Length.Percent(46);
            _screen.style.top = Length.Percent(9); _screen.style.height = Length.Percent(46);
            _screen.style.backgroundColor = (Color)new Color32(0xFB, 0xF8, 0xF0, 255);
            _screen.style.borderTopWidth = _screen.style.borderBottomWidth = _screen.style.borderLeftWidth = _screen.style.borderRightWidth = 6;
            _screen.style.borderTopColor = _screen.style.borderBottomColor = _screen.style.borderLeftColor = _screen.style.borderRightColor = Navy;
            _screen.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            _screen.style.rotate = new Rotate(new Angle(-1.5f, AngleUnit.Degree));
            var table = Box("mock-room-table").NoPick().AddTo(_layer);
            table.style.position = Position.Absolute;
            table.style.left = Length.Percent(4); table.style.right = Length.Percent(4);
            table.style.bottom = -260; table.style.height = 560;
            table.style.backgroundColor = (Color)new Color32(0x96, 0x68, 0x3E, 255);
            table.style.borderTopLeftRadius = table.style.borderTopRightRadius = table.style.borderBottomLeftRadius = table.style.borderBottomRightRadius = 600;
            if (_tex != null) _screen.style.backgroundImage = new StyleBackground(Background.FromRenderTexture((RenderTexture)_tex));
        }

        public void EnterOffice(int localSlot, int colorIndex, string playerName) { }
        public void EnterMeeting(int localSlot, IReadOnlyList<MeetingSeatInfo> seats, string managerProfileId) { }
        public PovMode Mode => _mode;
        public void SetPovMode(PovMode mode) { _mode = mode; }
        public void SetPhoneState(PhoneLineState state) { }
        public void SetMonitorState(MonitorState state) { }
        public void SetTimeOfDay(float t01) { }
        public void SetSpeaking(int slot, float level) { }
        public void SetSpotlight(int slot) { }
        public void React(int slot, MeetingReaction reaction) { }
        public void SetManagerTalking(bool talking) { }
        public void SetFired(int slot) { }
        public void SetProjectorTexture(Texture texture)
        {
            _tex = texture;
            if (_screen != null && texture is RenderTexture rt) _screen.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
        }
        public Camera PovCamera => Camera.main;
    }
}
