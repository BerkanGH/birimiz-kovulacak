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
    /// <c>"v5.meeting_reveal*"</c> the desk opening (drawer, count, "11 PRİM", v5.2: the declaration stamped YALAN / DOĞRU
    /// against it, the box, the bundles sliding to the voters), also the 6-player sequence (first opening and payout, the
    /// second vote with the bundles on the table, the second opening); <c>"v5.meeting_results*"</c> the ceremony, the folder's
    /// full reveal and the results bar; "_en" variants.
    /// v5.3: <c>"v5.meeting_files*"</c> "Dosyalarım" in the folder (a held file, the open / shred confirmations), the
    /// opened file stamping itself onto the projector, the agenda with the count of unopened files and the week's
    /// stakes, the whiteboard's counter, the anonymous shredding notice; <c>"v5.meeting_reveal_files*"</c> the results'
    /// "Dosyalar" tab and the story with the week's agenda.
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
                .Do(7.4f, () => { MeetingProjector.MockDeckIndex = -1; var v = Discussion(6); AddFileOpened(v, 4, 5); Show(v); }, 0.25f)
                .Do(8.8f, () => { }, 0.2f)
                .Do(9.6f, () => { MeetingUi.I?.CloseAll(); LookAtProjector(); }, true));
            // v5.1 meeting 2: the shift was short, the skippers' suç is automatic, no vote (agenda and the penalty entry)
            LineMock.Register("v5.meeting_projector_shift", () => new LineMockScenario()
                .Do(0f, () => { Reset(); Show(Meeting2(6)); MeetingProjector.MockDeckIndex = 0; })
                .Do(0.4f, () => MeetingUi.I?.OpenProjector(), true)
                .Do(1.8f, () => MeetingProjector.MockDeckIndex = 3, 0.8f)
                .Do(3.2f, () => { MeetingProjector.MockDeckIndex = -1; MeetingUi.I?.CloseAll(); Aim(0f, 8f); }, true));
            LineMock.Register("v5.meeting_projector_shift_en", () => new LineMockScenario()
                .Do(0f, () => English())
                .Do(0.3f, () => { Reset(); var v = Meeting2(6); LineMock.SetNames(v, true); Show(v); MeetingProjector.MockDeckIndex = 0; })
                .Do(0.7f, () => MeetingUi.I?.OpenProjector(), true)
                .Do(2.1f, () => MeetingProjector.MockDeckIndex = 3, 0.8f));
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

            // ---- the desk opening (4 players): drawer, count, the prim stamp, the declaration's YALAN stamp, who shares it,
            // back to the table, the box, the bundles sliding
            LineMock.Register("v5.meeting_reveal", () => new LineMockScenario()
                .Do(0f, () => { Reset(); Show(Verdict(4, Firing(2, 11, FiringDecision.Plurality, new[] { 0, 3 }, 6))); Aim(0f, 6f); }, 0.7f)
                .Do(1.9f, () => { }, 0.15f)
                .Do(3.6f, () => { }, 0.2f)
                .Do(4.3f, () => { }, 0.15f)
                .Do(5.0f, () => { }, 0.2f)
                .Do(6.3f, () => { }, 0.2f)
                .Do(7.1f, () => { }, 0.25f)
                .Do(8.4f, () => { }, 0.6f));
            // the fired colleague had told the truth: DOĞRU (zoomed in for the whole count)
            LineMock.Register("v5.meeting_reveal_true", () => new LineMockScenario()
                .Do(0f, () => { Reset(); MeetingUi.NoAutoCamera = true; Show(Verdict(4, Firing(2, 11, FiringDecision.Plurality, new[] { 0, 3 }, 11))); MeetingUi.I?.OpenProjector(); }, 4.9f)
                .Do(6.0f, () => { MeetingUi.NoAutoCamera = false; MeetingUi.I?.CloseAll(); Aim(0f, 6f); }, 0.8f));
            // the desk grew after the reveal (a share, a transfer): the declaration was true when it was revealed, so
            // DOĞRU with the note "Beyanlar açılırken: 8 prim"
            foreach (bool en in new[] { false, true })
            {
                bool e = en;
                LineMock.Register("v5.meeting_reveal_later" + (en ? "_en" : ""), () => new LineMockScenario()
                    .Do(0f, () => { if (e) English(); })
                    .Do(e ? 0.3f : 0f, () => { Reset(); MeetingUi.NoAutoCamera = true; var v = Verdict(4, Firing(2, 13, FiringDecision.Plurality, new[] { 0, 3 }, 8, 8));
                        if (e) LineMock.SetNames(v, true); Show(v); MeetingUi.I?.OpenProjector(); }, 4.9f)
                    .Do(6.3f, () => { MeetingUi.NoAutoCamera = false; MeetingUi.I?.CloseAll(); Aim(0f, 6f); }, 0.8f));
            }
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

            // ---- v5.3 "Dosya" in the meeting room
            LineMock.Register("v5.meeting_files", () => new LineMockScenario()
                .Do(0f, () => { Reset(); Show(Discussion(5)); Aim(0f, 6f); MeetingProjector.MockDeckIndex = 0; }, true)
                .Do(1.6f, () => MeetingUi.I?.OpenFolder(MeetingFolder.Tab.Actions), true)
                .Do(3.2f, () => AskFile(1), true)
                .Do(4.8f, () => AskFile(2), true)
                .Do(6.4f, () =>
                {
                    // confirmed "Aç": the line joins the dossier, Sarı's suç lands at once
                    MeetingUi.I.State.AskFile(null, 0);
                    MeetingUi.I?.CloseAll();
                    MeetingProjector.MockDeckIndex = -1;
                    Show(Opened(5));
                    LineClient.I.ApplyEvents(new List<LineEvent> { new LineEvent { Type = LineEventType.FileOpened, Actor = 0, Target = 1, A = (int)TaskKind.ClientBonus, B = 4, Time = LineMock.Now } });
                    LookAtProjector();
                }, 0.9f)
                .Do(8.2f, () => MeetingUi.I?.OpenProjector(), true)
                .Do(9.8f, () => MeetingProjector.MockDeckIndex = 0, 0.8f)
                .Do(11.2f, () => { MeetingProjector.MockDeckIndex = -1; MeetingUi.I?.CloseAll(); MeetingUi.I?.OpenBoard(); }, true)
                .Do(12.8f, () =>
                {
                    MeetingUi.I?.CloseAll();
                    Aim(0f, 6f);
                    var v = Opened(5);
                    v.Files.Held = 0; v.Files.Shredded = 1;
                    Show(v);
                    LineClient.I.ApplyEvents(new List<LineEvent> { new LineEvent { Type = LineEventType.FileNotice, A = (int)LineFileState.Shredded, B = 1, C = 0, Audience = LineAudience.Player, AudiencePlayer = 0, Time = LineMock.Now } });
                }, true));
            LineMock.Register("v5.meeting_files_en", () => new LineMockScenario()
                .Do(0f, () => English())
                .Do(0.3f, () => { Reset(); var v = Discussion(5); LineMock.SetNames(v, true); Show(v); Aim(0f, 6f); })
                .Do(0.9f, () => MeetingUi.I?.OpenFolder(MeetingFolder.Tab.Actions), true)
                .Do(2.5f, () => AskFile(1), true)
                .Do(4.1f, () => { MeetingUi.I.State.AskFile(null, 0); MeetingUi.I?.CloseAll(); MeetingProjector.MockDeckIndex = 0; MeetingUi.I?.OpenProjector(); }, true));
            LineMock.Register("v5.meeting_reveal_files", () => new LineMockScenario()
                .Do(0f, () => { Reset(); RevealMemory.MockGroup(1, "mock", 0, 0, 1); Show(Results(6)); })
                .Do(0.6f, () => MeetingUi.I?.OpenReveal(MeetingReveal.TabFiles), true)
                .Do(2.2f, () => MeetingUi.I?.OpenReveal(MeetingReveal.TabStory), true)
                .Do(3.8f, () => MeetingUi.I?.OpenReveal(MeetingReveal.TabSummary), true));
            LineMock.Register("v5.meeting_reveal_files_en", () => new LineMockScenario()
                .Do(0f, () => English())
                .Do(0.3f, () => { Reset(); RevealMemory.MockGroup(1, "mock", 0, 0, 1); var v = Results(6); LineMock.SetNames(v, true); Show(v); })
                .Do(0.9f, () => MeetingUi.I?.OpenReveal(MeetingReveal.TabFiles), true));

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
                .Do(0.3f, () => { Reset(); var v = Verdict(4, Firing(2, 23, FiringDecision.Revote, new[] { 0, 1, 3 }, 14)); LineMock.SetNames(v, true); Show(v); }, 4.9f));
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

        /// <summary>"Dosyalarım" in the folder: Aç (1) / İmha et (2) was pressed on my file: the confirmation is up.</summary>
        static void AskFile(int action)
        {
            var v = LineClient.I.View;
            var f = v != null ? v.Mine.Files.Find(x => x.Holder == 0 && x.State == LineFileState.Held) : null;
            if (f != null && MeetingUi.I != null) MeetingUi.I.State.AskFile(f, action);
        }

        /// <summary>The discussion after I opened my Client Bonus file on Sarı.</summary>
        static LineView Opened(int players)
        {
            var v = Discussion(players);
            var mine = v.Mine.Files.Find(x => x.Holder == 0 && x.Task == TaskKind.ClientBonus);
            mine.State = LineFileState.Opened;
            mine.Phase = LinePhase.MeetingDiscussion;
            v.Files.Opened.Add(mine);
            v.Files.Held = 1;
            v.Meeting.Dossier.Add(new LineDossierEntry { Kind = DossierKind.FileOpened, Task = TaskKind.ClientBonus, Player = 0, Other = 1, N1 = 4, Time = LineMock.Now });
            v.Players[1].Blame += 4;
            return v;
        }

        /// <summary>A Client Bonus file is opened in the meeting room: its line joins the dossier, the suç lands at once.</summary>
        static void AddFileOpened(LineView v, int by, int target)
        {
            v.Meeting.Dossier.Add(new LineDossierEntry { Kind = DossierKind.FileOpened, Task = TaskKind.ClientBonus, Player = by, Other = target, N1 = 4, Time = LineMock.Now });
            LineMock.AddFile(v, LineMock.File(TaskKind.ClientBonus, by, target, LineFileState.Opened, LinePhase.MeetingDiscussion));
            v.Files.Held = System.Math.Max(0, v.Files.Held - 1);
            v.Players[target].Blame += 4;
        }

        /// <summary>Meeting 2 (v5.1): the shift was short (Mavi and Yeşil signed), the four skippers got +2 suç, no vote.</summary>
        static LineView Meeting2(int players)
        {
            var v = LineMock.BaseView(LinePhase.MeetingDiscussion, players, 1, 40);
            var mt = new LineMeetingView { Index = 1, VoteableCount = 0 };
            mt.FailedTasks.Add(TaskKind.WeekendShift);
            mt.Roster.AddRange(new[] { 0, 3 });
            var c = LineConfig.CreateDefault();
            mt.Dossier.Add(new LineDossierEntry { Kind = DossierKind.TaskFailed, Task = TaskKind.WeekendShift, N1 = 2, Time = LineMock.Now });
            mt.Dossier.Add(new LineDossierEntry { Kind = DossierKind.ShiftRoster, Task = TaskKind.WeekendShift, N1 = 2, N2 = LineConfig.ShiftNeeded(players), Time = LineMock.Now });
            mt.Dossier.Add(new LineDossierEntry { Kind = DossierKind.ShiftSkipBlame, Task = TaskKind.WeekendShift, N1 = c.ShiftFailSkipBlame, N2 = players - 2, Time = LineMock.Now });
            v.Meeting = mt;
            foreach (var p in v.Players) p.Blame = new[] { 1, 6, 2, 3, 5, 2 }[p.Id];
            return v;
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

        /// <summary><paramref name="had"/>: the prim at the reveal when the desk holds something else (prim arrived later).</summary>
        internal static LineFiringView Firing(int player, int desk, FiringDecision decision, int[] voters, int declared = -1, int had = -1)
        {
            var f = new LineFiringView
            {
                Player = player, Decision = decision, Votes = voters.Length, Desk = desk, Declared = declared, DeclaredActual = had >= 0 ? had : declared >= 0 ? desk : -1,
            };
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
            // v5.2: the declarations were revealed in the final discussion (the fired colleague's is on the firing)
            v.Final.DeclarationsRevealed = true;
            foreach (int id in v.Final.Candidates) v.Final.Declarations.Add(new LineDeclarationView { Player = id, Amount = id == f.Player ? f.Declared : 9 });
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
            var c = LineMock.Config();
            r.Stakes = LineMock.Week();
            int[] prim = new int[6];
            int[] blame = { 9, 6, 4, 7, 3, 8 };
            bool six = players >= 5;
            // task 1: Client Bonus (Mavi–Sarı: Sarı took it all), task 2: Petty Cash, task 3: Weekend Shift
            for (int i = 0; i < players; i++)
            {
                bool take = i == 1 || i == 4;
                int pay = i == 1 || i == 4 ? 5 : i == 0 || (i == 5 && six) ? 0 : 3;
                if (players == 4 && i == 2) pay = 3;
                r.Choices.Add(new LineChoiceView { TaskNumber = 0, Task = TaskKind.ClientBonus, Player = i, Choice = take ? LineChoice.Take : LineChoice.Share, Payout = take ? 5 : pay });
            }
            for (int i = 0; i < players; i++) r.Choices.Add(new LineChoiceView { TaskNumber = 1, Task = TaskKind.PettyCash, Player = i, Choice = i == 3 ? 1 : 0, Payout = i == 3 ? c.CashTake : 0 });
            for (int i = 0; i < players; i++) r.Choices.Add(new LineChoiceView { TaskNumber = 2, Task = TaskKind.WeekendShift, Player = i, Choice = i % 2, Payout = i % 2 == 0 ? c.ShiftSkipPay : 0 });
            for (int i = 0; i < players; i++)
            {
                r.Ballots.Add(new LineBallotView { Kind = BallotKind.Meeting, Round = 0, Voter = i, Index = 0, Target = i == 0 ? 1 : 0 });
                r.Ballots.Add(new LineBallotView { Kind = BallotKind.Crisis, Voter = i, Target = i == 0 ? 3 : 0 });
                if (i != 0) r.Ballots.Add(new LineBallotView { Kind = BallotKind.Final, Round = 0, Voter = i, Target = i == 2 ? -1 : i == 5 ? 3 : 0 });
            }
            r.Ballots.Add(new LineBallotView { Kind = BallotKind.Final, Round = 0, Voter = 0, Target = 3 });
            r.Transfers.Add(new LineTransferView { From = 3, To = 1, Amount = 2, Phase = LinePhase.Break });
            r.Transfers.Add(new LineTransferView { From = 0, To = 2, Amount = 3, Phase = LinePhase.CrisisCalls });
            r.Transfers.Add(new LineTransferView { From = 3, To = 2, Amount = 1, Phase = LinePhase.Break });
            // v5.3 files: I opened mine on Sarı at the meeting; Pembe kept Yeşil's silence money and opened in the crisis
            // calls anyway; with six, Turuncu's taker paid nothing and the file was shredded all the same
            r.Incidents.Add(new LineDossierEntry { Kind = DossierKind.FileOpened, Task = TaskKind.ClientBonus, Player = 0, Other = 1, N1 = 4 });
            r.Incidents.Add(new LineDossierEntry { Kind = DossierKind.FileOpened, Task = TaskKind.PettyCash, Player = 2, Other = 3, N1 = 3 });
            r.Incidents.Add(new LineDossierEntry { Kind = DossierKind.Leak, Player = 2, Other = 0, N1 = 1, N2 = 3 });
            r.Files.Add(LineMock.File(TaskKind.ClientBonus, 0, 1, LineFileState.Opened, LinePhase.MeetingDossier));
            r.Files.Add(LineMock.File(TaskKind.PettyCash, 2, 3, LineFileState.Opened, LinePhase.CrisisCalls));
            if (six) r.Files.Add(LineMock.File(TaskKind.ClientBonus, 5, 4, LineFileState.Shredded, LinePhase.Break));
            // every balance follows from the logs (start, tasks, transfers), then the desks are opened
            for (int i = 0; i < players; i++) prim[i] = c.StartPrim;
            foreach (var ch in r.Choices) prim[ch.Player] += ch.Payout;
            foreach (var t in r.Transfers) { prim[t.From] -= t.Amount; prim[t.To] += t.Amount; }
            r.Fired.Add(0);
            r.Firings.Add(Firing(0, prim[0], FiringDecision.Plurality, six ? new[] { 1, 3, 4 } : new[] { 1, 3 }));
            if (six) { r.Fired.Add(5); r.Firings.Add(Firing(5, prim[5], FiringDecision.Revote, new[] { 1, 3 })); }
            foreach (var f in r.Firings) { prim[f.Player] = 0; foreach (int id in f.Recipients) prim[id] += f.Share; }
            int best = 0;
            for (int i = 0; i < players; i++) if (!r.Fired.Contains(i)) best = System.Math.Max(best, prim[i]);
            for (int i = 0; i < players; i++)
            {
                bool fired = r.Fired.Contains(i), winner = !fired && prim[i] == best;
                if (winner) r.Winners.Add(i);
                r.Players.Add(new LineWeekPlayerResult { Player = i, Prim = prim[i], Blame = blame[i], Fired = fired, Winner = winner,
                    WasCandidate = i == 0 || i == 3 || (six && i == 5), Recorded = i == 1 ? 1 : 0, Leaked = i == 2 ? 1 : 0 });
            }
            // the suç log behind every board total (v5.1: the volunteers 1, 3, 5 lost 1; Sarı was investigated)
            var why = new[]
            {
                new[] { (int)BlameReason.Vote, 5, (int)BlameReason.Crisis, 4 },
                new[] { (int)BlameReason.Vote, 3, (int)BlameReason.InvestigationGuilty, 4, (int)BlameReason.ShiftClean, -1 },
                new[] { (int)BlameReason.Vote, 4 },
                new[] { (int)BlameReason.Whistled, 3, (int)BlameReason.Vote, 5, (int)BlameReason.ShiftClean, -1 },
                new[] { (int)BlameReason.Vote, 3 },
                new[] { (int)BlameReason.Vote, 5, (int)BlameReason.Crisis, 4, (int)BlameReason.ShiftClean, -1 },
            };
            for (int i = 0; i < players; i++)
                for (int k = 0; k < why[i].Length; k += 2)
                    r.BlameLog.Add(new LineBlameChange { Player = i, Reason = (BlameReason)why[i][k], Delta = why[i][k + 1] });
            // the prim log behind every balance (start 6)
            foreach (var ch in r.Choices) if (ch.Payout != 0) r.PrimLog.Add(new LinePrimChange { Player = ch.Player, Delta = ch.Payout, Reason = PrimReason.Task });
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
