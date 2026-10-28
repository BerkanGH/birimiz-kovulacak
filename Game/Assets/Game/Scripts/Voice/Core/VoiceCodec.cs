using System;

namespace BirimizKovulacak.Voice
{
    /// <summary>Opus encoder for one 16 kHz mono stream (20 ms frames of <see cref="VoiceFormat.FrameSamples"/>).</summary>
    public interface IVoiceEncoder : IDisposable
    {
        VoiceCodecBackend Backend { get; }
        /// <summary>Encodes pcm[0..320) into output[offset..]; returns the byte count or a negative error.</summary>
        int Encode(float[] pcm, byte[] output, int offset, int maxBytes);
        void Reset();
    }

    /// <summary>Opus decoder producing 20 ms frames at its construction sample rate.</summary>
    public interface IVoiceDecoder : IDisposable
    {
        VoiceCodecBackend Backend { get; }
        int SampleRate { get; }
        int FrameSamples { get; }
        /// <summary>Decodes one packet. With <paramref name="fec"/> the packet is the one AFTER a lost frame and
        /// the lost frame is reconstructed from its in-band FEC data. Returns samples or a negative error.</summary>
        int Decode(byte[] data, int offset, int length, float[] pcm, bool fec);
        /// <summary>Packet loss concealment for one missing frame.</summary>
        int DecodeLoss(float[] pcm);
        void Reset();
    }

    public struct VoiceEncoderSettings
    {
        public int Bitrate;
        public int Complexity;
        public bool InbandFec;
        public int PacketLossPercent;
        public bool ConstrainedVbr;

        /// <summary>18 kbps constrained VBR, wideband voice, in-band FEC tuned for 10 % loss.</summary>
        public static VoiceEncoderSettings Default => new VoiceEncoderSettings
        {
            Bitrate = VoiceFormat.DefaultBitrate,
            Complexity = 8,
            InbandFec = true,
            PacketLossPercent = 10,
            ConstrainedVbr = true,
        };
    }

    /// <summary>
    /// Picks the codec implementation. Both produce standard Opus packets, so a native client and a managed
    /// client interoperate (verified in Tools/VoiceTests).
    /// </summary>
    public static class VoiceCodec
    {
        public static string LastError { get; private set; } = "";

        public static VoiceCodecBackend Resolve(VoiceCodecBackend requested)
        {
            if (requested == VoiceCodecBackend.Managed) return VoiceCodecBackend.Managed;
            if (NativeOpus.IsAvailable) return VoiceCodecBackend.Native;
            LastError = NativeOpus.LoadError;
            return VoiceCodecBackend.Managed;
        }

        public static IVoiceEncoder CreateEncoder(VoiceEncoderSettings settings, VoiceCodecBackend requested = VoiceCodecBackend.Auto)
        {
            if (Resolve(requested) == VoiceCodecBackend.Native)
            {
                try { return new NativeOpusEncoder(settings); }
                catch (Exception e) { LastError = e.Message; }
            }
            return new ManagedOpusEncoder(settings);
        }

        public static IVoiceDecoder CreateDecoder(int sampleRate = VoiceFormat.PlaybackSampleRate, VoiceCodecBackend requested = VoiceCodecBackend.Auto)
        {
            if (Resolve(requested) == VoiceCodecBackend.Native)
            {
                try { return new NativeOpusDecoder(sampleRate); }
                catch (Exception e) { LastError = e.Message; }
            }
            return new ManagedOpusDecoder(sampleRate);
        }
    }
}
