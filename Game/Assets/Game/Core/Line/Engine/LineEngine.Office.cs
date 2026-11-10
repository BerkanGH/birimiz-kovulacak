using System;
using System.Collections.Generic;
using System.Text;

namespace BirimizKovulacak.Core
{
    public static class LineText
    {
        public static string Sanitize(string text, int maxChars, bool allowNewlines, out LineFailure failure)
        {
            failure = LineFailure.None;
            if (maxChars < 0) { failure = LineFailure.BadParam; return null; }
            string normal;
            try { normal = (text ?? "").Replace("\r\n", "\n").Normalize(NormalizationForm.FormC); }
            catch (ArgumentException) { failure = LineFailure.BadParam; return null; }
            var b = new StringBuilder(normal.Length);
            int consecutive = 0;
            foreach (char ch in normal)
            {
                if (ch == '\n' && allowNewlines) { if (consecutive < 3) b.Append(ch); consecutive++; continue; }
                if (char.IsControl(ch)) continue;
                consecutive = 0;
                b.Append(ch == '<' ? '‹' : ch == '>' ? '›' : ch);
            }
            string value = b.ToString().Trim();
            if (value.Length == 0) failure = LineFailure.EmptyText;
            else if (value.Length > maxChars) failure = LineFailure.TooLong;
            return failure == LineFailure.None ? value : null;
        }
    }

    /// <summary>Phone (v4 behaviour), line board, messages, transfers and the recorder.</summary>
    internal sealed partial class LineEngine
    {
        private sealed class Call
        {
            internal int Id, Caller, Callee;
            internal LinePhase Phase;
            internal double Start, End = -1;
            internal bool Connected;
            internal LineCallRecordView CallerLog, CalleeLog;
            /// <summary>Players who recorded this call (secret).</summary>
            internal readonly List<int> RecordedBy = new List<int>();
            internal bool Leaked;
        }

        private readonly List<Call> calls = new List<Call>();
        private int nextCallId = 1, nextMessageId = 1;

        private Call FindCall(int id) { return calls.Find(x => x.Id == id); }

        // ---- phone

        private LineFailure PhoneAction(int id, LineAction a, double time)
        {
            if (!CallsOpen) return LineFailure.WrongPhase;
            var s = seats[id];
            if (a.Verb == LineVerb.Call)
            {
                var f = TargetFailure(id, a.Target);
                if (f != LineFailure.None) return f;
                if (!seats[a.Target].Connected) return LineFailure.Absent;
                if (s.Phone.Status != CallStatus.Idle) return LineFailure.AlreadyInCall;
                var other = seats[a.Target];
                if (other.Phone.Status != CallStatus.Idle)
                {
                    Emit(LineEventType.CallBusy, time, target: a.Target, recipient: id);
                    Emit(LineEventType.MissedCall, time, actor: id, recipient: a.Target);
                    return LineFailure.Busy;
                }
                var call = new Call { Id = nextCallId++, Caller = id, Callee = a.Target, Start = time, Phase = Phase };
                call.CallerLog = new LineCallRecordView { CallId = call.Id, Other = a.Target, Outgoing = true, Start = time };
                call.CalleeLog = new LineCallRecordView { CallId = call.Id, Other = id, Start = time };
                calls.Add(call);
                s.Log.Add(call.CallerLog);
                other.Log.Add(call.CalleeLog);
                s.Call = other.Call = call.Id;
                s.Phone = new LinePhoneView { Status = CallStatus.Dialing, Other = a.Target, Since = time, RingUntil = time + c.Durations.Ring };
                other.Phone = new LinePhoneView { Status = CallStatus.Ringing, Other = id, Since = time, RingUntil = time + c.Durations.Ring };
                Emit(LineEventType.CallIncoming, time, actor: id, recipient: a.Target);
            }
            else if (a.Verb == LineVerb.Answer || a.Verb == LineVerb.Decline)
            {
                if (s.Phone.Status != CallStatus.Ringing) return LineFailure.NotRinging;
                if (a.Verb == LineVerb.Decline) EndCall(id, time, CallEndReason.Declined);
                else
                {
                    var call = FindCall(s.Call);
                    call.Connected = true;
                    call.Start = time;
                    call.CallerLog.Connected = call.CalleeLog.Connected = true;
                    call.CallerLog.Start = call.CalleeLog.Start = time;
                    int other = s.Phone.Other;
                    s.Phone = new LinePhoneView { Status = CallStatus.Connected, Other = other, CallId = call.Id, Since = time };
                    seats[other].Phone = new LinePhoneView { Status = CallStatus.Connected, Other = id, CallId = call.Id, Since = time };
                    Emit(LineEventType.CallConnected, time, call.Caller, call.Callee, call.Id, recipient: id);
                    Emit(LineEventType.CallConnected, time, call.Caller, call.Callee, call.Id, recipient: other);
                }
            }
            else
            {
                if (s.Phone.Status == CallStatus.Idle) return LineFailure.NotInCall;
                EndCall(id, time, CallEndReason.HungUp);
            }
            return LineFailure.None;
        }

        private void EndCall(int id, double time, CallEndReason reason)
        {
            var s = seats[id];
            if (s.Call < 0) return;
            var call = FindCall(s.Call);
            int other = s.Phone.Other;
            call.End = time;
            call.CallerLog.End = call.CalleeLog.End = time;
            call.CallerLog.EndReason = call.CalleeLog.EndReason = reason;
            s.Phone = new LinePhoneView();
            seats[other].Phone = new LinePhoneView();
            s.Call = seats[other].Call = -1;
            Emit(LineEventType.CallEnded, time, other, a: (int)reason, b: call.Id, recipient: id);
            Emit(LineEventType.CallEnded, time, id, a: (int)reason, b: call.Id, recipient: other);
            Changed();
        }

        private void EndAllCalls(double time, CallEndReason reason) { foreach (int id in ids) EndCall(id, time, reason); }

        public int ConnectedCallId(int player) { return HasSeat(player) && seats[player].Phone.Status == CallStatus.Connected ? seats[player].Phone.CallId : -1; }
        public int CallPartner(int player) { return HasSeat(player) && seats[player].Phone.Status == CallStatus.Connected ? seats[player].Phone.Other : -1; }

        public int TappersOf(int speaker, int[] buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            return 0;
        }

        public bool MaySpeak(int player)
        {
            if (!HasSeat(player)) return false;
            if (Phase != LinePhase.FinalDefense) return true;
            return defenderIndex < final.DefenseOrder.Count && final.DefenseOrder[defenderIndex] == player;
        }

        private List<LineBoardCall> LineBoard()
        {
            var board = new List<LineBoardCall>();
            if (!CallsOpen) return board;
            foreach (var call in calls)
            {
                if (call.End >= 0) continue;
                board.Add(new LineBoardCall
                {
                    CallId = call.Id, Caller = call.Caller, Callee = call.Callee, Start = call.Start,
                    Status = call.Connected ? CallStatus.Connected : CallStatus.Ringing,
                });
            }
            return board;
        }

        // ---- messages

        public List<LineMessage> MessagesFor(int player, long afterSeq)
        {
            if (!HasSeat(player)) throw new ArgumentException("Unknown recipient.");
            var list = new List<LineMessage>();
            foreach (var m in seats[player].Messages) if (m.Seq > afterSeq) list.Add(CopyMessage(m));
            return list;
        }

        private static LineMessage CopyMessage(LineMessage m)
        {
            return new LineMessage { Seq = m.Seq, Id = m.Id, Kind = m.Kind, From = m.From, To = m.To, ToAll = m.ToAll, Text = m.Text, Time = m.Time };
        }

        private void Deliver(MessageKind kind, int from, int to, string text, double time)
        {
            var m = new LineMessage { Id = nextMessageId++, Kind = kind, From = from, To = to, ToAll = to == -1, Text = text, Time = time };
            foreach (int recipient in ids)
            {
                if (to != -1 && recipient != to && recipient != from) continue;
                var copy = CopyMessage(m);
                var s = seats[recipient];
                copy.Seq = ++s.MessageSeq;
                s.Messages.Add(copy);
                if (s.Messages.Count > c.MessagesKeptPerPlayer) s.Messages.RemoveAt(0);
                Emit(LineEventType.MessageReceived, time, a: (int)kind, b: (int)copy.Seq, recipient: recipient);
            }
            Changed();
        }

        private LineFailure MessageAction(int id, LineAction a, double time)
        {
            var s = seats[id];
            bool chat = a.Verb == LineVerb.SendMeetingChat;
            if (chat ? !ChatOpen : !TextsOpen) return LineFailure.WrongPhase;
            if (time < s.NextMessage) return LineFailure.RateLimited;
            if (chat)
            {
                if (s.Chats >= c.MeetingChatPerSegment) return LineFailure.LimitReached;
            }
            else
            {
                var f = TargetFailure(id, a.Target);
                if (f != LineFailure.None) return f;
                if (s.Texts >= c.TextsPerSegment) return LineFailure.LimitReached;
            }
            string text = LineText.Sanitize(a.Text, chat ? c.MeetingChatMaxChars : c.TextMaxChars, false, out var fail);
            if (fail != LineFailure.None) return fail;
            Deliver(chat ? MessageKind.MeetingChat : MessageKind.Text, id, chat ? -1 : a.Target, text, time);
            if (chat) s.Chats++; else s.Texts++;
            s.NextMessage = time + c.MessageMinInterval;
            return LineFailure.None;
        }

        // ---- transfers (private to sender and receiver until Results)

        private LineFailure Transfer(int id, LineAction a, double time)
        {
            if (!TransfersOpen) return LineFailure.WrongPhase;
            var s = seats[id];
            // Sealed desk: once candidates are fixed a candidate cannot empty their desk to an ally before the verdict.
            if (s.Candidate) return LineFailure.DeskSealed;
            var f = TargetFailure(id, a.Target);
            if (f != LineFailure.None) return f;
            if (a.A < 1 || a.A > c.TransferMax) return LineFailure.BadParam;
            if (a.A > s.Prim) return LineFailure.InsufficientFunds;
            if (time < s.NextTransfer) return LineFailure.RateLimited;
            AddPrim(id, -a.A, PrimReason.TransferOut, time, a.Target);
            AddPrim(a.Target, a.A, PrimReason.TransferIn, time, id);
            var t = new LineTransferView { From = id, To = a.Target, Amount = a.A, Time = time, Phase = Phase };
            transferLog.Add(t);
            s.Transfers.Add(t);
            seats[a.Target].Transfers.Add(t);
            s.NextTransfer = time + c.TransferMinInterval;
            Emit(LineEventType.TransferReceived, time, actor: id, a: a.A, recipient: a.Target);
            return LineFailure.None;
        }

        // ---- recorder

        private LineFailure Record(int id, double time)
        {
            if (!RecordingsOn) return LineFailure.NotAvailable;
            var s = seats[id];
            if (s.Phone.Status != CallStatus.Connected) return LineFailure.NotInCall;
            var call = FindCall(s.Phone.CallId);
            if (call.RecordedBy.Contains(id)) return LineFailure.AlreadyUsed;
            if (s.RecorderUses <= 0) return LineFailure.LimitReached;
            call.RecordedBy.Add(id);
            s.RecorderUses--;
            s.Recorded++;
            return LineFailure.None;
        }

        private List<LineRecordedCallView> RecordedBy(int id)
        {
            var list = new List<LineRecordedCallView>();
            foreach (var call in calls)
                if (call.RecordedBy.Contains(id))
                    list.Add(new LineRecordedCallView { CallId = call.Id, Other = call.Caller == id ? call.Callee : call.Caller, Start = call.Start, Leaked = call.Leaked });
            return list;
        }
    }
}
