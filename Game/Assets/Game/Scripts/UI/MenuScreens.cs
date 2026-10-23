using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    public sealed class MainMenuScreen : UiKit.Screen
    {
        readonly TextField _name, _address, _port;
        readonly Button _rejoin;
        readonly Label _error;
        readonly VisualElement _help;

        public MainMenuScreen(VisualElement parent) : base(parent, "dim", "center")
        {
            var col = Box("col", "center").AddTo(Root);
            Title(Loc.T("game.title"), "title-logo").AddTo(col);
            Bold(Loc.T("game.tagline"), "tagline").AddTo(col);

            var card = Box("card").AddTo(col);
            card.style.width = 560;
            _name = new TextField(Loc.T("menu.name")) { value = PlayerPrefs.GetString("player.name", DefaultName()), maxLength = 16 }.Cls("field").AddTo(card);
            Btn(Loc.T("menu.host"), Host, "btn-primary").AddTo(card);
            Text(Loc.T("menu.or_join"), "muted").AddTo(card);
            var row = Box("row").AddTo(card);
            _address = new TextField(Loc.T("menu.address")) { value = PlayerPrefs.GetString("join.address", "127.0.0.1") }.Cls("field", "grow").AddTo(row);
            _port = new TextField(Loc.T("menu.port")) { value = PlayerPrefs.GetString("join.port", "7777"), maxLength = 5 }.Cls("field").AddTo(row);
            _port.style.width = 190;
            Btn(Loc.T("menu.join"), Join).AddTo(card);
            _rejoin = Btn(Loc.T("menu.rejoin"), Rejoin, "btn-primary").AddTo(card);
            _rejoin.Show(false);
            _error = Text("", "warn").AddTo(card);
            var bottom = Box("row").AddTo(card);
            Btn(Loc.T("menu.how_to"), () => _help.Show(!_help.IsShown()), "btn-small").AddTo(bottom);
            Btn(Loc.T("menu.settings"), () => PauseMenu.OpenSettings(), "btn-small").AddTo(bottom);
            Btn(Loc.T("menu.quit"), Application.Quit, "btn-small", "btn-danger").AddTo(bottom);
            Text(Loc.T("menu.discord_hint"), "muted", "small").AddTo(card);

            _help = Box("card").AddTo(col);
            _help.style.width = 760;
            _help.style.marginTop = 10;
            Bold(Loc.T("help.title"), "h2").AddTo(_help);
            Text(Loc.T("help.body"), "label-wrap").AddTo(_help);
            _help.Show(false);

            var ver = Text(Loc.F("menu.version", GameConfig.BuildId), "muted", "small").AddTo(Root);
            ver.style.position = Position.Absolute; ver.style.right = 10; ver.style.bottom = 6;
        }

        static string DefaultName()
        {
            string n = Environment.UserName;
            return string.IsNullOrEmpty(n) ? "Çalışan" : (n.Length > 16 ? n.Substring(0, 16) : n);
        }

        void Save()
        {
            PlayerPrefs.SetString("player.name", _name.value);
            PlayerPrefs.SetString("join.address", _address.value);
            PlayerPrefs.SetString("join.port", _port.value);
            PlayerPrefs.Save();
        }

        ushort Port() { ushort p; return ushort.TryParse(_port.value, out p) && p > 0 ? p : (ushort)7777; }

        void Host() { Save(); ConnectionManager.I.StartHost(_name.value, Port()); }
        void Join() { Save(); ConnectionManager.I.StartClient(_name.value, _address.value, Port()); }

        void Rejoin()
        {
            string token, address; ushort port;
            if (ConnectionManager.TryGetRecentSession(out token, out address, out port))
                ConnectionManager.I.StartClient(_name.value, address, port, token);
        }

        public override void Tick()
        {
            if (!Visible) return;
            var c = ConnectionManager.I;
            string tk, ad; ushort po;
            _rejoin.Show(ConnectionManager.TryGetRecentSession(out tk, out ad, out po));
            _error.text = c != null && c.Current == ConnectionManager.State.Failed && !string.IsNullOrEmpty(c.LastErrorKey) ? Loc.T(c.LastErrorKey) : "";
        }
    }

    public sealed class LobbyScreen : UiKit.Screen
    {
        readonly VisualElement _slots, _hostTools;
        readonly Label _status, _addresses, _title;
        readonly Button _ready, _start;
        int _lastHash;

        public LobbyScreen(VisualElement parent) : base(parent, "dim", "center")
        {
            var card = Box("card").AddTo(Root);
            card.style.width = 1080;
            _title = Title(Loc.T("lobby.title"), "h1").AddTo(card);
            _addresses = Text("", "muted").AddTo(card);
            _slots = Box("row", "wrap").AddTo(card);
            _status = Bold("", "h3").AddTo(card);
            var row = Box("row").AddTo(card);
            _ready = Btn(Loc.T("lobby.ready"), ToggleReady, "btn-primary").AddTo(row);
            _start = Btn(Loc.T("lobby.start"), () => ConnectionManager.I.HostController?.StartMatch(), "btn-primary").AddTo(row);
            Btn(Loc.T("lobby.leave"), () => ConnectionManager.I.Leave(), "btn-danger").AddTo(row);
            _hostTools = Box("row").AddTo(card);
            Text(Loc.T("lobby.fill_bots"), "muted").AddTo(_hostTools);
            for (int n = 0; n <= 3; n++)
            {
                int k = n;
                Btn(n.ToString(), () => ConnectionManager.I.HostController?.SetFillBots(k), "btn-small").AddTo(_hostTools);
            }
            Text(Loc.T("lobby.voice_hint"), "muted", "small").AddTo(card);
        }

        void ToggleReady()
        {
            var lobby = ClientState.I.Lobby;
            if (lobby == null || NetSession.Instance == null) return;
            var mine = lobby.Slots.FirstOrDefault(s => s.Slot == ClientState.I.MySlot);
            NetSession.Instance.SetReady(mine == null || !mine.Ready);
        }

        public override void Tick()
        {
            if (!Visible) return;
            var lobby = ClientState.I.Lobby;
            bool isHost = ConnectionManager.I.HostController != null;
            _hostTools.Show(isHost);
            _start.Show(isHost);
            _ready.Show(!isHost);
            if (lobby == null)
            {
                _status.text = Loc.T("lobby.waiting");
                return;
            }
            int hash = lobby.Slots.Aggregate(17, (h, s) => h * 31 + (s.Occupied ? 1 : 0) + (s.Ready ? 2 : 0) + (s.Connected ? 4 : 0) + (s.Name ?? "").GetHashCode()) ^ ClientState.I.MySlot;
            if (hash != _lastHash)
            {
                _lastHash = hash;
                _slots.Clear();
                foreach (var s in lobby.Slots)
                {
                    var c = Box("slot-card").AddTo(_slots);
                    if (s.Ready) c.AddToClassList("ready");
                    var top = Box("row").AddTo(c);
                    Chip(s.Slot, true).AddTo(top);
                    Bold(s.Occupied ? s.Name : Loc.T("lobby.empty"), "slot-name").AddTo(top);
                    Text(Loc.T("player.default." + s.Slot), "muted").AddTo(c);
                    string tags = (s.IsHost ? Loc.T("lobby.host_tag") + " " : "") + (s.Slot == ClientState.I.MySlot ? Loc.T("lobby.you_tag") : "");
                    Text(tags, "accent", "small").AddTo(c);
                    Bold(!s.Occupied ? "" : (s.Ready ? Loc.T("lobby.is_ready") : Loc.T("lobby.not_ready")), s.Ready ? "good" : "muted").AddTo(c);
                }
            }
            var mine = lobby.Slots.FirstOrDefault(s => s.Slot == ClientState.I.MySlot);
            _ready.text = mine != null && mine.Ready ? Loc.T("lobby.unready") : Loc.T("lobby.ready");
            string reason;
            bool can = isHost && ConnectionManager.I.HostController.CanStart(out reason);
            _start.SetEnabled(can);
            _status.text = Loc.T(string.IsNullOrEmpty(lobby.StatusKey) ? "lobby.waiting" : lobby.StatusKey);
            _addresses.text = isHost ? Loc.F("lobby.host_addresses", LocalAddresses(), ConnectionManager.I.Port) : Loc.F("lobby.connected_to", ConnectionManager.I.Address);
            if (isHost && LaunchArgs.Current.AutoStart && can) ConnectionManager.I.HostController.StartMatch();
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

    public sealed class StatusOverlay
    {
        readonly VisualElement _root;
        readonly Label _text;

        public StatusOverlay(VisualElement parent)
        {
            _root = Box("banner").AddTo(parent);
            _root.style.top = new Length(45, LengthUnit.Percent);
            var b = Box("card-dark").AddTo(_root);
            _text = Bold("", "h2", "light").AddTo(b);
            _root.Show(false);
            _root.pickingMode = PickingMode.Ignore;
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
            var v = ClientState.I != null ? ClientState.I.View : null;
            if (s == null && !string.IsNullOrEmpty(ClientState.I?.StatusKey)) s = Loc.T(ClientState.I.StatusKey);
            _root.Show(s != null);
            if (s != null) _text.text = s;
        }
    }

    public sealed class PauseMenu : UiKit.Screen
    {
        static PauseMenu _i;
        readonly VisualElement _main, _settings, _bindings;

        public PauseMenu(VisualElement parent) : base(parent, "dim", "center")
        {
            _i = this;
            var card = Box("card").AddTo(Root);
            card.style.width = 640;
            _main = Box("col").AddTo(card);
            Title(Loc.T("pause.title"), "h1").AddTo(_main);
            Btn(Loc.T("pause.resume"), Hide, "btn-primary").AddTo(_main);
            Btn(Loc.T("menu.settings"), () => ShowSettings(true)).AddTo(_main);
            Bold(Loc.T("help.title"), "h3").AddTo(_main);
            Text(Loc.T("help.controls"), "small", "label-wrap").AddTo(_main);
            Btn(Loc.T("pause.return_lobby"), () => { ConnectionManager.I.HostController?.ReturnToLobby(); Hide(); }, "btn-small").AddTo(_main).name = "returnLobby";
            Btn(Loc.T("pause.leave"), () => { ConnectionManager.I.Leave(); Hide(); }, "btn-danger").AddTo(_main);
            Btn(Loc.T("menu.quit"), Application.Quit, "btn-danger", "btn-small").AddTo(_main);

            _settings = Box("col").AddTo(card);
            Title(Loc.T("menu.settings"), "h1").AddTo(_settings);
            var vol = new Slider(Loc.T("settings.volume"), 0f, 1f) { value = Sfx.Volume }.AddTo(_settings);
            vol.RegisterValueChangedCallback(e => Sfx.Volume = e.newValue);
            var sens = new Slider(Loc.T("settings.sensitivity"), 0.03f, 0.4f) { value = PlayerPrefs.GetFloat("settings.sensitivity", 0.12f) }.AddTo(_settings);
            sens.RegisterValueChangedCallback(e => { PlayerPrefs.SetFloat("settings.sensitivity", e.newValue); if (CameraRig.I != null) CameraRig.I.Sensitivity = e.newValue; });
            var fs = new Toggle(Loc.T("settings.fullscreen")) { value = UnityEngine.Screen.fullScreen }.AddTo(_settings);
            fs.RegisterValueChangedCallback(e => UnityEngine.Screen.fullScreen = e.newValue);
            Bold(Loc.T("settings.bindings"), "h3").AddTo(_settings);
            _bindings = Box("col").AddTo(_settings);
            var row = Box("row").AddTo(_settings);
            Btn(Loc.T("settings.reset_bindings"), () => { GameInput.ResetBindings(); RebuildBindings(); }, "btn-small").AddTo(row);
            Btn(Loc.T("common.back"), () => ShowSettings(false), "btn-primary").AddTo(row);
            ShowSettings(false);
        }

        public static void OpenSettings()
        {
            if (_i == null) return;
            _i.SetVisible(true);
            _i.ShowSettings(true);
        }

        void ShowSettings(bool on)
        {
            _main.Show(!on);
            _settings.Show(on);
            if (on) RebuildBindings();
        }

        void RebuildBindings()
        {
            _bindings.Clear();
            foreach (var (action, index, label) in GameInput.Rebindable)
            {
                var r = Box("list-row").AddTo(_bindings);
                Text(Loc.T(label), "list-row-text").AddTo(r);
                Button b = null;
                b = Btn(GameInput.DisplayName(action, index), () =>
                {
                    b.text = Loc.T("settings.press_key");
                    GameInput.StartRebind(action, index, RebuildBindings);
                }, "btn-small").AddTo(r);
            }
        }

        public void Toggle()
        {
            SetVisible(!Visible);
            if (Visible) ShowSettings(false);
        }

        public void Hide() { SetVisible(false); }

        public override void Tick()
        {
            if (!Visible) return;
            var rl = _main.Q<Button>("returnLobby");
            if (rl != null) rl.Show(ConnectionManager.I.HostController != null && ConnectionManager.I.HostController.MatchRunning);
        }
    }
}
