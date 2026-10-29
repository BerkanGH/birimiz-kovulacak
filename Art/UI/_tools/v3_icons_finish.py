"""Finish generated icons and their deterministic A/Ö lettering; only Art/UI writes."""
import sys
sys.dont_write_bytecode = True
import json, hashlib, shutil
from kit import ROOT, safe, save, Image, ImageDraw, font, metadata, sheet, audit
from fontTools.ttLib import TTFont

def finish_icons():
    records=json.loads((ROOT/'_tools/v3_icons_sources.json').read_text(encoding='utf-8'))
    entries=[]
    for row in records:
        target=ROOT/'icons_v3'/(row['id']+'.png')
        archive=ROOT/'_tools/v3_raw_icons'/(row.get('archiveName',row['id'])+'.png')
        safe(archive)
        if not archive.exists(): shutil.copyfile(row['source'],archive)
        im=Image.open(archive).convert('RGBA')
        a=im.getchannel('A')
        assert a.getextrema()[0]==0, row['id']+' lacks real alpha'
        bounds=a.point(lambda v:255 if v>64 else 0).getbbox()
        im=im.crop(bounds)
        im.thumbnail((220,220),Image.Resampling.LANCZOS)
        out=Image.new('RGBA',(256,256))
        out.alpha_composite(im,((256-im.width)//2,(256-im.height)//2))
        if row['id']=='language':
            # Exact letters on inspected blank bubbles. The slash is deliberately
            # visible as well, preserving the requested A/Ö string.
            high=out.resize((768,768),Image.Resampling.LANCZOS)
            d=ImageDraw.Draw(high); glyphs=[]
            cmap=TTFont('C:/Windows/Fonts/arialbd.ttf').getBestCmap()
            for ch,x,y,size in [('A',91,139,64),('/',143,137,43),('Ö',190,96,58)]:
                assert ord(ch) in cmap and cmap[ord(ch)]!='.notdef'
                f=font(size*3,True); b=d.textbbox((0,0),ch,font=f)
                px=round(x*3-(b[2]+b[0])/2); py=round(y*3-(b[3]+b[1])/2)
                d.text((px,py),ch,font=f,fill='#1F2A44')
                glyphs.append(dict(character=ch,codepoint=f'U+{ord(ch):04X}',fontGlyph=cmap[ord(ch)],bounds=[(px+b[0])/3,(py+b[1])/3,(px+b[2])/3,(py+b[3])/3]))
            out=high.resize((256,256),Image.Resampling.LANCZOS)
            safe(ROOT/'_tools/v3_language_typography.json').write_text(json.dumps(dict(text='A/Ö',codepoints=['U+0041','U+002F','U+00D6'],font='Arial Bold',glyphsVerified=True,glyphs=glyphs),ensure_ascii=False,indent=2),encoding='utf-8')
        save(out,target,79999)
        entry=metadata(target,'icons_v3',row.get('subject',row['id'].replace('_',' '))+'; stationery cutout, reviewed at 48 px.',text=row['id']=='language')
        entry['id']='v3_'+row['id']
        entries.append(entry)
        row['archivedSource']='Art/UI/'+str(archive.relative_to(ROOT)).replace('\\','/')
        row['sourceSha256']=hashlib.sha256(archive.read_bytes()).hexdigest()
        row['audit']=audit(target)
    safe(ROOT/'icons_v3/_metadata.json').write_text(json.dumps(entries,ensure_ascii=False,indent=2),encoding='utf-8')
    (ROOT/'_tools/v3_icons_sources.json').write_text(json.dumps(records,ensure_ascii=False,indent=2),encoding='utf-8')
    all_records=records[:]
    extra=ROOT/'_tools/v3_icons_extra_sources.json'
    if extra.exists(): all_records+=json.loads(extra.read_text(encoding='utf-8'))
    sheet([ROOT/'icons_v3'/(r['id']+'.png') for r in all_records],ROOT/'_sheets/v3_icons_v3.png',cols=7,cell=(170,182))
    # Actual 48px display size on both light paper and medium-grey grounds.
    cols=7; rows=(len(all_records)+cols-1)//cols
    sh=Image.new('RGB',(cols*154,rows*132),'#F4EFE3'); d=ImageDraw.Draw(sh)
    for i,r in enumerate(all_records):
        x=i%cols*154; y=i//cols*132
        d.rectangle((x+77,y,x+153,y+91),fill='#777B80')
        icon=Image.open(ROOT/'icons_v3'/(r['id']+'.png')).convert('RGBA').resize((48,48),Image.Resampling.LANCZOS)
        sh.paste(icon,(x+14,y+18),icon); sh.paste(icon,(x+92,y+18),icon)
        d.text((x+77,y+98),r['id'],font=font(12),fill='#1F2A44',anchor='mt')
    save(sh,ROOT/'_sheets/v3_icons_48.png')
    print(json.dumps({'count':len(entries),'maxBytes':max(r['audit']['bytes'] for r in records)}))

if __name__=='__main__': finish_icons()
