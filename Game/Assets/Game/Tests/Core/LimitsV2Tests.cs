using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class LimitsV2Tests
    {
        private static TestMatch Match(Action<MatchConfig> configure = null, params int[] ids)
        {
            if (ids.Length == 0) ids = new[] { 0, 1, 2, 3, 4, 5 };
            MatchSetup setup = PlayersV2Tests.Setup(ids); setup.ForceInformant = ids.Max();
            setup.ForceCards = new List<List<SabotageType>>();
            for (int p = 0; p < Protocol.MaxPlayers; p++) setup.ForceCards.Add(new List<SabotageType>
            {
                SabotageType.LockWorkstation, SabotageType.FakeEmail, SabotageType.PullPlug,
                SabotageType.SaltCoffee, SabotageType.InternetOutage, SabotageType.PowerCut
            });
            return new TestMatch(setup: setup, configure: c => { c.Tasks.MailEnabled = false; if (configure != null) configure(c); });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OfficeWideCompletionCapRevalidatesAcrossActorsAndIndependentTargets(bool card)
        {
            SabotageType type = card ? SabotageType.PullPlug : SabotageType.PlantItem;
            TestMatch m = Match(c => c.GetSabotage(type).MaxPerMatch = 1); m.Work();
            if (!card)
            {
                m.Do(0, ActionVerb.PickUp, EntityIds.Suspicious(ItemVariants.Duck));
                m.Do(3, ActionVerb.PickUp, EntityIds.Suspicious(ItemVariants.Stapler));
            }
            ActionVerb verb = card ? ActionVerb.PullPlug : ActionVerb.Place;
            string firstTarget = card ? EntityIds.Desk(1) : EntityIds.DeskDrawer(1);
            string secondTarget = card ? EntityIds.Desk(2) : EntityIds.DeskDrawer(2);
            ActionResult first = m.Start(0, verb, firstTarget);
            ActionResult second = m.Start(3, verb, secondTarget);
            Assert.AreEqual(first.CompletesAt, second.CompletesAt);
            m.AdvanceTo(first.CompletesAt + 0.001);
            List<IncidentReveal> incidents = m.Engine.BuildHostDebugView().Incidents.Where(i => i.Type == type).ToList();
            Assert.AreEqual(1, incidents.Count); Assert.AreEqual(0, incidents[0].Actor);
            Assert.IsNull(m.View(3).Me.ActiveInteraction);
            Assert.IsTrue(m.Events.Any(e => e.Type == EventType.InteractionCancelled && e.Actor == 3));
            Assert.AreEqual(0, m.View(3).Me.SabotageCooldowns[(int)type]);
            Assert.AreEqual(FailureCode.LimitReached, m.Request(3, verb, secondTarget).Failure);
        }

        [Test]
        public void CombinedActorLimitIncludesOfficeWideEffectsAndRestorationReleasesCapacity()
        {
            TestMatch m = Match(c => { c.AntiGrief.MaxActiveEffectsPerActor = 1; c.GetSabotage(SabotageType.PowerCut).EffectSeconds = 100; }); m.Work();
            m.Do(0, ActionVerb.CutPower, EntityIds.FuseBox);
            m.MoveTo(0, EntityIds.Desk(1));
            Assert.AreEqual(FailureCode.LimitReached, m.Request(0, ActionVerb.PullPlug, EntityIds.Desk(1)).Failure);
            m.MoveTo(0, EntityIds.Router);
            Assert.AreEqual(FailureCode.LimitReached, m.Request(0, ActionVerb.CutInternet, EntityIds.Router).Failure);
            m.Do(5, ActionVerb.ResetFuse, EntityIds.FuseBox);
            m.Do(0, ActionVerb.PullPlug, EntityIds.Desk(1));
            Assert.AreEqual(2, m.Engine.BuildHostDebugView().Incidents.Count);
            Assert.AreEqual(5, m.Engine.BuildHostDebugView().Incidents.First().ReversedBy);
        }

        [Test]
        public void VictimCombinedLimitCountsDifferentNewCardTypes()
        {
            TestMatch m = Match(c => c.AntiGrief.MaxActiveEffectsPerVictim = 1); m.Work();
            m.Do(1, ActionVerb.WriteReport, EntityIds.DeskComputer(1));
            m.Do(0, ActionVerb.SendFakeEmail, EntityIds.DeskComputer(1), 0);
            m.MoveTo(3, EntityIds.Desk(1));
            Assert.AreEqual(FailureCode.TargetImmune, m.Request(3, ActionVerb.PullPlug, EntityIds.Desk(1)).Failure);
            m.MoveTo(3, EntityIds.DeskComputer(1));
            Assert.AreEqual(FailureCode.TargetImmune, m.Request(3, ActionVerb.LockWorkstation, EntityIds.DeskComputer(1)).Failure);
            m.Do(3, ActionVerb.PullPlug, EntityIds.Desk(2));
        }

        [Test]
        public void SameVictimCooldownSurvivesPlugRestorationButOtherActorCanAct()
        {
            TestMatch m = Match(c => c.GetSabotage(SabotageType.PullPlug).ActorCooldownSeconds = 0); m.Work();
            m.Do(0, ActionVerb.PullPlug, EntityIds.Desk(1)); m.Do(5, ActionVerb.PlugIn, EntityIds.Desk(1));
            m.MoveTo(0, EntityIds.Desk(1));
            Assert.AreEqual(FailureCode.TargetImmune, m.Request(0, ActionVerb.PullPlug, EntityIds.Desk(1)).Failure);
            m.Do(3, ActionVerb.PullPlug, EntityIds.Desk(1)); m.Do(5, ActionVerb.PlugIn, EntityIds.Desk(1));
            m.Do(0, ActionVerb.PullPlug, EntityIds.Desk(2));
            double firstAt = m.Engine.BuildHostDebugView().Incidents.First(i => i.Type == SabotageType.PullPlug).Time;
            m.AdvanceTo(firstAt + m.Config.GetSabotage(SabotageType.PullPlug).SameTargetCooldownSeconds + 0.001);
            m.Do(0, ActionVerb.PullPlug, EntityIds.Desk(1));
            Assert.AreEqual(4, m.Engine.BuildHostDebugView().Incidents.Count(i => i.Type == SabotageType.PullPlug));
        }

        [TestCase(ActionVerb.CutInternet, ActionVerb.RestartRouter)]
        [TestCase(ActionVerb.CutPower, ActionVerb.ResetFuse)]
        [TestCase(ActionVerb.PullPlug, ActionVerb.PlugIn)]
        public void StationsReserveBothSabotageAndReversal(ActionVerb sabotage, ActionVerb reverse)
        {
            TestMatch m = Match(); m.Work();
            string target = sabotage == ActionVerb.CutInternet ? EntityIds.Router : sabotage == ActionVerb.CutPower ? EntityIds.FuseBox : EntityIds.Desk(1);
            ActionResult first = m.Start(0, sabotage, target); m.MoveTo(3, target);
            Assert.AreEqual(FailureCode.TargetBusy, m.Request(3, sabotage, target).Failure);
            m.AdvanceTo(first.CompletesAt + 0.001);
            ActionResult fixing = m.Start(3, reverse, target); m.MoveTo(5, target);
            Assert.AreEqual(FailureCode.TargetBusy, m.Request(5, reverse, target).Failure);
            m.AdvanceTo(fixing.CompletesAt + 0.001);
            Assert.AreEqual(3, m.Engine.BuildHostDebugView().Incidents.Single().ReversedBy);
        }

        [Test]
        public void CounterVerbsRemainAvailableToActualNonholder()
        {
            MatchSetup setup = PlayersV2Tests.Setup(0, 3, 5); setup.ForceCards = new List<List<SabotageType>>();
            for (int p = 0; p < Protocol.MaxPlayers; p++) setup.ForceCards.Add(p == 0 ? new List<SabotageType> { SabotageType.PowerCut, SabotageType.InternetOutage, SabotageType.PullPlug } : new List<SabotageType>());
            var m = new TestMatch(setup: setup); m.Work();
            Assert.IsEmpty(m.View(3).Me.Cards);
            m.Do(0, ActionVerb.CutInternet, EntityIds.Router); m.Do(3, ActionVerb.RestartRouter, EntityIds.Router);
            m.Do(0, ActionVerb.CutPower, EntityIds.FuseBox); m.Do(3, ActionVerb.ResetFuse, EntityIds.FuseBox);
            m.Do(0, ActionVerb.PullPlug, EntityIds.Desk(1)); m.Do(3, ActionVerb.PlugIn, EntityIds.Desk(1));
            Assert.IsTrue(m.Engine.BuildHostDebugView().Incidents.All(i => i.ReversedBy == 3));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RunningEmailRevalidatesUnplugOrInternetLossWithoutIncident(bool internet)
        {
            TestMatch m = Match(c => c.GetSabotage(SabotageType.FakeEmail).DurationSeconds = 8); m.Work();
            m.Do(1, ActionVerb.WriteReport, EntityIds.DeskComputer(1));
            ActionResult email = m.Start(0, ActionVerb.SendFakeEmail, EntityIds.DeskComputer(1), 0);
            m.Do(3, internet ? ActionVerb.CutInternet : ActionVerb.PullPlug, internet ? EntityIds.Router : EntityIds.Desk(1));
            m.AdvanceTo(email.CompletesAt + 0.001);
            Assert.IsEmpty(m.View().Inbox); Assert.IsFalse(m.Engine.BuildHostDebugView().Incidents.Any(i => i.Type == SabotageType.FakeEmail));
            Assert.AreEqual(0, m.View(0).Me.SabotageCooldowns[(int)SabotageType.FakeEmail]);
            Assert.IsTrue(m.Events.Any(e => e.Type == EventType.InteractionCancelled && e.Actor == 0));
        }

        [TestCase(ActionVerb.SendFakeEmail)]
        [TestCase(ActionVerb.PullPlug)]
        [TestCase(ActionVerb.LockWorkstation)]
        [TestCase(ActionVerb.TipManager)]
        public void EveryOwnerRequiredVerbRejectsUnassignedDeskWithGappedPlayerIds(ActionVerb verb)
        {
            TestMatch m = Match(null, 0, 3, 5); m.Work();
            string target = verb == ActionVerb.SendFakeEmail || verb == ActionVerb.LockWorkstation ? EntityIds.DeskComputer(3) : EntityIds.Desk(3);
            m.MoveTo(0, target);
            Assert.AreEqual(FailureCode.InvalidTarget, m.Request(0, verb, target, 0).Failure);
            Assert.IsEmpty(m.Engine.BuildHostDebugView().Incidents);
            Assert.AreEqual(m.Config.Calls.TipsPerPlayer, m.View().Me.TipsLeft);
        }

        [TestCase(ActionVerb.PullPlug)]
        [TestCase(ActionVerb.TipManager)]
        public void NewTimedVerbAtExactMeetingBoundaryIsCancelledBeforeEffect(ActionVerb verb)
        {
            TestMatch m = Match(c => c.Calls.TipSeconds = 3); m.Phase(MatchPhase.FinalInspection);
            double end = m.View().PhaseEndTime;
            float duration = verb == ActionVerb.TipManager ? m.Config.Calls.TipSeconds : m.Config.GetSabotage(SabotageType.PullPlug).DurationSeconds;
            string target = EntityIds.Desk(1);
            m.AdvanceTo(end - duration); m.Engine.Tick(end - duration); m.MoveTo(0, target);
            ActionResult action = m.Engine.Submit(0, new ActionRequest(m.Engine.MatchId, 90000, verb, target), end - duration);
            Assert.IsTrue(action.Accepted, action.Failure.ToString()); Assert.AreEqual(end, action.CompletesAt);
            m.Engine.Tick(end);
            PlayerView view = m.Engine.BuildView(0, end);
            Assert.AreEqual(MatchPhase.MeetingDiscussion, view.Phase); Assert.IsNull(view.Me.ActiveInteraction);
            Assert.IsFalse(view.Office.InternetDown); Assert.IsFalse(view.Office.PowerDown);
            Assert.IsFalse(view.Desks.Single(d => d.Index == 1).Unplugged);
            Assert.IsEmpty(m.Engine.BuildHostDebugView().Incidents);
            Assert.IsFalse(m.Engine.BuildHostDebugView().Calls.Any(c => c.Tip));
            Assert.AreEqual(m.Config.Calls.TipsPerPlayer, view.Me.TipsLeft);
        }

        [Test]
        public void TargetlessTasteCannotCopyUntrustedTargetIntoSerializableState()
        {
            TestMatch m = Match(); m.Work();
            m.Do(0, ActionVerb.PickUp, EntityIds.Mug(0)); m.Do(0, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine);
            foreach (string target in new[] { "unrelated.private.target", "\ud800" })
            {
                ActionResult request = m.Request(0, ActionVerb.TasteCoffee, target);
                Assert.IsTrue(request.Accepted, request.Failure.ToString());
                Assert.AreEqual("", m.Player(0).ActivityTargetId);
                Assert.AreEqual("", m.View().Me.ActiveInteraction.TargetId);
                foreach (int recipient in m.Engine.Players)
                {
                    Assert.DoesNotThrow(() => WireCodec.EncodeView(m.View(recipient)));
                    Assert.DoesNotThrow(() => WireCodec.EncodeEvents(m.Events.Where(e => e.IsVisibleTo(recipient)).ToList()));
                }
                if (target.Length > 1) m.Request(0, ActionVerb.CancelInteraction);
                else m.AdvanceTo(request.CompletesAt + 0.001);
            }
            foreach (int recipient in m.Engine.Players)
                Assert.DoesNotThrow(() => WireCodec.EncodeEvents(m.Events.Where(e => e.IsVisibleTo(recipient)).ToList()));
        }

        [Test]
        public void DropCannotSpoofAWitnessNoteAtAnUnrelatedHidingSpot()
        {
            TestMatch m = Match(); m.Work(); m.Do(0, ActionVerb.PickUp, EntityIds.Mug(0));
            Vec3 actualPosition = m.Layout.GetStation(EntityIds.CoffeeMachine).Position;
            m.Move(0, actualPosition); m.Move(1, actualPosition + new Vec3(0, 0, 1), 180);
            int notesBefore = m.View(1).Me.Notes.Count;
            SlotDef hiding = m.Layout.Slots.First(s => s.Kind == SlotKind.HidingSpot);
            ActionResult dropped = m.Request(0, ActionVerb.Drop, hiding.Id);
            Assert.IsTrue(dropped.Accepted, dropped.Failure.ToString());
            Assert.AreEqual(notesBefore, m.View(1).Me.Notes.Count);
            ItemView mug = m.Item(EntityIds.Mug(0));
            Assert.AreEqual(ItemLocationKind.Floor, mug.Location);
            Assert.AreEqual(m.Config.Interaction.DropForwardOffset, Vec3.FlatDistance(actualPosition, mug.FloorPosition), 0.0001);
            Assert.IsFalse(m.View(1).Me.Notes.Any(n => n.TargetId == hiding.Id));
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
        [TestCase(MatchPhase.Abandoned)]
        public void EveryNewVerbToleratesMalformedClientFieldsAndGappedIdsInEveryPhase(MatchPhase phase)
        {
            TestMatch m = Match(null, 0, 3, 5);
            if (phase == MatchPhase.Abandoned) m.Engine.Abandon(AbandonReason.HostAborted, m.Now); else m.Phase(phase);
            uint id = 100000;
            string[] targets = { null, "", "\0", "desk.99999999999999999999999.pc", "st.missing", "\ud800" };
            int[] arguments = { int.MinValue, -1, 1, 6, int.MaxValue };
            for (int value = (int)ActionVerb.LockScreen; value <= (int)ActionVerb.Emote; value++)
                foreach (string target in targets) foreach (int argument in arguments) foreach (int actor in new[] { 0, 3, 5 })
                {
                    var request = new ActionRequest(m.Engine.MatchId, id++, (ActionVerb)value) { TargetId = target, IntParam = argument };
                    Assert.DoesNotThrow(() => m.Engine.Submit(actor, request, m.Now), phase + ": " + value);
                }
            foreach (int actor in new[] { int.MinValue, -1, 1, 2, 4, 6, int.MaxValue })
            {
                Assert.DoesNotThrow(() => m.Engine.Submit(actor, null, m.Now));
                Assert.AreEqual(FailureCode.NotConnected, m.Engine.Submit(actor, new ActionRequest(m.Engine.MatchId, id++, ActionVerb.CallPlayer, intParam: 0), m.Now).Failure);
            }
            foreach (int value in new[] { 0, 40, 127, 255 })
            {
                ActionResult result = null;
                Assert.DoesNotThrow(() => result = m.Engine.Submit(0, new ActionRequest(m.Engine.MatchId, id++, (ActionVerb)value), m.Now));
                Assert.IsFalse(result.Accepted);
            }
            Assert.DoesNotThrow(() => m.Engine.Submit(0, null, m.Now));
            Assert.DoesNotThrow(() => m.Engine.Tick(double.NaN));
            Assert.DoesNotThrow(() => m.Engine.Tick(double.PositiveInfinity));
            Assert.DoesNotThrow(() => m.Engine.Tick(double.NegativeInfinity));
            Assert.DoesNotThrow(() => m.Engine.UpdateAvatar(0, new AvatarSample { Position = new Vec3(float.NaN, 0, 0) }));
            foreach (int player in m.Engine.Players) Assert.DoesNotThrow(() => WireCodec.EncodeView(m.View(player)));
        }
    }
}
