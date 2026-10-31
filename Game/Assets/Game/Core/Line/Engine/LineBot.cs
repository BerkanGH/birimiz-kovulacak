using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    public enum LineBotPolicy : byte { Talk=0, NoComm=1, Greedy=2 }
    /// <summary>A view-only synthetic policy. Shared facts are supplied only after simulated communication.</summary>
    public sealed class LineBot
    {
        private readonly int seat;private readonly LineRandom rng;private readonly LineBotPolicy policy;
        private uint request;private int taskNumber=-1,block=-1;private double nextCall;
        private readonly HashSet<int> contacted=new HashSet<int>();
        private readonly Dictionary<int,HashSet<int>> participants=new Dictionary<int,HashSet<int>>();
        private int week=int.MinValue;
        private bool complained,transferred,triedRemedial;private int chatDay=-1;
        private bool take,conflict;private int gifts;
        public LineBot(int seat,int seed,LineBotPolicy policy=LineBotPolicy.Talk)
        {if(seat<0||seat>=6||!Enum.IsDefined(typeof(LineBotPolicy),policy))throw new ArgumentException("Invalid bot.");this.seat=seat;this.policy=policy;rng=new LineRandom(seed);}
        private LineAction Action(LineView v,LineVerb verb,int target=-1,int a=-1,int b=-1,int c=-1,string text="")
        {return new LineAction(v.WeekId,++request,verb,target,a,b,c,text);}
        private static LineFact Fact(LineTaskView t,string key) {return t.Facts.Find(f=>f.Key==key)??t.PublicFacts.Find(f=>f.Key==key);}
        private static int N(LineTaskView t,string key,int fallback=0) {var f=Fact(t,key);return f==null?fallback:f.N1;}
        private static List<int> Ns(LineTaskView t,string key) {var a=new List<int>();foreach(var f in t.Facts)if(f.Key==key)a.Add(f.N1);return a;}
        public LineAction Next(LineView v,LineConfig config,double now,IReadOnlyList<LineView> shared=null)
        {
            if(v==null||v.Me!=seat||v.Phase==LinePhase.WeekResults||v.Phase==LinePhase.Abandoned)return null;
            var me=v.Players.Find(p=>p.Id==seat);if(me==null||!me.Connected)return null;
            if(week!=v.WeekId){week=v.WeekId;block=-1;taskNumber=-1;chatDay=-1;participants.Clear();}
            if(block!=v.Day){block=v.Day;complained=transferred=triedRemedial=false;}
            if(v.Task!=null&&(v.Phase==LinePhase.RoundIntro||v.Phase==LinePhase.RoundPlay))
            {
                if(!participants.TryGetValue(v.Task.Number,out var members)){members=new HashSet<int>();participants[v.Task.Number]=members;}
                foreach(var p in v.Players)if(p.Connected)members.Add(p.Id);
            }
            if(v.Task!=null&&taskNumber!=v.Task.Number){taskNumber=v.Task.Number;contacted.Clear();gifts=0;take=policy==LineBotPolicy.Greedy||rng.Unit()<(policy==LineBotPolicy.NoComm?0.4:0.08);conflict=policy==LineBotPolicy.Greedy||rng.Unit()<0.08;}
            var m=v.Mine;
            if(v.Features.CallsOpen&&m.PhoneDeadUntil<=now)
            {
                if(m.Phone.Status==CallStatus.Ringing)return Action(v,policy==LineBotPolicy.NoComm?LineVerb.Decline:LineVerb.Answer);
                if(m.Phone.Status==CallStatus.Connected){if(v.Task!=null)contacted.Add(m.Phone.Other);if(now-m.Phone.Since>=7)return Action(v,LineVerb.HangUp);}
            }
            if(m.IncomingRemedial!=null&&v.Features.CallsOpen)return Action(v,LineVerb.RespondRemedial,m.IncomingRemedial.Requester,1);
            if(v.Features.PresentationOpen&&m.PresentationsLeft>0&&m.PresentationsLeft==config.PresentationsPerMeeting)
            {
                var record=m.Records.Find(r=>r.ProofOf>0&&!r.Presented&&AboutOtherPlayer(v,r))??(m.Objective.Kind==ObjectiveKind.Presenter?m.Records.Find(r=>!r.Presented&&AboutOtherPlayer(v,r)):null);
                if(record!=null)return Action(v,LineVerb.PresentRecord,a:record.Id);
            }
            if(v.Features.EvidenceOpen&&m.PurchasesLeftThisBlock>0&&m.PurchasesLeftThisMatch>0&&v.Phase!=LinePhase.RoundPlay)
            {
                var offer=m.Offers.Find(o=>o.Available&&!o.Purchased&&o.Price<=m.Money&&((v.Phase==LinePhase.MeetingDossier&&o.Day==v.Day&&o.Kind==OfferKind.ITLog)||v.Phase==LinePhase.RoundReveal&&o.TaskNumber==v.TaskNumber));
                if(offer!=null)return Action(v,LineVerb.BuyEvidence,a:offer.Id);
            }
            if(v.Features.RemedialOpen&&!triedRemedial&&m.RemedialOptions.Count>0&&m.OutgoingRemedial==null)
            {int number=m.RemedialOptions[0];int peer=-1;if(participants.TryGetValue(number,out var members))foreach(var p in v.Players)if(p.Id!=seat&&p.Connected&&members.Contains(p.Id)){peer=p.Id;break;}if(peer>=0){triedRemedial=true;return Action(v,LineVerb.StartRemedial,peer,number);}}
            if(v.Features.TransfersOpen&&!transferred&&config.TransferMax>0&&now>=m.NextTransferAt)
            {
                if(m.Objective.Kind==ObjectiveKind.Patron&&m.Objective.Progress<m.Objective.Threshold&&m.Money>0)
                {
                    var paid = new int[6];
                    foreach(var record in m.Records)
                        if(record.Kind==RecordKind.Transfer&&record.Actor==seat&&record.Subject>=0&&record.Subject<6)
                            paid[record.Subject]+=Math.Max(0,record.N1);
                    int target=-1, remaining=int.MaxValue;
                    foreach(var player in v.Players)if(player.Id!=seat&&player.Connected)
                    {
                        int needed=config.PatronEach-paid[player.Id];
                        if(needed>0&&(needed<remaining||needed==remaining&&(target<0||player.Id<target)))
                        {target=player.Id;remaining=needed;}
                    }
                    if(target>=0)
                    {
                        transferred=true;
                        return Action(v,LineVerb.Transfer,target,Math.Min(m.Money,Math.Min(config.TransferMax,remaining)));
                    }
                }
                else if(m.Objective.Kind!=ObjectiveKind.Patron&&m.Money>=2&&policy==LineBotPolicy.Talk&&rng.Unit()<0.015)
                {int peer=Other(v);if(peer>=0){transferred=true;return Action(v,LineVerb.Transfer,peer,Math.Min(2,config.TransferMax));}}
            }
            if(v.Features.TricksUnlocked&&v.Features.CallsOpen&&v.Day>0&&!m.TrickUsedToday&&m.TricksLeftThisWeek>0&&m.Trick!=TrickType.None&&policy==LineBotPolicy.Greedy)
            {
                int target=Other(v);if(target>=0&&(m.Trick!=TrickType.SahteEposta||now>=m.NextMessageAt&&m.EmailsLeftThisBlock>0))return Action(v,LineVerb.UseTrick,target,a:m.Trick==TrickType.SahteEposta&&config.MailTemplates>0?0:-1,text:m.Trick==TrickType.SahteEposta&&config.MailTemplates==0?"Please check this memo.":"");
            }
            if(v.Phase==LinePhase.MeetingVote||v.Phase==LinePhase.FinalVote)
            {
                if(v.Meeting.MyVote== -1&&(v.Phase!=LinePhase.FinalVote||v.Meeting.Roster.Contains(seat)))
                {
                    int target=LineIds.Nobody,best=0;foreach(var p in v.Players)if(p.Id!=seat&&p.Blame>best&&p.Blame>config.StartBlame){best=p.Blame;target=p.Id;}
                    if(m.Objective.Kind==ObjectiveKind.Rival)target=m.Objective.Target;
                    return Action(v,LineVerb.Vote,target);
                }
                return null;
            }
            if(v.Features.MeetingChatOpen&&v.Day!=chatDay&&now>=m.NextMessageAt&&m.MeetingChatLeft>0&&policy==LineBotPolicy.Talk)
            {chatDay=v.Day;return Action(v,LineVerb.SendMeetingChat,text:"Let's compare the records.");}
            if(v.Features.ComplaintsOpen&&!complained&&v.Day>0&&now>=m.NextMessageAt&&m.ComplaintsLeftThisBlock>0&&policy==LineBotPolicy.Greedy&&config.ComplaintPresets>0)
            {int target=Other(v);if(target>=0){complained=true;return Action(v,LineVerb.FileComplaint,target,0);}}
            if(v.Task==null||v.Phase!=LinePhase.RoundPlay||m.ComputerBlockedUntil>now)return null;
            var t=v.Task;
            if(policy==LineBotPolicy.Talk)
            {
                // Fixed placeholder text: the harness models information sharing, never real dialogue.
                foreach(int peer in t.Group)if(!contacted.Contains(peer)&&v.Players.Exists(p=>p.Id==peer&&p.Connected))
                {
                    if(now>=m.NextMessageAt&&m.TextsLeftThisBlock>0){contacted.Add(peer);return Action(v,LineVerb.SendText,peer,text:"Let's compare our task facts.");}
                    if(now>=nextCall&&m.Phone.Status==CallStatus.Idle&&m.PhoneDeadUntil<=now){nextCall=now+3;return Action(v,LineVerb.Call,peer);}
                    break;
                }
            }
            var known=new List<LineView>{v};
            if(policy==LineBotPolicy.Talk&&shared!=null)foreach(var s in shared)if(s!=null&&s.Me!=seat&&s.Task!=null&&s.Task.Number==t.Number&&t.Group.Contains(s.Me))known.Add(s);
            // Wait for the partner dependency before submitting; remaining time still bounds the policy.
            if(policy==LineBotPolicy.Talk&&known.Count<t.Group.Count+1&&now<v.PhaseEndTime-10)return null;
            if(t.Kind==TaskKind.SupplySwap)
            {
                if(policy==LineBotPolicy.Talk&&known.Count>1)
                {
                    for(int index=0;index<t.Items.Count;index++)
                    {
                        int item=t.Items[index];if(Ns(t,"need").Contains(item))continue;
                        foreach(var peer in known)if(peer.Me!=seat&&Ns(peer.Task,"need").Contains(item)&&!peer.Task.Items.Contains(item)&&v.Players.Exists(p=>p.Id==peer.Me&&p.Connected))return Action(v,LineVerb.GiveItem,peer.Me,index);
                    }
                }
                else if(gifts<2&&t.Items.Count>0&&!t.SubmittedFields.HasFlag(TaskField.A))
                {int peer=Other(v);if(peer>=0){gifts++;return Action(v,LineVerb.GiveItem,peer,rng.Index(t.Items.Count));}}
            }
            int a=Choose(v,known),b=0,flagTarget=-1;TaskField mask=TaskField.None;
            if(t.Kind==TaskKind.PhishingCheck)
            {
                int ending=-1;foreach(var k in known){var f=Fact(k.Task,"insider_offer");if(f!=null)ending=f.N1;}
                flagTarget=seat;foreach(var k in known)if(N(k.Task,"account")==ending)flagTarget=k.Me;
                if(ending<0)flagTarget=v.Players[rng.Index(v.Players.Count)].Id;
            }
            if(conflict&&policy!=LineBotPolicy.NoComm)b=1;
            if(t.Kind==TaskKind.ReportReview)
            {
                b=0;var review=Fact(t,"review_figure");
                if(review!=null&&review.N1>=0)foreach(var k in known)foreach(var f in k.Task.Facts)
                    if(f.Key=="audit_figure"&&f.P==review.P)b=review.N1==f.N1?0:1;
            }
            foreach(var ctrl in t.Controls)
            {
                if(!ctrl.Enabled||ctrl.WaitingFor>=0||ctrl.Kind==ControlKind.Items)continue;
                int value=ctrl.Field==TaskField.Target?flagTarget:ctrl.Field==TaskField.A?a:b;
                int old=ctrl.Field==TaskField.Target?t.MyTarget:ctrl.Field==TaskField.A?t.MyA:t.MyB;
                if((t.SubmittedFields&ctrl.Field)==0||!ctrl.Locks&&value!=old)mask|=ctrl.Field;
            }
            if(mask==TaskField.None)return null;
            return Action(v,LineVerb.SubmitTask,flagTarget,a,b,(int)mask);
        }
        private bool AboutOtherPlayer(LineView v,LineRecordView record)
        {return v.Players.Exists(p=>p.Id!=seat&&(p.Id==record.Actor||p.Id==record.Subject));}
        private static Dictionary<int,int> ShiftAssignment(List<LineView> known)
        {
            var present=known.FindAll(v=>v.Players.Exists(p=>p.Id==v.Me&&p.Connected));
            present.Sort((a,b)=>a.Me.CompareTo(b.Me));
            var current=new Dictionary<int,int>();var best=new Dictionary<int,int>();int coverage=-1,cost=int.MaxValue;
            SearchShifts(present,0,0,0,current,ref coverage,ref cost,ref best);return best;
        }
        private static void SearchShifts(List<LineView> known,int index,int used,int cost,Dictionary<int,int> current,
            ref int bestCoverage,ref int bestCost,ref Dictionary<int,int> best)
        {
            if(index==known.Count)
            {
                int coverage=Count(used);
                if(coverage>bestCoverage||coverage==bestCoverage&&cost<bestCost)
                {bestCoverage=coverage;bestCost=cost;best=new Dictionary<int,int>(current);}
                return;
            }
            var v=known[index];current[v.Me]=9;
            SearchShifts(known,index+1,used,cost,current,ref bestCoverage,ref bestCost,ref best);
            foreach(int shift in Ns(v.Task,"available"))if((used&(1<<shift))==0)
            {
                current[v.Me]=shift;
                SearchShifts(known,index+1,used|(1<<shift),cost+N(v.Task,"cost"),current,ref bestCoverage,ref bestCost,ref best);
            }
        }
        private int Other(LineView v) {foreach(var p in v.Players)if(p.Id!=seat&&p.Connected)return p.Id;return -1;}
        private int Choose(LineView v,List<LineView> known)
        {
            var t=v.Task;bool greedy=policy==LineBotPolicy.Greedy;
            switch(t.Kind)
            {
                case TaskKind.InvoiceReconcile:
                    int sum=0;foreach(var k in known)foreach(var f in k.Task.Facts)if(f.Key=="line")sum+=f.N2;
                    return sum+10*(t.Group.Count+1-known.Count);
                case TaskKind.BudgetSplit:return Math.Min(2*v.Players.Count+2,N(t,"need")+(greedy||policy==LineBotPolicy.NoComm?2:conflict?1:0));
                case TaskKind.CustomerSpec:
                    var choices=new List<LineOption>(t.Controls.Find(c=>c.Key=="order").Options);
                    foreach(var k in known)foreach(var f in k.Task.Facts)if(f.Key=="color"||f.Key=="size"||f.Key=="day")
                    {int div=f.Key=="color"?9:f.Key=="size"?3:1;choices.RemoveAll(o=>o.N1/div%3!=f.N1);}
                    if(conflict)return N(t,"commission");
                    foreach(var peer in known)if(peer.Me!=seat&&peer.Task.MyA>=0)
                    {
                        // A partner ordering its commission spec can misreport that preference as the customer choice.
                        // Trust is seeded once per task, so repeated polls cannot reroll the decision.
                        if(take&&peer.Task.MyA==N(peer.Task,"commission",-1))return peer.Task.MyA;
                    }
                    return choices.Count>0?choices[0].Value:0;
                case TaskKind.ReportReview:
                    // version uses A; the B field below is computed separately in Next.
                    return greedy&&conflict?1:0;
                case TaskKind.RoomBooking:
                    var assignment=RoomAssignment(known);return assignment.TryGetValue(seat,out int slot)?slot:Ns(t,"ok_slot")[0];
                case TaskKind.ArchiveVisitor:
                    if(t.Role==1)
                    {
                        int mask=0,cap=N(t,"capacity");foreach(var k in known)if(k.Me!=seat)
                        {int title=N(k.Task,"need_doc",-1);var f=t.Facts.Find(x=>x.Key=="doc"&&x.N2==title);if(f!=null&&Count(mask)<cap)mask|=1<<f.N1;}
                        if(mask==0)for(int i=0;i<cap;i++)mask|=1<<i;return mask;
                    }
                    int need=N(t,"need_doc");foreach(var o in t.Controls.Find(c=>c.Key=="confirm").Options)if(o.N2==need)return o.Value;return 0;
                case TaskKind.BonusPool:return take?1:0;
                case TaskKind.ServerCode:
                    int n=v.Players.Count;var digits=new int[n];for(int i=0;i<n;i++)digits[i]=-1;
                    foreach(var k in known)foreach(var f in k.Task.Facts)if(f.Key=="my_digit"||f.Key=="next_digit")digits[f.N1]=f.N2;
                    int code=0;for(int i=0;i<n;i++)code=code*10+(digits[i]>=0?digits[i]:i==0?1+rng.Index(9):rng.Index(10));return code;
                case TaskKind.PhishingCheck:return 0;
                case TaskKind.ShiftCover:
                    // Preserve the solo policies' original minimum-slot choice. Only Talk coordinates a matching.
                    if(policy!=LineBotPolicy.Talk||known.Count==1)
                    {
                        int pick=9;foreach(int available in Ns(t,"available"))pick=Math.Min(pick,available);
                        return greedy&&N(t,"cost")==2?9:pick;
                    }
                    var shifts=ShiftAssignment(known);
                    return shifts.TryGetValue(seat,out int shift)?shift:9;
                case TaskKind.VendorPick:
                    if(greedy&&Fact(t,"kickback")!=null)return N(t,"kickback");int best=-1,vendor=0;foreach(var k in known){var f=Fact(k.Task,"tested");if(f!=null&&f.N2>best){best=f.N2;vendor=f.N1;}}return vendor;
                default:return 1;
            }
        }
        private static int Count(int n){int c=0;while(n!=0){n&=n-1;c++;}return c;}
        private static Dictionary<int,int> RoomAssignment(List<LineView> known)
        {
            known.Sort((a,b)=>a.Me.CompareTo(b.Me));var current=new Dictionary<int,int>();var best=new Dictionary<int,int>();int bestValue=-1;
            SearchRoom(known,0,0,0,current,ref bestValue,ref best);return best;
        }
        private static void SearchRoom(List<LineView> known,int i,int used,int score,Dictionary<int,int> current,ref int bestScore,ref Dictionary<int,int> best)
        {
            if(i==known.Count){if(score>bestScore){bestScore=score;best=new Dictionary<int,int>(current);}return;}
            var v=known[i];current[v.Me]=9;SearchRoom(known,i+1,used,score,current,ref bestScore,ref best);
            foreach(int s in Ns(v.Task,"ok_slot"))if((used&(1<<s))==0){current[v.Me]=s;SearchRoom(known,i+1,used|(1<<s),score+N(v.Task,"value"),current,ref bestScore,ref best);}
        }
    }
}
