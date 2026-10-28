using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Procedural sound generators (mono, 22.05 kHz). Every clip is generated once at startup from a fixed seed, so
    /// the build ships no audio assets for effects. Measured: 47 clips, 39.8 s ≈ 0.88 M samples ≈ 3.4 MB as float PCM, 114 ms to synthesise
    /// (.NET 8 Release; expect 0.1–0.3 s under Mono at boot).
    /// </summary>
    public static class SfxSynth
    {
        public const int Rate = 22050;
        public enum Wave { Sine, Square, Triangle, Saw }

        const double TwoPi = Math.PI * 2.0;

        public static int N(float seconds) { return Mathf.Max(1, (int)(seconds * Rate)); }

        public static float Osc(Wave w, double phase)
        {
            double x = phase - Math.Floor(phase);
            switch (w)
            {
                case Wave.Square: return x < 0.5 ? 0.6f : -0.6f;
                case Wave.Triangle: return (float)(4.0 * Math.Abs(x - 0.5) - 1.0);
                case Wave.Saw: return (float)(2.0 * x - 1.0);
                default: return (float)Math.Sin(TwoPi * x);
            }
        }

        // ------------------------------------------------------------------ building blocks

        /// <summary>Sequence of equal-length notes with a click-free linear decay per note.</summary>
        public static float[] Tone(float[] notes, float noteDur, float vol, Wave w = Wave.Sine)
        {
            int per = N(noteDur);
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

        public static float[] Sweep(float f0, float f1, float dur, float vol, Wave w = Wave.Sine)
        {
            int len = N(dur);
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

        /// <summary>Adds a (gliding) tone with attack and exponential decay (tau seconds; 0 = linear to zero).</summary>
        public static void AddTone(float[] d, float start, float dur, float f0, float f1, float vol, Wave w = Wave.Sine, float tau = 0f, float attack = 0.004f)
        {
            int s = N(start) - 1, len = N(dur);
            double ph = 0;
            for (int i = 0; i < len && s + i < d.Length; i++)
            {
                if (s + i < 0) continue;
                float t = i / (float)Rate, u = i / (float)len;
                ph += Mathf.Lerp(f0, f1, u) / Rate;
                float env = Mathf.Min(1f, t / Mathf.Max(attack, 1e-4f)) * (tau > 0f ? Mathf.Exp(-t / tau) : 1f - u);
                if (tau > 0f) env *= Mathf.Min(1f, (len - i) / (Rate * 0.005f)); // no click at the cut
                d[s + i] += Osc(w, ph) * env * vol;
            }
        }

        /// <summary>Adds filtered noise. lp: one-pole low-pass coefficient (1 = none); highPass subtracts the low band.</summary>
        public static void AddNoise(float[] d, float start, float dur, float vol, System.Random rnd, float lp = 1f, bool highPass = false, float tau = 0f, float attack = 0.002f)
        {
            int s = N(start) - 1, len = N(dur);
            float low = 0f, band = 0f;
            for (int i = 0; i < len && s + i < d.Length; i++)
            {
                if (s + i < 0) continue;
                float x = (float)(rnd.NextDouble() * 2 - 1);
                low += (x - low) * 0.12f;
                band += (x - band) * lp;
                float y = highPass ? x - low : band;
                float t = i / (float)Rate, u = i / (float)len;
                float env = Mathf.Min(1f, t / Mathf.Max(attack, 1e-4f)) * (tau > 0f ? Mathf.Exp(-t / tau) : 1f - u);
                d[s + i] += y * env * vol;
            }
        }

        /// <summary>RBJ biquad (mutable struct: keep it in a local variable).</summary>
        public struct Biquad
        {
            float _b0, _b1, _b2, _a1, _a2, _x1, _x2, _y1, _y2;

            public static Biquad BandPass(float f, float q) { var b = new Biquad(); b.SetBandPass(f, q); return b; }
            public static Biquad LowPass(float f, float q = 0.707f) { var b = new Biquad(); b.SetLowPass(f, q); return b; }

            public void SetBandPass(float f, float q)
            {
                float w0 = 2f * Mathf.PI * Mathf.Clamp(f, 20f, Rate * 0.45f) / Rate, alpha = Mathf.Sin(w0) / (2f * q), a0 = 1f + alpha;
                _b0 = alpha / a0; _b1 = 0f; _b2 = -alpha / a0; _a1 = -2f * Mathf.Cos(w0) / a0; _a2 = (1f - alpha) / a0;
            }

            public void SetLowPass(float f, float q)
            {
                float w0 = 2f * Mathf.PI * Mathf.Clamp(f, 20f, Rate * 0.45f) / Rate, alpha = Mathf.Sin(w0) / (2f * q), c = Mathf.Cos(w0), a0 = 1f + alpha;
                _b0 = (1f - c) * 0.5f / a0; _b1 = (1f - c) / a0; _b2 = _b0; _a1 = -2f * c / a0; _a2 = (1f - alpha) / a0;
            }

            public float Process(float x)
            {
                float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
                _x2 = _x1; _x1 = x; _y2 = _y1; _y1 = y;
                return y;
            }
        }

        public static float[] Normalize(float[] d, float peak)
        {
            float m = 0f;
            for (int i = 0; i < d.Length; i++) m = Mathf.Max(m, Mathf.Abs(d[i]));
            if (m > 1e-5f) { float k = peak / m; for (int i = 0; i < d.Length; i++) d[i] *= k; }
            return d;
        }

        /// <summary>Makes a buffer loop seamlessly by cross-fading its last <paramref name="overlap"/> seconds into its head.</summary>
        static float[] LoopCrossfade(float[] src, float overlap)
        {
            int x = N(overlap), len = src.Length - x;
            var d = new float[len];
            for (int i = 0; i < len; i++) d[i] = src[i];
            for (int i = 0; i < x; i++)
            {
                float k = i / (float)x;
                d[i] = src[i] * k + src[len + i] * (1f - k);
            }
            return d;
        }

        // ------------------------------------------------------------------ v1 kit (kept, now 22.05 kHz)

        public static float[] Trombone()
        {
            float[] notes = { 294f, 277f, 262f, 247f };
            float[] durs = { 0.38f, 0.38f, 0.38f, 1.2f };
            var list = new List<float>(N(2.4f));
            double ph = 0;
            for (int n = 0; n < notes.Length; n++)
            {
                int len = N(durs[n]);
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

        public static float[] PhoneRing()
        {
            int len = N(2.4f);
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

        public static float[] Printer()
        {
            int len = N(1f);
            var d = new float[len];
            var rnd = new System.Random(3);
            float lp = 0;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rnd.NextDouble() * 2 - 1);
                lp = Mathf.Lerp(lp, noise, 0.25f);
                float hum = Mathf.Sin(2 * Mathf.PI * 110f * t) * 0.3f;
                float click = (t * 8f) % 1f < 0.04f ? 0.6f : 0f;
                d[i] = (lp * 0.5f + hum + click * noise) * 0.16f;
            }
            return d;
        }

        public static float[] Coffee()
        {
            int len = N(1.5f);
            var d = new float[len];
            var rnd = new System.Random(5);
            float lp = 0;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rnd.NextDouble() * 2 - 1);
                lp = Mathf.Lerp(lp, noise, 0.11f);
                float gurgle = 0.5f + 0.5f * Mathf.Sin(2 * Mathf.PI * 7f * t + Mathf.Sin(t * 13f) * 2f);
                d[i] = lp * gurgle * 0.35f;
            }
            return d;
        }

        public static float[] Siren()
        {
            int len = N(1f);
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

        // ------------------------------------------------------------------ v2: phone & calls

        /// <summary>Cheesy mobile ringtone arpeggio (loops; 1.45 s).</summary>
        public static float[] Ringtone()
        {
            float[] notes = { 1046.5f, 1318.5f, 1568f, 1318.5f, 1046.5f, 1318.5f, 1568f, 2093f };
            var d = new float[N(1.45f)];
            for (int i = 0; i < notes.Length; i++)
            {
                AddTone(d, i * 0.11f, 0.105f, notes[i], notes[i], 0.13f, Wave.Square, 0.09f);
                AddTone(d, i * 0.11f, 0.105f, notes[i], notes[i], 0.2f, Wave.Sine, 0.12f);
            }
            return d;
        }

        /// <summary>Outgoing ring-back tone: 1 s on, 2 s off (loops).</summary>
        public static float[] Ringback()
        {
            var d = new float[N(3f)];
            int on = N(1f), ramp = N(0.01f);
            for (int i = 0; i < on; i++)
            {
                float env = Mathf.Min(1f, Mathf.Min(i, on - i) / (float)ramp);
                d[i] = Mathf.Sin(2f * Mathf.PI * 450f * i / Rate) * 0.16f * env;
            }
            return d;
        }

        public static float[] CallConnect()
        {
            var d = new float[N(0.2f)];
            AddTone(d, 0f, 0.07f, 660f, 660f, 0.3f);
            AddTone(d, 0.09f, 0.09f, 990f, 990f, 0.3f);
            return d;
        }

        public static float[] CallEnd()
        {
            var d = new float[N(0.55f)];
            for (int i = 0; i < 3; i++) AddTone(d, i * 0.17f, 0.1f, 480f, 480f, 0.22f, Wave.Triangle);
            return d;
        }

        /// <summary>DTMF digits (tip line / outgoing call dial).</summary>
        public static float[] Dial()
        {
            int[,] pairs = { { 697, 1209 }, { 770, 1336 }, { 697, 1477 }, { 852, 1477 }, { 941, 1336 }, { 697, 1336 } };
            var d = new float[N(0.8f)];
            for (int i = 0; i < pairs.GetLength(0); i++)
            {
                AddTone(d, i * 0.13f, 0.08f, pairs[i, 0], pairs[i, 0], 0.14f, Wave.Sine, 0f, 0.003f);
                AddTone(d, i * 0.13f, 0.08f, pairs[i, 1], pairs[i, 1], 0.12f, Wave.Sine, 0f, 0.003f);
            }
            return d;
        }

        public static float[] EmailDing()
        {
            var d = new float[N(1.0f)];
            float[] ratio = { 1f, 2.4f, 4.1f }, amp = { 1f, 0.35f, 0.15f }, tau = { 0.35f, 0.2f, 0.1f };
            float[] fund = { 1318.5f, 1046.5f };
            for (int n = 0; n < 2; n++)
                for (int p = 0; p < 3; p++)
                    AddTone(d, n * 0.17f, 0.8f, fund[n] * ratio[p], fund[n] * ratio[p], 0.22f * amp[p], Wave.Sine, tau[p], 0.002f);
            return Normalize(d, 0.32f);
        }

        // ------------------------------------------------------------------ v2: sabotage & outages

        public static float[] Spark()
        {
            var rnd = new System.Random(11);
            var d = new float[N(0.5f)];
            AddTone(d, 0f, 0.06f, 2800f, 600f, 0.22f, Wave.Square);
            for (int k = 0; k < 18; k++)
            {
                float r = (float)rnd.NextDouble();
                float t = 0.4f * r * r;
                AddNoise(d, t, 0.002f + 0.005f * (float)rnd.NextDouble(), 0.55f * (1f - t / 0.5f), rnd, 1f, true, 0.002f, 0.0005f);
            }
            return Normalize(d, 0.5f);
        }

        public static float[] PlugIn()
        {
            var rnd = new System.Random(12);
            var d = new float[N(0.12f)];
            AddNoise(d, 0f, 0.004f, 0.5f, rnd, 1f, true, 0.001f, 0.0003f);
            AddTone(d, 0f, 0.03f, 1800f, 1700f, 0.18f, Wave.Sine, 0.008f);
            AddNoise(d, 0.05f, 0.003f, 0.3f, rnd, 1f, true, 0.001f, 0.0003f);
            return d;
        }

        public static float[] InternetDown()
        {
            var d = new float[N(0.75f)];
            float[] notes = { 784f, 659.3f, 523.3f };
            for (int i = 0; i < 3; i++) AddTone(d, i * 0.14f, 0.3f, notes[i], notes[i], 0.28f, Wave.Triangle, 0.12f);
            AddTone(d, 0.45f, 0.2f, 300f, 150f, 0.25f, Wave.Sine, 0.08f);
            return d;
        }

        public static float[] PowerDown()
        {
            var rnd = new System.Random(13);
            var d = new float[N(1.4f)];
            AddTone(d, 0f, 0.35f, 90f, 40f, 0.7f, Wave.Sine, 0.12f);
            AddNoise(d, 0f, 0.06f, 0.4f, rnd, 0.05f, false, 0.02f);
            AddTone(d, 0f, 0.03f, 3000f, 3000f, 0.06f, Wave.Sine, 0.01f);
            // Mains hum sliding down as the fluorescent ballasts die.
            int s = N(0.02f), len = N(1.1f);
            double ph = 0;
            for (int i = 0; i < len && s + i < d.Length; i++)
            {
                float u = i / (float)len;
                ph += Mathf.Lerp(120f, 35f, u) / Rate;
                float h = Osc(Wave.Sine, ph) + 0.5f * Osc(Wave.Sine, ph * 2) + 0.25f * Osc(Wave.Sine, ph * 3);
                d[s + i] += h * 0.12f * Mathf.Pow(1f - u, 1.5f);
            }
            return Normalize(d, 0.7f);
        }

        public static float[] PowerUp()
        {
            var rnd = new System.Random(14);
            var d = new float[N(1.2f)];
            AddNoise(d, 0f, 0.04f, 0.5f, rnd, 0.1f, false, 0.015f);
            AddTone(d, 0f, 0.14f, 140f, 70f, 0.5f, Wave.Sine, 0.05f);
            int s = N(0.1f), len = N(1.05f);
            double ph = 0;
            for (int i = 0; i < len && s + i < d.Length; i++)
            {
                float u = i / (float)len;
                ph += Mathf.Lerp(40f, 120f, Mathf.Min(1f, u * 1.6f)) / Rate;
                float h = Osc(Wave.Sine, ph) + 0.4f * Osc(Wave.Sine, ph * 2);
                d[s + i] += h * 0.1f * Mathf.Min(1f, u * 4f) * (1f - u);
            }
            float[] ticks = { 0.35f, 0.5f, 0.58f, 0.72f };
            for (int i = 0; i < ticks.Length; i++)
            {
                AddNoise(d, ticks[i], 0.005f, 0.3f, rnd, 1f, true, 0.002f, 0.0003f);
                AddTone(d, ticks[i], 0.02f, 2400f, 2400f, 0.08f, Wave.Sine, 0.006f);
            }
            return Normalize(d, 0.6f);
        }

        public static float[] RouterRestore()
        {
            var d = new float[N(0.85f)];
            int s = N(0.35f);
            double ph = 0;
            for (int i = 0; i < s; i++)
            {
                float f = (i / N(0.015f)) % 2 == 0 ? 1200f : 2200f;
                ph += f / Rate;
                d[i] = Osc(Wave.Square, ph) * 0.06f * Mathf.Min(1f, (s - i) / (float)N(0.01f));
            }
            float[] notes = { 523.3f, 659.3f, 784f, 1046.5f };
            for (int i = 0; i < 4; i++) AddTone(d, 0.38f + i * 0.09f, 0.25f, notes[i], notes[i], 0.26f, Wave.Triangle, 0.1f);
            return d;
        }

        public static float[] SaltShake()
        {
            var rnd = new System.Random(15);
            var d = new float[N(0.62f)];
            for (int k = 0; k < 4; k++)
            {
                int s = N(k * 0.16f), len = N(0.09f);
                float low = 0f;
                for (int i = 0; i < len && s + i < d.Length; i++)
                {
                    float x = (float)(rnd.NextDouble() * 2 - 1);
                    low += (x - low) * 0.2f;
                    float grain = rnd.NextDouble() > 0.6 ? 1f : 0.3f;
                    float u = i / (float)len;
                    d[s + i] += (x - low) * grain * Mathf.Min(1f, u * 10f) * (1f - u) * 0.4f;
                }
            }
            return Normalize(d, 0.45f);
        }

        public static float[] Slurp()
        {
            var rnd = new System.Random(16);
            int len = N(0.75f);
            var d = new float[len];
            var bp = Biquad.BandPass(500f, 6f);
            for (int i = 0; i < len; i++)
            {
                float u = i / (float)len, t = i / (float)Rate;
                if ((i & 63) == 0) bp.SetBandPass(Mathf.Lerp(500f, 1600f, u), 6f);
                float x = (float)(rnd.NextDouble() * 2 - 1);
                float bubbles = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 18f * t) * (float)rnd.NextDouble();
                d[i] = bp.Process(x) * bubbles * Mathf.Min(1f, t / 0.05f) * Mathf.Min(1f, (1f - u) * 5f);
            }
            return Normalize(d, 0.5f);
        }

        /// <summary>"Bleh!" after tasting a salted coffee.</summary>
        public static float[] Bleh()
        {
            var rnd = new System.Random(17);
            int len = N(0.45f);
            var d = new float[len];
            AddNoise(d, 0f, 0.06f, 0.25f, rnd, 1f, true, 0.02f);
            var f1 = Biquad.BandPass(900f, 2f);
            var f2 = Biquad.BandPass(2400f, 4f);
            double ph = 0;
            int s = N(0.04f);
            for (int i = s; i < len; i++)
            {
                float u = (i - s) / (float)(len - s);
                ph += Mathf.Lerp(200f, 130f, u) / Rate;
                float src = Osc(Wave.Saw, ph);
                d[i] += (f1.Process(src) + 0.5f * f2.Process(src)) * Mathf.Min(1f, u * 12f) * (1f - u);
            }
            return Normalize(d, 0.4f);
        }

        public static float[] MailDrop()
        {
            var rnd = new System.Random(18);
            var d = new float[N(0.3f)];
            int len = N(0.08f);
            var bp = Biquad.BandPass(1500f, 0.8f);
            for (int i = 0; i < len; i++)
            {
                float u = i / (float)len;
                d[i] += bp.Process((float)(rnd.NextDouble() * 2 - 1)) * (1f - u) * 0.5f;
            }
            AddTone(d, 0.02f, 0.2f, 120f, 70f, 0.45f, Wave.Sine, 0.05f);
            return Normalize(d, 0.45f);
        }

        // ------------------------------------------------------------------ v2: footsteps & manager voice

        public static float[] Step(int variant)
        {
            var rnd = new System.Random(20 + variant);
            var d = new float[N(0.11f)];
            float[] lp = { 0.08f, 0.1f, 0.12f };
            float[] f = { 95f, 105f, 85f };
            AddNoise(d, 0f, 0.05f, 0.5f, rnd, lp[variant % 3], false, 0.015f, 0.002f);
            AddTone(d, 0f, 0.08f, f[variant % 3], f[variant % 3] * 0.7f, 0.5f, Wave.Sine, 0.03f);
            return Normalize(d, 0.5f);
        }

        /// <summary>The manager's dress shoes: a sharper heel clack.</summary>
        public static float[] ManagerStep(int variant)
        {
            var rnd = new System.Random(30 + variant);
            var d = new float[N(0.09f)];
            var bp = Biquad.BandPass(variant == 0 ? 2600f : 2300f, 3f);
            int len = N(0.025f);
            for (int i = 0; i < len; i++) d[i] += bp.Process((float)(rnd.NextDouble() * 2 - 1)) * (1f - i / (float)len) * 0.8f;
            AddTone(d, 0f, 0.06f, variant == 0 ? 480f : 430f, 300f, 0.3f, Wave.Sine, 0.015f);
            AddTone(d, 0f, 0.07f, 110f, 80f, 0.3f, Wave.Sine, 0.025f);
            return Normalize(d, 0.6f);
        }

        /// <summary>A closed-mouth "hmm": glottal saw through nasal formants, f0 from a contour (Hz over 0..1).</summary>
        static void AddHum(float[] d, float start, float dur, Func<float, float> f0, float vol)
        {
            int s = N(start), len = N(dur);
            var n1 = Biquad.BandPass(290f, 3f);
            var n2 = Biquad.BandPass(1250f, 5f);
            var n3 = Biquad.BandPass(2500f, 6f);
            double ph = 0;
            float lp = 0f;
            for (int i = 0; i < len && s + i < d.Length; i++)
            {
                float u = i / (float)len, t = i / (float)Rate;
                float vib = 1f + 0.015f * Mathf.Sin(2f * Mathf.PI * 5.5f * t);
                ph += f0(u) * vib / Rate;
                float src = Osc(Wave.Saw, ph);
                lp += (src - lp) * 0.2f;
                float y = n1.Process(lp) + 0.25f * n2.Process(lp) + 0.1f * n3.Process(lp);
                float env = Mathf.Min(1f, t / 0.04f) * (u > 0.7f ? (1f - u) / 0.3f : 1f);
                d[s + i] += y * env * vol;
            }
        }

        public static float[] ManagerHum(string profile)
        {
            var rnd = new System.Random(40);
            float[] d;
            switch (profile)
            {
                case ManagerProfileIds.Paragoz: // grumpy, falling, long
                    d = new float[N(0.6f)];
                    AddHum(d, 0f, 0.58f, u => Mathf.Lerp(108f, 88f, u), 1f);
                    break;
                case ManagerProfileIds.Disiplinci: // "tsk" + short sharp hm
                    d = new float[N(0.4f)];
                    AddNoise(d, 0f, 0.006f, 0.6f, rnd, 1f, true, 0.002f, 0.0003f);
                    AddHum(d, 0.09f, 0.28f, u => Mathf.Lerp(230f, 200f, u), 1f);
                    break;
                case ManagerProfileIds.Kayirmaci: // sing-song
                    d = new float[N(0.62f)];
                    AddHum(d, 0f, 0.6f, u => u < 0.4f ? Mathf.Lerp(190f, 255f, u / 0.4f) : Mathf.Lerp(255f, 175f, (u - 0.4f) / 0.6f), 1f);
                    break;
                case ManagerProfileIds.Paranoyak: // "hm? hm?"
                    d = new float[N(0.55f)];
                    AddHum(d, 0f, 0.2f, u => Mathf.Lerp(135f, 185f, u), 1f);
                    AddHum(d, 0.3f, 0.22f, u => Mathf.Lerp(150f, 210f, u), 1f);
                    break;
                default:
                    d = new float[N(0.48f)];
                    AddHum(d, 0f, 0.45f, u => Mathf.Lerp(150f, 118f, u), 1f);
                    break;
            }
            return Normalize(d, 0.55f);
        }

        // ------------------------------------------------------------------ v2: stings & meeting

        public static float[] InspectionSting()
        {
            var rnd = new System.Random(50);
            var d = new float[N(1.15f)];
            AddNoise(d, 0f, 0.45f, 0.25f, rnd, 1f, true, 0.15f);
            float[] a = { 146.8f, 174.6f, 207.7f, 293.7f };
            float[] b = { 138.6f, 164.8f, 196f, 277.2f };
            for (int i = 0; i < 4; i++) AddTone(d, 0f, 0.2f, a[i], a[i], 0.16f, Wave.Saw, 0f, 0.003f);
            for (int i = 0; i < 4; i++) AddTone(d, 0.22f, 0.9f, b[i], b[i], 0.16f, Wave.Saw, 0.3f, 0.01f);
            // soften the saws
            float lp = 0f;
            for (int i = 0; i < d.Length; i++) { lp += (d[i] - lp) * 0.3f; d[i] = lp; }
            return Normalize(d, 0.6f);
        }

        /// <summary>"Dun-dun!" when the manager catches you.</summary>
        public static float[] Caught()
        {
            var d = new float[N(0.85f)];
            AddTone(d, 0f, 0.17f, 110f, 110f, 0.4f, Wave.Saw, 0f, 0.004f);
            AddTone(d, 0f, 0.17f, 55f, 55f, 0.4f, Wave.Sine, 0f, 0.004f);
            AddTone(d, 0.2f, 0.62f, 87.3f, 85f, 0.4f, Wave.Saw, 0.25f, 0.004f);
            AddTone(d, 0.2f, 0.62f, 43.7f, 43f, 0.45f, Wave.Sine, 0.3f, 0.004f);
            float lp = 0f;
            for (int i = 0; i < d.Length; i++) { lp += (d[i] - lp) * 0.2f; d[i] = lp; }
            return Normalize(d, 0.6f);
        }

        /// <summary>CCTV tape: clunk, hiss, mains hum and wow.</summary>
        public static float[] Vhs()
        {
            var rnd = new System.Random(51);
            int len = N(1.6f);
            var d = new float[len];
            AddTone(d, 0f, 0.15f, 120f, 60f, 0.5f, Wave.Sine, 0.05f);
            AddNoise(d, 0f, 0.01f, 0.4f, rnd, 1f, true, 0.003f, 0.0005f);
            float lp = 0f;
            int s = N(0.08f);
            for (int i = s; i < len; i++)
            {
                float t = i / (float)Rate, u = (i - s) / (float)(len - s);
                float x = (float)(rnd.NextDouble() * 2 - 1);
                lp += (x - lp) * 0.35f;
                float wow = 0.8f + 0.2f * Mathf.Sin(2f * Mathf.PI * 0.7f * t);
                float hum = Mathf.Sin(2f * Mathf.PI * 50f * t) * 0.08f + Mathf.Sin(2f * Mathf.PI * 100f * t) * 0.04f;
                float whine = Mathf.Sin(2f * Mathf.PI * 7800f * t) * 0.008f;
                d[i] += (lp * 0.14f + hum + whine) * wow * Mathf.Min(1f, u * 8f) * Mathf.Min(1f, (1f - u) * 4f);
            }
            return Normalize(d, 0.45f);
        }

        public static float[] Gavel()
        {
            var rnd = new System.Random(52);
            var d = new float[N(0.5f)];
            for (int k = 0; k < 2; k++)
            {
                float t = k * 0.2f;
                AddTone(d, t, 0.15f, 210f, 150f, 0.6f, Wave.Sine, 0.04f, 0.001f);
                AddTone(d, t, 0.2f, 80f, 60f, 0.35f, Wave.Sine, 0.06f, 0.001f);
                int s = N(t), len = N(0.015f);
                var bp = Biquad.BandPass(1200f, 1.2f);
                for (int i = 0; i < len && s + i < d.Length; i++) d[s + i] += bp.Process((float)(rnd.NextDouble() * 2 - 1)) * (1f - i / (float)len) * 0.8f;
            }
            return Normalize(d, 0.7f);
        }

        public static float[] Stamp()
        {
            var rnd = new System.Random(53);
            var d = new float[N(0.45f)];
            AddTone(d, 0f, 0.4f, 75f, 45f, 0.8f, Wave.Sine, 0.09f, 0.001f);
            AddNoise(d, 0f, 0.05f, 0.5f, rnd, 0.08f, false, 0.015f, 0.0005f);
            AddNoise(d, 0f, 0.015f, 0.35f, rnd, 1f, true, 0.004f, 0.0003f);
            return Normalize(d, 0.75f);
        }

        // ------------------------------------------------------------------ v2: small UI-ish & VFX companions

        public static float[] EmotePop()
        {
            var rnd = new System.Random(54);
            var d = new float[N(0.1f)];
            AddTone(d, 0f, 0.07f, 420f, 980f, 0.35f, Wave.Sine, 0.03f, 0.002f);
            AddNoise(d, 0f, 0.01f, 0.1f, rnd, 1f, true, 0.003f);
            return d;
        }

        public static float[] WheelTick()
        {
            var d = new float[N(0.02f)];
            AddTone(d, 0f, 0.015f, 2200f, 2200f, 0.3f, Wave.Sine, 0.004f, 0.0005f);
            return d;
        }

        public static float[] Rustle()
        {
            var rnd = new System.Random(55);
            int len = N(0.16f);
            var d = new float[len];
            var bp = Biquad.BandPass(1400f, 0.7f);
            for (int i = 0; i < len; i++)
            {
                float u = i / (float)len;
                d[i] = bp.Process((float)(rnd.NextDouble() * 2 - 1)) * Mathf.Min(1f, u * 5f) * (1f - u);
            }
            return Normalize(d, 0.3f);
        }

        public static float[] Confetti()
        {
            var rnd = new System.Random(56);
            var d = new float[N(0.7f)];
            AddNoise(d, 0f, 0.025f, 0.7f, rnd, 0.4f, false, 0.008f, 0.0005f);
            AddTone(d, 0f, 0.08f, 180f, 120f, 0.4f, Wave.Sine, 0.03f, 0.001f);
            AddTone(d, 0.02f, 0.25f, 900f, 2600f, 0.1f, Wave.Sine, 0f, 0.01f);
            for (int k = 0; k < 12; k++)
            {
                float t = 0.08f + 0.52f * (float)rnd.NextDouble();
                AddNoise(d, t, 0.003f, 0.2f * (1f - t), rnd, 1f, true, 0.001f, 0.0003f);
            }
            return Normalize(d, 0.55f);
        }

        public static float[] Splash()
        {
            var rnd = new System.Random(57);
            int len = N(0.5f);
            var d = new float[len];
            var lp = Biquad.LowPass(3000f);
            int sweep = N(0.3f);
            for (int i = 0; i < len; i++)
            {
                float u = i / (float)len, t = i / (float)Rate;
                if ((i & 31) == 0) lp.SetLowPass(Mathf.Lerp(3000f, 400f, Mathf.Min(1f, i / (float)sweep)), 0.9f);
                d[i] = lp.Process((float)(rnd.NextDouble() * 2 - 1)) * Mathf.Min(1f, t / 0.005f) * Mathf.Exp(-t / 0.12f) * 0.6f;
            }
            for (int k = 0; k < 6; k++)
            {
                float t = 0.08f + 0.32f * (float)rnd.NextDouble();
                float f = 900f + 900f * (float)rnd.NextDouble();
                AddTone(d, t, 0.035f, f, f * 0.6f, 0.15f, Wave.Sine, 0.012f, 0.001f);
            }
            return Normalize(d, 0.5f);
        }

        public static float[] Dust()
        {
            var rnd = new System.Random(58);
            var d = new float[N(0.35f)];
            AddNoise(d, 0f, 0.33f, 0.45f, rnd, 0.06f, false, 0.09f, 0.06f);
            return Normalize(d, 0.4f);
        }

        // ------------------------------------------------------------------ v2: ambience loops

        /// <summary>Distant office typing, 6 s loop (sparse clicks, nothing near the seam).</summary>
        public static float[] AmbienceKeys()
        {
            var rnd = new System.Random(60);
            var d = new float[N(6f)];
            float t = 0.3f;
            int keys = 0;
            while (t < 5.5f)
            {
                int burst = 3 + rnd.Next(8);
                for (int k = 0; k < burst && t < 5.6f; k++)
                {
                    float v = 0.6f + 0.4f * (float)rnd.NextDouble();
                    AddNoise(d, t, 0.003f, 0.3f * v, rnd, 1f, true, 0.001f, 0.0003f);
                    float f = 1700f + 900f * (float)rnd.NextDouble();
                    AddTone(d, t, 0.02f, f, f, 0.1f * v, Wave.Sine, 0.006f, 0.0005f);
                    if (++keys % 6 == 0) AddTone(d, t + 0.01f, 0.04f, 300f, 260f, 0.12f, Wave.Sine, 0.012f, 0.001f);
                    t += 0.07f + 0.09f * (float)rnd.NextDouble();
                }
                if (rnd.NextDouble() < 0.3 && t < 5.4f)
                {
                    AddTone(d, t + 0.2f, 0.01f, 3500f, 3500f, 0.08f, Wave.Sine, 0.003f, 0.0005f);
                    AddTone(d, t + 0.28f, 0.01f, 3500f, 3500f, 0.08f, Wave.Sine, 0.003f, 0.0005f);
                }
                t += 0.4f + (float)rnd.NextDouble();
            }
            return Normalize(d, 0.45f);
        }

        /// <summary>Air conditioning + mains hum, 2 s seamless loop (integer number of cycles, cross-faded air).</summary>
        public static float[] AmbienceHum()
        {
            var rnd = new System.Random(61);
            int len = N(2.2f);
            var d = new float[len];
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)Rate;
                float x = (float)(rnd.NextDouble() * 2 - 1);
                lp += (x - lp) * 0.03f;
                lp2 += (lp - lp2) * 0.1f;
                d[i] = Mathf.Sin(2f * Mathf.PI * 60f * t) * 0.35f + Mathf.Sin(2f * Mathf.PI * 120f * t) * 0.25f +
                       Mathf.Sin(2f * Mathf.PI * 180f * t) * 0.08f + lp2 * 1.6f;
            }
            return Normalize(LoopCrossfade(d, 0.2f), 0.3f);
        }
    }
}
