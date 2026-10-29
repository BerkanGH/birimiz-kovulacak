# Birimiz Kovulacak

*Ofisi birlikte kurtar. Sonra suçu arkadaşına at.*

A 3–6 player online social-deception comedy for Windows (Turkish + English) with **built-in voice**. **v3 "Ofis
Hattı":** every player sits alone in their own office, in first person. The only channel is the desk phone (private
1:1 calls, which may be recorded). Each day brings two short office rounds with one-sentence rules (Bonus Pool, Odd
Email, Lost Report, Complaint Box, Promotion List, Shared Password), played on your computer and decided on the phone.
At the end of the day everyone meets around one table: the manager presents the day, **plays a real 7-second clip
of somebody's phone call**, and the room votes a written warning (İhtar). Nobody is eliminated until the last day,
when the shortlist defends and one person is fired. A hidden agenda, one dirty trick a week and four manager
personalities keep every week different.

v3: design `docs/V3_DESIGN.md` (§0 = v3.1 rules) · contract `docs/CONTRACTS_V3.md` · engine `docs/ENGINE_V3.md` ·
voice `docs/VOICE.md` · status `docs/V3_STATUS.md`. v2 (open office, third person) lives on `main`, tag `v2.3-night`.

## Play (testers)

1. Unzip `BirimizKovulacak-Windows-Playtest.zip`, run `BirimizKovulacak.exe`, pick Türkçe or English.
2. Settings → Microphone: pick your mic (push-to-talk **V**). Voice is in-game — no Discord. Headphones recommended.
3. One player: **Lobi Kur / Host** (Short Week 3 days ≈ 30 min, Full Week 5 days ≈ 50 min; call recordings can be
   switched off in the lobby). Others: host's IP + port 7777 → **Join** → **Ready**. 3–6 players; bots fill seats.
4. In the office: mouse look, click the monitor (**E**) or the phone (**Tab**), **Y/N** answer/decline, **V** talk,
   **Esc** back. In the meeting: **Tab** switches between looking around and the projector/ballot.

Full Turkish guide (internet play via Tailscale/port forwarding, reconnects, troubleshooting): `docs/HOSTING.md`.

## Build from source

Requirements (as used for this prototype):

| Tool | Version |
|---|---|
| Unity | 6000.6.3f1 (Windows Build Support (Mono) included) — see DECISIONS §1 about LTS |
| Packages | URP 17.6.0, Netcode for GameObjects 2.13.2, Unity Transport 6.6.0 (built-in), Input System 1.20.0, uGUI/TextMeshPro 2.6.0, AI Navigation 2.0.14, Newtonsoft Json 3.2.2, Test Framework 1.8.0 |
| Blender | 5.2.2 LTS (only to regenerate art) |
| .NET SDK | 8.0+ (only for the fast rules test harness) |
| Python | 3.x with Pillow (only for tooling: package extraction, contact sheets, log verification) |

```powershell
# Rules tests (pure C#, ~1 s)
cd Tools\CoreTests\Tests ; dotnet test

# Regenerate Unity content (materials, prefabs, Rooms scene) and build the Windows x64 player
powershell -ExecutionPolicy Bypass -File Tools\build.ps1            # add -Art to regenerate Blender assets first

# N local windows (host + clients, 3..6) driven by test bots, then verify identical results
powershell -ExecutionPolicy Bypass -File Tools\run_local4.ps1 -Bots -DurationScale 0.3 -Matches 1 -Players 6 -Week -VoiceTest speech

# v3 UI mocks / 3D scene test (screenshots without a network)
Builds\BirimizKovulacak-Windows\BirimizKovulacak.exe -uimock "desk.*" -uimockout Builds\mock
Builds\BirimizKovulacak-Windows\BirimizKovulacak.exe -scenetest -scenetestout Builds\scenetest

# v3 balance simulator (bots that do not talk: social-round catch rates are a floor, not a forecast)
dotnet run -c Release --project Tools\LineSim

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

No external services, accounts or secrets are required. Unity Relay / Lobby were not used (they need a linked Unity
Gaming Services project); remote play uses direct IP (port forwarding or a VPN such as Tailscale).

## Repository layout

```
Birimiz_Kovulacak_Oyun_Akisi.pdf   design concept (source of truth for rules)
docs/                              DECISIONS, CONTRACTS, ARCHITECTURE, ASSETS, HOSTING (TR), VERIFICATION, PLAYTEST, CODEX_*
Game/                              Unity project
  Assets/Game/Core/Contracts       shared rule contracts (pure C#)
  Assets/Game/Core/Engine          authoritative rules engine (Codex work package)
  Assets/Game/Tests/Core           NUnit rules tests (Unity EditMode + dotnet)
  Assets/Game/Scripts              networking adapter, world, player, UI, bots, audio
  Assets/Game/Editor               ProjectBuilder (generates scene/prefabs/materials, builds player)
  Assets/StreamingAssets           match_config.json (balance), Localization/tr.json
Art/                               Blender generator scripts, .blend sources, FBX exports, palette, previews
Tools/                             build/run/package scripts, .NET test harness, verification scripts
Builds/                            (ignored) player builds, logs, test runs, ZIP
```

## Development model

v1: the rules engine and codec were delegated to Codex (`docs/CODEX_PACKAGE.md`, `docs/CODEX_NOTES.md`); Unity
integration, art, UI, networking, bots and verification by Claude (`docs/VERIFICATION.md`).

v2 (one overnight session, `docs/V2_STATUS.md`): Claude (lead) wrote the plan, the contracts and the networking/bot
integration and ran every build; Codex implemented engine v2–v2.2 (`docs/CODEX_NOTES_V2.md`), produced the whole 2D
art kit with image generation (`Art/UI/`), and cross-reviewed Claude's code; Claude sub-agents built voice
(`docs/VOICE_NOTES.md`), characters (`docs/CHARACTERS.md`), the multi-room map (`docs/MAP.md`), the UI
(`docs/UI.md`) and camera/visibility/audio/VFX (`docs/FEEL.md`), and a Claude reviewer stress-tested the rules for
exploits (`docs/BALANCE_V2.md`).
