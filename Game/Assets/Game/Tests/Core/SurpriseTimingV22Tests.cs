using System;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class SurpriseTimingV22Tests
    {
        private static TestMatch Match(int seed, float min, float max, float legacy = 0.45f)
        {
            return new TestMatch(seed, configure: c =>
            {
                c.ManagerProfiles.Clear();
                c.ManagerProfiles.Add(new ManagerProfileDef { Id = "surprise", SurpriseInspection = true });
                c.Manager.SurpriseMinFraction = min; c.Manager.SurpriseMaxFraction = max;
                c.Manager.SurpriseAtFraction = legacy;
                c.Manager.SurpriseSeconds = 1;
            });
        }

        private static double StartFraction(TestMatch m)
        {
            m.Phase(MatchPhase.Work2);
            double start = m.View().PhaseStartTime;
            m.Phase(MatchPhase.FinalInspection);
            GameEvent inspection = m.Events.SingleOrDefault(e => e.Type == EventType.SurpriseInspection);
            return inspection == null ? 1 : (inspection.Time - start) / m.Config.Durations.Work2;
        }

        [Test]
        public void DefaultRangeIsSeededVariesAcrossSeedsAndIgnoresLegacyFraction()
        {
            var fractions = Enumerable.Range(0, 12).Select(seed => StartFraction(Match(seed, 0.2f, 0.7f))).ToArray();
            Assert.IsTrue(fractions.All(f => f >= 0.2 && f <= 0.7));
            Assert.Greater(fractions.Distinct().Count(), 1);
            for (int seed = 0; seed < fractions.Length; seed++)
                Assert.AreEqual(fractions[seed], StartFraction(Match(seed, 0.2f, 0.7f, 0.99f)));
        }

        [TestCase(0.3f, 0.3f, 0.9f, 0.3)]
        [TestCase(0.8f, 0.2f, 0.9f, 0.8)]
        [TestCase(-1f, -0.5f, 0.9f, 0)]
        [TestCase(2f, 3f, 0.9f, 1)]
        [TestCase(0f, 0f, 0.4f, 0.4)]
        [TestCase(0f, 0f, -1f, 0)]
        [TestCase(0f, 0f, 2f, 1)]
        public void InvalidBoundsClampAndOnlyBothZeroUseLegacy(float min, float max, float legacy, double expected)
        {
            TestMatch m = Match(91, min, max, legacy);
            Assert.AreEqual(expected, StartFraction(m), 0.000001);
            Assert.AreEqual(expected == 1 ? 0 : 1, m.Events.Count(e => e.Type == EventType.SurpriseInspection),
                "A draw at Work2's end yields to the final-inspection phase boundary.");
        }

        [Test]
        public void OutOfRangeBoundsClampToWholeWorkPhase()
        {
            double[] fractions = Enumerable.Range(0, 8).Select(seed => StartFraction(Match(seed, -2, 3))).ToArray();
            Assert.IsTrue(fractions.All(f => f >= 0 && f < 1));
            Assert.Greater(fractions.Distinct().Count(), 1);
        }
    }
}
