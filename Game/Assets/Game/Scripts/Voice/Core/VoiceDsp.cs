using System;

namespace BirimizKovulacak.Voice
{
    /// <summary>RBJ-cookbook biquad (transposed direct form II). Used for anti-aliasing and the wall muffle.</summary>
    public struct Biquad
    {
        float _b0, _b1, _b2, _a1, _a2;
        float _z1, _z2;

        public static Biquad LowPass(float sampleRate, float cutoffHz, float q)
        {
            var f = new Biquad();
            f.SetLowPass(sampleRate, cutoffHz, q);
            return f;
        }

        public void SetLowPass(float sampleRate, float cutoffHz, float q)
        {
            double w0 = 2.0 * Math.PI * Math.Min(cutoffHz, sampleRate * 0.49f) / sampleRate;
            double cos = Math.Cos(w0), alpha = Math.Sin(w0) / (2.0 * q);
            double a0 = 1.0 + alpha;
            _b0 = (float)((1.0 - cos) * 0.5 / a0);
            _b1 = (float)((1.0 - cos) / a0);
            _b2 = _b0;
            _a1 = (float)(-2.0 * cos / a0);
            _a2 = (float)((1.0 - alpha) / a0);
        }

        public static Biquad HighPass(float sampleRate, float cutoffHz, float q)
        {
            var f = new Biquad();
            f.SetHighPass(sampleRate, cutoffHz, q);
            return f;
        }

        public void SetHighPass(float sampleRate, float cutoffHz, float q)
        {
            double w0 = 2.0 * Math.PI * Math.Min(cutoffHz, sampleRate * 0.49f) / sampleRate;
            double cos = Math.Cos(w0), alpha = Math.Sin(w0) / (2.0 * q);
            double a0 = 1.0 + alpha;
            _b0 = (float)((1.0 + cos) * 0.5 / a0);
            _b1 = (float)(-(1.0 + cos) / a0);
            _b2 = _b0;
            _a1 = (float)(-2.0 * cos / a0);
            _a2 = (float)((1.0 - alpha) / a0);
        }

        /// <summary>Peaking EQ (bell) with <paramref name="gainDb"/> at <paramref name="centerHz"/>.</summary>
        public static Biquad Peaking(float sampleRate, float centerHz, float q, float gainDb)
        {
            var f = new Biquad();
            double a = Math.Pow(10.0, gainDb / 40.0);
            double w0 = 2.0 * Math.PI * Math.Min(centerHz, sampleRate * 0.49f) / sampleRate;
            double cos = Math.Cos(w0), alpha = Math.Sin(w0) / (2.0 * q);
            double a0 = 1.0 + alpha / a;
            f._b0 = (float)((1.0 + alpha * a) / a0);
            f._b1 = (float)(-2.0 * cos / a0);
            f._b2 = (float)((1.0 - alpha * a) / a0);
            f._a1 = f._b1;
            f._a2 = (float)((1.0 - alpha / a) / a0);
            return f;
        }

        public float Process(float x)
        {
            float y = _b0 * x + _z1;
            _z1 = _b1 * x - _a1 * y + _z2;
            _z2 = _b2 * x - _a2 * y;
            return y;
        }

        public void Reset() { _z1 = 0; _z2 = 0; }
    }

    /// <summary>
    /// Subtle telephone colour for call audio ("clarity first", it is the main channel in v3): 2nd-order high-pass
    /// at 200 Hz (removes boom and desk rumble), +2.5 dB presence bell at 1.8 kHz, gentle 2nd-order low-pass at
    /// 5.5 kHz. Speech intelligibility stays wideband; it just sounds like a good handset. Allocation-free.
    /// </summary>
    public struct PhoneEq
    {
        Biquad _hp, _bell, _lp;

        public static PhoneEq Create(float sampleRate)
        {
            return new PhoneEq
            {
                _hp = Biquad.HighPass(sampleRate, 200f, 0.707f),
                _bell = Biquad.Peaking(sampleRate, 1800f, 0.8f, 2.5f),
                _lp = Biquad.LowPass(sampleRate, 5500f, 0.707f),
            };
        }

        public float Process(float x) { return _lp.Process(_bell.Process(_hp.Process(x))); }

        public void Reset() { _hp.Reset(); _bell.Reset(); _lp.Reset(); }
    }

    /// <summary>
    /// "Recorded call played on a cassette deck" colour for the meeting tape: 4th-order band-pass 300–3400 Hz
    /// (classic telephone band), +2 dB at 1.4 kHz, soft tape saturation, wow (0.55 Hz) + flutter (6.5 Hz) through a
    /// modulated delay line (≈0.25 % pitch drift — felt, not heard as vibrato) and a quiet band-limited hiss
    /// (≈ -50 dBFS) that runs while the tape runs, with fades so start/stop never click. Allocation-free after
    /// construction; deterministic noise.
    /// </summary>
    public sealed class TapeFilter
    {
        readonly float _rate;
        Biquad _hp1, _hp2, _lp1, _lp2, _bell, _hissHp, _hissLp;
        readonly float[] _delay = new float[2048];
        int _write;
        double _wowPhase, _flutterPhase;
        uint _rng = 0x9E3779B9u;
        float _hiss;          // current hiss envelope 0..1

        public float WowDepthMs = 0.7f, WowHz = 0.55f, FlutterDepthMs = 0.05f, FlutterHz = 6.5f;
        public float BaseDelayMs = 3f;
        public float Drive = 1.6f;
        /// <summary>Compensates the energy the band-pass removes from speech.</summary>
        public float MakeupGain = 1.3f;
        /// <summary>Hiss amplitude before its band filter (the result is ≈ -50 dBFS RMS).</summary>
        public float HissLevel = 0.012f;
        /// <summary>Hiss fade time constant (s).</summary>
        public float HissFadeSeconds = 0.08f;

        public TapeFilter(int sampleRate)
        {
            _rate = sampleRate > 0 ? sampleRate : VoiceFormat.PlaybackSampleRate;
            _hp1 = Biquad.HighPass(_rate, 300f, 0.5412f);
            _hp2 = Biquad.HighPass(_rate, 300f, 1.3066f);
            _lp1 = Biquad.LowPass(_rate, 3400f, 0.5412f);
            _lp2 = Biquad.LowPass(_rate, 3400f, 1.3066f);
            _bell = Biquad.Peaking(_rate, 1400f, 0.9f, 2f);
            _hissHp = Biquad.HighPass(_rate, 1500f, 0.707f);
            _hissLp = Biquad.LowPass(_rate, 7000f, 0.707f);
        }

        public int SampleRate => (int)_rate;
        /// <summary>Current hiss envelope (0 = silent tape stopped, 1 = running).</summary>
        public float HissEnvelope => _hiss;

        /// <summary>Filters buf[offset..offset+count) in place. <paramref name="hissTarget"/>: 1 while the tape runs,
        /// 0 to fade the hiss out.</summary>
        public void Process(float[] buf, int offset, int count, float hissTarget)
        {
            int mask = _delay.Length - 1;
            double wowInc = 2.0 * Math.PI * WowHz / _rate, flutterInc = 2.0 * Math.PI * FlutterHz / _rate;
            float msToSamples = _rate / 1000f;
            float baseD = BaseDelayMs * msToSamples, wowD = WowDepthMs * msToSamples, flutterD = FlutterDepthMs * msToSamples;
            float maxD = _delay.Length - 4;
            float hissK = 1f - (float)Math.Exp(-1.0 / (Math.Max(0.001f, HissFadeSeconds) * _rate));
            float drive = Drive > 0.01f ? Drive : 0.01f;
            for (int i = 0; i < count; i++)
            {
                float x = buf[offset + i];
                x = _hp2.Process(_hp1.Process(x));
                x = _lp2.Process(_lp1.Process(x));
                x = _bell.Process(x);
                // Soft saturation: unity gain for small signals, peaks gently rounded (0.3 → 0.28).
                x = (float)Math.Tanh(drive * x) / drive;

                _delay[_write] = x;
                float d = baseD + wowD * (float)Math.Sin(_wowPhase) + flutterD * (float)Math.Sin(_flutterPhase);
                if (d < 1f) d = 1f; else if (d > maxD) d = maxD;
                _wowPhase += wowInc;
                if (_wowPhase > 2.0 * Math.PI) _wowPhase -= 2.0 * Math.PI;
                _flutterPhase += flutterInc;
                if (_flutterPhase > 2.0 * Math.PI) _flutterPhase -= 2.0 * Math.PI;
                int di = (int)d;
                float frac = d - di;
                float a = _delay[(_write - di) & mask], b = _delay[(_write - di - 1) & mask];
                float y = (a + (b - a) * frac) * MakeupGain;
                _write = (_write + 1) & mask;

                _hiss += (hissTarget - _hiss) * hissK;
                _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
                float white = (_rng & 0xFFFFFF) / 8388608f - 1f;
                float hiss = _hissLp.Process(_hissHp.Process(white)) * HissLevel * _hiss;

                float s = y + hiss;
                buf[offset + i] = s > 1f ? 1f : (s < -1f ? -1f : s);
            }
        }

        public void Reset()
        {
            _hp1.Reset(); _hp2.Reset(); _lp1.Reset(); _lp2.Reset(); _bell.Reset(); _hissHp.Reset(); _hissLp.Reset();
            Array.Clear(_delay, 0, _delay.Length);
            _write = 0;
            _wowPhase = _flutterPhase = 0;
            _hiss = 0;
        }
    }

    /// <summary>
    /// Streaming mono resampler: 6th-order Butterworth anti-alias low-pass (when downsampling) followed by linear
    /// interpolation with the fractional position carried across calls. Allocation-free; good enough for voice
    /// (e.g. 48 kHz / 44.1 kHz microphones down to the 16 kHz codec rate).
    /// </summary>
    public sealed class StreamResampler
    {
        readonly int _inRate, _outRate;
        readonly double _step;
        readonly bool _filter;
        Biquad _aa1, _aa2, _aa3;
        double _pos = 1.0;
        float _prev;

        public int InputRate => _inRate;
        public int OutputRate => _outRate;

        public StreamResampler(int inputRate, int outputRate)
        {
            if (inputRate <= 0 || outputRate <= 0) throw new ArgumentOutOfRangeException(nameof(inputRate));
            _inRate = inputRate;
            _outRate = outputRate;
            _step = (double)inputRate / outputRate;
            _filter = inputRate > outputRate;
            float cutoff = 0.45f * outputRate;
            _aa1 = Biquad.LowPass(inputRate, cutoff, 0.5176f);
            _aa2 = Biquad.LowPass(inputRate, cutoff, 0.7071f);
            _aa3 = Biquad.LowPass(inputRate, cutoff, 1.9319f);
        }

        /// <summary>Upper bound of outputs produced for <paramref name="inputCount"/> inputs.</summary>
        public int MaxOutput(int inputCount) { return (int)Math.Ceiling(inputCount / _step) + 2; }

        /// <summary>
        /// Resamples input[offset..offset+count). NOTE: when downsampling the input range is low-pass filtered in
        /// place (it is used as scratch). Returns the number of samples written to output[outOffset..].
        /// </summary>
        public int Process(float[] input, int offset, int count, float[] output, int outOffset)
        {
            if (count <= 0) return 0;
            if (_inRate == _outRate)
            {
                Array.Copy(input, offset, output, outOffset, count);
                return count;
            }
            if (_filter)
                for (int i = 0; i < count; i++)
                    input[offset + i] = _aa3.Process(_aa2.Process(_aa1.Process(input[offset + i])));

            int written = 0;
            double pos = _pos;
            int cap = output.Length - outOffset;
            // Virtual signal X(0) = previous call's last sample, X(k) = input[offset + k - 1] for k = 1..count.
            while (pos < count && written < cap)
            {
                int k = (int)pos;
                float frac = (float)(pos - k);
                float a = k == 0 ? _prev : input[offset + k - 1];
                float b = input[offset + k];
                output[outOffset + written++] = a + (b - a) * frac;
                pos += _step;
            }
            _pos = pos - count;
            _prev = input[offset + count - 1];
            return written;
        }

        public void Reset()
        {
            _aa1.Reset();
            _aa2.Reset();
            _aa3.Reset();
            _pos = 1.0;
            _prev = 0;
        }
    }

    public static class VoiceLevel
    {
        /// <summary>RMS of buf[0..count) in dBFS (silence clamps to -100).</summary>
        public static float RmsDb(float[] buf, int offset, int count)
        {
            if (count <= 0) return -100f;
            double sum = 0;
            for (int i = 0; i < count; i++) { float s = buf[offset + i]; sum += s * s; }
            double rms = Math.Sqrt(sum / count);
            return rms < 1e-5 ? -100f : (float)(20.0 * Math.Log10(rms));
        }

        /// <summary>Maps dBFS to a 0..1 meter value (-60 dB → 0, 0 dB → 1).</summary>
        public static float Meter(float db)
        {
            float v = (db + 60f) / 60f;
            return v < 0 ? 0 : (v > 1 ? 1 : v);
        }

        /// <summary>Maps the dBFS of a speech frame to 0..1 for talk animation / waveforms: -50 dB (room noise) → 0,
        /// -14 dB (loud speech) → 1; normal speech lands around 0.4–0.8.</summary>
        public static float Speech01(float db)
        {
            float v = (db + 50f) / 36f;
            return v < 0 ? 0 : (v > 1 ? 1 : v);
        }

        /// <summary>One-pole envelope follower step: fast attack, slower release (time constants in seconds).</summary>
        public static float Smooth(float current, float target, float dt, float attackSeconds = 0.03f, float releaseSeconds = 0.12f)
        {
            float tau = target > current ? attackSeconds : releaseSeconds;
            if (tau <= 0f || dt <= 0f) return dt <= 0f ? current : target;
            float k = 1f - (float)Math.Exp(-dt / tau);
            return current + (target - current) * k;
        }
    }

    /// <summary>
    /// Distance attenuation for office voice: full volume up to <paramref name="fullDistance"/>, a 1/d law
    /// re-scaled so it reaches exactly 0 at <paramref name="maxDistance"/>.
    /// </summary>
    public static class VoiceAttenuation
    {
        public static float Gain(float distance, float fullDistance, float maxDistance)
        {
            if (distance <= fullDistance) return 1f;
            if (distance >= maxDistance || maxDistance <= fullDistance) return 0f;
            float floor = fullDistance / maxDistance;
            float g = (fullDistance / distance - floor) / (1f - floor);
            return g < 0 ? 0 : (g > 1 ? 1 : g);
        }
    }
}
