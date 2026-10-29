using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// FinalVerdict: the fire-score breakdown (Blame + weight × votes, bars grow lowest first for suspense), then the
    /// termination letter slides in and the FIRED stamp slams. "Save letter" writes a PNG (v2 <see cref="ScreenshotSaver"/>).
    /// </summary>
    public sealed class FinalVerdictView : MeetingPanel
    {
        readonly VisualElement _board, _rows, _letterCol, _letter, _letterBody, _stamp, _actions;
        readonly Label _formula, _saved;
        readonly Button _save;
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

            _board = Box("score-board").NoPick().AddTo(wrap);
            Title(Loc.T("final.scores.title"), "score-title").NoPick().AddTo(_board);
            _formula = Memo("", "score-formula").NoPick().AddTo(_board);
            _rows = Box("score-rows").NoPick().AddTo(_board);
            var legend = Box("score-legend").NoPick().AddTo(_board);
            Legend(legend, "seg-blame", Loc.T("final.scores.blame"));
            Legend(legend, "seg-votes", Loc.T("final.scores.votes"));
        }

        static void Legend(VisualElement parent, string cls, string text)
        {
            var i = Box("legend-item").NoPick().AddTo(parent);
            Box("legend-swatch", cls).NoPick().AddTo(i);
            Text(text, "legend-text").NoPick().AddTo(i);
        }

        protected override void OnShow(LineView v)
        {
            var m = v.Meeting;
            _fired = m != null ? m.Fired : LineIds.None;
            _firedName = MeetingText.Name(_fired);
            _saved.text = "";
            _save.Show(_fired >= 0);
            var prof = MeetingText.Profile(v);
            _formula.text = Loc.F("final.scores.formula", prof.FinalVoteWeight);
            BuildScores(v, prof.FinalVoteWeight);
            BuildLetter(v);

            // timeline: board first, letter at 2.8 s, stamp at 3.6 s
            UiMotion.SlideIn(_board, new Vector2(0f, 40f), 0.35f);
            _letterCol.style.opacity = 0f;
            UiMotion.SlideIn(_letterCol, new Vector2(-80f, 260f), 0.55f, 2.8f, UiMotion.Ease.OutCubic);
            var letter = _letter;
            UiMotion.Stamp(_stamp, -12f, 0.24f, 3.7f, () =>
            {
                Sfx.Play("evidence", 1f);
                UiMotion.Shake(letter, 8f, 0.35f);
                FeelHooks.ResultsStamp(ScreenPoint(_stamp));
                if (_fired >= 0) _stamp.schedule.Execute(() => Sfx.Play("fired", 0.8f)).StartingIn(350);
            });
        }

        void BuildScores(LineView v, int weight)
        {
            _rows.Clear();
            var m = v.Meeting;
            var scores = new List<LineFireScore>();
            if (m != null) scores.AddRange(m.Scores);
            if (scores.Count == 0)
                foreach (var p in v.Players) scores.Add(new LineFireScore { Player = p.Id, Blame = p.Blame, Ihtars = p.Ihtars, Score = p.Blame });
            scores.Sort((a, b) => b.Score != a.Score ? b.Score.CompareTo(a.Score) : b.Ihtars != a.Ihtars ? b.Ihtars.CompareTo(a.Ihtars) : b.Blame.CompareTo(a.Blame));
            int max = 1;
            foreach (var s in scores) if (s.Score > max) max = s.Score;
            // the lowest row animates first; the fired one last
            int count = scores.Count;
            for (int i = 0; i < count; i++)
            {
                var s = scores[i];
                bool fired = m != null && s.Player == m.Fired;
                var row = Box("score-row").NoPick().AddTo(_rows);
                if (fired) row.AddToClassList("score-row-fired");
                if (s.Player == v.Me) row.AddToClassList("score-row-me");
                Badge(s.Player, "badge-m").AddTo(row);
                var col = Box("col", "grow").NoPick().AddTo(row);
                var top = Box("row").NoPick().AddTo(col);
                Title(MeetingText.Name(s.Player), "score-name").NoPick().AddTo(top);
                for (int k = 0; k < s.Ihtars; k++) DossierDeck.IhtarMark(top);
                Box("spacer").NoPick().AddTo(top);
                Memo(Loc.F("final.scores.sum", s.Blame, weight, s.Votes), "score-sum").NoPick().AddTo(top);
                var bar = Box("score-bar").NoPick().AddTo(col);
                var segB = Box("seg", "seg-blame").NoPick().AddTo(bar);
                var segV = Box("seg", "seg-votes").NoPick().AddTo(bar);
                float wb = s.Blame / (float)max * 100f, wv = (s.Score - s.Blame) / (float)max * 100f;
                segB.style.width = 0; segV.style.width = 0;
                var total = Title("0", "score-total").NoPick().AddTo(row);
                float delay = 0.3f + (count - 1 - i) * 0.32f;
                UiMotion.SlideIn(row, new Vector2(30f, 0f), 0.25f, delay * 0.4f);
                UiMotion.Tween(segB, 0.5f, t => segB.style.width = Length.Percent(wb * t), UiMotion.Ease.OutCubic, null, delay, "grow");
                UiMotion.Tween(segV, 0.45f, t => segV.style.width = Length.Percent(Mathf.Max(0f, wv) * t), UiMotion.Ease.OutCubic, null, delay + 0.4f, "grow");
                UiMotion.CountUp(total, 0, s.Score, 0.8f, null, delay);
                if (fired)
                {
                    var tag = Box("score-fired-tag").NoPick().AddTo(row);
                    if (!MeetingText.ApplyStamp(tag, "stamps/stamp_kovuldu", "stamps_v3/stamp_fired")) TextStamp(tag, Loc.T("final.stamp.fired"));
                    UiMotion.Stamp(tag, -10f, 0.2f, delay + 1.1f, () => UiMotion.Shake(row, 6f, 0.25f));
                }
            }
        }

        void BuildLetter(LineView v)
        {
            _letterBody.Clear();
            _stamp.Clear();
            _stamp.RemoveFromClassList("stamp-text");
            _stamp.style.backgroundImage = StyleKeyword.Null;
            int fired = _fired;
            Bold(Loc.T("final.letter.dept"), "letter-dept", "letter-dept-green").NoPick().AddTo(_letterBody);
            Memo(Loc.F("final.letter.ref", v.WeekId, MeetingText.Day(v.Day)), "letter-ref").NoPick().AddTo(_letterBody);
            MemoBold(Loc.T(fired >= 0 ? "final.letter.subject" : "final.letter.subject_none"), "letter-subject").NoPick().AddTo(_letterBody);
            if (fired >= 0)
            {
                var who = Box("letter-who").NoPick().AddTo(_letterBody);
                Badge(fired, "badge-l").AddTo(who);
                Title(Loc.F("final.letter.dear", _firedName), "letter-dear").NoPick().AddTo(who);
                Memo(Loc.T("final.letter.body"), "letter-par").NoPick().AddTo(_letterBody);
                LineFireScore score = null;
                if (v.Meeting != null) foreach (var s in v.Meeting.Scores) if (s.Player == fired) score = s;
                if (score != null)
                {
                    Memo(Loc.F("final.letter.score", score.Blame, score.Votes, score.Score), "letter-par").NoPick().AddTo(_letterBody);
                    if (score.Ihtars > 0) Memo(Loc.F("final.letter.ihtars", score.Ihtars), "letter-par").NoPick().AddTo(_letterBody);
                }
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
