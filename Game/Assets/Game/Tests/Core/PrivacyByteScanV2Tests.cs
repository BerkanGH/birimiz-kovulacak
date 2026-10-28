using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class PrivacyByteScanV2Tests
    {
        private static MatchSetup Setup(int count = 4)
        {
            MatchSetup setup = TestMatch.Setup(8431);
            setup.Players.Clear();
            for (int p = 0; p < count; p++) setup.Players.Add(new PlayerSlotInfo { Id = p, Name = "Player " + p });
            setup.ForceInformant = count - 1;
            setup.ForceCards = new List<List<SabotageType>>();
            for (int p = 0; p < Protocol.MaxPlayers; p++) setup.ForceCards.Add(new List<SabotageType>
            {
                SabotageType.LockWorkstation, SabotageType.FakeEmail, SabotageType.PullPlug,
                SabotageType.SaltCoffee, SabotageType.InternetOutage, SabotageType.PowerCut
            });
            return setup;
        }

        private static TestMatch Match(MatchSetup setup = null)
        {
            return new TestMatch(setup: setup ?? Setup(), configure: c =>
            {
                c.Tasks.MailEnabled = false;
                c.Tasks.PhoneIntervalMin = c.Tasks.PhoneIntervalMax = 1000;
            });
        }

        private static byte[] VisibleBytes(TestMatch match, int recipient)
        {
            PlayerView view = match.View(recipient);
            // Version counts authoritative mutations, including secrets, but carries no secret payload.
            view.StateVersion = 0;
            return WireCodec.EncodeView(view);
        }

        private static byte[] EventBytes(TestMatch match, int recipient)
        {
            return WireCodec.EncodeEvents(match.Events.Where(e => e.IsVisibleTo(recipient)).ToList());
        }

        private static void SameRecipientBytes(TestMatch a, TestMatch b, int recipient)
        {
            Assert.AreEqual(a.Now, b.Now, .0000001);
            CollectionAssert.AreEqual(VisibleBytes(a, recipient), VisibleBytes(b, recipient), "Recipient view must not encode the varied secret.");
            CollectionAssert.AreEqual(EventBytes(a, recipient), EventBytes(b, recipient), "Recipient events must not encode the varied secret.");
        }

        private static bool Contains(byte[] bytes, byte[] secret)
        {
            for (int start = 0; start <= bytes.Length - secret.Length; start++)
            {
                bool match = true;
                for (int i = 0; i < secret.Length; i++) if (bytes[start + i] != secret[i]) { match = false; break; }
                if (match) return true;
            }
            return false;
        }

        private static byte[] CallFingerprint(CallRecordView call)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(true);
                writer.Write(call.Caller); writer.Write(call.Callee); writer.Write(call.Start); writer.Write(call.End);
                writer.Write(call.Answered); writer.Write(call.Tip); writer.Write(call.TipDesk);
                return stream.ToArray();
            }
        }

        [Test]
        public void OtherPlayersCardHandsDoNotChangeAnyRecipientBytes()
        {
            MatchSetup first = Setup(), second = Setup();
            first.ForceCards[0] = new List<SabotageType>
            {
                SabotageType.PowerCut, SabotageType.SaltCoffee, SabotageType.LockWorkstation,
                SabotageType.FakeEmail, SabotageType.InternetOutage, SabotageType.PullPlug
            };
            second.ForceCards[0] = new List<SabotageType> { SabotageType.FakeEmail };
            first.ForceCards[3].Clear(); second.ForceCards[3].Clear();
            var a = Match(first); var b = Match(second);
            SameRecipientBytes(a, b, 3);
            byte[] hand = new byte[] { 7, (byte)SabotageType.PowerCut, (byte)SabotageType.SaltCoffee,
                (byte)SabotageType.LockWorkstation, (byte)SabotageType.FakeEmail, (byte)SabotageType.InternetOutage, (byte)SabotageType.PullPlug };
            Assert.IsFalse(Contains(VisibleBytes(a, 3), hand));
            Assert.IsTrue(Contains(VisibleBytes(a, 0), hand));
            Assert.AreNotEqual(a.View(0).Me.Cards.Count, b.View(0).Me.Cards.Count);
        }

        [Test]
        public void TrueEmailSenderAndImmutableIncidentActorDoNotChangeRecipientBytes()
        {
            var a = Match(); var b = Match();
            a.Work(); b.Work();
            a.Do(0, ActionVerb.WriteReport, EntityIds.DeskComputer(0));
            b.Do(0, ActionVerb.WriteReport, EntityIds.DeskComputer(0));
            a.Do(1, ActionVerb.SendFakeEmail, EntityIds.DeskComputer(0), 7);
            b.Do(2, ActionVerb.SendFakeEmail, EntityIds.DeskComputer(0), 7);
            Assert.AreEqual(1, a.Engine.BuildHostDebugView().Incidents.Single().Actor);
            Assert.AreEqual(2, b.Engine.BuildHostDebugView().Incidents.Single().Actor);
            SameRecipientBytes(a, b, 3);
            Assert.AreEqual(0, a.View(3).Inbox.Single().FromPlayer);
            Assert.IsNull(a.View(3).Result);
        }

        [Test]
        public void SaltStateDoesNotChangeUninformedRecipientsBytes()
        {
            var a = Match(); var b = Match();
            a.Work(); b.Work();
            a.Do(0, ActionVerb.PickUp, EntityIds.Mug(0)); b.Do(0, ActionVerb.PickUp, EntityIds.Mug(0));
            a.Do(0, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine); b.Do(0, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine);
            a.Do(0, ActionVerb.SaltCoffee, EntityIds.CoffeeMachine); b.AdvanceTo(a.Now);
            SameRecipientBytes(a, b, 3);
            Assert.IsTrue(a.Item(EntityIds.Mug(0), 0).KnownSalted);
            Assert.IsFalse(a.Item(EntityIds.Mug(0), 3).KnownSalted);
            Assert.IsNotEmpty(a.Engine.BuildHostDebugView().SaltedItems);
            Assert.IsEmpty(b.Engine.BuildHostDebugView().SaltedItems);
        }

        [Test]
        public void SaltedTasteNoteAndEventArePrivate()
        {
            var a = Match(); var b = Match();
            foreach (TestMatch match in new[] { a, b })
            {
                match.Work(); match.Do(0, ActionVerb.PickUp, EntityIds.Mug(0));
                match.Do(0, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine);
            }
            a.Do(0, ActionVerb.SaltCoffee, EntityIds.CoffeeMachine); b.AdvanceTo(a.Now);
            a.Do(0, ActionVerb.TasteCoffee); b.Do(0, ActionVerb.TasteCoffee);
            SameRecipientBytes(a, b, 3);
            Assert.IsTrue(a.View(0).Me.Notes.Single(n => n.Kind == NoteKind.TasteResult).Salted);
            Assert.IsFalse(b.View(0).Me.Notes.Single(n => n.Kind == NoteKind.TasteResult).Salted);
            Assert.AreEqual(1, a.Events.Last(e => e.Type == EventType.CoffeeTasted).IntArg);
            Assert.IsFalse(a.Events.Last(e => e.Type == EventType.CoffeeTasted).IsVisibleTo(3));
        }

        [Test]
        public void TipDeskIsAbsentFromRecipientBytesDuringAndAfterTip()
        {
            var a = Match(); var b = Match(); a.Work(); b.Work();
            ActionResult tipA = a.Start(0, ActionVerb.TipManager, EntityIds.Desk(1));
            ActionResult tipB = b.Start(0, ActionVerb.TipManager, EntityIds.Desk(2));
            SameRecipientBytes(a, b, 3);
            Assert.AreEqual("", a.Player(0).ActivityTargetId);
            a.AdvanceTo(tipA.CompletesAt + .0001); b.AdvanceTo(tipB.CompletesAt + .0001);
            SameRecipientBytes(a, b, 3);
            CallRecordView record = a.Engine.BuildHostDebugView().Calls.Single();
            Assert.AreEqual(1, record.TipDesk);
            Assert.IsFalse(Contains(VisibleBytes(a, 3), CallFingerprint(record)));
        }

        [Test]
        public void TipsterIdentityDoesNotChangeIdleRecipientBytes()
        {
            var a = Match(); var b = Match(); a.Work(); b.Work();
            a.Do(0, ActionVerb.TipManager, EntityIds.Desk(2));
            b.Do(1, ActionVerb.TipManager, EntityIds.Desk(2));
            SameRecipientBytes(a, b, 3);
            Assert.AreEqual(0, a.Engine.BuildHostDebugView().Calls.Single().Caller);
            Assert.AreEqual(1, b.Engine.BuildHostDebugView().Calls.Single().Caller);
        }

        [Test]
        public void ActiveCallPartnerPairingsAreAbsentFromOtherPlayersBytes()
        {
            var a = Match(Setup(6)); var b = Match(Setup(6)); a.Work(); b.Work();
            // The same two incoming phones ring in the same order. All four callers end active.
            Assert.IsTrue(a.Request(0, ActionVerb.CallPlayer, param: 1).Accepted);
            Assert.IsTrue(a.Request(2, ActionVerb.CallPlayer, param: 3).Accepted);
            Assert.IsTrue(b.Request(2, ActionVerb.CallPlayer, param: 1).Accepted);
            Assert.IsTrue(b.Request(0, ActionVerb.CallPlayer, param: 3).Accepted);
            foreach (TestMatch match in new[] { a, b })
            {
                Assert.IsTrue(match.Request(1, ActionVerb.AnswerCall).Accepted);
                Assert.IsTrue(match.Request(3, ActionVerb.AnswerCall).Accepted);
            }
            SameRecipientBytes(a, b, 5);
            Assert.AreEqual(1, a.Engine.CallPartnerOf(0));
            Assert.AreEqual(3, b.Engine.CallPartnerOf(0));
            foreach (CallRecordView record in a.Engine.BuildHostDebugView().Calls)
                Assert.IsFalse(Contains(VisibleBytes(a, 5), CallFingerprint(record)));
            Assert.AreEqual(1, a.View(0).Me.Call.Other);
            Assert.AreEqual(3, b.View(0).Me.Call.Other);
        }

        [Test]
        public void PulledCallLogIsByteAbsentUntilInformantPresentsIt()
        {
            var match = Match(); match.Work();
            Assert.IsTrue(match.Request(0, ActionVerb.CallPlayer, param: 1).Accepted);
            Assert.IsTrue(match.Request(1, ActionVerb.AnswerCall).Accepted);
            match.Advance(1);
            Assert.IsTrue(match.Request(0, ActionVerb.HangUp).Accepted);
            CallRecordView record = match.Engine.BuildHostDebugView().Calls.Single();
            byte[] fingerprint = CallFingerprint(record);
            Assert.IsTrue(match.Request(3, ActionVerb.PullCallLogs).Accepted);
            Assert.IsTrue(Contains(VisibleBytes(match, 3), fingerprint));
            Assert.IsFalse(Contains(VisibleBytes(match, 2), fingerprint));
            Assert.IsFalse(Contains(EventBytes(match, 2), fingerprint));
            int note = match.View(3).Me.Notes.Single(n => n.Kind == NoteKind.CallLog).Id;
            match.Phase(MatchPhase.MeetingDiscussion);
            Assert.IsTrue(match.Request(3, ActionVerb.PresentNote, param: note).Accepted);
            Assert.IsTrue(Contains(VisibleBytes(match, 2), fingerprint), "Presented statements intentionally reveal the record.");
        }

        [Test]
        public void PositionLogCoordinatesNeverAppearInUnrequestedViewsOrEvents()
        {
            var a = Match(); var b = Match(); a.Work(); b.Work();
            Vec3 secret = a.Layout.BoundsMax + new Vec3(1234.125f, 678.625f, 901.375f);
            Vec3 other = b.Layout.GetDesk(0).SeatPosition;
            a.Move(0, secret); b.Move(0, other);
            a.Advance(2); b.Advance(2);
            SameRecipientBytes(a, b, 3);
            byte[] xyz = BitConverter.GetBytes(secret.X).Concat(BitConverter.GetBytes(secret.Y)).Concat(BitConverter.GetBytes(secret.Z)).ToArray();
            Assert.IsFalse(Contains(VisibleBytes(a, 3), xyz));
            Assert.IsFalse(Contains(EventBytes(a, 3), xyz));
            Assert.IsFalse(a.View(3).Evidence.Any(e => e.Kind == EvidenceKind.CctvFootage));
        }
    }
}
