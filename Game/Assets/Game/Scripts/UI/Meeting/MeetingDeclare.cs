using System;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.PlayKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5.2 "prim beyanı" (docs/V5_DESIGN.md §5): the card a final candidate declares their prim on. It comes up by itself
    /// when the crisis verdict names me a candidate (<see cref="LineFeaturesView.DeclareOpen"/>) and stays until I declare
    /// or the window closes: a number stepper 0..<see cref="LineConfig.DeclareMax"/> that starts at my real prim, a
    /// countdown and "Beyan et". Keyboard: ← → change by 1, ↑ ↓ by 5, digits type the number, Enter seals it, Esc puts
    /// the card away (a chip stays; Enter brings it back). The mouse works too (the card frees the cursor while it is up).
    /// After I declared the chip says "Beyanın mühürlendi: N prim" until everything is revealed on the projector.
    /// </summary>
    public sealed class MeetingDeclare
    {
        readonly VisualElement _card, _chip;
        readonly Label _timer, _value, _real, _keys, _chipText;
        readonly Button _ok;
        int _amount = -1, _week = int.MinValue, _max = 99;
        bool _minimised, _sent;
        float _sentAt, _typedAt = -10f;
        string _typed = "";
        string _chipSig;

        /// <summary>The card is up (it owns the arrow keys, the digits and Enter, and frees the cursor).</summary>
        public bool Open => _card.IsShown();
        public bool WantsCursor => Open;
        /// <summary>The chip says a declaration is still expected of me.</summary>
        public bool Waiting { get; private set; }
        /// <summary>UI mocks: the stepper's value.</summary>
        public int Amount { get { return _amount; } set { _amount = Mathf.Clamp(value, 0, _max); } }

        public MeetingDeclare(VisualElement parent)
        {
            _chip = Box("dc-chip").NoPick().AddTo(parent);
            Box("dc-chip-seal").NoPick().AddTo(_chip);
            _chipText = Bold("", "dc-chip-text").NoPick().AddTo(_chip);
            _chip.Show(false);

            _card = Box("dc-card").AddTo(parent);
            var head = Box("dc-head").AddTo(_card);
            Title(Loc.Upper(T("declare.card.title")), "dc-title").AddTo(head);
            _timer = Bold("", "dc-timer").AddTo(head);
            Text(T("declare.card.info"), "dc-info").AddTo(_card);
            var row = Box("dc-stepper").AddTo(_card);
            Step(row, -5);
            Step(row, -1);
            var mid = Box("dc-value-box").AddTo(row);
            _value = Title("", "dc-value").AddTo(mid);
            Bold(T("declare.card.unit"), "dc-unit").AddTo(mid);
            Step(row, +1);
            Step(row, +5);
            _real = Text("", "dc-real").AddTo(_card);
            var foot = Box("dc-foot").AddTo(_card);
            _keys = Text("", "dc-keys").AddTo(foot);
            Btn(T("declare.card.later"), () => Minimise(), "pl-big").AddTo(foot);
            _ok = Btn(T("declare.card.ok"), Confirm, "pl-primary", "pl-big", "dc-ok").AddTo(foot);
            _card.Show(false);
        }

        void Step(VisualElement row, int delta)
        {
            Btn((delta > 0 ? "+" : "−") + Math.Abs(delta), () => Add(delta), "dc-step", Math.Abs(delta) > 1 ? "dc-step-big" : null).AddTo(row);
        }

        void Add(int delta)
        {
            _typed = "";
            _amount = Mathf.Clamp(_amount + delta, 0, _max);
        }

        void Confirm()
        {
            if (_amount < 0 || _sent) return;
            Send(LineVerb.Declare, a: _amount);
            _sent = true;
            _sentAt = Time.unscaledTime;
            Sfx.Play("stamp", 0.55f);
        }

        /// <summary>Esc / "Sonra": the card goes away, the chip stays (false when it was not up).</summary>
        public bool Minimise()
        {
            if (!Open) return false;
            _minimised = true;
            _card.Show(false);
            return true;
        }

        /// <summary>Enter on the chip: the card comes back.</summary>
        public void Reopen() { _minimised = false; }

        public void Close()
        {
            _card.Show(false);
            _chip.Show(false);
            Waiting = false;
        }

        /// <summary>The card's keys (only while it is up); true when a key was used.</summary>
        public bool Keys()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null || !Open) return false;
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) { Confirm(); return true; }
            if (kb.leftArrowKey.wasPressedThisFrame || kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame) { Add(-1); return true; }
            if (kb.rightArrowKey.wasPressedThisFrame || kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame) { Add(+1); return true; }
            if (kb.downArrowKey.wasPressedThisFrame) { Add(-5); return true; }
            if (kb.upArrowKey.wasPressedThisFrame) { Add(+5); return true; }
            if (kb.backspaceKey.wasPressedThisFrame)
            {
                _typed = _typed.Length > 1 ? _typed.Substring(0, _typed.Length - 1) : "";
                _amount = _typed.Length > 0 ? Mathf.Clamp(int.Parse(_typed), 0, _max) : 0;
                _typedAt = Time.unscaledTime;
                return true;
            }
            int digit = Digit(kb);
            if (digit < 0) return false;
            // digits within a moment of each other make one number ("1", "2" = 12); later a new one starts
            if (Time.unscaledTime - _typedAt > 1.5f || _typed.Length >= 2) _typed = "";
            _typed += digit;
            _typedAt = Time.unscaledTime;
            _amount = Mathf.Clamp(int.Parse(_typed), 0, _max);
            return true;
        }

        static int Digit(UnityEngine.InputSystem.Keyboard kb)
        {
            var top = new[] { kb.digit0Key, kb.digit1Key, kb.digit2Key, kb.digit3Key, kb.digit4Key, kb.digit5Key, kb.digit6Key, kb.digit7Key, kb.digit8Key, kb.digit9Key };
            var pad = new[] { kb.numpad0Key, kb.numpad1Key, kb.numpad2Key, kb.numpad3Key, kb.numpad4Key, kb.numpad5Key, kb.numpad6Key, kb.numpad7Key, kb.numpad8Key, kb.numpad9Key };
            for (int i = 0; i < 10; i++) if (top[i].wasPressedThisFrame || pad[i].wasPressedThisFrame) return i;
            return -1;
        }

        /// <summary><paramref name="blocked"/>: something else is up (the folder, the paper, a zoom, a prank card): only the chip shows.</summary>
        public void Tick(LineClient lc, LineView v, double now, bool blocked)
        {
            var fv = v.Final;
            if (v.WeekId != _week)
            {
                _week = v.WeekId;
                _amount = -1;
                _minimised = _sent = false;
                _typed = "";
            }
            bool can = v.Features.DeclareOpen && fv != null;
            if (!can)
            {
                _card.Show(false);
                Waiting = false;
                _sent = false;
                // declared and still sealed: say so until the projector opens them all
                bool sealedMine = fv != null && fv.MyDeclaration >= 0 && !fv.DeclarationsRevealed && fv.DeclareUntil > 0;
                if (sealedMine) Chip(F("declare.chip.sealed", fv.MyDeclaration, Clock(v, fv, now)), false);
                else _chip.Show(false);
                return;
            }
            _max = Math.Max(0, lc.Config.DeclareMax);
            if (_amount < 0) _amount = Mathf.Clamp(v.Mine.Prim, 0, _max);   // the honest answer is one Enter away
            // a declaration the host did not take (too late, a dropped packet): ask again after a moment
            if (_sent && Time.unscaledTime - _sentAt > 2.5f) _sent = false;
            Waiting = !_sent;
            bool show = !_sent && !_minimised && !blocked;
            if (show != _card.IsShown())
            {
                _card.Show(show);
                if (show) { UiMotion.PopIn(_card, 0.2f); Sfx.Play("pickup", 0.4f); }
            }
            if (show)
            {
                _chip.Show(false);
                _timer.SetText(F("declare.card.timer", Clock(v, fv, now)));
                _timer.EnableInClassList("dc-timer-low", fv.DeclareUntil - now < 6);
                _value.SetText(_amount.ToString());
                _real.SetText(F("declare.card.real", v.Mine.Prim));
                _real.EnableInClassList("dc-real-lie", _amount != v.Mine.Prim);
                _keys.SetText(T("declare.card.keys"));
                return;
            }
            Chip(_sent ? T("declare.chip.sent") : F("declare.chip.todo", Clock(v, fv, now)), !_sent);
        }

        static string Clock(LineView v, LineFinalView fv, double now) { return UiKit.Clock(Math.Max(0, fv.DeclareUntil - now)); }

        void Chip(string text, bool todo)
        {
            if (!_chip.IsShown()) { _chip.Show(true); UiMotion.FadeIn(_chip, 0.2f); }
            string sig = text + todo;
            if (sig == _chipSig) return;
            _chipSig = sig;
            _chipText.SetText(text);
            _chip.EnableInClassList("dc-chip-todo", todo);
        }
    }
}
