using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    internal sealed partial class LineEngine
    {
        private int DefaultA(TaskKind kind) { return kind==TaskKind.BudgetSplit||kind==TaskKind.BonusPool||kind==TaskKind.ArchiveVisitor?0:kind==TaskKind.RoomBooking||kind==TaskKind.ShiftCover?9:-1; }
        private void ResolveTask(double time)
        {
            var t=task; var r=c.TaskRules;
            var incomeBefore = new int[6];
            foreach (int id in ids) incomeBefore[id] = Filter(seats[id].RawIncome);
            t.Reveal=new LineTaskReveal{Kind=t.Kind,Index=roundIndex,Number=t.Number,MaxOutput=t.Max};
            foreach(int id in ids)
            {
                var s=seats[id]; bool missing=!Complete(t,id)&&t.Kind!=TaskKind.SupplySwap;
                if(missing || !s.Connected)
                {
                    int reason=!s.Connected?3:Math.Max(s.CoffeeUntil,s.OfflineUntil)>time?2:1;
                    if(missing) Emit(LineEventType.HostMissedTask,time,id,a:t.Number,b:reason,host:true);
                    if(missing && reason==1)
                    {
                        t.Reveal.Missed.Add(id); var delta=Blame(id,c.MissedTaskBlame,BlameReason.MissedTask,time,t.Kind); t.Reveal.Deltas.Add(delta);
                    }
                    // Absent players are censored to defaults only for fields they never submitted: a submission made before a disconnect stands.
                    if((t.Fields[id]&TaskField.A)==0) t.A[id]=t.Kind==TaskKind.ArchiveVisitor&&id!=t.Role?-1:DefaultA(t.Kind);
                    if((t.Fields[id]&TaskField.Target)==0) t.Target[id]=-1;
                    if((t.Fields[id]&TaskField.B)==0) t.B[id]=0;
                }
                if(t.B[id]<0)t.B[id]=0;
            }
            int output=0, withoutSabotage=0, fullGroups=0;
            switch(t.Kind)
            {
                case TaskKind.InvoiceReconcile:
                    int correct=0,groupsCorrect=0;
                    foreach(var group in t.Groups)
                    {
                        int good=0,pads=0; foreach(int id in group) { if(t.A[id]==t.D[id,2])good++; if(t.B[id]==1)pads++; }
                        int baseline=100*good/group.Count; int score=Math.Max(0,baseline-r.InvoicePaddingLoss*pads); output+=score; withoutSabotage+=baseline; if(score>=100)fullGroups++; if(good==group.Count)groupsCorrect++;
                        foreach(int id in group)
                        {
                            bool exact=t.A[id]==t.D[id,2]; t.Q[id]=exact?100:0; t.Credit[id]=good==group.Count; t.GroupScore[id]=score;
                            if(exact)Money(id,r.InvoiceCorrectIncome,MoneyReason.TaskIncome,time);
                            if(t.B[id]==1) { var inc=Incident(IncidentKind.InvoicePadding,id,-1,t.Number,time); Deal(id,r.InvoicePaddingKickback,inc,time,RecordKind.DealPayment); }
                            PartnerEntry(t,id,time); AuditEntry(t,id,time);
                        }
                        correct+=good;
                    }
                    output/=t.Groups.Count; withoutSabotage/=t.Groups.Count; t.Reveal.PublicFacts.Add(Fact("correct_entries",correct,ids.Count,scope:2)); t.Reveal.PublicFacts.Add(Fact("groups_correct",groupsCorrect,t.Groups.Count,scope:2)); break;
                case TaskKind.BudgetSplit:
                    int sum=0,successes=0,budget=2*ids.Count+2; foreach(int id in ids)sum+=Math.Max(0,t.A[id]);
                    foreach(int id in ids)
                    {
                        int request=Math.Max(0,t.A[id]),grant=sum<=budget?request:request*budget/sum; bool success=grant>=t.D[id,0];
                        if(success) { successes++; Money(id,r.BudgetSuccessIncome,MoneyReason.TaskIncome,time); }
                        Money(id,Math.Min(r.BudgetSurplusIncomeMax,Math.Max(0,grant-t.D[id,0])),MoneyReason.TaskIncome,time);
                        t.Q[id]=success?100:0; t.Credit[id]=request<=t.D[id,0]+1&&sum<=budget;
                        t.Reveal.PublicFacts.Add(Fact("grant",request,grant,success?1:0,id,2)); TaskVote(t,id,request);
                    }
                    output=100*successes/ids.Count; t.Reveal.PublicFacts.Add(Fact("overflow",sum,budget,scope:2)); break;
                case TaskKind.CustomerSpec:
                    int orders=0;
                    foreach(var g in t.Groups)
                    {
                        int good=0; foreach(int id in g) if(t.A[id]==t.D[id,0])good++;
                        int score=100*good/g.Count; output+=score; orders+=good; if(score>=100)fullGroups++;
                        foreach(int id in g)
                        {
                            bool exact=t.A[id]==t.D[id,0]; t.Q[id]=exact?100:0; t.GroupScore[id]=score;
                            if(exact)Money(id,r.SpecCorrectIncome,MoneyReason.TaskIncome,time);
                            if(t.A[id]==t.D[id,2])Deal(id,r.SpecCommission,null,time,RecordKind.DealPayment);
                            PartnerEntry(t,id,time);
                        }
                        t.Reveal.PublicFacts.Add(Fact("true_spec",t.D[g[0],1],scope:2));
                    }
                    output/=t.Groups.Count; t.Reveal.PublicFacts.Add(Fact("correct_orders",orders,ids.Count,scope:2)); break;
                case TaskKind.ReportReview:
                    int approved=0;
                    foreach(int id in ids)
                    {
                        int approver=t.D[id,3]; bool submitted=t.A[id]>=0, accurate=t.A[id]==0, approve=t.B[approver]==0;
                        bool right=submitted && (accurate==approve); int score=!submitted?0:accurate?(approve?r.ReportAccurateApproved:r.ReportAccurateRejected):(approve?r.ReportPolishedApproved:r.ReportPolishedRejected);
                        output+=score; withoutSabotage+=!submitted?0:approve?r.ReportAccurateApproved:r.ReportAccurateRejected; if(approve&&submitted)approved++;
                        if(submitted&&accurate&&approve)Money(id,r.ReportAcceptedIncome,MoneyReason.TaskIncome,time);
                        if(right)Money(approver,r.ReportCorrectDecisionIncome,MoneyReason.TaskIncome,time);
                        t.Q[id]+=submitted&&accurate?50:0; t.Q[approver]+=right?50:0; t.Credit[approver]=right;
                        IncidentState inc=null; if(submitted&&!accurate) { inc=Incident(IncidentKind.CookedReport,id,-1,t.Number,time); if(approve)Deal(id,r.ReportPolishedKickback,null,time,RecordKind.DealPayment); }
                        AddRecord(RecordKind.ReportDraft,RecordSource.TaskSystem,time,id,n1:t.A[id],n2:t.D[id,0],n3:submitted?t.D[id,accurate?0:1]:-1,taskNumber:t.Number,proof:inc==null?-1:inc.View.Id);
                        // Split records prevent a partner record from disclosing the preparer's true figure or private draft.
                        AddRecord(RecordKind.TaskEntry,RecordSource.TaskSystem,time,id,approver,n2:submitted?t.D[id,accurate?0:1]:-1,taskNumber:t.Number,access:new[]{id,approver});
                        AddRecord(RecordKind.TaskEntry,RecordSource.TaskSystem,time,approver,id,n3:t.B[approver],taskNumber:t.Number,access:new[]{id,approver});
                        int auditor=ids.Find(p=>t.D[p,4]==id);
                        AddRecord(RecordKind.AuditFigure,RecordSource.TaskSystem,time,auditor,id,t.D[id,0],taskNumber:t.Number,access:new[]{auditor});
                    }
                    output/=ids.Count; withoutSabotage/=ids.Count; t.Reveal.PublicFacts.Add(Fact("reports_approved",approved,ids.Count,scope:2)); break;
                case TaskKind.RoomBooking:
                    int value=0; var counts=new int[ids.Count-1]; foreach(int id in ids) if(t.A[id]>=0&&t.A[id]<counts.Length)counts[t.A[id]]++;
                    foreach(int id in ids)
                    { int slot=t.A[id]; bool ok=slot>=0&&slot<counts.Length&&counts[slot]==1&&(slot==t.D[id,1]||slot==t.D[id,2]); t.D[id,3]=ok?1:0; if(ok)value+=t.D[id,0]; }
                    output=t.Opt==0?0:100*value/t.Opt;
                    foreach(int id in ids)
                    {
                        int slot=t.A[id]; bool ok=t.D[id,3]==1,collision=slot>=0&&slot<counts.Length&&counts[slot]>1;
                        t.Q[id]=ok||slot==9&&output>=90?100:collision?0:50; t.Credit[id]=slot==9&&output>=90;
                        if(ok)Money(id,r.RoomSuccessIncome,MoneyReason.TaskIncome,time); if(slot==9)Money(id,r.RoomYieldIncome,MoneyReason.TaskIncome,time);
                        t.Reveal.PublicFacts.Add(Fact("booking",slot,ok?1:0,p:id,scope:2)); TaskVote(t,id,slot);
                    } break;
                case TaskKind.ArchiveVisitor:
                    int served=0,k=Math.Max(1,ids.Count-2),mask=Math.Max(0,t.A[t.Role]);
                    foreach(int id in ids)if(id!=t.Role)
                    {
                        bool confirmed=t.A[id]==t.D[id,0],got=confirmed&&(mask&(1<<t.D[id,0]))!=0;
                        t.Q[id]=confirmed?100:0; if(got) { served++; Money(id,r.ArchiveServedIncome,MoneyReason.TaskIncome,time); }
                        AuditEntry(t,id,time);
                    }
                    output=100*served/k; withoutSabotage=output; t.Q[t.Role]=output; Money(t.Role,r.ArchiveVisitorIncomePerServed*served,MoneyReason.TaskIncome,time);
                    for(int doc=0;doc<6;doc++)if((mask&(1<<doc))!=0)AddRecord(RecordKind.BadgeAccess,RecordSource.BadgeLog,time,t.Role,n1:doc,n2:0,taskNumber:t.Number);
                    if(t.B[t.Role]==1)
                    {
                        output=Math.Max(0,output-r.ArchiveSnoopLoss); var inc=Incident(IncidentKind.ArchiveSnoop,t.Role,t.OtherRole,t.Number,time);
                        AddRecord(RecordKind.BadgeAccess,RecordSource.BadgeLog,time,t.Role,t.OtherRole,t.D[t.Role,0],1,taskNumber:t.Number,proof:inc.View.Id);
                        AddRecord(RecordKind.HrFile,RecordSource.BadgeLog,time,t.Role,t.OtherRole,(int)seats[t.OtherRole].Objective,taskNumber:t.Number,access:new[]{t.Role},purchasable:false);
                    }
                    AuditEntry(t,t.Role,time); t.Reveal.PublicFacts.Add(Fact("served",served,k,scope:2)); break;
                case TaskKind.BonusPool:
                    int takes=0;
                    foreach(var g in t.Groups)
                    {
                        int takers=0; foreach(int id in g)if(t.A[id]==1)takers++; takes+=takers;
                        int score=(g.Count==2?r.BonusPairOutput:r.BonusTrioOutput)[takers]; output+=score; if(score>=100)fullGroups++;
                        foreach(int id in g)
                        {
                            bool take=t.A[id]==1; int income=takers==0?r.BonusAllShareIncome:takers==g.Count?r.BonusAllTakeIncome:!take?0:takers==1?r.BonusSoloTakerIncome:r.BonusTrioTwoTakersIncome;
                            Money(id,income,MoneyReason.TaskIncome,time); t.Q[id]=take?30:100; t.Credit[id]=takers==0; t.GroupScore[id]=score; PartnerEntry(t,id,time);
                        }
                    }
                    output/=t.Groups.Count; t.Reveal.PublicFacts.Add(Fact("takes",takes,scope:2)); break;
                case TaskKind.ServerCode:
                    int exacts=0; foreach(int id in ids)if(t.A[id]==t.Code)exacts++;
                    bool cracked=exacts>ids.Count/2; output=cracked?100:Math.Min(r.ServerFailCap,r.ServerFailBase+r.ServerFailPerExact*exacts);
                    foreach(int id in ids) { bool exact=t.A[id]==t.Code; t.Q[id]=exact?100:0; if(exact)Money(id,r.ServerExactIncome,MoneyReason.TaskIncome,time); if(cracked)Money(id,r.ServerCrackedIncome,MoneyReason.TaskIncome,time); AuditEntry(t,id,time); }
                    if(t.B[t.Role]==1) { var inc=Incident(IncidentKind.AcceptedCompetitorOffer,t.Role,-1,t.Number,time); Deal(t.Role,!cracked?r.ServerOffer:0,inc,time,RecordKind.OfferAcceptance); }
                    t.Reveal.PublicFacts.Add(Fact("cracked",cracked?1:0,exacts,scope:2)); break;
                case TaskKind.PhishingCheck:
                    int winner=PluralityTask(t,true,false); bool blocked=winner==t.OtherRole;
                    output=blocked?r.PhishingBlockedOutput:r.PhishingFailedOutput;
                    foreach(int id in ids) { bool right=t.Target[id]==t.OtherRole; t.Q[id]=right?100:0; if(right&&blocked)Money(id,r.PhishingFlagIncome,MoneyReason.TaskIncome,time); TaskVote(t,id,t.Target[id]); AuditEntry(t,id,time); }
                    if(t.B[t.Role]==1) { var inc=Incident(IncidentKind.AcceptedKickback,t.Role,-1,t.Number,time); Deal(t.Role,!blocked?r.PhishingKickback:0,inc,time,RecordKind.OfferAcceptance); }
                    t.Reveal.PublicFacts.Add(Fact("fake_holder",p:t.OtherRole,scope:2)); t.Reveal.PublicFacts.Add(Fact("blocked",blocked?1:0,scope:2)); break;
                case TaskKind.ShiftCover:
                    var covered=new bool[(ids.Count+1)/2]; foreach(int id in ids)if(t.A[id]>=0&&t.A[id]<covered.Length&&(t.A[id]==t.D[id,0]||t.A[id]==t.D[id,1]))covered[t.A[id]]=true;
                    int coveredCount=0; foreach(bool yes in covered)if(yes)coveredCount++; output=100*coveredCount/covered.Length;
                    var onShift=new int[covered.Length]; foreach(int id in ids)if(t.A[id]>=0&&t.A[id]<covered.Length&&(t.A[id]==t.D[id,0]||t.A[id]==t.D[id,1]))onShift[t.A[id]]++;
                    foreach(int id in ids)
                    {
                        bool volunteer=t.A[id]>=0&&t.A[id]<covered.Length&&(t.A[id]==t.D[id,0]||t.A[id]==t.D[id,1]);
                        bool sole=volunteer&&onShift[t.A[id]]==1;
                        bool uncovered=!covered[t.D[id,0]]||(t.D[id,1]>=0&&!covered[t.D[id,1]]);
                        // Overtime is paid to the one volunteer who covers a shift; doubling up earns nothing (coordination needed).
                        t.Q[id]=sole?100:volunteer?50:t.A[id]==9&&uncovered?0:60; t.Credit[id]=sole&&output==100;
                        if(sole)Money(id,Math.Max(0,r.ShiftOvertime-t.D[id,2]),MoneyReason.TaskIncome,time); TaskVote(t,id,t.A[id]); AuditEntry(t,id,time);
                    }
                    t.Reveal.PublicFacts.Add(Fact("covered",coveredCount,covered.Length,scope:2)); break;
                case TaskKind.VendorPick:
                    int vendor=PluralityTask(t,false,true),quality=vendor<0?0:t.Extra[vendor]; output=(100*quality+4)/9;
                    foreach(int id in ids) { t.Q[id]=t.A[id]>=0?100*t.Extra[t.A[id]]/9:0; if(t.A[id]==vendor&&quality==9)Money(id,r.VendorBestIncome,MoneyReason.TaskIncome,time); TaskVote(t,id,t.A[id]); AuditEntry(t,id,time); }
                    if(t.B[t.Role]==1) { var inc=Incident(IncidentKind.VendorKickback,t.Role,-1,t.Number,time); Deal(t.Role,vendor==t.Code?r.VendorKickback:0,inc,time,RecordKind.OfferAcceptance); }
                    t.Reveal.PublicFacts.Add(Fact("winner",vendor,quality,scope:2)); for(int v=0;v<4;v++)t.Reveal.PublicFacts.Add(Fact("quality",v,t.Extra[v],scope:2)); break;
                case TaskKind.SupplySwap:
                    int complete=0; foreach(int id in ids)
                    { bool done=Needs(t,id); if(done) { complete++; Money(id,r.SwapCompleteIncome,MoneyReason.TaskIncome,time); } t.Q[id]=done?100:0; if(t.Items[id].Contains(t.D[id,2]))Deal(id,r.SwapCollectorBonus,null,time,RecordKind.DealPayment); }
                    output=100*complete/ids.Count; t.Reveal.PublicFacts.Add(Fact("completed",complete,ids.Count,scope:2)); break;
            }
            int prior=Provisional(); t.Output=Math.Max(0,Math.Min(t.Max,(int)((long)output*t.Max/100))); t.Resolved=true; t.Reveal.Output=t.Output;
            foreach(int id in ids)
            {
                if(LineTasks.Family(t.Kind)!=TaskFamily.Pair)t.GroupScore[id]=output;
                if(t.Participated[id]) { seats[id].Quality+=Math.Max(0,Math.Min(100,t.Q[id])); seats[id].TasksConnected++; if(HasCreditRule(t.Kind)){seats[id].Opportunities++;t.Opportunity[id]=true;} if(t.Credit[id])seats[id].Credits++; }
                int flags = (t.Opportunity[id] ? 1 : 0) | (t.Participated[id] && t.Credit[id] ? 2 : 0) | (seats[id].Connected ? 4 : 0);
                Emit(LineEventType.HostOutcome, time, actor:id, target:t.Number,
                    a:t.Participated[id] ? Math.Max(0, Math.Min(100, t.Q[id])) : 0,
                    b:Filter(seats[id].RawIncome)-incomeBefore[id], extra:flags, host:true);
            }
            // Counterfactual output holds submitted non-sabotage decisions fixed; it never changes the task result.
            int loss = 0;
            if (t.Kind == TaskKind.InvoiceReconcile || t.Kind == TaskKind.ReportReview || t.Kind == TaskKind.ArchiveVisitor)
                loss = Math.Max(0, Math.Min(t.Max, (int)((long)withoutSabotage*t.Max/100))-t.Output);
            bool pairs = LineTasks.Family(t.Kind) == TaskFamily.Pair;
            int groups = pairs ? t.Groups.Count : 1;
            if (!pairs) fullGroups = t.Output == t.Max ? 1 : 0;
            Emit(LineEventType.HostTaskOutcome, time, a:t.Number, b:loss, extra:groups*10+fullGroups, host:true);
            if(favourite>=0)Money(favourite,profile.FavouriteIncome,MoneyReason.Favourite,time);
            AddRecord(RecordKind.TaskOutcome,RecordSource.TaskSystem,time,n1:t.Output,n2:t.Max,taskNumber:t.Number,publicRecord:true);
            lastChange=CompanyChange.TaskResolved; lastTask=t.Kind; lastDelta=t.Output; Changed();
            Emit(LineEventType.TaskResolved,time,a:t.Number,b:t.Output); Emit(LineEventType.ProductivityChanged,time,a:Provisional(),b:Provisional()-(prior<0?0:prior));
        }
        private static bool HasCreditRule(TaskKind kind)
        { return kind==TaskKind.InvoiceReconcile||kind==TaskKind.BudgetSplit||kind==TaskKind.ReportReview||kind==TaskKind.RoomBooking||kind==TaskKind.BonusPool||kind==TaskKind.ShiftCover||kind==TaskKind.SupplySwap; }
        private void TaskVote(TaskState t,int id,int value) { t.Reveal.Votes.Add(new LineVoteView{Voter=id,Target=value}); }
        private void AuditEntry(TaskState t,int id,double time) { AddRecord(RecordKind.TaskEntry,RecordSource.TaskSystem,time,id,n1:t.Target[id],n2:t.A[id],n3:t.B[id],taskNumber:t.Number); }
        private void PartnerEntry(TaskState t,int id,double time)
        { var access=new List<int>(t.Group[id]); access.Add(id); AddRecord(RecordKind.TaskEntry,RecordSource.TaskSystem,time,id,n1:t.Target[id],n2:t.A[id],n3:-1,taskNumber:t.Number,access:access); }
        private int PluralityTask(TaskState t,bool target,bool randomTies)
        {
            int max=0; var tied=new List<int>(); int limit=target?6:4;
            for(int v=0;v<limit;v++) { int count=0; foreach(int id in ids)if((target?t.Target[id]:t.A[id])==v)count++; if(count>max){max=count;tied.Clear();}if(count==max&&count>0)tied.Add(v); }
            return tied.Count==0?-1:tied.Count==1?tied[0]:randomTies?tied[rng.Index(tied.Count)]:-1;
        }
        private int ResolvedOutput() { int sum=0; foreach(var t in tasks)if(t.Resolved)sum+=t.Output; return sum; }
        private int ResolvedMax() { int sum=0; foreach(var t in tasks)if(t.Resolved)sum+=t.Max; return sum; }
        private int ScheduledMax() { int sum=0; foreach(var kind in schedule)sum+=c.Task(kind).MaxOutput; return sum; }
        private int Provisional() { int max=ResolvedMax(); return max==0?-1:(int)(1000L*ResolvedOutput()/max); }
        private void FreezeCompany() { if(finalProductivity>=0)return; finalProductivity=(int)(1000L*ResolvedOutput()/ScheduledMax());closed=finalProductivity<c.CompanyTargetPercent*10;Changed(); }
        private LineBlameDelta Blame(int id,int delta,BlameReason reason,double time,TaskKind kind=TaskKind.None)
        {
            int old=seats[id].Blame; seats[id].Blame=Math.Max(0,old+delta); var d=new LineBlameDelta{Player=id,Delta=seats[id].Blame-old,Reason=reason,Task=kind,Day=Day};
            blameLog.Add(d); Emit(LineEventType.BlameChanged,time,target:id,a:d.Delta,b:(int)reason); Changed();return d;
        }
        private readonly List<LineBlameDelta> blameLog=new List<LineBlameDelta>();
    }
}
