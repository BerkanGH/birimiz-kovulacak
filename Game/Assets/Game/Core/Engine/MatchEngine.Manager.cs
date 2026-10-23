using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    public sealed partial class MatchEngine
    {
        private int noteSerial, evidenceSerial, observationSerial, managerRouteIndex;
        private double managerDeadline = double.PositiveInfinity, managerSampleAt = double.PositiveInfinity;
        private double managerSegmentStart;
        private Vec3 managerSegmentFrom, managerSegmentTo;
        private readonly double[] movingSince = { -1, -1, -1, -1 };
        private readonly bool[] movementObserved = new bool[4];
        private readonly List<string> observedCarry = new List<string>();
        private readonly List<InteractionState> observedInteractions = new List<InteractionState>();
        private readonly List<string> inspectedDesks = new List<string>();

        partial void ManagerPhaseChanged(MatchPhase previous)
        {
            manager.InspectingDesk = -1;
            manager.ReactionObservation = -1;
            manager.ReactionUntil = 0;
            managerDeadline = managerSampleAt = double.PositiveInfinity;
            if (Phase == MatchPhase.Raid || Phase == MatchPhase.FinalInspection)
            {
                manager.State = ManagerState.Warning;
                manager.Position = Layout.ManagerHome;
                manager.Yaw = Layout.ManagerHomeYaw;
                manager.ArrivesAt = phaseStart + Math.Max(0, Phase == MatchPhase.Raid ? Config.Durations.RaidWarning : Config.Durations.FinalWarning);
                managerDeadline = manager.ArrivesAt;
                managerRouteIndex = 0;
                observedCarry.Clear(); observedInteractions.Clear();
                for (int p = 0; p < 4; p++) { movingSince[p] = -1; movementObserved[p] = false; }
                Emit(EventType.ManagerWarning);
            }
            else if (Phase >= MatchPhase.MeetingDiscussion && Phase != MatchPhase.Abandoned)
            {
                manager.State = ManagerState.InMeeting; manager.Position = Layout.ManagerMeetingSpot; manager.Yaw = Layout.ManagerMeetingYaw;
            }
            else { manager.State = ManagerState.InOffice; manager.Position = Layout.ManagerHome; manager.Yaw = Layout.ManagerHomeYaw; }
        }

        partial void NextManagerTime(ref double next)
        {
            if (Phase != MatchPhase.Raid && Phase != MatchPhase.FinalInspection) return;
            next = Math.Min(next, Math.Max(clock, Math.Min(managerDeadline, managerSampleAt)));
        }

        partial void AdvanceManager(double previous)
        {
            if (Phase != MatchPhase.Raid && Phase != MatchPhase.FinalInspection) return;
            if (manager.State == ManagerState.Walking || manager.State == ManagerState.Returning)
            {
                double duration = managerDeadline - managerSegmentStart;
                float t = duration <= 0 ? 1 : (float)Math.Min(1, (clock - managerSegmentStart) / duration);
                Vec3 position = managerSegmentFrom + (managerSegmentTo - managerSegmentFrom) * t;
                if (!manager.Position.Equals(position)) { manager.Position = position; Changed(); }
            }
            int guard = 0;
            while (clock >= managerDeadline && guard++ < Layout.ManagerRoute.Count * 2 + 4)
            {
                if (manager.State == ManagerState.Warning) StartManagerLeg();
                else if (manager.State == ManagerState.Walking)
                {
                    ManagerStop stop = Layout.ManagerRoute[managerRouteIndex];
                    manager.Position = stop.Position; manager.Yaw = stop.Yaw;
                    manager.InspectingDesk = stop.DeskIndex;
                    manager.State = ManagerState.Inspecting;
                    managerDeadline = clock + Math.Max(0, stop.DwellSeconds);
                    if (stop.DeskIndex >= 0) InspectDesk(stop.DeskIndex);
                    if (stop.DwellSeconds <= 0) { managerRouteIndex++; StartManagerLeg(); }
                }
                else if (manager.State == ManagerState.Inspecting) { managerRouteIndex++; StartManagerLeg(); }
                else if (manager.State == ManagerState.Returning)
                {
                    manager.Position = Layout.ManagerHome; manager.Yaw = Layout.ManagerHomeYaw;
                    manager.State = ManagerState.InOffice; manager.InspectingDesk = -1;
                    managerDeadline = managerSampleAt = double.PositiveInfinity;
                }
                else break;
                Changed();
            }
            if (manager.State != ManagerState.Warning && manager.State != ManagerState.InOffice)
            {
                ObservePlayers();
                if (clock >= managerSampleAt) managerSampleAt = clock + 0.1;
            }
            if (manager.ReactionObservation >= 0 && clock >= manager.ReactionUntil) { manager.ReactionObservation = -1; Changed(); }
        }

        private void StartManagerLeg()
        {
            manager.InspectingDesk = -1;
            managerSegmentFrom = manager.Position;
            managerSegmentTo = managerRouteIndex < Layout.ManagerRoute.Count ? Layout.ManagerRoute[managerRouteIndex].Position : Layout.ManagerHome;
            manager.State = managerRouteIndex < Layout.ManagerRoute.Count ? ManagerState.Walking : ManagerState.Returning;
            manager.Yaw = Vec3.YawTo(managerSegmentFrom, managerSegmentTo);
            managerSegmentStart = clock;
            managerDeadline = clock + Vec3.FlatDistance(managerSegmentFrom, managerSegmentTo) / Math.Max(0.001f, Config.Manager.WalkSpeed);
            managerSampleAt = Math.Min(managerSampleAt, clock + 0.1);
        }

        private bool InVision(Vec3 from, float yaw, Vec3 target, float range, float halfAngle, bool requireLos)
        {
            float distance = Vec3.FlatDistance(from, target);
            if (distance > range || (distance > 0 && Vec3.YawDelta(yaw, Vec3.YawTo(from, target)) > halfAngle)) return false;
            return !requireLos || world.HasLineOfSight(new Vec3(from.X, from.Y + 1.6f, from.Z), new Vec3(target.X, target.Y + 1f, target.Z));
        }

        private bool ManagerSees(Vec3 target)
        {
            return InVision(manager.Position, manager.Yaw, target, Config.Manager.VisionRange, Config.Manager.VisionHalfAngle, true);
        }

        private void ObservePlayers()
        {
            for (int p = 0; p < 4; p++)
            {
                PlayerState player = players[p];
                if (!player.Public.Connected || !ManagerSees(player.Avatar.Position)) { movingSince[p] = -1; continue; }
                if (player.Avatar.Speed > Config.Manager.MovingSpeedThreshold)
                {
                    if (movingSince[p] < 0) movingSince[p] = clock;
                    if (!movementObserved[p] && clock - movingSince[p] >= Config.Manager.MovingNoticeSeconds)
                    { movementObserved[p] = true; AddObservation(ObservationKind.MovingDuringInspection, p, -1, Config.Risk.MovingDuringInspection); }
                }
                else movingSince[p] = -1;
                ItemState held = Held(p);
                string carryKey = p + ":" + (held == null ? "" : held.View.Id);
                if (held != null && held.View.Kind == ItemKind.Suspicious && !observedCarry.Contains(carryKey))
                { observedCarry.Add(carryKey); AddObservation(ObservationKind.CarryingSuspicious, p, -1, Config.Risk.CarryingSuspicious, held.View.Variant); }
                InteractionState active = player.Interaction;
                if (active != null && active.Sabotage.HasValue && !observedInteractions.Contains(active))
                { observedInteractions.Add(active); AddObservation(ObservationKind.CaughtInAct, p, active.DeskIndex, Config.Risk.CaughtInAct, active.Activity.ToString()); }
            }
        }

        private void InspectDesk(int index)
        {
            DeskState desk = Desk(index);
            if (desk == null || !ManagerSees(desk.Def.Position)) return;
            inspectedDesks.Add(((int)Phase) + ":" + index);
            if (Phase == MatchPhase.FinalInspection) RefreshEvidenceStatus(index);
            int owner = ApparentOwner(index);
            foreach (ItemState item in items)
            {
                if (item.View.Location != ItemLocationKind.Slot) continue;
                bool drawer = item.View.SlotId == desk.Def.DrawerSlotId;
                if (!drawer && item.View.SlotId != desk.Def.SurfaceSlotId) continue;
                bool suspicious = item.View.Kind == ItemKind.Suspicious;
                bool hidden = drawer && item.View.Kind == ItemKind.Report && item.View.ReportOwner != desk.View.AssignedOwner;
                if (!suspicious && !hidden) continue;
                bool covered = evidence.Exists(e => e.View.ItemId == item.View.Id && e.View.DeskIndex == index && e.View.Status == EvidenceStatus.Active);
                if (!covered)
                {
                    EvidenceState moved = evidence.Find(e => e.View.ItemId == item.View.Id && e.View.DeskIndex == index && e.View.Status == EvidenceStatus.Moved && !e.View.Sealed && e.View.LocationId == item.View.SlotId);
                    if (moved != null) { moved.View.Status = EvidenceStatus.Active; moved.View.RiskWeight = EvidenceWeight(moved.View); Emit(EventType.EvidenceUpdated, arg: moved.View.Id); }
                    else AddEvidence(suspicious ? EvidenceKind.SuspiciousItemFound : EvidenceKind.HiddenReportFound, item, item.View.SlotId, index, DiscoverySource.Manager);
                }
                if (suspicious) AddObservation(ObservationKind.SuspiciousItemAtDesk, owner, index, 0, item.View.Variant);
                ObserveConsequences(index, owner, true, false, item.View.Id);
            }
            float mess = (desk.View.Dirty ? Config.Risk.DirtyDesk : 0) + (desk.View.Spill ? Config.Risk.SpillOnDesk : 0) + desk.View.ClutterCount * Config.Risk.ClutterEach;
            if (desk.View.Dirty || desk.View.Spill || desk.View.ClutterCount > 0)
            {
                var detail = new List<string>();
                if (desk.View.Dirty) detail.Add("dirty"); if (desk.View.Spill) detail.Add("spill");
                if (desk.View.ClutterCount > 0) detail.Add("clutter:" + desk.View.ClutterCount);
                AddObservation(ObservationKind.MessyDesk, owner, index, Math.Min(Config.Risk.MessyDeskCap, mess), string.Join(",", detail));
                ObserveConsequences(index, owner, false, true, "");
            }
            if (!PlayerIds.IsValid(owner)) return;
            bool present = Vec3.FlatDistance(players[owner].Avatar.Position, desk.Def.SeatPosition) <= Config.Manager.AtDeskRadius;
            if (!present) AddObservation(ObservationKind.NotAtDesk, owner, index, Config.Risk.NotAtDesk);
            for (int p = 0; p < 4; p++) if (p != owner && players[p].Public.Connected && Vec3.FlatDistance(players[p].Avatar.Position, desk.Def.SeatPosition) <= Config.Manager.AtDeskRadius && ManagerSees(players[p].Avatar.Position))
                AddObservation(ObservationKind.AtWrongDesk, p, index, Config.Risk.AtWrongDesk);
            float expected = Phase == MatchPhase.Raid ? Config.Manager.ExpectedPerformanceAtRaid : Config.Manager.ExpectedPerformanceAtFinal;
            if (players[owner].Public.Performance < expected) AddObservation(ObservationKind.BehindOnWork, owner, index, Config.Risk.BehindOnWork);
            PublicActivity activity = players[owner].Public.Activity;
            bool working = activity == PublicActivity.Typing || activity == PublicActivity.Cleaning || activity == PublicActivity.Brewing || activity == PublicActivity.OnPhone;
            if (present && ManagerSees(players[owner].Avatar.Position) && (players[owner].Public.WorkPose || working)) AddObservation(ObservationKind.WorkingHard, owner, index, Config.Risk.WorkingHard);
        }

        private void AddObservation(ObservationKind kind, int subject, int desk, float severity, string detail = "")
        {
            var observation = new ObservationView { Id = ++observationSerial, Kind = kind, Subject = subject, DeskIndex = desk, Time = clock, Phase = Phase, Severity = severity, Detail = detail };
            observations.Add(observation); manager.ReactionObservation = observation.Id; manager.ReactionUntil = clock + Config.Manager.ReactionSeconds;
            Emit(EventType.ManagerObservation, target: desk < 0 ? "" : EntityIds.Desk(desk), arg: observation.Id);
            if (desk >= 0 && subject == ApparentOwner(desk) && kind != ObservationKind.CaughtInAct && kind != ObservationKind.AtWrongDesk) ObserveConsequences(desk, subject, false, false, "");
        }

        private EvidenceState AddEvidence(EvidenceKind kind, ItemState item, string location, int desk, DiscoverySource source, int sourcePlayer = -1, int attribution = -2)
        {
            var record = new EvidenceState { View = new EvidenceView { Id = ++evidenceSerial, Kind = kind, Time = clock, PhaseFound = Phase, ItemId = item == null ? "" : item.View.Id, ItemVariant = item == null ? "" : item.View.Variant, LocationId = location ?? "", DeskIndex = desk, Source = source, SourcePlayer = sourcePlayer, AttributedTo = attribution == -2 ? ApparentOwner(desk) : attribution, Status = EvidenceStatus.Active }, Placer = item == null ? PlayerIds.None : item.LastPlacer, PlacedAt = item == null ? double.NegativeInfinity : item.PlacedAt };
            evidence.Add(record); record.View.RiskWeight = EvidenceWeight(record.View);
            Emit(EventType.EvidenceCreated, target: location, arg: record.View.Id);
            return record;
        }

        private void AddNote(int p, PrivateNote note)
        {
            note.Id = ++noteSerial; note.Time = clock; players[p].Private.Notes.Add(note);
            Emit(EventType.NoteAdded, arg: note.Id, audience: Audience.Player, recipient: p);
        }

        private void RefreshEvidenceStatus(int desk = -1)
        {
            foreach (EvidenceState record in evidence)
            {
                EvidenceView e = record.View;
                if (e.Sealed || e.Status != EvidenceStatus.Active || e.ItemId.Length == 0 || (desk >= 0 && e.DeskIndex != desk)) continue;
                ItemState item = Item(e.ItemId);
                if (item != null && item.View.Location == ItemLocationKind.Slot && item.View.SlotId == e.LocationId) continue;
                e.Status = EvidenceStatus.Moved; e.RiskWeight = EvidenceWeight(e); Emit(EventType.EvidenceUpdated, arg: e.Id);
            }
        }

        partial void FreezeEvidence() { RefreshEvidenceStatus(); if (Phase == MatchPhase.MeetingDiscussion) FinalizeMisconduct(); }
        partial void AddPrivateNote(int p, PrivateNote note) { AddNote(p, note); }
        partial void LockEvidence(DeskState desk) { AddEvidence(EvidenceKind.WorkstationLockLog, null, desk.Def.ComputerId, desk.View.Index, DiscoverySource.System, attribution: PlayerIds.None); }
        partial void RecoverReportEvidence(int p, ItemState item, string location, int desk)
        {
            EvidenceState recovered = AddEvidence(EvidenceKind.HiddenReportFound, item, location, desk, DiscoverySource.Player, p);
            // Recovery creates the record while the same successful pickup removes its physical subject.
            recovered.View.Status = EvidenceStatus.Moved; recovered.View.RiskWeight = EvidenceWeight(recovered.View);
            Emit(EventType.EvidenceUpdated, arg: recovered.View.Id);
        }

        partial void WitnessCompleted(int p, InteractionState action, string heldAfter)
        {
            SlotDef slot = Layout.GetSlot(action.Request.TargetId);
            if (action.Request.Verb == ActionVerb.PickUp)
            {
                ItemState item = Item(action.Request.TargetId);
                if (item != null) slot = Layout.GetSlot(item.PlacementSlot);
            }
            int desk = slot != null ? slot.DeskIndex : action.DeskIndex;
            bool witnessable = action.Request.Verb == ActionVerb.SpillCoffee || (slot != null && (slot.Kind == SlotKind.NameplateHolder || slot.Kind == SlotKind.HidingSpot)) || (desk >= 0 && OwnerOfDesk(desk) != p);
            if (!witnessable) return;
            for (int witness = 0; witness < 4; witness++)
            {
                PlayerState observer = players[witness];
                if (witness == p || !observer.Public.Connected || !InVision(observer.Avatar.Position, observer.Avatar.Yaw, players[p].Avatar.Position, Config.Witness.Range, Config.Witness.HalfAngle, Config.Witness.RequireLineOfSight)) continue;
                AddNote(witness, new PrivateNote { Kind = NoteKind.Witness, Subject = p, Activity = action.Activity, TargetId = action.Request.TargetId, DeskIndex = desk, HeldBefore = action.HeldBefore, HeldAfter = heldAfter ?? "" });
            }
        }
    }
}
