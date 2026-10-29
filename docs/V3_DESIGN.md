# v3 "Ofis Hattı" — design (normative)

Producer decisions (2026-10-29, after the first look at v2): the game was too busy and the lying never took centre
stage. v3 separates the players: **everyone sits alone in their own office (first-person, seated), the only channel is
the desk phone, and the week ends with one firing.** Pitch: *"Rooms like Scam Line, a table like Liar's Bar, and a
grumpy manager as the judge."* v2 stays at tag `v2.3-night` on `main`; v3 lives on branch `v3`.

Pillars
1. **Talking is the game.** No system knows the truth better than the players; information exists in people's heads
   and on their private screens. Every round is won or lost on the phone.
2. **One thing at a time.** Each round has a one-sentence rule. No real-time chores, no hold-to-interact, no stealth.
3. **Nobody waits.** All players play every day until Friday; the firing happens only on the last day.
4. **Clip moments.** Phone calls may be recorded; the manager plays a real 7-second snippet in the meeting.

## 0. v3.1 changes (design review, 2026-10-29) — these supersede the sections below where they differ

1. **Start blame 5** for everyone each week (floor stays 0); the strict session-points leader starts at 7.
2. **One İhtar per player per week.** If the plurality lands on someone who already has one, nobody gets it.
   **İhtar ties → nobody** (no blame tie-break).
3. **Final = shortlist.** Top 2 by blame at 3–4 players, top 3 at 5–6 (ties: more İhtars, then seed). Only they
   defend, 30 s each, highest blame first. Everyone (shortlisted too) votes for one shortlisted name (not yourself);
   most votes is fired; ties → higher blame → more İhtars → seed. No fire-score formula.
4. **Round length by head count:** RoundPlay 100 / 120 / 135 / 150 s for 3 / 4 / 5 / 6 players.
5. **Daily pairing:** every day = one *deal* round (Bonus Pool or Shared Password) + one *vote* round (Odd Email,
   Lost Report, Promotion List, Complaint Box). Day 1 of a session = Bonus Pool + Odd Email. Complaint Box only with
   ≥ 5 players; Lost Report only with ≥ 4.
6. **Hidden roles** (odd one, culprit, saboteur) are dealt without repeats inside a week and, across the session,
   to the players who had the fewest.
7. **Bonus Pool:** taker vs sharer: taker −4 (Paragöz −5), sharer +2. Odd head count: one **trio** instead of the
   intern — nobody takes −1 each; one taker −4 and the sharers +2; two takers −2 each and the sharer +2; all take +1.
8. **Lost Report slips:** each slip names a random innocent other than the holder, dealt so that exactly 1 (4–5
   players) or 2 (6 players) innocents appear on no slip; the culprit's slip names a random innocent and the culprit
   also sees who has no alibi. The honest pool narrows to the culprit + 1–2 alibi-less innocents → a voice read.
9. **Shared Password:** each player sees their own digit and their right-hand colleague's digit (position owners
   public). No live attempts: everyone enters one code before time runs out; **cracked if more than half of the
   players entered the exact code.** Payoffs unchanged.
10. **Odd Email topics** come in categories of 4 (6 categories): the majority's text and the 4 guess options are the
    same category; the odd one's text is from a different category (it is no clue).
11. **Dirty tricks:** once per week (Short Week) / twice (Full Week), any day after unlock. **Kaydı Sil is retired**
    (it deleted the recording moment); duplicates allowed at 6 players. **Manager line:** once per week, unlocked on
    day 3 of the first week; a false tip costs the tipper +2 and **the false tipper is named**; a true tip stays
    anonymous. Genuine mail: 1 per day.
12. **Missed calls:** a busy (or dead-line) callee gets a "missed call from X" notice.
13. **Hidden agenda:** each player gets an independent random target (never themselves, at most 2 players share a
    target); +1 point per İhtar the target receives, +2 if the target is fired.
14. **Employee of the Week** only for a strict lowest blame (ties → nobody).
15. **Next manager:** last week's fired player picks next week's manager during WeekResults.
16. **Recordings:** calls between today's Bonus Pool partners or involving a round's hidden player weigh ×3.

## 1. Session, week, day

- Players 3–6 (bots fill seats for tests). One **week** = `DayCount` days: Short Week 3 (Mon–Wed), Full Week 5.
  A session is any number of weeks; week points add up on a session board.
- Week start: random manager profile (not the previous one), each player gets a **hidden agenda** (a target colleague,
  derangement: everyone is somebody's target exactly once) and one **dirty trick** type (distinct per player).
- Day phases (default seconds):

| Phase | Seconds | Voice | What happens |
|---|---|---|---|
| `Morning` | 20 (first day of the session: 40) | calls | Manager's morning call: today's rounds, today's favourite (Kayırmacı). |
| `RoundIntro` | 7 (type never seen this session: 16) | calls | Rule card of the next round. |
| `RoundPlay` | 150 | calls | Private info on your computer, phone calls, submit your choice. When every connected player has submitted, the remaining time drops to at most 10 s. |
| `RoundReveal` | 12 | calls | Outcome and blame changes, animated. |
| `MeetingDossier` | 25 + recordings | all hear | Everyone is seated in the meeting room. The manager presents the day: round outcomes, trick traces, tip results, **call recording(s)**. |
| `MeetingDiscussion` | 75 | all hear | Free talk. |
| `MeetingVote` | 25 | all hear | Vote who gets today's **İhtar** (written warning). Not yourself; abstaining allowed. |
| `MeetingVerdict` | 8 | all hear | İhtar stamp. |

  `RoundsPerDay` = 2 (RoundIntro → RoundPlay → RoundReveal twice), then the meeting.
- **Last day** replaces the daily meeting with the final: `MeetingDossier` → `MeetingDiscussion` (60) →
  `FinalDefense` (20 s per player, highest blame first; only the defender's mic is open) → `FinalVote` (30) →
  `FinalVerdict` (12) → `WeekResults` (45, host can continue earlier).
- One day ≈ 8.5 min; Short Week ≈ 30 min; Full Week ≈ 50 min. Launch arg `-durationscale` scales every phase.

## 2. Blame, warnings, firing, points

- **Kusur** (blame, EN "Blame") is a public integer per player, never below 0. Rounds, warnings, caught tricks and
  false tips change it.
- **İhtar** (EN "Warning"): daily meeting plurality (ties → higher blame among the tied; still tied → nobody).
  The receiver gets `IhtarBlame` (profile, default 3) and an İhtar mark.
- **Firing** (last day): `FireScore = Blame + FinalVoteWeight × final votes`. Highest is fired. Ties: more İhtars →
  higher blame → seeded random.
- **Week points:** fired 0; every survivor +3; hidden agenda target fired +2; lowest blame of the week ("Haftanın
  Elemanı", ties all) +1.

## 3. Rounds (each with a one-sentence rule)

A day draws `RoundsPerDay` types from a shuffled deck without repeats inside a day, preferring types seen least this
session. `KayipRapor` needs ≥ 4 players. The first day of a session is fixed: `PrimHavuzu`, then `IhbarKutusu`.
Unsubmitted choices take the default. Votes and recommendations can never target yourself.

| Type | TR / EN | One-sentence rule | Private info | Choice (default) | Outcome |
|---|---|---|---|---|---|
| `PrimHavuzu` | Prim Havuzu / Bonus Pool | "You're paired up: secretly Share or Take It All." | your partner (odd count: one player is paired with the NPC intern, who shares 60 %) | Share / Take (Share) | Share/Share −1 each; Take/Share taker −2 (Paragöz −3), sharer +2; Take/Take +1 each. All pairs and choices are revealed. |
| `FarkliEposta` | Farklı E-posta / Odd Email | "Everyone got the same email — except one. Find them; if it's you, blend in." | majority: topic A; the odd one: topic B and "you are the odd one" | vote a player; the odd one also guesses topic A among 4 options (none) | Plurality on the odd one: odd +3 (+1 if their guess was right), each correct voter −1. Otherwise odd −2, plurality innocent (if any) +2. Odd one, both topics and all votes revealed. |
| `KayipRapor` | Kayıp Rapor / Lost Report | "Someone lost the report. Everyone holds one true 'X is innocent' slip — the culprit lies." | culprit flag; clue "X is innocent" (random cycle over all players; the player before the culprit skips them; the culprit's own clue is true) | vote a player (none) | Plurality on culprit: culprit +3, correct voters −1. Otherwise culprit −2, plurality innocent +2. Culprit, clues and votes revealed. ≥ 4 players. |
| `IhbarKutusu` | İhbar Kutusu / Complaint Box | "File one anonymous complaint." | – | target + reason 0..5, or abstain (abstain) | Most complaints (all tied at max, if ≥ 1) +2; if any complaint was filed, everyone with none −1. Counts and reasons revealed, authors hidden. |
| `TerfiListesi` | Terfi Listesi / Promotion List | "Recommend one colleague for promotion — publicly." | – | target or abstain (abstain) | Plurality winner (ties → nobody) −3; each recommender of the winner −1. Every recommendation revealed with its author. |
| `OrtakSifre` | Ortak Şifre / Shared Password | "Each of you holds one digit of the server password. One of you is lying." | your position + digit (positions → players public); saboteur flag | up to 2 attempts per player of the full code (–) | Any fully correct attempt ends the round: everyone −1 except the saboteur +2. Time out: everyone +1 except the saboteur −2. Every attempt and its count of correct digits is public. |

## 4. Phone

- One call at a time per player. `Call(target)` → callee rings for 15 s → `Answer` / `Decline` / time out.
  `HangUp` from either side. Busy callee: immediate "busy" result. Calls are allowed in Morning, RoundIntro,
  RoundPlay and RoundReveal; meetings end every call.
- Odd player out is never stuck: the **manager line** is always available (no voice): `TipOff(target)` once per day
  (from day 2 of the first week): "X used a dirty trick today." Resolved at the meeting: true → X's trick trace is
  named and X +2 blame; false → the tipper +1 blame. The tipper is never named.
- Every connected call is logged (`CallRecord`: id, caller, callee, start, end). Each player sees their own log.

## 5. Dirty tricks (one type per player per week, once per day, from day 2 of the first week)

| Type | TR / EN | Effect | Trace (meeting dossier) |
|---|---|---|---|
| `SahteEposta` | Sahte E-posta / Fake Email | Pick an apparent sender (anyone but you) and one of 8 templates; every other player gets the mail. | "A fake email went out today: <subject>". |
| `KahveDok` | Kahve Dök / Coffee Spill | Target's computer unusable for 40 s (can't read the round, can't submit). | "<victim>'s keyboard got coffee." |
| `FisCek` | Fişi Çek / Pull the Plug | Target's phone dead for 45 s (current call drops). | "<victim>'s phone line was cut." |
| `InternetKes` | İnterneti Kes / Kill the Wi-Fi | Everyone else's computer offline for 30 s (no submits, no new mail). | "The office internet went down." |
| `HatDinle` | Hattı Dinle / Wiretap | For 60 s you silently hear every connected call of the target. | Only if named. |
| `KayitSil` | Kaydı Sil / Erase Tape | Your calls of today are never played in a meeting. | "A recording was erased." |

Each use is **named** (the culprit printed in the dossier, +`TrickCaughtBlame`, default 2) with the profile's
`TraceChance` (default 0.35), or by a true tip-off.

## 6. Recordings ("Bu görüşme kalite standartları gereği kaydedilmektedir")

- Host-only, in memory, cleared after each meeting; never written to disk. Host toggle in the lobby (default on).
- At `MeetingDossier` the engine picks `Recordings` calls (profile, default 1; Paranoyak 2) among today's calls of at
  least 6 s whose participants did not erase their tape, weighted by duration, each with up to 3 fallback candidates.
  The voice system plays the most speech-dense 7 s of the first candidate that has speech, to everyone, with a
  tape/phone filter. The UI shows "<caller> → <callee>, 14:32" and a waveform.

## 7. Managers (data in `LineConfig.Profiles`)

| Id | Name TR / EN | Personality | Rule changes |
|---|---|---|---|
| `paragoz` | Hasan Bey / Mr. Harris | money first | Takers get an extra −1; İhtar 2; final vote weight 1. |
| `disiplinci` | Nermin Hanım / Ms. Norris | rules first | Trace chance 0.6; caught trick +3; İhtar 4. |
| `kayirmaci` | Selin Hanım / Ms. Sterling | has a favourite | Each morning names a favourite (not the blame leader): every blame gain of the favourite is reduced by 1. |
| `paranoyak` | Kemal Bey / Mr. Kemp | hears everything | Two recordings per meeting; complaint-author leak chance 0.6 (others 0.25). |

## 8. Onboarding (first week of a session)

Day 1 teaches one thing at a time: the manager calls you (answer the phone) → look at the computer (round card) →
call your partner → submit → meeting (everyone hears everyone) → vote. Day 2 unlocks dirty tricks and the manager
line, each with a one-sentence card. Every round type shows its rule card for 16 s the first time it appears in the
session. A sticky note on the monitor lists the next step.

## 9. Screens and world (first person, seated)

- **Your office** (one room built once, personalised for the local player: colour, nameplate, decor): desk, monitor,
  desk phone, mug, window. Mouse-look within limits; click the monitor → computer; click the phone (or press the
  phone key) → phone. Ringing phone shakes and blinks; coffee/offline/dead-line states are visible on the props.
- **Computer apps:** Görev/Task (current round), E-posta/Mail, Sicil/Record (blame table, İhtars, favourite),
  Kirli İş/Dirty Trick, Ajanda/Agenda (week calendar + your hidden target).
- **Meeting room:** everyone seated around one table (characters with talk animation), the manager at the head,
  projector with the dossier, recording waveform, voting by clicking a colleague. Final: spotlight on the defender.
- **Languages:** Turkish and English from the first build of v3; every new string exists in both. Manager and NPC
  names are localised.
- **Motion:** every screen enters/leaves with motion; stamps slam, numbers count, the phone rings physically.

## 10. Vocabulary (TR / EN)

Kusur / Blame · İhtar / Warning · Kovuldu / Fired · Gizli Ajanda / Hidden Agenda · Kirli İş / Dirty Trick ·
Müdür Hattı / Manager Line · İhbar / Tip-off · Haftanın Elemanı / Employee of the Week · Kısa Hafta / Short Week ·
Tam Hafta / Full Week · Görüşme kaydı / Call recording · Stajyer / Intern · Toplantı / Meeting · Savunma / Defense.
