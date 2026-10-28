using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
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
        public const string MasterVolumeKey = "settings.volume";      // existing (Sfx.Volume -> AudioListener.volume)
        public const string SfxVolumeKey = "audio.sfx_volume";        // 0..1
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

    /// <summary>UI-owned keys (separate action map so the gameplay map stays untouched). Rebindable in Settings.</summary>
    public static class UiInput
    {
        const string PrefsKey = "ui.input.overrides";
        static InputActionAsset _asset;
        public static InputAction EmoteWheel, AnswerCall, DeclineCall, Confirm;
        public static readonly List<(InputAction action, int binding, string labelKey)> Rebindable = new List<(InputAction, int, string)>();

        public static void Initialize()
        {
            if (_asset != null) return;
            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = _asset.AddActionMap("UI");
            EmoteWheel = map.AddAction("EmoteWheel", InputActionType.Button, "<Keyboard>/g");
            AnswerCall = map.AddAction("AnswerCall", InputActionType.Button, "<Keyboard>/y");
            DeclineCall = map.AddAction("DeclineCall", InputActionType.Button, "<Keyboard>/n");
            Confirm = map.AddAction("Confirm", InputActionType.Button, "<Keyboard>/enter");
            Rebindable.Clear();
            Rebindable.Add((EmoteWheel, 0, "input.emote_wheel"));
            Rebindable.Add((AnswerCall, 0, "input.answer_call"));
            Rebindable.Add((DeclineCall, 0, "input.decline_call"));
            string json = PlayerPrefs.GetString(PrefsKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                try { _asset.LoadBindingOverridesFromJson(json); }
                catch (Exception e) { Debug.LogWarning("[UiInput] " + e.Message); }
            }
            map.Enable();
        }

        public static string KeyLabel(InputAction a)
        {
            if (a == null) return "?";
            return a.GetBindingDisplayString(0).ToUpperInvariant();
        }

        public static void StartRebind(InputAction action, int bindingIndex, Action onDone)
        {
            action.Disable();
            action.PerformInteractiveRebinding(bindingIndex)
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnComplete(op => { op.Dispose(); action.Enable(); Save(); onDone?.Invoke(); })
                .OnCancel(op => { op.Dispose(); action.Enable(); onDone?.Invoke(); })
                .Start();
        }

        public static void ResetBindings()
        {
            if (_asset == null) return;
            _asset.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(PrefsKey);
        }

        static void Save()
        {
            PlayerPrefs.SetString(PrefsKey, _asset.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }
    }

    /// <summary>Hooks the gameplay code calls into the UI.</summary>
    public static class UiHooks
    {
        /// <summary>
        /// Call from LocalPlayerController.TryOption before sending an affordance. Returns true when the UI took over
        /// (e.g. SendFakeEmail with IntParam -1 opens the template picker, which sends the request itself).
        /// </summary>
        public static bool TryHandleAffordance(Affordance o)
        {
            if (o == null) return false;
            if (o.Verb == ActionVerb.SendFakeEmail && o.IntParam < 0 && o.BlockedBy == FailureCode.None)
            {
                EmailPicker.Open(o.TargetId);
                return true;
            }
            return false;
        }
    }
}
