using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 step 2 task objects of the office (docs/V5_INTERACTION.md §2): the Petty Cash box on the desk (lid, the notes
    /// in the box or on the desk beside it, the right mitten that takes them / puts them back, the İhbar button, the red
    /// seal when both takers are caught), the Weekend Shift roster on the wall (a pencil on a string that writes and
    /// erases; the sheet itself is drawn by the UI), and the prim stack in the desk drawer (bundles of five, loose notes,
    /// a paper tag with the exact number; the drawer jolts when prim comes in). The UI decides the states
    /// (OfficeObjects); everything here is presentation and runs locally.
    /// </summary>
    public sealed partial class OfficeRoom
    {
        [Header("Petty Cash box (v5 step 2)")]
        public Transform CashBox;
        public Transform CashLid, CashSlot, CashPile;
        public Renderer CashButton;
        public Material CashButtonOff, CashButtonOn;
        public Light CashButtonLight;
        public GameObject CashSeal;
        public TextMeshPro[] CashLabels = new TextMeshPro[0];
        public float CashLidOpen = 104f;

        [Header("Weekend Shift roster (v5 step 2)")]
        public Transform Roster, RosterPen, RosterStringBase;
        public Renderer RosterSheet;
        public LineRenderer RosterString;
        /// <summary>Pencil length tip → eraser end (local +Y).</summary>
        public float PenLength = 0.15f;

        [Header("Drawer prim stack (v5 step 2)")]
        public Transform DrawerCash, PrimTag;
        public TextMeshPro PrimTagText;
        public GameObject BillTemplate, BundleTemplate;

        // ---- cash box
        const int MaxBills = 6;
        bool _cashShown;
        float _cashAppear = 1f, _lid, _btnPress, _btnGlow;
        bool _lidWant, _seal, _btnLit, _btnDown;
        Vector3 _cashRestPos, _btnRest;
        Quaternion _lidRest = Quaternion.identity;
        readonly List<Transform> _cashBills = new List<Transform>();
        readonly int[] _billPlace = new int[MaxBills];      // 0 in the box, 1 on the desk, 2 gone
        readonly float[] _billT = new float[MaxBills];
        readonly Vector3[] _billFromPos = new Vector3[MaxBills];
        readonly Quaternion[] _billFromRot = new Quaternion[MaxBills];
        int _billCount = 3;
        // the right mitten carrying the notes (take / put back while the lid is open)
        float _grabStart = -10f;
        bool _grabToPile;
        const float GrabTime = 1.05f;

        // ---- roster pencil
        enum PenMode { Rest, Write, Erase }
        PenMode _pen;
        float _penStart = -10f, _penDur;
        Vector3 _penA, _penB;
        Vector3 _penRestPos;
        Quaternion _penRestRot;
        const int StringPoints = 24;
        readonly Vector3[] _string = new Vector3[StringPoints];

        // ---- drawer
        bool _drawerHeld;
        float _drawerKick = -10f;
        int _prim = -1;
        readonly List<Transform> _bundles = new List<Transform>(), _loose = new List<Transform>();
        readonly List<Transform> _dying = new List<Transform>();
        readonly List<float> _dyingAt = new List<float>();
        readonly Dictionary<Transform, float> _born = new Dictionary<Transform, float>();
        readonly Dictionary<Transform, Vector3> _home = new Dictionary<Transform, Vector3>();

        // ---- hands (presence 0..1: the resting mittens slide back toward you and hide when you look away)
        float _presL = 1f, _presR = 1f;
        bool _grabWas;

        public bool CashBoxShown => _cashShown && CashBox != null;
        /// <summary>The pencil is away from its loop (writing or erasing).</summary>
        public bool PenBusy => _pen != PenMode.Rest || Time.time - _penStart < _penDur + 0.9f;

        void InitTasks()
        {
            if (CashBox != null) { _cashRestPos = CashBox.localPosition; CashBox.gameObject.SetActive(false); }
            if (CashLid != null) _lidRest = CashLid.localRotation;
            if (CashButton != null) _btnRest = CashButton.transform.localPosition;
            if (CashSeal != null) CashSeal.SetActive(false);
            if (CashButtonLight != null) { CashButtonLight.intensity = 0f; CashButtonLight.enabled = false; }
            if (RosterPen != null) { _penRestPos = RosterPen.localPosition; _penRestRot = RosterPen.localRotation; }
            if (RosterString != null) { RosterString.positionCount = StringPoints; RosterString.useWorldSpace = true; }
            if (BillTemplate != null) BillTemplate.SetActive(false);
            if (BundleTemplate != null) BundleTemplate.SetActive(false);
            if (PrimTag != null) PrimTag.gameObject.SetActive(false);
            for (int i = 0; i < MaxBills; i++) _billPlace[i] = 0;
        }

        void ApplyTaskLabels()
        {
            // [0] lid tag, [1] under the İhbar button, [2] the seal (keys in tr/en.v5.json)
            string[] keys = { "v5.office.cash.lid_label", "v5.office.cash.button_label", "v5.office.cash.seal_label" };
            for (int i = 0; i < CashLabels.Length && i < keys.Length; i++)
                if (CashLabels[i] != null) CashLabels[i].text = Loc.T(keys[i]);
        }

        // ------------------------------------------------------------------ cash box API

        /// <summary>
        /// The Petty Cash box: <paramref name="shown"/> (Petty Cash TaskPlay → its break), how many notes it holds
        /// (<paramref name="count"/>, the config's take amount), where they are (<paramref name="onDesk"/>: taken out and
        /// lying beside the box; <paramref name="gone"/>: nobody's any more, e.g. a partner took them or a whistle took
        /// them back), the lid, the İhbar button (lit for a witness, pressed after the whistle) and the seal.
        /// </summary>
        public void SetCashBox(bool shown, int count, bool onDesk, bool gone, bool lidOpen, bool buttonLit, bool buttonDown, bool seal)
        {
            Init();
            if (CashBox == null) return;
            if (shown != _cashShown)
            {
                _cashShown = shown;
                CashBox.gameObject.SetActive(shown);
                _cashAppear = shown && Time.timeSinceLevelLoad > 2f ? 0f : 1f;
                if (!shown) foreach (var b in _cashBills) if (b != null) b.gameObject.SetActive(false);
                if (shown) { _lid = 0f; _grabStart = -10f; SnapBills(onDesk, gone); }
            }
            if (!shown) return;
            _billCount = Mathf.Clamp(count, 1, MaxBills);
            EnsureBills();
            int want = gone ? 2 : onDesk ? 1 : 0;
            for (int i = 0; i < _billCount && i < _cashBills.Count; i++)
            {
                if (_billPlace[i] == want) continue;
                bool wasGone = _billPlace[i] == 2;
                _billFromPos[i] = _cashBills[i].position;
                _billFromRot[i] = _cashBills[i].rotation;
                _billPlace[i] = want;
                _billT[i] = -i * 0.07f; // stagger
                // never fly through a closed lid, never fly in from nowhere: those changes just happen
                if ((want == 0 && _lid < 0.5f) || wasGone)
                {
                    Pose(i, want, out var p, out var r);
                    _cashBills[i].SetPositionAndRotation(p, r);
                    _cashBills[i].localScale = Vector3.one;
                    _billT[i] = 1f;
                }
            }
            _lidWant = lidOpen && !seal;
            _btnLit = buttonLit;
            _btnDown = buttonDown;
            _seal = seal;
            if (CashSeal != null && CashSeal.activeSelf != seal) CashSeal.SetActive(seal);
        }

        /// <summary>The right mitten reaches into the open box and carries the notes to the desk beside it (or back).</summary>
        public void CashGrab(bool toDesk)
        {
            if (!_cashShown || _lid < 0.6f) return;
            _grabStart = Time.time;
            _grabToPile = toDesk;
        }

        bool Grabbing => Time.time - _grabStart < GrabTime;

        void EnsureBills()
        {
            while (_cashBills.Count < _billCount && BillTemplate != null)
            {
                var go = Instantiate(BillTemplate, CashBox);
                go.name = "CashNote" + _cashBills.Count;
                go.SetActive(true);
                _cashBills.Add(go.transform);
                int i = _cashBills.Count - 1;
                Pose(i, _billPlace[i], out var p, out var r);
                go.transform.SetPositionAndRotation(p, r);
                _billT[i] = 1f;
            }
            for (int i = 0; i < _cashBills.Count; i++)
            {
                bool on = i < _billCount;
                if (_cashBills[i].gameObject.activeSelf != on && (!on || _billPlace[i] != 2)) _cashBills[i].gameObject.SetActive(on);
            }
        }

        void SnapBills(bool onDesk, bool gone)
        {
            int want = gone ? 2 : onDesk ? 1 : 0;
            for (int i = 0; i < MaxBills; i++) { _billPlace[i] = want; _billT[i] = 1f; }
            for (int i = 0; i < _cashBills.Count; i++)
            {
                Pose(i, want, out var p, out var r);
                _cashBills[i].SetPositionAndRotation(p, r);
                _cashBills[i].localScale = Vector3.one;
                _cashBills[i].gameObject.SetActive(want != 2 && i < _billCount);
            }
        }

        static readonly float[] BillYaw = { -3f, 4f, 9f, -7f, 2f, 12f };

        /// <summary>Resting pose of note i in the box (place 0) or on the desk (place 1).</summary>
        void Pose(int i, int place, out Vector3 pos, out Quaternion rot)
        {
            var anchor = place == 1 && CashPile != null ? CashPile : CashSlot != null ? CashSlot : CashBox;
            float k = i - (_billCount - 1) * 0.5f;
            var local = place == 1 ? new Vector3(k * 0.006f, 0.0006f + i * 0.0019f, k * 0.004f) : new Vector3(k * 0.004f, i * 0.0019f, k * 0.003f);
            pos = anchor.TransformPoint(local);
            rot = anchor.rotation * Quaternion.Euler(0f, BillYaw[i % BillYaw.Length] * (place == 1 ? 1.6f : 1f), 0f);
        }

        void TickCashBox(Camera cam, float now, float dt)
        {
            if (CashBox == null || !_cashShown) return;
            // drops onto the desk when the task starts
            _cashAppear = Mathf.MoveTowards(_cashAppear, 1f, dt / 0.42f);
            float a = 1f - _cashAppear;
            float bounce = _cashAppear > 0.78f ? Mathf.Sin((_cashAppear - 0.78f) / 0.22f * Mathf.PI) * 0.006f : 0f;
            CashBox.localPosition = _cashRestPos + Vector3.up * (0.16f * a * a * a + bounce);
            // lid
            _lid = Mathf.MoveTowards(_lid, _lidWant ? 1f : 0f, dt * (_lidWant ? 2.6f : 3.4f));
            if (CashLid != null)
            {
                float e = _lid * _lid * (3f - 2f * _lid);
                float over = _lidWant && _lid > 0.85f ? Mathf.Sin((_lid - 0.85f) / 0.15f * Mathf.PI) * 3f : 0f;
                CashLid.localRotation = _lidRest * Quaternion.Euler(CashLidOpen * e + over, 0f, 0f);
            }
            // İhbar button: lit (pulsing) for a witness, pressed in after the whistle
            _btnPress = Mathf.MoveTowards(_btnPress, _btnDown ? 1f : 0f, dt * 10f);
            if (CashButton != null)
            {
                CashButton.transform.localPosition = _btnRest + new Vector3(0f, 0f, 0.0022f * _btnPress);
                var m = _btnLit && !_btnDown ? CashButtonOn : CashButtonOff;
                if (m != null && CashButton.sharedMaterial != m) CashButton.sharedMaterial = m;
            }
            float glowWant = _btnLit && !_btnDown ? 0.55f + 0.45f * Mathf.Sin(now * 5.5f) : 0f;
            _btnGlow = Mathf.MoveTowards(_btnGlow, glowWant, dt * 6f);
            if (CashButtonLight != null)
            {
                CashButtonLight.intensity = _btnGlow * 0.07f;
                bool on = _btnGlow > 0.01f;
                if (CashButtonLight.enabled != on) CashButtonLight.enabled = on;
            }
            // the notes
            float g = Time.time - _grabStart;
            bool carried = g >= 0f && g < GrabTime;
            for (int i = 0; i < _billCount && i < _cashBills.Count; i++)
            {
                var b = _cashBills[i];
                int place = _billPlace[i];
                if (carried && place != 2) { CarryBill(i, g, cam); continue; }
                _billT[i] = Mathf.MoveTowards(_billT[i], 1f, dt / 0.5f);
                float t = Mathf.Clamp01(_billT[i]);
                if (place == 2)
                {
                    // taken away: shrink where it lies
                    float s = 1f - t;
                    b.localScale = Vector3.one * Mathf.Max(0.001f, s);
                    if (t >= 1f && b.gameObject.activeSelf) b.gameObject.SetActive(false);
                    continue;
                }
                if (!b.gameObject.activeSelf) { b.gameObject.SetActive(true); b.localScale = Vector3.one; }
                b.localScale = Vector3.one;
                Pose(i, place, out var to, out var rto);
                if (t >= 1f) { b.SetPositionAndRotation(to, rto); continue; }
                float e = t * t * (3f - 2f * t);
                Vector3 from = _billFromPos[i];
                Vector3 mid = (from + to) * 0.5f + Vector3.up * (0.1f + 0.3f * (from - to).magnitude);
                Vector3 p = Vector3.Lerp(Vector3.Lerp(from, mid, e), Vector3.Lerp(mid, to, e), e);
                b.SetPositionAndRotation(p, Quaternion.Slerp(_billFromRot[i], rto, e));
            }
        }

        /// <summary>Where the carried bunch is at grab time g (between the box and the desk), and the hand above it.</summary>
        void GrabPath(float g, Camera cam, out Vector3 bunch, out Vector3 hand, out bool holding)
        {
            Vector3 box = (CashSlot != null ? CashSlot : CashBox).position + Vector3.up * 0.004f;
            Vector3 desk = (CashPile != null ? CashPile : CashBox).position + Vector3.up * 0.003f;
            Vector3 src = _grabToPile ? box : desk, dst = _grabToPile ? desk : box;
            Vector3 off = cam != null ? cam.transform.TransformPoint(new Vector3(0.26f, -0.24f, 0.2f)) : src + Vector3.up * 0.3f;
            float u = g / GrabTime;
            const float hover = 0.028f;
            holding = u >= 0.32f && u < 0.76f;
            if (u < 0.3f)
            {
                float e = Ease(u / 0.3f);
                hand = Vector3.Lerp(off, src + Vector3.up * (hover + 0.05f * (1f - e)), e);
                bunch = src;
            }
            else if (u < 0.36f) { hand = src + Vector3.up * hover; bunch = src; }
            else if (u < 0.72f)
            {
                float e = Ease((u - 0.36f) / 0.36f);
                Vector3 mid = (src + dst) * 0.5f + Vector3.up * 0.12f;
                bunch = Vector3.Lerp(Vector3.Lerp(src, mid, e), Vector3.Lerp(mid, dst, e), e);
                hand = bunch + Vector3.up * hover;
            }
            else if (u < 0.78f) { bunch = dst; hand = dst + Vector3.up * hover; }
            else
            {
                float e = Ease((u - 0.78f) / 0.22f);
                bunch = dst;
                hand = Vector3.Lerp(dst + Vector3.up * hover, off, e);
            }
        }

        static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        void CarryBill(int i, float g, Camera cam)
        {
            var b = _cashBills[i];
            if (!b.gameObject.activeSelf) b.gameObject.SetActive(true);
            b.localScale = Vector3.one;
            GrabPath(g, cam, out var bunch, out _, out bool holding);
            int srcPlace = _grabToPile ? 0 : 1, dstPlace = _grabToPile ? 1 : 0;
            Pose(i, srcPlace, out var sp, out var sr);
            Pose(i, dstPlace, out var dp, out var dr);
            var srcAnchor = (srcPlace == 1 && CashPile != null ? CashPile : CashSlot != null ? CashSlot : CashBox).position;
            var dstAnchor = (dstPlace == 1 && CashPile != null ? CashPile : CashSlot != null ? CashSlot : CashBox).position;
            float u = g / GrabTime;
            if (u < 0.36f) { b.SetPositionAndRotation(sp, sr); return; }
            if (u >= 0.72f) { b.SetPositionAndRotation(dp, dr); _billT[i] = 1f; return; }
            float e = Ease((u - 0.36f) / 0.36f);
            Vector3 offset = Vector3.Lerp(sp - srcAnchor, dp - dstAnchor, e);
            b.SetPositionAndRotation(bunch + offset + Vector3.up * (holding ? 0.004f : 0f), Quaternion.Slerp(sr, dr, e));
            _billT[i] = 1f;
        }

        // ------------------------------------------------------------------ roster pencil API

        /// <summary>World point on the roster sheet at UV (0..1, origin bottom-left of the paper), lifted off it a little.</summary>
        public Vector3 SheetPoint(Vector2 uv, float lift = 0.0012f)
        {
            if (RosterSheet == null) return Roster != null ? Roster.position : transform.position;
            var mf = RosterSheet.GetComponent<MeshFilter>();
            var b = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one * 0.2f);
            var tr = RosterSheet.transform;
            var local = new Vector3(Mathf.Lerp(b.min.x, b.max.x, uv.x), Mathf.Lerp(b.min.y, b.max.y, uv.y), b.center.z);
            return tr.TransformPoint(local) - tr.forward * lift;
        }

        /// <summary>The pencil leaves its loop, writes (or, turned round, rubs out) along the line from
        /// <paramref name="from"/> to <paramref name="to"/> (sheet UVs) for <paramref name="seconds"/>, and goes back.</summary>
        public void PenStroke(Vector2 from, Vector2 to, bool erase, float seconds)
        {
            if (RosterPen == null) return;
            _pen = erase ? PenMode.Erase : PenMode.Write;
            _penStart = Time.time;
            _penDur = Mathf.Max(0.2f, seconds);
            _penA = SheetPoint(from);
            _penB = SheetPoint(to);
        }

        const float PenGo = 0.32f, PenBack = 0.45f;

        /// <summary>0..1 progress of the writing / rubbing part of the current stroke (for the sheet's ink).</summary>
        public float PenProgress => _pen == PenMode.Rest ? 1f : Mathf.Clamp01((Time.time - _penStart - PenGo) / _penDur);

        void TickRoster(Camera cam, float now, float dt)
        {
            if (RosterPen == null) return;
            var parent = RosterPen.parent;
            Vector3 restPos = parent != null ? parent.TransformPoint(_penRestPos) : _penRestPos;
            Quaternion restRot = parent != null ? parent.rotation * _penRestRot : _penRestRot;
            if (_pen == PenMode.Rest)
            {
                RosterPen.SetPositionAndRotation(restPos, restRot);
            }
            else
            {
                float t = now - _penStart;
                bool erase = _pen == PenMode.Erase;
                var sheet = RosterSheet != null ? RosterSheet.transform : Roster;
                Vector3 outN = sheet != null ? -sheet.forward : Vector3.back;
                Vector3 up = sheet != null ? sheet.up : Vector3.up, right = sheet != null ? sheet.right : Vector3.right;
                Vector3 axis = (outN * 0.55f + right * 0.32f + up * 0.78f).normalized;
                Quaternion workRot = erase ? Quaternion.LookRotation(Vector3.ProjectOnPlane(outN, -axis), -axis) : Quaternion.LookRotation(Vector3.ProjectOnPlane(outN, axis), axis);
                Vector3 contact;
                float lift = 0f;
                if (t < PenGo)
                {
                    float e = Ease(t / PenGo);
                    contact = _penA;
                    lift = 0.03f * (1f - e) + 0.012f * Mathf.Sin(e * Mathf.PI);
                    var p = WorkPos(contact + outN * lift, axis, erase);
                    RosterPen.SetPositionAndRotation(Vector3.Lerp(restPos, p, e) + outN * (0.04f * Mathf.Sin(e * Mathf.PI)), Quaternion.Slerp(restRot, workRot, e));
                }
                else if (t < PenGo + _penDur)
                {
                    float u = (t - PenGo) / _penDur;
                    if (erase)
                    {
                        // three passes back and forth over the name
                        float w = Mathf.PingPong(u * 6f, 1f);
                        contact = Vector3.Lerp(_penA, _penB, w) + up * (Mathf.Sin(u * 40f) * 0.0025f);
                    }
                    else
                    {
                        contact = Vector3.Lerp(_penA, _penB, u) + up * (Mathf.Sin(u * 55f) * 0.0028f + Mathf.Sin(u * 17f) * 0.0015f);
                        lift = Mathf.Max(0f, Mathf.Sin(u * 23f) - 0.92f) * 0.04f;
                    }
                    RosterPen.SetPositionAndRotation(WorkPos(contact + outN * lift, axis, erase), workRot);
                }
                else if (t < PenGo + _penDur + PenBack)
                {
                    float e = Ease((t - PenGo - _penDur) / PenBack);
                    var p = WorkPos(_penB + outN * 0.012f, axis, erase);
                    RosterPen.SetPositionAndRotation(Vector3.Lerp(p, restPos, e) + outN * (0.04f * Mathf.Sin(e * Mathf.PI)), Quaternion.Slerp(workRot, restRot, e));
                }
                else
                {
                    _pen = PenMode.Rest;
                    RosterPen.SetPositionAndRotation(restPos, restRot);
                }
            }
            UpdateString();
        }

        /// <summary>Pencil pivot (its tip) for a contact point: writing = the tip, rubbing = the eraser end.</summary>
        Vector3 WorkPos(Vector3 contact, Vector3 axis, bool erase) { return erase ? contact + axis * PenLength : contact; }

        void UpdateString()
        {
            if (RosterString == null || RosterStringBase == null || RosterPen == null) return;
            Vector3 a = RosterStringBase.position;
            Vector3 b = RosterPen.TransformPoint(new Vector3(0f, PenLength * 0.94f, 0f));
            float dist = Vector3.Distance(a, b);
            float slack = Mathf.Max(0f, 0.26f - dist);
            Vector3 mid = (a + b) * 0.5f + Vector3.down * (0.015f + slack * 0.4f);
            var sheet = RosterSheet != null ? RosterSheet.transform : null;
            if (sheet != null) mid -= sheet.forward * 0.004f;
            for (int i = 0; i < StringPoints; i++)
            {
                float t = i / (float)(StringPoints - 1), u = 1f - t;
                _string[i] = u * u * a + 2f * u * t * mid + t * t * b;
            }
            RosterString.SetPositions(_string);
        }

        // ------------------------------------------------------------------ drawer API

        /// <summary>Keeps the drawer open (the drawer view) or lets it close.</summary>
        public void SetDrawerHeld(bool held) { _drawerHeld = held; }

        /// <summary>Prim came in: the drawer jolts (heard as a thunk by the UI).</summary>
        public void DrawerThunk() { _drawerKick = Time.time; }

        float DrawerKickOffset(float now)
        {
            float k = now - _drawerKick;
            if (k < 0f || k > 0.42f) return 0f;
            float u = k / 0.42f;
            return Mathf.Sin(u * Mathf.PI) * (1f - u) * 0.05f;
        }

        const float BundleStep = 0.0094f, NoteStep = 0.0019f;
        static readonly Vector3[] StackAt = { new Vector3(-0.082f, 0f, 0.041f), new Vector3(0.082f, 0f, 0.041f), new Vector3(-0.082f, 0f, -0.041f), new Vector3(0.082f, 0f, -0.041f) };
        static readonly float[] LooseYaw = { 4f, 14f, -9f, 21f };

        /// <summary>The stack in the drawer shows <paramref name="prim"/>: bundles of five (back row first, so taller
        /// stacks never hide the front), the remainder as loose notes on top, and the paper tag with the number.
        /// <paramref name="animate"/>: new notes drop in, removed ones lift away (only while the drawer is open).</summary>
        public void SetPrim(int prim, bool animate)
        {
            Init();
            prim = Mathf.Max(0, prim);
            if (prim == _prim || DrawerCash == null) return;
            _prim = prim;
            int bundles = prim / 5, loose = prim % 5;
            int shownBundles = Mathf.Min(bundles, 28);
            int perStack = shownBundles <= 20 ? 5 : Mathf.CeilToInt(shownBundles / 4f);
            animate &= _drawerOpen > 0.5f;
            Resize(_bundles, shownBundles, BundleTemplate, "Bundle", animate);
            Resize(_loose, loose, BillTemplate, "Note", animate);
            float[] tops = new float[4];
            for (int i = 0; i < _bundles.Count; i++)
            {
                int s = Mathf.Min(3, i / perStack), k = i % perStack;
                if (i >= perStack * 4) { s = 3; k = i - perStack * 3; }
                var j = Jitter(i);
                Place(_bundles[i], StackAt[s] + new Vector3(j.x, k * BundleStep, j.y), j.z);
                tops[s] = (k + 1) * BundleStep;
            }
            int last = _bundles.Count > 0 ? Mathf.Min(3, (_bundles.Count - 1) / perStack) : 0;
            for (int i = 0; i < _loose.Count; i++)
                Place(_loose[i], StackAt[last] + new Vector3(0.004f * i - 0.006f, tops[last] + i * NoteStep, 0.003f * i), LooseYaw[i % LooseYaw.Length]);
            if (_loose.Count > 0) tops[last] += _loose.Count * NoteStep + 0.002f;
            if (PrimTag != null)
            {
                PrimTag.gameObject.SetActive(true);
                PrimTag.localPosition = StackAt[0] + new Vector3(0.004f, tops[0] + 0.0018f, -0.004f);
                PrimTag.localRotation = Quaternion.Euler(0f, 7f, 0f);
                if (PrimTagText != null) PrimTagText.text = Loc.F("v5.office.drawer.tag", prim);
            }
        }

        static Vector3 Jitter(int i)
        {
            uint h = (uint)(i * 2654435761u) ^ 0x9E3779B9u;
            float a = ((h & 0xFF) / 255f - 0.5f) * 0.004f, b = (((h >> 8) & 0xFF) / 255f - 0.5f) * 0.004f, c = (((h >> 16) & 0xFF) / 255f - 0.5f) * 6f;
            return new Vector3(a, b, c);
        }

        void Place(Transform t, Vector3 local, float yaw)
        {
            _home[t] = local;
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);
            if (!_born.ContainsKey(t)) t.localPosition = local;
        }

        void Resize(List<Transform> list, int n, GameObject template, string name, bool animate)
        {
            while (list.Count > n)
            {
                var t = list[list.Count - 1];
                list.RemoveAt(list.Count - 1);
                _home.Remove(t);
                _born.Remove(t);
                if (animate) { _dying.Add(t); _dyingAt.Add(Time.time); }
                else Destroy(t.gameObject);
            }
            while (list.Count < n && template != null)
            {
                var go = Instantiate(template, DrawerCash);
                go.name = name + list.Count;
                go.SetActive(true);
                list.Add(go.transform);
                if (animate) _born[go.transform] = Time.time + 0.045f * (list.Count % 6);
            }
        }

        void TickPrimStack(float now)
        {
            if (DrawerCash == null) return;
            if (_born.Count > 0)
            {
                var done = new List<Transform>();
                foreach (var kv in _born)
                {
                    var t = kv.Key;
                    if (t == null) { done.Add(t); continue; }
                    float u = Mathf.Clamp01((now - kv.Value) / 0.26f);
                    Vector3 home = _home.TryGetValue(t, out var h) ? h : t.localPosition;
                    t.localPosition = home + Vector3.up * (0.07f * (1f - u * u));
                    if (u >= 1f) done.Add(t);
                }
                foreach (var t in done) _born.Remove(t);
            }
            for (int i = _dying.Count - 1; i >= 0; i--)
            {
                var t = _dying[i];
                float u = (now - _dyingAt[i]) / 0.24f;
                if (t == null || u >= 1f)
                {
                    if (t != null) Destroy(t.gameObject);
                    _dying.RemoveAt(i); _dyingAt.RemoveAt(i);
                    continue;
                }
                t.localPosition += Vector3.up * (Time.deltaTime * 0.25f);
                t.localScale = Vector3.one * (1f - u);
            }
        }

        // ------------------------------------------------------------------ frame

        void TickTasks(Camera cam, PovMode mode, float now, float dt)
        {
            TickCashBox(cam, now, dt);
            TickRoster(cam, now, dt);
            TickPrimStack(now);
        }

        /// <summary>Seated look direction relative to the desk (degrees, right +).</summary>
        float SeatYaw(Camera cam)
        {
            if (cam == null || Eye == null) return 0f;
            Vector3 f = cam.transform.forward; f.y = 0f;
            Vector3 e = Eye.forward; e.y = 0f;
            if (f.sqrMagnitude < 1e-4f || e.sqrMagnitude < 1e-4f) return 0f;
            return Vector3.SignedAngle(e, f, Vector3.up);
        }

        /// <summary>The right mitten while it carries the Petty Cash notes (overrides its resting pose): it comes in
        /// from the lower right, so the arm never covers the box.</summary>
        bool GrabHand(Camera cam, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            float g = Time.time - _grabStart;
            if (cam == null || g < 0f || g >= GrabTime || !_cashShown) return false;
            GrabPath(g, cam, out _, out pos, out _);
            var ct = cam.transform;
            Vector3 arm = (-ct.up * 0.3f - ct.forward * 0.45f + ct.right * 0.84f).normalized;
            rot = Quaternion.LookRotation(arm, Vector3.ProjectOnPlane(Vector3.up, arm).normalized);
            return true;
        }
    }
}
