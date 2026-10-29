using BirimizKovulacak.Game;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace BirimizKovulacak.EditorTools
{
    /// <summary>Shared scene-building helpers (models, instances, 3D labels, primitives) and the network prefabs.</summary>
    public static partial class ProjectBuilder
    {
        public sealed class PrefabSet
        {
            public GameObject Session;
        }

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

        static PrefabSet CreatePrefabs()
        {
            var set = new PrefabSet();

            // ---------------- characters: materials, imports, animator controllers (ProjectBuilder.Characters.cs)
            CreateCharacterAssets();

            // ---------------- net session
            {
                var root = new GameObject("NetSession");
                root.AddComponent<NetworkObject>();
                root.AddComponent<NetSession>();
                set.Session = PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "/NetSession.prefab");
                Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            Log("Prefabs: session");
            return set;
        }
    }
}
