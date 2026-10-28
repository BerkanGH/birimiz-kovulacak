using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>Line of sight for the manager and witnesses: only opaque walls block (glass and desks do not).</summary>
    public sealed class UnityWorldQueries : IWorldQueries
    {
        // Partitions are ~1.3 m: eye-height rays pass over them, but they keep the rules consistent with what players see.
        public static int OccluderMask => (1 << Layers.Walls) | (1 << Layers.Partitions);

        public bool HasLineOfSight(Vec3 from, Vec3 to)
        {
            var a = new Vector3(from.X, from.Y, from.Z);
            var b = new Vector3(to.X, to.Y, to.Z);
            return !Physics.Linecast(a, b, OccluderMask, QueryTriggerInteraction.Ignore);
        }
    }

    public static class Layers
    {
        public const string WallsName = "Walls";
        public const string GlassName = "Glass";
        public const string FurnitureName = "Furniture";
        public const string AvatarName = "Avatar";
        /// <summary>v2: ~1.3 m cubicle partitions; hide crouched players from other players (not the manager/witness rules).</summary>
        public const string PartitionsName = "Partitions";
        public const int Walls = 8, Glass = 9, Furniture = 10, Avatar = 11, Partitions = 12;
    }
}
