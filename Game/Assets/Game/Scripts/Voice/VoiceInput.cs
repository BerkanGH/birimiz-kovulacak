using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Push-to-talk key (default V), built in code like <see cref="GameInput"/> but in its own action map so the
    /// voice module does not edit GameInput. Rebinding is persisted in PlayerPrefs ("voice.input.overrides").
    /// The settings screen can rebind with <see cref="StartRebind"/> and show <see cref="KeyLabel"/>.
    /// </summary>
    public static class VoiceInput
    {
        const string PrefsKey = "voice.input.overrides";
        static InputActionMap _map;

        public static InputAction PushToTalk { get; private set; }

        public static void Initialize()
        {
            if (_map != null) return;
            _map = new InputActionMap("Voice");
            PushToTalk = _map.AddAction("PushToTalk", InputActionType.Button, "<Keyboard>/v");
            string json = PlayerPrefs.GetString(PrefsKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                try { _map.LoadBindingOverridesFromJson(json); }
                catch (Exception e) { Debug.LogWarning("[Voice] Could not load PTT binding: " + e.Message); }
            }
            _map.Enable();
        }

        public static bool PushToTalkHeld => PushToTalk != null && PushToTalk.IsPressed();

        public static string KeyLabel => PushToTalk == null ? "V" : PushToTalk.GetBindingDisplayString(0).ToUpperInvariant();

        public static void StartRebind(Action onDone)
        {
            if (PushToTalk == null) return;
            PushToTalk.Disable();
            PushToTalk.PerformInteractiveRebinding(0)
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnComplete(op => { op.Dispose(); PushToTalk.Enable(); Save(); onDone?.Invoke(); })
                .OnCancel(op => { op.Dispose(); PushToTalk.Enable(); onDone?.Invoke(); })
                .Start();
        }

        public static void ResetBinding()
        {
            if (_map == null) return;
            _map.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(PrefsKey);
        }

        static void Save()
        {
            PlayerPrefs.SetString(PrefsKey, _map.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }
    }
}
