using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Telemetry
{
    /// <summary>
    /// Shared envelope fields. Created once per process (session id fixed), updated on lobby / match / phase changes.
    /// Main-thread only: events copy the values when they are built.
    /// </summary>
    public sealed class TelemetrySessionContext
    {
        public const int SchemaVersion = 1;

        public string Environment { get; private set; } = "development";
        public string SessionId { get; private set; } = "";
        public string GroupId { get; private set; } = "";
        public string GameVersion { get; set; } = "";
        public string BuildVersion { get; set; } = "";
        public string RulesetVersion { get; set; } = "";
        public string Cohort { get; private set; } = "";
        public IReadOnlyList<KeyValuePair<string, string>> ConfigExperiments { get; private set; } = Array.Empty<KeyValuePair<string, string>>();

        public string MatchId { get; private set; } = "";
        public int WeekId { get; private set; } = -1;
        public int PlayerCount { get; private set; } = -1;
        public bool HasBots { get; private set; }
        public IReadOnlyList<KeyValuePair<string, string>> Experiments { get; private set; } = Array.Empty<KeyValuePair<string, string>>();

        /// <summary>Local player's seat (client events carry it); -1 before the welcome.</summary>
        public int LocalSlot { get; set; } = -1;
        /// <summary>Local anonymous id, set only after consent >= 2.</summary>
        public string LocalPlayerId { get; set; } = "";
        public bool LocalIsBot { get; set; }

        public int DayIndex { get; private set; } = -1;
        public int RoundIndex { get; private set; } = -1;
        public int TaskNumber { get; private set; } = -1;
        public string Phase { get; private set; } = "";
        public string TaskId { get; private set; } = "";
        public int TaskVersion { get; private set; } = -1;

        public int Initializations { get; private set; }

        /// <summary>First call fixes environment, session id, cohort and configured experiments; later calls are ignored (init once).</summary>
        public bool InitializeOnce(string environment, string sessionId, string groupId, string cohort, IEnumerable<string> experiments)
        {
            if (Initializations > 0) return false;
            Initializations = 1;
            Environment = environment ?? "development";
            SessionId = TelemetryNames.IsValidToken(sessionId) ? sessionId : "";
            GroupId = TelemetryNames.IsValidGroupId(groupId) ? groupId : "";
            Cohort = TelemetryNames.IsValidToken(cohort) ? cohort : "";
            ConfigExperiments = ParseExperiments(experiments);
            Experiments = ConfigExperiments;
            return true;
        }

        /// <summary>The host's lobby id (shared with clients through LineLobbyView.SessionId) replaces the local one.</summary>
        public void AdoptSession(string sessionId, string groupId)
        {
            if (TelemetryNames.IsValidToken(sessionId)) SessionId = sessionId;
            if (TelemetryNames.IsValidGroupId(groupId)) GroupId = groupId;
        }

        public void BeginMatch(string matchId, int weekId, int playerCount, bool hasBots, IEnumerable<string> experiments)
        {
            MatchId = TelemetryNames.IsValidToken(matchId) ? matchId : "";
            WeekId = weekId;
            PlayerCount = playerCount;
            HasBots = hasBots;
            var all = new List<KeyValuePair<string, string>>(ConfigExperiments);
            foreach (var kv in ParseExperiments(experiments))
            {
                bool dup = false;
                foreach (var x in all) if (x.Key == kv.Key) dup = true;
                if (!dup) all.Add(kv);
            }
            Experiments = all;
            DayIndex = RoundIndex = TaskNumber = TaskVersion = -1;
            Phase = TaskId = "";
        }

        public void EndMatch()
        {
            MatchId = "";
            WeekId = -1;
            PlayerCount = -1;
            HasBots = false;
            Experiments = ConfigExperiments;
            DayIndex = RoundIndex = TaskNumber = TaskVersion = -1;
            Phase = TaskId = "";
        }

        public void SetPosition(string phase, int day, int roundIndex, int taskNumber, string taskId, int taskVersion)
        {
            Phase = phase ?? "";
            DayIndex = day;
            RoundIndex = roundIndex;
            TaskNumber = taskNumber;
            TaskId = taskId ?? "";
            TaskVersion = taskVersion;
        }

        public static bool TryParseExperiment(string entry, out string id, out string variant)
        {
            id = variant = null;
            if (string.IsNullOrEmpty(entry)) return false;
            int eq = entry.IndexOf('=');
            if (eq <= 0 || eq == entry.Length - 1) return false;
            string a = entry.Substring(0, eq), b = entry.Substring(eq + 1);
            if (a.Length > 24 || b.Length > 24 || !Lower(a) || !Lower(b)) return false;
            id = a; variant = b;
            return true;
        }

        static bool Lower(string s)
        {
            foreach (char c in s) if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')) return false;
            return true;
        }

        static List<KeyValuePair<string, string>> ParseExperiments(IEnumerable<string> entries)
        {
            var list = new List<KeyValuePair<string, string>>();
            if (entries == null) return list;
            foreach (var e in entries)
            {
                string id, v;
                if (TryParseExperiment(e, out id, out v)) list.Add(new KeyValuePair<string, string>(id, v));
            }
            return list;
        }
    }
}
