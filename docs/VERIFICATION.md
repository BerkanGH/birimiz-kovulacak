# Verification Report

## v2 "İş Haftası" (2026-10-29, overnight)

Build: Windows x64, Unity 6000.6.3f1, protocol 2, same machine as below. Logs under `Builds/runs/` (ignored).

| Level | Status | Evidence |
|---|---|---|
| Rules engine tests | **497 passed, 0 failed** | `dotnet test` in `Tools/CoreTests/Tests` (privacy byte scans, determinism with 6 players and all 39 verbs, full 5-day week, balance v2.2/v2.3) |
| Voice core tests | **70 passed, 0 failed** | `dotnet test` in `Tools/VoiceTests/Tests` (Opus round trip, jitter/loss/FEC, routing incl. call privacy, zero-alloc hot path) |
| Unity compile + player build | **Yes** | `Tools/build.ps1`: "Player build Succeeded", 0 errors; builder "NAV reachability: 69 ok, 0 problems" |
| 3-player local match, week mode, 2 days | **Yes** | `final_p3_week`: Monday (Paragöz) → Tuesday (Disiplinci), identical result hashes per match, 0 errors |
| 6-player local match, quick play, synthetic voice | **Yes** | `final_p6_quick`: 6 processes, identical hash, 9 incidents, 0 errors |
| 6-player week day with voice | **Yes** | `int7_p6_week`: identical hash, company succeeded, a winner |
| In-game voice end to end in Unity | **Yes (synthetic audio)** | `-VoiceTest speech`: native libopus loaded, ~16–17 kbps per talker, call frames routed only to the partner, walls muffle; no real microphones were used |
| Memory per process (4 bots, visible windows) | **−39 % working set / −29 % private vs v1 RC** | `docs/PERFORMANCE.md` |
| Remote internet match | **No** | One machine only. |
| Human playtest (fun, voice feel, camera feel) | **No** | Everything above is bots or synthetic audio. Needs real players. |

Cross-reviews performed: Codex reviewed the Claude-written Unity code twice (5 + 8 verified findings, all fixed);
a Claude reviewer stress-tested the Codex rules engine for exploits with a probe harness (12 findings →
`docs/BALANCE_V2.md`, fixed in config + engine v2.2).

---

## v1 release candidate (2026-10-23)


Date: 2026-10-23 · Build: Windows x64, Unity 6000.6.3f1 · Machine: i5-12500H, RTX 3050 Ti Laptop, Windows 11.

## Status at a glance

| Level | Status | Evidence |
|---|---|---|
| Successfully compiled | **Yes** | `Tools/build.ps1` → "Player build Succeeded", 0 errors (Builds/logs/build.log) |
| Successfully launched | **Yes** | Built player runs windowed, renders the office, menu, HUD, meeting and results (screenshots below) |
| Four-client local match verified | **Yes** | 4 separate processes (1 host + 3 clients) over NGO/Unity Transport on localhost; all phases; identical result hashes |
| Remote internet match verified | **No** | Only one machine was available; no second network. Direct-IP path is the same code; see "Not verified". |
| Human playtest completed | **No** | All matches were driven by automated test bots (`-bot`) or the input-simulation driver. No human results are claimed. |

## 1. Automated rules tests

| Suite | Result |
|---|---|
| `dotnet test` (Tools/CoreTests: core compiled as netstandard2.1 / C# 9) | **125 passed, 0 failed** |
| Unity Test Runner, EditMode, same sources inside Unity's NUnit | **125 passed, 0 failed** (`Builds/logs/editmode_results.xml`) |

118 tests were delivered by the Codex work package; 7 regression tests were added during integration for the
review findings below (each was confirmed to fail when its fix is reverted).

### Independent engine review
A separate read-only reviewer read the contracts and engine, then fuzzed ~4,000 random four-player matches against
invariants (single carrier per item, slot capacity, sealed items immobile, incident actors and attributions
immutable, Informant charges bounded, quota = sum of performance and frozen at the meeting, nothing completes after
the meeting starts, closed-container privacy, Informant/win results equal a recomputation from host truth): zero
violations, zero exceptions. Confirmed findings, all fixed with tests (`IntegrationReviewTests.cs`, CONTRACTS v1.1):
framing with your *own* report cost no misconduct (now a PlantItem incident); voluntary drops grew the public log
without bound (now private, log capped at 200 in views); a direct sighting could charge a nameplate swapper; recovered
hidden reports skipped discovery bookkeeping; closed-container slot indices leaked the presence of unknown items;
evidence put back where it was found was counted twice; Clean Hands / Under Pressure edge cases.

Coverage includes every check requested in the brief: exactly one Informant (many seeds, no repeat when avoidable);
private information only to eligible recipients (views and encoded bytes scanned for leaks; closed containers;
witness notes; anonymous lock logs); simultaneous pick-up race; quota awarded once (duplicate ids, re-validation);
framing never alters the true perpetrator (`RubberDuckScenario`); sealed evidence cannot be rewritten (pick-up,
nameplate swap, disposal, final freeze); Informant charge limits; stale actions rejected across phases and matches;
voting/tie/no-vote/single-nominee/no-nomination resolution; company failure invalidates all personal and Informant
wins; reconnect preserves role/objective/notes/charges and grace expiry abandons; identical result hash for every
recipient; anti-grief limits; manager cone/LOS/inspection-only/nameplate attribution; codec round-trips and
malformed input; determinism; a scripted full 4-player match with every task and sabotage type.

## 2. Multi-process network runs (built player)

All runs: 1 host + 3 clients as separate `BirimizKovulacak.exe` processes on 127.0.0.1, every player driven by the
automated test bot (it only sees its own filtered view and sends ordinary requests). `Tools/scripts/verify_logs.py`
checks that every process saw every phase, logged no exceptions, and received the same `ResultHash`.

| Run | Timeline | What it shows | Result |
|---|---|---|---|
| `net1` | ×0.5 | Full match over the network | PASS — hash `9e9327d68b3c00b3` on all 4; 5 incidents (plant, 2× lock, hide report, clutter); framed player blamed while the true actor stayed recorded |
| `suite/reconnect` | ×0.5 | Client 2 loses its connection at 55.7 s, rejoins 6 s later | PASS — rejoined as a new connection mapped to the same slot (`reconnected=True`), match completed, hash `ce92333d2ce7e465` on all 4; Informant won |
| `suite/grace_expiry` | ×0.5 | Client 3 drops and never returns | Expected outcome — host abandons exactly 30 s after the drop (`MatchAbandoned`), all remaining clients show the Abandoned screen, no results/winners |
| `suite/full_rematch` | ×1.0 (8 min) | Full-length match + rematch in one session | PASS — match 1 hash `e18f85d8b28ab0e4` (6 incidents, quota 31/18), match 2 hash `ea23e1e52b7e0edd` (13 incidents, quota 29/18) identical on all 4; Informant rotated Mavi → Sarı |
| `suite/duck` (first try) | ×0.6 | Scripted rubber-duck story | PASS for hashes, but the scripted nameplate swap never happened: a bot pathing bug (start point inside the NavMesh agent margin next to a desk). Fixed (start-point snapping + side-step) |
| `suite2/duck` | ×0.6 | Scripted rubber-duck story, after the fix | PASS — hash `02806fa9d09cfc44` on all 4. Pembe planted the duck in Mavi's drawer; Sarı swapped the nameplates (desk 0 now showed Yeşil); the manager found the duck and blamed Yeşil; Yeşil (secretly the Informant) scanned and sealed it, presented the scan and accused Pembe; the others still voted Yeşil out. Reveal: PlantItem actor = Pembe, both NameplateSwaps actor = Sarı, Mavi and Yeşil true misconduct 0 |

| `final/host_kill` | ×0.5 | Host process force-killed during Work 1 (no goodbye) | All 3 clients detect the loss after the 10 s transport timeout, retry for the 30 s grace period, then end with "Yeniden bağlanılamadı. Maç iptal edildi." (first attempt exposed a zombie "reconnected" state — fixed by tearing down the old connection and requiring a fresh approval) |
| `final/inputsim` + `final/solo_modifier` | ×0.5 / ×0.35 | Human control path: real Input System keyboard events (W/E/R) through movement, focus, prompts and requests | Walk + focus + take mug + brew + work pose work through the human path. Found and fixed two focus issues: the adjacent mug shelf stole focus from the coffee machine; your own carried item could become the focus target |
| `final/two_matches` | ×0.5 | Rematch in one session | PASS — hashes `810bd4fea8b37979` / `b241a3b1a4607df4` on all 4 |
| `final/solo_modifier` | ×0.35 | Daily modifier on a rematch (seeded) | Match 2 ran as "Kahve krizi" (briefing card + HUD show it; modified config sent to clients) |
| `release_check` | ×0.5 | Release build, 4 processes | PASS — hash `9159fc98946d4e27` on all 4, 8 incidents, Informant won |
| ZIP check | — | `BirimizKovulacak-Windows-Playtest.zip` extracted to a temp folder and launched | Starts, hosts, fills seats, reaches Work 1, no errors |

## 3. Visual checks

Screenshots are captured automatically by each process (`-screenshots`) at fixed moments of every phase
(`Builds/runs/**/shots`). Reviewed: briefing card, office HUD (quota, orders, coworker suspicion meters),
contextual prompts, locked workstation (red screen + padlock), manager vision cone during inspections, raid banner,
meeting dossier (evidence, sealed badge, witness statements, accusations, defense tags), verdict breakdown,
results reveal (roles, objectives, true misconduct bars, titles like "Profesyonel Günah Keçisi").

## 4. Not verified / known limitations

- **Internet play across different networks** was not tested (single machine). The connection code is the same for
  LAN/VPN/port-forwarded play; it needs one real remote session (see `HOSTING.md`, Tailscale recommended).
- **Human playtest** has not happened. Use `docs/PLAYTEST.md` and record results separately.
- Unity **LTS** editor could not be installed (Windows elevation prompt was not approved); built with 6000.6.3f1.
- Blender MCP / Unity MCP were not available in the session; assets come from committed Blender Python scripts.
- Unity Relay/Lobby are not used (would need a linked UGS project).
- Balance (quota 18, durations, cooldowns, verdict weights) is a first guess; bots are not a substitute for players.
- Bots are a verification tool, not opponents: their play is simple and not representative of human strategy.
- The host is trusted (it holds full state); owner-authoritative movement means a modified client could move faster
  than allowed (rule outcomes stay host-validated).
