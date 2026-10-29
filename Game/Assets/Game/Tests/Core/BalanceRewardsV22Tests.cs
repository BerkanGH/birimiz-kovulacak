using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class BalanceRewardsV22Tests
    {
        [TestCase(FavorSource.ReportsDelivered, 0)]
        [TestCase(FavorSource.ReportsDelivered, 1)]
        [TestCase(FavorSource.AnyTask, 0)]
        [TestCase(FavorSource.AnyTask, 1)]
        public void ReportDeliveryCreditsWriterPerformanceAndFavorExactlyOnce(FavorSource source, int deliverer)
        {
            var m = new TestMatch(configure: c =>
            {
                c.QuotaTarget = 1;
                c.ManagerProfiles[0].FavorSource = source;
                c.ManagerProfiles[0].FavorPerAction = 2;
            });
            m.Work(); string report = m.Print(0);
            m.Do(deliverer, ActionVerb.PickUp, report);
            m.Do(deliverer, ActionVerb.Place, EntityIds.DeliveryTray);
            Assert.AreEqual(m.Config.Tasks.ReportPoints, m.View().Quota.Points);
            Assert.AreEqual(m.Config.Tasks.ReportPoints, m.Player(0).Performance);
            Assert.AreEqual(2, m.Player(0).Favor);
            Assert.AreEqual(0, m.Player(1).Performance);
            Assert.AreEqual(0, m.Player(1).Favor);
            Assert.AreEqual(1, m.Player(deliverer).TasksCompleted);
            Assert.AreEqual(0, m.Player(1 - deliverer).TasksCompleted);
            Assert.AreEqual(2, m.View().Quota.ReportOrders);
            Assert.IsNull(m.Item(report));
            Assert.AreEqual(deliverer, m.Events.Single(e => e.Type == EventType.TaskCompleted).Actor);
            Assert.AreEqual(0, m.Events.Single(e => e.Type == EventType.FavorChanged).Actor);
            Assert.AreEqual(1, m.Events.Count(e => e.Type == EventType.QuotaReached));
            Assert.AreEqual(FailureCode.HandsEmpty, m.Request(deliverer, ActionVerb.Place, EntityIds.DeliveryTray).Failure);
            Assert.AreEqual(m.Config.Tasks.ReportPoints, m.View().Quota.Points);
        }

        [TestCase("CLEAN_HANDS")]
        [TestCase("OFFICE_ALL_ROUNDER")]
        [TestCase("UNDER_PRESSURE")]
        public void DeliveringAnotherWritersReportStillCountsForDeliveryTaskObjectives(string objective)
        {
            var setup = TestMatch.Setup();
            setup.ForceObjectives = new List<string> { "QUIET_STAR", objective, "SCAPEGOAT", "" };
            var m = new TestMatch(setup: setup, configure: c =>
            {
                ObjectiveDef card = c.Objectives.Single(o => o.Id == objective);
                card.Count = 1; card.Count2 = 1;
            });
            m.Phase(MatchPhase.Work2);
            string report = m.Print(0); m.Do(1, ActionVerb.PickUp, report); m.Do(1, ActionVerb.Place, EntityIds.DeliveryTray);
            Assert.AreEqual(1, m.View(1).Me.Objective.Progress);
            Assert.AreEqual(ObjectiveStatus.OnTrack, m.View(1).Me.Objective.Status);
            Assert.AreEqual(1, m.Player(1).TasksCompleted);
            Assert.AreEqual(0, m.Player(1).Performance);
            Assert.AreEqual(0, m.Player(0).TasksCompleted);
            Assert.AreEqual(m.Config.Tasks.ReportPoints, m.Player(0).Performance);
            Assert.AreEqual(ObjectiveStatus.OnTrack, m.View(0).Me.Objective.Status, "The writer retains performance-based objective credit.");
            if (objective == "CLEAN_HANDS") Assert.AreEqual(1, m.View(1).Me.Objective.Progress2);
            if (objective == "OFFICE_ALL_ROUNDER") CollectionAssert.Contains(m.View(1).Me.Objective.Flags, "report");
        }

        [TestCase(1)]
        [TestCase(4)]
        [TestCase(5)]
        public void AllRounderUsesConfiguredCountInProgressAndFinalOutcome(int count)
        {
            var m = new TestMatch(configure: c =>
            {
                c.QuotaTarget = 0;
                c.Objectives.Single(o => o.Type == ObjectiveType.OfficeAllRounder).Count = count;
            }, arrange: l => l.ManagerRoute.Clear());
            m.Work(); m.Coffee(0, 0);
            Assert.AreEqual(count, m.View().Me.Objective.Required);
            Assert.AreEqual(count == 1 ? ObjectiveStatus.OnTrack : ObjectiveStatus.InProgress, m.View().Me.Objective.Status);
            m.Finish();
            Assert.AreEqual(count == 1, m.View().Result.Players.Single(p => p.Player == 0).ObjectiveCompleted);
            Assert.AreEqual(count == 1 ? ObjectiveStatus.Completed : ObjectiveStatus.Failed, m.View().Me.Objective.Status);
        }

        [Test]
        public void FridayMultipliesSurvivalObjectiveAndFavouriteButAddsFiredPenaltyAfterward()
        {
            var setup = TestMatch.Setup(); setup.Mode = GameMode.Week; setup.WeekDay = 4;
            setup.ManagerProfileId = "legacy_test"; setup.ForceFavourite = 0;
            setup.ForceObjectives[1] = "UNNOTICED";
            var m = new TestMatch(setup: setup, configure: c =>
            {
                c.QuotaTarget = 0;
                c.ManagerProfiles[0].HasFavourite = true; c.ManagerProfiles[0].FavorWeight = 0;
                c.Career.QuotaMet = 2; c.Career.Survived = 3; c.Career.ObjectiveWon = 5;
                c.Career.Favourite = 7; c.Career.Fired = -11;
            }, arrange: l => l.ManagerRoute.Clear());
            m.Phase(MatchPhase.MeetingDiscussion);
            foreach (int p in new[] { 1, 2, 3 }) m.Do(p, ActionVerb.Accuse, param: 0);
            m.Phase(MatchPhase.MeetingVote);
            foreach (int p in new[] { 1, 2, 3 }) m.Do(p, ActionVerb.CastBallot, param: 0);
            m.Finish(); var result = m.View().Result;
            Assert.AreEqual(0, result.FiredPlayer);
            CollectionAssert.AreEqual(new[] { 0 }, result.Favourites);
            Assert.AreEqual(3, result.Players.Single(p => p.Player == 0).CareerDelta, "2 * 7 favourite - 11 fired.");
            Assert.IsTrue(result.Players.Single(p => p.Player == 1).Won);
            Assert.AreEqual(20, result.Players.Single(p => p.Player == 1).CareerDelta, "2 * (2 quota + 3 survival + 5 objective).");
        }

        [TestCase(0, false)]
        [TestCase(2, false)]
        [TestCase(3, true)]
        public void ScamArtistCountsAnsweredPersonalCallsOnly(int answered, bool expected)
        {
            var m = new TestMatch(configure: c => { c.Calls.CooldownSeconds = 0; c.Calls.DeclineBlockSeconds = 0; });
            m.Work();
            for (int n = 0; n < 5; n++)
            {
                m.Do(0, ActionVerb.CallPlayer, param: 1);
                if (n < answered) { m.Do(1, ActionVerb.AnswerCall); m.Do(0, ActionVerb.HangUp); }
                else m.Do(1, ActionVerb.DeclineCall);
            }
            m.Do(0, ActionVerb.TipManager, EntityIds.Desk(1));
            m.Finish(); var result = m.View().Result;
            Assert.AreEqual(answered, result.Calls.Count(c => c.Answered && !c.Tip));
            Assert.AreEqual(expected, result.Players.Single(p => p.Player == 0).Titles.Contains("title.scam_artist"));
        }

        [Test]
        public void FridayMultipliesTheInformantWinAndSurvivingFavouriteRewards()
        {
            var setup = TestMatch.Setup(); setup.Mode = GameMode.Week; setup.WeekDay = 4;
            setup.ManagerProfileId = "legacy_test"; setup.ForceFavourite = 3;
            var m = new TestMatch(setup: setup, configure: c =>
            {
                c.QuotaTarget = 0;
                c.ManagerProfiles[0].HasFavourite = true; c.ManagerProfiles[0].FavorWeight = 0;
                c.Career.QuotaMet = 2; c.Career.Survived = 3; c.Career.InformantWon = 5;
                c.Career.Favourite = 7; c.Career.Fired = -11;
            }, arrange: l => l.ManagerRoute.Clear());
            m.Work(); m.Do(1, ActionVerb.PickUp, EntityIds.Mug(0));
            m.Do(1, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine); m.Do(1, ActionVerb.SpillCoffee, EntityIds.Desk(0));
            m.Phase(MatchPhase.MeetingDiscussion);
            foreach (int p in new[] { 0, 2, 3 }) m.Do(p, ActionVerb.Accuse, param: 1);
            m.Phase(MatchPhase.MeetingVote);
            foreach (int p in new[] { 0, 2, 3 }) m.Do(p, ActionVerb.CastBallot, param: 1);
            m.Finish(); var result = m.View().Result;
            Assert.AreEqual(1, result.FiredPlayer); Assert.IsTrue(result.InformantWon);
            Assert.AreEqual(34, result.Players.Single(p => p.Player == 3).CareerDelta, "2 * (2 quota + 3 survival + 5 informant + 7 favourite).");
            Assert.AreEqual(-11, result.Players.Single(p => p.Player == 1).CareerDelta);
        }

        [TestCase("suspicious", false)]
        [TestCase("suspicious", true)]
        [TestCase("clutter", false)]
        [TestCase("own_report", true)]
        [TestCase("foreign_report", true)]
        [TestCase("nameplate", false)]
        public void PlacingAnyItemOnAnUnassignedDeskCreatesNoIncident(string kind, bool drawer)
        {
            var m = new TestMatch(); m.Work();
            int desk = m.View().Desks.First(d => d.AssignedOwner == PlayerIds.None).Index;
            string item = kind == "suspicious" ? EntityIds.Suspicious(ItemVariants.Duck) :
                kind == "clutter" ? EntityIds.Clutter(0) : kind == "nameplate" ? EntityIds.Nameplate(0) :
                m.Print(kind == "own_report" ? 0 : 1);
            string slot = kind == "nameplate" ? EntityIds.DeskNameplate(desk) :
                drawer ? EntityIds.DeskDrawer(desk) : EntityIds.DeskSurface(desk);
            m.Do(0, ActionVerb.PickUp, item);
            Affordance place = Affordances.For(m.View(), m.Engine.Config, m.Layout, slot, m.Layout.GetSlot(slot).Position)
                .Single(a => a.Verb == ActionVerb.Place);
            Assert.IsFalse(place.IsSabotage);
            Assert.AreEqual(FailureCode.None, place.BlockedBy);
            Assert.AreEqual(kind == "nameplate" ? m.Config.Interaction.PlaceNameplateSeconds :
                drawer ? m.Config.Interaction.PlaceDrawerSeconds : m.Config.Interaction.PlaceSurfaceSeconds, place.Seconds);
            m.Do(0, ActionVerb.Place, slot);
            Assert.AreEqual(slot, m.Item(item).SlotId);
            Assert.IsEmpty(m.Engine.BuildHostDebugView().Incidents);
            Assert.IsTrue(m.Engine.BuildHostDebugView().TrueMisconduct.All(value => value == 0));
        }

        [Test]
        public void TastingUnsaltedCoffeeKeepsItDeliverableAndDoesNotAwardAnExtraTask()
        {
            var m = new TestMatch(); m.Work();
            m.Do(0, ActionVerb.PickUp, EntityIds.Mug(0)); m.Do(0, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine);
            m.Do(0, ActionVerb.TasteCoffee);
            Assert.AreEqual(MugFill.Full, m.Item(EntityIds.Mug(0)).Fill);
            Assert.IsFalse(m.View().Me.Notes.Single(n => n.Kind == NoteKind.TasteResult).Salted);
            Assert.AreEqual(0, m.Player(0).TasksCompleted);
            m.Do(0, ActionVerb.Place, EntityIds.DeliveryTray);
            Assert.AreEqual(MugFill.Used, m.Item(EntityIds.Mug(0)).Fill);
            Assert.AreEqual(1, m.Player(0).TasksCompleted);
            Assert.AreEqual(m.Config.Tasks.CoffeePoints, m.View().Quota.Points);
        }
    }
}
