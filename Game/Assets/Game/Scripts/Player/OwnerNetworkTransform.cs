using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Owner-authoritative transform: each client moves its own avatar; everything else is host-validated.
    /// v2: also carries the owner's crouch (sneak) flag, so the host can fill <c>AvatarSample.Crouched</c> and other
    /// peers can pose the avatar without waiting for the next filtered view.
    /// </summary>
    public sealed class OwnerNetworkTransform : NetworkTransform
    {
        /// <summary>Owner-written sneak flag (1 bit, replicated at tick rate only when it changes).</summary>
        public readonly NetworkVariable<bool> CrouchedVar =
            new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        protected override bool OnIsServerAuthoritative() { return false; }

        public bool Crouched => IsSpawned && CrouchedVar.Value;

        /// <summary>Owner only (local player, network bot, or the host for virtual bots). No-op otherwise.</summary>
        public void SetCrouched(bool crouched)
        {
            if (!IsSpawned || !IsOwner || CrouchedVar.Value == crouched) return;
            CrouchedVar.Value = crouched;
        }

        /// <summary>Allocation-free lookup used by the host's avatar sampling and by presentation code.</summary>
        public static bool IsCrouched(GameObject avatar)
        {
            OwnerNetworkTransform t;
            return avatar != null && avatar.TryGetComponent(out t) && t.Crouched;
        }

        public static void SetCrouched(GameObject avatar, bool crouched)
        {
            OwnerNetworkTransform t;
            if (avatar != null && avatar.TryGetComponent(out t)) t.SetCrouched(crouched);
        }
    }
}
