using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    /// <summary>Regression tests for findings from the independent engine review during integration.</summary>
    [TestFixture]
    public sealed class IntegrationReviewTests
    {
        static void RaidThroughAllDesks(TestMatch m)
        {
            m.Phase(MatchPhase.Raid);
            m.Advance(m.Config.Durations.Raid - 0.5);
        }

        [Test]
        public void OwnReportInAnotherDrawerIsAPlantNotFreeFraming()
        {
            var m = new TestMatch(); m.Work();
            string report = m.Print(0);
            m.Do(0, ActionVerb.PickUp, report);
            m.Do(0, ActionVerb.Place, EntityIds.DeskDrawer(1));
            var debug = m.Engine.BuildHostDebugView();
            var plant = debug.Incidents.Single(i => i.Type == SabotageType.PlantItem);
            Assert.AreEqual(0, plant.Actor);
            Assert.AreEqual(1, plant.Victim);
            RaidThroughAllDesks(m);
            var found = m.View(2).Evidence.Single(e => e.ItemId == report);
            Assert.AreEqual(EvidenceKind.HiddenReportFound, found.Kind);
            Assert.AreEqual(1, found.AttributedTo, "the manager still blames the drawer's apparent owner");
            debug = m.Engine.BuildHostDebugView();
            Assert.Greater(debug.TrueMisconduct[0], 0f, "the framer carries true misconduct");
            Assert.AreEqual(0f, debug.TrueMisconduct[1]);
            Assert.IsTrue(debug.Incidents.Single(i => i.Type == SabotageType.PlantItem).Discovered);
        }

        [Test]
        public void OwnReportInOwnDrawerIsNotAnIncident()
        {
            var m = new TestMatch(); m.Work();
            string report = m.Print(0);
            m.Do(0, ActionVerb.PickUp, report);
            m.Do(0, ActionVerb.Place, EntityIds.DeskDrawer(0));
            Assert.IsEmpty(m.Engine.BuildHostDebugView().Incidents);
        }

        [Test]
        public void VoluntaryDropsDoNotGrowThePublicLog()
        {
            var m = new TestMatch(); m.Work();
            int before = m.View(1).Log.Count;
            for (int i = 0; i < 40; i++)
            {
                m.Do(0, ActionVerb.PickUp, EntityIds.Spray(0));
                m.Do(0, ActionVerb.Drop);
            }
            Assert.LessOrEqual(m.View(1).Log.Count - before, 2);
            m.Do(0, ActionVerb.PickUp, EntityIds.Spray(0));
            m.Phase(MatchPhase.MeetingDiscussion);
            Assert.IsTrue(m.View(1).Log.Any(l => l.Kind == EventType.ItemDropped && l.Actor == 0), "forced drops at the meeting are still public");
        }

        [Test]
        public void ClosedContainerIndicesDoNotRevealUnknownContents()
        {
            var m = new TestMatch(); m.Work();
            m.Plant(2, 0);
            m.Do(1, ActionVerb.PickUp, EntityIds.Mug(0));
            m.Do(1, ActionVerb.Place, EntityIds.DeskDrawer(0));
            var mine = m.View(1).Items.Single(i => i.Id == EntityIds.Mug(0));
            Assert.AreEqual(0, mine.SlotIndex, "P1 does not know about the duck, so its mug must not appear at index 1");
            Assert.IsFalse(m.View(1).Items.Any(i => i.Id == EntityIds.Suspicious(ItemVariants.Duck)));
        }

        [Test]
        public void RecoveringAHiddenReportMarksItDiscoveredAndBlamesTheDrawer()
        {
            var m = new TestMatch(); m.Work();
            string report = m.Print(0);
            m.Do(1, ActionVerb.PickUp, report);
            m.Do(1, ActionVerb.Place, EntityIds.DeskDrawer(2));
            m.Do(3, ActionVerb.SearchContainer, EntityIds.DeskDrawer(2));
            m.Do(3, ActionVerb.PickUp, report);
            var hide = m.Engine.BuildHostDebugView().Incidents.Single(i => i.Type == SabotageType.HideReport);
            Assert.IsTrue(hide.Discovered);
            Assert.AreEqual(2, hide.BlamedPlayer);
            Assert.AreEqual(1, hide.Actor, "recovery never rewrites the true actor");
            Assert.AreEqual(3, hide.ReversedBy);
        }

        [Test]
        public void EvidencePutBackWhereItWasFoundIsNotCountedTwice()
        {
            var m = new TestMatch(); m.Work();
            string duck = EntityIds.Suspicious(ItemVariants.Duck);
            m.Plant(2, 0, drawer: false);
            RaidThroughAllDesks(m);
            Assert.AreEqual(1, m.View().Evidence.Count(e => e.ItemId == duck && e.Status == EvidenceStatus.Active));
            m.Phase(MatchPhase.Work2);
            m.Do(1, ActionVerb.PickUp, duck);
            Assert.AreEqual(EvidenceStatus.Moved, m.View().Evidence.Single(e => e.ItemId == duck).Status);
            m.Do(1, ActionVerb.Place, EntityIds.DeskSurface(0));
            m.Phase(MatchPhase.FinalInspection);
            m.Advance(m.Config.Durations.FinalInspection - 0.5);
            var records = m.View().Evidence.Where(e => e.ItemId == duck).ToList();
            Assert.AreEqual(1, records.Count, "the original record is re-activated rather than duplicated");
            Assert.AreEqual(EvidenceStatus.Active, records[0].Status);
        }

        [Test]
        public void CleanHandsIsNotBrokenByRemovingEvidenceFromYourOwnDesk()
        {
            var setup = TestMatch.Setup();
            setup.ForceObjectives = new System.Collections.Generic.List<string> { "CLEAN_HANDS", "FIXER", "SCAPEGOAT", "" };
            var m = new TestMatch(setup: setup); m.Work();
            string duck = EntityIds.Suspicious(ItemVariants.Duck);
            m.Plant(2, 0, drawer: false);
            RaidThroughAllDesks(m);
            m.Phase(MatchPhase.Work2);
            m.Do(0, ActionVerb.PickUp, duck);
            Assert.IsTrue(m.Engine.BuildHostDebugView().Incidents.Any(i => i.Type == SabotageType.EvidenceTampering && i.Actor == 0), "tampering is still misconduct");
            Assert.IsTrue(m.View(0).Me.Objective.Flags.Contains("clean"), "but it is not sabotage for Clean Hands");
        }
    }
}
