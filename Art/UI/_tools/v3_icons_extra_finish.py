"""Finish nine v3 office icons using the source kit's alpha and optical-bound rules."""
import sys
sys.dont_write_bytecode=True
from kit import *
import hashlib, shutil

records=json.loads((ROOT/'_tools/v3_icons_extra_sources.json').read_text(encoding='utf-8'))
assert len(records)==9
descriptions={
 'coffee_spill':'Tipping green mug pouring coffee onto a blank-key keyboard.',
 'phone_dead':'Green handset with a visibly cut coiled telephone cord.',
 'wifi_off':'Crossed Wi-Fi arcs above a navy-and-cream office router.',
 'manager_line':'Red manager desk phone with blank cream keys and green LCD.',
 'tip_off':'Blue bean with green tie cups its hand to whisper a confidential tip.',
 'intern':'Small yellow intern bean wearing an oversized teal lanyard and blank ID.',
 'defense':'Manila podium with a large gooseneck microphone and blank sheet.',
 'meeting':'Oval manila meeting table and four office-green chairs.',
 'morning':'Morning sunrise behind a steaming office-green coffee mug.',
}
meta=[];audits=[]
for record in records:
    archived=safe(ROOT/record['archivedSource'])
    if not archived.exists(): shutil.copyfile(record['source'],archived)
    source=Image.open(archived).convert('RGBA')
    alpha=source.getchannel('A')
    assert alpha.getextrema()==(0,255),record['id']
    bbox=alpha.point(lambda v:255 if v>64 else 0).getbbox()
    source=source.crop(bbox)
    source.thumbnail((220,220),Image.Resampling.LANCZOS)
    out=Image.new('RGBA',(256,256))
    out.alpha_composite(source,((256-source.width)//2,(256-source.height)//2))
    path=ROOT/'icons_v3'/(record['id']+'.png')
    save(out,path,79999)
    a=audit(path)
    assert a['alphaExtrema']==(0,255)
    assert a['cornerAlpha']==[0,0,0,0]
    assert a['bytes']<80000
    m=metadata(path,'icons_v3',descriptions[record['id']]+' Isolated silhouette, meaningful-alpha crop and centered 220px optical bounds; readable at 48px.')
    m['id']='v3_'+record['id']
    meta.append(m)
    a['sha256']=hashlib.sha256(path.read_bytes()).hexdigest()
    audits.append(a)
(ROOT/'icons_v3/_metadata_extra.json').write_text(json.dumps(meta,ensure_ascii=False,indent=2),encoding='utf-8')
(ROOT/'_tools/v3_icons_extra_audit.json').write_text(json.dumps(audits,ensure_ascii=False,indent=2),encoding='utf-8')
# A single row at exactly 48x48 for unambiguous small-size review.
out=Image.new('RGB',(9*120,106),(113,117,122));d=ImageDraw.Draw(out)
for i,record in enumerate(records):
    im=Image.open(ROOT/'icons_v3'/(record['id']+'.png')).convert('RGBA').resize((48,48),Image.Resampling.LANCZOS)
    out.paste(im,(i*120+36,12),im)
    d.text((i*120+60,72),record['id'],fill='white',font=font(12),anchor='mt')
save(out,ROOT/'_sheets/v3_icons_extra.png')
print(json.dumps({'count':len(meta),'maxBytes':max(a['bytes'] for a in audits),'files':[a['path'] for a in audits]},indent=2))
