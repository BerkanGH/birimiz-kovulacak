# V4 art delivery

Delivered 2026-11-01: **23 new PNGs**, four category metadata files, 23 matching Unity Resources copies, an appended manifest, source/provenance ledger, finishing and validation tools, and visual-review records. Nothing is missing. No commit or Unity `.meta` file was created.

## Finished file inventory

All paths below are relative to `Art/UI/`. Each PNG also exists under `Game/Assets/Game/UI/Resources/ui/` with the same category and filename.

| Category | Files | Canvas |
| --- | --- | --- |
| `tasks/` | `task_invoice_reconcile.png`, `task_budget_split.png`, `task_customer_spec.png`, `task_report_review.png`, `task_room_booking.png`, `task_archive_visitor.png`, `task_bonus_pool.png`, `task_server_code.png`, `task_phishing_check.png`, `task_shift_cover.png`, `task_vendor_pick.png`, `task_supply_swap.png` | 512×716 each |
| `evidence/` | `evidence_phone_log.png`, `evidence_bank.png`, `evidence_task_system.png`, `evidence_it_log.png`, `evidence_badge_log.png`, `evidence_hr.png` | 512×716 each |
| `onboarding_v4/` | `intro_1_company.png`, `intro_2_secret.png`, `intro_3_meeting.png` | 1280×720 each |
| `results_v4/` | `company_survived.png`, `company_closed.png` | 1280×720 each |

Each category contains `_metadata.json` with the existing nine fields: `id`, `path`, `w`, `h`, `category`, `nineSlice`, `screenRect`, `hasText`, `description`. All previous 308 manifest entries remain in their original order; 23 entries are appended, for **331 total assets: 330 PNGs and one ICO**.

## Generation and inspection

- Tool actually used: built-in **`image_gen.imagegen`**. Its returned fields were `image_url` and `output_hint`; **no model identifier was reported**. No API/CLI fallback was used.
- Each initial generation received existing card/bean or onboarding references, intended use, target canvas, palette, framing, transparency and text constraints. Palette values came from `Art/palette.json` and the locked UI README. The six portrait references were combined into an inspected reference sheet because the tool accepts at most five reference files.
- One initial candidate per asset; **three bounded retries**, one each for the defects below. There were **26 successful image-generation/edit calls**. One input was rejected before generation for exceeding the reference-path limit; it produced no candidate and is recorded separately in the ledger.
- Every returned candidate was visually inspected. Final PNGs were inspected on the detailed card sheets and wide sheet, with a second card review at 180px height. Final SHA-256 values are bound to [_tools/v4_review.json](_tools/v4_review.json).
- Raw selected candidates, three rejected candidates and the bean reference sheet are saved in ignored `_tools/v4_sources/` (27 PNGs). Prompts, references and their hashes, returned tool hints, date, retries, source hashes, actual source dimensions, finishing details, final hashes and Resources paths are recorded in [_tools/v4_generation.json](_tools/v4_generation.json). The tool chose its raw generation dimensions; final normalization produces the exact required canvases without stretching.

| Asset | Retry reason | Accepted correction |
| --- | --- | --- |
| `task_server_code` | Yellow lacked glasses; pink hair was brown/red. | Restored round glasses and navy bun; preserved digits **4 1 7 2**. |
| `task_phishing_check` | An unrequested dollar glyph appeared on the payment coin. | Replaced it with a blank coin face. |
| `intro_1_company` | Pink's hair bun was brown. | Restored navy hair while retaining the six-bean gauge composition. |

Deterministic finishing follows the explicitly requested v3 pipeline. [v4_finish.py](_tools/v4_finish.py) imports `v3_cards_finish.frame()`, clears source alpha below 24, trims and proportionally fits each card illustration to at most 410×448, and places it above the protected text panel. It does not paint scene content. Wide scenes use a proportional center fit to 1280×720; the source aspect differs from 16:9 by less than 0.1%. PNG encoding is lossless RGBA. The existing frame and all earlier artwork are unchanged.

## Validation and reproduction

This machine's Windows Python aliases do not launch, so finishing and checks used the installed Blender Python and existing Pillow setup:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/5.2/python/bin/python.exe' -B Art/UI/_tools/v4_finish.py
& 'C:/Program Files/Blender Foundation/Blender 5.2/5.2/python/bin/python.exe' -B Art/UI/_tools/validate_kit.py --strict
```

The standalone entry point is `Art/UI/_tools/v4_validate.py --strict`. Validation is read-only for artwork and manifest; it writes [v4_validation.json](v4_validation.json). The standard validator dispatches to v4 once its baseline is present. A changed image requires fresh visual inspection and updated review hashes before validation can pass.

```json
{
  "passed": true,
  "expectedV4Count": 23,
  "presentV4Count": 23,
  "manifestAssetCount": 331,
  "countsByCategory": {"tasks": 12, "evidence": 6, "onboarding_v4": 3, "results_v4": 2},
  "checkCount": 1501,
  "priorFilesVerified": {"assetSha256": 308, "metadataSha256": 26, "unitySha256": 488},
  "retries": 3,
  "errorCount": 0,
  "errors": [],
  "warnings": []
}
```

Checks cover exact inventory, dimensions and PNG/RGBA formats; real card alpha and exact v3 silhouettes; untouched frame regions and blank runtime text panels; fully opaque wide scenes; unique manifest IDs/paths and exact nine-field category metadata; source/reference hashes; bounded retry history; final review hashes; established file-size budgets; and byte-identical Resources copies. All 308 pre-v4 final assets, 26 original metadata files and 488 existing Unity UI files match their captured baseline hashes. Historical ignored v1–v3 sources are not available in this worktree; no claim is made to regenerate them. Lettering absence and scene semantics were visually reviewed, not OCR-tested.

## Integration and review notes

- Load sprites using keys such as `ui/tasks/task_invoice_reconcile`, `ui/evidence/evidence_bank`, `ui/onboarding_v4/intro_1_company` and `ui/results_v4/company_survived`, with no extension. The task/evidence IDs match the v4 gameplay source keys. Only PNGs were copied into the new Resources folders; Unity should create `.meta` files during import.
- Import as Sprite (2D and UI), Single, Full Rect, sRGB, alpha transparency enabled, bilinear filtering, Clamp, mipmaps off for screen UI. Preserve aspect ratio; these illustrations are not nine-slice skins. Suggested max texture sizes: 1024 for cards and 2048 for wide scenes.
- Card source size is **512×716**; prefer **256×358 or larger** for a focused card, with **129×180** for compact overview. Illustration region is `[40,44,428,474]`; runtime title/body safe rectangle is **`[52,552,408,104]`**, using top-left `[x,y,width,height]`. At half scale, the title/body rectangle becomes `[26,276,204,52]`. It is opaque blank paper, not an aperture; render localized UI above it.
- Wide scenes are **1280×720**, opaque; display at **640×360 through 1280×720**. Their compositions occupy the canvas, so put intro/result copy in surrounding UI or an intentional overlay panel.
- Only `task_server_code` has baked digits. Those digits are illustrative, not the seeded task answer. Stamp faces, calendars, record windows, HR folders and other papers contain symbols/diagram rules rather than localized text. Evidence illustrations identify a record source and do not assert who committed sabotage.
- Known weaknesses: at 180px card height, the visitor badge, exact folder count, accounting rules and toner details are small. Wide closure art is intentionally dimmer than the survival scene. Unity editor import, platform compression and in-game contrast/camera checks remain for integration; no engine test was performed in this art-only run.
- Survival includes an empty chair and box; closure is an empty office with lights off, packing boxes and chairs on desks. The bonus envelope scene is newly composed with pink and purple beans and differs from the v3 blue/orange card.

Review sheets are local and git-ignored: [_sheets/v4_tasks.png](_sheets/v4_tasks.png), [_sheets/v4_evidence.png](_sheets/v4_evidence.png), [_sheets/v4_tasks_detail_1.png](_sheets/v4_tasks_detail_1.png), [_sheets/v4_tasks_detail_2.png](_sheets/v4_tasks_detail_2.png), [_sheets/v4_evidence_detail_1.png](_sheets/v4_evidence_detail_1.png) and [_sheets/v4_wide.png](_sheets/v4_wide.png). Keep the local ignored source archive for rebuilding this delivery.
