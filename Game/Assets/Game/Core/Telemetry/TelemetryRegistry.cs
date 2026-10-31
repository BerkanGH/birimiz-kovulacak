using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Telemetry
{
    /// <summary>
    /// Late-bound extension point so optional assemblies (UnityAnalyticsSink, compiled only when the Unity Analytics
    /// package is installed) can contribute a sink without the Game assembly referencing them.
    /// </summary>
    public static class TelemetryRegistry
    {
        static readonly List<Func<ITelemetrySink>> Factories = new List<Func<ITelemetrySink>>();

        /// <summary>Asks the cloud sink (when present) to delete this install's cloud data; set by the optional assembly.</summary>
        public static Action RequestCloudDeletion;
        /// <summary>Tells the cloud sink the local consent level changed (start/stop data collection); set by the optional assembly.</summary>
        public static Action<int> CloudConsentChanged;

        public static void RegisterSinkFactory(Func<ITelemetrySink> factory)
        {
            if (factory == null) return;
            lock (Factories) { if (!Factories.Contains(factory)) Factories.Add(factory); }
        }

        public static bool HasCloudSink { get { lock (Factories) return Factories.Count > 0; } }

        public static List<ITelemetrySink> CreateSinks()
        {
            var list = new List<ITelemetrySink>();
            Func<ITelemetrySink>[] copy;
            lock (Factories) copy = Factories.ToArray();
            foreach (var f in copy)
            {
                try { var s = f(); if (s != null) list.Add(s); }
                catch (Exception) { }
            }
            return list;
        }
    }
}
