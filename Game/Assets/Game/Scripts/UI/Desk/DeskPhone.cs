using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// The desk phone: an LCD (status, timers, "ON RECORD"), speed-dial keys with a paper label per colleague (digits
    /// 1–6 on the keyboard), the red Manager Line key (0) whose IVR runs the tip-off flow, and a call-log notepad.
    /// </summary>
    public sealed class DeskPhone
    {
        enum Ivr { Off, Pick, Confirm, Done }

        readonly DeskUi _ui;
        readonly VisualElement _layer, _console, _lcdIcon, _rec, _dial, _actions, _log, _pad;
        readonly Label _lcd1, _lcd2, _lcdTime;
        bool _open;
        int _sig = int.MinValue, _lastSec = -1;
        Ivr _ivr;
        int _ivrTarget = LineIds.None;
        float _ivrDoneAt;
        int _busyWho = LineIds.None;
        float _busyAt = -10f;
        readonly List<int> _speed = new List<int>();

        public bool IsOpen => _open;

        public DeskPhone(DeskUi ui, VisualElement layer)
        {
            _ui = ui;
            _layer = layer;
            var back = Box("dk-phone-backdrop").AddTo(layer);
            back.RegisterCallback<PointerDownEvent>(e => { if (e.target == back) _ui.ClosePhone(); });
            var wrap = Box("dk-phone-wrap").AddTo(back);
            _console = Box("dk-phone").AddTo(wrap);
            var lcd = Box("dk-lcd").NoPick().AddTo(_console);
            Dk.Skin(lcd, "deskphone/lcd_frame");
            var lcdRow = Box("dk-lcd-row").NoPick().AddTo(lcd);
            _lcdIcon = Box("dk-lcd-icon").NoPick().AddTo(lcdRow);
            var lcdCol = Box("dk-lcd-col").NoPick().AddTo(lcdRow);
            _lcd1 = MemoBold("", "dk-lcd-1").NoPick().AddTo(lcdCol);
            _lcd2 = Memo("", "dk-lcd-2").NoPick().AddTo(lcdCol);
            _lcdTime = MemoBold("", "dk-lcd-time").NoPick().AddTo(lcdRow);
            _rec = Box("dk-lcd-rec").NoPick().AddTo(lcd);
            if (!Dk.Art(_rec, "deskphone/rec_badge")) { _rec.AddToClassList("dk-lcd-rec-fallback"); MemoBold("REC", "dk-lcd-rec-text").NoPick().AddTo(_rec); }
            _rec.Show(false);

            var label = Box("dk-dial-label").NoPick().AddTo(_console);
            StampText(Loc.Upper(Dk.T("desk.phone.speed_dial")), "dk-dial-label-text").NoPick().AddTo(label);
            Box("spacer").NoPick().AddTo(label);
            Text(Dk.T("desk.phone.speed_dial_hint"), "dk-dial-label-hint").NoPick().AddTo(label);
            _dial = Box("dk-dial").AddTo(_console);
            _actions = Box("dk-phone-actions").AddTo(_console);

            _pad = Box("dk-notepad").AddTo(wrap);
            Hand(Dk.T("desk.phone.log"), "dk-notepad-title").NoPick().AddTo(_pad);
            var scroll = new ScrollView(ScrollViewMode.Vertical).Cls("dk-notepad-scroll").AddTo(_pad);
            _log = scroll.contentContainer;
            var close = Box("dk-phone-close").AddTo(wrap);
            Btn(Dk.T("desk.phone.put_down"), () => _ui.ClosePhone(), "btn-secondary", "btn-small").AddTo(close);
            Dk.Key(Dk.KeyLabel(GameInput.Phone), "dk-key-s").AddTo(close);
            _layer.Show(false);
        }

        public void Open()
        {
            bool was = _open;
            _open = true;
            _layer.Show(true);
            _layer.pickingMode = PickingMode.Position;
            _sig = int.MinValue;
            if (!was)
            {
                _ivr = Ivr.Off;
                UiMotion.SlideIn(_console, new Vector2(0, 90f), 0.34f, 0f, UiMotion.Ease.OutBack);
                UiMotion.SlideIn(_pad, new Vector2(60f, 20f), 0.34f, 0.1f, UiMotion.Ease.OutBack);
            }
            var v = Dk.View;
            if (v != null) Tick(v, true);
        }

        public void Close(bool silent)
        {
            if (!_open) return;
            _open = false;
            _ivr = Ivr.Off;
            _layer.Show(false);
            _layer.pickingMode = PickingMode.Ignore;
        }

        public bool Back()
        {
            if (_ivr != Ivr.Off) { _ivr = Ivr.Off; _sig = int.MinValue; return true; }
            return false;
        }

        public void Digit(int d)
        {
            var v = Dk.View;
            if (v == null) return;
            if (d == 0) { ManagerLine(v); return; }
            int i = d - 1;
            if (i >= 0 && i < _speed.Count) Press(v, _speed[i]);
        }

        /// <summary>UI mock: 1 = manager line menu, 2 = confirm tip on <paramref name="target"/>, 3 = tip received.</summary>
        internal void MockIvr(int stage, int target)
        {
            _ivr = (Ivr)stage;
            _ivrTarget = target;
            _ivrDoneAt = Time.unscaledTime + 60f;
            _sig = int.MinValue;
        }

        public void OnConnected() { _sig = int.MinValue; }
        public void OnEnded() { _sig = int.MinValue; }

        public void OnBusy(int who)
        {
            _busyWho = who;
            _busyAt = Time.unscaledTime;
            _sig = int.MinValue;
            if (_open) Dk.Shake(_console, 8f, 0.35f);
        }

        // ------------------------------------------------------------------ actions

        void Press(LineView v, int who)
        {
            if (_ivr == Ivr.Pick || _ivr == Ivr.Confirm)
            {
                _ivrTarget = who;
                _ivr = Ivr.Confirm;
                _sig = int.MinValue;
                Dk.Play("click");
                return;
            }
            _ui.Call(who);
        }

        void ManagerLine(LineView v)
        {
            if (!v.Features.TipLineUnlocked) { Dk.Play("deny"); Dk.Shake(_console, 6f, 0.3f); return; }
            if (TipSpent(v) || Dk.PhoneDead(v) || v.Mine.Phone.Status != CallStatus.Idle) { Dk.Play("deny"); return; }
            _ivr = _ivr == Ivr.Off ? Ivr.Pick : Ivr.Off;
            _ivrTarget = LineIds.None;
            _sig = int.MinValue;
            Dk.Play("tip_dial", "click");
        }

        static bool TipSpent(LineView v) { return v.Mine.TipUsedToday || v.Mine.TipsLeftThisWeek <= 0; }

        void ConfirmTip()
        {
            if (_ivrTarget < 0) return;
            Dk.Send(LineVerb.TipOff, _ivrTarget);
            Dk.Play("stamp");
            _ivr = Ivr.Done;
            _ivrDoneAt = Time.unscaledTime;
            _sig = int.MinValue;
        }

        // ------------------------------------------------------------------ tick

        public void Tick(LineView v, bool changed)
        {
            if (!_open) return;
            if (_ivr == Ivr.Done && Time.unscaledTime - _ivrDoneAt > 2.5f) { _ivr = Ivr.Off; _sig = int.MinValue; }
            bool busyFlash = Time.unscaledTime - _busyAt < 2.2f;
            var ph = v.Mine.Phone;
            bool dead = Dk.PhoneDead(v);
            int sig = (int)ph.Status * 1000 + ph.Other;
            sig = sig * 31 + (dead ? 1 : 0) + (TipSpent(v) ? 2 : 0) + (v.Features.TipLineUnlocked ? 4 : 0) + (v.Features.RecordingsEnabled ? 8 : 0) + (busyFlash ? 16 : 0);
            sig = sig * 31 + (int)_ivr * 10 + _ivrTarget;
            sig = sig * 31 + v.Mine.CallLog.Count;
            int lastEnd = v.Mine.CallLog.Count > 0 ? (int)v.Mine.CallLog[v.Mine.CallLog.Count - 1].End : 0;
            sig = sig * 31 + lastEnd;
            for (int i = 0; i < v.Players.Count; i++) sig = sig * 3 + (v.Players[i].Connected ? 1 : 0);
            if (sig != _sig) { _sig = sig; Build(v, dead, busyFlash); }

            int s = -1;
            if (dead) s = Mathf.CeilToInt((float)(v.Mine.PhoneDeadUntil - Dk.Now));
            else if (ph.Status == CallStatus.Connected) s = (int)(Dk.Now - ph.Since);
            else if (ph.Status == CallStatus.Dialing || ph.Status == CallStatus.Ringing) s = Mathf.CeilToInt((float)(ph.RingUntil - Dk.Now));
            else s = Mathf.CeilToInt((float)(v.PhaseEndTime - Dk.Now));
            if (s < 0) s = 0;
            if (s != _lastSec) { _lastSec = s; _lcdTime.SetText(ClockInt(s)); }
        }

        void Build(LineView v, bool dead, bool busyFlash)
        {
            var ph = v.Mine.Phone;
            _lastSec = -1;
            // LCD
            string icon = null;
            if (_ivr == Ivr.Done) { Lcd(Dk.T("desk.phone.lcd.tip_done"), Dk.T("desk.phone.lcd.tip_done_2")); icon = "deskphone/rec_badge"; }
            else if (_ivr == Ivr.Confirm) Lcd(Dk.F("desk.phone.lcd.tip_confirm", Loc.Upper(Dk.Name(_ivrTarget))), Dk.T("desk.phone.lcd.tip_confirm_2"));
            else if (_ivr == Ivr.Pick) Lcd(Dk.T("desk.phone.lcd.ivr"), Dk.T("desk.phone.lcd.ivr_2"));
            else if (dead) { Lcd(Dk.T("desk.phone.lcd.dead"), Dk.T("desk.phone.lcd.dead_2")); icon = "deskphone/line_dead"; }
            else if (busyFlash && ph.Status == CallStatus.Idle) { Lcd(Dk.F("desk.phone.lcd.busy", Loc.Upper(Dk.Name(_busyWho))), Dk.T("desk.phone.lcd.busy_2")); icon = "deskphone/busy"; }
            else switch (ph.Status)
            {
                case CallStatus.Dialing: Lcd(Dk.F("desk.phone.lcd.dialing", Loc.Upper(Dk.Name(ph.Other))), Dk.T("desk.phone.lcd.dialing_2")); icon = "deskphone/ring_waves"; break;
                case CallStatus.Ringing: Lcd(Dk.F("desk.phone.lcd.ringing", Loc.Upper(Dk.Name(ph.Other))), Dk.F("desk.phone.lcd.ringing_2", Dk.KeyLabel(GameInput.AnswerCall), Dk.KeyLabel(GameInput.HangUp))); icon = "deskphone/ring_waves"; break;
                case CallStatus.Connected: Lcd(Loc.Upper(Dk.Name(ph.Other)), Dk.F("desk.phone.lcd.connected_2", DeskHud.PttLabel())); break;
                default: Lcd(Dk.T("desk.phone.lcd.idle"), Dk.T("desk.phone.lcd.idle_2")); break;
            }
            _lcdIcon.style.backgroundImage = StyleKeyword.Null;
            _lcdIcon.Show(icon != null && Dk.Art(_lcdIcon, icon));
            bool rec = ph.Status == CallStatus.Connected && v.Features.RecordingsEnabled;
            if (rec && _rec.style.display == DisplayStyle.None) UiMotion.PopIn(_rec, 0.3f);
            _rec.Show(rec);
            _console.EnableInClassList("dk-phone-ivr", _ivr != Ivr.Off);
            _console.EnableInClassList("dk-phone-dead", dead);

            // speed dial
            _dial.Clear();
            _speed.Clear();
            bool idle = ph.Status == CallStatus.Idle && !dead;
            bool ivr = _ivr == Ivr.Pick || _ivr == Ivr.Confirm;
            int n = 0;
            for (int i = 0; i < v.Players.Count; i++)
            {
                var p = v.Players[i];
                if (p.Id == v.Me) continue;
                n++;
                _speed.Add(p.Id);
                int who = p.Id;
                var row = Dk.Tile(() => Press(Dk.View, who), "dk-dial-row").AddTo(_dial);
                var key = Box("dk-dial-key").NoPick().AddTo(row);
                Dk.Skin(key, "deskphone/key");
                MemoBold(Num(n), "dk-dial-key-text").NoPick().AddTo(key);
                Dk.Badge(who, "badge-s").AddTo(row);
                Bold(p.Name, "dk-dial-name").NoPick().AddTo(row);
                Box("spacer").NoPick().AddTo(row);
                string tag = null;
                if (ph.Other == who && ph.Status == CallStatus.Connected) tag = Dk.T("desk.phone.tag.talking");
                else if (ph.Other == who && ph.Status != CallStatus.Idle) tag = Dk.T("desk.phone.tag.ringing");
                else if (busyFlash && _busyWho == who) tag = Dk.T("desk.phone.tag.busy");
                else if (!p.Connected) tag = Dk.T("desk.away");
                if (tag != null) Tag(tag, ph.Other == who ? "tag-green" : "tag-grey").AddTo(row);
                if (ivr)
                {
                    row.AddToClassList("dk-dial-row-ivr");
                    if (who == _ivrTarget) row.AddToClassList("dk-dial-row-on");
                    Bold(Dk.T("desk.phone.tip_this"), "dk-dial-tip").NoPick().AddTo(row);
                }
                else Dk.Icon("call", "dk-dial-call").AddTo(row);
                row.SetEnabled(ivr || (idle && p.Connected));
            }
            // manager line
            var mgr = Dk.Tile(() => ManagerLine(Dk.View), "dk-dial-row", "dk-dial-manager").AddTo(_dial);
            var mkey = Box("dk-dial-key", "dk-dial-key-red").NoPick().AddTo(mgr);
            MemoBold("0", "dk-dial-key-text").NoPick().AddTo(mkey);
            Dk.Icon("manager_line", "dk-dial-mgr-icon").AddTo(mgr);
            var mc = Box("dk-dial-mgr-col").NoPick().AddTo(mgr);
            Bold(Dk.T("desk.phone.manager_line"), "dk-dial-name").NoPick().AddTo(mc);
            string mgrSub = !v.Features.TipLineUnlocked ? Dk.F("desk.phone.manager_locked", Dk.DayName(Dk.Config.TipUnlockDayFirstWeek))
                : TipSpent(v) ? Dk.T("desk.phone.manager_used")
                : Dk.T("desk.phone.manager_sub");
            Text(mgrSub, "dk-dial-mgr-sub").NoPick().AddTo(mc);
            if (!v.Features.TipLineUnlocked) Dk.Icon("lock", "dk-dial-call").AddTo(mgr);
            mgr.SetEnabled(v.Features.TipLineUnlocked && !TipSpent(v) && idle);

            // actions
            _actions.Clear();
            if (_ivr == Ivr.Confirm)
            {
                Text(Dk.F("desk.phone.tip_rule", Dk.Config.ConfirmedTipBlame, Dk.Config.FalseTipBlame), "dk-phone-rule").NoPick().AddTo(_actions);
                Box("spacer").NoPick().AddTo(_actions);
                Btn(Dk.T("desk.cancel"), () => { _ivr = Ivr.Off; _sig = int.MinValue; }, "btn-ghost").AddTo(_actions);
                Btn(Dk.T("desk.phone.tip_send"), ConfirmTip, "btn-danger", "dk-primary").AddTo(_actions);
            }
            else if (_ivr == Ivr.Pick)
            {
                Text(Dk.F("desk.phone.tip_rule", Dk.Config.ConfirmedTipBlame, Dk.Config.FalseTipBlame), "dk-phone-rule").NoPick().AddTo(_actions);
                Box("spacer").NoPick().AddTo(_actions);
                Btn(Dk.T("desk.cancel"), () => { _ivr = Ivr.Off; _sig = int.MinValue; }, "btn-ghost").AddTo(_actions);
            }
            else switch (ph.Status)
            {
                case CallStatus.Ringing:
                    Box("spacer").NoPick().AddTo(_actions);
                    Btn(Dk.T("desk.incoming.decline"), _ui.Decline, "btn-ghost").AddTo(_actions);
                    Dk.Key(Dk.KeyLabel(GameInput.HangUp), "dk-key-s").AddTo(_actions);
                    Btn(Dk.T("desk.incoming.answer"), _ui.Answer, "btn-primary", "dk-primary").AddTo(_actions);
                    Dk.Key(Dk.KeyLabel(GameInput.AnswerCall), "dk-key-s").AddTo(_actions);
                    break;
                case CallStatus.Dialing:
                    Box("spacer").NoPick().AddTo(_actions);
                    Btn(Dk.T("desk.call.cancel"), _ui.HangUp, "btn-danger", "dk-primary").AddTo(_actions);
                    Dk.Key(Dk.KeyLabel(GameInput.HangUp), "dk-key-s").AddTo(_actions);
                    break;
                case CallStatus.Connected:
                    var ptt = Box("dk-ptt").NoPick().AddTo(_actions);
                    Dk.Key(DeskHud.PttLabel()).AddTo(ptt);
                    Text(Dk.T("desk.phone.ptt"), "dk-ptt-text").NoPick().AddTo(ptt);
                    Box("spacer").NoPick().AddTo(_actions);
                    if (v.Features.RecordingsEnabled) Text(Dk.T("desk.phone.recorded_line"), "dk-phone-rule", "dk-phone-recorded").NoPick().AddTo(_actions);
                    Btn(Dk.T("desk.call.hang_up"), _ui.HangUp, "btn-danger", "dk-primary").AddTo(_actions);
                    Dk.Key(Dk.KeyLabel(GameInput.HangUp), "dk-key-s").AddTo(_actions);
                    break;
                default:
                    Text(dead ? Dk.T("desk.phone.dead_hint") : Dk.T("desk.phone.idle_hint"), "dk-phone-rule").NoPick().AddTo(_actions);
                    break;
            }

            BuildLog(v);
        }

        void Lcd(string a, string b)
        {
            if (_lcd1.text != a) UiMotion.FadeIn(_lcd1, 0.15f);
            _lcd1.SetText(a);
            _lcd2.SetText(b);
        }

        void BuildLog(LineView v)
        {
            _log.Clear();
            var log = v.Mine.CallLog;
            if (log.Count == 0) { Hand(Dk.T("desk.phone.log_empty"), "dk-notepad-empty").NoPick().AddTo(_log); return; }
            for (int i = log.Count - 1; i >= 0 && i >= log.Count - 12; i--)
            {
                var c = log[i];
                var row = Box("dk-log-row").NoPick().AddTo(_log);
                Dk.Badge(c.Other, "badge-xs").AddTo(row);
                var col = Box("dk-log-col").NoPick().AddTo(row);
                Hand(Dk.F(c.Outgoing ? "desk.phone.log_out" : "desk.phone.log_in", Dk.Name(c.Other)), "dk-log-who").NoPick().AddTo(col);
                string what;
                if (c.Connected) what = c.End < 0 ? Dk.T("desk.phone.log_live") : Dk.F("desk.phone.log_talked", ClockInt((int)(c.End - c.Start)));
                else what = Dk.T("desk.phone.log_end." + c.EndReason);
                Hand(what, "dk-log-what").NoPick().AddTo(col);
                if (!c.Connected) row.AddToClassList("dk-log-row-miss");
                Box("spacer").NoPick().AddTo(row);
                MemoBold(Dk.OfficeClock(c.Start), "dk-log-time").NoPick().AddTo(row);
            }
        }
    }
}
