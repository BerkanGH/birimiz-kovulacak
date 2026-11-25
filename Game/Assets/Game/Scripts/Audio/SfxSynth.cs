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

        // ------------------------------------------------------------------ v5 step 2: task objects

        /// <summary>Steel cash-box lid: hinge squeak and a tinny knock (open), or the knock first (close).</summary>
        public static float[] Lid(bool open)
        {
            var rnd = new System.Random(open ? 101 : 102);
            var d = new float[N(0.42f)];
            float knock = open ? 0.18f : 0f;
            AddTone(d, open ? 0f : 0.08f, 0.16f, open ? 1250f : 1500f, open ? 1700f : 1150f, 0.05f, Wave.Triangle, 0.06f, 0.01f);
            AddNoise(d, knock, 0.03f, 0.45f, rnd, 0.5f, false, 0.01f, 0.0004f);
            AddTone(d, knock, 0.2f, 620f, 590f, 0.22f, Wave.Sine, 0.05f, 0.001f);
            AddTone(d, knock, 0.2f, 1310f, 1290f, 0.1f, Wave.Sine, 0.035f, 0.001f);
            return Normalize(d, 0.5f);
        }

        /// <summary>The box landing on the desk: a dull wooden thud with a metal ring.</summary>
        public static float[] BoxDrop()
        {
            var rnd = new System.Random(103);
            var d = new float[N(0.5f)];
            AddTone(d, 0f, 0.3f, 110f, 70f, 0.7f, Wave.Sine, 0.07f, 0.001f);
            AddNoise(d, 0f, 0.05f, 0.4f, rnd, 0.15f, false, 0.015f, 0.0005f);
            AddTone(d, 0.005f, 0.4f, 780f, 760f, 0.12f, Wave.Sine, 0.09f, 0.002f);
            AddNoise(d, 0.11f, 0.04f, 0.12f, rnd, 0.2f, false, 0.01f, 0.0005f);
            return Normalize(d, 0.6f);
        }

        /// <summary>A handful of banknotes: crisp paper rustle in short bursts.</summary>
        public static float[] Bills()
        {
            var rnd = new System.Random(104);
            var d = new float[N(0.55f)];
            for (int k = 0; k < 7; k++)
            {
                float t = 0.02f + k * 0.06f + (float)rnd.NextDouble() * 0.03f;
                AddNoise(d, t, 0.05f + (float)rnd.NextDouble() * 0.04f, 0.32f - k * 0.02f, rnd, 1f, true, 0.018f, 0.002f);
            }
            return Normalize(d, 0.45f);
        }

        /// <summary>The İhbar button: a firm plastic click and a short alarm chirp.</summary>
        public static float[] AlarmButton()
        {
            var rnd = new System.Random(105);
            var d = new float[N(0.45f)];
            AddNoise(d, 0f, 0.02f, 0.5f, rnd, 0.4f, false, 0.006f, 0.0003f);
            AddTone(d, 0.05f, 0.12f, 1180f, 1180f, 0.18f, Wave.Square, 0f, 0.004f);
            AddTone(d, 0.2f, 0.16f, 880f, 880f, 0.18f, Wave.Square, 0f, 0.004f);
            return Normalize(d, 0.5f);
        }

        /// <summary>Pencil writing a name: scratchy strokes.</summary>
        public static float[] Pencil()
        {
            var rnd = new System.Random(106);
            var d = new float[N(1.15f)];
            float t = 0.3f;
            while (t < 1.1f)
            {
                float len = 0.05f + (float)rnd.NextDouble() * 0.08f;
                AddNoise(d, t, len, 0.22f, rnd, 1f, true, 0f, 0.008f);
                t += len + 0.015f + (float)rnd.NextDouble() * 0.03f;
            }
            return Normalize(d, 0.32f);
        }

        /// <summary>Rubber eraser rubbing back and forth (softer, lower than the pencil).</summary>
        public static float[] Eraser()
        {
            var rnd = new System.Random(107);
            var d = new float[N(1.25f)];
            for (int k = 0; k < 6; k++)
                AddNoise(d, 0.32f + k * 0.15f, 0.13f, 0.28f, rnd, 0.35f, false, 0f, 0.03f);
            return Normalize(d, 0.32f);
        }

        /// <summary>Wooden drawer sliding on its runners.</summary>
        public static float[] DrawerSlide()
        {
            var rnd = new System.Random(108);
            var d = new float[N(0.4f)];
            AddNoise(d, 0f, 0.3f, 0.3f, rnd, 0.25f, false, 0f, 0.04f);
            AddTone(d, 0f, 0.3f, 160f, 190f, 0.08f, Wave.Saw, 0f, 0.04f);
            AddNoise(d, 0.29f, 0.04f, 0.35f, rnd, 0.3f, false, 0.012f, 0.0005f);
            return Normalize(d, 0.42f);
        }

        /// <summary>Money landing in the drawer: a soft wooden thunk and a short rustle.</summary>
        public static float[] DrawerThunk()
        {
            var rnd = new System.Random(109);
            var d = new float[N(0.5f)];
            AddTone(d, 0f, 0.22f, 130f, 80f, 0.6f, Wave.Sine, 0.05f, 0.001f);
            AddNoise(d, 0f, 0.04f, 0.4f, rnd, 0.2f, false, 0.012f, 0.0005f);
            AddNoise(d, 0.07f, 0.12f, 0.16f, rnd, 1f, true, 0.03f, 0.004f);
            return Normalize(d, 0.55f);
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

        /// <summary>A paper shredder taking one file (~0.75 s): the motor spinning up, the blades chewing, the last strips.</summary>
        public static float[] Shredder()
        {
            var rnd = new System.Random(131);
            int len = N(0.75f);
            var d = new float[len];
            var teeth = Biquad.BandPass(3100f, 1.1f);
            var body = Biquad.LowPass(900f);
            double ph = 0;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)Rate, u = i / (float)len;
                float spin = Mathf.Min(1f, t / 0.07f) * Mathf.Min(1f, (1f - u) / 0.22f);
                ph += Mathf.Lerp(70f, 118f, Mathf.Min(1f, t / 0.12f)) * (0.82f + 0.18f * spin) / Rate;
                float motor = Osc(Wave.Saw, ph) * 0.2f + Osc(Wave.Square, ph * 2.0) * 0.06f;
                float x = (float)(rnd.NextDouble() * 2 - 1);
                // the blades: paper noise gated at the cutters' rate, densest in the middle
                float chew = 0.3f + 0.7f * Mathf.Abs(Mathf.Sin(t * 2f * Mathf.PI * 46f));
                float paper = teeth.Process(x) * chew * Mathf.Sin(Mathf.Clamp01((u - 0.06f) / 0.86f) * Mathf.PI);
                d[i] = (body.Process(motor) + paper * 0.95f) * spin;
            }
            return Normalize(d, 0.5f);
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

        // ------------------------------------------------------------------ v5 Yaramazlıklar (all original, synthesised)

        /// <summary>An embarrassing ringtone of our own: a bouncy kazoo-like ditty (square wave with vibrato) that ends in a
        /// silly falling "boing", played twice (~3.4 s).</summary>
        public static float[] PrankRing()
        {
            // a made-up ditty (no existing melody): hop, hop, wobble, fall
            float[] notes = { 784f, 659f, 784f, 988f, 880f, 784f, 659f, 587f, 659f, 784f, 659f, 523f };
            float[] lens = { 0.1f, 0.1f, 0.1f, 0.2f, 0.1f, 0.1f, 0.1f, 0.2f, 0.1f, 0.1f, 0.1f, 0.18f };
            var d = new float[N(3.4f)];
            for (int rep = 0; rep < 2; rep++)
            {
                float t = rep * 1.7f;
                for (int i = 0; i < notes.Length; i++)
                {
                    float f = notes[i];
                    int s0 = N(t), len = N(lens[i] * 0.92f);
                    double ph = 0;
                    for (int k = 0; k < len && s0 + k < d.Length; k++)
                    {
                        float u = k / (float)len, tt = k / (float)Rate;
                        float vib = 1f + 0.018f * Mathf.Sin(2f * Mathf.PI * 7.5f * tt);
                        ph += f * vib / Rate;
                        float env = Mathf.Min(1f, tt / 0.006f) * (1f - u * 0.6f) * Mathf.Min(1f, (len - k) / (Rate * 0.006f));
                        float x = Osc(Wave.Square, ph) * 0.55f + Osc(Wave.Triangle, ph * 2.0) * 0.3f;
                        d[s0 + k] += x * env * 0.22f;
                    }
                    t += lens[i];
                }
                // the boing
                AddTone(d, t + 0.02f, 0.28f, 420f, 150f, 0.3f, Wave.Triangle, 0.12f);
            }
            return Normalize(d, 0.62f);
        }

        /// <summary>A loud slurp of hot tea: band-passed noise sweeping up with a bubbling tremolo (~0.8 s).</summary>
        public static float[] Slurp()
        {
            var rnd = new System.Random(71);
            int len = N(0.85f);
            var d = new float[len];
            var bp = Biquad.BandPass(500f, 4f);
            for (int i = 0; i < len; i++)
            {
                float u = i / (float)len, t = i / (float)Rate;
                if ((i & 15) == 0) bp.SetBandPass(Mathf.Lerp(450f, 2600f, Mathf.Pow(u, 0.7f)) + 260f * Mathf.Sin(t * 60f), 3.5f);
                float trem = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(t * 2f * Mathf.PI * 13f));
                float env = Mathf.Min(1f, t / 0.05f) * Mathf.Min(1f, (len - i) / (Rate * 0.08f));
                d[i] = bp.Process((float)(rnd.NextDouble() * 2 - 1)) * trem * env;
            }
            return Normalize(d, 0.6f);
        }

        /// <summary>A palm slammed on the meeting table: a low wooden thump and a rattle.</summary>
        public static float[] TableSlam()
        {
            var rnd = new System.Random(73);
            var d = new float[N(0.6f)];
            AddTone(d, 0f, 0.35f, 95f, 55f, 0.9f, Wave.Sine, 0.09f, 0.001f);
            AddTone(d, 0f, 0.12f, 240f, 160f, 0.35f, Wave.Triangle, 0.03f, 0.001f);
            AddNoise(d, 0f, 0.09f, 0.6f, rnd, 0.18f, false, 0.025f, 0.0005f);
            for (int k = 0; k < 4; k++) AddNoise(d, 0.07f + k * 0.045f, 0.03f, 0.12f, rnd, 0.5f, true, 0.01f);
            return Normalize(d, 0.8f);
        }

        /// <summary>A wet splat (coffee on a shirt, cream in a face).</summary>
        public static float[] Splat()
        {
            var rnd = new System.Random(79);
            int len = N(0.45f);
            var d = new float[len];
            var lp = Biquad.LowPass(1800f);
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)Rate;
                if ((i & 31) == 0) lp.SetLowPass(Mathf.Lerp(2200f, 300f, Mathf.Min(1f, t / 0.25f)), 0.8f);
                d[i] = lp.Process((float)(rnd.NextDouble() * 2 - 1)) * Mathf.Min(1f, t / 0.003f) * Mathf.Exp(-t / 0.07f);
            }
            AddTone(d, 0f, 0.12f, 140f, 70f, 0.5f, Wave.Sine, 0.04f, 0.001f);
            return Normalize(d, 0.7f);
        }

        /// <summary>Something thrown across the table: a short airy whoosh.</summary>
        public static float[] Whoosh()
        {
            var rnd = new System.Random(83);
            int len = N(0.5f);
            var d = new float[len];
            var bp = Biquad.BandPass(600f, 1.2f);
            for (int i = 0; i < len; i++)
            {
                float u = i / (float)len;
                if ((i & 15) == 0) bp.SetBandPass(Mathf.Lerp(500f, 1800f, Mathf.Sin(u * Mathf.PI)), 1.3f);
                d[i] = bp.Process((float)(rnd.NextDouble() * 2 - 1)) * Mathf.Sin(u * Mathf.PI);
            }
            return Normalize(d, 0.4f);
        }

        /// <summary>A paper plane gliding: soft fluttering paper (~0.9 s).</summary>
        public static float[] PaperFlutter()
        {
            var rnd = new System.Random(89);
            int len = N(0.9f);
            var d = new float[len];
            var bp = Biquad.BandPass(2400f, 0.9f);
            for (int i = 0; i < len; i++)
            {
                float u = i / (float)len, t = i / (float)Rate;
                float flap = 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(t * 2f * Mathf.PI * 19f));
                d[i] = bp.Process((float)(rnd.NextDouble() * 2 - 1)) * flap * Mathf.Sin(u * Mathf.PI);
            }
            return Normalize(d, 0.32f);
        }

        /// <summary>A post-it slapped onto a forehead.</summary>
        public static float[] Slap()
        {
            var rnd = new System.Random(97);
            var d = new float[N(0.18f)];
            AddNoise(d, 0f, 0.05f, 0.7f, rnd, 0.6f, true, 0.012f, 0.0003f);
            AddTone(d, 0f, 0.06f, 320f, 210f, 0.3f, Wave.Sine, 0.02f, 0.0005f);
            return Normalize(d, 0.55f);
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

        /// <summary>
        /// v5 bot talk: a bean-like babble (~3.3 s, cut to the line's length): quick voiced syllables with a pitch glide and
        /// a vowel colour (two formant bumps over the harmonics), a short gap now and then between "words". Played at a
        /// per-colleague pitch, so every bot has its own voice.
        /// </summary>
        public static float[] Mumble(int variant)
        {
            var rnd = new System.Random(4100 + variant * 37);
            float total = 3.3f;
            var d = new float[N(total)];
            float[] f1 = { 700f, 420f, 320f, 560f, 360f }, f2 = { 1200f, 2000f, 850f, 1750f, 2300f };
            float t = 0.02f;
            double ph = 0;
            while (t < total - 0.18f)
            {
                float syl = 0.075f + (float)rnd.NextDouble() * 0.085f;
                int v = rnd.Next(f1.Length);
                float a = 235f * (0.88f + 0.28f * (float)rnd.NextDouble());
                float b = a * (0.82f + 0.32f * (float)rnd.NextDouble());
                int s0 = N(t), len = N(syl);
                float c1 = f1[v], c2 = f2[v];
                for (int i = 0; i < len && s0 + i < d.Length; i++)
                {
                    float u = i / (float)len;
                    float f0 = Mathf.Lerp(a, b, u);
                    ph += f0 / Rate;
                    float env = Mathf.Sin(Mathf.PI * Mathf.Min(1f, u * 1.1f));
                    env *= env;
                    float x = 0f;
                    for (int h = 1; h <= 9; h++)
                    {
                        float fh = f0 * h;
                        float w = Bump(fh, c1) + 0.55f * Bump(fh, c2) + (h == 1 ? 0.35f : 0f);
                        x += (float)Math.Sin(TwoPi * (ph * h - Math.Floor(ph * h))) * w;
                    }
                    d[s0 + i] += x * env * 0.16f;
                }
                t += syl + (rnd.NextDouble() < 0.16 ? 0.1f + (float)rnd.NextDouble() * 0.06f : 0.012f);
            }
            return Normalize(d, 0.5f);
        }

        static float Bump(float f, float centre)
        {
            float x = (f - centre) / (centre * 0.32f);
            return Mathf.Exp(-x * x);
        }
    }
}
