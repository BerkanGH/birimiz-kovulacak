using System;

namespace BirimizKovulacak.Voice
{
    /// <summary>Receives encoded frames of the local speaker.</summary>
    public interface IVoiceFrameSink
    {
        /// <summary>payload[0..length) is only valid during the call.</summary>
        void OnVoiceFrame(ushort sequence, VoiceFrameFlags flags, byte[] payload, int length);
    }

    /// <summary>
    /// Local capture chain: device-rate mono samples → resample to 16 kHz → 20 ms frames → level meter + VAD →
    /// push-to-talk / open-mic gate (with pre-roll and release tail) → Opus encode → sink. Allocation-free per
    /// frame (with the native codec); only <see cref="SetInputRate"/> allocates, when the device rate changes.
    /// </summary>
    public sealed class VoiceCapturePipeline
    {
        public const int ChunkSamples = 2048;
        public const int PrerollFrames = 2;

        readonly IVoiceEncoder _encoder;
        StreamResampler _resampler;
        readonly float[] _resampled = new float[8192];
        readonly float[] _frame = new float[VoiceFormat.FrameSamples];
        int _frameFill;
        readonly float[][] _preroll = new float[PrerollFrames][];
        int _prerollCount, _prerollNext;
        readonly byte[] _packet = new byte[VoiceFormat.MaxPayloadBytes];
        ushort _seq;
        bool _open;
        int _pttTail;

        // Inputs (set by the owner every frame).
        public bool PushToTalk = true;
        public bool PttHeld;
        public bool SelfMuted;
        /// <summary>False outside a session: the meter and VAD still run, nothing is encoded.</summary>
        public bool TransmitEnabled = true;
        public float InputGain = 1f;
        /// <summary>Keep sending this many frames after the PTT key is released (120 ms).</summary>
        public int PttReleaseFrames = 6;
        public IVoiceFrameSink Sink;
        public readonly VoiceActivityDetector Vad = new VoiceActivityDetector();

        // Outputs.
        public float LevelDb { get; private set; } = -100f;
        /// <summary>Smoothed 0..1 meter of the (gained) microphone signal.</summary>
        public float Level01 { get; private set; }
        public bool IsTransmitting => _open;
        public int InputRate => _resampler.InputRate;
        public VoiceCodecBackend Backend => _encoder.Backend;

        // Counters (cumulative).
        public long FramesIn, FramesEncoded, BytesEncoded, EncodeErrors, Spurts;

        public VoiceCapturePipeline(IVoiceEncoder encoder, int inputRate, ushort initialSequence = 0)
        {
            _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
            _resampler = new StreamResampler(inputRate, VoiceFormat.CodecSampleRate);
            for (int i = 0; i < PrerollFrames; i++) _preroll[i] = new float[VoiceFormat.FrameSamples];
            _seq = initialSequence;
        }

        public ushort NextSequence => _seq;

        public void SetInputRate(int rate)
        {
            if (rate == _resampler.InputRate) return;
            _resampler = new StreamResampler(rate, VoiceFormat.CodecSampleRate);
            _frameFill = 0;
        }

        /// <summary>Feeds device-rate mono samples. NOTE: the input range may be modified (used as scratch).</summary>
        public void Process(float[] input, int offset, int count)
        {
            while (count > 0)
            {
                int n = count < ChunkSamples ? count : ChunkSamples;
                int produced = _resampler.Process(input, offset, n, _resampled, 0);
                for (int i = 0; i < produced; i++)
                {
                    float s = _resampled[i] * InputGain;
                    _frame[_frameFill++] = s > 1f ? 1f : (s < -1f ? -1f : s);
                    if (_frameFill == VoiceFormat.FrameSamples)
                    {
                        ProcessFrame();
                        _frameFill = 0;
                    }
                }
                offset += n;
                count -= n;
            }
        }

        void ProcessFrame()
        {
            FramesIn++;
            float db = VoiceLevel.RmsDb(_frame, 0, VoiceFormat.FrameSamples);
            LevelDb = db;
            float m = VoiceLevel.Meter(db);
            Level01 = m > Level01 ? m : Level01 * 0.85f + m * 0.15f;
            bool vad = Vad.ProcessLevel(db);

            bool want;
            if (!TransmitEnabled || SelfMuted) want = false;
            else if (PushToTalk)
            {
                if (PttHeld) { want = true; _pttTail = PttReleaseFrames; }
                else if (_open && _pttTail > 0) { _pttTail--; want = true; }
                else want = false;
            }
            else want = vad;

            if (want)
            {
                if (!_open)
                {
                    _open = true;
                    Spurts++;
                    // Pre-roll: the frames just before the gate opened (VAD attack / reaction to the key press).
                    for (int k = _prerollCount; k > 0; k--)
                    {
                        int idx = (_prerollNext - k + PrerollFrames * 2) % PrerollFrames;
                        Encode(_preroll[idx], VoiceFrameFlags.None);
                    }
                }
                Encode(_frame, VoiceFrameFlags.None);
            }
            else if (_open)
            {
                _open = false;
                Encode(_frame, VoiceFrameFlags.EndOfSpeech);
            }

            Array.Copy(_frame, _preroll[_prerollNext], VoiceFormat.FrameSamples);
            _prerollNext = (_prerollNext + 1) % PrerollFrames;
            if (_prerollCount < PrerollFrames) _prerollCount++;
        }

        void Encode(float[] pcm, VoiceFrameFlags flags)
        {
            int len = _encoder.Encode(pcm, _packet, 0, VoiceFormat.MaxPayloadBytes);
            if (len < 0) { EncodeErrors++; return; }
            FramesEncoded++;
            BytesEncoded += len;
            Sink?.OnVoiceFrame(_seq, flags, _packet, len);
            _seq++;
        }

        /// <summary>Closes an open spurt immediately (e.g. disconnect, device change).</summary>
        public void ForceClose()
        {
            if (_open) { _open = false; Encode(_frame, VoiceFrameFlags.EndOfSpeech); }
            _pttTail = 0;
        }

        public void Reset()
        {
            _open = false;
            _pttTail = 0;
            _frameFill = 0;
            _prerollCount = 0;
            _resampler.Reset();
            _encoder.Reset();
            Vad.Reset();
            Level01 = 0;
        }
    }
}
