# Map v2 — office, props, lighting (map agent)

Owner: Claude sub-agent `map` (branch `v2/map`). Plan: `docs/V2_PLAN.md` §5 U1/U6. Contracts: `docs/CONTRACTS_V2.md`.

## 1. Pipeline (one source of truth, previewable without Unity)

```
Game/Assets/Game/Core/Contracts/OfficeLayout.cs   DefaultOffice.Create(): desks, stations, slots, rooms, cameras, route
        │ parsed by
Art/Scripts/map_layout.py ──► Art/Layout/office_v2.json   walls/openings, floors, rugs, room paint, every prop placement,
        │                                                  desk kit, lights, decal spots, signs (+ a copy of the parsed
        │                                                  DefaultOffice for staleness checks)
        ├─► Art/Scripts/build_architecture.py ──► Models/Architecture/OfficeShell.fbx, Skyline.fbx
        ├─► Art/Scripts/map_preview.py        ──► Art/Previews/map/*.png (EEVEE) + navcheck.png
        └─► Editor/ProjectBuilder.Scene.cs    ──► Scenes/Office.unity
Art/Scripts/build_props.py (+ map_props_office.py, map_props_rooms.py, map_kit.py) ──► Models/Props/*.fbx (131 props)
Art/Scripts/map_textures.py ──► Art/Textures/Screens/*.png (monitor states; build_props.py runs it)
```

Commands (from the repo root):

| What | Command |
|---|---|
| Everything for Unity | `powershell -File Tools/build.ps1 -Art` (runs build_props.py → screens, build_architecture.py → layout JSON + shell, then the Unity builder) |
| Layout JSON only | `python -I Art/Scripts/map_layout.py` |
| Map previews | `blender -b --factory-startup -P Art/Scripts/map_preview.py [-- --only top,office] [--fast] [--power-cut]` |
| Walkability check | `blender -b --factory-startup -P Art/Scripts/map_preview.py -- --navcheck` → `Art/Previews/map/navcheck.png` |
| Prop sheet | `blender -b --factory-startup -P Art/Scripts/build_props.py -- --previews` (+ `--each` for per-prop renders) |

**If someone edits DefaultOffice numbers, re-run `Tools/build.ps1 -Art`** (the Unity builder logs
`MAP JSON STALE: …` when the JSON no longer matches the contract).

Previews (lead: please look at these): `Art/Previews/map/top.png` (plan), `overview.png`, `office.png`,
`office_close.png` (player-camera distance), `manager_meeting.png`, `kitchen_copy.png`, `reception.png`, `lounge.png`,
`south_wing.png`, power cut: `office_powercut.png`, `office_close_powercut.png`, `south_wing_powercut.png`; monitor
states `screens.png`; walkability `navcheck.png`. Characters in previews are placeholder beans.

## 2. Floor plan (x −16..16, z −10..10; walls 2.8 m)

| Room | Rect | Floor / walls | Doors / openings |
|---|---|---|---|
| Müdür odası | −16..−8, 4..10 | dark wood planks, navy + wood wainscot, red rug; **glass** south + east walls | glass door x −10 (1.6 m) → open office |
| Toplantı odası | −8..2, 4..10 | wood planks, slate/pale green | door x −4.5 (1.6) |
| Mutfak | 2..10, 4..10 | green/white checker, mint tiles | door x 4.0 (1.8) |
| Fotokopi | 10..16, 4..10 | vinyl checker, manila/mustard | door x 12.0 (1.6) → reception |
| Açık ofis | −16..6, −5..4 | blue-grey carpet tiles, paper + office-green wainscot | low counter wall to reception with 2 gaps |
| Resepsiyon | 6..16, −5..4 | stone tiles, paper + deep green | low wall gaps at z −0.6 (2.0) and z 3.1 (1.8); door x 9 → lounge; glass entrance (closed) on the east wall |
| Depo | −16..−10, −10..−5 | concrete, greys | door x −13 (1.6) |
| Tuvalet | −10..−5, −10..−5 | bath tiles; 2 stalls (2.0 m opaque, raised 12 cm, 0.95 m gaps) | door x −6.2 (1.6) |
| Sunucu odası | −5..3, −10..−5 | raised-floor grid, dark greys | door x 1.6 (1.6) |
| Dinlenme | 3..16, −10..−5 | light wood, peach/orange | 2.4 m opening x 4.5 from the office, door x 9 from reception |

Outer walls are 0.3 m thick and sit outside the room rectangles; interior walls 0.14 m centred on the boundaries.
Windows (sill 0.9, top 2.35) on all outer walls except storage/server; high window in the restroom. No ceiling (the
orbit camera is above wall height); light comes from the sun through windows + room lights.

Open office: 6 cubicles (desk at `DeskPositions`, yaw 0, employee faces +Z). Cubicle = back panel behind the monitor
(2.06 m) + two side panels (1.68 m) at x ±1.0, **1.15 m tall** (characters are ~1.2 m beans), open towards the seat.
Aisles: north 1.84 m (manager stops z 3.2), centre 2.09 m (stops z −0.6), south 1.65 m, 2.94 m between cubicles.
The east part of the open office (x −1..6) is a hub: water cooler, notice board, huddle table, mobile whiteboard, sofa.

## 3. Layers, colliders, camera fade

| Objects | Layer | Collider | Notes |
|---|---|---|---|
| `OfficeShell/Wall_*`, `Header_*` (above doors), `Stall_*` | Walls (8) | BoxCollider = mesh bounds | `OccluderWall`; walls are chunked ≤ 3.2 m so only the pieces in front of the player fade |
| `Glass_*` (manager office) | Glass (9) | BoxCollider | blocks movement, not sight |
| `Low_*` (office/reception counter wall, 1.1 m) | Partitions (12) | BoxCollider | `OccluderWall` |
| Cubicle panels (`Partition` in each `desk.N`) | Partitions (12) | BoxCollider (prop bounds) | `OccluderWall` on the prop root |
| Furniture props | Furniture (10) | BoxCollider from renderer bounds (plants/lamps/coat rack: pot/pole footprint only) | `NavMeshModifier` = Not Walkable (no NavMesh on table tops) |
| Wall decor, desk clutter, chairs, stools, door frames, floors, rugs | — | none | |
| Floor | Default | one `FloorCollider` box over the whole building | |

`OccluderWall` (`Scripts/World/OccluderWall.cs`) implements feel's `IOccluderFadable`: `SetFaded(true)` swaps every
submesh to one shared translucent `M_WallGhost` (shadow caster pass kept, so sunlight patches do not jump);
`SetFaded(false)` restores the cached materials. Arrays are cached per submesh count — no allocations after the first
fade. Every opaque wall (outer walls included) has one, so feel's `CameraOcclusion` runs in strict mode.

## 4. Gameplay positions and `DefaultOffice` changes

The scene builder creates every slot/station anchor at its **exact** DefaultOffice pose (parented to the prop that
shows it); props are placed around those points. Numeric changes I made in `DefaultOffice.Create()` (no entity
added/removed, nothing else touched):

| Value | Before | After | Why |
|---|---|---|---|
| `st.delivery` position / yaw | (−11, 0, 6.4) / 0 | (−9.2, 0, 6.8) / 90 | The manager walks in a straight line home (−13, 8) → (−10, 5); the inbox cabinet stood on that line (he walked through it every raid). It now stands against the manager office's east glass wall, reachable through the door. |
| `slot.delivery` position / yaw | (−11, 0.95, 6.45) / 0 | (−8.32, 0.95, 6.8) / 90 | top of that cabinet (reports left, coffee right as before) |
| `NameplateOffset` | (−0.6, 0.76, 0.35) | (−0.55, 1.16, 0.5) | The nameplate now sits on a rail on top of the cubicle back panel: readable from the aisle where the manager inspects (and by passers-by) and from the seat; on the desk top it would be hidden behind the panel. Bots' nameplate stand point (desk + (−0.6, 0, 0.95)) is 0.45 m away; from the seat it is 1.46 m (< 1.8). |
| `st.cleaning` position | (−15.2, 0, −9.2) | (−14.9, 0, −8.9) | The stand-point was the closet's own corner spot (= the spray rack slot), 0.15 m inside the closet: no NavMesh within 1.2 m in the first Unity bake. It now sits 0.42 m out in front of the closet; `slot.sprayrack` stays on the shelf (the closet is placed from the slot). |

Slot pose tweaks in `OfficeWorld.SlotPose` (mine): hiding spots spread their 2 items sideways (x ±0.13) so both lie
in the cistern / planter / box top / cabinet; mail room items lie in a row on the mail counter.

## 5. World visuals (Scripts/World, all driven from `ClientState.View`, no per-frame allocations)

| Component | Shows |
|---|---|
| `DeskVisual` | drawer slide; dirt/spill; **unassigned desk** ("boş masa": monitor off, BOŞ card, no personal clutter, grey chair pushed in); **unplugged** (screen + power LEDs off, cable on the floor); **password-locked** (red screen + padlock); **internet down** ("no signal" colour bars on every PC); **screen session open** (desktop with e-mail window) vs **lock screen**; chair fabric in the owner's colour; monitor glow light during a power cut. Material changes only on state change. |
| `OfficeLighting` | `view.Office.PowerDown`: flicker, room lights + lamp surfaces off, sun ×0.12, ambient to night blue, 12 red emergency lights (pulsing) + emergency lamp lenses on; restores over 0.9 s. ScreenFx (feel) darkens post on top. |
| `RouterVisual` | st.router LEDs: green traffic flicker; outage → red/amber blink |
| `FuseBoxVisual` | st.fusebox: main lever flips down, LED red blink during a power cut |
| `CctvVisual` | REC light blinks on every `layout.Cameras` prop; off during a power cut ("kayıt yok") |
| `RackBlinker` | server rack LED strips blink; amber/red during an outage; dark without power |
| `ItemVisual` (mail) | envelope stripe + name label in the addressee desk owner's colour |
| `DecalSpot` | id + renderer of every poster spot (see §7) |
| `OfficeWorld` | ticks the devices; `TryGetItemVisual(id, out Transform)` for feel; attract view shows 4 assigned + 2 empty desks |

## 6. Lighting & performance

- One shadowed light (sun, soft, through the windows). 20 room point lights (no shadows, range 3–9 m), 12 emergency
  lights (off until a power cut), 6 monitor glows (power cut only), 2 alarm spots (raids). Typical active: sun + 20.
- Trilight ambient (warm), no baked GI (power cuts must switch lighting at runtime), no reflection probes.
- Props are merged in Blender to one body mesh per prop (one submesh per material) + driven sub-objects
  (`map_kit.DRIVEN`): 131 props, 59k tris for one of each.
- Scene estimate (from the layout): 251 prop placements (112 models) ≈ 144k tris, items ≈ 9k, shell ≈ 16k
  (131 objects), skyline 4 instances. Unique materials: palette-based (≈ 110 used by the map), SRP Batcher friendly.
- Everything that does not move is `BatchingStatic`; drawers, chairs, phone handset, fuse lever and steam are not.
  Renderers smaller than 0.45 m (desk clutter, wall decor) do not cast shadows.
- No occlusion culling: walls fade instead of hiding what is behind them (static occluders would cull rooms the
  camera looks into through a faded wall). Rooms are small; frustum culling + static batching is enough.
- The builder logs `MAP STATS renderers=… activeTris=… materials=… lights=…` — please paste it into PERFORMANCE.md.

## 7. Decal spots (posters/signs for the 2D art run)

`office_v2.json` → `decals`: 21 framed spots. The builder uses `M_PosterPlaceholder` (manila) unless
`Art/UI/decals/<id>.png` exists in the checkout that runs the builder; then it copies it to
`Game/Assets/Game/Art/Decals/`, imports it (sRGB, mipmaps, clamp, max 1024, compressed) and makes
`M_Decal_<id>` (URP Lit, alpha clip 0.4). Requested textures (aspect = size; ~512 px on the long side is plenty):

| id | size (m) | idea |
|---|---|---|
| poster_teamwork | 0.6×0.85 | "TAKIM RUHU" motivational poster, ironic |
| poster_motivation | 0.6×0.85 | "BAŞARI = MESAİ" style |
| poster_safety | 0.6×0.85 | office safety, rubber-stamp look |
| memo_board | 1.05×0.65 | memo sheets pinned on the notice board |
| poster_quota | 0.7×0.5 | quota chart going down |
| portrait_manager | 0.7×0.9 | framed portrait of "the manager" |
| diploma | 0.45×0.34 | framed diploma |
| poster_kpi | 0.8×0.6 | KPI slide |
| poster_mission | 0.9×0.65 | "VİZYONUMUZ" mission statement |
| poster_coffee | 0.6×0.85 | coffee rules |
| sign_salt | 0.32×0.22 | "TUZ ≠ ŞEKER" warning label |
| poster_fridge_rules | 0.32×0.42 | passive-aggressive fridge note |
| poster_printer | 0.6×0.8 | printer instructions |
| sign_out_of_order | 0.36×0.22 | "ARIZALI" |
| logo_company | 1.3×0.65 | company logo (reception) |
| poster_welcome | 0.7×0.5 | "HOŞ GELDİNİZ" |
| sign_fusebox | 0.3×0.3 | high-voltage warning |
| sign_wash_hands | 0.3×0.4 | wash-hands sign |
| sign_server_danger | 0.35×0.35 | "YETKİSİZ GİRİLMEZ" |
| poster_relax | 0.7×0.5 | lounge poster |
| poster_vending | 0.45×0.6 | vending machine ad |

Signs with text (room names over doors, MÜDÜR, POSTA, TESLİM, TEMİZLİK, KAYIP EŞYA, GEREKSİZ BELGELER, DUYURULAR,
MODEM) are TMP labels from `labels` in the JSON and in the builder.

## 8. Navigation (bots)

- Before baking, the builder sets the Humanoid agent to radius 0.35, height 1.3, climb 0.3 (the beans' controller is
  r 0.33 / h 1.2 / step 0.25; the default 0.5 radius would close the 0.95 m stall doors and make 1.6 m doors tight),
  voxel 0.1. Furniture and shell get `NavMeshModifier` Not Walkable, so table/counter tops are never NavMesh islands
  that `SamplePosition` could snap to.
- After baking the builder logs `NAV reachability: N ok, M problems` for every desk seat, station, slot, meeting seat,
  manager stop and the manager's home (path from the office centre).
- Unity-free check (`--navcheck`): colliders rasterised, inflated by 0.5 m (agent 0.35 + 0.15 voxel margin), door
  leaves excluded like in Unity, flood fill: **36/36 gameplay points reachable, 0 problems; the manager's straight route
  legs (home → stops → home) cross no prop or wall.** (The first version inflated by 0.35 m only and missed three
  props standing in doorways; the 0.5 m version reproduces exactly the six failures of that bake.)
- Unity bake (main, 2026-10-29): `Nav agent radius=0.35 height=1.3 climb=0.3`, **`NAV reachability: 69 ok, 0 problems`**.
  Fixes after the first bake (8 problems): shredder removed from the kitchen doorway, paper-box stack removed from
  inside the copy-room door, plant moved away from the server-room door, prop colliders ignore swung-open `Door`
  leaves (cleaning closet, meeting sideboard), `st.cleaning` moved out of the corner (§4).

## 9. Requests for the lead

1. **Run** `powershell -File Tools/build.ps1 -Art` (or `-Art -NoPlayer`), then screenshot the scene. Unity creates
   `.meta` files for the new scripts (`Scripts/World/OccluderWall.cs, OfficeLighting.cs, DeviceVisual.cs,
   RouterVisual.cs, FuseBoxVisual.cs, CctvVisual.cs, RackBlinker.cs, DecalSpot.cs`), the 85 new prop FBX files,
   `OfficeShell.fbx` and `Art/Textures/Screens/*.png`. I deleted the v1 wall kit (`Wall_1m`, `Glass_1m`, …),
   `Counter` and `PhoneTable` models with their metas.
2. Paste the builder's `Shell:`, `Lights:`, `Static flags`, `Nav agent`, `NAV reachability`, `MAP STATS`, `ANCHOR …`
   and `MAP JSON STALE` lines into your report (and tell me if any `ANCHOR OFFSET` / `NAV UNREACHABLE` appears).
3. **URP for low-end laptops** (ProjectBuilder.cs): rendering path **Forward+** (or Forward with
   *Additional Lights Per Object Limit* ≥ 6) so overlapping room lights and the 12 emergency lights are not dropped;
   additional lights per-pixel, **no additional-light shadows**, cast shadows only from the main light; shadow distance
   25–30, 2 cascades, 2048 map is fine; SSAO: keep but consider *Downsample* + lower radius, or disable on a "low"
   quality level; HDR on (emissive LEDs/monitors bloom).
4. ~~`M_PlayerColor4/5`~~ done on main (`#F4853C`, `#A872D0`); my builder only creates them if they are missing and
   uses the same values.
5. Rules LOS now includes Partitions (`UnityWorldQueries.OccluderMask`): partitions are 1.15 m and the low counter wall
   1.1 m, so the manager (eye 1.6 m → target 1.0 m) loses sight of someone right behind a panel — intended? If you
   want the manager to see over cubicles, drop Partitions from the rules mask (feel's avatar LOS can keep it).
6. Engine (Codex): with tips, the manager walks straight from (−10, 3.2) to the tipped desk's stop; for row-B desks
   that line crosses row-A cubicles (cosmetic clipping). Suggest routing tip visits through the existing aisle
   waypoints (e.g. go to (0.8, 3.2)/(−14.6, 3.2) and down the end aisles first).
7. `docs/ASSETS.md` still describes the v1 architecture kit; point it at this file.

## 10. Not verified without Unity

- Actual look under URP (colours, light intensities, bloom on LEDs/screens, ghost wall transparency, decal frames).
  Previews are EEVEE approximations of the same geometry/materials/lights.
- FBX import of the merged props and the shell (names of driven sub-objects; `check_driven` passes in Blender).
- NavMesh bake with the new agent settings (the serialized-settings write is logged; `NAV reachability` will tell).
- `OccluderWall` + feel's `CameraOcclusion` interplay at doors/corners; static batching with material swaps.
- TMP sign sizes/readability; mail label placement on the envelope.
- Frame time / memory (no Unity run); budgets above are geometry counts, not measurements.
