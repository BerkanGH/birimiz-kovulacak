using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    public class ContractSmokeTests
    {
        [Test]
        public void DefaultConfigTimelineIncludesV2Discussion()
        {
            var config = MatchConfig.CreateDefault();
            Assert.AreEqual(495f, config.Durations.Total(), 0.001f);
            Assert.AreEqual(12, config.Objectives.Count);
            Assert.AreEqual(12, config.Sabotage.Count);
        }

        [Test]
        public void DefaultOfficeHasSixDesksAndUniqueIds()
        {
            var layout = DefaultOffice.Create();
            Assert.AreEqual(Protocol.MaxPlayers, layout.Desks.Count);
            var ids = new System.Collections.Generic.HashSet<string>();
            foreach (var s in layout.Slots) Assert.IsTrue(ids.Add(s.Id), "duplicate " + s.Id);
            foreach (var s in layout.Stations) Assert.IsTrue(ids.Add(s.Id), "duplicate " + s.Id);
            foreach (var s in layout.Items) Assert.IsTrue(ids.Add(s.Id), "duplicate " + s.Id);
            Assert.AreEqual(2, EntityIds.DeskIndexOf("desk.2.drawer"));
            Assert.AreEqual(-1, EntityIds.DeskIndexOf("st.coffee"));
        }
    }
}
