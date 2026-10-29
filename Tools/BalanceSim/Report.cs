using System.Text;
using BirimizKovulacak.Core;

namespace BalanceSim;

public static class Report
{
    static string P(int numerator, int denominator) => denominator == 0 ? "—" : $"{100.0 * numerator / denominator:F1}%";
    static string Rate<T>(IEnumerable<T> items, Func<T, bool> predicate)
    {
        var a = items.ToArray(); return P(a.Count(predicate), a.Length);
    }

    public static string Headlines(RunReport run)
    {
        var s = new StringBuilder();
        s.AppendLine("| Experiment | N | Quota success | Mean points / target | Fired: top / other / zero | Informant win | ≥1 winner |");
        s.AppendLine("|---|---:|---:|---:|---|---:|---:|");
        foreach (var g in run.Groups)
        {
            var m = g.Matches;
            s.AppendLine($"| {g.Label} | {m.Length} | {Rate(m, x => x.Result.CompanySucceeded)} | {m.Average(x => x.Result.QuotaPoints):F2} / {m.Average(x => x.Result.QuotaTarget):F2} | {Rate(m, x => x.FiredCategory == "top tier")} / {Rate(m, x => x.FiredCategory == "other")} / {Rate(m, x => x.FiredCategory == "zero")} | {Rate(m, x => x.Result.InformantWon)} | {Rate(m, x => x.Result.Players.Any(p => p.Won))} |");
        }
        return s.ToString();
    }

    public static string Render(RunReport run)
    {
        var s = new StringBuilder("# BalanceSim results\n\n");
        s.AppendLine($"Runtime: {run.Runtime}; logical processors: {run.LogicalProcessors}; workers: {run.Options.Threads}; simulation elapsed: {run.ElapsedSeconds:F1}s.");
        s.AppendLine();
        s.AppendLine("Overrides before headcount: " + (run.Options.Overrides.Count == 0 ? "none (CreateDefault)." : string.Join(", ", run.Options.Overrides.Select(x => "`" + x + "`"))) + "\n");
        s.AppendLine(Headlines(run));
        s.AppendLine("## Seeds and determinism\n");
        s.AppendLine("| Experiment | Engine seeds (inclusive) | Result hash digest (SHA-256, ordered) |");
        s.AppendLine("|---|---|---|");
        foreach (var g in run.Groups) s.AppendLine($"| {g.Label} | {g.Seed}–{g.Seed + g.Matches.Length - 1} | `{g.Digest}` |");
        foreach (var w in run.WeekGroups) s.AppendLine($"| Week {w.Players}p | {w.Seed}–{w.Seed + w.Weeks.Length * 5 - 1} | `{w.Digest}` |");

        if (run.Groups.Count > 0)
        {
            s.AppendLine("\n## Objective wins (final company + survival gates included)\n");
            s.Append("| Card |"); foreach (var g in run.Groups) s.Append($" {g.Label}: win % (N) |"); s.AppendLine();
            s.Append("|---|"); foreach (var g in run.Groups) s.Append("---:|"); s.AppendLine();
            foreach (var card in MatchConfig.CreateDefault().Objectives)
            {
                s.Append($"| {card.Id} |");
                foreach (var g in run.Groups)
                {
                    var owners = g.Matches.SelectMany(m => m.Result.Players).Where(p => p.Role == Role.Employee && p.ObjectiveCardId == card.Id).ToArray();
                    s.Append($" {Rate(owners, p => p.Won)} ({owners.Length}) |");
                }
                s.AppendLine();
            }
            s.AppendLine("\n## Incidents\n");
            s.Append("| Type |"); foreach (var g in run.Groups) s.Append($" {g.Label}: mean / discovered % |"); s.AppendLine();
            s.Append("|---|"); foreach (var g in run.Groups) s.Append("---:|"); s.AppendLine();
            foreach (SabotageType type in Enum.GetValues<SabotageType>())
            {
                s.Append($"| {type} |");
                foreach (var g in run.Groups)
                {
                    var incidents = g.Matches.SelectMany(m => m.Result.Incidents).Where(i => i.Type == type).ToArray();
                    s.Append($" {(double)incidents.Length / g.Matches.Length:F2} / {Rate(incidents, i => i.Discovered)} |");
                }
                s.AppendLine();
            }
            s.AppendLine("\n## Calls, footage, and action diagnostics\n");
            s.AppendLine("| Experiment | Incidents / match | Discovered | Footage / match | Footage with incident (hit count) | True actor / all footage (N) | True actor / incident windows | Calls / match | Answered | Tips / match | Tip hit / evaluated visits (N) | Requests / match | Rejected |");
            s.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var g in run.Groups)
            {
                var m = g.Matches; int n = m.Length;
                var inc = m.SelectMany(x => x.Result.Incidents).ToArray();
                int footage = m.Sum(x => x.FootageRequests), withIncident = m.Sum(x => x.FootageWithIncident), hits = m.Sum(x => x.FootageActorHits);
                int visits = m.Sum(x => x.TipInspections), calls = m.Sum(x => x.Calls), requests = m.Sum(x => x.ProcessedRequests);
                s.AppendLine($"| {g.Label} | {(double)inc.Length / n:F2} | {Rate(inc, i => i.Discovered)} | {(double)footage / n:F2} | {P(withIncident, footage)} ({withIncident}) | {P(hits, footage)} ({footage}) | {P(hits, withIncident)} | {(double)calls / n:F2} | {P(m.Sum(x => x.AnsweredCalls), calls)} | {(double)m.Sum(x => x.Tips) / n:F2} | {P(m.Sum(x => x.TipHits), visits)} ({visits}) | {(double)requests / n:F1} | {P(m.Sum(x => x.RejectedRequests), requests)} |");
            }
            s.AppendLine("\n## Inspection coverage (unique assigned desks)\n");
            s.AppendLine("| Experiment | N | Raid | Final | Surprise eligible matches | Surprise conditional | Surprise over all matches |");
            s.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
            foreach (var g in run.Groups)
            {
                var m = g.Matches; int n = m.Length, surprise = m.Count(x => x.HasSurprise);
                s.AppendLine($"| {g.Label} | {n} | {P(m.Sum(x => x.RaidInspected), n * g.Players)} | {P(m.Sum(x => x.FinalInspected), n * g.Players)} | {surprise} | {P(m.Sum(x => x.SurpriseInspected), surprise * g.Players)} | {P(m.Sum(x => x.SurpriseInspected), n * g.Players)} |");
            }
            s.AppendLine("\n## Fired player's performance rank\n\nCompetition rank, 1 = highest final performance; tied scores share rank.\n");
            s.AppendLine("| Experiment | Mean rank | Rank 1 | Rank 2 | Rank 3 | Rank 4 | Rank 5 | Rank 6 |");
            s.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var g in run.Groups)
            {
                s.Append($"| {g.Label} | {g.Matches.Average(x => x.FiredPerformanceRank):F2} |");
                for (int rank = 1; rank <= 6; rank++) s.Append($" {(rank <= g.Players ? Rate(g.Matches, x => x.FiredPerformanceRank == rank) : "—")} |");
                s.AppendLine();
            }
        }
        if (run.WeekGroups.Count > 0)
        {
            s.AppendLine("\n## Week mode\n");
            s.AppendLine("| Players | Weeks N | Mean spread | Median spread | P95 spread | Friday leader already led Wednesday (any) | All Friday leaders already led Wednesday | Ayın Elemanı tie | Wednesday tie |");
            s.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var g in run.WeekGroups)
            {
                var w = g.Weeks; var spread = w.Select(x => x.CareerSpread).Order().ToArray();
                s.AppendLine($"| {g.Players} | {w.Length} | {w.Average(x => x.CareerSpread):F2} | {spread[(spread.Length - 1) / 2]} | {spread[(int)Math.Ceiling(spread.Length * .95) - 1]} | {Rate(w, x => x.FridayLeaderAlreadyLeading)} | {Rate(w, x => x.AllFridayLeadersAlreadyLeading)} | {Rate(w, x => x.EmployeeTie)} | {Rate(w, x => x.WednesdayTie)} |");
            }
        }
        return s.ToString();
    }
}
