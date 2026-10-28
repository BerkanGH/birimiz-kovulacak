using System.Collections.Generic;
using System.IO;
using System.Linq;
using BirimizKovulacak.Core;
using BirimizKovulacak.Game;
using Newtonsoft.Json.Linq;
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
    /// <summary>
    /// Office v2 scene (map agent). Sources:
    ///  - gameplay positions: DefaultOffice.Create() (desks, stations, slots, cameras, manager, seats);
    ///  - architecture: Models/Architecture/OfficeShell.fbx (generated from the layout by build_architecture.py);
    ///  - dressing, lights, decal spots and signs: ../Art/Layout/office_v2.json (map_layout.py).
    /// See docs/MAP.md.
    /// </summary>
    public static partial class ProjectBuilder
    {
        public const string MapJsonPath = "../Art/Layout/office_v2.json";
        public const string DecalSourceDir = "../Art/UI/decals";
        public const string DecalDir = Root + "/Art/Decals";
        static readonly string[] PlayerColorHex = { "#4A90E2", "#F5C842", "#F28AB2", "#6CC56A", "#FF8A3D", "#9D6BDB" };

        static Transform _env;

        sealed class MapBuild
        {
            public OfficeLayout Layout;
            public JObject Map;
            public FontSet Fonts;
            public readonly List<StationVisual> Stations = new List<StationVisual>();
            public readonly List<DeviceVisual> Devices = new List<DeviceVisual>();
            public readonly List<Light> RoomLights = new List<Light>();
            public readonly List<Light> EmergencyLights = new List<Light>();
            public readonly List<Renderer> LampGlows = new List<Renderer>();
            public readonly List<Renderer> EmergencyLamps = new List<Renderer>();
            public readonly HashSet<GameObject> NonStatic = new HashSet<GameObject>();
            public int Racks, Cameras;
        }

        static Vector3 V(Vec3 v) { return new Vector3(v.X, v.Y, v.Z); }
        static Vector3 V3(JToken t) { return new Vector3((float)t[0], (float)t[1], (float)t[2]); }
        static bool Flag(JObject e, string key, bool dflt) { var t = e[key]; return t == null ? dflt : (bool)t; }

        static void BuildScene(PrefabSet prefabs, FontSet fonts, PipelineAssets pipeline)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var layout = DefaultOffice.Create();
            var mb = new MapBuild { Layout = layout, Map = LoadMap(), Fonts = fonts };
            EnsureMapMaterials();
            CheckMapFreshness(mb);

            // ---------------- lighting & environment (warm interior; one shadowed light)
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex("#DCE6F5");
            RenderSettings.ambientEquatorColor = Hex("#EADFCC");
            RenderSettings.ambientGroundColor = Hex("#7A6A5A");
            RenderSettings.fog = false;
            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Hex("#FFF0D8");
            sun.intensity = 1.35f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.7f;
            sun.lightmapBakeType = LightmapBakeType.Realtime;
            sun.transform.rotation = Quaternion.Euler(52f, -32f, 0f);
            var vol = new GameObject("PostProcess").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = pipeline.Profile;

            _env = new GameObject("Office").transform;
            var arch = new GameObject("Architecture").transform; arch.SetParent(_env);
            BuildShell(arch);
            BuildFloorCollider(arch, layout);
            BuildSkyline(arch);

            // ---------------- gameplay layout
            var world = new GameObject("World");
            var ow = world.AddComponent<OfficeWorld>();
            var palette = world.AddComponent<WorldPalette>();
            palette.PlayerColors = Enumerable.Range(0, PlayerColorHex.Length).Select(i => Mat("M_PlayerColor" + i)).ToArray();
            palette.Screen = Mat("M_ScreenDesktop");
            palette.ScreenLocked = Mat("M_ScreenLocked");
            palette.ScreenLockScreen = Mat("M_ScreenLockScreen");
            palette.ScreenOff = Mat("M_ScreenOff");
            palette.ScreenNoSignal = Mat("M_ScreenNoSignal");
            palette.ConeMaterial = Mat("M_VisionCone");
            palette.HighlightMaterial = Mat("M_FocusMarker");
            palette.LedGreen = Mat("M_LEDGreen");
            palette.LedRed = Mat("M_LEDRed");
            palette.LedAmber = Mat("M_LEDAmber");
            palette.LedBlue = Mat("M_LEDBlue");
            palette.LedOff = Mat("M_LEDOff");
            palette.WallGhost = Mat("M_WallGhost");
            palette.ChairNeutral = Mat("M_PaintGreyDark");
            ow.ItemPrefabs = prefabs.Items.Select(kv => new OfficeWorld.ItemPrefab { Variant = kv.Key, Prefab = kv.Value }).ToArray();

            var props = new GameObject("Props").transform; props.SetParent(_env);
            var deskRoot = new GameObject("Desks").transform; deskRoot.SetParent(props);
            var desks = new List<DeskVisual>();
            foreach (var d in layout.Desks) desks.Add(BuildDesk(deskRoot, d, mb));
            ow.Desks = desks.ToArray();
            BuildMapProps(props, mb);
            EnsureAnchors(props, layout);
            BuildDecals(props, mb);
            BuildSigns(props, mb);
            var alarms = BuildAlarm(props);
            ow.AlarmLights = alarms.Item1;
            ow.AlarmRotor = alarms.Item2;
            ow.Stations = mb.Stations.ToArray();
            ow.Devices = mb.Devices.ToArray();
            BuildLights(_env, mb, sun);

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
            cam.farClipPlane = 150f;
            camGo.AddComponent<AudioListener>();
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.AddComponent<CameraRig>();
            camGo.transform.SetPositionAndRotation(new Vector3(0, 15, -22), Quaternion.Euler(35, 0, 0));

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

            // ---------------- static batching flags + small-prop shadow culling
            MarkStatic(_env, mb);

            // ---------------- navigation (bots)
            ConfigureNavAgent();
            var navGo = new GameObject("NavMesh");
            var surface = navGo.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~(1 << Layers.Avatar);
            surface.overrideVoxelSize = true;
            surface.voxelSize = 0.1f;
            surface.BuildNavMesh();
            string navPath = Root + "/Scenes/Office_NavMesh.asset";
            AssetDatabase.DeleteAsset(navPath);
            if (surface.navMeshData != null) AssetDatabase.CreateAsset(surface.navMeshData, navPath);
            CheckReachability(layout);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            LogLayoutCheck(layout);
            LogMapStats();
            Log("Scene saved: " + ScenePath);
        }

        // ================================================================== data

        static JObject LoadMap()
        {
            string path = Path.GetFullPath(MapJsonPath);
            if (!File.Exists(path)) throw new System.Exception("Map layout missing: " + path + " (run Art/Scripts/map_layout.py)");
            return JObject.Parse(File.ReadAllText(path));
        }

        /// <summary>Warn when DefaultOffice changed after office_v2.json was generated (re-run map_layout.py / build.ps1 -Art).</summary>
        static void CheckMapFreshness(MapBuild mb)
        {
            var src = mb.Map["source"] as JObject;
            if (src == null) return;
            int stale = 0;
            foreach (var s in mb.Layout.Stations)
            {
                var t = src["stations"]?[s.Id];
                if (t != null && (V3(t) - V(s.Position)).sqrMagnitude > 0.0004f) { Log("MAP JSON STALE: station " + s.Id); stale++; }
            }
            foreach (var s in mb.Layout.Slots)
            {
                var t = src["slots"]?[s.Id];
                if (t != null && (V3(t) - V(s.Position)).sqrMagnitude > 0.0004f) { Log("MAP JSON STALE: slot " + s.Id); stale++; }
            }
            var np = src["nameplateOffset"];
            if (np != null && (V3(np) - V(DefaultOffice.NameplateOffset)).sqrMagnitude > 0.0004f) { Log("MAP JSON STALE: NameplateOffset"); stale++; }
            if (stale > 0) Log("WARNING: office_v2.json is older than DefaultOffice (" + stale + " differences). Run Tools/build.ps1 -Art.");
        }

        /// <summary>Materials the map needs that are not in palette.json (player colours 4-5, wall ghost).</summary>
        static void EnsureMapMaterials()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            for (int i = 0; i < PlayerColorHex.Length; i++)
                if (Mat("M_PlayerColor" + i) == null) CreateLit("M_PlayerColor" + i, lit, Hex(PlayerColorHex[i]), 0.4f, 0, 0.15f, 1);
            var ghost = GetOrCreate("M_WallGhost", lit);
            var c = Hex("#F1E9D8"); c.a = 0.16f;
            ghost.SetColor("_BaseColor", c);
            ghost.SetFloat("_Smoothness", 0.1f);
            ghost.SetFloat("_Metallic", 0f);
            ghost.DisableKeyword("_EMISSION");
            SetTransparent(ghost);
            ghost.SetShaderPassEnabled("ShadowCaster", true);   // faded walls keep their shadows
            EditorUtility.SetDirty(ghost);
            ApplyScreenTextures();
            // Repeated non-static props (chairs, items, mugs) can instance where the SRP Batcher does not apply.
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MatDir }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m != null && !m.enableInstancing) { m.enableInstancing = true; EditorUtility.SetDirty(m); }
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>Monitor state textures (Art/Scripts/map_textures.py) as base + emission maps of the screen materials.</summary>
        static void ApplyScreenTextures()
        {
            var pairs = new[] { ("M_ScreenDesktop", "Desktop", 1.25f), ("M_ScreenLockScreen", "LockScreen", 1.1f), ("M_ScreenLocked", "Locked", 1.25f), ("M_ScreenNoSignal", "NoSignal", 1.0f) };
            foreach (var (matName, file, emit) in pairs)
            {
                string path = Root + "/Art/Textures/Screens/" + file + ".png";
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp != null && (imp.maxTextureSize != 256 || imp.mipmapEnabled == false))
                {
                    imp.textureType = TextureImporterType.Default;
                    imp.sRGBTexture = true;
                    imp.mipmapEnabled = true;
                    imp.maxTextureSize = 256;
                    imp.wrapMode = TextureWrapMode.Clamp;
                    imp.textureCompression = TextureImporterCompression.CompressedHQ;
                    imp.SaveAndReimport();
                }
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                var m = Mat(matName);
                if (tex == null || m == null) { Log("screen texture missing: " + path); continue; }
                m.SetTexture("_BaseMap", tex);
                m.SetColor("_BaseColor", Color.white);
                m.SetTexture("_EmissionMap", tex);
                m.SetColor("_EmissionColor", Color.white * emit);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                EditorUtility.SetDirty(m);
            }
        }

        // ================================================================== architecture

        static void BuildShell(Transform parent)
        {
            var model = Model("Architecture", "OfficeShell");
            if (model == null) { Log("MISSING Architecture/OfficeShell.fbx (run Tools/build.ps1 -Art)"); return; }
            var shell = Inst(model, parent, Vector3.zero, 0, "OfficeShell");
            var mod = shell.AddComponent<NavMeshModifier>();
            mod.overrideArea = true;
            mod.area = NavMesh.GetAreaFromName("Not Walkable");
            mod.applyToChildren = true;
            int walls = 0, glass = 0, low = 0;
            // Walk every mesh node (works whether or not the importer collapsed the FBX's single root node).
            foreach (var mf in shell.GetComponentsInChildren<MeshFilter>(true))
            {
                var c = mf.transform;
                string prefix = c.name.Split('_')[0];
                switch (prefix)
                {
                    case "Wall": case "Header": case "Stall": Solid(c.gameObject, Layers.Walls, true); walls++; break;
                    case "Glass": Solid(c.gameObject, Layers.Glass, false); glass++; break;
                    case "Low": Solid(c.gameObject, Layers.Partitions, true); low++; break;
                    case "GlassDoor": SetLayerRecursive(c.gameObject, Layers.Glass); break;
                    default:
                        foreach (var r in c.GetComponentsInChildren<Renderer>())
                            if (prefix == "Floor" || prefix == "Rug" || prefix == "Ground") r.shadowCastingMode = ShadowCastingMode.Off;
                        break;
                }
            }
            Log("Shell: " + walls + " opaque wall pieces, " + glass + " glass, " + low + " low walls");
        }

        static void Solid(GameObject go, int layer, bool occluder)
        {
            SetLayerRecursive(go, layer);
            var mf = go.GetComponent<MeshFilter>();
            var r = go.GetComponent<Renderer>();
            if (mf == null || mf.sharedMesh == null || r == null) return;
            var bc = go.AddComponent<BoxCollider>();
            bc.center = mf.sharedMesh.bounds.center;
            bc.size = mf.sharedMesh.bounds.size;
            if (occluder)
            {
                var o = go.AddComponent<OccluderWall>();
                o.Renderers = new[] { r };
            }
        }

        static void BuildFloorCollider(Transform parent, OfficeLayout layout)
        {
            var col = new GameObject("FloorCollider");
            col.transform.SetParent(parent);
            var bc = col.AddComponent<BoxCollider>();
            Vector3 min = V(layout.BoundsMin), max = V(layout.BoundsMax);
            bc.center = new Vector3((min.x + max.x) / 2, -0.05f, (min.z + max.z) / 2);
            bc.size = new Vector3(max.x - min.x + 0.6f, 0.1f, max.z - min.z + 0.6f);
        }

        static void BuildSkyline(Transform parent)
        {
            var sky = Model("Architecture", "Skyline");
            if (sky == null) return;
            var root = new GameObject("Skyline").transform; root.SetParent(parent);
            Inst(sky, root, new Vector3(0, 0, -27), 180, "Skyline_S");
            Inst(sky, root, new Vector3(-36, 0, 0), 270, "Skyline_W");
            Inst(sky, root, new Vector3(0, 0, 27), 0, "Skyline_N");
            Inst(sky, root, new Vector3(36, 0, 0), 90, "Skyline_E");
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ================================================================== props

        /// <summary>Collider footprint factors (XZ) for props whose renderer bounds are much wider than what you bump into.</summary>
        static readonly Dictionary<string, float> Footprint = new Dictionary<string, float>
        {
            { "BigPlant", 0.45f }, { "FloorLamp", 0.5f }, { "CoatRack", 0.45f }, { "StandingTable", 0.6f }, { "KitchenTable", 0.75f },
            { "Globe", 0.7f }, { "UmbrellaStand", 0.9f }, { "Planter", 0.9f }, { "Ladder", 0.8f },
        };

        static GameObject Prop(Transform parent, string model, Vector3 pos, float yaw, string name = null, bool collider = true, int layer = Layers.Furniture)
        {
            var m = Model("Props", model);
            if (m == null) { Log("Missing prop model " + model); return new GameObject(name ?? model); }
            var go = Inst(m, parent, pos, yaw, name ?? model);
            SetLayerRecursive(go, layer);
            go.isStatic = false;
            if (collider)
            {
                float f;
                AddBoundsCollider(go, Footprint.TryGetValue(model, out f) ? f : 0.98f);
                NotWalkable(go);
            }
            return go;
        }

        static void NotWalkable(GameObject go)
        {
            var mod = go.AddComponent<NavMeshModifier>();
            mod.overrideArea = true;
            mod.area = NavMesh.GetAreaFromName("Not Walkable");
            mod.applyToChildren = true;
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
            var center = b.center;
            if (shrink < 0.9f) { center.x = 0; center.z = 0; }   // tall plants / lamps: collide with the pot or pole only
            bc.center = center;
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

        /// <summary>Scene anchor at the exact layout pose of a slot or station, parented (world pose kept).</summary>
        static Transform AnchorAt(Transform parent, string id, OfficeLayout layout)
        {
            Vector3 p; float yaw;
            var slot = layout.GetSlot(id);
            var st = layout.GetStation(id);
            if (slot != null) { p = V(slot.Position); yaw = slot.Yaw; }
            else if (st != null) { p = V(st.Position); yaw = st.Yaw; }
            else { Log("Unknown anchor id " + id); return null; }
            var go = new GameObject(id);
            go.transform.SetPositionAndRotation(p, Quaternion.Euler(0, yaw, 0));
            go.transform.SetParent(parent, true);
            return go.transform;
        }

        static void EnsureAnchors(Transform parent, OfficeLayout layout)
        {
            var root = new GameObject("Anchors").transform; root.SetParent(parent);
            foreach (var s in layout.Slots)
                if (GameObject.Find(s.Id) == null) { AnchorAt(root, s.Id, layout); Log("anchor (no prop) " + s.Id); }
            foreach (var s in layout.Stations)
                if (GameObject.Find(s.Id) == null) { AnchorAt(root, s.Id, layout); Log("anchor (no prop) " + s.Id); }
        }

        static void Tint(GameObject go, JObject tint)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var to = tint[mats[i].name];
                    if (to == null) continue;
                    var m = Mat((string)to);
                    if (m != null) { mats[i] = m; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        static Renderer RendererOf(Transform root, string name)
        {
            var t = FindDeep(root, name);
            return t != null ? t.GetComponentInChildren<Renderer>() : null;
        }

        static DeskVisual BuildDesk(Transform parent, DeskDef d, MapBuild mb)
        {
            var kit = (JObject)mb.Map["desk"];
            var root = new GameObject(d.Id).transform;
            root.SetParent(parent);
            root.SetPositionAndRotation(V(d.Position), Quaternion.Euler(0, d.Yaw, 0));
            var desk = Prop(root, (string)kit["model"], Vector3.zero, 0, "DeskModel");
            var dv = root.gameObject.AddComponent<DeskVisual>();
            dv.DeskIndex = d.Index;
            var drawer = FindDeep(desk.transform, "Drawer");
            dv.Drawer = drawer;
            if (drawer != null) { Anchor(drawer, d.DrawerSlotId, new Vector3(0, 0.05f, -0.12f)); mb.NonStatic.Add(drawer.gameObject); }
            else Anchor(root, d.DrawerSlotId, V(DefaultOffice.DrawerOffset));
            Anchor(root, d.SurfaceSlotId, V(DefaultOffice.SurfaceOffset));
            Anchor(root, d.NameplateSlotId, V(DefaultOffice.NameplateOffset));
            bool assigned = d.Index < 4;
            var leds = new List<Renderer>();
            foreach (JObject part in (JArray)kit["parts"])
            {
                string m = (string)part["m"];
                string role = (string)part["role"];
                Vector3 p = V3(part["p"]);
                float yaw = (float)part["r"];
                switch (role)
                {
                    case "pc":
                    {
                        var pc = Prop(root, m, p, yaw, d.ComputerId, false);
                        dv.Screen = RendererOf(pc.transform, "Screen");
                        var led = RendererOf(pc.transform, "PcLed");
                        if (led != null) leds.Add(led);
                        var lockGo = Prop(pc.transform, "Padlock", new Vector3(0.2f, 0.47f, -0.03f), 0, "Padlock", false);
                        lockGo.SetActive(false);
                        dv.Padlock = lockGo;
                        var glow = new GameObject("MonitorGlow").AddComponent<Light>();
                        glow.transform.SetParent(pc.transform, false);
                        glow.transform.localPosition = new Vector3(0, 0.35f, -0.3f);
                        glow.type = LightType.Point;
                        glow.color = Hex("#8FD3FF");
                        glow.range = 2.4f;
                        glow.intensity = 0f;
                        glow.shadows = LightShadows.None;
                        glow.lightmapBakeType = LightmapBakeType.Realtime;
                        glow.enabled = false;
                        dv.MonitorGlow = glow;
                        break;
                    }
                    case "chair":
                    {
                        var chair = Prop(root, m, p, yaw, "Chair", false);
                        dv.Chair = chair.transform;
                        dv.ChairSeat = p;
                        dv.ChairPushedIn = V3(kit["chairPushedIn"]);
                        var rs = chair.GetComponentsInChildren<Renderer>();
                        dv.ChairRenderers = rs;
                        dv.ChairFabricSlots = rs.Select(r => System.Array.FindIndex(r.sharedMaterials, x => x != null && x.name == "M_ChairFabric")).ToArray();
                        foreach (var r in rs)
                        {
                            var mats = r.sharedMaterials;
                            for (int i = 0; i < mats.Length; i++) if (mats[i] != null && mats[i].name == "M_ChairFabric") mats[i] = assigned ? Mat("M_PlayerColor" + d.Index) : Mat("M_PaintGreyDark");
                            r.sharedMaterials = mats;
                        }
                        if (!assigned) chair.transform.localPosition = dv.ChairPushedIn;
                        mb.NonStatic.Add(chair);
                        break;
                    }
                    case "partition":
                    {
                        var panel = Prop(root, m, p, yaw, "Partition", true, Layers.Partitions);
                        var o = panel.AddComponent<OccluderWall>();
                        o.Renderers = panel.GetComponentsInChildren<Renderer>();
                        break;
                    }
                    case "cable":
                    {
                        var cable = Prop(root, m, p, yaw, "UnpluggedCable", false);
                        cable.SetActive(false);
                        dv.Cable = cable;
                        break;
                    }
                    case "dirt":
                    {
                        var dirt = Prop(root, m, p, yaw, "Dirt", false);
                        dirt.SetActive(false);
                        dv.Dirt = dirt;
                        break;
                    }
                    case "spill":
                    {
                        var spill = Prop(root, m, p, yaw, "Spill", false);
                        spill.SetActive(false);
                        dv.Spill = spill;
                        break;
                    }
                    case "empty":
                    {
                        var card = Prop(root, m, p, yaw, "EmptyCard", false);
                        card.SetActive(!assigned);
                        dv.EmptyCard = card;
                        break;
                    }
                    default:
                    {
                        var deco = Prop(root, m, p, yaw, m, false);
                        var led = RendererOf(deco.transform, "PcLed");
                        if (led != null) leds.Add(led);
                        break;
                    }
                }
            }
            dv.PowerLeds = leds.ToArray();
            var personal = new GameObject("Personal").transform;
            personal.SetParent(root, false);
            var sets = (JArray)kit["personal"];
            foreach (JObject e in (JArray)sets[d.Index % sets.Count])
                Prop(personal, (string)e["m"], V3(e["p"]), (float)e["r"], (string)e["m"], false);
            personal.gameObject.SetActive(assigned);
            dv.Personal = personal.gameObject;
            var nh = (JObject)kit["nameplateHolder"];
            Prop(root, (string)nh["m"], V3(nh["p"]), (float)nh["r"], "NameplateRail", false, Layers.Partitions);
            return dv;
        }

        static void BuildMapProps(Transform parent, MapBuild mb)
        {
            var rooms = new Dictionary<string, Transform>();
            foreach (JObject e in (JArray)mb.Map["props"])
            {
                string room = (string)e["room"] ?? "misc";
                Transform rp;
                if (!rooms.TryGetValue(room, out rp))
                {
                    rp = new GameObject(room).transform;
                    rp.SetParent(parent);
                    rooms[room] = rp;
                }
                MapProp(rp, e, mb);
            }
        }

        static GameObject MapProp(Transform parent, JObject e, MapBuild mb)
        {
            string m = (string)e["m"];
            string id = (string)e["id"];
            string role = (string)e["role"];
            int layer = Layers.Furniture;
            string layerName = (string)e["layer"];
            if (layerName == "Partitions") layer = Layers.Partitions;
            else if (layerName == "Walls") layer = Layers.Walls;
            var go = Prop(parent, m, V3(e["p"]), (float)e["r"], id ?? m, Flag(e, "c", true), layer);
            if (e["scale"] != null) go.transform.localScale = V3(e["scale"]);
            if (e["tint"] is JObject tint) Tint(go, tint);
            if (!Flag(e, "s", true)) mb.NonStatic.Add(go);
            foreach (var glowName in new[] { "Glow", "TvScreen" })
            {
                var g = FindDeep(go.transform, glowName);
                if (g != null && g.GetComponent<Renderer>() != null) mb.LampGlows.Add(g.GetComponent<Renderer>());
            }
            if (e["anchors"] is JArray anchors)
                foreach (var a in anchors) AnchorAt(go.transform, (string)a, mb.Layout);
            var fonts = mb.Fonts;
            switch (role)
            {
                case "coffee":
                {
                    var sv = go.AddComponent<StationVisual>();
                    sv.StationId = id;
                    sv.Steam = Steam(go.transform, new Vector3(0, 0.33f, -0.24f));
                    mb.Stations.Add(sv);
                    break;
                }
                case "printer":
                {
                    var sv = go.AddComponent<StationVisual>();
                    sv.StationId = id;
                    sv.Light = RendererOf(go.transform, "StatusLight");
                    sv.LightOn = Mat("M_GreenOn"); sv.LightOff = Mat("M_GreenOff");
                    mb.Stations.Add(sv);
                    break;
                }
                case "phone":
                {
                    var sv = go.AddComponent<StationVisual>();
                    sv.StationId = id;
                    sv.ShakeTarget = FindDeep(go.transform, "Handset");
                    sv.Light = RendererOf(go.transform, "RingLight");
                    sv.LightOn = Mat("M_LightOn"); sv.LightOff = Mat("M_LightOff");
                    mb.Stations.Add(sv);
                    mb.NonStatic.Add(go);
                    break;
                }
                case "delivery":
                    Label3D(go.transform, "Sign", new Vector3(0, 1.4f, 0.17f), Vector3.zero, new Vector2(0.5f, 0.15f), fonts.TmpTitle, Color.white, "TESLİM");
                    break;
                case "closet":
                    Label3D(go.transform, "Sign", new Vector3(0, 1.78f, -0.255f), Vector3.zero, new Vector2(0.3f, 0.08f), fonts.Tmp, Hex("#2E2257"), "TEMİZLİK");
                    break;
                case "lostfound":
                    Label3D(go.transform, "Label", new Vector3(0, 0.17f, -0.218f), Vector3.zero, new Vector2(0.34f, 0.09f), fonts.Tmp, Hex("#2E2257"), "KAYIP EŞYA");
                    break;
                case "junk":
                    Label3D(go.transform, "Label", new Vector3(0, 0.3f, -0.268f), Vector3.zero, new Vector2(0.48f, 0.14f), fonts.Tmp, Hex("#2E2257"), "GEREKSİZ\nBELGELER");
                    break;
                case "noticeboard":
                    Label3D(go.transform, "Title", new Vector3(0, 1.98f, -0.03f), Vector3.zero, new Vector2(1.1f, 0.18f), fonts.TmpTitle, Hex("#24315E"), "DUYURULAR");
                    break;
                case "router":
                {
                    var rv = go.AddComponent<RouterVisual>();
                    rv.LightA = RendererOf(go.transform, "RouterLight");
                    rv.LightB = RendererOf(go.transform, "RouterLight2");
                    mb.Devices.Add(rv);
                    Label3D(go.transform, "Sign", new Vector3(0, 1.17f, -0.26f), Vector3.zero, new Vector2(0.28f, 0.1f), fonts.Tmp, Hex("#2E2257"), "MODEM");
                    break;
                }
                case "fusebox":
                {
                    var fv = go.AddComponent<FuseBoxVisual>();
                    fv.Lever = FindDeep(go.transform, "FuseLever");
                    fv.Light = RendererOf(go.transform, "FuseLight");
                    if (fv.Lever != null) mb.NonStatic.Add(fv.Lever.gameObject);
                    mb.Devices.Add(fv);
                    break;
                }
                case "camera":
                {
                    var cv = go.AddComponent<CctvVisual>();
                    cv.CameraId = id;
                    cv.RecLight = RendererOf(go.transform, "RecLight");
                    cv.Phase = 0.37f * mb.Cameras++;
                    mb.Devices.Add(cv);
                    break;
                }
                case "emergency":
                {
                    var lamp = RendererOf(go.transform, "EmLamp");
                    if (lamp != null) mb.EmergencyLamps.Add(lamp);
                    break;
                }
            }
            if (m == "ServerRack")
            {
                var rb = go.AddComponent<RackBlinker>();
                rb.Leds = RendererOf(go.transform, "Leds");
                rb.LedsB = RendererOf(go.transform, "LedsB");
                rb.Phase = 0.29f * mb.Racks;
                rb.Speed = 0.8f + 0.15f * (mb.Racks % 4);
                mb.Racks++;
                mb.Devices.Add(rb);
            }
            if (layer == Layers.Partitions && go.GetComponent<OccluderWall>() == null)
                go.AddComponent<OccluderWall>().Renderers = go.GetComponentsInChildren<Renderer>();
            return go;
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
            main.maxParticles = 40;
            var em = ps.emission; em.rateOverTime = 14f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 8f; shape.radius = 0.03f;
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 2f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Mat("M_Steam");
            ps.Stop();
            return ps;
        }

        // ================================================================== decals & signs

        static void BuildDecals(Transform parent, MapBuild mb)
        {
            var root = new GameObject("Decals").transform; root.SetParent(parent);
            int textured = 0, total = 0;
            foreach (JObject e in (JArray)mb.Map["decals"])
            {
                string id = (string)e["id"];
                var size = (JArray)e["size"];
                float w = (float)size[0], h = (float)size[1];
                float yaw = (float)e["r"];
                var rot = Quaternion.Euler(0, yaw, 0);
                var go = new GameObject("Decal_" + id);
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(V3(e["p"]) + rot * new Vector3(0, 0, -0.015f), rot);
                Cube(go.transform, "Frame", new Vector3(0, 0, 0.007f), new Vector3(w + 0.05f, h + 0.05f, 0.012f), Mat("M_DarkWood"));
                bool tex;
                var q = Quad(go.transform, "Surface", new Vector3(0, 0, -0.0015f), Vector3.zero, new Vector3(w, h, 1), DecalMaterial(id, out tex));
                var spot = go.AddComponent<DecalSpot>();
                spot.Id = id;
                spot.Surface = q.GetComponent<Renderer>();
                foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
                if (tex) textured++;
                total++;
            }
            Log("Decal spots: " + total + " (" + textured + " with art from " + DecalSourceDir + ", others placeholder)");
        }

        /// <summary>Texture from ../Art/UI/decals/&lt;id&gt;.png when the 2D art run produced it, else the placeholder.</summary>
        static Material DecalMaterial(string id, out bool textured)
        {
            textured = false;
            string src = Path.GetFullPath(Path.Combine(DecalSourceDir, id + ".png"));
            if (!File.Exists(src)) return Mat("M_PosterPlaceholder");
            Directory.CreateDirectory(DecalDir);
            string dst = DecalDir + "/" + id + ".png";
            if (!File.Exists(dst) || File.GetLastWriteTimeUtc(dst) < File.GetLastWriteTimeUtc(src))
            {
                File.Copy(src, dst, true);
                AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
            }
            var imp = AssetImporter.GetAtPath(dst) as TextureImporter;
            if (imp != null)
            {
                imp.textureType = TextureImporterType.Default;
                imp.sRGBTexture = true;
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = true;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.maxTextureSize = 1024;
                imp.textureCompression = TextureImporterCompression.Compressed;
                imp.SaveAndReimport();
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
            if (texture == null) return Mat("M_PosterPlaceholder");
            var m = GetOrCreate("M_Decal_" + id, Shader.Find("Universal Render Pipeline/Lit"));
            m.SetTexture("_BaseMap", texture);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.15f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.4f);
            m.EnableKeyword("_ALPHATEST_ON");
            SetOpaque(m);
            m.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(m);
            textured = true;
            return m;
        }

        static void BuildSigns(Transform parent, MapBuild mb)
        {
            var root = new GameObject("Signs").transform; root.SetParent(parent);
            foreach (JObject e in (JArray)mb.Map["labels"])
            {
                string text = (string)e["text"];
                var size = (JArray)e["size"];
                var sz = new Vector2((float)size[0], (float)size[1]);
                var rot = Quaternion.Euler(0, (float)e["r"], 0);
                var go = new GameObject("Sign_" + text);
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(V3(e["p"]) + rot * new Vector3(0, 0, -0.02f), rot);
                string plate = (string)e["plate"];
                if (!string.IsNullOrEmpty(plate) && Mat(plate) != null)
                {
                    var c = Cube(go.transform, "Plate", new Vector3(0, 0, 0.012f), new Vector3(sz.x + 0.14f, sz.y + 0.07f, 0.02f), Mat(plate));
                    c.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                }
                var font = (string)e["font"] == "title" ? mb.Fonts.TmpTitle : mb.Fonts.Tmp;
                Label3D(go.transform, "Text", Vector3.zero, Vector3.zero, sz, font, Hex((string)e["color"]), text);
            }
        }

        // ================================================================== lights

        static void BuildLights(Transform parent, MapBuild mb, Light sun)
        {
            var root = new GameObject("Lighting").transform; root.SetParent(parent);
            foreach (JObject e in (JArray)mb.Map["lights"])
            {
                string group = (string)e["group"] ?? "room";
                var go = new GameObject("Light_" + ((string)e["room"] ?? "").Replace("room.", "") + "_" + group);
                go.transform.SetParent(root, false);
                go.transform.position = V3(e["p"]);
                var l = go.AddComponent<Light>();
                l.type = (string)e["t"] == "spot" ? LightType.Spot : LightType.Point;
                l.color = Hex((string)e["color"]);
                l.intensity = (float)e["i"];
                l.range = (float)e["range"];
                l.shadows = LightShadows.None;
                l.lightmapBakeType = LightmapBakeType.Realtime;
                l.renderMode = LightRenderMode.Auto;
                if (l.type == LightType.Spot)
                {
                    l.spotAngle = (float)e["angle"];
                    go.transform.rotation = Quaternion.Euler((float)e["pitch"], (float)e["yaw"], 0);
                }
                if (group == "emergency") { l.enabled = false; mb.EmergencyLights.Add(l); }
                else mb.RoomLights.Add(l);
            }
            var ol = root.gameObject.AddComponent<OfficeLighting>();
            ol.Sun = sun;
            ol.RoomLights = mb.RoomLights.ToArray();
            ol.EmergencyLights = mb.EmergencyLights.ToArray();
            ol.LampGlows = mb.LampGlows.ToArray();
            ol.LampOff = Mat("M_LEDOff");
            ol.EmergencyLamps = mb.EmergencyLamps.ToArray();
            ol.EmergencyOn = Mat("M_LEDRed");
            ol.EmergencyOff = Mat("M_LEDOff");
            ol.AmbientSky = RenderSettings.ambientSkyColor;
            ol.AmbientEquator = RenderSettings.ambientEquatorColor;
            ol.AmbientGround = RenderSettings.ambientGroundColor;
            Log("Lights: " + mb.RoomLights.Count + " room (no shadows) + " + mb.EmergencyLights.Count + " emergency (off until a power cut) + sun (shadows)");
        }

        static System.Tuple<Light[], Transform> BuildAlarm(Transform parent)
        {
            // Rotating beacon on top of the open office's north wall (above the meeting room door).
            var lights = new List<Light>();
            var pos = new Vector3(-4.5f, 2.8f, 4.0f);
            var rotor = new GameObject("AlarmRotor").transform;
            rotor.SetParent(parent);
            rotor.position = pos + new Vector3(0, 0.1f, 0);
            Prop(parent, "AlarmLight", pos, 0, "AlarmLight", false);
            for (int i = 0; i < 2; i++)
            {
                var l = new GameObject("AlarmSpot" + i).AddComponent<Light>();
                l.transform.SetParent(rotor, false);
                l.transform.localRotation = Quaternion.Euler(30, i * 180, 0);
                l.type = LightType.Spot;
                l.color = new Color(1f, 0.15f, 0.1f);
                l.intensity = 25f;
                l.range = 24f;
                l.spotAngle = 70f;
                l.shadows = LightShadows.None;
                l.enabled = false;
                lights.Add(l);
            }
            return System.Tuple.Create(lights.ToArray(), rotor);
        }

        // ================================================================== performance flags

        static readonly string[] MovingParts = { "Drawer", "Handset", "FuseLever", "Steam", "Padlock" };

        static void MarkStatic(Transform root, MapBuild mb)
        {
            var flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
            int stat = 0, shadowOff = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var go = t.gameObject;
                if (go.GetComponent<Light>() != null) continue;
                GameObjectUtility.SetStaticEditorFlags(go, flags);
                stat++;
            }
            // Clear static on everything that moves at runtime (drawers, chairs, handset, fuse lever, steam).
            foreach (var go in mb.NonStatic)
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (System.Array.IndexOf(MovingParts, t.name) >= 0)
                    foreach (var c in t.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(c.gameObject, 0);
            // Desk clutter and wall decor do not need to cast sun shadows.
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (r.shadowCastingMode == ShadowCastingMode.On && r.bounds.size.magnitude < 0.45f) { r.shadowCastingMode = ShadowCastingMode.Off; shadowOff++; }
            Log("Static flags on " + stat + " objects; " + mb.NonStatic.Count + " dynamic roots; " + shadowOff + " small renderers without shadows");
        }

        // ================================================================== navigation

        /// <summary>Humanoid agent sized like the bean avatars (CharacterController r 0.33, h 1.2, step 0.25).</summary>
        static void ConfigureNavAgent()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset");
            if (assets == null || assets.Length == 0) { Log("NavMeshAreas.asset not found; default agent"); return; }
            var so = new SerializedObject(assets[0]);
            var arr = so.FindProperty("m_Settings");
            if (arr == null || arr.arraySize == 0) return;
            var e = arr.GetArrayElementAtIndex(0);
            SetRel(e, "agentRadius", 0.35f);
            SetRel(e, "agentHeight", 1.3f);
            SetRel(e, "agentClimb", 0.3f);
            SetRel(e, "minRegionArea", 1f);
            so.ApplyModifiedPropertiesWithoutUndo();
            var s = NavMesh.GetSettingsByID(0);
            Log("Nav agent radius=" + s.agentRadius + " height=" + s.agentHeight + " climb=" + s.agentClimb);
        }

        static void SetRel(SerializedProperty p, string name, float v)
        {
            var c = p.FindPropertyRelative(name);
            if (c != null) c.floatValue = v;
        }

        /// <summary>Every desk seat, station, slot, meeting seat and manager stop must be reachable on the NavMesh from the
        /// open office (bots walk there; the manager's stops must lie on walkable floor).</summary>
        static void CheckReachability(OfficeLayout layout)
        {
            var start = new Vector3(-4.5f, 0f, -0.6f);
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(start, out hit, 2f, NavMesh.AllAreas)) { Log("NAV: no NavMesh near the office centre!"); return; }
            start = hit.position;
            var path = new NavMeshPath();
            int ok = 0, bad = 0;
            System.Action<string, Vector3, float> check = (name, p, radius) =>
            {
                p.y = 0;
                NavMeshHit h;
                if (!NavMesh.SamplePosition(p, out h, radius, NavMesh.AllAreas)) { Log("NAV UNREACHABLE (no mesh within " + radius + " m) " + name + " " + p); bad++; return; }
                if (!NavMesh.CalculatePath(start, h.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                { Log("NAV UNREACHABLE (no path) " + name + " " + p); bad++; return; }
                ok++;
            };
            foreach (var d in layout.Desks) check(d.Id + ".seat", V(d.SeatPosition), 1.0f);
            foreach (var s in layout.Stations) check(s.Id, V(s.Position), 1.2f);
            foreach (var s in layout.Slots) check(s.Id, V(s.Position), 1.6f);
            for (int i = 0; i < layout.MeetingSeats.Count; i++) check("meeting.seat" + i, V(layout.MeetingSeats[i].Position), 1.0f);
            for (int i = 0; i < layout.ManagerRoute.Count; i++) check("manager.stop" + i, V(layout.ManagerRoute[i].Position), 0.4f);
            check("manager.home", V(layout.ManagerHome), 0.6f);
            Log("NAV reachability: " + ok + " ok, " + bad + " problems");
        }

        // ================================================================== checks

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
            foreach (var c in layout.Cameras)
                if (GameObject.Find(c.Id) == null) Log("CAMERA PROP MISSING " + c.Id);
        }

        static void LogMapStats()
        {
            int renderers = 0, tris = 0, lights = 0;
            var mats = new HashSet<Material>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
            {
                renderers++;
                foreach (var m in r.sharedMaterials) if (m != null) mats.Add(m);
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null && r.gameObject.activeInHierarchy)
                    for (int i = 0; i < mf.sharedMesh.subMeshCount; i++) tris += (int)mf.sharedMesh.GetIndexCount(i) / 3;
            }
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include)) lights++;
            Log("MAP STATS renderers=" + renderers + " activeTris=" + tris + " materials=" + mats.Count + " lights=" + lights);
        }
    }
}
