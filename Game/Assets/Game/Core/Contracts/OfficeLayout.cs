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
        /// <summary>v2: named rooms (UI labels, footage zones, position log).</summary>
        public List<RoomDef> Rooms = new List<RoomDef>();
        /// <summary>v2: security cameras; each records the players inside its zone rectangle.</summary>
        public List<CameraDef> Cameras = new List<CameraDef>();

        public DeskDef GetDesk(int index) { for (int i = 0; i < Desks.Count; i++) if (Desks[i].Index == index) return Desks[i]; return null; }
        public DeskDef GetDesk(string id) { for (int i = 0; i < Desks.Count; i++) if (Desks[i].Id == id) return Desks[i]; return null; }
        public StationDef GetStation(string id) { for (int i = 0; i < Stations.Count; i++) if (Stations[i].Id == id) return Stations[i]; return null; }
        public SlotDef GetSlot(string id) { for (int i = 0; i < Slots.Count; i++) if (Slots[i].Id == id) return Slots[i]; return null; }
        public ItemDef GetItem(string id) { for (int i = 0; i < Items.Count; i++) if (Items[i].Id == id) return Items[i]; return null; }
        public CameraDef GetCamera(string id) { for (int i = 0; i < Cameras.Count; i++) if (Cameras[i].Id == id) return Cameras[i]; return null; }
        public RoomDef GetRoom(string id) { for (int i = 0; i < Rooms.Count; i++) if (Rooms[i].Id == id) return Rooms[i]; return null; }

        /// <summary>First room whose rectangle contains the point (XZ), or null.</summary>
        public RoomDef RoomAt(Vec3 p) { for (int i = 0; i < Rooms.Count; i++) if (Rooms[i].Contains(p)) return Rooms[i]; return null; }
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

    /// <summary>v2: axis-aligned room rectangle on the XZ plane. Loc key "room.&lt;name&gt;" = Id.</summary>
    [Serializable]
    public sealed class RoomDef
    {
        public string Id;
        public Vec3 Min;
        public Vec3 Max;
        public bool Contains(Vec3 p) { return p.X >= Min.X && p.X <= Max.X && p.Z >= Min.Z && p.Z <= Max.Z; }
    }

    /// <summary>v2: a security camera. Footage lists the players whose sampled position lies inside the zone.</summary>
    [Serializable]
    public sealed class CameraDef
    {
        public string Id;
        /// <summary>Mount point (for the 3D prop).</summary>
        public Vec3 Position;
        public float Yaw;
        public Vec3 ZoneMin;
        public Vec3 ZoneMax;
        /// <summary>Room the camera is in (UI label).</summary>
        public string RoomId;
        public bool Covers(Vec3 p) { return p.X >= ZoneMin.X && p.X <= ZoneMax.X && p.Z >= ZoneMin.Z && p.Z <= ZoneMax.Z; }
    }

    public static class DefaultOffice
    {
        // v2 floor plan, x in [-16, 16], z in [-10, 10] (see docs/V2_PLAN.md §5 U1):
        //   north row (z 4..10): manager office | meeting room | kitchen | copy room
        //   middle (z -5..4):    open office with six desks (x -16..6) | reception + mail (x 6..16)
        //   south row (z -10..-5): storage | restroom | server room | lounge
        // Desks: two rows of three; index = default seat of player id (ShuffleDesks = false).
        public static readonly Vec3[] DeskPositions =
        {
            new Vec3(-12.0f, 0f, 1.6f),
            new Vec3(-7.0f, 0f, 1.6f),
            new Vec3(-2.0f, 0f, 1.6f),
            new Vec3(-12.0f, 0f, -2.2f),
            new Vec3(-7.0f, 0f, -2.2f),
            new Vec3(-2.0f, 0f, -2.2f),
        };

        public static readonly Vec3 SurfaceOffset = new Vec3(0.45f, 0.76f, -0.1f);
        public static readonly Vec3 DrawerOffset = new Vec3(0.55f, 0.45f, -0.3f);
        public static readonly Vec3 NameplateOffset = new Vec3(-0.55f, 1.16f, 0.5f);
        public static readonly Vec3 ComputerOffset = new Vec3(-0.25f, 0.76f, 0.1f);
        public static readonly Vec3 SeatOffset = new Vec3(0f, 0f, -0.85f);

        public const int MugCount = 6;
        public const int SprayCount = 3;

        public static OfficeLayout Create()
        {
            var l = new OfficeLayout();
            l.BoundsMin = new Vec3(-16f, 0f, -10f);
            l.BoundsMax = new Vec3(16f, 3f, 10f);

            Room(l, "manager", -16f, 4f, -8f, 10f);
            Room(l, "meeting", -8f, 4f, 2f, 10f);
            Room(l, "kitchen", 2f, 4f, 10f, 10f);
            Room(l, "copy", 10f, 4f, 16f, 10f);
            Room(l, "office", -16f, -5f, 6f, 4f);
            Room(l, "reception", 6f, -5f, 16f, 4f);
            Room(l, "storage", -16f, -10f, -10f, -5f);
            Room(l, "restroom", -10f, -10f, -5f, -5f);
            Room(l, "server", -5f, -10f, 3f, -5f);
            Room(l, "lounge", 3f, -10f, 16f, -5f);

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

            // Shared stations
            l.Stations.Add(new StationDef { Id = EntityIds.CoffeeMachine, Kind = StationKind.CoffeeMachine, Position = new Vec3(5.5f, 0f, 9.3f), Yaw = 0f });
            l.Stations.Add(new StationDef { Id = EntityIds.Printer, Kind = StationKind.Printer, Position = new Vec3(13.0f, 0f, 9.2f), Yaw = 0f });
            l.Stations.Add(new StationDef { Id = EntityIds.Phone, Kind = StationKind.Phone, Position = new Vec3(10.5f, 0f, -0.5f), Yaw = 90f });
            l.Stations.Add(new StationDef { Id = EntityIds.DeliveryPoint, Kind = StationKind.DeliveryPoint, Position = new Vec3(-9.2f, 0f, 6.8f), Yaw = 90f });
            l.Stations.Add(new StationDef { Id = EntityIds.CleaningCloset, Kind = StationKind.CleaningCloset, Position = new Vec3(-15.2f, 0f, -9.2f), Yaw = 225f });
            l.Stations.Add(new StationDef { Id = EntityIds.Router, Kind = StationKind.Router, Position = new Vec3(-1.0f, 0f, -9.3f), Yaw = 180f });
            l.Stations.Add(new StationDef { Id = EntityIds.FuseBox, Kind = StationKind.FuseBox, Position = new Vec3(-12.8f, 0f, -9.5f), Yaw = 180f });

            // Shared slots / containers
            l.Slots.Add(new SlotDef { Id = EntityIds.MugShelf, Kind = SlotKind.MugShelf, Position = new Vec3(7.0f, 0.95f, 9.6f), Yaw = 0f, Capacity = MugCount });
            l.Slots.Add(new SlotDef { Id = EntityIds.PrinterTray, Kind = SlotKind.PrinterTray, Position = new Vec3(13.0f, 1.0f, 9.15f), Yaw = 0f, Capacity = 3 });
            l.Slots.Add(new SlotDef { Id = EntityIds.DeliveryTray, Kind = SlotKind.DeliveryTray, Position = new Vec3(-8.32f, 0.95f, 6.8f), Yaw = 90f, Capacity = 10 });
            l.Slots.Add(new SlotDef { Id = EntityIds.JunkBin, Kind = SlotKind.JunkBin, Position = new Vec3(8.0f, 0f, -9.3f), Yaw = 180f, Capacity = 8 });
            l.Slots.Add(new SlotDef { Id = EntityIds.LostAndFound, Kind = SlotKind.LostAndFound, Position = new Vec3(-10.9f, 0.35f, -9.4f), Yaw = 180f, Capacity = 6 });
            l.Slots.Add(new SlotDef { Id = EntityIds.SprayRack, Kind = SlotKind.SprayRack, Position = new Vec3(-15.2f, 0.9f, -9.2f), Yaw = 225f, Capacity = SprayCount });
            l.Slots.Add(new SlotDef { Id = EntityIds.Mailroom, Kind = SlotKind.Mailroom, Position = new Vec3(15.3f, 0.95f, 1.5f), Yaw = 90f, Capacity = 4 });
            l.Slots.Add(new SlotDef { Id = EntityIds.HideBehindPrinter, Kind = SlotKind.HidingSpot, Position = new Vec3(14.4f, 0.3f, 9.5f), Yaw = 0f, Capacity = 2, Closed = true });
            l.Slots.Add(new SlotDef { Id = EntityIds.HideInPlant, Kind = SlotKind.HidingSpot, Position = new Vec3(12.0f, 0.3f, -9.4f), Yaw = 180f, Capacity = 2, Closed = true });
            l.Slots.Add(new SlotDef { Id = EntityIds.HideInCabinet, Kind = SlotKind.HidingSpot, Position = new Vec3(-7.3f, 0.6f, 9.5f), Yaw = 0f, Capacity = 2, Closed = true });
            l.Slots.Add(new SlotDef { Id = EntityIds.HideInToilet, Kind = SlotKind.HidingSpot, Position = new Vec3(-7.5f, 0.7f, -9.5f), Yaw = 180f, Capacity = 2, Closed = true });

            // Items. Nameplates exist for every possible player; the engine only places those of players in the match.
            for (int i = 0; i < MugCount; i++)
                l.Items.Add(new ItemDef { Id = EntityIds.Mug(i), Kind = ItemKind.Mug, Variant = ItemVariants.Mug, InitialSlotId = EntityIds.MugShelf });
            for (int p = 0; p < Protocol.MaxPlayers; p++)
                l.Items.Add(new ItemDef { Id = EntityIds.Nameplate(p), Kind = ItemKind.Nameplate, Variant = ItemVariants.Nameplate, InitialSlotId = null, NameplateOf = p });
            string[] suspicious = { ItemVariants.Duck, ItemVariants.Stapler, ItemVariants.RivalCv, ItemVariants.BossMug, ItemVariants.SalaryList };
            for (int i = 0; i < suspicious.Length; i++)
                l.Items.Add(new ItemDef { Id = EntityIds.Suspicious(suspicious[i]), Kind = ItemKind.Suspicious, Variant = suspicious[i], InitialSlotId = EntityIds.LostAndFound });
            for (int i = 0; i < 4; i++)
                l.Items.Add(new ItemDef { Id = EntityIds.Clutter(i), Kind = ItemKind.Clutter, Variant = (i % 2 == 0) ? ItemVariants.ClutterPapers : ItemVariants.ClutterBox, InitialSlotId = EntityIds.JunkBin });
            for (int i = 0; i < SprayCount; i++)
                l.Items.Add(new ItemDef { Id = EntityIds.Spray(i), Kind = ItemKind.Spray, Variant = ItemVariants.Spray, InitialSlotId = EntityIds.SprayRack });

            // Manager: home in the glass office (NW), out through its door, along row A (north side), down the east
            // aisle, back along row B (aisle side), up the west side, home. Stops at unassigned desks are walked
            // through without inspection.
            l.ManagerHome = new Vec3(-13.0f, 0f, 8.0f);
            l.ManagerHomeYaw = 180f;
            l.ManagerRoute.Add(new ManagerStop { Position = new Vec3(-10.0f, 0f, 5.0f), Yaw = 180f });
            l.ManagerRoute.Add(new ManagerStop { Position = new Vec3(-10.0f, 0f, 3.2f), Yaw = 270f });
            l.ManagerRoute.Add(new ManagerStop { Position = new Vec3(-12.0f, 0f, 3.2f), Yaw = 180f, DeskIndex = 0, DwellSeconds = 2.5f });
            l.ManagerRoute.Add(new ManagerStop { Position = new Vec3(-7.0f, 0f, 3.2f), Yaw = 180f, DeskIndex = 1, DwellSeconds = 2.5f });
            l.ManagerRoute.Add(new ManagerStop { Position = new Vec3(-2.0f, 0f, 3.2f), Yaw = 180f, DeskIndex = 2, DwellSeconds = 2.5f });
            l.ManagerRoute.Add(new ManagerStop { Position = new Vec3(0.8f, 0f, 3.2f), Yaw = 180f });
            l.ManagerRoute.Add(new ManagerStop { Position = new Vec3(0.8f, 0f, -0.6f), Yaw = 270f });
            l.ManagerRoute.Add(new ManagerStop { Position = new Vec3(-2.0f, 0f, -0.6f), Yaw = 180f, DeskIndex = 5, DwellSeconds = 2.5f });
            l.ManagerRoute.Add(new ManagerStop { Position = new Vec3(-7.0f, 0f, -0.6f), Yaw = 180f, DeskIndex = 4, DwellSeconds = 2.5f });
            l.ManagerRoute.Add(new ManagerStop { Position = new Vec3(-12.0f, 0f, -0.6f), Yaw = 180f, DeskIndex = 3, DwellSeconds = 2.5f });
            l.ManagerRoute.Add(new ManagerStop { Position = new Vec3(-14.6f, 0f, -0.6f), Yaw = 0f });
            l.ManagerRoute.Add(new ManagerStop { Position = new Vec3(-14.6f, 0f, 3.2f), Yaw = 90f });
            l.ManagerRoute.Add(new ManagerStop { Position = new Vec3(-10.0f, 0f, 3.2f), Yaw = 0f });
            l.ManagerRoute.Add(new ManagerStop { Position = new Vec3(-10.0f, 0f, 5.0f), Yaw = 0f });

            // Meeting room table centred at (-3, 7.2): three seats per side; index = player id.
            float[] seatX = { -4.4f, -3.0f, -1.6f };
            for (int i = 0; i < 3; i++) l.MeetingSeats.Add(new SeatDef { Position = new Vec3(seatX[i], 0f, 6.0f), Yaw = 0f });
            for (int i = 0; i < 3; i++) l.MeetingSeats.Add(new SeatDef { Position = new Vec3(seatX[i], 0f, 8.4f), Yaw = 180f });
            l.ManagerMeetingSpot = new Vec3(-6.2f, 0f, 7.2f);
            l.ManagerMeetingYaw = 90f;

            // Cameras: open office (two halves), kitchen, reception, copy room. No camera in the manager office,
            // meeting room, restroom, server room, storage or lounge.
            Cam(l, "office_w", new Vec3(-15.6f, 2.7f, 3.6f), 135f, -16f, -5f, -5f, 4f, "office");
            Cam(l, "office_e", new Vec3(5.6f, 2.7f, 3.6f), 225f, -5f, -5f, 6f, 4f, "office");
            Cam(l, "kitchen", new Vec3(2.4f, 2.7f, 9.6f), 135f, 2f, 4f, 10f, 10f, "kitchen");
            Cam(l, "reception", new Vec3(15.6f, 2.7f, -4.6f), 315f, 6f, -5f, 16f, 4f, "reception");
            Cam(l, "copy", new Vec3(10.4f, 2.7f, 4.4f), 45f, 10f, 4f, 16f, 10f, "copy");
            return l;
        }

        static void Room(OfficeLayout l, string name, float minX, float minZ, float maxX, float maxZ)
        {
            l.Rooms.Add(new RoomDef { Id = EntityIds.Room(name), Min = new Vec3(minX, 0f, minZ), Max = new Vec3(maxX, 3f, maxZ) });
        }

        static void Cam(OfficeLayout l, string name, Vec3 pos, float yaw, float minX, float minZ, float maxX, float maxZ, string room)
        {
            l.Cameras.Add(new CameraDef { Id = EntityIds.Camera(name), Position = pos, Yaw = yaw, ZoneMin = new Vec3(minX, 0f, minZ), ZoneMax = new Vec3(maxX, 3f, maxZ), RoomId = EntityIds.Room(room) });
        }
    }
}
