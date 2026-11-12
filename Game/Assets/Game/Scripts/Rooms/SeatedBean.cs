using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>Faces of the shared atlas (eye cell, mouth cell); same table as Art/character_looks.json "expressions".</summary>
    public enum BeanFace : byte { Neutral, Happy, Sneaky, Shocked, Angry, Sad, Talking, Laugh, Smug, Stern, Cheerful, Nervous, Asleep }

    /// <summary>
    /// A bean at the meeting table (players seated, the manager standing at the head). Drives the Animator states of
    /// the generated controllers (Sit / SitTalk / Sit* reactions / SitBox; manager idles, Talk, Point, Pleased, Angry),
    /// the face atlas cells (blink, lip flap while talking), a head turn toward whoever has the floor and the hover
    /// outline. No allocations per frame; materials are swapped only when a cell changes.
    /// Prefabs: Prefabs/Rooms/Seat_*.prefab and Boss_*.prefab (ProjectBuilder.Rooms).
    /// </summary>
    public sealed class SeatedBean : MonoBehaviour
    {
        public Animator Anim;
        public Renderer Eyes, Mouth;
        /// <summary>Atlas cells 0..7 (neutral, happy, sneaky, shocked, angry, sad, blink, laugh).</summary>
        public Material[] EyeCells = new Material[0];
        /// <summary>Atlas cells 0..7 (neutral, happy, smirk, shocked, angry, sad, talk A, talk E).</summary>
        public Material[] MouthCells = new Material[0];
        public Transform Head;
        public Transform ModelRoot;
        public GameObject Box;
        public BeanFace RestFace = BeanFace.Neutral;
        public bool IsManager;
        public string IdleState = "Sit";
        public string TalkState = "SitTalk";
        public Renderer[] OutlineRenderers = new Renderer[0];
        public Material Outline, OutlineHover;
        /// <summary>Look id this instance was made from (colour index for players, profile id for managers).</summary>
        public string LookId = "";

        static readonly byte[] EyeOf = { 0, 1, 2, 3, 4, 5, 0, 7, 2, 4, 0, 5, 6 };
        static readonly byte[] MouthOf = { 0, 1, 2, 3, 4, 5, 6, 1, 1, 5, 1, 4, 0 };
        const int EyeBlink = 6, MouthTalkA = 6, MouthTalkE = 7;
        static int s_seed = 17;

        int _stateShown;
        int _idleHash, _talkHash;
        bool _talking;
        float _talkLevel;
        MeetingReaction _reaction;
        float _reactionUntil;
        bool _fired, _away, _spot;
        int _eyeShown = -1, _mouthShown = -1;
        float _nextBlink, _blinkUntil, _nextFlap, _nextGesture;
        int _flapCell;
        uint _rng = 2463534242u;
        Vector3 _look;
        bool _hasLook;
        float _headYaw, _headYawVel, _headPitch, _headPitchVel;
        bool _gaze;
        float _gazeYaw, _gazePitch, _squash, _phase;
        Vector3 _modelScale = Vector3.one;
        Quaternion _modelRootBase = Quaternion.identity;
        Material[][] _normalMats, _hoverMats;
        bool _hover;

        void Awake()
        {
            _idleHash = Animator.StringToHash(IdleState);
            _talkHash = Animator.StringToHash(TalkState);
            if (ModelRoot != null) { _modelRootBase = ModelRoot.localRotation; _modelScale = ModelRoot.localScale; }
            _rng ^= (uint)(System.Threading.Interlocked.Increment(ref s_seed) * 2654435761u);
            if (_rng == 0) _rng = 1;
            _phase = Rand() * 6.28f;
            _nextBlink = Time.time + 0.5f + Rand() * 3f;
            if (Anim != null)
            {
                Anim.cullingMode = AnimatorCullingMode.AlwaysAnimate; // head turn is applied on top every frame
                Anim.Play(_idleHash, 0, Rand());
                _stateShown = _idleHash;
            }
            if (Box != null) Box.SetActive(false);
            CacheOutline();
        }

        void CacheOutline()
        {
            _normalMats = new Material[OutlineRenderers.Length][];
            _hoverMats = new Material[OutlineRenderers.Length][];
            for (int i = 0; i < OutlineRenderers.Length; i++)
            {
                var r = OutlineRenderers[i];
                if (r == null) continue;
                var a = r.sharedMaterials;
                var b = (Material[])a.Clone();
                for (int k = 0; k < b.Length; k++) if (b[k] == Outline && OutlineHover != null) b[k] = OutlineHover;
                _normalMats[i] = a;
                _hoverMats[i] = b;
            }
        }

        float Rand()
        {
            _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
            return (_rng & 0xFFFFFF) / 16777216f;
        }

        // ------------------------------------------------------------------ inputs

        public void SetTalking(bool talking, float level)
        {
            _talking = talking;
            _talkLevel = Mathf.Clamp01(level);
        }

        public void React(MeetingReaction r)
        {
            if (r == MeetingReaction.None || _fired) return;
            _reaction = r;
            _reactionUntil = Time.time + ReactionSeconds(r);
            int h = Animator.StringToHash(ReactionState(r));
            if (Anim != null && Anim.HasState(0, h))
            {
                Anim.CrossFadeInFixedTime(h, 0.12f, 0, 0f);
                _stateShown = h;
            }
        }

        public void SetFired(bool fired)
        {
            if (_fired == fired) return;
            _fired = fired;
            if (Box != null) Box.SetActive(fired);
            _reactionUntil = 0f;
        }

        public void SetAway(bool away) { _away = away; }
        public void SetSpotlit(bool on) { _spot = on; }

        public void LookAt(Vector3 world) { _look = world; _hasLook = true; _gaze = false; }
        public void ClearLook() { _hasLook = false; _gaze = false; }

        /// <summary>v5 step 3a shared gaze: the head turns to where the player looks (degrees relative to the seat,
        /// yaw right +, pitch down +), smoothly; the body follows a little.</summary>
        public void SetGaze(float yaw, float pitch) { _gaze = true; _gazeYaw = yaw; _gazePitch = pitch; _hasLook = false; }

        public void SetHover(bool on)
        {
            if (_hover == on || _normalMats == null) return;
            _hover = on;
            for (int i = 0; i < OutlineRenderers.Length; i++)
                if (OutlineRenderers[i] != null && _normalMats[i] != null) OutlineRenderers[i].sharedMaterials = on ? _hoverMats[i] : _normalMats[i];
        }

        string ReactionState(MeetingReaction r)
        {
            if (IsManager)
            {
                switch (r)
                {
                    case MeetingReaction.Nod:
                    case MeetingReaction.Laugh:
                    case MeetingReaction.Cheer: return "Pleased";
                    case MeetingReaction.Point: return "Point";
                    default: return "Angry";
                }
            }
            switch (r)
            {
                case MeetingReaction.Nod: return "SitNod";
                case MeetingReaction.Laugh: return "SitLaugh";
                case MeetingReaction.Gasp: return "SitGasp";
                case MeetingReaction.Point: return "SitPoint";
                case MeetingReaction.Slump: return "SitSlump";
                case MeetingReaction.Cheer: return "SitCheer";
                default: return IdleState;
            }
        }

        static float ReactionSeconds(MeetingReaction r)
        {
            switch (r)
            {
                case MeetingReaction.Nod: return 1.0f;
                case MeetingReaction.Laugh: return 1.5f;
                case MeetingReaction.Gasp: return 1.35f;
                case MeetingReaction.Point: return 1.35f;
                case MeetingReaction.Slump: return 2.6f;
                case MeetingReaction.Cheer: return 1.25f;
                default: return 0f;
            }
        }

        static BeanFace ReactionFace(MeetingReaction r)
        {
            switch (r)
            {
                case MeetingReaction.Nod: return BeanFace.Happy;
                case MeetingReaction.Laugh: return BeanFace.Laugh;
                case MeetingReaction.Gasp: return BeanFace.Shocked;
                case MeetingReaction.Point: return BeanFace.Angry;
                case MeetingReaction.Slump: return BeanFace.Sad;
                case MeetingReaction.Cheer: return BeanFace.Laugh;
                default: return BeanFace.Neutral;
            }
        }

        // ------------------------------------------------------------------ frame

        void Update()
        {
            float now = Time.time;
            bool reacting = now < _reactionUntil;
            int want;
            BeanFace face = RestFace;
            bool flap = false;
            if (_fired) { want = Animator.StringToHash(IsManager ? "Angry" : "SitBox"); face = BeanFace.Sad; }
            else if (reacting) { want = _stateShown; face = IsManager ? (_talking ? BeanFace.Talking : RestFace) : ReactionFace(_reaction); flap = IsManager && _talking; }
            else if (_away) { want = Animator.StringToHash(IsManager ? IdleState : "SitSlump"); face = BeanFace.Asleep; }
            else if (_talking)
            {
                want = _talkHash;
                flap = true;
                face = _spot ? BeanFace.Nervous : (IsManager ? RestFace : BeanFace.Talking);
                if (IsManager && now > _nextGesture)
                {
                    // the manager punctuates his monologue now and then
                    _nextGesture = now + 3.5f + Rand() * 4f;
                    if (Rand() < 0.45f) { React(MeetingReaction.Point); want = _stateShown; }
                }
            }
            else
            {
                want = _idleHash;
                if (_spot) face = BeanFace.Nervous;
            }
            if (Anim != null)
            {
                if (want != _stateShown && Anim.HasState(0, want))
                {
                    Anim.CrossFadeInFixedTime(want, 0.25f, 0);
                    _stateShown = want;
                }
                Anim.speed = _talking && !reacting && !_fired ? 0.85f + 0.45f * _talkLevel : 1f;
            }
            ShowFace(face, flap, now);
        }

        void ShowFace(BeanFace e, bool talking, float now)
        {
            int idx = (int)e;
            if (idx < 0 || idx >= EyeOf.Length) idx = 0;
            int eye = EyeOf[idx];
            int mouth = MouthOf[idx];
            if (now >= _nextBlink)
            {
                _blinkUntil = now + 0.11f;
                _nextBlink = now + 2.2f + Rand() * 3.5f;
                if (Rand() < 0.15f) _nextBlink = now + 0.25f;
            }
            if (now < _blinkUntil && (eye == 0 || eye == 2 || eye == 4 || eye == 5)) eye = EyeBlink;
            if (talking)
            {
                if (now >= _nextFlap)
                {
                    float speed = 0.06f + (1f - _talkLevel) * 0.08f;
                    _nextFlap = now + speed + Rand() * 0.07f;
                    float r = Rand();
                    _flapCell = r < 0.42f ? MouthTalkA : (r < 0.78f ? MouthTalkE : 0);
                }
                mouth = _flapCell;
            }
            if (eye != _eyeShown && Eyes != null && eye < EyeCells.Length && EyeCells[eye] != null)
            {
                _eyeShown = eye;
                Eyes.sharedMaterial = EyeCells[eye];
            }
            if (mouth != _mouthShown && Mouth != null && mouth < MouthCells.Length && MouthCells[mouth] != null)
            {
                _mouthShown = mouth;
                Mouth.sharedMaterial = MouthCells[mouth];
            }
        }

        void LateUpdate()
        {
            float want = 0f, wantPitch = 0f;
            if (_gaze)
            {
                want = Mathf.Clamp(_gazeYaw, -70f, 70f);
                wantPitch = Mathf.Clamp(_gazePitch, -30f, 40f);
            }
            else if (_hasLook && Head != null)
            {
                Vector3 d = transform.InverseTransformDirection(_look - Head.position);
                float flat = new Vector2(d.x, d.z).magnitude;
                if (flat > 1e-3f)
                {
                    want = Mathf.Clamp(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -70f, 70f);
                    wantPitch = Mathf.Clamp(-Mathf.Atan2(d.y, flat) * Mathf.Rad2Deg, -30f, 40f);
                }
            }
            if (_fired) { want *= 0.3f; wantPitch = 18f; }
            _headYaw = Mathf.SmoothDamp(_headYaw, want, ref _headYawVel, 0.3f);
            _headPitch = Mathf.SmoothDamp(_headPitch, wantPitch, ref _headPitchVel, 0.3f);
            if (Head != null)
                Head.rotation = Quaternion.AngleAxis(_headYaw * 0.7f, transform.up) * Quaternion.AngleAxis(_headPitch * 0.55f, transform.right) * Head.rotation;
            if (ModelRoot != null)
            {
                ModelRoot.localRotation = Quaternion.Euler(_headPitch * 0.12f, _headYaw * 0.3f, 0f) * _modelRootBase;
                // talking: a little squash and bob with the voice level (code-driven on top of the SitTalk clip)
                float k = Mathf.MoveTowards(_squash, _talking && !_fired ? Mathf.Clamp01(0.35f + _talkLevel) : 0f, Time.deltaTime * 6f);
                _squash = k;
                float s = k > 0.001f ? Mathf.Sin(Time.time * 15f + _phase) * 0.035f * k : 0f;
                ModelRoot.localScale = new Vector3(_modelScale.x * (1f - s * 0.5f), _modelScale.y * (1f + s), _modelScale.z * (1f - s * 0.5f));
            }
        }
    }
}
