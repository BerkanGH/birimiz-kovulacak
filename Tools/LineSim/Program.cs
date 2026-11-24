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
/// not talk, call or record. <c>--vote richest</c> (default, the game's bots) or <c>blame</c> (the old rule) picks
/// <see cref="LineBotVoteStyle"/>; the vote lines report whether the votes found the richest player. <c>--rules new</c>
/// (default: the v5.3 config defaults, bots with <see cref="LineBotPunishStyle.Deals"/>), <c>v52</c> (the one-shot
/// investigation / whistle at fixed stakes: reproduces the v5.2 statistics) or <c>old</c> (every v5.0 number and
/// switch, meeting 2 and no declarations, <see cref="LineBotPunishStyle.Legacy"/>: reproduces the v5.0 statistics).
/// v5.2: no meeting 2 (the short shift's suç lands at its reveal), the candidates' prim declarations and the results'
/// awards / headlines (<see cref="LineStory"/>); the Declarations and Story lines report them.
/// v5.3: "Dosya" and "Haftanın gündemi"; the Files, Stakes and Week types lines report them. <c>--stakes fixed</c>
/// plays every task at the normal level (the default for v52 / old), <c>permutation</c> draws the week type.
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
        // v5.1: the shift, candidates and firings
        internal long ShiftVolunteers, ShiftCleaned, ShiftSkipBlame, Candidates;
        internal int FewerFirings, NoFiring;
        internal long DeskTotal, DeskRemoved;
        internal ulong Hash = 14695981039346656037UL;
        // votes vs the true balances (taken when each crisis / final vote starts)
        internal int FinalFirings, FiredRichestAlive, FiredRichestOption, FiredBlameLeader, CrisisVotes, CrisisRichest, CrisisBlameLeader;
        internal int EstimateChecks, EstimateTopHits, EstimateErrors, VoteRounds, Shared;
        internal double EstimateAbsError, VoteTopShare, WinnerPrim, WinnerGap;
        // v5.2: prim declarations and the results' story
        internal int DeclCandidates, DeclMade, DeclTrue, DeclLow, FiredDeclared, FiredTrue, ShortShifts;
        internal long DeclClaim, DeclActual, Awards, Headlines;
        internal readonly Dictionary<LineBotPolicy, int> DeclBy = new Dictionary<LineBotPolicy, int>(), DeclTrueBy = new Dictionary<LineBotPolicy, int>();
        internal readonly int[] AwardKinds = new int[LineAwards.Count + 1];
        internal readonly int[] HeadlineKinds = new int[7];
        // v5.3: files (by what became of them and when they were opened) and the week's stakes
        internal long Files, FilesBonus, FilesCash, FilesOpened, FilesEarly, FilesMeeting, FilesShift, FilesLate, FilesShredded, FilesVoid, FilesExpired;
        /// <summary>[task 0..2, level 0..2]: greedy choices (take the bonus / the cash, skip the shift) and seats.</summary>
        internal readonly long[,] LevelTakes = new long[3, 3], LevelSeats = new long[3, 3];
        internal readonly int[] WeekMatches = new int[LineStakes.WeekTypes];
        internal readonly long[] WeekPrim = new long[LineStakes.WeekTypes], WeekBlame = new long[LineStakes.WeekTypes];
        internal readonly long[,] WeekTakes = new long[LineStakes.WeekTypes, 3];

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
            string mix = "all", output = "Tools/LineSim/v5-sim.txt", vote = "richest", rules = "new", stakes = "";
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--help")
                {
                    Console.WriteLine("LineSim --matches N --players all|3|4|5|6 --mix all|half|one-greedy|mixed|lobby --vote richest|blame --rules new|v52|old --stakes permutation|fixed --think seconds --seed N --output path");
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
                    case "--vote": vote = args[i]; break;
                    case "--rules": rules = args[i]; break;
                    case "--stakes": stakes = args[i]; break;
                    default: throw new ArgumentException("Unknown flag " + flag);
                }
            }
            var mixes = new[] { "half", "one-greedy", "mixed", "lobby" };
            if (matches < 1 || players != 0 && (players < 3 || players > 6) || think < 0 || think > 50 || mix != "all" && !mixes.Contains(mix) || vote != "richest" && vote != "blame" || rules != "new" && rules != "old" && rules != "v52" || stakes != "" && stakes != "fixed" && stakes != "permutation")
                throw new ArgumentException("Invalid arguments.");
            var style = vote == "blame" ? LineBotVoteStyle.MostBlame : LineBotVoteStyle.Richest;
            bool old = rules == "old", v52 = rules == "v52";
            if (stakes == "") stakes = old || v52 ? "fixed" : "permutation";
            if (old && stakes != "fixed") throw new ArgumentException("--rules old plays fixed stakes.");
            var mode = new Mode { Old = old, V52 = v52, Permutation = stakes == "permutation" };
            var report = new StringBuilder();
            var watch = Stopwatch.StartNew();
            report.AppendLine((old ? "v5.0-kayit rules (--rules old) on " : v52 ? "v5.2-kayit rules (--rules v52) on " : "") + LineProtocol.RulesetVersion + " — SYNTHETIC BOT DATA (LineSim)");
            if (old)
            {
                report.AppendLine("Bots never call, text or record (spec §13). Honest: share, leave, volunteer by a simple count (the cleanest Needed+1 by suç volunteer), whistle as a witness.");
                report.AppendLine("Greedy: take, take, skip, whistle as a witness, investigate a Client Bonus partner who took. Mixed: honest or greedy per decision (50/50, seeded).");
                report.AppendLine("Rules: every v5.0 number and switch (Client Bonus 2/2, investigation 4 prim / refund 3 / +3 suç / false +3, whistle +2 and the cash taken back, skippers +2 always, meeting 2 vote, 0 suç can be a candidate).");
            }
            else if (!v52)
            {
                report.AppendLine("Bots never call, text or record (spec §13), so nobody pays for silence here. Honest: share, leave, volunteer by a simple count (the cleanest Needed+1 by suç volunteer),");
                report.AppendLine("open every file it holds at the meeting's dossier. Greedy: take, take, skip (so it never holds a file). Mixed: honest or greedy per task decision (seeded; take / skip with");
                report.AppendLine("25 / 50 / 75% at the small / normal / big level) and per file (Honest mode: opens at the dossier; Greedy mode, never paid here: opens late in the meeting's discussion).");
                report.AppendLine("Rules: the v5.3 defaults. \"Dosya\": each Client Bonus sharer holds a file on each group-mate who took (+4 suç when opened), each Petty Cash witness one on the sole thief (+3 suç);");
                report.AppendLine("open or shred from the reveal until the crisis calls end, then it expires; the meeting votes on a failed task while one of its offences is unresolved when the vote starts.");
                report.AppendLine(mode.Permutation
                    ? "\"Haftanın gündemi\": a seeded permutation gives one task the small level, one the normal, one the big (Client Bonus pot 4 / 6 / 8, Petty Cash 2 / 3 / 5, skip pay 1 / 2 / 3)."
                    : "Fixed stakes (--stakes fixed): every task at the normal level (Client Bonus pot 6, Petty Cash 3, skip pay 2).");
                report.AppendLine("Otherwise as v5.2: volunteers −1 suç, an unstaffed shift: skippers 0 prim and +2 suç at its reveal and no meeting 2, 0 suç is never a candidate, firings ≤ candidates.");
                report.AppendLine("Declarations (v5.2): every candidate bot declares its prim once: Honest the truth, Greedy 35–45% less (at least 2 less, never below 3), Mixed one or the other (seeded coin).");
                report.AppendLine("No effect on prim, suç or votes. Story: the results' awards (at most 4, one per player where possible) and headlines (at most 5), LineStory.");
                report.AppendLine("Files line: what became of the files (opened: before the meeting / in the meeting / during the shift / in the crisis calls). Stakes line: greedy choices (take, skip) per level.");
                report.AppendLine("Week types line: levels of Client Bonus-Petty Cash-Weekend Shift (S small, N normal, B big): matches, prim per seat before the desk reveal, suç per seat.");
            }
            else
            {
                report.AppendLine("Bots never call, text or record (spec §13), so nobody pays for silence here. Honest: share, leave, volunteer by a simple count (the cleanest Needed+1 by suç volunteer),");
                report.AppendLine("punish whenever eligible (whistle as a witness, investigate a Client Bonus partner who took). Greedy: take, take, skip; punish only when paid (never here).");
                report.AppendLine("Mixed: honest or greedy per task decision (50/50, seeded) and per punishment (Honest or Greedy mode, seeded coin).");
                report.AppendLine("Rules: the v5.2 defaults (Client Bonus 3/3, free sharer-only investigation +4 suç, free whistle +3 suç and the thief keeps the cash, volunteers −1 suç,");
                report.AppendLine("an unstaffed shift: skippers 0 prim and +2 suç at its reveal and no meeting 2, 0 suç is never a candidate, firings ≤ candidates).");
                report.AppendLine("Declarations (v5.2): every candidate bot declares its prim once: Honest the truth, Greedy 35–45% less (at least 2 less, never below 3), Mixed one or the other (seeded coin).");
                report.AppendLine("No effect on prim, suç or votes. Story: the results' awards (at most 4, one per player where possible) and headlines (at most 5), LineStory.");
            }
            report.AppendLine(style == LineBotVoteStyle.Richest
                ? "Votes (--vote richest, the game's bots): every policy votes for the rival it estimates to be the richest (LineBotLedger: its own views only, plus a fixed ±0.4 per-bot noise; seeded tie-break)."
                : "Votes (--vote blame, the old rule): every policy votes for a known culprit, else the highest suç (seeded tie-break).");
            report.AppendLine("Mixes: half = each seat Honest or Greedy 50/50 (seeded per match); one-greedy = one seeded Greedy seat, the rest Honest; mixed = every seat Mixed;");
            report.AppendLine("lobby = seat % 3 Honest / Greedy / Mixed like the game's lobby, seats shuffled per match (seeded).");
            report.AppendLine("Vote lines: true balances when each crisis / final vote starts. Fired = richest alive / richest candidate / suç leader; crisis suç to the richest / suç leader;");
            report.AppendLine("estimate = each bot's ledger at the start of each final round (top-hit: its top rival is truly the richest; error: mean |estimate - truth| per rival); final vote top share = votes for the most-voted candidate.");
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
                    for (int i = 0; i < matches; i++) Run(n, m, seed + i, think, style, mode, stats);
                    total += matches;
                    Write(report, n, m, stats, mode);
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
        if (mix == "lobby")
        {
            for (int i = 0; i < n; i++) p[i] = (LineBotPolicy)(i % 3);
            for (int i = n - 1; i > 0; i--) { int j = r.Next(i + 1); var t = p[i]; p[i] = p[j]; p[j] = t; }
            return p;
        }
        for (int i = 0; i < n; i++) p[i] = r.Next(2) == 0 ? LineBotPolicy.Honest : LineBotPolicy.Greedy;
        return p;
    }

    /// <summary>The true balances, suç and status at the start of a vote (for the vote lines only; read-only views).</summary>
    private sealed class Snapshot
    {
        internal int[] Prim, Blame;
        internal bool[] Alive, Option;
    }

    private static Snapshot Snap(ILineEngine engine, int n, double now, LineView v0)
    {
        var s = new Snapshot { Prim = new int[n], Blame = new int[n], Alive = new bool[n], Option = new bool[n] };
        for (int j = 0; j < n; j++) s.Prim[j] = engine.BuildView(j, now).Mine.Prim;
        foreach (var p in v0.Players) { s.Blame[p.Id] = p.Blame; s.Alive[p.Id] = !p.Fired; }
        if (v0.Phase == LinePhase.FinalVote) foreach (int o in v0.Final.Options) s.Option[o] = true;
        return s;
    }

    private static int Max(Snapshot s, int[] values, bool optionsOnly)
    {
        int best = int.MinValue;
        for (int j = 0; j < values.Length; j++) if (optionsOnly ? s.Option[j] : s.Alive[j]) best = Math.Max(best, values[j]);
        return best;
    }

    /// <summary>--rules / --stakes: the v5.3 defaults, the v5.2 one-shot rules or every v5.0 number; the stakes scheme.</summary>
    private sealed class Mode
    {
        internal bool Old, V52, Permutation;
        /// <summary>The one-shot investigation / whistle instead of the files (LineConfig.Legacy).</summary>
        internal bool Legacy { get { return Old || V52; } }
    }

    /// <summary>Every v5.0 number and switch (--rules old): also meeting 2 after an unstaffed shift and no declarations.</summary>
    private static void OldRules(LineConfig c)
    {
        c.ShiftMeeting = true; c.DeclarationsEnabled = false;
        c.BonusPairShare = 2; c.BonusTrioNone = 2;
        c.Legacy = new LineLegacyRules
        {
            CashWhistleReward = 2, CashWhistledKeeps = false, ShiftFailVote = true,
            InvestigationSharerOnly = false, InvestigationCost = 4, InvestigationRefund = 3,
        };
        c.ShiftFailSkipPay = c.ShiftSkipPay; c.ShiftFailSkipBlame = 0; c.ShiftVolunteerClean = 0;
        c.InvestigationGuiltyBlame = 3;
        c.CandidateMinBlame = 0;
    }

    private static void Run(int n, string mix, int seed, double think, LineBotVoteStyle style, Mode mode, Stats stats)
    {
        bool old = mode.Old;
        var setup = new LineSetup { WeekId = 1, MatchId = "sim-" + n + "-" + mix + "-" + seed, Seed = seed };
        for (int i = 0; i < n; i++) setup.Players.Add(new LinePlayerInfo { Id = i, Name = "Bot " + i, IsBot = true });
        var config = LineConfig.CreateDefault();
        config.StakesScheme = mode.Permutation ? LineStakes.Permutation : LineStakes.Fixed;
        if (mode.V52) config.Legacy = new LineLegacyRules();
        if (old) OldRules(config);
        var engine = LineEngineFactory.Create(setup, config);
        var policy = Assign(n, mix, seed);
        var bots = new LineBot[n];
        for (int i = 0; i < n; i++)
            bots[i] = new LineBot(i, seed * 31 + i * 97, policy[i])
            { ThinkSeconds = think, VoteStyle = style, PunishStyle = old ? LineBotPunishStyle.Legacy : LineBotPunishStyle.Deals };
        double now = 0;
        int steps = 0;
        double phaseKey = double.NaN;
        Snapshot crisis = null;
        var rounds = new Dictionary<int, Snapshot>();
        var finalVotes = new Dictionary<double, Dictionary<int, int>>();
        while (!engine.IsFinished)
        {
            if (++steps > 5000) throw new InvalidOperationException("Stall: " + n + "p " + mix + " seed " + seed + " at " + engine.Phase);
            engine.Tick(now);
            var v0 = engine.BuildView(0, now);
            if (v0.PhaseStartTime != phaseKey)
            {
                phaseKey = v0.PhaseStartTime;
                if (v0.Phase == LinePhase.CrisisVote) crisis = Snap(engine, n, now, v0);
                if (v0.Phase == LinePhase.CrisisVerdict && crisis != null && v0.Final != null && v0.Final.Crisis.Count > 0)
                {
                    int top = v0.Final.Crisis.Max(x => x.Amount), richest = Max(crisis, crisis.Prim, false), blameTop = Max(crisis, crisis.Blame, false);
                    var got = v0.Final.Crisis.Where(x => x.Amount == top).Select(x => x.Player).ToList();
                    stats.CrisisVotes++;
                    if (got.Any(x => crisis.Prim[x] == richest)) stats.CrisisRichest++;
                    if (got.Any(x => crisis.Blame[x] == blameTop)) stats.CrisisBlameLeader++;
                    crisis = null;
                }
                if (v0.Phase == LinePhase.FinalVote && !v0.Final.Revote && !rounds.ContainsKey(v0.Final.Round))
                {
                    var snap = rounds[v0.Final.Round] = Snap(engine, n, now, v0);
                    for (int i = 0; i < n; i++)
                    {
                        if (!snap.Alive[i]) continue;
                        var est = bots[i].EstimatedPrim(engine.BuildView(i, now));
                        int top = -1, truth = int.MinValue;
                        double best = double.NegativeInfinity;
                        for (int j = 0; j < n; j++)
                        {
                            if (j == i || !snap.Alive[j]) continue;
                            if (est[j] > best) { best = est[j]; top = j; }
                            truth = Math.Max(truth, snap.Prim[j]);
                            stats.EstimateAbsError += Math.Abs(est[j] - snap.Prim[j]);
                            stats.EstimateErrors++;
                        }
                        if (top < 0) continue;
                        stats.EstimateChecks++;
                        if (snap.Prim[top] == truth) stats.EstimateTopHits++;
                    }
                }
            }
            for (int round = 0; round < 8; round++)
            {
                bool acted = false;
                for (int i = 0; i < n; i++)
                {
                    var view = engine.BuildView(i, now);
                    var action = bots[i].Next(view, config, now);
                    if (action == null) continue;
                    if (action.Verb == LineVerb.Vote && view.Phase == LinePhase.FinalVote && action.Target >= 0)
                    {
                        if (!finalVotes.TryGetValue(view.PhaseStartTime, out var tally)) finalVotes[view.PhaseStartTime] = tally = new Dictionary<int, int>();
                        tally.TryGetValue(action.Target, out int had);
                        tally[action.Target] = had + 1;
                    }
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
        stats.ShiftVolunteers += r.Choices.Count(c => c.Task == TaskKind.WeekendShift && c.Choice == LineChoice.Volunteer);
        stats.ShiftCleaned -= r.BlameLog.Where(b => b.Reason == BlameReason.ShiftClean).Sum(b => b.Delta);
        stats.ShiftSkipBlame += r.BlameLog.Where(b => b.Reason == BlameReason.ShiftFailSkip).Sum(b => b.Delta);
        stats.Candidates += r.Players.Count(p => p.WasCandidate);
        if (r.Firings.Count < config.FiringsFor(n)) stats.FewerFirings++;
        if (r.Firings.Count == 0) stats.NoFiring++;
        if (r.Choices.Count(c => c.Task == TaskKind.WeekendShift && c.Choice == LineChoice.Volunteer) < LineConfig.ShiftNeeded(n)) stats.ShortShifts++;
        foreach (var d in r.Declarations)
        {
            stats.DeclCandidates++;
            if (d.Amount < 0) continue;
            stats.DeclMade++;
            stats.DeclClaim += d.Amount;
            stats.DeclActual += d.Actual;
            stats.Add(stats.DeclBy, policy[d.Player], v => v + 1);
            if (d.Amount == d.Actual) { stats.DeclTrue++; stats.Add(stats.DeclTrueBy, policy[d.Player], v => v + 1); }
            else if (d.Amount < d.Actual) stats.DeclLow++;
        }
        foreach (var f in r.Firings)
        {
            if (f.Declared < 0) continue;
            stats.FiredDeclared++;
            if (LineStory.Truthful(f)) stats.FiredTrue++;
        }
        var story = LineStory.Build(r, config);
        stats.Awards += story.Awards.Count;
        stats.Headlines += story.Headlines.Count;
        foreach (var a in story.Awards) stats.AwardKinds[(int)a.Kind]++;
        foreach (var h in story.Headlines) stats.HeadlineKinds[(int)h.Kind]++;
        foreach (var f in r.Files)
        {
            stats.Files++;
            if (f.Task == TaskKind.ClientBonus) stats.FilesBonus++; else stats.FilesCash++;
            switch (f.State)
            {
                case LineFileState.Opened:
                    stats.FilesOpened++;
                    if (f.Phase == LinePhase.CrisisCalls) stats.FilesLate++;
                    else if (f.Phase >= LinePhase.MeetingDossier && f.Phase <= LinePhase.MeetingVerdict) stats.FilesMeeting++;
                    else if (r.Choices.Any(c => c.Task == TaskKind.WeekendShift) && f.Time > r.BlameLog.Where(b => b.Reason == BlameReason.Vote).Select(b => b.Time).DefaultIfEmpty(double.MaxValue).Min()) stats.FilesShift++;
                    else stats.FilesEarly++;
                    break;
                case LineFileState.Shredded: stats.FilesShredded++; break;
                case LineFileState.Void: stats.FilesVoid++; break;
                case LineFileState.Expired: stats.FilesExpired++; break;
            }
        }
        if (r.Stakes.Scheme == LineStakes.Permutation && r.Stakes.WeekType >= 0)
        {
            int type = r.Stakes.WeekType;
            stats.WeekMatches[type]++;
            foreach (var p in r.Players)
            {
                int desk = r.PrimLog.Where(x => x.Player == p.Player && (x.Reason == PrimReason.Fired || x.Reason == PrimReason.DeskShare)).Sum(x => x.Delta);
                stats.WeekPrim[type] += p.Prim - desk;
                stats.WeekBlame[type] += p.Blame;
            }
        }
        foreach (var c in r.Choices)
        {
            int task = (int)c.Task - 1, level = r.Stakes.Level(c.Task);
            if (task < 0 || task > 2) continue;
            bool greedy = c.Task == TaskKind.WeekendShift ? c.Choice == LineChoice.Skip : c.Choice == LineChoice.Take;
            stats.LevelSeats[task, level]++;
            if (greedy) stats.LevelTakes[task, level]++;
            if (greedy && r.Stakes.WeekType >= 0) stats.WeekTakes[r.Stakes.WeekType, task]++;
        }
        foreach (var d in r.Incidents)
        {
            if (d.Kind == DossierKind.Investigation) { stats.Investigations++; if (d.N1 == 1) stats.GuiltyInvestigations++; }
            if (d.Kind == DossierKind.Whistle) stats.Whistles++;
            if (d.Kind == DossierKind.CashCaught) stats.Caught++;
        }
        stats.Hash = unchecked((stats.Hash ^ r.ResultHash) * 1099511628211UL);
        foreach (var f in r.Firings)
        {
            if (!rounds.TryGetValue(f.Round, out var snap)) continue;
            stats.FinalFirings++;
            if (snap.Prim[f.Player] == Max(snap, snap.Prim, false)) stats.FiredRichestAlive++;
            if (snap.Prim[f.Player] == Max(snap, snap.Prim, true)) stats.FiredRichestOption++;
            if (snap.Blame[f.Player] == Max(snap, snap.Blame, false)) stats.FiredBlameLeader++;
        }
        foreach (var tally in finalVotes.Values)
        {
            int total = tally.Values.Sum();
            if (total == 0) continue;
            stats.VoteRounds++;
            stats.VoteTopShare += (double)tally.Values.Max() / total;
        }
        var prims = r.Players.Select(x => x.Prim).OrderByDescending(x => x).ToList();
        stats.WinnerPrim += prims[0];
        stats.WinnerGap += prims[0] - prims[1];
        if (r.Winners.Count > 1) stats.Shared++;
    }

    private static double Avg<T>(Dictionary<LineBotPolicy, T> sum, Dictionary<LineBotPolicy, int> count, LineBotPolicy p)
    {
        if (!count.TryGetValue(p, out int c) || c == 0) return double.NaN;
        return Convert.ToDouble(sum.TryGetValue(p, out T s) ? s : default(T), Inv) / c;
    }

    private static void Write(StringBuilder b, int n, string mix, Stats s, Mode mode)
    {
        bool old = mode.Old;
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
        if (mode.Legacy)
            b.AppendLine("Meeting 1 vote held " + F(Pct(s.Meeting1Votes, s.Matches)) + "%; meeting 2 held " + F(Pct(s.Meeting2Held, s.Matches)) + "%; per match: investigations " +
                         F((double)s.Investigations / s.Matches) + " (guilty " + F(Pct(s.GuiltyInvestigations, s.Investigations)) + "%), whistles " + F((double)s.Whistles / s.Matches) +
                         ", caught takers " + F((double)s.Caught / s.Matches));
        else
            b.AppendLine("Meeting 1 vote held " + F(Pct(s.Meeting1Votes, s.Matches)) + "%; meeting 2 held " + F(Pct(s.Meeting2Held, s.Matches)) + "%; per match: files opened " +
                         F((double)s.FilesOpened / s.Matches) + ", caught takers " + F((double)s.Caught / s.Matches));
        b.AppendLine("Shift: volunteers per match " + F((double)s.ShiftVolunteers / s.Matches) + " (needed " + LineConfig.ShiftNeeded(n) + "); suç cleaned per match " +
                     F((double)s.ShiftCleaned / s.Matches) + ", skipper suç per match " + F((double)s.ShiftSkipBlame / s.Matches) + "; candidates per match " +
                     F((double)s.Candidates / s.Matches) + "; fewer firings than planned " + F(Pct(s.FewerFirings, s.Matches)) + "%, no firing " + F(Pct(s.NoFiring, s.Matches)) + "%");
        b.AppendLine("Firings decided by revote " + F(Pct(s.Revotes, s.Firings)) + "%, by tie-break " + F(Pct(s.TieBreaks, s.Firings)) + "%; desk mean " + F((double)s.DeskTotal / Math.Max(1, s.Firings)) +
                     ", removed (remainder) mean " + F((double)s.DeskRemoved / Math.Max(1, s.Firings)) + "; result aggregate hash " + s.Hash.ToString("X16", Inv));
        b.AppendLine("Votes: fired = richest alive " + F(Pct(s.FiredRichestAlive, s.FinalFirings)) + "%, richest candidate " + F(Pct(s.FiredRichestOption, s.FinalFirings)) +
                     "%, suç leader " + F(Pct(s.FiredBlameLeader, s.FinalFirings)) + "%; crisis suç to the richest " + F(Pct(s.CrisisRichest, s.CrisisVotes)) +
                     "%, to the suç leader " + F(Pct(s.CrisisBlameLeader, s.CrisisVotes)) + "%; final vote top share " + F(Pct(s.VoteTopShare, s.VoteRounds)) + "%");
        b.AppendLine("Estimate: top-hit " + F(Pct(s.EstimateTopHits, s.EstimateChecks)) + "%, mean abs error " + F(s.EstimateAbsError / Math.Max(1, s.EstimateErrors)) +
                     " prim; winner prim mean " + F(s.WinnerPrim / s.Matches) + ", gap to 2nd " + F(s.WinnerGap / s.Matches) + ", shared wins " + F(Pct(s.Shared, s.Matches)) + "%");
        if (old) return;   // (--rules old keeps exactly the v5.0 lines)
        b.AppendLine("Declarations: short shift (suç at its reveal) " + F(Pct(s.ShortShifts, s.Matches)) + "%; candidates per match " + F((double)s.DeclCandidates / s.Matches) + ", declared " +
                     F(Pct(s.DeclMade, s.DeclCandidates)) + "%; truthful " + F(Pct(s.DeclTrue, s.DeclMade)) + "% (" +
                     string.Join(", ", Policies.Where(p => s.DeclBy.ContainsKey(p)).Select(p => p + " " + (s.DeclTrueBy.TryGetValue(p, out int t) ? t : 0) + "/" + s.DeclBy[p])) +
                     "), understated " + F(Pct(s.DeclLow, s.DeclMade)) + "%; mean claim " + F((double)s.DeclClaim / Math.Max(1, s.DeclMade)) + " of " +
                     F((double)s.DeclActual / Math.Max(1, s.DeclMade)) + " prim; fired with a true claim (DOĞRU stamp) " + F(Pct(s.FiredTrue, s.FiredDeclared)) + "% of " + s.FiredDeclared);
        b.AppendLine("Story: awards per match " + F((double)s.Awards / s.Matches) + " (" +
                     string.Join(", ", Enumerable.Range(1, LineAwards.Count).Where(k => s.AwardKinds[k] > 0).Select(k => LineAwards.Id((AwardKind)k) + " " + F(Pct(s.AwardKinds[k], s.Matches)) + "%")) +
                     "); headlines per match " + F((double)s.Headlines / s.Matches) + " (" +
                     string.Join(", ", Enumerable.Range(1, s.HeadlineKinds.Length - 1).Where(k => s.HeadlineKinds[k] > 0).Select(k => LineHeadlines.Id((HeadlineKind)k) + " " + F((double)s.HeadlineKinds[k] / s.Matches))) + ")");
        if (mode.Legacy) return;   // (--rules v52 keeps exactly the v5.2 lines)
        b.AppendLine("Files: per match " + F((double)s.Files / s.Matches) + " (bonus " + F((double)s.FilesBonus / s.Matches) + ", cash " + F((double)s.FilesCash / s.Matches) + "); opened " +
                     F(Pct(s.FilesOpened, s.Files)) + "% (before the meeting " + F(Pct(s.FilesEarly, s.Files)) + "%, in the meeting " + F(Pct(s.FilesMeeting, s.Files)) + "%, during the shift " +
                     F(Pct(s.FilesShift, s.Files)) + "%, in the crisis calls " + F(Pct(s.FilesLate, s.Files)) + "%), shredded " + F(Pct(s.FilesShredded, s.Files)) + "%, void " +
                     F(Pct(s.FilesVoid, s.Files)) + "%, expired " + F(Pct(s.FilesExpired, s.Files)) + "%");
        string[] tasks = { "Client Bonus take", "Petty Cash take", "shift skip" };
        b.AppendLine("Stakes: % at small / normal / big: " + string.Join("; ", Enumerable.Range(0, 3).Select(t => tasks[t] + " " +
                     string.Join(" / ", Enumerable.Range(0, 3).Select(l => s.LevelSeats[t, l] == 0 ? "-" : F(Pct(s.LevelTakes[t, l], s.LevelSeats[t, l])))))));
        if (!mode.Permutation) return;
        string levels = "SNB";
        b.AppendLine("Week types: " + string.Join("; ", Enumerable.Range(0, LineStakes.WeekTypes).Select(w =>
                     "" + levels[LineStakes.Level(w, TaskKind.ClientBonus)] + levels[LineStakes.Level(w, TaskKind.PettyCash)] + levels[LineStakes.Level(w, TaskKind.WeekendShift)] + " " + s.WeekMatches[w] +
                     ": prim " + F((double)s.WeekPrim[w] / Math.Max(1, s.WeekMatches[w] * n)) + ", suç " + F((double)s.WeekBlame[w] / Math.Max(1, s.WeekMatches[w] * n)))));
    }
}
