using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    /// <summary>A client's request. The host validates everything (phase, week id, target, limits, text) and answers with a <see cref="LineActionResult"/>.</summary>
    [Serializable]
    public sealed class LineAction
    {
        public int WeekId;
        /// <summary>Client-local increasing id; duplicates per player are ignored (retries never repeat an effect).</summary>
        public uint RequestId;
        public LineVerb Verb;
        public int Target = LineIds.None;
        public int A = -1;
        public int B = -1;
        public int C = -1;
        /// <summary>Player-authored text (texts, meeting chat). Never logged.</summary>
        public string Text = "";

        public LineAction() { }

        public LineAction(int weekId, uint requestId, LineVerb verb, int target = LineIds.None, int a = -1, int b = -1, int c = -1, string text = "")
        {
            WeekId = weekId; RequestId = requestId; Verb = verb; Target = target; A = a; B = b; C = c; Text = text ?? "";
        }

        /// <summary>Never includes <see cref="Text"/> (logs must not contain player-authored content).</summary>
        public override string ToString() { return Verb + "(" + Target + "," + A + "," + B + "," + C + (Text.Length > 0 ? ",text" : "") + ")#" + RequestId; }
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
        public int C = -1;

        public bool IsVisibleTo(int player)
        {
            if (Audience == LineAudience.All) return true;
            if (Audience == LineAudience.Player) return AudiencePlayer == player;
            return false;
        }

        public override string ToString() { return Type + "@" + Time.ToString("0.0") + " a=" + Actor + " t=" + Target + " A=" + A + " B=" + B + " C=" + C + " [" + Audience + (Audience == LineAudience.Player ? ":" + AudiencePlayer : "") + "]"; }
    }

    [Serializable]
    public sealed class LinePlayerInfo
    {
        public int Id;
        public string Name = "";
        public int ColorIndex;
        public bool IsBot;
    }

    /// <summary>Everything needed to start one match.</summary>
    [Serializable]
    public sealed class LineSetup
    {
        public int WeekId;
        /// <summary>Host-generated random id, copied into views and results (analytics correlation only).</summary>
        public string MatchId = "";
        public int Seed;
        public double StartTime;
        public List<LinePlayerInfo> Players = new List<LinePlayerInfo>();
        public bool RecordingsEnabled = true;
        /// <summary>Lobby option "Yaramazlıklar açık" (docs/V5_YARAMAZLIKLAR.md); false: every Prank fails with NotAvailable.</summary>
        public bool PranksEnabled = true;
        /// <summary>Lobby option "Konuşma süresi" (<see cref="LineTalkTime"/>: 0 short, 1 normal, 2 long). The engine scales
        /// the talking phases of its config by <see cref="LineConfig.TalkFactor"/> when the match starts.</summary>
        public int TalkTime = LineTalkTime.Normal;
        /// <summary>Carried across matches of one session; null for the first match.</summary>
        public LineSessionHistory History;

        // ---- test hooks (null/empty = default)
        /// <summary>Task kinds for the three task slots (default ClientBonus, PettyCash, WeekendShift). Slots 0 and 1 must be pair tasks, slot 2 an everyone task.</summary>
        public List<TaskKind> ForceTasks;
    }

    [Serializable]
    public sealed class LineSessionHistory
    {
        public int WeeksPlayed;
        /// <summary>Matches won (shared wins count) per player id.</summary>
        public List<int> Wins = new List<int>();
        /// <summary>Times fired per player id.</summary>
        public List<int> TimesFired = new List<int>();
    }

    /// <summary>
    /// The deterministic v5 rules engine (pure C#, no UnityEngine). Host-only. The same seed, setup and sequence of
    /// (time, player, action) calls always produce the same views, events, messages and result.
    /// </summary>
    public interface ILineEngine
    {
        /// <summary>The effective config of this match: the one given, with the talking phases already scaled by
        /// <see cref="LineSetup.TalkTime"/> (never feed it back into a new engine together with that option).</summary>
        LineConfig Config { get; }
        LineSetup Setup { get; }
        LinePhase Phase { get; }
        double PhaseEndTime { get; }
        /// <summary>Meeting index (0 = tasks 1-2 and meeting 1, 1 = task 3 and meeting 2, 2 = crisis and final).</summary>
        int Day { get; }
        long StateVersion { get; }
        bool IsFinished { get; }
        /// <summary>Set once Results is reached (null when abandoned).</summary>
        LineWeekResult Result { get; }

        /// <summary>Advance time (phase changes, ring timeouts, defender changes, abandon rule).</summary>
        void Tick(double now);
        LineActionResult Apply(int player, LineAction action, double now);
        /// <summary>Host-side presence; absent players keep their seat, their choices take defaults.</summary>
        void SetConnected(int player, bool connected, double now);
        LineView BuildView(int player, double now);
        /// <summary>All events since the last drain, in order (the adapter routes by audience).</summary>
        List<LineEvent> DrainEvents();
        /// <summary>History to pass into the next match's setup (valid when finished).</summary>
        LineSessionHistory NextHistory();

        // ---- messages (free text travels outside the view)
        /// <summary>Detached copies of the player's messages with Seq &gt; <paramref name="afterSeq"/>, oldest first.</summary>
        List<LineMessage> MessagesFor(int player, long afterSeq);

        // ---- voice integration (host)
        /// <summary>Connected call id of a player, or -1.</summary>
        int ConnectedCallId(int player);
        /// <summary>Connected call partner, or -1.</summary>
        int CallPartner(int player);
        /// <summary>v5 has no wiretap: always 0.</summary>
        int TappersOf(int speaker, int[] buffer);
        /// <summary>False while somebody else holds the floor (final defense).</summary>
        bool MaySpeak(int player);
        /// <summary>Leaked recordings of the current meeting, in play order.</summary>
        IReadOnlyList<LineRecordingPick> RecordingPicks { get; }
        /// <summary>The voice system reports that pick <paramref name="pickIndex"/> played (<paramref name="chosenCallId"/>) or had no speech (-1).</summary>
        void ReportRecording(int pickIndex, int chosenCallId, double now);
        /// <summary>Results: the host ends the match early.</summary>
        void HostContinue(double now);
    }

    // ---- lobby / connection

    [Serializable]
    public sealed class LineLobbyView
    {
        public int ProtocolVersion = LineProtocol.Version;
        public string BuildId = "";
        public string RulesetVersion = LineProtocol.RulesetVersion;
        public List<LineLobbySlot> Slots = new List<LineLobbySlot>();
        public bool WeekRunning;
        public int WeekId;
        public bool RecordingsEnabled = true;
        /// <summary>Lobby option "Yaramazlıklar açık".</summary>
        public bool PranksEnabled = true;
        /// <summary>Lobby option "Konuşma süresi" (<see cref="LineTalkTime"/>).</summary>
        public int TalkTime = LineTalkTime.Normal;
        /// <summary>Matches finished in this session.</summary>
        public int WeeksPlayed;
        /// <summary>Message key shown when the host cannot start, e.g. "lobby.need_players", "lobby.pool_too_small".</summary>
        public string StatusKey = "";
        /// <summary>Host-generated random id of this hosting session (analytics correlation).</summary>
        public string SessionId = "";
        /// <summary>Pseudonymous friend-group id (facilitator code or random per session).</summary>
        public string GroupId = "";
        /// <summary>Declared experiment assignments, "id=variant" entries (analytics tagging).</summary>
        public List<string> Experiments = new List<string>();
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
        /// <summary>Matches won in this session.</summary>
        public int Wins;
        /// <summary>The player pressed "Play again" on the last results screen.</summary>
        public bool WantsRematch;
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
        /// <summary>Analytics consent of this participant: 0 unknown, 1 denied, 2 local logs, 3 local + cloud.</summary>
        public int TelemetryConsent;
        /// <summary>Random pseudonymous analytics id (empty unless consent ≥ 2). Never a name, account or device id.</summary>
        public string AnonymousPlayerId = "";
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
