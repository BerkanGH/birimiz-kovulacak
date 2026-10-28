using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Keyboard + mouse actions built in code (no asset dependency) with remappable bindings persisted in
    /// PlayerPrefs ("input.overrides").
    ///
    /// Default layout (v2): WASD move, mouse look, wheel zoom, E/F interact, Q drop, R work pose, G use held item
    /// (taste coffee), Left Ctrl hold / C toggle crouch (sneak), T hold = emote wheel, 1-5 quick emotes,
    /// Y answer call, N decline / hang up, Tab phone, V push-to-talk (read by the voice module only), Esc pause.
    /// </summary>
    public static class GameInput
    {
        const string PrefsKey = "input.overrides";
        static InputActionAsset _asset;
        public static InputActionMap Gameplay { get; private set; }

        public static InputAction Move, Look, Zoom, Interact, Secondary, Drop, WorkPose, Phone, Pause;
        // v2
        public static InputAction Crouch, CrouchToggle, EmoteWheel, AnswerCall, HangUp, UseItem, PushToTalk;
        /// <summary>Quick emotes 1..5 (index = LocalPlayerController.WheelEmotes index).</summary>
        public static readonly InputAction[] EmoteKeys = new InputAction[5];

        /// <summary>v1 name of the hold-to-sneak key (now crouch). Kept for existing callers (HUD hints).</summary>
        public static InputAction Slow => Crouch;

        /// <summary>Rebindable entries shown in Settings: (action, binding index, label key).</summary>
        public static readonly List<(InputAction action, int binding, string labelKey)> Rebindable = new List<(InputAction, int, string)>();

        public static void Initialize()
        {
            if (_asset != null) return;
            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            Gameplay = _asset.AddActionMap("Gameplay");

            Move = Gameplay.AddAction("Move", InputActionType.Value);
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            Look = Gameplay.AddAction("Look", InputActionType.Value, "<Mouse>/delta");
            Zoom = Gameplay.AddAction("Zoom", InputActionType.Value, "<Mouse>/scroll/y");
            Interact = Gameplay.AddAction("Interact", InputActionType.Button, "<Keyboard>/e");
            Secondary = Gameplay.AddAction("Secondary", InputActionType.Button, "<Keyboard>/f");
            Drop = Gameplay.AddAction("Drop", InputActionType.Button, "<Keyboard>/q");
            WorkPose = Gameplay.AddAction("WorkPose", InputActionType.Button, "<Keyboard>/r");
            Phone = Gameplay.AddAction("Phone", InputActionType.Button, "<Keyboard>/tab");
            Pause = Gameplay.AddAction("Pause", InputActionType.Button, "<Keyboard>/escape");

            Crouch = Gameplay.AddAction("Crouch", InputActionType.Button, "<Keyboard>/leftCtrl");
            CrouchToggle = Gameplay.AddAction("CrouchToggle", InputActionType.Button, "<Keyboard>/c");
            EmoteWheel = Gameplay.AddAction("EmoteWheel", InputActionType.Button, "<Keyboard>/t");
            for (int i = 0; i < EmoteKeys.Length; i++)
                EmoteKeys[i] = Gameplay.AddAction("Emote" + (i + 1), InputActionType.Button, "<Keyboard>/" + (i + 1));
            AnswerCall = Gameplay.AddAction("AnswerCall", InputActionType.Button, "<Keyboard>/y");
            HangUp = Gameplay.AddAction("HangUp", InputActionType.Button, "<Keyboard>/n");
            UseItem = Gameplay.AddAction("UseItem", InputActionType.Button, "<Keyboard>/g");
            // Owned by the voice module: nothing else may read it or bind V.
            PushToTalk = Gameplay.AddAction("PushToTalk", InputActionType.Button, "<Keyboard>/v");

            Rebindable.Clear();
            Rebindable.Add((Move, 1, "input.move_up"));
            Rebindable.Add((Move, 2, "input.move_down"));
            Rebindable.Add((Move, 3, "input.move_left"));
            Rebindable.Add((Move, 4, "input.move_right"));
            Rebindable.Add((Interact, 0, "input.interact"));
            Rebindable.Add((Secondary, 0, "input.secondary"));
            Rebindable.Add((Drop, 0, "input.drop"));
            Rebindable.Add((UseItem, 0, "input.use_item"));
            Rebindable.Add((WorkPose, 0, "input.work_pose"));
            Rebindable.Add((Crouch, 0, "input.crouch"));
            Rebindable.Add((CrouchToggle, 0, "input.crouch_toggle"));
            Rebindable.Add((EmoteWheel, 0, "input.emote"));
            Rebindable.Add((AnswerCall, 0, "input.answer_call"));
            Rebindable.Add((HangUp, 0, "input.hang_up"));
            Rebindable.Add((Phone, 0, "input.phone"));
            Rebindable.Add((PushToTalk, 0, "input.push_to_talk"));

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
