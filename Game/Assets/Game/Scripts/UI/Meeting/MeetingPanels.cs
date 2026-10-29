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

        protected static LinePlayerView Player(LineView v, int id)
        {
            foreach (var p in v.Players) if (p.Id == id) return p;
            return null;
        }
    }

    // ====================================================================== ballot

    /// <summary>The paper ballot: one row per colleague (not yourself) plus "abstain"; your choice gets an inked X.</summary>
    public sealed class Ballot : MeetingPanel
    {
        sealed class Row { public int Target; public VisualElement Root, Box, X; }

        readonly Action<int> _vote;
        readonly VisualElement _paper, _rows;
        readonly Label _title, _question, _note, _count;
        readonly List<Row> _list = new List<Row>();
        int _pending = int.MinValue, _shown = int.MinValue, _countKey = -1;
        float _pendingUntil;

        public Ballot(VisualElement parent, Action<int> vote) : base(parent, "mt-ballot-layer")
        {
            _vote = vote;
            _paper = Box("ballot").AddTo(Root);
            var head = Box("ballot-head").NoPick().AddTo(_paper);
            _title = StampText("", "ballot-title").NoPick().AddTo(head);
            Box("spacer").NoPick().AddTo(head);
            _count = Bold("", "ballot-count").NoPick().AddTo(head);
            _count.Show(false); // the count is in the top bar
            _question = Title("", "ballot-question").NoPick().AddTo(_paper);
            _note = Text("", "ballot-note").NoPick().AddTo(_paper);
            _rows = Box("ballot-rows").AddTo(_paper);
        }

        protected override void OnShow(LineView v)
        {
            bool final = v.Phase == LinePhase.FinalVote;
            _title.text = Loc.T("meeting.ballot.title");
            _question.text = Loc.T(final ? "final.ballot.question" : "meeting.ballot.question");
            var prof = MeetingText.Profile(v);
            _note.text = final ? Loc.F("final.ballot.note", prof.FinalVoteWeight) : Loc.F("meeting.ballot.note", prof.IhtarBlame);
            _paper.EnableInClassList("ballot-final", final);
            _rows.Clear();
            _list.Clear();
            _pending = int.MinValue;
            _shown = int.MinValue;
            _countKey = -1;
            int n = 0;
            var shortlist = final && v.Meeting != null && v.Meeting.Shortlist.Count > 0 ? v.Meeting.Shortlist : null;
            if (shortlist != null)
            {
                // v3.1 final: one name from the shortlist (in defense order), never yourself, no abstaining
                foreach (int id in shortlist)
                {
                    if (id == v.Me) continue;
                    AddRow(id, Player(v, id), n++, false);
                }
                if (shortlist.Contains(v.Me)) Text(Loc.T("final.ballot.you_listed"), "ballot-you").NoPick().AddTo(_rows);
            }
            else
            {
                int max = MeetingText.Config.MaxIhtarsPerPlayerPerWeek;
                foreach (var p in v.Players)
                {
                    if (p.Id == v.Me) continue;
                    AddRow(p.Id, p, n++, !final && max > 0 && p.Ihtars >= max);
                }
                AddRow(LineIds.None, null, n, false);
            }
            Refresh(v);
            UiMotion.SlideIn(_paper, new Vector2(0f, 80f), 0.38f, 0f, UiMotion.Ease.OutBack);
            UiMotion.Tween(_paper, 0.5f, t => _paper.style.rotate = new Rotate(new Angle(Mathf.Lerp(-4f, -0.8f, t), AngleUnit.Degree)), UiMotion.Ease.OutCubic, null, 0f, "rot");
        }

        void AddRow(int target, LinePlayerView p, int n, bool warned)
        {
            var r = new Row { Target = target };
            r.Root = Box("ballot-row").AddTo(_rows);
            if (target < 0) r.Root.AddToClassList("ballot-row-abstain");
            r.Box = Box("ballot-box").NoPick().AddTo(r.Root);
            r.X = Box("ballot-x").NoPick().AddTo(r.Box);
            Box("ballot-x-a").NoPick().AddTo(r.X);
            Box("ballot-x-b").NoPick().AddTo(r.X);
            r.X.style.opacity = 0f;
            if (p != null)
            {
                Badge(p.Id, "badge-m").AddTo(r.Root);
                Title(p.Name, "ballot-name").NoPick().AddTo(r.Root);
                var line = Box("row", "grow", "ballot-line").NoPick().AddTo(r.Root);
                Text(Loc.F("meeting.blame_n", p.Blame), "ballot-sub").NoPick().AddTo(line);
                for (int i = 0; i < p.Ihtars; i++) DossierDeck.IhtarMark(line);
                if (warned)
                {
                    r.Root.AddToClassList("ballot-row-warned");
                    Text(Loc.T("meeting.ballot.already_warned"), "ballot-warned").NoPick().AddTo(line);
                }
                if (!p.Connected) Tag(Loc.T("lobby.offline_tag"), "tag-grey").AddTo(r.Root);
            }
            else
            {
                Title(Loc.T("meeting.ballot.abstain"), "ballot-name").NoPick().AddTo(r.Root);
                Text(Loc.T("meeting.ballot.abstain_sub"), "ballot-sub", "ballot-line").NoPick().AddTo(r.Root);
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
            var me = Player(v, v.Me);
            int confirmed = me != null && me.Voted && v.Meeting != null ? v.Meeting.MyVote : int.MinValue;
            if (_pending != int.MinValue && confirmed == _pending) _pending = int.MinValue;
            ShowChoice(_pending != int.MinValue ? _pending : confirmed, false);
            int voted = MeetingText.CountVoted(v), total = MeetingText.CountConnected(v);
            int key = voted * 100 + total;
            if (key != _countKey)
            {
                _countKey = key;
                _count.text = Loc.F("meeting.ballot.count", voted, total);
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

    // ====================================================================== İhtar verdict

    /// <summary>MeetingVerdict: the written warning letter with the İHTAR stamp slam, and the revealed votes.</summary>
    public sealed class IhtarVerdictView : MeetingPanel
    {
        readonly VisualElement _letter, _letterBody, _stamp, _tally;
        int _react = LineIds.None;
        bool _reactPending;

        public IhtarVerdictView(VisualElement parent) : base(parent, "mt-verdict")
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
            int to = m != null ? m.IhtarTo : LineIds.None;
            _letterBody.Clear();
            _stamp.Clear();
            _stamp.RemoveFromClassList("stamp-text");
            _stamp.RemoveFromClassList("stamp-navy");
            _stamp.style.backgroundImage = StyleKeyword.Null;
            bool art = UiArt.Apply(_letter, "meeting_v3/warning_letter");
            _letter.EnableInClassList("warning-letter-fallback", !art);
            var prof = MeetingText.Profile(v);

            if (to >= 0)
            {
                Bold(Loc.T("meeting.letter.dept"), "letter-dept").NoPick().AddTo(_letterBody);
                Memo(Loc.F("meeting.letter.ref", v.WeekId, v.Day + 1, MeetingText.Day(v.Day)), "letter-ref").NoPick().AddTo(_letterBody);
                MemoBold(Loc.T("meeting.letter.subject"), "letter-subject").NoPick().AddTo(_letterBody);
                var who = Box("letter-who").NoPick().AddTo(_letterBody);
                Badge(to, "badge-l").AddTo(who);
                Title(Loc.F("meeting.letter.dear", MeetingText.Name(to)), "letter-dear").NoPick().AddTo(who);
                int votes = 0;
                if (m != null) foreach (var x in m.Votes) if (x.Target == to) votes++;
                Memo(Loc.F("meeting.letter.body", votes), "letter-par").NoPick().AddTo(_letterBody);
                var blame = Box("letter-blame").NoPick().AddTo(_letterBody);
                Memo(Loc.T("meeting.letter.blame"), "letter-par").NoPick().AddTo(blame);
                var num = Title("+0", "letter-blame-num").NoPick().AddTo(blame);
                UiMotion.CountUp(num, 0, prof.IhtarBlame, 0.4f, n => "+" + n, 0.55f);
                Memo(Loc.T("meeting.letter.closing"), "letter-par").NoPick().AddTo(_letterBody);
                if (to == v.Me) Hand(Loc.T("meeting.letter.its_you"), "letter-you").NoPick().AddTo(_letterBody);
                Box("spacer").NoPick().AddTo(_letterBody);
                Hand(Loc.ManagerName(v.ManagerProfileId), "letter-sign").NoPick().AddTo(_letterBody);
                Memo(Loc.ManagerTitle(v.ManagerProfileId), "letter-sign-title").NoPick().AddTo(_letterBody);
                if (!MeetingText.ApplyStamp(_stamp, "stamps_v3/stamp_ihtar", "stamps_v3/stamp_warning")) TextStamp(_stamp, Loc.T("meeting.stamp.ihtar"));
                _react = to;
            }
            else
            {
                Bold(Loc.T("meeting.letter.dept"), "letter-dept").NoPick().AddTo(_letterBody);
                MemoBold(Loc.T("meeting.letter.none_subject"), "letter-subject").NoPick().AddTo(_letterBody);
                Memo(NoIhtarReason(v), "letter-par").NoPick().AddTo(_letterBody);
                Memo(Loc.T("meeting.letter.none_closing"), "letter-par").NoPick().AddTo(_letterBody);
                Box("spacer").NoPick().AddTo(_letterBody);
                Hand(Loc.ManagerName(v.ManagerProfileId), "letter-sign").NoPick().AddTo(_letterBody);
                TextStamp(_stamp, Loc.T("meeting.stamp.none"), "stamp-navy");
                _react = LineIds.None;
            }
            _reactPending = true;
            BuildTally(v);

            UiMotion.SlideIn(_letter, new Vector2(0f, 120f), 0.42f, 0f, UiMotion.Ease.OutCubic);
            var letter = _letter;
            UiMotion.Stamp(_stamp, -11f, 0.22f, 0.85f, () =>
            {
                Sfx.Play(to >= 0 ? "evidence" : "place", 0.9f);
                UiMotion.Shake(letter, 7f, 0.3f);
                if (to >= 0) FeelHooks.ResultsStamp(ScreenPoint(_stamp));
            });
        }

        /// <summary>v3.1: nobody gets the İhtar on a tie, or when the plurality names someone who already has one.</summary>
        static string NoIhtarReason(LineView v)
        {
            var m = v.Meeting;
            if (m == null) return Loc.T("meeting.letter.none_votes");
            var counts = new Dictionary<int, int>();
            foreach (var x in m.Votes) if (x.Target >= 0) { int c; counts.TryGetValue(x.Target, out c); counts[x.Target] = c + 1; }
            if (counts.Count == 0) return Loc.T("meeting.letter.none_votes");
            int max = 0, top = LineIds.None, tied = 0;
            foreach (var kv in counts)
            {
                if (kv.Value > max) { max = kv.Value; top = kv.Key; tied = 1; }
                else if (kv.Value == max) tied++;
            }
            if (tied > 1) return Loc.T("meeting.letter.none_tie");
            var p = Player(v, top);
            if (p != null && p.Ihtars >= MeetingText.Config.MaxIhtarsPerPlayerPerWeek) return Loc.F("meeting.letter.none_warned", MeetingText.Name(top));
            return Loc.T("meeting.letter.none_tie");
        }

        void BuildTally(LineView v)
        {
            _tally.Clear();
            Title(Loc.T("meeting.tally.title"), "tally-title").NoPick().AddTo(_tally);
            var m = v.Meeting;
            var counts = new Dictionary<int, List<int>>();
            var abstain = new List<int>();
            if (m != null)
                foreach (var x in m.Votes)
                {
                    if (x.Target < 0) { abstain.Add(x.Voter); continue; }
                    List<int> l;
                    if (!counts.TryGetValue(x.Target, out l)) counts[x.Target] = l = new List<int>();
                    l.Add(x.Voter);
                }
            var targets = new List<int>(counts.Keys);
            targets.Sort((a, b) => counts[b].Count != counts[a].Count ? counts[b].Count.CompareTo(counts[a].Count) : a.CompareTo(b));
            int n = 0;
            float delay = 0.25f;
            foreach (int t in targets)
            {
                var row = Box("tally-row").NoPick().AddTo(_tally);
                if (m != null && t == m.IhtarTo) row.AddToClassList("tally-winner");
                Badge(t, "badge-m").AddTo(row);
                var col = Box("col", "grow").NoPick().AddTo(row);
                Title(MeetingText.Name(t), "tally-name").NoPick().AddTo(col);
                var voters = Box("tally-voters").NoPick().AddTo(col);
                foreach (int voter in counts[t])
                {
                    var b = Badge(voter, "badge-xs").AddTo(voters);
                    UiMotion.PopIn(b, 0.22f, delay, 0.4f);
                    delay += 0.06f;
                }
                Title(counts[t].Count.ToString(), "tally-count").NoPick().AddTo(row);
                UiMotion.SlideIn(row, new Vector2(40f, 0f), 0.26f, 0.15f + n * 0.08f);
                n++;
            }
            if (targets.Count == 0) Text(Loc.T("meeting.tally.empty"), "tally-empty").NoPick().AddTo(_tally);
            if (abstain.Count > 0)
            {
                var row = Box("tally-abstain").NoPick().AddTo(_tally);
                Text(Loc.T("meeting.tally.abstained"), "tally-sub").NoPick().AddTo(row);
                foreach (int voter in abstain) Badge(voter, "badge-xs").AddTo(row);
            }
        }

        public void DriveRoom(IRoomPresenter rooms)
        {
            if (!Visible || !_reactPending || rooms == null) return;
            _reactPending = false;
            if (_react >= 0) rooms.React(_react, MeetingReaction.Slump);
        }
    }

    // ====================================================================== final defense

    /// <summary>FinalDefense: spotlight card of the defender with their own timer, the defense order, the mic rule.</summary>
    public sealed class DefenseView : MeetingPanel
    {
        readonly VisualElement _center, _cone, _badge, _ring, _fill, _order;
        readonly Label _name, _role, _time, _rule, _counter;
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
                    for (int k = 0; k < p.Ihtars; k++) DossierDeck.IhtarMark(line);
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
