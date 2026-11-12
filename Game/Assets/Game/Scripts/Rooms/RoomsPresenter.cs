using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// The v3 world: implements <see cref="IRoomPresenter"/> over the rooms scene (one private office personalised for
    /// the local player, one meeting room) and registers itself in <see cref="Rooms.Presenter"/>. Callers drive the
    /// place (EnterOffice / EnterMeeting) and the camera mode; the world only raises click events.
    /// v5 office: the player starts seated; WASD stands up and walks (<see cref="OfficeWalker"/>, local only), the
    /// crosshair hovers desk objects within reach and E / left click uses them; the computer, the phone and the cork
    /// board have their own camera poses (the computer and the phone sit you back down).
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
        public float OfficeReach = 2.4f, MeetingReach = 6f;

        /// <summary>Optional: let the world lock/unlock the cursor itself (UIRoot normally owns it).</summary>
        public static bool ManageCursor;
        /// <summary>Test harness: show the crosshair and allow hover/click while the cursor is free.</summary>
        public static bool ForceLookInput;
        /// <summary>Test harness / screenshots: walk input (strafe, forward) used instead of the keyboard.</summary>
        public static Vector2? ScriptedMove;
        /// <summary>False while something else owns the keyboard (text field focused, menus): no walking.</summary>
        public static bool AllowWalk = true;
        /// <summary>The UI used this frame's E (closed the folder, dropped the ballot): the world must not use it again.</summary>
        public static int UseConsumedFrame = -1;
        /// <summary>Fraction of the screen height to keep free under the projector when zoomed in (the results bar).</summary>
        public static float ProjectorRoomBelow;

        public static RoomsPresenter Instance { get; private set; }

        enum Place { Office, Meeting }

        Place _place = Place.Office;
        PovMode _mode = PovMode.Office;
        bool _entered;
        RoomTarget _hover;
        readonly RaycastHit[] _hits = new RaycastHit[16];
        Color _hoverBase;
        int _localSlot = -1;
        Vector3 _meetEye;
        Quaternion _meetRot = Quaternion.identity;
        // v5 office presence: seated at the desk, or standing and walking
        bool _seated = true;
        readonly OfficeWalker _walker = new OfficeWalker();
        float _stepNext, _bobPhase, _bobAmp;
        int _stepFoot;
        // cutscene pose (scripted camera)
        bool _hasCutscene;
        Vector3 _cutPos;
        Quaternion _cutRot;
        float _cutFov = 45f;

        public PovMode Mode => _mode;
        public Camera PovCamera => Rig != null ? Rig.Cam : null;
        public bool InMeeting => _place == Place.Meeting;
        /// <summary>Office: seated at the desk (false = standing / walking).</summary>
        public bool Seated => _seated;
        public OfficeWalker Walker => _walker;

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
                SitDown(false);
                Rig.Snap();
                if (Lighting != null) Lighting.RefreshProbes();
            }
        }

        // ------------------------------------------------------------------ v5: standing and sitting

        /// <summary>Get up from the desk chair (the chair rolls back); the view keeps its direction.</summary>
        public void StandUp()
        {
            if (!_seated || _place != Place.Office) return;
            _seated = false;
            float yaw = (Office.Eye != null ? Office.Eye.eulerAngles.y : 0f) + Rig.Yaw;
            float pitch = Rig.Pitch;
            Office.SetStanding(true);
            _walker.Place(Office.StandSpot, Office);
            Rig.SetLimits(100000f, 70f, 72f);
            Rig.SetLook(Mathf.Repeat(yaw + 180f, 360f) - 180f, Mathf.Clamp(pitch, -20f, 45f));
            Rig.BeginTransition();
            _stepNext = _walker.Travelled + 0.35f;
        }

        /// <summary>Sit back down at the desk, facing the monitor.</summary>
        public void SitDown(bool transition = true)
        {
            bool was = _seated;
            _seated = true;
            Office.SetStanding(false);
            Rig.SetLimits(Office.YawLimit, Office.PitchUp, Office.PitchDown);
            if (!was || !transition) Rig.SetLook(0f, Office.EyePitch);
            if (!was && transition) Rig.BeginTransition();
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
            // v5 step 3b: about ±55° each way, and as far as the projector on its side (every seat can turn to it)
            float pyaw, ppitch, ymin = -Meeting.YawLimit, ymax = Meeting.YawLimit;
            if (Meeting.ProjectorAngles(localSlot, out pyaw, out ppitch))
            {
                if (pyaw > 0f) ymax = Mathf.Max(ymax, Mathf.Min(pyaw + 12f, 100f));
                else ymin = Mathf.Min(ymin, Mathf.Max(pyaw - 12f, -100f));
            }
            Rig.SetLimits(ymin, ymax, Meeting.PitchUp, Meeting.PitchDown);
            if (switched)
            {
                Rig.SetLook(0f, 6f);
                Rig.Snap();
                if (Lighting != null) Lighting.RefreshProbes();
            }
        }

        /// <summary>v5 step 3b: turn the seated view toward the projector (and say how far that is).</summary>
        public bool TryGetProjectorLook(out float yaw, out float pitch)
        {
            yaw = 0f; pitch = 0f;
            return _place == Place.Meeting && Meeting != null && Meeting.ProjectorAngles(_localSlot, out yaw, out pitch);
        }

        public void SetPovMode(PovMode mode)
        {
            if (mode == _mode) return;
            var old = _mode;
            _mode = mode;
            // the computer and the phone are used from the chair: coming back from them you sit at the desk
            if (_place == Place.Office && (mode == PovMode.Monitor || mode == PovMode.Phone)) SitDown(false);
            if (_place == Place.Office && mode == PovMode.Office && (old == PovMode.Monitor || old == PovMode.Phone)) Rig.SetLook(0f, Office.EyePitch);
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
                : framing == PovMode.MeetingProjector ? Meeting.Canvas : framing == PovMode.Board ? Office.CorkSurface
                : framing == PovMode.Roster ? Office.RosterSheet : null;
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

        /// <summary>v5 step 3a shared gaze: where the local camera looks relative to the meeting seat (yaw right +,
        /// pitch down +); false outside the meeting room.</summary>
        public bool TryGetMeetingLook(out float yaw, out float pitch)
        {
            yaw = 0f; pitch = 0f;
            var cam = PovCamera;
            if (_place != Place.Meeting || cam == null) return false;
            Vector3 f = Quaternion.Inverse(_meetRot) * cam.transform.forward;
            float flat = new Vector2(f.x, f.z).magnitude;
            yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            pitch = flat > 1e-4f ? -Mathf.Atan2(f.y, flat) * Mathf.Rad2Deg : 0f;
            return true;
        }

        /// <summary>Look direction relative to the seat (test harness / scripted moments).</summary>
        public void SetLook(float yaw, float pitch) { if (Rig != null) Rig.SetLook(yaw, pitch); }

        /// <summary>Turn the head toward a world point (test harness / scripted moments).</summary>
        public void AimAt(Vector3 world)
        {
            Vector3 eye; float baseYaw;
            if (_place == Place.Meeting) { eye = _meetEye; baseYaw = _meetRot.eulerAngles.y; }
            else if (!_seated) { eye = Office.transform.TransformPoint(new Vector3(_walker.Position.x, Office.StandEye, _walker.Position.y)); baseYaw = 0f; }
            else { eye = Office.Eye != null ? Office.Eye.position : Vector3.up * 1.24f; baseYaw = Office.Eye != null ? Office.Eye.eulerAngles.y : 0f; }
            Vector3 d = world - eye;
            float yaw = Mathf.DeltaAngle(baseYaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
            float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            if (Rig != null) Rig.SetLook(yaw, pitch);
        }

        /// <summary>Raise the click for whatever is under the crosshair (test harness).</summary>
        public void ClickHovered() { Click(); }

        /// <summary>A new match in the office: seated at the desk facing the monitor, no blend.</summary>
        public void ResetOffice()
        {
            if (_place != Place.Office) return;
            _seated = false;
            SitDown(false);
            Rig.Snap();
        }

        /// <summary>Test harness / screenshots: stand at an office spot (x, z) looking along yaw/pitch.</summary>
        public void StandAt(Vector2 spot, float yaw, float pitch)
        {
            if (_place != Place.Office) return;
            if (_seated) StandUp();
            _walker.Place(spot, Office);
            Rig.SetLook(yaw, pitch);
            Rig.Snap();
        }

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
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            bool inputOk = LookMode && (locked || ForceLookInput);
            Rig.UpdateLook(LookMode);
            if (_place == Place.Office) Walk(inputOk && (locked || ScriptedMove.HasValue) && _mode == PovMode.Office, dt);
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
                Meeting.TickProps(cam, dt);
            }
            // no hover while the camera eases (also: the E that closed the board must not reopen it)
            UpdateHover(inputOk && !Rig.Transitioning);
            if (inputOk && _hover != null && (locked || ForceLookInput) && UsePressed()) Click();
            Rig.SetCrosshair(_entered && LookMode && (ForceLookInput || !Cursor.visible), _hover != null, dt);
            if (HoverMat != null && _hover != null)
            {
                var c = _hoverBase * (0.78f + 0.22f * Mathf.Sin(Time.time * 6f));
                c.a = 1f;
                HoverMat.SetColor("_BaseColor", c);
            }
        }

        /// <summary>E or a left click (only while the cursor is locked: a free cursor belongs to the UI).</summary>
        static bool UsePressed()
        {
            if (!AllowWalk || Time.frameCount == UseConsumedFrame) return false;
            bool click = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && Cursor.lockState == CursorLockMode.Locked;
            bool e = GameInput.Interact != null && GameInput.Interact.WasPressedThisFrame();
            return click || e;
        }

        /// <summary>WASD: the first key press stands you up, then you walk (local only).</summary>
        void Walk(bool allowed, float dt)
        {
            Vector2 input = Vector2.zero;
            if (allowed && (AllowWalk || ScriptedMove.HasValue))
            {
                if (ScriptedMove.HasValue) input = ScriptedMove.Value;
                else if (GameInput.Move != null) input = GameInput.Move.ReadValue<Vector2>();
            }
            if (_seated)
            {
                if (input.sqrMagnitude > 0.04f) StandUp();
                _bobAmp = 0f;
                return;
            }
            Vector2? tether = null;
            if (Office.HandsetUp && Office.Phone != null)
            {
                var ph = Office.transform.InverseTransformPoint(Office.Phone.position);
                tether = new Vector2(ph.x, ph.z);
                // answered from across the room: you go back to the desk (the cord does not reach)
                if ((_walker.Position - tether.Value).magnitude > _walker.CordReach + 0.05f) { SitDown(); return; }
            }
            float yaw = Rig.Yaw;
            _walker.Step(input, yaw, dt, Office, tether);
            float speed = _walker.Velocity.magnitude;
            _bobAmp = Mathf.MoveTowards(_bobAmp, Mathf.Clamp01(speed / Mathf.Max(0.1f, _walker.Speed)), dt * 4f);
            _bobPhase = _walker.Travelled * Mathf.PI / 0.62f;
            if (_walker.Travelled >= _stepNext)
            {
                _stepNext = _walker.Travelled + 0.62f;
                if (speed > 0.4f) Sfx.Play(_stepFoot++ % 2 == 0 ? "step_a" : "step_b", 0.32f);
            }
        }

        void TargetPose(out Vector3 pos, out Quaternion rot, out float fov)
        {
            fov = Rig.SettingsFov;
            float framingFov = 38f;
            var mode = _mode;
            if (_place == Place.Meeting && (mode == PovMode.Office || mode == PovMode.Monitor || mode == PovMode.Phone)) mode = PovMode.Meeting;
            if (_place == Place.Office && (mode == PovMode.Meeting || mode == PovMode.MeetingProjector || mode == PovMode.MeetingBoard)) mode = PovMode.Office;
            switch (mode)
            {
                case PovMode.Monitor:
                    if (Frame(Office.Screen, 0.92f, framingFov, out pos, out rot)) { fov = framingFov; return; }
                    break;
                case PovMode.Phone:
                    if (Office.Phone != null && Office.Eye != null)
                    {
                        // v5: from the chair, turned toward the phone; the phone sits right of centre so the phone
                        // panel (left half of the screen) does not cover it, the lifted handset is at your ear
                        Vector3 eye = Office.Eye.position;
                        Vector3 c = Office.Phone.TransformPoint(new Vector3(0f, 0.05f, 0f));
                        Vector3 d = c - eye;
                        float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg - 15f;
                        float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg - 3f;
                        pos = eye + Breath();
                        rot = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(pitch, 0f, 0f);
                        fov = 54f;
                        return;
                    }
                    break;
                case PovMode.Board:
                    if (Frame(Office.CorkSurface, 0.9f, framingFov, out pos, out rot)) { fov = framingFov; return; }
                    break;
                case PovMode.CashBox:
                    if (CashBoxPose(out pos, out rot, out fov)) return;
                    break;
                case PovMode.Roster:
                    if (RosterPose(out pos, out rot, out fov)) return;
                    break;
                case PovMode.Drawer:
                    if (DrawerPose(out pos, out rot, out fov)) return;
                    break;
                case PovMode.MeetingProjector:
                {
                    // (3b polish) with room kept below (the results bar) the screen is framed a little smaller and higher
                    float room = Mathf.Clamp(ProjectorRoomBelow, 0f, 0.3f);
                    if (Frame(Meeting.Canvas, 0.94f * (1f - room), framingFov, out pos, out rot))
                    {
                        if (room > 0f)
                        {
                            float d = Vector3.Distance(pos, Meeting.Canvas.bounds.center);
                            float up = 0.95f - 0.94f * (1f - room); // in half-heights of the view
                            pos += rot * Vector3.down * (up * d * Mathf.Tan(framingFov * 0.5f * Mathf.Deg2Rad));
                        }
                        fov = framingFov;
                        return;
                    }
                    break;
                }
                case PovMode.MeetingBoard:
                    // v5 step 3a: the whiteboard across the table (the heads in front of it stay in the lower edge)
                    if (Frame(Meeting.BoardFor(_localSlot), 0.9f, framingFov, out pos, out rot)) { fov = framingFov; pos += Breath() * 0.5f; return; }
                    break;
                case PovMode.Cutscene:
                    if (CutscenePose(out pos, out rot, out fov)) return;
                    break;
            }
            if (_place == Place.Meeting)
            {
                // v5 step 3b: the local defender stands up (the eye rises, a step back from the table); turned far to one
                // side (the projector, the head or the foot of the table) you lean in over the table to see past the
                // colleague beside you
                float stand = Meeting != null ? Meeting.LocalStand : 0f;
                float lean = Mathf.Clamp01((Mathf.Abs(Rig.Yaw) - 40f) / 35f);
                lean = lean * lean * (3f - 2f * lean) * (1f - stand);
                pos = _meetEye + Breath() + Vector3.up * (Meeting != null ? Meeting.StandEyeUp * stand : 0f) + (_meetRot * Vector3.forward) * (0.28f * lean - 0.08f * stand)
                      + Vector3.down * (0.03f * lean);
                rot = Quaternion.Euler(0f, _meetRot.eulerAngles.y + Rig.Yaw, 0f) * Quaternion.Euler(Rig.Pitch, 0f, 0f);
            }
            else if (!_seated)
            {
                // standing: the walker's spot at eye height, a very subtle step bob
                var w = _walker.Position;
                float bobY = Mathf.Abs(Mathf.Sin(_bobPhase)) * 0.012f * _bobAmp - 0.006f * _bobAmp;
                float bobX = Mathf.Sin(_bobPhase) * 0.004f * _bobAmp;
                Quaternion yawRot = Quaternion.Euler(0f, Rig.Yaw, 0f);
                pos = Office.transform.TransformPoint(new Vector3(w.x, Office.StandEye + bobY, w.y)) + yawRot * new Vector3(bobX, 0f, 0f) + Breath();
                rot = Office.transform.rotation * yawRot * Quaternion.Euler(Rig.Pitch, 0f, 0f);
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

        /// <summary>v5 step 2: leaning over the Petty Cash box (in front of it, looking down into the open lid); the box
        /// sits in the upper part of the view so the box card fits underneath.</summary>
        bool CashBoxPose(out Vector3 pos, out Quaternion rot, out float fov)
        {
            pos = Vector3.zero; rot = Quaternion.identity; fov = 44f;
            var b = Office.CashBox;
            if (b == null || !b.gameObject.activeInHierarchy) return false;
            // frame the box and the spot beside it where taken notes lie
            Vector3 c = b.position + Vector3.up * 0.05f;
            if (Office.CashPile != null) c = Vector3.Lerp(c, Office.CashPile.position + Vector3.up * 0.05f, 0.3f);
            Vector3 front = -b.forward; front.y = 0f;
            front = front.sqrMagnitude > 1e-4f ? front.normalized : Vector3.back;
            pos = c + front * 0.34f + Vector3.up * 0.33f + Breath() * 0.5f;
            Vector3 aim = c + Vector3.down * 0.07f + front * 0.03f;
            rot = Quaternion.LookRotation(aim - pos, Vector3.up);
            return true;
        }

        /// <summary>v5 step 2: in front of the roster; the sheet sits right of centre (the roster card is on the left).</summary>
        bool RosterPose(out Vector3 pos, out Quaternion rot, out float fov)
        {
            fov = 36f;
            if (!Frame(Office.RosterSheet, 0.7f, fov, out pos, out rot)) return false;
            var tr = Office.RosterSheet.transform;
            float dist = Vector3.Distance(pos, Office.RosterSheet.bounds.center);
            float aspect = Rig.Cam != null ? Rig.Cam.aspect : 16f / 9f;
            float halfH = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * dist, halfW = halfH * aspect;
            // the sheet right of centre (the card is on the left) and a little low (the clip clears the top strip)
            pos -= tr.right * (halfW * 0.3f);
            pos += tr.up * (halfH * 0.1f);
            pos += Breath() * 0.5f;
            return true;
        }

        /// <summary>v5 step 2: looking down into the open drawer (follows it while it slides out).</summary>
        bool DrawerPose(out Vector3 pos, out Quaternion rot, out float fov)
        {
            pos = Vector3.zero; rot = Quaternion.identity; fov = 44f;
            var spot = Office.DrawerCash != null ? Office.DrawerCash : Office.Drawer;
            if (spot == null) return false;
            Vector3 c = spot.position + Vector3.up * 0.02f;
            pos = c + Office.transform.rotation * new Vector3(-0.17f, 0.47f, -0.27f) + Breath() * 0.5f;
            Vector3 aim = c + Office.transform.rotation * new Vector3(0.0f, -0.035f, 0.0f);
            rot = Quaternion.LookRotation(aim - pos, Vector3.up);
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
                focus = mt.position + Vector3.up * 1.15f;
                Vector3 fwd = Vector3.ProjectOnPlane(mt.forward, Vector3.up).normalized;
                pos = focus + fwd * 2.3f + Vector3.up * 0.02f + Vector3.Cross(Vector3.up, fwd) * 0.35f;
                rot = Quaternion.LookRotation(focus - pos, Vector3.up);
                fov = 38f;
                return true;
            }
            Meeting.TryGetHead(subject, eye, out focus);
            focus += Vector3.down * 0.22f;
            pos = eye + (focus - eye) * 0.3f + Vector3.up * 0.04f;
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
                // nearest usable target along the ray (the seated chair's box must not hide the desk below it)
                int n = Physics.RaycastNonAlloc(ray, _hits, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
                float best = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    var info = _hits[i];
                    if (info.distance >= best) continue;
                    var t = info.collider.GetComponentInParent<RoomTarget>();
                    if (t == null) continue;
                    bool ok = false;
                    if (_place == Place.Office) ok = OfficeTargetOk(t.Desk);
                    else if (_place == Place.Meeting && t.Meet != MeetingTarget.None) ok = MeetingTargetOk(t.Meet);
                    else if (_place == Place.Meeting && t.Seat >= 0)
                    {
                        int slot = Meeting.SlotAtSeat(t.Seat);
                        ok = slot >= 0 && slot != _localSlot;
                    }
                    if (!ok) continue;
                    best = info.distance;
                    hit = t;
                }
            }
            SetHover(hit);
        }

        /// <summary>v5 step 3a: the folder while it lies on the table, the ballot paper while it lies there, the whiteboards;
        /// step 3b: the projector screen.</summary>
        bool MeetingTargetOk(MeetingTarget m)
        {
            switch (m)
            {
                case MeetingTarget.Folder: return !Meeting.FolderHeld && Meeting.FolderProgress < 0.05f;
                case MeetingTarget.Ballot: return Meeting.BallotShown && !Meeting.BallotHeld && !Meeting.BallotDropping;
                case MeetingTarget.Board:
                case MeetingTarget.Projector: return true;
                default: return false;
            }
        }

        /// <summary>Things you can use in the office (the window is only scenery; the chair only while standing).</summary>
        bool OfficeTargetOk(DeskTarget d)
        {
            switch (d)
            {
                case DeskTarget.None:
                case DeskTarget.Window: return false;
                case DeskTarget.Chair: return !_seated;
                default: return true;
            }
        }

        void SetHover(RoomTarget t)
        {
            if (t == _hover) return;
            if (_hover != null) _hover.SetHover(false);
            _hover = t;
            if (_hover != null) _hover.SetHover(true);
            Rooms.Hovered = t != null && _place == Place.Office ? t.Desk : DeskTarget.None;
            Rooms.HoveredSeat = t != null && _place == Place.Meeting && t.Meet == MeetingTarget.None ? Meeting.SlotAtSeat(t.Seat) : -1;
            Rooms.HoveredMeeting = t != null && _place == Place.Meeting ? t.Meet : MeetingTarget.None;
            if (t == null && HoverMat != null) HoverMat.SetColor("_BaseColor", _hoverBase);
        }

        void Click()
        {
            if (_hover == null) return;
            if (_place == Place.Office && _hover.Desk != DeskTarget.None)
            {
                var desk = _hover.Desk;
                Office.Clicked(desk);
                if (desk == DeskTarget.Chair) SitDown();
                Rooms.RaiseDeskClicked(desk);
            }
            else if (_place == Place.Meeting && _hover.Meet != MeetingTarget.None) Rooms.RaiseMeetingClicked(_hover.Meet);
            else if (_place == Place.Meeting && _hover.Seat >= 0)
            {
                int slot = Meeting.SlotAtSeat(_hover.Seat);
                if (slot >= 0 && slot != _localSlot) Rooms.RaiseSeatClicked(slot);
            }
        }
    }
}
