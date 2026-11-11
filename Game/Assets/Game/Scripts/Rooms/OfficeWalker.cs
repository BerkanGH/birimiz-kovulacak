using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 walking inside the private office (local only: nobody else sees your office, so nothing goes over the
    /// network). A circle of <see cref="OfficeRoom.WalkRadius"/> moves on the floor plane (office space x/z) with a
    /// little acceleration, slides along the walls and the furniture footprints from the layout, and stays within
    /// the phone cord's reach while the handset is up. No physics engine: a few push-out passes per frame.
    /// </summary>
    public sealed class OfficeWalker
    {
        public float Speed = 2.0f;
        /// <summary>1/s: how fast the velocity follows the input (start/stop feel).</summary>
        public float Response = 11f;
        /// <summary>Phone cord reach from the phone base while the handset is up (m).</summary>
        public float CordReach = 1.15f;

        public Vector2 Position;
        public Vector2 Velocity;
        /// <summary>Distance walked (m), for footsteps and the head-bob phase.</summary>
        public float Travelled;

        public void Place(Vector2 p, OfficeRoom room)
        {
            Position = p;
            Velocity = Vector2.zero;
            Position = Resolve(Position, room, true);
        }

        /// <summary>
        /// One frame. <paramref name="input"/> = (strafe, forward) in -1..1, <paramref name="yawDeg"/> = the view yaw
        /// (office space, clockwise from +z). <paramref name="tether"/>: the phone base (x, z) when the handset is up.
        /// </summary>
        public void Step(Vector2 input, float yawDeg, float dt, OfficeRoom room, Vector2? tether)
        {
            if (input.sqrMagnitude > 1f) input.Normalize();
            float yaw = yawDeg * Mathf.Deg2Rad;
            var fwd = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
            var right = new Vector2(fwd.y, -fwd.x);
            Vector2 want = (right * input.x + fwd * input.y) * Speed;
            Velocity = Vector2.Lerp(Velocity, want, 1f - Mathf.Exp(-Response * dt));
            if (want.sqrMagnitude < 1e-4f && Velocity.sqrMagnitude < 0.0004f) Velocity = Vector2.zero;
            Vector2 before = Position;
            Vector2 p = Position + Velocity * dt;
            p = Resolve(p, room, true);
            if (tether.HasValue)
            {
                Vector2 d = p - tether.Value;
                if (d.magnitude > CordReach) p = tether.Value + d.normalized * CordReach;
                p = Resolve(p, room, true);
            }
            Position = p;
            float moved = (Position - before).magnitude;
            Travelled += moved;
            // hitting a wall kills the velocity into it (no sliding build-up)
            if (dt > 0f) Velocity = Vector2.Lerp(Velocity, (Position - before) / dt, 0.5f);
        }

        /// <summary>Pushes a body circle out of the walls, the furniture and (optionally) the chair.</summary>
        public static Vector2 Resolve(Vector2 p, OfficeRoom room, bool chairSolid)
        {
            if (room == null) return p;
            float r = room.WalkRadius;
            for (int pass = 0; pass < 4; pass++)
            {
                bool moved = false;
                for (int i = 0; i < room.WalkRects.Length; i++)
                {
                    var rc = room.WalkRects[i];
                    float yaw = i < room.WalkRectYaw.Length ? room.WalkRectYaw[i] : 0f;
                    moved |= PushOutOfRect(ref p, r, new Vector2(rc.x, rc.y), new Vector2(rc.z, rc.w), yaw);
                }
                for (int i = 0; i < room.WalkCircles.Length; i++)
                {
                    var c = room.WalkCircles[i];
                    moved |= PushOutOfCircle(ref p, r, new Vector2(c.x, c.y), c.z);
                }
                if (chairSolid)
                {
                    // where the chair ends up while you stand (not where it is mid-roll, or standing up would shove you)
                    var chair = room.ChairBack;
                    moved |= PushOutOfCircle(ref p, r, new Vector2(chair.x, chair.z), room.ChairRadius);
                }
                // walls last: they always win
                float x0 = room.RoomMin.x + r, x1 = room.RoomMax.x - r, z0 = room.RoomMin.y + r, z1 = room.RoomMax.y - r;
                var clamped = new Vector2(Mathf.Clamp(p.x, x0, x1), Mathf.Clamp(p.y, z0, z1));
                if (clamped != p) { p = clamped; moved = true; }
                if (!moved) break;
            }
            return p;
        }

        static bool PushOutOfCircle(ref Vector2 p, float r, Vector2 c, float cr)
        {
            Vector2 d = p - c;
            float min = r + cr;
            float m = d.sqrMagnitude;
            if (m >= min * min) return false;
            float len = Mathf.Sqrt(m);
            Vector2 n = len > 1e-5f ? d / len : Vector2.up;
            p = c + n * min;
            return true;
        }

        /// <summary>Circle vs rotated rectangle (yaw clockwise from above, like the layout).</summary>
        static bool PushOutOfRect(ref Vector2 p, float r, Vector2 c, Vector2 half, float yawDeg)
        {
            float a = yawDeg * Mathf.Deg2Rad;
            float cs = Mathf.Cos(a), sn = Mathf.Sin(a);
            // world -> rect local (undo the clockwise yaw)
            Vector2 d = p - c;
            var l = new Vector2(d.x * cs - d.y * sn, d.x * sn + d.y * cs);
            var q = new Vector2(Mathf.Clamp(l.x, -half.x, half.x), Mathf.Clamp(l.y, -half.y, half.y));
            Vector2 diff = l - q;
            float m = diff.sqrMagnitude;
            if (m >= r * r) return false;
            Vector2 outL;
            if (m > 1e-8f)
            {
                float len = Mathf.Sqrt(m);
                outL = q + diff / len * r;
            }
            else
            {
                // centre inside the rectangle: leave through the nearest side
                float dx = half.x - Mathf.Abs(l.x), dz = half.y - Mathf.Abs(l.y);
                outL = dx < dz ? new Vector2(Mathf.Sign(l.x) * (half.x + r), l.y) : new Vector2(l.x, Mathf.Sign(l.y) * (half.y + r));
            }
            // rect local -> world
            p = c + new Vector2(outL.x * cs + outL.y * sn, -outL.x * sn + outL.y * cs);
            return true;
        }
    }
}
