using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BirimizKovulacak.Core.Tests.Line;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class LineSnapshotTests
    {
        [TestCase(0),TestCase(1)]
        public void RivalGuardianProbabilityEndpointsApplyAtThreeAndSixPlayers(int same)
        {
            foreach(int n in new[]{3,6})for(int seed=0;seed<12;seed++)
            {
                var s=LineTest.MakeSetup(n,seed);s.ForceObjectives=new List<ObjectiveKind>{ObjectiveKind.Rival,ObjectiveKind.Guardian};var m=new LineTest(s,c=>c.RivalGuardianSameTarget=same);
                int rival=m.View().Mine.Objective.Target,guardian=m.View(1).Mine.Objective.Target;Assert.AreEqual(same==1,rival==guardian);Assert.AreNotEqual(0,rival);Assert.AreNotEqual(1,guardian);
            }
        }
        [TestCase(false),TestCase(true)]
        public void EotmUsesStrictContributionMaximumAndExcludesExposedActor(bool expose)
        {
            var s=LineTest.MakeSetup();s.ForceTasks=new List<TaskKind>{TaskKind.BonusPool,TaskKind.InvoiceReconcile};var m=new LineTest(s,c=>{c.ShortWeekDays=1;c.CompanyTargetPercent=0;});m.Until(LinePhase.RoundPlay);for(int i=0;i<4;i++)m.Submit(i,i==0?1:0);m.Until(LinePhase.RoundPlay,0,1);var views=m.Views();
            for(int id=0;id<4;id++)
            {
                int correct=views.Where(v=>v.Me==id||views[id].Task.Group.Contains(v.Me)).Sum(v=>v.Task.Facts.Where(f=>f.Key=="line").Sum(f=>f.N2));m.Submit(id,id==1?correct:0,id==1&&expose?1:0,fields:TaskField.A|TaskField.B);
            }
            if(expose){m.Until(LinePhase.MeetingDossier);int proof=m.View(1).Mine.Records.Single(r=>r.ProofOf>0).Id;m.Ok(1,LineVerb.PresentRecord,a:proof);}
            m.Finish();Assert.AreEqual(!expose,m.Engine.Result.Players[1].EmployeeOfMonth);if(!expose)Assert.AreEqual(1,m.Engine.Result.Players.Count(p=>p.EmployeeOfMonth));
        }
        [Test]
        public void MoneyObjectivesUseReceivedAndDistinctColleaguesNotNetBalance()
        {
            var s=LineTest.MakeSetup();s.ForceObjectives=new List<ObjectiveKind>{ObjectiveKind.Patron,ObjectiveKind.Broker,ObjectiveKind.Saver,ObjectiveKind.Presenter};var m=new LineTest(s,c=>c.StartMoney=20);
            m.Ok(0,LineVerb.Transfer,1,2);Assert.AreEqual(1,m.View().Mine.Objective.Progress);Assert.AreEqual(2,m.View(1).Mine.Objective.Progress);m.Advance(2);m.Ok(0,LineVerb.Transfer,1,2);Assert.AreEqual(1,m.View().Mine.Objective.Progress);m.Advance(2);m.Ok(0,LineVerb.Transfer,2,2);Assert.AreEqual(2,m.View().Mine.Objective.Progress);m.Ok(1,LineVerb.Transfer,0,4);Assert.AreEqual(4,m.View(1).Mine.Objective.Progress);
        }
        [Test]
        public void GuardianFailsIfTargetWasWarnedEvenWhenCompanySurvives()
        {
            var s=LineTest.MakeSetup();s.ForceObjectives=new List<ObjectiveKind>{ObjectiveKind.Guardian};s.ForceObjectiveTargets=new List<int>{1};var m=new LineTest(s,c=>c.CompanyTargetPercent=0);m.Until(LinePhase.MeetingVote);m.Ok(0,LineVerb.Vote,1);m.Ok(2,LineVerb.Vote,1);m.Finish();Assert.IsFalse(m.Engine.Result.Players[0].ObjectiveDone);
        }
        [Test]
        public void RemedialPerBlockAndMatchCapsCannotBeBypassedByMoreTasks()
        {
            var s=LineTest.MakeSetup();s.ForceTasks=new List<TaskKind>{TaskKind.InvoiceReconcile,TaskKind.CustomerSpec,TaskKind.BudgetSplit};var m=new LineTest(s);
            m.Until(LinePhase.RoundReveal);m.Ok(0,LineVerb.StartRemedial,1,0);m.Ok(1,LineVerb.RespondRemedial,0,1);m.Until(LinePhase.RoundReveal,0,1);Assert.AreEqual(LineFailure.NotAvailable,m.Do(0,LineVerb.StartRemedial,1,1).Failure);
            m.Until(LinePhase.RoundReveal,1,0);m.Ok(0,LineVerb.StartRemedial,1,2);m.Ok(1,LineVerb.RespondRemedial,0,1);Assert.AreEqual(0,m.View().Mine.RemedialsLeftThisMatch);m.Until(LinePhase.RoundReveal,2,0);Assert.IsEmpty(m.View().Mine.RemedialOptions);
        }
        [Test]
        public void RemedialAddsAtMostRemainingOutputAndUpdatesObjectiveProgress()
        {
            var m=LineTest.Task(TaskKind.VendorPick,3,c=>{c.RemedialOutput=100;c.TeamPlayerOutput=90;},seed:731);m.Perfect();m.Reveal();if(m.View().Company.ResolvedOutput==100)Assert.Pass("Seed has all vendors needed for full output.");m.Ok(0,LineVerb.StartRemedial,1,0);m.Ok(1,LineVerb.RespondRemedial,0,1);Assert.AreEqual(100,m.View().Company.ResolvedOutput);Assert.IsEmpty(m.View().Mine.RemedialOptions);
        }
        [Test]
        public void PurchasesRespectMatchCapAndHigherPriceWithoutChargingRejectedRequests()
        {
            var s=LineTest.MakeSetup();s.Length=WeekLength.Full;var m=new LineTest(s,c=>c.StartMoney=100);
            for(int day=0;day<3;day++){m.Until(LinePhase.Morning,day);int before=m.View().Mine.Money;int price=m.View().Mine.Offers.Single(o=>o.Id==100+day).Price;m.Ok(0,LineVerb.BuyEvidence,a:100+day);Assert.AreEqual(before-price,m.View().Mine.Money);}
            m.Until(LinePhase.Morning,3);int money=m.View().Mine.Money;Assert.AreEqual(LineFailure.LimitReached,m.Do(0,LineVerb.BuyEvidence,a:103).Failure);Assert.AreEqual(money,m.View().Mine.Money);
        }
        [Test]
        public void NumberFieldMaskItemsAndRangeValidationRejectWithoutPartialSubmission()
        {
            var m=LineTest.Task(TaskKind.ServerCode);m.Until(LinePhase.RoundPlay);Assert.AreEqual(LineFailure.BadParam,m.Do(0,LineVerb.SubmitTask,a:999,c:2).Failure);Assert.AreEqual(LineFailure.BadParam,m.Do(0,LineVerb.SubmitTask,a:1234,c:8).Failure);Assert.AreEqual(TaskField.None,m.View().Task.SubmittedFields);
            var x=LineTest.Task(TaskKind.SupplySwap);x.Until(LinePhase.RoundPlay);Assert.AreEqual(LineFailure.NotOwned,x.Do(0,LineVerb.GiveItem,1,99).Failure);Assert.AreEqual(LineFailure.Self,x.Do(0,LineVerb.GiveItem,0,0).Failure);
            var b=LineTest.Task(TaskKind.BudgetSplit);b.Until(LinePhase.RoundPlay);Assert.AreEqual(LineFailure.NotAvailable,b.Do(0,LineVerb.SubmitTask,a:2,b:1,c:6).Failure);Assert.AreEqual(TaskField.None,b.View().Task.SubmittedFields);
        }
        [Test]
        public void AbsentSubmissionsAreCensoredAndReconnectDoesNotResetRequests()
        {
            var m=LineTest.Task(TaskKind.InvoiceReconcile);m.Until(LinePhase.RoundPlay);m.Engine.SetConnected(1,false,m.Now);m.Reveal();Assert.AreEqual(5,m.Player(1).Blame);Assert.IsFalse(m.View().Task.Reveal.Missed.Contains(1));var e=m.Engine.DrainEvents().Single(x=>x.Type==LineEventType.HostMissedTask&&x.Actor==1);Assert.AreEqual(3,e.B);
        }
        [Test]
        public void NextManagerChoiceChangesHashAndCarriesToNextSession()
        {
            var m=new LineTest(null,c=>c.CompanyTargetPercent=0);m.Until(LinePhase.FinalVote);for(int i=1;i<4;i++)m.Ok(i,LineVerb.Vote,0);m.Until(LinePhase.WeekResults);ulong hash=m.Engine.Result.ResultHash;Assert.AreEqual(LineFailure.NotAvailable,m.Do(1,LineVerb.PickNextManager,a:0).Failure);m.Ok(0,LineVerb.PickNextManager,a:0);Assert.AreNotEqual(hash,m.Engine.Result.ResultHash);m.Engine.HostContinue(m.Now);var s=LineTest.MakeSetup();s.ManagerProfileId="";s.History=m.Engine.NextHistory();var next=new LineTest(s);Assert.AreEqual("paragoz",next.View().ManagerProfileId);
        }
        [Test]
        public void HashCoversEachResultFieldExceptMatchIdAndHashItself()
        {
            var method=typeof(LineCodec).GetMethod("HashResult",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);var r=new LineWeekResult();ulong before=(ulong)method.Invoke(null,new object[]{r});
            foreach(var field in typeof(LineWeekResult).GetFields())
            {
                if(field.Name=="MatchId"||field.Name=="ResultHash")continue;object old=field.GetValue(r);object changed=null;
                if(field.FieldType==typeof(int))changed=(int)old+1;else if(field.FieldType==typeof(bool))changed=!(bool)old;else if(field.FieldType==typeof(string))changed="changed";else if(field.FieldType==typeof(VoteOutcome))changed=VoteOutcome.Fired;
                else if(field.Name=="Players")changed=new List<LineWeekPlayerResult>{new LineWeekPlayerResult{Player=2}};else if(field.Name=="Exposed")changed=new List<LineIncidentView>{new LineIncidentView{Id=1}};
                field.SetValue(r,changed);Assert.AreNotEqual(before,(ulong)method.Invoke(null,new object[]{r}),field.Name);field.SetValue(r,old);
            }
        }
        [TestCase("SendText",200),TestCase("SendEmail",400),TestCase("FileComplaint",140),TestCase("SendMeetingChat",160)]
        public void MessageLengthEdgesAreCheckedAfterSanitising(string verb,int max)
        {
            var m=new LineTest();var action=(LineVerb)Enum.Parse(typeof(LineVerb),verb);if(action==LineVerb.SendMeetingChat)m.Until(LinePhase.MeetingDiscussion);
            Assert.AreEqual(LineFailure.TooLong,m.Do(0,action,1,a:-1,text:new string('ş',max+1)).Failure);m.Ok(0,action,1,a:-1,text:new string('ş',max));
        }
        [Test]
        public void NoActionTiedWithNomineeIssuesNoWarningAndVotesCanWithdraw()
        {
            var m=new LineTest();m.Until(LinePhase.MeetingVote);m.Ok(1,LineVerb.Vote,0);m.Ok(2,LineVerb.Vote,0);m.Ok(3,LineVerb.Vote,LineIds.Nobody);m.Ok(0,LineVerb.Vote,LineIds.Nobody);m.Next();Assert.AreEqual(VoteOutcome.Tie,m.View().Meeting.Outcome);
            m.Until(LinePhase.MeetingVote,1);m.Ok(1,LineVerb.Vote,0);m.Ok(1,LineVerb.Vote,-1);m.Next();Assert.AreEqual(VoteOutcome.NoVotes,m.View().Meeting.Outcome);
        }
    }
}
