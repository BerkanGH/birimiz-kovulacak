using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Telemetry
{
    /// <summary>
    /// Bounded in-memory ring for the development overlay. It only ever sees events that already passed the consent
    /// gate and the private_until_match_end buffer. <see cref="Snapshot"/> additionally hides host-only diagnostics
    /// from non-hosts and every private-class event while a match is running.
    /// </summary>
    public sealed class DebugTelemetrySink : ITelemetrySink
    {
        readonly TelemetryEvent[] _ring;
        readonly object _lock = new object();
        int _next, _count;

        public DebugTelemetrySink(int capacity = 256) { _ring = new TelemetryEvent[Math.Max(8, capacity)]; }

        public string Name { get { return "debug"; } }
        public TelemetrySinkKind Kind { get { return TelemetrySinkKind.Debug; } }
        public int Capacity { get { return _ring.Length; } }

        public void Write(IReadOnlyList<TelemetryEvent> batch)
        {
            lock (_lock)
            {
                for (int i = 0; i < batch.Count; i++)
                {
                    _ring[_next] = batch[i];
                    _next = (_next + 1) % _ring.Length;
                    if (_count < _ring.Length) _count++;
                }
            }
        }

        public void Flush() { }
        public void Dispose() { lock (_lock) { Array.Clear(_ring, 0, _ring.Length); _count = 0; } }

        /// <summary>Newest last. <paramref name="isHost"/>: show host-authority diagnostics; <paramref name="matchRunning"/>: hide private-class events.</summary>
        public List<TelemetryEvent> Snapshot(bool isHost, bool matchRunning, int max = 64)
        {
            var list = new List<TelemetryEvent>();
            lock (_lock)
            {
                for (int i = 0; i < _count && list.Count < _ring.Length; i++)
                {
                    int idx = (_next - _count + i + _ring.Length * 2) % _ring.Length;
                    var e = _ring[idx];
                    if (e == null) continue;
                    if (!Visible(e, isHost, matchRunning)) continue;
                    list.Add(e);
                }
            }
            if (list.Count > max) list.RemoveRange(0, list.Count - max);
            return list;
        }

        public static bool Visible(TelemetryEvent e, bool isHost, bool matchRunning)
        {
            if (e.IsPrivate && matchRunning) return false;
            if (e.Source == TelemetryAuthority.Host && !isHost) return false;
            return true;
        }
    }
}
