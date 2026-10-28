using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Component kit for UI Toolkit trees built in code ("kurumsal hiciv": warm paper, manila folders, sticky notes,
    /// rubber stamps). Everything renders with USS only; when the 2D art kit is present (UiArt) the same classes pick
    /// up 9-slice skins through the root ".art" class. See docs/UI.md for the class reference.
    /// </summary>
    public static class UiKit
    {
        // ------------------------------------------------------------------ fonts
        public static Font BodyFont, BoldFont, TitleFont;   // from the scene (Nunito / Baloo 2)
        public static Font HandFont, MonoFont, MonoBoldFont, StampFont; // Resources/ui/fonts (Patrick Hand / Courier Prime / Bebas Neue)

        public static void LoadExtraFonts()
        {
            HandFont = Resources.Load<Font>("ui/fonts/PatrickHand-Regular");
            MonoFont = Resources.Load<Font>("ui/fonts/CourierPrime-Regular");
            MonoBoldFont = Resources.Load<Font>("ui/fonts/CourierPrime-Bold");
            StampFont = Resources.Load<Font>("ui/fonts/BebasNeue-Regular");
            if (HandFont == null) HandFont = BoldFont;
            if (MonoFont == null) MonoFont = BodyFont;
            if (MonoBoldFont == null) MonoBoldFont = MonoFont;
            if (StampFont == null) StampFont = TitleFont;
        }

        public static void UseFont(VisualElement e, Font f)
        {
            if (f != null) e.style.unityFontDefinition = new StyleFontDefinition(f);
        }

        // ------------------------------------------------------------------ palette
        public static readonly Color Paper = new Color32(0xF4, 0xEF, 0xE3, 255);
        public static readonly Color Manila = new Color32(0xE8, 0xC9, 0x8A, 255);
        public static readonly Color Green = new Color32(0x2F, 0x6B, 0x4F, 255);
        public static readonly Color Red = new Color32(0xD2, 0x38, 0x2F, 255);
        public static readonly Color Navy = new Color32(0x1F, 0x2A, 0x44, 255);
        public static readonly Color Sticky = new Color32(0xFF, 0xE0, 0x66, 255);
        public static readonly Color Teal = new Color32(0x3B, 0xB9, 0xB0, 255);

        /// <summary>The six character looks: Mavi, Sarı, Pembe, Yeşil, Turuncu, Mor (matches portraits/player_N).</summary>
        public static readonly Color[] PlayerColors =
        {
            new Color32(0x3B, 0x86, 0xD8, 255),
            new Color32(0xF2, 0xC2, 0x30, 255),
            new Color32(0xEE, 0x7B, 0xA6, 255),
            new Color32(0x3F, 0xA8, 0x63, 255),
            new Color32(0xF0, 0x86, 0x2E, 255),
            new Color32(0x8A, 0x60, 0xCF, 255),
        };

        public static Color PlayerColor(int id) { return id >= 0 && id < PlayerColors.Length ? PlayerColors[id] : new Color32(0x8A, 0x90, 0xA6, 255); }

        // ------------------------------------------------------------------ builders
        public static T Cls<T>(this T e, params string[] classes) where T : VisualElement
        {
            for (int i = 0; i < classes.Length; i++) if (!string.IsNullOrEmpty(classes[i])) e.AddToClassList(classes[i]);
            return e;
        }

        public static T AddTo<T>(this T e, VisualElement parent) where T : VisualElement
        {
            parent.Add(e);
            return e;
        }

        public static T NoPick<T>(this T e) where T : VisualElement
        {
            e.pickingMode = PickingMode.Ignore;
            return e;
        }

        public static VisualElement Box(params string[] classes) { return new VisualElement().Cls(classes); }

        /// <summary>Paper / folder / sticky / dark panel (9-slice scale set here because it is per kind).</summary>
        public static VisualElement Panel(string kind, params string[] classes)
        {
            var p = Box(kind).Cls(classes);
            p.style.unitySliceScale = kind == "panel-folder" ? 0.5f : kind == "panel-sticky" ? 0.4f : 0.6f;
            return p;
        }

        public static Label Text(string text, params string[] classes)
        {
            var l = new Label(text).Cls(classes);
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        public static Label Bold(string text, params string[] classes) { var l = Text(text, classes); UseFont(l, BoldFont); return l; }
        public static Label Title(string text, params string[] classes) { var l = Text(text, classes); UseFont(l, TitleFont); return l; }
        public static Label Hand(string text, params string[] classes) { var l = Text(text, classes).Cls("t-hand"); UseFont(l, HandFont); return l; }
        public static Label Memo(string text, params string[] classes) { var l = Text(text, classes).Cls("t-mono"); UseFont(l, MonoFont); return l; }
        public static Label MemoBold(string text, params string[] classes) { var l = Text(text, classes).Cls("t-mono"); UseFont(l, MonoBoldFont); return l; }
        public static Label StampText(string text, params string[] classes) { var l = Text(text, classes).Cls("t-stamp"); UseFont(l, StampFont); return l; }

        static bool HasVariant(string[] classes)
        {
            for (int i = 0; i < classes.Length; i++)
            {
                string c = classes[i];
                if (c == "btn-primary" || c == "btn-secondary" || c == "btn-danger" || c == "btn-ghost" || c == "btn-dark") return true;
            }
            return false;
        }

        /// <summary>Button. Variants: btn-primary (green), btn-secondary (manila, default), btn-danger (red), btn-ghost, btn-dark; sizes btn-small / btn-big.</summary>
        public static Button Btn(string text, Action onClick, params string[] classes)
        {
            var b = new Button(() => { Sfx.Play("click"); onClick?.Invoke(); }) { text = text }.Cls("btn").Cls(classes);
            if (!HasVariant(classes)) b.AddToClassList("btn-secondary");
            UseFont(b, BoldFont);
            b.style.unitySliceScale = 0.5f;
            return b;
        }

        /// <summary>Button with a leading icon (UiArt id, falls back to a glyph).</summary>
        public static Button IconBtn(string iconId, string text, Action onClick, params string[] classes)
        {
            var b = Btn("", onClick, classes).Cls("btn-icon");
            Icon(iconId, "btn-icon-img").AddTo(b);
            if (!string.IsNullOrEmpty(text)) Bold(text, "btn-label").NoPick().AddTo(b);
            return b;
        }

        /// <summary>Square icon from the art kit (icons/&lt;id&gt;); without art a small rounded tile with a glyph.</summary>
        public static VisualElement Icon(string iconId, params string[] classes)
        {
            var e = Box("icon").Cls(classes).NoPick();
            SetIcon(e, iconId);
            return e;
        }

        public static void SetIcon(VisualElement e, string iconId)
        {
            bool ok = !string.IsNullOrEmpty(iconId) && UiArt.Apply(e, "icons/" + iconId);
            e.EnableInClassList("icon-fallback", !ok);
            if (!ok)
            {
                e.style.backgroundImage = StyleKeyword.Null;
                Label glyph = e.childCount > 0 ? e[0] as Label : null;
                if (glyph == null) { glyph = Bold("", "icon-glyph").NoPick(); e.Add(glyph); }
                glyph.text = UiArt.FallbackGlyph(iconId);
            }
            else if (e.childCount > 0 && e[0] is Label l && l.ClassListContains("icon-glyph")) l.text = "";
        }

        /// <summary>Player badge: portrait (portraits/player_N) in a ring of the player's colour; fallback = coloured disc with initial.</summary>
        public static VisualElement Badge(int playerId, string sizeClass = "badge-m")
        {
            var b = Box("pbadge", sizeClass).NoPick();
            SetBadge(b, playerId, null);
            return b;
        }

        public static void SetBadge(VisualElement b, int playerId, string name)
        {
            b.style.borderTopColor = b.style.borderBottomColor = b.style.borderLeftColor = b.style.borderRightColor = PlayerColor(playerId);
            bool art = playerId >= 0 && UiArt.Apply(b, "portraits/player_" + playerId);
            if (!art && playerId < 0) art = UiArt.Apply(b, "portraits/player_unknown");
            b.EnableInClassList("pbadge-fallback", !art);
            if (!art)
            {
                b.style.backgroundImage = StyleKeyword.Null;
                b.style.backgroundColor = PlayerColor(playerId);
                Label init = b.Q<Label>(className: "pbadge-initial");
                if (init == null) { init = Bold("", "pbadge-initial").NoPick(); b.Add(init); }
                string n = string.IsNullOrEmpty(name) ? Loc.T("player.default." + playerId) : name;
                init.text = string.IsNullOrEmpty(n) ? "?" : n.Substring(0, 1).ToUpper(Loc.Culture);
            }
            else b.style.backgroundColor = Paper;
        }

        /// <summary>Manager portrait (portraits/mgr_&lt;profile&gt;[_angry]); fallback = navy disc with the profile initial.</summary>
        public static VisualElement ManagerPortrait(string profileId, bool angry, string sizeClass = "badge-l")
        {
            var b = Box("pbadge", "mgr-badge", sizeClass).NoPick();
            SetManagerPortrait(b, profileId, angry);
            return b;
        }

        public static void SetManagerPortrait(VisualElement b, string profileId, bool angry)
        {
            string id = string.IsNullOrEmpty(profileId) ? "" : profileId;
            bool art = id.Length > 0 && (angry && UiArt.Apply(b, "portraits/mgr_" + id + "_angry") || UiArt.Apply(b, "portraits/mgr_" + id));
            if (!art) art = UiArt.Apply(b, "icons/manager");
            b.EnableInClassList("pbadge-fallback", !art);
            if (!art)
            {
                b.style.backgroundImage = StyleKeyword.Null;
                b.style.backgroundColor = Navy;
                Label init = b.Q<Label>(className: "pbadge-initial");
                if (init == null) { init = Bold("", "pbadge-initial").NoPick(); b.Add(init); }
                init.text = "M";
            }
        }

        /// <summary>Legacy coloured dot (v1 API).</summary>
        public static VisualElement Chip(int playerId, bool big = false)
        {
            var c = Box("chip").NoPick();
            if (big) c.AddToClassList("chip-big");
            c.style.backgroundColor = PlayerColor(playerId);
            return c;
        }

        public static Label KeyCap(string key, params string[] classes)
        {
            var k = Bold(key, "keycap").Cls(classes).NoPick();
            k.style.whiteSpace = WhiteSpace.NoWrap;
            return k;
        }

        public static Label Tag(string text, params string[] classes) { return Bold(text, "tag").Cls(classes).NoPick(); }

        public static VisualElement Bar(float fraction, out VisualElement fill, params string[] classes)
        {
            var bar = Box("bar").Cls(classes);
            fill = Box("bar-fill").AddTo(bar);
            fill.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100f);
            return bar;
        }

        public static void SetFill(VisualElement fill, float fraction)
        {
            fill.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100f);
        }

        /// <summary>Suspicion pips (0..max).</summary>
        public static VisualElement Pips(int level, int max = 4)
        {
            var p = Box("pips").NoPick();
            for (int i = 0; i < max; i++) Box("pip").NoPick().AddTo(p);
            SetPips(p, level);
            return p;
        }

        public static void SetPips(VisualElement pips, int level)
        {
            for (int i = 0; i < pips.childCount; i++)
            {
                pips[i].EnableInClassList("pip-on", i < level);
                pips[i].EnableInClassList("pip-hot", i < level && level >= 3);
            }
        }

        public static VisualElement Section(VisualElement parent, string title, string iconId = null)
        {
            var h = Box("section-head").AddTo(parent);
            if (iconId != null) Icon(iconId, "icon-s").AddTo(h);
            Bold(title, "section-title").AddTo(h);
            return h;
        }

        /// <summary>Keeps <paramref name="target"/> at aspect w/h, as large as fits inside <paramref name="container"/> (fixed-aspect art: letter, projector).</summary>
        public static void KeepAspect(VisualElement container, VisualElement target, float aspect, float reserveHeight = 0f)
        {
            container.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float w = container.resolvedStyle.width, h = container.resolvedStyle.height - reserveHeight;
                if (w <= 1f || h <= 1f) return;
                float tw = w, th = w / aspect;
                if (th > h) { th = h; tw = h * aspect; }
                target.style.width = tw;
                target.style.height = th;
            });
        }

        public static Label Empty(VisualElement parent, string text) { return Text(text, "empty").AddTo(parent); }

        public static void Show(this VisualElement e, bool visible)
        {
            var d = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (e.style.display != d) e.style.display = d;
        }

        public static bool IsShown(this VisualElement e) { return e.style.display != DisplayStyle.None && e.resolvedStyle.display != DisplayStyle.None; }

        /// <summary>Assigns only when different (avoids re-layout).</summary>
        public static void SetText(this Label l, string s)
        {
            if (!string.Equals(l.text, s, StringComparison.Ordinal)) l.text = s;
        }

        public static void SetText(this Button b, string s)
        {
            if (!string.Equals(b.text, s, StringComparison.Ordinal)) b.text = s;
        }

        public static void Toggle(this VisualElement e, string cls, bool on) { e.EnableInClassList(cls, on); }

        /// <summary>Plays the short "appear" transition (USS .pop-start -> normal).</summary>
        public static void Pop(VisualElement e, string startClass = "pop-start")
        {
            e.AddToClassList(startClass);
            e.schedule.Execute(() => e.RemoveFromClassList(startClass)).StartingIn(16);
        }

        // ------------------------------------------------------------------ cached clock strings (no per-frame allocations)
        static string[] _clock;
        static string[] _seconds;

        /// <summary>"m:ss", ceil; cached for 0..59:59.</summary>
        public static string Clock(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds)) seconds = 0;
            int s = (int)Math.Ceiling(seconds);
            return ClockInt(s);
        }

        public static string ClockInt(int s)
        {
            if (s < 0) s = 0;
            if (_clock == null) _clock = new string[3600];
            if (s >= _clock.Length) return (s / 60).ToString("0") + ":" + (s % 60).ToString("00");
            return _clock[s] ?? (_clock[s] = (s / 60).ToString("0") + ":" + (s % 60).ToString("00"));
        }

        /// <summary>"12" for 0..999 (cached).</summary>
        public static string Num(int n)
        {
            if (_seconds == null) _seconds = new string[1000];
            if (n < 0 || n >= _seconds.Length) return n.ToString();
            return _seconds[n] ?? (_seconds[n] = n.ToString());
        }

        // ------------------------------------------------------------------ screens

        /// <summary>Base for full-screen panels: snappy fade/scale-in on show, instant hide.</summary>
        public abstract class Screen
        {
            protected readonly VisualElement Root;
            public bool Visible { get; private set; }
            public VisualElement Element => Root;

            protected Screen(VisualElement parent, params string[] classes)
            {
                Root = Box("screen").Cls(classes).AddTo(parent);
                Root.style.display = DisplayStyle.None;
            }

            public void SetVisible(bool v)
            {
                if (v == Visible) return;
                Visible = v;
                Root.Show(v);
                if (v)
                {
                    Pop(Root, "screen-enter");
                    OnShow();
                }
                else OnHide();
            }

            protected virtual void OnShow() { }
            protected virtual void OnHide() { }
            public virtual void Tick() { }
        }

        /// <summary>Simple tab strip: one active button at a time.</summary>
        public sealed class TabBar
        {
            public readonly VisualElement Root;
            readonly List<Button> _buttons = new List<Button>();
            readonly List<int> _ids = new List<int>();
            readonly Action<int> _onSelect;
            public int Active { get; private set; } = -1;

            public TabBar(VisualElement parent, Action<int> onSelect, params string[] classes)
            {
                Root = Box("tabs").Cls(classes).AddTo(parent);
                _onSelect = onSelect;
            }

            public Button Add(int id, string text, string iconId = null)
            {
                Button b = iconId != null ? IconBtn(iconId, text, () => Select(id), "tab") : Btn(text, () => Select(id), "tab");
                b.RemoveFromClassList("btn-secondary");
                b.AddTo(Root);
                _buttons.Add(b);
                _ids.Add(id);
                if (Active < 0) Select(id, false);
                return b;
            }

            public void SetTabVisible(int id, bool visible)
            {
                int i = _ids.IndexOf(id);
                if (i >= 0) _buttons[i].Show(visible);
            }

            public void Select(int id, bool notify = true)
            {
                Active = id;
                for (int i = 0; i < _buttons.Count; i++) _buttons[i].EnableInClassList("tab-active", _ids[i] == id);
                if (notify) _onSelect?.Invoke(id);
            }

            public Button ButtonFor(int id) { int i = _ids.IndexOf(id); return i >= 0 ? _buttons[i] : null; }
        }
    }
}
