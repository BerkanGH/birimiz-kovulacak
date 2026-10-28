using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Full-screen looks as runtime URP volumes layered over the scene's PostProcess volume (priority 10/11, weights
    /// driven here; a volume at weight 0 is disabled, so they cost nothing when idle):
    /// <list type="bullet">
    /// <item>Blackout (power cut): exposure −2.3, desaturated cold tint, heavy vignette; flickers on, fades back.</item>
    /// <item>CCTV (footage): grain, chromatic aberration, green-grey grade, barrel distortion. <see cref="Cctv"/> plays it for
    /// a few seconds (FeelDirector does this on FootageShown); <see cref="SetCctv"/> holds it (UI footage viewer).</item>
    /// </list>
    /// Note: UI Toolkit panels draw after post-processing, so these affect the 3D view only.
    /// </summary>
    public sealed class ScreenFx : MonoBehaviour
    {
        public static ScreenFx I { get; private set; }

        static readonly float[] Flicker = { 1f, 0.15f, 0.9f, 0.35f, 1f, 0.6f, 1f };
        const float FlickerStep = 0.05f;

        Volume _blackout, _cctv;
        bool _blackoutOn;
        float _blackoutSince = -10f;
        float _blackoutWeight;
        float _cctvUntil = -1f;
        bool _cctvHeld;
        float _cctvWeight;

        void Awake() { I = this; }

        void Start()
        {
            _blackout = MakeVolume("ScreenFx_Blackout", 10f, p =>
            {
                var v = p.Add<Vignette>(true);
                v.intensity.Override(0.55f);
                v.smoothness.Override(0.5f);
                v.color.Override(new Color(0.01f, 0.02f, 0.06f));
                var c = p.Add<ColorAdjustments>(true);
                c.postExposure.Override(-2.3f);
                c.saturation.Override(-45f);
                c.colorFilter.Override(new Color(0.72f, 0.8f, 1f));
            });
            _cctv = MakeVolume("ScreenFx_Cctv", 11f, p =>
            {
                var g = p.Add<FilmGrain>(true);
                g.type.Override(FilmGrainLookup.Medium4);
                g.intensity.Override(0.85f);
                g.response.Override(0.6f);
                var ca = p.Add<ChromaticAberration>(true);
                ca.intensity.Override(0.65f);
                var c = p.Add<ColorAdjustments>(true);
                c.saturation.Override(-75f);
                c.contrast.Override(25f);
                c.colorFilter.Override(new Color(0.82f, 1f, 0.85f));
                var ld = p.Add<LensDistortion>(true);
                ld.intensity.Override(-0.25f);
                var v = p.Add<Vignette>(true);
                v.intensity.Override(0.4f);
            });
        }

        Volume MakeVolume(string name, float priority, System.Action<VolumeProfile> fill)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = priority;
            vol.weight = 0f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = name;
            fill(profile);
            vol.sharedProfile = profile;
            vol.enabled = false;
            return vol;
        }

        // ------------------------------------------------------------------ API

        public static void SetBlackout(bool on)
        {
            var s = I;
            if (s == null || s._blackoutOn == on) return;
            s._blackoutOn = on;
            if (on) s._blackoutSince = Time.time;
        }

        /// <summary>CCTV look for <paramref name="seconds"/> (re-triggering extends it).</summary>
        public static void Cctv(float seconds)
        {
            var s = I;
            if (s == null) return;
            s._cctvUntil = Mathf.Max(s._cctvUntil, Time.time + seconds);
        }

        /// <summary>Hold the CCTV look while a footage viewer is open (UI hook).</summary>
        public static void SetCctv(bool on)
        {
            if (I != null) I._cctvHeld = on;
        }

        void Update()
        {
            if (_blackout == null) return;
            float dt = Time.unscaledDeltaTime;
            float since = Time.time - _blackoutSince;
            if (_blackoutOn && since < Flicker.Length * FlickerStep)
                _blackoutWeight = Flicker[Mathf.Clamp((int)(since / FlickerStep), 0, Flicker.Length - 1)];
            else _blackoutWeight = Mathf.MoveTowards(_blackoutWeight, _blackoutOn ? 1f : 0f, dt / (_blackoutOn ? 0.2f : 0.8f));
            Apply(_blackout, _blackoutWeight);

            bool cctv = _cctvHeld || Time.time < _cctvUntil;
            _cctvWeight = Mathf.MoveTowards(_cctvWeight, cctv ? 1f : 0f, dt / 0.15f);
            Apply(_cctv, _cctvWeight);
        }

        static void Apply(Volume v, float w)
        {
            bool on = w > 0.001f;
            if (v.enabled != on) v.enabled = on;
            if (on && Mathf.Abs(v.weight - w) > 0.0005f) v.weight = w;
        }

        void OnDestroy()
        {
            if (_blackout != null) Destroy(_blackout.sharedProfile);
            if (_cctv != null) Destroy(_cctv.sharedProfile);
        }
    }
}
