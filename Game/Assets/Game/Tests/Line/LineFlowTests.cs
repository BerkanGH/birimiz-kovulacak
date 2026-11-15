using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BirimizKovulacak.Core.Tests.Line;
using P = BirimizKovulacak.Core.LinePhase;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class LineFlowTests
    {
        static readonly P[] Tasks12 = { P.Intro, P.TaskPlay, P.TaskReveal, P.Break, P.TaskPlay, P.TaskReveal, P.Break };
        static readonly P[] FinalTail = { P.CrisisCalls, P.CrisisVote, P.CrisisVerdict, P.FinalDiscussion, P.FinalDefense };

        [TestCase(3), TestCase(4), TestCase(5), TestCase(6)]
        public void DefaultMatchSkipsMeeting1VoteAndRunsMeeting2(int n)
        {
            // Defaults: share, leave, skip. Tasks 1-2 succeed (no vote-able failure); task 3 fails (no volunteers): meeting 2
            // is the dossier and the discussion (v5.1: the skippers' suç is automatic, no vote).
            var m = new LineTest(n);
            m.Finish();
            var expected = new List<P>(Tasks12) { P.MeetingDossier, P.MeetingDiscussion, P.TaskPlay, P.TaskReveal,
                P.MeetingDossier, P.MeetingDiscussion };
            expected.AddRange(FinalTail);
            int firings = m.Config.FiringsFor(n);
            for (int i = 0; i < firings; i++)
            {
                expected.Add(P.FinalVote);
                // With nobody voting every candidate ties: one revote, then the tie chain.
                expected.Add(P.FinalVote);
                expected.Add(P.FinalVerdict);
            }
            expected.Add(P.Results);
            CollectionAssert.AreEqual(expected, m.PhaseTrace());
            Assert.AreEqual(firings, m.Engine.Result.Fired.Count);
        }

        [TestCase(3), TestCase(4), TestCase(5), TestCase(6)]
        public void SuccessfulTask3SkipsMeeting2AndFailedTask1HoldsMeeting1Vote(int n)
        {
            var m = new LineTest(n);
            m.PlayTask(0, id => id == 0 ? LineChoice.Take : LineChoice.Share);
            m.PlayTask(1, LineTest.Share);
            m.Until(P.MeetingDossier);
            m.Until(P.MeetingVote);
            Assert.AreEqual(1, m.View().Meeting.VoteableCount);
            m.Until(P.MeetingVerdict);
            m.PlayTask(2, id => LineChoice.Volunteer);
            m.Next();
            Assert.AreEqual(P.CrisisCalls, m.Engine.Phase);
            var trace = m.PhaseTrace();
            CollectionAssert.AreEqual(new[] { P.MeetingDossier, P.MeetingDiscussion, P.MeetingVote, P.MeetingVerdict, P.TaskPlay, P.TaskReveal, P.CrisisCalls },
                trace.Skip(Tasks12.Length).ToList());
            var skipped = m.Events.Last(e => e.Type == LineEventType.PhaseChanged);
            Assert.AreEqual((int)PhaseEndReason.Skipped, skipped.B);
        }

        [Test]
        public void DayIsTheMeetingIndex()
        {
            var m = new LineTest();
            Assert.AreEqual(0, m.Engine.Day);
            m.ToMeeting1();
            Assert.AreEqual(0, m.Engine.Day);
            m.Until(P.TaskPlay);
            Assert.AreEqual(1, m.Engine.Day);
            m.Until(P.MeetingDossier);
            Assert.AreEqual(1, m.Engine.Day);
            m.Until(P.CrisisCalls);
            Assert.AreEqual(2, m.Engine.Day);
        }

        [Test]
        public void PhaseDurationsFollowConfig()
        {
            var m = new LineTest();
            var d = m.Config.Durations;
            Assert.AreEqual(d.Intro, m.Engine.PhaseEndTime - m.Now, 1e-6);
            m.Next();
            Assert.AreEqual(P.TaskPlay, m.Engine.Phase);
            Assert.AreEqual(d.TaskPlay, m.Engine.PhaseEndTime - m.Now, 1e-6);
            m.Next();
            Assert.AreEqual(d.TaskReveal, m.Engine.PhaseEndTime - m.Now, 1e-6);
            m.Next();
            Assert.AreEqual(d.Break, m.Engine.PhaseEndTime - m.Now, 1e-6);
            m.Until(P.MeetingDossier);
            Assert.AreEqual(d.MeetingDossier, m.Engine.PhaseEndTime - m.Now, 1e-6);
            m.Next();
            Assert.AreEqual(d.MeetingDiscussion, m.Engine.PhaseEndTime - m.Now, 1e-6);
            m.Until(P.CrisisCalls);
            Assert.AreEqual(d.CrisisCalls, m.Engine.PhaseEndTime - m.Now, 1e-6);
            m.Next();
            Assert.AreEqual(d.CrisisVote, m.Engine.PhaseEndTime - m.Now, 1e-6);
            m.Next();
            Assert.AreEqual(d.CrisisVerdict, m.Engine.PhaseEndTime - m.Now, 1e-6);
            m.Next();
            Assert.AreEqual(d.FinalDiscussion, m.Engine.PhaseEndTime - m.Now, 1e-6);
            m.Next();
            int candidates = m.View().Final.Candidates.Count;
            Assert.AreEqual(d.FinalDefense * candidates, m.Engine.PhaseEndTime - m.Now, 1e-6);
            m.Next();
            Assert.AreEqual(d.FinalVote, m.Engine.PhaseEndTime - m.Now, 1e-6);
            m.Next();
            Assert.IsTrue(m.View().Final.Revote);
            Assert.AreEqual(d.FinalRevote, m.Engine.PhaseEndTime - m.Now, 1e-6);
            m.Next();
            Assert.AreEqual(d.FinalVerdict, m.Engine.PhaseEndTime - m.Now, 1e-6);
            m.Next();
            Assert.AreEqual(P.Results, m.Engine.Phase);
            Assert.AreEqual(d.Results, m.Engine.PhaseEndTime - m.Now, 1e-6);
        }

        [Test]
        public void EveryoneSubmittedEndsTaskPlayThreeSecondsLaterWithoutCarry()
        {
            var m = new LineTest();
            m.Next();
            m.Advance(10);
            foreach (int id in m.Ids.Take(3)) m.Ok(id, LineVerb.SubmitTask, a: 0);
            Assert.AreEqual(m.Config.Durations.Intro + m.Config.Durations.TaskPlay, m.Engine.PhaseEndTime, 1e-6);
            m.Ok(3, LineVerb.SubmitTask, a: 1);
            Assert.AreEqual(m.Now + 3, m.Engine.PhaseEndTime, 1e-6);
            m.Advance(3);
            Assert.AreEqual(P.TaskReveal, m.Engine.Phase);
            Assert.AreEqual((int)PhaseEndReason.TimerShortened, m.Events.Last(e => e.Type == LineEventType.PhaseChanged).B);
            Assert.AreEqual(m.Config.Durations.TaskReveal, m.Engine.PhaseEndTime - m.Now, 1e-6);
        }

        [Test]
        public void EarlyFinishCountsOnlyConnectedPlayers()
        {
            var m = new LineTest();
            m.Next();
            m.Engine.SetConnected(3, false, m.Now);
            foreach (int id in m.Ids.Take(3)) m.Ok(id, LineVerb.SubmitTask, a: 0);
            Assert.AreEqual(m.Now + 3, m.Engine.PhaseEndTime, 1e-6);
        }

        [Test]
        public void AbsentPlayersTakeTheDefaultChoiceAndCannotAct()
        {
            var m = new LineTest();
            m.Next();
            m.Engine.SetConnected(2, false, m.Now);
            m.Fail(LineFailure.Absent, 2, LineVerb.SubmitTask, a: 1);
            m.Until(P.TaskReveal);
            var mine = m.View(2).Task.Choices.Single(c => c.Player == 2);
            Assert.AreEqual(LineChoice.Share, mine.Choice);
        }

        [Test]
        public void HostContinueEndsResultsOnlyAndRequestIdsAreDeduplicated()
        {
            var m = new LineTest();
            m.HostContinueIgnoredOutsideResults();
            var dup = m.Engine.Apply(0, new LineAction(m.Setup.WeekId, 5, LineVerb.SendText, 1, text: "a"), m.Now);
            Assert.IsTrue(dup.Accepted);
            m.Now += 5;
            var again = m.Engine.Apply(0, new LineAction(m.Setup.WeekId, 5, LineVerb.SendText, 1, text: "a"), m.Now);
            Assert.AreEqual(LineFailure.Duplicate, again.Failure);
            Assert.AreEqual(LineFailure.StaleWeek, m.Engine.Apply(0, new LineAction(7, 6, LineVerb.SendText, 1, text: "a"), m.Now).Failure);
            Assert.AreEqual(LineFailure.HostOnly, m.Engine.Apply(0, new LineAction(m.Setup.WeekId, 9, LineVerb.HostContinue), m.Now).Failure);
            m.Until(P.Results);
            Assert.IsFalse(m.Engine.IsFinished);
            Assert.NotNull(m.View().Results);
            m.Engine.HostContinue(m.Now);
            Assert.IsTrue(m.Engine.IsFinished);
        }

        [Test]
        public void FewerThanTwoConnectedForSixtySecondsAbandons()
        {
            var m = new LineTest(3);
            m.Engine.SetConnected(1, false, 5);
            m.Engine.SetConnected(2, false, 6);
            m.Now = 65.5;
            m.Engine.Tick(m.Now);
            Assert.IsFalse(m.Engine.IsFinished);
            m.Engine.SetConnected(2, true, 65.9);
            m.Engine.SetConnected(2, false, 70);
            m.Now = 129.9; m.Engine.Tick(m.Now);
            Assert.IsFalse(m.Engine.IsFinished);
            m.Now = 130; m.Engine.Tick(m.Now);
            Assert.IsTrue(m.Engine.IsFinished);
            Assert.AreEqual(P.Abandoned, m.Engine.Phase);
            Assert.IsNull(m.Engine.Result);
            Assert.AreEqual(0, m.Engine.NextHistory().WeeksPlayed);
        }

        [Test]
        public void TimeMustBeFiniteAndMonotonic()
        {
            var m = new LineTest();
            m.Advance(5);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => m.Engine.Tick(4));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => m.Engine.Tick(double.NaN));
        }

        [Test]
        public void NextHistoryCountsWinsAndFirings()
        {
            var m = new LineTest();
            m.Finish();
            var r = m.Engine.Result;
            var h = m.Engine.NextHistory();
            Assert.AreEqual(1, h.WeeksPlayed);
            foreach (int id in m.Ids)
            {
                Assert.AreEqual(r.Winners.Contains(id) ? 1 : 0, h.Wins[id]);
                Assert.AreEqual(r.Fired.Contains(id) ? 1 : 0, h.TimesFired[id]);
            }
            var setup = LineTest.MakeSetup();
            setup.History = h;
            var next = LineEngineFactory.Create(setup);
            Assert.AreEqual(P.Intro, next.Phase);
        }

        [Test]
        public void ForcedTaskOrderIsHonouredAndValidated()
        {
            var m = new LineTest(prepare: s => s.ForceTasks = new List<TaskKind> { TaskKind.PettyCash, TaskKind.ClientBonus });
            m.Next();
            Assert.AreEqual(TaskKind.PettyCash, m.View().Task.Kind);
            var bad = LineTest.MakeSetup();
            bad.ForceTasks = new List<TaskKind> { TaskKind.WeekendShift };
            Assert.Throws<System.ArgumentException>(() => LineEngineFactory.Create(bad));
            bad.ForceTasks = new List<TaskKind> { TaskKind.ClientBonus, TaskKind.ClientBonus };
            Assert.Throws<System.ArgumentException>(() => LineEngineFactory.Create(bad));
        }
    }

    internal static class LineFlowTestExtensions
    {
        internal static void HostContinueIgnoredOutsideResults(this LineTest m)
        {
            m.Engine.HostContinue(m.Now);
            Assert.IsFalse(m.Engine.IsFinished);
            Assert.AreEqual(LinePhase.Intro, m.Engine.Phase);
        }
    }
}
