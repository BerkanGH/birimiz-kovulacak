using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    internal sealed partial class LineEngine
    {
        private readonly List<LineRecordingPick> picks=new List<LineRecordingPick>();
        private readonly List<int[]> playedCalls=new List<int[]>();
        public IReadOnlyList<LineRecordingPick> RecordingPicks
        {
            get { var list=new List<LineRecordingPick>();if(Phase==LinePhase.MeetingDossier)foreach(var p in picks)list.Add(CopyPick(p,true));return list; }
        }
        private static LineRecordingPick CopyPick(LineRecordingPick p,bool host)
        { return new LineRecordingPick{Index=p.Index,CandidateCallIds=host?new List<int>(p.CandidateCallIds):new List<int>(),PlayAt=p.PlayAt,Seconds=p.Seconds,ChosenCallId=p.ChosenCallId,Caller=p.Caller,Callee=p.Callee,CallStart=p.CallStart,Context=p.Context,Played=p.Played}; }
        private void StartDossier(double time)
        {
            EndAllCalls(time,CallEndReason.PhaseEnd);EndRemedials(time);meeting=new LineMeetingView{IsFinal=Day==days-1};
            foreach(int id in ids){seats[id].Vote=-1;seats[id].Presentations=seats[id].Chats=0;}
            if(meeting.IsFinal){FreezeCompany();meeting.CompanyClosed=closed;if(closed)Emit(LineEventType.CompanyClosed,time,a:finalProductivity);}
            meeting.Dossier.Add(new LineDossierEntry{Kind=DossierKind.Productivity,N1=Provisional(),N2=c.CompanyTargetPercent,Time=time});
            foreach(var t in tasks)if(t.Number/2==Day&&t.Resolved)
            {
                meeting.Dossier.Add(new LineDossierEntry{Kind=DossierKind.TaskOutcome,Task=t.Kind,TaskNumber=t.Number,N1=t.Output,N2=t.Max,Time=time});
                if(profile.ListMissedTasks)foreach(int id in t.Reveal.Missed)meeting.Dossier.Add(new LineDossierEntry{Kind=DossierKind.MissedTask,Player=id,Task=t.Kind,TaskNumber=t.Number,Time=time});
            }
            foreach(var inc in incidents)if(inc.View.Day==Day&&(inc.Trick==TrickType.KahveDok||inc.Trick==TrickType.FisCek||inc.Trick==TrickType.InternetKes))
                meeting.Dossier.Add(new LineDossierEntry{Kind=DossierKind.IncidentEffect,Trick=inc.Trick,Victim=inc.Victim,Time=inc.View.Time});
            foreach(var record in records)if(record.View.Day==Day&&record.View.Kind==RecordKind.Remedial)
            {var v=record.View;meeting.Dossier.Add(new LineDossierEntry{Kind=DossierKind.Remedial,Player=v.Actor,Victim=v.Subject,Task=v.Task,TaskNumber=v.TaskNumber,N1=v.N2,Time=v.Time});}
            if(favourite>=0)meeting.Dossier.Add(new LineDossierEntry{Kind=DossierKind.Favourite,Player=favourite,Time=time});
            meeting.Complaints.AddRange(complaints);if(complaints.Count>0)meeting.Dossier.Add(new LineDossierEntry{Kind=DossierKind.Complaints,N1=complaints.Count,Time=time});
            SelectRecordings(time);
            foreach(var p in picks)meeting.Dossier.Add(new LineDossierEntry{Kind=DossierKind.Recording,Arg=p.Index,Time=p.PlayAt});
            Enter(LinePhase.MeetingDossier,time,c.Durations.Dossier+c.Durations.DossierPerRecording*picks.Count);
        }
        private bool GroupRelated(Call call,TaskState t) { return t.Number/2==call.Day&&t.Group[call.Caller].Contains(call.Callee); }
        private TaskKind RecordingContext(Call call)
        {
            foreach(var t in tasks)if(t.Number/2==call.Day)foreach(var d in t.Decisions)
                if((d.Player==call.Caller||d.Player==call.Callee)&&d.Time>=call.Start&&d.Time<=call.End+c.RecordingDecisionWindow)return t.Kind;
            foreach(var t in tasks)if(GroupRelated(call,t))return t.Kind;return TaskKind.None;
        }
        private double RecordingWeight(Call call)
        {
            double weight=call.End-call.Start;bool group=false,decision=false,transfer=false;
            foreach(var t in tasks)if(t.Number/2==call.Day)
            {
                if(GroupRelated(call,t))group=true;
                foreach(var d in t.Decisions)if((d.Player==call.Caller||d.Player==call.Callee)&&d.Time>=call.Start&&d.Time<=call.End+c.RecordingDecisionWindow)decision=true;
            }
            if(c.RecordingSelection==RecordingSelection.Legacy)
            {
                bool focus=false;
                foreach(var t in tasks)if(t.Number/2==call.Day)
                {
                    if((LineTasks.Family(t.Kind)==TaskFamily.Pair||t.Kind==TaskKind.ReportReview)&&GroupRelated(call,t))focus=true;
                    if(t.Role==call.Caller||t.Role==call.Callee||t.Kind==TaskKind.PhishingCheck&&(t.OtherRole==call.Caller||t.OtherRole==call.Callee))focus=true;
                }
                return weight*(focus?c.RecordingFocusWeight:1);
            }
            foreach(var tr in transfers)if((tr.A==call.Caller&&tr.B==call.Callee||tr.A==call.Callee&&tr.B==call.Caller)&&tr.Time>=call.Start-c.RecordingTransferWindow&&tr.Time<=call.End+c.RecordingTransferWindow)transfer=true;
            int exposure=0;foreach(var pair in playedCalls)if(pair[0]==call.Caller||pair[1]==call.Caller||pair[0]==call.Callee||pair[1]==call.Callee)exposure++;
            return weight*(1+(group?c.RecordingGroupBonus:0))*(1+(decision?c.RecordingDecisionBonus:0))*(1+(transfer?c.RecordingTransferBonus:0))*Math.Pow(c.RecordingExposureDecay,exposure);
        }
        private void SelectRecordings(double time)
        {
            picks.Clear();if(!setup.RecordingsEnabled||!c.RecordingsEnabled)return;
            var pool=calls.FindAll(x=>x.Connected&&x.End>=x.Start+c.RecordingMinCallSeconds);
            for(int i=0;i<profile.Recordings&&pool.Count>0&&c.RecordingCandidates>0;i++)
            {
                var pick=new LineRecordingPick{Index=i,PlayAt=time+c.Durations.Dossier+i*c.Durations.DossierPerRecording,Seconds=c.RecordingSnippetSeconds};
                for(int k=0;k<c.RecordingCandidates&&pool.Count>0;k++)
                {
                    double total=0;foreach(var call in pool)total+=RecordingWeight(call);int selected=0;
                    if(total>0){double draw=rng.Unit()*total;for(int j=0;j<pool.Count;j++){draw-=RecordingWeight(pool[j]);if(draw<0){selected=j;break;}}}
                    else selected=rng.Index(pool.Count);
                    pick.CandidateCallIds.Add(pool[selected].Id);pool.RemoveAt(selected);
                }
                picks.Add(pick);Emit(LineEventType.HostRecordingPick,time,a:i,b:pick.CandidateCallIds.Count,extra:(int)c.RecordingSelection,host:true);
            }
        }
        public void ReportRecording(int pickIndex,int chosenCallId,double time)
        {
            Tick(time);if(Phase!=LinePhase.MeetingDossier||pickIndex<0||pickIndex>=picks.Count)return;var p=picks[pickIndex];
            if(p.Played||time<p.PlayAt||chosenCallId!= -1&&!p.CandidateCallIds.Contains(chosenCallId))return;
            if(chosenCallId>=0)
            {
                var call=FindCall(chosenCallId);if(call==null)return;p.ChosenCallId=call.Id;p.Caller=call.Caller;p.Callee=call.Callee;p.CallStart=call.Start;p.Context=RecordingContext(call);
                playedCalls.Add(new[]{call.Caller,call.Callee});
            }
            p.Played=true;Emit(LineEventType.RecordingStarted,time,a:pickIndex);Changed();
        }
        private void DailyVerdict(double time)
        {
            int top= -1,max=0,ties=0,noAction=0,total=0;
            foreach(int id in ids)
            {int count=0;foreach(int voter in ids)if(seats[voter].Vote==id)count++;if(count>max){max=count;top=id;ties=1;}else if(count==max&&count>0)ties++;}
            foreach(int id in ids){if(seats[id].Vote==LineIds.Nobody)noAction++;if(seats[id].Vote!= -1)total++;meeting.Votes.Add(new LineVoteView{Voter=id,Target=seats[id].Vote});}
            VoteOutcome outcome=total==0?VoteOutcome.NoVotes:noAction>max?VoteOutcome.NoActionWon:noAction==max||ties>1?VoteOutcome.Tie:max<c.WarningMinVotes?VoteOutcome.BelowMinimum:seats[top].Warnings>=c.MaxWarningsPerPlayer?VoteOutcome.Capped:VoteOutcome.WarningGiven;
            meeting.Outcome=outcome;
            if(outcome==VoteOutcome.WarningGiven)
            {
                meeting.WarningTo=top;seats[top].Warnings++;Blame(top,profile.WarningBlame,BlameReason.Warning,time);
                AddRecord(RecordKind.Warning,RecordSource.HR,time,subject:top,publicRecord:true);Emit(LineEventType.WarningGiven,time,target:top);
            }
            Emit(LineEventType.HostBallot,time,target:top,a:max,b:(int)outcome,extra:noAction,host:true);
        }
        private void StartDefense(double time)
        {
            var order=ids.FindAll(id=>seats[id].Connected);rng.Shuffle(order);
            // Stable insertion preserves the seeded order within equal blame/warnings.
            for(int i=1;i<order.Count;i++){int id=order[i],j=i-1;while(j>=0&&(seats[order[j]].Blame<seats[id].Blame||seats[order[j]].Blame==seats[id].Blame&&seats[order[j]].Warnings<seats[id].Warnings)){order[j+1]=order[j];j--;}order[j+1]=id;}
            meeting.DefenseOrder=order;defenderIndex=0;Enter(LinePhase.FinalDefense,time,c.Durations.DefensePerPlayer*order.Count);
        }
        public bool MaySpeak(int player)
        { return HasSeat(player)&&seats[player].Connected&&(Phase!=LinePhase.FinalDefense||defenderIndex<meeting.DefenseOrder.Count&&meeting.DefenseOrder[defenderIndex]==player); }
        private void StartFinalVote(double time)
        {
            foreach(int id in ids){seats[id].Vote=-1;if(seats[id].Connected)meeting.Roster.Add(id);}
            meeting.MajorityNeeded=meeting.Roster.Count/2+1;Enter(LinePhase.FinalVote,time,c.Durations.FinalVote);
        }
        private void FinalVerdict(double time)
        {
            int top=-1,max=0,nobody=0,total=0;
            if(closed){meeting.Outcome=VoteOutcome.CompanyClosed;meeting.Fired=-1;}
            else
            {
                foreach(int id in ids){int count=0;foreach(int voter in meeting.Roster)if(seats[voter].Vote==id)count++;if(count>max){top=id;max=count;}}
                foreach(int id in meeting.Roster){if(seats[id].Vote==LineIds.Nobody)nobody++;if(seats[id].Vote!= -1)total++;meeting.Votes.Add(new LineVoteView{Voter=id,Target=seats[id].Vote});}
                meeting.Fired=max>=meeting.MajorityNeeded?top:LineIds.Nobody;
                meeting.Outcome=meeting.Fired>=0?VoteOutcome.Fired:total==0?VoteOutcome.NoVotes:VoteOutcome.NoMajority;
                Emit(LineEventType.HostBallot,time,target:top,a:max,b:(int)meeting.Outcome,extra:nobody,host:true);
                Emit(LineEventType.Fired,time,target:meeting.Fired);
            }
            foreach(int id in ids){seats[id].ObjectiveDone=ObjectiveDone(id);Emit(LineEventType.HostObjective,time,id,seats[id].ObjectiveTarget,(int)seats[id].Objective,seats[id].ObjectiveDone?1:0,host:true);}
        }
        private int ExposedCount(int id) { int n=0;foreach(var inc in incidents)if(inc.Exposed&&inc.View.Actor==id)n++;return n; }
        private int ObjectiveProgress(int id)
        {
            var s=seats[id];switch(s.Objective)
            {
                case ObjectiveKind.Saver:return s.Money;
                case ObjectiveKind.Broker:return s.Received;
                case ObjectiveKind.Patron:int paid=0;foreach(int p in ids)if(p!=id&&s.Paid[p]>=c.PatronEach)paid++;return paid;
                case ObjectiveKind.TeamPlayer:int tasksGood=0;foreach(var t in tasks)if(t.Resolved&&t.Participated[id]&&t.GroupScore[id]>=c.TeamPlayerOutput)tasksGood++;return tasksGood;
                case ObjectiveKind.Rival:case ObjectiveKind.Guardian:return seats[s.ObjectiveTarget].Warnings;
                case ObjectiveKind.Archivist:int count=0;foreach(var r in records)if(s.Access.Contains(r.View.Id)&&(r.View.Actor>=0&&r.View.Actor!=id||r.View.Subject>=0&&r.View.Subject!=id))count++;return count;
                case ObjectiveKind.Presenter:return s.Presented.Count;
                default:return -1;
            }
        }
        private int ObjectiveThreshold(ObjectiveKind kind)
        {
            switch(kind){case ObjectiveKind.Saver:return c.StartMoney+c.SaverMargin;case ObjectiveKind.Broker:return c.BrokerReceived;case ObjectiveKind.Patron:return c.PatronColleagues;case ObjectiveKind.TeamPlayer:return c.TeamPlayerTasks;case ObjectiveKind.Rival:return 1;case ObjectiveKind.Guardian:return 0;case ObjectiveKind.Archivist:return c.ArchivistRecords;case ObjectiveKind.Presenter:return c.PresenterRecords;default:return c.StartBlame;}
        }
        private bool ObjectiveDone(int id)
        {
            var s=seats[id];if(s.Objective==ObjectiveKind.CleanRecord)return s.Blame<=c.StartBlame&&s.Warnings==0;
            if(s.Objective==ObjectiveKind.Guardian)return seats[s.ObjectiveTarget].Warnings==0&&meeting.Fired!=s.ObjectiveTarget&&!closed;
            if(s.Objective==ObjectiveKind.Archivist)
            {
                var colleagues=new HashSet<int>();foreach(var r in records)if(s.Access.Contains(r.View.Id)){if(HasSeat(r.View.Actor)&&r.View.Actor!=id)colleagues.Add(r.View.Actor);if(HasSeat(r.View.Subject)&&r.View.Subject!=id)colleagues.Add(r.View.Subject);}
                return ObjectiveProgress(id)>=c.ArchivistRecords&&colleagues.Count>=c.ArchivistColleagues;
            }
            return ObjectiveProgress(id)>=ObjectiveThreshold(s.Objective);
        }
        private void FinishResult(double time)
        {
            result=new LineWeekResult{WeekId=setup.WeekId,MatchId=setup.MatchId,ManagerProfileId=profile.Id,Days=days,Fired=meeting.Fired,FinalOutcome=meeting.Outcome,CompanyClosed=closed,FinalProductivityPermille=finalProductivity,TargetPercent=c.CompanyTargetPercent,IncidentsTotal=incidents.Count};
            foreach(var inc in incidents)if(inc.Exposed)result.Exposed.Add(inc.View);
            int winner=-1,best=int.MinValue,ties=0;
            if(!closed)foreach(int id in ids)
            {
                var s=seats[id];if(id==meeting.Fired||ExposedCount(id)>0||s.TasksConnected*2<schedule.Count)continue;
                int score=(int)(100L*s.Quality/s.TasksConnected)+(s.Opportunities==0?0:c.EotmCooperationWeight*s.Credits/s.Opportunities);
                if(score>best){winner=id;best=score;ties=1;}else if(score==best)ties++;
            }
            if(ties!=1)winner=-1;
            foreach(int id in ids)
            {
                var s=seats[id];var p=new LineWeekPlayerResult{Player=id,Blame=s.Blame,Warnings=s.Warnings,Fired=closed||id==meeting.Fired,Objective=s.Objective,ObjectiveTarget=s.ObjectiveTarget,ObjectiveDone=s.ObjectiveDone,EmployeeOfMonth=id==winner,Money=s.Money,ContributionQuality=s.TasksConnected==0?-1:s.Quality/s.TasksConnected,TasksConnected=s.TasksConnected,ExposedIncidents=ExposedCount(id)};
                if(!closed)
                {
                    if(!p.Fired)Career(p,CareerItem.NotFired,c.CareerNotFired);
                    if(finalProductivity>=c.CareerTargetPercent*10)Career(p,CareerItem.CompanyTarget,c.CareerCompanyTarget);
                    if(p.ObjectiveDone)Career(p,CareerItem.SecretObjective,c.CareerObjective);
                    if(p.EmployeeOfMonth)Career(p,CareerItem.EmployeeOfMonth,c.CareerEmployeeOfMonth);
                }
                if(p.ExposedIncidents>0)Career(p,CareerItem.ProvenSabotage,-c.CareerSabotagePenalty,p.ExposedIncidents);
                p.SessionPoints=PriorPoints(id)+p.CareerTotal;result.Players.Add(p);
            }
            if(!closed)foreach(var p in result.Players){p.Rank=1;foreach(var other in result.Players)if(other.CareerTotal>p.CareerTotal)p.Rank++;}
            result.ResultHash=LineCodec.HashResult(result);Changed();
        }
        private static void Career(LineWeekPlayerResult p,CareerItem item,int points,int count=1)
        {p.Career.Add(new LineCareerLine{Item=item,Points=points*count,Count=count});p.CareerTotal+=points*count;}
        private LineFailure PickNextManager(int id,LineAction a,double time)
        {
            if(Phase!=LinePhase.WeekResults)return LineFailure.WrongPhase;if(result.Fired!=id||closed)return LineFailure.NotAvailable;
            if(!string.IsNullOrEmpty(result.NextManagerPick))return LineFailure.AlreadyUsed;
            if(a.A<0||a.A>=LineManagerIds.All.Length||LineManagerIds.All[a.A]==profile.Id||!c.Profiles.Exists(p=>p.Id==LineManagerIds.All[a.A]))return LineFailure.BadParam;
            result.NextManagerPick=LineManagerIds.All[a.A];result.ResultHash=LineCodec.HashResult(result);Emit(LineEventType.NextManagerPicked,time,id,a:a.A);return LineFailure.None;
        }
        public LineSessionHistory NextHistory()
        {
            if(!IsFinished)throw new InvalidOperationException("Match is still running.");
            var h=setup.History==null?new LineSessionHistory():LineCodec.Clone(setup.History);
            while(h.SessionPoints.Count<6)h.SessionPoints.Add(0);while(h.TimesFired.Count<6)h.TimesFired.Add(0);while(h.RoleUses.Count<6)h.RoleUses.Add(0);while(h.TaskUses.Count<=LineTasks.Count)h.TaskUses.Add(0);
            h.WeeksPlayed++;h.PastManagers.Add(profile.Id);h.SeenTasks=new List<TaskKind>(seen);h.NextManager=result==null?"":result.NextManagerPick;
            foreach(var t in tasks)h.TaskUses[(int)t.Kind]++;
            foreach(int id in ids)h.RoleUses[id]+=seats[id].RoleUses;
            if(result!=null)foreach(var p in result.Players){h.SessionPoints[p.Player]=p.SessionPoints;if(p.Fired)h.TimesFired[p.Player]++;}
            return h;
        }
    }
}
