using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 step 3b: the desk opening after a firing (docs/V5_INTERACTION.md §3, docs/V5_DESIGN.md §5 "Masa açılışı"),
    /// one timeline shared by the projector (the drawer opens, the bundles are counted: "23 PRİM"), the world (the fired
    /// colleague takes the box, the bundles appear at their place and the shares slide to the voters) and the camera
    /// (the projector first, then the table). Times are seconds of a 10 s verdict; a shorter verdict (automated runs)
    /// squeezes them. Everyone computes it from the same host time (<see cref="LineView.PhaseStartTime"/>).
    /// </summary>
    public static class DeskOpening
    {
        // the drawer opens, the bundles are counted, "23 PRİM" is stamped, the declaration is stamped against it (v5.2:
        // YALAN / DOĞRU), who shares it; the camera comes back to the table, the fired colleague takes the box, the
        // bundles appear at their place and the shares slide to the voters
        public const float DrawerOpen = 0.7f, CountStart = 1.1f, CountEnd = 3.3f, StampAt = 3.45f, VerdictAt = 4.05f, ChipsAt = 4.6f,
            BoxAt = 5.7f, PileAt = 6.0f, SlideAt = 6.7f, SlideTime = 1.2f;

        /// <summary>The latest firing while its verdict runs, else null.</summary>
        public static LineFiringView Current(LineView v)
        {
            if (v == null || v.Phase != LinePhase.FinalVerdict || v.Final == null || v.Final.Firings.Count == 0) return null;
            return v.Final.Firings[v.Final.Firings.Count - 1];
        }

        /// <summary>1 for a 10 s verdict; smaller when the phase is shorter (never below a quarter).</summary>
        public static float Scale(LineView v)
        {
            float len = v != null ? (float)(v.PhaseEndTime - v.PhaseStartTime) : 10f;
            return Mathf.Clamp(len / 10f, 0.25f, 1f);
        }

        /// <summary>Seconds into the opening, in timeline units (0 at the verdict's start).</summary>
        public static float Time(LineView v, double now)
        {
            if (v == null) return 0f;
            return (float)(now - v.PhaseStartTime) / Scale(v);
        }

        /// <summary>Count progress 0..1 (the drawer is open and the bundles are counted).</summary>
        public static float Count(float t) { return Mathf.Clamp01((t - CountStart) / (CountEnd - CountStart)); }

        /// <summary>The fired colleague shows in the room with their box (after the count).</summary>
        public static bool BoxShown(LineView v, double now) { return Current(v) == null || Time(v, now) >= BoxAt; }
    }

    /// <summary>
    /// v5 step 3b presentation fix: in the final vote a blank paper (çekimser) is a valid vote, but the engine reports it
    /// as "not voted" (the ballot stays empty). The meeting room tracks who dropped a paper this round from the public
    /// VoteCast events (never the content), so the board, the name tags and the projector say "pusula attı" for every
    /// paper that went into the box. Meeting and crisis votes keep the engine's flag (an empty ballot counts against you).
    /// </summary>
    public static class BallotDrops
    {
        static double _round = double.NaN;
        static LinePhase _phase;
        static readonly HashSet<int> _dropped = new HashSet<int>();

        static void Sync(LineView v)
        {
            if (v == null || (v.PhaseStartTime == _round && v.Phase == _phase)) return;
            _round = v.PhaseStartTime;
            _phase = v.Phase;
            _dropped.Clear();
        }

        /// <summary>A paper went into the box (VoteCast event, or my own drop).</summary>
        public static void Dropped(LineView v, int slot)
        {
            if (v == null) return;
            Sync(v);
            _dropped.Add(slot);
        }

        /// <summary>The colleague's paper is in the box this round.</summary>
        public static bool InBox(LineView v, LinePlayerView p)
        {
            if (v == null || p == null || p.Fired) return false;
            Sync(v);
            if (p.Voted) return true;
            return v.Phase == LinePhase.FinalVote && _dropped.Contains(p.Id);
        }

        /// <summary>Papers in the box / colleagues who can still vote (connected, not fired).</summary>
        public static void Count(LineView v, out int inBox, out int voters)
        {
            inBox = voters = 0;
            if (v == null) return;
            foreach (var p in v.Players)
            {
                if (p.Fired) continue;
                if (p.Connected) voters++;
                if (InBox(v, p)) inBox++;
            }
        }

        public static void Clear() { _round = double.NaN; _dropped.Clear(); }
    }
}
