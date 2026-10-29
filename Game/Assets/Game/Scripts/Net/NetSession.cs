using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using Unity.Netcode;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Network transport for the rules contract. Spawned by the host; every message is a LineCodec payload.
    /// Clients only send requests (actions, ready flag). The host sends each client its own filtered view,
    /// its own events and results — never a shared "everything" snapshot.
    /// </summary>
    public sealed class NetSession : NetworkBehaviour
    {
        public static NetSession Instance { get; private set; }
        /// <summary>v3 host (null on clients).</summary>
        public HostLineController LineHost { get; private set; }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            // v3 client plumbing (the host's own client goes through the same path).
            LineClient.Transport = req => SubmitLineRpc(LineCodec.EncodeAction(req));
            LineClient.ReadyTransport = SetReady;
            LineClient.Clock = () => NetworkManager != null && NetworkManager.IsListening ? NetworkManager.ServerTime.Time : Time.realtimeSinceStartupAsDouble;
            if (IsServer)
            {
                LineHost = ConnectionManager.I.LineHost;
                LineHost?.Attach(this);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this)
            {
                Instance = null;
                LineClient.Transport = null;
                LineClient.ReadyTransport = null;
            }
        }

        // ---------------------------------------------------------------- client -> host

        public void SetReady(bool ready) { SetReadyRpc(ready); }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        void SetReadyRpc(bool ready, RpcParams rpcParams = default)
        {
            LineHost?.SetReady(rpcParams.Receive.SenderClientId, ready);
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        void SubmitLineRpc(byte[] payload, RpcParams rpcParams = default)
        {
            LineHost?.OnClientRequest(rpcParams.Receive.SenderClientId, payload);
        }

        // ---------------------------------------------------------------- host -> client(s)

        public void SendLineWelcome(ulong clientId, LineWelcome welcome, string configJson)
        {
            LineWelcomeRpc(LineCodec.EncodeWelcome(welcome), configJson ?? "", RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        public void BroadcastLineLobby(LineLobbyView lobby) { LineLobbyRpc(LineCodec.EncodeLobby(lobby)); }

        public void SendLineView(ulong clientId, byte[] payload) { LineViewRpc(payload, RpcTarget.Single(clientId, RpcTargetUse.Temp)); }

        public void SendLineEvents(ulong clientId, List<LineEvent> events)
        {
            if (events.Count == 0) return;
            LineEventsRpc(LineCodec.EncodeEvents(events), RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        public void SendLineResult(ulong clientId, LineActionResult result)
        {
            LineResultRpc(LineCodec.EncodeResult(result), RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void LineWelcomeRpc(byte[] payload, string configJson, RpcParams rpcParams)
        {
            Safe(() => LineClient.I.ApplyWelcome(LineCodec.DecodeWelcome(payload), configJson), "welcome");
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        void LineLobbyRpc(byte[] payload)
        {
            Safe(() => LineClient.I.ApplyLobby(LineCodec.DecodeLobby(payload)), "lobby");
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void LineViewRpc(byte[] payload, RpcParams rpcParams)
        {
            Safe(() => LineClient.I.ApplyView(LineCodec.DecodeView(payload)), "view");
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void LineEventsRpc(byte[] payload, RpcParams rpcParams)
        {
            Safe(() => LineClient.I.ApplyEvents(LineCodec.DecodeEvents(payload)), "events");
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void LineResultRpc(byte[] payload, RpcParams rpcParams)
        {
            Safe(() => LineClient.I.ApplyResult(LineCodec.DecodeResult(payload)), "result");
        }

        static void Safe(Action a, string what)
        {
            try { a(); }
            catch (FormatException e)
            {
                Debug.LogError("[Net] Incompatible " + what + " payload: " + e.Message);
                if (LineClient.I != null) LineClient.I.StatusKey = "net.incompatible";
            }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
