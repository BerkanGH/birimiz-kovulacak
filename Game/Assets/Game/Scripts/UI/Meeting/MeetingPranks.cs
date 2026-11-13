using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.PlayKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 Yaramazlıklar, the meeting room UI (docs/V5_YARAMAZLIKLAR.md §6): the folder's "Yaramazlıklar" tab (every prank
    /// with its price, patience dots, uses left and why it is closed; name buttons for far seats), the G quick wheel, aiming
    /// (look at a colleague: highlight and name, click or E; Esc or a right click cancels), the post-it word buttons and the
    /// plane's note field, reading a plane that landed on my table, the coffee / cream splash over my screen, my own post-it,
    /// the manager's lines over his head on level changes and tutanaks, and a bot's reactions out loud. Every action is a
    /// <see cref="LineVerb.Prank"/> through <see cref="LineClient.Send"/>; the host validates it.
    /// </summary>
    public sealed class MeetingPranks
    {
        enum Mode { None, Wheel, Dialog, Aim, Read }

        static readonly PrankKind[] Order =
        {
            PrankKind.Coffee, PrankKind.Plane, PrankKind.PostIt, PrankKind.PhoneRing, PrankKind.Slurp, PrankKind.StandUp, PrankKind.Flatter, PrankKind.Cake,
        };

        readonly MeetingUi _ui;
        readonly VisualElement _wheel, _wheelRing, _dialog, _dialogWords, _dialogTargets, _aim, _reader, _splash, _cream, _myPostIt;
        readonly Label _wheelTitle, _wheelLevel, _wheelInfo, _dialogTitle, _dialogInfo, _aimText, _readerFrom, _readerText, _myPostItWord;
        readonly VisualElement _wheelGauge;
        readonly Button[] _slices = new Button[Order.Length];
        readonly Label[] _sliceName = new Label[Order.Length], _sliceInfo = new Label[Order.Length];
        readonly TextField _note;
        Mode _mode;
        PrankKind _armed, _wheelHover = PrankKind.None;
        int _word, _wheelSeat = -1, _readIndex = -1;
        float _splashFrom = -10f, _creamFrom = -10f, _splashDelay, _creamDelay;
        string _wheelSig, _dialogSig;
        // the manager's next line (the strongest of what just happened)
        string _mgrLine;
        int _mgrPriority;
        float _mgrAt;

        /// <summary>The prank picked in the folder's tab (its actions open under it).</summary>
        public PrankKind Selected = PrankKind.Coffee;
        public bool WantsCursor => _mode == Mode.Wheel || _mode == Mode.Dialog || _mode == Mode.Read;
        public bool Aiming => _mode == Mode.Aim;
        public bool WheelOpen => _mode == Mode.Wheel;
        public bool Reading => _mode == Mode.Read;
        public bool DialogOpen => _mode == Mode.Dialog;
        public PrankKind Armed => _armed;

        public MeetingPranks(MeetingUi ui, VisualElement parent)
        {
            _ui = ui;
            // ---- screen splashes (under everything else)
            _splash = Box("pk-splash").NoPick().AddTo(parent);
            _splash.style.backgroundImage = new StyleBackground(SplatTexture(new Color(0.36f, 0.2f, 0.08f), 11, 0.75f, 1f));
            _splash.Show(false);
            _cream = Box("pk-splash").NoPick().AddTo(parent);
            _cream.style.backgroundImage = new StyleBackground(SplatTexture(new Color(0.99f, 0.97f, 0.93f), 23, 0.9f, 0.62f));
            _cream.Show(false);
            // ---- my own post-it (I cannot see my forehead)
            _myPostIt = Box("pk-mypostit").NoPick().AddTo(parent);
            Bold(T("prank.mypostit"), "pk-mypostit-kicker").NoPick().AddTo(_myPostIt);
            _myPostItWord = Title("", "pk-mypostit-word").NoPick().AddTo(_myPostIt);
            _myPostIt.Show(false);

            // ---- the quick wheel
            _wheel = Box("pk-wheel").AddTo(parent);
            _wheel.RegisterCallback<PointerDownEvent>(e => { if (e.target == _wheel) Close(); });
            _wheelRing = Box("pk-ring").AddTo(_wheel);
            var centre = Box("pk-centre").NoPick().AddTo(_wheelRing);
            _wheelTitle = Bold(Loc.Upper(T("prank.wheel.title")), "pk-centre-title").NoPick().AddTo(centre);
            _wheelGauge = Box("pk-gauge-row").NoPick().AddTo(centre);
            _wheelLevel = Bold("", "pk-centre-level").NoPick().AddTo(centre);
            _wheelInfo = Text("", "pk-centre-info").NoPick().AddTo(centre);
            // an ellipse: wide slices never overlap their neighbours
            const float rx = 450f, ry = 300f;
            for (int i = 0; i < Order.Length; i++)
            {
                var kind = Order[i];
                float a = -90f + i * 360f / Order.Length;
                var b = new Button(() => Pick(kind)).Cls("pk-slice");
                b.RemoveFromClassList(Button.ussClassName);
                b.style.left = Mathf.Cos(a * Mathf.Deg2Rad) * rx;
                b.style.top = Mathf.Sin(a * Mathf.Deg2Rad) * ry;
                UiKit.KeyCap((i + 1).ToString(), "pk-slice-key").NoPick().AddTo(b);
                _sliceName[i] = Bold(kind == PrankKind.StandUp ? T("prank.standup.wheel") : T("prank." + LinePranks.Id(kind) + ".name"), "pk-slice-name").NoPick().AddTo(b);
                _sliceInfo[i] = Text("", "pk-slice-info").NoPick().AddTo(b);
                b.RegisterCallback<PointerEnterEvent>(_ => { _wheelHover = kind; _wheelSig = null; });
                b.RegisterCallback<PointerLeaveEvent>(_ => { if (_wheelHover == kind) { _wheelHover = PrankKind.None; _wheelSig = null; } });
                b.AddTo(_wheelRing);
                _slices[i] = b;
            }
            Text(F("prank.wheel.foot", Key(GameInput.Prank)), "pk-wheel-foot").NoPick().AddTo(_wheel);
            _wheel.Show(false);

            // ---- the post-it word / plane note card (also from the folder's tab)
            _dialog = Box("pk-dialog").AddTo(parent);
            _dialogTitle = Title("", "pk-dialog-title").AddTo(_dialog);
            _dialogInfo = Text("", "pk-dialog-info").AddTo(_dialog);
            _dialogWords = Box("pk-words").AddTo(_dialog);
            _note = new TextField { maxLength = 60 }.Cls("pl-field", "pk-note-field").AddTo(_dialog);
            _note.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { AimFromDialog(); e.StopPropagation(); }
            }, TrickleDown.TrickleDown);
            Bold(T("prank.dialog.targets"), "pk-dialog-sub").AddTo(_dialog);
            _dialogTargets = Box("pk-targets").AddTo(_dialog);
            var row = Box("pk-dialog-buttons").AddTo(_dialog);
            Btn(F("prank.dialog.aim", Key(GameInput.Interact)), AimFromDialog, "pl-primary").AddTo(row);
            Btn(T("prank.dialog.cancel"), () => Close()).AddTo(row);
            _dialog.Show(false);

            // ---- aiming
            _aim = Box("pk-aim").NoPick().AddTo(parent);
            _aimText = Bold("", "pk-aim-text").NoPick().AddTo(_aim);
            _aim.Show(false);

            // ---- a plane's note
            _reader = Box("pk-reader").AddTo(parent);
            Bold(Loc.Upper(T("prank.reader.kicker")), "pk-reader-kicker").AddTo(_reader);
            _readerFrom = Text("", "pk-reader-from").AddTo(_reader);
            _readerText = Hand("", "pk-reader-text").AddTo(_reader);
            Btn(F("prank.reader.close", Key(GameInput.Interact)), () => Close(), "pl-primary").AddTo(_reader);
            _reader.Show(false);
        }

        static string Key(UnityEngine.InputSystem.InputAction a) { return OfficeText.Key(a); }

        // ------------------------------------------------------------------ helpers

        static LinePrankOptionView Opt(LineView v, PrankKind k)
        {
            if (v == null || v.Pranks == null) return null;
            foreach (var o in v.Pranks.Options) if (o.Kind == k) return o;
            return null;
        }

        static string NameOf(PrankKind k) { return T("prank." + LinePranks.Id(k) + ".name"); }

        /// <summary>Why it is closed, in words ("" when it is open).</summary>
        public static string Reason(LineView v, LinePrankOptionView o, double now)
        {
            if (o == null) return T("prank.block.NotAvailable");
            switch (o.Block)
            {
                case LineFailure.None: return "";
                case LineFailure.WrongPhase:
                    if (v.Phase == LinePhase.Results) return T("prank.block.results_cake");
                    return T(o.Kind == PrankKind.Cake ? "prank.block.cake_results" : "prank.block.WrongPhase");
                case LineFailure.RateLimited: return F("prank.block.RateLimited", Mathf.Max(1, Mathf.CeilToInt((float)(o.ReadyAt - now))));
                case LineFailure.InsufficientFunds: return F("prank.block.InsufficientFunds", o.Cost);
                default: return T("prank.block." + o.Block);
            }
        }

        static string Price(LinePrankOptionView o) { return o.Cost > 0 ? F("prank.cost", o.Cost) : T("prank.free"); }

        static string PatienceText(LinePrankOptionView o)
        {
            if (o.Kind == PrankKind.Cake) return T("prank.patience_none");
            return o.Patience < 0 ? F("prank.relief", -o.Patience) : F("prank.patience", o.Patience);
        }

        static string Uses(LinePrankOptionView o)
        {
            if (o.UsesLeft < 0) return o.Kind == PrankKind.Slurp || o.Kind == PrankKind.StandUp ? T("prank.uses_cooldown") : "";
            return o.Kind == PrankKind.Cake ? F("prank.uses_match", o.UsesLeft) : F("prank.uses_session", o.UsesLeft);
        }

        /// <summary>The manager's face and the patience notches (green / amber / red), like the projector's corner.</summary>
        public static void Gauge(VisualElement p, LinePranksView pv, int capacity, string extra = null)
        {
            var g = Box("pk-gauge").NoPick().AddTo(p);
            if (extra != null) g.AddToClassList(extra);
            var face = Box("pk-face", "pk-face-" + pv.Level).NoPick().AddTo(g);
            UiArt.Apply(face, pv.Level >= 2 ? "portraits/mgr_disiplinci_angry" : "portraits/mgr_disiplinci");
            var notches = Box("pk-notches").NoPick().AddTo(g);
            for (int i = 0; i < capacity; i++)
            {
                var n = Box("pk-notch").NoPick().AddTo(notches);
                if (i < pv.Patience) n.AddToClassList("pk-notch-" + LinePranks.Level(i + 1, capacity));
            }
        }

        // ------------------------------------------------------------------ sending

        void Fire(PrankKind kind, int target, int word = -1, string note = "")
        {
            var lc = LineClient.I;
            if (lc == null || lc.View == null) return;
            var o = Opt(lc.View, kind);
            if (o == null || o.Block != LineFailure.None)
            {
                _ui.Toast(Reason(lc.View, o, lc.Now), "of-toast-warn");
                Sfx.Play("deny", 0.5f);
                return;
            }
            lc.Send(LineVerb.Prank, target, (int)kind, word, -1, note ?? "");
            Sfx.Play("click", 0.4f);
        }

        /// <summary>A prank picked from the wheel or the tab: free ones go at once; targeted ones arm the aim (the post-it and
        /// the plane first ask for the word / the note).</summary>
        public void Pick(PrankKind kind)
        {
            var lc = LineClient.I;
            var v = lc != null ? lc.View : null;
            var o = Opt(v, kind);
            if (o == null || o.Block != LineFailure.None)
            {
                _ui.Toast(Reason(v, o, lc != null ? lc.Now : 0), "of-toast-warn");
                Sfx.Play("deny", 0.5f);
                return;
            }
            switch (kind)
            {
                case PrankKind.Slurp:
                case PrankKind.Flatter:
                    Close();
                    Fire(kind, -1);
                    return;
                case PrankKind.StandUp:
                    // you point at whoever you were looking at
                    int seat = _wheelSeat;
                    Close();
                    Fire(kind, seat);
                    return;
                case PrankKind.PostIt:
                case PrankKind.Plane:
                    OpenDialog(kind);
                    return;
                default:
                    Arm(kind);
                    return;
            }
        }

        /// <summary>Aim mode: look at a colleague and click (or E); Esc / a right click cancels.</summary>
        public void Arm(PrankKind kind)
        {
            _ui.CloseAll();
            _armed = kind;
            _mode = Mode.Aim;
            HideAll();
            _aim.Show(true);
            UiMotion.SlideIn(_aim, new Vector2(0f, 30f), 0.2f);
            _aimText.SetText(F("prank.aim", NameOf(kind), Key(GameInput.Interact)));
            Sfx.Play("pickup", 0.4f);
        }

        /// <summary>A colleague clicked while aiming: the armed prank goes at them. False when not aiming.</summary>
        public bool SeatClicked(int slot)
        {
            if (_mode != Mode.Aim) return false;
            var kind = _armed;
            int word = _word;
            string note = _note.value;
            Close();
            Fire(kind, slot, kind == PrankKind.PostIt ? word : -1, kind == PrankKind.Plane ? note : "");
            if (kind == PrankKind.Plane) _note.SetValueWithoutNotify("");
            return true;
        }

        public void OpenWheel()
        {
            var lc = LineClient.I;
            var v = lc != null ? lc.View : null;
            if (v == null || v.Pranks == null || !v.Features.PranksEnabled) return;
            if (_mode == Mode.Wheel) { Close(); return; }
            _wheelSeat = Rooms.HoveredSeat;
            _ui.CloseAll();
            HideAll();
            _mode = Mode.Wheel;
            _wheelSig = null;
            _wheel.Show(true);
            UiMotion.PopIn(_wheelRing, 0.18f, 0f, 0.8f);
            Sfx.Play("pickup", 0.45f);
        }

        void OpenDialog(PrankKind kind)
        {
            _ui.CloseAll();
            HideAll();
            _armed = kind;
            _mode = Mode.Dialog;
            _dialogSig = null;
            _dialog.Show(true);
            UiMotion.PopIn(_dialog, 0.18f);
            _note.Show(kind == PrankKind.Plane);
            _dialogWords.Show(kind == PrankKind.PostIt);
            if (kind == PrankKind.Plane) _note.schedule.Execute(() => _note.Focus()).StartingIn(60);
            Sfx.Play("pickup", 0.4f);
        }

        void AimFromDialog()
        {
            if (_mode != Mode.Dialog) return;
            if (_armed == PrankKind.Plane && string.IsNullOrWhiteSpace(_note.value))
            {
                _ui.Toast(T("prank.dialog.note_empty"), "of-toast-warn");
                Sfx.Play("deny", 0.5f);
                return;
            }
            if (_note.focusController != null && _note.focusController.focusedElement == _note) _note.Blur();
            Arm(_armed);
        }

        /// <summary>Opens the note of a plane on my table (the newest when <paramref name="index"/> is -1).</summary>
        public void OpenNote(int index)
        {
            var lc = LineClient.I;
            var v = lc != null ? lc.View : null;
            if (v == null || v.Pranks == null) return;
            LinePrankEntryView e = null;
            foreach (var x in v.Pranks.Log)
                if (x.Kind == PrankKind.Plane && x.Target == lc.MySlot && (index < 0 || x.Index == index)) e = x;
            if (e == null) return;
            _ui.CloseAll();
            HideAll();
            _mode = Mode.Read;
            _readIndex = e.Index;
            _readerFrom.SetText(F("prank.reader.from", Name(e.Actor)));
            _readerText.SetText(NoteText(e.Note));
            _reader.Show(true);
            UiMotion.PopIn(_reader, 0.2f);
            Sfx.Play("prank_paper", 0.5f);
        }

        /// <summary>A note as written, or a bot's line token in my language.</summary>
        public static string NoteText(string note)
        {
            string key;
            List<int> args;
            if (LineBotLines.TryParse(note, out key, out args)) return BotSpeech.Text(key, args);
            return note ?? "";
        }

        void HideAll()
        {
            _wheel.Show(false);
            _dialog.Show(false);
            _aim.Show(false);
            _reader.Show(false);
        }

        /// <summary>Closes the wheel / card / aim / note. True when something was open (Esc consumed).</summary>
        public bool Close()
        {
            bool was = _mode != Mode.None;
            if (_note.focusController != null && _note.focusController.focusedElement == _note) _note.Blur();
            _mode = Mode.None;
            HideAll();
            if (was) Sfx.Play("place", 0.35f);
            return was;
        }

        // ------------------------------------------------------------------ per frame

        public void Tick(LineClient lc, LineView v, double now, bool keysFree)
        {
            var pv = v.Pranks;
            float t = Time.unscaledTime;
            if (pv == null || !v.Features.PranksOpen)
            {
                if (_mode != Mode.None && _mode != Mode.Read) Close();
            }
            // the armed prank closed meanwhile (phase over, cooldown, someone else ...): stop aiming
            if (_mode == Mode.Aim)
            {
                var o = Opt(v, _armed);
                if (o == null || o.Block != LineFailure.None) { _ui.Toast(Reason(v, o, now), "of-toast-warn"); Close(); }
                else
                {
                    var mouse = UnityEngine.InputSystem.Mouse.current;
                    if (mouse != null && mouse.rightButton.wasPressedThisFrame) Close();
                }
            }
            if (keysFree)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (_mode == Mode.Wheel && kb != null)
                {
                    for (int i = 0; i < Order.Length; i++)
                        if (kb[UnityEngine.InputSystem.Key.Digit1 + i].wasPressedThisFrame || kb[UnityEngine.InputSystem.Key.Numpad1 + i].wasPressedThisFrame) { Pick(Order[i]); break; }
                    var mouse = UnityEngine.InputSystem.Mouse.current;
                    if (_mode == Mode.Wheel && mouse != null && mouse.rightButton.wasPressedThisFrame) Close();
                }
            }
            if (_mode == Mode.Wheel) UpdateWheel(lc, v, now);
            if (_mode == Mode.Dialog) UpdateDialog(lc, v);
            // the splashes over my screen
            Fade(_splash, t - _splashFrom - _splashDelay, 1.5f);
            Fade(_cream, t - _creamFrom - _creamDelay, 2.6f);
            // my post-it
            int word = pv != null && lc.MySlot >= 0 && lc.MySlot < pv.PostIts.Count && pv.Session >= 0 && pv.Session < LinePranks.SessionResults ? pv.PostIts[lc.MySlot] : -1;
            bool show = word >= 0 && !_ui.FolderOpen;
            if (show) _myPostItWord.SetText(T("prank.word." + LinePranks.WordId(word)));
            if (show != _myPostIt.IsShown()) { _myPostIt.Show(show); if (show) UiMotion.Stamp(_myPostIt, 4f, 0.2f); }
            // the manager speaks a moment after what happened
            if (_mgrLine != null && t >= _mgrAt)
            {
                _ui.Subs.SayManager(_mgrLine);
                _mgrLine = null;
                _mgrPriority = 0;
            }
        }

        static void Fade(VisualElement e, float age, float seconds)
        {
            bool on = age >= 0f && age < seconds;
            if (e.IsShown() != on) e.Show(on);
            if (on) e.style.opacity = age < 0.08f ? age / 0.08f : Mathf.Clamp01(1f - (age - seconds * 0.45f) / (seconds * 0.55f));
        }

        void UpdateWheel(LineClient lc, LineView v, double now)
        {
            var pv = v.Pranks;
            if (pv == null) return;
            string sig = pv.Patience + ":" + pv.Level + ":" + (int)_wheelHover + ":" + (int)(now * 2) + ":" + v.Mine.Prim + Loc.Language;
            if (sig == _wheelSig) return;
            _wheelSig = sig;
            _wheelGauge.Clear();
            Gauge(_wheelGauge, pv, lc.Config.PatienceCapacity);
            _wheelLevel.SetText(LevelText(v));
            for (int i = 0; i < Order.Length; i++)
            {
                var o = Opt(v, Order[i]);
                if (o == null) continue;
                string reason = Reason(v, o, now);
                _sliceInfo[i].SetText(reason.Length > 0 ? reason : Price(o) + " · " + PatienceText(o));
                _slices[i].EnableInClassList("pk-slice-off", o.Block != LineFailure.None);
                _slices[i].EnableInClassList("pk-slice-on", Order[i] == _wheelHover);
            }
            var h = Opt(v, _wheelHover);
            _wheelInfo.SetText(h != null ? T("prank." + LinePranks.Id(h.Kind) + ".desc") + (Uses(h).Length > 0 ? "\n" + Uses(h) : "") : F("prank.wheel.hint", Key(GameInput.Prank)));
        }

        static string LevelText(LineView v)
        {
            var pv = v.Pranks;
            if (pv == null) return "";
            if (pv.Session == LinePranks.SessionResults) return T("prank.patience.results");
            return T("prank.level." + pv.Level) + (pv.Multiplier > 1 ? " · " + F("prank.patience.defence", pv.Multiplier) : "");
        }

        void UpdateDialog(LineClient lc, LineView v)
        {
            var o = Opt(v, _armed);
            if (o == null) return;
            string sig = (int)_armed + ":" + _word + ":" + o.Block + ":" + v.Players.Count + Loc.Language;
            if (sig == _dialogSig) return;
            _dialogSig = sig;
            _dialogTitle.SetText(NameOf(_armed));
            string reason = Reason(v, o, lc.Now);
            _dialogInfo.SetText(T("prank." + LinePranks.Id(_armed) + ".desc") + "\n" + Price(o) + " · " + PatienceText(o) + (reason.Length > 0 ? " · " + reason : ""));
            _dialogWords.Clear();
            if (_armed == PrankKind.PostIt)
                for (int w = 0; w < LinePranks.WordCount; w++)
                {
                    int word = w;
                    var b = Btn(T("prank.word." + LinePranks.WordId(w)), () => { _word = word; _dialogSig = null; Sfx.Play("click", 0.4f); }, "pk-word", w == _word ? "pk-word-on" : null);
                    b.AddTo(_dialogWords);
                }
            _dialogTargets.Clear();
            foreach (var p in v.Players)
            {
                if (p.Id == lc.MySlot) continue;
                int id = p.Id;
                var b = Btn(p.Name, () => SendFromDialog(id), "pk-target").AddTo(_dialogTargets);
                b.style.borderLeftColor = PlayerColor(p.ColorIndex);
                b.SetEnabled(o.Block == LineFailure.None);
            }
        }

        void SendFromDialog(int target)
        {
            var kind = _armed;
            if (kind == PrankKind.Plane && string.IsNullOrWhiteSpace(_note.value))
            {
                _ui.Toast(T("prank.dialog.note_empty"), "of-toast-warn");
                Sfx.Play("deny", 0.5f);
                return;
            }
            string note = _note.value;
            int word = _word;
            Close();
            Fire(kind, target, kind == PrankKind.PostIt ? word : -1, kind == PrankKind.Plane ? note : "");
            if (kind == PrankKind.Plane) _note.SetValueWithoutNotify("");
        }

        // ------------------------------------------------------------------ what happened (events)

        /// <summary>A prank event, the patience changing, a tutanak: the room plays it, the manager reacts, my screen splashes.</summary>
        public void OnEvent(LineEvent e, LineClient lc)
        {
            var v = lc.View;
            int me = lc.MySlot;
            var rooms = RoomsPresenter.Instance;
            var room = rooms != null && rooms.InMeeting ? rooms.Meeting : null;
            switch (e.Type)
            {
                case LineEventType.Prank:
                {
                    var kind = (PrankKind)e.A;
                    if (room != null) room.PrankFx(kind, e.Actor, e.Target, e.C);
                    if (e.Target == me && e.Actor != me) AtMe(kind, e, lc);
                    if (kind == PrankKind.StandUp && e.Actor >= 0 && e.Actor != me) _ui.Subs.Say(lc, v, e.Actor, T("prank.standup.shout"));
                    if (kind == PrankKind.PhoneRing) Manager(T("prank.manager.phone"), 2, 1.6f);
                    if (kind == PrankKind.Flatter) Manager(T("prank.manager.flatter"), 1, 0.6f);
                    if (_ui.FolderOpen) _ui.Folder.MarkDirty();
                    break;
                }
                case LineEventType.Patience:
                    if (e.B > e.C)
                    {
                        Manager(T("prank.manager.level." + e.B), e.B, 1.0f);
                        if (room != null) room.ManagerReact(MeetingReaction.Angry, e.Target, 1.6f);
                    }
                    _ui.Projector.Patience();
                    break;
                case LineEventType.Tutanak:
                    Manager(T("prank.manager.tutanak"), 3, 1.1f);
                    if (room != null) room.ManagerReact(MeetingReaction.Point, e.Target, 2f);
                    _ui.Projector.Tutanak(e.Target);
                    break;
            }
        }

        void AtMe(PrankKind kind, LineEvent e, LineClient lc)
        {
            string who = e.Actor >= 0 ? Name(e.Actor) : "?";
            switch (kind)
            {
                case PrankKind.Coffee:
                    _splashFrom = Time.unscaledTime; _splashDelay = 0.6f;
                    _ui.Toast(F("prank.toast.coffee", who), "of-toast-warn", 5f);
                    break;
                case PrankKind.Cake:
                    _creamFrom = Time.unscaledTime; _creamDelay = 0.6f;
                    _ui.Toast(F("prank.toast.cake", who), "of-toast-warn", 5f);
                    break;
                case PrankKind.Plane:
                    _ui.Toast(F("prank.toast.plane", who, Key(GameInput.Interact)), "of-toast-msg", 7f);
                    break;
                case PrankKind.PostIt:
                    _ui.Toast(F("prank.toast.postit", who, T("prank.word." + LinePranks.WordId(e.B))), "of-toast-warn", 6f);
                    break;
                case PrankKind.PhoneRing:
                    _ui.Toast(T("prank.toast.phone"), "of-toast-warn", 6f);
                    break;
            }
        }

        /// <summary>The manager's next line: the strongest of what just happened, a moment later.</summary>
        void Manager(string line, int priority, float delay)
        {
            if (_mgrLine != null && priority <= _mgrPriority) return;
            _mgrLine = line;
            _mgrPriority = priority;
            _mgrAt = Time.unscaledTime + delay;
        }

        /// <summary>E / Enter: the card aims, the note closes.</summary>
        public void Primary()
        {
            if (_mode == Mode.Read) Close();
            else if (_mode == Mode.Dialog) AimFromDialog();
        }

        /// <summary>UI mocks: a slice of the wheel under the pointer.</summary>
        public void MockHover(PrankKind k) { _wheelHover = k; _wheelSig = null; }

        /// <summary>UI mocks: the word / note card with a word picked and a note typed.</summary>
        public void MockDialog(PrankKind k, int word, string note)
        {
            OpenDialog(k);
            if (word >= 0) _word = word;
            _note.SetValueWithoutNotify(note ?? "");
            _dialogSig = null;
        }

        /// <summary>UI mocks: the manager says it now.</summary>
        public void MockManager(string key) { _mgrLine = T(key); _mgrPriority = 9; _mgrAt = 0f; }

        /// <summary>UI mocks: my screen splashes now.</summary>
        public void MockSplash(bool cake) { if (cake) { _creamFrom = Time.unscaledTime; _creamDelay = 0f; } else { _splashFrom = Time.unscaledTime; _splashDelay = 0f; } }

        // ------------------------------------------------------------------ the folder's tab

        public void BuildTab(VisualElement p, LineClient lc, LineView v, double now)
        {
            var pv = v.Pranks;
            if (pv == null) { Line(p, T("prank.off"), "pl-dim"); return; }
            var c = lc.Config;
            int me = lc.MySlot;
            // the manager's patience
            var head = Box("pl-focus", "mt-card", "pk-head").AddTo(p);
            var row = Box("pk-head-row").AddTo(head);
            Gauge(row, pv, c.PatienceCapacity, "pk-gauge-big");
            var col = Box("pk-head-text").AddTo(row);
            Bold(Loc.Upper(T("prank.patience.title")), "pl-focus-kicker").AddTo(col);
            Big(col, LevelText(v), pv.Level >= 2 ? "pl-bad" : pv.Level == 1 ? "pl-warn" : null);
            Line(col, F("prank.patience.rule", c.PatienceCapacity, c.TutanakBlame), "pl-dim");
            Line(head, F("prank.tab.how", Key(GameInput.Prank), Key(GameInput.Interact)), "pl-dim");
            // the pranks: one row each, the picked one opens its actions
            foreach (var kind in Order)
            {
                var o = Opt(v, kind);
                if (o == null) continue;
                bool open = kind == Selected;
                var card = Box("pk-row", open ? "pk-row-open" : null, o.Block != LineFailure.None ? "pk-row-off" : null).AddTo(p);
                var k = kind;
                card.RegisterCallback<ClickEvent>(_ => { if (Selected != k) { Selected = k; _ui.Folder.MarkDirty(); Sfx.Play("click", 0.35f); } });
                var top = Box("pk-row-top").AddTo(card);
                Bold(NameOf(kind), "pk-row-name").AddTo(top);
                Box("grow").AddTo(top);
                Bold(Price(o), "pl-chip", o.Cost > 0 ? "pk-chip-cost" : "pk-chip-free").AddTo(top);
                Dots(top, o);
                string uses = Uses(o);
                if (uses.Length > 0) Bold(uses, "pl-chip").AddTo(top);
                if (o.Block == LineFailure.RateLimited)
                {
                    var oo = o;
                    Live(card, "pkwait" + (int)kind + ":" + o.ReadyAt.ToString("0.0"), now, tt => F("prank.block.RateLimited", Mathf.Max(1, Mathf.CeilToInt((float)(oo.ReadyAt - tt)))), "pk-row-reason");
                }
                else if (o.Block != LineFailure.None) Text(Reason(v, o, now), "pk-row-reason").AddTo(card);
                if (!open) continue;
                Text(T("prank." + LinePranks.Id(kind) + ".desc"), "pk-row-desc").AddTo(card);
                if (o.Block != LineFailure.None) continue;
                var acts = Box("pk-row-actions").AddTo(card);
                switch (kind)
                {
                    case PrankKind.Slurp:
                    case PrankKind.Flatter:
                    case PrankKind.StandUp:
                        Btn(T("prank." + LinePranks.Id(kind) + ".do"), () => Fire(k, -1), "pl-primary").AddTo(acts);
                        if (kind == PrankKind.StandUp) Btn(F("prank.tab.aim_point", Key(GameInput.Prank)), () => Arm(k)).AddTo(acts);
                        break;
                    case PrankKind.PostIt:
                    case PrankKind.Plane:
                        Btn(T("prank." + LinePranks.Id(kind) + ".do"), () => OpenDialog(k), "pl-primary").AddTo(acts);
                        break;
                    default:
                        foreach (var pl in v.Players)
                        {
                            if (pl.Id == me) continue;
                            int id = pl.Id;
                            var b = Btn(pl.Name, () => Fire(k, id), "pk-target").AddTo(acts);
                            b.style.borderLeftColor = PlayerColor(pl.ColorIndex);
                        }
                        Btn(F("prank.tab.aim", Key(GameInput.Interact)), () => Arm(k), "pl-primary").AddTo(acts);
                        break;
                }
            }
            // planes on my table: read them here too
            bool notes = false;
            foreach (var e in pv.Log)
            {
                if (e.Kind != PrankKind.Plane || e.Target != me) continue;
                if (!notes) { notes = true; Sub(p, T("prank.tab.my_planes")); }
                int idx = e.Index;
                var r = Box("pl-row", "pk-note-row").AddTo(p);
                Text(F("prank.tab.plane_line", Name(e.Actor), NoteText(e.Note)), "pl-text", "grow").AddTo(r);
                Btn(T("prank.tab.read"), () => OpenNote(idx)).AddTo(r);
            }
            // what happened in this session
            bool any = false;
            foreach (var e in pv.Log)
            {
                if (e.Session != pv.Session && v.Phase != LinePhase.Results) continue;
                if (!any) { any = true; Sub(p, T(v.Phase == LinePhase.Results ? "prank.tab.log_match" : "prank.tab.log_session")); }
                Line(p, "• " + LogLine(e, me), e.Tutanak ? "pl-bad" : e.Actor == me || e.Target == me ? "pl-private" : null);
            }
        }

        /// <summary>The patience a prank costs as dots (filled: it adds; hollow with a minus: flattering takes away).</summary>
        static void Dots(VisualElement p, LinePrankOptionView o)
        {
            var chip = Box("pl-chip", "pk-dots-chip", o.Patience >= 3 ? "pl-chip-bad" : o.Patience < 0 ? "pk-chip-free" : "pl-chip-warn").AddTo(p);
            Bold(T("prank.dots"), "pk-dots-label").AddTo(chip);
            if (o.Kind == PrankKind.Cake) { Bold(T("prank.patience_none"), "pk-dots-label").AddTo(chip); return; }
            if (o.Patience < 0) Bold("−", "pk-dots-label").AddTo(chip);
            int n = Math.Abs(o.Patience);
            for (int i = 0; i < n; i++) Box("pk-dot", o.Patience < 0 ? "pk-dot-relief" : null).AddTo(chip);
        }

        /// <summary>One prank in words (names bare, no suffixes): "Sarı → Pembe: kahve fırlattı · TUTANAK: Sarı".</summary>
        public static string LogLine(LinePrankEntryView e, int me)
        {
            string actor = e.Actor >= 0 ? Name(e.Actor) : T("prank.someone");
            string what = T("prank." + LinePranks.Id(e.Kind) + ".did");
            string s = e.Target >= 0 ? F("prank.log.target", actor, Name(e.Target), what) : F("prank.log.alone", actor, what);
            if (e.Kind == PrankKind.PostIt && e.Word >= 0) s += " (" + T("prank.word." + LinePranks.WordId(e.Word)) + ")";
            if (e.Cost > 0) s += " · " + F("prank.log.burned", e.Cost);
            if (e.Tutanak) s += " · " + F("prank.log.tutanak", Name(e.Culprit));
            return s;
        }

        // ------------------------------------------------------------------ art

        /// <summary>A splatter for the screen (drops of one colour around the edges, the middle mostly clear).</summary>
        static Texture2D SplatTexture(Color c, int seed, float alpha, float size)
        {
            const int W = 320, H = 180;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "PrankSplat" + seed };
            var px = new Color32[W * H];
            var rnd = new System.Random(seed);
            var blobs = new List<Vector3>();
            for (int i = 0; i < 26; i++)
            {
                // mostly toward the edges and corners
                float x = (float)rnd.NextDouble(), y = (float)rnd.NextDouble();
                if (rnd.NextDouble() < 0.7) { if (rnd.NextDouble() < 0.5) x = x < 0.5f ? x * 0.35f : 1f - (1f - x) * 0.35f; else y = y < 0.5f ? y * 0.4f : 1f - (1f - y) * 0.4f; }
                float r = (6f + (float)rnd.NextDouble() * (i < 8 ? 34f : 14f)) * size;
                blobs.Add(new Vector3(x * W, y * H, r));
                // drips and satellites
                int n = rnd.Next(2, 5);
                for (int k = 0; k < n; k++)
                {
                    float a = (float)(rnd.NextDouble() * Math.PI * 2), d = r * (1.1f + (float)rnd.NextDouble() * 0.8f);
                    blobs.Add(new Vector3(x * W + Mathf.Cos(a) * d, y * H + Mathf.Sin(a) * d, r * (0.15f + 0.25f * (float)rnd.NextDouble())));
                }
            }
            for (int yy = 0; yy < H; yy++)
                for (int xx = 0; xx < W; xx++)
                {
                    float best = 0f;
                    for (int bi = 0; bi < blobs.Count; bi++)
                    {
                        var b = blobs[bi];
                        float dx = xx - b.x, dy = yy - b.y;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        if (d > b.z * 1.4f + 3f) continue;
                        // a ragged rim, not a circle
                        float a = Mathf.Atan2(dy, dx);
                        float r = b.z * (1f + 0.16f * Mathf.Sin(a * 2f + bi * 2.3f) + 0.1f * Mathf.Sin(a * 3f + bi * 1.1f) + 0.05f * Mathf.Sin(a * 7f + bi * 0.7f));
                        best = Mathf.Max(best, Mathf.Clamp01((r - d) / 2.2f));
                        if (best >= 1f) break;
                    }
                    float shade = 0.85f + 0.15f * Mathf.PerlinNoise(xx * 0.08f, yy * 0.08f);
                    px[yy * W + xx] = new Color(c.r * shade, c.g * shade, c.b * shade, best * alpha);
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
