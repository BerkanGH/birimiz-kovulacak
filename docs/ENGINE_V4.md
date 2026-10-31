# v4 Hybrid Office rules engine

The `v4.0-hybrid` rules core replaces the v3 Line engine. All implementation changes are in
`Game/Assets/Game/Core/Line/Engine`, the replacement NUnit suites, `Tools/LineSim`, and this document.
Lead-owned contracts and Unity scripts were left unchanged. No Unity process or compile-check script was run;
no commit was made.

## Implementation

`LineEngine` retains the v3 partial-class structure. The main file owns validation, secrets, scheduling,
presence, deadlines and request deduplication. `Rounds` deals the twelve tasks and validates field-masked
controls; `Resolution` implements their output, contribution, income, defaults and public facts.
`Office` owns calls, messages and the five tricks; `Evidence` owns money, access sets, purchases,
presentations, incidents and remedials; `Meeting` owns recordings, ballots, objectives, career and history.
`Views` builds the recipient allow-list. `LineSchedule`, `LineText`, the factory, codec and bot provide
all signatures from `Tools/stubs/LineEngineStubs.cs`.

The implementation includes pair/trio groups, role balancing, unique weighted task schedules, onboarding
intros, inter-task and closing breaks, early-submit carry, provisional/final productivity, closure,
daily warnings, defense for all present seats, roster-based majority voting, Fire nobody, all nine objectives,
career components, competition ranks, strict EotM ties, and session history. Each explicit sabotage
creates one stable incident and authenticated proof. Presentation exposes once and never applies a second
productivity loss. Records bought for an interval include later records created in that interval.

Requests consume a monotonic per-seat id even on rejection; reconnects preserve that counter. Transfers
conserve balances. Productive income applies the running raw-income filter before crediting each payment;
deal income is separate. Rejected or repeated purchases never charge. Remedials expire at their deadline
or the end of the office phase set and are bounded by task, block and match caps.

Free text travels through `MessagesFor`, outside views and events, except the explicitly allowed
anonymous complaint text in a dossier. Per-recipient sequences start at one; broadcasts share a global id.
Email replies retain the root thread. Templates, free text, meeting chat, preset/free complaints, manager
memos, IT notices and spoofed email are supported. Sanitisation normalises NFC, strips controls, replaces
angle brackets, handles email newlines, and validates the sanitised UTF-16 length. Message history is bounded
by the configured retention count.

The codec has explicit field-order readers/writers for all **43 serialisable contract classes**. There is
no reflection in the wire codec. It uses magic `B3`, little-endian protocol 4, strict presence/boolean/enum
bytes, canonical LEB128 length-plus-one, strict UTF-8, a 2,048-byte string limit, a 4,096-entry list limit,
and a 1 MiB envelope limit. Unknown enum holes are rejected; valid `TaskField` flag combinations are accepted.
Connection consent fields, setup/config/history and message lists all round-trip.

## Determinism, privacy and decisions

All engine randomness uses one seeded SplitMix64 stream. Seats are traversed in id order; sets are used
only for membership/counts, never random iteration. Absolute deadlines handle large ticks identically to
small ticks. Returned setup, config, views, results, messages, recording picks and history are detached.
`MatchId` is correlation data and does not influence rules or `ResultHash`. The result hash covers every
outcome field except itself and MatchId, using the explicit binary schema and FNV-1a.

The privacy allow-list continues through results: each client's result copy hides other balances and
objective kinds/targets, while completion, career components, contributions and exposed incidents remain
public. `ILineEngine.Result` is the full host-side result. Recording candidate ids remain host-only.
Complaint authors are never stored in the client complaint type. Fake mail has the apparent sender only;
the IT proof is the sole authenticated attribution. A copied HR file is accessible only to its copier
until that holder presents it; buying a badge interval does not grant the HR file's contents.

Decisions where the prose needed an implementation choice:

- Task percentages, group means, quality and grants use integer division; vendor output rounds to nearest.
  Normalised task output is scaled by its configured `MaxOutput`. Missing archive request confirmations
  default to none; an empty visitor fetch defaults to an empty mask.
- The six customer specs form one public catalogue per task, shared by the pair/trio groups. Private
  attributes and commission choices remain recipient-specific. Exactly one catalogue entry matches the
  combined wishes; each decoy differs in one attribute.
- Role test hooks select the primary role; for phishing this is the fake-mail holder. The insider is a
  separate balanced, non-holder draw. Rival/Guardian use the configured same-target probability exactly:
  the common-target branch excludes both owners; the other branch makes their targets distinct.
- A player connected during intro or play counts as participating. Reconnecting only after resolution
  does not retroactively count. Defense order snapshots the connected seats at defense start; the final
  electorate snapshots connected seats separately at final-vote start. Cast final votes survive a disconnect.
- Cooperation opportunities exist for task rules that explicitly award a cooperation credit. Each player
  has at most one opportunity and credit per task. A remedial fills that task's missing credit, adding its
  opportunity if necessary, instead of generating an unlimited action count. EotM multiplies the quality
  sum before dividing, retaining precision in the comparison.
- TeamPlayer checks the member's group result for pair tasks and task output otherwise. Remedial output
  also improves that task's normalised objective progress, bounded at 100.
- Remedial caps count accepted work; declined/expired requests consume no work allowance. One pending
  request per task and one incoming request per co-signer keep the contract's single incoming/outgoing
  slots truthful. Busy can be discovered privately. Bots choose co-signers they observed participating.
- TaskEntry partner records omit invoice padding; the complete task-audit entry is separate. T4 figure,
  decision, audit figure and draft history are separate records to avoid granting a partner the true draft.
- Offline computers block task submission and email sending; incoming durable messages still arrive.
  Texts remain a phone alternative. Custom player-message limits are validated at at most 512 UTF-16
  units so every accepted string fits the UTF-8 wire bound.
- `PredictMeetingStarts` is a display forecast: reference PlayFactor 100, one recording per earlier meeting,
  rounded to ten seconds. Runtime phase deadlines are never rounded. Literal first-session Short/4p
  reference timings are 275/622/994 s, including the closing break; predictions are **280/620/990 s**,
  inside the required windows. This resolves the four-second discrepancy between the default config's
  last reference timing and the brief's 990 s window. Later short-intro forecasts are 220/540/890 s.
  Actual hub/trading PlayFactor 110 cards and recording availability can shift runtime starts.
- Disabled/ineligible onboarding tasks fall back to the eligible pool. Explicit forced tasks still reject
  repeats/ineligibility. Least historical usage takes precedence over the soft family preference.
- Legacy recording focus covers pair/role partners and special-role callers. Context uses group, decision
  and transfer factors; each previously played recording involving either caller contributes one decay
  factor across the entire match. A call containing both previously played partners is counted once for
  that prior playback. Only a reported chosen recording increments exposure; no-speech picks do not.
- HostContinue emits a terminal PhaseChanged with HostContinue while retaining the WeekResults phase,
  since the contract has no separate Finished phase.

## Validation

Run from this worktree:

```text
dotnet build Tools/CoreTests/Core/Core.csproj
dotnet test Tools/CoreTests/Tests/CoreTests.csproj
dotnet run -c Release --project Tools/LineSim/LineSim.csproj -- --matches 20 --players all --length both --mix all --seed 1927 --step 2 --session-matches 10 --output Tools/LineSim/v4-sim.txt
```

Final core build: **0 warnings, 0 errors**, targeting netstandard2.1 / C# 9.
Final NUnit run: **417 passed, 0 failed, 0 skipped**. NuGet emitted NU1900 because the sandbox could not
reach the package vulnerability service; cached packages restored successfully and the tests completed.

Coverage includes every task and raw income at all four head counts, pair/trio payoffs, defaults and
missing-input exemptions, sabotage proof sources, atomic/locked/waiting controls, all 32 scheduling presets,
no repeats, prediction windows, carries, productivity/freeze/unresolved zero, closure, objective success/failure,
career tables/rank ties/EotM eligibility, every warning outcome, final roster/disconnects, money deduplication,
purchase prices/limits/future access, idempotent exposure, remedial caps/expiry, messages/threads/limits/NFC,
all codec fields/envelopes/boundaries, private sentinel byte scans through every phase including messages,
and bot matches at every head count/length/policy.

Determinism tests compare complete encoded views, messages, actions, action results and events from two
identical scripted bot runs. Separate tests compare result hashes, MatchId independence, and large-tick vs
per-phase transitions. Idle-tick tests assert exactly zero allocation and no StateVersion change.

Measured six-player block-2 fixture view: **1,533 bytes**. The simulator also measures block-2 play views
with actual bot history and evidence; the maximum from the final run is recorded below. Measurements are
binary payload bytes, without transport framing.

## Synthetic bot data

**Synthetic bot data — bots do not talk.** The Talk policy models perfect information sharing between
call partners; the harness also treats accepted placeholder texts as a completed information exchange.
Shared views are supplied only for partners contacted in the current task, never to NoComm or Greedy.
Talk occasionally follows a conflicting commission/preference with a seeded probability; this models
misinformation mechanically, without generating or analysing dialogue. Greedy takes offers and bounded
sabotage; the mixed cohort has one Greedy seat and the rest Talk. NoComm guesses missing facts, requests
extra budget and trades at most two items per task. It never calls or texts and declines incoming calls.
Bots make modest purchases, transfers, presentations, remedials and preset-only complaints. Recording
playback in the simulator is metadata-only and assumes a candidate is playable; it simulates no speech.

The final matrix uses **20 matches per cell**, 3–6 players × Short/Full × all Talk/all NoComm/one Greedy:
**480 matches**, default unscaled timings, 2-second host input steps, seeds 1927–1946 in each cell,
history reset every ten matches. Engine deadlines still occur at their exact times between input steps.
The complete distributions, each task's output, career component averages, objective completion by kind,
incident/exposure counts, warning/no-action and firing/no-majority rates, EotM rates, carry totals, encoded
view maxima and deterministic hashes are in [v4-sim.txt](../Tools/LineSim/v4-sim.txt).

| Players | Length | Talk mean % / closure % | NoComm mean % / closure % | Mixed mean % / closure % |
|---|---|---:|---:|---:|
| 3 | Short | 95.58 / 0 | 54.41 / 75 | 83.24 / 0 |
| 3 | Full | 97.93 / 0 | 56.19 / 70 | 85.08 / 0 |
| 4 | Short | 97.65 / 0 | 57.77 / 80 | 90.11 / 0 |
| 4 | Full | 97.28 / 0 | 60.15 / 80 | 90.11 / 0 |
| 5 | Short | 97.81 / 0 | 63.35 / 50 | 89.83 / 0 |
| 5 | Full | 98.01 / 0 | 61.14 / 70 | 92.58 / 0 |
| 6 | Short | 98.03 / 0 | 62.43 / 60 | 93.17 / 0 |
| 6 | Full | 98.26 / 0 | 62.52 / 70 | 93.11 / 0 |

Talk (160 matches): EotM 113/160; incidents created/exposed 70/53; daily warnings/no-action 48/432; surviving-company firing/no-majority 33/127; carry 125.12 s/match over 1176 early-submit triggers. Accepted actions 34423, Busy discoveries 0.

NoComm (160 matches): EotM 49/160; incidents created/exposed 0/0; daily warnings/no-action 0/480; surviving-company firing/no-majority 0/49; carry 125.12 s/match over 1176 early-submit triggers. Accepted actions 16536, Busy discoveries 0.

Mixed (160 matches): EotM 124/160; incidents created/exposed 601/269; daily warnings/no-action 211/269; surviving-company firing/no-majority 130/30; carry 125.12 s/match over 1176 early-submit triggers. Accepted actions 33002, Busy discoveries 0.

Career component mean points per player, including closures:

| Component | Talk | NoComm | Mixed |
|---|---:|---:|---:|
| NotFired | 95.42 | 31.81 | 81.94 |
| CompanyTarget | 75.00 | 23.85 | 75.00 |
| SecretObjective | 35.33 | 11.00 | 32.83 |
| EmployeeOfMonth | 6.28 | 2.72 | 6.89 |
| ProvenSabotage | -3.68 | 0.00 | -18.68 |

Task mean output points (100 max), pooled by observed task instances:

| Task | Talk | NoComm | Mixed |
|---|---:|---:|---:|
| invoice_reconcile | 95.19 | 9.63 | 59.80 |
| budget_split | 99.54 | 90.27 | 88.69 |
| customer_spec | 93.21 | 22.45 | 69.70 |
| report_review | 100.00 | 100.00 | 92.69 |
| room_booking | 100.00 | 61.60 | 79.41 |
| archive_visitor | 97.70 | 57.30 | 84.17 |
| bonus_pool | 91.51 | 68.11 | 73.01 |
| server_code | 100.00 | 21.50 | 100.00 |
| phishing_check | 100.00 | 29.95 | 100.00 |
| shift_cover | 80.00 | 83.36 | 81.91 |
| vendor_pick | 99.19 | 66.42 | 99.19 |
| supply_swap | 100.00 | 17.29 | 95.60 |

Objective completion, including completed objectives on closed-company results (those receive no positive career award):

| Objective | Talk | NoComm | Mixed |
|---|---:|---:|---:|
| Saver | 22/80 (27.5%) | 15/80 (18.8%) | 22/80 (27.5%) |
| Broker | 8/68 (11.8%) | 3/68 (4.4%) | 7/68 (10.3%) |
| Patron | 0/88 (0.0%) | 0/88 (0.0%) | 0/88 (0.0%) |
| TeamPlayer | 74/74 (100.0%) | 36/74 (48.6%) | 71/74 (95.9%) |
| CleanRecord | 88/92 (95.7%) | 92/92 (100.0%) | 61/92 (66.3%) |
| Rival | 2/82 (2.4%) | 0/82 (0.0%) | 7/82 (8.5%) |
| Guardian | 66/72 (91.7%) | 25/72 (34.7%) | 62/72 (86.1%) |
| Archivist | 62/62 (100.0%) | 62/62 (100.0%) | 62/62 (100.0%) |
| Presenter | 102/102 (100.0%) | 102/102 (100.0%) | 102/102 (100.0%) |

Maximum measured six-player block-2 play view: **3,454 bytes**, below 6 KB. 1,000,000 idle ticks: 0 allocated bytes; 17.94 million ticks/s (local observation). Final matrix aggregate hash: **`057C28DACE6569CB`**. Local run time: 79.79 seconds.


## Limits

These policies are an integration/balance probe, not human playtest evidence. The large Talk advantage
includes perfect instantaneous information after contact; it does not measure real conversation time,
trust, negotiation or betrayal. Some normative tasks permit local cooperative strategies (request your
own need; always Share; prepare accurate reports and approve). NoComm results therefore describe this
particular policy and cannot establish that communication is logically necessary for every task.
No balance tunables were changed from `LineConfig.CreateDefault()` to improve the synthetic numbers.

The wire codec's 6 KB view target is for typical mid-match snapshots, not a hard cap on an entire match's
authenticated history. Unusually many connected calls or a large purchased history can create larger
snapshots; messages have their own bounded, separately paged delivery. Voice capture, speech-density
selection, transport routing, telemetry consent enforcement, UI/localisation and playtesting belong to
the host/client work and were not exercised by running Unity.

## Contract requests
