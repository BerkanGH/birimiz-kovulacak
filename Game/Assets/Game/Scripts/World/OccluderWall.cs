using UnityEngine;
using UnityEngine.Rendering;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Marker on every opaque wall / partition renderer (map v2). The follow camera (feel's CameraOcclusion) calls
    /// <see cref="SetFaded"/> for occluders between the camera and the player: all submeshes switch to one shared
    /// translucent "ghost" material (which still casts shadows, so room lighting does not jump), and back.
    /// No per-frame work and no allocations after the first fade (material arrays are cached per submesh count).
    /// </summary>
    public sealed class OccluderWall : MonoBehaviour, IOccluderFadable
    {
        /// <summary>Renderers faded together (set by the ProjectBuilder; empty = renderers on this object).</summary>
        public Renderer[] Renderers;
        /// <summary>Optional override; otherwise <see cref="WorldPalette.WallGhost"/>.</summary>
        public Material GhostMaterial;

        Material[][] _original;
        bool _faded;

        static readonly Material[][] GhostArrays = new Material[16][];
        static Material _ghostFor;

        public bool Faded => _faded;

        public void SetFaded(bool faded)
        {
            if (_faded == faded) return;
            _faded = faded;
            if (Renderers == null || Renderers.Length == 0) Renderers = GetComponents<Renderer>();
            if (_original == null)
            {
                _original = new Material[Renderers.Length][];
                for (int i = 0; i < Renderers.Length; i++) _original[i] = Renderers[i] != null ? Renderers[i].sharedMaterials : null;
            }
            var ghost = GhostMaterial != null ? GhostMaterial : (WorldPalette.I != null ? WorldPalette.I.WallGhost : null);
            for (int i = 0; i < Renderers.Length; i++)
            {
                var r = Renderers[i];
                if (r == null || _original[i] == null) continue;
                if (!faded) { r.sharedMaterials = _original[i]; r.shadowCastingMode = ShadowCastingMode.On; continue; }
                if (ghost == null) { r.shadowCastingMode = ShadowCastingMode.ShadowsOnly; continue; }
                r.sharedMaterials = GhostArray(ghost, _original[i].Length);
            }
        }

        static Material[] GhostArray(Material ghost, int count)
        {
            if (_ghostFor != ghost)
            {
                for (int i = 0; i < GhostArrays.Length; i++) GhostArrays[i] = null;
                _ghostFor = ghost;
            }
            if (count >= GhostArrays.Length) count = GhostArrays.Length - 1;
            var a = GhostArrays[count];
            if (a == null)
            {
                a = new Material[count];
                for (int i = 0; i < count; i++) a[i] = ghost;
                GhostArrays[count] = a;
            }
            return a;
        }
    }
}
