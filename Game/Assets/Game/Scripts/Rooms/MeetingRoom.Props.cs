using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 step 3a objects of the meeting room (docs/V5_INTERACTION.md §3): the folder in front of each seat (the local
    /// one lifts to the camera and its cover flips open, the folder UI is laid over the tablet screen), the ballot paper
    /// and pencil that appear in front of you during a vote (the paper comes up to be filled in, folds in two and flies
    /// into the slot of the ballot box), the clear ballot box in the middle of the table (drops in for the votes; every
    /// paper dropped by anyone lands in its pile and rattles it — nobody sees the content) and the two whiteboards (the
    /// wall one faces the left row, the one on wheels faces the right row; the UI draws the suç tahtası into both).
    /// Everything here is local presentation; the UI decides the states (MeetingUi, MeetingBallot, MeetingFolder).
    /// </summary>
    public sealed partial class MeetingRoom
    {
        [Header("Step 3a: ballot box, paper, pencil, whiteboards")]
        public Transform BallotBox;
        public Transform BallotSlot, BallotPile;
        public TextMeshPro BallotLabel, BallotLabelBack;
        public GameObject PencilTemplate;
        /// <summary>Paper material (lit, plain); the front of the ballot paper gets a copy with the ballot texture.</summary>
        public Material PaperMat;
        /// <summary>Whiteboard surfaces: [0] the wall board, [1] the board on wheels.</summary>
        public Renderer[] Boards = new Renderer[0];
        public float FolderOpenAngle = 176f;

        public const float PaperW = 0.148f, PaperH = 0.21f;

        // ---- folder
        bool _folderHeld;
        float _folderT;
        int _folderSeat = -1;
        Vector3 _folderRestPos;
        Quaternion _folderRestRot, _coverRest;
        Vector3 _screenLocal;
        Vector2 _screenSize = new Vector2(0.262f, 0.172f);

        // ---- ballot paper
        Transform _paper, _paperHinge, _pencil;
        Renderer _paperLowFront, _paperTopFront;
        BoxCollider _paperCollider;
        bool _ballotShown, _ballotHeld, _dropping;
        float _holdT, _appear = 1f, _dropStart = -10f, _respawnAt = -1f;
        Vector3 _dropFromPos;
        Quaternion _dropFromRot;
        const float FoldTime = 0.38f, FlyTime = 0.62f, SinkTime = 0.22f;

        // ---- ballot box
        bool _boxShown;
        float _boxAppear = 1f, _boxScale = 1f, _rattleUntil = -1f;
        Vector3 _boxRest;
        Quaternion _boxRestRot;
        readonly List<Transform> _pile = new List<Transform>();
        Mesh _packetMesh;
        Material _packetMat;
        string _labelLang;

        struct Flyer { public Transform T; public Vector3 From; public float Start; }
        readonly List<Flyer> _flyers = new List<Flyer>();

        public bool FolderHeld => _folderHeld;
        /// <summary>0 on the table .. 1 held up, open (the folder UI fades in near 1).</summary>
        public float FolderProgress => _folderT;
        public bool BallotShown => _ballotShown;
        public bool BallotHeld => _ballotHeld;
        public bool BallotDropping => _dropping;
        /// <summary>The ballot paper is held up and settled (the paper UI fades in).</summary>
        public bool BallotSettled => _ballotHeld && _holdT > 0.97f && !_dropping;
        public Renderer BallotSurface => _paperLowFront;
        public Renderer BallotSurfaceTop => _paperTopFront;
        public int PileCount => _pile.Count;

        void InitProps()
        {
            if (BallotBox != null) { _boxRest = BallotBox.localPosition; _boxRestRot = BallotBox.localRotation; BallotBox.gameObject.SetActive(false); }
            if (PencilTemplate != null) PencilTemplate.SetActive(false);
            BuildPaper();
        }

        /// <summary>Seats changed (<see cref="Enter"/>): only the local seat's folder can be used.</summary>
        void SeatsChanged()
        {
            int local = LocalSeat;
            for (int s = 0; s < Seats.Length; s++)
            {
                var rig = Seats[s];
                if (rig.FolderCollider != null) rig.FolderCollider.enabled = s == local;
            }
            if (_folderSeat >= 0 && _folderSeat < Seats.Length && _folderSeat != local) RestoreFolder(_folderSeat);
            _folderSeat = local;
            _folderHeld = false;
            _folderT = 0f;
            if (local >= 0 && local < Seats.Length && Seats[local].Folder != null)
            {
                var f = Seats[local].Folder;
                _folderRestPos = f.position; _folderRestRot = f.rotation;
                if (Seats[local].FolderCover != null) _coverRest = Seats[local].FolderCover.localRotation;
                var scr = Seats[local].FolderScreen;
                if (scr != null)
                {
                    var mf = scr.GetComponent<MeshFilter>();
                    var b = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, new Vector3(0.262f, 0f, 0.172f));
                    _screenLocal = scr.transform.TransformPoint(b.center) - f.position; // world offset at rest (any import scale)
                    var size = Vector3.Scale(b.size, scr.transform.lossyScale);
                    _screenSize = new Vector2(Mathf.Max(size.x, 0.05f), Mathf.Max(size.z, size.y, 0.05f));
                }
            }
            SetBallot(false);
            ClearPile();
        }

        void RestoreFolder(int seat)
        {
            var rig = Seats[seat];
            if (rig.Folder == null) return;
            rig.Folder.SetPositionAndRotation(_folderRestPos, _folderRestRot);
            if (rig.FolderCover != null) rig.FolderCover.localRotation = _coverRest;
        }

        // ------------------------------------------------------------------ folder API

        /// <summary>The local folder comes up to the camera and opens (or goes back down, closed).</summary>
        public void SetFolderHeld(bool held)
        {
            if (held && _ballotHeld) SetBallotHeld(false);
            _folderHeld = held;
        }

        /// <summary>Screen rect (pixels, origin bottom-left) of the local folder's tablet screen.</summary>
        public bool TryGetFolderScreen(Camera cam, out Rect rect)
        {
            rect = default;
            int s = LocalSeat;
            if (cam == null || s < 0 || s >= Seats.Length || Seats[s].FolderScreen == null) return false;
            var r = Seats[s].FolderScreen;
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return false;
            return ProjectRect(cam, r.transform, mf.sharedMesh.bounds, out rect);
        }

        static bool ProjectRect(Camera cam, Transform tr, Bounds b, out Rect rect)
        {
            rect = default;
            Vector3 mn = new Vector3(float.MaxValue, float.MaxValue), mx = new Vector3(float.MinValue, float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                var sp = cam.WorldToScreenPoint(tr.TransformPoint(c));
                if (sp.z <= 0) return false;
                mn = Vector3.Min(mn, sp); mx = Vector3.Max(mx, sp);
            }
            rect = Rect.MinMaxRect(mn.x, mn.y, mx.x, mx.y);
            return true;
        }

        /// <summary>Camera distance at which a held object of this size fills the given share of the view.</summary>
        static float HoldDistance(Camera cam, Vector2 size, float fillH, float fillW)
        {
            float tan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 2f;
            float dh = size.y / (fillH * tan);
            float dw = size.x / (fillW * tan * Mathf.Max(0.5f, cam.aspect));
            return Mathf.Max(dh, dw);
        }

        void TickFolder(Camera cam, float dt)
        {
            int s = _folderSeat;
            if (s < 0 || s >= Seats.Length || Seats[s].Folder == null) return;
            var rig = Seats[s];
            float want = _folderHeld ? 1f : 0f;
            if (Mathf.Approximately(_folderT, want) && !_folderHeld) return;
            _folderT = Mathf.MoveTowards(_folderT, want, dt * (_folderHeld ? 2.4f : 3.0f));
            float e = Ease(_folderT);
            var f = rig.Folder;
            if (_folderT <= 0.0001f || cam == null)
            {
                f.SetPositionAndRotation(_folderRestPos, _folderRestRot);
                if (rig.FolderCover != null) rig.FolderCover.localRotation = _coverRest;
                if (rig.FolderCollider != null) rig.FolderCollider.enabled = true;
                return;
            }
            if (rig.FolderCollider != null) rig.FolderCollider.enabled = false;
            var ct = cam.transform;
            float d = HoldDistance(cam, _screenSize, 0.72f, 0.82f);
            Quaternion heldRot = Quaternion.LookRotation(ct.up, -ct.forward);
            Vector3 screenAt = ct.position + ct.forward * d - ct.up * (d * 0.02f);
            Vector3 heldPos = screenAt - heldRot * (Quaternion.Inverse(_folderRestRot) * _screenLocal);
            // rise off the table first, then come to you
            Vector3 lift = Vector3.up * (0.12f * Mathf.Sin(e * Mathf.PI));
            f.SetPositionAndRotation(Vector3.Lerp(_folderRestPos, heldPos, e) + lift, Quaternion.Slerp(_folderRestRot, heldRot, e));
            if (rig.FolderCover != null)
            {
                float c = Ease(Mathf.Clamp01((_folderT - 0.1f) / 0.75f));
                rig.FolderCover.localRotation = _coverRest * Quaternion.Euler(FolderOpenAngle * c, 0f, 0f);
            }
        }

        // ------------------------------------------------------------------ ballot paper

        void BuildPaper()
        {
            if (_paper != null) return;
            _paper = new GameObject("BallotPaper").transform;
            _paper.SetParent(transform, false);
            var front = PaperMat != null ? new Material(PaperMat) { name = "M_BallotPaperFront" } : null;
            var back = PaperMat;
            float hw = PaperW * 0.5f, hh = PaperH * 0.5f;
            _paperLowFront = Quad(_paper, "LowFront", -hw, hw, -hh, 0f, 0f, 0.5f, 0.0002f, false, front);
            Quad(_paper, "LowBack", -hw, hw, -hh, 0f, 0f, 0.5f, 0f, true, back);
            _paperHinge = new GameObject("Fold").transform;
            _paperHinge.SetParent(_paper, false);
            _paperHinge.localPosition = new Vector3(0f, 0.0006f, 0f);
            _paperTopFront = Quad(_paperHinge, "TopFront", -hw, hw, 0f, hh, 0.5f, 1f, 0.0002f, false, front);
            Quad(_paperHinge, "TopBack", -hw, hw, 0f, hh, 0.5f, 1f, 0f, true, back);
            _paperCollider = _paper.gameObject.AddComponent<BoxCollider>();
            _paperCollider.size = new Vector3(PaperW + 0.02f, 0.012f, PaperH + 0.02f);
            var t = _paper.gameObject.AddComponent<RoomTarget>();
            t.Meet = MeetingTarget.Ballot;
            _paper.gameObject.SetActive(false);
            if (PencilTemplate != null)
            {
                _pencil = Instantiate(PencilTemplate, transform).transform;
                _pencil.name = "BallotPencil";
                _pencil.gameObject.SetActive(false);
            }
            // a folded paper for the pile in the box and the papers other colleagues drop
            _packetMesh = PacketMesh();
            _packetMat = PaperMat;
        }

        static Renderer Quad(Transform parent, string name, float x0, float x1, float z0, float z1, float v0, float v1, float y, bool back, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var m = new Mesh { name = "Ballot" + name };
            float w = Mathf.Max(1e-4f, x1 - x0);
            m.vertices = new[] { new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0) };
            m.uv = new[] { new Vector2(0f, v0), new Vector2(0f, v1), new Vector2(1f, v1), new Vector2(1f, v0) };
            m.triangles = back ? new[] { 0, 2, 1, 0, 3, 2 } : new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateNormals();
            m.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        /// <summary>A folded ballot (A6 packet, 2 mm thick) centred on its pivot.</summary>
        static Mesh PacketMesh()
        {
            var m = new Mesh { name = "BallotPacket" };
            float hx = PaperW * 0.5f, hz = PaperH * 0.25f, hy = 0.0012f;
            var v = new List<Vector3>();
            var tri = new List<int>();
            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int k = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                tri.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3 });
            }
            Face(new Vector3(-hx, hy, -hz), new Vector3(-hx, hy, hz), new Vector3(hx, hy, hz), new Vector3(hx, hy, -hz));
            Face(new Vector3(-hx, -hy, -hz), new Vector3(hx, -hy, -hz), new Vector3(hx, -hy, hz), new Vector3(-hx, -hy, hz));
            Face(new Vector3(-hx, -hy, -hz), new Vector3(-hx, hy, -hz), new Vector3(hx, hy, -hz), new Vector3(hx, -hy, -hz));
            Face(new Vector3(hx, -hy, hz), new Vector3(hx, hy, hz), new Vector3(-hx, hy, hz), new Vector3(-hx, -hy, hz));
            Face(new Vector3(-hx, -hy, hz), new Vector3(-hx, hy, hz), new Vector3(-hx, hy, -hz), new Vector3(-hx, -hy, -hz));
            Face(new Vector3(hx, -hy, -hz), new Vector3(hx, hy, -hz), new Vector3(hx, hy, hz), new Vector3(hx, -hy, hz));
            m.SetVertices(v);
            m.SetTriangles(tri, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        Transform Packet(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _packetMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _packetMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        Transform LocalSpot()
        {
            int s = LocalSeat;
            if (s < 0 || s >= Seats.Length) return null;
            return Seats[s].BallotSpot != null ? Seats[s].BallotSpot : Seats[s].Anchor;
        }

        /// <summary>The paper and pencil lie in front of you (a vote you can still cast in); false clears them.</summary>
        public void SetBallot(bool shown)
        {
            if (shown == _ballotShown) return;
            _ballotShown = shown;
            if (shown)
            {
                if (!_dropping) { _appear = 0f; _holdT = 0f; _ballotHeld = false; }
                Sfx.Play("bills", 0.35f);
            }
            else
            {
                _ballotHeld = false;
                if (!_dropping && _paper != null) _paper.gameObject.SetActive(false);
                _respawnAt = -1f;
            }
            if (_pencil != null) _pencil.gameObject.SetActive(shown);
        }

        /// <summary>The paper comes up to be filled in (cursor free, the paper UI is laid over it) or goes back down.</summary>
        public void SetBallotHeld(bool held)
        {
            if (held && (!_ballotShown || _dropping)) return;
            if (held && _folderHeld) _folderHeld = false;
            _ballotHeld = held;
        }

        /// <summary>Fold the paper in two and drop it into the box (a fresh one comes back while the vote runs).</summary>
        public void DropBallot()
        {
            if (_paper == null || _dropping) return;
            if (!_paper.gameObject.activeSelf) { _paper.gameObject.SetActive(true); TickPaperPose(Camera.main, 0f); }
            _dropping = true;
            _dropStart = Time.time;
            _dropFromPos = _paper.position;
            _dropFromRot = _paper.rotation;
            _ballotHeld = false;
            _respawnAt = Time.time + FoldTime + FlyTime + SinkTime + 0.9f;
            if (_paperCollider != null) _paperCollider.enabled = false;
            Sfx.Play("bills", 0.55f);
        }

        /// <summary>Screen rect (pixels, origin bottom-left) of the held ballot paper.</summary>
        public bool TryGetBallotScreen(Camera cam, out Rect rect)
        {
            rect = default;
            if (cam == null || _paper == null || !_paper.gameObject.activeInHierarchy) return false;
            var b = new Bounds(Vector3.zero, new Vector3(PaperW, 0f, PaperH));
            return ProjectRect(cam, _paper, b, out rect);
        }

        void PaperTablePose(out Vector3 pos, out Quaternion rot)
        {
            var spot = LocalSpot();
            if (spot == null) { pos = transform.position; rot = transform.rotation; return; }
            pos = spot.position + spot.up * 0.0012f;
            rot = spot.rotation;
        }

        void PaperHeldPose(Camera cam, out Vector3 pos, out Quaternion rot)
        {
            var ct = cam.transform;
            float d = HoldDistance(cam, new Vector2(PaperW, PaperH), 0.76f, 0.6f);
            rot = Quaternion.LookRotation(ct.up, -ct.forward);
            pos = ct.position + ct.forward * d - ct.up * (d * 0.025f);
        }

        void TickPaperPose(Camera cam, float dt)
        {
            if (_paper == null) return;
            _holdT = Mathf.MoveTowards(_holdT, _ballotHeld ? 1f : 0f, dt * (_ballotHeld ? 3.2f : 3.6f));
            _appear = Mathf.MoveTowards(_appear, 1f, dt / 0.45f);
            PaperTablePose(out var tp, out var tr);
            // slides in from your left when the vote starts
            var spot = LocalSpot();
            float a = 1f - Ease(_appear);
            if (spot != null && a > 0f) { tp += -spot.right * (0.32f * a) + spot.up * (0.05f * Mathf.Sin(a * Mathf.PI)); tr = tr * Quaternion.Euler(0f, -25f * a, 0f); }
            float e = Ease(_holdT);
            if (e <= 0.0001f || cam == null) { _paper.SetPositionAndRotation(tp, tr); return; }
            PaperHeldPose(cam, out var hp, out var hr);
            Vector3 lift = Vector3.up * (0.06f * Mathf.Sin(e * Mathf.PI));
            _paper.SetPositionAndRotation(Vector3.Lerp(tp, hp, e) + lift, Quaternion.Slerp(tr, hr, e));
        }

        void TickBallot(Camera cam, float now, float dt)
        {
            if (_paper == null) return;
            if (_dropping)
            {
                TickDrop(now);
                return;
            }
            if (_respawnAt > 0f && now >= _respawnAt && _ballotShown)
            {
                // a fresh paper (a new vote replaces the one in the box)
                _respawnAt = -1f;
                _appear = 0f;
                _holdT = 0f;
                _paperHinge.localRotation = Quaternion.identity;
                Sfx.Play("bills", 0.3f);
            }
            bool show = _ballotShown && _respawnAt < 0f;
            if (_paper.gameObject.activeSelf != show) _paper.gameObject.SetActive(show);
            if (!show) return;
            _paperHinge.localRotation = Quaternion.identity;
            if (_paperCollider != null) _paperCollider.enabled = !_ballotHeld && _holdT < 0.05f;
            TickPaperPose(cam, dt);
            if (_pencil != null)
            {
                var spot = LocalSpot();
                if (spot != null)
                {
                    // lying across the paper (it stays on the table when you pick the paper up)
                    Vector3 tip = spot.position - spot.right * 0.012f - spot.forward * 0.052f + spot.up * (0.0037f + 0.0013f * (1f - Ease(_holdT)));
                    _pencil.SetPositionAndRotation(tip, spot.rotation * Quaternion.Euler(0f, 34f, 0f) * Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, 30f, 0f));
                }
            }
        }

        void TickDrop(float now)
        {
            float t = now - _dropStart;
            // fold: the top half turns over onto the lower half (the marks inside)
            float fold = Ease(Mathf.Clamp01(t / FoldTime));
            _paperHinge.localRotation = Quaternion.Euler(-178f * fold, 0f, 0f);
            Vector3 slot = BallotSlot != null ? BallotSlot.position : (BallotBox != null ? BallotBox.position + Vector3.up * 0.24f : transform.position);
            Quaternion slotRot = Quaternion.LookRotation(Vector3.down, BallotBox != null ? BallotBox.forward : Vector3.forward);
            // the folded packet's centre sits a quarter paper toward the sitter from the fold
            Vector3 packetLocal = new Vector3(0f, 0.001f, -PaperH * 0.25f);
            if (t < FoldTime)
            {
                _paper.SetPositionAndRotation(_dropFromPos, _dropFromRot);
                return;
            }
            Vector3 fromCentre = _dropFromPos + _dropFromRot * packetLocal;
            Vector3 entry = slot + Vector3.up * (PaperH * 0.25f + 0.012f);
            float f = (t - FoldTime) / FlyTime;
            Vector3 centre;
            Quaternion rot;
            if (f < 1f)
            {
                float e = Ease(f);
                Vector3 mid = (fromCentre + entry) * 0.5f + Vector3.up * (0.16f + 0.1f * Vector3.Distance(fromCentre, entry));
                centre = Vector3.Lerp(Vector3.Lerp(fromCentre, mid, e), Vector3.Lerp(mid, entry, e), e);
                rot = Quaternion.Slerp(_dropFromRot, slotRot, Ease(Mathf.Clamp01(f * 1.3f)));
            }
            else
            {
                float k = Mathf.Clamp01((t - FoldTime - FlyTime) / SinkTime);
                centre = entry + Vector3.down * (PaperH * 0.5f + 0.03f) * k;
                rot = slotRot;
                if (k >= 1f)
                {
                    _dropping = false;
                    _paper.gameObject.SetActive(false);
                    _paperHinge.localRotation = Quaternion.identity;
                    if (_paperCollider != null) _paperCollider.enabled = true;
                    Landed();
                    return;
                }
            }
            _paper.SetPositionAndRotation(centre - rot * packetLocal, rot);
        }

        /// <summary>A colleague dropped a paper (VoteCast): a folded paper flies from their seat into the box.</summary>
        public void OtherDrop(int slot)
        {
            if (!_boxShown || BallotBox == null) return;
            var a = SeatAnchorOfSlot(slot);
            if (a == null) return;
            var t = Packet("Flyer" + slot);
            Vector3 from = a.position + a.forward * 0.5f + Vector3.up * 0.95f;
            t.SetPositionAndRotation(from, a.rotation);
            _flyers.Add(new Flyer { T = t, From = from, Start = Time.time });
        }

        void TickFlyers(float now)
        {
            Vector3 slot = BallotSlot != null ? BallotSlot.position : (BallotBox != null ? BallotBox.position + Vector3.up * 0.24f : transform.position);
            Quaternion slotRot = Quaternion.LookRotation(Vector3.down, BallotBox != null ? BallotBox.forward : Vector3.forward);
            for (int i = _flyers.Count - 1; i >= 0; i--)
            {
                var f = _flyers[i];
                if (f.T == null) { _flyers.RemoveAt(i); continue; }
                float t = (now - f.Start) / 0.7f;
                Vector3 entry = slot + Vector3.up * (PaperH * 0.25f + 0.01f);
                if (t < 1f)
                {
                    float e = Ease(t);
                    Vector3 mid = (f.From + entry) * 0.5f + Vector3.up * 0.28f;
                    f.T.SetPositionAndRotation(Vector3.Lerp(Vector3.Lerp(f.From, mid, e), Vector3.Lerp(mid, entry, e), e),
                        Quaternion.Slerp(f.T.rotation, slotRot, Mathf.Clamp01(t * 1.4f)));
                    continue;
                }
                float k = Mathf.Clamp01((now - f.Start - 0.7f) / 0.2f);
                f.T.position = entry + Vector3.down * (PaperH * 0.5f) * k;
                if (k < 1f) continue;
                Destroy(f.T.gameObject);
                _flyers.RemoveAt(i);
                Landed();
            }
        }

        /// <summary>A paper reached the inside of the box: one more in the pile, the box rattles.</summary>
        void Landed()
        {
            _rattleUntil = Time.time + 0.32f;
            Sfx.Play("mail_drop", 0.55f);
            Sfx.Play("box_drop", 0.25f);
            if (BallotPile == null || _pile.Count >= 18) return;
            var p = Packet("Pile" + _pile.Count);
            p.SetParent(BallotBox, true);
            int n = _pile.Count;
            float yaw = (n * 47f) % 70f - 35f;
            Vector3 off = new Vector3(((n * 37) % 9 - 4) * 0.006f, 0.0013f + n * 0.0026f, ((n * 53) % 7 - 3) * 0.006f);
            p.SetPositionAndRotation(BallotPile.TransformPoint(off), BallotPile.rotation * Quaternion.Euler(0f, yaw, (n % 2 == 0 ? 3f : -4f)));
            _pile.Add(p);
        }

        void ClearPile()
        {
            foreach (var p in _pile) if (p != null) Destroy(p.gameObject);
            _pile.Clear();
            foreach (var f in _flyers) if (f.T != null) Destroy(f.T.gameObject);
            _flyers.Clear();
        }

        // ------------------------------------------------------------------ ballot box

        /// <summary>The ballot box on the table (votes and their verdicts); it arrives empty for every vote.</summary>
        public void SetBallotBox(bool shown, bool fresh = false)
        {
            if (BallotBox == null) return;
            if (fresh) ClearPile();
            if (shown == _boxShown) return;
            _boxShown = shown;
            if (shown)
            {
                BallotBox.gameObject.SetActive(true);
                _boxAppear = Time.timeSinceLevelLoad > 2f ? 0f : 1f;
                _boxScale = 1f;
                if (_boxAppear < 1f) Sfx.Play("box_drop", 0.5f);
            }
        }

        void TickBox(float now, float dt)
        {
            if (BallotBox == null) return;
            if (BallotLabel != null && _labelLang != Loc.Language)
            {
                _labelLang = Loc.Language;
                BallotLabel.text = Loc.T("v5.meeting.box_label");
                if (BallotLabelBack != null) BallotLabelBack.text = BallotLabel.text;
            }
            if (!_boxShown)
            {
                if (!BallotBox.gameObject.activeSelf) return;
                _boxScale = Mathf.MoveTowards(_boxScale, 0f, dt / 0.25f);
                BallotBox.localScale = Vector3.one * Mathf.Max(0.001f, Ease(_boxScale));
                if (_boxScale <= 0f) { BallotBox.gameObject.SetActive(false); BallotBox.localScale = Vector3.one; ClearPile(); }
                return;
            }
            _boxAppear = Mathf.MoveTowards(_boxAppear, 1f, dt / 0.45f);
            float a = 1f - _boxAppear;
            float bounce = _boxAppear > 0.78f ? Mathf.Sin((_boxAppear - 0.78f) / 0.22f * Mathf.PI) * 0.008f : 0f;
            BallotBox.localPosition = _boxRest + Vector3.up * (0.3f * a * a * a + bounce);
            BallotBox.localScale = Vector3.one;
            if (now < _rattleUntil)
            {
                float k = (_rattleUntil - now) / 0.32f;
                BallotBox.localRotation = _boxRestRot * Quaternion.Euler(Mathf.Sin(now * 71f) * 1.6f * k, Mathf.Sin(now * 53f) * 2.2f * k, Mathf.Sin(now * 89f) * 1.4f * k);
            }
            else BallotBox.localRotation = _boxRestRot;
        }

        // ------------------------------------------------------------------ whiteboards

        /// <summary>The whiteboard the seat faces (the wall board for the left row, the board on wheels for the right row).</summary>
        public Renderer BoardFor(int slot)
        {
            var a = SeatAnchorOfSlot(slot);
            Renderer best = null;
            float bestDot = -2f;
            foreach (var b in Boards)
            {
                if (b == null) continue;
                if (a == null) return b;
                Vector3 d = b.bounds.center - a.position;
                d.y = 0f;
                float dot = Vector3.Dot(a.forward, d.normalized);
                if (dot > bestDot) { bestDot = dot; best = b; }
            }
            return best;
        }

        // ------------------------------------------------------------------ frame

        /// <summary>Folder, ballot paper, flyers and the box (after the camera moved this frame).</summary>
        public void TickProps(Camera cam, float dt)
        {
            float now = Time.time;
            TickFolder(cam, dt);
            TickBallot(cam, now, dt);
            TickFlyers(now);
            TickBox(now, dt);
            TickPayouts(now, dt);
        }

        static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
    }
}
