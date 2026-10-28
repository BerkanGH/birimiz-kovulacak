using System;
using BirimizKovulacak.Voice;
using NUnit.Framework;

namespace BirimizKovulacak.VoiceTests
{
    [TestFixture]
    public class CodecTests
    {
        const int Frame = VoiceFormat.FrameSamples;

        static float[] EncodeDecode(IVoiceEncoder enc, IVoiceDecoder dec, float[] input, out long totalBytes, out int maxBytes)
        {
            int frames = input.Length / Frame;
            var outFrame = dec.FrameSamples;
            var output = new float[frames * outFrame];
            var pcm = new float[Frame];
            var pkt = new byte[VoiceFormat.MaxPayloadBytes];
            var dpcm = new float[outFrame];
            totalBytes = 0;
            maxBytes = 0;
            for (int f = 0; f < frames; f++)
            {
                Array.Copy(input, f * Frame, pcm, 0, Frame);
                int n = enc.Encode(pcm, pkt, 0, pkt.Length);
                Assert.Greater(n, 0, "encode frame " + f);
                totalBytes += n;
                maxBytes = Math.Max(maxBytes, n);
                int s = dec.Decode(pkt, 0, n, dpcm, false);
                Assert.AreEqual(outFrame, s, "decode frame " + f);
                Array.Copy(dpcm, 0, output, f * outFrame, outFrame);
            }
            return output;
        }

        [TestCaseSource(typeof(Backends), nameof(Backends.All))]
        public void RoundTrip_HarmonicTone_HasSaneSnr(VoiceCodecBackend backend)
        {
            using var enc = Backends.Encoder(backend);
            using var dec = Backends.Decoder(backend, VoiceFormat.CodecSampleRate);
            var input = Signals.Harmonic(VoiceFormat.CodecSampleRate, 2.0);
            var output = EncodeDecode(enc, dec, input, out long bytes, out int maxBytes);
            int lag;
            double snr = Signals.AlignedSnrDb(input, output, 3200, 800, out lag);
            double kbps = bytes * 8.0 / 2.0 / 1000.0;
            TestContext.WriteLine($"{backend}: SNR {snr:0.0} dB at lag {lag} samples, {kbps:0.0} kbps payload, max frame {maxBytes} B");
            Assert.Greater(snr, 8.0, "decoded tone should resemble the input");
            Assert.Less(lag, 400, "codec delay should be a few ms");
            Assert.LessOrEqual(maxBytes, VoiceFormat.MaxPayloadBytes);
        }

        [TestCaseSource(typeof(Backends), nameof(Backends.All))]
        public void Decode_At48k_KeepsPitchAndLevel(VoiceCodecBackend backend)
        {
            using var enc = Backends.Encoder(backend);
            using var dec = Backends.Decoder(backend, VoiceFormat.PlaybackSampleRate);
            var input = Signals.Sine(VoiceFormat.CodecSampleRate, 1.0, 440, 0.3f);
            var output = EncodeDecode(enc, dec, input, out _, out _);
            int skip = 9600;
            int len = output.Length - skip;
            double at440 = Signals.TonePower(output, skip, len, 48000, 440);
            double at880 = Signals.TonePower(output, skip, len, 48000, 880);
            double at1320 = Signals.TonePower(output, skip, len, 48000, 1320);
            float inRms = Signals.Rms(input, 3200, input.Length - 3200);
            float outRms = Signals.Rms(output, skip, len);
            TestContext.WriteLine($"{backend}: rms in {inRms:0.000} out {outRms:0.000}; 440 Hz {at440:E2} vs 880 Hz {at880:E2}");
            Assert.Greater(at440, at880 * 50, "440 Hz must dominate");
            Assert.Greater(at440, at1320 * 50, "440 Hz must dominate");
            Assert.AreEqual(inRms, outRms, inRms * 0.4f, "level within ~3 dB");
        }

        [Test]
        public void NativeAndManaged_AreBitstreamCompatible()
        {
            Backends.Require(VoiceCodecBackend.Native);
            var input = Signals.Harmonic(VoiceFormat.CodecSampleRate, 1.0);
            using (var enc = Backends.Encoder(VoiceCodecBackend.Native))
            using (var dec = Backends.Decoder(VoiceCodecBackend.Managed, VoiceFormat.CodecSampleRate))
            {
                var output = EncodeDecode(enc, dec, input, out _, out _);
                double snr = Signals.AlignedSnrDb(input, output, 3200, 800, out _);
                Assert.Greater(snr, 8.0, "native → managed");
            }
            using (var enc = Backends.Encoder(VoiceCodecBackend.Managed))
            using (var dec = Backends.Decoder(VoiceCodecBackend.Native, VoiceFormat.CodecSampleRate))
            {
                var output = EncodeDecode(enc, dec, input, out _, out _);
                double snr = Signals.AlignedSnrDb(input, output, 3200, 800, out _);
                Assert.Greater(snr, 8.0, "managed → native");
            }
        }

        [TestCaseSource(typeof(Backends), nameof(Backends.All))]
        public void LossConcealment_And_Fec_ProduceFullFrames(VoiceCodecBackend backend)
        {
            using var enc = Backends.Encoder(backend);
            using var dec = Backends.Decoder(backend, VoiceFormat.PlaybackSampleRate);
            var input = Signals.Harmonic(VoiceFormat.CodecSampleRate, 1.0);
            var pcm = new float[Frame];
            var pkts = new byte[50][];
            var buf = new byte[VoiceFormat.MaxPayloadBytes];
            for (int f = 0; f < 50; f++)
            {
                Array.Copy(input, f * Frame, pcm, 0, Frame);
                int n = enc.Encode(pcm, buf, 0, buf.Length);
                pkts[f] = new byte[n];
                Buffer.BlockCopy(buf, 0, pkts[f], 0, n);
            }
            var outPcm = new float[dec.FrameSamples];
            for (int f = 0; f < 20; f++) Assert.AreEqual(dec.FrameSamples, dec.Decode(pkts[f], 0, pkts[f].Length, outPcm, false));
            // frame 20 lost: recover it from frame 21's FEC, then decode 21 normally
            Assert.AreEqual(dec.FrameSamples, dec.Decode(pkts[21], 0, pkts[21].Length, outPcm, true), "FEC");
            Assert.Greater(Signals.Rms(outPcm, 0, outPcm.Length), 0.01f, "FEC frame not silent");
            Assert.AreEqual(dec.FrameSamples, dec.Decode(pkts[21], 0, pkts[21].Length, outPcm, false));
            // frames 22, 23 lost without FEC: PLC
            Assert.AreEqual(dec.FrameSamples, dec.DecodeLoss(outPcm), "PLC 1");
            Assert.Greater(Signals.Rms(outPcm, 0, outPcm.Length), 0.005f, "PLC continues the signal");
            Assert.AreEqual(dec.FrameSamples, dec.DecodeLoss(outPcm), "PLC 2");
            Assert.AreEqual(dec.FrameSamples, dec.Decode(pkts[24], 0, pkts[24].Length, outPcm, false));
            // garbage must not crash
            var junk = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x01, 0x02 };
            int r = dec.Decode(junk, 0, junk.Length, outPcm, false);
            TestContext.WriteLine($"{backend}: junk decode returned {r}");
        }

        [Test]
        public void NativeEncoder_AppliesCtlSettings()
        {
            Backends.Require(VoiceCodecBackend.Native);
            var s = VoiceEncoderSettings.Default;
            s.Bitrate = 18000;
            using var enc = new NativeOpusEncoder(s);
            Assert.AreEqual(18000, enc.GetBitrate(), "varargs ctl binding works on this ABI");
            TestContext.WriteLine("libopus version: " + NativeOpus.Version);
        }

        [Test]
        public void AutoBackend_PrefersNativeWhenAvailable()
        {
            var resolved = VoiceCodec.Resolve(VoiceCodecBackend.Auto);
            Assert.AreEqual(NativeOpus.IsAvailable ? VoiceCodecBackend.Native : VoiceCodecBackend.Managed, resolved);
            Assert.AreEqual(VoiceCodecBackend.Managed, VoiceCodec.Resolve(VoiceCodecBackend.Managed));
        }
    }
}
