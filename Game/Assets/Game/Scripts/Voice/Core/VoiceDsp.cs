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
