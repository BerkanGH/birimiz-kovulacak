using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// First day of the session: a sticky note on the monitor with the day's to-do list (answer the phone → open the
    /// computer → read the card → call your partner → submit → wait for the meeting) and a hand-drawn coach arrow that
    /// points at the thing to do next. Steps tick off as they happen (out of order is fine).
    /// </summary>
    public sealed class DeskOnboarding
    {
        const int Steps = 6;
        readonly DeskUi _ui;
        readonly VisualElement _note, _list, _arrow;
        readonly Label _title;
        readonly VisualElement[] _rows = new VisualElement[Steps];
        readonly VisualElement[] _boxes = new VisualElement[Steps];
        readonly Label[] _texts = new Label[Steps];
        int _doneMask = -1, _current = -1, _modeSig = -1;
        VisualElement _target;
        bool _visible;

        public DeskOnboarding(DeskUi ui, VisualElement layer)
        {
            _ui = ui;
            _note = Box("dk-ob-note").NoPick().AddTo(layer);
            Dk.Skin(_note, "onboarding/sticky_note_9slice");
            _title = Hand(Dk.T("onboard.title"), "dk-ob-title").NoPick().AddTo(_note);
            _list = Box("dk-ob-list").NoPick().AddTo(_note);
            for (int i = 0; i < Steps; i++)
            {
                var row = Box("dk-ob-row").NoPick().AddTo(_list);
                _boxes[i] = Dk.Icon("sticky_todo", "dk-ob-box").AddTo(row);
                _texts[i] = Hand("", "dk-ob-text").NoPick().AddTo(row);
                _rows[i] = row;
            }
            _arrow = Box("dk-ob-arrow").NoPick().AddTo(layer);
            if (!Dk.Art(_arrow, "onboarding/coach_arrow")) Dk.Art(_arrow, "icons_v3/hint_arrow");
            _note.Show(false);
            _arrow.Show(false);
        }

        string StepText(int i)
        {
            switch (i)
            {
                case 0: return Dk.F("onboard.step.0", Dk.KeyLabel(GameInput.AnswerCall));
                case 1: return Dk.F("onboard.step.1", Dk.KeyLabel(GameInput.Interact));
                case 3: return Dk.F("onboard.step.3", Dk.KeyLabel(GameInput.Phone));
                default: return Dk.T("onboard.step." + i);
            }
        }

        bool Done(LineView v, int i)
        {
            var s = _ui.State;
            switch (i)
            {
                case 0: return s.MorningAnsweredDay == v.Day || v.Phase != LinePhase.Morning && s.MorningRangDay != v.Day;
                case 1: return s.ObOpenedComputer;
                case 2: return s.IntroSeenKey >= 0;
                case 3: return s.ObCalled;
                case 4: return s.ObSubmitted;
                default: return false;
            }
        }

        public void Tick(LineView v, bool changed)
        {
            bool show = v.Features.OnboardingDay && LineClient.IsOfficePhase(v.Phase);
            if (show != _visible)
            {
                _visible = show;
                _note.Show(show);
                if (show) { UiMotion.SlideIn(_note, new Vector2(-40f, -20f), 0.4f, 0.3f, UiMotion.Ease.OutBack); _doneMask = -1; _modeSig = -1; }
                else _arrow.Show(false);
            }
            if (!show) return;

            int mask = 0, current = Steps - 1;
            for (int i = Steps - 1; i >= 0; i--) if (Done(v, i)) mask |= 1 << i;
            for (int i = 0; i < Steps; i++) if ((mask & (1 << i)) == 0) { current = i; break; }
            bool compact = _ui.ComputerOpen || _ui.PhoneOpen;
            int modeSig = compact ? 1 : 0;
            if (mask != _doneMask || current != _current || modeSig != _modeSig)
            {
                int newly = _doneMask < 0 ? 0 : mask & ~_doneMask;
                _doneMask = mask;
                _current = current;
                _modeSig = modeSig;
                _note.EnableInClassList("dk-ob-compact", compact);
                _title.SetText(Dk.T(compact ? "onboard.title_short" : "onboard.title"));
                for (int i = 0; i < Steps; i++)
                {
                    bool done = (mask & (1 << i)) != 0;
                    _texts[i].SetText(StepText(i));
                    _rows[i].EnableInClassList("dk-ob-done", done);
                    _rows[i].EnableInClassList("dk-ob-now", i == current);
                    _rows[i].Show(!compact || i == current);
                    Dk.SetIcon(_boxes[i], done ? "sticky_check" : "sticky_todo");
                    if ((newly & (1 << i)) != 0) { UiMotion.Pulse(_rows[i], 0.18f, 0.35f); Dk.Play("complete", null, 0.6f); }
                }
                if (newly == 0) UiMotion.Pulse(_note, 0.04f, 0.3f);
            }
            PlaceArrow(v);
        }

        VisualElement Target(LineView v)
        {
            switch (_current)
            {
                case 0: return _ui.Incoming.Visible ? _ui.Incoming.Card : null;
                case 1: return !_ui.ComputerOpen && !_ui.PhoneOpen && !_ui.Cards.Visible ? _ui.Hud.ComputerKey : null;
                case 2: return _ui.Cards.IntroOpen ? _ui.Cards.PrimaryButton : null;
                case 3:
                    if (_ui.ComputerOpen && _ui.Computer.App == DeskApp.Task) return _ui.Computer.Task.CallTarget;
                    return !_ui.ComputerOpen && !_ui.PhoneOpen && !_ui.Cards.Visible ? _ui.Hud.PhoneKey : null;
                case 4: return _ui.ComputerOpen && _ui.Computer.App == DeskApp.Task && v.Phase == LinePhase.RoundPlay ? _ui.Computer.Task.SubmitTarget : null;
                default: return null;
            }
        }

        void PlaceArrow(LineView v)
        {
            var t = Target(v);
            if (t == null || t.panel == null || t.worldBound.width < 2f)
            {
                if (_target != null) { _arrow.Show(false); _target = null; }
                return;
            }
            if (t != _target)
            {
                _target = t;
                _arrow.Show(true);
                UiMotion.PopIn(_arrow, 0.3f, 0f, 0.6f);
            }
            var b = t.worldBound;
            var root = _arrow.parent != null ? _arrow.parent.worldBound : new Rect(0, 0, 1920, 1080);
            float w = 200f, h = 100f;
            float bob = Mathf.Sin(Time.unscaledTime * 5f) * 10f;
            bool left = b.xMin - root.xMin > w + 30f;
            float x, y, rot;
            if (left) { x = b.xMin - root.xMin - w - 12f + bob; y = b.center.y - root.yMin - h * 0.62f; rot = 0f; }
            else { x = b.center.x - root.xMin - w * 0.5f; y = b.yMax - root.yMin + 8f + bob; rot = -90f; }
            _arrow.style.left = x;
            _arrow.style.top = y;
            _arrow.style.rotate = new Rotate(new Angle(rot, AngleUnit.Degree));
        }
    }
}
