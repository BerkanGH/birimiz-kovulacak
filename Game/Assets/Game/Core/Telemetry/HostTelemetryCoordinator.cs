using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;

namespace BirimizKovulacak.Telemetry
{
    /// <summary>What the host voice system reports about one played (or failed) recording pick.</summary>
    public struct TelemetryRecordingInfo
    {
        public int Candidates;
        /// <summary>0-based rank of the played candidate in the engine's list, -1 when none played.</summary>
        public int ChosenRank;
        public int SpeechMs, SnippetMs, CallLengthMs;
        /// <summary>none, no_candidates, no_speech, disabled, voice_unavailable.</summary>
        public string Failure;
    }

    /// <summary>
    /// Host lobby/match lifecycle telemetry (pure C#, testable). HostTelemetryBridge (Unity) forwards its calls here.
    /// Never throws. v5 Phase 1: the v4 gameplay observer was removed; the per-match engine hooks below are kept as no-ops
    /// until the v5 observer exists, so the Unity bridge keeps its call sites.
    /// </summary>
    public sealed class HostTelemetryCoordinator
    {
        readonly IGameTelemetry _t;
        readonly Func<double> _clock;
        readonly Func<ulong> _random;
        readonly double[] _joinedAt = new double[LineProtocol.MaxPlayers];
        readonly bool[] _joined = new bool[LineProtocol.MaxPlayers];
        double _lobbyCreatedAt = -1, _lastJoinAt = -1, _lastReadyAt = -1;
        bool _matchRunning;
        bool _recordings;

        public HostTelemetryCoordinator(IGameTelemetry telemetry, Func<double> clock = null, Func<ulong> random64 = null)
        {
            _t = telemetry ?? throw new ArgumentNullException("telemetry");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _clock = clock ?? (() => sw.Elapsed.TotalSeconds);
            _random = random64 ?? (() => { var b = new byte[8]; new Random().NextBytes(b); return BitConverter.ToUInt64(b, 0); });
        }

        public bool MatchRunning { get { return _matchRunning; } }

        public void LobbyCreated(string sessionId, string groupId, bool recordings)
        {
            try
            {
                _t.HostMode = true;
                _recordings = recordings;
                _lobbyCreatedAt = _clock();
                _lastJoinAt = _lastReadyAt = -1;
                Array.Clear(_joined, 0, _joined.Length);
                string group = TelemetryNames.IsValidGroupId(groupId) ? groupId : (TelemetryNames.IsValidGroupId(_t.Context.GroupId) ? _t.Context.GroupId : "g-" + _random().ToString("x16"));
                _t.Context.AdoptSession(sessionId, group);
                _t.Begin("lobby_created").Bool("recordings_enabled", recordings).Key("lobby|" + _t.Context.SessionId).Emit();
            }
            catch (Exception) { }
        }

        public void PlayerConsent(int slot, int consent, string anonymousId, bool isBot)
        {
            try
            {
                if (slot < 0 || slot >= _joined.Length) return;
                if (!_joined[slot]) { _joined[slot] = true; _joinedAt[slot] = _clock(); _lastJoinAt = _joinedAt[slot]; }
                _t.SetParticipant(slot, consent, anonymousId, isBot);
            }
            catch (Exception) { }
        }

        public void PlayerReadyChanged(int slot, bool ready)
        {
            try
            {
                if (slot < 0 || slot >= _joined.Length) return;
                double now = _clock();
                if (ready) _lastReadyAt = now;
                var b = _t.Begin("player_ready_changed").Slot("slot", slot).Bool("ready", ready);
                if (_joined[slot]) b.Float("seconds_since_join", Math.Max(0, now - _joinedAt[slot]));
                b.Emit();
            }
            catch (Exception) { }
        }

        public void SeatReleased(int slot)
        {
            try
            {
                if (slot >= 0 && slot < _joined.Length) _joined[slot] = false;
                _t.RemoveParticipant(slot);
            }
            catch (Exception) { }
        }

        public void MatchStartAttempted(int humans, int bots, int ready)
        {
            try { _t.Begin("match_start_attempted").Int("humans", humans).Int("bots", bots).Int("ready", ready).Emit(); } catch (Exception) { }
        }

        public void MatchStartFailed(string reasonKey)
        {
            try
            {
                string r = reasonKey == "lobby.need_players" || reasonKey == "lobby.need_ready" || reasonKey == "lobby.pool_too_small" ? reasonKey : "engine_error";
                _t.Begin("match_start_failed").Enum("reason", r).Emit();
            }
            catch (Exception) { }
        }

        public void MatchStarted(ILineEngine engine, LineSetup setup, IReadOnlyList<string> experiments, double now)
        {
            try
            {
                _t.HostMode = true;
                _t.Context.RulesetVersion = LineProtocol.RulesetVersion;
                _matchRunning = true;
                int humans = 0, bots = 0;
                foreach (var p in setup.Players) { if (p.IsBot) bots++; else humans++; }
                foreach (var p in setup.Players)
                {
                    if (p.Id < 0 || p.Id >= LineProtocol.MaxPlayers) continue;
                    var b = _t.Begin("player_slot_map").At(now).Key("slot|" + p.Id).Slot("slot", p.Id).Bool("is_bot", p.IsBot);
                    string id = _t.AnonymousIdOfSlot(p.Id);
                    if (!p.IsBot && id.Length > 0) b.Id("anonymous_player_id", id);
                    b.Emit();
                }
                var ms = _t.Begin("match_started").At(now).Key("start").Int("humans", humans).Int("bots", bots)
                    .Bool("recordings_enabled", setup.RecordingsEnabled);
                if (_lobbyCreatedAt >= 0 && _lastJoinAt >= _lobbyCreatedAt) ms.Float("lobby_fill_seconds", _lastJoinAt - _lobbyCreatedAt);
                if (_lastReadyAt >= 0) ms.Float("ready_seconds", Math.Max(0, _clock() - _lastReadyAt));
                ms.Emit();
            }
            catch (Exception) { }
        }

        // ---- per-match engine hooks: no-ops until the v5 gameplay observer exists.
        public void EngineEvents(ILineEngine engine, List<LineEvent> events, double now) { }
        public void EngineTick(ILineEngine engine, double now) { }
        public void RecordingStarted(ILineEngine engine, LineRecordingPick pick, int chosenCallId, TelemetryRecordingInfo info, double now) { }
        public void RecordingFinished(int callId, double actualSeconds, bool stoppedEarly, double now) { }
        public void PlayerConnection(int slot, bool connected, double now) { }

        public void MatchEnded(ILineEngine engine, string reason, double now)
        {
            try
            {
                _matchRunning = false;
                _t.Context.EndMatch();
            }
            catch (Exception) { }
        }

        public void RematchStarted(string previousMatchId, int returningHumans, int requests, bool auto)
        {
            try
            {
                var b = _t.Begin("rematch_started").Int("returning_humans", returningHumans).Bool("auto", auto).Int("requests", requests);
                if (TelemetryNames.IsValidId(IdKind.MatchId, previousMatchId)) b.Id("previous_match_id", previousMatchId);
                b.Emit();
            }
            catch (Exception) { }
        }

        public void TechnicalFailure(string category, Exception e)
        {
            try
            {
                string cat = category == "engine_exception" || category == "rpc_decode" || category == "voice_exception" ? category : "engine_exception";
                _t.Begin("technical_failure").Enum("category", cat).Id("type", e != null ? e.GetType().Name : "Exception").Emit();
            }
            catch (Exception) { }
        }
    }
}
