# UI

## v5 play UI (current, `feature/v5-integration`)

Rules: `docs/V5_ENGINE_SPEC.md` (normative) and `docs/V5_DESIGN.md` (Turkish, rationale). Everything below the
"History" line describes the v2–v4 UI, which no longer exists in code.

During a match the player is either **in their own office** (Intro, tasks, breaks, crisis calls: the diegetic office
layer of docs/V5_INTERACTION.md step 1) or **in the meeting room** (meetings, crisis vote, final, results: one UI Toolkit
screen built in code over the first-person room; step 3 will replace it). Shell screens (menu, lobby, settings,
how-to, first-launch flow) are the older shell code, updated for v5 where needed.

| File (under `Game/Assets/Game/`) | What |
|---|---|
| `Scripts/UI/Play/PlayUi.cs` | The match screen: header, players column, focus card, phase details, phone, transfers, events, messages. |
| `Scripts/UI/Play/PlayText.cs` | Typed data → sentences (`v5.*` keys): phases, tasks, dossier lines, events. |
| `Scripts/UI/Play/TapeDeck.cs` | Cassette deck shown while a leaked recording plays (compact inside the centre column). |
| `UI/Resources/ui/styles/play.uss` | Styles of the match screen (logical 1920×1080, scaled; same layout at 1600×900). |
| `StreamingAssets/Localization/tr.v5.json`, `en.v5.json` | Match strings (`v5.*`, `rec.*`). Turkish first; English mirrors it. |
| `Scripts/UI/SettingsScreen.cs` → `HowToPlayScreen` | "Nasıl oynanır": 11 v5 pages (`howto.N.*` in `tr.json`/`en.json`), numbers formatted from the host's `LineConfig` (else `GameConfig.Line`). `PageGoal`…`PageFinal` constants. |
| `Scripts/UI/ShellOverlays.cs` | First-launch flow (language, consent, 3 intro cards in `*.privacy.json`), moment marker. |
| `Scripts/UI/MenuScreens.cs`, `UIRoot.cs` | Menu, lobby, pause (how-to button in all three); layers, Esc routing, cursor. |
| `Scripts/UI/Dev/LineMock.cs` | UI mocks: hand-made `LineView`s pushed into `LineClient` (`-uimock "v5.*"`). |
| `Scripts/UI/Play/PlayKit.cs`, `PlayBlocks.cs` | Shared by the meeting screen and the office: play-styled buttons, live labels, the content-diffed `PlaySection`; the task card, rules, intro, transfer, messages and call-log blocks (no rule text is duplicated). |
| `Scripts/UI/Office/OfficeUi.cs` | Office layer: thin HUD strip, toasts, incoming-call card, call pill, use prompt, hotkeys; opens the desk objects and drives the office camera. |
| `Scripts/UI/Office/OfficeComputer.cs` | "KOVA OS" laid exactly over the monitor screen: apps Görev, Havale, Mesajlar, Arama geçmişi, Kurallar; sticky note with the "Şimdi" hint; taskbar. |
| `Scripts/UI/Office/OfficePhone.cs` | Desk phone face (left half; the 3D phone and the lifted handset stay visible): LCD, contacts = line board, call actions, missed calls. |
| `Scripts/UI/Office/CorkBoard.cs` | Suç tahtası: a UI Toolkit panel rendered into the cork board's texture (one pinned note per colleague). |
| `Scripts/UI/Office/OfficeObjects.cs` | Step 2 task objects: the object card (cash box, roster, drawer), their world states from the view, the prim watcher (drawer thunk + toast). |
| `Scripts/UI/Office/RosterSheet.cs`, `WorldPanel.cs` | The Weekend Shift roster drawn into the clipboard paper; the shared "UI panel → texture on a room surface" helper (also used by the cork board). |
| `Scripts/Rooms/OfficeRoom.Tasks.cs` | Step 2 world side: the cash box (lid, notes, the mitten that carries them, İhbar button, seal), the roster pencil (it writes on its own) and its string, the drawer's prim stack, the hands that slide away when you look aside. |
| `Scripts/UI/Office/OfficeText.cs`, `ScreenFade.cs` | "Şimdi: …" sentence and use prompts; the black fade of office ↔ meeting switches. |
| `Scripts/UI/Dev/OfficeMock.cs` | Office mocks (`-uimock "v5.office*"`). |
| `UI/Resources/ui/styles/office.uss` | Office layer, OS, phone and cork board styles. |

**Office (v5 step 1).** Cursor locked, mouse look; WASD stands up from the chair and walks (2 m/s, local only:
`Scripts/Rooms/OfficeWalker.cs` collides a 0.24 m circle with the walls and the furniture footprints that
`Art/Scripts/build_rooms.py` writes into `rooms_v3.json` → `walk`). The crosshair names what it is on within 2.4 m
("E — Bilgisayar · kısayol: Q"); E or a left click uses it. Hotkeys from anywhere: Q computer, Tab phone, R record,
Y / N answer / decline (N also hangs up), V push-to-talk, Esc puts down what you hold, then opens the menu. The computer
and the phone sit you down (the camera eases ~0.4 s); answering from across the room walks you back too (the cord
reaches 1.15 m). The only permanent element is the top strip: phase, timer, *Primin*, recorder tapes and one "Şimdi"
line (`OfficeText.Now`: the same decisions as the meeting screen's focus card, pointing at the desk object). Toasts
(right): texts, prim received, missed / declined / busy calls, the recorder, phase changes. The cassette recorder on the
desk (`RmRecorder`): REC key and lamp, turning reels, one blank tape per use left in the rack. The lamp over the door
lights up in the last 15 s before a meeting; switching rooms fades to black (`LineWorldDriver.ShownPlace`).

**Data flow.** `LineClient.View` (the player's `LineView`, docs/V5_ENGINE_SPEC.md §11), `LineClient.Config` (the host's
`LineConfig`; every number on screen comes from it), `LineClient.Messages`, and `EventReceived` (→ `PlayText.Event` →
event feed). Every action is `LineClient.Send(verb, …)`; the host validates it and a rejection shows as a toast.

**Layout.** Header: phase (task/meeting/firing round), timer, *Primin*, recorder/investigation, voice mode. Left: players
with suç, tags (SEN, BOT, ADAY, MÜHÜRLÜ, KOVULDU, TELEFONDA), "seçti/oy verdi" marks and shortcuts (Ara, İhbar et,
Soruştur, Prim gönder, Mesaj). Centre, top to bottom: **focus card** ("Şimdi ne yapıyorsun?"), the tape deck while a leak
plays, then secondary details (task rules, dossier, leaks, candidates, earlier firings, results details). Right: phone
and line board, transfer panel (sealed for candidates), events, private texts / meeting chat.

**Focus card per phase** (`PlayUi.BuildFocus`): one instruction sentence plus the phase's main action; orange border =
something still to do (no choice / empty ballot / witness / your defence turn), green = done. A *Kurallar* button opens
the how-to page for the phase.

| Phase | Card |
|---|---|
| Intro | goal and start prim |
| TaskPlay | task-specific instruction, group, the two choice buttons, current choice / default |
| TaskReveal, Break | group's choices as chips, my payout; Break after Petty Cash: whistle button (witness) or warning (sole taker) |
| MeetingDossier/Discussion | what to discuss, failed tasks, optional investigate / leak buttons |
| MeetingVote, CrisisVote, FinalVote | the ballots (one row per ballot), clear/abstain, "not voted yet" warning, live progress |
| MeetingVerdict, CrisisVerdict | blame handed out; candidates and the sealed-desk note |
| CrisisCalls, FinalDiscussion | what to bargain about; candidates; leak buttons |
| FinalDefense | defence order with a live countdown for the speaker |
| FinalVerdict | desk reveal: "X kovuldu", desk prim, decision (plurality / revote / tie-breaks), each voter's share, burned remainder |
| Results | winner, ranking table (prim, suç, tags), my place; details below in tabs (ballots grouped per vote, firings, choices, transfers, incidents) |

**Two rules that keep buttons clickable.** (1) A `Section` rebuilds its subtree on any change but swaps it in only when
the rendered content (types, classes, texts, enabled state) differs. (2) Time-driven or fast-changing texts (call
duration, ringing time, leak "N sn sonra", defence countdown, "oy verdi", vote progress, TELEFONDA) are **live labels**
(`PlayUi.Live`): refreshed in place at 2 Hz and counted in the diff only by their key. Put everything a live label's
closure depends on into its key, or let it read the current `LineClient.View`.

**Text rules.** Rich text is off (player names are plain). Turkish sentences avoid suffixes on names ("Soruşturma: X
açtı → Y almış", "X ile görüşme"). Avoid "çalındı/çalıyor" for recordings (also reads "stolen"): use "dinletildi".
Terms: prim, suç, ihbar, soruşturma, sızdır/sızıntı, aday, mühürlü, kovuldu.

**Mocks and checks.**

```
Builds\BirimizKovulacak-Windows\BirimizKovulacak.exe -uimock "v5.*" -uimockout Builds\mock\1600 -screen-fullscreen 0 -screen-width 1600 -screen-height 900
python -I Tools/scripts/loc_check.py           # v5 keys: parity, placeholders, enum families, keys used by UI/Play
python -I Tools/scripts/loc_parity_meeting.py  # shell + privacy areas, how-to placeholders
python -I Tools/scripts/compile_check.py --rsp
```

**Task objects (v5 step 2).** Each task has its own object (docs/V5_INTERACTION.md §2, §6.1): Client Bonus stays on the
computer; **Küçük Kasa** is the steel cash box that drops onto the desk when Petty Cash starts and leaves after its
break; **Hafta Sonu Nöbeti** is the clipboard on the desk wall right of the cork board (in reach from the chair). E on
an object (or the Görev app's one button, so nobody has to walk) eases the camera to it and opens a small card
(`OfficeObjects`): bottom for the box, left for the roster (the sheet sits right of centre), a one-line caption for the
drawer. The card's main action is also on E; Esc goes back.

| Object | While choosing | After the reveal / in the break |
|---|---|---|
| Cash box | Lid open, the notes (config `CashTake`, runtime clones of `RmBill`) lie in the box. "Al" → the right mitten carries them out onto the desk beside the box; "Geri koy" puts them back (also "Dokunma" before any choice). The box always shows your current choice; "Seçmezsen: dokunmazsın". | Your group's outcome: notes still in the box (nobody took), beside it (you were the only taker), gone (a group-mate took them, or a whistle took them back), or a red **EL KONULDU** seal (both took). A witness's **İhbar** button glows in the break; E on the box → a red confirm card naming the thief → `Whistle`; after it the button stays pressed. The sole taker gets a warning toast when the break starts. Other presses get a reason (deny sound). |
| Roster | "Gerekli: K kişi", one line per colleague; the pencil (on a string) writes your name in your ink (`Volunteer`) or turns round and rubs it out (`Skip`). Everyone else's line stays empty until the reveal. | The public roster in seat order (yours first) with a TAMAM / EKSİK stamp; it stays on the wall until the end of the match. |
| Drawer | Always: bundles of five (`RmBundle`, back row first so tall stacks never hide the front), the remainder as loose notes, a manila tag with the exact number. Prim coming in → the drawer jolts with a thunk and a toast "+N prim" (transfers and whistle rewards keep their own toast); prim going out → the stack shrinks. | Same. |

The Görev app no longer has Petty Cash / Weekend Shift / whistle buttons: the card says where the task is played and
offers "Kasa kutusuna bak" / "Nöbet çizelgesine bak" / "Kasa kutusuna git: İhbar". Art: `RmCashBox`, `RmRoster`,
`RmBill`, `RmBundle` and the open drawer tray in `Art/Scripts/room_props.py` (banknote texture `Bill.png` from
`room_textures.py Bill`), placed by `build_rooms.py` (the desk's mug and notepad moved a little to make room).

Office scenarios: `v5.office_desk` (strip, prompts on the monitor / recorder / board), `v5.office_task` (computer:
task, messages, transfer, call log, rules), `v5.office_ring` (incoming call card, prim toast, phone panel while ringing),
`v5.office_call` (recorded crisis call: phone panel, call pill, recorder), `v5.office_board`, `v5.office_board_tags`
(small notes, stamps), `v5.office_witness`,
`v5.office_shift`, `v5.office_standing` (standing views, door lamp), `v5.office_en`. Step 2: `v5.office_cashbox`
(prompt, open box, the notes in the mitten, on the desk, put back), `v5.office_cashbox_reveal` (only taker, a
group-mate took, both caught, nobody), `v5.office_cashbox_witness` (lit button, confirm card, after the whistle),
`v5.office_cashbox_thief` (warning toast, whistled on), `v5.office_roster` (prompt, empty sheet, writing, written,
rubbing out), `v5.office_roster_reveal` (full / short), `v5.office_drawer` (6 → 23 → +4 transfer → 0 → 58 prim, then
looking sharply right and left from the chair). The room itself: `-scenetest` (now also the recorder states,
standing, walking into the desk, the door lamp, the board zoom, and step 2: the cash box states and the mitten, the
roster pencil, the drawer stacks, the hands looking aside, nameplates "MAVİ / YEŞİL / IŞIK").

Scenarios: `v5.task`, `v5.task_shift`, `v5.reveal` (witness; all three open the office computer), `v5.meeting`, `v5.meeting_vote` (leak playing over the
ballots), `v5.crisis_vote`, `v5.crisis_verdict`, `v5.sealed` (I am a candidate), `v5.defense`, `v5.final` (second
firing vote), `v5.verdict`, `v5.verdict_tie`, `v5.results`, `v5.results_tabs`, `v5.howto` (all pages), `v5.en_task`,
`v5.en_verdict`. Automated matches (`Tools/run_local4.ps1 -Bots`) also save one screenshot per phase per slot under
`<run>/shots`.

---

# History: UI / UX v2 — "kurumsal hiciv"

> v2–v4 UI. The files below (Hud, PrivateScreens, MeetingScreens, ResultsScreen, UI/Desk, UI/Meeting, …) were deleted
> in v5; kept for the art direction and the record.


Owner: Claude sub-agent `ui` (branch `v2/ui`). Plan: `docs/V2_PLAN.md` §5 U4. Rules: `docs/CONTRACTS_V2.md`.
Everything is UI Toolkit built in code (no UXML), styled by `Game/UI/Game.uss`, all strings in
`StreamingAssets/Localization/tr.json`. Compiles with `python -I Tools/scripts/compile_check.py` (also `--player`).
**Not yet seen in Unity** — see "Needs a Unity run".

## Files (all under `Game/Assets/Game/`)

| File | What |
|---|---|
| `Scripts/UI/UIRoot.cs` | layers, visibility, Esc/Tab routing, cursor policy, `IFeelUi` (feel hooks) |
| `Scripts/UI/UiKit.cs` | component kit: palette, fonts, labels (Bold/Title/Hand/Memo/StampText), `Btn`/`IconBtn`, `Panel`, `Icon`, `Badge` (player portrait), `ManagerPortrait`, `KeyCap`, `Tag`, `Bar`, `Pips`, `Section`, `TabBar`, `Screen` base (fade/scale-in), cached clock/number strings, `KeepAspect` |
| `Scripts/UI/UiArt.cs` | optional art kit loader (Resources, cached incl. misses, `Release` for big one-screen textures), id mapping verbs/tasks/sabotage/activities/emotes → icons, fallback glyphs |
| `Scripts/UI/UiServices.cs` | `IVoiceUi` + `UiServices.Voice`, `UiSettings` (PlayerPrefs keys, `Changed` event), `UiInput` (Enter only) |
| `Scripts/UI/GameInfo.cs` | small read helpers (profile, phases, desks, footage range) |
| `Scripts/UI/Hud.cs` | office HUD + toast feed + call widget + emote wheel renderer |
| `Scripts/UI/PrivateScreens.cs` | briefing, phone (7 apps) |
| `Scripts/UI/MeetingScreens.cs` | HR meeting (projector slides, footage, people, verdict) |
| `Scripts/UI/ResultsScreen.cs` | results (letter + stamp, reveal tabs, clip, week board, Ayın Elemanı), `ScreenshotSaver`, `WeekBoard` |
| `Scripts/UI/FloorPlan.cs` | top-down CCTV floor plan, `FootageView` (meeting), `HighlightPlayer` ("Günün klibi") |
| `Scripts/UI/MenuScreens.cs` | main menu + join, lobby, status overlay, pause |
| `Scripts/UI/SettingsScreen.cs` | settings (audio, mic, controls, display/language) + `HowToPlayScreen` (6 illustrated pages) |
| `Scripts/UI/Modals.cs` | `Modal` base (Esc closes top-most), `EmailPicker` (fake e-mail templates), `ConfirmDialog` |
| `Scripts/UI/Describe.cs`, `Loc.cs` | rules records → Turkish sentences; localization + `MatchClock` |
| `UI/Game.uss` | theme (palette tokens as literal colours; `.art` gate for 9-slice skins) |
| `UI/Resources/ui/<folder>/*.png` | copy of the UI art kit (`Art/UI/<folder>`), loaded at runtime |
| `UI/Resources/ui/fonts/*` | Patrick Hand (sticky notes), Courier Prime Regular/Bold (memo/letter), Bebas Neue (stamp fallback) + OFL licences |
| `Editor/ProjectBuilder.UI.cs` | `UiTextureImportRules` (AssetPostprocessor for every PNG under `Resources/ui`), menu *Birimiz Kovulacak/UI/Sync UI art from Art/UI* and *Reimport UI textures* |
| `StreamingAssets/Localization/tr.json` | every string (≈1000 keys) incl. 10 fake e-mails, 4 manager profiles, cards, how-to |

## Art direction & kit

Palette (USS + `UiKit`): paper `#F4EFE3`, manila `#E8C98A`, office green `#2F6B4F` (primary), stamp red `#D2382F`
(danger / sabotage / fired), ink navy `#1F2A44` (text), sticky `#FFE066`, HR teal `#3BB9B0`. Player colours follow the
portraits: 0 Mavi, 1 Sarı, 2 Pembe, 3 Yeşil, 4 Turuncu, 5 Mor.

- **USS-only first.** Every component renders with colours, borders and radii. When `Resources/ui/buttons/primary_normal`
  exists the root gets `.art` and buttons, tabs and panels switch to the 9-slice skins (`resource("ui/buttons/...")`,
  slice borders from the manifest, slice scale 0.5 for the 2× art, set in code). Icons/portraits/cards/stamps/frames
  are applied per element with `UiArt.Apply` and fall back to a glyph tile / coloured disc with an initial / plain panel.
- **Loading scheme:** `UiArt.Tex("icons/task_coffee")` = `Resources.Load<Texture2D>("ui/icons/task_coffee")`, cached
  (misses too). Big one-screen textures are unloaded when their screen hides (menu/lobby backgrounds, how-to pictures).
  All kit textures together are ≈37 MPix (≈37 MB BC7 if everything were loaded at once; a match loads ≈20 MB).
- **Import rules** (`UiTextureImportRules`, automatic): Default texture type, no NPOT rescale, no mipmaps, clamp,
  bilinear, CompressedHQ (BC7; every kit image has dimensions divisible by 4), max 2048, not readable.
- **Fonts (runtime, nothing generated in the editor):** body Nunito / bold Nunito ExtraBold / titles Baloo 2 come from the
  scene's `UIRoot` fields as before; Patrick Hand, Courier Prime and Bebas Neue are loaded with `Resources.Load<Font>` and
  passed as `StyleFontDefinition(Font)` — UI Toolkit builds dynamic SDF font assets from them. Turkish glyphs
  (ç ğ ı İ ö ş ü â î û) verified in every font's cmap; `→ ✓ ★ ●` are **not** in the fonts, so the UI never uses them.

## Screens & flows

- **Main menu**: logo, name, Lobi Kur / Lobiye Katıl (join page: address, port, last 5 hosts) / rejoin after a drop /
  Ayarlar / Nasıl Oynanır / Çıkış (confirm).
- **Lobby**: six seat cards (portrait badge, name, look, tags EV SAHİBİ / SEN / BOT / BAĞLANTI YOK, ONAYLANDI stamp when
  ready), host: mode switch Hızlı Oyun / İş Haftası, add bot (button + per empty seat), remove bot, start (enabled by
  `HostController.CanStart`, `-autostart` kept); week board with career points in week mode; status line from
  `LobbyView.StatusKey` (`lobby.need_players` …).
- **Briefing** (manila folder, Enter / button to dismiss): today's manager (portrait, name, title, likes / hates / notes,
  Kayırmacı's favourite), weekday banner, role + secret objective, the two secret cards (card art), today's features
  (on/off chips), Friday multiplier, v1 day modifier if the host still sends one, controls strip.
- **HUD**: phase pill + timer + weekday chip; manager chip (portrait, state, warning countdown, SÜRPRİZ); banners
  (raid / final / surprise, İnternet yok + back-in timer, Elektrik kesik + timer); quota card (bar, coffee / report /
  phone (countdown) / dirty desks / mail chips); coworkers (badge with speaking ring, name, crown for the favourite,
  on-call / ringing bubbles faded by `AvatarVisibility.Alpha`, favour, performance, suspicion pips); prompts (icon, key,
  label, seconds, blocked reason, SABOTAJ tag) for E / F **and the held item on G**; hold bar from
  `LocalPlayerController.InteractionProgress`; me card (badge, favour bar, suspicion icon + pips, carrying, EĞİLİYORSUN,
  mic icon + level + PTT key + route); call widget (incoming / ringing out / active with timer, Y/N key caps, clickable
  buttons); hints with unread-mail badge; emote wheel (draws feel's wheel state); toast feed with icons (moves bottom-left
  in meeting/results and stays above them).
- **Phone** (Tab; same phone for everyone, İK app only on the Informant's): Görev (role, objective + progress + flags,
  today's manager), Kartlar (my cards with cooldown / office-wide limit, basic sabotage list), Rehber (call state, call
  buttons with availability + cooldown, tip line desk grid with confirm), Gelen Kutusu (newest first, tap to read, unread
  badge, "lock your screen" lesson), Notlar (sticky notes incl. call logs and tasting), Pano (evidence cards +
  observations), İK (charges, pull call logs, nearby scan, scan results, evidence with scan/seal).
- **Fake e-mail picker**: opened by feel's `IFeelUi.PickEmailTemplate` (SendFakeEmail affordance with IntParam −1);
  10 template cards with subject + body as they will appear "from" the pc owner; keys 1–0; Esc cancels (callback −1).
  Fallback: a rejected SendFakeEmail(−1) request also opens it.
- **Meeting**: projector frame (kept at 16:9) with slides Deliller / Kamera / Gözlemler / Kayıtlar; evidence cards incl.
  e-mail logs, call logs, tasting; footage cards = floor plan with the camera zone, every sighting as a player-coloured dot
  (older = fainter, newest with name + activity icon), timeline strip, sighting list, "KAYIT YOK" in a power cut; the
  Kamera slide opens itself when footage is shown and holds `ScreenFx.SetCctv`. Side panel: defense spotlight, people
  (accuse with confirm, vote / abstain, shortlist / favourite tags, who accused whom), footage request (only when allowed:
  own defense window or Informant, camera buttons + match-second slider within [0, meeting start]), my notes with "Sun"
  (max 2), verdict: fired name + stacked bars per candidate (vote / risk / performance share, favour discount text),
  tie-break, key reasons.
- **Results**: termination letter (letterhead art kept at 3:4, memo font, reason from key evidence / observation, votes,
  closing line, "Evet… bu sensin."), KOVULDU stamp slam at +0.9 s → `FeelHooks.ResultsStamp(screenPos)` (feel plays the
  thump + ink burst + camera punch), trombone after it; "Mektubu kaydet" crops the letter from the frame to
  `Pictures/BirimizKovulacak/kovuldu_<name>_<time>.png`. Reveal tabs: Kim kimdi? (role, objective, explanation, stats,
  misconduct bar, cards, title badges, career delta), Günün klibi (default when present: ~10 s top-down replay of
  `Result.Highlight`, interpolated dots, activity icons, actor revealed + caption at the end, skip / replay, CCTV look),
  Gerçekte ne oldu? (incidents with true actor, who was blamed, recap), Telefon kayıtları, Hafta (week board + standings
  with deltas). Ayın Elemanı overlay on the last day (trophy, stamp, winners). Rematch bar: Rövanş! / "<gün> gününe
  geç!" / "Yeni hafta başlasın!", votes, host: start now / back to lobby. The middle of the screen stays open so the
  feel camera's results shot is visible.
- **Settings**: Ses (master, effects `Sfx.EffectsVolume`, ambience `Sfx.AmbienceVolume`, voice, per-player volume by
  name), Mikrofon (device from `Microphone.devices`, push-to-talk vs open mic, mute, VAD threshold with live meter),
  Kontroller (mouse sensitivity, camera shake, every `GameInput.Rebindable` row, reset), Görüntü ve Dil (fullscreen,
  language placeholder "Türkçe").
- **Nasıl Oynanır**: 6 pages with the `Art/UI/howto` illustrations (icon collages as fallback), arrows / dots / ← →.
- **Pause** (Esc): resume, settings, how-to, controls strip, host "back to lobby" (confirm), leave (confirm), quit.

Esc closes the top-most thing: modal → settings → how-to → phone → join page → pause. Tab toggles the phone in
Briefing..FinalInspection. `UIRoot.BlocksGameplay` (cursor free, no movement) for menus, lobby, phone, pause, settings,
how-to, modals, meeting, results and the undismissed briefing — **not** for the emote wheel (feel reads mouse deltas).

## Privacy & robustness notes

- Line of sight: crew-row ringing / on-call flags and speaking rings fade with `AvatarVisibility.Alpha` and take no
  layout space while hidden; the phone's Rehber shows "Görüş alanında değil" instead of ringing/busy for someone out of
  sight, and the call button never reflects the callee's state (the engine answers `Unavailable`).
- Match clock (`MatchClock.Update`, every frame): the Briefing start when this client saw it, otherwise the current
  phase start minus the configured phase durations (reconnects). Footage range = Briefing..FinalInspection durations.
- Toasts for EvidenceCreated / FootageShown / ManagerObservation / EmailSent wait (≤ 3 s) for the snapshot that contains
  the record; after that a generic line is shown.
- Results reset their tab, replay and Ayın Elemanı overlay per match; meeting footage cards are cached per meeting only.

## Performance

- HUD: built once; pooled crew rows (6), prompt rows (3), order chips, toast slots (7), wheel. On a view change
  (≈10 Hz) only fields whose value changed are re-formatted (quota, counts via cached "×n" strings, manager chip key,
  crew name key, favour value, carried-item key, suspicion level). Per frame: timers via `UiKit.ClockInt` (cached
  "m:ss" strings, only on second change), hold bar width, mic level (quantized), speaking rings / LOS opacity
  (quantized, change-only). No per-frame managed allocations after warm-up (closures only on events).
- Phone / meeting / results rebuild their content only on state change (phone throttled to ≥0.35 s, 1 Hz for timer
  apps); footage cards are cached per evidence id; the replay is allocation-free per frame.
- Texture memory as above; fonts: 4 extra dynamic font atlases.

## Integration steps for the lead

1. **Merge `v2/ui`.** Unity's import creates `.meta` files for the new scripts, PNGs and fonts; the AssetPostprocessor
   applies the UI texture settings on import (if textures were imported before the editor assembly compiled, run
   *Birimiz Kovulacak/UI/Reimport UI textures*). No scene/prefab change: `UIRoot`'s serialized fields are unchanged
   (`Style`, `BodyFont`, `BoldFont`, `TitleFont` set by `ProjectBuilder.Scene.cs`).
2. **Art updates:** `Resources/ui` is a copy of `Art/UI/{backgrounds,bubbles,buttons,cards,howto,icons,logo,meeting,
   phone,portraits,stamps,titles,week}`; after a new art run use *Birimiz Kovulacak/UI/Sync UI art from Art/UI* (or
   `ProjectBuilder.SyncUiArt()` in batch mode) and commit the copied PNGs.
3. **Voice** — already wired to `VoiceSystem.Instance` (docs/VOICE_NOTES.md "API for the UI agent"): `VoiceSystemUi`
   (in `UiServices.cs`) feeds speaking rings (`IsSpeaking`), the mic meter (`LocalMicLevel`), transmit state, mute and
   PTT label; Settings edits `VoiceSystem.Settings` (device, PTT / open mic, sensitivity, input gain, volume 0..2,
   self-mute, deafen, loopback test, `MicTest` while the mic tab is open, `Save()` on close) and per-player volume via
   `Get/SetPlayerVolume(slot)`. With `-novoice` the HUD hides the mic and Settings edits `VoiceSettings.Load()`.
   `UiServices.Override` can replace the bridge (tests).
4. **Feel** — already wired: `UIRoot` implements `IFeelUi` (`FeelHooks.Ui = this` in `Awake`, `DrawsEmoteWheel`,
   `PickEmailTemplate`, `TextInputFocused`), sets `FeelHooks.UiDrivesResultsStamp = true` and calls
   `FeelHooks.ResultsStamp`; footage viewers hold `ScreenFx.SetCctv`. The UI no longer reads T/Y/N/G/1–5 itself and plays
   none of the FeelDirector sounds. `Hud.Toast(text, kind)` keeps its v1 signature (optional icon parameter added).
5. **Manager bubbles** — done in `Scripts/World/ManagerVisual.cs` (lead-approved edit): PersonalCall / InappropriateEmail /
   SaltyCoffee / TipClean / TipFound map to `manager.bubble.call/.email/.salty/.tip_clean/.tip_found`, and the start of a
   surprise inspection shows `manager.bubble.surprise`.
6. **Lobby** uses the host API directly (`AddBot`, `RemoveBot`, `SetMode`, `CanStart`, `StartMatch`, `Mode`); nothing to
   add. `reject.full` now reads 6/6.
7. Optional: if the host stops sending v1 `day.*` modifiers in `LobbyView.StatusKey`, the briefing simply omits that line.

## Still placeholder / known gaps

- Per-player hearing state (`GetHearing`: muffled / call) is not shown yet; only speaking rings.
- Language selector is a disabled placeholder (only `tr.json` exists).
- No gamepad navigation (keyboard + mouse only); phone tabs have no number-key shortcuts (1–5 are emotes).
- Card illustrations exist only for the six card sabotages; basic sabotage uses icons.
- `ScreenshotSaver` crops via `ScreenCapture.CaptureScreenshotAsTexture` (full-frame read-back, once per click).

## Needs a Unity run to verify

1. USS import: no warnings for `resource(...)` skins, transitions (`scale`, `translate`, `rotate`, `ease-out-back`),
   `-unity-slice-*`; 9-slice look of buttons / tabs / panels with slice scale 0.5 / 0.6 / 0.4 (README: kit is 2×).
2. Fonts: Patrick Hand / Courier Prime / Bebas Neue render Turkish text through `StyleFontDefinition(Font)`.
3. Layout at 1280×720 and 1920×1080: HUD corners don't overlap (crew ≤ 6 rows vs feed at top 330 px), phone frame
   screen rect, lobby 3×2 seats, briefing three columns, results letter (3:4) + reveal, meeting projector (16:9).
4. Phone dock with `phone/app_*` icons; email picker grid (10 cards) fits; ConfirmDialog over the meeting.
5. Floor plan proportions vs `DefaultOffice` rooms; footage dots; highlight replay interpolation and timing (~10 s).
6. Results stamp slam timing with feel's burst position; "Mektubu kaydet" crop rectangle and file in Pictures.
7. Emote wheel drawing matches feel's sector order and cursor; Y/N in the call widget; held-item (G) prompt.
8. LOS fade of crew-row call / ringing / speaking indicators; power-cut and outage banners with timers.
9. Esc / Tab routing; `TextInputFocused` true only while a menu field is focused (fields blur when the menu hides).


## v4 meeting / results / shell track (hybrid office) — history

Owner: meeting + shell UI. Rules: `docs/GAMEPLAY_REDESIGN.md`; contract: `docs/CONTRACTS_V4.md`.

- **Rich text is off everywhere.** `UiKit.Text` (and so Title/Bold/Memo/Hand/Tag/StampText) and `UiKit.Btn` set `enableRichText = false`,
  so a player name like `<size=300>` renders literally. Field labels that can carry a name use `.PlainLabel()`. Never build rich-text
  strings around player text.
- **Dossier slides** (`MeetingSlides.cs`, `DossierDeck`): cover, company productivity vs target (gauge, task outcomes, favourite),
  office log (observable incident effects, missed inputs, remedial reports), anonymous complaints (labelled
  "anonymous · unverified · not evidence"), recordings (tape deck + task context), presented records, blame board. No culprit is ever shown.
- **Side drawer** (`MeetingSide.cs`): public meeting chat (`SendMeetingChat`, limits from `Mine.MeetingChatLeft` / `NextMessageAt`,
  rejection reasons) and the evidence picker (`PresentRecord`). Presentations render as cards (`MeetingRecords.cs`): source, time,
  "authenticated record", presenter; `EXPOSED: <incident kind>` stamp naming the record's actor; repeats say "no new consequence".
- **Ballots** (`MeetingPanels.cs`): colleagues + "No action"/"Fire nobody" (`LineIds.Nobody`) + withdraw; voters shown, votes not;
  verdict reveals every vote and explains each `VoteOutcome`. Final: defense for everyone in `DefenseOrder`, "needs N of M", verdict
  fired / nobody / company closed (`FinalVerdictView.cs`).
- **Results** (`WeekResultsView.cs`): company outcome first; closed = shared defeat (no ranks); itemised career lines, shared ranks,
  EotM / "no award", objectives, exposed incidents only (+ total count), money, contribution quality, session board, next-manager pick,
  "Play again" (`RequestRematch`, `TelemetryHooks.RematchRequested`; `RematchPromptShown` once per match with >= 3 players).
- **Shell** (`ShellOverlays.cs`): first-launch flow `ShellFlow.Next()` = language picker -> consent prompt (default No) -> 3-card intro
  (art `ui/onboarding_v4/intro_{1_company,2_secret,3_meeting}`, falls back to `onboarding/ob_*`); reopenable from the menu.
  Settings: music slider + mute (`MusicSettings`), fullscreen via `DisplaySettings`, Privacy tab (only when `TelemetryHooks.Privacy != null`).
  Moment marker: F8 or the corner flag -> category picker -> `TelemetryHooks.MomentMarked(category)`; 1 per 20 s, 15 per match.
- **Lobby**: v4 status keys (`lobby.need_players`, `lobby.need_ready`, `lobby.pool_too_small`), Short/Full with approximate minutes,
  recordings toggle, host-only playtest group code (`LobbyScreen.GroupCodeSetter`), declared experiments, rematch intents per seat.
- **Mocks**: `MeetingMocks.cs` (`-uimock "meeting.*"`, `final.*`, `results.*`, `menu.*`, `lobby.*`, `howto.*`, `match.*`, each `.tr` / `.en`).
- **Localisation**: `tr/en.meeting.json` (rewritten for v4), new `tr/en.privacy.json` (consent, privacy, intro, moment marker), plus patches in
  `tr/en.json` (how-to, lobby, settings, menu). Check with `python -I Tools/scripts/loc_parity_meeting.py`.
