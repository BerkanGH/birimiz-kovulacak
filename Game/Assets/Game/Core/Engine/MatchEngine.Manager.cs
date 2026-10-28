using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    public sealed partial class MatchEngine
    {
        private int noteSerial, evidenceSerial, observationSerial, managerRouteIndex;
        private double managerDeadline = double.PositiveInfinity, managerSampleAt = double.PositiveInfinity;
        private double managerSegmentStart;
        private double surpriseAt = double.PositiveInfinity, surpriseEnds = double.PositiveInfinity;
        private readonly List<ManagerStop> inspectionRoute = new List<ManagerStop>();
        private readonly List<int> inspectionTipDesks = new List<int>();
        private Vec3 managerSegmentFrom, managerSegmentTo;
        private readonly double[] movingSince = new double[Protocol.MaxPlayers];
        private readonly bool[] movementObserved = new bool[Protocol.MaxPlayers];
        private struct CarryObservation { internal int Player; internal ItemState Item; }
        private readonly List<CarryObservation> observedCarry = new List<CarryObservation>();
        private readonly List<InteractionState> observedInteractions = new List<InteractionState>();
        private readonly List<string> inspectedDesks = new List<string>();

        partial void ManagerPhaseChanged(MatchPhase previous)
        {
            manager.InspectingDesk = -1;
            manager.ReactionObservation = -1;
            manager.ReactionUntil = 0;
            manager.ArrivesAt = 0;
            manager.Surprise = false;
            surpriseAt = surpriseEnds = managerDeadline = managerSampleAt = double.PositiveInfinity;
            if (Phase == MatchPhase.Raid || Phase == MatchPhase.FinalInspection)
                BeginManagerInspection(false);
            else if (Phase >= MatchPhase.MeetingDiscussion && Phase != MatchPhase.Abandoned)
            {
                manager.State = ManagerState.InMeeting; manager.Position = Layout.ManagerMeetingSpot; manager.Yaw = Layout.ManagerMeetingYaw;
            }
            else
            {
                ManagerHome();
                if (Phase == MatchPhase.Work2 && managerProfile != null && managerProfile.SurpriseInspection)
                    surpriseAt = phaseStart + Math.Max(0, Math.Min(1, Config.Manager.SurpriseAtFraction)) * Math.Max(0, Config.Durations.Work2);
            }
        }

        private void BeginManagerInspection(bool surprise)
        {
            manager.Position = Layout.ManagerHome; manager.Yaw = Layout.ManagerHomeYaw;
            manager.Surprise = surprise;
            managerRouteIndex = 0;
            inspectionRoute.Clear(); inspectionRoute.AddRange(Layout.ManagerRoute);
            inspectionTipDesks.Clear();
            observedCarry.Clear(); observedInteractions.Clear();
            foreach (int p in playerIds) { movingSince[p] = -1; movementObserved[p] = false; }
            if (surprise)
            {
                manager.ArrivesAt = clock;
                surpriseEnds = Math.Min(phaseEnd, clock + Math.Max(0, Config.Manager.SurpriseSeconds));
                Emit(EventType.SurpriseInspection);
                PrepareCallInspectionRoute();
                StartManagerLeg();
            }
            else
            {
                manager.State = ManagerState.Warning;
                manager.ArrivesAt = phaseStart + Math.Max(0, Phase == MatchPhase.Raid ? Config.Durations.RaidWarning : Config.Durations.FinalWarning);
                managerDeadline = manager.ArrivesAt;
                Emit(EventType.ManagerWarning);
            }
        }

        private void ManagerHome()
        {
            manager.Position = Layout.ManagerHome; manager.Yaw = Layout.ManagerHomeYaw;
            manager.State = ManagerState.InOffice; manager.InspectingDesk = -1; manager.Surprise = false;
            managerDeadline = managerSampleAt = surpriseEnds = double.PositiveInfinity;
        }

        partial void NextManagerTime(ref double next)
        {
            if (Phase == MatchPhase.Work2) next = Math.Min(next, Math.Max(clock, Math.Min(surpriseAt, surpriseEnds)));
            if (Phase != MatchPhase.Raid && Phase != MatchPhase.FinalInspection && !manager.Surprise) return;
            next = Math.Min(next, Math.Max(clock, Math.Min(managerDeadline, managerSampleAt)));
        }

        partial void AdvanceManager(double previous)
        {
            if (Phase == MatchPhase.Work2 && clock >= surpriseAt)
            {
                surpriseAt = double.PositiveInfinity;
                BeginManagerInspection(true);
            }
            if (manager.Surprise && clock >= surpriseEnds) { ManagerHome(); Changed(); return; }
            if (Phase != MatchPhase.Raid && Phase != MatchPhase.FinalInspection && !manager.Surprise) return;
            if (manager.State == ManagerState.Walking || manager.State == ManagerState.Returning)
            {
                double duration = managerDeadline - managerSegmentStart;
                float t = duration <= 0 ? 1 : (float)Math.Min(1, (clock - managerSegmentStart) / duration);
                Vec3 position = managerSegmentFrom + (managerSegmentTo - managerSegmentFrom) * t;
                if (!manager.Position.Equals(position)) { manager.Position = position; Changed(); }
            }
            int guard = 0;
            while (clock >= managerDeadline && guard++ < inspectionRoute.Count * 2 + 4)
            {
                if (manager.State == ManagerState.Warning) { PrepareCallInspectionRoute(); StartManagerLeg(); }
                else if (manager.State == ManagerState.Walking)
                {
                    ManagerStop stop = inspectionRoute[managerRouteIndex];
                    manager.Position = stop.Position; manager.Yaw = stop.Yaw;
                    if (stop.DeskIndex >= 0 && !Present(OwnerOfDesk(stop.DeskIndex))) { managerRouteIndex++; StartManagerLeg(); continue; }
                    manager.InspectingDesk = stop.DeskIndex;
                    manager.State = ManagerState.Inspecting;
                    managerDeadline = clock + Math.Max(0, stop.DwellSeconds);
                    if (stop.DeskIndex >= 0) InspectDesk(stop.DeskIndex);
                    if (stop.DwellSeconds <= 0) { managerRouteIndex++; StartManagerLeg(); }
                }
                else if (manager.State == ManagerState.Inspecting) { managerRouteIndex++; StartManagerLeg(); }
                else if (manager.State == ManagerState.Returning) ManagerHome();
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
            managerSegmentTo = managerRouteIndex < inspectionRoute.Count ? inspectionRoute[managerRouteIndex].Position : Layout.ManagerHome;
            manager.State = managerRouteIndex < inspectionRoute.Count ? ManagerState.Walking : ManagerState.Returning;
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
            return InVision(manager.Position, manager.Yaw, target, ManagerRangeV2(), Config.Manager.VisionHalfAngle, true);
        }

        private void ObservePlayers()
        {
            foreach (int p in playerIds)
            {
                PlayerState player = players[p];
                if (!player.Public.Connected || !ManagerSees(player.Avatar.Position)) { movingSince[p] = -1; continue; }
                ObservePersonalCall(p);
                if (player.Avatar.Speed > Config.Manager.MovingSpeedThreshold)
                {
                    if (movingSince[p] < 0) movingSince[p] = clock;
                    if (!movementObserved[p] && clock - movingSince[p] >= Config.Manager.MovingNoticeSeconds)
                    { movementObserved[p] = true; AddObservation(ObservationKind.MovingDuringInspection, p, -1, Config.Risk.MovingDuringInspection); }
                }
                else movingSince[p] = -1;
                ItemState held = Held(p);
                if (held != null && held.View.Kind == ItemKind.Suspicious)
                {
                    bool seen = false;
                    foreach (CarryObservation carry in observedCarry) if (carry.Player == p && carry.Item == held) { seen = true; break; }
                    if (!seen)
                    { observedCarry.Add(new CarryObservation { Player = p, Item = held }); AddObservation(ObservationKind.CarryingSuspicious, p, -1, Config.Risk.CarryingSuspicious, held.View.Variant); }
                }
                InteractionState active = player.Interaction;
                if (active != null && active.Sabotage.HasValue && !observedInteractions.Contains(active))
                { observedInteractions.Add(active); AddObservation(ObservationKind.CaughtInAct, p, active.DeskIndex, Config.Risk.CaughtInAct, active.Activity.ToString()); }
            }
        }

        private void InspectDesk(int index)
        {
            DeskState desk = Desk(index);
            if (desk == null || !Present(desk.View.AssignedOwner) || !ManagerSees(desk.Def.Position)) return;
            inspectedDesks.Add(((int)Phase) + ":" + index);
            if (Phase == MatchPhase.FinalInspection) RefreshEvidenceStatus(index);
            int owner = ApparentOwner(index);
            foreach (ItemState item in items)
            {
                if (item.View.Location != ItemLocationKind.Slot) continue;
                bool drawer = item.View.SlotId == desk.Def.DrawerSlotId;
                if (!drawer && item.View.SlotId != desk.Def.SurfaceSlotId) continue;
                if (drawer && Phase != MatchPhase.FinalInspection && !(managerProfile != null && managerProfile.SearchAllDrawers) && !inspectionTipDesks.Contains(index)) continue;
                bool suspicious = item.View.Kind == ItemKind.Suspicious;
                bool hidden = drawer && item.View.Kind == ItemKind.Report && item.View.ReportOwner != desk.View.AssignedOwner;
                if (!suspicious && !hidden) continue;
                bool covered = evidence.Exists(e => e.View.ItemId == item.View.Id && e.View.DeskIndex == index && e.View.Status == EvidenceStatus.Active);
                if (!covered)
                {
                    EvidenceState moved = evidence.Find(e => e.View.ItemId == item.View.Id && e.View.DeskIndex == index && e.View.Status == EvidenceStatus.Moved && !e.View.Sealed && e.View.LocationId == item.View.SlotId);
                    if (moved != null) { moved.View.Status = EvidenceStatus.Active; moved.View.RiskWeight = EvidenceWeight(moved.View); LinkPhysicalEvidence(moved, item); Emit(EventType.EvidenceUpdated, arg: moved.View.Id); }
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
            ObserveTippedDesk(index);
            if (!Present(owner)) return;
            bool present = Vec3.FlatDistance(players[owner].Avatar.Position, desk.Def.SeatPosition) <= Config.Manager.AtDeskRadius;
            if (!present) AddObservation(ObservationKind.NotAtDesk, owner, index, Config.Risk.NotAtDesk);
            foreach (int p in playerIds) if (p != owner && players[p].Public.Connected && Vec3.FlatDistance(players[p].Avatar.Position, desk.Def.SeatPosition) <= Config.Manager.AtDeskRadius && ManagerSees(players[p].Avatar.Position))
                AddObservation(ObservationKind.AtWrongDesk, p, index, Config.Risk.AtWrongDesk);
            float expected = Phase == MatchPhase.FinalInspection ? Config.Manager.ExpectedPerformanceAtFinal : Config.Manager.ExpectedPerformanceAtRaid;
            if (players[owner].Public.Performance < expected) AddObservation(ObservationKind.BehindOnWork, owner, index, Config.Risk.BehindOnWork);
            PublicActivity activity = players[owner].Public.Activity;
            bool working = activity == PublicActivity.Typing || activity == PublicActivity.Cleaning || activity == PublicActivity.Brewing || activity == PublicActivity.OnPhone;
            if (present && ManagerSees(players[owner].Avatar.Position) && (players[owner].Public.WorkPose || working)) AddObservation(ObservationKind.WorkingHard, owner, index, Config.Risk.WorkingHard);
        }

        private void AddObservation(ObservationKind kind, int subject, int desk, float severity, string detail = "")
        {
            var observation = new ObservationView { Id = ++observationSerial, Kind = kind, Subject = subject, DeskIndex = desk, Time = clock, Phase = Phase, Severity = ObservationSeverity(kind, severity), Detail = detail };
            observations.Add(observation); manager.ReactionObservation = observation.Id; manager.ReactionUntil = clock + Config.Manager.ReactionSeconds;
            Emit(EventType.ManagerObservation, target: desk < 0 ? "" : EntityIds.Desk(desk), arg: observation.Id);
            if (desk >= 0 && subject == ApparentOwner(desk) && kind != ObservationKind.CaughtInAct && kind != ObservationKind.AtWrongDesk) ObserveConsequences(desk, subject, false, false, "");
        }

        private EvidenceState AddEvidence(EvidenceKind kind, ItemState item, string location, int desk, DiscoverySource source, int sourcePlayer = -1, int attribution = -2)
        {
            var record = new EvidenceState { View = new EvidenceView { Id = ++evidenceSerial, Kind = kind, Time = clock, PhaseFound = Phase, ItemId = item == null ? "" : item.View.Id, ItemVariant = item == null ? "" : item.View.Variant, LocationId = location ?? "", DeskIndex = desk, Source = source, SourcePlayer = sourcePlayer, AttributedTo = attribution == -2 ? ApparentOwner(desk) : attribution, Status = EvidenceStatus.Active }, Placer = item == null ? PlayerIds.None : item.LastPlacer, PlacedAt = item == null ? double.NegativeInfinity : item.PlacedAt };
            evidence.Add(record); record.View.RiskWeight = EvidenceWeight(record.View);
            LinkPhysicalEvidence(record, item);
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

        partial void WitnessCompleted(int p, InteractionState action, string heldAfter, int firstIncident)
        {
            SlotDef slot = Layout.GetSlot(action.Request.TargetId);
            if (action.Request.Verb == ActionVerb.PickUp)
            {
                ItemState item = Item(action.Request.TargetId);
                if (item != null) slot = Layout.GetSlot(item.PlacementSlot);
            }
            int desk = slot != null ? slot.DeskIndex : action.DeskIndex;
            bool witnessable = action.Request.Verb == ActionVerb.SpillCoffee || (slot != null && (slot.Kind == SlotKind.NameplateHolder || slot.Kind == SlotKind.HidingSpot)) || (desk >= 0 && OwnerOfDesk(desk) != p);
            witnessable |= action.Request.Verb == ActionVerb.SaltCoffee || action.Request.Verb == ActionVerb.BrewCoffee || action.Request.Verb == ActionVerb.TasteCoffee || (action.Request.Verb >= ActionVerb.CutInternet && action.Request.Verb <= ActionVerb.ResetFuse);
            if (!witnessable) return;
            foreach (int witness in playerIds)
            {
                PlayerState observer = players[witness];
                if (witness == p || !observer.Public.Connected || !InVision(observer.Avatar.Position, observer.Avatar.Yaw, players[p].Avatar.Position, WitnessRangeV2(p), Config.Witness.HalfAngle, Config.Witness.RequireLineOfSight)) continue;
                var note = new PrivateNote { Kind = NoteKind.Witness, Subject = p, Activity = action.Activity, TargetId = action.Request.TargetId, DeskIndex = desk, HeldBefore = action.HeldBefore, HeldAfter = heldAfter ?? "" };
                AddNote(witness, note);
                for (int n = firstIncident; n < incidents.Count; n++) incidents[n].WitnessNoteIds.Add(note.Id);
            }
        }
    }
}
