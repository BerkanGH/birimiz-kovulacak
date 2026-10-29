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
- Waiting for: engine (runtime needs `LineCodec`/`LineEngineFactory`), voice API (bridge v3 + recordings hook
  `HostLineController.PlayRecording`), world presenter, UI screens, art sync into `UI/Resources/ui/**`.
- After all merges: remove v2 gameplay code (engine v2, HUD, open-office map, third-person camera, v2 bots) from `v3`.
