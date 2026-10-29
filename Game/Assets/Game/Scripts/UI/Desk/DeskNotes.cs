using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    public enum SlipKind { Plain, Good, Bad, Manager, Sticky, Dark }

    /// <summary>
    /// Notifications as paper slips pinned to the top-right corner (memo paper, a pin, slight tilt; the kind shows in
    /// the paper tint and the icon — never a coloured side stripe). Max four at once; click one to act on it.
    /// </summary>
    public sealed class DeskNotes
    {
        sealed class Slip { public VisualElement E; public float Until; public bool Leaving; }

        const int Max = 4;
        readonly DeskUi _ui;
        readonly VisualElement _stack;
        readonly List<Slip> _slips = new List<Slip>();
        int _count;

        public DeskNotes(DeskUi ui, VisualElement layer)
        {
            _ui = ui;
            _stack = Box("dk-notes").NoPick().AddTo(layer);
        }

        public void Post(SlipKind kind, string icon, string title, string sub, int player = LineIds.None, Action onClick = null)
        {
            if (string.IsNullOrEmpty(title)) return;
            int active = _slips.Count - CountLeaving();
            for (int i = 0; i < _slips.Count && active >= Max; i++)
                if (!_slips[i].Leaving) { Dismiss(_slips[i]); active--; }

            var e = Box("dk-slip", "dk-slip-" + kind.ToString().ToLowerInvariant());
            if (kind != SlipKind.Dark) Dk.Skin(e, kind == SlipKind.Sticky || kind == SlipKind.Manager ? "onboarding/sticky_note_9slice" : "computer/notification_card");
            Box("dk-slip-pin").NoPick().AddTo(e);
            if (player >= 0 && player < 6) Dk.Badge(player, "badge-s").AddTo(e).AddToClassList("dk-slip-badge");
            else Dk.Icon(icon, "dk-slip-icon").AddTo(e);
            var col = Box("dk-slip-col").NoPick().AddTo(e);
            Bold(title, "dk-slip-title").NoPick().AddTo(col);
            if (!string.IsNullOrEmpty(sub)) Text(sub, "dk-slip-sub").NoPick().AddTo(col);
            if (onClick == null) e.pickingMode = PickingMode.Ignore;
            Dk.SetTilt(e, Dk.Tilt(++_count, 1.4f));

            var slip = new Slip { E = e, Until = Time.unscaledTime + (kind == SlipKind.Bad ? 7.5f : 5.5f) };
            e.RegisterCallback<ClickEvent>(_ => { onClick?.Invoke(); Dismiss(slip); });
            _stack.Insert(0, e);
            _slips.Add(slip);
            UiMotion.SlideIn(e, new Vector2(80f, -16f), 0.34f, 0f, UiMotion.Ease.OutBack);
            if (kind == SlipKind.Bad) e.schedule.Execute(() => Dk.Shake(e, 8f, 0.4f)).StartingIn(380);
            Sfx.Play("toast", 0.6f);
        }

        int CountLeaving()
        {
            int n = 0;
            for (int i = 0; i < _slips.Count; i++) if (_slips[i].Leaving) n++;
            return n;
        }

        void Dismiss(Slip s)
        {
            if (s.Leaving) return;
            s.Leaving = true;
            var e = s.E;
            UiMotion.Tween(e, 0.22f, t =>
            {
                e.style.translate = new Translate(t * 90f, 0f);
                e.style.opacity = 1f - t;
            }, UiMotion.Ease.InCubic, () => { e.RemoveFromHierarchy(); _slips.Remove(s); }, 0f, "move");
        }

        public void Tick()
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < _slips.Count; i++)
                if (!_slips[i].Leaving && now > _slips[i].Until) Dismiss(_slips[i]);
        }

        public void Clear()
        {
            for (int i = 0; i < _slips.Count; i++) _slips[i].E.RemoveFromHierarchy();
            _slips.Clear();
        }

        /// <summary>A one-liner for clicking things on the desk that are not the computer or the phone.</summary>
        public void Quip(DeskTarget t)
        {
            string key = "desk.quip." + t;
            if (!Loc.Has(key)) return;
            string text = t == DeskTarget.Nameplate ? Dk.F(key, Dk.Name(Dk.Me)) : Dk.T(key);
            Post(SlipKind.Sticky, t == DeskTarget.Mug ? "morning" : "info", text, null);
        }
    }
}
