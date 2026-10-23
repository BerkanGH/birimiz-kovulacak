using System;

namespace BirimizKovulacak.Core
{
    /// <summary>Engine-independent vector in Unity world space (metres, y up).</summary>
    [Serializable]
    public struct Vec3 : IEquatable<Vec3>
    {
        public float X;
        public float Y;
        public float Z;

        public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }

        public static readonly Vec3 Zero = new Vec3(0f, 0f, 0f);

        /// <summary>Horizontal (XZ-plane) distance. All interaction ranges use this.</summary>
        public static float FlatDistance(Vec3 a, Vec3 b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        public static Vec3 operator +(Vec3 a, Vec3 b) { return new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static Vec3 operator -(Vec3 a, Vec3 b) { return new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static Vec3 operator *(Vec3 a, float s) { return new Vec3(a.X * s, a.Y * s, a.Z * s); }

        /// <summary>Unit direction on the XZ plane for a yaw in degrees (0 = +Z, 90 = +X), Unity convention.</summary>
        public static Vec3 FromYaw(float yawDegrees)
        {
            double r = yawDegrees * Math.PI / 180.0;
            return new Vec3((float)Math.Sin(r), 0f, (float)Math.Cos(r));
        }

        /// <summary>Yaw in degrees of the XZ direction from a to b (0 = +Z).</summary>
        public static float YawTo(Vec3 a, Vec3 b)
        {
            return (float)(Math.Atan2(b.X - a.X, b.Z - a.Z) * 180.0 / Math.PI);
        }

        /// <summary>Absolute smallest difference between two yaw angles, 0..180.</summary>
        public static float YawDelta(float a, float b)
        {
            float d = (a - b) % 360f;
            if (d < -180f) d += 360f;
            if (d > 180f) d -= 360f;
            return Math.Abs(d);
        }

        public bool Equals(Vec3 other) { return X == other.X && Y == other.Y && Z == other.Z; }
        public override bool Equals(object obj) { return obj is Vec3 && Equals((Vec3)obj); }
        public override int GetHashCode() { return X.GetHashCode() ^ (Y.GetHashCode() << 2) ^ (Z.GetHashCode() >> 2); }
        public override string ToString() { return "(" + X.ToString("0.##") + ", " + Y.ToString("0.##") + ", " + Z.ToString("0.##") + ")"; }
    }
}
