using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Fullscreen on first launch, then the player's choice (PlayerPrefs). Applied once at boot; Settings → Display
    /// toggles it. Launch args such as -screen-fullscreen 0 (automation) win for that process only.
    /// </summary>
    public static class DisplaySettings
    {
        public const string FullscreenKey = "display.fullscreen";

        /// <summary>True when the player has never chosen (first launch).</summary>
        public static bool Unset => !PlayerPrefs.HasKey(FullscreenKey);

        public static bool Fullscreen
        {
            get { return PlayerPrefs.GetInt(FullscreenKey, 1) == 1; }
            set
            {
                PlayerPrefs.SetInt(FullscreenKey, value ? 1 : 0);
                PlayerPrefs.Save();
                Apply();
            }
        }

        public static void Apply()
        {
            if (Application.isBatchMode || Application.isEditor) return;
            bool want = Fullscreen;
            if (want)
            {
                var r = Screen.currentResolution;
                Screen.SetResolution(r.width, r.height, FullScreenMode.FullScreenWindow);
            }
            else if (Screen.fullScreenMode != FullScreenMode.Windowed)
            {
                Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
            }
        }

        /// <summary>Boot: honour an explicit -screen-fullscreen argument, otherwise the stored (or first-launch) choice.</summary>
        public static void ApplyAtBoot()
        {
            foreach (var a in System.Environment.GetCommandLineArgs())
                if (a == "-screen-fullscreen" || a == "-popupwindow" || a == "-batchmode") return;
            Apply();
        }
    }
}
