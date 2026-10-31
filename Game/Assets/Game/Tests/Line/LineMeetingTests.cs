using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BirimizKovulacak.Core.Tests.Line;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class LineMeetingTests
    {
        [TestCase("warning",VoteOutcome.WarningGiven),TestCase("tie",VoteOutcome.Tie),TestCase("below",VoteOutcome.BelowMinimum),TestCase("none",VoteOutcome.NoVotes),TestCase("noaction",VoteOutcome.NoActionWon)]
        public void DailyBallotOutcomesMatchStrictPluralityAndMinimum(string ballot,VoteOutcome expected)
        {
            var m=new LineTest();m.Until(LinePhase.MeetingVote);
            if(ballot=="warning"){m.Ok(1,LineVerb.Vote,0);m.Ok(2,LineVerb.Vote,0);}
            if(ballot=="tie"){m.Ok(1,LineVerb.Vote,0);m.Ok(2,LineVerb.Vote,0);m.Ok(0,LineVerb.Vote,1);m.Ok(3,LineVerb.Vote,1);}
            if(ballot=="below")m.Ok(1,LineVerb.Vote,0);
            if(ballot=="noaction"){m.Ok(1,LineVerb.Vote,0);m.Ok(2,LineVerb.Vote,LineIds.Nobody);m.Ok(3,LineVerb.Vote,LineIds.Nobody);}
            m.Next();Assert.AreEqual(expected,m.View().Meeting.Outcome);Assert.AreEqual(expected==VoteOutcome.WarningGiven?1:0,m.Player(0).Warnings);Assert.AreEqual(4,m.View().Meeting.Votes.Count);Assert.AreEqual(1,m.Engine.DrainEvents().Count(e=>e.Type==LineEventType.HostBallot));
        }
        [Test]
        public void WarningCapDoesNotReassignNomination()
        {
            var s=LineTest.MakeSetup();s.Length=WeekLength.Full;var m=new LineTest(s);
            for(int day=0;day<3;day++){m.Until(LinePhase.MeetingVote,day);m.Ok(1,LineVerb.Vote,0);m.Ok(2,LineVerb.Vote,0);m.Next();Assert.AreEqual(day==2?VoteOutcome.Capped:VoteOutcome.WarningGiven,m.View().Meeting.Outcome);}
            Assert.AreEqual(2,m.Player(0).Warnings);
        }
        [Test]
        public void EveryPresentPlayerDefendsInBlameWarningSeedOrder()
        {
            var m=new LineTest(null,c=>c.CompanyTargetPercent=0);m.Until(LinePhase.MeetingVote);m.Ok(1,LineVerb.Vote,0);m.Ok(2,LineVerb.Vote,0);m.Until(LinePhase.FinalDefense);var order=m.View().Meeting.DefenseOrder;Assert.AreEqual(4,order.Count);Assert.AreEqual(0,order[0]);
            for(int i=0;i<order.Count;i++){var v=m.View();Assert.AreEqual(order[i],v.Meeting.Defender);foreach(int id in Enumerable.Range(0,4))Assert.AreEqual(id==order[i],m.Engine.MaySpeak(id));m.Advance(15);}
            Assert.AreEqual(LinePhase.FinalVote,m.Engine.Phase);
        }
        [TestCase(0,VoteOutcome.NoVotes,-2),TestCase(1,VoteOutcome.NoMajority,-2),TestCase(2,VoteOutcome.NoMajority,-2),TestCase(3,VoteOutcome.Fired,0)]
        public void FinalRequiresStrictRosterMajority(int votes,VoteOutcome outcome,int fired)
        {
            var m=new LineTest(null,c=>c.CompanyTargetPercent=0);m.Until(LinePhase.FinalVote);Assert.AreEqual(3,m.View().Meeting.MajorityNeeded);
            for(int i=1;i<=votes;i++)m.Ok(i,LineVerb.Vote,0);m.Next();Assert.AreEqual(outcome,m.View().Meeting.Outcome);Assert.AreEqual(fired,m.View().Meeting.Fired);Assert.AreEqual(fired,m.Engine.DrainEvents().Single(e=>e.Type==LineEventType.Fired).Target);
        }
        [Test]
        public void RosterSnapshotRetainsDisconnectedVotesAndExcludesLateJoiners()
        {
            var m=new LineTest(null,c=>c.CompanyTargetPercent=0);m.Until(LinePhase.FinalDefense);m.Engine.SetConnected(3,false,m.Now);m.Until(LinePhase.FinalVote);Assert.AreEqual(3,m.View().Meeting.Roster.Count);Assert.AreEqual(2,m.View().Meeting.MajorityNeeded);
            m.Ok(1,LineVerb.Vote,0);m.Ok(2,LineVerb.Vote,0);m.Engine.SetConnected(1,false,m.Now);m.Engine.SetConnected(3,true,m.Now);Assert.AreEqual(LineFailure.NotAvailable,m.Do(3,LineVerb.Vote,0).Failure);m.Next();Assert.AreEqual(0,m.View().Meeting.Fired);Assert.AreEqual(0,m.View().Meeting.Votes.Single(v=>v.Voter==1).Target);
        }
        [Test]
        public void FireNobodyIsARealBallotOptionAndSelfVotesAreRejected()
        {
            var m=new LineTest(null,c=>c.CompanyTargetPercent=0);m.Until(LinePhase.FinalVote);Assert.AreEqual(LineFailure.Self,m.Do(0,LineVerb.Vote,0).Failure);for(int i=0;i<4;i++)m.Ok(i,LineVerb.Vote,LineIds.Nobody);m.Next();Assert.AreEqual(LineIds.Nobody,m.View().Meeting.Fired);Assert.AreEqual(VoteOutcome.NoMajority,m.View().Meeting.Outcome);
        }
        [Test]
        public void CompanyClosureHasPostMortemSkipsDefenseVoteAndHasNoPositiveAwardsOrRanks()
        {
            var m=LineTest.Task(TaskKind.InvoiceReconcile);m.Until(LinePhase.MeetingDossier,2);Assert.IsTrue(m.View().Company.Closed);m.Next();Assert.AreEqual(LinePhase.MeetingDiscussion,m.Engine.Phase);m.Next();Assert.AreEqual(LinePhase.FinalVerdict,m.Engine.Phase);Assert.AreEqual((int)PhaseEndReason.Skipped,m.Engine.DrainEvents().Last(e=>e.Type==LineEventType.PhaseChanged).B);m.Next();
            Assert.IsTrue(m.Engine.Result.CompanyClosed);Assert.IsTrue(m.Engine.Result.Players.All(p=>p.Fired&&p.Rank==0&&!p.EmployeeOfMonth&&p.Career.All(c=>c.Points<=0)));
        }
        [Test]
        public void CareerAwardsIncludeFiredTargetAwardAndCompetitionRanks()
        {
            var s=LineTest.MakeSetup();s.ForceObjectives=new List<ObjectiveKind>{ObjectiveKind.Saver,ObjectiveKind.Broker,ObjectiveKind.Presenter,ObjectiveKind.Patron};
            var m=new LineTest(s,c=>{c.CompanyTargetPercent=0;c.CareerTargetPercent=0;c.SaverMargin=0;c.BrokerReceived=0;c.PresenterRecords=999;c.PatronEach=999;c.CareerEmployeeOfMonth=0;});
            m.Until(LinePhase.FinalVote);for(int i=1;i<4;i++)m.Ok(i,LineVerb.Vote,0);m.Until(LinePhase.WeekResults);var result=m.Engine.Result;
            Assert.AreEqual(135,result.Players[0].CareerTotal);Assert.IsFalse(result.Players[0].Career.Any(c=>c.Item==CareerItem.NotFired));Assert.AreEqual(235,result.Players[1].CareerTotal);Assert.AreEqual(175,result.Players[2].CareerTotal);Assert.AreEqual(175,result.Players[3].CareerTotal);Assert.AreEqual(2,result.Players[2].Rank);Assert.AreEqual(2,result.Players[3].Rank);Assert.AreEqual(4,result.Players[0].Rank);
        }
        [Test]
        public void EqualCareerTotalsRankOneOneThreeAndEotmTiesAwardNobody()
        {
            var s=LineTest.MakeSetup();s.ForceTasks=new List<TaskKind>{TaskKind.BonusPool,TaskKind.InvoiceReconcile};s.ForceObjectives=new List<ObjectiveKind>{ObjectiveKind.Saver,ObjectiveKind.Broker,ObjectiveKind.Presenter,ObjectiveKind.Patron};
            var m=new LineTest(s,c=>{c.ShortWeekDays=1;c.SaverMargin=0;c.BrokerReceived=0;c.PresenterRecords=999;c.PatronEach=999;});m.Perfect();m.Until(LinePhase.RoundPlay,0,1);m.Perfect();m.Finish();var r=m.Engine.Result;
            CollectionAssert.AreEqual(new[]{1,1,3,3},r.Players.Select(p=>p.Rank));Assert.IsTrue(r.Players.All(p=>!p.EmployeeOfMonth));Assert.IsTrue(r.Players.All(p=>p.ContributionQuality==100));
        }
        [TestCase(0),TestCase(1),TestCase(2),TestCase(3),TestCase(4),TestCase(5),TestCase(6),TestCase(7),TestCase(8)]
        public void EachObjectiveHasLiveProgressAndEndEvaluation(int offset)
        {
            var s=LineTest.MakeSetup();s.ForceObjectives=Enumerable.Range(0,4).Select(i=>(ObjectiveKind)((i+offset)%9+1)).ToList();s.ForceObjectiveTargets=new List<int>{1,2,3,0};
            var m=new LineTest(s,c=>{c.CompanyTargetPercent=0;c.SaverMargin=0;c.BrokerReceived=0;c.PatronEach=1;c.PatronColleagues=1;c.TeamPlayerTasks=0;c.ArchivistRecords=0;c.ArchivistColleagues=0;c.PresenterRecords=0;});
            var kind=s.ForceObjectives[0];var first=m.View().Mine.Objective;Assert.AreEqual(kind,first.Kind);if(kind==ObjectiveKind.Patron)m.Ok(0,LineVerb.Transfer,1,1);
            if(kind==ObjectiveKind.Rival){m.Until(LinePhase.MeetingVote);m.Ok(0,LineVerb.Vote,1);m.Ok(2,LineVerb.Vote,1);}
            if(kind==ObjectiveKind.CleanRecord)
            {while(!m.Engine.IsFinished){if(m.Engine.Phase==LinePhase.RoundIntro)m.Perfect();else m.Next();}}
            else m.Finish();Assert.IsTrue(m.Engine.Result.Players[0].ObjectiveDone,"Objective "+kind);
            Assert.AreEqual(1,m.Engine.DrainEvents().Count(e=>e.Type==LineEventType.HostObjective&&e.Actor==0));
        }
        [Test]
        public void RivalGuardianShareOnlyLegalTargetsAndSessionHistoryIsDetached()
        {
            var s=LineTest.MakeSetup();s.ForceObjectives=new List<ObjectiveKind>{ObjectiveKind.Rival,ObjectiveKind.Guardian,ObjectiveKind.Saver,ObjectiveKind.Broker};
            var m=new LineTest(s,c=>{c.RivalGuardianSameTarget=1;c.CompanyTargetPercent=0;});Assert.AreEqual(m.View().Mine.Objective.Target,m.View(1).Mine.Objective.Target);Assert.That(m.View().Mine.Objective.Target,Is.InRange(2,3));m.Finish();var h=m.Engine.NextHistory();Assert.AreEqual(1,h.WeeksPlayed);Assert.AreEqual(6,h.SeenTasks.Count);Assert.AreEqual(6,h.TaskUses.Sum());Assert.AreEqual(1,h.PastManagers.Count);h.SessionPoints[0]=999999;Assert.AreNotEqual(999999,m.Engine.NextHistory().SessionPoints[0]);
        }
    }
}
namespace BirimizKovulacak.Core.Tests
{
    [NUnit.Framework.TestFixture]
    public sealed class LineCooperationTests
    {
        [NUnit.Framework.Test]
        public void RemedialCooperationStaysNormalisedToOneOpportunityPerTask()
        {
            var m=BirimizKovulacak.Core.Tests.Line.LineTest.Task(TaskKind.BudgetSplit);m.Until(LinePhase.RoundPlay);for(int i=0;i<4;i++)m.Submit(i,0);m.Reveal();m.Ok(0,LineVerb.StartRemedial,1,0);m.Ok(1,LineVerb.RespondRemedial,0,1);
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var seats=(System.Array)m.Engine.GetType().GetField("seats",flags).GetValue(m.Engine);
            for(int id=0;id<2;id++){var seat=seats.GetValue(id);NUnit.Framework.Assert.AreEqual(1,seat.GetType().GetField("Opportunities",flags).GetValue(seat));NUnit.Framework.Assert.AreEqual(1,seat.GetType().GetField("Credits",flags).GetValue(seat));}
        }
    }
}
