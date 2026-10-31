# One-off helper that writes music_manifest.json (kept so the prompts are easy to edit). python -I build_manifest.py
import json, os
HERE = os.path.dirname(os.path.abspath(__file__))
BASE = ("Instrumental only, no vocals. Sparse dry arrangement with lots of empty space for people talking over it. "
        "Muted upright-style bass, brushed or soft rim-click percussion, restrained electric piano, celesta, muted vibraphone, pizzicato strings. "
        "Recurring short four-note rising-then-falling motif, played politely and slightly off-balance. Dark office comedy atmosphere, awkward corporate optimism with playful suspicion. ")
cues = [
 ("menu_lobby","Main menu and lobby bed","loop",75,"Mid-slow tempo around 92 BPM, light and awkwardly cheerful, hopeful but faintly suspicious, a gentle loop-friendly groove that begins and ends in the same state. "),
 ("office_work","Low-intensity bed during work blocks, task intro, reveal, break","loop",75,"Very low intensity, slow 84 BPM, steady quiet pulse, small playful pizzicato figures, almost background, restrained suspense, steady throughout with no big builds. "),
 ("deadline","Rising tension in the last seconds of a task","loop",40,"Faster 120 BPM, ticking muted percussion, pizzicato ostinato, slowly rising tension through the piece, restrained not dramatic, steady energy at the end so it can repeat. "),
 ("meeting_intro","Stinger at the start of the meeting dossier","stinger",10,"Short stinger, a stiff formal fanfare played on celesta and pizzicato strings, one awkward wrong-footed final chord, then a short decay. "),
 ("meeting_discussion","Very sparse bed during meeting discussion, vote, verdict","loop",55,"Extremely sparse, 70 BPM, a few soft sustained notes, occasional muted vibraphone, one low bass note per bar, quiet uneasy suspicion, almost nothing happening. "),
 ("final_vote","Tense bed for final defense, vote and verdict","loop",40,"Tense, slow 76 BPM, low muted bass pulse, sparse dissonant electric piano, soft ticking, restrained dread with a hint of absurdity, steady and loopable. "),
 ("results_success","Stinger when the company survives","stinger",10,"Short stinger in a major key, the recurring motif played hopefully on celesta and vibraphone with a warm pizzicato resolution, a small awkward flourish at the end. "),
 ("results_collapse","Stinger when the company collapses","stinger",10,"Short stinger in a minor key, the recurring motif played slowly and deflated, sagging pitch, a comically sad muted descending figure, ending on a flat unresolved note. "),
]
out = {"provider":"ElevenLabs","endpoint":"https://api.elevenlabs.io/v1/music","model_id":"music_v2_5","output_format":"mp3_44100_128","force_instrumental":True,
       "cues":[]}
for cid,use,kind,dur,extra in cues:
    out["cues"].append({"id":cid,"use":use,"kind":kind,"target_duration_s":dur,"prompt":BASE+extra,
        "source":f"Art/Audio/Music/source/{cid}.mp3","integrated":f"Game/Assets/Game/Audio/Resources/music/{cid}.ogg",
        "loop":kind=="loop","crossfade_ms":1500 if kind=="loop" else 0})
json.dump(out,open(os.path.join(HERE,"music_manifest.json"),"w"),indent=2)
