# Birimiz Kovulacak — 2D UI art kit

The kit contains **183 final PNG assets plus one multi-resolution ICO** for the warm office satire direction, plus review sheets, source records and finishing scripts. The second run adds **34 PNGs + 1 ICO**; the original 149 PNGs are preserved byte-for-byte. [manifest.json](manifest.json) is the asset inventory; it excludes `_sources`, `_sheets`, `_tools` and category metadata. [validation.json](validation.json) records the final file audit. Start with the [second-run overview](_sheets/second_run.png), [complete overview](_sheets/index.png), or [icons at their actual 48px display size](_sheets/icons_48.png).

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
| `decals` | 8 | 512×768 posters/sign; other sizes in manifest |
| `howto` | 6 | 1280×720, text-free |
| `week` | 6 | Five 960×200 weekday banners; 512×512 trophy |
| `titles` | 10 | 256×256 round badges |
| `bubbles` | 8 | 256×256 speech/thought sprites |
| `app` | 1 + 1 ICO | 1024×1024 PNG; ICO at 16, 24, 32, 48, 64, 128, 256 |
| `store` | 3 | 920×430, 1232×706 capsules; 1920×1080 key art |

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

Illustrations were created with the built-in image-generation tool, using the locked style reference. Typography, buttons, device/meeting frames, paper tiling, compositing, crop/padding and PNG optimization use scripts. Prompts and source locations are recorded in `_sources/*prompts*.json` and the other category source ledgers. Some first-run generation originals live in the tool cache; selected cleaned/generated inputs are retained under `_sources`. Every second-run raw generation is archived locally. Final assets are local PNGs and the application ICO in the named category folders.

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

`kit.py` locates the workstation's existing Pillow installation. The validator rebuilds `manifest.json` and `validation.json`, checks all 184 expected entries and dimensions, verifies real alpha/corners and screen apertures, checks ghost centers and paper-tile seams, checks file-size budgets and all seven ICO entries, verifies the original 149 asset hashes, and requires every category review sheet plus removal of `_smoke/`. The `-B` flag prevents Python bytecode writes outside the art workspace. No Unity or repository-state commands are needed.

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
