using System;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.PlayKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// The office computer ("KOVA OS"): an OS-like desktop laid exactly over the monitor's screen while the camera is
    /// framed on it (wallpaper, app icons, one app window, a sticky note with the "Şimdi" hint, taskbar with the phase
    /// clock and your prim). Apps: Görev (task card: Client Bonus is chosen here; Petty Cash, Weekend Shift and the
    /// whistle are played on the desk objects, so the card says where and one button turns the view there), Havale
    /// (transfers), Mesajlar (private texts), Arama geçmişi (call log and line board), Kurallar (the how-to pages).
    /// Content comes from the shared <see cref="PlayBlocks"/>.
    /// </summary>
    public sealed class OfficeComputer
    {
        public enum App { Task = 0, Transfer = 1, Messages = 2, Calls = 3, Rules = 4 }

        static readonly string[] Icons = { "icons_v3/task_app", "icons_v3/share", "icons_v3/mail_app", "icons/call", "icons_v3/agenda_app" };
        static readonly string[] Ids = { "task", "transfer", "messages", "calls", "rules" };

        readonly OfficeUi _ui;
        readonly PlayState _st;
        public readonly VisualElement Root;
        readonly VisualElement _window, _winIcon, _composeBox;
        readonly Label _winTitle, _winSub, _stickyText, _stickyKicker, _tbPhase, _tbTimer, _tbPrim, _tbUser;
        readonly VisualElement _tbBadge;
        readonly Button[] _tiles = new Button[5];
        readonly Label[] _badges = new Label[5];
        readonly ScrollView _scroll;
        readonly PlaySection _body, _composeTargets;
        readonly TextField _field;
        readonly Label _composeInfo;
        App _app;
        bool _open, _dirty = true, _scrollBottom;
        int _rulesPage, _seenMessages, _seenMissed, _userId = int.MinValue;
        float _alpha;

        public bool IsOpen => _open;
        public App Current => _app;
        public bool Shown => _open && _alpha > 0.01f;

        public OfficeComputer(OfficeUi ui, VisualElement parent, PlayState st)
        {
            _ui = ui;
            _st = st;
            Root = Box("os-root").AddTo(parent);
            Root.style.position = Position.Absolute;
            UiArt.Apply(Root, "computer/os_wallpaper");

            var desk = Box("os-desk").AddTo(Root);
            var icons = Box("os-icons").AddTo(desk);
            for (int i = 0; i < Icons.Length; i++)
            {
                var app = (App)i;
                var tile = new Button(() => { Sfx.Play("click"); SetApp(app); }).Cls("os-tile");
                tile.RemoveFromClassList(Button.ussClassName);
                var img = Box("os-tile-img").NoPick().AddTo(tile);
                if (!UiArt.Apply(img, Icons[i])) img.AddToClassList("os-tile-img-fallback");
                Text(T("office.app." + Ids[i]), "os-tile-label").NoPick().AddTo(tile);
                _badges[i] = Bold("", "os-badge").NoPick().AddTo(tile);
                _badges[i].Show(false);
                tile.AddTo(icons);
                _tiles[i] = tile;
            }

            _window = Box("os-window").AddTo(desk);
            var title = Box("os-titlebar").AddTo(_window);
            _winIcon = Box("os-win-icon").NoPick().AddTo(title);
            _winTitle = Title("", "os-win-title").NoPick().AddTo(title);
            _winSub = Bold("", "os-win-sub").NoPick().AddTo(title);
            Box("grow").NoPick().AddTo(title);
            var x = new Button(() => _ui.CloseFocus()) { text = "×" }.Cls("os-win-close");
            x.AddTo(title);
            _scroll = new ScrollView(ScrollViewMode.Vertical).Cls("os-body");
            _scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _window.Add(_scroll);
            _body = new PlaySection(_scroll.contentContainer, "os-section");

            _composeBox = Box("os-compose").AddTo(_window);
            _composeInfo = Text("", "pl-text", "pl-dim").AddTo(_composeBox);
            _composeTargets = new PlaySection(_composeBox, "os-section");
            var row = Box("pl-row").AddTo(_composeBox);
            _field = new TextField { maxLength = 200 }.Cls("pl-field", "grow").AddTo(row);
            _field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Send(); e.StopPropagation(); }
            }, TrickleDown.TrickleDown);
            Btn(T("msg.send"), Send, "pl-primary").AddTo(row);
            _composeBox.Show(false);

            var side = Box("os-side").AddTo(desk);
            var sticky = Box("os-sticky").NoPick().AddTo(side);
            _stickyKicker = Hand(T("office.now_kicker"), "os-sticky-kicker").NoPick().AddTo(sticky);
            _stickyText = Hand("", "os-sticky-text").NoPick().AddTo(sticky);

            var bar = Box("os-taskbar").AddTo(Root);
            var brand = Box("os-tb-brand").NoPick().AddTo(bar);
            var logo = Box("os-tb-logo").NoPick().AddTo(brand);
            if (!UiArt.Apply(logo, "computer/boot_logo")) logo.AddToClassList("os-tb-logo-fallback");
            Title(T("office.os_brand"), "os-tb-brand-text").NoPick().AddTo(brand);
            var user = Box("os-tb-user").NoPick().AddTo(bar);
            _tbBadge = Box("os-tb-dot").NoPick().AddTo(user);
            _tbUser = Bold("", "os-tb-text").NoPick().AddTo(user);
            Box("grow").NoPick().AddTo(bar);
            _tbPhase = Text("", "os-tb-text", "os-tb-dim").NoPick().AddTo(bar);
            _tbTimer = Title("", "os-tb-timer").NoPick().AddTo(bar);
            _tbPrim = Bold("", "os-tb-prim").NoPick().AddTo(bar);
            var close = Btn(T("office.os_close") + "  ·  Esc", () => _ui.CloseFocus(), "os-tb-close");
            close.AddTo(bar);
            Root.Show(false);
        }

        // ------------------------------------------------------------------ open / close

        public void Open(App app)
        {
            bool was = _open;
            _open = true;
            Root.Show(true);
            if (!was) { _alpha = 0f; Root.style.opacity = 0f; }
            SetApp(app);
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            Root.Show(false);
            Root.style.opacity = 0f;
            _alpha = 0f;
            if (_field.focusController != null && _field.focusController.focusedElement == _field) _field.Blur();
        }

        public void SetApp(App app)
        {
            bool changed = app != _app;
            _app = app;
            for (int i = 0; i < _tiles.Length; i++) _tiles[i].EnableInClassList("os-tile-on", i == (int)app);
            _winTitle.SetText(T("office.app." + Ids[(int)app]));
            UiArt.Apply(_winIcon, Icons[(int)app]);
            _composeBox.Show(app == App.Messages);
            _dirty = true;
            _body.Invalidate();
            if (changed || app == App.Messages) _scroll.scrollOffset = Vector2.zero;
            _scrollBottom = app == App.Messages;
            if (_open) UiMotion.SlideIn(_window, new Vector2(0f, 18f), 0.22f, 0f, UiMotion.Ease.OutCubic);
        }

        /// <summary>Open the rules app on a page (the "Kurallar" link of the task card).</summary>
        public void ShowRules(int page)
        {
            _rulesPage = Mathf.Clamp(page, 0, HowToPlayScreen.Pages - 1);
            SetApp(App.Rules);
        }

        public void MarkDirty() { _dirty = true; }

        /// <summary>Lays the desktop over the monitor screen (panel coordinates); fades in once the camera has settled.</summary>
        public void Place(Rect r, bool settled, float dt)
        {
            if (!_open) return;
            Root.style.left = r.x; Root.style.top = r.y; Root.style.width = r.width; Root.style.height = r.height;
            _alpha = Mathf.MoveTowards(_alpha, settled ? 1f : 0f, dt * (settled ? 7f : 20f));
            Root.style.opacity = _alpha;
            Root.pickingMode = _alpha > 0.5f ? PickingMode.Position : PickingMode.Ignore;
        }

        void Send()
        {
            var lc = LineClient.I;
            var v = lc != null ? lc.View : null;
            if (!PlayBlocks.SendMessage(v, _st, _field.value)) return;
            _field.SetValueWithoutNotify("");
            _field.Focus();
            _scrollBottom = true;
        }

        public void FocusField() { if (_app == App.Messages) _field.Focus(); }

        // ------------------------------------------------------------------ per frame

        public void Tick(LineClient lc, LineView v, double now, bool dirty, bool live)
        {
            UpdateBadges(lc, v);
            if (!_open) return;
            var m = v.Mine;
            if (lc.MySlot != _userId)
            {
                _userId = lc.MySlot;
                var me = lc.Me;
                _tbBadge.style.backgroundColor = PlayerColor(me != null ? me.ColorIndex : _userId);
            }
            _tbUser.SetText(lc.NameOf(lc.MySlot));
            _tbPhase.SetText(PlayText.PhaseTitle(v));
            _tbTimer.SetText(UiKit.Clock(Math.Max(0, v.PhaseEndTime - now)));
            _tbPrim.SetText(F("header.my_prim", m.Prim));
            bool todo;
            _stickyText.SetText(OfficeText.Now(lc, v, out todo));
            _stickyText.EnableInClassList("os-sticky-todo", todo);
            _winSub.SetText(Subtitle(v));
            if (dirty || _dirty)
            {
                _dirty = false;
                _body.Update(p => Build(p, lc, v, now));
                if (_app == App.Messages)
                {
                    _composeTargets.Update(p => PlayBlocks.BuildComposeTargets(p, lc, v, _st, _composeInfo));
                    _seenMessages = CountIncoming(lc);
                }
                if (_app == App.Calls) _seenMissed = CountMissed(v);
            }
            if (_scrollBottom && _scroll.contentContainer.layout.height > 1f)
            {
                _scrollBottom = false;
                _scroll.scrollOffset = new Vector2(0f, Mathf.Max(0f, _scroll.contentContainer.layout.height));
            }
            if (live) _body.TickLive(now);
        }

        string Subtitle(LineView v)
        {
            switch (_app)
            {
                case App.Task: return v.Task != null ? F("task.title", v.Task.Number + 1, PlayText.Task(v.Task.Kind)) : PlayText.Phase(v.Phase);
                case App.Transfer: return F("header.my_prim", v.Mine.Prim);
                case App.Rules: return (_rulesPage + 1) + " / " + HowToPlayScreen.Pages;
                default: return "";
            }
        }

        static int CountIncoming(LineClient lc)
        {
            int n = 0;
            foreach (var msg in lc.Messages) if (msg.From != lc.MySlot) n++;
            return n;
        }

        static int CountMissed(LineView v)
        {
            int n = 0;
            foreach (var c in v.Mine.CallLog) if (!c.Connected && !c.Outgoing && c.EndReason == CallEndReason.Missed) n++;
            return n;
        }

        void UpdateBadges(LineClient lc, LineView v)
        {
            var m = v.Mine;
            bool taskTodo = (v.Phase == LinePhase.TaskPlay && v.Task != null && !v.Task.Submitted)
                            || (v.Phase == LinePhase.Break && v.Features.WhistleOpen && m.WitnessOf.Count > 0);
            SetBadge(App.Task, taskTodo ? "!" : null);
            int unread = Math.Max(0, CountIncoming(lc) - _seenMessages);
            SetBadge(App.Messages, unread > 0 && !(_open && _app == App.Messages) ? unread.ToString() : null);
            int missed = Math.Max(0, CountMissed(v) - _seenMissed);
            SetBadge(App.Calls, missed > 0 && !(_open && _app == App.Calls) ? missed.ToString() : null);
        }

        /// <summary>Unread texts / missed calls since the apps were last opened (the HUD shows them too).</summary>
        public int Unread(LineClient lc) { return Math.Max(0, CountIncoming(lc) - _seenMessages); }

        void SetBadge(App app, string text)
        {
            var b = _badges[(int)app];
            bool show = !string.IsNullOrEmpty(text);
            if (b.IsShown() != show) { b.Show(show); if (show) UiMotion.Pulse(b, 0.3f, 0.3f); }
            if (show && b.text != text) b.text = text;
        }

        /// <summary>A new week: forget what was read.</summary>
        public void ResetWeek() { _seenMessages = 0; _seenMissed = 0; _rulesPage = 0; _app = App.Task; _dirty = true; }

        // ------------------------------------------------------------------ apps

        void Build(VisualElement p, LineClient lc, LineView v, double now)
        {
            switch (_app)
            {
                case App.Task: BuildTask(p, lc, v); break;
                case App.Transfer: PlayBlocks.BuildTransfer(p, lc, v, _st, false); break;
                case App.Messages: BuildChat(p, lc); break;
                case App.Calls: BuildCalls(p, v, now); break;
                case App.Rules: BuildRules(p); break;
            }
        }

        void BuildTask(VisualElement p, LineClient lc, LineView v)
        {
            var card = Box("pl-focus", "os-card").AddTo(p);
            var head = Row(card);
            Bold(T("focus.kicker"), "pl-focus-kicker", "grow").AddTo(head);
            int page = PlayBlocks.HowToPage(v);
            if (page >= 0) Btn(T("focus.rules"), () => ShowRules(page), "pl-small").AddTo(head);
            int state = Info;
            switch (v.Phase)
            {
                case LinePhase.Intro:
                    Big(card, T("hint.Intro"));
                    Line(card, F("focus.intro_goal", lc.Config.StartPrim));
                    break;
                case LinePhase.TaskPlay:
                    state = PlayBlocks.FocusTask(card, lc, v, _ui.OpenTaskObject);
                    break;
                case LinePhase.TaskReveal:
                case LinePhase.Break:
                    state = PlayBlocks.FocusReveal(card, lc, v, _ui.OpenTaskObject);
                    if (state != Todo && v.Task != null && v.Task.Kind != TaskKind.ClientBonus && v.Task.Revealed)
                    {
                        var kind = v.Task.Kind;
                        Btn(T("office.look." + LineTasks.Id(kind)), () => _ui.OpenTaskObject(kind), "pl-small").AddTo(card);
                    }
                    break;
                case LinePhase.CrisisCalls:
                    PlayBlocks.CrisisCalls(card, lc, v, "office.crisis_how");
                    break;
                default:
                    Big(card, PlayText.Hint(v.Phase));
                    break;
            }
            card.EnableInClassList("pl-focus-todo", state == Todo);
            card.EnableInClassList("pl-focus-done", state == Done);
            if (v.Phase == LinePhase.Intro) PlayBlocks.BuildIntro(p, lc, v);
            else PlayBlocks.BuildTask(p, lc, v);
        }

        /// <summary>Private texts as chat bubbles: yours on the right, theirs on the left with the sender's colour.</summary>
        static void BuildChat(VisualElement p, LineClient lc)
        {
            var msgs = lc.Messages;
            if (msgs.Count == 0) { Line(p, T("msg.empty"), "pl-dim"); return; }
            int me = lc.MySlot;
            for (int i = Math.Max(0, msgs.Count - 40); i < msgs.Count; i++)
            {
                var m = msgs[i];
                bool mine = m.From == me;
                var row = Box("os-msg-row", mine ? "os-msg-row-out" : null).AddTo(p);
                var bubble = Box("os-msg", mine ? "os-msg-out" : m.Kind == MessageKind.System ? "os-msg-sys" : "os-msg-in").AddTo(row);
                string who = m.Kind == MessageKind.System ? T("msg.system") : mine ? F("msg.text_to", Name(m.To)) : Name(m.From);
                var head = Bold(who, "os-msg-who").AddTo(bubble);
                if (!mine && m.Kind != MessageKind.System) head.style.color = PlayerColor(lc.ColorOf(m.From)) * 0.8f + new Color(0f, 0f, 0f, 0.2f);
                Text(m.Text, "os-msg-text").AddTo(bubble);
            }
        }

        void BuildCalls(VisualElement p, LineView v, double now)
        {
            var f = v.Features;
            Sub(p, T("phone.board"));
            if (v.LineBoard.Count == 0) Line(p, T(f.CallsOpen ? "phone.board_empty" : "phone.closed"), "pl-dim");
            foreach (var c in v.LineBoard)
            {
                string head = Name(c.Caller) + " → " + Name(c.Callee) + " · " + T(c.Status == CallStatus.Connected ? "phone.board_talking" : "phone.board_ringing");
                double start = c.Start;
                Live(p, "board" + c.CallId + ":" + c.Status + ":" + start, now, t => head + " · " + UiKit.Clock(Math.Max(0, t - start)));
            }
            PlayBlocks.BuildCallLog(p, v, now, 24, true);
            if (v.Mine.CallLog.Count == 0) Line(p, T("office.calllog_empty"), "pl-dim");
        }

        void BuildRules(VisualElement p)
        {
            var cols = Box("os-rules").AddTo(p);
            var list = Box("os-rules-list").AddTo(cols);
            for (int i = 0; i < HowToPlayScreen.Pages; i++)
            {
                int page = i;
                Btn(HowToPlayScreen.PageTitle(i), () => { _rulesPage = page; _dirty = true; _scroll.scrollOffset = Vector2.zero; }, "os-rules-item", i == _rulesPage ? "pl-on" : null).AddTo(list);
            }
            var text = Box("os-rules-text").AddTo(cols);
            Title(HowToPlayScreen.PageTitle(_rulesPage), "os-rules-title").AddTo(text);
            Text(HowToPlayScreen.PageBody(_rulesPage), "os-rules-body").AddTo(text);
        }
    }
}
