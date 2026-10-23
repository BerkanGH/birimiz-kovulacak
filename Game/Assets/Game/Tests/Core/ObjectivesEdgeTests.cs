using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class ObjectivesEdgeTests
    {
        private static MatchSetup Objective(string card, int target = 1)
        {
            MatchSetup setup = TestMatch.Setup();
            setup.ForceObjectives = new List<string> { card, "FIXER", "SCAPEGOAT", "" };
            setup.ForceTargets = new List<int> { target, -1, 0, -1 };
            return setup;
        }

        [TestCase("SCAPEGOAT")]
        [TestCase("CHAOS_COORDINATOR")]
        [TestCase("CLEAN_HANDS")]
        [TestCase("OFFICE_ALL_ROUNDER")]
        [TestCase("FIXER")]
        [TestCase("PERFORMANCE_RIVAL")]
        [TestCase("UNDER_PRESSURE")]
        public void UnachievedPredicatesCannotBecomeWinsWhenCompanySucceeds(string card)
        {
            var m = new TestMatch(setup: Objective(card), configure: c => c.QuotaTarget = 0, arrange: l => l.ManagerRoute.Clear());
            m.Finish();
            PlayerOutcome outcome = m.View().Result.Players.Single(p => p.Player == 0);
            Assert.IsFalse(outcome.ObjectiveCompleted, card);
            Assert.IsFalse(outcome.Won, card);
            Assert.AreEqual(ObjectiveStatus.Failed, m.View().Me.Objective.Status);
        }

        [Test]
        public void GuardianUsesTargetsAssignedDeskEvenWhenAnotherNameplateIsDisplayed()
        {
            var m = new TestMatch(setup: Objective("DESK_GUARDIAN"), configure: c => c.QuotaTarget = 0);
            m.Work();
            m.Plant(2, 1, drawer: false);
            m.SwapInto(3, 0, 1);
            m.Do(3, ActionVerb.Drop);
            m.Phase(MatchPhase.Raid); m.Advance(10);
            Assert.AreEqual(0, m.FindEvidence(EntityIds.Suspicious(ItemVariants.Duck)).AttributedTo);
            Assert.IsTrue(m.View().Me.Objective.Flags.Contains("raid:found"));
            m.Finish();
            Assert.IsFalse(m.View().Result.Players.Single(p => p.Player == 0).ObjectiveCompleted);
        }

        [Test]
        public void WorkTwoTasksDoNotSatisfyUnderPressureAfterNegativeFinalObservation()
        {
            var m = new TestMatch(setup: Objective("UNDER_PRESSURE"), configure: c => c.QuotaTarget = 0);
            m.Phase(MatchPhase.Work2);
            m.Coffee(0, 0); m.Coffee(0, 1);
            m.Move(0, new Vec3(60, 0, 60));
            m.Finish();
            Assert.GreaterOrEqual(m.View().Me.Objective.Progress, 2);
            Assert.IsTrue(m.View().Me.Objective.Flags.Contains("penalty"));
            Assert.IsFalse(m.View().Result.Players.Single(p => p.Player == 0).ObjectiveCompleted);
        }

        [Test]
        public void CleanHandsRejectsSabotageDespiteEnoughTasksAndTypes()
        {
            var m = new TestMatch(setup: Objective("CLEAN_HANDS"), configure: c => c.QuotaTarget = 0, arrange: l => l.ManagerRoute.Clear());
            m.Work(); m.Coffee(0, 0); m.Coffee(0, 1); m.Coffee(0, 2);
            string report = m.Print(0);
            m.Do(0, ActionVerb.PickUp, report); m.Do(0, ActionVerb.Place, EntityIds.DeliveryTray);
            m.Do(0, ActionVerb.LockWorkstation, EntityIds.DeskComputer(1));
            m.Finish();
            Assert.AreEqual(4, m.View().Me.Objective.Progress);
            Assert.AreEqual(2, m.View().Me.Objective.Progress2);
            Assert.IsTrue(m.View().Me.Objective.Flags.Contains("sabotaged"));
            Assert.IsFalse(m.View().Result.Players.Single(p => p.Player == 0).ObjectiveCompleted);
        }

        [Test]
        public void InformantObjectiveProgressNeverReportsTheSecretMisconductTier()
        {
            var m = new TestMatch(); m.Work();
            m.Do(2, ActionVerb.LockWorkstation, EntityIds.DeskComputer(0));
            Assert.Greater(m.Engine.BuildHostDebugView().TrueMisconduct[2], 0);
            ObjectiveView objective = m.View(3).Me.Objective;
            Assert.AreEqual(ObjectiveStatus.PendingVerdict, objective.Status);
            Assert.AreEqual(0, objective.Progress); Assert.AreEqual(0, objective.Progress2);
            Assert.IsEmpty(objective.Flags);
            m.Phase(MatchPhase.MeetingVerdict);
            Assert.AreEqual(ObjectiveStatus.PendingVerdict, m.View(3).Me.Objective.Status);
        }

        [Test]
        public void RecoveryRecordsAlreadyMovedReportWithoutChargingTamperingForItsDiscovery()
        {
            var m = new TestMatch(); m.Work();
            string report = m.Print(0);
            m.Do(2, ActionVerb.PickUp, report); m.Do(2, ActionVerb.Place, EntityIds.DeskDrawer(1));
            m.Do(0, ActionVerb.SearchContainer, EntityIds.DeskDrawer(1)); m.Do(0, ActionVerb.PickUp, report);
            EvidenceView record = m.View().Evidence.Single(e => e.Kind == EvidenceKind.HiddenReportFound);
            Assert.AreEqual(EvidenceStatus.Moved, record.Status);
            Assert.AreEqual(m.Config.Risk.HiddenReportInDrawer * m.Config.Risk.MovedEvidenceFactor, record.RiskWeight);
            Assert.AreEqual(0, m.Engine.BuildHostDebugView().TrueMisconduct[0]);
        }
    }
}
