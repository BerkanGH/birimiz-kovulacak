using System;
using System.Collections.Generic;
using BirimizKovulacak.Voice;
using NUnit.Framework;

namespace BirimizKovulacak.VoiceTests
{
    static class Signals
    {
        /// <summary>Voice-band test tone: 220 Hz fundamental with two harmonics.</summary>
        public static float[] Harmonic(int sampleRate, double seconds, double f0 = 220, float amp = 0.3f)
        {
            int n = (int)(sampleRate * seconds);
            var x = new float[n];
            for (int i = 0; i < n; i++)
            {
                double p = 2 * Math.PI * f0 * i / sampleRate;
                x[i] = amp * (float)(Math.Sin(p) * 0.6 + Math.Sin(2 * p) * 0.25 + Math.Sin(3 * p) * 0.15);
            }
            return x;
        }

        public static float[] Sine(int sampleRate, double seconds, double f, float amp)
        {
            int n = (int)(sampleRate * seconds);
            var x = new float[n];
            for (int i = 0; i < n; i++) x[i] = amp * (float)Math.Sin(2 * Math.PI * f * i / sampleRate);
            return x;
        }

        public static float Rms(float[] x, int offset, int count)
        {
            double s = 0;
            for (int i = 0; i < count; i++) s += x[offset + i] * (double)x[offset + i];
            return (float)Math.Sqrt(s / Math.Max(1, count));
        }

        /// <summary>Goertzel power of one frequency, normalised by the signal length.</summary>
        public static double TonePower(float[] x, int offset, int count, int sampleRate, double f)
        {
            double w = 2 * Math.PI * f / sampleRate, c = 2 * Math.Cos(w), s1 = 0, s2 = 0;
            for (int i = 0; i < count; i++)
            {
                double s0 = x[offset + i] + c * s1 - s2;
                s2 = s1;
                s1 = s0;
            }
            return (s1 * s1 + s2 * s2 - c * s1 * s2) / ((double)count * count);
        }

        /// <summary>SNR (dB) of decoded vs reference after searching the codec delay and the best gain.</summary>
        public static double AlignedSnrDb(float[] reference, float[] decoded, int skip, int maxLag, out int bestLag)
        {
            bestLag = 0;
            double bestCorr = double.NegativeInfinity;
            int len = Math.Min(reference.Length, decoded.Length) - maxLag - skip;
            for (int lag = 0; lag <= maxLag; lag++)
            {
                double num = 0, den = 0;
                for (int i = skip; i < skip + len; i++) { num += reference[i] * (double)decoded[i + lag]; den += decoded[i + lag] * (double)decoded[i + lag]; }
                double corr = den > 0 ? num / Math.Sqrt(den) : 0;
                if (corr > bestCorr) { bestCorr = corr; bestLag = lag; }
            }
            double rr = 0, rd = 0, dd = 0;
            for (int i = skip; i < skip + len; i++)
            {
                double r = reference[i], d = decoded[i + bestLag];
                rr += r * r; rd += r * d; dd += d * d;
            }
            double g = dd > 0 ? rd / dd : 0;
            double err = 0;
            for (int i = skip; i < skip + len; i++)
            {
                double e = reference[i] - g * decoded[i + bestLag];
                err += e * e;
            }
            return 10 * Math.Log10(rr / Math.Max(err, 1e-12));
        }
    }

    static class Backends
    {
        public static IEnumerable<VoiceCodecBackend> All
        {
            get
            {
                yield return VoiceCodecBackend.Native;
                yield return VoiceCodecBackend.Managed;
            }
        }

        public static void Require(VoiceCodecBackend b)
        {
            if (b == VoiceCodecBackend.Native && !NativeOpus.IsAvailable)
                Assert.Ignore("opus.dll not loadable here: " + NativeOpus.LoadError);
        }

        public static IVoiceEncoder Encoder(VoiceCodecBackend b, VoiceEncoderSettings? s = null)
        {
            Require(b);
            var e = VoiceCodec.CreateEncoder(s ?? VoiceEncoderSettings.Default, b);
            Assert.AreEqual(b, e.Backend, "encoder backend");
            return e;
        }

        public static IVoiceDecoder Decoder(VoiceCodecBackend b, int rate)
        {
            Require(b);
            var d = VoiceCodec.CreateDecoder(rate, b);
            Assert.AreEqual(b, d.Backend, "decoder backend");
            return d;
        }
    }

    /// <summary>Scriptable host-side world for router tests.</summary>
    sealed class FakeWorld : IVoicePositions, IVoiceCallState, IVoiceModeProvider, IVoiceOcclusion, IVoiceClientMap
    {
        public readonly bool[] Active = new bool[VoiceFormat.MaxPlayers];
        public readonly VoiceVec3[] Pos = new VoiceVec3[VoiceFormat.MaxPlayers];
        public readonly bool[] HasPos = new bool[VoiceFormat.MaxPlayers];
        public readonly int[] Partner = { -1, -1, -1, -1, -1, -1 };
        public readonly bool[] Muted = new bool[VoiceFormat.MaxPlayers];
        public VoiceMode Mode = VoiceMode.Office;
        /// <summary>Walls: an axis-aligned plane x = WallX between WallZMin..WallZMax blocks when crossed.</summary>
        public float WallX = float.NaN;
        public int OcclusionQueries;

        public FakeWorld Place(int slot, float x, float z)
        {
            Active[slot] = true;
            HasPos[slot] = true;
            Pos[slot] = new VoiceVec3(x, 0, z);
            return this;
        }

        public void Call(int a, int b) { Partner[a] = b; Partner[b] = a; }

        public bool IsActive(int slot) { return slot >= 0 && slot < Active.Length && Active[slot]; }
        public bool TryGetPosition(int slot, out VoiceVec3 position) { position = Pos[slot]; return HasPos[slot]; }
        public int GetCallPartner(int slot) { return Partner[slot]; }
        public VoiceMode CurrentMode => Mode;
        public bool CanSpeak(int slot) { return !Muted[slot]; }

        public bool IsOccluded(in VoiceVec3 from, in VoiceVec3 to)
        {
            OcclusionQueries++;
            if (float.IsNaN(WallX)) return false;
            return (from.X - WallX) * (to.X - WallX) < 0;
        }

        public int SlotOfClient(ulong clientId) { return clientId < 6 ? (int)clientId : -1; }
        public bool TryGetClient(int slot, out ulong clientId) { clientId = (ulong)slot; return IsActive(slot); }
    }

    /// <summary>Collects encoded frames into a bundle writer (no allocations per frame).</summary>
    sealed class BundleSink : IVoiceFrameSink
    {
        public readonly VoiceBundleWriter Writer = new VoiceBundleWriter();
        public int Slot;
        public int Frames, EndFrames;
        public long Bytes;

        public void OnVoiceFrame(ushort sequence, VoiceFrameFlags flags, byte[] payload, int length)
        {
            Frames++;
            Bytes += length;
            if ((flags & VoiceFrameFlags.EndOfSpeech) != 0) EndFrames++;
            if (!Writer.TryAppend(Slot, sequence, flags, payload, 0, length))
            {
                Writer.Reset();
                Writer.TryAppend(Slot, sequence, flags, payload, 0, length);
            }
        }
    }

    /// <summary>Records frames (allocates; for logic tests only).</summary>
    sealed class ListSink : IVoiceFrameSink
    {
        public readonly List<(ushort seq, VoiceFrameFlags flags, byte[] data)> Frames = new List<(ushort, VoiceFrameFlags, byte[])>();

        public void OnVoiceFrame(ushort sequence, VoiceFrameFlags flags, byte[] payload, int length)
        {
            var copy = new byte[length];
            Buffer.BlockCopy(payload, 0, copy, 0, length);
            Frames.Add((sequence, flags, copy));
        }
    }
}
