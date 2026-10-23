using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Slightly elevated third-person follow camera with mouse orbit, zoom and wall collision, plus fixed shots
    /// for the menu, the HR meeting and the results/firing sequence.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraRig : MonoBehaviour
    {
        public static CameraRig I { get; private set; }

        public enum Mode { Menu, Follow, Meeting, Results }
        public Mode Current = Mode.Menu;

        public float Distance = 4.8f;
        public float MinDistance = 2.6f, MaxDistance = 8.5f;
        public float Pitch = 33f, MinPitch = 12f, MaxPitch = 70f;
        public float Yaw;
        public float Sensitivity = 0.12f;
        public float TargetHeight = 1.15f;

        Transform _target;
        Vector3 _velocity;
        float _menuAngle;
        float _shake;

        void Awake()
        {
            I = this;
            Sensitivity = PlayerPrefs.GetFloat("settings.sensitivity", 0.12f);
        }

        public void SetTarget(Transform t)
        {
            if (_target != t && t != null) Yaw = t.eulerAngles.y;
            _target = t;
        }

        public void Shake(float amount) { _shake = Mathf.Max(_shake, amount); }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            var view = ClientState.I != null ? ClientState.I.View : null;
            if (view == null) Current = Mode.Menu;
            else if (view.Phase >= MatchPhase.MeetingDiscussion && view.Phase <= MatchPhase.MeetingVerdict) Current = Mode.Meeting;
            else if (view.Phase == MatchPhase.Results || view.Phase == MatchPhase.Abandoned) Current = Mode.Results;
            else Current = Mode.Follow;
            if (Current == Mode.Follow && PlayerAvatar.Local != null) SetTarget(PlayerAvatar.Local.transform);

            Vector3 pos; Quaternion rot;
            switch (Current)
            {
                case Mode.Follow when _target != null:
                    FollowPose(dt, out pos, out rot);
                    break;
                case Mode.Meeting:
                    pos = new Vector3(6.5f, 4.6f, 1.2f);
                    rot = Quaternion.LookRotation(new Vector3(6.5f, 0.7f, 4.9f) - pos);
                    break;
                case Mode.Results:
                    ResultsPose(view, dt, out pos, out rot);
                    break;
                default:
                    _menuAngle += dt * 4f;
                    var c = new Vector3(-1f, 0.5f, -1.5f);
                    pos = c + Quaternion.Euler(0, _menuAngle, 0) * new Vector3(0, 9f, -13f);
                    rot = Quaternion.LookRotation(c - pos);
                    break;
            }
            float k = Current == Mode.Follow ? 1f : 1f - Mathf.Exp(-3f * dt);
            transform.position = Current == Mode.Follow ? pos : Vector3.Lerp(transform.position, pos, k);
            transform.rotation = Current == Mode.Follow ? rot : Quaternion.Slerp(transform.rotation, rot, k);
            if (_shake > 0.001f)
            {
                transform.position += Random.insideUnitSphere * _shake * 0.08f;
                _shake = Mathf.MoveTowards(_shake, 0, dt * 2f);
            }
        }

        void FollowPose(float dt, out Vector3 pos, out Quaternion rot)
        {
            bool look = Cursor.lockState == CursorLockMode.Locked;
            if (look && GameInput.Look != null)
            {
                Vector2 d = GameInput.Look.ReadValue<Vector2>();
                Yaw += d.x * Sensitivity;
                Pitch = Mathf.Clamp(Pitch - d.y * Sensitivity, MinPitch, MaxPitch);
            }
            if (GameInput.Zoom != null && !UIRoot.PointerOverUI)
            {
                float z = GameInput.Zoom.ReadValue<float>();
                if (Mathf.Abs(z) > 0.01f) Distance = Mathf.Clamp(Distance - Mathf.Sign(z) * 0.5f, MinDistance, MaxDistance);
            }
            Vector3 focus = _target.position + Vector3.up * TargetHeight;
            rot = Quaternion.Euler(Pitch, Yaw, 0);
            Vector3 desired = focus - rot * Vector3.forward * Distance;
            // Pull in front of opaque walls.
            Vector3 dir = desired - focus;
            float dist = dir.magnitude;
            RaycastHit hit;
            if (Physics.SphereCast(focus, 0.25f, dir.normalized, out hit, dist, LayerMask.GetMask(Layers.WallsName), QueryTriggerInteraction.Ignore))
                desired = focus + dir.normalized * Mathf.Max(0.8f, hit.distance - 0.1f);
            pos = Vector3.SmoothDamp(transform.position, desired, ref _velocity, 0.06f);
            if ((pos - desired).sqrMagnitude > 25f) pos = desired;
        }

        void ResultsPose(PlayerView view, float dt, out Vector3 pos, out Quaternion rot)
        {
            Vector3 c = new Vector3(6.5f, 0.6f, 4.9f);
            if (view != null && view.Result != null)
            {
                var a = PlayerAvatar.BySlot(view.Result.FiredPlayer);
                if (a != null) c = a.transform.position + Vector3.up * 0.7f;
            }
            _menuAngle += dt * 10f;
            pos = c + Quaternion.Euler(0, _menuAngle, 0) * new Vector3(0, 1.6f, -3.6f);
            rot = Quaternion.LookRotation(c - pos);
        }
    }
}
