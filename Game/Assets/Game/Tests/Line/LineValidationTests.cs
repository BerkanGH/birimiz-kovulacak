using System;
using System.Reflection;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class LineValidationTests
    {
        private static LineSetup Setup()
        {
            var setup = new LineSetup { Seed = 407, ManagerProfileId = LineManagerIds.Paragoz };
            for (int i = 0; i < 3; i++) setup.Players.Add(new LinePlayerInfo { Id = i });
            return setup;
        }

        [TestCase(-100.0), TestCase(double.NaN), TestCase(double.PositiveInfinity), TestCase(double.NegativeInfinity)]
        public void InvalidStartTimeIsRejectedBeforeTimersCanRun(double start)
        {
            var setup = Setup(); setup.StartTime = start;
            Assert.Throws<ArgumentException>(() => LineEngineFactory.Create(setup, LineConfig.CreateDefault()));
        }

        [TestCase("CoffeeSeconds"), TestCase("PhoneDeadSeconds"), TestCase("InternetSeconds"), TestCase("WiretapSeconds"),
         TestCase("RecordingMinCallSeconds"), TestCase("RecordingSnippetSeconds")]
        public void InvalidEffectDurationsAreRejected(string field)
        {
            foreach (float value in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                var config = LineConfig.CreateDefault(); typeof(LineConfig).GetField(field).SetValue(config, value);
                Assert.Throws<ArgumentException>(() => LineEngineFactory.Create(Setup(), config), field + " = " + value);
            }
        }

        [TestCase("SessionPoints"), TestCase("PastManagers"), TestCase("SeenRounds"), TestCase("UsedTopics"), TestCase("TimesFired")]
        public void NullHistoryCollectionsAreRejectedAtCreation(string field)
        {
            var setup = Setup(); setup.History = new LineSessionHistory();
            typeof(LineSessionHistory).GetField(field).SetValue(setup.History, null);
            Assert.Throws<ArgumentException>(() => LineEngineFactory.Create(setup, LineConfig.CreateDefault()));
        }

        [Test]
        public void NullAndInvalidProfilesAreRejectedAtCreation()
        {
            var config = LineConfig.CreateDefault(); config.Profiles.Add(null);
            Assert.Throws<ArgumentException>(() => LineEngineFactory.Create(Setup(), config));
            config = LineConfig.CreateDefault(); config.Profiles[0].TraceChance = float.NaN;
            Assert.Throws<ArgumentException>(() => LineEngineFactory.Create(Setup(), config));
            config = LineConfig.CreateDefault(); config.Profiles[0].LeakChance = 1.1f;
            Assert.Throws<ArgumentException>(() => LineEngineFactory.Create(Setup(), config));
            config = LineConfig.CreateDefault(); config.Rounds.InternShareChance = float.NaN;
            Assert.Throws<ArgumentException>(() => LineEngineFactory.Create(Setup(), config));
        }

        [Test]
        public void AllZeroPhaseDurationsStillCompleteWithoutLooping()
        {
            var config = LineConfig.CreateDefault();
            foreach (FieldInfo field in typeof(LineDurations).GetFields(BindingFlags.Public | BindingFlags.Instance)) field.SetValue(config.Durations, 0f);
            ILineEngine engine = LineEngineFactory.Create(Setup(), config);
            engine.Tick(0);
            Assert.IsTrue(engine.IsFinished); Assert.NotNull(engine.Result);
        }

        [Test]
        public void FractionalDefenseAtLargeClockAdvancesAtEachAbsoluteBoundary()
        {
            var setup = Setup(); setup.StartTime = 1000000000000;
            var config = LineConfig.CreateDefault(); config.Durations.DefensePerPlayer = 0.2f;
            ILineEngine engine = LineEngineFactory.Create(setup, config);
            double now = setup.StartTime;
            while (engine.Phase != LinePhase.FinalDefense) { now = engine.PhaseEndTime; engine.Tick(now); }
            var first = engine.BuildView(0, now).Meeting;
            engine.Tick(first.DefenderEndTime);
            var second = engine.BuildView(0, first.DefenderEndTime).Meeting;
            Assert.AreEqual(first.DefenseOrder[1], second.Defender);
            engine.Tick(second.DefenderEndTime);
            Assert.AreEqual(first.DefenseOrder[2], engine.BuildView(0, second.DefenderEndTime).Meeting.Defender);
        }
    }
}
