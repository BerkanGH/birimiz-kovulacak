using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    /// <summary>A client's request. The host validates everything (phase, week id, target, limits) and answers with a <see cref="LineActionResult"/>.</summary>
    [Serializable]
    public sealed class LineAction
    {
        public int WeekId;
        /// <summary>Client-local increasing id; duplicates per player are ignored.</summary>
        public uint RequestId;
        public LineVerb Verb;
        public int Target = LineIds.None;
        public int A = -1;
        public int B = -1;

        public LineAction() { }

        public LineAction(int weekId, uint requestId, LineVerb verb, int target = LineIds.None, int a = -1, int b = -1)
        {
            WeekId = weekId; RequestId = requestId; Verb = verb; Target = target; A = a; B = b;
        }

        public override string ToString() { return Verb + "(" + Target + "," + A + "," + B + ")#" + RequestId; }
    }

    [Serializable]
    public sealed class LineActionResult
    {
        public uint RequestId;
        public bool Accepted;
        public LineFailure Failure;

        public static LineActionResult Ok(uint id) { return new LineActionResult { RequestId = id, Accepted = true }; }
        public static LineActionResult Fail(uint id, LineFailure f) { return new LineActionResult { RequestId = id, Accepted = false, Failure = f }; }
        public override string ToString() { return Accepted ? ("OK#" + RequestId) : ("FAIL#" + RequestId + ":" + Failure); }
    }

    /// <summary>Transient feedback (sounds, toasts, animations). Durable facts live in <see cref="LineView"/>. Field meaning per type: <see cref="LineEventType"/>.</summary>
    [Serializable]
    public sealed class LineEvent
    {
        public LineEventType Type;
        public double Time;
        public LineAudience Audience;
        public int AudiencePlayer = LineIds.None;
        public int Actor = LineIds.None;
        public int Target = LineIds.None;
        public int A = -1;
        public int B = -1;

        public bool IsVisibleTo(int player)
        {
            if (Audience == LineAudience.All) return true;
            if (Audience == LineAudience.Player) return AudiencePlayer == player;
            return false;
        }

        public override string ToString() { return Type + "@" + Time.ToString("0.0") + " a=" + Actor + " t=" + Target + " A=" + A + " B=" + B + " [" + Audience + (Audience == LineAudience.Player ? ":" + AudiencePlayer : "") + "]"; }
    }

    [Serializable]
    public sealed class LinePlayerInfo
    {
        public int Id;
        public string Name = "";
        public int ColorIndex;
        public bool IsBot;
    }

    /// <summary>Everything needed to start one week.</summary>
    [Serializable]
    public sealed class LineSetup
    {
        public int WeekId;
        public int Seed;
        public double StartTime;
        public WeekLength Length;
        public List<LinePlayerInfo> Players = new List<LinePlayerInfo>();
        /// <summary>Empty = the engine picks (seeded, never the previous week's manager).</summary>
        public string ManagerProfileId = "";
        public bool RecordingsEnabled = true;
        /// <summary>Carried across weeks of one session; null for the first week (onboarding rules).</summary>
        public LineSessionHistory History;

        // ---- test hooks (null/empty = random)
        /// <summary>Agenda target per player id.</summary>
        public List<int> ForceAgendas;
        /// <summary>Trick per player id.</summary>
        public List<TrickType> ForceTricks;
        /// <summary>Round types day by day (RoundsPerDay per day, flattened).</summary>
        public List<RoundType> ForceRounds;
        /// <summary>Hidden player per round (odd one / culprit / saboteur), flattened like ForceRounds; -1 = random.</summary>
        public List<int> ForceHidden;
    }

    [Serializable]
    public sealed class LineSessionHistory
    {
        public int WeeksPlayed;
        /// <summary>Points per player id.</summary>
        public List<int> SessionPoints = new List<int>();
        public List<string> PastManagers = new List<string>();
        /// <summary>Round types already introduced in this session (long rule card only the first time).</summary>
        public List<RoundType> SeenRounds = new List<RoundType>();
        /// <summary>Odd Email topic ids already used.</summary>
        public List<int> UsedTopics = new List<int>();
        /// <summary>Times fired per player id.</summary>
        public List<int> TimesFired = new List<int>();
        /// <summary>v3.1: manager picked by last week's fired player ("" = none; Setup.ManagerProfileId still wins).</summary>
        public string NextManager = "";
        /// <summary>v3.1: hidden-role count per player id (odd one / culprit / saboteur) so roles spread across the session.</summary>
        public List<int> HiddenRoles = new List<int>();
    }

    /// <summary>
    /// The deterministic v3 rules engine (pure C#, no UnityEngine). Host-only. The same seed, setup and sequence of
    /// (time, player, action) calls always produce the same views, events and result.
    /// </summary>
    public interface ILineEngine
    {
        LineConfig Config { get; }
        LineSetup Setup { get; }
        LinePhase Phase { get; }
        double PhaseEndTime { get; }
        int Day { get; }
        long StateVersion { get; }
        bool IsFinished { get; }
        /// <summary>Set once WeekResults is reached.</summary>
        LineWeekResult Result { get; }

        /// <summary>Advance time (phase changes, timeouts, ring timeouts, trick expiry).</summary>
        void Tick(double now);
        LineActionResult Apply(int player, LineAction action, double now);
        /// <summary>Host-side presence; absent players keep their seat, their choices take defaults.</summary>
        void SetConnected(int player, bool connected, double now);
        LineView BuildView(int player, double now);
        /// <summary>All events since the last drain, in order (the adapter routes by audience).</summary>
        List<LineEvent> DrainEvents();
        /// <summary>History to pass into the next week's setup (valid when finished).</summary>
        LineSessionHistory NextHistory();

        // ---- voice integration (host)
        /// <summary>Connected call id of a player, or -1.</summary>
        int ConnectedCallId(int player);
        /// <summary>Connected call partner, or -1.</summary>
        int CallPartner(int player);
        /// <summary>Players currently wiretapping <paramref name="speaker"/>; returns the count written into <paramref name="buffer"/>.</summary>
        int TappersOf(int speaker, int[] buffer);
        /// <summary>False while somebody else holds the floor (final defense).</summary>
        bool MaySpeak(int player);
        /// <summary>Recordings scheduled for the current meeting (empty outside MeetingDossier).</summary>
        IReadOnlyList<LineRecordingPick> RecordingPicks { get; }
        /// <summary>The voice system reports which candidate it played (or -1 if none had speech).</summary>
        void ReportRecording(int pickIndex, int chosenCallId, double now);
        /// <summary>WeekResults: the host ends the week early.</summary>
        void HostContinue(double now);
    }

    // ---- lobby / connection (v3)

    [Serializable]
    public sealed class LineLobbyView
    {
        public int ProtocolVersion = LineProtocol.Version;
        public string BuildId = "";
        public List<LineLobbySlot> Slots = new List<LineLobbySlot>();
        public bool WeekRunning;
        public int WeekId;
        public WeekLength Length;
        public bool RecordingsEnabled = true;
        /// <summary>Weeks finished in this session.</summary>
        public int WeeksPlayed;
        /// <summary>Message key shown when the host cannot start, e.g. "lobby.need_players".</summary>
        public string StatusKey = "";
    }

    [Serializable]
    public sealed class LineLobbySlot
    {
        public int Slot;
        public bool Occupied;
        public bool Connected;
        public bool Ready;
        public bool IsHost;
        public bool IsBot;
        public string Name = "";
        public int SessionPoints;
    }

    [Serializable]
    public sealed class LineConnectionPayload
    {
        public int ProtocolVersion = LineProtocol.Version;
        public string BuildId = "";
        public string PlayerName = "";
        public string ReconnectToken = "";
        /// <summary>"tr" or "en" (informational; text is localised on each client).</summary>
        public string Language = "tr";
    }

    [Serializable]
    public sealed class LineWelcome
    {
        public int Slot;
        public string ReconnectToken = "";
        public bool Reconnected;
        public int WeekId;
    }
}
