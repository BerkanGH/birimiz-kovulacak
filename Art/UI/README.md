# Birimiz Kovulacak — 2D UI art kit

The kit contains **307 final PNG assets plus one multi-resolution ICO**. The **v3 Office Line run adds 103 PNGs** for private offices, desk-phone conversations, computer rounds and the daily meeting. All 205 previous PNG/ICO assets are preserved byte-for-byte. [manifest.json](manifest.json) retains the original nine-field schema; [validation.json](validation.json) records the current file audit. Review sheets, source records and finishing scripts are excluded from the final asset count.

## V3 delivery — 103 new PNGs

The full [v3 design](../../docs/V3_DESIGN.md) governs these assets. Illustrations follow the locked style sheet and existing bean identities. Controls use the kit's native stationery construction: quiet paper centers, manila edges, navy outlines and restrained grain. Notifications and sticky notes have no colored side stripes. Required lettering uses deterministic Arial Bold; `İHTAR` preserves U+0130 and `A/Ö` preserves U+00D6. The requested Shared Password digit cards contain `4 1 7 2 9 6`; their digits are the sole lettering exception among otherwise text-free round/trick cards. All card title/body regions remain blank for runtime localization.

| New category | PNG count | Review sheet |
| --- | ---: | --- |
| `rounds` | 7 | [Cards at 180px](_sheets/v3_rounds.png) |
| `tricks` | 3 | [Cards at 180px](_sheets/v3_tricks.png) |
| `icons_v3` | 31 | [Full sheet](_sheets/v3_icons_v3.png), [actual 48px](_sheets/v3_icons_48.png) |
| `computer` | 9 | [Sheet](_sheets/v3_computer.png), [screen effects](_sheets/v3_computer_effects.png) |
| `deskphone` | 9 | [Sheet](_sheets/v3_deskphone.png) |
| `meeting_v3` | 8 | [Sheet](_sheets/v3_meeting_v3.png) |
| `onboarding` | 9 | [Sheet](_sheets/v3_onboarding.png) |
| `stamps_v3` | 11 | [Sheet](_sheets/v3_stamps_v3.png) |
| `logo_en` | 1 | [Sheet](_sheets/v3_logo_en.png) |
| `week_v3` | 5 | [Sheet](_sheets/v3_week_v3.png) |
| `decals_en` | 10 | [Sheet](_sheets/v3_decals_en.png) |

The remaining three dirty tricks reuse `cards/card_fake_email.png`, `cards/card_pull_plug.png` and `cards/card_internet.png`. New icon manifest IDs are `v3_<filename>`; localized decal IDs are `en_<filename>` to preserve globally unique IDs. Always resolve by manifest path/ID, rather than assuming every ID equals the filename.

### V3 slicing and layout

All borders below use **source pixels, `[left, top, right, bottom]`**. Unity expects `[left, bottom, right, top]`. Width must exceed left+right and height must exceed top+bottom. Leave more space for runtime text and hit targets.

| Asset | Canvas | Nine-slice borders |
| --- | --- | --- |
| `computer/window_frame` | 640×480 | `[40,104,40,40]` |
| `computer/taskbar` | 640×80 | `[26,28,26,30]` |
| `computer/notification_card` | 384×192 | `[36,36,36,36]` |
| `computer/app_tile` | 192×192 | `[40,40,40,40]` |
| `deskphone/lcd_frame` | 480×192 | `[44,44,44,44]` |
| `deskphone/key`, `key_pressed` | 128×96 | `[28,28,28,28]` |
| `meeting_v3/waveform_bar` | 384×96 | `[30,30,30,30]` |
| `meeting_v3/nameplate` | 512×160 | `[60,32,60,32]` |
| `meeting_v3/dossier_tab` | 384×144 | `[42,42,42,42]` |
| `onboarding/sticky_note_9slice` | 384×256 | `[32,32,40,40]` |

The title bar is protected by the window's 104px top border. Panels and LCDs contain opaque paper/display surfaces; they do not have transparent content apertures. Draw their content above the skin. Render the animated waveform over its empty tray. [Computer stretch review](_sheets/v3_computer_slice.png) and [prop stretch review](_sheets/v3_props_nineslice.png) show wide and compact examples.

Phone base, handset and ringing arcs share **800×600** canvases: stack all at **[0,0]**, with no independent trimming. Rotate/shake the handset around **[458,136]**, using a top-left origin. The left cord extends the base's silhouette, so the handset is centered on the cradle hardware rather than the canvas. Two cradle contacts were measured. The cassette deck is **1024×512**; render two copies of `tape_reel` at **100×100**, centered at **[262,214]** and **[497,214]**. Each 256×256 reel rotates around source pivot **[128,128]**. The deck has clear reel apertures, and measured aperture centers are within one pixel of those pivots. See the [layer composite](_sheets/v3_layers.png), [alignment data](_tools/v3_props_alignment.json) and [measured checks](_tools/v3_props_layer_checks.json).

The 64px cursor hotspot is **[27,7]**. Full-screen coffee/offline overlays use **1920×1080**, top-left origin [0,0]. The defense spotlight intentionally uses soft alpha **0–82**, with clear corners. Its translucency is an explicit exception to cutout alpha 0–255. Weekday label safe rectangle is **[209,49,675,76]**; card title/body safe rectangle is **[52,552,408,104]**. Rectangles use `[x,y,width,height]`.

### V3 checks and reproducibility

Run `python -B Art/UI/_tools/validate_kit.py --strict` (or the Blender interpreter shown below). The extended audit validates 308 final assets, including all 103 requested v3 paths and dimensions, real alpha, size budgets, exact metadata and eleven slice specifications, deterministic glyph records, source archives, layer checks and final review hashes. All 205 original file hashes are verified against [_tools/v3_baseline.json](_tools/v3_baseline.json).

The worktree does not include the ignored historical `_sources` archives or old contact sheets. Their previous successful audit is retained in [_tools/v3_prior_validation.json](_tools/v3_prior_validation.json), and their final assets are rechecked directly. `--legacy` runs the original archive audit only in a checkout containing those historical files. The current audit reports this archive limitation explicitly.

Rebuild scripts are `_tools/v3_cards_finish.py`, `v3_icons_finish.py`, `v3_icons_extra_finish.py`, `v3_props_finish.py`, `v3_localizations.py`, and `v3_computer.py`. New generated sources and prompt ledgers remain inside `_tools/v3_*`; all final project references use the category folders. [_tools/v3_review.json](_tools/v3_review.json) binds the visual review to final SHA-256 hashes. Rebuilding changed art requires a renewed visual review before refreshing that record.

No assets are missing. At 180px card height, Shared Password's crossed fingers are subtle; its six digit cards and keypad remain clear. Display the long `EMPLOYEE OF THE WEEK` stamp at roughly 400px wide or more for comfortable reading. Unity import, compression, animation and in-game camera checks remain for integration. No existing PNGs, Unity files, `.meta` files or git state were modified.

## Prior-run inventory (preserved)

| Category | PNG count | Primary sizes |
| --- | ---: | --- |
| `style` | 1 | 1600×1000 |
| `logo` | 2 | 1600×700 title, 512×512 mark |
| `backgrounds` | 4 | 1920×1080 scenes, 512×512 paper tile |
| `buttons` | 29 | 192×96 button states; panel/control sizes in manifest |
| `icons` | 63 | 256×256 |
| `cards` | 8 | 512×716 |
| `portraits` | 15 | 512×512 |
| `stamps` | 6 | 960×320 |
| `phone` | 8 | 600×1200 frame, 192×192 apps, 504×984 wallpaper |
| `meeting` | 5 | 1600×900 projector/board, 1280×720 CCTV, 1200×1600 letter, 800×600 folder |
| `decals` | 29 | 8 existing decals; 21 new wall textures at 308–768 px, exact physical aspect ratios |
| `howto` | 6 | 1280×720, text-free |
| `week` | 6 | Five 960×200 weekday banners; 512×512 trophy |
| `titles` | 10 | 256×256 round badges |
| `bubbles` | 8 | 256×256 speech/thought sprites |
| `app` | 1 + 1 ICO | 1024×1024 PNG; ICO at 16, 24, 32, 48, 64, 128, 256 |
| `store` | 3 | 920×430, 1232×706 capsules; 1920×1080 key art |

## Third art run — office wall posters and signs

The 21 new filenames match the decal spots in [`docs/MAP.md` §7](../../docs/MAP.md#7-decal-spots-posterssigns-for-the-2d-art-run). Each PNG is a straight-on, opaque printed-paper or framed texture: the rectangle contains the artwork, with no photographed wall or exterior backdrop. The memo board is 756×468 and the reception logo is 768×384; all other new textures have a long side of 495–512 px. Every pixel aspect ratio exactly matches the specified size in metres, with no stretching required on the wall.

The artwork uses the locked stationery palette, rounded beans, navy contours, simple shading and paper grain. The manager portrait deliberately uses oil-painting brush texture. The 19 required labels use Arial Bold with exact NFC Turkish Unicode lettering; the memo board and manager portrait are text-free. No small readable text is baked into memos, charts or the diploma. The salt sign retains the correctly rendered `≠` symbol.

| New decal | Pixels | Size (m) | Exact visible text |
| --- | --- | --- | --- |
| `poster_teamwork` | 360×510 | 0.6×0.85 | TAKIM RUHU |
| `poster_motivation` | 360×510 | 0.6×0.85 | BAŞARI = MESAİ |
| `poster_safety` | 360×510 | 0.6×0.85 | DİKKAT |
| `memo_board` | 756×468 | 1.05×0.65 | None |
| `poster_quota` | 504×360 | 0.7×0.5 | KOTA |
| `portrait_manager` | 392×504 | 0.7×0.9 | None |
| `diploma` | 495×374 | 0.45×0.34 | DİPLOMA |
| `poster_kpi` | 512×384 | 0.8×0.6 | KPI |
| `poster_mission` | 504×364 | 0.9×0.65 | VİZYONUMUZ |
| `poster_coffee` | 360×510 | 0.6×0.85 | KAHVE KURALLARI |
| `sign_salt` | 512×352 | 0.32×0.22 | TUZ ≠ ŞEKER |
| `poster_fridge_rules` | 384×504 | 0.32×0.42 | YOĞURDUMU YİYEN? |
| `poster_printer` | 384×512 | 0.6×0.8 | YAZICI: SABIR |
| `sign_out_of_order` | 504×308 | 0.36×0.22 | ARIZALI |
| `logo_company` | 768×384 | 1.3×0.65 | KOVA HOLDİNG |
| `poster_welcome` | 504×360 | 0.7×0.5 | HOŞ GELDİNİZ |
| `sign_fusebox` | 512×512 | 0.3×0.3 | YÜKSEK GERİLİM |
| `sign_wash_hands` | 384×512 | 0.3×0.4 | ELLERİNİZİ YIKAYIN |
| `sign_server_danger` | 512×512 | 0.35×0.35 | YETKİSİZ GİRİLMEZ |
| `poster_relax` | 504×360 | 0.7×0.5 | MOLA |
| `poster_vending` | 384×512 | 0.45×0.6 | ATIŞTIR! |

Review the [full decal contact sheet](_sheets/decals_v2.png) and [small foreshortened preview](_sheets/decals_v2_distance.png). The latter assumes 140 screen pixels per metre, 65% horizontal and 82% vertical scale; it is a comparative readability check, not a calibrated 5–7 m Unity render. At the smallest physical sign sizes, symbols remain clearer than multiword labels. Actual readability at 5–7 m still depends on camera projection, resolution, mipmaps and lighting and needs an in-game check.

All 21 raw generations and the full prompt sets are archived under [`_sources/decals_v2/`](_sources/decals_v2/): [group A prompts](_sources/decals_v2/prompts_a.json), [group B prompts](_sources/decals_v2/prompts_b.json), [group C prompts](_sources/decals_v2/prompts_c.json). [Finishing records](_sources/decals_v2/finishing.json) store exact text, codepoints, type size and glyph bounds. [Visual review](_sources/decals_v2/review.json) records the final reviewed file hashes. Generated art uses the built-in image-generation tool; frames, composition, final resizing and typography use deterministic finishing, following this kit's existing workflow. Rebuild with `_tools/decals_v2_finish.py`, then run the strict validator. Any changed PNG needs renewed visual review before the strict audit passes.

No Unity files, `.meta` files or git state were changed in this run. Import these new opaque textures using the office builder's decal settings: sRGB, mipmaps enabled, Clamp wrap, max size 1024 and the chosen platform compression. Existing screen-space sprite guidance below is separate from world decals.

## Second art run

The six how-to scenes teach shared quota, hidden sabotage cards, manager inspections and the tip line, private calls with physical eavesdropping, CCTV accusations, and the five-day trophy ceremony. All illustrations are text-free. The five blank board columns in `howto_6_week` represent Monday through Friday from left to right; trophy plaques are deliberately blank.

Week banner IDs are `week_pazartesi`, `week_sali`, `week_carsamba`, `week_persembe`, and `week_cuma`. Their pictograms follow the game plan: work, cards, call evidence, investigation, and the Friday trophy. Friday uses office green, gold and confetti. `week_trophy` is the text-free Ayın Elemanı award.

Title IDs match the ten requested end-of-match keys exactly. Badges use a round navy/gold rim with transparent corners. Bubbles have cream/navy balloons, consistent tails and distinct symbols; `!`, `?` and `ZZZ` are the only baked-in bubble lettering. Review badges and bubbles at 48px before choosing world-space scale.

Store capsules reuse the verified existing logo over a new HR scene with six accusing employees and the watching manager. The text-free key art is supplied separately. The header crops the lower table to retain all faces. The application icon keeps the blue bean carrying its box on an office-green rounded square. ICO manifest dimensions describe its largest 256px entry; `validation.json` lists all seven embedded sizes.

New illustrated sources use the **built-in image-generation tool**. Week banner layouts and Unicode lettering, store logo placement, final sizing and ICO encoding use deterministic finishing. Every raw generation from this run is retained locally under `_sources/`; prompt/provenance ledgers are listed below. No Unity import or store upload is included in this art run.

Review found no blocking visual weaknesses. At the smallest 16px app-icon size, the mug and plant details merge; the blue bean and cardboard-box silhouette remain readable. All 27 raw generations are archived. Turkish weekday names and all three dotted capital `İ` glyphs in the capsule title were independently reviewed. The final strict audit passes without errors or warnings.

## Locked style

Use [style/style_sheet.png](style/style_sheet.png) as the visual reference. The world is made from warm office stationery: manila folders, off-white paper, sticky notes, green equipment and distressed rubber stamps. Illustrations use rounded flat vector forms, medium navy contours, a restrained top-left highlight, simple bottom-right shading and subtle paper grain. Keep the controls crisp and quieter than the character illustrations.

| Role | Hex |
| --- | --- |
| Paper | `#F4EFE3` |
| Manila | `#E8C98A` |
| Primary / office green | `#2F6B4F` |
| Danger / sabotage / fired | `#D2382F` |
| Ink / outline navy | `#1F2A44` |
| Sticky-note yellow | `#FFE066` |
| Informant / HR teal | `#3BB9B0` |

Player slot order is fixed: **0 Mavi** — tall blue, tie; **1 Sarı** — round yellow, glasses; **2 Pembe** — pink, hair bun; **3 Yeşil** — short green, headset; **4 Turuncu** — orange, beanie; **5 Mor** — purple, curly hair and scarf. Managers are larger suited beans. Preserve accessories, face proportions and palette when adding expressions.

Icons should use one strong silhouette, minimal interior detail and consistent visual weight. Their final canvases are 256×256 with transparent breathing room; check legibility at 48×48. All cutouts have real transparency, including nonrectangular edges within their content bounds. The style sheet, scene backgrounds, how-to illustrations, store art, paper tile and call wallpaper are opaque by design.

## Text and localization

Most artwork is text-free so the game can overlay localized labels using a font. Required lettering was rendered deterministically with Turkish Unicode glyphs and visually reviewed in the logo, stamps and decals sheets. Do not replace dotted `İ` with `I`, or dotless `ı` with `i` when changing case.

| Asset | Exact visible text |
| --- | --- |
| `logo` | **BİRİMİZ KOVULACAK** |
| `stamp_kovuldu` | **KOVULDU** |
| `stamp_kurtuldu` | **KURTULDU** |
| `stamp_onaylandi` | **ONAYLANDI** |
| `stamp_reddedildi` | **REDDEDİLDİ** |
| `stamp_gizli` | **GİZLİ** |
| `stamp_ayin_elemani` | **AYIN ELEMANI** |
| `poster_takim_ruhu` | **TAKIM RUHU** |
| `poster_verimlilik` | **VERİMLİLİK** |
| `poster_sadakat` | **SADAKAT** |
| `poster_kahve` | **KAHVE** |
| `exit_sign` | **ÇIKIŞ** |
| `week_pazartesi` | **PAZARTESİ** |
| `week_sali` | **SALI** |
| `week_carsamba` | **ÇARŞAMBA** |
| `week_persembe` | **PERŞEMBE** |
| `week_cuma` | **CUMA** |
| Both store capsules | **BİRİMİZ KOVULACAK** |
| `bubble_exclaim`, `bubble_question`, `bubble_zzz` | **!**, **?**, **ZZZ** |

Cards provide a blank title/body safe area `[52,552,408,104]`; their illustration area is `[40,44,428,474]`, using top-left `[x,y,w,h]` coordinates. The empty frame has a transparent illustration opening. Letterhead, week-board headers, buttons and phone screens rely on game-rendered text. CCTV has a red recording dot and no baked-in timestamp or REC lettering.

## Unity import and layout

Import only the appropriate PNG paths listed in the manifest. `app_icon.ico` is a Windows application resource, not a Unity sprite; `store/` contains marketing exports. Suggested UI settings are **Sprite (2D and UI)**, Single sprite, Full Rect mesh, alpha transparency enabled, sRGB color, bilinear filtering, Clamp wrap and mipmaps disabled. Treat these as import guidance: **Unity integration, atlas behavior and in-game appearance have not been tested in this art run.** The lead's Unity import should generate `.meta` files and choose platform compression while checking alpha edges and small text. Scene decals may need different mipmap/material settings from screen-space UI.

Button skins are authored at 2×: a 192×96 texture corresponds to a 96×48 design control before canvas scaling. Use `Image.Type.Sliced` for the skins with recorded borders. Keep portraits, stamps, cards, toggles, slider knobs and frames at their aspect ratio; they are not nine-slice skins. Progress fills should be cropped/masked horizontally for the current value.

The manifest stores borders as **`[left, top, right, bottom]` in source texture pixels**. Unity's Sprite border/`Vector4` ordering is **`[left, bottom, right, top]`**. Convert the order when importing; do not halve the border numbers just because the artwork is at 2×. Apply the corresponding UI scale through the project's pixels-per-unit/canvas setup.

| Asset group | Manifest border `[l,t,r,b]` | Stretch constraint in source pixels |
| --- | --- | --- |
| All 16 button states | `[32,32,32,32]` | Width and height greater than 64 |
| `panel_paper` | `[32,32,32,32]` | Width and height greater than 64 |
| `panel_folder` | `[256,112,32,32]` | Width greater than 288, height greater than 144; protects the tab |
| `panel_sticky` | `[32,64,80,88]` | Width greater than 112, height greater than 152; protects the folded corner |
| `tooltip` | `[24,24,24,36]` | Width greater than 48, height greater than 60 |
| Three progress-bar skins | `[16,0,16,0]` | Width greater than 32; keep height at 32 |
| `slider_track` | `[12,0,12,0]` | Width greater than 24; keep height at 24 |
| Active/inactive tabs | `[24,24,24,24]` | Width and height greater than 48 |

These are geometric limits, not recommended readable control sizes. Scale those limits with the sprite; leave additional space for localized labels and hit targets. A stretched sample review is saved at [_sheets/controls_slice_review.png](_sheets/controls_slice_review.png).

Screen rectangles use a **top-left origin** and `[x,y,width,height]` in source pixels. Every pixel in each recorded rectangle has alpha 0.

| Frame | Screen rectangle | Rectangle with bottom-left origin |
| --- | --- | --- |
| `phone_frame.png`, 600×1200 | `[48,108,504,984]` | `[48,108,504,984]` |
| `projector_frame.png`, 1600×900 | `[86,100,1428,690]` | `[86,110,1428,690]` |

For other images, convert rectangle y using `imageHeight - y - height`. Place `call_screen_bg.png` in the phone aperture; it matches 504×984 exactly. Keep phone/projector content behind their transparent frame overlay. The paper tile has matching opposite edge samples; use Repeat wrap when using it as a tiled surface.

## Production and adding assets

Illustrations were created with the built-in image-generation tool, using the locked style reference. Typography, buttons, device/meeting frames, paper tiling, compositing, crop/padding and PNG optimization use scripts. Prompts and source locations are recorded in `_sources/*prompts*.json` and the other category source ledgers. Some first-run generation originals live in the tool cache; selected cleaned/generated inputs are retained under `_sources`. Every second- and third-run raw generation is archived locally. Final assets are local PNGs and the application ICO in the named category folders.

Second-run prompt and provenance records:

- [_sources/howto_prompts.json](_sources/howto_prompts.json)
- [_sources/titles_generation_records.json](_sources/titles_generation_records.json)
- [_sources/bubbles_app_prompts.json](_sources/bubbles_app_prompts.json)
- [_sources/second_run_root_prompts.json](_sources/second_run_root_prompts.json)
- [_sources/week_typography.json](_sources/week_typography.json)
- [_sources/store_layouts.json](_sources/store_layouts.json)

The validator verifies those generation archives and records their SHA-256 hashes in `validation.json`. Rebuild week/store exports with `_tools/second_run_root.py`; the other new categories retain their finishing scripts under `_tools/`.

1. Choose the category, final filename and exact dimensions; avoid baked-in text unless the design requires it.
2. Generate one illustrated asset at a time using the style sheet as a reference, requesting true transparency for cutouts. Prefer deterministic drawing for controls and font rendering for required Turkish text.
3. Remove accidental background/alpha noise, crop to the meaningful silhouette, then contain/pad to the final canvas. Preserve antialiasing and intentional cutouts. Use `_tools/kit.py` for finishing; retain the generation prompt and selected source record.
4. Inspect the asset over grey and paper backgrounds, including a 48px view for icons. Check every visible Turkish letter. Regenerate or make the art text-free if a glyph is wrong.
5. Add/update the category's `_metadata*.json` entry using the manifest's nine fields. When deliberately extending this kit, update the requested inventory and count assertions in `_tools/validate_kit.py`, plus this inventory table.
6. Rebuild the category contact sheet, review its palette/outline/scale consistency, then run the validator. Keep icons below **80,000 bytes** and backgrounds below **1,500,000 bytes**.

The manifest schema is an array of `{ "id", "path", "w", "h", "category", "nineSlice", "screenRect", "hasText", "description" }`. Paths are relative to the repository root. Both rectangle fields are `null` when not applicable.

Run validation from the repository root with Python and Pillow:

```powershell
python -B Art/UI/_tools/validate_kit.py --strict
```

On the production workstation, the Windows `python` alias could not launch; the available interpreter was:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/5.2/python/bin/python.exe' -B Art/UI/_tools/validate_kit.py --strict
```

`kit.py` locates the workstation's existing Pillow installation. The current validator rebuilds `manifest.json` and `validation.json` for all **308** final entries, using the v3 checks documented above. Its optional `--legacy` mode retains the historical 205-asset audit, including original 149 and pre-third-run 184 hashes, old source archives, paper-tile seams, ghost centers, decal physical ratios and historical review sheets; those ignored archives must be present to run that mode. The `-B` flag prevents Python bytecode writes outside the art workspace. No Unity or repository-state commands are needed.

## Review sheets

All category sheets use neutral mid-grey behind transparency and show filenames:

- [_sheets/index.png — complete kit overview](_sheets/index.png)
- [_sheets/icons_48.png — all 63 icons at 48px](_sheets/icons_48.png)
- [_sheets/style.png](_sheets/style.png)
- [_sheets/logo.png](_sheets/logo.png)
- [_sheets/backgrounds.png](_sheets/backgrounds.png)
- [_sheets/buttons.png](_sheets/buttons.png)
- [_sheets/icons.png](_sheets/icons.png)
- [_sheets/cards.png](_sheets/cards.png)
- [_sheets/portraits.png](_sheets/portraits.png)
- [_sheets/stamps.png](_sheets/stamps.png)
- [_sheets/phone.png](_sheets/phone.png)
- [_sheets/meeting.png](_sheets/meeting.png)
- [_sheets/decals.png](_sheets/decals.png)
- [_sheets/decals_v2.png — 21 third-run wall decals](_sheets/decals_v2.png)
- [_sheets/decals_v2_distance.png — small foreshortened readability preview](_sheets/decals_v2_distance.png)
- [_sheets/second_run.png — second-run overview](_sheets/second_run.png)
- [_sheets/howto.png](_sheets/howto.png)
- [_sheets/week.png](_sheets/week.png)
- [_sheets/titles.png](_sheets/titles.png)
- [_sheets/titles_48.png](_sheets/titles_48.png)
- [_sheets/bubbles.png](_sheets/bubbles.png)
- [_sheets/bubbles_48.png](_sheets/bubbles_48.png)
- [_sheets/app.png](_sheets/app.png)
- [_sheets/store.png](_sheets/store.png)

Additional subset, 48px and slicing review sheets are also in `_sheets/`.
