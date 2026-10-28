using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class ManagerProfilesV2Tests
    {
        private static TestMatch Match(ManagerProfileDef profile = null, Action<MatchConfig> configure = null, MatchSetup setup = null, Action<OfficeLayout> arrange = null)
        {
            return new TestMatch(configure: c =>
            {
                c.ManagerProfiles.Clear();
                if (profile != null) c.ManagerProfiles.Add(profile);
                if (configure != null) configure(c);
            }, setup: setup, arrange: arrange);
        }

        [Test]
        public void ExplicitProfileWinsOverWeekAndWeekWinsOverRandom()
        {
            MatchSetup setup = TestMatch.Setup(); setup.Mode = GameMode.Week; setup.WeekDay = 1; setup.ManagerProfileId = "explicit";
            Action<MatchConfig> configure = c =>
            {
                c.ManagerProfiles.Add(new ManagerProfileDef { Id = "explicit", QuotaDelta = 7 });
                c.ManagerProfiles.Add(new ManagerProfileDef { Id = "scheduled", QuotaDelta = 3 });
                c.WeekDays[1].ProfileId = "scheduled";
            };
            TestMatch explicitMatch = Match(configure: configure, setup: setup);
            Assert.AreEqual("explicit", explicitMatch.View().ManagerProfileId);
            Assert.AreEqual(explicitMatch.Config.QuotaTarget + 7, explicitMatch.View().Quota.Target);
            setup.ManagerProfileId = "";
            Assert.AreEqual("scheduled", Match(configure: configure, setup: setup).View().ManagerProfileId);
        }

        [Test]
        public void RandomProfileIsSeededAndExcludesPreviousWhenPossible()
        {
            for (int seed = 0; seed < 24; seed++)
            {
                MatchSetup setup = TestMatch.Setup(seed); setup.History = new SessionHistory { PastManagerProfiles = new List<string> { "last" } };
                Action<MatchConfig> configure = c =>
                {
                    c.ManagerProfiles.Add(new ManagerProfileDef { Id = "last" });
                    c.ManagerProfiles.Add(new ManagerProfileDef { Id = "a" });
                    c.ManagerProfiles.Add(new ManagerProfileDef { Id = "b" });
                };
                string selected = Match(configure: configure, setup: setup).View().ManagerProfileId;
                Assert.AreNotEqual("last", selected);
                Assert.AreEqual(selected, Match(configure: configure, setup: setup).View().ManagerProfileId);
            }
        }

        [Test]
        public void SingleOrMissingProfileWorksAndInvalidExplicitProfileFailsClearly()
        {
            MatchSetup setup = TestMatch.Setup(); setup.History = new SessionHistory { PastManagerProfiles = new List<string> { "only" } };
            Assert.AreEqual("only", Match(new ManagerProfileDef { Id = "only" }, setup: setup).View().ManagerProfileId);
            TestMatch legacy = Match();
            Assert.AreEqual("", legacy.View().ManagerProfileId);
            Assert.AreEqual(legacy.Config.QuotaTarget, legacy.View().Quota.Target);
            setup.ManagerProfileId = "unknown";
            Assert.Throws<ArgumentException>(() => Match(new ManagerProfileDef { Id = "only" }, setup: setup));
        }

        [Test]
        public void OverridesUsePrivateDeepConfigCopyAndNegativeValuesPreserveDefaults()
        {
            var profile = new ManagerProfileDef { Id = "overrides", QuotaDelta = 2, VoteWeight = 0, RiskWeight = 0.7f, PerformanceWeight = 0.8f, FavorWeight = 0.4f, VisionRange = 3, VisionHalfAngle = 15 };
            TestMatch m = Match(profile);
            MatchConfig effective = m.Engine.Config;
            Assert.AreNotSame(m.Config, effective);
            Assert.AreNotSame(m.Config.Manager, effective.Manager);
            Assert.AreNotSame(m.Config.Verdict, effective.Verdict);
            Assert.AreEqual(m.Config.QuotaTarget + 2, effective.QuotaTarget);
            Assert.AreEqual(0, effective.Verdict.VoteWeight);
            Assert.AreEqual(0.7f, effective.Verdict.RiskWeight);
            Assert.AreEqual(0.8f, effective.Verdict.PerformanceWeight);
            Assert.AreEqual(0.4f, effective.Verdict.FavorWeight);
            Assert.AreEqual(3, effective.Manager.VisionRange);
            Assert.AreEqual(15, effective.Manager.VisionHalfAngle);
            Assert.AreNotEqual(3, m.Config.Manager.VisionRange);
            profile.VisionRange = 99; profile.ObservationFactors.Add(new ObservationFactor(ObservationKind.NotAtDesk, 99));
            Assert.AreEqual(3, effective.Manager.VisionRange);
            Assert.IsEmpty(effective.ManagerProfiles[0].ObservationFactors);
            TestMatch defaults = Match(new ManagerProfileDef { Id = "defaults" });
            Assert.AreEqual(defaults.Config.Verdict.VoteWeight, defaults.Engine.Config.Verdict.VoteWeight);
            Assert.AreEqual(defaults.Config.Manager.VisionRange, defaults.Engine.Config.Manager.VisionRange);
        }

        [Test]
        public void WeekFeatureFlagsAreAndedAndDetachedFromViews()
        {
            MatchSetup setup = TestMatch.Setup(); setup.Mode = GameMode.Week; setup.WeekDay = 4;
            TestMatch m = Match(configure: c =>
            {
                c.WeekDays[4].Cards = false; c.WeekDays[4].Calls = false; c.WeekDays[4].CareerMultiplier = 2.5f;
                c.Calls.TipLineEnabled = false; c.Footage.Enabled = false; c.Tasks.MailEnabled = false;
            }, setup: setup);
            PlayerView view = m.View();
            Assert.AreEqual(GameMode.Week, view.Mode); Assert.AreEqual(4, view.WeekDay);
            Assert.IsFalse(view.Features.Cards); Assert.IsFalse(view.Features.Calls); Assert.IsFalse(view.Features.TipLine);
            Assert.IsFalse(view.Features.Footage); Assert.IsFalse(view.Features.Mail); Assert.AreEqual(2.5f, view.Features.CareerMultiplier);
            view.Features.Cards = true; view.Features.CareerMultiplier = 99;
            Assert.IsFalse(m.View().Features.Cards); Assert.AreEqual(2.5f, m.View().Features.CareerMultiplier);
            TestMatch quick = Match(); Assert.AreEqual(1, quick.View().Features.CareerMultiplier);
        }

        [Test]
        public void FavouriteUsesPresentIdsAndIsPublicAndSeeded()
        {
            MatchSetup setup = TestMatch.Setup(); setup.Players.RemoveAt(2); setup.Players[1].Id = 4; setup.ForceFavourite = 4;
            TestMatch m = Match(new ManagerProfileDef { Id = "favourite", HasFavourite = true }, setup: setup);
            foreach (int p in m.Engine.Players)
            {
                Assert.AreEqual(4, m.View(p).Favourite);
                Assert.AreEqual(2, m.Player(4).Favor);
            }
            GameEvent change = m.Events.Single(e => e.Type == EventType.FavorChanged);
            Assert.AreEqual(4, change.Actor); Assert.AreEqual(Audience.All, change.Audience); Assert.AreEqual("2", change.StrArg);
            setup.ForceFavourite = PlayerIds.None;
            int selected = Match(new ManagerProfileDef { Id = "favourite", HasFavourite = true }, setup: setup).View().Favourite;
            Assert.Contains(selected, setup.Players.Select(p => p.Id).ToList());
            Assert.AreEqual(selected, Match(new ManagerProfileDef { Id = "favourite", HasFavourite = true }, setup: setup).View().Favourite);
        }

        [TestCase(FavorSource.None, 0)]
        [TestCase(FavorSource.CoffeeDelivered, 0.75f)]
        [TestCase(FavorSource.ReportsDelivered, 0.75f)]
        [TestCase(FavorSource.AnyTask, 1.5f)]
        public void FavorSourceRewardsCompleterExactlyOnce(FavorSource source, float expected)
        {
            TestMatch m = Match(new ManagerProfileDef { Id = "favor", FavorSource = source, FavorPerAction = 0.75f }); m.Work();
            m.Coffee(1, 0);
            string report = m.Print(0); m.Do(1, ActionVerb.PickUp, report); m.Do(1, ActionVerb.Place, EntityIds.DeliveryTray);
            Assert.AreEqual(expected, m.Player(1).Favor);
            Assert.AreEqual(0, m.Player(0).Favor);
            m.Advance(2); Assert.AreEqual(expected, m.Player(1).Favor);
        }

        [Test]
        public void FavorDiscountAppliesToAllPositiveLeadersButNotLowerFavor()
        {
            TestMatch m = Match(new ManagerProfileDef { Id = "favor", FavorSource = FavorSource.CoffeeDelivered, FavorWeight = 0.25f });
            m.Work(); m.Coffee(0, 0); m.Coffee(1, 1);
            m.Phase(MatchPhase.MeetingDiscussion); m.Do(2, ActionVerb.Accuse, param: 0); m.Do(3, ActionVerb.Accuse, param: 1);
            m.Phase(MatchPhase.MeetingVerdict);
            foreach (CandidateScore score in m.View().Meeting.Verdict.Candidates)
            {
                Assert.AreEqual(1, score.Favor); Assert.AreEqual(0.25f, score.FavorDiscount);
                float raw = score.VoteShare * m.Engine.Config.Verdict.VoteWeight + score.RiskNormalized * m.Engine.Config.Verdict.RiskWeight + score.PerformanceDeficit * m.Engine.Config.Verdict.PerformanceWeight;
                Assert.AreEqual(raw - 0.25f, score.Score);
            }
            m.Finish(); CollectionAssert.AreEqual(new[] { 0, 1 }, m.View().Result.Favourites);
            Assert.AreEqual(1, m.View().Result.Players.Single(p => p.Player == 0).Favor);
        }

        [Test]
        public void NoncandidateFavouriteDoesNotDiscountCandidatesAndZeroFavorHasNoLeaders()
        {
            MatchSetup setup = TestMatch.Setup(); setup.ForceFavourite = 3;
            TestMatch m = Match(new ManagerProfileDef { Id = "fav", HasFavourite = true }, setup: setup);
            m.Phase(MatchPhase.MeetingDiscussion); m.Do(2, ActionVerb.Accuse, param: 0); m.Do(3, ActionVerb.Accuse, param: 1); m.Finish();
            Assert.IsTrue(m.View().Result.Verdict.Candidates.All(c => c.FavorDiscount == 0)); CollectionAssert.AreEqual(new[] { 3 }, m.View().Result.Favourites);
            TestMatch none = Match(); none.Finish(); Assert.IsEmpty(none.View().Result.Favourites); Assert.IsTrue(none.View().Result.Verdict.Candidates.All(c => c.FavorDiscount == 0));
        }

        [Test]
        public void ObservationFactorsKeepZeroRecordsAndMultiplyEvidence()
        {
            TestMatch m = Match(new ManagerProfileDef
            {
                Id = "factors", EvidenceFactor = 1.5f,
                ObservationFactors = { new ObservationFactor(ObservationKind.MessyDesk, 0), new ObservationFactor(ObservationKind.NotAtDesk, 2) }
            }, configure: c => c.Tasks.InitialDirtyDesks = 6);
            m.Work(); m.Plant(2, 0, drawer: false); m.Move(2, m.Layout.BoundsMax + new Vec3(10, 0, 10));
            m.Phase(MatchPhase.Raid); m.Advance(8);
            Assert.IsTrue(m.View().Observations.Any(o => o.Kind == ObservationKind.MessyDesk && o.Severity == 0));
            Assert.AreEqual(m.Config.Risk.NotAtDesk * 2, m.View().Observations.First(o => o.Kind == ObservationKind.NotAtDesk).Severity);
            Assert.AreEqual(m.Config.Risk.SuspiciousItem * 1.5f, m.FindEvidence(EntityIds.Suspicious(ItemVariants.Duck)).RiskWeight);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RaidDrawersNeedSearchAllAndFinalAlwaysOpensThem(bool searchAll)
        {
            TestMatch m = Match(new ManagerProfileDef { Id = "drawers", SearchAllDrawers = searchAll }); m.Work(); m.Plant(2, 0);
            m.Phase(MatchPhase.Raid); m.Advance(8);
            Assert.AreEqual(searchAll, m.View().Evidence.Any(e => e.ItemId == EntityIds.Suspicious(ItemVariants.Duck)));
            m.Phase(MatchPhase.FinalInspection); m.Advance(8);
            Assert.IsTrue(m.View().Evidence.Any(e => e.ItemId == EntityIds.Suspicious(ItemVariants.Duck)));
        }

        [Test]
        public void SurpriseStartsExactlyWithoutWarningAndStopsAtItsDuration()
        {
            TestMatch m = Match(new ManagerProfileDef { Id = "surprise", SurpriseInspection = true }, c =>
            {
                c.Manager.SurpriseAtFraction = 0.25f; c.Manager.SurpriseSeconds = 2; c.Manager.WalkSpeed = 0.1f;
            });
            m.Phase(MatchPhase.Work2); double start = m.View().PhaseStartTime + m.Config.Durations.Work2 * 0.25;
            m.AdvanceTo(start - 0.001); Assert.IsFalse(m.View().Manager.Surprise);
            m.AdvanceTo(start); Assert.IsTrue(m.View().Manager.Surprise);
            Assert.AreEqual(start, m.Events.Single(e => e.Type == EventType.SurpriseInspection).Time);
            Assert.IsFalse(m.Events.Any(e => e.Type == EventType.ManagerWarning && e.Phase == MatchPhase.Work2));
            m.AdvanceTo(start + 2); m.Engine.Tick(start + 2); Assert.IsFalse(m.View().Manager.Surprise); Assert.AreEqual(ManagerState.InOffice, m.View().Manager.State);
            m.Advance(3); Assert.AreEqual(1, m.Events.Count(e => e.Type == EventType.SurpriseInspection));
        }

        [Test]
        public void SurpriseEndsOnReturnHomeAndObservesLikeRaidWithoutOpeningDrawers()
        {
            TestMatch m = Match(new ManagerProfileDef { Id = "surprise", SurpriseInspection = true }, c =>
            {
                c.Manager.SurpriseAtFraction = 0; c.Manager.SurpriseSeconds = 100;
            });
            m.Work(); m.Plant(2, 0); m.Phase(MatchPhase.Work2); Assert.IsTrue(m.View().Manager.Surprise);
            m.Advance(10); Assert.IsFalse(m.View().Manager.Surprise); Assert.AreEqual(ManagerState.InOffice, m.View().Manager.State);
            Assert.IsTrue(m.View().Observations.Any(o => o.Phase == MatchPhase.Work2));
            Assert.IsFalse(m.View().Evidence.Any(e => e.ItemId == EntityIds.Suspicious(ItemVariants.Duck)));
        }

        [Test]
        public void SurpriseCannotExtendWork2AndZeroDurationDoesNotSpin()
        {
            TestMatch m = Match(new ManagerProfileDef { Id = "surprise", SurpriseInspection = true }, c =>
            {
                c.Durations.Work2 = 2; c.Manager.SurpriseAtFraction = 0.5f; c.Manager.SurpriseSeconds = 100; c.Manager.WalkSpeed = 0.1f;
            });
            m.Phase(MatchPhase.Work2); double end = m.View().PhaseEndTime;
            m.AdvanceTo(end - 0.01); Assert.IsTrue(m.View().Manager.Surprise);
            m.AdvanceTo(end); Assert.AreEqual(MatchPhase.FinalInspection, m.Engine.Phase); Assert.IsFalse(m.View().Manager.Surprise);
            TestMatch zero = Match(new ManagerProfileDef { Id = "zero", SurpriseInspection = true }, c => { c.Manager.SurpriseAtFraction = 0; c.Manager.SurpriseSeconds = 0; });
            zero.Phase(MatchPhase.Work2); Assert.IsFalse(zero.View().Manager.Surprise); Assert.AreEqual(1, zero.Events.Count(e => e.Type == EventType.SurpriseInspection));
        }
    }
}
