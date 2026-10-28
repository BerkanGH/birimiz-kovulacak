using System;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Push-to-talk = <see cref="GameInput.PushToTalk"/> (V by default, remappable and persisted by GameInput,
    /// listed in the settings screen as "input.push_to_talk"). Ignored while a UI text field has focus so typing a
    /// "v" in a name field does not open the mic.
    /// </summary>
    public static class VoiceInput
    {
        public static bool PushToTalkHeld
        {
            get
            {
                var a = GameInput.PushToTalk;
                return a != null && a.IsPressed() && !FeelHooks.TextInputFocused;
            }
        }

        public static string KeyLabel => GameInput.PushToTalk != null ? GameInput.KeyLabel(GameInput.PushToTalk) : "V";

        /// <summary>Interactive rebind of the push-to-talk key (saved with the other GameInput overrides).</summary>
        public static void StartRebind(Action onDone)
        {
            if (GameInput.PushToTalk != null) GameInput.StartRebind(GameInput.PushToTalk, 0, onDone);
        }
    }
}
