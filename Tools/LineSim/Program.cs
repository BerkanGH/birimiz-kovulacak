using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BirimizKovulacak.Core;

/// <summary>
/// v5 bot simulation (docs/V5_ENGINE_SPEC.md §15): seeded bot matches per head count and policy mix, reporting prim
/// by policy, suç distribution and who gets fired, to sanity-check docs/V5_DESIGN.md §12. Synthetic data: bots do
/// not talk, call or record.
/// </summary>
internal static class Program
{
    private sealed class Stats
    {
        internal int Matches, Steps, MaxSteps;
        internal double Seconds;
        internal readonly Dictionary<LineBotPolicy, long> Prim = new Dictionary<LineBotPolicy, long>();
        internal readonly Dictionary<LineBotPolicy, long> Blame = new Dictionary<LineBotPolicy, long>();
        /// <summary>Prim before the desk reveal (firing losses and desk shares removed).</summary>
        internal readonly Dictionary<LineBotPolicy, long> PreFire = new Dictionary<LineBotPolicy, long>();
        internal readonly Dictionary<LineBotPolicy, long> SurvivorPrim = new Dictionary<LineBotPolicy, long>();
        internal readonly Dictionary<LineBotPolicy, int> Survivors = new Dictionary<LineBotPolicy, int>();
        internal readonly Dictionary<LineBotPolicy, int> Players = new Dictionary<LineBotPolicy, int>();
        internal readonly Dictionary<LineBotPolicy, double> Wins = new Dictionary<LineBotPolicy, double>();
        internal readonly Dictionary<LineBotPolicy, int> FiredBy = new Dictionary<LineBotPolicy, int>();
        internal int Firings, MatchesWithGreedy, MatchesGreedyFired;
        internal long LeaderBlame, TotalBlame;
        internal readonly long[] BlameHistogram = new long[16];
        internal int Meeting1Votes, Meeting2Held, Investigations, GuiltyInvestigations, Whistles, Caught, Revotes, TieBreaks;
        internal long DeskTotal, DeskRemoved;
        internal ulong Hash = 14695981039346656037UL;

        internal void Add<T>(Dictionary<LineBotPolicy, T> d, LineBotPolicy p, Func<T, T> f) { d.TryGetValue(p, out T v); d[p] = f(v); }
    }

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static string F(double n) { return n.ToString("0.00", Inv); }
    private static double Pct(double n, double d) { return d == 0 ? 0 : 100.0 * n / d; }
    private static readonly LineBotPolicy[] Policies = { LineBotPolicy.Honest, LineBotPolicy.Greedy, LineBotPolicy.Mixed };

    private static int Main(string[] args)
    {
        try
        {
            int matches = 1000, seed = 5000, players = 0;
            double think = 15;
            string mix = "all", output = "Tools/LineSim/v5-sim.txt";
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--help")
                {
                    Console.WriteLine("LineSim --matches N --players all|3|4|5|6 --mix all|half|one-greedy|mixed --think seconds --seed N --output path");
                    return 0;
                }
                string flag = args[i];
                if (++i >= args.Length) throw new ArgumentException("Missing value for " + flag);
                switch (flag)
                {
                    case "--matches": matches = int.Parse(args[i], Inv); break;
                    case "--seed": seed = int.Parse(args[i], Inv); break;
                    case "--players": players = args[i] == "all" ? 0 : int.Parse(args[i], Inv); break;
                    case "--mix": mix = args[i]; break;
                    case "--think": think = double.Parse(args[i], Inv); break;
                    case "--output": output = args[i]; break;
                    default: throw new ArgumentException("Unknown flag " + flag);
                }
            }
            var mixes = new[] { "half", "one-greedy", "mixed" };
            if (matches < 1 || players != 0 && (players < 3 || players > 6) || think < 0 || think > 50 || mix != "all" && !mixes.Contains(mix))
                throw new ArgumentException("Invalid arguments.");
            var report = new StringBuilder();
            var watch = Stopwatch.StartNew();
            report.AppendLine(LineProtocol.RulesetVersion + " — SYNTHETIC BOT DATA (LineSim)");
            report.AppendLine("Bots never call, text or record (spec §13). Honest: share, leave, volunteer by a simple count (the cleanest Needed+1 by suç volunteer), whistle as a witness, vote for a known culprit else the highest suç.");
            report.AppendLine("Greedy: take, take, skip, whistle as a witness, investigate a Client Bonus partner who took, same voting rule. Mixed: honest or greedy per decision (50/50, seeded).");
            report.AppendLine("Mixes: half = each seat Honest or Greedy 50/50 (seeded per match); one-greedy = one seeded Greedy seat, the rest Honest; mixed = every seat Mixed.");
            report.AppendLine("Default config and timings; bots act after a seeded 0.." + F(think) + " s think delay per phase. Match length = Intro start to Results start.");
            report.AppendLine(matches + " matches per cell; seeds " + seed + ".." + (seed + matches - 1) + ".");
            report.AppendLine("Command: dotnet run -c Release --project Tools/LineSim/LineSim.csproj -- " + string.Join(" ", args));
            int total = 0;
            for (int n = 3; n <= 6; n++)
            {
                if (players != 0 && players != n) continue;
                foreach (string m in mixes)
                {
                    if (mix != "all" && mix != m) continue;
                    var stats = new Stats();
                    for (int i = 0; i < matches; i++) Run(n, m, seed + i, think, stats);
                    total += matches;
                    Write(report, n, m, stats);
                    if (m != "mixed") Console.WriteLine(n + "p " + m + ": honest " + F(Avg(stats.Prim, stats.Players, LineBotPolicy.Honest)) + " / greedy " +
                                      F(Avg(stats.Prim, stats.Players, LineBotPolicy.Greedy)) + " prim; greedy fired in " +
                                      F(Pct(stats.MatchesGreedyFired, stats.MatchesWithGreedy)) + "% of matches with a greedy bot");
                }
            }
            report.AppendLine();
            report.AppendLine("Completed " + total + " matches without a stall. Wall seconds: " + F(watch.Elapsed.TotalSeconds));
            string directory = Path.GetDirectoryName(output);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(output, report.ToString(), new UTF8Encoding(false));
            Console.WriteLine("Wrote " + output + "; " + total + " matches.");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.GetType().Name + ": " + e.Message);
            return 1;
        }
    }

    private static LineBotPolicy[] Assign(int n, string mix, int seed)
    {
        var r = new Random(seed * 7919 + n);
        var p = new LineBotPolicy[n];
        if (mix == "mixed") { for (int i = 0; i < n; i++) p[i] = LineBotPolicy.Mixed; return p; }
        if (mix == "one-greedy") { int g = r.Next(n); for (int i = 0; i < n; i++) p[i] = i == g ? LineBotPolicy.Greedy : LineBotPolicy.Honest; return p; }
        for (int i = 0; i < n; i++) p[i] = r.Next(2) == 0 ? LineBotPolicy.Honest : LineBotPolicy.Greedy;
        return p;
    }

    private static void Run(int n, string mix, int seed, double think, Stats stats)
    {
        var setup = new LineSetup { WeekId = 1, MatchId = "sim-" + n + "-" + mix + "-" + seed, Seed = seed };
        for (int i = 0; i < n; i++) setup.Players.Add(new LinePlayerInfo { Id = i, Name = "Bot " + i, IsBot = true });
        var config = LineConfig.CreateDefault();
        var engine = LineEngineFactory.Create(setup, config);
        var policy = Assign(n, mix, seed);
        var bots = new LineBot[n];
        for (int i = 0; i < n; i++) bots[i] = new LineBot(i, seed * 31 + i * 97, policy[i]) { ThinkSeconds = think };
        double now = 0;
        int steps = 0;
        while (!engine.IsFinished)
        {
            if (++steps > 5000) throw new InvalidOperationException("Stall: " + n + "p " + mix + " seed " + seed + " at " + engine.Phase);
            engine.Tick(now);
            for (int round = 0; round < 8; round++)
            {
                bool acted = false;
                for (int i = 0; i < n; i++)
                {
                    var view = engine.BuildView(i, now);
                    var action = bots[i].Next(view, config, now);
                    if (action == null) continue;
                    var result = engine.Apply(i, action, now);
                    if (!result.Accepted && result.Failure != LineFailure.AlreadyResolved)
                        throw new InvalidOperationException("Bot " + i + " " + view.Phase + " " + action + " " + result);
                    acted = true;
                }
                if (!acted) break;
            }
            foreach (var e in engine.DrainEvents())
            {
                if (e.Type == LineEventType.PhaseChanged && (LinePhase)e.A == LinePhase.MeetingVote) { if (engine.Day == 0) stats.Meeting1Votes++; }
                if (e.Type == LineEventType.PhaseChanged && (LinePhase)e.A == LinePhase.MeetingDossier && engine.Day == 1) stats.Meeting2Held++;
            }
            if (engine.Phase == LinePhase.Results) { engine.HostContinue(now); break; }
            double next = engine.PhaseEndTime;
            foreach (var b in bots) if (b.WakeAt > now) next = Math.Min(next, b.WakeAt);
            now = Math.Max(now, next);
        }
        var r = engine.Result ?? throw new InvalidOperationException("No result");
        stats.Matches++;
        stats.Steps += steps;
        stats.MaxSteps = Math.Max(stats.MaxSteps, steps);
        stats.Seconds += r.EndTime - r.StartTime;
        int leader = 0;
        foreach (var p in r.Players)
        {
            var pol = policy[p.Player];
            stats.Add(stats.Prim, pol, v => v + p.Prim);
            stats.Add(stats.Blame, pol, v => v + p.Blame);
            int desk = r.PrimLog.Where(x => x.Player == p.Player && (x.Reason == PrimReason.Fired || x.Reason == PrimReason.DeskShare)).Sum(x => x.Delta);
            stats.Add(stats.PreFire, pol, v => v + p.Prim - desk);
            if (!p.Fired) { stats.Add(stats.SurvivorPrim, pol, v => v + p.Prim); stats.Add(stats.Survivors, pol, v => v + 1); }
            stats.Add(stats.Players, pol, v => v + 1);
            if (p.Winner) stats.Add(stats.Wins, pol, v => v + 1.0 / r.Winners.Count);
            leader = Math.Max(leader, p.Blame);
            stats.TotalBlame += p.Blame;
            stats.BlameHistogram[Math.Min(p.Blame, stats.BlameHistogram.Length - 1)]++;
        }
        stats.LeaderBlame += leader;
        bool hasGreedy = policy.Contains(LineBotPolicy.Greedy);
        if (hasGreedy) stats.MatchesWithGreedy++;
        if (r.Fired.Any(id => policy[id] == LineBotPolicy.Greedy)) stats.MatchesGreedyFired++;
        foreach (var f in r.Firings)
        {
            stats.Firings++;
            stats.Add(stats.FiredBy, policy[f.Player], v => v + 1);
            stats.DeskTotal += f.Desk;
            stats.DeskRemoved += f.Removed;
            if (f.Decision == FiringDecision.Revote) stats.Revotes++;
            if (f.Decision >= FiringDecision.HigherBlame) stats.TieBreaks++;
        }
        foreach (var d in r.Incidents)
        {
            if (d.Kind == DossierKind.Investigation) { stats.Investigations++; if (d.N1 == 1) stats.GuiltyInvestigations++; }
            if (d.Kind == DossierKind.Whistle) stats.Whistles++;
            if (d.Kind == DossierKind.CashCaught) stats.Caught++;
        }
        stats.Hash = unchecked((stats.Hash ^ r.ResultHash) * 1099511628211UL);
    }

    private static double Avg<T>(Dictionary<LineBotPolicy, T> sum, Dictionary<LineBotPolicy, int> count, LineBotPolicy p)
    {
        if (!count.TryGetValue(p, out int c) || c == 0) return double.NaN;
        return Convert.ToDouble(sum.TryGetValue(p, out T s) ? s : default(T), Inv) / c;
    }

    private static void Write(StringBuilder b, int n, string mix, Stats s)
    {
        b.AppendLine();
        b.AppendLine(n + " players | " + mix + " | " + s.Matches + " matches | candidates " + LineConfig.CreateDefault().CandidatesFor(n) + ", firings " + LineConfig.CreateDefault().FiringsFor(n));
        b.AppendLine("Match length s (to Results): mean " + F(s.Seconds / s.Matches) + "; engine steps per match mean/max " + F((double)s.Steps / s.Matches) + "/" + s.MaxSteps);
        foreach (var p in Policies)
        {
            if (!s.Players.TryGetValue(p, out int count) || count == 0) continue;
            s.FiredBy.TryGetValue(p, out int fired);
            s.Wins.TryGetValue(p, out double wins);
            b.AppendLine("  " + p + ": players " + count + "; final prim mean " + F(Avg(s.Prim, s.Players, p)) + " (before the desk reveal " + F(Avg(s.PreFire, s.Players, p)) +
                         ", surviving seats " + F(Avg(s.SurvivorPrim, s.Survivors, p)) + "); suç mean " + F(Avg(s.Blame, s.Players, p)) +
                         "; fired " + fired + " (" + F(Pct(fired, count)) + "% of " + p + " seats); win share " + F(Pct(wins, count)) + "% per seat");
        }
        b.AppendLine("Greedy fired in " + F(Pct(s.MatchesGreedyFired, s.MatchesWithGreedy)) + "% of matches with a greedy bot (" + s.MatchesGreedyFired + "/" + s.MatchesWithGreedy + "); firings "
                     + s.Firings + ", greedy share " + F(Pct(s.FiredBy.TryGetValue(LineBotPolicy.Greedy, out int gf) ? gf : 0, s.Firings)) + "%");
        b.AppendLine("Suç: leader mean " + F((double)s.LeaderBlame / s.Matches) + "; total per match " + F((double)s.TotalBlame / s.Matches) +
                     "; distribution per player 0.." + (s.BlameHistogram.Length - 1) + "+: " + string.Join(" ", s.BlameHistogram.Select((x, i) => i + ":" + F(Pct(x, s.Matches * n)) + "%").Where((x, i) => s.BlameHistogram[i] > 0)));
        b.AppendLine("Meeting 1 vote held " + F(Pct(s.Meeting1Votes, s.Matches)) + "%; meeting 2 held " + F(Pct(s.Meeting2Held, s.Matches)) + "%; per match: investigations " +
                     F((double)s.Investigations / s.Matches) + " (guilty " + F(Pct(s.GuiltyInvestigations, s.Investigations)) + "%), whistles " + F((double)s.Whistles / s.Matches) +
                     ", caught takers " + F((double)s.Caught / s.Matches));
        b.AppendLine("Firings decided by revote " + F(Pct(s.Revotes, s.Firings)) + "%, by tie-break " + F(Pct(s.TieBreaks, s.Firings)) + "%; desk mean " + F((double)s.DeskTotal / Math.Max(1, s.Firings)) +
                     ", removed (remainder) mean " + F((double)s.DeskRemoved / Math.Max(1, s.Firings)) + "; result aggregate hash " + s.Hash.ToString("X16", Inv));
    }
}
