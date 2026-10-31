using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;

namespace BirimizKovulacak.Telemetry
{
    /// <summary>
    /// Host lobby/match lifecycle telemetry (pure C#, testable). HostTelemetryBridge (Unity) forwards its calls here.
    /// Owns the <see cref="LineTelemetryObserver"/> of the running match. Never throws.
    /// </summary>
    public sealed class HostTelemetryCoordinator
    {
        readonly IGameTelemetry _t;
        readonly Func<double> _clock;
        readonly Func<ulong> _random;
        readonly double[] _joinedAt = new double[LineProtocol.MaxPlayers];
        readonly bool[] _joined = new bool[LineProtocol.MaxPlayers];
        double _lobbyCreatedAt = -1, _lastJoinAt = -1, _lastReadyAt = -1;
        LineTelemetryObserver _observer;
        WeekLength _length;
        bool _recordings;

        public HostTelemetryCoordinator(IGameTelemetry telemetry, Func<double> clock = null, Func<ulong> random64 = null)
        {
            _t = telemetry ?? throw new ArgumentNullException("telemetry");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _clock = clock ?? (() => sw.Elapsed.TotalSeconds);
            _random = random64 ?? (() => { var b = new byte[8]; new Random().NextBytes(b); return BitConverter.ToUInt64(b, 0); });
        }

        public LineTelemetryObserver Observer { get { return _observer; } }
        public bool MatchRunning { get { return _observer != null && !_observer.IsEnded; } }

        public void LobbyCreated(string sessionId, string groupId, WeekLength length, bool recordings)
        {
            try
            {
                _t.HostMode = true;
                _length = length; _recordings = recordings;
                _lobbyCreatedAt = _clock();
                _lastJoinAt = _lastReadyAt = -1;
                Array.Clear(_joined, 0, _joined.Length);
                string group = TelemetryNames.IsValidGroupId(groupId) ? groupId : (TelemetryNames.IsValidGroupId(_t.Context.GroupId) ? _t.Context.GroupId : "g-" + _random().ToString("x16"));
                _t.Context.AdoptSession(sessionId, group);
                _t.Begin("lobby_created").Enum("length", length == WeekLength.Full ? "full" : "short").Bool("recordings_enabled", recordings).Key("lobby|" + _t.Context.SessionId).Emit();
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
                if (_observer != null && !_observer.IsEnded) _observer.MatchEnded("host_closed", now);
                _t.Context.RulesetVersion = LineProtocol.RulesetVersion;
                var obs = new LineTelemetryObserver(_t, engine, experiments);
                obs.Start(now);
                _observer = obs;
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
                string manager = setup.ManagerProfileId;
                if (Array.IndexOf(LineManagerIds.All, manager) < 0)
                {
                    manager = "";
                    try { manager = engine.BuildView(setup.Players[0].Id, now).ManagerProfileId; } catch (Exception) { }
                }
                var ms = _t.Begin("match_started").At(now).Key("start").Int("humans", humans).Int("bots", bots)
                    .Enum("length", setup.Length == WeekLength.Full ? "full" : "short").Bool("recordings_enabled", setup.RecordingsEnabled);
                if (Array.IndexOf(LineManagerIds.All, manager) >= 0) ms.Enum("manager", manager);
                if (_lobbyCreatedAt >= 0 && _lastJoinAt >= _lobbyCreatedAt) ms.Float("lobby_fill_seconds", _lastJoinAt - _lobbyCreatedAt);
                if (_lastReadyAt >= 0) ms.Float("ready_seconds", Math.Max(0, _clock() - _lastReadyAt));
                ms.Emit();
            }
            catch (Exception) { }
        }

        public void EngineEvents(ILineEngine engine, List<LineEvent> events, double now)
        {
            var o = _observer;
            if (o != null) o.OnEvents(events, now);
        }

        public void EngineTick(ILineEngine engine, double now)
        {
            var o = _observer;
            if (o != null) o.Tick(now);
        }

        public void RecordingStarted(ILineEngine engine, LineRecordingPick pick, int chosenCallId, TelemetryRecordingInfo info, double now)
        {
            var o = _observer;
            if (o != null) o.RecordingStarted(pick, chosenCallId, info, now);
        }

        public void RecordingFinished(int callId, double actualSeconds, bool stoppedEarly, double now)
        {
            var o = _observer;
            if (o != null) o.RecordingFinished(callId, actualSeconds, stoppedEarly, now);
        }

        public void PlayerConnection(int slot, bool connected, double now)
        {
            var o = _observer;
            if (o != null && !o.IsEnded) o.PlayerConnection(slot, connected, now);
        }

        public void MatchEnded(ILineEngine engine, string reason, double now)
        {
            try
            {
                var o = _observer;
                if (o != null) o.MatchEnded(reason, now);
                _observer = null;
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
