using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BirimizKovulacak.Core;

namespace BalanceSim;

public static class Program
{
    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        try
        {
            Options options = Options.Parse(args);
            if (options.Help) { Console.WriteLine(Options.Usage); return 0; }
            var run = new RunReport { Options = options, Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                LogicalProcessors = Environment.ProcessorCount };
            var timer = Stopwatch.StartNew();
            if (options.Experiment is "all" or "quick")
                foreach (int n in options.Players)
                    run.Groups.Add(RunGroup(options, $"Quick {n}p", n, options.Matches, checked(options.Seed + n * 10000), ""));
            if (options.Experiment is "all" or "profiles")
            {
                var profiles = MatchConfig.CreateDefault().ManagerProfiles;
                for (int p = 0; p < profiles.Count; p++)
                    run.Groups.Add(RunGroup(options, profiles[p].Id, 4, options.ProfileMatches, checked(options.Seed + 100000 + p * 10000), profiles[p].Id));
            }
            if (options.Experiment is "all" or "week")
                foreach (int n in options.Players.Where(n => n is 4 or 6))
                    run.WeekGroups.Add(RunWeeks(options, n, checked(options.Seed + 200000 + n * 10000)));
            run.ElapsedSeconds = timer.Elapsed.TotalSeconds;
            string dir = Path.Combine(FindRoot(), "Tools", "BalanceSim", "results");
            Directory.CreateDirectory(dir);
            var jsonOptions = new JsonSerializerOptions { IncludeFields = true, WriteIndented = false };
            jsonOptions.Converters.Add(new JsonStringEnumConverter());
            File.WriteAllText(Path.Combine(dir, options.Output + ".json"), JsonSerializer.Serialize(run, jsonOptions));
            string markdown = Report.Render(run);
            File.WriteAllText(Path.Combine(dir, options.Output + ".md"), markdown);
            Console.WriteLine(Report.Headlines(run));
            Console.WriteLine($"Finished in {run.ElapsedSeconds:F1}s; results/{options.Output}.md and .json");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    static GroupReport RunGroup(Options options, string label, int players, int count, int seed, string profile)
    {
        Console.WriteLine($"{label}: {count} matches, seeds {seed}..{checked(seed + count - 1)}, {options.Threads} workers");
        var records = new MatchMetrics[count];
        var timer = Stopwatch.StartNew();
        Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = options.Threads }, i =>
            records[i] = Play(options, players, checked(seed + i), profile, GameMode.QuickPlay, 0, null).Metrics);
        Console.WriteLine($"  {timer.Elapsed.TotalSeconds:F1}s; quota {100.0 * records.Count(m => m.Result.CompanySucceeded) / count:F1}%");
        return new GroupReport { Label = label, Players = players, Seed = seed, Matches = records, ElapsedSeconds = timer.Elapsed.TotalSeconds,
            Digest = Digest(records.Select(m => m.Result.ResultHash)) };
    }

    static WeekReport RunWeeks(Options options, int players, int seed)
    {
        Console.WriteLine($"Week {players}p: {options.Weeks} weeks, daily seeds {seed}..{checked(seed + options.Weeks * 5 - 1)}");
        var weeks = new WeekMetrics[options.Weeks];
        var timer = Stopwatch.StartNew();
        Parallel.For(0, weeks.Length, new ParallelOptions { MaxDegreeOfParallelism = options.Threads }, i =>
        {
            SessionHistory? history = null;
            var days = new MatchMetrics[5];
            for (int day = 0; day < 5; day++)
            {
                var played = Play(options, players, checked(seed + i * 5 + day), "", GameMode.Week, day, history);
                days[day] = played.Metrics; history = played.History;
            }
            int wedMax = days[2].Result.Players.Max(p => p.CareerTotal);
            int[] wedLeaders = days[2].Result.Players.Where(p => p.CareerTotal == wedMax).Select(p => p.Player).ToArray();
            MatchResultView friday = days[4].Result;
            weeks[i] = new WeekMetrics { Seed = seed + i * 5, CareerSpread = friday.Players.Max(p => p.CareerTotal) - friday.Players.Min(p => p.CareerTotal),
                FridayLeaderAlreadyLeading = friday.EmployeeOfTheMonth.Any(wedLeaders.Contains),
                AllFridayLeadersAlreadyLeading = friday.EmployeeOfTheMonth.All(wedLeaders.Contains),
                EmployeeTie = friday.EmployeeOfTheMonth.Count > 1, WednesdayTie = wedLeaders.Length > 1,
                DailyHashes = days.Select(d => d.Result.ResultHash).ToArray(), CareerTotals = friday.Players.Select(p => p.CareerTotal).ToArray() };
        });
        Console.WriteLine($"  {timer.Elapsed.TotalSeconds:F1}s; mean career spread {weeks.Average(w => w.CareerSpread):F2}");
        return new WeekReport { Players = players, Seed = seed, Weeks = weeks, ElapsedSeconds = timer.Elapsed.TotalSeconds,
            Digest = Digest(weeks.SelectMany(w => w.DailyHashes)) };
    }

    static (MatchMetrics Metrics, SessionHistory History) Play(Options options, int players, int seed, string profile, GameMode mode, int day, SessionHistory? history)
    {
        var layout = DefaultOffice.Create();
        var setup = new MatchSetup { MatchId = day + 1, Seed = seed, StartTime = 1, ManagerProfileId = profile, Mode = mode, WeekDay = day, History = history };
        for (int p = 0; p < players; p++) setup.Players.Add(new PlayerSlotInfo { Id = p, Name = "Bot " + p, ColorIndex = p });
        var engine = new MatchEngine(options.CreateConfig(players), layout, setup, OpenWorld.Instance);
        var metrics = new MatchMetricsCollector(engine);
        var bots = Enumerable.Range(0, players).Select(p => new SimBot(engine, layout, engine.Config, p, unchecked(seed * 397 ^ (p + 1) * 7919))).ToArray();
        int deadline = (int)Math.Ceiling(engine.Config.Durations.Total() * 10) + 20;
        double now = setup.StartTime;
        for (int tick = 0; tick <= deadline && !engine.IsFinished; tick++)
        {
            now = setup.StartTime + tick * 0.1;
            engine.Tick(now);
            // Rotate request ordering each tick so a fixed lobby slot does not always win contention.
            if (!engine.IsFinished) for (int j = 0; j < players; j++) bots[(j + tick) % players].Tick(now, 0.1f);
            metrics.Observe(engine.DrainEvents());
        }
        if (engine.Phase != MatchPhase.Results || engine.Result == null) throw new InvalidOperationException($"Match {seed} did not finish: {engine.Phase}");
        return (metrics.Finish(now), engine.BuildNextHistory());
    }

    static string Digest(IEnumerable<string> hashes) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", hashes))));
    static string FindRoot()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "Tools", "BalanceSim", "BalanceSim.csproj"))) return dir.FullName;
        throw new DirectoryNotFoundException("Cannot locate Tools/BalanceSim/BalanceSim.csproj");
    }
}

public sealed class RunReport
{
    public Options Options = new();
    public string Runtime = "";
    public int LogicalProcessors;
    public double ElapsedSeconds;
    public List<GroupReport> Groups = [];
    public List<WeekReport> WeekGroups = [];
}
public sealed class GroupReport
{
    public string Label = "", Digest = "";
    public int Players, Seed;
    public double ElapsedSeconds;
    public MatchMetrics[] Matches = [];
}
public sealed class WeekReport
{
    public int Players, Seed;
    public string Digest = "";
    public double ElapsedSeconds;
    public WeekMetrics[] Weeks = [];
}
public sealed class WeekMetrics
{
    public int Seed, CareerSpread;
    public bool FridayLeaderAlreadyLeading, AllFridayLeadersAlreadyLeading, EmployeeTie, WednesdayTie;
    public int[] CareerTotals = [];
    public string[] DailyHashes = [];
}
