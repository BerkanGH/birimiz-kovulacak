using BirimizKovulacak.Core;
using TMPro;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>Phone, printer and coffee machine feedback.</summary>
    public sealed class StationVisual : MonoBehaviour
    {
        public string StationId;
        public Transform ShakeTarget;
        public Renderer Light;
        public Material LightOn, LightOff;
        public ParticleSystem Steam;
        Vector3 _rest;
        bool _restSet;

        public void Apply(PlayerView v, StationView s, double now)
        {
            bool active = false;
            if (s != null)
            {
                switch (s.Kind)
                {
                    case StationKind.Phone: active = v.Quota.PhoneRinging && s.UsedBy == PlayerIds.None; break;
                    case StationKind.Printer: active = s.PrintEndsAt > now; break;
                    case StationKind.CoffeeMachine: active = s.UsedBy != PlayerIds.None; break;
                }
            }
            if (ShakeTarget != null)
            {
                if (!_restSet) { _rest = ShakeTarget.localPosition; _restSet = true; }
                ShakeTarget.localPosition = _rest + (active && s != null && s.Kind == StationKind.Phone ? new Vector3(Mathf.Sin(Time.time * 70f) * 0.006f, Mathf.Abs(Mathf.Sin(Time.time * 35f)) * 0.01f, 0) : Vector3.zero);
            }
            if (Light != null && LightOn != null && LightOff != null)
                Light.sharedMaterial = active && Mathf.Repeat(Time.time, 0.5f) < 0.3f ? LightOn : LightOff;
            if (Steam != null)
            {
                if (active && !Steam.isPlaying) Steam.Play();
                else if (!active && Steam.isPlaying) Steam.Stop();
            }
            Sfx.Loop(StationId, active ? LoopFor(s) : null, transform.position);
        }

        static string LoopFor(StationView s)
        {
            if (s == null) return null;
            switch (s.Kind)
            {
                case StationKind.Phone: return "phone_ring";
                case StationKind.Printer: return "printer";
                case StationKind.CoffeeMachine: return "coffee";
                default: return null;
            }
        }
    }
}
