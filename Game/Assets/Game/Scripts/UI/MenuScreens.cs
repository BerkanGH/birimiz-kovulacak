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
    /// <summary>
    /// Main menu on the empty left of <c>backgrounds/menu_bg</c>: logo (per language), tagline, one card with the name,
    /// Host (primary) / Join, then three equal secondary buttons (Settings · How to play · Quit); a TR/EN switch in the
    /// corner; the join page with recent hosts; the first-launch language picker.
    /// </summary>
    public sealed class MainMenuScreen : UiKit.Screen
    {
        const string RecentKey = "join.recent";
        static MainMenuScreen _i;
        readonly TextField _name, _address, _port;
        readonly Button _rejoin;
        readonly Label _error, _joinError;
        readonly VisualElement _col, _logo, _main, _join, _recent, _picker, _tagline;
        bool _joinOpen;

        public bool JoinOpen => _joinOpen;
        public static MainMenuScreen Instance => _i;
        /// <summary>Set by <see cref="UIRoot.Rebuild"/> when the language changed on the join page.</summary>
        public bool OpenJoinOnShow;

        public MainMenuScreen(VisualElement parent) : base(parent, "menu-bg")
        {
            _i = this;
            var layout = Box("menu-layout").AddTo(Root);
            _col = Box("menu-col").AddTo(layout);

            _logo = Box("menu-logo").AddTo(_col);
            BuildLogo(_logo);
            _tagline = Bold(Loc.T("game.tagline"), "menu-tagline").AddTo(_col);

            // ---- main card
            _main = Panel("panel", "menu-card").AddTo(_col);
            _name = new TextField(Loc.T("menu.name")) { value = PlayerPrefs.GetString("player.name", DefaultName()), maxLength = 16 }.Cls("field", "menu-field").AddTo(_main);
            IconBtn("host", Loc.T("menu.host"), Host, "btn-primary", "menu-btn", "menu-btn-main").AddTo(_main);
            IconBtn("contacts", Loc.T("menu.join"), () => ShowJoin(true), "btn-secondary", "menu-btn").AddTo(_main);
            _rejoin = IconBtn("call", Loc.T("menu.rejoin"), Rejoin, "btn-secondary", "menu-btn").AddTo(_main);
            _rejoin.Show(false);
            var row = Box("menu-row").AddTo(_main);
            Btn(Loc.T("menu.settings"), () => SettingsScreen.Open(), "btn-ghost", "menu-small").AddTo(row);
            Btn(Loc.T("menu.how_to"), HowToPlayScreen.Open, "btn-ghost", "menu-small").AddTo(row);
            Btn(Loc.T("menu.quit"), () => ConfirmDialog.Show(Loc.T("menu.quit"), Loc.T("menu.quit_confirm"), Loc.T("menu.quit"), Application.Quit, true), "btn-ghost", "menu-small").AddTo(row);
            _error = Text("", "error-line").AddTo(_main);

            // ---- join card
            _join = Panel("panel", "menu-card").AddTo(_col);
            var jh = Box("row").AddTo(_join);
            Title(Loc.T("join.title"), "h2", "grow").AddTo(jh);
            var jr = Box("row").AddTo(_join);
            _address = new TextField(Loc.T("menu.address")) { value = PlayerPrefs.GetString("join.address", "127.0.0.1") }.Cls("field", "grow", "join-address").AddTo(jr);
            _port = new TextField(Loc.T("menu.port")) { value = PlayerPrefs.GetString("join.port", "7777"), maxLength = 5 }.Cls("field", "join-port").AddTo(jr);
            Text(Loc.T("join.port_help"), "t-tiny", "t-muted").AddTo(_join);
            var ja = Box("row", "mt-s").AddTo(_join);
            Btn(Loc.T("common.back"), () => ShowJoin(false), "btn-ghost").AddTo(ja);
            IconBtn("call", Loc.T("join.connect"), Join, "btn-primary", "grow").AddTo(ja);
            _joinError = Text("", "error-line").AddTo(_join);
            Section(_join, Loc.T("join.recent"), "notes");
            _recent = Box("col").AddTo(_join);
            _join.Show(false);

            // ---- corner: language switch + version line
            var lang = Box("lang-switch").AddTo(Root);
            LangButton(lang, "tr", "TR");
            LangButton(lang, "en", "EN");
            Text(Loc.F("menu.version", GameConfig.BuildId), "version").AddTo(Root);

            // ---- first-launch language picker (over everything on this screen)
            _picker = Box("lang-picker").AddTo(Root);
            BuildPicker(_picker);
            _picker.Show(false);

            _address.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Join(); });
        }

        /// <summary>Logo art per language (logo/logo, logo_en/logo_en); text fallback in the same layout.</summary>
        static void BuildLogo(VisualElement logo)
        {
            bool en = Loc.Language == "en";
            if (UiArt.Apply(logo, en ? "logo_en/logo_en" : "logo/logo")) return;
            logo.AddToClassList("menu-logo-fallback");
            var card = Box("logo-card").AddTo(logo);
            var mark = Box("logo-mark").AddTo(card);
            UiArt.Apply(mark, "logo/logo_mark");
            var col = Box("col", "grow").AddTo(card);
            Title(Loc.T("game.title_1"), "logo-line1").AddTo(col);
            var stamp = Box("logo-stamp").AddTo(col);
            Title(Loc.T("game.title_2"), "logo-line2").AddTo(stamp);
        }

        static void LangButton(VisualElement parent, string lang, string label)
        {
            var b = Btn(label, () => { if (Loc.Language != lang) Loc.SetLanguage(lang); }, "btn-small", "lang-btn").AddTo(parent);
            b.RemoveFromClassList("btn-secondary");
            b.EnableInClassList("lang-btn-on", Loc.Language == lang);
        }

        void BuildPicker(VisualElement layer)
        {
            var card = Panel("panel", "picker-card").AddTo(layer);
            var icon = Box("icon", "icon-xl").AddTo(card);
            MeetingText.ApplyIcon(icon, "icons_v3/language");
            Title("Dil seç  ·  Choose a language", "picker-title").AddTo(card);
            var row = Box("picker-row").AddTo(card);
            PickerOption(row, "tr", "Türkçe", "Oyunu Türkçe oyna");
            PickerOption(row, "en", "English", "Play the game in English");
            Text("Ayarlar'dan her zaman değiştirebilirsin  ·  You can change it any time in Settings", "picker-note").AddTo(card);
        }

        static void PickerOption(VisualElement row, string lang, string name, string sub)
        {
            var b = Btn("", () => Loc.SetLanguage(lang), "btn-secondary", "picker-option").AddTo(row);
            Title(name, "picker-name").NoPick().AddTo(b);
            Text(sub, "picker-sub").NoPick().AddTo(b);
        }

        /// <summary>True on the very first launch (no saved language) of an interactive client.</summary>
        static bool NeedsLanguage()
        {
            if (PlayerPrefs.HasKey(Loc.PrefKey)) return false;
            var a = LaunchArgs.Current;
            return !(a.Bot || a.AutoHost || LineMockDriver.Active || !string.IsNullOrEmpty(a.AutoJoinAddress));
        }

        /// <summary>Opens the language picker (first launch; mocks call it directly).</summary>
        public static void ShowLanguagePicker()
        {
            if (_i == null) return;
            _i._picker.Show(true);
            _i._picker.BringToFront();
            UiMotion.FadeIn(_i._picker, 0.25f);
            UiMotion.PopIn(_i._picker[0], 0.35f, 0.05f, 0.85f);
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
            var card = on ? _join : _main;
            if (on) BuildRecent();
            UiMotion.SlideIn(card, new Vector2(on ? 50f : -50f, 0f), 0.28f);
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
                Icon("contacts", "icon-s").AddTo(r);
                Bold(entry, "recent-host", "grow").AddTo(r);
                Btn(Loc.T("join.use"), () =>
                {
                    int colon = entry.LastIndexOf(':');
                    _address.value = colon > 0 ? entry.Substring(0, colon) : entry;
                    if (colon > 0) _port.value = entry.Substring(colon + 1);
                    Join();
                }, "btn-small", "btn-primary").AddTo(r);
                Btn("×", () => { ForgetHost(entry); BuildRecent(); }, "btn-small", "btn-ghost", "recent-x").AddTo(r);
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

        static void ForgetHost(string entry)
        {
            var list = RecentHosts();
            list.Remove(entry);
            PlayerPrefs.SetString(RecentKey, string.Join("|", list));
            PlayerPrefs.Save();
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
            if (string.IsNullOrWhiteSpace(_address.value)) { _joinError.text = Loc.T("join.need_address"); UiMotion.Shake(_join, 8f); return; }
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

        protected override void OnShow()
        {
            _joinOpen = false;
            _main.Show(true);
            _join.Show(false);
            UiArt.Apply(Root, "backgrounds/menu_bg");
            // entrance: logo drops in and settles, tagline fades, card slides from the left, buttons follow
            UiMotion.Tween(_logo, 0.55f, t =>
            {
                _logo.style.translate = new Translate(0f, Mathf.LerpUnclamped(-90f, 0f, t));
                _logo.style.rotate = new Rotate(new Angle(Mathf.LerpUnclamped(-6f, 0f, t), AngleUnit.Degree));
                _logo.style.opacity = Mathf.Clamp01(t * 3f);
            }, UiMotion.Ease.OutBack, null, 0.05f, "drop");
            _logo.style.opacity = 0f;
            UiMotion.FadeIn(_tagline, 0.35f, 0.35f);
            UiMotion.SlideIn(_main, new Vector2(-70f, 0f), 0.4f, 0.3f);
            float d = 0.42f;
            foreach (var child in _main.Children())
            {
                if (child is Button || child.ClassListContains("menu-row")) { UiMotion.SlideIn(child, new Vector2(-24f, 0f), 0.25f, d); d += 0.05f; }
            }
            if (NeedsLanguage()) ShowLanguagePicker();
            if (OpenJoinOnShow) { OpenJoinOnShow = false; ShowJoin(true); }
        }

        // A hidden field must not keep keyboard focus (feel suppresses Y/N/T while a text field is focused).
        protected override void OnHide()
        {
            _name.Blur(); _address.Blur(); _port.Blur();
            Root.style.backgroundImage = StyleKeyword.Null; // 1920x1080 background is not needed during a match
            UiArt.Release("backgrounds/menu_bg");
        }

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
            if (!string.IsNullOrEmpty(err) || _joinError.text != Loc.T("join.need_address")) _joinError.SetText(err);
        }
    }

    /// <summary>
    /// v3 lobby (<see cref="LineClient.Lobby"/>): 3–6 seats pinned on the cork board (badge, name, tags, ready stamp,
    /// session points); below, the host desk: week length, call recordings with the consent line, add bot, start;
    /// clients get "Ready". How-to and leave in the corner; the host's addresses at the bottom.
    /// </summary>
    public sealed class LobbyScreen : UiKit.Screen
    {
        readonly VisualElement _board, _grid, _desk, _lengthHost, _recHost, _week, _ticket;
        readonly Label _count, _status, _addresses, _lengthClient, _consent, _session;
        readonly Button _ready, _start, _addBot, _short, _full;
        readonly Toggle _rec;
        readonly List<SlotCard> _cards = new List<SlotCard>();
        int _lastHash = int.MinValue;
        float _nextStatus;
        bool _entered;

        public LobbyScreen(VisualElement parent) : base(parent, "menu-bg", "lobby-screen")
        {
            // ---- title note (top left)
            _ticket = Box("lobby-ticket").AddTo(Root);
            Title(Loc.T("lobby.title"), "lobby-title").AddTo(_ticket);
            _count = Bold("", "lobby-count").AddTo(_ticket);
            _session = Text("", "lobby-session").AddTo(_ticket);
            var tr = Box("row", "mt-s").AddTo(_ticket);
            Btn(Loc.T("menu.how_to"), HowToPlayScreen.Open, "btn-small", "btn-ghost").AddTo(tr);
            Btn(Loc.T("lobby.leave"), ConfirmLeave, "btn-small", "btn-ghost").AddTo(tr);

            // ---- the cork board: seats
            _board = Box("lobby-board").AddTo(Root);
            _grid = Box("lobby-grid").AddTo(_board);
            for (int i = 0; i < LineProtocol.MaxPlayers; i++) _cards.Add(new SlotCard(_grid, i));

            // ---- the desk: week settings + primary action
            _desk = Panel("panel", "lobby-desk").AddTo(Root);
            var cols = Box("lobby-desk-cols").AddTo(_desk);
            _week = Box("lobby-week").AddTo(cols);
            Bold(Loc.T("lobby.week_length"), "lobby-label").AddTo(_week);
            _lengthHost = Box("seg-switch").AddTo(_week);
            _short = SegButton(_lengthHost, Loc.T("lobby.short_week"), Loc.T("lobby.short_week_sub"), () => HostLineController.Current?.SetLength(WeekLength.Short));
            _full = SegButton(_lengthHost, Loc.T("lobby.full_week"), Loc.T("lobby.full_week_sub"), () => HostLineController.Current?.SetLength(WeekLength.Full));
            _lengthClient = Title("", "lobby-length-client").AddTo(_week);
            var cfg = LineClient.I != null ? LineClient.I.Config : LineConfig.CreateDefault();
            Text(Loc.F("lobby.start_blame", cfg.StartBlame, cfg.StartBlame + cfg.LeaderStartBonus), "lobby-note").AddTo(_week);
            Bold(Loc.T("lobby.recordings"), "lobby-label", "mt-m").AddTo(_week);
            _recHost = Box("row").AddTo(_week);
            _rec = new Toggle(Loc.T("lobby.recordings_toggle")).Cls("toggle", "lobby-rec-toggle").AddTo(_recHost);
            _rec.RegisterValueChangedCallback(e => HostLineController.Current?.SetRecordings(e.newValue));
            _consent = Text("", "lobby-consent").AddTo(_week);

            var act = Box("lobby-act").AddTo(cols);
            _status = Bold("", "lobby-status").AddTo(act);
            _start = IconBtn("ready", Loc.T("lobby.start_week"), () => HostLineController.Current?.StartWeek(), "btn-primary", "btn-big", "lobby-primary").AddTo(act);
            _ready = IconBtn("ready", Loc.T("lobby.ready"), ToggleReady, "btn-primary", "btn-big", "lobby-primary").AddTo(act);
            _addBot = IconBtn("bot", Loc.T("lobby.add_bot"), () => HostLineController.Current?.AddBot(), "btn-ghost", "btn-small").AddTo(act);
            Text(Loc.T("lobby.voice_hint_v3"), "lobby-voice").AddTo(act);

            _addresses = Text("", "lobby-addresses").AddTo(Root);
        }

        static Button SegButton(VisualElement parent, string title, string sub, Action onClick)
        {
            var b = Btn("", onClick, "btn-secondary", "seg-btn").AddTo(parent);
            Title(title, "seg-title").NoPick().AddTo(b);
            Text(sub, "seg-sub").NoPick().AddTo(b);
            return b;
        }

        static void ConfirmLeave()
        {
            bool host = HostLineController.Current != null;
            ConfirmDialog.Show(Loc.T("lobby.leave"), Loc.T(host ? "lobby.leave_host_confirm" : "lobby.leave_confirm"), Loc.T("lobby.leave"), () => ConnectionManager.I?.Leave(), true);
        }

        void ToggleReady()
        {
            var c = LineClient.I;
            var lobby = c != null ? c.Lobby : null;
            if (lobby == null) return;
            LineLobbySlot mine = null;
            foreach (var s in lobby.Slots) if (s.Slot == c.MySlot) mine = s;
            c.SetReady(mine == null || !mine.Ready);
        }

        protected override void OnShow()
        {
            _lastHash = int.MinValue;
            _entered = false;
            UiArt.Apply(Root, "backgrounds/lobby_bg");
        }

        protected override void OnHide()
        {
            Root.style.backgroundImage = StyleKeyword.Null;
            UiArt.Release("backgrounds/lobby_bg");
        }

        public override void Tick()
        {
            if (!Visible) return;
            var c = LineClient.I;
            var lobby = c != null ? c.Lobby : null;
            var hc = HostLineController.Current;
            bool isHost = hc != null;
            if (lobby == null)
            {
                _status.SetText(Loc.T("lobby.waiting"));
                return;
            }
            int hash = 17;
            foreach (var s in lobby.Slots)
                hash = hash * 31 + (s.Occupied ? 1 : 0) + (s.Ready ? 2 : 0) + (s.Connected ? 4 : 0) + (s.IsBot ? 8 : 0) + (s.IsHost ? 16 : 0) + s.SessionPoints * 64 + (s.Name ?? "").GetHashCode();
            hash = hash * 31 + (int)lobby.Length * 7 + (lobby.RecordingsEnabled ? 3 : 0) + lobby.WeeksPlayed * 131 + c.MySlot * 17 + (isHost ? 1 : 0) + (lobby.WeekRunning ? 5 : 0);
            if (hash != _lastHash)
            {
                _lastHash = hash;
                Rebuild(lobby, isHost, c.MySlot);
                if (!_entered) { _entered = true; Enter(); }
            }
            if (Time.unscaledTime >= _nextStatus)
            {
                _nextStatus = Time.unscaledTime + 0.25f;
                string reason = lobby.StatusKey;
                bool can = isHost && hc.CanStart(out reason);
                _start.SetEnabled(can);
                _status.SetText(Loc.T(string.IsNullOrEmpty(reason) ? (can ? "lobby.ready_to_start" : "lobby.waiting") : reason));
                _status.EnableInClassList("lobby-status-go", can);
                var cm = ConnectionManager.I;
                _addresses.SetText(isHost ? Loc.F("lobby.host_addresses", LocalAddresses(), cm != null ? cm.Port : (ushort)7777) : (cm != null && cm.Current == ConnectionManager.State.Connected ? Loc.F("lobby.connected_to", cm.Address) : ""));
                _addresses.Show(!string.IsNullOrEmpty(_addresses.text));
                if (isHost && LaunchArgs.Current.AutoStart && can) hc.StartWeek();
            }
        }

        void Enter()
        {
            UiMotion.SlideIn(_ticket, new Vector2(-60f, 0f), 0.35f);
            UiMotion.SlideIn(_desk, new Vector2(0f, 80f), 0.4f, 0.15f);
            float d = 0.1f;
            foreach (var card in _cards) { card.Pin(d); d += 0.07f; }
        }

        void Rebuild(LineLobbyView lobby, bool isHost, int mySlot)
        {
            int occupied = 0, ready = 0;
            LineLobbySlot mine = null;
            for (int i = 0; i < _cards.Count; i++)
            {
                LineLobbySlot s = null;
                foreach (var x in lobby.Slots) if (x.Slot == i) s = x;
                _cards[i].Set(s, lobby, isHost, mySlot);
                if (s != null && s.Occupied) { occupied++; if (s.Ready || s.IsHost) ready++; }
                if (s != null && s.Slot == mySlot) mine = s;
            }
            _count.text = Loc.F("lobby.count", occupied, LineProtocol.MaxPlayers);
            _session.text = lobby.WeeksPlayed > 0 ? Loc.F("lobby.session_weeks", lobby.WeeksPlayed) : Loc.T("lobby.session_new");
            _lengthHost.Show(isHost);
            _lengthClient.Show(!isHost);
            _lengthClient.text = Loc.T(lobby.Length == WeekLength.Full ? "lobby.full_week" : "lobby.short_week") + " · " + Loc.T(lobby.Length == WeekLength.Full ? "lobby.full_week_sub" : "lobby.short_week_sub");
            _short.EnableInClassList("seg-on", lobby.Length == WeekLength.Short);
            _full.EnableInClassList("seg-on", lobby.Length == WeekLength.Full);
            _recHost.Show(isHost);
            _rec.SetValueWithoutNotify(lobby.RecordingsEnabled);
            _consent.text = Loc.T(lobby.RecordingsEnabled ? "lobby.recordings_consent" : "lobby.recordings_off");
            _consent.EnableInClassList("lobby-consent-off", !lobby.RecordingsEnabled);
            _start.Show(isHost);
            _ready.Show(!isHost);
            _addBot.Show(isHost && occupied < LineProtocol.MaxPlayers && !lobby.WeekRunning);
            var readyLabel = _ready.Q<Label>(className: "btn-label");
            bool imReady = mine != null && mine.Ready;
            if (readyLabel != null) readyLabel.text = imReady ? Loc.T("lobby.unready") : Loc.T("lobby.ready");
            _ready.EnableInClassList("btn-primary", !imReady);
            _ready.EnableInClassList("btn-secondary", imReady);
        }

        sealed class SlotCard
        {
            readonly int _slot;
            readonly VisualElement _root, _badge, _tags, _stamp, _actions, _pin;
            readonly Label _name, _sub, _pts;
            readonly float _tilt;

            public SlotCard(VisualElement parent, int slot)
            {
                _slot = slot;
                _tilt = ((slot * 37) % 5 - 2) * 0.9f;
                _root = Box("slot").AddTo(parent);
                _root.style.rotate = new Rotate(new Angle(_tilt, AngleUnit.Degree));
                _pin = Box("slot-pin").NoPick().AddTo(_root);
                _pin.style.backgroundColor = PlayerColor(slot);
                var top = Box("slot-top").AddTo(_root);
                _badge = Badge(slot, "badge-l").AddTo(top);
                var col = Box("col", "grow").AddTo(top);
                _name = Title("", "slot-name").AddTo(col);
                _sub = Text("", "slot-sub").AddTo(col);
                _tags = Box("slot-tags").AddTo(col);
                Box("spacer").AddTo(_root);
                var bottom = Box("slot-bottom").AddTo(_root);
                _pts = Bold("", "slot-pts").AddTo(bottom);
                Box("spacer").AddTo(bottom);
                _actions = Box("row").AddTo(bottom);
                _stamp = Box("slot-stamp").NoPick().AddTo(_root);
            }

            public void Pin(float delay)
            {
                UiMotion.Tween(_root, 0.32f, t =>
                {
                    _root.style.translate = new Translate(0f, Mathf.LerpUnclamped(-40f, 0f, t));
                    _root.style.opacity = Mathf.Clamp01(t * 2.5f);
                }, UiMotion.Ease.OutBack, null, delay, "pin");
                _root.style.opacity = 0f;
            }

            public void Set(LineLobbySlot s, LineLobbyView lobby, bool isHost, int mySlot)
            {
                bool occ = s != null && s.Occupied;
                bool wasReady = _root.ClassListContains("slot-ready");
                _root.EnableInClassList("slot-empty", !occ);
                _root.EnableInClassList("slot-ready", occ && s.Ready);
                _root.EnableInClassList("slot-me", occ && s.Slot == mySlot);
                _badge.style.opacity = occ ? 1f : 0.3f;
                SetBadge(_badge, _slot, occ ? s.Name : null);
                _name.text = occ ? s.Name : Loc.T("lobby.empty");
                _sub.text = Loc.F("lobby.look", Loc.T("player.default." + _slot));
                _tags.Clear();
                _actions.Clear();
                var oldRemove = _root.Q(className: "slot-remove");
                oldRemove?.RemoveFromHierarchy();
                _stamp.Clear();
                _stamp.RemoveFromClassList("stamp-text");
                _stamp.style.backgroundImage = StyleKeyword.Null;
                _pts.text = occ && lobby.WeeksPlayed > 0 ? Loc.F("lobby.points", s.SessionPoints) : "";
                if (!occ)
                {
                    if (isHost && !lobby.WeekRunning)
                        IconBtn("bot", Loc.T("lobby.add_bot_here"), () => HostLineController.Current?.AddBot(), "btn-small", "btn-ghost").AddTo(_actions);
                    return;
                }
                if (s.IsHost) Tag(Loc.T("lobby.host_tag"), "tag-teal").AddTo(_tags);
                if (s.Slot == mySlot) Tag(Loc.T("lobby.you_tag"), "tag-sticky").AddTo(_tags);
                if (s.IsBot) Tag(Loc.T("lobby.bot_tag"), "tag-grey").AddTo(_tags);
                if (!s.Connected) Tag(Loc.T("lobby.offline_tag"), "tag-red").AddTo(_tags);
                if (s.Ready || s.IsHost)
                {
                    if (!MeetingText.ApplyStamp(_stamp, "stamps/stamp_onaylandi", "stamps_v3/stamp_approved"))
                    {
                        _stamp.AddToClassList("stamp-text");
                        StampText(Loc.T("lobby.is_ready"), "stamp-text-label").NoPick().AddTo(_stamp);
                    }
                    if (!wasReady && s.Ready) UiMotion.Stamp(_stamp, -8f, 0.2f);
                    else _stamp.style.opacity = 1f;
                }
                else
                {
                    _stamp.style.opacity = 1f;
                    Bold(Loc.T("lobby.not_ready"), "slot-waiting").NoPick().AddTo(_stamp);
                }
                if (isHost && s.IsBot && !lobby.WeekRunning)
                {
                    int slot = s.Slot;
                    Btn("×", () => HostLineController.Current?.RemoveBot(slot), "btn-small", "btn-ghost", "slot-remove").AddTo(_root);
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
        int _lastSec = -1;
        string _reconnecting;

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
                else if (c.Current == ConnectionManager.State.Reconnecting)
                {
                    int sec = Mathf.CeilToInt(c.ReconnectRemaining);
                    if (sec != _lastSec || _reconnecting == null) { _lastSec = sec; _reconnecting = Loc.F("net.reconnecting_s", sec); }
                    s = _reconnecting;
                }
            }
            var lc = LineClient.I;
            if (s == null && lc != null && !string.IsNullOrEmpty(lc.StatusKey)) s = Loc.T(lc.StatusKey);
            if (s == null && ClientState.I != null && !string.IsNullOrEmpty(ClientState.I.StatusKey)) s = Loc.T(ClientState.I.StatusKey);
            bool show = s != null;
            if (show != _root.IsShown()) { _root.Show(show); if (show) UiMotion.PopIn(_root[0], 0.25f); }
            if (s != null && s != _last) { _last = s; _text.text = s; }
        }
    }

    /// <summary>Esc menu during a session: resume, settings, how to play, the v3 controls, back to lobby (host), leave, quit.</summary>
    public sealed class PauseMenu : UiKit.Screen
    {
        readonly VisualElement _card;
        readonly Button _returnLobby;

        public PauseMenu(VisualElement parent) : base(parent, "dim", "center")
        {
            _i = this;
            _card = Panel("panel", "pause").AddTo(Root);
            var head = Box("row").AddTo(_card);
            Title(Loc.T("pause.title"), "h1", "grow").AddTo(head);
            Box("pause-mug").AddTo(head);
            Btn(Loc.T("pause.resume"), Hide, "btn-primary", "menu-btn").AddTo(_card);
            var row = Box("menu-row").AddTo(_card);
            Btn(Loc.T("menu.settings"), () => SettingsScreen.Open(), "btn-ghost", "menu-small").AddTo(row);
            Btn(Loc.T("menu.how_to"), HowToPlayScreen.Open, "btn-ghost", "menu-small").AddTo(row);
            ControlsStrip(_card);
            _returnLobby = Btn(Loc.T("pause.return_lobby"), () => ConfirmDialog.Show(Loc.T("pause.return_lobby"), Loc.T("pause.return_lobby_confirm"), Loc.T("pause.return_lobby"),
                () => { HostLineController.Current?.ReturnToLobby(); Hide(); }, true), "btn-ghost", "menu-btn").AddTo(_card);
            var bottom = Box("menu-row").AddTo(_card);
            Btn(Loc.T("pause.leave"), () => ConfirmDialog.Show(Loc.T("pause.leave"), Loc.T("pause.leave_confirm"), Loc.T("pause.leave"), () => { ConnectionManager.I?.Leave(); Hide(); }, true), "btn-ghost", "menu-small").AddTo(bottom);
            Btn(Loc.T("menu.quit"), () => ConfirmDialog.Show(Loc.T("menu.quit"), Loc.T("menu.quit_confirm"), Loc.T("menu.quit"), Application.Quit, true), "btn-ghost", "menu-small").AddTo(bottom);
        }

        /// <summary>v3 controls: mouse look, click, Tab phone, V talk, Esc.</summary>
        public static void ControlsStrip(VisualElement parent)
        {
            var s = Box("controls-strip", "controls-v3").AddTo(parent);
            Ctl(s, Loc.T("ctl.mouse_key"), "ctl.v3.look");
            Ctl(s, Loc.T("ctl.click_key"), "ctl.v3.click");
            Ctl(s, GameInput.KeyLabel(GameInput.Phone), "ctl.v3.phone");
            Ctl(s, GameInput.KeyLabel(GameInput.PushToTalk), "ctl.v3.talk");
            Ctl(s, "Esc", "ctl.v3.menu");
        }

        static void Ctl(VisualElement parent, string key, string labelKey)
        {
            var c = Box("ctl").AddTo(parent);
            KeyCap(key).AddTo(c);
            Text(Loc.T(labelKey), "ctl-text").AddTo(c);
        }

        public void Toggle() { SetVisible(!Visible); }
        public void Hide() { SetVisible(false); }

        static PauseMenu _i;
        /// <summary>Mocks / tools: open the pause menu without Esc.</summary>
        public static void Open() { if (_i != null) _i.SetVisible(true); }
        public static void Close() { if (_i != null) _i.SetVisible(false); }

        protected override void OnShow()
        {
            var lc = LineClient.I;
            _returnLobby.Show(HostLineController.Current != null && lc != null && lc.InWeek);
            UiMotion.PopIn(_card, 0.26f, 0f, 0.92f);
        }
    }
}
