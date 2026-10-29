using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// UI mock scenarios of the meeting + shell track (<c>-uimock "meeting.*" | "final.*" | "results.*" | "menu.*" | "lobby.*"</c>),
    /// each in Turkish and English, with 3 and 6 colleagues. Views are built when their step runs (times relative to
    /// <see cref="LineMock.Now"/>). A scenario sets everything global it relies on (language, host, voice, room).
    /// </summary>
    public static class MeetingMocks
    {
        static int _week = 100;
        static long _version = 1;

        static readonly string[] NamesTr = { "Ayşe", "Burak", "Ece", "Kaan", "Zeynep", "Mert" };
        static readonly string[] NamesEn = { "Alex", "Sam", "Jordan", "Riley", "Casey", "Morgan" };
        static readonly int[] Blames = { 6, 9, 12, 5, 8, 4 };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Register()
        {
            foreach (var en in new[] { false, true })
            {
                string L = en ? ".en" : ".tr";
                // ---- meeting
                LineMock.Register("meeting.dossier_cover" + L, () => Dossier(en, en ? 3 : 6, DossierShot.Cover));
                LineMock.Register("meeting.dossier_tape" + L, () => Dossier(en, en ? 3 : 6, DossierShot.Tape));
                LineMock.Register("meeting.dossier_rounds" + L, () => Dossier(en, en ? 6 : 3, DossierShot.Rounds));
                LineMock.Register("meeting.dossier_incidents" + L, () => Dossier(en, 6, DossierShot.Incidents));
                LineMock.Register("meeting.discussion" + L, () => Discussion(en, en ? 3 : 6, false));
                LineMock.Register("meeting.vote" + L, () => Vote(en, en ? 3 : 6));
                LineMock.Register("meeting.verdict" + L, () => Verdict(en, en ? 3 : 6, true));
                LineMock.Register("meeting.verdict_none" + L, () => Verdict(en, 6, false));
                LineMock.Register("meeting.room" + L, () => Discussion(en, 6, true));
                LineMock.Register("meeting.pause" + L, () => Pause(en));
                // ---- final
                LineMock.Register("final.shortlist" + L, () => FinalDiscussion(en, en ? 3 : 6));
                LineMock.Register("final.defense" + L, () => Defense(en, en ? 3 : 6));
                LineMock.Register("final.vote" + L, () => FinalVote(en, en ? 3 : 6));
                LineMock.Register("final.verdict" + L, () => FinalVerdict(en, en ? 4 : 6));
                // ---- results
                LineMock.Register("results.week" + L, () => Results(en, en ? 3 : 6, false));
                LineMock.Register("results.pick" + L, () => Results(en, en ? 6 : 4, true));
                LineMock.Register("results.abandoned" + L, () => Abandoned(en));
                // ---- shell
                LineMock.Register("menu.main" + L, () => Menu(en, 0));
                LineMock.Register("menu.join" + L, () => Menu(en, 1));
                LineMock.Register("menu.settings" + L, () => Menu(en, 2));
                LineMock.Register("menu.howto" + L, () => Menu(en, 3));
                LineMock.Register("lobby.host" + L, () => Lobby(en, en ? 3 : 6, true));
                LineMock.Register("lobby.client" + L, () => Lobby(en, 4, false));
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

        /// <summary>Resets globals and switches the language (the UI rebuilds next frame).</summary>
        static void Setup(LineMockScenario s, bool en, bool host = false, IVoiceUi voice = null, bool room = false, int me = 0)
        {
            s.Me = me;
            s.Do(0f, () =>
            {
                PauseMenu.Close();
                if (MainMenuScreen.Instance != null && MainMenuScreen.Instance.JoinOpen) MainMenuScreen.Instance.ShowJoin(false);
                SettingsScreen.Close();
                HowToPlayScreen.Close();
                Modal.CloseAll();
                HostLineController.Current = host ? new HostLineController() : null;
                UiServices.Override = voice;
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

        static LineView Base(bool en, int players, LinePhase phase, double elapsed, double left, int day = 1, int week = 0)
        {
            double now = LineMock.Now;
            var v = LineMock.BaseView(phase, players, day, left);
            v.WeekId = week != 0 ? week : _week;
            v.PhaseStartTime = now - elapsed;
            v.PhaseEndTime = now + left;
            v.DayCount = 5;
            v.ManagerProfileId = LineManagerIds.Paranoyak;
            var names = en ? NamesEn : NamesTr;
            for (int i = 0; i < v.Players.Count; i++)
            {
                var p = v.Players[i];
                p.Name = names[i];
                p.Blame = Blames[i];
                p.Ihtars = i == 2 ? 1 : 0;
                p.IsBot = i == 4;
                p.SessionPoints = new[] { 7, 3, 5, 9, 2, 4 }[i];
            }
            v.TodayRounds.Clear();
            v.TodayRounds.Add(RoundType.PrimHavuzu);
            v.TodayRounds.Add(RoundType.FarkliEposta);
            return v;
        }

        static LineMeetingView Meeting(LineView v, bool final, double dossierStart, bool lateTape)
        {
            var m = new LineMeetingView { IsFinal = final };
            v.Meeting = m;
            int n = v.Players.Count;
            // today's rounds
            var prim = new LineRoundReveal { Type = RoundType.PrimHavuzu, Index = 0 };
            prim.Pairs.Add(new LinePairReveal { A = 0, B = 1, ChoiceA = 0, ChoiceB = 1 });
            if (n >= 4) prim.Pairs.Add(new LinePairReveal { A = 2, B = 3, ChoiceA = 0, ChoiceB = 0 });
            if (n >= 6) prim.Pairs.Add(new LinePairReveal { A = 4, B = 5, ChoiceA = 1, ChoiceB = 1 });
            if (n == 3) prim.Pairs.Add(new LinePairReveal { A = 2, B = LineIds.Intern, ChoiceA = 0, ChoiceB = 0 });
            prim.Deltas.Add(new LineBlameDelta { Player = 0, Delta = 2 });
            prim.Deltas.Add(new LineBlameDelta { Player = 1, Delta = -4 });
            if (n >= 4) { prim.Deltas.Add(new LineBlameDelta { Player = 2, Delta = -1 }); prim.Deltas.Add(new LineBlameDelta { Player = 3, Delta = -1 }); }
            if (n >= 6) { prim.Deltas.Add(new LineBlameDelta { Player = 4, Delta = 1 }); prim.Deltas.Add(new LineBlameDelta { Player = 5, Delta = 1 }); }
            v.TodayReveals.Add(prim);
            var odd = new LineRoundReveal { Type = RoundType.FarkliEposta, Index = 1, Hidden = 2, Winner = 2, TopicId = 4, OddGuess = 7, OddGuessCorrect = false };
            for (int i = 0; i < n; i++) if (i != 2) odd.Votes.Add(new LineVoteView { Voter = i, Target = i == 1 ? 0 : 2 });
            odd.Deltas.Add(new LineBlameDelta { Player = 2, Delta = 3 });
            odd.Deltas.Add(new LineBlameDelta { Player = 0, Delta = -1 });
            if (n >= 4) odd.Deltas.Add(new LineBlameDelta { Player = 3, Delta = -1 });
            v.TodayReveals.Add(odd);

            // dossier
            m.Dossier.Add(new LineDossierEntry { Kind = DossierKind.RoundOutcome, Round = RoundType.PrimHavuzu, Player = 1, Arg = 0 });
            m.Dossier.Add(new LineDossierEntry { Kind = DossierKind.RoundOutcome, Round = RoundType.FarkliEposta, Player = 2, Arg = 1 });
            m.Dossier.Add(new LineDossierEntry { Kind = DossierKind.TrickTrace, Trick = TrickType.KahveDok, Victim = 1, Named = false });
            if (n >= 4) m.Dossier.Add(new LineDossierEntry { Kind = DossierKind.TrickTrace, Trick = TrickType.FisCek, Victim = 3, Player = n >= 6 ? 4 : 0, Named = true });
            m.Dossier.Add(new LineDossierEntry { Kind = DossierKind.TipResult, Player = n >= 6 ? 5 : 1, Arg = 0, Victim = 0 });
            if (n >= 6) m.Dossier.Add(new LineDossierEntry { Kind = DossierKind.ComplaintLeak, Player = 2, Victim = 1 });
            m.Dossier.Add(new LineDossierEntry { Kind = DossierKind.Recording, Arg = 0 });

            // one recording (v3.0 engines play it 4 s in; "lateTape" models the v3.1 climax at the end)
            double playAt = lateTape ? dossierStart + 22 : dossierStart + 4;
            var pick = new LineRecordingPick { Index = 0, PlayAt = playAt, Seconds = 7f, ChosenCallId = 12, Caller = 1, Callee = 3 % n, CallStart = dossierStart - 140 };
            pick.CandidateCallIds.Add(12);
            m.Recordings.Add(pick);
            return m;
        }

        // ================================================================== meeting

        enum DossierShot { Cover, Tape, Rounds, Incidents }

        static LineMockScenario Dossier(bool en, int players, DossierShot shot)
        {
            var s = new LineMockScenario();
            Setup(s, en);
            _week++;
            int week = _week;
            Show(s, 0.3f, () =>
            {
                // dossier = 25 + 9 s; place "now" where the wanted slide is on screen when the shot is taken (+1.2 s)
                double elapsed;
                bool late = shot == DossierShot.Rounds || shot == DossierShot.Incidents;
                switch (shot)
                {
                    case DossierShot.Cover: elapsed = 0.5; break;
                    case DossierShot.Tape: elapsed = 6.0; break; // tape at +4 s: 3.2 s in when the shot is taken
                    case DossierShot.Rounds: elapsed = 7.5; break;
                    default: elapsed = 13.5; break;
                }
                var v = Base(en, players, LinePhase.MeetingDossier, elapsed, 34 - elapsed, 1, week);
                Meeting(v, false, LineMock.Now - elapsed, late);
                return v;
            }, true);
            return s;
        }

        static LineMockScenario Discussion(bool en, int players, bool room)
        {
            var voice = new FakeVoice();
            voice.Speaking.Add(1);
            voice.Speaking.Add(players > 3 ? 3 : 2);
            var s = new LineMockScenario();
            Setup(s, en, false, voice, room);
            _week++;
            int week = _week;
            Show(s, 0.3f, () =>
            {
                var v = Base(en, players, LinePhase.MeetingDiscussion, 20, 55, 1, week);
                Meeting(v, false, LineMock.Now - 54, false);
                v.Meeting.Recordings[0].Played = true;
                return v;
            }, true);
            if (room)
            {
                // Tab: the readable projector view in the room
                s.Do(2.0f, () => MockRoom.PressTab());
                Wait(s, 2.1f);
            }
            return s;
        }

        static LineMockScenario Vote(bool en, int players)
        {
            var s = new LineMockScenario();
            Setup(s, en);
            _week++;
            int week = _week;
            Func<bool, LineView> make = voted =>
            {
                var v = Base(en, players, LinePhase.MeetingVote, 6, 19, 2, week);
                Meeting(v, false, LineMock.Now - 150, false);
                for (int i = 0; i < v.Players.Count; i++) v.Players[i].Voted = i == 1 || i == 4 || (voted && i == 0);
                if (voted) v.Meeting.MyVote = 1;
                return v;
            };
            Show(s, 0.3f, () => make(false), true);
            Show(s, 2.0f, () => make(true), true);
            return s;
        }

        static LineMockScenario Verdict(bool en, int players, bool someone)
        {
            var s = new LineMockScenario();
            Setup(s, en);
            _week++;
            int week = _week;
            Show(s, 0.3f, () =>
            {
                var v = Base(en, players, LinePhase.MeetingVerdict, 0.2, 7.8, 2, week);
                Meeting(v, false, LineMock.Now - 160, false);
                var m = v.Meeting;
                if (someone)
                {
                    m.IhtarTo = 1;
                    v.Players[1].Ihtars = 1;
                    for (int i = 0; i < players; i++)
                        if (i != 1) m.Votes.Add(new LineVoteView { Voter = i, Target = i == 3 ? 0 : (i == 5 ? -1 : 1) });
                    m.Votes.Add(new LineVoteView { Voter = 1, Target = 0 });
                }
                else
                {
                    // the plurality picked Ece, who already has a warning (v3.1: nobody gets one)
                    m.Votes.Add(new LineVoteView { Voter = 0, Target = 2 });
                    m.Votes.Add(new LineVoteView { Voter = 1, Target = 2 });
                    m.Votes.Add(new LineVoteView { Voter = 3, Target = 2 });
                    m.Votes.Add(new LineVoteView { Voter = 2, Target = 4 });
                    m.Votes.Add(new LineVoteView { Voter = 4, Target = 0 });
                    m.Votes.Add(new LineVoteView { Voter = 5, Target = -1 });
                }
                return v;
            }, true);
            return s;
        }

        static LineMockScenario Pause(bool en)
        {
            var s = new LineMockScenario();
            Setup(s, en);
            _week++;
            int week = _week;
            Show(s, 0.3f, () =>
            {
                var v = Base(en, 6, LinePhase.MeetingDiscussion, 20, 55, 1, week);
                Meeting(v, false, LineMock.Now - 54, false);
                return v;
            });
            s.Do(0.8f, PauseMenu.Open, true);
            return s;
        }

        // ================================================================== final

        static void FinalSetup(LineView v, int players)
        {
            var m = v.Meeting;
            // shortlist: top 2 at 3–4 players, top 3 at 5–6 (blame desc)
            if (players >= 5) { m.Shortlist.Add(2); m.Shortlist.Add(1); m.Shortlist.Add(4); }
            else { m.Shortlist.Add(2); m.Shortlist.Add(1); }
            m.DefenseOrder.AddRange(m.Shortlist);
            v.Day = 4;
            v.Features.IsLastDay = true;
        }

        static LineMockScenario FinalDiscussion(bool en, int players)
        {
            var s = new LineMockScenario();
            var voice = new FakeVoice();
            voice.Speaking.Add(2);
            Setup(s, en, false, voice);
            _week++;
            int week = _week;
            Show(s, 0.3f, () =>
            {
                var v = Base(en, players, LinePhase.MeetingDiscussion, 10, 50, 4, week);
                Meeting(v, true, LineMock.Now - 44, true);
                FinalSetup(v, players);
                return v;
            }, true);
            return s;
        }

        static LineMockScenario Defense(bool en, int players)
        {
            var voice = new FakeVoice();
            voice.Speaking.Add(2);
            var s = new LineMockScenario();
            Setup(s, en, false, voice, false, en ? 2 : 0);
            _week++;
            int week = _week;
            Show(s, 0.3f, () =>
            {
                var v = Base(en, players, LinePhase.FinalDefense, 12, 78, 4, week);
                v.Me = en ? 2 : 0;
                Meeting(v, true, LineMock.Now - 120, true);
                FinalSetup(v, players);
                v.Meeting.Defender = 2;
                v.Meeting.DefenderEndTime = LineMock.Now + 18;
                return v;
            }, true);
            return s;
        }

        static LineMockScenario FinalVote(bool en, int players)
        {
            var s = new LineMockScenario();
            Setup(s, en, false, null, false, en ? 1 : 0);
            _week++;
            int week = _week;
            Func<bool, LineView> make = voted =>
            {
                var v = Base(en, players, LinePhase.FinalVote, 5, 25, 4, week);
                v.Me = en ? 1 : 0;
                Meeting(v, true, LineMock.Now - 200, true);
                FinalSetup(v, players);
                for (int i = 0; i < v.Players.Count; i++) v.Players[i].Voted = i == 3 || (voted && i == v.Me);
                if (voted) v.Meeting.MyVote = 2;
                return v;
            };
            Show(s, 0.3f, () => make(false), true);
            Show(s, 2.0f, () => make(true), true);
            return s;
        }

        static LineMockScenario FinalVerdict(bool en, int players)
        {
            var s = new LineMockScenario();
            Setup(s, en);
            _week++;
            int week = _week;
            Show(s, 0.3f, () =>
            {
                var v = Base(en, players, LinePhase.FinalVerdict, 0.2, 11.8, 4, week);
                Meeting(v, true, LineMock.Now - 260, true);
                FinalSetup(v, players);
                var m = v.Meeting;
                // votes: 2 → Ece (fired), others split
                int[] targets = players >= 6 ? new[] { 2, 2, 1, 2, 1, 2 } : new[] { 2, 2, 1, 1 };
                for (int i = 0; i < players; i++) m.Votes.Add(new LineVoteView { Voter = i, Target = targets[i] == i ? (i == 1 ? 2 : 1) : targets[i] });
                foreach (int id in m.Shortlist)
                {
                    int votes = 0;
                    foreach (var x in m.Votes) if (x.Target == id) votes++;
                    m.Scores.Add(new LineFireScore { Player = id, Blame = v.Players[id].Blame, Ihtars = v.Players[id].Ihtars, Votes = votes, Score = votes });
                }
                m.Fired = 2;
                return v;
            }, true);
            Wait(s, 4.5f);
            return s;
        }

        // ================================================================== week results

        static LineMockScenario Results(bool en, int players, bool meFired)
        {
            var s = new LineMockScenario();
            Setup(s, en, !meFired, null, false, meFired ? 2 : 0);
            _week++;
            int week = _week;
            Func<string, LineView> make = pick =>
            {
                var v = Base(en, players, LinePhase.WeekResults, 1, 44, 4, week);
                v.Me = meFired ? 2 : 0;
                var r = new LineWeekResult { WeekId = week, ManagerProfileId = LineManagerIds.Paranoyak, Days = 5, Fired = 2, NextManagerPick = pick };
                var cfg = LineConfig.CreateDefault();
                for (int i = 0; i < players; i++)
                {
                    int target = (i + 2) % players;
                    var p = new LineWeekPlayerResult
                    {
                        Player = i, Blame = v.Players[i].Blame, Ihtars = v.Players[i].Ihtars, Survived = i != 2, AgendaTarget = target,
                        AgendaIhtars = target == 2 ? 1 : (target == 1 ? 1 : 0), EmployeeOfWeek = i == (players >= 6 ? 5 : 0),
                        Trick = (TrickType)(1 + i % 5), TricksUsed = i % 3,
                    };
                    p.AgendaHit = p.AgendaIhtars > 0 || target == 2;
                    p.Points = (p.Survived ? cfg.SurvivePoints : 0) + cfg.AgendaPointsPerIhtar * p.AgendaIhtars + (target == 2 ? cfg.AgendaPoints : 0) + (p.EmployeeOfWeek ? cfg.EmployeeOfWeekPoints : 0);
                    p.SessionPoints = v.Players[i].SessionPoints + p.Points;
                    r.Players.Add(p);
                }
                v.Result = r;
                v.Meeting = null;
                return v;
            };
            Show(s, 0.3f, () => make(""), true);
            Wait(s, 4.0f);
            if (meFired) Show(s, 6.0f, () => make(LineManagerIds.Disiplinci), true);
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
            Setup(s, en);
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
                    s.Do(0.6f, () => SettingsScreen.Open((int)SettingsScreen.Tab.Display), true);
                    s.Do(2.2f, () => SettingsScreen.Open((int)SettingsScreen.Tab.Controls), true);
                    break;
                case 3:
                    s.Do(0.6f, () => HowToPlayScreen.OpenAt(0), true);
                    s.Do(2.2f, () => HowToPlayScreen.OpenAt(5), true);
                    s.Do(3.8f, () => HowToPlayScreen.OpenAt(6), true);
                    break;
                default:
                    s.Do(0.6f, MainMenuScreen.ShowLanguagePicker, true);
                    break;
            }
            return s;
        }

        static LineMockScenario Lobby(bool en, int seats, bool host)
        {
            var s = new LineMockScenario();
            Setup(s, en, host, null, false, host ? 0 : 2);
            s.Do(0.1f, () =>
            {
                LineClient.I.ClearWeek();
                var names = en ? NamesEn : NamesTr;
                var lobby = new LineLobbyView { Length = host ? WeekLength.Short : WeekLength.Full, RecordingsEnabled = true, WeeksPlayed = host ? 0 : 2, StatusKey = host ? "" : "lobby.need_ready" };
                for (int i = 0; i < LineProtocol.MaxPlayers; i++)
                {
                    bool occ = i < seats;
                    lobby.Slots.Add(new LineLobbySlot
                    {
                        Slot = i, Occupied = occ, Connected = occ && (i != 3 || host), Ready = occ && (host ? seats >= 6 || i != 2 : (i == 1 || i == 4 || i == 2)), IsHost = i == 0,
                        IsBot = occ && i == 4, Name = occ ? (i == 4 ? Loc.T("player.bot.4") : names[i]) : "", SessionPoints = host ? 0 : new[] { 7, 3, 5, 9, 2, 4 }[i],
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
