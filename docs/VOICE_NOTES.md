# Voice — notes (v2 / U5)

Owner: Claude sub-agent `voice` (branch `v2/voice`, merged with main `67ef3dc`). Plan: `docs/V2_PLAN.md` §0, §5 U5, §8.
In-game voice replaces Discord: proximity voice in the office, private 1:1 phone calls, everyone-hears in the
lobby / meeting / results. Verified without Unity: `python -I Tools/scripts/compile_check.py` → `COMPILE OK` (editor and
`--player`), `dotnet test Tools/VoiceTests/Tests` → **70/70 passed**.

## Data flow

```
 every client                                       host (also a client: its own mic/speakers skip the network)
 Microphone / -voicetest source                      bk.voice.up ─▶ slot = sender's slot (claimed slot ignored)
   → VoiceCapturePipeline (resample → 16 kHz,         → rate limit (60 fps, burst 25)
     20 ms frames, meter, VAD / PTT gate, Opus)       → ProximityVoiceRouter.Route(speaker) per frame
   → bundle → bk.voice.up (NGO named msg,                 mode Muted / AllHear / Office (+ call, walls)
     Unreliable, own seq numbers) ─────────────────▶   → per-recipient bundle → bk.voice.down
 bk.voice.down ◀────────────────────────────────────  (one message per recipient per frame)
   → VoicePlaybackChannel per speaker slot (jitter buffer, FEC/PLC, distance gain, wall muffle, 48 kHz ring)
   → VoiceEmitter (pooled AudioSource looping a constant-1 clip; OnAudioFilterRead multiplies the voice in)
```

Wire format (both directions): `bundle := 0xB5 frame*`, `frame := slot:u8 seq:u16le flags:u8 len:u8 opus[len]`
(5 B header per 20 ms). Flags: `EndOfSpeech` (only flag a client may set), `Call`, `Muffled`, `AllHear`, `Loopback`.
Bundles are capped at 1100 B (NGO's unfragmented limit is ~1264 B for named messages).

## Files

| Path | What |
|---|---|
| `Game/Assets/Plugins/Concentus/Concentus.dll` (+ `LICENSE.txt`) | Concentus 2.2.2, netstandard2.0 build from NuGet — managed Opus (fallback) |
| `Game/Assets/Plugins/Concentus/x86_64/opus.dll` (+ `LICENSE-libopus.txt`) | libopus 1.5.2 win-x64 from NuGet `Concentus.Native.NetCore` 1.5.2 — primary codec |
| `Scripts/Voice/Core/*` (pure C#, no UnityEngine) | `VoiceFormat` (constants, flags, modes), `VoiceWire` (bundle writer/reader), `VoiceCodec` + `NativeOpus` + `ManagedOpus` (backends), `VoiceJitterBuffer`, `VoiceRouting` (provider interfaces + `ProximityVoiceRouter` + `VoiceRoutingSettings`), `VoiceActivityDetector`, `VoiceCapturePipeline`, `VoicePlaybackChannel`, `PcmRing` (+ audio-thread `PcmPlayoutReader`), `VoiceDsp` (biquad, resampler, levels, `VoiceAttenuation`), `SyntheticVoiceSource` (+ `WavReader`), `VoiceStats` (stats line, transport counters, rate limiter, `VoiceLaunchOptions`) |
| `Scripts/Voice/VoiceSystem.cs` | the MonoBehaviour: capture, NGO transport, host routing, playback, UI API, stats |
| `Scripts/Voice/VoiceEmitter.cs` | pooled per-slot audio output (6) |
| `Scripts/Voice/VoiceMicrophone.cs` | Unity `Microphone` ring reader (`AudioClip.GetData(Span)`, no allocations) |
| `Scripts/Voice/HostMatchVoiceBridge.cs` | default host providers from `HostMatchController` (slots, avatars, phase → mode, walls, calls) |
| `Scripts/Voice/VoiceSettings.cs` | PlayerPrefs `voice.*` |
| `Scripts/Voice/VoiceInput.cs` | thin wrapper over `GameInput.PushToTalk` (ignored while `FeelHooks.TextInputFocused`) |
| `Tools/VoiceTests/` | `Core/VoiceCore.csproj` (netstandard2.1, C# 9 — compiles Voice/Core like Unity) + `Tests/VoiceTests.csproj` (net8.0 NUnit, copies opus.dll) |

## Deviations from the brief (and why)

1. **Codec: native libopus first, Concentus as automatic fallback.** Measured: Concentus allocates **~60 KB of managed
   garbage per encoded 20 ms frame and ~12 KB per decoded frame** (≈ 2.9 MB/s while talking + 0.6 MB/s per remote
   speaker; same in 1.1.7 and 2.2.2, every complexity) because the C port turns stack arrays into heap arrays. That
   breaks the §8 "zero managed allocations in capture/encode/decode" budget. libopus via P/Invoke allocates **0 B**
   (test `HotPath_NativeBackend_AllocatesNothingPerFrame`, 2000 frames through capture → route → decode → playout) and is
   ~6× faster. Both produce standard Opus; a native client and a managed client interoperate (tested both ways).
   `opus.dll` imports `VCRUNTIME140.dll` + UCRT; if it cannot load (missing VC++ 2015–2022 runtime, other OS),
   `VoiceCodec.Resolve` falls back to Concentus and the log says so. Force with `-voicecodec managed|native`.
2. **Transport `NetworkDelivery.Unreliable` + own sequence numbers** (allowed by the brief): UnreliableSequenced would
   drop late-but-useful packets that the jitter buffer can still reorder.
3. **Distance attenuation is computed by the voice module, not by the AudioSource rolloff.** Gain = f(local avatar →
   speaker avatar distance), the same metric the host router uses (full ≤ 3 m, 1/d law reaching 0 at 14 m). The
   AudioSource has a flat custom rolloff and only pans (relative to the AudioListener, which feel now keeps on the local
   avatar's head). Works identically when spectating (listener falls back to the `AudioListener` object).
4. **Glass muffles voice.** Occlusion = `Physics.Linecast` at 1.5 m mouth height against `Walls | Glass`
   (`UnityWorldQueries` LOS uses Walls + Partitions; feel's `Sfx` muffles by Walls only). Partitions (~1.3 m) are below
   mouth height, so they never muffle. One-line change in `HostMatchVoiceBridge` (`_occluderMask`) if design disagrees.
5. **Default Opus target 18 kbps** (brief said ~20): payload + our 5 B header + NGO's ~11 B named-message header stays
   under 24 kbps on the wire. Constrained VBR, wideband, complexity 8 (Concentus capped at 5), in-band FEC tuned for 10 %
   loss, DTX off (we gate ourselves).
6. **Launch args are parsed by `VoiceSystem` itself** (`VoiceLaunchOptions`), so no `LaunchArgs` change is needed. Main's
   `LaunchArgs.VoiceTest` / `run_local4.ps1 -VoiceTest` pass the same token, so both agree.
7. **Push-to-talk:** `GameInput.PushToTalk` (V, remappable) as the lead asked; no own binding.

## Measured (Tools/VoiceTests, .NET 8, this machine)

| What | Native libopus | Concentus |
|---|---|---|
| Opus payload, `speech` synthetic / harmonic tone | 14.5 / 15.8 kbps | 13.5 / 14.7 kbps |
| Stream (payload + 5 B frame header) | 16.5 / 17.8 kbps | 15.5 / 16.7 kbps |
| + NGO named-message header (~11 B × ~50 msg/s ≈ 4.4 kbps) | ≈ 21–22 kbps on the wire | ≈ 20–21 kbps |
| Largest frame seen | 94 B | 59 B |
| Encode / decode@48 kHz CPU per 20 ms frame | 0.095 ms / 0.013 ms | 0.61 ms / 0.20 ms |
| Host worst case (1 encode + 5 decodes per 20 ms) | 0.16 ms | 1.6 ms |
| Managed allocations per frame (whole hot path) | **0 B** | ~60 KB encode, ~12 KB decode |
| Round-trip SNR of a 220 Hz harmonic tone | 11.4 dB | 9.8 dB |

End-to-end simulation (5 s, uplink 5–15 ms 2 % loss, downlink 20–65 ms 5 % loss + 1 % duplicates, 60 fps updates):
proximity listener played 230 + concealed 13 (13 recovered by FEC) of 250 frames, **0 late**, 0 decode errors; call
partner played 225 + 18 concealed (16 FEC); the far player outside the call received nothing.
Host upload worst case (meeting, 6 talking at once, 5 clients): ≈ 5 × (5 × 18 + 4.4) ≈ 470 kbps (budget 6 × 5 × 24 = 720).

Memory (bounded, allocated once): 6 playback channels × ~93 KB (48 kHz ring 64 KB, jitter buffer 6.4 KB, libopus decoder
18 KB) + capture ~140 KB (libopus encoder 44 KB, buffers) + mic clip 192 KB + shared constant-1 clip 192 KB ≈ **1.1 MB**.

Latency (estimate, 60 fps, LAN): 20 ms frame + ~8 ms capture polling + host relay ~8 ms + playout ≈ 100 ms (60 ms base
jitter delay + the output ring prefetch of one DSP buffer + 1.5 frames) ≈ 140–160 ms mouth-to-ear. Tunable:
`VoiceSystem.Instance.BasePlayoutDelayMs` (20..200). The delay adapts per spurt to the peak transit-delay spread (up to
240 ms), grows mid-spurt by one concealed frame when packets start arriving late, and shrinks by skipping when a burst
queues too much.

## Integration steps for the lead

### 1. REQUIRED — asmdef reference (Unity will not compile without it)
`compile_check.py` passes because it references every DLL under `Plugins/` automatically, but the Game asmdef has
`overrideReferences: true`. `Game/Assets/Game/Scripts/BirimizKovulacak.Game.asmdef`:
```json
    "precompiledReferences": [
        "Newtonsoft.Json.dll",
        "Concentus.dll"
    ],
```

### 2. REQUIRED — create the VoiceSystem
Self-installing, like feel (no scene rebuild). `Scripts/Boot/GameBootstrap.cs`, `Awake()`, after `GameInput.Initialize();`:
```csharp
            if (GetComponent<VoiceSystem>() == null) gameObject.AddComponent<VoiceSystem>();
```
(Alternative: `boot.AddComponent<VoiceSystem>();` after `boot.AddComponent<Sfx>();` in `Editor/ProjectBuilder.Scene.cs`.
Do one, not both.) It needs `MatchLog.Initialize` to have run first (it logs its init line) — true at that spot.

### 3. Plugin import settings (when your Unity import creates the `.meta` files)
- `Plugins/Concentus/Concentus.dll`: Any Platform, Auto Reference on. Its references to `System.Memory` 4.0.1.1 and
  `System.Numerics.Vectors` 4.1.3 resolve to Unity 6's .NET Standard 2.1 facades (checked: 4.0.99 / 4.1.5 shipped in
  6000.6.3f1). If the inspector still reports "Unable to resolve reference", untick **Validate References** — do NOT add
  NuGet's System.Memory.dll (it collides with Unity's).
- `Plugins/Concentus/x86_64/opus.dll`: native; Editor (Windows, x86_64) + Standalone Windows x86_64 only. In the built
  player it must end up in `BirimizKovulacak_Data/Plugins/x86_64/opus.dll`.
- Check the first log lines of a run: `voice init codec=Native (libopus unknown) source=microphone output=48000Hz`.
  `codec=Managed (Concentus; native: DllNotFoundException ...)` means opus.dll was not deployed or VCRUNTIME140 is missing
  (voice still works, but allocates — see deviation 1).
- `compile_check.py` prints a harmless `MSB3246` warning for opus.dll (only with `--warnings`). Optional one-line fix in
  `generate()`: `if f.lower().endswith(".dll") and "x86_64" not in dirpath and ...`.

### 4. Calls — nothing to do
`HostMatchVoiceBridge` uses `MatchEngine.CallPartnerOf(slot)` (partner only while the call is `Active`, so ringing
never opens the line). The router also requires both sides to point at each other. To override (e.g. a different call
model), set `VoiceSystem.Instance.CallPartnerResolver = slot => ...` (return −1 when not connected).

### 5. Mode per match phase — automatic
`HostMatchVoiceBridge.ModeForPhase`: no engine (lobby) / Meeting* / Results / Abandoned → `AllHear`;
Briefing, Work1, Raid, Work2, FinalInspection → `Office`. If v2 adds phases, extend that switch.
Overrides: `VoiceSystem.Instance.ModeOverride = VoiceMode.Muted` (e.g. loading) and, optionally, only the defending
player talks during a defense:
```csharp
// needs a public accessor on MatchEngine, e.g. public int MeetingSpeaker => meeting.Speaker;
var hc = ConnectionManager.I.HostController;
VoiceSystem.Instance.SpeakerFilter = slot =>
{
    var e = hc != null ? hc.Engine : null;
    if (e == null || (e.Phase != MatchPhase.MeetingDefense1 && e.Phase != MatchPhase.MeetingDefense2)) return true;
    return slot == e.MeetingSpeaker;
};
```
(Design call; default = everyone may talk in every AllHear phase.)

### 6. Slots, players, bots
- Slots 0..5 (`VoiceFormat.MaxPlayers = 6 = Protocol.MaxPlayers`). The host maps NGO client ids ↔ slots from
  `HostMatchController.Slots` (`Occupied && Connected && !Virtual`, `ClientId`); host-side virtual bots neither speak nor
  listen. Emitters follow `PlayerAvatar.BySlot(slot)` automatically (re-resolved every 0.5 s);
  `VoiceSystem.Instance.AttachEmitter(slot, transform)` overrides (e.g. a head bone), `DetachEmitter(slot)` reverts.
- Speaking changes call `FeelHooks.SetTalking(slot, bool)` (lip-flap) for every slot, including the local player.

### 7. Automated multi-window runs (synthetic voice instead of microphones)
`powershell -File Tools\run_local4.ps1 -Bots -VoiceTest speech` (or `-ExtraAll "-voicetest sine"`). Every process then
"talks" 2.5 s every 6 s (phase-shifted per player) through open-mic VAD, so routing, calls, muffle, jitter buffer and
playback all run. Args (all parsed by `VoiceSystem`):
- `-voicetest sine|speech|<path.wav>` — synthetic source (WAV: PCM 8/16/24/32-bit or float, any rate/channels, looped).
- `-voicestats <seconds>` — stats period (default 10 s with `-voicetest`, 30 s otherwise; 0 = off).
- `-voicecodec auto|native|managed`, `-voiceloopback` (hear yourself), `-novoice` (disable entirely).
Each process writes to its match log, e.g.
```
voice stats window=10.0s codec=Native | tx frames=125 spurts=2 errors=0 opus_kbps_while_talking=14.6 stream_kbps_while_talking=16.6
  | net up_msgs=125 up_kbps=1.9 down_msgs=231 down_kbps=4.1 | route decisions=... delivered=... call=... muffled=... muted=...
  out_of_range=... rate_limited=0 unknown_sender=0 | rx s1 pkts=118 kbps=... played=117 lost=3 fec=2 late=0 dup=0 dropped=0
  decode_err=0 underruns=0 jitter_ms=6.2 target_ms=60
```
(`route ...` only on the host.) Suggested `verify_logs.py` checks: every process logs `voice init codec=Native`; in the
work phases each client has `rx s<N>` lines with `played>0`, `decode_err=0`, and `late` < 5 % of `pkts`.
Small note on main's `LaunchArgs`: `-voicetest` always consumes the next token, so `-voicetest -autohost` would eat
`-autohost`; `VoiceLaunchOptions` treats a following `-flag` as "no value" (defaults to `sine`).

### 8. Quick check in the editor without a second client
Start a host and press V with `VoiceSystem.Instance.LoopbackTest = true` (or launch with `-voiceloopback`): you hear your
own encoded voice in 2D through the full encode → jitter buffer → decode → emitter path.

## API for the UI agent (settings screen, HUD)

`VoiceSystem.Instance` (null with `-novoice`):
- Settings: `Settings` (`VoiceSettings`: `InputDevice` ("" = default), `InputMode` (`PushToTalk`/`OpenMic`),
  `VadSensitivity` 0..1, `MasterVolume` 0..2, `InputGain` 0..4, `SelfMute`, `Deafen`; call `Save()` when the screen
  closes; changes apply immediately). `VoiceSystem.InputDevices` (string[], allocates — call when the screen opens).
- Mic test: `MicTest = true` while the settings screen is open (runs the mic outside a session), `LocalMicLevel` 0..1
  meter, `LoopbackTest` (hear yourself), `MicrophoneAvailable`, `MicrophoneError` ("no_device", ...).
- Keys: push-to-talk is `GameInput.PushToTalk` (already a `GameInput.Rebindable` row, `input.push_to_talk`);
  `PushToTalkKey` label.
- Speaking: `IsSpeaking(slot)`, `event OnSpeakingChanged(int slot, bool speaking)` (main thread, change-only, includes the
  local player), `IsTransmitting`, `GetHearing(slot)` → `None / Proximity / Muffled / Call / AllHear / Loopback` (phone icon
  for calls, wall icon for muffled).
- Per-player volume: `GetPlayerVolume(slot)` / `SetPlayerVolume(slot, 0..2)` (persisted by player name).
- `Backend` (Native/Managed) for a diagnostics line.
Suggested loc keys: `voice.mode.ptt`, `voice.mode.open`, `voice.device`, `voice.sensitivity`, `voice.volume`,
`voice.input_gain`, `voice.self_mute`, `voice.deafen`, `voice.mic_test`, `voice.no_mic`, `voice.player_volume`.
Privacy is enforced by the host: a client only receives (and can only show speaking indicators for) voices it can
actually hear.

## Licenses (commercial use OK)

Concentus and libopus: BSD-3-Clause-style Opus license (© Xiph.Org, Skype, CSIRO, Microsoft, Jean-Marc Valin, Logan
Stromberg et al.). Binary redistribution must reproduce the copyright notice + conditions in the documentation/credits:
ship `Plugins/Concentus/LICENSE.txt` text in the game's credits or a `THIRD_PARTY_NOTICES.txt` next to the exe.
Opus patents are licensed royalty-free (opus-codec.org/license). Sources: NuGet `Concentus` 2.2.2 and
`Concentus.Native.NetCore` 1.5.2 (libopus v1.5.2 build), SHA-256 of the committed binaries:
`Concentus.dll` 11a6de9c…7583a01, `opus.dll` 9827076…f2209a9.

## Known risks / not verifiable without Unity

1. Unity's reference validation of `Concentus.dll` against the System.Memory / Numerics.Vectors facades (expected fine,
   see step 3).
2. `opus.dll` plugin import settings + deployment into the player, and the VCRUNTIME140 dependency on clean PCs
   (fallback keeps voice working).
3. The constant-1 clip × `OnAudioFilterRead` approach must give correct panning in 3D and full level in 2D (it is
   correct whether Unity spatialises before or after the filter; listen with two clients standing left/right).
4. Unity `Microphone`: 48 kHz request via `GetDeviceCaps`, stereo devices (downmixed), Windows privacy settings that
   deliver silence instead of an error, `Microphone.Start` blocking ~100 ms at session start, device unplug → retry
   every 2 s.
5. Real latency and the adaptive delay with Update quantisation at 30 fps (bots) — read `jitter_ms`/`target_ms`/`late`
   in the stats lines; tune `BasePlayoutDelayMs`.
6. NGO allocates a small array per `SendNamedMessage` (it UTF-8-encodes the message name to hash it; ≤ 6 sends per
   frame on the host, ~40 B each). Our code is allocation-free; unnamed messages would avoid this but allocate a
   delegate array per received message instead, so named messages were kept.
7. PTT is ignored while `FeelHooks.TextInputFocused` — only works once the UI implements `IFeelUi`.
8. Overlapping speech of 5+ players at full AllHear volume can clip (each channel is clamped to ±1; Unity sums the
   sources). Lower `MasterVolume` default or add a limiter if it is audible.
9. A client that never gets a slot (`ClientState.MySlot` = −1) does not transmit; frames for it are still played.
