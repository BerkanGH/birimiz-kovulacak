using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class SabotageV2Tests
    {
        private static MatchSetup AllCards(int seed = 741)
        {
            MatchSetup setup = TestMatch.Setup(seed);
            setup.ForceCards = new List<List<SabotageType>>();
            for (int p = 0; p < Protocol.MaxPlayers; p++) setup.ForceCards.Add(new List<SabotageType>
            {
                SabotageType.LockWorkstation, SabotageType.FakeEmail, SabotageType.PullPlug,
                SabotageType.SaltCoffee, SabotageType.InternetOutage, SabotageType.PowerCut
            });
            return setup;
        }

        private static TestMatch Match(Action<MatchConfig> configure = null, MatchSetup setup = null)
        {
            return new TestMatch(setup: setup ?? AllCards(), configure: config =>
            {
                config.Tasks.MailEnabled = false;
                if (configure != null) configure(config);
            });
        }

        private static DeskView Desk(TestMatch match, int index) { return match.View().Desks.Single(d => d.Index == index); }
        private static IncidentReveal Incident(TestMatch match, SabotageType type)
        { return match.Engine.BuildHostDebugView().Incidents.Last(i => i.Type == type); }
        private static void FullMug(TestMatch match, int player, int mug)
        {
            match.Do(player, ActionVerb.PickUp, EntityIds.Mug(mug));
            match.Do(player, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine);
        }

        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void CardsDealDistinctTypesToEveryoneIncludingInformantDeterministically(int count)
        {
            var setup = TestMatch.Setup(force: false);
            setup.Players.Clear();
            for (int p = 0; p < count; p++) setup.Players.Add(new PlayerSlotInfo { Id = p, Name = "P" + p });
            var config = MatchConfig.CreateDefault();
            var first = new MatchEngine(config, DefaultOffice.Create(), setup, OpenWorld.Instance);
            var second = new MatchEngine(config, DefaultOffice.Create(), setup, OpenWorld.Instance);
            foreach (int p in first.Players)
            {
                var cards = first.BuildView(p, 0).Me.Cards;
                Assert.AreEqual(config.Cards.CardsPerPlayer, cards.Count);
                Assert.AreEqual(cards.Count, cards.Distinct().Count());
                Assert.IsTrue(cards.All(c => !config.GetSabotage(c).Basic && c != SabotageType.EvidenceTampering));
                CollectionAssert.AreEqual(cards, second.BuildView(p, 0).Me.Cards);
                Assert.AreEqual(12, first.BuildView(p, 0).Me.SabotageCooldowns.Count);
            }
        }

        [Test]
        public void ForcedCardsArePrivateUniqueAndFeatureSwitchWins()
        {
            var setup = AllCards();
            setup.ForceCards[0] = new List<SabotageType> { SabotageType.PowerCut, SabotageType.PowerCut, SabotageType.EvidenceTampering };
            setup.ForceCards[1].Clear();
            var match = Match(setup: setup); match.Work();
            CollectionAssert.AreEqual(new[] { SabotageType.PowerCut }, match.View(0).Me.Cards);
            Assert.IsEmpty(match.View(1).Me.Cards);
            match.MoveTo(1, EntityIds.FuseBox);
            Assert.AreEqual(FailureCode.MissingCard, match.Request(1, ActionVerb.CutPower, EntityIds.FuseBox).Failure);
            match.Do(0, ActionVerb.CutPower, EntityIds.FuseBox);
            CollectionAssert.AreEqual(new[] { SabotageType.PowerCut }, match.View(0).Me.Cards, "Cards are reusable.");
            match.View(0).Me.Cards.Clear();
            Assert.AreEqual(1, match.View(0).Me.Cards.Count);
            var disabled = Match(c => c.Cards.Enabled = false); disabled.Work();
            Assert.IsEmpty(disabled.View().Me.Cards);
            disabled.MoveTo(0, EntityIds.FuseBox);
            Assert.AreEqual(FailureCode.FeatureDisabled, disabled.Request(0, ActionVerb.CutPower, EntityIds.FuseBox).Failure);
        }

        [Test]
        public void WeekDayCardSwitchAndBasicSabotageRemainIndependent()
        {
            MatchSetup setup = AllCards(); setup.Mode = GameMode.Week; setup.WeekDay = 0; setup.ManagerProfileId = "legacy_test";
            var match = Match(setup: setup); match.Work();
            Assert.IsFalse(match.View().Features.Cards);
            match.MoveTo(0, EntityIds.DeskComputer(1));
            Assert.AreEqual(FailureCode.FeatureDisabled, match.Request(0, ActionVerb.LockWorkstation, EntityIds.DeskComputer(1)).Failure);
            match.Plant(0, 1);
            Assert.AreEqual(0, Incident(match, SabotageType.PlantItem).Actor);
        }

        [Test]
        public void ScreenSessionsOpenOnlyAfterReportCompletionAndExpire()
        {
            var match = Match(c => c.Email.ScreenOpenSeconds = 2); match.Work();
            Assert.IsFalse(Desk(match, 0).ScreenOpen);
            ActionResult write = match.Start(0, ActionVerb.WriteReport, EntityIds.DeskComputer(0));
            Assert.IsFalse(Desk(match, 0).ScreenOpen);
            match.AdvanceTo(write.CompletesAt + .0001);
            Assert.IsTrue(Desk(match, 0).ScreenOpen);
            Assert.AreEqual(write.CompletesAt + 2, Desk(match, 0).ScreenOpenUntil, .00001);
            match.AdvanceTo(Desk(match, 0).ScreenOpenUntil);
            Assert.IsFalse(Desk(match, 0).ScreenOpen);
            Assert.AreEqual(0, Desk(match, 0).ScreenOpenUntil);
        }

        [Test]
        public void LockScreenRequiresOwnOpenPoweredPcAndClosesSession()
        {
            var match = Match(); match.Work();
            match.MoveTo(0, EntityIds.DeskComputer(0));
            Assert.AreEqual(FailureCode.ScreenLocked, match.Request(0, ActionVerb.LockScreen, EntityIds.DeskComputer(0)).Failure);
            match.Do(0, ActionVerb.WriteReport, EntityIds.DeskComputer(0));
            match.MoveTo(1, EntityIds.DeskComputer(0));
            Assert.AreEqual(FailureCode.NotOwnDesk, match.Request(1, ActionVerb.LockScreen, EntityIds.DeskComputer(0)).Failure);
            match.Do(0, ActionVerb.LockScreen, EntityIds.DeskComputer(0));
            Assert.IsFalse(Desk(match, 0).ScreenOpen);
            match.MoveTo(1, EntityIds.DeskComputer(0));
            Assert.AreEqual(FailureCode.ScreenLocked, match.Request(1, ActionVerb.SendFakeEmail, EntityIds.DeskComputer(0), 0).Failure);
        }

        [Test]
        public void PasswordLockClosesScreenAndUnlockDoesNotReopenIt()
        {
            var match = Match(); match.Work();
            match.Do(0, ActionVerb.WriteReport, EntityIds.DeskComputer(0));
            match.Do(1, ActionVerb.LockWorkstation, EntityIds.DeskComputer(0));
            Assert.IsFalse(Desk(match, 0).ScreenOpen);
            match.Do(0, ActionVerb.UnlockWorkstation, EntityIds.DeskComputer(0));
            Assert.IsFalse(Desk(match, 0).ScreenOpen);
            Assert.That(Desk(match, 0).PcImmuneUntil, Is.GreaterThan(match.Now));
        }

        [Test]
        public void FakeEmailUsesAssignedOwnerRegardlessOfNameplateAndAppliesProfileWeights()
        {
            var match = Match(c =>
            {
                c.Risk.EmailLog = 2;
                c.ManagerProfiles[0].EvidenceFactor = 1.5f;
                c.ManagerProfiles[0].EmailFactor = 2;
                c.ManagerProfiles[0].ObservationFactors.Add(new ObservationFactor(ObservationKind.InappropriateEmail, 3));
            });
            match.Work(); match.Do(0, ActionVerb.WriteReport, EntityIds.DeskComputer(0));
            match.SwapInto(2, 2, 0); match.Do(2, ActionVerb.Drop);
            match.Do(1, ActionVerb.SendFakeEmail, EntityIds.DeskComputer(0), 4);
            PlayerView view = match.View(3);
            Assert.AreEqual(0, view.Inbox.Single().FromPlayer);
            Assert.AreEqual(4, view.Inbox.Single().TemplateId);
            EvidenceView evidence = view.Evidence.Single(e => e.Kind == EvidenceKind.EmailLog);
            Assert.AreEqual(0, evidence.AttributedTo);
            Assert.AreEqual(DiscoverySource.System, evidence.Source);
            Assert.AreEqual(PlayerIds.None, evidence.SourcePlayer);
            Assert.AreEqual(EntityIds.DeskComputer(0), evidence.LocationId);
            Assert.AreEqual(4, evidence.EmailTemplate);
            Assert.AreEqual(3, evidence.RiskWeight);
            Assert.AreEqual(match.Config.Risk.InappropriateEmail * 6, view.Observations.Single(o => o.Kind == ObservationKind.InappropriateEmail).Severity);
            Assert.AreEqual(1, Incident(match, SabotageType.FakeEmail).Actor);
            Assert.AreEqual(0, Incident(match, SabotageType.FakeEmail).Victim);
            Assert.AreEqual(PlayerIds.None, match.Events.Last(e => e.Type == EventType.EmailSent).Actor);
            view.Inbox[0].FromPlayer = 5;
            Assert.AreEqual(0, match.View().Inbox[0].FromPlayer);
        }

        [TestCase(-1)]
        [TestCase(10)]
        [TestCase(int.MaxValue)]
        public void FakeEmailRejectsInvalidTemplates(int template)
        {
            var match = Match(); match.Work(); match.Do(0, ActionVerb.WriteReport, EntityIds.DeskComputer(0));
            match.MoveTo(1, EntityIds.DeskComputer(0));
            Assert.AreEqual(FailureCode.InvalidTarget, match.Request(1, ActionVerb.SendFakeEmail, EntityIds.DeskComputer(0), template).Failure);
            Assert.IsEmpty(match.View().Inbox);
        }

        [Test]
        public void FakeEmailRevalidatesScreenAtCompletion()
        {
            var match = Match(c => c.Email.ScreenOpenSeconds = 2); match.Work();
            match.Do(0, ActionVerb.WriteReport, EntityIds.DeskComputer(0));
            ActionResult email = match.Start(1, ActionVerb.SendFakeEmail, EntityIds.DeskComputer(0), 0);
            Assert.AreEqual(PublicActivity.Typing, match.Player(1).Activity);
            match.AdvanceTo(email.CompletesAt + .001);
            Assert.IsEmpty(match.View().Inbox);
            Assert.IsFalse(match.Engine.BuildHostDebugView().Incidents.Any(i => i.Type == SabotageType.FakeEmail));
            Assert.IsTrue(match.Events.Any(e => e.Type == EventType.InteractionCancelled && e.AudiencePlayer == 1));
        }

        [Test]
        public void FakeEmailAddsConsequenceOnceWhenApparentSenderIsFired()
        {
            var match = Match(); match.Work(); match.Do(0, ActionVerb.WriteReport, EntityIds.DeskComputer(0));
            match.Do(1, ActionVerb.SendFakeEmail, EntityIds.DeskComputer(0), 0);
            float baseline = Incident(match, SabotageType.FakeEmail).Misconduct;
            match.Phase(MatchPhase.MeetingDiscussion);
            for (int p = 1; p < 4; p++) Assert.IsTrue(match.Request(p, ActionVerb.Accuse, param: 0).Accepted);
            match.Phase(MatchPhase.MeetingVote);
            for (int p = 1; p < 4; p++) Assert.IsTrue(match.Request(p, ActionVerb.CastBallot, param: 0).Accepted);
            match.Finish();
            Assert.AreEqual(0, match.View().Result.FiredPlayer);
            Assert.AreEqual(baseline + match.Config.GetSabotage(SabotageType.FakeEmail).ConsequenceMisconduct, Incident(match, SabotageType.FakeEmail).Misconduct);
            match.Advance(10);
            Assert.AreEqual(baseline + match.Config.GetSabotage(SabotageType.FakeEmail).ConsequenceMisconduct, Incident(match, SabotageType.FakeEmail).Misconduct);
        }

        [Test]
        public void PullPlugCancelsReportWithBoundedConsequenceAndAnyPlayerCanRestore()
        {
            var match = Match(); match.Work();
            match.Start(0, ActionVerb.WriteReport, EntityIds.DeskComputer(0));
            match.Do(1, ActionVerb.PullPlug, EntityIds.Desk(0));
            Assert.IsNull(match.View(0).Me.ActiveInteraction);
            Assert.IsTrue(Desk(match, 0).Unplugged);
            Assert.IsFalse(Desk(match, 0).ScreenOpen);
            Assert.AreEqual(match.Config.GetSabotage(SabotageType.PullPlug).BaseMisconduct + match.Config.GetSabotage(SabotageType.PullPlug).ConsequenceMisconduct, Incident(match, SabotageType.PullPlug).Misconduct);
            foreach (ActionVerb verb in new[] { ActionVerb.WriteReport, ActionVerb.SendFakeEmail, ActionVerb.LockWorkstation, ActionVerb.UnlockWorkstation, ActionVerb.LockScreen })
            {
                int actor = verb == ActionVerb.WriteReport || verb == ActionVerb.LockScreen ? 0 : 2;
                match.MoveTo(actor, EntityIds.DeskComputer(0));
                Assert.AreEqual(FailureCode.PcUnplugged, match.Request(actor, verb, EntityIds.DeskComputer(0), 0).Failure, verb.ToString());
            }
            Assert.AreEqual(PlayerIds.None, match.Events.Last(e => e.Type == EventType.PlugPulled).Actor);
            match.Do(2, ActionVerb.PlugIn, EntityIds.Desk(0));
            Assert.IsFalse(Desk(match, 0).Unplugged);
            Assert.AreEqual(2, Incident(match, SabotageType.PullPlug).ReversedBy);
            match.Do(0, ActionVerb.WriteReport, EntityIds.DeskComputer(0));
            Assert.IsTrue(Desk(match, 0).ScreenOpen);
        }

        [Test]
        public void SaltIsPrivateAndDeliveryBlamesDelivererWithoutAwardingAnything()
        {
            var match = Match(c => c.ManagerProfiles[0].FavorSource = FavorSource.AnyTask); match.Work();
            FullMug(match, 0, 0); match.Do(0, ActionVerb.SaltCoffee, EntityIds.CoffeeMachine);
            Assert.IsTrue(match.Item(EntityIds.Mug(0), 0).KnownSalted);
            Assert.IsFalse(match.Item(EntityIds.Mug(0), 1).KnownSalted);
            CollectionAssert.AreEqual(new[] { EntityIds.Mug(0) }, match.Engine.BuildHostDebugView().SaltedItems);
            match.Do(0, ActionVerb.Place, EntityIds.DeskSurface(1));
            match.Do(1, ActionVerb.PickUp, EntityIds.Mug(0));
            float quota = match.View().Quota.Points;
            match.Do(1, ActionVerb.Place, EntityIds.DeliveryTray);
            Assert.AreEqual(quota, match.View().Quota.Points);
            Assert.AreEqual(0, match.Player(1).Performance);
            Assert.AreEqual(0, match.Player(1).TasksCompleted);
            Assert.AreEqual(0, match.Player(1).Favor);
            Assert.AreEqual(MugFill.Used, match.Item(EntityIds.Mug(0)).Fill);
            Assert.IsEmpty(match.Engine.BuildHostDebugView().SaltedItems);
            Assert.IsFalse(match.Item(EntityIds.Mug(0)).KnownSalted);
            Assert.AreEqual(1, match.View().Observations.Single(o => o.Kind == ObservationKind.SaltyCoffee).Subject);
            IncidentReveal incident = Incident(match, SabotageType.SaltCoffee);
            Assert.AreEqual(0, incident.Actor); Assert.AreEqual(1, incident.Victim); Assert.AreEqual(1, incident.BlamedPlayer);
            Assert.AreEqual(match.Config.GetSabotage(SabotageType.SaltCoffee).BaseMisconduct + match.Config.GetSabotage(SabotageType.SaltCoffee).ConsequenceMisconduct, incident.Misconduct);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void TastingOnlyConsumesSaltedCoffeeCreatesPrivateNoteAndReversesSalt(bool salted)
        {
            var match = Match(); match.Work(); FullMug(match, 0, 0);
            if (salted) match.Do(0, ActionVerb.SaltCoffee, EntityIds.CoffeeMachine);
            match.Do(0, ActionVerb.Place, EntityIds.DeskSurface(1)); match.Do(1, ActionVerb.PickUp, EntityIds.Mug(0));
            match.Do(1, ActionVerb.TasteCoffee);
            PrivateNote note = match.View(1).Me.Notes.Single(n => n.Kind == NoteKind.TasteResult);
            Assert.AreEqual(salted, note.Salted); Assert.AreEqual(EntityIds.Mug(0), note.ItemId);
            Assert.IsFalse(match.View(2).Me.Notes.Any(n => n.Kind == NoteKind.TasteResult));
            Assert.AreEqual(salted ? MugFill.Used : MugFill.Full, match.Item(EntityIds.Mug(0)).Fill);
            GameEvent tasted = match.Events.Last(e => e.Type == EventType.CoffeeTasted);
            Assert.AreEqual(Audience.Player, tasted.Audience); Assert.AreEqual(1, tasted.AudiencePlayer);
            Assert.AreEqual(salted ? 1 : 0, tasted.IntArg);
            if (salted) Assert.AreEqual(1, Incident(match, SabotageType.SaltCoffee).ReversedBy);
            Assert.IsEmpty(match.Engine.BuildHostDebugView().SaltedItems);
            Assert.AreEqual(0, match.View().Quota.Points);
        }

        [Test]
        public void SalterTastingOwnCoffeeGetsNoReversalAndSpillingRemovesSalt()
        {
            var match = Match(); match.Work(); FullMug(match, 0, 0);
            match.Do(0, ActionVerb.SaltCoffee, EntityIds.CoffeeMachine); match.Do(0, ActionVerb.TasteCoffee);
            Assert.AreEqual(PlayerIds.None, Incident(match, SabotageType.SaltCoffee).ReversedBy);
            FullMugAfterUsed(match, 0);
            match.Do(1, ActionVerb.PickUp, EntityIds.Mug(1)); match.Do(1, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine);
            match.Do(1, ActionVerb.SaltCoffee, EntityIds.CoffeeMachine); match.Do(1, ActionVerb.SpillCoffee, EntityIds.Desk(2));
            Assert.IsFalse(match.Item(EntityIds.Mug(1), 1).KnownSalted);
            Assert.IsEmpty(match.Engine.BuildHostDebugView().SaltedItems);
        }

        private static void FullMugAfterUsed(TestMatch match, int player) { match.Do(player, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine); }

        [Test]
        public void SaltingReservesCoffeeMachineAndUsesNeutralPublicActivity()
        {
            var match = Match(); match.Work(); FullMug(match, 0, 0);
            match.Do(1, ActionVerb.PickUp, EntityIds.Mug(1));
            ActionResult salt = match.Start(0, ActionVerb.SaltCoffee, EntityIds.CoffeeMachine);
            Assert.AreEqual(PublicActivity.Brewing, match.Player(0).Activity);
            match.MoveTo(1, EntityIds.CoffeeMachine);
            Assert.AreEqual(FailureCode.TargetBusy, match.Request(1, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine).Failure);
            match.AdvanceTo(salt.CompletesAt + .001);
            match.Do(1, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine);
        }

        [Test]
        public void InternetCancelsReportsAndPausesPrinterRemainingTime()
        {
            var match = Match(c => c.Tasks.PrintSeconds = 30); match.Work();
            match.Do(0, ActionVerb.WriteReport, EntityIds.DeskComputer(0));
            double originalEnd = match.View().Stations.Single(s => s.Kind == StationKind.Printer).PrintEndsAt;
            match.Start(1, ActionVerb.WriteReport, EntityIds.DeskComputer(1));
            match.Do(2, ActionVerb.CutInternet, EntityIds.Router);
            double cutAt = match.Events.Last(e => e.Type == EventType.InternetDown).Time;
            Assert.IsTrue(match.View().Office.InternetDown);
            Assert.IsNull(match.View(1).Me.ActiveInteraction);
            match.MoveTo(0, EntityIds.DeskComputer(0));
            Assert.AreEqual(FailureCode.NoInternet, match.Request(0, ActionVerb.WriteReport, EntityIds.DeskComputer(0)).Failure);
            match.MoveTo(3, EntityIds.DeskComputer(0));
            Assert.AreEqual(FailureCode.NoInternet, match.Request(3, ActionVerb.SendFakeEmail, EntityIds.DeskComputer(0), 0).Failure);
            match.Advance(8);
            Assert.IsFalse(match.View().Items.Any(i => i.Kind == ItemKind.Report));
            match.Do(3, ActionVerb.RestartRouter, EntityIds.Router);
            double restoredAt = match.Events.Last(e => e.Type == EventType.InternetRestored).Time;
            Assert.IsFalse(match.View().Office.InternetDown);
            Assert.AreEqual(originalEnd + restoredAt - cutAt, match.View().Stations.Single(s => s.Kind == StationKind.Printer).PrintEndsAt, .00001);
            Assert.AreEqual(3, Incident(match, SabotageType.InternetOutage).ReversedBy);
            Assert.AreEqual(PlayerIds.None, Incident(match, SabotageType.InternetOutage).Victim);
            match.AdvanceTo(originalEnd + restoredAt - cutAt + .001);
            Assert.AreEqual(1, match.View().Items.Count(i => i.Kind == ItemKind.Report));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OutagesAutoRestoreWithoutActorOrConsequence(bool power)
        {
            var match = Match(); match.Work();
            match.Do(0, power ? ActionVerb.CutPower : ActionVerb.CutInternet, power ? EntityIds.FuseBox : EntityIds.Router);
            double end = power ? match.View().Office.PowerBackAt : match.View().Office.InternetBackAt;
            match.AdvanceTo(end + .001);
            Assert.IsFalse(power ? match.View().Office.PowerDown : match.View().Office.InternetDown);
            GameEvent restored = match.Events.Last(e => e.Type == (power ? EventType.PowerRestored : EventType.InternetRestored));
            Assert.AreEqual(PlayerIds.None, restored.Actor);
            var type = power ? SabotageType.PowerCut : SabotageType.InternetOutage;
            Assert.AreEqual(match.Config.GetSabotage(type).BaseMisconduct, Incident(match, type).Misconduct);
            Assert.AreEqual(PlayerIds.None, Incident(match, type).ReversedBy);
        }

        [Test]
        public void MeetingEndsBothOutagesAndOfficeWideLimitsCountSuccessfulCompletions()
        {
            var match = Match(c =>
            {
                c.GetSabotage(SabotageType.PowerCut).ActorCooldownSeconds = 0;
                c.GetSabotage(SabotageType.PowerCut).EffectSeconds = 1000;
                c.GetSabotage(SabotageType.InternetOutage).EffectSeconds = 1000;
            });
            match.Work(); match.Do(0, ActionVerb.CutPower, EntityIds.FuseBox);
            match.Do(1, ActionVerb.ResetFuse, EntityIds.FuseBox);
            match.Do(2, ActionVerb.CutPower, EntityIds.FuseBox);
            match.Do(1, ActionVerb.ResetFuse, EntityIds.FuseBox);
            match.MoveTo(0, EntityIds.FuseBox);
            Assert.AreEqual(FailureCode.LimitReached, match.Request(0, ActionVerb.CutPower, EntityIds.FuseBox).Failure);
            match.Do(0, ActionVerb.CutInternet, EntityIds.Router);
            match.Phase(MatchPhase.MeetingDiscussion);
            Assert.IsFalse(match.View().Office.InternetDown);
            Assert.IsFalse(match.View().Office.PowerDown);
        }

        [Test]
        public void BothActiveOutagesAreTruncatedAtMeetingStart()
        {
            var match = Match(c =>
            {
                c.GetSabotage(SabotageType.PowerCut).EffectSeconds = 1000;
                c.GetSabotage(SabotageType.InternetOutage).EffectSeconds = 1000;
            });
            match.Work(); match.Do(0, ActionVerb.CutInternet, EntityIds.Router);
            match.Do(1, ActionVerb.CutPower, EntityIds.FuseBox);
            Assert.IsTrue(match.View().Office.InternetDown); Assert.IsTrue(match.View().Office.PowerDown);
            match.Phase(MatchPhase.MeetingDiscussion);
            Assert.IsFalse(match.View().Office.InternetDown); Assert.IsFalse(match.View().Office.PowerDown);
            Assert.AreEqual(match.View().PhaseStartTime, match.Events.Last(e => e.Type == EventType.PowerRestored).Time, .00001);
            Assert.AreEqual(match.View().PhaseStartTime, match.Events.Last(e => e.Type == EventType.InternetRestored).Time, .00001);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DarknessCapsManagerVisionDuringRealInspection(bool dark)
        {
            var match = new TestMatch(setup: AllCards(), configure: c =>
            {
                c.Durations.RaidWarning = 0; c.Manager.VisionRange = 8; c.Manager.DarkVisionRange = 3;
                c.GetSabotage(SabotageType.PowerCut).EffectSeconds = 1000;
            }, arrange: layout =>
            {
                Vec3 origin = layout.GetDesk(0).Position;
                layout.ManagerHome = origin; layout.ManagerRoute.Clear();
                layout.ManagerRoute.Add(new ManagerStop { Position = origin, Yaw = 0, DwellSeconds = 10 });
            });
            match.Work();
            if (dark) match.Do(2, ActionVerb.CutPower, EntityIds.FuseBox);
            match.Do(0, ActionVerb.PickUp, EntityIds.Suspicious(ItemVariants.Duck));
            match.Move(0, match.Layout.ManagerHome + new Vec3(0, 0, 4));
            match.Phase(MatchPhase.Raid); match.Advance(.2);
            Assert.AreEqual(!dark, match.View().Observations.Any(o => o.Kind == ObservationKind.CarryingSuspicious && o.Subject == 0));
        }

        [TestCase(ActionVerb.SendFakeEmail, true)]
        [TestCase(ActionVerb.PullPlug, false)]
        public void NewDeskSabotageRejectsUnassignedAndOwnDesks(ActionVerb verb, bool computer)
        {
            var match = Match(); match.Work();
            string unused = computer ? EntityIds.DeskComputer(5) : EntityIds.Desk(5);
            match.MoveTo(0, unused);
            Assert.AreEqual(FailureCode.InvalidTarget, match.Request(0, verb, unused, 0).Failure);
            string own = computer ? EntityIds.DeskComputer(0) : EntityIds.Desk(0);
            match.MoveTo(0, own);
            Assert.AreEqual(FailureCode.CannotTargetSelf, match.Request(0, verb, own, 0).Failure);
        }

        [TestCase(false, false, true)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        public void CrouchAndDarknessReduceWitnessRange(bool crouched, bool dark, bool expected)
        {
            var match = Match(c => { c.Witness.Range = 8; c.Witness.CrouchRangeFactor = .5f; c.Witness.DarkRange = 2.5f; });
            match.Work();
            if (dark) match.Do(2, ActionVerb.CutPower, EntityIds.FuseBox);
            match.Do(0, ActionVerb.PickUp, EntityIds.Suspicious(ItemVariants.Duck));
            Vec3 target = match.Layout.GetSlot(EntityIds.DeskSurface(1)).Position;
            Vec3 observer = target + new Vec3(6, 0, 0);
            match.Move(1, observer, Vec3.YawTo(observer, target));
            match.Engine.UpdateAvatar(0, new AvatarSample { Position = target, Crouched = crouched });
            ActionResult place = match.Request(0, ActionVerb.Place, EntityIds.DeskSurface(1));
            Assert.IsTrue(place.Accepted);
            match.AdvanceTo(place.CompletesAt + .001);
            Assert.AreEqual(crouched, match.Player(0).Crouched);
            Assert.AreEqual(expected, match.View(1).Me.Notes.Any(n => n.Kind == NoteKind.Witness && n.Subject == 0));
        }
    }
}
