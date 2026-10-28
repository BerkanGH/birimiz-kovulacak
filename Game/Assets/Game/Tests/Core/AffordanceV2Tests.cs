using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class AffordanceV2Tests
    {
        private MatchConfig config;
        private OfficeLayout layout;
        private PlayerView view;

        [SetUp]
        public void SetUp()
        {
            config = MatchConfig.CreateDefault(); layout = DefaultOffice.Create();
            view = new PlayerView { Recipient = 0, Phase = MatchPhase.Work1, ServerTime = 10 };
            for (int p = 0; p < 4; p++) view.Players.Add(new PlayerPublicView { Id = p, DeskIndex = p, Connected = true });
            foreach (DeskDef desk in layout.Desks) view.Desks.Add(new DeskView
            {
                Index = desk.Index, Id = desk.Id, AssignedOwner = desk.Index < 4 ? desk.Index : PlayerIds.None,
                ScreenOpen = true, ScreenOpenUntil = 70
            });
            foreach (SabotageDef def in config.Sabotage) if (!def.Basic) view.Me.Cards.Add(def.Type);
            for (int s = 0; s < 12; s++) view.Me.SabotageCooldowns.Add(0);
        }

        private DeskView Desk(int index) { return view.Desks.Single(d => d.Index == index); }
        private ItemView Carry(ItemKind kind, MugFill fill = MugFill.Empty)
        {
            view.Players[0].CarryingItemId = "held";
            var item = new ItemView { Id = "held", Kind = kind, Fill = fill, Location = ItemLocationKind.Carried, CarrierId = 0 };
            view.Items.Add(item); return item;
        }
        private List<Affordance> Options(string target)
        {
            Vec3 position = layout.GetStation(target)?.Position ?? layout.GetSlot(target)?.Position ??
                layout.GetDesk(target)?.Position ?? Vec3.Zero;
            return Affordances.For(view, config, layout, target, position);
        }

        [TestCase(ActionVerb.LockScreen, "verb.lock_screen")]
        [TestCase(ActionVerb.SendFakeEmail, "verb.send_fake_email")]
        [TestCase(ActionVerb.PullPlug, "verb.pull_plug")]
        [TestCase(ActionVerb.PlugIn, "verb.plug_in")]
        [TestCase(ActionVerb.SaltCoffee, "verb.salt_coffee")]
        [TestCase(ActionVerb.TasteCoffee, "verb.taste_coffee")]
        [TestCase(ActionVerb.CutInternet, "verb.cut_internet")]
        [TestCase(ActionVerb.RestartRouter, "verb.restart_router")]
        [TestCase(ActionVerb.CutPower, "verb.cut_power")]
        [TestCase(ActionVerb.ResetFuse, "verb.reset_fuse")]
        [TestCase(ActionVerb.Place, "verb.deliver_mail")]
        public void EveryNewWorldVerbHasItsLabelDurationAndTarget(ActionVerb verb, string label)
        {
            string target;
            float duration;
            bool sabotage = false;
            switch (verb)
            {
                case ActionVerb.LockScreen:
                    target = EntityIds.DeskComputer(0); duration = config.Email.LockScreenSeconds; break;
                case ActionVerb.SendFakeEmail:
                    target = EntityIds.DeskComputer(1); duration = config.GetSabotage(SabotageType.FakeEmail).DurationSeconds; sabotage = true; break;
                case ActionVerb.PullPlug:
                    target = EntityIds.Desk(1); duration = config.GetSabotage(SabotageType.PullPlug).DurationSeconds; sabotage = true; break;
                case ActionVerb.PlugIn:
                    target = EntityIds.Desk(1); Desk(1).Unplugged = true; duration = config.GetSabotage(SabotageType.PullPlug).ReverseSeconds; break;
                case ActionVerb.SaltCoffee:
                    target = EntityIds.CoffeeMachine; Carry(ItemKind.Mug, MugFill.Full); duration = config.GetSabotage(SabotageType.SaltCoffee).DurationSeconds; sabotage = true; break;
                case ActionVerb.TasteCoffee:
                    target = "held"; Carry(ItemKind.Mug, MugFill.Full); duration = config.Tasks.TasteSeconds; break;
                case ActionVerb.CutInternet:
                    target = EntityIds.Router; duration = config.GetSabotage(SabotageType.InternetOutage).DurationSeconds; sabotage = true; break;
                case ActionVerb.RestartRouter:
                    target = EntityIds.Router; view.Office.InternetDown = true; duration = config.GetSabotage(SabotageType.InternetOutage).ReverseSeconds; break;
                case ActionVerb.CutPower:
                    target = EntityIds.FuseBox; duration = config.GetSabotage(SabotageType.PowerCut).DurationSeconds; sabotage = true; break;
                case ActionVerb.ResetFuse:
                    target = EntityIds.FuseBox; view.Office.PowerDown = true; duration = config.GetSabotage(SabotageType.PowerCut).ReverseSeconds; break;
                default:
                    target = EntityIds.DeskSurface(1); Carry(ItemKind.Mail).AddressedDesk = 1; duration = config.Interaction.PlaceSurfaceSeconds; break;
            }
            var option = Options(target).Single(a => a.Verb == verb);
            Assert.AreEqual(label, option.LabelKey); Assert.AreEqual(duration, option.Seconds);
            Assert.AreEqual(sabotage, option.IsSabotage); Assert.AreEqual(FailureCode.None, option.BlockedBy);
            Assert.AreEqual(verb == ActionVerb.TasteCoffee ? "" : target, option.TargetId);
            Assert.AreEqual(-1, option.IntParam, "Fake email leaves template selection to the UI.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CardOptionsAreOmittedForNonHoldersAndDisabledFeatures(bool disabled)
        {
            Carry(ItemKind.Mug, MugFill.Full);
            if (disabled) view.Features.Cards = false; else view.Me.Cards.Clear();
            foreach (string target in new[] { EntityIds.DeskComputer(1), EntityIds.Desk(1), EntityIds.CoffeeMachine, EntityIds.Router, EntityIds.FuseBox })
                Assert.IsFalse(Options(target).Any(a => a.Verb == ActionVerb.LockWorkstation || a.Verb == ActionVerb.SendFakeEmail ||
                    a.Verb == ActionVerb.PullPlug || a.Verb == ActionVerb.SaltCoffee || a.Verb == ActionVerb.CutInternet || a.Verb == ActionVerb.CutPower));
            Assert.IsTrue(Options(EntityIds.Desk(1)).Any(a => a.Verb == ActionVerb.SpillCoffee), "Basic sabotage remains available.");
        }

        [Test]
        public void CounterVerbsRequireNoCardsAndUsePublicState()
        {
            view.Me.Cards.Clear(); view.Features.Cards = false;
            view.Office.InternetDown = view.Office.PowerDown = true; Desk(1).Unplugged = true; Desk(1).PcLocked = true;
            Assert.AreEqual(FailureCode.None, Options(EntityIds.Router).Single().BlockedBy);
            Assert.AreEqual(ActionVerb.RestartRouter, Options(EntityIds.Router).Single().Verb);
            Assert.AreEqual(ActionVerb.ResetFuse, Options(EntityIds.FuseBox).Single().Verb);
            Assert.AreEqual(ActionVerb.PlugIn, Options(EntityIds.Desk(1)).Single().Verb);
            Assert.AreEqual(FailureCode.PcUnplugged, Options(EntityIds.DeskComputer(1)).Single().BlockedBy);
        }

        [Test]
        public void PcFailuresUseUnpluggedInternetPasswordScreenOrder()
        {
            Desk(1).Unplugged = true; Desk(1).PcLocked = true; Desk(1).ScreenOpen = false; view.Office.InternetDown = true;
            Func<FailureCode> blocked = () => Options(EntityIds.DeskComputer(1)).Single(a => a.Verb == ActionVerb.SendFakeEmail).BlockedBy;
            Assert.AreEqual(FailureCode.PcUnplugged, blocked());
            Desk(1).Unplugged = false; Assert.AreEqual(FailureCode.NoInternet, blocked());
            view.Office.InternetDown = false; Assert.AreEqual(FailureCode.StationLocked, blocked());
            Desk(1).PcLocked = false; Assert.AreEqual(FailureCode.ScreenLocked, blocked());
            Desk(1).ScreenOpen = true; Assert.AreEqual(FailureCode.None, blocked());
        }

        [Test]
        public void OwnPcWritingAndScreenLockRespectDifferentInternetNeeds()
        {
            view.Office.InternetDown = true;
            var own = Options(EntityIds.DeskComputer(0));
            Assert.AreEqual(FailureCode.NoInternet, own.Single(a => a.Verb == ActionVerb.WriteReport).BlockedBy);
            Assert.AreEqual(FailureCode.None, own.Single(a => a.Verb == ActionVerb.LockScreen).BlockedBy);
            Assert.IsFalse(own.Any(a => a.Verb == ActionVerb.SendFakeEmail || a.Verb == ActionVerb.LockWorkstation));
            Assert.IsFalse(Options(EntityIds.Desk(0)).Any(a => a.Verb == ActionVerb.PullPlug));
        }

        [Test]
        public void UnassignedDesksNeverOfferHostilePcOrPlugActions()
        {
            Assert.IsEmpty(Options(EntityIds.DeskComputer(5)));
            Assert.IsFalse(Options(EntityIds.Desk(5)).Any(a => a.Verb == ActionVerb.PullPlug));
        }

        [Test]
        public void KnownCooldownImmunityAndReservationsAreHinted()
        {
            Desk(1).PcImmuneUntil = view.ServerTime + 5;
            Assert.AreEqual(FailureCode.TargetImmune, Options(EntityIds.DeskComputer(1)).Single(a => a.Verb == ActionVerb.LockWorkstation).BlockedBy);
            Desk(1).PcImmuneUntil = 0;
            view.Me.SabotageCooldowns[(int)SabotageType.FakeEmail] = view.ServerTime + 1;
            Assert.AreEqual(FailureCode.Cooldown, Options(EntityIds.DeskComputer(1)).Single(a => a.Verb == ActionVerb.SendFakeEmail).BlockedBy);
            view.Me.SabotageCooldowns[(int)SabotageType.FakeEmail] = 0;
            view.Stations.Add(new StationView { Id = EntityIds.DeskComputer(1), UsedBy = 2, BusyUntil = 20 });
            Assert.AreEqual(FailureCode.TargetBusy, Options(EntityIds.DeskComputer(1)).Single(a => a.Verb == ActionVerb.SendFakeEmail).BlockedBy);
            view.Players[2].Activity = PublicActivity.Plugging; view.Players[2].ActivityTargetId = EntityIds.Desk(1); view.Players[2].ActivityEnd = 20;
            Assert.AreEqual(FailureCode.TargetBusy, Options(EntityIds.Desk(1)).Single(a => a.Verb == ActionVerb.PullPlug).BlockedBy);
        }

        [Test]
        public void TasteHasNoWorldRangeAndSaltHintsOnlyRecipientKnowledge()
        {
            ItemView mug = Carry(ItemKind.Mug, MugFill.Full);
            var taste = Affordances.For(view, config, layout, "held", layout.BoundsMax + new Vec3(100, 0, 100)).Single(a => a.Verb == ActionVerb.TasteCoffee);
            Assert.AreEqual(FailureCode.None, taste.BlockedBy);
            mug.KnownSalted = true;
            Assert.AreEqual(FailureCode.AlreadyActive, Options(EntityIds.CoffeeMachine).Single(a => a.Verb == ActionVerb.SaltCoffee).BlockedBy);
            mug.KnownSalted = false;
            Assert.AreEqual(FailureCode.None, Options(EntityIds.CoffeeMachine).Single(a => a.Verb == ActionVerb.SaltCoffee).BlockedBy);
        }

        [Test]
        public void MailDeliveryLabelAppliesOnlyToAssignedAddresseeSurface()
        {
            Carry(ItemKind.Mail).AddressedDesk = 1;
            Assert.AreEqual("verb.deliver_mail", Options(EntityIds.DeskSurface(1)).Single().LabelKey);
            Assert.AreEqual("verb.place_surface", Options(EntityIds.DeskSurface(2)).Single().LabelKey);
            Assert.AreEqual("verb.place_drawer", Options(EntityIds.DeskDrawer(1)).Single(a => a.Verb == ActionVerb.Place).LabelKey);
        }

        [Test]
        public void PhoneMeetingAndEmoteActionsNeverBecomeWorldPrompts()
        {
            foreach (string target in Affordances.TargetIds(view, layout))
                Assert.IsFalse(Options(target).Any(a => a.Verb >= ActionVerb.CallPlayer), target);
        }

        [Test]
        public void NewPhysicalOptionsKeepConnectionPhaseAndBusyPriority()
        {
            view.Me.ActiveInteraction = new InteractionView();
            Assert.AreEqual(FailureCode.Busy, Options(EntityIds.FuseBox).Single().BlockedBy);
            view.Phase = MatchPhase.MeetingDiscussion;
            Assert.AreEqual(FailureCode.WrongPhase, Options(EntityIds.FuseBox).Single().BlockedBy);
            view.Players[0].Connected = false;
            Assert.AreEqual(FailureCode.NotConnected, Options(EntityIds.FuseBox).Single().BlockedBy);
        }

        [Test]
        public void OwnReportInOtherDrawerShowsPlantingWarning()
        {
            Carry(ItemKind.Report).ReportOwner = 0;
            Assert.IsTrue(Options(EntityIds.DeskDrawer(1)).Single(a => a.Verb == ActionVerb.Place).IsSabotage);
            Assert.IsFalse(Options(EntityIds.DeskDrawer(0)).Single(a => a.Verb == ActionVerb.Place).IsSabotage);
        }
    }
}
