using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BirimizKovulacak.Core;
using BirimizKovulacak.Game;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace BirimizKovulacak.EditorTools
{
    /// <summary>
    /// Regenerates every Unity-side asset from code and the Blender exports: project settings, URP, materials
    /// (from Art/palette.json), model import rules, animator controllers, fonts, prefabs, the Office scene (from
    /// DefaultOffice) and the Windows player. Run from the menu or batchmode:
    ///   Unity -batchmode -projectPath Game -executeMethod BirimizKovulacak.EditorTools.ProjectBuilder.BuildAllCli -quit
    /// </summary>
    public static partial class ProjectBuilder
    {
        public const string Root = "Assets/Game";
        public const string MatDir = Root + "/Art/Materials";
        public const string ModelDir = Root + "/Art/Models";
        public const string PrefabDir = Root + "/Prefabs";
        public const string GenDir = Root + "/Generated";
        public const string ScenePath = Root + "/Scenes/Office.unity";
        public const string BuildDir = "../Builds/BirimizKovulacak-Windows";
        public const string ExeName = "BirimizKovulacak.exe";

        static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();

        [MenuItem("Birimiz Kovulacak/Build Everything (assets + scene + player)")]
        public static void BuildAllMenu() { BuildAll(true); }

        [MenuItem("Birimiz Kovulacak/Rebuild Assets + Scene")]
        public static void BuildAssetsMenu() { BuildAll(false); }

        public static void BuildAllCli()
        {
            try { BuildAll(!Environment.GetCommandLineArgs().Contains("-noPlayer")); EditorApplication.Exit(0); }
            catch (RerunRequiredException) { EditorApplication.Exit(3); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static void BuildPlayerCli()
        {
            try { EditorApplication.Exit(BuildPlayer() ? 0 : 1); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static void BuildAll(bool player)
        {
            Log("=== ProjectBuilder start ===");
            EnsureDirs();
            ConfigureProject();
            var pipeline = ConfigureRenderPipeline();
            CreateMaterials();
            ConfigureModelImports();
            if (ImportTmpEssentials())
            {
                // The package import finishes after this method returns in batchmode; run the builder again.
                Log("TMP essentials were just imported: RERUN REQUIRED");
                throw new RerunRequiredException();
            }
            var fonts = CreateFonts();
            WriteStreamingAssets();
            var prefabs = CreatePrefabs(fonts);
            BuildScene(prefabs, fonts, pipeline);
            AssetDatabase.SaveAssets();
            Log("=== Assets + scene done ===");
            if (player && !BuildPlayer()) throw new Exception("Player build failed");
        }

        public static void Log(string s) { Debug.Log("[Builder] " + s); }

        static void EnsureDirs()
        {
            foreach (var d in new[] { MatDir, PrefabDir, PrefabDir + "/Items", GenDir, Root + "/Scenes", Root + "/Resources", "Assets/StreamingAssets/Config", "Assets/StreamingAssets/Localization" })
                Directory.CreateDirectory(d);
            AssetDatabase.Refresh();
        }

        // ================================================================== project settings

        static void ConfigureProject()
        {
            PlayerSettings.companyName = "Leonidas Studio";
            PlayerSettings.productName = "Birimiz Kovulacak";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.usePlayerLog = true;
            PlayerSettings.forceSingleInstance = false;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Standard);
            // Memory (docs/PERFORMANCE.md): D3D11 first (smaller driver footprint than D3D12 for this scene), incremental GC,
            // strip unused mesh channels and mips.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11, UnityEngine.Rendering.GraphicsDeviceType.Direct3D12 });
            PlayerSettings.gcIncremental = true;
            PlayerSettings.stripUnusedMeshComponents = true;
            PlayerSettings.mipStripping = true;

            var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (ps != null)
            {
                var so = new SerializedObject(ps);
                var p = so.FindProperty("activeInputHandler");
                if (p != null) { p.intValue = 2; so.ApplyModifiedProperties(); }
            }

            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tm.FindProperty("layers");
            SetLayer(layers, Layers.Walls, Layers.WallsName);
            SetLayer(layers, Layers.Glass, Layers.GlassName);
            SetLayer(layers, Layers.Furniture, Layers.FurnitureName);
            SetLayer(layers, Layers.Avatar, Layers.AvatarName);
            SetLayer(layers, Layers.Partitions, Layers.PartitionsName);
            tm.ApplyModifiedProperties();
            Log("Project settings configured");
        }

        static void SetLayer(SerializedProperty layers, int index, string name)
        {
            var e = layers.GetArrayElementAtIndex(index);
            if (e.stringValue != name) e.stringValue = name;
        }

        // ================================================================== URP

        public sealed class PipelineAssets
        {
            public UniversalRenderPipelineAsset Asset;
            public VolumeProfile Profile;
        }

        static PipelineAssets ConfigureRenderPipeline()
        {
            string rdPath = GenDir + "/URP_Renderer.asset";
            string assetPath = GenDir + "/URP_Pipeline.asset";
            var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rdPath);
            if (rd == null)
            {
                rd = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rd, rdPath);
                var ssaoType = typeof(ScriptableRendererFeature).Assembly.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion");
                if (ssaoType != null)
                {
                    var f = (ScriptableRendererFeature)ScriptableObject.CreateInstance(ssaoType);
                    f.name = "SSAO";
                    AssetDatabase.AddObjectToAsset(f, rd);
                    rd.rendererFeatures.Add(f);
                    var fso = new SerializedObject(f);
                    var intensity = fso.FindProperty("m_Settings.Intensity");
                    if (intensity != null) intensity.floatValue = 1.4f;
                    var radius = fso.FindProperty("m_Settings.Radius");
                    if (radius != null) radius.floatValue = 0.35f;
                    fso.ApplyModifiedPropertiesWithoutUndo();
                    var map = new SerializedObject(rd).FindProperty("m_RendererFeatureMap");
                    if (map != null)
                    {
                        var rso = new SerializedObject(rd);
                        var m = rso.FindProperty("m_RendererFeatureMap");
                        m.arraySize = 1;
                        long id; string guid;
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(f, out guid, out id);
                        m.GetArrayElementAtIndex(0).longValue = id;
                        rso.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
                EditorUtility.SetDirty(rd);
            }
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
            if (asset == null)
            {
                asset = UniversalRenderPipelineAsset.Create(rd);
                AssetDatabase.CreateAsset(asset, assetPath);
            }
            var so = new SerializedObject(asset);
            SetProp(so, "m_SupportsHDR", true);
            SetProp(so, "m_MSAA", 2);
            SetProp(so, "m_ShadowDistance", 32f);
            SetProp(so, "m_ShadowCascadeCount", 2);
            SetProp(so, "m_SoftShadowsSupported", true);
            SetProp(so, "m_MainLightShadowmapResolution", 2048);
            SetProp(so, "m_RenderScale", 1f);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);

            GraphicsSettings.defaultRenderPipeline = asset;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }

            string profilePath = GenDir + "/PostProfile.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, profilePath);
                var tone = profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.Neutral);
                var bloom = profile.Add<Bloom>(true); bloom.intensity.Override(0.55f); bloom.threshold.Override(1.05f); bloom.scatter.Override(0.6f);
                var ca = profile.Add<ColorAdjustments>(true); ca.postExposure.Override(0.2f); ca.saturation.Override(14f); ca.contrast.Override(10f);
                var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.2f); vig.smoothness.Override(0.45f);
                foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
                EditorUtility.SetDirty(profile);
            }
            AssetDatabase.SaveAssets();
            Log("URP configured");
            return new PipelineAssets { Asset = asset, Profile = profile };
        }

        static void SetProp(SerializedObject so, string name, object value)
        {
            var p = so.FindProperty(name);
            if (p == null) { Log("  (no property " + name + ")"); return; }
            switch (value)
            {
                case bool b: p.boolValue = b; break;
                case int i: if (p.propertyType == SerializedPropertyType.Enum) p.enumValueIndex = i; else p.intValue = i; break;
                case float f: p.floatValue = f; break;
            }
        }

        // ================================================================== materials

        public static Material Mat(string name)
        {
            Material m;
            if (Mats.TryGetValue(name, out m) && m != null) return m;
            m = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/" + name + ".mat");
            if (m != null) Mats[name] = m;
            return m;
        }

        static void CreateMaterials()
        {
            var json = JObject.Parse(File.ReadAllText(Path.GetFullPath("../Art/palette.json")));
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var prop in (JObject)json["materials"])
            {
                var spec = (JObject)prop.Value;
                CreateLit(prop.Key, lit, Hex((string)spec["color"]), (float?)spec["smooth"] ?? 0.3f, (float?)spec["metal"] ?? 0f, (float?)spec["emit"] ?? 0f, (float?)spec["alpha"] ?? 1f);
            }
            // Runtime-only materials.
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            CreateLit("M_PlayerColor0", lit, Hex("#4A90E2"), 0.4f, 0, 0.15f, 1);
            CreateLit("M_PlayerColor1", lit, Hex("#F5C842"), 0.4f, 0, 0.15f, 1);
            CreateLit("M_PlayerColor2", lit, Hex("#F28AB2"), 0.4f, 0, 0.15f, 1);
            CreateLit("M_PlayerColor3", lit, Hex("#6CC56A"), 0.4f, 0, 0.15f, 1);
            CreateUnlit("M_VisionCone", unlit, new Color(1f, 0.85f, 0.2f, 0.22f));
            CreateUnlit("M_FocusMarker", unlit, new Color(1f, 0.78f, 0.15f, 0.95f));
            CreateUnlit("M_EvidenceRing", unlit, new Color(1f, 0.25f, 0.2f, 0.8f));
            CreateUnlit("M_SealTape", unlit, new Color(1f, 0.82f, 0.1f, 1f));
            CreateUnlit("M_BubbleBg", unlit, new Color(1f, 1f, 1f, 0.95f));
            CreateLit("M_LightOn", lit, Hex("#FF3B2F"), 0.6f, 0, 2.5f, 1);
            CreateLit("M_LightOff", lit, Hex("#5A1A16"), 0.6f, 0, 0, 1);
            CreateLit("M_GreenOn", lit, Hex("#3BFF73"), 0.6f, 0, 2.5f, 1);
            CreateLit("M_GreenOff", lit, Hex("#1E5A32"), 0.6f, 0, 0, 1);
            var particle = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (particle != null)
            {
                var steam = GetOrCreate("M_Steam", particle);
                steam.SetColor("_BaseColor", new Color(1, 1, 1, 0.35f));
                SetTransparent(steam);
            }
            AssetDatabase.SaveAssets();
            Log("Materials: " + Mats.Count);
        }

        static Color Hex(string hex)
        {
            Color c;
            ColorUtility.TryParseHtmlString(hex, out c);
            return c;
        }

        static Material GetOrCreate(string name, Shader shader)
        {
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            else m.shader = shader;
            Mats[name] = m;
            return m;
        }

        static void CreateLit(string name, Shader shader, Color color, float smooth, float metal, float emit, float alpha)
        {
            var m = GetOrCreate(name, shader);
            color.a = alpha;
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            if (emit > 0)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", new Color(color.r, color.g, color.b) * emit);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
            }
            if (alpha < 0.999f) SetTransparent(m);
            else SetOpaque(m);
            EditorUtility.SetDirty(m);
        }

        static void CreateUnlit(string name, Shader shader, Color color)
        {
            var m = GetOrCreate(name, shader);
            m.SetColor("_BaseColor", color);
            if (color.a < 0.999f) SetTransparent(m); else SetOpaque(m);
            EditorUtility.SetDirty(m);
        }

        static void SetTransparent(Material m)
        {
            m.SetFloat("_Surface", 1);
            m.SetFloat("_Blend", 0);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetShaderPassEnabled("DepthOnly", false);
            m.SetShaderPassEnabled("ShadowCaster", false);
        }

        static void SetOpaque(Material m)
        {
            m.SetFloat("_Surface", 0);
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", (float)BlendMode.Zero);
            m.SetFloat("_ZWrite", 1);
            m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Opaque");
            m.renderQueue = -1;
            m.SetShaderPassEnabled("DepthOnly", true);
            m.SetShaderPassEnabled("ShadowCaster", true);
        }

        // ================================================================== model import

        static readonly string[] LoopClips = { "Idle", "Walk", "CarryIdle", "CarryWalk", "Work", "WorkPose", "Interact", "Sneak", "Phone", "Panic", "Fired", "Cheer", "Talk", "Inspect", "Angry", "Point" };

        static void ConfigureModelImports()
        {
            var guids = AssetDatabase.FindAssets("t:Model", new[] { ModelDir });
            foreach (var g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var imp = AssetImporter.GetAtPath(path) as ModelImporter;
                if (imp == null) continue;
                bool character = path.Contains("/Characters/");
                imp.globalScale = 1f;
                imp.useFileScale = true;
                imp.bakeAxisConversion = true;
                imp.importCameras = false;
                imp.importLights = false;
                imp.importBlendShapes = false;
                imp.isReadable = false;
                imp.meshCompression = ModelImporterMeshCompression.Off;
                imp.importNormals = ModelImporterNormals.Import;
                imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
                if (character)
                {
                    imp.animationType = ModelImporterAnimationType.Generic;
                    imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    imp.importAnimation = true;
                    imp.animationCompression = ModelImporterAnimationCompression.Optimal;
                    var clips = imp.defaultClipAnimations;
                    foreach (var c in clips)
                    {
                        string n = c.name.Contains("|") ? c.name.Substring(c.name.LastIndexOf('|') + 1) : c.name;
                        c.name = n;
                        c.loopTime = LoopClips.Contains(n);
                        c.loopPose = false;
                    }
                    imp.clipAnimations = clips;
                }
                else
                {
                    imp.animationType = ModelImporterAnimationType.None;
                    imp.importAnimation = false;
                }
                imp.SaveAndReimport();
                imp.SearchAndRemapMaterials(ModelImporterMaterialName.BasedOnMaterialName, ModelImporterMaterialSearch.Everywhere);
                imp.SaveAndReimport();
            }
            Log("Configured " + guids.Length + " models");
        }

        // ================================================================== fonts

        public sealed class FontSet
        {
            public Font Body, Bold, Title;
            public TMP_FontAsset Tmp;
            public TMP_FontAsset TmpTitle;
        }

        public sealed class RerunRequiredException : Exception { }

        static bool ImportTmpEssentials()
        {
            if (AssetDatabase.LoadAssetAtPath<TMP_Settings>("Assets/TextMesh Pro/Resources/TMP Settings.asset") != null) return false;
            string pkg = Directory.GetFiles(Path.GetFullPath("Library/PackageCache"), "TMP Essential Resources.unitypackage", SearchOption.AllDirectories).FirstOrDefault();
            if (pkg == null) { Log("TMP essentials package not found"); return false; }
            AssetDatabase.ImportPackage(pkg, false);
            AssetDatabase.Refresh();
            Log("Imported TMP essentials from " + pkg);
            return AssetDatabase.LoadAssetAtPath<TMP_Settings>("Assets/TextMesh Pro/Resources/TMP Settings.asset") == null;
        }

        static FontSet CreateFonts()
        {
            var fs = new FontSet
            {
                Body = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/Nunito-Regular.ttf"),
                Bold = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/Nunito-ExtraBold.ttf"),
                Title = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/Baloo2-ExtraBold.ttf"),
            };
            fs.Tmp = TmpFont(fs.Bold, GenDir + "/Nunito-ExtraBold SDF.asset");
            fs.TmpTitle = TmpFont(fs.Title, GenDir + "/Baloo2-ExtraBold SDF.asset");
            var settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>("Assets/TextMesh Pro/Resources/TMP Settings.asset");
            if (settings != null && fs.Tmp != null)
            {
                var so = new SerializedObject(settings);
                var def = so.FindProperty("m_defaultFontAsset");
                if (def != null) def.objectReferenceValue = fs.Tmp;
                var fb = so.FindProperty("m_fallbackFontAssets");
                if (fb != null && fb.isArray) { fb.arraySize = 1; fb.GetArrayElementAtIndex(0).objectReferenceValue = fs.Tmp; }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            Log("Fonts ready");
            return fs;
        }

        static TMP_FontAsset TmpFont(Font font, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null) return existing;
            if (font == null) return null;
            var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            fa.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(fa, path);
            fa.material.name = fa.name + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            foreach (var tex in fa.atlasTextures)
            {
                tex.name = fa.name + " Atlas";
                AssetDatabase.AddObjectToAsset(tex, fa);
            }
            // Pre-populate the glyphs we always need (Turkish + digits) so the build never misses them.
            fa.TryAddCharacters("ABCÇDEFGĞHIİJKLMNOÖPRSŞTUÜVYZQWXabcçdefgğhıijklmnoöprsştuüvyzqwx0123456789!?.,:;-+()'\"/%&*#@ÂâÎîÛû");
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            return fa;
        }

        // ================================================================== streaming assets

        static void WriteStreamingAssets()
        {
            string cfgPath = "Assets/StreamingAssets/Config/match_config.json";
            // (Re)write the balance file when it is missing or was written for another protocol version.
            bool current = false;
            if (File.Exists(cfgPath))
            {
                try { var c = Newtonsoft.Json.JsonConvert.DeserializeObject<MatchConfig>(File.ReadAllText(cfgPath), GameConfig.JsonSettings); current = c != null && c.ConfigVersion == Protocol.Version; }
                catch (Exception) { current = false; }
            }
            if (!current) { File.WriteAllText(cfgPath, GameConfig.ToJson(MatchConfig.CreateDefault())); Log("match_config.json (re)written for protocol " + Protocol.Version); }
            string id = DateTime.UtcNow.ToString("yyyyMMddHHmm") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            File.WriteAllText(Root + "/Resources/build_id.txt", id);
            if (!File.Exists("Assets/StreamingAssets/Localization/tr.json")) Log("WARNING: tr.json missing");
            string theme = Root + "/UI/RuntimeTheme.tss";
            if (!File.Exists(theme)) File.WriteAllText(theme, "@import url(\"unity-theme://default\");\nVisualElement {}\n");
            AssetDatabase.Refresh();
            Log("StreamingAssets written, build id " + id);
        }

        // ================================================================== player build

        public static bool BuildPlayer()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            string out_ = Path.GetFullPath(Path.Combine(BuildDir, ExeName));
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = out_,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            Log("Player build " + s.result + " size=" + (s.totalSize / (1024 * 1024)) + "MB errors=" + s.totalErrors + " -> " + out_);
            return s.result == BuildResult.Succeeded;
        }
    }
}
