# v3.1 rules engine

The pure C# engine implements V3_DESIGN §0 with the supplied contracts, targeting netstandard2.1 / C# 9
without UnityEngine. No contracts or v2 sources were changed.

## Implementation and determinism

LineEngine owns setup, scheduling, deadlines, presence and requests; its Rounds partial owns deals and
outcomes, Office owns calls/mail/tricks/tips, Meeting owns dossiers/recordings/votes/points/history, and
Views constructs the recipient allow-list. Setup, config, views, results and history are detached copies.
One seeded SplitMix64 stream drives randomness, with ordered seat traversal and no dictionary dependence.
Tick processes absolute deadlines, including each shortlisted defender. Idle ticks allocate zero bytes
and leave StateVersion unchanged. Identical setup/actions/times reproduce views, events and hashes.

Everyone starts at StartBlame (5); the strict session-points leader receives LeaderStartBonus (2).
Agendas independently draw non-self targets, rejecting assignments with more than two authors per target.
Five active tricks are shuffled; the sixth player receives a seeded duplicate. KayitSil is retired.

Each generated day is a deal then a vote round; session day one is Bonus Pool then Odd Email. Eligible
types minimize History.RoundUses plus planned uses. Complaint Box respects ComplaintMinPlayers; Lost
Report always needs at least four seats. Generated schedules cap hidden-role rounds at the seat count
to guarantee no repeats within the week. Once that budget is exhausted, alternatives are Bonus Pool
and Promotion List / Complaint Box. Roles go to unused seats with the lowest historical hidden-role
count. NextHistory accumulates actual rounds introduced and roles dealt. Forced test schedules/roles
may repeat hidden players; forced rounds still obey player minimums. Isolated complaint fixtures
explicitly lower ComplaintMinPlayers.

RoundPlayLength = RoundPlayByPlayers[players - 3] × Durations.RoundPlay / 150.0. The list retains base
seconds; LineDurations.Scale scales the effective length via that ratio. RoundIntro's PlayEndTime uses
the same calculation. Defaults are 100 / 120 / 135 / 150 seconds.

## Rounds and actions

- Bonus Pool deals pairs and exactly one trio at odd head counts. Partners contains every peer; Partner
  is its first entry. C/ChoiceC contain the third member, otherwise -1. GreedBonus is subtracted from
  the taker's blame delta whenever a group contains a sharer: Paragoz solo taker -5, two trio takers -3 each.
  InternShareChance is unused.
- Lost Report slips name another innocent. Every named innocent has an honest witness; exactly one
  innocent at 4–5 players or two at 6 appears on no slip. Only the culprit sees NoAlibi.
- Shared Password reveals the recipient's digit and the next position's digit, wrapping in public
  PositionOwners order. SubmitChoice.A is a private, changeable code. Because A is an integer with
  exactly CodeLength digits, generation uses a nonzero first digit and validation requires
  [10^(CodeLength-1), 10^CodeLength). No live correctness feedback is emitted. Attempts remains empty;
  AttemptsLeft is zero. Attempt returns BadParam during RoundPlay, WrongPhase otherwise. All connected
  players submitting shortens the remaining phase to AllSubmittedRemaining. At reveal, more than half
  of all seats must have entered the exact code. CodesByPlayer has six id-indexed entries (-1 for none
  or unused seats). Collective success uses SolvedBy=-1 and PasswordCracked.Actor=-1.
- Odd Email uses category × TopicsPerCategory + item IDs. The odd text comes from another category;
  guess options contain every topic in the majority category. TopicVariant is consistently -1.
  Historical topics are preferred unused, with seeded reuse when eligible topics are exhausted.
  TopicPairs is unused.

Tricks allow 1 Short / 2 Full uses per week, still at most once daily. Tips default to once weekly,
unlocking on zero-based day 2 of the first week; tricks unlock on day 1. Later weeks unlock both
immediately. Allowances are private. Genuine mail respects MailsPerDay (1). False tips add FalseTipBlame
(2), with TipResult.Player=subject, Arg=0, Victim=tipper; true tips keep Victim=-1. Busy/dead-line calls
privately notify the callee through MissedCall.Actor.

## Meetings, results and recordings

Warning ties award nobody. A plurality on a player at MaxIhtarsPerPlayerPerWeek awards nobody, without
transferring votes. The final shortlist is built after dossier blame adjustments: top 2 at 3–4 seats,
top 3 at 5–6, configurable through FinalShortlistSmall/Large. Ranking is blame, warnings, then seeded
priority. Only the shortlist defends, for DefensePerPlayer (30 seconds) each, and only the defender may
speak. Final votes abstain or target another shortlisted player. Most votes is fired, then higher blame,
more warnings, then the same seeded priority. Empty/tied ballots preserve ranking. Scores contains
every seat's votes/blame/warnings with Score=Votes; FinalVoteWeight is unused.

Survivors earn SurvivePoints, AgendaPointsPerIhtar per target warning, AgendaPoints for a fired target,
and EmployeeOfWeekPoints for a strict lowest blame. Fired players retain the existing zero-point rule.
AgendaIhtars is populated for everyone; tied lowest blame awards no employee bonus. The fired player
alone can PickNextManager during WeekResults, with A indexing LineManagerIds.All, excluding the current
manager. NextManagerPicked, NextManagerPick, ResultHash and NextHistory.NextManager reflect the choice.
Week start prioritizes Setup.ManagerProfileId, then History.NextManager, then a seeded nonprevious pick.

Recordings weight duration by RecordingFocusWeight (3) for calls joining any two members of today's
Bonus Pool pair/trio or involving today's hidden player from either round. The multiplier applies once
even if both conditions hold. Candidates remain distinct and meet minimum connected duration. Identities
become public only after ReportRecording. Capture, speech-density analysis, playback and audio-buffer
clearing remain the host adapter's responsibility; the engine holds metadata only.

## Codec and privacy

LineCodec uses magic B3, little-endian UInt16 version and message kind, explicit field order/widths,
nullable presence bytes and canonical unsigned LEB128 length+1 for lists/strings. Invalid versions,
tags, enums, booleans, UTF-8, truncation, trailing bytes and excessive lengths fail with FormatException.
Every additive field is encoded, including config/history. Retired fields round-trip for completeness.

Views include only the recipient's agenda, trick, allowances, partners, no-alibi information, digits,
code entry, calls and inbox. Encoded differential/sentinel scans cover these fields and every phase.
Other codes appear only at reveal; tip authors only in explicit false-tip results. Complaint authors
remain hidden except selected dossier leaks. Recording candidate IDs stay host-only. Broadcasts share
one mail ID, preventing offsets from exposing omitted actors. Offline recipients queue all mail.

## Validation

Final full run: **774 passed, 0 failed, 0 skipped: 497 v2 + 277 v3**.
The Unity-side compile check reports **COMPILE OK (0 errors)**. Deterministic replays and zero-allocation
idle ticks pass. Existing v2 wall-time performance thresholds can fail under concurrent builds/simulations;
the final full run passed after those processes stopped, without changing v2 code or thresholds.

Tests cover payoff tables, trios, no-alibi guarantees, password majority/overwrite/privacy, categories,
session balance, shortlist voting/ties/speaking, points/history/picks, weekly limits, missed calls and
recording focus. Reflection completeness/null tests cover every contract class. Bot weeks cover every
head count/length and replay identical hashes. Bots use only their own views; private Busy results
discovered by calling are the only accepted action failures.

Run dotnet test Tools/CoreTests/Tests/CoreTests.csproj and python -I Tools/scripts/compile_check.py.
The WindowsApps Python alias cannot launch on this machine; the installed Blender Python runs the same
isolated compile-check script. Final counts and measurements are recorded below.

## v3.1 balance

Reproduce with:

    dotnet run -c Release --project Tools/LineSim/LineSim.csproj -- --weeks 1000 --players all --length both --seed 1927 --step 2 --session-weeks 10

This runs 2,000 weeks per head count (1,000 Short + 1,000 Full), 8,000 total, with default rules and
unscaled durations. Each length uses seeds 1927–2926 and resets history every ten weeks. The host step
is 2 seconds (--step defaults to 0.25): bot actions occur on those boundaries, while engine deadlines
remain exact. Sessions exercise points, round/role history, allowances and next-manager picks.
Raw output: Tools/LineSim/v31-balance.txt.

Rank is pre-vote blame descending, warnings, then seeded shortlist priority; lower ranks cannot be fired.
Prior-evening leaders are sampled after the penultimate warning and include ties; strict-leader counts
are separate. Final-changed means the fired player differs from rank 1. Catch rates divide by rounds of
the corresponding type; n/a means ineligible. Blame cells give signed applied delta / absolute applied
swing per player per round after floor/favourite. Usage counts accepted actions.

Bots call but do not simulate speech or pool hidden knowledge. Password codes combine two known digits
with random guesses; Odd Email voting cannot use human conversation. Catch rates describe this policy,
not expected human success rates.

| Players | Weeks | Fired rank 1 | Rank 2 | Rank 3 | Ranks 4-6 | Prior-evening leader fired | Final vote changed outcome | Odd caught | Culprit caught | Password cracked |
|---|---|---|---|---|---|---|---|---|---|---|
| 3 | 2000 | 76.40% | 23.60% | 0.00% | 0.00% | 67.30% | 23.60% | 26.57% | n/a | 2.58% |
| 4 | 2000 | 95.95% | 4.05% | 0.00% | 0.00% | 78.40% | 4.05% | 18.20% | 23.80% | 0.00% |
| 5 | 2000 | 93.05% | 6.60% | 0.35% | 0.00% | 78.05% | 6.95% | 13.01% | 19.77% | 0.00% |
| 6 | 2000 | 95.70% | 4.10% | 0.20% | 0.00% | 77.80% | 4.30% | 10.41% | 13.62% | 0.00% |

Mean applied delta / absolute swing per player per round:

| Players | Bonus Pool | Odd Email | Lost Report | Complaint Box | Promotion List | Shared Password | Tricks/player/week | Trick allowance used | Tips/player/week | Tip allowance used |
|---|---|---|---|---|---|---|---|---|---|---|
| 3 | -0.372 / 1.989 | -0.104 / 1.184 | n/a | n/a | -1.132 / 1.132 | -0.044 / 1.273 | 1.491 | 99.41% | 0.716 | 71.57% |
| 4 | -0.680 / 1.921 | -0.108 / 0.868 | -0.098 / 0.933 | n/a | -0.871 / 0.871 | 0.212 / 1.192 | 1.493 | 99.53% | 0.723 | 72.31% |
| 5 | -0.519 / 1.998 | -0.124 / 0.665 | -0.097 / 0.725 | 0.319 / 0.883 | -0.687 / 0.687 | 0.371 / 1.148 | 1.491 | 99.39% | 0.716 | 71.60% |
| 6 | -0.709 / 1.949 | -0.131 / 0.522 | -0.117 / 0.555 | 0.245 / 0.815 | -0.529 / 0.529 | 0.479 / 1.119 | 1.490 | 99.32% | 0.722 | 72.17% |

| Players | Strict prior-evening leader fired | Strict-leader weeks |
|---|---|---|
| 3 | 65.42% | 1,226 / 1,874 |
| 4 | 77.60% | 1,434 / 1,848 |
| 5 | 77.01% | 1,397 / 1,814 |
| 6 | 76.36% | 1,363 / 1,785 |

Release measurement: **17.88 million idle ticks/second**, **0 bytes over 1,000,000 ticks**;
a six-player mid-week encoded view was **979 bytes**, below the 4 KB target. Wall-time measurements
are local observations and vary with machine load.

## Contract requests

None. RoundUses, GreedBonus, category topic IDs and false-tip attribution use the producer's clarifications.
