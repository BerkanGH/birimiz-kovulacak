using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Per-desk visuals from the local view: drawer, dirt, spill, and the v2 workstation states:
    /// unassigned ("boş masa": tidy, monitor off, chair pushed in, BOŞ card) / unplugged (dark screen, power LED off,
    /// cable on the floor) / password-locked (red + padlock) / internet down ("no signal") / screen session open
    /// (desktop) / lock screen. During a power cut the monitor keeps glowing (small blue light).
    /// All material changes happen only when the state changes.
    /// </summary>
    public sealed class DeskVisual : MonoBehaviour
    {
        public int DeskIndex;
        public Transform Drawer;
        public Renderer Screen;
        public GameObject Padlock;
        public GameObject Dirt;
        public GameObject Spill;
        public GameObject OwnerMarker;     // optional floor ring in the owner's colour (public desk assignment)
        public Renderer OwnerMarkerRenderer;
        // v2
        public GameObject Cable;           // unplugged cable on the floor
        public Renderer[] PowerLeds;       // monitor + tower power LEDs
        public GameObject Personal;        // owner's clutter (plants, photos, ...)
        public GameObject EmptyCard;       // "BOŞ MASA" card on unassigned desks
        public Transform Chair;
        public Vector3 ChairSeat, ChairPushedIn;
        public Renderer[] ChairRenderers;
        public int[] ChairFabricSlots;     // per ChairRenderers entry: submesh index of the fabric (-1 none)
        public Light MonitorGlow;

        float _open;
        public bool WantOpen;
        [HideInInspector] public float DrawerRestZ;

        enum ScreenState : byte { Unknown, Off, Locked, NoSignal, Desktop, LockScreen }
        ScreenState _screen;
        int _owner = int.MinValue;
        Material _ownerMat;
        Material[][] _chairMats;
        bool _ledOn = true, _ledSet;
        float _chairK = -1f;
        float _glow = -1f;

        void Awake()
        {
            if (Drawer != null) DrawerRestZ = Drawer.localPosition.z;
            if (ChairRenderers != null)
            {
                _chairMats = new Material[ChairRenderers.Length][];
                for (int i = 0; i < ChairRenderers.Length; i++) _chairMats[i] = ChairRenderers[i] != null ? ChairRenderers[i].sharedMaterials : null;
            }
        }

        /// <summary>v1 entry point (kept for callers that only have the desk view).</summary>
        public void Apply(DeskView d) { Apply(d, null); }

        public void Apply(DeskView d, PlayerView v)
        {
            var pal = WorldPalette.I;
            bool assigned = d.AssignedOwner >= 0;
            if (Dirt != null && Dirt.activeSelf != d.Dirty) Dirt.SetActive(d.Dirty);
            if (Spill != null && Spill.activeSelf != d.Spill) Spill.SetActive(d.Spill);
            if (Padlock != null && Padlock.activeSelf != d.PcLocked) Padlock.SetActive(d.PcLocked);
            if (Cable != null && Cable.activeSelf != d.Unplugged) Cable.SetActive(d.Unplugged);
            if (Personal != null && Personal.activeSelf != assigned) Personal.SetActive(assigned);
            if (EmptyCard != null && EmptyCard.activeSelf == assigned) EmptyCard.SetActive(!assigned);

            bool internetDown = v != null && v.Office != null && v.Office.InternetDown;
            ScreenState s;
            if (!assigned || d.Unplugged) s = ScreenState.Off;
            else if (d.PcLocked) s = ScreenState.Locked;
            else if (internetDown) s = ScreenState.NoSignal;
            else if (d.ScreenOpen) s = ScreenState.Desktop;
            else s = ScreenState.LockScreen;
            if (s != _screen && pal != null && Screen != null)
            {
                _screen = s;
                Material m = null;
                switch (s)
                {
                    case ScreenState.Off: m = pal.ScreenOff; break;
                    case ScreenState.Locked: m = pal.ScreenLocked; break;
                    case ScreenState.NoSignal: m = pal.ScreenNoSignal; break;
                    case ScreenState.Desktop: m = pal.Screen; break;
                    case ScreenState.LockScreen: m = pal.ScreenLockScreen; break;
                }
                if (m != null) Screen.sharedMaterial = m;
            }

            bool led = assigned && !d.Unplugged;
            if ((led != _ledOn || !_ledSet) && pal != null && PowerLeds != null)
            {
                _ledOn = led; _ledSet = true;
                for (int i = 0; i < PowerLeds.Length; i++) if (PowerLeds[i] != null) PowerLeds[i].sharedMaterial = led ? pal.LedBlue : pal.LedOff;
            }

            // Owner colour on the chair fabric (+ optional floor marker).
            Material ownerMat = pal == null ? null : (assigned ? pal.PlayerColor(v, d.AssignedOwner) : pal.ChairNeutral);
            if ((d.AssignedOwner != _owner || ownerMat != _ownerMat) && ownerMat != null)
            {
                _owner = d.AssignedOwner;
                _ownerMat = ownerMat;
                if (_chairMats != null && ChairFabricSlots != null)
                    for (int i = 0; i < ChairRenderers.Length; i++)
                    {
                        var arr = _chairMats[i];
                        int slot = i < ChairFabricSlots.Length ? ChairFabricSlots[i] : -1;
                        if (arr == null || slot < 0 || slot >= arr.Length) continue;
                        arr[slot] = ownerMat;
                        ChairRenderers[i].sharedMaterials = arr;
                    }
                if (OwnerMarkerRenderer != null && assigned) OwnerMarkerRenderer.sharedMaterial = ownerMat;
            }
            if (OwnerMarker != null && OwnerMarker.activeSelf != assigned) OwnerMarker.SetActive(assigned);
            _chairTarget = assigned ? 0f : 1f;

            // Monitor glow during a power cut (UPS): only for a powered, assigned workstation.
            float dark = OfficeLighting.I != null ? OfficeLighting.I.Darkness : 0f;
            float glow = led ? dark : 0f;
            if (MonitorGlow != null && Mathf.Abs(glow - _glow) > 0.01f)
            {
                _glow = glow;
                bool on = glow > 0.05f;
                if (MonitorGlow.enabled != on) MonitorGlow.enabled = on;
                MonitorGlow.intensity = 1.3f * glow;
            }
        }

        float _chairTarget;

        void Update()
        {
            _open = Mathf.MoveTowards(_open, WantOpen ? 1f : 0f, Time.deltaTime * 3f);
            if (Drawer != null) Drawer.localPosition = new Vector3(Drawer.localPosition.x, Drawer.localPosition.y, -_open * 0.32f + DrawerRestZ);
            if (Chair != null && _chairK != _chairTarget)
            {
                _chairK = _chairK < 0f ? _chairTarget : Mathf.MoveTowards(_chairK, _chairTarget, Time.deltaTime * 2f);
                Chair.localPosition = Vector3.Lerp(ChairSeat, ChairPushedIn, _chairK);
            }
        }
    }
}
