using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.PlayKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 meeting room layer (docs/V5_INTERACTION.md §3, step 3a): everything on screen while the player sits at the
    /// meeting table (meetings, the crisis vote, the final, the results). Seated first-person look (about ±55°), the same
    /// thin top strip as the office (phase, time, prim, one "Şimdi: …" line) and toasts; looking at a colleague shows
    /// their name tag; the live suç tahtası on the whiteboards (<see cref="MeetingBoard"/>, E zooms in); the folder
    /// (<see cref="MeetingFolder"/>: Soruştur, Kaydı sızdır, chat, texts, Havale; E or Q); the ballot paper during a vote
    /// (<see cref="MeetingBallot"/>). Step 3b will move the public card (<see cref="MeetingSlide"/>: dossier, verdicts,
    /// defence order, the desk reveal, results) and the cassette deck onto the projector; until then they are cards
    /// over the room. Replaces the one-window meeting screen (PlayUi stays only as the fallback without the rooms scene).
    /// </summary>
    public sealed class MeetingUi : UiKit.Screen
    {
        public static MeetingUi I { get; private set; }

        readonly VisualElement _strip, _prompt, _nameTag, _mic, _tapeHost, _boardHint;
        readonly Label _phase, _timer, _prim, _now, _promptKey, _promptText, _promptAlt, _nameText, _nameInfo, _nameAct, _micText;
        readonly ToastStack _toasts;
        readonly MeetingFolder _folder;
        readonly MeetingBallot _ballot;
        readonly MeetingSlide _slide;
        readonly MeetingBoard _board = new MeetingBoard();
        readonly TapeDeck _tape;
        readonly PlayState _st = new PlayState();
        bool _bound, _dirty = true, _boardZoom;
        int _week = int.MinValue;
        float _nextLive;
        string _pendingPhaseToast, _promptSig;

        public bool FolderOpen => _folder.IsOpen;
        public bool BallotOpen => _ballot.IsOpen;
        public bool BoardOpen => _boardZoom;
        public MeetingFolder Folder => _folder;
        public MeetingBallot Ballot => _ballot;
        /// <summary>The folder, the ballot paper or the results card is up: the cursor is free (no mouse-look).</summary>
        public bool WantsCursor => _folder.IsOpen || _ballot.IsOpen || _slide.WantsCursor;

        public MeetingUi(VisualElement parent) : base(parent, "mt-screen")
        {
            I = this;
            foreach (var n in new[] { "ui/styles/play", "ui/styles/office", "ui/styles/meeting" })
            {
                var sheet = Resources.Load<StyleSheet>(n);
                if (sheet != null) Root.styleSheets.Add(sheet);
            }
            Root.pickingMode = PickingMode.Ignore;
            Root.AddToClassList("of-screen");
            _st.Dirty = () => _dirty = true;
            _st.Toast = t => Toast(t, "of-toast-warn");

            _slide = new MeetingSlide(Root, (t, k, s) => { Toast(t, k, s); return true; });
            _tapeHost = Box("mt-tape").AddTo(Root);
            _tape = new TapeDeck(_tapeHost);
            _tapeHost.Show(false);
            _folder = new MeetingFolder(this, Root, _st);
            _st.FocusMessageField = _folder.FocusField;
            _ballot = new MeetingBallot(this, Root);

            // ---- top strip (same as the office)
            _strip = Box("of-strip").NoPick().AddTo(Root);
            var main = Box("of-strip-main").NoPick().AddTo(_strip);
            _phase = Bold("", "of-phase").NoPick().AddTo(main);
            _timer = Title("", "of-timer").NoPick().AddTo(main);
            Box("of-sep").NoPick().AddTo(main);
            _prim = Bold("", "of-prim").NoPick().AddTo(main);
            var nowRow = Box("of-now-row").NoPick().AddTo(_strip);
            Bold(T("office.now_label"), "of-now-label").NoPick().AddTo(nowRow);
            _now = Text("", "of-now").NoPick().AddTo(nowRow);

            _toasts = new ToastStack(Root);

            // ---- push-to-talk
            _mic = Box("of-mic").NoPick().AddTo(Root);
            Box("of-mic-dot").NoPick().AddTo(_mic);
            _micText = Bold("", "of-mic-text").NoPick().AddTo(_mic);
            _mic.Show(false);

            // ---- use prompt and the colleague's name tag under the crosshair
            _prompt = Box("of-prompt").NoPick().AddTo(Root);
            _promptKey = UiKit.KeyCap("E", "of-prompt-key").NoPick().AddTo(_prompt);
            _promptText = Bold("", "of-prompt-text").NoPick().AddTo(_prompt);
            _promptAlt = Text("", "of-prompt-alt").NoPick().AddTo(_prompt);
            _prompt.Show(false);
            _nameTag = Box("mt-name").NoPick().AddTo(Root);
            _nameText = Title("", "mt-name-text").NoPick().AddTo(_nameTag);
            _nameInfo = Text("", "mt-name-info").NoPick().AddTo(_nameTag);
            _nameAct = Bold("", "mt-name-act").NoPick().AddTo(_nameTag);
            _nameTag.Show(false);
            _boardHint = Box("of-board-hint").NoPick().AddTo(Root);
            Bold(F("meeting.board_hint", OfficeText.Key(GameInput.Interact)), "of-board-hint-text").NoPick().AddTo(_boardHint);
            _boardHint.Show(false);
        }

        public void Toast(string text, string kind = null, float seconds = 5f) { _toasts.Show(text, kind, seconds); }

        // ------------------------------------------------------------------ lifecycle

        protected override void OnShow() { _dirty = true; _slide.MarkDirty(); }

        protected override void OnHide()
        {
            CloseAll(false);
            _prompt.Show(false);
            _nameTag.Show(false);
            MeetingGaze.LookingDown = false;
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
            Rooms.MeetingClicked += OnMeetingClicked;
            Rooms.SeatClicked += OnSeatClicked;
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
            if (_bound) { Rooms.MeetingClicked -= OnMeetingClicked; Rooms.SeatClicked -= OnSeatClicked; }
            _bound = false;
            _board.Release();
            _ballot.Release();
            return true;
        }

        void OnView(LineView o, LineView n) { if (!Detached()) { _dirty = true; } }

        void OnMessages(IReadOnlyList<LineMessage> list)
        {
            if (Detached()) return;
            _dirty = true;
            var lc = LineClient.I;
            if (!Visible || lc == null) return;
            foreach (var m in list)
            {
                if (m.From == lc.MySlot) continue;
                string text = m.Text ?? "";
                if (text.Length > 70) text = text.Substring(0, 68) + "…";
                string who = m.Kind == MessageKind.System ? T("msg.system")
                    : m.Kind == MessageKind.MeetingChat ? F("meeting.toast_chat", Name(m.From)) : F("meeting.toast_text", Name(m.From));
                bool open = _folder.IsOpen && _folder.Current == MeetingFolder.Tab.Chat;
                if (!open) Toast(who + " " + text + "  ·  " + F("meeting.toast_open_folder", OfficeText.Key(GameInput.Computer)), "of-toast-msg", 6f);
                Sfx.Play("email_ding", 0.55f);
            }
        }

        void OnEvent(LineEvent e)
        {
            if (Detached()) return;
            _dirty = true;
            _slide.MarkDirty();
            var lc = LineClient.I;
            int me = lc != null ? lc.MySlot : -1;
            if (e.Type == LineEventType.VoteCast)
            {
                // someone dropped a paper (never whose name is on it); my own drop is already flying
                var room = RoomsPresenter.Instance != null && RoomsPresenter.Instance.InMeeting ? RoomsPresenter.Instance.Meeting : null;
                if (room != null && e.Actor != me) room.OtherDrop(e.Actor);
                return;
            }
            if (!Visible) return;
            switch (e.Type)
            {
                case LineEventType.TransferReceived:
                    Toast(PlayText.Event(e, me), "of-toast-money", 6f);
                    Sfx.Play("complete", 0.6f);
                    break;
                case LineEventType.Investigated:
                case LineEventType.Leaked:
                case LineEventType.BlameAdded:
                case LineEventType.CrisisResolved:
                case LineEventType.Fired:
                case LineEventType.Whistled:
                case LineEventType.Presence:
                case LineEventType.RecordingPlayed:
                {
                    string line = PlayText.Event(e, me);
                    if (line != null) Toast(line, e.Type == LineEventType.Presence ? null : e.Type == LineEventType.Fired || e.Type == LineEventType.BlameAdded ? "of-toast-warn" : "of-toast-call", 6f);
                    if (e.Type == LineEventType.Fired) Sfx.Play("fired", 0.6f);
                    else if (e.Type == LineEventType.Investigated || e.Type == LineEventType.Whistled) Sfx.Play("stamp", 0.6f);
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
            _slide.MarkDirty();
            if (to == LinePhase.None) { CloseAll(false); _pendingPhaseToast = null; _toasts.Clear(); return; }
            var lc = LineClient.I;
            var v = lc != null ? lc.View : null;
            if (v == null || !(LineClient.IsMeetingPhase(to) || to == LinePhase.Results)) return;
            // a vote starts or ends: the board zoom goes back to the table (the paper is in front of you)
            if (_boardZoom && (to == LinePhase.MeetingVote || to == LinePhase.CrisisVote || to == LinePhase.FinalVote)) CloseAll();
            _pendingPhaseToast = PlayText.PhaseTitle(v);
        }

        // ------------------------------------------------------------------ objects

        void OnMeetingClicked(MeetingTarget t)
        {
            if (Detached() || !Visible) return;
            switch (t)
            {
                case MeetingTarget.Folder: OpenFolder(); break;
                case MeetingTarget.Ballot: OpenBallot(); break;
                case MeetingTarget.Board: OpenBoard(); break;
            }
        }

        /// <summary>E on a colleague: during a vote their name gets your X; otherwise the folder opens on a text to them.</summary>
        void OnSeatClicked(int slot)
        {
            if (Detached() || !Visible) return;
            var v = LineClient.I != null ? LineClient.I.View : null;
            if (v == null) return;
            if (MeetingBallot.Available(v)) { _ballot.MarkFromLook(slot); return; }
            if (v.Features.TextsOpen)
            {
                _st.MsgTarget = slot;
                OpenFolder(MeetingFolder.Tab.Chat);
                _folder.FocusField();
            }
        }

        public void OpenFolder(MeetingFolder.Tab? tab = null)
        {
            var rooms = RoomsPresenter.Instance;
            var v = LineClient.I != null ? LineClient.I.View : null;
            if (rooms == null || !rooms.InMeeting || v == null) return;
            if (_ballot.IsOpen) _ballot.Close();
            if (_boardZoom) { _boardZoom = false; rooms.SetPovMode(PovMode.Meeting); }
            bool was = _folder.IsOpen;
            _folder.Open(tab ?? (was ? _folder.Current : MeetingFolder.Suggested(v)));
            rooms.Meeting.SetFolderHeld(true);
            if (!was) Sfx.Play("pickup", 0.5f);
        }

        public void OpenBallot()
        {
            var rooms = RoomsPresenter.Instance;
            var v = LineClient.I != null ? LineClient.I.View : null;
            if (rooms == null || !rooms.InMeeting || !MeetingBallot.Available(v)) return;
            if (_folder.IsOpen) { _folder.Close(); rooms.Meeting.SetFolderHeld(false); }
            if (_boardZoom) { _boardZoom = false; rooms.SetPovMode(PovMode.Meeting); }
            _ballot.Open();
        }

        public void OpenBoard()
        {
            var rooms = RoomsPresenter.Instance;
            if (rooms == null || !rooms.InMeeting) return;
            CloseAll(false);
            _boardZoom = true;
            rooms.SetPovMode(PovMode.MeetingBoard);
            Sfx.Play("click", 0.4f);
        }

        /// <summary>Back to looking around the table (Esc, the close buttons, E with the folder up).</summary>
        public void CloseAll() { CloseAll(true); }

        void CloseAll(bool sound)
        {
            var rooms = RoomsPresenter.Instance;
            bool folder = _folder.IsOpen;
            _folder.Close();
            _ballot.Close();
            if (rooms != null && rooms.Meeting != null) rooms.Meeting.SetFolderHeld(false);
            if (_boardZoom)
            {
                _boardZoom = false;
                if (rooms != null && rooms.InMeeting) rooms.SetPovMode(PovMode.Meeting);
            }
            if (sound && folder) Sfx.Play("place", 0.5f);
        }

        /// <summary>Esc from UIRoot: puts down what you hold; false when nothing was up (UIRoot opens the menu).</summary>
        public bool Back()
        {
            if (!Visible || !(_folder.IsOpen || _ballot.IsOpen || _boardZoom)) return false;
            CloseAll();
            return true;
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
                _folder.ResetWeek();
                _ballot.ResetWeek();
                _slide.ResetWeek();
                _dirty = true;
            }
            double now = lc.Now;
            float dt = Time.unscaledDeltaTime;
            HandleKeys(lc, v);
            var rooms = RoomsPresenter.Instance;
            var panel = rooms != null && rooms.Rig != null ? rooms.Rig.Panel : null;
            _ballot.Tick(lc, v, now, dt, panel);
            UpdateStrip(lc, v, now);
            UpdateMic(v);
            UpdatePrompt(lc, v);
            UpdateTape(v, now);
            UpdateWorld(lc, v, panel);
            bool live = Time.unscaledTime >= _nextLive;
            if (live) _nextLive = Time.unscaledTime + 0.5f;
            bool dirty = _dirty;
            _dirty = false;
            PlaceFolder(dt);
            _folder.Tick(lc, v, now, dirty, live);
            _slide.Tick(lc, v, now, dirty, live, _folder.IsOpen || _boardZoom);
            _boardHint.Show(_boardZoom);
            _strip.Show(!_folder.IsOpen && !_boardZoom);
            if (_pendingPhaseToast != null && ScreenFade.Alpha < 0.5f)
            {
                Toast(_pendingPhaseToast, "of-toast-phase", 4f);
                Sfx.Play("phase", 0.5f);
                _pendingPhaseToast = null;
            }
            _toasts.Tick();
        }

        void HandleKeys(LineClient lc, LineView v)
        {
            if (UIRoot.I != null && (UIRoot.I.TextInputFocused || UIRoot.I.OverlayOpen)) return;
            if (ScreenFade.Alpha > 0.5f) return;
            bool e = Pressed(GameInput.Interact), q = Pressed(GameInput.Computer);
            if (_folder.IsOpen)
            {
                if (e || q) { CloseAll(); Consume(); }
                return;
            }
            if (_ballot.IsOpen)
            {
                if (e) { _ballot.Primary(); Consume(); }
                return;
            }
            if (_boardZoom)
            {
                if (e) { CloseAll(); Consume(); }
                return;
            }
            if (q) { OpenFolder(); Consume(); return; }
            // E with nothing under the crosshair: the paper during a vote, else the folder (nothing is more than a key away)
            if (e && Rooms.HoveredMeeting == MeetingTarget.None && Rooms.HoveredSeat < 0)
            {
                if (MeetingBallot.Available(v) && !(RoomsPresenter.Instance != null && RoomsPresenter.Instance.Meeting.BallotDropping)) OpenBallot();
                else OpenFolder();
                Consume();
            }
        }

        static void Consume() { RoomsPresenter.UseConsumedFrame = Time.frameCount; }

        static bool Pressed(UnityEngine.InputSystem.InputAction a) { return a != null && a.WasPressedThisFrame(); }

        void UpdateStrip(LineClient lc, LineView v, double now)
        {
            _phase.SetText(PlayText.PhaseTitle(v));
            double left = Math.Max(0, v.PhaseEndTime - now);
            _timer.SetText(v.Phase == LinePhase.Abandoned ? "" : UiKit.Clock(left));
            _timer.EnableInClassList("of-timer-low", left < 10 && left > 0 && v.Features.VoteOpen);
            _prim.SetText(F("header.my_prim", v.Mine.Prim));
            bool todo;
            _now.SetText(MeetingText.Now(lc, v, _ballot.Cast(v), out todo));
            _strip.EnableInClassList("of-strip-todo", todo);
        }

        void UpdateMic(LineView v)
        {
            bool held = VoiceInput.PushToTalkHeld && (UIRoot.I == null || !UIRoot.I.TextInputFocused);
            _mic.Show(held);
            if (!held) return;
            bool heard = v.Features.OpenVoice || (v.Final != null && v.Final.Defender == v.Me);
            _micText.SetText(T(heard ? "meeting.mic_all" : "meeting.mic_muted"));
            _mic.EnableInClassList("of-mic-alone", !heard);
        }

        void UpdatePrompt(LineClient lc, LineView v)
        {
            var rooms = RoomsPresenter.Instance;
            bool looking = rooms != null && rooms.Mode == PovMode.Meeting && !_folder.IsOpen && !_ballot.IsOpen && !_boardZoom;
            int seat = looking ? Rooms.HoveredSeat : -1;
            var target = looking ? Rooms.HoveredMeeting : MeetingTarget.None;
            // a colleague: name tag (name in their colour, suç and tags, what E does)
            var p = seat >= 0 ? lc.Player(seat) : null;
            if (p != null)
            {
                string sig = "n" + seat + p.Name + p.Blame + p.Candidate + p.Fired + p.Voted + v.Features.VoteOpen + Loc.Language;
                if (sig != _promptSig || !_nameTag.IsShown())
                {
                    if (!_nameTag.IsShown()) UiMotion.FadeIn(_nameTag, 0.12f);
                    _promptSig = sig;
                    _nameText.SetText(p.Name);
                    _nameText.style.color = Color.Lerp(PlayerColor(p.ColorIndex), Color.white, 0.4f);
                    _nameInfo.SetText(MeetingText.NameInfo(v, p));
                    bool vote = MeetingBallot.Available(v);
                    string act = vote ? F("meeting.name.act_vote", OfficeText.Key(GameInput.Interact)) : v.Features.TextsOpen ? F("meeting.name.act_text", OfficeText.Key(GameInput.Interact)) : "";
                    _nameAct.SetText(act);
                    _nameAct.Show(act.Length > 0);
                }
                _nameTag.Show(true);
                _prompt.Show(false);
                return;
            }
            _nameTag.Show(false);
            string text = target != MeetingTarget.None ? MeetingText.Use(target, v) : "";
            bool show = !string.IsNullOrEmpty(text);
            if (show != _prompt.IsShown()) { _prompt.Show(show); if (show) UiMotion.FadeIn(_prompt, 0.12f); }
            if (!show) { _promptSig = null; return; }
            string hot = target == MeetingTarget.Folder ? OfficeText.Key(GameInput.Computer) : null;
            string psig = "t" + text + hot;
            if (psig == _promptSig) return;
            _promptSig = psig;
            _promptKey.SetText(OfficeText.Key(GameInput.Interact));
            _promptText.SetText(text);
            _promptAlt.SetText(hot != null ? F("office.prompt_hotkey", hot) : "");
            _promptAlt.Show(hot != null);
        }

        /// <summary>The cassette deck while a leaked recording plays (or the next one within a few seconds); 3b: projector.</summary>
        void UpdateTape(LineView v, double now)
        {
            LineRecordingPick show = null;
            if (v.Meeting != null)
                foreach (var p in v.Meeting.Recordings)
                    if (now >= p.PlayAt - 4 && now < p.PlayAt + p.Seconds + 3) { show = p; break; }
            bool on = show != null && !_folder.IsOpen;
            _tapeHost.Show(on);
            if (!on) return;
            _tape.Set(show, v);
            _tape.Tick(now, Time.unscaledDeltaTime);
        }

        void UpdateWorld(LineClient lc, LineView v, PanelSettings panel)
        {
            var rooms = RoomsPresenter.Instance;
            if (rooms == null || rooms.Meeting == null || !rooms.InMeeting) return;
            if (_board.Ensure(rooms.Meeting.Boards, panel)) _board.Update(lc, v);
            MeetingGaze.LookingDown = _folder.IsOpen || _ballot.IsOpen;
            // the camera follows what is open (re-applied after the room re-enters)
            var want = _boardZoom ? PovMode.MeetingBoard : PovMode.Meeting;
            if (rooms.Mode != want && rooms.Mode != PovMode.Cutscene) rooms.SetPovMode(want);
        }

        void PlaceFolder(float dt)
        {
            var rooms = RoomsPresenter.Instance;
            var panel = Root.panel;
            if (!_folder.IsOpen || rooms == null || panel == null) return;
            Rect r;
            if (!rooms.Meeting.TryGetFolderScreen(rooms.PovCamera, out r)) return;
            float sw = Mathf.Max(1, Screen.width), sh = Mathf.Max(1, Screen.height);
            var size = panel.visualTree.layout.size;
            float kx = size.x / sw, ky = size.y / sh;
            var pr = new Rect(r.xMin * kx, (sh - r.yMax) * ky, r.width * kx, r.height * ky);
            _folder.Place(pr, rooms.Meeting.FolderProgress > 0.96f, dt);
        }
    }
}
