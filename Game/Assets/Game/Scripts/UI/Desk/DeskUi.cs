using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    public enum DeskApp { Task = 0, Messages = 1, Mail = 2, Bank = 3, Evidence = 4, Remedial = 5, Trick = 6, Complaint = 7 }

    /// <summary>Why a call did not connect (phone strip: call back / send a text).</summary>
    public enum MissKind { None = 0, Missed = 1, Busy = 2, NoAnswer = 3 }

    /// <summary>Client-side memory of the desk UI for one match (survives meetings and language switches).</summary>
    public sealed class DeskState
    {
        public int WeekId = -1;
        public readonly HashSet<int> ReadIds = new HashSet<int>();
        public int MorningRangDay = -1, MorningAnsweredDay = -1, MorningClosedDay = -1;
        public bool TrickUnlockSeen;
        public DeskApp LastApp = DeskApp.Task;
        // onboarding (first block of the session)
        public bool ObOpenedComputer, ObCalled, ObSubmitted, ObSawCompany, ObBreakSeen;
        public int IntroSeenKey = -1;
        // survives UIRoot.Rebuild (language switch): what was open in which phase
        public int PhaseKey = int.MinValue;
        public bool ComputerOpen, PhoneOpen;
        // tutorials: the task number the tutorial auto-opened for, and which tasks were seen this session
        public int TutAutoNumber = -1;
        public readonly HashSet<int> TutSeen = new HashSet<int>();
        // last call that did not connect
        public int MissWho = LineIds.None;
        public MissKind Miss;
        public double MissAt;
        // selected message thread / mail selection
        public int MsgThread = LineIds.None;

        public void Reset(int weekId)
        {
            WeekId = weekId;
            ReadIds.Clear();
            MorningRangDay = MorningAnsweredDay = MorningClosedDay = -1;
            TrickUnlockSeen = false;
            LastApp = DeskApp.Task;
            ObOpenedComputer = ObCalled = ObSubmitted = ObSawCompany = ObBreakSeen = false;
            IntroSeenKey = -1;
            PhaseKey = int.MinValue; ComputerOpen = PhoneOpen = false;
            TutAutoNumber = -1;
            MissWho = LineIds.None; Miss = MissKind.None; MissAt = 0;
            MsgThread = LineIds.None;
        }
    }

    /// <summary>
    /// v4 office-phase UI (owner: desk UI track): computer apps (task, messages, email, bank, evidence, remedial, dirty
    /// trick, complaint), desk phone, morning call, task intro / reveal / break cards, tutorials, onboarding and HUD.
    /// Created by <see cref="UIRoot"/>; visible during Morning..RoundReveal and Break. Layers bottom to top: HUD, computer,
    /// phone, cards, coach (onboarding), call popup, slips. Hotkeys: E computer, Tab phone, Y answer, N decline/hang up,
    /// Enter primary card action, F1-F8 apps, Esc / right click back. Hotkeys are ignored while typing in a text field.
    /// </summary>
    public sealed class DeskUi : UiKit.Screen
    {
        public static DeskUi I { get; private set; }
        /// <summary>When true the desk UI also tells the world which state the phone and monitor props show.</summary>
        public static bool DrivesWorldProps = true;

        /// <summary>One per process: UIRoot rebuilds the screens on a language switch, the match's desk memory stays.</summary>
        static readonly DeskState _state = new DeskState();
        internal DeskState State => _state;
        internal DeskHud Hud;
        internal DeskNotes Notes;
        internal DeskComputer Computer;
        internal DeskPhone Phone;
        internal DeskCards Cards;
        internal DeskIncoming Incoming;
        internal DeskOnboarding Onboarding;

        VisualElement _hudLayer, _computerLayer, _phoneLayer, _cardLayer, _coachLayer, _popupLayer, _notesLayer;
        readonly DeskEsc _esc;
        LineView _last;
        bool _subscribed;
        VisualElement _pauseRoot, _settingsRoot, _howtoRoot;
        bool _overlayLookedUp;
        CallStatus _activeStatus;
        PhoneLineState _worldPhone = (PhoneLineState)(-1);
        MonitorState _worldMonitor = (MonitorState)(-1);

        public DeskUi(VisualElement parent) : base(parent, "desk-ui")
        {
            I = this;
            Root.pickingMode = PickingMode.Ignore;
            var sheet = Resources.Load<StyleSheet>("ui/styles/desk");
            if (sheet != null) Root.styleSheets.Add(sheet);
            _esc = new DeskEsc(new VisualElement(), Back);
            if (Dk.View == null) _state.Reset(-1); // built (or rebuilt) outside a match: nothing to carry over
            Build();
            Rooms.DeskClicked += OnDeskClicked;
            // UIRoot.Rebuild() drops this screen and builds a new one: let go of every static hook.
            Root.RegisterCallback<DetachFromPanelEvent>(_ => Detach());
        }

        void Detach()
        {
            Rooms.DeskClicked -= OnDeskClicked;
            var c = LineClient.I;
            if (c != null && _subscribed) { c.EventReceived -= OnEvent; c.ResultReceived -= OnResult; c.MessagesReceived -= OnMessages; }
            _subscribed = false;
            _esc.SetOpen(false);
        }

        void Build()
        {
            Root.Clear();
            _hudLayer = Layer("dk-layer-hud");
            _computerLayer = Layer("dk-layer-computer");
            _phoneLayer = Layer("dk-layer-phone");
            _cardLayer = Layer("dk-layer-cards");
            _coachLayer = Layer("dk-layer-coach");
            _popupLayer = Layer("dk-layer-popup");
            _notesLayer = Layer("dk-layer-notes");
            Hud = new DeskHud(this, _hudLayer, _popupLayer);
            Computer = new DeskComputer(this, _computerLayer);
            Phone = new DeskPhone(this, _phoneLayer);
            Cards = new DeskCards(this, _cardLayer);
            Onboarding = new DeskOnboarding(this, _coachLayer);
            Incoming = new DeskIncoming(this, _popupLayer);
            Notes = new DeskNotes(this, _notesLayer);
        }

        VisualElement Layer(string cls) { return Box("dk-layer", cls).NoPick().AddTo(Root); }

        /// <summary>True when the UI needs a free cursor (computer/phone open, a card waiting for a click).</summary>
        public bool WantsCursor => Visible && (Computer.IsOpen || Phone.IsOpen || Cards.WantsCursor);

        public bool ComputerOpen => Computer.IsOpen;
        public bool PhoneOpen => Phone.IsOpen;

        // ------------------------------------------------------------------ open / close

        public void OpenComputer(DeskApp app)
        {
            var v = Dk.View;
            if (v == null) return;
            if (Phone.IsOpen) Phone.Close(false);
            bool was = Computer.IsOpen;
            Computer.Open(app);
            State.LastApp = app;
            State.ObOpenedComputer = true;
            if (!was) { Rooms.Presenter?.SetPovMode(PovMode.Monitor); Dk.Play("pc_open", "click"); }
        }

        public void OpenComputer() { OpenComputer(State.LastApp); }

        public void CloseComputer()
        {
            if (!Computer.IsOpen) return;
            Computer.Close();
            if (!Phone.IsOpen) Rooms.Presenter?.SetPovMode(PovMode.Office);
        }

        public void OpenPhone()
        {
            if (Dk.View == null) return;
            if (Computer.IsOpen) Computer.Close();
            bool was = Phone.IsOpen;
            Phone.Open();
            if (!was) { Rooms.Presenter?.SetPovMode(PovMode.Phone); Dk.Play("phone_pick", "pickup"); }
        }

        public void ClosePhone()
        {
            if (!Phone.IsOpen) return;
            Phone.Close(false);
            if (!Computer.IsOpen) Rooms.Presenter?.SetPovMode(PovMode.Office);
        }

        /// <summary>Opens the messages app on a colleague's thread with the input focused ("send a text instead").</summary>
        public void OpenMessages(int with)
        {
            State.MsgThread = with;
            OpenComputer(DeskApp.Messages);
            Computer.Messages.Select(with, true);
        }

        /// <summary>Opens the mail app on a reply to a mail (or its thread).</summary>
        public void OpenMail(int mailId)
        {
            OpenComputer(DeskApp.Mail);
            Computer.Mail.Select(mailId);
        }

        /// <summary>Esc / right click: closes the innermost thing.</summary>
        public void Back()
        {
            if (Computer.IsOpen) { if (!Computer.Back()) CloseComputer(); return; }
            if (Phone.IsOpen) { if (!Phone.Back()) ClosePhone(); return; }
            Cards.Back();
        }

        bool AnyBackTarget => Computer.IsOpen || Phone.IsOpen || Cards.WantsCursor;

        // ------------------------------------------------------------------ calls

        public void Call(int target)
        {
            var v = Dk.View;
            if (v == null || target == v.Me) return;
            if (Dk.PhoneDead(v)) { Notes.Post(SlipKind.Bad, "phone_dead", Dk.T("desk.note.phone_dead"), null); Dk.Play("deny"); return; }
            if (v.Mine.Phone.Status != CallStatus.Idle) { Notes.Post(SlipKind.Bad, "call", Dk.T("desk.note.already_in_call"), null); Dk.Play("deny"); return; }
            Dk.Send(LineVerb.Call, target);
            State.ObCalled = true;
            Dk.Play("tip_dial", "click");
        }

        public void Answer() { Dk.Send(LineVerb.Answer); }
        public void Decline() { Dk.Send(LineVerb.Decline); }
        public void HangUp() { Dk.Send(LineVerb.HangUp); }

        // ------------------------------------------------------------------ show / hide

        protected override void OnShow()
        {
            var v = Dk.View;
            if (v != null && v.WeekId != State.WeekId) State.Reset(v.WeekId);
        }

        protected override void OnHide()
        {
            bool keep = Dk.View != null && LineClient.IsOfficePhase(Dk.View.Phase);
            bool pc = State.ComputerOpen, ph = State.PhoneOpen;
            // Meetings (or the end of the match) take the screen: close everything without moving the camera.
            Computer.Close();
            Phone.Close(true);
            Cards.HideAll();
            Incoming.Hide();
            _esc.SetOpen(false);
            _last = null;
            Sfx.StopLoop("desk_ringback");
            if (Dk.View == null) State.Reset(-1);
            else if (keep) { State.ComputerOpen = pc; State.PhoneOpen = ph; }
        }

        // ------------------------------------------------------------------ tick

        public override void Tick()
        {
            if (!Visible) return;
            var v = Dk.View;
            if (v == null) return;
            if (!_subscribed) Subscribe();
            if (v.WeekId != State.WeekId) State.Reset(v.WeekId);

            bool changed = !ReferenceEquals(v, _last);
            if (changed)
            {
                var old = _last;
                _last = v;
                OnView(old, v);
            }

            bool overlay = OverlayOpen();
            if (!overlay) HandleInput(v);

            Hud.Tick(v, changed);
            Computer.Tick(v, changed);
            Phone.Tick(v, changed);
            Cards.Tick(v, changed);
            Incoming.Tick(v, changed);
            Onboarding.Tick(v, changed);
            Notes.Tick();
            _esc.SetOpen(AnyBackTarget);
            State.ComputerOpen = Computer.IsOpen;
            State.PhoneOpen = Phone.IsOpen;
            if (DrivesWorldProps) SyncWorld(v);
        }

        void OnView(LineView old, LineView v)
        {
            bool newPhase = old == null || old.Phase != v.Phase || old.Day != v.Day || old.RoundIndex != v.RoundIndex || old.WeekId != v.WeekId;
            var ph = v.Mine.Phone;
            if (ph.Status != CallStatus.Idle) _activeStatus = ph.Status;
            if (ph.Status == CallStatus.Dialing) Sfx.LoopAt("desk_ringback", "ringback", Vector3.zero, 0.5f, false);
            else Sfx.StopLoop("desk_ringback");
            if (v.Task != null && v.Task.Submitted && v.Features.OnboardingDay) State.ObSubmitted = true;
            if (ph.Status == CallStatus.Connected || ph.Status == CallStatus.Dialing) State.ObCalled = true;
            if (v.Phase == LinePhase.RoundReveal || v.Company.TasksResolved > 0 && v.Features.OnboardingDay) State.ObSawCompany = true;
            if (!newPhase) return;
            int key = PhaseKey(v);
            if (old == null && key == State.PhaseKey)
            {
                // Same phase as before a screen rebuild (language switch): restore instead of replaying the entrance.
                if (State.ComputerOpen) OpenComputer(State.LastApp);
                else if (State.PhoneOpen) OpenPhone();
                if (v.Phase == LinePhase.RoundReveal) Cards.ShowReveal(v);
                else if (v.Phase == LinePhase.Break) Cards.ShowBreak(v);
                else if (v.Phase == LinePhase.Morning && State.MorningClosedDay != v.Day) Cards.MorningRing(v);
                return;
            }
            State.PhaseKey = key;
            OnPhaseEnter(old, v);
        }

        static int PhaseKey(LineView v) { return ((v.WeekId * 8 + v.Day) * 16 + (int)v.Phase) * 8 + v.RoundIndex + 1; }

        void OnPhaseEnter(LineView old, LineView v)
        {
            switch (v.Phase)
            {
                case LinePhase.Morning:
                    CloseComputer();
                    Cards.HideAll();
                    Cards.MorningRing(v);
                    break;
                case LinePhase.RoundIntro:
                    Cards.HideAll();
                    if (v.Features.FirstTimeTask && v.Task != null)
                    {
                        // A task the player has never seen: the 5-step tutorial opens next to the task (never pauses anything).
                        if (!Phone.IsOpen) OpenComputer(DeskApp.Task); else Computer.SetApp(DeskApp.Task);
                        Computer.Task.AutoTutorial(v);
                    }
                    else Cards.ShowIntro(v);
                    break;
                case LinePhase.RoundPlay:
                    Cards.HideIntro();
                    if (!Phone.IsOpen) OpenComputer(DeskApp.Task);
                    else Computer.SetApp(DeskApp.Task);
                    break;
                case LinePhase.RoundReveal:
                    Cards.ShowReveal(v);
                    break;
                case LinePhase.Break:
                    Cards.ShowBreak(v);
                    break;
            }
        }

        // ------------------------------------------------------------------ input

        void HandleInput(LineView v)
        {
            if (Dk.Typing(Root)) return; // typing in a text field: no hotkeys
            var ph = v.Mine.Phone;
            if (Pressed(GameInput.AnswerCall))
            {
                if (ph.Status == CallStatus.Ringing) Answer();
                else if (Cards.ManagerRinging) Cards.AnswerManager();
            }
            if (Pressed(GameInput.HangUp))
            {
                if (ph.Status == CallStatus.Ringing) Decline();
                else if (ph.Status == CallStatus.Dialing || ph.Status == CallStatus.Connected) HangUp();
            }
            if (Pressed(GameInput.Phone))
            {
                if (Phone.IsOpen) ClosePhone(); else OpenPhone();
            }
            if (Pressed(GameInput.Interact) && !Computer.IsOpen && !Cards.BlocksDesk) OpenComputer();
            if (UiInput.Confirm != null && UiInput.Confirm.WasPressedThisFrame()) Cards.Primary();

            var kb = Keyboard.current;
            if (kb != null && Computer.IsOpen)
            {
                if (kb.f1Key.wasPressedThisFrame) OpenComputer(DeskApp.Task);
                else if (kb.f2Key.wasPressedThisFrame) OpenComputer(DeskApp.Messages);
                else if (kb.f3Key.wasPressedThisFrame) OpenComputer(DeskApp.Mail);
                else if (kb.f4Key.wasPressedThisFrame) OpenComputer(DeskApp.Bank);
                else if (kb.f5Key.wasPressedThisFrame) OpenComputer(DeskApp.Evidence);
                else if (kb.f6Key.wasPressedThisFrame) OpenComputer(DeskApp.Remedial);
                else if (kb.f7Key.wasPressedThisFrame) OpenComputer(DeskApp.Trick);
                else if (kb.f8Key.wasPressedThisFrame) OpenComputer(DeskApp.Complaint);
            }
            if (kb != null)
            {
                int digit = DigitPressed(kb);
                if (digit >= 0)
                {
                    if (Computer.IsOpen) Computer.Digit(digit);
                    else if (Phone.IsOpen) Phone.Digit(digit);
                }
                if (kb.backspaceKey.wasPressedThisFrame && Computer.IsOpen) Computer.Digit(-1);
            }
            var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.wasPressedThisFrame && (Computer.IsOpen || Phone.IsOpen)) Back();
        }

        static bool Pressed(InputAction a) { return a != null && a.WasPressedThisFrame(); }

        static int DigitPressed(Keyboard kb)
        {
            if (kb.digit0Key.wasPressedThisFrame || kb.numpad0Key.wasPressedThisFrame) return 0;
            if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) return 1;
            if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) return 2;
            if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) return 3;
            if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) return 4;
            if (kb.digit5Key.wasPressedThisFrame || kb.numpad5Key.wasPressedThisFrame) return 5;
            if (kb.digit6Key.wasPressedThisFrame || kb.numpad6Key.wasPressedThisFrame) return 6;
            if (kb.digit7Key.wasPressedThisFrame || kb.numpad7Key.wasPressedThisFrame) return 7;
            if (kb.digit8Key.wasPressedThisFrame || kb.numpad8Key.wasPressedThisFrame) return 8;
            if (kb.digit9Key.wasPressedThisFrame || kb.numpad9Key.wasPressedThisFrame) return 9;
            return -1;
        }

        /// <summary>Pause / settings / how-to are open (UIRoot owns them; looked up once by their panel classes).</summary>
        bool OverlayOpen()
        {
            if (!_overlayLookedUp && UIRoot.I != null && UIRoot.I.RootElement != null)
            {
                _overlayLookedUp = true;
                var r = UIRoot.I.RootElement;
                _pauseRoot = r.Q(className: "pause")?.parent;
                _settingsRoot = r.Q(className: "settings")?.parent;
                _howtoRoot = r.Q(className: "howto")?.parent;
            }
            return Shown(_pauseRoot) || Shown(_settingsRoot) || Shown(_howtoRoot);
        }

        static bool Shown(VisualElement e) { return e != null && e.style.display != DisplayStyle.None && e.resolvedStyle.display != DisplayStyle.None; }

        void OnDeskClicked(DeskTarget t)
        {
            if (!Visible || Dk.View == null || OverlayOpen()) return;
            switch (t)
            {
                case DeskTarget.Monitor: if (!Cards.BlocksDesk) OpenComputer(); break;
                case DeskTarget.Phone: OpenPhone(); break;
                default: Notes.Quip(t); break;
            }
        }

        // ------------------------------------------------------------------ events

        void Subscribe()
        {
            var c = LineClient.I;
            if (c == null) return;
            _subscribed = true;
            c.EventReceived += OnEvent;
            c.ResultReceived += OnResult;
            c.MessagesReceived += OnMessages;
        }

        void Miss(MissKind kind, int who)
        {
            State.Miss = kind; State.MissWho = who; State.MissAt = Dk.Now;
            Phone.OnMiss();
        }

        void OnEvent(LineEvent e)
        {
            var v = Dk.View;
            if (v == null || !Visible) return;
            switch (e.Type)
            {
                case LineEventType.CallIncoming:
                    Incoming.Ring();
                    break;
                case LineEventType.CallConnected:
                    Dk.Play("call_connect");
                    Phone.OnConnected();
                    Hud.OnConnected();
                    State.Miss = MissKind.None;
                    break;
                case LineEventType.CallEnded:
                {
                    Dk.Play("call_end");
                    var reason = (CallEndReason)e.A;
                    bool iCalled = _activeStatus == CallStatus.Dialing || (_activeStatus == CallStatus.Connected && WasCaller(v, e.Actor));
                    string who = Dk.Name(e.Actor);
                    int other = e.Actor == v.Me ? e.Target : e.Actor;
                    switch (reason)
                    {
                        case CallEndReason.Missed:
                            Notes.Post(SlipKind.Plain, "call", iCalled ? Dk.F("desk.note.no_answer", who) : Dk.F("desk.note.missed", who), Dk.T("desk.note.text_instead"), e.Actor, () => OpenMessages(other));
                            Miss(iCalled ? MissKind.NoAnswer : MissKind.Missed, other);
                            break;
                        case CallEndReason.Declined:
                            if (iCalled) { Notes.Post(SlipKind.Plain, "call", Dk.F("desk.note.declined", who), Dk.T("desk.note.text_instead"), e.Actor, () => OpenMessages(other)); Miss(MissKind.NoAnswer, other); }
                            break;
                        case CallEndReason.LineCut:
                            Notes.Post(SlipKind.Bad, "phone_dead", Dk.T("desk.note.line_cut"), Dk.F("desk.note.line_cut_sub", who));
                            break;
                        case CallEndReason.Disconnected:
                            Notes.Post(SlipKind.Plain, "call", Dk.F("desk.note.call_dropped", who), null, e.Actor);
                            break;
                    }
                    _activeStatus = CallStatus.Idle;
                    Phone.OnEnded();
                    break;
                }
                case LineEventType.CallBusy:
                    Dk.Play("deny");
                    Notes.Post(SlipKind.Plain, "busy", Dk.F("desk.note.busy", Dk.Name(e.Target)), Dk.T("desk.note.busy_sub"), e.Target, () => OpenMessages(e.Target));
                    Miss(MissKind.Busy, e.Target);
                    Phone.OnBusy(e.Target);
                    break;
                case LineEventType.MissedCall:
                    Dk.Play("toast");
                    Notes.Post(SlipKind.Plain, "missed_call", Dk.F("desk.note.missed", Dk.Name(e.Actor)), Dk.T("desk.note.missed_busy"), e.Actor, () => OpenMessages(e.Actor));
                    Miss(MissKind.Missed, e.Actor);
                    Phone.OnEnded();
                    break;
                case LineEventType.TaskSubmitted:
                    Computer.Task.OnSubmitted(e.Actor);
                    break;
                case LineEventType.ItemGiven:
                    if (e.Target == v.Me) { Dk.Play("toast"); Notes.Post(SlipKind.Good, "task_swap", Dk.F("desk.note.item_got", Dk.Name(e.Actor)), e.A >= 0 ? Dk.ItemName(e.A) : null, e.Actor); }
                    break;
                case LineEventType.TrickHit:
                {
                    var t = (TrickType)e.A;
                    if (t == TrickType.InternetKes) break; // InternetDown tells everyone
                    Dk.Play(t == TrickType.KahveDok ? "splash" : "power_down", "sabotage");
                    Notes.Post(SlipKind.Bad, Dk.TrickIcon(t), Dk.T("trick." + t + ".hit"), Dk.T("trick." + t + ".hit_sub"));
                    Hud.Shake();
                    break;
                }
                case LineEventType.InternetDown:
                    Dk.Play("internet_down", "sabotage");
                    Notes.Post(SlipKind.Bad, "wifi_off", Dk.T("desk.note.internet_down"), Dk.F("desk.note.internet_down_sub", Mathf.RoundToInt(Dk.Config.InternetSeconds)));
                    break;
                case LineEventType.TrickUsed:
                {
                    var t = (TrickType)e.A;
                    Dk.Play("sabotage");
                    Notes.Post(SlipKind.Dark, Dk.TrickIcon(t), Dk.F("desk.note.trick_used", Dk.TrickName(t)), Dk.T("desk.note.trick_used_sub"));
                    Computer.Refresh();
                    break;
                }
                case LineEventType.FavouriteNamed:
                    Notes.Post(SlipKind.Sticky, "favourite", Dk.F("desk.note.favourite", Dk.Name(e.Target)), Dk.T("desk.note.favourite_sub"), e.Target);
                    break;
                case LineEventType.Presence:
                    if (e.Target != v.Me)
                        Notes.Post(SlipKind.Plain, e.A == 1 ? "check" : "cross", Dk.F(e.A == 1 ? "desk.note.back" : "desk.note.dropped", Dk.Name(e.Target)), null, e.Target);
                    break;
                case LineEventType.Wiretap:
                    Notes.Post(SlipKind.Dark, "wiretap", Dk.F(e.A == 1 ? "desk.note.tap_on" : "desk.note.tap_off", Dk.Name(e.Target)), null, e.Target);
                    break;
                case LineEventType.TransferMade:
                    if (e.Target == v.Me) { Dk.Play("coin", "toast"); Notes.Post(SlipKind.Good, "bank_app", Dk.F("desk.note.transfer_in", Dk.Name(e.Actor), e.A), null, e.Actor, () => OpenComputer(DeskApp.Bank)); }
                    break;
                case LineEventType.RemedialRequested:
                    if (e.Target == v.Me) { Dk.Play("toast"); Notes.Post(SlipKind.Sticky, "remedial", Dk.F("desk.note.remedial_in", Dk.Name(e.Actor)), Dk.T("desk.note.remedial_in_sub"), e.Actor, () => OpenComputer(DeskApp.Remedial)); }
                    break;
                case LineEventType.RemedialResolved:
                    if (e.Actor == v.Me || e.Target == v.Me)
                        Notes.Post(e.A == 1 ? SlipKind.Good : SlipKind.Plain, "remedial", Dk.T(e.A == 1 ? "desk.note.remedial_ok" : "desk.note.remedial_no"), null, e.Actor == v.Me ? e.Target : e.Actor);
                    break;
                case LineEventType.ProductivityChanged:
                    Hud.OnProductivity();
                    break;
                case LineEventType.RecordGranted:
                    Notes.Post(SlipKind.Plain, "record_app", Dk.T("desk.note.record"), Dk.T("desk.note.record_sub"), LineIds.None, () => OpenComputer(DeskApp.Evidence));
                    break;
                case LineEventType.CompanyClosed:
                    Notes.Post(SlipKind.Bad, "warning", Dk.T("desk.note.closed"), Dk.T("desk.note.closed_sub"));
                    break;
            }
        }

        void OnMessages(IReadOnlyList<LineMessage> batch)
        {
            var v = Dk.View;
            if (v == null || !Visible) return;
            int me = v.Me;
            for (int i = 0; i < batch.Count; i++)
            {
                var m = batch[i];
                if (m.From == me || m.Kind == MessageKind.MeetingChat || m.Kind == MessageKind.Complaint) continue;
                Computer.Refresh();
                bool seen = Computer.IsOpen && ((m.Kind == MessageKind.Text && Computer.App == DeskApp.Messages && State.MsgThread == m.From) || (m.Kind == MessageKind.Email && Computer.App == DeskApp.Mail));
                if (seen) continue;
                Dk.Play("email_ding", "toast");
                string who = Dk.Name(m.From);
                if (m.Kind == MessageKind.Text)
                    Notes.Post(SlipKind.Plain, "message_app", Dk.F("desk.note.text_from", who), Preview(m.Text), m.From, () => OpenMessages(m.From));
                else if (m.Kind == MessageKind.Email)
                    Notes.Post(SlipKind.Plain, "mail_app", Dk.F("desk.note.mail_from", who), Preview(m.Text), m.From, () => OpenMail(m.Id));
                else
                    Notes.Post(m.Kind == MessageKind.Manager ? SlipKind.Manager : SlipKind.Plain, "mail_app", Dk.T("desk.note.mail"), Preview(m.Text), m.From, () => OpenMail(m.Id));
            }
        }

        static string Preview(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            s = s.Replace('\n', ' ');
            return s.Length > 70 ? s.Substring(0, 67) + "..." : s;
        }

        static bool WasCaller(LineView v, int other)
        {
            var log = v.Mine.CallLog;
            for (int i = log.Count - 1; i >= 0; i--) if (log[i].Other == other) return log[i].Outgoing;
            return false;
        }

        void OnResult(LineActionResult r, LineAction req)
        {
            if (!Visible || Dk.View == null) return;
            if (r.Failure == LineFailure.Duplicate || r.Failure == LineFailure.StaleWeek) return;
            if (req != null && req.Verb == LineVerb.Call && r.Failure == LineFailure.Busy) return; // CallBusy event says it
            bool handled = Computer.OnResult(r, req);
            if (r.Accepted) { if (!handled) AcceptedSlip(req); return; }
            Dk.Play("deny");
            if (!handled || !Computer.IsOpen) Notes.Post(SlipKind.Bad, "cross", Dk.T("desk.fail." + r.Failure), null);
        }

        void AcceptedSlip(LineAction req)
        {
            if (req == null) return;
            switch (req.Verb)
            {
                case LineVerb.Transfer: Notes.Post(SlipKind.Good, "bank_app", Dk.F("desk.note.transfer_out", Dk.Name(req.Target), req.A), null, req.Target); break;
                case LineVerb.BuyEvidence: Notes.Post(SlipKind.Good, "record_app", Dk.T("desk.note.bought"), null); break;
                case LineVerb.FileComplaint: Notes.Post(SlipKind.Dark, "complaint", Dk.T("desk.note.complaint_filed"), Dk.T("desk.note.complaint_filed_sub")); break;
                case LineVerb.StartRemedial: Notes.Post(SlipKind.Plain, "remedial", Dk.F("desk.note.remedial_out", Dk.Name(req.Target)), null, req.Target); break;
                case LineVerb.UseTrick: break; // TrickUsed event
            }
        }

        // ------------------------------------------------------------------ world props

        void SyncWorld(LineView v)
        {
            var rooms = Rooms.Presenter;
            if (rooms == null) return;
            PhoneLineState ps;
            if (Dk.PhoneDead(v)) ps = PhoneLineState.Dead;
            else switch (v.Mine.Phone.Status)
            {
                case CallStatus.Ringing: ps = PhoneLineState.Ringing; break;
                case CallStatus.Dialing: ps = PhoneLineState.Dialing; break;
                case CallStatus.Connected: ps = PhoneLineState.InCall; break;
                default: ps = Cards.ManagerRinging ? PhoneLineState.Ringing : PhoneLineState.Idle; break;
            }
            if (ps != _worldPhone) { _worldPhone = ps; rooms.SetPhoneState(ps); }
            MonitorState ms;
            if (Dk.ComputerBlocked(v)) ms = v.Mine.ComputerBlockedBy == TrickType.KahveDok ? MonitorState.Coffee : MonitorState.Offline;
            else if (!Computer.IsOpen && (v.Phase == LinePhase.RoundPlay && v.Task != null && !v.Task.Submitted || Computer.UnreadCount(v) > 0)) ms = MonitorState.Alert;
            else ms = MonitorState.Normal;
            if (ms != _worldMonitor) { _worldMonitor = ms; rooms.SetMonitorState(ms); }
        }

        /// <summary>Esc hook: a hidden Modal so UIRoot's "Esc closes the top-most modal first" reaches the desk UI.</summary>
        sealed class DeskEsc : Modal
        {
            readonly Action _onEsc;
            public DeskEsc(VisualElement detached, Action onEsc) : base(detached) { _onEsc = onEsc; }

            public void SetOpen(bool on)
            {
                if (on && !Visible) OpenModal();
                else if (!on && Visible) base.Close();
            }

            public override void Close()
            {
                if (!Visible) return;
                base.Close();
                _onEsc?.Invoke();
            }
        }
    }
}
