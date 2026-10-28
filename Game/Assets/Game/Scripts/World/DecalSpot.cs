using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>Wall poster / sign spot (map v2). The ProjectBuilder puts a placeholder or the 2D art texture on it.</summary>
    public sealed class DecalSpot : MonoBehaviour
    {
        public string Id;
        public Renderer Surface;
    }
}
