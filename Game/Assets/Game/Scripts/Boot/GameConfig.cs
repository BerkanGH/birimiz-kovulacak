using System;
using System.IO;
using BirimizKovulacak.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Loads the editable balance/rules file (StreamingAssets/Config/match_config.json) and the layout.
    /// Only the host's config matters for a match (it is authoritative); clients load it for prompts/timers.
    /// </summary>
    public static class GameConfig
    {
        public const string ConfigRelativePath = "Config/match_config.json";
        static MatchConfig _config;
        static OfficeLayout _layout;
        static string _buildId;

        public static MatchConfig Match
        {
            get
            {
                if (_config == null) _config = Load();
                return _config;
            }
        }

        public static OfficeLayout Layout
        {
            get
            {
                if (_layout == null) _layout = DefaultOffice.Create();
                return _layout;
            }
        }

        /// <summary>Identifies the exact build; host and clients must match.</summary>
        public static string BuildId
        {
            get
            {
                if (_buildId == null)
                {
                    var asset = Resources.Load<TextAsset>("build_id");
                    _buildId = (Application.version + "+" + (asset != null ? asset.text.Trim() : "dev"));
                }
                return _buildId;
            }
        }

        public static JsonSerializerSettings JsonSettings => new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() },
        };

        static MatchConfig Load()
        {
            MatchConfig cfg = MatchConfig.CreateDefault();
            string path = Path.Combine(Application.streamingAssetsPath, ConfigRelativePath);
            try
            {
                if (File.Exists(path))
                {
                    cfg = JsonConvert.DeserializeObject<MatchConfig>(File.ReadAllText(path), JsonSettings) ?? cfg;
                    Debug.Log("[Config] Loaded " + path);
                }
                else Debug.Log("[Config] Using built-in defaults (no " + path + ")");
            }
            catch (Exception e)
            {
                Debug.LogError("[Config] Failed to read " + path + ": " + e.Message + " — using defaults.");
                cfg = MatchConfig.CreateDefault();
            }
            float scale = LaunchArgs.Current.DurationScale;
            if (scale > 0f && Math.Abs(scale - 1f) > 0.001f) ScaleDurations(cfg, scale);
            return cfg;
        }

        /// <summary>Test helper: shortens the phase timeline (task/sabotage durations are unchanged).</summary>
        public static void ScaleDurations(MatchConfig cfg, float s)
        {
            var d = cfg.Durations;
            d.Briefing *= s; d.Work1 *= s; d.Raid = Math.Max(26f, d.Raid * s); d.Work2 *= s;
            d.FinalInspection = Math.Max(26f, d.FinalInspection * s);
            d.MeetingDiscussion *= s; d.MeetingDefense *= s; d.MeetingVote = Math.Max(6f, d.MeetingVote * s);
            d.MeetingVerdict = Math.Max(6f, d.MeetingVerdict * s); d.Results *= s;
            cfg.QuotaTarget = (float)Math.Round(cfg.QuotaTarget * Math.Max(0.35f, s));
        }

        public static string ToJson(MatchConfig cfg) { return JsonConvert.SerializeObject(cfg, JsonSettings); }
    }
}
