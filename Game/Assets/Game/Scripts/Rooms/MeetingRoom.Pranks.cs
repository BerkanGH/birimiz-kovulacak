using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using TMPro;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 Yaramazlıklar, world side (docs/V5_YARAMAZLIKLAR.md): what a prank looks like at the meeting table. The coffee mug
    /// arcs from the thrower's seat into the target's shirt (splash, a stain that stays for the match); the paper plane glides
    /// onto the target's table and stays there for the session (the target's own plane can be picked up to read the note);
    /// the post-it slaps onto the forehead (the word readable, until the session ends); the phone rings at the target's seat
    /// (an embarrassing tune of our own) and its owner panics and answers; the slurp plays the drinking clip; standing up
    /// rolls the chair back, slams the table and points; flattering makes the manager pleased and point at the flatterer;
    /// the cake flies into a face and leaves cream on it. Persistent marks follow the view (<see cref="SyncPranks"/>), the
    /// animations follow the events (<see cref="PrankFx"/>). Meshes are small runtime ones (no new assets). Presentation only.
    /// </summary>
    public sealed partial class MeetingRoom
    {
        sealed class Flight
        {
            public Transform T;
            public Vector3 From, To, Side;
            public float Start, Duration, Arc, Spin;
            public bool Nose, Rest, Landed;
            public Quaternion RestRot;
            public Action OnLand;
            public float FadeAt = -1f;
        }

        sealed class Drop { public Transform T; public Vector3 V; public float Until; }

        /// <summary>What sticks to one colleague. The marks live in the room (not on the bones: the skinned body does not
        /// follow its bones rigidly) and are laid on the body's surface again every frame, turned with the head.</summary>
        sealed class Marks
        {
            public SeatedBean Bean;
            public readonly List<Transform> Stains = new List<Transform>();
            public int Word = -1;
            public Transform PostIt;
            public TextMeshPro PostItText;
            public Transform Cream;
            public Transform Phone;
            public bool Any => Stains.Count > 0 || (PostIt != null && PostIt.gameObject.activeSelf) || Cream != null;
        }

        static readonly float[] StainHeights = { 0.37f, 0.29f, 0.41f, 0.32f }, StainSides = { 0.04f, -0.05f, -0.02f, 0.07f };
        const float PostItHeight = 0.77f, FaceHeight = 0.64f;

        sealed class Timed { public float At; public Action Do; }

        readonly List<Flight> _flights = new List<Flight>();
        readonly List<Drop> _drops = new List<Drop>();
        readonly List<Timed> _timed = new List<Timed>();
        readonly Marks[] _marks = new Marks[MaxSlots];
        readonly float[] _markAfter = new float[MaxSlots];
        readonly float[] _prankStandUntil = new float[6];
        readonly Dictionary<int, Transform> _planes = new Dictionary<int, Transform>();
        int _planeSession = int.MinValue;
        Mesh _planeMesh, _quadMesh, _sphereMesh, _cylinderMesh, _cubeMesh;
        Material _coffeeMat, _yellowMat, _creamMat, _cakeMat, _cherryMat, _phoneMat, _mugMat;
        TMP_FontAsset _font;
        bool _prankInit;
        Vector3 _eye;

        /// <summary>The local player's resting planes (the newest first), for the hover target and the note reader.</summary>
        public int MyPlaneIndex { get; private set; } = -1;

        void InitPranks()
        {
            if (_prankInit) return;
            _prankInit = true;
            _planeMesh = PlaneMesh();
            _quadMesh = QuadMesh();
            _sphereMesh = PrimitiveMesh(PrimitiveType.Sphere);
            _cylinderMesh = PrimitiveMesh(PrimitiveType.Cylinder);
            _cubeMesh = PrimitiveMesh(PrimitiveType.Cube);
            _coffeeMat = Tint(new Color(0.32f, 0.18f, 0.08f), 0.55f);
            _yellowMat = Tint(new Color(1f, 0.9f, 0.32f), 0.1f);
            _creamMat = Tint(new Color(0.99f, 0.97f, 0.92f), 0.35f);
            _cakeMat = Tint(new Color(0.85f, 0.6f, 0.35f), 0.15f);
            _cherryMat = Tint(new Color(0.85f, 0.08f, 0.12f), 0.6f);
            _phoneMat = Tint(new Color(0.12f, 0.13f, 0.16f), 0.5f);
            _mugMat = Tint(new Color(0.95f, 0.95f, 0.93f), 0.3f);
            _font = BallotLabel != null ? BallotLabel.font : null;
        }

        Material Tint(Color c, float smooth)
        {
            var m = PaperMat != null ? new Material(PaperMat) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            m.name = "M_Prank";
            return m;
        }

        static Mesh PrimitiveMesh(PrimitiveType t)
        {
            var go = GameObject.CreatePrimitive(t);
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Destroy(go);
            return mesh;
        }

        /// <summary>A folded paper plane (nose +z, 0.2 m long): two wings and a keel.</summary>
        static Mesh PlaneMesh()
        {
            var m = new Mesh { name = "PrankPlane" };
            var v = new List<Vector3>
            {
                new Vector3(0f, 0f, 0.1f), new Vector3(-0.075f, 0.012f, -0.1f), new Vector3(0f, -0.004f, -0.1f), // left wing
                new Vector3(0f, 0f, 0.1f), new Vector3(0f, -0.004f, -0.1f), new Vector3(0.075f, 0.012f, -0.1f),  // right wing
                new Vector3(0f, 0f, 0.1f), new Vector3(0f, -0.004f, -0.1f), new Vector3(0f, -0.03f, -0.08f),     // keel
            };
            // both sides of the paper: the back faces get their own vertices (shared ones would cancel the normals out)
            var tri = new List<int>();
            for (int i = 0; i < 9; i += 3)
            {
                tri.AddRange(new[] { i, i + 1, i + 2 });
                int k = v.Count;
                v.Add(v[i]); v.Add(v[i + 2]); v.Add(v[i + 1]);
                tri.AddRange(new[] { k, k + 1, k + 2 });
            }
            m.SetVertices(v);
            m.SetTriangles(tri, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static Mesh QuadMesh()
        {
            var m = new Mesh { name = "PrankQuad" };
            // two sided: the back face has its own vertices
            m.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            };
            m.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6 };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        Transform Make(string name, Mesh mesh, Material mat, Vector3 scale, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.localScale = scale;
            return go.transform;
        }

        // ------------------------------------------------------------------ where things are

        SeatedBean BeanAt(int slot) { return BeanOf(slot); }

        /// <summary>Forward of a seat (toward the table).</summary>
        Vector3 SeatForward(int slot)
        {
            var a = SeatAnchorOfSlot(slot);
            return a != null ? Vector3.ProjectOnPlane(a.forward, Vector3.up).normalized : Vector3.forward;
        }

        /// <summary>The face (the eyes' surface) of a colleague, or just in front of my camera.</summary>
        bool FacePoint(int slot, out Vector3 p)
        {
            p = Vector3.zero;
            if (slot == _local) { p = _eye + CamForward() * 0.32f + Vector3.down * 0.04f; return true; }
            var b = BeanAt(slot);
            if (b == null) return false;
            // the middle of the face (between the eyes and the mouth), on the bean's surface
            if (Surface(b, slot, FaceHeight, 0f, out p)) return true;
            if (!TryGetHead(slot, _eye, out p)) return false;
            p += SeatForward(slot) * 0.3f;
            return true;
        }

        /// <summary>The front of a colleague's shirt (or in front of my camera, a little low).</summary>
        bool ChestPoint(int slot, out Vector3 p)
        {
            p = Vector3.zero;
            if (slot == _local) { p = _eye + CamForward() * 0.36f + Vector3.down * 0.22f; return true; }
            var b = BeanAt(slot);
            if (b == null) return false;
            if (Surface(b, slot, 0.4f, 0f, out p)) return true;
            Vector3 head;
            if (!TryGetHead(slot, _eye, out head)) return false;
            p = head + Vector3.down * 0.42f + SeatForward(slot) * 0.16f;
            return true;
        }

        static Mesh _bake;
        static readonly List<Vector3> _bakeVerts = new List<Vector3>();
        static float _bakeMin, _bakeMax;
        static Vector3 _bakeCentre;

        /// <summary>The body mesh as it is posed right now, in world space (<see cref="_bakeVerts"/>). The renderers' bounds
        /// of the skinned beans are not usable, so the mesh is baked.</summary>
        static bool Bake(SeatedBean b)
        {
            var r = BodyRenderer(b);
            if (r == null) return false;
            var t = r.transform;
            _bakeVerts.Clear();
            var smr = r as SkinnedMeshRenderer;
            if (smr != null)
            {
                if (_bake == null) _bake = new Mesh { name = "PrankBake" };
                smr.BakeMesh(_bake, true);
                _bake.GetVertices(_bakeVerts);
                for (int i = 0; i < _bakeVerts.Count; i++) _bakeVerts[i] = t.position + t.rotation * _bakeVerts[i];
            }
            else
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) return false;
                mf.sharedMesh.GetVertices(_bakeVerts);
                for (int i = 0; i < _bakeVerts.Count; i++) _bakeVerts[i] = t.TransformPoint(_bakeVerts[i]);
            }
            if (_bakeVerts.Count == 0) return false;
            _bakeMin = float.MaxValue; _bakeMax = float.MinValue;
            Vector3 c = Vector3.zero;
            foreach (var v in _bakeVerts) { _bakeMin = Mathf.Min(_bakeMin, v.y); _bakeMax = Mathf.Max(_bakeMax, v.y); c += v; }
            _bakeCentre = c / _bakeVerts.Count;
            return true;
        }

        /// <summary>The front of the baked body facing <paramref name="f"/>: <paramref name="height"/> is a fraction of the body's
        /// height (0 bottom .. 1 top), <paramref name="side"/> metres to its right.</summary>
        static bool Sample(float height, float side, Vector3 f, out Vector3 p)
        {
            p = Vector3.zero;
            if (_bakeVerts.Count == 0) return false;
            Vector3 c = _bakeCentre, lat = Vector3.Cross(Vector3.up, f);
            float y = Mathf.Lerp(_bakeMin, _bakeMax, height), band = Mathf.Max(0.015f, (_bakeMax - _bakeMin) * 0.05f);
            for (int pass = 0; pass < 2; pass++)
            {
                float best = float.MinValue;
                bool found = false;
                foreach (var v in _bakeVerts)
                {
                    if (Mathf.Abs(v.y - y) > band * (pass + 1)) continue;
                    if (pass == 0 && Mathf.Abs(Vector3.Dot(v - c, lat) - side) > 0.045f) continue;
                    float d = Vector3.Dot(v - c, f);
                    if (d > best) { best = d; p = v; found = true; }
                }
                if (found)
                {
                    // the asked height and side exactly; the depth is the body's
                    Vector3 axis = new Vector3(c.x, y, c.z);
                    p = axis + lat * side + f * Mathf.Max(0f, Vector3.Dot(p - axis, f));
                    return true;
                }
            }
            return false;
        }

        bool Surface(SeatedBean b, int slot, float height, float side, out Vector3 p)
        {
            p = Vector3.zero;
            return Bake(b) && Sample(height, side, FaceDir(b, slot, 1f), out p);
        }

        /// <summary>Where a colleague's face (<paramref name="share"/> 1) or body (0.3) points: the seat's forward turned by the
        /// head turn the bean shows (the head takes 70 % of it, the body 30 %).</summary>
        Vector3 FaceDir(SeatedBean b, int slot, float share)
        {
            return Quaternion.AngleAxis(b.HeadYaw * share, Vector3.up) * SeatForward(slot);
        }

        /// <summary>Where something thrown leaves from: in front of the thrower's chest (my own: low in front of the camera).</summary>
        bool HandPoint(int slot, out Vector3 p)
        {
            p = Vector3.zero;
            if (slot == _local) { p = _eye + CamForward() * 0.3f + Vector3.down * 0.28f + CamRight() * 0.14f; return true; }
            if (!ChestPoint(slot, out p)) return false;
            p += SeatForward(slot) * 0.12f + Vector3.up * 0.05f;
            return true;
        }

        /// <summary>On the table in front of a seat (beside the folder, on the paper's side).</summary>
        bool TableSpot(int slot, int n, out Vector3 p, out Quaternion rot)
        {
            p = Vector3.zero; rot = Quaternion.identity;
            var a = SeatAnchorOfSlot(slot);
            if (a == null) return false;
            Vector3 toMiddle = transform.TransformPoint(new Vector3(0f, 0f, -0.15f)) - a.position;
            float middle = Vector3.Dot(a.right, toMiddle) >= 0f ? 1f : -1f;
            p = a.position + a.forward * (0.55f + 0.05f * (n % 3)) + a.right * middle * (0.36f + 0.07f * n) + Vector3.up * 0.762f;
            rot = Quaternion.LookRotation(Quaternion.Euler(0f, 160f + 12f * n, 0f) * a.forward, Vector3.up);
            return true;
        }

        static Renderer BodyRenderer(SeatedBean b)
        {
            foreach (var r in b.OutlineRenderers) if (r != null && r.name.IndexOf("Body", StringComparison.OrdinalIgnoreCase) >= 0) return r;
            foreach (var r in b.GetComponentsInChildren<SkinnedMeshRenderer>()) if (r.name.IndexOf("Body", StringComparison.OrdinalIgnoreCase) >= 0) return r;
            return null;
        }

        static Camera Pov { get { return RoomsPresenter.Instance != null ? RoomsPresenter.Instance.PovCamera : null; } }
        Vector3 CamForward() { var c = Pov; return c != null ? c.transform.forward : SeatForward(_local); }
        Vector3 CamRight() { var c = Pov; return c != null ? c.transform.right : Vector3.Cross(Vector3.up, SeatForward(_local)); }

        Vector3? SoundAt(int slot)
        {
            if (slot == _local) return null;
            Vector3 h;
            return TryGetHead(slot, _eye, out h) ? h : (Vector3?)null;
        }

        void After(float seconds, Action a) { _timed.Add(new Timed { At = Time.time + seconds, Do = a }); }

        // ------------------------------------------------------------------ the events

        /// <summary>A prank just happened (the event; <paramref name="actor"/> -1 = a hidden phone ring). Animations only: the
        /// lasting marks come from the view (<see cref="SyncPranks"/>), held back until the thing has landed.</summary>
        public void PrankFx(PrankKind kind, int actor, int target, int index)
        {
            InitPranks();
            Vector3 from, to;
            switch (kind)
            {
                case PrankKind.Coffee:
                {
                    if (!HandPoint(actor, out from) || !ChestPoint(target, out to)) break;
                    var mug = MakeMug();
                    _markAfter[target] = Time.time + 0.7f;
                    Sfx.PlayAt("prank_whoosh", SoundAt(actor), 0.6f);
                    Throw(mug, from, to, 0.62f, 0.32f, 720f, () =>
                    {
                        Splash(to, SeatForward(target) * -1f, _coffeeMat, 14);
                        Sfx.PlayAt("splash", SoundAt(target), 0.9f);
                        Sfx.PlayAt("prank_splat", SoundAt(target), 0.5f);
                        BeanAt(target)?.React(MeetingReaction.Shocked);
                    }, fallToTable: target);
                    BeanAt(actor)?.React(MeetingReaction.Point);
                    break;
                }
                case PrankKind.Plane:
                {
                    Vector3 rest; Quaternion restRot;
                    if (!HandPoint(actor, out from) || !TableSpot(target, CountPlanes(target), out rest, out restRot)) break;
                    var plane = Make("PrankPlane" + index, _planeMesh, PaperMat != null ? PaperMat : _creamMat, Vector3.one);
                    plane.position = from;
                    Sfx.PlayAt("prank_paper", SoundAt(actor), 0.8f);
                    var f = new Flight { T = plane, From = from, To = rest + Vector3.up * 0.012f, Start = Time.time, Duration = 1.25f, Arc = 0.22f, Nose = true,
                                         Side = Vector3.Cross(Vector3.up, (rest - from).normalized) * 0.12f, Rest = true, RestRot = restRot };
                    f.OnLand = () => { Sfx.PlayAt("place", SoundAt(target), 0.4f); };
                    _flights.Add(f);
                    _planes[index] = plane;
                    _planeOwner[index] = target;
                    if (target == _local) MakePlaneTarget(plane, index);
                    break;
                }
                case PrankKind.PostIt:
                {
                    if (!HandPoint(actor, out from) || !FacePoint(target, out to)) break;
                    var note = Make("PrankPostItFly", _quadMesh, _yellowMat, Vector3.one * 0.07f);
                    _markAfter[target] = Time.time + 0.45f;
                    Throw(note, from, to + Vector3.up * 0.07f, 0.42f, 0.12f, 360f, () =>
                    {
                        Sfx.PlayAt("prank_slap", SoundAt(target), 0.8f);
                        BeanAt(target)?.React(MeetingReaction.Gasp);
                    });
                    break;
                }
                case PrankKind.Cake:
                {
                    if (!HandPoint(actor, out from) || !FacePoint(target, out to)) break;
                    var cake = MakeCake();
                    _markAfter[target] = Time.time + 0.66f;
                    Sfx.PlayAt("prank_whoosh", SoundAt(actor), 0.6f);
                    Throw(cake, from, to, 0.6f, 0.28f, 180f, () =>
                    {
                        Splash(to, SeatForward(target) * -1f, _creamMat, 12);
                        Sfx.PlayAt("prank_splat", SoundAt(target), 0.9f);
                        BeanAt(target)?.React(MeetingReaction.Shocked);
                    }, fallToTable: target);
                    break;
                }
                case PrankKind.PhoneRing:
                {
                    Sfx.PlayAt("prank_ring", SoundAt(target), target == _local ? 0.75f : 1f);
                    var b = BeanAt(target);
                    if (b != null)
                    {
                        b.React(MeetingReaction.Panic, 1.3f);
                        After(1.3f, () =>
                        {
                            var bb = BeanAt(target);
                            if (bb == null) return;
                            bb.React(MeetingReaction.Phone, 2.2f);
                            ShowPhone(target, 2.2f);
                        });
                    }
                    break;
                }
                case PrankKind.Slurp:
                    Sfx.PlayAt("prank_slurp", SoundAt(actor), 1f);
                    BeanAt(actor)?.React(MeetingReaction.Drink, 1.6f);
                    break;
                case PrankKind.StandUp:
                {
                    int seat = actor >= 0 && actor < MaxSlots ? _seatOfSlot[actor] : -1;
                    if (seat >= 0 && seat < _prankStandUntil.Length) _prankStandUntil[seat] = Time.time + 2.6f;
                    After(0.75f, () =>
                    {
                        Sfx.PlayAt("prank_slam", SoundAt(actor), 1f);
                        _rattleUntil = Time.time + 0.32f;
                        var b = BeanAt(actor);
                        if (b == null) return;
                        Vector3 h;
                        if (target >= 0 && TryGetHead(target, _eye, out h)) b.LookAt(h);
                        b.React(MeetingReaction.Point);
                    });
                    break;
                }
                case PrankKind.Flatter:
                    ManagerReact(MeetingReaction.Pleased, -1, 1.4f);
                    After(1.2f, () => ManagerReact(MeetingReaction.Point, actor, 1.5f));
                    break;
            }
        }

        /// <summary>The manager reacts (anger, pleasure, pointing at someone); -1 = nobody in particular.</summary>
        public void ManagerReact(MeetingReaction r, int lookAt, float seconds = 0f)
        {
            if (_manager == null) return;
            Vector3 h;
            if (lookAt >= 0 && TryGetHead(lookAt, _eye, out h)) { _managerLookAt = h; _managerLookUntil = Time.time + Mathf.Max(1.5f, seconds); }
            _manager.React(r, seconds);
        }

        Vector3 _managerLookAt;
        float _managerLookUntil;

        /// <summary>The manager's mood as a resting face (calm, frowning, red).</summary>
        public void SetManagerMood(int level)
        {
            if (_manager == null) return;
            _manager.RestFace = level >= 2 ? BeanFace.Angry : level == 1 ? BeanFace.Stern : BeanFace.Neutral;
        }

        void Throw(Transform t, Vector3 from, Vector3 to, float duration, float arc, float spin, Action land, int fallToTable = -1)
        {
            t.position = from;
            var f = new Flight { T = t, From = from, To = to, Start = Time.time, Duration = duration, Arc = arc, Spin = spin, OnLand = land };
            if (fallToTable >= 0 && fallToTable != _local)
            {
                Vector3 spot; Quaternion rot;
                if (TableSpot(fallToTable, 2, out spot, out rot)) { f.Rest = true; f.RestRot = rot; f.FadeAt = 2.2f; f.Side = spot - to; }
            }
            else f.FadeAt = 0f;
            _flights.Add(f);
        }

        Transform MakeMug()
        {
            var office = RoomsPresenter.Instance != null ? RoomsPresenter.Instance.Office : null;
            if (office != null && office.Mug != null)
            {
                var copy = Instantiate(office.Mug.gameObject, transform);
                copy.name = "PrankMug";
                foreach (var c in copy.GetComponentsInChildren<Collider>()) Destroy(c);
                foreach (var t in copy.GetComponentsInChildren<RoomTarget>()) Destroy(t);
                copy.SetActive(true);
                copy.transform.localScale = office.Mug.lossyScale;
                return copy.transform;
            }
            var mug = Make("PrankMug", _cylinderMesh, _mugMat, new Vector3(0.08f, 0.05f, 0.08f));
            Make("Coffee", _cylinderMesh, _coffeeMat, new Vector3(0.9f, 0.05f, 0.9f), mug).localPosition = new Vector3(0f, 0.96f, 0f);
            return mug;
        }

        Transform MakeCake()
        {
            var cake = Make("PrankCake", _cylinderMesh, _cakeMat, new Vector3(0.16f, 0.035f, 0.16f));
            Make("Cream", _cylinderMesh, _creamMat, new Vector3(1.04f, 0.6f, 1.04f), cake).localPosition = new Vector3(0f, 1.1f, 0f);
            var cherry = Make("Cherry", _sphereMesh, _cherryMat, new Vector3(0.18f, 0.9f, 0.18f), cake);
            cherry.localPosition = new Vector3(0f, 2.1f, 0f);
            return cake;
        }

        /// <summary>A burst of drops flying back from the hit (coffee or cream).</summary>
        void Splash(Vector3 at, Vector3 back, Material mat, int count)
        {
            var rnd = new System.Random(_drops.Count * 31 + count);
            for (int i = 0; i < count; i++)
            {
                float s = 0.012f + 0.018f * (float)rnd.NextDouble();
                var d = Make("Drop", _sphereMesh, mat, Vector3.one * s);
                d.position = at;
                Vector3 v = back * (0.5f + (float)rnd.NextDouble()) + new Vector3((float)rnd.NextDouble() - 0.5f, 0.6f + (float)rnd.NextDouble(), (float)rnd.NextDouble() - 0.5f) * 1.1f;
                _drops.Add(new Drop { T = d, V = v, Until = Time.time + 0.55f + 0.25f * (float)rnd.NextDouble() });
            }
        }

        /// <summary>The phone in its owner's right hand while they answer it.</summary>
        void ShowPhone(int slot, float seconds)
        {
            var b = BeanAt(slot);
            if (b == null) return;
            var hand = FindDeep(b.transform, "Hand.R");
            if (hand == null) return;
            var m = MarksOf(slot);
            if (m.Phone == null)
            {
                m.Phone = Make("PrankPhone", _cubeMesh, _phoneMat, new Vector3(0.035f, 0.075f, 0.012f), hand);
                m.Phone.localPosition = Vector3.zero;
            }
            m.Phone.gameObject.SetActive(true);
            After(seconds, () => { if (m.Phone != null) m.Phone.gameObject.SetActive(false); });
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        int CountPlanes(int slot)
        {
            int n = 0;
            foreach (var kv in _planes) if (kv.Value != null && _planeOwner.TryGetValue(kv.Key, out var o) && o == slot) n++;
            return n;
        }

        readonly Dictionary<int, int> _planeOwner = new Dictionary<int, int>();

        void MakePlaneTarget(Transform plane, int index)
        {
            var box = plane.gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(0.22f, 0.06f, 0.26f);
            var t = plane.gameObject.AddComponent<RoomTarget>();
            t.Meet = MeetingTarget.Plane;
            MyPlaneIndex = index;
        }

        Marks MarksOf(int slot)
        {
            var b = BeanAt(slot);
            var m = _marks[slot];
            if (m == null || m.Bean != b)
            {
                // the bean was rebuilt (a new meeting room look): its marks go and come back on the new one
                if (m != null) DropMarks(m);
                m = _marks[slot] = new Marks { Bean = b };
            }
            return m;
        }

        void DropMarks(Marks m)
        {
            foreach (var t in m.Stains) if (t != null) Destroy(t.gameObject);
            m.Stains.Clear();
            if (m.PostIt != null) Destroy(m.PostIt.gameObject);
            if (m.Cream != null) Destroy(m.Cream.gameObject);
            if (m.Phone != null) Destroy(m.Phone.gameObject);
            m.PostIt = null; m.Cream = null; m.Phone = null; m.Word = -1;
        }

        // ------------------------------------------------------------------ the lasting marks (from the view)

        /// <summary>Stains (whole match), post-its (this session), cream (the results) and the planes resting on the tables
        /// (this session) as the view says; a mark whose prank is still in the air waits for it to land.</summary>
        public void SyncPranks(LinePranksView pv)
        {
            InitPranks();
            if (pv == null) { ClearPranks(); return; }
            float now = Time.time;
            for (int slot = 0; slot < MaxSlots && slot < pv.Stains.Count; slot++)
            {
                if (slot == _local || now < _markAfter[slot]) continue;
                var b = BeanAt(slot);
                if (b == null) continue;
                var m = MarksOf(slot);
                while (m.Stains.Count < Mathf.Min(4, pv.Stains[slot])) m.Stains.Add(MakeStain(b, slot, m.Stains.Count));
                while (m.Stains.Count > pv.Stains[slot])
                {
                    var last = m.Stains[m.Stains.Count - 1];
                    if (last != null) Destroy(last.gameObject);
                    m.Stains.RemoveAt(m.Stains.Count - 1);
                }
                int word = slot < pv.PostIts.Count ? pv.PostIts[slot] : -1;
                if (word != m.Word) SetPostIt(m, b, slot, word);
                int cakes = slot < pv.Cakes.Count ? pv.Cakes[slot] : 0;
                if (cakes > 0 && m.Cream == null) m.Cream = MakeCream(b, slot);
                if (cakes == 0 && m.Cream != null) { Destroy(m.Cream.gameObject); m.Cream = null; }
            }
            // the planes of this session stay on the tables; a new session clears them
            if (pv.Session != _planeSession)
            {
                _planeSession = pv.Session;
                foreach (var kv in _planes) if (kv.Value != null) Destroy(kv.Value.gameObject);
                _planes.Clear();
                _planeOwner.Clear();
                MyPlaneIndex = -1;
            }
            foreach (var e in pv.Log)
            {
                if (e.Kind != PrankKind.Plane || e.Session != pv.Session) continue;
                _planeOwner[e.Index] = e.Target;
                if (_planes.ContainsKey(e.Index)) continue;
                // a plane thrown before I arrived lies where it landed
                Vector3 spot; Quaternion rot;
                if (!TableSpot(e.Target, CountPlanes(e.Target), out spot, out rot)) continue;
                var plane = Make("PrankPlane" + e.Index, _planeMesh, PaperMat != null ? PaperMat : _creamMat, Vector3.one);
                plane.SetPositionAndRotation(spot + Vector3.up * 0.012f, rot);
                _planes[e.Index] = plane;
                if (e.Target == _local) MakePlaneTarget(plane, e.Index);
            }
        }

        /// <summary>Every mark, plane and thing in the air goes (a new match / meeting room).</summary>
        public void ClearPranks()
        {
            foreach (var f in _flights) if (f.T != null) Destroy(f.T.gameObject);
            _flights.Clear();
            foreach (var d in _drops) if (d.T != null) Destroy(d.T.gameObject);
            _drops.Clear();
            _timed.Clear();
            for (int i = 0; i < _prankStandUntil.Length; i++) _prankStandUntil[i] = 0f;
            for (int i = 0; i < _markAfter.Length; i++) _markAfter[i] = 0f;
            for (int i = 0; i < _marks.Length; i++)
            {
                if (_marks[i] != null) DropMarks(_marks[i]);
                _marks[i] = null;
            }
            foreach (var kv in _planes) if (kv.Value != null) Destroy(kv.Value.gameObject);
            _planes.Clear();
            _planeOwner.Clear();
            _planeSession = int.MinValue;
            MyPlaneIndex = -1;
        }

        Transform MakeStain(SeatedBean b, int slot, int n)
        {
            var stain = Make("CoffeeStain" + n, _sphereMesh, _coffeeMat, new Vector3(0.11f - 0.015f * n, 0.085f, 0.022f));
            return stain;
        }

        void SetPostIt(Marks m, SeatedBean b, int slot, int word)
        {
            m.Word = word;
            if (word < 0) { if (m.PostIt != null) m.PostIt.gameObject.SetActive(false); return; }
            if (m.PostIt == null)
            {
                var note = new GameObject("PostIt").transform;
                note.SetParent(transform, false);
                var paper = Make("Paper", _quadMesh, _yellowMat, new Vector3(0.16f, 0.125f, 1f), note);
                paper.localPosition = Vector3.zero;
                var textGo = new GameObject("Word");
                textGo.transform.SetParent(note, false);
                textGo.transform.localPosition = new Vector3(0f, 0f, -0.002f);
                var tmp = textGo.AddComponent<TextMeshPro>();
                if (_font != null) tmp.font = _font;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = new Color(0.12f, 0.16f, 0.27f);
                tmp.fontStyle = FontStyles.Bold;
                tmp.enableAutoSizing = true;
                tmp.fontSizeMin = 0.05f;
                tmp.fontSizeMax = 0.6f;
                tmp.rectTransform.sizeDelta = new Vector2(0.15f, 0.1f);
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
                m.PostIt = note;
                m.PostItText = tmp;
            }
            m.PostIt.gameObject.SetActive(true);
            m.PostItText.text = Loc.T("v5.prank.word." + LinePranks.WordId(word));
        }

        static readonly Vector3[] CreamBlobs = { new Vector3(0f, 0f, 0f), new Vector3(0.07f, 0.03f, 0f), new Vector3(-0.06f, 0.04f, 0f), new Vector3(0.02f, -0.06f, 0f), new Vector3(-0.045f, -0.035f, 0f) };
        static readonly float[] CreamSizes = { 0.12f, 0.075f, 0.085f, 0.065f, 0.055f };

        Transform MakeCream(SeatedBean b, int slot)
        {
            var cream = new GameObject("Cream").transform;
            cream.SetParent(transform, false);
            for (int i = 0; i < CreamBlobs.Length; i++)
            {
                var blob = Make("Blob" + i, _sphereMesh, _creamMat, new Vector3(CreamSizes[i], CreamSizes[i] * 0.85f, CreamSizes[i] * 0.4f), cream);
                // on the round face: the further out, the further back
                var o = CreamBlobs[i];
                blob.localPosition = new Vector3(o.x, o.y, -2.2f * (o.x * o.x + o.y * o.y));
            }
            var cherry = Make("Cherry", _sphereMesh, _cherryMat, Vector3.one * 0.032f, cream);
            cherry.localPosition = new Vector3(0.005f, 0.05f, 0.03f);
            return cream;
        }

        /// <summary>Lays every mark on its colleague's body as posed right now (stains on the shirt, the post-it on the forehead,
        /// the cream on the face), turned with the head.</summary>
        void AnchorMarks()
        {
            for (int slot = 0; slot < _marks.Length; slot++)
            {
                var m = _marks[slot];
                if (m == null || m.Bean == null || !m.Any) continue;
                var b = m.Bean;
                if (!Bake(b)) continue;
                Vector3 fh = FaceDir(b, slot, 1f), fb = FaceDir(b, slot, 0.3f);
                for (int n = 0; n < m.Stains.Count; n++)
                {
                    var st = m.Stains[n];
                    Vector3 p;
                    if (st == null || !Sample(StainHeights[n % 4], StainSides[n % 4], fb, out p)) continue;
                    st.SetPositionAndRotation(p + fb * 0.006f, Quaternion.LookRotation(fb, Vector3.up) * Quaternion.Euler(0f, 0f, 25f * n));
                }
                if (m.PostIt != null && m.PostIt.gameObject.activeSelf)
                {
                    Vector3 p;
                    if (Sample(PostItHeight, 0f, fh, out p))
                        m.PostIt.SetPositionAndRotation(p + fh * 0.008f, Quaternion.LookRotation(-fh, Vector3.up) * Quaternion.Euler(-8f, 0f, slot % 2 == 0 ? 6f : -5f));
                }
                if (m.Cream != null)
                {
                    Vector3 p;
                    if (Sample(FaceHeight, 0f, fh, out p)) m.Cream.SetPositionAndRotation(p + fh * 0.006f, Quaternion.LookRotation(fh, Vector3.up));
                }
            }
        }

        // ------------------------------------------------------------------ frame

        void TickPranks(Vector3 eye, float dt)
        {
            if (!_prankInit) return;
            _eye = eye;
            float now = Time.time;
            for (int i = _timed.Count - 1; i >= 0; i--)
            {
                if (now < _timed[i].At) continue;
                var a = _timed[i].Do;
                _timed.RemoveAt(i);
                try { a(); } catch (Exception e) { Debug.LogException(e); }
            }
            for (int i = _flights.Count - 1; i >= 0; i--)
            {
                var f = _flights[i];
                if (f.T == null) { _flights.RemoveAt(i); continue; }
                float u = Mathf.Clamp01((now - f.Start) / Mathf.Max(0.05f, f.Duration));
                if (!f.Landed)
                {
                    Vector3 p = Vector3.Lerp(f.From, f.To, u) + Vector3.up * (Mathf.Sin(u * Mathf.PI) * f.Arc);
                    if (f.Nose) p += f.Side * Mathf.Sin(u * Mathf.PI * 2f);
                    Vector3 vel = p - f.T.position;
                    f.T.position = p;
                    if (f.Nose && vel.sqrMagnitude > 1e-8f) f.T.rotation = Quaternion.Slerp(f.T.rotation, Quaternion.LookRotation(vel.normalized, Vector3.up), 0.5f);
                    if (f.Spin > 0f) f.T.rotation = f.T.rotation * Quaternion.Euler(f.Spin * dt * 0.7f, f.Spin * dt, 0f);
                    if (u < 1f) continue;
                    f.Landed = true;
                    f.Start = now;
                    try { f.OnLand?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
                    if (f.Nose && f.Rest) { f.T.rotation = f.RestRot; _flights.RemoveAt(i); continue; }
                    if (f.FadeAt == 0f) { Destroy(f.T.gameObject); _flights.RemoveAt(i); }
                    continue;
                }
                // after the hit: what was thrown drops onto the table, rests a moment and shrinks away
                float k = now - f.Start;
                if (f.Rest && k < 0.35f)
                {
                    float d = k / 0.35f;
                    f.T.position = Vector3.Lerp(f.To, f.To + f.Side, d) + Vector3.up * (0.08f * Mathf.Sin(d * Mathf.PI));
                    f.T.rotation = Quaternion.Slerp(f.T.rotation, f.RestRot, d);
                    continue;
                }
                if (k > f.FadeAt)
                {
                    float s = Mathf.Clamp01(1f - (k - f.FadeAt) / 0.3f);
                    f.T.localScale *= Mathf.Lerp(0.8f, 1f, s);
                    if (s <= 0f || f.T.localScale.x < 1e-3f) { Destroy(f.T.gameObject); _flights.RemoveAt(i); }
                }
            }
            for (int i = _drops.Count - 1; i >= 0; i--)
            {
                var d = _drops[i];
                if (d.T == null || now > d.Until) { if (d.T != null) Destroy(d.T.gameObject); _drops.RemoveAt(i); continue; }
                d.V += Vector3.down * 6f * dt;
                d.T.position += d.V * dt;
            }
            if (_manager != null && now < _managerLookUntil) _manager.LookAt(_managerLookAt);
            AnchorMarks();
        }

        /// <summary>A prank's stand-up (the seat's colleague on their feet for a moment).</summary>
        bool PrankStanding(int seat) { return seat >= 0 && seat < _prankStandUntil.Length && Time.time < _prankStandUntil[seat]; }

        /// <summary>Tests and mocks: things in the air, drops, marks.</summary>
        public int PranksFlying => _flights.Count + _drops.Count;
    }
}
