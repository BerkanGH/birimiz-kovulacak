using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BirimizKovulacak.Core.Tests.Line;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class LineRoundTests
    {
        public static IEnumerable<TestCaseData> Tasks()
        {for(int k=1;k<=12;k++)for(int n=3;n<=6;n++)yield return new TestCaseData((TaskKind)k,n);}
        [TestCaseSource(nameof(Tasks))]
        public void CooperativeResolutionFollowsEveryTaskTable(TaskKind kind,int players)
        {
            var m=LineTest.Task(kind,players);m.Perfect();var reveal=m.Reveal();
            if(kind==TaskKind.VendorPick&&players==3)
            {var win=reveal.PublicFacts.Single(f=>f.Key=="winner");Assert.AreEqual((100*win.N2+4)/9,reveal.Output);}
            else Assert.AreEqual(100,reveal.Output);
            Assert.AreEqual(0,reveal.Missed.Count);Assert.AreEqual(reveal.Output*10,m.View().Company.ProvisionalPermille);
            Assert.AreEqual(1,m.Engine.DrainEvents().Count(e=>e.Type==LineEventType.TaskResolved));
            Assert.IsTrue(m.Views().All(v=>v.Mine.Money>=m.Config.StartMoney));
        }
        [TestCase(3),TestCase(4),TestCase(5),TestCase(6)]
        public void PairGroupsCoverSeatsAndHaveOneTrioOnlyWhenOdd(int players)
        {
            var m=LineTest.Task(TaskKind.InvoiceReconcile,players);m.Until(LinePhase.RoundPlay);var views=m.Views();
            Assert.AreEqual(players%2==1?3:0,views.Count(v=>v.Task.Group.Count==2));
            foreach(var v in views){Assert.IsFalse(v.Task.Group.Contains(v.Me));foreach(int p in v.Task.Group)Assert.Contains(v.Me,views[p].Task.Group);}
        }
        [TestCase(0,100,2),TestCase(1,70,4),TestCase(2,40,2),TestCase(3,20,1)]
        public void BonusTrioPayoffsAreProductiveAndNotIncidents(int takers,int output,int takerCredit)
        {
            var m=LineTest.Task(TaskKind.BonusPool,3);m.Until(LinePhase.RoundPlay);
            for(int i=0;i<3;i++)m.Submit(i,i<takers?1:0);
            Assert.AreEqual(output,m.Reveal().Output);
            for(int i=0;i<3;i++)Assert.AreEqual(4+(takers==0?2:i<takers?takerCredit:0),m.View(i).Mine.Money);
            Assert.IsFalse(m.Engine.DrainEvents().Any(e=>e.Type==LineEventType.HostIncident));
            Assert.IsTrue(m.Views().All(v=>v.Mine.Records.Count(r=>r.Kind==RecordKind.TaskEntry)==3));
        }
        [TestCase(0,100),TestCase(1,80),TestCase(2,60)]
        public void BonusPairsUseThePairTable(int takers,int output)
        {
            var m=LineTest.Task(TaskKind.BonusPool);m.Until(LinePhase.RoundPlay);var group=new List<int>(m.View().Task.Group){0};
            for(int id=0;id<4;id++)m.Submit(id,group.IndexOf(id)>=0&&group.IndexOf(id)<takers?1:0);
            Assert.AreEqual(output,m.Reveal().Output);
        }
        [TestCase(3,10),TestCase(4,40),TestCase(5,25),TestCase(6,40)]
        public void InvoicePaddingLossAppliedOncePerGroupAndEveryPadHasBankProof(int players,int expected)
        {
            var m=LineTest.Task(TaskKind.InvoiceReconcile,players);m.Until(LinePhase.RoundPlay);var views=m.Views();
            foreach(var v in views){int total=views.Where(x=>x.Me==v.Me||v.Task.Group.Contains(x.Me)).Sum(x=>x.Task.Facts.Where(f=>f.Key=="line").Sum(f=>f.N2));m.Submit(v.Me,total,1,fields:TaskField.A|TaskField.B);}
            Assert.AreEqual(expected,m.Reveal().Output);foreach(var v in m.Views()){Assert.AreEqual(9,v.Mine.Money);Assert.AreEqual(1,v.Mine.Records.Count(r=>r.Kind==RecordKind.DealPayment&&r.ProofOf>0));}
            m.Next();Assert.AreEqual(expected*10,m.View().Company.ProvisionalPermille);
        }
        [TestCase(false,false,100,7),TestCase(false,true,50,4),TestCase(true,false,40,7),TestCase(true,true,70,5)]
        public void ReportOutputAndIncomeTable(bool polished,bool rejected,int output,int money)
        {
            var m=LineTest.Task(TaskKind.ReportReview);m.Until(LinePhase.RoundPlay);
            for(int i=0;i<4;i++)m.Submit(i,polished?1:0);for(int i=0;i<4;i++)m.Submit(i,-1,rejected?1:0,fields:TaskField.B);
            Assert.AreEqual(output,m.Reveal().Output);foreach(var v in m.Views())Assert.AreEqual(money,v.Mine.Money);
            Assert.AreEqual(polished?4:0,m.Engine.DrainEvents().Count(e=>e.Type==LineEventType.HostIncident));
        }
        [Test]
        public void ReportLocksAndDependenciesAreAtomic()
        {
            var m=LineTest.Task(TaskKind.ReportReview);m.Until(LinePhase.RoundPlay);var v=m.View();
            Assert.AreEqual(LineFailure.Waiting,m.Do(0,LineVerb.SubmitTask,a:0,b:0,c:6).Failure);Assert.AreEqual(TaskField.None,m.View().Task.SubmittedFields);
            m.Submit(0,0);Assert.AreEqual(LineFailure.Locked,m.Do(0,LineVerb.SubmitTask,a:1,c:2).Failure);
            Assert.AreEqual(-1,LineTest.Fact(v.Task,"review_figure").N1);
        }
        [Test]
        public void BudgetOverflowUsesFlooredProportionalGrants()
        {
            var m=LineTest.Task(TaskKind.BudgetSplit);m.Until(LinePhase.RoundPlay);for(int i=0;i<4;i++)m.Submit(i,10);
            var r=m.Reveal();Assert.IsTrue(r.PublicFacts.Where(f=>f.Key=="grant").All(f=>f.N1==10&&f.N2==2));Assert.AreEqual(r.PublicFacts.Count(f=>f.Key=="grant"&&f.N3==1)*25,r.Output);
        }
        [Test]
        public void CatalogueHasExactlyOneMatchingSpecAndPartialPrivateAttributes()
        {
            var m=LineTest.Task(TaskKind.CustomerSpec,5);m.Until(LinePhase.RoundPlay);var views=m.Views();
            foreach(var v in views){Assert.Less(v.Task.Facts.Count(f=>f.Key=="color"||f.Key=="size"||f.Key=="day"),3);Assert.AreEqual(6,v.Task.PublicFacts.Count(f=>f.Key=="spec"));}
            m.Perfect();Assert.AreEqual(100,m.Reveal().Output);
        }
        [Test]
        public void RoomCollisionsCancelBothAndYieldEarnsGoodwill()
        {
            var m=LineTest.Task(TaskKind.RoomBooking);m.Until(LinePhase.RoundPlay);for(int i=0;i<4;i++)m.Submit(i,0);Assert.AreEqual(0,m.Reveal().Output);
            Assert.IsTrue(m.Views().All(v=>v.Mine.Money==4));
            var y=LineTest.Task(TaskKind.RoomBooking);y.Until(LinePhase.RoundPlay);for(int i=0;i<4;i++)y.Submit(i,9);Assert.AreEqual(0,y.Reveal().Output);Assert.IsTrue(y.Views().All(v=>v.Mine.Money==5));
        }
        [Test]
        public void ArchiveCapacityAndSnoopAreBoundedAndHrFileIsCopierOnly()
        {
            var m=LineTest.Task(TaskKind.ArchiveVisitor);m.Until(LinePhase.RoundPlay);Assert.AreEqual(LineFailure.BadParam,m.Do(0,LineVerb.SubmitTask,a:63,c:2).Failure);
            m.Perfect();m.Submit(0,-1,1,fields:TaskField.B);Assert.AreEqual(85,m.Reveal().Output);Assert.AreEqual(1,m.View().Mine.Records.Count(r=>r.Kind==RecordKind.HrFile));Assert.IsFalse(m.View(1).Mine.Records.Any(r=>r.Kind==RecordKind.HrFile));
        }
        [TestCase(1,30),TestCase(2,40),TestCase(3,100),TestCase(4,100)]
        public void ServerRequiresStrictMajorityOfAllSeats(int exact,int output)
        {
            var m=LineTest.Task(TaskKind.ServerCode);m.Until(LinePhase.RoundPlay);int code=0;foreach(var f in m.Views().Select(v=>LineTest.Fact(v.Task,"my_digit")).OrderBy(f=>f.N1))code=code*10+f.N2;
            for(int i=0;i<4;i++)m.Submit(i,i<exact?code:code==1000?1001:1000);
            m.Submit(0,-1,1,fields:TaskField.B);Assert.AreEqual(output,m.Reveal().Output);Assert.IsTrue(m.View().Mine.Records.Any(r=>r.Kind==RecordKind.OfferAcceptance&&r.ProofOf>0));
        }
        [Test]
        public void PhishingTiesBlockNothingAndAcceptanceIsProofEvenWithoutPayoff()
        {
            var m=LineTest.Task(TaskKind.PhishingCheck);m.Until(LinePhase.RoundPlay);var insider=m.Views().Single(v=>v.Task.Role==1);m.Submit(insider.Me,-1,1,fields:TaskField.B);
            for(int i=0;i<4;i++)m.Submit(i,-1,target:i%2,fields:TaskField.Target);
            Assert.AreEqual(25,m.Reveal().Output);Assert.AreEqual(8,m.View(insider.Me).Mine.Money);
        }
        [Test]
        public void ShiftUnavailableChoicesProduceNoCoverageOrIncome()
        {
            var m=LineTest.Task(TaskKind.ShiftCover);m.Until(LinePhase.RoundPlay);for(int i=0;i<4;i++)m.Submit(i,9);Assert.AreEqual(0,m.Reveal().Output);Assert.IsTrue(m.Views().All(v=>v.Mine.Money==4));
        }
        [Test]
        public void VendorKickbackHasWeakOutputAndStableProof()
        {
            var m=LineTest.Task(TaskKind.VendorPick);m.Until(LinePhase.RoundPlay);int vendor=LineTest.Fact(m.View().Task,"kickback").N1;for(int i=0;i<4;i++)m.Submit(i,vendor);m.Submit(0,-1,1,fields:TaskField.B);
            var r=m.Reveal();Assert.LessOrEqual(r.Output,56);Assert.AreEqual(7,m.View().Mine.Money);Assert.IsTrue(m.View().Mine.Records.Any(x=>x.Kind==RecordKind.OfferAcceptance&&x.ProofOf>0));
        }
        [TestCase(3),TestCase(4),TestCase(5),TestCase(6)]
        public void SupplyExactCoverStartsWithNoNeededItemsAndNeverEarlyEnds(int n)
        {
            var m=LineTest.Task(TaskKind.SupplySwap,n);m.Until(LinePhase.RoundPlay);foreach(var v in m.Views()){var needs=v.Task.Facts.Where(f=>f.Key=="need").Select(f=>f.N1);Assert.IsFalse(v.Task.Items.Any(needs.Contains));m.Submit(v.Me,1);}
            Assert.AreEqual(0,m.Engine.DrainEvents().Count(e=>e.Type==LineEventType.HostEarlySubmit));m.Perfect();Assert.AreEqual(100,m.Reveal().Output);
        }
        [TestCase(TaskKind.InvoiceReconcile,0),TestCase(TaskKind.BonusPool,100),TestCase(TaskKind.ReportReview,0),TestCase(TaskKind.RoomBooking,0),TestCase(TaskKind.ServerCode,20),TestCase(TaskKind.SupplySwap,0)]
        public void MissingRequiredControlsUseDefaultsAndBlameExcludesTrading(TaskKind kind,int output)
        {
            var m=LineTest.Task(kind);Assert.AreEqual(output,m.Reveal().Output);Assert.AreEqual(kind==TaskKind.SupplySwap?0:4,m.View().Task.Reveal.Missed.Count);Assert.AreEqual(kind==TaskKind.SupplySwap?5:6,m.Player(0).Blame);
        }
    }
}

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class LineTaskEconomyTests
    {
        [TestCaseSource(typeof(LineRoundTests),nameof(LineRoundTests.Tasks))]
        public void EveryTaskPaysItsSpecifiedRawIncomeBeforeTheBlockFilter(TaskKind kind,int players)
        {
            var m=LineTest.Task(kind,players);m.Perfect();var views=m.Views();m.Reveal();var reveal=m.View().Task.Reveal;var events=m.Engine.DrainEvents();
            foreach(var v in views)
            {
                int expected=0;
                switch(kind)
                {
                    case TaskKind.InvoiceReconcile:case TaskKind.BudgetSplit:case TaskKind.CustomerSpec:case TaskKind.BonusPool:case TaskKind.ServerCode:case TaskKind.SupplySwap:expected=2;break;
                    case TaskKind.ReportReview:expected=3;break;
                    case TaskKind.RoomBooking:expected=v.Task.MyA==9?1:2;break;
                    case TaskKind.ArchiveVisitor:
                        if(v.Task.Role==1)expected=players-2;
                        else {var visitor=views.Single(x=>x.Task.Role==1);expected=(visitor.Task.MyA&(1<<v.Task.MyA))!=0?2:0;}break;
                    case TaskKind.PhishingCheck:expected=1;break;
                    case TaskKind.ShiftCover:expected=v.Task.MyA==9?0:Math.Max(0,3-LineTest.Fact(v.Task,"cost").N1);break;
                    case TaskKind.VendorPick:expected=reveal.PublicFacts.Single(f=>f.Key=="winner").N2==9?1:0;break;
                }
                Assert.AreEqual(expected,events.Where(e=>e.Type==LineEventType.HostIncome&&e.Actor==v.Me&&e.B==(int)MoneyReason.TaskIncome).Sum(e=>e.C),kind+" seat "+v.Me);
                Assert.AreEqual(m.Config.StartMoney+expected,m.View(v.Me).Mine.Money,kind+" credited money");
            }
        }
        [Test]
        public void SurplusBudgetIncomeIsCappedAndCommissionIsLegalDealMoney()
        {
            var budget=LineTest.Task(TaskKind.BudgetSplit);budget.Until(LinePhase.RoundPlay);var vs=budget.Views();for(int i=0;i<4;i++)budget.Submit(i,i==0?LineTest.Fact(vs[0].Task,"need").N1+2:0);budget.Reveal();Assert.AreEqual(8,budget.View().Mine.Money);
            var spec=LineTest.Task(TaskKind.CustomerSpec);spec.Until(LinePhase.RoundPlay);int commission=LineTest.Fact(spec.View().Task,"commission").N1;spec.Submit(0,commission);spec.Reveal();Assert.AreEqual(6,spec.View().Mine.Money);Assert.IsFalse(spec.Engine.DrainEvents().Any(e=>e.Type==LineEventType.HostIncident));
        }
        [Test]
        public void AcceptedOfferCreatesProofEvenIfTheCompanyTaskSucceeds()
        {
            var m=LineTest.Task(TaskKind.ServerCode);m.Perfect();m.Submit(0,-1,1,fields:TaskField.B);Assert.AreEqual(100,m.Reveal().Output);Assert.AreEqual(6,m.View().Mine.Money);Assert.AreEqual(1,m.View().Mine.Records.Count(r=>r.Kind==RecordKind.OfferAcceptance&&r.ProofOf>0));
        }
        [Test]
        public void PolishedDraftAndBadgeCopyArePurchasableProofs()
        {
            var report=LineTest.Task(TaskKind.ReportReview);report.Until(LinePhase.RoundPlay);for(int i=0;i<4;i++)report.Submit(i,1);for(int i=0;i<4;i++)report.Submit(i,-1,1,fields:TaskField.B);report.Reveal();report.Ok(1,LineVerb.BuyEvidence,a:200);Assert.AreEqual(4,report.View(1).Mine.Records.Count(r=>r.Kind==RecordKind.ReportDraft&&r.ProofOf>0));
            var archive=LineTest.Task(TaskKind.ArchiveVisitor);archive.Perfect();archive.Submit(0,-1,1,fields:TaskField.B);archive.Reveal();archive.Ok(1,LineVerb.BuyEvidence,a:400);Assert.AreEqual(1,archive.View(1).Mine.Records.Count(r=>r.Kind==RecordKind.BadgeAccess&&r.N2==1&&r.ProofOf>0));Assert.IsFalse(archive.View(1).Mine.Records.Any(r=>r.Kind==RecordKind.HrFile));
        }
        [Test]
        public void FinalDiscussionCanExposeButTransferIsClosedDuringFinalVote()
        {
            var m=LineTest.Task(TaskKind.InvoiceReconcile,4,c=>c.CompanyTargetPercent=0);m.Perfect();m.Submit(0,-1,1,fields:TaskField.B);m.Reveal();int id=m.View().Mine.Records.Single(r=>r.ProofOf>0).Id;m.Until(LinePhase.MeetingDiscussion,2);m.Ok(0,LineVerb.PresentRecord,a:id);m.Ok(0,LineVerb.Transfer,1,1);m.Until(LinePhase.FinalVote);Assert.AreEqual(LineFailure.WrongPhase,m.Do(0,LineVerb.Transfer,1,1).Failure);
        }
    }
}
