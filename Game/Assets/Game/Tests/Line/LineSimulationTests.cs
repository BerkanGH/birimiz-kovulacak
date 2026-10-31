using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BirimizKovulacak.Core.Tests.Line;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class LineSimulationTests
    {
        public static IEnumerable<TestCaseData> Cases()
        {for(int n=3;n<=6;n++)foreach(var l in new[]{WeekLength.Short,WeekLength.Full})foreach(var p in new[]{LineBotPolicy.Talk,LineBotPolicy.NoComm,LineBotPolicy.Greedy})yield return new TestCaseData(n,l,p);}
        internal static ulong Run(int n,WeekLength length,LineBotPolicy policy,int seed,Action<byte[]> trace=null)
        {
            var s=LineTest.MakeSetup(n,seed);s.Length=length;var config=LineConfig.CreateDefault();config.Durations.Scale(0.05f);config.MessageMinInterval=0.1f;config.TransferMinInterval=0.1f;
            var engine=LineEngineFactory.Create(s,config);var bots=Enumerable.Range(0,n).Select(i=>new LineBot(i,seed+i*97,policy)).ToArray();var talked=new bool[n,n];int last=-1;double now=0;
            for(int step=0;step<30000&&!engine.IsFinished;step++,now+=0.25)
            {
                engine.Tick(now);var snapshot=Enumerable.Range(0,n).Select(i=>engine.BuildView(i,now)).ToArray();int task=snapshot[0].TaskNumber;if(task!=last){Array.Clear(talked,0,talked.Length);last=task;}
                for(int i=0;i<n;i++)
                {
                    var view=engine.BuildView(i,now);var shared=snapshot.Where(v=>talked[i,v.Me]).ToList();if(trace!=null){trace(LineCodec.EncodeView(view));trace(LineCodec.EncodeMessages(engine.MessagesFor(i,0)));}
                    var a=bots[i].Next(view,config,now,shared);if(a==null)continue;
                    var r=engine.Apply(i,a,now);if(trace!=null){trace(LineCodec.EncodeAction(a));trace(LineCodec.EncodeResult(r));}Assert.IsTrue(r.Accepted||r.Failure==LineFailure.Busy,"Bot "+policy+" "+n+" "+length+" phase="+view.Phase+" task="+view.Task?.Kind+" "+a+" "+r);
                    if(r.Accepted&&a.Verb==LineVerb.SendText){talked[i,a.Target]=talked[a.Target,i]=true;}
                }
                var events=engine.DrainEvents();if(trace!=null)trace(LineCodec.EncodeEvents(events));foreach(var e in events)if(e.Type==LineEventType.CallConnected)talked[e.Actor,e.Target]=talked[e.Target,e.Actor]=true;
                if(engine.Phase==LinePhase.MeetingDossier)foreach(var pick in engine.RecordingPicks)if(!pick.Played&&pick.PlayAt<=now)engine.ReportRecording(pick.Index,pick.CandidateCallIds[0],now);
            }
            Assert.IsTrue(engine.IsFinished);Assert.NotNull(engine.Result);Assert.AreEqual(length==WeekLength.Short?6:10,engine.NextHistory().TaskUses.Sum());return engine.Result.ResultHash;
        }
        [TestCaseSource(nameof(Cases))]
        public void BotsFinishAllHeadCountsLengthsAndPoliciesWithoutKnownInvalidActions(int n,WeekLength l,LineBotPolicy p)
        {Assert.AreNotEqual(0,Run(n,l,p,912));}
        [Test]
        public void BotReplaysProduceIdenticalResultHashes()
        {Assert.AreEqual(Run(6,WeekLength.Full,LineBotPolicy.Talk,47),Run(6,WeekLength.Full,LineBotPolicy.Talk,47));}
        [Test]
        public void IdenticalInputsReproduceEveryViewMessageActionResultAndEvent()
        {
            ulong Digest()
            {
                ulong hash=14695981039346656037UL;
                Run(4,WeekLength.Short,LineBotPolicy.Talk,447,bytes=>{foreach(byte b in bytes)hash=unchecked((hash^b)*1099511628211UL);});return hash;
            }
            Assert.AreEqual(Digest(),Digest());
        }
        [Test]
        public void SixPlayerMidMatchViewStaysBelowSixKilobytes()
        {
            var m=new LineTest(LineTest.MakeSetup(6));m.Until(LinePhase.RoundPlay,1,0);int bytes=LineCodec.EncodeView(m.View()).Length;TestContext.WriteLine("6-player mid-match view: "+bytes+" bytes");Assert.Less(bytes,6144);
        }
    }
}
namespace BirimizKovulacak.Core.Tests
{
    [NUnit.Framework.TestFixture]
    public sealed class LineBotPresenceTests
    {
        [NUnit.Framework.Test]
        public void RemedialBotChoosesOnlyColleaguesObservedTakingPart()
        {
            var m=BirimizKovulacak.Core.Tests.Line.LineTest.Task(TaskKind.InvoiceReconcile,4,c=>c.PurchasesPerBlock=0);m.Engine.SetConnected(1,false,0);m.Until(LinePhase.RoundIntro);var bot=new LineBot(0,77);bot.Next(m.View(),m.Config,m.Now);m.Reveal();m.Engine.SetConnected(1,true,m.Now);
            var action=bot.Next(m.View(),m.Config,m.Now);NUnit.Framework.Assert.AreEqual(LineVerb.StartRemedial,action.Verb);NUnit.Framework.Assert.AreEqual(2,action.Target);NUnit.Framework.Assert.IsTrue(m.Engine.Apply(0,action,m.Now).Accepted);NUnit.Framework.Assert.AreEqual(LineFailure.NotAvailable,m.Do(3,LineVerb.StartRemedial,1,0).Failure);
        }
        [NUnit.Framework.Test]
        public void NoCommDeclinesCallsAndDoesNotPassItemsBackForever()
        {
            var setup=BirimizKovulacak.Core.Tests.Line.LineTest.MakeSetup();setup.ForceTasks=new System.Collections.Generic.List<TaskKind>{TaskKind.SupplySwap};setup.ForceObjectives=new System.Collections.Generic.List<ObjectiveKind>{ObjectiveKind.Saver};var m=new BirimizKovulacak.Core.Tests.Line.LineTest(setup,c=>c.PurchasesPerBlock=0);m.Ok(1,LineVerb.Call,0);var bot=new LineBot(0,91,LineBotPolicy.NoComm);var decline=bot.Next(m.View(),m.Config,m.Now);NUnit.Framework.Assert.AreEqual(LineVerb.Decline,decline.Verb);NUnit.Framework.Assert.IsTrue(m.Engine.Apply(0,decline,m.Now).Accepted);m.Until(LinePhase.RoundPlay);
            for(int i=0;i<2;i++)
            {
                var gift=bot.Next(m.View(),m.Config,m.Now);NUnit.Framework.Assert.AreEqual(LineVerb.GiveItem,gift.Verb);NUnit.Framework.Assert.IsTrue(m.Engine.Apply(0,gift,m.Now).Accepted);int index=m.View(1).Task.Items.Count-1;m.Ok(1,LineVerb.GiveItem,0,index);
            }
            var done=bot.Next(m.View(),m.Config,m.Now);NUnit.Framework.Assert.AreEqual(LineVerb.SubmitTask,done.Verb);NUnit.Framework.Assert.IsTrue(m.Engine.Apply(0,done,m.Now).Accepted);NUnit.Framework.Assert.IsNull(bot.Next(m.View(),m.Config,m.Now));
        }
    }
}
