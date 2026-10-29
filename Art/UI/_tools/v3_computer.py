"""V3 computer UI: native stationery controls and deterministic screen effects.

All outputs stay inside Art/UI/computer, _tools and _sheets. Existing PNGs are
read-only inputs; the KOVA bucket/sun is extracted from its original decal.
"""
import sys
sys.dont_write_bytecode=True
from kit import *
from PIL import ImageChops
from collections import deque
import hashlib, random, math

S=3
ENTRIES=[]
DETAILS=[]


def sha(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def coords(values):return tuple(round(v*S) for v in values)
def canvas(w,h):return Image.new('RGBA',(w*S,h*S))
def rr(im,b,r,fill,outline=P['navy'],width=3):
    ImageDraw.Draw(im).rounded_rectangle(coords(b),round(r*S),fill,outline,round(width*S))
def line(im,points,color,width):
    d=ImageDraw.Draw(im);pts=[coords(p) for p in points]
    d.line(pts,fill=color,width=round(width*S),joint='curve')
    r=width*S/2
    for x,y in [pts[0],pts[-1]]:d.ellipse((round(x-r),round(y-r),round(x+r),round(y+r)),fill=color)
def curve(points,steps=30):
    p0,p1,p2,p3=points
    return [((1-t)**3*p0[0]+3*(1-t)**2*t*p1[0]+3*(1-t)*t*t*p2[0]+t**3*p3[0],
             (1-t)**3*p0[1]+3*(1-t)**2*t*p1[1]+3*(1-t)*t*t*p2[1]+t**3*p3[1]) for t in [i/steps for i in range(steps+1)]]
def path_polygon(im,segments,fill,outline=None,width=3):
    pts=[]
    for c in segments:pts+=curve(c)
    d=ImageDraw.Draw(im);scaled=[coords(p) for p in pts]
    d.polygon(scaled,fill=fill)
    if outline:d.line(scaled+[scaled[0]],fill=outline,width=width*S,joint='curve')


def grain(im,rect,seed=85):
    rng=random.Random(seed); d=ImageDraw.Draw(im)
    x,y,w,h=rect
    for _ in range(int(w*h/135)):
        px=(x+rng.random()*w)*S;py=(y+rng.random()*h)*S
        d.ellipse((round(px),round(py),round(px+1),round(py+1)),fill='#DED7C7')


def output(im,name,desc,nine=None,size=None,limit=None):
    if size:im=im.resize(size,Image.Resampling.LANCZOS)
    path=ROOT/'computer'/f'{name}.png'
    save(im,path,limit)
    ENTRIES.append(metadata(path,'computer',desc,nine=nine))
    return im


def bucket():
    # Same pixel art as KOVA's reception plaque. Remove only paper-connected
    # exterior and handle opening; cream sunrise within the bucket is retained.
    source=ROOT/'decals/logo_company.png'
    crop=(43,49,282,314)
    im=Image.open(source).convert('RGBA').crop(crop)
    pix=im.load();w,h=im.size
    candidates=set()
    for y in range(h):
        for x in range(w):
            r,g,b,a=pix[x,y]
            if r>185 and g>175 and b>155:candidates.add((x,y))
    seeds=[(x,0) for x in range(w)]+[(x,h-1) for x in range(w)]+[(0,y) for y in range(h)]+[(w-1,y) for y in range(h)]+[(120,45)]
    remove=set();q=deque(p for p in seeds if p in candidates)
    while q:
        p=q.popleft()
        if p in remove:continue
        remove.add(p);x,y=p
        for nxt in [(x-1,y),(x+1,y),(x,y-1),(x,y+1)]:
            if nxt in candidates and nxt not in remove:q.append(nxt)
    a=Image.new('L',im.size,255);ap=a.load()
    for x,y in remove:ap[x,y]=0
    # Unmatte antialiased outline pixels against the original cream backdrop.
    # Keeping their cream RGB would make a visible white fringe on dark UI.
    inside=a.filter(ImageFilter.MinFilter(5));ip=inside.load()
    for y in range(h):
        for x in range(w):
            if ap[x,y] and not ip[x,y]:
                r,g,b,_=pix[x,y]
                if r>75 and g>75:
                    coverage=max(0,min(255,round(255*(244-r)/(244-31))))
                    ap[x,y]=coverage;pix[x,y]=(31,42,68,255)
    im.putalpha(a)
    im=im.crop(im.getchannel('A').getbbox())
    mark=Image.new('RGBA',(512,512));im=ImageOps.contain(im,(404,420),Image.Resampling.LANCZOS)
    mark.alpha_composite(im,((512-im.width)//2,(512-im.height)//2))
    output(mark,'boot_logo','Text-free KOVA bucket and sunrise company mark, extracted from the original reception plaque. Centered transparent 512px canvas.')
    DETAILS.append({'id':'boot_logo','source':'Art/UI/decals/logo_company.png','sourceSha256':sha(source),'sourceCrop':crop,'method':'Connected-paper alpha matte; original bucket, sunrise, navy contours and colors retained.'})
    return mark


def wallpaper(mark):
    out=Image.new('RGBA',(1920,1080),P['paper'])
    small=mark.resize((156,156),Image.Resampling.LANCZOS)
    # Only a subdued solid-ink silhouette repeats; no text or invented branding.
    alpha=small.getchannel('A').point(lambda a:round(a*.065))
    stamp=Image.new('RGBA',small.size,P['green']);stamp.putalpha(alpha)
    for row,y in enumerate(range(-40,1080,216)):
        for x in range(-60+(row%2)*120,1920,240):out.alpha_composite(stamp,(x,y))
    rng=random.Random(296);d=ImageDraw.Draw(out)
    for _ in range(19000):
        x=rng.randrange(1920);y=rng.randrange(1080)
        r,g,b,a=out.getpixel((x,y));n=rng.choice([-2,-1,1,2])
        d.point((x,y),fill=(r+n,g+n,b+n,255))
    output(out,'os_wallpaper','Opaque warm-paper company desktop wallpaper with a quiet repeating KOVA bucket silhouette. No readable text; subtle deterministic paper grain.',limit=1500000)


def controls():
    w,h=640,480;im=canvas(w,h)
    rr(im,(15,18,627,470),20,(31,42,68,42),None)
    rr(im,(10,10,622,462),18,P['paper'],width=4)
    # One manila title-bar field, its lower edge is a semantic title/body boundary.
    rr(im,(12,12,620,77),16,P['manila'],None)
    ImageDraw.Draw(im).rectangle(coords((12,44,620,77)),fill=P['manila'])
    line(im,[(12,78),(620,78)],P['navy'],3)
    rr(im,(10,10,622,462),18,None,width=4)
    rr(im,(20,90,612,452),10,None,'#D5CDBA',1)
    grain(im,(30,102,570,335))
    output(im,'window_frame','Paper OS window with manila title bar, navy perimeter and quiet body. Top border protects title bar and inner rule corners. Body content inset [40,108,560,324]. No baked controls or text.',nine=[40,104,40,40],size=(w,h))

    w,h=640,80;im=canvas(w,h)
    rr(im,(9,12,632,73),15,(31,42,68,45),None)
    rr(im,(8,8,632,68),15,P['manila'],width=3)
    rr(im,(16,15,624,60),10,P['paper'],'#D5CDBA',1)
    grain(im,(25,22,590,30),87)
    output(im,'taskbar','Stationery taskbar strip with quiet paper center and manila rim; runtime app buttons and status sit above it.',nine=[26,28,26,30],size=(w,h))

    w,h=384,192;im=canvas(w,h)
    rr(im,(12,16,375,184),14,(31,42,68,38),None)
    rr(im,(8,8,371,178),14,P['paper'],width=3)
    rr(im,(17,17,362,169),9,None,'#D5CDBA',1)
    grain(im,(25,25,330,132),89)
    output(im,'notification_card','Quiet cream paper notification with navy outline and inset stationery rule. No colored side stripe. Runtime icon and localized message supplied separately.',nine=[36,36,36,36],size=(w,h))

    w,h=192,192;im=canvas(w,h)
    rr(im,(11,15,184,184),19,(31,42,68,48),None)
    rr(im,(8,8,181,177),18,P['manila'],width=3)
    rr(im,(17,17,172,166),12,P['paper'],'#BFA36E',1)
    grain(im,(27,27,134,129),91)
    output(im,'app_tile','Blank desktop app tile in the original manila-and-paper control style. The icon and localized name are runtime overlays.',nine=[40,40,40,40],size=(w,h))


def cursor():
    im=canvas(64,64)
    segments=[[(21,43),(14,37),(9,32),(11,29)],[(11,29),(13,25),(17,28),(23,32)],
        [(23,32),(23,25),(23,16),(23,10)],[(23,10),(23,3),(32,3),(32,10)],
        [(32,10),(32,15),(32,22),(32,26)],[(32,26),(33,19),(40,19),(40,27)],
        [(40,27),(41,21),(48,23),(48,30)],[(48,30),(51,26),(57,29),(56,36)],
        [(56,36),(56,44),(52,48),(49,52)],[(49,52),(41,54),(33,54),(27,51)],
        [(27,51),(25,48),(23,45),(21,43)]]
    path_polygon(im,segments,P['paper'],P['navy'],2)
    for x,y in [(32,27),(40,28),(48,32)]:line(im,[(x,y),(x,y+8)],'#B5AB94',1)
    rr(im,(25,50,51,59),3,P['green'],width=2)
    output(im,'cursor_hand','Paper-colored office-bean pointing cursor with green cuff. Hotspot [27,7], source pixels; full hand remains inside the 64px canvas.',size=(64,64))
    DETAILS.append({'id':'cursor_hand','hotspot':[27,7]})


def offline():
    w,h=1920,1080;out=Image.new('RGBA',(w,h));d=ImageDraw.Draw(out);rng=random.Random(487)
    # Sparse irregular static, rather than decorative stripes. Transparent gaps
    # remain across the full canvas; the concentrated fault motif is central.
    for _ in range(22000):
        x=rng.randrange(8,w-8);y=rng.randrange(8,h-8)
        color=(31,42,68,rng.choice([16,22,28,38,48]))
        d.line((x,y,x+rng.choice([0,1,2,4,8]),y),fill=color,width=1)
    motif=canvas(w,h)
    cable=curve([(430,730),(400,920),(850,930),(770,700)],60)+curve([(770,700),(720,600),(600,650),(690,535)],45)
    line(motif,cable,P['navy'],32);line(motif,cable,'#46596A',20)
    rr(motif,(666,434,874,586),28,P['navy'],width=6)
    rr(motif,(677,444,862,574),22,'#46596A',None)
    # Warm paper insulation and two flat contacts identify an unplugged cord.
    for y in [465,536]:
        rr(motif,(854,y,934,y+22),7,P['manila'],width=5)
    rr(motif,(1088,375,1268,635),25,P['paper'],width=7)
    rr(motif,(1102,389,1254,621),16,P['manila'],'#BFA36E',2)
    ImageDraw.Draw(motif).ellipse(coords((1123,444,1235,574)),fill=P['paper'],outline=P['navy'],width=5*S)
    for x in [1151,1198]:rr(motif,(x,481,x+9,526),4,P['navy'],None)
    out.alpha_composite(motif.resize((w,h),Image.Resampling.LANCZOS))
    output(out,'offline_overlay','Transparent screen fault: irregular navy static with an unplugged cable and empty socket motif. No text, glow or decorative stripe. Full-screen 1920x1080 overlay.',limit=1500000)


def irregular(cx,cy,rx,ry,phase=0):
    points=[]
    for i in range(420):
        a=i*math.tau/420
        f=1+.072*math.sin(5*a+phase)+.047*math.sin(9*a+1.3+phase)+.027*math.sin(17*a+2)
        points.append((cx+rx*f*math.cos(a),cy+ry*f*math.sin(a)))
    return points


def coffee():
    out=Image.new('RGBA',(1920,1080));d=ImageDraw.Draw(out)
    # Hand-shaped pooled splash: irregular fingers, a narrow spilled stream and
    # broad lower pool, instead of repeating concentric contour rings.
    segments=[[(382,409),(300,370),(351,301),(461,346)],[(461,346),(579,395),(562,251),(700,288)],
      [(700,288),(829,317),(755,187),(883,217)],[(883,217),(1009,246),(1048,131),(1152,203)],
      [(1152,203),(1264,281),(1332,119),(1395,180)],[(1395,180),(1451,235),(1472,264),(1550,220)],
      [(1550,220),(1690,130),(1670,298),(1597,334)],[(1597,334),(1520,369),(1721,354),(1665,423)],
      [(1665,423),(1610,487),(1841,468),(1790,570)],[(1790,570),(1736,685),(1572,635),(1631,753)],
      [(1631,753),(1698,886),(1504,830),(1457,908)],[(1457,908),(1397,1008),(1255,925),(1160,964)],
      [(1160,964),(990,1037),(965,895),(839,949)],[(839,949),(705,1005),(673,840),(557,857)],
      [(557,857),(383,879),(504,711),(399,710)],[(399,710),(270,705),(456,631),(366,573)],
      [(366,573),(263,505),(496,462),(382,409)]]
    outer=[]
    for c in segments:outer+=curve(c,20)
    inner=irregular(1090,603,580,326,1.2)
    d.polygon(outer,fill=(114,68,36,195))
    d.line(outer+[outer[0]],fill=(98,58,31,214),width=4,joint='curve')
    d.polygon(inner,fill=(83,48,29,229))
    # Broad darker pooled coffee and a few transparent worn patches: flat
    # tones, no glossy lighting, gradients or artificial highlights.
    for cx,cy,rx,ry in [(228,615,65,31),(336,862,47,34),(1727,250,51,29),(1491,80,33,23),
                        (371,258,43,26),(167,417,26,18),(1750,927,32,24),(1840,680,42,23),
                        (640,97,28,19),(257,928,22,14)]:
        pts=irregular(cx,cy,rx,ry,.5)
        d.polygon(pts,fill=(102,60,32,182));d.line(pts+[pts[0]],fill=(99,58,29,200),width=3)
    # A recognisable cup-ring stain on a still-visible corner of the screen.
    ring=[(331+172*(1+.015*math.sin(11*a))*math.cos(a),206+108*(1+.02*math.sin(9*a))*math.sin(a)) for a in [i*math.tau/420 for i in range(420)]]
    d.line(ring[:337],fill=(123,78,41,132),width=13,joint='curve')
    ring2=[(331+162*math.cos(a),206+101*math.sin(a)) for a in [i*math.tau/420 for i in range(420)]]
    d.line(ring2[45:361],fill=(151,97,49,105),width=4,joint='curve')
    rng=random.Random(79)
    for _ in range(6000):
        x=rng.randrange(20,1900);y=rng.randrange(20,1060)
        r,g,b,a=out.getpixel((x,y))
        if a>150:d.point((x,y),fill=(r+3,g+2,b+1,max(0,a-8)))
    output(out,'coffee_overlay','Transparent coffee damage overlay: large dark pooled spill obscures the central screen, irregular splash droplets and dry cup-ring stain. Restrained flat brown tones; real alpha outside and within the spill.',limit=1500000)


def sliced(im,borders,size):
    l,t,r,b=borders;w,h=im.size;ow,oh=size
    assert ow>l+r and oh>t+b
    out=Image.new('RGBA',size)
    sx=[0,l,w-r,w];sy=[0,t,h-b,h];dx=[0,l,ow-r,ow];dy=[0,t,oh-b,oh]
    for yy in range(3):
        for xx in range(3):
            tile=im.crop((sx[xx],sy[yy],sx[xx+1],sy[yy+1]))
            tile=tile.resize((dx[xx+1]-dx[xx],dy[yy+1]-dy[yy]),Image.Resampling.NEAREST)
            out.alpha_composite(tile,(dx[xx],dy[yy]))
    return out


def sheets():
    sheet((ROOT/'computer').glob('*.png'),ROOT/'_sheets/v3_computer.png',3,(580,385))
    samples=[('window_frame',(840,360)),('taskbar',(840,80)),('notification_card',(650,130)),('app_tile',(320,150))]
    out=Image.new('RGB',(920,1000),'#71757A');d=ImageDraw.Draw(out);y=22
    for name,size in samples:
        e=next(e for e in ENTRIES if e['id']==name)
        im=Image.open(ROOT/'computer'/f'{name}.png').convert('RGBA')
        test=sliced(im,e['nineSlice'],size)
        out.paste(test,(32,y),test)
        d.text((35,y+size[1]+8),name+'  '+str(size)+'  borders '+str(e['nineSlice']),font=font(15),fill='white')
        y+=size[1]+52
    out.save(safe(ROOT/'_sheets/v3_computer_slice.png'),optimize=True)
    # Demonstrate the full-screen effects over a plausible desktop, preserving
    # true exported alpha and not baking the preview backdrop into either PNG.
    base=Image.open(ROOT/'computer/os_wallpaper.png').convert('RGBA')
    frame=Image.open(ROOT/'computer/window_frame.png').convert('RGBA')
    base.alpha_composite(sliced(frame,[40,104,40,40],(1220,760)),(350,100))
    preview=Image.new('RGB',(1200,410),'#71757A');d=ImageDraw.Draw(preview)
    for i,name in enumerate(['offline_overlay','coffee_overlay']):
        scene=base.copy();scene.alpha_composite(Image.open(ROOT/'computer'/f'{name}.png').convert('RGBA'))
        scene=scene.resize((576,324),Image.Resampling.LANCZOS)
        preview.paste(scene,(12+i*600,12));d.text((20+i*600,350),name,font=font(16),fill='white')
    preview.save(safe(ROOT/'_sheets/v3_computer_effects.png'),optimize=True)


def main():
    originals={str(p.relative_to(ROOT)):sha(p) for p in ROOT.rglob('*.png')
               if p.relative_to(ROOT).parts[0] not in ['computer','_tools','_sheets']}
    mark=bucket();wallpaper(mark);controls();cursor();offline();coffee();sheets()
    assert len(ENTRIES)==9
    safe(ROOT/'computer/_metadata.json').write_text(json.dumps(ENTRIES,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    audits=[]
    for e in ENTRIES:
        p=ROOT/'computer'/f"{e['id']}.png";a=audit(p);a['path']=p.relative_to(ROOT).as_posix();a['sha256']=sha(p)
        if e['id']=='os_wallpaper':assert a['alphaExtrema']==(255,255)
        else:
            assert a['alphaExtrema'][0]==0
            assert a['cornerAlpha']==[0,0,0,0]
        if e['w']==1920:assert a['bytes']<1500000
        audits.append(a)
    for rel,digest in originals.items():assert sha(ROOT/rel)==digest,rel
    ledger={'method':'Deterministic native UI extension of existing stationery controls; original KOVA bucket art extracted from company decal; procedural alpha screen effects.',
        'script':'Art/UI/_tools/v3_computer.py','details':DETAILS,'nineSlice':{e['id']:e['nineSlice'] for e in ENTRIES if e['nineSlice']},
        'assets':audits,'originalPngCountVerified':len(originals),'visualReview':{'status':'pending'}}
    safe(ROOT/'_tools/v3_computer_records.json').write_text(json.dumps(ledger,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'count':len(ENTRIES),'nineSlice':ledger['nineSlice'],'maxBytes':max(a['bytes'] for a in audits)}))


if __name__=='__main__':main()
