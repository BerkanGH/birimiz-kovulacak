using System;
using System.Collections.Generic;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Procedural sound generators (mono, 22.05 kHz). Every clip is generated once at startup from a fixed seed, so
    /// the build ships no audio assets for effects (<see cref="Sfx"/> builds the 25 clips the desk / meeting UI and
    /// the ambience use).
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

        // ------------------------------------------------------------------ UI blips, firing trombone

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

        // ------------------------------------------------------------------ desk phone & calls

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

        // ------------------------------------------------------------------ tricks & outages

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

        // ------------------------------------------------------------------ v5 office

        /// <summary>Soft footstep on the oak floor (two variants by seed).</summary>
        public static float[] Step(int seed)
        {
            var rnd = new System.Random(seed);
            var d = new float[N(0.16f)];
            AddTone(d, 0f, 0.12f, 120f + seed * 7f, 70f, 0.5f, Wave.Sine, 0.03f, 0.002f);
            AddNoise(d, 0f, 0.05f, 0.35f, rnd, 0.18f, false, 0.012f, 0.001f);
            AddNoise(d, 0.004f, 0.02f, 0.08f, rnd, 1f, true, 0.004f, 0.0005f);
            return Normalize(d, 0.5f);
        }

        /// <summary>Piano key of the cassette recorder: plastic clunk + motor whirr starting.</summary>
        public static float[] RecClunk()
        {
            var rnd = new System.Random(71);
            var d = new float[N(0.42f)];
            AddNoise(d, 0f, 0.025f, 0.55f, rnd, 0.35f, false, 0.008f, 0.0004f);
            AddTone(d, 0f, 0.08f, 260f, 180f, 0.35f, Wave.Triangle, 0.02f, 0.001f);
            AddTone(d, 0.05f, 0.35f, 95f, 130f, 0.12f, Wave.Saw, 0.25f, 0.03f);
            return Normalize(d, 0.55f);
        }

        /// <summary>Busy signal (three short beeps).</summary>
        public static float[] Busy()
        {
            var d = new float[N(1.05f)];
            for (int i = 0; i < 3; i++) AddTone(d, i * 0.34f, 0.2f, 480f, 480f, 0.2f, Wave.Sine, 0f, 0.004f);
            for (int i = 0; i < 3; i++) AddTone(d, i * 0.34f, 0.2f, 620f, 620f, 0.16f, Wave.Sine, 0f, 0.004f);
            return d;
        }

        /// <summary>Handset lifted off the cradle (click) and the dial tone's first breath.</summary>
        public static float[] OffHook()
        {
            var rnd = new System.Random(77);
            var d = new float[N(0.5f)];
            AddNoise(d, 0f, 0.02f, 0.4f, rnd, 0.4f, false, 0.006f, 0.0004f);
            AddTone(d, 0.06f, 0.42f, 350f, 350f, 0.08f, Wave.Sine, 0.3f, 0.02f);
            AddTone(d, 0.06f, 0.42f, 440f, 440f, 0.08f, Wave.Sine, 0.3f, 0.02f);
            return Normalize(d, 0.4f);
        }

        // ------------------------------------------------------------------ stamps

        public static float[] Stamp()
        {
            var rnd = new System.Random(53);
            var d = new float[N(0.45f)];
            AddTone(d, 0f, 0.4f, 75f, 45f, 0.8f, Wave.Sine, 0.09f, 0.001f);
            AddNoise(d, 0f, 0.05f, 0.5f, rnd, 0.08f, false, 0.015f, 0.0005f);
            AddNoise(d, 0f, 0.015f, 0.35f, rnd, 1f, true, 0.004f, 0.0003f);
            return Normalize(d, 0.75f);
        }

        // ------------------------------------------------------------------ coffee spill

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

        // ------------------------------------------------------------------ ambience loops

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
