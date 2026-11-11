using System;
using System.Collections.Generic;
using System.Text;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Builder kit shared by the v5 meeting screen (<see cref="PlayUi"/>) and the office (computer apps, desk phone):
    /// play-styled buttons, text helpers, live labels and the content-diffed <see cref="PlaySection"/>. Consumers
    /// import it with <c>using static PlayKit</c> (it re-exports the UiKit label helpers so the two kits never clash).
    /// </summary>
    public static class PlayKit
    {
        /// <summary>Focus states: informational, something still to do (orange), done (green).</summary>
        public const int Info = 0, Todo = 1, Done = 2;

        public static string T(string key) { return PlayText.T(key); }
        public static string F(string key, params object[] a) { return PlayText.F(key, a); }
        public static string Name(int id) { return PlayText.Name(id); }

        public static VisualElement Box(params string[] classes) { return UiKit.Box(classes); }
        public static Label Text(string text, params string[] classes) { return UiKit.Text(text, classes); }
        public static Label Bold(string text, params string[] classes) { return UiKit.Bold(text, classes); }
        public static Label Title(string text, params string[] classes) { return UiKit.Title(text, classes); }
        public static Label Hand(string text, params string[] classes) { return UiKit.Hand(text, classes); }
        public static Color PlayerColor(int id) { return UiKit.PlayerColor(id); }

        public static void Send(LineVerb verb, int target = LineIds.None, int a = -1, string text = "")
        {
            var lc = LineClient.I;
            if (lc != null) lc.Send(verb, target, a, -1, -1, text);
        }

        public static Button Btn(string text, Action onClick, params string[] cls)
        {
            var b = new Button(() => { Sfx.Play("click"); onClick?.Invoke(); }) { text = text };
            b.enableRichText = false;
            b.AddToClassList("pl-btn");
            foreach (var c in cls) if (!string.IsNullOrEmpty(c)) b.AddToClassList(c);
            return b;
        }

        public static Label Head(VisualElement p, string text) { return Title(text, "pl-title").AddTo(p); }
        public static Label Sub(VisualElement p, string text) { return Bold(text, "pl-sub").AddTo(p); }
        public static Label Line(VisualElement p, string text, params string[] cls) { return Text(text, "pl-text").Cls(cls).AddTo(p); }
        public static Label Big(VisualElement p, string text, params string[] cls) { return Bold(text, "pl-focus-text").Cls(cls).AddTo(p); }
        public static VisualElement Row(VisualElement p) { return Box("pl-row").AddTo(p); }
        public static VisualElement Wrap(VisualElement p) { return Box("pl-wrap").AddTo(p); }
        public static Label Tag(VisualElement p, string text, string cls = null) { return Bold(text, "pl-tag").Cls(cls).AddTo(p); }

        /// <summary>
        /// A label whose text (and classes) <paramref name="apply"/> refreshes at 2 Hz without rebuilding its section.
        /// Its text is not part of the section's content diff: put everything the closure depends on into
        /// <paramref name="key"/>, or let the closure read the current view (<see cref="LineClient.View"/>).
        /// </summary>
        public static Label Live(VisualElement p, string key, double now, Action<Label, double> apply, params string[] cls)
        {
            var l = Text("", "pl-text").Cls(cls).AddTo(p);
            l.userData = new LiveLabel { Key = key, Apply = apply };
            apply(l, now);
            return l;
        }

        public static Label Live(VisualElement p, string key, double now, Func<double, string> text, params string[] cls)
        {
            return Live(p, key, now, (l, t) => l.SetText(text(t)), cls);
        }

        public static string KeyLabel(UnityEngine.InputSystem.InputAction a) { return a != null ? GameInput.KeyLabel(a) : "?"; }

        /// <summary>The player is on the public line board (ringing or talking).</summary>
        public static bool InCall(LineView v, int id)
        {
            foreach (var c in v.LineBoard) if (c.Caller == id || c.Callee == id) return true;
            return false;
        }

        /// <summary>I am a candidate whose desk is sealed (no outgoing transfers), or fired (nothing left to send).</summary>
        public static bool CannotSend(LineClient lc)
        {
            var me = lc.Player(lc.MySlot);
            return me != null && (me.Candidate || me.Fired);
        }
    }

    /// <summary>Marks a label refreshed in place by <see cref="PlaySection.TickLive"/>.</summary>
    public sealed class LiveLabel
    {
        public string Key;
        public Action<Label, double> Apply;
    }

    /// <summary>
    /// A rebuildable block. <see cref="Update"/> builds a fresh subtree and swaps it in only when its rendered
    /// content (element types, classes, texts, enabled state) differs from what is shown. Live labels count only
    /// by their key; <see cref="TickLive"/> refreshes them in place.
    /// </summary>
    public sealed class PlaySection
    {
        readonly VisualElement _holder;
        readonly List<Label> _live = new List<Label>();
        VisualElement _current;
        string _dump = "";

        public PlaySection(VisualElement parent, string cls = "pl-section") { _holder = UiKit.Box(cls).AddTo(parent); }

        public VisualElement Holder => _holder;

        public void Update(Action<VisualElement> build)
        {
            var next = UiKit.Box("pl-section-body");
            try { build(next); }
            catch (Exception e) { Debug.LogException(e); next.Clear(); UiKit.Text("UI: " + e.GetType().Name, "pl-text", "pl-bad").AddTo(next); }
            var sb = new StringBuilder(256);
            Dump(next, sb);
            string dump = sb.ToString();
            if (_current != null && dump == _dump) return;
            _dump = dump;
            _current?.RemoveFromHierarchy();
            _current = next;
            _holder.Add(next);
            _holder.Show(next.childCount > 0);
            _live.Clear();
            Collect(next, _live);
        }

        /// <summary>Forget the shown content (the next <see cref="Update"/> always swaps in).</summary>
        public void Invalidate() { _dump = "\u0000"; }

        public void TickLive(double now)
        {
            for (int i = 0; i < _live.Count; i++)
            {
                var l = _live[i];
                if (l.userData is LiveLabel ll) ll.Apply(l, now);
            }
        }

        static void Collect(VisualElement e, List<Label> into)
        {
            if (e is Label l && l.userData is LiveLabel) into.Add(l);
            foreach (var child in e.Children()) Collect(child, into);
        }

        static void Dump(VisualElement e, StringBuilder sb)
        {
            if (e.userData is LiveLabel ll) { sb.Append("<live:").Append(ll.Key).Append('>'); return; }
            sb.Append('<').Append(e.GetType().Name);
            foreach (var c in e.GetClasses()) sb.Append('.').Append(c);
            if (e is TextElement te) sb.Append('|').Append(te.text);
            if (!e.enabledSelf) sb.Append("|off");
            if (e.style.backgroundColor.keyword == StyleKeyword.Undefined) sb.Append("|bg").Append(e.style.backgroundColor.value.GetHashCode());
            sb.Append(':');
            foreach (var child in e.Children()) Dump(child, sb);
            sb.Append('>');
        }
    }
}
