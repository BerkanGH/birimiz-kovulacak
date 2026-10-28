using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using EventType = BirimizKovulacak.Core.EventType;

namespace BirimizKovulacak.Game
{
    /// <summary>Faces available on every bean (eyes cell + mouth cell of the shared face atlas, see <see cref="FaceRig"/>).</summary>
    public enum FaceExpression : byte
    {
        Neutral = 0,
        Happy = 1,
        Sneaky = 2,
        Shocked = 3,
        Angry = 4,
        Sad = 5,
        Talking = 6,   // neutral eyes, mouth always flapping
        Laugh = 7,
        Smug = 8,
        Stern = 9,
        Cheerful = 10, // open eyes + big grin (Selin's default)
        Nervous = 11,
    }

    /// <summary>
    /// A player's body in the office. Movement is driven by whoever owns it (local input, a network bot, or a
    /// host-side virtual bot). Appearance and animation are derived from the public view, the replicated crouch flag
    /// and the feel layer (<see cref="IAvatarFeel"/>); nothing here sends network traffic.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerAvatar : NetworkBehaviour, IAvatarFeel
    {
        public static readonly List<PlayerAvatar> All = new List<PlayerAvatar>();
        public static PlayerAvatar Local { get; private set; }

        public readonly NetworkVariable<int> SlotVar = new NetworkVariable<int>(-1);
        public int Slot => SlotVar.Value;

        [Header("Wiring (set by ProjectBuilder.Characters)")]
        public Animator Animator;
        public Transform ModelRoot;
        /// <summary>Where carried items are drawn (moved every frame between the chest and right-hand mounts).</summary>
        public Transform CarrySocket;
        /// <summary>Right-hand mount (child of the Hand.R bone).</summary>
        public Transform HandSocket;
        public Transform ChestMount;
        public CharacterAppearance Appearance = new CharacterAppearance();
        public FaceRig Face = new FaceRig();
        public GameObject PropHandset;   // desk phone (OnPhone)
        public GameObject PropMobile;    // personal phone (OnCall)
        public GameObject PropBox;       // fired walk-out box
        /// <summary>Lengths (s) of the one-shot clips, indexed by <see cref="OneShot"/>.</summary>
        public float[] OneShotSeconds = { 3f, 1.5f, 1.33f, 1.67f, 1.33f, 1.67f, 1f };
        public TMPro.TextMeshPro NameTag;

        public const float WalkSpeed = 3.6f;
        public const float SlowSpeed = 1.0f;
        const float Gravity = -20f;

        CharacterController _cc;
        Vector3 _velocity;
        float _verticalVel;
        Vector3 _lastPos;
        float _visualSpeed;
        bool _movementLocked;
        int _appliedSlot = -2;

        /// <summary>True when this process should move this avatar (owner = this client, or host for bot avatars).</summary>
        public bool CanDrive => IsSpawned && IsOwner;
        public bool IsVirtualBot { get; set; }
        public float CurrentSpeed => _visualSpeed;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _nextIdle2 = Time.time + UnityEngine.Random.Range(7f, 14f);
            Face.Init(GetHashCode());
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            if (IsOwner && !IsVirtualBot && IsLocalPlayer) Local = this;
            SlotVar.OnValueChanged += OnSlotChanged;
            ApplyAppearance();
            _lastPos = transform.position;
            if (ClientState.I != null) ClientState.I.PhaseChanged += OnPhaseChanged;
            if (ClientState.I != null) ClientState.I.EventReceived += OnEvent;
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            if (Local == this) Local = null;
            SlotVar.OnValueChanged -= OnSlotChanged;
            if (ClientState.I != null) ClientState.I.PhaseChanged -= OnPhaseChanged;
            if (ClientState.I != null) ClientState.I.EventReceived -= OnEvent;
        }

        void OnSlotChanged(int previous, int current) { ApplyAppearance(); }

        public void ServerAssignSlot(int slot)
        {
            if (IsServer) SlotVar.Value = slot;
        }

        public static PlayerAvatar BySlot(int slot)
        {
            for (int i = 0; i < All.Count; i++) if (All[i].Slot == slot) return All[i];
            return null;
        }

        /// <summary>Player slot = colour index = look (0 Mavi, 1 Sarı, 2 Pembe, 3 Yeşil, 4 Turuncu, 5 Mor).</summary>
        void ApplyAppearance()
        {
            int slot = Slot;
            if (slot == _appliedSlot) return;
            _appliedSlot = slot;
            if (slot < 0) return;
            var look = Appearance.Apply(slot);
            if (look != null)
            {
                _lookScale = look.Scale;
                _lookTilt = look.Tilt;
                Face.SetBase(look.Face);
            }
            gameObject.name = "Avatar_" + slot;
        }

        // ---------------------------------------------------------------- movement (owner only)

        public void SetMovementLocked(bool locked) { _movementLocked = locked; }

        /// <summary>Called each frame by the controlling input source with a world-space direction.</summary>
        public void Drive(Vector3 worldDir, bool slow, float dt)
        {
            if (!CanDrive) return;
            worldDir.y = 0;
            if (_movementLocked) worldDir = Vector3.zero;
            if (worldDir.sqrMagnitude > 1f) worldDir.Normalize();
            float speed = slow ? SlowSpeed : WalkSpeed;
            Vector3 target = worldDir * speed;
            _velocity = Vector3.MoveTowards(_velocity, target, (target.sqrMagnitude > 0.01f ? 22f : 30f) * dt);
            _verticalVel = _cc.isGrounded ? -1f : _verticalVel + Gravity * dt;
            _cc.Move((_velocity + Vector3.up * _verticalVel) * dt);
            if (_velocity.sqrMagnitude > 0.04f)
            {
                var look = Quaternion.LookRotation(new Vector3(_velocity.x, 0, _velocity.z));
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, 720f * dt);
            }
        }

        public void FaceTowards(Vector3 worldPoint, float dt)
        {
            if (!CanDrive) return;
            Vector3 d = worldPoint - transform.position; d.y = 0;
            if (d.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 540f * dt);
        }

        public void Teleport(Vector3 position, float yaw)
        {
            if (!CanDrive) return;
            _cc.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
            _cc.enabled = true;
            _velocity = Vector3.zero;
            var nt = GetComponent<NetworkTransform>();
            if (nt != null && nt.CanCommitToTransform) nt.Teleport(position, Quaternion.Euler(0, yaw, 0), transform.localScale);
        }

        void OnPhaseChanged(MatchPhase from, MatchPhase to)
        {
            if (!CanDrive) return;
            var layout = GameConfig.Layout;
            int slot = Slot;
            if (slot < 0) return;
            if (to == MatchPhase.Briefing)
            {
                var desk = layout.GetDesk(DeskOf(slot));
                if (desk != null) Teleport(ToV(desk.SeatPosition) + Vector3.up * 0.05f, desk.Yaw);
            }
            else if (to == MatchPhase.MeetingDiscussion && slot < layout.MeetingSeats.Count)
            {
                var seat = layout.MeetingSeats[slot];
                Teleport(ToV(seat.Position) + Vector3.up * 0.05f, seat.Yaw);
            }
            SetMovementLocked(to >= MatchPhase.MeetingDiscussion && to <= MatchPhase.Results);
        }

        int DeskOf(int slot)
        {
            var v = ClientState.I != null ? ClientState.I.View : null;
            if (v != null)
                foreach (var p in v.Players) if (p.Id == slot) return p.DeskIndex;
            return slot;
        }

        static Vector3 ToV(Vec3 v) { return new Vector3(v.X, v.Y, v.Z); }

        void OnEvent(GameEvent e)
        {
            // Everyone flinches when the manager announces an inspection (one-shot, only when not busy).
            if (e.Type == EventType.ManagerWarning) _flinchRequested = true;
        }

        // ================================================================ presentation (all peers)

        /// <summary>One-shot clips (index into <see cref="OneShotSeconds"/>).</summary>
        public enum OneShot { Idle2 = 0, Wave = 1, Point = 2, Facepalm = 3, Shrug = 4, Laugh = 5, Shocked = 6 }

        // Animator state hashes (state names = ProjectBuilder.Characters state names).
        static readonly int SpeedParam = Animator.StringToHash("Speed");
        static readonly int StLocomotion = Animator.StringToHash("Locomotion");
        static readonly int StCrouch = Animator.StringToHash("CrouchLoco");
        static readonly int StCarry = Animator.StringToHash("CarryLoco");
        static readonly int StFired = Animator.StringToHash("FiredLoco");
        static readonly int StWorkPose = Animator.StringToHash("WorkPose");
        static readonly int StTyping = Animator.StringToHash("Typing");
        static readonly int StPhone = Animator.StringToHash("PhoneStand");
        static readonly int StCleaning = Animator.StringToHash("Cleaning");
        static readonly int StRummaging = Animator.StringToHash("Rummaging");
        static readonly int StFiddling = Animator.StringToHash("Fiddling");
        static readonly int StBrewing = Animator.StringToHash("Brewing");
        static readonly int StDelivering = Animator.StringToHash("Delivering");
        static readonly int StPlacing = Animator.StringToHash("Placing");
        static readonly int StSpilling = Animator.StringToHash("Spilling");
        static readonly int StDrinking = Animator.StringToHash("Drinking");
        static readonly int StPlugging = Animator.StringToHash("Plugging");
        static readonly int StReachUp = Animator.StringToHash("ReachUp");
        static readonly int StSit = Animator.StringToHash("Sit");
        static readonly int StSitTalk = Animator.StringToHash("SitTalk");
        static readonly int StTalk = Animator.StringToHash("Talk");
        static readonly int StPanic = Animator.StringToHash("Panic");
        static readonly int StCheer = Animator.StringToHash("Cheer");
        static readonly int[] OneShotStates =
        {
            Animator.StringToHash("Idle2"), Animator.StringToHash("Wave"), Animator.StringToHash("Point"), Animator.StringToHash("Facepalm"),
            Animator.StringToHash("Shrug"), Animator.StringToHash("Laugh"), Animator.StringToHash("Shocked"),
        };
        static readonly FaceExpression[] OneShotFaces =
        {
            FaceExpression.Neutral, FaceExpression.Happy, FaceExpression.Angry, FaceExpression.Sad, FaceExpression.Smug, FaceExpression.Laugh, FaceExpression.Shocked,
        };
        const int PhoneLayer = 1;

        int _state;
        int _oneShot = -1;
        float _oneShotUntil;
        float _nextIdle2;
        float _stillSince;
        bool _flinchRequested;
        bool _crouched;
        bool _talking;
        float _phoneWeight;
        float _carryHand;
        Vector3 _lookScale = Vector3.one;
        float _lookTilt;
        float _squash, _squashVel;
        FaceExpression _feelFace = FaceExpression.Neutral;
        FaceExpression _manualFace = FaceExpression.Neutral;
        float _manualFaceUntil;

        // ---------------------------------------------------------------- public API

        /// <summary>Show a face. seconds &gt; 0: temporary override; 0: request from the feel layer (persistent until changed).</summary>
        public void SetExpression(FaceExpression expression, float seconds = 0f)
        {
            if (seconds > 0f) { _manualFace = expression; _manualFaceUntil = Time.time + seconds; }
            else _feelFace = expression;
        }

        /// <summary>Mouth flap while this player speaks (voice) — also adds talking gestures when idle.</summary>
        public void SetTalking(bool talking) { _talking = talking; }

        /// <summary>Plays an emote locally (the feel layer calls this once per emote on every peer).</summary>
        public void PlayEmote(EmoteId emote)
        {
            int i;
            switch (emote)
            {
                case EmoteId.Wave: i = (int)OneShot.Wave; break;
                case EmoteId.Point: i = (int)OneShot.Point; break;
                case EmoteId.Facepalm: i = (int)OneShot.Facepalm; break;
                case EmoteId.Shrug: i = (int)OneShot.Shrug; break;
                case EmoteId.Laugh: i = (int)OneShot.Laugh; break;
                default: return;
            }
            StartOneShot(i);
        }

        /// <summary>Startled jump (e.g. a surprise). Plays only when the avatar is not busy.</summary>
        public void PlayShocked() { _flinchRequested = true; }

        public void SetCrouched(bool crouched) { _crouched = crouched; }

        /// <summary>Squash-and-stretch impulse (stop / land), amount ~0.1..0.3.</summary>
        public void Squash(float amount) { if (amount > _squash) { _squash = amount; _squashVel = 0f; } }

        // IAvatarFeel ----------------------------------------------------
        void IAvatarFeel.SetExpression(FeelExpression e) { SetExpression(FromFeel(e)); }
        void IAvatarFeel.SetTalking(bool talking) { SetTalking(talking); }
        void IAvatarFeel.PlayEmote(EmoteId emote) { PlayEmote(emote); }
        void IAvatarFeel.SetCrouched(bool crouched) { SetCrouched(crouched); }
        bool IAvatarFeel.SetVisibility(float alpha) { return false; } // AvatarVisibility toggles our renderers at 0.5
        void IAvatarFeel.Squash(float amount) { Squash(amount); }

        static FaceExpression FromFeel(FeelExpression e)
        {
            switch (e)
            {
                case FeelExpression.Nervous: return FaceExpression.Nervous;
                case FeelExpression.Shocked: return FaceExpression.Shocked;
                case FeelExpression.Happy: return FaceExpression.Happy;
                case FeelExpression.Angry: return FaceExpression.Angry;
                case FeelExpression.Sad: return FaceExpression.Sad;
                case FeelExpression.Smug: return FaceExpression.Smug;
                default: return FaceExpression.Neutral;
            }
        }

        void StartOneShot(int index)
        {
            if (Animator == null || index < 0 || index >= OneShotStates.Length) return;
            _oneShot = index;
            float len = OneShotSeconds != null && index < OneShotSeconds.Length ? OneShotSeconds[index] : 1.5f;
            _oneShotUntil = Time.time + Mathf.Max(0.3f, len - 0.15f);
            _state = OneShotStates[index];
            Animator.CrossFadeInFixedTime(_state, 0.12f, 0, 0f);
        }

        // ---------------------------------------------------------------- per frame

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            Vector3 p = transform.position;
            Vector3 d = p - _lastPos; d.y = 0;
            float inst = d.magnitude / Mathf.Max(dt, 1e-3f);
            _visualSpeed = Mathf.Lerp(_visualSpeed, inst, 0.25f);
            _lastPos = p;
            UpdateAnimation(dt);
            UpdateModelRoot(dt);
            UpdateCarrySocket(dt);
            if (NameTag != null)
            {
                var cam = Camera.main;
                if (cam != null) NameTag.transform.rotation = Quaternion.LookRotation(NameTag.transform.position - cam.transform.position);
                var view = ClientState.I != null ? ClientState.I.View : null;
                string nm = view != null ? Loc.PlayerName(view, Slot) : "";
                if (NameTag.text != nm) NameTag.text = nm;
                NameTag.gameObject.SetActive(view != null && Slot != (ClientState.I != null ? ClientState.I.MySlot : -1));
            }
        }

        public PlayerPublicView PublicState
        {
            get
            {
                var v = ClientState.I != null ? ClientState.I.View : null;
                if (v == null) return null;
                for (int i = 0; i < v.Players.Count; i++) if (v.Players[i].Id == Slot) return v.Players[i];
                return null;
            }
        }

        void UpdateAnimation(float dt)
        {
            if (Animator == null) return;
            var st = PublicState;
            var view = ClientState.I != null ? ClientState.I.View : null;
            float now = Time.time;
            bool carrying = st != null && !string.IsNullOrEmpty(st.CarryingItemId);
            bool moving = _visualSpeed > 0.25f;
            bool crouched = _crouched || (st != null && st.Crouched);
            PublicActivity activity = st != null ? st.Activity : PublicActivity.None;
            MatchPhase phase = view != null ? view.Phase : MatchPhase.None;

            if (moving || activity != PublicActivity.None) _stillSince = now;

            bool flinch = _flinchRequested;
            _flinchRequested = false;
            int target;
            FaceExpression autoFace = FaceExpression.Neutral;
            bool fired = false, phone = false, mobile = false;
            bool oneShotActive = _oneShot >= 0 && now < _oneShotUntil;
            if (!oneShotActive) _oneShot = -1;

            if (phase == MatchPhase.Results && view.Result != null)
            {
                bool won = false;
                foreach (var o in view.Result.Players) if (o.Player == Slot) won = o.Won;
                fired = view.Result.FiredPlayer == Slot;
                target = fired ? StFired : (won ? StCheer : StLocomotion);
                autoFace = fired ? FaceExpression.Sad : (won ? FaceExpression.Laugh : FaceExpression.Neutral);
                if (oneShotActive && !fired) target = _state;
            }
            else if (phase == MatchPhase.MeetingVerdict && view.Meeting.Verdict != null && view.Meeting.Verdict.FiredPlayer == Slot)
            {
                target = StPanic;
                autoFace = FaceExpression.Shocked;
            }
            else if (phase >= MatchPhase.MeetingDiscussion && phase <= MatchPhase.MeetingVerdict)
            {
                bool speaker = (phase == MatchPhase.MeetingDefense1 || phase == MatchPhase.MeetingDefense2) && view.Meeting.Speaker == Slot;
                target = oneShotActive ? _state : ((speaker || _talking) ? StSitTalk : StSit);
                if (speaker && !_talking) autoFace = FaceExpression.Talking; // no voice: still look like they are pleading
            }
            else if (oneShotActive)
            {
                target = _state;
                if (moving && now > _oneShotUntil - OneShotSeconds[_oneShot] + 0.35f) { _oneShot = -1; target = LocoState(crouched, carrying); }
            }
            else if (activity != PublicActivity.None && activity != PublicActivity.OnCall)
            {
                target = ActivityState(activity, out autoFace);
                phone = activity == PublicActivity.OnPhone;
            }
            else
            {
                if (flinch && !moving && !carrying) { StartOneShot((int)OneShot.Shocked); target = _state; }
                else
                {
                    target = LocoState(crouched, carrying);
                    if (activity == PublicActivity.OnCall) { mobile = true; if (!moving && !carrying && !crouched) target = StPhone; }
                    else if (!moving && st != null && st.WorkPose) target = StWorkPose;
                    else if (!moving && _talking && target == StLocomotion) target = StTalk;
                    else if (target == StLocomotion && !moving && now > _nextIdle2 && now - _stillSince > 6f)
                    {
                        _nextIdle2 = now + UnityEngine.Random.Range(9f, 16f);
                        StartOneShot((int)OneShot.Idle2);
                        target = _state;
                    }
                    if (crouched) autoFace = FaceExpression.Sneaky;
                }
            }
            if (_oneShot >= 0 && target == _state) autoFace = OneShotFaces[_oneShot];

            if (target != _state)
            {
                _state = target;
                Animator.CrossFadeInFixedTime(target, 0.18f, 0);
            }
            Animator.SetFloat(SpeedParam, _visualSpeed);

            // phone arm layer (desk handset or personal mobile)
            bool phoneArm = phone || mobile;
            _phoneWeight = Mathf.MoveTowards(_phoneWeight, phoneArm ? 1f : 0f, dt * 6f);
            if (Animator.layerCount > PhoneLayer) Animator.SetLayerWeight(PhoneLayer, _phoneWeight);
            SetActive(PropHandset, phone && _phoneWeight > 0.5f);
            SetActive(PropMobile, mobile && _phoneWeight > 0.5f);
            SetActive(PropBox, fired);

            // face: temporary override > feel request > automatic (emote / activity) > look default
            FaceExpression face = autoFace;
            if (now < _manualFaceUntil) face = _manualFace;
            else if (_feelFace != FaceExpression.Neutral && autoFace != FaceExpression.Laugh) face = _feelFace;
            Face.Show(face, _talking, now);

            // carried items follow the right hand for one-handed activities
            bool inHand = activity == PublicActivity.Cleaning || activity == PublicActivity.Spilling || activity == PublicActivity.Brewing ||
                          activity == PublicActivity.Drinking;
            _carryHand = Mathf.MoveTowards(_carryHand, inHand ? 1f : 0f, dt * 5f);
        }

        int LocoState(bool crouched, bool carrying)
        {
            if (crouched) return StCrouch;
            if (carrying) return StCarry;
            return StLocomotion;
        }

        static int ActivityState(PublicActivity a, out FaceExpression face)
        {
            face = FaceExpression.Neutral;
            switch (a)
            {
                case PublicActivity.Typing: return StTyping;
                case PublicActivity.OnPhone: face = FaceExpression.Talking; return StPhone;
                case PublicActivity.Cleaning: return StCleaning;
                case PublicActivity.Rummaging: face = FaceExpression.Sneaky; return StRummaging;
                case PublicActivity.Fiddling: face = FaceExpression.Sneaky; return StFiddling;
                case PublicActivity.Spilling: face = FaceExpression.Sneaky; return StSpilling;
                case PublicActivity.Brewing: return StBrewing;
                case PublicActivity.Delivering: face = FaceExpression.Happy; return StDelivering;
                case PublicActivity.Placing:
                case PublicActivity.PickingUp: return StPlacing;
                case PublicActivity.Plugging: face = FaceExpression.Sneaky; return StPlugging;
                case PublicActivity.ServerWork: return StFiddling;
                case PublicActivity.FuseWork: return StReachUp;
                case PublicActivity.Drinking: return StDrinking;
                default: return StFiddling;
            }
        }

        static void SetActive(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on) go.SetActive(on);
        }

        void UpdateModelRoot(float dt)
        {
            if (ModelRoot == null) return;
            // damped spring: impulses squash, the rebound stretches a little
            _squashVel += (-170f * _squash - 11f * _squashVel) * dt;
            _squash += _squashVel * dt;
            if (Mathf.Abs(_squash) < 0.0005f && Mathf.Abs(_squashVel) < 0.005f) { _squash = 0f; _squashVel = 0f; }
            float k = Mathf.Clamp(_squash, -0.3f, 0.35f);
            ModelRoot.localScale = new Vector3(_lookScale.x * (1f + k * 0.6f), _lookScale.y * (1f - k), _lookScale.z * (1f + k * 0.6f));
            ModelRoot.localRotation = _lookTilt != 0f ? Quaternion.Euler(_lookTilt, 0f, 0f) : Quaternion.identity;
        }

        void UpdateCarrySocket(float dt)
        {
            if (CarrySocket == null || ChestMount == null) return;
            Vector3 pos = ChestMount.position;
            if (HandSocket != null && _carryHand > 0.001f) pos = Vector3.Lerp(pos, HandSocket.position, _carryHand);
            CarrySocket.SetPositionAndRotation(pos, transform.rotation);
        }
    }

    // ==================================================================== shared character presentation types

    /// <summary>One look: accessories to show, material swaps, silhouette scale / tilt and the resting face.</summary>
    [Serializable]
    public sealed class CharacterLook
    {
        public string Id = "";
        public GameObject[] Accessories = new GameObject[0];
        /// <summary>Target material per <see cref="CharacterAppearance.Groups"/> entry (null = keep).</summary>
        public Material[] Swaps = new Material[0];
        public Vector3 Scale = Vector3.one;
        /// <summary>Forward hunch in degrees (Kemal Bey).</summary>
        public float Tilt;
        public FaceExpression Face = FaceExpression.Neutral;
        /// <summary>Managers: Animator state used for idling (personality pose).</summary>
        public string IdleState = "Idle";
    }

    /// <summary>A family of interchangeable materials (e.g. all bean skins); any renderer slot using one is swapped.</summary>
    [Serializable]
    public sealed class MaterialGroup
    {
        public string Name = "";
        public Material[] Members = new Material[0];
    }

    /// <summary>Applies looks to one shared model (all accessories are children; only the look's ones stay active).</summary>
    [Serializable]
    public sealed class CharacterAppearance
    {
        public Renderer[] Renderers = new Renderer[0];
        public GameObject[] AllAccessories = new GameObject[0];
        public MaterialGroup[] Groups = new MaterialGroup[0];
        public CharacterLook[] Looks = new CharacterLook[0];

        public int IndexOf(string id)
        {
            for (int i = 0; i < Looks.Length; i++) if (Looks[i] != null && Looks[i].Id == id) return i;
            return -1;
        }

        /// <summary>Applies look <paramref name="index"/> (wrapped). Allocates a little; call on look changes only.</summary>
        public CharacterLook Apply(int index)
        {
            if (Looks == null || Looks.Length == 0) return null;
            var look = Looks[((index % Looks.Length) + Looks.Length) % Looks.Length];
            for (int i = 0; i < AllAccessories.Length; i++)
            {
                var a = AllAccessories[i];
                if (a != null) a.SetActive(Array.IndexOf(look.Accessories, a) >= 0);
            }
            for (int r = 0; r < Renderers.Length; r++)
            {
                var rend = Renderers[r];
                if (rend == null) continue;
                var mats = rend.sharedMaterials;
                bool changed = false;
                for (int m = 0; m < mats.Length; m++)
                    for (int g = 0; g < Groups.Length && g < look.Swaps.Length; g++)
                        if (look.Swaps[g] != null && mats[m] != null && Array.IndexOf(Groups[g].Members, mats[m]) >= 0 && mats[m] != look.Swaps[g])
                        {
                            mats[m] = look.Swaps[g];
                            changed = true;
                        }
                if (changed) rend.sharedMaterials = mats;
            }
            return look;
        }
    }

    /// <summary>
    /// Expressive face: two small patches (eyes+brows, mouth) on the head sample cells of one 512² atlas. Each cell
    /// has its own material (offset baked), so switching is a sharedMaterial swap: no property blocks, SRP-batcher
    /// friendly, zero allocations. Adds blinking and lip flap.
    /// </summary>
    [Serializable]
    public sealed class FaceRig
    {
        public Renderer Eyes;
        public Renderer Mouth;
        /// <summary>Atlas cells 0..7: neutral, happy, sneaky, shocked, angry, sad, blink, laugh.</summary>
        public Material[] EyeCells = new Material[0];
        /// <summary>Atlas cells 8..15: neutral, happy, smirk, shocked, angry(teeth), sad, talk A, talk E.</summary>
        public Material[] MouthCells = new Material[0];

        const int EyeBlink = 6, MouthTalkA = 6, MouthTalkE = 7;
        // (eye cell, mouth cell) per FaceExpression; mirrored in Art/Scripts/char_preview.py EXPRESSIONS.
        static readonly byte[] EyeOf = { 0, 1, 2, 3, 4, 5, 0, 7, 2, 4, 0, 5 };
        static readonly byte[] MouthOf = { 0, 1, 2, 3, 4, 5, 6, 1, 1, 5, 1, 4 };

        FaceExpression _base = FaceExpression.Neutral;
        int _eyeShown = -1, _mouthShown = -1;
        float _nextBlink, _blinkUntil, _nextFlap;
        int _flapCell;
        uint _rng = 2463534242u;

        public void Init(int seed)
        {
            _rng ^= (uint)seed * 2654435761u;
            if (_rng == 0) _rng = 1;
            _nextBlink = Time.time + 1f + Rand() * 3f;
        }

        /// <summary>Resting face of the look (used whenever nothing else is requested).</summary>
        public void SetBase(FaceExpression e) { _base = e; }

        float Rand()
        {
            _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
            return (_rng & 0xFFFFFF) / 16777216f;
        }

        /// <summary>Call every frame (cheap; only swaps materials when a cell changes).</summary>
        public void Show(FaceExpression e, bool talking, float now)
        {
            if (e == FaceExpression.Neutral) e = _base;
            int idx = (int)e;
            if (idx < 0 || idx >= EyeOf.Length) idx = 0;
            int eye = EyeOf[idx];
            int mouth = MouthOf[idx];
            // blink (only on open-eyed cells)
            if (now >= _nextBlink)
            {
                _blinkUntil = now + 0.11f;
                _nextBlink = now + 2.2f + Rand() * 3.5f;
                if (Rand() < 0.15f) _nextBlink = now + 0.25f; // occasional double blink
            }
            if (now < _blinkUntil && (eye == 0 || eye == 2 || eye == 4 || eye == 5)) eye = EyeBlink;
            // lip flap
            if (talking || e == FaceExpression.Talking)
            {
                if (now >= _nextFlap)
                {
                    _nextFlap = now + 0.07f + Rand() * 0.09f;
                    float r = Rand();
                    _flapCell = r < 0.4f ? MouthTalkA : (r < 0.75f ? MouthTalkE : (mouth == MouthTalkA ? 0 : mouth));
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
    }
}
