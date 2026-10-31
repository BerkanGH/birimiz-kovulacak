using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    internal sealed partial class LineEngine
    {
        private sealed class IncidentState { internal LineIncidentView View; internal int Victim; internal TrickType Trick; internal bool Exposed; }
        private sealed class RecordState { internal LineRecordView View; internal bool Purchasable; }
        private readonly List<IncidentState> incidents=new List<IncidentState>();
        private readonly List<RecordState> records=new List<RecordState>();
        private readonly List<LineRemedialView> remedials=new List<LineRemedialView>();
        private IncidentState Incident(IncidentKind kind,int actor,int victim,int number,double time,TrickType trick=TrickType.None)
        {
            var inc=new IncidentState{View=new LineIncidentView{Id=incidents.Count+1,Kind=kind,Actor=actor,Day=Day,TaskNumber=number,Time=time},Victim=victim,Trick=trick};incidents.Add(inc);
            Emit(LineEventType.HostIncident,time,actor,victim,(int)kind,inc.View.Id,extra:number,host:true);Changed();return inc;
        }
        private LineRecordView AddRecord(RecordKind kind,RecordSource source,double time,int actor=-1,int subject=-1,int n1=-1,int n2=-1,int n3=-1,int taskNumber=-1,int proof=-1,IEnumerable<int> access=null,bool publicRecord=false,bool purchasable=true)
        {
            var v=new LineRecordView{Id=records.Count+1,Kind=kind,Source=source,Time=time,Day=Day,TaskNumber=taskNumber,Task=taskNumber>=0?schedule[taskNumber]:TaskKind.None,Actor=actor,Subject=subject,N1=n1,N2=n2,N3=n3,ProofOf=proof};
            var state=new RecordState{View=v,Purchasable=purchasable};records.Add(state);
            if(publicRecord)foreach(int id in ids)Grant(id,v.Id,time);else if(access!=null)foreach(int id in access)Grant(id,v.Id,time);
            if(purchasable)foreach(int id in ids)if(HasPurchase(seats[id],v))Grant(id,v.Id,time);
            Changed();return v;
        }
        private bool HasPurchase(Seat s,LineRecordView r)
        { return r.Source==RecordSource.Bank&&s.Bought.Contains(100+r.Day)||r.Source==RecordSource.TaskSystem&&r.TaskNumber>=0&&s.Bought.Contains(200+r.TaskNumber)||r.Source==RecordSource.ITLog&&s.Bought.Contains(300+r.Day)||r.Source==RecordSource.BadgeLog&&s.Bought.Contains(400+r.Day); }
        private void Grant(int id,int record,double time) { if(seats[id].Access.Add(record)){Emit(LineEventType.RecordGranted,time,a:record,recipient:id);Changed();} }
        private int Filter(int raw) { return Math.Min(c.BlockIncomeCap,Math.Min(raw,c.BlockIncomeFull)+Math.Max(0,raw-c.BlockIncomeFull)/2); }
        private void Money(int id,int amount,MoneyReason reason,double time)
        {
            int raw=amount;var s=seats[id];
            if(reason==MoneyReason.TaskIncome||reason==MoneyReason.Remedial||reason==MoneyReason.Favourite)
            { amount=Filter(s.RawIncome+raw)-Filter(s.RawIncome);s.RawIncome+=raw; }
            s.Money+=amount;
            if(raw!=0)
            {
                Emit(LineEventType.MoneyChanged,time,a:amount,b:(int)reason,recipient:id);
                Emit(LineEventType.HostIncome,time,id,a:amount,b:(int)reason,extra:raw,host:true);Changed();
            }
        }
        private void Deal(int id,int amount,IncidentState inc,double time,RecordKind kind)
        {
            Money(id,amount,MoneyReason.DealIncome,time);
            AddRecord(kind,RecordSource.Bank,time,id,n1:kind==RecordKind.OfferAcceptance?(int)inc.View.Kind:amount,n2:inc==null?0:(int)inc.View.Kind,
                taskNumber:task.Number,proof:inc==null?-1:inc.View.Id,access:new[]{id});
        }
        private List<LineOfferView> Offers(int id)
        {
            var list=new List<LineOfferView>();
            for(int day=0;day<=Day;day++)
            {
                Offer(list,id,100+day,OfferKind.BankLedger,day,-1,c.BankLedgerPrice-profile.BankLedgerDiscount);
                Offer(list,id,300+day,OfferKind.ITLog,day,-1,c.ITLogPrice);Offer(list,id,400+day,OfferKind.BadgeLog,day,-1,c.BadgeLogPrice);
            }
            foreach(var t in tasks)if(t.Resolved)Offer(list,id,200+t.Number,OfferKind.TaskAudit,t.Number/2,t.Number,c.TaskAuditPrice);
            return list;
        }
        private void Offer(List<LineOfferView> list,int id,int number,OfferKind kind,int day,int taskNumber,int price)
        {
            var s=seats[id];bool bought=s.Bought.Contains(number);
            list.Add(new LineOfferView{Id=number,Kind=kind,Day=day,TaskNumber=taskNumber,Task=taskNumber<0?TaskKind.None:schedule[taskNumber],Price=Math.Max(1,price)+c.PriceStep*s.Purchases,
                Purchased=bought,Available=EvidenceOpen&&!bought&&s.BlockPurchases<c.PurchasesPerBlock&&s.Purchases<c.PurchasesPerMatch});
        }
        private List<int> RemedialOptions(int id)
        {
            var list=new List<int>();var s=seats[id];
            if(!RemedialOpen||s.BlockRemedials>=c.RemedialsPerBlock||s.Remedials>=c.RemedialsPerMatch||remedials.Exists(r=>r.Requester==id))return list;
            foreach(var t in tasks)if(t.Resolved&&t.Output<t.Max&&!t.Remedied&&!remedials.Exists(r=>r.TaskNumber==t.Number))list.Add(t.Number);return list;
        }
        private LineFailure EvidenceAction(int id,LineAction a,double time)
        {
            var s=seats[id];
            switch(a.Verb)
            {
                case LineVerb.Transfer:
                    if(!TransfersOpen)return LineFailure.WrongPhase;var f=TargetFailure(id,a.Target,false);if(f!=LineFailure.None)return f;
                    if(a.A<1||a.A>c.TransferMax)return LineFailure.BadParam;if(time<s.NextTransfer)return LineFailure.RateLimited;if(a.A>s.Money)return LineFailure.InsufficientFunds;
                    Money(id,-a.A,MoneyReason.TransferOut,time);Money(a.Target,a.A,MoneyReason.TransferIn,time);s.Paid[a.Target]+=a.A;seats[a.Target].Received+=a.A;s.NextTransfer=time+c.TransferMinInterval;
                    var transfer=new TransferTime{A=id,B=a.Target,Time=time};transfers.Add(transfer);
                    AddRecord(RecordKind.Transfer,RecordSource.Bank,time,id,a.Target,a.A,access:new[]{id,a.Target});
                    Emit(LineEventType.TransferMade,time,id,a.Target,a.A,recipient:id);Emit(LineEventType.TransferMade,time,id,a.Target,a.A,recipient:a.Target);return LineFailure.None;
                case LineVerb.BuyEvidence:
                    if(!EvidenceOpen)return LineFailure.WrongPhase;if(s.Bought.Contains(a.A))return LineFailure.AlreadyUsed;
                    var offer=Offers(id).Find(o=>o.Id==a.A);if(offer==null)return LineFailure.NotAvailable;
                    if(s.BlockPurchases>=c.PurchasesPerBlock||s.Purchases>=c.PurchasesPerMatch)return LineFailure.LimitReached;
                    if(s.Money<offer.Price)return LineFailure.InsufficientFunds;
                    s.Bought.Add(a.A);s.BlockPurchases++;s.Purchases++;Money(id,-offer.Price,MoneyReason.Purchase,time);
                    foreach(var record in records)if(record.Purchasable&&HasPurchase(s,record.View))Grant(id,record.View.Id,time);
                    Emit(LineEventType.PurchaseMade,time,a:a.A,b:offer.Price,recipient:id);return LineFailure.None;
                case LineVerb.PresentRecord:
                    if(!PresentOpen)return LineFailure.WrongPhase;if(!s.Access.Contains(a.A))return LineFailure.NotOwned;
                    if(s.Presentations>=c.PresentationsPerMeeting)return LineFailure.LimitReached;
                    var v=records[a.A-1].View;bool repeat=v.Presented;s.Presentations++;s.Presented.Add(a.A);v.Presented=true;foreach(int p in ids)Grant(p,v.Id,time);
                    var presentation=new LinePresentationView{Presenter=id,Record=v,Time=time,Repeat=repeat};
                    if(v.ProofOf>0)
                    {
                        var inc=incidents[v.ProofOf-1];if(!inc.Exposed)
                        {
                            inc.Exposed=true;inc.View.ExposedBy=id;Blame(inc.View.Actor,c.ExposedSabotageBlame,BlameReason.ExposedSabotage,time,v.Task);
                            presentation.ExposedIncident=inc.View.Id;presentation.ExposedKind=inc.View.Kind;
                            Emit(LineEventType.IncidentExposed,time,target:inc.View.Actor,a:inc.View.Id,b:(int)inc.View.Kind);
                        }
                    }
                    meeting.Presentations.Add(presentation);Emit(LineEventType.EvidencePresented,time,id,a:v.Id);return LineFailure.None;
                case LineVerb.StartRemedial:
                    if(!RemedialOpen)return LineFailure.WrongPhase;var target=TargetFailure(id,a.Target);if(target!=LineFailure.None)return target;
                    if(!RemedialOptions(id).Contains(a.A))return LineFailure.NotAvailable;
                    var t=tasks.Find(x=>x.Number==a.A);if(!t.Participated[a.Target])return LineFailure.NotAvailable;
                    // One incoming request keeps the contract's single IncomingRemedial truthful.
                    if(remedials.Exists(r=>r.Cosigner==a.Target))return LineFailure.Busy;
                    remedials.Add(new LineRemedialView{Requester=id,Cosigner=a.Target,TaskNumber=a.A,Task=t.Kind,ExpiresAt=time+c.Durations.RemedialTimeout});
                    Emit(LineEventType.RemedialRequested,time,id,a:a.A,recipient:a.Target);return LineFailure.None;
                case LineVerb.RespondRemedial:
                    if(!Office)return LineFailure.WrongPhase;if(a.A!=0&&a.A!=1)return LineFailure.BadParam;
                    int index=remedials.FindIndex(r=>r.Cosigner==id&&r.Requester==a.Target);if(index<0)return LineFailure.NotOwned;
                    ResolveRemedial(index,a.A==1,time);return LineFailure.None;
                default:return LineFailure.BadParam;
            }
        }
        private void EndRemedials(double time) { while(remedials.Count>0)ResolveRemedial(remedials.Count-1,false,time); }
        private void ResolveRemedial(int index,bool accept,double time)
        {
            var r=remedials[index];remedials.RemoveAt(index);var t=tasks.Find(x=>x.Number==r.TaskNumber);var requester=seats[r.Requester];
            accept=accept&&!t.Remedied&&t.Output<t.Max&&requester.BlockRemedials<c.RemedialsPerBlock&&requester.Remedials<c.RemedialsPerMatch&&finalProductivity<0;
            if(accept)
            {
                int prior=Provisional(),delta=Math.Min(c.RemedialOutput,t.Max-t.Output);t.Output+=delta;t.Reveal.Output=t.Output;t.Remedied=true;foreach(int id in ids)t.GroupScore[id]=LineTasks.Family(t.Kind)==TaskFamily.Pair?Math.Min(100,t.GroupScore[id]+100*delta/t.Max):100*t.Output/t.Max;requester.Remedials++;requester.BlockRemedials++;
                Blame(r.Requester,-c.RemedialBlame,BlameReason.Remedial,time,t.Kind);Money(r.Requester,c.RemedialIncome,MoneyReason.Remedial,time);Money(r.Cosigner,c.RemedialIncome,MoneyReason.Remedial,time);
                // Cooperation is normalised per task, including remedial work on that task.
                foreach(int id in new[]{r.Requester,r.Cosigner})
                {
                    if(!t.Opportunity[id]){t.Opportunity[id]=true;seats[id].Opportunities++;}
                    if(!t.Credit[id]){t.Credit[id]=true;seats[id].Credits++;}
                }
                lastChange=CompanyChange.Remedial;lastTask=t.Kind;lastDelta=delta;
                AddRecord(RecordKind.Remedial,RecordSource.HR,time,r.Requester,r.Cosigner,t.Number,delta,taskNumber:t.Number,publicRecord:true);
                Emit(LineEventType.ProductivityChanged,time,a:Provisional(),b:Provisional()-prior);
            }
            Emit(LineEventType.RemedialResolved,time,r.Requester,r.Cosigner,accept?1:0,r.TaskNumber,recipient:r.Requester);
            Emit(LineEventType.RemedialResolved,time,r.Requester,r.Cosigner,accept?1:0,r.TaskNumber,recipient:r.Cosigner);Changed();
        }
        private sealed class TransferTime { internal int A,B;internal double Time; }
        private readonly List<TransferTime> transfers=new List<TransferTime>();
    }
}
