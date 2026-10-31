using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BirimizKovulacak.Telemetry
{
    /// <summary>Builds docs/analytics/event_catalog.json and docs/ANALYTICS_EVENT_CATALOG.md text from <see cref="TelemetryCatalog"/> (no IO).</summary>
    public static class TelemetryCatalogExport
    {
        static readonly string[] SlotNote = { "slot", "slot or enum" };

        public static string TypeName(ParamDef p)
        {
            switch (p.Type)
            {
                case ParamType.Int: return "int";
                case ParamType.Float: return "float (seconds unless named otherwise)";
                case ParamType.Bool: return "bool";
                case ParamType.Slot: return "slot (0-5)";
                case ParamType.SlotOrEnum: return "slot or enum";
                case ParamType.Id: return "id (" + p.Id + ")";
                default: return "enum";
            }
        }

        static string StatusName(TelemetryAvailability a)
        {
            switch (a)
            {
                case TelemetryAvailability.Implemented: return "implemented";
                case TelemetryAvailability.PendingIntegration: return "pending_integration";
                case TelemetryAvailability.Unsupported: return "unsupported";
                default: return "not_applicable";
            }
        }

        static string ConsentName(ConsentRule c)
        {
            switch (c)
            {
                case ConsentRule.Self: return "self";
                case ConsentRule.AllParticipants: return "all_participants";
                case ConsentRule.Voter: return "voter";
                default: return "host_aggregate";
            }
        }

        static string AuthName(TelemetryAuthority a) { return a == TelemetryAuthority.Host ? "host" : a == TelemetryAuthority.Client ? "client" : "both"; }

        static string RoutingName(TelemetryRouting r) { return (r & TelemetryRouting.Cloud) != 0 ? "local+cloud" : "local"; }

        public static string ToJson()
        {
            var sb = new StringBuilder(64 * 1024);
            sb.Append("{\n  \"schema_version\": ").Append(TelemetrySessionContext.SchemaVersion).Append(",\n  \"generated_from\": \"Game/Assets/Game/Core/Telemetry/TelemetryCatalog.cs\",\n  \"events\": [\n");
            for (int i = 0; i < TelemetryCatalog.Count; i++)
            {
                var d = TelemetryCatalog.All[i];
                sb.Append("    {");
                KV(sb, "name", d.Name, true);
                sb.Append(", \"version\": ").Append(d.Version);
                KV(sb, "authority", AuthName(d.Authority));
                KV(sb, "sensitivity", d.Sensitivity == TelemetrySensitivity.Public ? "public" : "private_until_match_end");
                KV(sb, "consent", ConsentName(d.Consent));
                KV(sb, "routing", RoutingName(d.Routing));
                KV(sb, "availability", StatusName(d.Availability));
                KV(sb, "availability_note", d.AvailabilityNote);
                KV(sb, "max_frequency", d.MaxFrequency);
                KV(sb, "meaning", d.Meaning);
                KV(sb, "trigger", d.Trigger);
                KV(sb, "notes", d.Notes);
                sb.Append(", \"kpi\": [");
                for (int k = 0; k < d.Kpi.Length; k++) { if (k > 0) sb.Append(", "); TelemetryJson.AppendString(sb, d.Kpi[k]); }
                sb.Append("], \"params\": [");
                for (int k = 0; k < d.Params.Length; k++)
                {
                    var p = d.Params[k];
                    if (k > 0) sb.Append(", ");
                    sb.Append('{');
                    KV(sb, "name", p.Name, true);
                    KV(sb, "type", p.Type.ToString().ToLowerInvariant());
                    sb.Append(", \"optional\": ").Append(p.Optional ? "true" : "false");
                    sb.Append(", \"cloud\": ").Append(p.Cloud ? "true" : "false");
                    if (p.Type == ParamType.Enum || p.Type == ParamType.SlotOrEnum)
                    {
                        if (p.EnumSource.Length > 0) KV(sb, "enum_source", p.EnumSource);
                        sb.Append(", \"values\": [");
                        for (int v = 0; v < p.EnumValues.Length; v++) { if (v > 0) sb.Append(", "); TelemetryJson.AppendString(sb, p.EnumValues[v]); }
                        sb.Append(']');
                    }
                    if (p.Type == ParamType.Id) KV(sb, "id_kind", p.Id.ToString().ToLowerInvariant());
                    sb.Append('}');
                }
                sb.Append("]}");
                sb.Append(i < TelemetryCatalog.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");
            return sb.ToString();
        }

        static void KV(StringBuilder sb, string key, string value, bool first = false)
        {
            if (!first) sb.Append(", ");
            TelemetryJson.AppendString(sb, key);
            sb.Append(": ");
            TelemetryJson.AppendString(sb, value ?? "");
        }

        public static string ToMarkdown()
        {
            var sb = new StringBuilder(96 * 1024);
            sb.Append("# Analytics event catalog\n\n");
            sb.Append("Generated from `Game/Assets/Game/Core/Telemetry/TelemetryCatalog.cs` by the `CatalogExport` test in `Tools/TelemetryTests` ");
            sb.Append("(`TELEMETRY_EXPORT_DOCS=1 dotnet test Tools/TelemetryTests/TelemetryTests.csproj --filter CatalogExport`). Do not edit by hand. ");
            sb.Append("Machine-readable twin: `docs/analytics/event_catalog.json`. Contract: `docs/ANALYTICS_ARCHITECTURE.md`.\n\n");
            int impl = 0, pend = 0, unsup = 0, na = 0;
            foreach (var d in TelemetryCatalog.All)
            {
                if (d.Availability == TelemetryAvailability.Implemented) impl++;
                else if (d.Availability == TelemetryAvailability.PendingIntegration) pend++;
                else if (d.Availability == TelemetryAvailability.Unsupported) unsup++;
                else na++;
            }
            sb.Append("**" + TelemetryCatalog.Count + " events**: " + impl + " implemented, " + pend + " pending_integration, " + unsup + " unsupported, " + na + " not_applicable.\n\n");
            sb.Append("Status meaning: `implemented` = emitting code exists in this repository, fed from sources that exist today (contract events/views, connection state), covered by tests; ");
            sb.Append("host events still need the `HostTelemetryBridge` call sites in `HostLineController` (see `docs/ANALYTICS_SETUP.md`) and the v4 engine emitting the contract events. ");
            sb.Append("`pending_integration` = a source is missing (named in the entry); the event is **not** emitted. `not_applicable` / `unsupported` = never emitted.\n\n");
            sb.Append("Envelope fields (every event, omitted when not applicable): schema_version, event, event_version, event_id, authority, timestamp_utc, monotonic_elapsed_seconds, environment, session_id, match_id, week_id, anonymous_group_id, anonymous_player_id, player_slot, player_count, is_bot, has_bots, day_index, round_index, task_number, phase, task_id, task_version, game_version, build_version, ruleset_version, test_cohort, experiments, observation_coverage, params.\n\n");
            sb.Append("## Index\n\n| Event | Authority | Sensitivity | Consent | Routing | Status |\n|---|---|---|---|---|---|\n");
            foreach (var d in TelemetryCatalog.All)
                sb.Append("| [`" + d.Name + "`](#" + d.Name + ") | " + AuthName(d.Authority) + " | " + (d.Sensitivity == TelemetrySensitivity.Public ? "public" : "private until match end") + " | " + ConsentName(d.Consent) + " | " + RoutingName(d.Routing) + " | " + StatusName(d.Availability) + " |\n");
            sb.Append("\n## Events\n");
            foreach (var d in TelemetryCatalog.All)
            {
                sb.Append("\n### " + d.Name + "\n\n");
                sb.Append("- **Name / version**: `" + d.Name + "` v" + d.Version + "\n");
                sb.Append("- **Meaning**: " + d.Meaning + "\n");
                sb.Append("- **Trigger location**: " + (d.Trigger.Length == 0 ? "-" : "`" + d.Trigger + "`") + "\n");
                sb.Append("- **Authority**: " + AuthName(d.Authority) + "\n");
                sb.Append("- **Privacy / consent**: " + (d.Sensitivity == TelemetrySensitivity.Public ? "public (written immediately)" : "private_until_match_end (held in host memory, written at results/abandon)") + "; consent rule `" + ConsentName(d.Consent) + "`\n");
                sb.Append("- **Routing**: " + RoutingName(d.Routing) + (d.Routing.HasFlag(TelemetryRouting.Cloud) ? " (cloud only for consent level 3; cloud payload = parameters marked cloud)" : "") + "\n");
                sb.Append("- **Expected max frequency**: " + (d.MaxFrequency.Length == 0 ? "-" : d.MaxFrequency) + "\n");
                sb.Append("- **KPI contribution**: " + (d.Kpi.Length == 0 ? "-" : string.Join(", ", d.Kpi)) + "\n");
                sb.Append("- **Availability**: " + StatusName(d.Availability) + (d.AvailabilityNote.Length > 0 ? " - " + d.AvailabilityNote : "") + "\n");
                if (d.Notes.Length > 0) sb.Append("- **Notes**: " + d.Notes + "\n");
                if (d.Params.Length == 0) sb.Append("- **Params**: none\n");
                else
                {
                    sb.Append("\n| Param | Type | Required | Cloud | Values |\n|---|---|---|---|---|\n");
                    foreach (var p in d.Params)
                    {
                        string values = p.Type == ParamType.Enum || p.Type == ParamType.SlotOrEnum
                            ? (p.EnumSource.Length > 0 ? "@" + p.EnumSource + " (" + string.Join(", ", p.EnumValues) + ")" : string.Join(", ", p.EnumValues))
                            : "";
                        sb.Append("| `" + p.Name + "` | " + TypeName(p) + " | " + (p.Optional ? "optional (omitted, never 0)" : "yes") + " | " + (p.Cloud ? "yes" : "no") + " | " + values + " |\n");
                    }
                }
            }
            return sb.ToString();
        }
    }
}
