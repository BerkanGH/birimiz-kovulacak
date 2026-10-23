using BirimizKovulacak.Core;
using TMPro;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// The manager NPC as seen by clients: smoothed position from the public view, readable state animation,
    /// a floor vision cone while inspecting (so players see what he can observe), and reaction bubbles that say
    /// exactly what attracted his attention.
    /// </summary>
    public sealed class ManagerVisual : MonoBehaviour
    {
        public Animator Animator;
        public TextMeshPro Bubble;
        public GameObject BubbleRoot;
        public MeshRenderer ConeRenderer;
        public MeshFilter ConeFilter;
        public float ConeRange = 10f;
        public float ConeHalfAngle = 70f;

        string _anim = "";
        int _shownObservation = -1;
        float _bubbleUntil;
        Vector3 _vel;
        bool _placed;

        void Start()
        {
            var cfg = ClientState.I != null ? ClientState.I.Config : GameConfig.Match;
            ConeRange = cfg.Manager.VisionRange;
            ConeHalfAngle = cfg.Manager.VisionHalfAngle;
            if (ConeFilter != null) ConeFilter.sharedMesh = BuildCone(ConeRange, ConeHalfAngle, 28);
            if (BubbleRoot != null) BubbleRoot.SetActive(false);
        }

        static Mesh BuildCone(float range, float halfAngle, int segments)
        {
            var m = new Mesh { name = "ManagerVisionCone" };
            var verts = new Vector3[segments + 2];
            var tris = new int[segments * 3];
            var uv = new Vector2[segments + 2];
            verts[0] = Vector3.zero;
            uv[0] = new Vector2(0, 0);
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.Deg2Rad * Mathf.Lerp(-halfAngle, halfAngle, i / (float)segments);
                verts[i + 1] = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * range;
                uv[i + 1] = new Vector2(i / (float)segments, 1);
            }
            for (int i = 0; i < segments; i++) { tris[i * 3] = 0; tris[i * 3 + 1] = i + 1; tris[i * 3 + 2] = i + 2; }
            m.vertices = verts; m.triangles = tris; m.uv = uv;
            m.RecalculateNormals();
            return m;
        }

        public void Apply(PlayerView v, double now)
        {
            var mv = v != null ? v.Manager : null;
            if (mv == null)
            {
                SetAnim("Idle");
                if (ConeRenderer != null) ConeRenderer.enabled = false;
                return;
            }
            var target = new Vector3(mv.Position.X, mv.Position.Y, mv.Position.Z);
            if (!_placed || (target - transform.position).sqrMagnitude > 9f) { transform.position = target; _placed = true; }
            else transform.position = Vector3.SmoothDamp(transform.position, target, ref _vel, 0.12f, 6f);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, mv.Yaw, 0), 1f - Mathf.Exp(-8f * Time.deltaTime));

            bool reacting = mv.ReactionObservation >= 0 && mv.ReactionUntil > now;
            string anim;
            switch (mv.State)
            {
                case ManagerState.Walking:
                case ManagerState.Returning: anim = "Walk"; break;
                case ManagerState.Inspecting: anim = "Inspect"; break;
                case ManagerState.Reacting: anim = "Angry"; break;
                case ManagerState.InMeeting: anim = (v.Phase == MatchPhase.MeetingVerdict || v.Phase == MatchPhase.Results) ? "Point" : "Talk"; break;
                default: anim = "Idle"; break;
            }
            if (reacting && mv.State != ManagerState.Walking) anim = "Angry";
            SetAnim(anim);

            bool inspecting = (v.Phase == MatchPhase.Raid || v.Phase == MatchPhase.FinalInspection) &&
                              (mv.State == ManagerState.Walking || mv.State == ManagerState.Inspecting || mv.State == ManagerState.Reacting);
            if (ConeRenderer != null) ConeRenderer.enabled = inspecting;

            if (reacting && mv.ReactionObservation != _shownObservation)
            {
                _shownObservation = mv.ReactionObservation;
                ObservationView obs = null;
                foreach (var o in v.Observations) if (o.Id == mv.ReactionObservation) obs = o;
                if (obs != null) ShowBubble(Exclamation(obs, v), 2.8f);
            }
            if (v.Phase == MatchPhase.Raid && mv.State == ManagerState.Warning && Time.time > _bubbleUntil)
                ShowBubble(Loc.T("manager.bubble.coming"), 1.5f);
            if (BubbleRoot != null)
            {
                bool show = Time.time < _bubbleUntil;
                if (BubbleRoot.activeSelf != show) BubbleRoot.SetActive(show);
                var cam = Camera.main;
                if (show && cam != null) BubbleRoot.transform.rotation = Quaternion.LookRotation(BubbleRoot.transform.position - cam.transform.position);
            }
        }

        public void ShowBubble(string text, float seconds)
        {
            if (Bubble != null) Bubble.text = text;
            _bubbleUntil = Time.time + seconds;
        }

        static string Exclamation(ObservationView o, PlayerView v)
        {
            string who = Loc.PlayerName(v, o.Subject);
            switch (o.Kind)
            {
                case ObservationKind.SuspiciousItemAtDesk: return Loc.F("manager.bubble.item", Loc.Item(o.Detail), who);
                case ObservationKind.MessyDesk: return Loc.F("manager.bubble.messy", who);
                case ObservationKind.NotAtDesk: return Loc.F("manager.bubble.not_at_desk", who);
                case ObservationKind.AtWrongDesk: return Loc.F("manager.bubble.wrong_desk", who);
                case ObservationKind.MovingDuringInspection: return Loc.F("manager.bubble.moving", who);
                case ObservationKind.CarryingSuspicious: return Loc.F("manager.bubble.carrying", who, Loc.Item(o.Detail));
                case ObservationKind.CaughtInAct: return Loc.F("manager.bubble.caught", who);
                case ObservationKind.BehindOnWork: return Loc.F("manager.bubble.behind", who);
                case ObservationKind.WorkingHard: return Loc.F("manager.bubble.good", who);
                default: return "!";
            }
        }

        void SetAnim(string a)
        {
            if (Animator == null || a == _anim) return;
            _anim = a;
            Animator.CrossFadeInFixedTime(a, 0.2f);
        }
    }
}
