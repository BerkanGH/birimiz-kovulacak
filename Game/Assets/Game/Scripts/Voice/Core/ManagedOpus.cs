using System;
using Concentus.Enums;
using Concentus.Structs;

// Concentus 2.x marks its direct constructors obsolete in favour of OpusCodecFactory, which would probe for a
// native library on its own. We choose the backend ourselves (VoiceCodec), so the managed types are used directly.
#pragma warning disable CS0618

namespace BirimizKovulacak.Voice
{
    /// <summary>
    /// Pure C# Opus (Concentus 2.2.2) fallback used when opus.dll cannot be loaded. Bit-compatible with libopus.
    /// Known cost (measured in Tools/VoiceTests): ~50-70 KB managed garbage per encoded 20 ms frame and ~10 KB per
    /// decoded frame, because the port turns C stack arrays into heap arrays. Complexity is capped at 5 here to
    /// keep CPU and garbage down.
    /// </summary>
    public sealed class ManagedOpusEncoder : IVoiceEncoder
    {
        readonly OpusEncoder _enc;

        public VoiceCodecBackend Backend => VoiceCodecBackend.Managed;

        public ManagedOpusEncoder(VoiceEncoderSettings s)
        {
            _enc = new OpusEncoder(VoiceFormat.CodecSampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
            _enc.Bitrate = s.Bitrate;
            _enc.Complexity = Math.Max(0, Math.Min(5, s.Complexity));
            _enc.UseVBR = true;
            _enc.UseConstrainedVBR = s.ConstrainedVbr;
            _enc.UseInbandFEC = s.InbandFec;
            _enc.PacketLossPercent = Math.Max(0, Math.Min(100, s.PacketLossPercent));
            _enc.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;
            _enc.MaxBandwidth = OpusBandwidth.OPUS_BANDWIDTH_WIDEBAND;
            _enc.UseDTX = false;
        }

        public int Encode(float[] pcm, byte[] output, int offset, int maxBytes)
        {
            if (pcm == null || pcm.Length < VoiceFormat.FrameSamples || output == null || offset < 0 || offset >= output.Length) return -1;
            maxBytes = Math.Min(maxBytes, output.Length - offset);
            try
            {
                return _enc.Encode(new ReadOnlySpan<float>(pcm, 0, VoiceFormat.FrameSamples), VoiceFormat.FrameSamples,
                    new Span<byte>(output, offset, maxBytes), maxBytes);
            }
            catch (Exception) { return -3; }
        }

        public void Reset() { _enc.ResetState(); }

        public void Dispose() { }
    }

    public sealed class ManagedOpusDecoder : IVoiceDecoder
    {
        readonly OpusDecoder _dec;
        readonly int _rate;
        readonly int _frame;

        public VoiceCodecBackend Backend => VoiceCodecBackend.Managed;
        public int SampleRate => _rate;
        public int FrameSamples => _frame;

        public ManagedOpusDecoder(int sampleRate)
        {
            _rate = sampleRate;
            _frame = sampleRate * VoiceFormat.FrameMs / 1000;
            _dec = new OpusDecoder(sampleRate, 1);
        }

        public int Decode(byte[] data, int offset, int length, float[] pcm, bool fec)
        {
            if (pcm == null || pcm.Length < _frame) return -1;
            if (data == null || length <= 0 || offset < 0 || offset + length > data.Length) return DecodeLoss(pcm);
            try { return _dec.Decode(new ReadOnlySpan<byte>(data, offset, length), new Span<float>(pcm, 0, _frame), _frame, fec); }
            catch (Exception) { return -4; }
        }

        public int DecodeLoss(float[] pcm)
        {
            if (pcm == null || pcm.Length < _frame) return -1;
            try { return _dec.Decode(ReadOnlySpan<byte>.Empty, new Span<float>(pcm, 0, _frame), _frame, false); }
            catch (Exception) { return -4; }
        }

        public void Reset() { _dec.ResetState(); }

        public void Dispose() { }
    }
}
