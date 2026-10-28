using System.Collections.Generic;
using System.Linq;
using BirimizKovulacak.Core;
using BirimizKovulacak.Game;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace BirimizKovulacak.EditorTools
{
    public static partial class ProjectBuilder
    {
        public sealed class PrefabSet
        {
            public GameObject Avatar;
            public GameObject Session;
            public GameObject Manager;
            public Dictionary<string, ItemVisual> Items = new Dictionary<string, ItemVisual>();
            public Mesh RingMesh;
        }

        static readonly Dictionary<string, string> ItemModels = new Dictionary<string, string>
        {
            { ItemVariants.Mug, "Mug" }, { ItemVariants.Report, "Report" }, { ItemVariants.Nameplate, "Nameplate" },
            { ItemVariants.Spray, "Spray" }, { ItemVariants.ClutterPapers, "ClutterPapers" }, { ItemVariants.ClutterBox, "ClutterBox" },
            { ItemVariants.Duck, "RubberDuck" }, { ItemVariants.Stapler, "Stapler" }, { ItemVariants.RivalCv, "RivalCv" },
            { ItemVariants.BossMug, "BossMug" }, { ItemVariants.SalaryList, "SalaryList" },
            { ItemVariants.Mail, "MailEnvelope" },
        };

        public static GameObject Model(string category, string name)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(ModelDir + "/" + category + "/" + name + ".fbx");
        }

        public static GameObject Inst(GameObject model, Transform parent, Vector3 localPos, float yaw = 0f, string name = null)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            if (go == null) go = Object.Instantiate(model, parent);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            if (name != null) go.name = name;
            return go;
        }

        public static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var r = FindDeep(c, name);
                if (r != null) return r;
            }
            return null;
        }

        static TextMeshPro Label3D(Transform parent, string name, Vector3 localPos, Vector3 euler, Vector2 size, TMP_FontAsset font, Color color, string text = "", float maxSize = 3f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshPro>();
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(euler);
            tmp.rectTransform.sizeDelta = size;
            if (font != null) { tmp.font = font; tmp.fontSharedMaterial = font.material; }
            tmp.text = text;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0.05f;
            tmp.fontSizeMax = maxSize;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return tmp;
        }

        static Mesh RingMesh()
        {
            string path = GenDir + "/RingMesh.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;
            var m = new Mesh { name = "Ring" };
            int seg = 32;
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                v.Add(new Vector3(Mathf.Cos(a) * 0.17f, 0, Mathf.Sin(a) * 0.17f));
                v.Add(new Vector3(Mathf.Cos(a) * 0.21f, 0, Mathf.Sin(a) * 0.21f));
            }
            for (int i = 0; i < seg; i++)
            {
                int a = i * 2;
                t.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
            }
            m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals();
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static GameObject Quad(Transform parent, string name, Vector3 pos, Vector3 euler, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        static GameObject Cube(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static PrefabSet CreatePrefabs(FontSet fonts)
        {
            var set = new PrefabSet { RingMesh = RingMesh() };

            // ---------------- items
            foreach (var kv in ItemModels)
            {
                var model = Model("Props", kv.Value);
                if (model == null) { Log("Missing item model " + kv.Value); continue; }
                var root = new GameObject("Item_" + kv.Key);
                var visual = root.AddComponent<ItemVisual>();
                visual.Variant = kv.Key;
                var inst = Inst(model, root.transform, Vector3.zero, 0, "Model");
                foreach (var r in inst.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                var coffee = FindDeep(inst.transform, "Coffee");
                if (coffee != null) visual.Coffee = coffee.gameObject;
                if (kv.Key == ItemVariants.Nameplate)
                {
                    var stripe = FindDeep(inst.transform, "Stripe");
                    if (stripe != null) visual.StripeRenderer = stripe.GetComponent<Renderer>();
                    var front = Label3D(root.transform, "LabelFront", new Vector3(0, 0.064f, -0.028f), new Vector3(26.6f, 0, 0), new Vector2(0.3f, 0.075f), fonts.Tmp, Color.white, "İSİM");
                    var back = Label3D(root.transform, "LabelBack", new Vector3(0, 0.064f, 0.028f), new Vector3(26.6f, 180, 0), new Vector2(0.3f, 0.075f), fonts.Tmp, Color.white, "İSİM");
                    visual.Labels = new[] { front, back };
                }
                if (kv.Key == ItemVariants.Mail)
                {
                    // Envelope stripe + name in the addressee's colour (ItemVisual resolves the desk owner).
                    var stripe = FindDeep(inst.transform, "Stripe");
                    if (stripe != null) visual.StripeRenderer = stripe.GetComponent<Renderer>();
                    var ml = Label3D(root.transform, "Label", new Vector3(-0.03f, 0.0185f, 0.02f), new Vector3(90, 0, 0), new Vector2(0.16f, 0.04f), fonts.Tmp, new Color(0.14f, 0.19f, 0.37f), "");
                    visual.Labels = new[] { ml };
                }
                if (kv.Key == ItemVariants.Report)
                {
                    var l = Label3D(root.transform, "Label", new Vector3(0, 0.0235f, 0.06f), new Vector3(90, 0, 0), new Vector2(0.14f, 0.05f), fonts.Tmp, new Color(0.15f, 0.12f, 0.3f), "Rapor");
                    visual.Labels = new[] { l };
                }
                var seal = Cube(root.transform, "SealTape", new Vector3(0, 0.06f, 0), new Vector3(0.32f, 0.02f, 0.05f), Mat("M_SealTape"));
                seal.transform.localRotation = Quaternion.Euler(0, 35, 0);
                seal.SetActive(false);
                visual.SealTape = seal;
                var ring = new GameObject("EvidenceRing");
                ring.transform.SetParent(root.transform, false);
                ring.transform.localPosition = new Vector3(0, 0.01f, 0);
                ring.AddComponent<MeshFilter>().sharedMesh = set.RingMesh;
                var rr = ring.AddComponent<MeshRenderer>();
                rr.sharedMaterial = Mat("M_EvidenceRing");
                rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ring.SetActive(false);
                visual.EvidenceRing = ring;
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "/Items/Item_" + kv.Key + ".prefab");
                Object.DestroyImmediate(root);
                set.Items[kv.Key] = prefab.GetComponent<ItemVisual>();
            }

            // ---------------- characters: player avatar + manager (ProjectBuilder.Characters.cs)
            CreateCharacterPrefabs(set, fonts);

            // ---------------- net session
            {
                var root = new GameObject("NetSession");
                root.AddComponent<NetworkObject>();
                root.AddComponent<NetSession>();
                set.Session = PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "/NetSession.prefab");
                Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            Log("Prefabs: " + set.Items.Count + " items + avatar, manager, session");
            return set;
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
        }
    }
}
