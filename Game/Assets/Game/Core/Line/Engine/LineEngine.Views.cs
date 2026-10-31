using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    internal sealed partial class LineEngine
    {
        public LineView BuildView(int player,double time)
        {
            Tick(time);if(!HasSeat(player))throw new ArgumentException("Unknown recipient.");var s=seats[player];
            var v=new LineView{WeekId=setup.WeekId,MatchId=setup.MatchId,StateVersion=StateVersion,Me=player,Now=time,Phase=Phase,PhaseStartTime=phaseStart,PhaseEndTime=PhaseEndTime,Day=Day,DayCount=days,Length=setup.Length,FirstWeek=firstWeek,
                RoundIndex=InTask?roundIndex:-1,TaskNumber=InTask?task.Number:-1,ManagerProfileId=profile.Id,Favourite=favourite};
            v.TodayTasks.Add(schedule[Day*2]);v.TodayTasks.Add(schedule[Day*2+1]);
            v.Features=new LineFeaturesView{TricksUnlocked=Unlocked,RecordingsEnabled=c.RecordingsEnabled&&setup.RecordingsEnabled,IsLastDay=Day==days-1,FirstTimeTask=InTask&&firstTimeTask,OnboardingDay=firstWeek&&Day==0,
                CarriedSeconds=Phase==LinePhase.Break?carried:0,CallsOpen=Office,TransfersOpen=TransfersOpen,EvidenceOpen=EvidenceOpen,PresentationOpen=PresentOpen,RemedialOpen=RemedialOpen,ComplaintsOpen=Office,MeetingChatOpen=IsMeeting};
            foreach(int id in ids)v.Players.Add(new LinePlayerView{Id=id,Name=seats[id].Info.Name,ColorIndex=seats[id].Info.ColorIndex,IsBot=seats[id].Info.IsBot,Connected=seats[id].Connected,Blame=seats[id].Blame,Warnings=seats[id].Warnings,
                Submitted=InTask&&Phase!=LinePhase.RoundIntro&&Complete(task,id),Voted=seats[id].Vote!= -1,SessionPoints=PriorPoints(id)});
            int resolved=0;foreach(var t in tasks)if(t.Resolved)resolved++;
            v.Company=new LineCompanyView{ProvisionalPermille=Provisional(),FinalPermille=finalProductivity,TargetPercent=c.CompanyTargetPercent,ResolvedOutput=ResolvedOutput(),ResolvedMax=ResolvedMax(),ScheduledMax=ScheduledMax(),TasksResolved=resolved,TasksScheduled=schedule.Count,
                LastChange=lastChange,LastTask=lastTask,LastDelta=lastDelta,Closed=closed};
            var mine=new LineMeView{Money=s.Money,BlockIncome=Filter(s.RawIncome),Objective=new LineObjectiveView{Kind=s.Objective,Target=s.ObjectiveTarget,Threshold=ObjectiveThreshold(s.Objective),Progress=ObjectiveProgress(player),Completed=Phase==LinePhase.WeekResults&&s.ObjectiveDone},
                Trick=s.Trick,TrickUsedToday=s.TrickUsed,TricksLeftThisWeek=Math.Max(0,TrickAllowance-s.TricksUsed),ComputerBlockedUntil=Math.Max(s.CoffeeUntil,s.OfflineUntil),ComputerBlockedBy=s.CoffeeUntil<=0&&s.OfflineUntil<=0?TrickType.None:s.CoffeeUntil>=s.OfflineUntil?TrickType.KahveDok:TrickType.InternetKes,
                PhoneDeadUntil=s.DeadUntil,WiretapTarget=s.TapTarget,WiretapUntil=s.TapUntil,Phone=s.Phone,CallLog=s.Log,Offers=Offers(player),PurchasesLeftThisBlock=Math.Max(0,c.PurchasesPerBlock-s.BlockPurchases),PurchasesLeftThisMatch=Math.Max(0,c.PurchasesPerMatch-s.Purchases),
                PresentationsLeft=Math.Max(0,c.PresentationsPerMeeting-s.Presentations),RemedialsLeftThisBlock=Math.Max(0,c.RemedialsPerBlock-s.BlockRemedials),RemedialsLeftThisMatch=Math.Max(0,c.RemedialsPerMatch-s.Remedials),RemedialOptions=RemedialOptions(player),
                IncomingRemedial=remedials.Find(r=>r.Cosigner==player),OutgoingRemedial=remedials.Find(r=>r.Requester==player),MessageSeq=s.MessageSeq,TextsLeftThisBlock=Math.Max(0,c.TextsPerBlock-s.Texts),EmailsLeftThisBlock=Math.Max(0,c.EmailsPerBlock-s.Emails),ComplaintsLeftThisBlock=Math.Max(0,c.ComplaintsPerBlock-s.Complaints),
                MeetingChatLeft=Math.Max(0,c.MeetingChatPerMeeting-s.Chats),NextMessageAt=s.NextMessage,NextTransferAt=s.NextTransfer};
            foreach(var record in records)if(s.Access.Contains(record.View.Id))mine.Records.Add(record.View);v.Mine=mine;
            if(InTask)v.Task=TaskView(player);
            foreach(var t in tasks)if(t.Resolved&&t.Number/2==Day)v.TodayReveals.Add(t.Reveal);
            if(IsMeeting)
            {
                var m=new LineMeetingView{IsFinal=meeting.IsFinal,Dossier=meeting.Dossier,Complaints=meeting.Complaints,Presentations=meeting.Presentations,MyVote=s.Vote,WarningTo=meeting.WarningTo,Outcome=meeting.Outcome,Roster=meeting.Roster,MajorityNeeded=meeting.MajorityNeeded,DefenseOrder=meeting.DefenseOrder,CompanyClosed=meeting.CompanyClosed,
                    Fired=Phase==LinePhase.FinalVerdict?meeting.Fired:-1};
                if(Phase==LinePhase.MeetingVerdict||Phase==LinePhase.FinalVerdict)m.Votes=meeting.Votes;
                if(Phase==LinePhase.FinalDefense&&defenderIndex<meeting.DefenseOrder.Count){m.Defender=meeting.DefenseOrder[defenderIndex];m.DefenderEndTime=phaseStart+(defenderIndex+1)*c.Durations.DefensePerPlayer;}
                foreach(var p in picks)m.Recordings.Add(CopyPick(p,false));v.Meeting=m;
            }
            if(Phase==LinePhase.WeekResults)
            {
                v.Result=LineCodec.Clone(result);
                // Strict privacy continues on results. Career components and objective completion are public;
                // another player's balance, objective category and target are never a client entitlement.
                foreach(var p in v.Result.Players)if(p.Player!=player){p.Money=-1;p.Objective=ObjectiveKind.None;p.ObjectiveTarget=-1;}
            }
            return LineCodec.Clone(v);
        }
        private LineTaskView TaskView(int id)
        {
            var t=task;var v=new LineTaskView{Kind=t.Kind,Version=c.Task(t.Kind).Version,Family=LineTasks.Family(t.Kind),Index=roundIndex,Number=t.Number,PlayEndTime=t.PlayEnd,Role=t.Roles[id],Group=t.Group[id],
                Facts=new List<LineFact>(t.Facts[id]),PublicFacts=new List<LineFact>(t.Public),Items=t.Items[id],Submitted=Complete(t,id),MyA=t.A[id],MyB=t.B[id],MyTarget=t.Target[id],SubmittedFields=t.Fields[id],Reveal=t.Resolved?t.Reveal:null};
            if(t.Kind==TaskKind.ReportReview)
            {int p=t.D[id,2];v.Facts.Add(Fact("review_figure",(t.Fields[p]&TaskField.A)==0?-1:t.D[p,t.A[p]==0?0:1],p:p));}
            foreach(var ctrl in t.Controls[id])
            {
                var x=new LineControlView{Kind=ctrl.Kind,Field=ctrl.Field,Key=ctrl.Key,Options=ctrl.Options,Min=ctrl.Min,Max=ctrl.Max,Digits=ctrl.Digits,MaxPicks=ctrl.MaxPicks,AllowSelf=ctrl.AllowSelf,AllowNone=ctrl.AllowNone,Required=ctrl.Required,Locks=ctrl.Locks,Sabotage=ctrl.Sabotage};
                if(t.Kind==TaskKind.ReportReview&&ctrl.Field==TaskField.B&&(t.Fields[t.D[id,2]]&TaskField.A)==0)x.WaitingFor=t.D[id,2];
                x.Enabled=Phase==LinePhase.RoundPlay&&Math.Max(seats[id].CoffeeUntil,seats[id].OfflineUntil)<=now&&x.WaitingFor<0&&(!ctrl.Locks||(t.Fields[id]&ctrl.Field)==0);v.Controls.Add(x);
            }
            return v;
        }
    }
}
