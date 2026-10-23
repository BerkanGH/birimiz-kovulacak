using BirimizKovulacak.Core;
using TMPro;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>Shared materials used by runtime visuals (assigned by the ProjectBuilder).</summary>
    public sealed class WorldPalette : MonoBehaviour
    {
        public static WorldPalette I { get; private set; }
        public Material[] PlayerColors;
        public Material Screen;
        public Material ScreenLocked;
        public Material ConeMaterial;
        public Material HighlightMaterial;
        void Awake() { I = this; }
    }
}
