using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class HighlightRetentionTests
    {
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void HighlightClampsToRetainedLogAfterCircularBufferEviction(bool overlapsRetainedRange, bool zeroLengthTail)
        {
            const double firstRetained = 477;
            var m = new TestMatch(configure: c =>
            {
                c.Footage.SampleInterval = 0.125f;
                c.Durations.Work1 = zeroLengthTail ? 1500 : 1497;
                c.Durations.Raid = c.Durations.Work2 = c.Durations.FinalInspection = zeroLengthTail ? 0 : 1;
                c.Tasks.MailEnabled = false;
                c.Tasks.PhoneIntervalMin = c.Tasks.PhoneIntervalMax = 2000;
            });
            m.Work();
            if (overlapsRetainedRange) m.AdvanceTo(480);
            m.Do(0, ActionVerb.LockWorkstation, EntityIds.DeskComputer(1));
            double completedAt = m.Engine.BuildHostDebugView().Incidents.Single().Time;
            m.Finish();

            // [0, 1501) at 8 Hz for four players: the latest 32768 entries start at 477.
            HighlightView highlight = m.View().Result.Highlight;
            Assert.IsNotNull(highlight);
            Assert.AreEqual(firstRetained, highlight.Start);
            Assert.AreEqual(overlapsRetainedRange ? completedAt + 4 : firstRetained, highlight.End);
            Assert.AreEqual((overlapsRetainedRange ? 89 : 1) * 4, highlight.Samples.Count);
            Assert.AreEqual(firstRetained, highlight.Samples.First().Time);
            foreach (var frame in highlight.Samples.GroupBy(s => s.Time))
                CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, frame.Select(s => s.Player));
            CollectionAssert.AreEqual(highlight.Samples.OrderBy(s => s.Time).ThenBy(s => s.Player), highlight.Samples);
            Assert.IsTrue(highlight.Samples.All(s => s.Time >= highlight.Start && s.Time <= highlight.End));
        }
    }
}
