using System;
using BirimizKovulacak.Voice;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// In-game voice (replaces Discord): proximity voice in the office, private 1:1 calls, everyone-hears in the
    /// lobby / meeting / results. One instance per process (add it next to ConnectionManager).
    /// <para>Data flow: mic (or -voicetest source) → <see cref="VoiceCapturePipeline"/> (16 kHz, 20 ms Opus) →
    /// client: bundle → NGO named message "bk.voice.up" (Unreliable, own sequence numbers) → host:
    /// <see cref="ProximityVoiceRouter"/> decides recipients + flags per frame → "bk.voice.down" per recipient →
    /// <see cref="VoicePlaybackChannel"/> (jitter buffer, FEC/PLC, gain, muffle) → <see cref="VoiceEmitter"/>.
    /// The host's own mic and speakers use the same router without the network.</para>
    /// <para>Everything is created automatically: on the host the providers come from
    /// <see cref="HostMatchVoiceBridge"/> (ConnectionManager.I.HostController); emitters follow
    /// <see cref="PlayerAvatar.BySlot"/>. Integration hooks: <see cref="CallPartnerResolver"/>,
    /// <see cref="SpeakerFilter"/>, <see cref="AttachEmitter"/>, <see cref="SetHostProviders"/>.</para>
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class VoiceSystem : MonoBehaviour, IVoiceFrameSink
    {
        public const string UpMessage = "bk.voice.up";
        public const string DownMessage = "bk.voice.down";
        const int Slots = VoiceFormat.MaxPlayers;

        public static VoiceSystem Instance { get; private set; }

        // ------------------------------------------------------------------ public API (UI / integration)

        public VoiceSettings Settings { get; private set; }
        /// <summary>Distances and levels (host routing + client attenuation). Change before a match starts.</summary>
        public readonly VoiceRoutingSettings Routing = new VoiceRoutingSettings();
        public VoiceLaunchOptions Options { get; private set; }
        /// <summary>Codec actually in use (Native = libopus, Managed = Concentus fallback).</summary>
        public VoiceCodecBackend Backend { get; private set; }

        /// <summary>(slot, speaking). Fires on change only, on the main thread. Includes the local player.</summary>
        public event Action<int, bool> OnSpeakingChanged;

        /// <summary>Host: slot → connected call partner (or -1). Wire to the engine's call state.</summary>
        public Func<int, int> CallPartnerResolver;
        /// <summary>Host: optional per-speaker permission (e.g. meeting defense).</summary>
        public Func<int, bool> SpeakerFilter;
        /// <summary>Host: forces a voice mode (null = from the match phase).</summary>
        public VoiceMode? ModeOverride { get; set; }
        /// <summary>Client: slot → player name, used to persist per-player volume (default: lobby names).</summary>
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

        /// <summary>How the local player hears <paramref name="slot"/> right now (UI icons: phone, wall...).</summary>
        public VoiceHearing GetHearing(int slot)
        {
            if (slot < 0 || slot >= Slots || slot == _localSlot || !_speaking[slot]) return VoiceHearing.None;
            var f = _channels[slot].CurrentFlags;
            if ((f & VoiceFrameFlags.Loopback) != 0) return VoiceHearing.Loopback;
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

        /// <summary>Makes the slot's voice come from this transform (default: the slot's PlayerAvatar).</summary>
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

        /// <summary>Listener position for distance attenuation (default: the local PlayerAvatar).</summary>
        public Transform ListenerOverride;

        /// <summary>Host: replaces the automatic <see cref="HostMatchVoiceBridge"/> (null = back to automatic).</summary>
        public void SetHostProviders(IVoicePositions positions, IVoiceCallState calls, IVoiceModeProvider mode,
            IVoiceOcclusion occlusion, IVoiceClientMap clients)
        {
            _customProviders = positions != null;
            _clients = clients;
            _router = positions == null ? null : new ProximityVoiceRouter(positions, calls, mode, occlusion, Routing);
            HostBridge = null;
        }

        /// <summary>The automatic host bridge (null on clients).</summary>
        public HostMatchVoiceBridge HostBridge { get; private set; }

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
        readonly float[] _playerVolume = { 1f, 1f, 1f, 1f, 1f, 1f };
        float _nextTargetRefresh;

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

        NetworkManager _nm;
        CustomMessagingManager _registered;
        CustomMessagingManager.HandleNamedMessageDelegate _onUp, _onDown;
        bool _inSession;
        int _localSlot = -1;
        double _nowMs;
        float _dtAvg = 1f / 60f;
        int _outputRate;
        double _nextStatsMs;
        bool _sendErrorLogged;

        bool LoopbackActive => LoopbackTest || (Options != null && Options.Loopback);

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
            VoiceInput.Initialize();

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
                var go = new GameObject("VoiceEmitter_" + i);
                go.transform.SetParent(transform, false);
                go.AddComponent<AudioSource>();                // generator first,
                var em = go.AddComponent<VoiceEmitter>();      // then the filter that writes the voice
                em.Init(i, _channels[i], _outputRate);
                _emitters[i] = em;
                _readers[i] = em.Reader;
                _down[i] = new VoiceBundleWriter();
            }

            _writer = new FastBufferWriter(VoiceWire.MaxBundleBytes + 16, Allocator.Persistent);
            _writerReady = true;
            _onUp = OnUpMessage;
            _onDown = OnDownMessage;
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
            ApplySettings();
            MatchLog.Write("voice init codec=" + Backend + (Backend == VoiceCodecBackend.Native ? " (" + NativeOpus.Version + ")" : " (Concentus; native: " + NativeOpus.LoadError + ")") +
                           " source=" + (_synthetic != null ? _synthetic.Description : "microphone") + " output=" + _outputRate + "Hz");
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
            if (Settings != null) Settings.Changed -= OnSettingsChanged;
            Unregister();
            _mic?.Stop();
            for (int i = 0; i < Slots; i++) _channels[i]?.Dispose();
            if (_writerReady) { _writer.Dispose(); _writerReady = false; }
        }

        void OnSettingsChanged() { ApplySettings(); }

        void ApplySettings()
        {
            if (_capture == null) return;
            _capture.Vad.SetSensitivity(Settings.VadSensitivity);
            _capture.InputGain = Settings.InputGain;
            if (_mic != null && _mic.IsRunning && _mic.ActiveDevice != Settings.InputDevice && !string.IsNullOrEmpty(Settings.InputDevice))
            {
                _mic.Stop();
                _nextMicAttemptMs = 0;
            }
            RefreshPlayerVolumes();
        }

        void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            _outputRate = AudioSettings.outputSampleRate;
            for (int i = 0; i < Slots; i++)
            {
                _emitters[i]?.SetOutputRate(_outputRate);
                _emitters[i]?.StopNow(); // the device restart stops sources; Tick restarts them
            }
        }

        // ------------------------------------------------------------------ frame

        void Update()
        {
            _nowMs = Time.realtimeSinceStartupAsDouble * 1000.0;
            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0.001f, 0.25f);
            _dtAvg += (dt - _dtAvg) * 0.05f;

            UpdateNetwork();
            UpdateCapture(dt);
            UpdatePlayback(dt);
            UpdateSpeaking();
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
            int slot = ClientState.I != null ? ClientState.I.MySlot : -1;
            _localSlot = slot >= 0 && slot < Slots ? slot : -1;
            _inSession = _registered != null && _localSlot >= 0;

            if (_registered != null && _nm.IsServer && !_customProviders)
            {
                var hc = ConnectionManager.I != null ? ConnectionManager.I.HostController : null;
                if (hc == null) { HostBridge = null; _router = null; _clients = null; }
                else if (HostBridge == null || HostBridge.Host != hc)
                {
                    HostBridge = new HostMatchVoiceBridge(hc) { MouthHeight = Routing.MouthHeight };
                    _router = new ProximityVoiceRouter(HostBridge, HostBridge, HostBridge, HostBridge, Routing);
                    _clients = HostBridge;
                    MatchLog.Write("voice: host routing ready");
                }
                if (HostBridge != null)
                {
                    HostBridge.CallPartnerResolver = CallPartnerResolver;
                    HostBridge.SpeakerFilter = SpeakerFilter;
                    HostBridge.ModeOverride = ModeOverride;
                }
            }
        }

        void BeginSession(NetworkManager nm, CustomMessagingManager cmm)
        {
            _nm = nm;
            _registered = cmm;
            try
            {
                if (nm.IsServer) cmm.RegisterNamedMessageHandler(UpMessage, _onUp);
                else cmm.RegisterNamedMessageHandler(DownMessage, _onDown);
            }
            catch (Exception e) { Debug.LogException(e); }
            ResetPlayback();
            _up.Reset();
            for (int i = 0; i < Slots; i++) _down[i].Reset();
            MatchLog.Write("voice: session " + (nm.IsServer ? "host" : "client") + " codec=" + Backend);
        }

        void EndSession()
        {
            Unregister();
            _capture?.ForceClose();
            _up.Reset();
            for (int i = 0; i < Slots; i++) _down[i].Reset();
            ResetPlayback();
            if (!_customProviders) { HostBridge = null; _router = null; _clients = null; }
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
            if (_router == null || _clients == null) return;
            int n = _router.Route(speaker, _routes);
            for (int i = 0; i < n; i++)
            {
                int r = _routes[i].Recipient;
                var f = _routes[i].Flags | endFlag;
                if (r == _localSlot)
                {
                    _channels[speaker].Receive(seq, f, buffer, offset, length, nowMs); // the host hears without the network
                    continue;
                }
                ulong clientId;
                if (!_clients.TryGetClient(r, out clientId)) continue;
                var w = _down[r];
                if (!w.IsEmpty && _downClient[r] != clientId) w.Reset(); // slot changed hands
                _downClient[r] = clientId;
                if (!w.TryAppend(speaker, seq, f, buffer, offset, length))
                {
                    SendBundle(DownMessage, clientId, w);
                    w.TryAppend(speaker, seq, f, buffer, offset, length);
                }
            }
        }

        void OnUpMessage(ulong senderClientId, FastBufferReader reader)
        {
            int len = reader.Length - reader.Position;
            if (len <= 0 || len > _rxBuffer.Length) { _net.Malformed++; return; }
            reader.ReadBytesSafe(ref _rxBuffer, len, 0);
            _net.UpMessages++;
            _net.UpBytes += len;
            int slot = _clients != null ? _clients.SlotOfClient(senderClientId) : -1;
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
                if (f.Slot == _localSlot) continue;
                _channels[f.Slot].Receive(f.Sequence, f.Flags, _rxBuffer, f.PayloadOffset, f.PayloadLength, now);
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
                for (int i = 0; i < Slots; i++)
                {
                    Transform t = _attached[i];
                    if (t == null)
                    {
                        var avatar = PlayerAvatar.BySlot(i);
                        t = avatar != null ? avatar.transform : null;
                    }
                    _emitters[i].Target = t;
                }
            }

            Transform listener = ListenerOverride;
            if (listener == null && PlayerAvatar.Local != null) listener = PlayerAvatar.Local.transform;

            // Ring low-water mark: one audio DSP buffer + ~1.5 frames of main-thread jitter + 10 ms safety.
            int dspLength, dspCount;
            AudioSettings.GetDSPBufferSize(out dspLength, out dspCount);
            int outRate = _outputRate > 0 ? _outputRate : 48000;
            int lowWater = (int)(dspLength * (double)VoiceFormat.PlaybackSampleRate / outRate + 1.5 * _dtAvg * VoiceFormat.PlaybackSampleRate) + 480;
            lowWater = Mathf.Clamp(lowWater, VoiceFormat.PlaybackFrameSamples, VoiceFormat.PlaybackFrameSamples * 10);
            int critical = (int)(dspLength * (double)VoiceFormat.PlaybackSampleRate / outRate + _dtAvg * VoiceFormat.PlaybackSampleRate);
            critical = Mathf.Clamp(critical, VoiceFormat.PlaybackFrameSamples / 2, lowWater);

            float master = Settings.Deafen ? 0f : Settings.MasterVolume;
            for (int i = 0; i < Slots; i++)
            {
                var ch = _channels[i];
                var em = _emitters[i];
                ch.VolumeScale = master * _playerVolume[i];
                ch.MuffledGain = Routing.MuffledGain;
                if (em.Target != null && listener != null)
                {
                    float d = Vector3.Distance(listener.position, em.Target.position);
                    ch.ProximityGain = VoiceAttenuation.Gain(d, Routing.FullVolumeDistance, Routing.MaxDistance);
                }
                else ch.ProximityGain = 0.6f; // no positions known (spectating / avatar not spawned): play softly in 2D
                ch.Pump(_nowMs, lowWater, critical);
                em.Tick(_nowMs, dt);
            }
        }

        void UpdateSpeaking()
        {
            for (int i = 0; i < Slots; i++)
            {
                bool s = i == _localSlot ? IsTransmitting : _channels[i].IsSpeaking(_nowMs);
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
            string line = _stats.Build(_nowMs, Backend.ToString(), _capture, _channels, _readers,
                _nm != null && _nm.IsServer ? _router : null, _net);
            if (line != null && !first) MatchLog.Write(line);
        }

        // ------------------------------------------------------------------ per-player volume

        string NameOf(int slot)
        {
            if (SlotNameResolver != null) return SlotNameResolver(slot);
            var lobby = ClientState.I != null ? ClientState.I.Lobby : null;
            if (lobby == null) return null;
            for (int i = 0; i < lobby.Slots.Count; i++)
                if (lobby.Slots[i].Slot == slot && lobby.Slots[i].Occupied) return lobby.Slots[i].Name;
            return null;
        }

        /// <summary>Re-reads per-player volumes (call after the lobby/roster changed; also on settings change).</summary>
        public void RefreshPlayerVolumes()
        {
            for (int i = 0; i < Slots; i++)
            {
                string key = NameOf(i);
                _playerVolume[i] = string.IsNullOrEmpty(key) ? 1f : Settings.GetPlayerVolume(key);
            }
        }

        void OnEnable()
        {
            if (ClientState.I != null) ClientState.I.LobbyChanged += OnLobbyChanged;
        }

        void Start()
        {
            // ClientState may wake after us; subscribe once it exists.
            if (ClientState.I != null) { ClientState.I.LobbyChanged -= OnLobbyChanged; ClientState.I.LobbyChanged += OnLobbyChanged; }
        }

        void OnDisable()
        {
            if (ClientState.I != null) ClientState.I.LobbyChanged -= OnLobbyChanged;
        }

        void OnLobbyChanged(BirimizKovulacak.Core.LobbyView lobby) { RefreshPlayerVolumes(); }
    }
}
