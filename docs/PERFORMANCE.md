# Performance & memory

Budgets: `docs/V2_PLAN.md` §8. Measurement tool: `Tools/scripts/measure_memory.ps1` (runs a local bot match and
samples every game process every 2 s → `memory.csv`, `memory_summary.csv`).

Test machine: i5-12500H (16 threads), 32 GB RAM, RTX 3050 Ti Laptop (4 GB), Windows 11; all processes on one PC,
960×540 windows.

## Baseline — v1 release candidate (`Builds/baseline_rc`, 2026-10-29 01:40)

4 bot processes, `-DurationScale 0.4`, one full match (`Builds/runs/mem_baseline_rc`):

| Process | Peak working set | Avg working set | Peak private bytes |
|---|---|---|---|
| host (Mavi) | 734 MB | 699 MB | 1291 MB |
| client (Pembe) | 721 MB | 677 MB | 1250 MB |
| client (Sarı) | 712 MB | 668 MB | 1264 MB |
| client (Yeşil) | 710 MB | 675 MB | 1254 MB |

Graphics API Direct3D 12 (split graphics jobs). The private bytes are dominated by driver/graphics allocations, not
by game data (one small scene, procedural audio).

## v2

(filled in after integration)
