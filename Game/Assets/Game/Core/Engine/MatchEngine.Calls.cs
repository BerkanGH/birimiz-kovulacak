using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    public sealed partial class MatchEngine
    {
        private readonly List<CallRecordView> calls = new List<CallRecordView>();
        private readonly int[] callRecord = new int[Protocol.MaxPlayers];
        private readonly bool[] personalCallObserved = new bool[Protocol.MaxPlayers];
        private readonly List<int> pendingTips = new List<int>();

        private void InitializeCalls()
        {
            foreach (int p in playerIds)
            {
                callRecord[p] = -1;
                players[p].Private.TipsLeft = features.TipLine ? Math.Max(0, Config.Calls.TipsPerPlayer) : 0;
                if (players[p].Private.Informant != null)
                    players[p].Private.Informant.CallLogsLeft = features.Calls ? Math.Max(0, Config.Informant.CallLogCharges) : 0;
            }
        }

        private bool UsingTipLine(int p)
        {
            return players[p].Interaction != null && players[p].Interaction.Request.Verb == ActionVerb.TipManager;
        }

        private void NextCallTime(ref double next)
        {
            foreach (int p in playerIds)
                if (players[p].Private.Call.State != CallState.None)
                    next = Math.Min(next, Math.Max(clock, players[p].Private.Call.Until));
        }

        private void AdvanceCalls()
        {
            foreach (int p in playerIds)
                if (players[p].Private.Call.State != CallState.None && clock >= players[p].Private.Call.Until)
                    EndCall(p);
        }

        private void CallsPhaseChanged()
        {
            if (Phase >= MatchPhase.MeetingDiscussion || Phase == MatchPhase.Abandoned)
                foreach (int p in playerIds) EndCall(p);
        }

        private void SubmitCalls(int p, ActionRequest request, ref bool handled, ref FailureCode failure, ref double end)
        {
            if (handled || request.Verb < ActionVerb.CallPlayer || request.Verb > ActionVerb.PullCallLogs) return;
            handled = true;
            failure = FailureCode.None;
            PrivateView me = players[p].Private;
            if (request.Verb == ActionVerb.TipManager)
            {
                failure = ValidateTip(p, request.TargetId);
                if (failure != FailureCode.None) return;
                var interaction = new InteractionState
                {
                    Request = new ActionRequest(request.MatchId, request.RequestId, request.Verb, request.TargetId, request.IntParam),
                    Start = clock, End = clock + Math.Max(0, Config.Calls.TipSeconds), StartPosition = players[p].Avatar.Position,
                    Activity = PublicActivity.OnCall, DeskIndex = Layout.GetDesk(request.TargetId).Index
                };
                players[p].Interaction = interaction;
                players[p].Public.WorkPose = false;
                players[p].Public.Activity = PublicActivity.OnCall;
                // A phone user is visible, but the target desk is private until the manager visits it.
                players[p].Public.ActivityTargetId = "";
                players[p].Public.ActivityStart = clock; players[p].Public.ActivityEnd = interaction.End;
                end = interaction.End;
                if (interaction.End <= clock) CompleteTip(p, interaction);
                return;
            }
            if (!features.Calls) { failure = FailureCode.FeatureDisabled; return; }
            if (request.Verb == ActionVerb.PullCallLogs)
            {
                if (me.Role != Role.Informant || me.Informant == null) { failure = FailureCode.NotAllowed; return; }
                if (me.Informant.CallLogsLeft <= 0) { failure = FailureCode.NoCharges; return; }
                var note = new PrivateNote { Kind = NoteKind.CallLog, Time = clock };
                foreach (CallRecordView record in calls) note.Calls.Add(Snapshot.Copy(record));
                me.Informant.CallLogsLeft--;
                AddPrivateNote(p, note);
                return;
            }
            CallView call = me.Call;
            if (request.Verb == ActionVerb.CallPlayer)
            {
                if (call.State != CallState.None || UsingTipLine(p)) { failure = FailureCode.InCall; return; }
                int other = request.IntParam;
                if (other == p) { failure = FailureCode.CannotTargetSelf; return; }
                if (!Present(other)) { failure = FailureCode.InvalidTarget; return; }
                if (!players[other].Public.Connected || players[other].Private.Call.State != CallState.None || UsingTipLine(other))
                { failure = FailureCode.Unavailable; return; }
                if (me.CallCooldownUntil > clock) { failure = FailureCode.Cooldown; return; }
                double until = clock + Math.Max(0, Config.Calls.RingSeconds);
                SetCall(p, other, CallState.RingingOut, until);
                SetCall(other, p, CallState.RingingIn, until);
                callRecord[p] = callRecord[other] = AppendCallRecord(new CallRecordView { Caller = p, Callee = other, Start = clock });
                Emit(EventType.IncomingCall, other);
                return;
            }
            if (request.Verb == ActionVerb.AnswerCall)
            {
                if (call.State != CallState.RingingIn) { failure = FailureCode.NoIncomingCall; return; }
                int other = call.Other;
                double until = clock + Math.Max(0, Config.Calls.MaxCallSeconds);
                SetCall(p, other, CallState.Active, until);
                SetCall(other, p, CallState.Active, until);
                calls[callRecord[p]].Answered = true;
                Emit(EventType.CallStarted, other, audience: Audience.Player, recipient: p);
                Emit(EventType.CallStarted, p, audience: Audience.Player, recipient: other);
                return;
            }
            if (request.Verb == ActionVerb.DeclineCall)
            {
                if (call.State != CallState.RingingIn && call.State != CallState.RingingOut)
                { failure = FailureCode.NoIncomingCall; return; }
            }
            else if (call.State != CallState.Active) { failure = FailureCode.NotInCall; return; }
            EndCall(p);
        }

        private void SetCall(int p, int other, CallState state, double until)
        {
            CallView call = players[p].Private.Call;
            call.State = state; call.Other = other; call.Since = clock; call.Until = until;
            players[p].Public.Ringing = state == CallState.RingingIn;
            RefreshCallActivity(p);
        }

        private void EndCall(int p)
        {
            CallView call = players[p].Private.Call;
            if (call.State == CallState.None) return;
            int other = call.Other;
            bool answered = calls[callRecord[p]].Answered;
            calls[callRecord[p]].End = clock;
            ClearCall(p);
            ClearCall(other);
            Emit(EventType.CallEnded, other, arg: answered ? 1 : 0, audience: Audience.Player, recipient: p);
            Emit(EventType.CallEnded, p, arg: answered ? 1 : 0, audience: Audience.Player, recipient: other);
        }

        private void ClearCall(int p)
        {
            CallView call = players[p].Private.Call;
            call.State = CallState.None; call.Other = PlayerIds.None; call.Since = call.Until = 0;
            players[p].Public.Ringing = false;
            players[p].Private.CallCooldownUntil = clock + Math.Max(0, Config.Calls.CooldownSeconds);
            callRecord[p] = -1;
            RefreshCallActivity(p);
        }

        private void RefreshCallActivity(int p)
        {
            if (players[p].Interaction != null) return;
            PlayerPublicView pub = players[p].Public;
            CallView call = players[p].Private.Call;
            bool usingPhone = call.State == CallState.RingingOut || call.State == CallState.Active;
            pub.Activity = usingPhone ? PublicActivity.OnCall : PublicActivity.None;
            pub.ActivityTargetId = "";
            pub.ActivityStart = usingPhone ? call.Since : 0;
            pub.ActivityEnd = usingPhone ? call.Until : 0;
        }

        private FailureCode ValidateTip(int p, string target)
        {
            if (!features.TipLine) return FailureCode.FeatureDisabled;
            DeskDef desk = Layout.GetDesk(target);
            if (desk == null || desk.Id != target || !Present(OwnerOfDesk(desk.Index))) return FailureCode.InvalidTarget;
            if (OwnerOfDesk(desk.Index) == p) return FailureCode.CannotTargetSelf;
            if (players[p].Private.TipsLeft <= 0) return FailureCode.NoCharges;
            if (players[p].Private.Call.State != CallState.None) return FailureCode.InCall;
            return FailureCode.None;
        }

        private void CompleteTip(int p, InteractionState interaction)
        {
            if (!Office || !players[p].Public.Connected || ValidateTip(p, interaction.Request.TargetId) != FailureCode.None)
            { Cancel(p); return; }
            players[p].Interaction = null; ClearActivity(p);
            players[p].Private.TipsLeft--;
            AppendCallRecord(new CallRecordView { Caller = p, Callee = PlayerIds.None, Start = interaction.Start, End = clock,
                Answered = true, Tip = true, TipDesk = interaction.DeskIndex });
            // Final inspection has already begun: this tip is recorded, but cannot redirect that walk.
            if (Phase != MatchPhase.FinalInspection && !pendingTips.Contains(interaction.DeskIndex)) pendingTips.Add(interaction.DeskIndex);
            Emit(EventType.ManagerTipReceived);
            Emit(EventType.InteractionCompleted, p, interaction.Request.TargetId, arg: (int)ActionVerb.TipManager,
                audience: Audience.Player, recipient: p);
        }

        private void PrepareCallInspectionRoute()
        {
            Array.Clear(personalCallObserved, 0, personalCallObserved.Length);
            if (pendingTips.Count == 0) return;
            var original = new List<ManagerStop>(inspectionRoute);
            inspectionRoute.Clear();
            // Preserve the exit from the manager's room, then visit tips in their arrival order.
            int firstDesk = 0;
            while (firstDesk < original.Count && original[firstDesk].DeskIndex < 0)
                inspectionRoute.Add(original[firstDesk++]);
            foreach (int index in pendingTips)
            {
                ManagerStop source = original.Find(s => s.DeskIndex == index);
                if (source == null) continue;
                inspectionTipDesks.Add(index);
                inspectionRoute.Add(new ManagerStop { Position = source.Position, Yaw = source.Yaw, DeskIndex = index,
                    DwellSeconds = source.DwellSeconds + Math.Max(0, Config.Manager.TipDwellBonus) });
            }
            for (int i = firstDesk; i < original.Count; i++)
                if (!inspectionTipDesks.Contains(original[i].DeskIndex)) inspectionRoute.Add(original[i]);
            pendingTips.Clear();
        }

        private void ObservePersonalCall(int p)
        {
            if (players[p].Private.Call.State == CallState.Active && !personalCallObserved[p])
            {
                personalCallObserved[p] = true;
                AddObservation(ObservationKind.PersonalCall, p, -1, Config.Risk.PersonalCall);
            }
        }

        private void ObserveTippedDesk(int index)
        {
            if (!inspectionTipDesks.Contains(index)) return;
            DeskState desk = Desk(index);
            bool incriminating = desk.View.Dirty || desk.View.Spill || desk.View.ClutterCount > 0;
            foreach (ItemState item in items)
            {
                if (item.View.Location != ItemLocationKind.Slot) continue;
                bool drawer = item.View.SlotId == desk.Def.DrawerSlotId;
                bool surface = item.View.SlotId == desk.Def.SurfaceSlotId;
                if ((drawer || surface) && item.View.Kind == ItemKind.Suspicious) incriminating = true;
                if (drawer && item.View.Kind == ItemKind.Report && item.View.ReportOwner != desk.View.AssignedOwner) incriminating = true;
            }
            AddObservation(incriminating ? ObservationKind.TipFound : ObservationKind.TipClean,
                ApparentOwner(index), index, incriminating ? 0 : Config.Risk.TipClean);
        }

        private void FillCalls(List<CallRecordView> target)
        {
            foreach (CallRecordView call in calls) target.Add(Snapshot.Copy(call));
        }

        private int AppendCallRecord(CallRecordView record)
        {
            int at = calls.Count;
            while (at > 0 && calls[at - 1].Start > record.Start) at--;
            calls.Insert(at, record);
            foreach (int p in playerIds) if (callRecord[p] >= at) callRecord[p]++;
            return at;
        }
    }
}
