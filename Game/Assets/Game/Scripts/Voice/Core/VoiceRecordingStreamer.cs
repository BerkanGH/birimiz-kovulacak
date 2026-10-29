using System;

namespace BirimizKovulacak.Voice
{
    /// <summary>Receives the frames of a recording being played (host side).</summary>
    public interface IVoiceRecordingSink
    {
        /// <summary><paramref name="slot"/> is the ORIGINAL speaker; flags carry Recording (+ EndOfSpeech).
        /// payload[offset..offset+length) is only valid during the call.</summary>
        void OnRecordingFrame(int slot, ushort sequence, VoiceFrameFlags flags, byte[] payload, int offset, int length);
    }

    /// <summary>
    /// Host-side cursor that replays a <see cref="RecordingSnippet"/> from a <see cref="VoiceCallRecorder"/> at real-time
    /// pace: each frame is emitted once its media time (relative to the window start) has elapsed since
    /// <see cref="StartMs"/>. One cursor per speaker: frames are stored in host arrival order, which can differ
    /// between the two speakers by a network burst, so each side is paced on its own timeline. The last frame of
    /// every speaker inside the window is marked EndOfSpeech so the clients' jitter buffers close the spurt
    /// cleanly. Allocation-free.
    /// </summary>
    public sealed class VoiceRecordingStreamer
    {
        /// <summary>Within one speaker frames are media-ordered except for network reordering (≤ 32 frames);
        /// scan this far past the window end so no frame at its edge is missed.</summary>
        public const int ToleranceMs = 700;
        const int Slots = VoiceFormat.MaxPlayers;

        readonly int[] _lastOfSlot = new int[Slots];
        readonly int[] _cursor = new int[Slots];
        VoiceCallRecorder _rec;
        RecordingSnippet _snippet = RecordingSnippet.None;
        double _startMs;

        /// <summary>Time after the window end before <see cref="IsDone"/> (lets the last frames play out).</summary>
        public int TailMs = 300;
        public bool Active { get; private set; }
        /// <summary>All frames sent and the tail elapsed (stays true until <see cref="Stop"/>/<see cref="Begin"/>).</summary>
        public bool IsDone { get; private set; }
        public RecordingSnippet Snippet => _snippet;
        /// <summary>Clock time (caller's clock, ms) at which the window starts playing.</summary>
        public double StartMs => _startMs;
        public int FramesSent { get; private set; }

        public bool Begin(VoiceCallRecorder recorder, in RecordingSnippet snippet, double startMs)
        {
            Stop();
            if (recorder == null || !snippet.IsValid) return false;
            int first = recorder.FirstFrameOf(snippet.CallId);
            if (first < 0) return false;
            int end = snippet.StartMs + snippet.LengthMs;
            for (int i = 0; i < Slots; i++) { _lastOfSlot[i] = -1; _cursor[i] = -1; }
            // Per speaker: the first frame inside the window, and the last one (highest sequence: frames may be
            // stored slightly reordered). One pass over the call.
            for (int g = first; g >= 0; g = recorder.FrameNext(g))
            {
                int m = recorder.FrameMediaMs(g);
                if (m < snippet.StartMs || m >= end) continue;
                int slot = recorder.FrameSlot(g), last = _lastOfSlot[slot];
                if (_cursor[slot] < 0) _cursor[slot] = g;
                if (last < 0 || VoiceSeq.Diff(recorder.FrameSequence(g), recorder.FrameSequence(last)) > 0) _lastOfSlot[slot] = g;
            }
            _rec = recorder;
            _snippet = snippet;
            _startMs = startMs;
            Active = true;
            return true;
        }

        /// <summary>Emits every frame that is due at <paramref name="nowMs"/>; returns how many.</summary>
        public int Tick(double nowMs, IVoiceRecordingSink sink)
        {
            if (!Active || IsDone) return 0;
            double elapsed = nowMs - _startMs;
            int start = _snippet.StartMs, end = start + _snippet.LengthMs;
            int n = 0;
            bool running = false;
            for (int slot = 0; slot < Slots; slot++)
            {
                int c = _cursor[slot];
                while (c >= 0)
                {
                    if (_rec.FrameSlot(c) != slot) { c = _rec.FrameNext(c); continue; }
                    int m = _rec.FrameMediaMs(c);
                    if (m > end + ToleranceMs) { c = -1; break; }
                    if (m >= start && m < end)
                    {
                        if (m - start > elapsed) break;
                        VoiceFrameFlags flags = _rec.FrameFlags(c) | VoiceFrameFlags.Recording;
                        if (c == _lastOfSlot[slot]) flags |= VoiceFrameFlags.EndOfSpeech;
                        byte[] buffer;
                        int offset;
                        int length = _rec.FramePayload(c, out buffer, out offset);
                        sink?.OnRecordingFrame(slot, _rec.FrameSequence(c), flags, buffer, offset, length);
                        FramesSent++;
                        n++;
                    }
                    c = _rec.FrameNext(c);
                }
                _cursor[slot] = c;
                if (c >= 0) running = true;
            }
            if (!running && elapsed >= _snippet.LengthMs + TailMs) IsDone = true;
            return n;
        }

        public void Stop()
        {
            Active = false;
            IsDone = false;
            for (int i = 0; i < Slots; i++) _cursor[i] = -1;
            _rec = null;
            FramesSent = 0;
        }
    }

    /// <summary>Description of a tape being played (host → clients control message, UI events).</summary>
    public struct VoiceTapeInfo
    {
        public ushort PlaybackId;
        public int CallId;
        /// <summary>Server time (s) at which the window starts playing.</summary>
        public double StartServerTime;
        /// <summary>Window start inside the call (ms).</summary>
        public int CallOffsetMs;
        public int DurationMs;
        public byte SpeakersMask;
        public byte ParticipantsMask;

        public bool HasSpeaker(int slot) { return slot >= 0 && slot < 8 && (SpeakersMask & (1 << slot)) != 0; }
    }

    /// <summary>
    /// "bk.voice.tape" (Reliable, host → client), fixed 26 bytes:
    /// <code>version:u8=0xB6 kind:u8 playbackId:u16 callId:i32 startServerTime:f64 callOffsetMs:i32 durationMs:i32 speakers:u8 participants:u8</code>
    /// kind 1 = start (also re-sent to clients that join mid-playback), 2 = end.
    /// </summary>
    public static class VoiceTapeMessage
    {
        public const byte Version = 0xB6;
        public const byte KindStart = 1;
        public const byte KindEnd = 2;
        public const int Size = 26;

        public static int Write(byte[] buffer, byte kind, in VoiceTapeInfo info)
        {
            if (buffer == null || buffer.Length < Size) return 0;
            int p = 0;
            buffer[p++] = Version;
            buffer[p++] = kind;
            p = PutU16(buffer, p, info.PlaybackId);
            p = PutI32(buffer, p, info.CallId);
            long bits = BitConverter.DoubleToInt64Bits(info.StartServerTime);
            p = PutI32(buffer, p, (int)bits);
            p = PutI32(buffer, p, (int)(bits >> 32));
            p = PutI32(buffer, p, info.CallOffsetMs);
            p = PutI32(buffer, p, info.DurationMs);
            buffer[p++] = info.SpeakersMask;
            buffer[p++] = info.ParticipantsMask;
            return p;
        }

        public static bool TryRead(byte[] buffer, int length, out byte kind, out VoiceTapeInfo info)
        {
            kind = 0;
            info = default;
            if (buffer == null || length < Size || buffer.Length < Size || buffer[0] != Version) return false;
            kind = buffer[1];
            if (kind != KindStart && kind != KindEnd) return false;
            int p = 2;
            info.PlaybackId = (ushort)(buffer[p] | (buffer[p + 1] << 8)); p += 2;
            info.CallId = GetI32(buffer, p); p += 4;
            long lo = (uint)GetI32(buffer, p), hi = GetI32(buffer, p + 4); p += 8;
            info.StartServerTime = BitConverter.Int64BitsToDouble(lo | (hi << 32));
            info.CallOffsetMs = GetI32(buffer, p); p += 4;
            info.DurationMs = GetI32(buffer, p); p += 4;
            info.SpeakersMask = buffer[p++];
            info.ParticipantsMask = buffer[p];
            return !double.IsNaN(info.StartServerTime) && info.DurationMs >= 0;
        }

        static int PutU16(byte[] b, int p, ushort v) { b[p] = (byte)v; b[p + 1] = (byte)(v >> 8); return p + 2; }

        static int PutI32(byte[] b, int p, int v)
        {
            b[p] = (byte)v; b[p + 1] = (byte)(v >> 8); b[p + 2] = (byte)(v >> 16); b[p + 3] = (byte)(v >> 24);
            return p + 4;
        }

        static int GetI32(byte[] b, int p) { return b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24); }
    }
}
