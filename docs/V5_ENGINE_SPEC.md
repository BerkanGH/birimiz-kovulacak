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
- `LineProtocol.Version = 5`, `RulesetVersion = "v5.0-kayit"`, `ConfigVersion = 5`. Players 3–6. (Yaramazlıklar, §19: wire 6, config 6.
  v5.1 rules, §21: wire 7, config 7, `RulesetVersion = "v5.1-kayit"`. v5.2, §22: wire 8, config 8,
  `RulesetVersion = "v5.2-kayit"`; the lobby needs 4 seats, the engine still plays 3–6.)

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
Meeting 2:      only if task 3 failed: MeetingDossier → MeetingDiscussion [→ MeetingVote → MeetingVerdict]
                (v5.1: no vote, the skippers' suç is automatic, §21)
                (v5.2: no meeting 2 at all; the skippers' suç lands at the shift's reveal, §22;
                 the block above is the legacy flow behind `LineConfig.ShiftMeeting`)
Crisis:         CrisisCalls → CrisisVote → CrisisVerdict
                (v5.2: the candidates declare their prim from CrisisVerdict into FinalDiscussion, §22)
Final:          FinalDiscussion → FinalDefense (one slot per candidate) → FinalVote → FinalVerdict
                (v5.1: no candidate ⇒ CrisisVerdict goes straight to Results, §21)
                [5–6 players: FinalVote → FinalVerdict again for the second firing]
                (a tie inserts one revote: FinalVote again restricted to the tied candidates, see §9)
Results
```

- `MeetingVote`/`MeetingVerdict` are skipped when the meeting has zero vote-able failures (§7). Meeting 2 is skipped
  entirely when task 3 succeeded (v5.2: always, §22).
- Prototype task order is fixed: task 1 = `ClientBonus`, task 2 = `PettyCash`, task 3 = `WeekendShift`.
  `LineSetup.ForceTasks` may override for tests; keep a `TaskKind` enum (`None, ClientBonus, PettyCash,
  WeekendShift`) so more tasks can be added.
- Durations (seconds, *config* `Durations`): Intro 20, TaskPlay 60, TaskReveal 8, Break 30, MeetingDossier 15
  (+ 8 per leak queued during it is **not** added; leaks play during discussion too), MeetingDiscussion 50,
  MeetingVote 20, MeetingVerdict 6, CrisisCalls 75, CrisisVote 20, CrisisVerdict 8, FinalDiscussion 40,
  FinalDefense 15 per candidate, FinalVote 25, FinalRevote 15, FinalVerdict 10, Results 45, Ring 12.
  v5.2: `LineSetup.TalkTime` scales MeetingDiscussion, CrisisCalls, FinalDiscussion and FinalDefense (§22).
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
- Payout (*config*): pair — SS 3/3 (v5.0: 2/2), ST taker 5 / sharer 0, TT 1/1. Trio — no takers 3 each (v5.0: 2);
  exactly one taker 5, others 0; two or more takers 1 each.
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
  group, target is the sole taker). First accepted whistle per theft wins: the taker **keeps** the 3 prim (*config*
  `CashWhistledKeeps`; v5.0: lost it, min with balance, removed from the game), +3 suç (*config* `CashWhistleBlame`);
  the whistler gets nothing (*config* `CashWhistleReward` 0; v5.0: +2). Resolved. Public in Meeting 1's dossier,
  including the whistler's name.
- Failure: the task **fails** (vote-able) if any sole-taker theft is still unresolved at Meeting 1. The dossier shows
  the total missing prim from unresolved thefts, never names.

### 5.3 WeekendShift (task 3, everyone)

- Needed volunteers K = ceil(N / 2) (3→2, 4→2, 5→3, 6→3).
- Choice: `Skip 0` (default) / `Volunteer 1`.
- Non-volunteers +2 prim (*config* `ShiftSkipPay`) when the shift is staffed; when it is not, 0 (*config*
  `ShiftFailSkipPay`; v5.0: 2) and +2 suç each (*config* `ShiftFailSkipBlame`; v5.0: 0): at the reveal in v5.2
  (§22), when Meeting 2 opens with *config* `ShiftMeeting` (v5.1).
  Volunteers 0 prim and −1 suç at the reveal, staffed or not, never below 0 (*config* `ShiftVolunteerClean`; v5.0: 0).
- Reveal: the roster (who volunteered) is **public**.
- Failure: volunteers < K.

## 6. Prim and suç bookkeeping

- Start prim 6 (*config* `StartPrim`), start suç 0. Prim never below 0. Suç never below 0.
- Every prim change and suç change is logged internally with reason (task, whistle, investigation, vote, crisis,
  transfer, firing) for the final reveal.

## 7. Meetings (1 and 2)

v5.2 holds one meeting (index 0); meeting 2 below exists only with *config* `ShiftMeeting` (§22).

- **Dossier content** (public): failed tasks of this meeting; ClientBonus taker count; PettyCash caught groups,
  whistles (who whistled whom) and unresolved missing total; WeekendShift roster; investigations and leaks as they
  happen; current suç board.
- **Vote-able failures** (counted when `MeetingVote` starts): Meeting 1 — ClientBonus counts 1 if any taker is still
  unexposed; PettyCash counts 1 if any sole-taker theft is unresolved. Meeting 2 — WeekendShift counts 1 only with
  *config* `ShiftFailVote` (v5.0); v5.1: 0, set when Meeting 2 opens (the view shows "no vote" from the start).
  Zero ⇒ skip vote and verdict.
- **Blame vote**: each connected player has V = vote-able failures ballots. `Vote` (target, A = ballot index 0..V-1)
  sets/changes one ballot; target must be another seated player. When the phase ends every **connected** player's
  empty ballot is assigned to themselves. Each ballot = +1 suç (*config* `BlamePerVote`) to its target. Disconnected
  players' empty ballots are dropped.
- Ballots are secret until `Results`; during the vote views show only who has finished voting. `MeetingVerdict` shows
  suç received per player in this meeting.

## 8. Investigation (soruşturma)

- One per player per match. v5.1: free (*config* `InvestigationCost` 0 and `InvestigationRefund` 0; v5.0: costs 4,
  paid and removed in every case, guilty target refunds 3).
- Verb `Investigate` (target) in Meeting 1 `MeetingDossier`/`MeetingDiscussion` only. Valid when actor and target
  were in the same ClientBonus group and actor can pay; v5.1 (*config* `InvestigationSharerOnly`): **only an actor who
  shared, only against a target who took** (else `NotEligible`; `InvestigationTargets` lists only those).
- Result is public immediately: the target's ClientBonus choice.
  - Target chose Take: target +4 suç (*config* `InvestigationGuiltyBlame`; v5.0: 3), keeps its prim (v5.0: paid the
    refund). That taker is now exposed.
  - Target chose Share (v5.0 only; impossible with `InvestigationSharerOnly`): investigator +3 suç (*config*
    `InvestigationFalseBlame`).

## 9. Crisis and final

- **CrisisCalls**: calls and transfers open, everyone else silent (1:1 voice only).
- **CrisisVote**: each connected player votes one other player (`Vote`, target). Empty ⇒ self. The crisis blame 4
  (*config* `CrisisBlame`) goes to the plurality; a tie splits it equally among the tied (floor; remainder dropped).
- **Candidates** (fixed at `CrisisVerdict`): top `CandidatesFor(N)` by suç (*config*: 3–4 players ⇒ 2, 5–6 ⇒ 3)
  among the players with at least `CandidateMinBlame` suç (v5.1: 1, so 0 suç is never a candidate; v5.0: 0);
  everyone tied with the last included candidate is also a candidate. Fewer qualify ⇒ those; none ⇒ no final (§21).
- **Firings**: `FiringsFor(N)` (*config*: 3–4 ⇒ 1, 5–6 ⇒ 2), sequential.
- **Declarations** (v5.2, §22): every candidate may `Declare` a prim amount once, sealed until all are revealed
  together in `FinalDiscussion`; information only.
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
- `Final`: crisis result (who got how much), candidates, defender, firings so far with desk amounts and recipients
  (v5.2: the declaration window, my sealed declaration, the revealed declarations, the fired desk's declaration; §22).
- `Results` (everyone, full disclosure): final prim per player, fired flags, winners, every ballot of every vote
  (meetings, crisis, final), every transfer, every task choice, every whistle/investigation/leak, prim and suç logs.
- `LineSessionHistory` (v5): `WeeksPlayed`, `Wins` per seat, `TimesFired` per seat.

## 12. Events (transient feedback)

Keep `PhaseChanged`, call events (`CallIncoming, CallConnected, CallEnded, CallBusy, MissedCall`), `MessageReceived`,
`Presence`, `VoteCast`. Add: `TaskSubmitted`, `TransferReceived` (recipient only), `Whistled`, `Investigated`,
`Leaked`, `BlameAdded`, `CrisisResolved`, `Fired`, `RecordingPlayed`. Host-only telemetry events are not needed in
Phase 1. (v5.1: `BlameAdded.A` is negative for a volunteer's cleaning, `BlameReason.ShiftClean`; `Whistled.A` = 0 when
the thief keeps the cash. v5.2: `DeclarationsRevealed`, A = declarations made, B = candidates.)

## 13. Bots (`LineBot`)

Policies: `Honest` (share, leave, volunteer when needed by a simple count, vote for the most suspicious by suç),
`Greedy` (take, take, skip; whistle when witness; investigate when a partner took), `Mixed` (seeded per decision).
v5.1 (§21): whistles and investigations follow the silence deals (`LineBotPunishStyle.Deals`); the rule above is
`LineBotPunishStyle.Legacy` (LineSim `--rules old`).
Bots must reach `Results` in every phase without stalling; they do not call or use the recorder (Phase 3).
Since 2026-11-15 every policy votes for the rival it estimates to be the richest (`LineBotVoteStyle.Richest`, §20); the
suç rule above is kept as `LineBotVoteStyle.MostBlame`. v5.2 (§22): a candidate bot declares its prim (Honest the truth,
Greedy an understatement, Mixed on a seeded coin); declarations never change a bot's vote.

## 14. Codec

Rewrite `LineCodec` for the v5 contract types, reusing the v4 primitives (header with magic and version, LEB128 counts,
validated enum bytes, size limits). Every `[Serializable]` `Line*` contract class needs a writer/reader
(`LineCodecTests` enforces this by reflection).

## 15. Tests (NUnit, Unity-free via `Tools/CoreTests`)

At minimum: phase flow for 3/4/5/6 players including skipped vote and skipped Meeting 2; each task's payouts
(pair and trio); pairing without repeats; PettyCash caught/whistle/unresolved paths; vote with self-assignment;
investigation guilty/innocent/insufficient prim/wrong group (v5.1: sharer-only, free, takers blocked; §21);
crisis tie split; candidates with ties; final vote,
revote, tie chain, payout to voters, second firing at 5–6; transfers privacy and limits; recorder/leak rules and
pick timing; privacy (no other player's prim/choices/votes/transfers/recorder marks before reveal); determinism (two
runs, same hash); codec round-trip for every contract type; bots finish 1,000 seeded matches per head count.
The bot simulation `Tools/LineSim` (seeded bot matches per head count and policy mix, `Tools/LineSim/v5-sim.txt`) is
described in §20 (v5.1 numbers §21, v5.2 numbers §22).

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
  (v5.0: thief −3 removed, whistler +2; v5.1: none) apply immediately and privately; thief and whistler get a
  `Whistled` event. v5.1: a volunteer's cleaning applies at the shift's reveal, the skippers' suç of a short shift
  when meeting 2 opens (with its `ShiftSkipBlame` dossier line).
- **Defaults.** A choice submitted before a disconnect stands; only players who never submitted take the default.
- **Investigation.** A target can be investigated once (a trio-mate investigating the same target gets
  `AlreadyResolved`), so a taker is never punished twice. Failure order: `WrongPhase`, `AlreadyUsed`,
  `InvalidTarget`/`Self`, `NotEligible` (other group; v5.1 also: the actor took or the target shared),
  `AlreadyResolved`, `InsufficientFunds`. Meeting 1 lists Client
  Bonus as failed whenever someone took; it is vote-able only while an unexposed taker remains.
- **Ballots.** Meeting: `A` = ballot index, `Target = -1` clears a ballot. Crisis/final: `A` must be −1 or 0. Ballots
  already cast by a player who later disconnects still count; only empty ballots of disconnected players are dropped
  (meeting, crisis). Final: empty = abstain for everyone.
- **Fired players** have no ballot in the second firing (`LineFailure.Fired`); they may still chat and text.
- **Crisis.** No ballots at all ⇒ no crisis suç. A tie whose floor share is 0 (5+ tied) adds nothing.
- **Firings.** `FiringsTotal = min(FiringsFor(N), candidates)` (v5.1: candidates need suç, so this can now be less
  than planned, and 0). The desk goes to the voters for the fired player in the
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
- **Bots.** (v5.0, now `LineBotPunishStyle.Legacy`; v5.1 silence deals in §21.) Honest bots also whistle as witnesses (otherwise nobody ever whistles on a greedy thief); Honest volunteers
  are the cleanest `Needed + 1` connected players by suç (ties by seat). All bots vote for a culprit they saw take,
  else the highest suç (seeded tie-break; since §20 this is `LineBotVoteStyle.MostBlame`, the default votes for the
  estimated richest). `LineBot.ThinkSeconds`/`WakeAt` let simulations add a per-phase delay.
- **Telemetry.** The v4 observer is gone; its catalog rows stay as `pending_integration` with frozen v4 value lists
  (no v4 enum dependency) so the analyzer and service tests keep working; `lobby_created`/`match_started` lost the
  `length`/`manager` params; `HostTelemetryCoordinator` per-match hooks are no-ops and `LobbyCreated` lost its
  `WeekLength` argument. Docs regenerated (`docs/analytics/event_catalog.json`, `docs/ANALYTICS_EVENT_CATALOG.md`).
- **Known design weakness (rules as written).** Transfers stay open through `FinalVote`, so a candidate can empty the
  desk to an ally before the verdict and the voters get nothing. Not changed in Phase 1.

## 18. Post-Phase-1 rule change: sealed desks (2026-11-11, approved by the user)

Once candidates are fixed (`CrisisVerdict`), a candidate's outgoing transfers fail with `LineFailure.DeskSealed`.
Candidates may still receive prim (vote buying stays possible). Reason: LineSim/review showed a candidate could empty
their desk to an ally during `FinalVote`, so voters received nothing.

## 19. Yaramazlıklar: meeting room pranks (2026-11-13, approved by the user)

Design: `docs/V5_YARAMAZLIKLAR.md` (Turkish). Code: `Engine/LineEngine.Pranks.cs`, bots `Engine/LineBotTalker.Pranks.cs`,
tests `Tests/Line/LinePrankTests.cs`. Pranks never block voice, votes, the defence order or any other rule; their only
effects are burned prim and the tutanak (+suç) of whoever overflows the manager's patience.

**Verb.** `LineVerb.Prank` (15): `A` = `PrankKind` (`Coffee 1, Plane 2, PostIt 3, PhoneRing 4, Slurp 5, StandUp 6,
Flatter 7, Cake 8`), `Target` = a colleague (`-1` for Slurp and Flatter; optional for StandUp), `B` = post-it word
`0..4` (`YALANCI, HIRSIZ?, MASUM, ZENGİN, SESSİZ`; `LinePranks.WordId`), `Text` = the plane's note (sanitised like
`SendText`, `PlaneNoteMaxChars` 60), `C` = optional guard: the patience the sender saw (`-1` = no check).

**Numbers** (*config*, `LineConfig`): `PranksEnabled` true, `PatienceCapacity` 6, `DefencePatienceMultiplier` 2,
`TutanakBlame` 1; cost / patience / per session: Coffee 2 / 3 / 1, Plane 1 / 2 / 1, PostIt 1 / 2 / 1, PhoneRing 2 / 3
(charged to the target) / 1, Slurp free / 1 / cooldown `SlurpCooldown` 10 s, StandUp free / 1 / `StandUpCooldown` 10 s,
Flatter 1 / −`FlatterRelief` 2 / 1, Cake free / none / `CakeCooldown` 3 s and `CakeMax` 5 per match.
`LineSetup.PranksEnabled` (lobby option "Yaramazlıklar açık", default on) and the config flag must both be on.

**Sessions** (`LinePranks.SessionOf`): meeting 1 (Day 0 meeting phases) = 0, meeting 2 = 1, the crisis vote
(`CrisisVote`, `CrisisVerdict`) = 2, the final (`FinalDiscussion` .. every `FinalVerdict`) = 3, `Results` = 4 (cake only).
Office phases (and `CrisisCalls`) have none. A new session resets the patience to 0, clears the post-its and the
per-session uses; coffee stains stay for the match; cooldowns are host times and are not reset.

**Validation order.** `NotAvailable` (off), `BadParam` (kind), `WrongPhase` (no session; cake outside Results; anything
but cake in Results), `Fired` (fired actor before Results), target (`InvalidTarget` / `Self`; a target given to Slurp or
Flatter is `InvalidTarget`), parameters (`BadParam` word, `EmptyText` / `TooLong` note), then the target-independent block
that the view also shows per option: `DeskSealed` (a candidate after `CrisisVerdict` starts may only do free pranks),
`AlreadyUsed` (session limit), `LimitReached` (cakes), `RateLimited` (cooldown), `InsufficientFunds`; last the guard
`PatienceChanged` (25) when `C ≥ 0` and differs from the patience (not for the cake).

**Effects.** The cost is burned (`PrimReason.Prank`, Other = target; prim never below 0). Coffee adds a stain to the
target, a post-it replaces the target's word for the session, a cake adds a cake to the target (Results). Outside Results
the patience cost (×2 during `FinalDefense`, not for Flatter) is charged to the *culprit* (the actor; the target for a
phone ring). Reaching the capacity is a tutanak: `+TutanakBlame` suç to the culprit at once (`BlameReason.Tutanak`, on the
board; during the crisis vote it can change the candidates, in the final it only counts in the "higher suç" tie-break),
the patience back to 0. Flattering lowers the patience by 2, never below 0. Levels (`LinePranks.Level`): thirds of the
capacity, 0–1 calm, 2–3 frowning, 4–5 red. No randomness is used.

**Privacy.** Everything is public except: a phone ring's actor (the view's log and the events show `-1` to everyone but
the actor until Results), and the plane's note (only in the target's and the sender's view, never revealed, never logged).

**View.** `LineFeaturesView.PranksEnabled / PranksOpen`; `LineView.Pranks` (`LinePranksView`, null when off): `Session`,
`Patience`, `Level`, `Multiplier`, per seat id `PostIts` / `Stains` / `Cakes`, my `Options` (one per kind: cost, current
patience, uses left, `ReadyAt`, `Block` = the reason it is closed right now) and the match's `Log`
(`LinePrankEntryView`: index, kind, actor, target, word, note, session, phase, time, cost, patience, patience after,
culprit, tutanak). At Results the log is the full disclosure of the pranks (with `PrimLog` / `BlameLog`).

**Events.** `Prank` (19: Actor, Target, A = kind, B = word, C = log index; per-player audiences for a phone ring),
`Patience` (20: A = patience now, B = level now, C = level before; Actor = the prank's actor or -1, Target = culprit),
`Tutanak` (21: Target = culprit, A = kind, B = suç, Actor or -1) plus the usual `BlameAdded`. Flattering is the `Prank`
event with A = Flatter.

**Wire.** `LineProtocol.Version` 6 and `LineConfig.CurrentVersion` 6 (new verb, failure, event, reason values; the
pranks view; the config numbers). `RulesetVersion` stays `v5.0-kayit` (pranks are a lobby option like recordings).
`LineCodec.HashResult` writes its own frozen header (`ResultHashSchema` = 5) so outcomes that did not change keep their
hashes; LineSim output is identical to the pre-prank engine.

**Bots** (`LineBotTalker`, never `LineBot`, only from the bot's own view): Honest slurps and flatters when the manager is
red and never targets anyone; Greedy throws coffee at whoever accused it in the chat, whistled / investigated it or
leaked its call when it has prim to spare (≥ cost + 6) and rings its vote target's phone when the manager is red (the only
prank a bot makes that overflows the patience: the tutanak lands on the target); Mixed sticks a "YALANCI" (accuser) or
"HIRSIZ?" (known culprit) post-it or sends a plane with a line-token note; everyone sometimes cakes a winner. Never a
prank that would bring the bot a tutanak, never prim from a sealed desk, at most two per session, none during the
votes, moments staggered by seat order, the guard always sent. Reactions to pranks on the bot are lines out loud
(`BotTalkOutput.Shouts`, presentation only). At Results each bot logs `bottalk prank …` per prank (cakes as they land) and
`bottalk pranks …`; the host logs `host: pranks …` and `host: prank …`; `Tools/scripts/verify_logs.py` checks them.

## 20. Bots vote to win (2026-11-15, requested by the user)

No rule changed. Prim is the score, the richest player wins and a fired player's desk goes to the players who voted for
them, so bots now vote like players who want to win. Tasks (Honest shares / volunteers, Greedy takes / skips, Mixed
50/50), whistles and investigations are unchanged. Code: `Engine/LineBot.cs` (`LineBotVoteStyle`), `Engine/LineBotLedger.cs`,
talk `Engine/LineBotTalker.cs` (`docs/V5_BOT_TALK.md` §6, §6.1, §7), tests `Tests/Line/LineBotVoteTests.cs`.

**Vote rule.** `LineBot.VoteStyle` (instance property, default `Richest`): every policy fills every ballot (meeting suç
ballots, the crisis vote, final votes and revotes) with the option whose *estimated* prim plus a fixed per-(bot, target)
noise is highest. The noise is a hash of the seat, the target and the bot's seed in [−0.4, +0.4) prim, so bots with the
same facts disagree a little; exact ties take one `LineRandom.Index` draw, the same draw count as the MostBlame
tie-break, so the bot's random stream advances identically in both styles. `MostBlame` is the Phase 1 rule (a culprit
the bot saw take, else the highest suç): same code path, same random draws, same decisions as before (LineSim proof
below).

**The ledger** (`LineBotLedger`, one per bot): balances are hidden, so the bot estimates them from its own views only.
`Ingest(view)` records facts (idempotent; ignores views of other seats; a new week resets it) and is called on every
`LineBot.Next` (and by the previews); it remembers facts across phases because the meeting dossier is per meeting.
`Estimate(config)` per seat: start at `StartPrim`; my own balance exact; fired = 0; Client Bonus: my group exact, the
others the expected payout from the public takers count (unknown takers spread over the colleagues whose choice is
unknown, weight 2.5 for a known Petty Cash thief else 1; an investigation's result fixes the target's choice; groups
I was not in are valued with the pair payouts); Petty Cash: caught = 0, a whistled thief nets 0, the whistler
+`CashWhistleReward`, my group exact, the dossier's unresolved thefts (minus my group's open one) spread over the
colleagues who could have taken (not caught, whistled or a whistler) with weight 0.5 + their Client Bonus take
probability (0.3 when unknown); Weekend Shift: `ShiftSkipPay` to everyone off the public roster; investigations:
investigator −cost (+refund when guilty), guilty target −refund; desk shares of the firings so far; my transfers (I
sent X to j: j +X; j sent me X: j −X); pranks in the public log: the burned prim (`LinePrankEntryView.Cost`, i.e.
`LineConfig.PrankCost`) from a known actor (a phone ring's actor is hidden until Results: skipped). Never below 0.

**Previews and announcements.** `PlannedVote` runs the same estimate and the same random draws on a copy of the random
state, so with the same facts it is exactly the vote the bot will cast (tested in every phase before the vote). New facts
can still arrive between a preview and the ballot (a transfer, a prank, a desk share), so an announced vote is frozen:
when the talker builds a `Vote` / `NotVote` claim (Honest and Greedy crisis lines, the final vote lines) it calls
`LineBot.CommitVote(view, final, target)` with the target it built the line from. From then on the bot casts that
target for that vote (crisis; final round r, its revote too while the target is one of the tied), the tie-break draw
still happens, and `PlannedVote` returns it. The first announcement of a vote wins; MostBlame bots ignore it. So an
Honest vote line is never contradicted by the ballot and every claim stays consistent with the results
(`verify_logs.py`: "Honest 0 lies", no `ok=no`); when an Honest bot's announced target is no longer its estimated richest,
its later money lines switch to wording that does not claim it ("I want to win too", "I've said where my vote goes").
Unannounced statements (the meeting chat's wealth suspicion) are said
with the facts of that moment and are not commitments.

**Talk API.** `LineBot.EstimatedPrim(view)` (the estimate per seated player) and `LineBot.RichestRival(view, options,
countMe)` (the bot's belief, same weighing as its votes, lowest seat on exact ties; with `countMe` its own exact balance
competes, so Honest only says "X is the richest" when it believes it). Both learn from the view but decide nothing.
At Results each talker logs every ballot it cast: `bottalk vote seat policy style ballot round revote index target
target_prim richest richest_prim hit` (true balances at the start of that vote, from `PrimLog`); `verify_logs.py`
reports the hit rate per policy and ballot kind.

**Wire.** No change: `LineProtocol.Version` 6, `LineConfig.CurrentVersion` 6, `RulesetVersion` `v5.0-kayit`; no codec
change (bots, ledgers and commitments are local to the bot's process).

**LineSim** (`Tools/LineSim/Program.cs`; `dotnet run -c Release --project Tools/LineSim/LineSim.csproj -- --matches 1000
--output Tools/LineSim/v5-sim.txt` writes the committed report). Flags: `--matches N --players all|3..6 --mix
all|half|one-greedy|mixed|lobby --vote richest|blame --think s --seed N --output path`; `--vote richest` (default) is the
game's bots, `blame` the old rule. Mixes: `half` (each seat Honest or Greedy 50/50), `one-greedy`, `mixed` (all Mixed),
`lobby` (seat % 3 Honest / Greedy / Mixed like the lobby, seats shuffled per match). Per cell, besides the prim / suç /
firing lines: `Votes:` fired = richest alive / richest candidate / suç leader (true balances at the start of each final
round), crisis suç to the richest / the suç leader, final vote top share (concentration); `Estimate:` top-hit (each
bot's top estimated rival is truly the richest, at the start of each final round), mean absolute error, winner prim,
gap to the second, shared wins. Proof that only voting changed: LineSim built from 13aa1b1 and the new LineSim with
`--vote blame` (300 matches, seed 7000, think 15 and 0, each of half / one-greedy / mixed) give identical result
aggregate hashes in all 24 cells, e.g. think 15 half 3–6p `8092D555377C4DB1 663D2D5CA20E5E32 47F932C0375ED366
0A9C0581F8442085`.

Win share per seat, 1000 matches per cell, seeds 5000..5999, think 15 (`v5-sim.txt`; "blame" = the same run with
`--vote blame`):

| Mix | Players | Richest: win share H / G / M | Fired = richest alive | Estimate top-hit | Blame: win share H / G / M | Blame: fired = richest |
|---|---|---|---|---|---|---|
| lobby | 3 | 39.2 / 28.4 / 32.4 | 91.2% | 100% | 62.6 / 6.4 / 31.0 | 42.3% |
| lobby | 4 | 23.1 / 8.4 / 45.5 | 86.1% | 91.9% | 22.0 / 15.4 / 40.5 | 61.3% |
| lobby | 5 | 35.9 / 5.3 / 17.6 | 65.0% | 80.1% | 17.3 / 18.3 / 28.8 | 15.5% |
| lobby | 6 | 22.2 / 9.9 / 17.9 | 69.8% | 81.9% | 10.8 / 20.8 / 18.4 | 22.6% |
| half | 3 | 33.2 / 33.4 / – | 100% | 100% | 58.5 / 8.4 / – | 58.2% |
| half | 4 | 34.7 / 15.3 / – | 86.4% | 93.8% | 22.3 / 27.7 / – | 57.6% |
| half | 5 | 35.9 / 4.0 / – | 66.4% | 83.6% | 24.7 / 15.2 / – | 32.3% |
| half | 6 | 26.5 / 6.7 / – | 77.0% | 94.8% | 13.3 / 20.0 / – | 42.2% |
| one-greedy | 3–6 | Greedy fired 100%, wins 0 | 100 / 100 / 99.8 / 50.0% | 100 / 100 / 100 / 72.7% | same | 100 / 100 / 68.2 / 89.7% |

Notes: with 3 players everything is visible (one trio), so the estimate is exact. "Fired = richest alive" is lower at
5–6 players because only the candidates (top suç) can be fired; "richest candidate" is 88–96% there. 6-player
one-greedy is degenerate (five identical Honest seats; the estimate spreads the single unknown Client Bonus taker over
several colleagues, so the Honest whistler can look richer than the thief; the second firing then hits a tied Honest).
The scratch prototype ingested views only at phase changes and missed facts that appear mid-phase (e.g. an
investigation late in a meeting without a vote); ingesting every view makes the 3-player estimate exact (prototype
3p lobby H / M / G 49 / 31 / 20 at seeds 9000.., the port 44 / 30 / 26 there); 4–6 players match the prototype within
about one point.

## 21. v5.1 rules: silence deals (2026-11-15, decided by the user)

Design (`V5_DESIGN.md` §6, §8.2, §12, with the rejected alternatives): prim = money = score, the richest wins; suç =
risk (the candidates are the top suç, firing is by vote among them, the fired desk goes to their voters). A greedy move
keeps its money and pays in suç risk. Punishing (investigation, whistle) is free and earns nothing; what makes it a
decision is the silence deal ("Sus, sana 1 prim göndereyim"), made on the phone with non-binding transfers.

**Rules** (every number is in `LineConfig`; the defaults are v5.1, each field's comment names its v5.0 value):

| Area | v5.1 | Config (v5.1 / v5.0) |
|---|---|---|
| Client Bonus | pair SS 3/3, ST 5/0, TT 1/1; trio: nobody 3 each, one taker 5 / others 0, 2+ takers 1 each. Takers line and failure unchanged | `BonusPairShare` 3 / 2, `BonusTrioNone` 3 / 2 |
| Investigation | meeting 1 dossier/discussion, one per player per match; only a sharer, only against a group-mate who took (`NotEligible` otherwise, also two takers both ways); free, no refund, the target keeps the 5; target +4 suç; public dossier line unchanged | `InvestigationSharerOnly` true / false, `InvestigationCost` 0 / 4, `InvestigationRefund` 0 / 3, `InvestigationGuiltyBlame` 4 / 3 (`InvestigationFalseBlame` 3 only without sharer-only) |
| Petty Cash | sole taker +3 and keeps it when whistled; whistle (break after the task) free, no reward, thief +3 suç, first whistle only; 2+ takers caught (0 prim, +2 suç); unresolved theft: CashMissing, failure, meeting 1 vote | `CashWhistledKeeps` true / false, `CashWhistleReward` 0 / 2 |
| Weekend Shift | K = ceil(N/2); volunteers 0 prim and −1 suç at the reveal (never below 0), staffed or not; staffed: skippers +2, no meeting 2; short: skippers 0 prim and +2 suç when meeting 2 opens, meeting 2 = dossier + discussion, no vote | `ShiftVolunteerClean` 1 / 0, `ShiftSkipPay` 2, `ShiftFailSkipPay` 0 / 2, `ShiftFailSkipBlame` 2 / 0, `ShiftFailVote` false / true |
| Candidates | at least 1 suç (fewer qualify: those); firings = min(firings, candidates); none: no final, Results straight after `CrisisVerdict` | `CandidateMinBlame` 1 / 0 |

Unchanged: ties (shared win), the crisis (4 suç to the plurality), meeting 1 votes (1 suç per ballot), recorder and
leaks, pranks and tutanak, bots' votes (`Richest`), the lobby's bot rotation.

**Engine edge cases.**
- Cleaning: `RemoveBlame` logs a negative `LineBlameChange` (`BlameReason.ShiftClean`) and emits `BlameAdded` with a
  negative `A`; a volunteer at 0 suç gets no entry. The results' blame log still sums to each player's suç.
- Short shift: payouts at the reveal use `ShiftFailSkipPay`; when meeting 2 opens every skipper gets `AddBlame(+2,
  BlameReason.ShiftFailSkip)` (public `BlameAdded` events) and the dossier gets `DossierKind.ShiftSkipBlame` (N1 = suç
  each, N2 = skippers; the skippers are everyone not in `LineMeetingView.Roster`) after `TaskFailed` and `ShiftRoster`.
  `VoteableCount` is 0 from the start of meeting 2, so the views say no vote is coming; the discussion ends with
  `PhaseEndReason.Skipped` into `CrisisCalls`.
- Investigation: failure order `WrongPhase`, `AlreadyUsed`, `InvalidTarget`/`Self`, `NotEligible` (other group, or the
  actor took, or the target shared), `AlreadyResolved` (a trio-mate already investigated that taker),
  `InsufficientFunds` (only with a cost). A trio sharer with two takers picks one (one investigation per match). Prim
  moves only with a configured cost/refund (no `InvestigationCost`/`InvestigationRefund` log entries at 0).
- Whistle: `Whistled.A` = prim taken back, 0 when the thief keeps it; no `WhistleReward` entry at 0.
- No candidate (possible only when the crisis splits 4 suç five or six ways and nobody had suç): `ChooseCandidates`
  leaves the lists empty and `FiringsTotal` 0; `CrisisVerdict` → `Results` (`PhaseEndReason.Skipped`, nobody fired,
  winners = the richest of everyone). `FinalDefense` guards the same way (never a vote without options). Five- and
  six-player matches can now fire 1 (one candidate) instead of 2.

**Versions.** `LineProtocol.Version` 7 (new enum values `BlameReason.ShiftClean/ShiftFailSkip`,
`DossierKind.ShiftSkipBlame`; the codec's enum limits), `LineConfig.CurrentVersion` 7 (the new fields;
`StreamingAssets/Config/line_config.json` regenerated by the builder from `CreateDefault`), `RulesetVersion`
`"v5.1-kayit"` (results, analytics, logs: never pool with `v5.0-kayit`). The result hash keeps its frozen v5.0 header
(`ResultHashSchema` 5) but results carry the ruleset id, so every hash changes.

**Bots** (`LineBot`, `LineBotPunishStyle.Deals`, default; `Legacy` = the v5.0 rule for LineSim `--rules old`).
Task choices and votes are unchanged. Punishments:
- Honest: punishes whenever eligible (the whistle as soon as the break lets it act; the investigation in meeting 1)
  and sends back any silence money the target paid it since that task's `TaskPlay` started, until meeting 1 ends (its
  own `Transfer`; returns are matched in the view by amount and time and re-sent when the engine refused one;
  `Owed(view, target)` is what is still to return).
- Greedy: never punishes unless paid; once paid it keeps the money and punishes on a seeded 50% coin (betrayal). It is
  re-evaluated while the window is open, so money that arrives late can still be betrayed. Greedy always takes, so in
  play it is rarely a victim or a witness.
- Mixed: Honest mode or Greedy mode per colleague on a seeded 50/50 coin (one mode covers the rare colleague who both
  took its Client Bonus share and stole the Petty Cash in front of it: that money cannot be told apart).
- The coins hash the bot's seed, the week, the kind and the target: no draw from the random stream, so previews and the
  v5.0 choices / votes are unaffected; `HonestMode`, `Betrays`, `SilenceMoney`, `WillPunish(view, whistle, target,
  assumePaid)` and `Victim` / `Witness` expose them to the talk. One decision per target and week whichever meeting
  phase it falls in (the v5.0 investigation drew its coin again in the discussion: kept only in `Legacy`).
- `TransferHoldUntil` / `NoteTransfer` keep the bot's returns and the talker's payments one transfer interval apart (a
  `-bot` process's view lags).

**Ledger** (`LineBotLedger`): a whistled thief keeps the take (`CashWhistledKeeps`), the whistler gets
`CashWhistleReward` (0), the skippers of a short roster get `ShiftFailSkipPay` (0), the investigation moves
`InvestigationCost`/`InvestigationRefund` (0); silence money and returns are the bot's own transfers like any other.

**Talk** (`LineBotTalker`, `V5_BOT_TALK.md` §4.1): a Greedy / Mixed taker pays every Client Bonus victim / Petty Cash
witness 1 prim for silence in the break (a "hush" call, a text after a deadline: 85% of the break for the Client Bonus,
55% for Petty Cash); a bot paid by a colleague it could punish answers with what its `LineBot` will do (Honest mode:
"I'm sending it back, I won't keep quiet"; Greedy mode: "my lips are sealed", a lie when the betrayal coin says punish);
a betrayed briber says so in meeting 1. At Results: `bottalk hush seat policy kind to amount via punished returned` and
`bottalk punish seat policy kind target mode paid returned punished by_other spent outcome=none|refused|kept|betrayed`.
Silence money never counts as keeping another promise in the claim checks (payments are matched to results transfers
with a −1..+5 s window, for `-bot` clocks).

**Host log and checks.** At Results the host logs `host: rules week ruleset whistles investigations caught
shift=staffed|short volunteers=V/K cleaned skip_blamed skip_blame candidates firings=F/P transfers`.
`Tools/scripts/verify_logs.py`: the phase grammar allows meeting 2 without a vote and 1..firings final rounds (or none);
fails on a short shift without skipper suç (or a staffed one with it), meeting 2 held for a staffed shift (or skipped
for a short one), firings above the candidates / the plan, an Honest bot keeping silence money or paying for silence,
an Honest-mode bot that did not punish a colleague it could (nobody else did, its one investigation not spent), a
Greedy-mode bot punishing unpaid or returning money; it reports silence payments and deal outcomes per policy.

**UI** (`UI.md` "v5.1 rules on screen"): every rule text with the config's numbers (task cards and rules, how-to pages,
the whistle confirm card, the investigation buttons "(bedava)", the roster sheet and results, meeting 2 "no vote:
skippers +2 suç", the projector's `ShiftSkipBlame` slide, the candidate rule "0 suç olan aday olmaz", the reveal's suç
breakdown by reason).

**Tests.** `LineRulesV51Tests` (cleaning floor 0 and its negative events, a short shift still cleans, 0-suç
exclusion with firings ≤ candidates, fewer qualifiers, the no-candidate path to Results, v5.0 candidates), the task /
meeting tests for the new numbers, sharer-only both ways, the trio sharer, free investigation with no money moving, the
whistled thief keeping the cash, meeting 2 without a vote (and `V50*` variants of the old behaviour through
`LineTest.V50` / `Meeting2Vote`), `LineBotDealTests` (Honest returns and punishes, Mixed's Greedy mode only when paid /
keeps / betrays on its coin, previews equal decisions, no random draw, talking matches: silence paid, Honest returns,
Greedy mode keeps), ledger flows. CoreTests 260 → 278.

**LineSim** (`--rules new|old`, default new; a `Shift:` line per cell: volunteers, suç cleaned, skippers' automatic
suç, candidates per match, matches with fewer firings / none). LineSim's bots never talk, so nobody pays for silence:
Honest punishes whenever eligible, Greedy never, Mixed on its mode coin. **Identity:** `--rules old --matches 300
--seed 7000 --think 15` reproduces every statistic of 337227f's run in all 16 cells; its aggregate hashes differ only by
the ruleset id the results carry, and re-hashing those results stamped `v5.0-kayit` gives all 16 hashes of 337227f
exactly (`FCC048C6F08B155A 1A7A82B4E2059976 …`, checked with a throw-away patch). Win share per seat and firings,
1000 matches per cell, seeds 5000..5999, think 15 (`v5-sim.txt`):

| Mix | Players | Win share H / G / M | Fired % of seats H / G / M | Fired = suç leader | Fired = richest alive | Estimate top-hit | Short shift (meeting 2) | Fewer firings |
|---|---|---|---|---|---|---|---|---|
| lobby | 3 | 48.6 / 3.4 / 48.0 | 2.8 / 93.3 / 3.9 | 95.0% | 100.0% | 100.0% | 47.7% | 0.0% |
| lobby | 4 | 12.5 / 12.1 / 62.9 | 0.0 / 87.9 / 12.1 | 100.0% | 98.3% | 99.2% | 0.0% | 0.0% |
| lobby | 5 | 23.4 / 10.4 / 32.4 | 0.9 / 83.4 / 31.3 | 94.5% | 95.3% | 91.2% | 59.0% | 0.0% |
| lobby | 6 | 10.2 / 17.5 / 22.3 | 0.1 / 72.7 / 27.2 | 93.0% | 86.7% | 83.1% | 40.9% | 0.0% |
| half | 3 | 42.7 / 24.1 / – | 14.7 / 51.8 / – | 85.9% | 100.0% | 100.0% | 50.4% | 0.0% |
| half | 4 | 15.5 / 34.5 / – | 2.7 / 47.4 / – | 100.0% | 100.0% | 100.0% | 30.5% | 0.0% |
| half | 5 | 24.7 / 15.2 / – | 2.0 / 71.0 / – | 93.1% | 98.9% | 93.5% | 48.7% | 18.3% |
| half | 6 | 16.4 / 16.9 / – | 1.0 / 62.9 / – | 93.4% | 86.4% | 89.7% | 33.7% | 9.5% |
| one-greedy | 3 | 50.0 / 0.0 / – | 0.0 / 100.0 / – | 100.0% | 100.0% | 100.0% | 0.0% | 0.0% |
| one-greedy | 4 | 33.3 / 0.0 / – | 0.0 / 100.0 / – | 100.0% | 100.0% | 100.0% | 0.0% | 0.0% |
| one-greedy | 5 | 25.0 / 0.0 / – | 0.0 / 100.0 / – | 100.0% | 100.0% | 100.0% | 0.0% | 100.0% |
| one-greedy | 6 | 20.0 / 0.0 / – | 0.0 / 100.0 / – | 100.0% | 100.0% | 100.0% | 0.0% | 100.0% |

Notes: "Fired = suç leader" counts the final firings whose fired player had the highest suç among the living players
when the round started. Greedy (always takes, never pays in LineSim) is the suç leader almost every match and is fired
72–100%; with one Greedy at 5–6 players only it has suç, so only one is fired ("fewer firings" 100%). 3-player half has
a 50% short shift (two Greedy of three never staff it). The design's numbers from the smart-bot scratch simulation
(timed / mixed play ≈ 2× fair, pure Greedy fired 81–98%, suç leader fired 88/87/63% at 4/5/6 players vs 100/95/91% in
v5.0) are in `V5_DESIGN.md` §6; LineSim's bots are the simple game bots, not that calculating player.

## 22. v5.2: prim declarations, one meeting, talk time, results story (2026-11-24, decided by the user)

Design: `V5_DESIGN.md` §4.1 (one meeting, at least 4 players, talk time), §5 (prim beyanı, ödüller ve manşetler), §8.2
(the shift's consequences at its reveal), §12. Prim and suç numbers are the v5.1 ones; nothing here changes who wins.

### 22.1 Prim declarations (`Declare`)

- Verb `LineVerb.Declare = 16` (A = amount). Candidates only (alive, not fired), once per match, `0..DeclareMax`
  (*config*, 99). *Config* `DeclarationsEnabled` (true); off ⇒ `NotAvailable` and no declaration data anywhere.
- **Window**: from the start of `CrisisVerdict` (the candidates are fixed there) until `FinalDiscussion` start +
  `DeclareWindow`, where `DeclareWindow = max(0, min(DeclareSeconds, FinalDiscussion))` (*config* `DeclareSeconds` 12;
  0 reveals at the start of the discussion). `LineFeaturesView.DeclareOpen` is true for a candidate who may still declare.
- **Failure order**: `NotAvailable` (feature off), `WrongPhase` (outside the window, also after the reveal),
  `NotEligible` (not a candidate), `AlreadyUsed`, `BadParam` (amount out of range).
- **Sealed**: until the reveal a view carries only the viewer's own amount (`LineFinalView.MyDeclaration`, −1 = none)
  and the deadline (`DeclareUntil`); `Declarations` is empty for everyone, and no event tells who has declared.
- **Reveal** (`RevealDeclarations`): all at once, at max(`FinalDiscussion` start, the last candidate's declaration),
  at the deadline at the latest (a `Tick` due time), and in any case before `FinalDefense` starts. It snapshots every
  candidate's real prim (`DeclaredActual`), sets `DeclarationsRevealed`, fills `LineFinalView.Declarations` (one
  `LineDeclarationView {Player, Amount (−1 = none), Actual −1}` per candidate, in candidate order) and emits
  `LineEventType.DeclarationsRevealed = 22` to everyone (A = declarations made, B = candidates). All candidates
  declaring during `CrisisVerdict` still reveals only when `FinalDiscussion` starts.
- **After the reveal**: public for the rest of the match (both firing rounds), never editable.
  `LineFiringView.Declared` (−1 = none) and `LineFiringView.DeclaredActual` (the fired candidate's snapshot; −1 when
  nothing was revealed) ride with the desk, so the opening can stamp it: `LineStory.Truthful(f)` ⇔ `Declared ≥ 0 &&
  Declared == DeclaredActual`. The stamp is judged against the snapshot, not the desk: a candidate cannot send prim but
  still receives it (a share of the first fired desk at 5–6 players, a transfer), and prim that arrives after the reveal
  must not turn a true declaration into a lie, nor let a colleague frame an honest candidate with a late transfer. The
  fired player's own snapshot is the only one a view carries before Results.
- **Results**: `LineWeekResult.Declarations` with `Actual` = the snapshot (declared vs real for every candidate).
- **No mechanical effect** on prim, suç, votes, candidates or the winner.
- No final (no candidate, §21): no window, no reveal, no declarations in the results.

### 22.2 One meeting (`ShiftMeeting` false)

- `TaskReveal` of task 3 always goes to `CrisisCalls` (`PhaseEndReason.Timer`). With *config* `ShiftMeeting = true`
  the v5.1 flow is back (meeting 2 when the shift failed; `Skipped` when it did not).
- The short shift's consequences land when the shift resolves (start of its `TaskReveal`): volunteers are cleaned
  first (−1 suç, §21), then every skipper gets `AddBlame(+ShiftFailSkipBlame, BlameReason.ShiftFailSkip)` (public
  `BlameAdded` events) and 0 prim. The match's rule counters (`skip_blamed`) count them there.
- The dossier lines `ShiftRoster` and `ShiftSkipBlame` (N1 = suç each, N2 = skippers) and the roster
  (`LineMeetingView.Roster`) are attached to the **crisis segment** (meeting view index 2), so clients can show
  "NÖBET CEZASI" while the crisis vote opens. The first meeting's dossier never contains shift lines.
- Texts say "Toplantı" (no number); `PlayText.PhaseTitle` numbers a meeting only when its index is 1 (legacy flow).

### 22.3 Lobby: talk time and four seats

- `LineSetup.TalkTime` / `LineLobbyView.TalkTime`: `LineTalkTime.Short 0 / Normal 1 / Long 2` (default Normal; an
  invalid value fails `Setup` validation). The engine applies `Durations.ScaleTalk(c.TalkFactor(level))` to its own
  copy of the config: MeetingDiscussion, CrisisCalls, FinalDiscussion, FinalDefense × `TalkTimeShort` 0.7 / 1 /
  `TalkTimeLong` 1.5 (*config*). `Engine.Config` is the **effective** config; the host sends that one to the clients
  (`ConfigJson`), so every countdown and rule text agrees. `DeclareWindow` follows the scaled `FinalDiscussion`.
- Host-only option next to the lobby's checkboxes, synced through the lobby view like them; `-talktime
  short|normal|long` for scripted runs.
- `LineProtocol.LobbyMinPlayers = 4`: the lobby's start button needs 4 occupied seats (humans + bots).
  `LineProtocol.MinPlayers` stays 3: the engine, the tests, LineSim and `-expectplayers 3` autostart keep 3 players.

### 22.4 Results story (`LineStory`)

`LineStory.Build(LineWeekResult, LineConfig)` is a pure function of the full disclosure: every client builds the same
story; nothing is sent, nothing changes prim, suç or the winner. For it the results gained `LineWeekResult.Groups`
(`LineGroupView {TaskNumber, Task, Members}` of the pair tasks: who robbed whom) and `LineFiringView.Time`.

**Awards** (`AwardKind`, at most `MaxAwards` 4). Priority = enum order, the rarest story first; a kind's candidates are
ranked (best first); pass 1 gives each kind to its best candidate who has no award yet; kinds whose candidates all
have one already wait as leftovers and fill free places afterwards (so a player gets a second award only when no other
player qualifies for anything). The list is sorted by priority.

| # | Kind (id) | Who | Ranking |
|---|---|---|---|
| 1 | `WrongMan` (wrongman) "Yanlış Adam" | fired with a true declaration (`LineStory.Truthful`: declared = the snapshot) | firing order |
| 2 | `Liar` (liar) "En Pahalı Yalan" | a candidate whose declaration is at least `LieGap` 3 under the snapshot | biggest gap, then richest |
| 3 | `Sellout` (sellout) "Satılmış" | kept (net) prim from a colleague, then whistled / investigated them after a payment, or voted for them in the crisis or a firing vote decided after a payment | most prim kept |
| 4 | `Loyal` (loyal) "Sözünün Eri" | kept (net) prim from a colleague they could have punished (a Client Bonus victim of that taker, or a Petty Cash witness who left the box) and neither punished nor broke the deal | most prim kept |
| 5 | `Robber` (robber) "Soyguncu" | the only taker of a Client Bonus group | most victims, then richest |
| 6 | `CashRat` (cashrat) "Kasa Faresi" | took the Petty Cash with a payout and was not whistled | richest |
| 7 | `Snitch` (snitch) "Ayın İspiyoncusu" | most whistles + investigations (at least 1) | count, then earliest |
| 8 | `Scapegoat` (goat) "Günah Keçisi" | the only receiver of the crisis suç (no award on a split) | – |
| 9 | `BlackBox` (blackbox) "Kara Kutu" | leaked a recording | count, then earliest |
| 10 | `Tutanak` (tutanak) "Tutanak Kralı" | got a prank tutanak | count, then earliest |
| 11 | `Dodger` (dodger) "Nöbet Kaçağı" | skipped a staffed shift with a payout and was not fired | richest |
| 12 | `Clean` (clean) "Temiz Sicil" | ended with 0 suç and was not fired | richest |

Ties end on the lower seat id. "Kept" is sent minus sent back, so an Honest bot returning silence money is neither a
sellout nor loyal. `LineAward {Kind, Player, Other, N1..N3}` carries the numbers the texts need.

**Headlines** (`HeadlineKind`, at most `MaxHeadlines` 5): `DealBroken` (every broken deal, biggest first),
`BonusBetrayal` (every sole Client Bonus taker with the sharers), `Firing` (each firing: desk, declaration, round, the
snapshot the declaration is judged against),
`Crisis` (the receiver, or the split) and `BigTransfer` (the biggest single transfer of at least `BigTransferMin` 2).
The first of every kind is taken in that order, then the second of every kind, and so on; the list is sorted by kind.

`LineStory.AwardsAt(resultsSeconds) = clamp(0.55 · Results, 0, 8 s)`: when the projector's awards slide follows the
ranking; the bots' award lines wait for it.

### 22.5 Versions, codec, hash

- `LineProtocol.Version` 8 (verb 16, event 22, `TalkTime` in the setup and lobby view, the declaration fields and
  types, `Groups`, `LineFiringView.Declared/DeclaredActual/Time`; the codec's enum limits), `LineConfig.CurrentVersion` 8 (`ShiftMeeting`,
  `DeclarationsEnabled`, `DeclareSeconds`, `DeclareMax`, `TalkTimeShort`, `TalkTimeLong`; `line_config.json` regenerated
  by the builder), `RulesetVersion` `"v5.2-kayit"` (never pool with v5.1 / v5.0 data).
- `LineCodec`: writers/readers for `LineDeclarationView` and `LineGroupView`. The **result hash** keeps its frozen v5.0
  field list (`WriteWeekResult(…, hashSchema: true)` skips `Groups`, `LineFiringView.Declared/DeclaredActual/Time`); declarations are
  mixed in after it as (player, amount, actual) only when the result has any. A match without declarations therefore
  hashes exactly like v5.1 apart from the ruleset id.
- Analytics: verb `declare` in the event catalogue (`docs/ANALYTICS_EVENT_CATALOG.md`, `docs/analytics/
  event_catalog.json`).

### 22.6 Bots

- **Declaring** (`LineBot`): in `CrisisVerdict` / `FinalDiscussion` when `Features.DeclareOpen` and not yet declared,
  after the phase's usual think delay (no extra draw from `LineRandom`; the coins hash seed, week, topic and target).
  Honest: its real prim. Greedy: an understatement, `cut = 0.35 + 0.10 · coin`, `claim = max(3, min(round(prim · (1 −
  cut)), prim − 2))`, never above its prim or `DeclareMax` (a Greedy with 3 prim or less ends up telling the truth or
  close to it: there is nothing to hide). Mixed: the truth or the Greedy claim on a seeded 50% coin (`DeclaresTruth`).
  `PlannedDeclaration`, `DeclaredPrim`, `DeclaredAmount` expose it to the talk.
- **Votes ignore declarations**: `LineBotVoteStyle.Richest` still reads the ledger estimate only (a declaration is
  cheap talk; the estimate was right about the richest 83–100% of the time, §20).
- **Talk** (`LineBotTalker`, `V5_BOT_TALK.md` §4.2): a candidate bot states its claim once in its defence
  (`bot.declare.own.*`, `BotClaimKind.Declare`, checked against the amount it declared: never a lie about the
  declaration itself); a bot whose ledger estimate of a candidate is at least 3 above that candidate's claim says so in
  the final discussion (`bot.declare.doubt.*`: at most one doubt per bot, the widest gap, and never about a candidate
  somebody already doubted in the chat; Honest and Greedy always, Mixed on a coin); every award gets one shout
  at Results after `AwardsAt` (`bot.award.<id>`).
- Logs at Results: `bottalk declare seat policy declared had actual truthful`, `bottalk award seat policy key t`.

### 22.7 Host log and checks

- `host: declarations week= enabled= candidates= made= truthful= understated= list=seat:declared/actual
  fired=seat:declared/desk:true|lie|none` (true / lie by the snapshot), `host: awards week= count= list=id:seat`, `host: headlines week= count=
  list=id:seat` (after the `host: rules` line of §21).
- `Tools/scripts/verify_logs.py`: a `v5.2-kayit` match that holds meeting 2 fails; `story` check: the host's
  declarations line is consistent (made ≤ candidates, none when the feature is off), every Honest bot's `bottalk
  declare` is truthful, every bot's declaration is the one the host lists for its seat, at most 4 awards (no kind
  twice) and 5 headlines, a bot says an award line only for an award the host gave its seat. It reports the
  declarations per policy (truthful / understated / none) and the awards and headlines of the match.

### 22.8 Tests

`LineDeclarationTests` (candidates only, once, the range; sealed views until the reveal; the reveal at the start of
the final discussion, at the last declaration and at the deadline, reached within one tick and never past the
discussion; the snapshot and `LineFiringView.Declared` / `DeclaredActual`; prim that arrives after the reveal never turns a true
declaration into a lie; public through the second firing; off by config and without candidates; declaring changes no
prim, suç, vote or winner), `LineRulesV52Tests` (a short shift blames its skippers at
the reveal and the crisis follows, cleaning first and the suç counts for the candidates, a staffed shift, the old
switch keeps the v5.1 and v5.0 flows, talk time scales only the talking phases, is validated and never changes the
outcome), `LineStoryTests` (every award and headline kind, priority, one per player, the next candidate, leftovers,
the caps, the same story whatever the list order, the slide timing, consistency over real bot matches),
`LineBotDeclareTests` (Honest the truth, Greedy understates, Mixed either; no draw from the random stream; talk about
declarations where Honest never lies; award owners say their line at Results), the codec tests for the new types, the
`V51*` variants of the old meeting-2 tests through `LineTest.V51` / `Meeting2Vote`. CoreTests 278 → 337;
TelemetryTests 51, MusicTests 20, VoiceTests 109.

### 22.9 LineSim

`--rules new` (default) plays v5.2 and prints a `Declarations:` and a `Story:` line per cell; `--rules old` sets
`ShiftMeeting = true`, `DeclarationsEnabled = false` and the v5.0 numbers and prints neither. **Identity:** `--rules old
--matches 300 --seed 7000 --think 15` reproduces every statistic line of f4c349e (v5.1 tooling) in all 16 cells; a
throw-away build stamped `v5.1-kayit` reproduces all 16 aggregate hashes (`571936ADCC6B9E66 75F3BFE1F26C8E4A …`). With
the new rules the cells without short shifts are identical to v5.1 line for line; the others differ in match length and
"meeting 2 held" (now 0%), because the think-delay draws of the removed phases shift the random stream.

1000 matches per cell, seeds 5000..5999, think 15 (`v5-sim.txt`). Win shares and firings are the v5.1 table's (§21)
within noise; new columns:

| Mix | Players | To Results (s) | Short shift | Candidates | Truthful declarations | Mean claim of real prim | Fired with a true claim | Awards | Headlines |
|---|---|---|---|---|---|---|---|---|---|
| lobby | 3 | 431 | 47.7% | 1.83 | 24.9% | 6.20 of 9.28 | 4.8% | 3.90 | 2.49 |
| lobby | 4 | 427 | 0.0% | 1.66 | 21.5% | 9.24 of 13.72 | 6.3% | 4.00 | 3.15 |
| lobby | 5 | 493 | 59.0% | 2.85 | 14.9% | 6.61 of 10.23 | 8.5% | 3.99 | 4.07 |
| lobby | 6 | 500 | 40.9% | 3.05 | 19.8% | 7.95 of 11.84 | 14.6% | 3.99 | 4.52 |
| half | 3 | 428 | 50.4% | 1.63 | 16.3% | 6.01 of 9.27 | 21.9% | 3.35 | 2.37 |
| half | 4 | 433 | 30.5% | 1.73 | 3.1% | 7.53 of 12.36 | 5.4% | 3.81 | 3.00 |
| half | 5 | 478 | 48.7% | 2.48 | 2.6% | 6.18 of 10.22 | 2.8% | 3.88 | 3.70 |
| half | 6 | 486 | 33.7% | 2.76 | 1.1% | 7.16 of 11.90 | 1.6% | 3.89 | 4.28 |
| mixed | 3 | 437 | 50.9% | 2.05 | 53.2% | 6.97 of 8.61 | 53.3% | 3.50 | 2.38 |
| mixed | 4 | 450 | 51.1% | 2.17 | 51.2% | 8.63 of 10.71 | 50.2% | 3.86 | 3.06 |
| mixed | 5 | 504 | 68.6% | 3.25 | 50.8% | 7.35 of 9.16 | 51.3% | 3.88 | 3.86 |
| mixed | 6 | 510 | 71.1% | 3.49 | 50.2% | 8.17 of 10.20 | 50.8% | 3.95 | 4.33 |
| one-greedy | 3–6 | 405–410 | 0.0% | 1.00 | 0.0% | ≈ 9.6 of 16.00 | 0.0% | 4.00 | 3.00 |

Notes: every candidate bot declares (100%); Honest is truthful every time (it is rarely a candidate: 14–265 of the
1000-match cells), Greedy never, Mixed about half. "Fired with a true claim" is per firing (the DOĞRU stamp; the
"Yanlış Adam" award). Awards hit the cap of 4 in almost every match: `liar` goes out in 67–100% of matches (any
Greedy / lying Mixed candidate), `snitch` 66–100%, `clean` 31–100%, `robber` 20–100%; `sellout`, `loyal`, `blackbox`
and `tutanak` never appear here because LineSim's bots do not pay, record or prank (the game's talking bots do).
A 4-player bot match reaches Results after ≈ 7–7.5 minutes; with players who use the whole of every phase it is
≈ 10 minutes plus Results.
