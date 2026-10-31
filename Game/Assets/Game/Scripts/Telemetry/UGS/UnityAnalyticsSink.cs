#if BK_UGS_ANALYTICS
// Compiled only when the com.unity.services.analytics package is installed (see BirimizKovulacak.Telemetry.UGS.asmdef).
// NOT compiled or run against the real package in this repository's validation (the package is not installed):
// API calls follow the SDK 6.0 reference (IAnalyticsService: StartDataCollection, StopDataCollection, RequestDataDeletion,
// RecordEvent(Event), Flush; CustomEvent(string) with indexer). SDK 6.1+ on Unity 6.2+ moves consent to the Developer
// Data framework (EndUserConsent): see docs/ANALYTICS_SETUP.md before enabling the cloud route.
#pragma warning disable CS0618
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using BirimizKovulacak.Telemetry;
using Unity.Services.Analytics;
using Unity.Services.Core;
using UnityEngine;

namespace BirimizKovulacak.Telemetry.UGS
{
    /// <summary>
    /// Low-cardinality cloud subset (catalog events routed "local+cloud", only parameters marked cloud, durations and
    /// scores bucketed). Receives only consent-3 events from the service. Events are queued here (flush thread) and sent
    /// from the main thread by <see cref="UgsPump"/>.
    /// </summary>
    public sealed class UnityAnalyticsSink : ITelemetrySink
    {
        sealed class Pending { public string Name; public Dictionary<string, object> Params; }

        static readonly ConcurrentQueue<Pending> Queue = new ConcurrentQueue<Pending>();
        const int MaxQueue = 256;
        static bool _initStarted, _ready, _collecting;

        public string Name { get { return "unity_analytics"; } }
        public TelemetrySinkKind Kind { get { return TelemetrySinkKind.Cloud; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            TelemetryRegistry.RegisterSinkFactory(() => new UnityAnalyticsSink());
            TelemetryRegistry.RequestCloudDeletion = () => UgsPump.EnqueueAction(() => { try { AnalyticsService.Instance.RequestDataDeletion(); } catch (Exception) { } });
            TelemetryRegistry.CloudConsentChanged = level => UgsPump.EnqueueAction(() => ApplyConsent(level));
        }

        public void Write(IReadOnlyList<TelemetryEvent> batch)
        {
            for (int i = 0; i < batch.Count; i++)
            {
                if (Queue.Count >= MaxQueue) break; // bounded: drop rather than grow
                var p = Convert(batch[i]);
                if (p != null) Queue.Enqueue(p);
            }
            UgsPump.EnsureExists();
        }

        public void Flush() { }
        public void Dispose() { }

        static string Bucket(double v, double[] edges)
        {
            for (int i = 0; i < edges.Length; i++) if (v < edges[i]) return "lt_" + edges[i].ToString("0");
            return "ge_" + edges[edges.Length - 1].ToString("0");
        }

        static readonly double[] Seconds = { 10, 30, 60, 120, 300 };
        static readonly double[] Output = { 25, 50, 75, 100 };
        static readonly double[] Permille = { 400, 550, 650, 750, 900 };

        static Pending Convert(TelemetryEvent e)
        {
            if (!e.RouteCloud) return null;
            var d = new Dictionary<string, object>();
            var ps = e.Def.Params;
            for (int i = 0; i < ps.Length; i++)
            {
                if (!ps[i].Cloud || !e.Values[i].HasValue) continue;
                var v = e.Values[i];
                string n = ps[i].Name;
                switch (v.Kind)
                {
                    case ValueKind.Int:
                        if (n == "max_output") d["max_output_bucket"] = Bucket(v.L, Output);
                        else if (n == "final_permille") d["final_permille_bucket"] = Bucket(v.L, Permille);
                        else d[n] = (int)v.L;
                        break;
                    case ValueKind.Float:
                        if (n.EndsWith("_seconds")) d[n.Replace("_seconds", "_bucket")] = Bucket(v.D, Seconds);
                        else d[n] = (float)v.D;
                        break;
                    case ValueKind.Bool: d[n] = v.L != 0; break;
                    default: d[n] = v.S; break;
                }
            }
            if (e.Def.Name == "phase_exited" && !string.IsNullOrEmpty(e.Phase)) d["phase"] = e.Phase;
            if (e.Def.Name == "round_finished" && !string.IsNullOrEmpty(e.TaskId)) d["task_id"] = e.TaskId;
            if (!string.IsNullOrEmpty(e.Environment)) d["environment"] = e.Environment;
            if (!string.IsNullOrEmpty(e.RulesetVersion)) d["ruleset_version"] = e.RulesetVersion;
            return new Pending { Name = e.Def.Name, Params = d };
        }

        internal static async void Initialize()
        {
            if (_initStarted) return;
            _initStarted = true;
            try
            {
                if (UnityServices.State == ServicesInitializationState.Uninitialized) await UnityServices.InitializeAsync();
                _ready = UnityServices.State == ServicesInitializationState.Initialized;
            }
            catch (Exception) { _ready = false; }
        }

        internal static void ApplyConsent(int level)
        {
            try
            {
                Initialize();
                if (!_ready) return;
                if (level >= 3 && !_collecting) { AnalyticsService.Instance.StartDataCollection(); _collecting = true; }
                else if (level < 3 && _collecting) { AnalyticsService.Instance.StopDataCollection(); _collecting = false; }
            }
            catch (Exception) { }
        }

        internal static void Drain()
        {
            if (!_initStarted) Initialize();
            if (!_ready || !_collecting) return;
            Pending p;
            int n = 0;
            while (n++ < 32 && Queue.TryDequeue(out p))
            {
                try
                {
                    var ev = new CustomEvent(p.Name);
                    foreach (var kv in p.Params) ev[kv.Key] = kv.Value;
                    AnalyticsService.Instance.RecordEvent(ev);
                }
                catch (Exception) { }
            }
        }
    }

    /// <summary>Main-thread pump for the Unity Analytics SDK (initialization, consent, event submission).</summary>
    public sealed class UgsPump : MonoBehaviour
    {
        static UgsPump _instance;
        static readonly ConcurrentQueue<Action> Actions = new ConcurrentQueue<Action>();

        internal static void EnsureExists()
        {
            // may be called from the flush thread: creation happens on the next main-thread action instead
            if (_instance != null) return;
            Actions.Enqueue(Create);
        }

        internal static void EnqueueAction(Action a) { Actions.Enqueue(a); }

        static void Create()
        {
            if (_instance != null) return;
            var go = new GameObject("UgsAnalyticsPump") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<UgsPump>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { Create(); }

        void Update()
        {
            Action a;
            int n = 0;
            while (n++ < 16 && Actions.TryDequeue(out a)) { try { a(); } catch (Exception) { } }
            UnityAnalyticsSink.Drain();
        }
    }
}
#endif
