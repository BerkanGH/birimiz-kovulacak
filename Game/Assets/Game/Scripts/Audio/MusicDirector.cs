using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Phase-driven soundtrack. Cue choice is the pure <see cref="MusicCuePlanner"/>; this component owns exactly two
    /// AudioSources (crossfade A/B), lazily loads clips from Resources/music/&lt;cue&gt; (missing clip = silence, never throws),
    /// and applies volume = <see cref="MusicSettings.Effective"/> x smoothed duck x per-source fade.
    /// Ducking uses existing state only (tape reveal, connected phone call, speaking flags); there is no microphone analysis.
    /// </summary>
    public sealed class MusicDirector : MonoBehaviour
    {
        sealed class Slot
        {
            public AudioSource Src;
            public string Cue;
            public float Fade;        // current 0..1
            public float FadeTarget;  // 0 or 1
            public float FadeTime = 1f;
            public bool Loop;
            public bool Started;
            public bool Finished;     // one-shot ran to its end
        }

        const int MaxSlots = 6; // VoiceSystem.Slots upper bound for the speaking scan

        readonly Slot[] _slots = new Slot[2];
        readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>(8);
        string _cue;            // current planned cue (null = silence)
        bool _hasCue;           // false until the first plan is applied
        int _weekId = int.MinValue;
        float _duck = 1f, _duckTarget = 1f;
        public string CurrentCue { get { return _cue; } }
        public float CurrentDuck { get { return _duck; } }

        void Awake()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                var go = new GameObject("Music_" + (char)('A' + i));
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                s.priority = 64;
                s.volume = 0f;
                _slots[i] = new Slot { Src = s };
            }
        }

        void Update()
        {
            var lc = LineClient.I;
            var view = lc != null ? lc.View : null;
            var st = new MusicState();
            if (view != null)
            {
                st.HasView = true;
                st.Phase = view.Phase;
                st.WeekId = view.WeekId;
                st.PhaseLength = view.PhaseEndTime - view.PhaseStartTime;
                st.PhaseRemaining = lc.PhaseRemaining;
                st.CompanyClosed = view.Result != null && view.Result.CompanyClosed;
                st.PhoneConnected = view.Mine != null && view.Mine.Phone != null && view.Mine.Phone.Status == CallStatus.Connected;
                var vs = VoiceSystem.Instance;
                if (vs != null)
                {
                    st.RecordingActive = vs.RecordingActive;
                    var players = view.Players;
                    for (int i = 0; i < players.Count && i < MaxSlots; i++)
                        if (players[i] != null && vs.IsSpeaking(players[i].Id)) { st.AnySpeaking = true; break; }
                }
            }

            // Rematch / new match: drop whatever is playing so nothing from the previous week lingers.
            if (view != null && view.WeekId != _weekId)
            {
                if (_weekId != int.MinValue) ResetAll();
                _weekId = view.WeekId;
            }
            else if (view == null && _weekId != int.MinValue) _weekId = int.MinValue;

            var plan = MusicCuePlanner.Plan(st);
            if (!_hasCue || plan.Cue != _cue) SwitchTo(plan);
            _duckTarget = plan.Duck;

            float dt = Time.unscaledDeltaTime;
            // fast attack when ducking (speech must stay intelligible), slow release
            float rate = _duckTarget < _duck ? 1f / 0.15f : 1f / 1.2f;
            _duck = Mathf.MoveTowards(_duck, _duckTarget, rate * dt);

            float master = MusicSettings.Effective * _duck;
            for (int i = 0; i < _slots.Length; i++) Tick(_slots[i], master, dt);
        }

        void Tick(Slot s, float master, float dt)
        {
            if (s.Cue == null) return;
            if (!Mathf.Approximately(s.Fade, s.FadeTarget))
                s.Fade = Mathf.MoveTowards(s.Fade, s.FadeTarget, dt / Mathf.Max(0.05f, s.FadeTime));
            if (s.FadeTarget <= 0f && s.Fade <= 0.001f) { Release(s); return; }
            s.Src.volume = master * s.Fade;
            if (!s.Loop && s.Started && !s.Finished && !s.Src.isPlaying) { s.Finished = true; }
        }

        void SwitchTo(MusicPlan plan)
        {
            _hasCue = true;
            _cue = plan.Cue;
            bool ducked = plan.Duck < 0.95f;
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i].Cue != null) { _slots[i].FadeTarget = 0f; _slots[i].FadeTime = Mathf.Min(plan.FadeIn, 1.5f); }
            if (plan.Cue != null)
            {
                var clip = GetClip(plan.Cue);
                if (clip != null) Begin(PickSlot(), plan, clip);
            }
            TelemetryHooks.MusicCueChanged(plan.Cue ?? "none", ducked);
        }

        // Use a free slot; if both are busy (rapid switches) reclaim the quieter one.
        Slot PickSlot()
        {
            Slot best = _slots[0];
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].Cue == null) return _slots[i];
                if (_slots[i].Fade < best.Fade) best = _slots[i];
            }
            Release(best);
            return best;
        }

        void Begin(Slot s, MusicPlan plan, AudioClip clip)
        {
            s.Cue = plan.Cue;
            s.Loop = plan.Loop;
            s.Fade = 0f;
            s.FadeTarget = 1f;
            s.FadeTime = Mathf.Max(0.05f, plan.FadeIn);
            s.Started = true;
            s.Finished = false;
            s.Src.clip = clip;
            s.Src.loop = plan.Loop;
            s.Src.volume = 0f;
            s.Src.time = 0f;
            s.Src.Play();
        }

        static void Release(Slot s)
        {
            if (s.Src != null) { s.Src.Stop(); s.Src.clip = null; s.Src.volume = 0f; }
            s.Cue = null; s.Fade = 0f; s.FadeTarget = 0f; s.Started = false; s.Finished = false;
        }

        void ResetAll()
        {
            for (int i = 0; i < _slots.Length; i++) Release(_slots[i]);
            _hasCue = false;
            _cue = null;
            _duck = _duckTarget; // do not carry a stale duck into the new match
        }

        AudioClip GetClip(string cue)
        {
            AudioClip c;
            if (_clips.TryGetValue(cue, out c)) return c;
            try { c = Resources.Load<AudioClip>("music/" + cue); }
            catch (System.Exception) { c = null; }
            if (c == null) Debug.LogWarning("[Music] clip missing: music/" + cue + " (staying silent)");
            _clips[cue] = c; // cache misses too: no per-frame reloads
            return c;
        }

        void OnDestroy()
        {
            for (int i = 0; i < _slots.Length; i++) if (_slots[i] != null) Release(_slots[i]);
            _clips.Clear();
        }
    }
}
