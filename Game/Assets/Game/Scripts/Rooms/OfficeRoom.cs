using System;
using TMPro;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// The local player's private office (one room, personalised on <see cref="Enter"/>): monitor states, the desk
    /// phone (rings physically, handset lifts to your ear during a call, coiled cord, pulled plug when the line is
    /// dead), coffee spill, mug/drawer/nameplate click feedback, posters per slot and language, your own bean hands,
    /// passers-by behind the frosted door glass. All references are wired by ProjectBuilder.Rooms.
    /// </summary>
    public sealed class OfficeRoom : MonoBehaviour
    {
        [Serializable]
        public sealed class DecalSlot
        {
            public Renderer Surface;
            public Material[] Tr = new Material[0];
            public Material[] En = new Material[0];
            public int Offset;
        }

        [Header("Seat")]
        public Transform Eye;
        public float EyePitch = 13f, YawLimit = 115f, PitchUp = 40f, PitchDown = 65f;

        [Header("Monitor")]
        public Renderer Screen;
        public Material ScreenDesktop, ScreenAlert, ScreenOffline, ScreenCoffee, ScreenOff;
        public Renderer PcLed;
        public Material LedBlue, LedAmber, LedOff;
        public Light MonitorLight;

        [Header("Phone")]
        public Transform Phone;
        public Transform Handset;
        public Renderer RingLight, Display;
        public Material RingOff, RingRed, RingGreen, LcdOn, LcdOff;
        public GameObject Plug;
        public Transform CordBase, CordEnd;
        public LineRenderer Cord;
        /// <summary>Small red glow on the desk that pulses with the ring bursts.</summary>
        public Light RingGlow;
        /// <summary>Handset "at your ear" in camera space: shown in the lower right corner of the view (the ear end up/back).</summary>
        public Vector3 EarOffset = new Vector3(0.25f, -0.17f, 0.4f);
        public Vector3 EarForward = new Vector3(0.32f, 0.78f, -0.55f);
        public Vector3 EarUp = new Vector3(-1f, 0.25f, -0.1f);

        [Header("Desk")]
        public Transform Mug;
        public GameObject Coffee, Spill;
        public Renderer MugStripe;
        public Transform Drawer;
        public Transform Nameplate;
        public Renderer NameStripe;
        public TextMeshPro[] NameLabels = new TextMeshPro[0];
        public Renderer ChairFabric;
        public Renderer LampGlow;
        public Light LampLight;
        public Renderer Photo;
        public Material[] PhotoMats = new Material[0];
        public DecalSlot[] Decals = new DecalSlot[0];
        public Material[] PlayerColors = new Material[0];
        public Material[] SeatColors = new Material[0];

        [Header("Hands")]
        public Transform HandL, HandR;
        public Renderer[] HandRenderers = new Renderer[0];
        public Material HandSkinDefault;
        public Material[] SkinMats = new Material[0];

        [Header("Corridor")]
        public Transform Silhouette;
        public float CorridorX = 2.45f, CorridorZ0 = -4f, CorridorZ1 = 3f;

        PhoneLineState _phone;
        MonitorState _monitor;
        Material _screenShown;
        Vector3 _handsetRestPos, _cordEndLocal;
        Quaternion _handsetRestRot;
        float _lift;
        Vector3 _mugRestPos;
        Quaternion _mugRestRot;
        float _tip, _sipUntil, _sip;
        Vector3 _drawerRest;
        float _drawerOpen, _drawerUntil;
        Quaternion _nameRest;
        float _wobbleStart = -10f;
        const int CordPoints = 72;
        readonly Vector3[] _cord = new Vector3[CordPoints];
        float _silT = -1f, _silNext, _silDir = 1f, _silSpeed = 1.2f, _silBaseY;
        Vector3 _handLPos, _handRPos;
        Quaternion _handLRot, _handRRot;
        Material[][] _handMats;
        int _skinIndex = -1;
        int _slot = -1, _color = -1;
        string _name;
        string _lang;
        float _nextLangCheck;
        bool _awake;

        public PhoneLineState PhoneState => _phone;
        public MonitorState MonitorState => _monitor;

        void Awake() { Init(); }

        void Init()
        {
            if (_awake) return;
            _awake = true;
            if (Handset != null) { _handsetRestPos = Handset.localPosition; _handsetRestRot = Handset.localRotation; }
            // CordEnd is exported as a sibling of the handset (flat hierarchy): keep its offset in handset space
            if (Handset != null && CordEnd != null) _cordEndLocal = Handset.InverseTransformPoint(CordEnd.position);
            if (Mug != null) { _mugRestPos = Mug.localPosition; _mugRestRot = Mug.localRotation; }
            if (Drawer != null) _drawerRest = Drawer.localPosition;
            if (Nameplate != null) _nameRest = Nameplate.localRotation;
            if (HandL != null) { _handLPos = HandL.position; _handLRot = HandL.rotation; }
            if (HandR != null) { _handRPos = HandR.position; _handRRot = HandR.rotation; }
            if (Silhouette != null) { _silBaseY = Silhouette.position.y; Silhouette.gameObject.SetActive(false); }
            _silNext = Time.time + 4f;
            _handMats = new Material[HandRenderers.Length][];
            for (int i = 0; i < HandRenderers.Length; i++) if (HandRenderers[i] != null) _handMats[i] = HandRenderers[i].sharedMaterials;
            if (Cord != null) { Cord.positionCount = CordPoints; Cord.useWorldSpace = true; }
            if (Plug != null) Plug.SetActive(false);
            if (Spill != null) Spill.SetActive(false);
            ApplyPhoneLook(PhoneLineState.Idle, false);
        }

        // ------------------------------------------------------------------ personalisation

        public void Enter(int slot, int colorIndex, string playerName)
        {
            Init();
            playerName = playerName ?? "";
            if (slot == _slot && colorIndex == _color && playerName == _name) return;
            _slot = slot; _color = colorIndex; _name = playerName;
            int c = Mod(colorIndex, 6);
            if (ChairFabric != null && c < SeatColors.Length) ChairFabric.sharedMaterial = SeatColors[c];
            if (MugStripe != null && c < PlayerColors.Length) MugStripe.sharedMaterial = PlayerColors[c];
            if (NameStripe != null && c < PlayerColors.Length) NameStripe.sharedMaterial = PlayerColors[c];
            string label = string.IsNullOrEmpty(playerName) ? "—" : playerName.ToUpperInvariant();
            foreach (var l in NameLabels) if (l != null) l.text = label;
            if (Photo != null && PhotoMats.Length > 0) Photo.sharedMaterial = PhotoMats[c % PhotoMats.Length];
            SetSkin(c);
            _lang = null;
            ApplyLanguage();
        }

        static int Mod(int a, int n) { return ((a % n) + n) % n; }

        void SetSkin(int c)
        {
            if (c == _skinIndex || c >= SkinMats.Length || SkinMats[c] == null) return;
            _skinIndex = c;
            for (int i = 0; i < HandRenderers.Length; i++)
            {
                var r = HandRenderers[i];
                if (r == null || _handMats[i] == null) continue;
                var mats = (Material[])_handMats[i].Clone();
                for (int k = 0; k < mats.Length; k++) if (mats[k] == HandSkinDefault) mats[k] = SkinMats[c];
                r.sharedMaterials = mats;
            }
        }

        void ApplyLanguage()
        {
            string lang = Loc.Language;
            if (lang == _lang) return;
            _lang = lang;
            int s = Mathf.Max(0, _slot);
            foreach (var d in Decals)
            {
                if (d == null || d.Surface == null || d.Tr.Length == 0) continue;
                int i = (s + d.Offset) % d.Tr.Length;
                var m = lang == "en" && i < d.En.Length && d.En[i] != null ? d.En[i] : d.Tr[i];
                if (m != null) d.Surface.sharedMaterial = m;
            }
        }

        // ------------------------------------------------------------------ states

        public void SetPhone(PhoneLineState s) { _phone = s; }

        public void SetMonitor(MonitorState s) { _monitor = s; }

        /// <summary>Feedback for clicks on the desk (the caller decides what the click means).</summary>
        public void Clicked(DeskTarget t)
        {
            switch (t)
            {
                case DeskTarget.Mug: if (_monitor != MonitorState.Coffee) _sipUntil = Time.time + 1.5f; break;
                case DeskTarget.Drawer: _drawerUntil = Time.time + 2.2f; break;
                case DeskTarget.Nameplate: _wobbleStart = Time.time; break;
            }
        }

        /// <summary>Starts a passer-by behind the frosted door glass now (test harness), progress 0..1 along the corridor.</summary>
        public void PassBy(float progress, float dir = 1f)
        {
            if (Silhouette == null) return;
            _silDir = dir >= 0 ? 1f : -1f;
            _silT = Mathf.Clamp01(progress);
            _silSpeed = 1.15f;
            Silhouette.gameObject.SetActive(true);
        }

        // ------------------------------------------------------------------ frame

        public void Tick(Camera cam, PovMode mode, float dt)
        {
            float now = Time.time;
            if (now >= _nextLangCheck) { _nextLangCheck = now + 1f; ApplyLanguage(); }
            TickMonitor(now, dt);
            TickPhone(cam, now, dt);
            TickDesk(cam, now, dt);
            TickHands(cam, mode, now, dt);
            TickSilhouette(cam, now, dt);
        }

        void TickMonitor(float now, float dt)
        {
            Material want;
            bool blink = Mathf.Repeat(now, 1.2f) < 0.75f;
            switch (_monitor)
            {
                case MonitorState.Alert: want = blink ? ScreenAlert : ScreenDesktop; break;
                case MonitorState.Offline: want = ScreenOffline; break;
                case MonitorState.Coffee: want = ScreenCoffee; break;
                case MonitorState.Off: want = ScreenOff; break;
                default: want = ScreenDesktop; break;
            }
            if (want != null && want != _screenShown && Screen != null) { Screen.sharedMaterial = want; _screenShown = want; }
            if (PcLed != null)
            {
                Material led = _monitor == MonitorState.Off ? LedOff
                    : _monitor == MonitorState.Normal ? LedBlue
                    : (Mathf.Repeat(now, 0.6f) < 0.3f ? LedAmber : LedOff);
                if (led != null && PcLed.sharedMaterial != led) PcLed.sharedMaterial = led;
            }
            if (MonitorLight != null)
            {
                Color c; float i;
                switch (_monitor)
                {
                    case MonitorState.Alert: c = blink ? new Color(1f, 0.55f, 0.45f) : new Color(0.95f, 0.93f, 0.86f); i = 0.55f; break;
                    case MonitorState.Offline: c = new Color(0.55f, 0.6f, 0.72f); i = 0.3f; break;
                    case MonitorState.Coffee: c = new Color(0.8f, 0.55f, 0.35f); i = 0.35f; break;
                    case MonitorState.Off: c = Color.black; i = 0f; break;
                    default: c = new Color(0.95f, 0.93f, 0.86f); i = 0.5f; break;
                }
                MonitorLight.color = Color.Lerp(MonitorLight.color, c, 1f - Mathf.Exp(-dt * 12f));
                MonitorLight.intensity = Mathf.Lerp(MonitorLight.intensity, i, 1f - Mathf.Exp(-dt * 12f));
            }
            // coffee: tipped mug + spill
            float tipWant = _monitor == MonitorState.Coffee ? 1f : 0f;
            _tip = Mathf.MoveTowards(_tip, tipWant, dt * 4f);
            if (Spill != null && Spill.activeSelf != (_tip > 0.5f)) Spill.SetActive(_tip > 0.5f);
            if (Coffee != null && Coffee.activeSelf != (_tip < 0.5f)) Coffee.SetActive(_tip < 0.5f);
        }

        static bool RingBurst(float t)
        {
            float p = Mathf.Repeat(t, 3f);
            return p < 0.42f || (p > 0.62f && p < 1.04f);
        }

        void ApplyPhoneLook(PhoneLineState s, bool burst)
        {
            Material ring = RingOff;
            float now = Time.time;
            switch (s)
            {
                case PhoneLineState.Ringing: ring = burst ? RingRed : RingOff; break;
                case PhoneLineState.Dialing: ring = Mathf.Repeat(now, 1f) < 0.5f ? RingGreen : RingOff; break;
                case PhoneLineState.InCall: ring = RingGreen; break;
            }
            if (RingLight != null && ring != null && RingLight.sharedMaterial != ring) RingLight.sharedMaterial = ring;
            var lcd = s == PhoneLineState.Dead ? LcdOff : LcdOn;
            if (Display != null && lcd != null && Display.sharedMaterial != lcd) Display.sharedMaterial = lcd;
            bool dead = s == PhoneLineState.Dead;
            if (Plug != null && Plug.activeSelf != dead) Plug.SetActive(dead);
        }

        void TickPhone(Camera cam, float now, float dt)
        {
            bool burst = _phone == PhoneLineState.Ringing && RingBurst(now);
            ApplyPhoneLook(_phone, burst);
            if (RingGlow != null)
            {
                float want = burst ? 1.4f : 0f;
                RingGlow.intensity = Mathf.MoveTowards(RingGlow.intensity, want, dt * 12f);
                bool on = RingGlow.intensity > 0.01f;
                if (RingGlow.enabled != on) RingGlow.enabled = on;
            }
            if (Handset == null) return;
            bool up = _phone == PhoneLineState.InCall || _phone == PhoneLineState.Dialing;
            _lift = Mathf.MoveTowards(_lift, up ? 1f : 0f, dt * (up ? 2.4f : 2.0f));
            var parent = Handset.parent;
            Vector3 restPos = parent != null ? parent.TransformPoint(_handsetRestPos) : _handsetRestPos;
            Quaternion restRot = parent != null ? parent.rotation * _handsetRestRot : _handsetRestRot;
            if (burst)
            {
                // the handset rattles in its cradle
                float k = now * 1.0f;
                restPos += Vector3.up * (Mathf.Abs(Mathf.Sin(k * 52f)) * 0.013f);
                restRot = restRot * Quaternion.Euler(Mathf.Sin(k * 61f) * 6f, Mathf.Sin(k * 47f) * 5f, Mathf.Sin(k * 57f) * 5f);
            }
            if (_lift <= 0.0001f || cam == null)
            {
                Handset.SetPositionAndRotation(restPos, restRot);
            }
            else
            {
                float e = _lift * _lift * (3f - 2f * _lift);
                Vector3 ear = cam.transform.TransformPoint(EarOffset);
                Quaternion earRot = cam.transform.rotation * Quaternion.LookRotation(EarForward, EarUp);
                Vector3 p = Vector3.Lerp(restPos, ear, e) + Vector3.up * (Mathf.Sin(e * Mathf.PI) * 0.08f);
                if (_phone == PhoneLineState.InCall && _lift >= 1f) p += cam.transform.up * (Mathf.Sin(now * 1.7f) * 0.004f);
                Handset.SetPositionAndRotation(p, Quaternion.Slerp(restRot, earRot, e));
            }
            UpdateCord();
        }

        void UpdateCord()
        {
            if (Cord == null || CordBase == null || Handset == null) return;
            Vector3 a = CordBase.position, b = Handset.TransformPoint(_cordEndLocal);
            float floorY = Phone != null ? Phone.position.y + 0.004f : a.y - 0.02f;
            float dist = Vector3.Distance(a, b);
            float sag = 0.035f + dist * 0.35f;
            Vector3 mid = (a + b) * 0.5f + Vector3.down * sag;
            float turns = Mathf.Max(6f, dist * 55f);
            for (int i = 0; i < CordPoints; i++)
            {
                float t = i / (float)(CordPoints - 1);
                float u = 1f - t;
                Vector3 p = u * u * a + 2f * u * t * mid + t * t * b;
                Vector3 tan = (2f * u * (mid - a) + 2f * t * (b - mid));
                if (tan.sqrMagnitude < 1e-8f) tan = b - a;
                tan.Normalize();
                Vector3 n1 = Vector3.Cross(tan, Vector3.up);
                if (n1.sqrMagnitude < 1e-6f) n1 = Vector3.Cross(tan, Vector3.right);
                n1.Normalize();
                Vector3 n2 = Vector3.Cross(tan, n1);
                float ang = t * turns * Mathf.PI * 2f;
                float rad = 0.0055f * Mathf.Clamp01(Mathf.Min(t, 1f - t) * 12f);
                p += (n1 * Mathf.Cos(ang) + n2 * Mathf.Sin(ang)) * rad;
                if (p.y < floorY) p.y = floorY;
                _cord[i] = p;
            }
            Cord.SetPositions(_cord);
        }

        void TickDesk(Camera cam, float now, float dt)
        {
            if (Mug != null)
            {
                _sip = Mathf.MoveTowards(_sip, now < _sipUntil ? 1f : 0f, dt * 2.6f);
                var parent = Mug.parent;
                Vector3 rest = parent != null ? parent.TransformPoint(_mugRestPos) : _mugRestPos;
                Quaternion restRot = parent != null ? parent.rotation * _mugRestRot : _mugRestRot;
                float te = _tip * _tip * (3f - 2f * _tip);
                // tipped over: lying on its side toward the keyboard
                Vector3 tipPos = rest + Vector3.up * 0.06f + Vector3.right * 0.05f;
                Quaternion tipRot = Quaternion.AngleAxis(-92f, Vector3.forward) * restRot;
                Vector3 p = Vector3.Lerp(rest, tipPos, te);
                Quaternion r = Quaternion.Slerp(restRot, tipRot, te);
                if (_sip > 0f && cam != null)
                {
                    float se = _sip * _sip * (3f - 2f * _sip);
                    Vector3 sipPos = cam.transform.TransformPoint(new Vector3(-0.1f, -0.2f, 0.42f));
                    Quaternion sipRot = Quaternion.AngleAxis(-22f, cam.transform.right) * restRot;
                    p = Vector3.Lerp(p, sipPos, se);
                    r = Quaternion.Slerp(r, sipRot, se);
                }
                Mug.SetPositionAndRotation(p, r);
            }
            if (Drawer != null)
            {
                _drawerOpen = Mathf.MoveTowards(_drawerOpen, now < _drawerUntil ? 1f : 0f, dt * 3.5f);
                float de = _drawerOpen * _drawerOpen * (3f - 2f * _drawerOpen);
                Drawer.localPosition = _drawerRest + new Vector3(0f, 0f, -0.26f * de);
            }
            if (Nameplate != null)
            {
                float w = now - _wobbleStart;
                float ang = w < 1.2f ? Mathf.Sin(w * 22f) * 9f * (1f - w / 1.2f) : 0f;
                Nameplate.localRotation = _nameRest * Quaternion.Euler(0f, ang, 0f);
            }
        }

        void TickHands(Camera cam, PovMode mode, float now, float dt)
        {
            float breathe = Mathf.Sin(now * 1.3f) * 0.0025f;
            if (HandL != null) HandL.SetPositionAndRotation(_handLPos + Vector3.up * breathe, _handLRot);
            if (HandR == null) return;
            Vector3 p = _handRPos + Vector3.up * breathe * 0.7f;
            Quaternion r = _handRRot;
            if (_phone == PhoneLineState.Dialing && _lift < 0.98f && Phone != null)
            {
                // tapping the keypad
                float tap = Mathf.Abs(Mathf.Sin(now * 9f));
                Vector3 pad = Phone.TransformPoint(new Vector3(0.05f, 0.11f + tap * 0.03f, -0.02f));
                p = Vector3.Lerp(p, pad, 0.85f);
            }
            if (_lift > 0f && Handset != null && cam != null)
            {
                float e = _lift * _lift * (3f - 2f * _lift);
                Vector3 grip = Handset.position - cam.transform.up * 0.035f + cam.transform.right * 0.015f;
                Vector3 armDir = (-cam.transform.up * 0.85f - cam.transform.forward * 0.25f + cam.transform.right * 0.2f).normalized;
                Quaternion gripRot = Quaternion.LookRotation(armDir, cam.transform.right);
                p = Vector3.Lerp(p, grip, e);
                r = Quaternion.Slerp(r, gripRot, e);
            }
            HandR.SetPositionAndRotation(p, r);
        }

        void TickSilhouette(Camera cam, float now, float dt)
        {
            if (Silhouette == null) return;
            if (_silT < 0f)
            {
                if (now < _silNext) return;
                PassBy(0f, UnityEngine.Random.value < 0.5f ? 1f : -1f);
                _silSpeed = UnityEngine.Random.Range(0.9f, 1.5f);
            }
            float len = Mathf.Max(0.1f, CorridorZ1 - CorridorZ0);
            _silT += dt * _silSpeed / len;
            if (_silT >= 1f)
            {
                _silT = -1f;
                _silNext = now + UnityEngine.Random.Range(7f, 22f);
                Silhouette.gameObject.SetActive(false);
                return;
            }
            float z = _silDir > 0 ? Mathf.Lerp(CorridorZ0, CorridorZ1, _silT) : Mathf.Lerp(CorridorZ1, CorridorZ0, _silT);
            float bob = Mathf.Abs(Mathf.Sin(_silT * len * 2.6f)) * 0.035f;
            var parent = Silhouette.parent;
            Vector3 local = new Vector3(CorridorX, 0f, z);
            Vector3 world = parent != null ? parent.TransformPoint(local) : local;
            world.y = _silBaseY + bob;
            Silhouette.position = world;
            if (cam != null)
            {
                // face the seat: through the frosted glass it reads as a soft shape, never as a thin card
                Vector3 d = world - cam.transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > 1e-4f) Silhouette.rotation = Quaternion.LookRotation(d, Vector3.up);
            }
        }
    }
}
