# v4 "Hybrid Office" — gameplay redesign (normative)

Ruleset id: **`v4.0-hybrid`** (`LineProtocol.RulesetVersion`). Protocol version 4. This document supersedes
`docs/V3_DESIGN.md` wherever they differ; v3 stays readable at tag `v3.1-day` and in git history. Wire/engine details:
`docs/CONTRACTS_V4.md`. Defaults live in `LineConfig.CreateDefault()`; if this file and the config disagree, the
config wins and this file is wrong.

Central question the design serves: *do situations arise where players want to communicate, negotiate, betray,
accuse, defend themselves and immediately play again — and do they produce moments people want to share?* Nothing in
this document claims the answer is yes. Every number below is a **hypothesis** for the first 10–20 friend-group
playtests (`docs/PLAYTEST_KPI_GUIDE.md`).

The emotional loop: *"I need something from this person. They might deceive me. I could deceive them too. We still
need the company to survive. If our conversation or actions come up in the meeting, I will have to explain myself."*

Preserved from v3: own first-person offices, private 1:1 phone calls, asymmetric information, shared meetings with
everyone's voice, real 7-second phone-recording reveals, four manager personalities, office comedy.

## 1. Mode: hybrid cooperation + personal ambition (default and only mode in v4.0)

- Everyone needs the **company** to reach its productivity target; everyone also holds one **secret objective**.
- Nobody has a "destroy the company" win condition. A traitor mode (3 vs 1) is a future, separate ruleset.
- Players may negotiate, mislead, break informal promises, transfer money, pursue competing interests and perform
  *limited, bounded* sabotage. Every sabotage is an explicit, game-defined action (§8) — the game never infers lies or
  intent from speech, text or an unfavourable choice.
- Balanced first for 4 players; 3–6 supported.

## 2. Match structure and pacing

A **match** (code: "week", `WeekId`) has `Blocks` work blocks (code: "day"): **Short = 3**, **Full = 5**.

Every block: `Morning` → task A (`RoundIntro` → `RoundPlay` → `RoundReveal`) → `Break` → task B (`RoundIntro` →
`RoundPlay` → `RoundReveal`) → meeting. The last block adds a closing `Break` after task B, then the **final meeting**.
Two **different** tasks per block; **no task repeats inside a match**.

| Phase | Default s | Voice | Notes |
|---|---|---|---|
| `Morning` | 12 (first block of a session: 40) | calls | Block brief: today's two tasks, company status, favourite. |
| `RoundIntro` | 12 (task never seen this session: 25) | calls | Task card + private info visible. First-time tasks auto-open the 5-step tutorial (§12). |
| `RoundPlay` | 75 × head-count factor | calls | 3/4/5/6 players: 65 / 75 / 85 / 95 s, times the task's `PlayFactor` (%). |
| `RoundReveal` | 10 | calls | Company output of the task, own records, public lines. |
| `Break` | 15 (+ carried time, §2.1; closing break 25) | calls | Calls, texts, transfers, evidence purchases, remedial work (§7). |
| `MeetingDossier` | 15 + 9 per recording | all hear | Productivity, task outcomes, observable incidents, anonymous complaints, recordings. |
| `MeetingDiscussion` | 50 (final: 75) | all hear | Free talk, meeting chat, evidence presentation. |
| `MeetingVote` | 20 | all hear | Warning ballot (colleague or **No action**). |
| `MeetingVerdict` | 6 | all hear | Warning stamp or "no action". |
| `FinalDefense` | 15 per present player | defender only | Everyone gets a turn (blame desc, warnings desc, seeded). |
| `FinalVote` | 25 | all hear | Firing ballot (colleague or **Fire nobody**). |
| `FinalVerdict` | 10 | all hear | Fired / nobody / company closed. |
| `WeekResults` | 45 | all hear | Career points, ranks, company result. Host may continue early. |

Predicted meeting starts (4 players, first match of a session, no early submissions): **≈ 4:45, ≈ 10:20, ≈ 15:55**;
later matches of a session (short intros): ≈ 4:05, 9:10, 14:20. The final discussion, defense, vote and results add
≈ 4 minutes (Short ≈ 20 min total, Full ≈ 33 min). `LineSchedule.Predict` computes these numbers from the config and a
test asserts the Short 4-player preset lands in [4:00, 5:30], [9:00, 10:50], [14:00, 16:30]. Meetings never cut a task:
task lengths are fixed when the task starts.

### 2.1 Early submission (`EarlySubmitPolicy`, default `CarryToBreak`)

v3 dropped the remaining `RoundPlay` time to ≤ 10 s when every connected player had submitted. v4 keeps that trigger
but **preserves the saved time as communication time**: the saved seconds of task A are added to the following
`Break` (capped at `BreakMax` = 45 s); the saved seconds of task B are added to the closing break on the last block and
dropped otherwise. Policy `Shorten` reproduces v3 (no carry). Both the trigger and the carried seconds are emitted as
host events so analytics can compare policies (§15). The trigger requires every **connected** player to have
submitted every *required* control (§5).

## 3. Company productivity

- Every scheduled task instance has `MaxOutput` = 100 (configurable per task `Weight`). Realized output is an integer
  in [0, MaxOutput] produced by explicit task rules (§6) — quality, missing contributions and sabotage enter **only**
  through those rules. Clicks, messages, call counts and call duration never count.
- **Provisional productivity** (shown during play) = Σ realized / Σ max over *resolved* tasks, in per-mille. Before
  the first task resolves the HUD shows **"Measuring…"** (`Provisional = -1`).
- **Final productivity** (frozen when the final meeting begins) = Σ realized / Σ max over **all scheduled** tasks;
  unresolved tasks (abandoned match) contribute 0. Remedial work (§7) adds to an already resolved task's realized output
  (never above max). A loss is applied exactly once: sabotage reduces the task it belongs to, at resolution.
- **Company target** `CompanyTargetPercent` = **65**. The HUD shows current value, target, and the last change with a
  short reason ("Invoice reconciled: +100", "Remedial report: +10").
- Dipping below 65 % mid-match ends nothing. Recovery paths: later tasks, remedial work (+10 per resolved task, §7).
- **Final productivity < target → the company closes**: everyone is fired, shared defeat, no positive career awards,
  no individual winner, no rank. Contributions, completed objectives and sabotage penalties stay visible. The final
  meeting still runs dossier + discussion (post-mortem, evidence allowed) and skips defense and vote.

Design check (hypothesis): a single ordinary sabotage costs one task 15–60 points ≈ 2.5–10 % of a Short match
(600 max). It should not, alone, push a healthy (≈ 80 %) company below 65 %.

## 4. Career points, ranking, Employee of the Month

When the company **survives** (configurable, `Career*` in `LineConfig`):

| Award | Points | Who |
|---|---|---|
| Not fired | **+100** | every player who is not fired |
| Company target met | **+75** | every player, including the fired one |
| Secret objective completed | **+60** | once per match |
| Employee of the Month | **+40** | at most one player |
| Proven sabotage | **−50 per distinct exposed incident** | the incident's actor (also shown on closure) |

- Positive awards occur once per match. Money, task count, call duration and message count never become points.
- Rank = competition ranking on the total (equal totals share a rank: 1, 1, 3). The winner need not be the survivor.
- Balance hypothesis (to test, not proven): survive + target + EotM = 215; survive + target + objective = 235; fired +
  target + objective = 135; survive + target − one exposed sabotage = 125.
- Note for the producer: with one threshold (65 %) the +75 is identical for everyone whenever the company survives; it
  only matters against closure. `CareerTargetPercent` can be raised above `CompanyTargetPercent` to make it a stretch
  goal — a declared experiment, not a default.
- **Employee of the Month** (only when the company survives): eligible = not fired, no exposed incident, connected for
  at least half of the scheduled tasks. Score (integer) = `100 × mean contribution quality` over tasks the player was
  connected for + `EotmCooperationWeight (20) × cooperation credits / cooperation opportunities` (§6 defines quality and
  credits per task; opportunities are counted per task, so a player is never rewarded for sheer task count). The strict
  maximum wins; an exact tie awards **nobody**. There are no nominations.
- Session board: career totals add up across matches of one session (closure: only penalties count).

## 5. Tasks — common model

A task instance = `TaskKind` + `TaskVersion` + groups/roles + private facts + controls + resolution.

- **Groups**: *pair tasks* deal a seeded shuffle into pairs; an odd head count makes the last group a trio. *All tasks*
  put everyone in one group. *Role tasks* assign seeded roles (the role holder is chosen among players who held a role
  least this match/session).
- **Facts** (private): typed lines with a localisation key and integer/player arguments — the UI renders them; the
  engine never sends prose. Partner-visible facts appear only in partners' views; public facts in everyone's.
- **Controls**: `Number`, `Choice`, `Player`, `Multi` (pick up to N), `Items` (give items). Each control writes one
  field of `SubmitTask` (`Target`, `A`, `B`) and is *required* or optional. A control may be `Locks` (cannot change
  once submitted) and `DependsOn` another player's submission (shown as "waiting for …"). Unless locked, submissions
  can change until `RoundPlay` ends.
- **Defaults**: a required control without a submission takes the task's default at resolution. A *connected* player
  who left a required control empty receives **+1 Blame** (`MissedTaskBlame`, reason `MissedTask`) unless their
  computer was blocked by a dirty trick when play ended, or the control was still waiting for another player (T4
  decision on a report that never arrived → default approve). Absent players are censored (no blame); fields they
  never submitted take the default, but a submission made before a disconnect stands.
- **Resolution** (at `RoundReveal` start): realized output, per-player contribution quality q ∈ [0,100], cooperation
  opportunity/credit, income, records (§9), incidents (§8), public reveal lines.
- **Money**: task income is *productive income* (block cap, §10); kickbacks/commissions/offers are *deal income*
  (uncapped but bounded by the task).
- No task's private screen is enough to resolve its cooperative dependency alone (tests check the no-communication
  bot policy against each task).
- Scheduling: per match, draw `Blocks × 2` distinct tasks from the eligible pool (head count, manager exclusivity),
  weighted by the manager's weights, preferring tasks used least this session, and preferring one *pair/role* task
  plus one *all* task per block (soft). The first block of a session's first match is fixed: `bonus_pool`, then
  `invoice_reconcile` (both pair tasks: teach "call your partner"). Before a match starts, `LineSchedule.Validate`
  checks that the eligible pool holds at least `Blocks × 2 + MinSpareTasks (1)` distinct tasks; otherwise the lobby
  refuses to start with `lobby.pool_too_small` — tasks are never silently repeated.

## 6. Task pool (12 definitions, version 1 each)

Pool sufficiency: all 12 tasks are eligible at 3–6 players. Short uses 6 (6 spare), Full uses 10 (2 spare).
Family: **P** = pair/trio, **A** = all players, **R** = roles. "Comm" = why calling/texting someone helps.
Without-comm values are the expected output of the engine's no-communication bot policy (LineSim reports actuals).

### T1 `invoice_reconcile` — Fatura Mutabakatı / Invoice Reconciliation (P)
- *One sentence:* "You and your partner each hold half of an invoice — both of you must enter the exact total."
- Private: your 2 line amounts (1–9). Partner-visible: who your partner is. Comm: the total needs the partner's lines.
- Controls: `Number` total (0–99, required, default none); `Choice` B "add a consulting fee for yourself"
  (optional, **sabotage**).
- Output per group = 100 × correct entries / members, −30 per padding member (floor 0); task = mean of groups.
- Income: correct entry +2. Padding: +3 deal income → incident `InvoicePadding`, proof = bank ledger record.
- Records: partners see each other's entered total. Task audit: all entries. Public: output, correct-entry count.
- q = 100 correct / 0; cooperation credit when the whole group was correct. Without comm ≈ 6 %.

### T2 `budget_split` — Bütçe Paylaşımı / Department Budget (A)
- "There are B units for everyone's projects. Ask for what you need — if the requests overflow, everyone is cut."
- Private: your need n ∈ [1,4]; B = 2N+2, Σneeds = B−2. Comm: fit everyone inside B; settle who gets the slack.
- Controls: `Number` request (0–B, required, default 0).
- If Σr ≤ B, grant = r; else grant = ⌊r × B / Σr⌋. Project succeeds when grant ≥ need. Output = 100 × successes / N.
- Income: success +2; surplus units beyond need +1 each (max +2). Public: all requests, grants and successes.
- q = 100 success / 0; credit when r ≤ n+1 and the total fit. Conflict: over-asking pays but risks everyone.

### T3 `customer_spec` — Müşteri Talebi / Customer Spec (P)
- "The customer's wishes are split between you. Order the one product that matches all of them."
- Private: pair — one member sees colour + size, the other sees delivery day; trio — one attribute each. Six catalogue
  specs form a symmetric catalogue (three colour+size pairs, each on two different days, every day twice): no spec stands
  out publicly, colour+size alone leaves two, the day alone leaves two, together exactly one. Each member also has a private
  **commission** on one decoy (+2 if *they* order it).
- Controls: `Choice` spec (6, required).
- Output per group = 100 × correct orders / members. Income: correct +2; commission +2 (deal income; not sabotage).
- Records: partners see each other's order; bank ledger shows commissions. Without comm ≈ 40 %.

### T4 `report_review` — Rapor Onayı / Report Review (R, cycle)
- "You write one report and approve another. A third colleague holds the true figure of the report you approve."
- Seeded cycle: player i prepares report i; next(i) approves it; next(next(i)) holds its audit figure (3 players: the
  cycle still works). Private: your true figure T; the polished figure T+δ (δ ∈ {8,12,15}); the report you approve
  (shown only after its preparer submits); the audit figure you hold.
- Controls: `Choice` A your version (accurate / polished, **locks**, polished = **sabotage**); `Choice` B decision on
  the report you approve (approve / reject, depends on that preparer, default approve).
- Per report: accurate+approve 100, accurate+reject 50, polished+approve 40, polished+reject 70, never submitted 0.
  Output = mean. Polished+approved pays the preparer +3 (deal). Polished (either way) → incident `CookedReport`,
  proof = task audit "draft history" record.
- Income: approved accurate report +2 (preparer); correct decision +1 (approver).
- Records: preparer sees the decision; approver sees the figure; auditor's audit figure is a presentable record.
- q = mean of role scores (accurate 100 / polished 0; correct decision 100 / wrong 0). Credit: correct decision.

### T5 `room_booking` — Toplantı Odası / Meeting Room (A)
- "N−1 slots for N client meetings. Book a slot that works for you — two bookings on one slot cancel both."
- Private: your client value v ∈ [2,5] and your 2 acceptable slots (seeded so an (N−1)-assignment exists).
- Controls: `Choice` slot (N−1 slots + **Yield**, required, default yield).
- A slot booked by exactly one player who accepts it succeeds. Output = 100 × Σv(successes) / OPT (brute-force max).
- Income: success +2; yield +1 (goodwill). Public: the calendar (who booked what, collisions).
- q = success 100 / yield with output ≥ 90 → 100 / collision 0 / other 50. Credit: yield with output ≥ 90.

### T6 `archive_visitor` — Arşiv Ziyaretçisi / Archive Visitor (R, hub)
- "{V} holds today's visitor badge and can fetch only K documents. Everyone else needs one of them."
- Visitor V is public (rotates, no repeat in a match). K = max(1, N−2). Six documents; each requester needs one
  distinct document; one extra document is an **HR file** about a colleague (subject ≠ V).
- Private: requester — the title of the document you need; visitor — all six codes and titles, capacity K, which code
  is the HR file and whom it is about.
- Controls: visitor `Multi` fetch (≤ K, required); visitor `Choice` B copy the HR file (**sabotage**); requester
  `Choice` confirm the document you need (6, required).
- Served requester = their document fetched **and** they confirmed it. Output = 100 × served / K, −15 if the HR file
  was copied (floor 0). Copying grants V a record of the subject's **secret objective category** and creates incident
  `ArchiveSnoop` (proof = badge log). Comm/conflict: requesters must reach one busy person; one requester is left out;
  bribes via transfer.
- Income: served requester +2; visitor +1 per served. q: visitor 100 × served/K; requester 100 if confirmed correctly.

### T7 `bonus_pool` — Prim Havuzu / Client Bonus Pool (P)
- "Secretly Share or Take everything. Greed costs the client relationship."
- Controls: `Choice` share / take everything (required, default share).
- Output per group — pair: SS 100, ST 60, TT 20; trio by takers 0/1/2/3: 100/70/40/20.
- Income — pair: SS +2/+2, ST taker +5 sharer 0, TT +1/+1; trio: no takers +2 each, one taker +5 others 0, two takers
  +2 each sharer 0, all +1. Taking is legitimate (not sabotage).
- Records: **partners see each other's choice** ("Mavi chose Take everything"); public only the output and the number of
  take decisions. q: share 100 / take 30. Credit: everyone shared.

### T8 `server_code` — Sunucu Şifresi / Server Maintenance Code (A)
- "Everyone holds one digit of the maintenance code. More than half of you must enter the exact code."
- Private: your position + digit and the next position's digit (positions public). One seeded player also gets a
  competitor's offer: +4 if maintenance fails.
- Controls: `Number` code (exactly N digits, required); offered player `Choice` B accept the offer (**sabotage**).
- Cracked iff exact entries > N/2 → 100; else min(60, 20 + 10 × exact entries).
- Income: exact entry +1; cracked +1 each. Accepted + not cracked → +4 (deal); accepted → incident
  `AcceptedCompetitorOffer`, proof = bank ledger. Public: cracked, number of exact entries. q: exact 100 / 0.

### T9 `phishing_check` — Şüpheli Ödeme / Suspicious Payment (A)
- "One of you received the supplier email with a fake bank account. Flag whose mailbox it is before the payment runs."
- Private: the account ending in your email (one player, unaware, has the fake one); one seeded **insider** (not the
  fake-mail holder) knows the fake ending and is offered +4 if the payment goes through.
- Controls: `Player` flag a mailbox (anyone including yourself, required); insider `Choice` B accept (**sabotage**).
- Plurality flag (ties → none). Blocked iff it hits the fake-mail holder → 100; else 25.
- Income: flaggers of the holder +1 when blocked. Insider accepted + not blocked → +4; accepted → incident
  `AcceptedKickback`, proof = bank ledger. Public at reveal: the fake-mail holder (the bank's finding; not a culprit),
  all flags. q: flagged the holder 100 / 0.

### T10 `shift_cover` — Hafta Sonu Nöbeti / Weekend Shifts (A)
- "K weekend shifts must be covered. Volunteers get overtime; free riders get the weekend."
- K = ⌈N/2⌉. Private: the 1–2 shifts you can cover, your private cost c ∈ {0,1,2} of volunteering.
- Controls: `Choice` volunteer for a shift / not this weekend (required, default not).
- A shift is covered when ≥ 1 available volunteer chose it. Output = 100 × covered / K.
- Income: overtime +3 − c (min 0) is paid only to the **sole** volunteer of a shift — two volunteers on one shift both
  earn nothing, so people must agree who takes what. The two available shifts are listed in random order. Public: the
  roster. q: sole volunteer 100; doubled-up volunteer 50; declined while one of your shifts stayed uncovered 0;
  otherwise 60. Credit: sole volunteer and output = 100.

### T11 `vendor_pick` — Tedarikçi Seçimi / Vendor Selection (A)
- "Each of you tested one vendor. Vote for the best — the winner's quality is the company's result."
- Four vendors, qualities {3,5,7,9} shuffled; each player privately sees one vendor's quality (every vendor seen by
  someone at N ≥ 4). One seeded insider is offered +3 by a weak vendor if it wins.
- Controls: `Choice` vote (4, required); insider `Choice` B accept (**sabotage**).
- Plurality (ties → seeded among tied). Output = round(100 × quality / 9).
- Income: voters of the winner +1 if it is the best. Accepted + their vendor wins → +3; accepted → incident
  `VendorKickback`, proof = bank ledger. Public: votes. q: 100 × quality of the vendor you voted / 9.

### T12 `supply_swap` — Malzeme Takası / Supply Swap (A, trading)
- "Your project needs two items you don't have. Trade with colleagues by giving items."
- N+2 item types; each player needs 2 distinct items and holds 2; holdings and needs are an exact cover (everyone can
  finish). Private: needs, holdings, and a **collector bonus** item (+2 if you still hold one at the end — always
  somebody else's need).
- Controls: `Items` give an item to a colleague (verb `GiveItem`, repeatable during play; records for both sides);
  `Choice` "done trading" (optional, ends nothing).
- Output = 100 × completed projects / N. Income: completed +2; collector bonus +2 (deal).
- No missed-contribution blame. q: completed 100 / 0; credit when an item you gave completed someone's project.

## 7. Blame (Kusur), warnings, recovery, firing

- **Blame** is a public reputation and sanction record, distinct from productivity, money, career points and sabotage
  penalties. It informs discussion; it never establishes guilt or forces a firing.
- Start `StartBlame` = **5**; `LeaderStartBonus` default 0 in v4 (blame no longer drives firing). Floor 0.
- Changes: warning **+3** (`IhtarBlame`, manager: Paragöz 2, Disiplinci 4); missed task +1; exposed sabotage +2
  (`ExposedSabotageBlame`); remedial work −2 (`RemedialBlame`). History is never deleted (`BlameLog`).
- **Remedial work (bounded recovery)**: in `Morning`, `RoundReveal` and `Break`, a player opens a remedial report for
  any resolved task of this match whose output is below max and that has had no remedial yet, naming a colleague who
  took part in it (`StartRemedial`). The colleague accepts or declines within 30 s (`RespondRemedial`). Accepted: the
  task's realized output +10 (≤ max), the requester's Blame −2 (if > 0), both +1 money (productive), both get a
  cooperation credit, and a public HR record. Caps: one remedial per task instance; per player one per block and two
  per match (as requester). Repetitive easy actions cannot recover unlimited blame.
- **Intermediate meetings**: ballot = a colleague or **No action** (`LineIds.Nobody`); abstaining is allowed. A
  warning is issued only when one colleague has strictly more votes than every other colleague *and* than No action,
  with at least `WarningMinVotes` (2). Otherwise no action. A player already at `MaxWarningsPerPlayer` (2) cannot get
  another: the meeting records "nomination capped" and issues nothing. Nobody is eliminated.
- **Final meeting**: dossier → discussion → **defense for every present player** (15 s each, order: blame desc,
  warnings desc, seeded) → **final vote**. Roster = players connected when `FinalVote` begins. Each roster member votes
  a colleague, **Fire nobody**, or abstains; votes may change until the phase ends; a roster member who disconnects
  keeps their cast vote. A colleague is fired only with **more than half of the roster** (`⌊roster/2⌋ + 1`). Ties or no
  majority → nobody is fired. The game never names a culprit on its own.

## 8. Sabotage and incidents

- Sabotage is limited to explicit options: task sabotage options (T1, T4, T6, T8, T9, T11) and the week's **dirty
  trick** (one type per player, Short 1 use / Full 2 uses, unlocked from block 2 of the first match, block 1 later):
  Coffee spill (target's computer blocked 25 s), Pull the plug (target's phone dead 30 s), Kill the Wi-Fi (everyone
  else's computer offline 20 s), Fake email (free-text or template mail appearing to come from a colleague), Wiretap
  (hear the target's calls silently for 45 s).
- Every use creates an **incident** with a stable id, kind, actor, block, task, time and one **proof record** (§9).
  Incident ids never change; exposure is idempotent.
- The meeting dossier shows only **observable effects** ("Sarı's keyboard got coffee at 14:32", "The office internet
  went down") — never a culprit. v3's random trace naming, the manager tip line and complaint leaks are removed.
- **Exposure**: an incident is *proven* only when its proof record is presented in a meeting (§9). Accusations,
  complaints and majority votes are never proof. Each distinct exposed incident: −50 career (once), +2 blame (once).
  Presenting the same proof again shows the record but changes nothing.

## 9. Records, evidence and money

- **Records** are authenticated game facts: phone log (your connected calls: who, when, how long), bank ledger
  (transfers, commissions, kickbacks, offers), task audit (entries/choices/fetches of a task), IT log (dirty tricks,
  spoofed mail), badge log (archive), HR (remedials, warnings). Each record has id, kind, source, time, block, task and
  the players involved. Players automatically hold records of their own calls and transfers and partner-visible task
  records; everything else is bought.
- **Evidence offers**: `Bank ledger — block b` (3), `Task audit — task t` (3), `IT log — block b` (4), `Badge log —
  block b` (4). A purchase grants access to the records of that interval/task, including ones created later in the same
  interval. Price +1 per earlier purchase by the same buyer; at most one purchase per block and three per match per
  player; an offer is bought at most once per player (repeat requests never charge twice). Several players may buy the
  same offer — nobody can corner the evidence market. If the prototype shows an event record rather than camera
  footage, the UI says "record", never "footage".
- **Presentation** (`PresentRecord`) in `MeetingDossier`/`MeetingDiscussion` (daily and final): at most 2 per player
  per meeting; the record appears on the projector with its source, time and "authenticated". Presence in a room or a
  phone call is never sabotage proof by itself.
- **Money**: start `StartMoney` = 4 (one basic purchase). Productive task income passes a per-block filter: the first
  4 per block at full value, the rest at half, at most 6 per block. Deal income (kickbacks, commissions, offers,
  collector bonus) is bounded per task, uncapped per block. Money never becomes career points.
- **Transfers** (`Transfer(target, amount)`): 1–10 and ≤ balance, office phases and daily-meeting phases (vote buying
  is allowed), one per 2 s per player. Money is conserved; request ids are deduplicated; both sides get a bank record.
  Payment never forces compliance with a promise.

## 10. Secret objectives

Each player gets one, distinct kinds, at most one *Rival* and one *Guardian* per match (when both are dealt they target
the same colleague half of the time). All are verified from modelled events only; the owner sees live progress.

| Kind | Incentive | Completed when (end of match) |
|---|---|---|
| `Saver` | resources | money ≥ StartMoney + 8 |
| `Broker` | negotiation | received ≥ 6 money in transfers from colleagues |
| `Patron` | negotiation / conflict with Saver | sent ≥ 2 money to each of ≥ 2 different colleagues |
| `TeamPlayer` | cooperation | member of ≥ 3 groups/tasks that reached output ≥ 90 |
| `CleanRecord` | reputation | blame ≤ StartBlame and no warnings |
| `Rival` (X) | reputation, conflicting | X received ≥ 1 warning |
| `Guardian` (Y) | reputation, conflicting | Y received no warning and was not fired |
| `Archivist` | information | holds ≥ 4 records about ≥ 2 different colleagues obtained by purchase or partner visibility (own calls/transfers, public records and records seen only because someone presented them do not count) |
| `Presenter` | information | presented ≥ 2 distinct records in meetings |

No objective is "get X fired". An archive snoop (T6) reveals a colleague's objective *category*, never its target.

## 11. Communication

- Voice: private 1:1 calls in office phases (`Morning`…`RoundReveal`, `Break`); everyone hears everyone in meetings;
  only the defender speaks during their defense. Busy and missed-call notices are immediate; the callee sees "missed
  call from X — call back" and a one-tap text reply.
- **Text is a complete alternative** to voice (H2/H5): private texts (1:1, any phase except `WeekResults`), meeting
  chat (public, meeting phases), **email** (to one or everyone, free text, optional template; **Reply** opens an editable
  reply addressed to the sender in the same thread), **anonymous complaints** (target + preset or free text; presets
  include crude office humour such as "Altına sıçtı." / "Totally crapped the bed."), shown in the next dossier as
  *unverified* with the author hidden from every client.
- Limits (config): text 200 chars, email 400, complaint 140, meeting chat 160; one message per 1.5 s per player;
  per block 60 texts, 8 emails, 1 complaint; 30 meeting-chat lines per meeting. Turkish characters supported; control
  characters stripped; every label rendering player text disables rich text.
- Free text never enters telemetry, logs or analysis output (`docs/ANALYTICS_ARCHITECTURE.md`).

## 12. Onboarding, presentation, fullscreen

- First launch: language, then a 3-card visual intro (no wall of text); the how-to is reachable any time.
- Every task has a **5-step visual tutorial** (Goal → Your information → Who to contact → What to do → Consequences
  for the company and you), each step one short sentence + an example + a highlighted control. Next / Back / Close;
  reopen from the task's "?" button. It opens automatically during a first-time `RoundIntro` (25 s) and never pauses
  the match.
- The game launches **fullscreen** the first time and then remembers the player's display choice (Settings → Display).
- Turkish and English for every string; manager and NPC names are localised.

## 13. Recordings (7-second reveals)

Unchanged pipeline and retention (host memory only, cleared at the next `Morning` or `WeekResults`, never on disk,
lobby toggle). Selection modes (`RecordingSelection`): `Legacy` (v3: duration × 3 for partner/hidden-player calls) and
**`Context`** (default, declared experiment `rec_select`): duration × (1 + 1.0 if both callers shared a group/role in
one of today's tasks) × (1 + 0.5 if the call ended within 30 s before, or overlapped, a submission by either caller) ×
(1 + 0.5 if they transferred money within ±60 s of the call) × 0.5^(times either caller was already played this
match). The excerpt is continuous and unaltered (the host still chooses the most speech-dense 7 s of the first candidate
with ≥ 1.5 s of speech). No transcription, sentiment, laughter or new microphone analysis.

## 14. Managers

| Id | Personality | v4 effects |
|---|---|---|
| `paragoz` | money first | warning +2 blame; bank-ledger offers cost 1 less; weights money tasks (T2, T7, T11) ×2 |
| `disiplinci` | rules first | warning +4 blame; dossier lists every missed task; weights T1, T4, T10 ×2 |
| `kayirmaci` | favourite | names a favourite each morning (not the blame leader): +1 productive income per task |
| `paranoyak` | hears everything | two recordings per meeting; weights T6, T9 ×2 |

Short reactions/subtitles per manager are text lines (`manager.<id>.react.*`); manager TTS is optional (P5).

## 15. What is instrumented (summary)

The engine emits host-only semantic events (choices, income, transfers, purchases, incidents, phase-end reasons,
early-submit carry, objective/career results) that `LineTelemetryObserver` converts to consent-filtered telemetry.
See `docs/ANALYTICS_ARCHITECTURE.md` and `docs/ANALYTICS_EVENT_CATALOG.md`.

## 16. Known risks to test first (producer critique)

1. Tasks are short (65–95 s). Hub tasks (T6) and all-player chains (T8) may leave players on busy lines — text and
   callbacks are the mitigation; H2 measures it.
2. Twelve tasks with five-step tutorials is a lot of reading for a first session; the fixed first block uses the two
   simplest pair tasks.
3. With one 65 % threshold the +75 award does not separate survivors (see §4).
4. Sabotage exposure needs a purchase *and* a presentation; it may be rare. Watch exposed/created incident ratios before
   tuning prices.
5. Remedial work could become a routine ritual between friends; caps bound it, analytics measure it (H6).
6. Budget Split (T2) is solvable without talking when everyone asks exactly their need (Σneeds = B − 2); its tension
   is only the temptation to over-ask. Customer Spec (T3) shares one customer across all pairs, so any player with
   the complementary attribute can help, not only your partner. Both are known, accepted for v4.0 (review notes).
