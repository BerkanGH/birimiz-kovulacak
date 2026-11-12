using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using static BirimizKovulacak.Game.MeetingMockScenarios;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 step 3b scenarios for the UI mock (docs/V5_INTERACTION.md §3: projector, defence, desk opening, results):
    /// <c>-uimock "v5.meeting_projector*"</c> the dossier deck (seen from the seat, zoomed, a new entry sliding in, from the
    /// far seats), the final's deck (candidates, crisis result and the board shift), the verdicts and a vote;
    /// <c>"v5.meeting_leak*"</c> a leaked recording on the projector (everyone turns to it, the note when it is out of view);
    /// <c>"v5.meeting_defence*"</c> the defender on their feet in the spotlight, the projector, me defending;
    /// <c>"v5.meeting_reveal*"</c> the desk opening (drawer, count, "11 PRİM", the box, the bundles sliding to the voters),
    /// also the 6-player sequence (first opening and payout, the second vote with the bundles on the table, the second
    /// opening); <c>"v5.meeting_results*"</c> the ceremony, the folder's full reveal and the results bar; "_en" variants.
    /// </summary>
    public static class MeetingMock3bScenarios
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Register()
        {
            // ---- the dossier on the projector
            LineMock.Register("v5.meeting_projector", () => new LineMockScenario()
                .Do(0f, () => { Reset(); Show(Discussion(6)); MeetingProjector.MockDeckIndex = 0; })
                .Do(0.6f, () => LookAtProjector(), true)
                .Do(2.2f, () => MeetingUi.I?.OpenProjector(), true)
                .Do(3.8f, () => MeetingProjector.MockDeckIndex = 3, 0.8f)
                .Do(5.0f, () => MeetingProjector.MockDeckIndex = 5, 0.8f)
                .Do(6.2f, () => MeetingProjector.MockDeckIndex = 6, 0.8f)
                .Do(7.4f, () => { MeetingProjector.MockDeckIndex = -1; var v = Discussion(6); AddInvestigation(v, 4, 5, true); Show(v); }, 0.25f)
                .Do(8.8f, () => { }, 0.2f)
                .Do(9.6f, () => { MeetingUi.I?.CloseAll(); LookAtProjector(); }, true));
            // readable from the far seats (left row, right row): turned toward it from the seat, then zoomed in
            LineMock.Register("v5.meeting_projector_far", () => new LineMockScenario { Me = 4 }
                .Do(0f, () => { Reset(); var v = Discussion(6); v.Me = 4; Show(v); MeetingProjector.MockDeckIndex = 3; })
                .Do(0.6f, () => LookAtProjector(), true)
                .Do(2.2f, () => MeetingUi.I?.OpenProjector(), true));
            LineMock.Register("v5.meeting_projector_farright", () => new LineMockScenario { Me = 5 }
                .Do(0f, () => { Reset(); var v = Discussion(6); v.Me = 5; Show(v); MeetingProjector.MockDeckIndex = 4; })
                .Do(0.6f, () => LookAtProjector(), true));
            // the final's deck (candidates, the crisis with the board shift), the verdicts, a vote with papers in the box
            LineMock.Register("v5.meeting_projector_final", () => new LineMockScenario()
                .Do(0f, () => { Reset(); Show(FinalDiscussion(6)); MeetingProjector.MockDeckIndex = 0; })
                .Do(0.4f, () => MeetingUi.I?.OpenProjector(), true)
                .Do(1.6f, () => MeetingProjector.MockDeckIndex = 1, true)
                .Do(3.0f, () => { MeetingProjector.MockDeckIndex = -1; Show(CrisisVerdict(6)); }, true)
                .Do(4.6f, () => { Show(MeetingVerdict(5)); }, true)
                .Do(6.2f, () => { Show(Vote(5, 3, 1)); MeetingUi.I?.OpenProjector(); }, true)
                .Do(7.8f, () => { var v = FinalVote(5); Show(v); MeetingUi.I?.OpenProjector(); }, true));

            // ---- the pencil draws the X (E on a colleague marks them; the paper comes up)
            LineMock.Register("v5.meeting_ballot_pen", () => new LineMockScenario()
                .Do(0f, () => { Reset(); Show(Vote(5, -1, -1)); })
                .Do(0.4f, () => { Aim(0f, 6f); MeetingUi.I?.OpenBallot(); }, true)
                .Do(1.8f, () => MeetingUi.I?.Ballot.MarkFromLook(3), 0.14f)
                .Do(2.6f, () => MeetingUi.I?.Ballot.MarkFromLook(1), 0.6f));

            // ---- a leaked recording plays on the projector
            LineMock.Register("v5.meeting_leak", () => new LineMockScenario()
                .Do(0f, () => { Reset(); Show(Leak(5)); })
                .Do(0.3f, () => AimAtSlot(1), 0.5f)
                .Do(2.6f, () => { }, 0.2f)
                .Do(3.6f, () => LookAtProjector(), 0.8f)
                .Do(5.4f, () => MeetingUi.I?.OpenProjector(), 0.9f)
                .Do(8.8f, () => { }, 0.6f));

            // ---- the defence: the defender stands up in the spotlight, the others turn to them
            LineMock.Register("v5.meeting_defence", () => new LineMockScenario()
                .Do(0f, () => { Reset(); Show(Defense(5)); Speak(1); })
                .Do(0.4f, () => AimAtSlot(1), 1.4f)
                .Do(2.2f, () => Aim(0f, 4f), 0.8f)
                .Do(3.4f, () => MeetingUi.I?.OpenProjector(), true)
                .Do(5.0f, () => { MeetingUi.I?.CloseAll(); Speak(0); var v = Defense(5); v.Final.DefenseOrder.Clear(); v.Final.DefenseOrder.AddRange(new[] { 2, 0, 3 });
                    v.Final.Candidates.Clear(); v.Final.Candidates.AddRange(new[] { 2, 0, 3 }); v.Final.Defender = 0; Candidates(v); Show(v); Aim(0f, 8f); }, 1.6f)
                .Do(7.2f, () => Aim(-30f, 6f), 0.8f));

            // ---- the desk opening (4 players): drawer, count, stamp, back to the table, the box, the bundles sliding
            LineMock.Register("v5.meeting_reveal", () => new LineMockScenario()
                .Do(0f, () => { Reset(); Show(Verdict(4, Firing(2, 11, FiringDecision.Plurality, new[] { 0, 3 }))); Aim(0f, 6f); }, 0.7f)
                .Do(1.9f, () => { }, 0.15f)
                .Do(3.9f, () => { }, 0.15f)
                .Do(5.3f, () => { }, 0.15f)
                .Do(6.2f, () => { }, 0.25f)
                .Do(7.6f, () => { }, 0.6f));
            // 6 players: the first opening and payout before the second vote, then the second opening
            LineMock.Register("v5.meeting_reveal6", () => new LineMockScenario()
                .Do(0f, () => { Reset(); Show(Verdict(6, Firing(2, 11, FiringDecision.Plurality, new[] { 0, 3, 4 }))); Aim(0f, 6f); }, 0.7f)
                .Do(3.9f, () => { }, 0.15f)
                .Do(6.2f, () => { }, 0.3f)
                .Do(8.0f, () => { }, 0.8f)
                .Do(9.4f, () => { Fresh(); Show(SecondVote(6)); Aim(-8f, 18f); }, 1.4f)
                .Do(11.4f, () => MeetingUi.I?.OpenProjector(), true)
                .Do(13.2f, () => { MeetingUi.I?.CloseAll(); Fresh(); var v = Verdict(6, Firing(2, 11, FiringDecision.Plurality, new[] { 0, 3, 4 }));
                    v.Final.Round = 1; v.Final.Firings.Add(Firing(5, 6, FiringDecision.Revote, new[] { 1, 3 })); Candidates(v); Show(v); Aim(0f, 6f); }, 0.7f)
                .Do(17.3f, () => { }, 0.15f)
                .Do(19.6f, () => { }, 0.4f)
                .Do(21.2f, () => Aim(-10f, 22f), 0.8f));

            // ---- the results: the ceremony on the projector, the full reveal in the folder, the results bar
            LineMock.Register("v5.meeting_results", () => new LineMockScenario()
                .Do(0f, () => { Reset(); RevealMemory.MockGroup(1, "mock", 0, 0, 1); Show(Results(6)); }, 0.9f)
                .Do(1.6f, () => { }, 0.4f)
                .Do(4.6f, () => { }, 0.8f)
                .Do(6.4f, () => MeetingUi.I?.OpenReveal(MeetingReveal.TabSummary), true)
                .Do(8.0f, () => MeetingUi.I?.OpenReveal(MeetingReveal.TabRobbed), true)
                .Do(9.6f, () => MeetingUi.I?.OpenReveal(MeetingReveal.TabBallots), true)
                .Do(11.2f, () => MeetingUi.I?.OpenReveal(MeetingReveal.TabFirings), true)
                .Do(12.8f, () => { MeetingUi.I?.CloseAll(); Aim(0f, 8f); }, true));
            LineMock.Register("v5.meeting_results4", () => new LineMockScenario()
                .Do(0f, () => { Reset(); RevealMemory.MockGroup(1, "mock", 0, 0, 1); Show(Results(4)); }, 4.6f)
                .Do(5.4f, () => MeetingUi.I?.OpenReveal(MeetingReveal.TabRobbed), true));

            // ---- English samples
            LineMock.Register("v5.meeting_projector_en", () => new LineMockScenario()
                .Do(0f, () => English())
                .Do(0.3f, () => { Reset(); var v = Discussion(6); LineMock.SetNames(v, true); Show(v); MeetingProjector.MockDeckIndex = 5; })
                .Do(0.7f, () => MeetingUi.I?.OpenProjector(), true)
                .Do(1.9f, () => { MeetingProjector.MockDeckIndex = -1; var v = CrisisVerdict(6); LineMock.SetNames(v, true); Show(v); }, true)
                .Do(3.5f, () => { var v = Defense(5); LineMock.SetNames(v, true); Show(v); Speak(1); MeetingUi.I?.OpenProjector(); }, true));
            LineMock.Register("v5.meeting_leak_en", () => new LineMockScenario()
                .Do(0f, () => English())
                .Do(0.3f, () => { Reset(); var v = Leak(5); LineMock.SetNames(v, true); Show(v); })
                .Do(0.7f, () => MeetingUi.I?.OpenProjector(), 2.2f));
            LineMock.Register("v5.meeting_reveal_en", () => new LineMockScenario()
                .Do(0f, () => English())
                .Do(0.3f, () => { Reset(); var v = Verdict(4, Firing(2, 23, FiringDecision.Revote, new[] { 0, 1, 3 })); LineMock.SetNames(v, true); Show(v); }, 4.1f));
            LineMock.Register("v5.meeting_results_en", () => new LineMockScenario()
                .Do(0f, () => English())
                .Do(0.3f, () => { Reset(); RevealMemory.MockGroup(1, "mock", 0, 0, 1); var v = Results(6); LineMock.SetNames(v, true); Show(v); }, 4.6f)
                .Do(5.6f, () => MeetingUi.I?.OpenReveal(MeetingReveal.TabRobbed), true)
                .Do(7.2f, () => MeetingUi.I?.OpenReveal(MeetingReveal.TabSummary), true));
        }

        // ------------------------------------------------------------------ helpers

        static void LookAtProjector()
        {
            float y, p;
            var rooms = Presenter();
            if (rooms != null && rooms.TryGetProjectorLook(out y, out p)) rooms.SetLook(y, p);
        }

        static void AddInvestigation(LineView v, int by, int target, bool guilty)
        {
            v.Meeting.Dossier.Add(new LineDossierEntry { Kind = DossierKind.Investigation, Player = by, Other = target, N1 = guilty ? 1 : 0, Time = LineMock.Now });
            v.Players[target].Blame += 3;
        }

        static LineView Leak(int players)
        {
            var v = Discussion(players);
            var pick = new LineRecordingPick { Index = 0, PlayAt = LineMock.Now + 0.6, Seconds = 7, Caller = 1, Callee = 3, Leaker = 2, CallStart = LineMock.Now - 300, ChosenCallId = 5 };
            pick.CandidateCallIds.Add(5);
            v.Meeting.Recordings.Add(pick);
            return v;
        }

        static LineView FinalDiscussion(int players)
        {
            var v = LineMock.BaseView(LinePhase.FinalDiscussion, players, 2, 30);
            foreach (var p in v.Players) p.Blame = new[] { 3, 5, 10, 7, 2, 1 }[p.Id];
            v.Meeting = new LineMeetingView { Index = 2 };
            v.Final = Final(new[] { 2, 3, 1 }, 2);
            Candidates(v);
            return v;
        }

        static LineView MeetingVerdict(int players)
        {
            var v = LineMock.BaseView(LinePhase.MeetingVerdict, players, 0, 5);
            v.Meeting = LineMock.MeetingView(0, 2);
            v.Meeting.Recordings.Clear();
            foreach (var p in v.Players) p.Blame = new[] { 2, 6, 0, 9, 3, 1 }[p.Id];
            v.Meeting.BlameGained.Add(new LineBlameGainView { Player = 3, Amount = 2 });
            v.Meeting.BlameGained.Add(new LineBlameGainView { Player = 1, Amount = 2 });
            v.Meeting.BlameGained.Add(new LineBlameGainView { Player = 0, Amount = 1 });
            return v;
        }

        internal static LineFiringView Firing(int player, int desk, FiringDecision decision, int[] voters)
        {
            var f = new LineFiringView { Player = player, Decision = decision, Votes = voters.Length, Desk = desk };
            f.Recipients.AddRange(voters);
            f.Share = voters.Length > 0 ? desk / voters.Length : 0;
            f.Removed = desk - f.Share * voters.Length;
            return f;
        }

        /// <summary>A desk opening that starts now (10 s verdict).</summary>
        static LineView Verdict(int players, LineFiringView f)
        {
            var v = LineMock.BaseView(LinePhase.FinalVerdict, players, 2, 10);
            v.PhaseStartTime = LineMock.Now;
            v.PhaseEndTime = LineMock.Now + 10;
            foreach (var p in v.Players) p.Blame = new[] { 3, 8, 11, 7, 2, 9 }[p.Id];
            v.Meeting = new LineMeetingView { Index = 2 };
            v.Final = Final(players >= 5 ? new[] { 2, 5, 1 } : new[] { 2, 1 }, players >= 5 ? 2 : 1);
            v.Final.Firings.Add(f);
            Candidates(v);
            return v;
        }

        /// <summary>6 players: the second firing vote after the first desk was opened and shared.</summary>
        static LineView SecondVote(int players)
        {
            var v = LineMock.BaseView(LinePhase.FinalVote, players, 2, 25);
            foreach (var p in v.Players) p.Blame = new[] { 3, 8, 11, 7, 2, 9 }[p.Id];
            v.Meeting = new LineMeetingView { Index = 2 };
            v.Final = Final(new[] { 2, 5, 1 }, 2);
            v.Final.Round = 1;
            v.Final.Firings.Add(Firing(2, 11, FiringDecision.Plurality, new[] { 0, 3, 4 }));
            v.Final.Options.Clear(); v.Final.Options.AddRange(new[] { 5, 1 });
            Candidates(v);
            v.Mine.Ballots.Add(-1);
            v.Mine.Prim = 12;
            v.Players[3].Voted = true; v.Players[4].Voted = true;
            return v;
        }

        /// <summary>The end of a match. I (Mavi) was robbed by Sarı in the Client Bonus pair of task 1 and fired with Sarı's
        /// vote; every balance has its prim log.</summary>
        static LineView Results(int players)
        {
            var v = LineMock.BaseView(LinePhase.Results, players, 2, 45);
            v.PhaseStartTime = LineMock.Now;
            v.PhaseEndTime = LineMock.Now + 45;
            var r = new LineWeekResult { WeekId = 1, MatchId = "mock", PlayerCount = players, ResultHash = 1234567 };
            int[] prim = { 0, 17, 9, 13, 6, 0 };
            int[] blame = { 9, 6, 4, 7, 3, 8 };
            bool six = players >= 5;
            for (int i = 0; i < players; i++)
            {
                bool fired = i == 0 || (six && i == 5);
                r.Players.Add(new LineWeekPlayerResult { Player = i, Prim = prim[i], Blame = blame[i], Fired = fired, Winner = i == 1,
                    WasCandidate = i == 0 || i == 3 || (six && i == 5), Recorded = i == 1 ? 1 : 0, Leaked = i == 2 ? 1 : 0 });
            }
            r.Winners.Add(1); r.Fired.Add(0);
            r.Firings.Add(Firing(0, 8, FiringDecision.Plurality, six ? new[] { 1, 3, 4 } : new[] { 1, 3 }));
            if (six) { r.Fired.Add(5); r.Firings.Add(Firing(5, 6, FiringDecision.Revote, new[] { 1, 3 })); }
            // task 1: Client Bonus (Mavi–Sarı: Sarı took it all), task 2: Petty Cash, task 3: Weekend Shift
            for (int i = 0; i < players; i++)
            {
                bool take = i == 1 || i == 4;
                int pay = i == 1 || i == 4 ? 5 : i == 0 || (i == 5 && six) ? 0 : 2;
                if (players == 4 && i == 2) pay = 2;
                r.Choices.Add(new LineChoiceView { TaskNumber = 0, Task = TaskKind.ClientBonus, Player = i, Choice = take ? LineChoice.Take : LineChoice.Share, Payout = take ? 5 : pay });
            }
            for (int i = 0; i < players; i++) r.Choices.Add(new LineChoiceView { TaskNumber = 1, Task = TaskKind.PettyCash, Player = i, Choice = i == 3 ? 1 : 0, Payout = i == 3 ? 3 : 0 });
            for (int i = 0; i < players; i++) r.Choices.Add(new LineChoiceView { TaskNumber = 2, Task = TaskKind.WeekendShift, Player = i, Choice = i % 2, Payout = i % 2 == 0 ? 2 : 0 });
            for (int i = 0; i < players; i++)
            {
                r.Ballots.Add(new LineBallotView { Kind = BallotKind.Meeting, Round = 0, Voter = i, Index = 0, Target = i == 0 ? 1 : 0 });
                r.Ballots.Add(new LineBallotView { Kind = BallotKind.Crisis, Voter = i, Target = i == 0 ? 3 : 0 });
                if (i != 0) r.Ballots.Add(new LineBallotView { Kind = BallotKind.Final, Round = 0, Voter = i, Target = i == 2 ? -1 : i == 5 ? 3 : 0 });
            }
            r.Ballots.Add(new LineBallotView { Kind = BallotKind.Final, Round = 0, Voter = 0, Target = 3 });
            r.Transfers.Add(new LineTransferView { From = 3, To = 1, Amount = 2, Phase = LinePhase.Break });
            r.Transfers.Add(new LineTransferView { From = 0, To = 2, Amount = 3, Phase = LinePhase.CrisisCalls });
            r.Incidents.Add(new LineDossierEntry { Kind = DossierKind.Whistle, Player = 2, Other = 3, N1 = 3 });
            r.Incidents.Add(new LineDossierEntry { Kind = DossierKind.Leak, Player = 2, Other = 0, N1 = 1, N2 = 3 });
            // the prim log behind every balance (start 6)
            foreach (var c in r.Choices) if (c.Payout != 0) r.PrimLog.Add(new LinePrimChange { Player = c.Player, Delta = c.Payout, Reason = PrimReason.Task });
            foreach (var t in r.Transfers)
            {
                r.PrimLog.Add(new LinePrimChange { Player = t.From, Delta = -t.Amount, Reason = PrimReason.TransferOut, Other = t.To });
                r.PrimLog.Add(new LinePrimChange { Player = t.To, Delta = t.Amount, Reason = PrimReason.TransferIn, Other = t.From });
            }
            foreach (var f in r.Firings)
            {
                r.PrimLog.Add(new LinePrimChange { Player = f.Player, Delta = -f.Desk, Reason = PrimReason.Fired });
                foreach (int id in f.Recipients) r.PrimLog.Add(new LinePrimChange { Player = id, Delta = f.Share, Reason = PrimReason.DeskShare, Other = f.Player });
            }
            v.Results = r;
            foreach (var p in v.Players) { p.Fired = r.Fired.Contains(p.Id); p.Blame = blame[p.Id]; }
            v.Mine.Prim = prim[0];
            return v;
        }
    }
}
