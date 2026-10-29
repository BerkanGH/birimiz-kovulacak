using System;
using System.Collections;
using BirimizKovulacak.Core;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Create Lobby / Join Lobby, connection approval wiring, disconnect handling and the client-side
    /// reconnect loop (30 s grace by default, mirrored by the host's MatchEngine).
    /// </summary>
    public sealed class ConnectionManager : MonoBehaviour
    {
        public static ConnectionManager I { get; private set; }

        public NetworkObject SessionPrefab;
        /// <summary>v2 host (unused on the v3 branch; kept until v2 code is removed).</summary>
        public HostMatchController HostController { get; private set; }
        /// <summary>v3 host session (null on clients and offline).</summary>
        public HostLineController LineHost { get; private set; }

        public enum State { Offline, Hosting, Connecting, Connected, Reconnecting, Failed }
        public State Current { get; private set; } = State.Offline;
        public string LastErrorKey { get; private set; } = "";
        public string PlayerName { get; private set; } = "";
        public string Address { get; private set; } = "127.0.0.1";
        public ushort Port { get; private set; } = 7777;
        public float ReconnectRemaining { get; private set; }

        public event Action StateChanged;

        NetworkManager _nm;
        bool _intentionalShutdown;
        bool _freshConnect;
        Coroutine _reconnect;

        void Awake()
        {
            I = this;
            _nm = GetComponent<NetworkManager>();
        }

        void Start()
        {
            _nm.ConnectionApprovalCallback = ApprovalCheck;
            _nm.OnClientConnectedCallback += OnClientConnected;
            _nm.OnClientDisconnectCallback += OnClientDisconnect;
            _nm.OnTransportFailure += OnTransportFailure;
        }

        void OnDestroy()
        {
            if (_nm == null) return;
            _nm.OnClientConnectedCallback -= OnClientConnected;
            _nm.OnClientDisconnectCallback -= OnClientDisconnect;
            _nm.OnTransportFailure -= OnTransportFailure;
        }

        void SetState(State s, string errorKey = null)
        {
            Current = s;
            if (errorKey != null) LastErrorKey = errorKey;
            MatchLog.Write("conn " + s + (string.IsNullOrEmpty(errorKey) ? "" : " " + errorKey));
            StateChanged?.Invoke();
        }

        UnityTransport Transport => (UnityTransport)_nm.NetworkConfig.NetworkTransport;

        byte[] BuildPayload(string token)
        {
            return LineCodec.EncodeConnection(new LineConnectionPayload
            {
                ProtocolVersion = LineProtocol.Version,
                BuildId = GameConfig.BuildId,
                PlayerName = PlayerName,
                ReconnectToken = token ?? "",
                Language = Loc.Language,
            });
        }

        // ------------------------------------------------------------------ host

        public bool StartHost(string playerName, ushort port)
        {
            if (_nm.IsListening) return false;
            PlayerName = playerName;
            Port = port;
            LastErrorKey = "";
            ClientState.I.ResetSession();
            LineClient.I.ResetSession();
            LineHost = new HostLineController();
            Transport.SetConnectionData("0.0.0.0", port, "0.0.0.0");
            _nm.NetworkConfig.ConnectionData = BuildPayload("");
            bool ok;
            try { ok = _nm.StartHost(); }
            catch (Exception e) { Debug.LogException(e); ok = false; }
            if (!ok)
            {
                LineHost?.Dispose();
                LineHost = null;
                SetState(State.Failed, "net.host_failed");
                return false;
            }
            var session = Instantiate(SessionPrefab);
            session.Spawn(true);
            SetState(State.Hosting);
            return true;
        }

        // ------------------------------------------------------------------ client

        public bool StartClient(string playerName, string address, ushort port, string reconnectToken = "")
        {
            if (_nm.IsListening) return false;
            PlayerName = playerName;
            Address = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
            Port = port;
            LastErrorKey = "";
            ClientState.I.ResetSession();
            LineClient.I.ResetSession();
            return Connect(reconnectToken ?? "");
        }

        // ---- crash-safe rejoin: the last match's token is kept locally for a short time
        const string TokenKey = "reconnect.token", AddrKey = "reconnect.address", PortKey = "reconnect.port", TimeKey = "reconnect.time";

        public static void RememberSession(string token, string address, ushort port)
        {
            if (string.IsNullOrEmpty(token)) return;
            PlayerPrefs.SetString(TokenKey, token);
            PlayerPrefs.SetString(AddrKey, address ?? "");
            PlayerPrefs.SetInt(PortKey, port);
            PlayerPrefs.SetString(TimeKey, DateTime.UtcNow.Ticks.ToString());
        }

        public static void ForgetSession()
        {
            PlayerPrefs.DeleteKey(TokenKey);
            PlayerPrefs.DeleteKey(TimeKey);
        }

        /// <summary>A token from a match this machine dropped out of less than ~a minute ago, if any.</summary>
        public static bool TryGetRecentSession(out string token, out string address, out ushort port)
        {
            token = PlayerPrefs.GetString(TokenKey, "");
            address = PlayerPrefs.GetString(AddrKey, "");
            port = (ushort)PlayerPrefs.GetInt(PortKey, 7777);
            long ticks;
            if (string.IsNullOrEmpty(token) || !long.TryParse(PlayerPrefs.GetString(TimeKey, "0"), out ticks)) return false;
            return (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds < 75;
        }

        /// <summary>Test hook (-simulatedrop): drop the transport as if the network failed, optionally rejoin.</summary>
        public void SimulateNetworkDrop(bool rejoin, float downtime)
        {
            if (!_nm.IsConnectedClient || _nm.IsServer) return;
            string token = LineClient.I.ReconnectToken;
            MatchLog.Write("simulated network drop (rejoin=" + rejoin + ", downtime=" + downtime + "s)");
            SetState(State.Reconnecting, "net.reconnecting");
            if (rejoin)
            {
                _nm.Shutdown();
                _reconnect = StartCoroutine(ReconnectLoop(token, downtime));
            }
            else
            {
                _intentionalShutdown = true;
                _nm.Shutdown();
                StartCoroutine(StayOffline());
            }
        }

        IEnumerator StayOffline()
        {
            yield return new WaitForSecondsRealtime(1f);
            _intentionalShutdown = false;
            SetState(State.Failed, "net.host_lost");
        }

        bool Connect(string token)
        {
            HostController = null;
            LineHost = null;
            Transport.SetConnectionData(Address, Port);
            _nm.NetworkConfig.ConnectionData = BuildPayload(token);
            bool ok;
            try { ok = _nm.StartClient(); }
            catch (Exception e) { Debug.LogException(e); ok = false; }
            if (!ok)
            {
                SetState(State.Failed, "net.connect_failed");
                return false;
            }
            if (Current != State.Reconnecting) SetState(State.Connecting);
            return true;
        }

        public void Leave()
        {
            _intentionalShutdown = true;
            if (_reconnect != null) { StopCoroutine(_reconnect); _reconnect = null; }
            bool hosting = LineHost != null && _nm.IsServer;
            if (hosting)
            {
                // Tell clients the week is over before the transport goes away.
                LineHost.Abort();
                LineHost.Dispose();
                StartCoroutine(ShutdownAfter(0.4f));
            }
            else if (_nm.IsListening) _nm.Shutdown();
            HostController = null;
            LineHost = null;
            ClientState.I.ResetSession();
            LineClient.I.ResetSession();
            SetState(State.Offline, "");
            if (!hosting) _intentionalShutdown = false;
        }

        IEnumerator ShutdownAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (_nm.IsListening) _nm.Shutdown();
            _intentionalShutdown = false;
        }

        void Update()
        {
            if (LineHost != null && _nm.IsServer) LineHost.Update();
        }

        // ------------------------------------------------------------------ callbacks

        void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            if (LineHost == null)
            {
                response.Approved = false;
                response.Reason = "reject.not_host";
                return;
            }
            bool isHostClient = request.ClientNetworkId == NetworkManager.ServerClientId;
            var a = LineHost.Approve(request.ClientNetworkId, request.Payload, isHostClient);
            response.Approved = a.Approved;
            response.Reason = a.Reason ?? "";
            response.CreatePlayerObject = false; // v3: no avatars travel over the network (seated first person)
            response.Pending = false;
            MatchLog.Write("approval client=" + request.ClientNetworkId + " ok=" + a.Approved + " slot=" + a.Slot + " reason=" + a.Reason);
        }

        void OnClientConnected(ulong clientId)
        {
            if (_nm.IsServer) LineHost?.OnClientConnected(clientId);
            if (clientId == _nm.LocalClientId && !_nm.IsServer)
            {
                _freshConnect = true;
                SetState(State.Connected, "");
                ReconnectRemaining = 0;
            }
        }

        void OnClientDisconnect(ulong clientId)
        {
            if (_nm.IsServer)
            {
                if (clientId != NetworkManager.ServerClientId) LineHost?.OnClientDisconnected(clientId);
                return;
            }
            if (clientId != _nm.LocalClientId && clientId != NetworkManager.ServerClientId) return;
            if (_intentionalShutdown) return;

            string reason = _nm.DisconnectReason;
            var view = LineClient.I.View;
            bool midMatch = view != null && view.Phase != LinePhase.WeekResults && view.Phase != LinePhase.Abandoned;
            bool rejected = !string.IsNullOrEmpty(reason) && reason.StartsWith("reject.");
            MatchLog.Write("disconnected reason='" + reason + "' midMatch=" + midMatch);
            if (rejected)
            {
                SetState(State.Failed, reason);
                ClientState.I.ResetSession();
                LineClient.I.ResetSession();
                return;
            }
            if (Current == State.Reconnecting) return; // the reconnect loop handles retries
            if (midMatch && !string.IsNullOrEmpty(LineClient.I.ReconnectToken))
            {
                _reconnect = StartCoroutine(ReconnectLoop(LineClient.I.ReconnectToken));
                return;
            }
            SetState(State.Failed, Current == State.Connecting ? "net.connect_failed" : "net.host_lost");
            ClientState.I.ResetSession();
            LineClient.I.ResetSession();
        }

        void OnTransportFailure()
        {
            MatchLog.Write("transport failure");
            if (_nm.IsServer) SetState(State.Failed, "net.transport_failed");
        }

        IEnumerator ReconnectLoop(string token, float initialDelay = 0f)
        {
            const float grace = 30f;
            float deadline = Time.realtimeSinceStartup + grace;
            SetState(State.Reconnecting, "net.reconnecting");
            // The old connection may still look alive inside NGO when its disconnect callback fires: tear it down
            // completely and only accept a brand-new approved connection as success.
            yield return null;
            if (_nm.IsListening && !_nm.ShutdownInProgress) _nm.Shutdown();
            if (initialDelay > 0) yield return new WaitForSecondsRealtime(initialDelay);
            while (Time.realtimeSinceStartup < deadline)
            {
                ReconnectRemaining = deadline - Time.realtimeSinceStartup;
                while (_nm.ShutdownInProgress || _nm.IsListening)
                {
                    if (_nm.IsListening && !_nm.ShutdownInProgress) _nm.Shutdown();
                    yield return null;
                }
                _freshConnect = false;
                Connect(token);
                float attemptEnd = Time.realtimeSinceStartup + 4f;
                while (Time.realtimeSinceStartup < attemptEnd && !_freshConnect)
                {
                    ReconnectRemaining = deadline - Time.realtimeSinceStartup;
                    yield return null;
                }
                if (_freshConnect)
                {
                    _reconnect = null;
                    MatchLog.Write("reconnected");
                    SetState(State.Connected, "");
                    yield break;
                }
                if (_nm.IsListening) _nm.Shutdown();
                yield return new WaitForSecondsRealtime(0.5f);
            }
            _reconnect = null;
            ClientState.I.ResetSession();
            LineClient.I.ResetSession();
            SetState(State.Failed, "net.reconnect_failed");
        }

        void OnApplicationQuit()
        {
            _intentionalShutdown = true;
            LineHost?.Abort();
            MatchLog.Close();
        }
    }
}
