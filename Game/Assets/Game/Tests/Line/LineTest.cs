using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests.Line
{
    internal sealed class LineTest
    {
        internal readonly ILineEngine Engine;internal readonly LineSetup Setup;internal readonly LineConfig Config;internal double Now;
        private readonly uint[] requests=new uint[6];
        internal static LineSetup MakeSetup(int count=4,int seed=731)
        {
            var s=new LineSetup{WeekId=42,Seed=seed,MatchId="fixture-match",ManagerProfileId=LineManagerIds.Disiplinci};
            for(int i=0;i<count;i++)s.Players.Add(new LinePlayerInfo{Id=i,Name="Seat "+i});return s;
        }
        internal LineTest(LineSetup setup=null,Action<LineConfig> configure=null)
        {Setup=setup??MakeSetup();Config=LineConfig.CreateDefault();configure?.Invoke(Config);Now=Setup.StartTime;Engine=LineEngineFactory.Create(Setup,Config);}
        internal static LineTest Task(TaskKind kind,int count=4,Action<LineConfig> configure=null,int seed=731)
        {var s=MakeSetup(count,seed);s.ForceTasks=new List<TaskKind>{kind};s.ForceRoles=new List<int>{0};return new LineTest(s,configure);}
        internal LineView View(int player=0){return Engine.BuildView(player,Now);}
        internal List<LineView> Views(){return Setup.Players.Select(p=>View(p.Id)).ToList();}
        internal LinePlayerView Player(int id){return View().Players.Single(p=>p.Id==id);}
        internal LineActionResult Do(int player,LineVerb verb,int target=-1,int a=-1,int b=-1,int c=-1,string text="")
        {return Engine.Apply(player,new LineAction(Setup.WeekId,++requests[player],verb,target,a,b,c,text),Now);}
        internal void Ok(int player,LineVerb verb,int target=-1,int a=-1,int b=-1,int c=-1,string text="")
        {var r=Do(player,verb,target,a,b,c,text);Assert.IsTrue(r.Accepted,verb+": "+r);}
        internal void Submit(int id,int a,int b=-1,int target=-1,TaskField fields=TaskField.A)
        {Ok(id,LineVerb.SubmitTask,target,a,b,(int)fields);}
        internal void Advance(double seconds){Now+=seconds;Engine.Tick(Now);}
        internal void Next(){Now=Engine.PhaseEndTime;Engine.Tick(Now);}
        internal void Until(LinePhase phase,int day=-1,int round=-1)
        {
            for(int i=0;i<200;i++){var v=View();if(v.Phase==phase&&(day<0||v.Day==day)&&(round<0||v.RoundIndex==round))return;Assert.IsFalse(Engine.IsFinished,"Finished before "+phase);Next();}Assert.Fail("Never reached "+phase);
        }
        internal LineTaskReveal Reveal(){Until(LinePhase.RoundReveal);return View().Task.Reveal;}
        internal void Finish(){while(!Engine.IsFinished)Next();}
        internal static LineFact Fact(LineTaskView t,string key){return t.Facts.Concat(t.PublicFacts).First(f=>f.Key==key);}
        internal void Perfect()
        {
            Until(LinePhase.RoundPlay);var views=Views();var kind=views[0].Task.Kind;
            if(kind==TaskKind.ReportReview)
            {
                foreach(var v in views)Submit(v.Me,0);
                foreach(var v in views)Submit(v.Me,-1,0,fields:TaskField.B);return;
            }
            if(kind==TaskKind.SupplySwap)
            {
                for(int guard=0;guard<50;guard++)
                {
                    views=Views();bool gave=false;
                    foreach(var v in views)
                    {
                        var needs=v.Task.Facts.Where(f=>f.Key=="need").Select(f=>f.N1).ToList();
                        for(int j=0;j<v.Task.Items.Count;j++)
                        {
                            int item=v.Task.Items[j];if(needs.Contains(item))continue;
                            var receiver=views.FirstOrDefault(x=>x.Me!=v.Me&&!x.Task.Items.Contains(item)&&x.Task.Facts.Any(f=>f.Key=="need"&&f.N1==item));
                            if(receiver!=null){Ok(v.Me,LineVerb.GiveItem,receiver.Me,j);gave=true;break;}
                        }
                        if(gave)break;
                    }
                    if(!gave)return;
                }
                Assert.Fail("Trading failed to converge.");
            }
            Dictionary<int,int> rooms=null;if(kind==TaskKind.RoomBooking)rooms=Rooms(views);
            foreach(var v in views)
            {
                var t=v.Task;int a=0,target=-1;
                switch(kind)
                {
                    case TaskKind.InvoiceReconcile:a=views.Where(x=>x.Me==v.Me||t.Group.Contains(x.Me)).Sum(x=>x.Task.Facts.Where(f=>f.Key=="line").Sum(f=>f.N2));break;
                    case TaskKind.BudgetSplit:a=Fact(t,"need").N1;break;
                    case TaskKind.CustomerSpec:
                        var attrs=new Dictionary<string,int>();foreach(var peer in views.Where(x=>x.Me==v.Me||t.Group.Contains(x.Me)))foreach(var f in peer.Task.Facts)if(f.Key=="color"||f.Key=="size"||f.Key=="day")attrs[f.Key]=f.N1;
                        int packed=attrs["color"]*9+attrs["size"]*3+attrs["day"];a=t.PublicFacts.First(f=>f.Key=="spec"&&f.N2==packed).N1;break;
                    case TaskKind.RoomBooking:a=rooms[v.Me];break;
                    case TaskKind.ArchiveVisitor:
                        if(t.Role==1){int k=Fact(t,"capacity").N1,count=0;foreach(var peer in views.Where(x=>x.Me!=v.Me)){int title=Fact(peer.Task,"need_doc").N1;var doc=t.Facts.First(f=>f.Key=="doc"&&f.N2==title);if(count++<k)a|=1<<doc.N1;}}
                        else a=t.Controls.First(c=>c.Key=="confirm").Options.First(o=>o.N2==Fact(t,"need_doc").N1).Value;break;
                    case TaskKind.ServerCode:
                        var ds=views.Select(x=>Fact(x.Task,"my_digit")).OrderBy(f=>f.N1);foreach(var d in ds)a=a*10+d.N2;break;
                    case TaskKind.PhishingCheck:
                        var insider=views.First(x=>x.Task.Role==1);int fake=Fact(insider.Task,"insider_offer").N1;target=views.First(x=>Fact(x.Task,"account").N1==fake).Me;break;
                    case TaskKind.ShiftCover:
                        // Assign distinct shifts greedily with exhaustive fallback.
                        a=Shifts(views)[v.Me];break;
                    case TaskKind.VendorPick:a=views.Select(x=>Fact(x.Task,"tested")).OrderByDescending(f=>f.N2).First().N1;break;
                }
                Submit(v.Me,a,target:target,fields:kind==TaskKind.PhishingCheck?TaskField.Target:TaskField.A);
            }
        }
        internal static Dictionary<int,int> Rooms(List<LineView> views)
        {
            Dictionary<int,int> best=null,current=new Dictionary<int,int>();int max=-1;
            void Search(int i,int used,int score)
            {
                if(i==views.Count){if(score>max){max=score;best=new Dictionary<int,int>(current);}return;}
                var v=views[i];current[v.Me]=9;Search(i+1,used,score);
                foreach(var f in v.Task.Facts.Where(x=>x.Key=="ok_slot"))if((used&(1<<f.N1))==0){current[v.Me]=f.N1;Search(i+1,used|(1<<f.N1),score+Fact(v.Task,"value").N1);}
            }
            Search(0,0,0);return best;
        }
        internal static Dictionary<int,int> Shifts(List<LineView> views)
        {
            var result=views.ToDictionary(v=>v.Me,v=>9);int k=Fact(views[0].Task,"shifts").N1;
            bool Search(int shift)
            {if(shift==k)return true;foreach(var v in views)if(result[v.Me]==9&&v.Task.Facts.Any(f=>f.Key=="available"&&f.N1==shift)){result[v.Me]=shift;if(Search(shift+1))return true;result[v.Me]=9;}return false;}
            Assert.IsTrue(Search(0));return result;
        }
    }
}
