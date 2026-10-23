using System.Collections;
using System.IO;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>Process entry point: initialisation order, automation flags (autohost/autojoin/bot), quitting.</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        int _matchesFinished;
        int _lastResultsMatch = -1;

        void Awake()
        {
            LaunchArgs.Initialize();
            var args = LaunchArgs.Current;
            Application.runInBackground = true;
            Application.targetFrameRate = args.Bot ? 30 : 120;
            QualitySettings.vSyncCount = args.Bot ? 0 : 1;
            string name = !string.IsNullOrEmpty(args.Name) ? args.Name : PlayerPrefs.GetString("player.name", System.Environment.UserName);
            MatchLog.Initialize(name);
            Loc.Load("tr");
            GameInput.Initialize();
            if (args.Bot) gameObject.AddComponent<NetworkBotDriver>();
            if (args.Screenshots) gameObject.AddComponent<ScreenshotRecorder>();
            if (args.InputSim && !args.Bot) gameObject.AddComponent<InputSimDriver>();
        }

        IEnumerator Start()
        {
            var args = LaunchArgs.Current;
            yield return null;
            ClientState.I.PhaseChanged += OnPhase;
            string name = !string.IsNullOrEmpty(args.Name) ? args.Name : PlayerPrefs.GetString("player.name", System.Environment.UserName);
            if (args.AutoHost)
            {
                ConnectionManager.I.StartHost(name, args.Port);
            }
            else if (!string.IsNullOrEmpty(args.AutoJoinAddress))
            {
                // The host may still be starting: retry for a while.
                float until = Time.realtimeSinceStartup + 60f;
                while (Time.realtimeSinceStartup < until)
                {
                    if (ConnectionManager.I.Current == ConnectionManager.State.Offline || ConnectionManager.I.Current == ConnectionManager.State.Failed)
                    {
                        while (Unity.Netcode.NetworkManager.Singleton.ShutdownInProgress) yield return null;
                        ConnectionManager.I.StartClient(name, args.AutoJoinAddress, args.Port);
                    }
                    float wait = Time.realtimeSinceStartup + 4f;
                    while (Time.realtimeSinceStartup < wait && ConnectionManager.I.Current != ConnectionManager.State.Connected) yield return null;
                    if (ConnectionManager.I.Current == ConnectionManager.State.Connected) break;
                    if (ConnectionManager.I.Current == ConnectionManager.State.Failed && ConnectionManager.I.LastErrorKey.StartsWith("reject.")) break;
                    if (ConnectionManager.I.Current == ConnectionManager.State.Connecting) { ConnectionManager.I.Leave(); yield return new WaitForSecondsRealtime(0.5f); }
                }
            }
            if (args.Bot)
            {
                // Bots ready up automatically once they are in the lobby.
                while (ConnectionManager.I.Current != ConnectionManager.State.Connected && ConnectionManager.I.Current != ConnectionManager.State.Hosting) yield return null;
                float t = Time.realtimeSinceStartup + 20f;
                while (Time.realtimeSinceStartup < t && (NetSession.Instance == null || ClientState.I.MySlot < 0)) yield return null;
                yield return new WaitForSecondsRealtime(1f);
                if (ConnectionManager.I.HostController == null) NetSession.Instance?.SetReady(true);
            }
        }

        void OnPhase(MatchPhase from, MatchPhase to)
        {
            var v = ClientState.I.View;
            if (to == MatchPhase.Results && v != null && v.MatchId != _lastResultsMatch)
            {
                _lastResultsMatch = v.MatchId;
                _matchesFinished++;
                int quitAfter = LaunchArgs.Current.QuitAfterMatches;
                if (quitAfter > 0 && _matchesFinished >= quitAfter) StartCoroutine(QuitSoon(12f));
            }
        }

        IEnumerator QuitSoon(float seconds)
        {
            MatchLog.Write("quitting after " + _matchesFinished + " match(es)");
            yield return new WaitForSecondsRealtime(seconds);
            if (ConnectionManager.I.HostController != null)
            {
                // Give clients time to quit first so they do not report a host loss.
                yield return new WaitForSecondsRealtime(4f);
            }
            MatchLog.Close();
            Application.Quit();
        }

        bool _dropped;

        void Update()
        {
            var a = LaunchArgs.Current;
            var view = ClientState.I != null ? ClientState.I.View : null;
            if (!_dropped && a.SimulateDropAt > 0 && view != null && view.Phase >= MatchPhase.Work1 && view.Phase <= MatchPhase.Work2 &&
                ClientState.I.Now - MatchClock.MatchStart > a.SimulateDropAt)
            {
                _dropped = true;
                ConnectionManager.I.SimulateNetworkDrop(a.DropRejoin, a.DropDowntime);
            }
            float q = LaunchArgs.Current.QuitAfterSeconds;
            if (q > 0 && Time.realtimeSinceStartup > q)
            {
                MatchLog.Write("quit-after-seconds reached");
                MatchLog.Close();
                Application.Quit();
                enabled = false;
            }
        }
    }

    /// <summary>Saves screenshots at key moments of each match (automation: -screenshots).</summary>
    public sealed class ScreenshotRecorder : MonoBehaviour
    {
        void Start()
        {
            ClientState.I.PhaseChanged += (from, to) => StartCoroutine(Shot(to));
        }

        IEnumerator Shot(MatchPhase phase)
        {
            float[] delays;
            switch (phase)
            {
                case MatchPhase.Briefing: delays = new[] { 3f }; break;
                case MatchPhase.Work1: delays = new[] { 25f, 90f }; break;
                case MatchPhase.Raid: delays = new[] { 4f, 14f }; break;
                case MatchPhase.Work2: delays = new[] { 30f }; break;
                case MatchPhase.FinalInspection: delays = new[] { 12f }; break;
                case MatchPhase.MeetingDiscussion: delays = new[] { 12f }; break;
                case MatchPhase.MeetingVote: delays = new[] { 6f }; break;
                case MatchPhase.MeetingVerdict: delays = new[] { 3f }; break;
                case MatchPhase.Results: delays = new[] { 4f }; break;
                default: yield break;
            }
            int match = ClientState.I.View != null ? ClientState.I.View.MatchId : 0;
            float start = Time.realtimeSinceStartup;
            foreach (float d in delays)
            {
                while (Time.realtimeSinceStartup - start < d * Mathf.Max(0.3f, LaunchArgs.Current.DurationScale)) yield return null;
                var v = ClientState.I.View;
                if (v == null || v.Phase != phase) yield break;
                string dir = Path.Combine(MatchLog.Directory ?? Application.persistentDataPath, "shots");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, string.Format("{0}_m{1}_{2}_{3:000}.png", ConnectionManager.I.PlayerName, match, phase, (int)(Time.realtimeSinceStartup - start)));
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(file);
                MatchLog.Write("screenshot " + file);
            }
        }
    }
}
