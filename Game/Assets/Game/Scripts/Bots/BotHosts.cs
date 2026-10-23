using System.Collections.Generic;
using BirimizKovulacak.Core;
using Unity.Netcode;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>Runs a BotBrain for this process's own networked player (launched with -bot).</summary>
    public sealed class NetworkBotDriver : MonoBehaviour
    {
        BotBrain _brain;

        void Update()
        {
            if (ClientState.I == null || ClientState.I.View == null) return;
            if (_brain == null)
                _brain = new BotBrain(ClientState.I, () => PlayerAvatar.Local, (LaunchArgs.Current.Seed + 1) * 7919 + ClientState.I.MySlot * 131, LaunchArgs.Current.BotAggression);
            _brain.Tick(Time.deltaTime);
        }
    }

    /// <summary>
    /// Host-side seat filler for testing with fewer than four humans: a server-owned avatar driven by a BotBrain
    /// that receives exactly the filtered view the engine builds for its slot.
    /// </summary>
    public sealed class VirtualBotChannel : IPlayerChannel
    {
        readonly HostMatchController _host;
        readonly BotBrain _brain;
        PlayerAvatar _avatar;
        uint _nextRequest = 1;

        public int Slot { get; }
        public PlayerView View { get; private set; }
        public MatchConfig Config => GameConfig.Match;
        public double Now => _host.Now;

        public VirtualBotChannel(HostMatchController host, int slot)
        {
            _host = host;
            Slot = slot;
            _brain = new BotBrain(this, () => _avatar, 1000 + slot * 37 + LaunchArgs.Current.Seed, LaunchArgs.Current.BotAggression);
        }

        public void SpawnAvatar(Vector3 position, float yaw)
        {
            var nm = NetworkManager.Singleton;
            var prefab = nm.NetworkConfig.PlayerPrefab;
            var go = Object.Instantiate(prefab, position + Vector3.up * 0.05f, Quaternion.Euler(0, yaw, 0));
            _avatar = go.GetComponent<PlayerAvatar>();
            _avatar.IsVirtualBot = true;
            go.GetComponent<NetworkObject>().Spawn(true);
            _avatar.ServerAssignSlot(Slot);
        }

        public void OnView(PlayerView v) { View = v; }
        public void OnEvents(List<GameEvent> events) { }

        public void Send(ActionVerb verb, string target = "", int param = -1)
        {
            if (View == null) return;
            _host.SubmitFor(Slot, new ActionRequest(View.MatchId, _nextRequest++, verb, target, param));
        }

        public void Update()
        {
            if (_avatar == null) return;
            _brain.Tick(Time.deltaTime);
        }

        public void Dispose()
        {
            if (_avatar != null && _avatar.IsSpawned) _avatar.NetworkObject.Despawn(true);
            _avatar = null;
        }
    }
}
