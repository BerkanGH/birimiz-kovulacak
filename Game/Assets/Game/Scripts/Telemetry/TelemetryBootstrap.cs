using System;
using System.IO;
using System.Threading;
using BirimizKovulacak.Core;
using BirimizKovulacak.Telemetry;
using Newtonsoft.Json;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Self-installing analytics runtime (no scene wiring). Loads StreamingAssets/Config/telemetry_config.json plus launch
    /// arguments, creates the service with its sinks, and runs a background flush thread. Events are built on the main
    /// thread; the thread only writes batches that were detached under the queue lock. Never throws into the game.
    /// </summary>
    public static class TelemetryBootstrap
    {
        public const string ConfigRelativePath = "Config/telemetry_config.json";

        public static GameTelemetryService Service { get; private set; }
        public static HostTelemetryCoordinator Host { get; private set; }
        public static ClientTelemetry Client { get; private set; }
        public static TelemetryConsentStore Consent { get; private set; }
        public static DebugTelemetrySink DebugSink { get; private set; }
        public static LocalJsonlTelemetrySink LocalSink { get; private set; }
        public static TelemetryConfiguration Config { get; private set; }
        public static string LogDirectory { get; private set; } = "";

        static Thread _thread;
        static volatile bool _run;
        static readonly AutoResetEvent Wake = new AutoResetEvent(false);
        static bool _installed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (_installed) return;
            _installed = true;
            try
            {
                var cfg = LoadConfig();
                Config = cfg;
                Consent = new TelemetryConsentStore();
                TelemetryHooks.Privacy = Consent;
                if (!cfg.Enabled)
                {
                    Debug.Log("[Telemetry] disabled by configuration");
                    return;
                }
                string dir = Path.Combine(Application.persistentDataPath, "telemetry", cfg.Environment);
                // Automated multi-process runs keep their events next to the run's logs (Tools/run_local4.ps1).
                string overrideDir = ArgValue("-telemetrydir");
                if (!string.IsNullOrEmpty(overrideDir)) dir = Path.Combine(overrideDir, cfg.Environment);
                LogDirectory = dir;
                var ctx = new TelemetrySessionContext { GameVersion = Application.version, BuildVersion = SafeBuildId(), RulesetVersion = LineProtocol.RulesetVersion };
                Service = new GameTelemetryService(cfg, ctx);
                if (cfg.Local)
                {
                    LocalSink = new LocalJsonlTelemetrySink(dir, ctx.SessionId, cfg.MaxFileBytes, cfg.MaxFiles, cfg.RetentionDays);
                    Service.AddSink(LocalSink);
                }
                if (Application.isEditor || Debug.isDebugBuild)
                {
                    DebugSink = new DebugTelemetrySink(256);
                    Service.AddSink(DebugSink);
                }
                if (cfg.Cloud) foreach (var s in TelemetryRegistry.CreateSinks()) Service.AddSink(s);
                Host = new HostTelemetryCoordinator(Service, () => Service.Now);
                Consent.Bind(Service, cfg);
                Client = new ClientTelemetry(Service, Consent);
                TelemetryHooks.Client = Client;

                var go = new GameObject("TelemetryRunner") { hideFlags = HideFlags.HideAndDontSave };
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<TelemetryRunner>();
                if (Application.isEditor || Debug.isDebugBuild) go.AddComponent<TelemetryDebugOverlay>();

                _run = true;
                _thread = new Thread(FlushLoop) { IsBackground = true, Name = "TelemetryFlush" };
                _thread.Start(cfg.FlushIntervalSeconds);
                Client.OnInstalled();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Telemetry] install failed: " + e.GetType().Name);
                Service = null;
            }
        }

        static string ArgValue(string key)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        static string SafeBuildId() { try { return GameConfig.BuildId; } catch (Exception) { return ""; } }

        static TelemetryConfiguration LoadConfig()
        {
            var cfg = new TelemetryConfiguration();
            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, ConfigRelativePath);
                if (File.Exists(path))
                    JsonConvert.PopulateObject(File.ReadAllText(path), cfg, new JsonSerializerSettings { Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() } });
            }
            catch (Exception e) { Debug.LogWarning("[Telemetry] bad config, using defaults: " + e.GetType().Name); }
            if (string.Equals(cfg.Environment, "auto", StringComparison.OrdinalIgnoreCase))
                cfg.Environment = Application.isEditor || Debug.isDebugBuild ? "development" : "playtest";
            return cfg.ApplyLaunchArgs(Environment.GetCommandLineArgs());
        }

        static void FlushLoop(object seconds)
        {
            int ms = Math.Max(100, (int)(((float)seconds) * 1000f));
            while (_run)
            {
                Wake.WaitOne(ms);
                try { var s = Service; if (s != null) s.Flush(); }
                catch (Exception) { }
            }
        }

        /// <summary>Wakes the flush thread (e.g. after a match ended).</summary>
        public static void RequestFlush() { Wake.Set(); }

        /// <summary>Synchronous final flush on quit; private match data is never released here (a crash/quit loses it by design).</summary>
        public static void Shutdown()
        {
            try
            {
                _run = false;
                Wake.Set();
                var s = Service;
                if (s != null) s.Shutdown(false);
            }
            catch (Exception) { }
        }
    }

    /// <summary>Main-thread pump: per-frame ticks and quit handling.</summary>
    public sealed class TelemetryRunner : MonoBehaviour
    {
        void Update()
        {
            try { TelemetryBootstrap.Client?.Update(); }
            catch (Exception) { }
        }

        void OnApplicationQuit()
        {
            try { TelemetryBootstrap.Client?.OnApplicationQuit(); }
            catch (Exception) { }
            TelemetryBootstrap.Shutdown();
        }
    }
}
