# Balance pass v2.2 (from the adversarial design review, 2026-10-29 ~05:40)

A Claude reviewer read the engine and drove it with a probe console app (default config). Findings and what we did.
"Config" = `MatchConfig.CreateDefault` (+ `ApplyHeadcount`); "Rule" = engine change by Codex (v2.2).

| # | Problem (verified) | Fix | Where |
|---|---|---|---|
| 1 | At 5–6 players the manager never reached the last desks (2.6 m/s, 30 s phases): one desk was immune all match; a tip shielded the last desk | WalkSpeed 3.5, Raid/Final 40 s, SurpriseSeconds 30, ShuffleDesks on; test: all assigned desks inspected at 6p incl. one tip | Config + Codex test |
| 2 | Nothing scaled with head count: 3p rarely made quota (no winners), 6p had no pressure | `ApplyHeadcount`: quota ×{3:16, 4:22, 5:26, 6:30}/22; 5–6p faster orders, 3 open mail, discussion ×60/45 | Config (host applies) |
| 3 | Report sniping: the deliverer took the writer's performance + Paragöz favour | Report performance/favour credited to the report owner; Paragöz vote 0.5 / perf 0.3 | Rule + config |
| 4 | Crouch made witnesses irrelevant; power cut made sabotage undetectable | CrouchRangeFactor 0.8, DarkRange 4 m | Config |
| 5 | Tip line not anonymous (3 s OnCall right before the public tip event); ringing-out showed OnCall at the same instant the callee rang | TipSeconds 0; ringing out no longer shows OnCall; contacts only for players in line of sight (UI) | Config + rule + UI |
| 6 | Phone harassment: callee cooldown never checked, re-dial every 8 s, scam_artist from unanswered calls | Only the caller gets the cooldown on unanswered calls; a declined/rung-out caller→callee pair is blocked 30 s (`Calls.DeclineBlockSeconds`); scam_artist counts answered calls | Rule |
| 7 | Rage-quit after being shortlisted abandoned the match → no career change, same day replays | From MeetingDiscussion on, grace expiry does not abandon: the quitter stays absent (no ballot), verdict runs | Rule |
| 8 | Kayırmacı favour discount ≈ immunity at 3–4p | FavorWeight 0.1, starting favour 1 | Config + rule |
| 9 | Friday ×2 only multiplied the quota term; objective luck dominated; OFFICE_ALL_ROUNDER trivial; CHAOS_COORDINATOR impossible at 3p | Multiplier applies to every positive term; OFFICE_ALL_ROUNDER uses card Count (5); CHAOS_COORDINATOR Count 2 | Rule + config |
| 10 | Internet cut at the final whistle = free quota spoiler | Outage 20 s; outage cards rejected once the final inspection's warning has started | Config + rule |
| 11 | Disiplinci surprise always at the same second | Seeded start in [0.2, 0.7] of Work2 | Rule + config |
| 12 | Parking an item on an empty desk counted as PlantItem (3 misconduct) | Unassigned desks are neutral (no incident) | Rule |
| — | Tasting always emptied the mug, so it was never worth it | An unsalted mug stays full after tasting | Rule |

## v2.3 — from the balance simulator (`docs/BALANCE_SIM.md`)

The simulator (Codex, 9,000+ matches against the real engine) showed quota at 100 % success with ~2× the target, and
objective outliers: Under Pressure ~0 %, Chaos 3–6 %, Scapegoat 7–14 %, Clean Hands 70–92 %. Changes: QuotaTarget
22 → 28 (4p base, head-count scaled), Scapegoat/Chaos Count 1, Clean Hands 7 tasks, Under Pressure only fails on
incriminating final-inspection observations. 4-player result: every card 19–62 % except Clean Hands 81 % and quota
still 100 % for the (tireless, silent) sim workers. **Not adopted:** zeroing NotAtDesk/Moving/PersonalCall risk
(would remove inspection tension) and quota ~44 (sim workers do not talk, call or sneak). Re-tune quota after the
first human week.

Also noted (no change tonight): the Informant outs themselves by requesting footage during the discussion (only they
can then); unplugging is a weak stall (fine); favour resets every match (fine).
