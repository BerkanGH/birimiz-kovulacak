# v4 implementation report — gameplay redesign + playtest analytics

Branch `v4` (from `main` @ `341a54a`). Date 2026-11-01. Everything below was produced and checked in one session; **no
human has played v4 yet**. Every number marked *synthetic* comes from bots or fixtures and is not evidence that the game
is fun, fair, balanced or shareable. Model/tool usage per workstream: `docs/V4_EXECUTION_RECORD.md`.

## 1. What is playable now

- **Hybrid ruleset `v4.0-hybrid`** (`docs/GAMEPLAY_REDESIGN.md`): company productivity with a 65 % target (shared
  defeat below it), secret objectives (9 kinds, none "get X fired"), career points (+100 / +75 / +60 / +40 / −50 per
  exposed incident, shared ranks), Employee of the Month by quality + cooperation ratio (tie → none).
- **12 distinct tasks** (two per block, never repeated in a match; pool validated per preset before start), each with
  private facts, a communication dependency, controls, output/income rules, sabotage incidents with proof records, and a
  5-step visual tutorial (TR + EN). New raster art for every task.
- **Pacing**: Short 3 blocks (meetings predicted at ≈ 4:40 / 10:20 / 16:30 in a first session, ≈ 3:40 / 9:00 / 14:50
  later; Short ≈ 20 min total), Full 5 blocks; breaks between tasks; early submission carries saved time into the break.
- **Blame/warnings/firing**: blame is a public record (start 5, warning +3, missed task +1, exposed sabotage +2,
  remedial −2 with caps); daily ballot with *No action*; final defense for every present player; final vote with *Fire
  nobody* and a strict majority of the roster; no forced or automatic culprit.
- **Money and evidence**: start 4, block-filtered income, transfers (bribes allowed, never binding), escalating evidence
  purchases with per-block/per-match limits, records with provenance, idempotent exposure in meetings.
- **Communication**: private calls (voice) plus a complete text alternative — private texts, email with editable reply
  threads and optional templates, public meeting chat, anonymous complaints (presets incl. crude office humour, or free
  text), busy/missed-call callback and "send a text" shortcuts. Rich text is disabled for every player-authored string
  and names; the engine sanitises text (NFC, control chars, `<`/`>`).
- **Information visibility**: no omniscient culprit messages; dossiers show observable effects only; partner-visible vs
  public records per task; recordings keep the real 7-second reveal (context-aware selection, declared experiment).
- **Presentation**: first-launch fullscreen remembered afterwards; consent prompt; 3-card visual intro; v4 how-to;
  original soundtrack (8 cues) with ducking during calls, meeting speech and recording reveals; music volume/mute.

## 2. Files changed

`git diff --shortstat main..v4`: 374 files, +54,862 / −18,023 (56 commits on `v4`, plus merged workstream branches).
Main areas: `Core/Line/Contracts` (v4 contract), `Core/Line/Engine` (rewritten engine, codec, bots, schedule),
`Core/Telemetry` (new), `Core/Audio` (new), `Scripts/Net` (message channel, consent, rematch, ids, telemetry calls),
`Scripts/Telemetry` (new), `Scripts/UI/Desk`, `Scripts/UI/Meeting`, shell UI, `Scripts/Audio` (music),
`StreamingAssets/Localization` (`*.tasks.json`, `*.privacy.json`, rewritten desk/meeting files), `Audio/Resources/music`,
`UI/Resources/ui/{tasks,evidence,onboarding_v4,results_v4}`, `Editor` (fullscreen default, music import), `Tools/*`
(TelemetryTests, MusicTests, AnalysisTests, music tooling, analyzer, playtest forms, LineSim v4), `docs/*`.

## 3. Instrumented integration points (all verified in code)

| Source | Hook |
|---|---|
| Engine semantic events | `LineEventType` 40–50 (Host audience): choices with category codes, income, incidents, early-submit carry, objectives, ballots, recording picks, messages (metadata only), missed tasks, per-participant and per-task outcomes |
| Host lifecycle | `HostTelemetryBridge` calls in `HostLineController` (lobby, consent per seat, ready, match start/fail/end, every drained event batch, frame tick, connections, rematch, technical failures) |
| Recordings | `HostLineVoiceBridge.PlayPick` (candidates, chosen rank, speech ms, snippet ms, failure reason) and `VoiceSystem.OnHostRecordingFinished` (actual duration, stopped early) |
| Client | `ClientTelemetry` subscribes to `LineClient` (phase, results → UI friction, messages) and `ConnectionManager.StateChanged` (join/fail/leave/interruption/reconnect, debounced) |
| UI | `TelemetryHooks` calls in desk (task app, tutorial, threads), shell (intro, settings, moment marker F8, rematch), music director |

Catalog: 95 events — 93 implemented, `final_candidate_selected` not applicable (v4 has no shortlist),
`voice_quality_summary` pending (voice exposes stats only as a log line). `docs/ANALYTICS_EVENT_CATALOG.md`,
machine-readable `docs/analytics/event_catalog.json`.

## 4. Local logs and export

- Location: `<persistentDataPath>/telemetry/<environment>/events_<session>_<n>.jsonl`; on Windows
  `%USERPROFILE%\AppData\LocalLow\Leonidas Studio\Birimiz Kovulacak\telemetry\playtest\`. 1 MB × 20 files, 30 days.
- Only after consent (default **No**); Settings → Privacy: change, withdraw (purges unsent data, revalidated at
  delivery), delete local data, reset the pseudonymous id, open folder, **Export logs** (zip). Facilitators collect the
  zip from every machine (`Tools/playtest/FACILITATOR_CHECKLIST.md`).
- Secrets (choices, money, objectives, calls, incidents, remedial requests) are held in memory and written only at
  results, so the host player cannot read colleagues' secrets from their own folder mid-match.
- Automated runs: `Tools/run_local4.ps1` passes process-only consent and writes to `<run>/telemetry/` (cohort `bot_sim`).

## 5. Analysis commands

```powershell
python -I Tools\scripts\analyze_playtests.py <collected folders...> --observer obs.csv --feedback fb.csv --out Reports
python -I Tools\scripts\analyze_playtests.py <folders> --purge-player p-<id> --purge-out <clean folder>   # deletion request
python -I Tools\scripts\telemetry_fixtures.py --out docs\analytics\fixtures                              # synthetic fixtures
python -I -m unittest discover -s Tools\AnalysisTests
```
Outputs: `Reports/PLAYTEST_SUMMARY.md`, `PLAYTEST_METRICS.csv`, `PLAYTEST_GROUP_COMPARISON.csv`, `PLAYTEST_FUNNELS.csv`,
`charts/*.svg`. The committed `Reports/` were generated from the post-fix **bot runs** (bannered SYNTHETIC);
`Reports/fixtures_example/` from fixtures. Human data is excluded from nothing and mixed with nothing: synthetic data is
dropped unless `--include-synthetic`, strata (build × ruleset) are never pooled, intervals resample whole friend groups.
Observer template, coding guide, offline player-feedback form: `Tools/playtest/`. KPIs A–M and the H1–H9 plan with
provisional design targets: `docs/PLAYTEST_KPI_GUIDE.md`.

## 6. Unity Analytics / Dashboard

Not set up. The project has no linked Unity Gaming Services project and `com.unity.services.analytics` is not
installed. `UnityAnalyticsSink` lives in its own asmdef that compiles only when the package is present (define
`BK_UGS_ANALYTICS`); it has **never been compiled against the real package**. Required steps (link project, install
package, verify API version — SDK 6.1+/Unity 6.2+ moved consent to `EndUserConsent`, define custom events/parameters
from the cloud subset in the catalog, check event limits): `docs/ANALYTICS_SETUP.md` §6. Local analysis does not need it.

## 7. Tests executed (final state of `v4`)

| Suite | Result |
|---|---|
| Rules engine (`Tools/CoreTests`, same sources as Unity EditMode) | **512 passed**, 0 failed |
| Telemetry (`Tools/TelemetryTests`) | **90 passed**, 0 failed |
| Analyzer (`Tools/AnalysisTests`) | **29 passed**, 0 failed |
| Music planner (`Tools/MusicTests`) | **17 passed**, 0 failed |
| Unity-free compile check, editor and `--player` | **0 errors** |
| Unity batch build (6000.6.3f1) | succeeded, 185 MB player, 0 errors |
| UI mocks (`-uimock` desk/meeting/final/results/menu/lobby/match/howto) | 293 screenshots, 0 exceptions; lead reviewed contact sheets |
| Multi-process bot runs, synthetic voice, ×0.3 timings | 4p Short ×2 matches PASS (pre-fix build), 3p Short PASS, 6p Short Paranoyak PASS, 5p Full with simulated drop + reconnect PASS — identical result hashes on every process, all v4 phases, 0 errors; final 4p ×2 run on the last build: see §8 |
| Regression tests written for bugs found during integration | 26 (each verified to fail on the old code) |

Not run: Unity EditMode/PlayMode runner inside the editor (the same NUnit sources run under dotnet), real microphones,
human-audible checks of voice, recordings over music, or the soundtrack (no listening check), Unity Analytics, internet
play, memory sampling (`Tools/scripts/measure_memory.ps1` exists; not re-run for v4).

## 8. Performance and event volume (synthetic)

- Engine: 6-player block-2 view ≤ 3.4 KB encoded; 1,000,000 idle ticks allocate 0 bytes (Codex measurement).
- Telemetry disabled: 1 M builder calls ≈ 28 ms without per-call allocation; enabled ≈ 2.2 µs per event
  (build + validate + consent + enqueue); one background flush thread every 2 s; bounded queue 2,048, private buffer
  4,096, debug ring 256 (telemetry agent's measurements).
- Event volume per match, all processes together (bot runs, cohort `bot_sim`): 3p Short 433, 6p Short 701,
  5p Full 1,105 events; ≈ 20–40 KB of JSONL per process per Short match. 0 queue drops, 0 sink errors,
  0 technical failures, 0 unexpected transitions in the post-fix runs.
- Final 4p ×2 run on the last build: FINAL_RUN_PLACEHOLDER

## 9. Bugs found by integration and review (all fixed)

Lead integration/reading: phishing real account drawn per player (task unsolvable), fake-email trick argument mapping,
observer views at stale event times (135 false technical failures), missing RoundReveal→MeetingDossier transition,
stamp font dropping Turkish İ, obsolete Unity 6 audio import API, recording task context lost to a stale copy,
analyzer income including transfers (top share 340 %), coverage over 100 %, false 100 % crash rate, UI-hook vocabularies
collapsing to "other". Sonnet rules review: EotM cooperation term 100× too small, waiting T4 decisions blamed, T10
solvable without talking, HR-file victim always the lowest seat, submissions erased on disconnect. Codex review: seed in
the live log (reproduces every secret), private calls/remedials/objectives written before results, withdrawal and
retroactive-consent gaps, three event-id collisions, order-dependent task join, host mode never reset, unbounded
private release, customer-spec answer derivable from the public catalogue, new seat occupants inheriting history.

## 10. Remaining gaps and honest limits

- **No human playtest.** Fun, fairness, negotiation, accusations, recording relevance and shareability are unmeasured.
  Synthetic LineSim on the final engine (480 matches, `Tools/LineSim/v4-sim.txt`): Talk 98.9 % mean productivity /
  0 % closures, NoComm 61.7 % / 64 % closures, Talk + one Greedy 90.0 % / 0 %. This shows the dependency is wired, not
  that people will talk (Talk = perfect information sharing after contact). Per task, NoComm still reaches 100 % in
  Report Review (bots always file accurate reports and approve), 89 % in Budget Split and 87 % in Weekend Shifts — the
  three tasks whose communication pressure depends on players being tempted, to watch first with humans.
- Network bots cannot exchange information, so every post-fix network match closed the company (40–55 %); these runs
  validate flow, determinism, reconnects and telemetry only.
- Known design trade-offs (documented in the design §16): Budget Split is solvable if everyone asks exactly their need;
  Customer Spec's customer is shared by all pairs; Presenter/Archivist objectives may be easy; +75 is identical for all
  survivors while the target equals the closure threshold.
- Telemetry leftovers: a call requested before consent still reports ring time on connect; a task detail arriving in a
  later batch than its output is not joined; a retried delivery of the same engine batch would double-count income.
- Recordings in bot runs: the 6-player Paranoyak run played 2 of up to 6 possible reveals (few calls ≥ 6 s); measure
  candidate availability with humans before tuning.
- `voice_quality_summary` needs a structured stats API in `VoiceSystem`.
- Optional P5 features **not built**: room visits, cup/pen meeting interactions, manager TTS. Managers have text
  reactions and task weights only. Early firing / exit-report role: not built (firing stays final-only).
- Unity Analytics: adapter only, uncompiled against the package, no dashboard.
- KVKK/GDPR: no compliance claim; items needing human/legal review are in `docs/ANALYTICS_SETUP.md` §8.

## 11. Asset usage conditions

- **Soundtrack** (ElevenLabs Music, model `music_v2_5` as returned, Creator plan, 8 requests, 4,332 credits): per the
  Eleven Music terms page dated 2026-10-09, Creator allows online/offline commercial use except film, TV, radio and
  "Studio Games"; an Indie Game (< $500k revenue since release and developer < $1M last fiscal year) qualifies; a game
  that stops qualifying needs a plan that covers Studio Games within 60 days; no attribution required; "individual use
  only". Promotional gameplay clips are not explicitly addressed. **Flag for legal review**; no "cleared" claim.
  Details: `docs/AUDIO_MUSIC.md`. Generation needs `ELEVENLABS_API_KEY` at development time only; the key is not in the
  repo or build (scanned).
- **Art**: 23 PNGs generated by Codex's built-in image tool (the tool reported no model id) following the existing kit;
  provenance and prompts in `Art/UI/_tools/v4_generation.json`. Usage terms of the image service were not reviewed in
  this session — check the provider's terms before commercial release.
