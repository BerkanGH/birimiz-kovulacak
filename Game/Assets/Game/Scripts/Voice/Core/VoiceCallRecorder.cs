using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Voice
{
    /// <summary>What <see cref="VoiceCallRecorder.TrySelectSnippet(IList{int}, out RecordingSnippet, int, int)"/> chose.</summary>
    public struct RecordingSnippet
    {
        public int CallId;
        /// <summary>Window start in ms from the call's first recorded frame.</summary>
        public int StartMs;
        public int LengthMs;
        /// <summary>Time inside the window during which at least one participant talks (20 ms resolution).</summary>
        public int SpeechMs;
        /// <summary>Bit per slot: who is heard inside the window, i.e. who the UI may name.</summary>
        public byte SpeakersMask;
        /// <summary>Bit per slot: both participants of the call (also a silent one).</summary>
        public byte ParticipantsMask;
        /// <summary>Recorded length of the whole call (ms).</summary>
        public int CallLengthMs;

        public bool IsValid => CallId >= 0 && LengthMs > 0;
        public bool HasSpeaker(int slot) { return slot >= 0 && slot < 8 && (SpeakersMask & (1 << slot)) != 0; }

        public static RecordingSnippet None => new RecordingSnippet { CallId = -1 };

        public override string ToString()
        {
            return "call " + CallId + " @" + StartMs + "ms +" + LengthMs + "ms speech=" + SpeechMs + "ms speakers=0x" + SpeakersMask.ToString("X2");
        }
    }

    /// <summary>Summary of one recorded call.</summary>
    public struct RecordedCallInfo
    {
        public int CallId;
        public int Frames;
        /// <summary>From the first recorded frame to the end of the last one (ms).</summary>
        public int LengthMs;
        public byte SpeakersMask;
        public byte ParticipantsMask;
        public bool Erased;
    }

    /// <summary>
    /// Host-only, in-memory recorder of connected phone calls (v3 "Bu görüşme kalite standartları gereği
    /// kaydedilmektedir"). Stores the routed Opus frames of both directions per call id with speaker slot, sequence,
    /// end-of-speech flag and a de-jittered media time — no decoding, no re-encoding, never written to disk.
    /// <para>Memory: frames go into pooled fixed-size chunks (metadata 16 B/frame in chunks of 4096 frames = 64 KB;
    /// Opus payload in 64 KB chunks), allocated on demand up to <see cref="MaxBytes"/> (default 8 MB) and kept for
    /// reuse after <see cref="Clear"/>, so a running game records with zero allocations per frame once warm
    /// (<see cref="Reserve"/> pre-warms). <see cref="Trim"/> gives the memory back. Over the cap new frames are
    /// dropped (counted in <see cref="DroppedFrames"/>); earlier audio is never overwritten.</para>
    /// <para>Measured (Tools/VoiceTests, native Opus at the 18 kbps target, continuous synthetic speech): ≈ 160 KB per
    /// talking minute per speaker (payload 113 KB + metadata 47 KB; Concentus 153 KB). Worst case for a 6-player day
    /// (everyone talking through every phone phase of the longest day, 6.6 min): 6.2 MB; typical ≈ 1.7 MB. Cleared
    /// after every meeting, so a whole 5-day week never holds more than one day. See docs/VOICE.md.</para>
    /// Main thread only.
    /// </summary>
    public sealed class VoiceCallRecorder
    {
        public const int FramesPerChunk = 4096;
        const int ChunkShift = 12;
        public const int FrameMetaBytes = 16;
        public const int FrameChunkBytes = FramesPerChunk * FrameMetaBytes;
        public const int PayloadChunkBytes = 65536;
        public const int MaxCalls = 1024;
        public const long DefaultMaxBytes = 8L * 1024 * 1024;
        /// <summary>Hard ceiling for <see cref="MaxBytes"/> (size of the chunk tables).</summary>
        public const long MaxBytesCeiling = 64L * 1024 * 1024;
        /// <summary>The window start may move back this far to begin at the start of a word.</summary>
        public const int SnapBackMs = 500;
        const int Slots = VoiceFormat.MaxPlayers;
        const int FrameMs = VoiceFormat.FrameMs;

        sealed class FrameChunk
        {
            public readonly int[] Media = new int[FramesPerChunk];
            public readonly int[] Pos = new int[FramesPerChunk];
            public readonly int[] Next = new int[FramesPerChunk];
            public readonly ushort[] Seq = new ushort[FramesPerChunk];
            public readonly byte[] SlotFlags = new byte[FramesPerChunk]; // slot | 0x80 = end of speech
            public readonly byte[] Len = new byte[FramesPerChunk];
        }

        struct CallEntry
        {
            public int Id;
            public double StartMs;
            public int First, Last, Frames, EndMs;
            public byte Speakers, Participants;
            public bool Erased;
        }

        readonly FrameChunk[] _frameChunks = new FrameChunk[(int)(MaxBytesCeiling / FrameChunkBytes)];
        readonly byte[][] _payloadChunks = new byte[(int)(MaxBytesCeiling / PayloadChunkBytes)][];
        int _frameChunksAllocated, _payloadChunksAllocated;
        int _payloadChunk, _payloadFill;
        int _frameCount;
        long _payloadBytes;
        readonly CallEntry[] _calls = new CallEntry[MaxCalls];
        int _callCount;

        // Per-slot timeline of the slot's current call (a slot is in at most one call at a time).
        readonly int[] _slotCall = new int[Slots];
        readonly ushort[] _slotSeq = new ushort[Slots];
        readonly int[] _slotMedia = new int[Slots];
        readonly bool[] _slotEnd = new bool[Slots];
        readonly bool[] _slotHas = new bool[Slots];
        readonly uint[] _slotWindow = new uint[Slots]; // bit i: sequence (last - i) stored
        byte _erasedSlots;
        byte[] _cover = new byte[0];
        long _maxBytes = DefaultMaxBytes;

        /// <summary>Host toggle (lobby, default on). Disabled = <see cref="Record"/> ignores everything.</summary>
        public bool Enabled = true;

        // Counters (cumulative, survive Clear).
        public long RecordedFrames, DroppedFrames, DuplicateFrames, ErasedFrames, RejectedFrames;

        public VoiceCallRecorder()
        {
            ResetSlots();
        }

        /// <summary>Memory cap for chunks (metadata + payload), 64 KB .. 64 MB. Lowering it never frees chunks
        /// already held (call <see cref="Trim"/>).</summary>
        public long MaxBytes
        {
            get => _maxBytes;
            set => _maxBytes = Math.Max(FrameChunkBytes + PayloadChunkBytes, Math.Min(MaxBytesCeiling, value));
        }

        public int FrameCount => _frameCount;
        public int CallCount => _callCount;
        public long PayloadBytes => _payloadBytes;
        /// <summary>Bytes actually holding recorded data (metadata + Opus payload).</summary>
        public long UsedBytes => (long)_frameCount * FrameMetaBytes + _payloadBytes;
        /// <summary>Bytes held by allocated chunks (what the process really pays; pooled across Clear).</summary>
        public long ReservedBytes => (long)_frameChunksAllocated * FrameChunkBytes + (long)_payloadChunksAllocated * PayloadChunkBytes;

        // ------------------------------------------------------------------ recording

        /// <summary>
        /// Records one routed call frame. <paramref name="callId"/> is the connected call (≥ 0, same for both sides),
        /// <paramref name="partner"/> the other participant (for erase-tape; -1 if unknown). Duplicates and frames
        /// older than the speaker's last recorded one are dropped. Returns true when stored. Allocation-free unless
        /// a new chunk is needed (never again after the first day thanks to pooling).
        /// </summary>
        public bool Record(int callId, int speaker, int partner, ushort seq, VoiceFrameFlags flags, byte[] payload, int offset, int length, double nowMs)
        {
            if (!Enabled) return false;
            if (callId < 0 || speaker < 0 || speaker >= Slots || length < 0 || length > VoiceFormat.MaxPayloadBytes ||
                (length > 0 && (payload == null || offset < 0 || offset + length > payload.Length)))
            {
                RejectedFrames++;
                return false;
            }

            int ci = FindCall(speaker, callId);
            if (ci < 0)
            {
                if (_callCount >= MaxCalls) { DroppedFrames++; return false; }
                ci = _callCount++;
                _calls[ci] = new CallEntry { Id = callId, StartMs = nowMs, First = -1, Last = -1 };
            }
            byte bits = (byte)(1 << speaker);
            if (partner >= 0 && partner < Slots) bits |= (byte)(1 << partner);
            _calls[ci].Participants |= bits;
            if (_calls[ci].Erased || (_calls[ci].Participants & _erasedSlots) != 0)
            {
                _calls[ci].Erased = true;
                ErasedFrames++;
                return false;
            }

            // De-jittered media time: inside a talk spurt frames are exactly 20 ms apart (sequence numbers), a new
            // spurt starts at its arrival time. Keeps network jitter out of the replay. A 32-frame replay window
            // accepts slightly reordered frames and drops duplicates.
            if (_slotCall[speaker] != ci) { _slotCall[speaker] = ci; _slotHas[speaker] = false; }
            int raw = (int)(nowMs - _calls[ci].StartMs);
            if (raw < 0) raw = 0;
            int media = raw;
            bool newest = true;
            if (_slotHas[speaker])
            {
                int gap = VoiceSeq.Diff(seq, _slotSeq[speaker]);
                if (gap <= 0)
                {
                    // Late (reordered) frame: placed on the spurt's timeline unless it is a duplicate or too old.
                    if (-gap >= 32 || (_slotWindow[speaker] & (1u << -gap)) != 0) { DuplicateFrames++; return false; }
                    media = Math.Max(0, _slotMedia[speaker] + gap * FrameMs);
                    newest = false;
                }
                else
                {
                    int predicted = _slotMedia[speaker] + gap * FrameMs;
                    if (!_slotEnd[speaker] && gap <= 25 && Math.Abs(raw - predicted) <= 200) media = predicted;
                    else media = Math.Max(raw, predicted);
                }
            }

            if (!EnsureFrameSpace() || !EnsurePayloadSpace(length)) { DroppedFrames++; return false; }

            int fi = _frameCount++;
            var fc = _frameChunks[fi >> ChunkShift];
            int k = fi & (FramesPerChunk - 1);
            bool end = (flags & VoiceFrameFlags.EndOfSpeech) != 0;
            fc.Media[k] = media;
            fc.Pos[k] = (_payloadChunk << 16) | _payloadFill;
            fc.Next[k] = -1;
            fc.Seq[k] = seq;
            fc.SlotFlags[k] = (byte)(speaker | (end ? 0x80 : 0));
            fc.Len[k] = (byte)length;
            if (length > 0) Buffer.BlockCopy(payload, offset, _payloadChunks[_payloadChunk], _payloadFill, length);
            _payloadFill += length;
            _payloadBytes += length;

            if (_calls[ci].Last >= 0) _frameChunks[_calls[ci].Last >> ChunkShift].Next[_calls[ci].Last & (FramesPerChunk - 1)] = fi;
            else _calls[ci].First = fi;
            _calls[ci].Last = fi;
            _calls[ci].Frames++;
            if (media + FrameMs > _calls[ci].EndMs) _calls[ci].EndMs = media + FrameMs;
            _calls[ci].Speakers |= (byte)(1 << speaker);

            if (newest)
            {
                int shift = _slotHas[speaker] ? VoiceSeq.Diff(seq, _slotSeq[speaker]) : 32;
                _slotWindow[speaker] = (shift >= 32 ? 0u : _slotWindow[speaker] << shift) | 1u;
                _slotHas[speaker] = true;
                _slotSeq[speaker] = seq;
                _slotMedia[speaker] = media;
                _slotEnd[speaker] = end;
            }
            else _slotWindow[speaker] |= 1u << -VoiceSeq.Diff(seq, _slotSeq[speaker]);
            RecordedFrames++;
            return true;
        }

        /// <summary>
        /// Erase Tape ("Kaydı Sil"): every call of <paramref name="slot"/> recorded so far today and every later one
        /// until <see cref="Clear"/> is excluded from selection, and its further frames are not even stored.
        /// </summary>
        public void EraseSlot(int slot)
        {
            if (slot < 0 || slot >= Slots) return;
            byte bit = (byte)(1 << slot);
            _erasedSlots |= bit;
            for (int i = 0; i < _callCount; i++)
                if ((_calls[i].Participants & bit) != 0) _calls[i].Erased = true;
        }

        public bool IsErased(int slot) { return slot >= 0 && slot < Slots && (_erasedSlots & (1 << slot)) != 0; }

        /// <summary>Forgets every recording and erase mark (after each meeting). Chunks stay pooled for reuse.</summary>
        public void Clear()
        {
            _frameCount = 0;
            _callCount = 0;
            _payloadChunk = 0;
            _payloadFill = 0;
            _payloadBytes = 0;
            _erasedSlots = 0;
            ResetSlots();
        }

        /// <summary>Clear and release every chunk to the GC (end of session).</summary>
        public void Trim()
        {
            Clear();
            for (int i = 0; i < _frameChunksAllocated; i++) _frameChunks[i] = null;
            for (int i = 0; i < _payloadChunksAllocated; i++) _payloadChunks[i] = null;
            _frameChunksAllocated = 0;
            _payloadChunksAllocated = 0;
            _cover = new byte[0];
        }

        /// <summary>Pre-allocates chunks for about <paramref name="bytes"/> of recording (≈ 70 % payload, 30 %
        /// metadata — the measured ratio) so recording never allocates while playing. Capped by MaxBytes.</summary>
        public void Reserve(long bytes)
        {
            bytes = Math.Min(bytes, _maxBytes);
            long payload = bytes * 7 / 10, meta = bytes - payload;
            while ((long)_payloadChunksAllocated * PayloadChunkBytes < payload && CanGrow(PayloadChunkBytes) &&
                   _payloadChunksAllocated < _payloadChunks.Length)
                _payloadChunks[_payloadChunksAllocated++] = new byte[PayloadChunkBytes];
            while ((long)_frameChunksAllocated * FrameChunkBytes < meta && CanGrow(FrameChunkBytes) &&
                   _frameChunksAllocated < _frameChunks.Length)
                _frameChunks[_frameChunksAllocated++] = new FrameChunk();
        }

        void ResetSlots()
        {
            for (int i = 0; i < Slots; i++)
            {
                _slotCall[i] = -1;
                _slotHas[i] = false;
            }
        }

        bool CanGrow(int bytes) { return ReservedBytes + bytes <= _maxBytes; }

        bool EnsureFrameSpace()
        {
            int chunk = _frameCount >> ChunkShift;
            if (chunk < _frameChunksAllocated) return true;
            if (chunk >= _frameChunks.Length || !CanGrow(FrameChunkBytes)) return false;
            _frameChunks[_frameChunksAllocated++] = new FrameChunk();
            return true;
        }

        bool EnsurePayloadSpace(int length)
        {
            int chunk = _payloadChunk, fill = _payloadFill;
            // Strictly below the chunk size so the packed offset always fits 16 bits.
            if (chunk < _payloadChunksAllocated && fill + length < PayloadChunkBytes) return true;
            if (chunk < _payloadChunksAllocated) { chunk++; fill = 0; }
            if (chunk >= _payloadChunksAllocated)
            {
                if (_payloadChunksAllocated >= _payloadChunks.Length || !CanGrow(PayloadChunkBytes)) return false;
                _payloadChunks[_payloadChunksAllocated++] = new byte[PayloadChunkBytes];
            }
            _payloadChunk = chunk;
            _payloadFill = fill;
            return true;
        }

        int FindCall(int speaker, int callId)
        {
            int cached = _slotCall[speaker];
            if (cached >= 0 && cached < _callCount && _calls[cached].Id == callId) return cached;
            return IndexOfCall(callId);
        }

        int IndexOfCall(int callId)
        {
            for (int i = _callCount - 1; i >= 0; i--)
                if (_calls[i].Id == callId) return i;
            return -1;
        }

        // ------------------------------------------------------------------ queries

        public bool TryGetCall(int callId, out RecordedCallInfo info)
        {
            int ci = IndexOfCall(callId);
            if (ci < 0) { info = default; info.CallId = -1; return false; }
            ref CallEntry c = ref _calls[ci];
            info = new RecordedCallInfo
            {
                CallId = c.Id, Frames = c.Frames, LengthMs = c.EndMs, SpeakersMask = c.Speakers,
                ParticipantsMask = c.Participants, Erased = c.Erased || (c.Participants & _erasedSlots) != 0,
            };
            return true;
        }

        /// <summary>Call id of the i-th recorded call (0 .. <see cref="CallCount"/>-1), in order of first frame.</summary>
        public int CallIdAt(int index) { return index >= 0 && index < _callCount ? _calls[index].Id : -1; }

        /// <summary>First frame of a call (follow with <see cref="FrameNext"/>), or -1.</summary>
        public int FirstFrameOf(int callId)
        {
            int ci = IndexOfCall(callId);
            return ci < 0 ? -1 : _calls[ci].First;
        }

        public int FrameNext(int frame) { return _frameChunks[frame >> ChunkShift].Next[frame & (FramesPerChunk - 1)]; }
        public int FrameMediaMs(int frame) { return _frameChunks[frame >> ChunkShift].Media[frame & (FramesPerChunk - 1)]; }
        public int FrameSlot(int frame) { return _frameChunks[frame >> ChunkShift].SlotFlags[frame & (FramesPerChunk - 1)] & 0x0F; }
        public ushort FrameSequence(int frame) { return _frameChunks[frame >> ChunkShift].Seq[frame & (FramesPerChunk - 1)]; }

        public VoiceFrameFlags FrameFlags(int frame)
        {
            return (_frameChunks[frame >> ChunkShift].SlotFlags[frame & (FramesPerChunk - 1)] & 0x80) != 0 ? VoiceFrameFlags.EndOfSpeech : VoiceFrameFlags.None;
        }

        /// <summary>Opus payload of a frame: buffer[offset..offset+return).</summary>
        public int FramePayload(int frame, out byte[] buffer, out int offset)
        {
            var fc = _frameChunks[frame >> ChunkShift];
            int k = frame & (FramesPerChunk - 1);
            int pos = fc.Pos[k];
            buffer = _payloadChunks[pos >> 16];
            offset = pos & 0xFFFF;
            return fc.Len[k];
        }

        // ------------------------------------------------------------------ snippet selection

        /// <summary>
        /// Picks the first candidate (in the given order — the engine orders them, e.g. weighted by duration, and
        /// already left out erased tapes) that has at least <paramref name="minSpeechMs"/> of speech inside its best
        /// window, and inside it the <paramref name="lengthMs"/> window with the most speech (time covered by at
        /// least one talking participant). The window start moves back up to 0.5 s so it does not begin mid-word.
        /// Calls shorter than the window are used whole. Erased or unknown calls are skipped. Allocation-free once
        /// its scratch buffer fits the longest call.
        /// </summary>
        public bool TrySelectSnippet(IList<int> candidateCallIds, out RecordingSnippet snippet, int lengthMs = 7000, int minSpeechMs = 1500)
        {
            if (candidateCallIds != null)
                for (int i = 0; i < candidateCallIds.Count; i++)
                    if (TrySelectSnippet(candidateCallIds[i], out snippet, lengthMs, minSpeechMs)) return true;
            snippet = RecordingSnippet.None;
            return false;
        }

        public bool TrySelectSnippet(int callId, out RecordingSnippet snippet, int lengthMs = 7000, int minSpeechMs = 1500)
        {
            snippet = RecordingSnippet.None;
            int ci = IndexOfCall(callId);
            if (ci < 0) return false;
            ref CallEntry c = ref _calls[ci];
            if (c.Erased || c.Frames == 0 || (c.Participants & _erasedSlots) != 0) return false;

            int bins = (c.EndMs + FrameMs - 1) / FrameMs;
            if (_cover.Length < bins) _cover = new byte[Math.Max(bins, _cover.Length * 2)];
            Array.Clear(_cover, 0, bins);
            for (int f = c.First; f >= 0; f = FrameNext(f))
            {
                int b = FrameMediaMs(f) / FrameMs;
                if (b >= 0 && b < bins) _cover[b] = 1;
            }

            int w = Math.Max(1, lengthMs / FrameMs);
            int bestStart = 0, best;
            if (w >= bins)
            {
                w = bins;
                best = Sum(0, bins);
            }
            else
            {
                int sum = Sum(0, w);
                best = sum;
                for (int s = 1; s + w <= bins; s++)
                {
                    sum += _cover[s + w - 1] - _cover[s - 1];
                    if (sum > best) { best = sum; bestStart = s; }
                }
            }
            if (best * FrameMs < minSpeechMs) return false;

            if (bestStart > 0 && _cover[bestStart] != 0)
            {
                int limit = Math.Max(0, bestStart - SnapBackMs / FrameMs);
                for (int b = bestStart - 1; b >= limit; b--)
                    if (_cover[b] == 0) { bestStart = b; break; }
            }

            int startMs = bestStart * FrameMs, len = w * FrameMs;
            byte speakers = 0;
            for (int f = c.First; f >= 0; f = FrameNext(f))
            {
                int m = FrameMediaMs(f);
                if (m >= startMs && m < startMs + len) speakers |= (byte)(1 << FrameSlot(f));
            }
            snippet = new RecordingSnippet
            {
                CallId = c.Id, StartMs = startMs, LengthMs = len, SpeechMs = Sum(bestStart, w) * FrameMs,
                SpeakersMask = speakers, ParticipantsMask = c.Participants, CallLengthMs = c.EndMs,
            };
            return true;
        }

        int Sum(int start, int count)
        {
            int s = 0;
            for (int i = start; i < start + count; i++) s += _cover[i];
            return s;
        }
    }
}
