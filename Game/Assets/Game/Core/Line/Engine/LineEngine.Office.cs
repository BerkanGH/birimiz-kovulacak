using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    internal sealed partial class LineEngine
    {
        private sealed class Call
        {
            internal int Id, Caller, Callee;
            internal double Start, End = -1;
            internal bool Connected;
            internal LineCallRecordView CallerLog, CalleeLog;
        }
        private sealed class Trace
        {
            internal int Actor, Victim, Template;
            internal TrickType Trick;
            internal bool Named;
        }
        private sealed class Tip { internal int Actor, Target; }
        private readonly List<Call> calls = new List<Call>();
        private readonly List<Trace> traces = new List<Trace>();
        private readonly List<Tip> tips = new List<Tip>();
        private int nextCallId = 1, nextMailId = 1;
        private bool TipUnlocked { get { return !firstWeek || Day >= c.TipUnlockDayFirstWeek; } }
        private int TrickAllowance { get { return setup.Length == WeekLength.Short ? c.TricksPerWeekShort : c.TricksPerWeekFull; } }

        private Call FindCall(int callId)
        { for (int i = 0; i < calls.Count; i++) if (calls[i].Id == callId) return calls[i]; return null; }

        private LineFailure PhoneAction(int id, LineAction a, double time)
        {
            if (!Office) return LineFailure.WrongPhase;
            var s = seats[id];
            if (s.DeadUntil > time) return LineFailure.PhoneDead;
            if (a.Verb == LineVerb.Call)
            {
                var target = TargetFailure(id, a.Target); if (target != LineFailure.None) return target;
                if (s.Phone.Status != CallStatus.Idle) return LineFailure.AlreadyInCall;
                var other = seats[a.Target];
                if (other.Phone.Status != CallStatus.Idle || other.DeadUntil > time)
                {
                    Emit(LineEventType.CallBusy, time, target: a.Target, recipient: id);
                    Emit(LineEventType.MissedCall, time, actor: id, recipient: a.Target);
                    return LineFailure.Busy;
                }
                var call = new Call { Id = nextCallId++, Caller = id, Callee = a.Target, Start = time };
                call.CallerLog = new LineCallRecordView { CallId = call.Id, Other = a.Target, Outgoing = true, Start = time };
                call.CalleeLog = new LineCallRecordView { CallId = call.Id, Other = id, Start = time };
                calls.Add(call); s.Log.Add(call.CallerLog); other.Log.Add(call.CalleeLog);
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
                    var call = FindCall(s.Call); call.Connected = true; call.Start = time;
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
                if (s.Phone.Status != CallStatus.Dialing && s.Phone.Status != CallStatus.Connected) return LineFailure.NotInCall;
                EndCall(id, time, CallEndReason.HungUp);
            }
            return LineFailure.None;
        }

        private void EndCall(int id, double time, CallEndReason reason)
        {
            var s = seats[id]; if (s.Call < 0) return;
            var call = FindCall(s.Call); int other = s.Phone.Other;
            call.End = time; call.CallerLog.End = call.CalleeLog.End = time;
            call.CallerLog.EndReason = call.CalleeLog.EndReason = reason;
            s.Phone = new LinePhoneView(); seats[other].Phone = new LinePhoneView(); s.Call = seats[other].Call = -1;
            Emit(LineEventType.CallEnded, time, actor: other, a: (int)reason, recipient: id);
            Emit(LineEventType.CallEnded, time, actor: id, a: (int)reason, recipient: other); Changed();
        }
        private void EndAllCalls(double time, CallEndReason reason) { foreach (int id in ids) EndCall(id, time, reason); }
        public int ConnectedCallId(int player) { return HasSeat(player) && seats[player].Phone.Status == CallStatus.Connected ? seats[player].Phone.CallId : -1; }
        public int CallPartner(int player) { return HasSeat(player) && seats[player].Phone.Status == CallStatus.Connected ? seats[player].Phone.Other : -1; }
        public int TappersOf(int speaker, int[] buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            int count = 0;
            if (!HasSeat(speaker) || !seats[speaker].Connected) return 0;
            int partner = CallPartner(speaker);
            foreach (int id in ids)
            {
                var s = seats[id];
                if (s.Connected && s.TapUntil > now && (s.TapTarget == speaker || s.TapTarget == partner) && count < buffer.Length)
                    buffer[count++] = id;
            }
            return count;
        }

        private void DeliverMail(int recipient, MailKind kind, int from, int template, int about, bool all, double time, int messageId = 0)
        {
            // A broadcast has one id. Per-recipient allocation would reveal which seat was omitted
            // (the hidden fake-mail / internet actor) through the recipient's numeric offset.
            var mail = new LineMailView { Id = messageId == 0 ? nextMailId++ : messageId, Kind = kind, From = from, TemplateId = template, About = about, ToAll = all, Time = time };
            var s = seats[recipient];
            if (s.OfflineUntil > time) s.PendingMail.Add(mail);
            else { s.Inbox.Add(mail); Emit(LineEventType.MailReceived, time, a: mail.Id, recipient: recipient); }
            Changed();
        }
        private bool ValidMail(LineAction a) { return a.A >= 0 && a.A < c.MailTemplates && (a.B == -1 || HasSeat(a.B)); }
        private LineFailure MailAction(int id, LineAction a, double time)
        {
            if (!Office) return LineFailure.WrongPhase;
            var s = seats[id];
            if (s.BlockedUntil > time) return LineFailure.ComputerBlocked;
            if (s.MailsSent >= c.MailsPerDay) return LineFailure.LimitReached;
            if (!ValidMail(a)) return LineFailure.BadParam;
            var target = TargetFailure(id, a.Target, false, true); if (target != LineFailure.None) return target;
            int messageId = nextMailId++;
            foreach (int recipient in ids) if (recipient != id && (a.Target == -1 || recipient == a.Target))
                DeliverMail(recipient, MailKind.Personal, id, a.A, a.B, a.Target == -1, time, messageId);
            s.MailsSent++; return LineFailure.None;
        }

        private static void RefreshBlock(Seat s)
        {
            s.BlockedUntil = Math.Max(s.CoffeeUntil, s.OfflineUntil);
            s.BlockedBy = s.BlockedUntil <= 0 ? TrickType.None : s.CoffeeUntil >= s.OfflineUntil ? TrickType.KahveDok : TrickType.InternetKes;
        }
        private void Hit(int id, TrickType trick, double time, int messageId = 0)
        {
            var s = seats[id];
            if (trick == TrickType.KahveDok) { s.CoffeeUntil = Math.Max(s.CoffeeUntil, time + c.CoffeeSeconds); RefreshBlock(s); }
            else if (trick == TrickType.InternetKes) { s.OfflineUntil = Math.Max(s.OfflineUntil, time + c.InternetSeconds); RefreshBlock(s); }
            else if (trick == TrickType.FisCek) { s.DeadUntil = Math.Max(s.DeadUntil, time + c.PhoneDeadSeconds); EndCall(id, time, CallEndReason.LineCut); }
            Emit(LineEventType.TrickHit, time, a: (int)trick, recipient: id);
            DeliverMail(id, MailKind.System, LineIds.System, (int)trick, -1, false, time, messageId);
        }

        private LineFailure TrickAction(int id, LineAction a, double time)
        {
            if (!Office || (a.Verb == LineVerb.UseTrick && !InRound)) return LineFailure.WrongPhase;
            var s = seats[id];
            if (a.Verb == LineVerb.TipOff)
            {
                if (!TipUnlocked) return LineFailure.NotUnlocked;
                if (s.TipUsed) return LineFailure.AlreadyUsed;
                if (s.TipsUsed >= c.TipsPerWeek) return LineFailure.LimitReached;
                if (s.DeadUntil > time) return LineFailure.PhoneDead;
                var target = TargetFailure(id, a.Target); if (target != LineFailure.None) return target;
                tips.Add(new Tip { Actor = id, Target = a.Target }); s.TipUsed = true; s.TipsUsed++;
                Emit(LineEventType.TipFiled, time, target: a.Target, recipient: id); return LineFailure.None;
            }
            if (!Unlocked) return LineFailure.NotUnlocked;
            if (s.TrickUsed) return LineFailure.AlreadyUsed;
            if (s.TricksUsed >= TrickAllowance) return LineFailure.LimitReached;
            if (s.Trick == TrickType.None || s.Trick == TrickType.KayitSil) return LineFailure.NotAvailable;
            bool needsTarget = s.Trick != TrickType.InternetKes;
            if (needsTarget) { var target = TargetFailure(id, a.Target); if (target != LineFailure.None) return target; }
            if (s.Trick == TrickType.SahteEposta && !ValidMail(a)) return LineFailure.BadParam;
            var trace = new Trace { Actor = id, Victim = needsTarget ? a.Target : -1, Trick = s.Trick,
                Template = s.Trick == TrickType.SahteEposta ? a.A : -1, Named = rng.Unit() < profile.TraceChance };
            traces.Add(trace); s.TrickUsed = true; s.TricksUsed++;
            switch (s.Trick)
            {
                case TrickType.SahteEposta:
                    int fakeId = nextMailId++;
                    foreach (int recipient in ids) if (recipient != id) DeliverMail(recipient, MailKind.Personal, a.Target, a.A, a.B, true, time, fakeId);
                    break;
                case TrickType.KahveDok: case TrickType.FisCek: Hit(a.Target, s.Trick, time); break;
                case TrickType.InternetKes:
                    int noticeId = nextMailId++;
                    foreach (int recipient in ids) if (recipient != id) Hit(recipient, s.Trick, time, noticeId);
                    Emit(LineEventType.InternetDown, time); break;
                case TrickType.HatDinle:
                    s.TapTarget = a.Target; s.TapUntil = time + c.WiretapSeconds;
                    Emit(LineEventType.Wiretap, time, target: a.Target, a: 1, recipient: id); break;
            }
            Emit(LineEventType.TrickUsed, time, target: needsTarget ? a.Target : -1, a: (int)s.Trick, recipient: id);
            return LineFailure.None;
        }
    }
}
