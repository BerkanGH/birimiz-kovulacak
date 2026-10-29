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

Re-measured with visible windows (hidden windows skip rendering; `Builds/runs/memv_baseline`): peak working set
713–736 MB, peak private 1260–1275 MB per process — same picture.

## v2 (integration builds, 4 bot processes, visible windows, `-DurationScale 0.35`)

| Build | What changed | Peak working set | Peak private bytes |
|---|---|---|---|
| RC baseline | — | 713–736 MB | 1260–1275 MB |
| v2 build 2 (`Builds/runs/memv_int2`) | D3D11 first, MSAA 4x→2x, incremental GC, mesh/mip stripping | 420–431 MB | 848–865 MB |
| v2 build 5 (`Builds/runs/int5_ui`) | + characters (6 looks, 48 clips), voice (libopus), full UI + 2D kit | 438–448 MB | 885–925 MB |

**−39 % working set, −29 % private bytes** versus the RC while the game gained characters, voice and a much richer
UI. The 6-player run of build 1 (still D3D12) also passed (`Builds/runs/int1_p6`: identical result hash on all six).

Where the savings come from: the D3D12 device + split graphics jobs mapped ~300 MB per process for a scene this
small; D3D11 is the primary API now (D3D12 kept as fallback). MSAA 2x halves the multisampled colour/depth targets.

## v3 "Ofis Hattı" (first-person rooms; 4 bot processes, visible windows, `-DurationScale 0.35`)

| Build | Peak working set | Avg working set | Peak private bytes |
|---|---|---|---|
| v3 final (`Builds/runs/mem_20261029_134838`, after the v2 code removal) | 415–427 MB | 387–401 MB | 849–882 MB |

Versus the v1 RC (≈ 700 MB average working set) that is −43 % per process, with a far richer scene. What keeps it
there: one office room built once and personalised for the local player (others never load), the meeting room
loaded once, ~49k triangles for the whole room kit, sun-only shadows, no NavMesh, no networked avatars (seated first
person: nothing moves over the network), views of ~1 KB per player (6 players mid-week: 1,060 B), the engine's idle
tick allocation-free, voice 0 B per frame, call recordings in a pooled buffer (≤ 6.2 MB on the worst day at 6
players, cleared every morning, never written to disk), and 28.6k lines of v2 code removed from the build.
v3 engine: a 6-player Short Week simulates in ~0.34 s; Tick idle ~26 M/s.

### Engine and voice (measured in the .NET harnesses)

| Item | Budget | Measured |
|---|---|---|
| Engine `Tick`, 6 players, active inspection | < 0.2 ms | 0.0032 ms, 0 B allocated per tick |
| 6× `BuildView` batch (populated results) | < 0.3 ms | 0.152 ms |
| Voice encode / decode per 20 ms frame (libopus) | — | 0.095 ms / 0.013 ms, 0 B per frame |
| Voice bitrate per talking player (on the wire, incl. NGO header) | ≤ 24 kbps | ~21–22 kbps (Unity run: 16.3–17.3 kbps stream) |
| Voice memory | bounded | ~1.1 MB total, allocated once (pooled emitters) |
| Host voice upload, worst case (meeting, 6 talking) | 720 kbps | ~470 kbps |

### Design rules that keep it lean

- Rules engine: preallocated position log, no per-tick allocations, views rebuilt only at ≤10 Hz per client.
- Audio: all 47 SFX are synthesised at boot (~3.4 MB, no audio assets). Voice uses pooled emitters and buffers.
- Characters: one shared rig and clip set, one 512² face atlas (BC7) for all looks, flat materials.
- UI: textures loaded once from Resources; full-screen illustrations are unloaded when their screen hides; HUD
  updates only on view changes.
- Visibility: 3 line-of-sight raycasts per frame round-robin, static hit buffers.
