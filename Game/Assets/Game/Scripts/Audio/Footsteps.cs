using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Distance-based footstep cadence for every avatar and the manager (stride 0.62 m walking, manager 0.75 m in dress
    /// shoes). Crouched avatars are silent to others (sneaking) and barely audible to themselves. Remote steps are 3D
    /// and wall-muffled, so you can hear someone in the next room you cannot see. Also fires the characters'
    /// squash impulse when an avatar stops abruptly. No allocations after the first frame an avatar appears.
    /// </summary>
    public sealed class Footsteps : MonoBehaviour
    {
        public static readonly string[] StepClips = { "step_0", "step_1", "step_2" };
        public static readonly string[] ManagerStepClips = { "mgr_step_0", "mgr_step_1" };

        public float Stride = 0.62f;
        public float CrouchStride = 0.5f;
        public float ManagerStride = 0.75f;
        public float LocalVolume = 0.28f;
        public float RemoteVolume = 0.55f;
        public float CrouchLocalVolume = 0.06f;
        public float ManagerVolume = 0.75f;

        sealed class Walker
        {
            public PlayerAvatar Avatar;
            public Vector3 Last;
            public float Accum;
            public float Speed;
            public float PeakSpeed;
            public int Variant;
            public bool Alive;
        }

        readonly List<Walker> _walkers = new List<Walker>(8);
        readonly Walker _manager = new Walker();
        bool _managerInit;

        void Update()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            var view = ClientState.I != null ? ClientState.I.View : null;
            bool office = view != null && AvatarVisibility.AppliesIn(view.Phase);
            Sync();
            var local = PlayerAvatar.Local;
            for (int i = 0; i < _walkers.Count; i++)
            {
                var w = _walkers[i];
                if (w.Avatar == null) continue;
                Vector3 p = w.Avatar.transform.position;
                Vector3 d = p - w.Last; d.y = 0;
                w.Last = p;
                float dist = d.magnitude;
                if (dist > 2f) { w.Accum = 0; w.Speed = 0; continue; } // teleport
                float inst = dist / dt;
                w.Speed = Mathf.Lerp(w.Speed, inst, 0.3f);
                bool crouched = OwnerNetworkTransform.IsCrouched(w.Avatar.gameObject);

                // Squash on an abrupt stop (cosmetic; characters module decides what it looks like).
                w.PeakSpeed = Mathf.Max(w.PeakSpeed * Mathf.Exp(-4f * dt), w.Speed);
                if (w.PeakSpeed > 2.4f && w.Speed < 0.35f)
                {
                    w.PeakSpeed = 0f;
                    var feel = FeelHooks.Of(w.Avatar);
                    if (feel != null) feel.Squash(0.15f);
                }

                if (!office) { w.Accum = 0; continue; }
                w.Accum += dist;
                float stride = crouched ? CrouchStride : Stride;
                if (w.Accum < stride) continue;
                w.Accum -= stride;
                bool isLocal = w.Avatar == local;
                if (crouched && !isLocal) continue; // sneaking is silent to others
                w.Variant = (w.Variant + 1) % StepClips.Length;
                float vol = isLocal ? (crouched ? CrouchLocalVolume : LocalVolume) : RemoteVolume;
                Sfx.PlayAt(StepClips[w.Variant], p, vol, Random.Range(0.92f, 1.08f), isLocal ? 0.3f : 1f);
            }
            StepManager(dt, office);
        }

        void StepManager(float dt, bool office)
        {
            var mgr = OfficeWorld.I != null ? OfficeWorld.I.Manager : null;
            if (mgr == null) return;
            Vector3 p = mgr.transform.position;
            if (!_managerInit) { _managerInit = true; _manager.Last = p; return; }
            Vector3 d = p - _manager.Last; d.y = 0;
            _manager.Last = p;
            float dist = d.magnitude;
            if (dist > 2f || !office) { _manager.Accum = 0; return; }
            _manager.Accum += dist;
            if (_manager.Accum < ManagerStride) return;
            _manager.Accum -= ManagerStride;
            _manager.Variant = (_manager.Variant + 1) % ManagerStepClips.Length;
            Sfx.PlayAt(ManagerStepClips[_manager.Variant], p, ManagerVolume, Random.Range(0.95f, 1.05f));
        }

        void Sync()
        {
            for (int i = 0; i < _walkers.Count; i++) _walkers[i].Alive = false;
            var all = PlayerAvatar.All;
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a == null) continue;
                Walker w = null;
                for (int j = 0; j < _walkers.Count; j++) if (_walkers[j].Avatar == a) { w = _walkers[j]; break; }
                if (w == null)
                {
                    w = new Walker { Avatar = a, Last = a.transform.position, Variant = i };
                    _walkers.Add(w);
                }
                w.Alive = true;
            }
            for (int i = _walkers.Count - 1; i >= 0; i--) if (!_walkers[i].Alive) _walkers.RemoveAt(i);
        }
    }
}
