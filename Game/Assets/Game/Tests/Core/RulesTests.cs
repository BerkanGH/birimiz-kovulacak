using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class RulesTests
    {
        [Test]
        public void RolesAreUniqueAndInformantDoesNotRepeatAcrossRematches()
        {
            SessionHistory history = null;
            int previous = -1;
            for (int seed = 0; seed < 80; seed++)
            {
                MatchSetup setup = TestMatch.Setup(seed, false);
                setup.History = history;
                var m = new TestMatch(setup: setup);
                int informant = Enumerable.Range(0, 4).Single(p => m.View(p).Me.Role == Role.Informant);
                if (previous >= 0) Assert.AreNotEqual(previous, informant);
                Assert.AreEqual(3, Enumerable.Range(0, 4).Count(p => m.View(p).Me.Role == Role.Employee));
                history = m.Engine.BuildNextHistory();
                Assert.AreEqual(seed + 1, history.MatchesPlayed);
                Assert.AreEqual(informant, history.PastInformants.Last());
                previous = informant;
            }
        }

        [Test]
        public void ObjectivesAreDistinctAndHistoryReducesRepeatedTargets()
        {
            int withHistory = 0, withoutHistory = 0;
            for (int seed = 0; seed < 48; seed++)
            {
                var random = new TestMatch(setup: TestMatch.Setup(seed, false));
                var employees = Enumerable.Range(0, 4).Where(p => random.View(p).Me.Role == Role.Employee).ToList();
                Assert.AreEqual(3, employees.Select(p => random.View(p).Me.Objective.CardId).Distinct().Count());
                foreach (int p in employees) Assert.AreNotEqual(p, random.View(p).Me.Objective.Target);

                MatchSetup firstSetup = TargetedSetup(seed);
                var first = new TestMatch(setup: firstSetup);
                var history = first.Engine.BuildNextHistory();
                MatchSetup nextSetup = TargetedSetup(seed + 1000);
                var noHistory = new TestMatch(setup: nextSetup);
                nextSetup.History = history;
                var next = new TestMatch(setup: nextSetup);
                for (int p = 0; p < 3; p++)
                {
                    int previous = first.View(p).Me.Objective.Target;
                    Assert.That(previous, Is.InRange(0, 3));
                    Assert.AreNotEqual(p, next.View(p).Me.Objective.Target);
                    if (next.View(p).Me.Objective.Target == previous) withHistory++;
                    if (noHistory.View(p).Me.Objective.Target == previous) withoutHistory++;
                }
            }
            Assert.Less(withHistory, withoutHistory, "History should measurably reduce repeated holder-target pairs");
        }

        private static MatchSetup TargetedSetup(int seed)
        {
            MatchSetup setup = TestMatch.Setup(seed);
            setup.ForceObjectives = new List<string> { "DISMISSAL_PLAN", "PROTECTION_DEAL", "SCAPEGOAT", "" };
            setup.ForceTargets = null;
            return setup;
        }

        [Test]
        public void PrivateObjectivesRolesScansAndClosedItemsDoNotLeakInEncodedViews()
        {
            const string secretVariant = "CLASSIFIED_DUCK_VARIANT_7c196";
            const string secretItem = "item.classified.8e8d3";
            var m = new TestMatch(arrange: l =>
            {
                ItemDef duck = l.GetItem(EntityIds.Suspicious(ItemVariants.Duck));
                duck.Id = secretItem;
                duck.Variant = secretVariant;
                duck.InitialSlotId = EntityIds.DeskDrawer(0);
            });
            m.Work();
            for (int p = 0; p < 4; p++)
            {
                PlayerView view = m.View(p);
                Assert.IsNull(view.Result);
                Assert.AreEqual(p == 3 ? Role.Informant : Role.Employee, view.Me.Role);
                Assert.AreEqual(p == 3, view.Me.Informant != null);
                Assert.IsNull(m.Item(secretItem, p));
                string bytes = Encoding.UTF8.GetString(WireCodec.EncodeView(view));
                Assert.IsFalse(bytes.Contains(secretItem));
                Assert.IsFalse(bytes.Contains(secretVariant));
                for (int other = 0; other < 3; other++)
                    if (other != p) Assert.IsFalse(bytes.Contains(m.View(other).Me.Objective.CardId));
            }
            m.Do(3, ActionVerb.SearchContainer, EntityIds.DeskDrawer(0));
            m.Do(3, ActionVerb.AuditScanItem, secretItem);
            Assert.AreEqual(1, m.View(3).Me.Informant.Scans.Count);
            Assert.AreEqual(2, m.View(3).Me.Notes.Count);
            for (int p = 0; p < 3; p++)
            {
                Assert.IsEmpty(m.View(p).Me.Notes);
                Assert.IsFalse(Encoding.UTF8.GetString(WireCodec.EncodeView(m.View(p))).Contains(secretVariant));
            }
            foreach (GameEvent note in m.Events.Where(e => e.Type == EventType.NoteAdded))
            {
                Assert.AreEqual(Audience.Player, note.Audience);
                Assert.IsTrue(note.IsVisibleTo(3));
                Assert.IsFalse(note.IsVisibleTo(0));
            }
        }

        [Test]
        public void ContainerKnowledgeBelongsToPlacersAndSearchersAndResetsOnNewPlacement()
        {
            var m = new TestMatch(); m.Work();
            string duck = EntityIds.Suspicious(ItemVariants.Duck);
            m.Plant(2, 0);
            Assert.IsNotNull(m.Item(duck, 2));
            Assert.IsNull(m.Item(duck, 0));
            m.Do(0, ActionVerb.SearchContainer, EntityIds.DeskDrawer(0));
            Assert.IsNotNull(m.Item(duck, 0));
            m.Do(0, ActionVerb.PickUp, duck);
            Assert.IsNotNull(m.Item(duck, 1), "Carried items are public");
            m.Do(0, ActionVerb.Place, EntityIds.HideInCabinet);
            Assert.IsNull(m.Item(duck, 2), "Former placer cannot see into a new closed container");
            Assert.IsNotNull(m.Item(duck, 0));
        }

        [Test]
        public void WitnessNotesRequireFacingRangeAndArePrivate()
        {
            var m = new TestMatch(); m.Work();
            Vec3 desk = m.Layout.GetSlot(EntityIds.DeskDrawer(0)).Position;
            m.Move(1, desk + new Vec3(0, 0, -1), 0);
            m.Move(3, desk + new Vec3(0, 0, -1), 180);
            m.Plant(2, 0);
            PrivateNote note = m.View(1).Me.Notes.Single(n => n.Kind == NoteKind.Witness);
            Assert.AreEqual(2, note.Subject);
            Assert.AreEqual(PublicActivity.Rummaging, note.Activity);
            Assert.AreEqual(ItemVariants.Duck, note.HeldBefore);
            Assert.IsEmpty(note.HeldAfter);
            Assert.IsEmpty(m.View(0).Me.Notes);
            Assert.IsEmpty(m.View(3).Me.Notes);
            Assert.IsFalse(Encoding.UTF8.GetString(WireCodec.EncodeView(m.View(0))).Contains(EntityIds.Suspicious(ItemVariants.Duck)));
            Assert.IsTrue(m.Events.Where(e => e.Type == EventType.NoteAdded).All(e => e.Audience == Audience.Player && e.AudiencePlayer == 1));
        }

        [Test]
        public void LockActorIsAbsentFromPublicEvidenceEventsAndLog()
        {
            var m = new TestMatch(); m.Work();
            var started = m.Start(2, ActionVerb.LockWorkstation, EntityIds.DeskComputer(0));
            Assert.AreEqual(PublicActivity.Typing, m.Player(2).Activity);
            Assert.AreEqual(ActionVerb.LockWorkstation, m.View(2).Me.ActiveInteraction.Verb);
            m.AdvanceTo(started.CompletesAt + 0.001);
            EvidenceView evidence = m.View().Evidence.Single(e => e.Kind == EvidenceKind.WorkstationLockLog);
            Assert.AreEqual(PlayerIds.None, evidence.AttributedTo);
            Assert.AreEqual(PlayerIds.None, evidence.SourcePlayer);
            Assert.AreEqual(PlayerIds.None, evidence.SubjectPlayer);
            Assert.AreEqual(PlayerIds.None, m.Events.Single(e => e.Type == EventType.WorkstationLocked).Actor);
            Assert.IsTrue(m.View().Log.Where(e => e.Kind == EventType.WorkstationLocked).All(e => e.Actor == PlayerIds.None));
        }

        [Test]
        public void SimultaneousPickupsHaveExactlyOneWinner()
        {
            var m = new TestMatch(); m.Work();
            string item = EntityIds.Mug(0);
            m.MoveTo(0, item); m.MoveTo(1, item);
            ActionResult first = m.Request(0, ActionVerb.PickUp, item);
            ActionResult second = m.Request(1, ActionVerb.PickUp, item);
            Assert.AreEqual(1, new[] { first, second }.Count(r => r.Accepted));
            Assert.AreEqual(0, m.Item(item).CarrierId);
            Assert.AreEqual(1, m.View().Players.Count(p => p.CarryingItemId == item));
        }

        [Test]
        public void TaskQuotaIsAwardedOnceAndMovingCancelsCompletion()
        {
            var m = new TestMatch(); m.Work();
            m.Do(0, ActionVerb.PickUp, EntityIds.Mug(0));
            m.Do(0, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine);
            m.MoveTo(0, EntityIds.DeliveryTray);
            var request = new ActionRequest(m.Engine.MatchId, 8000, ActionVerb.Place, EntityIds.DeliveryTray);
            ActionResult result = m.Engine.Submit(0, request, m.Now);
            Assert.IsTrue(result.Accepted);
            Assert.AreEqual(FailureCode.Duplicate, m.Engine.Submit(0, request, m.Now).Failure);
            m.AdvanceTo(result.CompletesAt + 0.001);
            Assert.AreEqual(m.Config.Tasks.CoffeePoints, m.View().Quota.Points);
            Assert.AreEqual(FailureCode.Duplicate, m.Engine.Submit(0, request, m.Now).Failure);
            m.Do(0, ActionVerb.PickUp, EntityIds.Mug(0));
            m.MoveTo(0, EntityIds.DeliveryTray);
            Assert.AreEqual(FailureCode.WrongItem, m.Request(0, ActionVerb.Place, EntityIds.DeliveryTray).Failure);
            m.Do(0, ActionVerb.Drop);
            var write = m.Start(0, ActionVerb.WriteReport, EntityIds.DeskComputer(0));
            m.Move(0, new Vec3(80, 0, 80));
            m.AdvanceTo(write.CompletesAt + m.Config.Tasks.PrintSeconds + 1);
            Assert.IsNull(m.View().Me.ActiveInteraction);
            Assert.IsEmpty(m.View().Items.Where(i => i.Kind == ItemKind.Report));
            Assert.AreEqual(m.Config.Tasks.CoffeePoints, m.View().Quota.Points);
            Assert.IsTrue(m.Events.Any(e => e.Type == EventType.InteractionCancelled && e.AudiencePlayer == 0));
        }

        [Test]
        public void DeliveryRevalidatesLastOrderWhenTwoPlayersFinishTogether()
        {
            var m = new TestMatch(configure: c => c.Tasks.InitialCoffeeOrders = 1); m.Work();
            m.Do(0, ActionVerb.PickUp, EntityIds.Mug(0));
            m.Do(0, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine);
            m.Do(1, ActionVerb.PickUp, EntityIds.Mug(1));
            m.Do(1, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine);
            var a = m.Start(0, ActionVerb.Place, EntityIds.DeliveryTray);
            var b = m.Start(1, ActionVerb.Place, EntityIds.DeliveryTray);
            m.AdvanceTo(Math.Max(a.CompletesAt, b.CompletesAt) + 0.001);
            Assert.AreEqual(1, m.View().Quota.Points);
            Assert.AreEqual(1, m.View().Players.Sum(p => p.TasksCompleted));
            Assert.AreEqual(1, m.Events.Count(e => e.Type == EventType.InteractionCancelled));
        }

        [Test]
        public void StationsReserveAndCancellationReleasesThem()
        {
            var m = new TestMatch(); m.Work();
            m.Do(0, ActionVerb.PickUp, EntityIds.Mug(0));
            m.Do(1, ActionVerb.PickUp, EntityIds.Mug(1));
            m.Start(0, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine);
            m.MoveTo(1, EntityIds.CoffeeMachine);
            Assert.AreEqual(FailureCode.TargetBusy, m.Request(1, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine).Failure);
            Assert.AreEqual(FailureCode.Busy, m.Request(0, ActionVerb.Drop).Failure);
            m.Do(0, ActionVerb.CancelInteraction);
            m.Do(1, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine);
            Assert.AreEqual(MugFill.Empty, m.Item(EntityIds.Mug(0)).Fill);
            Assert.AreEqual(MugFill.Full, m.Item(EntityIds.Mug(1)).Fill);
        }

        [Test]
        public void AnswerStartedBeforeRingDeadlineCanCompleteAfterItWithoutMiss()
        {
            var m = new TestMatch(configure: c =>
            {
                c.Tasks.PhoneIntervalMin = 1;
                c.Tasks.PhoneIntervalMax = 1;
                c.Tasks.PhoneRingSeconds = 1;
                c.Tasks.AnswerPhoneSeconds = 2;
            }); m.Work();
            for (int i = 0; i < 30 && !m.View().Quota.PhoneRinging; i++) m.Advance(0.1);
            Assert.IsTrue(m.View().Quota.PhoneRinging);
            m.AdvanceTo(m.View().Quota.PhoneRingEnds - 0.1);
            m.Do(0, ActionVerb.AnswerPhone, EntityIds.Phone);
            Assert.AreEqual(m.Config.Tasks.PhonePoints, m.View().Quota.Points);
            Assert.AreEqual(1, m.Player(0).TasksCompleted);
            Assert.IsFalse(m.Events.Any(e => e.Type == EventType.PhoneMissed));
        }

        [Test]
        public void DemandAccumulatesToCapsAndFreezeStopsOfficeProduction()
        {
            var m = new TestMatch(configure: c =>
            {
                c.Tasks.InitialCoffeeOrders = 0;
                c.Tasks.InitialReportOrders = 0;
                c.Tasks.InitialDirtyDesks = 0;
                c.Tasks.CoffeeOrderInterval = 2;
                c.Tasks.ReportOrderInterval = 3;
                c.Tasks.DirtIntervalSeconds = 4;
            }); m.Work(); m.Advance(15);
            Assert.AreEqual(m.Config.Tasks.MaxCoffeeOrders, m.View().Quota.CoffeeOrders);
            Assert.AreEqual(m.Config.Tasks.MaxReportOrders, m.View().Quota.ReportOrders);
            Assert.That(m.View().Quota.DirtyDesks, Is.InRange(1, 4));
            Assert.AreEqual(0, m.View().Quota.Points);
            m.Phase(MatchPhase.MeetingDiscussion);
            Assert.IsTrue(m.View().Quota.Frozen);
            int eventsAtFreeze = m.Events.Count;
            m.Finish();
            Assert.IsFalse(m.Events.Skip(eventsAtFreeze).Any(e => e.Type == EventType.OrderAdded || e.Type == EventType.DeskDirty || e.Type == EventType.ReportPrinted));
        }

        [Test]
        public void PrinterSerializesConcurrentWritersAndArchivesDeliveredReport()
        {
            var m = new TestMatch(); m.Work();
            ActionResult first = m.Start(0, ActionVerb.WriteReport, EntityIds.DeskComputer(0));
            ActionResult second = m.Start(1, ActionVerb.WriteReport, EntityIds.DeskComputer(1));
            m.AdvanceTo(Math.Max(first.CompletesAt, second.CompletesAt) + m.Config.Tasks.PrintSeconds + 0.01);
            Assert.AreEqual(1, m.View().Items.Count(i => i.Kind == ItemKind.Report));
            Assert.AreEqual(0, m.View().Items.Single(i => i.Kind == ItemKind.Report).ReportOwner);
            m.Advance(m.Config.Tasks.PrintSeconds + 0.01);
            var reports = m.View().Items.Where(i => i.Kind == ItemKind.Report).ToList();
            Assert.AreEqual(2, reports.Count);
            Assert.AreNotEqual(reports[0].Id, reports[1].Id);
            m.Do(0, ActionVerb.PickUp, reports[0].Id);
            m.Do(0, ActionVerb.Place, EntityIds.DeliveryTray);
            Assert.AreEqual(m.Config.Tasks.ReportPoints, m.View().Quota.Points);
            Assert.IsFalse(m.Request(0, ActionVerb.PickUp, reports[0].Id).Accepted);
            Assert.AreEqual(m.Config.Tasks.ReportPoints, m.View().Quota.Points);
        }

        [Test]
        public void RubberDuckScenario()
        {
            var m = new TestMatch(); m.Work();
            string duck = EntityIds.Suspicious(ItemVariants.Duck);
            m.Plant(2, 0);
            m.SwapInto(1, 1, 0);
            m.Advance(m.Config.GetSabotage(SabotageType.NameplateSwap).ActorCooldownSeconds + 0.1);
            m.Do(1, ActionVerb.Place, EntityIds.DeskNameplate(1));
            Assert.AreEqual(0, m.View().Desks.Single(d => d.Index == 1).DisplayedNameplateOf);
            m.Phase(MatchPhase.Raid); m.Advance(10);
            EvidenceView found = m.FindEvidence(duck);
            Assert.AreEqual(1, found.AttributedTo);
            Assert.AreEqual(DiscoverySource.Manager, found.Source);
            m.Do(3, ActionVerb.AuditScanEvidence, param: found.Id);
            ScanResultView scan = m.View(3).Me.Informant.Scans.Single();
            Assert.AreEqual(Reliability.Low, scan.Reliability);
            Assert.AreEqual(ScanSignal.NameplateChangedAfterPlacement, scan.Signal);
            Assert.AreEqual(1, scan.ApparentOwner);
            m.Phase(MatchPhase.MeetingDiscussion);
            m.Do(0, ActionVerb.Accuse, param: 1);
            m.Do(2, ActionVerb.Accuse, param: 1);
            m.Do(3, ActionVerb.PresentNote, param: scan.NoteId);
            m.Phase(MatchPhase.MeetingVote);
            m.Do(0, ActionVerb.CastBallot, param: 1);
            m.Do(2, ActionVerb.CastBallot, param: 1);
            m.Do(3, ActionVerb.CastBallot, param: 1);
            m.Finish();
            MatchResultView result = m.View().Result;
            Assert.AreEqual(2, result.Incidents.Single(i => i.Type == SabotageType.PlantItem).Actor);
            Assert.IsTrue(result.Incidents.Where(i => i.Type == SabotageType.NameplateSwap).All(i => i.Actor == 1));
            Assert.AreEqual(2, result.Incidents.Count(i => i.Type == SabotageType.NameplateSwap));
            Assert.AreEqual(0, result.Players.Single(p => p.Player == 0).TrueMisconduct);
            Assert.AreEqual(1, result.Incidents.Single(i => i.Type == SabotageType.PlantItem).BlamedPlayer);
        }

        [Test]
        public void SealPreventsPickupDisposalAndAttributionChangesThroughFreeze()
        {
            var m = new TestMatch(); m.Work();
            string duck = EntityIds.Suspicious(ItemVariants.Duck);
            m.Plant(2, 0, drawer: false);
            m.Phase(MatchPhase.Raid); m.Advance(10);
            EvidenceView found = m.FindEvidence(duck);
            m.Do(3, ActionVerb.SealEvidence, param: found.Id);
            m.MoveTo(2, duck);
            Assert.AreEqual(FailureCode.Sealed, m.Request(2, ActionVerb.PickUp, duck).Failure);
            Assert.AreEqual(FailureCode.HandsEmpty, m.Request(2, ActionVerb.Place, EntityIds.JunkBin).Failure);
            m.SwapInto(1, 1, 0);
            Assert.AreEqual(1, m.View().Desks.Single(d => d.Index == 0).DisplayedNameplateOf);
            m.Phase(MatchPhase.MeetingDiscussion);
            EvidenceView sealedRecord = m.View().Evidence.Single(e => e.Id == found.Id);
            Assert.IsTrue(m.View().Quota.Frozen);
            Assert.IsTrue(sealedRecord.Sealed);
            Assert.AreEqual(EvidenceStatus.Active, sealedRecord.Status);
            Assert.AreEqual(0, sealedRecord.AttributedTo);
            Assert.IsTrue(m.Events.Where(e => e.Type == EventType.EvidenceUpdated && e.IntArg == found.Id).All(e => e.Actor == PlayerIds.None));
        }

        [Test]
        public void InformantCannotExceedChargesAndEmployeesCannotUseTools()
        {
            var m = new TestMatch(); m.Work();
            string item = EntityIds.Nameplate(0);
            m.MoveTo(0, item);
            Assert.AreEqual(FailureCode.NotAllowed, m.Request(0, ActionVerb.AuditScanItem, item).Failure);
            Assert.AreEqual(FailureCode.NotAllowed, m.Request(0, ActionVerb.AuditScanEvidence, param: 987).Failure);
            Assert.AreEqual(FailureCode.NotAllowed, m.Request(0, ActionVerb.SealEvidence, param: 987).Failure);
            m.Do(3, ActionVerb.AuditScanItem, item);
            m.Do(3, ActionVerb.AuditScanItem, item);
            Assert.AreEqual(FailureCode.NoCharges, m.Request(3, ActionVerb.AuditScanItem, item).Failure);
            Assert.AreEqual(0, m.View(3).Me.Informant.ScansLeft);
            m.Do(2, ActionVerb.LockWorkstation, EntityIds.DeskComputer(0));
            int evidence = m.View().Evidence.Single(e => e.Kind == EvidenceKind.WorkstationLockLog).Id;
            m.Do(3, ActionVerb.SealEvidence, param: evidence);
            Assert.AreEqual(FailureCode.NoCharges, m.Request(3, ActionVerb.SealEvidence, param: evidence).Failure);
            Assert.AreEqual(0, m.View(3).Me.Informant.SealsLeft);
        }

        [Test]
        public void ScanReliabilityUsesPublicOwnerAndHandlingHistoryWithoutNamingPlanter()
        {
            var m = new TestMatch(); m.Work();
            string duck = EntityIds.Suspicious(ItemVariants.Duck);
            m.Do(0, ActionVerb.PickUp, duck);
            m.Do(0, ActionVerb.Place, EntityIds.DeskSurface(0));
            m.Do(3, ActionVerb.AuditScanItem, duck);
            ScanResultView first = m.View(3).Me.Informant.Scans[0];
            Assert.AreEqual(Reliability.High, first.Reliability);
            Assert.AreEqual(0, first.ApparentOwner);
            Assert.AreEqual(ScanSignal.MovedRecently, first.Signal);
            m.Do(2, ActionVerb.PickUp, duck);
            m.Do(2, ActionVerb.Place, EntityIds.DeskSurface(0));
            m.Do(3, ActionVerb.AuditScanItem, duck);
            ScanResultView second = m.View(3).Me.Informant.Scans[1];
            Assert.AreEqual(Reliability.Medium, second.Reliability);
            Assert.AreEqual(0, second.ApparentOwner);
            Assert.IsNull(typeof(ScanResultView).GetField("Actor"));
            Assert.AreEqual(2, m.Engine.BuildHostDebugView().Incidents.Single().Actor);
        }

        [Test]
        public void EvidenceScanUsesItsRecordedPlacementAfterTheItemMovesElsewhere()
        {
            var m = new TestMatch(); m.Work();
            string duck = EntityIds.Suspicious(ItemVariants.Duck);
            m.Plant(2, 0, drawer: false);
            m.Phase(MatchPhase.Raid); m.Advance(10);
            int evidence = m.FindEvidence(duck).Id;
            m.Do(0, ActionVerb.PickUp, duck);
            m.Do(0, ActionVerb.Place, EntityIds.DeskSurface(1));
            m.Do(3, ActionVerb.AuditScanEvidence, param: evidence);
            m.Do(3, ActionVerb.AuditScanItem, duck);
            List<ScanResultView> scans = m.View(3).Me.Informant.Scans;
            Assert.AreEqual(0, scans[0].DeskIndex);
            Assert.AreEqual(0, scans[0].ApparentOwner);
            Assert.AreEqual(Reliability.Medium, scans[0].Reliability, "The original owner handled it, but did not make the recorded placement");
            Assert.AreEqual(1, scans[1].DeskIndex);
            Assert.AreEqual(1, scans[1].ApparentOwner);
            Assert.AreEqual(Reliability.Low, scans[1].Reliability);
        }

        [Test]
        public void NonPhysicalToolsWorkWhileBusyWithoutRevealingTheToolOrCancellingWork()
        {
            var m = new TestMatch(); m.Work();
            var writing = m.Start(3, ActionVerb.WriteReport, EntityIds.DeskComputer(3));
            Assert.IsTrue(m.Request(3, ActionVerb.AuditScanItem, EntityIds.Nameplate(3)).Accepted);
            Assert.IsNotNull(m.View(3).Me.ActiveInteraction);
            Assert.AreEqual(ActionVerb.WriteReport, m.View(3).Me.ActiveInteraction.Verb);
            Assert.AreEqual(PublicActivity.Typing, m.Player(3).Activity);
            Assert.IsNull(m.View(0).Me.Informant);
            Assert.IsFalse(m.Events.Any(e => e.Audience == Audience.All && e.Type == EventType.NoteAdded));
            m.AdvanceTo(writing.CompletesAt + 0.001);
            Assert.IsNull(m.View(3).Me.ActiveInteraction);
        }

        [Test]
        public void DeepSnapshotsAndAcceptedRequestsCannotMutateEngineState()
        {
            var m = new TestMatch(); m.Work();
            m.Do(3, ActionVerb.AuditScanItem, EntityIds.Nameplate(0));
            PlayerView snapshot = m.View(3);
            snapshot.Me.Informant.Scans[0].Reliability = Reliability.High;
            snapshot.Me.Informant.Scans.Clear();
            snapshot.Me.Notes[0].Contents.Add("forged-private-note");
            snapshot.Me.Objective.Flags.Add("forged-objective-flag");
            snapshot.Me.SabotageCooldowns[0] = double.MaxValue;
            snapshot.Items[0].Id = "forged-item";
            snapshot.Desks[0].DisplayedNameplateOf = 999;
            PlayerView fresh = m.View(3);
            Assert.AreEqual(1, fresh.Me.Informant.Scans.Count);
            Assert.IsEmpty(fresh.Me.Notes[0].Contents);
            Assert.IsFalse(fresh.Me.Objective.Flags.Contains("forged-objective-flag"));
            Assert.AreEqual(0, fresh.Me.SabotageCooldowns[0]);
            Assert.IsFalse(fresh.Items.Any(i => i.Id == "forged-item"));
            Assert.AreEqual(0, fresh.Desks[0].DisplayedNameplateOf);
            m.MoveTo(0, EntityIds.DeskComputer(0));
            var request = new ActionRequest(m.Engine.MatchId, 9000, ActionVerb.WriteReport, EntityIds.DeskComputer(0));
            ActionResult accepted = m.Engine.Submit(0, request, m.Now);
            Assert.IsTrue(accepted.Accepted);
            request.Verb = ActionVerb.LockWorkstation;
            request.TargetId = EntityIds.DeskComputer(1);
            m.AdvanceTo(accepted.CompletesAt + m.Config.Tasks.PrintSeconds + 0.001);
            Assert.AreEqual(1, m.View().Items.Count(i => i.Kind == ItemKind.Report && i.ReportOwner == 0));
            Assert.IsFalse(m.View().Desks.Any(d => d.PcLocked));
            HostDebugView debug = m.Engine.BuildHostDebugView();
            debug.ItemHandlers[EntityIds.Nameplate(0)].Add(999);
            Assert.IsFalse(m.Engine.BuildHostDebugView().ItemHandlers[EntityIds.Nameplate(0)].Contains(999));
        }

        [Test]
        public void MovingAndDisposingEvidenceChangesWeightAndAddsOnlyOneTamperingIncident()
        {
            var m = new TestMatch(); m.Work();
            string duck = EntityIds.Suspicious(ItemVariants.Duck);
            m.Plant(2, 0, drawer: false);
            m.Phase(MatchPhase.Raid); m.Advance(10);
            int evidence = m.FindEvidence(duck).Id;
            Assert.AreEqual(m.Config.Risk.SuspiciousItem, m.FindEvidence(duck).RiskWeight);
            m.Do(1, ActionVerb.PickUp, duck);
            EvidenceView moved = m.View().Evidence.Single(e => e.Id == evidence);
            Assert.AreEqual(EvidenceStatus.Moved, moved.Status);
            Assert.AreEqual(m.Config.Risk.SuspiciousItem * m.Config.Risk.MovedEvidenceFactor, moved.RiskWeight);
            m.Do(1, ActionVerb.Place, EntityIds.JunkBin);
            EvidenceView disposed = m.View().Evidence.Single(e => e.Id == evidence);
            Assert.AreEqual(EvidenceStatus.Disposed, disposed.Status);
            Assert.AreEqual(0, disposed.RiskWeight);
            var incidents = m.Engine.BuildHostDebugView().Incidents;
            Assert.AreEqual(1, incidents.Count(i => i.Type == SabotageType.EvidenceTampering));
            Assert.AreEqual(1, incidents.Single(i => i.Type == SabotageType.EvidenceTampering).Actor);
            Assert.AreEqual(2, incidents.Single(i => i.Type == SabotageType.PlantItem).Actor);
            Assert.AreEqual(1, incidents.Single(i => i.Type == SabotageType.PlantItem).ReversedBy);
        }

        [Test]
        public void ValidationOrderWrongMatchConnectionDuplicatePhaseAndBusy()
        {
            var m = new TestMatch();
            var wrong = new ActionRequest(m.Engine.MatchId + 1, 77, ActionVerb.PickUp, "missing");
            m.Engine.SetConnected(0, false, m.Now);
            Assert.AreEqual(FailureCode.WrongMatch, m.Engine.Submit(0, wrong, m.Now).Failure);
            wrong.MatchId = m.Engine.MatchId;
            Assert.AreEqual(FailureCode.NotConnected, m.Engine.Submit(0, wrong, m.Now).Failure);
            m.Engine.SetConnected(0, true, m.Now);
            Assert.AreEqual(FailureCode.WrongPhase, m.Engine.Submit(0, wrong, m.Now).Failure);
            m.Work();
            Assert.AreEqual(FailureCode.Duplicate, m.Engine.Submit(0, wrong, m.Now).Failure);
            Assert.AreEqual(FailureCode.WrongPhase, m.Request(0, ActionVerb.CastBallot, param: 1).Failure);
            m.Start(0, ActionVerb.WriteReport, EntityIds.DeskComputer(0));
            Assert.AreEqual(FailureCode.Busy, m.Request(0, ActionVerb.PickUp, "missing").Failure);
            Assert.DoesNotThrow(() => m.Engine.Submit(0, null, m.Now));
            Assert.DoesNotThrow(() => m.Engine.Submit(-100, wrong, m.Now));
        }

        [Test]
        public void RequestDuplicateWindowRetainsExactlyLast64ProcessedIds()
        {
            var m = new TestMatch(); m.Work();
            for (uint request = 1; request <= 65; request++)
                Assert.IsTrue(m.Engine.Submit(0, new ActionRequest(m.Engine.MatchId, request, ActionVerb.CancelInteraction), m.Now).Accepted);
            Assert.AreEqual(FailureCode.Duplicate,
                m.Engine.Submit(0, new ActionRequest(m.Engine.MatchId, 65, ActionVerb.CancelInteraction), m.Now).Failure);
            Assert.IsTrue(m.Engine.Submit(0, new ActionRequest(m.Engine.MatchId, 1, ActionVerb.CancelInteraction), m.Now).Accepted);
        }

        [Test]
        public void PhysicalValidationRejectsUnseenItemsWrongDeskKindsAndRange()
        {
            var m = new TestMatch(); m.Work();
            Assert.AreEqual(FailureCode.TooFar, m.Request(0, ActionVerb.PickUp, EntityIds.Mug(0)).Failure);
            Assert.AreEqual(FailureCode.NotOwnDesk, m.Request(0, ActionVerb.WriteReport, EntityIds.DeskComputer(1)).Failure);
            Assert.AreEqual(FailureCode.NotAllowed, m.Request(0, ActionVerb.LockWorkstation, EntityIds.DeskComputer(0)).Failure);
            Assert.AreEqual(FailureCode.HandsEmpty, m.Request(0, ActionVerb.Drop).Failure);
            m.Plant(2, 0);
            Assert.AreEqual(FailureCode.InvalidTarget, m.Request(0, ActionVerb.PickUp, EntityIds.Suspicious(ItemVariants.Duck)).Failure);
            m.Do(0, ActionVerb.PickUp, EntityIds.Mug(0));
            Assert.AreEqual(FailureCode.WrongItem, m.Request(0, ActionVerb.Place, EntityIds.DeskNameplate(0)).Failure);
            Assert.AreEqual(FailureCode.InvalidTarget, m.Request(0, ActionVerb.Place, EntityIds.PrinterTray).Failure);
        }

        [Test]
        public void MalformedClientTargetsAndEnumValuesNeverThrow()
        {
            var m = new TestMatch(); m.Work();
            uint sequence = 50000;
            string[] targets = { null, "", "desk.-1", "desk.999999999999999999999.pc", "\0", "not-an-entity" };
            foreach (ActionVerb verb in Enum.GetValues(typeof(ActionVerb)))
                foreach (string target in targets)
                {
                    var request = new ActionRequest(m.Engine.MatchId, sequence++, verb) { TargetId = target, IntParam = int.MaxValue };
                    Assert.DoesNotThrow(() => m.Engine.Submit(0, request, m.Now), verb + " with malformed target");
                }
            Assert.DoesNotThrow(() => m.Engine.Submit(int.MaxValue, new ActionRequest(m.Engine.MatchId, sequence++, (ActionVerb)255), m.Now));
            Assert.IsFalse(m.Engine.Submit(0, new ActionRequest(m.Engine.MatchId, sequence++, (ActionVerb)255), m.Now).Accepted);
        }

        [Test]
        public void MeetingCancelsWork2InteractionDropsCarryAndRejectsOfficeVerbs()
        {
            var m = new TestMatch(configure: c => c.Tasks.WriteReportSeconds = 40); m.Work();
            m.Phase(MatchPhase.Work2);
            m.Do(0, ActionVerb.PickUp, EntityIds.Mug(0));
            m.AdvanceTo(m.View().PhaseEndTime - 0.5);
            m.Start(0, ActionVerb.WriteReport, EntityIds.DeskComputer(0));
            m.Phase(MatchPhase.FinalInspection);
            Assert.IsNotNull(m.View().Me.ActiveInteraction, "Office phase transitions preserve interactions");
            m.Phase(MatchPhase.MeetingDiscussion);
            Assert.IsNull(m.View().Me.ActiveInteraction);
            Assert.IsEmpty(m.Player(0).CarryingItemId);
            Assert.AreEqual(ItemLocationKind.Floor, m.Item(EntityIds.Mug(0)).Location);
            Assert.AreEqual(FailureCode.WrongPhase, m.Request(0, ActionVerb.WriteReport, EntityIds.DeskComputer(0)).Failure);
            m.Finish();
            Assert.IsFalse(m.Events.Any(e => e.Type == EventType.ReportPrinted));
            Assert.IsTrue(m.Events.Any(e => e.Type == EventType.ItemDropped && e.Actor == 0));
        }

        [Test]
        public void ZeroLengthPhasesDrainAtTheExactHostTimestamp()
        {
            var m = new TestMatch(configure: c =>
            {
                c.Durations.Briefing = 0;
                c.Durations.Work1 = 0;
                c.Durations.Raid = 0;
                c.Durations.RaidWarning = 0;
                c.Durations.Work2 = 0;
                c.Durations.FinalInspection = 0;
                c.Durations.FinalWarning = 0;
                c.Durations.MeetingDiscussion = 0;
                c.Durations.MeetingDefense = 0;
                c.Durations.MeetingVote = 0;
                c.Durations.MeetingVerdict = 0;
                c.Durations.Results = 0;
            });
            m.Engine.Tick(m.Now);
            m.Capture();
            Assert.AreEqual(MatchPhase.Results, m.Engine.Phase);
            Assert.IsTrue(m.View().Quota.Frozen);
            Assert.IsNotNull(m.View().Result);
            Assert.AreEqual(11, m.Events.Count(e => e.Type == EventType.PhaseChanged));
            Assert.IsTrue(m.Events.Where(e => e.Type == EventType.PhaseChanged).All(e => e.Time == m.Now));
            string hash = m.View().Result.ResultHash;
            m.Engine.Tick(m.Now);
            m.Engine.Tick(m.Now + 100);
            Assert.AreEqual(MatchPhase.Results, m.Engine.Phase);
            Assert.AreEqual(hash, m.View().Result.ResultHash);
        }

        [Test]
        public void ZeroLengthIntermediatePhasesReachTheNextFutureDeadline()
        {
            var m = new TestMatch(configure: c =>
            {
                c.Durations.Briefing = 0;
                c.Durations.Work1 = 0;
                c.Durations.Raid = 0;
                c.Durations.Work2 = 2;
                c.Durations.FinalInspection = 0;
                c.Tasks.WriteReportSeconds = 1;
            });
            m.Engine.Tick(0);
            Assert.AreEqual(MatchPhase.Work2, m.Engine.Phase);
            Assert.AreEqual(2, m.View().PhaseEndTime);
            m.Start(0, ActionVerb.WriteReport, EntityIds.DeskComputer(0));
            m.Engine.Tick(2);
            m.Capture();
            Assert.AreEqual(MatchPhase.MeetingDiscussion, m.Engine.Phase);
            Assert.AreEqual(2, m.View().PhaseStartTime);
            Assert.IsTrue(m.View().Quota.Frozen);
            Assert.IsTrue(m.Events.Any(e => e.Type == EventType.InteractionCompleted && e.IntArg == (int)ActionVerb.WriteReport && e.Time == 1));
            m.Engine.Tick(100);
            m.Capture();
            Assert.AreEqual(MatchPhase.Results, m.Engine.Phase);
            Assert.IsFalse(m.Events.Any(e => e.Type == EventType.ReportPrinted), "Printer cannot finish queued work after the freeze");
        }

        [Test]
        public void CoarseTickCompletesEarlierWorkButCancelsLaterWorkAtFreeze()
        {
            var m = new TestMatch(configure: c =>
            {
                c.Durations.Work1 = 2;
                c.Durations.Raid = 1;
                c.Durations.Work2 = 2;
                c.Durations.FinalInspection = 1;
                c.Tasks.BrewSeconds = 0.5f;
                c.Tasks.WriteReportSeconds = 15;
            }); m.Work();
            m.Do(0, ActionVerb.PickUp, EntityIds.Mug(0));
            m.Do(0, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine);
            ActionResult delivery = m.Start(0, ActionVerb.Place, EntityIds.DeliveryTray);
            m.Start(1, ActionVerb.WriteReport, EntityIds.DeskComputer(1));
            double freeze = m.Config.Durations.Briefing + m.Config.Durations.Work1 + m.Config.Durations.Raid + m.Config.Durations.Work2 + m.Config.Durations.FinalInspection;
            m.Engine.Tick(100);
            m.Capture();
            Assert.AreEqual(MatchPhase.Results, m.Engine.Phase);
            Assert.AreEqual(m.Config.Tasks.CoffeePoints, m.View().Result.QuotaPoints);
            Assert.IsTrue(m.Events.Any(e => e.Type == EventType.TaskCompleted && e.Actor == 0 && Math.Abs(e.Time - delivery.CompletesAt) < 0.00001));
            Assert.IsTrue(m.Events.Any(e => e.Type == EventType.InteractionCancelled && e.AudiencePlayer == 1 && e.Time == freeze));
            Assert.IsFalse(m.Events.Any(e => e.Type == EventType.ReportPrinted));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void NoNominationsNoVotesSplitAndAbstentionsResolveDeterministically(int ballotMode)
        {
            TestMatch a = EmptyVerdictMatch(88, ballotMode);
            TestMatch b = EmptyVerdictMatch(88, ballotMode);
            Assert.AreEqual(2, a.View().Result.Verdict.Candidates.Count);
            Assert.AreEqual(a.View().Result.FiredPlayer, b.View().Result.FiredPlayer);
            Assert.AreEqual(a.View().Result.ResultHash, b.View().Result.ResultHash);
            Assert.AreEqual(TieBreak.SeededDraw, a.View().Result.Verdict.TieBreakUsed);
            if (ballotMode != 1)
                Assert.IsTrue(a.View().Result.Verdict.Candidates.All(c => c.VoteShare == 0));
        }

        private static TestMatch EmptyVerdictMatch(int seed, int ballotMode)
        {
            var m = new TestMatch(seed, configure: c => c.Tasks.InitialDirtyDesks = 0,
                arrange: l => l.ManagerRoute.Clear());
            m.Phase(MatchPhase.MeetingVote);
            List<int> shortlist = m.View().Meeting.Shortlist;
            Assert.AreEqual(2, shortlist.Distinct().Count());
            if (ballotMode == 1)
            {
                m.Do(shortlist[1], ActionVerb.CastBallot, param: shortlist[0]);
                m.Do(shortlist[0], ActionVerb.CastBallot, param: shortlist[1]);
            }
            if (ballotMode == 2)
                for (int p = 0; p < 4; p++) m.Do(p, ActionVerb.CastBallot, param: PlayerIds.None);
            m.Finish();
            return m;
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ExactScoreTiesUseRiskThenPerformance(bool riskTieBreak)
        {
            var m = new TestMatch(configure: c =>
            {
                c.Verdict.VoteWeight = 0;
                c.Verdict.RiskWeight = 0;
                c.Verdict.PerformanceWeight = 0;
            }, arrange: l => { if (!riskTieBreak) l.ManagerRoute.Clear(); });
            m.Work();
            if (riskTieBreak) m.Plant(2, 1, drawer: false);
            else m.Coffee(0, 0);
            m.Phase(MatchPhase.MeetingDiscussion);
            m.Do(0, ActionVerb.Accuse, param: 1);
            m.Do(2, ActionVerb.Accuse, param: 0);
            m.Do(3, ActionVerb.Accuse, param: 0);
            m.Finish();
            VerdictView verdict = m.View().Result.Verdict;
            Assert.AreEqual(1, verdict.FiredPlayer);
            Assert.AreEqual(riskTieBreak ? TieBreak.ObservedRisk : TieBreak.Performance, verdict.TieBreakUsed);
            Assert.IsTrue(verdict.Candidates.All(c => c.Score == 0));
        }

        [Test]
        public void VerdictFormulaCountsCastAbstentionsAndFalsePenaltyOccursAfterVerdict()
        {
            var m = new TestMatch(arrange: l => l.ManagerRoute.Clear()); m.Work();
            m.Coffee(0, 0);
            m.Phase(MatchPhase.MeetingDiscussion);
            m.Do(0, ActionVerb.Accuse, param: 1);
            m.Do(2, ActionVerb.Accuse, param: 0);
            m.Do(3, ActionVerb.Accuse, param: 0);
            m.Phase(MatchPhase.MeetingVote);
            m.Do(0, ActionVerb.CastBallot, param: 1);
            m.Do(1, ActionVerb.CastBallot, param: 0);
            m.Do(2, ActionVerb.CastBallot, param: PlayerIds.None);
            m.Do(3, ActionVerb.CastBallot, param: 1);
            m.Phase(MatchPhase.MeetingVerdict);
            var verdict = m.View().Meeting.Verdict;
            Assert.AreEqual(1, m.Player(0).Performance);
            Assert.AreEqual(1, verdict.Candidates.Single(c => c.Player == 0).Performance);
            CandidateScore candidate = verdict.Candidates.Single(c => c.Player == 1);
            Assert.AreEqual(0.5f, candidate.VoteShare, 0.00001f);
            Assert.AreEqual(0.25f, candidate.PerformanceDeficit, 0.00001f);
            Assert.AreEqual(0.45f * 0.5f + 0.20f * 0.25f, candidate.Score, 0.00001f);
            m.Finish();
            PlayerOutcome outcome = m.View().Result.Players.Single(p => p.Player == 0);
            Assert.AreEqual(1, outcome.FalseAccusationPenalty);
            Assert.AreEqual(0, outcome.FinalPerformance);
            Assert.AreEqual(1, m.View().Result.Verdict.Candidates.Single(c => c.Player == 0).Performance);
        }

        [Test]
        public void SingleNominationGetsFallbackAndBallotsRemainPrivateUntilVerdict()
        {
            var m = new TestMatch(); m.Phase(MatchPhase.MeetingDiscussion);
            m.Do(0, ActionVerb.Accuse, param: 1);
            Assert.AreEqual(FailureCode.AlreadyUsed, m.Request(0, ActionVerb.Accuse, param: 2).Failure);
            m.Phase(MatchPhase.MeetingDefense1);
            Assert.AreEqual(1, m.View().Meeting.Shortlist[0]);
            Assert.AreEqual(2, m.View().Meeting.Shortlist.Count);
            Assert.AreEqual(1, m.View().Meeting.Speaker);
            int fallback = m.View().Meeting.Shortlist[1];
            m.Phase(MatchPhase.MeetingDefense2);
            Assert.AreEqual(fallback, m.View().Meeting.Speaker);
            m.Phase(MatchPhase.MeetingVote);
            Assert.AreEqual(FailureCode.CannotTargetSelf, m.Request(1, ActionVerb.CastBallot, param: 1).Failure);
            int outsider = Enumerable.Range(0, 4).First(p => p != 0 && p != 1 && p != fallback);
            Assert.AreEqual(FailureCode.NotCandidate, m.Request(0, ActionVerb.CastBallot, param: outsider).Failure);
            m.Do(0, ActionVerb.CastBallot, param: 1);
            Assert.IsTrue(m.View().Me.HasVoted);
            Assert.AreEqual(1, m.View().Me.Ballot);
            Assert.IsFalse(m.View(2).Me.HasVoted);
            Assert.AreEqual(PlayerIds.None, m.View(2).Me.Ballot);
            Assert.IsNull(m.View().Meeting.Verdict);
            Assert.AreEqual(1, m.View(2).Meeting.BallotsCast);
            Assert.AreEqual(-1, m.Events.Last(e => e.Type == EventType.BallotCast).IntArg);
        }

        [Test]
        public void CompanyFailurePreventsEveryVictoryButStillFiresOnePlayer()
        {
            var m = new TestMatch(); m.Finish();
            MatchResultView result = m.View().Result;
            Assert.IsFalse(result.CompanySucceeded);
            Assert.IsFalse(result.InformantWon);
            Assert.AreEqual(1, result.Players.Count(p => p.Fired));
            Assert.IsTrue(result.Players.All(p => !p.Won));
            Assert.IsTrue(result.Players.All(p => p.ExplanationKey == "outcome.company_failed"));
        }

        [TestCase("DISMISSAL_PLAN")]
        [TestCase("PROTECTION_DEAL")]
        [TestCase("QUIET_STAR")]
        [TestCase("CLEAN_HANDS")]
        [TestCase("FIXER")]
        [TestCase("DESK_GUARDIAN")]
        [TestCase("UNNOTICED")]
        [TestCase("PERFORMANCE_RIVAL")]
        [TestCase("UNDER_PRESSURE")]
        public void ObjectivePredicatesApplyAfterCompanyAndSurvivalGates(string card)
        {
            MatchSetup setup = TestMatch.Setup();
            setup.ForceObjectives = new List<string> { card, card == "FIXER" ? "OFFICE_ALL_ROUNDER" : "FIXER", "SCAPEGOAT", "" };
            setup.ForceTargets = new List<int> { card == "PROTECTION_DEAL" ? 2 : 1, -1, 0, -1 };
            var m = new TestMatch(setup: setup, configure: c => c.QuotaTarget = 0,
                arrange: l => l.ManagerRoute.Clear()); m.Work();
            if (card == "CLEAN_HANDS")
            {
                m.Coffee(0, 0); m.Coffee(0, 1); m.Coffee(0, 2);
                string report = m.Print(0);
                m.Do(0, ActionVerb.PickUp, report);
                m.Do(0, ActionVerb.Place, EntityIds.DeliveryTray);
            }
            if (card == "FIXER")
            {
                m.Do(2, ActionVerb.LockWorkstation, EntityIds.DeskComputer(0));
                m.Do(0, ActionVerb.UnlockWorkstation, EntityIds.DeskComputer(0));
                m.Plant(2, 1, drawer: false);
                m.Do(0, ActionVerb.PickUp, EntityIds.Suspicious(ItemVariants.Duck));
            }
            if (card == "PERFORMANCE_RIVAL") m.Coffee(0, 0);
            if (card == "UNDER_PRESSURE")
            {
                m.Phase(MatchPhase.Work2);
                m.Coffee(0, 0); m.Coffee(0, 1);
            }
            m.Phase(MatchPhase.MeetingDiscussion);
            m.Do(2, ActionVerb.Accuse, param: 1);
            m.Do(3, ActionVerb.Accuse, param: 1);
            m.Phase(MatchPhase.MeetingVote);
            m.Do(0, ActionVerb.CastBallot, param: 1);
            m.Do(2, ActionVerb.CastBallot, param: 1);
            m.Do(3, ActionVerb.CastBallot, param: 1);
            m.Finish();
            Assert.AreEqual(1, m.View().Result.FiredPlayer);
            PlayerOutcome employee = m.View().Result.Players.Single(p => p.Player == 0);
            Assert.IsTrue(employee.ObjectiveCompleted, card);
            Assert.IsTrue(employee.Won, card);
            Assert.AreEqual(ObjectiveStatus.Completed, m.View().Me.Objective.Status);
        }

        [TestCase("CHAOS_COORDINATOR")]
        [TestCase("SCAPEGOAT")]
        public void DiscoveryObjectivesCountDistinctIncidentsAndDesks(string card)
        {
            MatchSetup setup = TestMatch.Setup();
            setup.ForceObjectives = new List<string> { card, "FIXER", "UNNOTICED", "" };
            setup.ForceTargets = new List<int> { 1, -1, -1, -1 };
            var m = new TestMatch(setup: setup, configure: c => c.QuotaTarget = 0); m.Work();
            m.Plant(0, 1, ItemVariants.Duck, false);
            if (card == "SCAPEGOAT")
            {
                m.SwapInto(0, 1, 2);
                m.Do(0, ActionVerb.Drop);
            }
            m.Advance(26);
            m.Plant(0, 2, ItemVariants.Stapler, false);
            if (card == "CHAOS_COORDINATOR")
            {
                m.Advance(26);
                m.Plant(0, 3, ItemVariants.RivalCv, false);
            }
            m.Phase(MatchPhase.Raid); m.Advance(10);
            Assert.GreaterOrEqual(m.View().Me.Objective.Progress, card == "CHAOS_COORDINATOR" ? 3 : 2);
            m.Phase(MatchPhase.MeetingDiscussion);
            m.Do(2, ActionVerb.Accuse, param: 1);
            m.Do(3, ActionVerb.Accuse, param: 1);
            m.Phase(MatchPhase.MeetingVote);
            m.Do(0, ActionVerb.CastBallot, param: 1);
            m.Do(2, ActionVerb.CastBallot, param: 1);
            m.Do(3, ActionVerb.CastBallot, param: 1);
            m.Finish();
            Assert.IsTrue(m.View().Result.Players.Single(p => p.Player == 0).ObjectiveCompleted, card);
        }

        [Test]
        public void InformantRequiresPositiveTopMisconductAndSurvival()
        {
            var clean = new TestMatch(configure: c => c.QuotaTarget = 0); clean.Finish();
            Assert.IsEmpty(clean.View().Result.TopMisconductTier);
            Assert.IsFalse(clean.View().Result.InformantWon);
            var m = new TestMatch(configure: c => c.QuotaTarget = 0,
                arrange: l => l.ManagerRoute.Clear()); m.Work();
            m.Do(2, ActionVerb.LockWorkstation, EntityIds.DeskComputer(0));
            m.Phase(MatchPhase.MeetingDiscussion);
            m.Do(0, ActionVerb.Accuse, param: 2);
            m.Do(1, ActionVerb.Accuse, param: 2);
            m.Do(3, ActionVerb.Accuse, param: 2);
            m.Phase(MatchPhase.MeetingVote);
            m.Do(0, ActionVerb.CastBallot, param: 2);
            m.Do(1, ActionVerb.CastBallot, param: 2);
            m.Do(3, ActionVerb.CastBallot, param: 2);
            m.Finish();
            Assert.AreEqual(2, m.View().Result.FiredPlayer);
            Assert.AreEqual(new[] { 2 }, m.View().Result.TopMisconductTier);
            Assert.IsTrue(m.View().Result.InformantWon);
            Assert.IsTrue(m.View().Result.Players.Single(p => p.Player == 3).Won);
        }

        [Test]
        public void ReconnectPreservesPrivateStateAndHistoryWhileDroppingCarry()
        {
            var m = new TestMatch(); m.Work();
            m.Do(3, ActionVerb.AuditScanItem, EntityIds.Nameplate(0));
            m.Do(3, ActionVerb.PickUp, EntityIds.Mug(0));
            m.Start(3, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine);
            PlayerView before = m.View(3);
            SessionHistory history = m.Engine.BuildNextHistory();
            m.Engine.SetConnected(3, false, m.Now);
            Assert.IsNull(m.View(3).Me.ActiveInteraction);
            Assert.AreEqual(ItemLocationKind.Floor, m.Item(EntityIds.Mug(0)).Location);
            m.Advance(29);
            m.Engine.SetConnected(3, true, m.Now);
            PlayerView after = m.View(3);
            Assert.AreEqual(before.Me.Role, after.Me.Role);
            Assert.AreEqual(before.Me.Objective.CardId, after.Me.Objective.CardId);
            Assert.AreEqual(before.Me.Objective.Target, after.Me.Objective.Target);
            Assert.AreEqual(before.Me.Informant.ScansLeft, after.Me.Informant.ScansLeft);
            Assert.AreEqual(before.Me.Notes.Single().Id, after.Me.Notes.Single().Id);
            Assert.AreEqual(before.Me.ReconnectToken, after.Me.ReconnectToken);
            Assert.AreEqual(history.MatchesPlayed, m.Engine.BuildNextHistory().MatchesPlayed);
            Assert.IsTrue(m.Player(3).Connected);
            Assert.AreNotEqual(MatchPhase.Abandoned, m.Engine.Phase);
        }

        [Test]
        public void GraceExpiryAndHostLossAbandonWithoutWinners()
        {
            var m = new TestMatch(); m.Work();
            m.Engine.SetConnected(1, false, m.Now);
            m.Advance(m.Config.Reconnect.GraceSeconds + 0.01);
            Assert.AreEqual(MatchPhase.Abandoned, m.Engine.Phase);
            Assert.AreEqual(AbandonReason.PlayerTimedOut, m.View().AbandonReason);
            Assert.IsNull(m.View().Result);
            Assert.IsFalse(m.Request(0, ActionVerb.CancelInteraction).Accepted);
            m.Engine.SetConnected(1, true, m.Now);
            Assert.AreEqual(MatchPhase.Abandoned, m.Engine.Phase);
            var host = new TestMatch(); host.Work();
            host.Engine.Abandon(AbandonReason.HostLeft, host.Now);
            Assert.AreEqual(MatchPhase.Abandoned, host.Engine.Phase);
            Assert.AreEqual(AbandonReason.HostLeft, host.View().AbandonReason);
            Assert.IsNull(host.Engine.Result);
        }

        [Test]
        public void AllRecipientsHaveIdenticalVerdictAndResultHashAndViewsAreDetached()
        {
            var m = new TestMatch(); m.Finish();
            MatchResultView expected = m.View().Result;
            Assert.IsNotEmpty(expected.ResultHash);
            Assert.AreEqual(WireCodec.HashResult(expected), expected.ResultHash);
            for (int p = 0; p < 4; p++)
            {
                MatchResultView result = m.View(p).Result;
                Assert.AreEqual(expected.ResultHash, result.ResultHash);
                Assert.AreEqual(expected.FiredPlayer, result.Verdict.FiredPlayer);
                Assert.AreEqual(WireCodec.HashResult(result), result.ResultHash);
            }
            m.View().Players[0].Performance = 9999;
            m.View().Result.Players.Clear();
            Assert.AreEqual(4, m.View(1).Result.Players.Count);
            Assert.AreNotEqual(9999, m.Player(0).Performance);
            Assert.IsFalse(m.Engine.AllRematchVotesIn);
            for (int p = 0; p < 4; p++) m.Do(p, ActionVerb.RematchVote);
            Assert.IsTrue(m.Engine.AllRematchVotesIn);
            Assert.AreEqual(FailureCode.AlreadyUsed, m.Request(0, ActionVerb.RematchVote).Failure);
        }

        [Test]
        public void AntiGriefChecksActorCooldownThenSameTargetWindow()
        {
            var m = new TestMatch(); m.Work();
            m.Plant(2, 0, drawer: false);
            m.Do(0, ActionVerb.PickUp, EntityIds.Suspicious(ItemVariants.Duck));
            m.Do(0, ActionVerb.Drop);
            m.Do(2, ActionVerb.PickUp, EntityIds.Suspicious(ItemVariants.Stapler));
            m.MoveTo(2, EntityIds.DeskSurface(0));
            Assert.AreEqual(FailureCode.Cooldown, m.Request(2, ActionVerb.Place, EntityIds.DeskSurface(0)).Failure);
            m.Advance(26);
            Assert.AreEqual(FailureCode.TargetImmune, m.Request(2, ActionVerb.Place, EntityIds.DeskSurface(0)).Failure);
            m.Advance(35);
            m.Do(2, ActionVerb.Place, EntityIds.DeskSurface(0));
            Assert.AreEqual(2, m.Engine.BuildHostDebugView().Incidents.Count(i => i.Type == SabotageType.PlantItem));
        }

        [Test]
        public void AntiGriefEnforcesPerDeskCapAndWorkstationImmunity()
        {
            var m = new TestMatch(configure: c =>
            {
                c.AntiGrief.MaxSuspiciousItemsPerDesk = 1;
                c.AntiGrief.MaxActiveEffectsPerVictim = 6;
            }); m.Work();
            m.Plant(2, 0, drawer: false);
            m.Do(1, ActionVerb.PickUp, EntityIds.Suspicious(ItemVariants.Stapler));
            m.MoveTo(1, EntityIds.DeskSurface(0));
            Assert.AreEqual(FailureCode.LimitReached, m.Request(1, ActionVerb.Place, EntityIds.DeskSurface(0)).Failure);
            m.Do(2, ActionVerb.LockWorkstation, EntityIds.DeskComputer(1));
            m.Do(0, ActionVerb.UnlockWorkstation, EntityIds.DeskComputer(1));
            m.MoveTo(3, EntityIds.DeskComputer(1));
            Assert.AreEqual(FailureCode.TargetImmune, m.Request(3, ActionVerb.LockWorkstation, EntityIds.DeskComputer(1)).Failure);
            m.Advance(m.Config.AntiGrief.WorkstationImmunitySeconds + 0.1);
            m.Do(3, ActionVerb.LockWorkstation, EntityIds.DeskComputer(1));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void AntiGriefBoundsActiveIncidentsPerVictimAndActor(bool victimCap)
        {
            var m = new TestMatch(configure: c =>
            {
                c.AntiGrief.MaxActiveEffectsPerVictim = victimCap ? 1 : 6;
                c.AntiGrief.MaxActiveEffectsPerActor = victimCap ? 6 : 1;
            }); m.Work();
            m.Plant(2, 0, drawer: false);
            int actor = victimCap ? 1 : 2;
            string target = EntityIds.DeskComputer(victimCap ? 0 : 1);
            m.MoveTo(actor, target);
            Assert.AreEqual(victimCap ? FailureCode.TargetImmune : FailureCode.LimitReached,
                m.Request(actor, ActionVerb.LockWorkstation, target).Failure);
            m.Do(0, ActionVerb.PickUp, EntityIds.Suspicious(ItemVariants.Duck));
            m.Do(actor, ActionVerb.LockWorkstation, target);
        }

        [Test]
        public void ExpiredLockStartsImmunityAndStopsCountingAsActiveIncident()
        {
            var m = new TestMatch(); m.Work();
            m.Do(2, ActionVerb.LockWorkstation, EntityIds.DeskComputer(0));
            double expiry = m.View().Desks.Single(d => d.Index == 0).PcLockedUntil;
            m.AdvanceTo(expiry + 0.01);
            Assert.IsFalse(m.View().Desks.Single(d => d.Index == 0).PcLocked);
            m.MoveTo(1, EntityIds.DeskComputer(0));
            Assert.AreEqual(FailureCode.TargetImmune, m.Request(1, ActionVerb.LockWorkstation, EntityIds.DeskComputer(0)).Failure);
            m.Advance(m.Config.AntiGrief.WorkstationImmunitySeconds + 0.1);
            m.Do(1, ActionVerb.LockWorkstation, EntityIds.DeskComputer(0));
        }

        [Test]
        public void MisconductConsequencesAreBoundedAndReversalGoesToOtherActor()
        {
            var m = new TestMatch(); m.Work();
            m.Do(2, ActionVerb.LockWorkstation, EntityIds.DeskComputer(0));
            m.MoveTo(0, EntityIds.DeskComputer(0));
            for (int i = 0; i < 5; i++)
                Assert.AreEqual(FailureCode.StationLocked, m.Request(0, ActionVerb.WriteReport, EntityIds.DeskComputer(0)).Failure);
            SabotageDef definition = m.Config.GetSabotage(SabotageType.LockWorkstation);
            IncidentReveal incident = m.Engine.BuildHostDebugView().Incidents.Single();
            Assert.AreEqual(definition.BaseMisconduct + definition.ConsequenceMisconduct, incident.Misconduct);
            m.Do(1, ActionVerb.UnlockWorkstation, EntityIds.DeskComputer(0));
            incident = m.Engine.BuildHostDebugView().Incidents.Single();
            Assert.AreEqual(2, incident.Actor);
            Assert.AreEqual(1, incident.ReversedBy);
            Assert.AreEqual(1, m.View(1).Me.Objective.Progress);
        }

        [Test]
        public void ManagerHonorsConeLineOfSightAndInspectionPhase()
        {
            var world = new ToggleWorld();
            Vec3 stop = DefaultOffice.Create().GetDesk(0).Position + new Vec3(0, 0, -2);
            var m = new TestMatch(world: world, arrange: l =>
            {
                l.ManagerHome = stop + new Vec3(0, 0, -1);
                l.ManagerRoute.Clear();
                l.ManagerRoute.Add(new ManagerStop { Position = stop, Yaw = 0, DeskIndex = 0, DwellSeconds = 12 });
            });
            m.Work();
            m.Do(0, ActionVerb.PickUp, EntityIds.Suspicious(ItemVariants.Duck));
            m.Move(0, stop + new Vec3(0, 0, 2));
            m.Advance(2);
            Assert.IsEmpty(m.View().Observations);
            m.Phase(MatchPhase.Raid);
            m.Move(0, stop + new Vec3(0, 0, -2));
            m.Advance(2);
            Assert.IsFalse(m.View().Observations.Any(o => o.Kind == ObservationKind.CarryingSuspicious));
            m.Move(0, stop + new Vec3(0, 0, 2));
            world.Visible = false;
            m.Advance(1);
            Assert.IsFalse(m.View().Observations.Any(o => o.Kind == ObservationKind.CarryingSuspicious));
            world.Visible = true;
            m.Advance(1);
            Assert.AreEqual(1, m.View().Observations.Count(o => o.Kind == ObservationKind.CarryingSuspicious && o.Subject == 0));
            m.Advance(1);
            Assert.AreEqual(1, m.View().Observations.Count(o => o.Kind == ObservationKind.CarryingSuspicious && o.Subject == 0));
        }

        [Test]
        public void ManagerOutputDependsOnObservableDeskStateAndNotTruePlanter()
        {
            var innocent = new TestMatch(); innocent.Work();
            innocent.Plant(0, 0);
            var framed = new TestMatch(); framed.Work();
            framed.Plant(2, 0);
            foreach (TestMatch m in new[] { innocent, framed })
            {
                for (int p = 0; p < 4; p++) m.Move(p, new Vec3(50 + p * 10, 0, 50));
                m.Phase(MatchPhase.Raid); m.Advance(10);
            }
            Assert.IsEmpty(innocent.Engine.BuildHostDebugView().Incidents);
            Assert.AreEqual(1, framed.Engine.BuildHostDebugView().Incidents.Count);
            PlayerView first = innocent.View(), second = framed.View();
            var firstVisible = new PlayerView { Evidence = first.Evidence, Observations = first.Observations, Manager = first.Manager };
            var secondVisible = new PlayerView { Evidence = second.Evidence, Observations = second.Observations, Manager = second.Manager };
            Assert.AreEqual(WireCodec.EncodeView(firstVisible), WireCodec.EncodeView(secondVisible));
        }

        [Test]
        public void NameplateConsequenceAppliesForWronglyAttributedObservationWithoutItemsOrMess()
        {
            var m = new TestMatch(configure: c => c.Tasks.InitialDirtyDesks = 0); m.Work();
            m.SwapInto(2, 1, 0); m.Do(2, ActionVerb.Drop);
            for (int p = 0; p < 4; p++) m.Move(p, new Vec3(50 + p * 10, 0, 50));
            m.Phase(MatchPhase.Raid); m.Advance(10);
            Assert.IsTrue(m.View().Observations.Any(o => o.DeskIndex == 0 && o.Subject == 1 && o.Kind == ObservationKind.NotAtDesk));
            Assert.IsFalse(m.View().Evidence.Any(e => e.DeskIndex == 0));
            IncidentReveal incident = m.Engine.BuildHostDebugView().Incidents.Single();
            SabotageDef definition = m.Config.GetSabotage(SabotageType.NameplateSwap);
            Assert.AreEqual(definition.BaseMisconduct + definition.ConsequenceMisconduct, incident.Misconduct);
            Assert.IsTrue(incident.Discovered);
            Assert.AreEqual(1, incident.BlamedPlayer);
            m.Phase(MatchPhase.MeetingDiscussion);
            Assert.AreEqual(incident.Misconduct, m.Engine.BuildHostDebugView().Incidents.Single().Misconduct, "Repeated observations cannot add the consequence again");
        }

        private sealed class ToggleWorld : IWorldQueries
        {
            public bool Visible = true;
            public bool HasLineOfSight(Vec3 from, Vec3 to) { return Visible; }
        }

        [Test]
        public void DeterministicScriptProducesIdenticalEventsAndResultHash()
        {
            TestMatch first = ScriptedMatch();
            TestMatch second = ScriptedMatch();
            Assert.AreEqual(first.View().Result.ResultHash, second.View().Result.ResultHash);
            Assert.AreEqual(WireCodec.EncodeEvents(first.Events), WireCodec.EncodeEvents(second.Events));
        }

        [Test]
        public void FullFourPlayerMatchCompletesEveryV1TaskAndSabotageType()
        {
            var m = ScriptedMatch();
            Assert.AreEqual(MatchPhase.Results, m.Engine.Phase);
            foreach (TaskType type in new[] { TaskType.Coffee, TaskType.Report, TaskType.Phone, TaskType.CleanDesk })
                Assert.IsTrue(m.Events.Any(e => e.Type == EventType.TaskCompleted && e.IntArg == (int)type), type.ToString());
            foreach (SabotageType type in new[] { SabotageType.NameplateSwap, SabotageType.PlantItem, SabotageType.LockWorkstation,
                SabotageType.HideReport, SabotageType.SpillCoffee, SabotageType.MoveClutter, SabotageType.EvidenceTampering })
                Assert.IsTrue(m.View().Result.Incidents.Any(i => i.Type == type), type.ToString());
            Assert.IsTrue(m.View().Result.CompanySucceeded);
            Assert.AreEqual(1, m.View().Result.Players.Count(p => p.Fired));
            Assert.IsTrue(m.View().Result.Players.Single(p => p.Player == 0).ObjectiveCompleted, "All-rounder completed all four task types");
            Assert.LessOrEqual(m.View().Result.Recap.Count, 10);
            Assert.IsTrue(m.View().Result.Players.All(p => p.Titles.Count <= 2));
        }

        private static TestMatch ScriptedMatch()
        {
            var m = new TestMatch(configure: c =>
            {
                c.QuotaTarget = 5;
                // This fixture exercises the four task types present in v1.
                c.Objectives.Single(o => o.Type == ObjectiveType.OfficeAllRounder).Count = 4;
            }); m.Work();
            m.Coffee(0, 0);
            string report = m.Print(0);
            m.Do(2, ActionVerb.PickUp, report);
            m.Do(2, ActionVerb.Place, EntityIds.HideBehindPrinter);
            m.Do(0, ActionVerb.SearchContainer, EntityIds.HideBehindPrinter);
            m.Do(0, ActionVerb.PickUp, report);
            m.Do(0, ActionVerb.Place, EntityIds.DeliveryTray);
            for (int i = 0; !m.View().Quota.PhoneRinging && i < 100; i++) m.Advance(0.25);
            m.Do(0, ActionVerb.AnswerPhone, EntityIds.Phone);
            m.Do(0, ActionVerb.PickUp, EntityIds.Spray(0));
            m.Do(0, ActionVerb.CleanDesk, EntityIds.Desk(0));
            m.Do(0, ActionVerb.Drop);
            m.Plant(2, 0, drawer: false);
            m.SwapInto(1, 1, 0);
            m.Do(1, ActionVerb.Drop);
            m.Do(2, ActionVerb.LockWorkstation, EntityIds.DeskComputer(3));
            m.Do(1, ActionVerb.UnlockWorkstation, EntityIds.DeskComputer(3));
            m.Do(3, ActionVerb.PickUp, EntityIds.Mug(1));
            m.Do(3, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine);
            m.Do(3, ActionVerb.SpillCoffee, EntityIds.Desk(2));
            m.Do(3, ActionVerb.Drop);
            m.Do(1, ActionVerb.PickUp, EntityIds.Clutter(0));
            m.Do(1, ActionVerb.Place, EntityIds.DeskSurface(3));
            m.Phase(MatchPhase.Raid); m.Advance(10);
            EvidenceView duck = m.FindEvidence(EntityIds.Suspicious(ItemVariants.Duck));
            m.Do(3, ActionVerb.AuditScanEvidence, param: duck.Id);
            m.Do(1, ActionVerb.PickUp, EntityIds.Suspicious(ItemVariants.Duck));
            m.Do(1, ActionVerb.Place, EntityIds.JunkBin);
            Assert.AreEqual(EvidenceStatus.Disposed, m.View().Evidence.Single(e => e.Id == duck.Id).Status);
            m.Phase(MatchPhase.MeetingDiscussion);
            m.Do(0, ActionVerb.Accuse, param: 2);
            m.Do(1, ActionVerb.Accuse, param: 2);
            m.Do(3, ActionVerb.Accuse, param: 2);
            m.Do(3, ActionVerb.PresentNote, param: m.View(3).Me.Informant.Scans.Single().NoteId);
            m.Phase(MatchPhase.MeetingVote);
            m.Do(0, ActionVerb.CastBallot, param: 2);
            m.Do(1, ActionVerb.CastBallot, param: 2);
            m.Do(3, ActionVerb.CastBallot, param: 2);
            m.Do(2, ActionVerb.CastBallot, param: PlayerIds.None);
            m.Finish();
            return m;
        }
    }
}
