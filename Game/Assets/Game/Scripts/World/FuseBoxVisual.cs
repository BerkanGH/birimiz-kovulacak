using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>st.fusebox: the main lever flips down and the status LED blinks red while the power is cut.</summary>
    public sealed class FuseBoxVisual : DeviceVisual
    {
        public Transform Lever;
        public Renderer Light;
        public float DownAngle = 70f;
        Quaternion _rest;
        bool _restSet;
        float _k;
        Material _m;

        public override void Tick(PlayerView v, double now, float t)
        {
            bool down = PowerDown(v);
            if (Lever != null)
            {
                if (!_restSet) { _rest = Lever.localRotation; _restSet = true; }
                float target = down ? 1f : 0f;
                if (_k != target)
                {
                    _k = Mathf.MoveTowards(_k, target, Time.deltaTime * 6f);
                    Lever.localRotation = _rest * Quaternion.Euler(_k * DownAngle, 0f, 0f);
                }
            }
            if (P != null) Set(Light, down ? (Mathf.Repeat(t, 0.5f) < 0.25f ? P.LedRed : P.LedOff) : P.LedGreen, ref _m);
        }
    }
}
