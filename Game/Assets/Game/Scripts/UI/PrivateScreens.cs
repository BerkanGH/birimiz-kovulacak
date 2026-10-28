using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>Morning briefing: today's manager, your role, objective, secret cards, today's features, controls.</summary>
    public sealed class BriefingScreen : UiKit.Screen
    {
        readonly VisualElement _body;
        readonly Label _timer, _title;
        bool _dismissed;
        int _builtFor = -1, _builtVersion = -1, _lastSec = -1;
        public bool WantsCursor => Visible && !_dismissed;

        public BriefingScreen(VisualElement parent) : base(parent, "center", "dim-soft")
        {
            var card = Panel("panel-folder", "briefing").AddTo(Root);
            var head = Box("brief-head").AddTo(card);
            _title = Title("", "h1", "grow").AddTo(head);
            Bold(Loc.T("briefing.starts_in"), "t-muted", "gap-m").AddTo(head);
            _timer = Title("", "brief-timer").AddTo(head);
            _body = Box("col").AddTo(card);
            var foot = Box("row").AddTo(card);
            Text(Loc.T("briefing.tips"), "t-small", "t-muted", "grow").AddTo(foot);
            Btn(Loc.T("briefing.go"), Dismiss, "btn-primary", "btn-big").AddTo(foot);
        }

        void Dismiss() { _dismissed = true; Root.Show(false); }

        protected override void OnShow() { _dismissed = false; Root.Show(true); _builtFor = -1; }

        public override void Tick()
        {
            if (!Visible) return;
            var v = ClientState.I.View;
            if (v == null) return;
            int sec = (int)System.Math.Ceiling(System.Math.Max(0, v.PhaseEndTime - ClientState.I.Now));
            if (sec != _lastSec) { _lastSec = sec; _timer.text = ClockInt(sec); }
            if (!_dismissed && UiInput.Confirm != null && UiInput.Confirm.WasPressedThisFrame()) Dismiss();
            if (_builtFor == v.MatchId && (v.StateVersion - _builtVersion) < 20) return;
            if (_builtFor != v.MatchId) { _dismissed = false; Root.Show(true); }
            _builtFor = v.MatchId;
            _builtVersion = v.StateVersion;
            Build(v);
        }

        void Build(PlayerView v)
        {
            _body.Clear();
            _title.text = v.Mode == GameMode.Week ? Loc.F("briefing.title_day", Loc.DayName(v.WeekDay)) : Loc.T("briefing.title");
            if (v.Mode == GameMode.Week) WeekBoard.DayBanner(_body, v.WeekDay, "h2").AddToClassList("brief-banner");
            var lobby = ClientState.I.Lobby;
            if (lobby != null && lobby.MatchRunning && lobby.StatusKey != null && lobby.StatusKey.StartsWith("day.") && lobby.StatusKey != DayModifiers.Normal)
                Bold(Loc.F("briefing.day", Loc.T(lobby.StatusKey)) + " — " + Loc.T(lobby.StatusKey + ".desc"), "t-accent").AddTo(_body);
            var cols = Box("brief-cols").AddTo(_body);

            var c1 = Box("brief-col").AddTo(cols);
            ManagerCard(c1, v, false);

            var c2 = Box("brief-col").AddTo(cols);
            PhonePanel.BuildRoleCards(c2, v, true);
            var me = ClientState.I.Me;
            Bold(Loc.F("briefing.quota", v.Quota.Target.ToString("0")), "h3", "mt-s").AddTo(c2);
            if (me != null) Text(Loc.F("briefing.desk", me.DeskIndex + 1), "t-small").AddTo(c2);

            var c3 = Box("brief-col").AddTo(cols);
            Section(c3, Loc.T("briefing.cards"), "cards");
            if (!v.Features.Cards) Empty(c3, Loc.T("cards.disabled_today"));
            else if (v.Me.Cards.Count == 0) Empty(c3, Loc.T("cards.none"));
            else foreach (var t in v.Me.Cards) PhonePanel.CardView(c3, t, v, false);
            Section(c3, Loc.T("briefing.features"), "week_calendar");
            FeatureChips(c3, v);
            if (v.Features.CareerMultiplier > 1.01f) Bold(Loc.F("briefing.multiplier", v.Features.CareerMultiplier.ToString("0.#")), "t-bad", "mt-s").AddTo(c3);

            ControlsStrip(_body);
        }

        public static void FeatureChips(VisualElement parent, PlayerView v)
        {
            var row = Box("feature-row").AddTo(parent);
            Chip(row, "cards", v.Features.Cards);
            Chip(row, "calls", v.Features.Calls);
            Chip(row, "tipline", v.Features.TipLine);
            Chip(row, "footage", v.Features.Footage);
            Chip(row, "mail", v.Features.Mail);
        }

        static void Chip(VisualElement row, string feature, bool on)
        {
            var c = Box("feature-chip").AddTo(row);
            if (!on) c.AddToClassList("feature-off");
            Icon(GameInfo.FeatureIcon(feature)).AddTo(c);
            Bold(Loc.T("feature." + feature) + (on ? "" : " " + Loc.T("feature.off")), "feature-chip-text").AddTo(c);
        }

        /// <summary>Manager card: portrait, name, title, likes (+) / hates (-) / notes (i).</summary>
        public static void ManagerCard(VisualElement parent, PlayerView v, bool compact)
        {
            string id = v.ManagerProfileId;
            var card = Box("mgr-card").AddTo(parent);
            if (compact)
            {
                var row = Box("row").AddTo(card);
                ManagerPortrait(id, false, "badge-l").AddTo(row);
                var col = Box("col", "grow").AddTo(row);
                col.style.marginLeft = 10;
                Title(Loc.ManagerName(id), "h3").AddTo(col);
                Bold(Loc.ManagerTitle(id), "mgr-title").AddTo(col);
            }
            else
            {
                Bold(Loc.T("briefing.today_manager"), "t-muted", "t-small").AddTo(card);
                ManagerPortrait(id, false, "badge-xxl").AddTo(card);
                Title(Loc.ManagerName(id), "mgr-name").AddTo(card);
                Bold(Loc.ManagerTitle(id), "mgr-title").AddTo(card);
            }
            var likes = Box("likes").AddTo(card);
            string prefix = string.IsNullOrEmpty(id) ? "manager.default" : "manager." + id;
            for (int i = 0; i < 4; i++) Like(likes, prefix + ".like." + i, "like-good", "+");
            for (int i = 0; i < 4; i++) Like(likes, prefix + ".hate." + i, "like-bad", "–");
            for (int i = 0; i < 3; i++) Like(likes, prefix + ".note." + i, "like-info", "i");
            if (v.Favourite >= 0) Like(likes, null, "like-good", "*", Loc.F("manager.favourite_is", Loc.PlayerName(v, v.Favourite)));
        }

        static void Like(VisualElement parent, string key, string cls, string mark, string text = null)
        {
            if (text == null)
            {
                if (!Loc.Has(key)) return;
                text = Loc.T(key);
            }
            var r = Box("like", cls).AddTo(parent);
            Bold(mark, "like-mark").AddTo(r);
            Text(text, "like-text").AddTo(r);
        }

        public static void ControlsStrip(VisualElement parent)
        {
            var s = Box("controls-strip").AddTo(parent);
            Ctl(s, "WASD", "ctl.move");
            Ctl(s, GameInput.KeyLabel(GameInput.Interact) + " / " + GameInput.KeyLabel(GameInput.Secondary), "ctl.interact");
            Ctl(s, GameInput.KeyLabel(GameInput.Drop), "ctl.drop");
            Ctl(s, GameInput.KeyLabel(GameInput.WorkPose), "ctl.work_pose");
            Ctl(s, GameInput.KeyLabel(GameInput.UseItem), "ctl.use_item");
            Ctl(s, GameInput.KeyLabel(GameInput.Crouch) + " / " + GameInput.KeyLabel(GameInput.CrouchToggle), "ctl.crouch");
            Ctl(s, GameInput.KeyLabel(GameInput.Phone), "ctl.phone");
            Ctl(s, GameInput.KeyLabel(GameInput.EmoteWheel) + " · 1–5", "ctl.emote");
            Ctl(s, GameInput.KeyLabel(GameInput.AnswerCall) + " / " + GameInput.KeyLabel(GameInput.HangUp), "ctl.call");
            Ctl(s, GameInput.KeyLabel(GameInput.PushToTalk), "ctl.ptt");
        }

        static void Ctl(VisualElement parent, string key, string labelKey)
        {
            var c = Box("ctl").AddTo(parent);
            KeyCap(key).AddTo(c);
            Text(Loc.T(labelKey), "ctl-text").AddTo(c);
        }
    }

    /// <summary>
    /// The private phone (Tab). Everyone opens the same phone, so using it is no tell; the İK app only exists on the
    /// Informant's phone. Apps: Görev, Kartlar, Rehber (calls + tip line), Gelen Kutusu, Notlar, Pano, İK.
    /// Content is rebuilt only when the view changes (throttled) or a timer second ticks over.
    /// </summary>
    public sealed class PhonePanel : UiKit.Screen
    {
        public enum App { Objective = 0, Cards = 1, Contacts = 2, Inbox = 3, Notes = 4, Board = 5, Hr = 6 }
        static PhonePanel _i;
        static int _seenEmails, _seenMatch = -1;

        readonly VisualElement _phone, _content;
        readonly ScrollView _scroll;
        readonly Label _appTitle, _clock;
        readonly Dictionary<App, Button> _dock = new Dictionary<App, Button>();
        readonly Label _inboxBadge, _notesBadge;
        App _app = App.Objective;
        bool _dirty = true;
        int _lastVersion = -1, _lastSecond = -1, _seenNotes, _expandedMail = -1, _tipDesk = -1;
        float _lastBuild;

        public PhonePanel(VisualElement parent) : base(parent, "dim-soft", "phone-layer")
        {
            _i = this;
            _phone = Box("phone").AddTo(Root);
            if (!UiArt.Apply(_phone, "phone/phone_frame")) _phone.AddToClassList("phone-fallback");
            var screen = Box("phone-screen").AddTo(_phone);
            var status = Box("phone-status").AddTo(screen);
            _appTitle = Bold("", "phone-status-title").AddTo(status);
            _clock = Bold("", "phone-status-time").AddTo(status);
            _scroll = new ScrollView(ScrollViewMode.Vertical).Cls("phone-body").AddTo(screen);
            _content = _scroll.contentContainer;
            var dock = Box("phone-dock").AddTo(screen);
            AddApp(dock, App.Objective, "app_tasks", "objective");
            AddApp(dock, App.Cards, "app_cards", "cards");
            AddApp(dock, App.Contacts, "app_contacts", "contacts");
            _inboxBadge = Bold("", "dock-badge").NoPick().AddTo(AddApp(dock, App.Inbox, "app_inbox", "email"));
            _notesBadge = Bold("", "dock-badge").NoPick().AddTo(AddApp(dock, App.Notes, "app_notes", "notes"));
            AddApp(dock, App.Board, null, "manager");
            AddApp(dock, App.Hr, "app_hr", "hr_scan");
            Root.RegisterCallback<PointerDownEvent>(e => { if (e.target == Root) Hide(); });
        }

        Button AddApp(VisualElement dock, App app, string appArt, string icon)
        {
            var b = Btn("", () => Open(app), "dock-btn");
            b.RemoveFromClassList("btn-secondary");
            var ic = Icon(icon).AddTo(b);
            if (appArt != null && UiArt.Apply(ic, "phone/" + appArt)) { ic.RemoveFromClassList("icon-fallback"); if (ic.childCount > 0) ic.Clear(); }
            Bold(Loc.T("phone.app." + app), "dock-label").NoPick().AddTo(b);
            b.AddTo(dock);
            _dock[app] = b;
            return b;
        }

        public static int UnreadCount(PlayerView v)
        {
            if (v == null) return 0;
            if (v.MatchId != _seenMatch) { _seenMatch = v.MatchId; _seenEmails = 0; }
            return Mathf.Max(0, v.Inbox.Count - _seenEmails);
        }

        public static void OpenApp(App app)
        {
            if (_i == null) return;
            _i.SetVisible(true);
            _i.Open(app);
        }

        void Open(App app)
        {
            if (_app != app) _scroll.scrollOffset = Vector2.zero;
            _app = app;
            _dirty = true;
        }

        public void Toggle() { SetVisible(!Visible); }
        public void Hide() { SetVisible(false); }
        protected override void OnShow() { _dirty = true; Pop(_phone); }

        public override void Tick()
        {
            if (!Visible) return;
            var v = ClientState.I.View;
            if (v == null) return;
            double now = ClientState.I.Now;
            int sec = (int)now;
            bool timed = _app == App.Cards || _app == App.Contacts || _app == App.Hr || _app == App.Objective;
            if (v.StateVersion != _lastVersion && Time.unscaledTime - _lastBuild > 0.35f) _dirty = true;
            if (timed && sec != _lastSecond) _dirty = true;
            if (sec != _lastSecond) { _lastSecond = sec; _clock.text = Loc.Time(now, v); }
            if (!_dirty) return;
            _dirty = false;
            _lastVersion = v.StateVersion;
            _lastBuild = Time.unscaledTime;
            if (v.Me.Role != Role.Informant && _app == App.Hr) _app = App.Objective;
            _dock[App.Hr].Show(v.Me.Role == Role.Informant);
            foreach (var kv in _dock) kv.Value.EnableInClassList("dock-btn-active", kv.Key == _app);
            if (_app == App.Inbox) { UnreadCount(v); _seenEmails = v.Inbox.Count; }
            if (_app == App.Notes) _seenNotes = v.Me.Notes.Count;
            int unread = UnreadCount(v);
            _inboxBadge.Show(unread > 0);
            _inboxBadge.text = Num(unread);
            int newNotes = Mathf.Max(0, v.Me.Notes.Count - _seenNotes);
            _notesBadge.Show(newNotes > 0);
            _notesBadge.text = Num(newNotes);
            _appTitle.text = Loc.T("phone.app_title." + _app);

            Vector2 scroll = _scroll.scrollOffset;
            _content.Clear();
            switch (_app)
            {
                case App.Objective: BuildObjective(_content, v); break;
                case App.Cards: BuildCards(_content, v, now); break;
                case App.Contacts: BuildContacts(_content, v, now); break;
                case App.Inbox: BuildInbox(_content, v); break;
                case App.Notes: BuildNotes(_content, v, false); break;
                case App.Board: BuildBoard(_content, v, v.Me.Role == Role.Informant); break;
                case App.Hr: BuildInformant(_content, v); break;
            }
            _scroll.schedule.Execute(() => _scroll.scrollOffset = scroll);
        }

        // ------------------------------------------------------------------ Görev

        static void BuildObjective(VisualElement parent, PlayerView v)
        {
            BuildRoleCards(parent, v, false);
            Section(parent, Loc.T("phone.today"), "manager");
            BriefingScreen.ManagerCard(parent, v, true);
            Text(Loc.F("hud.quota", v.Quota.Points.ToString("0"), v.Quota.Target.ToString("0")), "t-small", "mt-s").AddTo(parent);
        }

        public static void BuildRoleCards(VisualElement parent, PlayerView v, bool briefing)
        {
            var me = v.Me;
            bool inf = me.Role == Role.Informant;
            var role = Box("role-card").AddTo(parent);
            if (inf) role.AddToClassList("role-informant");
            var rr = Box("row").AddTo(role);
            Icon(inf ? "hr_seal" : "objective", "icon-m", "gap-m").AddTo(rr);
            Title(Loc.T(inf ? "role.informant" : "role.employee"), "h2").AddTo(rr);
            Text(Loc.T(inf ? "role.informant.desc" : "role.employee.desc"), "t-light", "t-small").AddTo(role);

            var o = me.Objective;
            if (o == null) return;
            var card = Box("objective-card").AddTo(parent);
            Bold(Loc.T("objective.secret"), "t-muted", "t-tiny").AddTo(card);
            Title(Loc.ObjectiveName(o.Type), "h3").AddTo(card);
            string target = o.Target >= 0 ? Loc.PlayerName(v, o.Target) : "";
            Text(Loc.F("objective." + o.Type + ".desc", target, o.Required, o.Required2), "t-small").AddTo(card);
            if (o.Required > 0)
            {
                var pr = Box("row", "mt-s").AddTo(card);
                Bar(o.Required > 0 ? (float)o.Progress / o.Required : 0, out var fill, "bar-good", "grow").AddTo(pr);
                Bold(" " + o.Progress + "/" + o.Required, "t-small").AddTo(pr);
                if (o.Required2 > 0) Text(Loc.F("objective.progress2", o.Progress2, o.Required2), "t-tiny", "t-muted").AddTo(card);
            }
            var st = Tag(Loc.T("objective.status." + o.Status), StatusClass(o.Status), "status-pill");
            st.AddTo(card);
            if (!briefing)
                foreach (var f in o.Flags) if (Loc.Has("objective.flag." + f)) Text("• " + Loc.T("objective.flag." + f), "flag-line").AddTo(card);
            Text(Loc.T(inf ? "objective.informant_gate" : "objective.gate"), "t-tiny", "t-muted", "mt-s").AddTo(card);
        }

        static string StatusClass(ObjectiveStatus s)
        {
            switch (s)
            {
                case ObjectiveStatus.Completed: case ObjectiveStatus.OnTrack: return "tag-green";
                case ObjectiveStatus.Failed: return "tag-red";
                case ObjectiveStatus.PendingVerdict: return "tag-sticky";
                default: return "tag-manila";
            }
        }

        // ------------------------------------------------------------------ Kartlar

        static void BuildCards(VisualElement parent, PlayerView v, double now)
        {
            Text(Loc.T("cards.help"), "t-small", "t-muted").AddTo(parent);
            Section(parent, Loc.T("cards.mine"), "cards");
            if (!v.Features.Cards) Empty(parent, Loc.T("cards.disabled_today"));
            else if (v.Me.Cards.Count == 0) Empty(parent, Loc.T("cards.none"));
            else foreach (var t in v.Me.Cards) CardView(parent, t, v, true, now);
            Section(parent, Loc.T("cards.basic"), "sab_plant");
            var cfg = GameInfo.Config;
            if (cfg == null) return;
            foreach (var d in cfg.Sabotage)
            {
                if (!d.Basic || d.Type == SabotageType.EvidenceTampering) continue;
                var r = Box("basic-row").AddTo(parent);
                Icon(UiArt.SabotageIcon(d.Type)).AddTo(r);
                var c = Box("col", "grow").AddTo(r);
                Bold(Loc.CardName(d.Type), "t-small").AddTo(c);
                Text(Loc.CardDesc(d.Type), "t-tiny", "t-muted").AddTo(c);
                double cd = GameInfo.Cooldown(v, d.Type) - now;
                if (cd > 0) Tag(Clock(cd), "tag-grey").AddTo(r);
            }
        }

        public static void CardView(VisualElement parent, SabotageType t, PlayerView v, bool showState, double now = 0)
        {
            var card = Box("sab-card").AddTo(parent);
            var art = Box("sab-card-art").AddTo(card);
            UiArt.ApplyCardArt(art, t);
            var col = Box("col", "grow").AddTo(card);
            Title(Loc.CardName(t), "sab-card-name").AddTo(col);
            Text(Loc.CardDesc(t), "sab-card-desc").AddTo(col);
            string where = Loc.T("card." + t + ".where");
            if (Loc.Has("card." + t + ".where")) Text(where, "t-tiny", "t-muted").AddTo(col);
            if (!showState) return;
            double cd = GameInfo.Cooldown(v, t) - now;
            var def = GameInfo.Config != null ? GameInfo.Config.GetSabotage(t) : null;
            var tags = Box("row", "mt-s").AddTo(col);
            if (cd > 0) { Tag(Loc.F("cards.cooldown", Clock(cd)), "tag-grey").AddTo(tags); card.AddToClassList("sab-card-cooldown"); }
            else Tag(Loc.T("cards.ready"), "tag-green").AddTo(tags);
            if (def != null && def.MaxPerMatch > 0) Tag(Loc.F("cards.max_per_match", def.MaxPerMatch), "tag-manila").AddTo(tags);
        }

        // ------------------------------------------------------------------ Rehber

        void BuildContacts(VisualElement parent, PlayerView v, double now)
        {
            int my = ClientState.I.MySlot;
            var call = v.Me.Call;
            if (!v.Features.Calls) { Empty(parent, Loc.T("contacts.disabled_today")); }
            else
            {
                if (call.State != CallState.None)
                {
                    var cur = Box("contact-row", "call-active").AddTo(parent);
                    Badge(call.Other, "badge-m").AddTo(cur);
                    var cc = Box("col", "grow").AddTo(cur);
                    Bold(Loc.T("call.state." + call.State), "contact-sub").AddTo(cc);
                    Title(Loc.PlayerName(v, call.Other), "contact-name").AddTo(cc);
                    if (call.State == CallState.RingingIn) Btn(Loc.T("call.answer"), () => ClientState.I.Send(ActionVerb.AnswerCall), "btn-primary", "btn-small").AddTo(cur);
                    Btn(Loc.T(call.State == CallState.Active ? "call.hang_up" : call.State == CallState.RingingOut ? "call.cancel" : "call.decline"),
                        () => ClientState.I.Send(call.State == CallState.Active ? ActionVerb.HangUp : ActionVerb.DeclineCall), "btn-danger", "btn-small").AddTo(cur);
                }
                Text(Loc.T("contacts.help"), "t-small", "t-muted").AddTo(parent);
                bool phaseOk = GameInfo.CallPhase(v.Phase);
                double cool = v.Me.CallCooldownUntil - now;
                if (cool > 0) Bold(Loc.F("contacts.cooldown", Clock(cool)), "t-small", "t-accent").AddTo(parent);
                foreach (var p in v.Players)
                {
                    if (p.Id == my) continue;
                    int id = p.Id;
                    var r = Box("contact-row").AddTo(parent);
                    Badge(p.Id, "badge-m").AddTo(r);
                    var c = Box("col", "grow").AddTo(r);
                    Bold(p.Name, "contact-name").AddTo(c);
                    string sub = !p.Connected ? Loc.T("contacts.offline") : p.Ringing ? Loc.T("contacts.ringing") : p.Activity == PublicActivity.OnCall ? Loc.T("contacts.busy") : Loc.T("contacts.available");
                    Text(sub, "contact-sub").AddTo(c);
                    var b = IconBtn("call", Loc.T("contacts.call"), () => ClientState.I.Send(ActionVerb.CallPlayer, "", id), "btn-primary", "btn-small").AddTo(r);
                    b.SetEnabled(phaseOk && call.State == CallState.None && cool <= 0 && p.Connected && !p.Ringing);
                }
                if (!phaseOk) Empty(parent, Loc.T("contacts.wrong_phase"));
            }

            Section(parent, Loc.T("tip.title"), "ringing");
            if (!v.Features.TipLine) { Empty(parent, Loc.T("tip.disabled_today")); return; }
            Text(Loc.F("tip.help", v.Me.TipsLeft), "t-small").AddTo(parent);
            int myDesk = GameInfo.MyDesk(v);
            bool can = v.Me.TipsLeft > 0 && GameInfo.TipPhase(v.Phase) && call.State == CallState.None;
            var grid = Box("desk-grid").AddTo(parent);
            foreach (var d in v.Desks)
            {
                if (d.AssignedOwner < 0 || d.Index == myDesk) continue;
                int desk = d.Index;
                var b = Btn("", () => ConfirmTip(desk), "desk-btn", "btn-small").AddTo(grid);
                Bold(Loc.Desk(desk), "t-small").NoPick().AddTo(b);
                Text(Loc.PlayerName(v, d.AssignedOwner), "t-tiny").NoPick().AddTo(b);
                b.SetEnabled(can);
            }
            if (v.Me.TipsLeft <= 0) Empty(parent, Loc.T("tip.used"));
            else if (!GameInfo.TipPhase(v.Phase)) Empty(parent, Loc.T("tip.wrong_phase"));
        }

        static void ConfirmTip(int desk)
        {
            var v = ClientState.I.View;
            if (v == null) return;
            ConfirmDialog.Show(Loc.T("tip.confirm_title"), Loc.F("tip.confirm", Loc.Desk(desk), Describe.DeskOwnerName(v, desk), GameInfo.Config != null ? GameInfo.Config.Calls.TipSeconds.ToString("0") : "3"),
                Loc.T("tip.confirm_ok"), () => ClientState.I.Send(ActionVerb.TipManager, EntityIds.Desk(desk)), true);
        }

        // ------------------------------------------------------------------ Gelen kutusu

        void BuildInbox(VisualElement parent, PlayerView v)
        {
            if (v.Inbox.Count == 0) Empty(parent, Loc.T("inbox.empty"));
            for (int i = v.Inbox.Count - 1; i >= 0; i--)
            {
                var m = v.Inbox[i];
                int id = m.Id;
                var row = Box("mail-row").AddTo(parent);
                var head = Box("mail-head").AddTo(row);
                Badge(m.FromPlayer, "badge-s").AddTo(head);
                var c = Box("col", "grow").AddTo(head);
                Text(Loc.F("inbox.from", Loc.PlayerName(v, m.FromPlayer), Loc.Desk(m.DeskIndex)), "mail-from").AddTo(c);
                Bold(Describe.EmailSubject(m.TemplateId, v, m.FromPlayer), "mail-subject").AddTo(c);
                Text(Loc.Time(m.Time, v), "mail-time").AddTo(head);
                if (_expandedMail == id) Text(Describe.EmailBody(m.TemplateId, v, m.FromPlayer), "mail-body").AddTo(row);
                row.RegisterCallback<ClickEvent>(_ => { _expandedMail = _expandedMail == id ? -1 : id; _dirty = true; });
            }
            Text(Loc.T("inbox.lesson"), "phone-hint").AddTo(parent);
        }

        // ------------------------------------------------------------------ Notlar

        public static void BuildNotes(VisualElement parent, PlayerView v, bool meeting)
        {
            if (!meeting) Text(Loc.T("phone.notes_help"), "t-small", "t-muted").AddTo(parent);
            if (v.Me.Notes.Count == 0) Empty(parent, Loc.T("phone.no_notes"));
            for (int i = v.Me.Notes.Count - 1; i >= 0; i--) NoteCard(parent, v.Me.Notes[i], v, null);
        }

        public static VisualElement NoteCard(VisualElement parent, PrivateNote n, PlayerView v, System.Action<int> present)
        {
            var card = Panel("panel-sticky", "note-card").AddTo(parent);
            if (n.Presented) card.AddToClassList("note-presented");
            var meta = Box("note-meta").AddTo(card);
            Icon(Describe.NoteIcon(n)).AddTo(meta);
            Bold(Loc.T("note.kind." + n.Kind), "t-small", "grow").AddTo(meta);
            if (n.Presented) Tag(Loc.T("phone.presented"), "tag-green").AddTo(meta);
            Hand(Describe.Note(n, v)).AddTo(card);
            if (n.Kind == NoteKind.CallLog)
                foreach (var c in n.Calls) Text("• " + Describe.Call(c, v), "call-list-row").AddTo(card);
            if (present != null && !n.Presented)
            {
                int id = n.Id;
                Btn(Loc.T("meeting.present"), () => present(id), "btn-primary", "btn-small").AddTo(card);
            }
            return card;
        }

        // ------------------------------------------------------------------ Pano

        public static void BuildBoard(VisualElement parent, PlayerView v, bool informantTools)
        {
            Section(parent, Loc.T("phone.evidence_board"), "warning");
            if (v.Evidence.Count == 0) Empty(parent, Loc.T("phone.no_evidence"));
            for (int i = v.Evidence.Count - 1; i >= 0; i--) EvidenceCard(parent, v.Evidence[i], v, informantTools);
            Section(parent, Loc.T("phone.observations"), "manager");
            if (v.Observations.Count == 0) Empty(parent, Loc.T("phone.no_observations"));
            for (int i = v.Observations.Count - 1; i >= 0; i--) ObservationRow(parent, v.Observations[i], v);
        }

        public static void ObservationRow(VisualElement parent, ObservationView o, PlayerView v)
        {
            var r = Box("list-row").AddTo(parent);
            Badge(o.Subject, "badge-xs").AddTo(r);
            Icon(Describe.ObservationIcon(o.Kind), "icon-s").AddTo(r);
            Text(Describe.Observation(o, v), "list-row-text").AddTo(r);
            if (Mathf.Abs(o.Severity) > 0.01f) Tag((o.Severity > 0 ? "+" : "") + o.Severity.ToString("0.#"), o.Severity > 0 ? "tag-red" : "tag-green").AddTo(r);
        }

        public static VisualElement EvidenceCard(VisualElement parent, EvidenceView e, PlayerView v, bool informantTools)
        {
            var card = Box("ev-card", e.Source == DiscoverySource.Manager ? "ev-manager" : e.Source == DiscoverySource.System ? "ev-system" : "ev-player").AddTo(parent);
            var head = Box("ev-head").AddTo(card);
            Icon(Describe.EvidenceIcon(e)).AddTo(head);
            Bold(Describe.EvidenceTitle(e), "ev-title").AddTo(head);
            int who = e.AttributedTo >= 0 ? e.AttributedTo : e.SubjectPlayer;
            if (who >= 0) Badge(who, "badge-xs").AddTo(head);
            string st = Describe.Status(e);
            if (!string.IsNullOrEmpty(st)) Tag(st, e.Sealed ? "tag-teal" : e.Status == EvidenceStatus.Moved ? "tag-manila" : "tag-grey").AddTo(head);
            if (e.RiskWeight > 0.01f) Tag("+" + e.RiskWeight.ToString("0.#"), "tag-red").AddTo(head);
            Text(Describe.Evidence(e, v), "ev-body").AddTo(card);
            if (e.Kind == EvidenceKind.CallLogStatement)
                foreach (var c in e.Calls) Text("• " + Describe.Call(c, v), "call-list-row").AddTo(card);
            if (informantTools && v.Me.Informant != null) InformantActions(card, e, v);
            return card;
        }

        static void InformantActions(VisualElement card, EvidenceView e, PlayerView v)
        {
            var inf = v.Me.Informant;
            bool scannable = e.Kind == EvidenceKind.SuspiciousItemFound || e.Kind == EvidenceKind.HiddenReportFound;
            bool sealable = scannable || e.Kind == EvidenceKind.WorkstationLockLog;
            bool office = LocalPlayerController.OfficePhase(v.Phase);
            if (!(scannable && (office || v.Phase == MatchPhase.MeetingDiscussion)) && !(sealable && office && !e.Sealed && e.Status == EvidenceStatus.Active)) return;
            var row = Box("ev-actions").AddTo(card);
            int id = e.Id;
            if (scannable && (office || v.Phase == MatchPhase.MeetingDiscussion))
            {
                var b = IconBtn("hr_scan", Loc.F("informant.scan_btn", inf.ScansLeft), () => ClientState.I.Send(ActionVerb.AuditScanEvidence, "", id), "btn-small").AddTo(row);
                b.SetEnabled(inf.ScansLeft > 0);
            }
            if (sealable && office && !e.Sealed && e.Status == EvidenceStatus.Active)
            {
                var b = IconBtn("hr_seal", Loc.F("informant.seal_btn", inf.SealsLeft), () => ClientState.I.Send(ActionVerb.SealEvidence, "", id), "btn-small").AddTo(row);
                b.SetEnabled(inf.SealsLeft > 0);
            }
        }

        // ------------------------------------------------------------------ İK

        static void BuildInformant(VisualElement parent, PlayerView v)
        {
            var inf = v.Me.Informant;
            if (inf == null) return;
            var charges = Box("row", "wrap").AddTo(parent);
            Tag(Loc.F("informant.scans", inf.ScansLeft), "tag-teal").AddTo(charges);
            Tag(Loc.F("informant.seals", inf.SealsLeft), "tag-teal").AddTo(charges);
            Tag(Loc.F("informant.call_logs", inf.CallLogsLeft), "tag-teal").AddTo(charges);
            if (v.Features.Footage) Tag(Loc.F("informant.footage", v.Me.FootageLeft), "tag-teal").AddTo(charges);
            Text(Loc.T("informant.help"), "t-small", "t-muted", "mt-s").AddTo(parent);

            Section(parent, Loc.T("informant.call_logs_title"), "hr_call_logs");
            Text(Loc.T("informant.call_logs_help"), "t-tiny", "t-muted").AddTo(parent);
            var pull = IconBtn("hr_call_logs", Loc.T("informant.pull_logs"), () => ClientState.I.Send(ActionVerb.PullCallLogs), "btn-small").AddTo(parent);
            pull.SetEnabled(inf.CallLogsLeft > 0 && v.Features.Calls);

            Section(parent, Loc.T("informant.nearby"), "hr_scan");
            var avatar = PlayerAvatar.Local;
            int shown = 0;
            if (avatar != null && LocalPlayerController.OfficePhase(v.Phase) && OfficeWorld.I != null)
            {
                float range = ClientState.I.Config.Informant.ItemScanRange;
                foreach (var it in v.Items)
                {
                    if (it.Kind != ItemKind.Suspicious && it.Kind != ItemKind.Report && it.Kind != ItemKind.Nameplate) continue;
                    Vector3 p;
                    if (!OfficeWorld.I.TryTargetPosition(it.Id, out p)) continue;
                    Vector3 d = p - avatar.transform.position; d.y = 0;
                    if (d.magnitude > range) continue;
                    string itemId = it.Id;
                    var r = Box("list-row").AddTo(parent);
                    Text(Hud.ItemLabel(it, v) + " — " + (it.Location == ItemLocationKind.Slot ? Loc.Location(it.SlotId) : Loc.T("informant.on_floor")), "list-row-text").AddTo(r);
                    var b = Btn(Loc.F("informant.scan_btn", inf.ScansLeft), () => ClientState.I.Send(ActionVerb.AuditScanItem, itemId), "btn-small").AddTo(r);
                    b.SetEnabled(inf.ScansLeft > 0);
                    shown++;
                }
            }
            if (shown == 0) Empty(parent, Loc.F("informant.nothing_nearby", ClientState.I.Config.Informant.ItemScanRange.ToString("0.#")));

            Section(parent, Loc.T("informant.results"), "notes");
            if (inf.Scans.Count == 0) Empty(parent, Loc.T("informant.no_scans"));
            foreach (var s in inf.Scans) Text(Describe.Scan(s, v), "list-row", "list-row-text").AddTo(parent);
            if (inf.SealedEvidenceId >= 0) Bold(Loc.F("informant.sealed", inf.SealedEvidenceId), "t-teal").AddTo(parent);

            Section(parent, Loc.T("phone.evidence_board"), "warning");
            if (v.Evidence.Count == 0) Empty(parent, Loc.T("phone.no_evidence"));
            for (int i = v.Evidence.Count - 1; i >= 0; i--) EvidenceCard(parent, v.Evidence[i], v, true);
        }
    }
}
