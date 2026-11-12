using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 step 3a shared gaze (docs/V5_INTERACTION.md §3, [KARAR] 4): in the meeting room each player's look direction
    /// (yaw/pitch relative to their seat, whole degrees) goes to the host at 8 Hz (unreliable, only when it changed or
    /// once a second); the host adds its bots' gaze (<see cref="BotGazer"/>) and sends everyone one small packet at the
    /// same rate. Each client turns the colleagues' heads toward it (<see cref="MeetingRoom.SetGaze"/>). Presentation
    /// only: nothing here reaches the rules engine, logs or analytics.
    /// </summary>
    public static class MeetingGaze
    {
        public const float Interval = 0.125f;
        const int Slots = 8;
        static readonly float[] _yaw = new float[Slots], _pitch = new float[Slots], _until = new float[Slots];

        /// <summary>Client → host (set by NetSession): my yaw, pitch.</summary>
        public static Action<sbyte, sbyte> Transport;
        /// <summary>The UI holds something up (folder, ballot paper): others see you looking down at it.</summary>
        public static bool LookingDown;

        static float _next, _lastSent = -10f;
        static int _sentYaw = 999, _sentPitch = 999;
        static BotGazer _self;

        /// <summary>Fresh gaze of a colleague (degrees), false when none arrived lately.</summary>
        public static bool TryGet(int slot, out float yaw, out float pitch)
        {
            yaw = pitch = 0f;
            if (slot < 0 || slot >= Slots || Time.unscaledTime > _until[slot]) return false;
            yaw = _yaw[slot]; pitch = _pitch[slot];
            return true;
        }

        /// <summary>Stores a gaze (network packet, or a UI mock with a long hold).</summary>
        public static void Set(int slot, float yaw, float pitch, float hold = 1.5f)
        {
            if (slot < 0 || slot >= Slots) return;
            _yaw[slot] = yaw; _pitch[slot] = pitch; _until[slot] = Time.unscaledTime + hold;
        }

        public static void Clear() { for (int i = 0; i < Slots; i++) _until[i] = 0f; _self = null; }

        // ------------------------------------------------------------------ packets: [n] then n x [slot, yaw, pitch]

        public static byte[] Pack(List<int> slots, List<sbyte> yaw, List<sbyte> pitch)
        {
            var b = new byte[1 + slots.Count * 3];
            b[0] = (byte)slots.Count;
            for (int i = 0; i < slots.Count; i++)
            {
                b[1 + i * 3] = (byte)slots[i];
                b[2 + i * 3] = unchecked((byte)yaw[i]);
                b[3 + i * 3] = unchecked((byte)pitch[i]);
            }
            return b;
        }

        /// <summary>Host packet: everyone's gaze (my own entry is ignored: I am the camera).</summary>
        public static void Apply(byte[] packet)
        {
            if (packet == null || packet.Length < 1) return;
            int n = packet[0];
            if (packet.Length < 1 + n * 3) return;
            int me = LineClient.I != null ? LineClient.I.MySlot : -1;
            for (int i = 0; i < n; i++)
            {
                int slot = packet[1 + i * 3];
                if (slot == me) continue;
                Set(slot, unchecked((sbyte)packet[2 + i * 3]), unchecked((sbyte)packet[3 + i * 3]));
            }
        }

        public static sbyte Quantise(float deg, int limit) { return (sbyte)Mathf.Clamp(Mathf.RoundToInt(deg), -limit, limit); }

        // ------------------------------------------------------------------ the local player's gaze

        /// <summary>Sends my look direction (meeting room only). Automated players (-bot) look like a bot would.</summary>
        public static void TickLocal(LineClient lc, LineView v)
        {
            if (Transport == null || lc == null || v == null) return;
            float now = Time.unscaledTime;
            if (now < _next) return;
            _next = now + Interval;
            var rooms = RoomsPresenter.Instance;
            if (rooms == null || !rooms.InMeeting) return;
            float yaw, pitch;
            if (LaunchArgs.Current.Bot)
            {
                if (_self == null || _self.Slot != lc.MySlot) _self = new BotGazer(lc.MySlot, LaunchArgs.Current.Seed * 31 + lc.MySlot);
                if (!_self.Next(v, rooms.Meeting, now, out yaw, out pitch)) return;
            }
            else if (LookingDown) { yaw = 0f; pitch = 34f; }
            else if (!rooms.TryGetMeetingLook(out yaw, out pitch)) return;
            int qy = Quantise(yaw, 90), qp = Quantise(pitch, 60);
            if (Mathf.Abs(qy - _sentYaw) < 1 && Mathf.Abs(qp - _sentPitch) < 1 && now - _lastSent < 1f) return;
            _sentYaw = qy; _sentPitch = qp; _lastSent = now;
            Transport((sbyte)qy, (sbyte)qp);
        }
    }

    /// <summary>
    /// Where a bot looks in the meeting room (host side for the host's bots, and for automated test players): the
    /// defender during the defence, else mostly whoever is talking, sometimes the colleague with the most suç (the one
    /// they suspect) or someone else, sometimes the table — with a little idle drift. v5 step 3b: the projector while a
    /// leaked recording plays, while a desk is counted and at the results (then the fired colleague taking the box).
    /// Uses public information only.
    /// </summary>
    public sealed class BotGazer
    {
        public readonly int Slot;
        readonly System.Random _rng;
        int _target = -1, _lastSpeaker = -1;
        float _until, _phase;

        public BotGazer(int slot, int seed)
        {
            Slot = slot;
            _rng = new System.Random(seed * 7919 + slot * 104729 + 17);
            _phase = (float)_rng.NextDouble() * 6.28f;
        }

        float R() { return (float)_rng.NextDouble(); }

        public bool Next(LineView v, MeetingRoom room, float now, out float yaw, out float pitch)
        {
            yaw = 0f; pitch = 10f;
            if (room == null || v == null || room.SeatAnchorOfSlot(Slot) == null) return false;
            int speaker = room.Speaker;
            if (speaker == Slot) speaker = -1;
            int defender = v.Phase == LinePhase.FinalDefense && v.Final != null ? v.Final.Defender : -1;
            if (defender == Slot) defender = -1;
            float driftY0 = Mathf.Sin(now * 0.7f + _phase) * 3f + Mathf.Sin(now * 1.9f + _phase * 2f) * 1.2f;
            float driftP0 = Mathf.Sin(now * 0.5f + _phase) * 2f;
            // the screen: a leak playing, a desk being counted, the results (glances away now and then)
            if (WatchesProjector(v) && Mathf.Repeat(now * 0.23f + _phase, 1f) > 0.12f && room.ProjectorAngles(Slot, out yaw, out pitch))
            {
                yaw += driftY0 * 0.6f; pitch += driftP0 * 0.6f;
                return true;
            }
            // the fired colleague takes the box: everyone looks
            var firing = DeskOpening.Current(v);
            double hostNow = LineClient.I != null ? LineClient.I.Now : 0;
            if (firing != null && firing.Player != Slot && DeskOpening.BoxShown(v, hostNow) && DeskOpening.Time(v, hostNow) < DeskOpening.PileAt + 0.6f
                && room.GazeAngles(Slot, firing.Player, out yaw, out pitch))
                return true;
            bool newSpeaker = speaker >= 0 && speaker != _lastSpeaker;
            _lastSpeaker = speaker;
            if (now >= _until || (newSpeaker && R() < 0.8f) || (defender >= 0 && _target != defender && R() < 0.5f))
            {
                float r = R();
                if (defender >= 0 && r < 0.92f) _target = defender;
                else if (speaker >= 0 && r < 0.75f) _target = speaker;
                else if (r < 0.88f) _target = Suspect(v);
                else if (r < 0.95f) _target = Random(v);
                else _target = -1;
                _until = now + 1.6f + R() * 2.8f;
            }
            float driftY = Mathf.Sin(now * 0.7f + _phase) * 3f + Mathf.Sin(now * 1.9f + _phase * 2f) * 1.2f;
            float driftP = Mathf.Sin(now * 0.5f + _phase) * 2f;
            if (_target >= 0 && room.GazeAngles(Slot, _target, out yaw, out pitch))
            {
                yaw += driftY; pitch += driftP;
                return true;
            }
            // the table in front, a little to one side
            yaw = Mathf.Sin(now * 0.31f + _phase) * 14f + driftY;
            pitch = 16f + driftP;
            return true;
        }

        /// <summary>The projector has the room's attention: a leaked recording plays, a desk is counted, the results.</summary>
        static bool WatchesProjector(LineView v)
        {
            if (v.Phase == LinePhase.Results) return true;
            double now = LineClient.I != null ? LineClient.I.Now : 0;
            if (v.Phase == LinePhase.FinalVerdict) return DeskOpening.Time(v, now) < DeskOpening.BoxAt;
            if (v.Meeting != null)
                foreach (var p in v.Meeting.Recordings)
                    if (now >= p.PlayAt - 1 && now < p.PlayAt + Math.Max(1f, p.Seconds) + 1) return true;
            return false;
        }

        /// <summary>The colleague with the most suç (ties broken per bot): the one this bot suspects.</summary>
        int Suspect(LineView v)
        {
            int best = -1, bestBlame = int.MinValue, bestTie = int.MaxValue;
            foreach (var p in v.Players)
            {
                if (p.Id == Slot || p.Fired) continue;
                int tie = (p.Id * 31 + Slot * 7) % 13;
                if (p.Blame > bestBlame || (p.Blame == bestBlame && tie < bestTie)) { best = p.Id; bestBlame = p.Blame; bestTie = tie; }
            }
            return best;
        }

        int Random(LineView v)
        {
            int n = 0;
            foreach (var p in v.Players) if (p.Id != Slot) n++;
            if (n == 0) return -1;
            int k = _rng.Next(n);
            foreach (var p in v.Players)
            {
                if (p.Id == Slot) continue;
                if (k-- == 0) return p.Id;
            }
            return -1;
        }
    }
}
