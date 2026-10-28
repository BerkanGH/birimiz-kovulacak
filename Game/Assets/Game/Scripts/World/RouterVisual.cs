using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>st.router: two status LEDs flicker green (traffic); during an outage they blink red/amber.</summary>
    public sealed class RouterVisual : DeviceVisual
    {
        public Renderer LightA, LightB;
        Material _a, _b;

        public override void Tick(PlayerView v, double now, float t)
        {
            if (P == null) return;
            if (InternetDown(v))
            {
                bool on = Mathf.Repeat(t, 0.6f) < 0.3f;
                Set(LightA, on ? P.LedRed : P.LedOff, ref _a);
                Set(LightB, on ? P.LedOff : P.LedAmber, ref _b);
            }
            else
            {
                Set(LightA, P.LedGreen, ref _a);
                Set(LightB, Mathf.Repeat(t * 7.3f, 1f) < 0.55f ? P.LedGreen : P.LedOff, ref _b);
            }
        }
    }
}
