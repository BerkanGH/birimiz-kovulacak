using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v3 UI mock: drives <see cref="LineClient"/> with hand-made views so screens can be built and screenshotted
    /// without a running engine or network. Launch with <c>-uimock &lt;name|prefix*|all&gt; -uimockout &lt;dir&gt;</c>;
    /// each scenario plays its steps, a screenshot is saved after each step marked <see cref="LineMockStep.Shot"/>
    /// (and after the last step), then the next scenario runs; the process quits at the end.
    /// UI tracks register scenarios from their own files with [RuntimeInitializeOnLoadMethod].
    /// </summary>
    public static class LineMock
    {
        static readonly SortedDictionary<string, Func<LineMockScenario>> _scenarios = new SortedDictionary<string, Func<LineMockScenario>>(StringComparer.Ordinal);

        public static void Register(string name, Func<LineMockScenario> build) { _scenarios[name] = build; }
        public static IEnumerable<string> Names => _scenarios.Keys;
        public static bool TryBuild(string name, out LineMockScenario s)
        {
            Func<LineMockScenario> f;
            s = _scenarios.TryGetValue(name, out f) ? f() : null;
            return s != null;
        }

        /// <summary>Mock host clock (seconds). Views in scenarios use times relative to it (start = 1000).</summary>
        public static double Now;

        /// <summary>Convenience: a mid-week view with <paramref name="players"/> colleagues, Me = 0.</summary>
        public static LineView BaseView(LinePhase phase, int players = 6, int day = 1, double phaseLeft = 90)
        {
            var v = new LineView
            {
                WeekId = 1, StateVersion = 1, Me = 0, Now = Now, Phase = phase, PhaseStartTime = Now - 10, PhaseEndTime = Now + phaseLeft,
                Day = day, DayCount = 5, Length = WeekLength.Full, FirstWeek = false, ManagerProfileId = LineManagerIds.Paragoz,
            };
            v.TodayRounds.Add(RoundType.PrimHavuzu); v.TodayRounds.Add(RoundType.FarkliEposta);
            string[] names = { "Mavi", "Sarı", "Pembe", "Yeşil", "Turuncu", "Mor" };
            for (int i = 0; i < players; i++)
                v.Players.Add(new LinePlayerView { Id = i, Name = names[i], ColorIndex = i, Connected = true, Blame = (i * 3) % 7, Ihtars = i == 2 ? 1 : 0, SessionPoints = i });
            v.Features.TricksUnlocked = true; v.Features.TipLineUnlocked = true; v.Features.RecordingsEnabled = true;
            v.Mine.AgendaTarget = 3; v.Mine.Trick = TrickType.KahveDok;
            return v;
        }
    }

    public sealed class LineMockScenario
    {
        public int Me;
        public LineConfig Config = LineConfig.CreateDefault();
        public readonly List<LineMockStep> Steps = new List<LineMockStep>();
        /// <summary>Optional: called for every request the UI sends (log it, or mutate the next view).</summary>
        public Action<LineAction> OnSend;

        public LineMockScenario Step(float atSeconds, LineView view, bool shot = false, params LineEvent[] events)
        {
            Steps.Add(new LineMockStep { At = atSeconds, View = view, Shot = shot, Events = new List<LineEvent>(events) });
            return this;
        }

        public LineMockScenario Do(float atSeconds, Action action, bool shot = false)
        {
            Steps.Add(new LineMockStep { At = atSeconds, Action = action, Shot = shot });
            return this;
        }
    }

    public sealed class LineMockStep
    {
        public float At;
        public LineView View;
        public List<LineEvent> Events;
        public Action Action;
        public bool Shot;
    }

    /// <summary>Added by <see cref="GameBootstrap"/> when <c>-uimock</c> is present.</summary>
    public sealed class LineMockDriver : MonoBehaviour
    {
        public static bool Active { get; private set; }
        string _pattern = "", _outDir = "";

        void Awake()
        {
            Active = true;
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++)
            {
                if (a[i] == "-uimock") _pattern = a[i + 1];
                if (a[i] == "-uimockout") _outDir = a[i + 1];
            }
            if (string.IsNullOrEmpty(_outDir)) _outDir = Path.Combine(Application.persistentDataPath, "uimock");
            _outDir = Path.GetFullPath(_outDir);
            Directory.CreateDirectory(_outDir);
            double start = Time.realtimeSinceStartupAsDouble;
            LineClient.Clock = () => 1000.0 + (Time.realtimeSinceStartupAsDouble - start);
        }

        IEnumerator Start()
        {
            yield return null;
            yield return null; // UIRoot builds its screens in Start
            var names = new List<string>();
            foreach (var n in LineMock.Names)
                if (_pattern == "all" || n == _pattern || (_pattern.EndsWith("*") && n.StartsWith(_pattern.Substring(0, _pattern.Length - 1), StringComparison.Ordinal)))
                    names.Add(n);
            if (names.Count == 0) Debug.LogWarning("[LineMock] no scenario matches '" + _pattern + "'. Known: " + string.Join(", ", LineMock.Names));
            foreach (var name in names)
            {
                LineMockScenario s;
                if (!LineMock.TryBuild(name, out s)) continue;
                yield return Run(name, s);
            }
            Debug.Log("[LineMock] done: " + names.Count + " scenario(s) → " + _outDir);
            yield return new WaitForSecondsRealtime(0.3f);
            Application.Quit();
        }

        IEnumerator Run(string name, LineMockScenario s)
        {
            var client = LineClient.I;
            client.ClearWeek();
            client.SetIdentity(s.Me, s.Config);
            LineClient.Transport = req => { MatchLog.Write("[mock] send " + req); s.OnSend?.Invoke(req); };
            float t0 = Time.realtimeSinceStartup;
            int shot = 0;
            for (int i = 0; i < s.Steps.Count; i++)
            {
                var st = s.Steps[i];
                while (Time.realtimeSinceStartup - t0 < st.At) yield return null;
                LineMock.Now = client.Now;
                st.Action?.Invoke();
                if (st.View != null)
                {
                    st.View.Now = client.Now;
                    client.ApplyView(st.View);
                }
                if (st.Events != null && st.Events.Count > 0) client.ApplyEvents(st.Events);
                if (st.Shot || i == s.Steps.Count - 1)
                {
                    yield return new WaitForSecondsRealtime(1.2f); // let entrance motion settle
                    yield return new WaitForEndOfFrame();
                    string file = Path.Combine(_outDir, name + "_" + (shot++).ToString("00") + ".png");
                    ScreenCapture.CaptureScreenshot(file);
                    yield return null;
                }
            }
            yield return new WaitForSecondsRealtime(0.3f);
        }
    }
}
