using System;

namespace BirimizKovulacak.Voice
{
    /// <summary>
    /// Fixed audio format of the voice pipeline. Capture is resampled to 16 kHz mono and cut into 20 ms frames;
    /// playback decodes at 48 kHz (Opus decodes to any of its native rates regardless of the encoded rate).
    /// This folder (Scripts/Voice/Core) is pure C#: no UnityEngine, so Tools/VoiceTests can run it.
    /// </summary>
    public static class VoiceFormat
    {
        public const int CodecSampleRate = 16000;
        public const int FrameMs = 20;
        public const int FrameSamples = CodecSampleRate * FrameMs / 1000;          // 320
        public const int PlaybackSampleRate = 48000;
        public const int PlaybackFrameSamples = PlaybackSampleRate * FrameMs / 1000; // 960
        public const int MaxPlayers = 6;
        /// <summary>Hard cap of one encoded Opus frame (fits the one-byte length field of the wire format).</summary>
        public const int MaxPayloadBytes = 200;
        /// <summary>Opus target. 18 kbps keeps payload + 5 B frame header + NGO named-message header (~11 B) under 24 kbps.</summary>
        public const int DefaultBitrate = 18000;
        public const int NoSlot = -1;
    }

    /// <summary>Per-frame flags. The client may only set <see cref="EndOfSpeech"/>; the host composes the rest.</summary>
    [Flags]
    public enum VoiceFrameFlags : byte
    {
        None = 0,
        /// <summary>Last frame of a talk spurt: the receiver stops playout without a concealment tail.</summary>
        EndOfSpeech = 1,
        /// <summary>Private phone call stream: 2D, clear, full volume.</summary>
        Call = 2,
        /// <summary>Proximity voice through a wall: low-pass + lower gain.</summary>
        Muffled = 4,
        /// <summary>Everyone-hears phase (lobby, meeting, results): 2D, full volume.</summary>
        AllHear = 8,
        /// <summary>Local mic test: the player's own voice played back to them.</summary>
        Loopback = 16,
    }

    public enum VoiceMode : byte
    {
        /// <summary>Proximity voice in the office plus private calls.</summary>
        Office = 0,
        /// <summary>Everyone hears everyone (lobby, meeting, results).</summary>
        AllHear = 1,
        /// <summary>Nothing is routed.</summary>
        Muted = 2,
    }

    /// <summary>How the local player currently hears a remote speaker (for UI icons).</summary>
    public enum VoiceHearing : byte
    {
        None = 0,
        Proximity = 1,
        Muffled = 2,
        Call = 3,
        AllHear = 4,
        Loopback = 5,
    }

    public enum VoiceCodecBackend : byte
    {
        /// <summary>Native libopus when it loads, otherwise managed Concentus.</summary>
        Auto = 0,
        /// <summary>libopus (opus.dll) via P/Invoke: zero managed allocations per frame.</summary>
        Native = 1,
        /// <summary>Concentus (pure C#): portable, but allocates ~50-90 KB per encoded frame.</summary>
        Managed = 2,
    }

    /// <summary>Minimal position type so the core does not depend on UnityEngine or the rules contracts.</summary>
    public struct VoiceVec3
    {
        public float X, Y, Z;

        public VoiceVec3(float x, float y, float z) { X = x; Y = y; Z = z; }

        public static float DistanceSquared(in VoiceVec3 a, in VoiceVec3 b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        public static float Distance(in VoiceVec3 a, in VoiceVec3 b) { return (float)Math.Sqrt(DistanceSquared(a, b)); }

        public override string ToString() { return "(" + X.ToString("0.0") + "," + Y.ToString("0.0") + "," + Z.ToString("0.0") + ")"; }
    }

    public static class VoiceSeq
    {
        /// <summary>Wrap-aware difference a - b of 16-bit sequence numbers (-32768..32767).</summary>
        public static int Diff(ushort a, ushort b) { return (short)(a - b); }
    }
}
