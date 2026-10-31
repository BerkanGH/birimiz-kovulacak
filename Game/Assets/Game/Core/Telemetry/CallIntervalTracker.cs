using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Telemetry
{
    public struct CallInterval
    {
        public int CallId, A, B;
        public double Start;
        /// <summary>End time; <see cref="double.PositiveInfinity"/> while the call is still open.</summary>
        public double End;
        /// <summary>The end is a lower bound (disconnect, telemetry stop, match end): the real call may have lasted longer.</summary>
        public bool Censored;
        public bool IsOpen { get { return double.IsPositiveInfinity(End); } }
        public bool Involves(int player) { return A == player || B == player; }
        public int Other(int player) { return A == player ? B : A; }
    }

    /// <summary>
    /// Connected-call intervals per player: clipping to phase/task windows, interval unions (overlapping calls of one
    /// player are never double counted) and censoring when a participant disconnects. Only connected calls are ever
    /// recorded here, so failed, rejected and missed attempts cannot count as participation by construction.
    /// </summary>
    public sealed class CallIntervalTracker
    {
        readonly List<CallInterval> _intervals = new List<CallInterval>();

        public IReadOnlyList<CallInterval> Intervals { get { return _intervals; } }
        public void Clear() { _intervals.Clear(); }

        public bool Open(int callId, int a, int b, double start)
        {
            if (IndexOf(callId) >= 0) return false; // duplicate CallConnected (both parties receive it)
            _intervals.Add(new CallInterval { CallId = callId, A = a, B = b, Start = start, End = double.PositiveInfinity });
            return true;
        }

        public int IndexOf(int callId)
        {
            for (int i = _intervals.Count - 1; i >= 0; i--) if (_intervals[i].CallId == callId) return i;
            return -1;
        }

        public bool IsOpen(int callId)
        {
            int i = IndexOf(callId);
            return i >= 0 && _intervals[i].IsOpen;
        }

        /// <summary>Closes an open call. Returns false when unknown or already closed (the second CallEnded of a pair).</summary>
        public bool Close(int callId, double end, bool censored, out CallInterval closed)
        {
            closed = default(CallInterval);
            int i = IndexOf(callId);
            if (i < 0 || !_intervals[i].IsOpen) return false;
            var c = _intervals[i];
            c.End = Math.Max(c.Start, end);
            c.Censored = censored;
            _intervals[i] = c;
            closed = c;
            return true;
        }

        /// <summary>Marks every open call of <paramref name="player"/> as ended at <paramref name="at"/> and censored.</summary>
        public int CensorPlayer(int player, double at, List<CallInterval> closedOut)
        {
            int n = 0;
            for (int i = 0; i < _intervals.Count; i++)
            {
                var c = _intervals[i];
                if (!c.IsOpen || !c.Involves(player)) continue;
                c.End = Math.Max(c.Start, at);
                c.Censored = true;
                _intervals[i] = c;
                if (closedOut != null) closedOut.Add(c);
                n++;
            }
            return n;
        }

        public int CloseAll(double at, bool censored, List<CallInterval> closedOut)
        {
            int n = 0;
            for (int i = 0; i < _intervals.Count; i++)
            {
                var c = _intervals[i];
                if (!c.IsOpen) continue;
                c.End = Math.Max(c.Start, at);
                c.Censored = censored;
                _intervals[i] = c;
                if (closedOut != null) closedOut.Add(c);
                n++;
            }
            return n;
        }

        /// <summary>Length of [Start,End] clipped to [from,to]; open intervals end at <paramref name="now"/>.</summary>
        public static double ClippedLength(CallInterval c, double from, double to, double now)
        {
            double s = Math.Max(c.Start, from);
            double e = Math.Min(c.IsOpen ? now : c.End, to);
            return e > s ? e - s : 0;
        }

        /// <summary>Union of the player's connected intervals inside the window (seconds).</summary>
        public double UnionSeconds(int player, double from, double to, double now)
        {
            return UnionSeconds(p => p == player, from, to, now);
        }

        /// <summary>Union over all calls involving any player for which <paramref name="include"/> is true.</summary>
        public double UnionSeconds(Func<int, bool> include, double from, double to, double now)
        {
            var spans = new List<double[]>();
            foreach (var c in _intervals)
            {
                if (!include(c.A) && !include(c.B)) continue;
                double s = Math.Max(c.Start, from), e = Math.Min(c.IsOpen ? now : c.End, to);
                if (e > s) spans.Add(new[] { s, e });
            }
            return UnionLength(spans);
        }

        public static double UnionLength(List<double[]> spans)
        {
            if (spans.Count == 0) return 0;
            spans.Sort((x, y) => x[0].CompareTo(y[0]));
            double total = 0, curS = spans[0][0], curE = spans[0][1];
            for (int i = 1; i < spans.Count; i++)
            {
                if (spans[i][0] <= curE) curE = Math.Max(curE, spans[i][1]);
                else { total += curE - curS; curS = spans[i][0]; curE = spans[i][1]; }
            }
            return total + (curE - curS);
        }

        /// <summary>True when the player had a connected call overlapping the window (any positive-or-instant overlap).</summary>
        public bool HasConnectedOverlap(int player, double from, double to, double now)
        {
            foreach (var c in _intervals)
            {
                if (!c.Involves(player)) continue;
                double end = c.IsOpen ? now : c.End;
                if (c.Start <= to && end >= from) return true;
            }
            return false;
        }

        /// <summary>Earliest connect time of a call of the player at or after <paramref name="from"/> (or -1).</summary>
        public double FirstStart(int player, double from, double to)
        {
            double best = -1;
            foreach (var c in _intervals)
            {
                if (!c.Involves(player) || c.Start < from || c.Start > to) continue;
                if (best < 0 || c.Start < best) best = c.Start;
            }
            return best;
        }

        /// <summary>Connected calls of the player overlapping the window, and their distinct partners.</summary>
        public int CountOverlap(int player, double from, double to, double now, out int distinctPartners)
        {
            int n = 0, mask = 0;
            foreach (var c in _intervals)
            {
                if (!c.Involves(player)) continue;
                double end = c.IsOpen ? now : c.End;
                if (c.Start <= to && end >= from) { n++; int o = c.Other(player); if (o >= 0 && o < 31) mask |= 1 << o; }
            }
            distinctPartners = 0;
            for (int m = mask; m != 0; m &= m - 1) distinctPartners++;
            return n;
        }

        public int CountConnected(int player, double from, double to)
        {
            int n = 0;
            foreach (var c in _intervals) if (c.Involves(player) && c.Start >= from && c.Start <= to) n++;
            return n;
        }

        /// <summary>Longest stretch of [from,to] covered by neither a connected interval nor one of the given instants.</summary>
        public double LongestGap(int player, double from, double to, double now, List<double> instants, List<double[]> extraSpans = null)
        {
            var spans = new List<double[]>();
            foreach (var c in _intervals)
            {
                if (!c.Involves(player)) continue;
                double s = Math.Max(c.Start, from), e = Math.Min(c.IsOpen ? now : c.End, to);
                if (e >= s) spans.Add(new[] { s, e });
            }
            if (instants != null) foreach (var t in instants) if (t >= from && t <= to) spans.Add(new[] { t, t });
            if (extraSpans != null) foreach (var x in extraSpans) { double s = Math.Max(x[0], from), e = Math.Min(x[1], to); if (e >= s) spans.Add(new[] { s, e }); }
            spans.Sort((x, y) => x[0].CompareTo(y[0]));
            double gap = 0, cursor = from;
            foreach (var sp in spans)
            {
                if (sp[0] > cursor) gap = Math.Max(gap, sp[0] - cursor);
                cursor = Math.Max(cursor, sp[1]);
            }
            return Math.Max(gap, to - cursor);
        }
    }
}
