using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// FinalVerdict (v4): the three outcomes. A colleague got more than half of the roster: FIRED stamp and the
    /// termination letter. No majority (or no votes): "nobody was fired". The company closed (productivity below the
    /// target): shared defeat, everyone is fired, no defense/vote happened. The ballot is revealed on the right.
    /// "Save letter" writes a PNG (<see cref="ScreenshotSaver"/>).
    /// </summary>
    public sealed class FinalVerdictView : MeetingPanel
    {
        readonly VisualElement _board, _tally, _closed, _letterCol, _letter, _letterBody, _stamp, _actions;
        readonly Label _need, _reason;
        readonly Button _save;
        readonly Label _saved;
        bool _saving;
        int _fired = LineIds.None;
        string _firedName = "";

        public FinalVerdictView(VisualElement parent) : base(parent, "mt-final")
        {
            var wrap = Box("final-wrap").NoPick().AddTo(Root);
            _letterCol = Box("final-letter-col").AddTo(wrap);
            _letter = Box("fire-letter").NoPick().AddTo(_letterCol);
            if (!UiArt.Apply(_letter, "meeting/termination_letter")) _letter.AddToClassList("fire-letter-fallback");
            _letterBody = Box("letter-text", "fire-letter-text").NoPick().AddTo(_letter);
            _stamp = Box("verdict-stamp", "fire-stamp").NoPick().AddTo(_letter);
            _actions = Box("letter-actions").AddTo(_letterCol);
            _save = IconBtn("star", Loc.T("final.save_letter"), SaveLetter, "btn-small", "btn-secondary").AddTo(_actions);
            _saved = Text("", "letter-saved").NoPick().AddTo(_letterCol);

            _board = Box("count-board").NoPick().AddTo(wrap);
            _need = Bold("", "final-need").NoPick().AddTo(_board);
            _reason = Text("", "final-reason").NoPick().AddTo(_board);
            _tally = Box("tally", "final-tally").NoPick().AddTo(_board);
            _closed = Box("final-closed").NoPick().AddTo(_board);
        }

        protected override void OnShow(LineView v)
        {
            var m = v.Meeting;
            bool closed = (m != null && m.CompanyClosed) || (v.Result != null && v.Result.CompanyClosed) || v.Company.Closed;
            _fired = closed ? LineIds.None : (m != null ? m.Fired : LineIds.None);
            _firedName = _fired >= 0 ? MeetingText.Name(_fired) : "";
            _saved.text = "";
            _save.Show(_fired >= 0);
            _tally.Show(!closed);
            _closed.Show(closed);
            _need.Show(!closed);
            _reason.Show(!closed);
            float end = 0.5f;
            if (closed) BuildClosed(v);
            else end = BuildBallot(v);
            BuildLetter(v, closed);

            UiMotion.SlideIn(_board, new Vector2(0f, 40f), 0.35f);
            _letterCol.style.opacity = 0f;
            UiMotion.SlideIn(_letterCol, new Vector2(-80f, 260f), 0.55f, end + 0.7f, UiMotion.Ease.OutCubic);
            var letter = _letter;
            UiMotion.Stamp(_stamp, -12f, 0.24f, end + 1.5f, () =>
            {
                Sfx.Play("evidence", 1f);
                UiMotion.Shake(letter, 8f, 0.35f);
                FeelHooks.ResultsStamp(ScreenPoint(_stamp));
                if (_fired >= 0 || closed) _stamp.schedule.Execute(() => Sfx.Play("fired", 0.8f)).StartingIn(350);
            });
        }

        float BuildBallot(LineView v)
        {
            var m = v.Meeting;
            int roster = m != null ? m.Roster.Count : 0, need = m != null ? m.MajorityNeeded : 0;
            _need.text = Loc.F("final.needs", need, roster);
            _reason.text = ReasonText(v);
            VoteTally.Build(_tally, v, _fired >= 0 ? _fired : (m != null && VoteTally_NobodyTop(m) ? LineIds.Nobody : int.MinValue), "final.ballot.nobody");
            int votes = m != null ? m.Votes.Count : 0;
            return 0.7f + votes * 0.06f + 0.4f;
        }

        static bool VoteTally_NobodyTop(LineMeetingView m)
        {
            int nobody = MeetingText.VotesFor(m, LineIds.Nobody), best = 0;
            var counts = new Dictionary<int, int>();
            foreach (var x in m.Votes) if (x.Target >= 0) { int c; counts.TryGetValue(x.Target, out c); counts[x.Target] = c + 1; if (counts[x.Target] > best) best = counts[x.Target]; }
            return nobody > best;
        }

        /// <summary>Why the final vote ended the way it did (never judges the outcome).</summary>
        static string ReasonText(LineView v)
        {
            var m = v.Meeting;
            if (m == null) return "";
            if (m.Fired >= 0) return Loc.F("final.reason.fired", MeetingText.Name(m.Fired), MeetingText.VotesFor(m, m.Fired), m.Roster.Count);
            var outcome = v.Result != null ? v.Result.FinalOutcome : m.Outcome;
            if (outcome == VoteOutcome.NoVotes || m.Votes.Count == 0) return Loc.T("final.reason.no_votes");
            return Loc.F("final.reason.no_majority", m.MajorityNeeded, m.Roster.Count);
        }

        void BuildClosed(LineView v)
        {
            _closed.Clear();
            var c = v.Company;
            int perm = c.FinalPermille >= 0 ? c.FinalPermille : (v.Result != null ? v.Result.FinalProductivityPermille : c.ProvisionalPermille);
            int target = c.TargetPercent > 0 ? c.TargetPercent : (v.Result != null && v.Result.TargetPercent > 0 ? v.Result.TargetPercent : MeetingText.Config.CompanyTargetPercent);
            StampText(Loc.T("final.closed.title"), "final-closed-title").NoPick().AddTo(_closed);
            Title(MeetingText.Percent(perm), "company-big", "company-bad").NoPick().AddTo(_closed);
            Bold(Loc.F("company.vs_target", target), "company-target").NoPick().AddTo(_closed);
            DossierDeck.Gauge(_closed, perm, target, true, "company-gauge");
            Text(Loc.T("final.closed.body"), "final-reason").NoPick().AddTo(_closed);
            Text(Loc.T("final.closed.no_vote"), "final-reason", "t-muted").NoPick().AddTo(_closed);
        }

        void BuildLetter(LineView v, bool closed)
        {
            _letterBody.Clear();
            _stamp.Clear();
            _stamp.RemoveFromClassList("stamp-text");
            _stamp.RemoveFromClassList("stamp-navy");
            _stamp.style.backgroundImage = StyleKeyword.Null;
            int fired = _fired;
            var m = v.Meeting;
            Bold(Loc.T("final.letter.dept"), "letter-dept", "letter-dept-green").NoPick().AddTo(_letterBody);
            Memo(Loc.F("final.letter.ref", v.WeekId, MeetingText.Day(v.Day)), "letter-ref").NoPick().AddTo(_letterBody);
            MemoBold(Loc.T(closed ? "final.letter.subject_closed" : fired >= 0 ? "final.letter.subject" : "final.letter.subject_none"), "letter-subject").NoPick().AddTo(_letterBody);
            if (closed)
            {
                int perm = v.Company.FinalPermille >= 0 ? v.Company.FinalPermille : (v.Result != null ? v.Result.FinalProductivityPermille : v.Company.ProvisionalPermille);
                int target = v.Company.TargetPercent > 0 ? v.Company.TargetPercent : MeetingText.Config.CompanyTargetPercent;
                Memo(Loc.F("final.letter.closed_body", MeetingText.Percent(perm), target), "letter-par").NoPick().AddTo(_letterBody);
                Memo(Loc.T("final.letter.closed_everyone"), "letter-par").NoPick().AddTo(_letterBody);
                TextStamp(_stamp, Loc.T("final.stamp.closed"));
            }
            else if (fired >= 0)
            {
                var who = Box("letter-who").NoPick().AddTo(_letterBody);
                Badge(fired, "badge-l").AddTo(who);
                Title(Loc.F("final.letter.dear", _firedName), "letter-dear").NoPick().AddTo(who);
                Memo(Loc.T("final.letter.body"), "letter-par").NoPick().AddTo(_letterBody);
                if (m != null) Memo(Loc.F("final.letter.votes", MeetingText.VotesFor(m, fired), m.Roster.Count, m.MajorityNeeded), "letter-par").NoPick().AddTo(_letterBody);
                Memo(Loc.T("final.letter.closing." + (Mathf.Abs(v.WeekId * 7 + fired) % 3)), "letter-par").NoPick().AddTo(_letterBody);
                if (fired == v.Me) Hand(Loc.T("final.letter.its_you"), "letter-you").NoPick().AddTo(_letterBody);
                if (!MeetingText.ApplyStamp(_stamp, "stamps/stamp_kovuldu", "stamps_v3/stamp_fired")) TextStamp(_stamp, Loc.T("final.stamp.fired"));
            }
            else
            {
                Memo(Loc.T("final.letter.nobody"), "letter-par").NoPick().AddTo(_letterBody);
                TextStamp(_stamp, Loc.T("final.stamp.none"), "stamp-navy");
            }
            Box("spacer").NoPick().AddTo(_letterBody);
            Hand(Loc.ManagerName(v.ManagerProfileId), "letter-sign").NoPick().AddTo(_letterBody);
            Memo(Loc.ManagerTitle(v.ManagerProfileId), "letter-sign-title").NoPick().AddTo(_letterBody);
        }

        void SaveLetter()
        {
            if (_saving || UIRoot.I == null) return;
            _saving = true;
            _actions.visible = false;
            string stem = (Loc.Language == "en" ? "fired_" : "kovuldu_") + Safe(_firedName);
            UIRoot.I.StartCoroutine(ScreenshotSaver.Capture(_letter, stem, path =>
            {
                _saving = false;
                _actions.visible = true;
                _saved.text = path != null ? Loc.F("final.saved", path) : Loc.T("final.save_failed");
                _saved.EnableInClassList("t-bad", path == null);
                UiMotion.FadeIn(_saved, 0.2f);
            }));
        }

        static string Safe(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.Length > 0 ? sb.ToString() : "letter";
        }
    }
}
