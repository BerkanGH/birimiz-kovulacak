# Rules engine v2 integration notes

## Implemented

- Stage 1: present-player iteration (3–6, sparse lobby ids), desk assignment, absent nameplate filtering, host `Players` / `CallPartnerOf` API, 12 sabotage cooldowns, complete protocol v2 and deep copies.
- Stage 2: private config copies; profile selection and overrides; observation/evidence multipliers; public favour and verdict discounts; surprise inspection scheduling/truncation; final/profile/tip-aware drawer gate.
- Stage 3: private sabotage card hands; fake emails/screen sessions; plug and salt actions/counters; internet and power outages with restore/timeout; paused printer jobs; crouch/dark vision and witness limits; incident limits/consequences and private salt views.
- Stage 4: private calls, lifecycle/cooldowns, host voice partners, anonymous queued tips and priority drawer visits, Informant log snapshots, presented call/taste/search evidence, result/debug call lists.
- Stage 5: deterministic addressed mail, capped arrivals, exactly-once delivery rewards, any-four-of-five all-rounder progress, public emotes with independent cooldowns.
- Stage 6: preallocated private position log, camera/time/dark filters, meeting request allowances, footage evidence and recipient-specific remaining charges.
- Stage 7: week/career totals and final winners, next-day/reset history, all result/debug reveals, new recap keys/titles, all new world affordances and precise visible-state blocking hints.
- Stage 8: differential encoded-view/event privacy tests for every new secret, completion races and shared limits, malformed-request matrices, deterministic all-39-verb six-player replay, full five-day six-player week, and measured performance/allocation budgets.

## Interpretations

- Host debug per-player lists are in ascending present-id order; map them using `MatchEngine.Players`. Per-player configuration/test-hook lists remain indexed by lobby id.
- Initial/periodic dirt is generated only on assigned desks. Empty desks remain valid item destinations.
- Forced objective cards must be distinct; invalid forced duplicates throw `ArgumentException` during host construction.
- Unknown explicit manager profiles (when profiles exist) and invalid week days reject host setup. No profiles gives base weights/empty profile id but still uses v2 drawer rules. Surprise fraction clamps to [0,1]; phase boundaries take precedence. Duplicate observation factors use the first entry.
- Fake emails remain active desk effects for anti-grief limits because their evidence is irreversible. Salt starts with no victim; delivery records the deliverer as victim without changing the actor. Tasting consumes the salt immediately, leaving knowledge in the taster's private note.
- Tips queue when completed, deduplicate desks for one extra dwell, and use apparent-nameplate attribution like other manager observations. Incoming calls do not check the callee's cooldown (only the caller's, per normative text).
- Mail intervals restart when Work1/Work2 begins; arrivals stop during inspections. Open mail means room/carried, exactly as specified (floor/desk mail does not count). The mailroom is a system-filled slot, like the printer tray, so player placement into it is rejected.
- Footage starts at match start, excludes the exact meeting boundary, and retains the latest 32,768 samples for extreme custom schedules (default full matches fit without eviction). Nonpositive sample intervals use 1 second. Empty windows are empty recordings, not all-dark recordings. An Informant speaker spends their speaker allowance first.
- Career multiplies only the quota-met term, following the normative formula; midpoint rounding is away from zero. Quick play preserves incoming career/day history and adds its completed manager profile. New titles take priority over old titles while retaining the two-title limit. Personal calls, excluding tips, count for scam artist. Restoring one's own outage can earn IT hero but gives no reversal credit.

## Known gaps

- No known unimplemented v2 engine rules. Unity/network/UI/voice integration was not run in this owned-core worktree.
- Git staging is blocked: Windows denied creation of `.git/worktrees/codex-engine-v2/index.lock`. No permissions or ACL changes attempted. Following the work package, implementation continues and the lead must commit the owned paths. Suggested stage 1 message: `Support sparse 3–6 player rosters and protocol v2` followed by a blank line and `Co-Authored-By: Codex <noreply@openai.com>`.
- Staging was attempted at all eight stage boundaries and remained blocked. No commits, pushes, or history rewrites were performed. All code/test/documentation changes remain in `codex/engine-v2`.

Adapter responsibilities:

- Upgrade both sides to Protocol 2. Iterate `engine.Players` (ascending present ids), use each player's assigned `DeskIndex`, and treat absent lobby ids and unassigned desks separately. Host debug per-player lists align with `Players`.
- Voice routing uses `CallPartnerOf(id)`; it returns a partner only for an active call, otherwise -1. Continue filtering events by `Audience`/`AudiencePlayer`; private call events contain partner identities. Calls do not replace physical-world proximity voice routing by the adapter.
- Use `engine.Config` for the privately adjusted profile config and `view.Features` for effective day/config switches. Send `AvatarSample.Crouched`. Render the new office, screen, plug, mail, favour, inbox, emote and call fields from recipient snapshots.
- Fake-email affordances have `IntParam = -1`: open the template picker, then submit a valid template index. Mail delivery is still `Place` on the addressed surface. Tasting has no target; the engine canonicalizes it to an empty target even when selected through a mug affordance.
- Calls, tips, call logs, footage and emotes are phone/meeting UI actions. Footage `IntParam` is seconds since match start, not absolute host time. During an internet outage `PrintEndsAt` is positive infinity for a paused current job; display a paused state using `view.Office.InternetDown`.
- For the next week match use `BuildNextHistory()` and set `MatchSetup.WeekDay` from `NextWeekDay`. Finale results retain final totals/winners; the next history resets career points and increments the week number. Abandoned matches preserve career/day data.
- The inherited reconnect-token echo limitation remains: the adapter owns the token and may populate `Me.ReconnectToken` on the detached view. No new contract change is needed.

## Contract change requests

- None. Contracts and `.meta` files were not modified.

## Tests

- Baseline harness uses the preinstalled NuGet cache: `dotnet restore --ignore-failed-sources -p:NuGetAudit=false --packages C:/Users/agdem/.nuget/packages` in `Tools/CoreTests/Tests`.
- Stage counts and timings are recorded below as stages finish.
- Stage 1: **147 passed**, including 14 variable-roster cases and 26 codec cases (142 individual result-field hash mutations).
- Stage 2: **165 passed**, including 18 profile/favour/inspection cases. Suggested commit: `Apply manager profiles, favour, surprise inspections and drawer rules` (same coauthor trailer).
- Stage 3: **199 passed**, including 34 sabotage/session/outage cases. Suggested commit: `Add secret sabotage cards, screen sessions, outages and counter-actions` (same coauthor trailer).
- Stage 4: **229 passed**, including 29 call/tip cases and the neutral brewing/salting witness privacy regression. Suggested commit: `Add private calls, anonymous manager tips and call log evidence` (same coauthor trailer).
- Stage 5: **248 passed**, including 19 mail/emote cases. Suggested commit: `Add addressed mail tasks and public emotes` (same coauthor trailer).
- Stage 6: **264 passed**, including 16 footage cases. Suggested commit: `Record bounded CCTV samples and present filtered meeting footage` (same coauthor trailer).
- Stage 7: **300 passed**, including 13 career/result cases and 23 affordance cases. Suggested commit: `Complete week careers, result reveals and v2 world affordances` (same coauthor trailer).
- Stage 8: **347 passed, 0 failed, 0 skipped**, including 9 differential byte privacy cases, 33 limit/malformed-input cases, 2 full replay/week cases and 3 six-player performance cases. Suggested commit: `Harden v2 privacy, input handling, determinism and performance` (same coauthor trailer).
- Final command: `dotnet test --no-restore --logger "console;verbosity=normal"` in `Tools/CoreTests/Tests`; C# 9 / netstandard2.1 core and NUnit tests compile without compiler warnings. `git diff --check` passes. Only the four allowed path groups were changed.
- The full six-player script successfully executes every one of the 39 non-None verbs, all five task types and all twelve incident types; repeated runs compare full event bytes and result hashes. The chained five-day week checks each day's features/profile, independently recomputed deltas/totals, finale ties/winners and history reset.
- Malformed target hardening: targetless TasteCoffee/Drop requests now canonicalize their target to empty. This prevents an invalid UTF-16 target from poisoning view encoding and prevents a forged Drop target from creating a witness note at a false location.

Measured on this machine, Debug harness, warm-up then 4,000 iterations (budgets asserted):

| Six-player state | Tick | Six-recipient BuildView batch | Stable Tick allocation |
|---|---:|---:|---:|
| Work1 | 0.00124 ms | 0.07877 ms | 0 B |
| Active inspection, visible carried suspicious items | 0.00318 ms | 0.08035 ms | 0 B |
| Populated Results from full v2 script | 0.00050 ms | 0.15227 ms | 0 B |

Budgets: Tick <0.2 ms; six-view batch <0.3 ms. Advancing Tick measurements include scheduled work; allocation assertions measure the stable hot loop separately. Item/desk lookup and continuous manager carry checks avoid closure/string allocations. These are core harness timings, not Unity-player process profiling.
