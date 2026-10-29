using System;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>One computer app: built into <see cref="Root"/>, refreshed when the view changes.</summary>
    public abstract class DeskAppView
    {
        protected readonly DeskUi Ui;
        public readonly VisualElement Root;
        protected DeskAppView(DeskUi ui, string cls) { Ui = ui; Root = Box("dk-app", cls); }
        public abstract DeskApp Id { get; }
        public abstract string Icon { get; }
        public string Title => Dk.T("desk.app." + Id);
        public virtual string Subtitle(LineView v) { return ""; }
        /// <summary>Rebuild if the relevant sub-state changed (or <paramref name="force"/>).</summary>
        public abstract void Refresh(LineView v, bool force);
        public virtual void Tick(LineView v) { }
        public virtual bool Back() { return false; }
        public virtual void Digit(int d) { }
        /// <summary>Desktop icon badge (0 = none).</summary>
        public virtual int Badge(LineView v) { return 0; }
        public virtual void OnShown() { }
    }

    /// <summary>
    /// The office computer ("KOVA OS"): monitor bezel, wallpaper, desktop app icons (F1–F5), one app window at a time,
    /// taskbar with the phase clock, and the coffee / offline overlays while the computer is blocked.
    /// </summary>
    public sealed class DeskComputer
    {
        readonly DeskUi _ui;
        readonly VisualElement _user;
        int _userId = int.MinValue;
        readonly VisualElement _layer, _monitor, _screen, _icons, _window, _winIcon, _winBody, _blocked, _blockArt, _net;
        readonly Label _winTitle, _winSub, _tbClock, _tbPhase, _blockTitle, _blockSub, _blockTimer;
        readonly DeskAppView[] _apps;
        readonly VisualElement[] _iconTiles;
        readonly Label[] _iconBadges;
        readonly int[] _badgeVals = { -1, -1, -1, -1, -1 };
        internal readonly DeskTaskApp Task;
        internal readonly DeskMailApp Mail;
        internal readonly DeskRecordApp Record;
        internal readonly DeskTrickApp Trick;
        internal readonly DeskAgendaApp Agenda;
        DeskApp _app = DeskApp.Task;
        bool _open, _refresh;
        int _lastSec = -1, _blockSig = -1, _lastBlockSec = -1, _subSig = int.MinValue;

        public bool IsOpen => _open;
        public DeskApp App => _app;

        public DeskComputer(DeskUi ui, VisualElement layer)
        {
            _ui = ui;
            _layer = layer;
            _monitor = Box("dk-monitor").AddTo(layer);
            _screen = Box("dk-screen").AddTo(_monitor);
            Dk.Art(_screen, "computer/os_wallpaper");
            var watermark = Box("dk-watermark").NoPick().AddTo(_screen);
            Dk.Art(watermark, "computer/boot_logo");

            var desk = Box("dk-desktop").AddTo(_screen);
            _icons = Box("dk-desk-icons").AddTo(desk);
            Task = new DeskTaskApp(ui);
            Mail = new DeskMailApp(ui);
            Record = new DeskRecordApp(ui);
            Trick = new DeskTrickApp(ui);
            Agenda = new DeskAgendaApp(ui);
            _apps = new DeskAppView[] { Task, Mail, Record, Trick, Agenda };
            _iconTiles = new VisualElement[_apps.Length];
            _iconBadges = new Label[_apps.Length];
            for (int i = 0; i < _apps.Length; i++)
            {
                var app = _apps[i].Id;
                var tile = new Button(() => { Sfx.Play("click"); _ui.OpenComputer(app); }).Cls("dk-desk-icon");
                tile.RemoveFromClassList(Button.ussClassName);
                Dk.Icon(_apps[i].Icon, "dk-desk-icon-img").AddTo(tile);
                Text(_apps[i].Title, "dk-desk-icon-label").NoPick().AddTo(tile);
                Text("F" + (i + 1), "dk-desk-icon-key").NoPick().AddTo(tile);
                _iconBadges[i] = Bold("", "dk-desk-icon-badge").NoPick().AddTo(tile);
                _iconBadges[i].Show(false);
                tile.AddTo(_icons);
                _iconTiles[i] = tile;
            }

            _window = Box("dk-window").AddTo(desk);
            Dk.Skin(_window, "computer/window_frame");
            var titleRow = Box("dk-window-title").NoPick().AddTo(_window);
            _winIcon = Dk.Icon("task_app", "dk-window-icon").AddTo(titleRow);
            _winTitle = Title("", "dk-window-title-text").NoPick().AddTo(titleRow);
            Box("spacer").NoPick().AddTo(titleRow);
            _winSub = Bold("", "dk-window-sub").NoPick().AddTo(titleRow);
            _winBody = Box("dk-window-body").AddTo(_window);
            for (int i = 0; i < _apps.Length; i++) { _apps[i].Root.AddTo(_winBody); _apps[i].Root.Show(false); }

            _blocked = Box("dk-blocked").AddTo(_screen);
            _blockArt = Box("dk-blocked-art").NoPick().AddTo(_blocked);
            var slip = Box("dk-blocked-slip").NoPick().AddTo(_blocked);
            Dk.Skin(slip, "computer/notification_card");
            _blockTitle = Title("", "dk-blocked-title").NoPick().AddTo(slip);
            _blockSub = Text("", "dk-blocked-sub").NoPick().AddTo(slip);
            _blockTimer = MemoBold("", "dk-blocked-timer").NoPick().AddTo(slip);
            _blocked.Show(false);

            // taskbar
            var bar = Box("dk-taskbar").AddTo(_screen);
            Dk.Skin(bar, "computer/taskbar");
            var start = Box("dk-tb-start").NoPick().AddTo(bar);
            var logo = Box("dk-tb-logo").NoPick().AddTo(start);
            if (!Dk.Art(logo, "computer/boot_logo")) logo.AddToClassList("dk-tb-logo-fallback");
            StampText(Dk.T("desk.os.brand"), "dk-tb-brand").NoPick().AddTo(start);
            _user = Box("dk-tb-user").NoPick().AddTo(bar);
            Box("spacer").NoPick().AddTo(bar);
            _net = Dk.Icon("wifi_off", "dk-tb-net").AddTo(bar);
            _net.Show(false);
            var clock = Box("dk-tb-clock").NoPick().AddTo(bar);
            _tbPhase = Text("", "dk-tb-clock-sub").NoPick().AddTo(clock);
            _tbClock = MemoBold("", "dk-tb-clock-digits").NoPick().AddTo(clock);
            var leave = Box("dk-tb-leave").AddTo(bar);
            Btn(Dk.T("desk.os.leave"), () => _ui.CloseComputer(), "btn-ghost", "btn-small", "dk-tb-leave-btn").AddTo(leave);
            Dk.Key(Dk.KeyLabel(GameInput.Pause), "dk-key-s").AddTo(leave);

            _layer.Show(false);
        }

        // ------------------------------------------------------------------ open / close

        public void Open(DeskApp app)
        {
            var v = Dk.View;
            bool was = _open;
            _open = true;
            _layer.Show(true);
            _layer.pickingMode = PickingMode.Position;
            if (Dk.Me != _userId)
            {
                _userId = Dk.Me;
                _user.Clear();
                Dk.Badge(_userId, "badge-xs").AddTo(_user);
                Bold(Dk.Name(_userId), "dk-tb-user-name").NoPick().AddTo(_user);
            }
            if (!was)
            {
                UiMotion.Tween(_monitor, 0.28f, t =>
                {
                    float s = Mathf.Lerp(0.94f, 1f, t);
                    _monitor.style.scale = new Scale(new Vector3(s, s, 1f));
                    _monitor.style.opacity = Mathf.Clamp01(t * 2f);
                }, UiMotion.Ease.OutCubic, () => _monitor.style.scale = StyleKeyword.Null, 0f, "scale");
                _lastSec = -1; _blockSig = -1; _subSig = int.MinValue;
                for (int i = 0; i < _badgeVals.Length; i++) _badgeVals[i] = -1;
                _ui.Cards.OnComputerOpened();
            }
            SetApp(app, !was || app != _app);
            if (v != null) Tick(v, true);
        }

        public void SetApp(DeskApp app) { SetApp(app, app != _app); }

        void SetApp(DeskApp app, bool animate)
        {
            _app = app;
            if (!_open) return;
            var v = Dk.View;
            for (int i = 0; i < _apps.Length; i++)
            {
                bool on = _apps[i].Id == app;
                _apps[i].Root.Show(on);
                _iconTiles[i].EnableInClassList("dk-desk-icon-on", on);
            }
            var a = _apps[(int)app];
            Dk.SetIcon(_winIcon, a.Icon);
            _winTitle.SetText(a.Title);
            _subSig = int.MinValue;
            if (v != null) a.Refresh(v, true);
            a.OnShown();
            if (animate)
            {
                UiMotion.SlideIn(_window, new Vector2(0, 26f), 0.26f, 0f, UiMotion.Ease.OutCubic);
                if (app == DeskApp.Mail) _ui.State.ObOpenedComputer = true;
            }
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            _layer.Show(false);
            _layer.pickingMode = PickingMode.Ignore;
        }

        public bool Back()
        {
            if (!_open) return false;
            return _apps[(int)_app].Back();
        }

        public void Digit(int d) { if (_open && !Dk.ComputerBlocked(Dk.View)) _apps[(int)_app].Digit(d); }
        public void OnSubmitted(int actor) { Task.OnSubmitted(actor); }
        public void Refresh() { _refresh = true; }

        public int UnreadCount(LineView v) { return Mail.Unread(v); }

        // ------------------------------------------------------------------ tick

        public void Tick(LineView v, bool changed)
        {
            if (!_open) return;
            var a = _apps[(int)_app];
            if (changed || _refresh) { _refresh = false; a.Refresh(v, false); }
            a.Tick(v);

            int sec = Mathf.CeilToInt((float)(v.PhaseEndTime - Dk.Now));
            if (sec < 0) sec = 0;
            if (sec != _lastSec)
            {
                _lastSec = sec;
                _tbClock.SetText(ClockInt(sec));
                for (int i = 0; i < _apps.Length; i++) UpdateBadge(i, v);
            }
            if (changed)
            {
                int ss = (int)v.Phase * 100 + v.RoundIndex * 10 + v.Day;
                if (ss != _subSig)
                {
                    _subSig = ss;
                    _tbPhase.SetText(Dk.PhaseName(v.Phase));
                    _winSub.SetText(a.Subtitle(v));
                }
            }

            // blocked (coffee / offline)
            bool blocked = Dk.ComputerBlocked(v);
            int bs = blocked ? (int)v.Mine.ComputerBlockedBy : 0;
            if (bs != _blockSig)
            {
                bool was = _blockSig > 0;
                _blockSig = bs;
                _blocked.Show(blocked);
                _net.Show(blocked && v.Mine.ComputerBlockedBy == TrickType.InternetKes);
                if (blocked)
                {
                    bool coffee = v.Mine.ComputerBlockedBy == TrickType.KahveDok;
                    _blocked.EnableInClassList("dk-blocked-coffee", coffee);
                    _blocked.EnableInClassList("dk-blocked-offline", !coffee);
                    _blockArt.style.backgroundImage = StyleKeyword.Null;
                    Dk.Art(_blockArt, coffee ? "computer/coffee_overlay" : "computer/offline_overlay");
                    _blockTitle.SetText(Dk.T(coffee ? "desk.blocked.coffee" : "desk.blocked.offline"));
                    _blockSub.SetText(Dk.T(coffee ? "desk.blocked.coffee_sub" : "desk.blocked.offline_sub"));
                    _lastBlockSec = -1;
                    if (!was)
                    {
                        UiMotion.FadeIn(_blockArt, 0.25f);
                        UiMotion.PopIn(_blockTimer.parent, 0.3f, 0.15f);
                        UiMotion.Shake(_monitor, 10f, 0.4f);
                    }
                }
            }
            if (blocked)
            {
                int s = Mathf.CeilToInt((float)(v.Mine.ComputerBlockedUntil - Dk.Now));
                if (s != _lastBlockSec) { _lastBlockSec = s; _blockTimer.SetText(ClockInt(s)); }
            }
        }

        void UpdateBadge(int i, LineView v)
        {
            int n = _apps[i].Badge(v);
            if (n == _badgeVals[i]) return;
            _badgeVals[i] = n;
            _iconBadges[i].Show(n != 0);
            _iconBadges[i].SetText(n < 0 ? "!" : Num(n));
            if (n != 0) UiMotion.Pulse(_iconBadges[i], 0.3f, 0.3f);
        }

        /// <summary>Selects the window subtitle of the active app after a local change (e.g. compose).</summary>
        public void RefreshSubtitle() { _subSig = int.MinValue; }
    }
}
