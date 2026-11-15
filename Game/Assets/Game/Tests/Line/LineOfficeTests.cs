using System.Linq;
using NUnit.Framework;
using BirimizKovulacak.Core.Tests.Line;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class LineOfficeTests
    {
        // ---- phone and line board

        [Test]
        public void CallsRingConnectAndShowOnThePublicLineBoard()
        {
            var m = new LineTest();
            m.Ok(0, LineVerb.Call, 1);
            Assert.AreEqual(CallStatus.Dialing, m.View(0).Mine.Phone.Status);
            Assert.AreEqual(CallStatus.Ringing, m.View(1).Mine.Phone.Status);
            var board = m.View(3).LineBoard.Single();
            Assert.AreEqual(0, board.Caller);
            Assert.AreEqual(1, board.Callee);
            Assert.AreEqual(CallStatus.Ringing, board.Status);
            m.Ok(1, LineVerb.Answer);
            Assert.AreEqual(CallStatus.Connected, m.View(3).LineBoard.Single().Status);
            int call = m.Engine.ConnectedCallId(0);
            Assert.AreEqual(call, m.Engine.ConnectedCallId(1));
            Assert.AreEqual(1, m.Engine.CallPartner(0));
            Assert.IsTrue(m.View(3).Features.CallsOpen);
            Assert.IsFalse(m.View(3).Features.OpenVoice, "calls phases are 1:1 voice");
            Assert.AreEqual(0, m.Engine.TappersOf(0, new int[6]));
            m.Ok(0, LineVerb.HangUp);
            Assert.IsEmpty(m.View(3).LineBoard);
            var log = m.View(0).Mine.CallLog.Single();
            Assert.IsTrue(log.Outgoing && log.Connected);
            Assert.AreEqual(CallEndReason.HungUp, log.EndReason);
        }

        [Test]
        public void BusyMissedAndDeclinedCallsBehaveAsInV4()
        {
            var m = new LineTest();
            m.Ok(0, LineVerb.Call, 1);
            m.Fail(LineFailure.Busy, 2, LineVerb.Call, 1);
            Assert.IsTrue(m.Events.Any(e => e.Type == LineEventType.CallBusy && e.AudiencePlayer == 2));
            Assert.IsTrue(m.Events.Any(e => e.Type == LineEventType.MissedCall && e.AudiencePlayer == 1 && e.Actor == 2));
            m.Fail(LineFailure.AlreadyInCall, 0, LineVerb.Call, 3);
            m.Fail(LineFailure.NotRinging, 0, LineVerb.Answer);
            m.Advance(12);
            Assert.AreEqual(CallStatus.Idle, m.View(1).Mine.Phone.Status);
            Assert.AreEqual(CallEndReason.Missed, m.View(1).Mine.CallLog.Single().EndReason);
            m.Ok(2, LineVerb.Call, 3);
            m.Ok(3, LineVerb.Decline);
            Assert.AreEqual(CallEndReason.Declined, m.View(2).Mine.CallLog.Single().EndReason);
            m.Fail(LineFailure.NotInCall, 2, LineVerb.HangUp);
            m.Fail(LineFailure.Self, 2, LineVerb.Call, 2);
            m.Engine.SetConnected(3, false, m.Now);
            m.Fail(LineFailure.Absent, 2, LineVerb.Call, 3);
        }

        [Test]
        public void LeavingACallsPhaseEndsCallsAndMeetingsHaveOpenVoice()
        {
            var m = new LineTest();
            m.ToMeeting1();
            m.Fail(LineFailure.WrongPhase, 0, LineVerb.Call, 1);
            Assert.IsTrue(m.View().Features.OpenVoice);
            Assert.IsEmpty(m.View().LineBoard);
            m.Until(LinePhase.TaskPlay);
            m.Ok(0, LineVerb.Call, 1);
            m.Ok(1, LineVerb.Answer);
            m.Until(LinePhase.MeetingDossier);
            Assert.AreEqual(-1, m.Engine.ConnectedCallId(0));
            Assert.AreEqual(CallEndReason.PhaseEnd, m.View(0).Mine.CallLog.Last().EndReason);
            m.Until(LinePhase.CrisisCalls);
            m.Ok(2, LineVerb.Call, 3);
            m.Next();
            Assert.AreEqual(CallStatus.Idle, m.View(2).Mine.Phone.Status);
        }

        [Test]
        public void DisconnectEndsTheCall()
        {
            var m = new LineTest();
            m.Ok(0, LineVerb.Call, 1);
            m.Ok(1, LineVerb.Answer);
            m.Engine.SetConnected(1, false, m.Now);
            Assert.AreEqual(CallStatus.Idle, m.View(0).Mine.Phone.Status);
            Assert.AreEqual(CallEndReason.Disconnected, m.View(0).Mine.CallLog.Single().EndReason);
        }

        // ---- transfers

        [Test]
        public void TransfersArePrivateBoundedAndRateLimited()
        {
            var m = new LineTest();
            m.Fail(LineFailure.BadParam, 0, LineVerb.Transfer, 1, 0);
            m.Fail(LineFailure.BadParam, 0, LineVerb.Transfer, 1, 11);
            m.Fail(LineFailure.InsufficientFunds, 0, LineVerb.Transfer, 1, 7);
            m.Fail(LineFailure.Self, 0, LineVerb.Transfer, 0, 1);
            m.Fail(LineFailure.InvalidTarget, 0, LineVerb.Transfer, 4, 1);
            m.Ok(0, LineVerb.Transfer, 1, 4);
            m.Fail(LineFailure.RateLimited, 0, LineVerb.Transfer, 1, 1);
            Assert.AreEqual(2, m.Prim(0));
            Assert.AreEqual(10, m.Prim(1));
            Assert.AreEqual(1, m.View(0).Mine.Transfers.Count);
            Assert.AreEqual(1, m.View(1).Mine.Transfers.Count);
            Assert.IsEmpty(m.View(2).Mine.Transfers);
            var e = m.Events.Single(x => x.Type == LineEventType.TransferReceived);
            Assert.AreEqual(LineAudience.Player, e.Audience);
            Assert.AreEqual(1, e.AudiencePlayer);
            Assert.AreEqual(4, e.A);
            m.Advance(1);
            m.Ok(0, LineVerb.Transfer, 1, 2);
            m.Fail(LineFailure.InsufficientFunds, 0, LineVerb.Transfer, 1, 1);
        }

        [Test]
        public void TransfersCloseAfterTheFinalVote()
        {
            var m = new LineTest(configure: LineTest.Meeting2Vote);
            m.ToCrisis(id => LineChoice.Skip, id => id == 3 ? 0 : 3);
            m.Until(LinePhase.FinalVote);
            int sender = m.Ids.First(id => !m.Player(id).Candidate);
            int receiver = m.Ids.First(id => id != sender);
            m.Ok(sender, LineVerb.Transfer, receiver, 1);
            m.Until(LinePhase.FinalVerdict);
            m.Fail(LineFailure.WrongPhase, sender, LineVerb.Transfer, receiver, 1);
            m.Until(LinePhase.Results);
            Assert.AreEqual(1, m.View(3).Results.Transfers.Count, "every transfer is disclosed at Results");
        }

        [Test]
        public void CandidateDesksAreSealedButCanReceive()
        {
            var m = new LineTest(configure: LineTest.Meeting2Vote);
            m.ToCrisis(id => LineChoice.Skip, id => id == 3 ? 0 : 3);
            m.Ok(0, LineVerb.Transfer, 1, 1); // before candidates exist anyone may send
            m.Until(LinePhase.CrisisVerdict);
            int candidate = m.Ids.First(id => m.Player(id).Candidate);
            int other = m.Ids.First(id => !m.Player(id).Candidate);
            m.Advance(2);
            m.Fail(LineFailure.DeskSealed, candidate, LineVerb.Transfer, other, 1);
            int before = m.Prim(candidate);
            m.Ok(other, LineVerb.Transfer, candidate, 1);
            Assert.AreEqual(before + 1, m.Prim(candidate), "a candidate can still be paid (vote buying)");
            m.Until(LinePhase.FinalVote);
            m.Fail(LineFailure.DeskSealed, candidate, LineVerb.Transfer, other, 1);
        }

        // ---- messages

        [Test]
        public void TextsArePrivateAndMeetingChatIsPublicInMeetingPhasesOnly()
        {
            var m = new LineTest(configure: c => c.MessageMinInterval = 0);
            m.Ok(0, LineVerb.SendText, 1, text: "selam <b>");
            var msg = m.Engine.MessagesFor(1, 0).Single();
            Assert.AreEqual("selam ‹b›", msg.Text);
            Assert.AreEqual(MessageKind.Text, msg.Kind);
            Assert.AreEqual(1, m.Engine.MessagesFor(0, 0).Count, "sender copy");
            Assert.IsEmpty(m.Engine.MessagesFor(2, 0));
            m.Fail(LineFailure.EmptyText, 0, LineVerb.SendText, 1, text: "  ");
            m.Fail(LineFailure.TooLong, 0, LineVerb.SendText, 1, text: new string('x', 201));
            m.Fail(LineFailure.WrongPhase, 0, LineVerb.SendMeetingChat, text: "hi");
            m.ToMeeting1();
            m.Ok(2, LineVerb.SendMeetingChat, text: "herkese");
            foreach (int id in m.Ids) Assert.AreEqual("herkese", m.Engine.MessagesFor(id, 0).Last().Text);
            m.Until(LinePhase.CrisisCalls);
            m.Fail(LineFailure.WrongPhase, 2, LineVerb.SendMeetingChat, text: "x");
            m.Next();
            m.Ok(2, LineVerb.SendMeetingChat, text: "kriz");
            m.Until(LinePhase.Results);
            m.Fail(LineFailure.WrongPhase, 0, LineVerb.SendText, 1, text: "x");
        }

        [Test]
        public void MessagesAreRateLimitedAndCappedPerSegment()
        {
            var m = new LineTest(configure: c => c.TextsPerSegment = 2);
            m.Ok(0, LineVerb.SendText, 1, text: "a");
            m.Fail(LineFailure.RateLimited, 0, LineVerb.SendText, 1, text: "b");
            m.Advance(1.5);
            m.Ok(0, LineVerb.SendText, 1, text: "b");
            m.Advance(1.5);
            m.Fail(LineFailure.LimitReached, 0, LineVerb.SendText, 1, text: "c");
            Assert.AreEqual(0, m.View(0).Mine.TextsLeft);
            m.ToMeeting1();
            m.Until(LinePhase.TaskPlay);
            Assert.AreEqual(2, m.View(0).Mine.TextsLeft, "reset with the meeting index");
        }

        // ---- recorder and leaks

        static LineTest RecordedCall(out int callId, System.Action<LineConfig> configure = null, System.Action<LineSetup> prepare = null)
        {
            var m = new LineTest(configure: configure, prepare: prepare);
            m.Ok(0, LineVerb.Call, 1);
            m.Ok(1, LineVerb.Answer);
            callId = m.Engine.ConnectedCallId(0);
            return m;
        }

        [Test]
        public void RecorderMarksTheConnectedCallSecretlyOncePerMatch()
        {
            var m = RecordedCall(out int call);
            m.Fail(LineFailure.NotInCall, 2, LineVerb.Record);
            Assert.AreEqual(1, m.View(0).Mine.RecorderUsesLeft);
            int before = m.Events.Count;
            m.Ok(0, LineVerb.Record);
            Assert.IsFalse(m.Events.Skip(before).Any(e => e.IsVisibleTo(1)), "the other party is not told");
            m.Fail(LineFailure.AlreadyUsed, 0, LineVerb.Record);
            Assert.AreEqual(0, m.View(0).Mine.RecorderUsesLeft);
            var rec = m.View(0).Mine.Recorded.Single();
            Assert.AreEqual(call, rec.CallId);
            Assert.AreEqual(1, rec.Other);
            Assert.IsFalse(rec.Leaked);
            Assert.IsEmpty(m.View(1).Mine.Recorded, "the other party is not told");
            m.Ok(0, LineVerb.HangUp);
            m.Ok(0, LineVerb.Call, 2);
            m.Ok(2, LineVerb.Answer);
            m.Fail(LineFailure.LimitReached, 0, LineVerb.Record);
            m.Ok(2, LineVerb.Record);
        }

        [Test]
        public void LeaksQueuePicksBackToBackAndArePublic()
        {
            var m = RecordedCall(out int first);
            m.Ok(0, LineVerb.Record);
            m.Ok(1, LineVerb.Record);
            m.Ok(0, LineVerb.HangUp);
            m.Ok(2, LineVerb.Call, 3);
            m.Ok(3, LineVerb.Answer);
            int second = m.Engine.ConnectedCallId(2);
            m.Ok(3, LineVerb.Record);
            m.Fail(LineFailure.WrongPhase, 0, LineVerb.Leak, a: first);
            m.ToMeeting1();
            m.Fail(LineFailure.NotOwned, 2, LineVerb.Leak, a: first);
            m.Fail(LineFailure.NotOwned, 0, LineVerb.Leak, a: 999);
            m.Advance(2);
            m.Ok(0, LineVerb.Leak, a: first);
            m.Fail(LineFailure.AlreadyResolved, 1, LineVerb.Leak, a: first);
            m.Ok(3, LineVerb.Leak, a: second);
            var picks = m.Engine.RecordingPicks;
            Assert.AreEqual(2, picks.Count);
            Assert.AreEqual(m.Now + 0.5, picks[0].PlayAt, 1e-9);
            Assert.AreEqual(picks[0].PlayAt + 7, picks[1].PlayAt, 1e-9);
            Assert.AreEqual(7f, picks[0].Seconds);
            CollectionAssert.AreEqual(new[] { first }, picks[0].CandidateCallIds);
            Assert.AreEqual(0, picks[0].Caller);
            Assert.AreEqual(1, picks[0].Callee);
            Assert.AreEqual(0, picks[0].Leaker);
            var meeting = m.View(2).Meeting;
            Assert.AreEqual(2, meeting.Recordings.Count);
            var leak = meeting.Dossier.First(d => d.Kind == DossierKind.Leak);
            Assert.AreEqual(0, leak.Player);
            Assert.AreEqual(0, leak.N1);
            Assert.AreEqual(1, leak.N2);
            Assert.IsTrue(m.View(0).Mine.Recorded.Single().Leaked);
            Assert.IsTrue(m.Events.Any(e => e.Type == LineEventType.Leaked && e.Audience == LineAudience.All && e.Actor == 3));
        }

        [Test]
        public void ReportRecordingMarksPlayedOrEmptyAndPicksClearPerMeeting()
        {
            var m = RecordedCall(out int call);
            m.Ok(0, LineVerb.Record);
            m.ToMeeting1();
            m.Ok(0, LineVerb.Leak, a: call);
            m.Engine.ReportRecording(0, -1, m.Now + 1);
            m.Now += 1;
            var pick = m.View().Meeting.Recordings.Single();
            Assert.IsTrue(pick.Empty);
            Assert.IsFalse(pick.Played);
            Assert.IsTrue(m.Engine.RecordingPicks[0].Empty);
            m.Engine.ReportRecording(0, call, m.Now);
            Assert.IsFalse(m.Engine.RecordingPicks[0].Played, "a reported pick is final");
            m.Drain();
            Assert.IsTrue(m.Events.Any(e => e.Type == LineEventType.RecordingPlayed && e.B == 0));
            m.Until(LinePhase.TaskPlay);
            m.Until(LinePhase.MeetingDossier);
            Assert.AreEqual(0, m.Engine.RecordingPicks.Count, "cleared when a meeting starts");
        }

        [Test]
        public void ReportedPlaybackStoresTheChosenCall()
        {
            var m = RecordedCall(out int call);
            m.Ok(1, LineVerb.Record);
            m.ToMeeting1();
            m.Ok(1, LineVerb.Leak, a: call);
            m.Engine.ReportRecording(0, call, m.Now);
            var pick = m.Engine.RecordingPicks[0];
            Assert.IsTrue(pick.Played);
            Assert.AreEqual(call, pick.ChosenCallId);
        }

        [Test]
        public void FinalDiscussionAllowsLeaks()
        {
            var m = RecordedCall(out int call);
            m.Ok(0, LineVerb.Record);
            m.ToCrisis();
            m.Fail(LineFailure.WrongPhase, 0, LineVerb.Leak, a: call);
            m.Until(LinePhase.FinalDiscussion);
            Assert.IsTrue(m.View(0).Features.LeakOpen);
            m.Ok(0, LineVerb.Leak, a: call);
            Assert.AreEqual(1, m.View(2).Meeting.Recordings.Count);
            Assert.AreEqual(2, m.View(2).Meeting.Index);
        }

        [Test]
        public void RecordingsDisabledMakesRecorderAndLeaksUnavailable()
        {
            var m = RecordedCall(out int call, prepare: s => s.RecordingsEnabled = false);
            m.Fail(LineFailure.NotAvailable, 0, LineVerb.Record);
            Assert.AreEqual(0, m.View(0).Mine.RecorderUsesLeft);
            m.ToMeeting1();
            m.Fail(LineFailure.NotAvailable, 0, LineVerb.Leak, a: call);
            var off = RecordedCall(out _, configure: c => c.RecordingsEnabled = false);
            off.Fail(LineFailure.NotAvailable, 0, LineVerb.Record);
        }
    }
}
