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
        /// <summary>Who hears the local player right now.</summary>
        VoiceRoute Route { get; }
        /// <summary>Display label of the push-to-talk key (e.g. "V").</summary>
        string PttKeyLabel { get; }
    }

    public enum VoiceRoute : byte { None = 0, Proximity = 1, Call = 2, Everyone = 3 }

    /// <summary>Default until the voice system registers itself: nothing is speaking.</summary>
    public sealed class NullVoiceUi : IVoiceUi
    {
        public bool Available => false;
        public bool IsSpeaking(int playerId) { return false; }
        public float MicLevel => 0f;
        public bool Transmitting => false;
        public VoiceRoute Route => VoiceRoute.None;
        public string PttKeyLabel => "V";
    }

    /// <summary>Service registry the adapter / voice code fills in (see docs/UI.md "Integration steps").</summary>
    public static class UiServices
    {
        public static IVoiceUi Voice = new NullVoiceUi();
    }

    /// <summary>
    /// Player settings persisted in PlayerPrefs. The UI writes them; audio/voice code reads them (and listens to
    /// <see cref="Changed"/>). Keys are part of the integration contract (docs/UI.md).
    /// </summary>
    public static class UiSettings
    {
        public const string MasterVolumeKey = "settings.volume";      // Sfx.Volume -> AudioListener.volume
        public const string AmbienceVolumeKey = "settings.ambience_volume"; // Sfx.AmbienceVolume
        public const string CameraShakeKey = "settings.camera_shake"; // CameraRig.JuiceScale
        public const string SfxVolumeKey = "settings.sfx_volume";     // 0..1 (Sfx.EffectsVolume)
        public const string VoiceVolumeKey = "voice.volume";          // 0..1 incoming voice
        public const string VoiceModeKey = "voice.mode";              // 0 = push-to-talk, 1 = open mic (VAD)
        public const string VadThresholdKey = "voice.vad_threshold";  // 0..1 of MicLevel
        public const string MicDeviceKey = "voice.mic_device";        // "" = system default
        public const string MicMutedKey = "voice.mic_muted";          // 0/1
        public const string PlayerVolumePrefix = "voice.player.";     // + player name, 0..2 (1 = normal)
        public const string LanguageKey = "settings.language";        // "tr"

        /// <summary>Raised with the key that changed.</summary>
        public static event Action<string> Changed;

        public static float GetFloat(string key, float def) { return PlayerPrefs.GetFloat(key, def); }
        public static void SetFloat(string key, float v) { PlayerPrefs.SetFloat(key, v); Changed?.Invoke(key); }
        public static int GetInt(string key, int def) { return PlayerPrefs.GetInt(key, def); }
        public static void SetInt(string key, int v) { PlayerPrefs.SetInt(key, v); Changed?.Invoke(key); }
        public static string GetString(string key, string def) { return PlayerPrefs.GetString(key, def); }
        public static void SetString(string key, string v) { PlayerPrefs.SetString(key, v); Changed?.Invoke(key); }

        public static bool PushToTalk { get { return GetInt(VoiceModeKey, 0) == 0; } set { SetInt(VoiceModeKey, value ? 0 : 1); } }
        public static bool MicMuted { get { return GetInt(MicMutedKey, 0) != 0; } set { SetInt(MicMutedKey, value ? 1 : 0); } }
        public static float PlayerVolume(string playerName) { return GetFloat(PlayerVolumePrefix + playerName, 1f); }
        public static void SetPlayerVolume(string playerName, float v) { SetFloat(PlayerVolumePrefix + playerName, v); }

        public static void Save() { PlayerPrefs.Save(); }
    }

    /// <summary>
    /// UI-only keys. Gameplay keys (emote wheel T + 1-5, answer Y, decline/hang up N, use item G, crouch, push-to-talk)
    /// live in GameInput and are handled by LocalPlayerController (feel); the UI only displays them.
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
