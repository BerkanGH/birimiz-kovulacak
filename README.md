# Birimiz Kovulacak

*Ofisi birlikte kurtar. Sonra suçu arkadaşına at.*

A 3–6 player online social-deduction office comedy for Windows (Turkish + English) with **built-in voice**.

## Developers

Developed together by **Lucestdra** and **BerkanGH**.

Development was AI-assisted (Claude Code; earlier versions also used OpenAI Codex).

> **v5 is the current version.** The game runs the v5 ruleset "Her Şey Kayıtta" (`v5.0-kayit`): 3 tasks (Client
> Bonus, Petty Cash, Weekend Shift), 1–2 meetings with blame votes, investigations and leaked call recordings, a crisis
> vote and a final firing vote whose desk goes to the voters. Rules: `docs/V5_ENGINE_SPEC.md` (design
> `docs/V5_DESIGN.md`, plan `docs/V5_IMPLEMENTATION_PLAN.md`); play UI: `Game/Assets/Game/Scripts/UI/Play`. The v4 text
> below and the tester steps about the desk/monitor, Short/Full weeks and task tutorials describe v4 and are history
> (tag `v4.0-hybrid`).

**v4 "Hybrid Office"** (ruleset `v4.0-hybrid`): everyone sits alone in a first-person office and talks through the desk
phone (private 1:1 calls) or by text (messages, email with replies, meeting chat, anonymous complaints). Each match is
3 work blocks (Short, ≈ 20 min) or 5 (Full, ≈ 33 min); every block has two office **tasks** (12 in the pool: invoices,
budgets, approvals, a visitor badge, a server code, a phishing check…) and a meeting. Everyone needs the **company** to
reach 65 % productivity — or it closes and everyone is fired — but everyone also has a **secret objective**. Players
negotiate, trade money, buy and present evidence, and may sabotage (provable only with records). Meetings play a real
7-second phone-call recording; the final vote may fire one person — or nobody. Career points decide the ranking.

Design `docs/GAMEPLAY_REDESIGN.md` · contract `docs/CONTRACTS_V4.md` · engine `docs/ENGINE_V4.md` · analytics
`docs/ANALYTICS_ARCHITECTURE.md`, `docs/ANALYTICS_SETUP.md`, `docs/ANALYTICS_EVENT_CATALOG.md`, KPIs and the H1–H9
playtest plan `docs/PLAYTEST_KPI_GUIDE.md` · music `docs/AUDIO_MUSIC.md` · status `docs/V4_EXECUTION_RECORD.md`,
`docs/ANALYTICS_IMPLEMENTATION_REPORT.md`. v3 (rounds, shortlist firing) is history: tag `v3.1-day`.

## Play (testers)

1. Unzip `BirimizKovulacak-Windows-Playtest.zip`, run `BirimizKovulacak.exe` (fullscreen on first launch), pick Türkçe or
   English, answer the optional playtest-analytics question (default No; Settings → Privacy changes, deletes, exports).
2. Settings → Microphone: pick your mic (push-to-talk **V**). Voice is in-game — no Discord. Headphones recommended.
3. One player: **Lobi Kur / Host** (Short / Full; call recordings can be switched off; optional playtest group code).
   Others: host's IP + port 7777 → **Join** → **Ready**. 3–6 players; bots fill seats.
4. In the office: mouse look, monitor (**E**) or phone (**Tab**), **Y/N** answer/decline, **V** talk, **Esc** back,
   **F8** bookmark a moment (playtest only). Every task has a 5-step tutorial (the **?** button reopens it).

Full Turkish guide (internet play via Tailscale/port forwarding, reconnects, analytics export): `docs/HOSTING.md`.

## Build from source

Requirements (as used for this prototype):

| Tool | Version |
|---|---|
| Unity | 6000.6.8f1 (Windows Build Support (Mono) included; upgraded from 6000.6.3f1 in 2026-11) — see DECISIONS §1 about LTS |
| Packages | URP 17.6.0, Netcode for GameObjects 2.13.3, Unity Transport 6.6.0 (built-in), Input System 1.20.1, uGUI/TextMeshPro 2.6.0, Newtonsoft Json 3.2.2, Test Framework 1.8.0 |
| Blender | 5.2.2 LTS (only to regenerate art) |
| .NET SDK | 8.0+ (only for the fast rules test harness) |
| Python | 3.x with Pillow (only for tooling: package extraction, contact sheets, log verification) |

```powershell
# Rules tests (pure C#, ~6 s)
cd Tools\CoreTests\Tests ; dotnet test

# Regenerate Unity content (materials, prefabs, Rooms scene) and build the Windows x64 player
powershell -ExecutionPolicy Bypass -File Tools\build.ps1            # add -Art to regenerate Blender assets first

# N local windows (host + clients, 3..6) driven by v5 test bots, then verify identical results and the v5 phase sequence
# (-CallTest: bots also call each other, record one call and leak it in meeting 1; -BotPolicy honest|greedy|mixed)
powershell -ExecutionPolicy Bypass -File Tools\run_local4.ps1 -Bots -DurationScale 0.3 -Matches 1 -Players 6 -VoiceTest speech -CallTest

# v5 play-screen mocks / 3D scene test (screenshots without a network)
Builds\BirimizKovulacak-Windows\BirimizKovulacak.exe -uimock "v5.*" -uimockout Builds\mock
Builds\BirimizKovulacak-Windows\BirimizKovulacak.exe -scenetest -scenetestout Builds\scenetest

# v5 balance simulator (synthetic Honest / Greedy / Mixed bots; they never call or record)
dotnet run -c Release --project Tools\LineSim\LineSim.csproj -- --matches 1000 --output Tools\LineSim\v5-sim.txt

# Localisation checks (v5 play UI area; shell + privacy areas)
python -I Tools\scripts\loc_check.py ; python -I Tools\scripts\loc_parity_meeting.py

# Telemetry / music tests (pure C#)
dotnet test Tools\TelemetryTests ; dotnet test Tools\MusicTests

# Playtest analysis (stdlib Python): JSONL from every machine (+ optional observer / feedback CSV) -> Reports
python -I Tools\scripts\analyze_playtests.py <folders...> --observer obs.csv --feedback fb.csv --out Reports
python -I -m unittest discover -s Tools\AnalysisTests

# Soundtrack (development only; needs ELEVENLABS_API_KEY; skips existing cues) - see docs/AUDIO_MUSIC.md
python -I Tools\music\generate_music.py ; python -I Tools\music\process_music.py

# Same, sampling memory of every process (writes memory_summary.csv)
powershell -ExecutionPolicy Bypass -File Tools\scripts\measure_memory.ps1 -Players 4 -DurationScale 0.35

# Unity-free compile check of the Game/Editor assemblies (~15 s; works in worktrees)
python -I Tools\scripts\compile_check.py

# Voice core tests
cd Tools\VoiceTests\Tests ; dotnet test

# Shareable ZIP -> Builds\BirimizKovulacak-Windows-Playtest.zip
powershell -ExecutionPolicy Bypass -File Tools\package.ps1
```

Set `UNITY_EXE` / `BLENDER_EXE` if your editors are installed elsewhere. Open `Game/` in the Unity Hub to work in the
editor; menu **Birimiz Kovulacak → Rebuild Assets + Scene** regenerates generated content.

Playing, building and analysing need no external services, accounts or secrets. Unity Relay / Lobby / Analytics are
not linked (they need a Unity Gaming Services project; the optional cloud analytics sink compiles only when
`com.unity.services.analytics` is installed — `docs/ANALYTICS_SETUP.md`). Remote play uses direct IP. Only regenerating
the soundtrack calls ElevenLabs (development machine, key from the environment, never stored in the repo or build).

## Repository layout

```
Birimiz_Kovulacak_Oyun_Akisi.pdf   original design concept (v1)
docs/                              GAMEPLAY_REDESIGN (v4 rules), CONTRACTS_V4, ENGINE_V4, ANALYTICS_*, PLAYTEST_KPI_GUIDE,
                                   AUDIO_MUSIC, HOSTING (TR), V4_EXECUTION_RECORD, codex/ (briefs), older v1-v3 docs
Game/                              Unity project
  Assets/Game/Core/Line            v4 rule contracts + authoritative engine, codec, bots, schedule (pure C#)
  Assets/Game/Core/Telemetry       analytics core: catalog, consent-gated service, JSONL sink, host observer (pure C#)
  Assets/Game/Core/Audio           pure music cue planner
  Assets/Game/Tests/Line           NUnit rules tests (Unity EditMode + dotnet)
  Assets/Game/Scripts              networking/host, rooms (first person), desk/meeting UI, voice, audio/music, telemetry glue
  Assets/Game/Audio/Resources      generated soundtrack (OGG)
  Assets/Game/Editor               ProjectBuilder (scene/prefabs/materials, player settings, build), music import settings
  Assets/StreamingAssets           Config/line_config.json (rules), Config/telemetry_config.json, Localization/<lang>(.<area>).json
Art/                               Blender scripts and exports, 2D UI kit (Art/UI, incl. v4 task/evidence cards), Audio/Music sources
Tools/                             build/run/package scripts, CoreTests, TelemetryTests, MusicTests, AnalysisTests, LineSim,
                                   music/ (ElevenLabs tooling), playtest/ (observer + feedback forms), scripts/ (analyzer, checks)
Reports/                           analysis output (committed examples are SYNTHETIC)
Builds/                            (ignored) player builds, logs, test runs, ZIP
```

## Development model

v4 (one session, `docs/V4_EXECUTION_RECORD.md`): Claude Opus 5.5 (lead) wrote the design, contracts, host/net
integration and docs and ran every build/run; Codex `gpt-6.1-sol` (max) implemented the v4 engine, codec, bots and
simulator, produced the v4 card art with its image tool, and ran an independent integration review; Claude Sonnet 5.5
sub-agents built telemetry, the playtest analyzer and tools, the desk and meeting/shell UI, the ElevenLabs soundtrack and
music playback, and ran an audit and a rules-conformance review.

v1: the rules engine and codec were delegated to Codex (`docs/CODEX_PACKAGE.md`, `docs/CODEX_NOTES.md`); Unity
integration, art, UI, networking, bots and verification by Claude (`docs/VERIFICATION.md`).

v2 (one overnight session, `docs/V2_STATUS.md`): Claude (lead) wrote the plan, the contracts and the networking/bot
integration and ran every build; Codex implemented engine v2–v2.2 (`docs/CODEX_NOTES_V2.md`), produced the whole 2D
art kit with image generation (`Art/UI/`), and cross-reviewed Claude's code; Claude sub-agents built voice
(`docs/VOICE_NOTES.md`), characters (`docs/CHARACTERS.md`), the multi-room map (`docs/MAP.md`), the UI
(`docs/UI.md`) and camera/visibility/audio/VFX (`docs/FEEL.md`), and a Claude reviewer stress-tested the rules for
exploits (`docs/BALANCE_V2.md`).
