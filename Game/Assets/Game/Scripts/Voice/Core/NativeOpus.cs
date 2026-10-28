using System;
using System.Runtime.InteropServices;

namespace BirimizKovulacak.Voice
{
    /// <summary>
    /// Minimal P/Invoke binding to libopus (Assets/Plugins/Concentus/x86_64/opus.dll, libopus 1.5.2 from
    /// Concentus.Native). Arrays are pinned by the marshaller, so encode/decode allocate nothing managed.
    /// The *_ctl functions are C varargs; declaring them with fixed int arguments is correct for the Windows x64
    /// calling convention (the only shipping target). Other platforms fall back to Concentus automatically.
    /// </summary>
    public static class NativeOpus
    {
        const string Lib = "opus";

        public const int ApplicationVoip = 2048;
        public const int SetBitrate = 4002, GetBitrate = 4003, SetMaxBandwidth = 4004, SetVbr = 4006, SetComplexity = 4010,
            SetInbandFec = 4012, SetPacketLossPerc = 4014, SetDtx = 4016, SetVbrConstraint = 4020, SetSignal = 4024,
            ResetState = 4028;
        public const int BandwidthWideband = 1103, SignalVoice = 3001;

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr opus_get_version_string();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr opus_encoder_create(int fs, int channels, int application, out int error);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void opus_encoder_destroy(IntPtr st);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int opus_encode_float(IntPtr st, float[] pcm, int frameSize, ref byte data, int maxDataBytes);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "opus_encoder_ctl")]
        internal static extern int EncoderCtl(IntPtr st, int request, int value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "opus_encoder_ctl")]
        internal static extern int EncoderCtlGet(IntPtr st, int request, out int value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr opus_decoder_create(int fs, int channels, out int error);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void opus_decoder_destroy(IntPtr st);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int opus_decode_float(IntPtr st, ref byte data, int len, [Out] float[] pcm, int frameSize, int decodeFec);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "opus_decode_float")]
        internal static extern int opus_decode_float_null(IntPtr st, IntPtr data, int len, [Out] float[] pcm, int frameSize, int decodeFec);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "opus_decoder_ctl")]
        internal static extern int DecoderCtl(IntPtr st, int request, int value);

        static int _state; // 0 unknown, 1 ok, 2 failed
        static string _version = "";

        public static string LoadError { get; private set; } = "";

        /// <summary>True when opus.dll (and its VC++ runtime) can be loaded. Probed once.</summary>
        public static bool IsAvailable
        {
            get
            {
                if (_state == 0) Probe();
                return _state == 1;
            }
        }

        public static string Version
        {
            get { return IsAvailable ? _version : ""; }
        }

        static void Probe()
        {
            try
            {
                IntPtr p = opus_get_version_string();
                _version = p == IntPtr.Zero ? "libopus" : Marshal.PtrToStringAnsi(p);
                _state = 1;
            }
            catch (Exception e)
            {
                // DllNotFoundException (missing dll or VCRUNTIME140), EntryPointNotFoundException, BadImageFormatException
                LoadError = e.GetType().Name + ": " + e.Message;
                _state = 2;
            }
        }
    }

    public sealed class NativeOpusEncoder : IVoiceEncoder
    {
        IntPtr _st;

        public VoiceCodecBackend Backend => VoiceCodecBackend.Native;

        public NativeOpusEncoder(VoiceEncoderSettings s)
        {
            int err;
            _st = NativeOpus.opus_encoder_create(VoiceFormat.CodecSampleRate, 1, NativeOpus.ApplicationVoip, out err);
            if (err != 0 || _st == IntPtr.Zero) throw new InvalidOperationException("opus_encoder_create failed: " + err);
            NativeOpus.EncoderCtl(_st, NativeOpus.SetBitrate, s.Bitrate);
            NativeOpus.EncoderCtl(_st, NativeOpus.SetComplexity, Clamp(s.Complexity, 0, 10));
            NativeOpus.EncoderCtl(_st, NativeOpus.SetVbr, 1);
            NativeOpus.EncoderCtl(_st, NativeOpus.SetVbrConstraint, s.ConstrainedVbr ? 1 : 0);
            NativeOpus.EncoderCtl(_st, NativeOpus.SetInbandFec, s.InbandFec ? 1 : 0);
            NativeOpus.EncoderCtl(_st, NativeOpus.SetPacketLossPerc, Clamp(s.PacketLossPercent, 0, 100));
            NativeOpus.EncoderCtl(_st, NativeOpus.SetSignal, NativeOpus.SignalVoice);
            NativeOpus.EncoderCtl(_st, NativeOpus.SetMaxBandwidth, NativeOpus.BandwidthWideband);
            NativeOpus.EncoderCtl(_st, NativeOpus.SetDtx, 0);
        }

        /// <summary>Reads back the configured bitrate (test hook for the varargs ctl binding).</summary>
        public int GetBitrate()
        {
            int v;
            NativeOpus.EncoderCtlGet(_st, NativeOpus.GetBitrate, out v);
            return v;
        }

        public int Encode(float[] pcm, byte[] output, int offset, int maxBytes)
        {
            if (_st == IntPtr.Zero) return -1;
            if (pcm == null || pcm.Length < VoiceFormat.FrameSamples || output == null || offset < 0 || offset >= output.Length) return -1;
            maxBytes = Math.Min(maxBytes, output.Length - offset);
            return NativeOpus.opus_encode_float(_st, pcm, VoiceFormat.FrameSamples, ref output[offset], maxBytes);
        }

        public void Reset()
        {
            if (_st != IntPtr.Zero) NativeOpus.EncoderCtl(_st, NativeOpus.ResetState, 0);
        }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        public void Dispose()
        {
            if (_st != IntPtr.Zero) { NativeOpus.opus_encoder_destroy(_st); _st = IntPtr.Zero; }
            GC.SuppressFinalize(this);
        }

        ~NativeOpusEncoder()
        {
            if (_st != IntPtr.Zero) { NativeOpus.opus_encoder_destroy(_st); _st = IntPtr.Zero; }
        }
    }

    public sealed class NativeOpusDecoder : IVoiceDecoder
    {
        IntPtr _st;
        readonly int _rate;
        readonly int _frame;

        public VoiceCodecBackend Backend => VoiceCodecBackend.Native;
        public int SampleRate => _rate;
        public int FrameSamples => _frame;

        public NativeOpusDecoder(int sampleRate)
        {
            int err;
            _rate = sampleRate;
            _frame = sampleRate * VoiceFormat.FrameMs / 1000;
            _st = NativeOpus.opus_decoder_create(sampleRate, 1, out err);
            if (err != 0 || _st == IntPtr.Zero) throw new InvalidOperationException("opus_decoder_create failed: " + err);
        }

        public int Decode(byte[] data, int offset, int length, float[] pcm, bool fec)
        {
            if (_st == IntPtr.Zero || pcm == null || pcm.Length < _frame) return -1;
            if (data == null || length <= 0 || offset < 0 || offset + length > data.Length) return DecodeLoss(pcm);
            return NativeOpus.opus_decode_float(_st, ref data[offset], length, pcm, _frame, fec ? 1 : 0);
        }

        public int DecodeLoss(float[] pcm)
        {
            if (_st == IntPtr.Zero || pcm == null || pcm.Length < _frame) return -1;
            return NativeOpus.opus_decode_float_null(_st, IntPtr.Zero, 0, pcm, _frame, 0);
        }

        public void Reset()
        {
            if (_st != IntPtr.Zero) NativeOpus.DecoderCtl(_st, NativeOpus.ResetState, 0);
        }

        public void Dispose()
        {
            if (_st != IntPtr.Zero) { NativeOpus.opus_decoder_destroy(_st); _st = IntPtr.Zero; }
            GC.SuppressFinalize(this);
        }

        ~NativeOpusDecoder()
        {
            if (_st != IntPtr.Zero) { NativeOpus.opus_decoder_destroy(_st); _st = IntPtr.Zero; }
        }
    }
}
