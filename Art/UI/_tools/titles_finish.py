"""Finish second-run title badge sources, preserving real alpha. Writes Art/UI only."""
import sys
sys.dont_write_bytecode = True
from kit import ROOT, Image, ImageDraw, ImageOps, font, safe, metadata, audit, sheet
import json

DESCRIPTIONS = {
 'title_chaos_agent': 'Red sabotage bolt and fanned cards; chaos agent award.',
 'title_cleanest_desk': 'Sparkling tidy desk with closed green laptop and aligned reports; cleanest desk award.',
 'title_coffee_master': 'Crowned steaming office-green mug; coffee master award.',
 'title_favourite': 'Crowned red heart and green ribbon; manager favourite award.',
 'title_informant_of_month': 'Teal tip-line handset and watchful eye on gold star; informant of the month award.',
 'title_it_hero': 'Repair wrench and network plug over green shield; IT hero award.',
 'title_printer_enemy': 'Office printer jammed with crumpled paper and red distress bolts; printer enemy award.',
 'title_pro_scapegoat': 'Worried Mavi bean in green tie with accusing hands; professional scapegoat award.',
 'title_salt_bae': 'Salt shaker dropping salt into office-green coffee mug; salt sabotage award.',
 'title_scam_artist': 'Mischievous masked envelope with red wax seal; scam artist award.'
}
records=json.loads((ROOT/'_sources/titles_generation_records.json').read_text(encoding='utf-8'))
audits=[]
meta=[]
for r in records:
    im=Image.open(ROOT/'_sources'/f"{r['id']}_raw.png").convert('RGBA')
    alpha=im.getchannel('A')
    bbox=alpha.point(lambda x: 255 if x > 32 else 0).getbbox()
    assert bbox, r['id']
    im=im.crop(bbox)
    im.thumbnail((232,232),Image.Resampling.LANCZOS)
    out=Image.new('RGBA',(256,256))
    out.alpha_composite(im,((256-im.width)//2,(256-im.height)//2))
    # Quantize color only: do not merge transparent and opaque samples in one palette.
    alpha=out.getchannel('A')
    out=out.convert('RGB').quantize(colors=192,method=Image.Quantize.MEDIANCUT,dither=Image.Dither.NONE).convert('RGBA')
    out.putalpha(alpha)
    p=safe(ROOT/'titles'/f"{r['id']}.png")
    out.save(p,optimize=True)
    assert p.stat().st_size<80000,(r['id'],p.stat().st_size)
    assert out.getchannel('A').getextrema()==(0,255),r['id']
    assert all(out.getpixel(x)[3]==0 for x in [(0,0),(255,0),(0,255),(255,255)])
    audits.append(audit(p))
    meta.append(metadata(p,'titles',DESCRIPTIONS[r['id']]+' Text-free cream round medallion with navy/gold rim; true transparent exterior; fixed aspect.'))
safe(ROOT/'titles/_metadata.json').write_text(json.dumps(meta,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
safe(ROOT/'_sources/titles_audit.json').write_text(json.dumps(audits,indent=2)+'\n',encoding='utf-8')
paths=sorted((ROOT/'titles').glob('*.png'))
sheet(paths,ROOT/'_sheets/titles.png',cols=5,cell=(300,316))
# Actual 48-pixel review, repeated on grey and paper to expose alpha fringes.
out=Image.new('RGB',(1000,440),'#71757A')
d=ImageDraw.Draw(out)
for i,p in enumerate(paths):
    x=(i%5)*200;y=(i//5)*220
    d.rectangle((x+100,y,x+199,y+219),fill='#F4EFE3')
    im=Image.open(p).convert('RGBA').resize((48,48),Image.Resampling.LANCZOS)
    out.paste(im,(x+26,y+24),im);out.paste(im,(x+126,y+24),im)
    lines=p.stem.replace('title_','').replace('_',' ').split()
    label=' '.join(lines)
    import textwrap
    for j,line in enumerate(textwrap.wrap(label,15)):
        d.text((x+50,y+100+j*20),line,font=font(12),anchor='mt',fill='white')
        d.text((x+150,y+100+j*20),line,font=font(12),anchor='mt',fill='#1F2A44')
safe(ROOT/'_sheets/titles_48.png')
out.save(ROOT/'_sheets/titles_48.png',optimize=True)
print(json.dumps({'count':len(meta),'largestBytes':max(a['bytes'] for a in audits),'alpha':'all 0..255','sheets':['Art/UI/_sheets/titles.png','Art/UI/_sheets/titles_48.png']},indent=2))
