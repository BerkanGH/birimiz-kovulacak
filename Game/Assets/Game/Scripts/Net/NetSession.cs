using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using Unity.Netcode;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Network transport for the rules contract. Spawned by the host; every message is a WireCodec payload.
    /// Clients only send requests (actions, ready flag). The host sends each client its own filtered view,
    /// its own events and results — never a shared "everything" snapshot.
    /// </summary>
    public sealed class NetSession : NetworkBehaviour
    {
        public static NetSession Instance { get; private set; }
        public HostMatchController Host { get; private set; }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (IsServer)
            {
                Host = ConnectionManager.I.HostController;
                Host?.Attach(this);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        // ---------------------------------------------------------------- client -> host

        public void SubmitAction(ActionRequest req)
        {
            SubmitActionRpc(WireCodec.EncodeRequest(req));
        }

        public void SetReady(bool ready) { SetReadyRpc(ready); }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        void SubmitActionRpc(byte[] payload, RpcParams rpcParams = default)
        {
            Host?.OnClientRequest(rpcParams.Receive.SenderClientId, payload);
        }

        [Rpc(SendTo.Server, RequireOwnership = false)]
        void SetReadyRpc(bool ready, RpcParams rpcParams = default)
        {
            Host?.SetReady(rpcParams.Receive.SenderClientId, ready);
        }

        // ---------------------------------------------------------------- host -> client(s)

        public void SendWelcome(ulong clientId, WelcomeMessage welcome, string configJson)
        {
            WelcomeRpc(WireCodec.EncodeWelcome(welcome), configJson ?? "", RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        public void BroadcastLobby(LobbyView lobby)
        {
            LobbyRpc(WireCodec.EncodeLobby(lobby));
        }

        public void SendView(ulong clientId, byte[] payload)
        {
            ViewRpc(payload, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        public void SendEvents(ulong clientId, List<GameEvent> events)
        {
            if (events.Count == 0) return;
            EventsRpc(WireCodec.EncodeEvents(events), RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        public void SendResult(ulong clientId, ActionResult result)
        {
            ResultRpc(WireCodec.EncodeResult(result), RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void WelcomeRpc(byte[] payload, string configJson, RpcParams rpcParams)
        {
            Safe(() => ClientState.I.ApplyWelcome(WireCodec.DecodeWelcome(payload), configJson), "welcome");
        }

        [Rpc(SendTo.ClientsAndHost)]
        void LobbyRpc(byte[] payload)
        {
            Safe(() => ClientState.I.ApplyLobby(WireCodec.DecodeLobby(payload)), "lobby");
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void ViewRpc(byte[] payload, RpcParams rpcParams)
        {
            Safe(() => ClientState.I.ApplyView(WireCodec.DecodeView(payload)), "view");
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void EventsRpc(byte[] payload, RpcParams rpcParams)
        {
            Safe(() => ClientState.I.ApplyEvents(WireCodec.DecodeEvents(payload)), "events");
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void ResultRpc(byte[] payload, RpcParams rpcParams)
        {
            Safe(() => ClientState.I.ApplyResult(WireCodec.DecodeResult(payload)), "result");
        }

        static void Safe(Action a, string what)
        {
            try { a(); }
            catch (FormatException e)
            {
                Debug.LogError("[Net] Incompatible " + what + " payload: " + e.Message);
                ClientState.I.StatusKey = "net.incompatible";
            }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
