# Codex brief — v4 rules engine (gpt-6.1-sol, max)

You are the engine owner for "Birimiz Kovulacak" v4 (Unity 6 game, pure C# rules core). Work ONLY inside this git
worktree. Do not commit (the lead commits for you; Windows denies index.lock in worktrees). Do not run Unity.

## Objective
Replace the v3 engine with the **v4 "Hybrid Office" engine** exactly as specified by:
1. `docs/GAMEPLAY_REDESIGN.md` — normative rules and numbers (read completely).
2. `docs/CONTRACTS_V4.md` — engine/wire contract: flow, scheduling, per-task fact/control keys, records/offers/incidents,
   privacy, host-only semantic events and category codes, codec, bot, tests (read completely).
3. `Game/Assets/Game/Core/Line/Contracts/*.cs` — the v4 types (lead-owned; **do not edit**).
4. `Tools/stubs/LineEngineStubs.cs` — the exact public API Unity scripts call. Your engine must provide every member
   with identical signatures (`LineEngineFactory`, `LineCodec` Encode/Decode pairs incl. `EncodeMessages`,
   `LineBotPolicy`, `LineBot`, `LineSchedule`, `LineText`). Internal helpers are yours.

Reference for style and quality: the v3 engine you (Codex) wrote earlier — `git show v3:Game/Assets/Game/Core/Line/Engine/`
files and `docs/ENGINE_V3.md` (determinism, SplitMix64 `LineRandom`, partial classes, allow-list views, codec
completeness tests, privacy byte-scans, zero-allocation idle ticks). Keep that bar.

## Owned files (only these)
- `Game/Assets/Game/Core/Line/Engine/**` (rewrite; keep `LineRandom.cs` if useful)
- `Game/Assets/Game/Tests/Line/**` (replace v3 tests with v4 tests; `LineTest.cs` helpers may be reused)
- `Tools/LineSim/**` (v4 simulator)
- `docs/ENGINE_V4.md` (new: implementation notes, decisions, test counts, measured view sizes, sim results)

## Requirements (summary — the documents above are authoritative)
- Deterministic, pure C# (netstandard2.1, C# 9), one seeded PRNG, no UnityEngine/System.Random/Guid/DateTime.
- 12 tasks with groups/roles, facts and controls exactly as `CONTRACTS_V4.md §4` (keys!), resolution/output/income/
  quality/cooperation per `GAMEPLAY_REDESIGN.md §6`, sabotage incidents + proof records.
- Block flow with `Break` phases and the closing break; early-submit `CarryToBreak`; phase-end reasons on `PhaseChanged.B`.
- Productivity (provisional/final per mille, measuring = -1), company closure path (skips defense/vote).
- Money (block filter on productive income), transfers, evidence offers/purchases (escalating price, limits, no double
  charge), record access sets, presentation + idempotent exposure, remedial work with caps and expiry.
- Secret objectives (9 kinds, distinct, Rival/Guardian rules) with progress; career lines, ranks (1,1,3), EotM (ties →
  none), closure (no awards, no rank), session history.
- Daily warning ballot with No action (`LineIds.Nobody`), `WarningMinVotes`, cap → `Capped`; final defense for every
  present player; final vote with roster snapshot and strict majority; Fire nobody.
- Messages: texts, email (threads/replies, templates optional), meeting chat, anonymous complaints (dossier only),
  system/manager notices, fake email trick (free text allowed); per-recipient `Seq`; `MessagesFor`; sanitising and limits
  via public `LineText.Sanitize`; never put text into events.
- Dirty tricks (5 types; KayitSil is gone); dossier shows observable effects only; no TipOff, no trace naming, no
  complaint leaks, no intern.
- Recording selection `Legacy` and `Context` (design §13), `HostRecordingPick`, exposure decay across the match,
  `LineRecordingPick.Context`.
- Host-only events (`LineEventType` 40–48) with the category codes of `CONTRACTS_V4.md §8`.
- `LineSchedule.Validate` / `PredictMeetingStarts` (Short 4-player prediction inside the documented windows).
- Codec v4 for every type incl. `LineMessage` lists and the consent fields; completeness + round-trip tests.
- `LineBot` policies `Talk`, `NoComm`, `Greedy`; never sends an action its view says will fail (Busy discovery excepted).
- `Tools/LineSim`: run N matches per head count/length/policy mix (all-Talk, all-NoComm, mixed with one Greedy),
  report: final productivity distribution, closure rate, output per task, career component averages, EotM award rate,
  objective completion per kind, incidents created/exposed, warnings/no-action rates, firing/no-majority rates, early
  submit carry. Write the run's output to `Tools/LineSim/v4-sim.txt` and summarize in `docs/ENGINE_V4.md`, clearly
  labelled **synthetic bot data** (bots do not talk; the Talk policy models perfect information sharing between call
  partners).

## Validation (run these yourself)
- `dotnet build Tools/CoreTests/Core/Core.csproj` (0 errors).
- `dotnet test Tools/CoreTests/Tests/CoreTests.csproj` (all green; report counts).
- `dotnet run -c Release --project Tools/LineSim/LineSim.csproj -- <your args>` (document the exact command).
- Do NOT expect `Tools/scripts/compile_check.py` (Unity assemblies) to pass: the UI is being rewritten in parallel.

## Handoff (in `docs/ENGINE_V4.md`)
Actual changes, design decisions where the docs were ambiguous (choose, implement, document), tests run with counts,
measured 6-player view size, determinism evidence, sim results (synthetic), limitations, and a **Contract requests**
section (empty if none). If a contract type truly blocks you, implement the closest workaround and describe the
requested change there — do not edit the contract files.
