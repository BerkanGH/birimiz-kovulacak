using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>Shared materials used by runtime visuals (assigned by the ProjectBuilder).</summary>
    public sealed class WorldPalette : MonoBehaviour
    {
        public static WorldPalette I { get; private set; }
        /// <summary>Indexed by colour index (= player id unless the lobby assigns colours): Mavi, Sarı, Pembe, Yeşil, Turuncu, Mor.</summary>
        public Material[] PlayerColors;
        /// <summary>Screen session open (desktop / e-mail UI).</summary>
        public Material Screen;
        /// <summary>Password-locked workstation (LockWorkstation sabotage).</summary>
        public Material ScreenLocked;
        /// <summary>Lock screen (session closed, normal idle state).</summary>
        public Material ScreenLockScreen;
        /// <summary>Unplugged / unassigned desk.</summary>
        public Material ScreenOff;
        /// <summary>Internet outage.</summary>
        public Material ScreenNoSignal;
        public Material ConeMaterial;
        public Material HighlightMaterial;
        public Material LedGreen, LedRed, LedAmber, LedBlue, LedOff;
        /// <summary>Translucent material OccluderWall swaps in while the camera looks through a wall.</summary>
        public Material WallGhost;
        /// <summary>Chair fabric of an unassigned desk.</summary>
        public Material ChairNeutral;

        void Awake() { I = this; }

        /// <summary>Material of a player's colour (uses the public ColorIndex when the player is in the view).</summary>
        public Material PlayerColor(PlayerView v, int player)
        {
            if (PlayerColors == null || PlayerColors.Length == 0 || player < 0) return null;
            int index = player;
            if (v != null)
                for (int i = 0; i < v.Players.Count; i++)
                    if (v.Players[i].Id == player) { index = v.Players[i].ColorIndex; break; }
            if (index < 0 || index >= PlayerColors.Length) index = player % PlayerColors.Length;
            return PlayerColors[index];
        }
    }
}
