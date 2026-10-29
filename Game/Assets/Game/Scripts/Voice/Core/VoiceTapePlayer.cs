using System;

namespace BirimizKovulacak.Voice
{
    /// <summary>
    /// Client-side meeting tape: plays a call recording streamed by the host (frames flagged
    /// <see cref="VoiceFrameFlags.Recording"/>, slot = original speaker). Two independent streams (a call has two
    /// sides; each needs its own Opus decoder and jitter buffer), mixed into one 2D output through
    /// <see cref="TapeFilter"/> (band-pass, saturation, wow/flutter, hiss that runs while the tape runs).
    /// <para>Kept apart from the live per-slot channels on purpose: the original speaker may talk live in the
    /// meeting while their recorded voice plays, and the two sequence spaces must never meet.</para>
    /// <para>Lifecycle: <see cref="Begin"/> (control message or first frame) → frames → <see cref="End"/> (host says
    /// done) → drains until both streams are idle → Idle (hiss fades out over the last frame, no click).
    /// A client that joins mid-playback simply begins on the late start message and hears the rest.</para>
    /// Exposes <see cref="Level01"/> (voice only, 0..1, smoothed) and <see cref="CurrentSpeaker"/> for the UI.
    /// Main thread only (except <see cref="Ring"/>). No allocations per frame.
    /// </summary>
    public sealed class VoiceTapePlayer : IVoicePcmSource, IDisposable
    {
        public const int Streams = 2;
        enum State : byte { Idle, Playing, Draining }

        readonly VoiceJitterBuffer[] _jitter = new VoiceJitterBuffer[Streams];
        readonly IVoiceDecoder[] _decoder = new IVoiceDecoder[Streams];
        readonly int[] _slot = new int[Streams];
        readonly float[] _streamLevel = new float[Streams];
        readonly float[] _pcm, _mix;
        readonly int _frame;
        readonly PcmRing _ring;
        readonly TapeFilter _filter;
        State _state;
        double _drainDeadlineMs;
        double _speakerHoldUntilMs;
        float _level;
        int _speaker = -1;

        /// <summary>Output gain (master volume × deafen; the owner sets it every frame).</summary>
        public float Volume = 1f;
        /// <summary>After <see cref="End"/>, give up waiting for late frames after this long.</summary>
        public double DrainTimeoutMs = 1500;
        /// <summary>Stream level above which its slot becomes <see cref="CurrentSpeaker"/>.</summary>
        public float SpeakerThreshold = 0.15f;
        /// <summary>How long the last speaker stays current through short pauses (ms).</summary>
        public double SpeakerHoldMs = 350;

        // Counters (cumulative).
        public long Received, Dropped, FramesOut, DecodeErrors;

        public VoiceTapePlayer(IVoiceDecoder streamA, IVoiceDecoder streamB, int ringCapacity = 16384)
        {
            _decoder[0] = streamA ?? throw new ArgumentNullException(nameof(streamA));
            _decoder[1] = streamB ?? throw new ArgumentNullException(nameof(streamB));
            if (streamA.SampleRate != streamB.SampleRate || streamA.FrameSamples != streamB.FrameSamples)
                throw new ArgumentException("both tape decoders need the same rate");
            for (int i = 0; i < Streams; i++) { _jitter[i] = new VoiceJitterBuffer(); _slot[i] = -1; }
            _frame = streamA.FrameSamples;
            _pcm = new float[_frame];
            _mix = new float[_frame];
            _ring = new PcmRing(ringCapacity);
            _filter = new TapeFilter(streamA.SampleRate);
        }

        public PcmRing Ring => _ring;
        public int SampleRate => _decoder[0].SampleRate;
        public bool Is2D => true;
        /// <summary>The tape runs (playing or draining its last frames).</summary>
        public bool IsPlaying => _state != State.Idle;
        public bool Active => _state != State.Idle;
        public bool Draining => _state == State.Draining;
        /// <summary>Smoothed voice level 0..1 of what the tape plays (hiss excluded) — waveform / VU needle.</summary>
        public float Level01 => _level;
        /// <summary>Original slot of whoever is audible on the tape right now, or -1 (nameplate).</summary>
        public int CurrentSpeaker => _speaker;
        public TapeFilter Filter => _filter;

        /// <summary>Starts a new tape (drops anything left from the previous one).</summary>
        public void Begin()
        {
            ResetStreams();
            _ring.Clear();
            _filter.Reset();
            _state = State.Playing;
        }

        /// <summary>The host finished sending: play what is buffered, then stop.</summary>
        public void End(double nowMs)
        {
            if (_state != State.Playing) return;
            _state = State.Draining;
            _drainDeadlineMs = nowMs + DrainTimeoutMs;
        }

        /// <summary>Immediate stop (disconnect).</summary>
        public void Stop()
        {
            _state = State.Idle;
            ResetStreams();
            _ring.Clear();
        }

        void ResetStreams()
        {
            for (int i = 0; i < Streams; i++)
            {
                _jitter[i].Reset();
                _decoder[i].Reset();
                _slot[i] = -1;
                _streamLevel[i] = 0f;
            }
            _level = 0f;
            _speaker = -1;
            _speakerHoldUntilMs = 0;
        }

        /// <summary>A Recording frame from the host. Ignored while idle.</summary>
        public bool Receive(int slot, ushort seq, VoiceFrameFlags flags, byte[] buffer, int offset, int length, double nowMs)
        {
            if (_state == State.Idle || slot < 0) return false;
            int k = -1;
            for (int i = 0; i < Streams; i++) if (_slot[i] == slot) { k = i; break; }
            if (k < 0)
                for (int i = 0; i < Streams; i++) if (_slot[i] < 0) { _slot[i] = slot; k = i; break; }
            if (k < 0) { Dropped++; return false; }
            Received++;
            return _jitter[k].Insert(seq, flags & VoiceFrameFlags.EndOfSpeech, buffer, offset, length, nowMs);
        }

        bool AllIdle()
        {
            for (int i = 0; i < Streams; i++)
                if (_slot[i] >= 0 && (_jitter[i].IsPlaying || _jitter[i].BufferedCount > 0)) return false;
            return true;
        }

        /// <summary>
        /// Same contract as <see cref="VoicePlaybackChannel.Pump"/>: produce mixed 20 ms frames until the ring holds
        /// <paramref name="lowWaterSamples"/>. While the tape runs it produces continuously (hiss between words);
        /// a running stream whose next packet is late is waited for until the ring drops below
        /// <paramref name="criticalSamples"/>, then concealed.
        /// </summary>
        public int Pump(double nowMs, int lowWaterSamples, int criticalSamples = -1, int maxFrames = 8)
        {
            if (_state == State.Idle) return 0;
            if (criticalSamples < 0) criticalSamples = lowWaterSamples / 2;
            int prefetch = Math.Max(0, (lowWaterSamples + _frame - 1) / _frame - 1);
            for (int i = 0; i < Streams; i++) _jitter[i].PrefetchFrames = prefetch;
            int produced = 0;
            while (produced < maxFrames && _ring.Count < lowWaterSamples && _ring.Free >= _frame)
            {
                if (_ring.Count >= criticalSamples)
                {
                    bool wait = false;
                    for (int i = 0; i < Streams; i++)
                        if (_slot[i] >= 0 && _jitter[i].IsPlaying && !_jitter[i].NextReady) wait = true;
                    if (wait) break;
                }

                Array.Clear(_mix, 0, _frame);
                bool any = false;
                float bestLevel = 0f;
                int bestSlot = -1;
                for (int k = 0; k < Streams; k++)
                {
                    if (_slot[k] < 0) continue;
                    int index;
                    JitterPull r = _jitter[k].Pull(nowMs, true, out index);
                    int samples;
                    if (r == JitterPull.Frame) samples = _decoder[k].Decode(_jitter[k].PayloadAt(index), 0, _jitter[k].LengthAt(index), _pcm, false);
                    else if (r == JitterPull.Fec) samples = _decoder[k].Decode(_jitter[k].PayloadAt(index), 0, _jitter[k].LengthAt(index), _pcm, true);
                    else if (r == JitterPull.Lost) samples = _decoder[k].DecodeLoss(_pcm);
                    else { _streamLevel[k] = 0f; continue; }
                    if (samples != _frame) { DecodeErrors++; _streamLevel[k] = 0f; continue; }
                    any = true;
                    float lvl = VoiceLevel.Speech01(VoiceLevel.RmsDb(_pcm, 0, _frame));
                    _streamLevel[k] = lvl;
                    if (lvl > bestLevel) { bestLevel = lvl; bestSlot = _slot[k]; }
                    for (int i = 0; i < _frame; i++) _mix[i] += _pcm[i];
                }

                bool finishing = _state == State.Draining && ((!any && AllIdle()) || nowMs >= _drainDeadlineMs);
                float mixLevel = any ? VoiceLevel.Speech01(VoiceLevel.RmsDb(_mix, 0, _frame)) : 0f;
                _level = finishing ? 0f : VoiceLevel.Smooth(_level, mixLevel, VoiceFormat.FrameMs / 1000f, 0.03f, 0.15f);
                if (bestSlot >= 0 && bestLevel >= SpeakerThreshold) { _speaker = bestSlot; _speakerHoldUntilMs = nowMs + SpeakerHoldMs; }
                else if (nowMs >= _speakerHoldUntilMs) _speaker = -1;

                _filter.Process(_mix, 0, _frame, finishing ? 0f : 1f);
                float g = Volume;
                if (finishing)
                {
                    // Fade the hiss / delay-line tail to zero over this last frame.
                    float step = g / _frame;
                    for (int i = 0; i < _frame; i++) { _mix[i] *= g; g -= step; }
                }
                else if (g != 1f)
                {
                    for (int i = 0; i < _frame; i++) _mix[i] *= g;
                }
                _ring.Write(_mix, 0, _frame);
                produced++;
                FramesOut++;
                if (finishing)
                {
                    _state = State.Idle;
                    _speaker = -1;
                    ResetStreams();
                    break;
                }
            }
            return produced;
        }

        public void Dispose()
        {
            for (int i = 0; i < Streams; i++) _decoder[i].Dispose();
        }
    }
}
