# Facilitator checklist

For a playtest session of one friend group (3-6 players). The analyzer treats the **group** as the unit, so every machine in the
session must use the same group code.

## Before the session

- [ ] Pick a **group code** (`G01`, `G02`, ...) and write it on the facilitator sheet. Start the host with `-playtestgroup G07`
      (or fill the lobby field). Never use names.
- [ ] Note the **build** and **ruleset** shown in the lobby. If you change either mid-day, that is a new stratum: say so on the sheet.
- [ ] Set the **cohort** (`friends_wave1`, ...) in `telemetry_config.json` (`test_cohort`). Fixtures use `synthetic_fixture`, bot
      simulations `bot_sim`; never use those names for real people.
- [ ] Decide the declared experiment arm **per group** (not per player): `rec_select` (legacy|context), `early_submit`
      (shorten|carry), `recording_awareness` (on|off via the lobby recordings toggle). Write it down.
- [ ] **Consent check, out loud, per participant**: analytics consent is a separate screen from the recordings toggle. Participants
      may choose "local logs only" or "no collection"; a player without consent is simply not observed (their rows are missing and
      reported as coverage loss, never as zeros). Recording consent (lobby toggle) is independent of analytics consent.
- [ ] Tell the group: no audio, text, names or transcripts are collected by telemetry; they can withdraw any time and ask for their
      anonymous id to be purged afterwards.
- [ ] Observer(s) have `observer_template.csv` and read `OBSERVER_GUIDE.md`.

## What not to record

- No audio or video of the table, no screenshots with names, no chat transcripts, no quotes, no personal details in any file or
  sheet. Free-form notes stay on the observer's machine as separate files.
- No real names next to anonymous ids. If you must map ids to people for withdrawal requests, keep that sheet offline and delete it
  after the study.

## During

- [ ] Start the observer stopwatch when the host presses Start; use the same convention for `moment_timestamp`.
- [ ] Do not coach during rounds. Log when you intervene (that is a confounder).
- [ ] Mark a planned repeat as `moderator_required_repeat = yes` in the observer rows. Never press rematch for them if you want to
      measure voluntary rematch.
- [ ] If anything crashes, write down the time and phase. The analyzer only sees events the game could still write.

## After each match

- [ ] Players fill `player_feedback.html` on a laptop (offline, no text fields) and send you the exported CSV.
- [ ] Observer finishes coding the match rows.

## Collect the logs from every machine

Each machine writes only what it observed: the host writes gameplay facts, each client writes its own UI/connection events.

1. On **every** machine (host and all clients): Settings > Privacy > Export. This copies
   `telemetry/<environment>/events_<session>_<n>.jsonl` (rotated files at 1 MB, up to 20 per environment) to a folder.
2. Put each machine's folder under one directory per group, for example `Playtests/G07/host/`, `Playtests/G07/client_a/`, ... (any
   structure works; the analyzer searches recursively and de-duplicates by event id, so re-importing a file is harmless).
3. Do **not** edit files. A truncated last line (game killed mid-write) is tolerated and counted.
4. Run: `python -I Tools/scripts/analyze_playtests.py Playtests --observer Playtests/observer_G07.csv --feedback Playtests/feedback_G07.csv --out Reports`
5. A missing client folder lowers coverage for that player's events; it is not an error.

## Withdrawal / deletion

`python -I Tools/scripts/analyze_playtests.py Playtests --purge-player p-<id> --purge-out Playtests_clean` writes cleaned copies
without that anonymous player id (own events plus host events naming their slot). Aggregate host events cannot be un-aggregated;
verify the copy, then delete the originals and any cloud data via Settings > Privacy.

## Sample size reminder

10-20 groups are exploratory evidence. Report numbers with their group counts, never as proof that a mechanic works.
