using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Office bed: air-conditioning / mains hum + distant keyboards (two 2D loops). State-driven from the match phase
    /// (<see cref="LineClient"/>): full in the office, ducked in the meeting room and at the week results, quietest
    /// in the menu / lobby. Two AudioSources, no allocations.
    /// </summary>
    public sealed class Ambience : MonoBehaviour
    {
        public float HumLevel = 0.35f;
        public float KeysLevel = 0.3f;
        public float MenuDuck = 0.45f;
        public float MeetingDuck = 0.3f;
        public float ResultsDuck = 0.2f;

        AudioSource _hum, _keys;
        float _humGain, _keysGain;

        void Start()
        {
            _hum = MakeSource("ambience_hum");
            _keys = MakeSource("ambience_keys");
        }

        AudioSource MakeSource(string clip)
        {
            var c = Sfx.Clip(clip);
            if (c == null) return null;
            var go = new GameObject("Ambience_" + clip);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.clip = c;
            s.loop = true;
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.volume = 0f;
            s.priority = 200;
            s.time = Random.Range(0f, c.length * 0.9f);
            s.Play();
            return s;
        }

        void Update()
        {
            if (_hum == null || _keys == null) return;
            var view = LineClient.I != null ? LineClient.I.View : null;
            var phase = view != null ? view.Phase : LinePhase.None;
            float duck;
            if (phase == LinePhase.None) duck = MenuDuck;
            else if (LineClient.IsMeetingPhase(phase)) duck = MeetingDuck;
            else if (phase == LinePhase.Results || phase == LinePhase.Abandoned) duck = ResultsDuck;
            else duck = 1f;

            float dt = Time.unscaledDeltaTime;
            _humGain = Mathf.MoveTowards(_humGain, HumLevel * duck, dt * 0.5f);
            _keysGain = Mathf.MoveTowards(_keysGain, KeysLevel * duck, dt * 0.6f);
            float master = Sfx.AmbienceVolume;
            _hum.volume = _humGain * master;
            _keys.volume = _keysGain * master;
        }
    }
}
