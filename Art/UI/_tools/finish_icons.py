import sys, json
sys.dont_write_bytecode = True
from kit import ROOT, save, audit, sheet, metadata, Image

ledger=json.loads((ROOT/'_sources/icons_prompts.json').read_text(encoding='utf-8'))
items=[]
for entry in ledger:
    src=entry['source']
    if not src: continue
    category='phone' if entry['id'].startswith('app_') else 'icons'
    size=192 if category=='phone' else 256
    dst=ROOT/category/(entry['id']+'.png')
    if not dst.exists() or '--force' in sys.argv:
        im=Image.open(src).convert('RGBA')
        a=im.getchannel('A')
        if a.getextrema()[0] != 0:
            raise RuntimeError('Source lacks transparency: '+entry['id'])
        # Ignore imperceptible generated alpha haze when setting optical bounds.
        # Crop original pixels, preserve edge antialias, then downsample once.
        bounds=a.point(lambda v: 255 if v>64 else 0).getbbox()
        if not bounds: raise RuntimeError('Empty source '+entry['id'])
        im=im.crop(bounds)
        pad=18 if category=='icons' else 10
        im.thumbnail((size-2*pad,size-2*pad),Image.Resampling.LANCZOS)
        out=Image.new('RGBA',(size,size))
        out.alpha_composite(im,((size-im.width)//2,(size-im.height)//2))
        save(out,dst,limit=79999)
    report=audit(dst)
    if report['cornerAlpha'] != [0,0,0,0] or report['alphaExtrema'] != (0,255):
        raise RuntimeError('Bad alpha '+entry['id'])
    if report['bytes']>=80000: raise RuntimeError('Too large '+entry['id'])
    items.append(metadata(dst,category,entry['subject'],text=entry['id']=='info'))

for category in ['icons','phone']:
    entries=[i for i in items if i['category']==category]
    if not entries: continue
    target=ROOT/category/('_metadata.json' if category=='icons' else '_metadata_apps.json')
    target.write_text(json.dumps(entries,ensure_ascii=False,indent=2),encoding='utf-8')
    sheet([ROOT/category/(i['id']+'.png') for i in entries],ROOT/'_sheets'/('icons.png' if category=='icons' else 'phone_apps.png'),cols=7 if category=='icons' else 6)
print(json.dumps({'count':len(items),'largest':max(audit(ROOT/i['category']/(i['id']+'.png'))['bytes'] for i in items)}))
