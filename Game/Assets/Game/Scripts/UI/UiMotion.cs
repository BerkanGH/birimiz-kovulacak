using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Small tween toolkit for UI Toolkit (v3 motion pass): every screen and card enters with motion, stamps slam,
    /// numbers count up. Tweens run on the element's scheduler with unscaled time and stop when the element leaves the
    /// panel; starting a new tween of the same channel on an element replaces the old one. USS transitions stay the
    /// right tool for hover/press states; use this for one-shot entrances and reactions.
    /// </summary>
    public static class UiMotion
    {
        public enum Ease { Linear, OutCubic, InCubic, InOutCubic, OutBack, OutElastic, OutBounce }

        public static float Apply(Ease ease, float t)
        {
            t = Mathf.Clamp01(t);
            switch (ease)
            {
                case Ease.OutCubic: { float u = 1f - t; return 1f - u * u * u; }
                case Ease.InCubic: return t * t * t;
                case Ease.InOutCubic: return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
                case Ease.OutBack: { const float c1 = 1.70158f, c3 = c1 + 1f; float u = t - 1f; return 1f + c3 * u * u * u + c1 * u * u; }
                case Ease.OutElastic:
                    if (t <= 0f || t >= 1f) return t;
                    return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * (2f * Mathf.PI / 3f)) + 1f;
                case Ease.OutBounce:
                {
                    const float n1 = 7.5625f, d1 = 2.75f;
                    if (t < 1f / d1) return n1 * t * t;
                    if (t < 2f / d1) { t -= 1.5f / d1; return n1 * t * t + 0.75f; }
                    if (t < 2.5f / d1) { t -= 2.25f / d1; return n1 * t * t + 0.9375f; }
                    t -= 2.625f / d1; return n1 * t * t + 0.984375f;
                }
                default: return t;
            }
        }

        sealed class Running { public IVisualElementScheduledItem Item; }
        // Weak per-element table: a removed element (whose scheduler stops) never keeps tween state alive.
        static readonly ConditionalWeakTable<VisualElement, Dictionary<string, Running>> _running = new ConditionalWeakTable<VisualElement, Dictionary<string, Running>>();

        /// <summary>
        /// Runs <paramref name="step"/> with eased progress 0..1 over <paramref name="seconds"/> (after <paramref name="delay"/>).
        /// <paramref name="channel"/> groups tweens that must not overlap on one element (e.g. "move", "fade").
        /// </summary>
        public static void Tween(VisualElement e, float seconds, Action<float> step, Ease ease = Ease.OutCubic, Action done = null,
            float delay = 0f, string channel = "main")
        {
            if (e == null || step == null) return;
            Stop(e, channel);
            float start = Time.unscaledTime + Mathf.Max(0f, delay);
            float dur = Mathf.Max(0.0001f, seconds);
            var run = new Running();
            _running.GetOrCreateValue(e)[channel] = run;
            if (delay <= 0f) step(Apply(ease, 0f));
            run.Item = e.schedule.Execute(() =>
            {
                float now = Time.unscaledTime;
                if (now < start) return;
                float t = (now - start) / dur;
                if (e.panel == null) t = 1f; // detached: jump to the end
                step(Apply(ease, t));
                if (t >= 1f)
                {
                    run.Item.Pause();
                    Dictionary<string, Running> map;
                    Running cur;
                    if (_running.TryGetValue(e, out map) && map.TryGetValue(channel, out cur) && cur == run) map.Remove(channel);
                    done?.Invoke();
                }
            }).Every(16);
        }

        public static void Stop(VisualElement e, string channel = "main")
        {
            Dictionary<string, Running> map;
            Running r;
            if (e != null && _running.TryGetValue(e, out map) && map.TryGetValue(channel, out r))
            {
                r.Item?.Pause();
                map.Remove(channel);
            }
        }

        // ------------------------------------------------------------------ presets

        public static void FadeIn(VisualElement e, float seconds = 0.2f, float delay = 0f)
        {
            if (e == null) return;
            e.style.opacity = 0f;
            Tween(e, seconds, t => e.style.opacity = t, Ease.OutCubic, () => e.style.opacity = StyleKeyword.Null, delay, "fade");
        }

        public static void FadeOut(VisualElement e, float seconds = 0.18f, bool hideAfter = true, Action done = null)
        {
            if (e == null) return;
            float from = e.resolvedStyle.opacity;
            Tween(e, seconds, t => e.style.opacity = Mathf.Lerp(from, 0f, t), Ease.InCubic, () =>
            {
                if (hideAfter) e.style.display = DisplayStyle.None;
                e.style.opacity = StyleKeyword.Null;
                done?.Invoke();
            }, 0f, "fade");
        }

        /// <summary>Slides in from an offset (px) while fading in.</summary>
        public static void SlideIn(VisualElement e, Vector2 from, float seconds = 0.28f, float delay = 0f, Ease ease = Ease.OutCubic)
        {
            if (e == null) return;
            e.style.opacity = delay > 0f ? 0f : 1f;
            Tween(e, seconds, t =>
            {
                e.style.translate = new Translate(from.x * (1f - t), from.y * (1f - t));
                e.style.opacity = Mathf.Clamp01(t * 2.5f);
            }, ease, () => { e.style.translate = StyleKeyword.Null; e.style.opacity = StyleKeyword.Null; }, delay, "move");
        }

        /// <summary>Scale pop (0.85 → 1 with overshoot) + fade.</summary>
        public static void PopIn(VisualElement e, float seconds = 0.26f, float delay = 0f, float fromScale = 0.85f)
        {
            if (e == null) return;
            e.style.opacity = 0f;
            Tween(e, seconds, t =>
            {
                float s = Mathf.LerpUnclamped(fromScale, 1f, t);
                e.style.scale = new Scale(new Vector3(s, s, 1f));
                e.style.opacity = Mathf.Clamp01(t * 3f);
            }, Ease.OutBack, () => { e.style.scale = StyleKeyword.Null; e.style.opacity = StyleKeyword.Null; }, delay, "scale");
        }

        /// <summary>Rubber stamp: big, tilted and transparent → slams to rest with a small bounce. <paramref name="onImpact"/> fires at contact (play a thud).</summary>
        public static void Stamp(VisualElement e, float restRotation = -10f, float seconds = 0.22f, float delay = 0f, Action onImpact = null)
        {
            if (e == null) return;
            e.style.opacity = 0f;
            Tween(e, seconds, t =>
            {
                float s = Mathf.Lerp(2.6f, 1f, t);
                e.style.scale = new Scale(new Vector3(s, s, 1f));
                e.style.rotate = new Rotate(new Angle(Mathf.Lerp(restRotation - 22f, restRotation, t), AngleUnit.Degree));
                e.style.opacity = Mathf.Clamp01(t * 1.6f);
            }, Ease.InCubic, () =>
            {
                onImpact?.Invoke();
                Tween(e, 0.16f, u =>
                {
                    float s = 1f + 0.06f * Mathf.Sin(u * Mathf.PI);
                    e.style.scale = new Scale(new Vector3(s, s, 1f));
                }, Ease.OutCubic, null, 0f, "scale");
            }, delay, "scale");
        }

        /// <summary>Horizontal shake (wrong input, ringing phone, warning).</summary>
        public static void Shake(VisualElement e, float amplitude = 10f, float seconds = 0.35f, float frequency = 7f)
        {
            if (e == null) return;
            Tween(e, seconds, t =>
            {
                float decay = 1f - t;
                e.style.translate = new Translate(Mathf.Sin(t * seconds * frequency * 2f * Mathf.PI) * amplitude * decay, 0f);
            }, Ease.Linear, () => e.style.translate = StyleKeyword.Null, 0f, "move");
        }

        /// <summary>Quick attention pulse (scale up and back).</summary>
        public static void Pulse(VisualElement e, float amount = 0.08f, float seconds = 0.3f)
        {
            if (e == null) return;
            Tween(e, seconds, t =>
            {
                float s = 1f + amount * Mathf.Sin(t * Mathf.PI);
                e.style.scale = new Scale(new Vector3(s, s, 1f));
            }, Ease.OutCubic, () => e.style.scale = StyleKeyword.Null, 0f, "scale");
        }

        /// <summary>Counts a number on a label from → to (format gets the current integer).</summary>
        public static void CountUp(Label l, int from, int to, float seconds = 0.6f, Func<int, string> format = null, float delay = 0f)
        {
            if (l == null) return;
            Func<int, string> f = format ?? (n => n.ToString());
            l.text = f(from);
            Tween(l, seconds, t => l.text = f(Mathf.RoundToInt(Mathf.Lerp(from, to, t))), Ease.OutCubic, () => l.text = f(to), delay, "count");
        }

        /// <summary>Card flip around the vertical axis; <paramref name="atMiddle"/> swaps the content when edge-on.</summary>
        public static void Flip(VisualElement e, Action atMiddle, float seconds = 0.36f, float delay = 0f)
        {
            if (e == null) return;
            bool swapped = false;
            Tween(e, seconds, t =>
            {
                float sx = Mathf.Abs(Mathf.Cos(t * Mathf.PI));
                if (!swapped && t >= 0.5f) { swapped = true; atMiddle?.Invoke(); }
                e.style.scale = new Scale(new Vector3(Mathf.Max(0.001f, sx), 1f, 1f));
            }, Ease.InOutCubic, () => e.style.scale = StyleKeyword.Null, delay, "scale");
        }

        /// <summary>Runs <paramref name="each"/> for every element with a growing delay (lists, card fans).</summary>
        public static void Stagger(IEnumerable<VisualElement> elements, float step, Action<VisualElement, float> each)
        {
            if (elements == null || each == null) return;
            float d = 0f;
            foreach (var e in elements) { each(e, d); d += step; }
        }
    }
}
