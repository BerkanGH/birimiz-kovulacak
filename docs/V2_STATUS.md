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
