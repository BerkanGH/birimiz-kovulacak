"""Build the 149-asset manifest and audit the PNG kit. Writes only beneath Art/UI.

Run with Python + Pillow, optionally --strict to return a failure exit code when
the kit is incomplete or a required check fails. Review sheets remain untouched.
"""
import sys
sys.dont_write_bytecode = True
from pathlib import Path
import argparse
import collections
import datetime
import hashlib
import json
from kit import Image, ROOT, safe

FIELDS = ['id', 'path', 'w', 'h', 'category', 'nineSlice', 'screenRect', 'hasText', 'description']
EXPECTED = {}

def group(category, names, size):
    for name in names.split() if isinstance(names, str) else names:
        EXPECTED[(category, name)] = tuple(size)

group('style', 'style_sheet', (1600,1000))
group('logo', 'logo', (1600,700))
group('logo', 'logo_mark', (512,512))
group('backgrounds', 'menu_bg lobby_bg results_bg', (1920,1080))
group('backgrounds', 'paper_tile', (512,512))
group('buttons', [f'{kind}_{state}' for kind in ['primary','secondary','danger','ghost']
                 for state in ['normal','hover','pressed','disabled']], (192,96))
for name,size in {
    'panel_paper':(512,512), 'panel_folder':(640,480), 'panel_sticky':(384,384),
    'tooltip':(320,160), 'progress_bar_bg':(320,32), 'progress_bar_fill_green':(320,32),
    'progress_bar_fill_red':(320,32), 'toggle_on':(112,64), 'toggle_off':(112,64),
    'slider_track':(320,24), 'slider_knob':(64,64), 'tab_active':(192,72), 'tab_inactive':(192,72),
}.items(): group('buttons',[name],size)
group('icons', '''task_coffee task_report task_phone task_clean task_mail
    sab_nameplate sab_plant sab_hide_report sab_spill sab_clutter
    sab_password sab_fake_email sab_pull_plug sab_salt sab_internet sab_power
    hr_scan hr_seal hr_call_logs hr_footage
    quota timer manager suspicion_0 suspicion_1 suspicion_2 suspicion_3 suspicion_4
    mic_on mic_off speaker deafen call hang_up ringing accuse ballot footage settings bot
    ready not_ready crown star favor_heart lock_screen email notes objective cards contacts
    warning check cross info kick host week_calendar
    emote_wave emote_point emote_facepalm emote_shrug emote_laugh''', (256,256))
group('cards', '''card_password card_fake_email card_pull_plug card_salt card_internet card_power
    card_back card_frame_empty''', (512,716))
group('portraits', [f'mgr_{p}{state}' for p in ['paragoz','disiplinci','kayirmaci','paranoyak']
                    for state in ['', '_angry']] + [f'player_{i}' for i in range(6)] + ['player_unknown'], (512,512))
group('stamps', 'stamp_kovuldu stamp_kurtuldu stamp_onaylandi stamp_reddedildi stamp_gizli stamp_ayin_elemani', (960,320))
group('phone', 'phone_frame', (600,1200))
group('phone', 'app_tasks app_cards app_contacts app_inbox app_notes app_hr', (192,192))
group('phone', 'call_screen_bg', (504,984))
group('meeting', 'projector_frame week_board', (1600,900))
group('meeting', 'evidence_folder', (800,600))
group('meeting', 'cctv_overlay', (1280,720))
group('meeting', 'termination_letter', (1200,1600))
group('decals', 'poster_takim_ruhu poster_verimlilik poster_sadakat poster_kahve wet_floor', (512,768))
group('decals', 'whiteboard_doodles', (1024,512))
group('decals', 'corkboard_notes', (1024,768))
group('decals', 'exit_sign', (512,256))
assert len(EXPECTED)==149, len(EXPECTED)
assert len({name for _,name in EXPECTED})==149, 'Asset ids must be globally unique'

REQUIRED_TEXT = {
    'logo':'BİRİMİZ KOVULACAK',
    'stamp_kovuldu':'KOVULDU', 'stamp_kurtuldu':'KURTULDU',
    'stamp_onaylandi':'ONAYLANDI', 'stamp_reddedildi':'REDDEDİLDİ',
    'stamp_gizli':'GİZLİ', 'stamp_ayin_elemani':'AYIN ELEMANI',
    'poster_takim_ruhu':'TAKIM RUHU', 'poster_verimlilik':'VERİMLİLİK',
    'poster_sadakat':'SADAKAT', 'poster_kahve':'KAHVE', 'exit_sign':'ÇIKIŞ',
}
SLICE_EXPECTED = {
    **{f'{kind}_{state}':[32,32,32,32] for kind in ['primary','secondary','danger','ghost']
       for state in ['normal','hover','pressed','disabled']},
    'panel_paper':[32,32,32,32], 'panel_folder':[256,112,32,32],
    'panel_sticky':[32,64,80,88], 'tooltip':[24,24,24,36],
    'progress_bar_bg':[16,0,16,0], 'progress_bar_fill_green':[16,0,16,0],
    'progress_bar_fill_red':[16,0,16,0], 'slider_track':[12,0,12,0],
    'tab_active':[24,24,24,24], 'tab_inactive':[24,24,24,24],
}
SCREEN_EXPECTED = {'phone_frame':[48,108,504,984], 'projector_frame':[86,100,1428,690]}
DESCRIPTIONS = {
    'style_sheet':'Locked warm stationery palette, outline/shading study, icon, button, card and blue-bean portrait; text-free.',
    'menu_bg':'Open-plan office with all six scheming player beans and a manager in the glass office; calm left 40% for menu controls.',
    'lobby_bg':'Warm office break room with a large quiet corkboard for lobby information.',
    'results_bg':'Overhead office desk with scattered papers, coffee, paper clips and a red stamp; open central paper for results.',
    'paper_tile':'Seamless off-white paper micro-grain; opposite pixel edges match exactly.',
    'logo_mark':'Text-free blue bean carrying a cardboard box of desk belongings; company/game emblem.',
    'logo':'BİRİMİZ KOVULACAK title on layered stationery with red rubber-stamp treatment and blue-bean emblem.',
    'accuse':'Cream cartoon pointing hand with navy cuff and red attention marks; accusation action.',
    'bot':'Friendly teal robot colleague head with large cream eyes and a red antenna tip.',
    'poster_takim_ruhu':'TAKIM RUHU parody poster: three smiling coworkers pulling one folder in opposing directions.',
    'poster_verimlilik':'VERİMLİLİK parody poster: frantic green bean stamping excessive report piles.',
    'poster_sadakat':'SADAKAT parody poster: tiny orange bean adoring an oversized executive chair.',
    'poster_kahve':'KAHVE parody poster: exhausted beans worshipping an oversized mug on a report pedestal.',
    'whiteboard_doodles':'Office whiteboard with text-free marker doodles: collapsing graph, bean faces, secret arrows, mug and plug.',
    'corkboard_notes':'Framed corkboard with blank notes, pushpins, red string and small mug/plant picture cards.',
    'exit_sign':'Green ÇIKIŞ sign with a bold right-pointing cream arrow.',
    'wet_floor':'Yellow folding caution sign with a slipping bean pictogram and transparent handle opening; text-free.',
}
for n,t in REQUIRED_TEXT.items():
    if n.startswith('stamp_'): DESCRIPTIONS[n]=f'Distressed transparent rubber stamp reading {t}; Turkish glyphs rendered with a font.'

def main(strict=False):
    errors=[]; warnings=[]; metadata={}; metadata_sources={}; fallbacks=[]
    cats=list(dict.fromkeys(cat for cat,_ in EXPECTED))
    files=[p for cat in cats for p in (ROOT/cat).glob('_metadata*.json')]
    for p in sorted(files,key=lambda p:(p.stat().st_mtime_ns,str(p))):
        try:
            rows=json.loads(p.read_text(encoding='utf-8-sig'))
            if not isinstance(rows,list): raise ValueError('Metadata root must be an array')
            for row in rows:
                key=(row.get('category'),row.get('id'))
                if key not in EXPECTED:
                    warnings.append(f'Ignored non-inventory metadata: {p.relative_to(ROOT)} / {key}')
                    continue
                metadata[key]=row;metadata_sources[key]=str(p.relative_to(ROOT)).replace('\\','/')
        except Exception as exc: errors.append(f'Metadata {p.relative_to(ROOT)}: {exc}')

    manifest=[]; audits=[]; counts=collections.Counter(); size_total=0
    for (cat,name),(w,h) in EXPECTED.items():
        path=ROOT/cat/(name+'.png');rel=f'Art/UI/{cat}/{name}.png'
        md=metadata.get((cat,name))
        if md is None:
            fallbacks.append(rel)
            md={}
        desc=DESCRIPTIONS.get(name,md.get('description') or f'Text-free {cat} asset: {name.replace("_"," ")}.')
        entry=dict(id=name,path=rel,w=w,h=h,category=cat,nineSlice=md.get('nineSlice',SLICE_EXPECTED.get(name)),
                   screenRect=md.get('screenRect',SCREEN_EXPECTED.get(name)),hasText=name in REQUIRED_TEXT,description=desc)
        manifest.append(entry)
        if md:
            for k in ['id','path','w','h','category','nineSlice','screenRect','hasText']:
                if k not in md: errors.append(f'{rel}: metadata missing {k}')
                elif md[k]!=entry[k]: errors.append(f'{rel}: metadata {k}={md[k]!r}, expected {entry[k]!r}')
        if entry['nineSlice'] != SLICE_EXPECTED.get(name): errors.append(f'{rel}: incorrect slice border')
        if entry['screenRect'] != SCREEN_EXPECTED.get(name): errors.append(f'{rel}: incorrect screen rectangle')
        if not path.exists():
            errors.append(f'Missing asset: {rel}')
            audits.append(dict(path=rel,exists=False))
            continue
        counts[cat]+=1;size=path.stat().st_size;size_total+=size
        try:
            with Image.open(path) as src:
                fmt=src.format;mode=src.mode;has_alpha='A' in src.getbands() or 'transparency' in src.info
                im=src.convert('RGBA')
            if fmt!='PNG': errors.append(f'{rel}: format is {fmt}, must be PNG')
            if im.size!=(w,h): errors.append(f'{rel}: dimensions {im.size}, expected {(w,h)}')
            a=im.getchannel('A');aw,ah=im.size
            corners=[a.getpixel(p) for p in [(0,0),(aw-1,0),(0,ah-1),(aw-1,ah-1)]]
            hist=a.histogram(); total=aw*ah; bounds=a.point(lambda v:255 if v>16 else 0).getbbox()
            opaque_expected=cat in ['backgrounds','style'] or name=='call_screen_bg'
            transparent_inside=0;inside_fraction=0.0
            if bounds:
                inner=a.crop(bounds);ih=inner.histogram();transparent_inside=ih[0]
                inside_fraction=transparent_inside/(inner.width*inner.height)
            if opaque_expected:
                if a.getextrema()!=(255,255):errors.append(f'{rel}: background/style surface must be opaque')
            else:
                if not has_alpha: errors.append(f'{rel}: no alpha channel/transparency table')
                if corners!=[0,0,0,0]:errors.append(f'{rel}: nontransparent corner(s) {corners}')
                if a.getextrema()!=(0,255):errors.append(f'{rel}: expected alpha range 0..255, got {a.getextrema()}')
                if not bounds or not transparent_inside:
                    errors.append(f'{rel}: no clear pixels within silhouette bounds; may be an opaque image with only transparent padding')
            if cat=='icons' and size>=80000:errors.append(f'{rel}: {size} bytes exceeds strict icon limit <80000')
            if cat=='backgrounds' and size>=1500000:errors.append(f'{rel}: {size} bytes exceeds strict background limit <1500000')
            if name.startswith('ghost_') and a.getpixel((aw//2,ah//2))!=0:
                errors.append(f'{rel}: ghost center must be fully transparent')
            screen_clear=None
            if entry['screenRect']:
                x,y,sw,sh=entry['screenRect']
                screen_clear=a.crop((x,y,x+sw,y+sh)).getextrema()==(0,0)
                if not screen_clear:errors.append(f'{rel}: screen rectangle contains nontransparent pixels')
            tile_edges=None
            if name=='paper_tile':
                tile_edges=(im.crop((0,0,1,ah)).tobytes()==im.crop((aw-1,0,aw,ah)).tobytes()
                            and im.crop((0,0,aw,1)).tobytes()==im.crop((0,ah-1,aw,ah)).tobytes())
                if not tile_edges:errors.append(f'{rel}: opposite tile edges differ')
            border=entry['nineSlice']
            if border:
                l,t,r,b=border
                if min(border)<0 or l+r>=w or t+b>=h:errors.append(f'{rel}: invalid slice region')
            audits.append(dict(path=rel,exists=True,w=aw,h=ah,bytes=size,mode=mode,hasAlpha=has_alpha,
                alphaExtrema=list(a.getextrema()),cornerAlpha=corners,transparentFraction=round(hist[0]/total,6),
                clearFractionInsideSilhouetteBounds=round(inside_fraction,6),screenRectClear=screen_clear,
                tileEdgesMatch=tile_edges,metadataSource=metadata_sources.get((cat,name)),
                sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
        except Exception as exc: errors.append(f'{rel}: {type(exc).__name__}: {exc}')

    extra=[f'Art/UI/{cat}/{p.name}' for cat in cats for p in (ROOT/cat).glob('*.png')
           if (cat,p.stem) not in EXPECTED]
    if extra: warnings.append('Additional PNGs excluded from manifest: '+', '.join(extra))
    if (ROOT/'_smoke').exists():errors.append('Art/UI/_smoke/ still exists; remove before delivery.')
    required_sheets=[f'_sheets/{cat}.png' for cat in cats]
    for s in required_sheets:
        if not (ROOT/s).exists():errors.append(f'Missing category contact sheet: Art/UI/{s}')
    known_fallbacks=[x for x in fallbacks if not x.endswith('/style_sheet.png')]
    if known_fallbacks:warnings.append('Fallback descriptions used for missing category metadata: '+', '.join(known_fallbacks))
    report=dict(
        generatedAtUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
        passed=not errors, expectedAssetCount=len(EXPECTED), presentAssetCount=sum(counts.values()),
        expectedCountsByCategory=dict(collections.Counter(c for c,_ in EXPECTED)),
        presentCountsByCategory=dict(counts), totalFinalPngBytes=size_total,
        errorCount=len(errors),errors=errors,warnings=warnings,
        checks=['Exact requested inventory and PNG dimensions','True alpha including clear pixels within silhouette bounds',
                'Transparent corners for cutouts','Icons below 80000 bytes; backgrounds below 1500000 bytes',
                'Phone/projector screen rectangles fully transparent','Ghost centers fully transparent',
                'Paper-tile opposite edges byte-identical','Manifest schema and metadata consistency','Category review sheets present','Smoke folder absent'],
        textReview=dict(method='Deterministic Unicode font rendering and human/model visual review of text-bearing images; not OCR.',
                        requiredStrings=REQUIRED_TEXT,reviewSheets=['_sheets/logo.png','_sheets/stamps.png','_sheets/decals.png']),
        unityIntegrationTested=False,requiredContactSheets=required_sheets,
        assets=audits,
    )
    safe(ROOT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    safe(ROOT/'validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:report[k] for k in ['passed','expectedAssetCount','presentAssetCount','presentCountsByCategory','totalFinalPngBytes','errors','warnings']},ensure_ascii=False,indent=2))
    return 1 if strict and errors else 0

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--strict',action='store_true')
    raise SystemExit(main(parser.parse_args().strict))
