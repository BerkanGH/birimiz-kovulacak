using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    internal sealed partial class LineEngine
    {
        private sealed class TaskState
        {
            internal TaskKind Kind;
            internal int Number, Max, Role = -1, OtherRole = -1, Code, Output, Opt;
            internal double PlayEnd;
            internal bool Resolved, Remedied;
            internal readonly int[] A = new int[6], B = new int[6], Target = new int[6], Roles = new int[6], Q = new int[6], GroupScore = new int[6];
            internal readonly TaskField[] Fields = new TaskField[6];
            internal readonly bool[] Participated = new bool[6], Credit = new bool[6], Opportunity = new bool[6];
            internal readonly int[,] D = new int[6, 12];
            internal readonly List<int>[] Group = new List<int>[6], Items = new List<int>[6];
            internal readonly List<LineFact>[] Facts = new List<LineFact>[6];
            internal readonly List<LineControlView>[] Controls = new List<LineControlView>[6];
            internal readonly List<LineFact> Public = new List<LineFact>();
            internal readonly List<List<int>> Groups = new List<List<int>>();
            internal readonly List<int> Extra = new List<int>();
            internal readonly List<Decision> Decisions = new List<Decision>();
            internal LineTaskReveal Reveal;
            internal TaskState()
            {
                for (int i = 0; i < 6; i++) { A[i] = B[i] = Target[i] = -1; Group[i] = new List<int>(); Items[i] = new List<int>(); Facts[i] = new List<LineFact>(); Controls[i] = new List<LineControlView>(); }
            }
        }
        private static LineFact Fact(string key, int n1 = -1, int n2 = -1, int n3 = -1, int p = -1, int scope = 0)
        { return new LineFact { Key = key, N1 = n1, N2 = n2, N3 = n3, P = p, Scope = scope }; }
        private static LineOption Option(string key, int value, int n1 = -1, int n2 = -1, bool sabotage = false)
        { return new LineOption { Key = key, Value = value, N1 = n1, N2 = n2, Sabotage = sabotage }; }
        private LineControlView Choice(TaskState t, int id, string key, TaskField field, bool required, params LineOption[] options)
        {
            var v = new LineControlView { Kind = ControlKind.Choice, Field = field, Key = key, Required = required };
            v.Options.AddRange(options); t.Controls[id].Add(v); return v;
        }
        private void YesNo(TaskState t, int id, string key)
        { Choice(t, id, key, TaskField.B, false, Option("no", 0), Option("yes", 1, sabotage: true)).Sabotage = true; }
        private void Number(TaskState t, int id, string key, int min, int max, int digits = 0)
        { t.Controls[id].Add(new LineControlView { Kind = ControlKind.Number, Field = TaskField.A, Key = key, Min = min, Max = max, Digits = digits, Required = true }); }
        private double PlayLength(TaskKind kind) { return c.RoundPlayByPlayers[ids.Count - 3] * (c.Durations.RoundPlay / 75.0) * c.Task(kind).PlayFactor / 100.0; }
        private void StartTask(double time)
        {
            int number = Day * 2 + roundIndex; var kind = schedule[number];
            task = new TaskState { Number = number, Kind = kind, Max = c.Task(kind).MaxOutput }; tasks.Add(task); shortened = false; carry = 0;
            firstTimeTask = !seen.Contains(kind); if (firstTimeTask) seen.Add(kind);
            var order = new List<int>(ids); rng.Shuffle(order);
            if (LineTasks.Family(kind) == TaskFamily.Pair)
            {
                int i = 0; while (i < order.Count) { int size = order.Count - i == 3 ? 3 : 2; task.Groups.Add(order.GetRange(i, size)); i += size; }
                foreach (var group in task.Groups) foreach (int id in group) foreach (int peer in group) if (peer != id) { task.Group[id].Add(peer); task.Facts[id].Add(Fact("partner", p: peer, scope: 1)); }
            }
            else foreach (int id in ids) foreach (int peer in ids) if (id != peer) task.Group[id].Add(peer);
            foreach (int id in ids) task.Participated[id] = seats[id].Connected;
            DealTask(task, order);
            double intro = firstTimeTask ? c.Durations.RoundIntroFirst : c.Durations.RoundIntro;
            task.PlayEnd = time + intro + PlayLength(kind); Enter(LinePhase.RoundIntro, time, intro);
        }
        private void DealTask(TaskState t, List<int> order)
        {
            int n = ids.Count;
            switch (t.Kind)
            {
                case TaskKind.InvoiceReconcile:
                    foreach (int id in ids)
                    {
                        for (int j = 0; j < 2; j++) { t.D[id,j] = 1 + rng.Index(9); t.Facts[id].Add(Fact("line", j + 1, t.D[id,j])); }
                        Number(t,id,"total",0,99); YesNo(t,id,"pad");
                    }
                    foreach (var g in t.Groups) { int sum = 0; foreach (int id in g) sum += t.D[id,0] + t.D[id,1]; foreach (int id in g) t.D[id,2] = sum; } break;
                case TaskKind.BudgetSplit:
                    foreach (int id in ids) t.D[id,0] = 1;
                    for (int i = 0; i < n; i++) { var eligible = ids.FindAll(id => t.D[id,0] < 4); t.D[eligible[rng.Index(eligible.Count)],0]++; }
                    foreach (int id in ids) { t.Facts[id].Add(Fact("need",t.D[id,0])); Number(t,id,"request",0,2*n+2); }
                    t.Public.Add(Fact("budget",2*n+2,n,scope:2)); break;
                case TaskKind.CustomerSpec:
                    {
                        int color = rng.Index(3), size = rng.Index(3), day = rng.Index(3), packed = color*9+size*3+day;
                        var specs = new List<int> { packed };
                        for (int a = 0; a < 3; a++) for (int x = 1; x <= 2; x++)
                            specs.Add(((a==0 ? (color+x)%3 : color)*9)+((a==1 ? (size+x)%3 : size)*3)+(a==2 ? (day+x)%3 : day));
                        specs.RemoveAt(1+rng.Index(6)); rng.Shuffle(specs); int correct = specs.IndexOf(packed);
                        for(int k=0;k<6;k++)t.Public.Add(Fact("spec",k,specs[k],scope:2));
                        foreach(var g in t.Groups)
                        for (int i = 0; i < g.Count; i++)
                        {
                            int id = g[i]; t.D[id,0] = correct; t.D[id,1] = packed;
                            var decoys = new List<int>(); for(int k=0;k<6;k++) if(k!=correct) decoys.Add(k);
                            t.D[id,2] = decoys[rng.Index(5)]; t.Facts[id].Add(Fact("commission",t.D[id,2]));
                            if (g.Count==2) { if(i==0) { t.Facts[id].Add(Fact("color",color)); t.Facts[id].Add(Fact("size",size)); } else t.Facts[id].Add(Fact("day",day)); }
                            else t.Facts[id].Add(Fact(i==0?"color":i==1?"size":"day",i==0?color:i==1?size:day));
                            var control = Choice(t,id,"order",TaskField.A,true);
                            // Catalogues are group-local: their contents are public to each member's task screen.
                            for(int k=0;k<6;k++) { control.Options.Add(Option("spec",k,specs[k])); }
                        }
                    } break;
                case TaskKind.ReportReview:
                    for (int i=0;i<n;i++)
                    {
                        int id=order[i], preparer=order[(i+n-1)%n], approver=order[(i+1)%n], audited=order[(i+n-2)%n];
                        t.D[id,0]=10+rng.Index(80); t.D[id,1]=t.D[id,0]+new[]{8,12,15}[rng.Index(3)]; t.D[id,2]=preparer; t.D[id,3]=approver; t.D[id,4]=audited;
                        t.Group[id].Clear(); t.Group[id].Add(preparer); t.Group[id].Add(approver);
                        Choice(t,id,"version",TaskField.A,true,Option("accurate",0),Option("polished",1,sabotage:true)).Locks=true;
                        Choice(t,id,"decision",TaskField.B,true,Option("approve",0),Option("reject",1));
                    }
                    foreach(int id in ids) { t.Facts[id].Add(Fact("my_figure",t.D[id,0],t.D[id,1])); t.Facts[id].Add(Fact("audit_figure",t.D[t.D[id,4],0],p:t.D[id,4])); }
                    break;
                case TaskKind.RoomBooking:
                    t.Public.Add(Fact("slots",n-1,scope:2));
                    for(int i=0;i<n;i++)
                    {
                        int id=order[i]; t.D[id,0]=2+rng.Index(4); t.D[id,1]=i%(n-1);
                        do { t.D[id,2]=rng.Index(n-1); } while(t.D[id,2]==t.D[id,1]);
                        t.Facts[id].Add(Fact("value",t.D[id,0])); t.Facts[id].Add(Fact("ok_slot",t.D[id,1])); t.Facts[id].Add(Fact("ok_slot",t.D[id,2]));
                        var ctrl=Choice(t,id,"slot",TaskField.A,true); for(int k=0;k<n-1;k++) ctrl.Options.Add(Option("slot",k,k)); ctrl.Options.Add(Option("yield",9));
                    }
                    t.Opt=RoomOpt(t,0,0); break;
                case TaskKind.ArchiveVisitor:
                    t.Role=Role(t.Number); t.OtherRole=ids.Find(id=>id!=t.Role); int capacity=Math.Max(1,n-2);
                    var titles=new List<int>(); for(int j=0;j<10;j++) titles.Add(j); rng.Shuffle(titles); t.Extra.AddRange(titles.GetRange(0,6));
                    var codes=new List<int>{0,1,2,3,4,5}; rng.Shuffle(codes); int pos=0;
                    foreach(int id in ids) if(id!=t.Role) { t.D[id,0]=codes[pos++]; t.Roles[id]=2; t.Facts[id].Add(Fact("need_doc",t.Extra[t.D[id,0]])); t.Group[id].Clear(); t.Group[id].Add(t.Role); }
                    t.D[t.Role,0]=codes[pos]; t.Roles[t.Role]=1;
                    t.Public.Add(Fact("visitor",p:t.Role,scope:2)); t.Public.Add(Fact("capacity",capacity,scope:2));
                    foreach(int id in ids)
                    {
                        if(id==t.Role)
                        {
                            var ctrl=new LineControlView{Kind=ControlKind.Multi,Field=TaskField.A,Key="fetch",Required=true,MaxPicks=capacity}; t.Controls[id].Add(ctrl);
                            for(int k=0;k<6;k++) { ctrl.Options.Add(Option("doc",k,n2:t.Extra[k])); t.Facts[id].Add(Fact("doc",k,t.Extra[k])); }
                            t.Facts[id].Add(Fact("hr_file",t.D[id,0],p:t.OtherRole)); YesNo(t,id,"copy");
                        }
                        else { var ctrl=Choice(t,id,"confirm",TaskField.A,true); for(int k=0;k<6;k++) ctrl.Options.Add(Option("doc",k,n2:t.Extra[k])); }
                    } break;
                case TaskKind.BonusPool:
                    foreach(int id in ids) Choice(t,id,"choice",TaskField.A,true,Option("share",0),Option("take",1)); break;
                case TaskKind.ServerCode:
                    t.Role=Role(t.Number); t.Roles[t.Role]=1;
                    for(int i=0;i<n;i++) { int id=order[i]; t.D[id,0]=i; t.D[id,1]=(i==0?1:0)+rng.Index(i==0?9:10); t.Code=t.Code*10+t.D[id,1]; t.Public.Add(Fact("position",i,p:id,scope:2)); }
                    for(int i=0;i<n;i++) { int id=order[i], next=order[(i+1)%n]; t.Facts[id].Add(Fact("my_digit",i,t.D[id,1])); t.Facts[id].Add(Fact("next_digit",(i+1)%n,t.D[next,1])); Number(t,id,"code",Pow10(n-1),Pow10(n)-1,n); }
                    t.Facts[t.Role].Add(Fact("offer",c.TaskRules.ServerOffer)); YesNo(t,t.Role,"accept"); break;
                case TaskKind.PhishingCheck:
                    t.OtherRole=Role(t.Number); t.Role=Role(t.Number,t.OtherRole,false); t.Roles[t.Role]=1;
                    int fake=1000+rng.Index(9000); t.Code=fake;
                    // Everyone but the fake-mail holder shares the one real account: comparing endings finds the odd one.
                    int account; do { account=1000+rng.Index(9000); } while(account==fake);
                    foreach(int id in ids)
                    {
                        t.D[id,0]=id==t.OtherRole?fake:account; t.Facts[id].Add(Fact("account",t.D[id,0]));
                        var ctrl=new LineControlView{Kind=ControlKind.Player,Field=TaskField.Target,Key="flag",Required=true,AllowSelf=true};
                        foreach(int p in ids) ctrl.Options.Add(new LineOption{Key="player",Value=p,P=p}); t.Controls[id].Add(ctrl);
                    }
                    t.Facts[t.Role].Add(Fact("insider_offer",fake,c.TaskRules.PhishingKickback)); YesNo(t,t.Role,"accept"); break;
                case TaskKind.ShiftCover:
                    int shifts=(n+1)/2; t.Public.Add(Fact("shifts",shifts,scope:2));
                    for(int i=0;i<n;i++)
                    {
                        int id=order[i]; t.D[id,0]=i%shifts; t.D[id,1]=rng.Unit()<0.5?-1:(i+1)%shifts; t.D[id,2]=rng.Index(3);
                        t.Facts[id].Add(Fact("available",t.D[id,0])); if(t.D[id,1]>=0) t.Facts[id].Add(Fact("available",t.D[id,1])); t.Facts[id].Add(Fact("cost",t.D[id,2]));
                        var ctrl=Choice(t,id,"volunteer",TaskField.A,true); for(int k=0;k<shifts;k++) ctrl.Options.Add(Option("shift",k,k)); ctrl.Options.Add(Option("decline",9));
                    } break;
                case TaskKind.VendorPick:
                    t.Extra.AddRange(new[]{3,5,7,9}); rng.Shuffle(t.Extra); t.Role=Role(t.Number); t.Roles[t.Role]=1;
                    var weak=new List<int>(); for(int k=0;k<4;k++) if(t.Extra[k]<=5) weak.Add(k); t.Code=weak[rng.Index(weak.Count)];
                    for(int i=0;i<n;i++) { int id=order[i],vendor=i%4; t.D[id,0]=vendor; t.Facts[id].Add(Fact("tested",vendor,t.Extra[vendor])); var ctrl=Choice(t,id,"vote",TaskField.A,true); for(int k=0;k<4;k++) ctrl.Options.Add(Option("vendor",k,k)); }
                    t.Facts[t.Role].Add(Fact("kickback",t.Code,c.TaskRules.VendorKickback)); YesNo(t,t.Role,"accept"); break;
                case TaskKind.SupplySwap:
                    var types=new List<int>(); for(int i=0;i<n+2;i++) types.Add(i); rng.Shuffle(types);
                    var tokens=new List<int>();
                    for(int i=0;i<n;i++) { int id=order[i]; t.D[id,0]=types[(2*i)%(n+2)]; t.D[id,1]=types[(2*i+1)%(n+2)]; tokens.Add(t.D[id,0]);tokens.Add(t.D[id,1]); }
                    rng.Shuffle(tokens);
                    if(!DealItems(t,tokens,order,0))throw new InvalidOperationException("No exact cover.");
                    for(int i=0;i<n;i++)
                    {
                        int id=order[i]; var bonus=tokens.FindAll(x=>x!=t.D[id,0]&&x!=t.D[id,1]);t.D[id,2]=bonus[rng.Index(bonus.Count)];
                        t.Facts[id].Add(Fact("need",t.D[id,0])); t.Facts[id].Add(Fact("need",t.D[id,1])); t.Facts[id].Add(Fact("collector",t.D[id,2]));
                        t.Controls[id].Add(new LineControlView{Kind=ControlKind.Items,Field=TaskField.Target,Key="give"}); Choice(t,id,"done",TaskField.A,false,Option("done",1));
                    } break;
            }
        }
        private bool DealItems(TaskState t,List<int> tokens,List<int> owners,int index)
        {
            if(index==tokens.Count)return true;int item=tokens[index];
            foreach(int id in owners)if(t.Items[id].Count<2&&t.D[id,0]!=item&&t.D[id,1]!=item)
            {t.Items[id].Add(item);if(DealItems(t,tokens,owners,index+1))return true;t.Items[id].RemoveAt(t.Items[id].Count-1);}
            return false;
        }
        private static int Pow10(int n) { int x=1; while(n-->0) x*=10; return x; }
        private int RoomOpt(TaskState t,int index,int used)
        {
            if(index==ids.Count) return 0; int id=ids[index], best=RoomOpt(t,index+1,used);
            for(int j=1;j<=2;j++) { int s=t.D[id,j]; if((used&(1<<s))==0) best=Math.Max(best,t.D[id,0]+RoomOpt(t,index+1,used|(1<<s))); }
            return best;
        }
        private bool Complete(TaskState t,int id)
        {
            if(t.Kind==TaskKind.SupplySwap) return false;
            foreach(var ctrl in t.Controls[id]) if(ctrl.Required && ctrl.Enabled && (t.Fields[id]&ctrl.Field)==0) return false;
            return true;
        }
        private static int Bits(int value) { int count=0; while(value!=0) { value&=value-1; count++; } return count; }
        private bool Needs(TaskState t,int id) { return t.Items[id].Contains(t.D[id,0]) && t.Items[id].Contains(t.D[id,1]); }
        private LineFailure TaskAction(int id,LineAction a,double time)
        {
            if(Phase!=LinePhase.RoundPlay) return LineFailure.WrongPhase;
            if(Math.Max(seats[id].CoffeeUntil,seats[id].OfflineUntil)>time) return LineFailure.ComputerBlocked;
            var t=task;
            if(a.Verb==LineVerb.GiveItem)
            {
                if(t.Kind!=TaskKind.SupplySwap) return LineFailure.NotAvailable;
                var f=TargetFailure(id,a.Target); if(f!=LineFailure.None) return f;
                if(a.A<0 || a.A>=t.Items[id].Count) return LineFailure.NotOwned;
                bool before=Needs(t,a.Target); int item=t.Items[id][a.A]; t.Items[id].RemoveAt(a.A); t.Items[a.Target].Add(item); bool completes=!before&&Needs(t,a.Target);
                if(completes) t.Credit[id]=true;
                AddRecord(RecordKind.ItemTransfer,RecordSource.TaskSystem,time,id,a.Target,item,taskNumber:t.Number,access:new[]{id,a.Target});
                Emit(LineEventType.ItemGiven,time,id,a.Target,item,recipient:id); Emit(LineEventType.ItemGiven,time,id,a.Target,item,recipient:a.Target);
                Emit(LineEventType.HostChoice,time,id,(int)TaskField.Target,completes?1:0,t.Number,extra:0,host:true); return LineFailure.None;
            }
            if(a.C<=0 || (a.C&~7)!=0) return LineFailure.BadParam;
            var mask=(TaskField)a.C;
            // Validate every requested field before changing any of them.
            foreach(TaskField field in new[]{TaskField.Target,TaskField.A,TaskField.B}) if((mask&field)!=0)
            {
                var ctrl=t.Controls[id].Find(x=>x.Field==field && x.Kind!=ControlKind.Items);
                if(ctrl==null || !ctrl.Enabled) return LineFailure.NotAvailable;
                if(ctrl.Locks && (t.Fields[id]&field)!=0) return LineFailure.Locked;
                if(t.Kind==TaskKind.ReportReview && field==TaskField.B && (t.Fields[t.D[id,2]]&TaskField.A)==0) return LineFailure.Waiting;
                int value=field==TaskField.Target?a.Target:field==TaskField.A?a.A:a.B;
                if(ctrl.Kind==ControlKind.Number && (value<ctrl.Min || value>ctrl.Max)) return LineFailure.BadParam;
                if(ctrl.Kind==ControlKind.Multi)
                { if(value<0 || (value>>ctrl.Options.Count)!=0 || Bits(value)>ctrl.MaxPicks) return LineFailure.BadParam; }
                else if(ctrl.Kind!=ControlKind.Number && !ctrl.Options.Exists(x=>x.Value==value&&x.Enabled)) return LineFailure.BadParam;
                if(ctrl.Kind==ControlKind.Player && value==id && !ctrl.AllowSelf) return LineFailure.Self;
            }
            bool change=false; TaskField oldFields=t.Fields[id];
            foreach(TaskField field in new[]{TaskField.Target,TaskField.A,TaskField.B}) if((mask&field)!=0)
            {
                int value=field==TaskField.Target?a.Target:field==TaskField.A?a.A:a.B;
                int old=field==TaskField.Target?t.Target[id]:field==TaskField.A?t.A[id]:t.B[id];
                if(old!=value || (oldFields&field)==0) change=true;
                if(field==TaskField.Target)t.Target[id]=value; else if(field==TaskField.A)t.A[id]=value; else t.B[id]=value;
                t.Fields[id]|=field;
                Emit(LineEventType.HostChoice,time,id,(int)field,Category(t,id,field,value),t.Number,extra:(oldFields&field)!=0?1:0,host:true);
            }
            if(change) Emit(LineEventType.TaskSubmitted,time,actor:id);
            t.Decisions.Add(new Decision{Player=id,Time=time}); Shorten(time); return LineFailure.None;
        }
        private void Shorten(double time)
        {
            if(shortened || task.Kind==TaskKind.SupplySwap) return;
            int count=0; foreach(int id in ids) if(seats[id].Connected) { count++; if(!Complete(task,id))return; }
            if(count==0) return; shortened=true;
            double saved=Math.Max(0,PhaseEndTime-(time+c.Durations.AllSubmittedRemaining));
            PhaseEndTime-=saved; task.PlayEnd=PhaseEndTime;
            bool canCarry=roundIndex==0 || Day==days-1;
            if(c.EarlySubmit==EarlySubmitPolicy.CarryToBreak && canCarry)
                carry=Math.Min(saved,Math.Max(0,c.Durations.BreakMax-(roundIndex==0?c.Durations.Break:c.Durations.BreakClosing)));
            Emit(LineEventType.HostEarlySubmit,time,a:(int)Math.Round(saved*1000),b:(int)Math.Round(carry*1000),extra:task.Number,host:true); Changed();
        }
        private int Category(TaskState t,int id,TaskField field,int value)
        {
            if(field==TaskField.B)
            { if(t.Kind==TaskKind.ReportReview) { int p=t.D[id,2]; bool right=t.A[p]>=0&&((t.A[p]==0)==(value==0)); return value+(right?10:0); } return value; }
            switch(t.Kind)
            {
                case TaskKind.InvoiceReconcile:return value==t.D[id,2]?1:0;
                case TaskKind.BudgetSplit:return value<t.D[id,0]?0:value==t.D[id,0]?1:value==t.D[id,0]+1?2:3;
                case TaskKind.CustomerSpec:return value==t.D[id,0]?1:value==t.D[id,2]?2:0;
                case TaskKind.ReportReview:return value;
                case TaskKind.RoomBooking:return value==9?0:value==t.D[id,1]||value==t.D[id,2]?1:2;
                case TaskKind.ArchiveVisitor: if(id!=t.Role)return value==t.D[id,0]?1:0; int needed=0; foreach(int p in ids)if(p!=id&&(value&(1<<t.D[p,0]))!=0)needed++;return needed;
                case TaskKind.BonusPool:return value;
                case TaskKind.ServerCode:return value==t.Code?1:0;
                case TaskKind.PhishingCheck:return value==id?2:value==t.OtherRole?1:0;
                case TaskKind.ShiftCover:return value==9?0:value==t.D[id,0]||value==t.D[id,1]?1:2;
                case TaskKind.VendorPick:return t.Extra[value];
                default:return 1;
            }
        }
        private sealed class Decision { internal int Player; internal double Time; }
    }
}
