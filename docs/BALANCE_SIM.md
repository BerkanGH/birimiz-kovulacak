# Birimiz Kovulacak v2.2 balance simulator

The default configuration made quota in all 4,000 quick-play matches. Personal objectives were much less even: Chaos Coordinator won 2.4–4.6%, Under Pressure 0.0–0.6%, and Clean Hands 70.7–91.6%. These are measurements of the seeded headless policy below, not estimates of human win rates.

## Reproduce the baseline

Run from the repository root with a .NET 8 runtime and SDK capable of targeting .NET 8:

```powershell
dotnet run -c Release --project Tools/BalanceSim -- --experiment all --matches 1000 --profile-matches 500 --weeks 300 --players 3,4,5,6 --seed 220000 --threads 8 --out baseline
```

The baseline ran 9,000 complete matches: 4,000 quick-play matches, 2,000 manager-profile matches, and 3,000 daily matches across 600 weeks. Simulation time was **139.7 seconds** on .NET 8.0.22 with 16 logical processors and 8 workers. This excludes compilation. Matches run concurrently; each engine and its policies remain on one worker and execute sequentially. Days within a week also execute sequentially.

The simulator writes [Markdown](../Tools/BalanceSim/results/baseline.md) and [JSON](../Tools/BalanceSim/results/baseline.json) into `Tools/BalanceSim/results/`. JSON retains per-match results and counters plus weekly totals and daily result hashes. Build and restore outputs for both the simulator and referenced core stay under `Tools/BalanceSim/bin/` and `Tools/BalanceSim/obj/`.

Use `--help` for options. `--experiment quick`, `profiles`, and `week` select smaller runs. `--set Key=Value` accepts repeatable numeric or Boolean configuration overrides, including named collection entries such as `--set Objectives.FIXER.Count=1`. Overrides apply before headcount scaling and the engine's manager-profile changes. Duration overrides are rejected.

## Method and policy

[BalanceSim.csproj](../Tools/BalanceSim/BalanceSim.csproj) uses a `ProjectReference` to [Core.csproj](../Tools/CoreTests/Core/Core.csproj), which compiles the actual `Game/Assets/Game/Core/**/*.cs` sources. Each match creates `DefaultOffice.Create()`, calls `MatchConfig.CreateDefault()`, applies command-line overrides, then calls `ApplyHeadcount(n)` before constructing `MatchEngine`. This preserves the order in [HostMatchController.StartMatch](../Game/Assets/Game/Scripts/Net/HostMatchController.cs). Optional day modifiers are omitted. Week mode still uses the engine's normal Monday–Friday profile, feature, and career schedule and carries `BuildNextHistory()` between days. Quick-play matches start independent sessions with no previous-match history.

The engine advances in **0.1-second ticks** through all configured phases, with no duration scaling. Office and meeting decisions use each bot's own filtered `BuildView`, refreshed every 0.4 seconds, at phase transitions, after submissions, and at timed-interaction completion. All gameplay actions use `Submit`; avatar motion uses `UpdateAvatar`. Request order rotates every tick so a fixed lobby slot does not always win contention. The run ends on entry to Results, after the real engine has produced its final outcomes; only the inert 30-second results-display hold is skipped.

[SimBot](../Tools/BalanceSim/SimBot.cs) adapts the decision mix in [BotBrain](../Game/Assets/Game/Scripts/Bots/BotBrain.cs):

- Every player receives a seeded aggression value uniformly distributed from 0.2 to 0.8. Clean Hands avoids sabotage; Dismissal Plan, Scapegoat, and Chaos Coordinator increase its probability; the Informant uses reduced aggression. Targeted objectives influence victim selection.
- Workers brew and deliver coffee; write, print, collect, and deliver reports; answer the task phone; clean desks and spills; deliver mail; and repair workstations and outages. Task choices use visible state and ordinary affordance checks.
- Sabotage includes taking and planting suspicious items, favoring drawers and following up with manager tips; clutter, nameplate swaps, report hiding, coffee spills, and card sabotage only when the bot holds the required card and can use it. Bots also inspect and clean up their own desks.
- The Informant scans eligible public evidence, seals evidence, pulls call logs, and requests footage using the Unity bot's timing and selection mix. These are actions with the engine's real charges and restrictions, not extra knowledge.
- Calls, answers, declines, hangups, tips, and emotes follow the bot's seeded social cadence. During announced inspections, bots usually return to their own seat and adopt a work pose.
- At meetings, bots present eligible notes, accuse and vote according to objective targets or otherwise the highest visible suspicion, and request footage during defense. Quiet Star avoids accusing; Protection Deal avoids its protected target where possible.

The navigation adaptation moves in a straight line at 3 m/s, slowing to 1.65 m/s while sneaking near a sabotage destination. Bots stop during timed interactions and face their targets. There is no NavMesh, collision avoidance, physical blocking, or player voice reasoning. `OpenWorld.Instance` reports unobstructed line of sight, although the engine still applies distance and facing rules. Straight routes can shorten task travel and change camera-zone coverage; open sightlines can change witnesses and manager inspections. CCTV itself uses zone rectangles, without line-of-sight occlusion. Results therefore measure this reproducible policy/world model; human or Unity navigation results may differ materially.

The observer in [MatchMetrics.cs](../Tools/BalanceSim/MatchMetrics.cs) never supplies data to a policy. It reads the engine's ordinary post-match reveal after Results and records action diagnostics from the host debug view. The core exposes no public event for every completed clean-desk inspection, so the observer reads the private `inspectedDesks` ledger once after Results using cached, read-only reflection. It fails explicitly if that field or ledger format changes. This avoids treating a manager passing near a desk, or a desk producing no observation, as evidence of a completed inspection. Neither engine state nor contracts are modified.

## Seeds, definitions, and uncertainty

For base seed `S`, quick-play engine seeds start at `S + players × 10000`; profile seeds start at `S + 100000 + profileIndex × 10000` in default profile order; and week seeds start at `S + 200000 + players × 10000`. Week `w`, day `d` uses the latter start plus `5w + d`, with zero-based indices. A player's policy seed is `unchecked(engineSeed * 397 ^ (playerId + 1) * 7919)`. Matches write into fixed seed-indexed slots, so scheduling and worker count do not determine aggregation order. The tables retain ordered engine-result SHA-256 digests for reproducibility on this policy and runtime.

All displayed percentages aggregate underlying numerators and denominators:

- **Quota:** fraction of completed matches where the actual engine reports success; points and target are separate means. Targets include manager-profile adjustments, hence fractional mean targets in mixed-profile groups.
- **Fired misconduct:** `top` means the highest strictly positive true-misconduct tier, including ties; `other` means positive misconduct below that tier; `zero` means no true misconduct. Fired performance rank is competition rank after the result's performance adjustments: one plus the number of players strictly above the fired player.
- **Wins:** Informant wins and objective wins use final engine victories, including company success and survival gates. Objective N counts holders of that card; it excludes Informants. At least one winner counts matches with any final personal victory.
- **Incidents:** means count all post-match incident reveals. Discovery is the engine's `IncidentReveal.Discovered` flag, pooled across incidents, rather than whether a human could infer the event. A zero discovery rate does not mean that sabotage never occurred.
- **Calls and tips:** calls exclude manager tips; answered percentage is answered calls divided by initiated calls. Tip hit rate is `TipFound / (TipFound + TipClean)` at evaluated inspection visits. Several callers tipping the same desk may be coalesced into one visit, and a tip that never receives an evaluated visit is outside this denominator. A hit means the tipped desk had a qualifying find when inspected, not necessarily when the call was placed. The table's tip N is evaluated visits.
- **Footage:** requests count accepted requests that produced CCTV evidence, including empty or unavailable recordings. “With incident” means at least one true incident time falls in the requested window; its parenthesized count is **hits**, not a denominator. “True actor” requires the incident actor to appear in that camera's sightings within one configured sample interval of the incident. This measures visibility, not proof or engine attribution. It is reported over all footage requests and separately over footage windows containing incidents. The all-footage N is the number of requests.
- **Inspection coverage:** unique assigned desks successfully inspected divided by assigned desks across matches; empty desks are excluded. Surprise coverage is reported both conditional on a surprise inspection occurring and over all matches. Only the Disiplinci profile normally schedules one.
- **Weeks:** spread is Friday's maximum minus minimum official career total. “Any” means at least one Friday winner was among Wednesday's tied leaders; “all” requires every Friday winner to have been among them. Ayın Elemanı tie rate means more than one official Friday winner. All week rates use weeks as their denominator.

For a proportion near 50%, an approximate 95% sampling margin is ±3.1 percentage points at N=1,000, ±4.4 at N=500, and ±5.7 at N=300. Card-holder counts are smaller and their margins are wider: approximately ±4.7 to ±8.5 points for quick-play card Ns in this run. These are per-estimate sampling guides without multiple-comparison correction; they do not quantify the larger uncertainty from the policy/world assumptions. Observing 100% quota success in 1,000 trials does not establish certainty (the usual one-sided rule-of-three bound allows roughly 0.3% failures).

The requested target combination has a structural conflict in [MatchEngine.BuildResults](../Game/Assets/Game/Core/Engine/MatchEngine.Meeting.cs): every personal win requires `CompanySucceeded`. Therefore **P(at least one winner) ≤ P(quota success)**. Quota success of 55–75% cannot coexist with at least one winner in 80% or more of matches through configuration alone. Any tuning must explicitly choose a tradeoff or separately propose a rule change; the simulator preserves the current engine gates.

## Baseline measurements

Runtime: .NET 8.0.22; logical processors: 16; workers: 8; simulation elapsed: 139.7s.

Overrides before headcount: none (CreateDefault).

| Experiment | N | Quota success | Mean points / target | Fired: top / other / zero | Informant win | ≥1 winner |
|---|---:|---:|---:|---|---:|---:|
| Quick 3p | 1000 | 100.0% | 40.91 / 16.51 | 43.1% / 43.7% / 13.2% | 41.9% | 73.9% |
| Quick 4p | 1000 | 100.0% | 45.06 / 22.53 | 36.3% / 53.6% / 10.1% | 35.9% | 86.3% |
| Quick 5p | 1000 | 100.0% | 52.33 / 26.52 | 34.0% / 59.4% / 6.6% | 33.6% | 92.0% |
| Quick 6p | 1000 | 100.0% | 53.54 / 30.56 | 29.1% / 65.3% / 5.6% | 29.0% | 94.8% |
| paragoz | 500 | 100.0% | 44.96 / 24.00 | 37.6% / 53.6% / 8.8% | 37.2% | 86.0% |
| disiplinci | 500 | 100.0% | 45.22 / 22.00 | 29.8% / 57.4% / 12.8% | 29.0% | 84.0% |
| kayirmaci | 500 | 100.0% | 44.85 / 22.00 | 38.6% / 53.8% / 7.6% | 38.0% | 85.2% |
| paranoyak | 500 | 100.0% | 45.00 / 22.00 | 36.4% / 55.0% / 8.6% | 35.6% | 87.4% |

### Baseline seeds and determinism

| Experiment | Engine seeds (inclusive) | Result hash digest (SHA-256, ordered) |
|---|---|---|
| Quick 3p | 250000–250999 | `DB86D2AB1017A567CCB6D61D6B8398F223D4608103DDC5536DC7CC95D00FA8D5` |
| Quick 4p | 260000–260999 | `D3102E6EE3E4FBB830C71C10F0750E1065C4E89ADD387BF26DC53979B27E0675` |
| Quick 5p | 270000–270999 | `05DF0DF224A1BFFB22504292E87180657F4CC0AFEB280E9DE3308747907C720F` |
| Quick 6p | 280000–280999 | `1C7841944630752D78D908E0F58CB20BEE7CD73B7340FE7CA1C595311906D124` |
| paragoz | 320000–320499 | `3C42DE149E96672A75A8A66ABB3352EF9127F83CC9C88B73CE0AEA74DD01A812` |
| disiplinci | 330000–330499 | `7E0649A539E4D9091129343A6D5233FFE87A2EDA42802EF6BDA15C92EC83BCF2` |
| kayirmaci | 340000–340499 | `D632140795246DA9EA9372357E2E7DF834740D0927A581592890C3D12217D04B` |
| paranoyak | 350000–350499 | `E6A4461F8A3E45A8B2E2E94873C9D0C35C5F7EA39CF49D48AB9A4949B78A84FA` |
| Week 4p | 460000–461499 | `37DD5E8FB34FAFD62C7D985D963A21202917BE880D930E58154E8CB2951A0FC7` |
| Week 6p | 480000–481499 | `20EAF3FCDF185C45C6FAD87F2F39957525B374DD54189F0DDA36318280514C63` |

### Objective wins (final company + survival gates included)

| Card | Quick 3p: win % (N) | Quick 4p: win % (N) | Quick 5p: win % (N) | Quick 6p: win % (N) | paragoz: win % (N) | disiplinci: win % (N) | kayirmaci: win % (N) | paranoyak: win % (N) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| DISMISSAL_PLAN | 45.7% (162) | 42.1% (233) | 38.1% (318) | 37.6% (420) | 37.2% (129) | 54.4% (125) | 29.2% (130) | 45.9% (135) |
| PROTECTION_DEAL | 51.3% (152) | 57.7% (274) | 66.8% (337) | 72.3% (397) | 63.9% (119) | 72.1% (122) | 63.5% (137) | 65.9% (126) |
| SCAPEGOAT | 14.0% (172) | 12.9% (248) | 11.4% (316) | 7.2% (430) | 11.7% (137) | 19.4% (124) | 13.4% (119) | 20.7% (121) |
| QUIET_STAR | 23.9% (159) | 19.1% (251) | 20.0% (315) | 18.9% (433) | 23.8% (122) | 25.5% (137) | 12.7% (126) | 19.0% (116) |
| CHAOS_COORDINATOR | 2.4% (167) | 3.4% (234) | 4.3% (346) | 4.6% (416) | 3.4% (117) | 2.5% (118) | 6.2% (128) | 4.9% (123) |
| CLEAN_HANDS | 70.7% (181) | 82.1% (262) | 90.0% (351) | 91.6% (418) | 85.0% (133) | 80.4% (112) | 84.1% (126) | 80.3% (122) |
| OFFICE_ALL_ROUNDER | 34.4% (180) | 37.7% (257) | 31.1% (318) | 22.0% (427) | 34.2% (120) | 33.1% (124) | 36.9% (111) | 30.4% (125) |
| FIXER | 21.2% (179) | 35.5% (242) | 41.8% (330) | 49.5% (412) | 46.5% (127) | 26.8% (123) | 34.4% (128) | 38.6% (132) |
| DESK_GUARDIAN | 56.4% (133) | 61.9% (247) | 61.4% (355) | 65.8% (424) | 55.5% (128) | 58.9% (129) | 58.1% (124) | 56.1% (123) |
| UNNOTICED | 24.7% (174) | 38.7% (271) | 42.6% (338) | 43.7% (430) | 37.9% (116) | 31.2% (125) | 43.5% (124) | 37.0% (135) |
| PERFORMANCE_RIVAL | 34.4% (154) | 33.2% (259) | 40.5% (321) | 39.2% (401) | 40.3% (124) | 41.5% (135) | 39.7% (126) | 35.9% (128) |
| UNDER_PRESSURE | 0.5% (187) | 0.0% (222) | 0.6% (355) | 0.0% (392) | 1.6% (128) | 0.0% (126) | 4.1% (121) | 0.0% (114) |

### Incidents

| Type | Quick 3p: mean / discovered % | Quick 4p: mean / discovered % | Quick 5p: mean / discovered % | Quick 6p: mean / discovered % | paragoz: mean / discovered % | disiplinci: mean / discovered % | kayirmaci: mean / discovered % | paranoyak: mean / discovered % |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| NameplateSwap | 0.54 / 81.2% | 1.18 / 80.9% | 1.85 / 79.6% | 5.04 / 78.4% | 1.16 / 77.6% | 1.15 / 80.0% | 1.15 / 79.3% | 1.10 / 76.5% |
| PlantItem | 2.08 / 37.9% | 3.05 / 37.4% | 4.33 / 36.7% | 5.15 / 36.4% | 3.35 / 33.9% | 3.29 / 36.9% | 3.39 / 34.7% | 3.31 / 42.9% |
| LockWorkstation | 0.54 / 0.0% | 0.70 / 0.0% | 0.98 / 0.0% | 1.09 / 0.0% | 0.73 / 0.0% | 0.67 / 0.0% | 0.66 / 0.0% | 0.74 / 0.0% |
| HideReport | 0.47 / 0.0% | 0.83 / 0.0% | 1.23 / 0.0% | 1.60 / 0.0% | 0.80 / 0.0% | 0.84 / 0.0% | 0.86 / 0.0% | 0.79 / 0.0% |
| SpillCoffee | 0.50 / 28.7% | 0.70 / 18.2% | 0.90 / 14.6% | 1.09 / 15.5% | 0.71 / 16.6% | 0.66 / 23.6% | 0.73 / 15.9% | 0.72 / 15.8% |
| MoveClutter | 2.12 / 10.1% | 3.21 / 9.3% | 4.30 / 8.9% | 5.00 / 8.2% | 3.30 / 6.9% | 3.26 / 11.7% | 3.16 / 7.9% | 3.18 / 7.4% |
| EvidenceTampering | 0.26 / 0.0% | 0.39 / 0.0% | 0.55 / 0.0% | 0.63 / 0.0% | 0.36 / 0.0% | 0.43 / 0.0% | 0.34 / 0.0% | 0.57 / 0.0% |
| FakeEmail | 0.12 / 100.0% | 0.17 / 100.0% | 0.23 / 100.0% | 0.28 / 100.0% | 0.19 / 100.0% | 0.23 / 100.0% | 0.17 / 100.0% | 0.22 / 100.0% |
| PullPlug | 0.46 / 0.0% | 0.82 / 0.0% | 1.06 / 0.0% | 1.22 / 0.0% | 0.77 / 0.0% | 0.80 / 0.0% | 0.81 / 0.0% | 0.80 / 0.0% |
| SaltCoffee | 0.27 / 90.1% | 0.36 / 80.6% | 0.44 / 81.2% | 0.45 / 79.2% | 0.36 / 83.4% | 0.35 / 80.6% | 0.37 / 85.3% | 0.33 / 83.6% |
| InternetOutage | 0.20 / 0.0% | 0.31 / 0.0% | 0.37 / 0.0% | 0.42 / 0.0% | 0.27 / 0.0% | 0.24 / 0.0% | 0.27 / 0.0% | 0.29 / 0.0% |
| PowerCut | 0.20 / 0.0% | 0.30 / 0.0% | 0.38 / 0.0% | 0.40 / 0.0% | 0.30 / 0.0% | 0.29 / 0.0% | 0.25 / 0.0% | 0.31 / 0.0% |

### Calls, footage, and action diagnostics

| Experiment | Incidents / match | Discovered | Footage / match | Footage with incident % (hits) | True actor / all footage % (requests N) | True actor / incident windows | Calls / match | Answered | Tips / match | Tip hit / evaluated visits (N) | Requests / match | Rejected |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Quick 3p | 7.76 | 25.1% | 3.49 | 13.8% (482) | 2.9% (3491) | 20.7% | 16.52 | 73.5% | 0.67 | 76.1% (654) | 234.5 | 8.6% |
| Quick 4p | 12.03 | 24.8% | 3.48 | 19.1% (666) | 3.7% (3478) | 19.4% | 22.50 | 73.9% | 0.99 | 68.3% (958) | 317.4 | 10.4% |
| Quick 5p | 16.64 | 25.1% | 3.50 | 21.8% (763) | 3.8% (3504) | 17.4% | 27.98 | 73.6% | 1.42 | 67.8% (1362) | 401.9 | 11.7% |
| Quick 6p | 22.38 | 31.5% | 3.50 | 22.7% (793) | 4.6% (3496) | 20.3% | 33.60 | 74.2% | 1.73 | 63.8% (1635) | 490.4 | 14.4% |
| paragoz | 12.30 | 23.3% | 3.00 | 21.4% (321) | 3.6% (1500) | 16.8% | 22.29 | 74.1% | 1.02 | 66.6% (491) | 316.1 | 10.3% |
| disiplinci | 12.19 | 26.1% | 3.00 | 19.6% (294) | 4.3% (1500) | 22.1% | 22.25 | 74.3% | 1.10 | 67.1% (529) | 316.9 | 10.5% |
| kayirmaci | 12.16 | 24.2% | 3.00 | 18.2% (273) | 3.6% (1500) | 19.8% | 22.32 | 73.9% | 1.10 | 70.1% (525) | 316.7 | 10.4% |
| paranoyak | 12.35 | 25.1% | 5.00 | 15.2% (380) | 2.8% (2500) | 18.4% | 22.27 | 73.7% | 1.06 | 68.9% (505) | 318.7 | 10.3% |

### Inspection coverage (unique assigned desks)

| Experiment | N | Raid | Final | Surprise eligible matches | Surprise conditional | Surprise over all matches |
|---|---:|---:|---:|---:|---:|---:|
| Quick 3p | 1000 | 100.0% | 100.0% | 239 | 100.0% | 23.9% |
| Quick 4p | 1000 | 100.0% | 100.0% | 246 | 100.0% | 24.6% |
| Quick 5p | 1000 | 99.7% | 100.0% | 258 | 99.9% | 25.8% |
| Quick 6p | 1000 | 96.7% | 99.9% | 236 | 98.0% | 23.1% |
| paragoz | 500 | 100.0% | 100.0% | 0 | — | 0.0% |
| disiplinci | 500 | 100.0% | 100.0% | 500 | 100.0% | 100.0% |
| kayirmaci | 500 | 100.0% | 100.0% | 0 | — | 0.0% |
| paranoyak | 500 | 100.0% | 100.0% | 0 | — | 0.0% |

### Fired player's performance rank

Competition rank, 1 = highest final performance; tied scores share rank.

| Experiment | Mean rank | Rank 1 | Rank 2 | Rank 3 | Rank 4 | Rank 5 | Rank 6 |
|---|---:|---:|---:|---:|---:|---:|---:|
| Quick 3p | 2.17 | 24.5% | 33.6% | 41.9% | — | — | — |
| Quick 4p | 2.73 | 18.8% | 21.9% | 26.4% | 32.9% | — | — |
| Quick 5p | 3.42 | 9.5% | 18.6% | 20.0% | 23.7% | 28.2% | — |
| Quick 6p | 4.03 | 8.2% | 12.6% | 16.3% | 17.7% | 21.1% | 24.1% |
| paragoz | 2.79 | 15.4% | 23.2% | 28.8% | 32.6% | — | — |
| disiplinci | 2.74 | 15.8% | 25.2% | 28.6% | 30.4% | — | — |
| kayirmaci | 2.70 | 20.0% | 21.4% | 27.4% | 31.2% | — | — |
| paranoyak | 2.69 | 20.0% | 20.2% | 30.8% | 29.0% | — | — |

### Week mode

| Players | Weeks N | Mean spread | Median spread | P95 spread | Friday leader already led Wednesday (any) | All Friday leaders already led Wednesday | Ayın Elemanı tie | Wednesday tie |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 4 | 300 | 16.65 | 16 | 28 | 56.7% | 53.0% | 7.0% | 9.7% |
| 6 | 300 | 18.17 | 18 | 27 | 54.3% | 47.7% | 10.3% | 17.7% |

## Recommendations

**No configuration satisfies all four requested bands.** Every victory requires company success, so a 75% company-success ceiling imposes a 75% ceiling on matches with winners. The following are two measured alternatives, not a claim that the conflicting targets have been met. All reruns use the same baseline seeds and policy; values were passed with `--set`, without changing `CreateDefault`.

For a quota-difficulty target, use the following **pre-headcount** `QuotaTarget` values. The old headcount multipliers cannot produce the desired success band with one shared base value: this workload generates about 41 points with three players and 54 with six. These values are headcount-specific presets; the simulator applies the ordinary `ApplyHeadcount` afterward. They achieve the quota band but fail the winner-coverage goal, and Informant wins fall below 30% at 4–6 players.

| Players | `QuotaTarget` old → override | Effective base after headcount | Quota success before → after | Informant before → after | ≥1 winner before → after | N, engine seeds |
|---|---|---:|---|---|---|---|
| 3 | 22 → 54 | 39 (+2 for Paragoz) | 100.0% → 71.9% | 41.9% → 30.1% | 73.9% → 53.2% | 1000, 250000–250999 |
| 4 | 22 → 44 | 44 (+2 for Paragoz) | 100.0% → 69.0% | 35.9% → 24.6% | 86.3% → 60.3% | 1000, 260000–260999 |
| 5 | 22 → 43 | 51 (+2 for Paragoz) | 100.0% → 72.6% | 33.6% → 23.9% | 92.0% → 67.0% | 1000, 270000–270999 |
| 6 | 22 → 39 | 53 (+2 for Paragoz) | 100.0% → 61.6% | 29.0% → 18.9% | 94.8% → 58.9% | 1000, 280000–280999 |

For winner coverage, the stronger config-only candidate keeps `QuotaTarget=22` and uses this pack:

| Key | Default | Override | Reason / cost |
|---|---:|---|---|
| `Objectives.SCAPEGOAT.Count` | 2 | 1 | One discovered framing success is achievable under this policy. |
| `Objectives.CHAOS_COORDINATOR.Count` | 2 | 1 | One discovered planted desk; two distinct discovered desks was too rare. |
| `Objectives.CLEAN_HANDS.Count` | 4 | 3p: 12; 4p: 11; 5p/6p: 10 | Raises the task burden of the strongest card; task-type requirement stays 2. |
| `Risk.NotAtDesk` | 1 | 0 | Removes absence from observed risk and Under Pressure’s positive-penalty gate. |
| `Risk.MovingDuringInspection` | 1.5 | 0 | Removes movement penalties, including for legitimate travel. |
| `Risk.PersonalCall` | 1 | 0 | Removes personal-call penalties. |

The three zero-risk changes are a substantial design tradeoff: absence, movement, and personal calls no longer increase firing risk. Other evidence, being caught, suspicious items, mess, and incorrect desk presence still matter. The pack improves the measured outcomes below, but should be playtested before adoption. It intentionally retains easy quota to protect winner coverage. Lowering only `Objectives.UNDER_PRESSURE.Count` cannot address that card’s hardcoded “no positive final-inspection observation” condition; this pack leaves its count at 2.

| Players | N | Quota before → after | Informant before → after | ≥1 winner before → after | Fired top / other / zero after |
|---|---:|---|---|---|---|
| 3 | 1000 | 100.0% → 100.0% | 41.9% → 42.1% | 73.9% → 81.9% | 43.4% / 43.7% / 12.9% |
| 4 | 1000 | 100.0% → 100.0% | 35.9% → 39.5% | 86.3% → 89.2% | 39.9% / 52.5% / 7.6% |
| 5 | 1000 | 100.0% → 100.0% | 33.6% → 38.3% | 92.0% → 94.0% | 38.8% / 56.5% / 4.7% |
| 6 | 1000 | 100.0% → 100.0% | 29.0% → 36.0% | 94.8% → 95.9% | 36.0% / 60.2% / 3.8% |

Card-level paired rerun, final win % before → after (card-holder N; identical assignments in each pair):

| Card | 3 players | 4 players | 5 players | 6 players |
|---|---:|---:|---:|---:|
| CHAOS_COORDINATOR | 2.4% → 28.7% (167) | 3.4% → 25.6% (234) | 4.3% → 28.3% (346) | 4.6% → 29.3% (416) |
| CLEAN_HANDS | 70.7% → 58.6% (181) | 82.1% → 46.2% (262) | 90.0% → 59.5% (351) | 91.6% → 36.1% (418) |
| DESK_GUARDIAN | 56.4% → 57.1% (133) | 61.9% → 61.1% (247) | 61.4% → 58.6% (355) | 65.8% → 62.7% (424) |
| DISMISSAL_PLAN | 45.7% → 43.2% (162) | 42.1% → 35.6% (233) | 38.1% → 28.9% (318) | 37.6% → 25.2% (420) |
| FIXER | 21.2% → 19.6% (179) | 35.5% → 32.2% (242) | 41.8% → 43.6% (330) | 49.5% → 51.2% (412) |
| OFFICE_ALL_ROUNDER | 34.4% → 30.6% (180) | 37.7% → 33.5% (257) | 31.1% → 28.9% (318) | 22.0% → 21.3% (427) |
| PERFORMANCE_RIVAL | 34.4% → 35.1% (154) | 33.2% → 32.4% (259) | 40.5% → 40.5% (321) | 39.2% → 40.1% (401) |
| PROTECTION_DEAL | 51.3% → 53.9% (152) | 57.7% → 50.4% (274) | 66.8% → 59.6% (337) | 72.3% → 72.8% (397) |
| QUIET_STAR | 23.9% → 24.5% (159) | 19.1% → 20.3% (251) | 20.0% → 17.1% (315) | 18.9% → 17.6% (433) |
| SCAPEGOAT | 14.0% → 34.3% (172) | 12.9% → 35.1% (248) | 11.4% → 37.0% (316) | 7.2% → 38.1% (430) |
| UNDER_PRESSURE | 0.5% → 27.8% (187) | 0.0% → 33.3% (222) | 0.6% → 30.7% (355) | 0.0% → 23.2% (392) |
| UNNOTICED | 24.7% → 31.0% (174) | 38.7% → 50.2% (271) | 42.6% → 53.6% (338) | 43.7% → 63.5% (430) |

The pack puts most cards in or near 20–60%, but does **not** meet “every card”: six-player Protection Deal is 72.8%, Unnoticed 63.5%, Desk Guardian 62.7%, and Quiet Star 17.6%; five-player Quiet Star is 17.1%. These predicates have no configurable count threshold. Four-player Desk Guardian is 61.1% and three-player Fixer is 19.6%, both close to the boundary relative to sampling uncertainty. These remaining outliers need further measured tuning or a predicate/policy review; this experiment did not establish a config-only solution for every card. Achieving both quota and winner bands requires changing the victory gate or the targets. Engine changes remain outside this task.

The affected profile and week experiments were also rerun with this pack (4p Clean Hands count 11, 6p count 10):

| Profile, 4p | N | Quota before → after | Informant before → after | ≥1 winner before → after | Fired mean performance rank before → after |
|---|---:|---|---|---|---|
| paragoz | 500 | 100.0% → 100.0% | 37.2% → 41.0% | 86.0% → 88.6% | 2.79 → 2.78 |
| disiplinci | 500 | 100.0% → 100.0% | 29.0% → 39.4% | 84.0% → 90.8% | 2.74 → 2.61 |
| kayirmaci | 500 | 100.0% → 100.0% | 38.0% → 36.8% | 85.2% → 86.4% | 2.70 → 2.59 |
| paranoyak | 500 | 100.0% → 100.0% | 35.6% → 37.4% | 87.4% → 88.2% | 2.69 → 2.68 |

| Week players | Weeks N | Mean spread before → after | Friday leader led Wednesday (any) before → after | Ayın Elemanı tie before → after |
|---|---:|---|---|---|
| 4 | 300 | 16.65 → 16.96 | 56.7% → 61.3% | 7.0% → 6.0% |
| 6 | 300 | 18.17 → 17.92 | 54.3% → 55.0% | 10.3% → 10.0% |

Quick-play points, incident counts/discovery, calls, tips, and inspection coverage stayed unchanged in the winner-coverage rerun. Meeting changes altered footage actor visibility (3p: 2.9% → 2.8%; 4p: 3.7% → 4.0%; 5p: 3.8% → 4.0%; 6p: 4.6% → 4.5%) and some FakeEmail consequence misconduct, which depends on the fired player. Full per-run tables remain in the generated Markdown files.

Example quota-only rerun at four players:

```powershell
dotnet run -c Release --project Tools/BalanceSim -- --experiment quick --players 4 --matches 1000 --seed 220000 --out quota_final_4 --set QuotaTarget=44
```

Example winner-coverage rerun at four players:

```powershell
dotnet run -c Release --project Tools/BalanceSim -- --experiment quick --players 4 --matches 1000 --seed 220000 --out balance_4 --set Objectives.SCAPEGOAT.Count=1 --set Objectives.CHAOS_COORDINATOR.Count=1 --set Objectives.CLEAN_HANDS.Count=11 --set Risk.NotAtDesk=0 --set Risk.MovingDuringInspection=0 --set Risk.PersonalCall=0
```

Run [Run-Experiments.ps1](../Tools/BalanceSim/Run-Experiments.ps1) to reproduce the baseline plus both final alternatives (22,000 matches total); `-SkipBaseline` reuses an existing baseline. It passes every override on the command line. Generated JSON/Markdown are ignored by Git; source and this report are the review artifacts. Optional `node Tools/BalanceSim/Write-TuningReport.mjs` regenerates this recommendations section from the exports; Node is not required to build or run the simulator.

| Rerun | N | Seed range | Ordered result digest (SHA-256) |
|---|---:|---|---|
| quota_3: Quick 3p | 1000 matches | 250000–250999 | `5681CEBBC35B9BD43121F74B7B8B11A834DFE55E4025AEE962BADF51E2EA38B5` |
| quota_final_4: Quick 4p | 1000 matches | 260000–260999 | `B1044BECA4F63C0A50705D0F851C2F52F5208BC716604878F5DC063E20FDC492` |
| quota_5: Quick 5p | 1000 matches | 270000–270999 | `DB6CDA5C0B364E437BF38ABC1295FBD689B83AF107EE066A5405C835C52D0D1A` |
| quota_final_6: Quick 6p | 1000 matches | 280000–280999 | `18D6F1C037CCC2ED52BC207E82355620EC1E9C07F550773C24C5BC9D76654E2A` |
| balance_3: Quick 3p | 1000 matches | 250000–250999 | `EF8136866BEC2EF9487C47974BC963F0FB4EB3A1E8CDED97C569754B29796C16` |
| balance_4: Quick 4p | 1000 matches | 260000–260999 | `AC743CF693B5D8F5C7099275B3271CCF30E50159E06D5D716E08BB198323F4D3` |
| balance_5: Quick 5p | 1000 matches | 270000–270999 | `2CB54FC4B4E03CB66DE9E1985F38E3E42A2CE3F04581235B1932CB4AA466895A` |
| balance_6: Quick 6p | 1000 matches | 280000–280999 | `E3D8A20ED03460D0C756522956D6EBF1304EA8FFAAE092B53F9D00ACEF033AED` |
| balance_profiles: paragoz | 500 matches | 320000–320499 | `294BB1134F52397F4C9D38BDA84C0E31B628A2A96705F3B16BEDD6F9214B38B8` |
| balance_profiles: disiplinci | 500 matches | 330000–330499 | `67BF98B3CF2B4B851D55C20D3AF7DD6137A8912FAA6281C40F359EA14EA2B4E6` |
| balance_profiles: kayirmaci | 500 matches | 340000–340499 | `631D15EEE2D8BE1C6A81A144C4B0DFAD753D712A8A7C9FDEFE861A885E286BF2` |
| balance_profiles: paranoyak | 500 matches | 350000–350499 | `55925CA2F1F6F841635CC3E696E0BB08331A77891E2308EC233449E44BA72272` |
| balance_week_4 | 300 weeks | 460000–461499 | `5DEAA3A199CA827C7ECF9CFEDDCF6DC709C5BB50E6D0CBAFA6D7D0DEB15F9784` |
| balance_week_6 | 300 weeks | 480000–481499 | `A7CE0BD7214342583ADF70814654E36D243A75C26809823A73AADAE8096DA1BE` |

Measured simulation time for the baseline and final alternatives was **327.3 seconds (5.46 minutes)** summed across processes on this machine. Including the 300-match pilot, two superseded quota trials, and the two 32-match determinism smoke runs, all recorded simulation runs totaled **370.8 seconds (6.18 minutes)**. Compilation and offline export validation are excluded; two rerun batches briefly overlapped, so summed process time exceeds their wall-clock time. The 10-minute budget is hardware dependent.

Validation: Release build passed with zero warnings/errors. [Verify-Results.ps1](../Tools/BalanceSim/Verify-Results.ps1) checked all 6,000 baseline quick/profile records and all 600 weeks for victory gates, exactly one fired player, misconduct totals, ranks, denominators, career spreads/ties, seed ordering, and digests. The same 32 seeds with one and eight workers produced identical complete exported metrics. CLI validation also rejects duration overrides.

```powershell
./Tools/BalanceSim/Verify-Results.ps1 -Path Tools/BalanceSim/results/smoke.json -Compare Tools/BalanceSim/results/smoke_serial.json
```

## v2.3 defaults

Four-player quick play was rerun for **1,000 matches** with the same policy and seeds as the original baseline, using `CreateDefault` with no overrides: `QuotaTarget=28`, `SCAPEGOAT.Count=1`, `CHAOS_COORDINATOR.Count=1`, and `CLEAN_HANDS.Count=7` (two task types). UNDER_PRESSURE still requires its configured Work2 task count (default two), but now fails only for an incriminating final-inspection observation attributed to its holder: CaughtInAct, CarryingSuspicious, SuspiciousItemAtDesk, TipFound, InappropriateEmail, or SaltyCoffee, regardless of severity. Ordinary inspection observations still affect firing risk but no longer fail this card or set its `penalty` flag.

```powershell
dotnet run -c Release --project Tools/BalanceSim -- --experiment quick --players 4 --matches 1000 --seed 220000 --threads 8 --out v23_defaults_4
./Tools/BalanceSim/Verify-Results.ps1 -Path Tools/BalanceSim/results/v23_defaults_4.json
```

**Quota success: 100.0% (1,000/1,000)**; mean points / target: **45.06 / 28.53** (target includes manager adjustments). At least one player won in 91.8% of matches. Objective wins below include the final company-success and survival gates; N counts employee card holders.

| Card | Win % | N |
|---|---:|---:|
| DISMISSAL_PLAN | 42.1% | 233 |
| PROTECTION_DEAL | 57.7% | 274 |
| SCAPEGOAT | 35.1% | 248 |
| QUIET_STAR | 19.1% | 251 |
| CHAOS_COORDINATOR | 26.1% | 234 |
| CLEAN_HANDS | 81.3% | 262 |
| OFFICE_ALL_ROUNDER | 37.7% | 257 |
| FIXER | 35.5% | 242 |
| DESK_GUARDIAN | 61.9% | 247 |
| UNNOTICED | 38.7% | 271 |
| PERFORMANCE_RIVAL | 33.2% | 259 |
| UNDER_PRESSURE | 51.4% | 222 |

Simulation took 11.0 seconds on .NET 8.0.22 with eight workers. Engine seeds: 260000–260999; ordered result SHA-256: `38530A18852C0B389708AA513B57CFF03A7589A2A9EDC21FC5A86A9F251812AE`. Export validation passed for all 1,000 match records. These remain measurements of the headless policy; Clean Hands is still strong and quota remains easy under this policy.
