using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BirimizKovulacak.Core.Tests.Line;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class LineTaskTests
    {
        const int Take = LineChoice.Take;

        static List<List<int>> Groups(LineTest m)
        {
            var groups = new List<List<int>>();
            foreach (int id in m.Ids)
            {
                var g = m.Group(id).OrderBy(x => x).ToList();
                if (!groups.Any(x => x.SequenceEqual(g))) groups.Add(g);
            }
            return groups;
        }

        // ---- grouping

        [TestCase(3), TestCase(4), TestCase(5), TestCase(6)]
        public void PairsCoverEveryoneOnceAndOddCountsGetOneTrio(int n)
        {
            var m = new LineTest(n);
            m.Next();
            var groups = Groups(m);
            CollectionAssert.AreEquivalent(m.Ids, groups.SelectMany(g => g));
            Assert.AreEqual(n % 2 == 1 ? 1 : 0, groups.Count(g => g.Count == 3));
            Assert.IsTrue(groups.All(g => g.Count == 2 || g.Count == 3));
            var task = m.View().Task;
            Assert.AreEqual(TaskKind.ClientBonus, task.Kind);
            Assert.AreEqual(TaskFamily.Pair, task.Family);
            Assert.AreEqual(-1, task.Needed);
        }

        [TestCase(4), TestCase(6)]
        public void Task2NeverRepeatsATask1PairWhenAvoidable(int n)
        {
            for (int seed = 1; seed <= 40; seed++)
            {
                var m = new LineTest(n, seed);
                m.Next();
                var first = Groups(m);
                m.Until(LinePhase.TaskPlay); m.Next(); m.Next(); m.Next();
                Assert.AreEqual(1, m.View().TaskNumber);
                var second = Groups(m);
                foreach (var g in second) Assert.IsFalse(first.Any(f => f.Contains(g[0]) && f.Contains(g[1])), "seed " + seed);
            }
        }

        [Test]
        public void FivePlayersRepeatAtMostOnePairAndChangeTheTrio()
        {
            for (int seed = 1; seed <= 40; seed++)
            {
                var m = new LineTest(5, seed);
                m.Next();
                var first = Groups(m);
                m.Next(); m.Next(); m.Next();
                var second = Groups(m);
                int repeats = 0;
                foreach (var g in second)
                    for (int a = 0; a < g.Count; a++)
                        for (int b = a + 1; b < g.Count; b++)
                            if (first.Any(f => f.Contains(g[a]) && f.Contains(g[b]))) repeats++;
                Assert.AreEqual(1, repeats, "the minimum for five players is one repeated pair (seed " + seed + ")");
                Assert.IsFalse(first.Single(g => g.Count == 3).SequenceEqual(second.Single(g => g.Count == 3)));
            }
        }

        [Test]
        public void GroupingIsDeterministicPerSeed()
        {
            var a = new LineTest(6, 99); a.Next();
            var b = new LineTest(6, 99); b.Next();
            Assert.AreEqual(string.Join(";", Groups(a).Select(g => string.Join(",", g))), string.Join(";", Groups(b).Select(g => string.Join(",", g))));
        }

        // ---- submission

        [Test]
        public void ChoicesAreChangeableUntilPlayEndsAndValidated()
        {
            var m = new LineTest();
            m.Fail(LineFailure.WrongPhase, 0, LineVerb.SubmitTask, a: 1);
            m.Next();
            m.Fail(LineFailure.BadParam, 0, LineVerb.SubmitTask, a: 2);
            m.Ok(0, LineVerb.SubmitTask, a: Take);
            Assert.AreEqual(Take, m.View(0).Task.MyChoice);
            Assert.IsTrue(m.View(1).Players.Single(p => p.Id == 0).Submitted);
            m.Ok(0, LineVerb.SubmitTask, a: LineChoice.Share);
            Assert.AreEqual(LineChoice.Share, m.View(0).Task.MyChoice);
            Assert.AreEqual(2, m.Events.Count(e => e.Type == LineEventType.TaskSubmitted && e.Actor == 0));
            m.Until(LinePhase.TaskReveal);
            m.Fail(LineFailure.WrongPhase, 0, LineVerb.SubmitTask, a: 1);
            Assert.AreEqual(LineChoice.Share, m.View(0).Task.Choices.Single(c => c.Player == 0).Choice);
        }

        // ---- Client Bonus

        [Test]
        public void ClientBonusPairPayouts()
        {
            var m = new LineTest(4);
            m.Next();
            var groups = Groups(m);
            var a = groups[0]; var b = groups[1];
            // group a: share/take, group b: take/take.
            m.PlayTask(0, id => id == a[1] || b.Contains(id) ? Take : LineChoice.Share);
            Assert.AreEqual(6 + 0, m.Prim(a[0]));
            Assert.AreEqual(6 + 5, m.Prim(a[1]));
            Assert.AreEqual(6 + 1, m.Prim(b[0]));
            Assert.AreEqual(6 + 1, m.Prim(b[1]));
            Assert.AreEqual(3, m.View(a[0]).Task.Takers);
            Assert.AreEqual(5, m.View(a[1]).Task.MyPayout);
            var seen = m.View(a[0]).Task.Choices;
            CollectionAssert.AreEquivalent(a, seen.Select(c => c.Player));
            Assert.AreEqual(Take, seen.Single(c => c.Player == a[1]).Choice);
            Assert.AreEqual(-1, seen.Single(c => c.Player == a[1]).Payout, "a partner's payout is not shown");

            var both = new LineTest(4);
            both.PlayTask(0, LineTest.Share);
            foreach (int id in both.Ids) Assert.AreEqual(6 + 3, both.Prim(id), "v5.1: both share 3 each");
            Assert.AreEqual(0, both.View().Task.Takers);
        }

        [TestCase(0, 3, 3, 3)]
        [TestCase(1, 5, 0, 0)]
        [TestCase(2, 1, 1, 1)]
        [TestCase(3, 1, 1, 1)]
        public void ClientBonusTrioPayouts(int takers, int p0, int p1, int p2)
        {
            var m = new LineTest(3);
            m.PlayTask(0, id => id < takers ? Take : LineChoice.Share);
            // Payouts of the ordered members: seat 0 takes first.
            int[] expected = { p0, p1, p2 };
            for (int id = 0; id < 3; id++) Assert.AreEqual(6 + expected[id], m.Prim(id), "seat " + id);
            Assert.AreEqual(takers, m.View().Task.Takers);
        }

        [Test]
        public void FiveSeatsTrioPaysTheSoleTakerFive()
        {
            var m = new LineTest(5);
            m.Next();
            var trio = Groups(m).Single(g => g.Count == 3);
            m.PlayTask(0, id => id == trio[2] ? Take : LineChoice.Share);
            Assert.AreEqual(11, m.Prim(trio[2]));
            Assert.AreEqual(6, m.Prim(trio[0]));
            Assert.AreEqual(6, m.Prim(trio[1]));
        }

        // ---- Petty Cash

        [Test]
        public void PettyCashSoleTakerGetsThreeAndWitnessesSeeIt()
        {
            var m = new LineTest(4);
            m.PlayTask(0, LineTest.Share);
            m.Next(); m.Next();
            var g = m.Group(0);
            int thief = g[0], witness = g[1];
            m.PlayTask(1, id => id == thief ? Take : LineChoice.Leave);
            Assert.AreEqual(9 + 3, m.Prim(thief));
            Assert.AreEqual(9, m.Prim(witness));
            Assert.AreEqual(Take, m.View(witness).Task.Choices.Single(c => c.Player == thief).Choice);
            int outsider = m.Ids.First(id => !g.Contains(id));
            Assert.IsFalse(m.View(outsider).Task.Choices.Any(c => c.Player == thief), "outside the group nothing is visible");
            Assert.AreEqual(-1, m.View(outsider).Task.Takers);
            m.Next();
            Assert.AreEqual(LinePhase.Break, m.Engine.Phase);
            CollectionAssert.AreEqual(new[] { thief }, m.View(witness).Mine.WitnessOf);
            CollectionAssert.IsEmpty(m.View(outsider).Mine.WitnessOf);
            Assert.IsTrue(m.View(witness).Features.WhistleOpen);
        }

        [Test]
        public void UnresolvedTheftFailsPettyCashWithMissingTotalButNoNames()
        {
            var m = new LineTest(6);
            m.PlayTask(0, LineTest.Share);
            m.Next(); m.Next();
            var groups = Groups(m);
            var thieves = new[] { groups[0][0], groups[1][1] };
            m.ToMeeting1Continue(id => thieves.Contains(id) ? Take : 0);
            var meeting = m.View().Meeting;
            CollectionAssert.Contains(meeting.FailedTasks, TaskKind.PettyCash);
            var missing = meeting.Dossier.Single(d => d.Kind == DossierKind.CashMissing);
            Assert.AreEqual(6, missing.N1);
            Assert.AreEqual(2, missing.N2);
            Assert.AreEqual(-1, missing.Player);
            Assert.IsFalse(meeting.Dossier.Any(d => d.Player == thieves[0] || d.Other == thieves[0]));
            m.Until(LinePhase.MeetingVote);
            Assert.AreEqual(1, m.View().Meeting.VoteableCount);
        }

        [Test]
        public void TwoTakersInAGroupAreCaughtGetNothingAndTwoSucAtMeeting1()
        {
            var m = new LineTest(4);
            m.PlayTask(0, LineTest.Share);
            m.Next(); m.Next();
            var g = m.Group(0);
            m.PlayTask(1, id => g.Contains(id) ? Take : 0);
            foreach (int id in g) Assert.AreEqual(9, m.Prim(id));
            foreach (int id in g) Assert.AreEqual(0, m.Blame(id), "suç is announced in meeting 1, not at reveal");
            CollectionAssert.IsEmpty(m.View(g[0]).Mine.WitnessOf);
            m.Until(LinePhase.MeetingDossier);
            foreach (int id in g) Assert.AreEqual(2, m.Blame(id));
            var meeting = m.View(3).Meeting;
            CollectionAssert.AreEquivalent(g, meeting.Dossier.Where(d => d.Kind == DossierKind.CashCaught).Select(d => d.Player));
            CollectionAssert.DoesNotContain(meeting.FailedTasks, TaskKind.PettyCash);
            m.Until(LinePhase.TaskPlay);
            Assert.IsFalse(m.PhaseTrace().Contains(LinePhase.MeetingVote), "caught thefts are resolved: no vote");
        }

        [Test]
        public void WhistleIsFreeTheThiefKeepsTheCashAndItIsPublicInMeeting1()
        {
            var m = new LineTest(4);
            m.PlayTask(0, LineTest.Share);
            m.Next(); m.Next();
            var g = m.Group(0);
            int thief = g[0], witness = g[1];
            int outsider = m.Ids.First(id => !g.Contains(id));
            m.PlayTask(1, id => id == thief ? Take : 0);
            m.Fail(LineFailure.WrongPhase, witness, LineVerb.Whistle, thief);
            m.Next();
            m.Fail(LineFailure.Self, thief, LineVerb.Whistle, thief);
            m.Fail(LineFailure.NotEligible, outsider, LineVerb.Whistle, thief);
            m.Fail(LineFailure.NotEligible, thief, LineVerb.Whistle, witness);
            m.Ok(witness, LineVerb.Whistle, thief);
            m.Fail(LineFailure.AlreadyResolved, witness, LineVerb.Whistle, thief);
            Assert.AreEqual(9 + 3, m.Prim(thief), "v5.1: the whistled thief keeps the cash");
            Assert.AreEqual(9, m.Prim(witness), "v5.1: no whistle reward, no cost");
            Assert.AreEqual(0, m.Blame(thief), "suç lands with the dossier");
            Assert.IsTrue(m.Events.Any(e => e.Type == LineEventType.Whistled && e.AudiencePlayer == thief && e.A == 0), "nothing taken back");
            Assert.IsFalse(m.Events.Any(e => e.Type == LineEventType.Whistled && e.IsVisibleTo(outsider)));
            m.Next();
            Assert.AreEqual(LinePhase.MeetingDossier, m.Engine.Phase);
            Assert.AreEqual(3, m.Blame(thief));
            var line = m.View(outsider).Meeting.Dossier.Single(d => d.Kind == DossierKind.Whistle);
            Assert.AreEqual(witness, line.Player);
            Assert.AreEqual(thief, line.Other);
            CollectionAssert.DoesNotContain(m.View().Meeting.FailedTasks, TaskKind.PettyCash);
            m.Until(LinePhase.TaskPlay);
            Assert.IsFalse(m.PhaseTrace().Contains(LinePhase.MeetingVote));
        }

        [Test]
        public void V50WhistledThiefWhoSpentTheCashLosesOnlyWhatIsLeft()
        {
            var m = new LineTest(4, configure: LineTest.V50);
            m.PlayTask(0, LineTest.Share);
            m.Next(); m.Next();
            var g = m.Group(0);
            int thief = g[0], witness = g[1];
            m.PlayTask(1, id => id == thief ? Take : 0);
            m.Ok(thief, LineVerb.Transfer, witness, 10);
            Assert.AreEqual(1, m.Prim(thief));
            m.Next();
            m.Ok(witness, LineVerb.Whistle, thief);
            Assert.AreEqual(0, m.Prim(thief), "v5.0: the cash is taken back (what is left of it)");
            Assert.AreEqual(8 + 10 + 2, m.Prim(witness), "v5.0: the whistler earns 2");
        }

        [Test]
        public void TrioWitnessesShareOneWhistle()
        {
            var m = new LineTest(3);
            m.PlayTask(0, LineTest.Share);
            m.PlayTask(1, id => id == 1 ? Take : 0);
            m.Next();
            CollectionAssert.AreEqual(new[] { 1 }, m.View(0).Mine.WitnessOf);
            CollectionAssert.AreEqual(new[] { 1 }, m.View(2).Mine.WitnessOf);
            m.Ok(2, LineVerb.Whistle, 1);
            m.Fail(LineFailure.AlreadyResolved, 0, LineVerb.Whistle, 1);
            CollectionAssert.IsEmpty(m.View(0).Mine.WitnessOf);
        }

        // ---- Weekend Shift

        [TestCase(3, 2), TestCase(4, 2), TestCase(5, 3), TestCase(6, 3)]
        public void WeekendShiftNeedsHalfRoundedUpAndPaysTheSkippers(int n, int needed)
        {
            var m = new LineTest(n);
            m.ToMeeting1();
            m.Until(LinePhase.TaskPlay);
            var task = m.View().Task;
            Assert.AreEqual(TaskKind.WeekendShift, task.Kind);
            Assert.AreEqual(TaskFamily.All, task.Family);
            Assert.AreEqual(needed, task.Needed);
            CollectionAssert.AreEquivalent(m.Ids, task.Group);
            int before = m.Prim(0);
            m.PlayTask(2, id => id < needed ? LineChoice.Volunteer : LineChoice.Skip);
            for (int id = 0; id < n; id++) Assert.AreEqual(before + (id < needed ? 0 : 2), m.Prim(id));
            CollectionAssert.AreEqual(Enumerable.Range(0, needed), m.View(n - 1).Task.Roster, "roster is public");
            m.Next();
            Assert.AreEqual(LinePhase.CrisisCalls, m.Engine.Phase, "enough volunteers: meeting 2 skipped");
        }

        [Test]
        public void WeekendShiftOneShortFailsAndMeeting2ShowsTheRoster()
        {
            var m = new LineTest(6);
            m.ToMeeting1();
            m.PlayTask(2, id => id < 2 ? LineChoice.Volunteer : LineChoice.Skip);
            m.Next();
            Assert.AreEqual(LinePhase.MeetingDossier, m.Engine.Phase);
            var meeting = m.View(5).Meeting;
            Assert.AreEqual(1, meeting.Index);
            CollectionAssert.AreEqual(new[] { TaskKind.WeekendShift }, meeting.FailedTasks);
            CollectionAssert.AreEqual(new[] { 0, 1 }, meeting.Roster);
            var line = meeting.Dossier.Single(d => d.Kind == DossierKind.ShiftRoster);
            Assert.AreEqual(2, line.N1);
            Assert.AreEqual(3, line.N2);
        }
    }

    internal static class LineTaskTestExtensions
    {
        /// <summary>From the TaskPlay of task 2 (index 1): submit cash choices and run to meeting 1's dossier.</summary>
        internal static void ToMeeting1Continue(this LineTest m, Func<int, int> cash)
        {
            m.PlayTask(1, cash);
            m.Until(LinePhase.MeetingDossier);
        }
    }
}
