using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.PlayKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 office layer (docs/V5_INTERACTION.md §2, §4, step 1): everything on screen while the player is in their own
    /// office (Intro, tasks, breaks, crisis calls). Permanently only a thin top strip (phase, timer, prim, recorder
    /// tapes, one "Şimdi: …" line); toasts in a corner (texts, prim received, missed calls, phase changes); the
    /// incoming-call card; the use prompt under the crosshair. Opens the desk objects: the computer
    /// (<see cref="OfficeComputer"/>, Q / E on the monitor), the desk phone (<see cref="OfficePhone"/>, Tab / E on
    /// the phone), the cork board zoom (E) and the cassette recorder (R / E). Y / N answer / decline from anywhere.
    /// Drives the camera through <see cref="RoomsPresenter"/> (computer and phone sit you down; Esc goes back).
    /// </summary>
    public sealed class OfficeUi : UiKit.Screen
    {
        public enum Focus { None, Computer, Phone, Board }

        public static OfficeUi I { get; private set; }

        readonly VisualElement _strip, _call, _ring, _toasts, _prompt, _boardHint, _mic, _tapes;
        readonly Label _phase, _timer, _prim, _now, _callText, _callTime, _callRec, _ringName, _ringHint, _ringTime, _promptKey, _promptText, _promptAlt, _micText;
        readonly OfficeComputer _computer;
        readonly OfficePhone _phone;
        readonly CorkBoard _board = new CorkBoard();
        readonly PlayState _st = new PlayState();
        readonly List<KeyValuePair<VisualElement, float>> _toastList = new List<KeyValuePair<VisualElement, float>>();
        Focus _focus;
        bool _bound, _dirty = true, _ringLoop, _backLoop;
        int _week = int.MinValue, _tapeCount = -1;
        CallStatus _lastStatus;
        int _lastOther = LineIds.None, _lastCall = LineIds.None;
        bool _lastRecording;
        float _nextLive;
        DeskTarget _promptFor = DeskTarget.None;
        string _promptSig;

        public Focus Current => _focus;
        /// <summary>The computer or the phone panel is open: the cursor is free.</summary>
        public bool WantsCursor => _focus == Focus.Computer || _focus == Focus.Phone;
        public bool PhoneOpen => _focus == Focus.Phone;
        public OfficeComputer Computer => _computer;

        public OfficeUi(VisualElement parent) : base(parent, "of-screen")
        {
            I = this;
            var sheet = Resources.Load<StyleSheet>("ui/styles/play");
            if (sheet != null) Root.styleSheets.Add(sheet);
            var office = Resources.Load<StyleSheet>("ui/styles/office");
            if (office != null) Root.styleSheets.Add(office);
            Root.pickingMode = PickingMode.Ignore;
            _st.Dirty = () => _dirty = true;
            _st.Toast = t => Toast(t, "of-toast-warn");

            // the computer and the phone sit under the HUD
            _computer = new OfficeComputer(this, Root, _st);
            _st.FocusMessageField = _computer.FocusField;
            _phone = new OfficePhone(this, Root);

            // ---- top strip
            _strip = Box("of-strip").NoPick().AddTo(Root);
            var main = Box("of-strip-main").NoPick().AddTo(_strip);
            _phase = Bold("", "of-phase").NoPick().AddTo(main);
            _timer = Title("", "of-timer").NoPick().AddTo(main);
            Box("of-sep").NoPick().AddTo(main);
            _prim = Bold("", "of-prim").NoPick().AddTo(main);
            Box("of-sep").NoPick().AddTo(main);
            _tapes = Box("of-tapes").NoPick().AddTo(main);
            var nowRow = Box("of-now-row").NoPick().AddTo(_strip);
            Bold(T("office.now_label"), "of-now-label").NoPick().AddTo(nowRow);
            _now = Text("", "of-now").NoPick().AddTo(nowRow);

            // ---- call pill (dialing / connected) and the incoming call card
            _call = Box("of-call").NoPick().AddTo(Root);
            Box("of-call-dot").NoPick().AddTo(_call);
            _callText = Bold("", "of-call-text").NoPick().AddTo(_call);
            _callTime = Title("", "of-call-time").NoPick().AddTo(_call);
            _callRec = Bold(T("office.phone.rec"), "of-call-rec").NoPick().AddTo(_call);
            _call.Show(false);
            _ring = Box("of-ring").NoPick().AddTo(Root);
            var ringTop = Box("of-ring-top").NoPick().AddTo(_ring);
            Box("of-ring-bell").NoPick().AddTo(ringTop);
            _ringName = Title("", "of-ring-name").NoPick().AddTo(ringTop);
            _ringTime = Bold("", "of-ring-time").NoPick().AddTo(ringTop);
            _ringHint = Bold("", "of-ring-hint").NoPick().AddTo(_ring);
            _ring.Show(false);

            // ---- mic (push-to-talk held)
            _mic = Box("of-mic").NoPick().AddTo(Root);
            Box("of-mic-dot").NoPick().AddTo(_mic);
            _micText = Bold("", "of-mic-text").NoPick().AddTo(_mic);
            _mic.Show(false);

            // ---- toasts (top right, under the strip)
            _toasts = Box("of-toasts").NoPick().AddTo(Root);

            // ---- use prompt under the crosshair, board hint
            _prompt = Box("of-prompt").NoPick().AddTo(Root);
            _promptKey = UiKit.KeyCap("E", "of-prompt-key").NoPick().AddTo(_prompt);
            _promptText = Bold("", "of-prompt-text").NoPick().AddTo(_prompt);
            _promptAlt = Text("", "of-prompt-alt").NoPick().AddTo(_prompt);
            _prompt.Show(false);
            _boardHint = Box("of-board-hint").NoPick().AddTo(Root);
            Bold(T("office.board_hint"), "of-board-hint-text").NoPick().AddTo(_boardHint);
            _boardHint.Show(false);
        }

        // ------------------------------------------------------------------ lifecycle

        protected override void OnShow() { _dirty = true; }

        protected override void OnHide()
        {
            CloseFocus(false);
            StopLoops();
            _prompt.Show(false);
        }

        void StopLoops()
        {
            if (_ringLoop) { _ringLoop = false; Sfx.StopLoop("desk_ring"); }
            if (_backLoop) { _backLoop = false; Sfx.StopLoop("ringback"); }
        }

        void Bind()
        {
            var lc = LineClient.I;
            if (_bound || lc == null) return;
            _bound = true;
            lc.ViewChanged += OnView;
            lc.MessagesReceived += OnMessages;
            lc.EventReceived += OnEvent;
            lc.ResultReceived += OnResult;
            lc.PhaseChanged += OnPhase;
            Rooms.DeskClicked += OnDesk;
        }

        /// <summary>UIRoot.Rebuild (language switch) drops this screen and builds a new one: stop listening then.</summary>
        bool Detached()
        {
            if (Root.panel != null) return false;
            var lc = LineClient.I;
            if (_bound && lc != null)
            {
                lc.ViewChanged -= OnView;
                lc.MessagesReceived -= OnMessages;
                lc.EventReceived -= OnEvent;
                lc.ResultReceived -= OnResult;
                lc.PhaseChanged -= OnPhase;
            }
            if (_bound) Rooms.DeskClicked -= OnDesk;
            _bound = false;
            _board.Release();
            StopLoops();
            return true;
        }

        void OnView(LineView o, LineView n) { if (!Detached()) _dirty = true; }

        void OnMessages(IReadOnlyList<LineMessage> list)
        {
            if (Detached()) return;
            _dirty = true;
            var lc = LineClient.I;
            if (!Visible || lc == null) return;
            foreach (var m in list)
            {
                if (m.From == lc.MySlot || m.Kind == MessageKind.MeetingChat) continue;
                string text = m.Text ?? "";
                if (text.Length > 70) text = text.Substring(0, 68) + "…";
                string who = m.Kind == MessageKind.System ? T("msg.system") : Name(m.From) + ":";
                Toast(who + " " + text + "  ·  " + F("office.toast_open_messages", OfficeText.Key(GameInput.Computer)), "of-toast-msg", 6f);
                Sfx.Play("email_ding", 0.7f);
            }
        }

        void OnEvent(LineEvent e)
        {
            if (Detached()) return;
            _dirty = true;
            if (!Visible) return;
            var lc = LineClient.I;
            int me = lc != null ? lc.MySlot : -1;
            switch (e.Type)
            {
                case LineEventType.TransferReceived:
                    Toast(PlayText.Event(e, me), "of-toast-money", 6f);
                    Sfx.Play("complete", 0.6f);
                    break;
                case LineEventType.MissedCall:
                    Toast(PlayText.Event(e, me), "of-toast-call", 6f);
                    break;
                case LineEventType.CallBusy:
                    Toast(F("office.toast_busy", Name(e.Target)), "of-toast-call");
                    Sfx.Play("busy", 0.7f);
                    break;
                case LineEventType.CallConnected:
                    Sfx.Play("call_connect", 0.8f);
                    break;
                case LineEventType.CallEnded:
                {
                    var reason = (CallEndReason)e.A;
                    Sfx.Play("call_end", 0.7f);
                    if (reason == CallEndReason.Missed && _lastStatus == CallStatus.Ringing) Toast(F("office.toast_missed_in", Name(e.Actor)), "of-toast-call", 6f);
                    else if (reason == CallEndReason.Missed && _lastStatus == CallStatus.Dialing) Toast(F("office.toast_no_answer", Name(e.Actor)), "of-toast-call");
                    else if (reason == CallEndReason.Declined && _lastStatus == CallStatus.Dialing) Toast(F("office.toast_declined", Name(e.Actor)), "of-toast-call");
                    else if (reason == CallEndReason.HungUp && _lastStatus == CallStatus.Connected) Toast(F("office.toast_call_over", Name(e.Actor)), "of-toast-call", 3.5f);
                    break;
                }
                case LineEventType.Whistled:
                case LineEventType.BlameAdded:
                case LineEventType.Presence:
                {
                    string line = PlayText.Event(e, me);
                    if (line != null) Toast(line, e.Type == LineEventType.Presence ? null : "of-toast-warn");
                    if (e.Type == LineEventType.Whistled) Sfx.Play("stamp", 0.6f);
                    break;
                }
            }
        }

        void OnResult(LineActionResult r, LineAction req)
        {
            if (Detached() || !Visible || r == null || r.Accepted || r.Failure == LineFailure.Duplicate) return;
            Toast(F("toast.rejected", req != null ? T("verb." + req.Verb) : "?", PlayText.Failure(r.Failure)), "of-toast-warn");
            Sfx.Play("deny", 0.6f);
        }

        void OnPhase(LinePhase from, LinePhase to)
        {
            if (Detached()) return;
            if (to == LinePhase.None) { CloseFocus(false); _pendingPhaseToast = null; return; }
            var lc = LineClient.I;
            var v = lc != null ? lc.View : null;
            if (v == null || !LineClient.IsCallsPhase(to)) return;
            if (to == LinePhase.Intro) return; // the strip says it
            _pendingPhaseToast = PlayText.PhaseTitle(v);
            if (_focus == Focus.Computer) _computer.MarkDirty();
        }

        string _pendingPhaseToast;

        // ------------------------------------------------------------------ desk objects

        void OnDesk(DeskTarget t)
        {
            if (Detached() || !Visible) return;
            switch (t)
            {
                case DeskTarget.Monitor: OpenComputer(); break;
                case DeskTarget.Phone: OpenPhone(); break;
                case DeskTarget.Recorder: Record(); break;
                case DeskTarget.Board: OpenBoard(); break;
                case DeskTarget.Drawer:
                {
                    var v = LineClient.I != null ? LineClient.I.View : null;
                    if (v != null) Toast(F("office.toast_drawer", v.Mine.Prim), "of-toast-money", 3.5f);
                    break;
                }
            }
        }

        public void OpenComputer(OfficeComputer.App? app = null)
        {
            var rooms = RoomsPresenter.Instance;
            if (rooms == null || rooms.InMeeting) return;
            if (_focus == Focus.Phone) _phone.Close();
            _focus = Focus.Computer;
            rooms.SetPovMode(PovMode.Monitor);
            _computer.Open(app ?? _computer.Current);
            Sfx.Play("click", 0.5f);
        }

        public void OpenPhone()
        {
            var rooms = RoomsPresenter.Instance;
            if (rooms == null || rooms.InMeeting) return;
            if (_focus == Focus.Computer) _computer.Close();
            bool idle = LineClient.I != null && LineClient.I.View != null && LineClient.I.View.Mine.Phone.Status == CallStatus.Idle;
            _focus = Focus.Phone;
            rooms.SetPovMode(PovMode.Phone);
            _phone.Open();
            if (idle) Sfx.Play("offhook", 0.7f);
        }

        public void OpenBoard()
        {
            var rooms = RoomsPresenter.Instance;
            if (rooms == null || rooms.InMeeting) return;
            _computer.Close();
            _phone.Close();
            _focus = Focus.Board;
            rooms.SetPovMode(PovMode.Board);
        }

        /// <summary>Back to looking around the office (Esc, the close buttons).</summary>
        public void CloseFocus() { CloseFocus(true); }

        void CloseFocus(bool camera)
        {
            if (_focus == Focus.None) return;
            var was = _focus;
            _focus = Focus.None;
            _computer.Close();
            _phone.Close();
            var rooms = RoomsPresenter.Instance;
            if (camera && rooms != null && !rooms.InMeeting) rooms.SetPovMode(PovMode.Office);
            if (was == Focus.Phone)
            {
                var v = LineClient.I != null ? LineClient.I.View : null;
                if (v != null && v.Mine.Phone.Status == CallStatus.Idle) Sfx.Play("place", 0.6f);
            }
        }

        /// <summary>Esc from UIRoot: closes the open desk object; false when nothing was open (UIRoot opens the menu).</summary>
        public bool Back()
        {
            if (!Visible || _focus == Focus.None) return false;
            CloseFocus();
            return true;
        }

        /// <summary>R / the recorder's red key / the phone's record button.</summary>
        public void Record()
        {
            var lc = LineClient.I;
            var v = lc != null ? lc.View : null;
            var rooms = RoomsPresenter.Instance;
            if (v == null) return;
            if (rooms != null && rooms.Office != null) rooms.Office.PressRecord();
            Sfx.Play("rec_clunk", 0.7f);
            var m = v.Mine;
            if (!v.Features.RecordingsEnabled) { Toast(T("office.rec_disabled"), "of-toast-warn"); return; }
            if (m.Phone.Status != CallStatus.Connected) { Toast(T("office.rec_not_in_call"), "of-toast-warn"); Sfx.Play("deny", 0.5f); return; }
            if (PlayBlocks.RecordingCurrentCall(v)) { Toast(T("office.rec_already"), null, 3f); return; }
            if (m.RecorderUsesLeft <= 0) { Toast(T("office.rec_empty"), "of-toast-warn"); Sfx.Play("deny", 0.5f); return; }
            Send(LineVerb.Record);
        }

        // ------------------------------------------------------------------ per frame

        public override void Tick()
        {
            if (!Visible) return;
            Bind();
            var lc = LineClient.I;
            var v = lc != null ? lc.View : null;
            if (v == null) return;
            if (v.WeekId != _week)
            {
                _week = v.WeekId;
                _st.ResetWeek();
                _computer.ResetWeek();
                _lastRecording = false;
                _dirty = true;
            }
            double now = lc.Now;
            float dt = Time.unscaledDeltaTime;
            if (!LineClient.IsCallsPhase(v.Phase) && _focus != Focus.None) CloseFocus(false);
            HandleKeys(lc, v);
            SyncCamera();
            UpdateStrip(lc, v, now);
            UpdateCall(lc, v, now);
            UpdateMic(v);
            UpdatePrompt(v);
            UpdateWorld(lc, v);
            bool live = Time.unscaledTime >= _nextLive;
            if (live) _nextLive = Time.unscaledTime + 0.5f;
            bool dirty = _dirty;
            _dirty = false;
            PlaceComputer(dt);
            _computer.Tick(lc, v, now, dirty, live);
            _phone.Tick(lc, v, now, dirty, live);
            _boardHint.Show(_focus == Focus.Board);
            _strip.Show(_focus == Focus.None || _focus == Focus.Phone);
            if (_pendingPhaseToast != null && ScreenFade.Alpha < 0.5f)
            {
                Toast(_pendingPhaseToast, "of-toast-phase", 4f);
                Sfx.Play("phase", 0.5f);
                _pendingPhaseToast = null;
            }
            TickToasts();
        }

        void HandleKeys(LineClient lc, LineView v)
        {
            if (UIRoot.I != null && (UIRoot.I.TextInputFocused || UIRoot.I.OverlayOpen)) return;
            if (ScreenFade.Alpha > 0.5f) return;
            var ph = v.Mine.Phone;
            if (Pressed(GameInput.Computer)) { if (_focus == Focus.Computer) CloseFocus(); else OpenComputer(); }
            if (Pressed(GameInput.Phone)) { if (_focus == Focus.Phone) CloseFocus(); else OpenPhone(); }
            if (Pressed(GameInput.Record)) Record();
            if (Pressed(GameInput.AnswerCall) && ph.Status == CallStatus.Ringing) lc.Send(LineVerb.Answer);
            if (Pressed(GameInput.HangUp))
            {
                if (ph.Status == CallStatus.Ringing) lc.Send(LineVerb.Decline);
                else if (ph.Status == CallStatus.Connected || ph.Status == CallStatus.Dialing) lc.Send(LineVerb.HangUp);
                else if (_focus == Focus.Phone) CloseFocus();
            }
            if (_focus == Focus.Board && Pressed(GameInput.Interact)) CloseFocus();
        }

        static bool Pressed(UnityEngine.InputSystem.InputAction a) { return a != null && a.WasPressedThisFrame(); }

        /// <summary>The office camera follows what is open (re-applied after the world re-enters the office).</summary>
        void SyncCamera()
        {
            var rooms = RoomsPresenter.Instance;
            if (rooms == null || rooms.InMeeting || rooms.Mode == PovMode.Cutscene) return;
            var want = _focus == Focus.Computer ? PovMode.Monitor : _focus == Focus.Phone ? PovMode.Phone : _focus == Focus.Board ? PovMode.Board : PovMode.Office;
            if (rooms.Mode != want) rooms.SetPovMode(want);
        }

        void UpdateStrip(LineClient lc, LineView v, double now)
        {
            _phase.SetText(PlayText.PhaseTitle(v));
            double left = Math.Max(0, v.PhaseEndTime - now);
            _timer.SetText(UiKit.Clock(left));
            _timer.EnableInClassList("of-timer-low", left < 10 && left > 0);
            _prim.SetText(F("header.my_prim", v.Mine.Prim));
            int tapes = v.Features.RecordingsEnabled ? Math.Max(0, v.Mine.RecorderUsesLeft) : -1;
            if (tapes != _tapeCount)
            {
                _tapeCount = tapes;
                _tapes.Clear();
                _tapes.Show(tapes >= 0);
                if (tapes >= 0)
                {
                    Bold(T("office.tapes"), "of-tapes-label").NoPick().AddTo(_tapes);
                    int max = Math.Max(1, Math.Max(tapes, lc.Config.RecorderUses));
                    for (int i = 0; i < Math.Min(max, 3); i++)
                    {
                        var c = Box("of-cassette", i < tapes ? null : "of-cassette-used").NoPick().AddTo(_tapes);
                        Box("of-cassette-reel").NoPick().AddTo(c);
                        Box("of-cassette-reel").NoPick().AddTo(c);
                    }
                }
            }
            bool todo;
            string text = OfficeText.Now(lc, v, out todo);
            _now.SetText(text);
            _strip.EnableInClassList("of-strip-todo", todo);
        }

        void UpdateCall(LineClient lc, LineView v, double now)
        {
            var ph = v.Mine.Phone;
            // the incoming call card: visible anywhere in the office (also over the computer)
            bool ringing = ph.Status == CallStatus.Ringing;
            bool card = ringing && _focus != Focus.Phone;
            if (card != _ring.IsShown())
            {
                _ring.Show(card);
                if (card) { UiMotion.PopIn(_ring, 0.25f, 0f, 0.8f); Sfx.Play("toast", 0.6f); }
            }
            if (ringing)
            {
                _ringName.SetText(F("office.ring_title", Name(ph.Other)));
                _ringHint.SetText(F("office.ring_hint", OfficeText.Key(GameInput.AnswerCall), OfficeText.Key(GameInput.HangUp)));
                _ringTime.SetText(UiKit.Clock(Math.Max(0, ph.RingUntil - now)));
                _ring.EnableInClassList("of-ring-pulse", Mathf.Repeat((float)now, 1f) < 0.5f);
            }
            // the ring sound comes from the phone on the desk
            var rooms = RoomsPresenter.Instance;
            Vector3 phonePos = rooms != null && rooms.Office != null && rooms.Office.Phone != null ? rooms.Office.Phone.position : Vector3.zero;
            if (ringing) { Sfx.LoopAt("desk_ring", "ringtone", phonePos, 0.75f, true); _ringLoop = true; }
            else if (_ringLoop) { _ringLoop = false; Sfx.StopLoop("desk_ring"); }
            bool dialing = ph.Status == CallStatus.Dialing;
            if (dialing && !_backLoop) { _backLoop = true; Sfx.LoopAt("ringback", "ringback", Vector3.zero, 0.35f, false); }
            else if (!dialing && _backLoop) { _backLoop = false; Sfx.StopLoop("ringback"); }

            // the call pill (dialing / talking), hidden while the phone panel shows the same
            bool pill = (ph.Status == CallStatus.Connected || dialing) && _focus != Focus.Phone;
            _call.Show(pill);
            bool rec = PlayBlocks.RecordingCurrentCall(v);
            if (pill)
            {
                _callText.SetText(dialing ? F("office.call_dialing", Name(ph.Other)) : F("office.call_talking", Name(ph.Other)));
                _callTime.SetText(dialing ? UiKit.Clock(Math.Max(0, ph.RingUntil - now)) : UiKit.Clock(Math.Max(0, now - ph.Since)));
                _callRec.Show(rec);
                _call.EnableInClassList("of-call-dialing", dialing);
            }
            if (rec && !_lastRecording) { Toast(F("office.rec_started", Name(ph.Other)), "of-toast-rec", 5f); }
            _lastRecording = rec;
            if (ph.Status != _lastStatus || ph.Other != _lastOther || ph.CallId != _lastCall) _dirty = true;
            _lastStatus = ph.Status; _lastOther = ph.Other; _lastCall = ph.CallId;
        }

        void UpdateMic(LineView v)
        {
            bool held = VoiceInput.PushToTalkHeld && (UIRoot.I == null || !UIRoot.I.TextInputFocused);
            _mic.Show(held);
            if (!held) return;
            var ph = v.Mine.Phone;
            bool heard = ph.Status == CallStatus.Connected;
            _micText.SetText(heard ? F("office.mic_heard", Name(ph.Other)) : T("office.mic_nobody"));
            _mic.EnableInClassList("of-mic-alone", !heard);
        }

        void UpdatePrompt(LineView v)
        {
            var rooms = RoomsPresenter.Instance;
            DeskTarget d = rooms != null && rooms.Mode == PovMode.Office && _focus == Focus.None ? Rooms.Hovered : DeskTarget.None;
            string text = d != DeskTarget.None ? OfficeText.Use(d, v) : "";
            bool show = !string.IsNullOrEmpty(text);
            if (show != _prompt.IsShown()) _prompt.Show(show);
            if (!show) { _promptFor = DeskTarget.None; return; }
            string hot = OfficeText.Hotkey(d);
            string sig = text + "|" + hot;
            if (sig == _promptSig && d == _promptFor) return;
            _promptSig = sig;
            if (d != _promptFor) UiMotion.FadeIn(_prompt, 0.12f);
            _promptFor = d;
            _promptKey.SetText(OfficeText.Key(GameInput.Interact));
            _promptText.SetText(text);
            _promptAlt.SetText(hot != null ? F("office.prompt_hotkey", hot) : "");
            _promptAlt.Show(hot != null);
        }

        /// <summary>World state that belongs to the office UI: the handset goes up while the phone panel is open, the cork board.</summary>
        void UpdateWorld(LineClient lc, LineView v)
        {
            var rooms = RoomsPresenter.Instance;
            if (rooms == null || rooms.Office == null) return;
            if (_board.Ensure(rooms.Office.CorkSurface, rooms.Rig != null ? rooms.Rig.Panel : null)) _board.Update(lc, v);
        }

        void PlaceComputer(float dt)
        {
            if (!_computer.IsOpen) { LayoutHud(null); return; }
            var rooms = RoomsPresenter.Instance;
            var panel = Root.panel;
            if (rooms == null || panel == null) return;
            if (!rooms.TryGetScreenRect(PovMode.Monitor, out var r)) return;
            float sw = Mathf.Max(1, Screen.width), sh = Mathf.Max(1, Screen.height);
            var size = panel.visualTree.layout.size;
            float kx = size.x / sw, ky = size.y / sh;
            // screen pixels (origin bottom-left) -> panel units (origin top-left)
            var pr = new Rect(r.xMin * kx, (sh - r.yMax) * ky, r.width * kx, r.height * ky);
            // near the end of the ease the projected screen already matches: let the desktop fade in then
            bool settled = rooms.Mode == PovMode.Monitor && rooms.Rig.BlendProgress > 0.9f;
            _computer.Place(pr, settled, dt);
            LayoutHud(_computer.Shown ? pr : (Rect?)null);
        }

        bool _hudOs;

        /// <summary>While the computer is on screen the toasts and the call pill move into the desktop's right column
        /// (under the sticky note) and the incoming-call card above the taskbar, so they never cover the app window.</summary>
        void LayoutHud(Rect? os)
        {
            bool on = os.HasValue;
            if (!on && !_hudOs) return;
            _hudOs = on;
            _toasts.EnableInClassList("of-toasts-os", on);
            _call.EnableInClassList("of-call-os", on);
            _ring.EnableInClassList("of-ring-os", on);
            if (!on)
            {
                foreach (var e in new[] { _toasts, _call, _ring })
                {
                    e.style.left = StyleKeyword.Null; e.style.top = StyleKeyword.Null; e.style.right = StyleKeyword.Null; e.style.bottom = StyleKeyword.Null;
                }
                return;
            }
            var r = os.Value;
            var size = Root.layout.size;
            float colX = r.xMax - 18f - 300f;
            float y = r.y + 262f;
            _call.style.left = colX; _call.style.top = y; _call.style.right = StyleKeyword.Auto;
            if (_call.IsShown()) y += 52f;
            _toasts.style.left = colX; _toasts.style.top = y; _toasts.style.right = StyleKeyword.Auto;
            _ring.style.top = StyleKeyword.Auto;
            _ring.style.bottom = Mathf.Max(20f, size.y - r.yMax + 78f);
        }

        // ------------------------------------------------------------------ toasts

        public void Toast(string text, string kind = null, float seconds = 5f)
        {
            if (string.IsNullOrEmpty(text)) return;
            var el = Box("of-toast").NoPick();
            if (!string.IsNullOrEmpty(kind)) el.AddToClassList(kind);
            Box("of-toast-bar").NoPick().AddTo(el);
            Text(text, "of-toast-text").NoPick().AddTo(el);
            _toasts.Insert(0, el);
            _toastList.Add(new KeyValuePair<VisualElement, float>(el, Time.unscaledTime + seconds));
            UiMotion.SlideIn(el, new Vector2(60f, 0f), 0.25f, 0f, UiMotion.Ease.OutCubic);
            while (_toastList.Count > 4)
            {
                _toastList[0].Key.RemoveFromHierarchy();
                _toastList.RemoveAt(0);
            }
        }

        void TickToasts()
        {
            float now = Time.unscaledTime;
            for (int i = _toastList.Count - 1; i >= 0; i--)
            {
                var t = _toastList[i];
                float left = t.Value - now;
                if (left <= 0f) { t.Key.RemoveFromHierarchy(); _toastList.RemoveAt(i); continue; }
                if (left < 0.35f) t.Key.style.opacity = left / 0.35f;
            }
        }
    }
}
