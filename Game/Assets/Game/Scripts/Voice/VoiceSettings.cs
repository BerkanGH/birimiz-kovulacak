using System;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    public enum VoiceInputMode
    {
        PushToTalk = 0,
        OpenMic = 1,
    }

    /// <summary>
    /// Player voice preferences, persisted in PlayerPrefs ("voice.*"). The settings screen (UI agent) edits the
    /// properties and calls <see cref="Save"/>; <see cref="Changed"/> fires on every change so VoiceSystem applies
    /// them immediately. Per-player volume is keyed by player name so it survives slot changes between lobbies.
    /// </summary>
    public sealed class VoiceSettings
    {
        const string KDevice = "voice.device", KMode = "voice.mode", KVad = "voice.vad", KVolume = "voice.volume",
            KInputGain = "voice.input_gain", KSelfMute = "voice.self_mute", KDeafen = "voice.deafen", KPlayer = "voice.player.";

        string _device = "";
        VoiceInputMode _mode = VoiceInputMode.PushToTalk;
        float _vad = 0.5f, _volume = 1f, _inputGain = 1f;
        bool _selfMute, _deafen;

        public event Action Changed;

        /// <summary>Microphone device name ("" = system default).</summary>
        public string InputDevice
        {
            get => _device;
            set { value = value ?? ""; if (_device == value) return; _device = value; Notify(); }
        }

        public VoiceInputMode InputMode
        {
            get => _mode;
            set { if (_mode == value) return; _mode = value; Notify(); }
        }

        /// <summary>Open-mic sensitivity 0..1 (1 = picks up quiet speech, 0 = only loud speech).</summary>
        public float VadSensitivity
        {
            get => _vad;
            set { value = Mathf.Clamp01(value); if (Mathf.Approximately(_vad, value)) return; _vad = value; Notify(); }
        }

        /// <summary>Master voice volume 0..2 (applies to every remote speaker).</summary>
        public float MasterVolume
        {
            get => _volume;
            set { value = Mathf.Clamp(value, 0f, 2f); if (Mathf.Approximately(_volume, value)) return; _volume = value; Notify(); }
        }

        /// <summary>Microphone gain 0..4.</summary>
        public float InputGain
        {
            get => _inputGain;
            set { value = Mathf.Clamp(value, 0f, 4f); if (Mathf.Approximately(_inputGain, value)) return; _inputGain = value; Notify(); }
        }

        public bool SelfMute
        {
            get => _selfMute;
            set { if (_selfMute == value) return; _selfMute = value; Notify(); }
        }

        /// <summary>Hear nothing (and, like Discord, also do not transmit).</summary>
        public bool Deafen
        {
            get => _deafen;
            set { if (_deafen == value) return; _deafen = value; Notify(); }
        }

        /// <summary>Per-player volume 0..2 keyed by player name (1 = default).</summary>
        public float GetPlayerVolume(string playerKey)
        {
            if (string.IsNullOrEmpty(playerKey)) return 1f;
            return PlayerPrefs.GetFloat(KPlayer + playerKey, 1f);
        }

        public void SetPlayerVolume(string playerKey, float volume)
        {
            if (string.IsNullOrEmpty(playerKey)) return;
            PlayerPrefs.SetFloat(KPlayer + playerKey, Mathf.Clamp(volume, 0f, 2f));
            Changed?.Invoke();
        }

        void Notify() { Changed?.Invoke(); }

        public static VoiceSettings Load()
        {
            var s = new VoiceSettings
            {
                _device = PlayerPrefs.GetString(KDevice, ""),
                _mode = (VoiceInputMode)Mathf.Clamp(PlayerPrefs.GetInt(KMode, 0), 0, 1),
                _vad = Mathf.Clamp01(PlayerPrefs.GetFloat(KVad, 0.5f)),
                _volume = Mathf.Clamp(PlayerPrefs.GetFloat(KVolume, 1f), 0f, 2f),
                _inputGain = Mathf.Clamp(PlayerPrefs.GetFloat(KInputGain, 1f), 0f, 4f),
                _selfMute = PlayerPrefs.GetInt(KSelfMute, 0) != 0,
                _deafen = PlayerPrefs.GetInt(KDeafen, 0) != 0,
            };
            return s;
        }

        public void Save()
        {
            PlayerPrefs.SetString(KDevice, _device);
            PlayerPrefs.SetInt(KMode, (int)_mode);
            PlayerPrefs.SetFloat(KVad, _vad);
            PlayerPrefs.SetFloat(KVolume, _volume);
            PlayerPrefs.SetFloat(KInputGain, _inputGain);
            PlayerPrefs.SetInt(KSelfMute, _selfMute ? 1 : 0);
            PlayerPrefs.SetInt(KDeafen, _deafen ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
