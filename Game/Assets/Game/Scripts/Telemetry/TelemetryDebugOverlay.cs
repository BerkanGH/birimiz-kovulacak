using System;
using System.Collections.Generic;
using System.Text;
using BirimizKovulacak.Core;
using BirimizKovulacak.Telemetry;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// F9 diagnostics (editor and development builds only). Shows the local telemetry state and the player's OWN call
    /// and choice progress. It never shows other players' private data: private_until_match_end events (names, counts
    /// and contents) are hidden while a match runs, and host-only diagnostics appear only on the host.
    /// </summary>
    public sealed class TelemetryDebugOverlay : MonoBehaviour
    {
        bool _visible;
        string _text = "";
        float _nextRefresh;
        GUIStyle _style;

        void Update()
        {
            if (!(Application.isEditor || Debug.isDebugBuild)) return;
            var kb = Keyboard.current;
            if (kb != null && kb.f9Key.wasPressedThisFrame) _visible = !_visible;
            if (_visible && Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.5f;
                try { _text = Build(); } catch (Exception) { _text = "telemetry overlay error"; }
            }
        }

        void OnGUI()
        {
            if (!_visible || !(Application.isEditor || Debug.isDebugBuild)) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 12, richText = false, wordWrap = false };
            GUI.Box(new Rect(8, 8, 520, Mathf.Min(Screen.height - 16, 24 + 15 * (CountLines(_text) + 1))), _text, _style);
        }

        static int CountLines(string s) { int n = 1; foreach (char c in s) if (c == '\n') n++; return n; }

        static string Short(string id) { return string.IsNullOrEmpty(id) ? "-" : (id.Length > 8 ? id.Substring(0, 8) : id); }

        static string Build()
        {
            var svc = TelemetryBootstrap.Service;
            var sb = new StringBuilder(1024);
            sb.Append("TELEMETRY (F9)\n");
            if (svc == null)
            {
                sb.Append("disabled (config or -telemetry off)\n");
                return sb.ToString();
            }
            var cfg = TelemetryBootstrap.Config;
            var ctx = svc.Context;
            var lc = LineClient.I;
            var view = lc != null ? lc.View : null;
            bool isHost = svc.HostMode && ConnectionManager.I != null && ConnectionManager.I.LineHost != null;
            bool matchRunning = view != null && view.Phase != LinePhase.WeekResults && view.Phase != LinePhase.Abandoned && view.Phase != LinePhase.None;
            sb.Append("enabled=").Append(svc.IsEnabled).Append(" consent=").Append(svc.LocalConsent).Append(" env=").Append(ctx.Environment)
              .Append(" local=").Append(cfg.Local).Append(" cloud=").Append(cfg.Cloud).Append(isHost ? " HOST" : " client").Append('\n');
            sb.Append("session=").Append(Short(ctx.SessionId)).Append(" match=").Append(Short(ctx.MatchId)).Append(" group=").Append(string.IsNullOrEmpty(ctx.GroupId) ? "-" : ctx.GroupId).Append('\n');
            sb.Append("day=").Append(ctx.DayIndex).Append(" round=").Append(ctx.RoundIndex).Append(" phase=").Append(string.IsNullOrEmpty(ctx.Phase) ? "-" : ctx.Phase).Append('\n');
            var st = svc.GetStats();
            sb.Append("events: emitted=").Append(st.Emitted).Append(" accepted=").Append(st.Accepted).Append(" written=").Append(st.Written)
              .Append(" suppressed=").Append(st.Suppressed).Append(" rejected=").Append(st.Rejected).Append('\n');
            sb.Append("queue=").Append(st.QueueSize).Append(" dropped=").Append(st.Dropped).Append(" sinkErrors=").Append(st.SinkErrors).Append('\n');
            if (!matchRunning) sb.Append("private buffered=").Append(st.PrivateBuffered).Append('\n');

            // per-name volume: private-class names are hidden while a match runs, host events only for the host
            var names = new List<KeyValuePair<string, int>>();
            foreach (var kv in st.PerName)
            {
                var def = TelemetryCatalog.Find(kv.Key);
                if (def == null) continue;
                if (def.Sensitivity == TelemetrySensitivity.PrivateUntilMatchEnd && matchRunning) continue;
                if (def.Authority == TelemetryAuthority.Host && !isHost) continue;
                names.Add(kv);
            }
            names.Sort((a, b) => b.Value.CompareTo(a.Value));
            sb.Append("volume: ");
            for (int i = 0; i < names.Count && i < 8; i++) sb.Append(names[i].Key).Append('=').Append(names[i].Value).Append(i < 7 ? "  " : "");
            sb.Append('\n');

            if (isHost && TelemetryBootstrap.Host != null)
            {
                double cov = svc.Coverage();
                sb.Append("[host] observation coverage: ").Append(cov < 0 ? "n/a" : cov.ToString("0.00")).Append("  observer=").Append(TelemetryBootstrap.Host.MatchRunning ? "running" : "idle").Append('\n');
            }

            if (view != null && view.Mine != null)
            {
                int calls = view.Mine.CallLog != null ? view.Mine.CallLog.Count : 0, connected = 0;
                double secs = 0;
                if (view.Mine.CallLog != null)
                    foreach (var c in view.Mine.CallLog) if (c.Connected) { connected++; if (c.End >= 0) secs += Math.Max(0, c.End - c.Start); }
                sb.Append("you: calls=").Append(calls).Append(" connected=").Append(connected).Append(" talk=").Append(secs.ToString("0")).Append("s");
                if (view.Task != null) sb.Append(" submitted=").Append(view.Task.Submitted ? "yes" : "no");
                sb.Append('\n');
            }

            sb.Append("recent:\n");
            var dbg = TelemetryBootstrap.DebugSink;
            if (dbg != null)
            {
                var recent = dbg.Snapshot(isHost, matchRunning, 10);
                foreach (var e in recent) sb.Append("  ").Append(e.Name).Append('\n');
            }
            return sb.ToString();
        }
    }
}
