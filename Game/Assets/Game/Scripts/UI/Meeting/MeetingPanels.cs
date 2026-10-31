using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>A phase panel inside <see cref="MeetingUi"/>: shown/hidden by phase, rebuilt on show, refreshed per view.</summary>
    public abstract class MeetingPanel
    {
        public readonly VisualElement Root;
        public bool Visible { get; private set; }

        protected MeetingPanel(VisualElement parent, params string[] classes)
        {
            Root = Box(classes).AddTo(parent);
            Root.pickingMode = PickingMode.Ignore;
            Root.style.display = DisplayStyle.None;
        }

        public void SetVisible(bool on, LineView v)
        {
            if (on == Visible) return;
            Visible = on;
            Root.Show(on);
            if (on) OnShow(v); else OnHide();
        }

        protected virtual void OnShow(LineView v) { }
        protected virtual void OnHide() { }
        public virtual void Refresh(LineView v) { }
        public virtual void Tick(LineView v, double now) { }

        protected static Vector2 ScreenPoint(VisualElement e)
        {
            Rect wb = e.worldBound;
            float w = e.panel != null ? e.panel.visualTree.worldBound.width : UnityEngine.Screen.width;
            float scale = w > 0 ? UnityEngine.Screen.width / w : 1f;
            return new Vector2(wb.center.x * scale, UnityEngine.Screen.height - wb.center.y * scale);
        }

        /// <summary>Text stamp used until the stamp art lands (red ink border, tilted, Bebas Neue).</summary>
        protected static void TextStamp(VisualElement e, string text, string tone = "")
        {
            e.AddToClassList("stamp-text");
            if (!string.IsNullOrEmpty(tone)) e.AddToClassList(tone);
            StampText(text, "stamp-text-label").NoPick().AddTo(e);
        }

        protected static LinePlayerView Player(LineView v, int id) { return MeetingText.Player(v, id); }
    }

    // ====================================================================== vote tally

    /// <summary>The revealed ballot: one row per option (colleagues and No action / Fire nobody) with the voters' badges.</summary>
    public static class VoteTally
    {
        public static void Build(VisualElement host, LineView v, int highlight, string nobodyKey, bool animate = true)
        {
            host.Clear();
            Title(Loc.T("vote.tally.title"), "tally-title").NoPick().AddTo(host);
            var m = v.Meeting;
            var groups = new Dictionary<int, List<int>>();
            var abstain = new List<int>();
            var voted = new HashSet<int>();
            if (m != null)
                foreach (var x in m.Votes)
                {
                    voted.Add(x.Voter);
                    if (x.Target == LineIds.None) { abstain.Add(x.Voter); continue; }
                    List<int> l;
                    if (!groups.TryGetValue(x.Target, out l)) groups[x.Target] = l = new List<int>();
                    l.Add(x.Voter);
                }
            var targets = new List<int>(groups.Keys);
            targets.Sort((a, b) => groups[b].Count != groups[a].Count ? groups[b].Count.CompareTo(groups[a].Count) : a.CompareTo(b));
            int n = 0;
            float delay = 0.25f;
            foreach (int t in targets)
            {
                var row = Box("tally-row").NoPick().AddTo(host);
                if (t == highlight) row.AddToClassList("tally-winner");
                if (t == LineIds.Nobody)
                {
                    row.AddToClassList("tally-nobody");
                    MeetingText.ApplyIcon(Box("icon", "icon-m").NoPick().AddTo(row), "icons/ballot");
                }
                else Badge(t, "badge-m").AddTo(row);
                var col = Box("col", "grow").NoPick().AddTo(row);
                Title(t == LineIds.Nobody ? Loc.T(nobodyKey) : MeetingText.Name(t), "tally-name").NoPick().AddTo(col);
                var voters = Box("tally-voters").NoPick().AddTo(col);
                foreach (int voter in groups[t])
                {
                    var b = Badge(voter, "badge-xs").AddTo(voters);
                    if (animate) UiMotion.PopIn(b, 0.22f, delay, 0.4f);
                    delay += 0.06f;
                }
                Title(groups[t].Count.ToString(), "tally-count").NoPick().AddTo(row);
                if (animate) UiMotion.SlideIn(row, new Vector2(40f, 0f), 0.26f, 0.15f + n * 0.08f);
                n++;
            }
            if (targets.Count == 0) Text(Loc.T("vote.tally.empty"), "tally-empty").NoPick().AddTo(host);
            if (abstain.Count > 0)
            {
                var row = Box("tally-abstain").NoPick().AddTo(host);
                Text(Loc.T("vote.tally.abstained"), "tally-sub").NoPick().AddTo(row);
                foreach (int voter in abstain) Badge(voter, "badge-xs").AddTo(row);
            }
            var silent = new List<int>();
            foreach (var p in v.Players) if (!voted.Contains(p.Id) && p.Connected) silent.Add(p.Id);
            if (silent.Count > 0)
            {
                var row = Box("tally-abstain").NoPick().AddTo(host);
                Text(Loc.T("vote.tally.silent"), "tally-sub").NoPick().AddTo(row);
                foreach (int id in silent) Badge(id, "badge-xs").AddTo(row);
            }
        }
    }

    // ====================================================================== ballot

    /// <summary>
    /// The paper ballot (daily warning ballot and final firing ballot): one row per colleague (not yourself), the explicit
    /// "No action" / "Fire nobody" row and a withdraw button. Shows who has voted (never whom). Your choice gets an inked X.
    /// </summary>
    public sealed class Ballot : MeetingPanel
    {
        sealed class Row { public int Target; public VisualElement Root; public VisualElement X; }

        readonly Action<int> _vote;
        readonly VisualElement _paper, _rows, _votedRow;
        readonly Label _title, _question, _note, _need, _roster;
        readonly Button _withdraw;
        readonly List<Row> _list = new List<Row>();
        int _pending = int.MinValue, _shown = int.MinValue, _votedKey = -1;
        float _pendingUntil;
        bool _final;

        public Ballot(VisualElement parent, Action<int> vote) : base(parent, "mt-ballot-layer")
        {
            _vote = vote;
            _paper = Box("ballot").AddTo(Root);
            var head = Box("ballot-head").NoPick().AddTo(_paper);
            _title = StampText("", "ballot-title").NoPick().AddTo(head);
            Box("spacer").NoPick().AddTo(head);
            _need = Bold("", "ballot-count").NoPick().AddTo(head);
            _question = Title("", "ballot-question").NoPick().AddTo(_paper);
            _note = Text("", "ballot-note").NoPick().AddTo(_paper);
            _roster = Text("", "ballot-note", "ballot-roster").NoPick().AddTo(_paper);
            _rows = Box("ballot-rows").AddTo(_paper);
            _votedRow = Box("ballot-voted-row").NoPick().AddTo(_paper);
            var foot = Box("ballot-foot").AddTo(_paper);
            _withdraw = Btn(Loc.T("vote.withdraw"), () => _vote(LineIds.None), "btn-small", "btn-ghost").AddTo(foot);
        }

        protected override void OnShow(LineView v)
        {
            _final = v.Phase == LinePhase.FinalVote;
            var cfg = MeetingText.Config;
            _title.text = Loc.T("meeting.ballot.title");
            _question.text = Loc.T(_final ? "final.ballot.question" : "meeting.ballot.question");
            _note.text = _final ? Loc.T("final.ballot.note") : Loc.F("meeting.ballot.note", MeetingText.Profile(v).WarningBlame, cfg.WarningMinVotes, cfg.MaxWarningsPerPlayer);
            _paper.EnableInClassList("ballot-final", _final);
            _rows.Clear();
            _list.Clear();
            _pending = int.MinValue;
            _shown = int.MinValue;
            _votedKey = -1;
            int n = 0;
            var m = v.Meeting;
            foreach (var p in v.Players)
            {
                if (p.Id == v.Me) continue;
                bool capped = !_final && cfg.MaxWarningsPerPlayer > 0 && p.Warnings >= cfg.MaxWarningsPerPlayer;
                AddRow(p.Id, p, n++, capped);
            }
            AddRow(LineIds.Nobody, null, n, false);
            Refresh(v);
            UiMotion.SlideIn(_paper, new Vector2(0f, 80f), 0.38f, 0f, UiMotion.Ease.OutBack);
            UiMotion.Tween(_paper, 0.5f, t => _paper.style.rotate = new Rotate(new Angle(Mathf.Lerp(-4f, -0.8f, t), AngleUnit.Degree)), UiMotion.Ease.OutCubic, null, 0f, "rot");
        }

        void AddRow(int target, LinePlayerView p, int n, bool capped)
        {
            var r = new Row { Target = target };
            r.Root = Box("ballot-row").AddTo(_rows);
            if (target == LineIds.Nobody) r.Root.AddToClassList("ballot-row-abstain");
            var box = Box("ballot-box").NoPick().AddTo(r.Root);
            r.X = Box("ballot-x").NoPick().AddTo(box);
            Box("ballot-x-a").NoPick().AddTo(r.X);
            Box("ballot-x-b").NoPick().AddTo(r.X);
            r.X.style.opacity = 0f;
            if (p != null)
            {
                Badge(p.Id, "badge-m").AddTo(r.Root);
                Title(p.Name, "ballot-name").NoPick().AddTo(r.Root);
                var line = Box("row", "grow", "ballot-line").NoPick().AddTo(r.Root);
                Text(Loc.F("meeting.blame_n", p.Blame), "ballot-sub").NoPick().AddTo(line);
                for (int i = 0; i < p.Warnings; i++) DossierDeck.WarningMark(line);
                if (capped)
                {
                    r.Root.AddToClassList("ballot-row-warned");
                    Text(Loc.T("meeting.ballot.at_limit"), "ballot-warned").NoPick().AddTo(line);
                }
                if (!p.Connected) Tag(Loc.T("lobby.offline_tag"), "tag-grey").AddTo(r.Root);
            }
            else
            {
                MeetingText.ApplyIcon(Box("icon", "icon-m", "ballot-nobody-icon").NoPick().AddTo(r.Root), "icons/ballot");
                Title(Loc.T(_final ? "final.ballot.nobody" : "meeting.ballot.no_action"), "ballot-name").NoPick().AddTo(r.Root);
                Text(Loc.T(_final ? "final.ballot.nobody_sub" : "meeting.ballot.no_action_sub"), "ballot-sub", "ballot-line").NoPick().AddTo(r.Root);
            }
            int t = target;
            r.Root.RegisterCallback<ClickEvent>(_ => _vote(t));
            _list.Add(r);
            UiMotion.SlideIn(r.Root, new Vector2(-24f, 0f), 0.24f, 0.18f + n * 0.05f);
        }

        public void MarkPending(int target)
        {
            _pending = target;
            _pendingUntil = Time.unscaledTime + 2.5f;
            ShowChoice(target, true);
        }

        public override void Refresh(LineView v)
        {
            if (!Visible) return;
            int confirmed = v.Meeting != null ? v.Meeting.MyVote : LineIds.None;
            if (_pending != int.MinValue && confirmed == _pending) _pending = int.MinValue;
            int shown = _pending != int.MinValue ? _pending : confirmed;
            ShowChoice(shown, false);
            _withdraw.SetEnabled(shown != LineIds.None);

            int voted = MeetingText.CountVoted(v);
            int total = MeetingText.CountConnected(v);
            int key = voted * 1000 + total * 10;
            foreach (var p in v.Players) key = key * 31 + (p.Voted ? p.Id + 1 : 0);
            if (key != _votedKey)
            {
                _votedKey = key;
                _votedRow.Clear();
                Text(Loc.F("vote.voted_count", voted, total), "ballot-sub").NoPick().AddTo(_votedRow);
                foreach (var p in v.Players) if (p.Voted) Badge(p.Id, "badge-xs").AddTo(_votedRow);
                Text(Loc.T("vote.secret_note"), "ballot-sub", "ballot-secret").NoPick().AddTo(_votedRow);
            }
            if (_final && v.Meeting != null)
            {
                int roster = v.Meeting.Roster.Count, need = v.Meeting.MajorityNeeded;
                _need.text = Loc.F("final.needs", need, roster);
                bool onRoster = v.Meeting.Roster.Contains(v.Me);
                _roster.text = onRoster ? Loc.F("final.roster_line", roster) : Loc.T("final.not_on_roster");
                _roster.Show(true);
            }
            else
            {
                _need.text = Loc.F("meeting.ballot.min_votes", MeetingText.Config.WarningMinVotes);
                _roster.Show(false);
            }
        }

        public override void Tick(LineView v, double now)
        {
            if (_pending != int.MinValue && Time.unscaledTime > _pendingUntil) { _pending = int.MinValue; Refresh(v); }
        }

        void ShowChoice(int target, bool animate)
        {
            if (target == _shown) return;
            _shown = target;
            foreach (var r in _list)
            {
                bool on = r.Target == target;
                r.Root.EnableInClassList("ballot-row-chosen", on);
                if (on)
                {
                    if (animate) UiMotion.Stamp(r.X, -6f, 0.16f);
                    else { UiMotion.Stop(r.X, "scale"); r.X.style.opacity = 1f; r.X.style.scale = StyleKeyword.Null; }
                }
                else { UiMotion.Stop(r.X, "scale"); r.X.style.opacity = 0f; }
            }
        }
    }

    // ====================================================================== daily verdict

    /// <summary>
    /// MeetingVerdict: the written warning letter with the stamp slam when somebody got a warning, or a plain "no action"
    /// note naming the reason (VoteOutcome); every vote revealed on the right.
    /// </summary>
    public sealed class WarningVerdictView : MeetingPanel
    {
        readonly VisualElement _letter, _letterBody, _stamp, _tally;
        int _react = LineIds.None;
        bool _reactPending;

        public WarningVerdictView(VisualElement parent) : base(parent, "mt-verdict")
        {
            var wrap = Box("verdict-wrap").NoPick().AddTo(Root);
            _letter = Box("warning-letter").NoPick().AddTo(wrap);
            _letterBody = Box("letter-text").NoPick().AddTo(_letter);
            _stamp = Box("verdict-stamp").NoPick().AddTo(_letter);
            _tally = Box("tally").NoPick().AddTo(wrap);
        }

        protected override void OnShow(LineView v)
        {
            var m = v.Meeting;
            int to = m != null ? m.WarningTo : LineIds.None;
            bool warned = to >= 0 && (m == null || m.Outcome == VoteOutcome.WarningGiven || m.Outcome == VoteOutcome.None);
            _letterBody.Clear();
            _stamp.Clear();
            _stamp.RemoveFromClassList("stamp-text");
            _stamp.RemoveFromClassList("stamp-navy");
            _stamp.style.backgroundImage = StyleKeyword.Null;
            bool art = UiArt.Apply(_letter, "meeting_v3/warning_letter");
            _letter.EnableInClassList("warning-letter-fallback", !art);
            var prof = MeetingText.Profile(v);

            if (warned)
            {
                Bold(Loc.T("meeting.letter.dept"), "letter-dept").NoPick().AddTo(_letterBody);
                Memo(Loc.F("meeting.letter.ref", v.WeekId, v.Day + 1, MeetingText.Day(v.Day)), "letter-ref").NoPick().AddTo(_letterBody);
                MemoBold(Loc.T("meeting.letter.subject"), "letter-subject").NoPick().AddTo(_letterBody);
                var who = Box("letter-who").NoPick().AddTo(_letterBody);
                Badge(to, "badge-l").AddTo(who);
                Title(Loc.F("meeting.letter.dear", MeetingText.Name(to)), "letter-dear").NoPick().AddTo(who);
                int votes = MeetingText.VotesFor(m, to);
                Memo(Loc.F("meeting.letter.body", votes, MeetingText.VotesFor(m, LineIds.Nobody)), "letter-par").NoPick().AddTo(_letterBody);
                var blame = Box("letter-blame").NoPick().AddTo(_letterBody);
                Memo(Loc.T("meeting.letter.blame"), "letter-par").NoPick().AddTo(blame);
                var num = Title("+0", "letter-blame-num").NoPick().AddTo(blame);
                UiMotion.CountUp(num, 0, prof.WarningBlame, 0.4f, n => "+" + n, 0.55f);
                Memo(Loc.T("meeting.letter.closing"), "letter-par").NoPick().AddTo(_letterBody);
                if (to == v.Me) Hand(Loc.T("meeting.letter.its_you"), "letter-you").NoPick().AddTo(_letterBody);
                Box("spacer").NoPick().AddTo(_letterBody);
                Hand(Loc.ManagerName(v.ManagerProfileId), "letter-sign").NoPick().AddTo(_letterBody);
                Memo(Loc.ManagerTitle(v.ManagerProfileId), "letter-sign-title").NoPick().AddTo(_letterBody);
                if (!MeetingText.ApplyStamp(_stamp, "stamps_v3/stamp_ihtar", "stamps_v3/stamp_warning")) TextStamp(_stamp, Loc.T("meeting.stamp.warning"));
                _react = to;
            }
            else
            {
                Bold(Loc.T("meeting.letter.dept"), "letter-dept").NoPick().AddTo(_letterBody);
                MemoBold(Loc.T("meeting.letter.none_subject"), "letter-subject").NoPick().AddTo(_letterBody);
                Memo(NoActionReason(v), "letter-par").NoPick().AddTo(_letterBody);
                Memo(Loc.T("meeting.letter.none_closing"), "letter-par").NoPick().AddTo(_letterBody);
                Box("spacer").NoPick().AddTo(_letterBody);
                Hand(Loc.ManagerName(v.ManagerProfileId), "letter-sign").NoPick().AddTo(_letterBody);
                TextStamp(_stamp, Loc.T("meeting.stamp.none"), "stamp-navy");
                _react = LineIds.None;
            }
            _reactPending = true;
            VoteTally.Build(_tally, v, warned ? to : (m != null && m.Outcome == VoteOutcome.NoActionWon ? LineIds.Nobody : int.MinValue), "vote.no_action");

            UiMotion.SlideIn(_letter, new Vector2(0f, 120f), 0.42f, 0f, UiMotion.Ease.OutCubic);
            var letter = _letter;
            UiMotion.Stamp(_stamp, -11f, 0.22f, 0.85f, () =>
            {
                Sfx.Play(warned ? "evidence" : "place", 0.9f);
                UiMotion.Shake(letter, 7f, 0.3f);
                if (warned) FeelHooks.ResultsStamp(ScreenPoint(_stamp));
            });
        }

        /// <summary>The reason nobody got a warning (engine outcome); never claims anything about guilt.</summary>
        static string NoActionReason(LineView v)
        {
            var m = v.Meeting;
            var outcome = m != null ? m.Outcome : VoteOutcome.NoVotes;
            switch (outcome)
            {
                case VoteOutcome.Capped:
                    return Loc.F("vote.outcome.Capped", MeetingText.Name(TopNominee(m)), MeetingText.Config.MaxWarningsPerPlayer);
                case VoteOutcome.Tie: return Loc.T("vote.outcome.Tie");
                case VoteOutcome.BelowMinimum: return Loc.F("vote.outcome.BelowMinimum", MeetingText.Config.WarningMinVotes);
                case VoteOutcome.NoActionWon: return Loc.T("vote.outcome.NoActionWon");
                default: return Loc.T("vote.outcome.NoVotes");
            }
        }

        static int TopNominee(LineMeetingView m)
        {
            var counts = new Dictionary<int, int>();
            if (m != null) foreach (var x in m.Votes) if (x.Target >= 0) { int c; counts.TryGetValue(x.Target, out c); counts[x.Target] = c + 1; }
            int top = LineIds.None, max = 0;
            foreach (var kv in counts) if (kv.Value > max) { max = kv.Value; top = kv.Key; }
            return top;
        }

        public void DriveRoom(IRoomPresenter rooms)
        {
            if (!Visible || !_reactPending || rooms == null) return;
            _reactPending = false;
            if (_react >= 0) rooms.React(_react, MeetingReaction.Slump);
        }
    }

    // ====================================================================== final defense

    /// <summary>FinalDefense: every present player gets a turn. Spotlight card of the defender with their own timer, the order, the mic rule.</summary>
    public sealed class DefenseView : MeetingPanel
    {
        readonly VisualElement _center, _cone, _badge, _ring, _fill, _order;
        readonly Label _name, _role, _time, _rule, _counter, _note;
        int _defender = int.MinValue, _lastSec = -1;
        bool _speaking;

        public DefenseView(VisualElement parent) : base(parent, "mt-defense")
        {
            _cone = Box("spot-cone").NoPick().AddTo(Root);
            if (!UiArt.Apply(_cone, "meeting_v3/spotlight")) _cone.AddToClassList("spot-cone-fallback");
            _center = Box("spot-center").NoPick().AddTo(Root);
            _counter = Bold("", "spot-counter").NoPick().AddTo(_center);
            var bw = Box("spot-badge").NoPick().AddTo(_center);
            _badge = Badge(-1, "badge-xxl").AddTo(bw);
            _ring = Box("speak-ring", "spot-ring").NoPick().AddTo(bw);
            _name = Title("", "spot-name").NoPick().AddTo(_center);
            _role = StampText(Loc.T("final.defense.label"), "spot-role").NoPick().AddTo(_center);
            var tr = Box("spot-timer-row").NoPick().AddTo(_center);
            _time = Title("", "spot-time").NoPick().AddTo(tr);
            var bar = Box("spot-bar").NoPick().AddTo(_center);
            _fill = Box("spot-fill").NoPick().AddTo(bar);
            _rule = Bold("", "spot-rule").NoPick().AddTo(_center);
            _note = Text(Loc.T("final.defense.everyone"), "spot-note").NoPick().AddTo(_center);

            var side = Box("spot-side").NoPick().AddTo(Root);
            Title(Loc.T("final.defense.order"), "spot-side-title").NoPick().AddTo(side);
            _order = Box("col").NoPick().AddTo(side);
        }

        protected override void OnShow(LineView v)
        {
            _defender = int.MinValue;
            _lastSec = -1;
            Refresh(v);
            UiMotion.FadeIn(_cone, 0.6f);
        }

        public override void Refresh(LineView v)
        {
            if (!Visible || v.Meeting == null) return;
            var m = v.Meeting;
            if (m.Defender == _defender) return;
            _defender = m.Defender;
            SetBadge(_badge, m.Defender, MeetingText.Name(m.Defender));
            _name.text = MeetingText.Name(m.Defender);
            int idx = m.DefenseOrder.IndexOf(m.Defender);
            _counter.text = Loc.F("final.defense.counter", idx + 1, m.DefenseOrder.Count);
            _rule.text = m.Defender == v.Me ? Loc.T("final.defense.rule_me") : Loc.F("final.defense.rule", MeetingText.Name(m.Defender));
            _center.EnableInClassList("spot-me", m.Defender == v.Me);
            BuildOrder(v);
            UiMotion.PopIn(_center, 0.34f, 0f, 0.8f);
            Sfx.Play("phase", 0.6f);
        }

        void BuildOrder(LineView v)
        {
            var m = v.Meeting;
            _order.Clear();
            int cur = m.DefenseOrder.IndexOf(m.Defender);
            for (int i = 0; i < m.DefenseOrder.Count; i++)
            {
                int id = m.DefenseOrder[i];
                var p = Player(v, id);
                var row = Box("order-row").NoPick().AddTo(_order);
                row.AddToClassList(i < cur ? "order-done" : i == cur ? "order-now" : "order-next");
                Title((i + 1).ToString(), "order-n").NoPick().AddTo(row);
                Badge(id, "badge-s").AddTo(row);
                var col = Box("col", "grow").NoPick().AddTo(row);
                Bold(MeetingText.Name(id), "order-name").NoPick().AddTo(col);
                if (p != null)
                {
                    var line = Box("row").NoPick().AddTo(col);
                    Text(Loc.F("meeting.blame_n", p.Blame), "order-sub").NoPick().AddTo(line);
                    for (int k = 0; k < p.Warnings; k++) DossierDeck.WarningMark(line);
                }
                Bold(Loc.T(i < cur ? "final.defense.done" : i == cur ? "final.defense.now" : "final.defense.waiting"), "order-state").NoPick().AddTo(row);
            }
        }

        public override void Tick(LineView v, double now)
        {
            var m = v.Meeting;
            if (m == null) return;
            double left = m.DefenderEndTime - now;
            float per = MeetingText.Config.Durations.DefensePerPlayer;
            int sec = (int)System.Math.Ceiling(System.Math.Max(0, left));
            if (sec != _lastSec)
            {
                _lastSec = sec;
                _time.text = ClockInt(sec);
                _time.EnableInClassList("spot-time-urgent", sec <= 5);
            }
            _fill.style.width = Length.Percent(Mathf.Clamp01((float)(left / Mathf.Max(1f, per))) * 100f);
            var voice = UiServices.Voice;
            bool sp = voice.Available && m.Defender >= 0 && voice.IsSpeaking(m.Defender);
            if (sp != _speaking)
            {
                _speaking = sp;
                _ring.EnableInClassList("on", sp);
            }
        }
    }

    // ====================================================================== abandoned

    public sealed class AbandonedView : MeetingPanel
    {
        readonly VisualElement _actions, _stamp;

        public AbandonedView(VisualElement parent) : base(parent, "mt-abandoned")
        {
            var card = Panel("panel", "abandon-card").AddTo(Root);
            _stamp = Box("abandon-stamp").NoPick().AddTo(card);
            if (!MeetingText.ApplyStamp(_stamp, "stamps/stamp_reddedildi", "stamps_v3/stamp_rejected")) TextStamp(_stamp, Loc.T("results.abandoned.stamp"));
            Title(Loc.T("results.abandoned.title"), "h1").NoPick().AddTo(card);
            Text(Loc.T("results.abandoned.body"), "abandon-body").NoPick().AddTo(card);
            _actions = Box("row", "abandon-actions").AddTo(card);
        }

        protected override void OnShow(LineView v)
        {
            _actions.Clear();
            var host = HostLineController.Current;
            if (host != null) Btn(Loc.T("results.to_lobby"), host.ReturnToLobby, "btn-primary").AddTo(_actions);
            else Bold(Loc.T("results.waiting_host"), "t-muted").AddTo(_actions);
            Btn(Loc.T("pause.leave"), () => ConnectionManager.I?.Leave(), "btn-ghost").AddTo(_actions);
            UiMotion.PopIn(Root[0], 0.3f);
            UiMotion.Stamp(_stamp, -6f, 0.22f, 0.35f, () => Sfx.Play("place", 0.7f));
        }
    }
}
