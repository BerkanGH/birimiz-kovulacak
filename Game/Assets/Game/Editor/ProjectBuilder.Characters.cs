using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BirimizKovulacak.EditorTools
{
    /// <summary>
    /// Characters: imports Employee.fbx / Manager.fbx / FaceAtlas.png (Art/Scripts/build_characters.py), creates the
    /// character materials (Art/palette_characters.json), the face-cell materials and the animator controllers. The
    /// seated player looks and the managers of the meeting room are built from them in ProjectBuilder.Rooms.cs. Look
    /// data (accessories, material swaps, silhouettes, faces, clip loop flags) comes from Art/character_looks.json,
    /// written by the Blender script, so art and code cannot drift apart.
    /// </summary>
    public static partial class ProjectBuilder
    {
        const string CharModelDir = ModelDir + "/Characters";
        const string CharGenDir = GenDir + "/Characters";
        const string CharLooksJson = "../Art/character_looks.json";
        const string CharPaletteJson = "../Art/palette_characters.json";
        const int FaceCells = 8;

        static JObject _charLooks;

        static JObject CharLooks
        {
            get { return _charLooks ?? (_charLooks = JObject.Parse(File.ReadAllText(Path.GetFullPath(CharLooksJson)))); }
        }

        /// <summary>Called from CreatePrefabs: materials, model imports and the two animator controllers.</summary>
        static void CreateCharacterAssets()
        {
            _charLooks = null;
            Directory.CreateDirectory(CharGenDir);
            AssetDatabase.Refresh();
            CreateCharacterMaterials();
            ConfigureCharacterImports();
            var empModel = Model("Characters", "Employee");
            var mgrModel = Model("Characters", "Manager");
            if (empModel == null || mgrModel == null) throw new Exception("Character FBX missing: run Blender build_characters.py (Tools/build.ps1 -Art)");
            BuildEmployeeController(empModel);
            BuildManagerController(mgrModel);
            AssetDatabase.SaveAssets();
            Log("Characters: " + ((JArray)CharLooks["players"]).Count + " player looks, " + ((JArray)CharLooks["managers"]).Count + " manager looks");
        }

        // ================================================================== materials

        static void CreateCharacterMaterials()
        {
            var json = JObject.Parse(File.ReadAllText(Path.GetFullPath(CharPaletteJson)));
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            foreach (var prop in (JObject)json["materials"])
            {
                var spec = (JObject)prop.Value;
                if ((bool?)spec["unlit"] == true)
                {
                    CreateUnlit(prop.Key, unlit, Hex((string)spec["color"]));
                    var m = Mat(prop.Key);
                    m.SetShaderPassEnabled("ShadowCaster", false); // outline hulls never cast shadows
                    EditorUtility.SetDirty(m);
                }
                else CreateLit(prop.Key, lit, Hex((string)spec["color"]), (float?)spec["smooth"] ?? 0.3f, (float?)spec["metal"] ?? 0f, (float?)spec["emit"] ?? 0f, (float?)spec["alpha"] ?? 1f);
            }
            ConfigureFaceAtlasImport();
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(CharModelDir + "/FaceAtlas.png");
            if (atlas == null) Log("WARNING: FaceAtlas.png not imported yet");
            for (int i = 0; i < FaceCells; i++)
            {
                FaceCellMaterial("M_FaceEyes_" + i, lit, atlas, i);
                FaceCellMaterial("M_FaceMouth_" + i, lit, atlas, i);
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>Alpha-clipped Lit material showing one atlas cell (offset relative to the patch's neutral cell).</summary>
        static void FaceCellMaterial(string name, Shader lit, Texture2D atlas, int cell)
        {
            var m = GetOrCreate(name, lit);
            m.SetColor("_BaseColor", Color.white);
            if (atlas != null) m.SetTexture("_BaseMap", atlas);
            var offset = new Vector2((cell % 4) / 4f, -(cell / 4) / 4f);
            m.SetTextureScale("_BaseMap", Vector2.one);
            m.SetTextureOffset("_BaseMap", offset);
            m.SetFloat("_Smoothness", 0.35f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Surface", 0);
            m.SetFloat("_AlphaClip", 1);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_EMISSION");
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = (int)RenderQueue.AlphaTest;
            m.SetShaderPassEnabled("ShadowCaster", false);
            EditorUtility.SetDirty(m);
        }

        static void ConfigureFaceAtlasImport()
        {
            var ti = AssetImporter.GetAtPath(CharModelDir + "/FaceAtlas.png") as TextureImporter;
            if (ti == null) return;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = true;
            ti.mipMapsPreserveCoverage = true;   // keeps alpha-clipped eyes from thinning out at distance
            ti.alphaTestReferenceValue = 0.5f;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.anisoLevel = 2;
            ti.maxTextureSize = 512;
            ti.textureCompression = TextureImporterCompression.CompressedHQ; // BC7 on desktop
            ti.isReadable = false;
            ti.SaveAndReimport();
        }

        // ================================================================== model import

        static void ConfigureCharacterImports()
        {
            ConfigureCharacterModel(CharModelDir + "/Employee.fbx", (JArray)CharLooks["playerClips"]);
            ConfigureCharacterModel(CharModelDir + "/Manager.fbx", (JArray)CharLooks["managerClips"]);
        }

        /// <summary>
        /// Generic rig, clip names without the "Rig|" prefix, loop flags from character_looks.json, optimal keyframe
        /// compression, materials remapped by name. Public so ConfigureModelImports can delegate characters here.
        /// </summary>
        public static void ConfigureCharacterModel(string path, JArray clipList)
        {
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) { Log("Missing character model " + path); return; }
            var loops = new Dictionary<string, bool>();
            if (clipList != null) foreach (var c in clipList) loops[(string)c["name"]] = (bool)c["loop"];
            imp.globalScale = 1f;
            imp.useFileScale = true;
            imp.bakeAxisConversion = true;
            imp.importCameras = false;
            imp.importLights = false;
            imp.importBlendShapes = false;
            imp.isReadable = false;
            imp.meshCompression = ModelImporterMeshCompression.Low;
            imp.importNormals = ModelImporterNormals.Import;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
            imp.animationType = ModelImporterAnimationType.Generic;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.optimizeGameObjects = false; // bone transforms carry the carry/hand mounts
            imp.importAnimation = true;
            imp.animationCompression = ModelImporterAnimationCompression.Optimal;
            imp.animationRotationError = 0.5f;
            imp.animationPositionError = 0.5f;
            imp.animationScaleError = 0.5f;
            var clips = imp.defaultClipAnimations;
            foreach (var c in clips)
            {
                string n = c.name.Contains("|") ? c.name.Substring(c.name.LastIndexOf('|') + 1) : c.name;
                c.name = n;
                bool loop;
                c.loopTime = loops.TryGetValue(n, out loop) && loop;
                c.loopPose = false;
            }
            imp.clipAnimations = clips;
            imp.SaveAndReimport();
            imp.SearchAndRemapMaterials(ModelImporterMaterialName.BasedOnMaterialName, ModelImporterMaterialSearch.Everywhere);
            imp.SaveAndReimport();
        }

        static Dictionary<string, AnimationClip> CharClips(GameObject model)
        {
            var d = new Dictionary<string, AnimationClip>();
            foreach (var c in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(model)).OfType<AnimationClip>())
                if (!c.name.StartsWith("__preview") && !d.ContainsKey(c.name)) d[c.name] = c;
            return d;
        }

        // ================================================================== animator controllers

        static readonly string[] BlendedPlayerClips = { "Idle", "Walk", "Run", "CrouchIdle", "Sneak", "CarryIdle", "CarryWalk", "FiredIdle", "FiredWalk", "PhoneArm" };

        static AnimatorController BuildEmployeeController(GameObject model)
        {
            string path = CharGenDir + "/EmployeeAnimator.controller";
            AssetDatabase.DeleteAsset(path);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            var clips = CharClips(model);
            var sm = ctrl.layers[0].stateMachine;
            // Speed-driven locomotion families (m/s; walk 3.6, crouch ~1.7). The seated beans only play states by name.
            var loco = Blend1D(ctrl, "Locomotion", clips, new Vector3(260, 0), ("Idle", 0f), ("Walk", 1.4f), ("Run", 3.4f));
            Blend1D(ctrl, "CrouchLoco", clips, new Vector3(260, 70), ("CrouchIdle", 0f), ("Sneak", 1.7f));
            Blend1D(ctrl, "CarryLoco", clips, new Vector3(260, 140), ("CarryIdle", 0f), ("CarryWalk", 3.0f));
            Blend1D(ctrl, "FiredLoco", clips, new Vector3(260, 210), ("FiredIdle", 0f), ("FiredWalk", 1.3f));
            int i = 0;
            foreach (var kv in clips.OrderBy(k => k.Key))
            {
                if (BlendedPlayerClips.Contains(kv.Key)) continue;
                var st = sm.AddState(kv.Key, new Vector3(520 + (i % 4) * 220, (i / 4) * 60, 0));
                st.motion = kv.Value;
                st.writeDefaultValues = true;
                i++;
            }
            if (loco != null) sm.defaultState = loco;

            // Upper-body phone layer (right arm + head), weight 0 unless a script raises it.
            AnimationClip phoneArm;
            if (clips.TryGetValue("PhoneArm", out phoneArm))
            {
                var layer = new AnimatorControllerLayer
                {
                    name = "Phone",
                    defaultWeight = 0f,
                    blendingMode = AnimatorLayerBlendingMode.Override,
                    avatarMask = PhoneMask(model),
                    stateMachine = new AnimatorStateMachine { name = "Phone", hideFlags = HideFlags.HideInHierarchy },
                };
                AssetDatabase.AddObjectToAsset(layer.stateMachine, ctrl);
                var ps = layer.stateMachine.AddState("PhoneArm", new Vector3(260, 0, 0));
                ps.motion = phoneArm;
                ps.writeDefaultValues = false;
                layer.stateMachine.defaultState = ps;
                ctrl.AddLayer(layer);
            }
            EditorUtility.SetDirty(ctrl);
            Log("Animator " + path + ": " + clips.Count + " clips, layers " + ctrl.layers.Length);
            return ctrl;
        }

        static AnimatorState Blend1D(AnimatorController ctrl, string name, Dictionary<string, AnimationClip> clips, Vector3 pos, params (string clip, float threshold)[] children)
        {
            BlendTree tree;
            var st = ctrl.CreateBlendTreeInController(name, out tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            foreach (var c in children)
            {
                AnimationClip clip;
                if (clips.TryGetValue(c.clip, out clip)) tree.AddChild(clip, c.threshold);
                else Log("  missing clip " + c.clip + " for " + name);
            }
            st.writeDefaultValues = true;
            var sm = ctrl.layers[0].stateMachine;
            var states = sm.states;
            for (int i = 0; i < states.Length; i++)
                if (states[i].state == st) { states[i].position = pos; sm.states = states; break; }
            return st;
        }

        static AvatarMask PhoneMask(GameObject model)
        {
            string path = CharGenDir + "/PhoneArmMask.mask";
            AssetDatabase.DeleteAsset(path);
            var mask = new AvatarMask { name = "PhoneArmMask" };
            var all = model.GetComponentsInChildren<Transform>(true);
            var active = new HashSet<Transform>();
            foreach (var n in new[] { "Arm.R", "Head" })
            {
                var t = FindDeep(model.transform, n);
                if (t != null) foreach (var c in t.GetComponentsInChildren<Transform>(true)) active.Add(c);
            }
            mask.transformCount = all.Length;
            for (int i = 0; i < all.Length; i++)
            {
                mask.SetTransformPath(i, AnimationUtility.CalculateTransformPath(all[i], model.transform));
                mask.SetTransformActive(i, active.Contains(all[i]));
            }
            AssetDatabase.CreateAsset(mask, path);
            return mask;
        }

        static AnimatorController BuildManagerController(GameObject model)
        {
            string path = CharGenDir + "/ManagerAnimator.controller";
            AssetDatabase.DeleteAsset(path);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            var sm = ctrl.layers[0].stateMachine;
            var clips = CharClips(model);
            int i = 0;
            foreach (var kv in clips.OrderBy(k => k.Key))
            {
                var st = sm.AddState(kv.Key, new Vector3(260 + (i % 4) * 220, (i / 4) * 60, 0));
                st.motion = kv.Value;
                st.writeDefaultValues = true;
                if (kv.Key == "Idle") sm.defaultState = st;
                i++;
            }
            EditorUtility.SetDirty(ctrl);
            Log("Animator " + path + ": " + string.Join(",", clips.Keys));
            return ctrl;
        }

        // ================================================================== shared wiring

        static Animator SetupCharacterAnimator(GameObject inst, RuntimeAnimatorController ctrl)
        {
            var anim = inst.GetComponent<Animator>();
            if (anim == null) anim = inst.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            return anim;
        }

        /// <summary>Project materials by name on every renderer, face cell 0 on the face patches, cheap skinning settings.</summary>
        static SkinnedMeshRenderer[] SetupCharacterRenderers(GameObject inst, float boundsSize)
        {
            var smrs = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var r in smrs)
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var pm = Mat(mats[i].name);
                    if (pm != null) mats[i] = pm;
                }
                if (r.name == "FaceEyes") mats = new[] { Mat("M_FaceEyes_0") };
                else if (r.name == "FaceMouth") mats = new[] { Mat("M_FaceMouth_0") };
                r.sharedMaterials = mats;
                r.updateWhenOffscreen = false;
                r.localBounds = new Bounds(Vector3.zero, Vector3.one * boundsSize); // covers jumps / sitting in any bone orientation
                r.skinnedMotionVectors = false;
                bool face = r.name.StartsWith("Face") || r.name.StartsWith("Prop_Mobile");
                r.shadowCastingMode = face ? ShadowCastingMode.Off : ShadowCastingMode.On;
            }
            return smrs;
        }
    }
}
