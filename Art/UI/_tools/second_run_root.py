"""Second art-run typography, production sizing, and review sheets; Art/UI only."""
import sys
sys.dont_write_bytecode = True
from kit import *
from root_assets import textfit
import random

DAYS = [
    ('week_pazartesi', 'PAZARTESİ', 'icons/task_coffee.png', 'Work and shared quota; coffee pictogram.'),
    ('week_sali', 'SALI', 'icons/cards.png', 'Secret sabotage cards; fanned cards pictogram.'),
    ('week_carsamba', 'ÇARŞAMBA', 'icons/hr_call_logs.png', 'Tip line and evidence; call record pictogram.'),
    ('week_persembe', 'PERŞEMBE', 'icons/hr_scan.png', 'Full investigation; magnifier over a fingerprint report.'),
    ('week_cuma', 'CUMA', 'week/week_trophy.png', 'Friday finale; gold trophy and confetti.'),
]

def week():
    finish(ROOT/'_sources/week_trophy_raw.png', ROOT/'week/week_trophy.png', 512, 512, trim=True, pad=25)
    entries=[]
    for i,(name,label,icon,desc) in enumerate(DAYS):
        scale=2
        out=Image.new('RGBA',(960*scale,200*scale))
        d=ImageDraw.Draw(out)
        def rect(box,**kw):
            if 'width' in kw: kw['width']*=scale
            if 'radius' in kw: kw['radius']*=scale
            d.rounded_rectangle(tuple(v*scale for v in box),**kw)
        finale=i==4
        rect((12,15,948,190),radius=25,fill=P['navy'])
        rect((8,8,944,182),radius=24,fill=P['manila'],outline=P['navy'],width=3)
        rect((16,8,944,173),radius=22,fill=P['green'] if finale else P['paper'],outline=P['navy'],width=3)
        rect((38,29,178,153),radius=20,fill=P['manila'] if finale else '#E6D8B5',outline=P['navy'],width=2)
        # Slim green/gold rule and stepped week progress are purely pictographic.
        d.line((212*scale,144*scale,888*scale,144*scale),fill=P['manila'] if finale else P['green'],width=3*scale)
        for j in range(5):
            cx=(788+j*24)*scale
            d.ellipse((cx,36*scale,cx+9*scale,45*scale),fill=(P['yellow'] if finale else P['green']) if j<=i else '#CBC8BC')
        textfit(d,label,(209*scale,49*scale,675*scale,76*scale),P['paper'] if finale else P['navy'],maxsize=77*scale)
        art=Image.open(ROOT/icon).convert('RGBA')
        art=art.crop(art.getchannel('A').getbbox())
        art.thumbnail((122*scale,116*scale),Image.Resampling.LANCZOS)
        out.alpha_composite(art,(108*scale-art.width//2,91*scale-art.height//2))
        if finale:
            rng=random.Random(20261029)
            # Confetti is restricted to margins so CUMA remains perfectly legible.
            for j in range(45):
                x=rng.randint(197,920); y=rng.choice([rng.randint(20,37),rng.randint(151,165)])
                color=rng.choice([P['yellow'],P['teal'],P['red'],P['manila']])
                d.line((x*scale,y*scale,(x+4)*scale,(y+4)*scale),fill=color,width=2*scale)
        path=ROOT/'week'/f'{name}.png'
        save(out.resize((960,200),Image.Resampling.LANCZOS),path)
        entries.append(metadata(path,'week',f'{label}. {desc} Turkish Unicode font lettering.',text=True))
    entries.append(metadata(ROOT/'week/week_trophy.png','week','Text-free Ayın Elemanı award: golden bean, blank plaque, laurel frame; real alpha.'))
    safe(ROOT/'week/_metadata.json').write_text(json.dumps(entries,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    sheet((ROOT/'week').glob('*.png'),ROOT/'_sheets/week.png',2,(660,220))
    ledger={'method':'Deterministic stationery layout, Turkish Unicode font, existing kit pictograms, generated trophy.',
            'font':'C:/Windows/Fonts/arialbd.ttf', 'script':'Art/UI/_tools/second_run_root.py',
            'banners':[{'id':name,'exactText':label,'codepoints':[f'U+{ord(c):04X}' for c in label],'iconSource':'Art/UI/'+icon} for name,label,icon,_ in DAYS]}
    safe(ROOT/'_sources/week_typography.json').write_text(json.dumps(ledger,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

def save_scene(im,path):
    save(im.convert('RGB'),path,1500000)
    # FASTOCTREE can average an opaque palette alpha to 254; scene exports must
    # carry no transparency table. Re-index the existing <=256 RGB colors.
    result=Image.open(path)
    if result.mode=='P':
        result=result.convert('RGB').quantize(colors=256,method=Image.Quantize.MEDIANCUT,dither=Image.Dither.NONE)
    else:result=result.convert('RGB')
    result.save(safe(path),optimize=True)

def store():
    src=Image.open(ROOT/'_sources/store_key_art_raw.png').convert('RGBA')
    key=ImageOps.fit(src,(1920,1080),Image.Resampling.LANCZOS)
    save_scene(key,ROOT/'store/key_art_1920x1080.png')
    # Preserve all faces by cropping lower table, never the top of the scene.
    logo=Image.open(ROOT/'logo/logo.png').convert('RGBA')
    configs=[('capsule_header_920x430',(920,430),330,(12,4)),
             ('capsule_main_1232x706',(1232,706),445,(18,12))]
    entries=[metadata(ROOT/'store/key_art_1920x1080.png','store','Text-free HR meeting: the six player beans point accusations while Hasan watches; projector shows sabotage.')]
    for name,size,lw,xy in configs:
        im=ImageOps.fit(src,size,Image.Resampling.LANCZOS,centering=(.5,0))
        mark=logo.resize((lw,round(lw*logo.height/logo.width)),Image.Resampling.LANCZOS)
        im.alpha_composite(mark,xy)
        path=ROOT/'store'/f'{name}.png'
        save_scene(im,path)
        entries.append(metadata(path,'store','BİRİMİZ KOVULACAK. Verified existing logo over six accusing employee beans and watching manager in the HR meeting.',text=True))
    safe(ROOT/'store/_metadata.json').write_text(json.dumps(entries,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    sheet((ROOT/'store').glob('*.png'),ROOT/'_sheets/store.png',1,(1000,610))
    ledger={'method':'One generated text-free scene, production crops, existing verified Unicode logo composited at final sizes.',
            'logoSource':'Art/UI/logo/logo.png','exactText':'BİRİMİZ KOVULACAK',
            'dottedCapitalIPositions':[i for i,c in enumerate('BİRİMİZ KOVULACAK') if c=='İ'],
            'script':'Art/UI/_tools/second_run_root.py','masterSource':'Art/UI/_sources/store_key_art_raw.png',
            'layouts':[{'id':n,'size':s,'logoWidth':lw,'logoOrigin':xy,'cropAnchor':[.5,0]} for n,s,lw,xy in configs]}
    safe(ROOT/'_sources/store_layouts.json').write_text(json.dumps(ledger,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

if __name__=='__main__':
    for task in sys.argv[1:] or ['week','store']:
        {'week':week,'store':store}[task]()
