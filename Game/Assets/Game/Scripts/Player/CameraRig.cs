using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v2 camera: a close third-person follow camera (mouse orbit, scroll zoom within limits, damped focus that dips
    /// when crouching) that fades walls between itself and the avatar (<see cref="CameraOcclusion"/>) instead of
    /// swinging around them, plus fixed shots for the menu, the HR meeting and the results — both derived from the
    /// layout's meeting room. Juice API: <see cref="Shake"/> (trauma), <see cref="Punch"/> (spring kick) and
    /// <see cref="FovKick"/>. The audio listener rides on the avatar's head in follow mode so 3D sounds and voice
    /// proximity are heard from where the character stands.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraRig : MonoBehaviour
    {
        public static CameraRig I { get; private set; }

        public enum Mode { Menu, Follow, Meeting, Results }
        public Mode Current = Mode.Menu;

        [Header("Follow")]
        public float Distance = 3.6f;
        public float MinDistance = 2.2f, MaxDistance = 6.0f;
        public float ZoomStep = 0.45f;
        public float Pitch = 24f, MinPitch = 4f, MaxPitch = 62f;
        public float Yaw;
        public float Sensitivity = 0.12f;
        /// <summary>Focus height above the avatar's feet (characters are ~1.25 m tall: aim at the face).</summary>
        public float TargetHeight = 1.05f;
        public float CrouchTargetHeight = 0.7f;
        /// <summary>Sideways focus offset (0 = centred; ~0.35 = over the right shoulder).</summary>
        public float ShoulderOffset = 0f;
        /// <summary>Focus smoothing time (s). Rotation is never smoothed (mouse must feel direct).</summary>
        public float FocusDamping = 0.07f;

        [Header("Juice")]
        public float MaxShakeOffset = 0.12f;
        public float MaxShakeAngle = 2.2f;
        public float TraumaDecay = 1.4f;
        /// <summary>Player setting "settings.camera_shake" (0..1) scales all juice.</summary>
        public float JuiceScale = 1f;

        Transform _target;
        Camera _cam;
        CameraOcclusion _occlusion;
        Transform _listener;
        float _baseFov;
        float _menuAngle;
        Vector3 _focus, _focusVel;
        bool _focusValid;
        float _distance, _pulledDistance;
        float _crouchBlend;
        Mode _lastMode = Mode.Menu;

        float _trauma;
        Vector3 _punch, _punchVel;
        float _fovKick;
        float _noiseSeed;

        bool _meetingPoseReady;
        Vector3 _meetingPos, _meetingLook, _meetingCentre, _roomMin, _roomMax;

        void Awake()
        {
            I = this;
            _cam = GetComponent<Camera>();
            _baseFov = _cam.fieldOfView;
            Sensitivity = PlayerPrefs.GetFloat("settings.sensitivity", 0.12f);
            JuiceScale = PlayerPrefs.GetFloat("settings.camera_shake", 1f);
            Distance = Mathf.Clamp(PlayerPrefs.GetFloat("settings.camera_distance", Distance), MinDistance, MaxDistance);
            _distance = _pulledDistance = Distance;
            _noiseSeed = Random.value * 100f;
            _occlusion = GetComponent<CameraOcclusion>();
            if (_occlusion == null) _occlusion = gameObject.AddComponent<CameraOcclusion>();
            SetupListener();
        }

        void SetupListener()
        {
            if (LaunchArgs.Current.Bot) return;
            var old = GetComponent<AudioListener>();
            if (old != null) { old.enabled = false; Destroy(old); }
            var go = new GameObject("AudioListener");
            go.AddComponent<AudioListener>();
            _listener = go.transform;
            _listener.SetPositionAndRotation(transform.position, transform.rotation);
        }

        /// <summary>Where 3D sound is heard from (avatar head in follow mode, otherwise the camera).</summary>
        public static Vector3 ListenerPosition => I != null && I._listener != null ? I._listener.position : (I != null ? I.transform.position : Vector3.zero);

        public void SetTarget(Transform t)
        {
            if (_target != t && t != null) { Yaw = t.eulerAngles.y; _focusValid = false; }
            _target = t;
        }

        // ------------------------------------------------------------------ juice

        /// <summary>Adds camera trauma (0..1). Shake = trauma², decays over ~1 s.</summary>
        public void Shake(float amount) { _trauma = Mathf.Clamp01(_trauma + amount * JuiceScale); }

        /// <summary>Spring kick in camera space (e.g. Vector3.back for a recoil, Vector3.down for a thud).</summary>
        public void Punch(Vector3 localDirection, float amount) { _punchVel += localDirection.normalized * amount * 2.5f * JuiceScale; }

        /// <summary>Temporary field-of-view change in degrees (positive widens), eased back.</summary>
        public void FovKick(float degrees) { _fovKick += degrees * JuiceScale; }

        // ------------------------------------------------------------------ per frame

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            var view = ClientState.I != null ? ClientState.I.View : null;
            if (view == null) Current = Mode.Menu;
            else if (view.Phase >= MatchPhase.MeetingDiscussion && view.Phase <= MatchPhase.MeetingVerdict) Current = Mode.Meeting;
            else if (view.Phase == MatchPhase.Results || view.Phase == MatchPhase.Abandoned) Current = Mode.Results;
            else Current = Mode.Follow;
            if (Current == Mode.Follow && PlayerAvatar.Local != null) SetTarget(PlayerAvatar.Local.transform);
            if (Current != _lastMode)
            {
                if (_lastMode == Mode.Follow) _occlusion.ClearAll();
                _lastMode = Current;
            }

            Vector3 pos; Quaternion rot;
            bool follow = Current == Mode.Follow && _target != null;
            if (follow) FollowPose(dt, out pos, out rot);
            else if (Current == Mode.Meeting) MeetingPose(out pos, out rot);
            else if (Current == Mode.Results) ResultsPose(view, dt, out pos, out rot);
            else MenuPose(dt, out pos, out rot);

            if (!follow)
            {
                float k = 1f - Mathf.Exp(-3f * dt);
                pos = Vector3.Lerp(transform.position, pos, k);
                rot = Quaternion.Slerp(transform.rotation, rot, k);
            }
            ApplyJuice(dt, ref pos, ref rot);
            transform.SetPositionAndRotation(pos, rot);

            if (_listener != null)
            {
                Vector3 lp = follow ? _focus : pos;
                _listener.SetPositionAndRotation(lp, Quaternion.Euler(0f, rot.eulerAngles.y, 0f));
            }
        }

        void ApplyJuice(float dt, ref Vector3 pos, ref Quaternion rot)
        {
            // Spring for punches (critically-ish damped).
            _punchVel += (-_punch * 140f - _punchVel * 15f) * dt;
            _punch += _punchVel * dt;
            pos += rot * _punch;

            if (_trauma > 0.0001f)
            {
                float s = _trauma * _trauma;
                float t = Time.time * 22f;
                pos += rot * new Vector3(Noise(t, 0f), Noise(t, 1f), 0f) * (s * MaxShakeOffset);
                rot *= Quaternion.Euler(Noise(t, 2f) * s * MaxShakeAngle, Noise(t, 3f) * s * MaxShakeAngle, Noise(t, 4f) * s * MaxShakeAngle * 0.6f);
                _trauma = Mathf.Max(0f, _trauma - TraumaDecay * dt);
            }

            _fovKick *= Mathf.Exp(-5f * dt);
            float fov = _baseFov + _fovKick;
            if (Mathf.Abs(_cam.fieldOfView - fov) > 0.01f) _cam.fieldOfView = fov;
        }

        float Noise(float t, float channel) { return Mathf.PerlinNoise(_noiseSeed + channel * 17.3f, t) * 2f - 1f; }

        void FollowPose(float dt, out Vector3 pos, out Quaternion rot)
        {
            var lpc = LocalPlayerController.I;
            bool wheel = lpc != null && lpc.EmoteWheelOpen;
            bool look = Cursor.lockState == CursorLockMode.Locked && !wheel;
            if (look && GameInput.Look != null)
            {
                Vector2 d = GameInput.Look.ReadValue<Vector2>();
                Yaw += d.x * Sensitivity;
                Pitch = Mathf.Clamp(Pitch - d.y * Sensitivity, MinPitch, MaxPitch);
            }
            if (GameInput.Zoom != null && !UIRoot.PointerOverUI && !wheel)
            {
                float z = GameInput.Zoom.ReadValue<float>();
                if (Mathf.Abs(z) > 0.01f)
                {
                    Distance = Mathf.Clamp(Distance - Mathf.Sign(z) * ZoomStep, MinDistance, MaxDistance);
                    PlayerPrefs.SetFloat("settings.camera_distance", Distance);
                }
            }

            bool crouched = OwnerNetworkTransform.IsCrouched(_target.gameObject);
            _crouchBlend = Mathf.MoveTowards(_crouchBlend, crouched ? 1f : 0f, dt * 5f);
            Vector3 rawFocus = _target.position + Vector3.up * Mathf.Lerp(TargetHeight, CrouchTargetHeight, Smooth(_crouchBlend));
            if (!_focusValid || (rawFocus - _focus).sqrMagnitude > 16f)
            {
                _focus = rawFocus;
                _focusVel = Vector3.zero;
                _focusValid = true;
                _pulledDistance = Distance;
            }
            else _focus = Vector3.SmoothDamp(_focus, rawFocus, ref _focusVel, FocusDamping);

            _distance = Mathf.Lerp(_distance, Distance, 1f - Mathf.Exp(-10f * dt));
            rot = Quaternion.Euler(Pitch, Yaw, 0f);
            Vector3 back = rot * Vector3.back;
            Vector3 pivot = _focus + rot * Vector3.right * ShoulderOffset;
            float allowed = _occlusion.Resolve(pivot, back, _distance, _target.position + Vector3.up * 0.55f);
            // Snap in front of solid walls immediately; ease back out.
            _pulledDistance = allowed < _pulledDistance ? allowed : Mathf.Lerp(_pulledDistance, allowed, 1f - Mathf.Exp(-4f * dt));
            pos = pivot + back * _pulledDistance;
        }

        static float Smooth(float x) { return x * x * (3f - 2f * x); }

        void MenuPose(float dt, out Vector3 pos, out Quaternion rot)
        {
            _menuAngle += dt * 4f;
            var l = GameConfig.Layout;
            Vector3 c = l != null ? (OfficeWorld.V(l.BoundsMin) + OfficeWorld.V(l.BoundsMax)) * 0.5f : new Vector3(-1f, 0.5f, -1.5f);
            c.y = 0.5f;
            pos = c + Quaternion.Euler(0, _menuAngle, 0) * new Vector3(0, 13f, -19f);
            rot = Quaternion.LookRotation(c - pos);
        }

        // ------------------------------------------------------------------ meeting / results (from the layout)

        void EnsureMeetingPose()
        {
            if (_meetingPoseReady) return;
            _meetingPoseReady = true;
            var l = GameConfig.Layout;
            Vector3 centre = new Vector3(6.5f, 0f, 4.9f);
            Vector3 manager = centre + Vector3.left * 3f;
            if (l != null && l.MeetingSeats.Count > 0)
            {
                centre = Vector3.zero;
                for (int i = 0; i < l.MeetingSeats.Count; i++) centre += OfficeWorld.V(l.MeetingSeats[i].Position);
                centre /= l.MeetingSeats.Count;
                manager = OfficeWorld.V(l.ManagerMeetingSpot);
            }
            Vector3 away = centre - manager; away.y = 0;
            if (away.sqrMagnitude < 0.01f) away = Vector3.back;
            away.Normalize();
            _meetingCentre = centre;
            _meetingPos = centre + away * 4.0f + Vector3.up * 2.5f;
            _meetingLook = Vector3.Lerp(centre, manager, 0.35f) + Vector3.up * 0.8f;
            _roomMin = new Vector3(-999f, 0f, -999f);
            _roomMax = new Vector3(999f, 0f, 999f);
            var room = l != null ? l.RoomAt(new Vec3(centre.x, 0f, centre.z)) : null;
            if (room != null)
            {
                _roomMin = OfficeWorld.V(room.Min) + new Vector3(0.45f, 0f, 0.45f);
                _roomMax = OfficeWorld.V(room.Max) - new Vector3(0.45f, 0f, 0.45f);
                _meetingPos = ClampXZ(_meetingPos);
            }
        }

        Vector3 ClampXZ(Vector3 p)
        {
            p.x = Mathf.Clamp(p.x, _roomMin.x, _roomMax.x);
            p.z = Mathf.Clamp(p.z, _roomMin.z, _roomMax.z);
            return p;
        }

        void MeetingPose(out Vector3 pos, out Quaternion rot)
        {
            EnsureMeetingPose();
            pos = _meetingPos;
            rot = Quaternion.LookRotation(_meetingLook - pos);
        }

        void ResultsPose(PlayerView view, float dt, out Vector3 pos, out Quaternion rot)
        {
            EnsureMeetingPose();
            Vector3 c = _meetingCentre + Vector3.up * 0.6f;
            if (view != null && view.Result != null)
            {
                var a = PlayerAvatar.BySlot(view.Result.FiredPlayer);
                if (a != null) c = a.transform.position + Vector3.up * 0.7f;
            }
            // Slow sway inside the meeting room; aim right of the fired player so they stay visible beside the
            // results panel on the right side of the screen.
            _menuAngle += dt;
            Vector3 fromCam = _meetingPos - _meetingCentre; fromCam.y = 0;
            Vector3 side = Vector3.Cross(Vector3.up, fromCam.normalized);
            pos = Vector3.Lerp(_meetingPos, c, 0.3f) + side * (Mathf.Sin(_menuAngle * 0.4f) * 0.6f);
            pos.y = 2.4f;
            pos = ClampXZ(pos);
            Vector3 right = Quaternion.LookRotation(c - pos) * Vector3.right;
            rot = Quaternion.LookRotation(c + right * 1.1f - pos);
        }
    }
}
