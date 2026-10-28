using System;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class PerformanceV2Tests
    {
        [Test]
        public void SixPlayerOfficeTickAndSnapshotBatchStayWithinBudgets()
        {
            var setup = SixPlayers();
            var m = new TestMatch(setup: setup); m.Work();
            m.Do(0, ActionVerb.CallPlayer, param: 1); m.Do(1, ActionVerb.AnswerCall);
            m.Plant(2, 0); m.Coffee(3, 0); m.Start(4, ActionVerb.WriteReport, EntityIds.DeskComputer(4));
            Measure(m, "Six-player Work1", 4000);
            Assert.AreEqual(MatchPhase.Work1, m.Engine.Phase);
        }

        [Test]
        public void SixPlayerInspectionHotLoopDoesNotAllocateAndStaysWithinBudgets()
        {
            var m = new TestMatch(setup: SixPlayers(), configure: c => { c.Durations.Work1 = 10; c.Durations.Raid = 300; },
                arrange: l => { l.ManagerRoute.RemoveRange(1, l.ManagerRoute.Count - 1); l.ManagerRoute[0].DwellSeconds = 290; });
            m.Work();
            string[] suspicious = m.View().Items.Where(i => i.Kind == ItemKind.Suspicious).Select(i => i.Id).ToArray();
            for (int p = 0; p < suspicious.Length; p++) m.Do(p, ActionVerb.PickUp, suspicious[p]);
            m.Phase(MatchPhase.Raid); m.Advance(3);
            Vec3 point = m.View().Manager.Position + Vec3.FromYaw(m.View().Manager.Yaw);
            foreach (int p in m.Engine.Players) m.Move(p, point);
            m.Advance(0.2);
            Measure(m, "Six-player inspection", 4000);
            Assert.AreEqual(MatchPhase.Raid, m.Engine.Phase);
        }

        [Test]
        public void SixPlayerPopulatedResultsStayWithinSnapshotBudget()
        {
            TestMatch m = HardeningV2Tests.RunSixPlayerScript();
            Assert.AreEqual(6, m.View().Result.Players.Count);
            Assert.GreaterOrEqual(m.View().Result.Incidents.Count, 12);
            string hash = m.View().Result.ResultHash;
            Measure(m, "Six-player populated Results", 4000);
            Assert.AreEqual(hash, m.View().Result.ResultHash);
        }

        private static MatchSetup SixPlayers()
        {
            var setup = TestMatch.Setup();
            for (int p = setup.Players.Count; p < Protocol.MaxPlayers; p++) setup.Players.Add(new PlayerSlotInfo { Id = p, Name = "P" + p });
            return setup;
        }

        private static void Measure(TestMatch m, string label, int iterations)
        {
            double now = m.Now;
            for (int i = 0; i < 300; i++)
            {
                m.Engine.Tick(now);
                foreach (int p in m.Engine.Players) m.Engine.BuildView(p, now);
            }
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++) m.Engine.Tick(now += 1.0 / 60.0);
            watch.Stop(); double tickMs = watch.Elapsed.TotalMilliseconds / iterations;
            long viewedPlayers = 0;
            watch.Restart();
            for (int i = 0; i < iterations; i++)
                foreach (int p in m.Engine.Players) viewedPlayers += m.Engine.BuildView(p, now).Players.Count;
            watch.Stop(); double batchMs = watch.Elapsed.TotalMilliseconds / iterations;
            Assert.AreEqual(iterations * 36, viewedPlayers);
            Assert.Less(tickMs, 0.2, label + " Tick budget"); Assert.Less(batchMs, 0.3, label + " six-view batch budget");
            // Unity's older runtime need not expose allocation counters; the .NET harness does.
            var allocationCounter = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes);
            string allocation = "unavailable";
            if (allocationCounter != null)
            {
                var count = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), allocationCounter);
                for (int i = 0; i < 100; i++) m.Engine.Tick(now);
                long before = count();
                for (int i = 0; i < iterations; i++) m.Engine.Tick(now);
                long allocated = count() - before;
                allocation = (allocated / (double)iterations).ToString("F2") + " B/Tick";
                Assert.AreEqual(0, allocated, "Stable hot loop should allocate nothing; event/sample creation is measured separately by advancing Tick.");
            }
            TestContext.WriteLine(label + ": Tick=" + (tickMs * 1000).ToString("F2") + " us; six BuildView=" + (batchMs * 1000).ToString("F2") + " us; stable " + allocation + "; " + iterations + " iterations.");
        }
    }
}
