"""Strict v3 release audit. Historical assets are checked against the captured 205-file baseline.

The original archive audit remains available as validate_kit.py --legacy. Its ignored
source archives are absent in this worktree; this audit does not pretend to recreate them.
"""
import sys
sys.dont_write_bytecode=True
from pathlib import Path
import json, hashlib, collections, datetime, unicodedata
from kit import ROOT, Image, safe, font
from PIL import ImageChops

FIELDS={'id','path','w','h','category','nineSlice','screenRect','hasText','description'}
EXPECTED={}
def group(category,names,size):
    for name in names.split(): EXPECTED[f'Art/UI/{category}/{name}.png']=tuple(size)
group('rounds','round_prim_havuzu round_farkli_eposta round_kayip_rapor round_ihbar_kutusu round_terfi_listesi round_ortak_sifre round_card_back',(512,716))
group('tricks','trick_coffee_spill trick_wiretap trick_erase_tape',(512,716))
group('icons_v3','share take_all odd_email lost_report complaint promotion keypad blame warning_letter agenda_target recording wiretap erase_tape coffee_spill phone_dead wifi_off manager_line tip_off intern defense meeting morning task_app mail_app record_app trick_app agenda_app language hint_arrow sticky_check sticky_todo',(256,256))
for name,size in {'os_wallpaper':(1920,1080),'window_frame':(640,480),'taskbar':(640,80),'notification_card':(384,192),'app_tile':(192,192),'cursor_hand':(64,64),'offline_overlay':(1920,1080),'coffee_overlay':(1920,1080),'boot_logo':(512,512)}.items(): group('computer',name,size)
for name,size in {'lcd_frame':(480,192),'key':(128,96),'key_pressed':(128,96),'handset':(800,600),'phone_base':(800,600),'ring_waves':(800,600),'rec_badge':(256,144),'busy':(256,256),'line_dead':(256,256)}.items(): group('deskphone',name,size)
for name,size in {'tape_player':(1024,512),'tape_reel':(256,256),'waveform_bar':(384,96),'spotlight':(1024,1024),'warning_letter':(1200,1600),'nameplate':(512,160),'vote_token':(256,256),'dossier_tab':(384,144)}.items(): group('meeting_v3',name,size)
group('onboarding','ob_1_rooms ob_2_phone ob_3_rounds ob_4_blame ob_5_tricks ob_6_recording ob_7_friday',(1280,720))
group('onboarding','sticky_note_9slice',(384,256)); group('onboarding','coach_arrow',(512,256))
group('stamps_v3','stamp_ihtar stamp_warning stamp_fired stamp_survived stamp_approved stamp_rejected stamp_confidential stamp_employee_week_en stamp_haftanin_elemani stamp_kayitta stamp_on_record',(960,320))
group('logo_en','logo_en',(1600,700)); group('week_v3','week_blank_0 week_blank_1 week_blank_2 week_blank_3 week_blank_4',(960,200))
for name,size in {'poster_teamwork':(360,510),'poster_motivation':(360,510),'poster_coffee':(360,510),'poster_mission':(504,364),'poster_relax':(504,360),'poster_welcome':(504,360),'poster_fridge_rules':(384,504),'logo_company':(768,384),'poster_safety':(360,510),'poster_kpi':(512,384)}.items(): group('decals_en',name,size)
assert len(EXPECTED)==103
CATEGORIES=list(dict.fromkeys(p.split('/')[2] for p in EXPECTED))
SLICES={
 'computer/window_frame':[40,104,40,40], 'computer/taskbar':[26,28,26,30],
 'computer/notification_card':[36,36,36,36], 'computer/app_tile':[40,40,40,40],
 'deskphone/lcd_frame':[44,44,44,44], 'deskphone/key':[28,28,28,28], 'deskphone/key_pressed':[28,28,28,28],
 'meeting_v3/waveform_bar':[30,30,30,30], 'meeting_v3/nameplate':[60,32,60,32], 'meeting_v3/dossier_tab':[42,42,42,42],
 'onboarding/sticky_note_9slice':[32,32,40,40],
}
TEXT={
 'stamp_ihtar':'İHTAR','stamp_warning':'WARNING','stamp_fired':'FIRED','stamp_survived':'SURVIVED',
 'stamp_approved':'APPROVED','stamp_rejected':'REJECTED','stamp_confidential':'CONFIDENTIAL',
 'stamp_employee_week_en':'EMPLOYEE OF THE WEEK','stamp_haftanin_elemani':'HAFTANIN ELEMANI',
 'stamp_kayitta':'KAYITTA','stamp_on_record':'ON RECORD',
 'en_poster_teamwork':'TEAM SPIRIT','en_poster_motivation':'SUCCESS = OVERTIME','en_poster_coffee':'COFFEE RULES',
 'en_poster_mission':'OUR VISION','en_poster_relax':'BREAK','en_poster_welcome':'WELCOME',
 'en_poster_fridge_rules':'WHO ATE MY YOGURT?','en_logo_company':'KOVA HOLDINGS','en_poster_safety':'CAUTION','en_poster_kpi':'KPI',
 'logo_en_top':'SOMEONE','logo_en_stamp':'GETS FIRED',
}
def read(p): return json.loads((ROOT/p).read_text(encoding='utf-8-sig'))
def digest(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def local(rel):
    p=(ROOT.parents[1]/rel).resolve()
    assert ROOT in p.parents, 'Path outside Art/UI: '+rel
    return p

def ui_path(rel):
    """Normalize either a kit-relative or repository-relative ledger path."""
    rel=rel.replace('\\','/')
    return rel if rel.startswith('Art/UI/') else 'Art/UI/'+rel

def components(mask):
    """Four-connected alpha regions; matches the authored layer-review test."""
    w,h=mask.size
    remaining={i for i,v in enumerate(mask.tobytes()) if v}; groups=[]
    while remaining:
        start=remaining.pop(); todo=[start]; group=[start]
        while todo:
            i=todo.pop(); x=i%w; y=i//w
            neighbors=([i-1] if x else [])+([i+1] if x<w-1 else [])+([i-w] if y else [])+([i+w] if y<h-1 else [])
            for n in neighbors:
                if n in remaining: remaining.remove(n); todo.append(n); group.append(n)
        if len(group)>20:
            xs=[i%w for i in group]; ys=[i//w for i in group]
            groups.append(dict(pixels=len(group),bounds=[min(xs),min(ys),max(xs)+1,max(ys)+1],center=[round(sum(xs)/len(xs),2),round(sum(ys)/len(ys),2)]))
    return sorted(groups,key=lambda g:g['pixels'],reverse=True)

def check_layers(errors):
    """Recompute contacts and reel apertures from current pixels, not assertions in JSON."""
    alignment=read('_tools/v3_props_alignment.json')
    saved=read('_tools/v3_props_layer_checks.json')
    base=Image.open(ROOT/'deskphone/phone_base.png').convert('RGBA')
    handset=Image.open(ROOT/'deskphone/handset.png').convert('RGBA')
    if list(base.size)!=alignment['phone']['canvas'] or base.size!=handset.size:
        errors.append('Phone layers do not share documented canvas')
    contact=ImageChops.multiply(base.getchannel('A').point(lambda v:255 if v>127 else 0),handset.getchannel('A').point(lambda v:255 if v>127 else 0))
    contacts=components(contact)
    if len(contacts)!=2 or any(c['center'][1]>=205 for c in contacts):
        errors.append('Handset must meet the base in exactly two upper cradle contacts')
    tape=Image.open(ROOT/'meeting_v3/tape_player.png').convert('RGBA')
    reel=Image.open(ROOT/'meeting_v3/tape_reel.png').convert('RGBA')
    if list(tape.size)!=alignment['tape']['canvas'] or reel.size!=(256,256) or alignment['tape']['reelPivot']!=[128,128]:
        errors.append('Tape layer canvas or reel pivot mismatch')
    apertures=[]
    if len(alignment['tape']['reelCenters'])!=2: errors.append('Exactly two tape reel centers required')
    for x,y in alignment['tape']['reelCenters']:
        hole=tape.getchannel('A').crop((x-40,y-40,x+40,y+40)).point(lambda v:255 if v==0 else 0)
        group=components(hole)[0]; cx,cy=group['center']; offset=[round(cx-40,2),round(cy-40,2)]
        alpha=tape.getpixel((x,y))[3]
        if alpha or abs(offset[0])>=2 or abs(offset[1])>=2:
            errors.append('Tape aperture is not clear and centered at '+str([x,y]))
        apertures.append(dict(expectedCenter=[x,y],alpha=alpha,centroidOffset=offset,holeBoundsRelativeToCenter=[v-40 for v in group['bounds']]))
    result=dict(phoneContacts=contacts,tapeApertures=apertures,
                spotlightAlphaExtrema=list(Image.open(ROOT/'meeting_v3/spotlight.png').convert('RGBA').getchannel('A').getextrema()))
    for key,value in result.items():
        if saved.get(key)!=value: errors.append('Layer review record stale: '+key)
    return alignment,result

def main(strict=False):
    errors=[]; warnings=[]; audits=[]; text_checks=[]; original_verified=set(); original_checked=set()
    prior=read('_tools/v3_prior_manifest.json'); baseline=read('_tools/v3_baseline.json')
    assert len(prior)==len(baseline)==205
    old_hash={r['path']:r['sha256'] for r in baseline}
    old_audit={r['path']:r for r in read('_tools/v3_prior_validation.json')['assets']}
    prior_paths={r['path'] for r in prior}
    if len(prior_paths)!=205 or prior_paths!=set(old_hash) or prior_paths!=set(old_audit):
        errors.append('Captured prior manifest, baseline and historical audit inventories disagree')
    md={}
    for cat in CATEGORIES:
        for f in sorted((ROOT/cat).glob('_metadata*.json')):
            for r in json.loads(f.read_text(encoding='utf-8-sig')):
                if r['path'] in md and md[r['path']]!=r: errors.append('Conflicting metadata: '+r['path'])
                md[r['path']]=r
    if set(md)!=set(EXPECTED): errors.append('Metadata inventory mismatch: missing '+str(sorted(set(EXPECTED)-set(md)))+'; extra '+str(sorted(set(md)-set(EXPECTED))))
    new=[md[p] for p in EXPECTED if p in md]
    manifest=prior+new
    if len({r['id'] for r in manifest})!=len(manifest): errors.append('Duplicate global manifest IDs')
    count=collections.Counter()
    for r in manifest:
        rel=r['path']; p=local(rel); cat=r['category']; fresh=rel in EXPECTED
        if set(r)!=FIELDS: errors.append('Incorrect nine-field schema: '+rel)
        if not p.exists(): errors.append('Missing asset: '+rel); continue
        try:
            with Image.open(p) as src:
                fmt=src.format; mode=src.mode; has_alpha='A' in src.getbands() or 'transparency' in src.info
                ico=sorted(src.ico.sizes()) if p.suffix=='.ico' else None
                im=src.convert('RGBA')
            w,h=im.size; a=im.getchannel('A'); extrema=a.getextrema(); hist=a.histogram()
            corners=[a.getpixel(q) for q in [(0,0),(w-1,0),(0,h-1),(w-1,h-1)]]
            sha=digest(p); size=p.stat().st_size
            if fmt!=('ICO' if p.suffix=='.ico' else 'PNG'): errors.append('Incorrect format: '+rel)
            expected=EXPECTED[rel] if fresh else (r['w'],r['h'])
            if im.size!=expected or [r['w'],r['h']]!=list(expected): errors.append('Incorrect dimensions: '+rel)
            if not fresh:
                original_checked.add(rel)
                if sha!=old_hash.get(rel): errors.append('Original asset modified: '+rel)
                else: original_verified.add(rel)
            opaque=(cat=='decals_en' or (cat=='onboarding' and p.stem.startswith('ob_')) or p.stem=='os_wallpaper') if fresh else old_audit[rel]['alphaExtrema']==[255,255]
            if opaque:
                if extrema!=(255,255): errors.append('Opaque surface has alpha holes: '+rel)
            else:
                if not has_alpha or extrema[0]!=0 or extrema[1]==0: errors.append('Missing real alpha: '+rel)
                if corners!=[0]*4: errors.append('Nontransparent corners: '+rel)
                if p.stem=='spotlight':
                    if not 1<extrema[1]<100: errors.append('Spotlight must remain translucent: '+rel)
                elif fresh and rel=='Art/UI/computer/coffee_overlay.png':
                    if not 128<extrema[1]<255: errors.append('Coffee overlay must substantially obscure content while remaining translucent: '+rel)
                elif extrema[1]!=255: errors.append('No fully opaque ink: '+rel)
            budget=80000 if cat=='icons_v3' or (cat in ['deskphone','meeting_v3'] and (w,h) in [(256,256),(256,144)]) else 400000 if cat in ['rounds','tricks'] else 1500000 if w>=1280 and h>=720 else None
            if fresh and budget and size>=budget: errors.append(f'Over byte budget {budget}: {rel} ({size})')
            if fresh:
                if cat!=rel.split('/')[2]: errors.append('Category does not match asset path: '+rel)
                key=cat+'/'+p.stem
                if r['nineSlice']!=SLICES.get(key): errors.append('Wrong 9-slice border: '+rel)
                expected_text=cat in ['stamps_v3','decals_en','logo_en'] or key in ['icons_v3/language','rounds/round_ortak_sifre']
                if r['hasText']!=expected_text: errors.append('Wrong hasText: '+rel)
            if r['nineSlice']:
                l,t,rr,b=r['nineSlice']
                if min(l,t,rr,b)<0 or l+rr>=w or t+b>=h: errors.append('Invalid 9-slice center: '+rel)
            if r['screenRect']:
                x,y,sw,sh=r['screenRect']
                if a.crop((x,y,x+sw,y+sh)).getextrema()!=(0,0): errors.append('Screen aperture not clear: '+rel)
            if ico and set(map(tuple,ico))!={(n,n) for n in [16,24,32,48,64,128,256]}: errors.append('Missing ICO size')
            count[cat]+=1
            audits.append(dict(path=rel,w=w,h=h,bytes=size,format=fmt,mode=mode,hasAlpha=has_alpha,alphaExtrema=list(extrema),cornerAlpha=corners,transparentFraction=round(hist[0]/(w*h),6),sha256=sha))
        except Exception as exc: errors.append(rel+': '+str(exc))
    actual_new={f'Art/UI/{cat}/{p.name}' for cat in CATEGORIES for p in (ROOT/cat).glob('*.png')}
    if actual_new!=set(EXPECTED): errors.append('Final PNG inventory differs from exact 103 requested paths')
    sheets=[f'_sheets/v3_{c}.png' for c in CATEGORIES]+['_sheets/v3_icons_48.png','_sheets/v3_props_layers_48.png','_sheets/v3_props_nineslice.png','_sheets/v3_computer_slice.png','_sheets/v3_computer_effects.png']
    for s in sheets:
        if not (ROOT/s).exists(): errors.append('Missing review sheet: '+s)
    try:
        records=read('_tools/v3_localizations_records.json')['textRecords']
        lookup={r['id']:r for r in records}
        for name,txt in TEXT.items():
            row=lookup[name]; glyphs=row['glyphs']
            font_path=Path(row['font'])
            ok=row['exactText']==txt and unicodedata.normalize('NFC',txt)==txt and ''.join(g['character'] for g in glyphs)==txt and all(g['codepoint']==f"U+{ord(g['character']):04X}" and g['fontGlyph'] and g['fontGlyph']!='.notdef' for g in glyphs)
            ok=ok and font_path.is_file() and digest(font_path)==row['fontSha256']
            if not ok: errors.append('Invalid deterministic lettering: '+name)
            text_checks.append(dict(id=name,text=txt,verified=ok))
        digits=read('_tools/v3_cards_audit.json')['typography']
        glyphs=digits['glyphs']; chars=''.join(r['text'] for r in glyphs)
        ok=chars=='417296' and len(glyphs)==6 and all(r['codepoints']==[f"U+{ord(r['text']):04X}"] and font(digits['sizeInSourcePixels'],True).getmask(r['text']).getbbox() for r in glyphs)
        if not ok: errors.append('Digit cards mismatch')
        text_checks.append(dict(id='round_ortak_sifre',text=chars,verified=bool(ok)))
        lang=read('_tools/v3_language_typography.json')
        ok=lang['text']=='A/Ö' and lang['codepoints']==['U+0041','U+002F','U+00D6'] and lang['glyphsVerified'] and all(font(48,True).getmask(c).getbbox() for c in 'A/Ö')
        if not ok: errors.append('Language glyph mismatch')
        text_checks.append(dict(id='v3_language',text=lang['text'],verified=bool(ok)))
    except Exception as exc: errors.append('Typography audit: '+str(exc))
    source_audits=[]
    selected_sources={}
    try:
        cards=read('_tools/v3_cards_generation.json')['records']
        props=read('_tools/v3_props_sources.json')
        extra_icons=read('_tools/v3_icons_extra_sources.json')
        icons=read('_tools/v3_icons_sources.json')+extra_icons
        expected_cards={Path(p).stem for p in EXPECTED if '/rounds/' in p or '/tricks/' in p}-{'round_card_back'}
        expected_props={'ob_1_rooms','ob_2_phone','ob_3_rounds','ob_4_blame','ob_5_tricks','ob_6_recording','ob_7_friday','phone_base','handset','tape_player'}
        expected_icons={Path(p).stem for p in EXPECTED if '/icons_v3/' in p}
        for rows,expected,folder,path_key,hash_key in [
            (cards,expected_cards,'v3_cards_sources','archivedSource','sourceSha256'),
            (props,expected_props,'v3_props_sources','archive','sha256'),
            (icons,expected_icons,'v3_raw_icons','archivedSource','sourceSha256')]:
            if len(rows)!=len(expected) or {r['id'] for r in rows}!=expected:
                errors.append('Source ledger inventory mismatch: '+folder)
            for row in rows:
                rel=ui_path(row[path_key]); p=local(rel)
                if not rel.startswith('Art/UI/_tools/'+folder+'/'): errors.append('Source archive in wrong folder: '+rel)
                recorded_hash=row.get(hash_key,row.get('sha256'))
                if not recorded_hash:
                    errors.append('Missing source hash in generation ledger: '+rel)
                if not p.is_file() or (recorded_hash and digest(p)!=recorded_hash):
                    errors.append('Missing or modified selected source: '+rel)
                selected_sources[rel]=recorded_hash
    except Exception as exc: errors.append('Selected source ledgers: '+str(exc))
    for folder in ['v3_cards_sources','v3_props_sources','v3_raw_icons']:
        files=list((ROOT/'_tools'/folder).glob('*.png'))
        if not files: errors.append('Missing v3 source archive: '+folder)
        for p in files:
            try:
                with Image.open(p) as src: src.verify()
                rel='Art/UI/'+str(p.relative_to(ROOT)).replace('\\','/')
                source_audits.append(dict(path=rel,sha256=digest(p),selected=rel in selected_sources,
                    matchesSelectedLedger=digest(p)==selected_sources[rel] if selected_sources.get(rel) else None,
                    hashProvenance='compared with generation ledger' if selected_sources.get(rel) else 'current archive hash recorded by this audit'))
            except Exception as exc: errors.append('Bad source: '+str(p)+str(exc))
    alignment={}; layer_checks={}
    try: alignment,layer_checks=check_layers(errors)
    except Exception as exc: errors.append('Layer alignment audit: '+str(exc))
    try:
        review=read('_tools/v3_review.json')
        if not review.get('allCategoriesVisuallyReviewed'): errors.append('Visual review incomplete')
        hashes=review['finalSha256']
        if set(hashes)!=set(EXPECTED): errors.append('Visual review must cover all 103 new files')
        for rel,sha in hashes.items():
            if digest(local(rel))!=sha: errors.append('Changed since visual review: '+rel)
    except Exception as exc: errors.append('Visual review: '+str(exc))
    if not (ROOT/'_sources').exists(): warnings.append('Historical ignored _sources archives are absent in this worktree. The previous audit is retained; all 205 original final-file hashes are checked anew. Use --legacy only in a checkout containing those archives.')
    report=dict(generatedAtUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),passed=not errors,
        expectedAssetCount=308,presentAssetCount=sum(count.values()),expectedV3Count=103,presentCountsByCategory=dict(count),
        priorAssetsPreserved=len(original_verified)==205 and original_verified==prior_paths,
        originalHashCheckCount=len(original_checked),originalHashMatchedCount=len(original_verified),newCountsByCategory=dict(collections.Counter(r['category'] for r in new)),
        totalFinalPngBytes=sum(a['bytes'] for a in audits if a['format']=='PNG'),
        nineSliceBorders=SLICES,typographyChecks=text_checks,sourceArchives=source_audits,
        selectedSourceCount=len(selected_sources),selectedSourceHashComparisonCount=sum(bool(v) for v in selected_sources.values()),
        selectedSourceCurrentHashOnlyCount=sum(not v for v in selected_sources.values()),
        layerAlignment=alignment,layerPixelChecks=layer_checks,requiredContactSheets=sheets,
        checks=['Exact 103 new PNG paths and dimensions','205 original final-file SHA-256 comparisons',
            'Real alpha, cutout corners, intentional translucent spotlight and coffee overlay','PNG size budgets',
            'Unique IDs and unchanged nine-field manifest schema','11 fixed nine-slice border specifications',
            'Exact deterministic Unicode text, glyph records and localization font hashes','All new final hashes bound to visual review',
            'All 50 selected v3 source paths and hashes match generation ledgers; all archived PNGs decode and current hashes are recorded',
            'Phone cradle contacts and tape-aperture alignment recomputed from current pixels'],
        errors=errors,errorCount=len(errors),warnings=warnings,unityIntegrationTested=False,assets=audits)
    safe(ROOT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    safe(ROOT/'validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:report[k] for k in ['passed','presentAssetCount','newCountsByCategory','priorAssetsPreserved','errors','warnings']},ensure_ascii=False,indent=2))
    return 1 if strict and errors else 0

if __name__=='__main__': raise SystemExit(main('--strict' in sys.argv))
