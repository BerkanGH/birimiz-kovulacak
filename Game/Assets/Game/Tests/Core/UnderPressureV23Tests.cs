using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class UnderPressureV23Tests
    {
        private static TestMatch Match(int required = 2, bool zeroObservationWeights = false)
        {
            MatchSetup setup = TestMatch.Setup();
            setup.ForceObjectives = new List<string> { "UNDER_PRESSURE", "FIXER", "SCAPEGOAT", "" };
            setup.ForceCards = new List<List<SabotageType>>
            {
                new List<SabotageType> { SabotageType.LockWorkstation },
                new List<SabotageType>(),
                new List<SabotageType> { SabotageType.FakeEmail, SabotageType.SaltCoffee },
                new List<SabotageType>()
            };
            return new TestMatch(setup: setup, configure: c =>
            {
                c.QuotaTarget = 0;
                c.Objectives.Single(o => o.Id == "UNDER_PRESSURE").Count = required;
                c.Durations.Raid = 40;
                c.Durations.FinalInspection = 40;
                c.Manager.VisionRange = 100;
                c.Manager.VisionHalfAngle = 180;
                c.Manager.ExpectedPerformanceAtFinal = 100;
                if (zeroObservationWeights)
                    foreach (ObservationKind kind in System.Enum.GetValues(typeof(ObservationKind)))
                        c.ManagerProfiles[0].ObservationFactors.Add(new ObservationFactor(kind, 0));
            }, arrange: l =>
            {
                foreach (ManagerStop stop in l.ManagerRoute) stop.DwellSeconds = 5;
            });
        }

        private static void CompleteWorkTwoTasks(TestMatch m)
        {
            m.Phase(MatchPhase.Work2);
            m.Coffee(0, 0); m.Coffee(0, 1);
            Assert.AreEqual(2, m.View().Me.Objective.Progress);
        }

        private static void ObserveIncriminating(TestMatch m, ObservationKind kind, MatchPhase phase)
        {
            // Prepare physical evidence before the inspection so its source is unambiguous.
            if (kind == ObservationKind.SuspiciousItemAtDesk) m.Plant(2, 0);
            if (kind == ObservationKind.TipFound) m.Do(2, ActionVerb.TipManager, EntityIds.Desk(0));
            if (kind == ObservationKind.CarryingSuspicious)
                m.Do(0, ActionVerb.PickUp, EntityIds.Suspicious(ItemVariants.Duck));
            if (kind == ObservationKind.SaltyCoffee)
            {
                m.Do(2, ActionVerb.PickUp, EntityIds.Mug(2));
                m.Do(2, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine);
                m.Do(2, ActionVerb.SaltCoffee, EntityIds.CoffeeMachine);
                m.Do(2, ActionVerb.Drop);
                m.Do(0, ActionVerb.PickUp, EntityIds.Mug(2));
            }
            m.Phase(phase);
            if (kind == ObservationKind.CaughtInAct)
                m.Do(0, ActionVerb.LockWorkstation, EntityIds.DeskComputer(1));
            else if (kind == ObservationKind.InappropriateEmail)
            {
                m.Do(0, ActionVerb.WriteReport, EntityIds.DeskComputer(0));
                m.Do(2, ActionVerb.SendFakeEmail, EntityIds.DeskComputer(0), 0);
            }
            else if (kind == ObservationKind.SaltyCoffee)
                m.Do(0, ActionVerb.Place, EntityIds.DeliveryTray);
            else if (kind == ObservationKind.CarryingSuspicious)
                m.Move(0, m.Layout.GetDesk(0).SeatPosition);
            m.Advance(8);
            Assert.IsTrue(m.View().Observations.Any(o => o.Kind == kind && o.Subject == 0 && o.Phase == phase), kind.ToString());
        }

        [TestCase(ObservationKind.NotAtDesk)]
        [TestCase(ObservationKind.AtWrongDesk)]
        [TestCase(ObservationKind.MovingDuringInspection)]
        [TestCase(ObservationKind.BehindOnWork)]
        [TestCase(ObservationKind.MessyDesk)]
        [TestCase(ObservationKind.PersonalCall)]
        public void OrdinaryFinalObservationPreservesProgressAndCompletion(ObservationKind kind)
        {
            var m = Match(); CompleteWorkTwoTasks(m);
            m.Phase(MatchPhase.FinalInspection);
            if (kind == ObservationKind.AtWrongDesk)
                m.Move(0, m.Layout.GetDesk(1).SeatPosition);
            else if (kind == ObservationKind.MovingDuringInspection)
                m.Move(0, m.Layout.GetDesk(0).SeatPosition, speed: 3);
            else if (kind == ObservationKind.PersonalCall)
            {
                m.Move(0, m.Layout.GetDesk(0).SeatPosition);
                Assert.IsTrue(m.Request(0, ActionVerb.CallPlayer, param: 1).Accepted);
                Assert.IsTrue(m.Request(1, ActionVerb.AnswerCall).Accepted);
            }
            m.Advance(12);
            Assert.IsTrue(m.View().Observations.Any(o => o.Kind == kind && o.Subject == 0 &&
                o.Phase == MatchPhase.FinalInspection && o.Severity > 0), kind.ToString());
            Assert.IsFalse(m.View().Me.Objective.Flags.Contains("penalty"));
            Assert.AreEqual(ObjectiveStatus.OnTrack, m.View().Me.Objective.Status);
            m.Finish();
            Assert.IsTrue(m.View().Result.Players.Single(p => p.Player == 0).ObjectiveCompleted);
            Assert.AreEqual(ObjectiveStatus.Completed, m.View().Me.Objective.Status);
        }

        [TestCase(ObservationKind.CaughtInAct, false)]
        [TestCase(ObservationKind.CarryingSuspicious, false)]
        [TestCase(ObservationKind.SuspiciousItemAtDesk, false)]
        [TestCase(ObservationKind.TipFound, false)]
        [TestCase(ObservationKind.InappropriateEmail, false)]
        [TestCase(ObservationKind.SaltyCoffee, false)]
        [TestCase(ObservationKind.CaughtInAct, true)]
        [TestCase(ObservationKind.CarryingSuspicious, true)]
        [TestCase(ObservationKind.SuspiciousItemAtDesk, true)]
        [TestCase(ObservationKind.TipFound, true)]
        [TestCase(ObservationKind.InappropriateEmail, true)]
        [TestCase(ObservationKind.SaltyCoffee, true)]
        public void IncriminatingFinalObservationBlocksRegardlessOfWeight(ObservationKind kind, bool zeroObservationWeights)
        {
            var m = Match(zeroObservationWeights: zeroObservationWeights); CompleteWorkTwoTasks(m);
            ObserveIncriminating(m, kind, MatchPhase.FinalInspection);
            if (zeroObservationWeights)
                Assert.AreEqual(0, m.View().Observations.Single(o => o.Kind == kind && o.Subject == 0 &&
                    o.Phase == MatchPhase.FinalInspection).Severity);
            Assert.AreEqual(2, m.View().Me.Objective.Progress);
            Assert.IsTrue(m.View().Me.Objective.Flags.Contains("penalty"));
            Assert.AreEqual(ObjectiveStatus.InProgress, m.View().Me.Objective.Status);
            m.Finish();
            Assert.IsFalse(m.View().Result.Players.Single(p => p.Player == 0).ObjectiveCompleted);
            Assert.IsFalse(m.View().Result.Players.Single(p => p.Player == 0).Won);
            Assert.AreEqual(ObjectiveStatus.Failed, m.View().Me.Objective.Status);
        }

        [TestCase(ObservationKind.CaughtInAct)]
        [TestCase(ObservationKind.CarryingSuspicious)]
        [TestCase(ObservationKind.SuspiciousItemAtDesk)]
        [TestCase(ObservationKind.TipFound)]
        [TestCase(ObservationKind.InappropriateEmail)]
        [TestCase(ObservationKind.SaltyCoffee)]
        public void EarlierIncriminatingObservationDoesNotBlockFinalObjective(ObservationKind kind)
        {
            var m = Match(); m.Work();
            ObserveIncriminating(m, kind, MatchPhase.Raid);
            m.Phase(MatchPhase.Work2);
            if (kind == ObservationKind.CarryingSuspicious) m.Do(0, ActionVerb.Drop);
            if (kind == ObservationKind.SuspiciousItemAtDesk)
            {
                m.Do(2, ActionVerb.PickUp, EntityIds.Suspicious(ItemVariants.Duck));
                m.Do(2, ActionVerb.Drop);
            }
            CompleteWorkTwoTasks(m);
            Assert.IsFalse(m.View().Me.Objective.Flags.Contains("penalty"));
            m.Finish();
            Assert.IsFalse(m.View().Me.Objective.Flags.Contains("penalty"));
            Assert.IsTrue(m.View().Result.Players.Single(p => p.Player == 0).ObjectiveCompleted);
        }

        [TestCase(ObservationKind.SuspiciousItemAtDesk, true)]
        [TestCase(ObservationKind.SuspiciousItemAtDesk, false)]
        [TestCase(ObservationKind.TipFound, true)]
        [TestCase(ObservationKind.TipFound, false)]
        public void DeskFindUsesObservationAttributionInsteadOfAssignedOwner(ObservationKind kind, bool blamedHolder)
        {
            var m = Match(); CompleteWorkTwoTasks(m);
            int desk = blamedHolder ? 1 : 0;
            int nameplate = blamedHolder ? 0 : 1;
            m.SwapInto(2, nameplate, desk); m.Do(2, ActionVerb.Drop);
            if (kind == ObservationKind.SuspiciousItemAtDesk) m.Plant(2, desk);
            else m.Do(2, ActionVerb.TipManager, EntityIds.Desk(desk));
            m.Phase(MatchPhase.FinalInspection); m.Advance(12);
            ObservationView observation = m.View().Observations.Single(o => o.Kind == kind &&
                o.DeskIndex == desk && o.Phase == MatchPhase.FinalInspection);
            Assert.AreEqual(nameplate, observation.Subject);
            Assert.AreEqual(blamedHolder, m.View().Me.Objective.Flags.Contains("penalty"));
            m.Finish();
            Assert.AreEqual(!blamedHolder, m.View().Result.Players.Single(p => p.Player == 0).ObjectiveCompleted);
        }

        [TestCase(2, 0, 0, 2, false)]
        [TestCase(1, 1, 1, 2, false)]
        [TestCase(0, 2, 0, 2, true)]
        [TestCase(0, 3, 0, 2, true)]
        [TestCase(0, 2, 0, 3, false)]
        [TestCase(0, 3, 0, 3, true)]
        public void OnlyCompletedWorkTwoTasksMeetConfiguredCount(int workOne, int workTwo, int final, int required, bool completed)
        {
            var m = Match(required); int mug = 0;
            m.Work();
            for (int i = 0; i < workOne; i++) m.Coffee(0, mug++);
            m.Phase(MatchPhase.Work2);
            for (int i = 0; i < workTwo; i++) m.Coffee(0, mug++);
            m.Phase(MatchPhase.FinalInspection);
            for (int i = 0; i < final; i++) m.Coffee(0, mug++);
            Assert.AreEqual(required, m.View().Me.Objective.Required);
            Assert.AreEqual(workTwo, m.View().Me.Objective.Progress);
            Assert.IsFalse(m.View().Me.Objective.Flags.Contains("penalty"));
            m.Finish();
            Assert.AreEqual(workOne + workTwo + final, m.Player(0).TasksCompleted);
            Assert.AreEqual(workTwo, m.View().Me.Objective.Progress);
            Assert.AreEqual(completed, m.View().Result.Players.Single(p => p.Player == 0).ObjectiveCompleted);
        }
    }
}
