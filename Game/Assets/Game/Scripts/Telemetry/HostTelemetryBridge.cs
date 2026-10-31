using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;

namespace BirimizKovulacak.Game
{
    /// <summary>What the host voice system reports about one played (or failed) recording pick.</summary>
    public struct RecordingPlayInfo
    {
        /// <summary>Candidates the engine offered.</summary>
        public int Candidates;
        /// <summary>0-based rank of the played candidate in the engine's list, -1 when none played.</summary>
        public int ChosenRank;
        public int SpeechMs;
        public int SnippetMs;
        public int CallLengthMs;
        /// <summary>"none", "no_candidates", "no_speech", "disabled", "voice_unavailable".</summary>
        public string Failure;
    }

    /// <summary>
    /// Host → telemetry seam (signatures owned by the lead; bodies by the telemetry track). HostLineController and the
    /// voice bridge call these at the documented points. Every method must be cheap, must never throw and must never
    /// change gameplay state. Engine references are read-only: never Apply, Tick or DrainEvents through them.
    /// </summary>
    public static partial class HostTelemetryBridge
    {
        /// <summary>A hosting session began (lobby open).</summary>
        public static void LobbyCreated(string sessionId, string groupId, WeekLength length, bool recordings) { }
        /// <summary>A seat's analytics consent/id became known or changed (approval, update RPC, bot seat).</summary>
        public static void PlayerConsent(int slot, int consent, string anonymousId, bool isBot) { }
        public static void PlayerReadyChanged(int slot, bool ready) { }
        /// <summary>A seat left the lobby (not mid-match): its consent entry can be dropped.</summary>
        public static void SeatReleased(int slot) { }
        public static void MatchStartAttempted(int humans, int bots, int ready) { }
        public static void MatchStartFailed(string reasonKey) { }
        /// <summary>The engine was created; events of the first phase have not been drained yet.</summary>
        public static void MatchStarted(ILineEngine engine, LineSetup setup, IReadOnlyList<string> experiments, double now) { }
        /// <summary>Every drained batch (all audiences, including Host events), before routing to clients.</summary>
        public static void EngineEvents(ILineEngine engine, List<LineEvent> events, double now) { }
        /// <summary>Once per host frame while a match runs (watchdogs, interval bookkeeping).</summary>
        public static void EngineTick(ILineEngine engine, double now) { }
        /// <summary>A recording pick was handed to voice (played or failed).</summary>
        public static void RecordingStarted(ILineEngine engine, LineRecordingPick pick, int chosenCallId, RecordingPlayInfo info, double now) { }
        /// <summary>The host tape ended (or was stopped); actualSeconds measured with the host monotonic clock.</summary>
        public static void RecordingFinished(int callId, double actualSeconds, bool stoppedEarly, double now) { }
        public static void PlayerConnection(int slot, bool connected, double now) { }
        /// <summary>reason: "results", "abandoned", "host_closed". Called once per match.</summary>
        public static void MatchEnded(ILineEngine engine, string reason, double now) { }
        public static void RematchStarted(string previousMatchId, int returningHumans, int requests, bool auto) { }
        /// <summary>category: engine_exception, rpc_decode, voice_exception; only the exception type name may be recorded.</summary>
        public static void TechnicalFailure(string category, Exception e) { }
    }
}
