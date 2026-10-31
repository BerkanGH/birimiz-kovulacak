using System;
using System.Globalization;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Command-line options. Normal players never need any of them; they exist for automated multi-process
    /// verification (bots, autostart, logs, screenshots) and for quick local testing.
    /// </summary>
    public sealed class LaunchArgs
    {
        public bool AutoHost;
        public string AutoJoinAddress;
        public ushort Port = 7777;
        public string Name;
        public bool Bot;
        public int FillBots;
        public bool AutoStart;
        public float DurationScale = 1f;
        public int Seed;
        public int Rematches;
        public int QuitAfterMatches;
        public string LogDir;
        public bool Screenshots;
        public bool Mute;
        public float QuitAfterSeconds;
        public float SimulateDropAt;
        public bool DropRejoin = true;
        public float DropDowntime = 5f;
        public bool WeekMode;
        public int ExpectPlayers = 4;
        public string ManagerProfile;
        public string VoiceTest;
        /// <summary>Playtest facilitator's friend-group code (analytics grouping), e.g. G07.</summary>
        public string PlaytestGroup;

        public static LaunchArgs Current { get; private set; } = new LaunchArgs();

        public static LaunchArgs Parse(string[] args)
        {
            var a = new LaunchArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string k = args[i].ToLowerInvariant();
                string next = i + 1 < args.Length ? args[i + 1] : null;
                switch (k)
                {
                    case "-autohost": a.AutoHost = true; break;
                    case "-autojoin": a.AutoJoinAddress = next ?? "127.0.0.1"; i++; break;
                    case "-port": a.Port = ushort.Parse(next, CultureInfo.InvariantCulture); i++; break;
                    case "-name": a.Name = next; i++; break;
                    case "-bot": a.Bot = true; break;
                    case "-fillbots": a.FillBots = int.Parse(next, CultureInfo.InvariantCulture); i++; break;
                    case "-autostart": a.AutoStart = true; break;
                    case "-durationscale": a.DurationScale = float.Parse(next, CultureInfo.InvariantCulture); i++; break;
                    case "-seed": a.Seed = int.Parse(next, CultureInfo.InvariantCulture); i++; break;
                    case "-rematches": a.Rematches = int.Parse(next, CultureInfo.InvariantCulture); i++; break;
                    case "-quitaftermatches": a.QuitAfterMatches = int.Parse(next, CultureInfo.InvariantCulture); i++; break;
                    case "-quitafterseconds": a.QuitAfterSeconds = float.Parse(next, CultureInfo.InvariantCulture); i++; break;
                    case "-logdir": a.LogDir = next; i++; break;
                    case "-screenshots": a.Screenshots = true; break;
                    case "-mute": a.Mute = true; break;
                    case "-simulatedrop": a.SimulateDropAt = float.Parse(next, CultureInfo.InvariantCulture); i++; break;
                    case "-droprejoin": a.DropRejoin = next != "0"; i++; break;
                    case "-dropdowntime": a.DropDowntime = float.Parse(next, CultureInfo.InvariantCulture); i++; break;
                    case "-week": a.WeekMode = true; break;
                    case "-expectplayers": a.ExpectPlayers = int.Parse(next, CultureInfo.InvariantCulture); i++; break;
                    case "-manager": a.ManagerProfile = next; i++; break;
                    case "-playtestgroup": a.PlaytestGroup = next; i++; break;
                    case "-voicetest":
                        if (next != null && !next.StartsWith("-")) { a.VoiceTest = next; i++; }
                        else a.VoiceTest = "sine";
                        break;
                }
            }
            return a;
        }

        public static void Initialize()
        {
            try { Current = Parse(Environment.GetCommandLineArgs()); }
            catch (Exception e) { UnityEngine.Debug.LogWarning("Bad launch args: " + e.Message); Current = new LaunchArgs(); }
        }
    }
}
