using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    /// <summary>
    /// Contextual prompts computed entirely from recipient-visible state. Hidden reservations,
    /// provenance and incident limits remain authoritative host checks.
    /// </summary>
    public static class Affordances
    {
        public static List<Affordance> For(PlayerView view, MatchConfig config, OfficeLayout layout, string targetId, Vec3 actorPosition)
        {
            var options = new List<Affordance>();
            if (view == null || config == null || layout == null || view.Me == null || string.IsNullOrEmpty(targetId))
                return options;
            var actor = FindPlayer(view, view.Recipient);
            if (actor == null) return options;
            var held = FindItem(view, actor.CarryingItemId);
            var item = FindItem(view, targetId);
            if (item != null)
            {
                AddPickup(options, view, config, layout, actor, item, actorPosition);
                if (view.Me.Role == Role.Informant &&
                    (item.Kind == ItemKind.Suspicious || item.Kind == ItemKind.Report || item.Kind == ItemKind.Nameplate))
                {
                    FailureCode blocked = view.Me.Informant == null || view.Me.Informant.ScansLeft <= 0
                        ? FailureCode.NoCharges : FailureCode.None;
                    // An owned carried item is at the actor. Other carriers' positions are absent from
                    // PlayerView, so defer that range check to the host instead of using stale floor data.
                    if (blocked == FailureCode.None && item.Location != ItemLocationKind.Carried &&
                        Vec3.FlatDistance(actorPosition, ItemPosition(item, layout)) > config.Informant.ItemScanRange)
                        blocked = FailureCode.TooFar;
                    Add(options, view, actor, ActionVerb.AuditScanItem, item.Id, "audit_scan_item", 0, false, blocked, false);
                }
                return options;
            }

            var slot = layout.GetSlot(targetId);
            if (slot != null)
            {
                AddSlot(options, view, config, layout, actor, held, slot, actorPosition);
                return options;
            }

            var station = layout.GetStation(targetId);
            if (station != null)
            {
                FailureCode busy = StationBusy(view, station.Id);
                switch (station.Kind)
                {
                    case StationKind.CoffeeMachine:
                        FailureCode brew = Equipment(actor, held, ItemKind.Mug);
                        if (brew == FailureCode.None && held.Fill == MugFill.Full) brew = FailureCode.WrongItem;
                        if (brew == FailureCode.None) brew = busy;
                        AddPhysical(options, view, config, actor, ActionVerb.BrewCoffee, station.Id, "brew_coffee",
                            config.Tasks.BrewSeconds, false, brew, actorPosition, station.Position);
                        break;
                    case StationKind.Phone:
                        FailureCode phone = view.Quota == null || !view.Quota.PhoneRinging ? FailureCode.NotRinging : busy;
                        AddPhysical(options, view, config, actor, ActionVerb.AnswerPhone, station.Id, "answer_phone",
                            config.Tasks.AnswerPhoneSeconds, false, phone, actorPosition, station.Position);
                        break;
                    case StationKind.Computer:
                        var desk = FindDesk(view, station.DeskIndex);
                        if (desk == null) break;
                        if (desk.AssignedOwner == actor.Id)
                            AddPhysical(options, view, config, actor, ActionVerb.WriteReport, station.Id, "write_report",
                                config.Tasks.WriteReportSeconds, false, desk.PcLocked ? FailureCode.StationLocked : busy,
                                actorPosition, station.Position);
                        if (desk.PcLocked)
                            AddPhysical(options, view, config, actor, ActionVerb.UnlockWorkstation, station.Id, "unlock_pc",
                                Duration(config, SabotageType.LockWorkstation, true), false, busy, actorPosition, station.Position);
                        if (desk.AssignedOwner != actor.Id)
                        {
                            FailureCode blocked = desk.PcLocked ? FailureCode.AlreadyLocked : FailureCode.None;
                            // PcLockedUntil is the last lock end; an early unlock can only be hinted by the host.
                            if (blocked == FailureCode.None && desk.PcLockedUntil > 0 &&
                                view.ServerTime >= desk.PcLockedUntil &&
                                view.ServerTime < desk.PcLockedUntil + config.AntiGrief.WorkstationImmunitySeconds)
                                blocked = FailureCode.TargetImmune;
                            if (blocked == FailureCode.None) blocked = Cooldown(view, SabotageType.LockWorkstation);
                            if (blocked == FailureCode.None) blocked = busy;
                            AddPhysical(options, view, config, actor, ActionVerb.LockWorkstation, station.Id, "lock_pc",
                                Duration(config, SabotageType.LockWorkstation), true, blocked, actorPosition, station.Position);
                        }
                        break;
                    case StationKind.Printer:
                        ForwardSlot(options, view, config, layout, actor, held, EntityIds.PrinterTray, actorPosition);
                        break;
                    case StationKind.DeliveryPoint:
                        ForwardSlot(options, view, config, layout, actor, held, EntityIds.DeliveryTray, actorPosition);
                        break;
                    case StationKind.CleaningCloset:
                        ForwardSlot(options, view, config, layout, actor, held, EntityIds.SprayRack, actorPosition);
                        break;
                }
                return options;
            }

            var deskDef = layout.GetDesk(targetId);
            if (deskDef == null) return options;
            var deskView = FindDesk(view, deskDef.Index);
            if (deskView == null) return options;
            if (deskView.Dirty || (held != null && held.Kind == ItemKind.Spray))
            {
                FailureCode blocked = Equipment(actor, held, ItemKind.Spray);
                if (blocked == FailureCode.None && !deskView.Dirty) blocked = FailureCode.NothingToClean;
                if (blocked == FailureCode.None) blocked = CleaningBusy(view, targetId);
                AddPhysical(options, view, config, actor, ActionVerb.CleanDesk, targetId, "clean_desk",
                    config.Tasks.CleanDeskSeconds, false, blocked, actorPosition, deskDef.Position);
            }
            if (deskView.Spill || (held != null && held.Kind == ItemKind.Spray))
            {
                FailureCode blocked = Equipment(actor, held, ItemKind.Spray);
                if (blocked == FailureCode.None && !deskView.Spill) blocked = FailureCode.NothingToClean;
                if (blocked == FailureCode.None) blocked = CleaningBusy(view, targetId);
                AddPhysical(options, view, config, actor, ActionVerb.CleanSpill, targetId, "clean_spill",
                    config.Tasks.CleanSpillSeconds, false, blocked, actorPosition, deskDef.Position);
            }
            if (held != null && held.Kind == ItemKind.Mug && held.Fill == MugFill.Full)
            {
                bool sabotage = deskView.AssignedOwner != actor.Id;
                FailureCode blocked = deskView.Spill ? FailureCode.LimitReached :
                    sabotage ? Cooldown(view, SabotageType.SpillCoffee) : FailureCode.None;
                AddPhysical(options, view, config, actor, ActionVerb.SpillCoffee, targetId, "spill_coffee",
                    Duration(config, SabotageType.SpillCoffee), sabotage, blocked, actorPosition, deskDef.Position);
            }
            return options;
        }

        public static List<string> TargetIds(PlayerView view, OfficeLayout layout)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (layout != null)
            {
                for (int i = 0; i < layout.Desks.Count; i++) AddTarget(result, seen, layout.Desks[i].Id);
                for (int i = 0; i < layout.Slots.Count; i++) AddTarget(result, seen, layout.Slots[i].Id);
                for (int i = 0; i < layout.Stations.Count; i++) AddTarget(result, seen, layout.Stations[i].Id);
            }
            // Never enumerate layout.Items: a closed container's initial contents may be secret.
            if (view != null && view.Items != null)
                for (int i = 0; i < view.Items.Count; i++)
                    if (view.Items[i] != null && view.Items[i].Location != ItemLocationKind.Archived)
                        AddTarget(result, seen, view.Items[i].Id);
            return result;
        }

        private static void AddTarget(List<string> result, HashSet<string> seen, string id)
        {
            if (!string.IsNullOrEmpty(id) && seen.Add(id)) result.Add(id);
        }

        private static void ForwardSlot(List<Affordance> options, PlayerView view, MatchConfig config, OfficeLayout layout,
            PlayerPublicView actor, ItemView held, string id, Vec3 position)
        {
            var slot = layout.GetSlot(id);
            if (slot != null) AddSlot(options, view, config, layout, actor, held, slot, position);
        }

        private static void AddSlot(List<Affordance> options, PlayerView view, MatchConfig config, OfficeLayout layout,
            PlayerPublicView actor, ItemView held, SlotDef slot, Vec3 position)
        {
            if (!string.IsNullOrEmpty(actor.CarryingItemId))
                AddPlace(options, view, config, actor, held, slot, position);
            else
            {
                if (view.Items != null)
                    for (int i = 0; i < view.Items.Count; i++)
                    {
                        var item = view.Items[i];
                        if (item != null && item.Location == ItemLocationKind.Slot && item.SlotId == slot.Id)
                            AddPickup(options, view, config, layout, actor, item, position);
                    }
                if (slot.Kind == SlotKind.DeliveryTray && options.Count == 0)
                    AddPhysical(options, view, config, actor, ActionVerb.Place, slot.Id, "deliver",
                        config.Interaction.DeliverSeconds, false, FailureCode.HandsEmpty, position, slot.Position);
            }
            if (slot.Closed)
                AddPhysical(options, view, config, actor, ActionVerb.SearchContainer, slot.Id, "search",
                    config.Interaction.SearchSeconds, false, FailureCode.None, position, slot.Position);
        }

        private static void AddPickup(List<Affordance> options, PlayerView view, MatchConfig config, OfficeLayout layout,
            PlayerPublicView actor, ItemView item, Vec3 position)
        {
            var slot = item.Location == ItemLocationKind.Slot ? layout.GetSlot(item.SlotId) : null;
            float seconds = slot != null && slot.Kind == SlotKind.NameplateHolder ? config.Interaction.PickUpNameplateSeconds :
                slot != null && slot.Closed ? config.Interaction.PickUpContainerSeconds : config.Interaction.PickUpOpenSeconds;
            FailureCode blocked = item.Location == ItemLocationKind.Archived || item.Location == ItemLocationKind.Carried
                ? FailureCode.ItemUnavailable : FailureCode.None;
            if (blocked == FailureCode.None) blocked = ItemBusy(view, item.Id);
            if (blocked == FailureCode.None && item.Sealed) blocked = FailureCode.Sealed;
            if (blocked == FailureCode.None && !string.IsNullOrEmpty(actor.CarryingItemId)) blocked = FailureCode.HandsFull;
            bool tampering = false;
            if (view.Evidence != null)
                for (int i = 0; i < view.Evidence.Count; i++)
                {
                    var evidence = view.Evidence[i];
                    if (evidence != null && evidence.ItemId == item.Id && evidence.Status == EvidenceStatus.Active &&
                        !evidence.Sealed && item.Location == ItemLocationKind.Slot && evidence.LocationId == item.SlotId)
                        tampering = true;
                }
            AddPhysical(options, view, config, actor, ActionVerb.PickUp, item.Id, "take", seconds, tampering,
                blocked, position, ItemPosition(item, layout));
        }

        private static void AddPlace(List<Affordance> options, PlayerView view, MatchConfig config,
            PlayerPublicView actor, ItemView held, SlotDef slot, Vec3 position)
        {
            string label = "return_item";
            float seconds = config.Interaction.PlaceShelfSeconds;
            switch (slot.Kind)
            {
                case SlotKind.DeskSurface: label = "place_surface"; seconds = config.Interaction.PlaceSurfaceSeconds; break;
                case SlotKind.Drawer: label = "place_drawer"; seconds = config.Interaction.PlaceDrawerSeconds; break;
                case SlotKind.NameplateHolder: label = "place_nameplate"; seconds = config.Interaction.PlaceNameplateSeconds; break;
                case SlotKind.HidingSpot: label = "hide"; seconds = config.Interaction.PlaceHidingSpotSeconds; break;
                case SlotKind.DeliveryTray: label = "deliver"; seconds = config.Interaction.DeliverSeconds; break;
                case SlotKind.JunkBin: label = "throw_away"; seconds = config.Interaction.PlaceBinSeconds; break;
            }
            FailureCode blocked = held == null ? FailureCode.WrongItem : FailureCode.None;
            if (slot.Kind == SlotKind.PrinterTray) blocked = FailureCode.InvalidTarget;
            if (held != null && blocked == FailureCode.None)
            {
                if ((slot.Kind == SlotKind.NameplateHolder && held.Kind != ItemKind.Nameplate) ||
                    (slot.Kind == SlotKind.MugShelf && held.Kind != ItemKind.Mug) ||
                    (slot.Kind == SlotKind.SprayRack && held.Kind != ItemKind.Spray) ||
                    (slot.Kind == SlotKind.DeliveryTray && held.Kind != ItemKind.Report &&
                        (held.Kind != ItemKind.Mug || held.Fill != MugFill.Full)))
                    blocked = FailureCode.WrongItem;
                if (blocked == FailureCode.None && slot.Kind == SlotKind.DeliveryTray &&
                    (view.Quota == null || (held.Kind == ItemKind.Report ? view.Quota.ReportOrders : view.Quota.CoffeeOrders) <= 0))
                    blocked = FailureCode.NoDemand;
                if (blocked == FailureCode.None && slot.Kind != SlotKind.NameplateHolder && CountSlot(view, slot.Id) >= slot.Capacity)
                    blocked = FailureCode.SlotFull;
            }
            var desk = FindDesk(view, slot.DeskIndex);
            SabotageType? sabotage = null;
            if (held != null)
            {
                if (held.Kind == ItemKind.Suspicious && slot.Kind == SlotKind.Drawer)
                    seconds = Duration(config, SabotageType.PlantItem);
                if (held.Kind == ItemKind.Suspicious && desk != null && desk.AssignedOwner != actor.Id &&
                    (slot.Kind == SlotKind.Drawer || slot.Kind == SlotKind.DeskSurface))
                    sabotage = SabotageType.PlantItem;
                else if (held.Kind == ItemKind.Clutter && desk != null && desk.AssignedOwner != actor.Id && slot.Kind == SlotKind.DeskSurface)
                { sabotage = SabotageType.MoveClutter; seconds = Duration(config, sabotage.Value); }
                else if (held.Kind == ItemKind.Report && held.ReportOwner != actor.Id && slot.Closed)
                { sabotage = SabotageType.HideReport; seconds = Duration(config, sabotage.Value); }
                else if (held.Kind == ItemKind.Nameplate && desk != null && slot.Kind == SlotKind.NameplateHolder &&
                    held.NameplateOf != desk.AssignedOwner)
                { sabotage = SabotageType.NameplateSwap; seconds = Duration(config, sabotage.Value); }
                if (sabotage.HasValue && blocked == FailureCode.None)
                {
                    blocked = Cooldown(view, sabotage.Value);
                    if (blocked == FailureCode.None && sabotage == SabotageType.MoveClutter && desk.ClutterCount >= config.AntiGrief.MaxClutterPerDesk)
                        blocked = FailureCode.LimitReached;
                    if (blocked == FailureCode.None && sabotage == SabotageType.PlantItem &&
                        VisibleSuspiciousCount(view, slot.DeskIndex) >= config.AntiGrief.MaxSuspiciousItemsPerDesk)
                        blocked = FailureCode.LimitReached;
                }
            }
            // The view omits whether this carry already recorded tampering. Warn for disposal of any
            // unsealed evidence subject; the host decides whether it adds a new incident.
            bool disposalWarning = slot.Kind == SlotKind.JunkBin && held != null && HasDisposableEvidence(view, held.Id);
            AddPhysical(options, view, config, actor, ActionVerb.Place, slot.Id, label, seconds, sabotage.HasValue || disposalWarning,
                blocked, position, slot.Position);
        }

        private static bool HasDisposableEvidence(PlayerView view, string itemId)
        {
            if (view.Evidence != null)
                for (int i = 0; i < view.Evidence.Count; i++)
                {
                    var evidence = view.Evidence[i];
                    if (evidence != null && evidence.ItemId == itemId && !evidence.Sealed && evidence.Status != EvidenceStatus.Disposed)
                        return true;
                }
            return false;
        }

        private static int CountSlot(PlayerView view, string slot)
        {
            int count = 0;
            if (view.Items != null)
                for (int i = 0; i < view.Items.Count; i++)
                    if (view.Items[i] != null && view.Items[i].Location == ItemLocationKind.Slot && view.Items[i].SlotId == slot) count++;
            return count;
        }

        private static int VisibleSuspiciousCount(PlayerView view, int desk)
        {
            int count = 0;
            if (view.Items != null)
                for (int i = 0; i < view.Items.Count; i++)
                {
                    var item = view.Items[i];
                    if (item != null && item.Kind == ItemKind.Suspicious && item.Location == ItemLocationKind.Slot &&
                        EntityIds.DeskIndexOf(item.SlotId) == desk) count++;
                }
            return count;
        }

        private static FailureCode Equipment(PlayerPublicView actor, ItemView held, ItemKind kind)
        {
            if (string.IsNullOrEmpty(actor.CarryingItemId)) return FailureCode.HandsEmpty;
            return held == null || held.Kind != kind ? FailureCode.WrongItem : FailureCode.None;
        }

        private static FailureCode Cooldown(PlayerView view, SabotageType type)
        {
            var times = view.Me.SabotageCooldowns;
            return times != null && (int)type < times.Count && times[(int)type] > view.ServerTime
                ? FailureCode.Cooldown : FailureCode.None;
        }

        private static float Duration(MatchConfig config, SabotageType type, bool reverse = false)
        {
            var definition = config.GetSabotage(type);
            return definition == null ? 0 : reverse ? definition.ReverseSeconds : definition.DurationSeconds;
        }

        private static FailureCode StationBusy(PlayerView view, string id)
        {
            if (view.Stations != null)
                for (int i = 0; i < view.Stations.Count; i++)
                {
                    var station = view.Stations[i];
                    if (station != null && station.Id == id && station.UsedBy != PlayerIds.None && station.BusyUntil > view.ServerTime)
                        return FailureCode.TargetBusy;
                }
            return FailureCode.None;
        }

        private static FailureCode CleaningBusy(PlayerView view, string target)
        {
            for (int i = 0; i < view.Players.Count; i++)
            {
                var player = view.Players[i];
                if (player != null && player.Activity == PublicActivity.Cleaning && player.ActivityTargetId == target &&
                    player.ActivityEnd > view.ServerTime) return FailureCode.TargetBusy;
            }
            return FailureCode.None;
        }

        private static FailureCode ItemBusy(PlayerView view, string id)
        {
            for (int i = 0; i < view.Players.Count; i++)
            {
                var player = view.Players[i];
                if (player != null && player.ActivityTargetId == id && player.ActivityEnd > view.ServerTime)
                    return FailureCode.TargetBusy;
            }
            return FailureCode.None;
        }

        private static void AddPhysical(List<Affordance> options, PlayerView view, MatchConfig config, PlayerPublicView actor,
            ActionVerb verb, string target, string label, float seconds, bool sabotage, FailureCode blocked, Vec3 from, Vec3 to)
        {
            if (blocked == FailureCode.None && Vec3.FlatDistance(from, to) > config.Interaction.Range + config.Interaction.RangeSlack)
                blocked = FailureCode.TooFar;
            Add(options, view, actor, verb, target, label, seconds, sabotage, blocked, true);
        }

        private static void Add(List<Affordance> options, PlayerView view, PlayerPublicView actor,
            ActionVerb verb, string target, string label, float seconds, bool sabotage, FailureCode blocked, bool physical)
        {
            if (!actor.Connected) blocked = FailureCode.NotConnected;
            else if (view.Phase < MatchPhase.Work1 || view.Phase > MatchPhase.FinalInspection) blocked = FailureCode.WrongPhase;
            else if (physical && view.Me.ActiveInteraction != null) blocked = FailureCode.Busy;
            options.Add(new Affordance { Verb = verb, TargetId = target, LabelKey = "verb." + label,
                Seconds = seconds, IsSabotage = sabotage, BlockedBy = blocked });
        }

        private static Vec3 ItemPosition(ItemView item, OfficeLayout layout)
        {
            var slot = item.Location == ItemLocationKind.Slot ? layout.GetSlot(item.SlotId) : null;
            return slot == null ? item.FloorPosition : slot.Position;
        }

        private static PlayerPublicView FindPlayer(PlayerView view, int id)
        {
            if (view.Players != null)
                for (int i = 0; i < view.Players.Count; i++)
                    if (view.Players[i] != null && view.Players[i].Id == id) return view.Players[i];
            return null;
        }

        private static ItemView FindItem(PlayerView view, string id)
        {
            if (view.Items != null && !string.IsNullOrEmpty(id))
                for (int i = 0; i < view.Items.Count; i++)
                    if (view.Items[i] != null && view.Items[i].Id == id) return view.Items[i];
            return null;
        }

        private static DeskView FindDesk(PlayerView view, int index)
        {
            if (view.Desks != null)
                for (int i = 0; i < view.Desks.Count; i++)
                    if (view.Desks[i] != null && view.Desks[i].Index == index) return view.Desks[i];
            return null;
        }
    }
}
