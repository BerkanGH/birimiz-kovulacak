# Codex rules engine integration notes

Implemented against `CODEX_PACKAGE.md`, normative `CONTRACTS.md`, `DECISIONS.md`, and the supplied contract types on `codex/rules-engine`. Contract files, Unity files outside the owned directories, assembly definitions, and `.meta` files were not changed by this work. The worktree already contained line-ending modifications in those files when the assignment began; they were preserved.

## Implemented

- SplitMix64 PRNG with specified wrapping arithmetic, unbiased integer draws, deterministic shuffles, and instance-owned state.
- Absolute host-clock phase machine, request validation and last-64 request deduplication, timed interactions, reservations, movement cancellation, completion revalidation, and cleanup at meeting/disconnect/abandonment.
- Item slots, nameplate holder swaps, closed-container knowledge, provenance, floor drops, all four demand-driven tasks, reusable mugs, serialized printer queue, and exactly-once quota/performance awards.
- All six sabotage types, evidence tampering, per-actor and same-victim cooldowns, per-desk/type limits, workstation immunity, immutable true actors, bounded consequences, and reversal credit.
- Manager warning, route travel, desk dwell, cone/LOS checks, sustained-movement observation, public observations/reaction bubble, fallible nameplate attribution, private witnesses, evidence movement/disposal/sealing, observed risk, and final physical dossier freeze.
- Informant item/evidence scans, saved placement attribution, reliability and ordered inconsistency signals, private scan notes, and charge-limited seals that lock evidence items at their recorded location.
- One Informant, distinct ordinary employee cards, all twelve objective predicates/progress, forced test assignments, target balancing, and rematch history.
- Accusations, note presentation, nomination fallbacks, defense speakers, private ballots, hybrid verdict/tie-break explanations, post-verdict false-accusation penalties, company/survival gates, Informant result, titles, chronological recap, and common result hash.
- Reconnection grace preserving the existing player state, timeout/host abandonment, rematch votes, filtered/deep-copied recipient snapshots, and detached host debug views.
- View-only contextual affordances and complete explicit binary serialization of all seven network messages and nested result types. No runtime reflection, Unity dependencies, threads, wall clock, mutable static engine state, or `System.Random` in the core.

The implementation is divided into `MatchEngine` partial files by subsystem. All internal state types remain internal. `Internal/Snapshot.cs` contains explicit deep-copy overloads, so modifying returned views cannot change authoritative state.

## Interpretations and deviations

These resolve ambiguity without changing contract types:

1. **Timeline boundaries:** phase transitions win over interactions ending exactly when the meeting begins. Office-to-office boundaries preserve interactions. `Tick` processes intermediate deadlines during a large time jump and drains zero-length phases at the same timestamp. Backward/nonfinite host times do not rewind the engine.
2. **Demand:** initial orders/dirt are visible during Briefing; recurring task timers start at Work1. Nonpositive recurring order/dirt intervals disable those generators. A printer that finishes while its tray is full retains its completed job until space opens. Answering reserves the current call beyond its ring deadline.
3. **Limits:** type-specific item caps return `LimitReached`; per-desk combined caps return `TargetImmune`. A report hidden at a shared spot has no physical desk, so it counts toward its actor's active limit and report-owner cooldown, but no physical desk's cap. A report hidden in a drawer counts against that drawer's desk. Existing effects are checked before replacement, including the nameplate type cap.
4. **Suspicious placement:** all suspicious items placed into drawers use the PlantItem duration, including placement into one's own desk; own-desk placement creates no incident. Nameplate restoration credits only the currently open swap, never an obsolete swap replaced earlier.
5. **Discovery:** at a desk stop, the manager identifies a hidden report using the public printed owner versus that desk's assigned owner. A victim's own report hidden in their own drawer is therefore found through player search/recovery rather than a provenance-based manager accusation. The manager never consults incident actors to decide what it sees. WorkingHard recognizes public work activities (including Typing), so it cannot distinguish report writing from lock/unlock typing.
6. **Recovery:** newly created recovery evidence immediately has status Moved because recovery removes its subject from the recorded container. Recovery does not create an additional tampering incident for evidence that did not exist before that pickup. Undelivered-report consequences are finalized only after FinalInspection, not on early abandonment.
7. **Attribution:** an evidence record retains its discovery attribution; a nameplate change affects subsequent discoveries/observations. Seals preserve attribution and Active status, and lock the item only when it is still at that record's location. Seal ownership never appears in public views/events.
8. **Manager timing:** continuous vision checks use scheduled samples at most 0.1 seconds apart, with additional checks on host ticks/deadlines. Reaction bubbles overlay the current walking/inspection state rather than pausing the route. An inspection duration that is too short for the configured route ends the route at the phase boundary; the engine does not extend the match.
9. **Voting:** explicit abstentions count in the submitted-ballot denominator; missing ballots contribute no vote. This follows the stated `votes / ballots cast` formula. Exact score/risk/performance ties use the seeded generator, with no epsilon or hidden-misconduct input.
10. **History/test overrides:** normal employee assignments use distinct enabled cards. Explicit forced card assignments may duplicate/override enabled state for tests. History appends actual holder-to-target pairs; the latest three pairs include every pair from the preceding four-player match and may also discourage an older pair when that match had fewer targeted cards.
11. **Titles:** the spec names title keys but no exact thresholds. Priority, maximum two: winning Informant; tied positive coffee leader; top positive misconduct; report-hiding/PC-locking actor; framed player; physically clean assigned desk.
12. **Presentation limits:** performance-rival objective integer progress truncates fractional points; exact performance remains in public player fields. Presented search evidence stores the comma-joined variant list in `ItemVariant`, because EvidenceView has no contents-list field. Scan statements carry only the scan's public apparent owner and factual result.
13. **Affordances:** hidden provenance, same-victim cooldown history, some reservations, and active incident counts cannot be inferred from PlayerView. Those checks remain authoritative in Submit. An early unlock's immunity deadline is not exposed in DeskView. Carrier avatar positions are also absent, so carried-item scan distance cannot always be hinted; the host checks its actual avatar samples. Evidence disposal is conservatively marked as sabotage even when the same carry already incurred tampering, because that deduplication flag is host-only.

## Known gaps and adapter responsibilities

- **Reconnect token echo:** the supplied API has no way to pass the adapter-issued token into MatchEngine. `PrivateView.ReconnectToken` stays empty. The adapter already owns connection approval and `WelcomeMessage`; it must populate this field on its detached recipient snapshot if it needs the echo. Reconnect state retention and the configurable grace timer are implemented and tested.
- Unity scene/animation/UI/network transport integration was not run here. These are Claude/adapter-owned. The pure core and shared NUnit tests run through the requested .NET harness.
- Config/layout are host-owned inputs. The constructor rejects missing/duplicate player slots and insufficient desks; it does not validate every malformed host balance/layout combination. Bad client requests are handled without exceptions.

## Contract change requests

No contracts were edited. Suggested additions for a future protocol version:

```csharp
// PlayerSlotInfo: allows the engine to echo the already-issued adapter token.
public string ReconnectToken = "";

// DeskView: allows exact immunity prompts after an early unlock or natural expiry.
public double PcImmuneUntil;

// EvidenceView: avoids overloading ItemVariant for presented search statements.
public List<string> Contents = new List<string>();
```

The adapter can work around all three with the existing API as described above; none blocks match simulation. `Protocol.Version` remains 1.

## Wire format

Each message starts with tag byte plus little-endian `ushort Protocol.Version`: view=1, request=2, action-result=3, events=4, lobby=5, connection=6, welcome=7. The hash-only match-result encoding uses tag=8. Fields follow explicit contract declaration order, enums occupy one byte, objects use a presence byte, numeric types retain their full binary representation, and UTF-8 strings/lists use canonical base-128 unsigned lengths (`0` means null, otherwise length+1). The decoder rejects wrong tag/version, truncation, invalid booleans/UTF-8/lengths, and trailing bytes with `FormatException`. ResultHash is lowercase 16-character FNV-1a 64 over result bytes with the hash field blank, without mutating the source result.

## Tests

Command: `dotnet test` from `Tools/CoreTests/Tests`.

The tests cover all eighteen requested categories and the named `RubberDuckScenario`, plus regression cases for privacy during container pickup, completion races, bounded consequences, all objective predicates and failure conditions, hidden-truth-independent manager output, snapshot mutation isolation, invalid client input, and malformed binary data. The scripted four-player match exercises all four task types, all six sabotage types, evidence tampering, scans, meeting/voting, results, and deterministic replay.

Validation: **118 tests passed, 0 failed, 0 skipped**, with a warning-free core/test build. The .NET harness compiles the core as C# 9 / netstandard2.1 and tests as net8.0. NUnit assertions remain compatible with Unity's classic NUnit 3 API.

`CorePerformanceTests` warms up and measures 5,000 iterations, reporting averages without flaky timing assertions. On this runner: Work1 Tick **0.44 µs**, four-recipient BuildView batch **24.19 µs**; populated Results Tick **0.10 µs**, four-recipient BuildView batch **48.69 µs**. These are harness measurements, not a Unity-player profiling claim.

## Commit/integration status

The first requested stage built successfully. Its explicit owned-file `git add` and commit failed because Windows denied creation of `.git/worktrees/codex-rules/index.lock`. The directory ACL contains explicit deny entries for sandbox identities, despite the session listing the repository Git directory as writable. No permission escalation or ACL modification was attempted. The user was informed while implementation continued. No stage commits could be created while that access remained blocked; all source/test changes are retained in this worktree.

Requested commit grouping once Git access is restored (stage only these owned paths):

1. `MatchEngine.cs`, `MatchEngine.Views.cs`, `Internal/DeterministicRandom.cs`, `Internal/MatchState.cs`, `Internal/Snapshot.cs`: deterministic state/phase foundation.
2. `MatchEngine.Interactions.cs`, `MatchEngine.Tasks.cs`: interactions/items/tasks.
3. `MatchEngine.Sabotage.cs`: incidents and anti-grief.
4. `MatchEngine.Manager.cs`: manager/evidence/witnesses.
5. `MatchEngine.Informant.cs`: scans/seals.
6. `MatchEngine.Meeting.cs`, `MatchEngine.Objectives.cs`: assignment/objectives/verdict/results.
7. `MatchEngine.Reconnect.cs`: reconnect expiry/abandonment integration.
8. `Affordances.cs`, `WireCodec.cs`: view-only prompts and binary protocol.
9. New `Game/Assets/Game/Tests/Core/*.cs` test files only (exclude existing smoke test and asmdef): contract/regression tests.
10. `docs/CODEX_NOTES.md`: implementation/integration notes.

Do not stage the supplied unrelated line-ending changes. Each implementation grouping is designed to compile using optional partial subsystem hooks until subsequent groups are integrated; full match execution requires all groups.
