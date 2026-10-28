using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>Server rack LED strips: random-ish blinking; amber/red during an internet outage; dark without power.</summary>
    public sealed class RackBlinker : DeviceVisual
    {
        public Renderer Leds, LedsB;
        public float Phase;
        public float Speed = 1f;
        Material _a, _b;

        public override void Tick(PlayerView v, double now, float t)
        {
            if (P == null) return;
            if (PowerDown(v)) { Set(Leds, P.LedOff, ref _a); Set(LedsB, P.LedOff, ref _b); return; }
            float x = t * Speed + Phase;
            if (InternetDown(v))
            {
                Set(Leds, Mathf.Repeat(x * 2f, 1f) < 0.5f ? P.LedRed : P.LedAmber, ref _a);
                Set(LedsB, Mathf.Repeat(x * 2f + 0.5f, 1f) < 0.5f ? P.LedRed : P.LedOff, ref _b);
                return;
            }
            Set(Leds, Mathf.Repeat(x * 1.7f, 1f) < 0.8f ? P.LedGreen : P.LedOff, ref _a);
            Set(LedsB, Mathf.Repeat(x * 0.9f + 0.3f, 1f) < 0.35f ? P.LedOff : P.LedAmber, ref _b);
        }
    }
}
