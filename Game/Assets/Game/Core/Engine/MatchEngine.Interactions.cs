using System;
namespace BirimizKovulacak.Core
{
    public sealed partial class MatchEngine
    {
        partial void SubmitPhysical(int p, ActionRequest request, ref bool handled, ref FailureCode failure, ref double end)
        {
            if (handled || !IsPhysical(request.Verb)) return;
            handled = true;
            // Targetless verbs must not echo unchecked client strings into views/events.
            string target = request.Verb == ActionVerb.TasteCoffee || request.Verb == ActionVerb.Drop ? "" : request.TargetId;
            var a = new InteractionState { Request = new ActionRequest(request.MatchId, request.RequestId, request.Verb, target, request.IntParam), Start = clock, StartPosition = players[p].Avatar.Position, HeldItem = players[p].Public.CarryingItemId, HeldBefore = Held(p)?.View.Variant ?? "", PhoneCall = phoneCall };
            double duration; failure = ValidatePhysical(p, a, false, out duration);
            if (failure != FailureCode.None) return;
            a.End = clock + Math.Max(0, duration); players[p].Public.WorkPose = false;
            if (duration <= 0) { ApplyPhysical(p, a); return; }
            players[p].Interaction = a; end = a.End;
            PlayerPublicView pub = players[p].Public; pub.Activity = a.Activity; pub.ActivityTargetId = a.Request.TargetId;
            // Rummaging is public, but the identifier of a still-hidden item is not.
            if (a.Request.Verb == ActionVerb.PickUp && a.Activity == PublicActivity.Rummaging) pub.ActivityTargetId = Item(a.Request.TargetId).View.SlotId;
            pub.ActivityStart = clock; pub.ActivityEnd = end;
        }

        private FailureCode ValidatePhysical(int p, InteractionState a, bool completion, out double duration)
        {
            duration = 0; string id = a.Request.TargetId; ItemState held = Held(p), item = Item(id);
            SlotDef slot = Layout.GetSlot(id); StationDef station = Layout.GetStation(id); DeskDef deskDef = Layout.GetDesk(id);
            DeskState desk = deskDef != null ? Desk(deskDef.Index) : station != null ? Desk(station.DeskIndex) : null;
            Vec3 target = players[p].Avatar.Position; a.Sabotage = null;
            if (completion && a.HeldItem != players[p].Public.CarryingItemId) return FailureCode.WrongItem;
            switch (a.Request.Verb)
            {
                case ActionVerb.PickUp:
                    if (item == null || !Visible(item, p)) return FailureCode.InvalidTarget;
                    if (item.View.Location == ItemLocationKind.Archived || item.View.Location == ItemLocationKind.Carried) return FailureCode.ItemUnavailable;
                    if (Reserved(item.View.Id, p) || ItemPositionReserved(item, p)) return FailureCode.TargetBusy;
                    if (item.View.Sealed) return FailureCode.Sealed;
                    if (held != null) return FailureCode.HandsFull;
                    slot = item.View.Location == ItemLocationKind.Slot ? Layout.GetSlot(item.View.SlotId) : null;
                    a.Reservation = item.View.Id; a.DeskIndex = slot?.DeskIndex ?? -1;
                    a.Activity = slot?.Kind == SlotKind.NameplateHolder ? PublicActivity.Fiddling : slot != null && slot.Closed ? PublicActivity.Rummaging : PublicActivity.PickingUp;
                    duration = slot?.Kind == SlotKind.NameplateHolder ? Config.Interaction.PickUpNameplateSeconds : slot != null && slot.Closed ? Config.Interaction.PickUpContainerSeconds : Config.Interaction.PickUpOpenSeconds;
                    target = ItemPosition(item); break;
                case ActionVerb.Place:
                    if (held == null) return FailureCode.HandsEmpty;
                    if (slot == null) return FailureCode.InvalidTarget;
                    if (held.View.Sealed) return FailureCode.Sealed;
                    if (slot.Kind == SlotKind.PrinterTray || slot.Kind == SlotKind.Mailroom) return FailureCode.InvalidTarget;
                    if (held.View.Kind == ItemKind.Mail && !features.Mail) return FailureCode.FeatureDisabled;
                    if ((slot.Kind == SlotKind.NameplateHolder && held.View.Kind != ItemKind.Nameplate) || (slot.Kind == SlotKind.MugShelf && held.View.Kind != ItemKind.Mug) || (slot.Kind == SlotKind.SprayRack && held.View.Kind != ItemKind.Spray)) return FailureCode.WrongItem;
                    if (slot.Kind == SlotKind.DeliveryTray)
                    {
                        if (held.View.Kind != ItemKind.Report && (held.View.Kind != ItemKind.Mug || held.View.Fill != MugFill.Full)) return FailureCode.WrongItem;
                        if ((held.View.Kind == ItemKind.Report ? quota.ReportOrders : quota.CoffeeOrders) <= 0) return FailureCode.NoDemand;
                    }
                    int position = completion ? a.SlotIndex : FreeReservedIndex(slot.Id, p);
                    if (slot.Kind == SlotKind.NameplateHolder) { position = 0; if (Reserved(slot.Id + ":0", p) || SlotItemsReserved(slot.Id, p)) return FailureCode.TargetBusy; }
                    else if (position >= slot.Capacity || Occupied(slot.Id, position)) return FreeIndex(slot.Id) < slot.Capacity ? FailureCode.TargetBusy : FailureCode.SlotFull;
                    a.SlotIndex = position; a.Reservation = slot.Id + ":" + position; a.DeskIndex = slot.DeskIndex;
                    a.Activity = slot.Kind == SlotKind.NameplateHolder ? PublicActivity.Fiddling : slot.Closed ? PublicActivity.Rummaging : slot.Kind == SlotKind.DeliveryTray ? PublicActivity.Delivering : PublicActivity.Placing;
                    duration = PlaceDuration(slot);
                    ConfigurePlacement(p, held, slot, a, ref duration);
                    FailureCode placeLimit = FailureCode.None; CheckSabotage(p, a, ref placeLimit); if (placeLimit != FailureCode.None) return placeLimit;
                    target = slot.Position; break;
                case ActionVerb.Drop:
                    if (held == null) return FailureCode.HandsEmpty;
                    a.Activity = PublicActivity.Placing; break;
                case ActionVerb.BrewCoffee:
                    if (held == null) return FailureCode.HandsEmpty;
                    if (held.View.Kind != ItemKind.Mug || held.View.Fill == MugFill.Full) return FailureCode.WrongItem;
                    if (station == null || station.Kind != StationKind.CoffeeMachine) return FailureCode.InvalidTarget;
                    a.Reservation = id; a.Activity = PublicActivity.Brewing; duration = Config.Tasks.BrewSeconds; target = station.Position; break;
                case ActionVerb.WriteReport:
                    if (station == null || station.Kind != StationKind.Computer || desk == null) return FailureCode.InvalidTarget;
                    if (desk.View.AssignedOwner != p) return FailureCode.NotOwnDesk;
                    FailureCode reportPc = ValidatePcV2(desk, a.Request.Verb); if (reportPc != FailureCode.None) return reportPc;
                    if (desk.View.PcLocked) { if (!completion) OwnerBlocked(desk); return FailureCode.StationLocked; }
                    a.Reservation = id; a.DeskIndex = desk.View.Index; a.Activity = PublicActivity.Typing; duration = Config.Tasks.WriteReportSeconds; target = station.Position; break;
                case ActionVerb.AnswerPhone:
                    if (!quota.PhoneRinging || (completion && a.PhoneCall != phoneCall)) return FailureCode.NotRinging;
                    if (station == null || station.Kind != StationKind.Phone) return FailureCode.InvalidTarget;
                    a.Reservation = id; a.Activity = PublicActivity.OnPhone; duration = Config.Tasks.AnswerPhoneSeconds; target = station.Position; break;
                case ActionVerb.CleanDesk: case ActionVerb.CleanSpill:
                    if (held == null) return FailureCode.HandsEmpty;
                    if (held.View.Kind != ItemKind.Spray) return FailureCode.WrongItem;
                    if (deskDef == null) return FailureCode.InvalidTarget;
                    if (a.Request.Verb == ActionVerb.CleanDesk ? !desk.View.Dirty : !desk.View.Spill) return FailureCode.NothingToClean;
                    a.Reservation = id + ":clean"; a.DeskIndex = desk.View.Index; a.Activity = PublicActivity.Cleaning;
                    duration = a.Request.Verb == ActionVerb.CleanDesk ? Config.Tasks.CleanDeskSeconds : Config.Tasks.CleanSpillSeconds; target = deskDef.Position; break;
                case ActionVerb.SpillCoffee:
                    if (held == null) return FailureCode.HandsEmpty;
                    if (held.View.Kind != ItemKind.Mug || held.View.Fill != MugFill.Full) return FailureCode.WrongItem;
                    if (deskDef == null) return FailureCode.InvalidTarget;
                    if (desk.View.Spill) return FailureCode.LimitReached;
                    a.Reservation = id + ":spill"; a.DeskIndex = desk.View.Index; a.Activity = PublicActivity.Spilling;
                    if (desk.View.AssignedOwner != p) { a.Sabotage = SabotageType.SpillCoffee; a.Victim = desk.View.AssignedOwner; }
                    duration = Config.GetSabotage(SabotageType.SpillCoffee).DurationSeconds; target = deskDef.Position;
                    FailureCode spillLimit = FailureCode.None; CheckSabotage(p, a, ref spillLimit); if (spillLimit != FailureCode.None) return spillLimit; break;
                case ActionVerb.LockWorkstation: case ActionVerb.UnlockWorkstation:
                    if (station == null || station.Kind != StationKind.Computer || desk == null) return FailureCode.InvalidTarget;
                    FailureCode lockedPc = ValidatePcV2(desk, a.Request.Verb); if (lockedPc != FailureCode.None) return lockedPc;
                    a.DeskIndex = desk.View.Index; a.Reservation = id; a.Activity = PublicActivity.Typing; target = station.Position;
                    if (a.Request.Verb == ActionVerb.LockWorkstation)
                    {
                        if (!Present(desk.View.AssignedOwner)) return FailureCode.InvalidTarget;
                        if (desk.View.AssignedOwner == p) return FailureCode.NotAllowed;
                        if (desk.View.PcLocked) return FailureCode.AlreadyLocked;
                        if (desk.ImmuneUntil > clock) return FailureCode.TargetImmune;
                        a.Sabotage = SabotageType.LockWorkstation; a.Victim = desk.View.AssignedOwner; duration = Config.GetSabotage(SabotageType.LockWorkstation).DurationSeconds;
                        FailureCode lockLimit = FailureCode.None; CheckSabotage(p, a, ref lockLimit); if (lockLimit != FailureCode.None) return lockLimit;
                    }
                    else { if (!desk.View.PcLocked) return FailureCode.NotLocked; duration = Config.GetSabotage(SabotageType.LockWorkstation).ReverseSeconds; }
                    break;
                case ActionVerb.SearchContainer:
                    if (slot == null || !slot.Closed) return FailureCode.InvalidTarget;
                    a.Reservation = id + ":search"; a.DeskIndex = slot.DeskIndex; a.Activity = PublicActivity.Rummaging; duration = Config.Interaction.SearchSeconds; target = slot.Position; break;
                default: return ValidatePhysicalV2(p, a, completion, out duration);
            }
            if (a.Reservation.Length != 0 && Reserved(a.Reservation, p)) return FailureCode.TargetBusy;
            return Vec3.FlatDistance(players[p].Avatar.Position, target) <= Config.Interaction.Range + Config.Interaction.RangeSlack ? FailureCode.None : FailureCode.TooFar;
        }
        private double PlaceDuration(SlotDef slot)
        {
            switch (slot.Kind)
            {
                case SlotKind.Drawer: return Config.Interaction.PlaceDrawerSeconds;
                case SlotKind.NameplateHolder: return Config.Interaction.PlaceNameplateSeconds;
                case SlotKind.HidingSpot: return Config.Interaction.PlaceHidingSpotSeconds;
                case SlotKind.DeliveryTray: return Config.Interaction.DeliverSeconds;
                case SlotKind.JunkBin: return Config.Interaction.PlaceBinSeconds;
                case SlotKind.DeskSurface: return Config.Interaction.PlaceSurfaceSeconds;
                default: return Config.Interaction.PlaceShelfSeconds;
            }
        }
        partial void CompleteInteraction(int p, InteractionState a)
        {
            if (a.Request.Verb == ActionVerb.TipManager) { CompleteTip(p, a); return; }
            double ignored;
            if (!Office || !players[p].Public.Connected || ValidatePhysical(p, a, true, out ignored) != FailureCode.None) { Cancel(p); return; }
            players[p].Interaction = null; ClearActivity(p); ApplyPhysical(p, a);
            Emit(EventType.InteractionCompleted, p, a.Request.TargetId, arg: (int)a.Request.Verb, audience: Audience.Player, recipient: p);
        }
        private void ApplyPhysical(int p, InteractionState a)
        {
            int firstIncident = incidents.Count;
            ItemState held = Held(p); string id = a.Request.TargetId; DeskState desk = Desk(a.DeskIndex); SlotDef slot = Layout.GetSlot(id);
            switch (a.Request.Verb)
            {
                case ActionVerb.PickUp:
                    ItemState take = Item(id); BeforePickup(p, take); Take(p, take); break;
                case ActionVerb.Place:
                    ItemState displaced = slot.Kind == SlotKind.NameplateHolder ? items.Find(i => i.View.Location == ItemLocationKind.Slot && i.View.SlotId == id) : null;
                    Put(p, held, slot, a.SlotIndex);
                    if (displaced != null) { BeforePickup(p, displaced); Take(p, displaced); }
                    if (slot.Kind == SlotKind.DeliveryTray)
                    {
                        if (held.View.Kind == ItemKind.Report) { held.View.Location = ItemLocationKind.Archived; quota.ReportOrders--; AwardTask(p, TaskType.Report, held.View.ReportOwner); }
                        else { held.View.Fill = MugFill.Used; quota.CoffeeOrders--; if (!SaltedDeliveryV2(p, held)) AwardTask(p, TaskType.Coffee); }
                    }
                    DeliverMail(p, held, slot);
                    AfterPlacement(p, held, slot, a); break;
                case ActionVerb.Drop: DropItem(p, true); break;
                case ActionVerb.BrewCoffee: ClearSaltV2(held); held.View.Fill = MugFill.Full; break;
                case ActionVerb.WriteReport: EnqueuePrint(p); OpenScreenV2(p); break;
                case ActionVerb.AnswerPhone: quota.PhoneRinging = false; quota.PhoneRingEnds = 0; nextPhone = clock + PhoneInterval(); AwardTask(p, TaskType.Phone); break;
                case ActionVerb.CleanDesk: desk.View.Dirty = false; AwardTask(p, TaskType.CleanDesk); break;
                case ActionVerb.CleanSpill: desk.View.Spill = false; ReverseEffect(p, desk.SpillIncident); break;
                case ActionVerb.SpillCoffee: desk.View.Spill = true; ClearSaltV2(held); held.View.Fill = MugFill.Used; ApplySabotage(p, a, held); break;
                case ActionVerb.LockWorkstation:
                    CloseScreenV2(desk);
                    desk.View.PcLocked = true; desk.View.PcLockedAt = clock; desk.View.PcLockedUntil = clock + Config.GetSabotage(SabotageType.LockWorkstation).EffectSeconds;
                    ApplySabotage(p, a, null); LockEvidence(desk); Emit(EventType.WorkstationLocked, target: id); break;
                case ActionVerb.UnlockWorkstation: ReverseEffect(p, desk.LockIncident); Unlock(desk); break;
                case ActionVerb.SearchContainer:
                    var note = new PrivateNote { Kind = NoteKind.SearchResult, Time = clock, TargetId = id, DeskIndex = a.DeskIndex };
                    foreach (ItemState i in items) if (i.View.Location == ItemLocationKind.Slot && i.View.SlotId == id) { i.KnownBy[p] = true; note.Contents.Add(i.View.Variant); }
                    AddPrivateNote(p, note); break;
                default: ApplyPhysicalV2(p, a); break;
            }
            // Preserve the actual interaction start, including incidental tampering during pickup/placement.
            for (int n = firstIncident; n < incidents.Count; n++) incidents[n].Start = a.Start;
            RefreshDesks(); WitnessCompleted(p, a, Held(p)?.View.Variant ?? "", firstIncident); Changed();
        }
        private void Take(int p, ItemState item)
        {
            SlotDef origin = Layout.GetSlot(item.View.SlotId);
            if (item.View.Location == ItemLocationKind.Slot && origin?.Kind == SlotKind.NameplateHolder) Desk(origin.DeskIndex).NameplateChangedAt = clock;
            Array.Clear(item.KnownBy, 0, item.KnownBy.Length); item.View.Location = ItemLocationKind.Carried; item.View.SlotId = ""; item.View.CarrierId = p; item.View.SlotIndex = 0;
            item.Handlers.Add(p); item.MovedAt = clock; players[p].Public.CarryingItemId = item.View.Id;
        }
        private void Put(int p, ItemState item, SlotDef slot, int index)
        {
            item.View.Location = ItemLocationKind.Slot; item.View.SlotId = slot.Id; item.View.SlotIndex = index; item.View.CarrierId = PlayerIds.None;
            Array.Clear(item.KnownBy, 0, item.KnownBy.Length); if (slot.Closed) item.KnownBy[p] = true;
            item.LastPlacer = p; item.PlacementIncident = 0; item.PlacedAt = item.MovedAt = clock; item.PlacementSlot = slot.Id; players[p].Public.CarryingItemId = "";
            if (slot.Kind == SlotKind.NameplateHolder) Desk(slot.DeskIndex).NameplateChangedAt = clock;
        }
        partial void DropItem(int p, bool forward)
        {
            ItemState item = Held(p); if (item == null) return;
            item.View.Location = ItemLocationKind.Floor; item.View.SlotId = ""; item.View.CarrierId = PlayerIds.None; item.View.SlotIndex = 0;
            item.View.FloorPosition = players[p].Avatar.Position + Vec3.FromYaw(players[p].Avatar.Yaw) * (forward ? Config.Interaction.DropForwardOffset : 0);
            item.View.FloorYaw = players[p].Avatar.Yaw; item.MovedAt = clock; item.LastPlacer = p; item.PlacementIncident = 0; item.PlacedAt = clock; item.PlacementSlot = "";
            Array.Clear(item.KnownBy, 0, item.KnownBy.Length); players[p].Public.CarryingItemId = "";
            if (!forward) Emit(EventType.ItemDropped, p, item: item.View.Id);
        }
        private bool Reserved(string key, int except) { foreach (int p in playerIds) if (p != except && players[p].Interaction != null && players[p].Interaction.Reservation == key) return true; return false; }
        private bool ItemPositionReserved(ItemState item, int except) { return item.View.Location == ItemLocationKind.Slot && Reserved(item.View.SlotId + ":" + item.View.SlotIndex, except); }
        private bool SlotItemsReserved(string slot, int except) { foreach (ItemState item in items) if (item.View.Location == ItemLocationKind.Slot && item.View.SlotId == slot && Reserved(item.View.Id, except)) return true; return false; }
        private bool Occupied(string slot, int index) { return items.Exists(i => i.View.Location == ItemLocationKind.Slot && i.View.SlotId == slot && i.View.SlotIndex == index); }
        private int FreeReservedIndex(string slot, int p) { int index = 0; while (Occupied(slot, index) || Reserved(slot + ":" + index, p)) index++; return index; }
        private void Unlock(DeskState desk)
        {
            desk.View.PcLocked = false; desk.View.PcLockedUntil = 0; desk.ImmuneUntil = clock + Config.AntiGrief.WorkstationImmunitySeconds;
            desk.View.PcImmuneUntil = desk.ImmuneUntil;
            EndEffect(desk.LockIncident); Emit(EventType.WorkstationUnlocked, target: desk.Def.ComputerId);
        }
        partial void ConfigurePlacement(int p, ItemState item, SlotDef slot, InteractionState a, ref double duration);
        partial void CheckSabotage(int p, InteractionState a, ref FailureCode failure);
        partial void ApplySabotage(int p, InteractionState a, ItemState item);
        partial void BeforePickup(int p, ItemState item);
        partial void AfterPlacement(int p, ItemState item, SlotDef slot, InteractionState a);
        partial void ReverseEffect(int p, int incident);
        partial void EndEffect(int incident);
        partial void OwnerBlocked(DeskState desk);
        partial void AddPrivateNote(int p, PrivateNote note);
        partial void LockEvidence(DeskState desk);
        partial void WitnessCompleted(int p, InteractionState a, string heldAfter, int firstIncident);
    }
}
