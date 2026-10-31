using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Telemetry
{
    public enum TelemetrySinkKind : byte { Local = 0, Cloud = 1, Debug = 2 }

    /// <summary>
    /// Receives batches of consented events on the flush thread. A sink may throw: the service isolates the failure,
    /// counts it and reports telemetry_sink_error once per fault. Sinks must never touch gameplay state.
    /// </summary>
    public interface ITelemetrySink : IDisposable
    {
        /// <summary>local_jsonl, debug, unity_analytics (anything else is reported as "other").</summary>
        string Name { get; }
        TelemetrySinkKind Kind { get; }
        void Write(IReadOnlyList<TelemetryEvent> batch);
        void Flush();
        /// <summary>Local consent was lowered: drop anything this sink still holds (cloud hand-off queues).</summary>
        void OnConsentWithdrawn() { }
    }

    /// <summary>Counters for the debug overlay and the end-of-session report.</summary>
    public sealed class TelemetryStats
    {
        public long Emitted, Accepted, Written, Suppressed, Rejected, Duplicates;
        public long DroppedOverflow, DroppedPrivateOverflow, DroppedWithdrawal, SinkErrors;
        public int QueueSize, PrivateBuffered;
        public Dictionary<string, int> PerName = new Dictionary<string, int>();
        public long Dropped { get { return DroppedOverflow + DroppedPrivateOverflow + DroppedWithdrawal; } }
    }

    /// <summary>Application-facing telemetry API. Every call is safe: nothing here throws into gameplay.</summary>
    public interface IGameTelemetry
    {
        bool IsEnabled { get; }
        TelemetrySessionContext Context { get; }
        /// <summary>True when this process hosts a match (resolves the authority of "both" events).</summary>
        bool HostMode { get; set; }
        /// <summary>Starts an event; when telemetry is disabled a shared no-op builder is returned (no allocation).</summary>
        TelemetryEventBuilder Begin(string eventName);
        bool Emit(TelemetryEvent e);

        int LocalConsent { get; }
        void SetLocalConsent(int level);
        void SetParticipant(int slot, int consent, string anonymousId, bool isBot);
        void RemoveParticipant(int slot);
        int ConsentOfSlot(int slot);
        /// <summary>Changes whenever the slot's observed state (consent >= 2) flips.</summary>
        int ConsentVersion(int slot);
        string AnonymousIdOfSlot(int slot);

        /// <summary>Moves the host's private_until_match_end buffer into the queue (results / abandon).</summary>
        void ReleasePrivate();
        /// <summary>Discards the private buffer without writing (never used in normal flow).</summary>
        void DiscardPrivate();

        void Flush();
        TelemetryStats GetStats();
    }
}
