using System;
using BirimizKovulacak.Voice;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Unity Microphone wrapper: records into a looping 1 s clip and, every frame, copies the new samples into a
    /// preallocated buffer (AudioClip.GetData(Span) — no allocations) and feeds them to the capture pipeline.
    /// Records at 48 kHz when the device allows any rate; the pipeline resamples to 16 kHz itself.
    /// </summary>
    public sealed class VoiceMicrophone
    {
        const int ClipSeconds = 1;
        const int PreferredRate = 48000;

        string _device;
        AudioClip _clip;
        int _rate, _channels, _length, _readPos;
        readonly float[] _interleaved = new float[8192];
        readonly float[] _mono = new float[8192];

        public bool IsRunning => _clip != null;
        public int SampleRate => _rate;
        public string ActiveDevice => _device ?? "";
        public string LastError { get; private set; } = "";

        /// <summary>Starts recording. <paramref name="device"/> "" or unknown = system default.</summary>
        public bool Start(string device)
        {
            Stop();
            string[] devices = Microphone.devices; // allocates; only on (re)start
            if (devices.Length == 0) { LastError = "no_device"; return false; }
            _device = !string.IsNullOrEmpty(device) && Array.IndexOf(devices, device) >= 0 ? device : null;
            int min, max;
            Microphone.GetDeviceCaps(_device, out min, out max);
            int rate = (min == 0 && max == 0) ? PreferredRate : Mathf.Clamp(PreferredRate, min, max);
            try { _clip = Microphone.Start(_device, true, ClipSeconds, rate); }
            catch (Exception e) { LastError = e.Message; _clip = null; }
            if (_clip == null) { if (string.IsNullOrEmpty(LastError)) LastError = "start_failed"; return false; }
            _rate = _clip.frequency;
            _channels = Mathf.Max(1, _clip.channels);
            _length = _clip.samples;
            _readPos = 0;
            LastError = "";
            return true;
        }

        public void Stop()
        {
            if (_clip != null)
            {
                Microphone.End(_device);
                UnityEngine.Object.Destroy(_clip);
            }
            _clip = null;
        }

        /// <summary>False when the device disappeared (unplugged) — the owner restarts it later.</summary>
        public bool StillRecording => _clip != null && Microphone.IsRecording(_device);

        /// <summary>Feeds every sample recorded since the last call into <paramref name="pipeline"/>.</summary>
        public void Read(VoiceCapturePipeline pipeline)
        {
            if (_clip == null) return;
            int pos = Microphone.GetPosition(_device);
            if (pos < 0 || pos >= _length) return;
            int avail = pos - _readPos;
            if (avail < 0) avail += _length;
            int maxFrames = _interleaved.Length / _channels;
            while (avail > 0)
            {
                int n = Math.Min(avail, Math.Min(_length - _readPos, maxFrames));
                _clip.GetData(new Span<float>(_interleaved, 0, n * _channels), _readPos);
                float[] mono = _interleaved;
                if (_channels > 1)
                {
                    float inv = 1f / _channels;
                    for (int i = 0; i < n; i++)
                    {
                        float s = 0;
                        for (int c = 0; c < _channels; c++) s += _interleaved[i * _channels + c];
                        _mono[i] = s * inv;
                    }
                    mono = _mono;
                }
                pipeline.Process(mono, 0, n);
                _readPos = (_readPos + n) % _length;
                avail -= n;
            }
        }
    }
}
