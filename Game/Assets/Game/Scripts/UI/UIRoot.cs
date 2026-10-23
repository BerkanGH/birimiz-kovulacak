using System;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// UI Toolkit root. All screens are built in code (UiKit helpers + Game.uss) so the UI is reproducible from
    /// source. Owns cursor policy: the cursor is locked only during free movement in the office.
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
        /// <summary>True when a modal panel (phone, pause, meeting, lobby...) takes the mouse.</summary>
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
        StatusOverlay _status;

        void Awake() { I = this; }

        void Start()
        {
            var doc = GetComponent<UIDocument>();
            _root = doc.rootVisualElement;
            if (Style != null) _root.styleSheets.Add(Style);
            UiKit.BodyFont = BodyFont; UiKit.BoldFont = BoldFont; UiKit.TitleFont = TitleFont;
            if (BodyFont != null) _root.style.unityFontDefinition = new StyleFontDefinition(BodyFont);
            _root.AddToClassList("root");
            _root.pickingMode = PickingMode.Ignore;

            _hud = new Hud(_root);
            _briefing = new BriefingScreen(_root);
            _phone = new PhonePanel(_root);
            _meeting = new MeetingScreen(_root);
            _results = new ResultsScreen(_root);
            _lobby = new LobbyScreen(_root);
            _menu = new MainMenuScreen(_root);
            _pause = new PauseMenu(_root);
            _status = new StatusOverlay(_root);
            _root.RegisterCallback<PointerEnterEvent>(_ => { }, TrickleDown.TrickleDown);
        }

        void Update()
        {
            var cs = ClientState.I;
            var conn = ConnectionManager.I;
            var view = cs != null ? cs.View : null;
            bool online = conn != null && (conn.Current == ConnectionManager.State.Hosting || conn.Current == ConnectionManager.State.Connected || conn.Current == ConnectionManager.State.Reconnecting || conn.Current == ConnectionManager.State.Connecting);
            bool inMatch = view != null;

            if (GameInput.Pause != null && GameInput.Pause.WasPressedThisFrame())
            {
                if (_phone.Visible) _phone.Hide();
                else if (online) _pause.Toggle();
            }
            if (GameInput.Phone != null && GameInput.Phone.WasPressedThisFrame() && inMatch && !_pause.Visible && view.Phase < MatchPhase.MeetingDiscussion)
                _phone.Toggle();

            _menu.SetVisible(!online);
            _lobby.SetVisible(online && !inMatch && conn.Current != ConnectionManager.State.Connecting);
            _hud.SetVisible(inMatch);
            _briefing.SetVisible(inMatch && view.Phase == MatchPhase.Briefing);
            bool meeting = inMatch && view.Phase >= MatchPhase.MeetingDiscussion && view.Phase <= MatchPhase.MeetingVerdict;
            _meeting.SetVisible(meeting);
            _results.SetVisible(inMatch && (view.Phase == MatchPhase.Results || view.Phase == MatchPhase.Abandoned));
            if (!inMatch || meeting || view.Phase == MatchPhase.Results) _phone.Hide();

            _menu.Tick(); _lobby.Tick(); _hud.Tick(); _briefing.Tick(); _phone.Tick(); _meeting.Tick(); _results.Tick(); _pause.Tick(); _status.Tick();

            bool modal = !online || _lobby.Visible || _phone.Visible || _pause.Visible || meeting || _results.Visible || _briefing.Visible && _briefing.WantsCursor;
            BlocksGameplay = modal;
            bool wantLock = !modal && inMatch && Application.isFocused && !LaunchArgs.Current.Bot;
            UnityEngine.Cursor.lockState = wantLock ? CursorLockMode.Locked : CursorLockMode.None;
            UnityEngine.Cursor.visible = !wantLock;
            PointerOverUI = modal;
        }
    }
}
