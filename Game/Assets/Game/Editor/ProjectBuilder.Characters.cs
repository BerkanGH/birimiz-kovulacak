using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BirimizKovulacak.Core;
using BirimizKovulacak.Game;
using Newtonsoft.Json.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BirimizKovulacak.EditorTools
{
    /// <summary>
    /// Characters: imports Employee.fbx / Manager.fbx / FaceAtlas.png (Art/Scripts/build_characters.py), creates the
    /// character materials (Art/palette_characters.json) and face-cell materials, the animator controllers, the
    /// network PlayerAvatar prefab (6 looks), the Manager prefab (4 profile looks + classic) and visual-only look
    /// prefabs under Prefabs/Characters. Look data (accessories, material swaps, silhouettes, faces, clip loop flags)
    /// comes from Art/character_looks.json, written by the Blender script, so art and code cannot drift apart.
    /// </summary>
    public static partial class ProjectBuilder
    {
        const string CharModelDir = ModelDir + "/Characters";
        const string CharGenDir = GenDir + "/Characters";
        const string CharPrefabDir = PrefabDir + "/Characters";
        const string CharLooksJson = "../Art/character_looks.json";
        const string CharPaletteJson = "../Art/palette_characters.json";
        const int FaceCells = 8;

        static JObject _charLooks;

        static JObject CharLooks
        {
            get { return _charLooks ?? (_charLooks = JObject.Parse(File.ReadAllText(Path.GetFullPath(CharLooksJson)))); }
        }

        /// <summary>Called from CreatePrefabs: fills set.Avatar and set.Manager.</summary>
        static void CreateCharacterPrefabs(PrefabSet set, FontSet fonts)
        {
            _charLooks = null;
            Directory.CreateDirectory(CharGenDir);
            Directory.CreateDirectory(CharPrefabDir);
            AssetDatabase.Refresh();
            CreateCharacterMaterials();
            ConfigureCharacterImports();
            var empModel = Model("Characters", "Employee");
            var mgrModel = Model("Characters", "Manager");
            if (empModel == null || mgrModel == null) throw new Exception("Character FBX missing: run Blender build_characters.py (Tools/build.ps1 -Art)");
            var empCtrl = BuildEmployeeController(empModel);
            var mgrCtrl = BuildManagerController(mgrModel);
            set.Avatar = BuildPlayerAvatarPrefab(empModel, empCtrl, fonts);
            set.Manager = BuildManagerPrefab(mgrModel, mgrCtrl, fonts);
            BuildLookPrefabs(empModel, empCtrl, mgrModel, mgrCtrl);
            AssetDatabase.SaveAssets();
            Log("Characters: avatar (" + ((JArray)CharLooks["players"]).Count + " looks), manager (" + ((JArray)CharLooks["managers"]).Count + " looks)");
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
            // Speed-driven locomotion families (m/s; PlayerAvatar.WalkSpeed 3.6, crouch ~1.7)
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

            // Upper-body phone layer (right arm + head), weight driven by PlayerAvatar.
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

        static void WireFace(GameObject inst, FaceRig face)
        {
            var smrs = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            face.Eyes = smrs.FirstOrDefault(r => r.name == "FaceEyes");
            face.Mouth = smrs.FirstOrDefault(r => r.name == "FaceMouth");
            face.EyeCells = Enumerable.Range(0, FaceCells).Select(i => Mat("M_FaceEyes_" + i)).ToArray();
            face.MouthCells = Enumerable.Range(0, FaceCells).Select(i => Mat("M_FaceMouth_" + i)).ToArray();
        }

        static void WireAppearance(GameObject inst, CharacterAppearance app, JArray looks, JObject groups)
        {
            var smrs = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var byName = new Dictionary<string, GameObject>();
            foreach (var r in smrs) byName[r.name] = r.gameObject;
            var lookAcc = new HashSet<string>();
            foreach (var l in looks) foreach (var a in (JArray)l["accessories"]) lookAcc.Add((string)a);
            app.Renderers = smrs.Where(r => !r.name.StartsWith("Face")).Cast<Renderer>().ToArray();
            app.AllAccessories = smrs.Where(r => r.name.StartsWith("Acc_") || lookAcc.Contains(r.name)).Select(r => r.gameObject).ToArray();
            var groupNames = new List<string>();
            var groupList = new List<MaterialGroup>();
            foreach (var g in groups)
            {
                groupNames.Add(g.Key);
                groupList.Add(new MaterialGroup { Name = g.Key, Members = ((JArray)g.Value).Select(n => Mat((string)n)).Where(m => m != null).ToArray() });
            }
            app.Groups = groupList.ToArray();
            var result = new List<CharacterLook>();
            foreach (var l in looks)
            {
                var look = new CharacterLook { Id = (string)l["id"] };
                look.Accessories = ((JArray)l["accessories"]).Select(a => (string)a).Where(byName.ContainsKey).Select(a => byName[a]).ToArray();
                var swaps = (JObject)l["swaps"];
                look.Swaps = groupNames.Select(gn => swaps != null && swaps[gn] != null ? Mat((string)swaps[gn]) : null).ToArray();
                var sc = (JArray)l["scale"];
                look.Scale = sc != null && sc.Count == 3 ? new Vector3((float)sc[0], (float)sc[1], (float)sc[2]) : Vector3.one;
                look.Tilt = (float?)l["tilt"] ?? 0f;
                FaceExpression fe;
                look.Face = Enum.TryParse((string)l["face"] ?? "Neutral", out fe) ? fe : FaceExpression.Neutral;
                look.IdleState = (string)l["idle"] ?? "Idle";
                result.Add(look);
            }
            app.Looks = result.ToArray();
        }

        static Transform Mount(Transform bone, string name, Vector3 worldPos, Quaternion worldRot)
        {
            var t = new GameObject(name).transform;
            t.SetParent(bone, false);
            t.SetPositionAndRotation(worldPos, worldRot);
            return t;
        }

        static GameObject ChildByName(GameObject inst, string name)
        {
            var t = FindDeep(inst.transform, name);
            return t != null ? t.gameObject : null;
        }

        // ================================================================== player avatar (network)

        static GameObject BuildPlayerAvatarPrefab(GameObject model, AnimatorController ctrl, FontSet fonts)
        {
            var root = new GameObject("PlayerAvatar");
            root.layer = Layers.Avatar;
            root.AddComponent<NetworkObject>();
            var nt = root.AddComponent<OwnerNetworkTransform>();
            nt.SyncPositionX = nt.SyncPositionY = nt.SyncPositionZ = true;
            nt.SyncRotAngleX = false; nt.SyncRotAngleY = true; nt.SyncRotAngleZ = false;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.Interpolate = true;
            var cc = root.AddComponent<CharacterController>();
            cc.radius = 0.33f; cc.height = 1.2f; cc.center = new Vector3(0, 0.62f, 0); cc.stepOffset = 0.25f; cc.skinWidth = 0.03f;
            var avatar = root.AddComponent<PlayerAvatar>();
            var modelRoot = new GameObject("ModelRoot").transform;
            modelRoot.SetParent(root.transform, false);
            var inst = Inst(model, modelRoot, Vector3.zero, 0, "Employee");
            avatar.Animator = SetupCharacterAnimator(inst, ctrl);
            avatar.ModelRoot = modelRoot;
            SetupCharacterRenderers(inst, 3.4f);
            WireAppearance(inst, avatar.Appearance, (JArray)CharLooks["players"], (JObject)CharLooks["playerGroups"]);
            WireFace(inst, avatar.Face);
            avatar.PropHandset = ChildByName(inst, "Prop_Handset");
            avatar.PropMobile = ChildByName(inst, "Prop_Mobile");
            avatar.PropBox = ChildByName(inst, "Prop_Box");
            foreach (var p in new[] { avatar.PropHandset, avatar.PropMobile, avatar.PropBox }) if (p != null) p.SetActive(false);

            // Mounts on bones (model is in its rest pose here). Unity axes: x right, y up, z forward.
            var body = FindDeep(inst.transform, "Body");
            var handR = FindDeep(inst.transform, "Hand.R");
            var chestPos = new Vector3(0f, 0.53f, 0.31f);
            if (body != null) avatar.ChestMount = Mount(body, "ChestMount", chestPos, Quaternion.identity);
            if (handR != null) avatar.HandSocket = Mount(handR, "HandMount", new Vector3(0.455f, 0.17f, 0.07f), Quaternion.identity);
            var carry = new GameObject("CarrySocket").transform;
            carry.SetParent(root.transform, false);
            carry.localPosition = chestPos;
            avatar.CarrySocket = carry;

            var clips = CharClips(model);
            var oneShots = Enum.GetNames(typeof(PlayerAvatar.OneShot));
            avatar.OneShotSeconds = oneShots.Select(n => { AnimationClip c; return clips.TryGetValue(n, out c) ? c.length : 1.5f; }).ToArray();

            var tag = Label3D(root.transform, "NameTag", new Vector3(0, 1.62f, 0), Vector3.zero, new Vector2(1.6f, 0.26f), fonts.Tmp, Color.white, "", 2.2f);
            tag.outlineWidth = 0.25f;
            tag.outlineColor = new Color32(30, 24, 60, 255);
            avatar.NameTag = tag;
            avatar.Appearance.Apply(0);
            SetLayerRecursive(root, Layers.Avatar);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "/PlayerAvatar.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ================================================================== manager

        static GameObject BuildManagerPrefab(GameObject model, AnimatorController ctrl, FontSet fonts)
        {
            var root = new GameObject("Manager");
            var mv = root.AddComponent<ManagerVisual>();
            var modelRoot = new GameObject("ModelRoot").transform;
            modelRoot.SetParent(root.transform, false);
            var inst = Inst(model, modelRoot, Vector3.zero, 0, "ManagerModel");
            mv.Animator = SetupCharacterAnimator(inst, ctrl);
            mv.ModelRoot = modelRoot;
            SetupCharacterRenderers(inst, 4.0f);
            WireAppearance(inst, mv.Appearance, (JArray)CharLooks["managers"], (JObject)CharLooks["managerGroups"]);
            WireFace(inst, mv.Face);
            mv.PropClipboard = ChildByName(inst, "Prop_Clipboard");
            mv.Appearance.Apply(Math.Max(0, mv.Appearance.IndexOf("default")));

            var bubble = new GameObject("Bubble");
            bubble.transform.SetParent(root.transform, false);
            bubble.transform.localPosition = new Vector3(0, 2.05f, 0);
            Quad(bubble.transform, "Bg", new Vector3(0, 0, 0.01f), Vector3.zero, new Vector3(2.3f, 0.5f, 1), Mat("M_BubbleBg"));
            mv.Bubble = Label3D(bubble.transform, "Text", Vector3.zero, Vector3.zero, new Vector2(2.2f, 0.42f), fonts.Tmp, new Color(0.7f, 0.1f, 0.1f), "!", 2.5f);
            mv.BubbleRoot = bubble;
            var cone = new GameObject("VisionCone");
            cone.transform.SetParent(root.transform, false);
            cone.transform.localPosition = new Vector3(0, 0.03f, 0);
            mv.ConeFilter = cone.AddComponent<MeshFilter>();
            mv.ConeRenderer = cone.AddComponent<MeshRenderer>();
            mv.ConeRenderer.sharedMaterial = Mat("M_VisionCone");
            mv.ConeRenderer.shadowCastingMode = ShadowCastingMode.Off;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "/Manager.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ================================================================== visual-only look prefabs (menus, lobby, results)

        static void BuildLookPrefabs(GameObject empModel, AnimatorController empCtrl, GameObject mgrModel, AnimatorController mgrCtrl)
        {
            int n = 0;
            foreach (var l in (JArray)CharLooks["players"])
                n += LookPrefab(empModel, empCtrl, (JArray)CharLooks["players"], (JObject)CharLooks["playerGroups"], (string)l["id"], "Player_", 3.4f) ? 1 : 0;
            foreach (var l in (JArray)CharLooks["managers"])
                n += LookPrefab(mgrModel, mgrCtrl, (JArray)CharLooks["managers"], (JObject)CharLooks["managerGroups"], (string)l["id"], "Manager_", 4.0f) ? 1 : 0;
            Log("Character look prefabs: " + n);
        }

        /// <summary>Unpacked model with only this look's meshes (unused accessories/props removed), resting face, animator.</summary>
        static bool LookPrefab(GameObject model, AnimatorController ctrl, JArray looks, JObject groups, string id, string prefix, float bounds)
        {
            var root = new GameObject(prefix + id);
            var modelRoot = new GameObject("ModelRoot").transform;
            modelRoot.SetParent(root.transform, false);
            var inst = Inst(model, modelRoot, Vector3.zero, 0, "Model");
            if (PrefabUtility.IsPartOfPrefabInstance(inst)) PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            SetupCharacterAnimator(inst, ctrl);
            SetupCharacterRenderers(inst, bounds);
            var app = new CharacterAppearance();
            WireAppearance(inst, app, looks, groups);
            int idx = app.IndexOf(id);
            var look = app.Apply(idx);
            if (look != null)
            {
                modelRoot.localScale = look.Scale;
                modelRoot.localRotation = Quaternion.Euler(look.Tilt, 0, 0);
                var face = new FaceRig();
                WireFace(inst, face);
                face.SetBase(look.Face);
                face.Show(FaceExpression.Neutral, false, -1f);
            }
            foreach (var r in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (!r.gameObject.activeSelf || (r.name.StartsWith("Prop_") && (look == null || Array.IndexOf(look.Accessories, r.gameObject) < 0)))
                    Object.DestroyImmediate(r.gameObject);
            PrefabUtility.SaveAsPrefabAsset(root, CharPrefabDir + "/" + prefix + id + ".prefab");
            Object.DestroyImmediate(root);
            return look != null;
        }
    }
}
