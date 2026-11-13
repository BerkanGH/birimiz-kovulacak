# UI

## v5 play UI (current, `feature/v5-integration`)

Rules: `docs/V5_ENGINE_SPEC.md` (normative) and `docs/V5_DESIGN.md` (Turkish, rationale). Everything below the
"History" line describes the v2–v4 UI, which no longer exists in code.

During a match the player is either **in their own office** (Intro, tasks, breaks, crisis calls: the diegetic office
layer of docs/V5_INTERACTION.md step 1) or **in the meeting room** (meetings, crisis vote, final, results: the seated
meeting room layer of steps 3a and 3b, below). The old one-window match screen (`PlayUi`) is only the fallback without the rooms
scene. Shell screens (menu, lobby, settings, how-to, first-launch flow) are the older shell code, updated for v5 where
needed.

| File (under `Game/Assets/Game/`) | What |
|---|---|
| `Scripts/UI/Play/PlayUi.cs` | The old one-window match screen (header, players column, focus card, phase details, phone, transfers, events, messages); since step 3a only the fallback without the rooms scene. |
| `Scripts/UI/Play/PlayText.cs` | Typed data → sentences (`v5.*` keys): phases, tasks, dossier lines, events. |
| `Scripts/UI/Play/TapeDeck.cs` | Cassette deck shown while a leaked recording plays (on the meeting room projector since step 3b; compact inside the fallback screen's centre column). |
| `UI/Resources/ui/styles/play.uss` | Styles of the match screen (logical 1920×1080, scaled; same layout at 1600×900). |
| `StreamingAssets/Localization/tr.v5.json`, `en.v5.json` | Match strings (`v5.*`, `rec.*`). Turkish first; English mirrors it. |
| `Scripts/UI/SettingsScreen.cs` → `HowToPlayScreen` | "Nasıl oynanır": 11 v5 pages (`howto.N.*` in `tr.json`/`en.json`), numbers formatted from the host's `LineConfig` (else `GameConfig.Line`). `PageGoal`…`PageFinal` constants. |
| `Scripts/UI/ShellOverlays.cs` | First-launch flow (language, consent, 3 intro cards in `*.privacy.json`), moment marker. |
| `Scripts/UI/MenuScreens.cs`, `UIRoot.cs` | Menu, lobby, pause (how-to button in all three); layers, Esc routing, cursor. |
| `Scripts/UI/Dev/LineMock.cs` | UI mocks: hand-made `LineView`s pushed into `LineClient` (`-uimock "v5.*"`). |
| `Scripts/UI/Play/PlayKit.cs`, `PlayBlocks.cs` | Shared by the meeting room, the office and the fallback screen: play-styled buttons, live labels, the content-diffed `PlaySection`; the task card, rules, intro, transfer, messages and call-log blocks, the meeting / final info, the desk-reveal card, the results ballots (no rule text is duplicated). |
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
| `Scripts/UI/Meeting/MeetingUi.cs` | Meeting room layer: the same strip and toasts as the office, the name tag and use prompt, keys, opens the folder / ballot / whiteboard zoom / projector zoom (3b), drives the projector and the ceremony camera (desk opening, results), the "on the projector" note. |
| `Scripts/UI/Meeting/MeetingFolder.cs` | The folder's tablet laid over the tablet screen: Eylemler (Soruştur, Kaydı sızdır), Sohbet (meeting chat + texts), Havale, Dosya (in the results: the full reveal), Kurallar. |
| `Scripts/UI/Meeting/MeetingBallot.cs` | The ballot paper: one layout drawn into the paper's texture (what folds and flies) and laid over it while held; marks, drop (sends the votes), a fresh paper, the auto drop near the end. |
| `Scripts/UI/Meeting/MeetingBoard.cs`, `MeetingText.cs` | The live suç tahtası on both whiteboards; the "Şimdi" line, prompts and name tags. |
| `Scripts/UI/Meeting/MeetingProjector.cs` | Step 3b: the projector at the head of the table, a 1280×720 panel rendered onto the screen (`WorldPanel.CreateOffscreen` → `MeetingRoom.SetProjector`): dossier deck, leaked recording (TapeDeck), votes, verdicts, crisis, defence, desk opening, results. |
| `Scripts/UI/Meeting/MeetingTimeline.cs` | Step 3b: `DeskOpening` (one timeline for the projector count, the box, the payout on the table and the camera) and `BallotDrops` (papers in the box per vote round, from the public VoteCast events). |
| `Scripts/UI/Meeting/MeetingReveal.cs` | Step 3b: the full reveal in the folder's Dosya tab at Results (balances and their prim log, who robbed whom, every vote, desk openings, transfers, task choices, incidents); `RevealMemory` (this client's own task groups). |
| `Scripts/UI/Meeting/MeetingSlide.cs` | What is left on screen of the 3a public card: the results bar (my place, Lobiye dön / Tekrar / Çık, the reveal, projector or table) and the abandoned card. |
| `Scripts/Rooms/MeetingRoom.Props.cs`, `Scripts/Net/MeetingGaze.cs` | World side of step 3a (the folder lift, the paper fold and flight, the ballot box and its pile, the boards); shared gaze (8 Hz presentation packets) and `BotGazer`. |
| `Scripts/Rooms/MeetingRoom.Stage.cs` | World side of step 3b: the defender stands up (chair rolls back, standing clips, the spotlight follows, the local camera rises), the projector as everyone's point of attention, the desk payout (bundles at the fired colleague's place sliding to the voters). |
| `Scripts/UI/Office/ToastStack.cs` | The corner notes shared by the office and the meeting room. |
| `Scripts/UI/Office/BotCallCard.cs` | v5 bot talk: the bot's speech bubble while on the phone with a bot (typed lines, my questions, keys 1/2/3, "Prim gönder"); beside the phone panel, under the call pill, or in the computer's right column. |
| `Scripts/UI/Meeting/MeetingSubtitles.cs` | v5 bot talk: a bot's chat line as a bubble over its head; the defending bot's line as a subtitle over its standing character. |
| `Scripts/Net/BotSpeech.cs` | v5 bot talk, presentation side: phone-line / question packets, my call transcript, bot chat tokens → text in my language, the bots' mumble and talk level. |
| `Scripts/UI/Dev/BotTalkMock.cs` | Bot talk mocks (`-uimock "v5.office_botcall*"`, `"v5.meeting_botchat*"`). |
| `Scripts/UI/Dev/MeetingMock.cs`, `MeetingMock3b.cs`, `UI/Resources/ui/styles/meeting.uss`, `projector.uss` | Meeting room mocks (`-uimock "v5.meeting*"`, `"v5.office_partner*"`); meeting room, tablet, paper, whiteboard, results bar and projector styles. |

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

Scenarios: `v5.task`, `v5.task_shift`, `v5.reveal` (witness; all three open the office computer), `v5.crisis_vote`, `v5.crisis_verdict`, `v5.sealed` (I am a candidate), `v5.defense`, `v5.final` (second
firing vote), `v5.verdict`, `v5.verdict_tie`, `v5.results`, `v5.results_tabs`, `v5.howto` (all pages), `v5.en_task`,
`v5.en_verdict`. Automated matches (`Tools/run_local4.ps1 -Bots`) also save one screenshot per phase per slot under
`<run>/shots`.

**Meeting room (v5 step 3a).** docs/V5_INTERACTION.md §3. Meetings, the crisis vote, the final and the results are played
seated at the meeting table (the door lamp and the black fade of step 1 lead there). Mouse look about ±55° yaw, 30° up,
50° down (`MEETING_ANCHORS` in `build_rooms.py`). Every colleague's bean sits in their colour; looking at one shows a name
tag (name in their colour, suç, ADAY / KOVULDU / BOT, "oy verdi" during a vote, what E does). The current speaker (voice
level from `VoiceSystem.GetSpeechLevel`) plays the SitTalk clip with mouth flaps plus a code-driven squash and bob.

*Shared gaze ([KARAR] 4).* `MeetingGaze` sends my look (yaw/pitch relative to my seat, whole degrees, `sbyte`) to the host
at 8 Hz, unreliable, only when it changed or once a second (`NetSession.GazeRpc`); the host adds its bots (`BotGazer`:
the defender during the defence, else mostly the speaker, sometimes the colleague with the most suç or someone else,
sometimes the table, with drift) and sends everyone one packet (`GazeAllRpc`, `[n][slot, yaw, pitch]…`). Each client turns
the heads (`MeetingRoom.SetGaze` → `SeatedBean.SetGaze`, head 70 % / body 30 %, smoothed); without fresh gaze the old
automatic glances. Holding the folder or the paper sends "looking down". Automated players (`-bot`) look like a bot.
Presentation only: nothing reaches the engine, logs or analytics.

| Object | What |
|---|---|
| Whiteboards | The live suç tahtası (`MeetingBoard`, one 1024×512 texture on both boards): rows in seat order with tally marks and the number, ADAY / MÜHÜRLÜ / KOVULDU, "oy verdi", "+N" after a verdict; on the right the meeting's failed tasks or the candidates and the defence order. The wall board faces the left row, the board on wheels (`RmWhiteboardStand`) the right row. E zooms in (`PovMode.MeetingBoard`), E / Esc back. |
| Folder (tablet) | `RmFolder` in front of every seat; only yours is a target. E (or Q, or E with nothing under the crosshair outside a vote) lifts it to the camera and flips the cover open; the tablet UI is laid over the screen (`MeetingFolder`). Tabs: Eylemler (Soruştur with the rules and its cost, Kaydı sızdır per recorded call), Sohbet (meeting chat and texts in one thread; E on a colleague opens it addressed to them), Havale, Dosya (dossier, leaks, candidates, earlier firings; every vote in the results), Kurallar. Esc / E puts it down. |
| Ballot paper | Only during a vote (when you have ballots): a paper (runtime mesh, two halves) and a pencil (`RmPencil`) lie beside the folder. E on it (or E with nothing under the crosshair) brings it up; click the box next to a name (one column per ballot when a meeting has two failed tasks; E on a colleague marks them). "Katla ve kutuya at" (E) sends the votes, folds the paper and drops it into the box; a fresh paper comes back and replaces the vote if dropped. Final vote: an extra "Boş oy (çekimser)" row; suç and crisis votes: none (an empty ballot counts against you, as before). 1.6 s before the end a marked paper you did not drop goes in for you. |
| Ballot box | `RmBallotBox` drops onto the table centre for each vote and stays for its verdict. Every `VoteCast` event (anyone's, never the content) flies a folded paper from that seat into the slot, rattles the box and adds a paper to the visible pile. |

The strip's "Şimdi" line (`MeetingText.Now`) points at the folder, the paper, the board or the projector; toasts: texts
and meeting chat (when the chat tab is not open), prim received, blame, investigations, leaks, rejections, phase changes
(no firing toast since 3b: the desk opening is the moment). In 3a the public information was a card over the room; 3b
moved it onto the projector (below). Keys: mouse look, E use / pick up / mark / drop / zoom, Q folder, Tab projector
(again: back), V push-to-talk, Esc put down → menu.

**Partner call (office, step 3a).** When a pair / trio task starts (Client Bonus, Petty Cash) a toast names the partner(s)
and the key ("Ortağın: Yeşil · Aramak için Tab"). During that task, its reveal and its break the desk phone pins the
partners at the top of the contacts with an ORTAK tag; Tab opens the phone with the first partner selected (LCD:
"Ortağın: YEŞİL", "E / Enter: ara"); E or Enter calls the selected contact, ↑ / ↓ change it. Nothing is called
automatically. 15 s into a pair task without an outgoing call (and a human partner) the hint "İpucu: Ortağını arayıp
pazarlık edebilirsin (Tab)" shows once per task, only in the player's first three matches (PlayerPrefs
`v5.matches_played`; not counted for UI mocks and `-bot`).

**Meeting room (v5 step 3b).** docs/V5_INTERACTION.md §3: the projector, the leaked recording, the defence, the desk
opening and the results.

*Projector* (`MeetingProjector`). A 1280×720 UI panel rendered into a texture that `MeetingRoom.SetProjector` puts on the
canvas at the head of the table (the default slide comes back when no match runs). Same template as the default slide:
green bands with a gold rule, cream body, navy ink, stamp red; titles 56–64 px and lines 34–40 px so it reads from the far
seats. Header: what is on (Loc.Upper kicker) and the phase clock; footer: "Projektöre bak: Tab · Klasör: Q" and page dots.

| When | On the projector |
|---|---|
| Meeting dossier / discussion | A deck: the agenda (failed tasks, the ballots everyone has, the roster), then one slide per dossier entry with a stamp (İHBAR, SORUŞTURMA, SIZINTI, KASA AÇIĞI, …), the sentence and the faces involved with their role (an investigation says its result: "Sarı hepsini almış!" or "haksız soruşturma"), then the leaked recordings and their state. A slide every 6.5 s; a new entry jumps in, stamps itself and stays 9 s. |
| Final discussion | The candidates with MÜHÜRLÜ stamps, the crisis result (who got the suç, the board shift "6 → 10" with ADAY / MÜHÜRLÜ), the leaks. |
| A leaked recording (4 s before to 3 s after) | The cassette deck (`TapeDeck`, art `meeting_v3/tape_player`): "Sarı ↔ Yeşil" on the label, the leaker, both faces (the talking one lights up), live waveform and level meter, "Dinletiliyor 0:03 / 0:07"; the audio plays as before. Everyone without a fresh gaze looks at the screen (`MeetingRoom.SetAttentionProjector`); bots too (`BotGazer`). |
| Votes | What is voted (the candidates for a firing vote, "boş oy verilebilir"), one paper per colleague that turns over when theirs is in the box (`BallotDrops`, never the content), "Kutuda: 3 / 5 pusula"; the second firing vote also shows the first desk ("1. kovma: Pembe · 11 prim · paylaşanlar: …"). |
| Meeting verdict, crisis verdict | The suç handed out (faces, "+2 suç"); the crisis result with the board shift. |
| Defence | The defender's face in a spot with a voice meter, "SAVUNUYOR", the name, a big countdown (red under 5 s), the order (konuştu / şimdi konuşuyor / sırada). |
| Desk opening (`FinalVerdict`) | `DeskOpening` timeline (10 s; shorter verdicts squeeze it): the fired colleague's face and KOVULDU, how it was decided; the drawer slides out from under the desk top (seen from above), the bundles appear one by one while the counter counts "0 → 11 PRİM", the red "11 PRİM" stamp (with the trombone), then the voters' chips "Mavi +5", what burned, "Sen +5 prim aldın". |
| Results | The ranking by prim revealed from the last place up (prim counting up, KOVULDU stamps), then the winner(s) with the HAFTANIN ELEMANI stamp and a cheer. |

Tab, or E on the screen (a `MeetingTarget.Projector` collider over the frame), frames the screen (`PovMode.MeetingProjector`,
no mouse-look, the strip hides, "Projektör · Tab / E / Esc: masaya dön"). Every seat can also turn to it: the seated yaw
stays ±55° except on the projector's side, which opens up to the projector + 12° (`PovCameraRig.SetLimits(min, max, …)`);
turned more than 40° aside you lean in over the table (up to 0.28 m) to see past the colleague beside you.
When the projector shows a leak, the defence, the desk opening or the results and less than 60 % of it is on screen, a small
note bottom left says so ("Projektörde: kayıt dinletiliyor · bak: Tab").

*Defence.* The spotlit defender stands up (`MeetingRoom.Stage`): the chair rolls back 0.55 m and turns a little, the bean
rises 0.26 m against the table edge (the beans' legs are shorter than the chair is high, so the standing clips would make
the defender smaller: the seated upper body stays, with the talk gestures while the voice is heard), the spotlight aims at
the standing head; a local defender's camera rises 0.28 m. Everyone without a fresh gaze looks at the defender; bots do
92 % of the time; humans keep free look.

*Desk opening and payout.* When a firing's verdict starts (and nothing is in your hands) the camera goes to the projector
by itself for the count, then back to the table looking at the fired colleague's place (any key takes over;
`MeetingUi.NoAutoCamera` turns it off). After the count the fired colleague takes the box (`SetFired`), their desk appears
as bundles of five (`RmBundle`, loose `RmBill`, at most 16 bundles) at their place, and the shares slide to the places of
the voters (`MeetingRoom.StartPayout`; what cannot be split curls up and burns). The bundles stay on the table until the
match ends, so in 5–6 player matches the first payout lies on the table during the second vote. A late joiner sees them
where they ended up.

*Results.* The camera goes to the projector for the ceremony; the cursor is free for the results bar at the bottom
(`MeetingSlide`: my place, Lobiye dön (host) / Tekrar oynamak istiyorum / Oyundan çık, "Tüm açılış: klasör (Q)",
"Projektör / Masaya dön (Tab)"). Q opens the folder on Dosya with the full reveal (`MeetingReveal`, tabs): Bakiyeler (every
prim and its log: start, tasks, cash/whistle, investigation, transfers, desk), Kim kimi soydu (the player's own story in
first person, "Sarı benden 2 prim aldı ve yine de beni kovdu!", then every Client Bonus robbery and Petty Cash taking; the
pairs come from this client's own groups or from the payouts when only one pairing fits, else taker and victim are
listed apart), Bütün oylar, Masa açılışları, Havaleler, Görev seçimleri, Olaylar.

*3a leftovers fixed in 3b.* A blank paper in the final is a vote: the board, the name tags and the projector say "pusula
attı" for every paper in the box this round (`BallotDrops`, from the VoteCast events and my own drop; the engine still
reports an abstention as an empty ballot). The ballot box stands across the table with a label plate on both long sides
(art: `RmBallotBox` back plate, layout yaw 90°). The pencil draws each new X on the held paper. Office toasts are cleared
when a match closes. The results card under the HUD strip is gone (the results bar sits at the bottom).

Meeting room scenarios (`-uimock "v5.meeting*"`): `v5.meeting_look` (everyone looks at you, a colleague talks, a name
tag, colleagues looking at each other, looking down at the table), `v5.meeting_board` / `v5.meeting_board_stand` (the
board from the seat, zoomed, after the crisis with stamps; the right row's board on wheels), `v5.meeting_folder` (prompt,
Eylemler, Sohbet, Havale, Dosya), `v5.meeting_ballot` (paper on the table, prompt, held, two columns marked, folding,
flying, the colleagues' papers, a fresh paper with "Kutudaki oyun"), `v5.meeting_final` (final vote with the blank
row, dropped blank, the defence), `v5.meeting_crisis`, `v5.meeting_en`; step 3b: `v5.meeting_projector` (from the seat,
zoomed, deck slides, a new investigation sliding in), `v5.meeting_projector_far` / `_farright` (the far seats of both rows),
`v5.meeting_projector_final` (candidates, crisis and board shift, verdicts, a vote, the firing vote), `v5.meeting_leak`
(everyone turns to the screen, the note out of view, zoomed with the waveform), `v5.meeting_defence` (standing defender in
the spotlight, the projector, me defending), `v5.meeting_reveal` (drawer, count, stamp, back to the table, box, bundles
sliding, landed), `v5.meeting_reveal6` (first opening and payout, the second vote with the bundles on the table, the
second opening), `v5.meeting_results` / `_results4` (ceremony, reveal tabs, results bar), `v5.meeting_ballot_pen`, and
English `v5.meeting_projector_en`, `v5.meeting_leak_en`, `v5.meeting_reveal_en`, `v5.meeting_results_en`; office:
`v5.office_partner`, `v5.office_partner_trio`, `v5.office_partner_hint`. The legacy phase scenarios (`v5.crisis_vote` … `v5.results_tabs`)
now show the meeting room with the projector (`v5.results_tabs` opens the folder's reveal tabs). `-scenetest` adds `s3a_*` shots (gaze, talking, both boards and their
zoom, the folder on the table / lifting / held, the paper on the table / held / folding / flying, the box with a pile,
the box leaving) and `s3b_*` shots (the projector from three seats, its hover and zoom, everyone looking at it, the defender
standing and sitting back, the local defender, the payout pile / sliding / landed, the fired box, a second payout beside
the first, the ballot box label from both rows). Automated matches save `room`, `folder`, `board`, `ballot` and `box` shots
of the meeting phases, and since 3b `projector` (dossier, votes, crisis, defence, results), `projector_seat`, the defence
`room` (aimed at the defender), the desk opening `projector` / `payout` / `payout_end`, and the results `reveal` / `robbed`.

**Talking to a bot (v5 bot talk).** docs/V5_BOT_TALK.md. Bots are for testing only; they now call, answer (after 1–3
rings) or turn you down, and talk in text. Calls between people are unchanged (voice only, no buttons).

*On the phone.* In the contacts a bot reads "bot · yazıyla konuşur" and its Ara button is a normal one. With a bot on the
line the LCD says "Bot yazıyla konuşur · Sor: 1–3 · Kapat: N" and a speech bubble (`BotCallCard`, paper with a navy rim)
shows the call: the bot's name and colour, its lines typed out (~42 characters a second, the newest in full size, older
ones grey, my questions on the right in green: "Sen: Ne seçeceksin?"), 2–3 question buttons for the current task or the
crisis (key cap + text; keys 1 / 2 / 3 or click; Client Bonus / Petty Cash 3, Weekend Shift / crisis 2; none in the
breaks, where the bot says its own business) and "Prim gönder: 1 / 2 / 3 prim". Each line plays a short babble
(`SfxSynth.Mumble`, four takes, pitch per colleague's colour). Where it sits: beside the phone panel with a tail toward the
handset when the phone is up (`.bc-phone`), a compact version under the call pill when it is not (`.bc-hud`, last two
lines and the buttons), in the computer's right column under the call pill while the computer is open (`.bc-os`). A
refusal ("Şu an çok yoğunum.") or the bot's goodbye stays 4.5 s after the call ends ("görüşme bitti"). Prim a bot pays
arrives like any transfer (toast, drawer).

*In the meeting room.* Bot chat lines are a token in the engine's chat (`LineBotLines.Token`); every view of messages
(toasts, the folder's Sohbet, the computer's Mesajlar, the fallback screen) renders them in the reader's language (any
sender's well-formed token: an automated `-bot` player joins like a person, so the engine does not flag it). The phone
bubble shows for a colleague flagged as a bot or one that has said a bot line to me (`BotSpeech.Talks`). A bot's line also comes up as a bubble over its head for 4–7 s (`MeetingSubtitles`, typed out,
kept on screen; hidden while the folder, the paper or a zoom is up) while the bot mumbles from its seat and plays the talk
animation (its level feeds `MeetingRoom.SetSpeaking`, so the shared gaze turns to it too); a line whose bubble is on
screen gets no chat toast (the bubbles sit under the toasts). During the final defence the
defending bot's line is a subtitle over its standing character (dark with the spotlight's yellow rim, 26 px) until its
turn ends; out of view it sits at the bottom of the screen like a film subtitle.

*3b polish.* Zoomed on the projector the way back is written in the projector's own footer ("Projektör · Tab / E / Esc:
masaya dön") instead of a chip over the slide (the chip covered the desk opening's last line); the phase toast is not
shown while zoomed (the projector's header says the phase) and the corner notes start under the header
(`.mt-toasts-zoom`), clear of its clock; in the results the zoom frames the screen a little smaller and higher
(`RoomsPresenter.ProjectorRoomBelow` = 13 % of the height) so the results bar sits under the slide's footer. The
ceremony camera now decides once the room is there (arriving straight into a desk opening or the results used to skip it).

Scenarios: `v5.office_botcall` (a Greedy bot rings during Client Bonus, its opening lie under the call pill while typing,
the bubble beside the phone, questions 1 and 3 with its lies, the computer column), `v5.office_botcall_promise` (an Honest
bot: "Bu sefer ben yazmıyorum… Sen yazarsan sana 1 prim gönderirim.", a question, its goodbye, the reveal and the prim it
pays), `v5.office_botcall_refuse` (Greedy turns its victim down in the break, Mixed: "Sonra ararım."),
`v5.office_botcall_crisis` (crisis questions to an Honest bot), `v5.meeting_botchat` (an accusation over a bot's head, the
made-up counter-accusation, the folder chat), `v5.meeting_botchat_defence` (a bot defending on its feet with its subtitle,
out of view at the bottom, the final-vote lines), and `_en` variants of the office, promise, meeting and defence scenarios.


**Yaramazlıklar (v5, meeting-room pranks).** docs/V5_YARAMAZLIKLAR.md; engine rules docs/V5_ENGINE_SPEC.md §19. Lobby option
"Yaramazlıklar açık" (default on, host only, next to the recordings toggle; `LineLobbyView.PranksEnabled` →
`LineSetup.PranksEnabled`).

| File | What |
|---|---|
| `Scripts/UI/Meeting/MeetingPranks.cs` | The folder's **Yaramazlıklar** tab, the **G** quick wheel, aiming, the post-it word / plane note card, reading a plane's note, the coffee / cream splash over my screen, my own post-it badge, the manager's lines (strongest event first, a moment later), bot reactions out loud. |
| `Scripts/Rooms/MeetingRoom.Pranks.cs` | World side: the mug (a copy of the office mug) arcing into a shirt with a splash and a stain for the match, the paper plane gliding onto a table (it stays for the session; mine is a `MeetingTarget.Plane` to read), the post-it slapping onto a forehead (a TextMeshPro word, for the session), the phone ring at a seat (panic, then the phone clip and a phone in the hand), the slurp (drinking clip), standing up (the defender's stand: chair back, slam, point), flattering (the manager pleased, then pointing), the cake (cream on the face), the manager's mood face. Runtime meshes and tinted copies of the paper material, no new assets. |
| `Scripts/UI/Meeting/MeetingProjector.cs` | The patience gauge in the header corner (the manager's portrait in a green / amber / red ring, six notches, "x2" in the defence) and the **TUTANAK** stamp with the culprit's name over whatever is on. |
| `Scripts/UI/Meeting/MeetingSubtitles.cs` | The manager's line over his head (red rim, low mumble); bot reactions use the bot bubbles. |
| `Scripts/UI/Meeting/MeetingReveal.cs` | Results → Dosya: a **Yaramazlıklar** tab (every prank with its actor, phone rings revealed, prim burned per colleague, tutanaks; my own plane notes only) and "yaramazlık −N" in each balance. |
| `Scripts/Audio/SfxSynth.cs` | Procedural sounds: an original embarrassing ringtone (a made-up kazoo-ish ditty and a "boing"), slurp, table slam, splat, whoosh, paper flutter, slap. `Sfx.PlayAt` plays a one-shot from a seat. |
| `Scripts/Net/BotSpeech.cs`, `HostLineController`, `LineBotDriver` | Bot reactions as "shout" packets (bot → host → every client), never through the engine; the host's prank log at Results. |
| `Scripts/UI/Dev/PrankMock.cs` | Mocks `-uimock "v5.meeting_prank*"` (+ `_en`). |

*Folder tab.* Top: the manager's face and the notches, the mood ("Müdür sakin / kaşları çatık / kıpkırmızı!"), the
rule (capacity, +1 suç; the phone ring's tutanak goes to the target), "Savunmada sabır ×2" in the defence, the keys.
Then one row per prank: name, price chip (red "2 prim" / green "Bedava"), patience chip ("Sabır +3", "Sabır −2"), uses
("Bu oturum: 1 hak", "Kalan: 5", "Her 10 sn") and, when closed, why (mühürlü, prim yetmiyor (2 gerekir), bu oturumda
kullandın, 7 sn bekle (live), sadece Sonuç'ta, kovuldun). Clicking a row opens it: the description and its actions —
name buttons in each colleague's colour (far seats) plus "Bakarak at (E)" for coffee / phone / cake, "Kelime seç ve
yapıştır" / "Not yaz ve at" (the card) for the post-it / plane, one button for slurp / flatter / stand up. Below: the
planes on my table (sender and note, "Oku") and what happened this session (the whole match at Results).

*Wheel (G).* Eight slices around the manager's face and notches (1–8 or click; disabled slices grey with the reason in
red); the centre describes the hovered one. Free ones go at once (stand-up points at whoever you looked at when you
pressed G); coffee / phone / cake arm the aim; post-it / plane open the card (5 yellow word buttons, or a 60-character
note field), which also has name buttons. G, Esc, a right click or a click outside closes it. While aiming: "Kahve
fırlat: hedefine bak, tıkla ya da E · iptal: Esc / sağ tık" at the bottom, the hovered colleague's outline and name tag
("E / tık: Kahve fırlat", orange rim); E on nothing keeps aiming, Q / Tab give up.

*When it is me.* Coffee: a brown splatter over the screen for 1.5 s and a toast; cake: cream for ~2.6 s; plane: a toast
(look at the plane on your table and press E: the note on a paper card, a bot's note in your language); post-it: a
yellow "ALNINDA YAZIYOR: YALANCI" note under the strip for the session; phone: the ringtone and "Telefonun çalıyor!
Müdür sana kızdı.".

*Run screenshots* add `pranks` (the folder's tab) in the meeting discussion.

Scenarios: `v5.meeting_prank_menu` (the tab with a used and a cooling prank, the plane row, sealed in the defence ×2),
`v5.meeting_prank_wheel` (the wheel, a hovered slice, the post-it words, the plane note, aiming at Sarı),
`v5.meeting_prank_coffee` (in flight, the splash, Yeşil's line, coffee at me), `v5.meeting_prank_stain` (stains in
meeting 2, close up), `v5.meeting_prank_plane` (in flight, landed, the bot's line, a plane to me, the prompt, the note),
`v5.meeting_prank_postit` (post-its on two foreheads, the bot's line, mine), `v5.meeting_prank_phone` (a hidden ring:
panic, the phone, the bot's denial, the manager), `v5.meeting_prank_tutanak` (red gauge on the projector, the
overflowing coffee, the stamp, the board), `v5.meeting_prank_flatter` (the manager pleased and pointing, a stand-up,
a slurp), `v5.meeting_prank_cake` (results: the cake in flight, the cream, cake at me, the reveal tab),
`v5.meeting_prank_lobby` (the lobby checkbox, the how-to page), each with `_en`.

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
