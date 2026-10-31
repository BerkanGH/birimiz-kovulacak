# SYNTHETIC — not playtest evidence

> **SYNTHETIC — not playtest evidence** - this report contains synthetic data (fixtures, bot simulations or bot matches). Do not use it for design conclusions.

# Playtest summary

Generated: 2026-10-31 21:58 UTC

10–20 friend groups are **exploratory evidence**: they can show where to look and which numbers are implausible, not that a mechanic works. Intervals resample whole groups; players of one group are not independent and repeated matches of one group are not new groups.

## 1. Data and coverage

- Files read: 26 (recursive, rotated `events_<session>_<n>.jsonl` included); lines 2080; accepted events 1733 (host 1461, client 272)
- Duplicate events removed (same event_id across host/client/re-imported files): 346
- Truncated final lines: 1; corrupt lines: 0; rejected by envelope validation: 0
- Event catalog: not found - envelope validation + built-in section 5 list only
- Unknown event names: none
- Contexts analysed: 5 matches, 4 groups; strata (build | ruleset): 202611012000-a1b2c3 | v4.0-hybrid; 202611021200-d4e5f6 | v4.1-hybrid
- Excluded as synthetic (not human playtest data): none
- Observer rows linked: 6; feedback rows linked: 12

Matches by player count: 3 players: 1, 4 players: 2, 5 players: 1, 6 players: 1

## Stratum 1: build `202611012000-a1b2c3`, ruleset `v4.0-hybrid`

Strata are never pooled. Charts: `charts/*_s1.svg`.

### Largest completion losses

| stage | matches | share of started | groups |
|---|---|---|---|
| match_started | 4 | 100% | 3 |
| round_finished (>=1) | 4 | 100% | 3 |
| meeting_started (>=1) | 4 | 100% | 3 |
| final_started | 3 | 75% | 2 |
| week_completed | 3 | 75% | 2 |

Largest drop: 1 match(es) between `meeting_started (>=1)` and `final_started`.

- Match completion: 75% (0%–100%) [n=4, groups=3, matches=4]; unknown ends: 0% (0%–0%) [n=4, groups=3, matches=4]
- Abandonment by phase: RoundPlay 25% (0%–100%) [n=4, groups=3, matches=4]
- Lowest completion by player count:
  - `player_count=3`: 0% [n=1, groups=1, matches=1, **insufficient_data**]
  - `player_count=4`: 100% [n=2, groups=1, matches=2, **insufficient_data**]
  - `player_count=5`: 100% [n=1, groups=1, matches=1, **insufficient_data**]
- Rounds finishing least often by task:
  - `task_id=supply_swap`: 50% [n=2, groups=2, matches=2]
  - `task_id=archive_visitor`: 100% [n=1, groups=1, matches=1, **insufficient_data**]
  - `task_id=bonus_pool`: 100% [n=1, groups=1, matches=1, **insufficient_data**]

### Lowest communication participation

- Phone participation overall: 69% (67%–83%) [n=78, groups=3, matches=4]; observed share of eligible: 93% (80%–100%) [n=84, groups=3, matches=4]
- Lowest by round:
  - `day_round=d1_r0`: 42% [n=12, groups=2, matches=3]
  - `day_round=d2_r0`: 58% [n=12, groups=2, matches=3]
  - `day_round=d0_r0`: 73% (50%–100%) [n=15, groups=3, matches=4]
- Lowest by task:
  - `task_id=bonus_pool`: 25% [n=4, groups=1, matches=1, **insufficient_data**]
  - `task_id=shift_cover`: 42% [n=12, groups=2, matches=3]
  - `task_id=invoice_reconcile`: 50% [n=4, groups=1, matches=1, **insufficient_data**]
- Observed player-rounds with no successful contact: 31% (17%–33%) [n=78, groups=3, matches=4]; first-contact latency median: 17.2 (17.1–24.0) [n=54, groups=3, matches=4]
- Busy failures / unanswered requests: 49% (33%–62%) [n=70, groups=3, matches=4] / 7% (0%–13%) [n=70, groups=3, matches=4]; attempted but never connected: 0% (0%–0%) [n=15, groups=3, matches=4]

### Longest interaction gaps and waiting

- Longest gap mean/p90: 38.3 (29.8–46.4) [n=78, groups=3, matches=4] / 71.0 (54.8–85.0) [n=78, groups=3, matches=4] (unknown periods are not zero; observation coverage 93% (82%–100%) [n=84, groups=3, matches=4])
- Longest by task:
  - `task_id=bonus_pool`: 55.6 [n=4, groups=1, matches=1, **insufficient_data**]
  - `task_id=shift_cover`: 52.7 [n=12, groups=2, matches=3]
  - `task_id=invoice_reconcile`: 49.9 [n=4, groups=1, matches=1, **insufficient_data**]
- Waiting after submission: 21.7 (19.5–25.6) [n=73, groups=3, matches=4]; >= 20 s: 49% (44%–60%) [n=73, groups=3, matches=4]; blocked: 0% (0%–0%) [n=78, groups=3, matches=4]
- Worst-served player per group (largest mean gap): G03 54.2s; G02 45.0s; G01 42.2s

### Player-count differences (exploratory)

| metric | 3 players | 4 players | 5 players |
|---|---|---|---|
| completion | 0% [n=1, groups=1, matches=1, **insufficient_data**] | 100% [n=2, groups=1, matches=2, **insufficient_data**] | 100% [n=1, groups=1, matches=1, **insufficient_data**] |
| phone participation | 83% [n=6, groups=1, matches=1, **insufficient_data**] | 67% [n=48, groups=1, matches=2, **insufficient_data**] | 71% [n=24, groups=1, matches=1, **insufficient_data**] |
| first contact median s | 17.4 [n=5, groups=1, matches=1, **insufficient_data**] | 17.1 [n=32, groups=1, matches=2, **insufficient_data**] | 24.0 [n=17, groups=1, matches=1, **insufficient_data**] |
| post-submit wait s | 24.2 [n=5, groups=1, matches=1, **insufficient_data**] | 19.5 [n=45, groups=1, matches=2, **insufficient_data**] | 25.6 [n=23, groups=1, matches=1, **insufficient_data**] |
| longest gap s | 29.8 [n=6, groups=1, matches=1, **insufficient_data**] | 35.4 [n=48, groups=1, matches=2, **insufficient_data**] | 46.4 [n=24, groups=1, matches=1, **insufficient_data**] |

Differences with overlapping intervals or `insufficient_data` are not evidence of a player-count effect.

### Recurring technical problems

- Matches with a technical failure: voice_exception 25% (0%–50%) [n=4, groups=3, matches=4]
- Reconnect success: **not_measured**; non-busy phone failures: 3% (0%–4%) [n=70, groups=3, matches=4]; possible crashes (unknown): 33% (0%–100%) [n=3, groups=3, matches=0]
- stuck_phase per match: 0.00 (0.00–0.00) [n=4, groups=3, matches=4]; voice loss %: 1.59 (1.50–2.00) [n=24, groups=3, matches=4]; start failures: 0% (0%–0%) [n=4, groups=3, matches=4]; join success: 94% (89%–100%) [n=16, groups=3, matches=0]
- Data-quality: 0 corrupt and 1 truncated lines were skipped.

### Slow or confusing decisions

- Slowest submissions by task (median latency):
  - `task_id=archive_visitor`: 53.3 [n=4, groups=1, matches=1, **insufficient_data**]
  - `task_id=customer_spec`: 51.2 [n=4, groups=1, matches=1, **insufficient_data**]
  - `task_id=supply_swap`: 47.0 [n=4, groups=1, matches=1, **insufficient_data**]
- Most revisions per submission by task:
  - `task_id=bonus_pool`: 0.50 [n=4, groups=1, matches=1, **insufficient_data**]
  - `task_id=invoice_reconcile`: 0.50 [n=2, groups=1, matches=1, **insufficient_data**]
  - `task_id=budget_split`: 0.29 [n=7, groups=1, matches=2, **insufficient_data**]
- Timeouts (missed): 6% (4%–17%) [n=78, groups=3, matches=4]; rejected actions per player-round: 0.36 (0.33–0.42) [n=78, groups=3, matches=4]
- Phases overrunning most often:
  - `phase=Break`: 0% (0%–0%) [n=20, groups=3, matches=4]
  - `phase=MeetingDiscussion`: 0% (0%–0%) [n=10, groups=3, matches=4]
  - `phase=MeetingDossier`: 0% (0%–0%) [n=10, groups=3, matches=4]
- Observer: players appeared confused 40% (0%–100%) [n=5, groups=3, matches=4]; meeting felt too short 20% (0%–100%) [n=5, groups=3, matches=4]; too long 20% (0%–33%) [n=5, groups=3, matches=4]

### Potential blame and economy imbalances (concentration is not proof of unfairness)

- Blame leader share at day end: 39% (33%–47%) [n=10, groups=3, matches=4] vs even-split baseline 26% (25%–33%) [n=10, groups=3, matches=4]
- Blame spread: 4.70 (2.29–6.17) [n=10, groups=3, matches=4]; repeat warnings when possible: 67% [n=3, groups=2, matches=3]; capped nominations: 100% [n=2, groups=1, matches=2, **insufficient_data**]
- Warnings on the prior blame leader: 60% (33%–100%) [n=10, groups=3, matches=4]; recovery use / accept: 15% (7%–50%) [n=20, groups=3, matches=4] / 33% (0%–100%) [n=3, groups=3, matches=3]
- Income top share: 33% (30%–40%) [n=4, groups=3, matches=4] vs baseline 27% (25%–33%) [n=4, groups=3, matches=4]; income Gini 0.10 (0.07–0.13) [n=4, groups=3, matches=4]; evidence Gini 0.67 (0.62–0.75) [n=4, groups=3, matches=4]
- Rank 1 is richest: 100% [n=2, groups=1, matches=2, **insufficient_data**] (chance 38% [n=2, groups=1, matches=2, **insufficient_data**]); rank 1 is best quality: 50% [n=2, groups=1, matches=2, **insufficient_data**] (chance 25% [n=2, groups=1, matches=2, **insufficient_data**])
- Sabotage verified/created: InvoicePadding 100% [n=1, groups=1, matches=1, **insufficient_data**]; company closed: 33% [n=3, groups=2, matches=3]

### Voluntary rematches

- Voluntary rematch (groups): 50% (9%–91%) [n=2, groups=2, matches=2]; intent: 100% (34%–100%) [n=2, groups=2, matches=2]; intent -> actual: 50% (9%–91%) [n=2, groups=2, matches=2]
- Observer-coded rematch requests: 40% (0%–67%) [n=5, groups=3, matches=4]; feedback 'play again = Yes': 38% [n=8, groups=2, matches=2]. Auto restarts and moderator-required repeats are excluded; intent is not a rematch.

### Recording quality

- Playback success: 90% [n=10, groups=2, matches=3]; actual duration: 7.00 [n=9, groups=2, matches=3]; with source context: 70% [n=10, groups=2, matches=3]
- Prior exposure share: 30% [n=10, groups=2, matches=3]; participant coverage: 91% [n=11, groups=2, matches=3]; speech density: 53% [n=10, groups=2, matches=3]; per-player speaking time: **not_measured**
- Observer: intelligible 75% [n=4, groups=2, matches=3]; relevant 50% [n=4, groups=2, matches=3]; lacked context 50% [n=4, groups=2, matches=3]; reaction 50% [n=4, groups=2, matches=3]
- Meetings: vote participation 95% (92%–100%) [n=42, groups=3, matches=4]; abstain 5% (0%–8%) [n=39, groups=3, matches=4]; final Fire-nobody share 31% [n=13, groups=2, matches=3]

### Notable and shareable moments (six layers kept apart)

- (1) Automatic mechanical events per completed match: company_failed 0.33 [n=3, groups=2, matches=3]; player_fired 0.00 [n=3, groups=2, matches=3]; sabotage_verified 0.33 [n=3, groups=2, matches=3]
- (2) Player-marked moments: groups with >= 1 67% (21%–94%) [n=3, groups=3, matches=3]; per match 1.50 (0.00–2.00) [n=4, groups=3, matches=4]
- (3) Observer-confirmed: groups with >= 1 notable 67% (21%–94%) [n=3, groups=3, matches=3]; per observed match 0.75 (0.00–1.00) [n=4, groups=3, matches=4]; observation coverage 100% (100%–100%) [n=4, groups=3, matches=4]
- (3) Categories: funny_betrayal 33%; notable_funny_moment 33%; surprising_outcome 33%; unexpected_accusation 33%
- (4) Players wanting to share (feedback Yes): 12% [n=8, groups=2, matches=2]
- (5) Actual export: **not_measured**; (6) external sharing: **not_measured** - no implementation in the game.
- A shareable moment is not predicted virality.

## Stratum 2: build `202611021200-d4e5f6`, ruleset `v4.1-hybrid`

Strata are never pooled. Charts: `charts/*_s2.svg`.

### Largest completion losses

| stage | matches | share of started | groups |
|---|---|---|---|
| match_started | 1 | 100% | 1 |
| round_finished (>=1) | 1 | 100% | 1 |
| meeting_started (>=1) | 1 | 100% | 1 |
| final_started | 1 | 100% | 1 |
| week_completed | 1 | 100% | 1 |

No drop between observed stages.

- Match completion: 100% [n=1, groups=1, matches=1, **insufficient_data**]; unknown ends: 0% [n=1, groups=1, matches=1, **insufficient_data**]
- Abandonment by phase: no abandoned matches observed
- Lowest completion by player count:
  - `player_count=6`: 100% [n=1, groups=1, matches=1, **insufficient_data**]
- Rounds finishing least often by task:
  - `task_id=bonus_pool`: 100% [n=1, groups=1, matches=1, **insufficient_data**]
  - `task_id=budget_split`: 100% [n=1, groups=1, matches=1, **insufficient_data**]
  - `task_id=phishing_check`: 100% [n=1, groups=1, matches=1, **insufficient_data**]

### Lowest communication participation

- Phone participation overall: 62% [n=24, groups=1, matches=1, **insufficient_data**]; observed share of eligible: 100% [n=24, groups=1, matches=1, **insufficient_data**]
- Lowest by round:
  - `day_round=d0_r1`: 0% [n=4, groups=1, matches=1, **insufficient_data**]
  - `day_round=d0_r0`: 50% [n=4, groups=1, matches=1, **insufficient_data**]
  - `day_round=d2_r0`: 50% [n=4, groups=1, matches=1, **insufficient_data**]
- Lowest by task:
  - `task_id=shift_cover`: 0% [n=4, groups=1, matches=1, **insufficient_data**]
  - `task_id=bonus_pool`: 50% [n=4, groups=1, matches=1, **insufficient_data**]
  - `task_id=budget_split`: 50% [n=4, groups=1, matches=1, **insufficient_data**]
- Observed player-rounds with no successful contact: 38% [n=24, groups=1, matches=1, **insufficient_data**]; first-contact latency median: 17.8 [n=15, groups=1, matches=1, **insufficient_data**]
- Busy failures / unanswered requests: 33% [n=18, groups=1, matches=1, **insufficient_data**] / 17% [n=18, groups=1, matches=1, **insufficient_data**]; attempted but never connected: 0% [n=4, groups=1, matches=1, **insufficient_data**]

### Longest interaction gaps and waiting

- Longest gap mean/p90: 55.2 [n=24, groups=1, matches=1, **insufficient_data**] / 88.7 [n=24, groups=1, matches=1, **insufficient_data**] (unknown periods are not zero; observation coverage 100% [n=24, groups=1, matches=1, **insufficient_data**])
- Longest by task:
  - `task_id=budget_split`: 72.2 [n=4, groups=1, matches=1, **insufficient_data**]
  - `task_id=shift_cover`: 60.9 [n=4, groups=1, matches=1, **insufficient_data**]
  - `task_id=room_booking`: 60.4 [n=4, groups=1, matches=1, **insufficient_data**]
- Waiting after submission: 29.8 [n=23, groups=1, matches=1, **insufficient_data**]; >= 20 s: 61% [n=23, groups=1, matches=1, **insufficient_data**]; blocked: 0% [n=24, groups=1, matches=1, **insufficient_data**]
- Worst-served player per group (largest mean gap): G04 64.9s

### Player-count differences (exploratory)

| metric | 6 players |
|---|---|
| completion | 100% [n=1, groups=1, matches=1, **insufficient_data**] |
| phone participation | 62% [n=24, groups=1, matches=1, **insufficient_data**] |
| first contact median s | 17.8 [n=15, groups=1, matches=1, **insufficient_data**] |
| post-submit wait s | 29.8 [n=23, groups=1, matches=1, **insufficient_data**] |
| longest gap s | 55.2 [n=24, groups=1, matches=1, **insufficient_data**] |

Differences with overlapping intervals or `insufficient_data` are not evidence of a player-count effect.

### Recurring technical problems

- Matches with a technical failure: none observed (only caught failures are logged)
- Reconnect success: **not_measured**; non-busy phone failures: 6% [n=18, groups=1, matches=1, **insufficient_data**]; possible crashes (unknown): 0% [n=1, groups=1, matches=0, **insufficient_data**]
- stuck_phase per match: 0.00 [n=1, groups=1, matches=1, **insufficient_data**]; voice loss %: 2.44 [n=6, groups=1, matches=1, **insufficient_data**]; start failures: 0% [n=1, groups=1, matches=1, **insufficient_data**]; join success: 100% [n=4, groups=1, matches=0, **insufficient_data**]
- Data-quality: 0 corrupt and 1 truncated lines were skipped.

### Slow or confusing decisions

- Slowest submissions by task (median latency):
  - `task_id=room_booking`: 68.3 [n=3, groups=1, matches=1, **insufficient_data**]
  - `task_id=budget_split`: 53.7 [n=4, groups=1, matches=1, **insufficient_data**]
  - `task_id=phishing_check`: 46.2 [n=4, groups=1, matches=1, **insufficient_data**]
- Most revisions per submission by task:
  - `task_id=room_booking`: 0.33 [n=3, groups=1, matches=1, **insufficient_data**]
  - `task_id=budget_split`: 0.25 [n=4, groups=1, matches=1, **insufficient_data**]
  - `task_id=server_code`: 0.25 [n=4, groups=1, matches=1, **insufficient_data**]
- Timeouts (missed): 4% [n=24, groups=1, matches=1, **insufficient_data**]; rejected actions per player-round: 0.17 [n=24, groups=1, matches=1, **insufficient_data**]
- Phases overrunning most often:
  - `phase=Break`: 0% [n=6, groups=1, matches=1, **insufficient_data**]
  - `phase=MeetingDiscussion`: 0% [n=3, groups=1, matches=1, **insufficient_data**]
  - `phase=MeetingDossier`: 0% [n=3, groups=1, matches=1, **insufficient_data**]
- Observer: players appeared confused 0% [n=1, groups=1, matches=1, **insufficient_data**]; meeting felt too short 0% [n=1, groups=1, matches=1, **insufficient_data**]; too long 0% [n=1, groups=1, matches=1, **insufficient_data**]

### Potential blame and economy imbalances (concentration is not proof of unfairness)

- Blame leader share at day end: 25% [n=3, groups=1, matches=1, **insufficient_data**] vs even-split baseline 17% [n=3, groups=1, matches=1, **insufficient_data**]
- Blame spread: 3.67 [n=3, groups=1, matches=1, **insufficient_data**]; repeat warnings when possible: 100% [n=1, groups=1, matches=1, **insufficient_data**]; capped nominations: insufficient data (no observations)
- Warnings on the prior blame leader: 67% [n=3, groups=1, matches=1, **insufficient_data**]; recovery use / accept: 0% [n=4, groups=1, matches=1, **insufficient_data**] / **not_measured**
- Income top share: 33% [n=1, groups=1, matches=1, **insufficient_data**] vs baseline 17% [n=1, groups=1, matches=1, **insufficient_data**]; income Gini 0.43 [n=1, groups=1, matches=1, **insufficient_data**]; evidence Gini 0.72 [n=1, groups=1, matches=1, **insufficient_data**]
- Rank 1 is richest: 100% [n=1, groups=1, matches=1, **insufficient_data**] (chance 25% [n=1, groups=1, matches=1, **insufficient_data**]); rank 1 is best quality: 100% [n=1, groups=1, matches=1, **insufficient_data**] (chance 50% [n=1, groups=1, matches=1, **insufficient_data**])
- Sabotage verified/created:  **not_measured**; company closed: 0% [n=1, groups=1, matches=1, **insufficient_data**]

### Voluntary rematches

- Voluntary rematch (groups): 0% (0%–79%) [n=1, groups=1, matches=1, **insufficient_data**]; intent: 100% (21%–100%) [n=1, groups=1, matches=1, **insufficient_data**]; intent -> actual: 0% (0%–79%) [n=1, groups=1, matches=1, **insufficient_data**]
- Observer-coded rematch requests: 100% [n=1, groups=1, matches=1, **insufficient_data**]; feedback 'play again = Yes': 0% [n=4, groups=1, matches=1, **insufficient_data**]. Auto restarts and moderator-required repeats are excluded; intent is not a rematch.

### Recording quality

- Playback success: 100% [n=5, groups=1, matches=1, **insufficient_data**]; actual duration: 7.00 [n=5, groups=1, matches=1, **insufficient_data**]; with source context: 40% [n=5, groups=1, matches=1, **insufficient_data**]
- Prior exposure share: 40% [n=5, groups=1, matches=1, **insufficient_data**]; participant coverage: 67% [n=6, groups=1, matches=1, **insufficient_data**]; speech density: 70% [n=5, groups=1, matches=1, **insufficient_data**]; per-player speaking time: **not_measured**
- Observer: intelligible 100% [n=1, groups=1, matches=1, **insufficient_data**]; relevant 100% [n=1, groups=1, matches=1, **insufficient_data**]; lacked context 0% [n=1, groups=1, matches=1, **insufficient_data**]; reaction 100% [n=1, groups=1, matches=1, **insufficient_data**]
- Meetings: vote participation 92% [n=12, groups=1, matches=1, **insufficient_data**]; abstain 8% [n=12, groups=1, matches=1, **insufficient_data**]; final Fire-nobody share 50% [n=4, groups=1, matches=1, **insufficient_data**]

### Notable and shareable moments (six layers kept apart)

- (1) Automatic mechanical events per completed match: company_failed 0.00 [n=1, groups=1, matches=1, **insufficient_data**]; player_fired 0.00 [n=1, groups=1, matches=1, **insufficient_data**]; sabotage_verified 0.00 [n=1, groups=1, matches=1, **insufficient_data**]
- (2) Player-marked moments: groups with >= 1 100% (21%–100%) [n=1, groups=1, matches=1, **insufficient_data**]; per match 1.00 [n=1, groups=1, matches=1, **insufficient_data**]
- (3) Observer-confirmed: groups with >= 1 notable 0% (0%–79%) [n=1, groups=1, matches=1, **insufficient_data**]; per observed match 0.00 [n=1, groups=1, matches=1, **insufficient_data**]; observation coverage 100% [n=1, groups=1, matches=1, **insufficient_data**]
- (3) Categories: no confirmed moments
- (4) Players wanting to share (feedback Yes): 50% [n=4, groups=1, matches=1, **insufficient_data**]
- (5) Actual export: **not_measured**; (6) external sharing: **not_measured** - no implementation in the game.
- A shareable moment is not predicted virality.

## Next-playtest hypotheses (investigation suggestions, not verdicts)

1. **H2** - Investigate whether players at 3, 4, 5 and 6 get comparable chances to call and act: compare phone participation, waiting after submission and worst-served gaps by player count, and check whether the early_submit carry setting changes waiting. (evidence: phone participation 69%)
2. **H6** - Investigate whether a player hit by early blame actually finds and uses remedial work, and whether blame concentration persists to the last day. (evidence: recovery use 15%)
3. **H7** - Investigate whether the richest or highest-quality player tends to finish first more often than chance, and whether the company closes at a plausible rate. (evidence: rank 1 is richest in 100% of matches)
4. **H3** - Investigate whether the seven-second reveals are intelligible and relevant: pair playback telemetry with observer coding of relevance/context, and compare rec_select legacy vs context across groups. (evidence: metrics H.playback_success, H.context_share not yet ok (insufficient or not measured) - gather data first)
5. **H1** - Investigate whether rounds with no communication look as good as rounds with contact, per task, while listing confounders (task design, role, skill). (evidence: metrics E.silent_not_dominant, E.quality not yet ok (insufficient or not measured) - gather data first)

## Reference

- Metrics: `PLAYTEST_METRICS.csv` (one row per metric x stratum x partition x grouping). Per-group: `PLAYTEST_GROUP_COMPARISON.csv`. Funnels: `PLAYTEST_FUNNELS.csv`.
- KPI definitions and the H1-H9 plan: `docs/PLAYTEST_KPI_GUIDE.md`. Statuses: ok / insufficient_data / not_measured.
- Charts: `charts/funnel_match_progress_s1.svg`, `charts/completion_by_player_count_s1.svg`, `charts/phone_participation_by_round_s1.svg`, `charts/phone_participation_by_player_count_s1.svg`, `charts/first_contact_latency_median_by_player_count_s1.svg`, `charts/longest_gap_by_task_s1.svg`, `charts/post_submit_wait_by_player_count_s1.svg`, `charts/warning_meeting_outcomes_s1.svg`, `charts/blame_top_share_by_day_s1.svg`, `charts/phone_participation_by_group_s1.svg`, `charts/rematch_and_notable_groups_s1.svg`, `charts/funnel_match_progress_s2.svg`, `charts/completion_by_player_count_s2.svg`, `charts/phone_participation_by_round_s2.svg`, `charts/phone_participation_by_player_count_s2.svg`, `charts/first_contact_latency_median_by_player_count_s2.svg`, `charts/longest_gap_by_task_s2.svg`, `charts/post_submit_wait_by_player_count_s2.svg`, `charts/warning_meeting_outcomes_s2.svg`, `charts/blame_top_share_by_day_s2.svg`, `charts/phone_participation_by_group_s2.svg`, `charts/rematch_and_notable_groups_s2.svg`
- Invocation: `python -I Tools/scripts/analyze_playtests.py jsonl --include-synthetic`
