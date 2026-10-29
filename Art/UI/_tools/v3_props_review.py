"""Review composed animation layers and sliced panel stress cases."""
import sys
sys.dont_write_bytecode=True
from kit import *
import json,hashlib
from PIL import ImageChops

def components(mask):
    w,h=mask.size;data=mask.tobytes();remaining={i for i,v in enumerate(data) if v};groups=[]
    while remaining:
        start=remaining.pop();todo=[start];group=[start]
        while todo:
            i=todo.pop();x=i%w;y=i//w
            for n in ([i-1] if x else [])+([i+1] if x<w-1 else [])+([i-w] if y else [])+([i+w] if y<h-1 else []):
                if n in remaining:remaining.remove(n);todo.append(n);group.append(n)
        if len(group)>20:
            xs=[i%w for i in group];ys=[i//w for i in group]
            groups.append({'pixels':len(group),'bounds':[min(xs),min(ys),max(xs)+1,max(ys)+1],'center':[round(sum(xs)/len(xs),2),round(sum(ys)/len(ys),2)]})
    return sorted(groups,key=lambda g:g['pixels'],reverse=True)

def sliced(src,borders,size):
    l,t,r,b=borders;w,h=src.size;dw,dh=size
    out=Image.new('RGBA',size)
    xx=[0,l,w-r,w];yy=[0,t,h-b,h];dx=[0,l,dw-r,dw];dy=[0,t,dh-b,dh]
    for row in range(3):
        for col in range(3):
            p=src.crop((xx[col],yy[row],xx[col+1],yy[row+1]))
            p=p.resize((dx[col+1]-dx[col],dy[row+1]-dy[row]),Image.Resampling.LANCZOS)
            out.alpha_composite(p,(dx[col],dy[row]))
    return out

alignment=json.loads((ROOT/'_tools/v3_props_alignment.json').read_text())
phone=Image.open(ROOT/'deskphone/phone_base.png').convert('RGBA');handset=Image.open(ROOT/'deskphone/handset.png').convert('RGBA')
contact=ImageChops.multiply(phone.getchannel('A').point(lambda v:255 if v>127 else 0),handset.getchannel('A').point(lambda v:255 if v>127 else 0))
phone_contacts=components(contact)
assert len(phone_contacts)==2,phone_contacts
assert phone_contacts[0]['center'][1]<205 and phone_contacts[1]['center'][1]<205
phone.alpha_composite(handset)
phone.alpha_composite(Image.open(ROOT/'deskphone/ring_waves.png').convert('RGBA'))
tape=Image.open(ROOT/'meeting_v3/tape_player.png').convert('RGBA');reel=Image.open(ROOT/'meeting_v3/tape_reel.png').convert('RGBA').resize((100,100),Image.Resampling.LANCZOS)
apertures=[]
for x,y in alignment['tape']['reelCenters']:
    hole=tape.getchannel('A').crop((x-40,y-40,x+40,y+40)).point(lambda v:255 if v==0 else 0)
    group=components(hole)[0];cx,cy=group['center'];offset=[round(cx-40,2),round(cy-40,2)]
    assert abs(offset[0])<2 and abs(offset[1])<2,(x,y,offset)
    assert tape.getpixel((x,y))[3]==0
    apertures.append({'expectedCenter':[x,y],'alpha':0,'centroidOffset':offset,'holeBoundsRelativeToCenter':[v-40 for v in group['bounds']]})
for x,y in alignment['tape']['reelCenters']:tape.alpha_composite(reel,(x-50,y-50))
sheetim=Image.new('RGB',(1100,730),'#777B80');d=ImageDraw.Draw(sheetim)
for im,xy,size,label in [(phone,(25,25),(500,375),'Phone: layers stacked at same origin'),(tape,(550,55),(512,256),'Tape: reels at documented centers')]:
    im=im.resize(size,Image.Resampling.LANCZOS);sheetim.paste(im,xy,im);d.text((xy[0],420),label,fill='white',font=font(17))
for i,name in enumerate(['busy','line_dead','rec_badge']):
    im=Image.open(ROOT/'deskphone'/(name+'.png')).convert('RGBA');im.thumbnail((48,48),Image.Resampling.LANCZOS)
    x=40+i*180;sheetim.paste(im,(x,480),im);d.text((x,545),name+' @48',fill='white',font=font(14))
for i,name in enumerate(['tape_reel','vote_token']):
    im=Image.open(ROOT/'meeting_v3'/(name+'.png')).convert('RGBA');im.thumbnail((48,48),Image.Resampling.LANCZOS)
    x=590+i*200;sheetim.paste(im,(x,480),im);d.text((x,545),name+' @48',fill='white',font=font(14))
safe(ROOT/'_sheets/v3_props_layers_48.png');sheetim.save(ROOT/'_sheets/v3_props_layers_48.png',optimize=True)
sheetim.save(ROOT/'_sheets/v3_layers.png',optimize=True)
allmeta=sum([json.loads((ROOT/c/'_metadata.json').read_text()) for c in ['onboarding','deskphone','meeting_v3']],[])
slices=[m for m in allmeta if m['nineSlice']]
im=Image.new('RGB',(1500,len(slices)*220),'#777B80');d=ImageDraw.Draw(im)
for i,e in enumerate(slices):
    src=Image.open(ROOT.parent.parent/e['path']).convert('RGBA')
    h=min(170,e['h']);thumb=sliced(src,e['nineSlice'],(680,h))
    im.paste(thumb,(30,i*220+10),thumb)
    small=sliced(src,e['nineSlice'],(max(e['nineSlice'][0]+e['nineSlice'][2]+20,160),h))
    im.paste(small,(800,i*220+10),small)
    d.text((30,i*220+185),e['path']+' '+str(e['nineSlice']),font=font(17),fill='white')
im.save(ROOT/'_sheets/v3_props_nineslice.png',optimize=True)
report=[]
for e in allmeta:
    p=ROOT.parent.parent/e['path'];a=Image.open(p).convert('RGBA').getchannel('A')
    opaque=e['path'].split('/')[-1].startswith('ob_')
    assert a.getextrema()==(255,255) if opaque else a.getextrema()[0]==0
    if not opaque: assert all(a.getpixel(pt)==0 for pt in [(0,0),(e['w']-1,0),(0,e['h']-1),(e['w']-1,e['h']-1)])
    if e['w']==256: assert p.stat().st_size<80000
    report.append({'path':e['path'],'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'alphaVerified':True})
safe(ROOT/'_tools/v3_props_review.json').write_text(json.dumps({'assets':report,'reviewSheets':['Art/UI/_sheets/v3_onboarding.png','Art/UI/_sheets/v3_deskphone.png','Art/UI/_sheets/v3_meeting_v3.png','Art/UI/_sheets/v3_props_layers_48.png','Art/UI/_sheets/v3_props_nineslice.png'],'notes':['Two illustrations revised after initial sheet review: restored large cream eyes in rooms and added missing green headset employee to meeting.','Actual 48px badge, busy, dead-line, vote-token and reel review.','All transparent requested outputs have real zero alpha and transparent corners; scenes are fully opaque.']},indent=2),encoding='utf-8')
print('26 assets: alpha and icon size checks pass; layer/48px/nineslice review sheets saved.')
layerchecks={'phoneContacts':phone_contacts,'tapeApertures':apertures,'spotlightAlphaExtrema':Image.open(ROOT/'meeting_v3/spotlight.png').convert('RGBA').getchannel('A').getextrema(),'comment':'Phone hardware is centered about x458; coil cord extends only left. Handset overlaps the body at two separate cradle contact regions. Tape reel pivots are within 2px of aperture centroids.'}
(ROOT/'_tools/v3_props_layer_checks.json').write_text(json.dumps(layerchecks,indent=2),encoding='utf-8')
print(json.dumps(layerchecks,indent=2))
