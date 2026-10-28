import sys
sys.dont_write_bytecode=True
from kit import *
import json

records=json.loads((ROOT/'_sources/emotes_prompts.json').read_text(encoding='utf-8'))
paths=[];meta=[];audits=[]
for rec in records:
    path=ROOT/'icons'/f"{rec['id']}.png"
    finish(rec['source'],path,256,256,pad=18,trim=True,limit=79999)
    im=Image.open(path).convert('RGBA');im.save(path,optimize=True)
    if path.stat().st_size>=80000:
        im.quantize(colors=192,method=Image.Quantize.FASTOCTREE,dither=Image.Dither.NONE).convert('RGBA').save(path,optimize=True)
    paths.append(path);meta.append(metadata(path,'icons','Blue bean '+rec['id'].replace('emote_','')+' gesture. Text-free emote, readable at 48px.'))
    audits.append(audit(path))
    assert im.getchannel('A').getpixel((0,0))==0
    assert path.stat().st_size<80000
(ROOT/'icons/_metadata_emotes.json').write_text(json.dumps(meta,ensure_ascii=False,indent=2),encoding='utf-8')
(ROOT/'_sources/emotes_audit.json').write_text(json.dumps(audits,indent=2),encoding='utf-8')
sheet(paths,ROOT/'_sheets/icons_emotes.png',cols=5,cell=(210,230))
out=Image.new('RGB',(5*160,110),(113,117,122));d=ImageDraw.Draw(out)
for i,path in enumerate(paths):
    im=Image.open(path).convert('RGBA').resize((48,48),Image.Resampling.LANCZOS)
    out.paste(im,(i*160+56,12),im)
    d.text((i*160+80,74),path.stem,font=font(12),fill='white',anchor='mt')
out.save(ROOT/'_sheets/icons_emotes_48px.png',optimize=True)
print(json.dumps(audits,indent=2))
