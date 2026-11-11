using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BirimizKovulacak.Game;
using Newtonsoft.Json.Linq;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace BirimizKovulacak.EditorTools
{
    /// <summary>
    /// v3 rooms scene (world track): the player's private office + the meeting room, first-person camera, lighting,
    /// reflection probes and post, seated character prefabs, the bootstrap/network/UI objects. Sources:
    ///  - Art/Layout/rooms_v3.json (placements, anchors, lights, decals; written by Art/Scripts/build_rooms.py),
    ///  - Models/Rooms/*.fbx (shells, props, skyline, first-person hands), Art/Textures/Rooms/*.png (room_textures.py),
    ///  - Art/palette_rooms.json (room materials), Art/UI/decals(_en), portraits, logos (posters, photo, slides).
    /// Runtime: Scripts/Rooms (RoomsPresenter implements IRoomPresenter). No NavMesh, no static batching (the SRP
    /// Batcher handles the ~150 renderers; batching would duplicate meshes in memory).
    /// </summary>
    public static partial class ProjectBuilder
    {
        public const string RoomsScenePath = Root + "/Scenes/Rooms.unity";
        const string RoomsTexDir = Root + "/Art/Textures/Rooms";
        const string RoomsGenDir = GenDir + "/Rooms";
        const string RoomsPrefabDir = PrefabDir + "/Rooms";
        const string RoomsLayoutJson = "../Art/Layout/rooms_v3.json";
        const string RoomsPaletteJson = "../Art/palette_rooms.json";
        static readonly string[] RoomPlayerHex = { "#4A90E2", "#F5C842", "#F28AB2", "#6CC56A", "#F4853C", "#A872D0" };
        static readonly string[] RoomSkins = { "M_BeanBlue", "M_BeanYellow", "M_BeanPink", "M_BeanGreen", "M_BeanOrange", "M_BeanPurple" };

        // ================================================================== materials (before model import)

        /// <summary>Room materials from Art/palette_rooms.json + runtime materials. Called by BuildAll before ConfigureModelImports
        /// so the Rooms FBX files remap to them by name.</summary>
        static void CreateRoomMaterials()
        {
            Directory.CreateDirectory(RoomsGenDir);
            Directory.CreateDirectory(RoomsPrefabDir);
            Directory.CreateDirectory(RoomsTexDir);
            AssetDatabase.Refresh();
            ConfigureRoomTextures();
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var json = JObject.Parse(File.ReadAllText(Path.GetFullPath(RoomsPaletteJson)));
            int n = 0;
            foreach (var prop in (JObject)json["materials"])
            {
                var spec = (JObject)prop.Value;
                var col = Hex((string)spec["color"]);
                float alpha = (float?)spec["alpha"] ?? 1f;
                if ((bool?)spec["unlit"] == true)
                {
                    var m = GetOrCreate(prop.Key, unlit);
                    col.a = alpha;
                    m.SetColor("_BaseColor", col);
                    if (alpha < 0.999f) SetAdditive(m); else SetOpaque(m);
                    EditorUtility.SetDirty(m);
                }
                else
                {
                    CreateLit(prop.Key, lit, col, (float?)spec["smooth"] ?? 0.3f, (float?)spec["metal"] ?? 0f, (float?)spec["emit"] ?? 0f, alpha);
                    var m = Mat(prop.Key);
                    string tex = (string)spec["tex"];
                    if (!string.IsNullOrEmpty(tex))
                    {
                        var t = AssetDatabase.LoadAssetAtPath<Texture2D>(RoomsTexDir + "/" + tex + ".png");
                        float tile = (float?)spec["tile"] ?? 1f;
                        m.SetTexture("_BaseMap", t);
                        m.SetTextureScale("_BaseMap", new Vector2(tile, tile));
                    }
                    else m.SetTexture("_BaseMap", null);
                    EditorUtility.SetDirty(m);
                }
                n++;
            }
            // facades: lit windows at dusk (RoomLighting drives _EmissionColor)
            var facadeLit = AssetDatabase.LoadAssetAtPath<Texture2D>(RoomsTexDir + "/FacadeLit.png");
            foreach (var f in new[] { "M_RmFacade", "M_RmFacadeB", "M_RmFacadeC" })
            {
                var m = Mat(f);
                if (m == null) continue;
                m.EnableKeyword("_EMISSION");
                m.SetTexture("_EmissionMap", facadeLit);
                m.SetTextureScale("_EmissionMap", m.GetTextureScale("_BaseMap"));
                m.SetColor("_EmissionColor", Color.black);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                EditorUtility.SetDirty(m);
            }
            // monitor states (base + emission map)
            foreach (var s in new[] { "Desktop", "Alert", "Offline", "Coffee" })
            {
                var m = GetOrCreate("M_RmScreen" + s, lit);
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(RoomsTexDir + "/Screen" + s + ".png");
                m.SetTexture("_BaseMap", t);
                m.SetColor("_BaseColor", new Color(0.32f, 0.32f, 0.32f));
                m.SetTexture("_EmissionMap", t);
                m.SetColor("_EmissionColor", Color.white * 0.62f);
                m.EnableKeyword("_EMISSION");
                m.SetFloat("_Smoothness", 0.88f);
                m.SetFloat("_Metallic", 0f);
                SetOpaque(m);
                EditorUtility.SetDirty(m);
            }
            for (int i = 0; i < 6; i++)
            {
                var c = Hex(RoomPlayerHex[i]);
                CreateLit("M_RmSeatColor" + i, lit, new Color(c.r * 0.82f, c.g * 0.82f, c.b * 0.82f), 0.42f, 0f, 0f, 1f);
            }
            // hover = gold outline (inverted hull: back faces of a slightly grown copy), like the beans' hover outline
            var hover = GetOrCreate("M_RmHover", unlit);
            hover.SetColor("_BaseColor", Hex("#FFC93C"));
            SetOpaque(hover);
            hover.SetFloat("_Cull", 1f); // render front faces culled
            hover.SetShaderPassEnabled("ShadowCaster", false);
            EditorUtility.SetDirty(hover);
            // passer-by behind the frosted door glass: alpha-tested dark shape (the glass in front softens it)
            var sil = GetOrCreate("M_RmSilhouette", lit);
            sil.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(RoomsTexDir + "/Silhouette.png"));
            sil.SetColor("_BaseColor", new Color(0.16f, 0.17f, 0.24f, 1f));
            sil.SetFloat("_Smoothness", 0f);
            sil.SetFloat("_Metallic", 0f);
            sil.DisableKeyword("_EMISSION");
            sil.SetColor("_EmissionColor", Color.black);
            SetOpaque(sil);
            sil.SetFloat("_AlphaClip", 1f);
            sil.SetFloat("_Cutoff", 0.45f);
            sil.EnableKeyword("_ALPHATEST_ON");
            sil.SetFloat("_Cull", 0f);
            sil.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(sil);
            // v5: the meeting lamp over the office door (dark red glass / glowing amber-red)
            CreateLit("M_RmDoorLampOff", lit, Hex("#6B2D26"), 0.75f, 0f, 0f, 1f);
            CreateLit("M_RmDoorLampOn", lit, Hex("#FF6A48"), 0.6f, 0f, 1.25f, 1f);
            var cord = GetOrCreate("M_RmCord", unlit);
            cord.SetColor("_BaseColor", Hex("#E2DACB"));
            SetOpaque(cord);
            EditorUtility.SetDirty(cord);
            var canvas = GetOrCreate("M_RmCanvasUnlit", unlit);
            canvas.SetColor("_BaseColor", new Color(0.93f, 0.93f, 0.93f));
            SetOpaque(canvas);
            EditorUtility.SetDirty(canvas);
            var beam = GetOrCreate("M_RmBeamFx", unlit);
            beam.SetTexture("_BaseMap", RoomGradientTexture());
            beam.SetColor("_BaseColor", new Color(1f, 0.95f, 0.85f, 0.16f));
            SetAdditive(beam);
            EditorUtility.SetDirty(beam);
            var ohover = GetOrCreate("M_OutlineHover", unlit);
            ohover.SetColor("_BaseColor", Hex("#FFC93C"));
            SetOpaque(ohover);
            ohover.SetShaderPassEnabled("ShadowCaster", false);
            EditorUtility.SetDirty(ohover);
            var sky = GetOrCreate("M_RmSky", Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_SunDisk", 2f);
            sky.SetFloat("_SunSize", 0.045f);
            sky.SetFloat("_SunSizeConvergence", 6f);
            sky.SetFloat("_AtmosphereThickness", 0.85f);
            sky.SetColor("_SkyTint", Hex("#7F8FA8"));
            sky.SetColor("_GroundColor", Hex("#5B5650"));
            sky.SetFloat("_Exposure", 1.25f);
            EditorUtility.SetDirty(sky);
            RoomImageMaterials(lit, unlit);
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MatDir }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m != null && m.name.StartsWith("M_Rm") && !m.enableInstancing) { m.enableInstancing = true; EditorUtility.SetDirty(m); }
            }
            AssetDatabase.SaveAssets();
            Log("Room materials: " + n + " from palette_rooms.json + runtime/screen/decal materials");
        }

        static void SetAdditive(Material m)
        {
            SetTransparent(m);
            m.SetFloat("_Blend", 2f); // URP: 0 alpha, 1 premultiply, 2 additive, 3 multiply
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.One);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
        }

        static void ImportRoomTex(string path, bool repeat, int maxSize, bool alpha, bool mips = true)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;
            bool dirty = imp.textureType != TextureImporterType.Default || imp.maxTextureSize != maxSize || imp.mipmapEnabled != mips
                         || imp.wrapMode != (repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp) || imp.alphaIsTransparency != alpha || !imp.sRGBTexture
                         || imp.anisoLevel != (repeat ? 4 : 1);
            if (!dirty) return;
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = true;
            imp.mipmapEnabled = mips;
            imp.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            imp.anisoLevel = repeat ? 4 : 1;
            imp.maxTextureSize = maxSize;
            imp.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            imp.alphaIsTransparency = alpha;
            imp.textureCompression = TextureImporterCompression.Compressed;
            imp.isReadable = false;
            imp.SaveAndReimport();
        }

        static void ConfigureRoomTextures()
        {
            foreach (var t in new[] { "Wood", "Planks", "Carpet", "Plaster", "Facade", "FacadeLit" })
                ImportRoomTex(RoomsTexDir + "/" + t + ".png", true, t == "Planks" || t == "Wood" ? 512 : 256, false);
            foreach (var t in new[] { "ScreenDesktop", "ScreenAlert", "ScreenOffline", "ScreenCoffee" })
                ImportRoomTex(RoomsTexDir + "/" + t + ".png", false, 512, false);
            ImportRoomTex(RoomsTexDir + "/Silhouette.png", false, 128, true);
        }

        /// <summary>Copies an Art/UI image into Art/Textures/Rooms/&lt;sub&gt; (changed files only) and imports it.</summary>
        static Texture2D RoomImage(string srcRel, string sub, int maxSize, bool alpha = false)
        {
            string src = Path.GetFullPath(Path.Combine("..", srcRel));
            if (!File.Exists(src)) return null;
            string dir = RoomsTexDir + "/" + sub;
            Directory.CreateDirectory(dir);
            string dst = dir + "/" + Path.GetFileName(src);
            if (!File.Exists(dst) || File.GetLastWriteTimeUtc(dst) < File.GetLastWriteTimeUtc(src) || new FileInfo(dst).Length != new FileInfo(src).Length)
            {
                File.Copy(src, dst, true);
                AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
            }
            ImportRoomTex(dst, false, maxSize, alpha);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
        }

        static Material ImageMat(string name, Texture2D tex, Shader shader, float smooth = 0.15f)
        {
            var m = GetOrCreate(name, shader);
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_EmissionColor")) { m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); }
            SetOpaque(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        static readonly string[] RoomDecalIds = { "poster_kahve", "poster_sadakat", "poster_takim_ruhu", "poster_verimlilik", "poster_motivation",
            "poster_teamwork", "poster_safety", "poster_mission", "poster_kpi", "portrait_manager", "diploma", "logo_company" };

        static void RoomImageMaterials(Shader lit, Shader unlit)
        {
            foreach (var id in RoomDecalIds)
            {
                var tr = RoomImage("Art/UI/decals/" + id + ".png", "Decals", 512);
                if (tr != null) ImageMat("M_RmDecal_" + id, tr, lit);
                var en = RoomImage("Art/UI/decals_en/" + id + ".png", "DecalsEn", 512);
                if (en != null) ImageMat("M_RmDecalEn_" + id, en, lit);
            }
            var cork = RoomImage("Art/UI/decals/corkboard_notes.png", "Decals", 1024);
            if (cork != null) ImageMat("M_RmCorkNotes", cork, lit, 0.05f);
            var wb = RoomImage("Art/UI/decals/whiteboard_doodles.png", "Decals", 1024);
            if (wb != null) ImageMat("M_RmWhiteboardDoodles", wb, lit, 0.75f);
            for (int i = 0; i < 6; i++)
            {
                var p = RoomImage("Art/UI/portraits/player_" + i + ".png", "Portraits", 256);
                if (p != null) ImageMat("M_RmPhoto" + i, p, lit, 0.5f);
            }
            RoomSlide("Art/UI/logo/logo.png", "SlideTr");
            RoomSlide("Art/UI/logo_en/logo_en.png", "SlideEn");
        }

        /// <summary>Default projector slide: the company logo on a cream slide with a green header band.</summary>
        static Texture2D RoomSlide(string logoRel, string name)
        {
            string dst = RoomsTexDir + "/" + name + ".png";
            string src = Path.GetFullPath(Path.Combine("..", logoRel));
            if (File.Exists(src) && (!File.Exists(dst) || File.GetLastWriteTimeUtc(dst) < File.GetLastWriteTimeUtc(src)))
            {
                var logo = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                logo.LoadImage(File.ReadAllBytes(src));
                const int W = 1024, H = 576;
                var px = new Color32[W * H];
                var bg = new Color32(244, 238, 226, 255);
                var band = new Color32(47, 107, 74, 255);
                var gold = new Color32(201, 154, 63, 255);
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        Color32 c = bg;
                        if (y > H - 64) c = band;
                        else if (y > H - 70) c = gold;
                        else if (y < 26) c = band;
                        px[y * W + x] = c;
                    }
                float scale = Mathf.Min(W * 0.78f / logo.width, H * 0.62f / logo.height);
                int lw = Mathf.RoundToInt(logo.width * scale), lh = Mathf.RoundToInt(logo.height * scale);
                int ox = (W - lw) / 2, oy = (H - 64 - lh) / 2 + 14;
                for (int y = 0; y < lh; y++)
                    for (int x = 0; x < lw; x++)
                    {
                        Color s = logo.GetPixelBilinear((x + 0.5f) / lw, (y + 0.5f) / lh);
                        int i = (oy + y) * W + ox + x;
                        if (i < 0 || i >= px.Length) continue;
                        Color d = px[i];
                        px[i] = Color.Lerp(d, new Color(s.r, s.g, s.b, 1f), s.a);
                    }
                var outTex = new Texture2D(W, H, TextureFormat.RGBA32, false);
                outTex.SetPixels32(px);
                outTex.Apply();
                File.WriteAllBytes(dst, outTex.EncodeToPNG());
                Object.DestroyImmediate(logo);
                Object.DestroyImmediate(outTex);
                AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
            }
            ImportRoomTex(dst, false, 1024, false);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
        }

        /// <summary>Soft gradient for light beams (alpha falls off along V and toward the U edges).</summary>
        static Texture2D RoomGradientTexture()
        {
            string dst = RoomsTexDir + "/BeamGradient.png";
            if (!File.Exists(dst))
            {
                const int W = 64, H = 64;
                var t = new Texture2D(W, H, TextureFormat.RGBA32, false);
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        float v = y / (H - 1f);
                        float u = Mathf.Abs(x / (W - 1f) - 0.5f) * 2f;
                        float a = Mathf.Pow(1f - v, 1.4f) * (1f - u * u * 0.6f);
                        t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                t.Apply();
                File.WriteAllBytes(dst, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
            }
            ImportRoomTex(dst, false, 64, true, false);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
        }

        // ================================================================== characters for the meeting

        static BeanFace ParseFace(string f)
        {
            BeanFace e;
            return Enum.TryParse(f ?? "Neutral", out e) ? e : BeanFace.Neutral;
        }

        /// <summary>Seat_&lt;look&gt; (players, keeps Prop_Box for the firing) and Boss_&lt;id&gt; (managers) prefabs.</summary>
        static void BuildRoomCharacters(out SeatedBean[] players, out string[] bossIds, out SeatedBean[] bosses)
        {
            var emp = Model("Characters", "Employee");
            var mgr = Model("Characters", "Manager");
            var empCtrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CharGenDir + "/EmployeeAnimator.controller");
            var mgrCtrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CharGenDir + "/ManagerAnimator.controller");
            var pl = (JArray)CharLooks["players"];
            var ml = (JArray)CharLooks["managers"];
            players = new SeatedBean[pl.Count];
            for (int i = 0; i < pl.Count; i++)
                players[i] = BuildBeanPrefab(emp, empCtrl, (JObject)pl[i], (JObject)CharLooks["playerGroups"], (JArray)CharLooks["playerAccessories"], "Seat_" + (string)pl[i]["id"], false);
            bossIds = new string[ml.Count];
            bosses = new SeatedBean[ml.Count];
            for (int i = 0; i < ml.Count; i++)
            {
                bossIds[i] = (string)ml[i]["id"];
                bosses[i] = BuildBeanPrefab(mgr, mgrCtrl, (JObject)ml[i], (JObject)CharLooks["managerGroups"], (JArray)CharLooks["managerAccessories"], "Boss_" + bossIds[i], true);
            }
            var clips = CharClips(emp).Keys.Where(k => k.StartsWith("Sit")).OrderBy(k => k);
            Log("Room characters: " + players.Length + " seated looks, " + bosses.Length + " managers; seated clips: " + string.Join(",", clips));
        }

        static SeatedBean BuildBeanPrefab(GameObject model, RuntimeAnimatorController ctrl, JObject look, JObject groups, JArray allAcc, string name, bool manager)
        {
            var root = new GameObject(name);
            var modelRoot = new GameObject("ModelRoot").transform;
            modelRoot.SetParent(root.transform, false);
            var inst = Inst(model, modelRoot, Vector3.zero, 0, "Model");
            if (PrefabUtility.IsPartOfPrefabInstance(inst)) PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var anim = SetupCharacterAnimator(inst, ctrl);
            SetupCharacterRenderers(inst, manager ? 4.0f : 3.4f);
            var keep = new HashSet<string>(((JArray)look["accessories"]).Select(a => (string)a));
            var accNames = new HashSet<string>(allAcc.Select(a => (string)a));
            GameObject box = null;
            foreach (var r in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                string n = r.name;
                if (n == "Prop_Box" && !manager) { box = r.gameObject; box.SetActive(false); continue; }
                bool optional = n.StartsWith("Acc_") || n.StartsWith("Prop_") || accNames.Contains(n);
                if (optional && !keep.Contains(n)) { Object.DestroyImmediate(r.gameObject); continue; }
                r.gameObject.SetActive(true);
            }
            // material swaps (skin / tie / suit) by group membership
            var swaps = (JObject)look["swaps"];
            foreach (var g in groups)
            {
                string target = swaps != null ? (string)swaps[g.Key] : null;
                if (string.IsNullOrEmpty(target) || Mat(target) == null) continue;
                var members = new HashSet<Material>(((JArray)g.Value).Select(x => Mat((string)x)).Where(m => m != null));
                foreach (var r in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var mats = r.sharedMaterials;
                    bool changed = false;
                    for (int k = 0; k < mats.Length; k++) if (mats[k] != null && members.Contains(mats[k])) { mats[k] = Mat(target); changed = true; }
                    if (changed) r.sharedMaterials = mats;
                }
            }
            var sc = (JArray)look["scale"];
            if (sc != null && sc.Count == 3) modelRoot.localScale = new Vector3((float)sc[0], (float)sc[1], (float)sc[2]);
            modelRoot.localRotation = Quaternion.Euler((float?)look["tilt"] ?? 0f, 0f, 0f);
            var bean = root.AddComponent<SeatedBean>();
            bean.Anim = anim;
            var smrs = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            bean.Eyes = smrs.FirstOrDefault(r => r.name == "FaceEyes");
            bean.Mouth = smrs.FirstOrDefault(r => r.name == "FaceMouth");
            bean.EyeCells = Enumerable.Range(0, FaceCells).Select(i => Mat("M_FaceEyes_" + i)).ToArray();
            bean.MouthCells = Enumerable.Range(0, FaceCells).Select(i => Mat("M_FaceMouth_" + i)).ToArray();
            bean.Head = FindDeep(inst.transform, "Head");
            bean.ModelRoot = modelRoot;
            bean.Box = box;
            bean.IsManager = manager;
            bean.RestFace = ParseFace((string)look["face"]);
            bean.IdleState = manager ? ((string)look["idle"] ?? "Idle") : "Sit";
            bean.TalkState = manager ? "Talk" : "SitTalk";
            bean.Outline = Mat("M_Outline");
            bean.OutlineHover = Mat("M_OutlineHover");
            bean.OutlineRenderers = smrs.Where(r => r.sharedMaterials.Contains(bean.Outline)).Cast<Renderer>().ToArray();
            bean.LookId = (string)look["id"];
            if (!manager)
            {
                var cap = root.AddComponent<CapsuleCollider>();
                cap.center = new Vector3(0f, 1.05f, 0.02f);
                cap.radius = 0.34f;
                cap.height = 1.25f;
                var t = root.AddComponent<RoomTarget>();
                t.Bean = bean;
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, RoomsPrefabDir + "/" + name + ".prefab");
            Object.DestroyImmediate(root);
            return prefab.GetComponent<SeatedBean>();
        }

        // ================================================================== scene

        sealed class RoomBuild
        {
            public JObject Layout;
            public FontSet Fonts;
            public readonly List<Light> Warm = new List<Light>();
            public readonly List<Light> Fill = new List<Light>();
            public readonly List<ReflectionProbe> Probes = new List<ReflectionProbe>();
            public readonly List<RoomClock> Clocks = new List<RoomClock>();
            public readonly Dictionary<string, Light> Lights = new Dictionary<string, Light>();
            public int Targets, Overlays, Props;
        }

        static Vector3 RV(JToken t) { return new Vector3((float)t[0], (float)t[1], (float)t[2]); }

        static void BuildRoomsScene(PrefabSet prefabs, FontSet fonts, PipelineAssets pipeline)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string lpath = Path.GetFullPath(RoomsLayoutJson);
            if (!File.Exists(lpath)) throw new Exception("Rooms layout missing: " + lpath + " (run Blender Art/Scripts/build_rooms.py / Tools/build.ps1 -Art)");
            var rb = new RoomBuild { Layout = JObject.Parse(File.ReadAllText(lpath)), Fonts = fonts };
            ConfigureRoomsPipeline(pipeline);
            SeatedBean[] players, bosses;
            string[] bossIds;
            BuildRoomCharacters(out players, out bossIds, out bosses);

            // ---------------- environment
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex("#B9CDE6");
            RenderSettings.ambientEquatorColor = Hex("#D7C9B3");
            RenderSettings.ambientGroundColor = Hex("#6A5847");
            RenderSettings.skybox = Mat("M_RmSky");
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 30f;
            RenderSettings.fogEndDistance = 240f;
            RenderSettings.fogColor = Hex("#C3D4E8");
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.6f;
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Hex("#FFE6C2");
            sun.intensity = 2f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.82f;
            sun.shadowBias = 0.02f;
            sun.shadowNormalBias = 0.25f;
            sun.lightmapBakeType = LightmapBakeType.Realtime;
            sun.transform.rotation = Quaternion.LookRotation(new Vector3(0.8f, -0.4f, -0.45f), Vector3.up);
            var sunData = sun.gameObject.AddComponent<UniversalAdditionalLightData>();
            sunData.usePipelineSettings = false;
            RenderSettings.sun = sun;
            var vol = new GameObject("PostProcess").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = RoomsPostProfile();

            var world = new GameObject("Rooms");
            var officeRoot = new GameObject("Office");
            officeRoot.transform.SetParent(world.transform, false);
            var meetingRoot = new GameObject("Meeting");
            meetingRoot.transform.SetParent(world.transform, false);
            meetingRoot.transform.position = RV(rb.Layout["meeting"]["origin"]);
            var sky = Model("Rooms", "RmSkyline");
            if (sky != null)
            {
                var s = Inst(sky, world.transform, Vector3.zero, 0, "Skyline");
                foreach (var r in s.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
            }

            // ---------------- camera + presenter
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 400f;
            camGo.AddComponent<AudioListener>();
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camData.antialiasingQuality = AntialiasingQuality.Medium;
            var rig = camGo.AddComponent<PovCameraRig>();
            rig.Cam = cam;
            rig.Panel = RoomsPanelSettings();
            camGo.transform.SetPositionAndRotation(new Vector3(0f, 1.24f, 0.42f), Quaternion.Euler(13f, 0f, 0f));

            var presenter = world.AddComponent<RoomsPresenter>();
            presenter.Rig = rig;
            presenter.OfficeRoot = officeRoot;
            presenter.MeetingRoot = meetingRoot;
            presenter.HoverMat = Mat("M_RmHover");
            presenter.Office = BuildOffice(officeRoot.transform, (JObject)rb.Layout["office"], rb);
            presenter.Meeting = BuildMeeting(meetingRoot.transform, (JObject)rb.Layout["meeting"], rb, players, bossIds, bosses);
            presenter.Clocks = rb.Clocks.ToArray();
            var lighting = world.AddComponent<RoomLighting>();
            lighting.Sun = sun;
            lighting.Sky = Mat("M_RmSky");
            lighting.Facades = new[] { Mat("M_RmFacade"), Mat("M_RmFacadeB"), Mat("M_RmFacadeC") };
            lighting.Warm = rb.Warm.ToArray();
            lighting.WindowFill = rb.Fill.ToArray();
            lighting.Probes = rb.Probes.ToArray();
            lighting.Post = vol;
            presenter.Lighting = lighting;

            BuildRoomsBootstrap(prefabs, fonts);

            foreach (var r in world.GetComponentsInChildren<Renderer>(true))
                if (r.shadowCastingMode == ShadowCastingMode.On && r.bounds.size.magnitude < 0.06f) r.shadowCastingMode = ShadowCastingMode.Off;
            EditorSceneManager.SaveScene(scene, RoomsScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(RoomsScenePath, true) };
            int renderers = world.GetComponentsInChildren<Renderer>(true).Length;
            Log("ROOMS scene saved: " + RoomsScenePath + " props=" + rb.Props + " targets=" + rb.Targets + " hoverOverlays=" + rb.Overlays + " renderers=" + renderers
                + " lights=" + (rb.Lights.Count + 1) + " probes=" + rb.Probes.Count);
        }

        static PanelSettings RoomsPanelSettings()
        {
            return AssetDatabase.LoadAssetAtPath<PanelSettings>(GenDir + "/PanelSettings.asset");
        }

        static void ConfigureRoomsPipeline(PipelineAssets pipeline)
        {
            if (pipeline == null || pipeline.Asset == null) return;
            var so = new SerializedObject(pipeline.Asset);
            SetProp(so, "m_ShadowDistance", 14f);
            SetProp(so, "m_ShadowCascadeCount", 2);
            SetProp(so, "m_Cascade2Split", 0.3f);
            SetProp(so, "m_MainLightShadowmapResolution", 2048);
            SetProp(so, "m_SoftShadowsSupported", true);
            SetProp(so, "m_AdditionalLightShadowsSupported", false);
            SetProp(so, "m_MSAA", 1); // SMAA on the camera instead: smaller render targets (memory), same look at 1080p
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline.Asset);
        }

        static VolumeProfile RoomsPostProfile()
        {
            string path = RoomsGenDir + "/RoomsPost.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            var tone = profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.Neutral);
            var bloom = profile.Add<Bloom>(true); bloom.intensity.Override(0.42f); bloom.threshold.Override(1.0f); bloom.scatter.Override(0.62f); bloom.tint.Override(Hex("#FFE9CF"));
            var ca = profile.Add<ColorAdjustments>(true); ca.postExposure.Override(0.15f); ca.contrast.Override(10f); ca.saturation.Override(10f);
            var wb = profile.Add<WhiteBalance>(true); wb.temperature.Override(-4f); wb.tint.Override(0f);
            var lgg = profile.Add<LiftGammaGain>(true); lgg.lift.Override(new Vector4(0.98f, 1f, 1.04f, 0f));
            var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.22f); vig.smoothness.Override(0.5f); vig.color.Override(Hex("#1B1A2A"));
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        // ================================================================== helpers

        static GameObject RoomProp(Transform parent, string model, Vector3 pos, float yaw, string name, RoomBuild rb)
        {
            var m = Model("Rooms", model);
            if (m == null) { Log("MISSING room model " + model + " (run Tools/build.ps1 -Art)"); return new GameObject(name ?? model); }
            var go = Inst(m, parent, pos, yaw, name ?? model);
            if (PrefabUtility.IsPartOfPrefabInstance(go)) PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            rb.Props++;
            return go;
        }

        static Renderer RendererUnder(Transform root, string name)
        {
            var t = FindDeep(root, name);
            return t != null ? t.GetComponentInChildren<Renderer>(true) : null;
        }

        static Light RoomLight(Transform parent, JObject e, RoomBuild rb, string prefix)
        {
            var go = new GameObject("Light_" + (string)e["name"]);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = RV(e["p"]);
            var l = go.AddComponent<Light>();
            l.type = (string)e["t"] == "spot" ? LightType.Spot : LightType.Point;
            l.color = Hex((string)e["color"]);
            l.intensity = (float)e["i"];
            l.range = (float)e["range"];
            l.shadows = LightShadows.None;
            l.lightmapBakeType = LightmapBakeType.Realtime;
            if (l.type == LightType.Spot)
            {
                l.spotAngle = (float?)e["angle"] ?? 60f;
                l.innerSpotAngle = l.spotAngle * 0.45f;
                go.transform.localRotation = Quaternion.Euler((float?)e["pitch"] ?? 90f, (float?)e["yaw"] ?? 0f, 0f);
            }
            rb.Lights[prefix + (string)e["name"]] = l;
            return l;
        }

        static ReflectionProbe RoomProbe(Transform parent, Vector3 center, Vector3 size, RoomBuild rb)
        {
            var go = new GameObject("ReflectionProbe");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            var p = go.AddComponent<ReflectionProbe>();
            p.mode = ReflectionProbeMode.Realtime;
            p.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            p.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            p.boxProjection = true;
            p.size = size;
            p.resolution = 128;
            p.hdr = true;
            p.intensity = 0.9f;
            p.nearClipPlane = 0.05f;
            p.farClipPlane = 300f;
            rb.Probes.Add(p);
            return p;
        }

        /// <summary>Collider over the target's renderers (local space of <paramref name="host"/>) + hover overlays.</summary>
        static RoomTarget MakeTarget(GameObject host, IEnumerable<Renderer> renderers, DeskTarget desk, RoomBuild rb, float inflate = 0.01f)
        {
            var rs = renderers.Where(r => r != null && !(r is LineRenderer) && r.GetComponent<TMP_Text>() == null).ToList();
            var t = host.GetComponent<RoomTarget>() ?? host.AddComponent<RoomTarget>();
            t.Desk = desk;
            if (rs.Count > 0)
            {
                Vector3 mn = Vector3.one * float.MaxValue, mx = Vector3.one * float.MinValue;
                foreach (var r in rs)
                {
                    var b = r.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                        var l = host.transform.InverseTransformPoint(c);
                        mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l);
                    }
                }
                var bc = host.AddComponent<BoxCollider>();
                bc.center = (mn + mx) * 0.5f;
                bc.size = (mx - mn) + Vector3.one * inflate;
            }
            var overlays = new List<Renderer>();
            var hover = Mat("M_RmHover");
            foreach (var r in rs)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var go = new GameObject("Hover");
                go.transform.SetParent(r.transform, false);
                GrowAboutCenter(go.transform, mf.sharedMesh.bounds, r.transform.lossyScale);
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = Enumerable.Repeat(hover, mf.sharedMesh.subMeshCount).ToArray();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                mr.enabled = false;
                overlays.Add(mr);
            }
            t.Overlays = overlays.ToArray();
            rb.Targets++;
            rb.Overlays += overlays.Count;
            return t;
        }

        /// <summary>Scales an outline copy about the mesh's bounds centre so it sticks out ~7 mm on every side.</summary>
        static void GrowAboutCenter(Transform t, Bounds b, Vector3 lossy)
        {
            const float grow = 0.007f;
            Vector3 size = Vector3.Scale(b.size, lossy);
            var s = new Vector3((Mathf.Max(size.x, 0.004f) + 2 * grow) / Mathf.Max(size.x, 0.004f),
                                (Mathf.Max(size.y, 0.004f) + 2 * grow) / Mathf.Max(size.y, 0.004f),
                                (Mathf.Max(size.z, 0.004f) + 2 * grow) / Mathf.Max(size.z, 0.004f));
            t.localScale = s;
            t.localPosition = b.center - Vector3.Scale(b.center, s);
        }

        static Renderer RoomDecal(Transform parent, string name, Vector3 p, float yaw, Vector2 size, Material mat)
        {
            var go = new GameObject("Decal_" + name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = p;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var frame = Cube(go.transform, "Frame", new Vector3(0f, 0f, -0.007f), new Vector3(size.x + 0.05f, size.y + 0.05f, 0.014f), Mat("M_RmWalnutDark"));
            frame.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var q = Quad(go.transform, "Surface", new Vector3(0f, 0f, -0.0145f), Vector3.zero, new Vector3(size.x, size.y, 1f), mat);
            return q.GetComponent<Renderer>();
        }

        static Material DecalMat(string id, bool en)
        {
            var m = Mat((en ? "M_RmDecalEn_" : "M_RmDecal_") + id);
            return m != null ? m : (en ? null : Mat("M_PosterPlaceholder"));
        }

        static RoomClock MakeClock(GameObject clock, RoomBuild rb)
        {
            var c = clock.AddComponent<RoomClock>();
            c.Hour = FindDeep(clock.transform, "HourHand");
            c.Minute = FindDeep(clock.transform, "MinuteHand");
            rb.Clocks.Add(c);
            return c;
        }

        // ================================================================== office

        static OfficeRoom BuildOffice(Transform root, JObject o, RoomBuild rb)
        {
            var office = root.gameObject.AddComponent<OfficeRoom>();
            var shellModel = Model("Rooms", "RmOfficeShell");
            GameObject shell = shellModel != null ? Inst(shellModel, root, Vector3.zero, 0, "Shell") : new GameObject("Shell");
            foreach (var r in shell.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.name;
                if (n == "WindowGlass" || n == "DoorGlass") { r.shadowCastingMode = ShadowCastingMode.Off; }
                if (n == "Corridor") { r.shadowCastingMode = ShadowCastingMode.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off; }
            }
            var props = new Dictionary<string, GameObject>();
            foreach (JObject e in (JArray)o["props"])
                props[(string)e["id"]] = RoomProp(root, (string)e["model"], RV(e["p"]), (float)e["r"], (string)e["id"], rb);
            var a = (JObject)o["anchors"];
            var eye = new GameObject("Eye").transform;
            eye.SetParent(root, false);
            eye.localPosition = RV(a["eye"]);
            eye.localRotation = Quaternion.Euler(0f, (float)a["eyeYaw"], 0f);
            office.Eye = eye;
            office.EyePitch = (float)a["eyePitch"];
            office.YawLimit = (float)a["yawLimit"];
            office.PitchUp = (float)a["pitchUp"];
            office.PitchDown = (float)a["pitchDown"];
            GameObject g;
            // monitor
            if (props.TryGetValue("monitor", out g))
            {
                office.Screen = RendererUnder(g.transform, "Screen");
                office.PcLed = RendererUnder(g.transform, "PcLed");
                MakeTarget(g, g.GetComponentsInChildren<Renderer>(true), DeskTarget.Monitor, rb);
            }
            office.ScreenDesktop = Mat("M_RmScreenDesktop");
            office.ScreenAlert = Mat("M_RmScreenAlert");
            office.ScreenOffline = Mat("M_RmScreenOffline");
            office.ScreenCoffee = Mat("M_RmScreenCoffee");
            office.ScreenOff = Mat("M_RmScreenOff");
            office.LedBlue = Mat("M_LEDBlue"); office.LedAmber = Mat("M_LEDAmber"); office.LedOff = Mat("M_LEDOff");
            if (office.Screen != null) { office.Screen.sharedMaterial = office.ScreenDesktop; office.Screen.shadowCastingMode = ShadowCastingMode.Off; }
            // phone
            if (props.TryGetValue("phone", out g))
            {
                office.Phone = g.transform;
                office.Handset = FindDeep(g.transform, "Handset");
                office.RingLight = RendererUnder(g.transform, "RingLight");
                office.Display = RendererUnder(g.transform, "PhoneDisplay");
                var plug = FindDeep(g.transform, "Plug");
                office.Plug = plug != null ? plug.gameObject : null;
                office.CordBase = FindDeep(g.transform, "CordBase");
                office.CordEnd = FindDeep(g.transform, "CordEnd");
                var cordGo = new GameObject("Cord");
                cordGo.transform.SetParent(g.transform, false);
                var lr = cordGo.AddComponent<LineRenderer>();
                lr.sharedMaterial = Mat("M_RmCord");
                lr.widthMultiplier = 0.006f;
                lr.numCapVertices = 2;
                lr.numCornerVertices = 2;
                lr.alignment = LineAlignment.View;
                lr.shadowCastingMode = ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                office.Cord = lr;
                var glow = new GameObject("RingGlow").AddComponent<Light>();
                glow.transform.SetParent(g.transform, false);
                glow.transform.localPosition = new Vector3(0.08f, 0.16f, 0.02f);
                glow.type = LightType.Point;
                glow.color = Hex("#FF4A3A");
                glow.range = 0.75f;
                glow.intensity = 0f;
                glow.shadows = LightShadows.None;
                glow.enabled = false;
                office.RingGlow = glow;
                var targetRenderers = g.GetComponentsInChildren<Renderer>(true).Where(r => office.Plug == null || !r.transform.IsChildOf(office.Plug.transform));
                MakeTarget(g, targetRenderers, DeskTarget.Phone, rb, 0.03f);
                // the merged body includes the wall cord trailing behind: keep the click box on the base
                var pbc = g.GetComponent<BoxCollider>();
                if (pbc != null) { pbc.center = new Vector3(0f, 0.07f, 0f); pbc.size = new Vector3(0.27f, 0.16f, 0.25f); }
            }
            office.RingOff = Mat("M_RmRingOff"); office.RingRed = Mat("M_LEDRed"); office.RingGreen = Mat("M_LEDGreen");
            office.LcdOn = Mat("M_RmPhoneLcd"); office.LcdOff = Mat("M_RmScreenOff");
            // mug + spill
            if (props.TryGetValue("mug", out g))
            {
                office.Mug = g.transform;
                var coffee = FindDeep(g.transform, "Coffee");
                office.Coffee = coffee != null ? coffee.gameObject : null;
                office.MugStripe = RendererUnder(g.transform, "Stripe");
                MakeTarget(g, g.GetComponentsInChildren<Renderer>(true), DeskTarget.Mug, rb, 0.04f);
            }
            if (props.TryGetValue("spill", out g)) office.Spill = g;
            // drawer
            if (props.TryGetValue("drawerunit", out g))
            {
                office.Drawer = FindDeep(g.transform, "Drawer");
                MakeTarget(g, g.GetComponentsInChildren<Renderer>(true), DeskTarget.Drawer, rb);
            }
            // nameplate (+ names on both brass plates)
            if (props.TryGetValue("nameplate", out g))
            {
                office.Nameplate = g.transform;
                office.NameStripe = RendererUnder(g.transform, "Stripe");
                var font = rb.Fonts != null ? rb.Fonts.TmpTitle ?? rb.Fonts.Tmp : null;
                var ink = Hex("#2A1C10");
                var front = Label3D(g.transform, "NameFront", new Vector3(0f, 0.0605f, -0.0252f), new Vector3(18f, 0f, 0f), new Vector2(0.22f, 0.04f), font, ink, "AD", 0.5f);
                var back = Label3D(g.transform, "NameBack", new Vector3(0f, 0.0605f, 0.0252f), new Vector3(18f, 180f, 0f), new Vector2(0.22f, 0.04f), font, ink, "AD", 0.5f);
                office.NameLabels = new[] { front, back };
                MakeTarget(g, g.GetComponentsInChildren<MeshRenderer>(true), DeskTarget.Nameplate, rb, 0.03f);
            }
            if (props.TryGetValue("chair", out g))
            {
                office.ChairFabric = RendererUnder(g.transform, "Fabric");
                office.Chair = g.transform;
                MakeTarget(g, g.GetComponentsInChildren<Renderer>(true), DeskTarget.Chair, rb, 0.02f);
            }
            // v5 cassette recorder
            if (props.TryGetValue("recorder", out g))
            {
                office.Recorder = g.transform;
                office.RecButton = FindDeep(g.transform, "RecButton");
                office.RecLight = RendererUnder(g.transform, "RecLight");
                var tape = FindDeep(g.transform, "TapeIn");
                office.TapeIn = tape != null ? tape.gameObject : null;
                office.ReelL = FindDeep(g.transform, "ReelL");
                office.ReelR = FindDeep(g.transform, "ReelR");
                var cassettes = new List<GameObject>();
                for (int i = 0; i < 3; i++)
                {
                    var c = FindDeep(g.transform, "Cassette" + i);
                    if (c != null) cassettes.Add(c.gameObject);
                }
                office.Cassettes = cassettes.ToArray();
                office.RecOff = Mat("M_RmRingOff");
                office.RecOn = Mat("M_LEDRed");
                MakeTarget(g, g.GetComponentsInChildren<Renderer>(true), DeskTarget.Recorder, rb, 0.02f);
                Log("ROOMS recorder: button=" + (office.RecButton != null) + " lamp=" + (office.RecLight != null) + " tape=" + (office.TapeIn != null)
                    + " reels=" + (office.ReelL != null && office.ReelR != null) + " cassettes=" + office.Cassettes.Length);
            }
            if (props.TryGetValue("lamp", out g)) office.LampGlow = RendererUnder(g.transform, "Glow");
            if (props.TryGetValue("photo", out g)) office.Photo = RendererUnder(g.transform, "Photo");
            if (props.TryGetValue("clock", out g)) MakeClock(g, rb);
            if (props.TryGetValue("cork", out g))
            {
                var cs = RendererUnder(g.transform, "CorkSurface");
                if (cs != null && Mat("M_RmCorkNotes") != null) cs.sharedMaterial = Mat("M_RmCorkNotes");
                office.CorkSurface = cs;
                MakeTarget(g, g.GetComponentsInChildren<Renderer>(true), DeskTarget.Board, rb, 0.02f);
            }
            office.PhotoMats = Enumerable.Range(0, 6).Select(i => Mat("M_RmPhoto" + i)).Where(m => m != null).ToArray();
            office.PlayerColors = Enumerable.Range(0, 6).Select(i => Mat("M_PlayerColor" + i)).ToArray();
            office.SeatColors = Enumerable.Range(0, 6).Select(i => Mat("M_RmSeatColor" + i)).ToArray();
            // window target: the frame + glass of the shell
            var frame = FindDeep(shell.transform, "WindowFrame");
            var glass = FindDeep(shell.transform, "WindowGlass");
            if (frame != null)
            {
                var host = new GameObject("WindowTarget");
                host.transform.SetParent(root, false);
                var rs = new List<Renderer>();
                if (glass != null) rs.Add(glass.GetComponent<Renderer>());
                var t = MakeTarget(host, rs, DeskTarget.Window, rb, 0.08f);
                // highlight the frame, not the glass
                foreach (var ov in t.Overlays) Object.DestroyImmediate(ov.gameObject);
                var fr = frame.GetComponent<MeshRenderer>();
                var ovGo = new GameObject("Hover");
                ovGo.transform.SetParent(frame, false);
                GrowAboutCenter(ovGo.transform, frame.GetComponent<MeshFilter>().sharedMesh.bounds, frame.lossyScale);
                ovGo.AddComponent<MeshFilter>().sharedMesh = frame.GetComponent<MeshFilter>().sharedMesh;
                var omr = ovGo.AddComponent<MeshRenderer>();
                omr.sharedMaterials = Enumerable.Repeat(Mat("M_RmHover"), fr.sharedMaterials.Length).ToArray();
                omr.shadowCastingMode = ShadowCastingMode.Off;
                omr.enabled = false;
                t.Overlays = new Renderer[] { omr };
            }
            // posters
            var slots = new List<OfficeRoom.DecalSlot>();
            int di = 0;
            foreach (JObject d in (JArray)o["decals"])
            {
                var ids = ((JArray)d["variants"]).Select(v => (string)v).ToArray();
                var size = new Vector2((float)d["size"][0], (float)d["size"][1]);
                var surf = RoomDecal(root, (string)d["id"], RV(d["p"]), (float)d["r"], size, DecalMat(ids[0], false));
                slots.Add(new OfficeRoom.DecalSlot
                {
                    Surface = surf,
                    Tr = ids.Select(i => DecalMat(i, false)).ToArray(),
                    En = ids.Select(i => DecalMat(i, true)).ToArray(),
                    Offset = di * 2,
                });
                di++;
            }
            office.Decals = slots.ToArray();
            // first-person hands (bean mittens, skin = the player's look)
            var hl = RoomProp(root, "RmHandL", RV(a["handL"]), 0, "HandL", rb);
            var mitten = hl.GetComponentInChildren<MeshFilter>(true);
            if (mitten != null) Log("ROOMS hand check: mesh bounds " + mitten.sharedMesh.bounds.size.ToString("F3") + " (arm along local Z expected), node rot " + mitten.transform.localEulerAngles.ToString("F1"));
            var hr = RoomProp(root, "RmHandR", RV(a["handR"]), 0, "HandR", rb);
            hl.transform.localRotation = Quaternion.LookRotation(new Vector3(-0.1f, 0.22f, -1f).normalized, Vector3.up);
            hr.transform.localRotation = Quaternion.LookRotation(new Vector3(0.14f, 0.22f, -1f).normalized, Vector3.up);
            office.HandL = hl.transform;
            office.HandR = hr.transform;
            office.HandRenderers = hl.GetComponentsInChildren<Renderer>(true).Concat(hr.GetComponentsInChildren<Renderer>(true)).ToArray();
            office.HandSkinDefault = Mat("M_BeanBlue");
            office.SkinMats = RoomSkins.Select(Mat).ToArray();
            // passer-by behind the frosted door glass
            var corridor = (JObject)a["corridor"];
            var sil = Quad(root, "Silhouette", new Vector3((float)corridor["x"], 0.98f, -0.85f), new Vector3(0f, 90f, 0f), new Vector3(1.1f, 1.9f, 1f), Mat("M_RmSilhouette"));
            office.Silhouette = sil.transform;
            office.CorridorX = (float)corridor["x"];
            office.CorridorZ0 = (float)corridor["z0"];
            office.CorridorZ1 = (float)corridor["z1"];
            // lights
            var lroot = new GameObject("Lighting").transform;
            lroot.SetParent(root, false);
            foreach (JObject e in (JArray)o["lights"])
            {
                var l = RoomLight(lroot, e, rb, "office.");
                string n = (string)e["name"];
                if (n == "WindowFill") rb.Fill.Add(l);
                else if (n == "Monitor") office.MonitorLight = l;
                else rb.Warm.Add(l);
                if (n == "DeskLamp") office.LampLight = l;
            }
            var room = (JObject)o["room"];
            float rx0 = (float)room["x0"], rx1 = (float)room["x1"], rz0 = (float)room["z0"], rz1 = (float)room["z1"], rh = (float)room["h"];
            RoomProbe(root, new Vector3((rx0 + rx1) / 2, 1.3f, (rz0 + rz1) / 2), new Vector3(rx1 - rx0, rh, rz1 - rz0), rb);
            BuildOfficeWalk(office, o, rx0, rx1, rz0, rz1);
            BuildDoorLamp(root, office, o, rb);
            return office;
        }

        /// <summary>v5: walking data from the layout ("walk": eye height, body radius, stand spot, chair, footprints).</summary>
        static void BuildOfficeWalk(OfficeRoom office, JObject o, float x0, float x1, float z0, float z1)
        {
            office.RoomMin = new Vector2(x0, z0);
            office.RoomMax = new Vector2(x1, z1);
            var w = o["walk"] as JObject;
            if (w == null) { Log("ROOMS office layout has no walk data (run Blender build_rooms.py)"); return; }
            office.StandEye = (float)w["eye"];
            office.WalkRadius = (float)w["radius"];
            office.StandSpot = new Vector2((float)w["stand"][0], (float)w["stand"][1]);
            office.ChairBack = new Vector3((float)w["chairBack"][0], 0f, (float)w["chairBack"][1]);
            office.ChairBackYaw = (float)w["chairBackYaw"];
            office.ChairRadius = (float)w["chairRadius"];
            var rects = new List<Vector4>();
            var yaws = new List<float>();
            foreach (JObject r in (JArray)w["rects"])
            {
                rects.Add(new Vector4((float)r["c"][0], (float)r["c"][1], (float)r["h"][0], (float)r["h"][1]));
                yaws.Add((float)r["r"]);
            }
            var circles = new List<Vector3>();
            foreach (JObject c in (JArray)w["circles"]) circles.Add(new Vector3((float)c["c"][0], (float)c["c"][1], (float)c["r"]));
            office.WalkRects = rects.ToArray();
            office.WalkRectYaw = yaws.ToArray();
            office.WalkCircles = circles.ToArray();
            Log("ROOMS office walk: " + rects.Count + " rects, " + circles.Count + " circles, room " + office.RoomMin + ".." + office.RoomMax);
        }

        /// <summary>v5: "TOPLANTI" lamp over the inside of the office door (lights up before a meeting).</summary>
        static void BuildDoorLamp(Transform root, OfficeRoom office, JObject o, RoomBuild rb)
        {
            var w = o["walk"] as JObject;
            if (w == null || w["doorLamp"] == null) return;
            var p = RV(w["doorLamp"]);
            var lamp = new GameObject("DoorLamp").transform;
            lamp.SetParent(root, false);
            lamp.localPosition = new Vector3(p.x - 0.03f, p.y, p.z);
            lamp.localRotation = Quaternion.Euler(0f, 90f, 0f); // front (-z local) faces into the room (-x)
            var housing = Cube(lamp, "Housing", new Vector3(0f, 0f, 0.012f), new Vector3(0.34f, 0.1f, 0.05f), Mat("M_RmWalnutDark"));
            housing.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var glass = Cube(lamp, "Glass", new Vector3(0f, 0f, -0.016f), new Vector3(0.3f, 0.075f, 0.012f), Mat("M_RmDoorLampOff"));
            glass.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            office.DoorLamp = glass.GetComponent<Renderer>();
            office.DoorLampOff = Mat("M_RmDoorLampOff");
            office.DoorLampOn = Mat("M_RmDoorLampOn");
            var font = rb.Fonts != null ? rb.Fonts.TmpTitle ?? rb.Fonts.Tmp : null;
            office.DoorLabel = Label3D(lamp, "DoorLabel", new Vector3(0f, -0.002f, -0.0235f), new Vector3(0f, 0f, 0f), new Vector2(0.27f, 0.06f), font, Hex("#2A0A06"), "TOPLANTI", 0.5f);
            var light = new GameObject("DoorLight").AddComponent<Light>();
            light.transform.SetParent(lamp, false);
            light.transform.localPosition = new Vector3(0f, -0.05f, -0.22f);
            light.type = LightType.Point;
            light.color = Hex("#FF6A44");
            light.range = 1.8f;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            light.enabled = false;
            office.DoorLight = light;
        }

        // ================================================================== meeting

        static MeetingRoom BuildMeeting(Transform root, JObject o, RoomBuild rb, SeatedBean[] players, string[] bossIds, SeatedBean[] bosses)
        {
            var meeting = root.gameObject.AddComponent<MeetingRoom>();
            var shellModel = Model("Rooms", "RmMeetingShell");
            GameObject shell = shellModel != null ? Inst(shellModel, root, Vector3.zero, 0, "Shell") : new GameObject("Shell");
            foreach (var r in shell.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == "WindowGlass" || r.name == "DoorGlass") r.shadowCastingMode = ShadowCastingMode.Off;
                if (r.name == "Corridor") { r.shadowCastingMode = ShadowCastingMode.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off; }
            }
            var props = new Dictionary<string, GameObject>();
            foreach (JObject e in (JArray)o["props"])
                props[(string)e["id"]] = RoomProp(root, (string)e["model"], RV(e["p"]), (float)e["r"], (string)e["id"], rb);
            var a = (JObject)o["anchors"];
            GameObject g;
            if (props.TryGetValue("clock", out g)) MakeClock(g, rb);
            if (props.TryGetValue("screen", out g))
            {
                meeting.Canvas = RendererUnder(g.transform, "Canvas");
                if (meeting.Canvas != null) { meeting.Canvas.sharedMaterial = Mat("M_RmCanvasUnlit"); meeting.Canvas.shadowCastingMode = ShadowCastingMode.Off; }
            }
            meeting.DefaultSlideTr = AssetDatabase.LoadAssetAtPath<Texture2D>(RoomsTexDir + "/SlideTr.png");
            meeting.DefaultSlideEn = AssetDatabase.LoadAssetAtPath<Texture2D>(RoomsTexDir + "/SlideEn.png");
            if (Mat("M_RmCanvasUnlit") != null) { Mat("M_RmCanvasUnlit").SetTexture("_BaseMap", meeting.DefaultSlideTr); EditorUtility.SetDirty(Mat("M_RmCanvasUnlit")); }
            if (props.TryGetValue("whiteboard", out g))
            {
                var ws = RendererUnder(g.transform, "BoardSurface");
                if (ws != null && Mat("M_RmWhiteboardDoodles") != null) ws.sharedMaterial = Mat("M_RmWhiteboardDoodles");
            }
            // projector beam: lens -> canvas corners
            GameObject proj;
            if (props.TryGetValue("projector", out proj) && meeting.Canvas != null)
                meeting.Beam = ProjectorBeam(root, proj.transform.TransformPoint(new Vector3(0.07f, -0.395f, -0.18f)), meeting.Canvas);
            // seats
            var seats = new List<MeetingRoom.SeatRig>();
            int si = 0;
            foreach (JObject s in (JArray)o["seats"])
            {
                var anchor = new GameObject("Seat_" + si).transform;
                anchor.SetParent(root, false);
                anchor.localPosition = RV(s["p"]);
                anchor.localRotation = Quaternion.Euler(0f, (float)s["yaw"], 0f);
                var chair = RoomProp(anchor, "RmMeetingChair", Vector3.zero, 0, "Chair", rb);
                var items = new GameObject("Items");
                items.transform.SetParent(anchor, false);
                RoomProp(items.transform, "RmNotepad", new Vector3(-0.06f, 0.75f, 0.56f), 6f, "Notepad", rb);
                RoomProp(items.transform, "RmGlass", new Vector3(0.24f, 0.75f, 0.5f), 0f, "Glass", rb);
                seats.Add(new MeetingRoom.SeatRig { Anchor = anchor, ChairFabric = RendererUnder(chair.transform, "Fabric"), Items = items });
                si++;
            }
            meeting.Seats = seats.ToArray();
            meeting.BeanPrefabs = players;
            meeting.BossIds = bossIds;
            meeting.BossPrefabs = bosses;
            meeting.SeatColors = Enumerable.Range(0, 6).Select(i => Mat("M_RmSeatColor" + i)).ToArray();
            meeting.ChairNeutral = Mat("M_RmLeather");
            var mm = new GameObject("ManagerMount").transform;
            mm.SetParent(root, false);
            mm.localPosition = RV(a["manager"]);
            mm.localRotation = Quaternion.Euler(0f, (float)a["managerYaw"], 0f);
            meeting.ManagerMount = mm;
            meeting.EyeUp = (float)a["eyeUp"];
            meeting.EyeForward = (float)a["eyeForward"];
            meeting.YawLimit = (float)a["yawLimit"];
            meeting.PitchUp = (float)a["pitchUp"];
            meeting.PitchDown = (float)a["pitchDown"];
            var box = RoomProp(root, "RmBox", Vector3.zero, 0, "FiredBox", rb);
            box.SetActive(false);
            meeting.FiredBox = box;
            // decals
            foreach (JObject d in (JArray)o["decals"])
            {
                var ids = ((JArray)d["variants"]).Select(v => (string)v).ToArray();
                RoomDecal(root, (string)d["id"], RV(d["p"]), (float)d["r"], new Vector2((float)d["size"][0], (float)d["size"][1]), DecalMat(ids[0], false));
            }
            // lights + defense spotlight
            var lroot = new GameObject("Lighting").transform;
            lroot.SetParent(root, false);
            var dim = new List<Light>();
            foreach (JObject e in (JArray)o["lights"])
            {
                var l = RoomLight(lroot, e, rb, "meeting.");
                string n = (string)e["name"];
                if (n == "WindowFill") { rb.Fill.Add(l); dim.Add(l); }
                else { rb.Warm.Add(l); if (n.StartsWith("Pendant")) dim.Add(l); }
            }
            meeting.DimWithSpot = dim.ToArray();
            var spot = new GameObject("DefenseSpot").AddComponent<Light>();
            spot.transform.SetParent(lroot, false);
            spot.type = LightType.Spot;
            spot.color = Hex("#FFF1D2");
            spot.spotAngle = 38f;
            spot.innerSpotAngle = 20f;
            spot.range = 4.5f;
            spot.intensity = 0f;
            spot.shadows = LightShadows.None;
            spot.enabled = false;
            meeting.Spot = spot;
            meeting.SpotCone = SpotCone(lroot);
            var room = (JObject)o["room"];
            float rx0 = (float)room["x0"], rx1 = (float)room["x1"], rz0 = (float)room["z0"], rz1 = (float)room["z1"], rh = (float)room["h"];
            RoomProbe(root, new Vector3((rx0 + rx1) / 2, 1.3f, (rz0 + rz1) / 2), new Vector3(rx1 - rx0, rh, rz1 - rz0), rb);
            return meeting;
        }

        static Renderer SpotCone(Transform parent)
        {
            string path = RoomsGenDir + "/SpotCone.asset";
            AssetDatabase.DeleteAsset(path);
            var m = new Mesh { name = "SpotCone" };
            const int seg = 24;
            const float len = 1.75f, r0 = 0.05f, r1 = 0.62f;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                v.Add(dir * r0); uv.Add(new Vector2(i / (float)seg, 0f));
                v.Add(dir * r1 + Vector3.forward * len); uv.Add(new Vector2(i / (float)seg, 1f));
            }
            for (int i = 0; i < seg; i++)
            {
                int k = i * 2;
                tri.AddRange(new[] { k, k + 1, k + 2, k + 2, k + 1, k + 3 });
                tri.AddRange(new[] { k, k + 2, k + 1, k + 2, k + 3, k + 1 });
            }
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, path);
            var go = new GameObject("SpotCone");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Mat("M_RmBeamFx");
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.enabled = false;
            return mr;
        }

        static Renderer ProjectorBeam(Transform parent, Vector3 lensWorld, Renderer canvas)
        {
            string path = RoomsGenDir + "/ProjectorBeam.asset";
            AssetDatabase.DeleteAsset(path);
            var mf = canvas.GetComponent<MeshFilter>();
            var b = mf.sharedMesh.bounds;
            var tr = canvas.transform;
            var c = new[] { tr.TransformPoint(new Vector3(b.min.x, b.min.y, 0f)), tr.TransformPoint(new Vector3(b.max.x, b.min.y, 0f)),
                            tr.TransformPoint(new Vector3(b.max.x, b.max.y, 0f)), tr.TransformPoint(new Vector3(b.min.x, b.max.y, 0f)) };
            var go = new GameObject("ProjectorBeam");
            go.transform.SetParent(parent, true);
            go.transform.position = lensWorld;
            var m = new Mesh { name = "ProjectorBeam" };
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = c[i] - lensWorld, bb = c[(i + 1) % 4] - lensWorld;
                int k = v.Count;
                v.Add(Vector3.zero); uv.Add(new Vector2(0.5f, 0f));
                v.Add(a * 0.97f); uv.Add(new Vector2(0f, 1f));
                v.Add(bb * 0.97f); uv.Add(new Vector2(1f, 1f));
                tri.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 1 });
            }
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, path);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            var beam = GetOrCreate("M_RmProjectorBeam", Shader.Find("Universal Render Pipeline/Unlit"));
            beam.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(RoomsTexDir + "/BeamGradient.png"));
            beam.SetColor("_BaseColor", new Color(0.85f, 0.9f, 1f, 0.07f));
            SetAdditive(beam);
            EditorUtility.SetDirty(beam);
            mr.sharedMaterial = beam;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mr;
        }

        // ================================================================== bootstrap / network / UI
        // No avatars travel over the network (seated first person): NGO only spawns the NetSession object.

        static void BuildRoomsBootstrap(PrefabSet prefabs, FontSet fonts)
        {
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
                ConnectionApproval = true,
                EnableSceneManagement = false,
                TickRate = 30,
                ClientConnectionBufferTimeout = 15,
            };
            string listPath = GenDir + "/NetworkPrefabs.asset";
            AssetDatabase.DeleteAsset(listPath);
            var list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
            list.Add(new NetworkPrefab { Prefab = prefabs.Session });
            AssetDatabase.CreateAsset(list, listPath);
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(list);
            var conn = boot.AddComponent<ConnectionManager>();
            conn.SessionPrefab = prefabs.Session.GetComponent<NetworkObject>();
            boot.AddComponent<Sfx>();

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
        }
    }
}
