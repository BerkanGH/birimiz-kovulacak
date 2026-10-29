"""V3 onboarding, desk-phone and meeting finishing. Outputs restricted to Art/UI."""
import sys
sys.dont_write_bytecode = True
from kit import *
import random, hashlib, shutil

S=3
NAVY=P['navy']; PAPER=P['paper']; GREEN=P['green']; MANILA=P['manila']; RED=P['red']
ENTRIES={}; REVIEW=[]
SOURCES=json.loads((ROOT/'_tools/v3_props_sources.json').read_text(encoding='utf-8-sig'))
def cv(w,h): return Image.new('RGBA',(w*S,h*S))
def b(v): return tuple(round(x*S) for x in v)
def rr(im,box,r,fill,outline=NAVY,width=3): ImageDraw.Draw(im).rounded_rectangle(b(box),round(r*S),fill,outline,round(width*S))
def el(im,box,fill,outline=None,width=2): ImageDraw.Draw(im).ellipse(b(box),fill,outline,round(width*S))
def ln(im,pts,fill=NAVY,width=4):
    d=ImageDraw.Draw(im); p=[(round(x*S),round(y*S)) for x,y in pts];d.line(p,fill,width*S,joint='curve')
    r=width*S/2
    for x,y in [p[0],p[-1]]: d.ellipse((x-r,y-r,x+r,y+r),fill)
def poly(im,pts,fill,outline=None,width=3):
    ImageDraw.Draw(im).polygon([(round(x*S),round(y*S)) for x,y in pts],fill)
    if outline: ln(im,pts+[pts[0]],outline,width)
def arc(im,box,start,end,fill=NAVY,width=4): ImageDraw.Draw(im).arc(b(box),start,end,fill,width*S)
def out(im,cat,name,desc,nine=None,screen=None,sup=True,limit=400000):
    if sup: im=im.resize((im.width//S,im.height//S),Image.Resampling.LANCZOS)
    path=ROOT/cat/(name+'.png');save(im,path,limit)
    entry=metadata(path,cat,desc,nine,screen)
    entry['id']=cat+'_'+name if name in ['warning_letter','busy','line_dead','key','key_pressed','nameplate'] else name
    ENTRIES.setdefault(cat,[]).append(entry)
    REVIEW.append(audit(path));return path
def source(name):
    e=next(e for e in SOURCES if e['id']==name)
    archive_name=e.get('archive_name',name)
    dest=ROOT/'_tools/v3_props_sources'/(archive_name+'.png'); safe(dest)
    if not dest.exists(): shutil.copyfile(e['source'],dest)
    e['archive']='Art/UI/_tools/v3_props_sources/'+archive_name+'.png'
    e['sha256']=hashlib.sha256(dest.read_bytes()).hexdigest()
    return Image.open(dest).convert('RGBA')
def paste_fit(src,canvas,box):
    src=src.crop(src.getchannel('A').getbbox())
    x,y,w,h=box;src.thumbnail((w,h),Image.Resampling.LANCZOS)
    pos=(x+(w-src.width)//2,y+(h-src.height)//2);canvas.alpha_composite(src,pos)
    return {'position':pos,'size':src.size}

def onboarding():
    descs=['Six employees alone in six offices, linked only by desk-phone cords.','Two separate offices: pink bean whispers, yellow bean listens on desk phones.','Blue employee calls while reading a bonus round card on the computer.','Black blame strikes accumulate as the manager writes the record.','A sneaky employee tips coffee onto a keyboard while using the phone.','Manager plays a cassette recording; the meeting reacts to one employee.','One employee exits carrying a box; five coworkers remain relieved.']
    for e,desc in zip([e for e in SOURCES if e['id'].startswith('ob_')],descs):
        im=ImageOps.fit(source(e['id']).convert('RGB'),(1280,720),Image.Resampling.LANCZOS)
        out(im,'onboarding',e['id'],desc,sup=False,limit=1490000)
    im=cv(384,256)
    rr(im,(12,14,372,245),6,'#C9B470',None)
    rr(im,(10,10,370,238),6,P['yellow'],NAVY,3)
    # Tiny lower-right paper lift is anchored in the nine-slice corner.
    poly(im,[(343,238),(370,211),(370,238)],'#E1BE4E',NAVY,2)
    out(im,'onboarding','sticky_note_9slice','Plain sticky-note coaching panel with a folded corner and no stripe.',[32,32,40,40])
    im=cv(512,256)
    pts=[(31,78),(73,57),(117,47),(161,49),(201,67),(223,96),(220,127),(198,150),(165,153),(145,137),(150,111),(176,98),(214,102),(252,132),(289,162),(336,175),(385,167),(447,132)]
    smooth=[]
    extended=[pts[0]]+pts+[pts[-1]]
    for idx in range(1,len(extended)-2):
        p0,p1,p2,p3=extended[idx-1:idx+3]
        for j in range(12):
            t=j/12
            smooth.append(tuple(.5*((2*p1[k])+(-p0[k]+p2[k])*t+(2*p0[k]-5*p1[k]+4*p2[k]-p3[k])*t*t+(-p0[k]+3*p1[k]-3*p2[k]+p3[k])*t*t*t) for k in [0,1]))
    smooth.append(pts[-1]);ln(im,smooth,NAVY,11)
    ln(im,[(416,123),(454,128),(449,170)],NAVY,11)
    out(im,'onboarding','coach_arrow','Hand-drawn looping ink coaching arrow, points right.',limit=80000)

def tape_icon(im,x,y,scale=1):
    def bb(a):return (x+a[0]*scale,y+a[1]*scale,x+a[2]*scale,y+a[3]*scale)
    rr(im,bb((0,0,152,94)),12*scale,MANILA,NAVY,5)
    rr(im,bb((18,15,134,62)),7*scale,PAPER,NAVY,3)
    for xx in [46,107]:el(im,bb((xx-15,25,xx+15,55)),NAVY)
    poly(im,[(x+38*scale,y+94*scale),(x+48*scale,y+72*scale),(x+104*scale,y+72*scale),(x+115*scale,y+94*scale)],PAPER,NAVY,3)

def phone_symbol(im,y=76):
    ln(im,[(53,y+43),(64,y+22),(93,y+10),(125,y+7),(161,y+12),(190,y+31),(200,y+49)],GREEN,28)
    rr(im,(36,y+31,81,y+84),13,GREEN,NAVY,6)
    rr(im,(174,y+31,219,y+84),13,GREEN,NAVY,6)

def deskphone():
    im=cv(480,192)
    rr(im,(7,7,473,185),23,PAPER,NAVY,5)
    rr(im,(18,18,462,174),15,'#B8BCA3',NAVY,3)
    rr(im,(29,30,451,162),9,'#96AD82',NAVY,4)
    ln(im,[(37,38),(443,38)],'#65775E',3)
    out(im,'deskphone','lcd_frame','Greenish LCD with a warm plastic bezel; draw dark LCD text inside inset 40.',[44,44,44,44])
    for name,pressed in [('key',False),('key_pressed',True)]:
        im=cv(128,96)
        rr(im,(8,8,120,88),18,NAVY,NAVY,3)
        rr(im,(10,10 if not pressed else 16,118,77 if not pressed else 83),15, PAPER if not pressed else MANILA,NAVY,3)
        out(im,'deskphone',name,'Blank tactile keypad key; overlay all numerals at runtime.',[28,28,28,28])
    im=Image.new('RGBA',(800,600));base=source('phone_base');basebbox=base.getchannel('A').getbbox()
    baseinfo=paste_fit(base,im,(40,140,720,430));out(im,'deskphone','phone_base','Front-view phone base only. Shared 800x600 canvas with handset; composite at identical origin.',sup=False,limit=550000)
    im=Image.new('RGBA',(800,600));hinfo=paste_fit(source('handset'),im,(181,52,553,150));out(im,'deskphone','handset','Separate resting handset. Shared 800x600 origin with phone_base; animate around pixel pivot [458,136].',sup=False,limit=300000)
    im=cv(800,600)
    for k in range(3):
        arc(im,(143-k*25,46-k*10,248+k*5,225+k*10),125,235,GREEN,7)
        arc(im,(651-k*5,46-k*10,756+k*15,225+k*10),305,415,GREEN,7)
    out(im,'deskphone','ring_waves','Separate green ringing arcs on the shared 800x600 phone canvas; toggle alpha.',limit=80000)
    im=cv(256,144);tape_icon(im,16,25,1.1);el(im,(190,43,237,90),RED,NAVY,4)
    out(im,'deskphone','rec_badge','Red-dot cassette recording badge, no REC text.',limit=80000)
    im=cv(256,256)
    receiver=source('handset');receiver=receiver.crop(receiver.getchannel('A').getbbox());receiver.thumbnail((220*S,110*S),Image.Resampling.LANCZOS);im.alpha_composite(receiver,((256*S-receiver.width)//2,46*S))
    for xx in [84,144]:rr(im,(xx,174,xx+25,212),5,NAVY,None)
    out(im,'deskphone','busy','Desk-phone busy symbol: receiver over paired pause bars.',limit=80000)
    im=cv(256,256);im.alpha_composite(receiver,((256*S-receiver.width)//2,38*S))
    ln(im,[(55,145),(56,181),(92,181),(104,169)],NAVY,7)
    ln(im,[(151,169),(164,181),(199,181),(201,146)],NAVY,7)
    ln(im,[(116,158),(103,185)],RED,6);ln(im,[(143,158),(130,185)],RED,6)
    out(im,'deskphone','line_dead','Phone receiver above a clearly severed cord.',limit=80000)
    return {'canvas':[800,600],'base':baseinfo,'handset':hinfo,'handsetPivot':[458,136],'origin':'top-left; stack both at [0,0]','sourceBaseBounds':basebbox}

def meeting():
    im=Image.new('RGBA',(1024,512));src=source('tape_player');bbox=src.getchannel('A').getbbox();placement=paste_fit(src,im,(24,32,976,448))
    out(im,'meeting_v3','tape_player','Text-free cassette deck body with two transparent reel apertures; separate reels overlay at documented centers.',sup=False,limit=700000)
    im=cv(256,256);el(im,(20,20,236,236),MANILA,NAVY,10);el(im,(47,47,209,209),PAPER,NAVY,5)
    for theta in [0,120,240]:
        a=math.radians(theta);pts=[]
        for da,r in [(-25,32),(-19,70),(19,70),(25,32)]:
            aa=a+math.radians(da);pts.append((128+math.cos(aa)*r,128+math.sin(aa)*r))
        poly(im,pts,NAVY)
    el(im,(104,104,152,152),MANILA,NAVY,5);el(im,(118,118,138,138),(0,0,0,0),NAVY,3)
    out(im,'meeting_v3','tape_reel','One rotationally centered cassette reel. Pivot [128,128]; use twice over deck.',limit=80000)
    im=cv(384,96);rr(im,(7,9,377,87),14,PAPER,NAVY,4);rr(im,(17,19,367,77),8,'#BDD1B5',NAVY,2)
    out(im,'meeting_v3','waveform_bar','Quiet waveform tray. Draw animated waveform inside the green inset.',[30,30,30,30],limit=80000)
    # A real alpha falloff is intentional light, not a glossy UI gradient.
    im=Image.new('RGBA',(1024,1024),(255,239,188,0));a=Image.new('L',im.size);d=ImageDraw.Draw(a)
    d.polygon([(474,40),(550,40),(937,935),(87,935)],fill=68)
    d.ellipse((87,806,937,1020),fill=82);a=a.filter(ImageFilter.GaussianBlur(38))
    # Fade the very top and canvas border so no hard overlay rectangle is visible.
    px=a.load()
    for y in range(1024):
        for x in range(1024):
            edge=min(x,y,1023-x,1023-y);px[x,y]=int(px[x,y]*min(1,edge/44))
    im.putalpha(a);out(im,'meeting_v3','spotlight','Transparent warm soft light cone for final defense; alpha maximum below 0.33.',sup=False,limit=500000)
    im=Image.open(ROOT/'meeting/termination_letter.png').convert('RGBA')
    # Preserve original letter art; recolor only exact green letterhead ink into warning red.
    px=im.load()
    for y in range(im.height):
        for x in range(im.width):
            r,g,bl,aa=px[x,y]
            if g>r*1.2 and g>bl*1.15 and g<160: px[x,y]=(170,86,67,aa)
    out(im,'meeting_v3','warning_letter','Text-free warning stationery; existing termination letter composition with muted red letterhead tint.',sup=False,limit=900000)
    im=cv(512,160);poly(im,[(16,141),(48,18),(464,18),(496,141)],MANILA,NAVY,4);rr(im,(48,19,464,137),7,PAPER,NAVY,4)
    out(im,'meeting_v3','nameplate','Blank folded table name card. Preserve the folded side legs with nine-slice borders.',[60,32,60,32])
    im=cv(256,256);el(im,(21,26,235,240),'#BA975D',NAVY,7);el(im,(21,17,235,231),MANILA,NAVY,7);el(im,(37,33,219,215),PAPER,NAVY,3)
    rr(im,(75,66,174,169),9,MANILA,NAVY,4);ln(im,[(95,118),(117,140),(156,94)],GREEN,10)
    out(im,'meeting_v3','vote_token','Ballot chip with a paper slip and hand-inked check.',limit=80000)
    im=cv(384,144);rr(im,(8,19,376,136),18,MANILA,NAVY,4);rr(im,(27,33,357,118),10,PAPER,'#B69B62',2)
    out(im,'meeting_v3','dossier_tab','Blank manila dossier tab with a paper write-in inset.',[42,42,42,42])
    reelcenters=[[round(placement['position'][0]+(x-bbox[0])*placement['size'][0]/(bbox[2]-bbox[0])),round(placement['position'][1]+(y-bbox[1])*placement['size'][1]/(bbox[3]-bbox[1]))] for x,y in [[432,365],[860,365]]]
    return {'canvas':[1024,512],'sourceBounds':bbox,'placement':placement,'sourceReelCenters':[[432,365],[860,365]],'reelCenters':reelcenters,'reelPivot':[128,128],'reelRenderSize':[100,100]}

if __name__=='__main__':
    onboarding();alignment={'phone':deskphone(),'tape':meeting()}
    for cat,entries in ENTRIES.items():
        safe(ROOT/cat/'_metadata.json').write_text(json.dumps(entries,ensure_ascii=False,indent=2),encoding='utf-8')
        sheet((ROOT/cat).glob('*.png'),ROOT/'_sheets'/('v3_'+cat+'.png'),3,(400,280))
    safe(ROOT/'_tools/v3_props_alignment.json').write_text(json.dumps(alignment,indent=2),encoding='utf-8')
    safe(ROOT/'_tools/v3_props_audit.json').write_text(json.dumps(REVIEW,indent=2),encoding='utf-8')
    safe(ROOT/'_tools/v3_props_sources.json').write_text(json.dumps(SOURCES,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({'counts':{c:len(e) for c,e in ENTRIES.items()},'alignment':alignment},indent=2))
