using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class CareerV2Tests
    {
        private static MatchSetup Setup(int day = 0)
        {
            MatchSetup setup = TestMatch.Setup(); setup.Mode = GameMode.Week; setup.WeekDay = day; setup.ManagerProfileId = "legacy_test";
            setup.History = new SessionHistory { NextWeekDay = day, WeekNumber = 7, CareerPoints = new List<int> { 10, 20, 30, 40, 50, 60 } };
            return setup;
        }

        private static int Expected(PlayerOutcome p, MatchResultView r, CareerConfig c, float multiplier)
        {
            return (int)Math.Round(multiplier * ((r.CompanySucceeded && !p.Fired ? c.QuotaMet : 0)
                + (!p.Fired ? c.Survived : 0) + (p.Role == Role.Employee && p.Won ? c.ObjectiveWon : 0)
                + (p.Role == Role.Informant && r.InformantWon ? c.InformantWon : 0)
                + (r.Favourites.Contains(p.Player) ? c.Favourite : 0)) + (p.Fired ? c.Fired : 0), MidpointRounding.AwayFromZero);
        }

        [TestCase(0, 1f)]
        [TestCase(3, 1.5f)]
        [TestCase(4, 2f)]
        public void WeekAwardsExactFormulaWithAllPositiveTermsMultiplied(int day, float multiplier)
        {
            var setup = Setup(day); setup.ForceObjectives[0] = "UNNOTICED";
            var m = new TestMatch(setup: setup, configure: c => { c.QuotaTarget = 0; c.WeekDays[day].CareerMultiplier = multiplier; });
            Assert.AreEqual(GameMode.Week, m.View().Mode); Assert.AreEqual(day, m.View().WeekDay);
            Assert.AreEqual(multiplier, m.View().Features.CareerMultiplier);
            foreach (PlayerPublicView p in m.View().Players) Assert.AreEqual((p.Id + 1) * 10, p.CareerPoints);
            setup.History.CareerPoints[0] = -1000;
            m.Finish(); MatchResultView r = m.View().Result;
            foreach (PlayerOutcome p in r.Players)
            {
                Assert.AreEqual(Expected(p, r, m.Config.Career, multiplier), p.CareerDelta);
                Assert.AreEqual((p.Player + 1) * 10 + p.CareerDelta, p.CareerTotal);
                Assert.AreEqual((p.Player + 1) * 10, m.Player(p.Player).CareerPoints, "Public total remains pre-match until the next match.");
            }
            Assert.AreEqual(GameMode.Week, r.Mode); Assert.AreEqual(day, r.WeekDay); Assert.AreEqual("legacy_test", r.ManagerProfileId);
            SessionHistory next = m.Engine.BuildNextHistory();
            Assert.AreEqual(day == 4 ? 0 : day + 1, next.NextWeekDay);
            Assert.AreEqual(day == 4 ? 8 : 7, next.WeekNumber);
            Assert.AreEqual("legacy_test", next.PastManagerProfiles.Last());
            if (day == 4) Assert.IsTrue(next.CareerPoints.All(p => p == 0));
            else foreach (PlayerOutcome p in r.Players) Assert.AreEqual(p.CareerTotal, next.CareerPoints[p.Player]);
            Assert.AreEqual(day == 4, r.WeekFinished);
            CollectionAssert.AreEqual(day == 4 ? r.Players.Where(p => p.CareerTotal == r.Players.Max(x => x.CareerTotal)).Select(p => p.Player) : Enumerable.Empty<int>(), r.EmployeeOfTheMonth);
        }

        [Test]
        public void FailureStillAwardsSurvivalButNeverObjectiveOrInformantWinPoints()
        {
            var m = new TestMatch(setup: Setup(), configure: c => c.QuotaTarget = 999); m.Finish(); var r = m.View().Result;
            Assert.IsFalse(r.CompanySucceeded);
            foreach (PlayerOutcome p in r.Players) Assert.AreEqual(p.Fired ? m.Config.Career.Fired : m.Config.Career.Survived, p.CareerDelta);
        }

        [Test]
        public void InformantBonusIsAwardedOnceAndFavouriteBonusUsesVerdictLeaders()
        {
            var setup = Setup(2); setup.ForceFavourite = 0;
            var m = new TestMatch(setup: setup, configure: c => { c.QuotaTarget = 0; c.ManagerProfiles[0].HasFavourite = true; c.ManagerProfiles[0].FavorWeight = 0; });
            m.Work(); m.Do(1, ActionVerb.PickUp, EntityIds.Mug(0)); m.Do(1, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine);
            m.Do(1, ActionVerb.SpillCoffee, EntityIds.Desk(0));
            m.Phase(MatchPhase.MeetingDiscussion);
            foreach (int p in new[] { 0, 2, 3 }) m.Do(p, ActionVerb.Accuse, param: 1);
            m.Phase(MatchPhase.MeetingVote);
            foreach (int p in new[] { 0, 2, 3 }) m.Do(p, ActionVerb.CastBallot, param: 1);
            m.Finish(); var r = m.View().Result;
            Assert.AreEqual(1, r.FiredPlayer); Assert.IsTrue(r.InformantWon);
            Assert.AreEqual(5, r.Players.Single(p => p.Player == 3).CareerDelta);
            Assert.AreEqual(3, r.Players.Single(p => p.Player == 0).CareerDelta);
            CollectionAssert.AreEqual(new[] { 0 }, r.Favourites);
        }

        [Test]
        public void QuickPlayLeavesIncomingCareerAndDayUntouchedWithZeroResultCareerData()
        {
            var setup = Setup(3); setup.Mode = GameMode.QuickPlay;
            var m = new TestMatch(setup: setup, configure: c => c.QuotaTarget = 0); m.Finish(); var r = m.View().Result;
            Assert.AreEqual(0, r.WeekDay); Assert.IsFalse(r.WeekFinished); Assert.IsEmpty(r.EmployeeOfTheMonth);
            Assert.IsTrue(r.Players.All(p => p.CareerDelta == 0 && p.CareerTotal == 0));
            SessionHistory next = m.Engine.BuildNextHistory();
            Assert.AreEqual(3, next.NextWeekDay); Assert.AreEqual(7, next.WeekNumber); CollectionAssert.AreEqual(setup.History.CareerPoints, next.CareerPoints);
        }

        [Test]
        public void AbandonedOrUnfinishedMatchesDoNotAdvanceCareerAndReturnedHistoryIsDetached()
        {
            var setup = Setup(2); var m = new TestMatch(setup: setup);
            SessionHistory early = m.Engine.BuildNextHistory(); Assert.AreEqual(2, early.NextWeekDay);
            m.Engine.Abandon(AbandonReason.HostAborted, m.Now); SessionHistory next = m.Engine.BuildNextHistory();
            Assert.AreEqual(2, next.NextWeekDay); Assert.AreEqual(7, next.WeekNumber);
            CollectionAssert.AreEqual(setup.History.CareerPoints, next.CareerPoints); Assert.IsEmpty(next.PastManagerProfiles);
            next.CareerPoints[0] = -987; Assert.AreEqual(10, m.Engine.BuildNextHistory().CareerPoints[0]); Assert.IsNull(m.View().Result);
        }

        [Test]
        public void FinaleUsesConfiguredScheduleLengthAndAllowsTiedEmployeesOfTheMonth()
        {
            Action<MatchConfig> config = c => { c.WeekDays.RemoveRange(2, 3); c.QuotaTarget = 999; };
            var baseline = new TestMatch(setup: Setup(1), configure: config); baseline.Finish();
            var setup = Setup(1);
            foreach (PlayerOutcome p in baseline.View().Result.Players) setup.History.CareerPoints[p.Player] = 100 - p.CareerDelta;
            var tied = new TestMatch(setup: setup, configure: config); tied.Finish(); var result = tied.View().Result;
            Assert.IsTrue(result.WeekFinished); CollectionAssert.AreEqual(tied.Engine.Players, result.EmployeeOfTheMonth);
            Assert.IsTrue(result.Players.All(p => p.CareerTotal == 100));
            Assert.IsTrue(tied.Engine.BuildNextHistory().CareerPoints.All(p => p == 0));
        }

        [Test]
        public void ResultCardsAreRevealedDeepCopiedAndHashedForEveryRecipient()
        {
            var setup = Setup(2); setup.ForceCards = new List<List<SabotageType>>();
            for (int p = 0; p < Protocol.MaxPlayers; p++) setup.ForceCards.Add(new List<SabotageType> { SabotageType.FakeEmail, SabotageType.PowerCut });
            var m = new TestMatch(setup: setup); Assert.IsNull(m.View().Result); m.Finish();
            MatchResultView result = m.View().Result;
            foreach (PlayerOutcome p in result.Players) CollectionAssert.AreEqual(setup.ForceCards[p.Player], p.Cards);
            foreach (int p in m.Engine.Players) Assert.AreEqual(result.ResultHash, m.View(p).Result.ResultHash);
            string hash = result.ResultHash; result.Players[0].Cards.Clear();
            Assert.AreNotEqual(hash, WireCodec.HashResult(result)); Assert.AreEqual(2, m.View().Result.Players[0].Cards.Count);
            HostDebugView debug = m.Engine.BuildHostDebugView(); debug.Cards[0].Clear(); Assert.AreEqual(2, m.Engine.BuildHostDebugView().Cards[0].Count);
        }

        [Test]
        public void NewTitlesRespectTwoTitleLimitAndAllNewRecapKindsCanAppear()
        {
            var setup = TestMatch.Setup(); setup.ForceCards = new List<List<SabotageType>>();
            for (int p = 0; p < Protocol.MaxPlayers; p++) setup.ForceCards.Add(new List<SabotageType> { SabotageType.SaltCoffee, SabotageType.PowerCut });
            setup.ForceFavourite = 2;
            var m = new TestMatch(setup: setup, configure: c => { c.Calls.CooldownSeconds = 0; c.ManagerProfiles[0].HasFavourite = true; c.Tasks.InitialDirtyDesks = 0; });
            m.Work();
            for (int n = 0; n < 3; n++) { m.Do(3, ActionVerb.CallPlayer, param: 1); m.Do(1, ActionVerb.AnswerCall); m.Do(3, ActionVerb.HangUp); }
            m.Do(0, ActionVerb.CutPower, EntityIds.FuseBox); m.Do(0, ActionVerb.ResetFuse, EntityIds.FuseBox);
            m.Do(0, ActionVerb.PickUp, EntityIds.Mug(0)); m.Do(0, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine);
            m.Do(0, ActionVerb.SaltCoffee, EntityIds.CoffeeMachine); m.Do(0, ActionVerb.Drop);
            m.Do(1, ActionVerb.PickUp, EntityIds.Mug(0)); m.Do(1, ActionVerb.Place, EntityIds.DeliveryTray);
            m.Do(1, ActionVerb.TipManager, EntityIds.Desk(2)); m.Finish(); var r = m.View().Result;
            CollectionAssert.Contains(r.Players.Single(p => p.Player == 3).Titles, "title.scam_artist");
            CollectionAssert.Contains(r.Players.Single(p => p.Player == 0).Titles, "title.salt_bae");
            CollectionAssert.Contains(r.Players.Single(p => p.Player == 0).Titles, "title.it_hero");
            CollectionAssert.Contains(r.Players.Single(p => p.Player == 2).Titles, "title.favourite");
            Assert.IsTrue(r.Players.All(p => p.Titles.Count <= 2)); Assert.LessOrEqual(r.Recap.Count, 10);
            foreach (string key in new[] { "recap.call", "recap.power_down", "recap.salted", "recap.salty_delivery", "recap.tip", "recap.tip_clean" })
                CollectionAssert.Contains(r.Recap.Select(b => b.Key), key);
            CollectionAssert.AreEqual(r.Recap.Select(b => b.Time).OrderBy(t => t), r.Recap.Select(b => b.Time));
        }

        [TestCase(SabotageType.FakeEmail, "recap.fake_email")]
        [TestCase(SabotageType.PullPlug, "recap.pulled_plug")]
        [TestCase(SabotageType.InternetOutage, "recap.internet_down")]
        public void RemainingNewSabotagesHaveDedicatedRecapKeys(SabotageType type, string key)
        {
            var setup = TestMatch.Setup(); setup.ForceCards = new List<List<SabotageType>> { new List<SabotageType> { type } };
            var m = new TestMatch(setup: setup); m.Work();
            if (type == SabotageType.FakeEmail) { m.Do(1, ActionVerb.WriteReport, EntityIds.DeskComputer(1)); m.Do(0, ActionVerb.SendFakeEmail, EntityIds.DeskComputer(1), 2); }
            else if (type == SabotageType.PullPlug) m.Do(0, ActionVerb.PullPlug, EntityIds.Desk(1));
            else m.Do(0, ActionVerb.CutInternet, EntityIds.Router);
            m.Finish(); CollectionAssert.Contains(m.View().Result.Recap.Select(b => b.Key), key);
            Assert.IsFalse(m.View().Result.Recap.Any(b => b.Key == "recap.evidence_tampered"));
        }
    }
}
