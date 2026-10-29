using System;
using System.Collections.Generic;
using BirimizKovulacak.Voice;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// In-game voice: phone calls only during the day (<see cref="VoiceMode.Calls"/>) with wiretaps, everyone-hears
    /// lobby / meetings / results, the defense floor via CanSpeak, in-memory call recordings replayed as a meeting tape.
    /// One instance per process (GameBootstrap adds it).
    /// <para>Data flow: mic (or -voicetest source) → <see cref="VoiceCapturePipeline"/> (16 kHz, 20 ms Opus) →
    /// client: bundle → NGO named message "bk.voice.up" (Unreliable, own sequence numbers) → host:
    /// <see cref="ProximityVoiceRouter"/> decides recipients + flags per frame (and <see cref="VoiceCallRecorder"/>
    /// keeps call frames) → "bk.voice.down" per recipient → <see cref="VoicePlaybackChannel"/> (jitter buffer,
    /// FEC/PLC, gain, muffle, phone EQ) → <see cref="VoiceEmitter"/>. The meeting tape: host
    /// <see cref="VoiceRecordingStreamer"/> → "bk.voice.down" frames flagged Recording + "bk.voice.tape" start/end
    /// (Reliable) → <see cref="VoiceTapePlayer"/> → its own 2D emitter. The host's own mic and speakers use the same
    /// paths without the network.</para>
    /// <para>Session binding is pluggable (static hooks, see docs/VOICE.md "v3"): <see cref="LocalSlotSource"/>,
    /// <see cref="HostSessionActive"/>, <see cref="ExternalHostProviders"/> + <see cref="SetHostProviders"/>,
    /// <see cref="SlotNameResolver"/>, <see cref="ServerTimeSource"/>. The defaults read <see cref="LineClient"/>; the
    /// host routes only through the providers given to <see cref="SetHostProviders"/> (HostLineVoiceBridge).</para>
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class VoiceSystem : MonoBehaviour, IVoiceFrameSink, IVoiceRecordingSink
    {
        public const string UpMessage = "bk.voice.up";
        public const string DownMessage = "bk.voice.down";
        public const string TapeMessage = "bk.voice.tape";
        const int Slots = VoiceFormat.MaxPlayers;
        /// <summary>Client safety net: a running tape without frames for this long ends by itself (lost host).</summary>
        const double TapeSilenceTimeoutMs = 8000;

        public static VoiceSystem Instance { get; private set; }

        // ------------------------------------------------------------------ session binding (static hooks)

        /// <summary>Local player's slot (-1 = not in a session). Null = default: LineClient.I.MySlot.</summary>
        public static Func<int> LocalSlotSource;

        /// <summary>Host: true while voice should be routed (HostLineController.Current != null). Null = default:
        /// true while providers set with <see cref="SetHostProviders"/> exist.</summary>
        public static Func<bool> HostSessionActive;

        /// <summary>Kept for callers of the v3 integration API: routing always uses only what
        /// <see cref="SetHostProviders"/> passed (there is no automatic host bridge any more). The providers survive
        /// NGO re-registration / reconnects until <c>SetHostProviders(null, ...)</c>.</summary>
        public static bool ExternalHostProviders;

        /// <summary>Server clock in seconds used for recording start times and progress. Null = default:
        /// NetworkManager.ServerTime.Time (realtime when offline). Point it at LineClient.Clock if that differs.</summary>
        public static Func<double> ServerTimeSource;

        // ------------------------------------------------------------------ public API (UI / integration)

        public VoiceSettings Settings { get; private set; }
        /// <summary>Distances and levels (host routing + client attenuation). Change before a match starts.</summary>
        public readonly VoiceRoutingSettings Routing = new VoiceRoutingSettings();
        public VoiceLaunchOptions Options { get; private set; }
        /// <summary>Codec actually in use (Native = libopus, Managed = Concentus fallback).</summary>
        public VoiceCodecBackend Backend { get; private set; }

        /// <summary>(slot, speaking). Fires on change only, on the main thread. Includes the local player.</summary>
        public event Action<int, bool> OnSpeakingChanged;

        /// <summary>Slot → player name, used to persist per-player volume. Null = default: the LineClient lobby.
        /// Volumes re-read automatically when a slot's name changes.</summary>
        public Func<int, string> SlotNameResolver;

        /// <summary>Settings screen open: run the microphone (level meter) even outside a session.</summary>
        public bool MicTest { get; set; }
        /// <summary>Hear your own encoded voice (mic test). Also enabled by -voiceloopback.</summary>
        public bool LoopbackTest { get; set; }

        /// <summary>0..1 level of the local microphone (after input gain).</summary>
        public float LocalMicLevel => _capture != null ? _capture.Level01 : 0f;
        /// <summary>True while the local player's voice is being sent.</summary>
        public bool IsTransmitting => _capture != null && _capture.IsTransmitting && (_inSession || LoopbackActive);
        public bool MicrophoneAvailable => _synthetic != null || (_mic != null && _mic.IsRunning);
        public string MicrophoneError => _mic != null ? _mic.LastError : "";
        public string PushToTalkKey => VoiceInput.KeyLabel;
        public int LocalSlot => _localSlot;

        public bool IsSpeaking(int slot) { return slot >= 0 && slot < Slots && _speaking[slot]; }

        /// <summary>Smoothed speech level 0..1 of every player (local included) for talk animation: fast attack
        /// (30 ms), 120 ms release, independent of volume settings. Poll every frame; allocation-free.</summary>
        public float GetSpeechLevel(int slot) { return slot >= 0 && slot < Slots ? _level[slot] : 0f; }

        /// <summary>How the local player hears <paramref name="slot"/> right now (UI icons: phone, tap, wall...).</summary>
        public VoiceHearing GetHearing(int slot)
        {
            if (slot < 0 || slot >= Slots || slot == _localSlot || !_speaking[slot]) return VoiceHearing.None;
            var f = _channels[slot].CurrentFlags;
            if ((f & VoiceFrameFlags.Loopback) != 0) return VoiceHearing.Loopback;
            if ((f & VoiceFrameFlags.Tap) != 0) return VoiceHearing.Tap;
            if ((f & VoiceFrameFlags.Call) != 0) return VoiceHearing.Call;
            if ((f & VoiceFrameFlags.AllHear) != 0) return VoiceHearing.AllHear;
            if ((f & VoiceFrameFlags.Muffled) != 0) return VoiceHearing.Muffled;
            return VoiceHearing.Proximity;
        }

        public float GetPlayerVolume(int slot) { return slot >= 0 && slot < Slots ? _playerVolume[slot] : 1f; }

        public void SetPlayerVolume(int slot, float volume)
        {
            if (slot < 0 || slot >= Slots) return;
            _playerVolume[slot] = Mathf.Clamp(volume, 0f, 2f);
            string key = NameOf(slot);
            if (!string.IsNullOrEmpty(key)) Settings.SetPlayerVolume(key, _playerVolume[slot]);
        }

        /// <summary>Microphone names for the settings dropdown (allocates; call when the screen opens).</summary>
        public static string[] InputDevices => Microphone.devices;

        /// <summary>Makes the slot's voice come from this transform (default: none, the voice plays in 2D).</summary>
        public void AttachEmitter(int slot, Transform target)
        {
            if (slot < 0 || slot >= Slots) return;
            _attached[slot] = target;
            if (_emitters[slot] != null) _emitters[slot].Target = target;
        }

        public void DetachEmitter(int slot)
        {
            if (slot < 0 || slot >= Slots) return;
            _attached[slot] = null;
            if (_emitters[slot] != null) _emitters[slot].Target = null;
        }

        /// <summary>Listener position for distance attenuation (default: the scene's AudioListener).</summary>
        public Transform ListenerOverride;

        /// <summary>
        /// Host: the routing providers (call it when the host session starts). They stay in use across NGO
        /// re-registration until called again with <paramref name="positions"/> = null (no routing).
        /// </summary>
        public void SetHostProviders(IVoicePositions positions, IVoiceCallState calls, IVoiceModeProvider mode,
            IVoiceOcclusion occlusion, IVoiceClientMap clients)
        {
            _customProviders = positions != null;
            _clients = _customProviders ? clients : null;
            _router = _customProviders ? new ProximityVoiceRouter(positions, calls, mode, occlusion, Routing) : null;
            if (_customProviders) MatchLog.Write("voice: host routing uses external providers");
        }

        // ------------------------------------------------------------------ v3: call recordings (host)

        /// <summary>Host toggle (lobby, default on). Off = nothing is recorded and existing recordings are cleared.</summary>
        public bool RecordingEnabled
        {
            get => _recorder.Enabled;
            set { _recorder.Enabled = value; if (!value) ClearRecordings(); }
        }

        /// <summary>The host's recorder (stats, call infos, memory numbers). In memory only, never on disk.</summary>
        public VoiceCallRecorder Recorder => _recorder;

        /// <summary>Recorder chunks pre-allocated when a host session starts (so recording never allocates while
        /// playing). 0 = grow on demand. Default 1 MB ≈ 6.4 talking-minutes (a typical day is ≈ 1.7 MB; the rest grows
        /// in 64 KB chunks on day 1 and is reused afterwards).</summary>
        public long RecordingReserveBytes = 1L * 1024 * 1024;

        /// <summary>Host: forget all call audio (call after every meeting). Stops a running tape.</summary>
        public void ClearRecordings()
        {
            if (_streamer.Active) FinishHostTape(true);
            _recorder.Clear();
        }

        /// <summary>Host, "Kaydı Sil": the slot's calls of today (also later ones until <see cref="ClearRecordings"/>)
        /// are never selected, and their audio is no longer stored.</summary>
        public void EraseTape(int slot) { _recorder.EraseSlot(slot); }

        /// <summary>Host: the first candidate with at least <paramref name="minSpeechSeconds"/> of speech in its
        /// most speech-dense <paramref name="seconds"/> window (see <see cref="VoiceCallRecorder.TrySelectSnippet(IList{int}, out RecordingSnippet, int, int)"/>).
        /// Candidates in the engine's order (duration-weighted, erased tapes left out). No playback.</summary>
        public bool TrySelectRecording(IList<int> candidateCallIds, out RecordingSnippet snippet, float seconds = 7f, float minSpeechSeconds = 1.5f)
        {
            return _recorder.TrySelectSnippet(candidateCallIds, out snippet, (int)(seconds * 1000f), (int)(minSpeechSeconds * 1000f));
        }

        /// <summary>
        /// Host: plays <paramref name="snippet"/> to every client (and the host) starting at server time
        /// <paramref name="serverTimeSec"/> (≤ 0 = now; give ~0.3 s of lead so the start message arrives first).
        /// Streamed at real-time pace with the original speaker slots. Replaces a tape that is still running.
        /// </summary>
        public bool PlayRecording(in RecordingSnippet snippet, double serverTimeSec)
        {
            if (!IsHost || !_hostRouting || !snippet.IsValid) return false;
            if (_streamer.Active) FinishHostTape(true);
            double serverNow = ServerTimeNow();
            if (double.IsNaN(serverTimeSec) || serverTimeSec <= 0) serverTimeSec = serverNow;
            double nowMs = Time.realtimeSinceStartupAsDouble * 1000.0;
            double startMs = nowMs + (serverTimeSec - serverNow) * 1000.0;
            if (!_streamer.Begin(_recorder, snippet, startMs)) return false;
            _hostTapeInfo = new VoiceTapeInfo
            {
                PlaybackId = ++_playbackId, CallId = snippet.CallId, StartServerTime = serverTimeSec,
                CallOffsetMs = snippet.StartMs, DurationMs = snippet.LengthMs,
                SpeakersMask = snippet.SpeakersMask, ParticipantsMask = snippet.ParticipantsMask,
            };
            for (int r = 0; r < Slots; r++) _tapeNotified[r] = false;
            BeginLocalTape(_hostTapeInfo, nowMs, (serverTimeSec - serverNow) * 1000.0);
            NotifyTapeClients();
            MatchLog.Write("voice: tape play id=" + _playbackId + " " + snippet + " in " + ((serverTimeSec - serverNow) * 1000.0).ToString("0") + "ms");
            return true;
        }

        /// <summary>Host: <see cref="TrySelectRecording"/> + <see cref="PlayRecording"/>.</summary>
        public bool TryPlayBestRecording(IList<int> candidateCallIds, double serverTimeSec, out RecordingSnippet snippet, float seconds = 7f)
        {
            return TrySelectRecording(candidateCallIds, out snippet, seconds) && PlayRecording(snippet, serverTimeSec);
        }

        /// <summary>Host: stops a running tape now (clients drain what they have, then end).</summary>
        public void StopRecording() { if (_streamer.Active) FinishHostTape(true); }

        /// <summary>Host: a tape is being streamed.</summary>
        public bool HostRecordingPlaying => _streamer.Active;
        /// <summary>Host: the tape finished streaming (or was stopped). Main thread.</summary>
        public event Action<RecordingSnippet> OnHostRecordingFinished;

        // ------------------------------------------------------------------ v3: meeting tape (every client, host too)

        /// <summary>A recording plays on this machine (from the start message until the last frame faded).</summary>
        public bool RecordingActive => _tape != null && _tape.Active;
        /// <summary>Smoothed 0..1 voice level of the tape (waveform / VU needle; hiss excluded).</summary>
        public float RecordingLevel => _tape != null ? _tape.Level01 : 0f;
        /// <summary>Original slot of whoever is audible on the tape right now, or -1 (nameplate).</summary>
        public int RecordingSpeaker => _tape != null ? _tape.CurrentSpeaker : -1;
        /// <summary>The running (or last) tape: call id, start server time, duration, speakers (CallId -1 = unknown yet).</summary>
        public VoiceTapeInfo CurrentRecording => _tapeInfo;
        /// <summary>0..1 position in the tape window (from server time).</summary>
        public float RecordingProgress
        {
            get
            {
                if (_tape == null || !_tape.Active || _tapeInfo.DurationMs <= 0) return 0f;
                return Mathf.Clamp01((float)((ServerTimeNow() - _tapeInfo.StartServerTime) * 1000.0 / _tapeInfo.DurationMs));
            }
        }
        /// <summary>Main thread; fired once the tape info is known (or 0.3 s after the first frame).</summary>
        public event Action<VoiceTapeInfo> OnRecordingStarted;
        /// <summary>Main thread; after the last tape frame faded out (or the session ended).</summary>
        public event Action<VoiceTapeInfo> OnRecordingEnded;
        /// <summary>Live voices are scaled by this while a tape plays (the room quiets down a little).</summary>
        public float LiveDuckDuringRecording = 0.6f;
        /// <summary>Tape volume (× master volume).</summary>
        public float RecordingVolume = 1f;

        // ------------------------------------------------------------------ state

        VoiceCapturePipeline _capture;
        VoiceMicrophone _mic;
        SyntheticVoiceSource _synthetic;
        float[] _synthBuffer;
        double _synthAccumulator;
        double _nextMicAttemptMs;

        readonly VoicePlaybackChannel[] _channels = new VoicePlaybackChannel[Slots];
        readonly VoiceEmitter[] _emitters = new VoiceEmitter[Slots];
        readonly PcmPlayoutReader[] _readers = new PcmPlayoutReader[Slots];
        readonly Transform[] _attached = new Transform[Slots];
        readonly bool[] _speaking = new bool[Slots];
        readonly float[] _level = new float[Slots];
        readonly float[] _playerVolume = { 1f, 1f, 1f, 1f, 1f, 1f };
        readonly string[] _volumeKeys = new string[Slots];
        float _nextTargetRefresh;
        Transform _audioListener;

        readonly VoiceBundleWriter _up = new VoiceBundleWriter();
        readonly VoiceBundleWriter[] _down = new VoiceBundleWriter[Slots];
        readonly ulong[] _downClient = new ulong[Slots];
        byte[] _rxBuffer = new byte[2048];
        VoiceBundleReader _rx;
        readonly VoiceRoute[] _routes = new VoiceRoute[Slots];
        readonly VoiceRateLimiter _limiter = new VoiceRateLimiter();
        readonly VoiceTransportCounters _net = new VoiceTransportCounters();
        readonly VoiceStatsReporter _stats = new VoiceStatsReporter();
        FastBufferWriter _writer;
        bool _writerReady;

        ProximityVoiceRouter _router;
        IVoiceClientMap _clients;
        bool _customProviders;
        bool _hostRouting;

        // Recordings (host) and the tape (everyone).
        readonly VoiceCallRecorder _recorder = new VoiceCallRecorder();
        readonly VoiceRecordingStreamer _streamer = new VoiceRecordingStreamer();
        readonly bool[] _tapeNotified = new bool[Slots];
        readonly ulong[] _tapeNotifiedClient = new ulong[Slots];
        readonly byte[] _tapeTx = new byte[VoiceTapeMessage.Size];
        byte[] _tapeRx = new byte[64];
        VoiceTapeInfo _hostTapeInfo;
        ushort _playbackId;
        VoiceTapePlayer _tape;
        VoiceEmitter _tapeEmitter;
        VoiceTapeInfo _tapeInfo = new VoiceTapeInfo { CallId = -1 };
        bool _tapeKnown, _tapeStartFired;
        double _tapeBeganMs, _tapeEndedAtMs = -1e9, _tapeLastFrameMs;

        NetworkManager _nm;
        CustomMessagingManager _registered;
        CustomMessagingManager.HandleNamedMessageDelegate _onUp, _onDown, _onTape;
        bool _inSession;
        int _localSlot = -1;
        double _nowMs;
        float _dtAvg = 1f / 60f;
        int _outputRate;
        double _nextStatsMs;
        bool _sendErrorLogged;

        bool LoopbackActive => LoopbackTest || (Options != null && Options.Loopback);
        bool IsHost => _registered != null && _nm != null && _nm.IsServer;

        // ------------------------------------------------------------------ lifecycle

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            Options = VoiceLaunchOptions.Parse(Environment.GetCommandLineArgs());
            Settings = VoiceSettings.Load();
            if (Options.Disabled)
            {
                MatchLog.Write("voice disabled (-novoice)");
                enabled = false;
                return;
            }
            Settings.Changed += OnSettingsChanged;

            Backend = VoiceCodec.Resolve(Options.Codec);
            var encoder = VoiceCodec.CreateEncoder(VoiceEncoderSettings.Default, Backend);
            Backend = encoder.Backend;
            ushort seq0 = (ushort)UnityEngine.Random.Range(0, 65536);
            _capture = new VoiceCapturePipeline(encoder, 48000, seq0) { Sink = this };

            if (!string.IsNullOrEmpty(Options.TestSource))
            {
                _synthetic = SyntheticVoiceSource.Create(Options.TestSource, (int)(seq0 % 6));
                _synthBuffer = new float[4096];
                _capture.SetInputRate(SyntheticVoiceSource.SampleRate);
            }
            else _mic = new VoiceMicrophone();

            _outputRate = AudioSettings.outputSampleRate;
            for (int i = 0; i < Slots; i++)
            {
                _channels[i] = new VoicePlaybackChannel(i, VoiceCodec.CreateDecoder(VoiceFormat.PlaybackSampleRate, Backend), Routing.MuffledCutoffHz);
                _emitters[i] = CreateEmitter("VoiceEmitter_" + i, i, _channels[i]);
                _readers[i] = _emitters[i].Reader;
                _down[i] = new VoiceBundleWriter();
            }
            _tape = new VoiceTapePlayer(VoiceCodec.CreateDecoder(VoiceFormat.PlaybackSampleRate, Backend),
                                        VoiceCodec.CreateDecoder(VoiceFormat.PlaybackSampleRate, Backend));
            _tapeEmitter = CreateEmitter("VoiceEmitter_Tape", -1, _tape);

            _writer = new FastBufferWriter(VoiceWire.MaxBundleBytes + 16, Allocator.Persistent);
            _writerReady = true;
            _onUp = OnUpMessage;
            _onDown = OnDownMessage;
            _onTape = OnTapeMessage;
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
            ApplySettings();
            MatchLog.Write("voice init codec=" + Backend + (Backend == VoiceCodecBackend.Native ? " (" + NativeOpus.Version + ")" : " (Concentus; native: " + NativeOpus.LoadError + ")") +
                           " source=" + (_synthetic != null ? _synthetic.Description : "microphone") + " output=" + _outputRate + "Hz");
        }

        VoiceEmitter CreateEmitter(string name, int slot, IVoicePcmSource source)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<AudioSource>();                // generator first,
            var em = go.AddComponent<VoiceEmitter>();      // then the filter that writes the voice
            em.Init(slot, source, _outputRate);
            return em;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
            if (Settings != null) Settings.Changed -= OnSettingsChanged;
            Unregister();
            _mic?.Stop();
            for (int i = 0; i < Slots; i++) _channels[i]?.Dispose();
            _tape?.Dispose();
            _recorder.Trim();
            if (_writerReady) { _writer.Dispose(); _writerReady = false; }
        }

        void OnSettingsChanged() { ApplySettings(); }

        void ApplySettings()
        {
            if (_capture == null) return;
            _capture.Vad.SetSensitivity(Settings.VadSensitivity);
            _capture.InputGain = Settings.InputGain;
            if (_mic != null && _mic.IsRunning && _mic.RequestedDevice != Settings.InputDevice)
            {
                // Device switched in the settings screen ("" = system default): restart on the next frame.
                _mic.Stop();
                _capture.ForceClose();
                _nextMicAttemptMs = 0;
            }
            for (int i = 0; i < Slots; i++)
            {
                if (_channels[i] == null) continue;
                _channels[i].Jitter.BaseDelayMs = _basePlayoutDelayMs;
                _channels[i].PhoneEqEnabled = Settings.PhoneEq;
                _channels[i].TapGain = Routing.TapGain;
            }
            RefreshPlayerVolumes();
        }

        int _basePlayoutDelayMs = 60;

        /// <summary>Minimum jitter-buffer delay (ms, 20..200). Lower = less latency, more concealment on bad links.</summary>
        public int BasePlayoutDelayMs
        {
            get => _basePlayoutDelayMs;
            set { _basePlayoutDelayMs = Mathf.Clamp(value, 20, 200); ApplySettings(); }
        }

        void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            _outputRate = AudioSettings.outputSampleRate;
            for (int i = 0; i < Slots; i++)
            {
                _emitters[i]?.SetOutputRate(_outputRate);
                _emitters[i]?.StopNow(); // the device restart stops sources; Tick restarts them
            }
            _tapeEmitter?.SetOutputRate(_outputRate);
            _tapeEmitter?.StopNow();
        }

        // ------------------------------------------------------------------ frame

        void Update()
        {
            _nowMs = Time.realtimeSinceStartupAsDouble * 1000.0;
            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0.001f, 0.25f);
            _dtAvg += (dt - _dtAvg) * 0.05f;

            UpdateNetwork();
            UpdateHostTape();
            UpdateCapture(dt);
            UpdatePlayback(dt);
            UpdateSpeaking(dt);
            UpdateTapeState();
            UpdateStats();
        }

        void LateUpdate()
        {
            Flush();
        }

        void UpdateNetwork()
        {
            var nm = NetworkManager.Singleton;
            var cmm = nm != null && nm.IsListening && !nm.ShutdownInProgress ? nm.CustomMessagingManager : null;
            if (cmm != _registered)
            {
                if (_registered != null) EndSession();
                if (cmm != null) BeginSession(nm, cmm);
            }
            int slot = LocalSlotSource != null ? LocalSlotSource() : DefaultLocalSlot();
            slot = slot >= 0 && slot < Slots ? slot : -1;
            if (slot != _localSlot && slot >= 0 && _synthetic != null) _synthetic.SetSlot(slot); // players take turns
            _localSlot = slot;
            _inSession = _registered != null && _localSlot >= 0;

            _hostRouting = false;
            if (_registered != null && _nm.IsServer)
            {
                bool up = HostSessionActive != null ? HostSessionActive() : _customProviders;
                _hostRouting = up && _router != null && _clients != null;
            }
        }

        void BeginSession(NetworkManager nm, CustomMessagingManager cmm)
        {
            _nm = nm;
            _registered = cmm;
            try
            {
                if (nm.IsServer) cmm.RegisterNamedMessageHandler(UpMessage, _onUp);
                else
                {
                    cmm.RegisterNamedMessageHandler(DownMessage, _onDown);
                    cmm.RegisterNamedMessageHandler(TapeMessage, _onTape);
                }
            }
            catch (Exception e) { Debug.LogException(e); }
            ResetPlayback();
            _up.Reset();
            for (int i = 0; i < Slots; i++) _down[i].Reset();
            if (nm.IsServer)
            {
                _recorder.Clear();
                if (_recorder.Enabled && RecordingReserveBytes > 0) _recorder.Reserve(RecordingReserveBytes);
            }
            MatchLog.Write("voice: session " + (nm.IsServer ? "host" : "client") + " codec=" + Backend);
        }

        void EndSession()
        {
            if (_streamer.Active) FinishHostTape(false);
            Unregister();
            _capture?.ForceClose();
            _up.Reset();
            for (int i = 0; i < Slots; i++) _down[i].Reset();
            ResetPlayback();
            if (!_customProviders) { _router = null; _clients = null; }
            _hostRouting = false;
            _recorder.Trim(); // call audio never outlives the session
            _registered = null;
            _nm = null;
            _inSession = false;
            MatchLog.Write("voice: session ended");
        }

        void Unregister()
        {
            if (_registered == null) return;
            try
            {
                _registered.UnregisterNamedMessageHandler(UpMessage);
                _registered.UnregisterNamedMessageHandler(DownMessage);
                _registered.UnregisterNamedMessageHandler(TapeMessage);
            }
            catch (Exception) { /* the manager may already be torn down */ }
        }

        void ResetPlayback()
        {
            for (int i = 0; i < Slots; i++)
            {
                _channels[i]?.Reset();
                _emitters[i]?.StopNow();
            }
            _tape?.Stop();
            _tapeEmitter?.StopNow();
        }

        // ------------------------------------------------------------------ default binding (LineClient)
        // The only place that reads the session objects; every one of them can be replaced by a hook above.

        static int DefaultLocalSlot()
        {
            var line = LineClient.I;
            return line != null && line.MySlot >= 0 ? line.MySlot : -1;
        }

        static string DefaultSlotName(int slot)
        {
            var line = LineClient.I;
            var lobby = line != null ? line.Lobby : null;
            if (lobby == null) return null;
            var ls = lobby.Slots;
            for (int i = 0; i < ls.Count; i++)
                if (ls[i].Slot == slot && ls[i].Occupied) return ls[i].Name;
            return null;
        }

        double ServerTimeNow()
        {
            if (ServerTimeSource != null) return ServerTimeSource();
            if (_nm != null && _nm.IsListening) return _nm.ServerTime.Time;
            return Time.realtimeSinceStartupAsDouble;
        }

        // ------------------------------------------------------------------ capture

        void UpdateCapture(float dt)
        {
            if (_capture == null) return;
            bool want = _inSession || MicTest || LoopbackActive;
            _capture.PushToTalk = _synthetic == null && Settings.InputMode == VoiceInputMode.PushToTalk;
            _capture.PttHeld = VoiceInput.PushToTalkHeld;
            _capture.SelfMuted = Settings.SelfMute || Settings.Deafen;
            _capture.TransmitEnabled = _inSession || LoopbackActive;

            if (_synthetic != null)
            {
                if (!want) return;
                _synthAccumulator += dt * SyntheticVoiceSource.SampleRate;
                int n = (int)_synthAccumulator;
                _synthAccumulator -= n;
                while (n > 0)
                {
                    int k = Math.Min(n, _synthBuffer.Length);
                    _synthetic.Read(_synthBuffer, 0, k);
                    _capture.Process(_synthBuffer, 0, k);
                    n -= k;
                }
                return;
            }

            if (!want)
            {
                if (_mic.IsRunning) { _mic.Stop(); _capture.ForceClose(); }
                return;
            }
            if (_mic.IsRunning && !_mic.StillRecording)
            {
                MatchLog.Write("voice: microphone stopped (unplugged?) - retrying");
                _mic.Stop();
                _capture.ForceClose();
                _nextMicAttemptMs = _nowMs + 2000;
            }
            if (!_mic.IsRunning && _nowMs >= _nextMicAttemptMs)
            {
                if (_mic.Start(Settings.InputDevice))
                {
                    _capture.SetInputRate(_mic.SampleRate);
                    MatchLog.Write("voice: microphone '" + _mic.ActiveDevice + "' at " + _mic.SampleRate + " Hz");
                }
                else
                {
                    _nextMicAttemptMs = _nowMs + 5000;
                    MatchLog.Write("voice: microphone unavailable (" + _mic.LastError + ")");
                }
            }
            if (_mic.IsRunning) _mic.Read(_capture);
        }

        /// <summary>Encoded local frame (from the capture pipeline, main thread).</summary>
        public void OnVoiceFrame(ushort sequence, VoiceFrameFlags flags, byte[] payload, int length)
        {
            flags &= VoiceFrameFlags.EndOfSpeech;
            if (LoopbackActive)
            {
                int loopSlot = _localSlot >= 0 ? _localSlot : 0;
                _channels[loopSlot].Receive(sequence, flags | VoiceFrameFlags.Loopback, payload, 0, length, _nowMs);
            }
            if (!_inSession) return;
            if (_nm.IsServer)
            {
                HostRoute(_localSlot, sequence, flags, payload, 0, length, _nowMs);
            }
            else if (!_up.TryAppend(_localSlot, sequence, flags, payload, 0, length))
            {
                SendBundle(UpMessage, NetworkManager.ServerClientId, _up);
                _up.TryAppend(_localSlot, sequence, flags, payload, 0, length);
            }
        }

        // ------------------------------------------------------------------ host routing

        void HostRoute(int speaker, ushort seq, VoiceFrameFlags endFlag, byte[] buffer, int offset, int length, double nowMs)
        {
            if (!_hostRouting) return;
            int n = _router.Route(speaker, _routes);
            if (_router.LastCallId >= 0)
                _recorder.Record(_router.LastCallId, speaker, _router.LastCallPartner, seq, endFlag, buffer, offset, length, nowMs);
            for (int i = 0; i < n; i++)
            {
                int r = _routes[i].Recipient;
                var f = _routes[i].Flags | endFlag;
                if (r == _localSlot)
                {
                    _channels[speaker].Receive(seq, f, buffer, offset, length, nowMs); // the host hears without the network
                    continue;
                }
                AppendDown(r, speaker, seq, f, buffer, offset, length);
            }
        }

        void AppendDown(int recipient, int speaker, ushort seq, VoiceFrameFlags flags, byte[] buffer, int offset, int length)
        {
            ulong clientId;
            if (!_clients.TryGetClient(recipient, out clientId)) return;
            var w = _down[recipient];
            if (!w.IsEmpty && _downClient[recipient] != clientId) w.Reset(); // slot changed hands
            _downClient[recipient] = clientId;
            if (!w.TryAppend(speaker, seq, flags, buffer, offset, length))
            {
                SendBundle(DownMessage, clientId, w);
                w.TryAppend(speaker, seq, flags, buffer, offset, length);
            }
        }

        void OnUpMessage(ulong senderClientId, FastBufferReader reader)
        {
            int len = reader.Length - reader.Position;
            if (len <= 0 || len > _rxBuffer.Length) { _net.Malformed++; return; }
            reader.ReadBytesSafe(ref _rxBuffer, len, 0);
            _net.UpMessages++;
            _net.UpBytes += len;
            int slot = _hostRouting ? _clients.SlotOfClient(senderClientId) : -1;
            if (slot < 0) { _net.UnknownSender++; return; }
            if (!_rx.Begin(_rxBuffer, len)) { _net.Malformed++; return; }
            double now = Time.realtimeSinceStartupAsDouble * 1000.0;
            VoiceFrame f;
            while (_rx.TryNext(out f))
            {
                if (!_limiter.Allow(slot, now)) { _net.RateLimited++; continue; }
                // The claimed slot and flags are ignored: the host knows the sender and decides the routing.
                HostRoute(slot, f.Sequence, f.Flags & VoiceFrameFlags.EndOfSpeech, _rxBuffer, f.PayloadOffset, f.PayloadLength, now);
            }
        }

        // ------------------------------------------------------------------ host tape

        void UpdateHostTape()
        {
            if (!_streamer.Active) return;
            if (!_hostRouting) { FinishHostTape(false); return; }
            NotifyTapeClients();
            _streamer.Tick(Time.realtimeSinceStartupAsDouble * 1000.0, this);
            if (_streamer.IsDone) FinishHostTape(true);
        }

        /// <summary>Start message to every connected client that has not got it yet (also mid-playback joiners).</summary>
        void NotifyTapeClients()
        {
            var positions = _router.Positions;
            for (int r = 0; r < Slots; r++)
            {
                if (r == _localSlot) continue;
                ulong id;
                if (!positions.IsActive(r) || !_clients.TryGetClient(r, out id)) { _tapeNotified[r] = false; continue; }
                if (_tapeNotified[r] && _tapeNotifiedClient[r] == id) continue;
                SendTape(id, VoiceTapeMessage.KindStart);
                _tapeNotified[r] = true;
                _tapeNotifiedClient[r] = id;
            }
        }

        void FinishHostTape(bool sendEnd)
        {
            var snippet = _streamer.Snippet;
            int sent = _streamer.FramesSent;
            for (int r = 0; r < Slots; r++)
            {
                if (sendEnd && _tapeNotified[r] && _registered != null) SendTape(_tapeNotifiedClient[r], VoiceTapeMessage.KindEnd);
                _tapeNotified[r] = false;
            }
            double now = Time.realtimeSinceStartupAsDouble * 1000.0;
            if (_tape != null && _tapeKnown && _tapeInfo.PlaybackId == _hostTapeInfo.PlaybackId) _tape.End(now);
            _tapeEndedAtMs = now;
            _streamer.Stop();
            MatchLog.Write("voice: tape end id=" + _hostTapeInfo.PlaybackId + " frames=" + sent);
            OnHostRecordingFinished?.Invoke(snippet);
        }

        /// <summary>Streamer output (host): to the local tape and to every connected client.</summary>
        public void OnRecordingFrame(int slot, ushort sequence, VoiceFrameFlags flags, byte[] payload, int offset, int length)
        {
            double now = Time.realtimeSinceStartupAsDouble * 1000.0;
            if (_tape != null && _tape.Active)
            {
                _tape.Receive(slot, sequence, flags, payload, offset, length, now);
                _tapeLastFrameMs = now;
            }
            if (!_hostRouting) return;
            var positions = _router.Positions;
            for (int r = 0; r < Slots; r++)
            {
                if (r == _localSlot || !positions.IsActive(r)) continue;
                AppendDown(r, slot, sequence, flags, payload, offset, length);
            }
        }

        void SendTape(ulong clientId, byte kind)
        {
            if (!_writerReady || _registered == null) return;
            int len = VoiceTapeMessage.Write(_tapeTx, kind, _hostTapeInfo);
            _writer.Seek(0);
            _writer.Truncate();
            if (!_writer.TryBeginWrite(len)) return;
            _writer.WriteBytes(_tapeTx, len, 0);
            try { _registered.SendNamedMessage(TapeMessage, clientId, _writer, NetworkDelivery.Reliable); }
            catch (Exception e)
            {
                if (!_sendErrorLogged) { _sendErrorLogged = true; Debug.LogWarning("[Voice] tape send failed: " + e.Message); }
            }
        }

        // ------------------------------------------------------------------ client receive

        void OnDownMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (senderClientId != NetworkManager.ServerClientId) return;
            int len = reader.Length - reader.Position;
            if (len <= 0 || len > _rxBuffer.Length) { _net.Malformed++; return; }
            reader.ReadBytesSafe(ref _rxBuffer, len, 0);
            _net.DownMessages++;
            _net.DownBytes += len;
            if (!_rx.Begin(_rxBuffer, len)) { _net.Malformed++; return; }
            double now = Time.realtimeSinceStartupAsDouble * 1000.0;
            VoiceFrame f;
            while (_rx.TryNext(out f))
            {
                if ((f.Flags & VoiceFrameFlags.Recording) != 0)
                {
                    // Tape frames carry the ORIGINAL speaker (possibly us): never into the live channel.
                    ReceiveTapeFrame(f.Slot, f.Sequence, f.Flags, _rxBuffer, f.PayloadOffset, f.PayloadLength, now);
                    continue;
                }
                if (f.Slot == _localSlot) continue;
                _channels[f.Slot].Receive(f.Sequence, f.Flags, _rxBuffer, f.PayloadOffset, f.PayloadLength, now);
            }
        }

        void ReceiveTapeFrame(int slot, ushort seq, VoiceFrameFlags flags, byte[] buffer, int offset, int length, double now)
        {
            if (!_tape.Active)
            {
                if (now - _tapeEndedAtMs < 1000) return; // stragglers of a finished tape
                // Frame overtook the (reliable) start message: begin now, the info follows.
                _tape.Begin();
                _tapeInfo = new VoiceTapeInfo { CallId = -1 };
                _tapeKnown = false;
                _tapeStartFired = false;
                _tapeBeganMs = now;
            }
            _tape.Receive(slot, seq, flags, buffer, offset, length, now);
            _tapeLastFrameMs = now;
        }

        void OnTapeMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (senderClientId != NetworkManager.ServerClientId) return;
            int len = reader.Length - reader.Position;
            if (len < VoiceTapeMessage.Size || len > _tapeRx.Length) { _net.Malformed++; return; }
            reader.ReadBytesSafe(ref _tapeRx, len, 0);
            byte kind;
            VoiceTapeInfo info;
            if (!VoiceTapeMessage.TryRead(_tapeRx, len, out kind, out info)) { _net.Malformed++; return; }
            double now = Time.realtimeSinceStartupAsDouble * 1000.0;
            if (kind == VoiceTapeMessage.KindStart)
            {
                if (_tape.Active && (!_tapeKnown || _tapeInfo.PlaybackId == info.PlaybackId))
                {
                    _tapeInfo = info; // implicit begin (frames came first) or a repeated start: keep playing
                    _tapeKnown = true;
                    return;
                }
                BeginLocalTape(info, now, (info.StartServerTime - ServerTimeNow()) * 1000.0);
            }
            else
            {
                if (_tape.Active && (!_tapeKnown || _tapeInfo.PlaybackId == info.PlaybackId)) _tape.End(now);
                _tapeEndedAtMs = now;
            }
        }

        void BeginLocalTape(in VoiceTapeInfo info, double nowMs, double startsInMs)
        {
            if (_tape == null) return;
            if (_tapeStartFired)
            {
                _tapeStartFired = false;
                OnRecordingEnded?.Invoke(_tapeInfo); // a new tape replaces one that was still running
            }
            _tape.Begin();
            _tapeInfo = info;
            _tapeKnown = true;
            _tapeStartFired = false;
            _tapeBeganMs = nowMs;
            // The silence timeout counts from the scheduled start, not from now.
            _tapeLastFrameMs = nowMs + Math.Max(0.0, Math.Min(startsInMs, 10000.0));
        }

        /// <summary>Started/ended events and the client safety timeout (main thread, change-only).</summary>
        void UpdateTapeState()
        {
            if (_tape == null) return;
            bool active = _tape.Active;
            if (active && !_tape.Draining && _nowMs - _tapeLastFrameMs > TapeSilenceTimeoutMs)
            {
                _tape.End(_nowMs); // host vanished without an end message
                _tapeEndedAtMs = _nowMs;
            }
            if (active && !_tapeStartFired && (_tapeKnown || _nowMs - _tapeBeganMs > 300))
            {
                _tapeStartFired = true;
                OnRecordingStarted?.Invoke(_tapeInfo);
            }
            else if (!active && _tapeStartFired)
            {
                _tapeStartFired = false;
                OnRecordingEnded?.Invoke(_tapeInfo);
            }
        }

        // ------------------------------------------------------------------ send

        void Flush()
        {
            if (_registered == null || _nm == null || !_nm.IsListening) return;
            if (!_nm.IsServer)
            {
                if (!_up.IsEmpty) SendBundle(UpMessage, NetworkManager.ServerClientId, _up);
                return;
            }
            for (int r = 0; r < Slots; r++)
                if (!_down[r].IsEmpty) SendBundle(DownMessage, _downClient[r], _down[r]);
        }

        void SendBundle(string message, ulong clientId, VoiceBundleWriter bundle)
        {
            if (!_writerReady || _registered == null || bundle.IsEmpty) { bundle.Reset(); return; }
            _writer.Seek(0);
            _writer.Truncate();
            if (_writer.TryBeginWrite(bundle.Length))
            {
                _writer.WriteBytes(bundle.Buffer, bundle.Length, 0);
                try
                {
                    // Unreliable: voice tolerates loss (FEC/PLC) and the jitter buffer reorders by sequence number.
                    _registered.SendNamedMessage(message, clientId, _writer, NetworkDelivery.Unreliable);
                    if (message == UpMessage) { _net.UpMessages++; _net.UpBytes += bundle.Length; }
                    else { _net.DownMessages++; _net.DownBytes += bundle.Length; }
                }
                catch (Exception e)
                {
                    if (!_sendErrorLogged) { _sendErrorLogged = true; Debug.LogWarning("[Voice] send failed: " + e.Message); }
                }
            }
            bundle.Reset();
        }

        // ------------------------------------------------------------------ playback

        void UpdatePlayback(float dt)
        {
            if (Time.unscaledTime >= _nextTargetRefresh)
            {
                _nextTargetRefresh = Time.unscaledTime + 0.5f;
                for (int i = 0; i < Slots; i++) _emitters[i].Target = _attached[i];
                if (ListenerOverride == null && _audioListener == null)
                {
                    var al = FindAnyObjectByType<AudioListener>();
                    _audioListener = al != null ? al.transform : null;
                }
                RefreshVolumeKeys(false);
            }

            Transform listener = ListenerOverride;
            if (listener == null) listener = _audioListener; // the first-person camera carries the AudioListener

            // Ring low-water mark: one audio DSP buffer + ~1.5 frames of main-thread jitter + 10 ms safety.
            int dspLength, dspCount;
            AudioSettings.GetDSPBufferSize(out dspLength, out dspCount);
            int outRate = _outputRate > 0 ? _outputRate : 48000;
            int lowWater = (int)(dspLength * (double)VoiceFormat.PlaybackSampleRate / outRate + 1.5 * _dtAvg * VoiceFormat.PlaybackSampleRate) + 480;
            lowWater = Mathf.Clamp(lowWater, VoiceFormat.PlaybackFrameSamples, VoiceFormat.PlaybackFrameSamples * 10);
            int critical = (int)(dspLength * (double)VoiceFormat.PlaybackSampleRate / outRate + _dtAvg * VoiceFormat.PlaybackSampleRate);
            critical = Mathf.Clamp(critical, VoiceFormat.PlaybackFrameSamples / 2, lowWater);

            float master = Settings.Deafen ? 0f : Settings.MasterVolume;
            float duck = _tape.Active ? LiveDuckDuringRecording : 1f;
            for (int i = 0; i < Slots; i++)
            {
                var ch = _channels[i];
                var em = _emitters[i];
                ch.VolumeScale = master * _playerVolume[i] * duck;
                ch.MuffledGain = Routing.MuffledGain;
                if (em.Target != null && listener != null)
                {
                    float d = Vector3.Distance(listener.position, em.Target.position);
                    ch.ProximityGain = VoiceAttenuation.Gain(d, Routing.FullVolumeDistance, Routing.MaxDistance);
                }
                else ch.ProximityGain = 0.6f; // no positions (calls / meetings ignore it): play softly in 2D
                ch.Pump(_nowMs, lowWater, critical);
                em.Tick(_nowMs, dt);
            }
            _tape.Volume = master * RecordingVolume;
            _tape.Pump(_nowMs, lowWater, critical);
            _tapeEmitter.Tick(_nowMs, dt);
        }

        void UpdateSpeaking(float dt)
        {
            for (int i = 0; i < Slots; i++)
            {
                bool s = i == _localSlot ? IsTransmitting : _channels[i].IsSpeaking(_nowMs);
                float target = !s ? 0f : (i == _localSlot ? VoiceLevel.Speech01(_capture.LevelDb) : _channels[i].FrameLevel01);
                _level[i] = VoiceLevel.Smooth(_level[i], target, dt, 0.03f, 0.12f);
                if (s == _speaking[i]) continue;
                _speaking[i] = s;
                OnSpeakingChanged?.Invoke(i, s);
            }
        }

        void UpdateStats()
        {
            float interval = Options.EffectiveStatsSeconds;
            if (interval <= 0 || _nowMs < _nextStatsMs) return;
            bool first = _nextStatsMs == 0;
            _nextStatsMs = _nowMs + interval * 1000.0;
            bool host = _nm != null && _nm.IsServer;
            string line = _stats.Build(_nowMs, Backend.ToString(), _capture, _channels, _readers,
                host ? _router : null, _net, host ? _recorder : null);
            if (line != null && !first) MatchLog.Write(line);
        }

        // ------------------------------------------------------------------ per-player volume

        string NameOf(int slot) { return SlotNameResolver != null ? SlotNameResolver(slot) : DefaultSlotName(slot); }

        /// <summary>Re-reads every per-player volume (also done automatically when a slot's name changes and on
        /// every settings change).</summary>
        public void RefreshPlayerVolumes() { RefreshVolumeKeys(true); }

        /// <summary>Polled every 0.5 s: only a slot whose player name changed re-reads its volume (no allocation
        /// otherwise; works with any <see cref="SlotNameResolver"/>, no lobby events needed).</summary>
        void RefreshVolumeKeys(bool force)
        {
            if (Settings == null) return;
            for (int i = 0; i < Slots; i++)
            {
                string key = NameOf(i);
                if (!force && string.Equals(key, _volumeKeys[i], StringComparison.Ordinal)) continue;
                _volumeKeys[i] = key;
                _playerVolume[i] = string.IsNullOrEmpty(key) ? 1f : Settings.GetPlayerVolume(key);
            }
        }
    }
}
