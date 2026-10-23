# Closed Playtest Protocol (4 friends)

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
