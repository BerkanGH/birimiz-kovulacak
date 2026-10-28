import sys
sys.dont_write_bytecode=True
from kit import *
entries=json.loads((ROOT/'_sources/root_hud_prompts.json').read_text(encoding='utf-8'))
metas=[]; audits=[]
for e in entries:
    im=Image.open(e['source']).convert('RGBA')
    a=im.getchannel('A');a=a.point(lambda v: 0 if v<8 else v);im.putalpha(a)
    box=a.point(lambda v:255 if v>32 else 0).getbbox();im=im.crop(box)
    im.thumbnail((220,220),Image.Resampling.LANCZOS)
    out=Image.new('RGBA',(256,256));out.alpha_composite(im,((256-im.width)//2,(256-im.height)//2))
    path=ROOT/'icons'/(e['id']+'.png');save(out,path,80000)
    # Explicit RGBA alpha even if quantization was needed for size.
    re=Image.open(path).convert('RGBA');re.save(path,optimize=True)
    assert path.stat().st_size<80000, str(path)
    metas.append(metadata(path,'icons',e['prompt'].split('Subject: ')[-1],text=False))
    audits.append(audit(path))
safe(ROOT/'icons/_metadata_root.json').write_text(json.dumps(metas,ensure_ascii=False,indent=2),encoding='utf-8')
safe(ROOT/'_sources/root_hud_validation.json').write_text(json.dumps(audits,indent=2),encoding='utf-8')
sheet([ROOT/'icons'/(e['id']+'.png') for e in entries],ROOT/'_sheets/icons_hud_b.png',4,(240,240))
sheet([ROOT/'icons'/(e['id']+'.png') for e in entries],ROOT/'_sheets/icons_hud_b_48.png',4,(150,105))
print(json.dumps({'count':len(entries),'maxBytes':max(v['bytes'] for v in audits)}))
