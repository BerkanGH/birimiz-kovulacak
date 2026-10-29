> **This document describes v2** (open office, third-person, `MatchEngine`), which lives on branch `main` (tag `v2.3-night`). The `v3` branch no longer contains the v2 code.

# Contracts v2 — normative semantics (Protocol.Version 2)

Extends `docs/CONTRACTS.md` (still normative where not overridden) and `docs/DECISIONS.md`. Product context:
`docs/V2_PLAN.md`. Types: `Game/Assets/Game/Core/Contracts/*.cs` (owner: Claude lead). Everything marked v2 in the
contract files is specified here. When this file and the code comments disagree, this file wins; report it.

## 1. Players (3–6)

- `MatchSetup.Players.Count` ∈ [`Protocol.MinPlayers` 3, `Protocol.MaxPlayers` 6]. Ids are distinct values in
  0..5 (lobby slots; **gaps allowed**, e.g. {0,1,3,4}). Size per-player arrays by `MaxPlayers`; absent ids are
  ignored everywhere; always iterate present players in ascending id order. Views list only present players.
- Desks: the k-th present player (ascending id) gets desk k (or a seeded shuffle of desks 0..N-1 when
  `ShuffleDesks`). Desks N..5 are **unassigned**: `AssignedOwner = None`, never inspected (the manager walks past),
  never mail targets, cannot be victims of desk-targeted sabotage that needs an owner (FakeEmail, PullPlug,
  LockWorkstation, TipManager → `InvalidTarget`), but their slots still accept items.
- Nameplates exist in the layout for all six ids; only present players' nameplates are spawned (on their desk);
  the others are not part of the match (not in views).
- Exactly one Informant among present players. Objectives: N−1 employees get distinct cards; targets are present
  players ≠ holder. Mugs: layout has 6.
- Constructor throws `ArgumentException` for invalid setups (count, duplicate/invalid ids, too few desks).
- New engine API (host-only, never serialized): `int CallPartnerOf(int player)` (active-call partner or −1) and
  `IReadOnlyList<int> Players` (present ids ascending) — the network adapter uses them for voice routing.

## 2. Features per match

`PlayerView.Features` = config switches (`Cards.Enabled`, `Calls.Enabled`, `Calls.TipLineEnabled`,
`Footage.Enabled`, `Tasks.MailEnabled`) AND, in `GameMode.Week`, the flags of `config.WeekDays[setup.WeekDay]`.
`CareerMultiplier` = day multiplier in week mode, 1 otherwise. A verb whose feature is off fails with
`FeatureDisabled`.

## 3. Manager profiles

- Selection: `setup.ManagerProfileId` if non-empty; else in week mode `WeekDays[WeekDay].ProfileId` if non-empty;
  else a seeded draw among `config.ManagerProfiles` excluding the last entry of `History.PastManagerProfiles`
  (when more than one exists). No profiles configured → behave as v1 (`ManagerProfileId = ""`).
- Apply on a private working copy of the config: `QuotaTarget += QuotaDelta`; verdict weights / favour weight /
  vision range / half-angle replaced when the override is ≥ 0; observation severity × `ObservationFactors[kind]`
  (absent = 1, 0 = the observation is still recorded with severity 0); evidence `RiskWeight` × `EvidenceFactor`;
  InappropriateEmail severity × `EmailFactor`.
- **Favour** (`PlayerPublicView.Favor`, public): completing an action matching `FavorSource` adds
  `FavorPerAction` to the completer (ReportsDelivered: report delivery; CoffeeDelivered: coffee delivery
  (not salted); AnyTask: any task completion). `HasFavourite`: at construction pick a present player (seeded, or
  `ForceFavourite`) → `PlayerView.Favourite`, starting favour 2. Emit public `FavorChanged`.
- **Verdict favour discount:** the present player(s) with the highest **positive** favour get
  `Score -= FavorWeight` if they are candidates (`CandidateScore.Favor/FavorDiscount` filled for every candidate).
  Tie-breaks unchanged. `MatchResultView.Favourites` lists them.
- **Surprise inspection** (`SurpriseInspection`): at `Work2Start + SurpriseAtFraction × Work2` the manager walks the
  route with **no warning** (`ManagerView.Surprise = true`, public event `SurpriseInspection`) and observes exactly as
  in a raid, until he is home or `SurpriseSeconds` elapsed or Work2 ends (whichever first; truncate, never extend).
- **Drawers:** raid and surprise stops do **not** open drawers (surface, nameplate, mess, people only) unless the desk
  was tipped (§7) or the profile has `SearchAllDrawers`. The final inspection opens every assigned desk's drawer.
  (This makes "plant in a drawer + tip the manager" the signature combo.)

## 4. Secret sabotage cards

- Pool = `SabotageDef`s with `Basic == false` (never EvidenceTampering). If `Features.Cards`, every present player
  (Informant included) is dealt `CardsPerPlayer` **distinct** types from the pool with the match RNG,
  independently per player (two players may hold the same card). `ForceCards` overrides (test hook). Without cards
  enabled nobody holds cards and card verbs fail `FeatureDisabled`.
- A card verb by a non-holder fails `MissingCard`. Cards are private (`Me.Cards`) until Results
  (`PlayerOutcome.Cards`). Cards are not consumed; cooldowns/limits still apply.
- `MaxPerMatch` > 0 caps successful completions of that type office-wide → `LimitReached`.
- `PrivateView.SabotageCooldowns` has one entry per `SabotageType` value (12).

## 5. New sabotage and counter-verbs

Common rules from v1 apply (proximity, timed interaction, reservation/busy, actor + same-victim cooldowns, per-desk
and per-actor limits, incident ledger with immutable true actor, witness notes with neutral activity, reversal
credit to a non-actor who neutralises it, misconduct base + bounded consequence once).

| Verb | Target / needs | Time | Public activity | Effect |
|---|---|---|---|---|
| `LockScreen` | own `desk.N.pc`, screen open | `Email.LockScreenSeconds` | Typing | screen session closed |
| `SendFakeEmail` (card FakeEmail) | another **assigned** desk's pc; `ScreenOpen`, not unplugged, not password-locked, internet up; `IntParam` ∈ [0, TemplateCount) | def duration (4 s) | Typing | see below |
| `PullPlug` (card) | another desk `desk.N`, plugged | 2 s | Plugging | `Unplugged`, screen closed, owner's running WriteReport cancelled (consequence) |
| `PlugIn` | `desk.N` unplugged; anyone | `ReverseSeconds` 2 s | Plugging | plugged; reversal credit |
| `SaltCoffee` (card) | `st.coffee`, carrying a **Full** mug | 2 s | Brewing | mug secretly salted (host-only) |
| `TasteCoffee` | carrying a Full mug | `Tasks.TasteSeconds` | Drinking | mug → Used, salt removed, private `TasteResult` note + `CoffeeTasted` event; tasting a salted mug neutralises the incident (reversal credit if not the salter) |
| `CutInternet` (card) | `st.router`, internet up | 4 s | ServerWork | internet down `EffectSeconds` (40) |
| `RestartRouter` | `st.router`, internet down; anyone | `ReverseSeconds` 6 s | ServerWork | internet restored; reversal credit |
| `CutPower` (card) | `st.fusebox`, power on | 3 s | FuseWork | power down `EffectSeconds` (25) |
| `ResetFuse` | `st.fusebox`, power down; anyone | `ReverseSeconds` 4 s | FuseWork | power restored; reversal credit |

- **Screen sessions:** a pc's `ScreenOpen` becomes true with `ScreenOpenUntil = now + ScreenOpenSeconds` when its
  assigned owner completes `WriteReport` there; it closes on expiry, `LockScreen`, PullPlug or a password lock
  (LockWorkstation). Screens start closed.
- **Fake email completion:** append `EmailView` (`FromPlayer` = the pc's assigned owner, never the actor) to
  `PlayerView.Inbox`; public `EmailSent`; System evidence `EmailLog` (attributed to FromPlayer, `LocationId` = pc
  id, `EmailTemplate`, weight `RiskWeights.EmailLog`×EvidenceFactor); the manager reads it immediately → observation
  `InappropriateEmail` on FromPlayer (severity `RiskWeights.InappropriateEmail` × EmailFactor × factor). Incident
  victim = FromPlayer; consequence +1 if FromPlayer is fired.
- **Pull plug:** while unplugged, WriteReport / SendFakeEmail / LockWorkstation / UnlockWorkstation / LockScreen on
  that pc fail `PcUnplugged`. Public `PlugPulled` / `PlugRestored` (+ log entries, no actor on pull).
- **Salt:** the coffee machine is busy while salting (like brewing). Delivering a salted mug: the delivery completes
  (mug to the tray as Used) but awards **no** quota/performance/favour; observation `SaltyCoffee` on the
  deliverer; incident consequence +1 if the deliverer ≠ salter. `ItemView.KnownSalted` is true only in the views of
  the salter and of a taster of that mug, and only while it is salted. `HostDebugView.SaltedItems` lists salted mugs.
- **Internet outage:** `OfficeStatusView.InternetDown/InternetBackAt`; WriteReport cannot start (`NoInternet`) and a
  running WriteReport is cancelled at outage start; the printer pauses (the current job keeps its remaining time);
  SendFakeEmail fails `NoInternet`. Auto-restore at `InternetBackAt`. Office-wide incident (victim None, desk −1),
  no consequence. Public `InternetDown` / `InternetRestored` (Actor = restarter, None on timeout) + log entries.
- **Power cut:** `PowerDown/PowerBackAt`; witness range = min(Range, `Witness.DarkRange`); manager vision range =
  min(range, `Manager.DarkVisionRange`); position-log samples taken during it are flagged dark (§9). Public
  `PowerDown` / `PowerRestored` + log entries. Office-wide incident, no consequence.
- Both outages end at the meeting start at the latest.

## 6. Calls

- Allowed phases: Briefing, Work1, Raid, Work2, FinalInspection. Requires `Features.Calls`.
- `CallPlayer(IntParam = callee)`: callee present, connected, ≠ caller; neither party ringing/in a call
  (`InCall` for the caller, `Unavailable` for a busy/disconnected callee); caller's `CallCooldownUntil` passed
  (`Cooldown`). Instant: caller `RingingOut`, callee `RingingIn` (`Until = now + RingSeconds`), callee
  `PlayerPublicView.Ringing = true`, public `IncomingCall` (Actor = callee; caller hidden). Append a call record.
- `AnswerCall` (callee while RingingIn): both `Active`, `Since = now`, `Until = now + MaxCallSeconds`; record
  `Answered`; private `CallStarted` to both. `DeclineCall` (callee rejects or caller cancels) / ring timeout: both
  `None`. `HangUp` (either party, Active) / max length / either party disconnects / phase leaves FinalInspection:
  call ends. Every end: record `End`, private `CallEnded` to both (IntArg = answered), both parties get
  `CallCooldownUntil = now + CooldownSeconds`.
- Calls do not block movement or other interactions. Public activity shows `OnCall` for a player who is ringing out
  or in an active call **and** has no other active interaction (an interaction's activity takes precedence).
- Manager: during raid / surprise / final inspection, a player in an **Active** call inside the vision cone with
  line of sight gets observation `PersonalCall` once per inspection.
- Privacy: who called whom never appears in another player's view or event (only `Ringing`/`IncomingCall` for the
  callee and `OnCall` activity). `CallPartnerOf` is host-only.
- **Tip line** `TipManager(TargetId = desk.N)`: assigned desk ≠ own (`CannotTargetSelf`), `Features.TipLine`,
  `TipsLeft` > 0 (`TipsPerPlayer`, `NoCharges`), not in a call; Work1..FinalInspection; duration `TipSeconds`,
  activity OnCall. On completion: record `{Caller, Callee None, Tip, TipDesk}`; public `ManagerTipReceived` (no
  details); queue the tip. At the start of the next inspection walk (raid, surprise or final) the manager visits the
  tipped desk(s) first (walk straight there after leaving his office, skip it later in the route), dwells
  `+TipDwellBonus` and opens its drawer. Finds → normal evidence + observation `TipFound` (severity 0, for the feed);
  nothing incriminating (no suspicious item, no hidden report, not messy) → observation `TipClean` (positive,
  `RiskWeights.TipClean`) on the desk owner. Tips made after the final inspection started are recorded but have no
  effect.
- Informant `PullCallLogs`: charges `Informant.CallLogCharges` (`NoCharges`, non-Informant `NotAllowed`), phases
  Briefing..MeetingDefense2, instant → private note `CallLog` with a deep copy of all call records so far (tips show
  the tipster). Presentable (`PresentNote`) → public evidence `CallLogStatement` with `Calls`.

## 7. Mail task

If `Features.Mail`: mail items `item.mail.<serial>` (kind Mail, variant `mail`) appear in `slot.mailroom` with
`AddressedDesk` = a seeded random **assigned** desk: `InitialMail` at the briefing, then every `MailInterval`
during Work1/Work2 while open mail < `MaxOpenMail` (open = in the mail room or carried). Public `MailArrived`.
Placing a mail item on its addressee desk's surface (place-surface duration) completes `TaskType.Mail` (quota,
performance, task count, favour per source) and archives the item; elsewhere it is an ordinary placement.
`QuotaView.OpenMail`. OfficeAllRounder now means ≥1 completion of 4 distinct task types (any of the 5).

## 8. Crouch, emotes

- `AvatarSample.Crouched` → `PlayerPublicView.Crouched`. A witness notices a crouched actor only within
  `Witness.Range × CrouchRangeFactor` (and the dark range during a power cut, whichever is smaller).
- `Emote(IntParam = EmoteId)`: any phase except Abandoned, instant, not physical (allowed while busy);
  `Emote/EmoteUntil = now + 2.5 s`; public `Emote` event; while an emote is active a new one fails `Cooldown`.

## 9. CCTV footage

- Host-only position log: every `Footage.SampleInterval` of host time from the briefing until the meeting starts,
  for each present connected player: time, position, public activity, carried item variant, crouched, `dark` (power
  down). Bounded (≈ 6 × 400 samples), stored compactly; never sent to clients.
- `RequestFootage(TargetId = camera id, IntParam = second of the match)`; phases MeetingDiscussion,
  MeetingDefense1, MeetingDefense2; requires `Features.Footage`. Eligible: (a) the current defense speaker during
  their own window, `Footage.SpeakerRequests + profile.ExtraFootageRequests` per match; (b) the Informant,
  `Informant.FootageCharges` per match, in any of those phases. Otherwise `NotSpeaker` (or `NoCharges` when the
  allowance is used up). Unknown camera → `InvalidTarget`. Absolute time t = match start + IntParam must be within
  [match start, dossier freeze] (`InvalidTarget`).
- Result: public evidence `CctvFootage` (Source Player, SourcePlayer = requester, RiskWeight 0, `CameraId`,
  `WindowStart/End` = t ± `WindowSeconds`) with `Sightings` = every non-dark sample in the window whose position is
  inside the camera zone (ascending time, then player id). If every sample in the window is dark → `NoRecording`.
  Added to `MeetingView.PresentedEvidence`; public `FootageShown`.
- `PrivateView.FootageLeft` = what the recipient could still use right now.

## 10. Week mode and career

- `setup.Mode == Week`: `PlayerView.Mode/WeekDay`; `PlayerPublicView.CareerPoints` = history total.
- At Results (only when not abandoned) per present player:
  `delta = round(CareerMultiplier × (companySucceeded && !fired ? QuotaMet : 0) + (!fired ? Survived : 0) +
  (objective won ? ObjectiveWon : 0) + (Informant won ? InformantWon : 0) + (fired ? Fired : 0) +
  (in Favourites ? Favourite : 0))` → `PlayerOutcome.CareerDelta/CareerTotal`. `WeekFinished` = this was the last
  configured day; then `EmployeeOfTheMonth` = present players with the highest total (ties).
- `BuildNextHistory`: `NextWeekDay` = WeekDay + 1, or 0 with `WeekNumber + 1` and career points reset after the
  last day; `CareerPoints` updated; `PastManagerProfiles` appended. Abandoned matches change no career data and keep
  the same day. Quick play: no career data (deltas 0), day untouched.

## 11. Results, recap, titles, debug

- `MatchResultView`: `Mode`, `WeekDay`, `ManagerProfileId`, `Calls` (all records incl. tips), `Favourites`,
  `WeekFinished`, `EmployeeOfTheMonth`; `PlayerOutcome.Cards/Favor/CareerDelta/CareerTotal`; incidents include the
  new sabotage types (office-wide ones with Victim None). Recap keys added:
  `recap.fake_email, recap.pulled_plug, recap.salted, recap.salty_delivery, recap.internet_down, recap.power_down,
  recap.tip, recap.tip_clean, recap.call` (Actor/Other as natural). Titles may add
  `title.scam_artist` (most calls placed, ≥3), `title.salt_bae` (salted a coffee someone else delivered),
  `title.it_hero` (restored internet or power), `title.favourite` (highest favour).
- `HostDebugView`: `ManagerProfileId`, `Cards`, `Calls`, `SaltedItems`.

## 11b. "Günün klibi" highlight (added after the first package)

- `FootageSighting.Position/Yaw` are filled for footage sightings (positions inside the camera zone only).
- `MatchResultView.Highlight` (null if no incident happened): pick the incident whose discovered evidence added the
  most risk to the **fired** player; if none, the incident with the highest misconduct; ties → earliest. Window =
  [incident start − 6 s, incident completion + 4 s] clamped to the logged range. `Samples` = every logged sample of
  every present player in the window (positions, yaw, activity, carried variant, crouched), ascending time then
  player id; dark samples are included (the replay is the truth reveal, not a camera). `Key` = the recap key of that
  incident, `Actor` = true actor, `DeskIndex` = target desk. Part of the result hash.

## 12. Privacy checklist (tests must cover)

True email sender, salt state (except salter/taster), call partners, tipster identity and tipped desk (until the
manager walks there), cards, the position log, and incident actors never appear in another recipient's view/events
before Results. Encode views and scan the bytes as in v1.

## 13. Wire format

`Protocol.Version = 2`. Every new field/type is encoded in declaration order with the v1 conventions; lists of
lists (`HostDebugView.Cards`) are host-only and need not be encoded. Hash covers all result fields.

## 14. Affordances (view-only)

New label keys: `verb.lock_screen, verb.send_fake_email` (IntParam −1: the UI opens a template picker),
`verb.pull_plug, verb.plug_in, verb.salt_coffee, verb.taste_coffee, verb.cut_internet, verb.restart_router,
verb.cut_power, verb.reset_fuse, verb.deliver_mail`. Card verbs appear only for card holders (non-holders: omitted,
not greyed). Calls, tip line, footage, call logs and emotes are phone/meeting UI actions, not world affordances.
