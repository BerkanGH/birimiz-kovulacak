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
Final NUnit run: **505 passed, 0 failed, 0 skipped**. NuGet emitted NU1900 because the sandbox could not
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
| 3 | Short | 97.86 / 0 | 54.41 / 75 | 84.24 / 0 |
| 3 | Full | 98.47 / 0 | 56.24 / 70 | 84.44 / 0 |
| 4 | Short | 99.24 / 0 | 57.77 / 80 | 90.65 / 0 |
| 4 | Full | 98.98 / 0 | 60.10 / 80 | 90.34 / 0 |
| 5 | Short | 99.19 / 0 | 63.35 / 50 | 90.87 / 0 |
| 5 | Full | 98.93 / 0 | 61.14 / 70 | 92.79 / 0 |
| 6 | Short | 99.03 / 0 | 62.43 / 60 | 93.63 / 0 |
| 6 | Full | 99.20 / 0 | 62.52 / 70 | 93.97 / 0 |

Talk (160 matches): EotM 93/160; incidents created/exposed 77/0; daily warnings/no-action 0/480; surviving-company firing/no-majority 0/160; carry 125.12 s/match over 1176 early-submit triggers. Accepted actions 33771, Busy discoveries 0.

NoComm (160 matches): EotM 49/160; incidents created/exposed 0/0; daily warnings/no-action 0/480; surviving-company firing/no-majority 0/49; carry 125.12 s/match over 1176 early-submit triggers. Accepted actions 16068, Busy discoveries 0.

Mixed (160 matches): EotM 125/160; incidents created/exposed 601/50; daily warnings/no-action 63/417; surviving-company firing/no-majority 42/118; carry 125.12 s/match over 1176 early-submit triggers. Accepted actions 32211, Busy discoveries 0.

Career component mean points per player, including closures:

| Component | Talk | NoComm | Mixed |
|---|---:|---:|---:|
| NotFired | 100.00 | 31.81 | 94.17 |
| CompanyTarget | 75.00 | 23.85 | 75.00 |
| SecretObjective | 42.58 | 12.83 | 41.33 |
| EmployeeOfMonth | 5.17 | 2.72 | 6.94 |
| ProvenSabotage | 0.00 | 0.00 | -3.47 |

Task mean output points (100 max), pooled by observed task instances:

| Task | Talk | NoComm | Mixed |
|---|---:|---:|---:|
| invoice_reconcile | 93.81 | 9.63 | 59.66 |
| budget_split | 99.54 | 90.27 | 87.74 |
| customer_spec | 93.82 | 22.45 | 69.61 |
| report_review | 100.00 | 100.00 | 92.69 |
| room_booking | 100.00 | 61.60 | 79.41 |
| archive_visitor | 97.84 | 57.30 | 84.58 |
| bonus_pool | 91.26 | 68.11 | 72.92 |
| server_code | 100.00 | 21.50 | 100.00 |
| phishing_check | 100.00 | 29.95 | 100.00 |
| shift_cover | 100.00 | 83.36 | 89.74 |
| vendor_pick | 99.19 | 66.42 | 99.19 |
| supply_swap | 100.00 | 17.29 | 95.60 |

Objective completion, including completed objectives on closed-company results (those receive no positive career award):

| Objective | Talk | NoComm | Mixed |
|---|---:|---:|---:|
| Saver | 14/80 (17.5%) | 6/80 (7.5%) | 11/80 (13.8%) |
| Broker | 8/68 (11.8%) | 0/68 (0.0%) | 7/68 (10.3%) |
| Patron | 88/88 (100.0%) | 88/88 (100.0%) | 88/88 (100.0%) |
| TeamPlayer | 74/74 (100.0%) | 36/74 (48.6%) | 71/74 (95.9%) |
| CleanRecord | 92/92 (100.0%) | 92/92 (100.0%) | 83/92 (90.2%) |
| Rival | 0/82 (0.0%) | 0/82 (0.0%) | 1/82 (1.2%) |
| Guardian | 72/72 (100.0%) | 25/72 (34.7%) | 71/72 (98.6%) |
| Archivist | 61/62 (98.4%) | 60/62 (96.8%) | 62/62 (100.0%) |
| Presenter | 102/102 (100.0%) | 102/102 (100.0%) | 102/102 (100.0%) |

Maximum measured six-player block-2 play view: **3,405 bytes**, below 6 KB. 1,000,000 idle ticks: 0 allocated bytes; 16.06 million ticks/s (local observation). Final matrix aggregate hash: **`2719C182636CE5E2`**. Local run time: 86.57 seconds.

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

## Follow-up

This bounded follow-up implements `docs/codex/ENGINE_V4_FOLLOWUP.md` on the lead's merged v4 branch.
Contracts and `Core/Telemetry/**` remain untouched; no commit or Unity run was made.

- Each resolved task emits one Host-audience `HostOutcome` (49) per scheduled participant and one
  `HostTaskOutcome` (50). The codec now accepts both enum values. Participant income is only the credited
  `MoneyReason.TaskIncome` delta after the running block filter; deals, favourite bonuses and remedials
  are excluded. Quality is the value accumulated for EotM (zero when the seat never participated).
  Opportunity/credit flags reflect EotM accounting; the connected flag snapshots resolution presence.
- Task group counts use the pair/trio partition for Pair tasks and one group for the remaining tasks.
  Full groups use actual output after sabotage. Sabotage loss is the non-negative difference from
  output with only sabotage options removed, using the same submissions, grants/approval decisions,
  group floors, means and MaxOutput scaling. It is metadata and never subtracts output again.
  In Report Review the approval/rejection decision stays fixed: a rejected polished report can have
  zero measured loss relative to a rejected accurate report. Accepting a bank offer by itself changes
  no code, flag or vendor vote and therefore has zero direct output loss.
- Both recipient copies of `RemedialResolved` now carry the actual added output in C, capped by remaining
  task capacity; declines, timeouts and cancellation carry zero.
- Archivist tracks acquisition provenance in a private id set. Only independent purchases (including
  future records covered by an interval purchase) or partner visibility qualify. Own calls/transfers,
  own submissions and payments, public task outcomes/warnings/remedials, and records gained solely
  from another player's presentation do not count. T4 audit figures describe the Subject's department;
  a peer named as Subject on my own submission does not turn it into foreign information. A later
  purchase can qualify an already presented record. Distinct-colleague checks use the same qualified set.
- The Presenter objective rule is unchanged. All bot policies use at most one presentation per meeting,
  counting any allowance already consumed, and select only unpresented records about another player.
- The shift-cover bug was independent per-shift ownership: the same low-id flexible volunteer could
  be selected for several shifts, choose only the first, and leave other volunteers yielding. Talk now
  computes a deterministic matching that maximises covered shifts, with private cost as a tie-break.
  Each player gets at most one shift. NoComm and Greedy retain their previous minimum-available-slot
  choice, so the comparison isolates the Talk correction rather than exploiting fact order.
- Patron's `Other()` always chose the same colleague, so repeated payments could never satisfy the
  distinct-colleague goal. Bots now read their own authenticated transfer totals, finish a partial
  payment or choose another unpaid colleague, honour balance/rate/amount limits, and stop after reaching
  the objective. The policy still makes at most one transfer per block.

Validation: `dotnet test Tools/CoreTests/Tests/CoreTests.csproj`: **505 passed, 0 failed, 0 skipped**
(88 additional regression cases). Coverage includes T1/T7/T5 event values and all twelve task/head-count
combinations, filtered income, clamped/scaled sabotage loss, absence flags, actual remedial deltas,
codec enum support, and client event-list exclusion at every phase. Archivist fixtures explicitly set
thresholds rather than assuming the shipped default. Shift tests cover 48 seeded deals across 3-6
players; Patron tests complete the objective under all three policies. Determinism and privacy scans pass.
The core build also completed with 0 warnings and 0 errors. The package audit warning remains the sandbox's
unreachable NuGet vulnerability service; package restore and tests succeeded.

**Synthetic bot data.** Re-ran the exact matrix command shown above: 480 completed matches, 20 per cell,
unchanged seeds, 2-second input steps, and ten-match sessions. All updated tables above and
`Tools/LineSim/v4-sim.txt` describe this follow-up run. The raw report retains productivity distributions,
per-task output, career averages, EotM, objectives, incidents/exposure, warnings, firing and carry.

| Probe | Original Talk / NoComm / Mixed | Follow-up Talk / NoComm / Mixed |
|---|---|---|
| Shift output | 80.00 / 83.36 / 81.91 | 100.00 / 83.36 / 89.74 |
| Patron completed | 0/88 / 0/88 / 0/88 | 88/88 / 88/88 / 88/88 |
| Archivist completed | 62/62 / 62/62 / 62/62 | 61/62 / 60/62 / 62/62 |
| Presenter completed | 102/102 / 102/102 / 102/102 | 102/102 / 102/102 / 102/102 |

Talk: mean final productivity 98.86%, closures 0/160, EotM 93/160. NoComm: mean final productivity 59.75%, closures 111/160, EotM 49/160. Mixed: mean final productivity 90.12%, closures 0/160, EotM 125/160.

Presenter still completes in every observed assignment despite the one-per-meeting and foreign-record
restrictions. Early partner entries and purchased evidence provide enough qualifying records across
separate meetings. The cap is enforced; this cohort does not establish that the objective is difficult.

**Recommended lead-owned default: `ArchivistRecords = 4` (currently 6).** The engine reads the actual
config value and applies no override. This matrix therefore used **6**, with the stricter acquisition rule;
the reported rates are not measurements of the proposed default 4. A lead config change and subsequent
human playtests can evaluate that recommendation.

Final result aggregate hash: **`2719C182636CE5E2`**; six-player block-2 maximum **3,405 bytes**;
1,000,000 idle ticks: 0 allocated bytes; 16.06 million ticks/s (local observation).

## Contract requests

Recommended default change in the lead-owned config: `ArchivistRecords` 6 -> **4**. No schema change is needed.

## Lead changes after the Codex runs (2026-11-01)

Review fixes applied by the lead (each with a regression test in `LineReviewFixTests.cs` / `LineRoundTests.cs`):
phishing real account drawn once (was per player), EotM cooperation term in the quality scale, T4 waiting decision not
missed, T10 overtime only for a shift's sole volunteer + random listing order, T6 HR-file subject seeded, submissions
before a disconnect stand, symmetric customer-spec catalogue (public majority revealed the answer), `ArchivistRecords`
default 4. The tables above describe the Codex runs; the final-engine rerun of the same matrix is
`Tools/LineSim/v4-sim.txt` (synthetic: Talk 98.9 % / NoComm 61.7 % / Talk + one Greedy 90.0 % mean productivity).
