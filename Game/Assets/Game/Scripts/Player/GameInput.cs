using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Keyboard actions built in code (no asset dependency) with remappable bindings persisted in PlayerPrefs
    /// ("input.overrides"). Mouse look and clicks in the rooms are read directly (PovCameraRig / RoomsPresenter).
    ///
    /// Layout: E computer, Tab phone, Y answer call, N decline / hang up, V push-to-talk (read by the voice module
    /// only), Esc pause. Settings rebinds Tab, V, Y and N.
    /// </summary>
    public static class GameInput
    {
        const string PrefsKey = "input.overrides";
        static InputActionAsset _asset;
        public static InputActionMap Gameplay { get; private set; }

        public static InputAction Interact, Phone, Pause, AnswerCall, HangUp, PushToTalk;

        public static void Initialize()
        {
            if (_asset != null) return;
            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            Gameplay = _asset.AddActionMap("Gameplay");

            Interact = Gameplay.AddAction("Interact", InputActionType.Button, "<Keyboard>/e");
            Phone = Gameplay.AddAction("Phone", InputActionType.Button, "<Keyboard>/tab");
            Pause = Gameplay.AddAction("Pause", InputActionType.Button, "<Keyboard>/escape");
            AnswerCall = Gameplay.AddAction("AnswerCall", InputActionType.Button, "<Keyboard>/y");
            HangUp = Gameplay.AddAction("HangUp", InputActionType.Button, "<Keyboard>/n");
            // Owned by the voice module: nothing else may read it or bind V.
            PushToTalk = Gameplay.AddAction("PushToTalk", InputActionType.Button, "<Keyboard>/v");

            LoadOverrides();
            Gameplay.Enable();
        }

        public static string DisplayName(InputAction action, int bindingIndex)
        {
            return action.GetBindingDisplayString(bindingIndex, InputBinding.DisplayStringOptions.DontIncludeInteractions);
        }

        public static string KeyLabel(InputAction action)
        {
            if (action == null) return "?";
            return action.GetBindingDisplayString(0).ToUpperInvariant();
        }

        public static void StartRebind(InputAction action, int bindingIndex, Action onDone)
        {
            action.Disable();
            action.PerformInteractiveRebinding(bindingIndex)
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnComplete(op => { op.Dispose(); action.Enable(); SaveOverrides(); onDone?.Invoke(); })
                .OnCancel(op => { op.Dispose(); action.Enable(); onDone?.Invoke(); })
                .Start();
        }

        public static void ResetBindings()
        {
            _asset.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(PrefsKey);
        }

        static void SaveOverrides()
        {
            PlayerPrefs.SetString(PrefsKey, _asset.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }

        static void LoadOverrides()
        {
            string json = PlayerPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(json)) return;
            try { _asset.LoadBindingOverridesFromJson(json); }
            catch (Exception e) { Debug.LogWarning("[Input] Could not load overrides: " + e.Message); }
        }
    }
}
