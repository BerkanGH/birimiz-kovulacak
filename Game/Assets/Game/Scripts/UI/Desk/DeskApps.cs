using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    // ======================================================================== Mail

    /// <summary>E-posta / Mail: inbox (newest first), reading pane, compose (recipient or everyone + template + mentioned colleague).</summary>
    public sealed class DeskMailApp : DeskAppView
    {
        public DeskMailApp(DeskUi ui) : base(ui, "dk-app-mail") { }
        public override DeskApp Id => DeskApp.Mail;
        public override string Icon => "mail_app";

        int _sig = int.MinValue, _selected = -1;
        bool _compose;
        int _to = LineIds.None, _template = -1, _about = LineIds.None;
        VisualElement _list, _pane;

        public override string Subtitle(LineView v) { return Dk.F("desk.mail.sub", Unread(v), v.Mine.Inbox.Count); }

        public int Unread(LineView v)
        {
            int n = 0;
            var inbox = v.Mine.Inbox;
            for (int i = 0; i < inbox.Count; i++) if (!Ui.State.ReadMail.Contains(inbox[i].Id)) n++;
            return n;
        }

        public override int Badge(LineView v) { return Unread(v); }

        public void Select(int id)
        {
            _selected = id;
            _compose = false;
            Ui.State.ReadMail.Add(id);
            var v = Dk.View;
            if (v != null) Build(v, false);
            Ui.Computer.RefreshSubtitle();
        }

        public void Compose(int to = LineIds.None, int template = -1, int about = LineIds.None)
        {
            _compose = true;
            _to = to; _template = template; _about = about;
            var v = Dk.View;
            if (v != null) Build(v, false);
        }

        public override bool Back()
        {
            if (_compose) { _compose = false; var v = Dk.View; if (v != null) Build(v, false); return true; }
            return false;
        }

        public override void OnShown()
        {
            var v = Dk.View;
            if (v == null || _compose || _selected >= 0) return;
            var inbox = v.Mine.Inbox;
            if (inbox.Count > 0) Select(inbox[inbox.Count - 1].Id);
        }

        public override void Refresh(LineView v, bool force)
        {
            var inbox = v.Mine.Inbox;
            int sig = inbox.Count * 1000 + (inbox.Count > 0 ? inbox[inbox.Count - 1].Id : 0);
            sig = sig * 31 + v.Mine.MailsSentToday;
            sig = sig * 31 + (Dk.ComputerBlocked(v) ? 1 : 0);
            sig = sig * 31 + v.Players.Count;
            if (!force && sig == _sig) return;
            _sig = sig;
            Build(v, force);
        }

        void Build(LineView v, bool animate)
        {
            Root.Clear();
            var page = Box("dk-mail").AddTo(Root);
            var left = Box("dk-mail-left").AddTo(page);
            int left_ = Mathf.Max(0, Dk.Config.MailsPerDay - v.Mine.MailsSentToday);
            var newRow = Box("dk-mail-new").AddTo(left);
            var nb = Btn(Dk.T("desk.mail.new"), () => Compose(), "btn-primary", "dk-mail-new-btn").AddTo(newRow);
            nb.SetEnabled(left_ > 0);
            Text(Dk.F("desk.mail.left", left_, Dk.Config.MailsPerDay), "dk-mail-left-count").NoPick().AddTo(newRow);
            var scroll = new ScrollView(ScrollViewMode.Vertical).Cls("dk-mail-list").AddTo(left);
            _list = scroll.contentContainer;
            var inbox = v.Mine.Inbox;
            if (inbox.Count == 0) Text(Dk.T("desk.mail.empty"), "dk-empty").NoPick().AddTo(_list);
            int n = 0;
            for (int i = inbox.Count - 1; i >= 0; i--)
            {
                var m = inbox[i];
                int id = m.Id;
                bool unread = !Ui.State.ReadMail.Contains(id);
                var row = Dk.Tile(() => Select(id), "dk-mail-row").AddTo(_list);
                if (unread) row.AddToClassList("dk-mail-row-unread");
                if (id == _selected && !_compose) row.AddToClassList("dk-mail-row-on");
                if (m.Kind == MailKind.Manager) row.AddToClassList("dk-mail-row-manager");
                Dk.Badge(m.From, "badge-s").AddTo(row);
                var col = Box("dk-mail-row-col").NoPick().AddTo(row);
                var top = Box("dk-mail-row-top").NoPick().AddTo(col);
                Bold(Dk.Name(m.From), "dk-mail-row-from").NoPick().AddTo(top);
                Box("spacer").NoPick().AddTo(top);
                Text(Dk.Ago(m.Time), "dk-mail-row-time").NoPick().AddTo(top);
                var subj = Box("dk-mail-row-subj").NoPick().AddTo(col);
                if (unread) Box("dk-unread-dot").NoPick().AddTo(subj);
                Text(Dk.MailSubject(m), "dk-mail-row-subject").NoPick().AddTo(subj);
                if (animate && n < 6) UiMotion.SlideIn(row, new Vector2(-24f, 0), 0.24f, 0.05f * n);
                n++;
            }

            _pane = Box("dk-mail-pane").AddTo(page);
            if (_compose) BuildCompose(v);
            else
            {
                LineMailView sel = null;
                for (int i = 0; i < inbox.Count; i++) if (inbox[i].Id == _selected) sel = inbox[i];
                if (sel != null) BuildRead(v, sel, animate);
                else
                {
                    var empty = Box("dk-mail-none").NoPick().AddTo(_pane);
                    Dk.Icon("mail_app", "dk-mail-none-icon").AddTo(empty);
                    Text(Dk.T(inbox.Count == 0 ? "desk.mail.none_yet" : "desk.mail.pick"), "dk-empty").NoPick().AddTo(empty);
                }
            }
        }

        void BuildRead(LineView v, LineMailView m, bool animate)
        {
            var paper = Box("dk-letter").AddTo(_pane);
            var head = Box("dk-letter-head").NoPick().AddTo(paper);
            Dk.Badge(m.From, "badge-l").AddTo(head);
            var hc = Box("dk-letter-headcol").NoPick().AddTo(head);
            Title(Dk.MailSubject(m), "dk-letter-subject").NoPick().AddTo(hc);
            Text(Dk.F("desk.mail.from_to", Dk.Name(m.From), Dk.T(m.ToAll ? "desk.mail.to_all" : "desk.mail.to_you")) + "  ·  " + Dk.Ago(m.Time), "dk-letter-meta").NoPick().AddTo(hc);
            Box("dk-email-rule").NoPick().AddTo(paper);
            Memo(Dk.MailBody(m), "dk-letter-body").NoPick().AddTo(paper);
            if (m.Kind == MailKind.Personal)
            {
                var foot = Box("dk-letter-foot").AddTo(paper);
                Text(Dk.T("desk.mail.spoof_hint"), "dk-letter-hint").NoPick().AddTo(foot);
                Box("spacer").NoPick().AddTo(foot);
                if (m.From >= 0 && m.From < 100 && m.From != v.Me)
                {
                    int from = m.From;
                    Btn(Dk.T("desk.mail.reply"), () => Compose(from), "btn-secondary", "btn-small").AddTo(foot);
                    var cb = Btn("", () => Ui.Call(from), "btn-secondary", "btn-small", "btn-icon").AddTo(foot);
                    Dk.Icon("call", "btn-icon-img").AddTo(cb);
                    Bold(Dk.T("desk.task.call"), "btn-label").NoPick().AddTo(cb);
                }
            }
            if (animate) UiMotion.SlideIn(paper, new Vector2(30f, 0), 0.26f, 0.1f);
        }

        void BuildCompose(LineView v)
        {
            var c = Box("dk-compose").AddTo(_pane);
            var lcol = Box("dk-compose-left").AddTo(c);
            Dk.Heading(lcol, Dk.T("desk.mail.compose_to"));
            var chips = Box("dk-chips").AddTo(lcol);
            Chip(chips, LineIds.None, Dk.T("desk.mail.everyone"), _to == LineIds.None, () => { _to = LineIds.None; Build(v, false); });
            for (int i = 0; i < v.Players.Count; i++)
            {
                int id = v.Players[i].Id;
                if (id == v.Me) continue;
                Chip(chips, id, v.Players[i].Name, _to == id, () => { _to = id; Build(v, false); });
            }
            bool needs = _template >= 0 && Dk.TemplateMentions(_template);
            if (needs)
            {
                Dk.Heading(lcol, Dk.T("desk.mail.compose_about"));
                var ach = Box("dk-chips").AddTo(lcol);
                for (int i = 0; i < v.Players.Count; i++)
                {
                    int id = v.Players[i].Id;
                    Chip(ach, id, v.Players[i].Name, _about == id, () => { _about = id; Build(v, false); });
                }
            }
            Box("spacer").NoPick().AddTo(lcol);
            var send = Box("dk-compose-send").AddTo(lcol);
            int left = Mathf.Max(0, Dk.Config.MailsPerDay - v.Mine.MailsSentToday);
            var row = Box("row").AddTo(send);
            Btn(Dk.T("desk.cancel"), () => Back(), "btn-ghost").AddTo(row);
            var sb = Btn(Dk.T("desk.mail.send"), () => SendMail(v), "btn-primary", "btn-big", "dk-primary").AddTo(row);
            bool ok = _template >= 0 && (!needs || _about >= 0) && left > 0 && !Dk.ComputerBlocked(v) && LineClient.IsOfficePhase(v.Phase);
            sb.SetEnabled(ok);
            if (_template < 0) Text(Dk.T("desk.mail.pick_template"), "dk-compose-why").NoPick().AddTo(send);
            else if (needs && _about < 0) Text(Dk.T("desk.mail.pick_about"), "dk-compose-why").NoPick().AddTo(send);

            var rcol = Box("dk-compose-right").AddTo(c);
            Dk.Heading(rcol, Dk.T("desk.mail.compose_template"), Dk.T("desk.mail.compose_template_hint"));
            var scroll = new ScrollView(ScrollViewMode.Vertical).Cls("dk-templates").AddTo(rcol);
            int n = Dk.Config.MailTemplates;
            for (int i = 0; i < n; i++)
            {
                int t = i;
                var tile = Dk.Tile(() => { _template = t; if (!Dk.TemplateMentions(t)) _about = LineIds.None; Build(v, false); }, "dk-template").AddTo(scroll.contentContainer);
                if (t == _template) tile.AddToClassList("dk-template-on");
                Bold(Dk.T("mail.personal." + t + ".subject"), "dk-template-subject").NoPick().AddTo(tile);
                string body = t == _template && _about >= 0 ? Dk.PersonalBody(t, _about) : Dk.F("mail.personal." + t, "___");
                Memo(body, "dk-template-body").NoPick().AddTo(tile);
            }
        }

        static void Chip(VisualElement parent, int id, string label, bool on, Action click)
        {
            var c = Dk.Tile(click, "dk-chip").AddTo(parent);
            if (on) c.AddToClassList("dk-chip-on");
            if (id >= 0) Dk.Badge(id, "badge-xs").AddTo(c);
            else Dk.Icon("mail_app", "dk-chip-icon").AddTo(c);
            Bold(label, "dk-chip-text").NoPick().AddTo(c);
        }

        void SendMail(LineView v)
        {
            if (_template < 0) return;
            Dk.Send(LineVerb.SendMail, _to, _template, Dk.TemplateMentions(_template) ? _about : LineIds.None);
            Dk.Play("mail_drop", "email_ding");
            Ui.Notes.Post(SlipKind.Good, "mail_app", Dk.T("desk.mail.sent"), Dk.F("desk.mail.sent_to", _to < 0 ? Dk.T("desk.mail.everyone") : Dk.Name(_to)));
            _compose = false;
            _template = -1; _about = LineIds.None;
            Build(v, false);
        }
    }

    // ======================================================================== Record

    /// <summary>Sicil / Record: the blame table (highest first) with warnings, today's changes and the favourite.</summary>
    public sealed class DeskRecordApp : DeskAppView
    {
        public DeskRecordApp(DeskUi ui) : base(ui, "dk-app-record") { }
        public override DeskApp Id => DeskApp.Record;
        public override string Icon => "record_app";
        int _sig = int.MinValue;
        readonly List<LinePlayerView> _sorted = new List<LinePlayerView>();

        public override string Subtitle(LineView v) { return Dk.F("desk.record.sub", Dk.DayName(v.Day)); }

        public override void Refresh(LineView v, bool force)
        {
            int sig = v.Favourite + 7;
            for (int i = 0; i < v.Players.Count; i++)
            {
                var p = v.Players[i];
                sig = sig * 31 + p.Blame * 8 + p.Ihtars * 2 + (p.Connected ? 1 : 0);
            }
            sig = sig * 31 + v.TodayReveals.Count;
            if (!force && sig == _sig) return;
            _sig = sig;
            Build(v, force);
        }

        int Today(LineView v, int player)
        {
            int d = 0;
            for (int i = 0; i < v.TodayReveals.Count; i++)
            {
                var ds = v.TodayReveals[i].Deltas;
                for (int k = 0; k < ds.Count; k++) if (ds[k].Player == player) d += ds[k].Delta;
            }
            return d;
        }

        void Build(LineView v, bool animate)
        {
            Root.Clear();
            var page = Box("dk-record").AddTo(Root);
            var head = Box("dk-record-head").NoPick().AddTo(page);
            Dk.Heading(head, Dk.T("desk.record.title"), Dk.F(v.Features.IsLastDay ? "desk.record.hint_last" : "desk.record.hint", Dk.Profile(v).IhtarBlame));
            var cols = Box("dk-record-cols").NoPick().AddTo(page);
            Text(Dk.T("desk.record.col.who"), "dk-record-col", "dk-record-col-who").NoPick().AddTo(cols);
            Text(Dk.T("desk.record.col.blame"), "dk-record-col", "dk-record-col-blame").NoPick().AddTo(cols);
            Text(Dk.T("desk.record.col.ihtar"), "dk-record-col", "dk-record-col-ihtar").NoPick().AddTo(cols);
            Text(Dk.T("desk.record.col.today"), "dk-record-col", "dk-record-col-today").NoPick().AddTo(cols);

            _sorted.Clear();
            _sorted.AddRange(v.Players);
            _sorted.Sort((a, b) => b.Blame != a.Blame ? b.Blame.CompareTo(a.Blame) : b.Ihtars != a.Ihtars ? b.Ihtars.CompareTo(a.Ihtars) : a.Id.CompareTo(b.Id));
            int max = 1;
            for (int i = 0; i < _sorted.Count; i++) if (_sorted[i].Blame > max) max = _sorted[i].Blame;
            for (int i = 0; i < _sorted.Count; i++)
            {
                var p = _sorted[i];
                var row = Box("dk-record-row").NoPick().AddTo(page);
                if (p.Id == v.Me) row.AddToClassList("dk-record-me");
                if (i == 0 && p.Blame > 0) row.AddToClassList("dk-record-top");
                if (!p.Connected) row.AddToClassList("dk-record-away");
                Bold(Num(i + 1), "dk-record-rank").NoPick().AddTo(row);
                Dk.Badge(p.Id, "badge-m").AddTo(row);
                var nc = Box("dk-record-namecol").NoPick().AddTo(row);
                Bold(p.Name, "dk-record-name").NoPick().AddTo(nc);
                var tags = Box("dk-record-tags").NoPick().AddTo(nc);
                if (p.Id == v.Me) Tag(Dk.T("desk.you"), "tag-teal").AddTo(tags);
                if (p.Id == v.Favourite) Tag(Dk.T("desk.record.favourite"), "tag-sticky").AddTo(tags);
                if (!p.Connected) Tag(Dk.T("desk.away"), "tag-grey").AddTo(tags);
                // blame: number + ink blots
                var bcol = Box("dk-record-blame").NoPick().AddTo(row);
                var num = Title(Num(p.Blame), "dk-record-blame-n").NoPick().AddTo(bcol);
                var blots = Box("dk-blots").NoPick().AddTo(bcol);
                int shown = Mathf.Min(p.Blame, 14);
                for (int k = 0; k < shown; k++)
                {
                    var b = Box("dk-blot").NoPick().AddTo(blots);
                    Dk.Art(b, "icons_v3/blame");
                    Dk.SetTilt(b, Dk.Tilt(p.Id * 31 + k, 25f));
                }
                if (p.Blame > shown) Bold("+" + Num(p.Blame - shown), "dk-blot-more").NoPick().AddTo(blots);
                var ih = Box("dk-record-ihtar").NoPick().AddTo(row);
                for (int k = 0; k < p.Ihtars; k++) Dk.Icon("warning_letter", "dk-ihtar-mark").AddTo(ih);
                if (p.Ihtars == 0) Text("–", "dk-record-dash").NoPick().AddTo(ih);
                int today = Today(v, p.Id);
                var td = Bold(today == 0 ? "–" : Dk.Delta(today), "dk-record-today").NoPick().AddTo(row);
                td.AddToClassList(today > 0 ? "dk-bad" : today < 0 ? "dk-good" : "dk-neutral");
                if (animate)
                {
                    UiMotion.SlideIn(row, new Vector2(-30f, 0), 0.26f, 0.04f * i);
                    UiMotion.CountUp(num, 0, p.Blame, 0.5f, Num, 0.1f + 0.04f * i);
                }
            }
            var foot = Box("dk-record-foot").NoPick().AddTo(page);
            Text(Dk.T(Dk.Profile(v).HasFavourite ? "desk.record.foot_fav" : "desk.record.foot"), "dk-record-foot-text").NoPick().AddTo(foot);
        }
    }

    // ======================================================================== Dirty Trick

    /// <summary>Kirli İş / Dirty Trick: this week's card, how it works, and the use flow (target / fake-mail pickers, confirm).</summary>
    public sealed class DeskTrickApp : DeskAppView
    {
        public DeskTrickApp(DeskUi ui) : base(ui, "dk-app-trick") { }
        public override DeskApp Id => DeskApp.Trick;
        public override string Icon => "trick_app";
        int _sig = int.MinValue;
        int _target = LineIds.None, _template = -1, _about = LineIds.None;
        bool _armed;
        float _armedAt;
        Button _go;

        public override string Subtitle(LineView v) { return v.Mine.Trick != TrickType.None ? Dk.TrickName(v.Mine.Trick) : ""; }

        public override int Badge(LineView v)
        {
            return v.Features.TricksUnlocked && !v.Mine.TrickUsedToday && v.Mine.TricksLeftThisWeek > 0 && v.Mine.Trick != TrickType.None && Dk.InRound(v.Phase) ? v.Mine.TricksLeftThisWeek : 0;
        }

        public override void Refresh(LineView v, bool force)
        {
            int sig = (int)v.Mine.Trick;
            sig = sig * 31 + (v.Mine.TrickUsedToday ? 1 : 0) + (v.Features.TricksUnlocked ? 2 : 0) + (Dk.InRound(v.Phase) ? 4 : 0) + (Dk.ComputerBlocked(v) ? 8 : 0) + v.Mine.TricksLeftThisWeek * 16;
            sig = sig * 31 + v.Day;
            for (int i = 0; i < v.Players.Count; i++) sig = sig * 3 + (v.Players[i].Connected ? 1 : 0);
            if (!force && sig == _sig) return;
            _sig = sig;
            Build(v, force);
        }

        public override void Tick(LineView v)
        {
            if (_armed && Time.unscaledTime - _armedAt > 4f) { _armed = false; Build(v, false); }
        }

        void Build(LineView v, bool animate)
        {
            Root.Clear();
            _go = null;
            var t = v.Mine.Trick;
            var page = Box("dk-trick").AddTo(Root);
            var side = Box("dk-trick-side").NoPick().AddTo(page);
            var card = Dk.TrickCard(t, "dk-trick-card").AddTo(side);
            var main = Box("dk-trick-main").AddTo(page);
            StampText(Loc.Upper(Dk.T("desk.trick.band")), "dk-section-band").NoPick().AddTo(main);
            Title(Dk.TrickName(t), "dk-trick-name").NoPick().AddTo(main);
            Text(Dk.TrickHow(t), "dk-trick-how").NoPick().AddTo(main);
            var rules = Box("dk-trick-rules").NoPick().AddTo(main);
            var prof = Dk.Profile(v);
            RuleLine(rules, "timer", Dk.F("desk.trick.allowance", v.Mine.TricksLeftThisWeek, v.Length == WeekLength.Full ? Dk.Config.TricksPerWeekFull : Dk.Config.TricksPerWeekShort));
            RuleLine(rules, "warning", Dk.F("desk.trick.trace", Mathf.RoundToInt(prof.TraceChance * 100f), prof.TrickCaughtBlame));
            RuleLine(rules, "tip_off", Dk.F("desk.trick.tip", Dk.Config.ConfirmedTipBlame));

            var flow = Box("dk-trick-flow").AddTo(main);
            bool unlocked = v.Features.TricksUnlocked;
            if (t == TrickType.None) State(flow, "lock", Dk.T("desk.trick.none"), null);
            else if (!unlocked) State(flow, "lock", Dk.T("desk.trick.locked"), Dk.F("desk.trick.locked_sub", Dk.DayName(Dk.Config.UnlockDayFirstWeek)));
            else if (v.Mine.TricksLeftThisWeek <= 0 && !v.Mine.TrickUsedToday) State(flow, "check", Dk.T("desk.trick.spent"), Dk.T("desk.trick.spent_sub"));
            else if (v.Mine.TrickUsedToday)
            {
                State(flow, "check", Dk.T("desk.trick.used"), Dk.T("desk.trick.used_sub"));
                var st = Dk.Stamp(Dk.T("desk.trick.used_stamp"), null, "dk-trick-used-stamp").AddTo(card);
                if (animate) UiMotion.Stamp(st, -14f, 0.22f, 0.3f);
                else Dk.SetTilt(st, -14f);
            }
            else if (!Dk.InRound(v.Phase)) State(flow, "timer", Dk.T("desk.trick.wait"), Dk.T("desk.trick.wait_sub"));
            else BuildUse(flow, v, t);
            if (animate) { UiMotion.Flip(card, null, 0.45f, 0.05f); UiMotion.SlideIn(main, new Vector2(30f, 0), 0.3f, 0.1f); }
        }

        static void RuleLine(VisualElement parent, string icon, string text)
        {
            var r = Box("dk-rule-line").NoPick().AddTo(parent);
            Dk.Icon(icon, "dk-rule-icon").AddTo(r);
            Text(text, "dk-rule-text").NoPick().AddTo(r);
        }

        static void State(VisualElement parent, string icon, string title, string sub)
        {
            var s = Box("dk-trick-state").NoPick().AddTo(parent);
            Dk.Icon(icon, "dk-trick-state-icon").AddTo(s);
            var c = Box("dk-trick-state-col").NoPick().AddTo(s);
            Bold(title, "dk-trick-state-title").NoPick().AddTo(c);
            if (!string.IsNullOrEmpty(sub)) Text(sub, "dk-trick-state-sub").NoPick().AddTo(c);
        }

        void BuildUse(VisualElement flow, LineView v, TrickType t)
        {
            bool fake = t == TrickType.SahteEposta;
            bool needs = Dk.TrickNeedsTarget(t);
            if (needs)
            {
                Dk.Heading(flow, Dk.T(fake ? "desk.trick.pick_sender" : "desk.trick.pick_target"), Dk.T("trick." + t + ".target"));
                Action<int> pick = id => { _target = id; _armed = false; Build(v, false); };
                var upd = fake ? Dk.ChipPicker(flow, v, pick) : Dk.Picker(flow, v, pick, false, "badge-m", AwayIds(v));
                upd(_target);
            }
            if (fake)
            {
                Dk.Heading(flow, Dk.T("desk.trick.pick_template"));
                var grid = Box("dk-trick-templates").AddTo(flow);
                for (int i = 0; i < Dk.Config.MailTemplates; i++)
                {
                    int k = i;
                    var tile = Dk.Tile(() => { _template = k; if (!Dk.TemplateMentions(k)) _about = LineIds.None; _armed = false; Build(v, false); }, "dk-template", "dk-template-s").AddTo(grid);
                    if (k == _template) tile.AddToClassList("dk-template-on");
                    Bold(Dk.T("mail.personal." + k + ".subject"), "dk-template-subject").NoPick().AddTo(tile);
                }
                if (_template >= 0)
                {
                    if (Dk.TemplateMentions(_template))
                    {
                        Dk.Heading(flow, Dk.T("desk.mail.compose_about"));
                        var upd2 = Dk.ChipPicker(flow, v, id => { _about = id; _armed = false; Build(v, false); }, true);
                        upd2(_about);
                    }
                    var prev = Box("dk-trick-preview").NoPick().AddTo(flow);
                    Text(Dk.F("desk.trick.preview_from", _target >= 0 ? Dk.Name(_target) : "___"), "dk-trick-preview-from").NoPick().AddTo(prev);
                    Memo(_about >= 0 || !Dk.TemplateMentions(_template) ? Dk.PersonalBody(_template, _about) : Dk.F("mail.personal." + _template, "___"), "dk-trick-preview-body").NoPick().AddTo(prev);
                }
            }
            var foot = Box("dk-trick-foot").AddTo(flow);
            Box("spacer").NoPick().AddTo(foot);
            bool ok = (!needs || _target >= 0) && (!fake || (_template >= 0 && (!Dk.TemplateMentions(_template) || _about >= 0))) && !Dk.ComputerBlocked(v);
            _go = Btn(Dk.T(_armed ? "desk.trick.confirm" : "desk.trick.use"), () => Go(v, t), "btn-danger", "btn-big", "dk-primary").AddTo(foot);
            _go.SetEnabled(ok);
            if (_armed) UiMotion.Shake(_go, 5f, 0.3f);
        }

        static int[] AwayIds(LineView v)
        {
            int n = 0;
            for (int i = 0; i < v.Players.Count; i++) if (!v.Players[i].Connected) n++;
            if (n == 0) return null;
            var ids = new int[n];
            n = 0;
            for (int i = 0; i < v.Players.Count; i++) if (!v.Players[i].Connected) ids[n++] = v.Players[i].Id;
            return ids;
        }

        void Go(LineView v, TrickType t)
        {
            if (!_armed) { _armed = true; _armedAt = Time.unscaledTime; Build(v, false); return; }
            _armed = false;
            bool needs = Dk.TrickNeedsTarget(t);
            Dk.Send(LineVerb.UseTrick, needs ? _target : LineIds.None, t == TrickType.SahteEposta ? _template : -1,
                t == TrickType.SahteEposta && Dk.TemplateMentions(_template) ? _about : -1);
            _target = LineIds.None; _template = -1; _about = LineIds.None;
        }

        /// <summary>UI mock: pre-fill the use flow.</summary>
        internal void MockPick(int target, int template, int about, bool armed)
        {
            _target = target; _template = template; _about = about; _armed = armed; _armedAt = Time.unscaledTime + 60f;
            var v = Dk.View;
            if (v != null) Build(v, false);
        }

        public override bool Back()
        {
            if (_armed) { _armed = false; var v = Dk.View; if (v != null) Build(v, false); return true; }
            return false;
        }
    }

    // ======================================================================== Agenda

    /// <summary>Ajanda / Agenda: the week calendar (today's rounds, the final day) and your hidden agenda target.</summary>
    public sealed class DeskAgendaApp : DeskAppView
    {
        public DeskAgendaApp(DeskUi ui) : base(ui, "dk-app-agenda") { }
        public override DeskApp Id => DeskApp.Agenda;
        public override string Icon => "agenda_app";
        int _sig = int.MinValue;

        public override string Subtitle(LineView v) { return Dk.F("desk.agenda.sub", v.Day + 1, v.DayCount); }

        public override void Refresh(LineView v, bool force)
        {
            int sig = v.Day * 1000 + v.DayCount * 100 + v.RoundIndex * 10 + (int)v.Phase;
            sig = sig * 31 + v.Mine.AgendaTarget;
            sig = sig * 31 + v.TodayRounds.Count;
            if (!force && sig == _sig) return;
            _sig = sig;
            Build(v, force);
        }

        void Build(LineView v, bool animate)
        {
            Root.Clear();
            var page = Box("dk-agenda").AddTo(Root);
            var week = Box("dk-week").NoPick().AddTo(page);
            for (int d = 0; d < v.DayCount; d++)
            {
                var col = Box("dk-day").NoPick().AddTo(week);
                bool past = d < v.Day, today = d == v.Day, last = d == v.DayCount - 1;
                if (past) col.AddToClassList("dk-day-past");
                if (today) col.AddToClassList("dk-day-today");
                if (last) col.AddToClassList("dk-day-last");
                var head = Box("dk-day-head").NoPick().AddTo(col);
                StampText(Loc.Upper(Dk.DayName(d)), "dk-day-name").NoPick().AddTo(head);
                var body = Box("dk-day-body").NoPick().AddTo(col);
                if (today)
                {
                    for (int i = 0; i < v.TodayRounds.Count; i++)
                    {
                        var item = Box("dk-day-item").NoPick().AddTo(body);
                        bool done = v.RoundIndex > i || (v.RoundIndex == i && v.Phase == LinePhase.RoundReveal);
                        bool now = v.RoundIndex == i && !done;
                        if (done) item.AddToClassList("dk-day-item-done");
                        if (now) item.AddToClassList("dk-day-item-now");
                        Dk.Icon(done ? "sticky_check" : "sticky_todo", "dk-day-item-icon").AddTo(item);
                        Bold(Dk.RoundName(v.TodayRounds[i]), "dk-day-item-text").NoPick().AddTo(item);
                    }
                    var meet = Box("dk-day-item").NoPick().AddTo(body);
                    Dk.Icon("meeting", "dk-day-item-icon").AddTo(meet);
                    Bold(Dk.T(last ? "desk.agenda.final" : "desk.agenda.meeting"), "dk-day-item-text").NoPick().AddTo(meet);
                }
                else if (past)
                {
                    var s = Dk.Stamp(Dk.T("desk.agenda.done"), null, "dk-day-stamp").AddTo(body);
                    Dk.SetTilt(s, Dk.Tilt(d, 8f) - 8f);
                }
                else
                {
                    Text(Dk.T(last ? "desk.agenda.future_last" : "desk.agenda.future"), "dk-day-future").NoPick().AddTo(body);
                }
                if (last && !past)
                {
                    var f = Box("dk-day-final").NoPick().AddTo(col);
                    Dk.Icon("defense", "dk-day-final-icon").AddTo(f);
                    Bold(Dk.T("desk.agenda.firing"), "dk-day-final-text").NoPick().AddTo(f);
                }
                if (animate) UiMotion.SlideIn(col, new Vector2(0, 30f), 0.26f, 0.05f * d);
            }

            // hidden agenda: a confidential folder
            var folder = Box("dk-agenda-folder").NoPick().AddTo(page);
            var fl = Box("dk-agenda-folder-left").NoPick().AddTo(folder);
            StampText(Loc.Upper(Dk.T("desk.agenda.hidden")), "dk-section-band").NoPick().AddTo(fl);
            var trow = Box("dk-agenda-titlerow").NoPick().AddTo(fl);
            var crosshair = Dk.Icon("agenda_target", "dk-agenda-cross").AddTo(trow);
            Title(Dk.T("desk.agenda.target_title"), "dk-agenda-title").NoPick().AddTo(trow);
            Text(Dk.F("desk.agenda.target_text", Dk.Config.AgendaPointsPerIhtar, Dk.Config.AgendaPoints), "dk-agenda-text").NoPick().AddTo(fl);
            Text(Dk.T("desk.agenda.target_hint"), "dk-agenda-hint").NoPick().AddTo(fl);
            var target = Box("dk-agenda-target").NoPick().AddTo(folder);
            Dk.Badge(v.Mine.AgendaTarget, "badge-xl").AddTo(target);
            Title(v.Mine.AgendaTarget >= 0 ? Dk.Name(v.Mine.AgendaTarget) : "?", "dk-agenda-name").NoPick().AddTo(target);
            var stamp = Dk.Stamp(Dk.T("desk.agenda.confidential"), Loc.Language == "tr" ? "stamps/stamp_gizli" : "stamps_v3/stamp_confidential", "dk-agenda-stamp").AddTo(folder);
            if (animate)
            {
                UiMotion.SlideIn(folder, new Vector2(0, 40f), 0.3f, 0.2f);
                UiMotion.Stamp(stamp, -8f, 0.24f, 0.7f, () => Dk.Play("stamp", null, 0.7f));
                UiMotion.Pulse(crosshair, 0.15f, 0.4f);
            }
            else Dk.SetTilt(stamp, -8f);
        }
    }
}
