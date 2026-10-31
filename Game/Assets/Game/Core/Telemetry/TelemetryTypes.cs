using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Telemetry
{
    /// <summary>Both = emitted by whichever process observes it (technical_failure, telemetry_*): the event's <see cref="TelemetryEvent.Source"/> says which.</summary>
    public enum TelemetryAuthority : byte { Host = 0, Client = 1, Both = 2 }

    public enum TelemetrySensitivity : byte
    {
        Public = 0,
        /// <summary>Held in memory on the host and written only at match end / abandon.</summary>
        PrivateUntilMatchEnd = 1,
    }

    /// <summary>Whose consent is required (docs/ANALYTICS_ARCHITECTURE.md §4).</summary>
    public enum ConsentRule : byte
    {
        /// <summary>The subject player (first slot parameter), or the local player for client events.</summary>
        Self = 0,
        /// <summary>Every slot-typed parameter.</summary>
        AllParticipants = 1,
        /// <summary>The voter; a vote target without consent is rewritten to "unconsented".</summary>
        Voter = 2,
        /// <summary>The local (host) machine's consent; counts only, no per-player ids.</summary>
        HostAggregate = 3,
    }

    [Flags]
    public enum TelemetryRouting : byte { None = 0, Local = 1, Cloud = 2 }

    public enum TelemetryAvailability : byte
    {
        /// <summary>Emitted by code in this repository from sources that exist today.</summary>
        Implemented = 0,
        /// <summary>The emitting code exists or is specified but a source (engine field, UI call site, voice API) is missing.</summary>
        PendingIntegration = 1,
        /// <summary>Cannot be measured with the current contract.</summary>
        Unsupported = 2,
        /// <summary>Does not exist in this ruleset.</summary>
        NotApplicable = 3,
    }

    public enum ParamType : byte
    {
        Int = 0,
        Float = 1,
        Bool = 2,
        /// <summary>String from a fixed set only.</summary>
        Enum = 3,
        /// <summary>String matching a fixed validated pattern (<see cref="IdKind"/>).</summary>
        Id = 4,
        /// <summary>Player slot 0..5 (resolved to anonymous ids through player_slot_map).</summary>
        Slot = 5,
        /// <summary>Slot 0..5 or one enum string (no_action, abstain, ...).</summary>
        SlotOrEnum = 6,
    }

    public enum IdKind : byte { None = 0, PlayerId = 1, MatchId = 2, TypeName = 3 }

    public sealed class ParamDef
    {
        public string Name;
        public ParamType Type;
        public bool Optional;
        public bool Cloud;
        public IdKind Id;
        public string[] EnumValues = Array.Empty<string>();
        public string EnumSource = "";

        public bool IsSlot { get { return Type == ParamType.Slot; } }
        public bool AllowsEnum(string v)
        {
            for (int i = 0; i < EnumValues.Length; i++) if (string.Equals(EnumValues[i], v, StringComparison.Ordinal)) return true;
            return false;
        }
    }

    public sealed class EventDef
    {
        public int Index;
        public string Name;
        public int Version = 1;
        public TelemetryAuthority Authority;
        public TelemetrySensitivity Sensitivity;
        public ConsentRule Consent;
        public TelemetryRouting Routing;
        public ParamDef[] Params = Array.Empty<ParamDef>();
        public string[] Kpi = Array.Empty<string>();
        public string MaxFrequency = "";
        public TelemetryAvailability Availability;
        /// <summary>For pending/unsupported/not-applicable entries: the exact missing source or reason.</summary>
        public string AvailabilityNote = "";
        public string Meaning = "";
        public string Trigger = "";
        public string Notes = "";

        public ParamDef Find(string name, out int index)
        {
            for (int i = 0; i < Params.Length; i++)
                if (string.Equals(Params[i].Name, name, StringComparison.Ordinal)) { index = i; return Params[i]; }
            index = -1;
            return null;
        }
    }

    public enum ValueKind : byte { None = 0, Int = 1, Float = 2, Bool = 3, Str = 4 }

    /// <summary>
    /// A typed parameter value. Strings can only come from the catalog (enum sets, validated id patterns): the public
    /// builder never accepts arbitrary text, so free text cannot reach an event by construction.
    /// </summary>
    public struct TelemetryValue
    {
        public ValueKind Kind;
        public long L;
        public double D;
        public string S;
        public bool HasValue { get { return Kind != ValueKind.None; } }
        public static TelemetryValue OfInt(long v) { return new TelemetryValue { Kind = ValueKind.Int, L = v }; }
        public static TelemetryValue OfFloat(double v) { return new TelemetryValue { Kind = ValueKind.Float, D = v }; }
        public static TelemetryValue OfBool(bool v) { return new TelemetryValue { Kind = ValueKind.Bool, L = v ? 1 : 0 }; }
        internal static TelemetryValue OfStr(string v) { return new TelemetryValue { Kind = ValueKind.Str, S = v }; }
    }

    /// <summary>An immutable-by-convention event: catalog definition + envelope fields + typed values.</summary>
    public sealed class TelemetryEvent
    {
        public EventDef Def;
        /// <summary>Actual emitter (host or client), resolved from <see cref="EventDef.Authority"/>.</summary>
        public TelemetryAuthority Source;
        public string EventId = "";
        public ulong EventKey;
        public DateTime TimestampUtc;
        public double Monotonic;
        public TelemetryValue[] Values;

        // envelope snapshot (null / -1 = omitted)
        public string Environment, SessionId, MatchId, GroupId, PlayerId, TaskId, GameVersion, BuildVersion, RulesetVersion, Cohort, Phase;
        public IReadOnlyList<KeyValuePair<string, string>> Experiments;
        public int WeekId = -1, PlayerSlot = -1, PlayerCount = -1, DayIndex = -1, RoundIndex = -1, TaskNumber = -1, TaskVersion = -1;
        public bool HasIsBot, IsBot, HasBots;
        public double Coverage = -1;

        // routing decision (set by the service)
        public bool RouteLocal, RouteCloud;
        /// <summary>Set when consent was lowered after the event was accepted: it is never delivered.</summary>
        public bool Revoked;
        /// <summary>Primary subject slot (first slot parameter), -1 when none.</summary>
        public int Subject = -1;

        public string Name { get { return Def.Name; } }
        public bool IsPrivate { get { return Def.Sensitivity == TelemetrySensitivity.PrivateUntilMatchEnd; } }

        public bool TryGet(string param, out TelemetryValue v)
        {
            int i;
            if (Def.Find(param, out i) == null) { v = default(TelemetryValue); return false; }
            v = Values[i];
            return v.HasValue;
        }

        public long GetInt(string param, long fallback = -1) { TelemetryValue v; return TryGet(param, out v) ? v.L : fallback; }
        public double GetFloat(string param, double fallback = double.NaN) { TelemetryValue v; return TryGet(param, out v) ? (v.Kind == ValueKind.Float ? v.D : v.L) : fallback; }
        public string GetString(string param) { TelemetryValue v; return TryGet(param, out v) ? v.S : null; }
        public bool GetBool(string param) { TelemetryValue v; return TryGet(param, out v) && v.L != 0; }

        /// <summary>True when any slot-typed parameter references <paramref name="slot"/>.</summary>
        public bool References(int slot)
        {
            for (int i = 0; i < Def.Params.Length; i++)
            {
                var p = Def.Params[i];
                if ((p.Type == ParamType.Slot || (p.Type == ParamType.SlotOrEnum && Values[i].Kind == ValueKind.Int)) && Values[i].Kind == ValueKind.Int && Values[i].L == slot) return true;
            }
            return false;
        }
    }
}
