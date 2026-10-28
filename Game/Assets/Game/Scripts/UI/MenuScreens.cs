using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>Main menu (logo, Lobi Kur / Lobiye Katıl / Ayarlar / Nasıl Oynanır / Çıkış) with the join page.</summary>
    public sealed class MainMenuScreen : UiKit.Screen
    {
        const string RecentKey = "join.recent";
        readonly TextField _name, _address, _port;
        readonly Button _rejoin;
        readonly Label _error, _joinError;
        readonly VisualElement _main, _join, _recent;
        bool _joinOpen;

        public bool JoinOpen => _joinOpen;

        public MainMenuScreen(VisualElement parent) : base(parent, "menu-bg")
        {
            UiArt.Apply(Root, "backgrounds/menu_bg");
            var layout = Box("menu-layout").AddTo(Root);
            var col = Box("col", "center").AddTo(layout);

            var logo = Box("menu-logo").AddTo(col);
            if (!UiArt.Apply(logo, "logo/logo"))
            {
                logo.RemoveFromClassList("menu-logo");
                Title(Loc.T("game.title_1"), "menu-title").AddTo(logo);
                Title(Loc.T("game.title_2"), "menu-title-red").AddTo(logo);
            }
            Bold(Loc.T("game.tagline"), "menu-tagline").AddTo(col);

            // ---- main card
            _main = Panel("panel", "menu-card").AddTo(col);
            _name = new TextField(Loc.T("menu.name")) { value = PlayerPrefs.GetString("player.name", DefaultName()), maxLength = 16 }.Cls("field").AddTo(_main);
            IconBtn("host", Loc.T("menu.host"), Host, "btn-primary", "menu-btn").AddTo(_main);
            IconBtn("contacts", Loc.T("menu.join"), () => ShowJoin(true), "menu-btn").AddTo(_main);
            _rejoin = IconBtn("call", Loc.T("menu.rejoin"), Rejoin, "btn-primary", "menu-btn").AddTo(_main);
            _rejoin.Show(false);
            var row = Box("row").AddTo(_main);
            IconBtn("settings", Loc.T("menu.settings"), SettingsScreen.Open, "btn-small", "grow").AddTo(row);
            IconBtn("info", Loc.T("menu.how_to"), HowToPlayScreen.Open, "btn-small", "grow").AddTo(row);
            Btn(Loc.T("menu.quit"), Application.Quit, "btn-small", "btn-danger").AddTo(row);
            _error = Text("", "error-line").AddTo(_main);

            // ---- join card
            _join = Panel("panel", "menu-card").AddTo(col);
            Title(Loc.T("join.title"), "h2").AddTo(_join);
            var jr = Box("row").AddTo(_join);
            _address = new TextField(Loc.T("menu.address")) { value = PlayerPrefs.GetString("join.address", "127.0.0.1") }.Cls("field", "grow").AddTo(jr);
            _port = new TextField(Loc.T("menu.port")) { value = PlayerPrefs.GetString("join.port", "7777"), maxLength = 5 }.Cls("field").AddTo(_join);
            Text(Loc.T("join.port_help"), "t-tiny", "t-muted").AddTo(_join);
            var ja = Box("row").AddTo(_join);
            Btn(Loc.T("common.back"), () => ShowJoin(false), "btn-ghost").AddTo(ja);
            IconBtn("call", Loc.T("join.connect"), Join, "btn-primary", "grow").AddTo(ja);
            _joinError = Text("", "error-line").AddTo(_join);
            Section(_join, Loc.T("join.recent"), "notes");
            _recent = Box("col").AddTo(_join);
            _join.Show(false);

            Text(Loc.F("menu.version", GameConfig.BuildId), "version").AddTo(Root);
            _address.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Join(); });
        }

        static string DefaultName()
        {
            string n = Environment.UserName;
            return string.IsNullOrEmpty(n) ? Loc.T("common.player") : (n.Length > 16 ? n.Substring(0, 16) : n);
        }

        public void ShowJoin(bool on)
        {
            _joinOpen = on;
            _main.Show(!on);
            _join.Show(on);
            if (on) { BuildRecent(); Pop(_join); }
            else Pop(_main);
        }

        void BuildRecent()
        {
            _recent.Clear();
            var list = RecentHosts();
            if (list.Count == 0) Empty(_recent, Loc.T("join.no_recent"));
            foreach (var hp in list)
            {
                string entry = hp;
                var r = Box("recent-row").AddTo(_recent);
                Bold(entry, "grow").AddTo(r);
                Btn(Loc.T("join.use"), () =>
                {
                    int colon = entry.LastIndexOf(':');
                    _address.value = colon > 0 ? entry.Substring(0, colon) : entry;
                    if (colon > 0) _port.value = entry.Substring(colon + 1);
                    Join();
                }, "btn-small", "btn-primary").AddTo(r);
            }
        }

        static List<string> RecentHosts()
        {
            var s = PlayerPrefs.GetString(RecentKey, "");
            var list = new List<string>();
            foreach (var e in s.Split('|')) if (!string.IsNullOrWhiteSpace(e) && !list.Contains(e)) list.Add(e);
            return list;
        }

        static void RememberHost(string address, ushort port)
        {
            var list = RecentHosts();
            string e = address.Trim() + ":" + port;
            list.Remove(e);
            list.Insert(0, e);
            while (list.Count > 5) list.RemoveAt(list.Count - 1);
            PlayerPrefs.SetString(RecentKey, string.Join("|", list));
        }

        void Save()
        {
            PlayerPrefs.SetString("player.name", _name.value);
            PlayerPrefs.SetString("join.address", _address.value);
            PlayerPrefs.SetString("join.port", _port.value);
            PlayerPrefs.Save();
        }

        ushort Port() { ushort p; return ushort.TryParse(_port.value, out p) && p > 0 ? p : (ushort)7777; }

        string PlayerName() { return string.IsNullOrWhiteSpace(_name.value) ? DefaultName() : _name.value.Trim(); }

        void Host() { Save(); ConnectionManager.I.StartHost(PlayerName(), Port()); }

        void Join()
        {
            if (string.IsNullOrWhiteSpace(_address.value)) { _joinError.text = Loc.T("join.need_address"); return; }
            Save();
            RememberHost(_address.value, Port());
            ConnectionManager.I.StartClient(PlayerName(), _address.value, Port());
        }

        void Rejoin()
        {
            string token, address; ushort port;
            if (ConnectionManager.TryGetRecentSession(out token, out address, out port))
                ConnectionManager.I.StartClient(PlayerName(), address, port, token);
        }

        protected override void OnShow() { ShowJoin(false); }

        float _nextCheck;
        public override void Tick()
        {
            if (!Visible || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 0.25f;
            var c = ConnectionManager.I;
            string tk, ad; ushort po;
            _rejoin.Show(!_joinOpen && ConnectionManager.TryGetRecentSession(out tk, out ad, out po));
            string err = c != null && c.Current == ConnectionManager.State.Failed && !string.IsNullOrEmpty(c.LastErrorKey) ? Loc.T(c.LastErrorKey) : "";
            _error.SetText(err);
            _joinError.SetText(err);
        }
    }

    /// <summary>Lobby: 3–6 seats with badges and ready stamps, host controls (mode, bots, start), week board.</summary>
    public sealed class LobbyScreen : UiKit.Screen
    {
        readonly VisualElement _grid, _modeHost, _week;
        readonly Label _modeClient, _status, _addresses;
        readonly Button _ready, _start, _addBot, _quick, _weekBtn;
        readonly List<SlotCard> _cards = new List<SlotCard>();
        int _lastHash = int.MinValue;
        float _nextStatus;

        public LobbyScreen(VisualElement parent) : base(parent, "menu-bg", "center")
        {
            UiArt.Apply(Root, "backgrounds/lobby_bg");
            var card = Panel("panel-folder", "lobby").AddTo(Root);
            var head = Box("lobby-head").AddTo(card);
            Title(Loc.T("lobby.title"), "h1").AddTo(head);
            Box("spacer").AddTo(head);
            _modeHost = Box("mode-switch").AddTo(head);
            _quick = Btn(Loc.T("mode.QuickPlay"), () => SetMode(GameMode.QuickPlay), "btn-small", "mode-btn").AddTo(_modeHost);
            _weekBtn = Btn(Loc.T("mode.Week"), () => SetMode(GameMode.Week), "btn-small", "mode-btn").AddTo(_modeHost);
            _modeClient = Bold("", "mode-label").AddTo(head);
            _addresses = Text("", "address-line").AddTo(card);

            _grid = Box("slot-grid").AddTo(card);
            for (int i = 0; i < Protocol.MaxPlayers; i++) _cards.Add(new SlotCard(_grid, i));
            _week = Box("col").AddTo(card);

            _status = Bold("", "lobby-status").AddTo(card);
            var actions = Box("lobby-actions").AddTo(card);
            Btn(Loc.T("lobby.leave"), ConfirmLeave, "btn-ghost").AddTo(actions);
            _addBot = IconBtn("bot", Loc.T("lobby.add_bot"), () => ConnectionManager.I.HostController?.AddBot(), "btn-secondary").AddTo(actions);
            _ready = IconBtn("ready", Loc.T("lobby.ready"), ToggleReady, "btn-primary", "btn-big").AddTo(actions);
            _start = IconBtn("ready", Loc.T("lobby.start"), () => ConnectionManager.I.HostController?.StartMatch(), "btn-primary", "btn-big").AddTo(actions);
            Text(Loc.T(UiServices.Voice.Available ? "lobby.voice_hint" : "lobby.voice_hint_discord"), "t-small", "t-muted", "t-center").AddTo(card);
        }

        static void SetMode(GameMode m) { ConnectionManager.I.HostController?.SetMode(m); }

        static void ConfirmLeave()
        {
            bool host = ConnectionManager.I.HostController != null;
            ConfirmDialog.Show(Loc.T("lobby.leave"), Loc.T(host ? "lobby.leave_host_confirm" : "lobby.leave_confirm"), Loc.T("lobby.leave"), () => ConnectionManager.I.Leave(), true);
        }

        void ToggleReady()
        {
            var lobby = ClientState.I.Lobby;
            if (lobby == null || NetSession.Instance == null) return;
            LobbySlot mine = null;
            foreach (var s in lobby.Slots) if (s.Slot == ClientState.I.MySlot) mine = s;
            NetSession.Instance.SetReady(mine == null || !mine.Ready);
        }

        protected override void OnShow() { _lastHash = int.MinValue; }

        public override void Tick()
        {
            if (!Visible) return;
            var cs = ClientState.I;
            var lobby = cs.Lobby;
            var hc = ConnectionManager.I.HostController;
            bool isHost = hc != null;
            if (lobby == null)
            {
                _status.SetText(Loc.T("lobby.waiting"));
                return;
            }
            int hash = 17;
            foreach (var s in lobby.Slots)
                hash = hash * 31 + (s.Occupied ? 1 : 0) + (s.Ready ? 2 : 0) + (s.Connected ? 4 : 0) + (s.IsBot ? 8 : 0) + (s.IsHost ? 16 : 0) + (s.Name ?? "").GetHashCode();
            hash = hash * 31 + (int)lobby.Mode * 7 + lobby.WeekDay * 131 + cs.MySlot * 17 + (isHost ? 1 : 0);
            foreach (int p in lobby.CareerPoints) hash = hash * 31 + p;
            if (hash != _lastHash)
            {
                _lastHash = hash;
                Rebuild(lobby, isHost, cs.MySlot);
            }
            if (Time.unscaledTime >= _nextStatus)
            {
                _nextStatus = Time.unscaledTime + 0.25f;
                string reason = lobby.StatusKey;
                bool can = isHost && hc.CanStart(out reason);
                _start.SetEnabled(can);
                _status.SetText(Loc.T(string.IsNullOrEmpty(reason) ? "lobby.waiting" : reason));
                _status.EnableInClassList("t-good", can);
                _addresses.SetText(isHost ? Loc.F("lobby.host_addresses", LocalAddresses(), ConnectionManager.I.Port) : Loc.F("lobby.connected_to", ConnectionManager.I.Address));
                if (isHost && LaunchArgs.Current.AutoStart && can) hc.StartMatch();
            }
        }

        void Rebuild(LobbyView lobby, bool isHost, int mySlot)
        {
            int occupied = 0;
            LobbySlot mine = null;
            for (int i = 0; i < _cards.Count; i++)
            {
                LobbySlot s = null;
                foreach (var x in lobby.Slots) if (x.Slot == i) s = x;
                _cards[i].Set(s, lobby, isHost, mySlot);
                if (s != null && s.Occupied) occupied++;
                if (s != null && s.Slot == mySlot) mine = s;
            }
            _modeHost.Show(isHost);
            _modeClient.Show(!isHost);
            _modeClient.text = Loc.T("mode." + lobby.Mode) + (lobby.Mode == GameMode.Week ? " · " + Loc.DayName(lobby.WeekDay) : "");
            _quick.EnableInClassList("btn-selected", lobby.Mode == GameMode.QuickPlay);
            _weekBtn.EnableInClassList("btn-selected", lobby.Mode == GameMode.Week);
            _start.Show(isHost);
            _ready.Show(!isHost);
            _addBot.Show(isHost && occupied < Protocol.MaxPlayers);
            var readyLabel = _ready.Q<Label>(className: "btn-label");
            if (readyLabel != null) readyLabel.text = mine != null && mine.Ready ? Loc.T("lobby.unready") : Loc.T("lobby.ready");
            _ready.EnableInClassList("btn-primary", mine == null || !mine.Ready);
            _ready.EnableInClassList("btn-secondary", mine != null && mine.Ready);

            _week.Clear();
            if (lobby.Mode == GameMode.Week)
            {
                var rows = new List<WeekBoard.Standing>();
                foreach (var s in lobby.Slots)
                {
                    if (!s.Occupied) continue;
                    int pts = s.Slot < lobby.CareerPoints.Count ? lobby.CareerPoints[s.Slot] : 0;
                    rows.Add(new WeekBoard.Standing { Player = s.Slot, Name = s.Name, Points = pts });
                }
                WeekBoard.Build(_week, lobby.WeekDay, false, lobby.CareerPoints.Count > 0 ? rows : null, false);
            }
        }

        sealed class SlotCard
        {
            readonly int _slot;
            readonly VisualElement _root, _badge, _tags, _stamp, _actions;
            readonly Label _name, _sub, _pts;

            public SlotCard(VisualElement parent, int slot)
            {
                _slot = slot;
                _root = Box("slot").AddTo(parent);
                _badge = Badge(slot, "badge-l").AddTo(_root);
                var col = Box("col", "grow").AddTo(_root);
                _name = Title("", "slot-name").AddTo(col);
                _sub = Text("", "slot-sub").AddTo(col);
                _tags = Box("slot-tags").AddTo(col);
                _pts = Bold("", "slot-pts").AddTo(col);
                var right = Box("col", "center").AddTo(_root);
                _stamp = Box("slot-status").AddTo(right);
                _actions = Box("col").AddTo(right);
            }

            public void Set(LobbySlot s, LobbyView lobby, bool isHost, int mySlot)
            {
                bool occ = s != null && s.Occupied;
                _root.EnableInClassList("slot-empty", !occ);
                _root.EnableInClassList("slot-ready", occ && s.Ready);
                _root.EnableInClassList("slot-me", occ && s.Slot == mySlot);
                _badge.style.opacity = occ ? 1f : 0.35f;
                SetBadge(_badge, _slot, occ ? s.Name : null);
                _name.text = occ ? s.Name : Loc.T("lobby.empty");
                _sub.text = Loc.F("lobby.look", Loc.T("player.default." + _slot));
                _tags.Clear();
                _actions.Clear();
                _stamp.Clear();
                _stamp.style.backgroundImage = StyleKeyword.Null;
                _pts.text = lobby.Mode == GameMode.Week && occ && _slot < lobby.CareerPoints.Count ? Loc.F("week.points", lobby.CareerPoints[_slot]) : "";
                if (!occ)
                {
                    if (isHost && !lobby.MatchRunning)
                        IconBtn("bot", Loc.T("lobby.add_bot_here"), () => ConnectionManager.I.HostController?.AddBot(), "btn-small", "btn-ghost").AddTo(_actions);
                    return;
                }
                if (s.IsHost) Tag(Loc.T("lobby.host_tag"), "tag-teal").AddTo(_tags);
                if (s.Slot == mySlot) Tag(Loc.T("lobby.you_tag"), "tag-sticky").AddTo(_tags);
                if (s.IsBot) Tag(Loc.T("lobby.bot_tag"), "tag-grey").AddTo(_tags);
                if (!s.Connected) Tag(Loc.T("lobby.offline_tag"), "tag-red").AddTo(_tags);
                if (s.Ready)
                {
                    if (!UiArt.Apply(_stamp, "stamps/stamp_onaylandi")) Bold(Loc.T("lobby.is_ready"), "t-good").AddTo(_stamp);
                }
                else Bold(Loc.T("lobby.not_ready"), "t-muted").AddTo(_stamp);
                if (isHost && s.IsBot && !lobby.MatchRunning)
                {
                    int slot = s.Slot;
                    IconBtn("kick", Loc.T("lobby.remove_bot"), () => ConnectionManager.I.HostController?.RemoveBot(slot), "btn-small", "btn-ghost").AddTo(_actions);
                }
            }
        }

        static string _cachedAddresses;
        static string LocalAddresses()
        {
            if (_cachedAddresses != null) return _cachedAddresses;
            try
            {
                var list = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString()).Distinct().ToList();
                _cachedAddresses = list.Count == 0 ? "127.0.0.1" : string.Join(", ", list);
            }
            catch (Exception) { _cachedAddresses = "127.0.0.1"; }
            return _cachedAddresses;
        }
    }

    /// <summary>Connecting / reconnecting / status-key overlay.</summary>
    public sealed class StatusOverlay
    {
        readonly VisualElement _root;
        readonly Label _text;
        string _last;

        public StatusOverlay(VisualElement parent)
        {
            _root = Box("status").NoPick().AddTo(parent);
            var b = Box("status-box").NoPick().AddTo(_root);
            Icon("timer", "icon-m", "gap-m").AddTo(b);
            _text = Title("", "status-text").NoPick().AddTo(b);
            _root.Show(false);
        }

        public void Tick()
        {
            var c = ConnectionManager.I;
            string s = null;
            if (c != null)
            {
                if (c.Current == ConnectionManager.State.Connecting) s = Loc.T("net.connecting");
                else if (c.Current == ConnectionManager.State.Reconnecting) s = Loc.F("net.reconnecting_s", Mathf.CeilToInt(c.ReconnectRemaining));
            }
            if (s == null && ClientState.I != null && !string.IsNullOrEmpty(ClientState.I.StatusKey)) s = Loc.T(ClientState.I.StatusKey);
            _root.Show(s != null);
            if (s != null && s != _last) { _last = s; _text.text = s; }
        }
    }

    /// <summary>Esc menu during a session.</summary>
    public sealed class PauseMenu : UiKit.Screen
    {
        readonly Button _returnLobby;

        public PauseMenu(VisualElement parent) : base(parent, "dim", "center")
        {
            var card = Panel("panel", "pause").AddTo(Root);
            Title(Loc.T("pause.title"), "h1").AddTo(card);
            Btn(Loc.T("pause.resume"), Hide, "btn-primary", "menu-btn").AddTo(card);
            IconBtn("settings", Loc.T("menu.settings"), SettingsScreen.Open, "menu-btn").AddTo(card);
            IconBtn("info", Loc.T("menu.how_to"), HowToPlayScreen.Open, "menu-btn").AddTo(card);
            BriefingScreen.ControlsStrip(card);
            _returnLobby = Btn(Loc.T("pause.return_lobby"), () => ConfirmDialog.Show(Loc.T("pause.return_lobby"), Loc.T("pause.return_lobby_confirm"), Loc.T("pause.return_lobby"),
                () => { ConnectionManager.I.HostController?.ReturnToLobby(); Hide(); }, true), "btn-small", "btn-ghost").AddTo(card);
            var row = Box("row").AddTo(card);
            Btn(Loc.T("pause.leave"), () => ConfirmDialog.Show(Loc.T("pause.leave"), Loc.T("pause.leave_confirm"), Loc.T("pause.leave"), () => { ConnectionManager.I.Leave(); Hide(); }, true), "btn-danger", "grow").AddTo(row);
            Btn(Loc.T("menu.quit"), () => ConfirmDialog.Show(Loc.T("menu.quit"), Loc.T("menu.quit_confirm"), Loc.T("menu.quit"), Application.Quit, true), "btn-danger", "btn-small").AddTo(row);
        }

        public void Toggle() { SetVisible(!Visible); }
        public void Hide() { SetVisible(false); }

        protected override void OnShow()
        {
            var hc = ConnectionManager.I != null ? ConnectionManager.I.HostController : null;
            _returnLobby.Show(hc != null && hc.MatchRunning);
        }
    }
}
