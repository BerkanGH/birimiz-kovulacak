# Codex brief — v4 raster art (image generation)

You own the 2D art kit again ("Birimiz Kovulacak", `Art/UI/`, which you produced for v2/v3). Work only inside this git
worktree; do not commit (the lead commits). Use your **built-in image generation tool** (`imagegen` skill, default
mode). If it is unavailable, stop and report that — do not fake images, do not draw placeholders with code and call them
generated.

## Context
v4 replaces the six v3 rounds with twelve office **tasks** and adds evidence records and a company-productivity
result. Read `docs/GAMEPLAY_REDESIGN.md` §6 (task descriptions), §8–§9 (incidents, records, evidence), §3–§4 (company
closes / survives). Preserve the locked style of `Art/UI/README.md` (warm paper, manila frames, navy outlines,
restrained grain, the six bean characters `portraits/player_0..5.png`, rounded manila card frame of
`rounds/round_*.png`). Reuse your v3 finishing pipeline (`Art/UI/_tools/v3_cards_finish.py` and friends) for framing,
alpha, sizes and validation. **No baked text** (localisation stays in the UI); digits only if a task card truly needs
them.

## Deliverables (23 PNGs)
1. `Art/UI/tasks/task_<id>.png` — 12 cards, **512×716**, same frame and runtime title/body safe area `[52,552,408,104]`
   as `rounds/*`. One clear office-comedy scene per task:
   `invoice_reconcile` (two beans each holding half of a torn invoice), `budget_split` (beans reaching for slices of one
   small budget pie on a meeting table), `customer_spec` (product boxes in three colours/sizes, a customer silhouette
   with thought bubbles split between two beans), `report_review` (a report with a suspiciously tall bar chart, one bean
   with a magnifier, one with an APPROVE/REJECT rubber stamp pair — no letters on the stamps), `room_booking` (two beans
   colliding at one meeting-room door, wall calendar), `archive_visitor` (a bean with a visitor lanyard in an archive
   aisle carrying a limited stack of folders, others waving from outside), `bonus_pool` (two beans over one bonus
   envelope, one grabbing it all — fresh composition, do not copy the v3 card), `server_code` (server rack with a keypad
   and several hands each holding one digit card), `phishing_check` (a payment email on a monitor with a fishing hook
   through it), `shift_cover` (a weekend calendar with empty slots and beans pretending to be busy), `vendor_pick`
   (four supplier product samples on a table with score cards face-down), `supply_swap` (beans trading a stapler,
   toner and a chair across desks).
2. `Art/UI/evidence/evidence_<source>.png` — 6 record cards, **512×716**, same frame, for record sources:
   `phone_log` (desk phone + printed call log), `bank` (ledger book + coins), `task_system` (KOVA OS window with a
   checklist), `it_log` (server printout + coffee-stained keyboard), `badge_log` (badge reader + lanyard), `hr` (HR
   folder + rubber stamp). Keep the safe area empty.
3. `Art/UI/onboarding_v4/intro_<n>.png` — 3 wide illustrations **1280×720**, opaque paper background, for the
   first-launch visual intro: `intro_1_company` (all six beans pushing one big productivity gauge needle upward),
   `intro_2_secret` (one bean secretly reading a sealed envelope under the desk while on the phone), `intro_3_meeting`
   (meeting table, a record projected on the wall, beans pointing at each other).
4. `Art/UI/results_v4/` — 2 wide illustrations **1280×720**: `company_survived` (beans celebrating among desks, one
   empty chair with a cardboard box — someone may be fired), `company_closed` (empty office, boxes, chairs on desks,
   lights off — shared defeat, still comic).

## Process requirements
- Inspect the existing kit first (contact sheets in `Art/UI/_sheets` if present, `rounds/`, `portraits/`, `onboarding/`).
- Provide each generation with references (existing card/bean images via the tool's reference input where supported),
  intended use, exact canvas, framing, palette (from `Art/palette.json`), transparency rules (cards: transparent
  outside the rounded frame like `rounds/*`; wide illustrations: opaque).
- One candidate per asset; at most two bounded retries for a specific defect (wrong frame, text artefacts, off-model
  beans). Inspect every output yourself before accepting it.
- Save raw generations under `Art/UI/_tools/v4_sources/` (git-ignored pattern `*_sources/`), finished assets in the
  category folders, `_metadata.json` per new folder (same nine-field schema as `rounds/_metadata.json`), and append the
  new entries to `Art/UI/manifest.json`.
- Record for every asset: prompt, references, tool/model as reported by the tool, date, retries, post-processing →
  `Art/UI/_tools/v4_generation.json`. Write a contact sheet `Art/UI/_sheets/v4_tasks.png` (may stay untracked).
- Copy the finished PNGs into `Game/Assets/Game/UI/Resources/ui/tasks/`, `.../ui/evidence/`, `.../ui/onboarding_v4/`,
  `.../ui/results_v4/` (no `.meta` files — Unity creates them).
- Extend `Art/UI/_tools/validate_kit.py` (or add `v4_validate.py`) to check paths, sizes, alpha and metadata of the 23
  new assets; run it and report the result.
- Update `Art/UI/README.md` with a short "V4 delivery" section (counts, sizes, safe areas, known weaknesses).

## Handoff (final message)
List of produced files, the image tool/model actually used (as reported), retries and why, validation output,
anything that could not be produced, and integration notes (Resources paths, recommended display sizes).
