using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BirimizKovulacak.Core.Tests.Line;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class LineFinalTests
    {
        /// <summary>Clean tasks (everyone ends with 10 prim), meeting 2 and crisis ballots as given (-1 = leave empty).</summary>
        static LineTest Crisis(int n, int[] meeting2, int[] crisis, Action<LineTest> beforeCrisisVote = null, int seed = 731)
        {
            var m = new LineTest(n, seed);
            m.ToCrisis(id => LineChoice.Skip, id => meeting2[id]);
            beforeCrisisVote?.Invoke(m);
            m.Next();
            Assert.AreEqual(LinePhase.CrisisVote, m.Engine.Phase);
            foreach (int id in m.Ids) if (crisis[id] >= 0) m.Ok(id, LineVerb.Vote, crisis[id]);
            m.Next();
            Assert.AreEqual(LinePhase.CrisisVerdict, m.Engine.Phase);
            return m;
        }

        static void ToFinalVote(LineTest m) { m.Until(LinePhase.FinalVote); }

        [Test]
        public void CrisisBlameGoesToThePluralityAndEmptyBallotsToSelf()
        {
            // meeting 2: everyone votes 3 except 3 (votes 0) → blame 3:3, 0:1.
            var m = Crisis(4, new[] { 3, 3, 3, 0 }, new[] { 1, 2, 1, -1 });
            // crisis votes: 1 ← 0, 2; 2 ← 1; 3 ← self. 1 wins with 2.
            Assert.AreEqual(4, m.Blame(1));
            Assert.AreEqual(0, m.Blame(2));
            var crisis = m.View().Final.Crisis;
            Assert.AreEqual(1, crisis.Count);
            Assert.AreEqual(1, crisis[0].Player);
            Assert.AreEqual(4, crisis[0].Amount);
            var e = m.Events.Last(x => x.Type == LineEventType.CrisisResolved);
            Assert.AreEqual(1, e.Target);
            Assert.IsTrue(m.Engine.Result == null);
        }

        [Test]
        public void CrisisTieSplitsTheBlameFlooringTheRemainder()
        {
            var two = Crisis(4, new[] { 1, 0, 0, 0 }, new[] { 1, 0, 1, 0 });
            Assert.AreEqual(2, two.View().Final.Crisis.Single(c => c.Player == 0).Amount);
            Assert.AreEqual(2, two.View().Final.Crisis.Single(c => c.Player == 1).Amount);
            Assert.AreEqual(-1, two.Events.Last(x => x.Type == LineEventType.CrisisResolved).Target);
            var three = Crisis(3, new[] { 1, 2, 0 }, new[] { 1, 2, 0 });
            Assert.AreEqual(3, three.View().Final.Crisis.Count);
            Assert.IsTrue(three.View().Final.Crisis.All(c => c.Amount == 1), "4 / 3 = 1, remainder dropped");
            foreach (int id in three.Ids) Assert.AreEqual(1 + 1, three.Blame(id));
        }

        [Test]
        public void CrisisBallotsAreSecretUntilResults()
        {
            var m = new LineTest();
            m.ToCrisis();
            m.Next();
            m.Ok(0, LineVerb.Vote, 2);
            m.Fail(LineFailure.Self, 1, LineVerb.Vote, 1);
            CollectionAssert.AreEqual(new[] { -1 }, m.View(2).Mine.Ballots);
            Assert.IsTrue(m.Player(0).Voted);
            Assert.IsEmpty(m.View(2).Final.Crisis);
        }

        [Test]
        public void CandidatesIncludeEveryoneTiedWithTheLastCandidate()
        {
            // meeting 2: 1←0,3; 2←1; 3←2 → blame 1:2, 2:1, 3:1. Crisis: 1 ← 0,2,3 → 1:6.
            var m = Crisis(4, new[] { 1, 2, 3, 1 }, new[] { 1, 0, 1, 1 });
            var final = m.View().Final;
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, final.Candidates, "2 candidates for 4 players, 3 tied at the cut");
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, final.DefenseOrder);
            Assert.IsTrue(m.Player(2).Candidate);
            Assert.IsFalse(m.Player(0).Candidate);
            Assert.AreEqual(1, final.FiringsTotal);
        }

        [Test]
        public void DefenseGivesTheFloorToOneCandidateAtATime()
        {
            var m = Crisis(4, new[] { 1, 2, 3, 1 }, new[] { 1, 0, 1, 1 });
            m.Until(LinePhase.FinalDiscussion);
            Assert.IsTrue(m.Ids.All(id => m.Engine.MaySpeak(id)));
            m.Next();
            Assert.AreEqual(LinePhase.FinalDefense, m.Engine.Phase);
            Assert.AreEqual(1, m.View().Final.Defender);
            Assert.IsTrue(m.Engine.MaySpeak(1));
            Assert.IsFalse(m.Engine.MaySpeak(0));
            Assert.IsFalse(m.Engine.MaySpeak(2));
            m.Advance(15);
            Assert.AreEqual(2, m.View().Final.Defender);
            Assert.IsTrue(m.Engine.MaySpeak(2));
            Assert.IsFalse(m.Engine.MaySpeak(1));
            m.Advance(15);
            Assert.AreEqual(3, m.View().Final.Defender);
            m.Advance(15);
            Assert.AreEqual(LinePhase.FinalVote, m.Engine.Phase);
            Assert.IsTrue(m.Engine.MaySpeak(1));
        }

        [Test]
        public void FinalVotePluralityFiresAndSplitsTheDeskAmongItsVoters()
        {
            var m = Crisis(4, new[] { 1, 2, 3, 1 }, new[] { 1, 0, 1, 1 });
            ToFinalVote(m);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, m.View().Final.Options);
            m.Fail(LineFailure.InvalidTarget, 1, LineVerb.Vote, 0);   // not a candidate
            m.Fail(LineFailure.Self, 1, LineVerb.Vote, 1);
            m.Ok(0, LineVerb.Vote, 2);
            m.Ok(1, LineVerb.Vote, 2);
            m.Ok(3, LineVerb.Vote, 2);
            // seat 2 abstains
            Assert.AreEqual(10, m.Prim(2));
            m.Next();
            Assert.AreEqual(LinePhase.FinalVerdict, m.Engine.Phase);
            var firing = m.View(0).Final.Firings.Single();
            Assert.AreEqual(2, firing.Player);
            Assert.AreEqual(FiringDecision.Plurality, firing.Decision);
            Assert.AreEqual(10, firing.Desk);
            CollectionAssert.AreEqual(new[] { 0, 1, 3 }, firing.Recipients);
            Assert.AreEqual(3, firing.Share);
            Assert.AreEqual(1, firing.Removed);
            Assert.AreEqual(0, m.Prim(2));
            Assert.AreEqual(13, m.Prim(0));
            Assert.IsTrue(m.Player(2).Fired);
            m.Next();
            Assert.AreEqual(LinePhase.Results, m.Engine.Phase);
            var r = m.View().Results;
            CollectionAssert.AreEqual(new[] { 2 }, r.Fired);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 3 }, r.Winners, "13 each: shared win");
            Assert.IsTrue(r.Players.Single(p => p.Player == 0).Winner);
        }

        [Test]
        public void NobodyVotingForTheFiredRemovesTheWholeDesk()
        {
            var m = Crisis(4, new[] { 1, 2, 3, 1 }, new[] { 1, 0, 1, 1 });
            ToFinalVote(m);
            m.Ok(0, LineVerb.Vote, 1);
            m.Next();
            var firing = m.View().Final.Firings.Single();
            Assert.AreEqual(1, firing.Player);
            CollectionAssert.AreEqual(new[] { 0 }, firing.Recipients);
            Assert.AreEqual(20, m.Prim(0));

            var empty = Crisis(4, new[] { 1, 2, 3, 1 }, new[] { 1, 0, 1, 1 });
            ToFinalVote(empty);
            empty.Next();                    // tie (no votes) → revote
            Assert.IsTrue(empty.View().Final.Revote);
            empty.Next();                    // still tied → higher suç (seat 1: 6)
            var f = empty.View().Final.Firings.Single();
            Assert.AreEqual(1, f.Player);
            Assert.AreEqual(FiringDecision.HigherBlame, f.Decision);
            Assert.AreEqual(0, f.Recipients.Count);
            Assert.AreEqual(10, f.Removed);
        }

        [Test]
        public void TieTriggersOneRevoteAmongTheTiedThatCanDecide()
        {
            var m = Crisis(4, new[] { 1, 2, 3, 1 }, new[] { 1, 0, 1, 1 });
            ToFinalVote(m);
            m.Ok(0, LineVerb.Vote, 2);
            m.Ok(1, LineVerb.Vote, 3);
            m.Next();
            Assert.AreEqual(LinePhase.FinalVote, m.Engine.Phase);
            var final = m.View().Final;
            Assert.IsTrue(final.Revote);
            CollectionAssert.AreEqual(new[] { 2, 3 }, final.Options);
            CollectionAssert.AreEqual(new[] { -1 }, m.View(0).Mine.Ballots, "ballots reset for the revote");
            m.Fail(LineFailure.InvalidTarget, 0, LineVerb.Vote, 1);
            m.Ok(0, LineVerb.Vote, 3);
            m.Ok(2, LineVerb.Vote, 3);
            m.Next();
            var firing = m.View().Final.Firings.Single();
            Assert.AreEqual(3, firing.Player);
            Assert.AreEqual(FiringDecision.Revote, firing.Decision);
            CollectionAssert.AreEqual(new[] { 0, 2 }, firing.Recipients, "only the deciding vote pays");
            var ballots = m.View().Results;
            Assert.IsNull(ballots);
            m.Finish();
            Assert.AreEqual(2 + 2, m.Engine.Result.Ballots.Count(b => b.Kind == BallotKind.Final && b.Target >= 0));
        }

        [Test]
        public void StillTiedFallsBackToHigherSucThenHigherPrimThenSeededRandom()
        {
            // 2 and 3 tie on suç (1 each); seat 0 tips 3 one prim → 3 is richer.
            var prim = Crisis(4, new[] { 1, 2, 3, 1 }, new[] { 1, 0, 1, 1 });
            ToFinalVote(prim);
            prim.Ok(0, LineVerb.Vote, 2);
            prim.Ok(1, LineVerb.Vote, 3);
            prim.Next();
            prim.Ok(0, LineVerb.Vote, 2);
            prim.Ok(1, LineVerb.Vote, 3);
            prim.Ok(0, LineVerb.Transfer, 3, 1);
            prim.Next();
            var f = prim.View().Final.Firings.Single();
            Assert.AreEqual(3, f.Player);
            Assert.AreEqual(FiringDecision.HigherPrim, f.Decision);
            Assert.AreEqual(11, f.Desk);

            var picks = new HashSet<int>();
            for (int seed = 1; seed <= 12; seed++)
            {
                var r = Crisis(4, new[] { 1, 2, 3, 1 }, new[] { 1, 0, 1, 1 }, seed: seed);
                ToFinalVote(r);
                r.Ok(0, LineVerb.Vote, 2);
                r.Ok(1, LineVerb.Vote, 3);
                r.Next();
                r.Ok(0, LineVerb.Vote, 2);
                r.Ok(1, LineVerb.Vote, 3);
                r.Next();
                var rf = r.View().Final.Firings.Single();
                Assert.AreEqual(FiringDecision.Random, rf.Decision);
                picks.Add(rf.Player);
                var again = Crisis(4, new[] { 1, 2, 3, 1 }, new[] { 1, 0, 1, 1 }, seed: seed);
                ToFinalVote(again);
                again.Ok(0, LineVerb.Vote, 2); again.Ok(1, LineVerb.Vote, 3); again.Next();
                again.Ok(0, LineVerb.Vote, 2); again.Ok(1, LineVerb.Vote, 3); again.Next();
                Assert.AreEqual(rf.Player, again.View().Final.Firings.Single().Player, "seeded");
            }
            CollectionAssert.AreEquivalent(new[] { 2, 3 }, picks);
        }

        [TestCase(5), TestCase(6)]
        public void FiveAndSixPlayersFireTwiceAmongTheRemainingCandidates(int n)
        {
            // meeting 2: everyone votes 0 except 0 (votes 1) → 0 has n-1, 1 has 1. Crisis: all vote 2.
            var meeting2 = Enumerable.Range(0, n).Select(id => id == 0 ? 1 : 0).ToArray();
            var crisis = Enumerable.Range(0, n).Select(id => id == 2 ? 0 : 2).ToArray();
            var m = Crisis(n, meeting2, crisis);
            var final = m.View().Final;
            Assert.AreEqual(2, final.FiringsTotal);
            Assert.AreEqual(3, final.Candidates.Count);
            CollectionAssert.AreEqual(new[] { 0, 2, 1 }, final.DefenseOrder);
            ToFinalVote(m);
            foreach (int id in m.Ids) if (id != 0) m.Ok(id, LineVerb.Vote, 0);
            m.Ok(0, LineVerb.Vote, 2);
            m.Next();
            Assert.AreEqual(0, m.View().Final.Firings.Single().Player);
            m.Next();
            Assert.AreEqual(LinePhase.FinalVote, m.Engine.Phase, "second firing");
            final = m.View().Final;
            Assert.AreEqual(1, final.Round);
            Assert.IsFalse(final.Revote);
            CollectionAssert.AreEqual(new[] { 2, 1 }, final.Options);
            m.Fail(LineFailure.Fired, 0, LineVerb.Vote, 2);
            Assert.IsFalse(m.View(0).Features.VoteOpen);
            m.Ok(3, LineVerb.Vote, 1);
            m.Next();
            var second = m.View().Final.Firings[1];
            Assert.AreEqual(1, second.Player);
            Assert.AreEqual(1, second.Round);
            CollectionAssert.AreEqual(new[] { 3 }, second.Recipients);
            m.Next();
            Assert.AreEqual(LinePhase.Results, m.Engine.Phase);
            CollectionAssert.AreEqual(new[] { 0, 1 }, m.Engine.Result.Fired);
            Assert.IsFalse(m.Engine.Result.Winners.Contains(0));
            CollectionAssert.AreEqual(new[] { 3 }, m.Engine.Result.Winners, "seat 3 collected both desks");
        }

        [TestCase(3), TestCase(4)]
        public void ThreeAndFourPlayersFireOnce(int n)
        {
            var m = new LineTest(n);
            m.Finish();
            Assert.AreEqual(1, m.Engine.Result.Fired.Count);
        }

        [Test]
        public void ResultsDiscloseEverythingAndHashIsStable()
        {
            var m = Crisis(4, new[] { 1, 2, 3, 1 }, new[] { 1, 0, 1, 1 });
            m.Finish();
            var r = m.Engine.Result;
            Assert.AreEqual(4, r.Players.Count);
            Assert.AreEqual(12, r.Choices.Count, "three tasks × four players");
            Assert.IsTrue(r.Ballots.Any(b => b.Kind == BallotKind.Crisis));
            Assert.IsTrue(r.Ballots.Any(b => b.Kind == BallotKind.Meeting));
            Assert.IsTrue(r.PrimLog.Any(p => p.Reason == PrimReason.Task));
            Assert.IsTrue(r.BlameLog.Any(b => b.Reason == BlameReason.Crisis));
            Assert.AreNotEqual(0UL, r.ResultHash);
            var again = Crisis(4, new[] { 1, 2, 3, 1 }, new[] { 1, 0, 1, 1 });
            again.Finish();
            Assert.AreEqual(r.ResultHash, again.Engine.Result.ResultHash);
            foreach (var p in r.Players) Assert.AreEqual(p.Blame, r.BlameLog.Where(b => b.Player == p.Player).Sum(b => b.Delta));
            foreach (var p in r.Players) Assert.AreEqual(p.Prim, 6 + r.PrimLog.Where(x => x.Player == p.Player).Sum(x => x.Delta));
        }
    }
}
