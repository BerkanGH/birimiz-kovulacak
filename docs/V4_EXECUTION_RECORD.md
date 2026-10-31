# v4 execution record

Lead: Claude Opus 5.5 (`claude-opus-5-5`) — architecture, contracts, net/host integration, docs, verification.
Model facts below are taken from the tools themselves (Codex session metadata; each Claude sub-agent reports the model
id from its own system prompt). No token counts or costs are recorded because none were exposed.

## Capability check (2026-11-01)

| Capability | Result |
|---|---|
| Claude sub-agents, Sonnet 5.5 | Agent tool alias `sonnet`; first sub-agent reported `claude-sonnet-5-5` |
| Codex CLI | `codex-cli 0.165.0`; models cache lists `gpt-6.1-sol` with efforts low…max, **ultra** |
| Codex image generation | `imagegen` system skill present (built-in `image_gen` tool) |
| ElevenLabs | `ELEVENLABS_API_KEY` set; `/v1/user/subscription` OK (Creator, active); key lacks `models_read` |
| Unity | 6000.6.3f1 (and 6000.3.26f1) installed; batch builds via `Tools/build.ps1` |
| .NET / Python / ffmpeg | dotnet 8 SDK; Python 3.13 (no Pillow); ffmpeg/ffprobe present |
| Unity Gaming Services | no `com.unity.services.*` package, no linked cloud project |

## Workstreams

| Workstream | Executor (model / effort) | Branch / worktree | Owned files | Status | Validation | Integration |
|---|---|---|---|---|---|---|
| Audit (read-only) | Claude Sonnet 5.5 (`claude-sonnet-5-5`, reported) | — | none | done | notes → `docs/ANALYTICS_AUDIT.md` | merged into the audit doc by the lead |
| Contracts, design, seams, host/net, boot, ProjectBuilder | lead (Opus 5.5) | `v4` | `Core/Line/Contracts/**`, `Scripts/Net/**`, `Scripts/Boot/**`, seams in `Scripts/Telemetry/TelemetryHooks.cs` + `HostTelemetryBridge.cs` (signatures), `Scripts/Audio/MusicSettings.cs`, `Editor/ProjectBuilder.cs`, docs | in progress | contracts compile; non-UI scripts compile with a v3 shim | — |
| Rules engine v4, codec, bot, sim, tests | Codex `gpt-6.1-sol`, effort **max** (verified in the Codex session record) | `v4-engine` / `.worktrees/engine4` | `Core/Line/Engine/**`, `Tests/Line/**`, `Tools/LineSim/**`, `docs/ENGINE_V4.md` | running | — | — |
| 2D art (task/evidence/intro/results cards) | Codex `gpt-6.1-sol`, effort high + built-in image tool | `v4-art` / `.worktrees/art4` | `Art/UI/tasks|evidence|onboarding_v4|results_v4/**`, `UI/Resources/ui/<same>/` | running | — | — |
| Telemetry core, observer, consent, sinks, overlay, UGS adapter | Claude Sonnet 5.5 | `v4-telemetry` / `.worktrees/telemetry4` | `Core/Telemetry/**`, `Scripts/Telemetry/**` (bodies), `Tools/TelemetryTests/**`, catalog + setup docs | running | — | — |
| Playtest analyzer, fixtures, observer/feedback tools, KPI guide | Claude Sonnet 5.5 | `v4-analysis` / `.worktrees/analysis4` | `Tools/scripts/analyze_playtests.py`, `telemetry_fixtures.py`, `Tools/playtest/**`, `Tools/AnalysisTests/**`, `Reports/**`, `docs/PLAYTEST_KPI_GUIDE.md` | running | — | — |
| Desk/office UI (tasks, tutorials, messages, bank, evidence, remedial) | Claude Sonnet 5.5 | `v4-desk` / `.worktrees/desk4` | `Scripts/UI/Desk/**`, `UI/Dev/LineMock.cs`, `desk.uss`, `*.desk.json`, `*.tasks.json` | running | — | — |
| Meeting/results/shell/privacy UI | Claude Sonnet 5.5 | `v4-shell` / `.worktrees/shell4` | `Scripts/UI/Meeting/**`, menus/settings/shell, `Game.uss`, `meeting.uss`, `tr/en.json`, `*.meeting.json`, `*.privacy.json` | running | — | — |
| Soundtrack (ElevenLabs Music) + music playback | Claude Sonnet 5.5 | `v4-audio` / `.worktrees/audio4` | `Tools/music/**`, `Art/Audio/Music/**`, `Audio/Resources/music/**`, `Scripts/Audio/**` (not MusicSettings), `Core/Audio/**`, `Editor/MusicImportPostprocessor.cs`, `docs/AUDIO_MUSIC.md` | running | — | — |
| Independent integration review | Codex `gpt-6.1-sol` (max; ultra only if justified) | — | read-only | pending | — | — |

## Decisions recorded by the lead

- Instrumenting the v3 baseline in Unity before the redesign was judged impractical (v4 replaces rounds wholesale);
  telemetry is validated with fixtures and v4 bot runs (synthetic, labelled).
- Codex cannot commit inside worktrees on this machine (Windows `index.lock` denial, previous sessions); the lead
  commits Codex output after review.
