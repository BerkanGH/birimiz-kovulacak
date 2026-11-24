using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using P = BirimizKovulacak.Core.LinePhase;

namespace BirimizKovulacak.Core.Tests.Line
{
    /// <summary>
    /// v5.3 (docs/V5_ENGINE_SPEC.md §23). "Dosya": files are created at a pair task's reveal, opened (the target's suç at
    /// once, the other files on the offence void) or shredded until the crisis calls end, then they expire; the meeting
    /// never adds an opened file's suç again and votes on what is unresolved when the vote starts. "Haftanın gündemi":
    /// a seeded permutation of small / normal / big over the three tasks; payouts, views and the bots follow it.
    /// </summary>
    [TestFixture]
    public sealed class LineRulesV53Tests
    {
        const TaskKind Bonus = TaskKind.ClientBonus, Cash = TaskKind.PettyCash, Shift = TaskKind.WeekendShift;
        const int Take = LineChoice.Take, Share = LineChoice.Share, Leave = LineChoice.Leave;

        static int Partner(LineTest m, int id) { return m.Group(id).Single(x => x != id); }
        static List<LineFileView> AllFiles(LineTest m) { return m.Ids.SelectMany(id => m.View(id).Mine.Files.Where(f => f.Holder == id)).ToList(); }
        static List<LineEvent> Of(LineTest m, LineEventType type) { return m.Events.Where(e => e.Type == type).ToList(); }

        // ------------------------------------------------------------------ creation

        [Test]
        public void Files_PairShapes_ASharerHoldsOneOnTheTaker_AWitnessOnTheSoleThief()
        {
            var m = new LineTest(4);
            m.PlayTask(0, id => id == 0 ? Take : Share);
            int p = Partner(m, 0);
            // created at the reveal: the sharer holds it, the taker sees it, the other pair knows only the count
            var mine = m.View(p).Mine.Files.Single();
            Assert.AreEqual(Bonus, mine.Task);
            Assert.AreEqual(new[] { p, 0 }, new[] { mine.Holder, mine.Target });
            Assert.AreEqual(LineFileState.Held, mine.State);
            Assert.AreEqual(4, mine.Blame, "the price is on the file");
            Assert.AreEqual(p, m.View(0).Mine.Files.Single().Holder, "the target knows who holds a file on them");
            foreach (int id in m.Ids.Where(x => x != 0 && x != p))
            {
                var v = m.View(id);
                CollectionAssert.IsEmpty(v.Mine.Files);
                Assert.AreEqual(1, v.Files.Held, "the public counter");
                CollectionAssert.IsEmpty(v.Files.Opened);
                Assert.IsTrue(v.Features.FilesOpen);
            }
            // Petty Cash: the sole thief's witness
            m.PlayTask(1, id => id == 1 ? Take : Leave);
            int w = Partner(m, 1);
            var cash = m.FileOf(w, 1, Cash);
            Assert.AreEqual(3, cash.Blame);
            Assert.AreEqual(2, m.View().Files.Held);
            Assert.AreEqual(2, AllFiles(m).Count);

            // both take / both share: nobody holds anything
            var none = new LineTest(4);
            none.PlayTask(0, id => none.Group(0).Contains(id) ? Take : Share);
            none.PlayTask(1, id => none.Group(0).Contains(id) ? Take : Leave);
            CollectionAssert.IsEmpty(AllFiles(none));
            Assert.AreEqual(0, none.View().Files.Held);
        }

        [Test]
        public void Files_TrioShapes()
        {
            // one taker, two sharers: a file each
            var one = new LineTest(3);
            one.PlayTask(0, id => id == 0 ? Take : Share);
            CollectionAssert.AreEquivalent(new[] { "1>0", "2>0" }, AllFiles(one).Select(f => f.Holder + ">" + f.Target));
            // two takers, one sharer: the sharer holds a file on each (no limit of one punishment per player)
            var two = new LineTest(3);
            two.PlayTask(0, id => id == 2 ? Share : Take);
            CollectionAssert.AreEquivalent(new[] { "2>0", "2>1" }, AllFiles(two).Select(f => f.Holder + ">" + f.Target));
            two.OpenFile(2, 0, Bonus);
            two.OpenFile(2, 1, Bonus);
            Assert.AreEqual(new[] { 4, 4, 0 }, new[] { two.Blame(0), two.Blame(1), two.Blame(2) });
            // three takers: nobody shared
            var all = new LineTest(3);
            all.PlayTask(0, id => Take);
            CollectionAssert.IsEmpty(AllFiles(all));
            // Petty Cash: a sole thief has two witnesses; two takers are caught and nobody holds a file
            one.PlayTask(1, id => id == 1 ? Take : Leave);
            CollectionAssert.AreEquivalent(new[] { "0>1", "2>1" }, AllFiles(one).Where(f => f.Task == Cash).Select(f => f.Holder + ">" + f.Target));
            two.PlayTask(1, id => id == 2 ? Leave : Take);
            CollectionAssert.IsEmpty(AllFiles(two).Where(f => f.Task == Cash));
        }

        // ------------------------------------------------------------------ open

        [Test]
        public void Open_SucLandsAtOncePublicly_OtherFilesOnTheOffenceAreVoid_AndTheMeetingDoesNotAddItAgain()
        {
            var m = new LineTest(3);
            m.PlayTask(0, LineTest.Share);
            m.PlayTask(1, id => id == 0 ? Take : Leave);
            int prim = m.Prim(0);
            m.Events.Clear();
            m.OpenFile(1, 0, Cash);
            Assert.AreEqual(3, m.Blame(0), "at once");
            Assert.AreEqual(prim, m.Prim(0), "the thief keeps the cash; opening is free");
            Assert.AreEqual(6 + 3, m.Prim(1), "(its shared bonus: opening costs nothing)");
            var opened = Of(m, LineEventType.FileOpened).Single();
            Assert.AreEqual(LineAudience.All, opened.Audience);
            Assert.AreEqual(new[] { 1, 0, (int)Cash, 3 }, new[] { opened.Actor, opened.Target, opened.A, opened.B });
            var blame = Of(m, LineEventType.BlameAdded).Single();
            Assert.AreEqual(new[] { 0, 3, (int)BlameReason.Whistled }, new[] { blame.Target, blame.A, blame.B });
            CollectionAssert.IsEmpty(Of(m, LineEventType.FileShredded));
            CollectionAssert.IsEmpty(Of(m, LineEventType.FileNotice));
            // the other witness's file is void; nothing can be opened twice
            Assert.AreEqual(LineFileState.Opened, m.FileOf(1, 0, Cash).State);
            Assert.AreEqual(LineFileState.Void, m.FileOf(2, 0, Cash).State);
            m.Fail(LineFailure.AlreadyResolved, 2, LineVerb.FileOpen, 0, (int)Cash);
            m.Fail(LineFailure.AlreadyResolved, 2, LineVerb.FileShred, 0, (int)Cash);
            m.Fail(LineFailure.AlreadyResolved, 1, LineVerb.FileOpen, 0, (int)Cash);
            // public for everyone, in every phase
            var seen = m.View(2).Files;
            Assert.AreEqual(0, seen.Held);
            var pub = seen.Opened.Single();
            Assert.AreEqual(new[] { 1, 0, 3 }, new[] { pub.Holder, pub.Target, pub.Blame });
            Assert.AreEqual(P.TaskReveal, pub.Phase);
            // the meeting: the line is in the dossier, the suç is not added a second time, Petty Cash did not fail
            m.Until(P.MeetingDossier, 0);
            Assert.AreEqual(3, m.Blame(0), "no double suç");
            var mt = m.View().Meeting;
            var line = mt.Dossier.Single(d => d.Kind == DossierKind.FileOpened);
            Assert.AreEqual(Cash, line.Task);
            Assert.AreEqual(new[] { 1, 0, 3 }, new[] { line.Player, line.Other, line.N1 });
            CollectionAssert.DoesNotContain(mt.FailedTasks, Cash);
            Assert.IsFalse(mt.Dossier.Any(d => d.Kind == DossierKind.CashMissing || d.Kind == DossierKind.Whistle));
            m.Until(P.MeetingDiscussion);
            m.Next();
            Assert.AreEqual(P.TaskPlay, m.Engine.Phase, "nothing unresolved: no vote");
            m.Finish();
            var r = m.Engine.Result;
            Assert.AreEqual(1, r.Incidents.Count(d => d.Kind == DossierKind.FileOpened));
            Assert.AreEqual(3, r.BlameLog.Where(b => b.Player == 0 && b.Reason == BlameReason.Whistled).Sum(b => b.Delta));
            Assert.AreEqual(prim, r.PrimLog.Where(x => x.Player == 0 && x.Reason == PrimReason.Task).Sum(x => x.Delta) + 6);
        }

        [Test]
        public void Open_BonusFile_FourSuc_NoMoneyMoves_NoLimitPerPlayer()
        {
            var m = new LineTest(4);
            m.PlayTask(0, id => id == 0 ? Take : Share);
            int p = Partner(m, 0);
            m.PlayTask(1, id => id == 0 ? Take : Leave);
            int w = Partner(m, 0);
            Assert.AreNotEqual(p, w);
            m.Until(P.MeetingDossier, 0);
            int[] before = m.Ids.Select(m.Prim).ToArray();
            m.OpenFile(p, 0, Bonus);
            m.OpenFile(w, 0, Cash);
            Assert.AreEqual(7, m.Blame(0), "4 + 3: the suç prices never change");
            CollectionAssert.AreEqual(before, m.Ids.Select(m.Prim).ToArray());
            // opened in the meeting room: the lines join the dossier on screen
            var lines = m.View().Meeting.Dossier.Where(d => d.Kind == DossierKind.FileOpened).ToList();
            CollectionAssert.AreEqual(new[] { Bonus, Cash }, lines.Select(d => d.Task));
            Assert.IsFalse(m.View(p).Mine.InvestigationUsed);
            CollectionAssert.IsEmpty(m.View(p).Mine.InvestigationTargets);
            Assert.IsFalse(m.View(p).Features.InvestigationOpen || m.View(p).Features.WhistleOpen);
            m.Fail(LineFailure.WrongPhase, p, LineVerb.Investigate, 0);
        }

        // ------------------------------------------------------------------ shred

        [Test]
        public void Shred_IsIrreversibleAndQuiet_OnlyTheHolderAndTheTargetKnowWho()
        {
            var m = new LineTest(4);
            m.PlayTask(0, id => id == 0 ? Take : Share);
            int p = Partner(m, 0);
            var others = m.Ids.Where(x => x != 0 && x != p).ToList();
            m.Events.Clear();
            m.ShredFile(p, 0, Bonus);
            Assert.AreEqual(0, m.Blame(0));
            CollectionAssert.IsEmpty(Of(m, LineEventType.BlameAdded));
            CollectionAssert.IsEmpty(Of(m, LineEventType.FileOpened));
            var told = Of(m, LineEventType.FileShredded);
            CollectionAssert.AreEquivalent(new[] { p, 0 }, told.Select(e => e.AudiencePlayer));
            Assert.IsTrue(told.All(e => e.Audience == LineAudience.Player && e.Actor == p && e.Target == 0 && e.A == (int)Bonus));
            // everyone else: an anonymous notice and the counters
            var notices = Of(m, LineEventType.FileNotice);
            CollectionAssert.AreEquivalent(others, notices.Select(e => e.AudiencePlayer));
            Assert.IsTrue(notices.All(e => e.Audience == LineAudience.Player && e.Actor == -1 && e.Target == -1 && e.A == (int)LineFileState.Shredded && e.B == 1 && e.C == 0));
            foreach (int id in others)
            {
                var v = m.View(id);
                CollectionAssert.IsEmpty(v.Mine.Files);
                Assert.AreEqual(new[] { 0, 1, 0 }, new[] { v.Files.Held, v.Files.Shredded, v.Files.Expired });
                CollectionAssert.IsEmpty(v.Files.Opened);
            }
            Assert.AreEqual(LineFileState.Shredded, m.View(p).Mine.Files.Single().State);
            var mine = m.View(0).Mine.Files.Single();
            Assert.AreEqual(LineFileState.Shredded, mine.State);
            Assert.AreEqual(p, mine.Holder, "the target is told who shredded it");
            m.Fail(LineFailure.AlreadyResolved, p, LineVerb.FileOpen, 0, (int)Bonus);
            m.Fail(LineFailure.AlreadyResolved, p, LineVerb.FileShred, 0, (int)Bonus);
            // the offence is still unresolved: the meeting votes on it
            m.Until(P.MeetingVote);
            Assert.AreEqual(1, m.View().Meeting.VoteableCount);
            m.Finish();
            Assert.AreEqual(LineFileState.Shredded, m.Engine.Result.Files.Single().State);
            Assert.IsFalse(m.Engine.Result.Incidents.Any(d => d.Kind == DossierKind.FileOpened));
        }

        // ------------------------------------------------------------------ the window

        [Test]
        public void Window_FromTheRevealUntilTheCrisisCallsEnd_ThenTheFilesExpire()
        {
            var m = new LineTest(4);
            // nothing to act on before a reveal
            m.Fail(LineFailure.NotEligible, 1, LineVerb.FileOpen, 0, (int)Bonus);
            m.PlayTask(0, id => id == 0 ? Take : Share);
            int p = Partner(m, 0);
            Assert.AreEqual(P.TaskReveal, m.Engine.Phase);
            Assert.AreEqual(LineFileState.Held, m.FileOf(p, 0, Bonus).State, "created at the reveal");
            // validation
            m.Fail(LineFailure.NotEligible, 0, LineVerb.FileOpen, p, (int)Bonus);
            m.Fail(LineFailure.NotEligible, p, LineVerb.FileOpen, 0, (int)Cash);
            m.Fail(LineFailure.BadParam, p, LineVerb.FileOpen, 0, (int)Shift);
            m.Fail(LineFailure.BadParam, p, LineVerb.FileShred, 0);
            m.Fail(LineFailure.Self, p, LineVerb.FileOpen, p, (int)Bonus);
            m.Fail(LineFailure.InvalidTarget, p, LineVerb.FileOpen, 5, (int)Bonus);
            m.PlayTask(1, id => id == 0 ? Take : Leave);
            int w = Partner(m, 0);
            // still held through the meeting, its vote, the shift and into the crisis calls
            m.Until(P.MeetingVote);
            Assert.AreEqual(2, m.View().Meeting.VoteableCount);
            Assert.IsTrue(m.View(p).Features.FilesOpen);
            m.PlayTask(2, id => LineChoice.Volunteer);
            m.Until(P.CrisisCalls);
            Assert.AreEqual(2, m.View().Files.Held);
            // the last moment of the calls is still in time
            m.Now = m.Engine.PhaseEndTime - 0.001;
            m.Events.Clear();
            m.OpenFile(p, 0, Bonus);
            Assert.AreEqual(P.CrisisCalls, m.FileOf(p, 0, Bonus).Phase);
            Assert.IsTrue(m.View().Meeting.Dossier.Any(d => d.Kind == DossierKind.FileOpened && d.Player == p), "the crisis dossier shows it live");
            // the calls end: what is still held expires, publicly and anonymously
            m.Next();
            Assert.AreEqual(P.CrisisVote, m.Engine.Phase);
            Assert.AreEqual(LineFileState.Expired, m.FileOf(w, 0, Cash).State);
            var notice = Of(m, LineEventType.FileNotice).Single();
            Assert.AreEqual(LineAudience.All, notice.Audience);
            Assert.AreEqual(new[] { (int)LineFileState.Expired, 1, 0 }, new[] { notice.A, notice.B, notice.C });
            var v = m.View(3);
            Assert.IsFalse(v.Features.FilesOpen);
            Assert.AreEqual(new[] { 0, 0, 1 }, new[] { v.Files.Held, v.Files.Shredded, v.Files.Expired });
            m.Fail(LineFailure.WrongPhase, w, LineVerb.FileOpen, 0, (int)Cash);
            m.Fail(LineFailure.WrongPhase, w, LineVerb.FileShred, 0, (int)Cash);
            int suc = m.Blame(0);
            m.Finish();
            // the full reveal: every file, who held it on whom and what became of it
            var files = m.Engine.Result.Files;
            Assert.AreEqual(2, files.Count);
            var bonus = files.Single(f => f.Task == Bonus);
            Assert.AreEqual(new[] { p, 0 }, new[] { bonus.Holder, bonus.Target });
            Assert.AreEqual(LineFileState.Opened, bonus.State);
            Assert.AreEqual(P.CrisisCalls, bonus.Phase);
            var cash = files.Single(f => f.Task == Cash);
            Assert.AreEqual(LineFileState.Expired, cash.State);
            Assert.AreEqual(w, cash.Holder);
            Assert.GreaterOrEqual(suc, 4);
        }

        [Test]
        public void OpenedDuringTheShift_IsListedWhenTheCrisisDossierOpens()
        {
            var m = new LineTest(4);
            m.ToMeeting1(id => id == 0 ? Take : Share);
            int holder = m.View(0).Mine.Files.Single().Holder;
            m.PlayTask(2, id => LineChoice.Volunteer); // (task 3 is revealed: outside the meeting room)
            m.OpenFile(holder, 0, Bonus);
            Assert.IsNull(m.View().Meeting);
            Assert.AreEqual(1, m.View().Files.Opened.Count, "public at once all the same");
            m.Until(P.CrisisCalls);
            var line = m.View().Meeting.Dossier.Single(d => d.Kind == DossierKind.FileOpened);
            Assert.AreEqual(new[] { holder, 0, 4 }, new[] { line.Player, line.Other, line.N1 });
        }

        // ------------------------------------------------------------------ the meeting vote

        [Test]
        public void Vote_ATaskIsVoteableUnlessItsOffencesAreResolvedWhenTheVoteStarts_AndVoteSucIsNeverRefunded()
        {
            // held when the vote starts: voted on; opened during the vote: the ballots still count
            var held = new LineTest(4);
            held.ToMeeting1(id => id == 0 ? Take : Share);
            int p = held.View(0).Mine.Files.Single().Holder;
            held.Until(P.MeetingVote);
            Assert.AreEqual(1, held.View().Meeting.VoteableCount);
            foreach (int id in held.Ids.Where(x => x != 0)) held.Ok(id, LineVerb.Vote, 0, 0);
            held.OpenFile(p, 0, Bonus);
            Assert.AreEqual(4, held.Blame(0));
            held.Until(P.MeetingVerdict);
            Assert.AreEqual(4 + 4, held.Blame(0), "the file's 4 and four ballots (three votes and its own empty one): nothing refunded");
            // opened before the vote starts: resolved, no vote
            var opened = new LineTest(4);
            opened.ToMeeting1(id => id == 0 ? Take : Share);
            opened.Until(P.MeetingDiscussion);
            opened.OpenFile(p, 0, Bonus);
            opened.Next();
            Assert.AreEqual(P.TaskPlay, opened.Engine.Phase);
            Assert.AreEqual(4, opened.Blame(0));
            Assert.IsTrue(opened.Ids.Where(x => x != 0).All(id => opened.Blame(id) == 0), "no innocent got vote suç");
            // two takers in a trio: one opened, one held: still vote-able
            var trio = new LineTest(3);
            trio.ToMeeting1(id => id == 2 ? Share : Take);
            trio.OpenFile(2, 0, Bonus);
            trio.Until(P.MeetingVote);
            Assert.AreEqual(1, trio.View().Meeting.VoteableCount);
        }

        [Test]
        public void Guard_FileHeldBlocksVote_AnOffenceSomebodyCanStillOpenIsNotVotedOn()
        {
            Action<LineConfig> guard = c => c.FileHeldBlocksVote = true;
            var held = new LineTest(4, configure: guard);
            held.ToMeeting1(id => id == 0 ? Take : Share, id => id == 1 ? Take : Leave);
            held.Until(P.MeetingDiscussion);
            held.Next();
            Assert.AreEqual(P.TaskPlay, held.Engine.Phase, "both offences have a held file: no vote");
            Assert.IsTrue(held.Ids.All(id => held.Blame(id) == 0));
            // a shredded file: nobody can open it any more, so the meeting votes
            var shredded = new LineTest(4, configure: guard);
            shredded.ToMeeting1(id => id == 0 ? Take : Share);
            shredded.ShredFile(shredded.View(0).Mine.Files.Single().Holder, 0, Bonus);
            shredded.Until(P.MeetingVote);
            Assert.AreEqual(1, shredded.View().Meeting.VoteableCount);
            // both took: there never was a file
            var both = new LineTest(4, configure: guard);
            both.ToMeeting1(id => Take);
            both.Until(P.MeetingVote);
            Assert.AreEqual(1, both.View().Meeting.VoteableCount);
            // off by default: a held file does not stop the vote
            var off = new LineTest(4);
            off.ToMeeting1(id => id == 0 ? Take : Share);
            off.Until(P.MeetingVote);
            Assert.AreEqual(1, off.View().Meeting.VoteableCount);
        }

        // ------------------------------------------------------------------ the public counter

        [Test]
        public void PublicCounter_CanBeTurnedOff_OpenedFilesStayPublic()
        {
            var m = new LineTest(4, configure: c => c.FilePublicCount = false);
            m.PlayTask(0, id => id == 0 ? Take : Share);
            int p = Partner(m, 0);
            int other = m.Ids.First(x => x != 0 && x != p);
            Assert.AreEqual(new[] { -1, -1, -1 }, new[] { m.View(other).Files.Held, m.View(other).Files.Shredded, m.View(other).Files.Expired });
            Assert.AreEqual(1, m.View(p).Mine.Files.Count, "my own files are mine to see");
            m.PlayTask(1, id => id == 0 ? Take : Leave);
            int w = Partner(m, 0);
            m.Events.Clear();
            m.ShredFile(p, 0, Bonus);
            CollectionAssert.IsEmpty(Of(m, LineEventType.FileNotice), "no anonymous notice");
            Assert.AreEqual(2, Of(m, LineEventType.FileShredded).Count, "the holder and the target are still told");
            // the Petty Cash file stays held and expires without a notice
            m.ToCrisisFromHere();
            m.Until(P.CrisisVote);
            Assert.AreEqual(LineFileState.Expired, m.FileOf(w, 0, Cash).State);
            CollectionAssert.IsEmpty(Of(m, LineEventType.FileNotice));
            Assert.AreEqual(-1, m.View(other).Files.Expired);
            // an opened file is public all the same
            var open = new LineTest(4, configure: c => c.FilePublicCount = false);
            open.PlayTask(0, id => id == 0 ? Take : Share);
            open.OpenFile(p, 0, Bonus);
            Assert.AreEqual(1, open.View(other).Files.Opened.Count);
            Assert.AreEqual(-1, open.View(other).Files.Held);
            Assert.AreEqual(1, Of(open, LineEventType.FileOpened).Count);
        }

        [Test]
        public void LegacyRules_HaveNoFiles()
        {
            var m = LineTest.OneShot(4);
            m.PlayTask(0, id => id == 0 ? Take : Share);
            int p = Partner(m, 0);
            m.Fail(LineFailure.NotAvailable, p, LineVerb.FileOpen, 0, (int)Bonus);
            var v = m.View(p);
            CollectionAssert.IsEmpty(v.Mine.Files);
            Assert.IsFalse(v.Features.FilesOpen);
            Assert.AreEqual(-1, v.Files.Held);
            m.Until(P.MeetingDossier, 0);
            m.Ok(p, LineVerb.Investigate, 0);
            m.Finish();
            CollectionAssert.IsEmpty(m.Engine.Result.Files);
            Assert.AreEqual(1, m.Engine.Result.Incidents.Count(d => d.Kind == DossierKind.Investigation));
            // the config travels with its legacy numbers
            var c = LineConfig.CreateDefault();
            LineTest.V50(c);
            c.StakeCashBig = 9;
            c.FileHeldBlocksVote = true;
            c.FilePublicCount = false;
            var back = LineEngineFactory.Create(LineTest.MakeSetup(), c).Config;
            Assert.AreEqual(new[] { 4, 3, 3, 2 }, new[] { back.Legacy.InvestigationCost, back.Legacy.InvestigationRefund, back.Legacy.InvestigationFalseBlame, back.Legacy.CashWhistleReward });
            Assert.IsTrue(!back.Legacy.InvestigationSharerOnly && !back.Legacy.CashWhistledKeeps && back.Legacy.ShiftFailVote);
            Assert.AreEqual(9, back.StakeCashBig);
            Assert.IsTrue(back.FileHeldBlocksVote && !back.FilePublicCount && !back.FilesOn());
            Assert.IsNull(LineEngineFactory.Create(LineTest.MakeSetup()).Config.Legacy);
            c.StakesScheme = 2;
            Assert.Throws<ArgumentException>(() => LineEngineFactory.Create(LineTest.MakeSetup(), c));
            c.StakesScheme = LineStakes.Fixed;
            c.Legacy.InvestigationCost = -1;
            Assert.Throws<ArgumentException>(() => LineEngineFactory.Create(LineTest.MakeSetup(), c));
        }

        // ------------------------------------------------------------------ "Haftanın gündemi"

        [Test]
        public void Stakes_ASeededPermutation_SixWeekTypes_PairingsDoNotDependOnIt()
        {
            var seen = new int[LineStakes.WeekTypes];
            for (int seed = 1; seed <= 240; seed++)
            {
                var m = new LineTest(4, seed, LineTest.Agenda);
                var s = m.View().Stakes;
                Assert.AreEqual(LineStakes.Permutation, s.Scheme);
                Assert.That(s.WeekType, Is.InRange(0, 5));
                seen[s.WeekType]++;
                CollectionAssert.AreEquivalent(new[] { LineStakes.Small, LineStakes.Normal, LineStakes.Big }, new[] { s.LevelBonus, s.LevelCash, s.LevelShift }, "one task of each level");
                Assert.AreEqual(new[] { s.LevelBonus, s.LevelCash, s.LevelShift },
                    new[] { LineStakes.Level(s.WeekType, Bonus), LineStakes.Level(s.WeekType, Cash), LineStakes.Level(s.WeekType, Shift) });
                Assert.AreEqual(new[] { s.LevelBonus, s.LevelCash, s.LevelShift }, new[] { s.Level(Bonus), s.Level(Cash), s.Level(Shift) });
                // deterministic per seed, the same for every seat, public from the Intro
                Assert.AreEqual(P.Intro, m.Engine.Phase);
                Assert.AreEqual(s.WeekType, new LineTest(4, seed, LineTest.Agenda).View(3).Stakes.WeekType);
                if (seed > 40) continue;
                // its own generator: the same seed pairs the same colleagues with fixed stakes
                var fixedStakes = new LineTest(4, seed);
                for (int task = 0; task < 2; task++)
                {
                    m.PlayTask(task, LineTest.Share);
                    fixedStakes.PlayTask(task, LineTest.Share);
                    foreach (int id in m.Ids) CollectionAssert.AreEqual(fixedStakes.Group(id), m.Group(id), "seed " + seed + " task " + task);
                }
            }
            Assert.IsTrue(seen.All(n => n >= 20), "every week type comes up: " + string.Join(" ", seen));
            Assert.AreEqual(LineStakes.Normal, LineStakes.Level(-1, Bonus));
            Assert.AreEqual(LineStakes.Normal, LineStakes.Level(2, TaskKind.None));
            CollectionAssert.AreEqual(new[] { "small", "normal", "big", "" }, new[] { LineStakes.Id(0), LineStakes.Id(1), LineStakes.Id(2), LineStakes.Id(3) });
        }

        [TestCase(0), TestCase(1), TestCase(2), TestCase(3), TestCase(4), TestCase(5)]
        public void Stakes_PayoutsAndViewAmountsPerLevel(int weekType)
        {
            int[] pots = { 4, 6, 8 }, boxes = { 2, 3, 5 }, skips = { 1, 2, 3 };
            int lb = LineStakes.Level(weekType, Bonus), lc = LineStakes.Level(weekType, Cash), ls = LineStakes.Level(weekType, Shift);
            int pot = pots[lb], box = boxes[lc], skip = skips[ls];
            var m = new LineTest(4, LineTest.SeedFor(weekType), LineTest.Agenda);
            // the view and the config carry the match's numbers
            foreach (int id in m.Ids)
            {
                var s = m.View(id).Stakes;
                Assert.AreEqual(new[] { lb, lc, ls }, new[] { s.LevelBonus, s.LevelCash, s.LevelShift });
                Assert.AreEqual(new[] { pot, pot / 2, pot - 1, 1, box, skip }, new[] { s.BonusPot, s.BonusShare, s.BonusTaker, s.BonusBoth, s.CashTake, s.ShiftSkipPay });
            }
            var c = m.Engine.Config;
            Assert.AreEqual(new[] { pot / 2, pot - 1, 0, 1 }, new[] { c.BonusPairShare, c.BonusPairTaker, c.BonusPairSharer, c.BonusPairBothTake });
            Assert.AreEqual(new[] { pot / 2, pot - 1, 0, 1 }, new[] { c.BonusTrioNone, c.BonusTrioTaker, c.BonusTrioOthers, c.BonusTrioMany });
            Assert.AreEqual(new[] { box, skip }, new[] { c.CashTake, c.ShiftSkipPay });
            Assert.AreEqual(new[] { 4, 3, 2, 4 }, new[] { c.InvestigationGuiltyBlame, c.CashWhistleBlame, c.ShiftFailSkipBlame, c.CrisisBlame }, "the suç prices never change");
            Assert.AreEqual(6, m.Config.BonusPairShare + m.Config.BonusPairShare, "the caller's config is not touched");
            // Client Bonus: one pair shares, in the other one takes
            m.PlayTask(0, id => id == 0 ? Take : Share);
            int p = Partner(m, 0);
            foreach (int id in m.Ids)
                Assert.AreEqual(id == 0 ? pot - 1 : id == p ? 0 : pot / 2, m.View(id).Task.MyPayout, "seat " + id);
            // Petty Cash: a sole taker gets the box
            m.PlayTask(1, id => id == 0 ? Take : Leave);
            Assert.AreEqual(box, m.View(0).Task.MyPayout);
            Assert.IsTrue(m.Ids.Where(id => id != 0).All(id => m.View(id).Task.MyPayout == 0));
            // Weekend Shift, staffed: the skippers' pay
            m.PlayTask(2, id => id < 2 ? LineChoice.Volunteer : LineChoice.Skip);
            Assert.AreEqual(new[] { 0, 0, skip, skip }, m.Ids.Select(id => m.View(id).Task.MyPayout).ToArray());
            m.Finish();
            var r = m.Engine.Result;
            Assert.AreEqual(new[] { weekType, pot, box, skip }, new[] { r.Stakes.WeekType, r.Stakes.BonusPot, r.Stakes.CashTake, r.Stakes.ShiftSkipPay });
            // both take: 1 each at every level; a short shift pays nothing at every level
            var both = new LineTest(4, LineTest.SeedFor(weekType), LineTest.Agenda);
            both.PlayTask(0, id => Take);
            Assert.IsTrue(both.Ids.All(id => both.View(id).Task.MyPayout == 1));
            both.PlayTask(2, id => LineChoice.Skip);
            Assert.IsTrue(both.Ids.All(id => both.View(id).Task.MyPayout == 0));
        }

        [TestCase(0), TestCase(3), TestCase(5)]
        public void Stakes_TrioPayoutsPerLevel(int weekType)
        {
            int pot = new[] { 4, 6, 8 }[LineStakes.Level(weekType, Bonus)];
            int seed = LineTest.SeedFor(weekType, 3);
            var none = new LineTest(3, seed, LineTest.Agenda);
            none.PlayTask(0, LineTest.Share);
            Assert.IsTrue(none.Ids.All(id => none.View(id).Task.MyPayout == pot / 2), "nobody takes: pot / 2 each");
            var one = new LineTest(3, seed, LineTest.Agenda);
            one.PlayTask(0, id => id == 1 ? Take : Share);
            Assert.AreEqual(new[] { 0, pot - 1, 0 }, one.Ids.Select(id => one.View(id).Task.MyPayout).ToArray(), "one taker: pot - 1, the others 0");
            var two = new LineTest(3, seed, LineTest.Agenda);
            two.PlayTask(0, id => id == 1 ? Share : Take);
            Assert.AreEqual(new[] { 1, 1, 1 }, two.Ids.Select(id => two.View(id).Task.MyPayout).ToArray(), "two or more takers: 1 each");
        }

        [Test]
        public void Stakes_FixedSchemePlaysTheConfigsOwnNumbers()
        {
            var m = new LineTest(4, 12);
            var s = m.View().Stakes;
            Assert.AreEqual(LineStakes.Fixed, s.Scheme);
            Assert.AreEqual(-1, s.WeekType);
            Assert.AreEqual(new[] { 1, 1, 1 }, new[] { s.LevelBonus, s.LevelCash, s.LevelShift });
            Assert.AreEqual(new[] { 6, 3, 5, 1, 3, 2 }, new[] { s.BonusPot, s.BonusShare, s.BonusTaker, s.BonusBoth, s.CashTake, s.ShiftSkipPay });
            // an old config keeps its numbers whatever the Stake* table says
            var old = new LineTest(4, 12, LineTest.V50);
            Assert.AreEqual(new[] { 4, 2, 5 }, new[] { old.View().Stakes.BonusPot, old.View().Stakes.BonusShare, old.View().Stakes.BonusTaker });
            // the table is the config's: other numbers, other levels
            var custom = new LineTest(4, LineTest.SeedFor(4), c => { LineTest.Agenda(c); c.StakeBonusBig = 12; c.StakeTakerCut = 2; c.StakeCashSmall = 1; });
            Assert.AreEqual(new[] { 12, 6, 10, 1 }, new[] { custom.View().Stakes.BonusPot, custom.View().Stakes.BonusShare, custom.View().Stakes.BonusTaker, custom.View().Stakes.CashTake });
        }

        // ------------------------------------------------------------------ bots

        [Test]
        public void Bots_MixedFollowsTheStakes_HonestAndGreedyDoNot()
        {
            Assert.AreEqual(new[] { 0.25, 0.5, 0.75 }, new[] { LineBot.MixedTakeChance(0), LineBot.MixedTakeChance(1), LineBot.MixedTakeChance(2) });
            var takes = new int[3, 3];
            var total = new int[3, 3];
            for (int seed = 1; seed <= 400; seed++)
            {
                var r = LineSimulationTests.Run(4, 52000 + seed, seat => LineBotPolicy.Mixed).Result;
                foreach (var ch in r.Choices)
                {
                    int level = r.Stakes.Level(ch.Task), task = (int)ch.Task - 1;
                    total[task, level]++;
                    // (the greedy side of a decision: take the bonus / the cash, skip the shift)
                    if (ch.Task == Shift ? ch.Choice == LineChoice.Skip : ch.Choice == Take) takes[task, level]++;
                }
            }
            var rate = new double[3, 3];
            for (int t = 0; t < 3; t++) for (int l = 0; l < 3; l++) rate[t, l] = 100.0 * takes[t, l] / Math.Max(1, total[t, l]);
            TestContext.WriteLine("Mixed take % small / normal / big: bonus {0:0} {1:0} {2:0}; cash {3:0} {4:0} {5:0}; shift skip {6:0} {7:0} {8:0}",
                rate[0, 0], rate[0, 1], rate[0, 2], rate[1, 0], rate[1, 1], rate[1, 2], rate[2, 0], rate[2, 1], rate[2, 2]);
            for (int t = 0; t < 2; t++)
            {
                Assert.That(rate[t, 0], Is.InRange(18, 32), "small: about 25%");
                Assert.That(rate[t, 1], Is.InRange(43, 57), "normal: about 50%");
                Assert.That(rate[t, 2], Is.InRange(68, 82), "big: about 75%");
            }
            // (an honest decision in the shift may skip too, by its count of cleaner colleagues: only the order is fixed)
            Assert.Less(rate[2, 0], rate[2, 1]);
            Assert.Less(rate[2, 1], rate[2, 2]);
            // Honest and Greedy are the same at every level
            for (int seed = 1; seed <= 30; seed++)
            {
                var r = LineSimulationTests.Run(4, 53000 + seed, seat => seat < 2 ? LineBotPolicy.Honest : LineBotPolicy.Greedy).Result;
                foreach (var ch in r.Choices.Where(x => x.Task != Shift)) Assert.AreEqual(ch.Player < 2 ? Share : Take, ch.Choice);
                foreach (var ch in r.Choices.Where(x => x.Task == Shift && x.Player >= 2)) Assert.AreEqual(LineChoice.Skip, ch.Choice);
            }
            // fixed stakes: Mixed is the 50 / 50 it always was
            int fixedTakes = 0, fixedTotal = 0;
            for (int seed = 1; seed <= 150; seed++)
                foreach (var ch in LineSimulationTests.Run(4, 54000 + seed, seat => LineBotPolicy.Mixed, configure: c => c.StakesScheme = LineStakes.Fixed).Result.Choices.Where(x => x.Task != Shift))
                {
                    fixedTotal++;
                    if (ch.Choice == Take) fixedTakes++;
                }
            Assert.That(100.0 * fixedTakes / fixedTotal, Is.InRange(44, 56));
        }

        static LineView SeatView(int me, int players = 4)
        {
            var v = new LineView { Me = me, WeekId = 3 };
            for (int i = 0; i < players; i++) v.Players.Add(new LinePlayerView { Id = i, Connected = true });
            v.Mine.Prim = 6;
            return v;
        }

        static LineTaskView Revealed(TaskKind kind, int[] group, int[] choices, int takers = -1)
        {
            var t = new LineTaskView { Kind = kind, Number = kind == Bonus ? 0 : 1, Revealed = true, Takers = takers };
            t.Group.AddRange(group);
            for (int i = 0; i < group.Length; i++) t.Choices.Add(new LineChoiceView { Task = kind, Player = group[i], Choice = choices[i] });
            return t;
        }

        [Test]
        public void Ledger_AnOpenedFileTellsWhoTookAndWhoKeepsTheCash_AtTheMatchsAmounts()
        {
            var c = LineConfig.CreateDefault(); // (the caller's config says 3 / 5 / 3: the view's stakes win)
            var stakes = new LineStakesView { Scheme = LineStakes.Permutation, WeekType = 5, LevelBonus = 2, LevelCash = 1, LevelShift = 0, BonusPot = 8, BonusShare = 4, BonusTaker = 7, BonusBoth = 1, CashTake = 3, ShiftSkipPay = 1 };
            var ledger = new LineBotLedger(0);
            // my Client Bonus pair shared; one of the other pair took (the public count), I cannot tell who
            var reveal = SeatView(0);
            reveal.Stakes = stakes;
            reveal.Mine.Prim = 10;
            reveal.Task = Revealed(Bonus, new[] { 0, 1 }, new[] { Share, Share }, takers: 1);
            ledger.Ingest(reveal);
            var e = ledger.Estimate(c);
            Assert.AreEqual(10, e[0]);
            Assert.AreEqual(6 + 4, e[1], 1e-9, "my partner shared a big bonus: pot / 2");
            Assert.AreEqual(e[2], e[3], 1e-9, "one of them took");
            // (taker with a taking / sharing partner, sharer with a taking / sharing partner: each half the time)
            Assert.AreEqual(6 + 0.5 * (0.5 * 1 + 0.5 * 7) + 0.5 * (0.5 * 0 + 0.5 * 4), e[2], 1e-9);
            // 2 opens its file on 3: 3 took the whole pot less one, 2 shared and got nothing
            var open = SeatView(0);
            open.Stakes = stakes;
            open.Mine.Prim = 10;
            open.Files.Opened.Add(new LineFileView { Task = Bonus, Holder = 2, Target = 3, State = LineFileState.Opened, Time = 50, Blame = 4 });
            ledger.Ingest(open);
            e = ledger.Estimate(c);
            Assert.AreEqual(6 + 7, e[3], 1e-9, "an opened Client Bonus file: its target took");
            Assert.AreEqual(6, e[2], 1e-9, "and its holder shared");

            // Petty Cash: my pair left it; the meeting counts one unresolved theft in the other pair
            var cash = SeatView(0);
            cash.Stakes = stakes;
            cash.Mine.Prim = 10;
            cash.Task = Revealed(Cash, new[] { 0, 2 }, new[] { Leave, Leave });
            ledger.Ingest(cash);
            var meeting = SeatView(0);
            meeting.Stakes = stakes;
            meeting.Mine.Prim = 10;
            meeting.Meeting = new LineMeetingView { Index = 0 };
            meeting.Meeting.Dossier.Add(new LineDossierEntry { Kind = DossierKind.CashMissing, Task = Cash, N1 = 3, N2 = 1, Time = 100 });
            ledger.Ingest(meeting);
            e = ledger.Estimate(c);
            Assert.Greater(e[3], 6 + 7, "an unresolved theft: 1 or 3 has the box");
            Assert.Greater(e[1], 6 + 4);
            // 1 opens its file on 3 after the dossier: 3 is the thief and keeps the cash, 1 was the witness
            meeting.Files.Opened.Add(new LineFileView { Task = Bonus, Holder = 2, Target = 3, State = LineFileState.Opened, Time = 50, Blame = 4 });
            meeting.Files.Opened.Add(new LineFileView { Task = Cash, Holder = 1, Target = 3, State = LineFileState.Opened, Time = 120, Blame = 3 });
            ledger.Ingest(meeting);
            e = ledger.Estimate(c);
            Assert.AreEqual(6 + 7 + 3, e[3], 1e-9, "an opened Petty Cash file: its target keeps the box");
            Assert.AreEqual(6 + 4, e[1], 1e-9, "the holder was a witness, and the theft is settled");
            Assert.AreEqual(6, e[2], 1e-9);
            // the shift at the small level: a skipper's pay is 1
            var shift = SeatView(0);
            shift.Stakes = stakes;
            shift.Mine.Prim = 10;
            shift.Task = new LineTaskView { Kind = Shift, Number = 2, Revealed = true, Needed = 2 };
            shift.Task.Roster.AddRange(new[] { 0, 1 });
            ledger.Ingest(shift);
            Assert.AreEqual(6 + 7 + 3 + 1, ledger.Estimate(c)[3], 1e-9);
            // fixed stakes: the config's numbers
            var plain = new LineBotLedger(0);
            var fixedReveal = SeatView(0);
            fixedReveal.Task = Revealed(Bonus, new[] { 0, 1 }, new[] { Share, Share }, takers: 0);
            plain.Ingest(fixedReveal);
            Assert.AreEqual(6 + c.BonusPairShare, plain.Estimate(c)[1], 1e-9);
        }

        [Test]
        public void Ledger_InAMatch_EveryoneLearnsFromAnOpenedFile()
        {
            var m = new LineTest(4);
            m.PlayTask(0, id => id == 0 ? Take : Share);
            int p = Partner(m, 0);
            int other = m.Ids.First(x => x != 0 && x != p);
            var bot = new LineBot(other, 5);
            m.Next(); // the break: the reveal is gone from the other pair's view
            var before = bot.EstimatedPrim(m.View(other));
            m.OpenFile(p, 0, Bonus);
            var after = bot.EstimatedPrim(m.View(other));
            Assert.Less(before[0], 6 + 5);
            Assert.AreEqual(6 + 5, after[0], 1e-9, "the taker is known now");
            Assert.AreEqual(6, after[p], 1e-9, "and so is the sharer");
        }

        // ------------------------------------------------------------------ codec

        [Test]
        public void Codec_RoundTripsFilesStakesAndTheNewVerbsAndEvents()
        {
            var m = new LineTest(4, LineTest.SeedFor(1), LineTest.Agenda);
            m.PlayTask(0, id => id == 0 ? Take : Share);
            int p = Partner(m, 0);
            m.PlayTask(1, id => id == 0 ? Take : Leave);
            int w = Partner(m, 0);
            m.Events.Clear();
            m.OpenFile(p, 0, Bonus);
            m.ShredFile(w, 0, Cash);
            foreach (int id in m.Ids)
            {
                var v = m.View(id);
                var back = LineCodec.DecodeView(LineCodec.EncodeView(v));
                Assert.AreEqual(v.Features.FilesOpen, back.Features.FilesOpen);
                Assert.AreEqual(new[] { v.Files.Held, v.Files.Shredded, v.Files.Expired, v.Files.Opened.Count }, new[] { back.Files.Held, back.Files.Shredded, back.Files.Expired, back.Files.Opened.Count });
                Assert.AreEqual(v.Mine.Files.Count, back.Mine.Files.Count);
                for (int i = 0; i < v.Mine.Files.Count; i++)
                {
                    LineFileView a = v.Mine.Files[i], b = back.Mine.Files[i];
                    Assert.AreEqual(new object[] { a.Task, a.Holder, a.Target, a.State, a.Time, a.Phase, a.Blame }, new object[] { b.Task, b.Holder, b.Target, b.State, b.Time, b.Phase, b.Blame });
                }
                Assert.AreEqual(new[] { v.Stakes.Scheme, v.Stakes.WeekType, v.Stakes.LevelBonus, v.Stakes.LevelCash, v.Stakes.LevelShift, v.Stakes.BonusPot, v.Stakes.BonusShare, v.Stakes.BonusTaker, v.Stakes.BonusBoth, v.Stakes.CashTake, v.Stakes.ShiftSkipPay },
                    new[] { back.Stakes.Scheme, back.Stakes.WeekType, back.Stakes.LevelBonus, back.Stakes.LevelCash, back.Stakes.LevelShift, back.Stakes.BonusPot, back.Stakes.BonusShare, back.Stakes.BonusTaker, back.Stakes.BonusBoth, back.Stakes.CashTake, back.Stakes.ShiftSkipPay });
            }
            var events = LineCodec.DecodeEvents(LineCodec.EncodeEvents(m.Events));
            CollectionAssert.AreEqual(m.Events.Select(e => e.Type + ":" + e.Actor + ":" + e.Target + ":" + e.A + ":" + e.B + ":" + e.C + ":" + e.Audience + ":" + e.AudiencePlayer),
                events.Select(e => e.Type + ":" + e.Actor + ":" + e.Target + ":" + e.A + ":" + e.B + ":" + e.C + ":" + e.Audience + ":" + e.AudiencePlayer));
            Assert.IsTrue(events.Any(e => e.Type == LineEventType.FileOpened) && events.Any(e => e.Type == LineEventType.FileShredded) && events.Any(e => e.Type == LineEventType.FileNotice));
            foreach (var verb in new[] { LineVerb.FileOpen, LineVerb.FileShred })
            {
                var a = LineCodec.DecodeAction(LineCodec.EncodeAction(new LineAction(7, 3, verb, 2, (int)Cash)));
                Assert.AreEqual(new object[] { verb, 2, (int)Cash }, new object[] { a.Verb, a.Target, a.A });
            }
            m.Finish();
            var r = m.Engine.Result;
            var again = LineCodec.DecodeView(LineCodec.EncodeView(new LineView { Results = r })).Results;
            CollectionAssert.AreEqual(r.Files.Select(f => f.Task + ":" + f.Holder + ":" + f.Target + ":" + f.State + ":" + f.Phase),
                again.Files.Select(f => f.Task + ":" + f.Holder + ":" + f.Target + ":" + f.State + ":" + f.Phase));
            Assert.AreEqual(1, again.Stakes.WeekType);
            Assert.AreEqual(DossierKind.FileOpened, again.Incidents.Single(d => d.Kind == DossierKind.FileOpened).Kind);
            Assert.AreEqual(Bonus, again.Incidents.Single(d => d.Kind == DossierKind.FileOpened).Task);
            // the result hash covers what became of the files
            var shredded = new LineTest(4, LineTest.SeedFor(1), LineTest.Agenda);
            shredded.PlayTask(0, id => id == 0 ? Take : Share);
            shredded.PlayTask(1, id => id == 0 ? Take : Leave);
            shredded.ShredFile(p, 0, Bonus);
            shredded.ShredFile(w, 0, Cash);
            shredded.Finish();
            Assert.AreNotEqual(r.ResultHash, shredded.Engine.Result.ResultHash);
        }
    }

    internal static class LineRulesV53TestExtensions
    {
        /// <summary>From anywhere before the shift: everyone volunteers, then on to the crisis calls.</summary>
        internal static void ToCrisisFromHere(this LineTest m)
        {
            m.PlayTask(2, id => LineChoice.Volunteer);
            m.Until(LinePhase.CrisisCalls);
        }
    }
}
