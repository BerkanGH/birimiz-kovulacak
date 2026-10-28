using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class PlayersV2Tests
    {
        public static MatchSetup Setup(params int[] ids)
        {
            var setup = new MatchSetup { MatchId = 29, Seed = 701 };
            foreach (int id in ids) setup.Players.Add(new PlayerSlotInfo { Id = id, Name = "Player " + id, ColorIndex = id });
            return setup;
        }

        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void PresentPlayersGetDistinctObjectivesAndOnlyAssignedDesksAreOccupied(int count)
        {
            var ids = Enumerable.Range(0, count).Reverse().ToArray();
            CheckRoster(new TestMatch(setup: Setup(ids)), ids.OrderBy(p => p).ToArray(), false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LobbyGapsAndInputOrderingDoNotBecomeEmptyPlayers(bool shuffle)
        {
            var ids = new[] { 5, 0, 3 };
            CheckRoster(new TestMatch(setup: Setup(ids), configure: c => c.ShuffleDesks = shuffle), new[] { 0, 3, 5 }, shuffle);
        }

        static void CheckRoster(TestMatch m, int[] ids, bool shuffle)
        {
            CollectionAssert.AreEqual(ids, m.Engine.Players);
            Assert.AreEqual(1, ids.Count(p => m.View(p).Me.Role == Role.Informant));
            Assert.AreEqual(ids.Length - 1, ids.Where(p => m.View(p).Me.Role == Role.Employee)
                .Select(p => m.View(p).Me.Objective.CardId).Distinct().Count());
            foreach (int player in ids)
            {
                PlayerView view = m.View(player);
                CollectionAssert.AreEqual(ids, view.Players.Select(p => p.Id));
                Assert.AreEqual(12, view.Me.SabotageCooldowns.Count);
                Assert.AreEqual(PlayerIds.None, m.Engine.CallPartnerOf(player));
                int target = view.Me.Objective.Target;
                if (target != PlayerIds.None) { CollectionAssert.Contains(ids, target); Assert.AreNotEqual(player, target); }
                CollectionAssert.AreEquivalent(ids, view.Items.Where(i => i.Kind == ItemKind.Nameplate).Select(i => i.NameplateOf));
                CollectionAssert.AreEquivalent(Enumerable.Range(0, ids.Length), view.Players.Select(p => p.DeskIndex));
                for (int desk = 0; desk < ids.Length; desk++)
                {
                    DeskView d = view.Desks.Single(x => x.Index == desk);
                    if (!shuffle) Assert.AreEqual(ids[desk], d.AssignedOwner);
                    ItemView plate = view.Items.Single(i => i.Kind == ItemKind.Nameplate && i.SlotId == EntityIds.DeskNameplate(desk));
                    Assert.AreEqual(d.AssignedOwner, plate.NameplateOf);
                }
                foreach (DeskView desk in view.Desks.Where(d => d.Index >= ids.Length))
                {
                    Assert.AreEqual(PlayerIds.None, desk.AssignedOwner);
                    Assert.AreEqual(PlayerIds.None, desk.DisplayedNameplateOf);
                    Assert.IsFalse(view.Items.Any(i => i.SlotId == EntityIds.DeskNameplate(desk.Index)));
                }
            }
            Assert.AreEqual(PlayerIds.None, m.Engine.CallPartnerOf(-1));
            Assert.AreEqual(PlayerIds.None, m.Engine.CallPartnerOf(Protocol.MaxPlayers));
            m.Finish();
            CollectionAssert.AreEqual(ids, m.View(ids[0]).Result.Players.Select(p => p.Player));
            Assert.IsFalse(m.View(ids[0]).Observations.Any(o => o.DeskIndex >= ids.Length));
            Assert.IsFalse(m.Engine.AllRematchVotesIn);
            foreach (int player in ids) m.Do(player, ActionVerb.RematchVote);
            Assert.IsTrue(m.Engine.AllRematchVotesIn);
        }

        [Test]
        public void UnassignedDeskAcceptsItemsButCannotBeLocked()
        {
            var m = new TestMatch(setup: Setup(0, 3, 5)); m.Work();
            string emptyDesk = EntityIds.DeskSurface(3);
            m.Do(0, ActionVerb.PickUp, EntityIds.Mug(0));
            m.Do(0, ActionVerb.Place, emptyDesk);
            Assert.AreEqual(emptyDesk, m.Item(EntityIds.Mug(0)).SlotId);
            m.MoveTo(5, EntityIds.DeskComputer(3));
            Assert.AreEqual(FailureCode.InvalidTarget, m.Request(5, ActionVerb.LockWorkstation, EntityIds.DeskComputer(3)).Failure);
            Assert.IsEmpty(m.Engine.BuildHostDebugView().Incidents);
        }

        [Test]
        public void AbsentIdsCannotSubmitOrAccuseAndDoNotHaveNameplateBytes()
        {
            var m = new TestMatch(setup: Setup(0, 3, 5)); m.Work();
            Assert.AreEqual(FailureCode.NotConnected,
                m.Engine.Submit(1, new ActionRequest(m.Engine.MatchId, 17, ActionVerb.CancelInteraction), m.Now).Failure);
            foreach (int recipient in m.Engine.Players)
            {
                string encoded = Encoding.UTF8.GetString(WireCodec.EncodeView(m.View(recipient)));
                foreach (int absent in new[] { 1, 2, 4 }) Assert.IsFalse(encoded.Contains(EntityIds.Nameplate(absent)));
            }
            m.Phase(MatchPhase.MeetingDiscussion);
            Assert.AreEqual(FailureCode.InvalidTarget, m.Request(0, ActionVerb.Accuse, param: 1).Failure);
        }

        [Test]
        public void InvalidRostersAndInsufficientDesksThrowArgumentException()
        {
            var invalid = new[] { new[] { 0, 1 }, new[] { 0, 1, 2, 3, 4, 5, 6 }, new[] { 0, 0, 2 },
                new[] { -1, 1, 2 }, new[] { 0, 1, 6 } };
            foreach (int[] ids in invalid)
                Assert.Throws<ArgumentException>(() => new MatchEngine(MatchConfig.CreateDefault(), DefaultOffice.Create(), Setup(ids), OpenWorld.Instance));
            var layout = DefaultOffice.Create();
            layout.Desks.RemoveRange(2, layout.Desks.Count - 2);
            Assert.Throws<ArgumentException>(() => new MatchEngine(MatchConfig.CreateDefault(), layout, Setup(0, 2, 5), OpenWorld.Instance));
        }

        [TestCase(3, false)]
        [TestCase(5, false)]
        [TestCase(6, false)]
        [TestCase(3, true)]
        [TestCase(5, true)]
        public void FullScriptedMatchCoversLegacyTasksSabotageAndDeterminism(int count, bool gaps)
        {
            int[] ids = gaps ? (count == 3 ? new[] { 0, 2, 5 } : new[] { 0, 2, 3, 4, 5 }) : Enumerable.Range(0, count).ToArray();
            TestMatch first = Script(ids), second = Script(ids);
            Assert.AreEqual(first.View().Result.ResultHash, second.View().Result.ResultHash);
            CollectionAssert.AreEqual(WireCodec.EncodeEvents(first.Events), WireCodec.EncodeEvents(second.Events));
            Assert.IsTrue(first.View().Result.CompanySucceeded);
            Assert.AreEqual(1, first.View().Result.Players.Count(p => p.Fired));
            foreach (int player in ids) Assert.AreEqual(first.View().Result.ResultHash, first.View(player).Result.ResultHash);
            foreach (TaskType type in new[] { TaskType.Coffee, TaskType.Report, TaskType.Phone, TaskType.CleanDesk })
                Assert.IsTrue(first.Events.Any(e => e.Type == EventType.TaskCompleted && e.IntArg == (int)type), type.ToString());
            foreach (SabotageType type in new[] { SabotageType.NameplateSwap, SabotageType.PlantItem, SabotageType.LockWorkstation,
                SabotageType.HideReport, SabotageType.SpillCoffee, SabotageType.MoveClutter, SabotageType.EvidenceTampering })
                Assert.IsTrue(first.View().Result.Incidents.Any(i => i.Type == type), type.ToString());
        }

        static TestMatch Script(int[] ids)
        {
            int worker = ids[0], helper = ids[1], actor = ids[2], informant = ids[ids.Length - 1];
            MatchSetup setup = Setup(ids); setup.ForceInformant = informant;
            var m = new TestMatch(setup: setup, configure: c => { c.QuotaTarget = 5; c.Tasks.InitialDirtyDesks = ids.Length; });
            m.Work();
            m.Coffee(worker, 0);
            string report = m.Print(worker);
            m.Do(actor, ActionVerb.PickUp, report);
            m.Do(actor, ActionVerb.Place, EntityIds.HideBehindPrinter);
            m.Do(worker, ActionVerb.SearchContainer, EntityIds.HideBehindPrinter);
            m.Do(worker, ActionVerb.PickUp, report);
            m.Do(worker, ActionVerb.Place, EntityIds.DeliveryTray);
            for (int i = 0; !m.View().Quota.PhoneRinging && i < 100; i++) m.Advance(0.25);
            m.Do(worker, ActionVerb.AnswerPhone, EntityIds.Phone);
            m.Do(worker, ActionVerb.PickUp, EntityIds.Spray(0));
            m.Do(worker, ActionVerb.CleanDesk, EntityIds.Desk(m.Player(worker).DeskIndex));
            m.Do(worker, ActionVerb.Drop);
            m.Plant(actor, m.Player(worker).DeskIndex, drawer: false);
            m.SwapInto(helper, helper, m.Player(worker).DeskIndex);
            m.Do(helper, ActionVerb.Drop);
            m.Do(actor, ActionVerb.LockWorkstation, EntityIds.DeskComputer(m.Player(helper).DeskIndex));
            m.Do(worker, ActionVerb.UnlockWorkstation, EntityIds.DeskComputer(m.Player(helper).DeskIndex));
            m.Do(helper, ActionVerb.PickUp, EntityIds.Mug(1));
            m.Do(helper, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine);
            m.Do(helper, ActionVerb.SpillCoffee, EntityIds.Desk(m.Player(actor).DeskIndex));
            m.Do(helper, ActionVerb.Drop);
            m.Do(worker, ActionVerb.PickUp, EntityIds.Clutter(0));
            m.Do(worker, ActionVerb.Place, EntityIds.DeskSurface(m.Player(actor).DeskIndex));
            m.Phase(MatchPhase.Raid); m.Advance(12);
            EvidenceView duck = m.FindEvidence(EntityIds.Suspicious(ItemVariants.Duck));
            m.Do(informant, ActionVerb.AuditScanEvidence, param: duck.Id);
            m.Do(helper, ActionVerb.PickUp, EntityIds.Suspicious(ItemVariants.Duck));
            m.Do(helper, ActionVerb.Place, EntityIds.JunkBin);
            m.Phase(MatchPhase.MeetingDiscussion);
            foreach (int player in ids.Where(p => p != actor)) m.Do(player, ActionVerb.Accuse, param: actor);
            m.Do(informant, ActionVerb.PresentNote, param: m.View(informant).Me.Informant.Scans.Single().NoteId);
            m.Phase(MatchPhase.MeetingVote);
            foreach (int player in ids) m.Do(player, ActionVerb.CastBallot, param: player == actor ? PlayerIds.None : actor);
            m.Finish();
            return m;
        }
    }
}
