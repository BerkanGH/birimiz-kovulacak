using UnityEngine;
using UnityEngine.UIElements;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Full-screen black fade above every screen (office ↔ meeting room switches). UIRoot attaches it last and ticks
    /// it; anyone can ask for a fade with <see cref="To"/>.
    /// </summary>
    public static class ScreenFade
    {
        static VisualElement _el;
        static float _alpha, _target, _speed = 4f;

        public static float Alpha => _alpha;

        public static void Attach(VisualElement root)
        {
            _el = UiKit.Box("screen-fade").NoPick();
            _el.style.position = Position.Absolute;
            _el.style.left = 0; _el.style.top = 0; _el.style.right = 0; _el.style.bottom = 0;
            _el.style.backgroundColor = new Color(0.03f, 0.035f, 0.06f, 1f);
            root.Add(_el);
            Apply();
        }

        /// <summary>Fade to <paramref name="alpha"/> (0 clear, 1 black) over <paramref name="seconds"/>.</summary>
        public static void To(float alpha, float seconds)
        {
            _target = Mathf.Clamp01(alpha);
            _speed = seconds <= 0.001f ? 1000f : 1f / seconds;
            if (seconds <= 0.001f) { _alpha = _target; Apply(); }
        }

        public static void Tick(float dt)
        {
            if (Mathf.Approximately(_alpha, _target)) return;
            _alpha = Mathf.MoveTowards(_alpha, _target, dt * _speed);
            Apply();
        }

        static void Apply()
        {
            if (_el == null) return;
            _el.style.opacity = _alpha;
            _el.style.display = _alpha > 0.002f ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
