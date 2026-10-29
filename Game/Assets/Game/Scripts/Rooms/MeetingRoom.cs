using System;
using System.Collections.Generic;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// The end-of-day meeting: up to six seated colleagues (seat order alternates sides, nearest to the manager first),
    /// the manager standing at the head beside the projector screen, talk animation from voice levels, reactions,
    /// the defense spotlight, the fired player with a cardboard box. Characters are instantiated from the
    /// Seat_*/Boss_* prefabs on <see cref="Enter"/> and reused while the look does not change.
    /// </summary>
    public sealed class MeetingRoom : MonoBehaviour
    {
        [Serializable]
        public sealed class SeatRig
        {
            public Transform Anchor;
            public Renderer ChairFabric;
            public GameObject Items;
        }

        public SeatRig[] Seats = new SeatRig[0];
        /// <summary>Player looks by colour index (Mavi, Sarı, Pembe, Yeşil, Turuncu, Mor).</summary>
        public SeatedBean[] BeanPrefabs = new SeatedBean[0];
        public string[] BossIds = new string[0];
        public SeatedBean[] BossPrefabs = new SeatedBean[0];
        public Transform ManagerMount;
        public Material[] SeatColors = new Material[0];
        public Material ChairNeutral;

        [Header("Projector")]
        public Renderer Canvas;
        public Texture DefaultSlideTr, DefaultSlideEn;
        public Renderer Beam;

        [Header("Spotlight")]
        public Light Spot;
        public Renderer SpotCone;
        public Light[] DimWithSpot = new Light[0];
        public GameObject FiredBox;

        [Header("Seat camera")]
        public float EyeUp = 1.24f, EyeForward = 0.06f, YawLimit = 110f, PitchUp = 40f, PitchDown = 55f;

        public const int MaxSlots = 8;
        readonly SeatedBean[] _beans = new SeatedBean[6];
        readonly int[] _slotOfSeat = { -1, -1, -1, -1, -1, -1 };
        readonly int[] _seatOfSlot = new int[MaxSlots];
        readonly float[] _level = new float[MaxSlots];
        readonly float[] _talkUntil = new float[MaxSlots];
        readonly bool[] _connected = new bool[MaxSlots];
        SeatedBean _manager;
        string _managerId;
        int _local = -1;
        int _spot = -1, _fired = -1;
        bool _managerTalking;
        float _spotBlend;
        float[] _dimBase;
        Material _canvasMat;
        Texture _projector;
        bool _projectorSet;
        string _lang;
        int _signature;
        readonly List<MeetingSeatInfo> _sorted = new List<MeetingSeatInfo>(8);

        public int LocalSeat => _local >= 0 && _local < MaxSlots ? _seatOfSlot[_local] : -1;
        public SeatedBean Manager => _manager;
        /// <summary>Who a default cutscene frames: the fired player, else the defender, else -1 (the manager).</summary>
        public int CutsceneSubject => _fired >= 0 ? _fired : _spot;

        void Awake()
        {
            for (int i = 0; i < MaxSlots; i++) _seatOfSlot[i] = -1;
            if (Canvas != null) _canvasMat = Canvas.material;
            _dimBase = new float[DimWithSpot.Length];
            for (int i = 0; i < DimWithSpot.Length; i++) _dimBase[i] = DimWithSpot[i] != null ? DimWithSpot[i].intensity : 0f;
            if (Spot != null) Spot.enabled = false;
            if (SpotCone != null) SpotCone.enabled = false;
            if (FiredBox != null) FiredBox.SetActive(false);
        }

        // ------------------------------------------------------------------ seats

        public void Enter(int localSlot, IReadOnlyList<MeetingSeatInfo> seats, string managerId)
        {
            int sig = localSlot * 7919 + (managerId ?? "").GetHashCode();
            if (seats != null)
                for (int i = 0; i < seats.Count; i++)
                    sig = sig * 31 + seats[i].Slot * 97 + seats[i].ColorIndex * 13 + (seats[i].Connected ? 1 : 0);
            if (sig == _signature && _manager != null) return;
            _signature = sig;
            _local = localSlot;
            _sorted.Clear();
            if (seats != null) for (int i = 0; i < seats.Count; i++) _sorted.Add(seats[i]);
            _sorted.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            for (int i = 0; i < MaxSlots; i++) { _seatOfSlot[i] = -1; _connected[i] = false; }
            for (int s = 0; s < _slotOfSeat.Length; s++) _slotOfSeat[s] = -1;
            for (int i = 0; i < _sorted.Count && i < Seats.Length; i++)
            {
                var info = _sorted[i];
                if (info.Slot < 0 || info.Slot >= MaxSlots) continue;
                _slotOfSeat[i] = info.Slot;
                _seatOfSlot[info.Slot] = i;
                _connected[info.Slot] = info.Connected;
            }
            for (int s = 0; s < Seats.Length && s < _beans.Length; s++)
            {
                var rig = Seats[s];
                int slot = _slotOfSeat[s];
                bool occupied = slot >= 0;
                MeetingSeatInfo info = default;
                if (occupied) for (int i = 0; i < _sorted.Count; i++) if (_sorted[i].Slot == slot) info = _sorted[i];
                int color = ((info.ColorIndex % 6) + 6) % 6;
                if (rig.ChairFabric != null)
                    rig.ChairFabric.sharedMaterial = occupied && color < SeatColors.Length ? SeatColors[color] : ChairNeutral;
                if (rig.Items != null) rig.Items.SetActive(occupied);
                bool wantBean = occupied && slot != localSlot;
                string look = color.ToString();
                if (_beans[s] != null && (!wantBean || _beans[s].LookId != look))
                {
                    Destroy(_beans[s].gameObject);
                    _beans[s] = null;
                }
                if (wantBean && _beans[s] == null && color < BeanPrefabs.Length && BeanPrefabs[color] != null && rig.Anchor != null)
                {
                    var b = Instantiate(BeanPrefabs[color], rig.Anchor.position, rig.Anchor.rotation, transform);
                    b.LookId = look;
                    b.name = "Bean_seat" + s;
                    var target = b.GetComponent<RoomTarget>();
                    if (target != null) target.Seat = s;
                    _beans[s] = b;
                }
                if (_beans[s] != null)
                {
                    _beans[s].SetAway(!info.Connected);
                    _beans[s].SetFired(false);
                }
            }
            string mid = string.IsNullOrEmpty(managerId) ? "default" : managerId;
            if (_manager == null || _managerId != mid)
            {
                if (_manager != null) Destroy(_manager.gameObject);
                int idx = Array.IndexOf(BossIds, mid);
                if (idx < 0) idx = Array.IndexOf(BossIds, "default");
                if (idx < 0) idx = 0;
                if (idx < BossPrefabs.Length && BossPrefabs[idx] != null && ManagerMount != null)
                {
                    _manager = Instantiate(BossPrefabs[idx], ManagerMount.position, ManagerMount.rotation, transform);
                    _manager.name = "Manager_" + mid;
                    _manager.LookId = mid;
                }
                _managerId = mid;
            }
            _fired = -1;
            _spot = -1;
            if (FiredBox != null) FiredBox.SetActive(false);
        }

        /// <summary>Slot sitting at a seat index, -1 if empty.</summary>
        public int SlotAtSeat(int seat) { return seat >= 0 && seat < _slotOfSeat.Length ? _slotOfSeat[seat] : -1; }

        public bool TryGetEye(int slot, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            int s = slot >= 0 && slot < MaxSlots ? _seatOfSlot[slot] : -1;
            if (s < 0 || s >= Seats.Length || Seats[s].Anchor == null) return false;
            var a = Seats[s].Anchor;
            pos = a.position + Vector3.up * EyeUp + a.forward * EyeForward;
            rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(a.forward, Vector3.up).normalized, Vector3.up);
            return true;
        }

        /// <summary>Head position of whoever sits at a slot (the camera eye for the local player).</summary>
        public bool TryGetHead(int slot, Vector3 localEye, out Vector3 head)
        {
            head = Vector3.zero;
            if (slot == _local) { head = localEye; return true; }
            int s = slot >= 0 && slot < MaxSlots ? _seatOfSlot[slot] : -1;
            if (s < 0 || _beans[s] == null) return false;
            head = _beans[s].Head != null ? _beans[s].Head.position + Vector3.up * 0.25f : _beans[s].transform.position + Vector3.up * 1.3f;
            return true;
        }

        public Transform SeatAnchorOfSlot(int slot)
        {
            int s = slot >= 0 && slot < MaxSlots ? _seatOfSlot[slot] : -1;
            return s >= 0 && s < Seats.Length ? Seats[s].Anchor : null;
        }

        // ------------------------------------------------------------------ inputs

        public void SetSpeaking(int slot, float level)
        {
            if (slot < 0 || slot >= MaxSlots) return;
            _level[slot] = Mathf.Clamp01(level);
            if (level > 0.08f) _talkUntil[slot] = Time.time + 0.35f;
        }

        public void React(int slot, MeetingReaction r)
        {
            var b = BeanOf(slot);
            if (b != null) b.React(r);
        }

        public void SetManagerTalking(bool talking) { _managerTalking = talking; }

        public void SetSpotlight(int slot) { _spot = slot; }

        public void SetFired(int slot)
        {
            if (slot == _fired) return;
            _fired = slot;
            for (int s = 0; s < _beans.Length; s++) if (_beans[s] != null) _beans[s].SetFired(_slotOfSeat[s] == slot && slot >= 0);
            if (FiredBox != null)
            {
                bool localFired = slot >= 0 && slot == _local;
                FiredBox.SetActive(localFired);
                var a = SeatAnchorOfSlot(slot);
                if (localFired && a != null)
                    FiredBox.transform.SetPositionAndRotation(a.position + a.forward * 0.62f + Vector3.up * 0.752f, Quaternion.LookRotation(-a.forward, Vector3.up) * Quaternion.Euler(0f, 12f, 0f));
            }
        }

        public void SetProjector(Texture tex)
        {
            _projector = tex;
            _projectorSet = true;
            ApplyCanvas();
        }

        void ApplyCanvas()
        {
            if (_canvasMat == null) return;
            var t = _projector;
            if (t == null) t = Loc.Language == "en" && DefaultSlideEn != null ? DefaultSlideEn : DefaultSlideTr;
            _canvasMat.SetTexture("_BaseMap", t);
            _lang = Loc.Language;
        }

        SeatedBean BeanOf(int slot)
        {
            int s = slot >= 0 && slot < MaxSlots ? _seatOfSlot[slot] : -1;
            return s >= 0 && s < _beans.Length ? _beans[s] : null;
        }

        // ------------------------------------------------------------------ frame

        public void Tick(Vector3 localEye, float dt)
        {
            float now = Time.time;
            if (!_projectorSet || (_projector == null && _lang != Loc.Language)) { _projectorSet = true; ApplyCanvas(); }
            // who has the floor: spotlight > the loudest talker > the manager
            int speaker = -1;
            float best = 0.05f;
            for (int i = 0; i < MaxSlots; i++)
                if (now < _talkUntil[i] && _level[i] + 0.1f > best) { best = _level[i] + 0.1f; speaker = i; }
            Vector3 focus = Vector3.zero;
            bool hasFocus = false;
            int focusSlot = _spot >= 0 ? _spot : (_fired >= 0 ? _fired : speaker);
            if (focusSlot >= 0) hasFocus = TryGetHead(focusSlot, localEye, out focus);
            if (!hasFocus && _manager != null && _managerTalking)
            {
                focus = _manager.Head != null ? _manager.Head.position + Vector3.up * 0.2f : _manager.transform.position + Vector3.up * 1.6f;
                hasFocus = true;
            }
            for (int s = 0; s < _beans.Length; s++)
            {
                var b = _beans[s];
                if (b == null) continue;
                int slot = _slotOfSeat[s];
                bool talking = slot >= 0 && now < _talkUntil[slot];
                b.SetTalking(talking, slot >= 0 ? _level[slot] : 0f);
                b.SetSpotlit(slot == _spot && slot >= 0);
                if (hasFocus && focusSlot != slot) b.LookAt(focus);
                else if (_manager != null && !talking) b.LookAt(_manager.transform.position + Vector3.up * 1.6f);
                else b.ClearLook();
            }
            if (_manager != null)
            {
                _manager.SetTalking(_managerTalking, 0.7f);
                if (_fired >= 0 && TryGetHead(_fired, localEye, out var fh)) _manager.LookAt(fh);
                else if (hasFocus && !_managerTalking) _manager.LookAt(focus);
                else _manager.LookAt(localEye);
            }
            TickSpot(localEye, dt);
        }

        void TickSpot(Vector3 localEye, float dt)
        {
            bool on = _spot >= 0;
            _spotBlend = Mathf.MoveTowards(_spotBlend, on ? 1f : 0f, dt * 2.2f);
            for (int i = 0; i < DimWithSpot.Length; i++)
                if (DimWithSpot[i] != null) DimWithSpot[i].intensity = _dimBase[i] * Mathf.Lerp(1f, 0.2f, _spotBlend);
            if (Spot == null) return;
            var a = SeatAnchorOfSlot(_spot);
            if (on && a != null)
            {
                Vector3 head = a.position + Vector3.up * 1.15f;
                Vector3 lamp = a.position + Vector3.up * 2.75f + a.forward * 0.35f;
                Spot.transform.SetPositionAndRotation(lamp, Quaternion.LookRotation(head - lamp, a.forward));
                if (SpotCone != null) SpotCone.transform.SetPositionAndRotation(lamp, Quaternion.LookRotation(head - lamp, a.forward));
            }
            bool visible = _spotBlend > 0.01f;
            if (Spot.enabled != visible) Spot.enabled = visible;
            Spot.intensity = 14f * _spotBlend;
            if (SpotCone != null && SpotCone.enabled != visible) SpotCone.enabled = visible;
        }
    }
}
