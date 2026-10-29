using System;
using System.Collections.Generic;
using BirimizKovulacak.Voice;
using NUnit.Framework;

namespace BirimizKovulacak.VoiceTests
{
    /// <summary>Scripted call conversations for recorder / tape tests (dummy or real Opus payloads).</summary>
    sealed class CallScript
    {
        public struct Ev
        {
            public double At;
            public int Speaker, Partner, CallId;
            public ushort Seq;
            public VoiceFrameFlags Flags;
        }

        public readonly List<Ev> Events = new List<Ev>();
        readonly ushort[] _seq = { 100, 2000, 7000, 30000, 50000, 65500 };
        readonly Random _rnd;
        public double JitterMs;

        public CallScript(int seed = 1, double jitterMs = 0) { _rnd = new Random(seed); JitterMs = jitterMs; }

        /// <summary>One talk spurt: <paramref name="frames"/> frames from <paramref name="startMs"/> (send time),
        /// arriving with up to JitterMs of extra delay; the last frame carries EndOfSpeech.</summary>
        public CallScript Spurt(int callId, int speaker, int partner, double startMs, double seconds)
        {
            int frames = (int)Math.Round(seconds * 50);
            for (int k = 0; k < frames; k++)
            {
                Events.Add(new Ev
                {
                    At = startMs + k * 20 + _rnd.NextDouble() * JitterMs,
                    Speaker = speaker, Partner = partner, CallId = callId, Seq = _seq[speaker]++,
                    Flags = k == frames - 1 ? VoiceFrameFlags.EndOfSpeech : VoiceFrameFlags.None,
                });
            }
            return this;
        }

        public List<Ev> Sorted()
        {
            var l = new List<Ev>(Events);
            l.Sort((a, b) => a.At.CompareTo(b.At));
            return l;
        }

        public int RecordInto(VoiceCallRecorder rec, int payloadBytes = 40)
        {
            var payload = new byte[payloadBytes];
            int stored = 0;
            foreach (var e in Sorted())
            {
                payload[0] = (byte)e.Speaker;
                payload[1] = (byte)e.Seq;
                if (rec.Record(e.CallId, e.Speaker, e.Partner, e.Seq, e.Flags, payload, 0, payloadBytes, e.At)) stored++;
            }
            return stored;
        }
    }

    [TestFixture]
    public class RecorderTests
    {
        static List<int> FramesOf(VoiceCallRecorder rec, int callId)
        {
            var l = new List<int>();
            for (int f = rec.FirstFrameOf(callId); f >= 0; f = rec.FrameNext(f)) l.Add(f);
            return l;
        }

        [Test]
        public void Record_GroupsBothDirectionsPerCall_WithSpeakerSeqFlagsAndPayload()
        {
            var rec = new VoiceCallRecorder();
            var s = new CallScript();
            s.Spurt(7, 0, 1, 1000, 1.0).Spurt(7, 1, 0, 2200, 0.5)   // call 7: 0 ↔ 1
             .Spurt(9, 2, 3, 1500, 0.6);                             // call 9 at the same time: 2 ↔ 3
            Assert.AreEqual(50 + 25 + 30, s.RecordInto(rec));
            Assert.AreEqual(2, rec.CallCount);

            Assert.IsTrue(rec.TryGetCall(7, out var c7));
            Assert.AreEqual(75, c7.Frames);
            Assert.AreEqual(0b11, c7.SpeakersMask);
            Assert.AreEqual(0b11, c7.ParticipantsMask);
            Assert.AreEqual(1700, c7.LengthMs, 25, "from the first frame (1000) to the end of the last (2200+500)");
            Assert.IsTrue(rec.TryGetCall(9, out var c9));
            Assert.AreEqual(0b0100, c9.SpeakersMask, "only 2 talked");
            Assert.AreEqual(0b1100, c9.ParticipantsMask, "3 listened");

            var frames = FramesOf(rec, 7);
            Assert.AreEqual(75, frames.Count);
            int ends = 0;
            foreach (int f in frames)
            {
                int slot = rec.FrameSlot(f);
                Assert.That(slot, Is.EqualTo(0).Or.EqualTo(1), "only call 7 participants");
                int len = rec.FramePayload(f, out var buf, out int off);
                Assert.AreEqual(40, len);
                Assert.AreEqual(slot, buf[off], "payload kept intact");
                Assert.AreEqual((byte)rec.FrameSequence(f), buf[off + 1]);
                if ((rec.FrameFlags(f) & VoiceFrameFlags.EndOfSpeech) != 0) ends++;
            }
            Assert.AreEqual(2, ends);
            Assert.AreEqual(105L * 40, rec.PayloadBytes);
            Assert.AreEqual(105L * (40 + VoiceCallRecorder.FrameMetaBytes), rec.UsedBytes);
        }

        [Test]
        public void MediaTime_RemovesNetworkJitter_KeepsPauses()
        {
            var rec = new VoiceCallRecorder();
            // 45 ms of independent per-packet jitter on 20 ms frames: arrivals are jittery AND reordered.
            var s = new CallScript(seed: 3, jitterMs: 45);
            s.Spurt(1, 1, 0, 0, 0.2).Spurt(1, 0, 1, 500, 1.0).Spurt(1, 0, 1, 3500, 0.5);
            Assert.AreEqual(10 + 50 + 25, s.RecordInto(rec), "reordered frames are kept, not dropped");
            var frames = FramesOf(rec, 1).FindAll(f => rec.FrameSlot(f) == 0);
            frames.Sort((a, b) => VoiceSeq.Diff(rec.FrameSequence(a), rec.FrameSequence(b)));
            Assert.AreEqual(75, frames.Count);
            for (int k = 1; k < 50; k++)
                Assert.AreEqual(20, rec.FrameMediaMs(frames[k]) - rec.FrameMediaMs(frames[k - 1]), "inside a spurt frames are exactly 20 ms apart");
            Assert.AreEqual(500, rec.FrameMediaMs(frames[0]), 50, "spurt placed at its arrival time");
            int gap = rec.FrameMediaMs(frames[50]) - rec.FrameMediaMs(frames[0]);
            Assert.AreEqual(3000, gap, 60, "the pause between spurts is kept (arrival time)");
            for (int k = 51; k < 75; k++) Assert.AreEqual(20, rec.FrameMediaMs(frames[k]) - rec.FrameMediaMs(frames[k - 1]));
        }

        [Test]
        public void ReplayWindow_KeepsLateFrames_DropsDuplicatesAndAncientOnes()
        {
            var rec = new VoiceCallRecorder();
            var p = new byte[30];
            Assert.IsTrue(rec.Record(1, 0, 1, 10, 0, p, 0, 30, 0));
            Assert.IsTrue(rec.Record(1, 0, 1, 12, 0, p, 0, 30, 40));
            Assert.IsFalse(rec.Record(1, 0, 1, 12, 0, p, 0, 30, 41), "duplicate");
            Assert.IsTrue(rec.Record(1, 0, 1, 11, 0, p, 0, 30, 45), "late (reordered) frame is kept");
            Assert.IsFalse(rec.Record(1, 0, 1, 11, 0, p, 0, 30, 46), "duplicate of the late frame");
            Assert.IsTrue(rec.Record(1, 0, 1, 13, 0, p, 0, 30, 60));
            Assert.IsFalse(rec.Record(1, 0, 1, unchecked((ushort)(13 - 40)), 0, p, 0, 30, 61), "older than the replay window");
            Assert.AreEqual(3, rec.DuplicateFrames);
            Assert.AreEqual(4, rec.FrameCount);
            var media = new List<int>();
            for (int f = rec.FirstFrameOf(1); f >= 0; f = rec.FrameNext(f)) media.Add(rec.FrameMediaMs(f));
            CollectionAssert.AreEqual(new[] { 0, 40, 20, 60 }, media, "the late frame sits at its place on the timeline");
            Assert.IsFalse(rec.Record(-1, 0, 1, 13, 0, p, 0, 30, 60), "no call id");
            Assert.IsFalse(rec.Record(1, 9, 1, 13, 0, p, 0, 30, 60), "bad slot");
            Assert.AreEqual(2, rec.RejectedFrames);
        }

        [Test]
        public void Disabled_RecordsNothing()
        {
            var rec = new VoiceCallRecorder { Enabled = false };
            new CallScript().Spurt(1, 0, 1, 0, 1).RecordInto(rec);
            Assert.AreEqual(0, rec.FrameCount);
            Assert.AreEqual(0, rec.ReservedBytes, "no memory touched while disabled");
        }

        [Test]
        public void Select_PicksTheMostSpeechDenseWindow_StartingAtAWord()
        {
            var rec = new VoiceCallRecorder();
            var s = new CallScript(seed: 5, jitterMs: 30);
            // 30 s call between 1 and 2: sparse small talk, then a dense exchange from 15 s to 22 s, then sparse again.
            s.Spurt(42, 1, 2, 0, 0.8).Spurt(42, 2, 1, 4000, 0.6).Spurt(42, 1, 2, 9000, 1.0)
             .Spurt(42, 1, 2, 15000, 2.0).Spurt(42, 2, 1, 17050, 1.9).Spurt(42, 1, 2, 19000, 1.5).Spurt(42, 2, 1, 20550, 1.4)
             .Spurt(42, 1, 2, 26000, 0.7).Spurt(42, 2, 1, 29000, 0.8);
            s.RecordInto(rec);
            Assert.IsTrue(rec.TrySelectSnippet(new List<int> { 42 }, out var snip));
            TestContext.WriteLine(snip.ToString());
            Assert.AreEqual(42, snip.CallId);
            Assert.AreEqual(7000, snip.LengthMs);
            Assert.That(snip.StartMs, Is.InRange(14400, 15020), "the dense part, beginning just before the first word");
            Assert.Greater(snip.SpeechMs, 6400, "nearly all of the window is speech");
            Assert.AreEqual(0b110, snip.SpeakersMask, "both voices are on the tape");
            Assert.AreEqual(0b110, snip.ParticipantsMask);
            Assert.AreEqual(29800, snip.CallLengthMs, 80);
            // The first frame of the window is the first frame of a spurt (no mid-word start).
            int prevInside = -1;
            foreach (int f in FramesOf(rec, 42))
            {
                int m = rec.FrameMediaMs(f);
                if (m < snip.StartMs) prevInside = m;
            }
            Assert.Less(prevInside, snip.StartMs - 20, "silence right before the window start");
        }

        [Test]
        public void Select_FirstCandidateWithEnoughSpeech_InTheGivenOrder()
        {
            var rec = new VoiceCallRecorder();
            new CallScript().Spurt(1, 0, 1, 0, 1.0)                                // 1 s of speech in an 8 s call
                .Spurt(1, 1, 0, 7000, 0.02)
                .Spurt(2, 2, 3, 0, 3.0).Spurt(2, 3, 2, 3000, 3.0)                  // 6 s of speech
                .Spurt(3, 4, 5, 0, 5.0).RecordInto(rec);
            var order = new List<int> { 99, 1, 2, 3 };                             // 99 unknown (e.g. bot call)
            Assert.IsTrue(rec.TrySelectSnippet(order, out var a, 7000, 1500));
            Assert.AreEqual(2, a.CallId, "unknown and too-quiet candidates are skipped, then the given order wins");
            Assert.IsTrue(rec.TrySelectSnippet(order, out var b, 7000, 500));
            Assert.AreEqual(1, b.CallId, "a lower speech threshold accepts the first call");
            Assert.IsFalse(rec.TrySelectSnippet(new List<int> { 99, 1 }, out var none, 7000, 1500));
            Assert.AreEqual(-1, none.CallId);
            Assert.IsFalse(none.IsValid);
            Assert.IsFalse(rec.TrySelectSnippet((IList<int>)null, out _));
        }

        [Test]
        public void Select_ShortCall_IsUsedWhole()
        {
            var rec = new VoiceCallRecorder();
            new CallScript().Spurt(5, 0, 1, 0, 2.0).Spurt(5, 1, 0, 2500, 1.5).RecordInto(rec);
            Assert.IsTrue(rec.TrySelectSnippet(5, out var s));
            Assert.AreEqual(0, s.StartMs);
            Assert.AreEqual(4000, s.LengthMs, 40, "the whole 4 s call");
            Assert.AreEqual(3500, s.SpeechMs, 40);
        }

        [Test]
        public void EraseTape_ExcludesTodaysAndLaterCalls_UntilClear()
        {
            var rec = new VoiceCallRecorder();
            new CallScript().Spurt(1, 0, 2, 0, 3).Spurt(2, 3, 4, 0, 3).RecordInto(rec);
            rec.EraseSlot(2);   // slot 2 used "Kaydı Sil" (it never spoke in call 1, it only listened)
            Assert.IsTrue(rec.IsErased(2));
            Assert.IsFalse(rec.TrySelectSnippet(1, out _), "an erased participant's call is never played");
            Assert.IsTrue(rec.TrySelectSnippet(2, out _), "other calls are unaffected");
            int framesBefore = rec.FrameCount;
            new CallScript().Spurt(3, 2, 5, 10000, 2).Spurt(3, 5, 2, 12000, 2).RecordInto(rec);
            Assert.AreEqual(framesBefore, rec.FrameCount, "later calls of the erased slot are not even stored");
            Assert.AreEqual(200, rec.ErasedFrames);
            Assert.IsTrue(rec.TryGetCall(3, out var c3) && c3.Erased);
            rec.Clear();
            Assert.IsFalse(rec.IsErased(2), "a new day starts clean");
            new CallScript().Spurt(4, 2, 5, 0, 3).RecordInto(rec);
            Assert.IsTrue(rec.TrySelectSnippet(4, out _));
        }

        [Test]
        public void Cap_DropsNewFrames_NeverExceedsMaxBytes()
        {
            var rec = new VoiceCallRecorder { MaxBytes = VoiceCallRecorder.FrameChunkBytes + VoiceCallRecorder.PayloadChunkBytes };
            var s = new CallScript();
            for (int i = 0; i < 20; i++) s.Spurt(1, i % 2, 1 - i % 2, i * 5000, 4.0); // 4000 frames
            int stored = s.RecordInto(rec, payloadBytes: 60);
            TestContext.WriteLine($"stored {stored} of 4000, dropped {rec.DroppedFrames}, reserved {rec.ReservedBytes} B, used {rec.UsedBytes} B");
            Assert.Greater(rec.DroppedFrames, 0);
            Assert.LessOrEqual(rec.ReservedBytes, rec.MaxBytes);
            Assert.AreEqual(4000, stored + rec.DroppedFrames);
            Assert.IsTrue(rec.TrySelectSnippet(1, out var snip), "what fit is still playable");
            Assert.Greater(snip.SpeechMs, 3000);
        }

        [Test]
        public void Clear_KeepsChunksPooled_SecondDayRecordsWithoutAllocating_TrimReleases()
        {
            var rec = new VoiceCallRecorder();
            var day = new CallScript();
            for (int i = 0; i < 12; i++) day.Spurt(i / 3, i % 2, 1 - i % 2, i * 4000, 3.0); // 1800 frames, 4 calls
            var events = day.Sorted();
            var payload = new byte[45];
            void RecordDay()
            {
                foreach (var e in events) rec.Record(e.CallId, e.Speaker, e.Partner, e.Seq, e.Flags, payload, 0, payload.Length, e.At);
            }
            RecordDay();
            long reserved = rec.ReservedBytes;
            Assert.Greater(reserved, 0);
            var order = new List<int> { 0, 1, 2, 3 };
            Assert.IsTrue(rec.TrySelectSnippet(order, out _));
            rec.Clear();
            Assert.AreEqual(0, rec.FrameCount);
            Assert.AreEqual(0, rec.CallCount);
            Assert.AreEqual(reserved, rec.ReservedBytes, "chunks stay pooled");

            long before = GC.GetAllocatedBytesForCurrentThread();
            RecordDay();
            bool found = rec.TrySelectSnippet(order, out var snip);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(found);
            Assert.AreEqual(0, allocated, "day 2: recording 1800 frames + selecting a snippet allocates nothing");
            Assert.AreEqual(reserved, rec.ReservedBytes);

            rec.Trim();
            Assert.AreEqual(0, rec.ReservedBytes);
            Assert.AreEqual(0, rec.FrameCount);
        }

        [Test]
        public void Reserve_PreallocatesWithinTheCap()
        {
            var rec = new VoiceCallRecorder();
            rec.Reserve(1024 * 1024);
            Assert.That(rec.ReservedBytes, Is.InRange(1024 * 1024 - 2 * 65536, 1024 * 1024 + 2 * 65536));
            rec.Reserve(64L * 1024 * 1024);
            Assert.LessOrEqual(rec.ReservedBytes, rec.MaxBytes);
            var s = new CallScript();
            s.Spurt(1, 0, 1, 0, 30);
            long before = GC.GetAllocatedBytesForCurrentThread();
            s.RecordInto(rec);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            // RecordInto itself allocates its sorted event list and payload; the recorder adds nothing.
            Assert.Less(allocated, 200_000);
        }

        [TestCaseSource(typeof(Backends), nameof(Backends.All))]
        public void Memory_BytesPerTalkingMinute_IsMeasured(VoiceCodecBackend backend)
        {
            Backends.Require(backend);
            var rec = new VoiceCallRecorder();
            var src = SyntheticVoiceSource.Create("speech", 0);
            src.TalkSeconds = 1000; // continuous speech: worst case
            var sink = new RecordingSink(rec, callId: 1, speaker: 0, partner: 1);
            var capture = new VoiceCapturePipeline(VoiceCodec.CreateEncoder(VoiceEncoderSettings.Default, backend), 16000) { Sink = sink, PttHeld = true };
            var buf = new float[16000];
            for (int s = 0; s < 60; s++) { src.Read(buf, 0, buf.Length); capture.Process(buf, 0, buf.Length); sink.Now += 1000; }
            double minutes = rec.FrameCount * 0.02 / 60.0;
            double usedPerMin = rec.UsedBytes / minutes, payloadPerMin = rec.PayloadBytes / minutes;
            double reservedPerMin = rec.ReservedBytes / minutes;
            TestContext.WriteLine($"{backend}: {rec.FrameCount} frames ({minutes:0.00} min) → used {usedPerMin / 1024:0.0} KB/min " +
                                  $"(payload {payloadPerMin / 1024:0.0} KB/min, metadata {rec.FrameCount * 16 / minutes / 1024:0.0} KB/min), reserved {reservedPerMin / 1024:0.0} KB/min");
            // Week budget (docs/VOICE.md): recordings are cleared after every meeting, so the peak is one day.
            double dayCallMinutes = (40 + 2 * (16 + 150 + 12)) / 60.0; // first day of a session: Morning 40 s + 2 rounds
            double worstDay = 6 * dayCallMinutes * usedPerMin;          // all six talking non-stop through every phone phase
            double typicalDay = 6 * dayCallMinutes * 0.7 * 0.4 * usedPerMin; // 70 % of the time on the phone, 40 % of it talking
            TestContext.WriteLine($"  day of calls = {dayCallMinutes:0.0} min/player; worst day (6 players non-stop) {worstDay / 1048576.0:0.00} MB, " +
                                  $"typical day {typicalDay / 1048576.0:0.00} MB; 5-day week peak = one day (cleared per meeting)");
            Assert.Less(usedPerMin, 200 * 1024, "≤ 200 KB per talking minute");
            Assert.Less(worstDay, VoiceCallRecorder.DefaultMaxBytes, "the default cap holds the worst-case day");
        }

        sealed class RecordingSink : IVoiceFrameSink
        {
            readonly VoiceCallRecorder _rec;
            readonly int _call, _speaker, _partner;
            public double Now;
            int _k;

            public RecordingSink(VoiceCallRecorder rec, int callId, int speaker, int partner) { _rec = rec; _call = callId; _speaker = speaker; _partner = partner; }

            public void OnVoiceFrame(ushort sequence, VoiceFrameFlags flags, byte[] payload, int length)
            {
                _rec.Record(_call, _speaker, _partner, sequence, flags, payload, 0, length, _k++ * 20.0);
            }
        }
    }
}
