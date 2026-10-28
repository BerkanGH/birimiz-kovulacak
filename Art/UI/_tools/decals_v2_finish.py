"""Third-run layout, exact Unicode lettering and review sheets. Writes only Art/UI."""
import sys
sys.dont_write_bytecode = True
from kit import *
from decals_v2_spec import SPECS
import hashlib
import random
import unicodedata

SCALE=3
RAW=ROOT/'_sources/decals_v2'
TEXT_RECORDS=[]

def box(values):
    return tuple(round(v*SCALE) for v in values)

def paper_frame(size, accent=None, portrait=False):
    w,h=size
    out=Image.new('RGB',(w*SCALE,h*SCALE),P['navy'])
    d=ImageDraw.Draw(out)
    # Opaque full-rectangle bevel: no wall/background baked into the texture.
    d.rectangle(box((3,3,w-4,h-4)),fill='#B28C4C' if portrait else P['manila'])
    d.polygon([box(p) for p in [(4,4),(w-5,4),(w-13,12),(12,12),(12,h-13),(4,h-5)]],fill='#F4D995')
    d.polygon([box(p) for p in [(w-5,4),(w-5,h-5),(4,h-5),(12,h-13),(w-13,h-13),(w-13,12)]],fill='#8D6839' if portrait else '#BFA36E')
    inset=17 if portrait else 12
    d.rectangle(box((inset-2,inset-2,w-inset+1,h-inset+1)),fill=P['navy'])
    d.rectangle(box((inset,inset,w-inset-1,h-inset-1)),fill=P['paper'])
    return out

def art_panel(out,src,rect,cover=False):
    x,y,w,h=rect
    art=Image.open(src).convert('RGB')
    dims=(round(w*SCALE),round(h*SCALE))
    if cover:
        art=ImageOps.fit(art,dims,Image.Resampling.LANCZOS)
    else:
        art=ImageOps.contain(art,dims,Image.Resampling.LANCZOS)
    out.paste(art,(round((x+w/2)*SCALE)-art.width//2,round((y+h/2)*SCALE)-art.height//2))

def label(out,name,lines,rect,fill,color,maxsize=64,stamp=False):
    x,y,w,h=rect
    d=ImageDraw.Draw(out)
    if fill:d.rectangle(box((x,y,x+w,y+h)),fill=fill)
    if stamp:
        d.rectangle(box((x+1,y+1,x+w-1,y+h-1)),outline=color,width=4*SCALE)
        d.rectangle(box((x+7,y+7,x+w-7,y+h-7)),outline=color,width=SCALE)
        rng=random.Random(117)
        for _ in range(55):
            px=rng.uniform(x+2,x+w-2)
            py=rng.choice([y+2,y+h-3])
            d.line(box((px,py,px+1.5,py)),fill=P['paper'],width=SCALE)
    available_w=w-16
    available_h=h-14
    f=None
    for size in range(round(maxsize*SCALE),10,-1):
        candidate=font(size,True)
        bounds=[d.textbbox((0,0),s,font=candidate) for s in lines]
        heights=[b[3]-b[1] for b in bounds]
        gap=round(size*.21)
        if max(b[2]-b[0] for b in bounds)<=available_w*SCALE and sum(heights)+(len(lines)-1)*gap<=available_h*SCALE:
            f=candidate
            break
    if f is None:raise ValueError('No fitting font: '+name)
    cy=(y+h/2)*SCALE-(sum(heights)+(len(lines)-1)*gap)/2
    placed=[]
    for line,b,th in zip(lines,bounds,heights):
        tx=(x+w/2)*SCALE-(b[2]-b[0])/2-b[0]
        ty=cy-b[1]
        d.text((round(tx),round(ty)),line,font=f,fill=color,stroke_width=0)
        placed.append([round((tx+b[0])/SCALE,2),round((ty+b[1])/SCALE,2),round((tx+b[2])/SCALE,2),round((ty+b[3])/SCALE,2)])
        cy+=th+gap
    TEXT_RECORDS.append(dict(id=name,lines=lines,text=' '.join(lines),font='C:/Windows/Fonts/arialbd.ttf',
        fontSizePx=round(size/SCALE,3),textBoxPx=list(rect),glyphBoundsPx=placed,
        codepoints=[f'U+{ord(c):04X}' for c in ' '.join(lines)],normalization='NFC'))

def finish_one(spec):
    name,size,world,text,lines,description=spec
    src=RAW/(name+'_raw.png')
    if not src.exists():return None
    w,h=size
    if name=='memo_board':
        out=ImageOps.fit(Image.open(src).convert('RGB'),(w*SCALE,h*SCALE),Image.Resampling.LANCZOS)
    elif name=='portrait_manager':
        out=paper_frame(size,portrait=True)
        art_panel(out,src,(21,21,w-42,h-42),cover=True)
    elif name=='logo_company':
        out=paper_frame(size)
        art_panel(out,src,(25,42,275,282))
        label(out,name,lines,(316,84,421,199),None,P['green'],maxsize=92)
        d=ImageDraw.Draw(out)
        d.line(box((328,309,718,309)),fill=P['manila'],width=4*SCALE)
    elif name=='diploma':
        out=paper_frame(size,portrait=True)
        art_panel(out,src,(22,22,w-44,h-44),cover=True)
        label(out,name,lines,(71,64,w-142,70),None,P['navy'],maxsize=52)
    else:
        out=paper_frame(size)
        red=name in ['poster_safety','poster_fridge_rules','sign_salt','sign_out_of_order','sign_server_danger']
        accent=P['red'] if red else P['green']
        if name=='poster_printer':accent=P['navy']
        if name=='poster_vending':accent=P['red']
        header_h=58 if len(lines)==1 else 110
        if name=='poster_motivation':header_h=144
        if name=='sign_out_of_order':header_h=83
        if name in ['sign_fusebox','sign_server_danger']:header_h=128
        if name=='poster_mission':header_h=66
        header_y=22
        # Large flat label panels reserve all diacritics within their exact bounds.
        if name=='sign_fusebox':
            header_y=h-header_h-22
            art_panel(out,src,(24,21,w-48,header_y-27))
            label(out,name,lines,(22,header_y,w-44,header_h),P['yellow'],P['navy'],maxsize=67)
        else:
            art_y=header_y+header_h+9
            art_h=h-art_y-29
            art_panel(out,src,(23,art_y,w-46,art_h))
            label(out,name,lines,(22,header_y,w-44,header_h),P['paper'] if name=='poster_safety' else accent,
                  accent if name=='poster_safety' else P['paper'],maxsize=64,stamp=name=='poster_safety')
            d=ImageDraw.Draw(out)
            d.line(box((37,h-23,w-37,h-23)),fill=accent,width=SCALE)
    path=ROOT/'decals'/(name+'.png')
    out.resize(size,Image.Resampling.LANCZOS).convert('RGB').save(safe(path),optimize=True)
    return metadata(path,'decals',(text+'. ' if text else '')+description+' Opaque wall texture; exact physical aspect ratio.',text=bool(text))

def contact_sheets(names):
    paths=[ROOT/'decals'/(name+'.png') for name in names]
    sheet(paths,ROOT/'_sheets/decals_v2.png',cols=7,cell=(310,340))
    # Screen-space approximation only, not a claim about the actual game camera.
    out=Image.new('RGB',(7*210,3*210),'#71757A')
    d=ImageDraw.Draw(out)
    for i,spec in enumerate(SPECS):
        name,size,world,*_=spec
        path=ROOT/'decals'/(name+'.png')
        if not path.exists():continue
        im=Image.open(path).convert('RGBA')
        # 140 px per world metre with 65% horizontal and 82% vertical foreshortening.
        ow=max(1,round(world[0]/100*140*.65));oh=max(1,round(world[1]/100*140*.82))
        im=im.resize((ow,oh),Image.Resampling.LANCZOS)
        x=(i%7)*210;y=(i//7)*210
        out.paste(im,(x+(210-ow)//2,y+13+(145-oh)//2),im)
        for j,line in enumerate(textwrap.wrap(name,27)):
            d.text((x+105,y+164+j*15),line,font=font(12),fill='white',anchor='mt')
    out.save(safe(ROOT/'_sheets/decals_v2_distance.png'),optimize=True)

def main(partial=False):
    entries=[]
    for spec in SPECS:
        entry=finish_one(spec)
        if entry:entries.append(entry)
    if len(entries)!=21 and not partial:raise RuntimeError(f'Only {len(entries)}/21 raw sources are present')
    safe(ROOT/'decals/_metadata_v2.json').write_text(json.dumps(entries,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    assert all(unicodedata.normalize('NFC',x['text'])==x['text'] for x in TEXT_RECORDS)
    ledger=dict(method='Built-in generated text-free art; deterministic stationery frames, composition and exact Unicode font typography.',
        script='Art/UI/_tools/decals_v2_finish.py',textRecords=TEXT_RECORDS,
        assets=[dict(id=s[0],sizePx=s[1],sizeCm=s[2],raw='Art/UI/_sources/decals_v2/'+s[0]+'_raw.png') for s in SPECS])
    safe(ROOT/'_sources/decals_v2/finishing.json').write_text(json.dumps(ledger,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    contact_sheets([x['id'] for x in entries])
    print(json.dumps(dict(finished=len(entries),required=21,textCount=len(TEXT_RECORDS)),ensure_ascii=False))

if __name__=='__main__':
    main('--partial' in sys.argv)
