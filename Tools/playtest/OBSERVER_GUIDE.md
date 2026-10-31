# Observer guide

You watch a friend group play and code what you see into `observer_template.csv` (one row per **coded moment or meeting**, not per
player). The analyzer joins your rows to the telemetry logs and reports them next to the numbers
(`python -I Tools/scripts/analyze_playtests.py <logs> --observer observer.csv`).

## Hard rules (privacy)

- **No names, personal information, quotes or transcripts - anywhere in the CSV.** Use only the anonymous ids below and the fixed
  answers `yes` / `no` / blank. The analyzer ignores every column that is not in the template, so free text typed into an extra
  column is dropped, but do not rely on that: never type it.
- Free-form notes are allowed only as **separate local files** (for example `notes_G07.txt` on your own machine). They are never
  uploaded, never parsed, never joined to telemetry, and should not contain names or verbatim quotes either.
- Do not audio-record, photograph or screen-record the session for coding purposes unless the participants agreed in writing and
  the files stay offline.

## How rows are joined

| Column | How to fill it |
|---|---|
| `anonymous_group_id` | The facilitator's group code (for example `G07`) - the same code as the `-playtestgroup` launch argument. Never a person's name. |
| `build_version`, `ruleset_version` | Copy from the lobby/settings screen or the facilitator sheet (for example `202611012000-a1b2c3`, `v4.0-hybrid`). Different values are never pooled. |
| `player_count` | 3, 4, 5 or 6 humans (bots excluded). |
| `match_id` | Shown in Settings > Privacy > Export or read from the host log (`m-...`). If you cannot get it leave it blank: the analyzer then joins by group (and day/round) when there is exactly one logged match for the group. |
| `day_index` | 0-based work block (0 = first day). Blank if the moment is not tied to a day. |
| `round_index` | 0 or 1 (task A or B of the day). Blank for meetings and the final. |
| `phase` | The phase on screen: `Morning`, `RoundIntro`, `RoundPlay`, `RoundReveal`, `Break`, `MeetingDossier`, `MeetingDiscussion`, `MeetingVote`, `MeetingVerdict`, `FinalDefense`, `FinalVote`, `FinalVerdict`, `WeekResults`. |
| `moment_timestamp` | Elapsed time since match start as `hh:mm:ss` (a stopwatch started at "Start"), or the wall-clock time of day. One convention per session, written on the facilitator sheet. |

## Coding the yes/no fields

Use `yes`, `no`, or **leave blank when you could not observe it**. Blank is *missing*, never "no": a blank never counts against the
game. If you are unsure, leave blank and say so with `coding_confidence`.

| Field | Code `yes` when you saw ... |
|---|---|
| `natural_accusation_occurred` | A player spontaneously blamed or accused another player (not prompted by you). |
| `accusation_referenced_game_evidence` | (Only if an accusation occurred.) The accuser pointed to something that happened in the game: a record, a call, a recording, an incident, a number. |
| `recording_was_intelligible` | A 7-second reveal was understood by the room (people reacted to its content, nobody asked "what was that?"). |
| `recording_was_relevant` | The reveal was about something the table cared about (the task, a deal, a suspect). |
| `recording_lacked_context` | People said or showed they could not place the reveal (no idea which task or why). Can be `yes` together with intelligible. |
| `recording_created_reaction` | Laughter, gasp, accusation or argument directly after a reveal. |
| `recording_was_referenced_later` | Someone later brought the reveal back up in discussion or in the vote. |
| `players_reconsidered_accusation` | A player visibly changed their mind or vote because of new information. |
| `players_appeared_confused` | Several players stopped, asked the facilitator or each other how something works. One hesitating player does not count. |
| `players_appeared_excluded` | A player was clearly idle, ignored, or unable to reach others for a stretch. Reading or listening quietly is **not** exclusion. |
| `meeting_felt_too_short` / `meeting_felt_too_long` | The room asked for more time / was visibly waiting for the timer. Code per meeting row only. |
| `funny_betrayal` | A betrayal or double-cross that made the table laugh or groan. |
| `unexpected_accusation` | An accusation that surprised the room (target or reason). |
| `surprising_outcome` | A vote, result or company outcome the room did not expect. |
| `notable_funny_moment` | Any other moment you would describe as notable or funny to the team. |
| `players_requested_rematch` | At the end, players asked for another match without being prompted. |

A row is "notable" for KPI M if at least one of `funny_betrayal`, `unexpected_accusation`, `surprising_outcome`,
`notable_funny_moment` is `yes`. Only your confirmation makes a moment notable: the game's own markers are candidates.

## Optional columns (documented extras)

- `observer_id`: a short code for you (`O1`), never a name. Use it to check coding drift between observers.
- `coding_confidence`: `high`, `medium` or `low` for the row.
- `moderator_required_repeat`: `yes` when the facilitator told the group to play another match (planned repeat). The analyzer then
  excludes that match from the voluntary-rematch KPI. Leave blank otherwise.

## Practical tips

- One row per meeting is enough for the accusation/recording fields; add extra rows for individual standout moments.
- Code during the pause between matches while it is fresh. Do not code from memory the next day.
- Observe the group, not individuals; never write who did something.
- Adequate observation for KPI M means rows for at least half of the group's logged matches. If you could not watch a match, say
  nothing for it: missing is better than guessed.
