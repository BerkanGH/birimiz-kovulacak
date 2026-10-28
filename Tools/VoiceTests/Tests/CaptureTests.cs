using System;
using System.Linq;
using BirimizKovulacak.Voice;
using NUnit.Framework;

namespace BirimizKovulacak.VoiceTests
{
    [TestFixture]
    public class CaptureTests
    {
        [Test]
        public void Vad_OpensAfterAttack_HoldsForHangover_ThenCloses()
        {
            var vad = new VoiceActivityDetector { ThresholdDb = -40, AttackFrames = 2, HangoverFrames = 15 };
            for (int i = 0; i < 50; i++) Assert.IsFalse(vad.ProcessLevel(-80), "silence");
            Assert.IsFalse(vad.ProcessLevel(-20), "one loud frame is not enough (attack)");
            Assert.IsTrue(vad.ProcessLevel(-20), "second loud frame opens");
            for (int i = 0; i < 10; i++) Assert.IsTrue(vad.ProcessLevel(-22));
            for (int i = 0; i < 15; i++) Assert.IsTrue(vad.ProcessLevel(-80), "hangover frame " + i);
            Assert.IsFalse(vad.ProcessLevel(-80), "closes after 15 quiet frames (300 ms)");
        }

        [Test]
        public void Vad_ShortGapInsideSpeech_DoesNotClose()
        {
            var vad = new VoiceActivityDetector { ThresholdDb = -40 };
            for (int i = 0; i < 20; i++) vad.ProcessLevel(-80);
            for (int i = 0; i < 5; i++) vad.ProcessLevel(-25);
            for (int i = 0; i < 8; i++) Assert.IsTrue(vad.ProcessLevel(-70), "word gap");
            Assert.IsTrue(vad.ProcessLevel(-25));
        }

        [Test]
        public void Vad_IgnoresSteadyNoiseBelowThreshold_AndAdaptsToNoiseFloor()
        {
            var vad = new VoiceActivityDetector();
            vad.SetSensitivity(1f); // -60 dBFS threshold
            for (int i = 0; i < 400; i++) vad.ProcessLevel(-50); // constant fan noise above the threshold
            Assert.IsFalse(vad.IsActive, "steady noise is learned as the floor");
            Assert.IsTrue(vad.ProcessLevel(-30) | vad.ProcessLevel(-30), "speech above the floor still opens");
        }

        [Test]
        public void Vad_Sensitivity_MapsToThreshold()
        {
            var vad = new VoiceActivityDetector();
            vad.SetSensitivity(0); Assert.AreEqual(-25f, vad.ThresholdDb, 0.01f);
            vad.SetSensitivity(1); Assert.AreEqual(-60f, vad.ThresholdDb, 0.01f);
            vad.SetSensitivity(5); Assert.AreEqual(-60f, vad.ThresholdDb, 0.01f);
        }

        static VoiceCapturePipeline Pipeline(ListSink sink, int rate = 16000)
        {
            var enc = VoiceCodec.CreateEncoder(VoiceEncoderSettings.Default, VoiceCodecBackend.Auto);
            return new VoiceCapturePipeline(enc, rate, 100) { Sink = sink };
        }

        static void Feed(VoiceCapturePipeline p, float[] signal, int frames, ref int pos)
        {
            var chunk = new float[VoiceFormat.FrameSamples];
            for (int f = 0; f < frames; f++)
            {
                Array.Copy(signal, pos, chunk, 0, chunk.Length);
                pos += chunk.Length;
                p.Process(chunk, 0, chunk.Length);
            }
        }

        [Test]
        public void PushToTalk_SendsOnlyWhileHeld_PlusReleaseTail_EndsWithEndOfSpeech()
        {
            var sink = new ListSink();
            var p = Pipeline(sink);
            var tone = Signals.Harmonic(16000, 3.0);
            int pos = 0;
            Feed(p, tone, 10, ref pos);
            Assert.AreEqual(0, sink.Frames.Count, "PTT not held: nothing sent even with a loud mic");
            Assert.Greater(p.Level01, 0.3f, "meter still works");

            p.PttHeld = true;
            Feed(p, tone, 20, ref pos);
            p.PttHeld = false;
            Feed(p, tone, 20, ref pos);

            // 2 pre-roll + 20 held + 6 release tail + 1 closing frame
            Assert.AreEqual(2 + 20 + 6 + 1, sink.Frames.Count);
            Assert.AreEqual(VoiceFrameFlags.EndOfSpeech, sink.Frames.Last().flags);
            Assert.AreEqual(1, sink.Frames.Count(f => f.flags == VoiceFrameFlags.EndOfSpeech));
            for (int i = 1; i < sink.Frames.Count; i++)
                Assert.AreEqual((ushort)(sink.Frames[i - 1].seq + 1), sink.Frames[i].seq, "contiguous sequence");
            Assert.AreEqual(100, sink.Frames[0].seq);
            Assert.AreEqual(1, p.Spurts);
            Assert.IsFalse(p.IsTransmitting);
        }

        [Test]
        public void SelfMute_AndTransmitDisabled_SendNothing()
        {
            var sink = new ListSink();
            var p = Pipeline(sink);
            var tone = Signals.Harmonic(16000, 1.0);
            int pos = 0;
            p.PttHeld = true;
            p.SelfMuted = true;
            Feed(p, tone, 10, ref pos);
            p.SelfMuted = false;
            p.TransmitEnabled = false;
            Feed(p, tone, 10, ref pos);
            Assert.AreEqual(0, sink.Frames.Count);
        }

        [Test]
        public void OpenMic_FollowsVoiceActivity()
        {
            var sink = new ListSink();
            var p = Pipeline(sink);
            p.PushToTalk = false;
            var src = SyntheticVoiceSource.Create("sine", 0);
            src.TalkSeconds = 1.0;
            src.PauseSeconds = 1.0;
            var buf = new float[16000 * 4];
            src.Read(buf, 0, buf.Length);
            p.Process(buf, 0, buf.Length);
            int ends = sink.Frames.Count(f => (f.flags & VoiceFrameFlags.EndOfSpeech) != 0);
            TestContext.WriteLine($"open mic: {sink.Frames.Count} frames, {p.Spurts} spurts, {ends} end markers");
            Assert.AreEqual(2, p.Spurts, "two talk periods → two spurts");
            Assert.AreEqual(2, ends);
            // ~50 frames talk + preroll + 300 ms hangover per spurt; nothing during the rest of the pauses
            Assert.That(sink.Frames.Count, Is.InRange(2 * 50, 2 * (50 + 2 + 16 + 1)));
        }

        [Test]
        public void Capture_At48k_IsResampledTo20msFrames()
        {
            var sink = new ListSink();
            var p = Pipeline(sink, 48000);
            p.PttHeld = true;
            var tone = Signals.Harmonic(48000, 1.0);
            // feed in odd-sized chunks like a microphone ring would deliver
            int pos = 0, step = 731;
            var chunk = new float[step];
            while (pos + step <= tone.Length)
            {
                Array.Copy(tone, pos, chunk, 0, step);
                p.Process(chunk, 0, step);
                pos += step;
            }
            Assert.That(p.FramesIn, Is.InRange(48, 50), "≈ 1 s of 20 ms frames");
            Assert.AreEqual(p.FramesIn, sink.Frames.Count, "every frame sent while PTT held (no pre-roll history yet)");
        }

        [Test]
        public void Resampler_48kTo16k_KeepsToneAndRejectsAliases()
        {
            var rs = new StreamResampler(48000, 16000);
            var tone = Signals.Sine(48000, 1.0, 1000, 0.5f);
            var high = Signals.Sine(48000, 1.0, 12000, 0.5f); // would alias to 4 kHz without filtering
            var outTone = new float[rs.MaxOutput(tone.Length)];
            int n = 0;
            for (int off = 0; off < tone.Length; off += 480) n += rs.Process(tone, off, 480, outTone, n);
            Assert.That(n, Is.InRange(15998, 16002));
            Assert.AreEqual(0.5f / (float)Math.Sqrt(2), Signals.Rms(outTone, 1000, n - 1000), 0.02f, "1 kHz passes");
            double p1k = Signals.TonePower(outTone, 1000, n - 1000, 16000, 1000);
            Assert.Greater(p1k, 0.05);

            var rs2 = new StreamResampler(48000, 16000);
            var outHigh = new float[rs2.MaxOutput(high.Length)];
            int m = rs2.Process(high, 0, high.Length, outHigh, 0);
            float aliasRms = Signals.Rms(outHigh, 1000, m - 1000);
            TestContext.WriteLine($"12 kHz alias residue: {20 * Math.Log10(aliasRms / 0.3536):0.0} dB");
            Assert.Less(aliasRms, 0.3536f * 0.1f, "≥ 20 dB alias rejection");
        }

        [Test]
        public void Resampler_44100To16k_ProducesExpectedCount()
        {
            var rs = new StreamResampler(44100, 16000);
            var x = Signals.Sine(44100, 2.0, 300, 0.4f);
            var y = new float[rs.MaxOutput(x.Length)];
            int n = 0;
            for (int off = 0; off + 441 <= x.Length; off += 441) n += rs.Process(x, off, 441, y, n);
            Assert.That(n, Is.InRange(31998, 32002));
            Assert.Greater(Signals.TonePower(y, 2000, n - 2000, 16000, 300), Signals.TonePower(y, 2000, n - 2000, 16000, 600) * 100);
        }

        [Test]
        public void Resampler_Upsamples16kTo48k()
        {
            var rs = new StreamResampler(16000, 48000);
            var x = Signals.Sine(16000, 0.5, 500, 0.4f);
            var y = new float[rs.MaxOutput(x.Length)];
            int n = rs.Process(x, 0, x.Length, y, 0);
            Assert.That(n, Is.InRange(23998, 24002));
            Assert.AreEqual(0.4f / (float)Math.Sqrt(2), Signals.Rms(y, 100, n - 100), 0.02f);
        }
    }
}
