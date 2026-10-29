using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// UI Toolkit root. All screens are built in code (UiKit + Game.uss) so the UI is reproducible from source.
    /// Owns screen visibility, Esc / Tab routing and the cursor policy: the cursor is locked only during free
    /// movement in the office.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class UIRoot : MonoBehaviour, IFeelUi
    {
        public static UIRoot I { get; private set; }
        public StyleSheet Style;
        public Font BodyFont;
        public Font BoldFont;
        public Font TitleFont;

        public static bool PointerOverUI { get; private set; }
        /// <summary>True when a modal panel (phone, pause, meeting, lobby...) takes the mouse; gameplay input is ignored.</summary>
        public static bool BlocksGameplay { get; private set; }

        VisualElement _root;
        MainMenuScreen _menu;
        LobbyScreen _lobby;
        Hud _hud;
        PhonePanel _phone;
        MeetingScreen _meeting;
        ResultsScreen _results;
        PauseMenu _pause;
        BriefingScreen _briefing;
        SettingsScreen _settings;
        HowToPlayScreen _howTo;
        EmailPicker _email;
        ConfirmDialog _confirm;
        StatusOverlay _status;
        // v3
        DeskUi _desk;
        MeetingUi _meetingV3;
        bool _built, _rebuildPending;
        readonly System.Collections.Generic.List<VisualElement> _screenElements = new System.Collections.Generic.List<VisualElement>();

        /// <summary>Root element of the UI document (v3 screens and the UI mock attach below it).</summary>
        public VisualElement RootElement => _root;

        void Awake()
        {
            I = this;
            UiInput.Initialize();
            FeelHooks.Ui = this;
            FeelHooks.UiDrivesResultsStamp = true;
        }

        // ---- IFeelUi (docs/FEEL.md)
        public bool TextInputFocused
        {
            get
            {
                var fc = _root != null && _root.panel != null ? _root.panel.focusController : null;
                var f = fc != null ? fc.focusedElement as VisualElement : null;
                return f != null && (f is TextField || f.GetFirstAncestorOfType<TextField>() != null);
            }
        }

        public bool DrawsEmoteWheel => _built;

        public void PickEmailTemplate(string pcTargetId, System.Action<int> onPicked)
        {
            if (!_built) { onPicked?.Invoke(-1); return; }
            EmailPicker.Open(pcTargetId, onPicked);
        }

        void Start()
        {
            var doc = GetComponent<UIDocument>();
            _root = doc.rootVisualElement;
            if (Style != null) _root.styleSheets.Add(Style);
            UiKit.BodyFont = BodyFont; UiKit.BoldFont = BoldFont; UiKit.TitleFont = TitleFont;
            UiKit.LoadExtraFonts();
            if (BodyFont != null) _root.style.unityFontDefinition = new StyleFontDefinition(BodyFont);
            _root.AddToClassList("root");
            if (UiArt.KitAvailable) _root.AddToClassList("art");
            _root.pickingMode = PickingMode.Ignore;
            BuildScreens();
            Loc.Changed += OnLanguageChanged;
        }

        void OnDestroy() { Loc.Changed -= OnLanguageChanged; }

        /// <summary>Language switched (Settings, menu, first-launch picker): rebuild next frame, outside the click handler.</summary>
        void OnLanguageChanged() { _rebuildPending = true; }

        /// <summary>
        /// Recreates every screen so all texts, logos and per-language stamps follow <see cref="Loc.Language"/>.
        /// Overlays that were open (settings tab, how-to, pause, join page) are reopened.
        /// </summary>
        public void Rebuild()
        {
            if (!_built) return;
            bool settings = _settings.Visible, howTo = _howTo.Visible, pause = _pause.Visible, join = _menu.JoinOpen;
            int tab = (int)SettingsScreen.CurrentTab;
            Modal.CloseAll();
            foreach (var e in _screenElements) e.RemoveFromHierarchy();
            _screenElements.Clear();
            _built = false;
            BuildScreens();
            if (pause) _pause.Toggle();
            if (howTo) HowToPlayScreen.Open();
            if (settings) SettingsScreen.Open(tab);
            _menu.OpenJoinOnShow = join;
        }

        void BuildScreens()
        {
            int before = _root.childCount;
            // Layers, bottom to top.
            var feedLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            _hud = new Hud(_root, feedLayer);
            _briefing = new BriefingScreen(_root);
            _phone = new PhonePanel(_root);
            _meeting = new MeetingScreen(_root);
            _results = new ResultsScreen(_root);
            _lobby = new LobbyScreen(_root);
            _menu = new MainMenuScreen(_root);
            _desk = new DeskUi(_root);
            _meetingV3 = new MeetingUi(_root);
            feedLayer.style.position = Position.Absolute;
            feedLayer.style.left = 0; feedLayer.style.top = 0; feedLayer.style.right = 0; feedLayer.style.bottom = 0;
            _root.Add(feedLayer);
            _pause = new PauseMenu(_root);
            _howTo = new HowToPlayScreen(_root);
            _settings = new SettingsScreen(_root);
            _email = new EmailPicker(_root);
            _confirm = new ConfirmDialog(_root);
            _status = new StatusOverlay(_root);
            for (int i = before; i < _root.childCount; i++) _screenElements.Add(_root[i]);
            _built = true;
        }

        void Update()
        {
            if (_rebuildPending) { _rebuildPending = false; Rebuild(); }
            if (!_built) return;
            var cs = ClientState.I;
            var conn = ConnectionManager.I;
            var view = cs != null ? cs.View : null;
            MatchClock.Update(view);
            var lineView = LineClient.I != null ? LineClient.I.View : null;
            if (lineView != null) { UpdateV3(lineView, conn); return; }
            _desk.SetVisible(false);
            _meetingV3.SetVisible(false);
            bool online = conn != null && (conn.Current == ConnectionManager.State.Hosting || conn.Current == ConnectionManager.State.Connected || conn.Current == ConnectionManager.State.Reconnecting || conn.Current == ConnectionManager.State.Connecting);
            online |= LineClient.I != null && LineClient.I.Lobby != null; // v3 lobby (also the UI mock)
            bool inMatch = view != null;
            MatchPhase phase = inMatch ? view.Phase : MatchPhase.None;
            bool office = inMatch && GameInfo.OfficePhase(phase);
            bool meeting = inMatch && GameInfo.MeetingPhase(phase);
            bool results = inMatch && (phase == MatchPhase.Results || phase == MatchPhase.Abandoned);

            // ---- Esc: close the top-most thing first
            if (GameInput.Pause != null && GameInput.Pause.WasPressedThisFrame())
            {
                if (Modal.CloseTop()) { }
                else if (_settings.Visible) _settings.Hide();
                else if (_howTo.Visible) _howTo.Hide();
                else if (_phone.Visible) _phone.Hide();
                else if (!online && _menu.JoinOpen) _menu.ShowJoin(false);
                else if (online) _pause.Toggle();
            }
            bool overlay = _pause.Visible || _settings.Visible || _howTo.Visible || Modal.AnyOpen;
            if (GameInput.Phone != null && GameInput.Phone.WasPressedThisFrame() && office && !overlay)
                _phone.Toggle();

            _menu.SetVisible(!online);
            _lobby.SetVisible(online && !inMatch && conn.Current != ConnectionManager.State.Connecting);
            _hud.SetVisible(inMatch);
            _hud.SetOfficeVisible(office);
            _briefing.SetVisible(inMatch && phase == MatchPhase.Briefing);
            _meeting.SetVisible(meeting);
            _results.SetVisible(results);
            if (!office) _phone.Hide();
            if (!online && _pause.Visible) _pause.Hide();
            if (!inMatch && _email.Visible) _email.Close();

            bool inputAllowed = inMatch && !overlay;
            _hud.Tick(inputAllowed);
            _menu.Tick(); _lobby.Tick(); _briefing.Tick(); _phone.Tick(); _meeting.Tick(); _results.Tick(); _pause.Tick();
            _settings.Tick(); _howTo.Tick(); _email.Tick(); _confirm.Tick(); _status.Tick();

            bool modal = !online || _lobby.Visible || _phone.Visible || overlay || meeting || _results.Visible || (_briefing.Visible && _briefing.WantsCursor);
            BlocksGameplay = modal;
            bool wantLock = !modal && inMatch && Application.isFocused && !LaunchArgs.Current.Bot;
            UnityEngine.Cursor.lockState = wantLock ? CursorLockMode.Locked : CursorLockMode.None;
            UnityEngine.Cursor.visible = !wantLock;
            PointerOverUI = modal;
        }

        /// <summary>v3 week running: office/meeting screens over the first-person world; v2 screens stay hidden.</summary>
        void UpdateV3(LineView v, ConnectionManager conn)
        {
            if (GameInput.Pause != null && GameInput.Pause.WasPressedThisFrame())
            {
                if (Modal.CloseTop()) { }
                else if (_settings.Visible) _settings.Hide();
                else if (_howTo.Visible) _howTo.Hide();
                else _pause.Toggle();
            }
            bool overlay = _pause.Visible || _settings.Visible || _howTo.Visible || Modal.AnyOpen;
            bool office = LineClient.IsOfficePhase(v.Phase);
            bool meeting = LineClient.IsMeetingPhase(v.Phase) || v.Phase == LinePhase.WeekResults || v.Phase == LinePhase.Abandoned;

            _menu.SetVisible(false); _lobby.SetVisible(false); _hud.SetVisible(false); _briefing.SetVisible(false);
            _meeting.SetVisible(false); _results.SetVisible(false); _phone.Hide();
            _desk.SetVisible(office);
            _meetingV3.SetVisible(meeting);

            _desk.Tick(); _meetingV3.Tick();
            _pause.Tick(); _settings.Tick(); _howTo.Tick(); _confirm.Tick(); _status.Tick();

            var rooms = Rooms.Presenter;
            bool lookMode = rooms != null && (rooms.Mode == PovMode.Office || rooms.Mode == PovMode.Meeting);
            bool wantsCursor = overlay || (office && _desk.WantsCursor) || (meeting && _meetingV3.WantsCursor) || !lookMode;
            BlocksGameplay = wantsCursor;
            bool wantLock = !wantsCursor && Application.isFocused && !LaunchArgs.Current.Bot && !LineMockDriver.Active;
            UnityEngine.Cursor.lockState = wantLock ? CursorLockMode.Locked : CursorLockMode.None;
            UnityEngine.Cursor.visible = !wantLock;
            PointerOverUI = wantsCursor;
        }
    }
}
