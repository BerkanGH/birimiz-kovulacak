using System;
using System.Collections.Generic;
using BirimizKovulacak.Voice;
using NUnit.Framework;

namespace BirimizKovulacak.VoiceTests
{
    /// <summary>
    /// Whole pipeline without Unity: capture → encode → up bundle → (network) → host parse + route → down bundles →
    /// (network) → client parse → jitter buffer → decode → ring → audio-thread reader.
    /// </summary>
    [TestFixture]
    public class PipelineTests
    {
        sealed class Packet
        {
            public double At;
            public byte[] Data;
            public int To; // -1 = host
        }

        sealed class SimNet
        {
            readonly Random _rnd;
            readonly List<Packet> _queue = new List<Packet>();
            public double BaseMs, JitterMs, Loss, Duplicate;
            public int Sent, Dropped;

            public SimNet(int seed) { _rnd = new Random(seed); }

            public void Send(byte[] buf, int len, int to, double now)
            {
                Sent++;
                if (_rnd.NextDouble() < Loss) { Dropped++; return; }
                int copies = _rnd.NextDouble() < Duplicate ? 2 : 1;
                for (int c = 0; c < copies; c++)
                {
                    var d = new byte[len];
                    Buffer.BlockCopy(buf, 0, d, 0, len);
                    _queue.Add(new Packet { At = now + BaseMs + _rnd.NextDouble() * JitterMs, Data = d, To = to });
                }
            }

            public List<Packet> Due(double now)
            {
                var due = new List<Packet>();
                for (int i = _queue.Count - 1; i >= 0; i--)
                    if (_queue[i].At <= now) { due.Add(_queue[i]); _queue.RemoveAt(i); }
                due.Sort((a, b) => a.At.CompareTo(b.At));
                return due;
            }
        }

        [TestCaseSource(typeof(Backends), nameof(Backends.All))]
        public void EndToEnd_ImpairedNetwork_ProximityAndCall(VoiceCodecBackend backend)
        {
            Backends.Require(backend);
            // 0 speaks; 1 stands 2 m away (proximity); 2 is across the office in a call with 0; 3 is far, not in call.
            var world = new FakeWorld().Place(0, 0, 0).Place(1, 2, 0).Place(2, 30, 10).Place(3, -30, -10);
            world.Call(0, 2);
            var router = new ProximityVoiceRouter(world, world, world, world);

            var sink = new BundleSink { Slot = 0 };
            var capture = new VoiceCapturePipeline(VoiceCodec.CreateEncoder(VoiceEncoderSettings.Default, backend), 48000, 7) { Sink = sink, PttHeld = true };
            var source = Signals.Harmonic(48000, 6.0, 180, 0.25f);

            var up = new SimNet(1) { BaseMs = 5, JitterMs = 10, Loss = 0.02 };
            var down = new SimNet(2) { BaseMs = 20, JitterMs = 45, Loss = 0.05, Duplicate = 0.01 };
            var downWriters = new VoiceBundleWriter[6];
            for (int i = 0; i < 6; i++) downWriters[i] = new VoiceBundleWriter();
            var channels = new VoicePlaybackChannel[6];
            for (int i = 0; i < 6; i++) channels[i] = new VoicePlaybackChannel(0, VoiceCodec.CreateDecoder(48000, backend));
            var flagsSeen = new HashSet<VoiceFrameFlags>[6];
            for (int i = 0; i < 6; i++) flagsSeen[i] = new HashSet<VoiceFrameFlags>();
            var routes = new VoiceRoute[6];
            var reader = new VoiceBundleReader();

            double now = 0, dt = 1000.0 / 60.0, micAcc = 0;
            int micPos = 0;
            var mic = new float[4096];
            long upBytes = 0;
            int simulatedUpdates = (int)(5000 / dt);
            for (int u = 0; u < simulatedUpdates; u++)
            {
                now += dt;
                // --- speaker client: microphone samples for this Update
                micAcc += dt * 48.0;
                int n = (int)micAcc;
                micAcc -= n;
                Array.Copy(source, micPos, mic, 0, n);
                micPos += n;
                capture.Process(mic, 0, n);
                if (!sink.Writer.IsEmpty)
                {
                    up.Send(sink.Writer.Buffer, sink.Writer.Length, -1, now);
                    upBytes += sink.Writer.Length;
                    sink.Writer.Reset();
                }
                // --- host: route what arrived
                foreach (var p in up.Due(now))
                {
                    Assert.IsTrue(reader.Begin(p.Data, p.Data.Length));
                    while (reader.TryNext(out var f))
                    {
                        int count = router.Route(0, routes);
                        for (int r = 0; r < count; r++)
                            downWriters[routes[r].Recipient].TryAppend(0, f.Sequence, routes[r].Flags | (f.Flags & VoiceFrameFlags.EndOfSpeech), p.Data, f.PayloadOffset, f.PayloadLength);
                    }
                }
                for (int r = 0; r < 6; r++)
                    if (!downWriters[r].IsEmpty) { down.Send(downWriters[r].Buffer, downWriters[r].Length, r, now); downWriters[r].Reset(); }
                // --- clients: receive, decode, consume audio in real time
                foreach (var p in down.Due(now))
                {
                    reader.Begin(p.Data, p.Data.Length);
                    while (reader.TryNext(out var f))
                    {
                        channels[p.To].Receive(f.Sequence, f.Flags, p.Data, f.PayloadOffset, f.PayloadLength, now);
                        flagsSeen[p.To].Add(f.Flags & ~VoiceFrameFlags.EndOfSpeech);
                    }
                }
                for (int r = 1; r < 4; r++)
                {
                    channels[r].Pump(now, 960 * 2 + (int)(dt * 48));
                    channels[r].Ring.Discard((int)(dt * 48));
                }
            }

            double talkSeconds = capture.FramesEncoded * 0.02;
            double opusKbps = capture.BytesEncoded * 8 / talkSeconds / 1000;
            double streamKbps = (capture.BytesEncoded + capture.FramesEncoded * VoiceWire.FrameHeaderBytes) * 8 / talkSeconds / 1000;
            double upKbps = upBytes * 8 / talkSeconds / 1000;
            TestContext.WriteLine($"{backend}: sent {capture.FramesEncoded} frames; opus {opusKbps:0.0} kbps, stream (payload+5 B header) {streamKbps:0.0} kbps, up bundles {upKbps:0.0} kbps");
            for (int r = 1; r < 4; r++)
            {
                var j = channels[r].Jitter;
                TestContext.WriteLine($"  recipient {r}: received {j.Received} played {j.Played} concealed {j.Concealed} fec {j.FecRecovered} late {j.Late} dup {j.Duplicates} skipped {j.Skipped} jitter {j.JitterMs:0.0} ms target {j.TargetFrames * 20} ms decodeErr {channels[r].DecodeErrors}");
            }
            Assert.LessOrEqual(streamKbps, 24.0, "budget: ≤ 24 kbps per active speaker");
            foreach (int r in new[] { 1, 2 })
            {
                var j = channels[r].Jitter;
                Assert.AreEqual(0, channels[r].DecodeErrors);
                Assert.Greater(j.Played + j.Concealed, capture.FramesEncoded * 0.9, "recipient " + r + " heard nearly everything");
                Assert.Less(j.Concealed, capture.FramesEncoded * 0.15, "loss handled by FEC/PLC within reason");
                Assert.Less(j.Late, capture.FramesEncoded * 0.05, "adaptive delay absorbs the jitter");
            }
            CollectionAssert.AreEquivalent(new[] { VoiceFrameFlags.None }, flagsSeen[1], "proximity listener");
            CollectionAssert.AreEquivalent(new[] { VoiceFrameFlags.Call }, flagsSeen[2], "call partner");
            Assert.AreEqual(0, channels[3].Jitter.Received, "far player outside the call hears nothing");
        }

        [Test]
        public void Bitrate_SpeechLikeSignal_StaysWithinBudget()
        {
            foreach (var backend in Backends.All)
            {
                if (backend == VoiceCodecBackend.Native && !NativeOpus.IsAvailable) continue;
                foreach (var kind in new[] { "speech", "sine" })
                {
                    var src = SyntheticVoiceSource.Create(kind, 0);
                    src.TalkSeconds = 100;
                    var sink = new BundleSink();
                    var p = new VoiceCapturePipeline(VoiceCodec.CreateEncoder(VoiceEncoderSettings.Default, backend), 16000) { Sink = sink, PttHeld = true };
                    var buf = new float[16000 * 10];
                    src.Read(buf, 0, buf.Length);
                    p.Process(buf, 0, buf.Length);
                    double sec = p.FramesEncoded * 0.02;
                    double opus = p.BytesEncoded * 8 / sec / 1000, stream = (p.BytesEncoded + p.FramesEncoded * 5) * 8 / sec / 1000;
                    TestContext.WriteLine($"{backend} {kind}: opus {opus:0.0} kbps, stream {stream:0.0} kbps, {p.BytesEncoded / (double)p.FramesEncoded:0.0} B/frame");
                    Assert.LessOrEqual(stream, 24.0, backend + " " + kind);
                    Assert.Greater(opus, 8.0, "sanity: not starving the codec");
                }
            }
        }

        [Test]
        public void HotPath_NativeBackend_AllocatesNothingPerFrame()
        {
            Backends.Require(VoiceCodecBackend.Native);
            var world = new FakeWorld().Place(0, 0, 0).Place(1, 2, 0).Place(2, 8, 0).Place(3, 30, 0);
            world.WallX = 5f;
            world.Call(0, 3);
            var router = new ProximityVoiceRouter(world, world, world, world);
            var sink = new BundleSink { Slot = 0 };
            var capture = new VoiceCapturePipeline(VoiceCodec.CreateEncoder(VoiceEncoderSettings.Default, VoiceCodecBackend.Native), 48000) { Sink = sink, PttHeld = true };
            var down = new VoiceBundleWriter[6];
            for (int i = 0; i < 6; i++) down[i] = new VoiceBundleWriter();
            var channel = new VoicePlaybackChannel(0, VoiceCodec.CreateDecoder(48000, VoiceCodecBackend.Native));
            var muffled = new VoicePlaybackChannel(0, VoiceCodec.CreateDecoder(48000, VoiceCodecBackend.Native));
            var out48 = new PcmPlayoutReader();
            var out44 = new PcmPlayoutReader();
            out44.SetRates(48000, 44100);
            var routes = new VoiceRoute[6];
            var unity48 = new float[960 * 2];   // 20 ms of stereo at 48 kHz
            var unity44 = new float[882 * 2];   // 20 ms of stereo at 44.1 kHz
            var mic = Signals.Harmonic(48000, 1.0, 200, 0.3f);
            var chunk = new float[960];
            var reader = new VoiceBundleReader();
            double now = 0;
            int micPos = 0;
            int frames = 0;

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
                    for (int r = 0; r < n; r++)
                        down[routes[r].Recipient].TryAppend(0, f.Sequence, routes[r].Flags | (f.Flags & VoiceFrameFlags.EndOfSpeech), sink.Writer.Buffer, f.PayloadOffset, f.PayloadLength);
                }
                sink.Writer.Reset();
                ReceiveAll(down[1], channel, ref reader, now);
                ReceiveAll(down[2], muffled, ref reader, now);
                down[3].Reset();
                channel.Pump(now, 960 * 3);
                muffled.Pump(now, 960 * 3);
                for (int i = 0; i < unity48.Length; i++) unity48[i] = 1f;
                out48.MultiplyInto(channel.Ring, unity48, 2);
                for (int i = 0; i < unity44.Length; i++) unity44[i] = 1f;
                out44.MultiplyInto(muffled.Ring, unity44, 2);
                frames++;
            }

            for (int i = 0; i < 300; i++) Iteration(); // warm-up: JIT, VAD open, jitter buffers playing
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2000; i++) Iteration();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            TestContext.WriteLine($"allocated {allocated} bytes over 2000 frames; played {channel.Jitter.Played}, muffled played {muffled.Jitter.Played}, underruns {out48.Underruns}/{out44.Underruns}");
            foreach (var c in new[] { channel, muffled })
                TestContext.WriteLine($"  rx {c.Jitter.Received} played {c.Jitter.Played} concealed {c.Jitter.Concealed} late {c.Jitter.Late} skipped {c.Jitter.Skipped} overflow {c.Jitter.Overflow} ring {c.Ring.Count}");
            Assert.Greater(channel.Jitter.Played, 1500, "the loop really ran the whole pipeline");
            Assert.Greater(muffled.Jitter.Played, 1500);
            Assert.AreEqual(0, allocated, "capture/encode/route/decode/playout must not allocate");
        }

        static void ReceiveAll(VoiceBundleWriter w, VoicePlaybackChannel ch, ref VoiceBundleReader reader, double now)
        {
            reader.Begin(w.Buffer, w.Length);
            while (reader.TryNext(out var f)) ch.Receive(f.Sequence, f.Flags, w.Buffer, f.PayloadOffset, f.PayloadLength, now);
            w.Reset();
        }

        [Test]
        public void ManagedBackend_AllocationCost_IsMeasured()
        {
            var enc = VoiceCodec.CreateEncoder(VoiceEncoderSettings.Default, VoiceCodecBackend.Managed);
            var dec = VoiceCodec.CreateDecoder(48000, VoiceCodecBackend.Managed);
            var pcm = Signals.Harmonic(16000, 0.02);
            var pkt = new byte[200];
            var outPcm = new float[960];
            int len = 0;
            for (int i = 0; i < 100; i++) { len = enc.Encode(pcm, pkt, 0, 200); dec.Decode(pkt, 0, len, outPcm, false); }
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 500; i++) len = enc.Encode(pcm, pkt, 0, 200);
            long a1 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 500; i++) dec.Decode(pkt, 0, len, outPcm, false);
            long a2 = GC.GetAllocatedBytesForCurrentThread();
            double encPer = (a1 - a0) / 500.0, decPer = (a2 - a1) / 500.0;
            TestContext.WriteLine($"Concentus fallback: {encPer:0} B per encoded frame, {decPer:0} B per decoded frame " +
                                  $"(≈ {(encPer * 50 / 1024):0} KB/s talking, {(decPer * 50 / 1024):0} KB/s per remote speaker)");
            Assert.Pass("managed fallback allocates; native backend is the default (see VOICE_NOTES.md)");
        }

        [Test]
        public void Playback_FlagsShapeGainAndMuffle()
        {
            var enc = VoiceCodec.CreateEncoder(VoiceEncoderSettings.Default);
            var pcm = Signals.Harmonic(16000, 0.02, 220, 0.3f);
            var tone = Signals.Harmonic(16000, 2.0, 220, 0.3f);
            var hiss = Signals.Sine(16000, 2.0, 3500, 0.15f);
            for (int i = 0; i < tone.Length; i++) tone[i] += hiss[i];
            var pkts = new List<byte[]>();
            var buf = new byte[200];
            var frame = new float[320];
            for (int f = 0; f < 100; f++)
            {
                Array.Copy(tone, f * 320, frame, 0, 320);
                int n = enc.Encode(frame, buf, 0, 200);
                var p = new byte[n];
                Buffer.BlockCopy(buf, 0, p, 0, n);
                pkts.Add(p);
            }

            // Gain/muffle rules only: the v3 phone EQ on calls has its own test (TapeTests.Playback_PhoneEqAndTapGain).
            float[] Play(VoiceFrameFlags flags, float proximityGain, float volume, out double hfRatio)
            {
                var ch = new VoicePlaybackChannel(0, VoiceCodec.CreateDecoder(48000)) { ProximityGain = proximityGain, VolumeScale = volume, PhoneEqEnabled = false };
                var all = new float[100 * 960];
                int got = 0;
                for (int f = 0; f < 100; f++)
                {
                    ch.Receive((ushort)f, flags, pkts[f], 0, pkts[f].Length, f * 20);
                    ch.Pump(f * 20, 960 * 2);
                    got += ch.Ring.Read(all, got, Math.Min(960, all.Length - got)); // real-time consumption
                }
                int skip = 20 * 960;
                hfRatio = Signals.TonePower(all, skip, got - skip, 48000, 3500) / Math.Max(1e-12, Signals.TonePower(all, skip, got - skip, 48000, 220));
                return all;
            }

            var clear = Play(VoiceFrameFlags.None, 1f, 1f, out double hfClear);
            var muff = Play(VoiceFrameFlags.Muffled, 1f, 1f, out double hfMuff);
            var far = Play(VoiceFrameFlags.None, 0.25f, 1f, out _);
            var call = Play(VoiceFrameFlags.Call, 0.0f, 1f, out _);
            var deaf = Play(VoiceFrameFlags.AllHear, 1f, 0f, out _);
            int s0 = 30 * 960, len = 60 * 960;
            float rClear = Signals.Rms(clear, s0, len), rMuff = Signals.Rms(muff, s0, len), rFar = Signals.Rms(far, s0, len);
            float rCall = Signals.Rms(call, s0, len), rDeaf = Signals.Rms(deaf, s0, len);
            TestContext.WriteLine($"rms clear {rClear:0.000} muffled {rMuff:0.000} far {rFar:0.000} call {rCall:0.000} deaf {rDeaf:0.000}; HF/LF clear {hfClear:E2} muffled {hfMuff:E2}");
            Assert.Less(rMuff, rClear * 0.6f, "muffled is quieter");
            Assert.Less(hfMuff, hfClear * 0.05, "muffled loses the highs (low-pass)");
            Assert.AreEqual(rClear * 0.25f, rFar, rClear * 0.05f, "proximity gain applied");
            Assert.AreEqual(rClear, rCall, rClear * 0.1f, "call ignores distance attenuation");
            Assert.Less(rDeaf, 1e-4f, "volume 0 (deafen) is silent");
        }
    }
}
