using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Incoming call card (top centre), usable without opening the phone: caller badge, Answer (Y) / Decline (N), a ring
    /// countdown and a physical ring wobble. Also shows the manager's morning call (UI-only, no voice).
    /// </summary>
    public sealed class DeskIncoming
    {
        /// <summary>Set false when the world plays the ringing phone itself.</summary>
        public static bool PlaysRingtone = true;

        readonly DeskUi _ui;
        readonly VisualElement _card, _icon, _fill;
        readonly VisualElement _handset, _waves;
        readonly VisualElement _badgeSlot;
        readonly Label _band, _name, _sub;
        readonly Button _answer, _decline;
        readonly VisualElement _declineBox;
        int _sig = int.MinValue;
        float _nextWobble;
        bool _manager;
        double _ringFrom, _ringUntil;

        public DeskIncoming(DeskUi ui, VisualElement layer)
        {
            _ui = ui;
            var wrap = Box("dk-topwrap").NoPick().AddTo(layer);
            _card = Box("dk-incoming").AddTo(wrap);
            var row = Box("dk-incoming-row").AddTo(_card);
            // the desk phone itself: base + handset layers (800x600, same origin); the handset jumps on every ring
            var phone = Box("dk-incoming-phone").NoPick().AddTo(row);
            _waves = Box("dk-incoming-waves").NoPick().AddTo(phone);
            Dk.Art(_waves, "deskphone/ring_waves");
            var baseArt = Box("dk-incoming-layer").NoPick().AddTo(phone);
            _handset = Box("dk-incoming-layer", "dk-incoming-handset").NoPick().AddTo(phone);
            if (Dk.Art(baseArt, "deskphone/phone_base") && Dk.Art(_handset, "deskphone/handset")) _icon = phone;
            else { phone.RemoveFromHierarchy(); _handset = null; _icon = Dk.Icon("ringing", "dk-incoming-icon").AddTo(row); }
            _badgeSlot = Box("dk-incoming-badge").NoPick().AddTo(row);
            var col = Box("dk-incoming-col").NoPick().AddTo(row);
            _band = StampText("", "dk-incoming-band").NoPick().AddTo(col);
            _name = Title("", "dk-incoming-name").NoPick().AddTo(col);
            _sub = Text("", "dk-incoming-sub").NoPick().AddTo(col);
            var actions = Box("dk-incoming-actions").AddTo(row);
            var a = Box("dk-incoming-action").AddTo(actions);
            _answer = Btn(Dk.T("desk.incoming.answer"), OnAnswer, "btn-primary", "dk-incoming-btn").AddTo(a);
            Dk.Key(Dk.KeyLabel(GameInput.AnswerCall), "dk-key-s").AddTo(a);
            var d = Box("dk-incoming-action").AddTo(actions);
            _declineBox = d;
            _decline = Btn(Dk.T("desk.incoming.decline"), OnDecline, "btn-ghost", "dk-incoming-btn").AddTo(d);
            Dk.Key(Dk.KeyLabel(GameInput.HangUp), "dk-key-s").AddTo(d);
            var bar = Box("dk-incoming-bar").NoPick().AddTo(_card);
            _fill = Box("dk-incoming-fill").NoPick().AddTo(bar);
            _card.Show(false);
        }

        public bool Visible => _card.style.display != DisplayStyle.None;
        /// <summary>Coach-arrow anchor.</summary>
        public VisualElement Card => _card;

        void OnAnswer()
        {
            if (_manager) _ui.Cards.AnswerManager();
            else _ui.Answer();
        }

        void OnDecline()
        {
            if (_manager) _ui.Cards.AnswerManager(); // the manager does not take no for an answer
            else _ui.Decline();
        }

        public void Ring()
        {
            _nextWobble = 0f;
        }

        public void Hide()
        {
            _card.Show(false);
            _sig = int.MinValue;
            Sfx.StopLoop("desk_ring");
        }

        public void Tick(LineView v, bool changed)
        {
            var ph = v.Mine.Phone;
            bool real = ph.Status == CallStatus.Ringing;
            bool mgr = !real && _ui.Cards.ManagerRinging;
            int sig = real ? 1000 + ph.Other : mgr ? 2 : 0;
            if (sig != _sig)
            {
                bool wasShown = _sig > 0;
                _sig = sig;
                if (sig == 0)
                {
                    if (wasShown) UiMotion.Tween(_card, 0.2f, t => { _card.style.translate = new Translate(0, -140f * t); _card.style.opacity = 1f - t; }, UiMotion.Ease.InCubic,
                        () => { _card.Show(false); _card.style.translate = StyleKeyword.Null; _card.style.opacity = StyleKeyword.Null; }, 0f, "move");
                    Sfx.StopLoop("desk_ring");
                    return;
                }
                _manager = mgr;
                _badgeSlot.Clear();
                int who = real ? ph.Other : LineIds.Manager;
                Dk.Badge(who, "badge-l").AddTo(_badgeSlot);
                _band.SetText(Loc.Upper(Dk.T(mgr ? "desk.incoming.band_manager" : "desk.incoming.band")));
                _name.SetText(mgr ? Dk.ManagerName(v.ManagerProfileId) : Dk.Name(who));
                _sub.SetText(mgr ? Dk.F("desk.incoming.sub_manager", Dk.ManagerTitle(v.ManagerProfileId)) : Dk.T("desk.incoming.sub"));
                _card.EnableInClassList("dk-incoming-manager", mgr);
                _declineBox.Show(!mgr);
                _card.Show(true);
                _card.BringToFront();
                _ringFrom = Dk.Now;
                _ringUntil = real ? ph.RingUntil : Dk.Now + 6;
                UiMotion.SlideIn(_card, new Vector2(0, -150f), 0.38f, 0f, UiMotion.Ease.OutBack);
                _nextWobble = Time.unscaledTime + 0.4f;
                if (PlaysRingtone) Sfx.LoopAt("desk_ring", "ringtone", Vector3.zero, 0.55f, false);
            }
            if (_sig <= 0) return;
            if (_manager) _ringUntil = _ui.Cards.ManagerRingUntil;
            float span = (float)(_ringUntil - _ringFrom);
            float left = span > 0.01f ? Mathf.Clamp01((float)((_ringUntil - Dk.Now) / span)) : 0f;
            _fill.style.width = Length.Percent(left * 100f);
            if (Time.unscaledTime >= _nextWobble)
            {
                _nextWobble = Time.unscaledTime + 1.15f;
                if (_handset != null)
                {
                    var h = _handset;
                    UiMotion.Tween(h, 0.55f, t =>
                    {
                        float k = Mathf.Sin(t * Mathf.PI * 7f) * (1f - t);
                        h.style.rotate = new Rotate(new Angle(k * 9f, AngleUnit.Degree));
                        h.style.translate = new Translate(0f, -Mathf.Abs(k) * 7f);
                    }, UiMotion.Ease.Linear, () => { h.style.rotate = StyleKeyword.Null; h.style.translate = StyleKeyword.Null; }, 0f, "ring");
                    var w = _waves;
                    UiMotion.Tween(w, 0.6f, t => { w.style.opacity = Mathf.Sin(t * Mathf.PI); float s = 0.85f + 0.3f * t; w.style.scale = new Scale(new Vector3(s, s, 1f)); }, UiMotion.Ease.OutCubic, null, 0f, "ring");
                }
                else UiMotion.Shake(_icon, 7f, 0.5f, 11f);
                Ring(_card);
            }
        }

        static void Ring(VisualElement e)
        {
            UiMotion.Tween(e, 0.5f, t =>
            {
                float a = Mathf.Sin(t * Mathf.PI * 6f) * 1.6f * (1f - t);
                e.style.rotate = new Rotate(new Angle(a, AngleUnit.Degree));
            }, UiMotion.Ease.Linear, () => e.style.rotate = StyleKeyword.Null, 0f, "ring");
        }
    }
}
