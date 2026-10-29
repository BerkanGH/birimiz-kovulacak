using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Light of the day for both rooms (0 = 09:00 morning, 0.5 = early afternoon, 1 = 18:00 dusk): the sun through the
    /// west windows (only shadowed light), trilight ambient, the procedural sky, distance fog over the skyline, lit
    /// windows across the street at dusk, warm interior lamps, a gentle grade. Realtime reflection probes are re-rendered
    /// when the light changed noticeably or the room switched. Cheap: values only change when the time changes.
    /// </summary>
    public sealed class RoomLighting : MonoBehaviour
    {
        public Light Sun;
        public Material Sky;
        public Material[] Facades = new Material[0];
        public Light[] Warm = new Light[0];
        public Light[] WindowFill = new Light[0];
        public ReflectionProbe[] Probes = new ReflectionProbe[0];
        public Volume Post;

        float[] _warmBase, _fillBase;
        float _t = -1f, _probeT = -10f;
        int _probeFrame = -1;
        ColorAdjustments _grade;
        WhiteBalance _wb;
        Material _sky;

        // keys: morning, early afternoon, dusk
        static readonly float[] Elev = { 24f, 33f, 8f };
        static readonly float[] Heading = { 118f, 92f, 62f };
        static readonly float[] SunI = { 2.0f, 2.2f, 1.75f };
        static readonly Color[] SunC = { C("#FFE6C2"), C("#FFF3E4"), C("#FF9A55") };
        static readonly Color[] AmbSky = { C("#B9CDE6"), C("#C8D7EA"), C("#8C88B0") };
        static readonly Color[] AmbEq = { C("#D7C9B3"), C("#DFD3BF"), C("#C48E72") };
        static readonly Color[] AmbGround = { C("#6A5847"), C("#715E4C"), C("#4A3634") };
        static readonly Color[] FogC = { C("#C3D4E8"), C("#D2DFED"), C("#E3A285") };
        static readonly Color[] SkyTint = { C("#7F8FA8"), C("#808C9C"), C("#A07F86") };
        static readonly float[] Atmos = { 0.85f, 0.8f, 1.65f };
        static readonly float[] SkyExp = { 1.25f, 1.3f, 1.0f };
        static readonly Color[] FillC = { C("#D3E3F7"), C("#E6EEF8"), C("#F4B68A") };
        static readonly float[] FillI = { 1.0f, 1.1f, 0.55f };
        static readonly float[] WarmK = { 0.85f, 0.75f, 1.25f };
        static readonly float[] GradeTemp = { -4f, 0f, 14f };

        static Color C(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        void Awake()
        {
            _warmBase = new float[Warm.Length];
            for (int i = 0; i < Warm.Length; i++) _warmBase[i] = Warm[i] != null ? Warm[i].intensity : 0f;
            _fillBase = new float[WindowFill.Length];
            for (int i = 0; i < WindowFill.Length; i++) _fillBase[i] = WindowFill[i] != null ? WindowFill[i].intensity : 0f;
            if (Sky != null)
            {
                _sky = new Material(Sky);
                RenderSettings.skybox = _sky;
            }
            if (Sun != null) RenderSettings.sun = Sun;
            if (Post != null)
            {
                var profile = Post.profile; // runtime copy
                profile.TryGet(out _grade);
                profile.TryGet(out _wb);
            }
            foreach (var p in Probes)
            {
                if (p == null) continue;
                p.mode = ReflectionProbeMode.Realtime;
                p.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
                p.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            }
        }

        static float Key(float[] k, float t) { return t < 0.5f ? Mathf.Lerp(k[0], k[1], t * 2f) : Mathf.Lerp(k[1], k[2], (t - 0.5f) * 2f); }
        static Color Key(Color[] k, float t) { return t < 0.5f ? Color.Lerp(k[0], k[1], t * 2f) : Color.Lerp(k[1], k[2], (t - 0.5f) * 2f); }

        public float TimeOfDay => _t;

        public void SetTime(float t01)
        {
            t01 = Mathf.Clamp01(t01);
            if (Mathf.Abs(t01 - _t) < 0.002f) return;
            _t = t01;
            float el = Key(Elev, t01), hd = Key(Heading, t01);
            Vector3 dir = new Vector3(Mathf.Sin(hd * Mathf.Deg2Rad) * Mathf.Cos(el * Mathf.Deg2Rad), -Mathf.Sin(el * Mathf.Deg2Rad), Mathf.Cos(hd * Mathf.Deg2Rad) * Mathf.Cos(el * Mathf.Deg2Rad));
            if (Sun != null)
            {
                Sun.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                Sun.color = Key(SunC, t01);
                Sun.intensity = Key(SunI, t01);
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Key(AmbSky, t01);
            RenderSettings.ambientEquatorColor = Key(AmbEq, t01);
            RenderSettings.ambientGroundColor = Key(AmbGround, t01);
            RenderSettings.fogColor = Key(FogC, t01);
            if (_sky != null)
            {
                _sky.SetColor("_SkyTint", Key(SkyTint, t01));
                _sky.SetFloat("_AtmosphereThickness", Key(Atmos, t01));
                _sky.SetFloat("_Exposure", Key(SkyExp, t01));
            }
            float dusk = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 1f, t01));
            var lit = new Color(1f, 0.78f, 0.46f) * (2.2f * dusk);
            foreach (var m in Facades)
            {
                if (m == null) continue;
                m.SetColor("_EmissionColor", lit);
            }
            float wk = Key(WarmK, t01);
            for (int i = 0; i < Warm.Length; i++) if (Warm[i] != null) Warm[i].intensity = _warmBase[i] * wk;
            var fc = Key(FillC, t01);
            float fi = Key(FillI, t01);
            for (int i = 0; i < WindowFill.Length; i++)
                if (WindowFill[i] != null) { WindowFill[i].color = fc; WindowFill[i].intensity = _fillBase[i] * fi; }
            if (_wb != null) _wb.temperature.Override(Key(GradeTemp, t01));
            if (_grade != null) _grade.postExposure.Override(Mathf.Lerp(0.15f, 0.05f, dusk));
            if (Mathf.Abs(_probeT - t01) > 0.08f) RefreshProbes();
        }

        /// <summary>Re-render the realtime reflection probes (room switch, big light change). At most once per frame.</summary>
        public void RefreshProbes()
        {
            if (_probeFrame == Time.frameCount) return;
            _probeFrame = Time.frameCount;
            _probeT = _t;
            foreach (var p in Probes) if (p != null && p.isActiveAndEnabled) p.RenderProbe();
        }
    }
}
