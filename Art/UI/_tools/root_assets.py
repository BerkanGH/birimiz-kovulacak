"""Deterministic typography and stationery finishing for the generated kit."""
import sys
sys.dont_write_bytecode=True
from kit import *
import random

def textfit(draw,text,box,color,bold=True,maxsize=220):
    x,y,w,h=box
    for sz in range(maxsize,8,-1):
        f=font(sz,bold); bb=draw.textbbox((0,0),text,font=f)
        if bb[2]-bb[0] <= w and bb[3]-bb[1] <= h: break
    draw.text((x+(w-bb[2]+bb[0])/2-bb[0],y+(h-bb[3]+bb[1])/2-bb[1]),text,font=f,fill=color)

def distress(im,seed=42,density=900):
    rng=random.Random(seed); a=im.getchannel('A'); d=ImageDraw.Draw(a)
    for _ in range(density):
        x=rng.randrange(im.width); y=rng.randrange(im.height); r=rng.choice([1,1,1,2,2,3])
        d.ellipse((x,y,x+r,y+r),fill=0)
    im.putalpha(a);return im

def logo():
    im=Image.new('RGBA',(1600,700)); back=Image.new('RGBA',(1450,520));d=ImageDraw.Draw(back)
    d.rounded_rectangle((7,7,1440,510),radius=40,fill=P['manila'],outline=P['navy'],width=8)
    back=back.rotate(3,Image.Resampling.BICUBIC,expand=True);im.alpha_composite(back,(55,70))
    front=Image.new('RGBA',(1490,540));d=ImageDraw.Draw(front)
    d.rounded_rectangle((7,7,1482,530),radius=40,fill=P['paper'],outline=P['navy'],width=9)
    d.line((440,46,440,485),fill='#D2C8B4',width=3)
    d.line((480,65,1410,65),fill=P['green'],width=10)
    textfit(d,'BİRİMİZ',(490,90,920,150),P['navy'],maxsize=190)
    stamp=Image.new('RGBA',(1020,230));ds=ImageDraw.Draw(stamp)
    ds.rounded_rectangle((6,6,1013,223),radius=15,outline=P['red'],width=12)
    ds.rounded_rectangle((25,25,994,204),radius=6,outline=P['red'],width=3)
    textfit(ds,'KOVULACAK',(42,42,934,146),P['red'],maxsize=165)
    stamp=distress(stamp,44,1500).rotate(2,Image.Resampling.BICUBIC,expand=True)
    front.alpha_composite(stamp,(450,265))
    mark=Image.open(ROOT/'logo/logo_mark.png').convert('RGBA');mark.thumbnail((420,490),Image.Resampling.LANCZOS)
    front.alpha_composite(mark,((445-mark.width)//2,40))
    im.alpha_composite(front,(55,80))
    d=ImageDraw.Draw(im)
    d.rounded_rectangle((1380,58,1413,166),radius=16,outline=P['navy'],width=7)
    d.rounded_rectangle((1391,66,1404,151),radius=6,outline='#B4B6A7',width=4)
    save(im,ROOT/'logo/logo.png')
    sheet((ROOT/'logo').glob('*.png'),ROOT/'_sheets/logo.png',2,(640,360))

def stamps():
    vals=[('kovuldu','KOVULDU',P['red']),('kurtuldu','KURTULDU',P['green']),('onaylandi','ONAYLANDI',P['green']),('reddedildi','REDDEDİLDİ',P['red']),('gizli','GİZLİ',P['teal']),('ayin_elemani','AYIN ELEMANI','#AD8037')]
    for i,(id,txt,col) in enumerate(vals):
        w,h=960,320
        layer=Image.new('RGBA',(876,230));d=ImageDraw.Draw(layer)
        d.rounded_rectangle((6,6,868,222),radius=16,outline=col,width=11)
        d.rounded_rectangle((24,24,850,204),radius=5,outline=col,width=3)
        textfit(d,txt,(44,42,786,144),col,maxsize=140)
        distress(layer,60+i,1600)
        layer=layer.rotate([-3,2,-2,3,-3,2][i],Image.Resampling.BICUBIC,expand=True)
        out=Image.new('RGBA',(w,h));out.alpha_composite(layer,((w-layer.width)//2,(h-layer.height)//2))
        save(out,ROOT/'stamps'/('stamp_'+id+'.png'))
    sheet((ROOT/'stamps').glob('*.png'),ROOT/'_sheets/stamps.png',2,(600,235))

def paper():
    # Periodic fine fibers, mirrored random micro-grain: exact matching edge samples.
    rng=random.Random(51); small=Image.new('RGB',(256,256));p=small.load()
    for y in range(256):
        for x in range(256):
            n=rng.choices([-2,-1,0,1,2],[1,3,8,3,1])[0]
            p[x,y]=(244+n,239+n,227+n)
    im=Image.new('RGB',(512,512));im.paste(small,(0,0));im.paste(ImageOps.mirror(small),(256,0));im.paste(ImageOps.flip(small),(0,256));im.paste(ImageOps.flip(ImageOps.mirror(small)),(256,256))
    save(im,ROOT/'backgrounds/paper_tile.png',1500000)

def decals():
    entries=json.loads((ROOT/'_sources/root_decals.json').read_text(encoding='utf-8'))
    titles=['TAKIM RUHU','VERİMLİLİK','SADAKAT','KAHVE']
    for i,e in enumerate(entries):
        if e['id'].startswith('poster_'):
            im=Image.new('RGBA',(512,768));d=ImageDraw.Draw(im)
            d.rounded_rectangle((12,12,499,755),radius=18,fill=P['manila'],outline=P['navy'],width=5)
            d.rounded_rectangle((26,26,485,741),radius=10,fill=P['paper'],outline='#D2C4A4',width=2)
            d.rounded_rectangle((44,48,467,159),radius=6,fill=P['green'])
            textfit(d,titles[i],(62,60,387,85),P['paper'],maxsize=64)
            art=Image.open(e['source']).convert('RGBA');art=art.crop(art.getchannel('A').getbbox());art.thumbnail((430,470),Image.Resampling.LANCZOS)
            im.alpha_composite(art,((512-art.width)//2,190+(470-art.height)//2))
            d=ImageDraw.Draw(im);d.line((72,692,439,692),fill=P['green'],width=3)
            for x in [231,251,271]:d.ellipse((x,712,x+8,720),fill=P['red'])
            save(im,ROOT/'decals'/(e['id']+'.png'))
        else:
            dims={'whiteboard_doodles':(1024,512),'corkboard_notes':(1024,768),'wet_floor':(512,768)}
            if e['id']=='wet_floor':
                src=Image.open(e['source']).convert('RGBA'); a=src.getchannel('A')
                a=a.point(lambda v: 0 if v<245 else 255).filter(ImageFilter.GaussianBlur(0.4))
                src.putalpha(a);save(src,ROOT/'_sources/wet_floor_cutout.png');e['source']=str(ROOT/'_sources/wet_floor_cutout.png')
            finish(e['source'],ROOT/'decals'/(e['id']+'.png'),*dims[e['id']],trim=True,pad=16)
    # EXIT lettering is rendered from Unicode, with a clear directional arrow.
    im=Image.new('RGBA',(1024,512));d=ImageDraw.Draw(im)
    d.rounded_rectangle((18,18,1005,493),radius=38,fill=P['green'],outline=P['navy'],width=12)
    d.rounded_rectangle((40,40,983,471),radius=26,outline=P['paper'],width=5)
    textfit(d,'ÇIKIŞ',(70,110,590,280),P['paper'],maxsize=220)
    d.polygon([(700,201),(822,201),(822,132),(942,256),(822,380),(822,311),(700,311)],fill=P['paper'])
    save(im.resize((512,256),Image.Resampling.LANCZOS),ROOT/'decals/exit_sign.png')
    sheet((ROOT/'decals').glob('*.png'),ROOT/'_sheets/decals.png',4,(320,420))
    m=[metadata(p,'decals','Turkish office parody poster / wall decoration.',text=p.stem.startswith('poster_') or p.stem=='exit_sign') for p in sorted((ROOT/'decals').glob('*.png'))]
    safe(ROOT/'decals/_metadata.json').write_text(json.dumps(m,ensure_ascii=False,indent=2),encoding='utf-8')

def category_metadata(cat):
    descriptions={
      'logo':'Turkish title composed with deterministic font over paper and rubber stamp; generated bean emblem.',
      'stamps':'Distressed transparent rubber stamp. Turkish glyphs rendered deterministically.',
      'backgrounds':'Text-free warm office illustration; interface overlays supplied by the game.'}
    m=[metadata(p,cat,descriptions.get(cat,''),text=(cat=='stamps' or p.stem=='logo')) for p in sorted((ROOT/cat).glob('*.png'))]
    safe(ROOT/cat/'_metadata.json').write_text(json.dumps(m,ensure_ascii=False,indent=2),encoding='utf-8')

def letter():
    im=Image.new('RGBA',(1200,1600));d=ImageDraw.Draw(im)
    d.rounded_rectangle((24,24,1175,1575),radius=16,fill=P['paper'],outline=P['navy'],width=5)
    # This is deliberately blank letterhead; gameplay supplies all copy.
    mark=Image.open(ROOT/'logo/logo_mark.png').convert('RGBA');mark.thumbnail((118,150),Image.Resampling.LANCZOS)
    im.alpha_composite(mark,(92,80));d=ImageDraw.Draw(im)
    d.line((255,130,1080,130),fill=P['green'],width=9)
    d.line((255,155,1080,155),fill='#C9CCB7',width=3)
    d.line((90,265,1110,265),fill='#D9D1BF',width=2)
    d.line((90,1450,1110,1450),fill=P['green'],width=4)
    for x in [108,130,152]:d.ellipse((x,1493,x+8,1501),fill=P['manila'])
    # Pale company emblem watermark; remains readable beneath a red verdict stamp.
    watermark=Image.open(ROOT/'logo/logo_mark.png').convert('RGBA');watermark.thumbnail((380,380),Image.Resampling.LANCZOS)
    alpha=watermark.getchannel('A').point(lambda v: int(v*.045));watermark=Image.new('RGBA',watermark.size,P['green']);watermark.putalpha(alpha)
    im.alpha_composite(watermark,(780,1030))
    save(im,ROOT/'meeting/termination_letter.png')

def finish_scenery():
    letter()
    for path in list((ROOT/'backgrounds').glob('*.png'))+[ROOT/'phone/call_screen_bg.png']:
        im=Image.open(path).convert('RGB');save(im,path,1500000)
    category_metadata('backgrounds')
    m=[metadata(ROOT/'meeting'/f'{n}.png','meeting',desc) for n,desc in [
      ('evidence_folder','Manila evidence dossier with camera photo, red elastic and eye seal.'),
      ('week_board','Exactly five day columns; blank headers and two blank notes per day; Friday gold star.'),
      ('termination_letter','Blank company letterhead with generated emblem; ample space for game text and verdict stamp.')]]
    safe(ROOT/'meeting/_metadata_root.json').write_text(json.dumps(m,ensure_ascii=False,indent=2),encoding='utf-8')
    m=[metadata(ROOT/'phone/call_screen_bg.png','phone','Opaque 504x984 incoming-call wallpaper, fits phone screenRect exactly.')]
    safe(ROOT/'phone/_metadata_bg.json').write_text(json.dumps(m,ensure_ascii=False,indent=2),encoding='utf-8')
    sheet((ROOT/'meeting').glob('*.png'),ROOT/'_sheets/meeting.png',3,(480,420))
    sheet((ROOT/'backgrounds').glob('*.png'),ROOT/'_sheets/backgrounds.png',2,(640,405))

if __name__=='__main__':
    for item in sys.argv[1:]:
        if item=='logo':logo();category_metadata('logo')
        elif item=='stamps':stamps();category_metadata('stamps')
        elif item=='paper':paper()
        elif item=='decals':decals()
        elif item=='scenery':finish_scenery()
