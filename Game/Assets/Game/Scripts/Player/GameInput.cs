using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Keyboard + mouse actions built in code (no asset dependency) with remappable bindings persisted in
    /// PlayerPrefs ("input.overrides").
    /// </summary>
    public static class GameInput
    {
        const string PrefsKey = "input.overrides";
        static InputActionAsset _asset;
        public static InputActionMap Gameplay { get; private set; }

        public static InputAction Move, Look, Zoom, Interact, Secondary, Drop, WorkPose, Slow, Phone, Pause;

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
            Slow = Gameplay.AddAction("Slow", InputActionType.Button, "<Keyboard>/leftCtrl");
            Phone = Gameplay.AddAction("Phone", InputActionType.Button, "<Keyboard>/tab");
            Pause = Gameplay.AddAction("Pause", InputActionType.Button, "<Keyboard>/escape");

            Rebindable.Clear();
            Rebindable.Add((Move, 1, "input.move_up"));
            Rebindable.Add((Move, 2, "input.move_down"));
            Rebindable.Add((Move, 3, "input.move_left"));
            Rebindable.Add((Move, 4, "input.move_right"));
            Rebindable.Add((Interact, 0, "input.interact"));
            Rebindable.Add((Secondary, 0, "input.secondary"));
            Rebindable.Add((Drop, 0, "input.drop"));
            Rebindable.Add((WorkPose, 0, "input.work_pose"));
            Rebindable.Add((Slow, 0, "input.slow"));
            Rebindable.Add((Phone, 0, "input.phone"));

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
