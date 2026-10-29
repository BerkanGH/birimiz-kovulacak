# v3 rules engine

The implementation is independent of v2 and uses only the unchanged `Core/Line/Contracts` types. All eight engine
stages are implemented, including the optional `Tools/LineSim` console runner. Core targets netstandard2.1 / C# 9;
engine and tests use no UnityEngine APIs.

## Structure and determinism

`LineEngineFactory` creates the internal partial `LineEngine`. Its main file owns setup, schedule, phase deadlines,
presence, request deduplication and the event queue. `Rounds` owns private round state and applied blame deltas;
`Office` owns calls, queued mail, tricks and tips; `Meeting` owns dossiers, weighted recordings, votes, firing,
points and history. `Views` constructs a recipient allow-list. Input/config/result/history/view objects are detached
copies, so callers cannot mutate engine state through a returned list.

One independent SplitMix64 stream drives engine randomness. Seats are traversed in ascending id order; no logic
depends on dictionary order or LINQ. `Tick` processes the earliest absolute phase/timer/defender/presence deadline
until caught up. Expiries precede phase transitions at equal times. Host clocks must be finite, nonnegative and
monotonic. Fractional defense boundaries advance by index without floating-point division. An unchanged tick
allocates nothing and leaves StateVersion unchanged; consuming a fresh request also advances StateVersion.

Round deltas record the applied amount after the favourite reduction and zero floor. Disconnected seats retain
their place and use default choices. A full week produces a deterministic FNV-1a result hash at WeekResults;
natural completion or HostContinue then makes NextHistory available. Abandonment awards no points.

## Wire format and privacy

LineCodec uses magic `B3`, little-endian UInt16 protocol version, and a message-kind byte. Primitive widths and
field order are explicit; enums/bools are one byte, nullable objects have a presence byte, and strings/lists use
canonical unsigned LEB128 length+1. The decoder rejects foreign tags/versions, invalid enums/bools/UTF-8,
truncation, trailing data and excessive lengths with FormatException. All 34 contract classes and 314 fields
have explicit serializers; only tests use reflection to check completeness.

Views contain the recipient's secrets and public facts. They never contain host recording candidate IDs;
recording identities appear only after ReportRecording. Calls and victim notices use player audiences. Anonymous
complaints are shuffled, and only the selected dossier leak identifies an author. False-tip blame remains public,
but its public event omits the identifying FalseTip reason. Each broadcast shares one message ID across all
recipients, preventing numeric offsets from exposing an omitted trick actor. Offline recipients queue all mail,
including manager/IT messages; the separate TrickHit event immediately signals an outage.

## Tests

Tests live in `Game/Assets/Game/Tests/Line` and have their own Unity EditMode asmdef. The .NET test project includes
the same sources alongside every v2 test.

The combined suite has **727 passing cases: 497 v2 + 230 v3**. The final Unity-side compile check printed
`COMPILE OK (0 errors)`. The existing v2 populated-results timing assertion is sensitive to machine contention:
it exceeded its 0.3 ms budget in some runs (0.324 ms and 0.618 ms), while full-suite runs also passed unchanged.
No v2 tests or implementation were altered to accommodate that timing fluctuation.

| File | Coverage |
|---|---|
| LineFlowTests | Setup, sparse seats, derangements, schedule, onboarding, deadlines, presence, deduplication |
| LineRoundTests | All six outcome tables, ties, intern, clue-cycle property, attempts, favourite and floors |
| LineOfficeTests | Phone transitions, mail limits/queue, all tricks, wiretaps, trace/tip resolution |
| LineMeetingTests | Dossier order, leaks, votes, defense order, firing ties, points and history |
| LineRecordingTests | Minimum duration, erasure, deterministic duration weighting, candidates, playback reports |
| LinePrivacyTests | Every recipient at every phase, encoded secret fingerprints, differential mail/call/vote/agenda/complaint scans |
| LineCodecTests | Every field of every contract class, root messages, malformed payloads and hash stability |
| LineSnapshotTests | Input/config/view/reveal/result/history mutation isolation |
| LineValidationTests | Invalid clocks/config/history, zero durations and large fractional-clock boundaries |
| LineSimulationTests | Bot decisions, 24 scenarios replayed twice, allocation/throughput/view-size measurements |

The simulation matrix is 3, 4, 5 and 6 players, Short and Full weeks, seeds 13, 179 and 4261; each scenario is replayed
and must produce the same ResultHash. A bot's own view guards every action. Calls may return Busy because another
player's phone state is intentionally private; bots cannot know that failure beforehand.

## Measurements

Windows x64, .NET 8, Release runner, default durations, host step 0.25 seconds; these are local measurements,
not performance guarantees. Three seeds (1927–1929) per player-count/week-length combination completed all 24
weeks and picked 126 recordings. Six-player averages: Short Week **341.62 ms**, Full Week **568.27 ms**.
Six-player idle Tick: **26.62 million/s**, **0 allocated bytes over one million ticks**. A six-player mid-week view
(day 1, second RoundPlay) encoded to **1,060 bytes**, below the 4 KB target.

For the three six-player Short Weeks, average applied blame per player per round was: PrimHavuzu +0.611,
FarkliEposta -0.111, KayipRapor -0.111, IhbarKutusu +0.778, TerfiListesi -0.722, OrtakSifre +0.500.
Firings by seat 0–5 were `[1,0,0,1,1,0]`; agenda hits by seat were `[0,0,0,1,0,2]`.
Fired tricks (enum order 1–6) were `[1,0,0,1,1,0]`; 12 recordings were selected. This small sample checks the
runner, not game balance.

Run tests with `dotnet test Tools/CoreTests/Tests/CoreTests.csproj`. If the NuGet service is unavailable and packages
are already cached, restore from the local NuGet package directory, then use `--no-restore`. Unity-side validation
uses `python -I Tools/scripts/compile_check.py`; the installed Blender Python can substitute for the unusable
WindowsApps Python alias on this machine.

Run the simulator with:

```
dotnet run -c Release --project Tools/LineSim/LineSim.csproj -- --weeks 3 --players all --length both --seed 1927
```

## Contract clarifications requested

- Session history has SeenRounds membership but no per-type usage counts. The engine prefers least-used types
  this week, then unseen session types. Exact session-frequency preference needs a round-count list in history.
- GreedBonus defaults to positive 1 although its XML comment describes a negative delta. The engine subtracts
  the configured bonus, so default Paragoz takers receive -3. Clarify that this field is a relief magnitude.
- Odd Email gives the odd player TopicId and includes that same id in GuessOptions. A client can therefore infer
  the correct guess without conversation. Independent topic/display identifiers or opaque guess tokens are needed
  to remove this contract-level information leak; the engine preserves the specified fields.
- Public blame changes necessarily permit inference about a lone false tipper. The engine never explicitly names
  the author in dossiers or public tip events and removes the public FalseTip reason, but perfect anonymity needs
  a rule change (for example, a private penalty or delayed combined adjustments).

Finite topic pools eventually exhaust in an unlimited session; after exhaustion the engine reuses a seeded topic
from the pool. RecordingCandidates means total candidates per pick, matching CONTRACTS_V3/config. Voice capture,
speech-density analysis, audio playback and clearing audio buffers remain the host voice adapter's responsibility;
the engine holds metadata only. No contract files or v2 implementation files were changed.
