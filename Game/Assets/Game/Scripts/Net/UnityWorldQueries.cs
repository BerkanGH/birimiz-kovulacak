using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>Line of sight for the manager and witnesses: only opaque walls block (glass and desks do not).</summary>
    public sealed class UnityWorldQueries : IWorldQueries
    {
        public static int OccluderMask => LayerMask.GetMask(Layers.WallsName);

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
        public const int Walls = 8, Glass = 9, Furniture = 10, Avatar = 11;
    }
}
