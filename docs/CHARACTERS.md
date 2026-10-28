# Characters v2 (owner: `characters` sub-agent)

Bean office workers and managers, built headless in Blender from code and wired into Unity from code. Art direction
follows the 2D portraits in `Art/UI/portraits` (UI art run): tall capsule beans, a big cream shirt collar, navy
outline, big outlined oval eyes with thick navy brows, one signature accessory per player; managers are bigger beans
wearing jackets.

Contact sheets: `Art/Previews/sheet_players.png` (front + 6 m game camera), `sheet_managers.png`,
`sheet_expressions.png`, `sheet_poses.png` (36 key poses), `sheet_manager_poses.png`,
`Art/Previews/characters/face_atlas_cells.png`. Individual renders go to `Art/Previews/characters/_frames/` (git-ignored).

## Pipeline

```
blender -b --factory-startup -P Art/Scripts/build_characters.py [-- --previews]     # ~5 s, ~100 s with previews
```

| Module | Role |
|---|---|
| `build_characters.py` | look tables (single source of truth), build, FBX export, tri budget check, `Art/character_looks.json` |
| `char_rig.py` | shared bones, character-space pose solver (2-bone IK + rubber-hose stretch), actions (cyclic loops) |
| `char_models.py` | bean surface math (`BeanShape`: arc-length face mapping), tubes, shells, surface slabs, face patches |
| `char_parts.py` | bodies, inverted-hull outlines, every accessory, hand props (modelled in pose, baked to rest) |
| `char_anims.py` | 35 player clips + 13 manager clips |
| `char_face_atlas.py` | numpy SDF rasteriser for the 512² face atlas |
| `char_preview.py`, `char_png.py` | Eevee previews, contact sheets, dependency-free PNG IO |

Outputs: `Game/Assets/Game/Art/Models/Characters/{Employee,Manager}.fbx`, `FaceAtlas.png`; archive copies in
`Art/Export/`; editable `Art/Blender/{employee,manager}.blend`; materials in `Art/palette_characters.json` (the world
palette `Art/palette.json` is untouched); `Art/character_looks.json` (looks, swap groups, clip list + loop flags,
tri counts) is read by `Editor/ProjectBuilder.Characters.cs`, so art and code cannot drift.

## Rig (shared by all looks; managers use the same names at 1.2 scale with wider shoulders)

`Root → Body → Head → Top`, `Body → Tail`, `Body → Arm.L → Hand.L`, `Body → Arm.R → Hand.R`, `Root → Foot.L/R`.
Body pivots near the floor, so its scale is whole-bean squash & stretch. `Top` wobbles buns / curls / hats, `Tail`
swings scarf tails. Characters face Blender +Y = Unity +Z. Generic animation type (no humanoid avatar).

## Looks

Slot = colour index = look. Scale is Unity (x, height, depth) on `ModelRoot`.

| # | Look | Bean | Accessories | Silhouette | Tris |
|---|---|---|---|---|---|
| 0 | Mavi | blue | collar, dark green tie | tall 1.08 | 2952 |
| 1 | Sarı | yellow | collar, round navy glasses | round 1.1 × 0.95 | 3140 |
| 2 | Pembe | pink | rounded collar, navy hair bun | standard | 3364 |
| 3 | Yeşil | green | collar, forest tie, headset + boom mic | short 0.9 | 3386 |
| 4 | Turuncu | orange | collar, orange tie, navy knit beanie | standard | 3688 |
| 5 | Mor | purple | big navy curls, striped teal scarf (swinging tail) | standard | 3922 |

| Profile | Manager | Bean / outfit | Accessories | Idle | Resting face | Tris |
|---|---|---|---|---|---|---|
| `paragoz` | Hasan Bey | golden, navy suit, gold tie | handlebar moustache, gold watch | hands on belly (`IdleCrossed`) | Smug | 3808 |
| `disiplinci` | Nermin Hanım | teal, green blazer | cat-eye glasses, tight bun, clipboard | clipboard (`IdleClipboard`) | Stern | 3780 |
| `kayirmaci` | Selin Hanım | coral, green blazer | sunglasses on head, yellow/teal silk scarf, gold earrings | hands on hips (`IdleHips`) | Cheerful | 3884 |
| `paranoyak` | Kemal Bey | violet, beige trench (popped collar, belt), green tie | black shades; 7° hunch | hands steepled (`IdleSteeple`) | Sneaky | 4016 |
| (other / empty) | classic | brown, dark suit, red tie | moustache | hands behind back | Stern | 3592 |

Budgets (V2_PLAN §8): player ≤ 4000 tris, manager ≤ 6000 — all met (the build prints `TRIS … OK`). Counts include
the navy outline hulls (low-res copies with flipped faces + unlit material, so no custom shader).
Textures: one 512² RGBA face atlas shared by every character (BC7, mips with coverage preservation). Everything else is
flat palette materials. One rig + one clip set per FBX; looks are accessory toggles + material swaps on one model.

## Faces

`FaceAtlas.png`: 4×4 cells of 128 px. Eyes (cells 0–7): neutral, happy, sneaky, shocked, angry, sad, blink, laugh.
Mouths (cells 8–15): neutral smile, happy grin, smirk, shocked O, angry teeth, sad frown, talk A, talk E. Two small
skinned patches (`FaceEyes`, `FaceMouth`) sit 4 mm above the head; transparent texels show the skin (eyelids).
Unity uses one alpha-clipped Lit material per cell (`M_FaceEyes_0..7`, `M_FaceMouth_0..7`, `_BaseMap` offset baked),
so changing a face is a `sharedMaterial` swap: SRP-batcher friendly, no property blocks, no allocations.

`FaceExpression`: Neutral, Happy, Sneaky, Shocked, Angry, Sad, Talking, Laugh, Smug, Stern, Cheerful, Nervous.
`FaceRig` adds random blinks (open eyes only) and lip flap (talk A / talk E / resting mouth at ~9 Hz).

## Animation

Player controller (`Generated/Characters/EmployeeAnimator.controller`), parameter `Speed` (m/s):

| State | Contents |
|---|---|
| `Locomotion` | blend Idle 0 / Walk 1.4 / Run 3.4 |
| `CrouchLoco` | blend CrouchIdle 0 / Sneak 1.7 |
| `CarryLoco` | blend CarryIdle 0 / CarryWalk 3.0 (arms forward) |
| `FiredLoco` | blend FiredIdle 0 / FiredWalk 1.3 (cardboard box with plant, mug, photo) |
| activities | Typing, WorkPose, PhoneStand, Cleaning, Rummaging, Fiddling, Brewing, Delivering, Placing, Spilling, Drinking, Plugging, ReachUp |
| meeting | Sit, SitTalk (seat height ~0.48 m), Talk (standing gestures while speaking on voice) |
| reactions | Shocked (one-shot), Panic, Cheer |
| one-shots | Idle2 (bored stretch, random after ~7–16 s idle), Wave, Point, Facepalm, Shrug, Laugh |
| layer `Phone` | PhoneArm on a right-arm + head mask; weight driven by code (personal calls while walking) |

`PublicActivity` → state: Typing→Typing, OnPhone→PhoneStand (+desk handset), Cleaning, Rummaging, Fiddling,
Spilling, Brewing, Delivering, Placing/PickingUp→Placing, Plugging, ServerWork→Fiddling, FuseWork→ReachUp,
Drinking, **OnCall**→phone layer + mobile prop (PhoneStand when standing still). Sneaky face for rummaging,
fiddling, spilling, plugging and while crouched. Carried items follow the chest (bobbing with the body) and move to the
right hand for cleaning / spilling / brewing / drinking.

Manager controller: Idle, IdleCrossed, IdleClipboard, IdleSteeple, IdleHips, Walk, Inspect (clipboard shown for
every profile), Angry, Pleased (positive observations: WorkingHard, TipClean), Talk (meeting, lip flap), Point
(verdict), Sit, SitTalk. Face: Sneaky while inspecting, Angry/Happy on reactions, lip flap while a bubble shows.

## Unity API

```csharp
// PlayerAvatar : NetworkBehaviour, IAvatarFeel   (also keeps All, Local, Slot, BySlot, Drive, FaceTowards,
//                                                 Teleport, CurrentSpeed, CarrySocket, NameTag, PublicState)
void SetExpression(FaceExpression e, float seconds = 0)  // seconds > 0 = temporary override
void SetTalking(bool talking)                             // voice: mouth flap (+ talk gestures when idle)
void PlayEmote(EmoteId emote)                             // local only; feel calls it once per emote on every peer
void PlayShocked()
void SetCrouched(bool crouched)                           // feel: local immediate, remote from OwnerNetworkTransform
void Squash(float amount)                                 // spring on ModelRoot (stop / land)
bool IAvatarFeel.SetVisibility(float a) => false          // AvatarVisibility toggles our renderers

// ManagerVisual
void SetProfile(string profileId)  // also read automatically from PlayerView.ManagerProfileId every Apply()
void SetExpression(FaceExpression e, float seconds)
```

Prefabs: `Prefabs/PlayerAvatar.prefab` (network; all 6 looks inside, chosen by slot), `Prefabs/Manager.prefab`
(5 looks, chosen by profile), plus visual-only look prefabs `Prefabs/Characters/Player_<Look>.prefab` ×6 and
`Manager_<profile>.prefab` ×5 (only that look's meshes, resting face, animator; for lobby / briefing / results UI).
Animator culling `CullUpdateTransforms`, `updateWhenOffscreen = false` with generous fixed bounds.

## Steps for the lead

1. `powershell -File Tools\build.ps1 -Art -NoPlayer` (or just the Unity part: the FBX/atlas are committed). The
   Blender step prints `CHARACTERS_DONE`; the Unity log prints `[Builder] Characters: avatar (6 looks), manager (5 looks)`.
2. First import creates `FaceAtlas.png.meta` (new binary asset) — commit it with the build.
3. Screenshot checklist: 6 avatars in the office (looks per slot), carry pose + item position, a phone call while
   walking (mobile at the ear), crouch-walk, an emote, the meeting (seated beans on the chairs), the fired player in
   Results holding the box, each manager profile.

## Requests for the lead

- `Editor/ProjectBuilder.cs` → `ConfigureModelImports`: for paths under `/Characters/` call
  `ConfigureCharacterModel(path, clipsJson)` (or simply `continue`) instead of the generic character branch; the
  `LoopClips` list there is v1 and causes one redundant reimport of both FBX per build (harmless, just slow).
- `Editor/ProjectBuilder.cs` → `CreateMaterials`: add `M_PlayerColor4` (Turuncu `#F4853C`) and `M_PlayerColor5`
  (Mor `#A872D0`); consider the portrait-matched player colours `#2F8FDE #F6CF55 #F06B88 #3C9A6C #F4853C #A872D0`
  (also `Art/palette.json` "players", which still lists 4).
- Results: the fired avatar plays `FiredLoco` (box in hands) — if feel/lead walks that avatar toward the exit during
  Results, it becomes the full walk-out; standing still it holds the box and sighs.
- Meeting: `Sit` assumes the avatar root stands at the chair position (seat top ~0.48 m), as `OnPhaseChanged`
  teleports today. If the map moves chairs, keep `MeetingSeats[i].Position` = chair centre.
- `ManagerVisual.Exclamation` has no bubble text for the v2 observation kinds (PersonalCall, InappropriateEmail,
  SaltyCoffee, TipClean, TipFound) and shows "!" — needs loc keys from ui (`manager.bubble.call`, …).

## Not verifiable without Unity

Clip import names/loop flags, blend-tree feel at real speeds, bone-mount offsets (chest/hand, computed from the bind
pose at build time), the phone-layer mask, alpha-clip face materials under URP, outline hulls with SSAO, look
switching at runtime, and the face patch orientation (checked to be non-mirrored by reasoning about the axis
conversion, the same one the props use).
