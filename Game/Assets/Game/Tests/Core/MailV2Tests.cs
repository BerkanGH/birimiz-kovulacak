using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class MailV2Tests
    {
        [Test]
        public void BriefingMailHasStableIdsAndOnlyAssignedAddresseesWithGappedPlayers()
        {
            MatchSetup setup = TestMatch.Setup(force: false);
            setup.Players.RemoveAll(p => p.Id == 0); setup.Players.Add(new PlayerSlotInfo { Id = 5, Name = "P5" });
            var m = new TestMatch(setup: setup, configure: c => { c.Tasks.InitialMail = 3; c.Tasks.MaxOpenMail = 3; });
            var mail = m.View(1).Items.Where(i => i.Kind == ItemKind.Mail).ToList();
            Assert.AreEqual(3, mail.Count); Assert.AreEqual(3, m.View(1).Quota.OpenMail);
            CollectionAssert.AreEqual(new[] { EntityIds.Mail(1), EntityIds.Mail(2), EntityIds.Mail(3) }, mail.Select(i => i.Id));
            Assert.IsTrue(mail.All(i => i.Variant == ItemVariants.Mail && i.SlotId == EntityIds.Mailroom));
            Assert.IsTrue(mail.All(i => m.View(1).Desks.Single(d => d.Index == i.AddressedDesk).AssignedOwner != PlayerIds.None));
            Assert.AreEqual(3, m.Events.Count(e => e.Type == EventType.MailArrived));
        }

        [Test]
        public void MailCapCountsRoomAndCarriedButNotFloorAndStopsOutsideWorkPeriods()
        {
            var m = new TestMatch(configure: c => { c.Tasks.MaxOpenMail = 1; c.Tasks.MailInterval = 2; });
            m.Work(); m.Do(0, ActionVerb.PickUp, EntityIds.Mail(1)); m.Advance(4);
            Assert.AreEqual(1, m.View().Quota.OpenMail);
            Assert.AreEqual(1, m.Events.Count(e => e.Type == EventType.MailArrived));
            m.Do(0, ActionVerb.Drop); Assert.AreEqual(0, m.View().Quota.OpenMail); m.Advance(2.1);
            Assert.AreEqual(1, m.View().Quota.OpenMail);
            m.Phase(MatchPhase.Raid); m.Do(0, ActionVerb.PickUp, EntityIds.Mail(2)); m.Do(0, ActionVerb.Drop);
            int before = m.Events.Count(e => e.Type == EventType.MailArrived); m.Advance(5);
            Assert.AreEqual(before, m.Events.Count(e => e.Type == EventType.MailArrived));
            m.Phase(MatchPhase.Work2); m.Advance(2.1);
            Assert.AreEqual(before + 1, m.Events.Count(e => e.Type == EventType.MailArrived));
            m.Phase(MatchPhase.FinalInspection); m.Do(0, ActionVerb.PickUp, EntityIds.Mail(3)); m.Do(0, ActionVerb.Drop); m.Finish();
            Assert.AreEqual(before + 1, m.Events.Count(e => e.Type == EventType.MailArrived));
        }

        [Test]
        public void AddressedSurfaceDeliveryAwardsOnceArchivesAndEarnsTaskFavor()
        {
            var m = new TestMatch(configure: c => { c.Tasks.MailInterval = 1000; c.ManagerProfiles[0].FavorSource = FavorSource.AnyTask; });
            m.Work(); ItemView mail = m.Item(EntityIds.Mail(1));
            m.Do(0, ActionVerb.PickUp, mail.Id);
            int wrong = (mail.AddressedDesk + 1) % m.Engine.Players.Count;
            m.Do(0, ActionVerb.Place, EntityIds.DeskSurface(wrong));
            Assert.AreEqual(0, m.Player(0).TasksCompleted);
            m.Do(0, ActionVerb.PickUp, mail.Id);
            string target = EntityIds.DeskSurface(mail.AddressedDesk); m.MoveTo(0, target);
            var request = new ActionRequest(m.Engine.MatchId, 9000, ActionVerb.Place, target);
            ActionResult start = m.Engine.Submit(0, request, m.Now); Assert.IsTrue(start.Accepted);
            Assert.AreEqual(m.Config.Interaction.PlaceSurfaceSeconds, start.CompletesAt - m.Now, 0.00001);
            Assert.AreEqual(FailureCode.Duplicate, m.Engine.Submit(0, request, m.Now).Failure);
            m.AdvanceTo(start.CompletesAt + 0.001);
            Assert.IsNull(m.Item(mail.Id)); Assert.AreEqual(0, m.View().Quota.OpenMail);
            Assert.AreEqual(m.Config.Tasks.MailPoints, m.View().Quota.Points);
            Assert.AreEqual(m.Config.Tasks.MailPoints, m.Player(0).Performance);
            Assert.AreEqual(1, m.Player(0).TasksCompleted); Assert.AreEqual(1, m.Player(0).Favor);
            Assert.IsTrue(m.View().Me.Objective.Flags.Contains("mail"));
            Assert.AreEqual(FailureCode.HandsEmpty, m.Request(0, ActionVerb.Place, target).Failure);
        }

        [Test]
        public void MailDeliveryRevalidatesMovementAndMeetingBoundary()
        {
            var m = new TestMatch(); m.Work(); ItemView mail = m.Item(EntityIds.Mail(1));
            m.Do(0, ActionVerb.PickUp, mail.Id);
            m.Start(0, ActionVerb.Place, EntityIds.DeskSurface(mail.AddressedDesk));
            m.Move(0, m.Layout.BoundsMax); m.Advance(2);
            Assert.AreEqual(0, m.Player(0).TasksCompleted); Assert.AreEqual(mail.Id, m.Player(0).CarryingItemId);
            m.Phase(MatchPhase.FinalInspection); m.AdvanceTo(m.View().PhaseEndTime - 0.1);
            m.Start(0, ActionVerb.Place, EntityIds.DeskSurface(mail.AddressedDesk));
            m.Phase(MatchPhase.MeetingDiscussion);
            Assert.AreEqual(0, m.Player(0).TasksCompleted); Assert.AreEqual(ItemLocationKind.Floor, m.Item(mail.Id).Location);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MailFeatureUsesBothConfigAndWeekDay(bool week)
        {
            var setup = TestMatch.Setup(); setup.Mode = week ? GameMode.Week : GameMode.QuickPlay; setup.ManagerProfileId = "legacy_test";
            var m = new TestMatch(setup: setup, configure: c => { if (week) c.WeekDays[0].Mail = false; else c.Tasks.MailEnabled = false; });
            Assert.IsFalse(m.View().Features.Mail); m.Work(); m.Advance(65);
            Assert.IsFalse(m.View().Items.Any(i => i.Kind == ItemKind.Mail)); Assert.AreEqual(0, m.View().Quota.OpenMail);
        }

        [Test]
        public void OfficeAllRounderRequiresAllFiveDefaultTaskTypes()
        {
            var m = new TestMatch(); m.Work(); m.Coffee(0, 0);
            string report = m.Print(0); m.Do(0, ActionVerb.PickUp, report); m.Do(0, ActionVerb.Place, EntityIds.DeliveryTray);
            while (!m.View().Quota.PhoneRinging) m.Advance(0.2);
            m.Do(0, ActionVerb.AnswerPhone, EntityIds.Phone);
            ItemView mail = m.Item(EntityIds.Mail(1)); m.Do(0, ActionVerb.PickUp, mail.Id); m.Do(0, ActionVerb.Place, EntityIds.DeskSurface(mail.AddressedDesk));
            Assert.AreEqual(5, m.View().Me.Objective.Required);
            Assert.AreEqual(4, m.View().Me.Objective.Progress); Assert.AreEqual(ObjectiveStatus.InProgress, m.View().Me.Objective.Status);
            m.Do(0, ActionVerb.PickUp, EntityIds.Spray(0));
            int dirty = m.View().Desks.First(d => d.Dirty).Index; m.Do(0, ActionVerb.CleanDesk, EntityIds.Desk(dirty));
            Assert.AreEqual(5, m.View().Me.Objective.Progress); Assert.AreEqual(ObjectiveStatus.OnTrack, m.View().Me.Objective.Status);
        }

        [TestCase(MatchPhase.Briefing)]
        [TestCase(MatchPhase.Work1)]
        [TestCase(MatchPhase.Raid)]
        [TestCase(MatchPhase.Work2)]
        [TestCase(MatchPhase.FinalInspection)]
        [TestCase(MatchPhase.MeetingDiscussion)]
        [TestCase(MatchPhase.MeetingDefense1)]
        [TestCase(MatchPhase.MeetingDefense2)]
        [TestCase(MatchPhase.MeetingVote)]
        [TestCase(MatchPhase.MeetingVerdict)]
        [TestCase(MatchPhase.Results)]
        public void EmotesWorkInEveryNonAbandonedPhase(MatchPhase phase)
        {
            var m = new TestMatch(); m.Phase(phase);
            Assert.IsTrue(m.Request(0, ActionVerb.Emote, param: (int)EmoteId.Facepalm).Accepted);
            Assert.AreEqual((int)EmoteId.Facepalm, m.Player(0).Emote);
            Assert.AreEqual(m.Now + 2.5, m.Player(0).EmoteUntil);
            Assert.IsTrue(m.Events.Any(e => e.Type == EventType.Emote && e.Actor == 0 && e.IntArg == (int)EmoteId.Facepalm && e.Audience == Audience.All));
        }

        [Test]
        public void EmoteCooldownDoesNotBlockWorkAndInvalidInputNeverThrows()
        {
            var m = new TestMatch(); m.Work(); m.Start(0, ActionVerb.WriteReport, EntityIds.DeskComputer(0));
            Assert.IsTrue(m.Request(0, ActionVerb.Emote, param: 0).Accepted); Assert.IsNotNull(m.View().Me.ActiveInteraction);
            Assert.AreEqual(FailureCode.Cooldown, m.Request(0, ActionVerb.Emote, param: 1).Failure);
            m.Advance(2.501); Assert.AreEqual(-1, m.Player(0).Emote);
            Assert.IsTrue(m.Request(0, ActionVerb.Emote, param: 4).Accepted);
            foreach (int invalid in new[] { -1, 5, int.MinValue, int.MaxValue })
                Assert.AreEqual(FailureCode.InvalidTarget, m.Request(0, ActionVerb.Emote, param: invalid).Failure);
            m.Engine.Abandon(AbandonReason.HostAborted, m.Now);
            Assert.AreEqual(FailureCode.WrongPhase, m.Request(0, ActionVerb.Emote, param: 0).Failure);
        }
    }
}
