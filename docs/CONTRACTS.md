# Shared Contracts — Rules Engine ⇄ Unity Adapter

Code: `Game/Assets/Game/Core/Contracts/*.cs` (types) and `Game/Assets/Game/Core/Engine/*.cs` (engine surface).
Design rationale: `docs/DECISIONS.md`. This document defines **semantics**; the code defines **shape**.

## 0. Ground rules

- The core is pure C# (`noEngineReferences`), C# 9, netstandard2.1. No `UnityEngine`, no threads, no wall clock,
  no `System.Random` (use the core's own seeded PRNG), no static mutable state.
- Deterministic: identical config + layout + setup + call sequence ⇒ identical views, events and results.
- Time is `double` seconds of **host time**, passed in by the caller. Phase timers are absolute host times.
- The host is authoritative; clients only *request*. A client never declares completion, score, or success.
- The engine never throws on bad client input: it returns `ActionResult.Fail(code)`.

## 1. Identifiers

| Thing | Type | Format / range | Stability |
|---|---|---|---|
| Player | `int` | lobby slot 0..3, `PlayerIds.None = -1` | whole session incl. reconnect + rematch |
| Layout entity | `string` | `desk.N`, `desk.N.surface/.drawer/.nameplate/.pc`, `st.*`, `slot.*`, `spot.*`, `item.*` | across builds |
| Dynamic report | `string` | `item.report.<serial>` serial from 1 per match | match |
| Evidence | `int` | 1.. per match | match |
| Observation | `int` | 1.. per match | match |
| Private note | `int` | 1.. per match (global counter, owner-scoped visibility) | match |
| Scan result | `int` | 1.. per match | match |
| Incident | `int` | 1.. per match, **host-only** until Results | match |
| Match | `int` | `MatchSetup.MatchId`, adapter increments per rematch | session |
| Request | `uint` | client-chosen; duplicates = same id among that player's last 64 processed ids | match |
| Phase serial | `int` | +1 on every phase change | match |

## 2. Phases and allowed verbs

"Office phases" = Work1, Raid, Work2, FinalInspection.

| Verb | Briefing | Office phases | Discussion | Defense 1/2 | Vote | Verdict | Results |
|---|---|---|---|---|---|---|---|
| PickUp, Place, Drop, BrewCoffee, WriteReport, AnswerPhone, CleanDesk, CleanSpill, SpillCoffee, LockWorkstation, UnlockWorkstation, SearchContainer, SetWorkPose | – | ✔ | – | – | – | – | – |
| AuditScanItem, SealEvidence | – | ✔ | – | – | – | – | – |
| AuditScanEvidence | – | ✔ | ✔ | – | – | – | – |
| Accuse | – | – | ✔ | – | – | – | – |
| PresentNote | – | – | ✔ | ✔ | – | – | – |
| CastBallot | – | – | – | – | ✔ | – | – |
| RematchVote | – | – | – | – | – | – | ✔ |
| CancelInteraction | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |

Wrong phase ⇒ `WrongPhase`. Abandoned accepts nothing.

### Validation order (first failure wins)
1. `MatchId` ≠ current ⇒ `WrongMatch`. 2. Player not connected ⇒ `NotConnected`. 3. Duplicate request id ⇒
`Duplicate` (no side effects). 4. Phase ⇒ `WrongPhase`. 5. Physical verb while an interaction is active ⇒ `Busy`
(non-physical verbs — scans, seal, accuse, present, ballot, rematch, cancel — are allowed while busy).
6. Verb-specific checks in the order listed in §4.

### Interaction lifecycle
- Timed verbs create one `ActiveInteraction{verb, target, start, end, reservation}` per player; `ActionResult.CompletesAt` = end.
- The host completes it in `Tick` when `now >= end`, **re-validating** the completion conditions (still carrying the
  right item, slot still has room, demand still open, phone call still the same, actor within range). If
  re-validation fails the interaction is cancelled (`InteractionCancelled` event to the actor, no effect).
- Moving more than `Interaction.MoveCancelDistance` from the start position cancels it.
- Reservations: the target station / slot position / item is reserved by a timed interaction; competing requests get
  `TargetBusy`. Exclusive stations: coffee machine, phone, each desk PC, desk cleaning (per desk).
- Phase change into `MeetingDiscussion` (or `Abandoned`): every active interaction is cancelled, every carried item is
  dropped on the floor at its carrier's position (`ItemDropped` event), work poses are cleared.
- Work↔Raid↔Work↔Final transitions do **not** cancel interactions.
- Disconnect: the player's interaction is cancelled and their carried item dropped.

## 3. Visibility (what goes into `BuildView(recipient)`)

| Data | Public | Owner only | Host only |
|---|---|---|---|
| Phase, timers, quota, orders, phone, desk states (dirt, spill, lock + times, nameplate shown, clutter count) | ✔ | | |
| Players: name, colour, desk, connected, performance, tasks done, suspicion meter, carried item, **public activity** (not verb), work pose, accusations received | ✔ | | |
| Items on open slots / floor / carried | ✔ | | |
| Items in closed containers (drawers, hiding spots) | only to players in the item's `knownBy` set | | full |
| Evidence records, manager observations, public log, accusations, shortlist, ballots *count* + voter ids | ✔ | | |
| Who sealed an evidence record | | | ✔ |
| Own role, objective + progress, notes (witness/search/scan), Informant charges + scans, own interaction verb + times, own ballot, own accusation, cooldowns, reconnect token | | ✔ | |
| Other players' roles / objectives, incident list, true actors, item provenance (handlers), true misconduct, RNG seed state | | | ✔ |
| `MatchResultView` (full reveal) | ✔ (Results phase only) | | |

`knownBy` rules: when an item enters a closed container, `knownBy = {placer}`. A completed `SearchContainer` adds the
searcher to `knownBy` of every item currently inside. When an item leaves a closed container `knownBy` is cleared
(it is then visible publicly wherever it goes, unless it enters another closed container). Items placed in a container
by the system at match start (none in the default layout) are unknown to everyone.

Public events obey the same table (e.g. `WorkstationLocked` has no actor; `EvidenceUpdated` for a seal has no actor).
`PublicActivity` intentionally merges verbs: write report / lock / unlock are all `Typing`; drawer place / take /
search and hiding-spot use are all `Rummaging`.

## 4. Verb rules

Range: horizontal distance from the avatar to the target position ≤ `Range + RangeSlack` (target position = item
position for items, slot position for slots, station position for stations, desk position for `desk.N`).

| Verb | Checks (in order) | Duration | Completion effect |
|---|---|---|---|
| PickUp(item) | item exists and is visible to actor else `InvalidTarget`; not archived/carried/reserved else `ItemUnavailable`/`TargetBusy`; sealed ⇒ `Sealed`; hands full ⇒ `HandsFull`; range | open slot / floor: instant; closed container: `PickUpContainerSeconds`; nameplate holder: `PickUpNameplateSeconds` | actor carries it; handler appended to provenance; see *pick-up side effects* |
| Place(slot) | hands empty; slot exists; kind restrictions (`WrongItem`): nameplate holder ⇒ nameplates, mug shelf ⇒ mugs, spray rack ⇒ sprays, printer tray ⇒ nobody (`InvalidTarget`), delivery tray ⇒ full mug or report; delivery without an open order ⇒ `NoDemand`; full slot ⇒ `SlotFull` (nameplate holder: swap instead); sabotage limits (§5); range | see `InteractionConfig` / sabotage durations: suspicious → drawer uses PlantItem duration, other player's report → closed container uses HideReport duration, clutter → other desk surface uses MoveClutter duration | item enters slot at the lowest free index; delivery completes the task (mug → `Used`, stays on tray; report → `Archived`); nameplate holder swap gives the previous nameplate to the actor |
| Drop | hands empty ⇒ `HandsEmpty` | instant | item to floor at avatar + `DropForwardOffset` along yaw |
| BrewCoffee(st.coffee) | carrying a mug with fill Empty/Used else `WrongItem`/`HandsEmpty`; machine free; range | `BrewSeconds` | mug Full |
| WriteReport(desk.N.pc) | N = actor's assigned desk else `NotOwnDesk`; PC locked ⇒ `StationLocked` (records the lock's "owner blocked" consequence once); PC free; range | `WriteReportSeconds` | print job (owner = actor) enqueued at the printer |
| AnswerPhone(st.phone) | ringing else `NotRinging`; free; range | `AnswerPhoneSeconds` | task Phone; ringing stops; next ring scheduled. Ring end during an answer does not miss the call |
| CleanDesk(desk.N) | carrying spray; desk dirty else `NothingToClean`; desk-clean slot free; range | `CleanDeskSeconds` | dirt cleared; task CleanDesk |
| CleanSpill(desk.N) | carrying spray; spill else `NothingToClean`; free; range | `CleanSpillSeconds` | spill cleared; reversal credit if the spill's incident actor ≠ cleaner; **no** quota |
| SpillCoffee(desk.N) | carrying a Full mug; desk already spilled ⇒ `LimitReached`; sabotage limits; range | SpillCoffee duration | spill on desk; mug → Used (still carried); incident if desk owner ≠ actor |
| LockWorkstation(desk.N.pc) | own PC ⇒ `NotAllowed`; locked ⇒ `AlreadyLocked`; immunity ⇒ `TargetImmune`; sabotage limits; free; range | LockWorkstation duration | locked for `EffectSeconds`; public `WorkstationLockLog` evidence (no actor, `AttributedTo = None`); incident |
| UnlockWorkstation(desk.N.pc) | not locked ⇒ `NotLocked`; free; range | LockWorkstation `ReverseSeconds` | unlocked, immunity window starts; reversal credit if actor ≠ locker |
| SearchContainer(slot) | slot closed else `InvalidTarget`; range | `SearchSeconds` | actor added to `knownBy` of contents; private `SearchResult` note listing variants |
| SetWorkPose(0/1) | — | instant | flag; auto-cleared when avatar speed > 0.5 m/s or an interaction starts |
| CancelInteraction | — | instant | active interaction removed, reservations released, no effect |
| AuditScanEvidence(id) | not Informant ⇒ `NotAllowed`; no charges ⇒ `NoCharges`; evidence kind ∉ {SuspiciousItemFound, HiddenReportFound} ⇒ `EvidenceIneligible` | instant | scan result + private note; charge −1 |
| AuditScanItem(item) | Informant; charges; item visible to Informant, kind ∈ {Suspicious, Report, Nameplate} else `EvidenceIneligible`; within `ItemScanRange` | instant | scan result + private note; charge −1 |
| SealEvidence(id) | Informant; seal charge; evidence `Active`, not sealed, kind ∈ {SuspiciousItemFound, HiddenReportFound, WorkstationLockLog} | instant | sealed; attribution frozen; its item (if at the recorded location) cannot be picked up (`Sealed`) until the match ends |
| Accuse(p) | already accused ⇒ `AlreadyUsed`; self ⇒ `CannotTargetSelf`; invalid ⇒ `InvalidTarget` | instant | public accusation |
| PresentNote(noteId) | not own / unknown ⇒ `InvalidTarget`; presented ⇒ `AlreadyUsed`; 2 per player ⇒ `LimitReached` | instant | public evidence (Witness/Search/ScanStatement) with `SourcePlayer = presenter` |
| CastBallot(p or None) | voted ⇒ `AlreadyUsed`; self ⇒ `CannotTargetSelf`; p ∉ shortlist ⇒ `NotCandidate` | instant | ballot recorded (choice private until verdict tallies) |
| RematchVote | voted ⇒ `AlreadyUsed` | instant | flag |

### Pick-up side effects
- Item is the subject of an `Active`, unsealed evidence record and still at the recorded location ⇒ evidence → `Moved`,
  `EvidenceTampering` incident for the actor (whoever they are).
- Item was planted (open `PlantItem` incident at this desk) and actor ≠ planter ⇒ incident reversed by actor.
- Clutter placed by an incident on a desk and actor ≠ placer ⇒ reversed (the clutter must leave the desk).
- Report hidden by a `HideReport` incident (in a closed container) and actor ≠ hider ⇒ reversed; a public
  `HiddenReportFound` evidence is created (location = container, attributed to the desk nameplate if a drawer, else None).

### Place side effects (incidents) — misconduct is recorded for the **true actor** only
- Suspicious item → surface/drawer of a desk whose **assigned** owner ≠ actor ⇒ `PlantItem` (victim = assigned owner).
- Clutter → surface of a desk whose assigned owner ≠ actor ⇒ `MoveClutter`.
- Another player's report → closed container ⇒ `HideReport` (victim = report owner).
- Nameplate into a holder such that the holder's nameplate ≠ desk's assigned owner ⇒ `NameplateSwap` (victim =
  assigned owner of that desk). Restoring the correct nameplate reverses any open swap incident of another actor on
  that desk.
- Evidence item → junk bin ⇒ evidence `Disposed` (+ `EvidenceTampering` if not already recorded for this pick-up).
- Planting into your own desk, cluttering your own desk, hiding your own report: allowed, no incident.

## 5. Sabotage limits (host)

Before starting a sabotage-creating interaction (and again at completion):
1. Actor's `SabotageCooldowns[type]` > now ⇒ `Cooldown` (cooldown starts when the incident is created).
2. Same actor hit the same victim with the same type within `SameTargetCooldownSeconds` ⇒ `TargetImmune`.
3. Victim desk already has `MaxActiveEffectsPerVictim` active (unreversed, still-effective) incidents ⇒ `TargetImmune`.
4. Actor has `MaxActiveEffectsPerActor` active incidents ⇒ `LimitReached`.
5. Type-specific caps: ≤`MaxSuspiciousItemsPerDesk` suspicious items, ≤`MaxClutterPerDesk` clutter, 1 spill per desk,
   workstation immunity after unlock/expiry.

An incident stops being "active" when reversed, when its effect ends (lock expiry), or when the item leaves the desk.

## 6. Incidents and true misconduct (host-only)

`Incident{id, type, actor, victim, deskIndex, itemId, time, base, consequenceApplied, discovered, reversedBy}`.
Misconduct(p) = Σ over incidents with actor p of (base + consequence). Consequence (+`ConsequenceMisconduct`, at most
once per incident):
- PlantItem: discovered by the manager while attributed to someone other than the actor.
- NameplateSwap: an evidence/observation was attributed to the wrong person at this desk while the swap was in place.
- LockWorkstation: the desk owner tried `WriteReport` while locked.
- HideReport: the report was never delivered by the end of the final inspection.
- SpillCoffee / MoveClutter: a `MessyDesk` observation at that desk was attributed to someone other than the actor.
Nothing — moving items, swapping nameplates, sealing — ever changes an incident's actor.

## 7. Evidence, observations, observed risk

Evidence creation: manager desk inspection (`SuspiciousItemFound` for each suspicious item on the desk surface or in
the drawer not already covered by an Active record at that desk; `HiddenReportFound` for another player's report in
the drawer), report recovery (§4), lock log, presented notes. Attribution = player shown by the nameplate in that desk's
holder at that moment; empty holder ⇒ the desk's assigned owner.

Status: on pick-up from the recorded location ⇒ `Moved` (§4); junk bin ⇒ `Disposed`. During the final inspection each
desk stop re-checks the desk's Active records; at the end of `FinalInspection` all unsealed Active records whose item
is not at the recorded location become `Moved`, and the dossier is frozen (`QuotaView.Frozen = true`).

Observed risk(p) = max(0, Σ evidence weight attributed to p + Σ witness statements with subject p + Σ observation
severity with subject p). Evidence weight: SuspiciousItemFound = `Risk.SuspiciousItem`; HiddenReportFound in a drawer
= `Risk.HiddenReportInDrawer` (elsewhere 0); WitnessStatement = `Risk.WitnessStatement`; × `MovedEvidenceFactor` if
Moved; 0 if Disposed; sealed records always count fully. Accusations are **not** part of observed risk.
`SuspicionLevel` = number of `Risk.MeterThresholds` ≤ observed risk.

## 8. Manager

States per `ManagerState`. Raid: `Warning` for `RaidWarning` (event `ManagerWarning`, `ArrivesAt`), then walks
`ManagerHome → Route[0..n] → ManagerHome` at `WalkSpeed`, dwelling `DwellSeconds` at desk stops (state `Inspecting`).
Final inspection: same with `FinalWarning`. Yaw = travel direction while walking, stop yaw while inspecting.
Observation is active only while the phase is Raid/FinalInspection and the manager is out of the office.

Vision: target within `VisionRange`, within `VisionHalfAngle` of manager yaw, line of sight eye(1.6 m)→chest(1.0 m).
Continuous checks (once per player per inspection unless noted): moving faster than `MovingSpeedThreshold` for
`MovingNoticeSeconds` ⇒ `MovingDuringInspection`; carrying a suspicious item ⇒ `CarryingSuspicious` (once per item);
an active sabotage-creating interaction ⇒ `CaughtInAct` (once per interaction).

Desk inspection at stop D (apparent owner A via nameplate): evidence (§7); `MessyDesk` for A if dirty/spill/clutter
(severity = min(cap, Σ weights), detail string); `NotAtDesk` for A if A's avatar is farther than `AtDeskRadius` from
D's seat; `AtWrongDesk` for each other player within that radius; `BehindOnWork` if A's performance < expected;
`WorkingHard` (negative) if A is at the desk in work pose or doing a work activity. Each observation emits a public
`ManagerObservation` event and sets the reaction bubble.

The manager reads only what it observes. It never reads incidents or misconduct.

## 9. Witnesses

On completion of a witnessable activity (other player's desk surface/drawer/PC, any nameplate handling, any hiding
spot, any spill), every other connected player within `Witness.Range`, inside `Witness.HalfAngle` of their avatar
yaw, with line of sight, gets a private `Witness` note: subject, `PublicActivity`, target, desk, item variant held
before and after. Notes state what was seen, never the intent.

## 10. Informant scans

For an evidence record (or an item's current location): D = desk, A = apparent owner (record attribution, or current
nameplate for an item at a desk; None elsewhere), R = actor of the last placement of the item into that location.
Reliability: A = None or R unknown ⇒ Medium; R = A ⇒ High; A handled the item at some point ⇒ Medium; else Low.
Signal (first match): D's nameplate changed after the item was placed ⇒ `NameplateChangedAfterPlacement`; item moved
within `MovedRecentlySeconds` of the scan ⇒ `MovedRecently`; ≥2 distinct handlers ⇒ `HandledByMultiple`; else
`NoInconsistency`. A scan never contains a player id other than the apparent owner already public.

## 11. Meeting, verdict, results

- Discussion end ⇒ shortlist: sort players by accusations desc, observed risk desc, performance asc, seeded draw.
  ≥2 accused ⇒ top two; exactly one accused ⇒ that player + best-ranked other; none ⇒ top two by risk ordering.
- Defense 1 speaker = shortlist[0], Defense 2 = shortlist[1].
- Verdict (computed when MeetingVerdict starts; formula in DECISIONS §11). Missing ballots = abstain.
- Results (when Results starts): company success ⇔ quota points ≥ target at the freeze. False accusation penalty
  (accused had zero true misconduct) is applied to the accuser's final performance **after** the verdict.
  Objective predicates per DECISIONS §9. Employee win = company ∧ not fired ∧ predicate. Informant win = company ∧
  not fired ∧ fired ∈ top positive misconduct tier. Explanation keys: `outcome.company_failed`, `outcome.fired`,
  `outcome.objective_failed`, `outcome.won`, `outcome.informant_won`, `outcome.informant_wrong_target`,
  `outcome.no_misconduct`.
- Titles (max 2 per player): `title.coffee_master`, `title.printer_enemy`, `title.pro_scapegoat`,
  `title.cleanest_desk`, `title.informant_of_month`, `title.chaos_agent`.
- Recap: ≤ 10 chronological beats from incidents, manager discoveries, accusations and the firing.
- `ResultHash` = `WireCodec.HashResult` over the result content (excluding the hash field).
- Results phase lasts `Durations.Results`; the engine stays in Results afterwards until the adapter starts a rematch
  with a new `MatchEngine` (new `MatchId`, history from `BuildNextHistory()`).

## 12. Roles and objectives

Exactly one Informant (random; avoid repeating last match's Informant when `AvoidRepeatInformant`). Each employee gets a
distinct enabled card, preferring cards not used in the previous match; targets are other players (never self, never
absent), weighted toward players targeted least often and avoiding the last match's holder→target pairs.
`MatchSetup.ForceInformant/ForceObjectives/ForceTargets` override for tests.

## 13. Network transport (adapter-owned, using `WireCodec`)

| Direction | Message | When | Recipient |
|---|---|---|---|
| C→H | `ConnectionPayload` | NGO connection approval data | host |
| H→C | `WelcomeMessage` | after approval | joining client |
| H→all | `LobbyView` | lobby changes | everyone |
| C→H | `ActionRequest` | input | host |
| H→C | `ActionResult` | per request | requester |
| H→C | `PlayerView` | state version changed (≤ 10 Hz) and on (re)connect | each client, own view |
| H→C | `List<GameEvent>` | events drained this frame, filtered by `IsVisibleTo` | each client |

Compatibility: approval rejects `ProtocolVersion`/`BuildId` mismatch (`reject.version`), full lobby
(`reject.full`), match running without a valid reconnect token (`reject.in_progress`).
Reconnect: the token issued at first join maps to the slot; within the 30 s grace the same slot, role, objective,
notes and charges are restored and a full view is sent immediately. Grace expiry or host loss ⇒ `Abandoned`.

## Changelog
- v1 (2026-10-23): initial contract.
- v1.1 (2026-10-23, integration review — wire format unchanged, `Protocol.Version` stays 1):
  - Own report → **another employee's drawer** ⇒ `PlantItem` incident (victim = desk owner). Previously free framing.
  - Manager observations that identify a person directly (`CaughtInAct`, `AtWrongDesk`) no longer trigger the
    nameplate-swap "wrong attribution" consequence.
  - Recovering a hidden report marks the `HideReport` incident discovered/blamed and applies nameplate consequences.
  - Voluntary `Drop` no longer emits a public `ItemDropped` event (forced drops at meeting start/disconnect still do);
    views carry at most the last 200 public log entries.
  - Item `SlotIndex` inside closed containers is renumbered per recipient (no leak of unknown contents).
  - An evidence item put back where it was found is re-activated at the next inspection instead of duplicated.
  - `EvidenceTampering` is misconduct but does not count as sabotage for Clean Hands or the sabotage stat;
    a suspicious item found on your desk at the final inspection counts as an Under Pressure penalty.
