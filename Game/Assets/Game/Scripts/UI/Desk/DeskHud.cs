using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Office HUD-lite: a tear-off desk calendar + desk clock (top left), the call strip (top centre: dialing, in call,
    /// dead line, wiretap), the hover hint under the crosshair and a small key strip (bottom right).
    /// </summary>
    public sealed class DeskHud
    {
        readonly DeskUi _ui;
        readonly VisualElement _block, _clockBox, _keys, _hover, _call, _tap;
        readonly Label _calBand, _calDay, _calSub, _clock, _clockSub, _hoverText, _hoverSub;
        readonly Label _keyPcText;
        /// <summary>Coach-arrow anchors.</summary>
        public VisualElement ComputerKey { get; private set; }
        public VisualElement PhoneKey { get; private set; }
        int _sig = int.MinValue, _callSig = int.MinValue, _tapSig = 0, _lastSec = -1, _lastCallSec = -1, _lastTapSec = -1;
        DeskTarget _hovered = (DeskTarget)(-1);
        bool _urgent;
        Label _callTimer, _tapTimer;

        public DeskHud(DeskUi ui, VisualElement layer)
        {
            _ui = ui;
            // desk block: calendar + clock
            _block = Box("dk-deskblock").NoPick().AddTo(layer);
            var cal = Box("dk-cal").NoPick().AddTo(_block);
            var rings = Box("dk-cal-rings").NoPick().AddTo(cal);
            Box("dk-cal-ring").NoPick().AddTo(rings);
            Box("dk-cal-ring").NoPick().AddTo(rings);
            _calBand = StampText("", "dk-cal-band").NoPick().AddTo(cal);
            _calDay = Title("", "dk-cal-day").NoPick().AddTo(cal);
            _calSub = Bold("", "dk-cal-sub").NoPick().AddTo(cal);
            _clockBox = Box("dk-clock").NoPick().AddTo(_block);
            _clock = MemoBold("", "dk-clock-digits").NoPick().AddTo(_clockBox);
            _clockSub = Bold("", "dk-clock-sub").NoPick().AddTo(_clockBox);

            // call strip
            var top = Box("dk-topwrap").NoPick().AddTo(layer);
            _call = Box("dk-callstrip").AddTo(top);
            _call.Show(false);
            _tap = Box("dk-tapstrip").NoPick().AddTo(top);
            _tap.Show(false);

            // hover hint
            var mid = Box("dk-midwrap").NoPick().AddTo(layer);
            _hover = Box("dk-hover").NoPick().AddTo(mid);
            _hoverText = Bold("", "dk-hover-text").NoPick().AddTo(_hover);
            _hoverSub = Text("", "dk-hover-sub").NoPick().AddTo(_hover);
            _hover.Show(false);

            // key strip
            _keys = Box("dk-keys").NoPick().AddTo(layer);
            var k1 = Box("dk-keyhint").NoPick().AddTo(_keys);
            Dk.Key(Dk.KeyLabel(GameInput.Interact)).AddTo(k1);
            _keyPcText = Text(Dk.T("desk.key.computer"), "dk-keyhint-text").NoPick().AddTo(k1);
            ComputerKey = k1;
            var k2 = Box("dk-keyhint").NoPick().AddTo(_keys);
            PhoneKey = k2;
            Dk.Key(Dk.KeyLabel(GameInput.Phone)).AddTo(k2);
            Text(Dk.T("desk.key.phone"), "dk-keyhint-text").NoPick().AddTo(k2);
            var k3 = Box("dk-keyhint").NoPick().AddTo(_keys);
            Dk.Key(Dk.KeyLabel(GameInput.Pause)).AddTo(k3);
            Text(Dk.T("desk.key.menu"), "dk-keyhint-text").NoPick().AddTo(k3);
        }

        public void Shake() { UiMotion.Shake(_block, 12f, 0.5f); }

        public void OnConnected()
        {
            _callSig = int.MinValue;
        }

        public void Tick(LineView v, bool changed)
        {
            bool pc = _ui.ComputerOpen, phone = _ui.PhoneOpen;
            _block.Show(!pc);
            _keys.Show(!pc && !phone);

            if (changed)
            {
                int sig = Sig(v);
                if (sig != _sig)
                {
                    bool first = _sig == int.MinValue;
                    _sig = sig;
                    _calBand.SetText(Loc.Upper(Dk.F("desk.cal.band", v.Day + 1, v.DayCount)));
                    _calDay.SetText(Dk.DayName(v.Day));
                    _calSub.SetText(v.Features.IsLastDay ? Dk.T("desk.cal.last_day") : Dk.ManagerName(v.ManagerProfileId));
                    _clockSub.SetText(ClockSub(v));
                    if (first) { UiMotion.SlideIn(_block, new Vector2(-40, 0), 0.4f); UiMotion.SlideIn(_keys, new Vector2(30, 0), 0.4f, 0.2f); }
                    else UiMotion.Pulse(_clockBox, 0.06f, 0.35f);
                }
                bool blocked = Dk.ComputerBlocked(v);
                _keyPcText.SetText(blocked ? Dk.T("desk.key.computer_blocked") : Dk.T("desk.key.computer"));
            }

            int sec = Mathf.CeilToInt((float)(v.PhaseEndTime - Dk.Now));
            if (sec < 0) sec = 0;
            if (sec != _lastSec)
            {
                _lastSec = sec;
                _clock.SetText(ClockInt(sec));
                bool urgent = sec <= 10 && v.Phase == LinePhase.RoundPlay;
                if (urgent != _urgent) { _urgent = urgent; _clockBox.EnableInClassList("dk-clock-urgent", urgent); }
                if (urgent && sec > 0) UiMotion.Pulse(_clock, 0.12f, 0.3f);
            }

            TickCall(v, changed);
            TickHover(pc || phone || _ui.Cards.Visible);
        }

        static int Sig(LineView v)
        {
            int h = (int)v.Phase;
            h = h * 31 + v.Day;
            h = h * 31 + v.RoundIndex;
            h = h * 31 + v.DayCount;
            h = h * 31 + (v.Features.IsLastDay ? 1 : 0);
            h = h * 31 + (v.ManagerProfileId != null ? v.ManagerProfileId.Length : 0);
            return h;
        }

        static string ClockSub(LineView v)
        {
            if (Dk.InRound(v.Phase) && v.RoundIndex >= 0 && v.RoundIndex < v.TodayRounds.Count)
                return Dk.F("desk.clock.round", v.RoundIndex + 1, v.TodayRounds.Count, Dk.RoundName(v.TodayRounds[v.RoundIndex]));
            return Dk.PhaseName(v.Phase);
        }

        // ------------------------------------------------------------------ call strip

        void TickCall(LineView v, bool changed)
        {
            var ph = v.Mine.Phone;
            bool dead = Dk.PhoneDead(v);
            int sig = (int)ph.Status * 1000 + ph.Other + (dead ? 500 : 0) + (v.Features.RecordingsEnabled ? 7 : 0);
            if (sig != _callSig) { _callSig = sig; BuildCall(v, dead); }
            if (_callTimer != null)
            {
                int s;
                if (dead) s = Mathf.CeilToInt((float)(v.Mine.PhoneDeadUntil - Dk.Now));
                else if (ph.Status == CallStatus.Connected) s = (int)(Dk.Now - ph.Since);
                else s = Mathf.CeilToInt((float)(ph.RingUntil - Dk.Now));
                if (s < 0) s = 0;
                if (s != _lastCallSec) { _lastCallSec = s; _callTimer.SetText(ClockInt(s)); }
            }

            bool tapping = v.Mine.WiretapTarget >= 0 && v.Mine.WiretapUntil > Dk.Now;
            int tapSig = tapping ? v.Mine.WiretapTarget + 1 : 0;
            if (tapSig != _tapSig) { _tapSig = tapSig; BuildTap(v, tapping); }
            if (tapping && _tapTimer != null)
            {
                int s = Mathf.CeilToInt((float)(v.Mine.WiretapUntil - Dk.Now));
                if (s != _lastTapSec) { _lastTapSec = s; _tapTimer.SetText(ClockInt(s)); }
            }
        }

        void BuildCall(LineView v, bool dead)
        {
            var ph = v.Mine.Phone;
            _call.Clear();
            _callTimer = null;
            _lastCallSec = -1;
            bool show = dead || ph.Status == CallStatus.Dialing || ph.Status == CallStatus.Connected;
            bool wasShown = _call.style.display != DisplayStyle.None;
            _call.Show(show);
            _call.EnableInClassList("dk-callstrip-dead", dead);
            _call.EnableInClassList("dk-callstrip-live", !dead && ph.Status == CallStatus.Connected);
            if (!show) return;
            if (dead)
            {
                Dk.Icon("phone_dead", "dk-callstrip-icon").AddTo(_call);
                Bold(Dk.T("desk.call.dead"), "dk-callstrip-name").AddTo(_call);
                _callTimer = MemoBold("", "dk-callstrip-timer").AddTo(_call);
            }
            else if (ph.Status == CallStatus.Dialing)
            {
                var icon = Dk.Icon("ringing", "dk-callstrip-icon").AddTo(_call);
                Dk.Badge(ph.Other, "badge-s").AddTo(_call);
                Bold(Dk.F("desk.call.dialing", Dk.Name(ph.Other)), "dk-callstrip-name").AddTo(_call);
                _callTimer = MemoBold("", "dk-callstrip-timer", "dk-callstrip-timer-soft").AddTo(_call);
                Btn(Dk.T("desk.call.cancel"), _ui.HangUp, "btn-ghost", "btn-small", "dk-callstrip-btn").AddTo(_call);
                Dk.Key(Dk.KeyLabel(GameInput.HangUp), "dk-key-s").AddTo(_call);
                UiMotion.Pulse(icon, 0.15f, 0.5f);
            }
            else
            {
                Box("dk-live-dot").NoPick().AddTo(_call);
                Dk.Badge(ph.Other, "badge-s").AddTo(_call);
                var col = Box("dk-callstrip-col").NoPick().AddTo(_call);
                Bold(Dk.F("desk.call.with", Dk.Name(ph.Other)), "dk-callstrip-name").AddTo(col);
                Text(Dk.F("desk.call.ptt", PttLabel()), "dk-callstrip-sub").AddTo(col);
                _callTimer = MemoBold("", "dk-callstrip-timer").AddTo(_call);
                if (v.Features.RecordingsEnabled)
                {
                    var rec = Dk.Stamp(Dk.T("desk.call.on_record"), Dk.StampArt("stamp_kayitta", "stamp_on_record"), "dk-rec-stamp");
                    rec.AddTo(_call);
                    UiMotion.Stamp(rec, -6f, 0.22f, 0.25f, () => Dk.Play("stamp", null, 0.5f));
                }
                Btn(Dk.T("desk.call.hang_up"), _ui.HangUp, "btn-danger", "btn-small", "dk-callstrip-btn").AddTo(_call);
                Dk.Key(Dk.KeyLabel(GameInput.HangUp), "dk-key-s").AddTo(_call);
            }
            if (!wasShown) UiMotion.SlideIn(_call, new Vector2(0, -60), 0.3f, 0f, UiMotion.Ease.OutBack);
            else UiMotion.Pulse(_call, 0.04f, 0.25f);
        }

        void BuildTap(LineView v, bool tapping)
        {
            _tap.Clear();
            _tapTimer = null;
            _lastTapSec = -1;
            bool was = _tap.style.display != DisplayStyle.None;
            _tap.Show(tapping);
            if (!tapping) return;
            Dk.Icon("wiretap", "dk-tap-icon").AddTo(_tap);
            Bold(Dk.F("desk.call.tapping", Dk.Name(v.Mine.WiretapTarget)), "dk-tap-text").AddTo(_tap);
            _tapTimer = MemoBold("", "dk-tap-timer").AddTo(_tap);
            if (!was) UiMotion.SlideIn(_tap, new Vector2(0, -30), 0.3f);
        }

        static string _ptt = "V";
        static float _pttAt = -10f;

        /// <summary>Push-to-talk key label (VoiceSystem binding display string, refreshed at most once per second).</summary>
        public static string PttLabel()
        {
            if (Time.unscaledTime - _pttAt > 1f)
            {
                _pttAt = Time.unscaledTime;
                var vs = VoiceSystem.Instance;
                string l = vs != null ? vs.PushToTalkKey : Dk.KeyLabel(GameInput.PushToTalk);
                if (!string.IsNullOrEmpty(l)) _ptt = l;
            }
            return _ptt;
        }

        // ------------------------------------------------------------------ hover hint

        void TickHover(bool busy)
        {
            var t = busy ? DeskTarget.None : Rooms.Hovered;
            if (Rooms.Presenter != null && Rooms.Presenter.Mode != PovMode.Office) t = DeskTarget.None;
            if (t == _hovered) return;
            _hovered = t;
            if (t == DeskTarget.None) { _hover.Show(false); return; }
            _hoverText.SetText(Dk.T("desk.hover." + t));
            string sub = t == DeskTarget.Monitor ? Dk.F("desk.hover.click_key", Dk.KeyLabel(GameInput.Interact))
                : t == DeskTarget.Phone ? Dk.F("desk.hover.click_key", Dk.KeyLabel(GameInput.Phone))
                : Dk.T("desk.hover.click");
            _hoverSub.SetText(sub);
            _hover.Show(true);
            UiMotion.PopIn(_hover, 0.18f, 0f, 0.9f);
        }

        /// <summary>Mock/dev: pretend the crosshair is on a desk target.</summary>
        public void ForceHover(DeskTarget t) { Rooms.Hovered = t; _hovered = (DeskTarget)(-1); }
    }
}
