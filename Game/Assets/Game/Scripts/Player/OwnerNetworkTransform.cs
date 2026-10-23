using Unity.Netcode.Components;

namespace BirimizKovulacak.Game
{
    /// <summary>Owner-authoritative transform: each client moves its own avatar; everything else is host-validated.</summary>
    public sealed class OwnerNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative() { return false; }
    }
}
