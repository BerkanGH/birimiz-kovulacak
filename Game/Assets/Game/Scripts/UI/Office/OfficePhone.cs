using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.PlayKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// The desk phone's face while the handset is up: a green LCD (line state, who, timer, REC), the contacts = the
    /// public line board (each colleague with colour and status: available / on the phone with whom and how long /
    /// ringing / offline, and a Call button), the call actions (answer, decline, hang up, record) and missed calls
    /// with a call-back button. Shown on the left half of the screen; the 3D phone stays visible on the right.
    /// v5 step 3a (partner call): during a pair / trio task (Client Bonus, Petty Cash) the task partners are pinned at
    /// the top and marked ORTAK; Tab opens the phone with the first partner selected and E (or Enter) calls the selected
    /// contact (↑ / ↓ change it). Nothing is called automatically: whom to call stays the player's choice.
    /// </summary>
    public sealed class OfficePhone
    {
        readonly OfficeUi _ui;
        public readonly VisualElement Root;
        readonly VisualElement _face, _rec;
        readonly Label _lcd1, _lcd2, _lcdTime, _footer;
        readonly PlaySection _contacts, _actions, _missed;
        bool _open, _dirty = true;
        int _selected = -1;
        readonly List<int> _order = new List<int>();

        public bool IsOpen => _open;
        /// <summary>The contact E / Enter calls (-1 none).</summary>
        public int Selected => _selected;

        public OfficePhone(OfficeUi ui, VisualElement parent)
        {
            _ui = ui;
            Root = Box("ph-wrap").AddTo(parent);
            _face = Box("ph-face").AddTo(Root);
            var top = Box("ph-top").NoPick().AddTo(_face);
            Title(T("office.phone.brand"), "ph-brand").NoPick().AddTo(top);
            Box("grow").NoPick().AddTo(top);
            Box("ph-led").NoPick().AddTo(top);
            var lcd = Box("ph-lcd").NoPick().AddTo(_face);
            var lrow = Box("ph-lcd-row").NoPick().AddTo(lcd);
            _lcd1 = UiKit.MemoBold("", "ph-lcd-1").NoPick().AddTo(lrow);
            Box("grow").NoPick().AddTo(lrow);
            _lcdTime = UiKit.MemoBold("", "ph-lcd-time").NoPick().AddTo(lrow);
            _lcd2 = UiKit.Memo("", "ph-lcd-2").NoPick().AddTo(lcd);
            _rec = Box("ph-rec").NoPick().AddTo(lcd);
            Box("ph-rec-dot").NoPick().AddTo(_rec);
            Bold(T("office.phone.rec"), "ph-rec-text").NoPick().AddTo(_rec);
            _rec.Show(false);
            _actions = new PlaySection(_face, "ph-actions");
            Bold(T("office.phone.contacts"), "ph-head").NoPick().AddTo(_face);
            _contacts = new PlaySection(_face, "ph-contacts");
            _missed = new PlaySection(_face, "ph-missed");
            _footer = Text("", "ph-footer").NoPick().AddTo(_face);
            Root.Show(false);
        }

        /// <summary>Handset up; <paramref name="select"/> preselects a contact (the task partner) for E / Enter.</summary>
        public void Open(int select = -1)
        {
            if (select >= 0) { _selected = select; _dirty = true; }
            if (_open) return;
            _open = true;
            _dirty = true;
            Root.Show(true);
            Root.pickingMode = PickingMode.Ignore;
            UiMotion.SlideIn(_face, new Vector2(-40f, 0f), 0.24f, 0f, UiMotion.Ease.OutCubic);
            UiMotion.FadeIn(_face, 0.18f);
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            Root.Show(false);
        }

        public void MarkDirty() { _dirty = true; }

        /// <summary>E / Enter: call the selected contact (when the line is free and calls are open).</summary>
        public void CallSelected(LineView v)
        {
            if (_selected < 0 || v == null) return;
            if (!v.Features.CallsOpen || v.Mine.Phone.Status != CallStatus.Idle) return;
            LinePlayerView pl = null;
            foreach (var x in v.Players) if (x.Id == _selected) pl = x;
            if (pl == null || !pl.Connected) { Sfx.Play("deny", 0.5f); return; }
            Sfx.Play("tip_dial", 0.6f);
            Send(LineVerb.Call, _selected);
        }

        /// <summary>↑ / ↓: move the selection through the contacts (partners first).</summary>
        public void MoveSelection(int d)
        {
            if (_order.Count == 0) return;
            int i = _order.IndexOf(_selected);
            i = i < 0 ? 0 : (i + d + _order.Count) % _order.Count;
            _selected = _order[i];
            _dirty = true;
            Sfx.Play("click", 0.35f);
        }

        public void Tick(LineClient lc, LineView v, double now, bool dirty, bool live)
        {
            if (!_open) return;
            UpdateLcd(lc, v, now);
            if (dirty || _dirty)
            {
                _dirty = false;
                _actions.Update(p => BuildActions(p, v));
                _contacts.Update(p => BuildContacts(p, lc, v, now));
                _missed.Update(p => BuildMissed(p, v, now));
            }
            if (live) { _contacts.TickLive(now); _missed.TickLive(now); }
            bool call = v.Mine.Phone.Status == CallStatus.Connected || v.Mine.Phone.Status == CallStatus.Dialing;
            _footer.SetText(F(call ? "office.phone.footer_call" : "office.phone.footer", OfficeText.Key(GameInput.Phone)));
        }

        void UpdateLcd(LineClient lc, LineView v, double now)
        {
            var ph = v.Mine.Phone;
            string l1, l2, time = "";
            bool rec = false;
            switch (ph.Status)
            {
                case CallStatus.Dialing:
                    l1 = F("office.phone.lcd_dialing", Name(ph.Other));
                    l2 = F("office.phone.lcd_dialing_hint", OfficeText.Key(GameInput.HangUp));
                    time = UiKit.Clock(Math.Max(0, ph.RingUntil - now));
                    break;
                case CallStatus.Ringing:
                    l1 = F("office.phone.lcd_ringing", Name(ph.Other));
                    l2 = F("office.ring_hint", OfficeText.Key(GameInput.AnswerCall), OfficeText.Key(GameInput.HangUp));
                    time = UiKit.Clock(Math.Max(0, ph.RingUntil - now));
                    break;
                case CallStatus.Connected:
                    l1 = F("office.phone.lcd_connected", Name(ph.Other));
                    rec = PlayBlocks.RecordingCurrentCall(v);
                    l2 = rec ? T("office.phone.lcd_recording")
                        : v.Features.RecordingsEnabled && v.Mine.RecorderUsesLeft > 0 ? F("office.phone.lcd_connected_hint", OfficeText.Key(GameInput.Record), OfficeText.Key(GameInput.HangUp))
                        : F("office.phone.lcd_connected_hint_norec", OfficeText.Key(GameInput.HangUp));
                    time = UiKit.Clock(Math.Max(0, now - ph.Since));
                    break;
                default:
                    l1 = T(v.Features.CallsOpen ? "office.phone.lcd_idle" : "office.phone.lcd_closed");
                    l2 = T(v.Features.CallsOpen ? "office.phone.lcd_idle_hint" : "phone.closed");
                    if (v.Features.CallsOpen && _selected >= 0)
                    {
                        bool partner = OfficeText.Partners(lc, v).Contains(_selected);
                        l1 = F(partner ? "office.phone.lcd_partner" : "office.phone.lcd_selected", Name(_selected));
                        l2 = F("office.phone.lcd_call_hint", OfficeText.Key(GameInput.Interact));
                    }
                    break;
            }
            _lcd1.SetText(Loc.Upper(l1));
            _lcd2.SetText(l2);
            _lcdTime.SetText(time);
            _rec.Show(rec);
            _face.EnableInClassList("ph-face-ringing", ph.Status == CallStatus.Ringing);
        }

        void BuildActions(VisualElement p, LineView v)
        {
            var ph = v.Mine.Phone;
            var row = Box("ph-row").AddTo(p);
            switch (ph.Status)
            {
                case CallStatus.Ringing:
                    Btn(T("phone.answer") + "  (" + OfficeText.Key(GameInput.AnswerCall) + ")", () => Send(LineVerb.Answer), "pl-primary", "pl-big").AddTo(row);
                    Btn(T("phone.decline") + "  (" + OfficeText.Key(GameInput.HangUp) + ")", () => Send(LineVerb.Decline), "pl-danger", "pl-big").AddTo(row);
                    break;
                case CallStatus.Dialing:
                    Btn(T("phone.hangup") + "  (" + OfficeText.Key(GameInput.HangUp) + ")", () => Send(LineVerb.HangUp), "pl-danger", "pl-big").AddTo(row);
                    break;
                case CallStatus.Connected:
                    Btn(T("phone.hangup") + "  (" + OfficeText.Key(GameInput.HangUp) + ")", () => Send(LineVerb.HangUp), "pl-danger", "pl-big").AddTo(row);
                    if (v.Features.RecordingsEnabled)
                    {
                        bool rec = PlayBlocks.RecordingCurrentCall(v);
                        if (rec) Bold(T("office.phone.recording_on"), "ph-recording").AddTo(row);
                        else
                        {
                            var b = Btn(F("office.phone.record_btn", OfficeText.Key(GameInput.Record), v.Mine.RecorderUsesLeft), () => _ui.Record(), "ph-recbtn", "pl-big").AddTo(row);
                            b.SetEnabled(v.Mine.RecorderUsesLeft > 0);
                        }
                    }
                    break;
                default:
                    Text(T(v.Features.CallsOpen ? "office.phone.pick" : "phone.closed"), "ph-hint").AddTo(row);
                    break;
            }
        }

        void BuildContacts(VisualElement p, LineClient lc, LineView v, double now)
        {
            var f = v.Features;
            var mine = v.Mine.Phone;
            int me = lc.MySlot;
            // task partners first (pinned, ORTAK), then everyone else in seat order
            var partners = OfficeText.Partners(lc, v);
            var list = new List<LinePlayerView>();
            foreach (int pid in partners) foreach (var pl in v.Players) if (pl.Id == pid) list.Add(pl);
            foreach (var pl in v.Players) if (pl.Id != me && !partners.Contains(pl.Id)) list.Add(pl);
            _order.Clear();
            foreach (var pl in list) _order.Add(pl.Id);
            if (_selected >= 0 && !_order.Contains(_selected)) _selected = -1;
            foreach (var pl in list)
            {
                int id = pl.Id;
                bool partner = partners.Contains(id);
                bool sel = id == _selected && mine.Status == CallStatus.Idle && f.CallsOpen;
                var row = Box("ph-contact", partner ? "ph-contact-partner" : null, sel ? "ph-contact-sel" : null).AddTo(p);
                var dot = Box("ph-dot").AddTo(row);
                dot.style.backgroundColor = PlayerColor(pl.ColorIndex);
                var names = Box("ph-contact-names").AddTo(row);
                var top = Box("pl-row").AddTo(names);
                Bold(pl.Name, "ph-name").AddTo(top);
                if (partner) Tag(top, T("office.phone.partner_tag"), "pl-tag-partner");
                if (pl.IsBot) Tag(top, T("players.bot"));
                Live(names, "st" + id, now, (l, t) => ContactStatus(l, id, t), "ph-status");
                bool can = f.CallsOpen && mine.Status == CallStatus.Idle && pl.Connected;
                var b = Btn(sel ? F("office.phone.call_key", OfficeText.Key(GameInput.Interact)) : T("act.call"), () => { Sfx.Play("tip_dial", 0.6f); Send(LineVerb.Call, id); }, "ph-call", pl.IsBot ? "pl-dimbtn" : null, sel ? "pl-primary" : null).AddTo(row);
                b.SetEnabled(can);
            }
        }

        /// <summary>Live status of a contact from the current view: offline / on the phone with whom and for how long / ringing / available.</summary>
        static void ContactStatus(Label l, int id, double now)
        {
            var v = LineClient.I != null ? LineClient.I.View : null;
            if (v == null) return;
            LinePlayerView pl = null;
            foreach (var x in v.Players) if (x.Id == id) pl = x;
            string text, cls;
            LineBoardCall call = null;
            foreach (var c in v.LineBoard) if (c.Caller == id || c.Callee == id) call = c;
            if (pl == null || !pl.Connected) { text = T("office.phone.st_offline"); cls = "ph-st-off"; }
            else if (call != null && call.Status == CallStatus.Connected)
            {
                int other = call.Caller == id ? call.Callee : call.Caller;
                text = F("office.phone.st_talking", Name(other), UiKit.Clock(Math.Max(0, now - call.Start)));
                cls = "ph-st-busy";
            }
            else if (call != null)
            {
                bool me = LineClient.I != null && call.Callee == LineClient.I.MySlot;
                text = T(call.Caller == id ? (me ? "office.phone.st_calling_you" : "office.phone.st_calling") : "office.phone.st_ringing");
                cls = "ph-st-busy";
            }
            else if (pl.IsBot) { text = T("office.phone.st_bot"); cls = "ph-st-off"; }
            else { text = T("office.phone.st_free"); cls = "ph-st-free"; }
            l.SetText(text);
            l.EnableInClassList("ph-st-off", cls == "ph-st-off");
            l.EnableInClassList("ph-st-busy", cls == "ph-st-busy");
            l.EnableInClassList("ph-st-free", cls == "ph-st-free");
        }

        void BuildMissed(VisualElement p, LineView v, double now)
        {
            var log = v.Mine.CallLog;
            int shown = 0;
            for (int i = log.Count - 1; i >= 0 && shown < 3; i--)
            {
                var c = log[i];
                if (c.Connected || c.Outgoing || c.EndReason != CallEndReason.Missed) continue;
                if (shown == 0) Bold(T("office.phone.missed"), "ph-head", "ph-head-missed").AddTo(p);
                shown++;
                int other = c.Other;
                double end = c.End >= 0 ? c.End : c.Start;
                var row = Box("ph-missed-row").AddTo(p);
                Live(row, "miss" + c.CallId + ":" + end, now, t => F("office.phone.missed_line", Name(other), UiKit.Clock(Math.Max(0, t - end))), "ph-missed-text", "grow");
                var b = Btn(T("office.phone.call_back"), () => Send(LineVerb.Call, other), "pl-small").AddTo(row);
                b.SetEnabled(v.Features.CallsOpen && v.Mine.Phone.Status == CallStatus.Idle);
            }
        }
    }
}
