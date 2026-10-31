using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BirimizKovulacak.Core.Tests.Line;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class LineFollowupEventTests
    {
        private static List<LineEvent> Outcomes(LineTest m)
        {
            var events = LineCodec.DecodeEvents(LineCodec.EncodeEvents(m.Engine.DrainEvents()));
            var hosts = events.Where(e => e.Type == LineEventType.HostOutcome || e.Type == LineEventType.HostTaskOutcome).ToList();
            Assert.IsTrue(hosts.All(e => e.Audience == LineAudience.Host));
            foreach (var p in m.Setup.Players)
            {
                var clients = LineCodec.DecodeEvents(LineCodec.EncodeEvents(events.Where(e => e.IsVisibleTo(p.Id)).ToList()));
                Assert.IsFalse(clients.Any(e => e.Type == LineEventType.HostOutcome || e.Type == LineEventType.HostTaskOutcome));
            }
            return hosts;
        }
        [Test]
        public void InvoiceEmitsOneQualityIncomeAndCooperationOutcomePerSeatAndOneFullGroupSummary()
        {
            var m = LineTest.Task(TaskKind.InvoiceReconcile); m.Perfect(); m.Reveal();
            var events = Outcomes(m); var players = events.Where(e => e.Type == LineEventType.HostOutcome).ToList();
            Assert.AreEqual(4, players.Count); CollectionAssert.AreEquivalent(new[] {0,1,2,3}, players.Select(e => e.Actor));
            Assert.IsTrue(players.All(e => e.Target == 0 && e.A == 100 && e.B == 2 && e.C == 7));
            var task = events.Single(e => e.Type == LineEventType.HostTaskOutcome);
            Assert.AreEqual(0, task.A); Assert.AreEqual(0, task.B); Assert.AreEqual(22, task.C);
            m.Engine.Tick(m.Now); m.Next(); Assert.IsEmpty(Outcomes(m));
        }
        [TestCase(100,85,15), TestCase(250,212,38)]
        public void SabotagedInvoiceReportsOnlyActualScaledLossAndDoesNotApplyItAgain(int max,int output,int loss)
        {
            var m = LineTest.Task(TaskKind.InvoiceReconcile, 4, c => c.Task(TaskKind.InvoiceReconcile).MaxOutput = max);
            m.Perfect(); m.Submit(0, -1, 1, fields:TaskField.B); Assert.AreEqual(output, m.Reveal().Output);
            var events = Outcomes(m); var task = events.Single(e => e.Type == LineEventType.HostTaskOutcome);
            Assert.AreEqual(loss, task.B); Assert.AreEqual(21, task.C);
            Assert.AreEqual(2, events.Single(e => e.Type == LineEventType.HostOutcome && e.Actor == 0).B);
            Assert.AreEqual(9, m.View().Mine.Money); m.Next(); Assert.AreEqual(output, m.View().Company.ResolvedOutput);
        }
        [Test]
        public void PaddingOnAnAlreadyEmptyInvoiceHasZeroActualLoss()
        {
            var m = LineTest.Task(TaskKind.InvoiceReconcile); m.Until(LinePhase.RoundPlay);
            for (int i=0;i<4;i++) m.Submit(i,0,1,fields:TaskField.A|TaskField.B);
            Assert.AreEqual(0,m.Reveal().Output); var summary = Outcomes(m).Single(e=>e.Type==LineEventType.HostTaskOutcome);
            Assert.AreEqual(0,summary.B); Assert.AreEqual(20,summary.C);
        }
        [Test]
        public void BonusLegitimateTakingIsNotSabotageAndReportsFilteredIncome()
        {
            var m=LineTest.Task(TaskKind.BonusPool);m.Until(LinePhase.RoundPlay);int partner=m.View().Task.Group[0];
            for(int i=0;i<4;i++)m.Submit(i,i==0?1:0);Assert.AreEqual(80,m.Reveal().Output);
            var events=Outcomes(m);var taker=events.Single(e=>e.Type==LineEventType.HostOutcome&&e.Actor==0);
            Assert.AreEqual(30,taker.A);Assert.AreEqual(4,taker.B);Assert.AreEqual(5,taker.C);
            var sharer=events.Single(e=>e.Type==LineEventType.HostOutcome&&e.Actor==partner);
            Assert.AreEqual(100,sharer.A);Assert.AreEqual(0,sharer.B);Assert.AreEqual(5,sharer.C);
            Assert.IsTrue(events.Where(e=>e.Type==LineEventType.HostOutcome&&e.Actor!=0&&e.Actor!=partner).All(e=>e.A==100&&e.B==2&&e.C==7));
            var task=events.Single(e=>e.Type==LineEventType.HostTaskOutcome);Assert.AreEqual(0,task.B);Assert.AreEqual(21,task.C);
        }
        [TestCase(false),TestCase(true)]
        public void RoomOutcomesDistinguishUsefulYieldFromCollision(bool collisions)
        {
            var m=LineTest.Task(TaskKind.RoomBooking);m.Until(LinePhase.RoundPlay);
            if(collisions)for(int i=0;i<4;i++)m.Submit(i,0);else m.Perfect();
            var choices=m.Views().ToDictionary(v=>v.Me,v=>v.Task.MyA);m.Reveal();var events=Outcomes(m);
            foreach(var e in events.Where(e=>e.Type==LineEventType.HostOutcome))
            {
                bool yield=choices[e.Actor]==9;Assert.AreEqual(collisions?0:100,e.A);Assert.AreEqual(collisions?0:yield?1:2,e.B);Assert.AreEqual(!collisions&&yield?7:5,e.C);
            }
            var task=events.Single(e=>e.Type==LineEventType.HostTaskOutcome);Assert.AreEqual(0,task.B);Assert.AreEqual(collisions?10:11,task.C);
        }
        [Test]
        public void LaterTaskIncomeUsesRunningBlockFilterAndExcludesFavouriteIncome()
        {
            var s=LineTest.MakeSetup();s.ForceTasks=new List<TaskKind>{TaskKind.BonusPool,TaskKind.InvoiceReconcile};
            var m=new LineTest(s,c=>c.TaskRules.BonusAllShareIncome=4);m.Perfect();m.Reveal();m.Engine.DrainEvents();m.Until(LinePhase.RoundPlay,0,1);m.Perfect();m.Reveal();
            var players=Outcomes(m).Where(e=>e.Type==LineEventType.HostOutcome).ToList();Assert.AreEqual(4,players.Count);Assert.IsTrue(players.All(e=>e.Target==1&&e.B==1));
            s=LineTest.MakeSetup();s.ManagerProfileId=LineManagerIds.Kayirmaci;s.ForceTasks=new List<TaskKind>{TaskKind.InvoiceReconcile};m=new LineTest(s);m.Perfect();m.Reveal();int fav=m.View().Favourite;
            Assert.AreEqual(7,m.View(fav).Mine.Money);Assert.AreEqual(2,Outcomes(m).Single(e=>e.Type==LineEventType.HostOutcome&&e.Actor==fav).B);
        }
        [Test]
        public void AbsentSeatStillHasOutcomeWithDisconnectedFlagAndNoEotmContribution()
        {
            var m=LineTest.Task(TaskKind.BonusPool);m.Engine.SetConnected(3,false,m.Now);m.Until(LinePhase.RoundPlay);for(int id=0;id<3;id++)m.Submit(id,0);m.Reveal();
            var events=Outcomes(m);Assert.AreEqual(4,events.Count(e=>e.Type==LineEventType.HostOutcome));var absent=events.Single(e=>e.Type==LineEventType.HostOutcome&&e.Actor==3);
            Assert.AreEqual(0,absent.A);Assert.AreEqual(0,absent.C);Assert.AreEqual(2,absent.B);
        }
        [TestCase(0,10),TestCase(1,10),TestCase(1,100),TestCase(2,10)]
        public void RemedialReportsActualAddedPointsToBothParties(int response,int configuredOutput)
        {
            var m=LineTest.Task(TaskKind.InvoiceReconcile,4,c=>c.RemedialOutput=configuredOutput);m.Perfect();m.Submit(0,-1,1,fields:TaskField.B);m.Reveal();m.Engine.DrainEvents();m.Ok(0,LineVerb.StartRemedial,1,0);
            if(response==2)m.Advance(30);else m.Ok(1,LineVerb.RespondRemedial,0,response);
            var events=m.Engine.DrainEvents().Where(e=>e.Type==LineEventType.RemedialResolved).ToList();int added=response==1?Math.Min(15,configuredOutput):0;
            Assert.AreEqual(2,events.Count);Assert.IsTrue(events.All(e=>e.A==(response==1?1:0)&&e.B==0&&e.C==added));Assert.AreEqual(85+added,m.View().Company.ResolvedOutput);
        }
        [TestCase(false,60),TestCase(true,0)]
        public void ReportLossHoldsApprovalDecisionFixedAndNeverReportsNegativeLoss(bool reject,int loss)
        {
            var m=LineTest.Task(TaskKind.ReportReview);m.Until(LinePhase.RoundPlay);for(int i=0;i<4;i++)m.Submit(i,1);for(int i=0;i<4;i++)m.Submit(i,-1,reject?1:0,fields:TaskField.B);m.Reveal();
            Assert.AreEqual(loss,Outcomes(m).Single(e=>e.Type==LineEventType.HostTaskOutcome).B);
        }
        [Test]
        public void ArchiveCopyReportsItsActualPenalty()
        {
            var m=LineTest.Task(TaskKind.ArchiveVisitor);m.Perfect();m.Submit(0,-1,1,fields:TaskField.B);m.Reveal();var task=Outcomes(m).Single(e=>e.Type==LineEventType.HostTaskOutcome);Assert.AreEqual(15,task.B);Assert.AreEqual(10,task.C);
        }
        [TestCaseSource(typeof(LineRoundTests),nameof(LineRoundTests.Tasks))]
        public void EveryResolvedTaskEmitsExactlyOneSummaryAndAnOutcomeForEachSeat(TaskKind kind,int players)
        {
            var m=LineTest.Task(kind,players);m.Perfect();m.Reveal();var events=Outcomes(m);
            Assert.AreEqual(players,events.Count(e=>e.Type==LineEventType.HostOutcome));Assert.AreEqual(1,events.Count(e=>e.Type==LineEventType.HostTaskOutcome));
            Assert.IsTrue(events.Where(e=>e.Type==LineEventType.HostOutcome).All(e=>e.A>=0&&e.A<=100&&e.B>=0&&(e.C&4)!=0));
        }
    }
}
namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class LineArchivistFollowupTests
    {
        private static LineSetup Setup(int count=4,params TaskKind[] tasks)
        {
            var s=LineTest.MakeSetup(count);s.ForceObjectives=new List<ObjectiveKind>{ObjectiveKind.Archivist};
            s.ForceTasks=tasks.ToList();return s;
        }
        private static void Configure(LineConfig c)
        {c.ArchivistRecords=4;c.ArchivistColleagues=2;c.CompanyTargetPercent=0;c.StartMoney=100;c.PurchasesPerBlock=10;c.PurchasesPerMatch=10;}
        private static TaskKind[] PartnerThenPublicTasks()
        {return new[]{TaskKind.BonusPool,TaskKind.BudgetSplit,TaskKind.RoomBooking,TaskKind.ServerCode,TaskKind.ShiftCover,TaskKind.VendorPick};}
        [Test]
        public void OwnCallsTransfersAndPublicOutcomesWarningsAndRemedialsNeverCount()
        {
            var m=new LineTest(Setup(4,TaskKind.BudgetSplit,TaskKind.RoomBooking),Configure);
            m.Ok(0,LineVerb.Call,1);m.Ok(1,LineVerb.Answer);m.Advance(7);m.Ok(0,LineVerb.HangUp);m.Ok(0,LineVerb.Transfer,2,2);m.Ok(0,LineVerb.BuyEvidence,a:100);
            m.Reveal();m.Ok(0,LineVerb.StartRemedial,1,0);m.Ok(1,LineVerb.RespondRemedial,0,1);m.Until(LinePhase.MeetingVote);m.Ok(0,LineVerb.Vote,1);m.Ok(2,LineVerb.Vote,1);m.Next();
            var v=m.View();Assert.AreEqual(4,v.Mine.Objective.Threshold);Assert.AreEqual(0,v.Mine.Objective.Progress);
            foreach(var kind in new[]{RecordKind.Call,RecordKind.Transfer,RecordKind.TaskOutcome,RecordKind.Warning,RecordKind.Remedial})Assert.IsTrue(v.Mine.Records.Any(r=>r.Kind==kind),kind.ToString());
        }
        [TestCase(2,true),TestCase(3,false),TestCase(4,false)]
        public void PartnerEntriesCountOnceAndUseConfiguredThreshold(int threshold,bool complete)
        {
            var m=new LineTest(Setup(3,PartnerThenPublicTasks()),c=>{Configure(c);c.ArchivistRecords=threshold;});m.Perfect();m.Reveal();Assert.AreEqual(2,m.View().Mine.Objective.Progress);
            m.Ok(0,LineVerb.BuyEvidence,a:200);Assert.AreEqual(2,m.View().Mine.Objective.Progress);Assert.AreEqual(threshold,m.View().Mine.Objective.Threshold);m.Finish();Assert.AreEqual(complete,m.Engine.Result.Players[0].ObjectiveDone);
        }
        [Test]
        public void PresentedRecordsDoNotCountUntilIndependentlyPurchased()
        {
            var m=new LineTest(Setup(4,TaskKind.InvoiceReconcile),Configure);m.Perfect();int foreign=Enumerable.Range(1,3).First(id=>!m.View().Task.Group.Contains(id));m.Submit(foreign,-1,1,fields:TaskField.B);m.Reveal();
            int entry=m.View(foreign).Mine.Records.First(r=>r.Kind==RecordKind.TaskEntry&&r.Actor==foreign).Id;
            int proof=m.View(foreign).Mine.Records.Single(r=>r.Kind==RecordKind.DealPayment&&r.ProofOf>0).Id;
            Assert.AreEqual(1,m.View().Mine.Objective.Progress);m.Until(LinePhase.MeetingDossier);m.Ok(foreign,LineVerb.PresentRecord,a:entry);m.Ok(foreign,LineVerb.PresentRecord,a:proof);
            Assert.IsTrue(m.View().Mine.Records.Any(r=>r.Id==entry));Assert.IsTrue(m.View().Mine.Records.Any(r=>r.Id==proof));Assert.AreEqual(1,m.View().Mine.Objective.Progress);
            m.Ok(0,LineVerb.BuyEvidence,a:100);Assert.AreEqual(2,m.View().Mine.Objective.Progress);
            m.Ok(0,LineVerb.BuyEvidence,a:200);Assert.AreEqual(7,m.View().Mine.Objective.Progress);
        }
        [Test]
        public void PurchaseIncludesFutureForeignTransfersButExcludesOwnTransfers()
        {
            var m=new LineTest(Setup(4,TaskKind.BudgetSplit),Configure);m.Ok(0,LineVerb.BuyEvidence,a:100);m.Ok(0,LineVerb.Transfer,1,1);Assert.AreEqual(0,m.View().Mine.Objective.Progress);
            m.Ok(1,LineVerb.Transfer,2,2);Assert.AreEqual(1,m.View().Mine.Objective.Progress);Assert.AreEqual(2,m.View().Mine.Records.Count(r=>r.Kind==RecordKind.Transfer));
        }
        [Test]
        public void OwnDealAndOfferAcceptanceNeverCountEvenAfterPurchase()
        {
            var s=Setup(4,TaskKind.ServerCode);s.ForceRoles=new List<int>{0};var m=new LineTest(s,Configure);m.Until(LinePhase.RoundPlay);m.Submit(0,-1,1,fields:TaskField.B);m.Reveal();
            Assert.IsTrue(m.View().Mine.Records.Any(r=>r.Kind==RecordKind.OfferAcceptance));Assert.AreEqual(0,m.View().Mine.Objective.Progress);m.Ok(0,LineVerb.BuyEvidence,a:100);Assert.AreEqual(0,m.View().Mine.Objective.Progress);
        }
        [Test]
        public void AuditFiguresAndPartnerReportEntriesCountBeforePurchaseAndForeignDraftsAfterPurchase()
        {
            var m=new LineTest(Setup(4,TaskKind.ReportReview),Configure);m.Perfect();m.Reveal();var v=m.View();int visible=v.Mine.Records.Count(r=>r.Kind==RecordKind.TaskEntry&&r.Actor!=0||r.Kind==RecordKind.AuditFigure&&r.Subject!=0);
            Assert.AreEqual(3,visible);Assert.AreEqual(visible,v.Mine.Objective.Progress);Assert.AreEqual(1,v.Mine.Records.Count(r=>r.Kind==RecordKind.AuditFigure));Assert.IsFalse(v.Mine.Records.Any(r=>r.Kind==RecordKind.ReportDraft));
            m.Ok(0,LineVerb.BuyEvidence,a:200);Assert.GreaterOrEqual(m.View().Mine.Objective.Progress,visible+3);
        }
        [Test]
        public void DistinctColleagueRequirementUsesOnlyQualifyingRecords()
        {
            var m=new LineTest(Setup(4,PartnerThenPublicTasks()),c=>{Configure(c);c.ArchivistRecords=1;});m.Perfect();m.Reveal();int partner=m.View().Task.Group[0];int other=Enumerable.Range(1,3).First(id=>id!=partner);
            m.Ok(0,LineVerb.Call,other);m.Ok(other,LineVerb.Answer);m.Advance(7);m.Ok(0,LineVerb.HangUp);Assert.AreEqual(1,m.View().Mine.Objective.Progress);m.Finish();Assert.IsFalse(m.Engine.Result.Players[0].ObjectiveDone);
        }
    }
}
namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class LineBotFollowupTests
    {
        private static LineView View(int seat,int players=3)
        {
            var v=new LineView{WeekId=42,Me=seat,Phase=LinePhase.MeetingDossier,PhaseEndTime=100};
            for(int i=0;i<players;i++)v.Players.Add(new LinePlayerView{Id=i,Connected=true});
            v.Mine.Money=0;v.Mine.Objective.Kind=ObjectiveKind.Presenter;v.Mine.PresentationsLeft=2;v.Features.PresentationOpen=true;return v;
        }
        [Test]
        public void PresenterBotSkipsPublicAndOwnRecordsAndPresentsOnlyOncePerMeeting()
        {
            var c=LineConfig.CreateDefault();var v=View(0);v.Mine.Records.Add(new LineRecordView{Id=1,Kind=RecordKind.TaskOutcome});v.Mine.Records.Add(new LineRecordView{Id=2,Kind=RecordKind.OfferAcceptance,Actor=0,ProofOf=1});
            var bot=new LineBot(0,33);Assert.IsNull(bot.Next(v,c,0));
            v.Mine.Records.Add(new LineRecordView{Id=3,Kind=RecordKind.TaskEntry,Actor=1});var action=bot.Next(v,c,0);Assert.AreEqual(LineVerb.PresentRecord,action.Verb);Assert.AreEqual(3,action.A);
            v.Mine.PresentationsLeft--;v.Mine.Records.Last().Presented=true;v.Mine.Records.Add(new LineRecordView{Id=4,Kind=RecordKind.TaskEntry,Actor=2});v.Phase=LinePhase.MeetingDiscussion;
            Assert.IsNull(bot.Next(v,c,1));v.Day=1;v.Phase=LinePhase.MeetingDossier;v.Mine.PresentationsLeft=2;action=bot.Next(v,c,2);Assert.AreEqual(LineVerb.PresentRecord,action.Verb);Assert.AreEqual(4,action.A);
        }
        [TestCase(LineBotPolicy.Talk),TestCase(LineBotPolicy.NoComm),TestCase(LineBotPolicy.Greedy)]
        public void EveryPolicyLimitsProofPresentationToOneRecordAboutAnotherPlayer(LineBotPolicy policy)
        {
            var c=LineConfig.CreateDefault();var v=View(0);v.Mine.Objective.Kind=ObjectiveKind.Saver;
            v.Mine.Records.Add(new LineRecordView{Id=1,Kind=RecordKind.OfferAcceptance,Actor=0,ProofOf=1});v.Mine.Records.Add(new LineRecordView{Id=2,Kind=RecordKind.ReportDraft,Actor=1,ProofOf=2});
            var bot=new LineBot(0,15,policy);var first=bot.Next(v,c,0);Assert.AreEqual(2,first.A);v.Mine.PresentationsLeft=1;v.Mine.Records.Last().Presented=true;v.Mine.Records.Add(new LineRecordView{Id=3,Kind=RecordKind.ReportDraft,Actor=2,ProofOf=3});Assert.IsNull(bot.Next(v,c,1));
        }
        [TestCase(LineBotPolicy.Talk),TestCase(LineBotPolicy.NoComm),TestCase(LineBotPolicy.Greedy)]
        public void PatronBotPaysDifferentColleaguesAndCompletesObjective(LineBotPolicy policy)
        {
            var setup=LineTest.MakeSetup();setup.ForceObjectives=new List<ObjectiveKind>{ObjectiveKind.Patron};var m=new LineTest(setup,c=>{c.CompanyTargetPercent=0;c.PurchasesPerBlock=0;});var bot=new LineBot(0,21,policy);
            var first=bot.Next(m.View(),m.Config,m.Now);Assert.AreEqual(LineVerb.Transfer,first.Verb);Assert.AreEqual(1,first.Target);Assert.AreEqual(2,first.A);Assert.IsTrue(m.Engine.Apply(0,first,m.Now).Accepted);
            m.Until(LinePhase.Morning,1);var second=bot.Next(m.View(),m.Config,m.Now);Assert.AreEqual(LineVerb.Transfer,second.Verb);Assert.AreEqual(2,second.Target);Assert.AreEqual(2,second.A);Assert.IsTrue(m.Engine.Apply(0,second,m.Now).Accepted);Assert.AreEqual(2,m.View().Mine.Objective.Progress);
            m.Finish();Assert.IsTrue(m.Engine.Result.Players[0].ObjectiveDone);
        }
        [Test]
        public void PatronUsesVisibleTransferTotalsIncludingPartialPayments()
        {
            var c=LineConfig.CreateDefault();var v=View(0);v.Phase=LinePhase.Morning;v.Features.PresentationOpen=false;v.Features.TransfersOpen=true;v.Mine.Objective=new LineObjectiveView{Kind=ObjectiveKind.Patron,Progress=0,Threshold=2};v.Mine.Money=2;
            v.Mine.Records.Add(new LineRecordView{Id=1,Kind=RecordKind.Transfer,Actor=0,Subject=1,N1=1});var a=new LineBot(0,41).Next(v,c,0);Assert.AreEqual(1,a.Target);Assert.AreEqual(1,a.A);
            v.Mine.Records[0].N1=2;v.Mine.Objective.Progress=1;a=new LineBot(0,41).Next(v,c,0);Assert.AreEqual(2,a.Target);Assert.AreEqual(2,a.A);
        }
        private static LineView ShiftView(int seat,params int[] available)
        {
            var v=View(seat);v.Phase=LinePhase.RoundPlay;v.Features.PresentationOpen=false;v.Mine.Objective.Kind=ObjectiveKind.Saver;v.Mine.PhoneDeadUntil=1000;v.Mine.TextsLeftThisBlock=0;
            v.Task=new LineTaskView{Kind=TaskKind.ShiftCover,Number=0};v.Task.Group.AddRange(Enumerable.Range(0,3).Where(i=>i!=seat));v.Task.PublicFacts.Add(new LineFact{Key="shifts",N1=2});v.Task.Facts.Add(new LineFact{Key="cost",N1=seat==2?2:0});
            foreach(int shift in available)v.Task.Facts.Add(new LineFact{Key="available",N1=shift});v.Task.Controls.Add(new LineControlView{Kind=ControlKind.Choice,Field=TaskField.A,Key="volunteer",Required=true});return v;
        }
        [Test]
        public void ShiftMatchingDoesNotAssignBothShiftsToOneFlexibleColleague()
        {
            var c=LineConfig.CreateDefault();var shared=new[]{ShiftView(0,0,1),ShiftView(1,0),ShiftView(2,1)};var choices=new List<int>();
            for(int i=0;i<3;i++){var a=new LineBot(i,17+i).Next(shared[i],c,0,shared.Reverse().ToArray());Assert.AreEqual(LineVerb.SubmitTask,a.Verb);choices.Add(a.A);}
            CollectionAssert.AreEqual(new[]{1,0,9},choices);var local=new LineBot(0,17,LineBotPolicy.NoComm).Next(shared[0],c,0,shared);Assert.AreEqual(0,local.A);
        }
        [TestCase(LineBotPolicy.NoComm),TestCase(LineBotPolicy.Greedy)]
        public void SoloShiftPoliciesKeepMinimumSlotChoiceRegardlessOfFactOrder(LineBotPolicy policy)
        {
            var v=ShiftView(0,1,0);var a=new LineBot(0,17,policy).Next(v,LineConfig.CreateDefault(),0);
            Assert.AreEqual(LineVerb.SubmitTask,a.Verb);Assert.AreEqual(0,a.A);
        }
        [TestCase(3),TestCase(4),TestCase(5),TestCase(6)]
        public void FullySharedShiftFactsCoverEveryShiftAcrossSeededDeals(int players)
        {
            for(int seed=0;seed<12;seed++)
            {
                var m=LineTest.Task(TaskKind.ShiftCover,players,seed:seed);m.Until(LinePhase.RoundPlay);var shared=m.Views();
                for(int id=0;id<players;id++)
                {
                    var v=m.View(id);v.Features.TransfersOpen=v.Features.EvidenceOpen=v.Features.RemedialOpen=v.Features.ComplaintsOpen=false;v.Mine.PhoneDeadUntil=m.Now+1000;v.Mine.TextsLeftThisBlock=0;
                    var a=new LineBot(id,seed+31*id).Next(v,m.Config,m.Now,shared);Assert.AreEqual(LineVerb.SubmitTask,a.Verb);Assert.IsTrue(m.Engine.Apply(id,a,m.Now).Accepted);
                    Assert.IsTrue(a.A==9||v.Task.Facts.Any(f=>f.Key=="available"&&f.N1==a.A));
                }
                Assert.AreEqual(100,m.Reveal().Output,players+" players, seed "+seed);
            }
        }
    }
}
