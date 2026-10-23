using System.Collections.Generic;
using System.Linq;
using BirimizKovulacak.Core;
using BirimizKovulacak.Game;
using TMPro;
using Unity.AI.Navigation;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace BirimizKovulacak.EditorTools
{
    public static partial class ProjectBuilder
    {
        static Transform _env;

        static Vector3 V(Vec3 v) { return new Vector3(v.X, v.Y, v.Z); }

        static void BuildScene(PrefabSet prefabs, FontSet fonts, PipelineAssets pipeline)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var layout = DefaultOffice.Create();

            // ---------------- lighting & environment
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex("#CFE4FF") * 1.05f;
            RenderSettings.ambientEquatorColor = Hex("#E9DFCF");
            RenderSettings.ambientGroundColor = Hex("#7C6C5D");
            RenderSettings.fog = false;
            var skyMat = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.skybox = skyMat;
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Hex("#FFF2DE");
            sun.intensity = 1.45f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            sun.transform.rotation = Quaternion.Euler(52f, -32f, 0f);
            var vol = new GameObject("PostProcess").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = pipeline.Profile;

            _env = new GameObject("Office").transform;
            var arch = new GameObject("Architecture").transform; arch.SetParent(_env);
            BuildFloor(arch);
            BuildWalls(arch);

            // ---------------- gameplay layout
            var world = new GameObject("World");
            var ow = world.AddComponent<OfficeWorld>();
            var palette = world.AddComponent<WorldPalette>();
            palette.PlayerColors = new[] { Mat("M_PlayerColor0"), Mat("M_PlayerColor1"), Mat("M_PlayerColor2"), Mat("M_PlayerColor3") };
            palette.Screen = Mat("M_Screen");
            palette.ScreenLocked = Mat("M_ScreenLocked");
            palette.ConeMaterial = Mat("M_VisionCone");
            palette.HighlightMaterial = Mat("M_FocusMarker");
            ow.ItemPrefabs = prefabs.Items.Select(kv => new OfficeWorld.ItemPrefab { Variant = kv.Key, Prefab = kv.Value }).ToArray();

            var props = new GameObject("Props").transform; props.SetParent(_env);
            var desks = new List<DeskVisual>();
            string[] deskDecor = { "Cactus", "PhotoFrame", "DeskLamp", "SmallPlant" };
            foreach (var d in layout.Desks) desks.Add(BuildDesk(props, d, fonts, deskDecor[d.Index % deskDecor.Length]));
            ow.Desks = desks.ToArray();
            var stations = new List<StationVisual>();
            BuildStations(props, layout, fonts, stations);
            ow.Stations = stations.ToArray();
            BuildDecor(props, fonts);
            var alarms = BuildAlarm(props);
            ow.AlarmLights = alarms.Item1;
            ow.AlarmRotor = alarms.Item2;

            // Manager visual at home
            var mgr = (GameObject)PrefabUtility.InstantiatePrefab(prefabs.Manager);
            mgr.transform.SetPositionAndRotation(V(layout.ManagerHome), Quaternion.Euler(0, layout.ManagerHomeYaw, 0));
            ow.Manager = mgr.GetComponent<ManagerVisual>();

            // ---------------- camera
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 120f;
            camGo.AddComponent<AudioListener>();
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.AddComponent<CameraRig>();
            camGo.transform.SetPositionAndRotation(new Vector3(-1, 9, -14), Quaternion.Euler(30, 0, 0));

            // ---------------- bootstrap / network / UI
            var boot = new GameObject("Bootstrap");
            boot.AddComponent<GameBootstrap>();
            var nm = boot.AddComponent<NetworkManager>();
            var utp = boot.AddComponent<UnityTransport>();
            utp.ConnectionData.Port = 7777;
            utp.MaxPayloadSize = 65536;
            utp.MaxPacketQueueSize = 512;
            utp.DisconnectTimeoutMS = 10000;
            utp.HeartbeatTimeoutMS = 500;
            nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = utp,
                PlayerPrefab = prefabs.Avatar,
                ConnectionApproval = true,
                EnableSceneManagement = false,
                TickRate = 30,
                ClientConnectionBufferTimeout = 15,
            };
            string listPath = GenDir + "/NetworkPrefabs.asset";
            AssetDatabase.DeleteAsset(listPath);
            var list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
            list.Add(new NetworkPrefab { Prefab = prefabs.Avatar });
            list.Add(new NetworkPrefab { Prefab = prefabs.Session });
            AssetDatabase.CreateAsset(list, listPath);
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(list);
            var conn = boot.AddComponent<ConnectionManager>();
            conn.SessionPrefab = prefabs.Session.GetComponent<NetworkObject>();
            boot.AddComponent<ClientState>();
            boot.AddComponent<Sfx>();
            var lpc = boot.AddComponent<LocalPlayerController>();
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.name = "FocusMarker";
            marker.transform.localScale = new Vector3(0.12f, 0.12f, 0.12f);
            marker.transform.rotation = Quaternion.Euler(45, 0, 45);
            marker.GetComponent<MeshRenderer>().sharedMaterial = Mat("M_FocusMarker");
            marker.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            lpc.FocusMarker = marker.transform;

            var uiGo = new GameObject("UI");
            var doc = uiGo.AddComponent<UIDocument>();
            string psPath = GenDir + "/PanelSettings.asset";
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(psPath);
            if (ps == null) { ps = ScriptableObject.CreateInstance<PanelSettings>(); AssetDatabase.CreateAsset(ps, psPath); }
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(Root + "/UI/RuntimeTheme.tss");
            EditorUtility.SetDirty(ps);
            doc.panelSettings = ps;
            var ui = uiGo.AddComponent<UIRoot>();
            ui.Style = AssetDatabase.LoadAssetAtPath<StyleSheet>(Root + "/UI/Game.uss");
            ui.BodyFont = fonts.Body;
            ui.BoldFont = fonts.Bold;
            ui.TitleFont = fonts.Title;

            // ---------------- navigation (for test bots)
            var navGo = new GameObject("NavMesh");
            var surface = navGo.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~(1 << Layers.Avatar);
            surface.BuildNavMesh();
            string navPath = Root + "/Scenes/Office_NavMesh.asset";
            AssetDatabase.DeleteAsset(navPath);
            if (surface.navMeshData != null) AssetDatabase.CreateAsset(surface.navMeshData, navPath);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            LogLayoutCheck(layout);
            Log("Scene saved: " + ScenePath);
        }

        // ================================================================== architecture

        static void BuildFloor(Transform parent)
        {
            var carpet = Model("Architecture", "Floor_Carpet");
            var tile = Model("Architecture", "Floor_Tile");
            var wood = Model("Architecture", "Floor_Wood");
            var floors = new GameObject("Floor").transform; floors.SetParent(parent);
            for (int ix = 0; ix < 10; ix++)
                for (int iz = 0; iz < 7; iz++)
                {
                    float x = -9f + ix * 2f, z = -6f + iz * 2f;
                    GameObject m = carpet;
                    if (x > 3f && z > 2.5f) m = wood;
                    else if (x > 6f && z < 2.5f) m = tile;
                    Inst(m, floors, new Vector3(x, 0, z));
                }
            var col = new GameObject("FloorCollider");
            col.transform.SetParent(parent);
            var bc = col.AddComponent<BoxCollider>();
            bc.center = new Vector3(0, -0.05f, 0);
            bc.size = new Vector3(21f, 0.1f, 15f);
            col.isStatic = true;
        }

        enum Piece { Wall, Window, Glass, Accent }

        static void Segment(Transform parent, Vector3 a, Vector3 b, Piece piece)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 0.05f) return;
            float unit = piece == Piece.Window ? 2f : 1f;
            int n = Mathf.Max(1, Mathf.RoundToInt(len / unit));
            float size = len / n;
            float yaw = -Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg;
            string model = piece == Piece.Window ? "WallWindow_2m" : piece == Piece.Glass ? "Glass_1m" : piece == Piece.Accent ? "WallAccent_1m" : "Wall_1m";
            var m = Model("Architecture", model);
            for (int i = 0; i < n; i++)
            {
                Vector3 c = a + d.normalized * (size * (i + 0.5f));
                var go = Inst(m, parent, c, yaw);
                go.transform.localScale = new Vector3(size / unit, 1, 1);
                int layer = piece == Piece.Glass ? Layers.Glass : Layers.Walls;
                SetLayerRecursive(go, layer);
                var bc = go.AddComponent<BoxCollider>();
                bc.center = new Vector3(0, 1.3f, 0);
                bc.size = new Vector3(unit, 2.6f, piece == Piece.Glass ? 0.08f : 0.16f);
                go.isStatic = true;
            }
        }

        static void Door(Transform parent, Vector3 center, float yaw)
        {
            var go = Inst(Model("Architecture", "DoorFrame_1m6"), parent, center, yaw, "DoorFrame");
            SetLayerRecursive(go, Layers.Glass);
        }

        static void BuildWalls(Transform parent)
        {
            var walls = new GameObject("Walls").transform; walls.SetParent(parent);
            float X0 = -10, X1 = 10, Z0 = -7, Z1 = 7;
            // South wall (windows in the middle runs)
            Segment(walls, new Vector3(X0, 0, Z0), new Vector3(-8, 0, Z0), Piece.Wall);
            Segment(walls, new Vector3(-8, 0, Z0), new Vector3(-2, 0, Z0), Piece.Window);
            Segment(walls, new Vector3(-2, 0, Z0), new Vector3(0, 0, Z0), Piece.Wall);
            Segment(walls, new Vector3(0, 0, Z0), new Vector3(8, 0, Z0), Piece.Window);
            Segment(walls, new Vector3(8, 0, Z0), new Vector3(X1, 0, Z0), Piece.Wall);
            // East wall (equipment wall)
            Segment(walls, new Vector3(X1, 0, Z0), new Vector3(X1, 0, Z1), Piece.Wall);
            // North wall
            Segment(walls, new Vector3(X1, 0, Z1), new Vector3(8, 0, Z1), Piece.Wall);
            Segment(walls, new Vector3(8, 0, Z1), new Vector3(4, 0, Z1), Piece.Window);
            Segment(walls, new Vector3(4, 0, Z1), new Vector3(3, 0, Z1), Piece.Wall);
            Segment(walls, new Vector3(3, 0, Z1), new Vector3(-4, 0, Z1), Piece.Accent);
            Segment(walls, new Vector3(-4, 0, Z1), new Vector3(-5, 0, Z1), Piece.Wall);
            Segment(walls, new Vector3(-5, 0, Z1), new Vector3(-9, 0, Z1), Piece.Window);
            Segment(walls, new Vector3(-9, 0, Z1), new Vector3(X0, 0, Z1), Piece.Wall);
            // West wall
            Segment(walls, new Vector3(X0, 0, Z1), new Vector3(X0, 0, 2.5f), Piece.Wall);
            Segment(walls, new Vector3(X0, 0, 2.5f), new Vector3(X0, 0, 2f), Piece.Wall);
            Segment(walls, new Vector3(X0, 0, 2f), new Vector3(X0, 0, -6f), Piece.Window);
            Segment(walls, new Vector3(X0, 0, -6f), new Vector3(X0, 0, Z0), Piece.Wall);
            // Manager office partitions (glass) with a door at x = -5
            Segment(walls, new Vector3(X0, 0, 2.5f), new Vector3(-5.84f, 0, 2.5f), Piece.Glass);
            Door(walls, new Vector3(-5.0f, 0, 2.5f), 0);
            Segment(walls, new Vector3(-4.16f, 0, 2.5f), new Vector3(-4f, 0, 2.5f), Piece.Glass);
            Segment(walls, new Vector3(-4f, 0, 2.5f), new Vector3(-4f, 0, Z1), Piece.Glass);
            // Meeting room partitions (glass) with a door at x = 4
            Segment(walls, new Vector3(3f, 0, 2.5f), new Vector3(3.16f, 0, 2.5f), Piece.Glass);
            Door(walls, new Vector3(4.0f, 0, 2.5f), 0);
            Segment(walls, new Vector3(4.84f, 0, 2.5f), new Vector3(X1, 0, 2.5f), Piece.Glass);
            Segment(walls, new Vector3(3f, 0, 2.5f), new Vector3(3f, 0, Z1), Piece.Glass);

            var sky = Model("Architecture", "Skyline");
            Inst(sky, parent, new Vector3(0, 0, -17), 180, "Skyline_S");
            Inst(sky, parent, new Vector3(-20, 0, 0), -90, "Skyline_W");
            Inst(sky, parent, new Vector3(0, 0, 17), 0, "Skyline_N");
            Inst(sky, parent, new Vector3(20, 0, 0), 90, "Skyline_E");
        }

        // ================================================================== props

        static GameObject Prop(Transform parent, string model, Vector3 pos, float yaw, string name = null, bool collider = true, int layer = Layers.Furniture)
        {
            var m = Model("Props", model);
            if (m == null) { Log("Missing prop model " + model); return new GameObject(name ?? model); }
            var go = Inst(m, parent, pos, yaw, name ?? model);
            SetLayerRecursive(go, layer);
            go.isStatic = false;
            if (collider) AddBoundsCollider(go);
            return go;
        }

        static void AddBoundsCollider(GameObject go, float shrink = 0.98f)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            var inv = go.transform.worldToLocalMatrix;
            Bounds b = new Bounds();
            bool first = true;
            foreach (var r in rs)
            {
                var wb = r.bounds;
                foreach (var c in Corners(wb))
                {
                    var lc = inv.MultiplyPoint3x4(c);
                    if (first) { b = new Bounds(lc, Vector3.zero); first = false; }
                    else b.Encapsulate(lc);
                }
            }
            var bc = go.AddComponent<BoxCollider>();
            bc.center = b.center;
            bc.size = Vector3.Scale(b.size, new Vector3(shrink, 1, shrink));
        }

        static IEnumerable<Vector3> Corners(Bounds b)
        {
            for (int i = 0; i < 8; i++)
                yield return new Vector3(i % 2 == 0 ? b.min.x : b.max.x, (i / 2) % 2 == 0 ? b.min.y : b.max.y, i / 4 == 0 ? b.min.z : b.max.z);
        }

        static Transform Anchor(Transform parent, string id, Vector3 localPos, float yaw = 0)
        {
            var go = new GameObject(id);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            return go.transform;
        }

        static DeskVisual BuildDesk(Transform parent, DeskDef d, FontSet fonts, string decor)
        {
            var root = new GameObject(d.Id).transform;
            root.SetParent(parent);
            root.SetPositionAndRotation(V(d.Position), Quaternion.Euler(0, d.Yaw, 0));
            var desk = Prop(root, "Desk", Vector3.zero, 0, "DeskModel");
            var dv = root.gameObject.AddComponent<DeskVisual>();
            dv.DeskIndex = d.Index;
            var drawer = FindDeep(desk.transform, "Drawer");
            dv.Drawer = drawer;
            if (drawer != null) Anchor(drawer, d.DrawerSlotId, new Vector3(0, 0.05f, -0.12f));
            else Anchor(root, d.DrawerSlotId, V(DefaultOffice.DrawerOffset));
            Anchor(root, d.SurfaceSlotId, V(DefaultOffice.SurfaceOffset));
            Anchor(root, d.NameplateSlotId, V(DefaultOffice.NameplateOffset));
            var pc = Prop(root, "Computer", V(DefaultOffice.ComputerOffset), 0, d.ComputerId, false);
            var screen = FindDeep(pc.transform, "Screen");
            dv.Screen = screen != null ? screen.GetComponent<Renderer>() : null;
            var lockGo = Prop(pc.transform, "Padlock", new Vector3(0.18f, 0.43f, -0.04f), 0, "Padlock", false);
            lockGo.SetActive(false);
            dv.Padlock = lockGo;
            var chair = Prop(root, "OfficeChair", V(DefaultOffice.SeatOffset), 0, "Chair", false);
            foreach (var r in chair.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) if (mats[i] != null && mats[i].name == "M_ChairFabric") mats[i] = Mat("M_PlayerColor" + d.Index);
                r.sharedMaterials = mats;
            }
            var dirt = Prop(root, "DeskDirt", new Vector3(0f, 0.762f, -0.05f), 0, "Dirt", false);
            dirt.SetActive(false);
            dv.Dirt = dirt;
            var spill = Prop(root, "CoffeeSpill", new Vector3(-0.1f, 0.765f, 0.05f), 0, "Spill", false);
            spill.SetActive(false);
            dv.Spill = spill;
            Prop(root, decor, new Vector3(0.75f, 0.76f, 0.3f), 200, "Decor", false);
            if (d.Index == 1) Prop(root, "PencilCup", new Vector3(0.55f, 0.76f, 0.32f), 0, "Decor2", false);
            Prop(root, "Mug", new Vector3(-0.72f, 0.76f, -0.25f), 30, "DecorMug", false);
            return dv;
        }

        static void BuildStations(Transform parent, OfficeLayout layout, FontSet fonts, List<StationVisual> stations)
        {
            // Coffee corner
            Prop(parent, "Counter", new Vector3(9.62f, 0, 0.6f), 90, "CoffeeCounter");
            var coffee = Prop(parent, "CoffeeMachine", new Vector3(9.62f, 0.92f, 0.2f), 90, EntityIds.CoffeeMachine, false);
            var cv = coffee.AddComponent<StationVisual>();
            cv.StationId = EntityIds.CoffeeMachine;
            cv.Steam = Steam(coffee.transform, new Vector3(0, 0.35f, -0.2f));
            stations.Add(cv);
            Prop(parent, "MugShelf", new Vector3(9.62f, 0.92f, 1.1f), 90, EntityIds.MugShelf, false);
            Label3D(Prop(parent, "Corkboard", new Vector3(9.9f, -0.05f, 0.6f), 90, "CoffeeBoard", false).transform, "Text", new Vector3(0, 1.62f, -0.03f), Vector3.zero, new Vector2(1.0f, 0.3f), fonts.TmpTitle, Hex("#2E2257"), "KAHVE KÖŞESİ");

            // Printer
            var printer = Prop(parent, "Printer", new Vector3(9.35f, 0, -3.5f), 90, EntityIds.Printer);
            var pv = printer.AddComponent<StationVisual>();
            pv.StationId = EntityIds.Printer;
            var light = FindDeep(printer.transform, "StatusLight");
            pv.Light = light != null ? light.GetComponent<Renderer>() : null;
            pv.LightOn = Mat("M_GreenOn"); pv.LightOff = Mat("M_GreenOff");
            stations.Add(pv);
            Anchor(printer.transform, EntityIds.PrinterTray, new Vector3(-0.05f, 0.885f, -0.2f));
            var spot = Anchor(parent, EntityIds.HideBehindPrinter, new Vector3(9.8f, 0.05f, -3.5f), 90);

            // Phone stand
            var phone = Prop(parent, "PhoneTable", V(layout.GetStation(EntityIds.Phone).Position), 0, EntityIds.Phone);
            var phv = phone.AddComponent<StationVisual>();
            phv.StationId = EntityIds.Phone;
            phv.ShakeTarget = FindDeep(phone.transform, "Handset");
            var ring = FindDeep(phone.transform, "RingLight");
            phv.Light = ring != null ? ring.GetComponent<Renderer>() : null;
            phv.LightOn = Mat("M_LightOn"); phv.LightOff = Mat("M_LightOff");
            stations.Add(phv);

            // Delivery (manager's inbox + coffee tray), outside the manager's door
            var del = Prop(parent, "DeliveryCabinet", new Vector3(-3.4f, 0, 2.17f), 0, EntityIds.DeliveryPoint);
            Anchor(del.transform, EntityIds.DeliveryTray, new Vector3(0, 0.92f, 0));
            Label3D(del.transform, "Sign", new Vector3(0, 1.36f, 0.17f), Vector3.zero, new Vector2(0.5f, 0.15f), fonts.TmpTitle, Color.white, "TESLİM");

            // Cleaning closet + spray rack
            var closet = Prop(parent, "CleaningCloset", new Vector3(-9.45f, 0, -6.5f), 225, EntityIds.CleaningCloset);
            Anchor(closet.transform, EntityIds.SprayRack, new Vector3(0, 0.9f, -0.05f));
            Label3D(closet.transform, "Sign", new Vector3(0, 1.78f, -0.255f), Vector3.zero, new Vector2(0.3f, 0.08f), fonts.Tmp, Hex("#2E2257"), "TEMİZLİK");
            Prop(parent, "WetFloorSign", new Vector3(-8.5f, 0, -6.1f), 200, "WetFloorSign", false);

            // Lost & found, junk bin
            var lf = Prop(parent, "LostFoundBox", new Vector3(0.8f, 0, -6.45f), 180, EntityIds.LostAndFound);
            Label3D(lf.transform, "Label", new Vector3(0, 0.17f, -0.218f), Vector3.zero, new Vector2(0.34f, 0.09f), fonts.Tmp, Hex("#2E2257"), "KAYIP EŞYA");
            var junk = Prop(parent, "JunkBin", new Vector3(3.0f, 0, -6.45f), 180, EntityIds.JunkBin);
            Label3D(junk.transform, "Label", new Vector3(0, 0.3f, -0.268f), Vector3.zero, new Vector2(0.48f, 0.14f), fonts.Tmp, Hex("#2E2257"), "GEREKSİZ\nBELGELER");

            // Hiding spots
            var plant = Prop(parent, "BigPlant", new Vector3(-3.4f, 0, 6.35f), 0, "BigPlant");
            Anchor(plant.transform, EntityIds.HideInPlant, new Vector3(0, 0.48f, 0));
            var cab = Prop(parent, "FileCabinet", new Vector3(2.3f, 0, 6.5f), 0, "FileCabinet");
            Anchor(cab.transform, EntityIds.HideInCabinet, new Vector3(0, 0.62f, -0.12f));
        }

        static ParticleSystem Steam(Transform parent, Vector3 pos)
        {
            var go = new GameObject("Steam");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 1.2f; main.startSpeed = 0.4f; main.startSize = 0.12f;
            main.loop = true; main.playOnAwake = false;
            main.startColor = new Color(1, 1, 1, 0.5f);
            var em = ps.emission; em.rateOverTime = 14f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 8f; shape.radius = 0.03f;
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 2f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Mat("M_Steam");
            ps.Stop();
            return ps;
        }

        static void BuildDecor(Transform parent, FontSet fonts)
        {
            // Manager office
            Prop(parent, "ManagerDesk", new Vector3(-7.0f, 0, 4.75f), 180, "ManagerDesk");
            Prop(parent, "ExecutiveChair", new Vector3(-7.0f, 0, 5.75f), 180, "ManagerChair", false);
            Prop(parent, "Bookshelf", new Vector3(-9.75f, 0, 5.1f), 270, "ManagerShelf");
            Prop(parent, "Dartboard", new Vector3(-8.3f, 1.6f, 6.9f), 0, "Dartboard", false);
            Prop(parent, "SmallPlant", new Vector3(-6.2f, 0.78f, 4.55f), 0, "MgrPlant", false);
            Prop(parent, "BigPlant", new Vector3(-9.4f, 0, 3.1f), 30, "MgrBigPlant");
            var door = new GameObject("MgrDoorSign").transform;
            door.SetParent(parent);
            door.position = new Vector3(-5.0f, 2.42f, 2.42f);
            Label3D(door, "Text", Vector3.zero, Vector3.zero, new Vector2(1.4f, 0.24f), fonts.TmpTitle, Hex("#D62F3B"), "MÜDÜR");

            // Meeting room
            Prop(parent, "MeetingTable", new Vector3(6.5f, 0, 4.9f), 0, "MeetingTable");
            var layout = DefaultOffice.Create();
            for (int i = 0; i < layout.MeetingSeats.Count; i++)
                Prop(parent, "MeetingChair", V(layout.MeetingSeats[i].Position), layout.MeetingSeats[i].Yaw, "MeetingChair" + i, false);
            var wb = Prop(parent, "Whiteboard", new Vector3(9.88f, 0, 4.9f), 90, "Whiteboard", false);
            Label3D(wb.transform, "Title", new Vector3(0, 1.82f, -0.03f), Vector3.zero, new Vector2(1.5f, 0.22f), fonts.TmpTitle, Hex("#2E2257"), "OLAY GÜNLÜĞÜ");
            Label3D(wb.transform, "Body", new Vector3(0, 1.4f, -0.03f), Vector3.zero, new Vector2(1.6f, 0.55f), fonts.Tmp, Hex("#555066"), "Kim? Ne zaman? Neden ördek?", 1.2f);
            var mdoor = new GameObject("MeetingDoorSign").transform;
            mdoor.SetParent(parent);
            mdoor.position = new Vector3(4.0f, 2.42f, 2.42f);
            Label3D(mdoor, "Text", Vector3.zero, Vector3.zero, new Vector2(2.2f, 0.22f), fonts.TmpTitle, Hex("#2E2257"), "İK TOPLANTI ODASI");

            // Lounge
            Prop(parent, "Sofa", new Vector3(-1.2f, 0, 6.45f), 0, "Sofa");
            Prop(parent, "WaterCooler", new Vector3(1.0f, 0, 6.6f), 0, "WaterCooler");
            var cork = Prop(parent, "Corkboard", new Vector3(-1.2f, 0, 6.9f), 0, "GoalBoard", false);
            Label3D(cork.transform, "Text", new Vector3(0, 1.75f, -0.03f), Vector3.zero, new Vector2(1.1f, 0.22f), fonts.TmpTitle, Hex("#D62F3B"), "BUGÜNÜN HEDEFİ: KÂR!");
            Prop(parent, "WallClock", new Vector3(9.9f, 2.15f, -1.6f), 90, "Clock", false);
            Prop(parent, "SmallPlant", new Vector3(9.62f, 0.92f, 1.55f), 0, "CounterPlant", false);
            Prop(parent, "BigPlant", new Vector3(-9.3f, 0, 1.9f), 70, "CornerPlant");
            Prop(parent, "BigPlant", new Vector3(9.3f, 0, -6.3f), 10, "CornerPlant2");
            Prop(parent, "Bookshelf", new Vector3(5.6f, 0, -6.72f), 180, "OfficeShelf");

            // Pendant lamps over the desk rows
            foreach (var p in new[] { new Vector3(6.5f, 2.75f, 4.9f) })
                Inst(Model("Architecture", "PendantLamp"), parent, p, 0, "Lamp");
        }

        static System.Tuple<Light[], Transform> BuildAlarm(Transform parent)
        {
            var lights = new List<Light>();
            var rotor = new GameObject("AlarmRotor").transform;
            rotor.SetParent(parent);
            rotor.position = new Vector3(-5.0f, 2.62f, 2.3f);
            Prop(parent, "AlarmLight", new Vector3(-5.0f, 2.6f, 2.3f), 0, "AlarmLight", false);
            for (int i = 0; i < 2; i++)
            {
                var l = new GameObject("AlarmSpot" + i).AddComponent<Light>();
                l.transform.SetParent(rotor, false);
                l.transform.localRotation = Quaternion.Euler(35, i * 180, 0);
                l.type = LightType.Spot;
                l.color = new Color(1f, 0.15f, 0.1f);
                l.intensity = 25f;
                l.range = 22f;
                l.spotAngle = 70f;
                l.shadows = LightShadows.None;
                l.enabled = false;
                lights.Add(l);
            }
            return System.Tuple.Create(lights.ToArray(), rotor);
        }

        static void LogLayoutCheck(OfficeLayout layout)
        {
            // Report anchors whose scene position differs from the rules layout (interaction ranges rely on it).
            foreach (var s in layout.Slots)
            {
                var go = GameObject.Find(s.Id);
                if (go == null) { Log("ANCHOR MISSING " + s.Id); continue; }
                float dist = Vector2.Distance(new Vector2(go.transform.position.x, go.transform.position.z), new Vector2(s.Position.X, s.Position.Z));
                if (dist > 0.5f) Log("ANCHOR OFFSET " + s.Id + " " + dist.ToString("0.00") + "m scene=" + go.transform.position + " layout=" + V(s.Position));
            }
            foreach (var s in layout.Stations)
                if (GameObject.Find(s.Id) == null) Log("STATION MISSING " + s.Id);
            var desk = GameObject.Find("desk.0");
            if (desk != null)
            {
                var drawer = FindDeep(desk.transform, "Drawer");
                Log("desk.0 drawer local=" + (drawer != null ? drawer.localPosition.ToString("F2") + " rot=" + drawer.localEulerAngles.ToString("F0") : "none"));
                var model = FindDeep(desk.transform, "DeskModel");
                if (model != null) foreach (Transform c in model) Log("  DeskModel child " + c.name + " rot=" + c.localEulerAngles.ToString("F0"));
            }
        }
    }
}
