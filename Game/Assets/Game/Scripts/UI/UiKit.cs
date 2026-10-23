using System;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace BirimizKovulacak.Game
{
    /// <summary>Small helpers for building UI Toolkit trees in code.</summary>
    public static class UiKit
    {
        public static Font BodyFont, BoldFont, TitleFont;

        public static readonly Color[] PlayerColors =
        {
            new Color32(74, 144, 226, 255),
            new Color32(245, 200, 66, 255),
            new Color32(242, 138, 178, 255),
            new Color32(108, 197, 106, 255),
        };

        public static Color PlayerColor(int id) { return id >= 0 && id < PlayerColors.Length ? PlayerColors[id] : Color.gray; }

        public static T Cls<T>(this T e, params string[] classes) where T : VisualElement
        {
            foreach (var c in classes) if (!string.IsNullOrEmpty(c)) e.AddToClassList(c);
            return e;
        }

        public static T AddTo<T>(this T e, VisualElement parent) where T : VisualElement
        {
            parent.Add(e);
            return e;
        }

        public static VisualElement Box(params string[] classes) { return new VisualElement().Cls(classes); }

        public static Label Text(string text, params string[] classes)
        {
            var l = new Label(text).Cls(classes);
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        public static Label Bold(string text, params string[] classes)
        {
            var l = Text(text, classes);
            if (BoldFont != null) l.style.unityFontDefinition = new StyleFontDefinition(BoldFont);
            return l;
        }

        public static Label Title(string text, params string[] classes)
        {
            var l = Text(text, classes);
            if (TitleFont != null) l.style.unityFontDefinition = new StyleFontDefinition(TitleFont);
            return l;
        }

        public static Button Btn(string text, Action onClick, params string[] classes)
        {
            var b = new Button(() => { Sfx.Play("click"); onClick?.Invoke(); }) { text = text }.Cls("btn").Cls(classes);
            if (BoldFont != null) b.style.unityFontDefinition = new StyleFontDefinition(BoldFont);
            return b;
        }

        public static VisualElement Chip(int playerId, bool big = false)
        {
            var c = Box("chip");
            if (big) c.AddToClassList("chip-big");
            c.style.backgroundColor = PlayerColor(playerId);
            return c;
        }

        public static void Show(this VisualElement e, bool visible)
        {
            e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public static bool IsShown(this VisualElement e) { return e.resolvedStyle.display != DisplayStyle.None && e.style.display != DisplayStyle.None; }

        public static VisualElement Bar(float fraction, out VisualElement fill, params string[] classes)
        {
            var bar = Box("bar").Cls(classes);
            fill = Box("bar-fill").AddTo(bar);
            fill.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100f);
            return bar;
        }

        public static VisualElement Pips(int level, int max = 4)
        {
            var p = Box("pips");
            for (int i = 0; i < max; i++) Box("pip", i < level ? "pip-on" : null).AddTo(p);
            return p;
        }

        public static string Clock(double seconds)
        {
            if (seconds < 0) seconds = 0;
            int s = (int)Math.Ceiling(seconds);
            return (s / 60).ToString("0") + ":" + (s % 60).ToString("00");
        }

        /// <summary>Base for full-screen panels.</summary>
        public abstract class Screen
        {
            protected readonly VisualElement Root;
            public bool Visible { get; private set; }
            protected Screen(VisualElement parent, params string[] classes)
            {
                Root = Box("screen").Cls(classes).AddTo(parent);
                Root.Show(false);
            }
            public void SetVisible(bool v)
            {
                if (v == Visible) return;
                Visible = v;
                Root.Show(v);
                if (v) OnShow();
            }
            protected virtual void OnShow() { }
            public virtual void Tick() { }
        }
    }
}
