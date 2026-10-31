# v4 contract (engine ↔ host ↔ clients)

Rules and numbers: `docs/GAMEPLAY_REDESIGN.md` (normative). Types: `Game/Assets/Game/Core/Line/Contracts/*.cs` (owned by
the lead; request changes, do not edit). Engine, codec, bot, schedule: `Game/Assets/Game/Core/Line/Engine/**`. Rules
tests: `Game/Assets/Game/Tests/Line/**`, run with `dotnet test Tools/CoreTests/Tests/CoreTests.csproj`. Defaults live in
`LineConfig.CreateDefault()`; if this file and the config disagree, the config wins.

## 1. Determinism and time
- Pure C#, `netstandard2.1`, C# 9, no UnityEngine. All randomness from one seeded PRNG (`LineRandom`, SplitMix64); no
  `System.Random`, `Guid`, `DateTime`, dictionary-order or hash-order dependence. Same setup + same `(now, player,
  action)` sequence ⇒ identical views, events, messages and `ResultHash`.
- `now` is host seconds (double), monotonic; `Tick(now)` performs every transition due at or before `now`. Phase ends
  are absolute. `StateVersion` increments on every state change. Idle ticks allocate nothing.
- `MatchId` comes from the setup (host random), is copied to views/results and never influences rules or the hash.

## 2. Match flow
- Setup validation: 3–6 distinct seats 0..5, finite non-negative start time, known length, non-null history lists,
  valid config (`ValidateConfig`), and `LineSchedule.Validate` (pool sufficiency) — otherwise `ArgumentException`.
- Start: manager = `Setup.ManagerProfileId`, else `History.NextManager`, else seeded pick ≠ last past manager.
  Objectives (§7), tricks (distinct types while ≤ 5 players; the sixth gets a seeded duplicate), task schedule (§3).
  Everyone gets `StartBlame` and `StartMoney`.
- Block: `Morning` → task A (`RoundIntro` → `RoundPlay` → `RoundReveal`) → `Break` → task B (…) → [last block:
  closing `Break`] → meeting. Daily meeting: `MeetingDossier` → `MeetingDiscussion` → `MeetingVote` →
  `MeetingVerdict` → next `Morning`. Final: `MeetingDossier` → `MeetingDiscussion` (FinalDiscussion) → `FinalDefense`
  → `FinalVote` → `FinalVerdict` → `WeekResults` → finished. Company closed (final productivity < target, frozen at the
  final `MeetingDossier` start): `MeetingDiscussion` → `FinalVerdict` (PhaseChanged.B = `Skipped`) → `WeekResults`.
  `HostContinue` during WeekResults finishes. `Abandoned` if fewer than 2 players are connected for 60 s.
- Lengths: `MorningFirst` on block 0 of the first match; `RoundIntroFirst` when the task is not in `History.SeenTasks`
  and not yet shown this match; `RoundPlay` = `RoundPlayByPlayers[n-3] × (Durations.RoundPlay / 75) × PlayFactor/100`;
  `MeetingDossier` = `Dossier + DossierPerRecording × picks`; `FinalDefense` = `DefensePerPlayer × present players`.
- Early submit: once every connected player has submitted every *required, enabled* control (Supply Swap: never), end =
  `min(end, now + AllSubmittedRemaining)`; emit `HostEarlySubmit` with saved ms. With `CarryToBreak`, task A's saved
  seconds extend the next `Break` (cap `BreakMax`); the last task's saved seconds extend the closing break. Carried
  seconds appear in `Features.CarriedSeconds` and `HostEarlySubmit.B`. `PhaseChanged.B` on the next phase =
  `TimerShortened`.
- Favourite (Kayırmacı): each `Morning`, seeded among players not holding the strictly highest blame; public.

## 3. Scheduling (`LineSchedule`, static, pure)
- `bool Validate(LineConfig c, int players, WeekLength length, string managerId, out string reasonKey)` — false with
  `"lobby.pool_too_small"` when eligible tasks < `Days × RoundsPerDay + MinSpareTasks`.
- `List<double> PredictMeetingStarts(LineConfig c, int players, WeekLength length, bool firstSession, bool firstTimeTasks)`
  — seconds from match start to each meeting's `MeetingDossier`, assuming no early submits and one recording per meeting.
- Eligible = `Enabled`, `MinPlayers ≤ n ≤ MaxPlayers`, `ExclusiveManager` empty or equal to the manager.
- Draw per match without replacement: weight = profile `TaskWeights[kind]` (missing → 1), restricted to the tasks with
  the lowest `History.TaskUses` among eligible; soft rule — per block prefer one Pair/Role + one All task when both are
  available. First match block 0 = `BonusPool`, `InvoiceReconcile`. `ForceTasks` overrides (still validated: no repeats).
- Role holders (visitor, offered player, insiders, fake-mail holder) = seats with the fewest roles this match, then in
  `History.RoleUses`, seeded tie-break; `ForceRoles[taskNumber]` overrides.

## 4. Tasks: facts, controls, resolution keys

Localisation (UI): fact `task.<id>.fact.<key>`, control label `task.<id>.control.<key>`, option
`task.<id>.opt.<key>`, role `task.<id>.role.<n>`, tutorial `task.<id>.tut.<1..5>.{title,body,example}`, name
`task.<id>.name`, one sentence `task.<id>.rule`. `{p}` = player name, `{n1}..{n3}` = numbers. Scope 0 private, 1
partner, 2 public. Task `Group` lists partners (pair/trio), role partners (T4: [preparer I review, my approver]),
everyone else (all tasks, T6 visitor), or [visitor] (T6 requester).

| Task | Facts (key: args) | Controls (key, kind, field, options) | Reveal facts (public) |
|---|---|---|---|
| T1 invoice_reconcile | `line`: N1 line no (1-2), N2 amount · `partner`(1): P | `total` Number A 0..99 req · `pad` Choice B opts `no`=0,`yes`=1(sab) | `correct_entries`: N1 correct, N2 entries · `groups_correct`: N1, N2 groups |
| T2 budget_split | `need`: N1 · `budget`(2): N1 B, N2 players | `request` Number A 0..B req | per player `grant`: P, N1 request, N2 grant, N3 success · `overflow`: N1 Σrequests, N2 B · Votes = requests |
| T3 customer_spec | `color`/`size`/`day`: N1 0..2 · `commission`: N1 spec index · `partner`(1): P · public `spec`: N1 index, N2 packed attrs (color×9 + size×3 + day) | `order` Choice A opts `spec`=0..5 (N1 packed) req | `correct_orders`: N1, N2 · `true_spec`: N1 packed |
| T4 report_review | `my_figure`: N1 true, N2 polished · `review_figure`: P preparer, N1 shown (-1 until submitted) · `audit_figure`: P preparer, N1 audit figure | `version` Choice A opts `accurate`=0,`polished`=1(sab) req **locks** · `decision` Choice B opts `approve`=0,`reject`=1 req, WaitingFor = preparer until submitted, default approve | `reports_approved`: N1 approved, N2 reports |
| T5 room_booking | `value`: N1 · `ok_slot`: N1 slot (×2) · public `slots`: N1 S | `slot` Choice A opts `slot`=0..S-1 (N1), `yield`=9 req, default yield | per player `booking`: P, N1 slot or 9, N2 success · Votes = bookings |
| T6 archive_visitor | public `visitor`: P · public `capacity`: N1 K · requester `need_doc`: N1 title id · visitor `doc`: N1 index, N2 title id (×6) · visitor `hr_file`: N1 index, P subject | visitor: `fetch` Multi A bitmask of indices ≤ K req · `copy` Choice B opts `no`,`yes`(sab) · requester: `confirm` Choice A opts `doc`=0..5 (N2 title id) req | `served`: N1 served, N2 K |
| T7 bonus_pool | `partner`(1): P | `choice` Choice A opts `share`=0, `take`=1 req, default share | `takes`: N1 take decisions |
| T8 server_code | public `position`: P owner, N1 position (×N) · `my_digit`: N1 pos, N2 digit · `next_digit`: N1 pos, N2 digit · offered `offer`: N1 amount | `code` Number A, Digits = N, req · offered `accept` Choice B opts `no`,`yes`(sab) | `cracked`: N1 0/1, N2 exact entries |
| T9 phishing_check | `account`: N1 4-digit ending · insider `insider_offer`: N1 fake ending, N2 amount | `flag` Player Target, AllowSelf, req · insider `accept` Choice B (sab) | `fake_holder`: P · `blocked`: N1 0/1 · Votes = flags |
| T10 shift_cover | public `shifts`: N1 K · `available`: N1 shift (×1-2) · `cost`: N1 | `volunteer` Choice A opts `shift`=0..K-1 (N1), `decline`=9 req, default decline | `covered`: N1, N2 K · Votes = choices |
| T11 vendor_pick | `tested`: N1 vendor, N2 quality · insider `kickback`: N1 vendor, N2 amount | `vote` Choice A opts `vendor`=0..3 (N1) req · insider `accept` Choice B (sab) | `winner`: N1 vendor, N2 quality · `quality`: N1 vendor, N2 quality (×4) · Votes |
| T12 supply_swap | `need`: N1 item type (×2) · `collector`: N1 item type · Items = held types | `give` Items (verb GiveItem: Target receiver, A held index) · `done` Choice A opts `done`=1, optional | `completed`: N1, N2 players |

Roles (`LineTaskView.Role`, private): T6 1 visitor / 2 requester; T8 1 offered; T9 1 insider; T11 1 insider; else 0.
Item type ids 0..N+1 → `task.supply_swap.item.<id>`; document titles `task.archive_visitor.title.<id>` (pool of 10);
shift labels `task.shift_cover.shift.<i>`; slot labels `task.room_booking.slot.<i>`; vendors `task.vendor_pick.vendor.<i>`;
colour/size/day `task.customer_spec.color.<i>` etc.

`SubmitTask`: `C` = TaskField mask; only masked fields change; each field must belong to an enabled control of my
task; Number range/digits validated; Multi bitmask ≤ MaxPicks and within options; a locked field already submitted
→ `Locked`; a control waiting for another player → `Waiting`; computer blocked → `ComputerBlocked`; only in
`RoundPlay`. Emits public `TaskSubmitted` (when the player's required set first becomes complete or a value changes)
and host `HostChoice` per field (category codes §8).

## 5. Privacy (enforced by `BuildView`/`MessagesFor`, tested by encoding and byte-scanning)
For recipient R nothing reveals: another player's private facts, choices (before a public reveal), money, objective
(except R's own HR-file record), trick type or use (except victim-facing effects and public internet-down), incident
actors before exposure (and never in results for unexposed incidents), complaint authors, call partners/state/logs of
others, wiretaps, records R has no access to (purchase/offer state of others included), remedial requests between
others, messages addressed to others, spoofing of a mail, recording candidates (host-only). Host-audience events never
leave the host process (`RouteEvents` already drops them). Tests: build every view and message list at every phase of
scripted matches, encode with `LineCodec`, assert sentinel values are absent (pattern of the v3 privacy tests).

Text: the engine sanitises every player-authored string — trim, NFC, drop control characters (email keeps `\n`, max
3 consecutive), replace `<`/`>` with `‹`/`›`, reject empty (`EmptyText`) or over the limit (`TooLong`, measured in
UTF-16 units after sanitising). Names get the same treatment on the host (`HostLineController.SanitizeName`).

## 6. Records, offers, incidents, money
- Record ids are global, increasing from 1; incident ids too. Access sets are per player. Automatic access: own calls
  (`Call` on call end, both sides, N1 = whole seconds), own transfers (both sides), partner-visible `TaskEntry`
  records of group partners at reveal (T1, T3, T7, T4 decision to the preparer and figure to the approver), the T4
  auditor's `AuditFigure`, own `DealPayment`/`OfferAcceptance`, the victim's `TrickEffect`, public `TaskOutcome`,
  `Warning`, `Remedial`. The copier of an HR file gets `HrFile`.
- Proof records (`ProofOf` = incident id): InvoicePadding → Bank `DealPayment`; CookedReport → TaskSystem
  `ReportDraft`; ArchiveSnoop → BadgeLog `BadgeAccess` (N2 = 1); AcceptedCompetitorOffer / AcceptedKickback /
  VendorKickback → Bank `OfferAcceptance`; tricks → ITLog `TrickLog` (fake email → `SpoofedMail`).
- Offer ids: BankLedger block b = 100 + b, TaskAudit task t = 200 + t, ITLog block b = 300 + b, BadgeLog block b =
  400 + b. Block offers are available from that block's `Morning`; TaskAudit after the task resolves. Buying grants every
  record of that source in the block (or task), including later ones. Purchases: office phases and
  `MeetingDossier`/`MeetingDiscussion`; price = base (− `BankLedgerDiscount` for bank, min 1) + `PriceStep` × my earlier
  purchases; limits per block/match; a purchased offer again → `AlreadyUsed` with no charge.
- `PresentRecord`: meeting dossier/discussion; record accessible to me; ≤ `PresentationsPerMeeting`. Adds a
  `LinePresentationView`, marks the record `Presented`, makes it accessible to everyone. If it proves an unexposed
  incident: exposed (once), actor `−CareerSabotagePenalty` (results), `+ExposedSabotageBlame`, public `IncidentExposed`.
  Re-presenting → `Repeat = true`, no consequence.
- Money changes only through `MoneyReason`; productive income per block: running sum S of raw productive income this
  block; credited value = f(S) − f(S_prev) where f(x) = min(Cap, min(x, Full) + ⌊max(0, x − Full)/2⌋). Transfers
  conserve money, check balance, `TransferMinInterval`, 1..`TransferMax`, open in office phases and daily meeting
  `MeetingDossier`/`MeetingDiscussion`/`MeetingVote`, final `MeetingDossier`/`MeetingDiscussion`.
- Remedial: `StartRemedial` in Morning/RoundReveal/Break; task number resolved, below max, no remedial yet, co-signer
  took part, requester under both caps, no pending request; expires after `RemedialTimeout` or when the office phase
  set ends; `RespondRemedial` by the co-signer only.

## 7. Objectives, meeting, final, results
- Objectives: distinct kinds per player (seeded from the 9), Rival ≤ 1, Guardian ≤ 1; targets ≠ self; when both
  exist, same target with `RivalGuardianSameTarget`. `Progress` per kind: Saver money, Broker received, Patron
  colleagues paid ≥ PatronEach, TeamPlayer tasks ≥ output, CleanRecord -1, Rival/Guardian target warnings,
  Archivist records about others, Presenter distinct presentations. Evaluated at `FinalVerdict`.
- Daily ballot (`Vote`): MeetingVote; Target colleague (≠ self, any seat), `Nobody`, or -1 withdraw. Outcome
  (§7 of the design): `WarningGiven`, `Capped`, `Tie`, `BelowMinimum`, `NoActionWon`, `NoVotes`. Votes public at
  verdict; the warning applies at `MeetingVerdict` start; `HostBallot` once.
- Defense order: present seats by blame desc, warnings desc, seeded; `MaySpeak` only the defender.
- Final vote: `Roster` snapshot at FinalVote start; votes from non-roster seats rejected (`NotAvailable`);
  `MajorityNeeded = ⌊roster/2⌋ + 1`; outcome `Fired` / `NoMajority` / `NoVotes`; `Fired` event (target or Nobody).
- Results (`WeekResults` start): career lines (design §4) only when the company survives; ranks; EotM; objectives;
  exposed incidents only; `IncidentsTotal`; `ResultHash` over every outcome field (not MatchId). `NextHistory()`.

## 8. Host-only semantic events (`HostChoice.A` category codes)
T1 A: 1 correct / 0 wrong; B: 0 / 1 pad · T2 A: 0 under need, 1 exact, 2 need+1, 3 more · T3 A: 1 correct, 2
commission spec, 0 other · T4 A: 0 accurate / 1 polished; B: 0 approve / 1 reject (+10 when the decision was correct)
· T5 A: 0 yield, 1 acceptable slot, 2 unacceptable · T6 visitor A: needed docs among picks; B: copy 0/1; requester A:
1 correct / 0 wrong · T7 A: 0 share / 1 take · T8 A: 1 exact / 0 wrong; B: accept · T9 Target: 1 holder, 0 other, 2
self; B: accept · T10 A: 0 decline, 1 available shift, 2 unavailable · T11 A: quality of the vendor; B: accept · T12
`GiveItem` → HostChoice Target 0, A = 1 when the item completed the receiver's need, else 0; `done` A = 1.
These codes never reach clients; the telemetry layer holds them until the match ends (`private_until_match_end`).

## 9. Codec
`LineCodec`: compact binary, magic `B3`/version 4, little-endian, explicit field order, nullable presence bytes, LEB128
lengths; throws `FormatException` on bad/foreign payloads, unknown enum values, invalid UTF-8, strings over 2,048 UTF-8
bytes, lists over 4,096 entries. New: `EncodeMessages/DecodeMessages(List<LineMessage>)`, connection payload consent
fields, every new view/result/config/setup/history field. Round-trip tests for every type with non-default values and
the reflection completeness test. Typical 6-player mid-match view target < 6 KB (report the size).

## 10. Bot (`LineBot`) and simulation
`LineBot(int seat, int seed, LineBotPolicy policy)`; `LineAction Next(LineView view, LineConfig config, double now,
IReadOnlyList<LineView> shared = null)`. Policies: `NoComm` (own facts only, never calls/texts), `Talk` (calls/texts
partners; uses `shared` partner views supplied by a simulation harness for players it talked to this task, lying with
a seeded probability when its incentives conflict), `Greedy` (takes offers, over-requests). Never sends an action the
view says will fail (except discovering Busy). Uses transfers, purchases, presentations, remedial requests, complaints
(preset only) and texts (fixed placeholder text) at modest rates. `Tools/LineSim` reports productivity, closure rate,
career components, EotM, objective completion and output per task by policy mix and head count — synthetic numbers,
labelled as such.

## 11. Tests (minimum)
Task outputs/income/incidents per task incl. trios and edge cases; no task repeats in a match; `Validate` for every
preset (3–6 × Short/Full × each manager); `PredictMeetingStarts` Short 4p window; early-submit carry; productivity
provisional/final, unresolved = 0, no double loss, remedial bounded; career table, ranks with ties, EotM ties → none,
closure → no awards/no rank; exposure once per incident (re-presenting does nothing); transfers/purchases cannot
duplicate money or charges under duplicate request ids, reconnects and re-presented requests; warning outcomes
(tie, capped, below minimum, no action); final majority with roster/disconnects, Fire nobody; objectives; message
limits/sanitising/rate limits/threads; privacy scans incl. messages; codec round-trip/completeness; determinism; full bot
matches 3–6 × Short/Full × policies finishing without exceptions.
