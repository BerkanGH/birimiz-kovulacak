using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using BirimizKovulacak.Core;

internal static class Program
{
    private sealed class Totals
    {
        internal readonly long[] RoundDelta = new long[7];
        internal readonly long[] RoundSwing = new long[7];
        internal readonly int[] RoundPlayers = new int[7], FiredSeat = new int[6], FiredTrick = new int[7], AgendaHits = new int[6];
        internal readonly int[] FiredRank = new int[6], HiddenRounds = new int[7], Caught = new int[7];
        internal int Weeks, Recordings, Actions, Busy, MidweekBytes;
        internal int EveningLeaderFired, StrictEveningWeeks, StrictEveningFired, FinalChanged, Tricks, Tips, TrickAllowance, TipAllowance;
        internal double SimulatedSeconds, WallSeconds;
    }

    private static int Main(string[] args)
    {
        try
        {
            int weeks = 1, seed = 1927, selectedPlayers = 0, sessionWeeks = 10;
            double step = 0.25;
            string selectedLength = "both";
            for (int i = 0; i < args.Length; i++)
            {
                string option = args[i];
                if (option == "--help")
                {
                    Console.WriteLine("LineSim [--weeks N] [--players 3|4|5|6|all] [--length short|full|both] [--seed N] [--step seconds] [--session-weeks N]");
                    Console.WriteLine("N weeks are played for each selected player-count/week-length combination.");
                    return 0;
                }
                if (++i >= args.Length) throw new ArgumentException("Missing value for " + option);
                switch (option)
                {
                    case "--weeks": weeks = int.Parse(args[i], CultureInfo.InvariantCulture); break;
                    case "--seed": seed = int.Parse(args[i], CultureInfo.InvariantCulture); break;
                    case "--players": selectedPlayers = args[i] == "all" ? 0 : int.Parse(args[i], CultureInfo.InvariantCulture); break;
                    case "--length": selectedLength = args[i].ToLowerInvariant(); break;
                    case "--step": step = double.Parse(args[i], CultureInfo.InvariantCulture); break;
                    case "--session-weeks": sessionWeeks = int.Parse(args[i], CultureInfo.InvariantCulture); break;
                    default: throw new ArgumentException("Unknown option " + option);
                }
            }
            if (weeks < 1 || sessionWeeks < 1 || double.IsNaN(step) || double.IsInfinity(step) || step <= 0 || step > 5 ||
                (selectedPlayers != 0 && (selectedPlayers < 3 || selectedPlayers > 6)) ||
                (selectedLength != "short" && selectedLength != "full" && selectedLength != "both"))
                throw new ArgumentException("Invalid argument. Use --help for usage.");

            Console.WriteLine("Default rules; " + step.ToString(CultureInfo.InvariantCulture) + " s host step; " + sessionWeeks +
                "-week sessions; seeds start at " + seed + "; bots use only recipient views (no simulated speech or shared hidden knowledge).");
            var summaries = new List<Totals>();
            var headcounts = new List<int>();
            for (int players = 3; players <= 6; players++)
            {
                if (selectedPlayers != 0 && selectedPlayers != players) continue;
                var totals = new Totals();
                foreach (var length in new[] { WeekLength.Short, WeekLength.Full })
                {
                    if (selectedLength != "both" && selectedLength != length.ToString().ToLowerInvariant()) continue;
                    LineSessionHistory history = null;
                    for (int i = 0; i < weeks; i++)
                    {
                        if (i % sessionWeeks == 0) history = null;
                        history = Run(players, length, seed + i, totals, history, step);
                    }
                }
                Print(players, selectedLength, totals);
                summaries.Add(totals); headcounts.Add(players);
            }
            PrintTables(headcounts, summaries);
            BenchmarkTicks();
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static LineSetup Setup(int players, int seed, WeekLength length)
    {
        var setup = new LineSetup { WeekId = seed, Seed = seed, Length = length };
        for (int p = 0; p < players; p++) setup.Players.Add(new LinePlayerInfo { Id = p, Name = "Colleague " + p, ColorIndex = p, IsBot = true });
        return setup;
    }

    private static LineSessionHistory Run(int players, WeekLength length, int seed, Totals totals, LineSessionHistory history, double step)
    {
        var timer = Stopwatch.StartNew();
        var config = LineConfig.CreateDefault();
        var setup = Setup(players, seed, length); setup.History = history;
        var engine = LineEngineFactory.Create(setup, config);
        var bots = new LineBot[players];
        for (int p = 0; p < players; p++) bots[p] = new LineBot(p, seed + p * 313);
        int previousReveal = -1, previousDossier = -1;
        List<int> finalRanking = null, eveningLeaders = null;
        double now;
        for (now = 0; !engine.IsFinished && now < 20000; now += step)
        {
            engine.Tick(now);
            if (engine.IsFinished) break;
            for (int p = 0; p < players; p++)
            {
                var view = engine.BuildView(p, now);
                if (p == 0)
                {
                    if (view.Phase == LinePhase.RoundReveal && previousReveal != view.Day * config.RoundsPerDay + view.RoundIndex)
                    {
                        previousReveal = view.Day * config.RoundsPerDay + view.RoundIndex;
                        var reveal = view.Round.Reveal;
                        for (int d = 0; d < reveal.Deltas.Count; d++)
                        {
                            totals.RoundDelta[(int)reveal.Type] += reveal.Deltas[d].Delta;
                            totals.RoundSwing[(int)reveal.Type] += Math.Abs(reveal.Deltas[d].Delta);
                        }
                        totals.RoundPlayers[(int)reveal.Type] += players;
                        if (reveal.Type == RoundType.FarkliEposta || reveal.Type == RoundType.KayipRapor || reveal.Type == RoundType.OrtakSifre)
                        {
                            totals.HiddenRounds[(int)reveal.Type]++;
                            if (reveal.Type == RoundType.OrtakSifre ? reveal.Solved : reveal.Winner == reveal.Hidden)
                                totals.Caught[(int)reveal.Type]++;
                        }
                    }
                    if (view.Phase == LinePhase.MeetingDossier && previousDossier != view.Day)
                    {
                        previousDossier = view.Day; totals.Recordings += engine.RecordingPicks.Count;
                    }
                    if (players == 6 && view.Day == 1 && view.RoundIndex == 1 && view.Phase == LinePhase.RoundPlay && totals.MidweekBytes == 0)
                        totals.MidweekBytes = LineCodec.EncodeView(view).Length;
                    if (view.Phase == LinePhase.MeetingVerdict && view.Day == view.DayCount - 2 && eveningLeaders == null)
                    {
                        eveningLeaders = new List<int>(); int highest = int.MinValue;
                        foreach (var colleague in view.Players)
                        {
                            if (colleague.Blame > highest) { highest = colleague.Blame; eveningLeaders.Clear(); }
                            if (colleague.Blame == highest) eveningLeaders.Add(colleague.Id);
                        }
                    }
                    if (view.Phase == LinePhase.FinalDefense && finalRanking == null)
                        finalRanking = new List<int>(view.Meeting.Shortlist);
                }
                var action = bots[p].Next(view, config, now);
                if (action == null) continue;
                totals.Actions++;
                var result = engine.Apply(p, action, now);
                if (!result.Accepted)
                {
                    if (action.Verb == LineVerb.Call && result.Failure == LineFailure.Busy) totals.Busy++;
                    else throw new InvalidOperationException("Bot action failed: " + action + " " + result + " at " + now);
                }
                else if (action.Verb == LineVerb.UseTrick) totals.Tricks++;
                else if (action.Verb == LineVerb.TipOff) totals.Tips++;
            }
            if (engine.Phase == LinePhase.MeetingDossier)
            {
                var picks = engine.RecordingPicks;
                for (int i = 0; i < picks.Count; i++)
                    if (!picks[i].Played && now >= picks[i].PlayAt)
                        engine.ReportRecording(i, picks[i].CandidateCallIds.Count > 0 ? picks[i].CandidateCallIds[0] : -1, now);
            }
            engine.DrainEvents();
        }
        if (!engine.IsFinished || engine.Result == null) throw new InvalidOperationException("Week failed to finish.");
        var outcome = engine.Result;
        if (finalRanking == null || !finalRanking.Contains(outcome.Fired)) throw new InvalidOperationException("Missing final shortlist.");
        totals.FiredRank[finalRanking.IndexOf(outcome.Fired)]++;
        if (finalRanking[0] != outcome.Fired) totals.FinalChanged++;
        if (eveningLeaders != null)
        {
            if (eveningLeaders.Contains(outcome.Fired)) totals.EveningLeaderFired++;
            if (eveningLeaders.Count == 1)
            {
                totals.StrictEveningWeeks++;
                if (eveningLeaders[0] == outcome.Fired) totals.StrictEveningFired++;
            }
        }
        totals.TrickAllowance += players * (length == WeekLength.Short ? config.TricksPerWeekShort : config.TricksPerWeekFull);
        totals.TipAllowance += players * config.TipsPerWeek;
        totals.FiredSeat[outcome.Fired]++;
        foreach (var p in outcome.Players)
        {
            if (!p.Survived) totals.FiredTrick[(int)p.Trick]++;
            if (p.AgendaHit) totals.AgendaHits[p.Player]++;
        }
        timer.Stop(); totals.Weeks++; totals.WallSeconds += timer.Elapsed.TotalSeconds; totals.SimulatedSeconds += now;
        return engine.NextHistory();
    }

    private static void Print(int players, string length, Totals totals)
    {
        Console.WriteLine(players + " players, " + length + ": " + totals.Weeks + " weeks, " +
            (totals.WallSeconds * 1000 / totals.Weeks).ToString("F2", CultureInfo.InvariantCulture) + " ms/week; " +
            (totals.SimulatedSeconds / totals.Weeks / 60).ToString("F2", CultureInfo.InvariantCulture) + " simulated minutes/week; " +
            totals.Actions + " actions, " + totals.Busy + " private-busy call results; " + totals.Recordings + " recordings picked.");
        Console.Write("  Applied blame/player/round:");
        for (int r = 1; r <= 6; r++) if (totals.RoundPlayers[r] > 0)
            Console.Write(" " + (RoundType)r + "=" + ((double)totals.RoundDelta[r] / totals.RoundPlayers[r]).ToString("F3", CultureInfo.InvariantCulture));
        Console.WriteLine();
        Console.Write("  Fired by seat:"); for (int p = 0; p < players; p++) Console.Write(" " + p + "=" + totals.FiredSeat[p]); Console.WriteLine();
        Console.Write("  Agenda hits by seat:"); for (int p = 0; p < players; p++) Console.Write(" " + p + "=" + totals.AgendaHits[p]); Console.WriteLine();
        Console.Write("  Fired by trick:"); for (int t = 1; t <= 6; t++) Console.Write(" " + (TrickType)t + "=" + totals.FiredTrick[t]); Console.WriteLine();
        if (totals.MidweekBytes > 0) Console.WriteLine("  Six-player mid-week view: " + totals.MidweekBytes + " bytes.");
        Console.WriteLine("  Strict prior-evening leader fired: " + Percent(totals.StrictEveningFired, totals.StrictEveningWeeks) +
            " (" + totals.StrictEveningFired + "/" + totals.StrictEveningWeeks + " strict-leader weeks).");
    }

    private static string Percent(int numerator, int denominator)
    { return denominator == 0 ? "n/a" : (100.0 * numerator / denominator).ToString("F2", CultureInfo.InvariantCulture) + "%"; }

    private static void PrintTables(List<int> headcounts, List<Totals> summaries)
    {
        Console.WriteLine("\nRank uses the final shortlist's blame, warnings, seeded tie order; lower ranks cannot be fired. Prior-evening leader includes ties.");
        Console.WriteLine("| Players | Weeks | Fired rank 1 | Rank 2 | Rank 3 | Ranks 4-6 | Prior-evening leader fired | Final vote changed outcome | Odd caught | Culprit caught | Password cracked |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|");
        for (int i = 0; i < summaries.Count; i++)
        {
            var t = summaries[i];
            Console.WriteLine("| " + headcounts[i] + " | " + t.Weeks + " | " + Percent(t.FiredRank[0], t.Weeks) + " | " +
                Percent(t.FiredRank[1], t.Weeks) + " | " + Percent(t.FiredRank[2], t.Weeks) + " | " +
                Percent(t.FiredRank[3] + t.FiredRank[4] + t.FiredRank[5], t.Weeks) + " | " +
                Percent(t.EveningLeaderFired, t.Weeks) + " | " + Percent(t.FinalChanged, t.Weeks) + " | " +
                Percent(t.Caught[(int)RoundType.FarkliEposta], t.HiddenRounds[(int)RoundType.FarkliEposta]) + " | " +
                Percent(t.Caught[(int)RoundType.KayipRapor], t.HiddenRounds[(int)RoundType.KayipRapor]) + " | " +
                Percent(t.Caught[(int)RoundType.OrtakSifre], t.HiddenRounds[(int)RoundType.OrtakSifre]) + " |");
        }
        Console.WriteLine("\nMean applied blame delta / mean absolute swing per player per round (floor and favourite included).");
        Console.WriteLine("| Players | Bonus Pool | Odd Email | Lost Report | Complaint Box | Promotion List | Shared Password | Tricks/player/week | Trick allowance used | Tips/player/week | Tip allowance used |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|");
        for (int i = 0; i < summaries.Count; i++)
        {
            var t = summaries[i]; string row = "| " + headcounts[i] + " |";
            for (int r = 1; r <= 6; r++) row += t.RoundPlayers[r] == 0 ? " n/a |" : " " +
                ((double)t.RoundDelta[r] / t.RoundPlayers[r]).ToString("F3", CultureInfo.InvariantCulture) + " / " +
                ((double)t.RoundSwing[r] / t.RoundPlayers[r]).ToString("F3", CultureInfo.InvariantCulture) + " |";
            row += " " + ((double)t.Tricks / (t.Weeks * headcounts[i])).ToString("F3", CultureInfo.InvariantCulture) + " | " +
                Percent(t.Tricks, t.TrickAllowance) + " | " + ((double)t.Tips / (t.Weeks * headcounts[i])).ToString("F3", CultureInfo.InvariantCulture) +
                " | " + Percent(t.Tips, t.TipAllowance) + " |";
            Console.WriteLine(row);
        }
    }

    private static void BenchmarkTicks()
    {
        var engine = LineEngineFactory.Create(Setup(6, 1, WeekLength.Short), LineConfig.CreateDefault());
        for (int i = 0; i < 1000; i++) engine.Tick(0);
        const int count = 1000000;
        var watch = new Stopwatch(); long before = GC.GetAllocatedBytesForCurrentThread();
        watch.Start(); for (int i = 0; i < count; i++) engine.Tick((double)i / count); watch.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine("Six-player idle Tick: " + (count / watch.Elapsed.TotalSeconds).ToString("F0", CultureInfo.InvariantCulture) +
            "/s, " + allocated + " bytes allocated over " + count + " ticks.");
    }
}
