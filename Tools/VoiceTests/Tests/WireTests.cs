using System;
using BirimizKovulacak.Voice;
using NUnit.Framework;

namespace BirimizKovulacak.VoiceTests
{
    [TestFixture]
    public class WireTests
    {
        [Test]
        public void Bundle_RoundTripsFrames()
        {
            var w = new VoiceBundleWriter();
            var p1 = new byte[] { 1, 2, 3 };
            var p2 = new byte[60];
            for (int i = 0; i < p2.Length; i++) p2[i] = (byte)(200 - i);
            Assert.IsTrue(w.TryAppend(3, 65535, VoiceFrameFlags.Call | VoiceFrameFlags.EndOfSpeech, p1, 0, p1.Length));
            Assert.IsTrue(w.TryAppend(5, 7, VoiceFrameFlags.Muffled, p2, 10, 50));
            Assert.IsTrue(w.TryAppend(0, 0, VoiceFrameFlags.None, p1, 0, 0));
            Assert.AreEqual(3, w.FrameCount);
            Assert.AreEqual(1 + 3 * VoiceWire.FrameHeaderBytes + 3 + 50, w.Length);

            var r = new VoiceBundleReader();
            Assert.IsTrue(r.Begin(w.Buffer, w.Length));
            Assert.IsTrue(r.TryNext(out var f));
            Assert.AreEqual(3, f.Slot);
            Assert.AreEqual(65535, f.Sequence);
            Assert.AreEqual(VoiceFrameFlags.Call | VoiceFrameFlags.EndOfSpeech, f.Flags);
            Assert.AreEqual(3, f.PayloadLength);
            Assert.AreEqual(2, w.Buffer[f.PayloadOffset + 1]);
            Assert.IsTrue(r.TryNext(out f));
            Assert.AreEqual(5, f.Slot);
            Assert.AreEqual(7, f.Sequence);
            Assert.AreEqual(VoiceFrameFlags.Muffled, f.Flags);
            Assert.AreEqual(50, f.PayloadLength);
            Assert.AreEqual(p2[10], w.Buffer[f.PayloadOffset]);
            Assert.AreEqual(p2[59], w.Buffer[f.PayloadOffset + 49]);
            Assert.IsTrue(r.TryNext(out f));
            Assert.AreEqual(0, f.PayloadLength);
            Assert.IsFalse(r.TryNext(out f));
        }

        [Test]
        public void Bundle_RespectsCapacityAndLimits()
        {
            var w = new VoiceBundleWriter();
            var p = new byte[VoiceFormat.MaxPayloadBytes + 1];
            Assert.IsFalse(w.TryAppend(0, 1, VoiceFrameFlags.None, p, 0, p.Length), "oversized payload");
            Assert.IsFalse(w.TryAppend(-1, 1, VoiceFrameFlags.None, p, 0, 10), "bad slot");
            int appended = 0;
            while (w.TryAppend(1, (ushort)appended, VoiceFrameFlags.None, p, 0, 80)) appended++;
            Assert.LessOrEqual(w.Length, VoiceWire.MaxBundleBytes);
            Assert.AreEqual((VoiceWire.MaxBundleBytes - 1) / (VoiceWire.FrameHeaderBytes + 80), appended);
            Assert.Less(VoiceWire.MaxBundleBytes + 16, 1296, "bundle + NGO header stays below NGO's unfragmented limit");
            w.Reset();
            Assert.IsTrue(w.IsEmpty);
            Assert.AreEqual(1, w.Length);
        }

        [Test]
        public void Reader_RejectsMalformedInput()
        {
            var r = new VoiceBundleReader();
            Assert.IsFalse(r.Begin(null, 0));
            Assert.IsFalse(r.TryNext(out _));
            Assert.IsFalse(r.Begin(new byte[] { 0x00, 1, 2, 3 }, 4), "wrong version");
            Assert.IsFalse(r.TryNext(out _));

            // Truncated payload.
            var w = new VoiceBundleWriter();
            w.TryAppend(1, 9, VoiceFrameFlags.None, new byte[40], 0, 40);
            Assert.IsTrue(r.Begin(w.Buffer, w.Length - 1));
            Assert.IsFalse(r.TryNext(out _));

            // Slot out of range.
            var bad = new byte[] { VoiceWire.Version, 9, 1, 0, 0, 0 };
            Assert.IsTrue(r.Begin(bad, bad.Length));
            Assert.IsFalse(r.TryNext(out _));

            // Length byte beyond the payload cap.
            var big = new byte[300];
            big[0] = VoiceWire.Version; big[1] = 1; big[5] = 250;
            Assert.IsTrue(r.Begin(big, big.Length));
            Assert.IsFalse(r.TryNext(out _));

            // Header shorter than 5 bytes after a valid frame: stop cleanly.
            w.Reset();
            w.TryAppend(2, 1, VoiceFrameFlags.None, new byte[3], 0, 3);
            var tail = new byte[w.Length + 2];
            Buffer.BlockCopy(w.Buffer, 0, tail, 0, w.Length);
            Assert.IsTrue(r.Begin(tail, tail.Length));
            Assert.IsTrue(r.TryNext(out _));
            Assert.IsFalse(r.TryNext(out _));
        }

        [Test]
        public void SequenceDiff_IsWrapAware()
        {
            Assert.AreEqual(1, VoiceSeq.Diff(0, 65535));
            Assert.AreEqual(-1, VoiceSeq.Diff(65535, 0));
            Assert.AreEqual(10, VoiceSeq.Diff(5, 65531));
            Assert.AreEqual(0, VoiceSeq.Diff(1234, 1234));
        }
    }
}
