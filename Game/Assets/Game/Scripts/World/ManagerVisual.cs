using BirimizKovulacak.Core;
using TMPro;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// The manager NPC as seen by clients: smoothed position from the public view, one look per manager profile
    /// (Hasan Bey / Nermin Hanım / Selin Hanım / Kemal Bey, classic fallback), readable state animation with a
    /// personality idle, an expressive face, a floor vision cone while inspecting (so players see what he can
    /// observe), and reaction bubbles that say exactly what attracted his attention.
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

        [Header("Look (set by ProjectBuilder.Characters)")]
        public Transform ModelRoot;
        public CharacterAppearance Appearance = new CharacterAppearance();
        public FaceRig Face = new FaceRig();
        public GameObject PropClipboard;

        static readonly int StWalk = Animator.StringToHash("Walk");
        static readonly int StInspect = Animator.StringToHash("Inspect");
        static readonly int StAngry = Animator.StringToHash("Angry");
        static readonly int StPleased = Animator.StringToHash("Pleased");
        static readonly int StTalk = Animator.StringToHash("Talk");
        static readonly int StPoint = Animator.StringToHash("Point");

        int _anim;
        int _idleState = Animator.StringToHash("Idle");
        int _shownObservation = -1;
        bool _positiveReaction;
        float _bubbleUntil;
        Vector3 _vel;
        bool _placed;
        string _profile;
        int _lookIndex = -1;
        bool _lookHasClipboard;
        FaceExpression _manualFace;
        float _manualFaceUntil;

        void Start()
        {
            var cfg = ClientState.I != null ? ClientState.I.Config : GameConfig.Match;
            ConeRange = cfg.Manager.VisionRange;
            ConeHalfAngle = cfg.Manager.VisionHalfAngle;
            if (ConeFilter != null) ConeFilter.sharedMesh = BuildCone(ConeRange, ConeHalfAngle, 28);
            if (BubbleRoot != null) BubbleRoot.SetActive(false);
            Face.Init(GetHashCode());
            if (_lookIndex < 0) SetProfile("");
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

        /// <summary>
        /// Picks the look for a manager profile id (<see cref="ManagerProfileIds"/>: paragoz | disiplinci | kayirmaci |
        /// paranoyak); anything else shows the classic manager. Cheap to call repeatedly (no-op when unchanged).
        /// </summary>
        public void SetProfile(string profileId)
        {
            profileId = profileId ?? "";
            if (profileId == _profile && _lookIndex >= 0) return;
            _profile = profileId;
            int idx = Appearance.IndexOf(profileId);
            if (idx < 0) idx = Appearance.IndexOf("default");
            if (idx < 0) idx = 0;
            _lookIndex = idx;
            var look = Appearance.Apply(idx);
            if (look == null) return;
            if (ModelRoot != null)
            {
                ModelRoot.localScale = look.Scale;
                ModelRoot.localRotation = Quaternion.Euler(look.Tilt, 0f, 0f);
            }
            Face.SetBase(look.Face);
            _idleState = Animator.StringToHash(string.IsNullOrEmpty(look.IdleState) ? "Idle" : look.IdleState);
            _lookHasClipboard = PropClipboard != null && System.Array.IndexOf(look.Accessories, PropClipboard) >= 0;
            _anim = 0; // re-evaluate the state with the new idle
        }

        /// <summary>Temporary face override (e.g. scripted moments).</summary>
        public void SetExpression(FaceExpression e, float seconds)
        {
            _manualFace = e;
            _manualFaceUntil = Time.time + seconds;
        }

        public void Apply(PlayerView v, double now)
        {
            if (v != null) SetProfile(v.ManagerProfileId);
            var mv = v != null ? v.Manager : null;
            if (mv == null)
            {
                SetAnim(_idleState);
                if (ConeRenderer != null) ConeRenderer.enabled = false;
                Face.Show(FaceExpression.Neutral, false, Time.time);
                return;
            }
            var target = new Vector3(mv.Position.X, mv.Position.Y, mv.Position.Z);
            if (!_placed || (target - transform.position).sqrMagnitude > 9f) { transform.position = target; _placed = true; }
            else transform.position = Vector3.SmoothDamp(transform.position, target, ref _vel, 0.12f, 6f);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, mv.Yaw, 0), 1f - Mathf.Exp(-8f * Time.deltaTime));

            bool reacting = mv.ReactionObservation >= 0 && mv.ReactionUntil > now;
            if (reacting && mv.ReactionObservation != _shownObservation)
            {
                _shownObservation = mv.ReactionObservation;
                ObservationView obs = null;
                foreach (var o in v.Observations) if (o.Id == mv.ReactionObservation) obs = o;
                _positiveReaction = obs != null && (obs.Kind == ObservationKind.WorkingHard || obs.Kind == ObservationKind.TipClean);
                if (obs != null) ShowBubble(Exclamation(obs, v), 2.8f);
            }

            int anim;
            FaceExpression face = FaceExpression.Neutral;
            bool talking = false;
            switch (mv.State)
            {
                case ManagerState.Walking:
                case ManagerState.Returning: anim = StWalk; break;
                case ManagerState.Inspecting: anim = StInspect; face = FaceExpression.Sneaky; break;
                case ManagerState.Reacting: anim = _positiveReaction ? StPleased : StAngry; face = _positiveReaction ? FaceExpression.Happy : FaceExpression.Angry; break;
                case ManagerState.InMeeting:
                    bool verdict = v.Phase == MatchPhase.MeetingVerdict || v.Phase == MatchPhase.Results;
                    anim = verdict ? StPoint : StTalk;
                    face = verdict ? FaceExpression.Angry : FaceExpression.Neutral;
                    talking = !verdict && v.Phase == MatchPhase.MeetingDiscussion;
                    break;
                default: anim = _idleState; break;
            }
            if (reacting && mv.State != ManagerState.Walking)
            {
                anim = _positiveReaction ? StPleased : StAngry;
                face = _positiveReaction ? FaceExpression.Happy : FaceExpression.Angry;
            }
            SetAnim(anim);
            if (PropClipboard != null)
            {
                bool clip = _lookHasClipboard || anim == StInspect;
                if (PropClipboard.activeSelf != clip) PropClipboard.SetActive(clip);
            }

            bool inspecting = (v.Phase == MatchPhase.Raid || v.Phase == MatchPhase.FinalInspection || mv.Surprise) &&
                              (mv.State == ManagerState.Walking || mv.State == ManagerState.Inspecting || mv.State == ManagerState.Reacting);
            if (ConeRenderer != null) ConeRenderer.enabled = inspecting;

            if (v.Phase == MatchPhase.Raid && mv.State == ManagerState.Warning && Time.time > _bubbleUntil)
                ShowBubble(Loc.T("manager.bubble.coming"), 1.5f);
            bool show = Time.time < _bubbleUntil;
            if (BubbleRoot != null)
            {
                if (BubbleRoot.activeSelf != show) BubbleRoot.SetActive(show);
                var cam = Camera.main;
                if (show && cam != null) BubbleRoot.transform.rotation = Quaternion.LookRotation(BubbleRoot.transform.position - cam.transform.position);
            }
            if (Time.time < _manualFaceUntil) face = _manualFace;
            Face.Show(face, talking || show, Time.time);
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

        void SetAnim(int state)
        {
            if (Animator == null || state == _anim) return;
            _anim = state;
            Animator.CrossFadeInFixedTime(state, 0.2f, 0);
        }
    }
}
