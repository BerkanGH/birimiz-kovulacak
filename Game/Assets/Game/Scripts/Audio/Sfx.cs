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

        /// <summary>A one-shot from a point in the room (a prank at a colleague's seat); flat when <paramref name="at"/> is null.</summary>
        public static void PlayAt(string name, Vector3? at, float volume = 1f, float pitch = 1f)
        {
            if (_i == null || name == null) return;
            AudioClip c;
            if (!_i._clips.TryGetValue(name, out c)) return;
            if (!at.HasValue) { _i._oneShot.PlayOneShot(c, volume * EffectsVolume); return; }
            var go = new GameObject("Sfx3D_" + name);
            go.transform.SetParent(_i.transform);
            go.transform.position = at.Value;
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            Configure3D(src, 0.85f);
            src.minDistance = 2.5f;
            src.maxDistance = 25f;
            src.pitch = pitch;
            src.volume = volume * EffectsVolume;
            src.clip = c;
            src.Play();
            Destroy(go, c.length / Mathf.Max(0.1f, pitch) + 0.2f);
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

        // ------------------------------------------------------------------ v5 bot talk: the bots' mumble

        readonly AudioSource[] _voices = new AudioSource[4];
        readonly float[] _voiceStop = new float[4];
        readonly int[] _voiceOwner = { -1, -1, -1, -1 };
        int _nextVoice;

        /// <summary>
        /// A bot says a line: <paramref name="seconds"/> of babble at <paramref name="pitch"/> (its own voice), flat on the
        /// phone or from its seat in the meeting room (<paramref name="at"/>). A new line from the same bot replaces its last.
        /// </summary>
        public static void Mumble(float seconds, float pitch, int owner, Vector3? at)
        {
            if (_i == null) return;
            AudioClip c;
            if (!_i._clips.TryGetValue("mumble_" + ((owner % 4 + 4) % 4), out c)) return;
            int slot = -1;
            for (int i = 0; i < _i._voices.Length; i++) if (_i._voiceOwner[i] == owner && _i._voices[i] != null && _i._voices[i].isPlaying) slot = i;
            if (slot < 0) { slot = _i._nextVoice; _i._nextVoice = (_i._nextVoice + 1) % _i._voices.Length; }
            var src = _i._voices[slot];
            if (src == null)
            {
                var go = new GameObject("Voice_" + slot);
                go.transform.SetParent(_i.transform);
                src = _i._voices[slot] = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
            }
            Configure3D(src, at.HasValue ? 0.8f : 0f);
            if (at.HasValue) src.transform.position = at.Value;
            src.pitch = pitch;
            src.volume = 0.5f * EffectsVolume;
            src.clip = c;
            src.time = 0f;
            src.Play();
            _i._voiceOwner[slot] = owner;
            _i._voiceStop[slot] = Time.unscaledTime + Mathf.Min(seconds, c.length / Mathf.Max(0.5f, pitch) - 0.05f);
        }

        void Update()
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < _voices.Length; i++)
            {
                var src = _voices[i];
                if (src == null || !src.isPlaying) continue;
                // a short fade at the end of the line instead of a click
                float left = _voiceStop[i] - now;
                if (left <= 0f) { src.Stop(); _voiceOwner[i] = -1; }
                else if (left < 0.12f) src.volume = 0.5f * EffectsVolume * (left / 0.12f);
            }
        }

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
            Add("shred", SfxSynth.Shredder()); // v5.3: a file goes through the shredder
            // desk phone and mail
            Add("ringtone", SfxSynth.Ringtone());
            Add("ringback", SfxSynth.Ringback());
            Add("call_connect", SfxSynth.CallConnect());
            Add("call_end", SfxSynth.CallEnd());
            Add("tip_dial", SfxSynth.Dial());
            Add("email_ding", SfxSynth.EmailDing());
            Add("mail_drop", SfxSynth.MailDrop());
            // v5 office: footsteps, the cassette recorder, busy line, handset lifted
            Add("step_a", SfxSynth.Step(3));
            Add("step_b", SfxSynth.Step(9));
            Add("rec_clunk", SfxSynth.RecClunk());
            Add("busy", SfxSynth.Busy());
            Add("offhook", SfxSynth.OffHook());
            // v5 bot talk: four babble takes (each bot plays one at its own pitch)
            for (int m = 0; m < 4; m++) Add("mumble_" + m, SfxSynth.Mumble(m));
            // v5 step 2: the Petty Cash box, the roster pencil, the drawer
            Add("lid_open", SfxSynth.Lid(true));
            Add("lid_close", SfxSynth.Lid(false));
            Add("box_drop", SfxSynth.BoxDrop());
            Add("bills", SfxSynth.Bills());
            Add("alarm_btn", SfxSynth.AlarmButton());
            Add("pencil", SfxSynth.Pencil());
            Add("eraser", SfxSynth.Eraser());
            Add("drawer_slide", SfxSynth.DrawerSlide());
            Add("drawer_thunk", SfxSynth.DrawerThunk());
            // tricks landing on your desk
            Add("internet_down", SfxSynth.InternetDown());
            Add("power_down", SfxSynth.PowerDown());
            Add("splash", SfxSynth.Splash());
            // v5 Yaramazlıklar
            Add("prank_ring", SfxSynth.PrankRing());
            Add("prank_slurp", SfxSynth.Slurp());
            Add("prank_slam", SfxSynth.TableSlam());
            Add("prank_splat", SfxSynth.Splat());
            Add("prank_whoosh", SfxSynth.Whoosh());
            Add("prank_paper", SfxSynth.PaperFlutter());
            Add("prank_slap", SfxSynth.Slap());
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
