# Voice

> v4 note: the voice core is unchanged; the host now reports recording failure reasons and playback timing to analytics (`HostLineVoiceBridge`), and the `Break` phase uses calls-only routing like other office phases. Recording selection rules: `docs/GAMEPLAY_REDESIGN.md` §13.

In-game voice for "Birimiz Kovulacak": custom Opus voice over NGO named messages, host-authoritative routing.
The v2 design, wire format, codec choice (native libopus + Concentus fallback), measurements, launch args and the v2
integration steps are in [`VOICE_NOTES.md`](VOICE_NOTES.md). This file covers **v3 "Ofis Hattı"** (branch `v3-voice`).

Verified without Unity: `dotnet test Tools/VoiceTests/Tests` → **109/109 passed** (v2: 70), and
`python -I Tools/scripts/compile_check.py` (editor and `--player`) → `COMPILE OK`.

## v3 at a glance

| v3 rule (docs/V3_DESIGN.md) | Voice behaviour |
|---|---|
| Everyone alone in their office; the phone is the only channel during the day | `VoiceMode.Calls`: a speaker reaches only their **connected** call partner (both sides must point at each other; ringing is silent). No proximity, no positions needed. |
| Meetings: everyone hears everyone | `VoiceMode.AllHear` (unchanged). |
| Final defense: only the defender's mic is open | `AllHear` + `IVoiceModeProvider.CanSpeak(slot) = slot == defender`. |
| §5 Hattı Dinle (wiretap, 60 s) | The host also delivers both sides of the target's connected call to every tapper, flagged `Call \| Tap`. Clients play it through the phone EQ at 70 % gain. One-way: the tapper's voice never enters the tapped call. Never while the call is not connected. |
| §5 Kaydı Sil (erase tape) | **Retired since v3.1** (never dealt; `EraseTape` has no callers). Kept in the voice core for history. |
| §6 Recordings | The host records the routed call frames (both directions, Opus as received) per call id, **in memory only**, cleared when the next block's `Morning` (or `WeekResults`) begins, at match start/lobby return, when recordings are switched off, and at session end (v4: `HostLineController`). At the dossier it picks the most speech-dense ~7 s of the first good candidate and streams it to everyone at real-time pace, with the original speaker slots. Clients play it in 2D through a tape filter (300–3400 Hz band-pass, soft saturation, gentle wow/flutter, light hiss). |
| Talk animation in the meeting room | `GetSpeechLevel(slot)` 0..1 for every player, local included. |
| The phone is the main channel | Call audio gets a subtle telephone EQ (`VoiceSettings.PhoneEq`, default on): 200 Hz high-pass, +2.5 dB presence at 1.8 kHz, gentle 5.5 kHz low-pass. Measured: 90 Hz −14 dB, 1 kHz +1.2 dB, 7.5 kHz −6.9 dB. It stays wideband speech, not a 3.4 kHz line. |

## Integration: what the lead wires

Everything below is reached through `VoiceSystem` (namespace `BirimizKovulacak.Game`). `VoiceSystem.Instance` is created
by `GameBootstrap` and is `null` with `-novoice`.

### 1. Session binding (static hooks, set once at startup)

`VoiceSystem` no longer has to know the session objects. Each hook is optional; `null` means the built-in default.

```csharp
VoiceSystem.ExternalHostProviders = true;                              // kept for compatibility (there is no automatic bridge any more)
VoiceSystem.HostSessionActive = () => HostLineController.Current != null; // host: route voice only while the session is up
// Optional. These defaults already read LineClient:
VoiceSystem.LocalSlotSource = () => LineClient.I != null ? LineClient.I.MySlot : -1;
VoiceSystem.ServerTimeSource = () => NetworkManager.Singleton.ServerTime.Time;  // recording start times / progress (seconds)
VoiceSystem.Instance.SlotNameResolver = slot => /* lobby name, used as the per-player volume key */;
```

| Hook | Default | Notes |
|---|---|---|
| `static Func<int> LocalSlotSource` | `LineClient.I.MySlot` (−1 without a session) | −1 = not in a session (mic closed). |
| `static Func<bool> HostSessionActive` | `true` while `SetHostProviders` providers are set | Host only. False = route nothing and stop a running tape. |
| `static bool ExternalHostProviders` | not read any more | Routing always uses only `SetHostProviders` (the v2 automatic bridge was removed with the v2 code). |
| `static Func<double> ServerTimeSource` | `NetworkManager.ServerTime.Time` (realtime when offline) | Set it if `LineClient.Clock` uses another clock. |
| `Func<int,string> SlotNameResolver` (instance) | `LineClient.I.Lobby` | Polled every 0.5 s; a slot whose name changed re-reads its saved volume. No lobby events needed. |

The only places that still read `LineClient` are the private `DefaultLocalSlot` and `DefaultSlotName` methods, and
the hooks above replace them. (The v2 defaults — `ClientState`, `HostMatchController`, `HostMatchVoiceBridge` — were
removed from the `v3` branch; v2 lives on `main`.)

### 2. Host providers (when the host session starts)

```csharp
VoiceSystem.Instance.SetHostProviders(positions: bridge, calls: bridge, mode: bridge, occlusion: null, clients: bridge);
// ... and SetHostProviders(null, null, null, null, null) when the session object goes away.
```
The providers survive NGO re-registration and reconnects until they are cleared. All of them are called per frame,
so they must be allocation-free.

| Interface | Method | v3 contract |
|---|---|---|
| `IVoicePositions` | `bool IsActive(int slot)` | Occupied, connected, **human** slot. Bots neither speak nor listen. |
| | `bool TryGetPosition(int slot, out VoiceVec3 p)` | Return `false`: positions are only used by v2 `Office`. |
| `IVoiceCallState` | `int GetCallPartner(int slot)` | The partner **only while the call is connected** (−1 while ringing / idle / dead line). |
| | `int GetCallId(int slot)` *(new)* | The connected call's id, **the same on both sides**, unique until `ClearRecordings` (e.g. the engine's `CallRecord` id), or −1. Must be the id the engine later passes as a recording candidate. If the two sides disagree, the frame is routed but not recorded. |
| | `int GetTappers(int target, int[] buffer)` *(new)* | Writes the slots whose Hattı Dinle on `target` is active right now; returns the count (buffer length 6). The router asks for both sides of the call and removes duplicates, the partner and inactive slots itself. |
| `IVoiceModeProvider` | `VoiceMode CurrentMode` | See the phase table below. |
| | `bool CanSpeak(int slot)` | `FinalDefense`: `slot == defender`; otherwise `true`. |
| `IVoiceOcclusion` | | Pass `null` in v3. |
| `IVoiceClientMap` | `SlotOfClient` / `TryGetClient` | NGO client id ↔ slot (as in v2). |

Phase → mode:

| `LinePhase` | `CurrentMode` | `CanSpeak` |
|---|---|---|
| lobby / no week (`None`) | `AllHear` | all |
| `Morning`, `RoundIntro`, `RoundPlay`, `RoundReveal` | `Calls` | all |
| `MeetingDossier`, `MeetingDiscussion`, `MeetingVote`, `MeetingVerdict` | `AllHear` | all |
| `FinalDefense` | `AllHear` | defender only |
| `FinalVote`, `FinalVerdict`, `WeekResults` | `AllHear` | all |
| loading / transitions where nobody should talk | `Muted` | — |

The engine already ends every call at meeting start (§4). The voice side needs nothing more: with no connected partner
there is no call audio, and taps stop on their own when `GetTappers` stops listing the tapper.

### 3. Recordings (host)

```csharp
var vs = VoiceSystem.Instance;
vs.RecordingEnabled = hostLine.RecordingsEnabled;   // lobby toggle (default on). Off also clears.
vs.EraseTape(slot);                                  // when slot uses Kaydı Sil (the engine still filters its candidates too)

// MeetingDossier: candidates in the engine's order (v4: today's calls ≥ 6 s, weighted by duration × context factors; see GAMEPLAY_REDESIGN §13;
// duration, up to 3 fallbacks). serverTime ≈ now + 0.3..0.5 s so the start message lands before the audio.
double at = NetworkManager.Singleton.ServerTime.Time + 0.5;
if (vs.TryPlayBestRecording(candidateCallIds, at, out RecordingSnippet snip))
{
    // snip.CallId, snip.StartMs (offset into the call: UI timestamp = CallRecord.Start + StartMs),
    // snip.LengthMs (≤ 7000; extend the dossier phase by it), snip.SpeakersMask, snip.ParticipantsMask
}
// Paranoyak (2 recordings): start the second one from OnHostRecordingFinished, or schedule it at
// at + snip.LengthMs / 1000.0 + 1.0. A new PlayRecording replaces a tape that is still running.

vs.ClearRecordings();                                // at the next Morning / WeekResults, match start and lobby return
```

| Host API | |
|---|---|
| `bool RecordingEnabled { get; set; }` | Lobby toggle. Off = ignore and clear. |
| `void EraseTape(int slot)` | Kaydı Sil, until `ClearRecordings`. |
| `void ClearRecordings()` | Forget all call audio and stop a running tape. Memory stays pooled. `EndSession` also releases it. |
| `bool TrySelectRecording(IList<int> candidateCallIds, out RecordingSnippet s, float seconds = 7, float minSpeechSeconds = 1.5f)` | First candidate with ≥ `minSpeechSeconds` of speech in its best window, and inside it the `seconds` window with the most speech (time covered by at least one voice). The start moves back up to 0.5 s so it begins at a word. A call shorter than the window is used whole. Unknown, erased and too-quiet candidates are skipped. |
| `bool PlayRecording(in RecordingSnippet s, double serverTimeSec)` | Streams the snippet to every client and the host at real-time pace from `serverTimeSec` (≤ 0 = now). |
| `bool TryPlayBestRecording(IList<int> candidates, double serverTimeSec, out RecordingSnippet s, float seconds = 7)` | Select + play. |
| `void StopRecording()` | Stop now (clients play out what they have, then end). |
| `bool HostRecordingPlaying` / `event Action<RecordingSnippet> OnHostRecordingFinished` | Host-side status. |
| `VoiceCallRecorder Recorder` | `TryGetCall(id, out RecordedCallInfo)`, `CallCount`, `UsedBytes`, `ReservedBytes`, `DroppedFrames`... |
| `long RecordingReserveBytes` | Pre-allocated at host session start (default 1 MB). |

Late joiners: a client that connects during playback gets the start message on the next frame and hears the rest.

### 4. What the UI polls (every client, the host included)

| API | Use |
|---|---|
| `float GetSpeechLevel(int slot)` | 0..1, smoothed (30 ms attack, 120 ms release), the local player included, independent of volume settings. Talk animation of the meeting avatars. Allocation-free. |
| `bool IsSpeaking(int slot)`, `event OnSpeakingChanged(int, bool)` | As in v2. |
| `VoiceHearing GetHearing(int slot)` | Adds `Tap` (you are wiretapping this speaker). |
| `bool RecordingActive` | The tape runs, from the start message until its last frame has faded. |
| `float RecordingLevel` | 0..1 voice level of the tape (hiss excluded): waveform / VU needle. |
| `int RecordingSpeaker` | Original slot audible on the tape right now, or −1: nameplate. |
| `float RecordingProgress` | 0..1 through the window (server time). |
| `VoiceTapeInfo CurrentRecording` | `PlaybackId, CallId, StartServerTime, CallOffsetMs, DurationMs, SpeakersMask, ParticipantsMask`. `CallId` −1 means not known yet. |
| `event Action<VoiceTapeInfo> OnRecordingStarted / OnRecordingEnded` | Main thread. Started fires once the info is known. Ended fires after the fade, or when the session ends. |
| `float LiveDuckDuringRecording = 0.6f`, `float RecordingVolume = 1f` | Live voices duck a little while the tape plays. |
| `VoiceSettings.PhoneEq` (bool, PlayerPrefs `voice.phone_eq`, default on) | Settings screen toggle. Suggested loc key `voice.phone_eq` ("Telefon sesi" / "Phone sound"). |

## Wire additions (backward-compatible frame format)

- Frame flags: `Tap = 32` (always with `Call`), `Recording = 64` (slot = **original** speaker; the client sends it to
  the tape player and never to that slot's live channel, so a player can talk live over their own recording). Clients
  may still set only `EndOfSpeech`; the host strips everything else.
- `bk.voice.tape` (host → client, Reliable, 26 B): `0xB6 kind playbackId:u16 callId:i32 startServerTime:f64
  callOffsetMs:i32 durationMs:i32 speakers:u8 participants:u8`, where kind is 1 = start (re-sent to late joiners) and
  2 = end. If a tape frame arrives before the start message, the tape starts anyway and the info is filled in when the
  message arrives. Stray frames within 1 s after an end are ignored. A tape with no frames for 8 s ends by itself.

## Memory and performance

Recorder (host only), measured in `Tools/VoiceTests` (`Memory_BytesPerTalkingMinute_IsMeasured`):

| | Native libopus | Concentus |
|---|---|---|
| Opus payload per talking minute (18 kbps target, continuous synthetic speech) | 112.6 KB | 105.6 KB |
| Metadata (16 B/frame: media time, payload position, next link, seq, slot+end flag, length) | 46.9 KB | 46.9 KB |
| **Total per talking minute per speaker** | **159.5 KB** | 152.5 KB |

- Phone time per day: Morning 40 s (first day; 20 s otherwise) + 2 × (RoundIntro 16 + RoundPlay 150 + RoundReveal 12)
  = 6.6 min per player at most.
- **Worst-case day, 6 players** (everyone talking non-stop through every phone phase, 3 calls at once): 6 × 6.6 min ×
  159.5 KB = **6.2 MB**. **Typical day** (70 % of the time on the phone, 40 % of that talking): **≈ 1.7 MB**.
- **5-day week, 6 players: peak = one day (≤ 6.2 MB worst, ≈ 1.7 MB typical)**, not 5×, because `ClearRecordings`
  runs after every meeting. The chunks are pooled, so days 2–5 reuse day 1's memory without allocating. Everything is
  released at session end (`Trim`).
- Cap: `VoiceCallRecorder.MaxBytes` = 8 MB by default, so the worst case fits. Over the cap, new frames are dropped
  and counted; earlier audio is never overwritten. `-durationscale` > 1.3 with nonstop talk could hit the cap, which
  only costs that day's latest calls.
- Fixed cost: chunk tables 16 KB + call table (1024 calls) 32 KB + selection scratch (1 B per 20 ms of the longest
  call, ≈ 7.5 KB for a 150 s call). Storage is 64 KB metadata chunks (4096 frames) and 64 KB payload chunks.
- The recorder stores the Opus frames exactly as routed, so no decoding or re-encoding ever happens on the host.

Clients: the tape player adds 2 decoders (libopus ≈ 18 KB each), 2 jitter buffers (6.4 KB each), a 64 KB ring, the tape
filter (8 KB delay line) and one more pooled emitter, ≈ 125 KB in total. A tape adds at most 2 × ~19 kbps per client
for ≤ 7 s.

**Allocations: 0 B per frame**, test-verified:
- `HotPath_V3_CallsTapRecorderAndTape_AllocateNothingPerFrame` runs 2000 frames of capture → encode → Calls routing
  with a wiretap → recording → decode for the partner and the tapper → tape streamer → tape player (filter, mix,
  levels) → audio-thread readers at 48 kHz and 44.1 kHz.
- `Clear_KeepsChunksPooled_...`: a whole second day of 1800 recorded frames plus snippet selection allocates 0 B.
- `Route_IsAllocationFree_WithTaps`, plus the v2 `HotPath_NativeBackend_AllocatesNothingPerFrame`, which still passes.

The new `VoiceSystem` glue adds no per-frame allocations: delegates are only invoked, tape events fire only on change,
and the 26 B tape messages are sent only at start and end.

## New code

| File | What |
|---|---|
| `Scripts/Voice/Core/VoiceRouting.cs` | `IVoiceCallState.GetCallId` / `GetTappers`, `VoiceMode.Calls`, wiretaps, `LastCallId` / `LastCallPartner`, `TapRoutes`, `VoiceRoutingSettings.TapGain` |
| `Scripts/Voice/Core/VoiceCallRecorder.cs` | `VoiceCallRecorder`, `RecordingSnippet`, `RecordedCallInfo` |
| `Scripts/Voice/Core/VoiceRecordingStreamer.cs` | `VoiceRecordingStreamer` (host, per-speaker cursors), `IVoiceRecordingSink`, `VoiceTapeInfo`, `VoiceTapeMessage` |
| `Scripts/Voice/Core/VoiceTapePlayer.cs` | Client tape: 2 streams → mix → `TapeFilter` → ring; level, current speaker |
| `Scripts/Voice/Core/VoiceDsp.cs` | `Biquad.HighPass` / `Peaking`, `PhoneEq`, `TapeFilter`, `VoiceLevel.Speech01` / `Smooth` |
| `Scripts/Voice/Core/VoicePlaybackChannel.cs` | `IVoicePcmSource`, phone EQ + tap gain (crossfaded per frame), `FrameLevel01` |
| `Scripts/Voice/VoiceSystem.cs` | binding hooks, recording/tape API, tape transport, speech levels |
| `Scripts/Voice/VoiceEmitter.cs` | plays any `IVoicePcmSource` (6 slots + the tape) |
| `Scripts/Voice/VoiceSettings.cs` | `PhoneEq` |
| `Tools/VoiceTests/Tests/RouterV3Tests.cs`, `RecorderTests.cs`, `TapeTests.cs` | 39 new tests |
