using System;
namespace BirimizKovulacak.Core
{
    public sealed partial class MatchEngine
    {
        partial void ConfigurePlacement(int p, ItemState item, SlotDef slot, InteractionState a, ref double duration)
        {
            int owner = OwnerOfDesk(slot.DeskIndex); a.Victim = owner;
            if (item.View.Kind == ItemKind.Suspicious && slot.Kind == SlotKind.Drawer) duration = Config.GetSabotage(SabotageType.PlantItem).DurationSeconds;
            if (item.View.Kind == ItemKind.Suspicious && (slot.Kind == SlotKind.Drawer || slot.Kind == SlotKind.DeskSurface) && owner != p)
            {
                a.Sabotage = SabotageType.PlantItem;
                if (slot.Kind == SlotKind.Drawer) duration = Config.GetSabotage(SabotageType.PlantItem).DurationSeconds;
            }
            else if (item.View.Kind == ItemKind.Clutter && slot.Kind == SlotKind.DeskSurface && owner != p) { a.Sabotage = SabotageType.MoveClutter; duration = Config.GetSabotage(SabotageType.MoveClutter).DurationSeconds; }
            else if (item.View.Kind == ItemKind.Report && slot.Closed && item.View.ReportOwner != p) { a.Sabotage = SabotageType.HideReport; a.Victim = item.View.ReportOwner; duration = Config.GetSabotage(SabotageType.HideReport).DurationSeconds; }
            // Integration review fix: your own report in another employee's drawer frames them (the manager files it
            // as a hidden report against that desk), so it is a planted item, not a free action.
            else if (item.View.Kind == ItemKind.Report && slot.Kind == SlotKind.Drawer && item.View.ReportOwner == p && Present(owner) && owner != p) { a.Sabotage = SabotageType.PlantItem; duration = Config.GetSabotage(SabotageType.PlantItem).DurationSeconds; }
            else if (item.View.Kind == ItemKind.Nameplate && slot.Kind == SlotKind.NameplateHolder && item.View.NameplateOf != owner) { a.Sabotage = SabotageType.NameplateSwap; duration = Config.GetSabotage(SabotageType.NameplateSwap).DurationSeconds; }
        }
        private bool Effective(IncidentState i)
        {
            if (!i.Active || i.Reveal.ReversedBy != PlayerIds.None) return false;
            DeskState d = Desk(i.Reveal.DeskIndex); ItemState item = Item(i.Reveal.ItemId);
            switch (i.Reveal.Type)
            {
                case SabotageType.LockWorkstation: return d != null && d.View.PcLocked && d.LockIncident == i.Reveal.Id;
                case SabotageType.SpillCoffee: return d != null && d.View.Spill && d.SpillIncident == i.Reveal.Id;
                case SabotageType.PlantItem: return item != null && item.View.Location == ItemLocationKind.Slot && Layout.GetSlot(item.View.SlotId)?.DeskIndex == i.Reveal.DeskIndex;
                case SabotageType.MoveClutter: return item != null && item.View.Location == ItemLocationKind.Slot && item.View.SlotId == d?.Def.SurfaceSlotId;
                case SabotageType.HideReport: return item != null && item.View.Location == ItemLocationKind.Slot && item.View.SlotId == i.LocationId;
                case SabotageType.NameplateSwap: return item != null && item.View.Location == ItemLocationKind.Slot && item.View.SlotId == d?.Def.NameplateSlotId && item.View.NameplateOf != d.View.AssignedOwner;
                default: return EffectiveV2(i);
            }
        }
        partial void CheckSabotage(int p, InteractionState a, ref FailureCode failure)
        {
            if (!a.Sabotage.HasValue) return;
            SabotageType type = a.Sabotage.Value; SabotageDef def = Config.GetSabotage(type);
            failure = CheckCardAndLimitV2(p, type); if (failure != FailureCode.None) return;
            if (players[p].Private.SabotageCooldowns[(int)type] > clock) { failure = FailureCode.Cooldown; return; }
            int deskCount = 0, actorCount = 0, typeCount = 0;
            foreach (IncidentState i in incidents)
            {
                if (i.Reveal.Actor == p && i.Reveal.Victim == a.Victim && i.Reveal.Type == type && clock - i.Reveal.Time < def.SameTargetCooldownSeconds) { failure = FailureCode.TargetImmune; return; }
                if (!Effective(i)) continue;
                if (i.Reveal.Actor == p) actorCount++;
                if (a.DeskIndex >= 0 && i.Reveal.DeskIndex == a.DeskIndex) { deskCount++; if (i.Reveal.Type == type) typeCount++; }
            }
            if (deskCount >= Config.AntiGrief.MaxActiveEffectsPerVictim) { failure = FailureCode.TargetImmune; return; }
            if (actorCount >= Config.AntiGrief.MaxActiveEffectsPerActor) { failure = FailureCode.LimitReached; return; }
            if (a.DeskIndex >= 0 && def.MaxActivePerDesk > 0 && typeCount >= def.MaxActivePerDesk) { failure = FailureCode.LimitReached; return; }
            if (a.DeskIndex >= 0)
            {
                int suspicious = 0, clutter = 0;
                foreach (ItemState item in items) if (item.View.Location == ItemLocationKind.Slot && Layout.GetSlot(item.View.SlotId)?.DeskIndex == a.DeskIndex)
                {
                    if (item.View.Kind == ItemKind.Suspicious) suspicious++;
                    if (item.View.Kind == ItemKind.Clutter && Layout.GetSlot(item.View.SlotId).Kind == SlotKind.DeskSurface) clutter++;
                }
                if ((type == SabotageType.PlantItem && suspicious >= Config.AntiGrief.MaxSuspiciousItemsPerDesk) || (type == SabotageType.MoveClutter && clutter >= Config.AntiGrief.MaxClutterPerDesk)) failure = FailureCode.LimitReached;
            }
        }
        private IncidentState RecordIncident(int actor, SabotageType type, int victim, int desk, string item, string location)
        {
            SabotageDef def = Config.GetSabotage(type);
            var i = new IncidentState { Reveal = new IncidentReveal { Id = incidents.Count + 1, Actor = actor, Type = type, Victim = victim, DeskIndex = desk, ItemId = item ?? "", Time = clock, Misconduct = def.BaseMisconduct }, LocationId = location ?? "", Active = type != SabotageType.EvidenceTampering };
            incidents.Add(i); players[actor].Private.SabotageCooldowns[(int)type] = clock + def.ActorCooldownSeconds; Changed(); return i;
        }
        partial void ApplySabotage(int p, InteractionState a, ItemState item)
        {
            if (!a.Sabotage.HasValue) return;
            IncidentState i = RecordIncident(p, a.Sabotage.Value, a.Victim, a.DeskIndex, item?.View.Id, a.Request.TargetId);
            if (a.Sabotage == SabotageType.SpillCoffee) Desk(a.DeskIndex).SpillIncident = i.Reveal.Id;
            if (a.Sabotage == SabotageType.LockWorkstation) Desk(a.DeskIndex).LockIncident = i.Reveal.Id;
        }
        partial void BeforePickup(int p, ItemState item)
        {
            string location = item.View.SlotId; SlotDef slot = Layout.GetSlot(location); int desk = slot?.DeskIndex ?? -1;
            item.TamperedThisCarry = false;
            foreach (EvidenceState e in evidence)
            {
                if (e.View.ItemId != item.View.Id || e.View.Status != EvidenceStatus.Active || e.View.Sealed || e.View.LocationId != location) continue;
                e.View.Status = EvidenceStatus.Moved; Emit(EventType.EvidenceUpdated, arg: e.View.Id);
                if (!item.TamperedThisCarry) { RecordIncident(p, SabotageType.EvidenceTampering, e.View.AttributedTo, desk, item.View.Id, location); item.TamperedThisCarry = true; }
            }
            bool recovered = false; IncidentState hidden = null;
            // Do not iterate newly appended tampering records as candidates for reversal.
            foreach (IncidentState i in incidents)
            {
                if (!Effective(i) || i.Reveal.ItemId != item.View.Id) continue;
                if (i.Reveal.Type == SabotageType.PlantItem || i.Reveal.Type == SabotageType.MoveClutter || i.Reveal.Type == SabotageType.HideReport)
                {
                    if (i.Reveal.Actor != p) { ReverseEffect(p, i.Reveal.Id); if (i.Reveal.Type == SabotageType.HideReport) { recovered = true; hidden = i; } }
                    else i.Active = false;
                }
            }
            if (recovered) { RecoverReportEvidence(p, item, location, desk); MarkRecoveredReport(hidden, desk); }
        }
        private void MarkRecoveredReport(IncidentState hidden, int desk)
        {
            if (hidden == null) return;
            hidden.Reveal.Discovered = true;
            int blamed = desk >= 0 ? ApparentOwner(desk) : PlayerIds.None;
            if (Present(blamed)) { hidden.Reveal.BlamedPlayer = blamed; if (!hidden.BlamedPlayers.Contains(blamed)) hidden.BlamedPlayers.Add(blamed); }
            if (desk >= 0) ObserveConsequences(desk, blamed, false, false, "");
            Changed();
        }
        partial void AfterPlacement(int p, ItemState item, SlotDef slot, InteractionState a)
        {
            if (slot.Kind == SlotKind.NameplateHolder)
            {
                foreach (IncidentState i in incidents) if (i.Active && i.Reveal.Type == SabotageType.NameplateSwap && i.Reveal.DeskIndex == slot.DeskIndex && i.Reveal.ReversedBy == PlayerIds.None)
                {
                    if (item.View.NameplateOf == OwnerOfDesk(slot.DeskIndex)) ReverseEffect(p, i.Reveal.Id);
                    i.Active = false;
                }
            }
            ApplySabotage(p, a, item);
            if (slot.Kind == SlotKind.JunkBin)
            {
                foreach (EvidenceState e in evidence) if (e.View.ItemId == item.View.Id && !e.View.Sealed && e.View.Status != EvidenceStatus.Disposed)
                {
                    e.View.Status = EvidenceStatus.Disposed; Emit(EventType.EvidenceUpdated, arg: e.View.Id);
                    if (!item.TamperedThisCarry) { RecordIncident(p, SabotageType.EvidenceTampering, e.View.AttributedTo, e.View.DeskIndex, item.View.Id, slot.Id); item.TamperedThisCarry = true; }
                }
            }
        }
        private void Consequence(IncidentState i)
        {
            if (i == null || i.ConsequenceApplied) return;
            i.ConsequenceApplied = true; i.Reveal.Misconduct += Config.GetSabotage(i.Reveal.Type).ConsequenceMisconduct; Changed();
        }
        partial void OwnerBlocked(DeskState desk) { Consequence(incidents.Find(i => i.Reveal.Id == desk.LockIncident)); }
        partial void ReverseEffect(int p, int id)
        {
            IncidentState i = incidents.Find(x => x.Reveal.Id == id); if (i == null) return;
            if (i.Reveal.Actor != p && i.Reveal.ReversedBy == PlayerIds.None) i.Reveal.ReversedBy = p;
            i.Active = false; Changed();
        }
        partial void EndEffect(int id) { IncidentState i = incidents.Find(x => x.Reveal.Id == id); if (i != null) { i.Active = false; Changed(); } }
        partial void ObserveConsequences(int desk, int apparentOwner, bool suspicious, bool messy, string itemId)
        {
            foreach (IncidentState i in incidents)
            {
                if (!Effective(i) || i.Reveal.DeskIndex != desk) continue;
                bool discovered = (suspicious && i.Reveal.ItemId == itemId && (i.Reveal.Type == SabotageType.PlantItem || i.Reveal.Type == SabotageType.HideReport)) || (messy && (i.Reveal.Type == SabotageType.SpillCoffee || i.Reveal.Type == SabotageType.MoveClutter)) || i.Reveal.Type == SabotageType.NameplateSwap;
                if (!discovered) continue;
                i.Reveal.Discovered = true; i.Reveal.BlamedPlayer = apparentOwner;
                if (Present(apparentOwner) && !i.BlamedPlayers.Contains(apparentOwner)) i.BlamedPlayers.Add(apparentOwner);
                if ((i.Reveal.Type == SabotageType.NameplateSwap && apparentOwner != OwnerOfDesk(desk)) || (i.Reveal.Type != SabotageType.NameplateSwap && i.Reveal.Type != SabotageType.HideReport && apparentOwner != i.Reveal.Actor)) Consequence(i);
            }
        }
        partial void FinalizeMisconduct()
        {
            foreach (IncidentState i in incidents) if (i.Reveal.Type == SabotageType.HideReport && Item(i.Reveal.ItemId)?.View.Location != ItemLocationKind.Archived) Consequence(i);
        }
        partial void RecoverReportEvidence(int p, ItemState item, string location, int desk);
        partial void ObserveConsequences(int desk, int apparentOwner, bool suspicious, bool messy, string itemId);
        partial void FinalizeMisconduct();
    }
}
