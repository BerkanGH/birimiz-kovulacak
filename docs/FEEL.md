# Feel — camera, line-of-sight visibility, input, audio, VFX (v2)

Owner: Claude sub-agent `feel` (branch `v2/feel`). Plan: `docs/V2_PLAN.md` §1 pillar 2, §5 U2/U6, §8.
Everything below compiles today (`python -I Tools/scripts/compile_check.py` → `COMPILE OK`, also `--player`) and needs
**no scene/prefab change**: the components install themselves (see "Self-installation").

## Files

| File | What |
|---|---|
| `Scripts/Player/CameraRig.cs` | close follow camera, zoom, crouch dip, juice API (Shake/Punch/FovKick), meeting/results shots from the layout, audio listener on the avatar head |
| `Scripts/Player/CameraOcclusion.cs` | fades walls between camera and avatar (`IOccluderFadable`), keeps the camera out of solid walls |
| `Scripts/Player/AvatarVisibility.cs` | line-of-sight visibility of remote avatars (+ carried item, name tag) |
| `Scripts/Player/LocalPlayerController.cs` | movement, crouch, interactions (E/F/G), emotes, call keys, email-template hand-off, progress |
| `Scripts/Player/GameInput.cs` | bindings (remappable) |
| `Scripts/Player/OwnerNetworkTransform.cs` | owner-authoritative transform + replicated crouch flag |
| `Scripts/Player/FeelInterfaces.cs` | `IAvatarFeel` (characters), `IOccluderFadable` (map), `IFeelUi` (ui), `FeelHooks`, `FeelLayers` |
| `Scripts/Player/EmoteWheelGui.cs` | IMGUI emote wheel fallback (only until the UI draws one) |
| `Scripts/Audio/Sfx.cs` | sound API: 2D, pooled 3D with wall low-pass, keyed loops, manager voice, volume categories |
| `Scripts/Audio/SfxSynth.cs` | procedural synthesis of every clip (22.05 kHz mono) |
| `Scripts/Audio/Ambience.cs` | AC hum + distant typing; meeting duck, power-cut hum stop |
| `Scripts/Audio/Footsteps.cs` | footstep cadence for avatars and the manager; squash-on-stop impulse |
| `Scripts/Fx/FxPool.cs` | pooled code-built particles, "!" pops, spark flash light |
| `Scripts/Fx/ScreenFx.cs` | URP volumes: power-cut blackout, CCTV look |
| `Scripts/Fx/FeelDirector.cs` | events + view state → sounds, VFX, camera juice, expressions/emotes/crouch poses |

Self-installation (no ProjectBuilder change): `CameraRig.Awake` adds `CameraOcclusion` and moves the `AudioListener`;
`LocalPlayerController.Awake` adds `AvatarVisibility` + `EmoteWheelGui`; `Sfx.Awake` adds `Ambience`, `Footsteps`,
`FeelDirector`, which adds `FxPool` + `ScreenFx`. Bot / batch-mode processes skip clips, FX, visibility and the listener move.

## Controls (defaults, all remappable except 1–5)

| Key | Action | Notes |
|---|---|---|
| WASD / arrows | move | camera-relative |
| mouse / wheel | orbit / zoom 2.2–6 m | zoom persisted (`settings.camera_distance`) |
| E / F | first / second option of the focused target | v2 verbs come from the engine's affordances |
| **G** | use held item: *Tadına bak* (taste a full mug) | `LocalPlayerController.HeldOption` |
| Q / R | drop / work pose | unchanged |
| **Left Ctrl (hold) / C (toggle)** | crouch = sneak (1.7 m/s, silent footsteps for others, shorter witness range) | replaces v1 "slow walk"; `GameInput.Slow` is an alias of `Crouch` |
| **T (hold) + mouse direction** | emote wheel, release to send (centre = cancel) | wheel order: wave, point, facepalm, shrug, laugh |
| **1–5** | quick emotes (same order) | also in meeting/results unless a text field has focus |
| **Y / N** | answer / decline-cancel-hang up | works with the phone open; Briefing..FinalInspection |
| Tab | phone | unchanged (UIRoot) |
| **V** | push-to-talk | defined as `GameInput.PushToTalk` for the voice module; nothing else reads it |

## Camera

Defaults: distance 3.6 m (2.2–6), pitch 24° (4–62°), focus 1.05 m above the feet (0.7 m crouched, eased), focus
damping 0.07 s, rotation undamped. Walls between the head and the camera are **faded, not avoided**: one
`SphereCastNonAlloc` (r 0.25, Walls+Partitions) and one ray to the body (Partitions) per frame.
- Collider with `IOccluderFadable` (map's `OccluderWall`, found on the collider's children or parents) → `SetFaded(true)`,
  restored 0.25 s after it stops occluding.
- Scene without any `IOccluderFadable` (today's v1 scene) → fallback: the collider's renderers go `ShadowsOnly`
  (renderers bigger than 14 m are never hidden).
- Once `OccluderWall` exists anywhere, walls (layer 8) *without* one are solid: the camera snaps in front of them
  (eases back out).
Juice: `CameraRig.I.Shake(trauma 0..1)`, `Punch(localDir, amount)`, `FovKick(degrees)`; all scaled by
`settings.camera_shake` (0..1, `CameraRig.I.JuiceScale`). Meeting shot: from the seats' centroid away from
`ManagerMeetingSpot`, clamped inside the meeting room; results sway around the fired player inside the same room.

## Line-of-sight visibility (`AvatarVisibility`)

- Applies in Briefing..FinalInspection. Menu, meeting, results, abandoned, or no local avatar (spectating) → everyone visible.
- Ray from the local eye (1.5 m; 0.95 m when crouched) to the target chest (1.2 m; 0.6 m crouched) **or** head (+0.3 m),
  blocked by `Walls` (8) and `Partitions` (12); glass (9) does not block. Range 45 m, **4 m during a power cut**.
- 0.2 s fade, hide only after 0.3 s without sight, 3 avatars ray-tested per frame round-robin; new avatars are checked immediately.
- Hidden = every renderer under the avatar (`forceRenderingOff`, so no shadow leak) + the carried item's visual + the
  name tag (`TMP alpha` fades smoothly). If the avatar implements `IAvatarFeel.SetVisibility` and returns true, it fades itself.
- The manager is never hidden. Voice and sound are not affected (footsteps and ringtones are still heard, wall-muffled).
- Queries: `AvatarVisibility.IsVisible(slot)`, `AvatarVisibility.Alpha(slot)`, `AvatarVisibility.CanSee(worldPoint)`.

## Sound (procedural, `Sfx`)

API: `Sfx.Play(name, vol)` 2D · `Sfx.PlayAt(name, pos, vol, pitch, spatialBlend)` pooled 3D (12 voices, low-pass 900 Hz
×0.6 when a wall is between the listener and the source) · `Sfx.LoopAt(key, clip, pos, vol, spatial)` / `StopLoop` ·
`Sfx.ManagerVoice()` · `Sfx.Play("manager_hm")` is routed to the current profile's voice at the manager.
Categories: `Sfx.Volume` (master), `Sfx.EffectsVolume` (`settings.sfx_volume`), `Sfx.AmbienceVolume` (`settings.ambience_volume`).

| Clip | Trigger (who plays it) |
|---|---|
| `ringtone` (3D loop at the ringing player; 2D for yourself) | `PlayerPublicView.Ringing` — FeelDirector |
| `tip_dial` then `ringback` loop | my call `RingingOut`; `tip_dial` also when my TipManager starts — FeelDirector |
| `call_connect` / `call_end` | `CallStarted` / `CallEnded` — FeelDirector |
| `email_ding` | `EmailSent` — FeelDirector |
| `spark` (3D) + sparks VFX | `PlugPulled` — FeelDirector |
| `plug_in` (3D) | `PlugRestored` — FeelDirector |
| `internet_down` / `router_restore` | `InternetDown` / `InternetRestored` — FeelDirector |
| `power_down` (thunk + falling hum) / `power_up` (fuse) | `PowerDown` / `PowerRestored`; ambience hum winds down — FeelDirector, Ambience |
| `salt_shake` (only the salter hears it) | my `SaltCoffee` interaction starts — FeelDirector |
| `slurp` (3D) | any player's activity becomes `Drinking` — FeelDirector |
| `bleh` | private `CoffeeTasted` with salt — FeelDirector |
| `mail_drop` (3D at the mail room) | `MailArrived` — FeelDirector |
| `step_0..2`, `mgr_step_0..1` (3D) | distance cadence; crouched avatars are silent to others — Footsteps |
| `mgr_hmm_<profile>` (paragoz falling grumble, disiplinci "tsk-hm", kayirmaci sing-song, paranoyak "hm? hm?") | observations (via HUD's `manager_hm`), `ManagerTipReceived` |
| `inspection_sting` | `SurpriseInspection` — FeelDirector |
| `caught` ("dun-dun") + camera punch | an observation with positive severity on me — FeelDirector |
| `vhs` + CCTV screen look (3 s) | `FootageShown` — FeelDirector |
| `gavel` | phase → MeetingDiscussion, MeetingVerdict — FeelDirector |
| `stamp` + ink burst, `confetti` | Results +1.2 s (or the UI's stamp moment, see ui) — FeelDirector / `FeelHooks.ResultsStamp` |
| `splash` / `dust` (3D) + coffee splash / dust puff VFX | a desk's `Spill` turns on / `ClutterCount` rises (view diff) — FeelDirector |
| `emote_pop` (3D, only if visible) / `wheel_tick` / `crouch` | emote start / wheel selection / crouch toggle |
| `ambience_hum`, `ambience_keys` (2D loops) | always; ×0.3 in the meeting, ×0.2 in results, hum off in a power cut — Ambience |

**Ownership split (nothing plays twice):** FeelDirector plays every row above. The HUD keeps its v1 UI sounds
(`complete`, `evidence`, `toast`, `place`, `vote`, `phase`, `deny`, `pickup`, `manager_hm`, `fired`, `click`) and must
not add sounds for the events in the table.

## VFX (`FxPool`, `ScreenFx`)

`FxPool.Play(FxKind.CoffeeSplash | Sparks | DustPuff | Confetti | StampBurst, pos[, rot])`,
`FxPool.StampBurstAtScreen(px)`, `FxPool.ConfettiAtScreenEdges()`, `FxPool.Exclaim(pos)` ("!" pop),
Triggers: a desk's `Spill` false→true → coffee splash + `splash`; its `ClutterCount` rises → dust puff + `dust`
(both from view diffs, never on the first view of a match); `PlugPulled` → sparks + flash light; Results → stamp burst +
confetti (not when you are the one fired). `FxPool.SetSweat(on, headPos)` (local only: manager patrolling within 6 m while I carry a suspicious item / someone
else's nameplate / a mug I know is salted), `FxPool.SetSaltSparkle(on, pos)` (only the salter/taster ever has a
`KnownSalted` mug; never shown on a carrier you cannot see). `ScreenFx.SetBlackout(on)` (state-driven from
`view.Office.PowerDown`, flickers on), `ScreenFx.Cctv(seconds)`, `ScreenFx.SetCctv(hold)`.
One-shot world FX for public actions are gated by `AvatarVisibility.CanSee(point)` (you hear a spark through the wall, you
do not see it). The "!" pops when `ManagerView.ReactionObservation` changes. The particle material is cloned at runtime
from the scene's existing particle material (coffee steam, URP Particles/Unlit), so the shader is always in the build.

## Interfaces other modules implement

### characters — `IAvatarFeel` (on `PlayerAvatar` or any component on the avatar root)

```csharp
public sealed class PlayerAvatar : NetworkBehaviour, IAvatarFeel
{
    void IAvatarFeel.SetExpression(FeelExpression e) { SetExpression(/* map to your face enum */ e); }
    void IAvatarFeel.SetTalking(bool talking) { SetTalking(talking); }
    void IAvatarFeel.PlayEmote(EmoteId emote) { PlayEmote(emote); }
    void IAvatarFeel.SetCrouched(bool crouched) { _crouched = crouched; }   // sneak walk / crouch idle
    bool IAvatarFeel.SetVisibility(float alpha) { return false; }           // or dither-fade + return true
    void IAvatarFeel.Squash(float amount) { _squash = Mathf.Max(_squash, amount); } // e.g. ModelRoot (1+k, 1-k, 1+k), decay ~8/s
}
```
- Calls are change-only (expressions at ≤ 5 Hz), cosmetic and must not allocate.
- `PlayEmote` is called **once per emote**, driven by `PlayerPublicView.Emote/EmoteUntil` — do not also trigger emotes
  from the view. `SetCrouched` for remote avatars comes from the replicated flag (faster than the 10 Hz view); for the
  local avatar it is immediate. Prefer it over `PlayerPublicView.Crouched` (or read `OwnerNetworkTransform.IsCrouched(gameObject)`).
- Expressions pushed: Shocked (manager reacted to that player / salted coffee, 2 s), Happy (task done, positive
  observation, results winner), Nervous (manager patrolling within 5 m while carrying something suspicious), Sad (fired), Neutral.
- `SetTalking` is called by the voice module through `FeelHooks.SetTalking(slot, talking)`.
- Please keep `PlayerAvatar.NameTag` (a TMP: AvatarVisibility fades its alpha), `All`, `Local`, `Slot`, `BySlot`, `Drive`,
  `FaceTowards`, `CurrentSpeed`; if they change, tell feel/lead. Bubbles (emote/ringing) parented under the avatar are hidden
  with it automatically; screen-space ones must use `AvatarVisibility.Alpha(slot)`.

### map — `IOccluderFadable`

```csharp
public sealed class OccluderWall : MonoBehaviour, IOccluderFadable
{
    public void SetFaded(bool faded) { /* swap to the cut-away/transparent material or ShadowsOnly */ }
}
```
- Put it on the wall/partition renderer object; its collider may be on the same object, a parent or a child.
- Layers: opaque walls 8 (`Walls`), partitions 12 (`Partitions`), glass 9 (blocks nothing). Exterior walls that should never
  fade: no `OccluderWall` → the camera is pulled in front of them instead.
- Used from `World/**`: `OfficeWorld.I.Manager` (+ `ManagerVisual.Bubble.font` for the "!"), `OfficeWorld.Anchor(id)`,
  `OfficeWorld.TryTargetPosition`, item visuals pooled as children of `OfficeWorld` named by item id (if you add
  `OfficeWorld.TryGetItemVisual(id, out Transform)`, tell me and I swap `AvatarVisibility.FindItemVisual`).
- Keep at least one ParticleSystemRenderer with a URP particle material in the scene (the coffee steam) — FX clone it.
- Power cut lighting (emergency lights, dimmed fixtures) is yours: drive it from `view.Office.PowerDown`; `ScreenFx`
  already darkens and vignettes the post-processing.

### ui — `IFeelUi` + reading feel state

```csharp
public sealed class UIRoot : MonoBehaviour, IFeelUi
{
    void Awake() { FeelHooks.Ui = this; /* ... */ }
    public bool TextInputFocused => /* a TextField has focus */ false;
    public bool DrawsEmoteWheel => true;   // false keeps the IMGUI fallback
    public void PickEmailTemplate(string pcTargetId, System.Action<int> onPicked)
    {
        // open the template picker (Email.TemplateCount, loc "email.<i>.subject"); exactly one callback:
        // onPicked(index) or onPicked(-1) on cancel. The picker is modal (BlocksGameplay).
    }
}
```
- Prompts: `LocalPlayerController.I.Options` (E/F, unchanged) **plus** `HeldOption` on `GameInput.UseItem` (G,
  `verb.taste_coffee`). Progress radial: `InteractionProgress` (0..1, −1 idle), `InteractionVerb`, `InteractionRemaining`.
  `AwaitingEmailTemplate` while your picker is open. `Crouched` for a HUD icon.
- Emote wheel state: `EmoteWheelOpen`, `EmoteWheelSelection` (index into `LocalPlayerController.WheelEmotes`, −1 = cancel),
  `EmoteWheelCursor` (px, +y up, radius `EmoteWheelRadius`).
- Any per-player overlay in the office (names, ringing/speaking icons, the coworker panel's *activity* text) must be
  hidden/faded with `AvatarVisibility.Alpha(slot)` — otherwise the HUD leaks what LOS hides.
- Sounds: do not play sounds for the FeelDirector rows above. Results: set `FeelHooks.UiDrivesResultsStamp = true` and
  call `FeelHooks.ResultsStamp(screenPos)` when your stamp lands (default: auto at +1.2 s). Footage viewer:
  `ScreenFx.SetCctv(true/false)` while it is open.
- Digits 1–5 send emotes in meeting/results (unless `TextInputFocused`); if the meeting UI wants digit shortcuts, tell feel.
- `LocalPlayerController` calls `Hud.Toast(...)`/`Loc.Failure` for blocked options: keep those or tell feel.
- Settings: new `GameInput.Rebindable` rows; sliders for `Sfx.EffectsVolume`, `Sfx.AmbienceVolume`,
  `settings.camera_shake` (`CameraRig.I.JuiceScale`).
- Loc keys needed: `input.crouch`, `input.crouch_toggle`, `input.emote`, `input.answer_call`, `input.hang_up`,
  `input.use_item`, `input.push_to_talk`, `emote.wave`, `emote.point`, `emote.facepalm`, `emote.shrug`, `emote.laugh`,
  `verb.taste_coffee`; and change `hint.slow` text to crouch ("Eğil / sinsi yürü: {0}") or add `hint.crouch`.

### voice

- Push-to-talk: read `GameInput.PushToTalk.IsPressed()` (V, remappable, listed in settings). Nothing else is bound to V.
- The `AudioListener` is now its own GameObject ("AudioListener") at the local avatar's head with the camera's yaw in
  follow mode, at the camera otherwise — 3D voice sources need no change and match the host's proximity routing.
- Speaking indicator → `FeelHooks.SetTalking(slot, bool)`.

## Integration steps for the lead

1. **Crouch into the rules** — `Scripts/Net/HostMatchController.cs`, `SampleAvatars` (line ~479 on main `69dba9f`):
   ```csharp
   _engine.UpdateAvatar(slot, new AvatarSample { Position = new Vec3(p.x, p.y, p.z), Yaw = avatar.transform.eulerAngles.y, Speed = speed,
       Crouched = OwnerNetworkTransform.IsCrouched(avatar.gameObject) });
   ```
   (`IsCrouched` is a `TryGetComponent`, allocation-free. The flag is an owner-write `NetworkVariable<bool>` on
   `OwnerNetworkTransform`, already on the avatar prefab — no prefab change.)
2. **Partitions constant** — `Layers.Partitions` is *not* in `UnityWorldQueries.cs` on main yet (commit `db4c343` only
   added a BOM there). When you add `public const int Partitions = 12;` (+ `PartitionsName = "Partitions"`), change one line
   in `Scripts/Player/FeelInterfaces.cs`: `public const int Partitions = Layers.Partitions;`. Recommended for consistency:
   `UnityWorldQueries.OccluderMask => LayerMask.GetMask(Layers.WallsName, Layers.PartitionsName)` so the engine's
   witness/manager LOS matches what players can see (otherwise someone hidden behind a partition can still be "witnessed").
3. **Bots sneak (U7, optional)** — `Scripts/Bots/BotBrain.cs`, walking branch (the `avatar.Drive(... raid && Rand() < 0.02f, dt)` line):
   ```csharp
   bool sneak = !string.IsNullOrEmpty(_intent) && Time.time < _intentUntil && flat.magnitude < 6f;
   OwnerNetworkTransform.SetCrouched(avatar.gameObject, sneak);
   Vector3 dir = Time.time < _unstickUntil ? _unstickDir : d.normalized;
   avatar.Drive(sneak ? dir * LocalPlayerController.CrouchSpeedFactor : dir, false, dt);
   ```
   and `OwnerNetworkTransform.SetCrouched(avatar.gameObject, false);` where the bot stops (`avatar.Drive(Vector3.zero, ...)`).
4. **Layer 12 name** must exist in the TagManager (map/ProjectBuilder). Feel uses the number, so it works either way.
5. Nothing else: no scene, prefab, asmdef or package change. `.meta` for the new `Scripts/Fx` folder and the new scripts
   are generated by your Unity import.

## Performance and memory

- Per frame, 5 remote avatars: AvatarVisibility ≤ 3 avatars × 2 linecasts + O(n) bookkeeping; CameraOcclusion 1 sphere
  cast + 1 ray into static buffers; Sfx loop occlusion ≤ 2 linecasts; Footsteps / FeelDirector O(players + items);
  expressions at 5 Hz. Estimated well under 0.1 ms total (to be measured in the profiler). **Zero per-frame managed
  allocations** in these paths after warm-up (renderer/occluder caches fill the first time an avatar or wall is seen;
  emote / email callbacks allocate only on key press). LocalPlayerController's focus scoring reuses its collections; the
  engine's `Affordances.For` still allocates its lists every 80 ms (engine code).
- Audio memory (measured by running `SfxSynth` outside Unity): 47 clips, 39.8 s of mono 22.05 kHz = 0.88 M samples
  ≈ 3.4 MB as float PCM (v1 clips halved by the 44.1 → 22.05 kHz change); no NaN/clipping, loop seams continuous.
  Synthesis 114 ms in .NET 8 Release — expect 0.1–0.3 s under Mono, once at boot behind the menu (logged as
  `[Sfx] N clips synthesised in X ms`). Sources: 1 2D + 12 pooled 3D voices + 2 ambience + ≤ 8 keyed loops.
- FX: 11 ParticleSystems, 2 materials, one 32×32 texture (no CPU copy), 3 TMP objects, 1 point light (enabled only during
  a spark flash), 2 runtime volume profiles (disabled at weight 0).
- Bots / batch mode: no clips, no FX, no visibility pass.

## Needs a Unity run to verify

1. Crouch `NetworkVariable` on the `NetworkTransform` subclass replicates owner → host → others (NGO 2.x) and reaches
   `AvatarSample.Crouched` after step 1; remote crouch poses arrive.
2. Camera defaults (3.6 m / 24°) for reading faces; wall fading in fallback mode (v1 walls) and with `OccluderWall`;
   no camera inside walls; no flicker at doorways; meeting/results shots inside the v2 meeting room.
3. LOS numbers against real character and partition heights (tune `TargetHeight`, `HeadOffset`, `EyeHeight` on the
   AvatarVisibility component); carried items and name tags hide with their carrier; nothing pops at match start.
4. Particle look (material clone from M_Steam, sizes/colours), blackout and CCTV volumes, "!" font.
5. Mix: synthesized clip levels, wall muffling, ambience levels and duck, manager profile voices, footsteps volume.
6. IMGUI emote wheel fallback; T wheel does not rotate the camera; Y/N with the phone open.
7. Listener on the avatar with the voice module's 3D sources.
