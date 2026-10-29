using System;
using System.Diagnostics;
using System.Globalization;
using BirimizKovulacak.Core;

internal static class Program
{
    private sealed class Totals
    {
        internal readonly long[] RoundDelta = new long[7];
        internal readonly int[] RoundPlayers = new int[7], FiredSeat = new int[6], FiredTrick = new int[7], AgendaHits = new int[6];
        internal int Weeks, Recordings, Actions, Busy, MidweekBytes;
        internal double SimulatedSeconds, WallSeconds;
    }

    private static int Main(string[] args)
    {
        try
        {
            int weeks = 1, seed = 1927, selectedPlayers = 0;
            string selectedLength = "both";
            for (int i = 0; i < args.Length; i++)
            {
                string option = args[i];
                if (option == "--help")
                {
                    Console.WriteLine("LineSim [--weeks N] [--players 3|4|5|6|all] [--length short|full|both] [--seed N]");
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
                    default: throw new ArgumentException("Unknown option " + option);
                }
            }
            if (weeks < 1 || (selectedPlayers != 0 && (selectedPlayers < 3 || selectedPlayers > 6)) ||
                (selectedLength != "short" && selectedLength != "full" && selectedLength != "both"))
                throw new ArgumentException("Invalid argument. Use --help for usage.");

            Console.WriteLine("All runs use the default rules, a 0.25 s host step, and only recipient views as bot input.");
            for (int players = 3; players <= 6; players++)
            {
                if (selectedPlayers != 0 && selectedPlayers != players) continue;
                foreach (var length in new[] { WeekLength.Short, WeekLength.Full })
                {
                    if (selectedLength != "both" && selectedLength != length.ToString().ToLowerInvariant()) continue;
                    var totals = new Totals();
                    for (int i = 0; i < weeks; i++) Run(players, length, seed + i, totals);
                    Print(players, length, totals);
                }
            }
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

    private static void Run(int players, WeekLength length, int seed, Totals totals)
    {
        var timer = Stopwatch.StartNew();
        var config = LineConfig.CreateDefault();
        var engine = LineEngineFactory.Create(Setup(players, seed, length), config);
        var bots = new LineBot[players];
        for (int p = 0; p < players; p++) bots[p] = new LineBot(p, seed + p * 313);
        int previousReveal = -1, previousDossier = -1;
        double now;
        for (now = 0; !engine.IsFinished && now < 20000; now += 0.25)
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
                        for (int d = 0; d < reveal.Deltas.Count; d++) totals.RoundDelta[(int)reveal.Type] += reveal.Deltas[d].Delta;
                        totals.RoundPlayers[(int)reveal.Type] += players;
                    }
                    if (view.Phase == LinePhase.MeetingDossier && previousDossier != view.Day)
                    {
                        previousDossier = view.Day; totals.Recordings += engine.RecordingPicks.Count;
                    }
                    if (players == 6 && view.Day == 1 && view.RoundIndex == 1 && view.Phase == LinePhase.RoundPlay && totals.MidweekBytes == 0)
                        totals.MidweekBytes = LineCodec.EncodeView(view).Length;
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
        totals.FiredSeat[engine.Result.Fired]++;
        foreach (var p in engine.Result.Players)
        {
            if (!p.Survived) totals.FiredTrick[(int)p.Trick]++;
            if (p.AgendaHit) totals.AgendaHits[p.Player]++;
        }
        timer.Stop(); totals.Weeks++; totals.WallSeconds += timer.Elapsed.TotalSeconds; totals.SimulatedSeconds += now;
    }

    private static void Print(int players, WeekLength length, Totals totals)
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
