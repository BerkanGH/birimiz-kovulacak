using System;
using BirimizKovulacak.Core;

namespace BirimizKovulacak.Telemetry
{
    /// <summary>
    /// Builds one event against its catalog definition. There is no method that accepts arbitrary text: strings only
    /// enter through <see cref="Enum"/> (value must be in the catalog set) and <see cref="Id"/> (validated pattern).
    /// Invalid input marks the event rejected (counted, dropped) instead of throwing.
    /// </summary>
    public sealed class TelemetryEventBuilder
    {
        internal static readonly TelemetryEventBuilder Null = new TelemetryEventBuilder();

        readonly GameTelemetryService _svc;
        readonly EventDef _def;
        readonly TelemetryValue[] _vals;
        bool _invalid;
        string _error;
        string _key;
        double _at = double.NaN;

        TelemetryEventBuilder() { _invalid = true; _error = "disabled"; }

        internal TelemetryEventBuilder(GameTelemetryService svc, EventDef def)
        {
            _svc = svc;
            _def = def;
            _vals = new TelemetryValue[def.Params.Length];
        }

        bool IsNull { get { return _svc == null; } }
        public bool IsValid { get { return !_invalid; } }
        public string Error { get { return _error; } }

        void Fail(string why)
        {
            if (_invalid) return;
            _invalid = true;
            _error = why;
        }

        ParamDef Param(string name, out int idx)
        {
            var p = _def.Find(name, out idx);
            if (p == null) Fail("unknown param " + name);
            return p;
        }

        public TelemetryEventBuilder Int(string name, long value)
        {
            if (IsNull) return this;
            int i; var p = Param(name, out i);
            if (p == null) return this;
            if (p.Type != ParamType.Int) { Fail("param " + name + " is not int"); return this; }
            _vals[i] = TelemetryValue.OfInt(value);
            return this;
        }

        public TelemetryEventBuilder Float(string name, double value)
        {
            if (IsNull) return this;
            int i; var p = Param(name, out i);
            if (p == null) return this;
            if (p.Type != ParamType.Float) { Fail("param " + name + " is not float"); return this; }
            if (double.IsNaN(value) || double.IsInfinity(value)) { if (!p.Optional) Fail("non-finite " + name); return this; }
            _vals[i] = TelemetryValue.OfFloat(value);
            return this;
        }

        /// <summary>Optional float: omitted (never zero) when negative.</summary>
        public TelemetryEventBuilder FloatIfKnown(string name, double value)
        {
            if (value < 0 || double.IsNaN(value)) return this;
            return Float(name, value);
        }

        public TelemetryEventBuilder Bool(string name, bool value)
        {
            if (IsNull) return this;
            int i; var p = Param(name, out i);
            if (p == null) return this;
            if (p.Type != ParamType.Bool) { Fail("param " + name + " is not bool"); return this; }
            _vals[i] = TelemetryValue.OfBool(value);
            return this;
        }

        /// <summary>Enum parameter: the value must be in the catalog set (also valid for slot-or-enum params).</summary>
        public TelemetryEventBuilder Enum(string name, string value)
        {
            if (IsNull) return this;
            int i; var p = Param(name, out i);
            if (p == null) return this;
            if (p.Type != ParamType.Enum && p.Type != ParamType.SlotOrEnum) { Fail("param " + name + " is not enum"); return this; }
            if (value == null || !p.AllowsEnum(value)) { Fail("value not in set for " + name); return this; }
            _vals[i] = TelemetryValue.OfStr(value);
            return this;
        }

        /// <summary>Enum parameter from a contract enum value (snake_case name).</summary>
        public TelemetryEventBuilder Enum<T>(string name, T value) where T : struct, IConvertible
        {
            return Enum(name, TelemetryNames.Of(value));
        }

        public TelemetryEventBuilder Id(string name, string value)
        {
            if (IsNull) return this;
            int i; var p = Param(name, out i);
            if (p == null) return this;
            if (p.Type != ParamType.Id) { Fail("param " + name + " is not id"); return this; }
            if (!TelemetryNames.IsValidId(p.Id, value)) { Fail("invalid id for " + name); return this; }
            _vals[i] = TelemetryValue.OfStr(value);
            return this;
        }

        /// <summary>Slot 0..5. Negative slots omit an optional parameter and reject a required one.</summary>
        public TelemetryEventBuilder Slot(string name, int slot)
        {
            if (IsNull) return this;
            int i; var p = Param(name, out i);
            if (p == null) return this;
            if (p.Type != ParamType.Slot && p.Type != ParamType.SlotOrEnum) { Fail("param " + name + " is not a slot"); return this; }
            if (slot < 0) { if (!p.Optional) Fail("missing slot " + name); return this; }
            if (slot >= LineProtocol.MaxPlayers) { Fail("slot out of range " + name); return this; }
            _vals[i] = TelemetryValue.OfInt(slot);
            return this;
        }

        /// <summary>Slot-or-enum parameter from a ballot value: colleague slot, <see cref="LineIds.Nobody"/> (no_action / fire_nobody), or -1 (abstain).</summary>
        public TelemetryEventBuilder Ballot(string name, int target)
        {
            if (IsNull) return this;
            int i; var p = Param(name, out i);
            if (p == null) return this;
            if (p.Type != ParamType.SlotOrEnum) { Fail("param " + name + " is not slot-or-enum"); return this; }
            if (target >= 0) return Slot(name, target);
            if (target == LineIds.Nobody)
            {
                foreach (var v in p.EnumValues) if (v == "no_action" || v == "fire_nobody") return Enum(name, v);
                Fail("no nobody value for " + name);
                return this;
            }
            return Enum(name, "abstain");
        }

        /// <summary>Logical key for the deterministic host event id (retries re-use it; the service drops the duplicate).</summary>
        public TelemetryEventBuilder Key(string logicalKey)
        {
            if (IsNull) return this;
            _key = logicalKey;
            return this;
        }

        /// <summary>Monotonic time of the fact (host events: engine time). Default: the service clock.</summary>
        public TelemetryEventBuilder At(double monotonicSeconds)
        {
            if (IsNull) return this;
            _at = monotonicSeconds;
            return this;
        }

        /// <summary>Internal diagnostics only (bypasses the queue bound so drop reports cannot be dropped).</summary>
        internal bool EmitForced()
        {
            if (IsNull) return false;
            return _svc.SubmitForced(_def, _vals, _invalid);
        }

        /// <summary>Finishes and submits the event; false when rejected, suppressed, duplicate or disabled.</summary>
        public bool Emit()
        {
            if (IsNull) return false;
            return _svc.Submit(_def, _vals, _invalid, _error, _key, _at);
        }
    }
}
