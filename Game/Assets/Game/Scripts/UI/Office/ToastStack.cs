using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// The short notes in the top-right corner of the office and the meeting room (texts, prim received, missed calls,
    /// phase changes, …): newest on top, at most four, the same note again only stays up longer.
    /// Styles: office.uss (.of-toasts, .of-toast, .of-toast-&lt;kind&gt;).
    /// </summary>
    public sealed class ToastStack
    {
        readonly VisualElement _host;
        readonly List<KeyValuePair<VisualElement, float>> _list = new List<KeyValuePair<VisualElement, float>>();

        public VisualElement Host => _host;

        public ToastStack(VisualElement parent)
        {
            _host = UiKit.Box("of-toasts").NoPick().AddTo(parent);
        }

        public void Show(string text, string kind = null, float seconds = 5f)
        {
            if (string.IsNullOrEmpty(text)) return;
            // the same message still on screen: keep that one up a little longer instead of stacking a copy
            string key = text + "|" + kind;
            for (int i = 0; i < _list.Count; i++)
            {
                var old = _list[i];
                if (!(old.Key.userData is string k) || k != key) continue;
                _list[i] = new KeyValuePair<VisualElement, float>(old.Key, Mathf.Max(old.Value, Time.unscaledTime + seconds));
                old.Key.style.opacity = 1f;
                UiMotion.Pulse(old.Key, 0.04f, 0.2f);
                return;
            }
            var el = UiKit.Box("of-toast").NoPick();
            el.userData = key;
            if (!string.IsNullOrEmpty(kind)) el.AddToClassList(kind);
            UiKit.Box("of-toast-bar").NoPick().AddTo(el);
            UiKit.Text(text, "of-toast-text").NoPick().AddTo(el);
            _host.Insert(0, el);
            _list.Add(new KeyValuePair<VisualElement, float>(el, Time.unscaledTime + seconds));
            UiMotion.SlideIn(el, new Vector2(60f, 0f), 0.25f, 0f, UiMotion.Ease.OutCubic);
            while (_list.Count > 4)
            {
                _list[0].Key.RemoveFromHierarchy();
                _list.RemoveAt(0);
            }
        }

        public void Tick()
        {
            float now = Time.unscaledTime;
            for (int i = _list.Count - 1; i >= 0; i--)
            {
                var t = _list[i];
                float left = t.Value - now;
                if (left <= 0f) { t.Key.RemoveFromHierarchy(); _list.RemoveAt(i); continue; }
                if (left < 0.35f) t.Key.style.opacity = left / 0.35f;
            }
        }

        public void Clear()
        {
            foreach (var t in _list) t.Key.RemoveFromHierarchy();
            _list.Clear();
        }
    }
}
