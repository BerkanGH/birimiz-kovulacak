using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v4 meeting/final/results UI (owner: meeting UI track): dossier slides on the projector (rendered into a
    /// RenderTexture for the 3D room and shown in screen space when the player looks at it), call-recording playback,
    /// the side drawer (public meeting chat, evidence presentation), discussion timer with who is speaking, voting (3D
    /// seat click or the paper ballot, with "No action" / "Fire nobody"), the warning verdict, the final (defense for every
    /// present player, final vote, verdict fired / nobody / company closed) and the results.
    /// Created by <see cref="UIRoot"/>; visible from MeetingDossier to WeekResults.
    /// <para>Two layouts: with a 3D room (<see cref="Rooms.Presenter"/>) the screen stays mostly free and Tab switches
    /// between looking around (crosshair) and the screen view (projector / ballot, free cursor). Without a room
    /// (tests, mocks, before the world lands) the screen view is always on, over a dim meeting-room backdrop.</para>
    /// </summary>
    public sealed class MeetingUi : UiKit.Screen
    {
        static StyleSheet _style;
        public static StyleSheet Style => _style != null ? _style : (_style = Resources.Load<StyleSheet>("ui/styles/meeting"));

        readonly VisualElement _backdrop, _proj, _projScreen, _projImage, _top, _seatRow, _hint, _hover;
        readonly Label _dayLabel, _phaseLabel, _timer, _sub, _projTitle, _hintText, _hoverText;
        readonly Button _prev, _next, _follow;
        readonly List<Seat> _seats = new List<Seat>();
        readonly Ballot _ballot;
        readonly WarningVerdictView _verdict;
        readonly MeetingSide _side;
        readonly DefenseView _defense;
        readonly FinalVerdictView _final;
        readonly WeekResultsView _results;
        readonly AbandonedView _abandoned;
        DossierDeck _deck;

        LinePhase _phase = LinePhase.None;
        int _week = -1, _day = -1, _seatKey = int.MinValue, _lastSec = -1, _manualSlide = -1, _lastHover = int.MinValue;
        long _version = -1;
        bool _screen, _room, _projectorSent, _managerTalking;
        float _manualUntil;
        PovMode _lastMode = (PovMode)(-1);
        int _spot = int.MinValue, _fired = int.MinValue, _presCount;

        public MeetingUi(VisualElement parent) : base(parent, "meeting-ui")
        {
            Root.pickingMode = PickingMode.Ignore;
            if (Style != null) Root.styleSheets.Add(Style);

            _backdrop = Box("mt-backdrop").NoPick().AddTo(Root);
            Box("mt-backdrop-table").NoPick().AddTo(_backdrop);

            // ---- projector (screen-space copy of the RenderTexture)
            _proj = Box("mt-proj").AddTo(Root);
            var roller = Box("mt-proj-roller").NoPick().AddTo(_proj);
            _projTitle = Bold("", "mt-proj-title").NoPick().AddTo(roller);
            var body = Box("mt-proj-body").AddTo(_proj);
            _prev = Btn("‹", () => Page(-1), "btn-secondary", "mt-proj-nav").AddTo(body);
            _projScreen = Box("mt-proj-screen").NoPick().AddTo(body);
            _projImage = Box("mt-proj-image").NoPick().AddTo(_projScreen);
            _next = Btn("›", () => Page(1), "btn-secondary", "mt-proj-nav").AddTo(body);
            var bottom = Box("mt-proj-bottom").AddTo(_proj);
            Box("mt-proj-handle").NoPick().AddTo(bottom);
            _follow = Btn(Loc.T("meeting.follow_manager"), () => { _manualSlide = -1; _manualUntil = 0; }, "btn-small", "btn-ghost", "mt-proj-follow").AddTo(bottom);

            // ---- top bar: day · phase · timer, one instruction line
            _top = Box("mt-top").NoPick().AddTo(Root);
            var pill = Box("mt-pill").NoPick().AddTo(_top);
            _dayLabel = Bold("", "mt-day").NoPick().AddTo(pill);
            _phaseLabel = Title("", "mt-phase").NoPick().AddTo(pill);
            _timer = Title("", "mt-timer").NoPick().AddTo(pill);
            _sub = Bold("", "mt-sub").NoPick().AddTo(_top);

            // ---- the table: one nameplate per colleague (speaking ring, blame, warnings, voted)
            _seatRow = Box("mt-seats").AddTo(Root);

            // ---- phase panels
            _ballot = new Ballot(Root, Vote);
            _verdict = new WarningVerdictView(Root);
            _side = new MeetingSide(Root);
            _defense = new DefenseView(Root);
            _final = new FinalVerdictView(Root);
            _results = new WeekResultsView(Root);
            _abandoned = new AbandonedView(Root);

            // ---- hints (look mode)
            _hint = Box("mt-hint").NoPick().AddTo(Root);
            KeyCap("Tab").AddTo(_hint);
            _hintText = Text("", "mt-hint-text").NoPick().AddTo(_hint);
            _hover = Box("mt-hover").NoPick().AddTo(Root);
            _hoverText = Bold("", "mt-hover-text").NoPick().AddTo(_hover);
            _hover.Show(false);

            Current = this;
            Rooms.SeatClicked += OnSeatClicked;
            Root.RegisterCallback<DetachFromPanelEvent>(_ => Rooms.SeatClicked -= OnSeatClicked);
        }

        /// <summary>The live instance (UI mocks).</summary>
        internal static MeetingUi Current;

        /// <summary>Mocks: show the dossier slide at this index (clamped; 99 = last).</summary>
        internal void JumpTo(int index)
        {
            var v = LineClient.I != null ? LineClient.I.View : null;
            if (_deck == null || v == null || _deck.Count == 0) return;
            int i = Mathf.Clamp(index, 0, _deck.Count - 1);
            _manualSlide = i;
            _manualUntil = Time.unscaledTime + 60f;
            _deck.Show(i, v);
        }

        /// <summary>Mocks: force look mode (false) or screen view (true) in the 3D room.</summary>
        internal void ToggleScreenTo(bool screen) { if (_room) _screen = screen; }

        /// <summary>What Tab does in the room: switch between looking around and the screen view.</summary>
        internal void ToggleScreen() { if (_room) _screen = !_screen; }

        /// <summary>True when the UI needs a free cursor (screen view: projector, ballot, letter, results).</summary>
        public bool WantsCursor => Visible && (_phase == LinePhase.WeekResults || _phase == LinePhase.Abandoned || _phase == LinePhase.FinalVerdict || !_room || _screen);

        static bool IsVote(LinePhase p) { return p == LinePhase.MeetingVote || p == LinePhase.FinalVote; }

        /// <summary>Default view per phase: the projector while the manager presents, faces during talk, the ballot to vote.</summary>
        static bool DefaultScreen(LinePhase p)
        {
            return p == LinePhase.MeetingDossier || IsVote(p);
        }

        protected override void OnShow()
        {
            _phase = LinePhase.None;
            _week = -1;
            _seatKey = int.MinValue;
            _projectorSent = false;
            _lastMode = (PovMode)(-1);
            _spot = int.MinValue;
            _fired = int.MinValue;
        }

        protected override void OnHide()
        {
            ProjectorSurface.SetActive(false);
            var rooms = Rooms.Presenter;
            if (rooms != null)
            {
                rooms.SetSpotlight(-1);
                rooms.SetManagerTalking(false);
            }
            _managerTalking = false;
            _hover.Show(false);
        }

        // ------------------------------------------------------------------ tick

        public override void Tick()
        {
            if (!Visible) return;
            var c = LineClient.I;
            var v = c != null ? c.View : null;
            if (v == null) return;
            double now = c.Now;
            float dt = Time.unscaledDeltaTime;
            bool room = Rooms.Presenter != null;
            if (room != _room) { _room = room; _lastMode = (PovMode)(-1); }

            if (v.WeekId != _week || v.Phase != _phase || v.Day != _day) EnterPhase(v, now);
            else if (v.StateVersion != _version) Refresh(v);
            _version = v.StateVersion;

            HandleInput(v);
            ApplyLayout();
            UpdateTimer(v, now);
            UpdateSeatsLive(v);
            UpdateProjector(v, now, dt);
            DriveRoom(v, now);

            if (_side.Visible) { _side.SetLook(_room && !_screen); _side.Tick(v, now); }
            if (_ballot.Visible) _ballot.Tick(v, now);
            if (_verdict.Visible) _verdict.Tick(v, now);
            if (_defense.Visible) _defense.Tick(v, now);
            if (_final.Visible) _final.Tick(v, now);
            if (_results.Visible) _results.Tick(v, now);
        }

        void HandleInput(LineView v)
        {
            if (UIRoot.I != null && UIRoot.I.TextInputFocused) return;
            bool overlays = Modal.AnyOpen;
            if (overlays) return;
            if (_room && GameInput.Phone != null && GameInput.Phone.WasPressedThisFrame() && LineClient.IsMeetingPhase(v.Phase) && v.Phase != LinePhase.MeetingVerdict && v.Phase != LinePhase.FinalVerdict)
            {
                _screen = !_screen;
                Sfx.Play("click", 0.6f);
            }
            var kb = Keyboard.current;
            if (kb != null && _proj.IsShown())
            {
                if (kb.leftArrowKey.wasPressedThisFrame) Page(-1);
                if (kb.rightArrowKey.wasPressedThisFrame) Page(1);
            }
        }

        // ------------------------------------------------------------------ phases

        void EnterPhase(LineView v, double now)
        {
            var old = _phase;
            bool newMeeting = v.WeekId != _week || v.Day != _day || old == LinePhase.None;
            _phase = v.Phase;
            _week = v.WeekId;
            _day = v.Day;
            _screen = DefaultScreen(_phase);
            _manualSlide = -1;
            _lastSec = -1;
            _lastHover = int.MinValue;

            bool meeting = LineClient.IsMeetingPhase(_phase);
            BuildSeats(v);
            if (meeting)
            {
                var surface = ProjectorSurface.Ensure();
                ProjectorSurface.SetActive(true);
                if (_deck == null || surface.childCount == 0)
                {
                    surface.Clear();
                    _deck = new DossierDeck(surface);
                }
                bool listChanged = _deck.SetView(v);
                if (listChanged || newMeeting) _deck.Show(_deck.AutoIndex(v, now), v, true);
                else if (_phase != LinePhase.MeetingDossier) _deck.Show(_deck.RestIndex, v);
                _deck.Refresh(v);
                _projImage.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(ProjectorSurface.Texture));
            }
            else ProjectorSurface.SetActive(false);

            _presCount = v.Meeting != null ? v.Meeting.Presentations.Count : 0;
            _side.SetVisible(SideWanted(v), v);
            _ballot.SetVisible(IsVote(_phase), v);
            _verdict.SetVisible(_phase == LinePhase.MeetingVerdict, v);
            _defense.SetVisible(_phase == LinePhase.FinalDefense, v);
            _final.SetVisible(_phase == LinePhase.FinalVerdict, v);
            _results.SetVisible(_phase == LinePhase.WeekResults, v);
            _abandoned.SetVisible(_phase == LinePhase.Abandoned, v);

            _dayLabel.text = (v.Meeting != null && v.Meeting.IsFinal) ? Loc.F("meeting.day_final", MeetingText.Day(v.Day)) : Loc.F("meeting.day", MeetingText.Day(v.Day), v.Day + 1, v.DayCount);
            _phaseLabel.text = MeetingText.Phase(_phase);
            _sub.text = SubLine(v);
            _hintText.text = "";
            _lastMode = (PovMode)(-1);

            // entrances
            if (_top.IsShown()) UiMotion.SlideIn(_top, new Vector2(0f, -40f), 0.3f);
            if (old == LinePhase.None || old == LinePhase.WeekResults || !LineClient.IsMeetingPhase(old))
            {
                UiMotion.FadeIn(_backdrop, 0.4f);
                UiMotion.SlideIn(_proj, new Vector2(0f, -60f), 0.45f, 0.1f, UiMotion.Ease.OutBack);
            }
            if (_phase != LinePhase.MeetingDossier) Sfx.Play("phase", 0.5f);
        }

        void Refresh(LineView v)
        {
            BuildSeats(v);
            if (_deck != null && LineClient.IsMeetingPhase(v.Phase))
            {
                if (_deck.SetView(v)) _deck.Show(_deck.AutoIndex(v, LineClient.I.Now), v, true);
                else _deck.Refresh(v);
            }
            _sub.text = SubLine(v);
            _side.SetVisible(SideWanted(v), v);
            _side.Refresh(v);
            CheckPresentations(v);
            _ballot.Refresh(v);
            _verdict.Refresh(v);
            _defense.Refresh(v);
            _final.Refresh(v);
            _results.Refresh(v);
        }

        string SubLine(LineView v)
        {
            var m = v.Meeting;
            bool closed = v.Company.Closed || (m != null && m.CompanyClosed);
            switch (v.Phase)
            {
                case LinePhase.MeetingDossier: return Loc.T(closed && m != null && m.IsFinal ? "meeting.sub.postmortem" : "meeting.sub.dossier");
                case LinePhase.MeetingDiscussion:
                    if (closed && m != null && m.IsFinal) return Loc.T("meeting.sub.postmortem");
                    return Loc.T(m != null && m.IsFinal ? "meeting.sub.discussion_final" : "meeting.sub.discussion");
                case LinePhase.MeetingVote: return Loc.F("meeting.sub.vote", MeetingText.CountVoted(v), MeetingText.CountConnected(v));
                case LinePhase.FinalVote: return Loc.F("meeting.sub.final_vote", MeetingText.CountVoted(v), m != null ? m.Roster.Count : MeetingText.CountConnected(v), m != null ? m.MajorityNeeded : 0);
                case LinePhase.FinalDefense:
                    if (m != null && m.Defender >= 0)
                        return m.Defender == v.Me ? Loc.T("meeting.sub.defense_me") : Loc.F("meeting.sub.defense", MeetingText.Name(m.Defender));
                    return "";
                case LinePhase.MeetingVerdict: return Loc.T("meeting.sub.verdict");
                default: return "";
            }
        }

        /// <summary>The side drawer (chat + evidence) belongs to the talking and voting phases of a meeting.</summary>
        static bool SideWanted(LineView v)
        {
            if (v.Meeting == null) return false;
            bool phase = v.Phase == LinePhase.MeetingDossier || v.Phase == LinePhase.MeetingDiscussion || v.Phase == LinePhase.MeetingVote || v.Phase == LinePhase.FinalVote;
            return phase && (v.Features.MeetingChatOpen || v.Features.PresentationOpen);
        }

        /// <summary>A new presented record: everyone's projector jumps to the records slide.</summary>
        void CheckPresentations(LineView v)
        {
            int n = v.Meeting != null ? v.Meeting.Presentations.Count : 0;
            if (n > _presCount && _deck != null)
            {
                int idx = _deck.RecordsIndex;
                if (idx >= 0)
                {
                    _manualSlide = idx;
                    _manualUntil = Time.unscaledTime + 25f;
                    _deck.Show(idx, v, true);
                    var last = v.Meeting.Presentations[n - 1];
                    Sfx.Play(last.ExposedIncident >= 0 ? "fired" : "evidence", 0.8f);
                }
            }
            _presCount = n;
        }

        // ------------------------------------------------------------------ layout

        void ApplyLayout()
        {
            var p = _phase;
            bool meeting = LineClient.IsMeetingPhase(p);
            bool screenView = !_room || _screen;
            bool vote = IsVote(p);
            bool talk = p == LinePhase.MeetingDossier || p == LinePhase.MeetingDiscussion;
            _backdrop.Show((!_room && (meeting || p == LinePhase.Abandoned)) || (_room && _screen && (talk || vote)));
            _backdrop.EnableInClassList("mt-backdrop-dim", _room);
            _backdrop.EnableInClassList("mt-backdrop-dark", p == LinePhase.FinalDefense || p == LinePhase.MeetingVerdict || p == LinePhase.FinalVerdict);
            // The room view leaves the projector to the 3D screen; the screen view shows the readable copy.
            _proj.Show(talk && screenView);
            _proj.EnableInClassList("mt-proj-room", _room);
            _proj.EnableInClassList("mt-proj-side", _side.Visible);
            _top.Show(meeting && p != LinePhase.FinalVerdict);
            _seatRow.Show(meeting && (talk || vote || p == LinePhase.FinalDefense));
            _seatRow.EnableInClassList("mt-seats-compact", _room && !_screen);
            _seatRow.EnableInClassList("mt-seats-clickable", vote);
            _ballot.Root.Show(_ballot.Visible && screenView);
            _follow.Show(_manualSlide >= 0 && p == LinePhase.MeetingDossier);

            bool hint = _room && meeting && p != LinePhase.MeetingVerdict && p != LinePhase.FinalVerdict;
            _hint.Show(hint);
            if (hint)
            {
                string key = _screen ? (vote ? "meeting.hint.look_vote" : "meeting.hint.look") : (vote ? "meeting.hint.ballot" : "meeting.hint.projector");
                string t = Loc.T(key);
                if (!ReferenceEquals(_hintText.text, t) && _hintText.text != t) _hintText.text = t;
            }
        }

        void UpdateTimer(LineView v, double now)
        {
            double left = v.PhaseEndTime - now;
            if (v.Phase == LinePhase.FinalDefense && v.Meeting != null && v.Meeting.Defender >= 0) left = v.Meeting.DefenderEndTime - now;
            int sec = (int)System.Math.Ceiling(System.Math.Max(0, left));
            if (sec == _lastSec) return;
            _lastSec = sec;
            _timer.text = ClockInt(sec);
            bool urgent = sec <= 10 && (v.Phase == LinePhase.MeetingDiscussion || IsVote(v.Phase) || v.Phase == LinePhase.FinalDefense);
            _timer.EnableInClassList("mt-timer-urgent", urgent);
            if (urgent && sec > 0 && sec <= 5) UiMotion.Pulse(_timer, 0.12f, 0.25f);
        }

        // ------------------------------------------------------------------ projector

        void Page(int delta)
        {
            if (_deck == null || _deck.Count == 0) return;
            var v = LineClient.I != null ? LineClient.I.View : null;
            if (v == null) return;
            int i = Mathf.Clamp((_manualSlide >= 0 ? _manualSlide : _deck.Current) + delta, 0, _deck.Count - 1);
            _manualSlide = i;
            _manualUntil = Time.unscaledTime + 20f;
            _deck.Show(i, v);
        }

        void UpdateProjector(LineView v, double now, float dt)
        {
            if (_deck == null || !LineClient.IsMeetingPhase(v.Phase)) return;
            if (_manualSlide >= 0 && Time.unscaledTime > _manualUntil && v.Phase == LinePhase.MeetingDossier) _manualSlide = -1;
            int want = _manualSlide >= 0 ? _manualSlide : (v.Phase == LinePhase.MeetingDossier ? _deck.AutoIndex(v, now) : (_deck.Current >= 0 ? _deck.Current : _deck.RestIndex));
            if (want != _deck.Current) _deck.Show(want, v);
            _deck.Tick(now, dt);
            string title = _deck.TitleOf(_deck.Current);
            if (!string.Equals(_projTitle.text, title)) _projTitle.text = title;
            _prev.SetEnabled(_deck.Current > 0);
            _next.SetEnabled(_deck.Current < _deck.Count - 1);
        }

        // ------------------------------------------------------------------ seats

        sealed class Seat
        {
            public int Id;
            public VisualElement Root, Badge, Ring, Marks, Voted;
            public Label Name, Blame;
            public int ShownBlame = int.MinValue, ShownWarnings = -1;
            public bool Speaking, ShownVoted, ShownConnected = true;
        }

        void BuildSeats(LineView v)
        {
            int key = v.Me * 7919;
            foreach (var p in v.Players) key = key * 31 + p.Id * 13 + (p.Name ?? "").GetHashCode();
            if (key == _seatKey) { UpdateSeats(v, false); return; }
            _seatKey = key;
            _seatRow.Clear();
            _seats.Clear();
            int n = 0;
            foreach (var p in v.Players)
            {
                var s = new Seat { Id = p.Id };
                int id = p.Id;
                s.Root = Box("seat").AddTo(_seatRow);
                if (p.Id == v.Me) s.Root.AddToClassList("seat-me");
                s.Root.RegisterCallback<ClickEvent>(_ => OnSeatCard(id));
                var bw = Box("seat-badge").NoPick().AddTo(s.Root);
                s.Badge = Badge(p.Id, "badge-l").AddTo(bw);
                SetBadge(s.Badge, p.Id, p.Name);
                s.Ring = Box("speak-ring").NoPick().AddTo(bw);
                s.Voted = Box("seat-voted").NoPick().AddTo(bw);
                if (!UiArt.Apply(s.Voted, "meeting_v3/vote_token"))
                {
                    s.Voted.AddToClassList("seat-voted-fallback");
                    Bold(Loc.T("meeting.voted_tick"), "seat-voted-text").NoPick().AddTo(s.Voted);
                }
                s.Voted.Show(false);
                var col = Box("seat-col").NoPick().AddTo(s.Root);
                s.Name = Title(p.Name, "seat-name").NoPick().AddTo(col);
                var line = Box("seat-line").NoPick().AddTo(col);
                s.Blame = Bold("", "seat-blame").NoPick().AddTo(line);
                s.Marks = Box("seat-marks").NoPick().AddTo(line);
                if (p.Id == v.Me) Tag(Loc.T("lobby.you_tag"), "tag-sticky", "seat-you").AddTo(s.Root);
                _seats.Add(s);
                UiMotion.SlideIn(s.Root, new Vector2(0f, 40f), 0.3f, 0.05f + n * 0.06f, UiMotion.Ease.OutBack);
                n++;
            }
            UpdateSeats(v, true);
        }

        void UpdateSeats(LineView v, bool force)
        {
            bool vote = IsVote(v.Phase);
            int myVote = v.Meeting != null ? v.Meeting.MyVote : LineIds.None;
            for (int i = 0; i < _seats.Count; i++)
            {
                var s = _seats[i];
                LinePlayerView p = null;
                foreach (var x in v.Players) if (x.Id == s.Id) { p = x; break; }
                if (p == null) continue;
                if (force || p.Blame != s.ShownBlame)
                {
                    bool up = s.ShownBlame != int.MinValue && p.Blame > s.ShownBlame;
                    s.ShownBlame = p.Blame;
                    s.Blame.text = Loc.F("meeting.blame_n", p.Blame);
                    if (up) UiMotion.Shake(s.Root, 6f, 0.3f);
                }
                if (force || p.Warnings != s.ShownWarnings)
                {
                    s.ShownWarnings = p.Warnings;
                    s.Marks.Clear();
                    for (int k = 0; k < p.Warnings; k++) DossierDeck.WarningMark(s.Marks);
                }
                bool voted = vote && p.Voted;
                if (force || voted != s.ShownVoted)
                {
                    s.ShownVoted = voted;
                    s.Voted.Show(voted);
                    if (voted && !force) UiMotion.Stamp(s.Voted, -12f, 0.18f);
                }
                if (force || p.Connected != s.ShownConnected)
                {
                    s.ShownConnected = p.Connected;
                    s.Root.EnableInClassList("offline", !p.Connected);
                }
                s.Root.EnableInClassList("seat-chosen", vote && myVote == s.Id);
                s.Root.EnableInClassList("seat-defender", v.Phase == LinePhase.FinalDefense && v.Meeting != null && v.Meeting.Defender == s.Id);
            }
        }

        /// <summary>Per frame: speaking rings (voice), allocation-free.</summary>
        void UpdateSeatsLive(LineView v)
        {
            var voice = UiServices.Voice;
            for (int i = 0; i < _seats.Count; i++)
            {
                var s = _seats[i];
                bool sp = voice.Available && voice.IsSpeaking(s.Id);
                if (sp == s.Speaking) continue;
                s.Speaking = sp;
                s.Ring.EnableInClassList("on", sp);
                s.Root.EnableInClassList("seat-speaking", sp);
            }
            // hover hint for 3D voting
            int hover = _room && !_screen && IsVote(v.Phase) ? Rooms.HoveredSeat : -1;
            if (hover == v.Me) hover = -1;
            if (hover != _lastHover)
            {
                _lastHover = hover;
                _hover.Show(hover >= 0);
                if (hover >= 0) _hoverText.text = Loc.F("meeting.hover_vote", MeetingText.Name(hover));
            }
        }

        // ------------------------------------------------------------------ voting

        void OnSeatCard(int slot)
        {
            var v = LineClient.I != null ? LineClient.I.View : null;
            if (v == null || !IsVote(v.Phase) || slot == v.Me) return;
            Vote(slot);
        }

        void OnSeatClicked(int slot)
        {
            if (!Visible) return;
            var v = LineClient.I != null ? LineClient.I.View : null;
            if (v == null || !IsVote(v.Phase) || slot == v.Me) return;
            Vote(slot);
        }

        void Vote(int target)
        {
            var c = LineClient.I;
            if (c == null || c.View == null || !IsVote(c.View.Phase)) return;
            c.Send(LineVerb.Vote, target);
            _ballot.MarkPending(target);
            Sfx.Play("vote");
        }

        // ------------------------------------------------------------------ 3D room

        void DriveRoom(LineView v, double now)
        {
            var rooms = Rooms.Presenter;
            if (rooms == null) return;
            if (!_projectorSent && ProjectorSurface.Texture != null)
            {
                rooms.SetProjectorTexture(ProjectorSurface.Texture);
                _projectorSent = true;
            }
            PovMode want;
            switch (v.Phase)
            {
                case LinePhase.MeetingVerdict:
                case LinePhase.FinalVerdict:
                case LinePhase.WeekResults:
                case LinePhase.Abandoned:
                    want = PovMode.Cutscene; break;
                default:
                    want = _screen && !IsVote(v.Phase) ? PovMode.MeetingProjector : PovMode.Meeting; break;
            }
            if (want != _lastMode) { _lastMode = want; if (rooms.Mode != want) rooms.SetPovMode(want); }

            int spot = v.Phase == LinePhase.FinalDefense && v.Meeting != null ? v.Meeting.Defender : -1;
            if (spot != _spot) { _spot = spot; rooms.SetSpotlight(spot); }
            int fired = (v.Phase == LinePhase.FinalVerdict || v.Phase == LinePhase.WeekResults) && v.Meeting != null ? v.Meeting.Fired : (v.Result != null ? v.Result.Fired : -1);
            if (fired < 0) fired = -1; // nobody fired / company closed
            if (fired != _fired)
            {
                _fired = fired;
                rooms.SetFired(fired);
                if (fired >= 0) rooms.React(fired, MeetingReaction.Slump);
            }
            bool presenting = v.Phase == LinePhase.MeetingDossier && !RecordingAudible(v, now);
            if (presenting != _managerTalking) { _managerTalking = presenting; rooms.SetManagerTalking(presenting); }
            _verdict.DriveRoom(rooms);
        }

        static bool RecordingAudible(LineView v, double now)
        {
            var src = RecordingPlayback.Source;
            if (src != null) return src.PlayingPick >= 0;
            if (v.Meeting == null) return false;
            var recs = v.Meeting.Recordings;
            for (int i = 0; i < recs.Count; i++)
                if (recs[i].Caller >= 0 && now >= recs[i].PlayAt && now < recs[i].PlayAt + recs[i].Seconds) return true;
            return false;
        }
    }
}
