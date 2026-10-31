using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    public static class LineSchedule
    {
        internal static bool Eligible(LineTaskConfig t,int players,string manager)
        {return t!=null&&t.Enabled&&players>=t.MinPlayers&&players<=t.MaxPlayers&&(string.IsNullOrEmpty(t.ExclusiveManager)||t.ExclusiveManager==manager);}
        public static bool Validate(LineConfig c,int players,WeekLength length,string managerId,out string reasonKey)
        {
            reasonKey="lobby.pool_too_small";
            if(c==null||c.Tasks==null||players<3||players>6||!Enum.IsDefined(typeof(WeekLength),length)||c.RoundsPerDay!=2||c.DaysFor(length)<1||c.DaysFor(length)>LineTasks.Count/2||c.MinSpareTasks<0||c.MinSpareTasks>LineTasks.Count)return false;
            int count=0;var distinct=new HashSet<TaskKind>();foreach(var t in c.Tasks)if(Eligible(t,players,managerId)&&t.Kind!=TaskKind.None&&Enum.IsDefined(typeof(TaskKind),t.Kind)&&distinct.Add(t.Kind))count++;
            if(count<c.DaysFor(length)*c.RoundsPerDay+c.MinSpareTasks)return false;reasonKey="";return true;
        }
        public static List<double> PredictMeetingStarts(LineConfig c,int players,WeekLength length,bool firstSession,bool firstTimeTasks)
        {
            if(c==null||c.Durations==null||c.RoundPlayByPlayers==null||c.RoundPlayByPlayers.Count!=4||players<3||players>6||!Enum.IsDefined(typeof(WeekLength),length))throw new ArgumentException("Invalid prediction input.");
            var starts=new List<double>();var d=c.Durations;double time=0;
            // Forecasts use reference PlayFactor=100 and round display estimates to ten seconds.
            // Runtime deadlines remain exact; actual hub/trading cards can add 10%.
            for(int day=0;day<c.DaysFor(length);day++)
            {
                time+=firstSession&&day==0?d.MorningFirst:d.Morning;
                time+=2*((firstTimeTasks?d.RoundIntroFirst:d.RoundIntro)+c.RoundPlayByPlayers[players-3]*(d.RoundPlay/75.0)+d.RoundReveal)+d.Break;
                if(day==c.DaysFor(length)-1)time+=d.BreakClosing;starts.Add(Math.Round(time/10,MidpointRounding.AwayFromZero)*10);
                time+=d.Dossier+d.DossierPerRecording+d.Discussion+d.Vote+d.Verdict;
            }
            return starts;
        }
    }
}
