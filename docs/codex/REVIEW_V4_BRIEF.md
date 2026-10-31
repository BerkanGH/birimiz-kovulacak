# Codex brief — independent v4 integration review (gpt-6.1-sol)

Read-only review of the integrated v4 branch. Do not edit files; write your findings to
`Builds/codex/review_v4.md` (the only file you may create). Do not run Unity. You may run `dotnet test`, `python -I`
tools and `git` read commands.

## Scope (critical paths only, most severe first)
1. **Hidden-information boundaries**: `LineEngine` views/messages (`BuildView`, `MessagesFor`), host routing in
   `Scripts/Net/HostLineController.cs` (events by audience, Host-audience events never sent, messages per recipient,
   resync), `NetSession` RPC targets, UI debug overlay (`Scripts/Telemetry/TelemetryDebugOverlay*`), telemetry
   `private_until_match_end` buffering, MatchLog lines. Any path where a client (including the host player through
   UI, logs or the overlay) learns another player's private facts, choices, money, objective, trick use, unexposed
   incident actor, complaint author, call partner or message content before the rules make it public.
2. **Authoritative state transitions and idempotency**: duplicate request ids, reconnects, resync, repeated
   presentations, purchases, transfers, remedial requests, votes — money conservation, exactly-once charges and
   penalties, one MatchEnded per match, rematch accounting.
3. **Determinism and serialization**: codec completeness for every v4 type (incl. `LineMessage`, consent payload),
   enum validation, string limits, hash stability.
4. **Scoring invariants**: productivity provisional/final, closure path, career lines, ranks with ties, EotM tie →
   none, −50 once per distinct exposed incident, objective evaluation.
5. **Consent-aware telemetry and deduplication**: no collection before consent, host consent never covers another
   participant, two-party events need both, withdrawal purges, deterministic host event ids, client ids stable across
   retries, analyzer dedupe and synthetic separation (`Tools/scripts/analyze_playtests.py`).
6. **Concurrency/threading**: telemetry flush thread vs Unity main thread (no Unity API off the main thread),
   bounded queues, IO failure isolation.

Reference documents: `docs/GAMEPLAY_REDESIGN.md`, `docs/CONTRACTS_V4.md`, `docs/ANALYTICS_ARCHITECTURE.md`,
`docs/ENGINE_V4.md`.

## Output format
For each finding: severity (critical/high/medium/low), file:line, concrete failure scenario (inputs/state → wrong
outcome or leak), suggested fix. Then a short list of things you checked and found correct. Do not report style.
