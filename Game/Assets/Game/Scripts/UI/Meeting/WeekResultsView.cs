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
        readonly VisualElement _sheet, _rows, _session, _actions;
        readonly Label _title, _sub, _timer, _waiting;
        int _lastSec = -1, _builtWeek = -1;
        bool _host;

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
            _rows = Box("col").NoPick().AddTo(left);

            _session = Box("wr-session").NoPick().AddTo(cols);

            var foot = Box("wr-foot").AddTo(_sheet);
            _timer = Bold("", "wr-timer").NoPick().AddTo(foot);
            Box("spacer").NoPick().AddTo(foot);
            _waiting = Bold(Loc.T("results.waiting_host"), "wr-waiting").NoPick().AddTo(foot);
            _actions = Box("row").AddTo(foot);
        }

        protected override void OnShow(LineView v)
        {
            UiArt.Apply(Root, "backgrounds/results_bg");
            _builtWeek = -1;
            _lastSec = -1;
            Build(v);
        }

        protected override void OnHide()
        {
            Root.style.backgroundImage = StyleKeyword.Null;
            UiArt.Release("backgrounds/results_bg");
        }

        public override void Refresh(LineView v)
        {
            if (Visible && v.Result != null && v.WeekId != _builtWeek) Build(v);
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
                Tag(Loc.T(p.AgendaHit ? "results.agenda_hit" : "results.agenda_miss"), p.AgendaHit ? "tag-green" : "tag-grey").AddTo(ag);
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
                if (p.AgendaHit) PointChip(chips, Loc.T("results.pt.agenda"), cfg.AgendaPoints, delay + 0.4f);
                if (p.EmployeeOfWeek) PointChip(chips, Loc.T("results.pt.eotw"), cfg.EmployeeOfWeekPoints, delay + 0.5f);
                var total = Title("0", "wr-total").NoPick().AddTo(pts);
                UiMotion.CountUp(total, 0, p.Points, 0.6f, x => "+" + x, delay + 0.5f);

                if (p.EmployeeOfWeek || !p.Survived)
                {
                    var st = Box("wr-stamp").NoPick().AddTo(row);
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
            Memo(Loc.F("results.session.weeks", v.WeekId), "wr-session-sub").NoPick().AddTo(_session);
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

            UiMotion.SlideIn(_sheet, new Vector2(0f, 60f), 0.4f);
            UiMotion.SlideIn(_session, new Vector2(60f, 0f), 0.4f, 1.0f);
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
