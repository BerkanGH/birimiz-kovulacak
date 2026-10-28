using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Keeps walls out of the follow camera's way. Driven by <see cref="CameraRig"/> once per frame:
    /// a sphere sweep from the avatar's head to the desired camera point (Walls + Partitions) plus one ray to the
    /// avatar's body (Partitions) find occluders; occluders with an <see cref="IOccluderFadable"/> (map v2's
    /// OccluderWall) are faded through <c>SetFaded</c>. Colliders without one are either faded by hiding their
    /// renderers (fallback, used while the scene has no OccluderWall at all) or, once OccluderWall exists in the
    /// scene, treated as solid: the camera is pulled in front of them so it never clips through.
    ///
    /// Cost: 1 SphereCastNonAlloc + 1 RaycastNonAlloc per frame into static buffers; occluder lookups are cached per
    /// collider (first sight allocates once). Faded walls are restored after <see cref="UnfadeDelay"/>.
    /// </summary>
    public sealed class CameraOcclusion : MonoBehaviour
    {
        public float ProbeRadius = 0.25f;
        public float UnfadeDelay = 0.25f;
        public float MinDistance = 0.7f;
        /// <summary>Fallback renderer hiding for colliders without IOccluderFadable (only while no OccluderWall exists).</summary>
        public bool FallbackRendererFade = true;
        /// <summary>Fallback never hides renderers larger than this (a merged building mesh would vanish entirely).</summary>
        public float FallbackMaxRendererSize = 14f;

        static readonly RaycastHit[] Hits = new RaycastHit[24];

        sealed class Occluder
        {
            public IOccluderFadable[] Faders;
            public Renderer[] Renderers;
            public ShadowCastingMode[] Modes;
            public bool Faded;
            public float LastHit;
            public bool Fadable => (Faders != null && Faders.Length > 0) || (Renderers != null && Renderers.Length > 0);

            public void Set(bool faded)
            {
                if (Faded == faded) return;
                Faded = faded;
                if (Faders != null)
                    for (int i = 0; i < Faders.Length; i++)
                    {
                        var f = Faders[i];
                        if (f is Object o && o == null) continue; // destroyed
                        f?.SetFaded(faded);
                    }
                if (Renderers != null)
                    for (int i = 0; i < Renderers.Length; i++)
                        if (Renderers[i] != null) Renderers[i].shadowCastingMode = faded ? ShadowCastingMode.ShadowsOnly : Modes[i];
            }
        }

        readonly Dictionary<Collider, Occluder> _byCollider = new Dictionary<Collider, Occluder>(64);
        readonly List<Occluder> _faded = new List<Occluder>(16);
        int _strict = -1; // -1 unknown, 0 fallback (no OccluderWall in the scene), 1 strict

        /// <summary>True when the scene provides IOccluderFadable components (map v2).</summary>
        public bool Strict
        {
            get
            {
                if (_strict < 0)
                {
                    _strict = 0;
                    var all = FindObjectsByType<MonoBehaviour>();
                    for (int i = 0; i < all.Length; i++) if (all[i] is IOccluderFadable) { _strict = 1; break; }
                }
                return _strict == 1;
            }
        }

        /// <summary>Forget cached occluders (call after the office geometry is rebuilt at runtime).</summary>
        public void Rescan()
        {
            ClearAll();
            _byCollider.Clear();
            _strict = -1;
        }

        /// <summary>
        /// Fades occluders between <paramref name="pivot"/> and the camera point at <paramref name="distance"/> along
        /// <paramref name="dirBack"/> and returns the distance the camera may use (shortened only by solid walls).
        /// </summary>
        public float Resolve(Vector3 pivot, Vector3 dirBack, float distance, Vector3 avatarBody)
        {
            float now = Time.unscaledTime;
            float allowed = distance;
            int n = Physics.SphereCastNonAlloc(pivot, ProbeRadius, dirBack, Hits, distance, FeelLayers.CameraOccluders, QueryTriggerInteraction.Ignore);
            // Pass 1: the nearest solid (non-fadable) wall limits the camera distance.
            for (int i = 0; i < n; i++)
            {
                if (Hits[i].distance <= 0f) continue; // overlapping at the start (wall right beside the head): not between
                var occ = Get(Hits[i].collider);
                if (occ != null && !occ.Fadable && Hits[i].collider.gameObject.layer == Layers.Walls)
                    allowed = Mathf.Min(allowed, Mathf.Max(MinDistance, Hits[i].distance - 0.1f));
            }
            // Pass 2: fade fadable occluders in front of the (possibly shortened) camera.
            for (int i = 0; i < n; i++)
            {
                if (Hits[i].distance <= 0f || Hits[i].distance > allowed + 0.3f) continue;
                Mark(Get(Hits[i].collider), now);
            }
            // Low cameras: a partition can hide the body while the head ray is clear.
            Vector3 camPos = pivot + dirBack * allowed;
            Vector3 toBody = avatarBody - camPos;
            float bodyDist = toBody.magnitude;
            if (bodyDist > 0.05f)
            {
                int m = Physics.RaycastNonAlloc(camPos, toBody / bodyDist, Hits, bodyDist, 1 << FeelLayers.Partitions, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < m; i++) Mark(Get(Hits[i].collider), now);
            }
            for (int i = _faded.Count - 1; i >= 0; i--)
            {
                if (now - _faded[i].LastHit <= UnfadeDelay) continue;
                _faded[i].Set(false);
                _faded.RemoveAt(i);
            }
            return allowed;
        }

        void Mark(Occluder occ, float now)
        {
            if (occ == null || !occ.Fadable) return;
            occ.LastHit = now;
            if (occ.Faded) return;
            occ.Set(true);
            _faded.Add(occ);
        }

        /// <summary>Restores every faded occluder (leaving follow mode).</summary>
        public void ClearAll()
        {
            for (int i = 0; i < _faded.Count; i++) _faded[i].Set(false);
            _faded.Clear();
        }

        void OnDisable() { ClearAll(); }

        Occluder Get(Collider col)
        {
            if (col == null) return null;
            Occluder occ;
            if (_byCollider.TryGetValue(col, out occ)) return occ;
            occ = new Occluder();
            var faders = col.GetComponentsInChildren<IOccluderFadable>(true);
            if (faders.Length == 0)
            {
                var parent = col.GetComponentInParent<IOccluderFadable>();
                if (parent != null) faders = new[] { parent };
            }
            if (faders.Length > 0) occ.Faders = faders;
            else if (FallbackRendererFade && !Strict)
            {
                var rs = col.GetComponentsInChildren<Renderer>(false);
                int ok = 0;
                for (int i = 0; i < rs.Length; i++) if (Usable(rs[i])) ok++;
                if (ok > 0)
                {
                    occ.Renderers = new Renderer[ok];
                    occ.Modes = new ShadowCastingMode[ok];
                    ok = 0;
                    for (int i = 0; i < rs.Length; i++)
                        if (Usable(rs[i])) { occ.Renderers[ok] = rs[i]; occ.Modes[ok] = rs[i].shadowCastingMode; ok++; }
                }
            }
            _byCollider[col] = occ;
            return occ;
        }

        bool Usable(Renderer r)
        {
            return r != null && !(r is ParticleSystemRenderer) && r.bounds.size.magnitude <= FallbackMaxRendererSize;
        }
    }
}
