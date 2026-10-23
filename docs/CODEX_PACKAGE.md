# Codex Work Package — Authoritative Rules Engine

You (Codex) own the authoritative, Unity-independent rules engine for **Birimiz Kovulacak**, a 4-player social
sabotage office game. Claude owns everything Unity-side (scenes, prefabs, NGO networking adapter, UI, art) and
integrates your work.

Read first: `docs/DECISIONS.md` (design), `docs/CONTRACTS.md` (semantics you must implement — normative),
`Game/Assets/Game/Core/Contracts/*.cs` (types). The PDF `Birimiz_Kovulacak_Oyun_Akisi.pdf` is the original concept.

## File ownership (strict)

You may create/modify **only**:
- `Game/Assets/Game/Core/Engine/**` — replace the three stubs (`MatchEngine.cs`, `Affordances.cs`, `WireCodec.cs`)
  and add any internal files you need (e.g. `Engine/Internal/*.cs`). Keep internals `internal`.
- `Game/Assets/Game/Tests/Core/**` — NUnit tests (keep `BirimizKovulacak.Core.Tests.asmdef` as is).
- `Tools/CoreTests/**` — the .NET harness (only if needed).
- `docs/CODEX_NOTES.md` — your implementation notes, deviations, open questions.

Do **not** touch: `Game/Assets/Game/Core/Contracts/**` (contract types), anything else under `Game/`, `Art/`,
`ProjectSettings`, `Packages`, `.meta` files (do not create `.meta` files — Unity generates them).
If you believe a contract type must change, do not edit it: write the proposed change (exact C#) in
`docs/CODEX_NOTES.md` under "Contract change requests" and work around it.

## Technical constraints

- C# 9, netstandard2.1, `noEngineReferences`: no UnityEngine, no `System.Random` (implement a small deterministic
  PRNG, e.g. SplitMix64/xorshift, seeded from `MatchSetup.Seed`), no threads, no wall-clock, no static mutable state,
  no `dynamic`, no records/init accessors/file-scoped namespaces (Unity compiles C# 9 without IsExternalInit).
  LINQ is fine outside per-tick hot loops.
- Deterministic for identical inputs. Iterate collections in stable order (lists / sorted keys), never over
  `Dictionary` enumeration order when it affects outcomes.
- `Submit` never throws for client input; it returns `ActionResult.Fail(code)`.
- `BuildView` must not leak host-only data (§3 of CONTRACTS). Build each view from scratch for the recipient.
- Performance: 4 players, ~30 items; `Tick` at 30–60 Hz and `BuildView` ×4 at ≤10 Hz must be well under 1 ms each.

## Deliverables

1. `MatchEngine` — full implementation of CONTRACTS §2–§12: phase machine with configurable durations, demand
   generation (coffee/report orders, phone rings, desk dirt), interactions with reservations and host-clock
   completion, items/slots/containers with `knownBy`, printer queue, the six sabotage types + evidence tampering,
   anti-grief limits, incident ledger + misconduct with bounded consequences, reversal credit, witness notes,
   manager NPC (kinematic route walk, vision cone with `IWorldQueries.HasLineOfSight`, desk inspection, observations,
   reaction bubble), evidence lifecycle + final freeze, observed risk + suspicion meter, Informant scans and seal,
   meeting (accusations, shortlist + fallbacks, speakers, ballots), hybrid verdict with explanation and tie-breaks,
   results (company gate, objectives with progress, Informant outcome, false-accusation penalty after the verdict,
   titles, recap, result hash), role/objective assignment with session history, reconnect grace (30 s) and
   abandonment, `BuildHostDebugView`, `BuildNextHistory`, `AllRematchVotesIn`, `Abandon`.
2. `Affordances.For` / `TargetIds` — prompt options computed only from the recipient's `PlayerView` (+ config and
   layout). Use label keys `verb.<snake_case_verb>` (e.g. `verb.brew_coffee`, `verb.place_drawer`,
   `verb.place_surface`, `verb.place_nameplate`, `verb.hide`, `verb.deliver`, `verb.take`, `verb.search`,
   `verb.lock_pc`, `verb.unlock_pc`, `verb.write_report`, `verb.answer_phone`, `verb.clean_desk`,
   `verb.clean_spill`, `verb.spill_coffee`, `verb.throw_away`, `verb.return_item`). Mark `IsSabotage` for options
   that would create an incident. Include blocked-but-relevant options with `BlockedBy` set (e.g. locked PC,
   no order to deliver, cooldown).
3. `WireCodec` — compact binary codec for every contract message (tag byte + `Protocol.Version` ushort header),
   lossless round-trips, `FormatException` on mismatch/truncation; `HashResult` = stable hex hash (e.g. FNV-1a 64)
   of the encoded result with `ResultHash` blanked.
4. Tests (NUnit 3, classic `Assert.*` / `Assert.That` only — they also run inside Unity's NUnit 3.5) in
   `Game/Assets/Game/Tests/Core/`. Create a reusable `TestMatch` helper (fixed seed, forced roles/objectives,
   helpers to teleport avatars via `UpdateAvatar`, advance time, and run requests to completion).

### Required automated tests (minimum)

Rules:
1. Exactly one Informant is assigned, for many seeds; Informant does not repeat across a rematch when avoidable.
2. Objectives: three distinct cards for the employees; targets never self; history reduces repeated holder→target pairs.
3. Private information reaches only eligible recipients: role/objective/notes/scans of player X never appear in
   another player's `BuildView`; items in closed containers are hidden from non-knowers; witness notes only to witnesses;
   a lock's actor never appears in public view/events. (Encode views and scan the bytes for leaked ids/strings too.)
4. Two simultaneous pickup requests for the same item in the same tick: exactly one succeeds.
5. A task contributes to quota exactly once (duplicate request ids, delivering twice, completion re-validation).
6. Framing never alters the true perpetrator: the rubber-duck scenario below.
7. Sealed evidence cannot be rewritten: pick-up of the sealed item fails, nameplate swap after sealing does not change
   attribution, junk-bin disposal impossible, status stays Active through the final freeze.
8. Informant charges cannot be exceeded (third scan, second seal fail with `NoCharges`); non-Informants get `NotAllowed`.
9. Phase transitions reject stale actions: wrong `MatchId`, office verbs during the meeting, ballots outside the vote,
   an interaction started in Work2 is cancelled at the meeting start and never completes.
10. Voting, ties and no-vote outcomes resolve deterministically (no ballots, 1–1 split, all abstain, single nominee,
    no nominations, exact score ties → risk → performance → seeded draw).
11. Company failure invalidates every personal and Informant victory (exactly one player is still fired).
12. Reconnect within grace preserves role, objective, notes, charges, history; grace expiry ⇒ Abandoned, no winners.
13. All clients receive the same verdict and results (identical `ResultHash` across recipients' views).
14. Anti-grief limits (cooldowns, same-target windows, per-desk caps, workstation immunity).
15. Manager: observes only inside its cone/LOS and only during inspections; attributes by nameplate; never reads truth.
16. Codec round-trip for fully populated messages; truncated / wrong-version input throws `FormatException`.
17. Determinism: same seed + same script ⇒ identical event sequence and result hash.
18. A full scripted 4-player match from Briefing to Results with every task type and every sabotage type.

Rubber-duck integration scenario (must be a named test, e.g. `RubberDuckScenario`):
1. Pembe (P2) takes the duck from lost & found and plants it in Mavi's (P0) drawer (desk.0).
2. Sarı (P1) swaps the nameplates of desk.0 and desk.1 (desk.0 now shows Sarı… or the swapped name).
3. The manager discovers the duck during the raid; evidence is attributed to the player shown by desk.0's nameplate.
4. The Informant scans it: reliability Low (or Medium if the apparent owner handled it) and signal
   `NameplateChangedAfterPlacement` — no player id other than the public apparent owner in the result.
5. Accusations/votes proceed on the public record.
6. In Results, incidents show P2 as actor of PlantItem and P1 as actor of NameplateSwap; P0's true misconduct is 0.

## How to build and test

```
cd Tools/CoreTests/Tests
dotnet test            # builds Core as netstandard2.1 (C# 9) and runs Game/Assets/Game/Tests/Core/**
```
Packages are already restored. Keep the build warning-free where practical. Commit to your branch in small,
compiling steps with clear messages (`git add` only files you own). When finished, write `docs/CODEX_NOTES.md`
with: what is implemented, known gaps, any deviations from CONTRACTS.md and why, and test summary.

Integration is incremental: Claude will merge your branch while you work. Keep `main`-compatible public signatures.
