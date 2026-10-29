# v3 status (lead's log)

Design: `docs/V3_DESIGN.md`. Contract: `docs/CONTRACTS_V3.md`. Branch `v3` (v2 stays on `main`, tag `v2.3-night`).

## Tracks (started 2026-10-29 morning, after the producer's go)

| Track | Who | Branch / worktree | Owns |
|---|---|---|---|
| Contract, net/host plumbing, integration, builds | lead | `v3` (main checkout) | `Core/Line/Contracts/**`, `Scripts/Net/**`, `Scripts/Boot/**`, `Editor/ProjectBuilder.cs` |
| Rules engine, codec, bot, tests, sim | Codex | `v3-engine` (`.worktrees/engine3`) | `Core/Line/Engine/**`, `Tests/Line/**`, `Tools/LineSim/**` |
| 2D art kit v3 (rounds, tricks, computer, desk phone, stamps TR/EN, EN logo, onboarding) | Codex | `v3-art` (`.worktrees/art3`) | `Art/UI/**` |
| Rooms, first-person camera, meeting room, seated characters | Claude | `v3-world` (`.worktrees/world`) | `Scripts/Rooms/**`, Blender scripts, scene builder partial |
| Calls-only voice, wiretap, call recorder, recording playback | Claude | `v3-voice` (`.worktrees/voice3`) | `Scripts/Voice/**`, `Tools/VoiceTests` |
| Desk UI (computer, phone, rounds, onboarding) | Claude | `v3-uidesk` (`.worktrees/uidesk`) | `Scripts/UI/Desk/**`, `desk.uss`, `*.desk.json` |
| Meeting/shell UI (meeting, final, results, menu, lobby, settings, language, how-to) | Claude | `v3-uishell` (`.worktrees/uishell`) | `Scripts/UI/Meeting/**`, menus, settings, `Game.uss`, `tr.json`/`en.json`, `*.meeting.json` |

Seams written by the lead first so everyone compiles in parallel: `RoomContracts.cs` (world ↔ UI ↔ lead),
`LineClient` (UI data source), `HostLineController` (lobby API), `LineMock` (`-uimock` screenshots without an
engine), `UiMotion` (tweens), `Loc` area files (`<lang>.<area>.json`) + `SetLanguage`.

## Integration log

- `cc99dca` menu moved off the busy art (tagline unreadable), toasts as paper slips without the side stripe.
- `19dd45f` V3_DESIGN.md. `e0c5c46` contract + seams. `1c07efe` host/client plumbing (HostLineController,
  NetSession Line RPCs, ConnectionManager on protocol 3, LineBotDriver, LineWorldDriver, v3 screenshots, verify_logs).
- Codex art (103 PNGs, validator green) merged + synced into UI Resources (`f3a7083`).
- Independent design review (12 findings) → v3.1 rules (V3_DESIGN §0): start blame 5, one İhtar/week, final
  shortlist vote, head-count round lengths, deal+vote day pairing, roles without repeats, trio Bonus Pool, fair Lost
  Report slips, Shared Password single-code entry, topic categories, weekly trick/tip allowances, false tips named,
  agenda +1 per İhtar, next manager picked by the fired player. Contract additions only (`ece35b4`).
- Codex engine v3.0 merged (`9346cdd`): 230 v3 tests, 6-player view 1,060 B, Short Week sim 0.34 s. 26 tests red until
  Codex v3.1 (running) encodes the new fields.
- Voice merged (`0020547`): Calls mode, wiretap, recorder (≤ 6.2 MB worst day at 6p, 0 B/frame), tape playback;
  109 voice tests. Lead wiring `9bc0e80` (HostLineVoiceBridge).
- **First v3 network week (4 processes, bots, synthetic voice, scale 0.15): completed, all 4 agree on the result hash.**
  Host clock jitter threw in LineEngine.Tick → monotonic clock (`5bfdfa3`). Calls too short at 0.15 for recordings.
- Codex v3.1 engine merged (`fc31911`): 774 tests green (497 v2 + 277 v3); LineSim 2,000 weeks per head count.
- Week start time fix (`2cee6d5`): no more monotonic-time exceptions. Recordings play every meeting (voice picks the call with speech).
- Meeting/shell UI merged (`3315582`): dossier slides on the projector RenderTexture, tape deck, ballot, İhtar stamp,
  shortlist defenses, final ballot + letter, week results with next-manager pick, menu/lobby/settings/how-to, TR+EN.
- World merged (`e0dbd91`): first-person office + meeting room (Blender kit, ~49k tris), seated characters with talk
  animation, POV camera, room lighting morning→dusk; scenetest peak 385 MB working set.
- Desk UI merged (`3810bf4`): KOVA OS computer (5 apps), desk phone, morning call, rule cards, all six rounds play +
  reveal, onboarding sticky note, TR+EN (548 keys each).
- Codex review of the integration layer: 12 findings, 11 fixed (`c4da37d`) — server-only host RPCs, stale recordings,
  session history on lobby return, reconnect seat races, rejoin credentials, tape queueing, per-frame clones, cue order.
  The 12th (v2 screens kept alive on language rebuild) goes away with the v2 cleanup (running, branch v3-cleanup).
- **Network runs (bots + synthetic voice):** 4p Short (0.3) PASS, 5p Short (0.3) PASS, **6p Full Week (0.25) PASS** —
  same result hash on every process, all v3 phases, 0 errors, a recording played in every meeting.
- After all merges: remove v2 gameplay code (engine v2, HUD, open-office map, third-person camera, v2 bots) from `v3`.
