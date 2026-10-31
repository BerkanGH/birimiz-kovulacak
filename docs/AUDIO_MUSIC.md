# Music (v4)

Original instrumental soundtrack generated with the official ElevenLabs Music API, processed into game-ready OGG loops and stingers, and played by `MusicDirector`. **No human listening check has been run on any cue** (see "Verification").

## Identity

Awkward corporate optimism, playful suspicion, restrained suspense, occasional absurdity, lots of space for people talking over it. Sparse, dry arrangements: muted bass, soft percussion, electric piano, celesta, muted vibraphone, pizzicato strings, and one short rising-then-falling four-note motif requested in every cue. (Whether the model actually kept a consistent motif is **unverified**; that needs a listening pass.)

## Cues

| Cue | Use | Kind | Requested | Delivered (ogg) |
|---|---|---|---|---|
| menu_lobby | menu, lobby | loop | 75 s | 70.0 s |
| office_work | Morning, RoundIntro, RoundPlay (early), RoundReveal, Break | loop | 75 s | 69.75 s |
| deadline | RoundPlay, last min(30 s, 25 % of the play length) | loop | 40 s | 38.25 s |
| meeting_intro | start of MeetingDossier, once | stinger | 10 s | 10.17 s |
| meeting_discussion | MeetingDiscussion, MeetingVote, MeetingVerdict | loop | 55 s | 47.75 s |
| final_vote | FinalDefense, FinalVote, FinalVerdict | loop | 40 s | 36.75 s |
| results_success | WeekResults, company survived, once | stinger | 10 s | 9.89 s |
| results_collapse | WeekResults, `Result.CompanyClosed`, once | stinger | 10 s | 9.94 s |

Prompts: `Tools/music/music_manifest.json` (edit via `Tools/music/build_manifest.py`, which regenerates it). Prompts describe musical characteristics only; no artist, song or "in the style of" wording (the terms forbid it, section 2.1).

## ElevenLabs API facts (verified from the docs on 2026-10-31)

- Compose: `POST https://api.elevenlabs.io/v1/music`, JSON body, `output_format` as a query parameter. Docs: https://elevenlabs.io/docs/api-reference/music/compose (detailed variant, multipart with metadata: https://elevenlabs.io/docs/api-reference/music/compose-detailed).
- Body fields used: `prompt`, `music_length_ms` (allowed 3000-600000; only with `prompt`), `model_id`, `force_instrumental: true` (only with `prompt`). Not used: `composition_plan` (cannot be combined with `prompt`; v2/v2.5 plans are `chunks` with `text`, `duration_ms` 3000-120000, `positive_styles`, `negative_styles`).
- Model: `music_v2_5` (documented model ids: `music_v1` default, `music_v2`, `music_v2_5`; capability page: https://elevenlabs.io/docs/overview/capabilities/music). The key cannot list models, but the request with `music_v2_5` succeeded for all 8 cues (HTTP 200); the model string returned by the API is not exposed in headers, so provenance records the requested id.
- `output_format=mp3_44100_128` (the docs list many; Creator terms say high-quality 192 kbps / WAV downloads are not included in Creator).
- Response: the audio bytes; the song id arrives in the `song-id` response header (the docs do not name the header; observed). Stored in provenance.
- Errors documented: 422 validation (`detail` list). 429 and 5xx are retried with backoff by the script (never needed).
- Credits (character-based counter of `/v1/user/subscription`, tier creator): **46250 before, 50582 after** (limit 131000), so 4332 for 8 generations, 8 requests total, no retries. Creator plan generation limit per the terms: 62 minutes per month; we generated about 5.5 minutes.

## Commands

```
python -I Tools/music/generate_music.py [--cue ID ...] [--force] [--max-requests N] [--retries N]   # needs ELEVENLABS_API_KEY in the environment
python -I Tools/music/process_music.py [--cue ID ...]                                              # needs ffmpeg/ffprobe on PATH
cd Tools/MusicTests && dotnet test
```

Regenerate one cue: edit its prompt in `build_manifest.py`, run `python -I Tools/music/build_manifest.py`, then `generate_music.py --cue deadline --force` and `process_music.py --cue deadline`. Generation skips cues that already have a download and a provenance record unless `--force`. Builds never call the API; the key is read from the environment inside the script only and never written anywhere (headers stored in provenance are an allow-list of response headers).

## Provenance

`Tools/music/provenance.json`: per cue id, intended use, prompt, provider, endpoint, requested model id, output format, `force_instrumental`, UTC generation time, song id and safe response headers, source and integrated paths, byte size, loop and crossfade settings, post-processing steps, integrated duration/format. Original downloads (as returned, MP3 128 kbps 44.1 kHz) are in `Art/Audio/Music/source/`; game assets in `Game/Assets/Game/Audio/Resources/music/<cue>.ogg`.

## Processing chain (`process_music.py`)

1. Decode to 44.1 kHz stereo float. Trim leading/trailing silence (< -55 dB).
2. Loops: the model's output fades in and out, so the generated fade regions are cut (keep the span whose 250 ms levels are within 4 dB of the 70th-percentile level), then a 1.5 s equal-power (`qsin`) crossfade of the tail into the head is baked into the seam (result length = body minus 1.5 s). A "loopable" prompt is not assumed to be seamless; the seam is measured (below).
3. One linear gain to the target (beds -19 LUFS, stingers -17 LUFS integrated), re-measured; reduced if the true peak exceeds -2.3 dBTP before encoding (headroom for Vorbis overshoot). No compressor or limiter. Stingers get a 10 ms fade-in and a 350 ms fade-out.
4. Encode OGG Vorbis q5, 44.1 kHz stereo; verify codec, sample rate, loudness, true peak (<= -1.5 dBTP), seam, silence on the final file.

## Measurements (final OGG files, ffmpeg ebur128 / ffprobe, from `Tools/music/measurements.json`)

| Cue | Duration s | Integrated LUFS | True peak dBTP | Wrap jump (x p99 step) | Wrap 50 ms level step dB | Seam edge vs quietest 10% dB | Silences > 1.5 s (< -50 dB) |
|---|---|---|---|---|---|---|---|
| menu_lobby | 70.00 | -19.0 | -5.9 | 0.08 | 0.5 | +0.7 | 0 |
| office_work | 69.75 | -19.7 | -2.4 | 0.19 | 1.0 | -3.0 | 0 |
| deadline | 38.25 | -19.0 | -6.3 | 0.38 | 5.0 | -1.3 | 0 |
| meeting_discussion | 47.75 | -19.0 | -2.8 | 0.54 | 2.2 | +4.2 | 0 |
| final_vote | 36.75 | -19.0 | -6.9 | 0.44 | 0.2 | +0.4 | 0 |
| meeting_intro (stinger) | 10.17 | -18.5 | -2.6 | n/a | n/a | n/a | 0 |
| results_success (stinger) | 9.89 | -17.0 | -3.2 | n/a | n/a | n/a | 0 |
| results_collapse (stinger) | 9.94 | -19.9 | -2.6 | n/a | n/a | n/a | 0 |

Stingers sit up to 2.9 LU under their -17 target because the true-peak ceiling limits a gain-only fix (accepted: tolerance 3 LU). The deadline 50 ms step (5 dB) is natural variation of its ticking percussion, not a click (wrap jump 0.38 x the 99th-percentile sample step). office_work is very dynamic (plucked notes over near silence), so its edges naturally sit near its own floor. These are numbers about continuity at the wrap point; they do **not** say whether the loop is rhythmically on the beat or musically pleasant.

## Unity integration

- `Core/Audio/MusicCuePlanner.cs` (pure, namespace `BirimizKovulacak.Core`): `MusicCuePlanner.Plan(MusicState)` returns cue id (null = silence), `Loop`, `Duck` and crossfade time. Mapping: no view or `None` -> menu_lobby; Morning/RoundIntro/RoundReveal/Break -> office_work; RoundPlay -> deadline when remaining <= min(30 s, 25% of the play length) else office_work (a literal "30 s or 25%" would make every task under two minutes all-deadline, so the smaller value is used); MeetingDossier -> meeting_intro (one-shot, silence afterwards); MeetingDiscussion/Vote/Verdict -> meeting_discussion; FinalDefense/Vote/Verdict -> final_vote; WeekResults -> results_collapse if `Result.CompanyClosed` else results_success (one-shot, silence afterwards); Abandoned -> stop.
- Duck (minimum of all that apply): tape reveal (`VoiceSystem.RecordingActive`) 0.08; my phone `CallStatus.Connected` 0.35; anyone in the view speaking (`VoiceSystem.IsSpeaking`) during meeting and final phases 0.45; FinalDefense 0.25. No microphone analysis.
- `Scripts/Audio/MusicDirector.cs`: exactly two AudioSources (A/B crossfade), clips loaded lazily with `Resources.Load("music/<cue>")` and cached (a missing clip logs one warning and stays silent), volume = `MusicSettings.Effective` x smoothed duck (150 ms attack, 1.2 s release) x per-source fade. A stinger plays once because an unchanged cue is never restarted. New `WeekId` (rematch) resets both sources; `View == null` (menu return, reconnect gap) selects menu_lobby; Abandoned fades out and releases. `TelemetryHooks.MusicCueChanged(cue, ducked)` fires only on cue changes ("none" for silence). Added in `Sfx.Awake` next to `Ambience` behind the same batch-mode/bot guard.
- `Editor/MusicImportPostprocessor.cs`: Vorbis, quality 0.6, streaming for beds, compressed-in-memory for the three stingers, load in background, stereo kept.
- Offline: the OGG files ship in the build; gameplay never touches the network or credentials. Generation is a development-time tool only.

## Verification

Done: 17 NUnit tests (`Tools/MusicTests`) for mapping, deadline threshold, results, menu return/rematch, duck targets; `compile_check.py --engine-stubs` (and `--player`) shows zero errors in the audio files (remaining errors are in the UI folders being rewritten elsewhere); ffprobe/ebur128 checks above on every shipped file.

Not done: **no human listened to any cue**; no Unity run, so import settings, streaming playback, crossfade feel, ducking levels against real voice chat, and loop musicality are unverified. Motif consistency and the "major/hopeful vs deflated/absurd" intent of the two result stingers are unverified. Duck levels and the 70 s beds are first guesses.

## Licence and terms (flag for human/legal review)

Sources read: Eleven Music model-specific terms https://elevenlabs.io/eleven-music-model-specific-terms (page says "Last updated: 09 October 2026"), capability page https://elevenlabs.io/docs/overview/capabilities/music. Findings:

- Creator plan (Exhibit A): "All online and offline commercial use permitted, except film, TV, radio, and Studio Games"; model resale and music-library creation/distribution prohibited; no attribution required (attribution applies to the Free plan); individual use only; API access yes; high-quality downloads no.
- "Studio Game" = a monetised game made available to the public, excluding "Indie Games". Indie Game (section 4.8(c)): the game earned under $500,000 since first release and the developer's revenue from all games was under $1,000,000 in its last financial year (net of platform fees, refunds, chargebacks, including affiliates). If a game stops qualifying, a plan that permits Studio Games is needed within 60 days (section 4.7).
- The capability page claims music is "cleared for nearly all commercial uses ... gaming", which is broader than the terms; the terms govern.
- Prompts may not name artists, songs, labels or include substantial lyrics (done). Output must not imitate a recording artist (n/a, instrumental).
- Not found in the pages read: any specific statement about promotional gameplay clips or streaming/videos of the game (the Creator media-rights line is general commercial use except film/TV/radio/Studio Games, so trailers might be covered or might not; ask). Also not found: terms specific to `music_v2_5` (the page cites only the v1 family). The older Service Terms version restricted games for paid plans.
- Nothing here establishes the music as "copyright-safe" or "commercially cleared": copyrightability of AI output, the 62 min monthly cap, "individual use only" (does a studio/company account count?), the Studio Game threshold if sales grow, and the v2.5 terms are **unresolved and need human/legal review before commercial release**. Keep `provenance.json` (song ids, dates, plan tier) as evidence of the plan at generation time.
