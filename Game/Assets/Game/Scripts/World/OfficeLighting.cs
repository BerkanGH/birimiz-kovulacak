using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Office lighting driven by the local view: normal day lighting, and during a power cut
    /// (<c>view.Office.PowerDown</c>) the room lights flicker out, ambient and sun drop, red emergency lights come on and
    /// the monitors keep glowing (DeskVisual handles the per-desk glow). ScreenFx (feel) adds the post-processing
    /// darkening on top. Zero allocations per frame; light/material changes happen only while the blend moves.
    /// </summary>
    public sealed class OfficeLighting : MonoBehaviour
    {
        public static OfficeLighting I { get; private set; }

        public Light Sun;
        public Light[] RoomLights;
        public Light[] EmergencyLights;
        /// <summary>Emissive lamp surfaces (pendants, floor lamps, TV, vending glow) switched off in the dark.</summary>
        public Renderer[] LampGlows;
        public Material LampOff;
        /// <summary>Emergency lamp lenses (red while the power is down).</summary>
        public Renderer[] EmergencyLamps;
        public Material EmergencyOn, EmergencyOff;

        public Color AmbientSky = new Color(0.86f, 0.9f, 1f), AmbientEquator = new Color(0.92f, 0.87f, 0.8f), AmbientGround = new Color(0.48f, 0.42f, 0.36f);
        public Color DarkSky = new Color(0.07f, 0.08f, 0.13f), DarkEquator = new Color(0.06f, 0.05f, 0.07f), DarkGround = new Color(0.03f, 0.02f, 0.02f);
        [Range(0f, 1f)] public float DarkSunFactor = 0.12f;

        /// <summary>0 = normal, 1 = full power cut. Read by DeskVisual for monitor glow.</summary>
        public float Darkness { get; private set; }
        public bool PowerDown { get; private set; }

        float[] _roomIntensity, _emIntensity;
        Material[] _glowOn;
        float _sunIntensity;
        float _since;
        bool _lampsDark, _emOn;
        float _applied = -1f;

        static readonly float[] Flicker = { 1f, 0.2f, 0.9f, 0.1f, 0.6f, 1f, 1f };

        void Awake()
        {
            I = this;
            _sunIntensity = Sun != null ? Sun.intensity : 1f;
            _roomIntensity = Capture(RoomLights);
            _emIntensity = Capture(EmergencyLights);
            if (LampGlows != null)
            {
                _glowOn = new Material[LampGlows.Length];
                for (int i = 0; i < LampGlows.Length; i++) _glowOn[i] = LampGlows[i] != null ? LampGlows[i].sharedMaterial : null;
            }
            if (EmergencyLights != null) foreach (var l in EmergencyLights) if (l != null) l.enabled = false;
            ApplyLamps(false);
            ApplyEmergency(false);
        }

        static float[] Capture(Light[] lights)
        {
            var a = new float[lights != null ? lights.Length : 0];
            for (int i = 0; i < a.Length; i++) a[i] = lights[i] != null ? lights[i].intensity : 0f;
            return a;
        }

        void Update()
        {
            var cs = ClientState.I;
            var v = cs != null ? cs.View : null;
            bool down = v != null && v.Office != null && v.Office.PowerDown && v.Phase >= MatchPhase.Briefing && v.Phase <= MatchPhase.FinalInspection;
            if (down != PowerDown) { PowerDown = down; _since = Time.time; }

            float target = down ? 1f : 0f;
            float since = Time.time - _since;
            float d;
            if (down && since < Flicker.Length * 0.07f) d = 1f - Flicker[Mathf.Clamp((int)(since / 0.07f), 0, Flicker.Length - 1)] * 0.6f;
            else d = Mathf.MoveTowards(Darkness, target, Time.deltaTime / (down ? 0.25f : 0.9f));
            Darkness = d;
            if (Mathf.Abs(_applied - d) < 0.002f && (d <= 0f || d >= 1f) && _applied >= 0f) { Pulse(); return; }
            _applied = d;

            float lit = 1f - d;
            if (RoomLights != null)
                for (int i = 0; i < RoomLights.Length; i++)
                {
                    var l = RoomLights[i];
                    if (l == null) continue;
                    l.intensity = _roomIntensity[i] * lit;
                    bool on = lit > 0.02f;
                    if (l.enabled != on) l.enabled = on;
                }
            if (Sun != null) Sun.intensity = _sunIntensity * Mathf.Lerp(1f, DarkSunFactor, d);
            RenderSettings.ambientSkyColor = Color.Lerp(AmbientSky, DarkSky, d);
            RenderSettings.ambientEquatorColor = Color.Lerp(AmbientEquator, DarkEquator, d);
            RenderSettings.ambientGroundColor = Color.Lerp(AmbientGround, DarkGround, d);
            ApplyLamps(d > 0.5f);
            ApplyEmergency(d > 0.35f);
            Pulse();
        }

        void Pulse()
        {
            if (!_emOn || EmergencyLights == null) return;
            float k = 0.75f + 0.25f * Mathf.Sin(Time.time * 4.2f);
            for (int i = 0; i < EmergencyLights.Length; i++)
                if (EmergencyLights[i] != null) EmergencyLights[i].intensity = _emIntensity[i] * k * Mathf.Clamp01(Darkness * 1.4f);
        }

        void ApplyLamps(bool dark)
        {
            if (_glowOn == null || (dark == _lampsDark && _applied >= 0f)) return;
            _lampsDark = dark;
            for (int i = 0; i < LampGlows.Length; i++)
                if (LampGlows[i] != null) LampGlows[i].sharedMaterial = dark && LampOff != null ? LampOff : _glowOn[i];
        }

        void ApplyEmergency(bool on)
        {
            if (on == _emOn && _applied >= 0f) return;
            _emOn = on;
            if (EmergencyLights != null)
                for (int i = 0; i < EmergencyLights.Length; i++) if (EmergencyLights[i] != null) EmergencyLights[i].enabled = on;
            if (EmergencyLamps != null)
                for (int i = 0; i < EmergencyLamps.Length; i++)
                    if (EmergencyLamps[i] != null) EmergencyLamps[i].sharedMaterial = on ? EmergencyOn : EmergencyOff;
        }
    }
}
