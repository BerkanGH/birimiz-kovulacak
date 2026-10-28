using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Procedural sound kit (no audio assets): every clip is synthesised once at startup by <see cref="SfxSynth"/>
    /// (mono 22.05 kHz, ≈ 4 MB total). Playback:
    /// <list type="bullet">
    /// <item><see cref="Play"/> — 2D one-shot (UI blips).</item>
    /// <item><see cref="PlayAt"/> — pooled 3D one-shot (12 voices, steal-oldest) muffled by a low-pass when a wall is
    /// between the listener (avatar head) and the source.</item>
    /// <item><see cref="Loop"/> / <see cref="LoopAt"/> — keyed looping sources (stations, ringtones, alarms); 3D loops
    /// get the same wall muffling, re-tested round-robin (2 linecasts per frame at most).</item>
    /// </list>
    /// Volume categories: master (<see cref="Volume"/>, AudioListener), effects (<see cref="EffectsVolume"/>) and
    /// ambience (<see cref="AmbienceVolume"/>, used by <see cref="Ambience"/>). Voice chat is not routed here.
    /// </summary>
    public sealed class Sfx : MonoBehaviour
    {
        static Sfx _i;
        public const int Rate = SfxSynth.Rate;
        const int PoolSize = 12;
        const float MuffledCutoff = 900f;
        const float MuffledGain = 0.6f;

        sealed class LoopEntry
        {
            public AudioSource Src;
            public AudioLowPassFilter Lp;
            public bool Spatial;
            public float Volume = 1f;
            public bool Occluded;
        }

        readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>(96);
        readonly Dictionary<string, LoopEntry> _loops = new Dictionary<string, LoopEntry>(32);
        readonly List<LoopEntry> _loopList = new List<LoopEntry>(32);
        AudioSource _oneShot;
        AudioSource[] _pool;
        AudioLowPassFilter[] _poolLp;
        int _next;
        int _occCursor;

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
            BuildPool();
            Debug.Log("[Sfx] " + _clips.Count + " clips synthesised in " + ((Time.realtimeSinceStartup - t0) * 1000f).ToString("0") + " ms");
            if (GetComponent<Ambience>() == null) gameObject.AddComponent<Ambience>();
            if (GetComponent<Footsteps>() == null) gameObject.AddComponent<Footsteps>();
            if (GetComponent<FeelDirector>() == null) gameObject.AddComponent<FeelDirector>();
        }

        void BuildPool()
        {
            _pool = new AudioSource[PoolSize];
            _poolLp = new AudioLowPassFilter[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject("Sfx3D_" + i);
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                Configure3D(src, 1f);
                var lp = go.AddComponent<AudioLowPassFilter>();
                lp.cutoffFrequency = MuffledCutoff;
                lp.enabled = false;
                _pool[i] = src;
                _poolLp[i] = lp;
            }
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

        /// <summary>2D one-shot. "manager_hm" is routed to the current manager profile's voice at the manager.</summary>
        public static void Play(string name, float volume = 1f)
        {
            if (_i == null) return;
            if (name == "manager_hm") { ManagerVoice(volume); return; }
            AudioClip c;
            if (_i._clips.TryGetValue(name, out c)) _i._oneShot.PlayOneShot(c, volume * EffectsVolume);
        }

        /// <summary>Pooled positional one-shot. spatialBlend 1 = fully 3D, 0 = 2D (still pitchable).</summary>
        public static void PlayAt(string name, Vector3 position, float volume = 1f, float pitch = 1f, float spatialBlend = 1f)
        {
            if (_i == null || _i._pool == null) return;
            AudioClip c;
            if (!_i._clips.TryGetValue(name, out c)) return;
            int idx = _i.FreeVoice();
            var src = _i._pool[idx];
            var lp = _i._poolLp[idx];
            src.transform.position = position;
            src.spatialBlend = spatialBlend;
            bool occluded = spatialBlend > 0.01f && Occluded(position);
            lp.enabled = occluded;
            src.clip = c;
            src.pitch = pitch;
            src.volume = volume * EffectsVolume * (occluded ? MuffledGain : 1f);
            src.Play();
        }

        /// <summary>Pitch-varied 2D one-shot (footsteps, ticks).</summary>
        public static void PlayVaried(string name, float volume, float pitchJitter)
        {
            PlayAt(name, Vector3.zero, volume, 1f + Random.Range(-pitchJitter, pitchJitter), 0f);
        }

        int FreeVoice()
        {
            for (int k = 0; k < PoolSize; k++)
            {
                int i = (_next + k) % PoolSize;
                if (!_pool[i].isPlaying) { _next = (i + 1) % PoolSize; return i; }
            }
            int steal = _next;
            _next = (_next + 1) % PoolSize;
            return steal;
        }

        /// <summary>True when an opaque wall separates the listener (avatar head) from <paramref name="position"/>.</summary>
        public static bool Occluded(Vector3 position)
        {
            Vector3 l = CameraRig.ListenerPosition;
            if ((l - position).sqrMagnitude < 1f) return false;
            return Physics.Linecast(l, position, FeelLayers.SoundBlockers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>v1 API: keyed loop; position zero = 2D. clip null stops it.</summary>
        public static void Loop(string key, string clip, Vector3 position)
        {
            LoopAt(key, clip, position, 1f, position != Vector3.zero);
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
                e.Lp = go.AddComponent<AudioLowPassFilter>();
                e.Lp.cutoffFrequency = MuffledCutoff;
                e.Lp.enabled = false;
                _i._loops[key] = e;
                _i._loopList.Add(e);
            }
            if (e.Spatial != spatial || e.Src.clip == null)
            {
                e.Spatial = spatial;
                Configure3D(e.Src, spatial ? 0.9f : 0f);
                e.Src.minDistance = 2.5f;
                e.Src.maxDistance = 25f;
                if (!spatial) { e.Occluded = false; e.Lp.enabled = false; }
            }
            e.Src.transform.position = position;
            e.Volume = volume;
            e.Src.pitch = pitch;
            e.Src.volume = volume * EffectsVolume * (e.Occluded ? MuffledGain : 1f);
            if (e.Src.clip != c) { e.Src.clip = c; e.Src.Play(); }
            else if (!e.Src.isPlaying) e.Src.Play();
        }

        public static void StopLoop(string key) { LoopAt(key, null, Vector3.zero); }

        public static bool IsLooping(string key)
        {
            LoopEntry e;
            return _i != null && key != null && _i._loops.TryGetValue(key, out e) && e.Src.isPlaying;
        }

        void Update()
        {
            int n = _loopList.Count;
            if (n == 0) return;
            for (int k = 0; k < 2 && k < n; k++)
            {
                _occCursor = (_occCursor + 1) % n;
                var e = _loopList[_occCursor];
                if (!e.Spatial || !e.Src.isPlaying) continue;
                bool occ = Occluded(e.Src.transform.position);
                if (occ == e.Occluded) continue;
                e.Occluded = occ;
                e.Lp.enabled = occ;
                e.Src.volume = e.Volume * EffectsVolume * (occ ? MuffledGain : 1f);
            }
        }

        // ------------------------------------------------------------------ manager voice

        static string VoiceClip(string profile)
        {
            switch (profile)
            {
                case ManagerProfileIds.Paragoz: return "mgr_hmm_paragoz";
                case ManagerProfileIds.Disiplinci: return "mgr_hmm_disiplinci";
                case ManagerProfileIds.Kayirmaci: return "mgr_hmm_kayirmaci";
                case ManagerProfileIds.Paranoyak: return "mgr_hmm_paranoyak";
                default: return "mgr_hmm";
            }
        }

        /// <summary>The manager's profile-specific grumble, half-spatialised at his position (always audible).</summary>
        public static void ManagerVoice(float volume = 1f)
        {
            var view = ClientState.I != null ? ClientState.I.View : null;
            string clip = VoiceClip(view != null ? view.ManagerProfileId : "");
            var mgr = OfficeWorld.I != null ? OfficeWorld.I.Manager : null;
            if (mgr != null) PlayAt(clip, mgr.transform.position + Vector3.up * 1.6f, volume, 1f, 0.5f);
            else PlayAt(clip, Vector3.zero, volume, 1f, 0f);
        }

        // ------------------------------------------------------------------ synthesis

        void Build()
        {
            // v1 kit
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
            Add("phone_ring", SfxSynth.PhoneRing());
            Add("printer", SfxSynth.Printer());
            Add("coffee", SfxSynth.Coffee());
            Add("siren", SfxSynth.Siren());
            // v2: calls
            Add("ringtone", SfxSynth.Ringtone());
            Add("ringback", SfxSynth.Ringback());
            Add("call_connect", SfxSynth.CallConnect());
            Add("call_end", SfxSynth.CallEnd());
            Add("tip_dial", SfxSynth.Dial());
            Add("email_ding", SfxSynth.EmailDing());
            // v2: sabotage, outages, counters
            Add("spark", SfxSynth.Spark());
            Add("plug_in", SfxSynth.PlugIn());
            Add("internet_down", SfxSynth.InternetDown());
            Add("power_down", SfxSynth.PowerDown());
            Add("power_up", SfxSynth.PowerUp());
            Add("router_restore", SfxSynth.RouterRestore());
            Add("salt_shake", SfxSynth.SaltShake());
            Add("slurp", SfxSynth.Slurp());
            Add("bleh", SfxSynth.Bleh());
            Add("mail_drop", SfxSynth.MailDrop());
            Add("splash", SfxSynth.Splash());
            Add("dust", SfxSynth.Dust());
            // v2: bodies
            for (int i = 0; i < 3; i++) Add(Footsteps.StepClips[i], SfxSynth.Step(i));
            for (int i = 0; i < 2; i++) Add(Footsteps.ManagerStepClips[i], SfxSynth.ManagerStep(i));
            Add("mgr_hmm", SfxSynth.ManagerHum(""));
            Add("mgr_hmm_paragoz", SfxSynth.ManagerHum(ManagerProfileIds.Paragoz));
            Add("mgr_hmm_disiplinci", SfxSynth.ManagerHum(ManagerProfileIds.Disiplinci));
            Add("mgr_hmm_kayirmaci", SfxSynth.ManagerHum(ManagerProfileIds.Kayirmaci));
            Add("mgr_hmm_paranoyak", SfxSynth.ManagerHum(ManagerProfileIds.Paranoyak));
            // v2: stings, meeting, results
            Add("inspection_sting", SfxSynth.InspectionSting());
            Add("caught", SfxSynth.Caught());
            Add("vhs", SfxSynth.Vhs());
            Add("gavel", SfxSynth.Gavel());
            Add("stamp", SfxSynth.Stamp());
            Add("confetti", SfxSynth.Confetti());
            // v2: small feedback
            Add("emote_pop", SfxSynth.EmotePop());
            Add("wheel_tick", SfxSynth.WheelTick());
            Add("crouch", SfxSynth.Rustle());
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
