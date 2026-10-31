# SYNTHETIC — not playtest evidence

> **SYNTHETIC — not playtest evidence** - this report contains synthetic data (fixtures, bot simulations or bot matches). Do not use it for design conclusions.

# Playtest summary

Generated: 2026-10-31 23:35 UTC

10–20 friend groups are **exploratory evidence**: they can show where to look and which numbers are implausible, not that a mechanic works. Intervals resample whole groups; players of one group are not independent and repeated matches of one group are not new groups.

## 1. Data and coverage

- Files read: 18 (recursive, rotated `events_<session>_<n>.jsonl` included); lines 3352; accepted events 3352 (host 2861, client 491)
- Duplicate events removed (same event_id across host/client/re-imported files): 0
- Truncated final lines: 0; corrupt lines: 0; rejected by envelope validation: 0
- Event catalog: loaded (95 events), schema issues: 0
- Unknown event names: none
- Contexts analysed: 5 matches, 4 groups with matches (18 pre-lobby client sessions not linked to a group); strata (build | ruleset): 0.4.0+202610312257-ffdef6 | v4.0-hybrid; 0.4.0+202610312322-82c509 | v4.0-hybrid
- Excluded as synthetic (not human playtest data): none
- Observer rows linked: 0; feedback rows linked: 0

Matches by player count: 3 players: 1, 4 players: 2, 5 players: 1, 6 players: 1

## Stratum 1: build `0.4.0+202610312257-ffdef6`, ruleset `v4.0-hybrid`

Strata are never pooled. Charts: `charts/*_s1.svg`.

### Largest completion losses

| stage | matches | share of started | groups |
|---|---|---|---|
| match_started | 3 | 100% | 3 |
| round_finished (>=1) | 3 | 100% | 3 |
| meeting_started (>=1) | 3 | 100% | 3 |
| final_started | 3 | 100% | 3 |
| week_completed | 3 | 100% | 3 |

No drop between observed stages.

- Match completion: 100% (100%–100%) [n=3, groups=3, matches=3]; unknown ends: 0% (0%–0%) [n=3, groups=3, matches=3]
- Abandonment by phase: no abandoned matches observed
- Lowest completion by player count:
  - `player_count=3`: 100% [n=1, groups=1, matches=1, **insufficient_data**]
  - `player_count=5`: 100% [n=1, groups=1, matches=1, **insufficient_data**]
  - `player_count=6`: 100% [n=1, groups=1, matches=1, **insufficient_data**]
- Rounds finishing least often by task:
  - `task_id=archive_visitor`: 100% (100%–100%) [n=3, groups=3, matches=3]
  - `task_id=bonus_pool`: 100% (100%–100%) [n=3, groups=3, matches=3]
  - `task_id=budget_split`: 100% [n=1, groups=1, matches=1, **insufficient_data**]

### Lowest communication participation

- Phone participation overall: 56% (39%–67%) [n=104, groups=3, matches=3]; observed share of eligible: 100% (100%–100%) [n=104, groups=3, matches=3]
- Lowest by round:
  - `day_round=d0_r0`: 29% (0%–67%) [n=14, groups=3, matches=3]
  - `day_round=d0_r1`: 29% (0%–67%) [n=14, groups=3, matches=3]
  - `day_round=d3_r0`: 40% [n=5, groups=1, matches=1, **insufficient_data**]
- Lowest by task:
  - `task_id=bonus_pool`: 29% (0%–67%) [n=14, groups=3, matches=3]
  - `task_id=customer_spec`: 29% (0%–67%) [n=14, groups=3, matches=3]
  - `task_id=invoice_reconcile`: 29% (0%–55%) [n=14, groups=3, matches=3]
- Observed player-rounds with no successful contact: 0% (0%–0%) [n=104, groups=3, matches=3]; first-contact latency median: 0.17 (0.10–0.17) [n=104, groups=3, matches=3]
- Busy failures / unanswered requests: 46% (36%–50%) [n=54, groups=3, matches=3] / 0% (0%–0%) [n=54, groups=3, matches=3]; attempted but never connected: 0% (0%–0%) [n=12, groups=3, matches=3]

### Longest interaction gaps and waiting

- Longest gap mean/p90: 11.3 (8.37–15.4) [n=104, groups=3, matches=3] / 18.4 (14.3–18.7) [n=104, groups=3, matches=3] (unknown periods are not zero; observation coverage 100% (100%–100%) [n=103, groups=3, matches=3])
- Longest by task:
  - `task_id=archive_visitor`: 13.6 (6.32–17.2) [n=14, groups=3, matches=3]
  - `task_id=bonus_pool`: 13.3 (4.27–18.7) [n=14, groups=3, matches=3]
  - `task_id=customer_spec`: 13.1 (4.21–18.4) [n=14, groups=3, matches=3]
- Waiting after submission: 2.97 (2.42–3.58) [n=103, groups=3, matches=3]; >= 20 s: 0% (0%–0%) [n=103, groups=3, matches=3]; blocked: 0% (0%–0%) [n=104, groups=3, matches=3]
- Worst-served player per group (largest mean gap): g-055e7f7d8e3b 16.4s; g-edff7e13d209 12.0s; g-60347e0e520d 6.7s

### Player-count differences (exploratory)

| metric | 3 players | 5 players | 6 players |
|---|---|---|---|
| completion | 100% [n=1, groups=1, matches=1, **insufficient_data**] | 100% [n=1, groups=1, matches=1, **insufficient_data**] | 100% [n=1, groups=1, matches=1, **insufficient_data**] |
| phone participation | 67% [n=18, groups=1, matches=1, **insufficient_data**] | 64% [n=50, groups=1, matches=1, **insufficient_data**] | 39% [n=36, groups=1, matches=1, **insufficient_data**] |
| first contact median s | 0.08 [n=18, groups=1, matches=1, **insufficient_data**] | 0.17 [n=50, groups=1, matches=1, **insufficient_data**] | 0.17 [n=36, groups=1, matches=1, **insufficient_data**] |
| post-submit wait s | 3.58 [n=18, groups=1, matches=1, **insufficient_data**] | 3.14 [n=49, groups=1, matches=1, **insufficient_data**] | 2.42 [n=36, groups=1, matches=1, **insufficient_data**] |
| longest gap s | 5.47 [n=18, groups=1, matches=1, **insufficient_data**] | 10.5 [n=50, groups=1, matches=1, **insufficient_data**] | 15.4 [n=36, groups=1, matches=1, **insufficient_data**] |

Differences with overlapping intervals or `insufficient_data` are not evidence of a player-count effect.

### Recurring technical problems

- Matches with a technical failure: none observed (only caught failures are logged)
- Reconnect success: 100% [n=1, groups=1, matches=1, **insufficient_data**]; non-busy phone failures: 0% (0%–0%) [n=54, groups=3, matches=3]; possible crashes (unknown): 0% (0%–0%) [n=14, groups=14, matches=0]
- stuck_phase per match: 0.00 (0.00–0.00) [n=3, groups=3, matches=3]; voice loss %: **not_measured**; start failures: 0% (0%–0%) [n=3, groups=3, matches=0]; join success: 100% (100%–100%) [n=11, groups=11, matches=0]

### Slow or confusing decisions

- Slowest submissions by task (median latency):
  - `task_id=shift_cover`: 18.6 [n=9, groups=2, matches=2]
  - `task_id=phishing_check`: 18.6 [n=11, groups=2, matches=2]
  - `task_id=supply_swap`: 18.4 [n=24, groups=2, matches=2]
- Most revisions per submission by task:
  - `task_id=server_code`: 8.80 [n=5, groups=1, matches=1, **insufficient_data**]
  - `task_id=phishing_check`: 6.18 [n=11, groups=2, matches=2]
  - `task_id=archive_visitor`: 0.00 (0.00–0.00) [n=14, groups=3, matches=3]
- Timeouts (missed): 0% (0%–0%) [n=123, groups=3, matches=3]; rejected actions per player-round: 0.54 (0.33–0.70) [n=104, groups=3, matches=3]
- Phases overrunning most often:
  - `phase=break`: 0% (0%–0%) [n=14, groups=3, matches=3]
  - `phase=final_verdict`: 0% (0%–0%) [n=3, groups=3, matches=3]
  - `phase=meeting_discussion`: 0% (0%–0%) [n=11, groups=3, matches=3]
- Observer: players appeared confused **not_measured**; meeting felt too short **not_measured**; too long **not_measured**

### Potential blame and economy imbalances (concentration is not proof of unfairness)

- Blame leader share at day end: 36% (20%–45%) [n=11, groups=3, matches=3] vs even-split baseline 23% (17%–28%) [n=11, groups=3, matches=3]
- Blame spread: 2.91 (2.73–3.33) [n=11, groups=3, matches=3]; repeat warnings when possible: **not_measured**; capped nominations: insufficient data (no observations)
- Warnings on the prior blame leader: 0% [n=4, groups=1, matches=1, **insufficient_data**]; recovery use / accept: insufficient data (no observations) / 100% (100%–100%) [n=17, groups=3, matches=3]
- Income top share: 33% (27%–44%) [n=3, groups=3, matches=3] vs baseline 23% (17%–33%) [n=3, groups=3, matches=3]; income Gini 0.17 (0.13–0.22) [n=3, groups=3, matches=3]; evidence Gini 0.21 (0.18–0.23) [n=3, groups=3, matches=3]
- Rank 1 is richest: **not_measured** (chance **not_measured**); rank 1 is best quality: **not_measured** (chance **not_measured**)
- Sabotage verified/created: accepted_kickback 0% [n=1, groups=1, matches=1, **insufficient_data**]; invoice_padding 0% [n=1, groups=1, matches=1, **insufficient_data**]; vendor_kickback 0% [n=1, groups=1, matches=1, **insufficient_data**]; company closed: 100% (100%–100%) [n=3, groups=3, matches=3]

### Voluntary rematches

- Voluntary rematch (groups): 0% (0%–56%) [n=3, groups=3, matches=3]; intent: 0% (0%–56%) [n=3, groups=3, matches=3]; intent -> actual: **not_measured**
- Observer-coded rematch requests: **not_measured**; feedback 'play again = Yes': no row. Auto restarts and moderator-required repeats are excluded; intent is not a rematch.

### Recording quality

- Playback success: 100% (100%–100%) [n=10, groups=3, matches=3]; actual duration: 6.56 (6.28–7.50) [n=10, groups=3, matches=3]; with source context: 0% (0%–0%) [n=10, groups=3, matches=3]
- Prior exposure share: 0% (0%–0%) [n=10, groups=3, matches=3]; participant coverage: 86% (67%–100%) [n=14, groups=3, matches=3]; speech density: 78% (71%–81%) [n=10, groups=3, matches=3]; per-player speaking time: **not_measured**
- Observer: intelligible **not_measured**; relevant **not_measured**; lacked context **not_measured**; reaction **not_measured**
- Meetings: vote participation 100% (100%–100%) [n=38, groups=3, matches=3]; abstain 0% (0%–0%) [n=38, groups=3, matches=3]; final Fire-nobody share **not_measured**

### Notable and shareable moments (six layers kept apart)

- (1) Automatic mechanical events per completed match: company_failed 1.00 (1.00–1.00) [n=3, groups=3, matches=3]; player_fired 0.00 (0.00–0.00) [n=3, groups=3, matches=3]; sabotage_verified 0.00 (0.00–0.00) [n=3, groups=3, matches=3]
- (2) Player-marked moments: groups with >= 1 0% (0%–56%) [n=3, groups=3, matches=3]; per match 0.00 (0.00–0.00) [n=3, groups=3, matches=3]
- (3) Observer-confirmed: groups with >= 1 notable **not_measured**; per observed match **not_measured**; observation coverage 0% (0%–0%) [n=3, groups=3, matches=3]
- (3) Categories: no confirmed moments
- (4) Players wanting to share (feedback Yes): no row
- (5) Actual export: **not_measured**; (6) external sharing: **not_measured** - no implementation in the game.
- A shareable moment is not predicted virality.

## Stratum 2: build `0.4.0+202610312322-82c509`, ruleset `v4.0-hybrid`

Strata are never pooled. Charts: `charts/*_s2.svg`.

### Largest completion losses

| stage | matches | share of started | groups |
|---|---|---|---|
| match_started | 2 | 100% | 1 |
| round_finished (>=1) | 2 | 100% | 1 |
| meeting_started (>=1) | 2 | 100% | 1 |
| final_started | 2 | 100% | 1 |
| week_completed | 2 | 100% | 1 |

No drop between observed stages.

- Match completion: 100% [n=2, groups=1, matches=2, **insufficient_data**]; unknown ends: 0% [n=2, groups=1, matches=2, **insufficient_data**]
- Abandonment by phase: no abandoned matches observed
- Lowest completion by player count:
  - `player_count=4`: 100% [n=2, groups=1, matches=2, **insufficient_data**]
- Rounds finishing least often by task:
  - `task_id=archive_visitor`: 100% [n=1, groups=1, matches=1, **insufficient_data**]
  - `task_id=bonus_pool`: 100% [n=1, groups=1, matches=1, **insufficient_data**]
  - `task_id=budget_split`: 100% [n=1, groups=1, matches=1, **insufficient_data**]

### Lowest communication participation

- Phone participation overall: 58% [n=48, groups=1, matches=2, **insufficient_data**]; observed share of eligible: 100% [n=48, groups=1, matches=2, **insufficient_data**]
- Lowest by round:
  - `day_round=d0_r1`: 0% [n=8, groups=1, matches=2, **insufficient_data**]
  - `day_round=d0_r0`: 25% [n=8, groups=1, matches=2, **insufficient_data**]
  - `day_round=d2_r0`: 50% [n=8, groups=1, matches=2, **insufficient_data**]
- Lowest by task:
  - `task_id=bonus_pool`: 0% [n=4, groups=1, matches=1, **insufficient_data**]
  - `task_id=customer_spec`: 0% [n=4, groups=1, matches=1, **insufficient_data**]
  - `task_id=invoice_reconcile`: 0% [n=4, groups=1, matches=1, **insufficient_data**]
- Observed player-rounds with no successful contact: 0% [n=48, groups=1, matches=2, **insufficient_data**]; first-contact latency median: 0.12 [n=48, groups=1, matches=2, **insufficient_data**]
- Busy failures / unanswered requests: 44% [n=25, groups=1, matches=2, **insufficient_data**] / 0% [n=25, groups=1, matches=2, **insufficient_data**]; attempted but never connected: 0% [n=7, groups=1, matches=2, **insufficient_data**]

### Longest interaction gaps and waiting

- Longest gap mean/p90: 8.16 [n=48, groups=1, matches=2, **insufficient_data**] / 12.5 [n=48, groups=1, matches=2, **insufficient_data**] (unknown periods are not zero; observation coverage 100% [n=48, groups=1, matches=2, **insufficient_data**])
- Longest by task:
  - `task_id=invoice_reconcile`: 12.5 [n=4, groups=1, matches=1, **insufficient_data**]
  - `task_id=customer_spec`: 12.5 [n=4, groups=1, matches=1, **insufficient_data**]
  - `task_id=bonus_pool`: 12.3 [n=4, groups=1, matches=1, **insufficient_data**]
- Waiting after submission: 3.02 [n=48, groups=1, matches=2, **insufficient_data**]; >= 20 s: 0% [n=48, groups=1, matches=2, **insufficient_data**]; blocked: 0% [n=48, groups=1, matches=2, **insufficient_data**]
- Worst-served player per group (largest mean gap): g-5ae82c35b325 8.9s

### Player-count differences (exploratory)

| metric | 4 players |
|---|---|
| completion | 100% [n=2, groups=1, matches=2, **insufficient_data**] |
| phone participation | 58% [n=48, groups=1, matches=2, **insufficient_data**] |
| first contact median s | 0.12 [n=48, groups=1, matches=2, **insufficient_data**] |
| post-submit wait s | 3.02 [n=48, groups=1, matches=2, **insufficient_data**] |
| longest gap s | 8.16 [n=48, groups=1, matches=2, **insufficient_data**] |

Differences with overlapping intervals or `insufficient_data` are not evidence of a player-count effect.

### Recurring technical problems

- Matches with a technical failure: none observed (only caught failures are logged)
- Reconnect success: insufficient data (no observations); non-busy phone failures: 0% [n=25, groups=1, matches=2, **insufficient_data**]; possible crashes (unknown): 0% (0%–0%) [n=4, groups=4, matches=0]
- stuck_phase per match: 0.00 [n=2, groups=1, matches=2, **insufficient_data**]; voice loss %: **not_measured**; start failures: 0% [n=2, groups=1, matches=0, **insufficient_data**]; join success: 100% (100%–100%) [n=3, groups=3, matches=0]

### Slow or confusing decisions

- Slowest submissions by task (median latency):
  - `task_id=supply_swap`: 15.3 [n=12, groups=1, matches=1, **insufficient_data**]
  - `task_id=archive_visitor`: 15.0 [n=4, groups=1, matches=1, **insufficient_data**]
  - `task_id=room_booking`: 12.7 [n=4, groups=1, matches=1, **insufficient_data**]
- Most revisions per submission by task:
  - `task_id=server_code`: 8.50 [n=4, groups=1, matches=1, **insufficient_data**]
  - `task_id=phishing_check`: 4.75 [n=4, groups=1, matches=1, **insufficient_data**]
  - `task_id=archive_visitor`: 0.00 [n=4, groups=1, matches=1, **insufficient_data**]
- Timeouts (missed): 0% [n=59, groups=1, matches=2, **insufficient_data**]; rejected actions per player-round: 0.71 [n=48, groups=1, matches=2, **insufficient_data**]
- Phases overrunning most often:
  - `phase=break`: 0% [n=8, groups=1, matches=2, **insufficient_data**]
  - `phase=final_defense`: 0% [n=1, groups=1, matches=1, **insufficient_data**]
  - `phase=final_verdict`: 0% [n=2, groups=1, matches=2, **insufficient_data**]
- Observer: players appeared confused **not_measured**; meeting felt too short **not_measured**; too long **not_measured**

### Potential blame and economy imbalances (concentration is not proof of unfairness)

- Blame leader share at day end: 35% [n=6, groups=1, matches=2, **insufficient_data**] vs even-split baseline 25% [n=6, groups=1, matches=2, **insufficient_data**]
- Blame spread: 2.33 [n=6, groups=1, matches=2, **insufficient_data**]; repeat warnings when possible: **not_measured**; capped nominations: insufficient data (no observations)
- Warnings on the prior blame leader: insufficient data (no observations); recovery use / accept: insufficient data (no observations) / 100% [n=9, groups=1, matches=2, **insufficient_data**]
- Income top share: 35% [n=2, groups=1, matches=2, **insufficient_data**] vs baseline 25% [n=2, groups=1, matches=2, **insufficient_data**]; income Gini 0.14 [n=2, groups=1, matches=2, **insufficient_data**]; evidence Gini 0.22 [n=2, groups=1, matches=2, **insufficient_data**]
- Rank 1 is richest: 0% [n=1, groups=1, matches=1, **insufficient_data**] (chance 25% [n=1, groups=1, matches=1, **insufficient_data**]); rank 1 is best quality: 100% [n=1, groups=1, matches=1, **insufficient_data**] (chance 75% [n=1, groups=1, matches=1, **insufficient_data**])
- Sabotage verified/created:  **not_measured**; company closed: 50% [n=2, groups=1, matches=2, **insufficient_data**]

### Voluntary rematches

- Voluntary rematch (groups): 0% (0%–79%) [n=1, groups=1, matches=1, **insufficient_data**]; intent: 0% (0%–79%) [n=1, groups=1, matches=1, **insufficient_data**]; intent -> actual: insufficient data (no observations)
- Observer-coded rematch requests: **not_measured**; feedback 'play again = Yes': no row. Auto restarts and moderator-required repeats are excluded; intent is not a rematch.

### Recording quality

- Playback success: 100% [n=5, groups=1, matches=2, **insufficient_data**]; actual duration: 5.52 [n=5, groups=1, matches=2, **insufficient_data**]; with source context: 100% [n=5, groups=1, matches=2, **insufficient_data**]
- Prior exposure share: 0% [n=5, groups=1, matches=2, **insufficient_data**]; participant coverage: 88% [n=8, groups=1, matches=2, **insufficient_data**]; speech density: 87% [n=5, groups=1, matches=2, **insufficient_data**]; per-player speaking time: **not_measured**
- Observer: intelligible **not_measured**; relevant **not_measured**; lacked context **not_measured**; reaction **not_measured**
- Meetings: vote participation 100% [n=16, groups=1, matches=2, **insufficient_data**]; abstain 0% [n=16, groups=1, matches=2, **insufficient_data**]; final Fire-nobody share 100% [n=4, groups=1, matches=1, **insufficient_data**]

### Notable and shareable moments (six layers kept apart)

- (1) Automatic mechanical events per completed match: company_failed 0.50 [n=2, groups=1, matches=2, **insufficient_data**]; player_fired 0.00 [n=2, groups=1, matches=2, **insufficient_data**]; sabotage_verified 0.00 [n=2, groups=1, matches=2, **insufficient_data**]
- (2) Player-marked moments: groups with >= 1 0% (0%–79%) [n=1, groups=1, matches=1, **insufficient_data**]; per match 0.00 [n=2, groups=1, matches=2, **insufficient_data**]
- (3) Observer-confirmed: groups with >= 1 notable **not_measured**; per observed match **not_measured**; observation coverage 0% [n=2, groups=1, matches=2, **insufficient_data**]
- (3) Categories: no confirmed moments
- (4) Players wanting to share (feedback Yes): no row
- (5) Actual export: **not_measured**; (6) external sharing: **not_measured** - no implementation in the game.
- A shareable moment is not predicted virality.

## Next-playtest hypotheses (investigation suggestions, not verdicts)

1. **H2** - Investigate whether players at 3, 4, 5 and 6 get comparable chances to call and act: compare phone participation, waiting after submission and worst-served gaps by player count, and check whether the early_submit carry setting changes waiting. (evidence: phone participation 56%)
2. **H3** - Investigate whether the seven-second reveals are intelligible and relevant: pair playback telemetry with observer coding of relevance/context, and compare rec_select legacy vs context across groups. (evidence: context share 0%)
3. **H1** - Investigate whether rounds with no communication look as good as rounds with contact, per task, while listing confounders (task design, role, skill). (evidence: metrics E.silent_not_dominant, E.quality not yet ok (insufficient or not measured) - gather data first)
4. **H4** - Investigate whether telling players that recordings are on changes how often and how long they call: run the recording_awareness on/off lobby toggle with whole groups per arm. (evidence: metrics C.phone_participation not yet ok (insufficient or not measured) - gather data first)
5. **H5** - Investigate whether accusations in meetings point to observable evidence or repeatedly to the same person, using observer coding together with warning-target concentration. (evidence: metrics I.target_was_blame_leader, O.accusation_evidence not yet ok (insufficient or not measured) - gather data first)

## Reference

- Metrics: `PLAYTEST_METRICS.csv` (one row per metric x stratum x partition x grouping). Per-group: `PLAYTEST_GROUP_COMPARISON.csv`. Funnels: `PLAYTEST_FUNNELS.csv`.
- KPI definitions and the H1-H9 plan: `docs/PLAYTEST_KPI_GUIDE.md`. Statuses: ok / insufficient_data / not_measured.
- Charts: `charts/funnel_match_progress_s1.svg`, `charts/completion_by_player_count_s1.svg`, `charts/phone_participation_by_round_s1.svg`, `charts/phone_participation_by_player_count_s1.svg`, `charts/first_contact_latency_median_by_player_count_s1.svg`, `charts/longest_gap_by_task_s1.svg`, `charts/post_submit_wait_by_player_count_s1.svg`, `charts/warning_meeting_outcomes_s1.svg`, `charts/blame_top_share_by_day_s1.svg`, `charts/phone_participation_by_group_s1.svg`, `charts/rematch_and_notable_groups_s1.svg`, `charts/funnel_match_progress_s2.svg`, `charts/completion_by_player_count_s2.svg`, `charts/phone_participation_by_round_s2.svg`, `charts/phone_participation_by_player_count_s2.svg`, `charts/first_contact_latency_median_by_player_count_s2.svg`, `charts/longest_gap_by_task_s2.svg`, `charts/post_submit_wait_by_player_count_s2.svg`, `charts/warning_meeting_outcomes_s2.svg`, `charts/blame_top_share_by_day_s2.svg`, `charts/phone_participation_by_group_s2.svg`, `charts/rematch_and_notable_groups_s2.svg`
- Invocation: `python -I Tools/scripts/analyze_playtests.py bot_runs_telemetry --include-synthetic`
