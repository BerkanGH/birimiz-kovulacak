using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BirimizKovulacak.Core;

namespace BirimizKovulacak.Telemetry
{
    /// <summary>Naming, id generation and id validation helpers (no free text ever passes through here).</summary>
    public static class TelemetryNames
    {
        static readonly Dictionary<Type, string[]> SnakeCache = new Dictionary<Type, string[]>();

        /// <summary>PascalCase -> snake_case ("HungUp" -> "hung_up", "ITLog" -> "it_log").</summary>
        public static string Snake(string pascal)
        {
            if (string.IsNullOrEmpty(pascal)) return "";
            var sb = new StringBuilder(pascal.Length + 4);
            for (int i = 0; i < pascal.Length; i++)
            {
                char c = pascal[i];
                if (char.IsUpper(c))
                {
                    bool prevLower = i > 0 && !char.IsUpper(pascal[i - 1]);
                    bool acronymEnd = i > 0 && char.IsUpper(pascal[i - 1]) && i + 1 < pascal.Length && char.IsLower(pascal[i + 1]);
                    if (i > 0 && (prevLower || acronymEnd)) sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>snake_case name of an enum value, cached; "" for undefined values.</summary>
        public static string Of<T>(T value) where T : struct, IConvertible
        {
            var t = typeof(T);
            string[] table;
            lock (SnakeCache)
            {
                if (!SnakeCache.TryGetValue(t, out table))
                {
                    var values = (T[])Enum.GetValues(t);
                    int max = 0;
                    foreach (var v in values) max = Math.Max(max, Convert.ToInt32(v, CultureInfo.InvariantCulture));
                    table = new string[max + 1];
                    foreach (var v in values) table[Convert.ToInt32(v, CultureInfo.InvariantCulture)] = Snake(v.ToString());
                    SnakeCache[t] = table;
                }
            }
            int i = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            return i >= 0 && i < table.Length && table[i] != null ? table[i] : "";
        }

        public static string[] EnumNames<T>(bool skipNone) where T : struct, IConvertible
        {
            var list = new List<string>();
            foreach (T v in Enum.GetValues(typeof(T)))
            {
                string s = Snake(v.ToString());
                if (skipNone && s == "none") continue;
                list.Add(s);
            }
            return list.ToArray();
        }

        public static string[] TaskIds()
        {
            var list = new List<string>();
            for (int i = 1; i <= LineTasks.Count; i++) list.Add(LineTasks.Id((TaskKind)i));
            return list.ToArray();
        }

        // ---- ids --------------------------------------------------------------------------------

        const ulong FnvOffset = 14695981039346656037UL;
        const ulong FnvPrime = 1099511628211UL;

        public static ulong Fnv1a64(string s)
        {
            ulong h = FnvOffset;
            var bytes = Encoding.UTF8.GetBytes(s ?? "");
            for (int i = 0; i < bytes.Length; i++) { h ^= bytes[i]; h *= FnvPrime; }
            return h;
        }

        /// <summary>Deterministic host event key: FNV-1a 64 of match_id|event|logical_key.</summary>
        public static ulong HostEventKey(string matchId, string eventName, string logicalKey)
        {
            return Fnv1a64((matchId ?? "") + "|" + eventName + "|" + (logicalKey ?? ""));
        }

        public static string HostEventId(ulong key) { return "h-" + key.ToString("x16", CultureInfo.InvariantCulture); }

        /// <summary>Random 128-bit hex id with a one-letter prefix ("c-", "s-", "m-", "p-", "g-").</summary>
        public static string RandomId(string prefix, Func<ulong> random64)
        {
            return prefix + random64().ToString("x16", CultureInfo.InvariantCulture) + random64().ToString("x16", CultureInfo.InvariantCulture);
        }

        public static bool IsValidId(IdKind kind, string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            switch (kind)
            {
                case IdKind.PlayerId: return s.Length >= 10 && s.Length <= 40 && s[0] == 'p' && s[1] == '-' && IsHex(s, 2);
                case IdKind.MatchId: return s.Length >= 3 && s.Length <= 40 && IsTokenChars(s, 0);
                case IdKind.TypeName:
                    if (s.Length > 80 || !char.IsLetter(s[0]) || s[0] > 127) return false;
                    foreach (char c in s) if (!(c < 128 && (char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '`' || c == '+'))) return false;
                    return true;
            }
            return false;
        }

        /// <summary>Group id: facilitator code ("G07") or random "g-...": short token, no spaces.</summary>
        public static bool IsValidGroupId(string s) { return !string.IsNullOrEmpty(s) && s.Length <= 24 && IsTokenChars(s, 0); }
        public static bool IsValidToken(string s) { return !string.IsNullOrEmpty(s) && s.Length <= 40 && IsTokenChars(s, 0); }

        static bool IsHex(string s, int from)
        {
            if (s.Length - from < 8) return false;
            for (int i = from; i < s.Length; i++) { char c = s[i]; if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false; }
            return true;
        }

        static bool IsTokenChars(string s, int from)
        {
            for (int i = from; i < s.Length; i++)
            {
                char c = s[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '-' || c == '_' || c == '.')) return false;
            }
            return true;
        }
    }
}
