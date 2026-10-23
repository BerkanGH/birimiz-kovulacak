using System;
using System.Collections.Generic;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Tiny procedural sound kit (no audio assets needed): UI blips, phone ring, printer, coffee machine,
    /// raid siren, manager "hm!", and the sad trombone for the firing.
    /// </summary>
    public sealed class Sfx : MonoBehaviour
    {
        static Sfx _i;
        const int Rate = 44100;
        readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, AudioSource> _loops = new Dictionary<string, AudioSource>();
        AudioSource _oneShot;

        public static float Volume
        {
            get { return PlayerPrefs.GetFloat("settings.volume", 0.7f); }
            set { PlayerPrefs.SetFloat("settings.volume", value); AudioListener.volume = LaunchArgs.Current.Mute ? 0 : value; }
        }

        void Awake()
        {
            _i = this;
            _oneShot = gameObject.AddComponent<AudioSource>();
            _oneShot.playOnAwake = false;
            AudioListener.volume = LaunchArgs.Current.Mute ? 0f : Volume;
            Build();
        }

        public static void Play(string name, float volume = 1f)
        {
            if (_i == null) return;
            AudioClip c;
            if (_i._clips.TryGetValue(name, out c)) _i._oneShot.PlayOneShot(c, volume);
        }

        /// <summary>Starts/stops a named looping source at a position (clip null = stop).</summary>
        public static void Loop(string key, string clip, Vector3 position)
        {
            if (_i == null || string.IsNullOrEmpty(key)) return;
            AudioSource src;
            _i._loops.TryGetValue(key, out src);
            if (clip == null)
            {
                if (src != null && src.isPlaying) src.Stop();
                return;
            }
            if (src == null)
            {
                var go = new GameObject("Loop_" + key);
                go.transform.SetParent(_i.transform);
                src = go.AddComponent<AudioSource>();
                src.loop = true;
                src.spatialBlend = position == Vector3.zero ? 0f : 0.85f;
                src.minDistance = 3f;
                src.maxDistance = 30f;
                src.rolloffMode = AudioRolloffMode.Linear;
                _i._loops[key] = src;
            }
            src.transform.position = position;
            AudioClip c;
            if (!_i._clips.TryGetValue(clip, out c)) return;
            if (src.clip != c) { src.clip = c; src.Play(); }
            else if (!src.isPlaying) src.Play();
        }

        // ------------------------------------------------------------------ synthesis

        void Build()
        {
            Add("click", Tone(new[] { 880f }, 0.05f, 0.25f));
            Add("deny", Tone(new[] { 220f, 180f }, 0.09f, 0.3f, Wave.Square));
            Add("pickup", Sweep(500f, 900f, 0.09f, 0.3f));
            Add("place", Sweep(700f, 380f, 0.09f, 0.3f));
            Add("complete", Tone(new[] { 659f, 880f, 1318f }, 0.08f, 0.35f));
            Add("sabotage", Tone(new[] { 311f, 293f }, 0.12f, 0.3f, Wave.Triangle));
            Add("evidence", Tone(new[] { 196f, 185f, 147f }, 0.16f, 0.45f, Wave.Square));
            Add("phase", Tone(new[] { 523f, 659f, 784f }, 0.12f, 0.35f, Wave.Triangle));
            Add("vote", Tone(new[] { 988f }, 0.06f, 0.3f));
            Add("manager_hm", Sweep(180f, 120f, 0.35f, 0.5f, Wave.Square));
            Add("fired", Trombone());
            Add("cheer", Tone(new[] { 523f, 659f, 784f, 1046f, 784f, 1046f }, 0.09f, 0.35f, Wave.Triangle));
            Add("toast", Tone(new[] { 1174f }, 0.04f, 0.2f));
            Add("phone_ring", PhoneRing());
            Add("printer", Printer());
            Add("coffee", Coffee());
            Add("siren", Siren());
        }

        void Add(string name, float[] data)
        {
            var c = AudioClip.Create(name, data.Length, 1, Rate, false);
            c.SetData(data, 0);
            _clips[name] = c;
        }

        enum Wave { Sine, Square, Triangle }

        static float Osc(Wave w, double phase)
        {
            double x = phase - Math.Floor(phase);
            switch (w)
            {
                case Wave.Square: return x < 0.5 ? 0.6f : -0.6f;
                case Wave.Triangle: return (float)(4.0 * Math.Abs(x - 0.5) - 1.0);
                default: return (float)Math.Sin(2 * Math.PI * x);
            }
        }

        static float[] Tone(float[] notes, float noteDur, float vol, Wave w = Wave.Sine)
        {
            int per = (int)(noteDur * Rate);
            var d = new float[per * notes.Length];
            double ph = 0;
            for (int n = 0; n < notes.Length; n++)
                for (int i = 0; i < per; i++)
                {
                    ph += notes[n] / Rate;
                    float env = Mathf.Min(1f, i / (Rate * 0.005f)) * (1f - i / (float)per);
                    d[n * per + i] = Osc(w, ph) * env * vol;
                }
            return d;
        }

        static float[] Sweep(float f0, float f1, float dur, float vol, Wave w = Wave.Sine)
        {
            int len = (int)(dur * Rate);
            var d = new float[len];
            double ph = 0;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)len;
                ph += Mathf.Lerp(f0, f1, t) / Rate;
                d[i] = Osc(w, ph) * (1f - t) * Mathf.Min(1f, i / (Rate * 0.004f)) * vol;
            }
            return d;
        }

        static float[] Trombone()
        {
            float[] notes = { 294f, 277f, 262f, 247f };
            float[] durs = { 0.38f, 0.38f, 0.38f, 1.2f };
            var list = new List<float>();
            double ph = 0;
            for (int n = 0; n < notes.Length; n++)
            {
                int len = (int)(durs[n] * Rate);
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)len;
                    float vib = n == 3 ? Mathf.Sin(i / (float)Rate * 2f * Mathf.PI * 6f) * 6f : 0f;
                    ph += (notes[n] + vib) / Rate;
                    float x = (float)(ph - Math.Floor(ph));
                    float saw = (2f * x - 1f) * 0.5f + Osc(Wave.Sine, ph) * 0.5f;
                    float env = Mathf.Min(1f, i / (Rate * 0.02f)) * (n == 3 ? (1f - t) : (1f - t * 0.3f));
                    list.Add(saw * env * 0.32f);
                }
            }
            return list.ToArray();
        }

        static float[] PhoneRing()
        {
            int len = (int)(2.4f * Rate);
            var d = new float[len];
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)Rate;
                bool on = (t < 0.4f) || (t > 0.6f && t < 1.0f);
                if (!on) continue;
                float s = Mathf.Sin(2 * Mathf.PI * 440f * t) + Mathf.Sin(2 * Mathf.PI * 480f * t);
                float trill = Mathf.Sin(2 * Mathf.PI * 20f * t) > 0 ? 1f : 0.4f;
                d[i] = s * 0.18f * trill;
            }
            return d;
        }

        static float[] Printer()
        {
            int len = Rate;
            var d = new float[len];
            var rnd = new System.Random(3);
            float lp = 0;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rnd.NextDouble() * 2 - 1);
                lp = Mathf.Lerp(lp, noise, 0.15f);
                float hum = Mathf.Sin(2 * Mathf.PI * 110f * t) * 0.3f;
                float click = (t * 8f) % 1f < 0.04f ? 0.6f : 0f;
                d[i] = (lp * 0.5f + hum + click * noise) * 0.16f;
            }
            return d;
        }

        static float[] Coffee()
        {
            int len = (int)(1.5f * Rate);
            var d = new float[len];
            var rnd = new System.Random(5);
            float lp = 0;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rnd.NextDouble() * 2 - 1);
                lp = Mathf.Lerp(lp, noise, 0.06f);
                float gurgle = 0.5f + 0.5f * Mathf.Sin(2 * Mathf.PI * 7f * t + Mathf.Sin(t * 13f) * 2f);
                d[i] = lp * gurgle * 0.35f;
            }
            return d;
        }

        static float[] Siren()
        {
            int len = Rate;
            var d = new float[len];
            double ph = 0;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)len;
                float f = 620f + 320f * Mathf.Sin(t * 2f * Mathf.PI);
                ph += f / Rate;
                d[i] = (Osc(Wave.Triangle, ph) * 0.7f + Osc(Wave.Square, ph) * 0.3f) * 0.22f;
            }
            return d;
        }
    }
}
