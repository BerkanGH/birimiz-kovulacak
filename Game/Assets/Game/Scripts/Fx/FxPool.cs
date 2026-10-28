using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace BirimizKovulacak.Game
{
    public enum FxKind
    {
        CoffeeSplash = 0,
        Sparks = 1,
        DustPuff = 2,
        Confetti = 3,
        StampBurst = 4,
    }

    /// <summary>
    /// Code-built, pooled particle effects (no prefabs): one-shot bursts (<see cref="Play"/>), two attached emitters
    /// (sweat over the local avatar, salt sparkle on a mug only its salter/taster knows about), "!" pops over the
    /// manager, and one shared flash light for sparks. 11 ParticleSystems + 3 TextMeshPro objects + 1 light in total,
    /// created at startup; playing an effect never allocates. The particle material is cloned from a particle material
    /// already in the scene (coffee steam), so the URP particle shader is guaranteed to be in the build.
    /// </summary>
    [DefaultExecutionOrder(600)]
    public sealed class FxPool : MonoBehaviour
    {
        public static FxPool I { get; private set; }

        static readonly int[] PoolSizes = { 2, 3, 2, 2, 2 };

        ParticleSystem[][] _pools;
        int[] _cursor;
        ParticleSystem _sweat, _salt;
        Material _soft, _hard;
        Light _flash;
        float _flashStart = -10f, _flashDuration, _flashPeak;

        sealed class Pop
        {
            public TextMeshPro Text;
            public Vector3 Base;
            public float Start;
            public bool Active;
        }
        readonly Pop[] _pops = new Pop[3];
        int _popNext;
        bool _built;

        void Awake() { I = this; }

        void Start() { Build(); }

        // ------------------------------------------------------------------ API

        public static void Play(FxKind kind, Vector3 position) { Play(kind, position, Quaternion.identity); }

        public static void Play(FxKind kind, Vector3 position, Quaternion rotation)
        {
            var p = I;
            if (p == null || !p._built) return;
            int k = (int)kind;
            var pool = p._pools[k];
            var ps = pool[p._cursor[k]];
            p._cursor[k] = (p._cursor[k] + 1) % pool.Length;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.transform.SetPositionAndRotation(position, rotation);
            ps.Play(true);
            if (kind == FxKind.Sparks) p.Flash(position, new Color(1f, 0.85f, 0.45f), 3.5f, 0.14f);
        }

        /// <summary>Burst in front of the camera at a screen position (pixels, origin bottom-left).</summary>
        public static void StampBurstAtScreen(Vector2 screen)
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 w = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 1.4f));
            Play(FxKind.StampBurst, w, Quaternion.LookRotation(cam.transform.forward));
        }

        /// <summary>Confetti from both lower screen corners (results).</summary>
        public static void ConfettiAtScreenEdges()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 l = cam.ScreenToWorldPoint(new Vector3(Screen.width * 0.08f, Screen.height * 0.1f, 3f));
            Vector3 r = cam.ScreenToWorldPoint(new Vector3(Screen.width * 0.92f, Screen.height * 0.1f, 3f));
            Vector3 up = cam.transform.up;
            Play(FxKind.Confetti, l, Quaternion.LookRotation(up + cam.transform.right * 0.45f));
            Play(FxKind.Confetti, r, Quaternion.LookRotation(up - cam.transform.right * 0.45f));
        }

        /// <summary>Local-only nervous sweat drops above a point (call every frame while it applies).</summary>
        public static void SetSweat(bool on, Vector3 headPosition)
        {
            var p = I;
            if (p == null || !p._built) return;
            SetEmitter(p._sweat, on, headPosition);
        }

        /// <summary>Glitter on a salted mug (only ever called for mugs this client knows are salted).</summary>
        public static void SetSaltSparkle(bool on, Vector3 position)
        {
            var p = I;
            if (p == null || !p._built) return;
            SetEmitter(p._salt, on, position);
        }

        static void SetEmitter(ParticleSystem ps, bool on, Vector3 pos)
        {
            var em = ps.emission;
            if (on) ps.transform.position = pos;
            if (em.enabled != on) em.enabled = on;
            if (on && !ps.isPlaying) ps.Play(true);
        }

        /// <summary>A "!" that pops above a world point (manager reaction).</summary>
        public static void Exclaim(Vector3 position)
        {
            var p = I;
            if (p == null || !p._built || p._pops[0] == null) return;
            var pop = p._pops[p._popNext];
            p._popNext = (p._popNext + 1) % p._pops.Length;
            pop.Base = position;
            pop.Start = Time.time;
            pop.Active = true;
            pop.Text.gameObject.SetActive(true);
        }

        public void Flash(Vector3 position, Color color, float intensity, float duration)
        {
            if (_flash == null) return;
            _flash.transform.position = position;
            _flash.color = color;
            _flashPeak = intensity;
            _flashDuration = duration;
            _flashStart = Time.time;
            _flash.enabled = true;
        }

        // ------------------------------------------------------------------ per frame

        void LateUpdate()
        {
            if (!_built) return;
            if (_flash != null && _flash.enabled)
            {
                float t = (Time.time - _flashStart) / Mathf.Max(0.01f, _flashDuration);
                if (t >= 1f) _flash.enabled = false;
                else _flash.intensity = _flashPeak * (1f - t) * (1f - t);
            }
            var cam = Camera.main;
            for (int i = 0; i < _pops.Length; i++)
            {
                var pop = _pops[i];
                if (pop == null || !pop.Active) continue;
                float t = Time.time - pop.Start;
                if (t > 1.3f) { pop.Active = false; pop.Text.gameObject.SetActive(false); continue; }
                float s = t < 0.12f ? Mathf.Lerp(0f, 1.35f, t / 0.12f) : t < 0.22f ? Mathf.Lerp(1.35f, 1f, (t - 0.12f) / 0.1f) : 1f;
                var tr = pop.Text.transform;
                tr.position = pop.Base + Vector3.up * (t * 0.25f);
                tr.localScale = Vector3.one * s;
                if (cam != null) tr.rotation = Quaternion.LookRotation(tr.position - cam.transform.position);
                pop.Text.alpha = t > 0.9f ? Mathf.Clamp01(1f - (t - 0.9f) / 0.4f) : 1f;
            }
        }

        // ------------------------------------------------------------------ building

        void Build()
        {
            if (_built) return;
            BuildMaterials();
            _pools = new ParticleSystem[PoolSizes.Length][];
            _cursor = new int[PoolSizes.Length];
            for (int k = 0; k < PoolSizes.Length; k++)
            {
                _pools[k] = new ParticleSystem[PoolSizes[k]];
                for (int i = 0; i < PoolSizes[k]; i++) _pools[k][i] = BuildKind((FxKind)k);
            }
            _sweat = BuildSweat();
            _salt = BuildSalt();
            BuildPops();
            var lg = new GameObject("Fx_Flash");
            lg.transform.SetParent(transform, false);
            _flash = lg.AddComponent<Light>();
            _flash.type = LightType.Point;
            _flash.range = 3.5f;
            _flash.shadows = LightShadows.None;
            _flash.enabled = false;
            _built = true;
        }

        void BuildMaterials()
        {
            Material template = null;
            var rs = FindObjectsByType<ParticleSystemRenderer>(FindObjectsInactive.Include);
            for (int i = 0; i < rs.Length && template == null; i++) if (rs[i].sharedMaterial != null) template = rs[i].sharedMaterial;
            _soft = template != null ? new Material(template) : CreateParticleMaterial();
            _soft.name = "M_FxSoft";
            SetColor(_soft, Color.white);
            SetTexture(_soft, MakeDot(32));
            _hard = new Material(_soft) { name = "M_FxHard" };
            SetTexture(_hard, Texture2D.whiteTexture);
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

        static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        static void SetTexture(Material m, Texture t)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", t);
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

        ParticleSystem NewSystem(string name, Material mat, int maxParticles)
        {
            var go = new GameObject("Fx_" + name);
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxParticles;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.SetActive(true);
            return ps;
        }

        static void Burst(ParticleSystem ps, short count)
        {
            var em = ps.emission;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });
        }

        static ParticleSystem.MinMaxGradient FadeOut(Color c)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            return new ParticleSystem.MinMaxGradient(g);
        }

        static void ColorFade(ParticleSystem ps)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeOut(Color.white);
        }

        static void Shrink(ParticleSystem ps, float from, float to)
        {
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, from, 1f, to));
        }

        ParticleSystem BuildKind(FxKind kind)
        {
            ParticleSystem ps;
            ParticleSystem.MainModule main;
            ParticleSystem.ShapeModule shape;
            switch (kind)
            {
                case FxKind.CoffeeSplash:
                    ps = NewSystem("CoffeeSplash", _soft, 40);
                    main = ps.main;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.8f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
                    main.gravityModifier = 1.4f;
                    main.startColor = new Color(0.36f, 0.2f, 0.09f, 1f);
                    shape = ps.shape;
                    shape.shapeType = ParticleSystemShapeType.Hemisphere;
                    shape.radius = 0.05f;
                    shape.rotation = new Vector3(-90f, 0f, 0f);
                    Burst(ps, 22);
                    Shrink(ps, 1f, 0.4f);
                    ColorFade(ps);
                    return ps;
                case FxKind.Sparks:
                    ps = NewSystem("Sparks", _soft, 48);
                    main = ps.main;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.45f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(1.8f, 4.5f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
                    main.gravityModifier = 0.8f;
                    main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.7f), new Color(1f, 0.6f, 0.15f));
                    shape = ps.shape;
                    shape.shapeType = ParticleSystemShapeType.Sphere;
                    shape.radius = 0.02f;
                    Burst(ps, 24);
                    var sr = ps.GetComponent<ParticleSystemRenderer>();
                    sr.renderMode = ParticleSystemRenderMode.Stretch;
                    sr.velocityScale = 0.06f;
                    sr.lengthScale = 1f;
                    ColorFade(ps);
                    return ps;
                case FxKind.DustPuff:
                    ps = NewSystem("DustPuff", _soft, 24);
                    main = ps.main;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.0f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.3f);
                    main.gravityModifier = -0.04f;
                    main.startColor = new Color(0.78f, 0.73f, 0.64f, 0.55f);
                    shape = ps.shape;
                    shape.shapeType = ParticleSystemShapeType.Sphere;
                    shape.radius = 0.15f;
                    Burst(ps, 10);
                    Shrink(ps, 0.6f, 1.6f);
                    ColorFade(ps);
                    return ps;
                case FxKind.Confetti:
                    ps = NewSystem("Confetti", _hard, 120);
                    main = ps.main;
                    main.duration = 3f;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 6f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
                    main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                    main.gravityModifier = 0.5f;
                    var cg = new Gradient();
                    cg.SetKeys(new[]
                    {
                        new GradientColorKey(new Color(0.96f, 0.3f, 0.3f), 0f), new GradientColorKey(new Color(0.98f, 0.8f, 0.25f), 0.25f),
                        new GradientColorKey(new Color(0.3f, 0.75f, 0.45f), 0.5f), new GradientColorKey(new Color(0.3f, 0.55f, 0.95f), 0.75f),
                        new GradientColorKey(new Color(0.9f, 0.45f, 0.75f), 1f),
                    }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
                    main.startColor = new ParticleSystem.MinMaxGradient(cg) { mode = ParticleSystemGradientMode.RandomColor };
                    shape = ps.shape;
                    shape.shapeType = ParticleSystemShapeType.Cone;
                    shape.angle = 28f;
                    shape.radius = 0.05f;
                    Burst(ps, 70);
                    var lim = ps.limitVelocityOverLifetime;
                    lim.enabled = true;
                    lim.limit = 1.2f;
                    lim.dampen = 0.08f;
                    var rot = ps.rotationOverLifetime;
                    rot.enabled = true;
                    rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
                    ColorFade(ps);
                    return ps;
                default: // StampBurst: red ink flecks
                    ps = NewSystem("StampBurst", _soft, 32);
                    main = ps.main;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
                    main.startColor = new Color(0.78f, 0.1f, 0.12f, 1f);
                    shape = ps.shape;
                    shape.shapeType = ParticleSystemShapeType.Sphere;
                    shape.radius = 0.02f;
                    Burst(ps, 26);
                    Shrink(ps, 1f, 0.3f);
                    ColorFade(ps);
                    return ps;
            }
        }

        ParticleSystem BuildSweat()
        {
            var ps = NewSystem("Sweat", _soft, 24);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.06f);
            main.gravityModifier = 1.2f;
            main.startColor = new Color(0.55f, 0.8f, 1f, 0.9f);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 55f;
            shape.radius = 0.12f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var em = ps.emission;
            em.rateOverTime = 6f;
            em.enabled = false;
            ColorFade(ps);
            return ps;
        }

        ParticleSystem BuildSalt()
        {
            var ps = NewSystem("SaltSparkle", _soft, 24);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.03f, 0.15f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.045f);
            main.startColor = new ParticleSystem.MinMaxGradient(Color.white, new Color(0.7f, 0.95f, 1f));
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.08f;
            var em = ps.emission;
            em.rateOverTime = 10f;
            em.enabled = false;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0f)));
            return ps;
        }

        void BuildPops()
        {
            TMP_FontAsset font = null;
            var mgr = OfficeWorld.I != null ? OfficeWorld.I.Manager : null;
            if (mgr != null && mgr.Bubble != null) font = mgr.Bubble.font;
            if (font == null)
            {
                var any = FindAnyObjectByType<TextMeshPro>(FindObjectsInactive.Include);
                if (any != null) font = any.font;
            }
            if (font == null) font = TMP_Settings.defaultFontAsset;
            if (font == null) return;
            for (int i = 0; i < _pops.Length; i++)
            {
                var go = new GameObject("Fx_Exclaim" + i);
                go.transform.SetParent(transform, false);
                var t = go.AddComponent<TextMeshPro>();
                t.font = font;
                t.text = "!";
                t.fontSize = 7f;
                t.fontStyle = FontStyles.Bold;
                t.alignment = TextAlignmentOptions.Center;
                t.color = new Color(1f, 0.3f, 0.15f, 1f);
                t.rectTransform.sizeDelta = new Vector2(1f, 1.2f);
                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null) mr.shadowCastingMode = ShadowCastingMode.Off;
                go.SetActive(false);
                _pops[i] = new Pop { Text = t };
            }
        }
    }
}
