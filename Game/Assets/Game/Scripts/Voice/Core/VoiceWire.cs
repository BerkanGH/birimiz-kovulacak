using System;

namespace BirimizKovulacak.Voice
{
    /// <summary>One frame inside a received bundle; the payload stays in the bundle buffer (no copy).</summary>
    public struct VoiceFrame
    {
        public byte Slot;
        public ushort Sequence;
        public VoiceFrameFlags Flags;
        public int PayloadOffset;
        public int PayloadLength;
    }

    /// <summary>
    /// Wire format shared by both directions (client→host "bk.voice.up", host→client "bk.voice.down"):
    /// <code>
    /// bundle := version:u8 frame*
    /// frame  := slot:u8 seq:u16le flags:u8 len:u8 payload[len]
    /// </code>
    /// 5 bytes of header per 20 ms frame (2 kbps). One bundle per recipient per Unity frame keeps message
    /// overhead low; bundles never exceed <see cref="MaxBundleBytes"/> (below NGO's 1296-byte unfragmented limit).
    /// </summary>
    public static class VoiceWire
    {
        public const byte Version = 0xB5;
        public const int FrameHeaderBytes = 5;
        public const int MaxBundleBytes = 1100;
    }

    /// <summary>Builds a bundle into a fixed buffer. Allocation-free after construction.</summary>
    public sealed class VoiceBundleWriter
    {
        readonly byte[] _buffer;
        int _length;
        int _frames;

        public VoiceBundleWriter(int capacity = VoiceWire.MaxBundleBytes)
        {
            _buffer = new byte[Math.Max(capacity, 1 + VoiceWire.FrameHeaderBytes + VoiceFormat.MaxPayloadBytes)];
            Reset();
        }

        public byte[] Buffer => _buffer;
        public int Length => _length;
        public int FrameCount => _frames;
        public bool IsEmpty => _frames == 0;

        public void Reset()
        {
            _buffer[0] = VoiceWire.Version;
            _length = 1;
            _frames = 0;
        }

        /// <summary>True when a frame with this payload size still fits.</summary>
        public bool HasRoomFor(int payloadLength)
        {
            return _length + VoiceWire.FrameHeaderBytes + payloadLength <= _buffer.Length;
        }

        public bool TryAppend(int slot, ushort sequence, VoiceFrameFlags flags, byte[] payload, int offset, int length)
        {
            if (length < 0 || length > VoiceFormat.MaxPayloadBytes || slot < 0 || slot > 255) return false;
            if (!HasRoomFor(length)) return false;
            int p = _length;
            _buffer[p++] = (byte)slot;
            _buffer[p++] = (byte)(sequence & 0xFF);
            _buffer[p++] = (byte)(sequence >> 8);
            _buffer[p++] = (byte)flags;
            _buffer[p++] = (byte)length;
            if (length > 0) System.Buffer.BlockCopy(payload, offset, _buffer, p, length);
            _length = p + length;
            _frames++;
            return true;
        }
    }

    /// <summary>Parses a bundle. Stops (returns false) at the end or at the first malformed frame.</summary>
    public struct VoiceBundleReader
    {
        byte[] _buffer;
        int _length;
        int _pos;

        public bool Begin(byte[] buffer, int length)
        {
            _buffer = buffer;
            _length = buffer == null ? 0 : Math.Min(length, buffer.Length);
            _pos = 1;
            if (_length < 1 || buffer[0] != VoiceWire.Version) { _length = 0; return false; }
            return true;
        }

        public bool TryNext(out VoiceFrame frame)
        {
            frame = default;
            if (_buffer == null || _pos + VoiceWire.FrameHeaderBytes > _length) return false;
            int p = _pos;
            frame.Slot = _buffer[p++];
            frame.Sequence = (ushort)(_buffer[p] | (_buffer[p + 1] << 8));
            p += 2;
            frame.Flags = (VoiceFrameFlags)_buffer[p++];
            int len = _buffer[p++];
            if (len > VoiceFormat.MaxPayloadBytes || p + len > _length || frame.Slot >= VoiceFormat.MaxPlayers)
            {
                _pos = _length; // malformed: drop the rest of the bundle
                return false;
            }
            frame.PayloadOffset = p;
            frame.PayloadLength = len;
            _pos = p + len;
            return true;
        }
    }
}
