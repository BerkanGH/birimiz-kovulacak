using System;
using BirimizKovulacak.Core;

namespace BirimizKovulacak.Telemetry
{
    /// <summary>Mirror of ConnectionManager.State (same order; the Unity glue casts).</summary>
    public enum ClientConnState { Offline = 0, Hosting = 1, Connecting = 2, Connected = 3, Reconnecting = 4, Failed = 5 }

    /// <summary>
    /// Connection-experience telemetry for the local client (pure C#). The ConnectionManager reconnect loop flips
    /// states (a reject.* Failed, then Connecting again, finally net.reconnect_failed), so a Failed state is held for
    /// <c>failedHoldSeconds</c> and only the terminal outcome is reported, using the most specific reason seen.
    /// session_interrupted means the session did not survive; survivable drops show up as reconnect_attempted/succeeded.
    /// </summary>
    public sealed class ClientConnectionTelemetry
    {
        readonly IGameTelemetry _t;
        readonly Func<double> _clock;
        readonly double _hold;

        string _joinKind = "manual";
        double _attemptStart;
        bool _chain;            // reconnect chain active
        double _chainStart;
        int _attempts;
        bool _joined;           // connected at least once in this session
        double _joinedAt;
        bool _hosting;
        string _pendingError;   // tentative Failed outcome
        double _pendingAt;
        string _rejectReason;   // most specific reject.* seen in this chain
        LinePhase _phase = LinePhase.None;
        bool _midMatch;
        ClientConnState _state = ClientConnState.Offline;

        public ClientConnectionTelemetry(IGameTelemetry telemetry, Func<double> clock, double failedHoldSeconds = 6.0)
        {
            _t = telemetry;
            _clock = clock;
            _hold = failedHoldSeconds;
        }

        public bool HasPendingFailure { get { return _pendingError != null; } }

        /// <summary>kind: manual, rejoin, auto (applies to the next attempt).</summary>
        public void NoteJoinKind(string kind)
        {
            if (kind == "manual" || kind == "rejoin" || kind == "auto") _joinKind = kind;
        }

        public void OnState(ClientConnState state, string errorKey, LinePhase phase, bool midMatch)
        {
            try { Handle(state, errorKey ?? "", phase, midMatch); }
            catch (Exception) { }
        }

        void Handle(ClientConnState state, string error, LinePhase phase, bool midMatch)
        {
            double now = _clock();
            // the reconnect loop resets the client session (no view), so remember the last phase / mid-match flag seen
            if (phase != LinePhase.None) _phase = phase;
            if (midMatch) _midMatch = true;
            var prev = _state;
            _state = state;
            switch (state)
            {
                case ClientConnState.Connecting:
                    if (_pendingError != null && _chain) { _pendingError = null; _attempts++; } // the loop retries: not terminal
                    else if (_pendingError != null) Finalize(now);
                    if (!_chain)
                    {
                        _attemptStart = now;
                        _rejectReason = null;
                        _phase = LinePhase.None;
                        _midMatch = false;
                        _t.Begin("lobby_join_attempted").Enum("kind", _joinKind).Emit();
                    }
                    break;
                case ClientConnState.Reconnecting:
                    _pendingError = null;
                    if (!_chain)
                    {
                        _chain = true;
                        _chainStart = now;
                        _attempts = 1;
                        _rejectReason = null;
                        _t.Begin("reconnect_attempted").Int("attempt", 1).Float("seconds", 0).Emit();
                    }
                    break;
                case ClientConnState.Connected:
                    _pendingError = null;
                    if (_chain)
                    {
                        double secs = Math.Max(0, now - _chainStart);
                        _t.Begin("reconnect_succeeded").Int("attempt", Math.Max(1, _attempts)).Float("seconds", secs).Emit();
                        _t.Begin("lobby_joined").Bool("reconnect", true).Float("wait_seconds", secs).Emit();
                        if (!_joined) _joinedAt = now;
                    }
                    else
                    {
                        _t.Begin("lobby_joined").Bool("reconnect", false).Float("wait_seconds", Math.Max(0, now - _attemptStart)).Emit();
                        _joinedAt = now;
                    }
                    _chain = false;
                    _joined = true;
                    _hosting = false;
                    _rejectReason = null;
                    break;
                case ClientConnState.Hosting:
                    _joined = true; _hosting = true; _joinedAt = now; _chain = false; _pendingError = null;
                    break;
                case ClientConnState.Failed:
                    if (error == "net.host_failed") return; // StartHost failed: not a join outcome
                    if (_hosting && !_joined) return;
                    if (_hosting) { _hosting = false; _joined = false; return; } // host-side failures are not join outcomes
                    if (error.StartsWith("reject.") ) _rejectReason = error;
                    if (_pendingError == null || error.StartsWith("reject.")) _pendingError = error;
                    _pendingAt = now;
                    if (error == "net.reconnect_failed" || (!_chain && prev != ClientConnState.Reconnecting && prev != ClientConnState.Failed)) Finalize(now);
                    break;
                case ClientConnState.Offline:
                    if (_pendingError != null) Finalize(now);
                    if (_joined) EmitLeft("user", now);
                    _phase = LinePhase.None; _midMatch = false;
                    _joined = false; _hosting = false; _chain = false; _pendingError = null; _rejectReason = null;
                    break;
            }
        }

        /// <summary>Call regularly (once per frame is fine): reports a held Failed outcome when no retry followed.</summary>
        public void Tick()
        {
            if (_pendingError == null) return;
            try
            {
                double now = _clock();
                if (now - _pendingAt >= _hold) Finalize(now);
            }
            catch (Exception) { }
        }

        void Finalize(double now)
        {
            string error = _pendingError ?? "net.connect_failed";
            _pendingError = null;
            string reason = _rejectReason ?? error;
            bool wasJoined = _joined;
            bool graceful = _phase == LinePhase.WeekResults || _phase == LinePhase.Abandoned;
            if (_chain)
            {
                _t.Begin("reconnect_failed").Int("attempt", Math.Max(1, _attempts)).Float("seconds", Math.Max(0, now - _chainStart)).Emit();
                EmitJoinFailed(reason);
                EmitInterrupted(InterruptionReason(reason, true));
            }
            else if (wasJoined)
            {
                string ir = InterruptionReason(reason, false);
                EmitInterrupted(ir);
                if (ir == "host_lost" || ir == "transport_failed")
                    _t.Begin("host_departed").Enum("phase", PhaseName(_phase)).Bool("graceful", graceful).Emit();
                EmitLeft(ir == "host_lost" && graceful ? "host_closed" : "error", now);
            }
            else EmitJoinFailed(reason);
            _chain = false;
            _joined = false;
            _rejectReason = null;
        }

        static string PhaseName(LinePhase p) { return p == LinePhase.None ? "lobby" : TelemetryNames.Of(p); }

        void EmitInterrupted(string reason)
        {
            _t.Begin("session_interrupted").Enum("reason", reason).Enum("phase", PhaseName(_phase)).Bool("mid_match", _midMatch).Emit();
        }

        void EmitJoinFailed(string reason)
        {
            string r = reason == "reject.version" || reason == "reject.full" || reason == "reject.in_progress" || reason == "net.connect_failed" || reason == "net.host_lost" || reason == "net.reconnect_failed"
                ? reason : "net.connect_failed";
            _t.Begin("lobby_join_failed").Enum("reason", r).Emit();
        }

        void EmitLeft(string reason, double now)
        {
            _t.Begin("lobby_left").Enum("reason", reason).Float("seconds_in_lobby", Math.Max(0, now - _joinedAt)).Emit();
        }

        /// <summary>Interruption category for a terminal failure reason.</summary>
        public static string InterruptionReason(string reason, bool afterReconnect)
        {
            reason = reason ?? "";
            if (reason == "reject.version" || reason == "net.incompatible") return "incompatible";
            if (reason.IndexOf("kick", StringComparison.Ordinal) >= 0) return "kicked";
            if (reason == "net.transport_failed") return "transport_failed";
            if (reason == "net.reconnect_failed" || afterReconnect) return "reconnect_failed";
            return "host_lost";
        }
    }
}
