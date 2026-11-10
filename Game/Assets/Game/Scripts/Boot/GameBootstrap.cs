using System.Collections;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>Process entry point: initialisation order, automation flags (autohost/autojoin/bot), quitting.</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        int _matchesFinished;

        void Awake()
        {
            LaunchArgs.Initialize();
            var args = LaunchArgs.Current;
            Application.runInBackground = true;
            DisplaySettings.ApplyAtBoot();
            LobbyScreen.GroupCodeSetter = code => HostLineController.Current?.SetGroupCode(code);
            // Consent changes reach the host at once (the connection payload only carries the value at join time).
            TelemetryConsentStore.ConsentChanged += () =>
            {
                var p = TelemetryHooks.Privacy;
                if (p != null) LineClient.SendTelemetryConsent(p.Consent, p.AnonymousId);
            };
            Application.targetFrameRate = args.Bot ? 30 : 120;
            QualitySettings.vSyncCount = args.Bot ? 0 : 1;
            string name = !string.IsNullOrEmpty(args.Name) ? args.Name : PlayerPrefs.GetString("player.name", System.Environment.UserName);
            MatchLog.Initialize(name);
            Loc.Load(Loc.Language);
            GameInput.Initialize();
            // The host binds voice through HostLineVoiceBridge (calls-only office phases, all-hear meetings, defense floor).
            VoiceSystem.ExternalHostProviders = true;
            VoiceSystem.HostSessionActive = () => HostLineController.Current != null;
            if (GetComponent<VoiceSystem>() == null) gameObject.AddComponent<VoiceSystem>();
            if (GetComponent<LineClient>() == null) gameObject.AddComponent<LineClient>();
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-uimock") >= 0) gameObject.AddComponent<LineMockDriver>();
            if (args.Bot) gameObject.AddComponent<LineBotDriver>();
            if (args.Screenshots) gameObject.AddComponent<LineScreenshotRecorder>();
            if (GetComponent<LineWorldDriver>() == null) gameObject.AddComponent<LineWorldDriver>();
        }

        IEnumerator Start()
        {
            var args = LaunchArgs.Current;
            yield return null;
            LineClient.I.PhaseChanged += OnLinePhase;
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
                while (Time.realtimeSinceStartup < t && (NetSession.Instance == null || LineClient.I.MySlot < 0)) yield return null;
                yield return new WaitForSecondsRealtime(1f);
                if (ConnectionManager.I.LineHost == null) LineClient.I.SetReady(true);
            }
        }

        int _lastResultsWeek = -1;

        void OnLinePhase(LinePhase from, LinePhase to)
        {
            var v = LineClient.I.View;
            if (to == LinePhase.Results && v != null && v.WeekId != _lastResultsWeek)
            {
                _lastResultsWeek = v.WeekId;
                _matchesFinished++;
                int quitAfter = LaunchArgs.Current.QuitAfterMatches;
                if (quitAfter > 0 && _matchesFinished >= quitAfter) StartCoroutine(QuitSoon(8f));
            }
        }

        IEnumerator QuitSoon(float seconds)
        {
            MatchLog.Write("quitting after " + _matchesFinished + " match(es)");
            yield return new WaitForSecondsRealtime(seconds);
            if (ConnectionManager.I.LineHost != null)
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
            var view = LineClient.I != null ? LineClient.I.View : null;
            if (!_dropped && a.SimulateDropAt > 0 && view != null && view.Phase == LinePhase.TaskPlay && view.Day >= 1 &&
                LineClient.I.Now - view.PhaseStartTime > a.SimulateDropAt)
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
}
