using System.Collections;
using System.Globalization;
using System.Reflection;
using BirimizKovulacak.Core;

namespace BalanceSim;

public sealed class Options
{
    public string Experiment = "all";
    public int Matches = 1000, ProfileMatches = 500, Weeks = 300, Seed = 220000;
    public int Threads = Math.Min(8, Environment.ProcessorCount);
    public int[] Players = [3, 4, 5, 6];
    public string Output = "baseline";
    public List<string> Overrides = [];
    public bool Help;

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string Value() => ++i < args.Length ? args[i] : throw new ArgumentException("Missing value for " + args[i - 1]);
            switch (args[i])
            {
                case "--help": case "-h": o.Help = true; break;
                case "--experiment": o.Experiment = Value(); break;
                case "--matches": o.Matches = int.Parse(Value()); break;
                case "--profile-matches": o.ProfileMatches = int.Parse(Value()); break;
                case "--weeks": o.Weeks = int.Parse(Value()); break;
                case "--seed": o.Seed = int.Parse(Value()); break;
                case "--threads": o.Threads = int.Parse(Value()); break;
                case "--players": o.Players = Value().Split(',').Select(int.Parse).Distinct().Order().ToArray(); break;
                case "--out": o.Output = Value(); break;
                case "--set": o.Overrides.Add(Value()); break;
                default: throw new ArgumentException("Unknown option: " + args[i]);
            }
        }
        if (!new[] { "all", "quick", "profiles", "week" }.Contains(o.Experiment)) throw new ArgumentException("experiment must be all, quick, profiles, or week");
        if (o.Threads < 1 || o.Matches < 1 || o.ProfileMatches < 1 || o.Weeks < 1 || o.Players.Length == 0 || o.Players.Any(p => p < 3 || p > 6)) throw new ArgumentException("Positive counts/threads and 3–6 players required");
        if (o.Output.Length == 0 || o.Output.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')) throw new ArgumentException("out must be a simple filename stem (letters, digits, - or _)");
        _ = o.CreateConfig(4); // Fail before running any matches.
        return o;
    }

    public MatchConfig CreateConfig(int players)
    {
        var config = MatchConfig.CreateDefault();
        foreach (string assignment in Overrides) Apply(config, assignment);
        // Host order: fresh base config, optional day modifier (omitted), then headcount.
        // MatchEngine applies the manager profile and week features after this.
        config.ApplyHeadcount(players);
        return config;
    }

    static void Apply(MatchConfig config, string assignment)
    {
        int equals = assignment.IndexOf('=');
        if (equals <= 0) throw new ArgumentException("Expected --set Key=Value, got " + assignment);
        string path = assignment[..equals], value = assignment[(equals + 1)..];
        if (path == "Durations" || path.StartsWith("Durations.", StringComparison.Ordinal)) throw new ArgumentException("Duration scaling is deliberately unsupported; experiments use full host durations");
        string[] parts = path.Split('.');
        object current = config;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (current is IList list)
            {
                string key = parts[i];
                current = list.Cast<object>().SingleOrDefault(x =>
                    string.Equals(x.GetType().GetField("Id")?.GetValue(x)?.ToString(), key, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x.GetType().GetField("Type")?.GetValue(x)?.ToString(), key, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException("Unknown collection key in " + path);
            }
            else current = Field(current, parts[i]).GetValue(current) ?? throw new ArgumentException("Null config object: " + path);
        }
        FieldInfo field = Field(current, parts[^1]);
        if (field.FieldType != typeof(float) && field.FieldType != typeof(int) && field.FieldType != typeof(bool)) throw new ArgumentException("Only numeric/bool config leaves may be overridden: " + path);
        object converted = Convert.ChangeType(value, field.FieldType, CultureInfo.InvariantCulture);
        if (converted is float f && !float.IsFinite(f)) throw new ArgumentException("Config values must be finite");
        field.SetValue(current, converted);
    }

    static FieldInfo Field(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public)
        ?? throw new ArgumentException("Unknown config field: " + obj.GetType().Name + "." + name);

    public const string Usage = """
    BalanceSim — real core, 0.1 s ticks, full durations, independent seeded matches.
    dotnet run -c Release --project Tools/BalanceSim -- [options]
      --experiment all|quick|profiles|week  (default all)
      --matches 1000 --profile-matches 500 --weeks 300
      --players 3,4,5,6 --seed 220000 --threads 8 --out baseline
      --set QuotaTarget=30                  repeat for multiple overrides
      --set Objectives.FIXER.Count=1        list entries selected by Id or Type
    Overrides apply before ApplyHeadcount; engine profile overrides still apply.
    Reports and compact JSON are written only to Tools/BalanceSim/results/.
    Week experiments run for the selected headcounts among 4 and 6.
    """;
}
