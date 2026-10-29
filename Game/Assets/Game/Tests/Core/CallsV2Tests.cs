using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class CallsV2Tests
    {
        [Test]
        public void RingAnswerHangupExposesOnlyRecipientCallStateAndGivesBothCooldowns()
        {
            var m = new TestMatch();
            m.Do(0, ActionVerb.CallPlayer, param: 1);
            Assert.AreEqual(CallState.RingingOut, m.View(0).Me.Call.State);
            Assert.AreEqual(CallState.RingingIn, m.View(1).Me.Call.State);
            Assert.AreEqual(1, m.View(0).Me.Call.Other);
            Assert.AreEqual(0, m.View(1).Me.Call.Other);
            Assert.AreEqual(PublicActivity.None, m.Player(0).Activity);
            Assert.AreEqual(0, m.Player(0).ActivityStart);
            Assert.AreEqual(0, m.Player(0).ActivityEnd);
            Assert.AreEqual(PublicActivity.None, m.Player(1).Activity);
            Assert.IsTrue(m.Player(1).Ringing);
            Assert.AreEqual(PlayerIds.None, m.Engine.CallPartnerOf(0));
            GameEvent incoming = m.Events.Single(e => e.Type == EventType.IncomingCall);
            Assert.AreEqual(1, incoming.Actor);
            Assert.AreEqual(Audience.All, incoming.Audience);
            Assert.AreEqual("", incoming.TargetId);
            Assert.AreEqual(-1, incoming.IntArg);
            m.Advance(0.2);
            m.Do(1, ActionVerb.AnswerCall);
            Assert.AreEqual(CallState.Active, m.View(0).Me.Call.State);
            Assert.AreEqual(PublicActivity.OnCall, m.Player(0).Activity);
            Assert.AreEqual(1, m.Engine.CallPartnerOf(0));
            Assert.AreEqual(0, m.Engine.CallPartnerOf(1));
            Assert.IsFalse(m.Player(1).Ringing);
            Assert.AreEqual(PublicActivity.OnCall, m.Player(1).Activity);
            Assert.AreEqual(m.Now + m.Config.Calls.MaxCallSeconds, m.View(0).Me.Call.Until, 0.000001);
            foreach (GameEvent e in m.Events.Where(e => e.Type == EventType.CallStarted))
            { Assert.AreEqual(Audience.Player, e.Audience); Assert.IsFalse(e.IsVisibleTo(2)); }
            m.View(0).Me.Call.Other = 5;
            Assert.AreEqual(1, m.Engine.CallPartnerOf(0), "recipient views are detached");
            m.Advance(0.3);
            m.Do(0, ActionVerb.HangUp);
            foreach (int p in new[] { 0, 1 })
            {
                Assert.AreEqual(CallState.None, m.View(p).Me.Call.State);
                Assert.AreEqual(PlayerIds.None, m.Engine.CallPartnerOf(p));
                Assert.AreEqual(m.Now + m.Config.Calls.CooldownSeconds, m.View(p).Me.CallCooldownUntil, 0.000001);
                Assert.AreEqual(FailureCode.Cooldown, m.Request(p, ActionVerb.CallPlayer, param: 2).Failure);
            }
            CallRecordView record = m.Engine.BuildHostDebugView().Calls.Single();
            Assert.AreEqual(0, record.Caller); Assert.AreEqual(1, record.Callee);
            Assert.IsTrue(record.Answered); Assert.AreEqual(m.Now, record.End, 0.000001);
            Assert.IsTrue(m.Events.Where(e => e.Type == EventType.CallEnded).All(e => e.IntArg == 1 && e.Audience == Audience.Player));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void RingEndsByCallerCancelCalleeDeclineOrTimeout(int ending)
        {
            var m = new TestMatch();
            m.Do(0, ActionVerb.CallPlayer, param: 1);
            double until = m.View(0).Me.Call.Until;
            if (ending == 2) m.AdvanceTo(until + 2);
            else m.Do(ending, ActionVerb.DeclineCall);
            Assert.AreEqual(CallState.None, m.View(0).Me.Call.State);
            Assert.AreEqual(CallState.None, m.View(1).Me.Call.State);
            Assert.IsFalse(m.Player(1).Ringing);
            Assert.IsFalse(m.Engine.BuildHostDebugView().Calls.Single().Answered);
            if (ending == 2) Assert.AreEqual(until, m.Engine.BuildHostDebugView().Calls.Single().End, 0.000001);
            Assert.AreEqual(2, m.Events.Count(e => e.Type == EventType.CallEnded && e.IntArg == 0));
            double ended = m.Engine.BuildHostDebugView().Calls.Single().End;
            Assert.AreEqual(ended + m.Config.Calls.CooldownSeconds, m.View(0).Me.CallCooldownUntil, 0.000001);
            Assert.AreEqual(0, m.View(1).Me.CallCooldownUntil, "an unanswered callee gets no cooldown");
            Assert.IsTrue(m.Request(1, ActionVerb.CallPlayer, param: 2).Accepted);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DeclineOrRingOutBlocksOnlyTheDirectedPairUntilItsExactDeadline(bool ringOut)
        {
            var m = new TestMatch(configure: c =>
            { c.Calls.CooldownSeconds = 2; c.Calls.DeclineBlockSeconds = 10; c.Calls.RingSeconds = 1; });
            m.Do(0, ActionVerb.CallPlayer, param: 1);
            if (ringOut)
            {
                double until = m.View(0).Me.Call.Until;
                m.AdvanceTo(until); m.Engine.Tick(until); m.Capture();
            }
            else m.Do(1, ActionVerb.DeclineCall);
            double blockedUntil = m.Engine.BuildHostDebugView().Calls.Single().End + m.Config.Calls.DeclineBlockSeconds;
            Assert.AreEqual(FailureCode.Unavailable, m.Request(0, ActionVerb.CallPlayer, param: 1).Failure);
            m.Do(1, ActionVerb.CallPlayer, param: 0);
            m.Do(1, ActionVerb.DeclineCall);
            m.Advance(m.Config.Calls.CooldownSeconds);
            Assert.AreEqual(FailureCode.Unavailable, m.Request(0, ActionVerb.CallPlayer, param: 1).Failure);
            m.Do(0, ActionVerb.CallPlayer, param: 2);
            m.Do(0, ActionVerb.DeclineCall);
            m.AdvanceTo(blockedUntil - 0.0001);
            Assert.AreEqual(FailureCode.Unavailable, m.Request(0, ActionVerb.CallPlayer, param: 1).Failure);
            m.AdvanceTo(blockedUntil); m.Engine.Tick(blockedUntil); m.Capture();
            Assert.IsTrue(m.Request(0, ActionVerb.CallPlayer, param: 1).Accepted);
        }

        [Test]
        public void CallerCancellationDoesNotBlockThePairAndDoesNotChangeExistingCalleeCooldown()
        {
            var m = new TestMatch(configure: c => { c.Calls.CooldownSeconds = 2; c.Calls.DeclineBlockSeconds = 30; });
            m.Do(1, ActionVerb.CallPlayer, param: 2); m.Do(2, ActionVerb.AnswerCall); m.Do(1, ActionVerb.HangUp);
            double priorCooldown = m.View(1).Me.CallCooldownUntil;
            m.Advance(0.5);
            m.Do(0, ActionVerb.CallPlayer, param: 1);
            m.Do(0, ActionVerb.DeclineCall);
            Assert.AreEqual(priorCooldown, m.View(1).Me.CallCooldownUntil);
            m.Advance(m.Config.Calls.CooldownSeconds);
            Assert.IsTrue(m.Request(0, ActionVerb.CallPlayer, param: 1).Accepted);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void NonpositiveDeclineBlockDisablesThePairBlock(float seconds)
        {
            var m = new TestMatch(configure: c => { c.Calls.CooldownSeconds = 0; c.Calls.DeclineBlockSeconds = seconds; });
            m.Do(0, ActionVerb.CallPlayer, param: 1); m.Do(1, ActionVerb.DeclineCall);
            Assert.IsTrue(m.Request(0, ActionVerb.CallPlayer, param: 1).Accepted);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ActiveCallEndsAtMaximumDisconnectMeetingOrAbandon(int ending)
        {
            var m = new TestMatch();
            if (ending == 2) m.Phase(MatchPhase.FinalInspection);
            m.Do(0, ActionVerb.CallPlayer, param: 1); m.Do(1, ActionVerb.AnswerCall);
            double until = m.View(0).Me.Call.Until;
            if (ending == 0) m.AdvanceTo(until + 1);
            if (ending == 1) { m.Engine.SetConnected(1, false, m.Now); m.Capture(); }
            if (ending == 2) m.Phase(MatchPhase.MeetingDiscussion);
            if (ending == 3) { m.Engine.Abandon(AbandonReason.HostAborted, m.Now); m.Capture(); }
            Assert.AreEqual(PlayerIds.None, m.Engine.CallPartnerOf(0));
            Assert.AreEqual(PlayerIds.None, m.Engine.CallPartnerOf(1));
            Assert.AreEqual(2, m.Events.Count(e => e.Type == EventType.CallEnded && e.IntArg == 1));
            if (ending == 0) Assert.AreEqual(until, m.Engine.BuildHostDebugView().Calls.Single().End, 0.000001);
        }

        [Test]
        public void CallsDoNotBlockMovementOrTimedWorkAndActivityReturnsAfterCompletionOrCancel()
        {
            var m = new TestMatch(); m.Work();
            m.Do(0, ActionVerb.CallPlayer, param: 1); m.Do(1, ActionVerb.AnswerCall);
            m.MoveTo(0, EntityIds.DeskComputer(0));
            var write = m.Start(0, ActionVerb.WriteReport, EntityIds.DeskComputer(0));
            Assert.AreEqual(PublicActivity.Typing, m.Player(0).Activity);
            Assert.AreEqual(1, m.Engine.CallPartnerOf(0));
            m.AdvanceTo(write.CompletesAt + 0.0001);
            Assert.AreEqual(PublicActivity.OnCall, m.Player(0).Activity);
            m.Start(0, ActionVerb.WriteReport, EntityIds.DeskComputer(0));
            m.Do(0, ActionVerb.CancelInteraction);
            Assert.AreEqual(PublicActivity.OnCall, m.Player(0).Activity);
            m.Start(0, ActionVerb.WriteReport, EntityIds.DeskComputer(0));
            Assert.IsTrue(m.Request(0, ActionVerb.HangUp).Accepted);
            Assert.AreEqual(PublicActivity.Typing, m.Player(0).Activity);
            Assert.IsNotNull(m.View(0).Me.ActiveInteraction);
            m.Do(0, ActionVerb.CancelInteraction);
            Assert.AreEqual(PublicActivity.None, m.Player(0).Activity);
        }

        [Test]
        public void CallsCanStartAndBeAnsweredWhileOtherInteractionsAreBusy()
        {
            var m = new TestMatch(); m.Work();
            m.Start(0, ActionVerb.WriteReport, EntityIds.DeskComputer(0));
            m.Start(1, ActionVerb.WriteReport, EntityIds.DeskComputer(1));
            Assert.IsTrue(m.Request(0, ActionVerb.CallPlayer, param: 1).Accepted);
            Assert.IsTrue(m.Request(1, ActionVerb.AnswerCall).Accepted);
            Assert.IsNotNull(m.View(0).Me.ActiveInteraction);
            Assert.IsNotNull(m.View(1).Me.ActiveInteraction);
            Assert.AreEqual(1, m.Engine.CallPartnerOf(0));
            Assert.AreEqual(PublicActivity.Typing, m.Player(1).Activity);
        }

        [Test]
        public void CallValidationHandlesAbsentSelfDisconnectedBusyAndInvalidState()
        {
            var m = new TestMatch(setup: PlayersV2Tests.Setup(0, 3, 5));
            Assert.AreEqual(FailureCode.CannotTargetSelf, m.Request(0, ActionVerb.CallPlayer, param: 0).Failure);
            foreach (int target in new[] { -1, 1, 6, int.MinValue, int.MaxValue })
                Assert.AreEqual(FailureCode.InvalidTarget, m.Request(0, ActionVerb.CallPlayer, param: target).Failure);
            Assert.AreEqual(FailureCode.NoIncomingCall, m.Request(0, ActionVerb.AnswerCall).Failure);
            Assert.AreEqual(FailureCode.NoIncomingCall, m.Request(0, ActionVerb.DeclineCall).Failure);
            Assert.AreEqual(FailureCode.NotInCall, m.Request(0, ActionVerb.HangUp).Failure);
            m.Engine.SetConnected(5, false, m.Now);
            Assert.AreEqual(FailureCode.Unavailable, m.Request(0, ActionVerb.CallPlayer, param: 5).Failure);
            m.Do(0, ActionVerb.CallPlayer, param: 3);
            Assert.AreEqual(FailureCode.InCall, m.Request(0, ActionVerb.CallPlayer, param: 3).Failure);
            m.Engine.SetConnected(5, true, m.Now);
            Assert.AreEqual(FailureCode.Unavailable, m.Request(5, ActionVerb.CallPlayer, param: 3).Failure);
            Assert.AreEqual(FailureCode.NoIncomingCall, m.Request(0, ActionVerb.AnswerCall).Failure);
        }

        [TestCase(MatchPhase.Briefing)]
        [TestCase(MatchPhase.Work1)]
        [TestCase(MatchPhase.Raid)]
        [TestCase(MatchPhase.Work2)]
        [TestCase(MatchPhase.FinalInspection)]
        public void CallsAreAvailableThroughoutTheOfficeAndBriefing(MatchPhase phase)
        {
            var m = new TestMatch(); m.Phase(phase);
            Assert.IsTrue(m.Request(0, ActionVerb.CallPlayer, param: 1).Accepted);
        }

        [Test]
        public void FeatureSwitchesAndWeekFlagsDisablePhoneVerbs()
        {
            var m = new TestMatch(configure: c => { c.Calls.Enabled = false; c.Calls.TipLineEnabled = false; });
            Assert.AreEqual(FailureCode.FeatureDisabled, m.Request(0, ActionVerb.CallPlayer, param: 1).Failure);
            Assert.AreEqual(FailureCode.FeatureDisabled, m.Request(3, ActionVerb.PullCallLogs).Failure);
            m.Work();
            Assert.AreEqual(FailureCode.FeatureDisabled, m.Request(0, ActionVerb.TipManager, EntityIds.Desk(1)).Failure);
            var setup = TestMatch.Setup(); setup.Mode = GameMode.Week;
            var week = new TestMatch(setup: setup, configure: c => { c.ManagerProfiles.Clear(); c.WeekDays[0].Calls = false; });
            Assert.IsFalse(week.View().Features.Calls);
            Assert.IsFalse(week.View().Features.TipLine);
            Assert.AreEqual(FailureCode.FeatureDisabled, week.Request(0, ActionVerb.CallPlayer, param: 1).Failure);
            week.Work();
            Assert.AreEqual(FailureCode.FeatureDisabled, week.Request(0, ActionVerb.TipManager, EntityIds.Desk(1)).Failure);
        }

        [Test]
        public void CallLogsAreChargedPrivateSnapshotsAndOnlyPresentedCopyBecomesPublic()
        {
            var setup = TestMatch.Setup(); setup.StartTime = 317.125;
            var m = new TestMatch(setup: setup, configure: c => c.Informant.CallLogCharges = 2);
            m.Do(0, ActionVerb.CallPlayer, param: 1); m.Do(1, ActionVerb.AnswerCall);
            Assert.AreEqual(FailureCode.NotAllowed, m.Request(2, ActionVerb.PullCallLogs).Failure);
            m.Do(3, ActionVerb.PullCallLogs);
            PrivateNote note = m.View(3).Me.Notes.Single(n => n.Kind == NoteKind.CallLog);
            Assert.AreEqual(0, note.Calls.Single().End);
            byte[] marker = CallBytes(note.Calls.Single());
            Assert.IsTrue(ContainsBytes(WireCodec.EncodeView(m.View(3)), marker));
            foreach (int recipient in new[] { 0, 1, 2 })
            {
                Assert.IsFalse(ContainsBytes(WireCodec.EncodeView(m.View(recipient)), marker));
                Assert.IsFalse(ContainsBytes(WireCodec.EncodeEvents(m.Events.Where(e => e.IsVisibleTo(recipient)).ToList()), marker));
            }
            note.Calls[0].Caller = 5;
            Assert.AreEqual(0, m.View(3).Me.Notes.Single(n => n.Kind == NoteKind.CallLog).Calls[0].Caller);
            m.Advance(0.2); m.Do(0, ActionVerb.HangUp);
            Assert.AreEqual(0, m.View(3).Me.Notes.Single(n => n.Kind == NoteKind.CallLog).Calls[0].End, "pull is a frozen snapshot");
            m.Do(3, ActionVerb.PullCallLogs);
            Assert.AreEqual(FailureCode.NoCharges, m.Request(3, ActionVerb.PullCallLogs).Failure);
            m.Phase(MatchPhase.MeetingDiscussion);
            m.Do(3, ActionVerb.PresentNote, param: note.Id);
            EvidenceView evidence = m.View(2).Evidence.Single(e => e.Kind == EvidenceKind.CallLogStatement);
            Assert.AreEqual(3, evidence.SourcePlayer);
            Assert.AreEqual(0, evidence.Calls.Single().End);
            Assert.AreEqual(0, evidence.RiskWeight);
            CollectionAssert.Contains(m.View(2).Meeting.PresentedEvidence, evidence.Id);
            Assert.IsTrue(ContainsBytes(WireCodec.EncodeView(m.View(2)), marker));
            evidence.Calls[0].Caller = 4;
            Assert.AreEqual(0, m.View(1).Evidence.Single(e => e.Id == evidence.Id).Calls[0].Caller);
            m.Finish();
            Assert.Greater(m.View().Result.Calls.Single().End, 0);
            Assert.AreEqual(m.View().Result.ResultHash, WireCodec.HashResult(m.View().Result));
        }

        [Test]
        public void TipIsRemoteAnonymousCancellableAndBoundedWithoutLeakingTargetActivity()
        {
            var m = new TestMatch(configure: c => { c.Tasks.InitialDirtyDesks = 0; c.Calls.TipSeconds = 3; }); m.Work();
            Assert.AreEqual(FailureCode.CannotTargetSelf, m.Request(0, ActionVerb.TipManager, EntityIds.Desk(0)).Failure);
            Assert.AreEqual(FailureCode.InvalidTarget, m.Request(0, ActionVerb.TipManager, EntityIds.Desk(4)).Failure);
            Assert.AreEqual(FailureCode.InvalidTarget, m.Request(0, ActionVerb.TipManager, EntityIds.DeskComputer(1)).Failure);
            m.Do(0, ActionVerb.CallPlayer, param: 1);
            Assert.AreEqual(FailureCode.InCall, m.Request(0, ActionVerb.TipManager, EntityIds.Desk(1)).Failure);
            m.Do(0, ActionVerb.DeclineCall);
            var started = m.Request(0, ActionVerb.TipManager, EntityIds.Desk(2));
            Assert.IsTrue(started.Accepted, "phone tip needs no desk proximity");
            Assert.AreEqual(PublicActivity.None, m.Player(0).Activity);
            Assert.AreEqual("", m.Player(0).ActivityTargetId);
            Assert.AreEqual(0, m.Player(0).ActivityStart);
            Assert.AreEqual(0, m.Player(0).ActivityEnd);
            Assert.AreEqual(EntityIds.Desk(2), m.View(0).Me.ActiveInteraction.TargetId);
            Assert.IsNull(m.View(1).Me.ActiveInteraction);
            Assert.AreEqual(FailureCode.Busy, m.Request(0, ActionVerb.TipManager, EntityIds.Desk(1)).Failure);
            Assert.AreEqual(FailureCode.InCall, m.Request(0, ActionVerb.CallPlayer, param: 2).Failure);
            Assert.AreEqual(FailureCode.Unavailable, m.Request(2, ActionVerb.CallPlayer, param: 0).Failure);
            m.Do(0, ActionVerb.CancelInteraction);
            Assert.AreEqual(1, m.View(0).Me.TipsLeft);
            m.Do(0, ActionVerb.TipManager, EntityIds.Desk(2));
            Assert.AreEqual(FailureCode.NoCharges, m.Request(0, ActionVerb.TipManager, EntityIds.Desk(1)).Failure);
            GameEvent tip = m.Events.Single(e => e.Type == EventType.ManagerTipReceived);
            Assert.AreEqual(PlayerIds.None, tip.Actor); Assert.AreEqual("", tip.TargetId); Assert.AreEqual(-1, tip.IntArg);
            CallRecordView record = m.Engine.BuildHostDebugView().Calls.Single(c => c.Tip);
            Assert.AreEqual(0, record.Caller); Assert.AreEqual(PlayerIds.None, record.Callee); Assert.AreEqual(2, record.TipDesk);
            byte[] marker = CallBytes(record);
            foreach (int recipient in m.Engine.Players) Assert.IsFalse(ContainsBytes(WireCodec.EncodeView(m.View(recipient)), marker));
            m.Do(3, ActionVerb.PullCallLogs);
            Assert.AreEqual(2, m.View(3).Me.Notes.Single(n => n.Kind == NoteKind.CallLog).Calls.Single(c => c.Tip).TipDesk);
        }

        [Test]
        public void DefaultInstantTipCompletesWithoutPublicPhoneActivityOrPendingInteraction()
        {
            var m = new TestMatch(); m.Work();
            Assert.AreEqual(0, m.Config.Calls.TipSeconds);
            double now = m.Now;
            ActionResult result = m.Request(0, ActionVerb.TipManager, EntityIds.Desk(1));
            Assert.IsTrue(result.Accepted);
            Assert.AreEqual(now, result.CompletesAt);
            Assert.AreEqual(0, m.View().Me.TipsLeft);
            Assert.IsNull(m.View().Me.ActiveInteraction);
            foreach (int recipient in m.Engine.Players)
            {
                PlayerPublicView caller = m.View(recipient).Players.Single(p => p.Id == 0);
                Assert.AreEqual(PublicActivity.None, caller.Activity);
                Assert.AreEqual(0, caller.ActivityStart); Assert.AreEqual(0, caller.ActivityEnd);
                Assert.AreEqual("", caller.ActivityTargetId);
                Assert.IsFalse(caller.Ringing);
            }
            CallRecordView tip = m.Engine.BuildHostDebugView().Calls.Single();
            Assert.IsTrue(tip.Tip); Assert.AreEqual(now, tip.Start); Assert.AreEqual(now, tip.End);
            Assert.AreEqual(1, m.Events.Count(e => e.Type == EventType.ManagerTipReceived));
            Assert.IsFalse(m.Events.Any(e => e.Type == EventType.IncomingCall || e.Type == EventType.CallStarted));
        }

        [Test]
        public void TipsVisitFirstDwellLongerAndOpenOnlyTheirDrawerDuringRaid()
        {
            var m = new TestMatch(configure: c => { c.ManagerProfiles.Clear(); c.Tasks.InitialDirtyDesks = 0; }); m.Work();
            m.Plant(2, 0, ItemVariants.Duck);
            m.Plant(1, 3, ItemVariants.Stapler);
            m.Do(0, ActionVerb.TipManager, EntityIds.Desk(3));
            m.Phase(MatchPhase.Raid);
            while (m.View().Manager.InspectingDesk < 0) m.Advance(0.05);
            Assert.AreEqual(3, m.View().Manager.InspectingDesk);
            m.Advance(m.Layout.ManagerRoute.Single(s => s.DeskIndex == 3).DwellSeconds + m.Config.Manager.TipDwellBonus - 0.1);
            Assert.AreEqual(3, m.View().Manager.InspectingDesk);
            m.Advance(0.2);
            Assert.AreNotEqual(3, m.View().Manager.InspectingDesk);
            m.Advance(10);
            Assert.AreEqual(1, m.View().Evidence.Count(e => e.ItemId == EntityIds.Suspicious(ItemVariants.Stapler)));
            Assert.IsFalse(m.View().Evidence.Any(e => e.ItemId == EntityIds.Suspicious(ItemVariants.Duck)));
            Assert.AreEqual(1, m.View().Observations.Count(o => o.Kind == ObservationKind.TipFound && o.DeskIndex == 3 && o.Severity == 0));
            m.Phase(MatchPhase.MeetingDiscussion);
            Assert.IsTrue(m.View().Evidence.Any(e => e.ItemId == EntityIds.Suspicious(ItemVariants.Duck)), "final inspection searches all drawers");
        }

        [Test]
        public void CleanTipBackfiresAndTipsDuringWarningJoinTheUpcomingWalk()
        {
            var m = new TestMatch(configure: c =>
            { c.ManagerProfiles.Clear(); c.Tasks.InitialDirtyDesks = 0; c.Durations.RaidWarning = 5; c.Calls.TipSeconds = 1; });
            m.Phase(MatchPhase.Raid);
            m.Do(0, ActionVerb.TipManager, EntityIds.Desk(2));
            while (m.View().Manager.InspectingDesk < 0) m.Advance(0.05);
            Assert.AreEqual(2, m.View().Manager.InspectingDesk);
            ObservationView clean = m.View().Observations.Single(o => o.Kind == ObservationKind.TipClean);
            Assert.AreEqual(2, clean.Subject); Assert.AreEqual(m.Config.Risk.TipClean, clean.Severity);
        }

        [Test]
        public void MultipleTipsKeepArrivalOrderAndDuplicateDeskIsVisitedOnce()
        {
            var m = new TestMatch(configure: c => { c.ManagerProfiles.Clear(); c.Tasks.InitialDirtyDesks = 0; c.Durations.Raid = 35; }); m.Work();
            m.Do(0, ActionVerb.TipManager, EntityIds.Desk(3));
            m.Do(1, ActionVerb.TipManager, EntityIds.Desk(2));
            m.Do(2, ActionVerb.TipManager, EntityIds.Desk(3));
            m.Phase(MatchPhase.Raid); m.Advance(25);
            CollectionAssert.AreEqual(new[] { 3, 2 }, m.View().Observations.Where(o => o.Kind == ObservationKind.TipClean).Select(o => o.DeskIndex));
        }

        [Test]
        public void TipAfterWalkStartsWaitsForNextInspectionAndFinalTipsHaveNoEffect()
        {
            var m = new TestMatch(configure: c => { c.ManagerProfiles.Clear(); c.Tasks.InitialDirtyDesks = 0; });
            m.Phase(MatchPhase.Raid); m.Advance(3);
            m.Do(0, ActionVerb.TipManager, EntityIds.Desk(2));
            m.Phase(MatchPhase.Work2);
            Assert.IsFalse(m.View().Observations.Any(o => o.Kind == ObservationKind.TipClean));
            m.Phase(MatchPhase.FinalInspection);
            m.Do(1, ActionVerb.TipManager, EntityIds.Desk(3));
            m.Phase(MatchPhase.MeetingDiscussion);
            Assert.IsTrue(m.View().Observations.Any(o => o.Kind == ObservationKind.TipClean && o.DeskIndex == 2));
            Assert.IsFalse(m.View().Observations.Any(o => o.Kind == ObservationKind.TipClean && o.DeskIndex == 3));
            Assert.AreEqual(2, m.Engine.BuildHostDebugView().Calls.Count(c => c.Tip));
        }

        [Test]
        public void ManagerObservesOnlyActiveCallsInVisionOncePerInspection()
        {
            var world = new VisibilityWorld();
            var m = new TestMatch(world: world, configure: c => c.Calls.CooldownSeconds = 0, arrange: l =>
            {
                ManagerStop stop = l.ManagerRoute.Single(s => s.DeskIndex == 0);
                stop.DwellSeconds = 15;
                l.ManagerRoute.Clear(); l.ManagerRoute.Add(stop);
            });
            m.Phase(MatchPhase.Raid);
            m.Move(0, m.Layout.GetDesk(0).Position);
            m.Do(0, ActionVerb.CallPlayer, param: 1);
            m.Advance(3);
            Assert.IsFalse(m.View().Observations.Any(o => o.Kind == ObservationKind.PersonalCall), "ringing does not count");
            m.Do(1, ActionVerb.AnswerCall); world.Visible = false; m.Advance(1);
            Assert.IsFalse(m.View().Observations.Any(o => o.Kind == ObservationKind.PersonalCall));
            world.Visible = true; m.Advance(1);
            Assert.AreEqual(1, m.View().Observations.Count(o => o.Kind == ObservationKind.PersonalCall && o.Subject == 0));
            m.Do(0, ActionVerb.HangUp); m.Do(0, ActionVerb.CallPlayer, param: 1); m.Do(1, ActionVerb.AnswerCall); m.Advance(1);
            Assert.AreEqual(1, m.View().Observations.Count(o => o.Kind == ObservationKind.PersonalCall && o.Subject == 0));
            m.Phase(MatchPhase.FinalInspection);
            m.Do(0, ActionVerb.CallPlayer, param: 1); m.Do(1, ActionVerb.AnswerCall); m.Advance(3);
            Assert.AreEqual(2, m.View().Observations.Count(o => o.Kind == ObservationKind.PersonalCall && o.Subject == 0));
        }

        [Test]
        public void CallsAndLogsUseTheirSpecifiedMeetingPhaseBoundaries()
        {
            var m = new TestMatch(configure: c => c.Informant.CallLogCharges = 5);
            m.Phase(MatchPhase.MeetingDiscussion);
            foreach (ActionVerb verb in new[] { ActionVerb.CallPlayer, ActionVerb.AnswerCall, ActionVerb.DeclineCall, ActionVerb.HangUp, ActionVerb.TipManager })
                Assert.AreEqual(FailureCode.WrongPhase, m.Request(0, verb, EntityIds.Desk(1), 1).Failure);
            m.Do(3, ActionVerb.PullCallLogs);
            m.Phase(MatchPhase.MeetingDefense1); m.Do(3, ActionVerb.PullCallLogs);
            m.Phase(MatchPhase.MeetingDefense2); m.Do(3, ActionVerb.PullCallLogs);
            m.Phase(MatchPhase.MeetingVote);
            Assert.AreEqual(FailureCode.WrongPhase, m.Request(3, ActionVerb.PullCallLogs).Failure);
        }

        [Test]
        public void CallsSurviveEveryOfficePhaseTransitionAndEndAtMeeting()
        {
            var m = new TestMatch(configure: c => c.Calls.MaxCallSeconds = 1000);
            m.Do(0, ActionVerb.CallPlayer, param: 1); m.Do(1, ActionVerb.AnswerCall);
            foreach (MatchPhase phase in new[] { MatchPhase.Work1, MatchPhase.Raid, MatchPhase.Work2, MatchPhase.FinalInspection })
            { m.Phase(phase); Assert.AreEqual(1, m.Engine.CallPartnerOf(0), phase.ToString()); }
            m.Phase(MatchPhase.MeetingDiscussion);
            Assert.AreEqual(PlayerIds.None, m.Engine.CallPartnerOf(0));
            Assert.AreEqual(m.View().PhaseStartTime, m.Engine.BuildHostDebugView().Calls.Single().End, 0.000001);
        }

        [Test]
        public void TipRecordsStayInStartOrderWhenAnotherCallBeginsBeforeTipCompletes()
        {
            var m = new TestMatch(configure: c => c.Calls.TipSeconds = 3); m.Work();
            ActionResult tip = m.Request(0, ActionVerb.TipManager, EntityIds.Desk(1));
            Assert.IsTrue(tip.Accepted);
            m.Advance(0.5); m.Do(2, ActionVerb.CallPlayer, param: 3); m.Do(3, ActionVerb.AnswerCall);
            m.AdvanceTo(tip.CompletesAt + 0.01);
            m.Do(2, ActionVerb.HangUp);
            List<CallRecordView> records = m.Engine.BuildHostDebugView().Calls;
            Assert.AreEqual(2, records.Count);
            Assert.IsTrue(records[0].Tip); Assert.AreEqual(0, records[0].Caller);
            Assert.IsFalse(records[1].Tip); Assert.AreEqual(2, records[1].Caller);
            Assert.IsTrue(records[1].Answered); Assert.AreEqual(m.Now, records[1].End, 0.000001);
        }

        [Test]
        public void PendingTipRedirectsSurpriseInspection()
        {
            var m = new TestMatch(configure: c =>
            {
                c.ManagerProfiles.Clear();
                c.ManagerProfiles.Add(new ManagerProfileDef { Id = "surprise", SurpriseInspection = true });
                c.Manager.SurpriseMinFraction = c.Manager.SurpriseMaxFraction = 0;
                c.Manager.SurpriseAtFraction = 0.5f; c.Tasks.InitialDirtyDesks = 0;
            });
            m.Phase(MatchPhase.Work2);
            m.Do(0, ActionVerb.TipManager, EntityIds.Desk(3));
            double starts = m.View().PhaseStartTime + m.Config.Durations.Work2 * 0.5;
            m.AdvanceTo(starts + 0.01);
            while (m.View().Manager.InspectingDesk < 0) m.Advance(0.05);
            Assert.AreEqual(3, m.View().Manager.InspectingDesk);
            Assert.IsTrue(m.View().Manager.Surprise);
            Assert.IsTrue(m.View().Observations.Any(o => o.Kind == ObservationKind.TipClean && o.DeskIndex == 3));
        }

        [Test]
        public void TasteAndSearchNotesPresentTheirNewStructuredFields()
        {
            var setup = TestMatch.Setup();
            setup.ForceCards = new List<List<SabotageType>>();
            for (int p = 0; p < Protocol.MaxPlayers; p++) setup.ForceCards.Add(new List<SabotageType> { SabotageType.SaltCoffee });
            var m = new TestMatch(setup: setup); m.Work();
            m.Do(0, ActionVerb.PickUp, EntityIds.Mug(0));
            m.Do(0, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine);
            m.Do(0, ActionVerb.SaltCoffee, EntityIds.CoffeeMachine);
            m.Do(0, ActionVerb.TasteCoffee);
            PrivateNote taste = m.View().Me.Notes.Single(n => n.Kind == NoteKind.TasteResult);
            Assert.IsTrue(taste.Salted);
            m.Do(0, ActionVerb.Drop);
            m.Plant(0, 0);
            m.Do(0, ActionVerb.SearchContainer, EntityIds.DeskDrawer(0));
            PrivateNote search = m.View().Me.Notes.Single(n => n.Kind == NoteKind.SearchResult);
            m.Phase(MatchPhase.MeetingDiscussion);
            m.Do(0, ActionVerb.PresentNote, param: taste.Id);
            m.Do(0, ActionVerb.PresentNote, param: search.Id);
            EvidenceView tasting = m.View(1).Evidence.Single(e => e.Kind == EvidenceKind.TasteStatement);
            Assert.IsTrue(tasting.Salted); Assert.AreEqual(EntityIds.Mug(0), tasting.ItemId);
            EvidenceView searching = m.View(1).Evidence.Single(e => e.Kind == EvidenceKind.SearchStatement);
            CollectionAssert.AreEqual(search.Contents, searching.Contents);
            Assert.AreEqual("", searching.ItemVariant);
            searching.Contents.Clear();
            Assert.IsNotEmpty(m.View(2).Evidence.Single(e => e.Id == searching.Id).Contents);
        }

        private sealed class VisibilityWorld : IWorldQueries
        {
            public bool Visible = true;
            public bool HasLineOfSight(Vec3 from, Vec3 to) { return Visible; }
        }

        private static byte[] CallBytes(CallRecordView record)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                {
                    writer.Write(true); writer.Write(record.Caller); writer.Write(record.Callee);
                    writer.Write(record.Start); writer.Write(record.End); writer.Write(record.Answered);
                    writer.Write(record.Tip); writer.Write(record.TipDesk);
                }
                return stream.ToArray();
            }
        }

        private static bool ContainsBytes(byte[] haystack, byte[] needle)
        {
            for (int start = 0; start <= haystack.Length - needle.Length; start++)
            {
                int i = 0; while (i < needle.Length && haystack[start + i] == needle[i]) i++;
                if (i == needle.Length) return true;
            }
            return false;
        }
    }
}
