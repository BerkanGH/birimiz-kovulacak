# Birimiz Kovulacak

*Ofisi birlikte kurtar. Sonra suçu arkadaşına at.*

A 3–6 player online social-sabotage comedy for Windows with **built-in voice**. Employees share an office quota;
each has a secret personal objective and two secret sabotage cards, and one of them is secretly the **HR Informant
(İK Muhbiri)**. Everyone can work, sabotage, plant and move evidence, sneak, phone colleagues privately to talk them
into things, tip off the manager anonymously, and dodge raids by one of four manager personalities. The day ends in
an HR meeting (accusations, CCTV footage, defenses, votes) that decides who gets fired — then the truth is revealed,
including a security-camera replay of the decisive moment. **İş Haftası** chains five days with career points and an
"Ayın Elemanı" finale.

v2 overview: `docs/V2_PLAN.md` · rules: `docs/CONTRACTS_V2.md` · balance: `docs/BALANCE_V2.md` ·
status log: `docs/V2_STATUS.md` · performance: `docs/PERFORMANCE.md`.

Design source: `Birimiz_Kovulacak_Oyun_Akisi.pdf`. Implementation decisions: `docs/DECISIONS.md`.

## Play (testers)

1. Unzip `BirimizKovulacak-Windows-Playtest.zip`, run `BirimizKovulacak.exe`.
2. Settings → Microphone: pick your mic (push-to-talk **V** by default). Voice is in-game — no Discord.
3. One player: **Lobi Kur** (Quick Play or Work Week). Others: enter the host's IP + port 7777 → **Lobiye Katıl** →
   **Hazırım**. 3–6 players; the host can add bots to empty seats.

Full Turkish guide (internet play via Tailscale/port forwarding, reconnects, controls, troubleshooting):
`docs/HOSTING.md`.

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

# Regenerate Unity content (materials, prefabs, Office scene) and build the Windows x64 player
powershell -ExecutionPolicy Bypass -File Tools\build.ps1            # add -Art to regenerate Blender assets first

# N local windows (host + clients, 3..6) driven by test bots, then verify identical results
powershell -ExecutionPolicy Bypass -File Tools\run_local4.ps1 -Bots -DurationScale 0.5 -Matches 1 -Players 6 -Week -VoiceTest speech

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
