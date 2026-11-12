using UnityEngine;
using UnityEngine.UIElements;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// A UI Toolkit panel rendered into a texture that replaces the material of a surface in the room (the cork board's
    /// suç tahtası, the Weekend Shift roster). The panel never takes the mouse; its material is a copy of the surface's
    /// own (same shader and smoothness) with the render texture as base map. <see cref="Release"/> restores the
    /// original material and frees the texture. <see cref="CreateOffscreen"/> (v5 step 3b, the projector) only renders
    /// the texture; the caller decides where it shows.
    /// </summary>
    public sealed class WorldPanel
    {
        Renderer _surface;
        Material _orig, _mat;
        RenderTexture _rt;
        GameObject _go;
        PanelSettings _ps;

        public VisualElement Root { get; private set; }
        public Renderer Surface => _surface;
        public Material Original => _orig;
        public RenderTexture Texture => _rt;
        /// <summary>The surface's live material (the render texture as base map); other surfaces may show it too.</summary>
        public Material LiveMaterial => _mat;
        public bool Live => Root != null;

        /// <summary>Builds the panel (w x h pixels) for <paramref name="surface"/>; <paramref name="baseMat"/> is the material
        /// whose look the live copy keeps (null = the surface's current one). Throws on failure (the caller disables itself).</summary>
        public VisualElement Create(Renderer surface, PanelSettings like, int w, int h, Color clear, string name, Material baseMat = null)
        {
            Release();
            _surface = surface;
            _orig = baseMat != null ? baseMat : surface.sharedMaterial;
            Build(like, w, h, clear, name);
            _mat = new Material(_orig) { name = "M_" + name + "Live" };
            if (_mat.HasProperty("_BaseMap")) _mat.SetTexture("_BaseMap", _rt);
            if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", Color.white);
            _mat.mainTexture = _rt;
            surface.sharedMaterial = _mat;
            return Root;
        }

        /// <summary>Only the panel and its render texture (<see cref="Texture"/>); no surface material is touched.</summary>
        public VisualElement CreateOffscreen(PanelSettings like, int w, int h, Color clear, string name)
        {
            Release();
            Build(like, w, h, clear, name);
            return Root;
        }

        void Build(PanelSettings like, int w, int h, Color clear, string name)
        {
            _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = name + "RT", useMipMap = true, autoGenerateMips = true, filterMode = FilterMode.Trilinear, anisoLevel = 4,
            };
            _rt.Create();
            _ps = ScriptableObject.CreateInstance<PanelSettings>();
            _ps.name = name + "Panel";
            _ps.targetTexture = _rt;
            _ps.scaleMode = PanelScaleMode.ConstantPixelSize;
            _ps.scale = 1f;
            _ps.clearColor = true;
            _ps.colorClearValue = clear;
            _ps.sortingOrder = -50;
            if (like != null) _ps.themeStyleSheet = like.themeStyleSheet;
            _ps.SetScreenToPanelSpaceFunction(_ => new Vector2(float.NaN, float.NaN)); // never takes the mouse
            _go = new GameObject(name + "Panel");
            _go.SetActive(false);
            Object.DontDestroyOnLoad(_go);
            var doc = _go.AddComponent<UIDocument>();
            doc.panelSettings = _ps;
            _go.SetActive(true);
            Root = doc.rootVisualElement;
            Root.pickingMode = PickingMode.Ignore;
            var sheet = Resources.Load<StyleSheet>("ui/styles/office");
            if (sheet != null) Root.styleSheets.Add(sheet);
            Root.style.width = w; Root.style.height = h;
        }

        /// <summary>Puts the live material back if something else restored the surface's own.</summary>
        public void Keep() { if (_surface != null && _mat != null && _surface.sharedMaterial != _mat) _surface.sharedMaterial = _mat; }

        public void Release()
        {
            if (_surface != null && _orig != null && _surface.sharedMaterial == _mat) _surface.sharedMaterial = _orig;
            if (_go != null) Object.Destroy(_go);
            if (_ps != null) Object.Destroy(_ps);
            if (_mat != null) Object.Destroy(_mat);
            if (_rt != null) { _rt.Release(); Object.Destroy(_rt); }
            _go = null; _ps = null; _mat = null; _rt = null; Root = null; _surface = null; _orig = null;
        }
    }
}
