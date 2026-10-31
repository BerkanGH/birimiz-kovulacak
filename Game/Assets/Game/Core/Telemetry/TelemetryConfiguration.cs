using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Telemetry
{
    public enum OverflowPolicy { DropNewest = 0, DropOldest = 1 }

    /// <summary>Runtime configuration (StreamingAssets/Config/telemetry_config.json + launch arguments). Plain fields so any JSON reader can fill it.</summary>
    [Serializable]
    public sealed class TelemetryConfiguration
    {
        public static readonly string[] Environments = { "development", "playtest", "production" };

        public bool Enabled = true;
        public bool Local = true;
        public bool Cloud;
        /// <summary>development | playtest | production (also the log sub-folder).</summary>
        public string Environment = "development";
        public int QueueBound = 2048;
        public int PrivateBufferBound = 4096;
        public float FlushIntervalSeconds = 2f;
        public OverflowPolicy Overflow = OverflowPolicy.DropNewest;
        public int MaxFileBytes = 1024 * 1024;
        public int MaxFiles = 20;
        public int RetentionDays = 30;
        /// <summary>Facilitator cohort label, e.g. "friends_wave1" (token characters only).</summary>
        public string Cohort = "";
        /// <summary>Facilitator group code, e.g. "G07"; empty = random per hosting session.</summary>
        public string GroupId = "";
        /// <summary>Declared experiment assignments, "id=variant".</summary>
        public List<string> Experiments = new List<string>();

        public TelemetryConfiguration Clone()
        {
            var c = (TelemetryConfiguration)MemberwiseClone();
            c.Experiments = new List<string>(Experiments ?? new List<string>());
            return c;
        }

        /// <summary>Clamps every value to a sane range and drops invalid labels.</summary>
        public TelemetryConfiguration Normalize()
        {
            if (Array.IndexOf(Environments, Environment) < 0) Environment = "development";
            QueueBound = Math.Max(16, Math.Min(100000, QueueBound));
            PrivateBufferBound = Math.Max(16, Math.Min(100000, PrivateBufferBound));
            if (!(FlushIntervalSeconds >= 0.1f) || FlushIntervalSeconds > 60f) FlushIntervalSeconds = 2f;
            MaxFileBytes = Math.Max(4096, Math.Min(64 * 1024 * 1024, MaxFileBytes));
            MaxFiles = Math.Max(1, Math.Min(500, MaxFiles));
            RetentionDays = Math.Max(1, Math.Min(365, RetentionDays));
            if (!string.IsNullOrEmpty(Cohort) && !TelemetryNames.IsValidToken(Cohort)) Cohort = "";
            if (!string.IsNullOrEmpty(GroupId) && !TelemetryNames.IsValidGroupId(GroupId)) GroupId = "";
            if (Experiments == null) Experiments = new List<string>();
            var ok = new List<string>();
            foreach (var e in Experiments)
            {
                string id, v;
                if (TelemetrySessionContext.TryParseExperiment(e, out id, out v)) ok.Add(e);
            }
            Experiments = ok;
            return this;
        }

        /// <summary>
        /// Applies <c>-telemetry off|local|cloud</c>, <c>-cohort X</c>, <c>-playtestgroup G07</c>, <c>-experiment id=variant</c>.
        /// "cloud" only enables the cloud route (consent level 3 is still required per player).
        /// </summary>
        public TelemetryConfiguration ApplyLaunchArgs(string[] args)
        {
            if (args == null) return this;
            for (int i = 0; i < args.Length; i++)
            {
                string k = (args[i] ?? "").ToLowerInvariant();
                string next = i + 1 < args.Length ? args[i + 1] : null;
                if (next == null) continue;
                switch (k)
                {
                    case "-telemetry":
                        string m = next.ToLowerInvariant();
                        if (m == "off") { Enabled = false; Local = false; Cloud = false; }
                        else if (m == "local") { Enabled = true; Local = true; Cloud = false; }
                        else if (m == "cloud") { Enabled = true; Local = true; Cloud = true; }
                        i++;
                        break;
                    case "-cohort": Cohort = next; i++; break;
                    case "-playtestgroup": GroupId = next; i++; break;
                    case "-telemetryenv": Environment = next.ToLowerInvariant(); i++; break;
                    case "-experiment": if (Experiments == null) Experiments = new List<string>(); Experiments.Add(next); i++; break;
                }
            }
            return Normalize();
        }
    }
}
