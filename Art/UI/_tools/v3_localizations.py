"""V3 native typography exports. Writes only new Art/UI localization folders.

Uses the locked kit's original geometry and Arial Bold, never image-model text.
All pre-existing PNGs are hashed before work and asserted byte-identical after.
"""
import sys
sys.dont_write_bytecode = True
from kit import *
from root_assets import textfit, distress
from second_run_root import DAYS
from decals_v2_finish import label, TEXT_RECORDS
from PIL import ImageChops
from fontTools.ttLib import TTFont
import hashlib, random, unicodedata

FONT_PATH = 'C:/Windows/Fonts/arialbd.ttf'
RECORDS = []
PRESERVATION = []
CATEGORIES = ['stamps_v3', 'logo_en', 'week_v3', 'decals_en']
STAMP_SPECS = [
    ('stamp_ihtar', '\u0130HTAR', P['red']),
    ('stamp_warning', 'WARNING', P['red']),
    ('stamp_fired', 'FIRED', P['red']),
    ('stamp_survived', 'SURVIVED', P['green']),
    ('stamp_approved', 'APPROVED', P['green']),
    ('stamp_rejected', 'REJECTED', P['red']),
    ('stamp_confidential', 'CONFIDENTIAL', P['navy']),
    ('stamp_employee_week_en', 'EMPLOYEE OF THE WEEK', '#AD8037'),
    ('stamp_haftanin_elemani', 'HAFTANIN ELEMANI', '#AD8037'),
    ('stamp_kayitta', 'KAYITTA', P['red']),
    ('stamp_on_record', 'ON RECORD', P['red']),
]
DECAL_SPECS = [
    ('poster_teamwork', ['TEAM', 'SPIRIT'], 110, P['green']),
    ('poster_motivation', ['SUCCESS', '=', 'OVERTIME'], 144, P['green']),
    ('poster_coffee', ['COFFEE', 'RULES'], 110, P['green']),
    ('poster_mission', ['OUR VISION'], 66, P['green']),
    ('poster_relax', ['BREAK'], 58, P['green']),
    ('poster_welcome', ['WELCOME'], 58, P['green']),
    ('poster_fridge_rules', ['WHO ATE', 'MY YOGURT?'], 110, P['red']),
    ('logo_company', ['KOVA', 'HOLDINGS'], 199, P['green']),
    ('poster_safety', ['CAUTION'], 58, P['red']),
    ('poster_kpi', ['KPI'], 58, P['green']),
]


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def record_text(name, text, **extra):
    assert text == unicodedata.normalize('NFC', text)
    cmap = TTFont(FONT_PATH).getBestCmap()
    glyphs = []
    for ch in text:
        assert ord(ch) in cmap, (name, ch, 'missing font glyph')
        glyphs.append({'character': ch, 'codepoint': f'U+{ord(ch):04X}', 'fontGlyph': cmap[ord(ch)]})
    RECORDS.append({'id': name, 'exactText': text, 'normalization': 'NFC', 'font': FONT_PATH,
                    'fontSha256': sha(FONT_PATH), 'glyphs': glyphs, **extra})


def fit_record(draw, name, text, rect, color, maxsize=220):
    x, y, w, h = rect
    for size in range(maxsize, 8, -1):
        f = font(size, True)
        b = draw.textbbox((0, 0), text, font=f)
        if b[2]-b[0] <= w and b[3]-b[1] <= h:
            break
    tx = x+(w-b[2]+b[0])/2-b[0]
    ty = y+(h-b[3]+b[1])/2-b[1]
    draw.text((tx, ty), text, font=f, fill=color)
    record_text(name, text, fontSizePx=size, bounds=[tx+b[0], ty+b[1], tx+b[2], ty+b[3]], color=color)


def write_metadata(category, entries):
    safe(ROOT/category/'_metadata.json').write_text(json.dumps(entries, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')


def stamps():
    entries = []
    for i, (name, text, color) in enumerate(STAMP_SPECS):
        layer = Image.new('RGBA', (876, 230))
        d = ImageDraw.Draw(layer)
        d.rounded_rectangle((6,6,868,222), radius=16, outline=color, width=11)
        d.rounded_rectangle((24,24,850,204), radius=5, outline=color, width=3)
        fit_record(d, name, text, (44,42,786,144), color, maxsize=140)
        distress(layer, 60+i, 1600)
        layer = layer.rotate([-3,2,-2,3,-3,2][i%6], Image.Resampling.BICUBIC, expand=True)
        out = Image.new('RGBA', (960,320))
        out.alpha_composite(layer, ((960-layer.width)//2, (320-layer.height)//2))
        path = ROOT/'stamps_v3'/f'{name}.png'
        save(out, path)
        entries.append(metadata(path, 'stamps_v3', f'{text}. Distressed rubber stamp; locked original stamp geometry and deterministic Arial Bold Unicode.', text=True))
    write_metadata('stamps_v3', entries)
    sheet((ROOT/'stamps_v3').glob('*.png'), ROOT/'_sheets/v3_stamps_v3.png', 3, (430,195))


def logo():
    # Exact locked root_assets.logo layout; only its two text strings differ.
    im = Image.new('RGBA', (1600,700))
    back = Image.new('RGBA', (1450,520)); d = ImageDraw.Draw(back)
    d.rounded_rectangle((7,7,1440,510), radius=40, fill=P['manila'], outline=P['navy'], width=8)
    back = back.rotate(3, Image.Resampling.BICUBIC, expand=True); im.alpha_composite(back, (55,70))
    front = Image.new('RGBA', (1490,540)); d = ImageDraw.Draw(front)
    d.rounded_rectangle((7,7,1482,530), radius=40, fill=P['paper'], outline=P['navy'], width=9)
    d.line((440,46,440,485), fill='#D2C8B4', width=3)
    d.line((480,65,1410,65), fill=P['green'], width=10)
    fit_record(d, 'logo_en_top', 'SOMEONE', (490,90,920,150), P['navy'], maxsize=190)
    stamp = Image.new('RGBA', (1020,230)); ds = ImageDraw.Draw(stamp)
    ds.rounded_rectangle((6,6,1013,223), radius=15, outline=P['red'], width=12)
    ds.rounded_rectangle((25,25,994,204), radius=6, outline=P['red'], width=3)
    fit_record(ds, 'logo_en_stamp', 'GETS FIRED', (42,42,934,146), P['red'], maxsize=165)
    stamp = distress(stamp,44,1500).rotate(2,Image.Resampling.BICUBIC,expand=True)
    front.alpha_composite(stamp,(450,265))
    mark = Image.open(ROOT/'logo/logo_mark.png').convert('RGBA')
    mark.thumbnail((420,490),Image.Resampling.LANCZOS)
    front.alpha_composite(mark, ((445-mark.width)//2,40))
    im.alpha_composite(front,(55,80)); d=ImageDraw.Draw(im)
    d.rounded_rectangle((1380,58,1413,166),radius=16,outline=P['navy'],width=7)
    d.rounded_rectangle((1391,66,1404,151),radius=6,outline='#B4B6A7',width=4)
    path=ROOT/'logo_en/logo_en.png'; save(im,path)
    old = Image.open(ROOT/'logo/logo.png').convert('RGBA')
    dif = ImageChops.difference(old, im)
    ImageDraw.Draw(dif).rectangle((500,160,1520,600), fill=(0,0,0,0))
    assert not any(c.getbbox() for c in dif.split()), 'English logo changed non-lettered artwork'
    PRESERVATION.append({'id':'logo_en','source':'Art/UI/logo/logo.png','unchangedOutside':[500,160,1520,600], 'sourceSha256':sha(ROOT/'logo/logo.png')})
    write_metadata('logo_en',[metadata(path,'logo_en','SOMEONE / GETS FIRED. Exact original stationery, stamp frame, paperclip and box-carrying blue bean; only deterministic lettering is localized.',text=True)])
    sheet([path], ROOT/'_sheets/v3_logo_en.png', 1, (1200,580))


def weeks():
    entries=[]
    for i,(oldname,oldtext,icon,desc) in enumerate(DAYS):
        old=Image.open(ROOT/'week'/f'{oldname}.png').convert('RGBA')
        # Only the lettering area changes. Progress dots, rule, pictogram,
        # shadow, frames and Friday confetti remain identical pixels.
        out=old.copy()
        color=P['green'] if i==4 else P['paper']
        d=ImageDraw.Draw(out)
        rect=(205,47,889,132)
        d.rectangle(rect,fill=color)
        # Check rectangle is sufficient to erase every original glyph using
        # the same exact native text-fitting formula at its 2x authoring size.
        tmp=Image.new('L',(1920,400)); td=ImageDraw.Draw(tmp)
        textfit(td,oldtext,(418,98,1350,152),255,maxsize=154)
        bounds=tmp.resize((960,200),Image.Resampling.LANCZOS).getbbox()
        assert bounds[0]>=rect[0] and bounds[1]>=rect[1] and bounds[2]<=rect[2] and bounds[3]<=rect[3], (oldname,bounds)
        path=ROOT/'week_v3'/f'week_blank_{i}.png'; save(out,path)
        diff=ImageChops.difference(old,out)
        ImageDraw.Draw(diff).rectangle(rect,fill=(0,0,0,0))
        assert not any(c.getbbox() for c in diff.split())
        PRESERVATION.append({'id':path.stem,'source':f'Art/UI/week/{oldname}.png','blankLabelRect':[205,47,685,86], 'unchangedOutside':rect,'sourceSha256':sha(ROOT/'week'/f'{oldname}.png')})
        entries.append(metadata(path,'week_v3',f'{["Monday","Tuesday","Wednesday","Thursday","Friday"][i]} blank banner. Original pictogram, frame, progress dots and decorative art preserved pixel-for-pixel; label safe area [209,49,675,76].'))
    write_metadata('week_v3',entries)
    sheet((ROOT/'week_v3').glob('*.png'),ROOT/'_sheets/v3_week_v3.png',1,(960,240))


def decals():
    entries=[]
    for name,lines,header_h,accent in DECAL_SPECS:
        src=ROOT/'decals'/f'{name}.png'
        old=Image.open(src).convert('RGB'); out=old.copy(); w,h=out.size
        if name=='poster_kpi':
            # Identical English abbreviation: no image edits at all.
            record_text('en_'+name,'KPI', method='Existing verified Arial Bold source reused byte-for-byte')
            path=ROOT/'decals_en'/f'{name}.png'
            safe(path).write_bytes(src.read_bytes())
            change=None
        else:
            native=Image.new('RGB',(w*3,h*3),P['paper'])
            if name=='logo_company':
                rect=(316,84,421,199); fill=P['paper']; ink=P['green']; maxsize=92
                change=(316,84,738,284)
            else:
                rect=(22,22,w-44,header_h)
                fill=P['paper'] if name=='poster_safety' else accent
                ink=accent if name=='poster_safety' else P['paper']
                maxsize=64
                change=(25,25,w-24,22+header_h-2)
                if name=='poster_safety':change=(22,22,w-21,22+header_h+1)
            label(native,'en_'+name,lines,rect,fill,ink,maxsize=maxsize,stamp=name=='poster_safety')
            rendered=native.resize(old.size,Image.Resampling.LANCZOS)
            out.paste(rendered.crop(change),change[:2])
            path=ROOT/'decals_en'/f'{name}.png'
            out.save(safe(path),optimize=True)
            details=TEXT_RECORDS[-1]
            record_text('en_'+name,' '.join(lines),lines=lines,fontSizePx=details['fontSizePx'],glyphBoundsPx=details['glyphBoundsPx'],patchBox=change)
        dif=ImageChops.difference(old,Image.open(path).convert('RGB'))
        if change:ImageDraw.Draw(dif).rectangle((change[0],change[1],change[2]-1,change[3]-1),fill=(0,0,0))
        assert not dif.getbbox(), 'Changed art outside lettering: '+name
        PRESERVATION.append({'id':'en_'+name,'source':'Art/UI/decals/'+name+'.png','unchangedOutside':change,'sourceSha256':sha(src)})
        entry=metadata(path,'decals_en',' '.join(lines)+'. Original wall poster artwork and pixel size preserved; deterministic English lettering replaces only the label.',text=True)
        entry['id']='en_'+name
        entries.append(entry)
    write_metadata('decals_en',entries)
    sheet((ROOT/'decals_en').glob('*.png'),ROOT/'_sheets/v3_decals_en.png',5,(310,370))


def main():
    # Only files already present outside owned new categories count as sources.
    originals={str(p.relative_to(ROOT)):sha(p) for p in ROOT.rglob('*.png')
               if p.relative_to(ROOT).parts[0] not in CATEGORIES+['_tools','_sheets']}
    stamps(); logo(); weeks(); decals()
    for rel,digest in originals.items():
        assert sha(ROOT/rel)==digest, 'Pre-existing PNG modified: '+rel
    outputs=[p for c in CATEGORIES for p in (ROOT/c).glob('*.png')]
    assert len(outputs)==27
    audits=[audit(p) | {'path':p.relative_to(ROOT).as_posix(),'sha256':sha(p)} for p in sorted(outputs)]
    assert all(x['alphaExtrema']==(0,255) for x in audits if not x['path'].startswith('decals_en/'))
    assert all(x['alphaExtrema']==(255,255) for x in audits if x['path'].startswith('decals_en/'))
    ledger={'method':'Deterministic typography using original kit native layout and original art; no new model-generated text or artwork.',
            'script':'Art/UI/_tools/v3_localizations.py','textRecords':RECORDS,
            'pixelPreservation':PRESERVATION,'originalPngCountVerified':len(originals),
            'assets':audits, 'counts':{c:len(list((ROOT/c).glob('*.png'))) for c in CATEGORIES}}
    safe(ROOT/'_tools/v3_localizations_records.json').write_text(json.dumps(ledger,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'counts':ledger['counts'],'preservedOriginals':len(originals),'glyphsVerified':sum(len(x['glyphs']) for x in RECORDS)},ensure_ascii=False))


if __name__=='__main__':main()
