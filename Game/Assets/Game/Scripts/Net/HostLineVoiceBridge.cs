using BirimizKovulacak.Core;
using BirimizKovulacak.Voice;
using Unity.Netcode;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v4 host-side voice providers from <see cref="HostLineController"/> and its engine (docs/VOICE.md "Integration"):
    /// office phases = calls only (+ wiretaps), meetings = everyone hears everyone, final defense = only the defender.
    /// Every member is called per voice frame: no allocations.
    /// </summary>
    public sealed class HostLineVoiceBridge : IVoicePositions, IVoiceCallState, IVoiceModeProvider, IVoiceClientMap
    {
        readonly HostLineController _host;
        static VoiceSystem _boundVoice;
        static double _tapeStartedAt = -1;
        static int _tapeCallId = -1;
        static float _tapeExpected;

        public HostLineVoiceBridge(HostLineController host) { _host = host; }

        /// <summary>Wire voice to this host session (called when the host session object spawns).</summary>
        public static void Bind(HostLineController host)
        {
            var vs = VoiceSystem.Instance;
            if (vs == null) return;
            var b = new HostLineVoiceBridge(host);
            vs.SetHostProviders(b, b, b, null, b);
            vs.RecordingEnabled = host.RecordingsEnabled;
            HostLineController.PlayRecording = PlayPick;
            if (_boundVoice != null) _boundVoice.OnHostRecordingFinished -= OnTapeFinished;
            _boundVoice = vs;
            vs.OnHostRecordingFinished += OnTapeFinished;
        }

        public static void Unbind()
        {
            var vs = VoiceSystem.Instance;
            if (vs != null) { vs.SetHostProviders(null, null, null, null, null); vs.ClearRecordings(); }
            if (_boundVoice != null) { _boundVoice.OnHostRecordingFinished -= OnTapeFinished; _boundVoice = null; }
            HostLineController.PlayRecording = null;
        }

        /// <summary>
        /// Plays the most speech-dense snippet of the first candidate that has speech; returns its call id or -1 and
        /// reports what happened (for analytics: candidate count, chosen rank, speech, failure reason).
        /// </summary>
        static int PlayPick(LineRecordingPick pick, out RecordingPlayInfo info)
        {
            info = new RecordingPlayInfo { Candidates = pick.CandidateCallIds.Count, ChosenRank = -1, Failure = "none" };
            var vs = VoiceSystem.Instance;
            if (vs == null) { info.Failure = "voice_unavailable"; return LineIds.None; }
            if (!vs.RecordingEnabled) { info.Failure = "disabled"; return LineIds.None; }
            if (pick.CandidateCallIds.Count == 0) { info.Failure = "no_candidates"; return LineIds.None; }
            RecordingSnippet snip;
            if (!vs.TrySelectRecording(pick.CandidateCallIds, out snip, pick.Seconds)) { info.Failure = "no_speech"; return LineIds.None; }
            double at = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening ? NetworkManager.Singleton.ServerTime.Time : 0) + 0.4;
            if (!vs.PlayRecording(snip, at)) { info.Failure = "voice_unavailable"; return LineIds.None; }
            info.ChosenRank = pick.CandidateCallIds.IndexOf(snip.CallId);
            info.SpeechMs = snip.SpeechMs;
            info.SnippetMs = snip.LengthMs;
            info.CallLengthMs = snip.CallLengthMs;
            _tapeStartedAt = Time.realtimeSinceStartupAsDouble;
            _tapeCallId = snip.CallId;
            _tapeExpected = snip.LengthMs / 1000f;
            return snip.CallId;
        }

        static void OnTapeFinished(RecordingSnippet snippet)
        {
            if (_tapeStartedAt < 0) return;
            double actual = Time.realtimeSinceStartupAsDouble - _tapeStartedAt;
            int callId = _tapeCallId;
            _tapeStartedAt = -1;
            _tapeCallId = -1;
            // Playback starts 0.4 s after the hand-off and adds a short tail; an end well before the snippet length means it was cut.
            bool stoppedEarly = actual + 0.25 < _tapeExpected;
            var host = HostLineController.Current;
            HostTelemetryBridge.RecordingFinished(callId, actual, stoppedEarly, host != null ? host.Now : 0);
        }

        HostLineController.Slot SlotAt(int slot)
        {
            var slots = _host.Slots;
            return slot >= 0 && slot < slots.Length ? slots[slot] : null;
        }

        // ---- IVoicePositions
        public bool IsActive(int slot)
        {
            var s = SlotAt(slot);
            return s != null && s.Occupied && s.Connected && !s.Virtual;
        }

        public bool TryGetPosition(int slot, out VoiceVec3 position) { position = default; return false; }

        // ---- IVoiceCallState
        public int GetCallPartner(int slot) { var e = _host.Engine; return e != null ? e.CallPartner(slot) : -1; }
        public int GetCallId(int slot) { var e = _host.Engine; return e != null ? e.ConnectedCallId(slot) : -1; }
        public int GetTappers(int target, int[] buffer) { var e = _host.Engine; return e != null ? e.TappersOf(target, buffer) : 0; }

        // ---- IVoiceModeProvider
        public VoiceMode CurrentMode
        {
            get
            {
                var e = _host.Engine;
                if (e == null) return VoiceMode.AllHear; // lobby
                return LineClient.IsOfficePhase(e.Phase) ? VoiceMode.Calls : VoiceMode.AllHear;
            }
        }

        public bool CanSpeak(int slot) { var e = _host.Engine; return e == null || e.MaySpeak(slot); }

        // ---- IVoiceClientMap
        public int SlotOfClient(ulong clientId) { return _host.SlotOfClient(clientId); }

        public bool TryGetClient(int slot, out ulong clientId)
        {
            var s = SlotAt(slot);
            if (s != null && s.Occupied && s.Connected && !s.Virtual && s.ClientId != ulong.MaxValue) { clientId = s.ClientId; return true; }
            clientId = ulong.MaxValue;
            return false;
        }
    }
}
