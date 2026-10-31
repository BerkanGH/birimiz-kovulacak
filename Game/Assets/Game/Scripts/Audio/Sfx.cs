using System.Collections.Generic;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Procedural sound kit (no audio assets): every clip is synthesised once at startup by <see cref="SfxSynth"/>
    /// (mono 22.05 kHz). Playback:
    /// <list type="bullet">
    /// <item><see cref="Play"/> — 2D one-shot (UI blips, stamps, call tones).</item>
    /// <item><see cref="LoopAt"/> / <see cref="StopLoop"/> — keyed looping sources (desk ringtone, ringback).</item>
    /// </list>
    /// Volume categories: master (<see cref="Volume"/>, AudioListener), effects (<see cref="EffectsVolume"/>) and
    /// ambience (<see cref="AmbienceVolume"/>, used by <see cref="Ambience"/>). Voice chat is not routed here.
    /// </summary>
    public sealed class Sfx : MonoBehaviour
    {
        static Sfx _i;
        public const int Rate = SfxSynth.Rate;

        sealed class LoopEntry
        {
            public AudioSource Src;
            public bool Spatial;
        }

        readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>(32);
        readonly Dictionary<string, LoopEntry> _loops = new Dictionary<string, LoopEntry>(8);
        AudioSource _oneShot;

        static float _effects = -1f, _ambience = -1f;

        public static float Volume
        {
            get { return PlayerPrefs.GetFloat("settings.volume", 0.7f); }
            set { PlayerPrefs.SetFloat("settings.volume", value); AudioListener.volume = LaunchArgs.Current.Mute ? 0 : value; }
        }

        /// <summary>Effects category (0..1), persisted as "settings.sfx_volume".</summary>
        public static float EffectsVolume
        {
            get { if (_effects < 0f) _effects = PlayerPrefs.GetFloat("settings.sfx_volume", 1f); return _effects; }
            set { _effects = Mathf.Clamp01(value); PlayerPrefs.SetFloat("settings.sfx_volume", _effects); }
        }

        /// <summary>Ambience category (0..1), persisted as "settings.ambience_volume".</summary>
        public static float AmbienceVolume
        {
            get { if (_ambience < 0f) _ambience = PlayerPrefs.GetFloat("settings.ambience_volume", 0.8f); return _ambience; }
            set { _ambience = Mathf.Clamp01(value); PlayerPrefs.SetFloat("settings.ambience_volume", _ambience); }
        }

        void Awake()
        {
            _i = this;
            _oneShot = gameObject.AddComponent<AudioSource>();
            _oneShot.playOnAwake = false;
            AudioListener.volume = LaunchArgs.Current.Mute ? 0f : Volume;
            if (Application.isBatchMode || LaunchArgs.Current.Bot) return; // headless: no clips, every call is a no-op
            var t0 = Time.realtimeSinceStartup;
            Build();
            Debug.Log("[Sfx] " + _clips.Count + " clips synthesised in " + ((Time.realtimeSinceStartup - t0) * 1000f).ToString("0") + " ms");
            if (GetComponent<Ambience>() == null) gameObject.AddComponent<Ambience>();
            if (GetComponent<MusicDirector>() == null) gameObject.AddComponent<MusicDirector>();
            if (GetComponent<FxPool>() == null) gameObject.AddComponent<FxPool>();
        }

        static void Configure3D(AudioSource src, float blend)
        {
            src.spatialBlend = blend;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 2f;
            src.maxDistance = 22f;
            src.dopplerLevel = 0f;
            src.spread = 0f;
        }

        // ------------------------------------------------------------------ playback API

        public static bool Has(string name) { return _i != null && name != null && _i._clips.ContainsKey(name); }

        public static AudioClip Clip(string name)
        {
            AudioClip c;
            return _i != null && name != null && _i._clips.TryGetValue(name, out c) ? c : null;
        }

        /// <summary>2D one-shot.</summary>
        public static void Play(string name, float volume = 1f)
        {
            if (_i == null) return;
            AudioClip c;
            if (_i._clips.TryGetValue(name, out c)) _i._oneShot.PlayOneShot(c, volume * EffectsVolume);
        }

        /// <summary>Starts/updates a keyed looping source (call every frame to follow a moving emitter). clip null stops.</summary>
        public static void LoopAt(string key, string clip, Vector3 position, float volume = 1f, bool spatial = true, float pitch = 1f)
        {
            if (_i == null || string.IsNullOrEmpty(key)) return;
            LoopEntry e;
            _i._loops.TryGetValue(key, out e);
            if (clip == null)
            {
                if (e != null && e.Src.isPlaying) e.Src.Stop();
                return;
            }
            AudioClip c;
            if (!_i._clips.TryGetValue(clip, out c)) return;
            if (e == null)
            {
                var go = new GameObject("Loop_" + key);
                go.transform.SetParent(_i.transform);
                e = new LoopEntry { Src = go.AddComponent<AudioSource>() };
                e.Src.loop = true;
                e.Src.playOnAwake = false;
                _i._loops[key] = e;
            }
            if (e.Spatial != spatial || e.Src.clip == null)
            {
                e.Spatial = spatial;
                Configure3D(e.Src, spatial ? 0.9f : 0f);
                e.Src.minDistance = 2.5f;
                e.Src.maxDistance = 25f;
            }
            e.Src.transform.position = position;
            e.Src.pitch = pitch;
            e.Src.volume = volume * EffectsVolume;
            if (e.Src.clip != c) { e.Src.clip = c; e.Src.Play(); }
            else if (!e.Src.isPlaying) e.Src.Play();
        }

        public static void StopLoop(string key) { LoopAt(key, null, Vector3.zero); }

        // ------------------------------------------------------------------ synthesis

        void Build()
        {
            // UI blips and stamps
            Add("click", SfxSynth.Tone(new[] { 880f }, 0.05f, 0.25f));
            Add("deny", SfxSynth.Tone(new[] { 220f, 180f }, 0.09f, 0.3f, SfxSynth.Wave.Square));
            Add("pickup", SfxSynth.Sweep(500f, 900f, 0.09f, 0.3f));
            Add("place", SfxSynth.Sweep(700f, 380f, 0.09f, 0.3f));
            Add("complete", SfxSynth.Tone(new[] { 659f, 880f, 1318f }, 0.08f, 0.35f));
            Add("sabotage", SfxSynth.Tone(new[] { 311f, 293f }, 0.12f, 0.3f, SfxSynth.Wave.Triangle));
            Add("evidence", SfxSynth.Tone(new[] { 196f, 185f, 147f }, 0.16f, 0.45f, SfxSynth.Wave.Square));
            Add("phase", SfxSynth.Tone(new[] { 523f, 659f, 784f }, 0.12f, 0.35f, SfxSynth.Wave.Triangle));
            Add("vote", SfxSynth.Tone(new[] { 988f }, 0.06f, 0.3f));
            Add("fired", SfxSynth.Trombone());
            Add("cheer", SfxSynth.Tone(new[] { 523f, 659f, 784f, 1046f, 784f, 1046f }, 0.09f, 0.35f, SfxSynth.Wave.Triangle));
            Add("toast", SfxSynth.Tone(new[] { 1174f }, 0.04f, 0.2f));
            Add("stamp", SfxSynth.Stamp());
            // desk phone and mail
            Add("ringtone", SfxSynth.Ringtone());
            Add("ringback", SfxSynth.Ringback());
            Add("call_connect", SfxSynth.CallConnect());
            Add("call_end", SfxSynth.CallEnd());
            Add("tip_dial", SfxSynth.Dial());
            Add("email_ding", SfxSynth.EmailDing());
            Add("mail_drop", SfxSynth.MailDrop());
            // tricks landing on your desk
            Add("internet_down", SfxSynth.InternetDown());
            Add("power_down", SfxSynth.PowerDown());
            Add("splash", SfxSynth.Splash());
            // ambience (looped by Ambience)
            Add("ambience_keys", SfxSynth.AmbienceKeys());
            Add("ambience_hum", SfxSynth.AmbienceHum());
        }

        void Add(string name, float[] data)
        {
            var c = AudioClip.Create(name, data.Length, 1, Rate, false);
            c.SetData(data, 0);
            _clips[name] = c;
        }
    }
}
