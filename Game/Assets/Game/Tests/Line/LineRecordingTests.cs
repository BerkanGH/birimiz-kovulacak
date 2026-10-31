using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BirimizKovulacak.Core.Tests.Line;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class LineRecordingTests
    {
        [TestCase(RecordingSelection.Legacy),TestCase(RecordingSelection.Context)]
        public void RecordingCandidatesStayHostOnlyAndChosenContextBecomesPublic(RecordingSelection mode)
        {
            var m=new LineTest(null,c=>c.RecordingSelection=mode);m.Until(LinePhase.RoundPlay);int peer=m.View().Task.Group[0];m.Ok(0,LineVerb.Call,peer);m.Ok(peer,LineVerb.Answer);int call=m.Engine.ConnectedCallId(0);m.Advance(8);m.Ok(0,LineVerb.HangUp);m.Submit(0,0);
            m.Until(LinePhase.MeetingDossier);Assert.AreEqual(1,m.Engine.RecordingPicks.Count);var p=m.Engine.RecordingPicks[0];Assert.Contains(call,p.CandidateCallIds);Assert.AreEqual(0,m.View(2).Meeting.Recordings[0].CandidateCallIds.Count);Assert.AreEqual(-1,m.View(2).Meeting.Recordings[0].Caller);
            m.Advance(p.PlayAt-m.Now);m.Engine.ReportRecording(0,call,m.Now);Assert.AreEqual(0,m.View(2).Meeting.Recordings[0].Caller);Assert.AreEqual(TaskKind.BonusPool,m.View(2).Meeting.Recordings[0].Context);Assert.IsTrue(m.View(2).Meeting.Recordings[0].Played);
            m.Engine.ReportRecording(0,call,m.Now);Assert.AreEqual(1,m.Engine.DrainEvents().Count(e=>e.Type==LineEventType.RecordingStarted));m.Next();Assert.AreEqual(0,m.Engine.RecordingPicks.Count);
        }
        [Test]
        public void RecordingDisabledAndShortCallsYieldNoPicks()
        {
            var s=LineTest.MakeSetup();s.RecordingsEnabled=false;var m=new LineTest(s);m.Ok(0,LineVerb.Call,1);m.Ok(1,LineVerb.Answer);m.Advance(10);m.Ok(0,LineVerb.HangUp);m.Until(LinePhase.MeetingDossier);Assert.AreEqual(0,m.Engine.RecordingPicks.Count);
            var shortCall=new LineTest();shortCall.Ok(0,LineVerb.Call,1);shortCall.Ok(1,LineVerb.Answer);shortCall.Advance(2);shortCall.Ok(0,LineVerb.HangUp);shortCall.Until(LinePhase.MeetingDossier);Assert.AreEqual(0,shortCall.Engine.RecordingPicks.Count);
        }
        [Test]
        public void ParanoyakHasTwoPicksWithoutRepeatedCandidatesAndReportRejectsForeignCall()
        {
            var s=LineTest.MakeSetup();s.ManagerProfileId="paranoyak";var m=new LineTest(s,c=>c.RecordingCandidates=1);
            m.Ok(0,LineVerb.Call,1);m.Ok(1,LineVerb.Answer);m.Advance(8);m.Ok(0,LineVerb.HangUp);m.Ok(2,LineVerb.Call,3);m.Ok(3,LineVerb.Answer);m.Advance(8);m.Ok(2,LineVerb.HangUp);m.Until(LinePhase.MeetingDossier);
            Assert.AreEqual(2,m.Engine.RecordingPicks.Count);var ids=m.Engine.RecordingPicks.SelectMany(p=>p.CandidateCallIds).ToList();Assert.AreEqual(ids.Count,ids.Distinct().Count());m.Advance(m.Engine.RecordingPicks[0].PlayAt-m.Now);m.Engine.ReportRecording(0,999,m.Now);Assert.IsFalse(m.View().Meeting.Recordings[0].Played);
        }
        [Test]
        public void ContextWeightUsesGroupsDecisionTransferAndMatchWideExposureDecay()
        {
            var m=new LineTest();m.Until(LinePhase.RoundPlay);int peer=m.View().Task.Group[0];m.Ok(0,LineVerb.Call,peer);m.Ok(peer,LineVerb.Answer);int callId=m.Engine.ConnectedCallId(0);m.Advance(8);m.Ok(0,LineVerb.HangUp);m.Submit(0,0);m.Ok(0,LineVerb.Transfer,peer,1);
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var calls=(System.Collections.IList)m.Engine.GetType().GetField("calls",flags).GetValue(m.Engine);object call=calls[0];var method=m.Engine.GetType().GetMethod("RecordingWeight",flags);
            Assert.AreEqual(8*2*1.5*1.5,(double)method.Invoke(m.Engine,new[]{call}));m.Until(LinePhase.MeetingDossier);m.Advance(m.Engine.RecordingPicks[0].PlayAt-m.Now);m.Engine.ReportRecording(0,callId,m.Now);Assert.AreEqual(8*2*1.5*1.5*0.5,(double)method.Invoke(m.Engine,new[]{call}));
        }
    }
}
