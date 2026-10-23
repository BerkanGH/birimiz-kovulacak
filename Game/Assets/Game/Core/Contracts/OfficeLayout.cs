using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    /// <summary>
    /// Gameplay-relevant office layout: everything the rules need for proximity, containers and the manager route.
    /// <see cref="DefaultOffice.Create"/> is the single source of truth: the Unity scene builder places the 3D
    /// props at exactly these positions, and the rules engine validates against the same numbers.
    /// </summary>
    [Serializable]
    public sealed class OfficeLayout
    {
        public List<DeskDef> Desks = new List<DeskDef>();
        public List<StationDef> Stations = new List<StationDef>();
        public List<SlotDef> Slots = new List<SlotDef>();
        public List<ItemDef> Items = new List<ItemDef>();
        public List<ManagerStop> ManagerRoute = new List<ManagerStop>();
        public Vec3 ManagerHome;
        public float ManagerHomeYaw;
        public Vec3 ManagerMeetingSpot;
        public float ManagerMeetingYaw;
        public List<SeatDef> MeetingSeats = new List<SeatDef>();
        public Vec3 BoundsMin;
        public Vec3 BoundsMax;

        public DeskDef GetDesk(int index) { for (int i = 0; i < Desks.Count; i++) if (Desks[i].Index == index) return Desks[i]; return null; }
        public DeskDef GetDesk(string id) { for (int i = 0; i < Desks.Count; i++) if (Desks[i].Id == id) return Desks[i]; return null; }
        public StationDef GetStation(string id) { for (int i = 0; i < Stations.Count; i++) if (Stations[i].Id == id) return Stations[i]; return null; }
        public SlotDef GetSlot(string id) { for (int i = 0; i < Slots.Count; i++) if (Slots[i].Id == id) return Slots[i]; return null; }
        public ItemDef GetItem(string id) { for (int i = 0; i < Items.Count; i++) if (Items[i].Id == id) return Items[i]; return null; }
    }

    [Serializable]
    public sealed class DeskDef
    {
        public int Index;
        public string Id;
        /// <summary>Centre of the desk top footprint at floor level.</summary>
        public Vec3 Position;
        /// <summary>Direction the seated employee faces (degrees, 0 = +Z).</summary>
        public float Yaw;
        public Vec3 SeatPosition;
        public string SurfaceSlotId;
        public string DrawerSlotId;
        public string NameplateSlotId;
        public string ComputerId;
    }

    [Serializable]
    public sealed class StationDef
    {
        public string Id;
        public StationKind Kind;
        /// <summary>Interaction target position (proximity is measured on the XZ plane).</summary>
        public Vec3 Position;
        /// <summary>Direction a user faces while using the station.</summary>
        public float Yaw;
        /// <summary>For desk computers: owning desk index; otherwise -1.</summary>
        public int DeskIndex = -1;
    }

    [Serializable]
    public sealed class SlotDef
    {
        public string Id;
        public SlotKind Kind;
        public Vec3 Position;
        public float Yaw;
        public int Capacity;
        /// <summary>Closed containers hide their contents from players who have not placed or searched them.</summary>
        public bool Closed;
        public int DeskIndex = -1;
    }

    [Serializable]
    public sealed class ItemDef
    {
        public string Id;
        public ItemKind Kind;
        public string Variant;
        /// <summary>Initial slot. Null for nameplates (placed on the owner's assigned desk at match start).</summary>
        public string InitialSlotId;
        /// <summary>For nameplates: the player whose name is printed on it.</summary>
        public int NameplateOf = PlayerIds.None;
    }

    [Serializable]
    public sealed class ManagerStop
    {
        public Vec3 Position;
        public float Yaw;
        /// <summary>Desk inspected at this stop, or -1 for a pure waypoint.</summary>
        public int DeskIndex = -1;
        public float DwellSeconds;
    }

    [Serializable]
    public sealed class SeatDef
    {
        public Vec3 Position;
        public float Yaw;
    }

    public static class DefaultOffice
    {
        // Room: x in [-10, 10], z in [-7, 7]. Manager office NW (x<-4, z>2.5), meeting room NE (x>3, z>2.5),
        // lounge between them, four desks in two rows in the west half, shared equipment along the east wall.
        public static readonly Vec3[] DeskPositions =
        {
            new Vec3(-6.0f, 0f, -0.5f),
            new Vec3(-2.0f, 0f, -0.5f),
            new Vec3(-6.0f, 0f, -4.3f),
            new Vec3(-2.0f, 0f, -4.3f),
        };

        public static readonly Vec3 SurfaceOffset = new Vec3(0.45f, 0.76f, -0.1f);
        public static readonly Vec3 DrawerOffset = new Vec3(0.55f, 0.45f, -0.3f);
        public static readonly Vec3 NameplateOffset = new Vec3(-0.6f, 0.76f, 0.35f);
        public static readonly Vec3 ComputerOffset = new Vec3(-0.25f, 0.76f, 0.1f);
        public static readonly Vec3 SeatOffset = new Vec3(0f, 0f, -0.85f);

        public static OfficeLayout Create()
        {
            var l = new OfficeLayout();
            l.BoundsMin = new Vec3(-10f, 0f, -7f);
            l.BoundsMax = new Vec3(10f, 3f, 7f);

            for (int i = 0; i < DeskPositions.Length; i++)
            {
                Vec3 p = DeskPositions[i];
                var d = new DeskDef
                {
                    Index = i,
                    Id = EntityIds.Desk(i),
                    Position = p,
                    Yaw = 0f,
                    SeatPosition = p + SeatOffset,
                    SurfaceSlotId = EntityIds.DeskSurface(i),
                    DrawerSlotId = EntityIds.DeskDrawer(i),
                    NameplateSlotId = EntityIds.DeskNameplate(i),
                    ComputerId = EntityIds.DeskComputer(i),
                };
                l.Desks.Add(d);
                l.Slots.Add(new SlotDef { Id = d.SurfaceSlotId, Kind = SlotKind.DeskSurface, Position = p + SurfaceOffset, Capacity = 3, Closed = false, DeskIndex = i });
                l.Slots.Add(new SlotDef { Id = d.DrawerSlotId, Kind = SlotKind.Drawer, Position = p + DrawerOffset, Capacity = 3, Closed = true, DeskIndex = i });
                l.Slots.Add(new SlotDef { Id = d.NameplateSlotId, Kind = SlotKind.NameplateHolder, Position = p + NameplateOffset, Capacity = 1, Closed = false, DeskIndex = i });
                l.Stations.Add(new StationDef { Id = d.ComputerId, Kind = StationKind.Computer, Position = p + ComputerOffset, Yaw = 0f, DeskIndex = i });
            }

            l.Stations.Add(new StationDef { Id = EntityIds.CoffeeMachine, Kind = StationKind.CoffeeMachine, Position = new Vec3(9.55f, 0f, 0.2f), Yaw = 90f });
            l.Stations.Add(new StationDef { Id = EntityIds.Printer, Kind = StationKind.Printer, Position = new Vec3(9.2f, 0f, -3.5f), Yaw = 90f });
            l.Stations.Add(new StationDef { Id = EntityIds.Phone, Kind = StationKind.Phone, Position = new Vec3(2.5f, 0f, -1.5f), Yaw = 0f });
            l.Stations.Add(new StationDef { Id = EntityIds.DeliveryPoint, Kind = StationKind.DeliveryPoint, Position = new Vec3(-3.4f, 0f, 2.0f), Yaw = 0f });
            l.Stations.Add(new StationDef { Id = EntityIds.CleaningCloset, Kind = StationKind.CleaningCloset, Position = new Vec3(-9.3f, 0f, -6.3f), Yaw = 225f });

            l.Slots.Add(new SlotDef { Id = EntityIds.MugShelf, Kind = SlotKind.MugShelf, Position = new Vec3(9.6f, 0.95f, 1.1f), Yaw = 90f, Capacity = 4 });
            l.Slots.Add(new SlotDef { Id = EntityIds.PrinterTray, Kind = SlotKind.PrinterTray, Position = new Vec3(9.15f, 1.0f, -3.5f), Yaw = 90f, Capacity = 3 });
            l.Slots.Add(new SlotDef { Id = EntityIds.DeliveryTray, Kind = SlotKind.DeliveryTray, Position = new Vec3(-3.4f, 0.95f, 2.05f), Yaw = 0f, Capacity = 8 });
            l.Slots.Add(new SlotDef { Id = EntityIds.JunkBin, Kind = SlotKind.JunkBin, Position = new Vec3(3.0f, 0f, -6.4f), Yaw = 180f, Capacity = 8 });
            l.Slots.Add(new SlotDef { Id = EntityIds.LostAndFound, Kind = SlotKind.LostAndFound, Position = new Vec3(0.8f, 0.35f, -6.4f), Yaw = 180f, Capacity = 6 });
            l.Slots.Add(new SlotDef { Id = EntityIds.SprayRack, Kind = SlotKind.SprayRack, Position = new Vec3(-9.3f, 0.9f, -6.3f), Yaw = 225f, Capacity = 2 });
            l.Slots.Add(new SlotDef { Id = EntityIds.HideBehindPrinter, Kind = SlotKind.HidingSpot, Position = new Vec3(9.85f, 0.3f, -3.5f), Yaw = 90f, Capacity = 2, Closed = true });
            l.Slots.Add(new SlotDef { Id = EntityIds.HideInPlant, Kind = SlotKind.HidingSpot, Position = new Vec3(-3.4f, 0.3f, 6.3f), Yaw = 0f, Capacity = 2, Closed = true });
            l.Slots.Add(new SlotDef { Id = EntityIds.HideInCabinet, Kind = SlotKind.HidingSpot, Position = new Vec3(2.3f, 0.6f, 6.4f), Yaw = 0f, Capacity = 2, Closed = true });

            for (int i = 0; i < 4; i++)
                l.Items.Add(new ItemDef { Id = EntityIds.Mug(i), Kind = ItemKind.Mug, Variant = ItemVariants.Mug, InitialSlotId = EntityIds.MugShelf });
            for (int p = 0; p < 4; p++)
                l.Items.Add(new ItemDef { Id = EntityIds.Nameplate(p), Kind = ItemKind.Nameplate, Variant = ItemVariants.Nameplate, InitialSlotId = null, NameplateOf = p });
            string[] suspicious = { ItemVariants.Duck, ItemVariants.Stapler, ItemVariants.RivalCv, ItemVariants.BossMug, ItemVariants.SalaryList };
            for (int i = 0; i < suspicious.Length; i++)
                l.Items.Add(new ItemDef { Id = EntityIds.Suspicious(suspicious[i]), Kind = ItemKind.Suspicious, Variant = suspicious[i], InitialSlotId = EntityIds.LostAndFound });
            for (int i = 0; i < 4; i++)
                l.Items.Add(new ItemDef { Id = EntityIds.Clutter(i), Kind = ItemKind.Clutter, Variant = (i % 2 == 0) ? ItemVariants.ClutterPapers : ItemVariants.ClutterBox, InitialSlotId = EntityIds.JunkBin });
            for (int i = 0; i < 2; i++)
                l.Items.Add(new ItemDef { Id = EntityIds.Spray(i), Kind = ItemKind.Spray, Variant = ItemVariants.Spray, InitialSlotId = EntityIds.SprayRack });

            // Manager: home inside the glass office, then a fixed, readable inspection loop.
            l.ManagerHome = new Vec3(-7.0f, 0f, 5.6f);
            l.ManagerHomeYaw = 180f;
            l.ManagerRoute.Add(new ManagerStop { Position = new Vec3(-5.0f, 0f, 4.0f), Yaw = 180f });
            l.ManagerRoute.Add(new ManagerStop { Position = new Vec3(-5.0f, 0f, 1.6f), Yaw = 180f });
            l.ManagerRoute.Add(new ManagerStop { Position = new Vec3(-6.0f, 0f, 1.1f), Yaw = 180f, DeskIndex = 0, DwellSeconds = 2.5f });
            l.ManagerRoute.Add(new ManagerStop { Position = new Vec3(-2.0f, 0f, 1.1f), Yaw = 180f, DeskIndex = 1, DwellSeconds = 2.5f });
            l.ManagerRoute.Add(new ManagerStop { Position = new Vec3(0.6f, 0f, 1.1f), Yaw = 180f });
            l.ManagerRoute.Add(new ManagerStop { Position = new Vec3(0.6f, 0f, -2.7f), Yaw = 270f });
            l.ManagerRoute.Add(new ManagerStop { Position = new Vec3(-2.0f, 0f, -2.7f), Yaw = 180f, DeskIndex = 3, DwellSeconds = 2.5f });
            l.ManagerRoute.Add(new ManagerStop { Position = new Vec3(-6.0f, 0f, -2.7f), Yaw = 180f, DeskIndex = 2, DwellSeconds = 2.5f });
            l.ManagerRoute.Add(new ManagerStop { Position = new Vec3(-8.4f, 0f, -2.7f), Yaw = 0f });
            l.ManagerRoute.Add(new ManagerStop { Position = new Vec3(-8.4f, 0f, 1.1f), Yaw = 90f });
            l.ManagerRoute.Add(new ManagerStop { Position = new Vec3(-5.0f, 0f, 1.6f), Yaw = 0f });
            l.ManagerRoute.Add(new ManagerStop { Position = new Vec3(-5.0f, 0f, 4.0f), Yaw = 0f });

            // Meeting room table centred at (6.5, 4.9); index = player id.
            l.MeetingSeats.Add(new SeatDef { Position = new Vec3(5.7f, 0f, 3.75f), Yaw = 0f });
            l.MeetingSeats.Add(new SeatDef { Position = new Vec3(7.3f, 0f, 3.75f), Yaw = 0f });
            l.MeetingSeats.Add(new SeatDef { Position = new Vec3(5.7f, 0f, 6.05f), Yaw = 180f });
            l.MeetingSeats.Add(new SeatDef { Position = new Vec3(7.3f, 0f, 6.05f), Yaw = 180f });
            l.ManagerMeetingSpot = new Vec3(8.6f, 0f, 4.9f);
            l.ManagerMeetingYaw = 270f;
            return l;
        }
    }
}
