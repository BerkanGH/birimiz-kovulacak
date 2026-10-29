using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// RoundReveal sheet: a type-specific animated outcome (pairs flip Share/Take, vote tallies count up, complaint
    /// slips pile up, the code flips digit by digit) followed by the blame deltas counting up. Built once per reveal.
    /// </summary>
    public sealed class DeskReveal
    {
        const float DeltaStart = 3.6f;

        public void Build(VisualElement slot, LineView v, LineRoundReveal r)
        {
            var sheet = Box("dk-reveal").AddTo(slot);
            var head = Box("dk-reveal-head").NoPick().AddTo(sheet);
            Dk.Icon(Dk.RoundIcon(r.Type), "dk-reveal-icon").AddTo(head);
            var hc = Box("dk-reveal-headcol").NoPick().AddTo(head);
            StampText(Loc.Upper(Dk.F("desk.reveal.band", r.Index + 1, v.TodayRounds.Count)), "dk-section-band").NoPick().AddTo(hc);
            Title(Dk.RoundName(r.Type), "dk-reveal-title").NoPick().AddTo(hc);

            var body = Box("dk-reveal-body").NoPick().AddTo(sheet);
            switch (r.Type)
            {
                case RoundType.PrimHavuzu: Pairs(body, v, r); break;
                case RoundType.FarkliEposta: OddEmail(body, v, r); break;
                case RoundType.KayipRapor: LostReport(body, v, r); break;
                case RoundType.IhbarKutusu: Complaints(body, v, r); break;
                case RoundType.TerfiListesi: Promotion(body, v, r); break;
                case RoundType.OrtakSifre: Password(body, v, r); break;
            }
            Deltas(sheet, v, r);
            UiMotion.SlideIn(sheet, new Vector2(0, 70f), 0.36f, 0f, UiMotion.Ease.OutBack);
            Dk.Play("phase");
        }

        public void Tick() { }

        // ------------------------------------------------------------------ helpers

        static VisualElement Who(int id, string size = "badge-m", string cls = "dk-who")
        {
            var w = Box(cls).NoPick();
            Dk.Badge(id, size).AddTo(w);
            Bold(Dk.Name(id), "dk-who-name").NoPick().AddTo(w);
            return w;
        }

        static int DeltaOf(LineRoundReveal r, int player)
        {
            int d = 0;
            for (int i = 0; i < r.Deltas.Count; i++) if (r.Deltas[i].Player == player) d += r.Deltas[i].Delta;
            return d;
        }

        static VisualElement StampAt(VisualElement parent, string text, string art, string cls, float delay, float rot = -8f)
        {
            var s = Dk.Stamp(text, art, cls).AddTo(parent);
            UiMotion.Stamp(s, rot, 0.24f, delay, () => { Dk.Play("stamp"); UiMotion.Shake(parent, 5f, 0.25f); });
            return s;
        }

        /// <summary>Vote tally: one row per colleague with a growing bar and the voters' small badges.</summary>
        static void Tally(VisualElement parent, LineView v, LineRoundReveal r, int highlight, float delay)
        {
            var box = Box("dk-tally").NoPick().AddTo(parent);
            int max = 1;
            for (int i = 0; i < r.Counts.Count; i++) if (r.Counts[i] > max) max = r.Counts[i];
            float d = delay;
            for (int i = 0; i < v.Players.Count; i++)
            {
                int id = v.Players[i].Id;
                int count = id < r.Counts.Count ? r.Counts[id] : 0;
                var row = Box("dk-tally-row").NoPick().AddTo(box);
                if (id == highlight) row.AddToClassList("dk-tally-hot");
                Who(id, "badge-s").AddTo(row);
                var track = Box("dk-tally-track").NoPick().AddTo(row);
                var fill = Box("dk-tally-fill").NoPick().AddTo(track);
                var voters = Box("dk-tally-voters").NoPick().AddTo(track);
                for (int k = 0; k < r.Votes.Count; k++)
                    if (r.Votes[k].Target == id) Dk.Badge(r.Votes[k].Voter, "badge-xs").AddTo(voters);
                var n = MemoBold("0", "dk-tally-n").NoPick().AddTo(row);
                float target = count / (float)max;
                fill.style.width = Length.Percent(0);
                voters.style.opacity = 0f;
                UiMotion.Tween(fill, 0.6f, t => fill.style.width = Length.Percent(target * t * 100f), UiMotion.Ease.OutCubic, null, d, "grow");
                UiMotion.FadeIn(voters, 0.3f, d + 0.4f);
                if (count > 0) UiMotion.CountUp(n, 0, count, 0.6f, Num, d);
                d += 0.12f;
            }
        }

        // ------------------------------------------------------------------ Bonus Pool

        static void Pairs(VisualElement body, LineView v, LineRoundReveal r)
        {
            var list = Box("dk-pairs").NoPick().AddTo(body);
            float d = 0.5f;
            for (int i = 0; i < r.Pairs.Count; i++)
            {
                var p = r.Pairs[i];
                var row = Box("dk-pair").NoPick().AddTo(list);
                if (p.A == v.Me || p.B == v.Me) row.AddToClassList("dk-pair-me");
                Who(p.A).AddTo(row);
                ChoiceCard(row, p.ChoiceA, d);
                var vs = Box("dk-pair-vs").NoPick().AddTo(row);
                Bold(Outcome(p), "dk-pair-outcome").NoPick().AddTo(vs);
                ChoiceCard(row, p.ChoiceB, d + 0.35f);
                var wb = Who(p.B).AddTo(row);
                wb.AddToClassList("dk-who-right");
                if (p.C >= 0)
                {
                    row.AddToClassList("dk-pair-trio");
                    if (p.C == v.Me) row.AddToClassList("dk-pair-me");
                    ChoiceCard(row, p.ChoiceC, d + 0.7f);
                    var wc = Who(p.C).AddTo(row);
                    wc.AddToClassList("dk-who-right");
                    d += 0.35f;
                }
                UiMotion.SlideIn(row, new Vector2(-30f, 0), 0.3f, 0.15f + i * 0.12f);
                d += 0.8f;
            }
        }

        static string Outcome(LinePairReveal p)
        {
            int n = 2 + (p.C >= 0 ? 1 : 0);
            int takers = (p.ChoiceA == 1 ? 1 : 0) + (p.ChoiceB == 1 ? 1 : 0) + (p.C >= 0 && p.ChoiceC == 1 ? 1 : 0);
            if (takers == 0) return Dk.T("desk.reveal.pair.both_share");
            if (takers == n) return Dk.T("desk.reveal.pair.both_take");
            return takers == 1 ? Dk.T("desk.reveal.pair.one_took") : Dk.T("desk.reveal.pair.two_took");
        }

        static void ChoiceCard(VisualElement row, int choice, float delay)
        {
            var c = Box("dk-cc", "dk-cc-down").NoPick().AddTo(row);
            var l = StampText("?", "dk-cc-text").NoPick().AddTo(c);
            UiMotion.Flip(c, () =>
            {
                c.RemoveFromClassList("dk-cc-down");
                c.AddToClassList(choice == 1 ? "dk-cc-take" : "dk-cc-share");
                l.text = Loc.Upper(Dk.T(choice == 1 ? "desk.choice.take" : "desk.choice.share"));
                Dk.Play(choice == 1 ? "stamp" : "pickup", null, 0.7f);
            }, 0.42f, delay);
        }

        // ------------------------------------------------------------------ Odd Email

        static void OddEmail(VisualElement body, LineView v, LineRoundReveal r)
        {
            var cols = Box("dk-reveal-cols").NoPick().AddTo(body);
            var left = Box("dk-reveal-col").NoPick().AddTo(cols);
            var mails = Box("dk-reveal-mails").NoPick().AddTo(left);
            var a = Box("dk-mini-mail").NoPick().AddTo(mails);
            StampText(Loc.Upper(Dk.T("desk.reveal.odd.majority")), "dk-mini-mail-band").NoPick().AddTo(a);
            Memo(Dk.Topic(r.TopicId), "dk-mini-mail-text").NoPick().AddTo(a);
            UiMotion.SlideIn(a, new Vector2(-30, 0), 0.3f, 0.3f);

            var hidden = Box("dk-reveal-hidden").NoPick().AddTo(left);
            Who(r.Hidden, "badge-l", "dk-who-big").AddTo(hidden);
            var guess = Box("dk-reveal-guess").NoPick().AddTo(hidden);
            if (r.OddGuess >= 0)
            {
                Text(Dk.T("desk.reveal.odd.guess"), "dk-reveal-small").NoPick().AddTo(guess);
                Memo("“" + Dk.Topic(r.OddGuess) + "”", "dk-reveal-guess-text").NoPick().AddTo(guess);
                Bold(Dk.T(r.OddGuessCorrect ? "desk.reveal.odd.guess_right" : "desk.reveal.odd.guess_wrong"), r.OddGuessCorrect ? "t-good" : "t-bad").NoPick().AddTo(guess);
            }
            else Text(Dk.T("desk.reveal.odd.no_guess"), "dk-reveal-small").NoPick().AddTo(guess);
            UiMotion.PopIn(hidden, 0.3f, 0.8f);

            var right = Box("dk-reveal-col").NoPick().AddTo(cols);
            Bold(Dk.T("desk.reveal.votes"), "dk-reveal-sub").NoPick().AddTo(right);
            Tally(right, v, r, r.Hidden, 1.1f);
            bool caught = r.Winner >= 0 && r.Winner == r.Hidden;
            StampAt(hidden, Dk.T(caught ? "desk.reveal.caught" : "desk.reveal.escaped"), caught ? null : null, caught ? "dk-reveal-stamp" : "dk-reveal-stamp-green", 2.6f);
        }

        // ------------------------------------------------------------------ Lost Report

        static void LostReport(VisualElement body, LineView v, LineRoundReveal r)
        {
            var cols = Box("dk-reveal-cols").NoPick().AddTo(body);
            var left = Box("dk-reveal-col").NoPick().AddTo(cols);
            var hidden = Box("dk-reveal-hidden").NoPick().AddTo(left);
            Text(Dk.T("desk.reveal.lost.culprit"), "dk-reveal-small").NoPick().AddTo(hidden);
            Who(r.Hidden, "badge-l", "dk-who-big").AddTo(hidden);
            UiMotion.PopIn(hidden, 0.3f, 0.4f);
            var slips = Box("dk-clue-slips").NoPick().AddTo(left);
            float d = 0.7f;
            for (int i = 0; i < v.Players.Count; i++)
            {
                int id = v.Players[i].Id;
                int clue = id < r.Clues.Count ? r.Clues[id] : -1;
                if (clue < 0) continue;
                var s = Box("dk-clue-slip").NoPick().AddTo(slips);
                if (id == r.Hidden) s.AddToClassList("dk-clue-slip-culprit");
                Dk.Badge(id, "badge-xs").AddTo(s);
                Memo(Dk.F("desk.reveal.lost.slip", Dk.Name(clue)), "dk-clue-text").NoPick().AddTo(s);
                Dk.SetTilt(s, Dk.Tilt(id + 7, 2f));
                UiMotion.PopIn(s, 0.25f, d);
                d += 0.1f;
            }
            var right = Box("dk-reveal-col").NoPick().AddTo(cols);
            Bold(Dk.T("desk.reveal.votes"), "dk-reveal-sub").NoPick().AddTo(right);
            Tally(right, v, r, r.Hidden, 1.0f);
            bool caught = r.Winner >= 0 && r.Winner == r.Hidden;
            StampAt(hidden, Dk.T(caught ? "desk.reveal.caught" : "desk.reveal.escaped"), null, caught ? "dk-reveal-stamp" : "dk-reveal-stamp-green", 2.5f);
        }

        // ------------------------------------------------------------------ Complaint Box

        static void Complaints(VisualElement body, LineView v, LineRoundReveal r)
        {
            var piles = Box("dk-piles").NoPick().AddTo(body);
            int max = 0;
            for (int i = 0; i < r.Counts.Count; i++) if (r.Counts[i] > max) max = r.Counts[i];
            float d = 0.4f;
            var tops = new List<VisualElement>();
            for (int i = 0; i < v.Players.Count; i++)
            {
                int id = v.Players[i].Id;
                int count = id < r.Counts.Count ? r.Counts[id] : 0;
                var col = Box("dk-pile").NoPick().AddTo(piles);
                var stack = Box("dk-pile-stack").NoPick().AddTo(col);
                int k = 0;
                for (int c = 0; c < r.Complaints.Count; c++)
                {
                    if (r.Complaints[c].Target != id) continue;
                    var slip = Box("dk-pile-slip").NoPick().AddTo(stack);
                    Text(Dk.ComplaintReason(r.Complaints[c].Reason), "dk-pile-slip-text").NoPick().AddTo(slip);
                    Dk.SetTilt(slip, Dk.Tilt(c * 13 + id, 4f));
                    slip.style.opacity = 0f;
                    UiMotion.SlideIn(slip, new Vector2(0, -160f), 0.32f, d + k * 0.22f, UiMotion.Ease.OutBounce);
                    k++;
                }
                var n = Title("0", "dk-pile-n").NoPick().AddTo(col);
                if (count > 0) UiMotion.CountUp(n, 0, count, 0.3f + 0.22f * count, Num, d);
                Who(id, "badge-m", "dk-who-col").AddTo(col);
                if (max > 0 && count == max) tops.Add(col);
                d += 0.15f + k * 0.12f;
            }
            if (max == 0) Text(Dk.T("desk.reveal.complaint.none"), "dk-reveal-small").NoPick().AddTo(body);
            for (int i = 0; i < tops.Count; i++) StampAt(tops[i], Dk.T("desk.reveal.complaint.top"), null, "dk-pile-stamp", 2.4f + i * 0.2f);
        }

        // ------------------------------------------------------------------ Promotion List

        static void Promotion(VisualElement body, LineView v, LineRoundReveal r)
        {
            var cols = Box("dk-reveal-cols").NoPick().AddTo(body);
            var left = Box("dk-reveal-col").NoPick().AddTo(cols);
            Bold(Dk.T("desk.reveal.promo.who_backed"), "dk-reveal-sub").NoPick().AddTo(left);
            var list = Box("dk-arrows").NoPick().AddTo(left);
            float d = 0.4f;
            for (int i = 0; i < r.Votes.Count; i++)
            {
                var vote = r.Votes[i];
                var row = Box("dk-arrow-row").NoPick().AddTo(list);
                if (vote.Target == r.Winner) row.AddToClassList("dk-arrow-row-win");
                Who(vote.Voter, "badge-s").AddTo(row);
                Box("dk-arrow").NoPick().AddTo(row);
                Who(vote.Target, "badge-s").AddTo(row);
                UiMotion.SlideIn(row, new Vector2(-40f, 0), 0.26f, d);
                d += 0.18f;
            }
            if (r.Votes.Count == 0) Text(Dk.T("desk.reveal.promo.nobody_backed"), "dk-reveal-small").NoPick().AddTo(left);
            var right = Box("dk-reveal-col", "dk-reveal-col-center").NoPick().AddTo(cols);
            if (r.Winner >= 0)
            {
                var podium = Box("dk-podium").NoPick().AddTo(right);
                Dk.Icon("promotion", "dk-podium-icon").AddTo(podium);
                Who(r.Winner, "badge-xl", "dk-who-big").AddTo(podium);
                UiMotion.PopIn(podium, 0.4f, d + 0.2f, 0.6f);
                StampAt(podium, Dk.T("desk.reveal.promo.promoted"), null, "dk-reveal-stamp-green", d + 0.8f);
            }
            else
            {
                var none = Box("dk-podium", "dk-podium-empty").NoPick().AddTo(right);
                Title(Dk.T("desk.reveal.promo.tie"), "dk-podium-none").NoPick().AddTo(none);
                UiMotion.PopIn(none, 0.3f, d);
            }
        }

        // ------------------------------------------------------------------ Shared Password

        static void Password(VisualElement body, LineView v, LineRoundReveal r)
        {
            int len = v.Round != null && v.Round.CodeLength > 0 ? v.Round.CodeLength : Mathf.Max(1, v.Players.Count);
            string code = r.Code >= 0 ? r.Code.ToString().PadLeft(len, '0') : new string('?', len);
            var owners = v.Round != null ? v.Round.PositionOwners : null;
            var cols = Box("dk-reveal-cols").NoPick().AddTo(body);
            var left = Box("dk-reveal-col", "dk-reveal-col-center").NoPick().AddTo(cols);
            var box = Box("dk-code").NoPick().AddTo(left);
            Text(Dk.T("desk.reveal.code.real"), "dk-reveal-small").NoPick().AddTo(box);
            var row = Box("dk-code-row").NoPick().AddTo(box);
            for (int i = 0; i < len; i++)
            {
                var cell = Box("dk-code-cell").NoPick().AddTo(row);
                var digit = Box("dk-code-digit", "dk-code-digit-down").NoPick().AddTo(cell);
                var l = MemoBold("?", "dk-code-digit-text").NoPick().AddTo(digit);
                if (owners != null && i < owners.Count) Dk.Badge(owners[i], "badge-s").AddTo(cell);
                char ch = code[i];
                UiMotion.Flip(digit, () => { digit.RemoveFromClassList("dk-code-digit-down"); l.text = ch.ToString(); Dk.Play("click", null, 0.6f); }, 0.36f, 0.4f + i * 0.22f);
            }
            float after = 0.5f + len * 0.22f;

            // everyone's locked code; matching digits light up
            var right = Box("dk-reveal-col").NoPick().AddTo(cols);
            Bold(Dk.T("desk.reveal.code.entered"), "dk-reveal-sub").NoPick().AddTo(right);
            int exact = 0;
            float d = after;
            for (int i = 0; i < v.Players.Count; i++)
            {
                int id = v.Players[i].Id;
                int entered = id < r.CodesByPlayer.Count ? r.CodesByPlayer[id] : -1;
                var line = Box("dk-entered").NoPick().AddTo(right);
                if (id == r.Hidden) line.AddToClassList("dk-entered-saboteur");
                Who(id, "badge-s").AddTo(line);
                var digits = Box("dk-entered-digits").NoPick().AddTo(line);
                if (entered < 0) Text(Dk.T("desk.reveal.code.nothing"), "dk-reveal-small").NoPick().AddTo(digits);
                else
                {
                    string e = entered.ToString().PadLeft(len, '0');
                    bool all = e == code;
                    if (all) { exact++; line.AddToClassList("dk-entered-exact"); }
                    for (int k = 0; k < len && k < e.Length; k++)
                    {
                        var c = MemoBold(e[k].ToString(), "dk-entered-digit").NoPick().AddTo(digits);
                        if (r.Code >= 0 && e[k] == code[k]) c.AddToClassList("dk-entered-hit");
                    }
                }
                UiMotion.SlideIn(line, new Vector2(30f, 0), 0.26f, d);
                d += 0.12f;
            }
            var result = Box("dk-code-result").NoPick().AddTo(box);
            Bold(Dk.F("desk.reveal.code.majority", exact, v.Players.Count, v.Players.Count / 2 + 1), "dk-code-result-text").NoPick().AddTo(result);
            UiMotion.FadeIn(result, 0.3f, d);
            StampAt(box, Dk.T(r.Solved ? "desk.reveal.code.stamp_cracked" : "desk.reveal.code.stamp_locked"), null, r.Solved ? "dk-reveal-stamp-green" : "dk-reveal-stamp", d + 0.3f);
            var sab = Box("dk-reveal-hidden", "dk-reveal-hidden-row").NoPick().AddTo(left);
            Text(Dk.T("desk.reveal.code.saboteur"), "dk-reveal-small").NoPick().AddTo(sab);
            Who(r.Hidden, "badge-m", "dk-who").AddTo(sab);
            UiMotion.PopIn(sab, 0.3f, d + 0.6f);
        }

        // ------------------------------------------------------------------ blame deltas

        static void Deltas(VisualElement sheet, LineView v, LineRoundReveal r)
        {
            var foot = Box("dk-deltas").NoPick().AddTo(sheet);
            StampText(Loc.Upper(Dk.T("desk.reveal.blame")), "dk-deltas-band").NoPick().AddTo(foot);
            var row = Box("dk-deltas-row").NoPick().AddTo(foot);
            float d = DeltaStart;
            for (int i = 0; i < v.Players.Count; i++)
            {
                var p = v.Players[i];
                int delta = DeltaOf(r, p.Id);
                var chip = Box("dk-delta").NoPick().AddTo(row);
                if (p.Id == v.Me) chip.AddToClassList("dk-delta-me");
                if (delta == 0) chip.AddToClassList("dk-delta-zero");
                Dk.Badge(p.Id, "badge-s").AddTo(chip);
                var col = Box("dk-delta-col").NoPick().AddTo(chip);
                Bold(p.Name, "dk-delta-name").NoPick().AddTo(col);
                var total = MemoBold(Num(Mathf.Max(0, p.Blame - delta)), "dk-delta-total").NoPick().AddTo(col);
                var dl = Title(Dk.Delta(delta), "dk-delta-n").NoPick().AddTo(chip);
                dl.AddToClassList(delta > 0 ? "dk-delta-bad" : delta < 0 ? "dk-delta-good" : "dk-delta-none");
                chip.style.opacity = 0f;
                UiMotion.PopIn(chip, 0.28f, d);
                if (delta != 0)
                {
                    int from = Mathf.Max(0, p.Blame - delta);
                    UiMotion.CountUp(total, from, p.Blame, 0.6f, n => Dk.F("desk.reveal.blame_total", n), d + 0.3f);
                    float dd = d + 0.3f;
                    dl.schedule.Execute(() => { UiMotion.Pulse(dl, 0.3f, 0.3f); Dk.Play(delta > 0 ? "deny" : "complete", null, 0.5f); }).StartingIn((long)(dd * 1000));
                }
                else total.text = Dk.F("desk.reveal.blame_total", p.Blame);
                d += 0.14f;
            }
        }
    }
}
