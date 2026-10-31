# v3 contract (engine ↔ host ↔ clients)

> **Superseded (2026-11-01)** by `docs/CONTRACTS_V4.md` (protocol 4). Kept for history.

Rules and numbers: `docs/V3_DESIGN.md` (normative). Types: `Game/Assets/Game/Core/Line/Contracts/*.cs` (owned by the
lead; ask before changing). Engine, codec and bot: `Game/Assets/Game/Core/Line/Engine/**` (engine track). Tests:
`Game/Assets/Game/Tests/Line/**`, run with the .NET project under `Tools/CoreTests` (add the folder to its includes).
Defaults live in `LineConfig.CreateDefault()`; a value in this file and the config disagree → the config wins.

## 1. Determinism and time
- Pure C#, `netstandard2.1`, C# 9, no UnityEngine. All randomness from one seeded PRNG in the engine (no
  `System.Random` default seeds, no `Guid`, no dictionary-order dependence). Same setup + same `(now, player, action)`
  sequence ⇒ identical views, events and `ResultHash` on every machine.
- `now` is host time in seconds (double), monotonic. `Tick(now)` performs every transition due at or before `now`
  (it may cross several phases if called late). Phase ends are absolute times (`PhaseEndTime`).
- `StateVersion` increments on every state change; views carry it.

## 2. Week flow
- Setup validation: 3–6 players with distinct ids 0..5; otherwise throw `ArgumentException`.
- Week start (at `StartTime`): manager = `Setup.ManagerProfileId` or seeded pick ≠ last of `History.PastManagers`.
  Agendas: random derangement (everyone is targeted exactly once). Tricks: distinct types when ≤ 6 players, random
  subset/order. Round schedule for all days (see §3). First week of a session = `History == null || WeeksPlayed == 0`.
- Day: `Morning` → (`RoundIntro` → `RoundPlay` → `RoundReveal`) × `RoundsPerDay` → meeting. Daily meeting:
  `MeetingDossier` → `MeetingDiscussion` → `MeetingVote` → `MeetingVerdict` → next day's `Morning`. Last day:
  `MeetingDossier` → `MeetingDiscussion` (FinalDiscussion length) → `FinalDefense` → `FinalVote` → `FinalVerdict` →
  `WeekResults` → finished (`IsFinished`, `Result` set at WeekResults start). `HostContinue` during WeekResults finishes
  immediately. `Abandoned` only if fewer than 2 players are connected for 60 s (then finished, no points).
- Durations from `LineDurations`; `RoundIntroFirst` when the type is not in `History.SeenRounds` and not yet shown this
  week; `MorningFirst` on day 0 of the first week. `RoundPlay` ends early: once every connected player has
  `Submitted` (for Shared Password: once it is solved), the end becomes `min(end, now + AllSubmittedRemaining)`.
- `MeetingDossier` length = `Dossier + DossierPerRecording × picks`.
- Favourite (Kayırmacı): chosen each Morning among players not holding the strictly highest blame; public.

## 3. Rounds — schedule, private info, choices, outcomes
- Schedule: first week day 0 = `PrimHavuzu`, `IhbarKutusu`. Otherwise draw per day without repeats inside the day,
  prefer types used least in this week, then least in the session; `KayipRapor` only with ≥ `KayipRaporMinPlayers`.
- `SubmitChoice` semantics (resubmitting overwrites; only during `RoundPlay`; blocked while your computer is blocked):

| Round | Target | A | Default when not submitted |
|---|---|---|---|
| PrimHavuzu | – | 0 share / 1 take | share |
| FarkliEposta | voted player (≠ self) | odd player only: guessed topic id from `GuessOptions`, else -1 | no vote, no guess |
| KayipRapor | voted player (≠ self) | – | no vote |
| IhbarKutusu | complained-about player (≠ self) or -1 | reason 0..ComplaintReasons-1 | abstain |
| TerfiListesi | recommended player (≠ self) or -1 | – | abstain |
| OrtakSifre | use `Attempt`, not SubmitChoice | – | – |

- `Submitted` = has a stored choice (Shared Password: has made ≥ 1 attempt). Absent players never submit.
- Bonus Pool pairing: seeded shuffle, consecutive pairs; odd count → last player with `LineIds.Intern`
  (`InternShareChance`). Outcome table in V3_DESIGN §3, `GreedBonus` added to a taker whose partner shared.
- Odd Email: topic pair = seeded unused id (`History.UsedTopics` + this week); odd player = seeded. Majority sees
  `TopicId`, `TopicVariant 0`; odd sees `TopicId`, `TopicVariant 1`, `IsOdd`, and `GuessOptions` = `OddGuessOptions`
  topic ids including `TopicId`, shuffled. Plurality = unique max of votes (ties → none).
- Lost Report: culprit seeded; clue cycle exactly as V3_DESIGN §3 (seeded cyclic order of all players; the player
  before the culprit gets next-next; the culprit gets its true next).
- Complaint Box: anonymous; `Reveal.Complaints` lists (target, reason) in a seeded shuffled order; `Counts` per target.
- Promotion List: `Reveal.Votes` with authors.
- Shared Password: `CodeLength` = players; seeded digits 0–9; seeded position order (`PositionOwners` public at
  RoundIntro); saboteur seeded. `Attempt` (A = code, 0 ≤ A < 10^CodeLength): only during RoundPlay, max
  `AttemptsPerPlayer`; public `AttemptMade` with the count of correct digits; a fully correct attempt sets `Solved`,
  `PasswordCracked`, and shortens the phase like "all submitted". Saboteur may attempt too.
- Reveal (at `RoundReveal` start): fill `LineRoundReveal`, apply `Deltas` (blame floor 0; record the *applied*
  delta), add to `TodayReveals`. Kayırmacı favourite: every positive delta of the favourite is reduced by 1.

## 4. Phone, mail, tricks, tips
- Calls (Morning, RoundIntro, RoundPlay, RoundReveal only): `Call` fails with Busy if the callee is Dialing/Ringing/
  Connected or has a dead phone (caller gets `CallBusy` event either way), `Self`, `PhoneDead` (own), `AlreadyInCall`.
  Callee `Ringing` until `now + Ring` → missed. `Answer`/`Decline` only when Ringing. `HangUp` when Dialing (cancel)
  or Connected. Call ids increase from 1. Every meeting phase start ends all calls (`PhaseEnd`). Absent players:
  calls end (`Disconnected`), cannot be called (`Absent`).
- Mail: `SendMail` (genuine) up to `MailsPerDay`, any phase where calls are allowed, Target = recipient or -1 (all),
  A = template 0..MailTemplates-1, B = mentioned player or -1. Recipients get `MailReceived`. Manager mails:
  Morning memo (TemplateId 0) every morning to everyone; System mails: trick notices to victims (TemplateId =
  (int)TrickType). The client text keys: `mail.personal.<template>`, `mail.manager.<template>`, `mail.system.<template>`.
- Tricks (`UseTrick`, from `UnlockDayFirstWeek` in the first week, day 0 otherwise; once per day; only during
  RoundIntro/RoundPlay/RoundReveal; target ≠ self and present):
  - SahteEposta: Target = apparent sender (≠ self), A = template, B = mentioned player or -1; every other player gets a
    Personal mail `From = Target, ToAll = true`. Trace always (Player only if named, TemplateId set).
  - KahveDok: victim's `ComputerBlockedUntil = now + CoffeeSeconds`, `ComputerBlockedBy`; `TrickHit` to the victim.
  - FisCek: victim's `PhoneDeadUntil`; their call ends with `LineCut`; ringing/dialing cancelled.
  - InternetKes: every other player's computer blocked `InternetSeconds` (by InternetKes); public `InternetDown`.
  - HatDinle: `WiretapTarget/Until` for the actor; `TappersOf(target)` includes the actor while active.
  - KayitSil: marks the actor's calls of today as unrecordable (also calls made later today).
  - Trace named with `TraceChance` (seeded, decided at use time, hidden until the dossier); named ⇒ +TrickCaughtBlame
    at the dossier (reason TrickCaught). HatDinle without name ⇒ no dossier line.
- Manager line `TipOff` (same unlock as tricks, once per day, office phases): resolved at `MeetingDossier`: if the target
  used a trick today → that trace becomes named (if not already; no double blame) and target +ConfirmedTipBlame
  (TipConfirmed); else tipper +FalseTipBlame (FalseTip). Dossier shows the subject and the outcome, never the tipper.

## 5. Privacy (must be enforced by `BuildView` and tested by encoding views and scanning them)
For a recipient R, nothing in R's view or events reveals: other players' agendas, trick types, trick use (except
victim-facing effects, public internet-down, dossier lines), round private info of others (partner, topic variant,
odd/culprit/saboteur flags, clues, digits) before `RoundReveal`, any choice/vote/recommendation/attempt-code of others
before it is public by the rules (attempts are public immediately; choices at reveal; meeting votes at verdict), tip
authors, complaint authors (except a leaked one in the dossier), whether a mail was spoofed, other players' phone
state, call partners, call logs, or wiretaps. Tests: build every view at every phase of scripted weeks, encode with
`LineCodec`, and assert forbidden values are absent (pattern of the v2 "privacy byte-scan" tests).

## 6. Meeting and final
- `MeetingDossier` start: end all calls; resolve tips; apply named-trace blame; complaint leak (`LeakChance`, if any
  complaint today); build `Dossier` in this order: Favourite, RoundOutcome per round, TrickTrace/RecordingErased lines,
  TipResult lines, ComplaintLeak, Recording entries. Recording picks: if enabled, `profile.Recordings` picks from
  today's connected calls with duration ≥ `RecordingMinCallSeconds` whose participants did not use KayitSil today,
  weighted by duration, distinct; each pick gets up to `RecordingCandidates` candidate call ids (first = the pick),
  `PlayAt = phaseStart + 4 + i × DossierPerRecording`, `Seconds = RecordingSnippetSeconds`. `ReportRecording` fills
  Chosen/Caller/Callee/CallStart and `Played`, emits `RecordingStarted`.
- Votes: `Vote` during MeetingVote/FinalVote; target ≠ self, present or absent; -1 abstains; changeable. Public
  `VoteCast`. Daily verdict per V3_DESIGN §2 (ties → higher blame; still tied → nobody); İhtar blame applied at
  MeetingVerdict start. Kayırmacı: the favourite reduction applies to İhtar blame too.
- Final: `DefenseOrder` = players by blame desc, then ihtars desc, then id; `FinalDefense` length
  `DefensePerPlayer × count`; `Defender`/`DefenderEndTime` advance inside the phase; `MaySpeak(p)` false for everyone
  but the defender during FinalDefense (true otherwise). `FinalVerdict`: scores, fired, `Fired` event; points at
  `WeekResults` (V3_DESIGN §2). `NextHistory()` appends manager, seen rounds, used topics, adds points and fired count.

## 7. Codec
- `LineCodec`: compact binary, little-endian, starts with a magic byte + `LineProtocol.Version`; throws
  `FormatException` on a bad/foreign payload (clients show "incompatible version"). Round-trip tests for every type
  with every field set to non-default values; reflection-based completeness test so a new field cannot be forgotten.
- Typical view size target: < 4 KB for 6 players mid-week; report the measured size.

## 8. Bot (`LineBot`)
Uses only its own view. Plays every round sensibly but imperfectly (seeded): calls random colleagues for 8–25 s in
RoundPlay (answers ~70 % of incoming calls), submits a choice after 10–60 s (Bonus Pool shares ~60 %; votes favour
whoever its clue/info makes suspicious, else random; the odd one guesses randomly; complaints and promotions random
with a bias toward its agenda target / away from it), Shared Password attempts using its own digit and random others,
uses its trick once on most days on a random valid target, tips off ~30 % of days, sends a mail now and then, votes in
meetings (agenda target or highest blame). Never sends an action the view says will fail.

## 9. Tests (minimum)
Every round outcome table incl. ties, odd counts and the intern; clue-cycle property (all clues true; pooled clues
name everyone but the culprit); KayipRapor never scheduled at 3 players; first-week schedule and unlock days;
favourite reduction; trace/tip resolution incl. no double blame; call state machine (busy, ring timeout, decline,
hang-up from both sides, line cut, phase end, absent); wiretap window; recordings selection (min length, erased tape,
weighting determinism) and dossier timing; final defense order/floor; firing tie-breaks; points and history; privacy
scans; codec round-trip + completeness; determinism (same seed ⇒ same hash; different seeds ⇒ different schedules);
full simulated weeks with 3, 4, 5, 6 bots (Short and Full) finishing without exceptions.
