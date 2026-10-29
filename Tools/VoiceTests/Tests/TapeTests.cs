using System;
using System.Collections.Generic;
using BirimizKovulacak.Voice;
using NUnit.Framework;

namespace BirimizKovulacak.VoiceTests
{
    /// <summary>v3: recording playback (streamer → wire → tape player), tape/phone filters, speech levels.</summary>
    [TestFixture]
    public class TapeTests
    {
        sealed class Captured
        {
            public double At;
            public int Slot;
            public ushort Seq;
            public VoiceFrameFlags Flags;
            public byte[] Data;
        }

        sealed class CollectSink : IVoiceRecordingSink
        {
            public readonly List<Captured> Frames = new List<Captured>();
            public double Now;

            public void OnRecordingFrame(int slot, ushort sequence, VoiceFrameFlags flags, byte[] payload, int offset, int length)
            {
                var d = new byte[length];
                Buffer.BlockCopy(payload, offset, d, 0, length);
                Frames.Add(new Captured { At = Now, Slot = slot, Seq = sequence, Flags = flags, Data = d });
            }
        }

        // ------------------------------------------------------------------ streamer

        [Test]
        public void Streamer_ReplaysTheWindowAtRealTimePace()
        {
            var rec = new VoiceCallRecorder();
            new CallScript(seed: 2, jitterMs: 25).Spurt(3, 0, 5, 0, 2.0).Spurt(3, 5, 0, 2100, 3.0).Spurt(3, 0, 5, 5200, 3.0)
                .Spurt(3, 5, 0, 8300, 1.0).RecordInto(rec);
            Assert.IsTrue(rec.TrySelectSnippet(3, out var snip, 7000, 1500));
            int inWindow = 0;
            var mediaBySeq = new Dictionary<(int, ushort), int>();
            for (int f = rec.FirstFrameOf(3); f >= 0; f = rec.FrameNext(f))
            {
                int m = rec.FrameMediaMs(f);
                mediaBySeq[(rec.FrameSlot(f), rec.FrameSequence(f))] = m;
                if (m >= snip.StartMs && m < snip.StartMs + snip.LengthMs) inWindow++;
            }

            var streamer = new VoiceRecordingStreamer();
            var sink = new CollectSink();
            Assert.IsTrue(streamer.Begin(rec, snip, 1000));
            double dt = 1000.0 / 60.0;
            for (double t = 0; t < 1000 + snip.LengthMs + 1000; t += dt)
            {
                sink.Now = t;
                streamer.Tick(t, sink);
                if (streamer.IsDone) break;
            }
            Assert.IsTrue(streamer.IsDone);
            Assert.AreEqual(inWindow, sink.Frames.Count, "every frame of the window exactly once");
            Assert.AreEqual(inWindow, streamer.FramesSent);
            var lastEnd = new Dictionary<int, ushort>();
            int onTime = 0;
            foreach (var c in sink.Frames)
            {
                Assert.AreNotEqual(0, (int)(c.Flags & VoiceFrameFlags.Recording), "tape frames are flagged");
                double due = 1000 + mediaBySeq[(c.Slot, c.Seq)] - snip.StartMs;
                Assert.GreaterOrEqual(c.At, due - 1e-6, "never early");
                // A frame that reached the host out of order waits behind its successor (≤ 2 frames) — the
                // client's jitter buffer reorders it; everything else is sent within one update.
                Assert.Less(c.At, due + dt + 41, "never noticeably late");
                if (c.At < due + dt + 1) onTime++;
                if ((c.Flags & VoiceFrameFlags.EndOfSpeech) != 0) lastEnd[c.Slot] = c.Seq;
            }
            TestContext.WriteLine($"{onTime}/{sink.Frames.Count} frames within one update of their due time");
            Assert.Greater(onTime, sink.Frames.Count * 0.9);
            foreach (int slot in new[] { 0, 5 })
            {
                ushort maxSeq = 0;
                bool any = false;
                foreach (var c in sink.Frames)
                    if (c.Slot == slot && (!any || VoiceSeq.Diff(c.Seq, maxSeq) > 0)) { maxSeq = c.Seq; any = true; }
                Assert.IsTrue(lastEnd.ContainsKey(slot) && lastEnd[slot] == maxSeq || !any, "the speaker's last frame in the window ends the spurt");
            }
        }

        [Test]
        public void Streamer_NothingBeforeTheStartTime_StopIsImmediate()
        {
            var rec = new VoiceCallRecorder();
            new CallScript().Spurt(1, 0, 1, 0, 3).RecordInto(rec);
            Assert.IsTrue(rec.TrySelectSnippet(1, out var snip));
            var streamer = new VoiceRecordingStreamer();
            var sink = new CollectSink();
            Assert.IsFalse(streamer.Begin(rec, RecordingSnippet.None, 0), "invalid snippet");
            Assert.IsTrue(streamer.Begin(rec, snip, 5000));
            Assert.AreEqual(0, streamer.Tick(4999, sink));
            Assert.Greater(streamer.Tick(5100, sink), 0);
            streamer.Stop();
            Assert.IsFalse(streamer.Active);
            Assert.AreEqual(0, streamer.Tick(9000, sink));
        }

        [Test]
        public void TapeMessage_RoundTrip_AndRejectsGarbage()
        {
            var info = new VoiceTapeInfo
            {
                PlaybackId = 65000, CallId = 123456, StartServerTime = 98765.4321, CallOffsetMs = 14980,
                DurationMs = 7000, SpeakersMask = 0b100010, ParticipantsMask = 0b100011,
            };
            var buf = new byte[64];
            Assert.AreEqual(VoiceTapeMessage.Size, VoiceTapeMessage.Write(buf, VoiceTapeMessage.KindStart, info));
            Assert.IsTrue(VoiceTapeMessage.TryRead(buf, VoiceTapeMessage.Size, out byte kind, out var back));
            Assert.AreEqual(VoiceTapeMessage.KindStart, kind);
            Assert.AreEqual(info.PlaybackId, back.PlaybackId);
            Assert.AreEqual(info.CallId, back.CallId);
            Assert.AreEqual(info.StartServerTime, back.StartServerTime);
            Assert.AreEqual(info.CallOffsetMs, back.CallOffsetMs);
            Assert.AreEqual(info.DurationMs, back.DurationMs);
            Assert.AreEqual(info.SpeakersMask, back.SpeakersMask);
            Assert.AreEqual(info.ParticipantsMask, back.ParticipantsMask);
            Assert.IsTrue(back.HasSpeaker(5) && back.HasSpeaker(1) && !back.HasSpeaker(0));
            Assert.IsFalse(VoiceTapeMessage.TryRead(buf, VoiceTapeMessage.Size - 1, out _, out _), "short");
            buf[0] = 0x00;
            Assert.IsFalse(VoiceTapeMessage.TryRead(buf, VoiceTapeMessage.Size, out _, out _), "wrong version");
            buf[0] = VoiceTapeMessage.Version;
            buf[1] = 9;
            Assert.IsFalse(VoiceTapeMessage.TryRead(buf, VoiceTapeMessage.Size, out _, out _), "unknown kind");
        }

        // ------------------------------------------------------------------ filters and levels

        static double GainDb(Func<float, float> process, int rate, double freq)
        {
            var x = Signals.Sine(rate, 0.5, freq, 0.1f);
            var y = new float[x.Length];
            for (int i = 0; i < x.Length; i++) y[i] = process(x[i]);
            int skip = rate / 10;
            return 20 * Math.Log10(Signals.Rms(y, skip, y.Length - skip) / Signals.Rms(x, skip, x.Length - skip));
        }

        [Test]
        public void PhoneEq_IsSubtle_CutsBoomAndAir_KeepsTheVoiceBand()
        {
            var eq = PhoneEq.Create(48000);
            double lo = GainDb(eq.Process, 48000, 90), mid = GainDb(eq.Process, 48000, 1000), pres = GainDb(eq.Process, 48000, 1800);
            double hi = GainDb(eq.Process, 48000, 7500);
            TestContext.WriteLine($"phone EQ: 90 Hz {lo:0.0} dB, 1 kHz {mid:0.0} dB, 1.8 kHz {pres:0.0} dB, 7.5 kHz {hi:0.0} dB");
            Assert.Less(lo, -10, "desk rumble / boom removed");
            Assert.That(mid, Is.InRange(-1.5, 2.5), "voice band untouched");
            Assert.That(pres, Is.InRange(1.0, 3.5), "slight presence lift");
            Assert.Less(hi, -3, "a little air removed");
            Assert.Greater(hi, -15, "but not a 3.4 kHz telephone: clarity first");
        }

        [Test]
        public void TapeFilter_BandPass_QuietHiss_FadesOutWithoutClick()
        {
            var tape = new TapeFilter(48000) { HissLevel = 0 };
            double Gain(double f)
            {
                tape.Reset();
                var x = Signals.Sine(48000, 0.5, f, 0.1f);
                var y = (float[])x.Clone();
                tape.Process(y, 0, y.Length, 1f);
                return 20 * Math.Log10(Signals.Rms(y, 4800, y.Length - 4800) / Signals.Rms(x, 4800, x.Length - 4800));
            }
            double g150 = Gain(150), g1k = Gain(1000), g6k = Gain(6000);
            TestContext.WriteLine($"tape: 150 Hz {g150:0.0} dB, 1 kHz {g1k:0.0} dB, 6 kHz {g6k:0.0} dB");
            Assert.That(g1k, Is.InRange(-1.0, 5.0));
            Assert.Less(g150 - g1k, -12, "telephone band: lows gone");
            Assert.Less(g6k - g1k, -15, "telephone band: highs gone");

            var hissy = new TapeFilter(48000);
            var silence = new float[48000];
            hissy.Process(silence, 0, silence.Length, 1f);
            double hissDb = 20 * Math.Log10(Signals.Rms(silence, 24000, 24000));
            TestContext.WriteLine($"hiss {hissDb:0.0} dBFS");
            Assert.That(hissDb, Is.InRange(-60.0, -42.0), "audible but light tape hiss");
            var tail = new float[48000];
            hissy.Process(tail, 0, tail.Length, 0f);
            Assert.Less(Math.Abs(tail[0] - silence[silence.Length - 1]), 0.02f, "no jump when the fade starts");
            Assert.Less(Signals.Rms(tail, 24000, 24000), 1e-5f, "hiss fades out");
            foreach (float s in tail) Assert.IsFalse(float.IsNaN(s));
        }

        [Test]
        public void Playback_PhoneEqAndTapGain()
        {
            var enc = VoiceCodec.CreateEncoder(VoiceEncoderSettings.Default);
            var speech = SyntheticVoiceSource.Create("speech", 0);
            speech.TalkSeconds = 100;
            var pcm16 = new float[16000 * 2];
            speech.Read(pcm16, 0, pcm16.Length);
            var pkts = new List<byte[]>();
            var buf = new byte[200];
            var frame = new float[320];
            for (int f = 0; f < 100; f++)
            {
                Array.Copy(pcm16, f * 320, frame, 0, 320);
                int n = enc.Encode(frame, buf, 0, 200);
                var p = new byte[n];
                Buffer.BlockCopy(buf, 0, p, 0, n);
                pkts.Add(p);
            }
            float[] Play(VoiceFrameFlags flags, bool eq, out float level)
            {
                var ch = new VoicePlaybackChannel(0, VoiceCodec.CreateDecoder(48000)) { PhoneEqEnabled = eq };
                var all = new float[100 * 960];
                int got = 0;
                float maxLevel = 0;
                for (int f = 0; f < 100; f++)
                {
                    ch.Receive((ushort)f, flags, pkts[f], 0, pkts[f].Length, f * 20);
                    ch.Pump(f * 20, 960 * 2);
                    maxLevel = Math.Max(maxLevel, ch.FrameLevel01);
                    got += ch.Ring.Read(all, got, Math.Min(960, all.Length - got));
                }
                level = maxLevel;
                return all;
            }
            int s0 = 20 * 960, len = 60 * 960;
            var flat = Play(VoiceFrameFlags.Call, false, out float lvl);
            var phone = Play(VoiceFrameFlags.Call, true, out _);
            var tap = Play(VoiceFrameFlags.Call | VoiceFrameFlags.Tap, true, out _);
            float rFlat = Signals.Rms(flat, s0, len), rPhone = Signals.Rms(phone, s0, len), rTap = Signals.Rms(tap, s0, len);
            double lowFlat = Signals.TonePower(flat, s0, len, 48000, 140), lowPhone = Signals.TonePower(phone, s0, len, 48000, 140);
            TestContext.WriteLine($"call flat {rFlat:0.000} phone {rPhone:0.000} tap {rTap:0.000}; 140 Hz power ratio {lowPhone / lowFlat:0.000}; level {lvl:0.00}");
            Assert.AreEqual(rFlat, rPhone, rFlat * 0.3f, "the phone EQ keeps the loudness (clarity first)");
            Assert.Less(lowPhone, lowFlat * 0.5, "the phone EQ thins out the fundamental");
            Assert.AreEqual(rPhone * 0.7f, rTap, rPhone * 0.05f, "a wiretap is a bit quieter than a call");
            Assert.Greater(lvl, 0.3f, "speech level of a talking player");
        }

        [Test]
        public void SpeechLevel_MapsAndSmooths()
        {
            Assert.AreEqual(0f, VoiceLevel.Speech01(-100));
            Assert.AreEqual(0f, VoiceLevel.Speech01(-50));
            Assert.AreEqual(1f, VoiceLevel.Speech01(-14));
            Assert.That(VoiceLevel.Speech01(-30), Is.InRange(0.45f, 0.65f), "normal speech sits mid-range");
            float v = 0;
            for (int i = 0; i < 6; i++) v = VoiceLevel.Smooth(v, 1f, 1f / 60f, 0.03f, 0.12f);
            Assert.Greater(v, 0.9f, "attack ≈ 0.1 s to full");
            for (int i = 0; i < 6; i++) v = VoiceLevel.Smooth(v, 0f, 1f / 60f, 0.03f, 0.12f);
            Assert.That(v, Is.InRange(0.3f, 0.6f), "release is slower than the attack");
            for (int i = 0; i < 60; i++) v = VoiceLevel.Smooth(v, 0f, 1f / 60f);
            Assert.Less(v, 0.01f);
            var ch = new VoicePlaybackChannel(0, VoiceCodec.CreateDecoder(48000));
            Assert.AreEqual(0f, ch.FrameLevel01, "silent before any audio");
        }

        // ------------------------------------------------------------------ end to end

        /// <summary>Real Opus: a 2-person call is recorded (with jitter), the best 7 s are selected, streamed
        /// through an impaired network into a tape player that is consumed in real time.</summary>
        sealed class TapeRun
        {
            public int Sent, Received, MaxSpeakerSwitches;
            public float MaxLevel;
            public readonly List<int> SpeakerTimeline = new List<int>();
            public double EndedAtMs = -1, EndSentAtMs;
            public float VoiceRms, GapRms;
            public long DecodeErrors, FramesOut;
            public RecordingSnippet Snippet;
        }

        static TapeRun RunTape(VoiceCodecBackend backend, double joinAtElapsedMs, int seed)
        {
            var rnd = new Random(seed);
            var rec = new VoiceCallRecorder();
            var enc1 = VoiceCodec.CreateEncoder(VoiceEncoderSettings.Default, backend);
            var enc4 = VoiceCodec.CreateEncoder(VoiceEncoderSettings.Default, backend);
            var tone1 = Signals.Harmonic(16000, 10, 220, 0.3f);
            var tone4 = Signals.Harmonic(16000, 10, 330, 0.3f);
            var frame = new float[320];
            var pkt = new byte[200];
            ushort s1 = 500, s4 = 9000;
            // Call 11 (≈ 6.9 s, used whole): 1 and 4 alternate; a clear 0.6 s pause after the first spurt.
            var spurts = new (int slot, double start, double seconds)[] { (1, 0, 1.5), (4, 2100, 1.5), (1, 3700, 1.2), (4, 5000, 1.0), (1, 6100, 0.8) };
            var arrivals = new List<(double at, int slot, ushort seq, VoiceFrameFlags flags, byte[] data)>();
            foreach (var sp in spurts)
            {
                int frames = (int)(sp.seconds * 50);
                for (int k = 0; k < frames; k++)
                {
                    var src = sp.slot == 1 ? tone1 : tone4;
                    Array.Copy(src, (int)((sp.start / 1000.0 + k * 0.02) * 16000) % (src.Length - 320), frame, 0, 320);
                    int n = (sp.slot == 1 ? enc1 : enc4).Encode(frame, pkt, 0, 200);
                    var d = new byte[n];
                    Buffer.BlockCopy(pkt, 0, d, 0, n);
                    ushort seq = sp.slot == 1 ? s1++ : s4++;
                    arrivals.Add((sp.start + k * 20 + rnd.NextDouble() * 30, sp.slot, seq, k == frames - 1 ? VoiceFrameFlags.EndOfSpeech : 0, d));
                }
            }
            arrivals.Sort((a, b) => a.at.CompareTo(b.at));
            foreach (var a in arrivals) rec.Record(11, a.slot, a.slot == 1 ? 4 : 1, a.seq, a.flags, a.data, 0, a.data.Length, a.at);

            var run = new TapeRun();
            Assert.IsTrue(rec.TrySelectSnippet(new List<int> { 11 }, out run.Snippet, 7000, 1500));
            var streamer = new VoiceRecordingStreamer();
            var player = new VoiceTapePlayer(VoiceCodec.CreateDecoder(48000, backend), VoiceCodec.CreateDecoder(48000, backend));
            var sink = new CollectSink();
            double startMs = 500;
            Assert.IsTrue(streamer.Begin(rec, run.Snippet, startMs));
            if (joinAtElapsedMs <= 0) player.Begin(); // start message arrives before the first frame

            var net = new List<(double due, Captured c)>();
            var output = new List<float>();
            var outBuf = new float[4096];
            double dt = 1000.0 / 60.0, now = 0, endMsgDue = -1;
            int lastSpeaker = -1;
            bool joined = joinAtElapsedMs <= 0;
            for (int u = 0; u < 900; u++)
            {
                now += dt;
                // host
                sink.Now = now;
                int before = sink.Frames.Count;
                streamer.Tick(now, sink);
                for (int i = before; i < sink.Frames.Count; i++)
                {
                    run.Sent++;
                    if (rnd.NextDouble() < 0.03) continue; // 3 % loss
                    net.Add((now + 20 + rnd.NextDouble() * 40, sink.Frames[i]));
                }
                if (streamer.IsDone && endMsgDue < 0) { endMsgDue = now + 30; run.EndSentAtMs = now; streamer.Stop(); }
                // client
                if (!joined && now >= startMs + joinAtElapsedMs) { joined = true; player.Begin(); } // late start message
                for (int i = net.Count - 1; i >= 0; i--)
                {
                    if (net[i].due > now) continue;
                    var c = net[i].c;
                    net.RemoveAt(i);
                    if (joined && player.Receive(c.Slot, c.Seq, c.Flags, c.Data, 0, c.Data.Length, now)) run.Received++;
                }
                if (endMsgDue > 0 && now >= endMsgDue) { player.End(now); endMsgDue = double.MaxValue; }
                player.Pump(now, 960 * 3);
                int want = (int)(dt * 48);
                int got = player.Ring.Read(outBuf, 0, want);
                for (int i = 0; i < got; i++) output.Add(outBuf[i]);
                for (int i = got; i < want; i++) output.Add(0f);
                run.MaxLevel = Math.Max(run.MaxLevel, player.Level01);
                if (player.CurrentSpeaker != lastSpeaker && player.CurrentSpeaker >= 0)
                {
                    run.SpeakerTimeline.Add(player.CurrentSpeaker);
                    lastSpeaker = player.CurrentSpeaker;
                }
                if (joined && !player.Active && run.EndedAtMs < 0 && endMsgDue == double.MaxValue) run.EndedAtMs = now;
            }
            run.DecodeErrors = player.DecodeErrors;
            run.FramesOut = player.FramesOut;
            // Loudness in a spurt vs in the pause (hiss): output time ≈ start + media time + ~100-250 ms latency.
            int At(double mediaMs) => (int)((startMs + mediaMs - run.Snippet.StartMs + 150) * 48);
            int vs = At(500), gs = At(1750);
            if (vs + 9600 < output.Count) run.VoiceRms = RmsOf(output, vs, 9600);
            if (gs + 9600 < output.Count) run.GapRms = RmsOf(output, gs, 9600);
            player.Dispose();
            return run;
        }

        static float RmsOf(List<float> x, int start, int count)
        {
            double s = 0;
            for (int i = start; i < start + count; i++) s += x[i] * (double)x[i];
            return (float)Math.Sqrt(s / count);
        }

        [TestCaseSource(typeof(Backends), nameof(Backends.All))]
        public void Tape_EndToEnd_TwoVoices_ThroughTheTapeFilter(VoiceCodecBackend backend)
        {
            Backends.Require(backend);
            var run = RunTape(backend, 0, 7);
            TestContext.WriteLine($"{backend}: {run.Snippet}; sent {run.Sent} received {run.Received} out {run.FramesOut} frames; " +
                                  $"max level {run.MaxLevel:0.00}; speakers {string.Join(",", run.SpeakerTimeline)}; voice rms {run.VoiceRms:0.000} " +
                                  $"pause rms {run.GapRms:0.0000}; ended {run.EndedAtMs - run.EndSentAtMs:0} ms after the end message was sent");
            Assert.AreEqual(0, run.DecodeErrors);
            Assert.Greater(run.Received, run.Sent * 0.9, "the tape arrived");
            Assert.Greater(run.MaxLevel, 0.3f, "a waveform to show");
            Assert.That(run.SpeakerTimeline.Count, Is.GreaterThanOrEqualTo(3), "the nameplate follows the voices");
            Assert.AreEqual(1, run.SpeakerTimeline[0], "the call starts with slot 1");
            CollectionAssert.IsSubsetOf(new[] { 1, 4 }, run.SpeakerTimeline);
            Assert.Greater(run.VoiceRms, 0.02f, "voice audible through the band-pass");
            Assert.That(run.GapRms, Is.InRange(1e-4f, 0.02f), "between words: only the light hiss");
            Assert.Greater(run.EndedAtMs, 0, "the tape ends by itself after the host's end");
            Assert.Less(run.EndedAtMs - run.EndSentAtMs, 1600, "drains quickly");
        }

        [Test]
        public void Tape_ClientJoiningMidPlayback_HearsTheRest()
        {
            var backend = NativeOpus.IsAvailable ? VoiceCodecBackend.Native : VoiceCodecBackend.Managed;
            var full = RunTape(backend, 0, 9);
            var late = RunTape(backend, 3500, 9);
            TestContext.WriteLine($"full: received {full.Received} out {full.FramesOut}; joined at 3.5 s: received {late.Received} out {late.FramesOut}, speakers {string.Join(",", late.SpeakerTimeline)}");
            Assert.That(late.Received, Is.InRange(full.Received * 0.3, full.Received * 0.7), "about the second half");
            Assert.Greater(late.MaxLevel, 0.3f);
            Assert.IsNotEmpty(late.SpeakerTimeline);
            Assert.AreEqual(0, late.DecodeErrors);
            Assert.Greater(late.EndedAtMs, 0, "ends cleanly too");
        }

        [Test]
        public void TapePlayer_IgnoresFramesWhileIdle_ThirdVoiceDropped()
        {
            var player = new VoiceTapePlayer(VoiceCodec.CreateDecoder(48000), VoiceCodec.CreateDecoder(48000));
            var p = new byte[10];
            Assert.IsFalse(player.Receive(1, 0, VoiceFrameFlags.Recording, p, 0, 10, 0), "idle");
            player.Begin();
            Assert.IsTrue(player.Receive(1, 0, VoiceFrameFlags.Recording, p, 0, 10, 0));
            Assert.IsTrue(player.Receive(2, 0, VoiceFrameFlags.Recording, p, 0, 10, 0));
            Assert.IsFalse(player.Receive(3, 0, VoiceFrameFlags.Recording, p, 0, 10, 0), "a call has two sides");
            Assert.AreEqual(1, player.Dropped);
            player.End(0);
            Assert.IsTrue(player.Draining);
            player.Stop();
            Assert.IsFalse(player.Active);
        }

        // ------------------------------------------------------------------ allocations

        [Test]
        public void HotPath_V3_CallsTapRecorderAndTape_AllocateNothingPerFrame()
        {
            Backends.Require(VoiceCodecBackend.Native);
            var world = new FakeWorld { Mode = VoiceMode.Calls };
            for (int i = 0; i < 4; i++) world.Active[i] = true;
            world.Call(0, 1);
            world.Tap(2, 0);
            var router = new ProximityVoiceRouter(world, world, world, world);
            var recorder = new VoiceCallRecorder();
            recorder.Reserve(1024 * 1024);
            var sink = new BundleSink { Slot = 0 };
            var capture = new VoiceCapturePipeline(VoiceCodec.CreateEncoder(VoiceEncoderSettings.Default, VoiceCodecBackend.Native), 48000) { Sink = sink, PttHeld = true };
            var down = new VoiceBundleWriter[6];
            for (int i = 0; i < 6; i++) down[i] = new VoiceBundleWriter();
            var call = new VoicePlaybackChannel(0, VoiceCodec.CreateDecoder(48000, VoiceCodecBackend.Native));
            var tapped = new VoicePlaybackChannel(0, VoiceCodec.CreateDecoder(48000, VoiceCodecBackend.Native));
            var routes = new VoiceRoute[6];
            var reader = new VoiceBundleReader();
            var outCall = new PcmPlayoutReader();
            var outTap = new PcmPlayoutReader();
            var outTape = new PcmPlayoutReader();
            outTape.SetRates(48000, 44100);
            var unity = new float[960 * 2];
            var unity44 = new float[882 * 2];
            var mic = Signals.Harmonic(48000, 1.0, 200, 0.3f);
            var chunk = new float[960];

            // A recorded call to replay on the tape, on loop.
            var tapeSource = new VoiceCallRecorder();
            var tapeEvents = new CallScript().Spurt(5, 3, 4, 0, 3).Spurt(5, 4, 3, 3100, 3);
            var enc = VoiceCodec.CreateEncoder(VoiceEncoderSettings.Default, VoiceCodecBackend.Native);
            var pkt = new byte[200];
            var tone = Signals.Harmonic(16000, 0.02, 250, 0.3f);
            int plen = enc.Encode(tone, pkt, 0, 200);
            foreach (var e in tapeEvents.Sorted()) tapeSource.Record(5, e.Speaker, e.Partner, e.Seq, e.Flags, pkt, 0, plen, e.At);
            Assert.IsTrue(tapeSource.TrySelectSnippet(5, out var snip, 7000, 1000));
            var streamer = new VoiceRecordingStreamer();
            var tape = new VoiceTapePlayer(VoiceCodec.CreateDecoder(48000, VoiceCodecBackend.Native), VoiceCodec.CreateDecoder(48000, VoiceCodecBackend.Native));
            var tapeSink = new TapeFeed(tape);

            double now = 0;
            int micPos = 0;
            void Iteration()
            {
                now += 20;
                Array.Copy(mic, micPos, chunk, 0, 960);
                micPos = (micPos + 960) % (mic.Length - 960);
                capture.Process(chunk, 0, 960);
                reader.Begin(sink.Writer.Buffer, sink.Writer.Length);
                while (reader.TryNext(out var f))
                {
                    int n = router.Route(0, routes);
                    if (router.LastCallId >= 0)
                        recorder.Record(router.LastCallId, 0, router.LastCallPartner, f.Sequence, f.Flags, sink.Writer.Buffer, f.PayloadOffset, f.PayloadLength, now);
                    for (int r = 0; r < n; r++)
                        down[routes[r].Recipient].TryAppend(0, f.Sequence, routes[r].Flags | (f.Flags & VoiceFrameFlags.EndOfSpeech), sink.Writer.Buffer, f.PayloadOffset, f.PayloadLength);
                }
                sink.Writer.Reset();
                Deliver(down[1], call, ref reader, now);
                Deliver(down[2], tapped, ref reader, now);
                call.Pump(now, 960 * 3);
                tapped.Pump(now, 960 * 3);
                // Tape on loop.
                if (!streamer.Active || streamer.IsDone) { streamer.Begin(tapeSource, snip, now); tape.Begin(); }
                tapeSink.Now = now;
                streamer.Tick(now, tapeSink);
                tape.Pump(now, 960 * 3);
                for (int i = 0; i < unity.Length; i++) unity[i] = 1f;
                outCall.MultiplyInto(call.Ring, unity, 2);
                for (int i = 0; i < unity.Length; i++) unity[i] = 1f;
                outTap.MultiplyInto(tapped.Ring, unity, 2);
                for (int i = 0; i < unity44.Length; i++) unity44[i] = 1f;
                outTape.MultiplyInto(tape.Ring, unity44, 2);
            }

            for (int i = 0; i < 400; i++) Iteration();
            recorder.Clear(); // a new day: pooled chunks are reused
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2000; i++) Iteration();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            TestContext.WriteLine($"allocated {allocated} B over 2000 frames; call played {call.Jitter.Played}, tap played {tapped.Jitter.Played}, " +
                                  $"recorded {recorder.FrameCount}, tape out {tape.FramesOut}");
            Assert.Greater(call.Jitter.Played, 1500);
            Assert.Greater(tapped.Jitter.Played, 1500, "the tapper hears the call");
            Assert.AreEqual(2000, recorder.FrameCount, "every call frame recorded");
            Assert.Greater(tape.FramesOut, 1500, "the tape kept running");
            Assert.AreEqual(0, allocated, "route + taps + recording + tape playback must not allocate");
        }

        sealed class TapeFeed : IVoiceRecordingSink
        {
            readonly VoiceTapePlayer _tape;
            public double Now;
            public TapeFeed(VoiceTapePlayer tape) { _tape = tape; }
            public void OnRecordingFrame(int slot, ushort sequence, VoiceFrameFlags flags, byte[] payload, int offset, int length)
            {
                _tape.Receive(slot, sequence, flags, payload, offset, length, Now);
            }
        }

        static void Deliver(VoiceBundleWriter w, VoicePlaybackChannel ch, ref VoiceBundleReader reader, double now)
        {
            reader.Begin(w.Buffer, w.Length);
            while (reader.TryNext(out var f)) ch.Receive(f.Sequence, f.Flags, w.Buffer, f.PayloadOffset, f.PayloadLength, now);
            w.Reset();
        }
    }
}
