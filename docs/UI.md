# UI / UX v2 — "kurumsal hiciv"

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


## v4 meeting / results / shell track (hybrid office)

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
