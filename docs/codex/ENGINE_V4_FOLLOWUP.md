# Codex follow-up — v4 engine (bounded)

Your engine is committed (b535c4c) and the lead merged v4 into this branch (9fde4d3): it now also contains
`Game/Assets/Game/Core/Telemetry/**` (do not edit) and a lead contract addition in `LineEnums.cs`. Do not commit.

1. **Emit the new host-only events** (contract `LineEnums.cs`, `docs/CONTRACTS_V4.md` §8 last paragraph):
   - `HostOutcome` (49) once per participant of every resolved task: Actor = player, Target = task number,
     A = contribution quality 0..100 (the value EotM uses), B = productive task income credited for this task (after
     the block filter), C = flags 1 cooperation opportunity, 2 cooperation credit, 4 connected at resolution.
   - `HostTaskOutcome` (50) once per resolved task: A = task number, B = output points removed by sabotage options of
     that task (0 if none; never a second loss), C = groups × 10 + groups that reached full output (all-player tasks:
     1 group).
   - `RemedialResolved.C` = output points actually added (0 when declined/expired).
   Add tests (event presence/values for at least T1, T7, T5, a remedial, and a sabotaged T1), keep privacy scans green
   (these are Host-audience events: assert they never appear in any client event list).
2. **Balance probe fixes (rules decisions by the lead — implement exactly):**
   - `Archivist`: count only records the player obtained by **purchase** or **partner visibility** (TaskEntry,
     AuditFigure, ReportDraft visible to them, DealPayment/OfferAcceptance of others) — exclude own calls, own
     transfers, public TaskOutcome/Warning/Remedial records and presented-by-others records. Default threshold
     `ArchivistRecords` = 4 (config change in `LineConfig.CreateDefault` is the lead's file: instead make the engine
     read the config value and tell the lead the recommended default in your handoff; keep tests independent of the
     default by setting the config in the test).
   - `Presenter`: unchanged rule, but bots must present at most one record per meeting and only records that are
     about another player (so the bot probe stops completing it trivially).
   - Investigate why `shift_cover` Talk (80%) is below NoComm (83%) and why no bot ever completes `Patron`; fix bot
     policy bugs if any (do not change task rules); report findings.
3. Re-run `dotnet test Tools/CoreTests/Tests/CoreTests.csproj` and the same LineSim matrix command, update
   `Tools/LineSim/v4-sim.txt` and the tables in `docs/ENGINE_V4.md` (append a "Follow-up" section; keep it labelled
   synthetic).

Handoff: what changed, test counts, new sim numbers, recommended `ArchivistRecords` default.
