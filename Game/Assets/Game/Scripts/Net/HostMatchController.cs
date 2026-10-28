using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using Newtonsoft.Json;
using Unity.Netcode;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Host-only session authority: lobby slots, connection approval, reconnect tokens, the MatchEngine,
    /// per-recipient view/event routing and rematches. Lives for one hosting session.
    /// </summary>
    public sealed class HostMatchController
    {
        public const int SlotCount = Protocol.MaxPlayers;
        const double ViewInterval = 0.1;      // max 10 Hz per client
        const double HeartbeatInterval = 2.0; // resend even without changes (clock / lost-state safety)

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
            public Vector3 LastPosition;
            public float LastYaw;
            public VirtualBotChannel Bot;
        }

        readonly Slot[] _slots = new Slot[SlotCount];
        readonly Dictionary<ulong, int> _clientSlot = new Dictionary<ulong, int>();
        readonly Dictionary<ulong, int> _pendingApprovals = new Dictionary<ulong, int>();
        NetSession _session;
        MatchEngine _engine;
        SessionHistory _history;
        int _matchId;
        int _matchesCompleted;
        int _lastSentVersion = -1;
        double _lastViewSend;
        double _lastHeartbeat;
        double _abandonedAt = -1;
        readonly System.Random _rng = new System.Random();
        readonly Dictionary<int, Vector3> _prevPos = new Dictionary<int, Vector3>();
        readonly Dictionary<int, float> _speed = new Dictionary<int, float>();
        bool _resultLogged;

        public MatchEngine Engine => _engine;
        public bool MatchRunning => _engine != null;
        public Slot[] Slots => _slots;
        public int FillBots { get; private set; }
        public string ConfigJson { get; private set; }
        public string DayModifier { get; private set; } = DayModifiers.Normal;
        public GameMode Mode { get; private set; } = GameMode.QuickPlay;
        /// <summary>Week mode: the day the next match will be (0 = Pazartesi).</summary>
        public int NextWeekDay => Mode == GameMode.Week && _history != null ? _history.NextWeekDay : 0;

        public HostMatchController()
        {
            for (int i = 0; i < SlotCount; i++) _slots[i] = new Slot { Index = i };
            ConfigJson = GameConfig.ToJson(GameConfig.Match);
        }

        public void Attach(NetSession session)
        {
            _session = session;
            // Clients approved before the session object spawned (the host's own client) still need a welcome.
            foreach (var s in _slots)
                if (s.Occupied && !s.Virtual && s.Connected)
                    _session.SendWelcome(s.ClientId, new WelcomeMessage { Slot = s.Index, ReconnectToken = s.Token, MatchId = _matchId }, ConfigJson);
            if (LaunchArgs.Current.WeekMode) Mode = GameMode.Week;
            if (LaunchArgs.Current.FillBots > 0) SetFillBots(LaunchArgs.Current.FillBots);
            BroadcastLobby();
        }

        // ================================================================ approval & connections

        public struct Approval
        {
            public bool Approved;
            public string Reason;
            public int Slot;
            public Vector3 Position;
            public Quaternion Rotation;
        }

        public Approval Approve(ulong clientId, byte[] payload, bool isHostClient)
        {
            ConnectionPayload p;
            try { p = WireCodec.DecodeConnectionPayload(payload); }
            catch (Exception) { return new Approval { Reason = "reject.version" }; }
            if (p.ProtocolVersion != Protocol.Version || p.BuildId != GameConfig.BuildId)
                return new Approval { Reason = "reject.version" };

            int slot = -1;
            if (MatchRunning)
            {
                // Mid-match: only a disconnected player holding its reconnect token may come back.
                for (int i = 0; i < SlotCount; i++)
                    if (_slots[i].Occupied && !_slots[i].Virtual && !_slots[i].Connected && _slots[i].Token == p.ReconnectToken && !string.IsNullOrEmpty(p.ReconnectToken))
                        slot = i;
                if (slot < 0) return new Approval { Reason = "reject.in_progress" };
            }
            else
            {
                for (int i = 0; i < SlotCount && slot < 0; i++)
                    if (!_slots[i].Occupied && !_pendingApprovals.ContainsValue(i)) slot = i;
                if (slot < 0) return new Approval { Reason = "reject.full" };
            }

            var s = _slots[slot];
            if (!MatchRunning)
            {
                s.Occupied = true;
                s.Virtual = false;
                s.Name = SanitizeName(p.PlayerName, slot);
                s.Token = Guid.NewGuid().ToString("N");
                s.Ready = isHostClient;
                s.IsHost = isHostClient;
                var seat = GameConfig.Layout.Desks[slot].SeatPosition;
                s.LastPosition = new Vector3(seat.X, seat.Y, seat.Z);
                s.LastYaw = GameConfig.Layout.Desks[slot].Yaw;
            }
            _pendingApprovals[clientId] = slot;
            return new Approval
            {
                Approved = true,
                Slot = slot,
                Position = s.LastPosition + Vector3.up * 0.05f,
                Rotation = Quaternion.Euler(0, s.LastYaw, 0),
            };
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
            bool reconnect = MatchRunning;
            s.ClientId = clientId;
            s.Connected = true;
            _clientSlot[clientId] = slot;
            AssignAvatarSlot(clientId, slot);
            MatchLog.Write("host: client " + clientId + " -> slot " + slot + (reconnect ? " (reconnect)" : ""));
            if (_session != null && _session.IsSpawned)
                _session.SendWelcome(clientId, new WelcomeMessage { Slot = slot, ReconnectToken = s.Token, Reconnected = reconnect, MatchId = _matchId }, ConfigJson);
            if (reconnect && _engine != null)
            {
                _engine.SetConnected(slot, true, Now);
                SendViewTo(slot, true);
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
            if (_engine != null)
            {
                _engine.SetConnected(slot, false, Now);
            }
            else
            {
                s.Occupied = false;
                s.Ready = false;
                s.Name = "";
            }
            BroadcastLobby();
        }

        void AssignAvatarSlot(ulong clientId, int slot)
        {
            var nm = NetworkManager.Singleton;
            if (nm.ConnectedClients.TryGetValue(clientId, out var client) && client.PlayerObject != null)
            {
                var avatar = client.PlayerObject.GetComponent<PlayerAvatar>();
                if (avatar != null) avatar.ServerAssignSlot(slot);
            }
        }

        // ================================================================ lobby

        public void SetReady(ulong clientId, bool ready)
        {
            int slot;
            if (MatchRunning || !_clientSlot.TryGetValue(clientId, out slot)) return;
            _slots[slot].Ready = ready;
            BroadcastLobby();
        }

        public void SetFillBots(int count)
        {
            if (MatchRunning) return;
            FillBots = Mathf.Clamp(count, 0, SlotCount - 1);
            // Remove existing virtual bots, then fill free slots from the end.
            for (int i = 0; i < SlotCount; i++)
                if (_slots[i].Virtual) RemoveVirtual(i);
            int toAdd = FillBots;
            for (int i = SlotCount - 1; i >= 0 && toAdd > 0; i--)
            {
                if (_slots[i].Occupied || _pendingApprovals.ContainsValue(i)) continue;
                AddVirtual(i);
                toAdd--;
            }
            BroadcastLobby();
        }

        /// <summary>Lobby (host UI): put a bot in the lowest free seat. Returns the slot or -1.</summary>
        public int AddBot()
        {
            if (MatchRunning) return -1;
            for (int i = 0; i < SlotCount; i++)
            {
                if (_slots[i].Occupied || _pendingApprovals.ContainsValue(i)) continue;
                AddVirtual(i);
                BroadcastLobby();
                return i;
            }
            return -1;
        }

        public void RemoveBot(int slot)
        {
            if (MatchRunning || slot < 0 || slot >= SlotCount || !_slots[slot].Virtual) return;
            RemoveVirtual(slot);
            BroadcastLobby();
        }

        /// <summary>Lobby (host UI): switching mode starts a fresh session history (a week starts on Monday).</summary>
        public void SetMode(GameMode mode)
        {
            if (MatchRunning || Mode == mode) return;
            Mode = mode;
            _history = null;
            BroadcastLobby();
        }

        void AddVirtual(int i)
        {
            var s = _slots[i];
            s.Occupied = true;
            s.Virtual = true;
            s.Connected = true;
            s.Ready = true;
            s.Name = Loc.T("player.bot." + i);
            s.Token = "";
            var seat = GameConfig.Layout.Desks[i].SeatPosition;
            s.LastPosition = new Vector3(seat.X, seat.Y, seat.Z);
            s.Bot = new VirtualBotChannel(this, i);
            s.Bot.SpawnAvatar(s.LastPosition, GameConfig.Layout.Desks[i].Yaw);
        }

        void RemoveVirtual(int i)
        {
            var s = _slots[i];
            s.Bot?.Dispose();
            s.Bot = null;
            s.Occupied = false;
            s.Virtual = false;
            s.Connected = false;
            s.Ready = false;
            s.Name = "";
        }

        public bool CanStart(out string reasonKey)
        {
            int occupied = 0, ready = 0;
            foreach (var s in _slots)
            {
                if (!s.Occupied) continue;
                occupied++;
                if (s.Ready && s.Connected) ready++;
            }
            if (MatchRunning) { reasonKey = "lobby.running"; return false; }
            // Automated runs (-autostart) wait for the expected head count instead of starting at the minimum.
            int need = LaunchArgs.Current.AutoStart ? Mathf.Clamp(LaunchArgs.Current.ExpectPlayers, Protocol.MinPlayers, SlotCount) : Protocol.MinPlayers;
            if (occupied < need) { reasonKey = "lobby.need_players"; return false; }
            if (ready < occupied) { reasonKey = "lobby.need_ready"; return false; }
            reasonKey = "lobby.ready_to_start";
            return true;
        }

        public LobbyView BuildLobby()
        {
            string reason;
            CanStart(out reason);
            var lv = new LobbyView { BuildId = GameConfig.BuildId, MatchRunning = MatchRunning, MatchId = _matchId, StatusKey = MatchRunning ? DayModifier : reason, Mode = Mode, WeekDay = NextWeekDay };
            foreach (var s in _slots)
                lv.Slots.Add(new LobbySlot { Slot = s.Index, Occupied = s.Occupied, Connected = s.Connected, Ready = s.Ready, IsHost = s.IsHost, Name = s.Name, IsBot = s.Virtual });
            if (Mode == GameMode.Week && _history != null) lv.CareerPoints.AddRange(_history.CareerPoints);
            return lv;
        }

        public void BroadcastLobby()
        {
            if (_session == null || !_session.IsSpawned) return;
            _session.BroadcastLobby(BuildLobby());
        }

        // ================================================================ match lifecycle

        public double Now => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening ? NetworkManager.Singleton.ServerTime.Time : Time.realtimeSinceStartupAsDouble;

        public bool StartMatch(bool force = false)
        {
            string reason;
            if (!CanStart(out reason) && !force) { MatchLog.Write("host: cannot start: " + reason); return false; }
            _matchId++;
            var args = LaunchArgs.Current;
            var setup = new MatchSetup
            {
                MatchId = _matchId,
                Seed = args.Seed != 0 ? args.Seed + _matchId - 1 : _rng.Next(1, int.MaxValue),
                StartTime = Now,
                History = _history,
                ForceInformant = args.ForceInformant,
                Mode = Mode,
                WeekDay = NextWeekDay,
                ManagerProfileId = args.ManagerProfile ?? "",
            };
            ScenarioSetup.Apply(args.Scenario, setup);
            for (int i = 0; i < SlotCount; i++)
                if (_slots[i].Occupied) setup.Players.Add(new PlayerSlotInfo { Id = i, Name = _slots[i].Name, ColorIndex = i });
            // Today's conditions: a per-match copy of the config (the first match of a session is a normal day).
            var day = string.IsNullOrEmpty(args.Scenario) ? DayModifiers.Pick(_matchesCompleted, setup.Seed) : DayModifiers.All[0];
            var config = JsonConvert.DeserializeObject<MatchConfig>(GameConfig.ToJson(GameConfig.Match), GameConfig.JsonSettings);
            day.Apply(config);
            config.ApplyHeadcount(setup.Players.Count);
            DayModifier = day.Id;
            try
            {
                _engine = new MatchEngine(config, GameConfig.Layout, setup, new UnityWorldQueries());
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                MatchLog.Write("host: engine failed to start: " + e.Message);
                _engine = null;
                return false;
            }
            // Clients get the engine's effective config (manager profile + week day applied) for prompts and timers.
            ConfigJson = GameConfig.ToJson(_engine.Config);
            foreach (var s in _slots)
                if (s.Occupied && !s.Virtual && s.Connected && _session != null)
                    _session.SendWelcome(s.ClientId, new WelcomeMessage { Slot = s.Index, ReconnectToken = s.Token, MatchId = _matchId }, ConfigJson);
            for (int i = 0; i < SlotCount; i++)
                if (_slots[i].Occupied && !_slots[i].Connected) _engine.SetConnected(i, false, Now);
            _lastSentVersion = -1;
            _abandonedAt = -1;
            _resultLogged = false;
            MatchLog.Write("host: match " + _matchId + " started seed=" + setup.Seed + " day=" + DayModifier + " players=" + setup.Players.Count
                + " mode=" + Mode + " weekday=" + setup.WeekDay + " manager=" + _engine.BuildHostDebugView().ManagerProfileId);
            BroadcastLobby();
            SendViewsToAll(true);
            return true;
        }

        /// <summary>Stops the current match and returns everyone to the lobby.</summary>
        public void ReturnToLobby()
        {
            if (_engine != null && !_engine.IsFinished) _engine.Abandon(AbandonReason.HostAborted, Now);
            if (_engine != null) SendViewsToAll(true);
            _engine = null;
            for (int i = 0; i < SlotCount; i++)
            {
                var s = _slots[i];
                if (s.Occupied && !s.Virtual && !s.Connected) { s.Occupied = false; s.Name = ""; }
                if (!s.Virtual && !s.IsHost) s.Ready = false;
            }
            BroadcastLobby();
        }

        public void Abort()
        {
            if (_engine != null && !_engine.IsFinished)
            {
                _engine.Abandon(AbandonReason.HostLeft, Now);
                SendViewsToAll(true);
            }
        }

        // ================================================================ per-frame

        public void Update()
        {
            if (_session == null) return;
            double now = Now;
            foreach (var s in _slots) s.Bot?.Update();
            if (_engine == null) return;

            SampleAvatars(now);
            try { _engine.Tick(now); }
            catch (Exception e) { Debug.LogException(e); }

            List<GameEvent> events;
            try { events = _engine.DrainEvents(); }
            catch (Exception e) { Debug.LogException(e); events = new List<GameEvent>(); }
            if (events.Count > 0) RouteEvents(events);

            bool changed = _engine.StateVersion != _lastSentVersion;
            if ((changed && now - _lastViewSend >= ViewInterval) || now - _lastHeartbeat >= HeartbeatInterval)
            {
                SendViewsToAll(false);
            }

            if (_engine.Phase == MatchPhase.Results)
            {
                if (!_resultLogged && _engine.Result != null)
                {
                    _resultLogged = true;
                    _matchesCompleted++;
                    _history = _engine.BuildNextHistory();
                    MatchLog.Write("host: results match=" + _engine.MatchId + " hash=" + _engine.Result.ResultHash + " fired=" + _engine.Result.FiredPlayer);
                    try { MatchLog.WriteFile("host_truth_match" + _engine.MatchId + ".json", JsonConvert.SerializeObject(_engine.BuildHostDebugView(), GameConfig.JsonSettings)); }
                    catch (Exception e) { Debug.LogWarning(e.Message); }
                }
                if (_engine.AllRematchVotesIn) StartRematch();
            }
            else if (_engine.Phase == MatchPhase.Abandoned)
            {
                if (_abandonedAt < 0) _abandonedAt = now;
                else if (now - _abandonedAt > 10.0) ReturnToLobby();
            }
        }

        public void StartRematch()
        {
            if (_engine == null) return;
            if (_history == null) _history = _engine.BuildNextHistory();
            _engine = null;
            StartMatch(true);
        }

        void SampleAvatars(double now)
        {
            foreach (var avatar in PlayerAvatar.All)
            {
                int slot = avatar.Slot;
                if (slot < 0 || slot >= SlotCount) continue;
                Vector3 p = avatar.transform.position;
                float dt = Mathf.Max(Time.deltaTime, 1e-3f);
                Vector3 prev;
                float speed = 0f;
                if (_prevPos.TryGetValue(slot, out prev))
                {
                    Vector3 d = p - prev; d.y = 0;
                    float inst = d.magnitude / dt;
                    float old;
                    _speed.TryGetValue(slot, out old);
                    speed = Mathf.Lerp(old, inst, 0.35f);
                }
                _prevPos[slot] = p;
                _speed[slot] = speed;
                _slots[slot].LastPosition = p;
                _slots[slot].LastYaw = avatar.transform.eulerAngles.y;
                _engine.UpdateAvatar(slot, new AvatarSample { Position = new Vec3(p.x, p.y, p.z), Yaw = avatar.transform.eulerAngles.y, Speed = speed,
                    Crouched = OwnerNetworkTransform.IsCrouched(avatar.gameObject) });
            }
        }

        void RouteEvents(List<GameEvent> events)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                var s = _slots[i];
                if (!s.Connected) continue;
                List<GameEvent> mine = null;
                foreach (var e in events)
                {
                    if (!e.IsVisibleTo(i)) continue;
                    if (mine == null) mine = new List<GameEvent>();
                    mine.Add(e);
                }
                if (mine == null) continue;
                if (s.Virtual) s.Bot?.OnEvents(mine);
                else _session.SendEvents(s.ClientId, mine);
            }
            foreach (var e in events) if (e.Audience == Audience.Host) MatchLog.Write("host-event " + e);
        }

        void SendViewsToAll(bool force)
        {
            if (_engine == null) return;
            double now = Now;
            for (int i = 0; i < SlotCount; i++) SendViewTo(i, force);
            _lastSentVersion = _engine.StateVersion;
            _lastViewSend = now;
            _lastHeartbeat = now;
        }

        void SendViewTo(int slot, bool force)
        {
            var s = _slots[slot];
            if (!s.Connected || _engine == null) return;
            PlayerView view;
            try { view = _engine.BuildView(slot, Now); }
            catch (Exception e) { Debug.LogException(e); return; }
            if (s.Virtual) { s.Bot?.OnView(view); return; }
            view.Me.ReconnectToken = s.Token;
            _session.SendView(s.ClientId, WireCodec.EncodeView(view));
        }

        public void OnClientRequest(ulong clientId, byte[] payload)
        {
            int slot;
            if (!_clientSlot.TryGetValue(clientId, out slot)) return;
            ActionRequest req;
            try { req = WireCodec.DecodeRequest(payload); }
            catch (Exception) { return; }
            var result = SubmitFor(slot, req);
            if (result != null) _session.SendResult(clientId, result);
        }

        public ActionResult SubmitFor(int slot, ActionRequest req)
        {
            if (_engine == null) return ActionResult.Fail(req.RequestId, FailureCode.WrongMatch);
            try
            {
                var r = _engine.Submit(slot, req, Now);
                if (r.Accepted && _engine.StateVersion != _lastSentVersion)
                {
                    // Push state promptly after accepted actions for responsiveness.
                    _lastViewSend = 0;
                }
                return r;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return ActionResult.Fail(req.RequestId, FailureCode.InvalidTarget);
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < SlotCount; i++) _slots[i].Bot?.Dispose();
        }
    }
}
