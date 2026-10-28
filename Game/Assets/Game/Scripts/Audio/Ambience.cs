using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Office bed: air-conditioning / mains hum + distant keyboards (two 2D loops). State-driven:
    /// ducks during the HR meeting and results, the hum winds down and stops during a power cut (the keyboards go
    /// almost silent too), typing thins out while the internet is down. Two AudioSources, no allocations.
    /// </summary>
    public sealed class Ambience : MonoBehaviour
    {
        public float HumLevel = 0.35f;
        public float KeysLevel = 0.3f;
        public float MenuDuck = 0.45f;
        public float MeetingDuck = 0.3f;
        public float ResultsDuck = 0.2f;

        AudioSource _hum, _keys;
        float _humGain, _keysGain, _humPitch = 1f;

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
            var view = ClientState.I != null ? ClientState.I.View : null;
            float duck;
            if (view == null) duck = MenuDuck;
            else if (view.Phase >= MatchPhase.MeetingDiscussion && view.Phase <= MatchPhase.MeetingVerdict) duck = MeetingDuck;
            else if (view.Phase == MatchPhase.Results || view.Phase == MatchPhase.Abandoned) duck = ResultsDuck;
            else duck = 1f;
            bool power = view != null && view.Office != null && view.Office.PowerDown;
            bool net = view != null && view.Office != null && view.Office.InternetDown;

            float humTarget = power ? 0f : HumLevel * duck;
            float keysTarget = KeysLevel * duck * (power ? 0.1f : 1f) * (net ? 0.5f : 1f);
            float dt = Time.unscaledDeltaTime;
            // Power cut: the hum sags in pitch while it fades ("hum stop"); it spins back up on restore.
            _humPitch = Mathf.MoveTowards(_humPitch, power ? 0.55f : 1f, dt * (power ? 0.6f : 1.2f));
            _humGain = Mathf.MoveTowards(_humGain, humTarget, dt * (power ? 0.35f : 0.5f));
            _keysGain = Mathf.MoveTowards(_keysGain, keysTarget, dt * 0.6f);
            float master = Sfx.AmbienceVolume;
            _hum.volume = _humGain * master;
            _hum.pitch = _humPitch;
            _keys.volume = _keysGain * master;
        }
    }
}
