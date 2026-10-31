using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// WeekResults (v4): the end-of-match report on the desk. Company outcome first (productivity vs target; a closed
    /// company is a shared defeat: no winner, no ranks, but contributions, objectives and penalties stay visible), then per
    /// player the itemised career lines, shared ranks, Employee of the Month, objectives, exposed incidents only, money and
    /// contribution quality; the session board; the fired player's next-manager pick; "Play again".
    /// Wording never claims the match was fair or fun.
    /// </summary>
    public sealed class WeekResultsView : MeetingPanel
    {
        static int _promptedWeek = int.MinValue;

        readonly VisualElement _sheet, _banner, _rows, _side, _session, _eotm, _incidents, _next, _actions, _rematchRow, _pickLayer, _pickCards;
        readonly Label _title, _sub, _timer, _waiting;
        readonly ScrollView _scroll;
        Button _again;
        int _lastSec = -1, _builtWeek = -1;
        string _shownPick;
        bool _host, _picked, _wantsRematch;
        int _rematchKey = int.MinValue;

        public WeekResultsView(VisualElement parent) : base(parent, "mt-results")
        {
            Root.pickingMode = PickingMode.Position;
            _sheet = Box("wr-sheet").NoPick().AddTo(Root);
            var head = Box("wr-head").NoPick().AddTo(_sheet);
            var hc = Box("col", "grow").NoPick().AddTo(head);
            _title = Title("", "wr-title").NoPick().AddTo(hc);
            _sub = Bold("", "wr-sub").NoPick().AddTo(hc);
            var conf = Box("wr-confidential").NoPick().AddTo(head);
            if (!UiArt.Apply(conf, "stamps_v3/stamp_confidential")) conf.RemoveFromHierarchy();

            _banner = Box("wr4-banner").NoPick().AddTo(_sheet);

            var cols = Box("wr-cols").NoPick().AddTo(_sheet);
            _scroll = new ScrollView().Cls("wr4-scroll").AddTo(cols);
            _rows = _scroll.contentContainer;
            _side = new ScrollView().Cls("wr-side", "wr4-side").AddTo(cols);
            _session = Box("wr-session").NoPick().AddTo(_side.contentContainer);
            _eotm = Box("wr4-eotm").NoPick().AddTo(_side.contentContainer);
            _incidents = Box("wr4-incidents").NoPick().AddTo(_side.contentContainer);
            _next = Box("wr-next").NoPick().AddTo(_side.contentContainer);

            var foot = Box("wr-foot").AddTo(_sheet);
            _timer = Bold("", "wr-timer").NoPick().AddTo(foot);
            Box("spacer").NoPick().AddTo(foot);
            _rematchRow = Box("wr4-rematch").NoPick().AddTo(foot);
            _waiting = Bold(Loc.T("results.waiting_host"), "wr-waiting").NoPick().AddTo(foot);
            _actions = Box("row").AddTo(foot);

            // the fired player picks next match's manager (revenge overlay)
            _pickLayer = Box("pick-layer").AddTo(Root);
            var card = Box("pick-card").AddTo(_pickLayer);
            StampText(Loc.T("results.pick.title"), "pick-title").NoPick().AddTo(card);
            Bold(Loc.T("results.pick.sub"), "pick-sub").NoPick().AddTo(card);
            _pickCards = Box("pick-cards").AddTo(card);
            Text(Loc.T("results.pick.note"), "pick-note").NoPick().AddTo(card);
            _pickLayer.Show(false);
        }

        protected override void OnShow(LineView v)
        {
            UiArt.Apply(Root, "backgrounds/results_bg");
            _builtWeek = -1;
            _lastSec = -1;
            _shownPick = null;
            _picked = false;
            _wantsRematch = false;
            _rematchKey = int.MinValue;
            Build(v);
        }

        protected override void OnHide()
        {
            Root.style.backgroundImage = StyleKeyword.Null;
            UiArt.Release("backgrounds/results_bg");
        }

        public override void Refresh(LineView v)
        {
            if (!Visible) return;
            if (v.Result != null && v.WeekId != _builtWeek) Build(v);
            else if (v.Result != null && (v.Result.NextManagerPick ?? "") != (_shownPick ?? "")) BuildNext(v, true);
        }

        void Build(LineView v)
        {
            var r = v.Result;
            _rows.Clear();
            _session.Clear();
            _eotm.Clear();
            _incidents.Clear();
            _banner.Clear();
            _actions.Clear();
            _host = HostLineController.Current != null;
            _waiting.Show(!_host);
            if (_host)
            {
                Btn(Loc.T("results.to_lobby"), () => HostLineController.Current?.ReturnToLobby(), "btn-ghost").AddTo(_actions);
                IconBtn("week_calendar", Loc.T("results.next_week"), () => HostLineController.Current?.ContinueAfterResults(), "btn-primary", "btn-big").AddTo(_actions);
            }
            if (r == null)
            {
                _title.text = Loc.T("results.title");
                _sub.text = Loc.T("results.computing");
                return;
            }
            _builtWeek = v.WeekId;
            bool closed = r.CompanyClosed;
            _title.text = Loc.T(closed ? "results.title_closed" : "results.title");
            _sub.text = Loc.F("results.sub", Loc.ManagerName(r.ManagerProfileId), r.Days);
            if (r.Players.Count >= LineProtocol.MinPlayers && _promptedWeek != v.WeekId)
            {
                _promptedWeek = v.WeekId;
                TelemetryHooks.RematchPromptShown();
            }

            BuildBanner(v, r);
            BuildRematch(v, r);

            var cfg = MeetingText.Config;
            var players = new List<LineWeekPlayerResult>(r.Players);
            if (closed) players.Sort((a, b) => b.CareerTotal != a.CareerTotal ? b.CareerTotal.CompareTo(a.CareerTotal) : a.Player.CompareTo(b.Player));
            else players.Sort((a, b) => a.Rank != b.Rank ? (a.Rank == 0 ? 1 : b.Rank == 0 ? -1 : a.Rank.CompareTo(b.Rank)) : a.Player.CompareTo(b.Player));
            int n = 0;
            foreach (var p in players) { BuildPlayerRow(v, r, p, n); n++; }

            BuildSession(r);
            BuildEotm(v, r);
            BuildIncidents(r);
            BuildNext(v, false);
            UiMotion.SlideIn(_sheet, new Vector2(0f, 60f), 0.4f);
            UiMotion.SlideIn(_side, new Vector2(60f, 0f), 0.4f, 0.8f);
        }

        // ------------------------------------------------------------------ company banner

        void BuildBanner(LineView v, LineWeekResult r)
        {
            int target = r.TargetPercent > 0 ? r.TargetPercent : MeetingText.Config.CompanyTargetPercent;
            bool closed = r.CompanyClosed;
            _banner.EnableInClassList("wr4-banner-closed", closed);
            var left = Box("col").NoPick().AddTo(_banner);
            Memo(Loc.T("results.company"), "wr4-banner-label").NoPick().AddTo(left);
            Title(MeetingText.Percent(r.FinalProductivityPermille), "wr4-banner-big", closed ? "company-bad" : "company-good").NoPick().AddTo(left);
            Bold(Loc.F("company.vs_target", target), "company-target").NoPick().AddTo(left);
            var mid = Box("col", "grow", "wr4-banner-mid").NoPick().AddTo(_banner);
            DossierDeck.Gauge(mid, r.FinalProductivityPermille, target, closed, "wr4-gauge");
            Bold(Loc.T(closed ? "results.closed.headline" : "results.survived.headline"), "wr4-headline", closed ? "company-bad" : "company-good").NoPick().AddTo(mid);
            string line;
            if (closed) line = Loc.T("results.closed.body");
            else if (r.Fired >= 0) line = Loc.F("results.fired.line", MeetingText.Name(r.Fired));
            else line = Loc.T("results.nobody.line");
            Text(line, "wr4-line").NoPick().AddTo(mid);
            var st = Box("wr4-banner-stamp").NoPick().AddTo(_banner);
            if (closed)
            {
                TextStamp(st, Loc.T("final.stamp.closed"));
            }
            else if (r.Fired >= 0)
            {
                if (!MeetingText.ApplyStamp(st, "stamps/stamp_kovuldu", "stamps_v3/stamp_fired")) TextStamp(st, Loc.T("final.stamp.fired"));
            }
            else TextStamp(st, Loc.T("final.stamp.none"), "stamp-navy");
            UiMotion.Stamp(st, -8f, 0.22f, 0.5f, () => Sfx.Play(closed ? "fired" : "place", 0.7f));
        }

        // ------------------------------------------------------------------ player rows

        static string ObjectiveName(LineWeekPlayerResult p) { return p.Objective == ObjectiveKind.None ? "-" : Loc.T("objective.v4." + p.Objective + ".name"); }

        void BuildPlayerRow(LineView v, LineWeekResult r, LineWeekPlayerResult p, int n)
        {
            float delay = 0.25f + n * 0.2f;
            bool closed = r.CompanyClosed;
            var row = Box("wr4-row").NoPick().AddTo(_rows);
            if (p.Player == v.Me) row.AddToClassList("wr-row-me");
            if (p.Fired || closed) row.AddToClassList("wr-row-fired");

            // left: rank + identity
            var rank = Title(p.Rank > 0 ? p.Rank.ToString() : "-", "wr4-rank").NoPick().AddTo(row);
            if (p.Rank == 1) rank.AddToClassList("wr4-rank-first");
            var who = Box("wr4-who").NoPick().AddTo(row);
            Badge(p.Player, "badge-m").AddTo(who);
            var nc = Box("col").NoPick().AddTo(who);
            Title(MeetingText.Name(p.Player), "wr-name").NoPick().AddTo(nc);
            var tags = Box("row", "wr4-tags").NoPick().AddTo(nc);
            if (p.Fired && !closed) Tag(Loc.T("results.tag.fired"), "tag-red").AddTo(tags);
            if (closed) Tag(Loc.T("results.tag.fired_closed"), "tag-red").AddTo(tags);
            if (p.EmployeeOfMonth) Tag(Loc.T("results.tag.eotm"), "tag-sticky").AddTo(tags);
            if (p.Warnings > 0) Tag(Loc.F("results.tag.warnings", p.Warnings), "tag-grey").AddTo(tags);

            // middle: objective + facts
            var mid = Box("wr4-mid").NoPick().AddTo(row);
            var obj = Box("row").NoPick().AddTo(mid);
            MeetingText.ApplyIcon(Box("icon", "icon-s").NoPick().AddTo(obj), "icons_v3/agenda_target");
            Bold(ObjectiveName(p), "wr-agenda-name").NoPick().AddTo(obj);
            if (p.ObjectiveTarget >= 0) { Badge(p.ObjectiveTarget, "badge-xs").AddTo(obj); Text(MeetingText.Name(p.ObjectiveTarget), "wr-small").NoPick().AddTo(obj); }
            Tag(Loc.T(p.ObjectiveDone ? "results.objective.done" : "results.objective.missed"), p.ObjectiveDone ? "tag-green" : "tag-grey").AddTo(obj);
            var facts = Box("row", "wr4-facts").NoPick().AddTo(mid);
            Text(Loc.F("results.fact.money", MeetingText.Money(p.Money)), "wr-small").NoPick().AddTo(facts);
            Text(p.ContributionQuality >= 0 ? Loc.F("results.fact.quality", p.ContributionQuality, p.TasksConnected) : Loc.T("results.fact.quality_none"), "wr-small").NoPick().AddTo(facts);
            Text(Loc.F("results.fact.blame", p.Blame), "wr-small").NoPick().AddTo(facts);
            if (p.ExposedIncidents > 0) Tag(Loc.F("results.fact.exposed", p.ExposedIncidents), "tag-red").AddTo(facts);
            UiMotion.Flip(mid, null, 0.4f, delay + 0.1f);

            // right: career lines + total
            var career = Box("wr4-career").NoPick().AddTo(row);
            var chips = Box("wr-chips").NoPick().AddTo(career);
            int k = 0;
            foreach (var cl in p.Career)
            {
                string label = Loc.T("career.item." + cl.Item) + (cl.Count > 1 ? " ×" + cl.Count : "");
                CareerChip(chips, label, cl.Points, delay + 0.3f + k * 0.08f);
                k++;
            }
            if (p.Career.Count == 0) Text(Loc.T(closed ? "results.career.closed" : "results.career.none"), "wr-small", "wr4-nocareer").NoPick().AddTo(chips);
            var total = Title("0", "wr-total", "wr4-total").NoPick().AddTo(row);
            UiMotion.CountUp(total, 0, p.CareerTotal, 0.6f, x => (x > 0 ? "+" : "") + x, delay + 0.4f);
            UiMotion.SlideIn(row, new Vector2(0f, 24f), 0.3f, delay);
        }

        static void CareerChip(VisualElement parent, string label, int pts, float delay)
        {
            var c = Box("wr-chip", pts < 0 ? "wr-chip-bad" : "").NoPick().AddTo(parent);
            Text(label, "wr-chip-label").NoPick().AddTo(c);
            Bold((pts > 0 ? "+" : "") + pts, "wr-chip-num").NoPick().AddTo(c);
            UiMotion.PopIn(c, 0.22f, delay, 0.6f);
        }

        // ------------------------------------------------------------------ side column

        /// <summary>Competition ranks over a list already sorted by points (descending).</summary>
        static int[] Ranks(List<LineWeekPlayerResult> sorted, System.Func<LineWeekPlayerResult, int> score)
        {
            var ranks = new int[sorted.Count];
            for (int i = 0; i < sorted.Count; i++) ranks[i] = i > 0 && score(sorted[i]) == score(sorted[i - 1]) ? ranks[i - 1] : i + 1;
            return ranks;
        }

        void BuildSession(LineWeekResult r)
        {
            Title(Loc.T("results.session.title"), "wr-session-title").NoPick().AddTo(_session);
            Memo(Loc.T("results.session.sub"), "wr-session-sub").NoPick().AddTo(_session);
            var board = new List<LineWeekPlayerResult>(r.Players);
            board.Sort((a, b) => b.SessionPoints != a.SessionPoints ? b.SessionPoints.CompareTo(a.SessionPoints) : a.Player.CompareTo(b.Player));
            var ranks = Ranks(board, x => x.SessionPoints);
            int top = board.Count > 0 ? board[0].SessionPoints : 0;
            for (int i = 0; i < board.Count; i++)
            {
                var p = board[i];
                var row = Box("wr-srow").NoPick().AddTo(_session);
                if (p.SessionPoints == top && top > 0) row.AddToClassList("wr-srow-lead");
                Title(ranks[i] + ".", "wr-srank").NoPick().AddTo(row);
                Badge(p.Player, "badge-s").AddTo(row);
                Bold(MeetingText.Name(p.Player), "wr-sname").NoPick().AddTo(row);
                var pts = Title("", "wr-spts").NoPick().AddTo(row);
                UiMotion.CountUp(pts, p.SessionPoints - p.CareerTotal, p.SessionPoints, 0.7f, x => x.ToString(), 1.2f + i * 0.1f);
            }
            if (r.CompanyClosed) Text(Loc.T("results.session.closed_note"), "wr-session-note").NoPick().AddTo(_session);
        }

        void BuildEotm(LineView v, LineWeekResult r)
        {
            Bold(Loc.T("results.eotm.title"), "wr-next-title").NoPick().AddTo(_eotm);
            if (r.CompanyClosed) { Text(Loc.T("results.eotm.closed"), "wr-small").NoPick().AddTo(_eotm); return; }
            LineWeekPlayerResult w = null;
            foreach (var p in r.Players) if (p.EmployeeOfMonth) w = p;
            if (w == null) { Text(Loc.T("results.eotm.none"), "wr-small").NoPick().AddTo(_eotm); return; }
            var row = Box("row").NoPick().AddTo(_eotm);
            Badge(w.Player, "badge-m").AddTo(row);
            var col = Box("col", "grow").NoPick().AddTo(row);
            Title(MeetingText.Name(w.Player), "wr-next-name").NoPick().AddTo(col);
            Text(Loc.F("results.eotm.line", MeetingText.Config.CareerEmployeeOfMonth), "wr-small").NoPick().AddTo(col);
        }

        void BuildIncidents(LineWeekResult r)
        {
            Bold(Loc.T("results.incidents.title"), "wr-next-title").NoPick().AddTo(_incidents);
            Text(Loc.F("results.incidents.count", r.IncidentsTotal, r.Exposed.Count), "wr-small").NoPick().AddTo(_incidents);
            if (r.Exposed.Count == 0) { Text(Loc.T("results.incidents.none_exposed"), "wr-small", "t-muted").NoPick().AddTo(_incidents); return; }
            foreach (var e in r.Exposed)
            {
                var row = Box("wr4-inc").NoPick().AddTo(_incidents);
                if (e.Actor >= 0) Badge(e.Actor, "badge-xs").AddTo(row);
                var col = Box("col", "grow").NoPick().AddTo(row);
                Bold(MeetingText.Incident(e.Kind), "wr-small").NoPick().AddTo(col);
                Text(Loc.F("results.incidents.line", MeetingText.Name(e.Actor), MeetingText.Day(e.Day), e.ExposedBy >= 0 ? MeetingText.Name(e.ExposedBy) : "-"), "wr-small", "t-muted").NoPick().AddTo(col);
            }
        }

        // ------------------------------------------------------------------ rematch

        void BuildRematch(LineView v, LineWeekResult r)
        {
            _rematchRow.Clear();
            _again = null;
            _againLabelOn = false;
            _again = Btn(Loc.T("results.play_again"), ToggleRematch, "btn-primary").AddTo(_rematchRow);
            _rematchRow.pickingMode = PickingMode.Position;
            _rematchKey = int.MinValue;
            _wantsRematch = false;
            UpdateRematch();
        }

        bool _againLabelOn;
        VisualElement _rematchBadges;

        void ToggleRematch()
        {
            _wantsRematch = !_wantsRematch;
            var c = LineClient.I;
            if (c != null) c.RequestRematch(_wantsRematch);
            if (_wantsRematch) TelemetryHooks.RematchRequested();
            _rematchKey = int.MinValue;
            UpdateRematch();
        }

        void UpdateRematch()
        {
            if (_again == null) return;
            var lobby = LineClient.I != null ? LineClient.I.Lobby : null;
            int key = (_wantsRematch ? 1 : 0);
            if (lobby != null) foreach (var s in lobby.Slots) key = key * 3 + (s.Occupied && s.WantsRematch ? s.Slot + 1 : 0);
            if (key == _rematchKey) return;
            _rematchKey = key;
            _again.text = Loc.T(_wantsRematch ? "results.rematch_cancel" : "results.play_again");
            _again.EnableInClassList("btn-primary", !_wantsRematch);
            _again.EnableInClassList("btn-secondary", _wantsRematch);
            if (_rematchBadges != null) _rematchBadges.RemoveFromHierarchy();
            _rematchBadges = Box("row", "wr4-rematch-badges").NoPick().AddTo(_rematchRow);
            int want = 0;
            if (lobby != null)
                foreach (var s in lobby.Slots)
                    if (s.Occupied && s.WantsRematch) { Badge(s.Slot, "badge-xs").AddTo(_rematchBadges); want++; }
            if (want > 0) Text(Loc.F("results.rematch_count", want), "wr-small").NoPick().AddTo(_rematchBadges);
        }

        // ------------------------------------------------------------------ next manager

        void BuildNext(LineView v, bool animate)
        {
            var r = v.Result;
            _next.Clear();
            if (r == null || r.Fired < 0 || r.CompanyClosed) { _next.Show(false); _pickLayer.Show(false); return; }
            _next.Show(true);
            string pick = r.NextManagerPick ?? "";
            _shownPick = pick;
            Bold(Loc.T("results.next.title"), "wr-next-title").NoPick().AddTo(_next);
            if (pick.Length > 0)
            {
                var row = Box("row").NoPick().AddTo(_next);
                ManagerPortrait(pick, true, "badge-l").AddTo(row);
                var col = Box("col", "grow").NoPick().AddTo(row);
                Title(Loc.ManagerName(pick), "wr-next-name").NoPick().AddTo(col);
                Text(Loc.F("results.next.picked_by", MeetingText.Name(r.Fired)), "wr-small").NoPick().AddTo(col);
                if (animate)
                {
                    UiMotion.Flip(_next, null, 0.4f);
                    Sfx.Play("evidence", 0.6f);
                }
                if (_pickLayer.IsShown()) _pickLayer.schedule.Execute(() => UiMotion.FadeOut(_pickLayer, 0.25f)).StartingIn(900);
                return;
            }
            var faces = Box("row", "wr-next-faces").NoPick().AddTo(_next);
            foreach (var id in LineManagerIds.All)
            {
                if (id == r.ManagerProfileId) continue;
                var b = ManagerPortrait(id, false, "badge-s").AddTo(faces);
                b.AddToClassList("wr-next-face");
            }
            Text(r.Fired == v.Me ? Loc.T("results.next.you_pick") : Loc.F("results.next.picking", MeetingText.Name(r.Fired)), "wr-small").NoPick().AddTo(_next);
            if (r.Fired == v.Me && !_picked) ShowPicker(v);
        }

        void ShowPicker(LineView v)
        {
            var r = v.Result;
            _pickCards.Clear();
            int n = 0;
            for (int i = 0; i < LineManagerIds.All.Length; i++)
            {
                string id = LineManagerIds.All[i];
                if (id == r.ManagerProfileId) continue;
                int index = i;
                var card = Box("pick-option").AddTo(_pickCards);
                ManagerPortrait(id, true, "badge-xl").AddTo(card);
                Title(Loc.ManagerName(id), "pick-name").NoPick().AddTo(card);
                Bold(Loc.ManagerTitle(id), "pick-mtitle").NoPick().AddTo(card);
                Text(Loc.T("results.pick.pitch." + id), "pick-pitch").NoPick().AddTo(card);
                var stamp = Box("pick-stamp").NoPick().AddTo(card);
                stamp.style.opacity = 0f;
                card.RegisterCallback<ClickEvent>(_ => Pick(index, card, stamp));
                UiMotion.PopIn(card, 0.32f, 0.25f + n * 0.12f, 0.8f);
                n++;
            }
            _pickLayer.Show(true);
            _pickLayer.BringToFront();
            UiMotion.FadeIn(_pickLayer, 0.3f);
        }

        void Pick(int index, VisualElement card, VisualElement stamp)
        {
            if (_picked) return;
            _picked = true;
            LineClient.I?.Send(LineVerb.PickNextManager, LineIds.None, index);
            foreach (var c in _pickCards.Children()) c.EnableInClassList("pick-option-dim", c != card);
            card.AddToClassList("pick-option-on");
            if (!MeetingText.ApplyStamp(stamp, "stamps/stamp_onaylandi", "stamps_v3/stamp_approved")) TextStamp(stamp, Loc.T("results.pick.stamp"), "stamp-green");
            UiMotion.Stamp(stamp, -10f, 0.22f, 0f, () => Sfx.Play("evidence", 0.8f));
            _pickLayer.schedule.Execute(() => { if (_pickLayer.IsShown()) UiMotion.FadeOut(_pickLayer, 0.3f); }).StartingIn(1600);
        }

        public override void Tick(LineView v, double now)
        {
            UpdateRematch();
            int sec = (int)System.Math.Ceiling(System.Math.Max(0, v.PhaseEndTime - now));
            if (sec == _lastSec) return;
            _lastSec = sec;
            _timer.text = Loc.F("results.auto_continue", ClockInt(sec));
        }
    }
}
