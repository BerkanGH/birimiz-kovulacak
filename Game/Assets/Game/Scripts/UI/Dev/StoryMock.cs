using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using static BirimizKovulacak.Game.MeetingMockScenarios;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5.2 scenarios for the UI mock (docs/V5_DESIGN.md §4.1, §5; docs/UI.md "v5.2 on screen"), each with an "_en" sample:
    /// <c>-uimock "v5.meeting_declare*"</c> the candidate's prim declaration card (my real prim, an understatement, put away
    /// as a chip, sealed), the sealed note on the projector, then "PRİM BEYANLARI" revealed (from the seat, zoomed, the
    /// firing vote and the defence with the claims, a colleague's doubt, the folder's Dosya);
    /// <c>"v5.meeting_penalty*"</c> the short shift's "NÖBET CEZASI" slide that opens the crisis vote;
    /// <c>"v5.meeting_awards*"</c> the results: ranking, the "ÖDÜLLER" slide, "Senin unvanın" on the results bar, the folder's
    /// Manşetler / Ödüller / Beyanlar and the desk openings with their stamps;
    /// <c>"v5.lobby_options*"</c> the lobby: fewer than four seats (start disabled with the hint), the talk-time option.
    /// (The desk opening with its YALAN / DOĞRU stamp is "v5.meeting_reveal*" in <see cref="MeetingMock3bScenarios"/>.)
    /// </summary>
    public static class StoryMockScenarios
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Register()
        {
            foreach (bool en in new[] { false, true })
            {
                bool e = en;
                string s = en ? "_en" : "";
                LineMock.Register("v5.meeting_declare" + s, () => Declare(e));
                LineMock.Register("v5.meeting_declare_reveal" + s, () => DeclareReveal(e));
                LineMock.Register("v5.meeting_penalty" + s, () => Penalty(e));
                LineMock.Register("v5.meeting_awards" + s, () => Awards(e));
                LineMock.Register("v5.lobby_options" + s, () => Lobby(e));
            }
        }

        static LineMockScenario Start(bool en) { return new LineMockScenario().Do(0f, () => { if (en) English(); }); }
        static float T0(bool en) { return en ? 0.3f : 0f; }
        static LineView Named(LineView v, bool en) { if (en) LineMock.SetNames(v, true); return v; }

        static void LookAtProjector()
        {
            float y, p;
            var rooms = Presenter();
            if (rooms != null && rooms.TryGetProjectorLook(out y, out p)) rooms.SetLook(y, p);
        }

        // ------------------------------------------------------------------ the declaration card

        /// <summary>The crisis verdict just named Sarı and me (Mavi) candidates; I have 12 prim.</summary>
        static LineView VerdictCandidate(bool en, int mine, bool open)
        {
            var v = LineMock.BaseView(LinePhase.CrisisVerdict, 4, 2, 7);
            v.PhaseStartTime = LineMock.Now - 1;
            foreach (var p in v.Players) p.Blame = new[] { 6, 9, 2, 0 }[p.Id];
            v.Meeting = new LineMeetingView { Index = 2 };
            v.Final = Final(new[] { 1, 0 }, 1);
            v.Final.Crisis.Clear();
            v.Final.Crisis.Add(new LineBlameGainView { Player = 1, Amount = 4 });
            v.Final.DeclareUntil = v.PhaseEndTime + 12;
            v.Final.MyDeclaration = mine;
            v.Features.DeclareOpen = open;
            v.Mine.Prim = 12;
            Candidates(v);
            return Named(v, en);
        }

        /// <summary>The final discussion of a 6-player match (candidates Pembe, Sarı, Yeşil): declarations sealed or revealed.</summary>
        static LineView Final6(bool en, LinePhase phase, bool revealed)
        {
            LineView v;
            if (phase == LinePhase.FinalVote) v = FinalVote(6);
            else if (phase == LinePhase.FinalDefense) v = Defense(6);
            else
            {
                v = LineMock.BaseView(LinePhase.FinalDiscussion, 6, 2, 30);
                foreach (var p in v.Players) p.Blame = new[] { 3, 8, 11, 7, 2, 1 }[p.Id];
                v.Meeting = new LineMeetingView { Index = 2 };
                v.Final = Final(new[] { 2, 1, 3 }, 2);
                Candidates(v);
            }
            v.Final.DeclareUntil = revealed ? LineMock.Now - 3 : LineMock.Now + 9;
            v.Final.DeclarationsRevealed = revealed;
            if (revealed)
            {
                v.Final.Declarations.Add(new LineDeclarationView { Player = 2, Amount = 8 });
                v.Final.Declarations.Add(new LineDeclarationView { Player = 1, Amount = 14 });
                v.Final.Declarations.Add(new LineDeclarationView { Player = 3, Amount = -1 });
            }
            return Named(v, en);
        }

        static LineMockScenario Declare(bool en)
        {
            float t = T0(en);
            return Start(en)
                // the card comes up with my real prim; an understatement; put away (the chip); sealed
                .Do(t, () => { Reset(); Show(VerdictCandidate(en, -1, true)); Aim(0f, 6f); }, true)
                .Do(t + 1.8f, () => { if (MeetingUi.I != null) MeetingUi.I.Declare.Amount = 7; }, 0.4f)
                .Do(t + 3.0f, () => MeetingUi.I?.Back(), 0.6f)
                .Do(t + 4.4f, () => Show(VerdictCandidate(en, 7, false)), 0.6f)
                // a colleague who is not a candidate: the sealed note and its countdown on the projector
                .Do(t + 5.8f, () => { Reset(); Show(Final6(en, LinePhase.FinalDiscussion, false)); MeetingProjector.MockDeckIndex = 0; MeetingUi.I?.OpenProjector(); }, true)
                .Do(t + 7.6f, () => { MeetingProjector.MockDeckIndex = -1; MeetingUi.I?.CloseAll(); });
        }

        static LineMockScenario DeclareReveal(bool en)
        {
            float t = T0(en);
            return Start(en)
                .Do(t, () => { Reset(); Show(Final6(en, LinePhase.FinalDiscussion, false)); LookAtProjector(); })
                // the reveal happens in front of us: the slide jumps in and the numbers pop
                .Do(t + 1.2f, () =>
                {
                    Show(Final6(en, LinePhase.FinalDiscussion, true));
                    LineClient.I.ApplyEvents(new List<LineEvent> { new LineEvent { Type = LineEventType.DeclarationsRevealed, A = 2, B = 3, Time = LineMock.Now } });
                }, 2.6f)
                .Do(t + 4.4f, () => MeetingUi.I?.OpenProjector(), true)
                // the firing vote and the defence carry the claims
                .Do(t + 6.2f, () => { Show(Final6(en, LinePhase.FinalVote, true)); MeetingUi.I?.OpenProjector(); }, true)
                .Do(t + 8.0f, () => { Show(Final6(en, LinePhase.FinalDefense, true)); Speak(1); MeetingUi.I?.OpenProjector(); }, true)
                // a colleague whose count is well above a claim says so; the folder's Dosya lists the declarations
                .Do(t + 9.8f, () =>
                {
                    MeetingUi.I?.CloseAll();
                    Show(Final6(en, LinePhase.FinalDiscussion, true));
                    AimAtSlot(4);
                    var lc = LineClient.I;
                    if (MeetingUi.I != null && lc != null && lc.View != null)
                        MeetingUi.I.Subs.Say(lc, lc.View, 4, BotSpeech.Text("bot.declare.doubt.h", new List<int> { 2, 13 }));
                }, 1.6f)
                .Do(t + 12.0f, () => MeetingUi.I?.OpenFolder(MeetingFolder.Tab.Dossier), true);
        }

        // ------------------------------------------------------------------ the short shift's penalty opens the crisis vote

        static LineView CrisisVotePenalty(bool en)
        {
            var v = CrisisVote(6);
            v.PhaseStartTime = LineMock.Now;
            v.PhaseEndTime = LineMock.Now + 20;
            var c = LineConfig.CreateDefault();
            foreach (var p in v.Players) p.Blame = new[] { 1, 6, 4, 2, 5, 3 }[p.Id];
            v.Meeting.Roster.AddRange(new[] { 0, 3 });
            v.Meeting.Dossier.Add(new LineDossierEntry { Kind = DossierKind.ShiftRoster, Task = TaskKind.WeekendShift, N1 = 2, N2 = LineConfig.ShiftNeeded(6), Time = LineMock.Now });
            v.Meeting.Dossier.Add(new LineDossierEntry { Kind = DossierKind.ShiftSkipBlame, Task = TaskKind.WeekendShift, N1 = c.ShiftFailSkipBlame, N2 = 4, Time = LineMock.Now });
            return Named(v, en);
        }

        static LineMockScenario Penalty(bool en)
        {
            float t = T0(en);
            return Start(en)
                .Do(t, () => { Reset(); Show(CrisisVotePenalty(en)); LookAtProjector(); }, 0.9f)
                .Do(t + 1.4f, () => MeetingUi.I?.OpenProjector(), 1.0f)
                // then the crisis vote's own slide
                .Do(t + 6.6f, () => { }, 0.5f);
        }

        // ------------------------------------------------------------------ the results: awards and headlines

        /// <summary>
        /// A 6-player match with a story. Sarı took the whole Client Bonus from me (Mavi), paid me 1 prim to keep quiet and I
        /// investigated anyway (me: "Satılmış"); Pembe took the petty cash and paid Yeşil, who kept quiet ("Sözünün Eri");
        /// the crisis went to Sarı; Mor declared the truth (9) and was fired first ("Yanlış Adam"), Sarı declared 8 with 12
        /// ("En Pahalı Yalan") and was fired second; Pembe wins with 18.
        /// </summary>
        static LineView StoryResults(bool en, double startedAgo)
        {
            var v = LineMock.BaseView(LinePhase.Results, 6, 2, 45);
            v.PhaseStartTime = LineMock.Now - startedAgo;
            v.PhaseEndTime = v.PhaseStartTime + 45;
            var r = new LineWeekResult { WeekId = 1, MatchId = "mock", PlayerCount = 6, ResultHash = 520520 };
            int[] prim = { 15, 0, 18, 15, 14, 0 };
            int[] blame = { 1, 9, 0, 1, 3, 5 };
            for (int i = 0; i < 6; i++)
                r.Players.Add(new LineWeekPlayerResult { Player = i, Prim = prim[i], Blame = blame[i], Fired = i == 1 || i == 5, Winner = i == 2, WasCandidate = i == 1 || i == 4 || i == 5 });
            r.Winners.Add(2);
            r.Fired.Add(5); r.Fired.Add(1);
            // groups: Client Bonus (Mavi–Sarı, Pembe–Mor, Yeşil–Turuncu), Petty Cash (Mavi–Turuncu, Sarı–Mor, Pembe–Yeşil)
            r.Groups.Add(new LineGroupView { TaskNumber = 0, Task = TaskKind.ClientBonus, Members = new List<int> { 0, 1 } });
            r.Groups.Add(new LineGroupView { TaskNumber = 0, Task = TaskKind.ClientBonus, Members = new List<int> { 2, 5 } });
            r.Groups.Add(new LineGroupView { TaskNumber = 0, Task = TaskKind.ClientBonus, Members = new List<int> { 3, 4 } });
            r.Groups.Add(new LineGroupView { TaskNumber = 1, Task = TaskKind.PettyCash, Members = new List<int> { 0, 4 } });
            r.Groups.Add(new LineGroupView { TaskNumber = 1, Task = TaskKind.PettyCash, Members = new List<int> { 1, 5 } });
            r.Groups.Add(new LineGroupView { TaskNumber = 1, Task = TaskKind.PettyCash, Members = new List<int> { 2, 3 } });
            for (int i = 0; i < 6; i++) r.Choices.Add(new LineChoiceView { TaskNumber = 0, Task = TaskKind.ClientBonus, Player = i, Choice = i == 1 ? LineChoice.Take : LineChoice.Share, Payout = i == 1 ? 5 : i == 0 ? 0 : 3 });
            for (int i = 0; i < 6; i++) r.Choices.Add(new LineChoiceView { TaskNumber = 1, Task = TaskKind.PettyCash, Player = i, Choice = i == 2 ? LineChoice.Take : LineChoice.Leave, Payout = i == 2 ? 3 : 0 });
            // the shift was staffed (Mavi, Yeşil, Mor); the others kept the 2 prim
            for (int i = 0; i < 6; i++)
            {
                bool volunteer = i == 0 || i == 3 || i == 5;
                r.Choices.Add(new LineChoiceView { TaskNumber = 2, Task = TaskKind.WeekendShift, Player = i, Choice = volunteer ? LineChoice.Volunteer : LineChoice.Skip, Payout = volunteer ? 0 : 2 });
            }
            double t0 = v.PhaseStartTime - 600;
            r.Transfers.Add(new LineTransferView { From = 1, To = 0, Amount = 1, Phase = LinePhase.Break, Time = t0 + 95 });
            r.Transfers.Add(new LineTransferView { From = 2, To = 3, Amount = 1, Phase = LinePhase.Break, Time = t0 + 190 });
            r.Incidents.Add(new LineDossierEntry { Kind = DossierKind.Investigation, Player = 0, Other = 1, N1 = 1, Time = t0 + 230 });
            for (int i = 0; i < 6; i++)
            {
                r.Ballots.Add(new LineBallotView { Kind = BallotKind.Meeting, Round = 0, Voter = i, Index = 0, Target = i == 5 ? 4 : 5 });
                r.Ballots.Add(new LineBallotView { Kind = BallotKind.Crisis, Voter = i, Target = i == 1 ? 5 : i == 5 ? 4 : 1 });
            }
            foreach (int i in new[] { 0, 1, 4 }) r.Ballots.Add(new LineBallotView { Kind = BallotKind.Final, Round = 0, Voter = i, Target = 5 });
            foreach (int i in new[] { 2, 3, 5 }) r.Ballots.Add(new LineBallotView { Kind = BallotKind.Final, Round = 0, Voter = i, Target = 1 });
            foreach (int i in new[] { 0, 2, 3 }) r.Ballots.Add(new LineBallotView { Kind = BallotKind.Final, Round = 1, Voter = i, Target = 1 });
            r.Ballots.Add(new LineBallotView { Kind = BallotKind.Final, Round = 1, Voter = 4, Target = -1 });
            // the declarations against the truth at the reveal
            r.Declarations.Add(new LineDeclarationView { Player = 1, Amount = 8, Actual = 12 });
            r.Declarations.Add(new LineDeclarationView { Player = 5, Amount = 9, Actual = 9 });
            r.Declarations.Add(new LineDeclarationView { Player = 4, Amount = -1, Actual = 11 });
            var first = MeetingMock3bScenarios.Firing(5, 9, FiringDecision.HigherBlame, new[] { 0, 1, 4 }, 9);
            first.Time = t0 + 520;
            // (Sarı had 12 at the reveal and got 3 of Mor's desk: the lie is judged against the 12)
            var second = MeetingMock3bScenarios.Firing(1, 15, FiringDecision.Plurality, new[] { 0, 2, 3 }, 8, 12);
            second.Round = 1;
            second.Time = t0 + 580;
            r.Firings.Add(first);
            r.Firings.Add(second);
            var why = new[]
            {
                new[] { (int)BlameReason.Vote, 2, (int)BlameReason.ShiftClean, -1 },
                new[] { (int)BlameReason.Vote, 1, (int)BlameReason.InvestigationGuilty, 4, (int)BlameReason.Crisis, 4 },
                new int[0],
                new[] { (int)BlameReason.Vote, 2, (int)BlameReason.ShiftClean, -1 },
                new[] { (int)BlameReason.Vote, 3 },
                new[] { (int)BlameReason.Vote, 6, (int)BlameReason.ShiftClean, -1 },
            };
            for (int i = 0; i < 6; i++)
                for (int k = 0; k < why[i].Length; k += 2)
                    r.BlameLog.Add(new LineBlameChange { Player = i, Reason = (BlameReason)why[i][k], Delta = why[i][k + 1], Time = t0 + 300 + k });
            foreach (var c in r.Choices) if (c.Payout != 0) r.PrimLog.Add(new LinePrimChange { Player = c.Player, Delta = c.Payout, Reason = PrimReason.Task });
            foreach (var x in r.Transfers)
            {
                r.PrimLog.Add(new LinePrimChange { Player = x.From, Delta = -x.Amount, Reason = PrimReason.TransferOut, Other = x.To });
                r.PrimLog.Add(new LinePrimChange { Player = x.To, Delta = x.Amount, Reason = PrimReason.TransferIn, Other = x.From });
            }
            foreach (var f in r.Firings)
            {
                r.PrimLog.Add(new LinePrimChange { Player = f.Player, Delta = -f.Desk, Reason = PrimReason.Fired });
                foreach (int id in f.Recipients) r.PrimLog.Add(new LinePrimChange { Player = id, Delta = f.Share, Reason = PrimReason.DeskShare, Other = f.Player });
            }
            v.Results = r;
            v.Final = Final(new[] { 1, 5, 4 }, 2);
            v.Final.Crisis.Clear();
            v.Final.Crisis.Add(new LineBlameGainView { Player = 1, Amount = 4 });
            v.Final.Firings.AddRange(r.Firings);
            v.Final.DeclarationsRevealed = true;
            foreach (var d in r.Declarations) v.Final.Declarations.Add(new LineDeclarationView { Player = d.Player, Amount = d.Amount });
            foreach (var p in v.Players) { p.Fired = r.Fired.Contains(p.Id); p.Blame = blame[p.Id]; p.Candidate = p.Id == 1 || p.Id == 4 || p.Id == 5; }
            v.Mine.Prim = prim[0];
            return Named(v, en);
        }

        static LineMockScenario Awards(bool en)
        {
            float t = T0(en);
            if (en)
                // (the awards slide at rest, my title on the results bar, the story tab)
                return Start(en)
                    .Do(t, () => { Reset(); RevealMemory.MockGroup(1, "mock", 0, 0, 1); Show(StoryResults(true, 15)); }, 1.6f)
                    .Do(t + 2.4f, () => MeetingUi.I?.OpenReveal(MeetingReveal.TabStory), true);
            return Start(en)
                // the ranking and the winner, then "ÖDÜLLER" stamped in one by one, my title on the results bar
                .Do(t, () => { Reset(); RevealMemory.MockGroup(1, "mock", 0, 0, 1); Show(StoryResults(false, 0)); }, 4.8f)
                .Do(t + 9.4f, () => { }, 0.6f)
                .Do(t + 12.6f, () => { }, 0.8f)
                // the folder: Manşetler / Ödüller / Beyanlar, then the desk openings with their stamps
                .Do(t + 14.2f, () => MeetingUi.I?.OpenReveal(MeetingReveal.TabStory), true)
                .Do(t + 16.0f, () => MeetingUi.I?.OpenReveal(MeetingReveal.TabFirings), true)
                .Do(t + 17.8f, () => MeetingUi.I?.OpenReveal(MeetingReveal.TabRobbed), true)
                .Do(t + 19.6f, () => { MeetingUi.I?.CloseAll(); Aim(0f, 8f); }, true);
        }

        // ------------------------------------------------------------------ the lobby

        static void LobbyMock(bool en, int seats, int talk, string status)
        {
            LobbyScreen.MockHost = true;
            LineClient.I.ClearWeek();
            var lobby = new LineLobbyView { RecordingsEnabled = true, PranksEnabled = true, TalkTime = talk, StatusKey = status, SessionId = "mock" };
            var names = en ? LineMock.NamesEn : LineMock.NamesTr;
            for (int i = 0; i < 6; i++)
                lobby.Slots.Add(new LineLobbySlot { Slot = i, Occupied = i < seats, Connected = i < seats, Ready = i < seats, IsHost = i == 0, IsBot = i >= 2 && i < seats,
                                                    Name = i < seats ? names[i] : "" });
            LineClient.I.ApplyLobby(lobby);
        }

        static LineMockScenario Lobby(bool en)
        {
            float t = T0(en);
            return Start(en)
                // three seats: the start button is off with the hint; four: ready, talk time Uzun; then Kısa
                .Do(t, () => LobbyMock(en, 3, LineTalkTime.Normal, "lobby.need_players"), true)
                .Do(t + 1.6f, () => LobbyMock(en, 4, LineTalkTime.Long, "lobby.ready_to_start"), true)
                .Do(t + 3.2f, () => LobbyMock(en, 5, LineTalkTime.Short, "lobby.ready_to_start"), true)
                // the how-to pages that changed: the flow and the final
                .Do(t + 4.8f, () => { LobbyScreen.MockHost = false; HowToPlayScreen.OpenAt(HowToPlayScreen.PageFlow); }, true)
                .Do(t + 6.4f, () => HowToPlayScreen.OpenAt(HowToPlayScreen.PageFinal), true);
        }
    }
}
