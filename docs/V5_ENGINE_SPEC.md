# v5 engine specification (implementer reference)

Normative for the Phase 1 engine rewrite (`docs/V5_IMPLEMENTATION_PLAN.md` §2, Phase 1). Game design and rationale:
`docs/V5_DESIGN.md` (Turkish). If this file and the design doc disagree, this file wins for code and the design doc
must be fixed. Numbers marked *config* live in `LineConfig` with the given default.

Terminology: **prim** = money = score. **suç** = blame (public). A **group** = a pair, or a trio when the head count is
odd.

## 1. Boundaries and invariants (unchanged from v4)

- Pure C# under `Game/Assets/Game/Core/Line`, namespace `BirimizKovulacak.Core`, no UnityEngine. C# 9, netstandard2.1.
- Host-only, deterministic: same setup + seed + sequence of `(time, player, action)` ⇒ same views, events, result.
  `LineRandom` (seeded SplitMix64) is the only randomness. Time is host seconds, finite, monotonic.
- `ILineEngine` keeps its shape: `Config, Setup, Phase, PhaseEndTime, Day, StateVersion, IsFinished, Result, Tick,
  Apply, SetConnected, BuildView, DrainEvents, NextHistory, MessagesFor, ConnectedCallId, CallPartner, TappersOf,
  MaySpeak, RecordingPicks, ReportRecording, HostContinue`. `Day` = meeting index (0-based) for compatibility.
  `TappersOf` always returns 0 (no wiretap in v5).
- Per-player views only; privacy by allow-list. A view must never contain another player's prim, choices, transfers,
  recorder marks or votes before the rule below says they are revealed.
- Request ids, duplicate rejection, `LineActionResult`, `LineFailure`, presence (`SetConnected`), the 60 s
  "fewer than two connected ⇒ Abandoned" rule, `LineText.Sanitize`: keep v4 behaviour.
- `LineProtocol.Version = 5`, `RulesetVersion = "v5.0-kayit"`, `ConfigVersion = 5`. Players 3–6.

## 2. Removed v4 systems

Career points, Employee of the Month, company productivity/closure, managers' gameplay effects (keep `profile` only if
needed for flavour; no rules), 12-task pool and scheduler weights, objectives, dirty tricks, wiretap, evidence market,
records, incidents, remedial work, warnings/daily warning vote, emails, anonymous complaints, task tutorials state,
session task history. Messages keep only `Text` (private 1:1) and `MeetingChat` (public, meeting phases) plus
`System` notices if useful.

## 3. Match flow

Phases (`LinePhase`, new numbering): `None 0, Intro 1, TaskPlay 2, TaskReveal 3, Break 4, MeetingDossier 5,
MeetingDiscussion 6, MeetingVote 7, MeetingVerdict 8, CrisisCalls 9, CrisisVote 10, CrisisVerdict 11,
FinalDiscussion 12, FinalDefense 13, FinalVote 14, FinalVerdict 15, Results 16, Abandoned 17`.

```
Intro
Task 1 (pair):  TaskPlay → TaskReveal → Break
Task 2 (pair):  TaskPlay → TaskReveal → Break           (whistle window for Petty Cash)
Meeting 1:      MeetingDossier → MeetingDiscussion → [MeetingVote → MeetingVerdict]
Task 3 (group): TaskPlay → TaskReveal
Meeting 2:      only if task 3 failed: MeetingDossier → MeetingDiscussion → MeetingVote → MeetingVerdict
Crisis:         CrisisCalls → CrisisVote → CrisisVerdict
Final:          FinalDiscussion → FinalDefense (one slot per candidate) → FinalVote → FinalVerdict
                [5–6 players: FinalVote → FinalVerdict again for the second firing]
                (a tie inserts one revote: FinalVote again restricted to the tied candidates, see §9)
Results
```

- `MeetingVote`/`MeetingVerdict` are skipped when the meeting has zero vote-able failures (§7). Meeting 2 is skipped
  entirely when task 3 succeeded.
- Prototype task order is fixed: task 1 = `ClientBonus`, task 2 = `PettyCash`, task 3 = `WeekendShift`.
  `LineSetup.ForceTasks` may override for tests; keep a `TaskKind` enum (`None, ClientBonus, PettyCash,
  WeekendShift`) so more tasks can be added.
- Durations (seconds, *config* `Durations`): Intro 20, TaskPlay 60, TaskReveal 8, Break 30, MeetingDossier 15
  (+ 8 per leak queued during it is **not** added; leaks play during discussion too), MeetingDiscussion 50,
  MeetingVote 20, MeetingVerdict 6, CrisisCalls 75, CrisisVote 20, CrisisVerdict 8, FinalDiscussion 40,
  FinalDefense 15 per candidate, FinalVote 25, FinalRevote 15, FinalVerdict 10, Results 45, Ring 12.
- Early finish: when every connected player has submitted the task's required choice, `TaskPlay` ends 3 s later
  (*config* `EarlyFinishSeconds`). No time carry.
- `HostContinue` ends `Results` early (v4 behaviour).

## 4. Phone, line board, transfers, messages

- **Calls** (reuse v4 `Call/Answer/Decline/HangUp`, busy and missed logic, ring timeout): allowed in `Intro, TaskPlay,
  TaskReveal, Break, CrisisCalls`. Leaving those phases ends all calls (`CallEndReason.PhaseEnd`).
- Every call (connected or not) keeps an internal record: id, caller, callee, start, end, connected, phase.
- **Line board** (public, every view, while calls are allowed): each active call with caller, callee, status
  (`Ringing`/`Connected`) and start time. Contents are never public.
- **Voice mode**: calls phases = 1:1 only; all other phases = everyone hears everyone. `MaySpeak(p)` is false only in
  `FinalDefense` for everyone except the current defender.
- **Transfer** (`Transfer`, target, A = amount): 1..`TransferMax` (*config* 10), ≤ balance, target ≠ self, allowed
  from `Intro` through `FinalVote` inclusive. One per `TransferMinInterval` (*config* 1 s). Visible only to sender
  and receiver (their own transfer log) until `Results`.
- **Messages**: `SendText` (private, any phase except `Results`/`Abandoned`) and `SendMeetingChat` (meeting, crisis
  vote/verdict and final phases). Keep v4 limits/rate limits for these two kinds.

## 5. Tasks

Grouping per pair task: seeded shuffle of the seated players into pairs; odd count ⇒ the last group is a trio.
Task 2 must not repeat any pair from task 1 when an arrangement without repeats exists (search deterministically over
seeded shuffles, max 200 tries, then take the arrangement with the fewest repeats). The trio should differ between
tasks when possible.

Choices are submitted with `SubmitTask` (A = choice). Changeable until `TaskPlay` ends. Missing choice = default.
Absent players take the default. No blame for missing choices.

### 5.1 ClientBonus (task 1, pair/trio)

- Choice: `Share 0` (default) / `Take 1`.
- Payout (*config*): pair — SS 2/2, ST taker 5 / sharer 0, TT 1/1. Trio — no takers 2 each; exactly one taker 5,
  others 0; two or more takers 1 each.
- Reveal: each member sees every group member's choice and own payout. Public: total number of takers in the match
  (no groups).
- Failure: the task **fails** if at least one player chose Take. Each taker is an *unresolved culprit* until exposed
  by an investigation (§8).

### 5.2 PettyCash (task 2, pair/trio)

- Choice: `Leave 0` (default) / `Take 1`.
- Per group: 0 takers ⇒ nothing. Exactly 1 taker ⇒ taker +3 prim (*config* `CashTake`), group-mates are
  **witnesses**. ≥ 2 takers ⇒ all takers are **caught**: they get 0 prim and each +2 suç (*config*
  `CashCaughtBlame`), resolved, publicly announced in Meeting 1's dossier.
- Reveal: each member sees every group member's choice. Nothing public yet.
- **Whistle** (`Whistle`, target): only in the `Break` after task 2. Actor must be a witness of that target (same
  group, target is the sole taker). First accepted whistle per theft wins: taker loses the 3 prim (min with balance;
  removed from the game), +3 suç (*config* `CashWhistleBlame`); whistler +2 prim (*config* `CashWhistleReward`).
  Resolved. Public in Meeting 1's dossier, including the whistler's name.
- Failure: the task **fails** (vote-able) if any sole-taker theft is still unresolved at Meeting 1. The dossier shows
  the total missing prim from unresolved thefts, never names.

### 5.3 WeekendShift (task 3, everyone)

- Needed volunteers K = ceil(N / 2) (3→2, 4→2, 5→3, 6→3).
- Choice: `Skip 0` (default) / `Volunteer 1`.
- Non-volunteers +2 prim (*config* `ShiftSkipPay`). Volunteers 0.
- Reveal: the roster (who volunteered) is **public**.
- Failure: volunteers < K.

## 6. Prim and suç bookkeeping

- Start prim 6 (*config* `StartPrim`), start suç 0. Prim never below 0. Suç never below 0.
- Every prim change and suç change is logged internally with reason (task, whistle, investigation, vote, crisis,
  transfer, firing) for the final reveal.

## 7. Meetings (1 and 2)

- **Dossier content** (public): failed tasks of this meeting; ClientBonus taker count; PettyCash caught groups,
  whistles (who whistled whom) and unresolved missing total; WeekendShift roster; investigations and leaks as they
  happen; current suç board.
- **Vote-able failures** (counted when `MeetingVote` starts): Meeting 1 — ClientBonus counts 1 if any taker is still
  unexposed; PettyCash counts 1 if any sole-taker theft is unresolved. Meeting 2 — WeekendShift counts 1.
  Zero ⇒ skip vote and verdict.
- **Blame vote**: each connected player has V = vote-able failures ballots. `Vote` (target, A = ballot index 0..V-1)
  sets/changes one ballot; target must be another seated player. When the phase ends every **connected** player's
  empty ballot is assigned to themselves. Each ballot = +1 suç (*config* `BlamePerVote`) to its target. Disconnected
  players' empty ballots are dropped.
- Ballots are secret until `Results`; during the vote views show only who has finished voting. `MeetingVerdict` shows
  suç received per player in this meeting.

## 8. Investigation (soruşturma)

- One per player per match, costs 4 prim (*config* `InvestigationCost`, paid and removed in every case).
- Verb `Investigate` (target) in Meeting 1 `MeetingDossier`/`MeetingDiscussion` only. Valid when actor and target
  were in the same ClientBonus group and actor can pay.
- Result is public immediately: the target's ClientBonus choice.
  - Target chose Take: target +3 suç (*config* `InvestigationGuiltyBlame`) and pays the investigator 3 prim
    (*config* `InvestigationRefund`, min with target's balance). That taker is now exposed.
  - Target chose Share: investigator +3 suç (*config* `InvestigationFalseBlame`).

## 9. Crisis and final

- **CrisisCalls**: calls and transfers open, everyone else silent (1:1 voice only).
- **CrisisVote**: each connected player votes one other player (`Vote`, target). Empty ⇒ self. The crisis blame 4
  (*config* `CrisisBlame`) goes to the plurality; a tie splits it equally among the tied (floor; remainder dropped).
- **Candidates** (fixed at `CrisisVerdict`): top `CandidatesFor(N)` by suç (*config*: 3–4 players ⇒ 2, 5–6 ⇒ 3);
  everyone tied with the last included candidate is also a candidate.
- **Firings**: `FiringsFor(N)` (*config*: 3–4 ⇒ 1, 5–6 ⇒ 2), sequential.
- **FinalDiscussion**: everyone hears everyone; leaks allowed.
- **FinalDefense**: candidates only, in order suç desc then seat id; `MaySpeak` gives the floor to the defender.
- **FinalVote**: each connected player (candidates included) votes one candidate other than themselves (`Vote`,
  target). Abstaining is allowed, but **only players who voted for the fired candidate receive a share** of the
  fired candidate's prim. Plurality is fired.
- **Ties**: first a single `FinalVote` revote (duration FinalRevote) restricted to the tied candidates; still tied ⇒
  higher suç is fired; still tied ⇒ higher prim is fired; still tied ⇒ seeded random among them.
- **Desk reveal** (`FinalVerdict`): the fired player's prim is shown publicly, then split floor-equally among the
  players who voted for them in the deciding vote; remainder removed; fired prim = 0. If nobody voted for them, all is
  removed.
- **Second firing** (5–6 players): another `FinalVote` among the remaining candidates (same rules), then
  `FinalVerdict`.
- **Winner**: highest prim among non-fired players; equal prim ⇒ shared win.

## 10. Recorder and leaks

- Each player has `RecorderUses` (*config* 1) per match.
- `Record` while the actor is in a **connected** call: marks that call as recorded by the actor (secret; the other
  party is not told). One use consumed.
- `Leak` (A = call id) in `MeetingDossier`, `MeetingDiscussion` or `FinalDiscussion`: the actor must have recorded
  that call; each recorded call can be leaked once. Adds a `LineRecordingPick` with `CandidateCallIds = [callId]`,
  `Seconds = 7`, `PlayAt` = max(now + 0.5, end of the previous pick in this meeting), caller/callee, leaker. Public:
  "X leaked a call between A and B".
- `RecordingPicks` lists the current meeting's picks (cleared when a meeting starts). `ReportRecording` stores
  whether it played (−1 = no speech found) and shows "kayıt boş" in the view when nothing played.
- The voice layer already records every connected call in host memory; the engine only decides what plays.
- `Setup.RecordingsEnabled = false` ⇒ `Record`/`Leak` fail with `NotAvailable`.

## 11. Views (shape; names may differ)

- `LineView`: ruleset, protocol, match/week id, phase, phase end, meeting index, my seat, `Players`, `LineBoard`,
  `Mine`, `Task` (current/last task), `Meeting`, `Final`, `Results` (only in `Results`).
- `Players[i]`: id, name, color, connected, bot, **suç**, submitted (current task), voted (current vote), candidate,
  fired.
- `Mine`: prim, phone (v4 `LinePhoneView`), my transfer log, recorder uses left + my recorded calls (id, other,
  start, leaked), investigation used, my vote ballots, my group in the current/last task, witness targets (PettyCash
  break), my call log (other, start, duration, outgoing).
- `Task`: kind, number (0..2), my group members, my choice, after reveal: group members' choices and my payout; public
  part: ClientBonus taker count / WeekendShift roster.
- `Meeting`: index, failed tasks, vote-able count, dossier lines (typed, with player ids and numbers — no prose),
  investigations, whistles, caught groups, missing total, leaks, suç gained this meeting (after verdict).
- `Final`: crisis result (who got how much), candidates, defender, firings so far with desk amounts and recipients.
- `Results` (everyone, full disclosure): final prim per player, fired flags, winners, every ballot of every vote
  (meetings, crisis, final), every transfer, every task choice, every whistle/investigation/leak, prim and suç logs.
- `LineSessionHistory` (v5): `WeeksPlayed`, `Wins` per seat, `TimesFired` per seat.

## 12. Events (transient feedback)

Keep `PhaseChanged`, call events (`CallIncoming, CallConnected, CallEnded, CallBusy, MissedCall`), `MessageReceived`,
`Presence`, `VoteCast`. Add: `TaskSubmitted`, `TransferReceived` (recipient only), `Whistled`, `Investigated`,
`Leaked`, `BlameAdded`, `CrisisResolved`, `Fired`, `RecordingPlayed`. Host-only telemetry events are not needed in
Phase 1.

## 13. Bots (`LineBot`)

Policies: `Honest` (share, leave, volunteer when needed by a simple count, vote for the most suspicious by suç),
`Greedy` (take, take, skip; whistle when witness; investigate when a partner took), `Mixed` (seeded per decision).
Bots must reach `Results` in every phase without stalling; they do not call or use the recorder (Phase 3).

## 14. Codec

Rewrite `LineCodec` for the v5 contract types, reusing the v4 primitives (header with magic and version, LEB128 counts,
validated enum bytes, size limits). Every `[Serializable]` `Line*` contract class needs a writer/reader
(`LineCodecTests` enforces this by reflection).

## 15. Tests (NUnit, Unity-free via `Tools/CoreTests`)

At minimum: phase flow for 3/4/5/6 players including skipped vote and skipped Meeting 2; each task's payouts
(pair and trio); pairing without repeats; PettyCash caught/whistle/unresolved paths; vote with self-assignment;
investigation guilty/innocent/insufficient prim/wrong group; crisis tie split; candidates with ties; final vote,
revote, tie chain, payout to voters, second firing at 5–6; transfers privacy and limits; recorder/leak rules and
pick timing; privacy (no other player's prim/choices/votes/transfers/recorder marks before reveal); determinism (two
runs, same hash); codec round-trip for every contract type; bots finish 1,000 seeded matches per head count.

## 16. Out of scope for Phase 1

Unity scripts (`Game/Assets/Game/Scripts/**`), UI, localisation, ProjectBuilder. Phase 1 may leave the Unity
project uncompilable. **Inside Core**, `Core/Telemetry` and `Core/Audio` must still compile with the new contracts:
disable/remove the v4 `LineTelemetryObserver*` and any v4-enum dependencies (keep consent, privacy, sinks, catalog
infrastructure), and remap `MusicCuePlanner` to v5 phases. `Tools/TelemetryTests` and `Tools/stubs` may be adapted
or left failing with a note.

## 17. Implementation notes (Phase 1)

Interpretations chosen where this spec was silent or ambiguous. The code and tests follow these; change them here first.

- **Day / segments.** `Day` 0 = tasks 1–2 and meeting 1, 1 = task 3 and meeting 2, 2 = crisis and final (also when
  meeting 2 is skipped). The meeting view's `Index` uses the same numbers; the crisis/final segment is "meeting 2"
  for recording picks (cleared when it starts and again at `FinalDiscussion`).
- **Public suç timing.** Petty Cash catches (+2) and whistles (+3) change suç when meeting 1 opens (with their dossier
  lines), not at the reveal/break, so the public suç board does not announce them early. The whistle's prim effects
  (thief −3 removed, whistler +2) apply immediately and privately; thief and whistler get a `Whistled` event.
- **Defaults.** A choice submitted before a disconnect stands; only players who never submitted take the default.
- **Investigation.** A target can be investigated once (a trio-mate investigating the same target gets
  `AlreadyResolved`), so a taker is never punished twice. Failure order: `WrongPhase`, `AlreadyUsed`,
  `InvalidTarget`/`Self`, `NotEligible` (other group), `AlreadyResolved`, `InsufficientFunds`. Meeting 1 lists Client
  Bonus as failed whenever someone took; it is vote-able only while an unexposed taker remains.
- **Ballots.** Meeting: `A` = ballot index, `Target = -1` clears a ballot. Crisis/final: `A` must be −1 or 0. Ballots
  already cast by a player who later disconnects still count; only empty ballots of disconnected players are dropped
  (meeting, crisis). Final: empty = abstain for everyone.
- **Fired players** have no ballot in the second firing (`LineFailure.Fired`); they may still chat and text.
- **Crisis.** No ballots at all ⇒ no crisis suç. A tie whose floor share is 0 (5+ tied) adds nothing.
- **Firings.** `FiringsTotal = min(FiringsFor(N), candidates)`. The desk goes to the voters for the fired player in the
  deciding vote (the revote when there was one, also when the tie chain decided), in seat order; the remainder is
  removed. With one remaining candidate the second vote still runs and that candidate is fired.
- **Leaks.** `PlayAt = max(now + LeakDelay (0.5 s), previous pick's PlayAt + Seconds)`. If both parties recorded a call,
  the first leak wins (`AlreadyResolved` for the second); each recorder still spent a use.
- **Messages.** v4's per-block limits became per segment (reset when `Day` changes): 60 texts, 30 public chat lines,
  1.5 s between messages of either kind. Texts are open Intro…FinalVerdict.
- **Grouping.** Shuffle score = 100 × repeated pairs + overlap with the previous trio; stop at score 0. Five players
  always repeat at least one pair (a trio plus a pair cannot avoid all four task-1 pairs), three players always repeat
  the trio. `ForceTasks` slots must be (pair, pair, everyone) and distinct; the whistle window is the break after
  whichever slot is Petty Cash.
- **Early finish** needs at least one connected player and uses `PhaseEndReason.TimerShortened`. The abandon timer is
  `LineConfig.AbandonSeconds` (60). An abandoned match has no `Result`; `NextHistory()` then returns the input history.
- **Names.** `LinePhase.Results` replaces `WeekResults`; `LineWeekResult`/`WeekId` keep their v4 names. Removed:
  `WeekLength`, `LineManagerIds`, `LineIds.Manager/Nobody`, `LineSchedule`.
- **Bots.** Honest bots also whistle as witnesses (otherwise nobody ever whistles on a greedy thief); Honest volunteers
  are the cleanest `Needed + 1` connected players by suç (ties by seat). All bots vote for a culprit they saw take,
  else the highest suç (seeded tie-break). `LineBot.ThinkSeconds`/`WakeAt` let simulations add a per-phase delay.
- **Telemetry.** The v4 observer is gone; its catalog rows stay as `pending_integration` with frozen v4 value lists
  (no v4 enum dependency) so the analyzer and service tests keep working; `lobby_created`/`match_started` lost the
  `length`/`manager` params; `HostTelemetryCoordinator` per-match hooks are no-ops and `LobbyCreated` lost its
  `WeekLength` argument. Docs regenerated (`docs/analytics/event_catalog.json`, `docs/ANALYTICS_EVENT_CATALOG.md`).
- **Known design weakness (rules as written).** Transfers stay open through `FinalVote`, so a candidate can empty the
  desk to an ally before the verdict and the voters get nothing. Not changed in Phase 1.
