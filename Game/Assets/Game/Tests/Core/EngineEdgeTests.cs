using System;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class EngineEdgeTests
    {
        [Test]
        public void TimedHiddenPickupDoesNotExposeItemIdThroughPublicActivity()
        {
            string duck = EntityIds.Suspicious(ItemVariants.Duck);
            var match = new TestMatch(arrange: layout => layout.GetItem(duck).InitialSlotId = EntityIds.DeskDrawer(0));
            match.Work();
            match.Do(0, ActionVerb.SearchContainer, EntityIds.DeskDrawer(0));
            match.Start(0, ActionVerb.PickUp, duck);
            var owner = match.View(0);
            var observer = match.View(1);
            Assert.AreEqual(duck, owner.Me.ActiveInteraction.TargetId);
            Assert.IsFalse(observer.Items.Any(item => item.Id == duck));
            Assert.AreEqual(EntityIds.DeskDrawer(0), observer.Players[0].ActivityTargetId);
            Assert.IsFalse(Encoding.UTF8.GetString(WireCodec.EncodeView(observer)).Contains(duck),
                "An item hidden from this recipient must not appear through another snapshot field.");
        }

        [Test]
        public void OwnSuspiciousDrawerPlacementUsesPlantDurationWithoutIncident()
        {
            var match = new TestMatch();
            match.Work();
            match.Do(0, ActionVerb.PickUp, EntityIds.Suspicious(ItemVariants.Duck));
            ActionResult start = match.Start(0, ActionVerb.Place, EntityIds.DeskDrawer(0));
            Assert.AreEqual(match.Config.GetSabotage(SabotageType.PlantItem).DurationSeconds,
                start.CompletesAt - match.Now, 0.00001);
            match.AdvanceTo(start.CompletesAt + 0.0001);
            Assert.AreEqual(0, match.Engine.BuildHostDebugView().Incidents.Count);
        }

        [Test]
        public void RestoringNameplateCreditsOnlyOpenSwapNotReplacedHistoricalSwap()
        {
            var match = new TestMatch();
            match.Work();
            match.SwapInto(2, 2, 0);
            match.Do(2, ActionVerb.Drop);
            match.Do(2, ActionVerb.PickUp, EntityIds.Nameplate(2));
            match.Do(2, ActionVerb.Drop);
            match.SwapInto(1, 1, 0);
            match.Do(0, ActionVerb.PickUp, EntityIds.Nameplate(0));
            match.Do(0, ActionVerb.Place, EntityIds.DeskNameplate(0));
            var swaps = match.Engine.BuildHostDebugView().Incidents.Where(i => i.Type == SabotageType.NameplateSwap).ToList();
            Assert.AreEqual(2, swaps.Count);
            Assert.AreEqual(PlayerIds.None, swaps[0].ReversedBy, "The first swap ended when another wrong plate replaced it.");
            Assert.AreEqual(0, swaps[1].ReversedBy);
        }

        [Test]
        public void TwoReservedDeliveriesRevalidateLastDemandAtCompletion()
        {
            var match = new TestMatch(configure: c => c.Tasks.InitialCoffeeOrders = 1);
            match.Work();
            for (int p = 0; p < 2; p++)
            {
                match.Do(p, ActionVerb.PickUp, EntityIds.Mug(p));
                match.Do(p, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine);
            }
            ActionResult first = match.Start(0, ActionVerb.Place, EntityIds.DeliveryTray);
            ActionResult second = match.Start(1, ActionVerb.Place, EntityIds.DeliveryTray);
            Assert.AreEqual(first.CompletesAt, second.CompletesAt);
            match.AdvanceTo(first.CompletesAt + 0.0001);
            Assert.AreEqual(1, match.View().Quota.Points);
            Assert.AreEqual(0, match.View().Quota.CoffeeOrders);
            Assert.AreEqual(MugFill.Used, match.Item(EntityIds.Mug(0)).Fill);
            Assert.AreEqual(MugFill.Full, match.Item(EntityIds.Mug(1)).Fill);
            Assert.AreEqual(EntityIds.Mug(1), match.Player(1).CarryingItemId);
            Assert.IsTrue(match.Events.Any(e => e.Type == EventType.InteractionCancelled && e.Actor == 1));
        }

        [Test]
        public void PickupReservationAndNameplateSwapCannotStealEachOthersItem()
        {
            var match = new TestMatch();
            match.Work();
            match.Do(1, ActionVerb.PickUp, EntityIds.Nameplate(1));
            ActionResult pickup = match.Start(0, ActionVerb.PickUp, EntityIds.Nameplate(0));
            match.MoveTo(1, EntityIds.DeskNameplate(0));
            Assert.AreEqual(FailureCode.TargetBusy, match.Request(1, ActionVerb.Place, EntityIds.DeskNameplate(0)).Failure);
            match.AdvanceTo(pickup.CompletesAt + 0.0001);
            Assert.AreEqual(EntityIds.Nameplate(0), match.Player(0).CarryingItemId);
            Assert.AreEqual(EntityIds.Nameplate(1), match.Player(1).CarryingItemId);
        }

        [Test]
        public void PhoneRingExpiryDuringValidAnswerDoesNotMissOrDoubleAward()
        {
            var match = new TestMatch(configure: c =>
            {
                c.Tasks.PhoneIntervalMin = c.Tasks.PhoneIntervalMax = 1;
                c.Tasks.PhoneRingSeconds = 2;
                c.Tasks.AnswerPhoneSeconds = 8;
            });
            match.Work();
            match.Advance(1.1);
            var answer = match.Start(0, ActionVerb.AnswerPhone, EntityIds.Phone);
            match.AdvanceTo(answer.CompletesAt + 0.0001);
            Assert.AreEqual(1, match.Player(0).TasksCompleted);
            Assert.AreEqual(1, match.View().Quota.Points);
            Assert.AreEqual(0, match.Events.Count(e => e.Type == EventType.PhoneMissed));
        }
    }
}
