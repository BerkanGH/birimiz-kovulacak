using System.Collections.Generic;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class LineSnapshotTests
    {
        private static LineSetup Setup()
        {
            var setup = new LineSetup { WeekId = 71, Seed = 81, ManagerProfileId = LineManagerIds.Paragoz };
            for (int i = 0; i < 4; i++) setup.Players.Add(new LinePlayerInfo { Id = i, Name = "Seat " + i });
            return setup;
        }

        [Test]
        public void CallerOwnedSetupAndConfigCannotMutateAuthoritativeState()
        {
            var setup = Setup(); var config = LineConfig.CreateDefault();
            ILineEngine engine = LineEngineFactory.Create(setup, config);
            byte[] before = LineCodec.EncodeView(engine.BuildView(0, 0));
            setup.Players[0].Name = "changed";
            setup.Players.Clear(); setup.WeekId = 99; setup.ManagerProfileId = "changed";
            config.Durations.MorningFirst = 100000;
            config.Profiles[0].Id = "changed"; config.Profiles.Clear();
            CollectionAssert.AreEqual(before, LineCodec.EncodeView(engine.BuildView(0, 0)));
        }

        [Test]
        public void ReturnedSetupConfigAndViewsAreIndependentSnapshots()
        {
            ILineEngine engine = LineEngineFactory.Create(Setup(), LineConfig.CreateDefault());
            byte[] before = LineCodec.EncodeView(engine.BuildView(0, 0));
            engine.Setup.Players[0].Name = "changed";
            engine.Config.Durations.MorningFirst = 1;
            var view = engine.BuildView(0, 0);
            view.Mine.AgendaTarget = 0; view.Mine.Trick = TrickType.None;
            view.Mine.Inbox[0].TemplateId = 999; view.Players[0].Blame = 999;
            view.Players.Clear(); view.TodayRounds.Clear(); view.Mine.Inbox.Clear();
            CollectionAssert.AreEqual(before, LineCodec.EncodeView(engine.BuildView(0, 0)));
        }

        [Test]
        public void RevealsResultsAndNextHistoryRemainImmutableThroughReturnedObjects()
        {
            ILineEngine engine = LineEngineFactory.Create(Setup(), LineConfig.CreateDefault());
            double now = 0;
            while (engine.Phase != LinePhase.RoundReveal) { now = engine.PhaseEndTime; engine.Tick(now); }
            byte[] before = LineCodec.EncodeView(engine.BuildView(0, now));
            var view = engine.BuildView(0, now);
            view.TodayReveals[0].Pairs[0].ChoiceA = 99;
            view.Round.Reveal.Deltas.Clear(); view.TodayReveals.Clear();
            CollectionAssert.AreEqual(before, LineCodec.EncodeView(engine.BuildView(0, now)));
            while (!engine.IsFinished) { now = engine.PhaseEndTime; engine.Tick(now); }
            var result = engine.Result;
            ulong hash = result.ResultHash;
            result.Players[0].Points = 999; result.Players.Clear(); result.ResultHash = 0;
            Assert.AreEqual(hash, engine.Result.ResultHash);
            Assert.AreEqual(4, engine.Result.Players.Count);
            LineSessionHistory history = engine.NextHistory();
            int points = history.SessionPoints[0];
            history.SessionPoints[0] = 999; history.PastManagers.Clear();
            Assert.AreEqual(points, engine.NextHistory().SessionPoints[0]);
            Assert.AreEqual(1, engine.NextHistory().PastManagers.Count);
        }
    }
}
