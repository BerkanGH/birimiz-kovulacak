using System.Collections.Generic;
using BirimizKovulacak.Voice;
using NUnit.Framework;

namespace BirimizKovulacak.VoiceTests
{
    [TestFixture]
    public class JitterBufferTests
    {
        static readonly byte[] Payload = { 10, 20, 30, 40 };

        static void Put(VoiceJitterBuffer jb, ushort seq, double now, VoiceFrameFlags flags = VoiceFrameFlags.None)
        {
            Payload[0] = (byte)seq;
            jb.Insert(seq, flags, Payload, 0, Payload.Length, now);
        }

        /// <summary>Pulls until Idle/Waiting; returns a trace like "F5 F6 L F8".</summary>
        static List<string> Drain(VoiceJitterBuffer jb, double now, int max = 50)
        {
            var trace = new List<string>();
            for (int i = 0; i < max; i++)
            {
                var r = jb.Pull(now, out int idx);
                if (r == JitterPull.Idle || r == JitterPull.Waiting) { trace.Add(r.ToString()); break; }
                if (r == JitterPull.Frame) trace.Add("F" + jb.PayloadAt(idx)[0]);
                else if (r == JitterPull.Fec) trace.Add("E" + jb.PayloadAt(idx)[0]);
                else trace.Add("L");
            }
            return trace;
        }

        [Test]
        public void WaitsForTargetDepth_ThenPlaysInOrder()
        {
            var jb = new VoiceJitterBuffer();
            Assert.AreEqual(JitterPull.Idle, jb.Pull(0, out _));
            Put(jb, 5, 0);
            Assert.AreEqual(JitterPull.Waiting, jb.Pull(1, out _), "one packet < 60 ms target");
            Put(jb, 6, 20);
            Put(jb, 7, 40);
            Assert.AreEqual(JitterPull.Waiting, jb.Pull(41, out _), "oldest waited only 41 ms of the 60 ms delay");
            Put(jb, 8, 60);
            CollectionAssert.AreEqual(new[] { "F5", "F6", "F7", "F8", "L", "L", "L", "Idle" }, Drain(jb, 61));
            Assert.AreEqual(4, jb.Played);
            Assert.IsFalse(jb.IsPlaying);
        }

        [Test]
        public void StartsAfterTargetDelay_EvenWithOnePacket()
        {
            var jb = new VoiceJitterBuffer();
            Put(jb, 100, 0);
            Assert.AreEqual(JitterPull.Waiting, jb.Pull(30, out _));
            Assert.AreEqual(JitterPull.Frame, jb.Pull(61, out _), "waited ≥ target delay");
        }

        [Test]
        public void EndOfSpeech_StartsImmediately_AndStopsWithoutConcealment()
        {
            var jb = new VoiceJitterBuffer();
            Put(jb, 1, 0);
            Put(jb, 2, 20, VoiceFrameFlags.EndOfSpeech);
            CollectionAssert.AreEqual(new[] { "F1", "F2", "Idle" }, Drain(jb, 21));
            Assert.AreEqual(0, jb.Concealed);
        }

        [Test]
        public void Reorders_OutOfOrderArrivals()
        {
            var jb = new VoiceJitterBuffer();
            Put(jb, 12, 0);
            Put(jb, 10, 1);
            Put(jb, 11, 2);
            Put(jb, 13, 3, VoiceFrameFlags.EndOfSpeech);
            CollectionAssert.AreEqual(new[] { "F10", "F11", "F12", "F13", "Idle" }, Drain(jb, 4));
        }

        [Test]
        public void Loss_UsesFecWhenNextPacketQueued_PlcOtherwise()
        {
            var jb = new VoiceJitterBuffer();
            Put(jb, 1, 0);
            Put(jb, 3, 0);   // 2 lost, 3 present → FEC from 3
            Put(jb, 4, 0);
            Put(jb, 7, 0, VoiceFrameFlags.EndOfSpeech); // 5, 6 lost; 6 recovered from 7's FEC
            CollectionAssert.AreEqual(new[] { "F1", "E3", "F3", "F4", "L", "E7", "F7", "Idle" }, Drain(jb, 1));
            Assert.AreEqual(2, jb.FecRecovered);
            Assert.AreEqual(3, jb.Concealed);
        }

        [Test]
        public void LatePacket_AfterItsSlotPlayed_IsDropped()
        {
            var jb = new VoiceJitterBuffer();
            Put(jb, 1, 0);
            Put(jb, 3, 0);
            Put(jb, 4, 0);
            Put(jb, 5, 0);
            Assert.AreEqual(JitterPull.Frame, jb.Pull(0, out _));  // 1
            Assert.AreEqual(JitterPull.Fec, jb.Pull(0, out _));    // 2 concealed
            Assert.IsFalse(jb.Insert(2, VoiceFrameFlags.None, Payload, 0, 4, 50), "2 arrives too late");
            Assert.AreEqual(1, jb.Late);
            Assert.IsFalse(jb.Insert(1, VoiceFrameFlags.None, Payload, 0, 4, 50));
            Assert.AreEqual(2, jb.Late);
        }

        [Test]
        public void Duplicates_AreIgnored()
        {
            var jb = new VoiceJitterBuffer();
            Put(jb, 8, 0);
            Assert.IsFalse(jb.Insert(8, VoiceFrameFlags.None, Payload, 0, 4, 1));
            Assert.AreEqual(1, jb.Duplicates);
            Assert.AreEqual(1, jb.BufferedCount);
        }

        [Test]
        public void SequenceWrapAround_PlaysContinuously()
        {
            var jb = new VoiceJitterBuffer();
            Put(jb, 65534, 0);
            Put(jb, 0, 0);
            Put(jb, 65535, 0);
            Put(jb, 1, 0, VoiceFrameFlags.EndOfSpeech);
            var t = Drain(jb, 0);
            CollectionAssert.AreEqual(new[] { "F254", "F255", "F0", "F1", "Idle" }, t);
        }

        [Test]
        public void NewSpurt_AfterSilence_ContinuesSequence()
        {
            var jb = new VoiceJitterBuffer();
            Put(jb, 1, 0);
            Put(jb, 2, 20, VoiceFrameFlags.EndOfSpeech);
            Drain(jb, 21);
            Put(jb, 3, 1000);
            Put(jb, 4, 1020);
            Put(jb, 5, 1040);
            CollectionAssert.AreEqual(new[] { "F3", "F4", "F5", "L", "L", "L", "Idle" }, Drain(jb, 1061));
            Assert.AreEqual(0, jb.Late);
        }

        [Test]
        public void StreamJump_Resynchronises()
        {
            var jb = new VoiceJitterBuffer();
            Put(jb, 10, 0);
            Put(jb, 11, 0);
            Put(jb, 12, 0);
            Put(jb, 13, 0);
            Drain(jb, 0, 2);
            // the speaker walked out of range and came back 5 s later: seq jumped by 250
            Put(jb, 262, 5000);
            Put(jb, 263, 5000);
            Put(jb, 264, 5000, VoiceFrameFlags.EndOfSpeech);
            var t = Drain(jb, 5000);
            CollectionAssert.AreEqual(new[] { "F6", "F7", "F8", "Idle" }, t); // 262 & 0xFF = 6
        }

        [Test]
        public void Burst_IsTrimmedToTargetLatency()
        {
            var jb = new VoiceJitterBuffer();
            for (ushort s = 0; s < 20; s++) Put(jb, s, 0);
            var r = jb.Pull(0, out int idx);
            Assert.AreEqual(JitterPull.Frame, r);
            Assert.LessOrEqual(jb.BufferedCount, jb.TargetFrames, "old frames skipped to cut latency");
            Assert.Greater(jb.Skipped, 0);
        }

        [Test]
        public void AdaptiveTarget_GrowsWithJitter_StaysAtBaseWhenCalm()
        {
            var calm = new VoiceJitterBuffer();
            var jittery = new VoiceJitterBuffer();
            var rnd = new System.Random(1);
            double t = 0;
            for (ushort s = 0; s < 200; s++)
            {
                Put(calm, s, s * 20.0 + 3);                    // steady 20 ms
                t = s * 20.0 + rnd.NextDouble() * 80;          // 0..80 ms random delay
                Put(jittery, s, t);
            }
            TestContext.WriteLine($"calm jitter {calm.JitterMs:0.0} ms → {calm.EstimateTargetFrames() * 20} ms; " +
                                  $"jittery {jittery.JitterMs:0.0} ms → {jittery.EstimateTargetFrames() * 20} ms");
            Assert.AreEqual(3, calm.EstimateTargetFrames(), "calm network keeps the 60 ms base");
            Assert.Greater(jittery.EstimateTargetFrames(), 3, "jitter raises the playout delay");
            Assert.LessOrEqual(jittery.EstimateTargetFrames() * 20, jittery.MaxDelayMs);
        }

        [Test]
        public void SpeechPauses_DoNotInflateJitter()
        {
            var jb = new VoiceJitterBuffer();
            ushort s = 0;
            for (int spurt = 0; spurt < 10; spurt++)
            {
                double start = spurt * 3000.0;
                for (int f = 0; f < 20; f++, s++)
                    Put(jb, s, start + f * 20.0, f == 19 ? VoiceFrameFlags.EndOfSpeech : VoiceFrameFlags.None);
            }
            Assert.Less(jb.JitterMs, 1.0);
            Assert.Less(jb.PeakDelayMs, 1.0);
        }

        [Test]
        public void Prefetch_DelaysStart_SoTheConsumerRingDoesNotEatTheMargin()
        {
            var jb = new VoiceJitterBuffer { PrefetchFrames = 2 };
            for (ushort s = 0; s < 4; s++) Put(jb, s, s * 20.0);
            Assert.AreEqual(JitterPull.Waiting, jb.Pull(61, out _), "60 ms target + 2 prefetch frames = 100 ms");
            Put(jb, 4, 80);
            Assert.AreEqual(JitterPull.Waiting, jb.Pull(81, out _));
            Put(jb, 5, 100);
            Assert.AreEqual(JitterPull.Frame, jb.Pull(101, out _));
        }

        [Test]
        public void MissingFrame_IsNotConcealedWhileTheConsumerHasAudio()
        {
            var jb = new VoiceJitterBuffer();
            for (ushort s = 0; s < 4; s++) Put(jb, s, 0);
            for (int k = 0; k < 4; k++) Assert.AreEqual(JitterPull.Frame, jb.Pull(0, false, out _));
            Assert.AreEqual(JitterPull.Waiting, jb.Pull(5, false, out _), "frame 4 not here yet, no urgency");
            Put(jb, 4, 10);
            Assert.AreEqual(JitterPull.Frame, jb.Pull(10, false, out int idx), "slightly late packet still played");
            Assert.AreEqual(4, jb.PayloadAt(idx)[0]);
            Assert.AreEqual(0, jb.Late);
            Assert.AreEqual(JitterPull.Lost, jb.Pull(30, true, out _), "urgent: conceal");
        }

        [Test]
        public void RisingDelay_StretchesPlayoutMidSpurt()
        {
            var jb = new VoiceJitterBuffer();
            double t = 0;
            ushort s = 0;
            for (; s < 4; s++, t += 20) Put(jb, s, t);
            Assert.AreEqual(JitterPull.Frame, jb.Pull(t, out _));
            Assert.AreEqual(3, jb.TargetFrames);
            // the network suddenly adds 80 ms: the next packets arrive late
            for (int k = 0; k < 3; k++, s++, t += 20) Put(jb, s, t + 80);
            Assert.Greater(jb.EstimateTargetFrames(), 3);
            Assert.AreEqual(JitterPull.Lost, jb.Pull(t, out _), "one concealed frame grows the delay");
            Assert.AreEqual(4, jb.TargetFrames);
            Assert.AreEqual(1, jb.Stretched);
        }
    }
}
