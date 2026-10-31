using System;
using System.Collections.Generic;
using System.Text;

namespace BirimizKovulacak.Core
{
    public static class LineText
    {
        public static string Sanitize(string text, int maxChars, bool allowNewlines, out LineFailure failure)
        {
            failure=LineFailure.None;
            if(maxChars<0) { failure=LineFailure.BadParam; return null; }
            string normal;
            try { normal=(text??"").Replace("\r\n","\n").Normalize(NormalizationForm.FormC); }
            catch(ArgumentException) { failure=LineFailure.BadParam; return null; }
            var b=new StringBuilder(normal.Length); int consecutive=0;
            foreach(char ch in normal)
            {
                if(ch=='\n'&&allowNewlines) { if(consecutive<3)b.Append(ch); consecutive++; continue; }
                if(char.IsControl(ch))continue;
                consecutive=0; b.Append(ch=='<'?'‹':ch=='>'?'›':ch);
            }
            string value=b.ToString().Trim();
            if(value.Length==0)failure=LineFailure.EmptyText; else if(value.Length>maxChars)failure=LineFailure.TooLong;
            return failure==LineFailure.None?value:null;
        }
    }
    internal sealed partial class LineEngine
    {
        private sealed class Call
        {
            internal int Id, Caller, Callee, Day;
            internal double Start, End=-1;
            internal bool Connected;
            internal LineCallRecordView CallerLog,CalleeLog;
        }
        private readonly List<Call> calls=new List<Call>();
        private readonly List<LineComplaintView> complaints=new List<LineComplaintView>();
        private int nextCallId=1,nextMessageId=1;
        private Call FindCall(int id) { return calls.Find(x=>x.Id==id); }
        private LineFailure PhoneAction(int id,LineAction a,double time)
        {
            if(!Office)return LineFailure.WrongPhase;
            var s=seats[id]; if(s.DeadUntil>time)return LineFailure.PhoneDead;
            if(a.Verb==LineVerb.Call)
            {
                var f=TargetFailure(id,a.Target);if(f!=LineFailure.None)return f;
                if(s.Phone.Status!=CallStatus.Idle)return LineFailure.AlreadyInCall;
                var other=seats[a.Target];
                if(other.Phone.Status!=CallStatus.Idle||other.DeadUntil>time)
                { Emit(LineEventType.CallBusy,time,target:a.Target,recipient:id);Emit(LineEventType.MissedCall,time,actor:id,recipient:a.Target);return LineFailure.Busy; }
                var call=new Call{Id=nextCallId++,Caller=id,Callee=a.Target,Start=time,Day=Day};
                call.CallerLog=new LineCallRecordView{CallId=call.Id,Other=a.Target,Outgoing=true,Start=time};call.CalleeLog=new LineCallRecordView{CallId=call.Id,Other=id,Start=time};
                calls.Add(call);s.Log.Add(call.CallerLog);other.Log.Add(call.CalleeLog);s.Call=other.Call=call.Id;
                s.Phone=new LinePhoneView{Status=CallStatus.Dialing,Other=a.Target,Since=time,RingUntil=time+c.Durations.Ring};
                other.Phone=new LinePhoneView{Status=CallStatus.Ringing,Other=id,Since=time,RingUntil=time+c.Durations.Ring};Emit(LineEventType.CallIncoming,time,actor:id,recipient:a.Target);
            }
            else if(a.Verb==LineVerb.Answer||a.Verb==LineVerb.Decline)
            {
                if(s.Phone.Status!=CallStatus.Ringing)return LineFailure.NotRinging;
                if(a.Verb==LineVerb.Decline)EndCall(id,time,CallEndReason.Declined);
                else
                {
                    var call=FindCall(s.Call);call.Connected=true;call.Start=time;call.CallerLog.Connected=call.CalleeLog.Connected=true;call.CallerLog.Start=call.CalleeLog.Start=time;
                    int other=s.Phone.Other;s.Phone=new LinePhoneView{Status=CallStatus.Connected,Other=other,CallId=call.Id,Since=time};
                    seats[other].Phone=new LinePhoneView{Status=CallStatus.Connected,Other=id,CallId=call.Id,Since=time};
                    Emit(LineEventType.CallConnected,time,call.Caller,call.Callee,call.Id,recipient:id);Emit(LineEventType.CallConnected,time,call.Caller,call.Callee,call.Id,recipient:other);
                }
            }
            else { if(s.Phone.Status==CallStatus.Idle)return LineFailure.NotInCall;EndCall(id,time,CallEndReason.HungUp); }
            return LineFailure.None;
        }
        private void EndCall(int id,double time,CallEndReason reason)
        {
            var s=seats[id];if(s.Call<0)return;var call=FindCall(s.Call);int other=s.Phone.Other;
            call.End=time;call.CallerLog.End=call.CalleeLog.End=time;call.CallerLog.EndReason=call.CalleeLog.EndReason=reason;
            s.Phone=new LinePhoneView();seats[other].Phone=new LinePhoneView();s.Call=seats[other].Call=-1;
            Emit(LineEventType.CallEnded,time,other,a:(int)reason,b:call.Id,recipient:id);Emit(LineEventType.CallEnded,time,id,a:(int)reason,b:call.Id,recipient:other);
            if(call.Connected)AddRecord(RecordKind.Call,RecordSource.PhoneLog,time,call.Caller,call.Callee,(int)Math.Max(0,time-call.Start),access:new[]{id,other});Changed();
        }
        private void EndAllCalls(double time,CallEndReason reason) { foreach(int id in ids)EndCall(id,time,reason); }
        public int ConnectedCallId(int player) { return HasSeat(player)&&seats[player].Phone.Status==CallStatus.Connected?seats[player].Phone.CallId:-1; }
        public int CallPartner(int player) { return HasSeat(player)&&seats[player].Phone.Status==CallStatus.Connected?seats[player].Phone.Other:-1; }
        public int TappersOf(int speaker,int[] buffer)
        {
            if(buffer==null)throw new ArgumentNullException(nameof(buffer));if(!HasSeat(speaker)||!seats[speaker].Connected||CallPartner(speaker)<0)return 0;
            int count=0,partner=CallPartner(speaker);foreach(int id in ids)if(seats[id].Connected&&seats[id].TapUntil>now&&(seats[id].TapTarget==speaker||seats[id].TapTarget==partner)&&count<buffer.Length)buffer[count++]=id;return count;
        }
        public List<LineMessage> MessagesFor(int player,long afterSeq)
        {
            if(!HasSeat(player))throw new ArgumentException("Unknown recipient.");var list=new List<LineMessage>();
            foreach(var m in seats[player].Messages)if(m.Seq>afterSeq)list.Add(CopyMessage(m));return list;
        }
        private static LineMessage CopyMessage(LineMessage m)
        { return new LineMessage{Seq=m.Seq,Id=m.Id,Kind=m.Kind,ThreadId=m.ThreadId,ReplyTo=m.ReplyTo,From=m.From,To=m.To,ToAll=m.ToAll,Text=m.Text,TemplateId=m.TemplateId,About=m.About,Time=m.Time,Day=m.Day}; }
        private int Deliver(MessageKind kind,int from,int to,string text,int template,int about,int reply,double time,int root=-1,bool senderCopy=true)
        {
            int id=nextMessageId++;var m=new LineMessage{Id=id,Kind=kind,ThreadId=root<0?id:root,ReplyTo=reply,From=from,To=to,ToAll=to==-1,Text=text,TemplateId=template,About=about,Time=time,Day=Day};
            foreach(int recipient in ids)if(to==-1||recipient==to||senderCopy&&recipient==from)
            {
                var copy=CopyMessage(m);var s=seats[recipient];copy.Seq=++s.MessageSeq;s.Messages.Add(copy);if(s.Messages.Count>c.MessagesKeptPerPlayer)s.Messages.RemoveAt(0);
                Emit(LineEventType.MessageReceived,time,a:(int)kind,b:(int)copy.Seq,recipient:recipient);
            }
            Changed();return id;
        }
        private void MessageTelemetry(int id,MessageKind kind,int to,string text,double time,int flags)
        { Emit(LineEventType.HostMessage,time,id,to,(int)kind,text.Length<=20?0:text.Length<=80?1:2,extra:flags,host:true); }
        private LineFailure MessageAction(int id,LineAction a,double time)
        {
            if(Phase==LinePhase.WeekResults)return LineFailure.WrongPhase;
            var s=seats[id];if(time<s.NextMessage)return LineFailure.RateLimited;
            MessageKind kind;int max;bool newline=false;int flags=0;
            if(a.Verb==LineVerb.SendMeetingChat)
            { if(!IsMeeting)return LineFailure.WrongPhase;if(s.Chats>=c.MeetingChatPerMeeting)return LineFailure.LimitReached;kind=MessageKind.MeetingChat;max=c.MeetingChatMaxChars; }
            else if(a.Verb==LineVerb.FileComplaint)
            {
                if(!Office)return LineFailure.WrongPhase;var f=TargetFailure(id,a.Target,false);if(f!=LineFailure.None)return f;
                if(s.Complaints>=c.ComplaintsPerBlock)return LineFailure.LimitReached;if(a.A< -1||a.A>=c.ComplaintPresets)return LineFailure.BadParam;
                kind=MessageKind.Complaint;max=c.ComplaintMaxChars;if(a.A>=0)flags|=4;
            }
            else if(a.Verb==LineVerb.SendEmail)
            {
                if(Math.Max(s.CoffeeUntil,s.OfflineUntil)>time)return LineFailure.ComputerBlocked;
                if(s.Emails>=c.EmailsPerBlock)return LineFailure.LimitReached;if(a.A< -1||a.A>=c.MailTemplates||a.B!= -1&&!HasSeat(a.B))return LineFailure.BadParam;
                if(a.Target!= -1){var f=TargetFailure(id,a.Target,false);if(f!=LineFailure.None)return f;}
                kind=MessageKind.Email;max=c.EmailMaxChars;newline=true;if(a.A>=0)flags|=2;
            }
            else
            { var f=TargetFailure(id,a.Target,false);if(f!=LineFailure.None)return f;if(s.Texts>=c.TextsPerBlock)return LineFailure.LimitReached;kind=MessageKind.Text;max=c.TextMaxChars; }
            string text="";
            if(!string.IsNullOrEmpty(a.Text)||flags==0)
            { text=LineText.Sanitize(a.Text,max,newline,out var fail);if(fail!=LineFailure.None)return fail; }
            int root=-1;
            if(kind==MessageKind.Email&&a.C!= -1)
            {
                var original=s.Messages.Find(m=>m.Id==a.C&&m.Kind==MessageKind.Email);
                if(original==null)return LineFailure.NotOwned;
                int expected=original.From==id?original.To:original.From;
                if(a.Target!=expected)return LineFailure.InvalidTarget;root=original.ThreadId;flags|=1;
            }
            if(kind==MessageKind.Complaint) { s.Complaints++;complaints.Add(new LineComplaintView{Target=a.Target,Preset=a.A,Text=text}); }
            else { int to=kind==MessageKind.MeetingChat?-1:a.Target;Deliver(kind,id,to,text,kind==MessageKind.Email?a.A:-1,kind==MessageKind.Email?a.B:-1,kind==MessageKind.Email?a.C:-1,time,root);if(kind==MessageKind.Text)s.Texts++;else if(kind==MessageKind.Email)s.Emails++;else s.Chats++; }
            s.NextMessage=time+c.MessageMinInterval;MessageTelemetry(id,kind,kind==MessageKind.MeetingChat?-1:a.Target,text,time,flags);return LineFailure.None;
        }
        private void Hit(int id,TrickType trick,double time)
        {
            var s=seats[id];if(trick==TrickType.KahveDok)s.CoffeeUntil=Math.Max(s.CoffeeUntil,time+c.CoffeeSeconds);
            else if(trick==TrickType.InternetKes)s.OfflineUntil=Math.Max(s.OfflineUntil,time+c.InternetSeconds);
            else if(trick==TrickType.FisCek){s.DeadUntil=Math.Max(s.DeadUntil,time+c.PhoneDeadSeconds);EndCall(id,time,CallEndReason.LineCut);}
            Emit(LineEventType.TrickHit,time,a:(int)trick,recipient:id);Deliver(MessageKind.System,LineIds.System,id,"",(int)trick,-1,-1,time);
            AddRecord(RecordKind.TrickEffect,RecordSource.ITLog,time,subject:id,n1:(int)trick,access:new[]{id});
        }
        private LineFailure TrickAction(int id,LineAction a,double time)
        {
            if(!Office)return LineFailure.WrongPhase;var s=seats[id];if(!Unlocked)return LineFailure.NotUnlocked;
            if(s.TrickUsed)return LineFailure.AlreadyUsed;if(s.TricksUsed>=TrickAllowance)return LineFailure.LimitReached;if(s.Trick==TrickType.None)return LineFailure.NotAvailable;
            bool target=s.Trick!=TrickType.InternetKes;if(target){var f=TargetFailure(id,a.Target);if(f!=LineFailure.None)return f;}
            string text="";if(s.Trick==TrickType.SahteEposta)
            {
                if(a.A< -1||a.A>=c.MailTemplates||a.B!= -1&&!HasSeat(a.B))return LineFailure.BadParam;
                if(time<s.NextMessage)return LineFailure.RateLimited;if(s.Emails>=c.EmailsPerBlock)return LineFailure.LimitReached;
                if(!string.IsNullOrEmpty(a.Text)||a.A<0){text=LineText.Sanitize(a.Text,c.EmailMaxChars,true,out var f);if(f!=LineFailure.None)return f;}
            }
            IncidentKind kind=s.Trick==TrickType.KahveDok?IncidentKind.TrickCoffee:s.Trick==TrickType.FisCek?IncidentKind.TrickPhoneCut:s.Trick==TrickType.InternetKes?IncidentKind.TrickInternet:s.Trick==TrickType.HatDinle?IncidentKind.TrickWiretap:IncidentKind.TrickFakeEmail;
            int number=InTask?task.Number:-1;var inc=Incident(kind,id,target?a.Target:-1,number,time,s.Trick);s.TrickUsed=true;s.TricksUsed++;
            if(s.Trick==TrickType.SahteEposta)
            {
                // All seats see the same apparent broadcast, including the actor. Nothing identifies the forger.
                int mail=Deliver(MessageKind.Email,a.Target,-1,text,a.A,a.B,-1,time);s.Emails++;s.NextMessage=time+c.MessageMinInterval;
                AddRecord(RecordKind.SpoofedMail,RecordSource.ITLog,time,id,a.Target,mail,taskNumber:number,proof:inc.View.Id);
                MessageTelemetry(id,MessageKind.Email,-1,text,time,8|(a.A>=0?2:0));
            }
            else
            {
                AddRecord(RecordKind.TrickLog,RecordSource.ITLog,time,id,target?a.Target:-1,(int)s.Trick,taskNumber:number,proof:inc.View.Id);
                if(s.Trick==TrickType.InternetKes){foreach(int p in ids)if(p!=id)Hit(p,s.Trick,time);Emit(LineEventType.InternetDown,time);}
                else if(s.Trick==TrickType.HatDinle){s.TapTarget=a.Target;s.TapUntil=time+c.WiretapSeconds;Emit(LineEventType.Wiretap,time,target:a.Target,a:1,recipient:id);}
                else Hit(a.Target,s.Trick,time);
            }
            Emit(LineEventType.TrickUsed,time,target:target?a.Target:-1,a:(int)s.Trick,recipient:id);return LineFailure.None;
        }
    }
}
