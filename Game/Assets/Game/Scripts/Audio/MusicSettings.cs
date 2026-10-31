using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>Music volume and mute (independent of master/effects/ambience/voice). Persisted in PlayerPrefs.</summary>
    public static class MusicSettings
    {
        public const string VolumeKey = "settings.music_volume";
        public const string MuteKey = "settings.music_mute";

        public static float Volume
        {
            get { return Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 0.6f)); }
            set { PlayerPrefs.SetFloat(VolumeKey, Mathf.Clamp01(value)); }
        }

        public static bool Muted
        {
            get { return PlayerPrefs.GetInt(MuteKey, 0) == 1; }
            set { PlayerPrefs.SetInt(MuteKey, value ? 1 : 0); }
        }

        /// <summary>Volume after mute, before master volume (AudioListener) and ducking.</summary>
        public static float Effective => Muted ? 0f : Volume;
    }
}
