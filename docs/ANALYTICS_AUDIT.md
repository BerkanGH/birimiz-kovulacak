# Analytics audit (v3.1 baseline → v4)

Audit date 2026-11-01, branch `main` @ `341a54a` (v3.1 "Ofis Hattı"), before any v4 change. Sources: the lead's reading of
the rules engine and net layer, and a read-only Sonnet 5.5 sub-audit of voice, connection, UI, audio and tooling
(file/line references below were taken from that pass and re-checked where they drive integration).

## 1. Architecture

| Layer | Where | Notes |
|---|---|---|
| Rules engine | `Game/Assets/Game/Core/Line/**` (pure C#, netstandard2.1, no UnityEngine) | deterministic `LineEngine` (seeded SplitMix64), `LineCodec` binary codec, `LineBot`; contracts in `Contracts/*.cs` |
| Host authority | `Scripts/Net/HostLineController.cs` | owns lobby seats, approval/reconnect tokens, the engine, per-recipient `LineView` (≤ 10 Hz + 2 s heartbeat), event routing by audience, bots, recordings hand-off, session history |
| Transport | `Scripts/Net/NetSession.cs` (NGO 2.13.2 RPCs, Unity Transport 6.6, direct IP), `ConnectionManager.cs` | every payload is a `LineCodec` byte array; clients only send requests |
| Client state | `Scripts/Net/LineClient.cs` | latest filtered view, lobby, events, action results; the host's own UI reads the same filtered data |
| Voice | `Scripts/Voice/**` (Opus via Concentus, VAD, routing, recorder, tape) + `Net/HostLineVoiceBridge.cs` | calls-only in office phases, all-hear in meetings, defender-only during defense; call recorder in host memory |
| UI | `Scripts/UI/**` (UI Toolkit built in code, `Loc` TR/EN area files) | desk (computer/phone), meeting/final/results, shell (menu, lobby, settings, how-to) |
| World | `Scripts/Rooms/**` | first-person office + meeting room presenter driven by `LineWorldDriver` |
| Tooling | `Tools/` | `build.ps1`, `run_local4.ps1` (N local processes, bots), `verify_logs.py` (phase coverage + identical result hash), `compile_check.py`, `CoreTests`, `VoiceTests`, `LineSim` |

## 2. Authority model

- **Host-authoritative**: only the host runs the engine; time is NGO server time clamped monotonic (`HostLineController.Now`).
- Clients receive *their own* view, events and action results; the engine builds views from an allow-list
  (`LineEngine.Views.cs`) and privacy is tested by byte-scanning encoded views (`LinePrivacyTests`).
- Events carry an audience (`All`, `Player`, `Host`); in v3 nothing emitted `Host` events, so host-only semantic
  information (choices, trick use) was not observable without reading engine internals.
- Bots run on the host (`Slot.Virtual`) or as separate `-bot` processes (`LineBotDriver`).

## 3. Instrumentation points that exist

| Need | Verified hook |
|---|---|
| Phase transitions | engine `PhaseChanged` events (host drain) / `LineClient.PhaseChanged` (client) |
| Calls | `CallIncoming`, `CallConnected` (A = call id), `CallEnded` (A = reason), `CallBusy`, `MissedCall` — all routed to participants; the host sees every audience before routing |
| Choices/votes | public `RoundSubmitted`, `VoteCast` (no content); content only in reveals/verdict views |
| Blame | public `BlameChanged` (A delta, B reason) |
| Recordings | `HostLineController.TriggerRecordings` → `PlayRecording` delegate → `HostLineVoiceBridge.PlayPick` → `VoiceSystem.TryPlayBestRecording`; `VoiceSystem.OnHostRecordingFinished`; per-candidate metadata via `VoiceSystem.Recorder.TryGetCall` / `TrySelectSnippet` (speech ms, speakers mask) |
| Connection lifecycle | `ConnectionManager.SetState` → `StateChanged` (single choke point: Offline/Hosting/Connecting/Connected/Reconnecting/Failed + error key), host `Approve`/`OnClientConnected`/`OnClientDisconnected` |
| UI friction | `LineClient.ResultReceived` (only `DeskUi.OnResult` subscribed; meeting votes and lobby had no rejection feedback) |
| Speaking state (no new mic analysis) | `VoiceSystem.IsSpeaking(slot)`, `OnSpeakingChanged`, `GetSpeechLevel` (client-side; routing-dependent), `RecordingActive` |
| Voice quality | `VoiceStatsReporter` (text line every 30 s: loss, jitter, underruns) |

## 4. Missing callbacks / gaps found

1. No analytics of any kind (no telemetry, metrics, export; `com.unity.modules.unityanalytics` stub only; no
   `com.unity.services.*` package in `Library/PackageCache`).
2. No host-only semantic events: choice categories, trick use and phase-end reasons were not observable.
3. `PlayPick` returned −1 for every failure (voice missing, disabled, no candidates, no speech, streamer refused) —
   failure reasons and actual playback duration were not available. **Fixed in v4** (`RecordingPlayInfo`, tape-finish
   timing in `HostLineVoiceBridge`).
4. Phase-end reason (timer vs all-submitted shortening) was not exposed. **v4**: `PhaseChanged.B` + `HostEarlySubmit`.
5. No match id, session id or group id; week ids restart per process. **v4**: `SessionId`, `GroupId`, `MatchId`.
6. No consent mechanism; the connection payload carried nothing about analytics. **v4**: consent + pseudonymous id in
   `LineConnectionPayload`, update RPC, per-seat consent on the host.
7. No rematch intent (the lobby only showed ready flags). **v4**: `LineLobbySlot.WantsRematch`, results "Play again".
8. Reconnect flip-flop: a `reject.*` during `ConnectionManager.ReconnectLoop` sets `Failed` but the coroutine keeps
   reconnecting until `net.reconnect_failed` — telemetry must debounce and emit terminal outcomes only.
9. Meeting votes and `PickNextManager` had no rejection handling in the UI.
10. No per-player all-call airtime; only client-local speaking signals. Speaking time in meetings is therefore reported
    as `not_measured` unless explicitly added later (and consented).

## 5. Design ↔ code discrepancies (v3 docs)

- Kayıt Sil (Erase Tape) is retired in code (`LineEnums`, engine rejects it) but `V3_DESIGN.md` trick table/recording
  text, `CONTRACTS_V3.md` (RecordingErased) and `VOICE.md` still describe it; `VoiceSystem.EraseTape` has no callers.
- Recording weighting: docs say "weighted by duration"; code multiplies by `RecordingFocusWeight` (3) for Bonus Pool
  partners / hidden player.
- Recording clearing: `VOICE.md` says after every meeting; code clears when the next `Morning` or `WeekResults` starts.
- First-day schedule: `V3_DESIGN.md §3` and `CONTRACTS_V3.md` say Bonus Pool + Complaint Box; code (and V3 §0) Bonus Pool +
  Odd Email.
- Complaint Box requires ≥ 5 players in code; the round table and contract omit the gate.
- Manager-line unlock: code day 3 (`TipUnlockDayFirstWeek = 2`), parts of the docs say day 2.
- `docs/UI.md` partly describes v2 (6 how-to pages; code has 7) and `docs/PLAYTEST.md` is v1/v2 (Discord voice).
- `HOSTING.md`: a host *kill* ends as `net.reconnect_failed` after the 30 s loop, not "abandoned".
- `bundleVersion` was hard-coded 0.1.0 (now 0.4.0 for v4); the build id changes every build and gates joining.
v4 resolves these by superseding the rules (`GAMEPLAY_REDESIGN.md`) and updating the docs listed in the final report.

## 6. Existing analytics capability

None beyond: `MatchLog` text logs (`persistentDataPath/logs/<name>_<pid>.log`, also in release builds),
`VoiceStatsReporter` lines, `LineSim` offline bot statistics, and `verify_logs.py` (cross-process phase coverage and
result-hash agreement — keep the `phase …` and `results …` log formats, it depends on them).

## 7. Privacy findings (pre-v4)

- `MatchLog` printed the full command line (may contain `-name` and `-autojoin <IP>`). **v4**: values of identifying
  arguments are redacted.
- Player names were rendered as UI Toolkit rich text and only trimmed/truncated on the host (`<size=300>` names broke
  layout). **v4**: host sanitiser (`LineText.Sanitize`, markup characters replaced) + UI labels with rich text off.
- Per-player voice volume prefs are keyed by player *name*; screenshot file names used the name (now the slot).
- Client logs contain the player's own private actions (`send Vote(…)` etc.) — never upload `MatchLog` files.
- Host-audience events could have been logged by `host-event` lines; in v4 they carry private choices, so the line was
  removed and they go only to the telemetry bridge (held until the match ends).
- Recordings: host memory only, never written to disk (verified: the only voice `File.` call reads a WAV for
  `-voicetest`); cleared at the next `Morning`/`WeekResults`, on lobby/week start and when disabled.

## 8. Integration risks

1. The engine is rewritten for v4 in parallel with UI and telemetry; contracts were frozen first and a compile stub
   (`Tools/stubs/LineEngineStubs.cs`, `compile_check.py --engine-stubs`) lets tracks compile independently.
2. Host-side analytics logs live on a *player's* machine: anything secret is buffered until results
   (`private_until_match_end`), otherwise the host could read colleagues' choices mid-match.
3. Consent must be per participant; bots are synthetic (host consent, `is_bot`).
4. `verify_logs.py` relies on exact `MatchLog` line formats; v4 adds the `Break` phase (the verifier's phase list must be
   updated).
5. Instrumenting the v3 baseline first: not practical because v4 replaces the round system wholesale; instead the
   telemetry pipeline is validated with synthetic fixtures and v4 bot runs (labelled synthetic), and v3 bot statistics
   remain available in `Tools/LineSim/v31-balance.txt` as the only baseline numbers. No v3 human data exists.
6. Unity Analytics (UGS) needs a linked cloud project and the `com.unity.services.analytics` package, neither of which
   exists; the cloud sink compiles only when the package is installed.
