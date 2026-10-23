using System;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    /// <summary>Informational measurements, with state assertions but no machine-dependent time limits.</summary>
    [TestFixture]
    public sealed class CorePerformanceTests
    {
        [Test]
        public void MeasureOfficeTickAndFourRecipientSnapshots()
        {
            var m = new TestMatch(); m.Work();
            m.Plant(2, 0);
            m.Coffee(1, 0);
            m.Start(0, ActionVerb.WriteReport, EntityIds.DeskComputer(0));
            Measure(m, "Work1", 5000);
            Assert.AreEqual(MatchPhase.Work1, m.Engine.Phase);
            Assert.GreaterOrEqual(m.View().Quota.Points, 1);
        }

        [Test]
        public void MeasurePopulatedResultsTickAndFourRecipientSnapshots()
        {
            var m = new TestMatch(); m.Work();
            m.Plant(0, 1, ItemVariants.Duck);
            m.Plant(1, 2, ItemVariants.Stapler);
            m.Plant(2, 3, ItemVariants.RivalCv);
            m.Plant(3, 0, ItemVariants.BossMug);
            m.SwapInto(0, 0, 2); m.Do(0, ActionVerb.Drop);
            m.Do(1, ActionVerb.LockWorkstation, EntityIds.DeskComputer(3));
            m.Do(2, ActionVerb.LockWorkstation, EntityIds.DeskComputer(0));
            m.Phase(MatchPhase.Raid); m.Advance(10);
            EvidenceView first = m.View().Evidence.First(e => e.Kind == EvidenceKind.SuspiciousItemFound);
            EvidenceView second = m.View().Evidence.Last(e => e.Kind == EvidenceKind.SuspiciousItemFound);
            m.Do(3, ActionVerb.AuditScanEvidence, param: first.Id);
            m.Do(3, ActionVerb.AuditScanEvidence, param: second.Id);
            m.Do(3, ActionVerb.SealEvidence, param: first.Id);
            m.Phase(MatchPhase.MeetingDiscussion);
            m.Do(0, ActionVerb.Accuse, param: 2);
            m.Do(1, ActionVerb.Accuse, param: 2);
            m.Do(2, ActionVerb.Accuse, param: 0);
            m.Do(3, ActionVerb.Accuse, param: 0);
            m.Do(3, ActionVerb.PresentNote, param: m.View(3).Me.Informant.Scans[0].NoteId);
            m.Finish();
            string expectedHash = m.View().Result.ResultHash;
            Assert.GreaterOrEqual(m.View().Result.Incidents.Count, 7);
            Assert.GreaterOrEqual(m.View().Evidence.Count, 7);
            Measure(m, "Results", 5000);
            Assert.AreEqual(expectedHash, m.View().Result.ResultHash);
            Assert.AreEqual(4, m.View().Result.Players.Count);
        }

        private static void Measure(TestMatch m, string label, int iterations)
        {
            double now = m.Now;
            for (int i = 0; i < 200; i++)
            {
                m.Engine.Tick(now);
                for (int p = 0; p < 4; p++) m.Engine.BuildView(p, now);
            }
            var timer = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++) m.Engine.Tick(now += 1.0 / 60.0);
            timer.Stop();
            double tickMicroseconds = timer.Elapsed.TotalMilliseconds * 1000.0 / iterations;
            int views = 0;
            timer.Restart();
            for (int i = 0; i < iterations; i++)
                for (int p = 0; p < 4; p++)
                {
                    PlayerView snapshot = m.Engine.BuildView(p, now);
                    views += snapshot.Players.Count;
                }
            timer.Stop();
            double fourViewMicroseconds = timer.Elapsed.TotalMilliseconds * 1000.0 / iterations;
            Assert.AreEqual(iterations * 4 * 4, views);
            TestContext.WriteLine(label + ": Tick " + tickMicroseconds.ToString("F2") + " us; four BuildView calls " + fourViewMicroseconds.ToString("F2") + " us; " + iterations + " iterations. Informational; no timing threshold asserted.");
        }
    }
}
