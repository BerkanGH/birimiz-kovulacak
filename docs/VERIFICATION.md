# Verification Report

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
| `dotnet test` (Tools/CoreTests: core compiled as netstandard2.1 / C# 9) | **118 passed, 0 failed** |
| Unity Test Runner, EditMode, same sources inside Unity's NUnit | **118 passed, 0 failed** (`Builds/logs/editmode_results.xml`) |

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
| `suite/duck` | ×0.6 | Scripted rubber-duck story | _pending_ |
| `suite/full_rematch` | ×1.0 (8 min) | Full-length match + rematch in one session | _pending_ |

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
