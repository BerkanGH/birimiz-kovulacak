using System;
using System.IO;

namespace BirimizKovulacak.Voice
{
    /// <summary>
    /// Microphone replacement for automated runs (-voicetest). Produces 16 kHz mono in a talk/pause pattern so
    /// open-mic VAD, spurt boundaries, routing and playback are all exercised without a real microphone:
    /// <list type="bullet">
    /// <item><c>sine</c>: 220 Hz + harmonics with 4 Hz syllable modulation.</item>
    /// <item><c>speech</c>: a buzzy glottal pulse train through two moving formants (more speech-like spectrum).</item>
    /// <item>a path to a .wav file (PCM16 or float32, any rate/channels): looped.</item>
    /// </list>
    /// Pattern: talk <see cref="TalkSeconds"/>, pause <see cref="PauseSeconds"/>, phase-shifted per slot so
    /// players do not all talk at once.
    /// </summary>
    public sealed class SyntheticVoiceSource
    {
        public const int SampleRate = VoiceFormat.CodecSampleRate;

        public double TalkSeconds = 2.5;
        public double PauseSeconds = 3.5;
        public float Amplitude = 0.3f;

        readonly string _kind;
        readonly float[] _wav;
        long _sample;
        int _wavPos;
        double _phase, _f1Phase, _f2Phase;
        Biquad _f1, _f2;
        readonly long _offsetSamples;

        public string Description { get; }

        SyntheticVoiceSource(string kind, float[] wav, int slotSeed, string description)
        {
            _kind = kind;
            _wav = wav;
            Description = description;
            _offsetSamples = (long)((slotSeed % 6) * 1.1 * SampleRate);
        }

        /// <summary>Builds a source from a launch-arg spec; unknown specs fall back to "sine".</summary>
        public static SyntheticVoiceSource Create(string spec, int slotSeed)
        {
            spec = (spec ?? "").Trim();
            if (spec.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    float[] data = WavReader.LoadMono(File.ReadAllBytes(spec), SampleRate);
                    if (data.Length > 0) return new SyntheticVoiceSource("wav", data, slotSeed, "wav:" + Path.GetFileName(spec));
                }
                catch (Exception) { }
                return new SyntheticVoiceSource("sine", null, slotSeed, "sine (wav load failed: " + spec + ")");
            }
            string kind = spec.Equals("speech", StringComparison.OrdinalIgnoreCase) ? "speech" : "sine";
            return new SyntheticVoiceSource(kind, null, slotSeed, kind);
        }

        public bool IsTalkingAt(long sample)
        {
            double cycle = (TalkSeconds + PauseSeconds) * SampleRate;
            double t = (sample + _offsetSamples) % cycle;
            return t < TalkSeconds * SampleRate;
        }

        public bool IsTalking => IsTalkingAt(_sample);

        /// <summary>Fills buffer[offset..offset+count) with the next samples (16 kHz).</summary>
        public void Read(float[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++)
            {
                bool talking = IsTalkingAt(_sample);
                float s = 0f;
                if (_kind == "wav")
                {
                    s = talking ? _wav[_wavPos] : 0f;
                    _wavPos = (_wavPos + 1) % _wav.Length;
                }
                else if (talking)
                {
                    double t = (double)_sample / SampleRate;
                    float syllable = (float)(0.55 + 0.45 * Math.Sin(2 * Math.PI * 4.0 * t));
                    if (_kind == "speech")
                    {
                        double f0 = 140 + 25 * Math.Sin(2 * Math.PI * 0.7 * t);
                        _phase += f0 / SampleRate;
                        if (_phase >= 1) _phase -= 1;
                        float pulse = (float)(_phase < 0.08 ? 1.0 - _phase / 0.08 : 0.0) - 0.04f;
                        _f1Phase += 1.0 / SampleRate;
                        _f2Phase += 1.0 / SampleRate;
                        float f1 = (float)(500 + 250 * Math.Sin(2 * Math.PI * 1.3 * _f1Phase));
                        float f2 = (float)(1500 + 600 * Math.Sin(2 * Math.PI * 0.9 * _f2Phase));
                        _f1.SetLowPass(SampleRate, f1, 4f);
                        _f2.SetLowPass(SampleRate, f2, 3f);
                        s = (_f1.Process(pulse) * 0.6f + _f2.Process(pulse) * 0.4f) * 2.5f * syllable;
                    }
                    else
                    {
                        _phase += 220.0 / SampleRate;
                        if (_phase >= 1) _phase -= 1;
                        double p = 2 * Math.PI * _phase;
                        s = (float)(Math.Sin(p) * 0.6 + Math.Sin(2 * p) * 0.25 + Math.Sin(3 * p) * 0.15) * syllable;
                    }
                }
                buffer[offset + i] = s * Amplitude;
                _sample++;
            }
        }
    }

    /// <summary>Minimal RIFF/WAVE reader (PCM 8/16/24/32-bit, IEEE float 32) → mono floats at a target rate.</summary>
    public static class WavReader
    {
        public static float[] LoadMono(byte[] file, int targetRate)
        {
            if (file == null || file.Length < 44 || file[0] != 'R' || file[1] != 'I' || file[2] != 'F' || file[3] != 'F' ||
                file[8] != 'W' || file[9] != 'A' || file[10] != 'V' || file[11] != 'E')
                throw new InvalidDataException("not a RIFF/WAVE file");
            int pos = 12;
            int format = 0, channels = 0, rate = 0, bits = 0;
            int dataOffset = -1, dataLength = 0;
            while (pos + 8 <= file.Length)
            {
                int len = BitConverter.ToInt32(file, pos + 4);
                bool fmt = file[pos] == 'f' && file[pos + 1] == 'm' && file[pos + 2] == 't';
                bool data = file[pos] == 'd' && file[pos + 1] == 'a' && file[pos + 2] == 't' && file[pos + 3] == 'a';
                if (fmt)
                {
                    format = BitConverter.ToInt16(file, pos + 8);
                    channels = BitConverter.ToInt16(file, pos + 10);
                    rate = BitConverter.ToInt32(file, pos + 12);
                    bits = BitConverter.ToInt16(file, pos + 22);
                    if (format == unchecked((short)0xFFFE) && len >= 40) format = BitConverter.ToInt16(file, pos + 32); // extensible
                }
                else if (data)
                {
                    dataOffset = pos + 8;
                    dataLength = Math.Min(len, file.Length - dataOffset);
                    break;
                }
                pos += 8 + len + (len & 1);
            }
            if (dataOffset < 0 || channels <= 0 || rate <= 0) throw new InvalidDataException("missing fmt/data chunk");
            int bytesPer = bits / 8;
            if (bytesPer <= 0 || (format != 1 && format != 3)) throw new InvalidDataException("unsupported WAV format " + format + "/" + bits);
            int frames = dataLength / (bytesPer * channels);
            var mono = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    int o = dataOffset + (f * channels + c) * bytesPer;
                    float v;
                    if (format == 3 && bits == 32) v = BitConverter.ToSingle(file, o);
                    else if (bits == 16) v = BitConverter.ToInt16(file, o) / 32768f;
                    else if (bits == 8) v = (file[o] - 128) / 128f;
                    else if (bits == 24) v = ((file[o] << 8 | file[o + 1] << 16 | file[o + 2] << 24) >> 8) / 8388608f;
                    else if (bits == 32) v = BitConverter.ToInt32(file, o) / 2147483648f;
                    else throw new InvalidDataException("unsupported bit depth " + bits);
                    sum += v;
                }
                mono[f] = sum / channels;
            }
            if (rate == targetRate) return mono;
            var rs = new StreamResampler(rate, targetRate);
            var output = new float[rs.MaxOutput(frames)];
            int n = rs.Process(mono, 0, frames, output, 0);
            Array.Resize(ref output, n);
            return output;
        }
    }
}
