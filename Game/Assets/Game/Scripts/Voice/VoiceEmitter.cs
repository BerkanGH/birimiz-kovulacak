using BirimizKovulacak.Voice;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// One pooled audio output per remote speaker slot (6 total, created by <see cref="VoiceSystem"/>).
    /// The AudioSource loops a shared constant-1 "flatline" clip; OnAudioFilterRead multiplies the decoded voice
    /// into it. That way Unity applies its 3D panning to the voice whether spatialisation happens before or after
    /// the filter. Distance attenuation and the wall muffle are applied by <see cref="VoicePlaybackChannel"/>
    /// (computed from avatar positions, not from the camera-mounted AudioListener), so the AudioSource uses a flat
    /// custom rolloff and only pans. Office voice: spatialBlend 1 at the speaker's head; call / all-hear: 2D.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class VoiceEmitter : MonoBehaviour
    {
        const float HeadHeight = 1.6f;
        const double IdleStopMs = 1500;
        const double DeadAudioMs = 300;

        static AudioClip _flatline;
        static AnimationCurve _flatCurve;

        AudioSource _source;
        VoicePlaybackChannel _channel;
        readonly PcmPlayoutReader _reader = new PcmPlayoutReader();
        long _lastCallbacks;
        double _lastCallbackMs;
        double _lastActiveMs;
        float _currentBlend = -1f;

        public int Slot { get; private set; } = -1;
        /// <summary>Avatar transform to follow (null = none known: played 2D).</summary>
        public Transform Target;
        public PcmPlayoutReader Reader => _reader;
        public bool IsAudible => _source != null && _source.isPlaying;

        public void Init(int slot, VoicePlaybackChannel channel, int outputRate)
        {
            Slot = slot;
            _channel = channel;
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = true;
            _source.clip = Flatline(outputRate);
            _source.volume = 1f;
            _source.priority = 0;          // never culled by the voice limit
            _source.dopplerLevel = 0f;
            _source.spread = 0f;
            _source.rolloffMode = AudioRolloffMode.Custom;
            _source.minDistance = 1f;
            _source.maxDistance = 500f;
            _source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, FlatCurve());
            _source.spatialBlend = 0f;
            _source.bypassReverbZones = true;
            SetOutputRate(outputRate);
        }

        public void SetOutputRate(int outputRate)
        {
            if (_channel != null && outputRate > 0) _reader.SetRates(_channel.SampleRate, outputRate);
        }

        static AudioClip Flatline(int rate)
        {
            if (_flatline != null) return _flatline;
            if (rate <= 0) rate = 48000;
            _flatline = AudioClip.Create("voice_flatline", rate, 1, rate, false);
            var ones = new float[rate];
            for (int i = 0; i < ones.Length; i++) ones[i] = 1f;
            _flatline.SetData(ones, 0);
            return _flatline;
        }

        static AnimationCurve FlatCurve()
        {
            if (_flatCurve == null) _flatCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f));
            return _flatCurve;
        }

        /// <summary>Main thread, once per frame after the channel was pumped.</summary>
        public void Tick(double nowMs, float dt)
        {
            if (_channel == null) return;
            var ring = _channel.Ring;
            bool active = _channel.IsPlaying || ring.Count > 0;
            if (active) _lastActiveMs = nowMs;

            // Follow the speaker's head.
            if (Target != null) transform.position = Target.position + Vector3.up * HeadHeight;

            VoiceFrameFlags flags = _channel.CurrentFlags;
            bool twoD = Target == null || (flags & (VoiceFrameFlags.Call | VoiceFrameFlags.AllHear | VoiceFrameFlags.Loopback)) != 0;
            float blend = twoD ? 0f : 1f;
            if (blend != _currentBlend) { _source.spatialBlend = blend; _currentBlend = blend; }

            if (active && !_source.isPlaying)
            {
                _source.Play();
                _lastCallbackMs = nowMs;
            }
            else if (!active && _source.isPlaying && nowMs - _lastActiveMs > IdleStopMs)
            {
                _source.Stop();
            }

            // No audio device / audio disabled: nobody consumes the ring, so drain it in real time to keep the
            // jitter buffer and statistics honest (headless automated runs).
            long cb = _reader.Callbacks;
            if (cb != _lastCallbacks) { _lastCallbacks = cb; _lastCallbackMs = nowMs; }
            else if (_source.isPlaying && nowMs - _lastCallbackMs > DeadAudioMs)
                ring.Discard((int)(dt * _channel.SampleRate));
        }

        public void StopNow()
        {
            if (_source != null && _source.isPlaying) _source.Stop();
            _channel?.Ring.Clear();
        }

        /// <summary>Audio thread.</summary>
        void OnAudioFilterRead(float[] data, int channels)
        {
            var ch = _channel;
            if (ch == null)
            {
                for (int i = 0; i < data.Length; i++) data[i] = 0f;
                return;
            }
            _reader.MultiplyInto(ch.Ring, data, channels);
        }
    }
}
