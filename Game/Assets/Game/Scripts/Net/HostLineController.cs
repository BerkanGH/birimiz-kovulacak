using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using Unity.Netcode;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 host session authority: lobby seats (humans + host-side bots), connection approval and reconnect tokens,
    /// the <see cref="ILineEngine"/> match, per-recipient view/event/message routing, leaked recordings hand-off to voice,
    /// analytics consent per seat (HostTelemetryBridge) and the session history between matches. Lives for one hosting
    /// session. The lobby UI calls only the public lobby API.
    /// </summary>
    public sealed class HostLineController
    {
        /// <summary>Host of the current session (null on clients and offline).</summary>
        public static HostLineController Current { get; internal set; }

        /// <summary>Voice hook: play one recording pick to everyone now; returns the call id played (or -1) and what happened.</summary>
        public delegate int RecordingPlayer(LineRecordingPick pick, out RecordingPlayInfo info);

        /// <summary>Set by the voice integration (HostLineVoiceBridge). When null the first candidate is reported without audio.</summary>
        public static RecordingPlayer PlayRecording;

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
            /// <summary>v5 bot talk: the bot's phone calls, answers, chat lines and promised prim (docs/V5_BOT_TALK.md).</summary>
            public LineBotTalker Talker;
            /// <summary>Request ids for the bot's and its talker's actions (one increasing sequence per seat).</summary>
            public uint BotRequest;
            /// <summary>"Play again" pressed on the last results screen.</summary>
            public bool WantsRematch;
            /// <summary>Analytics consent of the seat's player (0 unknown, 1 denied, 2 local, 3 cloud) and pseudonymous id.</summary>
            public int TelemetryConsent;
            public string AnonymousId = "";
            /// <summary>Highest message seq pushed to this seat's client.</summary>
            public long MessagesSent;
            public bool MessagesDirty;
        }

        readonly Slot[] _slots = new Slot[SlotCount];
        readonly Dictionary<ulong, int> _clientSlot = new Dictionary<ulong, int>();
        readonly Dictionary<ulong, int> _pendingApprovals = new Dictionary<ulong, int>();
        readonly Dictionary<ulong, LineConnectionPayload> _pendingPayloads = new Dictionary<ulong, LineConnectionPayload>();
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
        bool _matchEndReported = true;
        string _matchId = "";
        string _lastMatchId = "";
        int _matchSeed;
        readonly List<string> _experiments = new List<string>();
        int _recordingsSegment = -1;
        long _picksVersion = -1;
        LinePhase _lastPhase = LinePhase.None;
        IReadOnlyList<LineRecordingPick> _picks;

        /// <summary>Seconds a bot waits (at most, seeded) after a phase starts before acting, so humans see the phase first.</summary>
        public static double BotThinkSeconds => 4.0 * Mathf.Clamp(LaunchArgs.Current.DurationScale, 0.2f, 1f);

        public ILineEngine Engine => _engine;
        public bool WeekRunning => _engine != null;
        public Slot[] Slots => _slots;
        public bool RecordingsEnabled { get; private set; } = true;
        /// <summary>Lobby option "Yaramazlıklar açık" (docs/V5_YARAMAZLIKLAR.md), on by default.</summary>
        public bool PranksEnabled { get; private set; } = true;
        public int WeeksPlayed => _history != null ? _history.WeeksPlayed : 0;
        public string ConfigJson { get; private set; }
        /// <summary>Random id of this hosting session (analytics correlation only).</summary>
        public string SessionId { get; private set; }
        /// <summary>Pseudonymous friend-group id: facilitator code (-playtestgroup / lobby field) or random per session.</summary>
        public string GroupId { get; private set; }
        public string MatchId => _matchId;

        /// <summary>Host time for the engine: NGO server time, clamped so it never goes backwards (the engine requires monotonic time).</summary>
        public double Now
        {
            get
            {
                double t = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening ? NetworkManager.Singleton.ServerTime.Time : Time.realtimeSinceStartupAsDouble;
                if (t < _clock) t = _clock;
                _clock = t;
                return t;
            }
        }
        double _clock;

        public HostLineController()
        {
            for (int i = 0; i < SlotCount; i++) _slots[i] = new Slot { Index = i };
            ConfigJson = GameConfig.ToJson(GameConfig.Line);
            SessionId = "s-" + RandomHex(8);
            GroupId = ValidGroupCode(LaunchArgs.Current.PlaytestGroup) ? LaunchArgs.Current.PlaytestGroup : "g-" + RandomHex(6);
            Current = this;
        }

        public void Attach(NetSession session)
        {
            _session = session;
            HostLineVoiceBridge.Bind(this);
            foreach (var s in _slots)
                if (s.Occupied && !s.Virtual && s.Connected)
                    _session.SendLineWelcome(s.ClientId, new LineWelcome { Slot = s.Index, ReconnectToken = s.Token, WeekId = _weekId }, ConfigJson);
            HostTelemetryBridge.LobbyCreated(SessionId, GroupId, RecordingsEnabled);
            if (LaunchArgs.Current.FillBots > 0)
                for (int k = 0; k < LaunchArgs.Current.FillBots; k++) AddBot();
            BroadcastLobby();
        }

        static string RandomHex(int bytes)
        {
            var b = new byte[bytes];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(b);
            var sb = new System.Text.StringBuilder(bytes * 2);
            foreach (var x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        static bool ValidGroupCode(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length > 16) return false;
            foreach (char c in code) if (!(c < 128 && (char.IsLetterOrDigit(c) || c == '-' || c == '_'))) return false;
            return true;
        }

        /// <summary>Host lobby: facilitator's playtest group code (letters, digits, - and _; max 16). Empty → random id.</summary>
        public void SetGroupCode(string code)
        {
            if (WeekRunning) return;
            code = (code ?? "").Trim();
            if (code.Length == 0 && GroupId.StartsWith("g-")) return;
            string next = ValidGroupCode(code) ? code : "g-" + RandomHex(6);
            if (next == GroupId) return;
            GroupId = next;
            HostTelemetryBridge.LobbyCreated(SessionId, GroupId, RecordingsEnabled);
            BroadcastLobby();
        }

        /// <summary>Session history is kept per seat; a new occupant starts clean (wins, firings).</summary>
        void ResetSeatHistory(int slot)
        {
            if (_history == null) return;
            if (slot < _history.Wins.Count) _history.Wins[slot] = 0;
            if (slot < _history.TimesFired.Count) _history.TimesFired[slot] = 0;
        }

        static bool ValidAnonymousId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length != 34 || !id.StartsWith("p-")) return false;
            for (int i = 2; i < id.Length; i++) { char c = id[i]; if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false; }
            return true;
        }

        void SetConsent(int slot, int consent, string anonymousId)
        {
            var s = _slots[slot];
            s.TelemetryConsent = consent < 0 || consent > 3 ? 0 : consent;
            s.AnonymousId = s.TelemetryConsent >= 2 && ValidAnonymousId(anonymousId) ? anonymousId : "";
            HostTelemetryBridge.PlayerConsent(slot, s.TelemetryConsent, s.AnonymousId, s.Virtual);
        }

        /// <summary>A client changed its analytics consent (NetSession RPC).</summary>
        public void OnTelemetryConsent(ulong clientId, int consent, string anonymousId)
        {
            int slot;
            if (_clientSlot.TryGetValue(clientId, out slot)) SetConsent(slot, consent, anonymousId);
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
                    if (_slots[i].Occupied && !_slots[i].Virtual && !_slots[i].Connected && !string.IsNullOrEmpty(p.ReconnectToken) && _slots[i].Token == p.ReconnectToken
                        && !_pendingApprovals.ContainsValue(i))
                        slot = i;
                if (slot < 0) return new Approval { Reason = "reject.in_progress" };
            }
            else
            {
                for (int i = 0; i < SlotCount && slot < 0; i++)
                    if (!_slots[i].Occupied && !_pendingApprovals.ContainsValue(i)) slot = i;
                if (slot < 0) return new Approval { Reason = "reject.full" };
                var s = _slots[slot];
                ResetSeatHistory(slot); // a new person takes this seat: they do not inherit its session record
                s.Occupied = true;
                s.Virtual = false;
                s.Name = SanitizeName(p.PlayerName, slot);
                s.Token = Guid.NewGuid().ToString("N");
                s.Ready = isHostClient;
                s.IsHost = isHostClient;
            }
            _pendingApprovals[clientId] = slot;
            _pendingPayloads[clientId] = p;
            return new Approval { Approved = true, Slot = slot };
        }

        string SanitizeName(string name, int slot)
        {
            // Same rules as player text (no markup or control characters); names never reach analytics.
            name = (name ?? "").Trim();
            if (name.Length > 16) name = name.Substring(0, 16);
            LineFailure failure;
            name = LineText.Sanitize(name, 16, false, out failure) ?? "";
            if (failure != LineFailure.None) name = "";
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
            LineConnectionPayload payload;
            _pendingPayloads.TryGetValue(clientId, out payload);
            _pendingPayloads.Remove(clientId);
            var s = _slots[slot];
            if (!s.Occupied)
            {
                // The seat was released while this connection was pending (week ended / lobby reset): refuse cleanly.
                MatchLog.Write("host: client " + clientId + " lost its pending seat " + slot);
                NetworkManager.Singleton?.DisconnectClient(clientId, "reject.in_progress");
                return;
            }
            bool reconnect = WeekRunning && InWeek(slot);
            s.ClientId = clientId;
            s.Connected = true;
            s.MessagesSent = 0;      // (re)connected client: resend every message of the match
            s.MessagesDirty = true;
            _clientSlot[clientId] = slot;
            if (payload != null) SetConsent(slot, payload.TelemetryConsent, payload.AnonymousPlayerId);
            MatchLog.Write("host: client " + clientId + " -> slot " + slot + (reconnect ? " (reconnect)" : ""));
            if (_session != null && _session.IsSpawned)
                _session.SendLineWelcome(clientId, new LineWelcome { Slot = slot, ReconnectToken = s.Token, Reconnected = reconnect, WeekId = _weekId }, ConfigJson);
            if (reconnect && _engine != null)
            {
                try { _engine.SetConnected(slot, true, Now); }
                catch (Exception e) { Debug.LogException(e); HostTelemetryBridge.TechnicalFailure("engine_exception", e); }
                HostTelemetryBridge.PlayerConnection(slot, true, Now);
                SendViewTo(slot);
            }
            BroadcastLobby();
        }

        bool InWeek(int slot)
        {
            if (_engine == null) return false;
            foreach (var p in _engine.Setup.Players) if (p.Id == slot) return true;
            return false;
        }

        public void OnClientDisconnected(ulong clientId)
        {
            int pending;
            _pendingPayloads.Remove(clientId);
            if (_pendingApprovals.TryGetValue(clientId, out pending))
            {
                _pendingApprovals.Remove(clientId);
                var ps = _slots[pending];
                if (!WeekRunning && ps.Occupied && !ps.Connected && !ps.Virtual) { ps.Occupied = false; ps.Ready = false; ps.Name = ""; ps.Token = ""; BroadcastLobby(); }
            }
            int slot;
            if (!_clientSlot.TryGetValue(clientId, out slot)) return;
            _clientSlot.Remove(clientId);
            var s = _slots[slot];
            s.Connected = false;
            s.ClientId = ulong.MaxValue;
            MatchLog.Write("host: client " + clientId + " (slot " + slot + ") disconnected");
            if (_engine != null && InWeek(slot))
            {
                try { _engine.SetConnected(slot, false, Now); }
                catch (Exception e) { Debug.LogException(e); HostTelemetryBridge.TechnicalFailure("engine_exception", e); }
                HostTelemetryBridge.PlayerConnection(slot, false, Now);
            }
            else
            {
                s.Occupied = false; s.Ready = false; s.Name = ""; s.WantsRematch = false; s.TelemetryConsent = 0; s.AnonymousId = "";
                HostTelemetryBridge.SeatReleased(slot);
            }
            BroadcastLobby();
        }

        /// <summary>Slot of a connected client, or -1.</summary>
        public int SlotOfClient(ulong clientId) { int s; return _clientSlot.TryGetValue(clientId, out s) ? s : -1; }

        // ================================================================ lobby (host UI)

        public void SetReady(ulong clientId, bool ready)
        {
            int slot;
            if (WeekRunning || !_clientSlot.TryGetValue(clientId, out slot)) return;
            if (_slots[slot].Ready == ready) return;
            _slots[slot].Ready = ready;
            HostTelemetryBridge.PlayerReadyChanged(slot, ready);
            BroadcastLobby();
        }

        /// <summary>"Play again" intent from a results screen (shown in the lobby; the host still starts the match).</summary>
        public void SetRematch(ulong clientId, bool want)
        {
            int slot;
            if (!_clientSlot.TryGetValue(clientId, out slot) || _slots[slot].WantsRematch == want) return;
            _slots[slot].WantsRematch = want;
            BroadcastLobby();
        }

        /// <summary>A client noticed missing messages (view seq ahead of what arrived): resend after its last seq.</summary>
        public void ResyncMessages(ulong clientId, long afterSeq)
        {
            int slot;
            if (!_clientSlot.TryGetValue(clientId, out slot)) return;
            var s = _slots[slot];
            s.MessagesSent = Math.Max(0, Math.Min(afterSeq, s.MessagesSent));
            s.MessagesDirty = true;
        }

        /// <summary>Put a bot in the lowest free seat.</summary>
        public void AddBot()
        {
            if (WeekRunning) return;
            for (int i = 0; i < SlotCount; i++)
            {
                if (_slots[i].Occupied || _pendingApprovals.ContainsValue(i)) continue;
                var s = _slots[i];
                ResetSeatHistory(i);
                s.Occupied = true; s.Virtual = true; s.Connected = true; s.Ready = true; s.Token = "";
                s.Name = Loc.T("player.bot." + i);
                SetConsent(i, 2, ""); // synthetic seat: covered by the host's own consent, always flagged is_bot
                BroadcastLobby();
                return;
            }
        }

        public void RemoveBot(int slot)
        {
            if (WeekRunning || slot < 0 || slot >= SlotCount || !_slots[slot].Virtual) return;
            var s = _slots[slot];
            s.Occupied = false; s.Virtual = false; s.Connected = false; s.Ready = false; s.Name = ""; s.Bot = null; s.BotView = null; s.Talker = null;
            HostTelemetryBridge.SeatReleased(slot);
            BroadcastLobby();
        }

        public void SetRecordings(bool on)
        {
            if (WeekRunning || RecordingsEnabled == on) return;
            RecordingsEnabled = on;
            if (VoiceSystem.Instance != null) VoiceSystem.Instance.RecordingEnabled = on;
            BroadcastLobby();
        }

        public void SetPranks(bool on)
        {
            if (WeekRunning || PranksEnabled == on) return;
            PranksEnabled = on;
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
                BuildId = GameConfig.BuildId, WeekRunning = WeekRunning, WeekId = _weekId,
                RecordingsEnabled = RecordingsEnabled, PranksEnabled = PranksEnabled, WeeksPlayed = WeeksPlayed, StatusKey = reason,
                SessionId = SessionId, GroupId = GroupId,
            };
            lv.Experiments.AddRange(CurrentExperiments());
            foreach (var s in _slots)
            {
                int wins = _history != null && s.Index < _history.Wins.Count ? _history.Wins[s.Index] : 0;
                lv.Slots.Add(new LineLobbySlot { Slot = s.Index, Occupied = s.Occupied, Connected = s.Connected, Ready = s.Ready, IsHost = s.IsHost, IsBot = s.Virtual, Name = s.Name, Wins = wins, WantsRematch = s.WantsRematch });
            }
            return lv;
        }

        /// <summary>Declared experiment assignments of the next/current match ("id=variant").</summary>
        List<string> CurrentExperiments()
        {
            _experiments.Clear();
            _experiments.Add("recording_awareness=" + (RecordingsEnabled ? "on" : "off"));
            return _experiments;
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
            int humans = 0, bots = 0, readyCount = 0, rematchRequests = 0;
            foreach (var s in _slots)
            {
                if (!s.Occupied) continue;
                if (s.Virtual) bots++; else humans++;
                if (s.Ready && s.Connected) readyCount++;
                if (s.WantsRematch && !s.Virtual) rematchRequests++;
            }
            HostTelemetryBridge.MatchStartAttempted(humans, bots, readyCount);
            if (!CanStart(out reason) && !force) { MatchLog.Write("host: cannot start: " + reason); HostTelemetryBridge.MatchStartFailed(reason); return false; }
            _weekId++;
            var args = LaunchArgs.Current;
            var setup = new LineSetup
            {
                WeekId = _weekId,
                MatchId = "m-" + RandomHex(8),
                Seed = args.Seed != 0 ? args.Seed + _weekId - 1 : _rng.Next(1, int.MaxValue),
                StartTime = Now, // the engine clock starts here; anything earlier would be time going backwards
                RecordingsEnabled = RecordingsEnabled,
                PranksEnabled = PranksEnabled,
                History = _history,
            };
            for (int i = 0; i < SlotCount; i++)
                if (_slots[i].Occupied) setup.Players.Add(new LinePlayerInfo { Id = i, Name = _slots[i].Name, ColorIndex = i, IsBot = _slots[i].Virtual });
            var config = GameConfig.CopyLine();
            config.RecordingsEnabled = RecordingsEnabled;
            config.PranksEnabled = config.PranksEnabled && PranksEnabled;
            try { _engine = LineEngineFactory.Create(setup, config); }
            catch (Exception e)
            {
                Debug.LogException(e);
                MatchLog.Write("host: engine failed to start: " + e.GetType().Name);
                HostTelemetryBridge.MatchStartFailed("engine_error");
                HostTelemetryBridge.TechnicalFailure("engine_exception", e);
                _engine = null;
                _weekId--;
                return false;
            }
            bool rematch = _weeksCompleted > 0 && !string.IsNullOrEmpty(_lastMatchId);
            _matchId = setup.MatchId;
            _matchEndReported = false;
            HostTelemetryBridge.MatchStarted(_engine, setup, CurrentExperiments(), Now);
            if (rematch)
            {
                int returning = 0;
                foreach (var s in _slots) if (s.Occupied && !s.Virtual && s.Connected) returning++;
                HostTelemetryBridge.RematchStarted(_lastMatchId, returning, rematchRequests, LaunchArgs.Current.AutoStart);
            }
            ConfigJson = GameConfig.ToJson(config);
            _botConfig = _engine.Config;
            for (int i = 0; i < SlotCount; i++)
            {
                var s = _slots[i];
                s.WantsRematch = false;
                s.MessagesSent = 0;
                s.MessagesDirty = false;
                if (!s.Occupied) continue;
                if (s.Virtual)
                {
                    var policy = LaunchArgs.Current.PolicyFor(i, true);
                    s.Bot = new LineBot(i, setup.Seed * 31 + i, policy) { ThinkSeconds = BotThinkSeconds };
                    s.Talker = new LineBotTalker(i, setup.Seed * 17 + i, policy, s.Bot);
                    s.BotView = null;
                    s.BotRequest = 0;
                }
                else if (s.Connected && _session != null)
                    _session.SendLineWelcome(s.ClientId, new LineWelcome { Slot = i, ReconnectToken = s.Token, WeekId = _weekId }, ConfigJson);
                if (!s.Connected) _engine.SetConnected(i, false, Now);
            }
            _lastSentVersion = -1;
            _abandonedAt = -1;
            _resultLogged = false;
            _pranksLogged = 0;
            _prankSummary = false;
            _prankLogVersion = -1;
            ResetRecordings();
            // The seed reproduces every secret of the match: it is logged only once results are public (LogResult).
            _matchSeed = setup.Seed;
            MatchLog.Write("host: week " + _weekId + " started players=" + setup.Players.Count + " ruleset=" + LineProtocol.RulesetVersion
                + " recordings=" + RecordingsEnabled + " pranks=" + PranksEnabled + " weeksPlayed=" + WeeksPlayed);
            BroadcastLobby();
            SendViewsToAll();
            return true;
        }

        /// <summary>Results: end the match now (the session continues in the lobby).</summary>
        public void ContinueAfterResults()
        {
            if (_engine == null) return;
            if (_engine.Phase == LinePhase.Results && !_engine.IsFinished) _engine.HostContinue(Now);
            FinishWeek();
        }

        /// <summary>Stops the current match and returns everyone to the lobby.</summary>
        public void ReturnToLobby()
        {
            if (_engine != null)
                ReportMatchEnd(_engine.Phase == LinePhase.Abandoned ? "abandoned" : _engine.IsFinished || _engine.Phase == LinePhase.Results ? "results" : "host_closed");
            if (_engine != null && _engine.Phase == LinePhase.Results && !_engine.IsFinished)
            {
                try { _engine.HostContinue(Now); } // results are final: keep the session wins / firings
                catch (Exception e) { Debug.LogException(e); }
            }
            if (_engine != null && _engine.IsFinished) CaptureHistory();
            _engine = null;
            ResetRecordings();
            for (int i = 0; i < SlotCount; i++)
            {
                var s = _slots[i];
                if (s.Occupied && !s.Virtual && !s.Connected && !_pendingApprovals.ContainsValue(i)) { s.Occupied = false; s.Name = ""; }
                if (!s.Virtual && !s.IsHost) s.Ready = false;
                s.Bot = null; s.BotView = null; s.Talker = null;
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

        /// <summary>Exactly one MatchEnded per match (results, abandon or the host closing it).</summary>
        void ReportMatchEnd(string reason)
        {
            if (_matchEndReported || _engine == null) return;
            _matchEndReported = true;
            _lastMatchId = _matchId;
            HostTelemetryBridge.MatchEnded(_engine, reason, Now);
        }

        void LogResult()
        {
            _resultLogged = true;
            _weeksCompleted++;
            var r = _engine.Result;
            if (r != null) MatchLog.Write("host: " + LineClient.ResultLine(r) + " seed=" + _matchSeed);
            LogRules(r);
            LogPranks();
            // Results are public now: telemetry may flush the private-until-match-end buffer.
            if (_engine.Phase == LinePhase.Results || _engine.IsFinished) ReportMatchEnd("results");
        }

        /// <summary>
        /// The v5.1 rules at Results (verify_logs.py): the week's punishments, the Weekend Shift (staffed or short, the
        /// volunteers' cleaning, the skippers' automatic suç) and the candidates / firings.
        /// </summary>
        void LogRules(LineWeekResult r)
        {
            if (r == null || _engine == null) return;
            int whistles = 0, investigations = 0, caught = 0, volunteers = 0, cleaned = 0, skipped = 0, skipBlame = 0, candidates = 0;
            foreach (var d in r.Incidents)
            {
                if (d.Kind == DossierKind.Whistle) whistles++;
                else if (d.Kind == DossierKind.Investigation) investigations++;
                else if (d.Kind == DossierKind.CashCaught) caught++;
            }
            foreach (var c in r.Choices) if (c.Task == TaskKind.WeekendShift && c.Choice == LineChoice.Volunteer) volunteers++;
            foreach (var b in r.BlameLog)
            {
                if (b.Reason == BlameReason.ShiftClean) cleaned -= b.Delta;
                if (b.Reason == BlameReason.ShiftFailSkip) { skipped++; skipBlame += b.Delta; }
            }
            foreach (var p in r.Players) if (p.WasCandidate) candidates++;
            int needed = LineConfig.ShiftNeeded(r.PlayerCount);
            MatchLog.Write("host: rules week=" + r.WeekId + " ruleset=" + LineProtocol.RulesetVersion + " whistles=" + whistles + " investigations=" + investigations
                + " caught=" + caught + " shift=" + (volunteers >= needed ? "staffed" : "short") + " volunteers=" + volunteers + "/" + needed + " cleaned=" + cleaned
                + " skip_blamed=" + skipped + " skip_blame=" + skipBlame + " candidates=" + candidates + " firings=" + r.Firings.Count + "/" + _engine.Config.FiringsFor(r.PlayerCount)
                + " transfers=" + r.Transfers.Count);
        }

        public void Abort()
        {
            // Clients learn about it from the lobby (WeekRunning=false) or the transport going away.
            ReportMatchEnd("host_closed");
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
            catch (Exception e) { Debug.LogException(e); HostTelemetryBridge.TechnicalFailure("engine_exception", e); }
            HostTelemetryBridge.EngineTick(_engine, now);
            if (_engine.Phase != _lastPhase)
            {
                // v5: a call recorded in task 1 may be leaked as late as the final discussion: keep all call audio
                // until the match ends.
                if (_engine.Phase == LinePhase.Results || _engine.Phase == LinePhase.Abandoned) VoiceSystem.Instance?.ClearRecordings();
                _lastPhase = _engine.Phase;
            }

            if (now >= _nextBotTick) { _nextBotTick = now + BotInterval; TickBots(now); }
            TriggerRecordings(now);

            List<LineEvent> events;
            try { events = _engine.DrainEvents(); }
            catch (Exception e) { Debug.LogException(e); HostTelemetryBridge.TechnicalFailure("engine_exception", e); events = null; }
            if (events != null && events.Count > 0)
            {
                HostTelemetryBridge.EngineEvents(_engine, events, now);
                RouteEvents(events);
            }
            PushMessages();
            TickGaze();

            bool changed = _engine.StateVersion != _lastSentVersion;
            if ((changed && now - _lastViewSend >= ViewInterval) || now - _lastHeartbeat >= HeartbeatInterval) SendViewsToAll();

            if (_engine.Phase == LinePhase.Results && !_resultLogged && _engine.Result != null) LogResult();
            else if (_resultLogged && _engine.Phase == LinePhase.Results && !_engine.IsFinished) LogPranks();
            if (_engine.IsFinished)
            {
                if (_engine.Phase == LinePhase.Abandoned)
                {
                    ReportMatchEnd("abandoned");
                    if (_abandonedAt < 0) _abandonedAt = now;
                    else if (now - _abandonedAt > 10.0) ReturnToLobby();
                }
                else FinishWeek();
            }
        }

        LineConfig _botConfig;
        readonly BotTalkOutput _talk = new BotTalkOutput();

        void TickBots(double now)
        {
            foreach (var s in _slots)
            {
                if (!s.Virtual || s.Bot == null || _engine == null) continue;
                try
                {
                    s.BotView = _engine.BuildView(s.Index, now);
                    var a = s.Bot.Next(s.BotView, _engine.Config, now);
                    if (a != null) _engine.Apply(s.Index, BotAction(s, a), now);
                    if (s.Talker != null) Talk(s, now, a != null);
                }
                catch (Exception e) { Debug.LogException(e); HostTelemetryBridge.TechnicalFailure("engine_exception", e); }
            }
        }

        /// <summary>The bot's and its talker's actions share one increasing request id sequence per seat.</summary>
        static LineAction BotAction(Slot s, LineAction a)
        {
            return new LineAction(a.WeekId, ++s.BotRequest, a.Verb, a.Target, a.A, a.B, a.C, a.Text);
        }

        /// <summary>v5 bot talk (docs/V5_BOT_TALK.md): the host's bot talks after it acts (calls, answers, chat, payments);
        /// its phone lines go to the human at the other end, a question to another of the host's bots goes straight to it.</summary>
        void Talk(Slot s, double now, bool acted)
        {
            var view = acted ? _engine.BuildView(s.Index, now) : s.BotView;
            _talk.Clear();
            s.Talker.Tick(view, _botConfig ?? _engine.Config, now, _engine.MessagesFor(s.Index, s.Talker.MessagesSeen), _talk);
            foreach (var a in _talk.Actions) _engine.Apply(s.Index, BotAction(s, a), now);
            foreach (var l in _talk.Lines) DeliverTalk(l.Listener, BotSpeech.PackLine(_weekId, l));
            foreach (var l in _talk.Shouts) Shout(BotSpeech.PackShout(_weekId, l), -1);
            foreach (var q in _talk.Asks)
            {
                var to = q.To >= 0 && q.To < SlotCount ? _slots[q.To] : null;
                if (to != null && to.Virtual) { if (to.Talker != null) to.Talker.Hear(q); }
                else DeliverTalk(q.To, BotSpeech.PackAsk(_weekId, q));
            }
            foreach (var line in _talk.Log) MatchLog.Write(line);
        }

        /// <summary>A reaction out loud: every person's (and -bot process's) client but the speaker's own process.</summary>
        void Shout(byte[] packet, int except)
        {
            for (int i = 0; i < SlotCount; i++) if (i != except) DeliverTalk(i, packet);
        }

        int _pranksLogged;
        bool _prankSummary;
        long _prankLogVersion = -1;

        /// <summary>
        /// Yaramazlıklar log (verify_logs.py): every prank with its actor once the results are public (a phone ring's actor is
        /// secret until then, so nothing is written during the match), then the cakes as they land.
        /// </summary>
        void LogPranks()
        {
            if (_engine == null || _engine.Phase != LinePhase.Results || _engine.StateVersion == _prankLogVersion) return;
            _prankLogVersion = _engine.StateVersion;
            LineView v;
            try { v = _engine.BuildView(FirstSeat(), Now); }
            catch (Exception e) { Debug.LogException(e); return; }
            var pv = v.Pranks;
            if (!_prankSummary)
            {
                _prankSummary = true;
                int tutanak = 0, burned = 0;
                if (pv != null) foreach (var e in pv.Log) { if (e.Tutanak) tutanak++; burned += e.Cost; }
                MatchLog.Write("host: pranks week=" + _weekId + " enabled=" + (pv != null ? "yes" : "no") + " total=" + (pv != null ? pv.Log.Count : 0)
                    + " tutanak=" + tutanak + " burned=" + burned);
            }
            if (pv == null) return;
            for (int i = _pranksLogged; i < pv.Log.Count; i++)
            {
                var e = pv.Log[i];
                MatchLog.Write(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "host: prank i={0} kind={1} actor={2} target={3} session={4} phase={5} cost={6} patience={7} after={8} tutanak={9} culprit={10}",
                    e.Index, e.Kind, e.Actor, e.Target, e.Session, e.Phase, e.Cost, e.Patience, e.PatienceAfter, e.Tutanak ? "yes" : "no", e.Culprit));
            }
            _pranksLogged = pv.Log.Count;
        }

        int FirstSeat()
        {
            foreach (var p in _engine.Setup.Players) return p.Id;
            return 0;
        }

        /// <summary>A bot talk packet to a person's (or a -bot process's) client.</summary>
        void DeliverTalk(int slot, byte[] packet)
        {
            if (slot < 0 || slot >= SlotCount || _session == null) return;
            var t = _slots[slot];
            if (!t.Occupied || t.Virtual || !t.Connected) return;
            try { _session.SendBotTalk(t.ClientId, packet); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>
        /// A client's bot talk packet (a -bot process's phone line or question, or a person's question button): forwarded
        /// only between the two ends of a call (connected, or the callee turning it down), never to anyone else.
        /// </summary>
        public void OnBotTalk(ulong clientId, byte[] packet)
        {
            int slot, week, from, to;
            bool ask;
            if (_engine == null || !_clientSlot.TryGetValue(clientId, out slot)) return;
            // Yaramazlıklar: a -bot player's reaction out loud goes to the whole room (only its own, only this week)
            if (BotSpeech.IsShout(packet, out week, out from))
            {
                if (from == slot && week == (_weekId & 0xFF)) Shout(packet, slot);
                return;
            }
            if (!BotSpeech.Peek(packet, out week, out from, out to, out ask) || from != slot || week != (_weekId & 0xFF) || to == from) return;
            int partner = _engine.CallPartner(from);
            if (partner != to)
            {
                LineView v;
                try { v = _engine.BuildView(from, Now); }
                catch (Exception e) { Debug.LogException(e); return; }
                if (v.Mine.Phone.Other != to || v.Mine.Phone.Status == CallStatus.Idle) return;
            }
            var target = _slots[to];
            if (target.Virtual)
            {
                BotAsk q;
                if (ask && target.Talker != null && BotSpeech.TryAsk(packet, out q)) target.Talker.Hear(q);
                return;
            }
            DeliverTalk(to, packet);
        }

        /// <summary>Drops the match's call audio, stops a running tape and forgets the scheduled picks (match start, lobby).</summary>
        void ResetRecordings()
        {
            var vs = VoiceSystem.Instance;
            if (vs != null) { vs.StopRecording(); vs.ClearRecordings(); }
            _recordingsTriggered.Clear();
            _recordingsSegment = -1;
            _picksVersion = -1;
            _picks = null;
            _lastPhase = LinePhase.None;
        }

        /// <summary>
        /// Leak segment of a phase, or -1: meeting 1 / meeting 2 (dossier .. verdict) and the final (discussion .. verdict).
        /// The engine clears its picks when a segment starts, so pick indices restart there.
        /// </summary>
        static int LeakSegment(LinePhase phase, int day)
        {
            if (phase >= LinePhase.MeetingDossier && phase <= LinePhase.MeetingVerdict) return day * 2;
            if (phase >= LinePhase.FinalDiscussion && phase <= LinePhase.FinalVerdict) return day * 2 + 1;
            return -1;
        }

        /// <summary>
        /// v5 leaks: at each pick's PlayAt hand it to voice (one tape at a time) and report back whether it played. Leaks
        /// are queued during MeetingDossier/MeetingDiscussion/FinalDiscussion and may play on into the vote.
        /// </summary>
        void TriggerRecordings(double now)
        {
            int segment = LeakSegment(_engine.Phase, _engine.Day);
            if (segment < 0) { _recordingsSegment = -1; _picks = null; _picksVersion = -1; return; }
            if (segment != _recordingsSegment)
            {
                _recordingsSegment = segment;
                _recordingsTriggered.Clear();
                _picksVersion = -1;
            }
            if (_engine.StateVersion != _picksVersion)
            {
                // RecordingPicks returns a detached copy: re-read it only when the state changed (a leak bumps it).
                _picksVersion = _engine.StateVersion;
                _picks = _engine.RecordingPicks;
            }
            var picks = _picks;
            if (picks == null || picks.Count == 0 || _recordingsTriggered.Count >= picks.Count) return;
            var voice = VoiceSystem.Instance;
            if (voice != null && voice.HostRecordingPlaying) return; // never cut a tape short; the next one waits
            for (int i = 0; i < picks.Count; i++)
            {
                var p = picks[i];
                if (_recordingsTriggered.Contains(p.Index) || now < p.PlayAt) continue;
                _recordingsTriggered.Add(p.Index);
                int chosen = LineIds.None;
                var info = new RecordingPlayInfo { Candidates = p.CandidateCallIds.Count, ChosenRank = -1, Failure = "voice_unavailable" };
                try
                {
                    if (PlayRecording != null) chosen = PlayRecording(p, out info);
                    else if (p.CandidateCallIds.Count > 0) { chosen = p.CandidateCallIds[0]; info.ChosenRank = 0; info.Failure = "none"; }
                }
                catch (Exception e) { Debug.LogException(e); HostTelemetryBridge.TechnicalFailure("voice_exception", e); }
                MatchLog.Write("host: recording pick=" + p.Index + " candidates=" + p.CandidateCallIds.Count + " played=" + (chosen >= 0 ? "yes" : "no") + " failure=" + info.Failure);
                _engine.ReportRecording(p.Index, chosen, now);
                // The cached pick predates the report: hand analytics the reported copy (caller, callee, leaker).
                var reported = p;
                try { foreach (var x in _engine.RecordingPicks) if (x.Index == p.Index) { reported = x; break; } }
                catch (Exception e) { Debug.LogException(e); }
                HostTelemetryBridge.RecordingStarted(_engine, reported, chosen, info, now);
                break; // one per frame; the next waits until this tape has finished
            }
        }

        // ================================================================ v5 step 3a: shared gaze (presentation only)

        readonly sbyte[] _gazeYaw = new sbyte[SlotCount], _gazePitch = new sbyte[SlotCount];
        readonly float[] _gazeAt = new float[SlotCount];
        readonly BotGazer[] _botGaze = new BotGazer[SlotCount];
        readonly List<int> _gazeSlots = new List<int>();
        readonly List<sbyte> _gazeY = new List<sbyte>(), _gazeP = new List<sbyte>();
        float _nextGaze;

        /// <summary>A client's look direction at the meeting table (never enters the engine, logs or analytics).</summary>
        public void OnGaze(ulong clientId, sbyte yaw, sbyte pitch)
        {
            int slot;
            if (!_clientSlot.TryGetValue(clientId, out slot) || slot < 0 || slot >= SlotCount) return;
            _gazeYaw[slot] = yaw; _gazePitch[slot] = pitch; _gazeAt[slot] = Time.unscaledTime;
        }

        /// <summary>~8 Hz in the meeting room: the host's bots pick where to look, then everyone's fresh gaze goes out in one packet.</summary>
        void TickGaze()
        {
            if (_engine == null || _session == null) return;
            float now = Time.unscaledTime;
            if (now < _nextGaze) return;
            _nextGaze = now + MeetingGaze.Interval;
            var phase = _engine.Phase;
            if (!LineClient.IsMeetingPhase(phase) && phase != LinePhase.Results) return;
            var room = RoomsPresenter.Instance != null && RoomsPresenter.Instance.InMeeting ? RoomsPresenter.Instance.Meeting : null;
            var view = LineClient.I != null ? LineClient.I.View : null;
            _gazeSlots.Clear(); _gazeY.Clear(); _gazeP.Clear();
            for (int i = 0; i < SlotCount; i++)
            {
                var s = _slots[i];
                if (!s.Occupied) continue;
                if (s.Virtual)
                {
                    if (room == null || view == null) continue;
                    if (_botGaze[i] == null) _botGaze[i] = new BotGazer(i, _matchSeed + 1);
                    float y, p;
                    if (!_botGaze[i].Next(view, room, now, out y, out p)) continue;
                    _gazeYaw[i] = MeetingGaze.Quantise(y, 90); _gazePitch[i] = MeetingGaze.Quantise(p, 60); _gazeAt[i] = now;
                }
                if (now - _gazeAt[i] > 1.5f || _gazeAt[i] <= 0f) continue;
                _gazeSlots.Add(i); _gazeY.Add(_gazeYaw[i]); _gazeP.Add(_gazePitch[i]);
            }
            if (_gazeSlots.Count == 0) return;
            try { _session.BroadcastGaze(MeetingGaze.Pack(_gazeSlots, _gazeY, _gazeP)); }
            catch (Exception e) { Debug.LogException(e); }
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
                    if (e.Type == LineEventType.MessageReceived) s.MessagesDirty = true;
                    if (mine == null) mine = new List<LineEvent>();
                    mine.Add(e);
                }
                if (mine != null) _session.SendLineEvents(s.ClientId, mine);
            }
            // Host-audience events carry private choices/incidents: HostTelemetryBridge only, never logs or clients.
        }

        /// <summary>Pushes new messages (free text) to each human client; reconnects and resync requests resend from their seq.</summary>
        void PushMessages()
        {
            if (_engine == null || _session == null) return;
            for (int i = 0; i < SlotCount; i++)
            {
                var s = _slots[i];
                if (!s.MessagesDirty || !s.Occupied || !s.Connected || s.Virtual) continue;
                s.MessagesDirty = false;
                List<LineMessage> batch;
                try { batch = _engine.MessagesFor(i, s.MessagesSent); }
                catch (Exception e) { Debug.LogException(e); HostTelemetryBridge.TechnicalFailure("engine_exception", e); continue; }
                if (batch == null || batch.Count == 0) continue;
                _session.SendLineMessages(s.ClientId, _weekId, batch);
                s.MessagesSent = batch[batch.Count - 1].Seq;
            }
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
            catch (Exception e) { HostTelemetryBridge.TechnicalFailure("rpc_decode", e); return; }
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
                    HostTelemetryBridge.TechnicalFailure("engine_exception", e);
                    result = LineActionResult.Fail(req.RequestId, LineFailure.BadParam);
                }
            }
            _session.SendLineResult(clientId, result);
        }

        public void Dispose()
        {
            ReportMatchEnd("host_closed");
            if (Current == this) Current = null;
            _engine = null;
            HostLineVoiceBridge.Unbind();
        }
    }
}
