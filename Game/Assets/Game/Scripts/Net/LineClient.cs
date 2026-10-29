using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v3: the local player's side of the session — latest filtered <see cref="LineView"/>, lobby, events and the
    /// request plumbing. The host's own UI reads from here too, so it obeys the same information limits as every
    /// client. UI code only reads this and calls <see cref="Send"/>; the network adapter (or the UI mock) feeds it.
    /// </summary>
    public sealed class LineClient : MonoBehaviour
    {
        public static LineClient I { get; private set; }

        public int MySlot { get; private set; } = LineIds.None;
        public string ReconnectToken { get; private set; } = "";
        public LineView View { get; private set; }
        public LineLobbyView Lobby { get; private set; }
        public LineConfig HostConfig { get; private set; }
        public string StatusKey { get; set; } = "";

        /// <summary>(old, new) after every applied view.</summary>
        public event Action<LineView, LineView> ViewChanged;
        /// <summary>(old, new) phase; None when the week closes.</summary>
        public event Action<LinePhase, LinePhase> PhaseChanged;
        public event Action<LineLobbyView> LobbyChanged;
        public event Action<LineEvent> EventReceived;
        public event Action<LineActionResult, LineAction> ResultReceived;
        public event Action Welcomed;

        /// <summary>Set by the network adapter (NetSession) or the UI mock; receives every outgoing request.</summary>
        public static Action<LineAction> Transport;
        /// <summary>Host clock source (network time); the mock replaces it. Default: realtime.</summary>
        public static Func<double> Clock;
        /// <summary>Set by the network adapter; receives the lobby ready flag.</summary>
        public static Action<bool> ReadyTransport;

        public void SetReady(bool ready) { ReadyTransport?.Invoke(ready); }

        uint _nextRequestId;
        readonly Dictionary<uint, LineAction> _pending = new Dictionary<uint, LineAction>();

        public LineConfig Config => HostConfig ?? LineConfig.CreateDefault();
        public double Now => Clock != null ? Clock() : Time.realtimeSinceStartupAsDouble;
        public bool InWeek => View != null && View.Phase != LinePhase.None;
        public double PhaseRemaining => View == null ? 0 : Math.Max(0, View.PhaseEndTime - Now);

        void Awake()
        {
            I = this;
            _nextRequestId = (uint)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond % 1000000000L) + 1;
        }

        public void ResetSession()
        {
            MySlot = LineIds.None;
            View = null;
            Lobby = null;
            HostConfig = null;
            _pending.Clear();
        }

        public void ClearWeek()
        {
            var old = View;
            View = null;
            if (old != null) PhaseChanged?.Invoke(old.Phase, LinePhase.None);
        }

        // ---- inbound -------------------------------------------------------------------------------

        public void ApplyWelcome(LineWelcome w, string configJson)
        {
            MySlot = w.Slot;
            ReconnectToken = w.ReconnectToken;
            if (!string.IsNullOrEmpty(configJson))
            {
                try { HostConfig = Newtonsoft.Json.JsonConvert.DeserializeObject<LineConfig>(configJson); }
                catch (Exception e) { Debug.LogWarning("[LineClient] Bad host config: " + e.Message); }
            }
            MatchLog.Write("welcome slot=" + w.Slot + " reconnected=" + w.Reconnected + " week=" + w.WeekId);
            Welcomed?.Invoke();
        }

        /// <summary>Test/mock hook: set the local slot and config without a network welcome.</summary>
        public void SetIdentity(int slot, LineConfig config) { MySlot = slot; HostConfig = config; }

        public void ApplyLobby(LineLobbyView lobby)
        {
            Lobby = lobby;
            if (!lobby.WeekRunning && View != null)
            {
                MatchLog.Write("lobby: week closed by host");
                ClearWeek();
            }
            LobbyChanged?.Invoke(lobby);
        }

        public void ApplyView(LineView v)
        {
            if (v == null) return;
            var old = View;
            if (old != null && v.WeekId == old.WeekId && v.StateVersion < old.StateVersion) return; // out of order
            View = v;
            if (v.Phase == LinePhase.WeekResults && v.Result != null && (old == null || old.Phase != LinePhase.WeekResults))
                MatchLog.Write("results week=" + v.WeekId + " hash=" + v.Result.ResultHash + " fired=" + v.Result.Fired + " manager=" + v.Result.ManagerProfileId + " days=" + v.Result.Days);
            ViewChanged?.Invoke(old, v);
            LinePhase oldPhase = old == null || old.WeekId != v.WeekId ? LinePhase.None : old.Phase;
            if (oldPhase != v.Phase || (old != null && (old.Day != v.Day || old.RoundIndex != v.RoundIndex) && v.Phase == LinePhase.RoundIntro))
            {
                MatchLog.Write("phase " + oldPhase + " -> " + v.Phase + " day=" + v.Day + " round=" + v.RoundIndex + " week=" + v.WeekId);
                PhaseChanged?.Invoke(oldPhase, v.Phase);
            }
        }

        public void ApplyEvents(List<LineEvent> events)
        {
            foreach (var e in events)
            {
                MatchLog.Write("event " + e);
                EventReceived?.Invoke(e);
            }
        }

        public void ApplyResult(LineActionResult r)
        {
            LineAction req;
            _pending.TryGetValue(r.RequestId, out req);
            _pending.Remove(r.RequestId);
            if (!r.Accepted) MatchLog.Write("rejected " + (req != null ? req.ToString() : r.RequestId.ToString()) + " " + r.Failure);
            ResultReceived?.Invoke(r, req);
        }

        // ---- outbound ------------------------------------------------------------------------------

        public void Send(LineVerb verb, int target = LineIds.None, int a = -1, int b = -1)
        {
            if (View == null || Transport == null) return;
            var req = new LineAction(View.WeekId, _nextRequestId++, verb, target, a, b);
            _pending[req.RequestId] = req;
            if (_pending.Count > 64)
            {
                uint oldest = uint.MaxValue;
                foreach (var k in _pending.Keys) if (k < oldest) oldest = k;
                _pending.Remove(oldest);
            }
            MatchLog.Write("send " + req);
            Transport(req);
        }

        // ---- convenience ---------------------------------------------------------------------------

        public LinePlayerView Me => Player(MySlot);

        public LinePlayerView Player(int id)
        {
            if (View == null) return null;
            for (int i = 0; i < View.Players.Count; i++) if (View.Players[i].Id == id) return View.Players[i];
            return null;
        }

        /// <summary>Display name for any participant id (colleagues, intern, manager, system).</summary>
        public string NameOf(int id)
        {
            if (id == LineIds.None) return Loc.T("common.nobody");
            if (id == LineIds.Intern) return Loc.T("line.intern.name");
            if (id == LineIds.Manager) return Loc.ManagerName(View != null ? View.ManagerProfileId : "");
            if (id == LineIds.System) return Loc.T("line.system.name");
            var p = Player(id);
            return p != null ? p.Name : Loc.T("player.default." + id);
        }

        public int ColorOf(int id)
        {
            var p = Player(id);
            return p != null ? p.ColorIndex : id;
        }

        public static bool IsOfficePhase(LinePhase p)
        {
            return p == LinePhase.Morning || p == LinePhase.RoundIntro || p == LinePhase.RoundPlay || p == LinePhase.RoundReveal;
        }

        public static bool IsMeetingPhase(LinePhase p)
        {
            return p >= LinePhase.MeetingDossier && p <= LinePhase.FinalVerdict;
        }
    }
}
