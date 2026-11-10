using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// UI Toolkit root. All screens are built in code (UiKit + Game.uss) so the UI is reproducible from source.
    /// Owns screen visibility, Esc routing and the cursor policy: menu / lobby screens keep the cursor free; during a
    /// week the cursor is locked only while the player looks around the room (no computer, phone or overlay open).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class UIRoot : MonoBehaviour
    {
        public static UIRoot I { get; private set; }
        public StyleSheet Style;
        public Font BodyFont;
        public Font BoldFont;
        public Font TitleFont;

        public static bool PointerOverUI { get; private set; }
        /// <summary>True when a panel (computer, phone, pause, meeting, lobby...) takes the mouse; room input is ignored.</summary>
        public static bool BlocksGameplay { get; private set; }

        VisualElement _root;
        MainMenuScreen _menu;
        LobbyScreen _lobby;
        PauseMenu _pause;
        SettingsScreen _settings;
        HowToPlayScreen _howTo;
        ConfirmDialog _confirm;
        StatusOverlay _status;
        ConsentScreen _consent;
        IntroScreen _intro;
        MomentMarker _moment;
        PlayUi _play;
        bool _built, _rebuildPending;
        readonly System.Collections.Generic.List<VisualElement> _screenElements = new System.Collections.Generic.List<VisualElement>();

        /// <summary>Root element of the UI document (the week screens and the UI mock attach below it).</summary>
        public VisualElement RootElement => _root;

        void Awake()
        {
            I = this;
            UiInput.Initialize();
        }

        /// <summary>True while a text field has keyboard focus (suppresses push-to-talk / hotkeys).</summary>
        public bool TextInputFocused
        {
            get
            {
                var fc = _root != null && _root.panel != null ? _root.panel.focusController : null;
                var f = fc != null ? fc.focusedElement as VisualElement : null;
                return f != null && (f is TextField || f.GetFirstAncestorOfType<TextField>() != null);
            }
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
            bool settings = _settings.Visible, howTo = _howTo.Visible, pause = _pause.Visible, join = _menu.JoinOpen, intro = _intro.Visible && !ShellFlow.NeedsIntro;
            int tab = (int)SettingsScreen.CurrentTab;
            Modal.CloseAll();
            foreach (var e in _screenElements) e.RemoveFromHierarchy();
            _screenElements.Clear();
            _built = false;
            BuildScreens();
            if (pause) _pause.Toggle();
            if (howTo) HowToPlayScreen.Open();
            if (settings) SettingsScreen.Open(tab);
            if (intro) IntroScreen.Open(false);
            _menu.OpenJoinOnShow = join;
        }

        void BuildScreens()
        {
            int before = _root.childCount;
            // Layers, bottom to top.
            _lobby = new LobbyScreen(_root);
            _menu = new MainMenuScreen(_root);
            _play = new PlayUi(_root);
            _pause = new PauseMenu(_root);
            _howTo = new HowToPlayScreen(_root);
            _settings = new SettingsScreen(_root);
            _confirm = new ConfirmDialog(_root);
            _status = new StatusOverlay(_root);
            _intro = new IntroScreen(_root);
            _consent = new ConsentScreen(_root);
            _moment = new MomentMarker(_root);
            for (int i = before; i < _root.childCount; i++) _screenElements.Add(_root[i]);
            _built = true;
        }

        void Update()
        {
            if (_rebuildPending) { _rebuildPending = false; Rebuild(); }
            if (!_built) return;
            var conn = ConnectionManager.I;
            var lineView = LineClient.I != null ? LineClient.I.View : null;
            if (lineView != null) { UpdateWeek(lineView); return; }

            // ---- no week running: main menu or lobby
            _play.SetVisible(false);
            bool online = conn != null && (conn.Current == ConnectionManager.State.Hosting || conn.Current == ConnectionManager.State.Connected || conn.Current == ConnectionManager.State.Reconnecting || conn.Current == ConnectionManager.State.Connecting);
            online |= LineClient.I != null && LineClient.I.Lobby != null; // lobby (also the UI mock)

            // Esc: close the top-most thing first
            if (GameInput.Pause != null && GameInput.Pause.WasPressedThisFrame())
            {
                if (Modal.CloseTop()) { }
                else if (_intro.Visible) _intro.Skip();
                else if (_settings.Visible) _settings.Hide();
                else if (_howTo.Visible) _howTo.Hide();
                else if (!online && _menu.JoinOpen) _menu.ShowJoin(false);
                else if (online) _pause.Toggle();
            }
            bool overlay = _pause.Visible || _settings.Visible || _howTo.Visible || Modal.AnyOpen || _intro.Visible || _consent.Visible;

            _menu.SetVisible(!online);
            _lobby.SetVisible(online && (conn == null || conn.Current != ConnectionManager.State.Connecting));
            if (!online && _pause.Visible) _pause.Hide();

            _menu.Tick(); _lobby.Tick(); _pause.Tick();
            _settings.Tick(); _howTo.Tick(); _confirm.Tick(); _status.Tick(); _intro.Tick(); _moment.Tick();

            bool modal = !online || _lobby.Visible || overlay;
            BlocksGameplay = modal;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
            PointerOverUI = modal;
        }

        /// <summary>Match running: the play screen over the first-person rooms.</summary>
        void UpdateWeek(LineView v)
        {
            if (GameInput.Pause != null && GameInput.Pause.WasPressedThisFrame())
            {
                if (Modal.CloseTop()) { }
                else if (_intro.Visible) _intro.Skip();
                else if (_settings.Visible) _settings.Hide();
                else if (_howTo.Visible) _howTo.Hide();
                else _pause.Toggle();
            }
            bool overlay = _pause.Visible || _settings.Visible || _howTo.Visible || Modal.AnyOpen || _intro.Visible;
            _menu.SetVisible(false); _lobby.SetVisible(false);
            // v5 Phase 2a: one minimal play screen for every phase (the 3D room shows through it).
            _play.SetVisible(true);

            _play.Tick();
            _pause.Tick(); _settings.Tick(); _howTo.Tick(); _confirm.Tick(); _status.Tick(); _intro.Tick(); _moment.Tick();

            var rooms = Rooms.Presenter;
            bool lookMode = rooms != null && (rooms.Mode == PovMode.Office || rooms.Mode == PovMode.Meeting);
            bool wantsCursor = overlay || _play.WantsCursor || !lookMode;
            BlocksGameplay = wantsCursor;
            bool wantLock = !wantsCursor && Application.isFocused && !LaunchArgs.Current.Bot && !LineMockDriver.Active;
            UnityEngine.Cursor.lockState = wantLock ? CursorLockMode.Locked : CursorLockMode.None;
            UnityEngine.Cursor.visible = !wantLock;
            PointerOverUI = wantsCursor;
        }
    }
}
