using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.PlayKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 step 2 (docs/V5_INTERACTION.md §2): the task objects of the office.
    /// <list type="bullet">
    /// <item><b>Kasa kutusu</b> (Petty Cash, TaskPlay → its break): E opens the lid; "Al" / "Geri koy" move the notes
    /// between the box and the desk (the box always shows your current choice), the reveal shows your group's outcome
    /// (empty box, notes beside it, the red seal when both took), a witness's İhbar button lights up in the break and the
    /// card asks to confirm the whistle; the sole taker is warned.</item>
    /// <item><b>Nöbet çizelgesi</b> (Weekend Shift): the pencil writes your name (Volunteer) or rubs it out (Skip);
    /// others stay hidden until the reveal fills in the public roster (<see cref="RosterSheet"/>).</item>
    /// <item><b>Çekmece</b>: the prim stack (bundles of five, a tag with the number); prim coming in jolts the drawer
    /// with a thunk and a toast (transfers keep their own toast), prim going out shrinks the stack.</item>
    /// </list>
    /// The world side is <see cref="OfficeRoom"/> (OfficeRoom.Tasks.cs); this class decides its states from the view
    /// and shows the object card while one of them is in focus. Choices and the whistle are the same verbs as before.
    /// </summary>
    public sealed class OfficeObjects
    {
        readonly OfficeUi _ui;
        readonly VisualElement _card;
        readonly Label _kicker, _clock, _foot, _flash;
        readonly PlaySection _body;
        readonly RosterSheet _sheet = new RosterSheet();
        OfficeUi.Focus _cardFor = OfficeUi.Focus.None;
        bool _dirty = true;
        int _week = int.MinValue;
        float _flashUntil;
        // cash box
        int _cashTask = -1, _whistledTarget = -1, _whistledBy = -1, _whistledLost;
        bool _tookKnown, _took, _boxShown;
        // roster
        int _shiftTask = -1, _lastNeeded = -1;
        bool _signKnown, _signed, _stroke, _strokeErase;
        List<int> _lastRoster;
        // prim
        int _lastPrim = -1, _pendingIn, _explained;
        float _pendingAt, _explainedUntil;

        public OfficeObjects(OfficeUi ui, VisualElement parent)
        {
            _ui = ui;
            _card = Box("oc-card").AddTo(parent);
            var head = Box("oc-head").NoPick().AddTo(_card);
            _kicker = Bold("", "oc-kicker").NoPick().AddTo(head);
            Box("grow").NoPick().AddTo(head);
            _clock = Title("", "oc-clock").NoPick().AddTo(head);
            _body = new PlaySection(_card, "oc-body");
            _flash = Bold("", "oc-flash").NoPick().AddTo(_card);
            _flash.Show(false);
            _foot = Text("", "oc-foot").NoPick().AddTo(_card);
            _card.Show(false);
        }

        public void MarkDirty() { _dirty = true; }

        /// <summary>The office became visible again (after a meeting): prim changes made elsewhere are not "new".</summary>
        public void OnShow() { _lastPrim = -1; _pendingIn = 0; _explained = 0; _dirty = true; }

        public void Release() { _sheet.Release(); }

        /// <summary>The match closed (lobby, a new mock scenario): forget what this one's objects showed.</summary>
        public void Reset()
        {
            _week = int.MinValue;
            _cashTask = _shiftTask = -1;
            _tookKnown = _signKnown = _stroke = false;
            _whistledTarget = _whistledBy = -1;
            _lastRoster = null; _lastNeeded = -1;
            _lastPrim = -1; _pendingIn = 0; _explained = 0;
            _dirty = true;
        }

        /// <summary>Petty Cash break: a witness took my notes back (the "Şimdi" line stops warning about it).</summary>
        public bool WhistledOnMe => _whistledBy >= 0;
        public int WhistledBy => _whistledBy;
        public int WhistledLost => _whistledLost;

        // ------------------------------------------------------------------ events

        public void OnEvent(LineEvent e, int me, LineConfig c)
        {
            switch (e.Type)
            {
                case LineEventType.TransferReceived:
                    _explained += Math.Max(0, e.A);
                    _explainedUntil = Time.unscaledTime + 2.5f;
                    break;
                case LineEventType.Whistled:
                    if (e.Actor == me)
                    {
                        _whistledTarget = e.Target;
                        _explained += c.CashWhistleReward;
                        _explainedUntil = Time.unscaledTime + 2.5f;
                    }
                    if (e.Target == me) { _whistledBy = e.Actor; _whistledLost = e.A; Sfx.Play("bills", 0.6f); }
                    _dirty = true;
                    break;
            }
        }

        /// <summary>A phase began in the office: the sole taker of Petty Cash hears that a witness may report them.</summary>
        public void OnPhase(LinePhase to, LineClient lc, LineView v)
        {
            if (to == LinePhase.Break && v.Task != null && v.Features.WhistleOpen && PlayBlocks.SoleTaker(v.Task, lc.MySlot))
                _ui.Toast(T("office.cash.toast_thief"), "of-toast-warn", 8f);
            _dirty = true;
        }

        // ------------------------------------------------------------------ E in the object's view

        /// <summary>E while the box / roster / drawer is in front of you: the card's main action, or why not.</summary>
        public void Primary(OfficeUi.Focus focus, LineClient lc, LineView v)
        {
            var t = v.Task;
            switch (focus)
            {
                case OfficeUi.Focus.CashBox:
                    if (t != null && t.Kind == TaskKind.PettyCash && v.Phase == LinePhase.TaskPlay) { Choose(t.Submitted && t.MyChoice == LineChoice.Take ? LineChoice.Leave : LineChoice.Take); return; }
                    if (Witness(v)) { Deny(T("office.cash.deny_confirm")); return; }
                    Deny(T(v.Phase == LinePhase.Break && v.Features.WhistleOpen ? "office.cash.deny_whistle" : "office.cash.deny_closed"));
                    return;
                case OfficeUi.Focus.Roster:
                    if (t != null && t.Kind == TaskKind.WeekendShift && v.Phase == LinePhase.TaskPlay) { Choose(t.Submitted && t.MyChoice == LineChoice.Volunteer ? LineChoice.Skip : LineChoice.Volunteer); return; }
                    Deny(T(t != null && t.Kind == TaskKind.WeekendShift ? "office.roster.deny_closed" : "office.roster.deny_later"));
                    return;
                case OfficeUi.Focus.Drawer:
                    _ui.CloseFocus();
                    return;
            }
        }

        static void Choose(int choice)
        {
            Sfx.Play("click", 0.5f);
            Send(LineVerb.SubmitTask, a: choice);
        }

        void Deny(string why)
        {
            Sfx.Play("deny", 0.55f);
            _flash.SetText(why);
            _flash.Show(true);
            _flashUntil = Time.unscaledTime + 2.6f;
            UiMotion.Pulse(_card, 0.03f, 0.2f);
        }

        static bool Witness(LineView v) { return v.Phase == LinePhase.Break && v.Features.WhistleOpen && v.Mine.WitnessOf.Count > 0; }

        /// <summary>Petty Cash is running (its TaskPlay, reveal or break): the box is on the desk.</summary>
        public static bool CashTask(LineView v)
        {
            return v != null && v.Task != null && v.Task.Kind == TaskKind.PettyCash
                   && (v.Phase == LinePhase.TaskPlay || v.Phase == LinePhase.TaskReveal || v.Phase == LinePhase.Break);
        }

        /// <summary>Applies the world states now (an object view opened in the same frame the view arrived).</summary>
        public void SyncNow(LineClient lc, LineView v, OfficeUi.Focus focus) { SyncWorld(lc, v, focus); }

        // ------------------------------------------------------------------ per frame

        public void Tick(LineClient lc, LineView v, double now, bool dirty, bool live, OfficeUi.Focus focus)
        {
            if (v.WeekId != _week)
            {
                Reset();
                _week = v.WeekId;
            }
            SyncWorld(lc, v, focus);
            bool show = focus == OfficeUi.Focus.CashBox || focus == OfficeUi.Focus.Roster || focus == OfficeUi.Focus.Drawer;
            if (focus != _cardFor)
            {
                _cardFor = focus;
                _card.Show(show);
                _card.EnableInClassList("oc-bottom", focus == OfficeUi.Focus.CashBox);
                _card.EnableInClassList("oc-side", focus == OfficeUi.Focus.Roster);
                _card.EnableInClassList("oc-mini", focus == OfficeUi.Focus.Drawer);
                _flash.Show(false);
                _body.Invalidate();
                _dirty = true;
                if (show) { UiMotion.FadeIn(_card, 0.2f); UiMotion.SlideIn(_card, focus == OfficeUi.Focus.Roster ? new Vector2(-40f, 0f) : new Vector2(0f, 30f), 0.26f, 0.12f, UiMotion.Ease.OutCubic); }
            }
            if (_flash.IsShown() && Time.unscaledTime > _flashUntil) _flash.Show(false);
            if (!show) return;
            UpdateHead(lc, v, now, focus);
            if (dirty || _dirty)
            {
                _dirty = false;
                _body.Update(p => Build(p, lc, v, focus));
            }
            if (live) _body.TickLive(now);
        }

        void UpdateHead(LineClient lc, LineView v, double now, OfficeUi.Focus focus)
        {
            string e = OfficeText.Key(GameInput.Interact);
            bool play = v.Phase == LinePhase.TaskPlay;
            switch (focus)
            {
                case OfficeUi.Focus.CashBox:
                    _kicker.SetText(Loc.Upper(PlayText.Task(TaskKind.PettyCash)));
                    _foot.SetText(play ? F("office.cash.foot_play", e) : T("office.cash.foot"));
                    break;
                case OfficeUi.Focus.Roster:
                    _kicker.SetText(Loc.Upper(PlayText.Task(TaskKind.WeekendShift)));
                    _foot.SetText(play && v.Task != null && v.Task.Kind == TaskKind.WeekendShift ? F("office.roster.foot_play", e) : T("office.roster.foot"));
                    break;
                default:
                    _kicker.SetText(T("office.drawer.kicker"));
                    _foot.SetText(F("office.drawer.foot", e));
                    break;
            }
            double left = Math.Max(0, v.PhaseEndTime - now);
            _clock.SetText(focus == OfficeUi.Focus.Drawer ? "" : UiKit.Clock(left));
            _clock.EnableInClassList("oc-clock-low", left < 10 && left > 0 && play);
        }

        // ------------------------------------------------------------------ cards

        void Build(VisualElement p, LineClient lc, LineView v, OfficeUi.Focus focus)
        {
            switch (focus)
            {
                case OfficeUi.Focus.CashBox: BuildCash(p, lc, v); break;
                case OfficeUi.Focus.Roster: BuildRoster(p, lc, v); break;
                default: BuildDrawer(p, v); break;
            }
        }

        void BuildCash(VisualElement p, LineClient lc, LineView v)
        {
            var t = v.Task;
            var c = lc.Config;
            int me = lc.MySlot;
            string e = OfficeText.Key(GameInput.Interact);
            if (t == null || t.Kind != TaskKind.PettyCash) { Big(p, T("office.cash.none")); return; }
            if (v.Phase == LinePhase.TaskPlay)
            {
                Big(p, T("focus.task.petty_cash"));
                Line(p, F("task.group", PlayText.Names(t.Group.FindAll(id => id != me))) + "  ·  " + F("office.cash.amount", c.CashTake), "pl-dim");
                bool took = t.Submitted && t.MyChoice == LineChoice.Take;
                Line(p, took ? F("office.cash.status_taken", c.CashTake) : t.Submitted ? T("office.cash.status_left") : T("office.cash.status_default"),
                     took ? "oc-status-took" : t.Submitted ? "oc-status-done" : "oc-status-todo");
                var row = Box("oc-buttons").AddTo(p);
                if (took) Btn(F("office.cash.btn_return", e), () => Choose(LineChoice.Leave), "pl-primary", "pl-big").AddTo(row);
                else
                {
                    Btn(F("office.cash.btn_take", e), () => Choose(LineChoice.Take), "oc-take", "pl-big").AddTo(row);
                    if (!t.Submitted) Btn(T("office.cash.btn_leave"), () => Choose(LineChoice.Leave), "pl-big").AddTo(row);
                }
                return;
            }
            if (!t.Revealed) { Big(p, T("task.not_revealed")); return; }
            var takers = new List<int>();
            foreach (var ch in t.Choices) if (ch.Choice == LineChoice.Take) takers.Add(ch.Player);
            if (_whistledBy >= 0) Big(p, F("office.cash.out_whistled_me", Name(_whistledBy), _whistledLost), "pl-bad");
            else if (_whistledTarget >= 0) Big(p, F("office.cash.out_whistled", Name(_whistledTarget), c.CashWhistleReward), "pl-good");
            else if (Witness(v))
            {
                foreach (int thief in v.Mine.WitnessOf)
                {
                    int id = thief;
                    var box = Box("oc-confirm").AddTo(p);
                    Bold(T("office.cash.confirm_kicker"), "oc-confirm-kicker").AddTo(box);
                    Big(box, F("break.witness", Name(id), c.CashWhistleBlame, c.CashWhistleReward));
                    var row = Box("oc-buttons").AddTo(box);
                    Btn(F("break.whistle_btn", Name(id)), () => { Sfx.Play("alarm_btn", 0.7f); Send(LineVerb.Whistle, id); }, "pl-danger", "pl-big").AddTo(row);
                    Btn(T("office.cash.btn_not_now"), () => _ui.CloseFocus(), "pl-big").AddTo(row);
                    Line(box, T("focus.witness_or"), "pl-dim");
                }
            }
            else if (takers.Count == 0) Big(p, T("office.cash.out_none"));
            else if (takers.Count == 1 && takers[0] == me)
            {
                Big(p, F("office.cash.out_me", Math.Max(0, t.MyPayout)));
                if (v.Phase == LinePhase.Break && v.Features.WhistleOpen) Line(p, T("focus.thief"), "oc-status-took");
                else Line(p, T("office.cash.out_me_wait"), "pl-dim");
            }
            else if (takers.Count == 1)
            {
                Big(p, F("office.cash.out_other", Name(takers[0])));
                Line(p, T(v.Phase == LinePhase.Break ? "office.cash.out_other_closed" : "office.cash.out_other_wait"), "pl-dim");
            }
            else if (takers.Contains(me)) Big(p, F("office.cash.out_caught_me", c.CashCaughtBlame), "pl-bad");
            else Big(p, F("office.cash.out_caught", PlayText.Names(takers), c.CashCaughtBlame));
            var chips = Wrap(p);
            foreach (var ch in t.Choices)
                Bold(Name(ch.Player) + ": " + PlayText.Choice(t.Kind, ch.Choice), "pl-chip", ch.Choice == LineChoice.Take ? "pl-chip-bad" : null).AddTo(chips);
        }

        void BuildRoster(VisualElement p, LineClient lc, LineView v)
        {
            var t = v.Task;
            var c = lc.Config;
            string e = OfficeText.Key(GameInput.Interact);
            bool shift = t != null && t.Kind == TaskKind.WeekendShift;
            int needed = shift && t.Needed >= 0 ? t.Needed : LineConfig.ShiftNeeded(v.Players.Count);
            if (shift && v.Phase == LinePhase.TaskPlay)
            {
                Big(p, F("focus.task.weekend_shift", c.ShiftSkipPay, needed));
                Line(p, F("office.roster.needed", needed), "oc-needed");
                bool signed = t.Submitted && t.MyChoice == LineChoice.Volunteer;
                Line(p, signed ? T("office.roster.status_signed") : t.Submitted ? F("office.roster.status_skipped", c.ShiftSkipPay) : F("office.roster.status_default", c.ShiftSkipPay),
                     signed ? "oc-status-done" : t.Submitted ? "oc-status-done" : "oc-status-todo");
                var row = Box("oc-buttons").AddTo(p);
                if (signed) Btn(F("office.roster.btn_erase", e), () => Choose(LineChoice.Skip), "pl-big").AddTo(row);
                else
                {
                    Btn(F("office.roster.btn_write", e), () => Choose(LineChoice.Volunteer), "pl-primary", "pl-big").AddTo(row);
                    if (!t.Submitted) Btn(T("office.roster.btn_skip"), () => Choose(LineChoice.Skip), "pl-big").AddTo(row);
                }
                return;
            }
            List<int> roster = shift && t.Revealed ? t.Roster : _lastRoster;
            if (roster == null) { Big(p, T("office.roster.later_card")); return; }
            int need = shift ? needed : _lastNeeded;
            bool ok = roster.Count >= need;
            Big(p, F(ok ? "office.roster.result_ok" : "office.roster.result_short", roster.Count, need), ok ? "pl-good" : "pl-bad");
            var chips = Wrap(p);
            if (roster.Count == 0) Line(p, T("office.roster.nobody"), "pl-dim");
            foreach (int id in roster) Bold(Name(id), "pl-chip").AddTo(chips);
            if (shift && t.MyPayout >= 0) Bold(F("task.my_payout", t.MyPayout), "pl-good", "pl-sub").AddTo(p);
        }

        void BuildDrawer(VisualElement p, LineView v)
        {
            Big(p, F("office.drawer.title", v.Mine.Prim));
            Line(p, T("office.drawer.private"), "pl-dim");
        }

        // ------------------------------------------------------------------ the world

        void SyncWorld(LineClient lc, LineView v, OfficeUi.Focus focus)
        {
            var rooms = RoomsPresenter.Instance;
            var room = rooms != null && !rooms.InMeeting ? rooms.Office : null;
            if (room == null) return;
            var t = v.Task;
            int me = lc.MySlot;
            var c = lc.Config;
            bool taskPhase = v.Phase == LinePhase.TaskPlay || v.Phase == LinePhase.TaskReveal || v.Phase == LinePhase.Break;

            // ---- the Petty Cash box
            bool cash = CashTask(v);
            if (cash && _cashTask != t.Number) { _cashTask = t.Number; _tookKnown = false; _whistledTarget = _whistledBy = -1; }
            if (cash)
            {
                bool onDesk = false, gone = false, seal = false;
                if (!t.Revealed)
                {
                    bool took = t.Submitted && t.MyChoice == LineChoice.Take;
                    onDesk = took;
                    if (_tookKnown && took != _took && v.Phase == LinePhase.TaskPlay)
                    {
                        room.CashGrab(took);
                        Sfx.Play("bills", 0.75f);
                    }
                    _took = took;
                    _tookKnown = true;
                }
                else
                {
                    int takers = 0, taker = -1;
                    foreach (var ch in t.Choices) if (ch.Choice == LineChoice.Take) { takers++; taker = ch.Player; }
                    if (takers == 1 && taker == me) { onDesk = _whistledBy < 0; gone = _whistledBy >= 0; }
                    else if (takers == 1) gone = true;
                    else if (takers >= 2) seal = true;
                }
                if (!_boxShown && _ui.Visible) Sfx.Play("box_drop", 0.6f);
                room.SetCashBox(true, Mathf.Clamp(c.CashTake, 1, 6), onDesk, gone, focus == OfficeUi.Focus.CashBox, Witness(v), _whistledTarget >= 0, seal);
            }
            else room.SetCashBox(false, 0, false, false, false, false, false, false);
            _boxShown = cash;

            // ---- the Weekend Shift roster
            bool shift = taskPhase && t != null && t.Kind == TaskKind.WeekendShift;
            if (shift && _shiftTask != t.Number) { _shiftTask = t.Number; _signKnown = false; }
            var s = new RosterSheet.State
            {
                Lines = v.Players.Count, SkipPay = c.ShiftSkipPay, MyName = Name(me), MyColor = lc.ColorOf(me),
                Needed = shift && t.Needed >= 0 ? t.Needed : LineConfig.ShiftNeeded(v.Players.Count),
            };
            bool startStroke = false;
            if (shift && !t.Revealed)
            {
                s.Stage = 1;
                bool signed = t.Submitted && t.MyChoice == LineChoice.Volunteer;
                s.MineOn = signed;
                if (_signKnown && signed != _signed && v.Phase == LinePhase.TaskPlay) { startStroke = true; _strokeErase = !signed; }
                _signed = signed;
                _signKnown = true;
            }
            else if (shift)
            {
                s.Stage = 2;
                s.Roster = MineFirst(t.Roster, me);
                _lastRoster = new List<int>(t.Roster);
                _lastNeeded = s.Needed;
            }
            else if (_lastRoster != null) { s.Stage = 2; s.Roster = MineFirst(_lastRoster, me); s.Needed = _lastNeeded; }
            var rig = rooms.Rig;
            if (_sheet.Ensure(room.RosterSheet, rig != null ? rig.Panel : null))
            {
                _sheet.Update(s);
                if (startStroke)
                {
                    _sheet.MineStroke(s.MyName, out var from, out var to);
                    room.PenStroke(from, to, _strokeErase, _strokeErase ? 0.95f : 0.85f);
                    Sfx.Play(_strokeErase ? "eraser" : "pencil", 0.7f);
                    _stroke = true;
                }
                if (_stroke)
                {
                    float pr = room.PenProgress;
                    if (_strokeErase) _sheet.Animate(1f, 1f - pr);
                    else _sheet.Animate(pr, 1f);
                    if (pr >= 1f) { _stroke = false; _sheet.Animate(s.MineOn ? 1f : 0f, 1f); }
                }
            }

            // ---- the drawer: held open in its view, the stack follows prim, incoming prim jolts it
            room.SetDrawerHeld(focus == OfficeUi.Focus.Drawer);
            int prim = v.Mine.Prim;
            if (_lastPrim < 0) { _lastPrim = prim; room.SetPrim(prim, false); }
            else if (prim != _lastPrim)
            {
                int d = prim - _lastPrim;
                _lastPrim = prim;
                room.SetPrim(prim, true);
                if (d > 0) { _pendingIn += d; _pendingAt = Time.unscaledTime + 0.45f; }
                _dirty = true;
            }
            if (_pendingIn > 0 && Time.unscaledTime >= _pendingAt)
            {
                if (Time.unscaledTime > _explainedUntil) _explained = 0;
                int unexplained = _pendingIn - _explained;
                _explained = Math.Max(0, _explained - _pendingIn);
                _pendingIn = 0;
                room.DrawerThunk();
                Sfx.Play("drawer_thunk", 0.75f);
                if (unexplained > 0 && _ui.Visible) _ui.Toast(F("office.drawer.toast_in", unexplained), "of-toast-money", 4.5f);
            }
        }

        static List<int> MineFirst(List<int> roster, int me)
        {
            var list = new List<int>(roster);
            if (list.Remove(me)) list.Insert(0, me);
            return list;
        }
    }
}
