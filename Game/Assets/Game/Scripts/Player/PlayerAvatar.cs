using System.Collections.Generic;
using BirimizKovulacak.Core;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using EventType = BirimizKovulacak.Core.EventType;

namespace BirimizKovulacak.Game
{

    /// <summary>
    /// A player's body in the office. Movement is driven by whoever owns it (local input, a network bot, or a
    /// host-side virtual bot). Appearance and animation are derived from the public view only.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerAvatar : NetworkBehaviour
    {
        public static readonly List<PlayerAvatar> All = new List<PlayerAvatar>();
        public static PlayerAvatar Local { get; private set; }

        public readonly NetworkVariable<int> SlotVar = new NetworkVariable<int>(-1);
        public int Slot => SlotVar.Value;

        [Header("Wiring (set by ProjectBuilder)")]
        public Animator Animator;
        public Transform ModelRoot;
        public Transform CarrySocket;
        public Transform HandSocket;
        public SkinnedMeshRenderer BodyRenderer;
        public GameObject[] Accessories;      // Acc_Collar, Acc_Tie, Acc_HairTuft, Acc_Glasses, Acc_HairBun, Acc_Headset
        public SkinnedMeshRenderer TieRenderer;
        public Material[] SkinMaterials;       // per slot
        public Material TieRed, TieGreen;
        public GameObject PhoneHandset;
        public GameObject FiredBox;
        public TMPro.TextMeshPro NameTag;

        public const float WalkSpeed = 3.6f;
        public const float SlowSpeed = 1.0f;
        const float Gravity = -20f;

        CharacterController _cc;
        Vector3 _velocity;
        float _verticalVel;
        Vector3 _lastPos;
        float _visualSpeed;
        string _currentAnim = "";
        float _panicUntil;
        bool _movementLocked;
        int _appliedSlot = -2;

        /// <summary>True when this process should move this avatar (owner = this client, or host for bot avatars).</summary>
        public bool CanDrive => IsSpawned && IsOwner;
        public bool IsVirtualBot { get; set; }
        public float CurrentSpeed => _visualSpeed;

        static readonly string[][] SlotAccessories =
        {
            new[] { "Acc_Collar", "Acc_Tie", "Acc_HairTuft" },
            new[] { "Acc_Collar", "Acc_Tie", "Acc_Glasses" },
            new[] { "Acc_HairBun" },
            new[] { "Acc_Collar", "Acc_Headset" },
        };
        static readonly Vector3[] SlotScale =
        {
            new Vector3(1.0f, 1.08f, 1.0f),
            new Vector3(1.1f, 0.95f, 1.1f),
            new Vector3(1.0f, 1.0f, 1.0f),
            new Vector3(0.98f, 0.9f, 0.98f),
        };

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            if (IsOwner && !IsVirtualBot && IsLocalPlayer) Local = this;
            SlotVar.OnValueChanged += (_, __) => ApplyAppearance();
            ApplyAppearance();
            _lastPos = transform.position;
            if (ClientState.I != null) ClientState.I.PhaseChanged += OnPhaseChanged;
            if (ClientState.I != null) ClientState.I.EventReceived += OnEvent;
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            if (Local == this) Local = null;
            if (ClientState.I != null) ClientState.I.PhaseChanged -= OnPhaseChanged;
            if (ClientState.I != null) ClientState.I.EventReceived -= OnEvent;
        }

        public void ServerAssignSlot(int slot)
        {
            if (IsServer) SlotVar.Value = slot;
        }

        public static PlayerAvatar BySlot(int slot)
        {
            for (int i = 0; i < All.Count; i++) if (All[i].Slot == slot) return All[i];
            return null;
        }

        void ApplyAppearance()
        {
            int slot = Slot;
            if (slot == _appliedSlot) return;
            _appliedSlot = slot;
            if (slot < 0 || slot >= 4) return;
            if (BodyRenderer != null && SkinMaterials != null && slot < SkinMaterials.Length)
            {
                var mats = BodyRenderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && mats[i].name.StartsWith("M_Skin")) mats[i] = SkinMaterials[slot];
                BodyRenderer.sharedMaterials = mats;
            }
            if (Accessories != null)
                foreach (var a in Accessories)
                    if (a != null) a.SetActive(System.Array.IndexOf(SlotAccessories[slot], a.name) >= 0);
            if (TieRenderer != null) TieRenderer.sharedMaterial = slot == 1 ? TieGreen : TieRed;
            if (ModelRoot != null) ModelRoot.localScale = SlotScale[slot];
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
            if (e.Type == EventType.ManagerWarning) _panicUntil = Time.time + 1.4f;
        }

        // ---------------------------------------------------------------- presentation (all peers)

        void LateUpdate()
        {
            Vector3 p = transform.position;
            Vector3 d = p - _lastPos; d.y = 0;
            float inst = d.magnitude / Mathf.Max(Time.deltaTime, 1e-3f);
            _visualSpeed = Mathf.Lerp(_visualSpeed, inst, 0.25f);
            _lastPos = p;
            UpdateAnimation();
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

        void UpdateAnimation()
        {
            if (Animator == null) return;
            var st = PublicState;
            var view = ClientState.I != null ? ClientState.I.View : null;
            string anim;
            bool carrying = st != null && !string.IsNullOrEmpty(st.CarryingItemId);
            bool moving = _visualSpeed > 0.25f;
            if (view != null && view.Phase == MatchPhase.Results && view.Result != null)
            {
                bool fired = view.Result.FiredPlayer == Slot;
                bool won = false;
                foreach (var o in view.Result.Players) if (o.Player == Slot) won = o.Won;
                anim = fired ? "Fired" : (won ? "Cheer" : "Idle");
            }
            else if (view != null && view.Phase == MatchPhase.MeetingVerdict && view.Meeting.Verdict != null && view.Meeting.Verdict.FiredPlayer == Slot)
                anim = "Panic";
            else if (view != null && (view.Phase == MatchPhase.MeetingDefense1 || view.Phase == MatchPhase.MeetingDefense2) && view.Meeting.Speaker == Slot)
                anim = "Talk";
            else if (st != null && st.Activity != PublicActivity.None)
                anim = ActivityAnim(st.Activity);
            else if (Time.time < _panicUntil)
                anim = "Panic";
            else if (moving)
                anim = carrying ? "CarryWalk" : "Walk";
            else if (st != null && st.WorkPose)
                anim = "WorkPose";
            else
                anim = carrying ? "CarryIdle" : "Idle";

            if (anim != _currentAnim)
            {
                _currentAnim = anim;
                Animator.CrossFadeInFixedTime(anim, 0.15f);
            }
            Animator.speed = (anim == "Walk" || anim == "CarryWalk") ? Mathf.Clamp(_visualSpeed / 2.4f, 0.5f, 1.6f) : 1f;
            if (PhoneHandset != null) PhoneHandset.SetActive(anim == "Phone");
            if (FiredBox != null) FiredBox.SetActive(anim == "Fired");
        }

        static string ActivityAnim(PublicActivity a)
        {
            switch (a)
            {
                case PublicActivity.Typing: return "Work";
                case PublicActivity.OnPhone: return "Phone";
                case PublicActivity.Rummaging:
                case PublicActivity.Fiddling: return "Sneak";
                case PublicActivity.Brewing:
                case PublicActivity.Cleaning:
                case PublicActivity.Spilling:
                case PublicActivity.Placing:
                case PublicActivity.Delivering:
                case PublicActivity.PickingUp:
                default: return "Interact";
            }
        }
    }
}
