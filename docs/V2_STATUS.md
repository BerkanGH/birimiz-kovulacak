# v2 status (lead's log)

Plan: `docs/V2_PLAN.md`. Rules: `docs/CONTRACTS_V2.md`.

## Tracks (started 2026-10-29)

| Track | Who | Branch / output | Started | State |
|---|---|---|---|---|
| Contracts v2 | lead | main `68c735d` | 01:40 | done |
| Compile-check tool | lead | main | 01:15 | done (`Tools/scripts/compile_check.py`) |
| 2D UI art kit | Codex (art run, main checkout, `Art/UI/**`) | uncommitted until review | 01:22 | running |
| Engine v2 (8 stages) | Codex | `codex/engine-v2` (`.worktrees/codex-engine-v2`) | 01:55 | running |
| Voice | Claude sub-agent | `v2/voice` | 01:25 | running |
| Characters & animation | Claude sub-agent | `v2/characters` | 01:25 | running |
| Map, props, lighting | Claude sub-agent | `v2/map` | 02:00 | running |
| UI/UX | Claude sub-agent | `v2/ui` | 02:00 | running |
| Camera, visibility, input, audio, VFX | Claude sub-agent | `v2/feel` | 02:00 | running |
| N-player networking, lobby bots, v2 plumbing, bots | lead | main | 02:00 | in progress |

Codex logs: `Builds/codex/*.log`, last messages `Builds/codex/*_last.txt`. Baseline RC build kept at
`Builds/baseline_rc` for memory comparison.

## Integration log

- 02:10 host lobby v2 (3–6, AddBot/RemoveBot, SetMode, week day), `-week -manager -expectplayers -voicetest`
  args, runner `-Players` (main `0d8b341`).
- 02:20 bots exercise every v2 verb (main `6d63a88`).
- 02:25 `ConfigVersion`: outdated `match_config.json` → defaults + builder rewrite (main `612843e`).
- 02:35 surprise feature contract: "Günün Klibi" results replay + positioned footage sightings (main `bc308cf`);
  UI agent asked for the replay, a minimap for footage and a "Mektubu kaydet" PNG share button.
- Codex art: style locked; portraits (4 managers × normal/angry, 6 players) and procedural 9-slice buttons reviewed — good.
- 02:45 baseline RC memory: ~700 MB working set / ~1.25 GB private per process (D3D12) → `docs/PERFORMANCE.md`.
  Memory settings on main: D3D11 first, incremental GC, MSAA 2x, mesh/mip stripping (`db4c343`).
- 03:05 **Codex engine v2 done**: 8 stages, 347 tests, Tick 0.003 ms, 6× BuildView 0.15 ms, 0 B/tick. Codex could
  not commit (Windows denied `index.lock` even with `--add-dir .git`); lead committed `04bd6e9` and merged to main.
  4 reflection-completeness tests fail on main only because of the later highlight/position/ConfigVersion contract
  fields → Codex follow-up v2.1 running.
- 03:10 host sends clients `engine.Config` (profile/day applied). First Unity integration build started.
- 03:20 Codex 2D kit done (149 PNGs, verified Turkish text) → main `4f03d62`; second art run (how-to, weekdays,
  title badges, bubbles, app icon, store capsules) started.
- 03:25 integration build 1 OK (1.5 min). **First 6-player v2 bot match: all 6 processes agree on the result hash,
  0 errors** (old scene, so quota low — props not yet at v2 positions).
- 03:35 found 6 PowerShell edits that silently did not apply on LF files (Partitions layer, D3D11/GC settings,
  config loader version check) — reapplied with an anchor-asserting script (`0a70555`). Lesson: patch via Python
  with asserts, never PowerShell `.Replace` with `\r\n`.
- 03:40 feel merged + integrated (crouch → engine samples, partitions in LOS mask, bots sneak) `04bca20`.
- 03:50 Codex v2.1 (highlight replay, positioned footage) merged: **382 tests green on main**. Codex cross-review of
  Claude's Unity-side code running (read-only snapshot `.worktrees/review`).
- 04:05 **memory, visible windows, 4 bots:** RC 720 MB working set / 1.27 GB private → v2 build 427 MB / 855 MB per
  process (−41 % / −33 %; D3D11 + MSAA 2x + stripping). Hidden windows had silently disabled screenshots → script fixed.
- 04:10 Codex art run 2 (how-to ×6, weekday banners, title badges, bubbles, app icon, store capsules) → main; exe icon
  wired in the builder.
- 04:15 Codex cross-review: 5 verified findings (camera spring blow-up at low FPS, carried-item visibility leak, bot
  footage time origin, observation feedback lost, bot hang-up timer) — all fixed (`6e2add8`).
- 04:25 voice merged (native libopus 1.5.2 primary, Concentus fallback; 0 B/frame; 70 tests; ~21 kbps on the wire;
  call privacy tested) `3387b80`.
- 04:35 characters merged: 6 player looks + 4 profile managers matching the portraits, face atlas, 48 clips; builder
  log "Characters: avatar (6 looks), manager (5 looks)". Unity build 4 OK (0.9 min).
