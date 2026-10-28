// Shared contract: stable identifiers.
// Owner: Claude (contract). Changes require a note in docs/CONTRACTS.md "Changelog".
// C# 9 / netstandard2.1 only (Unity-compatible): no records, no init accessors, no file-scoped namespaces.

namespace BirimizKovulacak.Core
{
    /// <summary>Wire/protocol constants shared by host and clients.</summary>
    public static class Protocol
    {
        /// <summary>Bump whenever any wire format or rule contract changes incompatibly.</summary>
        public const int Version = 2;
        public const int MaxPlayers = 6;
        public const int MinPlayers = 3;
    }

    /// <summary>
    /// Player identifiers are lobby slot indices 0..5 (3–6 players; slots are contiguous from 0 within a match). They are stable for the whole session (including
    /// reconnects and rematches). <see cref="None"/> means "no player".
    /// </summary>
    public static class PlayerIds
    {
        public const int None = -1;
        public static bool IsValid(int id) { return id >= 0 && id < Protocol.MaxPlayers; }
    }

    /// <summary>
    /// String ids for layout entities (desks, slots, stations, items). They are stable across builds
    /// and are used verbatim on the wire and in logs.
    /// </summary>
    public static class EntityIds
    {
        // Desks and their sub-targets
        public static string Desk(int deskIndex) { return "desk." + deskIndex; }
        public static string DeskSurface(int deskIndex) { return "desk." + deskIndex + ".surface"; }
        public static string DeskDrawer(int deskIndex) { return "desk." + deskIndex + ".drawer"; }
        public static string DeskNameplate(int deskIndex) { return "desk." + deskIndex + ".nameplate"; }
        public static string DeskComputer(int deskIndex) { return "desk." + deskIndex + ".pc"; }

        // Shared stations
        public const string CoffeeMachine = "st.coffee";
        public const string Printer = "st.printer";
        public const string Phone = "st.phone";
        public const string DeliveryPoint = "st.delivery";
        public const string CleaningCloset = "st.cleaning";
        public const string Router = "st.router";       // server room: InternetOutage / RestartRouter
        public const string FuseBox = "st.fusebox";     // storage: PowerCut / ResetFuse

        // Shared slots / containers
        public const string MugShelf = "slot.mugshelf";
        public const string PrinterTray = "slot.printer_tray";
        public const string DeliveryTray = "slot.delivery";
        public const string JunkBin = "slot.junk";
        public const string LostAndFound = "slot.lostfound";
        public const string SprayRack = "slot.sprayrack";
        public const string HideBehindPrinter = "spot.printer_back";
        public const string HideInPlant = "spot.plant";
        public const string HideInCabinet = "spot.cabinet";
        public const string HideInToilet = "spot.toilet";
        public const string Mailroom = "slot.mailroom"; // new mail appears here

        // Security cameras ("cam.<name>") and rooms ("room.<name>")
        public static string Camera(string name) { return "cam." + name; }
        public static string Room(string name) { return "room." + name; }

        // Items (reports are created dynamically: "item.report.<n>")
        public static string Mug(int i) { return "item.mug." + i; }
        public static string Nameplate(int playerId) { return "item.nameplate." + playerId; }
        public static string Clutter(int i) { return "item.clutter." + i; }
        public static string Spray(int i) { return "item.spray." + i; }
        public static string Suspicious(string variant) { return "item.sus." + variant; }
        public static string Report(int serial) { return "item.report." + serial; }
        public static string Mail(int serial) { return "item.mail." + serial; }

        /// <summary>Returns the desk index encoded in ids like "desk.2", "desk.2.drawer"; -1 otherwise.</summary>
        public static int DeskIndexOf(string id)
        {
            if (string.IsNullOrEmpty(id) || !id.StartsWith("desk.")) return -1;
            int start = 5;
            int end = id.IndexOf('.', start);
            string num = end < 0 ? id.Substring(start) : id.Substring(start, end - start);
            int value;
            return int.TryParse(num, out value) ? value : -1;
        }
    }

    /// <summary>Item variant keys (localization keys are "item.&lt;variant&gt;").</summary>
    public static class ItemVariants
    {
        public const string Mug = "mug";
        public const string Report = "report";
        public const string Nameplate = "nameplate";
        public const string Spray = "spray";
        public const string ClutterPapers = "clutter_papers";
        public const string ClutterBox = "clutter_box";
        public const string Duck = "duck";
        public const string Stapler = "stapler";
        public const string RivalCv = "rival_cv";
        public const string BossMug = "boss_mug";
        public const string SalaryList = "salary_list";
        public const string Mail = "mail";
    }

    /// <summary>Manager personality ids (data in <see cref="MatchConfig.ManagerProfiles"/>; loc keys "manager.&lt;id&gt;.*").</summary>
    public static class ManagerProfileIds
    {
        public const string Paragoz = "paragoz";
        public const string Disiplinci = "disiplinci";
        public const string Kayirmaci = "kayirmaci";
        public const string Paranoyak = "paranoyak";
    }
}
