using System;

namespace BirimizKovulacak.Voice
{
    public enum JitterPull : byte
    {
        /// <summary>No talk spurt in progress and nothing buffered.</summary>
        Idle = 0,
        /// <summary>Playout delay not reached yet, or the next frame is not here and concealing is not urgent.</summary>
        Waiting = 1,
        /// <summary>The next frame is available: decode <see cref="VoiceJitterBuffer.PayloadAt"/>.</summary>
        Frame = 2,
        /// <summary>The next frame was lost but the following packet is buffered: decode it with FEC.</summary>
        Fec = 3,
        /// <summary>The next frame was lost (or the delay grows by one frame): run packet loss concealment.</summary>
        Lost = 4,
    }

    /// <summary>
    /// Per-speaker adaptive jitter buffer of encoded 20 ms frames, driven by the consumer (one Pull per 20 ms of
    /// audio actually needed). Handles reordering, duplicates, late packets, loss (FEC/PLC), sequence wrap-around,
    /// end-of-speech and latency creep.
    /// <para>Playout delay: the peak relative transit delay of recent packets (late ones included) + a safety
    /// margin, at least <see cref="BaseDelayMs"/> (60 ms). It is chosen when a spurt starts, grows mid-spurt by
    /// one concealed frame at a time when packets start arriving later, and shrinks by skipping frames when a
    /// burst leaves too much queued.</para>
    /// Fixed storage, no allocations after construction. Main thread only.
    /// </summary>
    public sealed class VoiceJitterBuffer
    {
        public const int Capacity = 32; // 640 ms of audio

        // Tuning.
        public int BaseDelayMs = 60;
        public int MinDelayMs = 40;
        public int MaxDelayMs = 240;
        public int SafetyMs = 10;
        /// <summary>Concealment frames after the last packet before the spurt is considered over.</summary>
        public int MaxConcealFrames = 3;
        /// <summary>Extra frames above the target before old frames are skipped to cut latency.</summary>
        public int SlackFrames = 4;
        /// <summary>Per-packet decay of the peak delay estimate (0.997 → halves after ~4.6 s of speech).</summary>
        public double PeakDecay = 0.997;
        /// <summary>
        /// Frames the consumer pulls at once when playout starts (to fill its output ring). Added to the start
        /// condition so the prefetch does not eat the jitter margin. Set by <see cref="VoicePlaybackChannel"/>.
        /// </summary>
        public int PrefetchFrames;

        readonly byte[][] _payload = new byte[Capacity][];
        readonly int[] _length = new int[Capacity];
        readonly ushort[] _seq = new ushort[Capacity];
        readonly byte[] _flags = new byte[Capacity];
        readonly bool[] _present = new bool[Capacity];
        readonly double[] _arrival = new double[Capacity];
        int _count;

        bool _playing;
        ushort _nextSeq;
        bool _hasHistory;      // a spurt was played before: packets behind _nextSeq are late
        int _missStreak;
        int _targetFrames = 3;

        // Delay estimation.
        bool _hasRef;
        ushort _refSeq;
        long _refExt;
        bool _lastWasEnd;
        double _lastArrivalMs;
        double _baseTransit;
        double _peakRelMs;
        double _jitterMs;

        // Counters (cumulative).
        public long Received, Late, Duplicates, Overflow, Played, Concealed, FecRecovered, Skipped, Stretched;

        public VoiceJitterBuffer()
        {
            for (int i = 0; i < Capacity; i++) _payload[i] = new byte[VoiceFormat.MaxPayloadBytes];
        }

        public bool IsPlaying => _playing;
        public int BufferedCount => _count;
        /// <summary>Playout delay of the current/last spurt in frames.</summary>
        public int TargetFrames => _targetFrames;
        /// <summary>RFC 3550 smoothed inter-arrival jitter (ms), for statistics.</summary>
        public double JitterMs => _jitterMs;
        /// <summary>Peak relative transit delay (ms) that drives the playout delay.</summary>
        public double PeakDelayMs => _peakRelMs;

        /// <summary>True when the frame the next <see cref="Pull"/> wants (mid-spurt) is already buffered.</summary>
        public bool NextReady
        {
            get
            {
                int i = _nextSeq & (Capacity - 1);
                return _present[i] && _seq[i] == _nextSeq;
            }
        }

        public byte[] PayloadAt(int index) { return _payload[index]; }
        public int LengthAt(int index) { return _length[index]; }
        public VoiceFrameFlags FlagsAt(int index) { return (VoiceFrameFlags)_flags[index]; }

        /// <summary>Adds a received packet. Returns false when it was dropped (late, duplicate, malformed).</summary>
        public bool Insert(ushort seq, VoiceFrameFlags flags, byte[] src, int offset, int length, double nowMs)
        {
            if (length < 0 || length > VoiceFormat.MaxPayloadBytes) return false;
            Received++;
            Observe(seq, flags, nowMs); // late packets are the best evidence for a longer delay

            if (_playing || _hasHistory)
            {
                int d = VoiceSeq.Diff(seq, _nextSeq);
                if (d < 0)
                {
                    if (_playing || d > -Capacity * 4) { Late++; return false; }
                    // Far behind an old spurt: the sender restarted its sequence (reconnect). Start over.
                    _hasHistory = false;
                }
                else if (d >= Capacity)
                {
                    // Far ahead: the stream jumped (long gap, reconnect). Resynchronise on this packet.
                    Overflow += _count;
                    Flush();
                    _playing = false;
                    _hasHistory = false;
                }
            }
            else if (_count > 0)
            {
                // Waiting state: keep the window coherent around the oldest buffered packet.
                int d = VoiceSeq.Diff(seq, OldestSeq());
                if (d >= Capacity || d <= -Capacity) { Overflow += _count; Flush(); }
            }

            int i = seq & (Capacity - 1);
            if (_present[i])
            {
                if (_seq[i] == seq) { Duplicates++; return false; }
                Overflow++;
                _present[i] = false;
                _count--;
            }
            if (length > 0) Buffer.BlockCopy(src, offset, _payload[i], 0, length);
            _length[i] = length;
            _seq[i] = seq;
            _flags[i] = (byte)flags;
            _arrival[i] = nowMs;
            _present[i] = true;
            _count++;
            return true;
        }

        void Observe(ushort seq, VoiceFrameFlags flags, double nowMs)
        {
            bool end = (flags & VoiceFrameFlags.EndOfSpeech) != 0;
            if (!_hasRef)
            {
                _hasRef = true;
                _refSeq = seq;
                _refExt = 0;
                _baseTransit = nowMs;
                _lastArrivalMs = nowMs;
                _lastWasEnd = end;
                return;
            }
            int ds = VoiceSeq.Diff(seq, _refSeq);
            long ext = _refExt + ds;
            double transit = nowMs - ext * (double)VoiceFormat.FrameMs;
            // The sequence keeps counting across pauses, so every spurt gets a fresh transit baseline.
            bool newSpurt = ds > 0 && (_lastWasEnd || ds >= 25 || nowMs - _lastArrivalMs > 400);
            if (newSpurt || transit < _baseTransit) _baseTransit = transit;
            else _baseTransit += 0.01; // tolerate capture-clock drift
            double rel = transit - _baseTransit;
            if (rel > MaxDelayMs * 2) rel = MaxDelayMs * 2;
            _peakRelMs = Math.Max(rel, _peakRelMs * PeakDecay);
            if (ds > 0)
            {
                if (!newSpurt)
                {
                    double d = (nowMs - _lastArrivalMs) - ds * (double)VoiceFormat.FrameMs;
                    _jitterMs += (Math.Abs(d) - _jitterMs) / 16.0;
                }
                _refSeq = seq;
                _refExt = ext;
                _lastArrivalMs = nowMs;
                _lastWasEnd = end;
            }
        }

        /// <summary>Playout delay (frames) a spurt starting now would use.</summary>
        public int EstimateTargetFrames()
        {
            double ms = Math.Max(BaseDelayMs, _peakRelMs + SafetyMs);
            ms = Math.Max(MinDelayMs, Math.Min(MaxDelayMs, ms));
            return Math.Max(1, (int)Math.Ceiling(ms / VoiceFormat.FrameMs - 1e-9));
        }

        ushort OldestSeq()
        {
            bool found = false;
            ushort best = 0;
            for (int i = 0; i < Capacity; i++)
            {
                if (!_present[i]) continue;
                if (!found || VoiceSeq.Diff(_seq[i], best) < 0) { best = _seq[i]; found = true; }
            }
            return best;
        }

        bool AnyEndOfSpeech()
        {
            for (int i = 0; i < Capacity; i++)
                if (_present[i] && (_flags[i] & (byte)VoiceFrameFlags.EndOfSpeech) != 0) return true;
            return false;
        }

        /// <summary>
        /// Called by the consumer whenever it needs the next 20 ms. When the next frame has not arrived and
        /// <paramref name="allowConceal"/> is false (the consumer still has enough audio queued), returns
        /// <see cref="JitterPull.Waiting"/> so a slightly late packet still gets played. For Frame/Fec,
        /// <paramref name="index"/> addresses the payload to decode (valid until the next Insert/Pull).
        /// </summary>
        public JitterPull Pull(double nowMs, bool allowConceal, out int index)
        {
            index = -1;
            if (!_playing)
            {
                if (_count == 0) return JitterPull.Idle;
                ushort oldest = OldestSeq();
                int oi = oldest & (Capacity - 1);
                _targetFrames = EstimateTargetFrames();
                int startFrames = _targetFrames + PrefetchFrames;
                bool deepEnough = _count > startFrames;
                bool waitedEnough = nowMs - _arrival[oi] >= startFrames * VoiceFormat.FrameMs;
                if (!deepEnough && !waitedEnough && !AnyEndOfSpeech()) return JitterPull.Waiting;
                _playing = true;
                _hasHistory = true;
                _nextSeq = oldest;
                _missStreak = 0;
            }
            else if (EstimateTargetFrames() > _targetFrames && _missStreak == 0)
            {
                // Packets started arriving later than the current delay allows: grow by one frame now.
                _targetFrames++;
                Stretched++;
                return JitterPull.Lost;
            }

            // Latency control: a burst left too much audio queued → skip the oldest frames.
            if (_count > _targetFrames + SlackFrames)
            {
                while (_count > _targetFrames)
                {
                    int si = _nextSeq & (Capacity - 1);
                    if (_present[si] && _seq[si] == _nextSeq) { _present[si] = false; _count--; Skipped++; }
                    _nextSeq++;
                }
            }

            int i = _nextSeq & (Capacity - 1);
            if (_present[i] && _seq[i] == _nextSeq)
            {
                _present[i] = false;
                _count--;
                _nextSeq++;
                _missStreak = 0;
                Played++;
                if ((_flags[i] & (byte)VoiceFrameFlags.EndOfSpeech) != 0 && _count == 0) _playing = false;
                index = i;
                return JitterPull.Frame;
            }

            if (!allowConceal) return JitterPull.Waiting;

            if (_count > 0)
            {
                // A hole with later packets queued. Long holes (sender paused, out of range) are jumped over.
                _missStreak++;
                if (_missStreak > _targetFrames + MaxConcealFrames)
                {
                    _nextSeq = OldestSeq();
                    _missStreak = 0;
                    return Pull(nowMs, allowConceal, out index);
                }
                ushort following = (ushort)(_nextSeq + 1);
                int fi = following & (Capacity - 1);
                _nextSeq++;
                Concealed++;
                if (_present[fi] && _seq[fi] == following)
                {
                    FecRecovered++;
                    index = fi;
                    return JitterPull.Fec;
                }
                return JitterPull.Lost;
            }

            // Nothing queued: conceal a little, then end the spurt.
            _missStreak++;
            if (_missStreak > MaxConcealFrames)
            {
                _playing = false;
                return JitterPull.Idle;
            }
            _nextSeq++;
            Concealed++;
            return JitterPull.Lost;
        }

        /// <summary>Pull that always conceals a missing frame (tests / simple consumers).</summary>
        public JitterPull Pull(double nowMs, out int index) { return Pull(nowMs, true, out index); }

        void Flush()
        {
            for (int i = 0; i < Capacity; i++) _present[i] = false;
            _count = 0;
        }

        public void Reset()
        {
            Flush();
            _playing = false;
            _hasHistory = false;
            _missStreak = 0;
            _hasRef = false;
            _jitterMs = 0;
            _peakRelMs = 0;
        }
    }
}
