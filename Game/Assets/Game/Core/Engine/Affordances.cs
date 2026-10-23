using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    // CONTRACT STUB — owned by the Codex work package.

    /// <summary>
    /// Client-side prompt helper. Computes the contextual options for one focused target using only the
    /// recipient's own <see cref="PlayerView"/> (never host-only state), so prompts agree with host validation
    /// in the common case. The host still validates everything on Submit.
    /// Ordering: the first entry is bound to the primary interact key (E), the second to the secondary key (F).
    /// Blocked-but-relevant options are included with <see cref="Affordance.BlockedBy"/> set so the UI can explain why.
    /// </summary>
    public static class Affordances
    {
        /// <param name="targetId">Focused entity id: item, slot, station, desk ("desk.N").</param>
        /// <param name="actorPosition">The recipient's current avatar position (for range hints).</param>
        public static List<Affordance> For(PlayerView view, MatchConfig config, OfficeLayout layout, string targetId, Vec3 actorPosition)
        {
            throw new NotImplementedException();
        }

        /// <summary>Ids of every interactable target in the layout plus currently visible items (for focus search).</summary>
        public static List<string> TargetIds(PlayerView view, OfficeLayout layout)
        {
            throw new NotImplementedException();
        }
    }
}
