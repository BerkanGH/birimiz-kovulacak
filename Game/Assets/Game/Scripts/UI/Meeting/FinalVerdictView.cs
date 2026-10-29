using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// FinalVerdict (v3.1): the secret ballot is counted on the shortlisted personnel files. Anonymous vote tokens
    /// drop one by one, the most-voted file gets the FIRED stamp (the others SURVIVED), then the termination letter
    /// slides in and is stamped. "Save letter" writes a PNG (<see cref="ScreenshotSaver"/>).
    /// </summary>
    public sealed class FinalVerdictView : MeetingPanel
    {
        readonly VisualElement _board, _cards, _letterCol, _letter, _letterBody, _stamp, _actions;
        readonly Label _rule, _tie, _safe, _saved;
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

            _board = Box("count-board").NoPick().AddTo(wrap);
            Title(Loc.T("final.count.title"), "score-title").NoPick().AddTo(_board);
            _rule = Memo(Loc.T("final.count.rule"), "score-formula").NoPick().AddTo(_board);
            _cards = Box("count-cards").NoPick().AddTo(_board);
            _tie = Bold("", "count-tie").NoPick().AddTo(_board);
            _safe = Text("", "count-safe").NoPick().AddTo(_board);
        }

        protected override void OnShow(LineView v)
        {
            var m = v.Meeting;
            _fired = m != null ? m.Fired : LineIds.None;
            _firedName = MeetingText.Name(_fired);
            _saved.text = "";
            _save.Show(_fired >= 0);
            float end = BuildCount(v);
            BuildLetter(v);

            // timeline: files, tokens, file stamps (end), the letter (end + 0.7), the letter stamp (end + 1.5)
            UiMotion.SlideIn(_board, new Vector2(0f, 40f), 0.35f);
            _letterCol.style.opacity = 0f;
            UiMotion.SlideIn(_letterCol, new Vector2(-80f, 260f), 0.55f, end + 0.7f, UiMotion.Ease.OutCubic);
            var letter = _letter;
            UiMotion.Stamp(_stamp, -12f, 0.24f, end + 1.5f, () =>
            {
                Sfx.Play("evidence", 1f);
                UiMotion.Shake(letter, 8f, 0.35f);
                FeelHooks.ResultsStamp(ScreenPoint(_stamp));
                if (_fired >= 0) _stamp.schedule.Execute(() => Sfx.Play("fired", 0.8f)).StartingIn(350);
            });
        }

        sealed class Candidate
        {
            public LineFireScore Score;
            public VisualElement Card, Tray, Stamp;
            public Label Count;
            public int Shown;
        }

        /// <summary>Builds the shortlisted files and schedules the token drops; returns the time the count ends.</summary>
        float BuildCount(LineView v)
        {
            _cards.Clear();
            _tie.text = "";
            var m = v.Meeting;
            var list = new List<Candidate>();
            var order = m != null && m.Shortlist.Count > 0 ? m.Shortlist : null;
            if (m != null)
            {
                if (order != null)
                    foreach (int id in order)
                    {
                        LineFireScore sc = null;
                        foreach (var x in m.Scores) if (x.Player == id) sc = x;
                        if (sc == null) sc = FromVotes(v, id);
                        list.Add(new Candidate { Score = sc });
                    }
                else
                    foreach (var x in m.Scores) list.Add(new Candidate { Score = x });
            }
            if (list.Count == 0 && m != null && m.Fired >= 0) list.Add(new Candidate { Score = FromVotes(v, m.Fired) });

            // safe colleagues (not on the shortlist)
            var safe = new System.Text.StringBuilder();
            if (order != null)
                foreach (var p in v.Players)
                {
                    if (order.Contains(p.Id)) continue;
                    if (safe.Length > 0) safe.Append(", ");
                    safe.Append(p.Name);
                }
            _safe.text = safe.Length > 0 ? Loc.F("final.count.safe", safe.ToString()) : "";

            int n = 0;
            foreach (var c in list)
            {
                var s = c.Score;
                c.Card = Box("count-card").NoPick().AddTo(_cards);
                if (s.Player == v.Me) c.Card.AddToClassList("count-card-me");
                Badge(s.Player, "badge-xl").AddTo(c.Card);
                Title(MeetingText.Name(s.Player), "count-name").NoPick().AddTo(c.Card);
                var line = Box("row").NoPick().AddTo(c.Card);
                Bold(Loc.F("meeting.blame_n", s.Blame), "count-blame").NoPick().AddTo(line);
                for (int k = 0; k < s.Ihtars; k++) DossierDeck.IhtarMark(line);
                c.Tray = Box("count-tray").NoPick().AddTo(c.Card);
                c.Count = Title("0", "count-num").NoPick().AddTo(c.Card);
                c.Stamp = Box("count-stamp").NoPick().AddTo(c.Card);
                bool fired = m != null && s.Player == m.Fired;
                bool ok = fired ? MeetingText.ApplyStamp(c.Stamp, "stamps/stamp_kovuldu", "stamps_v3/stamp_fired")
                                : MeetingText.ApplyStamp(c.Stamp, "stamps/stamp_kurtuldu", "stamps_v3/stamp_survived");
                if (!ok) TextStamp(c.Stamp, Loc.T(fired ? "final.stamp.fired" : "final.stamp.survived"), fired ? "" : "stamp-green");
                c.Stamp.style.opacity = 0f;
                UiMotion.PopIn(c.Card, 0.3f, 0.1f + n * 0.12f, 0.88f);
                n++;
            }

            // anonymous tokens, round-robin so the piles grow side by side
            float t = 0.7f;
            const float step = 0.3f;
            bool more = true;
            for (int round = 0; more; round++)
            {
                more = false;
                foreach (var c in list)
                {
                    if (round >= c.Score.Votes) continue;
                    more = true;
                    var cand = c;
                    var token = Box("count-token").NoPick().AddTo(c.Tray);
                    if (!UiArt.Apply(token, "meeting_v3/vote_token")) token.AddToClassList("count-token-fallback");
                    token.style.opacity = 0f;
                    UiMotion.Tween(token, 0.3f, k =>
                    {
                        token.style.translate = new Translate(0f, Mathf.LerpUnclamped(-70f, 0f, k));
                        token.style.opacity = Mathf.Clamp01(k * 3f);
                    }, UiMotion.Ease.OutBounce, () =>
                    {
                        cand.Shown++;
                        cand.Count.text = cand.Shown.ToString();
                        UiMotion.Pulse(cand.Count, 0.18f, 0.2f);
                        Sfx.Play("vote", 0.5f);
                    }, t, "drop");
                    t += step;
                }
            }
            float end = t + 0.3f;

            // tie note (most votes shared: the engine broke it by blame, then warnings)
            int max = -1, atMax = 0;
            foreach (var c in list) { if (c.Score.Votes > max) { max = c.Score.Votes; atMax = 1; } else if (c.Score.Votes == max) atMax++; }
            if (atMax > 1 && _fired >= 0)
            {
                _tie.text = Loc.F("final.count.tie", _firedName);
                UiMotion.FadeIn(_tie, 0.3f, end);
            }

            foreach (var c in list)
            {
                bool fired = m != null && c.Score.Player == m.Fired;
                var card = c.Card;
                UiMotion.Stamp(c.Stamp, fired ? -10f : -6f, 0.22f, end + (fired ? 0.25f : 0f), () =>
                {
                    if (!fired) return;
                    card.AddToClassList("count-card-fired");
                    UiMotion.Shake(card, 8f, 0.3f);
                    Sfx.Play("evidence", 0.8f);
                });
            }
            return end + 0.25f;
        }

        static LineFireScore FromVotes(LineView v, int id)
        {
            var sc = new LineFireScore { Player = id };
            foreach (var p in v.Players) if (p.Id == id) { sc.Blame = p.Blame; sc.Ihtars = p.Ihtars; }
            if (v.Meeting != null) foreach (var x in v.Meeting.Votes) if (x.Target == id) sc.Votes++;
            sc.Score = sc.Votes;
            return sc;
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
                    Memo(Loc.F("final.letter.score", score.Votes, score.Blame), "letter-par").NoPick().AddTo(_letterBody);
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
