using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BirimizKovulacak.Telemetry;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Analytics consent and local data controls (Settings → Privacy, first-launch prompt). The anonymous id
    /// ("p-" + 128-bit random) exists only while consent is 2 or 3 and is never derived from anything about the user.
    /// </summary>
    public sealed class TelemetryConsentStore : ITelemetryPrivacy
    {
        const string ConsentKey = "telemetry.consent";
        const string IdKey = "telemetry.anon_id";

        GameTelemetryService _svc;
        TelemetryConfiguration _cfg;

        /// <summary>Raised after the level or id changed (NetSession should forward it to the host).</summary>
        public static event Action ConsentChanged;

        public int Consent { get; private set; }
        public bool NeedsConsentPrompt { get { return Consent == 0; } }
        public bool CloudAvailable { get { return _cfg != null && _cfg.Cloud && TelemetryRegistry.HasCloudSink; } }
        public string AnonymousId { get; private set; } = "";
        public string LogDirectory { get { return TelemetryBootstrap.LogDirectory; } }
        public bool MomentMarkerAvailable { get { return Consent >= 2 && _svc != null && _svc.IsEnabled; } }

        /// <summary>Automation (-telemetryconsent N with -bot/-autohost/-autojoin): consent for this process only, never persisted.</summary>
        public bool Ephemeral { get; private set; }

        public TelemetryConsentStore()
        {
            int automation = AutomationConsent();
            if (automation >= 0)
            {
                // Several local test processes share PlayerPrefs: give each its own in-memory consent and id.
                Ephemeral = true;
                Consent = automation;
                AnonymousId = Consent >= 2 ? NewId() : "";
                return;
            }
            try { Consent = Mathf.Clamp(PlayerPrefs.GetInt(ConsentKey, 0), 0, 3); } catch (Exception) { Consent = 0; }
            if (Consent >= 2) AnonymousId = LoadOrCreateId(); else DeleteStoredId();
        }

        static int AutomationConsent()
        {
            var a = LaunchArgs.Current;
            if (a == null || !(a.Bot || a.AutoHost || !string.IsNullOrEmpty(a.AutoJoinAddress))) return -1;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], "-telemetryconsent", StringComparison.OrdinalIgnoreCase))
                {
                    int v;
                    if (int.TryParse(args[i + 1], out v)) return Mathf.Clamp(v, 0, 3);
                }
            return -1;
        }

        static string NewId()
        {
            var b = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            var sb = new StringBuilder("p-");
            foreach (var x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        internal void Bind(GameTelemetryService svc, TelemetryConfiguration cfg)
        {
            _svc = svc;
            _cfg = cfg;
            Apply(false);
        }

        public void SetConsent(int level)
        {
            level = Mathf.Clamp(level, 0, 3);
            int old = Consent;
            Consent = level;
            if (Ephemeral)
            {
                if (level >= 2) { if (AnonymousId.Length == 0) AnonymousId = NewId(); }
                else AnonymousId = "";
                Apply(old != level);
                return;
            }
            try { PlayerPrefs.SetInt(ConsentKey, level); PlayerPrefs.Save(); } catch (Exception) { }
            if (level >= 2) { if (AnonymousId.Length == 0) AnonymousId = LoadOrCreateId(); }
            else { AnonymousId = ""; DeleteStoredId(); }
            Apply(old != level);
        }

        void Apply(bool changed)
        {
            if (_svc != null)
            {
                _svc.Context.LocalPlayerId = AnonymousId;
                _svc.SetLocalConsent(Consent); // withdrawal purges the queue inside the service
                try { var a = TelemetryRegistry.CloudConsentChanged; if (a != null) a(Consent); } catch (Exception) { }
                if (Consent >= 2) TelemetryBootstrap.Client?.OnConsentGranted();
            }
            if (changed) { try { var h = ConsentChanged; if (h != null) h(); } catch (Exception) { } }
        }

        static string LoadOrCreateId()
        {
            try
            {
                string id = PlayerPrefs.GetString(IdKey, "");
                if (TelemetryNames.IsValidId(IdKind.PlayerId, id)) return id;
                var b = new byte[16];
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
                var sb = new StringBuilder("p-");
                foreach (var x in b) sb.Append(x.ToString("x2"));
                id = sb.ToString();
                PlayerPrefs.SetString(IdKey, id);
                PlayerPrefs.Save();
                return id;
            }
            catch (Exception) { return ""; }
        }

        static void DeleteStoredId()
        {
            try { PlayerPrefs.DeleteKey(IdKey); } catch (Exception) { }
        }

        public void ResetAnonymousId()
        {
            DeleteStoredId();
            AnonymousId = Consent >= 2 ? LoadOrCreateId() : "";
            if (_svc != null) _svc.Context.LocalPlayerId = AnonymousId;
            try { var h = ConsentChanged; if (h != null) h(); } catch (Exception) { }
        }

        public bool DeleteLocalData()
        {
            bool ok = true;
            try
            {
                if (_svc != null) _svc.Flush();
                string dir = LogDirectory;
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    foreach (var f in Directory.GetFiles(dir, "events_*.jsonl"))
                    {
                        try { File.Delete(f); } catch (Exception) { ok = false; }
                    }
                }
                try { var d = TelemetryRegistry.RequestCloudDeletion; if (d != null) d(); } catch (Exception) { ok = false; }
            }
            catch (Exception) { ok = false; }
            return ok;
        }

        public void OpenLogFolder()
        {
            try
            {
                string dir = LogDirectory;
                if (string.IsNullOrEmpty(dir)) return;
                Directory.CreateDirectory(dir);
                Application.OpenURL("file:///" + dir.Replace('\\', '/'));
            }
            catch (Exception) { }
        }

        public string ExportLogs()
        {
            try
            {
                if (_svc != null) _svc.Flush();
                string dir = LogDirectory;
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
                string target = Path.Combine(Path.GetDirectoryName(dir) ?? dir, "export_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".zip");
                using (var fs = new FileStream(target, FileMode.Create, FileAccess.Write))
                using (var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
                {
                    foreach (var f in Directory.GetFiles(dir, "events_*.jsonl"))
                    {
                        var entry = zip.CreateEntry(Path.GetFileName(f), System.IO.Compression.CompressionLevel.Optimal);
                        using (var es = entry.Open())
                        using (var src = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            src.CopyTo(es);
                    }
                }
                return target;
            }
            catch (Exception) { return null; }
        }
    }
}
