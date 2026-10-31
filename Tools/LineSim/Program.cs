using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BirimizKovulacak.Core;

internal static class Program
{
    private sealed class Stats
    {
        internal int Matches,Closed,Awards,Incidents,Exposed,Warnings,Ballots,NoAction,Fired,NoMajority,FinalBallots,Actions,Busy,CarryEvents,MaxBytes;
        internal double CarrySeconds,SimulatedSeconds;
        internal readonly List<int> Productivity=new List<int>();
        internal readonly long[] TaskOutput=new long[13],TaskCount=new long[13],Career=new long[6],Objectives=new long[10],Completed=new long[10];
        internal ulong Hash=14695981039346656037UL;
    }
    private static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
    private static double Pct(long n,long d){return d==0?0:100.0*n/d;}
    private static string F(double n){return n.ToString("0.00",Inv);}
    private static int Main(string[] args)
    {
        try
        {
            int matches=20,seed=1927,players=0,session=10;double step=2;string lengths="both",mix="all",output="Tools/LineSim/v4-sim.txt";
            for(int i=0;i<args.Length;i++)
            {
                if(args[i]=="--help"){Console.WriteLine("LineSim --matches N --players all|3|4|5|6 --length both|short|full --mix all|talk|nocomm|mixed --seed N --step seconds --session-matches N --output path");return 0;}
                string flag=args[i];if(++i>=args.Length)throw new ArgumentException("Missing value for "+flag);
                switch(flag)
                {
                    case "--matches":matches=int.Parse(args[i],Inv);break;case "--seed":seed=int.Parse(args[i],Inv);break;
                    case "--players":players=args[i]=="all"?0:int.Parse(args[i],Inv);break;case "--length":lengths=args[i];break;
                    case "--mix":mix=args[i];break;case "--step":step=double.Parse(args[i],Inv);break;case "--session-matches":session=int.Parse(args[i],Inv);break;case "--output":output=args[i];break;
                    default:throw new ArgumentException("Unknown flag "+flag);
                }
            }
            if(matches<1||players!=0&&(players<3||players>6)||session<1||double.IsNaN(step)||double.IsInfinity(step)||step<=0||step>5||
                !new[]{"both","short","full"}.Contains(lengths)||!new[]{"all","talk","nocomm","mixed"}.Contains(mix))throw new ArgumentException("Invalid arguments.");
            var report=new StringBuilder();var watch=Stopwatch.StartNew();
            report.AppendLine("v4.0-hybrid — SYNTHETIC BOT DATA");
            report.AppendLine("Bots do not talk. Talk models perfect information sharing between communication partners (calls or placeholder texts), after contact in the current task. Greedy uses only its own facts. NoComm never calls or texts.");
            report.AppendLine("Default, unscaled timings. Each cell has "+matches+" matches; seeds "+seed+".."+(seed+matches-1)+"; host step "+F(step)+" s; history resets every "+session+" matches.");
            report.AppendLine("Command: dotnet run -c Release --project Tools/LineSim/LineSim.csproj -- "+string.Join(" ",args));
            int total=0;var matrix=new List<Stats>();
            for(int n=3;n<=6;n++)if(players==0||players==n)foreach(var length in new[]{WeekLength.Short,WeekLength.Full})
            {
                if(lengths!="both"&&length.ToString().ToLowerInvariant()!=lengths)continue;
                foreach(string policyMix in new[]{"talk","nocomm","mixed"})
                {
                    if(mix!="all"&&mix!=policyMix)continue;var stats=new Stats();LineSessionHistory history=null;
                    for(int i=0;i<matches;i++){if(i%session==0)history=null;history=Run(n,length,policyMix,seed+i,i,step,history,stats);}
                    total+=matches;matrix.Add(stats);Write(report,n,length,policyMix,stats);Console.WriteLine(n+"p "+length+" "+policyMix+": "+matches+" matches, productivity "+F(stats.Productivity.Average()/10)+"%, closed "+F(Pct(stats.Closed,matches))+"%");
                }
            }
            var measureSetup=Setup(6,WeekLength.Short,seed,0);var measure=LineEngineFactory.Create(measureSetup);for(int i=0;i<1000;i++)measure.Tick(0);
            long before=GC.GetAllocatedBytesForCurrentThread();var idle=Stopwatch.StartNew();for(int i=0;i<1000000;i++)measure.Tick(0);idle.Stop();long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
            // Stopwatch allocates once above; measure the tick loop alone separately.
            before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000000;i++)measure.Tick(0);bytes=GC.GetAllocatedBytesForCurrentThread()-before;
            report.AppendLine("Completed "+total+" matches. Aggregate deterministic hash: "+matrix.Aggregate(0UL,(h,s)=>h^s.Hash).ToString("X16",Inv));
            report.AppendLine("1,000,000 idle ticks: "+bytes+" allocated bytes; "+F(1/idle.Elapsed.TotalSeconds)+" million ticks/s (local observation).");
            report.AppendLine("Wall seconds: "+F(watch.Elapsed.TotalSeconds));
            string directory=Path.GetDirectoryName(output);if(!string.IsNullOrEmpty(directory))Directory.CreateDirectory(directory);File.WriteAllText(output,report.ToString(),new UTF8Encoding(false));Console.WriteLine("Wrote "+output+"; "+total+" matches; "+bytes+" idle bytes.");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e.GetType().Name+": "+e.Message);return 1;}
    }
    private static LineSetup Setup(int n,WeekLength length,int seed,int match)
    {
        var setup=new LineSetup{WeekId=match+1,MatchId="sim-"+n+"-"+seed+"-"+match,Seed=seed,Length=length};
        for(int i=0;i<n;i++)setup.Players.Add(new LinePlayerInfo{Id=i,Name="Bot "+i,IsBot=true});return setup;
    }
    private static LineSessionHistory Run(int n,WeekLength length,string mix,int seed,int match,double step,LineSessionHistory history,Stats stats)
    {
        var setup=Setup(n,length,seed,match);setup.History=history;var config=LineConfig.CreateDefault();var engine=LineEngineFactory.Create(setup,config);
        var bots=new LineBot[n];for(int i=0;i<n;i++)bots[i]=new LineBot(i,seed+97*i,mix=="nocomm"?LineBotPolicy.NoComm:mix=="mixed"&&i==n-1?LineBotPolicy.Greedy:LineBotPolicy.Talk);
        var talked=new bool[n,n];int task=-1;var kinds=new TaskKind[length==WeekLength.Short?6:10];double now=0;
        for(int tick=0;tick<20000&&!engine.IsFinished;tick++,now+=step)
        {
            engine.Tick(now);var views=new LineView[n];for(int i=0;i<n;i++)views[i]=engine.BuildView(i,now);
            int current=views[0].TaskNumber;if(current!=task){Array.Clear(talked,0,talked.Length);task=current;}
            if(task>=0)kinds[task]=views[0].Task.Kind;
            if(n==6&&views[0].Day==1&&views[0].Phase==LinePhase.RoundPlay)stats.MaxBytes=Math.Max(stats.MaxBytes,LineCodec.EncodeView(views[0]).Length);
            for(int i=0;i<n;i++)
            {
                var shared=new List<LineView>();for(int peer=0;peer<n;peer++)if(talked[i,peer])shared.Add(engine.BuildView(peer,now));
                var view=engine.BuildView(i,now);var action=bots[i].Next(view,config,now,shared);if(action==null)continue;var result=engine.Apply(i,action,now);
                if(!result.Accepted){if(result.Failure!=LineFailure.Busy)throw new InvalidOperationException("Bot "+mix+" "+n+"p "+length+" seed="+seed+" "+view.Phase+"/"+view.Task?.Kind+" "+action+" "+result);stats.Busy++;}
                else {stats.Actions++;if(action.Verb==LineVerb.SendText&&task>=0)talked[i,action.Target]=talked[action.Target,i]=true;}
            }
            if(engine.Phase==LinePhase.MeetingDossier)foreach(var pick in engine.RecordingPicks)if(!pick.Played&&pick.PlayAt<=now)engine.ReportRecording(pick.Index,pick.CandidateCallIds.Count==0?-1:pick.CandidateCallIds[0],now);
            foreach(var e in engine.DrainEvents())
            {
                if(e.Type==LineEventType.CallConnected&&task>=0)talked[e.Actor,e.Target]=talked[e.Target,e.Actor]=true;
                if(e.Type==LineEventType.TaskResolved){int kind=(int)kinds[e.A];stats.TaskOutput[kind]+=e.B;stats.TaskCount[kind]++;}
                if(e.Type==LineEventType.HostEarlySubmit){stats.CarryEvents++;stats.CarrySeconds+=e.B/1000.0;}
                if(e.Type==LineEventType.HostBallot)
                {
                    var outcome=(VoteOutcome)e.B;
                    if(outcome==VoteOutcome.Fired||outcome==VoteOutcome.NoMajority||outcome==VoteOutcome.NoVotes&&engine.Day==config.DaysFor(length)-1){stats.FinalBallots++;if(outcome==VoteOutcome.Fired)stats.Fired++;else stats.NoMajority++;}
                    else {stats.Ballots++;if(outcome==VoteOutcome.WarningGiven)stats.Warnings++;else stats.NoAction++;}
                }
            }
        }
        if(!engine.IsFinished||engine.Result==null)throw new InvalidOperationException("Simulation did not finish.");var r=engine.Result;stats.Matches++;if(r.CompanyClosed)stats.Closed++;stats.Productivity.Add(r.FinalProductivityPermille);stats.Incidents+=r.IncidentsTotal;stats.Exposed+=r.Exposed.Count;
        foreach(var p in r.Players){if(p.EmployeeOfMonth)stats.Awards++;stats.Objectives[(int)p.Objective]++;if(p.ObjectiveDone)stats.Completed[(int)p.Objective]++;foreach(var c in p.Career)stats.Career[(int)c.Item]+=c.Points;}
        stats.Hash=unchecked((stats.Hash^r.ResultHash)*1099511628211UL);stats.SimulatedSeconds+=now;return engine.NextHistory();
    }
    private static void Write(StringBuilder b,int n,WeekLength length,string mix,Stats s)
    {
        var p=s.Productivity.OrderBy(x=>x).ToArray();int At(double q){return p[(int)((p.Length-1)*q)];}
        b.AppendLine();b.AppendLine(n+" players | "+length+" | "+(mix=="mixed"?"Talk + one Greedy":mix=="talk"?"all Talk":"all NoComm"));
        b.AppendLine("Productivity % mean/min/p10/median/p90/max: "+F(p.Average()/10)+" / "+F(p[0]/10.0)+" / "+F(At(.1)/10.0)+" / "+F(At(.5)/10.0)+" / "+F(At(.9)/10.0)+" / "+F(p[p.Length-1]/10.0));
        b.AppendLine("Closure: "+F(Pct(s.Closed,s.Matches))+"%; EotM award: "+F(Pct(s.Awards,s.Matches))+"%; incidents created/exposed: "+s.Incidents+"/"+s.Exposed+" ("+F(Pct(s.Exposed,s.Incidents))+"% exposed)");
        b.AppendLine("Daily warnings/no-action: "+s.Warnings+"/"+s.NoAction+" of "+s.Ballots+" ("+F(Pct(s.Warnings,s.Ballots))+"% / "+F(Pct(s.NoAction,s.Ballots))+"%); firing/no-majority: "+s.Fired+"/"+s.NoMajority+" of "+s.FinalBallots+" ("+F(Pct(s.Fired,s.FinalBallots))+"% / "+F(Pct(s.NoMajority,s.FinalBallots))+"%) surviving-company final ballots (NoVotes included with no-majority)");
        b.AppendLine("Early-submit triggers: "+s.CarryEvents+"; carried seconds: "+F(s.CarrySeconds)+" total, "+F(s.CarrySeconds/s.Matches)+" per match; accepted actions/Busy: "+s.Actions+"/"+s.Busy);
        b.AppendLine("Career component mean points/player: "+string.Join(", ",Enumerable.Range(1,5).Select(i=>((CareerItem)i)+"="+F((double)s.Career[i]/(s.Matches*n)))));
        b.AppendLine("Output/task (mean points, instances): "+string.Join(", ",Enumerable.Range(1,12).Where(i=>s.TaskCount[i]>0).Select(i=>LineTasks.Id((TaskKind)i)+"="+F((double)s.TaskOutput[i]/s.TaskCount[i])+" ("+s.TaskCount[i]+")")));
        b.AppendLine("Objective completion by kind: "+string.Join(", ",Enumerable.Range(1,9).Select(i=>((ObjectiveKind)i)+"="+s.Completed[i]+"/"+s.Objectives[i]+" ("+F(Pct(s.Completed[i],s.Objectives[i]))+"%)")));
        b.AppendLine("Six-player block-2 play view maximum: "+s.MaxBytes+" bytes; result aggregate hash: "+s.Hash.ToString("X16",Inv));
    }
}
