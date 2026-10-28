using System;

namespace BirimizKovulacak.Voice
{
    /// <summary>
    /// Receive side for one remote speaker: jitter buffer → Opus decode (FEC / PLC on loss) → gain + muffle
    /// low-pass (ramped per frame, no zipper noise) → <see cref="PcmRing"/> at 48 kHz for the audio thread.
    /// Main thread only (except <see cref="Ring"/>, which the audio thread reads). No allocations per frame.
    /// </summary>
    public sealed class VoicePlaybackChannel : IDisposable
    {
        public readonly int Slot;
        readonly VoiceJitterBuffer _jitter = new VoiceJitterBuffer();
        readonly IVoiceDecoder _decoder;
        readonly PcmRing _ring;
        readonly float[] _pcm;
        readonly int _frame;
        Biquad _lowpass;
        float _gain;        // current (ramped) linear gain
        float _wet;         // current muffle mix 0..1
        double _lastFrameMs = -1e9;
        VoiceFrameFlags _currentFlags;

        /// <summary>Distance attenuation for proximity frames (set by the owner from avatar positions).</summary>
        public float ProximityGain = 1f;
        /// <summary>Player volume × master volume × (deafened ? 0 : 1).</summary>
        public float VolumeScale = 1f;
        public float MuffledGain = 0.45f;

        // Counters (cumulative).
        public long DecodeErrors, BytesReceived;

        public VoicePlaybackChannel(int slot, IVoiceDecoder decoder, float muffleCutoffHz = 900f, int ringCapacity = 16384)
        {
            Slot = slot;
            _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
            _frame = decoder.FrameSamples;
            _pcm = new float[_frame];
            _ring = new PcmRing(ringCapacity);
            _lowpass = Biquad.LowPass(decoder.SampleRate, muffleCutoffHz, 0.707f);
        }

        public PcmRing Ring => _ring;
        public VoiceJitterBuffer Jitter => _jitter;
        public int SampleRate => _decoder.SampleRate;
        public int FrameSamples => _frame;
        public VoiceCodecBackend Backend => _decoder.Backend;
        /// <summary>Flags of the most recently decoded frame (Call / AllHear / Muffled / Loopback).</summary>
        public VoiceFrameFlags CurrentFlags => _currentFlags;
        public bool IsPlaying => _jitter.IsPlaying;
        public double LastFrameMs => _lastFrameMs;

        public void SetMuffleCutoff(float hz) { _lowpass.SetLowPass(_decoder.SampleRate, hz, 0.707f); }

        /// <summary>A frame for this speaker arrived from the network (or the host's local route).</summary>
        public bool Receive(ushort seq, VoiceFrameFlags flags, byte[] buffer, int offset, int length, double nowMs)
        {
            BytesReceived += length;
            return _jitter.Insert(seq, flags, buffer, offset, length, nowMs);
        }

        /// <summary>True while a spurt plays or ended less than <paramref name="holdMs"/> ago.</summary>
        public bool IsSpeaking(double nowMs, double holdMs = 150)
        {
            return _jitter.IsPlaying || nowMs - _lastFrameMs < holdMs;
        }

        /// <summary>
        /// Decodes frames until the ring holds at least <paramref name="lowWaterSamples"/> (or the jitter buffer has
        /// nothing to give). A missing frame is only concealed when the ring is below
        /// <paramref name="criticalSamples"/> (default: half the low-water mark) — until then a late packet may still
        /// make it. Returns the number of 20 ms frames produced.
        /// </summary>
        public int Pump(double nowMs, int lowWaterSamples, int criticalSamples = -1, int maxFrames = 8)
        {
            if (criticalSamples < 0) criticalSamples = lowWaterSamples / 2;
            _jitter.PrefetchFrames = Math.Max(0, (lowWaterSamples + _frame - 1) / _frame - 1);
            int produced = 0;
            while (produced < maxFrames && _ring.Count < lowWaterSamples && _ring.Free >= _frame)
            {
                int index;
                JitterPull r = _jitter.Pull(nowMs, _ring.Count < criticalSamples, out index);
                int samples;
                VoiceFrameFlags flags = _currentFlags;
                if (r == JitterPull.Frame)
                {
                    flags = _jitter.FlagsAt(index);
                    samples = _decoder.Decode(_jitter.PayloadAt(index), 0, _jitter.LengthAt(index), _pcm, false);
                }
                else if (r == JitterPull.Fec)
                {
                    samples = _decoder.Decode(_jitter.PayloadAt(index), 0, _jitter.LengthAt(index), _pcm, true);
                }
                else if (r == JitterPull.Lost)
                {
                    samples = _decoder.DecodeLoss(_pcm);
                }
                else break; // Idle / Waiting

                if (samples != _frame)
                {
                    DecodeErrors++;
                    Array.Clear(_pcm, 0, _frame);
                }
                _currentFlags = flags & ~VoiceFrameFlags.EndOfSpeech;
                Process(flags);
                _ring.Write(_pcm, 0, _frame);
                _lastFrameMs = nowMs;
                produced++;
            }
            return produced;
        }

        void Process(VoiceFrameFlags flags)
        {
            bool twoD = (flags & (VoiceFrameFlags.Call | VoiceFrameFlags.AllHear | VoiceFrameFlags.Loopback)) != 0;
            bool muffled = !twoD && (flags & VoiceFrameFlags.Muffled) != 0;
            float targetGain = (twoD ? 1f : ProximityGain * (muffled ? MuffledGain : 1f)) * VolumeScale;
            float targetWet = muffled ? 1f : 0f;
            float g = _gain, w = _wet;
            float dg = (targetGain - g) / _frame, dw = (targetWet - w) / _frame;
            for (int i = 0; i < _frame; i++)
            {
                g += dg;
                w += dw;
                float dry = _pcm[i];
                float wet = _lowpass.Process(dry);
                float s = (dry + (wet - dry) * w) * g;
                _pcm[i] = s > 1f ? 1f : (s < -1f ? -1f : s);
            }
            _gain = targetGain;
            _wet = targetWet;
        }

        /// <summary>Drops queued audio and state (disconnect, deafen toggles do not need this).</summary>
        public void Reset()
        {
            _jitter.Reset();
            _ring.Clear();
            _decoder.Reset();
            _lowpass.Reset();
            _currentFlags = VoiceFrameFlags.None;
        }

        public void Dispose() { _decoder.Dispose(); }
    }
}
