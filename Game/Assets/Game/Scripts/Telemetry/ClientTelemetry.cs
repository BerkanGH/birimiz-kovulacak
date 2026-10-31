using System;
using System.Globalization;
using BirimizKovulacak.Core;
using BirimizKovulacak.Telemetry;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Client-local analytics facts: session, connection experience, refused actions, and the UI hooks of
    /// <see cref="TelemetryHooks"/>. Subscribes to LineClient / ConnectionManager (found lazily: both are created by
    /// the scene after this runtime installs). Gameplay facts are the host's job (HostTelemetryBridge): this class
    /// never reports who did what to whom.
    /// </summary>
    public sealed class ClientTelemetry : IClientTelemetryHooks
    {
        readonly GameTelemetryService _t;
        readonly TelemetryConsentStore _consent;
        readonly ClientConnectionTelemetry _conn;
        LineClient _client;
        ConnectionManager _cm;
        bool _sessionStarted;
        float _resultsAt = -1f;
        LinePhase _lastPhase = LinePhase.None;

        public ClientTelemetry(GameTelemetryService service, TelemetryConsentStore consent)
        {
            _t = service;
            _consent = consent;
            _conn = new ClientConnectionTelemetry(service, () => service.Now);
        }

        public void NoteJoinKind(string kind) { _conn.NoteJoinKind(kind); }

        // ---- lifecycle ---------------------------------------------------------------------------

        public void OnInstalled() { OnConsentGranted(); }

        /// <summary>Called whenever consent is applied; the first time it is >= 2 the session_started event is emitted.</summary>
        public void OnConsentGranted()
        {
            if (_sessionStarted || _consent == null || _consent.Consent < 2 || !_t.IsEnabled) return;
            _sessionStarted = true;
            try
            {
                bool first = PlayerPrefs.GetInt("telemetry.launched", 0) == 0;
                PlayerPrefs.SetInt("telemetry.launched", 1);
                string platform = Application.isEditor ? "editor"
                    : Application.platform == RuntimePlatform.WindowsPlayer ? "windows"
                    : Application.platform == RuntimePlatform.OSXPlayer ? "mac"
                    : Application.platform == RuntimePlatform.LinuxPlayer ? "linux" : "other";
                string lang = Loc.Language == "tr" || Loc.Language == "en" ? Loc.Language : "other";
                _t.Begin("session_started").Enum("platform", platform).Enum("language", lang).Bool("fullscreen", Screen.fullScreen).Bool("first_launch", first).Emit();
            }
            catch (Exception) { }
        }

        public void OnApplicationQuit()
        {
            if (!_sessionStarted) return;
            try { _t.Begin("session_ended").Float("duration_seconds", Time.realtimeSinceStartup).Enum("reason", "quit").Emit(); }
            catch (Exception) { }
        }

        public void Update()
        {
            Bind();
            _conn.Tick();
        }

        // ---- subscriptions -----------------------------------------------------------------------

        void Bind()
        {
            var lc = LineClient.I;
            if (lc != _client)
            {
                if (_client != null) Unsubscribe(_client);
                _client = lc;
                if (lc != null)
                {
                    lc.PhaseChanged += OnPhaseChanged;
                    lc.ResultReceived += OnResult;
                    lc.Welcomed += OnWelcomed;
                    lc.LobbyChanged += OnLobby;
                }
            }
            var cm = ConnectionManager.I;
            if (cm != _cm)
            {
                if (_cm != null) _cm.StateChanged -= OnConnectionState;
                _cm = cm;
                if (cm != null) cm.StateChanged += OnConnectionState;
            }
        }

        void Unsubscribe(LineClient lc)
        {
            lc.PhaseChanged -= OnPhaseChanged;
            lc.ResultReceived -= OnResult;
            lc.Welcomed -= OnWelcomed;
            lc.LobbyChanged -= OnLobby;
        }

        void OnConnectionState()
        {
            try
            {
                var cm = _cm;
                if (cm == null) return;
                var view = _client != null ? _client.View : null;
                var phase = view != null ? view.Phase : LinePhase.None;
                if (cm.Current == ConnectionManager.State.Connecting && LaunchArgs.Current != null && !string.IsNullOrEmpty(LaunchArgs.Current.AutoJoinAddress)) _conn.NoteJoinKind("auto");
                _conn.OnState((ClientConnState)(int)cm.Current, cm.LastErrorKey, phase, view != null);
            }
            catch (Exception) { }
        }

        void OnWelcomed()
        {
            try
            {
                _t.Context.LocalSlot = _client != null ? _client.MySlot : -1;
                _t.Context.LocalIsBot = false;
            }
            catch (Exception) { }
        }

        void OnLobby(LineLobbyView lobby)
        {
            try
            {
                if (lobby == null || _t.HostMode) return; // the host process already owns the session ids
                _t.Context.AdoptSession(lobby.SessionId, lobby.GroupId);
            }
            catch (Exception) { }
        }

        void OnPhaseChanged(LinePhase oldPhase, LinePhase newPhase)
        {
            try
            {
                if (newPhase != LinePhase.None) _lastPhase = newPhase;
                if (newPhase == LinePhase.WeekResults) _resultsAt = Time.realtimeSinceStartup;
                if (_t.HostMode || _client == null) return; // on the host the observer owns match id and position
                var v = _client.View;
                if (v == null) return; // the match id stays until the next match so rematch events can cite their results
                if (v.MatchId != _t.Context.MatchId)
                {
                    bool bots = false;
                    foreach (var p in v.Players) bots |= p.IsBot;
                    _t.Context.BeginMatch(v.MatchId, v.WeekId, v.Players.Count, bots, _client.Lobby != null ? _client.Lobby.Experiments : null);
                }
                TaskKind k = v.Task != null ? v.Task.Kind : TaskKind.None;
                _t.Context.SetPosition(TelemetryNames.Of(v.Phase), v.Day, v.RoundIndex, v.TaskNumber, k == TaskKind.None ? "" : LineTasks.Id(k), v.Task != null ? v.Task.Version : -1);
            }
            catch (Exception) { }
        }

        // ---- refused actions ---------------------------------------------------------------------

        void OnResult(LineActionResult result, LineAction request)
        {
            try
            {
                if (result == null || result.Accepted || request == null) return;
                string channel = null;
                switch (request.Verb)
                {
                    case LineVerb.SendText: channel = "text"; break;
                    case LineVerb.SendEmail: channel = "email"; break;
                    case LineVerb.SendMeetingChat: channel = "meeting_chat"; break;
                    case LineVerb.FileComplaint: channel = "complaint"; break;
                }
                if (channel != null)
                {
                    string reason = null;
                    switch (result.Failure)
                    {
                        case LineFailure.TooLong: reason = "too_long"; break;
                        case LineFailure.EmptyText: reason = "empty"; break;
                        case LineFailure.RateLimited: reason = "rate_limited"; break;
                        case LineFailure.LimitReached: reason = "limit_reached"; break;
                        case LineFailure.WrongPhase: reason = "wrong_phase"; break;
                    }
                    if (reason != null) { _t.Begin("message_rejected").Enum("channel", channel).Enum("reason", reason).Emit(); return; }
                }
                _t.Begin("ui_action_rejected").Enum("verb", request.Verb).Enum("failure", result.Failure).Emit();
            }
            catch (Exception) { }
        }

        // ---- IClientTelemetryHooks ---------------------------------------------------------------

        public void ChoiceUiOpened(TaskKind task, double secondsIntoPhase)
        {
            if (task == TaskKind.None) return;
            _t.Begin("choice_ui_opened").Enum("task_id", LineTasks.Id(task)).FloatIfKnown("seconds_into_phase", secondsIntoPhase).Emit();
        }

        public void TutorialStep(TaskKind task, int step, string action)
        {
            if (task == TaskKind.None) return;
            _t.Begin("tutorial_step_viewed").Enum("task_id", LineTasks.Id(task)).Int("step", step).Enum("action", action).Emit();
        }

        public void TutorialClosed(TaskKind task, int stepsSeen, double secondsOpen, bool completed)
        {
            if (task == TaskKind.None) return;
            _t.Begin("tutorial_closed").Enum("task_id", LineTasks.Id(task)).Int("steps_seen", stepsSeen).Float("seconds_open", Math.Max(0, secondsOpen)).Bool("completed", completed).Emit();
        }

        public void OnboardingIntroCompleted(int cardsSeen, bool skipped)
        {
            _t.Begin("onboarding_intro_completed").Int("cards_seen", cardsSeen).Bool("skipped", skipped).Emit();
        }

        public void MessageThreadOpened(MessageKind channel, int unread)
        {
            string c = channel == MessageKind.Text ? "text" : channel == MessageKind.Email ? "email" : channel == MessageKind.MeetingChat ? "meeting_chat" : channel == MessageKind.Complaint ? "complaint" : null;
            if (c == null) return;
            _t.Begin("message_thread_opened").Enum("channel", c).Int("unread", Math.Max(0, unread)).Emit();
        }

        static readonly string[] Settings = { "music_volume", "sfx_volume", "voice_volume", "mic_gain", "language", "fullscreen", "quality", "sensitivity" };
        static readonly string[] Values = { "off", "on", "low", "medium", "high", "tr", "en" };

        public void SettingChanged(string setting, string value)
        {
            string s = Array.IndexOf(Settings, setting) >= 0 ? setting : "other";
            var b = _t.Begin("settings_changed").Enum("setting", s);
            int pct;
            if (value != null && Array.IndexOf(Values, value) >= 0) b.Enum("value", value);
            else if (value != null && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out pct) && pct >= 0 && pct <= 100) b.Int("value_percent", pct);
            else b.Enum("value", "other");
            b.Emit();
        }

        static readonly string[] Moments = { "funny", "betrayal", "accusation", "surprise", "reveal", "other" };

        public void MomentMarked(string category)
        {
            _t.Begin("moment_marked").Enum("category", Array.IndexOf(Moments, category) >= 0 ? category : "other").Enum("source", "player").Emit();
        }

        public void RematchPromptShown() { _t.Begin("rematch_prompt_shown").Emit(); }

        public void RematchRequested()
        {
            var b = _t.Begin("rematch_requested");
            b.FloatIfKnown("seconds_after_results", _resultsAt >= 0 ? Time.realtimeSinceStartup - _resultsAt : -1);
            b.Emit();
        }

        static readonly string[] Cues = { "menu", "office", "meeting", "final", "results", "tension" };

        public void MusicCueChanged(string cue, bool ducked)
        {
            _t.Begin("music_cue_changed").Enum("cue", Array.IndexOf(Cues, cue) >= 0 ? cue : "other").Bool("ducked", ducked).Emit();
        }
    }
}
