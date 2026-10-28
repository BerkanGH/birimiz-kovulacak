using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class AffordanceTests
    {
        private MatchConfig config;
        private OfficeLayout layout;
        private PlayerView view;

        [SetUp]
        public void SetUp()
        {
            config = MatchConfig.CreateDefault();
            layout = DefaultOffice.Create();
            view = new PlayerView { Recipient = 0, Phase = MatchPhase.Work1, ServerTime = 10 };
            for (int i = 0; i < 4; i++)
            {
                view.Players.Add(new PlayerPublicView { Id = i, DeskIndex = i, Connected = true });
                view.Desks.Add(new DeskView { Index = i, Id = EntityIds.Desk(i), AssignedOwner = i, DisplayedNameplateOf = i });
            }
            for (int i = 0; i < 12; i++) view.Me.SabotageCooldowns.Add(0);
        }

        [Test]
        public void UnknownContainerItemsNeverBecomeFocusTargetsOrTakePrompts()
        {
            string duck = EntityIds.Suspicious(ItemVariants.Duck);
            Assert.IsFalse(Affordances.TargetIds(view, layout).Contains(duck));
            Assert.AreEqual(0, Options(duck).Count);
            var drawer = Options(EntityIds.DeskDrawer(1));
            Assert.AreEqual(1, drawer.Count);
            Assert.AreEqual(ActionVerb.SearchContainer, drawer[0].Verb);

            view.Items.Add(new ItemView { Id = duck, Kind = ItemKind.Suspicious, Location = ItemLocationKind.Slot, SlotId = EntityIds.DeskDrawer(1) });
            Assert.IsTrue(Affordances.TargetIds(view, layout).Contains(duck));
            Assert.IsTrue(Options(EntityIds.DeskDrawer(1)).Any(a => a.Verb == ActionVerb.PickUp && a.TargetId == duck));
        }

        [Test]
        public void TargetsAreStableUniqueAndExcludeArchivedReports()
        {
            view.Items.Add(new ItemView { Id = "floor.item", Location = ItemLocationKind.Floor });
            view.Items.Add(new ItemView { Id = "archived.item", Location = ItemLocationKind.Archived });
            view.Items.Add(new ItemView { Id = "floor.item", Location = ItemLocationKind.Floor });
            List<string> ids = Affordances.TargetIds(view, layout);
            CollectionAssert.AreEqual(ids, Affordances.TargetIds(view, layout));
            Assert.AreEqual(ids.Count, ids.Distinct().Count());
            Assert.IsTrue(ids.Contains("floor.item"));
            Assert.IsFalse(ids.Contains("archived.item"));
            Assert.IsTrue(layout.Desks.All(d => ids.Contains(d.Id)));
        }

        [Test]
        public void LockedOwnPcIncludesBlockedWritingAndAvailableUnlock()
        {
            view.Desks[0].PcLocked = true;
            var options = Options(EntityIds.DeskComputer(0));
            Assert.AreEqual(FailureCode.StationLocked, options.Single(a => a.Verb == ActionVerb.WriteReport).BlockedBy);
            var unlock = options.Single(a => a.Verb == ActionVerb.UnlockWorkstation);
            Assert.AreEqual(FailureCode.None, unlock.BlockedBy);
            Assert.AreEqual("verb.unlock_pc", unlock.LabelKey);
            Assert.IsFalse(unlock.IsSabotage);
        }

        [Test]
        public void DeliverWithoutDemandIsBlockedAndUsedMugCannotBeDelivered()
        {
            Carry(ItemKind.Mug, MugFill.Full);
            var delivery = Options(EntityIds.DeliveryTray).Single();
            Assert.AreEqual(ActionVerb.Place, delivery.Verb);
            Assert.AreEqual("verb.deliver", delivery.LabelKey);
            Assert.AreEqual(FailureCode.NoDemand, delivery.BlockedBy);
            view.Quota.CoffeeOrders = 1;
            Assert.AreEqual(FailureCode.None, Options(EntityIds.DeliveryTray).Single().BlockedBy);
            view.Items[0].Fill = MugFill.Used;
            Assert.AreEqual(FailureCode.WrongItem, Options(EntityIds.DeliveryTray).Single().BlockedBy);
        }

        [Test]
        public void PlantingMarksOnlyOtherAssignedDeskAndShowsActorCooldown()
        {
            Carry(ItemKind.Suspicious);
            var own = Options(EntityIds.DeskDrawer(0)).Single(a => a.Verb == ActionVerb.Place);
            var other = Options(EntityIds.DeskDrawer(1)).Single(a => a.Verb == ActionVerb.Place);
            Assert.IsFalse(own.IsSabotage);
            Assert.IsTrue(other.IsSabotage);
            Assert.AreEqual(config.GetSabotage(SabotageType.PlantItem).DurationSeconds, other.Seconds);
            view.Me.SabotageCooldowns[(int)SabotageType.PlantItem] = view.ServerTime + 20;
            Assert.AreEqual(FailureCode.Cooldown, Options(EntityIds.DeskDrawer(1)).First().BlockedBy);
            Assert.AreEqual(FailureCode.None, Options(EntityIds.DeskDrawer(0)).First().BlockedBy);
        }

        [Test]
        public void NameplateHolderSwapsEvenWhenFullAndRestoringIsNotSabotage()
        {
            Carry(ItemKind.Nameplate);
            view.Items[0].NameplateOf = 0;
            view.Items.Add(new ItemView { Id = "existing", Kind = ItemKind.Nameplate, NameplateOf = 1,
                Location = ItemLocationKind.Slot, SlotId = EntityIds.DeskNameplate(1) });
            var place = Options(EntityIds.DeskNameplate(1)).Single();
            Assert.AreEqual(FailureCode.None, place.BlockedBy);
            Assert.AreEqual("verb.place_nameplate", place.LabelKey);
            Assert.IsTrue(place.IsSabotage);
            view.Items[0].NameplateOf = 1;
            Assert.IsFalse(Options(EntityIds.DeskNameplate(1)).Single().IsSabotage);
        }

        [Test]
        public void EvidenceTamperingIsMarkedAndSealPrecedesHandsFull()
        {
            Carry(ItemKind.Mug);
            view.Items.Add(new ItemView { Id = "evidence.item", Kind = ItemKind.Suspicious, IsEvidence = true,
                Location = ItemLocationKind.Slot, SlotId = EntityIds.DeskSurface(1) });
            view.Evidence.Add(new EvidenceView { ItemId = "evidence.item", LocationId = EntityIds.DeskSurface(1), Status = EvidenceStatus.Active });
            var pickup = Options("evidence.item").Single();
            Assert.IsTrue(pickup.IsSabotage);
            Assert.AreEqual(FailureCode.HandsFull, pickup.BlockedBy);
            view.Items[1].Sealed = true;
            view.Evidence[0].Sealed = true;
            pickup = Options("evidence.item").Single();
            Assert.AreEqual(FailureCode.Sealed, pickup.BlockedBy);
            Assert.IsFalse(pickup.IsSabotage);
        }

        [Test]
        public void InformantScanIsAllowedWhileBusyButChargesAreVisibleOnlyToInformant()
        {
            view.Items.Add(new ItemView { Id = "duck", Kind = ItemKind.Suspicious, Location = ItemLocationKind.Floor });
            Assert.IsFalse(Options("duck").Any(a => a.Verb == ActionVerb.AuditScanItem));
            view.Me.Role = Role.Informant;
            view.Me.Informant = new InformantView { ScansLeft = 1 };
            view.Me.ActiveInteraction = new InteractionView { Verb = ActionVerb.WriteReport };
            var options = Options("duck");
            Assert.AreEqual(FailureCode.Busy, options.Single(a => a.Verb == ActionVerb.PickUp).BlockedBy);
            Assert.AreEqual(FailureCode.None, options.Single(a => a.Verb == ActionVerb.AuditScanItem).BlockedBy);
            view.Me.Informant.ScansLeft = 0;
            Assert.AreEqual(FailureCode.NoCharges, Options("duck").Single(a => a.Verb == ActionVerb.AuditScanItem).BlockedBy);
        }

        [TestCase(0)]
        [TestCase(2)]
        public void CarriedItemScanDoesNotUseStaleFloorPositionForRange(int carrier)
        {
            view.Me.Role = Role.Informant;
            view.Me.Informant = new InformantView { ScansLeft = 1 };
            view.Items.Add(new ItemView { Id = "carried.duck", Kind = ItemKind.Suspicious,
                Location = ItemLocationKind.Carried, CarrierId = carrier, FloorPosition = Vec3.Zero });
            var options = Affordances.For(view, config, layout, "carried.duck", new Vec3(50, 0, 50));
            Assert.AreEqual(FailureCode.None, options.Single(a => a.Verb == ActionVerb.AuditScanItem).BlockedBy);
        }

        [Test]
        public void DisposingMovedEvidenceStillShowsTamperingWarning()
        {
            Carry(ItemKind.Suspicious);
            view.Evidence.Add(new EvidenceView { ItemId = "held", Status = EvidenceStatus.Moved });
            Assert.IsTrue(Options(EntityIds.JunkBin).Single().IsSabotage);
            view.Evidence[0].Status = EvidenceStatus.Disposed;
            Assert.IsFalse(Options(EntityIds.JunkBin).Single().IsSabotage);
        }

        [Test]
        public void ValidationHintsPrioritizeConnectionThenPhaseThenBusy()
        {
            view.Desks[0].PcLocked = true;
            view.Me.ActiveInteraction = new InteractionView();
            Assert.AreEqual(FailureCode.Busy, Options(EntityIds.DeskComputer(0)).First().BlockedBy);
            view.Phase = MatchPhase.MeetingDiscussion;
            Assert.AreEqual(FailureCode.WrongPhase, Options(EntityIds.DeskComputer(0)).First().BlockedBy);
            view.Players[0].Connected = false;
            Assert.AreEqual(FailureCode.NotConnected, Options(EntityIds.DeskComputer(0)).First().BlockedBy);
        }

        [Test]
        public void RangeHintsUseHorizontalDistanceAndApplyAfterEquipmentChecks()
        {
            var station = layout.GetStation(EntityIds.CoffeeMachine);
            Carry(ItemKind.Mug);
            var highAbove = station.Position + new Vec3(0, 1000, 0);
            Assert.AreEqual(FailureCode.None, Affordances.For(view, config, layout, station.Id, highAbove).Single().BlockedBy);
            var far = station.Position + new Vec3(100, 0, 0);
            Assert.AreEqual(FailureCode.TooFar, Affordances.For(view, config, layout, station.Id, far).Single().BlockedBy);
            view.Players[0].CarryingItemId = "";
            Assert.AreEqual(FailureCode.HandsEmpty, Affordances.For(view, config, layout, station.Id, far).Single().BlockedBy);
        }

        [Test]
        public void OtherPlayersReportHiddenInContainerIsSabotageButOwnReportIsNot()
        {
            Carry(ItemKind.Report);
            view.Items[0].ReportOwner = 1;
            var hide = Options(EntityIds.HideInPlant).First();
            Assert.AreEqual("verb.hide", hide.LabelKey);
            Assert.IsTrue(hide.IsSabotage);
            Assert.AreEqual(config.GetSabotage(SabotageType.HideReport).DurationSeconds, hide.Seconds);
            view.Items[0].ReportOwner = 0;
            Assert.IsFalse(Options(EntityIds.HideInPlant).First().IsSabotage);
        }

        [Test]
        public void DeskPromptsOfferCleaningSpillsAndVisibleClutterCaps()
        {
            Carry(ItemKind.Spray);
            view.Desks[1].Dirty = true;
            view.Desks[1].Spill = true;
            var clean = Options(EntityIds.Desk(1));
            Assert.AreEqual(FailureCode.None, clean.Single(a => a.Verb == ActionVerb.CleanDesk).BlockedBy);
            Assert.AreEqual(FailureCode.None, clean.Single(a => a.Verb == ActionVerb.CleanSpill).BlockedBy);
            view.Items[0].Kind = ItemKind.Clutter;
            view.Desks[1].ClutterCount = config.AntiGrief.MaxClutterPerDesk;
            Assert.AreEqual(FailureCode.LimitReached, Options(EntityIds.DeskSurface(1)).Single().BlockedBy);
        }

        private void Carry(ItemKind kind, MugFill fill = MugFill.Empty)
        {
            view.Players[0].CarryingItemId = "held";
            view.Items.Add(new ItemView { Id = "held", Kind = kind, Fill = fill, Location = ItemLocationKind.Carried, CarrierId = 0 });
        }

        private List<Affordance> Options(string target)
        {
            Vec3 position = Vec3.Zero;
            var slot = layout.GetSlot(target);
            var station = layout.GetStation(target);
            var desk = layout.GetDesk(target);
            var item = view.Items.FirstOrDefault(i => i.Id == target);
            if (slot != null) position = slot.Position;
            else if (station != null) position = station.Position;
            else if (desk != null) position = desk.Position;
            else if (item != null) position = item.Location == ItemLocationKind.Slot ? layout.GetSlot(item.SlotId).Position : item.FloorPosition;
            return Affordances.For(view, config, layout, target, position);
        }
    }
}
