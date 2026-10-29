using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using Unity.Netcode;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v3 host session authority: lobby seats (humans + host-side bots), connection approval and reconnect tokens,
    /// the <see cref="ILineEngine"/> week, per-recipient view/event routing, recordings hand-off to voice, and the
    /// session history between weeks. Lives for one hosting session. The lobby UI calls only the public lobby API.
    /// </summary>
    public sealed class HostLineController
    {
        /// <summary>Host of the current session (null on clients and offline).</summary>
        public static HostLineController Current { get; internal set; }

        /// <summary>
        /// Voice hook (set by the voice integration): play one recording pick to everyone now; returns the call id
        /// actually played (a candidate with speech) or -1. When null the first candidate is reported without audio.
        /// </summary>
        public static Func<LineRecordingPick, int> PlayRecording;

        public const int SlotCount = LineProtocol.MaxPlayers;
        const double ViewInterval = 0.1;      // max 10 Hz per client
        const double HeartbeatInterval = 2.0; // resend even without changes
        const double BotInterval = 0.25;

        public sealed class Slot
        {
            public int Index;
            public bool Occupied;
            public bool Virtual;
            public ulong ClientId = ulong.MaxValue;
            public string Name = "";
            public bool Ready;
            public string Token = "";
            public bool Connected;
            public bool IsHost;
            public LineBot Bot;
            public LineView BotView;
        }

        readonly Slot[] _slots = new Slot[SlotCount];
        readonly Dictionary<ulong, int> _clientSlot = new Dictionary<ulong, int>();
        readonly Dictionary<ulong, int> _pendingApprovals = new Dictionary<ulong, int>();
        readonly System.Random _rng = new System.Random();
        readonly HashSet<int> _recordingsTriggered = new HashSet<int>();
        NetSession _session;
        ILineEngine _engine;
        LineSessionHistory _history;
        int _weekId;
        int _weeksCompleted;
        long _lastSentVersion = -1;
        double _lastViewSend, _lastHeartbeat, _nextBotTick;
        double _abandonedAt = -1;
        bool _resultLogged;
        int _recordingsPhaseKey = -1;
        LinePhase _lastPhase = LinePhase.None;

        public ILineEngine Engine => _engine;
        public bool WeekRunning => _engine != null;
        public Slot[] Slots => _slots;
        public WeekLength Length { get; private set; } = WeekLength.Short;
        public bool RecordingsEnabled { get; private set; } = true;
        public int WeeksPlayed => _history != null ? _history.WeeksPlayed : 0;
        public string ConfigJson { get; private set; }

        public double Now => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening ? NetworkManager.Singleton.ServerTime.Time : Time.realtimeSinceStartupAsDouble;

        public HostLineController()
        {
            for (int i = 0; i < SlotCount; i++) _slots[i] = new Slot { Index = i };
            ConfigJson = GameConfig.ToJson(GameConfig.Line);
            if (LaunchArgs.Current.WeekMode) Length = WeekLength.Full;
            Current = this;
        }

        public void Attach(NetSession session)
        {
            _session = session;
            HostLineVoiceBridge.Bind(this);
            foreach (var s in _slots)
                if (s.Occupied && !s.Virtual && s.Connected)
                    _session.SendLineWelcome(s.ClientId, new LineWelcome { Slot = s.Index, ReconnectToken = s.Token, WeekId = _weekId }, ConfigJson);
            if (LaunchArgs.Current.FillBots > 0)
                for (int k = 0; k < LaunchArgs.Current.FillBots; k++) AddBot();
            BroadcastLobby();
        }

        // ================================================================ approval & connections

        public struct Approval
        {
            public bool Approved;
            public string Reason;
            public int Slot;
        }

        public Approval Approve(ulong clientId, byte[] payload, bool isHostClient)
        {
            LineConnectionPayload p;
            try { p = LineCodec.DecodeConnection(payload); }
            catch (Exception) { return new Approval { Reason = "reject.version" }; }
            if (p.ProtocolVersion != LineProtocol.Version || p.BuildId != GameConfig.BuildId)
                return new Approval { Reason = "reject.version" };

            int slot = -1;
            if (WeekRunning)
            {
                // Mid-week: only a disconnected player holding its reconnect token may come back.
                for (int i = 0; i < SlotCount; i++)
                    if (_slots[i].Occupied && !_slots[i].Virtual && !_slots[i].Connected && !string.IsNullOrEmpty(p.ReconnectToken) && _slots[i].Token == p.ReconnectToken)
                        slot = i;
                if (slot < 0) return new Approval { Reason = "reject.in_progress" };
            }
            else
            {
                for (int i = 0; i < SlotCount && slot < 0; i++)
                    if (!_slots[i].Occupied && !_pendingApprovals.ContainsValue(i)) slot = i;
                if (slot < 0) return new Approval { Reason = "reject.full" };
                var s = _slots[slot];
                s.Occupied = true;
                s.Virtual = false;
                s.Name = SanitizeName(p.PlayerName, slot);
                s.Token = Guid.NewGuid().ToString("N");
                s.Ready = isHostClient;
                s.IsHost = isHostClient;
            }
            _pendingApprovals[clientId] = slot;
            return new Approval { Approved = true, Slot = slot };
        }

        string SanitizeName(string name, int slot)
        {
            name = (name ?? "").Trim();
            if (name.Length > 16) name = name.Substring(0, 16);
            if (name.Length == 0) name = Loc.T("player.default." + slot);
            for (int i = 0; i < SlotCount; i++)
                if (i != slot && _slots[i].Occupied && string.Equals(_slots[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    name = name + " " + (slot + 1);
            return name;
        }

        public void OnClientConnected(ulong clientId)
        {
            int slot;
            if (!_pendingApprovals.TryGetValue(clientId, out slot)) return;
            _pendingApprovals.Remove(clientId);
            var s = _slots[slot];
            bool reconnect = WeekRunning;
            s.ClientId = clientId;
            s.Connected = true;
            _clientSlot[clientId] = slot;
            MatchLog.Write("host: client " + clientId + " -> slot " + slot + (reconnect ? " (reconnect)" : ""));
            if (_session != null && _session.IsSpawned)
                _session.SendLineWelcome(clientId, new LineWelcome { Slot = slot, ReconnectToken = s.Token, Reconnected = reconnect, WeekId = _weekId }, ConfigJson);
            if (reconnect && _engine != null)
            {
                _engine.SetConnected(slot, true, Now);
                SendViewTo(slot);
            }
            BroadcastLobby();
        }

        public void OnClientDisconnected(ulong clientId)
        {
            _pendingApprovals.Remove(clientId);
            int slot;
            if (!_clientSlot.TryGetValue(clientId, out slot)) return;
            _clientSlot.Remove(clientId);
            var s = _slots[slot];
            s.Connected = false;
            s.ClientId = ulong.MaxValue;
            MatchLog.Write("host: client " + clientId + " (slot " + slot + ") disconnected");
            if (_engine != null) _engine.SetConnected(slot, false, Now);
            else { s.Occupied = false; s.Ready = false; s.Name = ""; }
            BroadcastLobby();
        }

        /// <summary>Slot of a connected client, or -1.</summary>
        public int SlotOfClient(ulong clientId) { int s; return _clientSlot.TryGetValue(clientId, out s) ? s : -1; }

        // ================================================================ lobby (host UI)

        public void SetReady(ulong clientId, bool ready)
        {
            int slot;
            if (WeekRunning || !_clientSlot.TryGetValue(clientId, out slot)) return;
            _slots[slot].Ready = ready;
            BroadcastLobby();
        }

        /// <summary>Put a bot in the lowest free seat.</summary>
        public void AddBot()
        {
            if (WeekRunning) return;
            for (int i = 0; i < SlotCount; i++)
            {
                if (_slots[i].Occupied || _pendingApprovals.ContainsValue(i)) continue;
                var s = _slots[i];
                s.Occupied = true; s.Virtual = true; s.Connected = true; s.Ready = true; s.Token = "";
                s.Name = Loc.T("player.bot." + i);
                BroadcastLobby();
                return;
            }
        }

        public void RemoveBot(int slot)
        {
            if (WeekRunning || slot < 0 || slot >= SlotCount || !_slots[slot].Virtual) return;
            var s = _slots[slot];
            s.Occupied = false; s.Virtual = false; s.Connected = false; s.Ready = false; s.Name = ""; s.Bot = null; s.BotView = null;
            BroadcastLobby();
        }

        public void SetLength(WeekLength length)
        {
            if (WeekRunning || Length == length) return;
            Length = length;
            BroadcastLobby();
        }

        public void SetRecordings(bool on)
        {
            if (WeekRunning || RecordingsEnabled == on) return;
            RecordingsEnabled = on;
            if (VoiceSystem.Instance != null) VoiceSystem.Instance.RecordingEnabled = on;
            BroadcastLobby();
        }

        /// <summary>False with a localisation key explaining why (e.g. "lobby.need_players").</summary>
        public bool CanStart(out string reasonKey)
        {
            int occupied = 0, ready = 0;
            foreach (var s in _slots)
            {
                if (!s.Occupied) continue;
                occupied++;
                if (s.Ready && s.Connected) ready++;
            }
            if (WeekRunning) { reasonKey = "lobby.running"; return false; }
            int need = LaunchArgs.Current.AutoStart ? Mathf.Clamp(LaunchArgs.Current.ExpectPlayers, LineProtocol.MinPlayers, SlotCount) : LineProtocol.MinPlayers;
            if (occupied < need) { reasonKey = "lobby.need_players"; return false; }
            if (ready < occupied) { reasonKey = "lobby.need_ready"; return false; }
            reasonKey = "lobby.ready_to_start";
            return true;
        }

        public LineLobbyView BuildLobby()
        {
            string reason;
            CanStart(out reason);
            var lv = new LineLobbyView
            {
                BuildId = GameConfig.BuildId, WeekRunning = WeekRunning, WeekId = _weekId, Length = Length,
                RecordingsEnabled = RecordingsEnabled, WeeksPlayed = WeeksPlayed, StatusKey = reason,
            };
            foreach (var s in _slots)
            {
                int pts = _history != null && s.Index < _history.SessionPoints.Count ? _history.SessionPoints[s.Index] : 0;
                lv.Slots.Add(new LineLobbySlot { Slot = s.Index, Occupied = s.Occupied, Connected = s.Connected, Ready = s.Ready, IsHost = s.IsHost, IsBot = s.Virtual, Name = s.Name, SessionPoints = pts });
            }
            return lv;
        }

        public void BroadcastLobby()
        {
            if (_session == null || !_session.IsSpawned) return;
            var lobby = BuildLobby();
            _session.BroadcastLineLobby(lobby);
        }

        // ================================================================ week lifecycle

        public void StartWeek() { StartWeek(false); }

        public bool StartWeek(bool force)
        {
            string reason;
            if (!CanStart(out reason) && !force) { MatchLog.Write("host: cannot start: " + reason); return false; }
            _weekId++;
            var args = LaunchArgs.Current;
            var setup = new LineSetup
            {
                WeekId = _weekId,
                Seed = args.Seed != 0 ? args.Seed + _weekId - 1 : _rng.Next(1, int.MaxValue),
                StartTime = Now + 0.5,
                Length = Length,
                ManagerProfileId = args.ManagerProfile ?? "",
                RecordingsEnabled = RecordingsEnabled,
                History = _history,
            };
            for (int i = 0; i < SlotCount; i++)
                if (_slots[i].Occupied) setup.Players.Add(new LinePlayerInfo { Id = i, Name = _slots[i].Name, ColorIndex = i, IsBot = _slots[i].Virtual });
            var config = GameConfig.CopyLine();
            config.RecordingsEnabled = RecordingsEnabled;
            try { _engine = LineEngineFactory.Create(setup, config); }
            catch (Exception e)
            {
                Debug.LogException(e);
                MatchLog.Write("host: engine failed to start: " + e.Message);
                _engine = null;
                _weekId--;
                return false;
            }
            ConfigJson = GameConfig.ToJson(config);
            for (int i = 0; i < SlotCount; i++)
            {
                var s = _slots[i];
                if (!s.Occupied) continue;
                if (s.Virtual) { s.Bot = new LineBot(i, setup.Seed * 31 + i); s.BotView = null; }
                else if (s.Connected && _session != null)
                    _session.SendLineWelcome(s.ClientId, new LineWelcome { Slot = i, ReconnectToken = s.Token, WeekId = _weekId }, ConfigJson);
                if (!s.Connected) _engine.SetConnected(i, false, Now);
            }
            _lastSentVersion = -1;
            _abandonedAt = -1;
            _resultLogged = false;
            _recordingsTriggered.Clear();
            _recordingsPhaseKey = -1;
            MatchLog.Write("host: week " + _weekId + " started seed=" + setup.Seed + " players=" + setup.Players.Count + " length=" + Length
                + " recordings=" + RecordingsEnabled + " weeksPlayed=" + WeeksPlayed);
            BroadcastLobby();
            SendViewsToAll();
            return true;
        }

        /// <summary>WeekResults: end the week now (the session continues in the lobby).</summary>
        public void ContinueAfterResults()
        {
            if (_engine == null) return;
            if (_engine.Phase == LinePhase.WeekResults && !_engine.IsFinished) _engine.HostContinue(Now);
            FinishWeek();
        }

        /// <summary>Stops the current week and returns everyone to the lobby.</summary>
        public void ReturnToLobby()
        {
            if (_engine != null && _engine.IsFinished) CaptureHistory();
            _engine = null;
            for (int i = 0; i < SlotCount; i++)
            {
                var s = _slots[i];
                if (s.Occupied && !s.Virtual && !s.Connected) { s.Occupied = false; s.Name = ""; }
                if (!s.Virtual && !s.IsHost) s.Ready = false;
                s.Bot = null; s.BotView = null;
            }
            BroadcastLobby();
        }

        void FinishWeek()
        {
            CaptureHistory();
            int completed = _weeksCompleted;
            ReturnToLobby();
            // Automated runs: chain the next week.
            if (LaunchArgs.Current.AutoStart && completed <= LaunchArgs.Current.Rematches) StartWeek(true);
        }

        void CaptureHistory()
        {
            if (_engine == null || !_engine.IsFinished) return;
            if (!_resultLogged) LogResult();
            try { _history = _engine.NextHistory(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        void LogResult()
        {
            _resultLogged = true;
            _weeksCompleted++;
            var r = _engine.Result;
            if (r != null) MatchLog.Write("host: results week=" + r.WeekId + " hash=" + r.ResultHash + " fired=" + r.Fired + " manager=" + r.ManagerProfileId);
        }

        public void Abort()
        {
            // Clients learn about it from the lobby (WeekRunning=false) or the transport going away.
            _engine = null;
            BroadcastLobby();
        }

        // ================================================================ per frame

        public void Update()
        {
            if (_session == null) return;
            double now = Now;
            if (_engine == null)
            {
                // Automated runs (-autostart) begin as soon as the expected seats are ready.
                string reason;
                if (LaunchArgs.Current.AutoStart && now >= _nextBotTick && _weeksCompleted == 0 && _weekId == 0)
                {
                    _nextBotTick = now + 0.5;
                    if (CanStart(out reason)) StartWeek(true);
                }
                return;
            }
            try { _engine.Tick(now); }
            catch (Exception e) { Debug.LogException(e); }
            if (_engine.Phase != _lastPhase)
            {
                // Recordings live for one day only: drop them when the next day (or the results) begins.
                if (_engine.Phase == LinePhase.Morning || _engine.Phase == LinePhase.WeekResults) VoiceSystem.Instance?.ClearRecordings();
                _lastPhase = _engine.Phase;
            }

            if (now >= _nextBotTick) { _nextBotTick = now + BotInterval; TickBots(now); }
            TriggerRecordings(now);

            List<LineEvent> events;
            try { events = _engine.DrainEvents(); }
            catch (Exception e) { Debug.LogException(e); events = null; }
            if (events != null && events.Count > 0) RouteEvents(events);

            bool changed = _engine.StateVersion != _lastSentVersion;
            if ((changed && now - _lastViewSend >= ViewInterval) || now - _lastHeartbeat >= HeartbeatInterval) SendViewsToAll();

            if (_engine.Phase == LinePhase.WeekResults && !_resultLogged && _engine.Result != null) LogResult();
            if (_engine.IsFinished)
            {
                if (_engine.Phase == LinePhase.Abandoned)
                {
                    if (_abandonedAt < 0) _abandonedAt = now;
                    else if (now - _abandonedAt > 10.0) ReturnToLobby();
                }
                else FinishWeek();
            }
        }

        void TickBots(double now)
        {
            foreach (var s in _slots)
            {
                if (!s.Virtual || s.Bot == null || _engine == null) continue;
                try
                {
                    s.BotView = _engine.BuildView(s.Index, now);
                    var a = s.Bot.Next(s.BotView, _engine.Config, now);
                    if (a != null) _engine.Apply(s.Index, a, now);
                }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        /// <summary>At each pick's PlayAt: hand it to voice, report back which call was played.</summary>
        void TriggerRecordings(double now)
        {
            var picks = _engine.RecordingPicks;
            int key = _engine.Day * 100 + (int)_engine.Phase;
            if (key != _recordingsPhaseKey) { _recordingsPhaseKey = key; _recordingsTriggered.Clear(); }
            if (picks == null || picks.Count == 0) return;
            for (int i = 0; i < picks.Count; i++)
            {
                var p = picks[i];
                if (p.Played || _recordingsTriggered.Contains(p.Index) || now < p.PlayAt) continue;
                _recordingsTriggered.Add(p.Index);
                int chosen = LineIds.None;
                try
                {
                    if (PlayRecording != null) chosen = PlayRecording(p);
                    else if (p.CandidateCallIds.Count > 0) chosen = p.CandidateCallIds[0];
                }
                catch (Exception e) { Debug.LogException(e); }
                MatchLog.Write("host: recording pick=" + p.Index + " candidates=" + string.Join(",", p.CandidateCallIds) + " played=" + chosen);
                _engine.ReportRecording(p.Index, chosen, now);
            }
        }

        void RouteEvents(List<LineEvent> events)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                var s = _slots[i];
                if (!s.Connected || s.Virtual) continue;
                List<LineEvent> mine = null;
                foreach (var e in events)
                {
                    if (!e.IsVisibleTo(i)) continue;
                    if (mine == null) mine = new List<LineEvent>();
                    mine.Add(e);
                }
                if (mine != null) _session.SendLineEvents(s.ClientId, mine);
            }
            foreach (var e in events) if (e.Audience == LineAudience.Host) MatchLog.Write("host-event " + e);
        }

        void SendViewsToAll()
        {
            if (_engine == null) return;
            double now = Now;
            for (int i = 0; i < SlotCount; i++) SendViewTo(i);
            _lastSentVersion = _engine.StateVersion;
            _lastViewSend = now;
            _lastHeartbeat = now;
        }

        void SendViewTo(int slot)
        {
            var s = _slots[slot];
            if (!s.Occupied || !s.Connected || s.Virtual || _engine == null) return;
            LineView view;
            try { view = _engine.BuildView(slot, Now); }
            catch (Exception e) { Debug.LogException(e); return; }
            _session.SendLineView(s.ClientId, LineCodec.EncodeView(view));
        }

        public void OnClientRequest(ulong clientId, byte[] payload)
        {
            int slot;
            if (!_clientSlot.TryGetValue(clientId, out slot)) return;
            LineAction req;
            try { req = LineCodec.DecodeAction(payload); }
            catch (Exception) { return; }
            LineActionResult result;
            if (_engine == null) result = LineActionResult.Fail(req.RequestId, LineFailure.StaleWeek);
            else
            {
                try
                {
                    result = _engine.Apply(slot, req, Now);
                    if (result.Accepted) _lastViewSend = 0; // push promptly
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    result = LineActionResult.Fail(req.RequestId, LineFailure.BadParam);
                }
            }
            _session.SendLineResult(clientId, result);
        }

        public void Dispose()
        {
            if (Current == this) Current = null;
            _engine = null;
            HostLineVoiceBridge.Unbind();
        }
    }
}
