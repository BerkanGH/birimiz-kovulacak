using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Small state lights of the v2 sabotage devices, ticked by <see cref="OfficeWorld"/> from the local view.
    /// Material swaps happen only when the shown state changes (no per-frame allocations).
    /// </summary>
    public abstract class DeviceVisual : MonoBehaviour
    {
        public abstract void Tick(PlayerView v, double now, float t);

        protected static void Set(Renderer r, Material m, ref Material last)
        {
            if (r == null || m == null || m == last) return;
            r.sharedMaterial = m;
            last = m;
        }

        protected static WorldPalette P => WorldPalette.I;
        protected static bool InternetDown(PlayerView v) { return v != null && v.Office != null && v.Office.InternetDown; }
        protected static bool PowerDown(PlayerView v) { return v != null && v.Office != null && v.Office.PowerDown; }
    }
}
