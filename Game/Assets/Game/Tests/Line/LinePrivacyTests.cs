using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using BirimizKovulacak.Core.Tests.Line;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class LinePrivacyTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private static object Field(object o,string name){return o.GetType().GetField(name,Private).GetValue(o);}
        private static void Set(object o,string name,object value){o.GetType().GetField(name,Private).SetValue(o,value);}
        private static bool Contains(byte[] haystack,byte[] needle)
        {for(int i=0;i<=haystack.Length-needle.Length;i++){int j=0;while(j<needle.Length&&haystack[i+j]==needle[j])j++;if(j==needle.Length)return true;}return false;}
        [Test]
        public void EveryPhaseAllowsOnlyOwnPrivateNumbersAndMessagesEvenAtResults()
        {
            var m=new LineTest(null,c=>c.CompanyTargetPercent=0);var all=(Array)Field(m.Engine,"seats");var secretSeat=all.GetValue(1);Set(secretSeat,"Money",190000071);Set(secretSeat,"Objective",ObjectiveKind.Saver);Set(secretSeat,"ObjectiveTarget",190000073);
            m.Ok(1,LineVerb.SendText,3,text:"PRIVATE_SENTINEL_İ_747347");
            int phases=0;
            while(!m.Engine.IsFinished)
            {
                foreach(var v in m.Views())
                {
                    byte[] bytes=LineCodec.EncodeView(v);Assert.IsFalse(Contains(bytes,Encoding.UTF8.GetBytes("PRIVATE_SENTINEL_İ_747347")));
                    if(v.Me!=1){Assert.IsFalse(Contains(bytes,BitConverter.GetBytes(190000071)),"money @"+v.Phase);Assert.IsFalse(Contains(bytes,BitConverter.GetBytes(190000073)),"objective @"+v.Phase);}
                    var messages=LineCodec.EncodeMessages(m.Engine.MessagesFor(v.Me,0));if(v.Me!=1&&v.Me!=3)Assert.IsFalse(Contains(messages,Encoding.UTF8.GetBytes("PRIVATE_SENTINEL_İ_747347")));
                    if(v.Me!=1)Assert.IsTrue(v.Mine.Records.All(r=>r.Kind!=RecordKind.HrFile));
                    if(v.Meeting!=null)Assert.IsTrue(v.Meeting.Recordings.All(p=>p.CandidateCallIds.Count==0));
                }
                var events=m.Engine.DrainEvents();
                Assert.IsFalse(Contains(LineCodec.EncodeEvents(events),Encoding.UTF8.GetBytes("PRIVATE_SENTINEL_İ_747347")));
                foreach(var player in m.Setup.Players)
                {
                    var client=LineCodec.DecodeEvents(LineCodec.EncodeEvents(events.Where(e=>e.IsVisibleTo(player.Id)).ToList()));
                    Assert.IsFalse(client.Any(e=>e.Type==LineEventType.HostOutcome||e.Type==LineEventType.HostTaskOutcome));
                }
                phases++;m.Next();
            }
            Assert.Greater(phases,30);
        }
        [TestCase(TaskKind.InvoiceReconcile),TestCase(TaskKind.CustomerSpec),TestCase(TaskKind.ReportReview),TestCase(TaskKind.ArchiveVisitor),TestCase(TaskKind.ServerCode),TestCase(TaskKind.PhishingCheck),TestCase(TaskKind.VendorPick)]
        public void PrivateTaskFactsAndSubmittedValuesNeverAppearInOtherScreens(TaskKind kind)
        {
            var m=LineTest.Task(kind);m.Until(LinePhase.RoundPlay);object state=Field(m.Engine,"task");var facts=(List<LineFact>[])Field(state,"Facts");facts[1].Add(new LineFact{Key="privacy_sentinel",N1=190000077});
            var a=(int[])Field(state,"A");int old=a[1];a[1]=190000079;
            foreach(var v in m.Views()){byte[] bytes=LineCodec.EncodeView(v);if(v.Me==1){Assert.IsTrue(Contains(bytes,BitConverter.GetBytes(190000077)));}else{Assert.IsFalse(Contains(bytes,BitConverter.GetBytes(190000077)));Assert.IsFalse(Contains(bytes,BitConverter.GetBytes(190000079)));}}
            a[1]=old;
        }
        [Test]
        public void UnexposedIncidentsHaveNoPublicAttributionAndComplaintsHaveNoAuthor()
        {
            var s=LineTest.MakeSetup();s.History=new LineSessionHistory{WeeksPlayed=1};s.ForceTricks=new List<TrickType>{TrickType.SahteEposta};var m=new LineTest(s);m.Ok(0,LineVerb.UseTrick,1,text:"mail");m.Finish();Assert.AreEqual(1,m.Engine.Result.IncidentsTotal);Assert.AreEqual(0,m.Engine.Result.Exposed.Count);
            foreach(var v in m.Views()){Assert.AreEqual(0,v.Result.Exposed.Count);foreach(var p in v.Result.Players.Where(p=>p.Player!=v.Me)){Assert.AreEqual(-1,p.Money);Assert.AreEqual(ObjectiveKind.None,p.Objective);Assert.AreEqual(-1,p.ObjectiveTarget);}}
            var events=m.Engine.DrainEvents();Assert.IsTrue(events.Where(e=>(int)e.Type>=40).All(e=>e.Audience==LineAudience.Host&&!e.IsVisibleTo(0)));
        }
        [Test]
        public void AllPublicSnapshotsAreDetachedFromEngineState()
        {
            var m=new LineTest();var v=m.View();v.Mine.Money=999;v.Players[0].Blame=999;v.TodayTasks.Clear();m.Engine.Setup.Players.Clear();m.Engine.Config.Durations.Morning=999;
            Assert.AreEqual(4,m.View().Mine.Money);Assert.AreEqual(5,m.Player(0).Blame);Assert.AreEqual(2,m.View().TodayTasks.Count);Assert.AreEqual(4,m.Engine.Setup.Players.Count);Assert.AreEqual(12,m.Engine.Config.Durations.Morning);
        }
        [Test]
        public void IdleTickAllocatesZeroAndStateVersionDoesNotChange()
        {
            var m=new LineTest();for(int i=0;i<100;i++)m.Engine.Tick(0);long version=m.Engine.StateVersion;long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<100000;i++)m.Engine.Tick(0);long bytes=GC.GetAllocatedBytesForCurrentThread()-before;Assert.AreEqual(0,bytes);Assert.AreEqual(version,m.Engine.StateVersion);
        }
    }
}
