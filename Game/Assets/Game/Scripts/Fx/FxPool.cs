using UnityEngine;
using UnityEngine.Rendering;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// The one code-built particle effect left after v2: red ink flecks when a stamp lands on a meeting / verdict card
    /// (<see cref="StampBurstAtScreen"/>, through <see cref="FeelHooks.ResultsStamp"/>). Two pooled ParticleSystems
    /// created at startup; playing never allocates. The material is cloned from a particle material in the scene when
    /// there is one, otherwise built from the URP particle shader (Sprites/Default as the last resort).
    /// </summary>
    [DefaultExecutionOrder(600)]
    public sealed class FxPool : MonoBehaviour
    {
        public static FxPool I { get; private set; }

        const int PoolSize = 2;
        readonly ParticleSystem[] _pool = new ParticleSystem[PoolSize];
        int _cursor;
        Material _soft;
        bool _built;

        void Awake() { I = this; }

        void Start() { Build(); }

        /// <summary>Burst in front of the camera at a screen position (pixels, origin bottom-left).</summary>
        public static void StampBurstAtScreen(Vector2 screen)
        {
            var p = I;
            var cam = Camera.main;
            if (p == null || !p._built || cam == null) return;
            Vector3 w = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 1.4f));
            var ps = p._pool[p._cursor];
            p._cursor = (p._cursor + 1) % PoolSize;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.transform.SetPositionAndRotation(w, Quaternion.LookRotation(cam.transform.forward));
            ps.Play(true);
        }

        void Build()
        {
            if (_built) return;
            BuildMaterial();
            for (int i = 0; i < PoolSize; i++) _pool[i] = BuildStampBurst();
            _built = true;
        }

        void BuildMaterial()
        {
            Material template = null;
            var rs = FindObjectsByType<ParticleSystemRenderer>(FindObjectsInactive.Include);
            for (int i = 0; i < rs.Length && template == null; i++) if (rs[i].sharedMaterial != null) template = rs[i].sharedMaterial;
            _soft = template != null ? new Material(template) : CreateParticleMaterial();
            _soft.name = "M_FxSoft";
            if (_soft.HasProperty("_BaseColor")) _soft.SetColor("_BaseColor", Color.white);
            if (_soft.HasProperty("_Color")) _soft.SetColor("_Color", Color.white);
            var dot = MakeDot(32);
            if (_soft.HasProperty("_BaseMap")) _soft.SetTexture("_BaseMap", dot);
            if (_soft.HasProperty("_MainTex")) _soft.SetTexture("_MainTex", dot);
        }

        static Material CreateParticleMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var m = new Material(shader);
            if (m.HasProperty("_Surface"))
            {
                m.SetFloat("_Surface", 1);
                m.SetFloat("_Blend", 0);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            return m;
        }

        static Texture2D MakeDot(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "FxDot", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    float a = Mathf.Clamp01(1f - d);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true); // no CPU copy kept
            return tex;
        }

        /// <summary>Red ink flecks: 26 particles, 0.3–0.5 s, shrinking and fading out.</summary>
        ParticleSystem BuildStampBurst()
        {
            var go = new GameObject("Fx_StampBurst");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 32;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
            main.startColor = new Color(0.78f, 0.1f, 0.12f, 1f);
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)26) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.02f;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(g);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = _soft;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.SetActive(true);
            return ps;
        }
    }

    /// <summary>Small hooks between the UI and the rest of the game.</summary>
    public static class FeelHooks
    {
        /// <summary>True while a UI text field has keyboard focus (push-to-talk and hotkeys stay quiet).</summary>
        public static bool TextInputFocused => UIRoot.I != null && UIRoot.I.TextInputFocused;

        /// <summary>UI: a stamp just landed at this screen position (pixels, origin bottom-left): thump + ink flecks.</summary>
        public static void ResultsStamp(Vector2 screenPosition)
        {
            Sfx.Play("stamp");
            FxPool.StampBurstAtScreen(screenPosition);
        }
    }
}
