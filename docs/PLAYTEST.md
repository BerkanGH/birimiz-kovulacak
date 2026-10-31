# Closed Playtest Protocol (4 friends)

> **Superseded.** This is the v1/v2 playtest sheet (Discord voice, informant, raid). For v4 playtests use `docs/PLAYTEST_KPI_GUIDE.md` (H1–H9 plan, KPIs) and `Tools/playtest/` (observer template, player feedback form, facilitator checklist).

Human playtest results are **not** produced by bots or automated tests; record them only from real sessions.
Technical verification lives in `docs/VERIFICATION.md`.

## Session plan (~45 minutes)

1. 5 min — install (see `HOSTING.md`), join Discord voice, host creates the lobby, everyone joins and readies up.
2. Match 1 (~8 min) — no coaching beyond the in-game briefing. Facilitator only watches and notes timestamps.
3. 2 min — quick questions (below), then ask "Rövanş?" and let the group decide.
4. Matches 2–4 as the group wants. Note who asks for the rematch and how fast.
5. 5 min — debrief: "Tell me one thing that happened" (record the stories verbatim).

## What to observe (targets from the concept PDF)

| Question | How to measure | Target (PDF) | Result |
|---|---|---|---|
| Do players understand the basic loop quickly? | Seconds until each player completes a first task without help | understood within ~20 s of play | |
| Does spontaneous laughter occur early? | Timestamp of first laugh in voice | within the first 3 minutes | |
| Does the shared quota create a work-vs-sabotage decision? | Count players who say they skipped sabotage to save the quota (or vice versa); quota pass/fail per match | — | |
| Can players recount a specific incident afterward? | Debrief: number of players who tell a concrete story | every group tells ≥ 1 story | |
| Does the Informant understand the role without solving every match? | Ask the Informant to explain their goal; did they get the right culprit fired? | ≥ 70 % explain it correctly; not 100 % wins | |
| Do players request a rematch? | Unprompted rematch requests / rematch votes | ≥ 50 % want another; ≥ 3 matches average | |
| Does the meeting feel connected to real events? | Count accusations that cite a dossier entry or a witnessed act | most accusations reference concrete events | |

## Per-match sheet

```
Match #   Date/time   Host:
Roles (from results screen): Informant = ____   Fired = ____   Company quota: pass / fail
First task completed at: P1 __ P2 __ P3 __ P4 __
First laugh at: ____      Funniest moment (verbatim): ______________________________
Sabotage seen by players: ____________________   Manager raid reaction: __________________
Meeting: accusations cite events? Y/N   Did anyone change their vote after a defense? Y/N
Confusions / bugs (with time): ___________________________________________________
Rematch requested by: ____   Overall energy (1-5): __
```

## After the session

- Collect `Player.log` + `logs/` folders (paths in `HOSTING.md`) for any bug.
- Tuning knobs to consider (all in `match_config.json`): `QuotaTarget`, phase durations, order intervals, sabotage
  cooldowns, manager vision range/angle, verdict weights, Informant charges.

## v2 additions (İş Haftası build)

Play one **İş Haftası** (5 matches, ~50 min) with 4–6 people, all on in-game voice (no Discord), headphones on.
Before match 1: everyone opens Ayarlar → Mikrofon, picks the mic and uses "Kendini dinle".

| Question | How to observe | Target |
|---|---|---|
| Does in-game voice work for everyone? | Anyone unheard / echo / too quiet in the first 2 min | 0 issues after setup |
| Do private calls create scheming? | Count calls per match (results "Telefon kayıtları"); note quotes like "beni arayan…" | ≥ 2 calls per player per match by Wednesday |
| Does eavesdropping happen? | Anyone repeating something they overheard next to a caller | ≥ 1 per session |
| Does line of sight hide enough? | Ask: "Did you know where everyone was most of the time?" | Mostly "no" |
| Is the tip line used and does it backfire? | Results recap: tips, tip_clean | Used in ≥ 1 of 2 matches from Wednesday; at least one backfire per week |
| Is footage used in defenses and does it change votes? | Footage requests in the meeting, vote changes after it | ≥ 1 request per meeting; ≥ 1 vote swing per week |
| Are manager personalities noticeable? | Ask after each day: "What did today's manager care about?" | ≥ 70 % answer correctly |
| Does the week create a story? | Debrief: "Who was the villain of the week and why?" | Everyone has an answer; laughter at "Ayın Elemanı" |
| Session length | Do they ask for another week? | ≥ 50 % yes |
| Camera / readability | Complaints about camera height, walls fading, finding rooms | Note every complaint with a timestamp |

Bugs and feel notes with timestamps → `Player.log` + `logs/` from `%USERPROFILE%\AppData\LocalLow\Leonidas Studio\Birimiz Kovulacak\`.

## v3 "Ofis Hattı" (separate rooms, phone, final-day firing)

Play one **Short Week** (≈ 30 min) first, then a **Full Week**. Note per day:

1. **Talk vs. clicks:** in a typical round, how much of the time was spent on the phone vs. reading/clicking? (Goal: mostly talking.)
2. **Lies:** did anybody lie on purpose in each round? Which round produced the best lie / betrayal / laugh?
3. **The recording:** what did the 7-second clip catch? Did it change a vote? Did anyone feel exposed in a bad way (privacy)?
4. **Dead time:** who was idle (never called, waiting)? At 3 and 5 players, did the trio / manager line help?
5. **Clarity:** after the rule card, could everyone play the round without asking? Which round needed explanation?
6. **Pacing:** round length (100–150 s by head count) and meeting length — too long / too short?
7. **Final:** was the firing decided before the last day? Did defenses or the shortlist vote change the outcome?
8. **Hook:** after the week, did the table want "one more week"? Which manager was the most fun? Was the next-manager pick used for revenge?
9. **Tuning knobs** (StreamingAssets/Config/line_config.json): `RoundPlayByPlayers`, `Durations.Discussion`, `StartBlame`,
   `Rounds.*` blame numbers, `TricksPerWeekShort/Full`, `RecordingSnippetSeconds`.
