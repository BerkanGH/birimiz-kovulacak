using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5: the local player's side of the session — latest filtered <see cref="LineView"/>, lobby, events and the
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
        /// <summary>New messages appended to <see cref="Messages"/> (texts, email, meeting chat, system/manager notices).</summary>
        public event Action<IReadOnlyList<LineMessage>> MessagesReceived;

        /// <summary>Set by the network adapter (NetSession) or the UI mock; receives every outgoing request.</summary>
        public static Action<LineAction> Transport;
        /// <summary>Host clock source (network time); the mock replaces it. Default: realtime.</summary>
        public static Func<double> Clock;
        /// <summary>Set by the network adapter; receives the lobby ready flag.</summary>
        public static Action<bool> ReadyTransport;
        /// <summary>Set by the network adapter: ask the host to resend my messages after this sequence number.</summary>
        public static Action<long> MessageResyncTransport;
        /// <summary>Set by the network adapter: "Play again" intent on the results screen.</summary>
        public static Action<bool> RematchTransport;
        /// <summary>Set by the network adapter: my analytics consent (0..3) and pseudonymous id changed.</summary>
        public static Action<int, string> TelemetryConsentTransport;

        /// <summary>Tell the host my analytics consent changed (telemetry track calls this; no-op offline).</summary>
        public static void SendTelemetryConsent(int consent, string anonymousId) { TelemetryConsentTransport?.Invoke(consent, anonymousId ?? ""); }

        public void SetReady(bool ready) { ReadyTransport?.Invoke(ready); }

        uint _nextRequestId;
        float _nextRemember;
        readonly Dictionary<uint, LineAction> _pending = new Dictionary<uint, LineAction>();
        readonly List<LineMessage> _messages = new List<LineMessage>();
        int _messagesWeek = -1;
        float _gapSince = -1f;

        /// <summary>My messages of the current match, oldest first (player text is untrusted: render with rich text off).</summary>
        public IReadOnlyList<LineMessage> Messages => _messages;
        public long LastMessageSeq => _messages.Count > 0 ? _messages[_messages.Count - 1].Seq : 0;

        public void RequestRematch(bool want) { RematchTransport?.Invoke(want); }

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
            _messages.Clear();
            _messagesWeek = -1;
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
            var cm = ConnectionManager.I;
            if (cm != null && cm.LineHost == null && !string.IsNullOrEmpty(w.ReconnectToken)) ConnectionManager.RememberSession(w.ReconnectToken, cm.Address, cm.Port);
            if (!string.IsNullOrEmpty(configJson))
            {
                try { HostConfig = Newtonsoft.Json.JsonConvert.DeserializeObject<LineConfig>(configJson, GameConfig.JsonSettings); }
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
            if (!lobby.WeekRunning && ConnectionManager.I != null && ConnectionManager.I.LineHost == null) ConnectionManager.ForgetSession();
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
            if (v.WeekId != _messagesWeek) { _messages.Clear(); _messagesWeek = v.WeekId; _gapSince = -1f; }
            CheckMessageGap(v);
            var cm = ConnectionManager.I;
            if (cm != null && cm.LineHost == null && !string.IsNullOrEmpty(ReconnectToken) && Time.unscaledTime > _nextRemember)
            {
                // The saved rejoin credentials expire after a short time: refresh them while the week is alive.
                _nextRemember = Time.unscaledTime + 5f;
                ConnectionManager.RememberSession(ReconnectToken, cm.Address, cm.Port);
            }
            if (v.Phase == LinePhase.Results && v.Results != null && (old == null || old.Phase != LinePhase.Results))
                MatchLog.Write(ResultLine(v.Results));
            ViewChanged?.Invoke(old, v);
            bool sameWeek = old != null && old.WeekId == v.WeekId;
            LinePhase oldPhase = sameWeek ? old.Phase : LinePhase.None;
            // A phase can follow itself (final revote: FinalVote -> FinalVote): a new start time is a new phase too.
            if (oldPhase != v.Phase || (sameWeek && old.PhaseStartTime != v.PhaseStartTime))
            {
                MatchLog.Write("phase " + oldPhase + " -> " + v.Phase + " day=" + v.Day + " task=" + v.TaskNumber + " week=" + v.WeekId);
                PhaseChanged?.Invoke(oldPhase, v.Phase);
            }
        }

        /// <summary>Messages pushed by the host (in order); duplicates and gaps are handled here.</summary>
        public void ApplyMessages(int weekId, List<LineMessage> batch)
        {
            if (batch == null || batch.Count == 0) return;
            if (weekId != _messagesWeek) { _messages.Clear(); _messagesWeek = weekId; }
            List<LineMessage> added = null;
            foreach (var m in batch)
            {
                if (m == null || m.Seq <= LastMessageSeq) continue; // duplicate or replayed after a resync
                _messages.Add(m);
                (added ?? (added = new List<LineMessage>())).Add(m);
            }
            if (added == null) return;
            _gapSince = -1f;
            MessagesReceived?.Invoke(added);
        }

        void CheckMessageGap(LineView v)
        {
            if (v.Mine == null || v.Mine.MessageSeq <= LastMessageSeq) { _gapSince = -1f; return; }
            // The view says more messages exist than arrived: after a short grace (in-flight RPCs), ask for a resend.
            if (_gapSince < 0f) { _gapSince = Time.unscaledTime; return; }
            if (Time.unscaledTime - _gapSince > 2f)
            {
                _gapSince = Time.unscaledTime;
                MessageResyncTransport?.Invoke(LastMessageSeq);
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

        public void Send(LineVerb verb, int target = LineIds.None, int a = -1, int b = -1, int c = -1, string text = "")
        {
            if (View == null || Transport == null) return;
            var req = new LineAction(View.WeekId, _nextRequestId++, verb, target, a, b, c, text);
            _pending[req.RequestId] = req;
            if (_pending.Count > 64)
            {
                uint oldest = uint.MaxValue;
                foreach (var k in _pending.Keys) if (k < oldest) oldest = k;
                _pending.Remove(oldest);
            }
            MatchLog.Write("send " + req); // ToString never includes the text
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

        /// <summary>
        /// The results line parsed by Tools/scripts/verify_logs.py (every process must log the same hash):
        /// "results week=W hash=H fired=a,b winners=c players=N".
        /// </summary>
        public static string ResultLine(LineWeekResult r)
        {
            return "results week=" + r.WeekId + " hash=" + r.ResultHash + " fired=" + string.Join(",", r.Fired) + " winners=" + string.Join(",", r.Winners)
                + " players=" + r.PlayerCount;
        }

        /// <summary>Display name for any participant id (colleagues, system).</summary>
        public string NameOf(int id)
        {
            if (id == LineIds.None) return Loc.T("common.nobody");
            if (id == LineIds.System) return Loc.T("line.system.name");
            var p = Player(id);
            return p != null ? p.Name : Loc.T("player.default." + id);
        }

        public int ColorOf(int id)
        {
            var p = Player(id);
            return p != null ? p.ColorIndex : id;
        }

        /// <summary>v5 calls phases: 1:1 phone calls only, the player sits at the office desk.</summary>
        public static bool IsCallsPhase(LinePhase p)
        {
            return p == LinePhase.Intro || p == LinePhase.TaskPlay || p == LinePhase.TaskReveal || p == LinePhase.Break || p == LinePhase.CrisisCalls;
        }

        /// <summary>Meeting room phases (everyone hears everyone): meetings, crisis vote/verdict and the final.</summary>
        public static bool IsMeetingPhase(LinePhase p)
        {
            return p >= LinePhase.MeetingDossier && p <= LinePhase.FinalVerdict && p != LinePhase.CrisisCalls;
        }
    }
}
