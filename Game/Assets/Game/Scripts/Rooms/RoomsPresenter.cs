using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// The v3 world: implements <see cref="IRoomPresenter"/> over the rooms scene (one private office personalised for
    /// the local player, one meeting room) and registers itself in <see cref="Rooms.Presenter"/>. Callers drive the
    /// place (EnterOffice / EnterMeeting) and the camera mode; the world only raises click events.
    /// Built by Editor/ProjectBuilder.Rooms.cs (scene Rooms.unity). Runs after default scripts so it sees the
    /// cursor lock UIRoot decided this frame.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class RoomsPresenter : MonoBehaviour, IRoomPresenter
    {
        public PovCameraRig Rig;
        public OfficeRoom Office;
        public MeetingRoom Meeting;
        public RoomLighting Lighting;
        public GameObject OfficeRoot, MeetingRoot;
        public RoomClock[] Clocks = new RoomClock[0];
        public Material HoverMat;
        public float OfficeReach = 3.2f, MeetingReach = 6f;

        /// <summary>Optional: let the world lock/unlock the cursor itself (UIRoot normally owns it).</summary>
        public static bool ManageCursor;
        /// <summary>Test harness: show the crosshair and allow hover/click while the cursor is free.</summary>
        public static bool ForceLookInput;

        public static RoomsPresenter Instance { get; private set; }

        enum Place { Office, Meeting }

        Place _place = Place.Office;
        PovMode _mode = PovMode.Office;
        bool _entered;
        RoomTarget _hover;
        Color _hoverBase;
        int _localSlot = -1;
        Vector3 _meetEye;
        Quaternion _meetRot = Quaternion.identity;
        // cutscene pose (scripted camera)
        bool _hasCutscene;
        Vector3 _cutPos;
        Quaternion _cutRot;
        float _cutFov = 45f;

        public PovMode Mode => _mode;
        public Camera PovCamera => Rig != null ? Rig.Cam : null;
        public bool InMeeting => _place == Place.Meeting;

        void Awake()
        {
            Instance = this;
            Rooms.Presenter = this;
            if (HoverMat != null) _hoverBase = HoverMat.GetColor("_BaseColor");
            ShowPlace(Place.Office);
        }

        void OnDestroy()
        {
            if (Rooms.Presenter == (IRoomPresenter)this) Rooms.Presenter = null;
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            // Attract view behind the menus until a match enters a room.
            if (!_entered && Office != null) Office.Enter(0, 0, "");
            if (Lighting != null) Lighting.SetTime(0.15f);
            Rig.SetLimits(Office.YawLimit, Office.PitchUp, Office.PitchDown);
            Rig.SetLook(0f, Office.EyePitch);
            Rig.Snap();
        }

        void ShowPlace(Place p)
        {
            _place = p;
            if (OfficeRoot != null && OfficeRoot.activeSelf != (p == Place.Office)) OfficeRoot.SetActive(p == Place.Office);
            if (MeetingRoot != null && MeetingRoot.activeSelf != (p == Place.Meeting)) MeetingRoot.SetActive(p == Place.Meeting);
        }

        // ------------------------------------------------------------------ IRoomPresenter

        public void EnterOffice(int localSlot, int colorIndex, string playerName)
        {
            _entered = true;
            _localSlot = localSlot;
            bool switched = _place != Place.Office;
            ShowPlace(Place.Office);
            Office.Enter(localSlot, colorIndex, playerName);
            if (switched)
            {
                Rig.SetLimits(Office.YawLimit, Office.PitchUp, Office.PitchDown);
                Rig.SetLook(0f, Office.EyePitch);
                Rig.Snap();
                if (Lighting != null) Lighting.RefreshProbes();
            }
        }

        public void EnterMeeting(int localSlot, IReadOnlyList<MeetingSeatInfo> seats, string managerProfileId)
        {
            _entered = true;
            _localSlot = localSlot;
            bool switched = _place != Place.Meeting;
            ShowPlace(Place.Meeting);
            Meeting.Enter(localSlot, seats, managerProfileId);
            if (!Meeting.TryGetEye(localSlot, out _meetEye, out _meetRot))
            {
                // spectator / no seat: stand at the foot of the table
                var t = Meeting.transform;
                _meetEye = t.TransformPoint(new Vector3(0f, 1.55f, -2.4f));
                _meetRot = Quaternion.LookRotation(t.forward, Vector3.up);
            }
            if (switched)
            {
                Rig.SetLimits(Meeting.YawLimit, Meeting.PitchUp, Meeting.PitchDown);
                Rig.SetLook(0f, 6f);
                Rig.Snap();
                if (Lighting != null) Lighting.RefreshProbes();
            }
        }

        public void SetPovMode(PovMode mode)
        {
            if (mode == _mode) return;
            _mode = mode;
            if (Rig != null) Rig.BeginTransition();
            SetHover(null);
        }

        public void SetPhoneState(PhoneLineState state) { if (Office != null) Office.SetPhone(state); }
        public void SetMonitorState(MonitorState state) { if (Office != null) Office.SetMonitor(state); }

        public void SetTimeOfDay(float t01)
        {
            if (Lighting != null) Lighting.SetTime(t01);
            float hours = 9f + 9f * Mathf.Clamp01(t01);
            for (int i = 0; i < Clocks.Length; i++) if (Clocks[i] != null) Clocks[i].SetTime(hours);
        }

        public void SetSpeaking(int slot, float level) { if (Meeting != null) Meeting.SetSpeaking(slot, level); }
        public void SetSpotlight(int slot) { if (Meeting != null) Meeting.SetSpotlight(slot); }
        public void React(int slot, MeetingReaction reaction) { if (Meeting != null) Meeting.React(slot, reaction); }
        public void SetManagerTalking(bool talking) { if (Meeting != null) Meeting.SetManagerTalking(talking); }
        public void SetFired(int slot) { if (Meeting != null) Meeting.SetFired(slot); }
        public void SetProjectorTexture(Texture texture) { if (Meeting != null) Meeting.SetProjector(texture); }

        // ------------------------------------------------------------------ extras (not in the seam)

        /// <summary>Scripted camera for <see cref="PovMode.Cutscene"/>; without one the cutscene frames the fired/spotlit seat or the manager.</summary>
        public void SetCutscenePose(Vector3 position, Quaternion rotation, float fov)
        {
            _hasCutscene = true; _cutPos = position; _cutRot = rotation; _cutFov = fov;
        }

        public void ClearCutscenePose() { _hasCutscene = false; }

        /// <summary>Screen-space rect (pixels, origin bottom-left) of the monitor screen / phone / projector canvas, for UI anchoring.</summary>
        public bool TryGetScreenRect(PovMode framing, out Rect rect)
        {
            rect = default;
            Renderer r = framing == PovMode.Monitor ? Office.Screen : framing == PovMode.Phone ? (Office.Phone != null ? Office.Phone.GetComponentInChildren<Renderer>() : null)
                : framing == PovMode.MeetingProjector ? Meeting.Canvas : null;
            var cam = PovCamera;
            if (r == null || cam == null) return false;
            var b = r.bounds;
            Vector3 mn = new Vector3(float.MaxValue, float.MaxValue), mx = new Vector3(float.MinValue, float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                var s = cam.WorldToScreenPoint(c);
                if (s.z <= 0) return false;
                mn = Vector3.Min(mn, s); mx = Vector3.Max(mx, s);
            }
            rect = Rect.MinMaxRect(mn.x, mn.y, mx.x, mx.y);
            return true;
        }

        /// <summary>Look direction relative to the seat (test harness / scripted moments).</summary>
        public void SetLook(float yaw, float pitch) { if (Rig != null) Rig.SetLook(yaw, pitch); }

        /// <summary>Turn the seated head toward a world point (test harness / scripted moments).</summary>
        public void AimAt(Vector3 world)
        {
            Vector3 eye; float baseYaw;
            if (_place == Place.Meeting) { eye = _meetEye; baseYaw = _meetRot.eulerAngles.y; }
            else { eye = Office.Eye != null ? Office.Eye.position : Vector3.up * 1.24f; baseYaw = Office.Eye != null ? Office.Eye.eulerAngles.y : 0f; }
            Vector3 d = world - eye;
            float yaw = Mathf.DeltaAngle(baseYaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
            float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            if (Rig != null) Rig.SetLook(yaw, pitch);
        }

        /// <summary>Raise the click for whatever is under the crosshair (test harness).</summary>
        public void ClickHovered() { Click(); }

        // ------------------------------------------------------------------ frame

        bool LookMode => _mode == PovMode.Office || _mode == PovMode.Meeting;

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (ManageCursor)
            {
                bool lockIt = LookMode && Application.isFocused;
                Cursor.lockState = lockIt ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !lockIt;
            }
            bool inputOk = LookMode && (Cursor.lockState == CursorLockMode.Locked || ForceLookInput);
            Rig.UpdateLook(LookMode);
            Vector3 pos; Quaternion rot; float fov;
            TargetPose(out pos, out rot, out fov);
            Rig.Apply(pos, rot, fov, dt);
            var cam = Rig.Cam;
            if (_place == Place.Office)
            {
                Office.Tick(cam, _mode, dt);
            }
            else
            {
                Meeting.Tick(cam != null ? cam.transform.position : _meetEye, dt);
            }
            UpdateHover(inputOk && !Rig.Transitioning);
            if (inputOk && _hover != null && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && Cursor.lockState == CursorLockMode.Locked) Click();
            Rig.SetCrosshair(_entered && LookMode && (ForceLookInput || !Cursor.visible), _hover != null, dt);
            if (HoverMat != null && _hover != null)
            {
                var c = _hoverBase;
                c.a = _hoverBase.a * (0.65f + 0.35f * Mathf.Sin(Time.time * 6f));
                HoverMat.SetColor("_BaseColor", c);
            }
        }

        void TargetPose(out Vector3 pos, out Quaternion rot, out float fov)
        {
            fov = Rig.SettingsFov;
            float framingFov = 38f;
            var mode = _mode;
            if (_place == Place.Meeting && (mode == PovMode.Office || mode == PovMode.Monitor || mode == PovMode.Phone)) mode = PovMode.Meeting;
            if (_place == Place.Office && mode == PovMode.Meeting || _place == Place.Office && mode == PovMode.MeetingProjector) mode = PovMode.Office;
            switch (mode)
            {
                case PovMode.Monitor:
                    if (Frame(Office.Screen, 0.92f, framingFov, out pos, out rot)) { fov = framingFov; return; }
                    break;
                case PovMode.Phone:
                    if (Office.Phone != null)
                    {
                        var ph = Office.Phone;
                        Vector3 c = ph.TransformPoint(new Vector3(0f, 0.05f, 0f));
                        Vector3 back = Vector3.ProjectOnPlane(-ph.forward, Vector3.up).normalized;
                        Vector3 dir = (back * 0.62f + Vector3.up * 0.78f).normalized;
                        pos = c + dir * 0.5f;
                        rot = Quaternion.LookRotation(c - pos, Vector3.up);
                        fov = framingFov;
                        return;
                    }
                    break;
                case PovMode.MeetingProjector:
                    if (Frame(Meeting.Canvas, 0.94f, framingFov, out pos, out rot)) { fov = framingFov; return; }
                    break;
                case PovMode.Cutscene:
                    if (CutscenePose(out pos, out rot, out fov)) return;
                    break;
            }
            if (_place == Place.Meeting)
            {
                pos = _meetEye + Breath();
                rot = Quaternion.Euler(0f, _meetRot.eulerAngles.y + Rig.Yaw, 0f) * Quaternion.Euler(Rig.Pitch, 0f, 0f);
            }
            else
            {
                var eye = Office.Eye;
                pos = (eye != null ? eye.position : new Vector3(0f, 1.24f, 0.42f)) + Breath();
                float baseYaw = eye != null ? eye.eulerAngles.y : 0f;
                rot = Quaternion.Euler(0f, baseYaw + Rig.Yaw, 0f) * Quaternion.Euler(Rig.Pitch, 0f, 0f);
            }
        }

        static Vector3 Breath()
        {
            float t = Time.time;
            return new Vector3(Mathf.Sin(t * 0.37f) * 0.0025f, Mathf.Sin(t * 1.25f) * 0.003f, 0f);
        }

        bool Frame(Renderer surface, float fill, float vfov, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            if (surface == null) return false;
            var tr = surface.transform;
            var mf = surface.GetComponent<MeshFilter>();
            Vector3 size = mf != null && mf.sharedMesh != null ? Vector3.Scale(mf.sharedMesh.bounds.size, tr.lossyScale) : surface.bounds.size;
            float w = Mathf.Max(size.x, 0.01f), h = Mathf.Max(size.y, 0.01f);
            Vector3 center = surface.bounds.center;
            Vector3 normal = -tr.forward;
            float aspect = Rig.Cam != null ? Rig.Cam.aspect : 16f / 9f;
            float tanV = Mathf.Tan(vfov * 0.5f * Mathf.Deg2Rad);
            float dh = (h * 0.5f) / (tanV * fill);
            float dw = (w * 0.5f) / (tanV * aspect * fill);
            float d = Mathf.Max(dh, dw);
            pos = center + normal * d;
            rot = Quaternion.LookRotation(-normal, tr.up);
            return true;
        }

        bool CutscenePose(out Vector3 pos, out Quaternion rot, out float fov)
        {
            pos = Vector3.zero; rot = Quaternion.identity; fov = 42f;
            if (_hasCutscene) { pos = _cutPos; rot = _cutRot; fov = _cutFov; return true; }
            if (_place != Place.Meeting || Meeting == null) return false;
            // frame the fired / defending colleague, else the manager, pushed in from the local seat
            Vector3 eye = _meetEye;
            int subject = Meeting.CutsceneSubject;
            Vector3 focus;
            bool person = subject >= 0 && subject != _localSlot && Meeting.TryGetHead(subject, eye, out focus);
            if (!person)
            {
                // the manager, framed from the table in front of him (head and shoulders)
                if (Meeting.Manager == null) return false;
                var mt = Meeting.Manager.transform;
                focus = mt.position + Vector3.up * 1.05f;
                Vector3 fwd = Vector3.ProjectOnPlane(mt.forward, Vector3.up).normalized;
                pos = focus + fwd * 1.55f + Vector3.up * 0.12f + Vector3.Cross(Vector3.up, fwd) * 0.25f;
                rot = Quaternion.LookRotation(focus + Vector3.up * 0.08f - pos, Vector3.up);
                fov = 38f;
                return true;
            }
            Meeting.TryGetHead(subject, eye, out focus);
            pos = eye + (focus - eye) * 0.4f + Vector3.up * 0.04f;
            rot = Quaternion.LookRotation(focus - pos, Vector3.up);
            fov = 40f;
            return true;
        }

        void UpdateHover(bool allowed)
        {
            RoomTarget hit = null;
            var cam = Rig.Cam;
            if (allowed && cam != null)
            {
                var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                float reach = _place == Place.Office ? OfficeReach : MeetingReach;
                if (Physics.Raycast(ray, out var info, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
                {
                    var t = info.collider.GetComponentInParent<RoomTarget>();
                    if (t != null)
                    {
                        if (_place == Place.Office && t.Desk != DeskTarget.None) hit = t;
                        else if (_place == Place.Meeting && t.Seat >= 0)
                        {
                            int slot = Meeting.SlotAtSeat(t.Seat);
                            if (slot >= 0 && slot != _localSlot) hit = t;
                        }
                    }
                }
            }
            SetHover(hit);
        }

        void SetHover(RoomTarget t)
        {
            if (t == _hover) return;
            if (_hover != null) _hover.SetHover(false);
            _hover = t;
            if (_hover != null) _hover.SetHover(true);
            Rooms.Hovered = t != null && _place == Place.Office ? t.Desk : DeskTarget.None;
            Rooms.HoveredSeat = t != null && _place == Place.Meeting ? Meeting.SlotAtSeat(t.Seat) : -1;
            if (t == null && HoverMat != null) HoverMat.SetColor("_BaseColor", _hoverBase);
        }

        void Click()
        {
            if (_hover == null) return;
            if (_place == Place.Office && _hover.Desk != DeskTarget.None)
            {
                Office.Clicked(_hover.Desk);
                Rooms.RaiseDeskClicked(_hover.Desk);
            }
            else if (_place == Place.Meeting && _hover.Seat >= 0)
            {
                int slot = Meeting.SlotAtSeat(_hover.Seat);
                if (slot >= 0 && slot != _localSlot) Rooms.RaiseSeatClicked(slot);
            }
        }
    }
}
