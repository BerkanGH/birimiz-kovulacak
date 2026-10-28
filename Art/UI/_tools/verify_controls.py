"""Audit transparent UI controls and render a 9-slice review sheet."""
from pathlib import Path
import json
from PIL import Image,ImageDraw,ImageFont

ROOT=Path(__file__).resolve().parents[1]
entries=json.loads((ROOT/'buttons/_metadata.json').read_text(encoding='utf-8-sig'))
assert len(entries)==29
frames=json.loads((ROOT/'phone/_metadata_frames.json').read_text())+json.loads((ROOT/'meeting/_metadata_frames.json').read_text())
stats=[]
for e in entries+frames:
    p=ROOT/e['category']/(e['id']+'.png')
    im=Image.open(p).convert('RGBA')
    assert im.size==(e['w'],e['h'])
    a=im.getchannel('A')
    assert a.getextrema()[0]==0
    assert all(a.getpixel(c)==0 for c in [(0,0),(im.width-1,0),(0,im.height-1),(im.width-1,im.height-1)])
    if e['id'].startswith('ghost'):
        assert a.getpixel((im.width//2,im.height//2))==0
    if e['screenRect']:
        x,y,w,h=e['screenRect'];assert a.crop((x,y,x+w,y+h)).getextrema()==(0,0)
    stats.append({'id':e['id'],'bytes':p.stat().st_size,'cornerAlpha':[0,0,0,0], 'screenRectClear':bool(e['screenRect'])})

def sliced(im,size,border):
    l,t,r,b=border; w,h=im.size; dw,dh=size
    out=Image.new('RGBA',size)
    sx=[0,l,w-r,w];sy=[0,t,h-b,h];dx=[0,l,dw-r,dw];dy=[0,t,dh-b,dh]
    for iy in range(3):
        for ix in range(3):
            sw=sx[ix+1]-sx[ix];sh=sy[iy+1]-sy[iy]
            tw=dx[ix+1]-dx[ix];th=dy[iy+1]-dy[iy]
            if min(sw,sh,tw,th)<=0:continue
            tile=im.crop((sx[ix],sy[iy],sx[ix+1],sy[iy+1])).resize((tw,th),Image.Resampling.LANCZOS)
            out.alpha_composite(tile,(dx[ix],dy[iy]))
    return out

samples=[('primary_normal',(440,96)),('primary_pressed',(440,96)),('secondary_hover',(440,96)),('ghost_hover',(440,96)),
         ('panel_folder',(760,360)),('panel_sticky',(460,280)),('progress_bar_fill_green',(650,32)),('tab_active',(440,72))]
font=ImageFont.truetype('C:/Windows/Fonts/consola.ttf',17)
sheet_height=32+sum(size[1]+58 for _,size in samples)
out=Image.new('RGB',(1060,sheet_height),'#787B80');d=ImageDraw.Draw(out)
index={e['id']:e for e in entries}; y=24
for name,size in samples:
    e=index[name];im=Image.open(ROOT/'buttons'/(name+'.png')).convert('RGBA')
    tile=sliced(im,size,e['nineSlice'])
    d.text((24,y),f"{name}   {size[0]} x {size[1]}   {e['nineSlice']}",font=font,fill='#F4EFE3')
    out.paste(tile,(24,y+30),tile);y+=size[1]+58
out=out.crop((0,0,1060,y+8))
out.save(ROOT/'_sheets/controls_slice_review.png',optimize=True)
(ROOT/'_tools/controls_validation.json').write_text(json.dumps(stats,indent=2),encoding='utf-8')
print(f'{len(stats)} assets validated; ghost interiors and frame screen rectangles clear. Largest PNG: {max(s["bytes"] for s in stats):,} bytes.')
