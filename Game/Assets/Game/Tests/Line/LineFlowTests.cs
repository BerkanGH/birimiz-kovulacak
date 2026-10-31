using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BirimizKovulacak.Core.Tests.Line;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class LineFlowTests
    {
        [Test]
        public void BlocksHaveBetweenTaskAndClosingBreakAndFirstIntros()
        {
            var m=new LineTest(null,c=>c.CompanyTargetPercent=0);Assert.AreEqual(40,m.Engine.PhaseEndTime);m.Next();Assert.AreEqual(LinePhase.RoundIntro,m.Engine.Phase);Assert.AreEqual(25,m.Engine.PhaseEndTime-m.Now);
            var phases=new List<string>();while(!m.Engine.IsFinished){var v=m.View();phases.Add(v.Day+":"+v.Phase);m.Next();}
            Assert.AreEqual(4,phases.Count(x=>x.EndsWith(":Break")));Assert.Contains("2:FinalDefense",phases);
            Assert.AreEqual(6,m.Engine.Result.Players[0].TasksConnected);
        }
        [TestCase(EarlySubmitPolicy.CarryToBreak,30),TestCase(EarlySubmitPolicy.Shorten,0)]
        public void EarlySubmitCarriesSavedSecondsToTheNextBreak(EarlySubmitPolicy policy,int carried)
        {
            var m=LineTest.Task(TaskKind.BonusPool,4,c=>c.EarlySubmit=policy);m.Until(LinePhase.RoundPlay);double original=m.Engine.PhaseEndTime;m.Perfect();
            Assert.AreEqual(m.Now+8,m.Engine.PhaseEndTime);var ev=m.Engine.DrainEvents().Single(e=>e.Type==LineEventType.HostEarlySubmit);Assert.AreEqual((original-m.Now-8)*1000,ev.A);Assert.AreEqual(carried*1000,ev.B);
            m.Next();Assert.AreEqual((int)PhaseEndReason.TimerShortened,m.Engine.DrainEvents().Single(e=>e.Type==LineEventType.PhaseChanged).B);m.Next();Assert.AreEqual(LinePhase.Break,m.Engine.Phase);Assert.AreEqual(15+carried,m.Engine.PhaseEndTime-m.Now);Assert.AreEqual(carried,m.View().Features.CarriedSeconds);
        }
        [Test]
        public void ClosingCarryIsCappedAndMiddleTaskBCarryIsDropped()
        {
            var m=new LineTest();m.Until(LinePhase.RoundPlay,0,1);m.Perfect();Assert.AreEqual(0,m.Engine.DrainEvents().Single(e=>e.Type==LineEventType.HostEarlySubmit).B);
            m.Until(LinePhase.RoundPlay,2,1);if(m.View().Task.Kind==TaskKind.SupplySwap)return;m.Perfect();var ev=m.Engine.DrainEvents().Last(e=>e.Type==LineEventType.HostEarlySubmit);Assert.LessOrEqual(ev.B,20000);m.Until(LinePhase.Break,2);Assert.LessOrEqual(m.Engine.PhaseEndTime-m.Now,45);
        }
        [Test]
        public void ProductivityMeasuresOnlyResolvedTasksAndFreezesAllScheduledAtFinal()
        {
            var m=LineTest.Task(TaskKind.BonusPool);Assert.AreEqual(-1,m.View().Company.ProvisionalPermille);Assert.AreEqual(-1,m.View().Company.FinalPermille);Assert.AreEqual(600,m.View().Company.ScheduledMax);
            m.Perfect();m.Reveal();Assert.AreEqual(1000,m.View().Company.ProvisionalPermille);Assert.AreEqual(100,m.View().Company.ResolvedOutput);
            m.Until(LinePhase.MeetingDossier,2);int frozen=m.View().Company.FinalPermille;Assert.AreEqual(m.View().Company.ResolvedOutput*1000/600,frozen);m.Finish();Assert.AreEqual(frozen,m.Engine.Result.FinalProductivityPermille);
        }
        [Test]
        public void AbandonmentUsesAllScheduledMaxAndCancelsCalls()
        {
            var m=new LineTest();m.Perfect();m.Reveal();m.Engine.SetConnected(1,false,m.Now);m.Engine.SetConnected(2,false,m.Now);m.Engine.SetConnected(3,false,m.Now);m.Advance(60);
            Assert.AreEqual(LinePhase.Abandoned,m.Engine.Phase);Assert.IsTrue(m.Engine.IsFinished);Assert.AreEqual(166,m.View().Company.FinalPermille);Assert.AreEqual(1000,m.View().Company.ProvisionalPermille);
            Assert.AreEqual((int)PhaseEndReason.Abandoned,m.Engine.DrainEvents().Last(e=>e.Type==LineEventType.PhaseChanged).B);
        }
        [Test]
        public void LargeTicksUseIdenticalAbsoluteTransitions()
        {
            var a=new LineTest();var b=new LineTest();a.Finish();b.Advance(100000);CollectionAssert.AreEqual(LineCodec.EncodeEvents(a.Engine.DrainEvents()),LineCodec.EncodeEvents(b.Engine.DrainEvents()));Assert.AreEqual(a.Engine.Result.ResultHash,b.Engine.Result.ResultHash);
        }
        [Test]
        public void HostContinueReportsReasonAndOnlyWorksInResults()
        {
            var m=new LineTest();m.Engine.HostContinue(m.Now);Assert.IsFalse(m.Engine.IsFinished);m.Until(LinePhase.WeekResults);m.Engine.HostContinue(m.Now);Assert.IsTrue(m.Engine.IsFinished);Assert.AreEqual((int)PhaseEndReason.HostContinue,m.Engine.DrainEvents().Last(e=>e.Type==LineEventType.PhaseChanged).B);
        }
        [Test]
        public void FirstTimeHistorySuppressesTutorialIntros()
        {
            var s=LineTest.MakeSetup();s.History=new LineSessionHistory{WeeksPlayed=1,SeenTasks=Enumerable.Range(1,12).Select(k=>(TaskKind)k).ToList()};var m=new LineTest(s);Assert.AreEqual(12,m.Engine.PhaseEndTime);m.Next();Assert.AreEqual(12,m.Engine.PhaseEndTime-m.Now);Assert.IsFalse(m.View().Features.FirstTimeTask);
        }
    }
}
