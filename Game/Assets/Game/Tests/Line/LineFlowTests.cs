using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests.Line
{
    [TestFixture]
    public sealed class LineFlowTests
    {
        [TestCase(0)] [TestCase(2)] [TestCase(7)]
        public void InvalidSeatCountsAreRejected(int count)
        {
            Assert.Throws<ArgumentException>(() => new LineTest(LineTest.MakeSetup(count)));
        }

        [TestCase(-1)] [TestCase(6)] [TestCase(1)]
        public void InvalidOrDuplicateIdsAreRejected(int id)
        {
            var s = LineTest.MakeSetup(); s.Players[0].Id = id;
            Assert.Throws<ArgumentException>(() => new LineTest(s));
        }

        [Test]
        public void SparseSeatsAreSupportedAndAgendasAreDeranged()
        {
            var s = LineTest.MakeSetup(3); s.Players[0].Id = 5;
            var m = new LineTest(s); var targets = new HashSet<int>();
            foreach (var p in s.Players)
            {
                var v = m.View(p.Id); Assert.AreNotEqual(p.Id, v.Mine.AgendaTarget);
                Assert.IsTrue(s.Players.Any(q => q.Id == v.Mine.AgendaTarget)); targets.Add(v.Mine.AgendaTarget);
            }
            Assert.AreEqual(3, targets.Count);
            m.Finish(); Assert.AreEqual(3, m.Engine.Result.Players.Count);
        }

        [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)]
        public void WeekAssignmentsAreDistinctAndDeranged(int count)
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var m = new LineTest(LineTest.MakeSetup(count, seed));
                var agendas = new HashSet<int>(); var tricks = new HashSet<TrickType>();
                for (int p = 0; p < count; p++)
                {
                    var mine = m.View(p).Mine; Assert.AreNotEqual(p, mine.AgendaTarget);
                    Assert.IsTrue(agendas.Add(mine.AgendaTarget)); Assert.IsTrue(tricks.Add(mine.Trick));
                    Assert.AreNotEqual(TrickType.None, mine.Trick);
                }
            }
        }

        [Test]
        public void FirstWeekScheduleDurationsAndUnlocks()
        {
            var m = new LineTest();
            CollectionAssert.AreEqual(new[] { RoundType.PrimHavuzu, RoundType.IhbarKutusu }, m.View().TodayRounds);
            Assert.AreEqual(m.Config.Durations.MorningFirst, m.Engine.PhaseEndTime - m.Now);
            Assert.IsTrue(m.View().Features.OnboardingDay); Assert.IsFalse(m.View().Features.TricksUnlocked);
            Assert.AreEqual(LineFailure.NotUnlocked, m.Do(0, LineVerb.TipOff, 1).Failure);
            m.Next(); Assert.AreEqual(LinePhase.RoundIntro, m.Engine.Phase);
            Assert.IsTrue(m.View().Features.FirstTimeRound);
            Assert.AreEqual(m.Config.Durations.RoundIntroFirst, m.Engine.PhaseEndTime - m.Now);
            Assert.AreEqual(LineFailure.NotUnlocked, m.Do(0, LineVerb.UseTrick, 1, 0).Failure);
            m.Until(LinePhase.Morning, 1);
            Assert.IsTrue(m.View().Features.TricksUnlocked); Assert.IsTrue(m.View().Features.TipLineUnlocked);
            Assert.IsFalse(m.View().Features.OnboardingDay);
            Assert.AreEqual(m.Config.Durations.Morning, m.Engine.PhaseEndTime - m.Now);
        }

        [Test]
        public void ThreePlayerSchedulesExcludeLostReportAndDaysDoNotRepeat()
        {
            var schedules = new HashSet<string>();
            for (int seed = 0; seed < 20; seed++)
            {
                var s = LineTest.MakeSetup(3, seed); s.Length = WeekLength.Full;
                var m = new LineTest(s); var all = new List<RoundType>();
                for (int day = 0; day < 5; day++)
                {
                    m.Until(LinePhase.Morning, day); var rounds = m.View().TodayRounds;
                    Assert.AreEqual(2, rounds.Distinct().Count()); CollectionAssert.DoesNotContain(rounds, RoundType.KayipRapor);
                    all.AddRange(rounds);
                }
                schedules.Add(string.Join(",", all));
                var counts = all.GroupBy(r => r).Select(g => g.Count()).ToArray();
                Assert.LessOrEqual(counts.Max() - counts.Min(), 1);
            }
            Assert.Greater(schedules.Count, 1);
        }

        [Test]
        public void LaterWeekAvoidsPreviousManagerAndUsesShortIntroductions()
        {
            var s = LineTest.MakeSetup(); s.ManagerProfileId = "";
            s.History = new LineSessionHistory { WeeksPlayed = 4 };
            s.History.PastManagers.Add(LineManagerIds.Disiplinci);
            s.History.SeenRounds.AddRange(new[] { RoundType.PrimHavuzu, RoundType.FarkliEposta, RoundType.KayipRapor, RoundType.IhbarKutusu, RoundType.TerfiListesi, RoundType.OrtakSifre });
            var m = new LineTest(s);
            Assert.AreNotEqual(LineManagerIds.Disiplinci, m.View().ManagerProfileId);
            Assert.IsTrue(m.View().Features.TricksUnlocked);
            m.Next(); Assert.IsFalse(m.View().Features.FirstTimeRound);
            Assert.AreEqual(m.Config.Durations.RoundIntro, m.Engine.PhaseEndTime - m.Now);
        }

        [Test]
        public void NextMorningFavouriteExcludesTheUniqueBlameLeader()
        {
            var s = LineTest.MakeSetup(); s.ManagerProfileId = LineManagerIds.Kayirmaci;
            var m = new LineTest(s); m.Until(LinePhase.MeetingVote);
            for (int p = 1; p < 4; p++) m.Ok(p, LineVerb.Vote, 0);
            m.Next(); Assert.Greater(m.Player(0).Blame, m.Player(1).Blame);
            m.Until(LinePhase.Morning, 1); Assert.AreNotEqual(0, m.View().Favourite);
        }

        [Test]
        public void LateTickMatchesSteppedClockAcrossEveryBoundary()
        {
            var a = new LineTest(); var b = new LineTest();
            a.Finish(); b.Advance(a.Now);
            Assert.IsTrue(b.Engine.IsFinished); Assert.AreEqual(a.Engine.Result.ResultHash, b.Engine.Result.ResultHash);
            CollectionAssert.AreEqual(LineCodec.EncodeEvents(a.Engine.DrainEvents()), LineCodec.EncodeEvents(b.Engine.DrainEvents()));
        }

        [Test]
        public void AllConnectedSubmissionsShortenEndAndOverwritesAreAllowed()
        {
            var m = new LineTest(); m.Until(LinePhase.RoundPlay); double original = m.Engine.PhaseEndTime;
            m.Engine.SetConnected(3, false, m.Now);
            for (int p = 0; p < 3; p++) m.Ok(p, LineVerb.SubmitChoice, a: 0);
            Assert.AreEqual(m.Now + m.Config.Durations.AllSubmittedRemaining, m.Engine.PhaseEndTime);
            Assert.Less(m.Engine.PhaseEndTime, original); m.Ok(0, LineVerb.SubmitChoice, a: 1);
            Assert.AreEqual(1, m.View().Round.MyA); Assert.IsFalse(m.Player(3).Submitted);
        }

        [Test]
        public void LowPresenceAbandonsOnlyAfterContinuousSixtySeconds()
        {
            var m = new LineTest();
            for (int p = 1; p < 4; p++) m.Engine.SetConnected(p, false, m.Now);
            m.Advance(59); Assert.IsFalse(m.Engine.IsFinished);
            m.Engine.SetConnected(1, true, m.Now); m.Advance(2); Assert.AreNotEqual(LinePhase.Abandoned, m.Engine.Phase);
            m.Engine.SetConnected(1, false, m.Now); m.Advance(59); Assert.IsFalse(m.Engine.IsFinished);
            m.Advance(1); Assert.IsTrue(m.Engine.IsFinished); Assert.AreEqual(LinePhase.Abandoned, m.Engine.Phase);
            Assert.IsNull(m.Engine.Result);
        }

        [Test]
        public void DuplicateStaleAndHostOnlyActionsAreRejected()
        {
            var m = new LineTest(); var a = new LineAction(m.Setup.WeekId, 100, LineVerb.SendMail, 1, 0);
            Assert.IsTrue(m.Engine.Apply(0, a, m.Now).Accepted);
            Assert.AreEqual(LineFailure.Duplicate, m.Engine.Apply(0, a, m.Now).Failure);
            a = new LineAction(m.Setup.WeekId + 1, 101, LineVerb.Call, 1);
            Assert.AreEqual(LineFailure.StaleWeek, m.Engine.Apply(0, a, m.Now).Failure);
            m.Until(LinePhase.WeekResults);
            Assert.AreEqual(LineFailure.HostOnly, m.Engine.Apply(0, new LineAction(m.Setup.WeekId, 102, LineVerb.HostContinue), m.Now).Failure);
            Assert.IsNotNull(m.Engine.Result); m.Engine.HostContinue(m.Now); Assert.IsTrue(m.Engine.IsFinished);
        }

        [Test]
        public void ViewIsADetachedSnapshot()
        {
            var m = new LineTest(); var v = m.View(); v.Players[0].Blame = 999; v.Mine.Inbox.Clear(); v.TodayRounds.Clear();
            Assert.AreEqual(0, m.Player(0).Blame); Assert.IsNotEmpty(m.View().Mine.Inbox); Assert.AreEqual(2, m.View().TodayRounds.Count);
            m.Until(LinePhase.RoundReveal); var original = m.View().Round.Reveal.Pairs[0].ChoiceA;
            m.View().Round.Reveal.Pairs[0].ChoiceA = 999;
            Assert.AreEqual(original, m.View().Round.Reveal.Pairs[0].ChoiceA);
        }
    }
}
