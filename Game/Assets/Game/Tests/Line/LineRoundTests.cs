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
            var r = m.Reveal(); Assert.IsTrue(m.View().Players.All(p => p.Blame == 6));
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
            Assert.AreEqual(0, pair.A == 0 ? pair.ChoiceA : pair.ChoiceB); Assert.AreEqual(7, m.Player(0).Blame);
        }

        [TestCase(0, 0, -1, -1)] [TestCase(0, 1, 2, -4)]
        [TestCase(1, 0, -4, 2)] [TestCase(1, 1, 10, 10)]
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
        public void ParagozTakerGetsFiveBlameRelief()
        {
            var m = LineTest.Primed(RoundType.PrimHavuzu, LineManagerIds.Paragoz);
            m.Ok(0, LineVerb.SubmitChoice, a: 1); m.Reveal(); Assert.AreEqual(5, m.Player(0).Blame);
        }

        [TestCase(3)] [TestCase(5)]
        public void OddPlayerCountsHaveOneTrioAndNoIntern(int count)
        {
            var m = new LineTest(LineTest.MakeSetup(count)); m.Until(LinePhase.RoundPlay);
            var views = Enumerable.Range(0, count).Select(p => m.View(p).Round).ToList();
            Assert.AreEqual(3, views.Count(r => r.Partners.Count == 2));
            foreach (int player in Enumerable.Range(0, count))
            {
                CollectionAssert.DoesNotContain(views[player].Partners, player);
                foreach (int partner in views[player].Partners) CollectionAssert.Contains(views[partner].Partners, player);
            }
            var r = m.Reveal(); Assert.AreEqual(1, r.Pairs.Count(p => p.C >= 0));
            var dealt = r.Pairs.SelectMany(p => p.C >= 0 ? new[] { p.A, p.B, p.C } : new[] { p.A, p.B });
            CollectionAssert.AreEquivalent(Enumerable.Range(0, count), dealt);
            Assert.IsTrue(r.Deltas.All(d => d.Delta == -1));
        }

        [TestCase(0, -1, -1)] [TestCase(1, -4, 2)] [TestCase(2, -2, 2)] [TestCase(3, 1, 1)]
        public void TrioOutcomeTable(int takers, int takerDelta, int sharerDelta)
        {
            var m = new LineTest(LineTest.MakeSetup(3)); m.Until(LinePhase.RoundPlay);
            for (int p = 0; p < 3; p++) m.Ok(p, LineVerb.SubmitChoice, a: p < takers ? 1 : 0);
            var reveal = m.Reveal(); Assert.AreEqual(1, reveal.Pairs.Count); Assert.GreaterOrEqual(reveal.Pairs[0].C, 0);
            for (int p = 0; p < 3; p++) Assert.AreEqual(5 + (p < takers ? takerDelta : sharerDelta), m.Player(p).Blame);
        }

        [TestCase(false, 3)] [TestCase(true, 1)]
        public void OddEmailCaughtUsesGuessAndRewardsCorrectVoters(bool correct, int penalty)
        {
            var m = LineTest.Primed(RoundType.FarkliEposta); var own = m.View().Round;
            Assert.IsTrue(own.IsOdd); Assert.AreEqual(-1, own.TopicVariant); Assert.AreEqual(4, own.GuessOptions.Distinct().Count());
            int majority = m.View(1).Round.TopicId;
            CollectionAssert.Contains(own.GuessOptions, majority); CollectionAssert.DoesNotContain(own.GuessOptions, own.TopicId);
            Assert.AreNotEqual(majority / 4, own.TopicId / 4);
            Assert.IsTrue(own.GuessOptions.All(t => t / 4 == majority / 4));
            int guess = correct ? majority : own.GuessOptions.First(i => i != majority);
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
        public void LostReportSlipsLeaveExactNoAlibiCountAndNeverNameCulpritOrHolder(int count)
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
                var missing = Enumerable.Range(0, count).Where(p => p != culprit && !named.Contains(p)).ToArray();
                Assert.AreEqual(count == 6 ? 2 : 1, missing.Length);
                CollectionAssert.AreEquivalent(missing, m.View(culprit).Round.NoAlibi);
                for (int p = 0; p < count; p++) if (p != culprit) Assert.IsEmpty(m.View(p).Round.NoAlibi);
                var honestNamed = Enumerable.Range(0, count).Where(p => p != culprit).Select(p => m.View(p).Round.ClueInnocent).Distinct();
                CollectionAssert.AreEquivalent(named, honestNamed);
                var r = m.Reveal(); Assert.AreEqual(culprit, r.Hidden); Assert.AreEqual(8, m.Player(culprit).Blame);
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
        public void SharedPasswordShowsRightNeighborAndOnlyRevealsCodesAtTheEnd()
        {
            var m = LineTest.Primed(RoundType.OrtakSifre); int code = 0;
            var owners = m.View().Round.PositionOwners;
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3 }, owners);
            for (int pos = 0; pos < 4; pos++)
            {
                var r = m.View(owners[pos]).Round; Assert.AreEqual(pos, r.DigitPosition); code = code * 10 + r.Digit;
                Assert.AreEqual((pos + 1) % 4, r.NeighborPosition);
                Assert.AreEqual(m.View(owners[(pos + 1) % 4]).Round.Digit, r.NeighborDigit);
            }
            Assert.GreaterOrEqual(code, 1000);
            m.Engine.DrainEvents();
            for (int p = 0; p < 4; p++) m.Ok(p, LineVerb.SubmitChoice, a: code);
            Assert.AreEqual(m.Now + m.Config.Durations.AllSubmittedRemaining, m.Engine.PhaseEndTime);
            Assert.IsFalse(m.View().Round.Solved); Assert.IsEmpty(m.View().Round.Attempts);
            Assert.IsFalse(m.Engine.DrainEvents().Any(e => e.Type == LineEventType.PasswordCracked || e.Type == LineEventType.AttemptMade));
            var reveal = m.Reveal(); Assert.AreEqual(code, reveal.Code); Assert.AreEqual(-1, reveal.SolvedBy); Assert.IsTrue(reveal.Solved);
            CollectionAssert.AreEqual(new[] { code, code, code, code, -1, -1 }, reveal.CodesByPlayer);
            Assert.AreEqual(12, m.Player(0).Blame);
            for (int p = 1; p < 4; p++) Assert.AreEqual(9, m.Player(p).Blame);
        }

        [TestCase(2, false)] [TestCase(3, true)]
        public void PasswordRequiresStrictMajorityAndLastEntryWins(int correctCount, bool solved)
        {
            var m = LineTest.Primed(RoundType.OrtakSifre); int code = 0;
            foreach (int owner in m.View().Round.PositionOwners) code = code * 10 + m.View(owner).Round.Digit;
            int wrong = code == 9999 ? 1000 : code + 1;
            for (int p = 0; p < 4; p++) m.Ok(p, LineVerb.SubmitChoice, a: wrong);
            for (int p = 0; p < correctCount; p++) m.Ok(p, LineVerb.SubmitChoice, a: code);
            var r = m.Reveal(); Assert.AreEqual(solved, r.Solved);
            Assert.AreEqual(solved ? 12 : 8, m.Player(0).Blame);
            for (int p = 1; p < 4; p++) Assert.AreEqual(solved ? 9 : 11, m.Player(p).Blame);
        }

        [Test]
        public void PasswordRejectsOldAttemptVerbAndWrongDigitCount()
        {
            var m = LineTest.Primed(RoundType.OrtakSifre);
            foreach (int invalid in new[] { -1, 0, 999, 10000 })
                Assert.AreEqual(LineFailure.BadParam, m.Do(1, LineVerb.SubmitChoice, a: invalid).Failure);
            Assert.AreEqual(LineFailure.BadParam, m.Do(1, LineVerb.Attempt, a: 1234).Failure);
            m.Ok(1, LineVerb.SubmitChoice, a: 1234); m.Ok(1, LineVerb.SubmitChoice, a: 5678);
            Assert.AreEqual(5678, m.View(1).Round.MyA); Assert.AreEqual(-1, m.View(2).Round.MyA);
            var r = m.Reveal(); Assert.IsFalse(r.Solved); Assert.AreEqual(5678, r.CodesByPlayer[1]);
            Assert.AreEqual(LineFailure.WrongPhase, m.Do(1, LineVerb.Attempt, a: 1234).Failure);
        }

        [Test]
        public void PositiveBlameFavouriteReductionAndAppliedFloor()
        {
            var s = LineTest.MakeSetup(); s.ManagerProfileId = LineManagerIds.Kayirmaci;
            var m = new LineTest(s); m.Until(LinePhase.RoundPlay); int favourite = m.View().Favourite;
            for (int p = 0; p < 4; p++) m.Ok(p, LineVerb.SubmitChoice, a: 1);
            var r = m.Reveal(); Assert.AreEqual(5, m.Player(favourite).Blame);
            for (int p = 0; p < 4; p++) if (p != favourite) Assert.AreEqual(6, m.Player(p).Blame);
            Assert.AreEqual(0, r.Deltas.Where(d => d.Player == favourite).Sum(d => d.Delta));
            var floor = new LineTest(configure: c => c.StartBlame = 0); floor.Until(LinePhase.RoundPlay); var f = floor.Reveal();
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

        [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)]
        public void FullWeekHiddenRolesNeverRepeatAndHistoryCountsPlayedRounds(int count)
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var setup = LineTest.MakeSetup(count, seed); setup.Length = WeekLength.Full;
                setup.History = new LineSessionHistory { WeeksPlayed = 1 };
                var m = new LineTest(setup); var hidden = new HashSet<int>(); var uses = new int[7];
                while (!m.Engine.IsFinished)
                {
                    if (m.Engine.Phase == LinePhase.RoundReveal)
                    {
                        var r = m.View().Round.Reveal; uses[(int)r.Type]++;
                        if (r.Hidden >= 0) Assert.IsTrue(hidden.Add(r.Hidden), "Repeated hidden role, seed " + seed);
                    }
                    m.Next();
                }
                var history = m.Engine.NextHistory(); CollectionAssert.AreEqual(uses, history.RoundUses);
                for (int p = 0; p < count; p++) Assert.AreEqual(hidden.Contains(p) ? 1 : 0, history.HiddenRoles[p]);
            }
        }

        [Test]
        public void HiddenRolePrefersLowestSessionCountAmongUnusedPlayers()
        {
            var setup = LineTest.MakeSetup(); setup.History = new LineSessionHistory { WeeksPlayed = 2 };
            setup.History.HiddenRoles.AddRange(new[] { 7, 1, 0, 5 });
            setup.ForceRounds = new List<RoundType> { RoundType.FarkliEposta, RoundType.OrtakSifre, RoundType.KayipRapor };
            var m = new LineTest(setup);
            m.Until(LinePhase.RoundReveal, 0, 0); Assert.AreEqual(2, m.View().Round.Reveal.Hidden);
            m.Until(LinePhase.RoundReveal, 0, 1); Assert.AreEqual(1, m.View().Round.Reveal.Hidden);
            m.Until(LinePhase.RoundReveal, 1, 0); Assert.AreEqual(3, m.View().Round.Reveal.Hidden);
        }

        [TestCase(3)] [TestCase(5)]
        public void TrioParagozReliefAppliesToTakersWithASharer(int count)
        {
            var setup = LineTest.MakeSetup(count); setup.ManagerProfileId = LineManagerIds.Paragoz;
            var m = new LineTest(setup, c => c.StartBlame = 10); m.Until(LinePhase.RoundPlay);
            int member = Enumerable.Range(0, count).First(p => m.View(p).Round.Partners.Count == 2);
            m.Ok(member, LineVerb.SubmitChoice, a: 1);
            var r = m.Reveal(); Assert.AreEqual(-5, r.Deltas.Single(d => d.Player == member).Delta);
        }
    }
}
