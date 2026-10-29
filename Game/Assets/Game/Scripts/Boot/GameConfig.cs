using System;
using System.IO;
using BirimizKovulacak.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Build id and the editable v3 rules file (StreamingAssets/Config/line_config.json). Only the host's config matters
    /// for a week (it is authoritative and sends it to every client in the welcome message).
    /// </summary>
    public static class GameConfig
    {
        static string _buildId;

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

        public const string LineConfigRelativePath = "Config/line_config.json";
        static LineConfig _line;

        /// <summary>v3 rules config: StreamingAssets/Config/line_config.json when it matches the protocol version, else defaults; -durationscale applied.</summary>
        public static LineConfig Line
        {
            get
            {
                if (_line == null) _line = LoadLine();
                return _line;
            }
        }

        static LineConfig LoadLine()
        {
            LineConfig cfg = LineConfig.CreateDefault();
            string path = Path.Combine(Application.streamingAssetsPath, LineConfigRelativePath);
            try
            {
                if (File.Exists(path))
                {
                    var loaded = JsonConvert.DeserializeObject<LineConfig>(File.ReadAllText(path), JsonSettings);
                    if (loaded != null && loaded.ConfigVersion == LineProtocol.Version) { cfg = loaded; Debug.Log("[Config] Loaded " + path); }
                    else Debug.LogWarning("[Config] " + path + " is for another version - using built-in v3 defaults.");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Config] Failed to read " + path + ": " + e.Message + " — using v3 defaults.");
                cfg = LineConfig.CreateDefault();
            }
            float scale = LaunchArgs.Current.DurationScale;
            if (scale > 0f && Math.Abs(scale - 1f) > 0.001f) cfg.Durations.Scale(scale);
            return cfg;
        }

        /// <summary>Deep copy (one per week, so per-week tweaks never leak into the next).</summary>
        public static LineConfig CopyLine() { return JsonConvert.DeserializeObject<LineConfig>(ToJson(Line), JsonSettings); }

        public static string ToJson(LineConfig cfg) { return JsonConvert.SerializeObject(cfg, JsonSettings); }
    }
}
