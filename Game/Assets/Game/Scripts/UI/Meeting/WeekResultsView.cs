using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// WeekResults: the end-of-week report on the desk — per player: survived / fired, the hidden agenda revealed
    /// (target and hit/miss), the dirty trick revealed, points counting up, the Employee of the Week stamp; the session
    /// board; host buttons (next week / back to lobby) or "waiting for the host".
    /// </summary>
    public sealed class WeekResultsView : MeetingPanel
    {
        readonly VisualElement _sheet, _rows, _side, _session, _next, _actions, _pickLayer, _pickCards;
        readonly Label _title, _sub, _timer, _waiting;
        int _lastSec = -1, _builtWeek = -1;
        string _shownPick;
        bool _host, _picked;

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

            var cols = Box("wr-cols").NoPick().AddTo(_sheet);
            var left = Box("wr-table").NoPick().AddTo(cols);
            var hdr = Box("wr-row", "wr-row-head").NoPick().AddTo(left);
            Text(Loc.T("results.col.employee"), "wr-c-name", "wr-h").NoPick().AddTo(hdr);
            Text(Loc.T("results.col.agenda"), "wr-c-agenda", "wr-h").NoPick().AddTo(hdr);
            Text(Loc.T("results.col.trick"), "wr-c-trick", "wr-h").NoPick().AddTo(hdr);
            Text(Loc.T("results.col.points"), "wr-c-pts", "wr-h").NoPick().AddTo(hdr);
            Box("wr-c-total").NoPick().AddTo(hdr);
            Box("wr-c-stamp").NoPick().AddTo(hdr);
            _rows = Box("col").NoPick().AddTo(left);

            _side = Box("wr-side").NoPick().AddTo(cols);
            _session = Box("wr-session").NoPick().AddTo(_side);
            _next = Box("wr-next").NoPick().AddTo(_side);

            var foot = Box("wr-foot").AddTo(_sheet);
            _timer = Bold("", "wr-timer").NoPick().AddTo(foot);
            Box("spacer").NoPick().AddTo(foot);
            _waiting = Bold(Loc.T("results.waiting_host"), "wr-waiting").NoPick().AddTo(foot);
            _actions = Box("row").AddTo(foot);

            // v3.1: the fired player picks next week's manager (revenge overlay)
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
            _title.text = Loc.T("results.title");
            _sub.text = Loc.F("results.sub", MeetingText.Name(r.Fired), Loc.ManagerName(r.ManagerProfileId), r.Days);

            var players = new List<LineWeekPlayerResult>(r.Players);
            players.Sort((a, b) => b.Points != a.Points ? b.Points.CompareTo(a.Points) : a.Blame != b.Blame ? a.Blame.CompareTo(b.Blame) : a.Player.CompareTo(b.Player));
            var cfg = MeetingText.Config;
            int n = 0;
            foreach (var p in players)
            {
                float delay = 0.25f + n * 0.22f;
                var row = Box("wr-row").NoPick().AddTo(_rows);
                if (p.Player == v.Me) row.AddToClassList("wr-row-me");
                if (!p.Survived) row.AddToClassList("wr-row-fired");

                // name + status
                var name = Box("wr-c-name", "row").NoPick().AddTo(row);
                Badge(p.Player, "badge-m").AddTo(name);
                var nc = Box("col").NoPick().AddTo(name);
                Title(MeetingText.Name(p.Player), "wr-name").NoPick().AddTo(nc);
                var line = Box("row").NoPick().AddTo(nc);
                Text(Loc.F("meeting.blame_n", p.Blame), "wr-small").NoPick().AddTo(line);
                for (int k = 0; k < p.Ihtars; k++) DossierDeck.IhtarMark(line);

                // hidden agenda: target + hit/miss (the reveal flips in)
                var ag = Box("wr-c-agenda").NoPick().AddTo(row);
                var agRow = Box("row").NoPick().AddTo(ag);
                MeetingText.ApplyIcon(Box("icon", "icon-s").NoPick().AddTo(agRow), "icons_v3/agenda_target");
                if (p.AgendaTarget >= 0) Badge(p.AgendaTarget, "badge-xs").AddTo(agRow);
                Bold(p.AgendaTarget >= 0 ? MeetingText.Name(p.AgendaTarget) : Loc.T("common.nobody"), "wr-agenda-name").NoPick().AddTo(agRow);
                bool targetFired = p.AgendaTarget >= 0 && p.AgendaTarget == r.Fired;
                int agendaPts = cfg.AgendaPointsPerIhtar * p.AgendaIhtars + (targetFired ? cfg.AgendaPoints : 0);
                var agTags = Box("row", "wr-agenda-tags").NoPick().AddTo(ag);
                if (targetFired) Tag(Loc.T("results.agenda_fired"), "tag-green").AddTo(agTags);
                if (p.AgendaIhtars > 0) Tag(Loc.F("results.agenda_ihtars", p.AgendaIhtars), "tag-green").AddTo(agTags);
                if (!targetFired && p.AgendaIhtars == 0) Tag(Loc.T("results.agenda_miss"), "tag-grey").AddTo(agTags);
                UiMotion.Flip(ag, null, 0.4f, delay + 0.1f);

                // dirty trick
                var tr = Box("wr-c-trick").NoPick().AddTo(row);
                var trRow = Box("row").NoPick().AddTo(tr);
                MeetingText.ApplyIcon(Box("icon", "icon-s").NoPick().AddTo(trRow), MeetingText.TrickIcon(p.Trick));
                Bold(p.Trick != TrickType.None ? MeetingText.Trick(p.Trick) : "–", "wr-trick-name").NoPick().AddTo(trRow);
                Text(p.TricksUsed > 0 ? Loc.F("results.trick_used", p.TricksUsed) : Loc.T("results.trick_unused"), "wr-small").NoPick().AddTo(tr);
                UiMotion.Flip(tr, null, 0.4f, delay + 0.2f);

                // points: breakdown chips + total counting up
                var pts = Box("wr-c-pts").NoPick().AddTo(row);
                var chips = Box("wr-chips").NoPick().AddTo(pts);
                if (p.Survived) PointChip(chips, Loc.T("results.pt.survived"), cfg.SurvivePoints, delay + 0.3f);
                else PointChip(chips, Loc.T("results.pt.fired"), 0, delay + 0.3f, true);
                if (agendaPts > 0) PointChip(chips, Loc.T("results.pt.agenda"), agendaPts, delay + 0.4f);
                if (p.EmployeeOfWeek) PointChip(chips, Loc.T("results.pt.eotw"), cfg.EmployeeOfWeekPoints, delay + 0.5f);
                var total = Title("0", "wr-total", "wr-c-total").NoPick().AddTo(row);
                var slot = Box("wr-c-stamp").NoPick().AddTo(row);
                UiMotion.CountUp(total, 0, p.Points, 0.6f, x => "+" + x, delay + 0.5f);

                if (p.EmployeeOfWeek || !p.Survived)
                {
                    var st = Box("wr-stamp").NoPick().AddTo(slot);
                    bool ok = p.EmployeeOfWeek
                        ? MeetingText.ApplyStamp(st, "stamps_v3/stamp_haftanin_elemani", "stamps_v3/stamp_employee_week_en")
                        : MeetingText.ApplyStamp(st, "stamps/stamp_kovuldu", "stamps_v3/stamp_fired");
                    if (!ok) TextStamp(st, Loc.T(p.EmployeeOfWeek ? "results.eotw_stamp" : "final.stamp.fired"), p.EmployeeOfWeek ? "stamp-gold" : "");
                    if (p.EmployeeOfWeek) st.AddToClassList("wr-stamp-gold");
                    UiMotion.Stamp(st, p.EmployeeOfWeek ? -6f : -10f, 0.22f, delay + 1.1f, () => Sfx.Play(p.EmployeeOfWeek ? "cheer" : "place", 0.6f));
                }
                UiMotion.SlideIn(row, new Vector2(0f, 24f), 0.3f, delay);
                n++;
            }

            // session board: totals including this week
            Title(Loc.T("results.session.title"), "wr-session-title").NoPick().AddTo(_session);
            Memo(Loc.T("results.session.sub"), "wr-session-sub").NoPick().AddTo(_session);
            var board = new List<LineWeekPlayerResult>(r.Players);
            board.Sort((a, b) => b.SessionPoints != a.SessionPoints ? b.SessionPoints.CompareTo(a.SessionPoints) : a.Player.CompareTo(b.Player));
            int top = board.Count > 0 ? board[0].SessionPoints : 0;
            int i = 0;
            foreach (var p in board)
            {
                var row = Box("wr-srow").NoPick().AddTo(_session);
                if (p.SessionPoints == top && top > 0) row.AddToClassList("wr-srow-lead");
                Title((i + 1) + ".", "wr-srank").NoPick().AddTo(row);
                Badge(p.Player, "badge-s").AddTo(row);
                Bold(MeetingText.Name(p.Player), "wr-sname").NoPick().AddTo(row);
                var pts = Title("", "wr-spts").NoPick().AddTo(row);
                UiMotion.CountUp(pts, p.SessionPoints - p.Points, p.SessionPoints, 0.7f, x => x.ToString(), 1.4f + i * 0.1f);
                i++;
            }

            Text(Loc.F("results.session.start_blame", cfg.StartBlame, cfg.StartBlame + cfg.LeaderStartBonus), "wr-session-note").NoPick().AddTo(_session);

            BuildNext(v, false);
            UiMotion.SlideIn(_sheet, new Vector2(0f, 60f), 0.4f);
            UiMotion.SlideIn(_side, new Vector2(60f, 0f), 0.4f, 1.0f);
        }

        // ------------------------------------------------------------------ next manager (v3.1)

        void BuildNext(LineView v, bool animate)
        {
            var r = v.Result;
            _next.Clear();
            if (r == null || r.Fired < 0) { _next.Show(false); _pickLayer.Show(false); return; }
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

        static void PointChip(VisualElement parent, string label, int pts, float delay, bool bad = false)
        {
            var c = Box("wr-chip", bad ? "wr-chip-bad" : "").NoPick().AddTo(parent);
            Text(label, "wr-chip-label").NoPick().AddTo(c);
            Bold("+" + pts, "wr-chip-num").NoPick().AddTo(c);
            UiMotion.PopIn(c, 0.22f, delay, 0.6f);
        }

        public override void Tick(LineView v, double now)
        {
            int sec = (int)System.Math.Ceiling(System.Math.Max(0, v.PhaseEndTime - now));
            if (sec == _lastSec) return;
            _lastSec = sec;
            _timer.text = Loc.F("results.auto_continue", ClockInt(sec));
        }
    }
}
