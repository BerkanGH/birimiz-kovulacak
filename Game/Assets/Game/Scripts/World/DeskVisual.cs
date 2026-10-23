using BirimizKovulacak.Core;
using TMPro;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>Per-desk visuals: drawer, screen lock, dirt, spill.</summary>
    public sealed class DeskVisual : MonoBehaviour
    {
        public int DeskIndex;
        public Transform Drawer;
        public Renderer Screen;
        public GameObject Padlock;
        public GameObject Dirt;
        public GameObject Spill;
        public GameObject OwnerMarker;     // floor ring in the owner's colour (public desk assignment)
        public Renderer OwnerMarkerRenderer;

        float _open;
        public bool WantOpen;

        public void Apply(DeskView d)
        {
            if (Dirt != null) Dirt.SetActive(d.Dirty);
            if (Spill != null) Spill.SetActive(d.Spill);
            if (Padlock != null) Padlock.SetActive(d.PcLocked);
            if (Screen != null && WorldPalette.I != null)
                Screen.sharedMaterial = d.PcLocked ? WorldPalette.I.ScreenLocked : WorldPalette.I.Screen;
            if (OwnerMarkerRenderer != null && WorldPalette.I != null && d.AssignedOwner >= 0 && d.AssignedOwner < WorldPalette.I.PlayerColors.Length)
                OwnerMarkerRenderer.sharedMaterial = WorldPalette.I.PlayerColors[d.AssignedOwner];
            if (OwnerMarker != null) OwnerMarker.SetActive(d.AssignedOwner >= 0);
        }

        void Update()
        {
            _open = Mathf.MoveTowards(_open, WantOpen ? 1f : 0f, Time.deltaTime * 3f);
            if (Drawer != null) Drawer.localPosition = new Vector3(Drawer.localPosition.x, Drawer.localPosition.y, -_open * 0.32f + DrawerRestZ);
        }

        [HideInInspector] public float DrawerRestZ;
        void Awake() { if (Drawer != null) DrawerRestZ = Drawer.localPosition.z; }
    }
}
