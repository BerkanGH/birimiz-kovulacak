using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Generic task reveal sheet (RoundReveal): output versus the maximum with a count-up, what it did to the company
    /// gauge, the public facts of the task (task.&lt;id&gt;.fact.&lt;key&gt;), the public ballots where the task has them,
    /// colleagues who missed their input, my income line and my blame changes. Built once per reveal.
    /// </summary>
    public sealed class DeskReveal
    {
        public void Build(VisualElement slot, LineView v, LineTaskReveal r)
        {
            var sheet = Box("dk-reveal").AddTo(slot);
            var head = Box("dk-reveal-head").NoPick().AddTo(sheet);
            var ic = Box("dk-reveal-iconbox").NoPick().AddTo(head);
            ic.style.backgroundColor = Dk.TaskColor(r.Kind);
            Dk.Icon(Dk.TaskIcon(r.Kind), "dk-reveal-icon").AddTo(ic);
            var hc = Box("dk-reveal-headcol").NoPick().AddTo(head);
            StampText(Loc.Upper(Dk.F("desk.reveal.band", r.Index + 1, Mathf.Max(1, v.TodayTasks.Count))), "dk-section-band").NoPick().AddTo(hc);
            Title(Dk.TaskName(r.Kind), "dk-reveal-title").NoPick().AddTo(hc);
            Box("spacer").NoPick().AddTo(head);

            int max = Mathf.Max(1, r.MaxOutput);
            float frac = Mathf.Clamp01(r.Output / (float)max);
            bool good = frac >= 0.65f;
            var stamp = Dk.Stamp(Dk.T(good ? "desk.reveal.stamp_good" : "desk.reveal.stamp_bad"),
                Dk.StampArt(good ? "stamp_onaylandi" : "stamp_reddedildi", good ? "stamp_approved" : "stamp_rejected"), "dk-reveal-stamp", good ? "dk-reveal-stamp-green" : "dk-reveal-stamp-red");
            stamp.AddTo(head);

            // output
            var outBox = Box("dk-reveal-out").NoPick().AddTo(sheet);
            var num = MemoBold("0", "dk-reveal-num").NoPick().AddTo(outBox);
            MemoBold("/ " + max, "dk-reveal-max").NoPick().AddTo(outBox);
            var oc = Box("dk-reveal-outcol").NoPick().AddTo(outBox);
            VisualElement fill;
            var bar = Bar(0f, out fill, "dk-reveal-bar");
            bar.pickingMode = PickingMode.Ignore;
            bar.AddTo(oc);
            fill.EnableInClassList("dk-gauge-fill-low", !good);
            Text(CompanyLine(v, r), "dk-reveal-company").NoPick().AddTo(oc);
            UiMotion.CountUp(num, 0, r.Output, 0.9f, null, 0.35f);
            UiMotion.Tween(fill, 0.9f, t => fill.style.width = Length.Percent(frac * 100f * t), UiMotion.Ease.OutCubic, null, 0.35f, "fill");
            UiMotion.Stamp(stamp, -8f, 0.24f, 1.1f, () => Dk.Play("stamp"));

            // facts and ballots
            var body = Box("dk-reveal-body").NoPick().AddTo(sheet);
            var facts = Box("dk-reveal-col").NoPick().AddTo(body);
            StampText(Loc.Upper(Dk.T("desk.reveal.facts")), "dk-reveal-colband").NoPick().AddTo(facts);
            int shown = 0;
            for (int i = 0; i < r.PublicFacts.Count; i++)
            {
                var f = r.PublicFacts[i];
                var row = Box("dk-fact").NoPick().AddTo(facts);
                Box("dk-bullet").NoPick().AddTo(row);
                Text(Dk.FactText(r.Kind, f), "dk-fact-text").NoPick().AddTo(row);
                shown++;
            }
            if (shown == 0) Text(Dk.T("desk.reveal.no_facts"), "dk-empty").NoPick().AddTo(facts);
            if (r.Votes.Count > 0 && !HideBallots(r.Kind))
            {
                var votes = Box("dk-reveal-col").NoPick().AddTo(body);
                StampText(Loc.Upper(Dk.T(r.Kind == TaskKind.PhishingCheck ? "desk.reveal.flags" : "desk.reveal.ballots")), "dk-reveal-colband").NoPick().AddTo(votes);
                for (int i = 0; i < r.Votes.Count; i++)
                {
                    var vote = r.Votes[i];
                    var row = Box("dk-ballot").NoPick().AddTo(votes);
                    Dk.Badge(vote.Voter, "badge-xs").AddTo(row);
                    Bold(Dk.Name(vote.Voter), "dk-ballot-who").Plain().NoPick().AddTo(row);
                    Box("spacer").NoPick().AddTo(row);
                    Bold(Dk.BallotText(r.Kind, vote.Target), "dk-ballot-what").Plain().NoPick().AddTo(row);
                }
            }
            if (r.Missed.Count > 0)
            {
                var miss = Box("dk-reveal-miss").NoPick().AddTo(sheet);
                Dk.Icon("warning", "dk-reveal-miss-icon").AddTo(miss);
                string names = "";
                for (int i = 0; i < r.Missed.Count; i++) names += (i > 0 ? ", " : "") + Dk.Name(r.Missed[i]);
                Bold(Dk.F("desk.reveal.missed", names), "dk-reveal-miss-text").Plain().NoPick().AddTo(miss);
            }

            // my line: income and blame
            var mine = Box("dk-reveal-mine").NoPick().AddTo(sheet);
            var cfg = Dk.Config;
            Bold(Dk.F("desk.reveal.income", v.Mine.BlockIncome, cfg.BlockIncomeCap), "dk-reveal-mine-chip").NoPick().AddTo(mine);
            int blame = 0;
            for (int i = 0; i < r.Deltas.Count; i++) if (r.Deltas[i].Player == v.Me) blame += r.Deltas[i].Delta;
            if (blame != 0) Bold(Dk.F("desk.reveal.blame", Dk.Delta(blame)), "dk-reveal-mine-chip", blame > 0 ? "dk-bad" : "dk-good").NoPick().AddTo(mine);
            Text(Dk.T("desk.reveal.next"), "dk-reveal-next").NoPick().AddTo(mine);
            Dk.NoRich(sheet);

            UiMotion.SlideIn(sheet, new Vector2(0, 70f), 0.36f, 0f, UiMotion.Ease.OutBack);
            Dk.Play("phase");
        }

        /// <summary>Budget requests and room bookings already appear per player in the public facts (grant / booking lines).</summary>
        static bool HideBallots(TaskKind kind) { return kind == TaskKind.RoomBooking || kind == TaskKind.BudgetSplit; }

        static string CompanyLine(LineView v, LineTaskReveal r)
        {
            var c = v.Company;
            if (c.ProvisionalPermille < 0) return Dk.F("desk.reveal.company_measuring", Dk.Delta(r.Output));
            return Dk.F("desk.reveal.company", Dk.Delta(r.Output), Dk.Pct(c.ProvisionalPermille), c.TargetPercent);
        }

        public void Tick() { }
    }
}
