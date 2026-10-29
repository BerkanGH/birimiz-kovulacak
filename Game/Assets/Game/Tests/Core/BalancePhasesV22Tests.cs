using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class BalancePhasesV22Tests
    {
        private static TestMatch WeekMatch()
        {
            MatchSetup setup = TestMatch.Setup();
            setup.Mode = GameMode.Week; setup.WeekDay = 4; setup.ManagerProfileId = "legacy_test";
            return new TestMatch(setup: setup, configure: c =>
            {
                c.Reconnect.GraceSeconds = 1;
                c.Durations.MeetingDiscussion = c.Durations.MeetingDefense = c.Durations.MeetingVote = 4;
                c.Durations.MeetingVerdict = 4;
                c.Verdict.VoteWeight = 1; c.Verdict.RiskWeight = c.Verdict.PerformanceWeight = c.Verdict.FavorWeight = 0;
            });
        }

        [TestCase(MatchPhase.MeetingDiscussion)]
        [TestCase(MatchPhase.MeetingDefense1)]
        [TestCase(MatchPhase.MeetingDefense2)]
        [TestCase(MatchPhase.MeetingVote)]
        [TestCase(MatchPhase.MeetingVerdict)]
        public void MeetingQuitterRemainsCandidateAndReceivesNormalCareerResult(MatchPhase disconnectPhase)
        {
            TestMatch m = WeekMatch();
            m.Phase(MatchPhase.MeetingDiscussion);
            m.Do(1, ActionVerb.Accuse, param: 0); m.Do(2, ActionVerb.Accuse, param: 0);
            if (disconnectPhase == MatchPhase.MeetingVerdict)
            {
                m.Phase(MatchPhase.MeetingVote);
                foreach (int p in new[] { 1, 2, 3 }) m.Do(p, ActionVerb.CastBallot, param: 0);
            }
            m.Phase(disconnectPhase);
            m.Engine.SetConnected(0, false, m.Now);
            m.Advance(1.01);
            Assert.AreEqual(disconnectPhase, m.Engine.Phase);
            Assert.IsFalse(m.Player(0).Connected);
            Assert.AreEqual(FailureCode.NotConnected, m.Request(0, ActionVerb.Accuse, param: 1).Failure);
            Assert.AreEqual(FailureCode.NotConnected, m.Request(0, ActionVerb.CastBallot, param: 1).Failure);
            if (disconnectPhase != MatchPhase.MeetingVerdict)
            {
                m.Phase(MatchPhase.MeetingVote);
                foreach (int p in new[] { 1, 2, 3 }) m.Do(p, ActionVerb.CastBallot, param: 0);
            }
            m.Finish();
            MatchResultView result = m.View().Result;
            Assert.AreEqual(0, result.FiredPlayer);
            Assert.IsTrue(result.Verdict.Candidates.Any(c => c.Player == 0));
            PlayerOutcome quitter = result.Players.Single(p => p.Player == 0);
            Assert.AreEqual(m.Config.Career.Fired, quitter.CareerDelta);
            Assert.IsFalse(m.Player(0).Connected);
            Assert.IsFalse(m.View().Meeting.Voters.Contains(0));
            Assert.IsTrue(result.WeekFinished);
            Assert.AreEqual(0, m.Engine.BuildNextHistory().NextWeekDay);
            Assert.AreEqual(1, m.Engine.BuildNextHistory().WeekNumber);
        }

        [TestCase(-0.01, true)]
        [TestCase(0, false)]
        [TestCase(0.01, false)]
        public void MeetingBoundaryStopsGraceAbandonment(double expiryOffset, bool abandoned)
        {
            TestMatch m = WeekMatch(); m.Phase(MatchPhase.FinalInspection);
            double meetingStart = m.View().PhaseEndTime;
            m.AdvanceTo(meetingStart - m.Config.Reconnect.GraceSeconds + expiryOffset);
            m.Engine.SetConnected(0, false, m.Now);
            m.AdvanceTo(meetingStart + 0.02);
            Assert.AreEqual(abandoned ? MatchPhase.Abandoned : MatchPhase.MeetingDiscussion, m.Engine.Phase);
            if (!abandoned) { m.Finish(); Assert.IsNotNull(m.View().Result); }
        }

        [Test]
        public void AllDisconnectedMeetingPlayersStillProduceVerdictAndResults()
        {
            TestMatch m = WeekMatch(); m.Phase(MatchPhase.MeetingDiscussion);
            foreach (int p in m.Engine.Players) m.Engine.SetConnected(p, false, m.Now);
            m.Finish();
            Assert.AreEqual(4, m.View().Result.Players.Count);
            Assert.AreEqual(0, m.View().Meeting.BallotsCast);
            Assert.Contains(m.View().Result.FiredPlayer, m.Engine.Players.ToArray());
            Assert.IsTrue(m.View().Players.All(p => !p.Connected));
        }

        [Test]
        public void ZeroLengthFinalDoesNotAbandonAtTheMeetingDeadline()
        {
            var m = new TestMatch(configure: c => { c.Durations.FinalInspection = 0; c.Reconnect.GraceSeconds = 1; });
            m.Phase(MatchPhase.Work2);
            double meetingStart = m.View().PhaseEndTime;
            m.AdvanceTo(meetingStart - 1);
            m.Engine.SetConnected(0, false, m.Now);
            m.AdvanceTo(meetingStart);
            Assert.AreEqual(MatchPhase.MeetingDiscussion, m.Engine.Phase);
            m.Finish(); Assert.IsNotNull(m.View().Result);
        }

        private static TestMatch OutageMatch()
        {
            MatchSetup setup = TestMatch.Setup();
            setup.ForceCards = new List<List<SabotageType>>
            {
                new List<SabotageType> { SabotageType.InternetOutage, SabotageType.PowerCut },
                new List<SabotageType>(), new List<SabotageType>(), new List<SabotageType>()
            };
            return new TestMatch(setup: setup);
        }

        [TestCase(ActionVerb.CutInternet, EntityIds.Router)]
        [TestCase(ActionVerb.CutPower, EntityIds.FuseBox)]
        public void FinalWarningRejectsOutagesAndOffersWrongPhaseHint(ActionVerb verb, string target)
        {
            TestMatch m = OutageMatch(); m.Phase(MatchPhase.FinalInspection); m.MoveTo(0, target);
            Assert.AreEqual(ManagerState.Warning, m.View().Manager.State);
            Assert.AreEqual(FailureCode.WrongPhase, m.Request(0, verb, target).Failure);
            Affordance option = Affordances.For(m.View(), m.Engine.Config, m.Layout, target,
                m.Layout.GetStation(target).Position).Single(a => a.Verb == verb);
            Assert.AreEqual(FailureCode.WrongPhase, option.BlockedBy);
            Assert.IsFalse(m.View().Office.InternetDown); Assert.IsFalse(m.View().Office.PowerDown);
        }

        [TestCase(ActionVerb.CutInternet, EntityIds.Router, SabotageType.InternetOutage)]
        [TestCase(ActionVerb.CutPower, EntityIds.FuseBox, SabotageType.PowerCut)]
        public void OutageCompletingAtFinalBoundaryIsCancelled(ActionVerb verb, string target, SabotageType type)
        {
            TestMatch m = OutageMatch(); m.Phase(MatchPhase.Work2);
            double finalStart = m.View().PhaseEndTime;
            m.AdvanceTo(finalStart - m.Config.GetSabotage(type).DurationSeconds);
            Assert.AreEqual(finalStart, m.Start(0, verb, target).CompletesAt, 0.00001);
            m.AdvanceTo(finalStart + 0.001);
            Assert.IsNull(m.View().Me.ActiveInteraction);
            Assert.IsFalse(m.View().Office.InternetDown); Assert.IsFalse(m.View().Office.PowerDown);
            Assert.IsFalse(m.Engine.BuildHostDebugView().Incidents.Any(i => i.Type == type));
        }

        [TestCase(ActionVerb.CutInternet, ActionVerb.RestartRouter, EntityIds.Router)]
        [TestCase(ActionVerb.CutPower, ActionVerb.ResetFuse, EntityIds.FuseBox)]
        public void ExistingOutageCanStillBeRestoredDuringFinal(ActionVerb cut, ActionVerb restore, string target)
        {
            TestMatch m = OutageMatch(); m.Phase(MatchPhase.Work2);
            m.AdvanceTo(m.View().PhaseEndTime - 10);
            m.Do(0, cut, target);
            m.Phase(MatchPhase.FinalInspection);
            m.Do(1, restore, target);
            Assert.IsFalse(m.View().Office.InternetDown); Assert.IsFalse(m.View().Office.PowerDown);
        }
    }
}
