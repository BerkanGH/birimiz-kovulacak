using System;
using System.Globalization;
using System.Text;

namespace BirimizKovulacak.Telemetry
{
    /// <summary>Minimal JSON writer for the event envelope (one object per line, no external dependency).</summary>
    public static class TelemetryJson
    {
        public static void AppendString(StringBuilder sb, string s)
        {
            sb.Append('"');
            if (s != null)
            {
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            else sb.Append(c);
                            break;
                    }
                }
            }
            sb.Append('"');
        }

        public static void AppendDouble(StringBuilder sb, double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) { sb.Append("null"); return; }
            sb.Append(Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture));
        }

        static void Field(StringBuilder sb, string name, string value)
        {
            if (value == null || value.Length == 0) return;
            sb.Append(',');
            AppendString(sb, name);
            sb.Append(':');
            AppendString(sb, value);
        }

        static void Field(StringBuilder sb, string name, int value, int omittedBelow)
        {
            if (value < omittedBelow) return;
            sb.Append(',');
            AppendString(sb, name);
            sb.Append(':').Append(value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Appends one JSON object (no trailing newline).</summary>
        public static void Write(StringBuilder sb, TelemetryEvent e)
        {
            sb.Append("{\"schema_version\":").Append(TelemetrySessionContext.SchemaVersion.ToString(CultureInfo.InvariantCulture));
            Field(sb, "event", e.Def.Name);
            sb.Append(",\"event_version\":").Append(e.Def.Version.ToString(CultureInfo.InvariantCulture));
            Field(sb, "event_id", e.EventId);
            Field(sb, "authority", e.Source == TelemetryAuthority.Host ? "host" : "client");
            sb.Append(",\"timestamp_utc\":");
            AppendString(sb, e.TimestampUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
            sb.Append(",\"monotonic_elapsed_seconds\":");
            AppendDouble(sb, e.Monotonic);
            Field(sb, "environment", e.Environment);
            Field(sb, "session_id", e.SessionId);
            Field(sb, "match_id", e.MatchId);
            Field(sb, "week_id", e.WeekId, 0);
            Field(sb, "anonymous_group_id", e.GroupId);
            Field(sb, "anonymous_player_id", e.PlayerId);
            Field(sb, "player_slot", e.PlayerSlot, 0);
            Field(sb, "player_count", e.PlayerCount, 0);
            if (e.HasIsBot) sb.Append(",\"is_bot\":").Append(e.IsBot ? "true" : "false");
            if (e.HasBots) sb.Append(",\"has_bots\":true");
            Field(sb, "day_index", e.DayIndex, 0);
            Field(sb, "round_index", e.RoundIndex, 0);
            Field(sb, "task_number", e.TaskNumber, 0);
            Field(sb, "phase", e.Phase);
            Field(sb, "task_id", e.TaskId);
            Field(sb, "task_version", e.TaskVersion, 0);
            Field(sb, "game_version", e.GameVersion);
            Field(sb, "build_version", e.BuildVersion);
            Field(sb, "ruleset_version", e.RulesetVersion);
            Field(sb, "test_cohort", e.Cohort);
            if (e.Experiments != null && e.Experiments.Count > 0)
            {
                sb.Append(",\"experiments\":{");
                for (int i = 0; i < e.Experiments.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    AppendString(sb, e.Experiments[i].Key);
                    sb.Append(':');
                    AppendString(sb, e.Experiments[i].Value);
                }
                sb.Append('}');
            }
            if (e.Coverage >= 0) { sb.Append(",\"observation_coverage\":"); AppendDouble(sb, e.Coverage); }
            sb.Append(",\"params\":{");
            bool first = true;
            var ps = e.Def.Params;
            for (int i = 0; i < ps.Length; i++)
            {
                var v = e.Values[i];
                if (!v.HasValue) continue;
                if (!first) sb.Append(',');
                first = false;
                AppendString(sb, ps[i].Name);
                sb.Append(':');
                switch (v.Kind)
                {
                    case ValueKind.Int: sb.Append(v.L.ToString(CultureInfo.InvariantCulture)); break;
                    case ValueKind.Float: AppendDouble(sb, v.D); break;
                    case ValueKind.Bool: sb.Append(v.L != 0 ? "true" : "false"); break;
                    default: AppendString(sb, v.S); break;
                }
            }
            sb.Append("}}");
        }

        public static string ToLine(TelemetryEvent e)
        {
            var sb = new StringBuilder(384);
            Write(sb, e);
            return sb.ToString();
        }
    }
}
