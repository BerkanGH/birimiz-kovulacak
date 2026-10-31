using System;
using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using BirimizKovulacak.Core.Tests.Line;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class LineHostEventTests
    {
        private static void Category(LineTest m,int actor,int value,int category,TaskField field=TaskField.A)
        {
            m.Engine.DrainEvents();m.Submit(actor,field==TaskField.A?value:-1,field==TaskField.B?value:-1,field==TaskField.Target?value:-1,field);
            var e=m.Engine.DrainEvents().Single(x=>x.Type==LineEventType.HostChoice);Assert.AreEqual((int)field,e.Target);Assert.AreEqual(category,e.A);Assert.AreEqual(LineAudience.Host,e.Audience);Assert.AreEqual(0,e.B);Assert.IsFalse(e.IsVisibleTo(actor));
        }
        [TestCase(0),TestCase(1),TestCase(2),TestCase(3)]
        public void BudgetChoiceCategoriesEncodeNeedRelativeRequests(int category)
        {
            var m=LineTest.Task(TaskKind.BudgetSplit);m.Until(LinePhase.RoundPlay);int need=LineTest.Fact(m.View().Task,"need").N1;Category(m,0,need+category-1,category);
        }
        [Test]
        public void InvoiceCategoriesDistinguishCorrectWrongAndPadding()
        {
            var m=LineTest.Task(TaskKind.InvoiceReconcile);m.Until(LinePhase.RoundPlay);var v=m.View();int total=m.Views().Where(x=>x.Me==0||v.Task.Group.Contains(x.Me)).Sum(x=>x.Task.Facts.Where(f=>f.Key=="line").Sum(f=>f.N2));Category(m,0,total,1);Category(m,0,0,0);Category(m,0,1,1,TaskField.B);
        }
        [Test]
        public void SpecCommissionAndReportDecisionCategoriesAreSemantic()
        {
            var m=LineTest.Task(TaskKind.CustomerSpec);m.Until(LinePhase.RoundPlay);Category(m,0,LineTest.Fact(m.View().Task,"commission").N1,2);
            var r=LineTest.Task(TaskKind.ReportReview);r.Until(LinePhase.RoundPlay);for(int i=0;i<4;i++)r.Submit(i,0);Category(r,0,0,10,TaskField.B);Category(r,0,1,1,TaskField.B);
        }
        [Test]
        public void RoomArchiveBonusShiftAndVendorCategoriesUseTheirDocumentedCodes()
        {
            var room=LineTest.Task(TaskKind.RoomBooking);room.Until(LinePhase.RoundPlay);Category(room,0,9,0);Category(room,0,LineTest.Fact(room.View().Task,"ok_slot").N1,1);
            var archive=LineTest.Task(TaskKind.ArchiveVisitor);archive.Until(LinePhase.RoundPlay);var requester=archive.View(1);int code=requester.Task.Controls.First(c=>c.Key=="confirm").Options.First(o=>o.N2==LineTest.Fact(requester.Task,"need_doc").N1).Value;Category(archive,1,code,1);Category(archive,0,1<<code,1);Category(archive,0,1,1,TaskField.B);
            var bonus=LineTest.Task(TaskKind.BonusPool);bonus.Until(LinePhase.RoundPlay);Category(bonus,0,0,0);Category(bonus,0,1,1);
            var shift=LineTest.Task(TaskKind.ShiftCover);shift.Until(LinePhase.RoundPlay);Category(shift,0,9,0);Category(shift,0,LineTest.Fact(shift.View().Task,"available").N1,1);
            var vendor=LineTest.Task(TaskKind.VendorPick);vendor.Until(LinePhase.RoundPlay);var test=LineTest.Fact(vendor.View().Task,"tested");Category(vendor,0,test.N1,test.N2);Category(vendor,0,1,1,TaskField.B);
        }
        [Test]
        public void ServerAndPhishingCategoriesKeepExactCodeAndAccountPrivate()
        {
            var server=LineTest.Task(TaskKind.ServerCode);server.Until(LinePhase.RoundPlay);int code=0;foreach(var f in server.Views().Select(v=>LineTest.Fact(v.Task,"my_digit")).OrderBy(f=>f.N1))code=code*10+f.N2;Category(server,0,code,1);Category(server,0,1,1,TaskField.B);
            var phishing=LineTest.Task(TaskKind.PhishingCheck);phishing.Until(LinePhase.RoundPlay);int insider=phishing.Views().Single(v=>v.Task.Role==1).Me;Category(phishing,insider,0,1,TaskField.Target);Category(phishing,insider,insider,2,TaskField.Target);Category(phishing,insider,1,1,TaskField.B);
        }
        [Test]
        public void SupplyEventsEncodeCompletionRatherThanItemTypeAndChangesFlagEarlierSubmissions()
        {
            var swap=LineTest.Task(TaskKind.SupplySwap);swap.Until(LinePhase.RoundPlay);Category(swap,0,1,1);swap.Perfect();var e=swap.Engine.DrainEvents().Where(x=>x.Type==LineEventType.HostChoice).ToList();Assert.IsTrue(e.Any(x=>x.Target==(int)TaskField.Target&&x.A==1));Assert.IsTrue(e.All(x=>x.A==0||x.A==1));
            var bonus=LineTest.Task(TaskKind.BonusPool);bonus.Until(LinePhase.RoundPlay);bonus.Submit(0,0);bonus.Engine.DrainEvents();bonus.Submit(0,1);Assert.AreEqual(1,bonus.Engine.DrainEvents().Single(x=>x.Type==LineEventType.HostChoice).C);
        }
    }
    [TestFixture]
    public sealed class LineValidationTests
    {
        public static IEnumerable<TestCaseData> Presets()
        {foreach(string manager in LineManagerIds.All)for(int n=3;n<=6;n++)foreach(var length in new[]{WeekLength.Short,WeekLength.Full})yield return new TestCaseData(n,length,manager);}
        [TestCaseSource(nameof(Presets))]
        public void EveryPresetHasEnoughDistinctTasksAndRunsWithoutRepeats(int n,WeekLength length,string manager)
        {
            var s=LineTest.MakeSetup(n);s.Length=length;s.ManagerProfileId=manager;var m=new LineTest(s);Assert.IsTrue(LineSchedule.Validate(m.Config,n,length,manager,out string reason));Assert.AreEqual("",reason);
            var kinds=new List<TaskKind>();while(!m.Engine.IsFinished){if(m.Engine.Phase==LinePhase.RoundIntro)kinds.Add(m.View().Task.Kind);m.Next();}Assert.AreEqual(length==WeekLength.Short?6:10,kinds.Count);Assert.AreEqual(kinds.Count,kinds.Distinct().Count());
        }
        [TestCase(true,true),TestCase(false,false)]
        public void FourPlayerShortPredictionFallsInDocumentedWindows(bool firstSession,bool firstTasks)
        {
            var p=LineSchedule.PredictMeetingStarts(LineConfig.CreateDefault(),4,WeekLength.Short,firstSession,firstTasks);Assert.AreEqual(3,p.Count);if(firstSession)Assert.That(p[0],Is.InRange(240,330));else Assert.AreEqual(220,p[0]);Assert.That(p[1],Is.InRange(540,650));Assert.That(p[2],Is.InRange(840,990));
        }
        [Test]
        public void InsufficientPoolAndDuplicateForcedTasksRejectStart()
        {
            var c=LineConfig.CreateDefault();c.Tasks.RemoveRange(5,7);Assert.IsFalse(LineSchedule.Validate(c,4,WeekLength.Short,"disiplinci",out string reason));Assert.AreEqual("lobby.pool_too_small",reason);Assert.Throws<ArgumentException>(()=>LineEngineFactory.Create(LineTest.MakeSetup(),c));
            var s=LineTest.MakeSetup();s.ForceTasks=new List<TaskKind>{TaskKind.BonusPool,TaskKind.BonusPool};Assert.Throws<ArgumentException>(()=>LineEngineFactory.Create(s));
        }
        [TestCase(-1),TestCase(2),TestCase(7)]
        public void InvalidSeatCountRejected(int n)
        {Assert.Throws<ArgumentException>(()=>LineEngineFactory.Create(LineTest.MakeSetup(n)));}
        [Test]
        public void InvalidConfigTimesHistoryAndSetupAreRejected()
        {
            var s=LineTest.MakeSetup();s.Players[1].Id=0;Assert.Throws<ArgumentException>(()=>LineEngineFactory.Create(s));
            s=LineTest.MakeSetup();s.StartTime=double.NaN;Assert.Throws<ArgumentException>(()=>LineEngineFactory.Create(s));
            s=LineTest.MakeSetup();s.History=new LineSessionHistory{RoleUses=null};Assert.Throws<ArgumentException>(()=>LineEngineFactory.Create(s));
            var c=LineConfig.CreateDefault();c.Durations.Break=float.NaN;Assert.Throws<ArgumentException>(()=>LineEngineFactory.Create(LineTest.MakeSetup(),c));c=LineConfig.CreateDefault();c.BlockIncomeFull=7;Assert.Throws<ArgumentException>(()=>LineEngineFactory.Create(LineTest.MakeSetup(),c));
            var m=new LineTest();Assert.Throws<ArgumentOutOfRangeException>(()=>m.Engine.Tick(-1));Assert.Throws<ArgumentOutOfRangeException>(()=>m.Engine.Tick(double.PositiveInfinity));
        }
        [Test]
        public void ForcedObjectivesAreDistinctAndRolesRotateByHistory()
        {
            var s=LineTest.MakeSetup();s.ForceObjectives=new List<ObjectiveKind>{ObjectiveKind.Saver,ObjectiveKind.Saver};Assert.Throws<ArgumentException>(()=>LineEngineFactory.Create(s));
            s=LineTest.MakeSetup();s.ForceTasks=new List<TaskKind>{TaskKind.ArchiveVisitor};s.History=new LineSessionHistory{WeeksPlayed=1,RoleUses=new List<int>{20,20,0,20}};var m=new LineTest(s);m.Until(LinePhase.RoundPlay);Assert.AreEqual(1,m.View(2).Task.Role);
        }
    }
}
