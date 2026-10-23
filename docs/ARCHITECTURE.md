# Architecture

Birimiz Kovulacak is a host-authoritative listen-server game: one player's process (the host) runs the only copy of
the rules; every process — including the host's own UI — renders a per-player filtered view of that state.

```
 ┌──────────── every process ────────────┐                 ┌──────────────── host only ────────────────┐
 │ LocalPlayerController / BotBrain      │  ActionRequest  │ NetSession (NGO RPC)                       │
 │   focus + Affordances.For(view)  ──────┼────────────────▶│   → HostMatchController.OnClientRequest    │
 │                                       │   (WireCodec)   │       → MatchEngine.Submit(player, req)    │
 │ ClientState  ◀── PlayerView ──────────┼─────────────────│   per frame: UpdateAvatar → Tick(now)      │
 │   ├─ OfficeWorld (items, desks,       │  (own view only) │       → DrainEvents → route by audience    │
 │   │   stations, manager visual)       │  GameEvents      │       → BuildView(slot) for each client    │
 │   ├─ UIRoot (HUD, phone, meeting…)    │  ActionResult    │   lobby, approval, reconnect tokens,       │
 │   └─ PlayerAvatar animation           │                  │   rematch, host-side test bots             │
 │ PlayerAvatar movement (owner-auth.) ──┼── NetworkTransform ─▶ positions sampled into the engine        │
 └───────────────────────────────────────┘                 └────────────────────────────────────────────┘
```

## Modules

| Path | Owner | Responsibility |
|---|---|---|
| `Game/Assets/Game/Core/Contracts` | Claude | Shared types: ids, enums, `MatchConfig`, `OfficeLayout`/`DefaultOffice`, requests/results, views, events, results. Pure C# 9 / netstandard2.1, no UnityEngine. |
| `Game/Assets/Game/Core/Engine` | Codex | `MatchEngine` (phases, tasks, sabotage, incidents, manager, evidence, Informant, meeting, verdict, objectives, reconnect), `Affordances`, `WireCodec`. Deterministic (own SplitMix64 PRNG), host clock passed in. |
| `Game/Assets/Game/Tests/Core` | Codex | 118 NUnit tests; run by `dotnet test` (Tools/CoreTests) and Unity EditMode. |
| `Game/Assets/Game/Scripts/Net` | Claude | NGO adapter: `ConnectionManager` (host/join, approval, reconnect loop, crash-safe rejoin), `NetSession` (RPC transport of WireCodec payloads), `HostMatchController` (slots, engine, routing, rematch), `ClientState` (local view store), `UnityWorldQueries` (line of sight via wall colliders). |
| `.../Scripts/World` | Claude | `OfficeWorld` maps entity ids to scene anchors and places pooled item visuals from the view; desk/station/manager visuals. |
| `.../Scripts/Player` | Claude | `PlayerAvatar` (movement, appearance, animation from public activity), `CameraRig`, `LocalPlayerController` (focus, prompts, E/F/Q/R), `GameInput` (remappable bindings). |
| `.../Scripts/UI` | Claude | UI Toolkit screens built in code (`Game.uss`), `Loc` + `StreamingAssets/Localization/tr.json`, `Describe` (records → Turkish sentences). |
| `.../Scripts/Bots` | Claude | `BotBrain`: automated test player using only its own view and ordinary requests (network bots via `-bot`, host seat-fillers via lobby). |
| `Game/Assets/Game/Editor` | Claude | `ProjectBuilder`: regenerates URP, materials, imports, prefabs, the Office scene (from `DefaultOffice`), NavMesh, and builds the player. |
| `Art/` | Claude | Blender generator scripts, `.blend` sources, FBX exports, `palette.json`, previews. |
| `Tools/` | Claude | `build.ps1`, `run_local4.ps1`, `package.ps1`, verification scripts, .NET test harness. |

## Authority, ownership and concurrency

- The host validates every request (phase, match id, duplicate request id, proximity with latency slack, carried
  item, permissions, cooldowns, charges) and completes timed interactions on its own clock. Clients never declare
  completion, score or success.
- Two players cannot hold the same item: `Submit` runs sequentially on the host; the first pick-up wins, timed
  pick-ups reserve the item, and the second request gets `ItemUnavailable`/`TargetBusy`.
- Quota/performance are awarded once at completion after re-validation; duplicate request ids are ignored.
- Movement is owner-authoritative (`OwnerNetworkTransform`) for responsiveness; the host samples avatar positions
  for proximity, witnesses and the manager. Friends-only prototype: a modified client could move fast; it cannot
  fake rule outcomes.
- Carried items drop at the carrier's position on disconnect and when the meeting starts; interactions are
  cancelled at the meeting boundary.

## Information visibility

`MatchEngine.BuildView(recipient)` builds each view from scratch: public state + that recipient's private state.
Roles, objectives, notes, scan results, closed-container contents (unless known to the recipient), item provenance,
incident actors and true misconduct are never put into another player's view. Public activities are deliberately
coarse (`Typing` covers report/lock/unlock). The host's UI reads the same filtered view through `ClientState`; the
host process itself is trusted (it holds the full state) — no protection against a modified host is claimed.

## Time, phases, rematch

Host time (`NetworkManager.ServerTime`) drives the engine. Phase end times are absolute, so clients show the same
countdown. Durations live in `StreamingAssets/Config/match_config.json`. After Results, a rematch starts when every
connected player votes (or the host forces it): a new `MatchEngine` with a new match id and the session history
(Informant rotation, objective target spreading).

## Connection lifecycle

Create Lobby → host listens on UDP 7777 (configurable) → clients join by IP. Connection approval checks protocol
version + build id, lobby capacity, and — mid-match — the reconnect token. Exactly four players are required to
start (test bots can fill seats). On a drop, a client retries with its token for the 30 s grace period and gets the
same slot, role, objective, notes and charges back; the token is also kept in PlayerPrefs for ~75 s so a crashed
client can use "Maça geri dön". Grace expiry or host loss ends the match as **Abandoned** (no winners).

## Determinism & testing

Same config + layout + setup + call sequence ⇒ same events and result hash. Every client logs the `ResultHash` it
received; `Tools/scripts/verify_logs.py` checks all processes agree. See `docs/VERIFICATION.md`.

## Extending

- Balance: edit `match_config.json` next to the executable (`BirimizKovulacak_Data/StreamingAssets/Config`) — only
  the host's file matters; it is sent to clients on join.
- Text: `StreamingAssets/Localization/tr.json` (add a language by adding `<lang>.json` and calling `Loc.Load`).
- Layout: `DefaultOffice.Create()` + `ProjectBuilder.Scene.cs`, then `Tools/build.ps1`.
- New objective/sabotage: add the enum + config entry (contract change, bump `Protocol.Version`), implement in the
  engine, add tests, add localization keys.
