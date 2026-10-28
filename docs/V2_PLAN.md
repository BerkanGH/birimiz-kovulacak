# Birimiz Kovulacak v2 — "İş Haftası" plan

Owner of this file: Claude (lead). Every agent reads this first. Started 2026-10-29 ~01:00 (overnight session).
Status lives in `docs/V2_STATUS.md` (lead updates it).

## 0. Product decisions (from the producer, final)

- **In-game voice replaces Discord.** Proximity voice in the office, private phone calls between players
  ("Scam Line" spirit: you call a colleague and talk them into turning on someone), everyone-hears in the meeting.
- **Camera:** closer third-person; other players are only visible when your avatar has line of sight to them.
- **Players:** 3–6 (default 4, host can fill seats with bots). Exactly one HR Informant.
- Visual quality must jump: map, props, characters, character interactions, UI/UX (buttons, flow).
- The architecture must visibly care about memory/CPU (budgets, pooling, measured numbers).
- Fun + hook first, built data-driven so content scales cheaply.

## 1. Pillars

1. **Konuş, kandır, kovdur** — talking is the core verb: proximity voice, private calls, eavesdropping, tip line.
2. **Kimse her şeyi görmez** — rooms, walls, partitions, line-of-sight visibility, power cuts, partial CCTV.
3. **Her gün başka bir gün** — manager personality × day event × secret sabotage cards = run variety.
4. **Hafta boyu hikâye** — a 5-match "work week" with career points, unlocks and a Friday finale (3–4 h sessions).
5. **Hafif ve hızlı** — budgets (§8), pooled objects, data-driven content in StreamingAssets.

## 2. Session structure: İş Haftası (week mode)

Quick Play = one match with every feature on and a random manager. **İş Haftası** = 5 matches (Pzt→Cum):

| Day | Manager profile | New this day |
|---|---|---|
| Pazartesi | Paragöz | basics: tasks, basic sabotage, calls, mail (onboarding day) |
| Salı | Disiplinci | secret sabotage cards |
| Çarşamba | Kayırmacı | tip line (ihbar hattı), CCTV footage in the meeting |
| Perşembe | Paranoyak | everything |
| Cuma | random | everything, **career points ×2** ("Cuma finali") |

Career points per match: company quota met +1 (each non-fired player), survived +1, personal objective won +3,
Informant won +3, fired −2, manager's favourite at the end +1. Friday ends with "Ayın Elemanı" ceremony (highest
total; ties allowed). A fired player simply plays the next day as a new hire (no elimination, no downtime).

## 3. Match (≈8–9 min, unchanged skeleton)

Briefing 30 s → Work1 180 s → Raid 30 s → Work2 90 s → Final inspection 30 s → Meeting (discussion **45 s**,
2×20 s defense, 10 s vote, 10 s verdict) → Results 30 s. Durations stay in `match_config.json`.

## 4. Engine v2 features (Codex owns `Core/Engine`; Claude owns `Core/Contracts`)

E1 **3–6 players.** `Protocol.MaxPlayers = 6`. Layout has 6 desks; with fewer players the extra desks are unassigned
("boş masa": no owner, not inspected, still usable as a place to put things).

E2 **Manager profiles** (data: `ManagerProfileDef`). Each changes *what the right strategy is*:
- `paragoz` Hasan Bey — numbers only. Performance weight high, mess ignored, BehindOnWork ×2, WorkingHard ×2,
  quota +2. Favour: reports you deliver.
- `disiplinci` Nermin Hanım — order. Mess ×2, NotAtDesk ×2, personal calls ×2, fake emails ×1.5, one **surprise
  inspection** (no warning) in Work2.
- `kayirmaci` Selin Hanım — has a public favourite from the briefing; favour from coffee you deliver to her; the
  highest-favour player at the verdict gets a firing-score discount.
- `paranoyak` Kemal Bey — evidence ×1.5, CaughtInAct ×1.5, wider vision, always searches a tipped desk's drawer,
  +1 footage request in the meeting.
Favour is a public per-player number (0..N) so players can compete for it.

E3 **Secret sabotage cards.** `SabotageDef.Basic`: basic sabotage is available to everyone (nameplate swap, plant
item, hide report, spill coffee, move clutter). Card sabotage needs the card: each player (Informant included)
privately draws `CardsPerPlayer` (2) distinct cards from the card pool; different players may hold the same card,
so a sabotage never proves who did it. Cards are revealed in Results.

E4 **New card sabotages**
- `LockWorkstation` (existing) — now a card; UI name "Şifresini değiştir".
- `FakeEmail` — at another desk's PC whose **screen session is open**; pick one of the email templates
  (IntParam). Public inbox entry "from" the PC's owner + manager reaction (observation on the apparent sender) +
  system evidence "e-posta kaydı: desk.N.pc, 03:12". Screen sessions: a PC opens when its owner finishes a report
  and stays open `ScreenOpenSeconds` (60) unless the owner uses **Ekranı kilitle** (1 s, own PC). Teaches
  "lock your screen" through comedy.
- `PullPlug` — another desk's PC turns off (no reports, cancels the owner's report-in-progress = consequence).
  Visible cable on the floor; anyone plugs it back (2 s).
- `SaltCoffee` — at the kitchen counter, carrying a full mug: the mug becomes secretly salted (host-only). Whoever
  delivers it to the manager gets the blame (observation) and no quota; the salter gets the misconduct.
  Counter: **Tadına bak** (1 s, full mug in hand → private note salted/not, mug becomes Used).
- `InternetOutage` — at the server-room router: 40 s office-wide, no reports/printing/emails. Fix: restart
  router (6 s, anyone). Max 2 per match.
- `PowerCut` — at the fuse box: 25 s lights out; witness range 2.5 m, manager vision 3 m, CCTV records nothing.
  Fix: reset fuse (4 s). Max 2 per match.

E5 **Calls and tip line** (voice is Unity-side; *call state* is rules state):
- Verbs `CallPlayer`(callee), `AnswerCall`, `DeclineCall`, `HangUp`. Ring 10 s, max call 45 s, cooldown 8 s.
  Allowed Briefing..FinalInspection. Both parties show public activity `OnCall` (not with whom). The callee's
  ringing is public (`IncomingCall` event at the callee, no caller). Private `Me.Call` shows the other party.
- Host-only call records (caller, callee, start, end, answered) → revealed in Results recap.
- Manager seeing someone `OnCall` during an inspection → observation `PersonalCall`.
- **Tip line** `TipManager`(desk.N): 3 s on the phone, one per player per match, anonymous. At the next inspection
  the manager goes to that desk first, dwells longer and searches its drawer. Clean desk → owner gets a positive
  "temiz çıktı" observation (the tip backfires).
- Informant tool **Arama kayıtları** (1 charge): private note with all call records so far (presentable).

E6 **Mail task** (legit reason to stand at other people's desks): mail items appear in the mail room addressed to an
occupied desk (public); placing it on that desk's surface completes it (+1 quota). Max 2 open, every ~30 s.

E7 **CCTV footage (defense).** Layout cameras cover zones (rectangles). The host keeps a 1 Hz host-only position
log (player, zone/camera visibility, activity). In the meeting: the current defense speaker may request footage
once (`RequestFootage`, camera id + second-of-match); the Informant has one footage charge for any meeting phase;
Paranoyak gives everyone +1. Result = public evidence listing who the camera saw (±5 s) and what they were doing;
"kayıt yok" during a power cut.

E8 **Week/career** carried in `SessionHistory` (day index, career points, past profiles); results carry career
deltas and the week standings.

## 5. Unity-side features (Claude + Claude sub-agents)

U1 **Map v2** — multi-room office ~32×20 m (sketch below), cubicle partitions, doorways (no door physics), a
restroom (private corner for calls + hiding spot in the cistern), server room, kitchen, copy room, mail room,
glass manager office, meeting room with projector, storage. Walls block LOS and muffle voice.

```
 z=10 ┌──────────────┬───────────────┬──────────────┬────────────┐
      │ Müdür odası  │ Toplantı odası│   Mutfak     │ Fotokopi   │
      │ (cam walls)  │ 6 seats, proj.│ coffee, salt │ printer    │
 z=4  ├────door──────┴─────door──────┴────door──────┴───door─────┤
      │  Açık ofis: 6 desks (2 rows × 3, cubicles)  │ Resepsiyon │
      │                                             │ mail, phone│
 z=-5 ├───────┬─────door───┬──────door──────┬──────door──────────┤
      │ Depo  │  Tuvalet   │  Sunucu odası  │ Dinlenme köşesi    │
      │spray, │ cistern    │  router        │ sofa, plant spot,  │
      │fusebox│ hiding     │  (no camera)   │ junk bin           │
 z=-10└───────┴────────────┴────────────────┴────────────────────┘
     x=-16                                                    x=16
```

U2 **Camera & visibility** — closer follow camera, walls between camera and player fade; remote avatars (and what
they carry) fade out when not in your line of sight; name tags only when visible.

U3 **Characters** — 6 looks (Mavi, Sarı, Pembe, Yeşil, Turuncu, Mor), expressive faces, 4 manager looks (one per
profile), animations: idle variants, walk, sneak, carry, typing, phone call, cleaning, rummaging, sit, emotes
(wave, point, facepalm, shrug, laugh), shocked, fired walk-out with a cardboard box.

U4 **UI/UX** — "kurumsal hiciv" art direction (manila folders, sticky notes, rubber stamps, corporate memo
typography, warm paper + office-green accents). Screens: main menu, lobby (3–6 slots, bots, mode), briefing (manager
card, role, objective, cards), HUD, phone (Görev / Kartlar / Rehber+Arama / Gelen Kutusu / Notlar / İK), meeting
(projector slides, footage), results (termination letter + stamp + reveal + week table), settings (mic, PTT/open
mic, volumes, keys), how-to-play. 2D assets generated by Codex into `Art/UI/`.

U5 **Voice** — Opus (Concentus, pure C#) 16 kHz mono 20 ms frames, client → host → recipients over NGO unreliable
sequenced messages; host routes per frame: office = proximity (full ≤3 m, silent ≥14 m, wall-muffled), call = 1:1
clear (+ your physical voice still leaks to people near you = eavesdropping), meeting/results/lobby = everyone.
Push-to-talk (V) default, open mic with VAD option, per-player volume, speaking indicators.

U6 **Presentation** — lighting, URP post (bloom, grading, vignette), VFX (spill, sparks, blackout + emergency
lights, "no signal" screens), audio (ambience, ringtones, footsteps).

U7 **Bots** use every new verb so automated 4- and 6-player matches exercise them.

## 6. Ownership (strict; ask the lead before touching anything else)

| Area | Owner | Paths |
|---|---|---|
| Plan, contracts, integration, builds, verification | Claude lead | `docs/V2_*`, `Core/Contracts/**`, `Scripts/Net/**`, `Scripts/Boot/**`, merges |
| Rules engine v2 + tests | Codex (worktree `codex/engine-v2`) | `Core/Engine/**`, `Tests/Core/**`, `Tools/CoreTests/**`, `docs/CODEX_NOTES_V2.md` |
| 2D UI art | Codex (art run) | `Art/UI/**` only |
| Voice | Claude sub-agent `voice` | `Scripts/Voice/**`, `Game/Assets/Plugins/Concentus/**` |
| Characters & animation | Claude sub-agent `characters` | `Art/Scripts/build_characters.py` (+ new character scripts), character exports, `Editor/ProjectBuilder.Characters.cs` |
| Map, props, lighting | Claude sub-agent `map` | `Art/Scripts/build_props.py`, `build_architecture.py`, `Editor/ProjectBuilder.Scene.cs`, `Editor/ProjectBuilder.Prefabs.cs`, `Scripts/World/**`, numeric positions inside `DefaultOffice` |
| UI/UX | Claude sub-agent `ui` | `Scripts/UI/**`, `Game/UI/**`, `StreamingAssets/Localization/**` |
| Player feel | Claude sub-agent `feel` (later) | `Scripts/Player/**`, `Scripts/Audio/**` |

Rules for every agent:
- Work in your own git worktree/branch under `.worktrees/`; commit small, compiling steps; never push; never
  rewrite history; never touch files you do not own (write requests into your notes file instead).
- Unity is **not** opened by agents (one editor at a time, owned by the lead). Check compilation with
  `python -I Tools/scripts/compile_check.py` (Unity-free, ~15 s) and rules with `dotnet test` in
  `Tools/CoreTests/Tests`.
- C# 9 / netstandard2.1 rules as in `docs/CODEX_PACKAGE.md`; no per-frame allocations in hot paths; pool objects.
- `.meta` files: do not hand-write them for scripts; for imported binary assets the lead's Unity run generates them.

## 7. Schedule (local time)

| When | Lead | Codex | Sub-agents |
|---|---|---|---|
| 01:00–02:00 | plan, compile-check tool | UI art smoke test → art kit | voice, characters start |
| 02:00–03:00 | contracts v2 | engine v2 starts (stages 1–7) | map, ui start |
| 03:00–06:00 | N-player networking, call/voice plumbing, merges, builds | engine stages land | art, map, ui, voice iterate |
| 06:00–08:00 | full integration, 4- and 6-player bot matches, RAM pass | cross-review of Claude code | feel, fixes, Claude review of Codex code |
| 08:00–09:30 | package, report | — | — |

## 8. Performance & memory budgets (to be measured, reported in `docs/PERFORMANCE.md`)

- Client process working set ≤ 600 MB, host ≤ 650 MB (6 players), measured in a bot match.
- Engine `Tick` < 0.2 ms, 6× `BuildView` < 0.3 ms per batch (host).
- Voice: ≤ 24 kbps per active speaker; host upload ≤ 6 × 5 × 24 kbps worst case; zero managed allocations per
  frame in capture/encode/decode (pooled buffers).
- No per-frame GC allocations in HUD/world updates; textures compressed (BC7/DXT), max 2048, UI atlases;
  audio Vorbis, long clips streamed; incremental GC on.
