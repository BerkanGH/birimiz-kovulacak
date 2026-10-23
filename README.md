# Birimiz Kovulacak

*Ofisi birlikte kurtar. Sonra suçu arkadaşına at.*

A four-player online social-sabotage comedy for Windows. Four employees share an office quota; each has a secret
personal objective, and one of them is secretly the **HR Informant (İK Muhbiri)**. Everyone can work, sabotage,
plant and move evidence, and dodge the manager's raids. The day ends in an HR meeting where accusations, defenses,
votes, manager observations and performance decide who gets fired — then the truth is revealed.

Design source: `Birimiz_Kovulacak_Oyun_Akisi.pdf`. Implementation decisions: `docs/DECISIONS.md`.

## Play (testers)

1. Unzip `BirimizKovulacak-Windows-Playtest.zip`, run `BirimizKovulacak.exe`.
2. One player: **Lobi Kur**. Others: enter the host's IP + port 7777 → **Lobiye Katıl** → **Hazırım**.
3. Host starts when four players are ready. Use Discord for voice.

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

# Four local windows (host + 3 clients) driven by test bots, then verify identical results
powershell -ExecutionPolicy Bypass -File Tools\run_local4.ps1 -Bots -DurationScale 0.5 -Matches 1

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

Rules engine and network message codec were delegated to a Codex agent (branch `codex/rules-engine`, worktree,
strict file ownership; see `docs/CODEX_PACKAGE.md` and `docs/CODEX_NOTES.md`). Unity integration, scenes, prefabs,
art, UI, networking adapter, bots, builds and verification were done by Claude. Status and evidence:
`docs/VERIFICATION.md`.
