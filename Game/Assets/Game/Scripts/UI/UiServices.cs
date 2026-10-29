using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BirimizKovulacak.Game
{
    /// <summary>What the voice system tells the UI (speaking rings, mic meter). Set <see cref="UiServices.Voice"/> from the voice code.</summary>
    public interface IVoiceUi
    {
        /// <summary>False until the voice system runs (the UI then hides voice indicators).</summary>
        bool Available { get; }
        /// <summary>Someone's voice is currently audible / being sent (including the local player).</summary>
        bool IsSpeaking(int playerId);
        /// <summary>Local microphone input level 0..1 (meter + VAD threshold preview in Settings).</summary>
        float MicLevel { get; }
        /// <summary>The local player's voice is being transmitted right now (PTT held or VAD open, not muted).</summary>
        bool Transmitting { get; }
        /// <summary>Display label of the push-to-talk key (e.g. "V").</summary>
        string PttKeyLabel { get; }
        /// <summary>Microphone muted by the player.</summary>
        bool Muted { get; }
        /// <summary>Push-to-talk mode (false = open mic).</summary>
        bool PushToTalk { get; }
    }

    /// <summary>Default until the voice system registers itself: nothing is speaking.</summary>
    public sealed class NullVoiceUi : IVoiceUi
    {
        public bool Available => false;
        public bool IsSpeaking(int playerId) { return false; }
        public float MicLevel => 0f;
        public bool Transmitting => false;
        public string PttKeyLabel => GameInput.KeyLabel(GameInput.PushToTalk);
        public bool Muted => false;
        public bool PushToTalk => true;
    }

    /// <summary>Adapter over the voice module (VoiceSystem.Instance; null with -novoice). Allocation-free.</summary>
    public sealed class VoiceSystemUi : IVoiceUi
    {
        string _pttLabel;
        float _pttLabelAt = -10f;

        public bool Available => VoiceSystem.Instance != null;
        public bool IsSpeaking(int playerId) { var vs = VoiceSystem.Instance; return vs != null && vs.IsSpeaking(playerId); }
        public float MicLevel { get { var vs = VoiceSystem.Instance; return vs != null ? vs.LocalMicLevel : 0f; } }
        public bool Transmitting { get { var vs = VoiceSystem.Instance; return vs != null && vs.IsTransmitting; } }
        public bool Muted { get { var vs = VoiceSystem.Instance; return vs != null && vs.Settings != null && vs.Settings.SelfMute; } }
        public bool PushToTalk { get { var vs = VoiceSystem.Instance; return vs == null || vs.Settings == null || vs.Settings.InputMode == VoiceInputMode.PushToTalk; } }

        public string PttKeyLabel
        {
            get
            {
                // The binding display string allocates: refresh at most once per second.
                if (_pttLabel == null || Time.unscaledTime - _pttLabelAt > 1f)
                {
                    _pttLabelAt = Time.unscaledTime;
                    var vs = VoiceSystem.Instance;
                    string l = vs != null ? vs.PushToTalkKey : GameInput.KeyLabel(GameInput.PushToTalk);
                    if (_pttLabel != l) _pttLabel = l;
                }
                return _pttLabel;
            }
        }
    }

    /// <summary>Service registry. <see cref="Voice"/> wraps the voice module unless another implementation is set.</summary>
    public static class UiServices
    {
        static readonly IVoiceUi _bridge = new VoiceSystemUi();
        public static IVoiceUi Override;
        public static IVoiceUi Voice => Override ?? _bridge;
    }

    /// <summary>
    /// Player settings persisted in PlayerPrefs. The UI writes them; audio/voice code reads them (and listens to
    /// <see cref="Changed"/>). Keys are part of the integration contract (docs/UI.md).
    /// </summary>
    public static class UiSettings
    {
        public const string MasterVolumeKey = "settings.volume";      // Sfx.Volume -> AudioListener.volume
        public const string AmbienceVolumeKey = "settings.ambience_volume"; // Sfx.AmbienceVolume
        public const string SfxVolumeKey = "settings.sfx_volume";     // 0..1 (Sfx.EffectsVolume)
        public const string LanguageKey = "settings.language";        // "tr"

        /// <summary>Raised with the key that changed.</summary>
        public static event Action<string> Changed;

        public static float GetFloat(string key, float def) { return PlayerPrefs.GetFloat(key, def); }
        public static void SetFloat(string key, float v) { PlayerPrefs.SetFloat(key, v); Changed?.Invoke(key); }
        public static int GetInt(string key, int def) { return PlayerPrefs.GetInt(key, def); }
        public static void SetInt(string key, int v) { PlayerPrefs.SetInt(key, v); Changed?.Invoke(key); }
        public static string GetString(string key, string def) { return PlayerPrefs.GetString(key, def); }
        public static void SetString(string key, string v) { PlayerPrefs.SetString(key, v); Changed?.Invoke(key); }


        public static void Save() { PlayerPrefs.Save(); }
    }

    /// <summary>
    /// UI-only keys. Desk keys (computer E, phone Tab, answer Y, decline/hang up N, push-to-talk V, pause Esc) live in
    /// GameInput.
    /// </summary>
    public static class UiInput
    {
        static InputActionAsset _asset;
        public static InputAction Confirm;

        public static void Initialize()
        {
            if (_asset != null) return;
            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = _asset.AddActionMap("UI");
            Confirm = map.AddAction("Confirm", InputActionType.Button, "<Keyboard>/enter");
            Confirm.AddBinding("<Keyboard>/numpadEnter");
            map.Enable();
        }
    }
}
