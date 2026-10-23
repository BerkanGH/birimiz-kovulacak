using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using Unity.Netcode;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>How a controller (human input, network bot, host-side virtual bot) talks to the rules.</summary>
    public interface IPlayerChannel
    {
        int Slot { get; }
        PlayerView View { get; }
        MatchConfig Config { get; }
        double Now { get; }
        void Send(ActionVerb verb, string target = "", int param = -1);
    }

    /// <summary>
    /// The local client's view of the session: latest filtered snapshot, lobby, events and request plumbing.
    /// The host's own UI reads from here too, so it obeys the same information limits as every client.
    /// </summary>
    public sealed class ClientState : MonoBehaviour, IPlayerChannel
    {
        public static ClientState I { get; private set; }

        public int MySlot { get; private set; } = PlayerIds.None;
        public string ReconnectToken { get; private set; } = "";
        public PlayerView View { get; private set; }
        public LobbyView Lobby { get; private set; }
        public MatchConfig HostConfig { get; private set; }
        public string StatusKey { get; set; } = "";

        public event Action<PlayerView, PlayerView> ViewChanged;   // (old, new)
        public event Action<MatchPhase, MatchPhase> PhaseChanged; // (old, new)
        public event Action<LobbyView> LobbyChanged;
        public event Action<GameEvent> EventReceived;
        public event Action<ActionResult, ActionRequest> ResultReceived;
        public event Action Welcomed;

        uint _nextRequestId;
        int _loggedResultMatch = -1, _loggedVerdictMatch = -1;
        readonly Dictionary<uint, ActionRequest> _pending = new Dictionary<uint, ActionRequest>();

        int IPlayerChannel.Slot => MySlot;
        public MatchConfig Config => HostConfig ?? GameConfig.Match;

        public double Now
        {
            get
            {
                var nm = NetworkManager.Singleton;
                if (nm != null && nm.IsListening) return nm.ServerTime.Time;
                return Time.realtimeSinceStartupAsDouble;
            }
        }

        public bool InMatch => View != null && View.Phase != MatchPhase.None;

        void Awake()
        {
            I = this;
            // Start request ids from a time-derived base so a restarted client never collides with old ids.
            _nextRequestId = (uint)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond % 1000000000L) + 1;
        }

        public void ResetSession()
        {
            MySlot = PlayerIds.None;
            View = null;
            Lobby = null;
            HostConfig = null;
            _pending.Clear();
            MatchClock.MatchStart = 0;
        }

        public void ClearMatch()
        {
            var old = View;
            View = null;
            if (old != null) PhaseChanged?.Invoke(old.Phase, MatchPhase.None);
        }

        // ---- Inbound (called by NetSession) ---------------------------------------------------------

        public void ApplyWelcome(WelcomeMessage w, string configJson)
        {
            MySlot = w.Slot;
            ReconnectToken = w.ReconnectToken;
            if (!string.IsNullOrEmpty(configJson))
            {
                try { HostConfig = Newtonsoft.Json.JsonConvert.DeserializeObject<MatchConfig>(configJson, GameConfig.JsonSettings); }
                catch (Exception e) { Debug.LogWarning("[Client] Bad host config: " + e.Message); }
            }
            MatchLog.Write("welcome slot=" + w.Slot + " reconnected=" + w.Reconnected + " match=" + w.MatchId);
            Welcomed?.Invoke();
        }

        public void ApplyLobby(LobbyView lobby)
        {
            Lobby = lobby;
            LobbyChanged?.Invoke(lobby);
        }

        public void ApplyView(PlayerView v)
        {
            if (v == null) return;
            var old = View;
            if (old != null && v.MatchId == old.MatchId && v.StateVersion < old.StateVersion) return; // out of order
            View = v;
            if (!string.IsNullOrEmpty(v.Me.ReconnectToken)) ReconnectToken = v.Me.ReconnectToken;
            if (old == null || old.MatchId != v.MatchId)
                MatchClock.MatchStart = v.Phase == MatchPhase.Briefing ? v.PhaseStartTime : (old == null ? v.PhaseStartTime : MatchClock.MatchStart);
            if (v.Phase == MatchPhase.Results && v.Result != null && _loggedResultMatch != v.MatchId)
            {
                _loggedResultMatch = v.MatchId;
                var r = v.Result;
                MatchLog.Write("results match=" + v.MatchId + " hash=" + r.ResultHash + " fired=" + r.FiredPlayer + " company=" + r.CompanySucceeded +
                               " quota=" + r.QuotaPoints + "/" + r.QuotaTarget + " informant=" + r.InformantPlayer + " informantWon=" + r.InformantWon +
                               " incidents=" + r.Incidents.Count + " winners=" + string.Join(",", r.Players.FindAll(p => p.Won).ConvertAll(p => p.Player.ToString())));
                foreach (var i in r.Incidents)
                    MatchLog.Write("incident " + i.Id + " " + i.Type + " actor=" + i.Actor + " victim=" + i.Victim + " desk=" + i.DeskIndex + " item=" + i.ItemId + " blamed=" + i.BlamedPlayer + " discovered=" + i.Discovered + " reversedBy=" + i.ReversedBy + " misconduct=" + i.Misconduct);
            }
            if (v.Phase == MatchPhase.MeetingVerdict && v.Meeting.Verdict != null && _loggedVerdictMatch != v.MatchId)
            {
                _loggedVerdictMatch = v.MatchId;
                MatchLog.Write("verdict match=" + v.MatchId + " fired=" + v.Meeting.Verdict.FiredPlayer + " shortlist=" + string.Join(",", v.Meeting.Shortlist) + " tiebreak=" + v.Meeting.Verdict.TieBreakUsed);
            }
            ViewChanged?.Invoke(old, v);
            MatchPhase oldPhase = old == null || old.MatchId != v.MatchId ? MatchPhase.None : old.Phase;
            if (oldPhase != v.Phase)
            {
                MatchLog.Write("phase " + oldPhase + " -> " + v.Phase + " match=" + v.MatchId);
                PhaseChanged?.Invoke(oldPhase, v.Phase);
            }
        }

        public void ApplyEvents(List<GameEvent> events)
        {
            foreach (var e in events)
            {
                MatchLog.Event(e);
                EventReceived?.Invoke(e);
            }
        }

        public void ApplyResult(ActionResult r)
        {
            ActionRequest req;
            _pending.TryGetValue(r.RequestId, out req);
            _pending.Remove(r.RequestId);
            if (!r.Accepted) MatchLog.Write("rejected " + (req != null ? req.ToString() : r.RequestId.ToString()) + " " + r.Failure);
            ResultReceived?.Invoke(r, req);
        }

        // ---- Outbound -------------------------------------------------------------------------------

        public void Send(ActionVerb verb, string target = "", int param = -1)
        {
            if (View == null || NetSession.Instance == null) return;
            var req = new ActionRequest(View.MatchId, _nextRequestId++, verb, target, param);
            _pending[req.RequestId] = req;
            if (_pending.Count > 64)
            {
                uint oldest = uint.MaxValue;
                foreach (var k in _pending.Keys) if (k < oldest) oldest = k;
                _pending.Remove(oldest);
            }
            MatchLog.Write("send " + req);
            NetSession.Instance.SubmitAction(req);
        }

        // ---- Convenience ----------------------------------------------------------------------------

        public PlayerPublicView Me => PlayerById(MySlot);

        public PlayerPublicView PlayerById(int id)
        {
            if (View == null) return null;
            for (int i = 0; i < View.Players.Count; i++) if (View.Players[i].Id == id) return View.Players[i];
            return null;
        }

        public double PhaseRemaining => View == null ? 0 : Math.Max(0, View.PhaseEndTime - Now);
    }
}
