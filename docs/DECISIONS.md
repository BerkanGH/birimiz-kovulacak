# Birimiz Kovulacak — Implementation Decisions

Source design: `Birimiz_Kovulacak_Oyun_Akisi.pdf` (concept v0.2, July 2026). The written rules in the PDF are the
primary reference. Everything below that is **not** stated in the PDF is a *first-version implementation decision*,
not a finalized design rule. Each decision is tunable; where a value lives in configuration, the config key is named.

## 1. Environment & toolchain (verified 2026-10-23)

| Item | Decision / finding |
|---|---|
| Engine | **Unity 6000.6.3f1** (Unity "Supported" stream). The current LTS (6000.3.27f1) was downloaded through Unity Hub but its installation requires a Windows elevation prompt that was not approved (two attempts). Deviation from the "LTS" requirement is recorded here. All scenes, prefabs, and materials are produced by an editor build script (`ProjectBuilder`), so the project can be regenerated on an LTS editor later. |
| Render pipeline | URP (bundled with the editor). |
| Networking | Netcode for GameObjects 2.13.3 + Unity Transport 2.x, host-authoritative listen server. (NGO 3.x requires Unity 6000.7.) |
| Blender | Blender 5.2.2 LTS, driven headless through its Python API (`blender -b -P`). **Blender MCP and Unity MCP are not connected to this session**, so the equivalent work is done with scripts that are committed (`Art/Scripts`), which keeps every asset reproducible. |
| Codex | Codex CLI 0.162 (bundled with the OpenAI VS Code extension), logged in with ChatGPT. Delegation verified with a smoke test. |
| Rules tests | Pure C# core is compiled both by Unity and by a .NET SDK 8 NUnit harness (`Tools/CoreTests`) so rules can be tested in seconds outside the editor. |

## 2. Scope defaults (from the brief)

Windows x64, keyboard + mouse, exactly four players (one host + three clients), three employees + one secret HR
Informant (İK Muhbiri), ~8-minute match, one compact open-plan office, external Discord voice, Turkish UI with
localization keys, stylized low-poly 3D. Not prerequisites: dedicated servers, crossplay, accounts, proximity voice,
ragdolls, Steam.

## 3. Match timeline (config: `MatchConfig.Durations`)

| Time | Phase | Notes |
|---|---|---|
| 00:00–00:30 | Briefing | Quota, desk assignment, private role & objective. Movement allowed, interactions disabled. |
| 00:30–03:30 | Work 1 | All office interactions. |
| 03:30–04:00 | Manager raid | 8 s alarm warning, then the manager walks a fixed, readable route and inspects four desks. Interactions remain allowed (risky). |
| 04:00–05:30 | Work 2 | |
| 05:30–06:00 | Final inspection | 5 s warning, second inspection; at 06:00 the evidence dossier and quota are frozen. |
| 06:00–07:30 | HR meeting | 30 s discussion + accusations, 2 × 20 s defenses, 10 s ballot, 10 s verdict. |
| 07:30–08:00 | Results | Firing, reveal, recap, rematch vote. |

Phase changes: timed interactions continue across Work↔Raid↔Work↔Final boundaries; entering the meeting cancels every
active interaction (no reward) and drops carried items where the carrier stood. Every request carries the `MatchId`;
phase-restricted requests are validated against the *current* phase on arrival, and timed interactions are completed
by the host clock only, so a late packet can never complete an earlier-phase action.

## 4. Tasks (config: `MatchConfig.Tasks`)

Demand-driven so that work is a real choice and equipment is contested.

| Task | Flow | Time | Quota / performance |
|---|---|---|---|
| Coffee | Take a mug from the shelf → brew at the single coffee machine → deliver to the manager's tray. | brew 6 s, deliver 2 s | 1 |
| Report | Write at **your own** desk PC → the shared printer prints (6 s, one job at a time) into its tray, labelled with your name → carry to the manager's inbox. | write 5 s, deliver 2 s | 2 |
| Phone | The single office phone rings every 28–42 s for 20 s; answer it while it rings. | 8 s | 1 |
| Clean desk | Desks get dirty over time; take a spray from the cleaning closet and clean a dirty desk. | 7 s | 1 |

Coffee/report orders accumulate (max 3 each); deliveries without an open order are rejected. Quota target: **18
points** (tuning value, `QuotaTarget`). Quota is awarded exactly once per completion, on the host. Delivered mugs stay
on the tray (reusable, "used"); delivered reports are archived.

## 5. Sabotage (config: `MatchConfig.Sabotage`)

The PDF defines four MVP sabotage actions and asks for six; the two extra ones are **proposed extensions**.
Sabotage is never a menu action: each one requires walking to a physical target, often carrying an item, and
plays a visible animation for its duration.

| Sabotage | Duration | Range / equipment | Visible animation | Effect | Trace | Cooldown & limits | Reversal | True misconduct |
|---|---|---|---|---|---|---|---|---|
| Swap nameplate | 1.5 s pick + 2 s place | ≤1.8 m, carry a nameplate | fiddling | Desk's *apparent* owner changes (manager attributes by nameplate) | Nameplate text in the world; witnesses; host-side change timestamp (scan signal) | 20 s actor CD; 60 s same desk | Put the right nameplate back (2 s) | 2 (+1 if a wrong attribution happened while swapped) |
| Plant suspicious item | 3 s drawer / 1 s surface | ≤1.8 m, carry a suspicious item | sneaky crouch | Incriminates the desk's apparent owner when discovered | Item handling history (host); witnesses | 25 s actor CD; 60 s same victim; ≤2 suspicious items per desk | Search (2 s) and remove | 3 (+1 if discovered while attributed to someone else) |
| Lock workstation | 4 s | ≤1.8 m of another desk's PC | typing (same anim as report/unlock) | That desk cannot write reports for 45 s | Public timestamped lock log entry, no actor | 30 s actor CD; PC immune 30 s after unlock | Anyone unlocks (5 s) | 2 (+1 if the owner was blocked) |
| Hide report | 2.5 s | ≤1.8 m, carry another player's report | sneaky | Delays delivery | Report stays physical in a hiding spot/drawer; witnesses | 30 s actor CD; 60 s same victim | Search spot and take it | 2 (+1 if never delivered) |
| Spill coffee *(extension)* | 2 s | ≤1.8 m of a desk, carry a full mug | "oops" tilt | Visible spill → mess at inspection | Visible stain; mug becomes used; witnesses | 30 s actor CD; 1 spill per desk | Clean with spray (5 s, no quota) | 2 (+1 if the manager penalises someone else for it) |
| Move clutter *(extension)* | 1 s | ≤1.8 m, carry clutter | dumping | Worsens desk tidiness (not a suspicious collectible) | Visible clutter; witnesses | 15 s actor CD; ≤2 clutter per desk; 45 s same victim | Carry it to the junk bin | 1 (+1 if the manager penalises someone else for it) |

Anti-grief (host): per (actor, victim) cooldowns above; at most 3 active sabotage effects per victim desk; at most 4
unreversed effects per actor; sabotage never subtracts quota directly, so total company damage is bounded by time.
Picking up an item that is the subject of active, unsealed evidence is **evidence tampering** (+1 misconduct, evidence
becomes "moved"). Planting into your own desk is not misconduct. Witness notes describe the observable activity
(e.g. "rummaged in desk 3's drawer; holding: rubber duck → nothing"), never the intent.

## 6. Performance, suspicion, true misconduct

- **Performance** (public): quota points the player personally completed.
- **Suspicion / observed risk** (public, partial): weighted sum of active evidence and manager observations attributed
  to the player. Shown as a 0–4 meter. Official accusations are tracked as a separate component and are **not** part
  of observed risk (they already act through nomination).
- **True misconduct** (host-only ledger): per incident `{id, true actor, type, target desk, victim, time, base,
  consequence}`. Moving evidence or swapping nameplates never rewrites the actor. The manager and the verdict never
  read it; it is used for the Informant outcome, a few objective predicates, and the post-match reveal.

## 7. Evidence model

Evidence records are created by manager discoveries (raid, final inspection), by players recovering hidden reports,
by public system logs (workstation locks) and by players presenting their private witness notes in the meeting.
Status: `Active`, `Moved` (physical item left the recorded place: weight × 0.5), `Disposed` (thrown in the junk bin:
excluded). Final inspection refreshes statuses and creates records for newly found items; at 06:00 the dossier is
frozen. Items in closed containers (drawers, hiding spots) are **not sent** to clients that have not placed or
searched them.

## 8. HR Informant

Exactly one, random per match (never the same player twice in a row when avoidable). Tools are used from the same
private "phone" panel every player opens to read their objective, so using them produces no visible tell.
- **Audit Scan × 2**: on an evidence record or a nearby physical item → reliability (Low/Medium/High: does the
  apparent owner match who actually put it there?) + one factual signal (`NameplateChangedAfterPlacement`,
  `MovedRecently`, `HandledByMultiple`, `NoInconsistency`). Never names a person.
- **Sealed Report × 1**: freezes an evidence record's attribution and locks its physical item until the meeting.
  Publicly visible as "sealed", sealer hidden. A seal preserves; it does not certify guilt.
- Category-level HR terminal and meeting note are deferred (secondary).
- Wins iff quota succeeds ∧ Informant not fired ∧ fired player is in the highest **positive** true-misconduct tier
  (ties count). Zero misconduct everywhere → no culprit win. The "identity stayed secret" bonus is deferred until it
  has a measurable in-game rule.

## 9. Personal objectives (config: `MatchConfig.Objectives`)

Each employee receives one of twelve cards (no duplicates per match; targets never self; recent holder→target
pairs are avoided across rematches). All employee wins require company success and surviving the verdict.

| Id | Name (TR) | Completion predicate | In-match progress shown to holder |
|---|---|---|---|
| `DISMISSAL_PLAN` | Kovdurma Planı | target is fired | target only |
| `PROTECTION_DEAL` | Koruma Anlaşması | target is not fired | target only |
| `SCAPEGOAT` | Günah Keçisi | ≥2 distinct incidents *you* caused were discovered and attributed to the target | x/2 |
| `QUIET_STAR` | Sessiz Yıldız | highest final performance (ties ok) and you filed no official accusation | rank + accusation flag |
| `CHAOS_COORDINATOR` | Kaos Koordinatörü | suspicious items you planted were later discovered at ≥3 distinct desks | x/3 desks |
| `CLEAN_HANDS` | Temiz Eller | ≥4 tasks across ≥2 types and zero sabotage incidents | tasks, types, clean flag |
| `OFFICE_ALL_ROUNDER` | Ofis Ustası | ≥1 completion of each of the 4 task types | checklist |
| `FIXER` | Tamirci | reversed ≥2 distinct incidents caused by other players | x/2 |
| `DESK_GUARDIAN` | Masa Koruyucusu | target's *assigned* desk had no suspicious item discovered at the raid or final inspection | per-inspection status |
| `UNNOTICED` | Fark Edilmeyen | received no official accusation | resolved in meeting |
| `PERFORMANCE_RIVAL` | Performans Rakibi | your final performance > target's | both values |
| `UNDER_PRESSURE` | Baskı Altında | ≥2 tasks during Work 2 and no negative manager observation of you at the final inspection | x/2 + penalty flag |

## 10. Manager NPC

Kinematic walk along a fixed route of desk inspection stops (route + dwell are layout data). Observes only during the
raid and final inspection, within a 10 m / 70° half-angle vision cone with line of sight. Attributes desks by the
**nameplate on the desk** (fallible by design). Observations: suspicious item found, messy desk (dirt / spill /
clutter), not at desk, at someone else's desk, moving during inspection, carrying a suspicious item, caught in the
act, behind on work, and a positive "working hard" (work pose or task at own desk). Each reaction shows a bubble and a
public feed line naming what attracted attention.

## 11. Meeting & verdict (config: `MatchConfig.Verdict`)

- Each player may file **one** official accusation (not self) during discussion. Players may also present private
  notes (witness / search / scan results) as public evidence.
- Shortlist: two most-accused. Ties → higher observed risk → lower performance → match-seeded draw. One accused →
  that player + highest-risk other. Nobody accused → two highest observed risk.
- Defense windows of 20 s each (more accused first). Then one final ballot per player among the two (no self-vote,
  abstain allowed). The ballot is not an accusation.
- **Firing score = 0.45 · vote share + 0.35 · observed risk (normalized) + 0.20 · performance deficit (normalized).**
  - vote share = ballots for candidate / ballots cast (0 if none);
  - risk norm = candidate risk / max(6, highest risk among all players);
  - perf deficit = (best performance − candidate performance) / max(4, best performance).
  Exact tie → higher risk → lower performance → seeded draw. A structured explanation is sent with the verdict.
- An accusation against someone with zero true misconduct costs the accuser 1 performance point, applied **after**
  the verdict (it can affect objective evaluation but never the verdict, so hidden truth is not leaked mid-meeting).

## 12. Networking

Host-authoritative listen server (NGO). Clients own only their avatar movement (owner-authoritative transform for
responsiveness); everything else is a request → host validation → filtered snapshot. Snapshots are built per
recipient from the rules engine (public / owner-only / host-only separation) and sent as compact binary. Connection:
direct IP/port (UDP 7777) for LAN or VPN (Tailscale / ZeroTier / Radmin) — this is the verified path. Unity Relay
requires linking a Unity Gaming Services project; it is optional and only reported as verified if actually tested.
Reconnect grace 30 s with slot/role/objective/charges restoration; host loss or grace expiry → match abandoned, no
victories. Host migration and replacement players are out of scope.

## 13. Presentation

Bean-style characters (as in the PDF art) with four silhouettes + colors: Mavi (blue, tall, tie), Sarı (yellow,
round, glasses), Pembe (pink, hair bun), Yeşil (green, short, headset). Manager: larger brown bean in a suit with a
moustache. Third-person elevated follow camera, WASD + mouse, remappable bindings stored in PlayerPrefs.
