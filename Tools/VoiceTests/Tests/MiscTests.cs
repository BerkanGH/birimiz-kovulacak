using System;
using System.IO;
using System.Threading;
using BirimizKovulacak.Voice;
using NUnit.Framework;

namespace BirimizKovulacak.VoiceTests
{
    [TestFixture]
    public class MiscTests
    {
        [Test]
        public void PcmRing_WrapsAndRespectsCapacity()
        {
            var ring = new PcmRing(1000);
            Assert.AreEqual(1024, ring.Capacity);
            var src = new float[700];
            for (int i = 0; i < src.Length; i++) src[i] = i;
            Assert.AreEqual(700, ring.Write(src, 0, 700));
            Assert.AreEqual(324, ring.Write(src, 0, 700), "only the free space is written");
            var dst = new float[1024];
            Assert.AreEqual(600, ring.Read(dst, 0, 600));
            Assert.AreEqual(599f, dst[599]);
            Assert.AreEqual(500, ring.Write(src, 200, 500));
            Assert.AreEqual(924, ring.Count);
            Assert.AreEqual(924, ring.Read(dst, 0, 2000));
            Assert.AreEqual(600f, dst[0]);
            Assert.AreEqual(323f, dst[100 + 323]);
            Assert.AreEqual(200f, dst[424]);
            Assert.AreEqual(699f, dst[923]);
            ring.Write(src, 0, 10);
            Assert.AreEqual(4, ring.Discard(4));
            Assert.AreEqual(6, ring.Count);
            ring.Clear();
            Assert.AreEqual(0, ring.Count);
        }

        [Test]
        public void PcmRing_ProducerConsumerThreads_KeepOrder()
        {
            var ring = new PcmRing(256);
            const int total = 200000;
            bool ok = true;
            var consumer = new Thread(() =>
            {
                var buf = new float[97];
                int expected = 0;
                while (expected < total)
                {
                    int n = ring.Read(buf, 0, buf.Length);
                    for (int i = 0; i < n; i++)
                        if (buf[i] != expected++) { ok = false; return; }
                }
            });
            consumer.Start();
            var src = new float[61];
            int next = 0;
            while (next < total)
            {
                int n = Math.Min(src.Length, total - next);
                for (int i = 0; i < n; i++) src[i] = next + i;
                int w = ring.Write(src, 0, n);
                next += w;
            }
            Assert.IsTrue(consumer.Join(10000), "consumer finished");
            Assert.IsTrue(ok, "samples arrive in order without loss");
        }

        [Test]
        public void PlayoutReader_48k_MultipliesIntoInterleavedBuffer_AndZeroFillsUnderrun()
        {
            var ring = new PcmRing(4096);
            var reader = new PcmPlayoutReader();
            reader.SetRates(48000, 48000);
            var src = new float[100];
            for (int i = 0; i < 100; i++) src[i] = 0.5f;
            ring.Write(src, 0, 100);
            var data = new float[256 * 2];
            for (int i = 0; i < data.Length; i++) data[i] = i < 100 ? 0.8f : 1f; // pretend left/right spatial gains
            reader.MultiplyInto(ring, data, 2);
            Assert.AreEqual(0.4f, data[0], 1e-6);
            Assert.AreEqual(0.4f, data[99], 1e-6);
            Assert.AreEqual(0.5f, data[100], 1e-6);
            Assert.AreEqual(0f, data[200], "underrun is silent");
            Assert.AreEqual(1, reader.Underruns);
            for (int i = 0; i < data.Length; i++) data[i] = 1f;
            reader.MultiplyInto(ring, data, 2);
            Assert.AreEqual(0f, data[0]);
            Assert.AreEqual(1, reader.Underruns, "an idle (empty) ring is not an underrun");
        }

        [Test]
        public void PlayoutReader_44k1_ConsumesAtTheRightRate_WithoutLosingSamples()
        {
            var ring = new PcmRing(1 << 16);
            var reader = new PcmPlayoutReader();
            reader.SetRates(48000, 44100);
            var ramp = new float[48000];
            for (int i = 0; i < ramp.Length; i++) ramp[i] = i / 48000f;
            ring.Write(ramp, 0, 40000);
            var data = new float[512];
            float last = -1;
            int callbacks = 0;
            for (int c = 0; c < 70; c++)
            {
                for (int i = 0; i < data.Length; i++) data[i] = 1f;
                reader.MultiplyInto(ring, data, 1);
                for (int i = 0; i < data.Length; i++)
                {
                    Assert.GreaterOrEqual(data[i], last - 1e-6f, "monotonic ramp: no skipped or repeated blocks");
                    last = data[i];
                }
                callbacks++;
            }
            int outFrames = callbacks * 512;
            int consumed = 40000 - ring.Count;
            double expected = outFrames * 48000.0 / 44100.0;
            Assert.AreEqual(expected, consumed, 6, "consumption matches the rate ratio");
            Assert.AreEqual(0, reader.Underruns);
        }

        [Test]
        public void LaunchOptions_Parse()
        {
            var o = VoiceLaunchOptions.Parse(new[] { "-bot", "-voicetest", "speech", "-voicecodec", "managed", "-voicestats", "5", "-voiceloopback" });
            Assert.AreEqual("speech", o.TestSource);
            Assert.AreEqual(VoiceCodecBackend.Managed, o.Codec);
            Assert.AreEqual(5f, o.EffectiveStatsSeconds);
            Assert.IsTrue(o.Loopback);
            Assert.IsFalse(o.Disabled);

            var d = VoiceLaunchOptions.Parse(new[] { "-voicetest", "-autohost" });
            Assert.AreEqual("sine", d.TestSource, "bare -voicetest defaults to sine");
            Assert.AreEqual(10f, d.EffectiveStatsSeconds);

            var w = VoiceLaunchOptions.Parse(new[] { "-voicetest", @"C:\clips\hello.wav", "-novoice" });
            Assert.AreEqual(@"C:\clips\hello.wav", w.TestSource);
            Assert.IsTrue(w.Disabled);

            var none = VoiceLaunchOptions.Parse(new[] { "-autojoin", "127.0.0.1" });
            Assert.IsNull(none.TestSource);
            Assert.AreEqual(VoiceCodecBackend.Auto, none.Codec);
            Assert.AreEqual(30f, none.EffectiveStatsSeconds);
        }

        static byte[] MakeWav(int rate, short channels, short[] interleaved)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(new[] { 'R', 'I', 'F', 'F' });
            w.Write(36 + interleaved.Length * 2);
            w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
            w.Write(16);
            w.Write((short)1);
            w.Write(channels);
            w.Write(rate);
            w.Write(rate * channels * 2);
            w.Write((short)(channels * 2));
            w.Write((short)16);
            w.Write(new[] { 'L', 'I', 'S', 'T' }); // unknown chunk must be skipped
            w.Write(4);
            w.Write(0);
            w.Write(new[] { 'd', 'a', 't', 'a' });
            w.Write(interleaved.Length * 2);
            foreach (var s in interleaved) w.Write(s);
            return ms.ToArray();
        }

        [Test]
        public void WavReader_StereoPcm16_8k_ToMono16k()
        {
            var samples = new short[8000 * 2];
            for (int i = 0; i < 8000; i++) { samples[2 * i] = 16384; samples[2 * i + 1] = 0; }
            float[] mono = WavReader.LoadMono(MakeWav(8000, 2, samples), 16000);
            Assert.That(mono.Length, Is.InRange(15998, 16002));
            Assert.AreEqual(0.25f, mono[8000], 0.01f, "stereo mixed down");
            Assert.Throws<InvalidDataException>(() => WavReader.LoadMono(new byte[64], 16000));
        }

        [Test]
        public void SyntheticSource_TalksInPattern_PhaseShiftedPerSlot()
        {
            var a = SyntheticVoiceSource.Create("sine", 0);
            var b = SyntheticVoiceSource.Create("sine", 2);
            Assert.IsTrue(a.IsTalkingAt(0));
            Assert.IsFalse(a.IsTalkingAt((long)(3.0 * 16000)));
            bool differs = false;
            for (long s = 0; s < 16000 * 6; s += 1600) differs |= a.IsTalkingAt(s) != b.IsTalkingAt(s);
            Assert.IsTrue(differs, "players do not all talk at the same time");
            var buf = new float[16000];
            a.Read(buf, 0, buf.Length);
            Assert.Greater(Signals.Rms(buf, 0, buf.Length), 0.05f);
            var sp = SyntheticVoiceSource.Create("speech", 1);
            sp.TalkSeconds = 10;
            sp.Read(buf, 0, buf.Length);
            float rms = Signals.Rms(buf, 0, buf.Length);
            Assert.That(rms, Is.InRange(0.01f, 0.7f), "speech-like source has a sane level");
            Assert.AreEqual("sine (wav load failed: missing.wav)", SyntheticVoiceSource.Create("missing.wav", 0).Description);
        }

        [Test]
        public void RateLimiter_AllowsBurstThenSteadyRate()
        {
            var rl = new VoiceRateLimiter { FramesPerSecond = 60, Burst = 10 };
            int ok = 0;
            for (int i = 0; i < 30; i++) if (rl.Allow(1, 1000)) ok++;
            Assert.AreEqual(10, ok, "burst");
            ok = 0;
            for (int t = 0; t < 1000; t += 10) if (rl.Allow(1, 1000 + t + 10)) ok++;
            Assert.That(ok, Is.InRange(58, 62), "~60 frames per second");
            Assert.IsFalse(rl.Allow(-1, 0));
            Assert.IsFalse(rl.Allow(6, 0));
        }

        [Test]
        public void StatsReporter_FormatsDeltas()
        {
            var sink = new BundleSink();
            var p = new VoiceCapturePipeline(VoiceCodec.CreateEncoder(VoiceEncoderSettings.Default), 16000) { Sink = sink, PttHeld = true };
            var tone = Signals.Harmonic(16000, 1.0);
            p.Process(tone, 0, tone.Length);
            var ch = new VoicePlaybackChannel(2, VoiceCodec.CreateDecoder(48000));
            var r = new VoiceStatsReporter();
            Assert.IsNull(r.Build(0, "native", null, null, null, null, null), "nothing happened");
            string line = r.Build(10000, "native", p, new[] { null, null, ch }, null, null, new VoiceTransportCounters { UpMessages = 50, UpBytes = 3000 });
            TestContext.WriteLine(line);
            StringAssert.Contains("tx frames=50", line);
            StringAssert.Contains("up_msgs=50", line);
            Assert.IsNull(r.Build(20000, "native", p, null, null, null, null), "no new activity → no line");
        }
    }
}
