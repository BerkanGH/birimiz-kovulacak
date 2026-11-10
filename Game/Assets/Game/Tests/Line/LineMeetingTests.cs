using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BirimizKovulacak.Core.Tests.Line;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class LineMeetingTests
    {
        const int Take = LineChoice.Take;

        /// <summary>4 players; seat 0 takes the client bonus, a petty cash thief stays unresolved. Returns (bonus partner of 0, thief).</summary>
        static (int partner, int thief) TwoFailures(LineTest m)
        {
            m.PlayTask(0, id => id == 0 ? Take : 0);
            int partner = m.Group(0).Single(x => x != 0);
            m.Next(); m.Next();
            var g0 = m.Group(0);
            int thief = m.Ids.First(id => !g0.Contains(id) && id != partner);
            m.PlayTask(1, id => id == thief ? Take : 0);
            m.Until(LinePhase.MeetingDossier);
            return (partner, thief);
        }

        [Test]
        public void TwoVoteableFailuresGiveTwoBallotsEachAndEmptyBallotsGoToSelf()
        {
            var m = new LineTest();
            var (_, thief) = TwoFailures(m);
            var meeting = m.View().Meeting;
            CollectionAssert.AreEquivalent(new[] { TaskKind.ClientBonus, TaskKind.PettyCash }, meeting.FailedTasks);
            Assert.AreEqual(1, meeting.Dossier.Single(d => d.Kind == DossierKind.BonusTakers).N1);
            Assert.AreEqual(-1, meeting.VoteableCount);
            m.Fail(LineFailure.WrongPhase, 1, LineVerb.Vote, 0, 0);
            m.Until(LinePhase.MeetingVote);
            Assert.AreEqual(2, m.View().Meeting.VoteableCount);
            Assert.AreEqual(2, m.View(1).Mine.Ballots.Count);
            m.Fail(LineFailure.BadParam, 1, LineVerb.Vote, 0, 2);
            m.Fail(LineFailure.Self, 1, LineVerb.Vote, 1, 0);
            m.Fail(LineFailure.InvalidTarget, 1, LineVerb.Vote, 5, 0);
            m.Ok(1, LineVerb.Vote, 0, 0);
            Assert.IsFalse(m.Player(1).Voted, "one of two ballots");
            m.Ok(1, LineVerb.Vote, 0, 1);
            Assert.IsTrue(m.Player(1).Voted);
            m.Ok(1, LineVerb.Vote, 3, 1);       // change
            m.Ok(2, LineVerb.Vote, 0, 0);       // second ballot left empty → self
            m.Ok(3, LineVerb.Vote, -1, 0);      // clear: still empty
            CollectionAssert.AreEqual(new[] { 0, 3 }, m.View(1).Mine.Ballots);
            CollectionAssert.AreEqual(new[] { -1, -1 }, m.View(0).Mine.Ballots, "ballots are secret");
            Assert.IsTrue(m.Events.Where(e => e.Type == LineEventType.VoteCast).All(e => e.Target == -1 && e.Audience == LineAudience.All));
            m.Next();
            Assert.AreEqual(LinePhase.MeetingVerdict, m.Engine.Phase);
            // 0: two votes from 1/2 (+1 from 1 then changed) → 0 gets 1 (seat 1 ballot 0) + 1 (seat 2) + 2 (own empties) = 4
            // 1: none; 2: one self; 3: one from 1 + two self = 3.
            var gained = m.View().Meeting.BlameGained.ToDictionary(g => g.Player, g => g.Amount);
            Assert.AreEqual(4, gained[0]);
            Assert.AreEqual(0, gained[1]);
            Assert.AreEqual(1, gained[2]);
            Assert.AreEqual(3, gained[3]);
            Assert.AreEqual(4, m.Blame(0));
            Assert.AreEqual(3, m.Blame(3));
            m.Finish();
            var meetingBallots = m.Engine.Result.Ballots.Where(b => b.Kind == BallotKind.Meeting && b.Round == 0).ToList();
            Assert.AreEqual(8, meetingBallots.Count);
            Assert.AreEqual(5, meetingBallots.Count(b => b.SelfAssigned));
        }

        [Test]
        public void DisconnectedPlayersEmptyBallotsAreDropped()
        {
            var m = new LineTest();
            TwoFailures(m);
            m.Until(LinePhase.MeetingVote);
            m.Ok(2, LineVerb.Vote, 1, 0);
            m.Engine.SetConnected(2, false, m.Now);
            m.Next();
            var gained = m.View().Meeting.BlameGained.ToDictionary(g => g.Player, g => g.Amount);
            Assert.AreEqual(0, gained[2], "the absent player's empty ballot is not self-assigned");
            Assert.AreEqual(1 + 2, gained[1], "cast ballot still counts");
        }

        [Test]
        public void ExposingTheOnlyTakerRemovesTheBonusFailureFromTheVote()
        {
            var m = new LineTest();
            var (partner, thief) = TwoFailures(m);
            m.Ok(partner, LineVerb.Investigate, 0);
            m.Until(LinePhase.MeetingVote);
            Assert.AreEqual(1, m.View().Meeting.VoteableCount, "only petty cash is left");
        }

        [Test]
        public void GuiltyInvestigationCostsFourRefundsThreeAndExposesTheTaker()
        {
            var m = new LineTest();
            var (partner, _) = TwoFailures(m);
            int partnerPrim = m.Prim(partner), takerPrim = m.Prim(0);
            Assert.AreEqual(6, partnerPrim);
            Assert.AreEqual(11, takerPrim);
            CollectionAssert.AreEqual(new[] { 0 }, m.View(partner).Mine.InvestigationTargets);
            m.Ok(partner, LineVerb.Investigate, 0);
            Assert.AreEqual(6 - 4 + 3, m.Prim(partner));
            Assert.AreEqual(11 - 3, m.Prim(0));
            Assert.AreEqual(3, m.Blame(0));
            Assert.AreEqual(0, m.Blame(partner));
            Assert.IsTrue(m.View(partner).Mine.InvestigationUsed);
            var line = m.View(3).Meeting.Dossier.Single(d => d.Kind == DossierKind.Investigation);
            Assert.AreEqual(partner, line.Player);
            Assert.AreEqual(0, line.Other);
            Assert.AreEqual(1, line.N1);
            Assert.IsTrue(m.Events.Any(e => e.Type == LineEventType.Investigated && e.Audience == LineAudience.All && e.A == 1));
            m.Fail(LineFailure.AlreadyUsed, partner, LineVerb.Investigate, 0);
        }

        [Test]
        public void InnocentInvestigationBlamesTheInvestigator()
        {
            var m = new LineTest();
            var (partner, _) = TwoFailures(m);
            m.Ok(0, LineVerb.Investigate, partner);
            Assert.AreEqual(11 - 4, m.Prim(0));
            Assert.AreEqual(6, m.Prim(partner));
            Assert.AreEqual(3, m.Blame(0));
            Assert.AreEqual(0, m.Blame(partner));
            Assert.AreEqual(0, m.View(3).Meeting.Dossier.Single(d => d.Kind == DossierKind.Investigation).N1);
        }

        [Test]
        public void InvestigationNeedsTheSameGroupThePhaseAndThePrim()
        {
            var m = new LineTest();
            m.PlayTask(0, id => id == 0 ? Take : 0);
            int partner = m.Group(0).Single(x => x != 0);
            int outsider = m.Ids.First(id => id != 0 && id != partner);
            m.Fail(LineFailure.WrongPhase, partner, LineVerb.Investigate, 0);
            m.Next(); m.Next();
            m.PlayTask(1, id => 0);
            m.Ok(partner, LineVerb.Transfer, outsider, 5);
            m.Until(LinePhase.MeetingDossier);
            m.Fail(LineFailure.NotEligible, outsider, LineVerb.Investigate, 0);
            m.Fail(LineFailure.Self, partner, LineVerb.Investigate, partner);
            m.Fail(LineFailure.InsufficientFunds, partner, LineVerb.Investigate, 0);
            m.Next();
            Assert.AreEqual(LinePhase.MeetingDiscussion, m.Engine.Phase);
            m.Ok(outsider, LineVerb.Transfer, partner, 5);
            m.Ok(partner, LineVerb.Investigate, 0);
            m.Until(LinePhase.MeetingVote);
            m.Fail(LineFailure.WrongPhase, 0, LineVerb.Investigate, partner);
        }

        [Test]
        public void TrioMatesCannotInvestigateTheSameTargetTwice()
        {
            var m = new LineTest(3);
            m.PlayTask(0, id => id == 0 ? Take : 0);
            m.ToMeeting1Continue(id => 0);
            m.Ok(1, LineVerb.Investigate, 0);
            m.Fail(LineFailure.AlreadyResolved, 2, LineVerb.Investigate, 0);
            CollectionAssert.AreEqual(new[] { 1 }, m.View(2).Mine.InvestigationTargets);
        }

        [Test]
        public void InvestigationIsOnlyForMeeting1()
        {
            var m = new LineTest();
            m.PlayTask(0, id => id == 0 ? Take : 0);
            int partner = m.Group(0).Single(x => x != 0);
            m.ToMeeting1Continue(id => 0);
            m.Until(LinePhase.MeetingVerdict);
            m.PlayTask(2, id => 0);
            m.Until(LinePhase.MeetingDossier);
            Assert.AreEqual(1, m.Engine.Day);
            Assert.IsFalse(m.View(partner).Features.InvestigationOpen);
            m.Fail(LineFailure.WrongPhase, partner, LineVerb.Investigate, 0);
        }

        [Test]
        public void Meeting2HasOneBallotForTheFailedShift()
        {
            var m = new LineTest(5);
            m.ToMeeting1();
            m.PlayTask(2, id => 0);
            m.Until(LinePhase.MeetingVote, 1);
            Assert.AreEqual(1, m.View().Meeting.VoteableCount);
            foreach (int id in m.Ids) m.Ok(id, LineVerb.Vote, id == 4 ? 3 : 4, 0);
            m.Next();
            Assert.AreEqual(4, m.Blame(4));
            Assert.AreEqual(1, m.Blame(3));
        }
    }
}
