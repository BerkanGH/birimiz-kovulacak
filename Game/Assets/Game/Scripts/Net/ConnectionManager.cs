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
        public HostMatchController HostController { get; private set; }

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
            return WireCodec.EncodeConnectionPayload(new ConnectionPayload
            {
                ProtocolVersion = Protocol.Version,
                BuildId = GameConfig.BuildId,
                PlayerName = PlayerName,
                ReconnectToken = token ?? "",
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
            HostController = new HostMatchController();
            Transport.SetConnectionData("0.0.0.0", port, "0.0.0.0");
            _nm.NetworkConfig.ConnectionData = BuildPayload("");
            bool ok;
            try { ok = _nm.StartHost(); }
            catch (Exception e) { Debug.LogException(e); ok = false; }
            if (!ok)
            {
                HostController = null;
                SetState(State.Failed, "net.host_failed");
                return false;
            }
            var session = Instantiate(SessionPrefab);
            session.Spawn(true);
            SetState(State.Hosting);
            return true;
        }

        // ------------------------------------------------------------------ client

        public bool StartClient(string playerName, string address, ushort port)
        {
            if (_nm.IsListening) return false;
            PlayerName = playerName;
            Address = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
            Port = port;
            LastErrorKey = "";
            ClientState.I.ResetSession();
            return Connect("");
        }

        bool Connect(string token)
        {
            HostController = null;
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
            bool hosting = HostController != null && _nm.IsServer;
            if (hosting)
            {
                // Tell clients the match is abandoned before the transport goes away.
                HostController.Abort();
                HostController.Dispose();
                StartCoroutine(ShutdownAfter(0.4f));
            }
            else if (_nm.IsListening) _nm.Shutdown();
            HostController = null;
            ClientState.I.ResetSession();
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
            if (HostController != null && _nm.IsServer) HostController.Update();
        }

        // ------------------------------------------------------------------ callbacks

        void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            if (HostController == null)
            {
                response.Approved = false;
                response.Reason = "reject.not_host";
                return;
            }
            bool isHostClient = request.ClientNetworkId == NetworkManager.ServerClientId;
            var a = HostController.Approve(request.ClientNetworkId, request.Payload, isHostClient);
            response.Approved = a.Approved;
            response.Reason = a.Reason ?? "";
            response.CreatePlayerObject = a.Approved;
            response.Position = a.Position;
            response.Rotation = a.Rotation;
            response.Pending = false;
            MatchLog.Write("approval client=" + request.ClientNetworkId + " ok=" + a.Approved + " slot=" + a.Slot + " reason=" + a.Reason);
        }

        void OnClientConnected(ulong clientId)
        {
            if (_nm.IsServer) HostController?.OnClientConnected(clientId);
            if (clientId == _nm.LocalClientId && !_nm.IsServer)
            {
                SetState(State.Connected, "");
                ReconnectRemaining = 0;
            }
        }

        void OnClientDisconnect(ulong clientId)
        {
            if (_nm.IsServer)
            {
                if (clientId != NetworkManager.ServerClientId) HostController?.OnClientDisconnected(clientId);
                return;
            }
            if (clientId != _nm.LocalClientId && clientId != NetworkManager.ServerClientId) return;
            if (_intentionalShutdown) return;

            string reason = _nm.DisconnectReason;
            var view = ClientState.I.View;
            bool midMatch = view != null && view.Phase != MatchPhase.Results && view.Phase != MatchPhase.Abandoned;
            bool rejected = !string.IsNullOrEmpty(reason) && reason.StartsWith("reject.");
            MatchLog.Write("disconnected reason='" + reason + "' midMatch=" + midMatch);
            if (rejected)
            {
                SetState(State.Failed, reason);
                ClientState.I.ResetSession();
                return;
            }
            if (Current == State.Reconnecting) return; // the reconnect loop handles retries
            if (midMatch && !string.IsNullOrEmpty(ClientState.I.ReconnectToken))
            {
                _reconnect = StartCoroutine(ReconnectLoop(ClientState.I.ReconnectToken));
                return;
            }
            SetState(State.Failed, Current == State.Connecting ? "net.connect_failed" : "net.host_lost");
            ClientState.I.ResetSession();
        }

        void OnTransportFailure()
        {
            MatchLog.Write("transport failure");
            if (_nm.IsServer) SetState(State.Failed, "net.transport_failed");
        }

        IEnumerator ReconnectLoop(string token)
        {
            float grace = (ClientState.I.Config != null ? ClientState.I.Config.Reconnect.GraceSeconds : 30f);
            float deadline = Time.realtimeSinceStartup + grace;
            SetState(State.Reconnecting, "net.reconnecting");
            while (Time.realtimeSinceStartup < deadline)
            {
                ReconnectRemaining = deadline - Time.realtimeSinceStartup;
                while (_nm.ShutdownInProgress) yield return null;
                if (!_nm.IsListening) Connect(token);
                float attemptEnd = Time.realtimeSinceStartup + 4f;
                while (Time.realtimeSinceStartup < attemptEnd && !(_nm.IsConnectedClient))
                {
                    ReconnectRemaining = deadline - Time.realtimeSinceStartup;
                    yield return null;
                }
                if (_nm.IsConnectedClient)
                {
                    _reconnect = null;
                    SetState(State.Connected, "");
                    yield break;
                }
                if (_nm.IsListening) _nm.Shutdown();
                yield return new WaitForSecondsRealtime(0.5f);
            }
            _reconnect = null;
            ClientState.I.ResetSession();
            SetState(State.Failed, "net.reconnect_failed");
        }

        void OnApplicationQuit()
        {
            _intentionalShutdown = true;
            HostController?.Abort();
            MatchLog.Close();
        }
    }
}
