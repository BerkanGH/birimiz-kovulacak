using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Seated first-person camera. The presenter hands it a target pose every frame; the rig follows it exactly in the
    /// look modes and blends with an under-damped spring (slight overshoot) whenever the mode or the place changes.
    /// Mouse-look runs only while the cursor is locked (UIRoot owns the lock) and the mode is Office/Meeting.
    /// Settings come from the PlayerPrefs keys in <see cref="Rooms"/> (sensitivity multiplier, invert Y 0/1,
    /// vertical FOV in degrees). Also owns the UI Toolkit crosshair.
    /// </summary>
    public sealed class PovCameraRig : MonoBehaviour
    {
        public Camera Cam;
        public PanelSettings Panel;
        public float DefaultFov = 60f;
        /// <summary>Degrees per mouse pixel at sensitivity 1.</summary>
        public float DegreesPerPixel = 0.075f;

        public float Yaw { get; private set; }
        public float Pitch { get; private set; }
        public bool Transitioning => _blending;
        /// <summary>0..1 (with a little overshoot) while blending to a new pose, 1 when settled.</summary>
        public float BlendProgress => _blending ? _p : 1f;

        float _yawMin = -110f, _yawMax = 110f, _pitchUp = 40f, _pitchDown = 60f;
        float _sens = 1f, _fov = 60f;
        bool _invert;
        float _nextPrefs;

        // spring blend from the pose at the last mode change to the live target
        bool _blending;
        float _p, _v;
        Vector3 _fromPos;
        Quaternion _fromRot;
        float _fromFov;
        const float Omega = 10.5f, Zeta = 0.74f;

        // crosshair
        VisualElement _dot;
        bool _crossShown = true, _crossHover;
        float _crossScale = 1f, _crossPx = -1f;

        public float SettingsFov => _fov;

        void Awake()
        {
            if (Cam == null) Cam = GetComponent<Camera>();
            ReadPrefs();
        }

        void Start() { BuildCrosshair(); }

        public void ReadPrefs()
        {
            _sens = Mathf.Clamp(PlayerPrefs.GetFloat(Rooms.PrefSensitivity, 1f), 0.05f, 6f);
            _invert = PlayerPrefs.GetInt(Rooms.PrefInvertY, 0) != 0;
            _fov = Mathf.Clamp(PlayerPrefs.GetFloat(Rooms.PrefFov, DefaultFov), 40f, 95f);
            _nextPrefs = Time.unscaledTime + 0.5f;
        }

        public void SetLimits(float yawLimit, float pitchUp, float pitchDown) { SetLimits(-yawLimit, yawLimit, pitchUp, pitchDown); }

        /// <summary>v5 step 3b: yaw limits may differ per side (a meeting seat turns further toward the projector).</summary>
        public void SetLimits(float yawMin, float yawMax, float pitchUp, float pitchDown)
        {
            _yawMin = yawMin; _yawMax = yawMax; _pitchUp = pitchUp; _pitchDown = pitchDown;
            Yaw = Mathf.Clamp(Yaw, _yawMin, _yawMax);
            Pitch = Mathf.Clamp(Pitch, -_pitchUp, _pitchDown);
        }

        /// <summary>Look direction relative to the seat (yaw right +, pitch down +).</summary>
        public void SetLook(float yaw, float pitch)
        {
            Yaw = Mathf.Clamp(yaw, _yawMin, _yawMax);
            Pitch = Mathf.Clamp(pitch, -_pitchUp, _pitchDown);
        }

        /// <summary>Start a spring blend from the current camera pose (call on every mode/place change).</summary>
        public void BeginTransition()
        {
            if (Cam == null) return;
            _fromPos = Cam.transform.position;
            _fromRot = Cam.transform.rotation;
            _fromFov = Cam.fieldOfView;
            _p = 0f; _v = 0f;
            _blending = true;
        }

        /// <summary>Jump straight to the target next frame (no blend).</summary>
        public void Snap() { _blending = false; }

        public void UpdateLook(bool lookMode)
        {
            if (Time.unscaledTime >= _nextPrefs) ReadPrefs();
            if (!lookMode || UnityEngine.Cursor.lockState != CursorLockMode.Locked) return;
            var mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 d = mouse.delta.ReadValue();
            if (d.sqrMagnitude < 1e-6f) return;
            float k = DegreesPerPixel * _sens;
            Yaw = Mathf.Clamp(Yaw + d.x * k, _yawMin, _yawMax);
            // unlimited yaw (standing in the office): keep it in -180..180
            if (_yawMax >= 1000f) Yaw = Mathf.Repeat(Yaw + 180f, 360f) - 180f;
            Pitch = Mathf.Clamp(Pitch + (_invert ? d.y : -d.y) * k, -_pitchUp, _pitchDown);
        }

        /// <summary>Moves the camera toward the target pose (exact when settled, spring while transitioning).</summary>
        public void Apply(Vector3 pos, Quaternion rot, float fov, float dt)
        {
            if (Cam == null) return;
            var t = Cam.transform;
            if (_blending)
            {
                dt = Mathf.Min(dt, 0.05f);
                // two half steps keep the spring stable at low frame rates
                for (int i = 0; i < 2; i++)
                {
                    float h = dt * 0.5f;
                    float a = Omega * Omega * (1f - _p) - 2f * Zeta * Omega * _v;
                    _v += a * h;
                    _p += _v * h;
                }
                if (Mathf.Abs(1f - _p) < 0.0015f && Mathf.Abs(_v) < 0.02f) _blending = false;
                t.SetPositionAndRotation(Vector3.LerpUnclamped(_fromPos, pos, _p), Quaternion.SlerpUnclamped(_fromRot, rot, Mathf.Min(_p, 1.08f)));
                Cam.fieldOfView = Mathf.LerpUnclamped(_fromFov, fov, _p);
                if (_blending) return;
            }
            t.SetPositionAndRotation(pos, rot);
            Cam.fieldOfView = fov;
        }

        // ------------------------------------------------------------------ crosshair

        void BuildCrosshair()
        {
            if (Panel == null) return;
            var go = new GameObject("Crosshair");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = Panel;
            doc.sortingOrder = -10;
            go.SetActive(true);
            var root = doc.rootVisualElement;
            if (root == null) return;
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.top = 0; root.style.right = 0; root.style.bottom = 0;
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            _dot = new VisualElement { pickingMode = PickingMode.Ignore, name = "pov-crosshair" };
            var s = _dot.style;
            s.width = 10; s.height = 10;
            s.borderTopLeftRadius = 5; s.borderTopRightRadius = 5; s.borderBottomLeftRadius = 5; s.borderBottomRightRadius = 5;
            s.backgroundColor = new Color(1f, 0.98f, 0.92f, 0.92f);
            var edge = new Color(0.09f, 0.12f, 0.24f, 0.85f);
            s.borderTopColor = edge; s.borderBottomColor = edge; s.borderLeftColor = edge; s.borderRightColor = edge;
            s.borderTopWidth = 2; s.borderBottomWidth = 2; s.borderLeftWidth = 2; s.borderRightWidth = 2;
            root.Add(_dot);
        }

        public void SetCrosshair(bool shown, bool hover, float dt)
        {
            if (_dot == null) return;
            if (shown != _crossShown)
            {
                _crossShown = shown;
                _dot.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (!shown) return;
            float want = hover ? 2.3f : 1f;
            _crossScale = Mathf.MoveTowards(_crossScale, want, dt * 9f);
            float px = 10f * _crossScale;
            if (Mathf.Abs(px - _crossPx) > 0.05f)
            {
                _crossPx = px;
                _dot.style.width = px; _dot.style.height = px;
                float r = px * 0.5f;
                _dot.style.borderTopLeftRadius = r; _dot.style.borderTopRightRadius = r; _dot.style.borderBottomLeftRadius = r; _dot.style.borderBottomRightRadius = r;
            }
            if (hover != _crossHover)
            {
                _crossHover = hover;
                _dot.style.backgroundColor = hover ? new Color(1f, 0.85f, 0.3f, 0.35f) : new Color(1f, 0.98f, 0.92f, 0.92f);
                var edge = hover ? new Color(1f, 0.82f, 0.25f, 1f) : new Color(0.09f, 0.12f, 0.24f, 0.85f);
                _dot.style.borderTopColor = edge; _dot.style.borderBottomColor = edge; _dot.style.borderLeftColor = edge; _dot.style.borderRightColor = edge;
            }
        }
    }
}
