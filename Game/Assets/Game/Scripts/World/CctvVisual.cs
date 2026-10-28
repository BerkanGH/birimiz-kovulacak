using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>Security camera: red REC light blinks while it records (not during a power cut: "kayıt yok").</summary>
    public sealed class CctvVisual : DeviceVisual
    {
        public string CameraId;
        public Renderer RecLight;
        public float Phase;
        Material _m;

        public override void Tick(PlayerView v, double now, float t)
        {
            if (P == null) return;
            bool rec = !PowerDown(v) && Mathf.Repeat(t + Phase, 1.2f) < 0.7f;
            Set(RecLight, rec ? P.LedRed : P.LedOff, ref _m);
        }
    }
}
