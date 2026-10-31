using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// The desk phone: an LCD (status, timers, "ON RECORD"), speed-dial keys with a paper label per colleague (digits
    /// 1-6 on the keyboard) plus a one-tap "text" key each, a busy / missed-call strip with "call back" and "send a text",
    /// and a call-log notepad. Calls are allowed in every office phase, the break included.
    /// </summary>
    public sealed class DeskPhone
    {
        readonly DeskUi _ui;
        readonly VisualElement _layer, _console, _lcdIcon, _rec, _dial, _actions, _log, _pad, _miss;
        readonly Label _lcd1, _lcd2, _lcdTime;
        bool _open;
        int _sig = int.MinValue, _lastSec = -1;
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

            _miss = Box("dk-phone-miss").AddTo(_console);
            _miss.Show(false);
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
            _layer.Show(false);
            _layer.pickingMode = PickingMode.Ignore;
        }

        public bool Back() { return false; }

        public void Digit(int d)
        {
            var v = Dk.View;
            if (v == null) return;
            if (d == 0) return;
            int i = d - 1;
            if (i >= 0 && i < _speed.Count) Press(v, _speed[i]);
        }

        public void OnMiss() { _sig = int.MinValue; }

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

        void Press(LineView v, int who) { _ui.Call(who); }

        // ------------------------------------------------------------------ tick

        public void Tick(LineView v, bool changed)
        {
            if (!_open) return;
            bool busyFlash = Time.unscaledTime - _busyAt < 2.2f;
            var ph = v.Mine.Phone;
            bool dead = Dk.PhoneDead(v);
            int sig = (int)ph.Status * 1000 + ph.Other;
            sig = sig * 31 + (dead ? 1 : 0) + (v.Features.RecordingsEnabled ? 8 : 0) + (busyFlash ? 16 : 0) + (v.Features.CallsOpen ? 32 : 0);
            sig = sig * 31 + (int)_ui.State.Miss * 10 + _ui.State.MissWho + (int)_ui.State.MissAt;
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
            if (dead) { Lcd(Dk.T("desk.phone.lcd.dead"), Dk.T("desk.phone.lcd.dead_2")); icon = "deskphone/line_dead"; }
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
            _console.EnableInClassList("dk-phone-dead", dead);

            // busy / missed strip: call back or send a text
            BuildMiss(v, dead);

            // speed dial
            _dial.Clear();
            _speed.Clear();
            bool idle = ph.Status == CallStatus.Idle && !dead && v.Features.CallsOpen;
            int n = 0;
            for (int i = 0; i < v.Players.Count; i++)
            {
                var p = v.Players[i];
                if (p.Id == v.Me) continue;
                n++;
                _speed.Add(p.Id);
                int who = p.Id;
                var row = Box("dk-dial-row").AddTo(_dial);
                var call = Dk.Tile(() => Press(Dk.View, who), "dk-dial-callarea").AddTo(row);
                var key = Box("dk-dial-key").NoPick().AddTo(call);
                Dk.Skin(key, "deskphone/key");
                MemoBold(Num(n), "dk-dial-key-text").NoPick().AddTo(key);
                Dk.Badge(who, "badge-s").AddTo(call);
                Bold(p.Name, "dk-dial-name").Plain().NoPick().AddTo(call);
                Box("spacer").NoPick().AddTo(call);
                string tag = null;
                if (ph.Other == who && ph.Status == CallStatus.Connected) tag = Dk.T("desk.phone.tag.talking");
                else if (ph.Other == who && ph.Status != CallStatus.Idle) tag = Dk.T("desk.phone.tag.ringing");
                else if (busyFlash && _busyWho == who) tag = Dk.T("desk.phone.tag.busy");
                else if (!p.Connected) tag = Dk.T("desk.away");
                if (tag != null) Tag(tag, ph.Other == who ? "tag-green" : "tag-grey").AddTo(call);
                Dk.Icon("call", "dk-dial-call").AddTo(call);
                call.SetEnabled(idle && p.Connected);
                var text = IconBtn("text", Dk.T("desk.phone.text"), () => { _ui.OpenMessages(who); }, "btn-secondary", "btn-small", "dk-dial-text").AddTo(row);
                text.SetEnabled(p.Connected || true);
            }

            // actions
            _actions.Clear();
            switch (ph.Status)
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
            Dk.NoRich(_dial);
            Dk.NoRich(_miss);
        }

        void BuildMiss(LineView v, bool dead)
        {
            var st = _ui.State;
            bool show = st.Miss != MissKind.None && st.MissWho >= 0 && v.Mine.Phone.Status == CallStatus.Idle && Dk.Now - st.MissAt < 90;
            _miss.Clear();
            _miss.Show(show);
            if (!show) return;
            int who = st.MissWho;
            string key = st.Miss == MissKind.Missed ? "desk.phone.miss.missed" : st.Miss == MissKind.Busy ? "desk.phone.miss.busy" : "desk.phone.miss.noanswer";
            Dk.Icon(st.Miss == MissKind.Missed ? "missed_call" : "busy", "dk-miss-icon").NoPick().AddTo(_miss);
            Dk.Badge(who, "badge-s").AddTo(_miss);
            var col = Box("dk-miss-col").NoPick().AddTo(_miss);
            Bold(Dk.F(key, Dk.Name(who)), "dk-miss-title").Plain().NoPick().AddTo(col);
            Text(Dk.T("desk.phone.miss.sub"), "dk-miss-sub").NoPick().AddTo(col);
            Box("spacer").NoPick().AddTo(_miss);
            var p = Dk.Player(who);
            var cb = Btn(Dk.T("desk.phone.miss.callback"), () => { _ui.Call(who); }, "btn-primary", "btn-small").AddTo(_miss);
            cb.SetEnabled(!dead && p != null && p.Connected && v.Features.CallsOpen);
            Btn(Dk.T("desk.phone.miss.text"), () => _ui.OpenMessages(who), "btn-secondary", "btn-small").AddTo(_miss);
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
                MemoBold(Dk.Ago(c.Start), "dk-log-time").NoPick().AddTo(row);
            }
        }
    }
}
