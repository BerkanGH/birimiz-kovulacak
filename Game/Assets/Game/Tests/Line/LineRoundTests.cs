using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests.Line
{
    [TestFixture]
    public sealed class LineRoundTests
    {
        [Test]
        public void DefaultBothTakeAddsExactlyOneBlameToEachPartner()
        {
            var m = new LineTest(); m.Until(LinePhase.RoundPlay);
            for (int p = 0; p < 4; p++) m.Ok(p, LineVerb.SubmitChoice, a: 1);
            var r = m.Reveal(); Assert.IsTrue(m.View().Players.All(p => p.Blame == 1));
            Assert.IsTrue(r.Deltas.All(d => d.Delta == 1));
        }

        [Test]
        public void DisconnectedChoiceUsesDefaultAndReconnectDoesNotRestoreIt()
        {
            var m = new LineTest(); m.Until(LinePhase.RoundPlay); int partner = m.View().Round.Partner;
            m.Ok(0, LineVerb.SubmitChoice, a: 1); m.Ok(partner, LineVerb.SubmitChoice, a: 1);
            m.Engine.SetConnected(0, false, m.Now); Assert.IsFalse(m.Player(0).Submitted);
            Assert.AreEqual(LineFailure.Absent, m.Do(0, LineVerb.SubmitChoice, a: 1).Failure);
            m.Engine.SetConnected(0, true, m.Now); Assert.IsFalse(m.Player(0).Submitted);
            var pair = m.Reveal().Pairs.Single(p => p.A == 0 || p.B == 0);
            Assert.AreEqual(0, pair.A == 0 ? pair.ChoiceA : pair.ChoiceB); Assert.AreEqual(2, m.Player(0).Blame);
        }

        [TestCase(0, 0, -1, -1)] [TestCase(0, 1, 2, -2)]
        [TestCase(1, 0, -2, 2)] [TestCase(1, 1, 10, 10)]
        public void BonusPoolOutcomeTable(int a, int b, int deltaA, int deltaB)
        {
            var m = LineTest.Primed(RoundType.PrimHavuzu); int partner = m.View().Round.Partner;
            m.Ok(0, LineVerb.SubmitChoice, a: a); m.Ok(partner, LineVerb.SubmitChoice, a: b);
            var r = m.Reveal();
            Assert.AreEqual(10 + deltaA, m.Player(0).Blame); Assert.AreEqual(10 + deltaB, m.Player(partner).Blame);
            Assert.AreEqual(deltaA, r.Deltas.Where(d => d.Player == 0).Sum(d => d.Delta));
            var pair = r.Pairs.Single(p => p.A == 0 || p.B == 0);
            Assert.AreEqual(a, pair.A == 0 ? pair.ChoiceA : pair.ChoiceB);
        }

        [Test]
        public void ParagozTakerGetsThreeBlameRelief()
        {
            var m = LineTest.Primed(RoundType.PrimHavuzu, LineManagerIds.Paragoz);
            m.Ok(0, LineVerb.SubmitChoice, a: 1); m.Reveal(); Assert.AreEqual(7, m.Player(0).Blame);
        }

        [TestCase(3, 0f, 2)] [TestCase(3, 1f, 0)] [TestCase(5, 0f, 2)] [TestCase(5, 1f, 0)]
        public void OddPlayerPairsWithConfiguredIntern(int count, float shareChance, int expected)
        {
            var m = new LineTest(LineTest.MakeSetup(count), c => c.Rounds.InternShareChance = shareChance);
            m.Until(LinePhase.RoundPlay);
            int internPartner = Enumerable.Range(0, count).Single(p => m.View(p).Round.Partner == LineIds.Intern);
            var r = m.Reveal(); var pair = r.Pairs.Single(p => p.B == LineIds.Intern);
            Assert.AreEqual(internPartner, pair.A); Assert.AreEqual(shareChance == 1 ? 0 : 1, pair.ChoiceB);
            Assert.AreEqual(expected, m.Player(internPartner).Blame);
        }

        [TestCase(false, 3)] [TestCase(true, 1)]
        public void OddEmailCaughtUsesGuessAndRewardsCorrectVoters(bool correct, int penalty)
        {
            var m = LineTest.Primed(RoundType.FarkliEposta); var own = m.View().Round;
            Assert.IsTrue(own.IsOdd); Assert.AreEqual(1, own.TopicVariant); Assert.AreEqual(4, own.GuessOptions.Distinct().Count());
            CollectionAssert.Contains(own.GuessOptions, own.TopicId);
            int guess = correct ? own.TopicId : own.GuessOptions.First(i => i != own.TopicId);
            m.Ok(0, LineVerb.SubmitChoice, 1, guess);
            for (int p = 1; p < 4; p++)
            {
                Assert.IsFalse(m.View(p).Round.IsOdd); Assert.IsEmpty(m.View(p).Round.GuessOptions);
                m.Ok(p, LineVerb.SubmitChoice, 0);
            }
            var r = m.Reveal(); Assert.AreEqual(0, r.Hidden); Assert.AreEqual(correct, r.OddGuessCorrect);
            Assert.AreEqual(10 + penalty, m.Player(0).Blame);
            for (int p = 1; p < 4; p++) Assert.AreEqual(9, m.Player(p).Blame);
            Assert.AreEqual(4, r.Votes.Count);
        }

        [TestCase(RoundType.FarkliEposta)] [TestCase(RoundType.KayipRapor)]
        public void HiddenEscapesWrongPluralityAndInnocentTakesBlame(RoundType type)
        {
            var m = LineTest.Primed(type);
            m.Ok(0, LineVerb.SubmitChoice, 1); m.Ok(2, LineVerb.SubmitChoice, 1); m.Ok(3, LineVerb.SubmitChoice, 1);
            var r = m.Reveal(); Assert.AreEqual(1, r.Winner); Assert.AreEqual(8, m.Player(0).Blame); Assert.AreEqual(12, m.Player(1).Blame);
        }

        [TestCase(RoundType.FarkliEposta)] [TestCase(RoundType.KayipRapor)]
        public void HiddenRoundTiedVotesHaveNoPlurality(RoundType type)
        {
            var m = LineTest.Primed(type);
            m.Ok(0, LineVerb.SubmitChoice, 1); m.Ok(1, LineVerb.SubmitChoice, 0);
            m.Ok(2, LineVerb.SubmitChoice, 0); m.Ok(3, LineVerb.SubmitChoice, 1);
            Assert.AreEqual(-1, m.Reveal().Winner); Assert.AreEqual(8, m.Player(0).Blame); Assert.AreEqual(10, m.Player(1).Blame);
        }

        [TestCase(4)] [TestCase(5)] [TestCase(6)]
        public void LostReportClueCycleContainsEveryInnocentAndNeverCulprit(int count)
        {
            for (int seed = 0; seed < 30; seed++)
            {
                var s = LineTest.MakeSetup(count, seed);
                s.ForceRounds = new List<RoundType> { RoundType.KayipRapor, RoundType.PrimHavuzu, RoundType.IhbarKutusu, RoundType.TerfiListesi, RoundType.OrtakSifre, RoundType.FarkliEposta };
                var m = new LineTest(s); m.Until(LinePhase.RoundPlay);
                int culprit = Enumerable.Range(0, count).Single(p => m.View(p).Round.IsCulprit);
                var named = new HashSet<int>();
                for (int p = 0; p < count; p++)
                {
                    int clue = m.View(p).Round.ClueInnocent;
                    Assert.AreNotEqual(culprit, clue); Assert.AreNotEqual(p, clue); named.Add(clue);
                    if (p != culprit) m.Ok(p, LineVerb.SubmitChoice, culprit);
                }
                CollectionAssert.AreEquivalent(Enumerable.Range(0, count).Where(p => p != culprit), named);
                var r = m.Reveal(); Assert.AreEqual(culprit, r.Hidden); Assert.AreEqual(3, m.Player(culprit).Blame);
                for (int p = 0; p < count; p++) Assert.AreEqual(m.View(p).Round.ClueInnocent, r.Clues[p]);
            }
        }

        [Test]
        public void ComplaintTiedLeadersAllGainAndZeroCountsGetRelief()
        {
            var m = LineTest.Primed(RoundType.IhbarKutusu);
            m.Ok(0, LineVerb.SubmitChoice, 1, 3); m.Ok(1, LineVerb.SubmitChoice, 0, 5);
            var r = m.Reveal(); CollectionAssert.AreEqual(new[] { 1, 1, 0, 0 }, r.Counts.Take(4));
            Assert.IsEmpty(r.Votes); Assert.AreEqual(2, r.Complaints.Count);
            Assert.AreEqual(12, m.Player(0).Blame); Assert.AreEqual(12, m.Player(1).Blame);
            Assert.AreEqual(9, m.Player(2).Blame); Assert.AreEqual(9, m.Player(3).Blame);
        }

        [Test]
        public void ComplaintAllAbstainDoesNotGrantRelief()
        {
            var m = LineTest.Primed(RoundType.IhbarKutusu); var r = m.Reveal(); Assert.IsEmpty(r.Complaints);
            Assert.IsTrue(m.View().Players.All(p => p.Blame == 10));
        }

        [Test]
        public void PromotionUniqueWinnerAndBackersGetRelief()
        {
            var m = LineTest.Primed(RoundType.TerfiListesi);
            m.Ok(0, LineVerb.SubmitChoice, 1); m.Ok(2, LineVerb.SubmitChoice, 1); m.Ok(3, LineVerb.SubmitChoice, 0);
            var r = m.Reveal(); Assert.AreEqual(1, r.Winner); Assert.AreEqual(7, m.Player(1).Blame);
            Assert.AreEqual(9, m.Player(0).Blame); Assert.AreEqual(9, m.Player(2).Blame); Assert.AreEqual(10, m.Player(3).Blame);
            Assert.IsTrue(r.Votes.Any(v => v.Voter == 0 && v.Target == 1));
        }

        [Test]
        public void PromotionTieHasNoWinnerOrBackerRelief()
        {
            var m = LineTest.Primed(RoundType.TerfiListesi); m.Ok(0, LineVerb.SubmitChoice, 1); m.Ok(1, LineVerb.SubmitChoice, 0);
            Assert.AreEqual(-1, m.Reveal().Winner); Assert.IsTrue(m.View().Players.All(p => p.Blame == 10));
        }

        [Test]
        public void SharedPasswordPositionsDigitsAttemptFeedbackAndSolve()
        {
            var m = LineTest.Primed(RoundType.OrtakSifre); int code = 0;
            var owners = m.View().Round.PositionOwners;
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3 }, owners);
            for (int pos = 0; pos < 4; pos++)
            {
                var r = m.View(owners[pos]).Round; Assert.AreEqual(pos, r.DigitPosition); code = code * 10 + r.Digit;
            }
            int wrong = (code + 1) % 10000;
            m.Ok(1, LineVerb.Attempt, a: wrong);
            var attempt = m.View(3).Round.Attempts.Single(); Assert.AreEqual(wrong, attempt.Code); Assert.AreEqual(1, attempt.Player);
            Assert.Less(attempt.Correct, 4); Assert.IsTrue(m.Player(1).Submitted);
            m.Ok(0, LineVerb.Attempt, a: code); Assert.IsTrue(m.View().Round.Solved);
            Assert.AreEqual(m.Now + m.Config.Durations.AllSubmittedRemaining, m.Engine.PhaseEndTime);
            Assert.AreEqual(LineFailure.RoundSolved, m.Do(2, LineVerb.Attempt, a: code).Failure);
            var reveal = m.Reveal(); Assert.AreEqual(code, reveal.Code); Assert.AreEqual(0, reveal.SolvedBy); Assert.IsTrue(reveal.Solved);
            Assert.AreEqual(12, m.Player(0).Blame);
            for (int p = 1; p < 4; p++) Assert.AreEqual(9, m.Player(p).Blame);
        }

        [Test]
        public void PasswordTimeoutAndAttemptLimits()
        {
            var m = LineTest.Primed(RoundType.OrtakSifre);
            int own = m.View(1).Round.Digit;
            int wrong = 0; for (int pos = 0; pos < 4; pos++) wrong = wrong * 10 + ((own + 1) % 10);
            Assert.AreEqual(LineFailure.BadParam, m.Do(1, LineVerb.Attempt, a: -1).Failure);
            Assert.AreEqual(LineFailure.BadParam, m.Do(1, LineVerb.Attempt, a: 10000).Failure);
            m.Ok(1, LineVerb.Attempt, a: wrong); m.Ok(1, LineVerb.Attempt, a: wrong);
            Assert.AreEqual(LineFailure.NoAttemptsLeft, m.Do(1, LineVerb.Attempt, a: wrong).Failure);
            var r = m.Reveal(); Assert.IsFalse(r.Solved); Assert.AreEqual(8, m.Player(0).Blame);
            for (int p = 1; p < 4; p++) Assert.AreEqual(11, m.Player(p).Blame);
        }

        [Test]
        public void PositiveBlameFavouriteReductionAndAppliedFloor()
        {
            var s = LineTest.MakeSetup(); s.ManagerProfileId = LineManagerIds.Kayirmaci;
            var m = new LineTest(s); m.Until(LinePhase.RoundPlay); int favourite = m.View().Favourite;
            for (int p = 0; p < 4; p++) m.Ok(p, LineVerb.SubmitChoice, a: 1);
            var r = m.Reveal(); Assert.AreEqual(0, m.Player(favourite).Blame);
            for (int p = 0; p < 4; p++) if (p != favourite) Assert.AreEqual(1, m.Player(p).Blame);
            Assert.AreEqual(0, r.Deltas.Where(d => d.Player == favourite).Sum(d => d.Delta));
            var floor = new LineTest(); floor.Until(LinePhase.RoundPlay); var f = floor.Reveal();
            Assert.IsTrue(f.Deltas.All(d => d.Delta == 0)); Assert.IsTrue(floor.View().Players.All(p => p.Blame == 0));
        }

        [TestCase(RoundType.FarkliEposta)] [TestCase(RoundType.KayipRapor)]
        [TestCase(RoundType.IhbarKutusu)] [TestCase(RoundType.TerfiListesi)]
        public void VotesAndRecommendationsRejectSelf(RoundType type)
        {
            var m = LineTest.Primed(type); Assert.AreEqual(LineFailure.Self, m.Do(1, LineVerb.SubmitChoice, 1, 0).Failure);
        }

        [Test]
        public void OddEmailTopicsAvoidPriorSessionAndWeekTopics()
        {
            var s = LineTest.MakeSetup(); s.History = new LineSessionHistory { WeeksPlayed = 1 };
            s.History.UsedTopics.AddRange(new[] { 0, 1, 2, 3, 4 });
            s.ForceRounds = new List<RoundType> { RoundType.FarkliEposta, RoundType.PrimHavuzu, RoundType.FarkliEposta, RoundType.IhbarKutusu, RoundType.FarkliEposta, RoundType.TerfiListesi };
            var m = new LineTest(s); var used = new HashSet<int>(s.History.UsedTopics);
            for (int day = 0; day < 3; day++)
            {
                m.Until(LinePhase.RoundPlay, day, 0); Assert.IsTrue(used.Add(m.View().Round.TopicId));
            }
        }
    }
}
