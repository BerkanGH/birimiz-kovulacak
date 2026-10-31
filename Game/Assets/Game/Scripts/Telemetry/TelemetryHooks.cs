using System;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>Client-local analytics facts reported by UI code (docs/ANALYTICS_ARCHITECTURE.md §5). Never pass free text.</summary>
    public interface IClientTelemetryHooks
    {
        void ChoiceUiOpened(TaskKind task, double secondsIntoPhase);
        /// <summary>action: "auto", "next", "back", "reopen".</summary>
        void TutorialStep(TaskKind task, int step, string action);
        void TutorialClosed(TaskKind task, int stepsSeen, double secondsOpen, bool completed);
        void OnboardingIntroCompleted(int cardsSeen, bool skipped);
        void MessageThreadOpened(MessageKind channel, int unread);
        /// <summary>setting: a stable key such as "music_volume"; value: a coarse bucket or enum name, never free text.</summary>
        void SettingChanged(string setting, string value);
        /// <summary>category: funny, betrayal, accusation, surprise, reveal, other.</summary>
        void MomentMarked(string category);
        void RematchPromptShown();
        void RematchRequested();
        void MusicCueChanged(string cue, bool ducked);
    }

    /// <summary>Analytics consent and local data controls for Settings → Privacy and the first-launch prompt.</summary>
    public interface ITelemetryPrivacy
    {
        /// <summary>0 unknown, 1 denied, 2 local logs, 3 local + cloud.</summary>
        int Consent { get; }
        bool NeedsConsentPrompt { get; }
        bool CloudAvailable { get; }
        void SetConsent(int level);
        /// <summary>Deletes this machine's analytics files and queue; returns false if something could not be deleted.</summary>
        bool DeleteLocalData();
        void ResetAnonymousId();
        string LogDirectory { get; }
        /// <summary>Opens the log folder in the OS file browser (no-op when there is none).</summary>
        void OpenLogFolder();
        /// <summary>Zips the local analytics files next to the folder; returns the archive path or null.</summary>
        string ExportLogs();
        bool MomentMarkerAvailable { get; }
    }

    /// <summary>
    /// Seam between UI and the telemetry track. UI calls the static methods; the telemetry track installs
    /// <see cref="Client"/> and <see cref="Privacy"/>. Every call is a no-op when telemetry is absent and never throws.
    /// </summary>
    public static class TelemetryHooks
    {
        public static IClientTelemetryHooks Client;
        public static ITelemetryPrivacy Privacy;

        static void Safe(Action<IClientTelemetryHooks> a)
        {
            var c = Client;
            if (c == null) return;
            try { a(c); }
            catch (Exception e) { Debug.LogWarning("[Telemetry] hook failed: " + e.GetType().Name); }
        }

        public static void ChoiceUiOpened(TaskKind task, double secondsIntoPhase) { Safe(c => c.ChoiceUiOpened(task, secondsIntoPhase)); }
        public static void TutorialStep(TaskKind task, int step, string action) { Safe(c => c.TutorialStep(task, step, action)); }
        public static void TutorialClosed(TaskKind task, int stepsSeen, double secondsOpen, bool completed) { Safe(c => c.TutorialClosed(task, stepsSeen, secondsOpen, completed)); }
        public static void OnboardingIntroCompleted(int cardsSeen, bool skipped) { Safe(c => c.OnboardingIntroCompleted(cardsSeen, skipped)); }
        public static void MessageThreadOpened(MessageKind channel, int unread) { Safe(c => c.MessageThreadOpened(channel, unread)); }
        public static void SettingChanged(string setting, string value) { Safe(c => c.SettingChanged(setting, value)); }
        public static void MomentMarked(string category) { Safe(c => c.MomentMarked(category)); }
        public static void RematchPromptShown() { Safe(c => c.RematchPromptShown()); }
        public static void RematchRequested() { Safe(c => c.RematchRequested()); }
        public static void MusicCueChanged(string cue, bool ducked) { Safe(c => c.MusicCueChanged(cue, ducked)); }
    }
}
