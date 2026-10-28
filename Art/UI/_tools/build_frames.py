"""Deterministic transparent device/meeting overlays in the locked stationery style."""
from pathlib import Path
import json
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
assert (ROOT / 'style/style_sheet.png').exists(), 'Style must be locked first'
S = 3
PAPER = '#F4EFE3'
MANILA = '#E8C98A'
GREEN = '#2F6B4F'
RED = '#D2382F'
NAVY = '#1F2A44'
TEAL = '#3BB9B0'

def canvas(w, h):
    return Image.new('RGBA', (w*S, h*S)), None

def box(b):
    return tuple(round(v*S) for v in b)

def rounded(im, b, radius, fill, outline=NAVY, width=4):
    ImageDraw.Draw(im).rounded_rectangle(box(b), round(radius*S), fill, outline, round(width*S))

def ellipse(im, b, fill, outline=None, width=1):
    ImageDraw.Draw(im).ellipse(box(b), fill, outline, round(width*S))

def line(im, pts, fill, width=3):
    d = ImageDraw.Draw(im)
    p = [(round(x*S), round(y*S)) for x,y in pts]
    d.line(p, fill, round(width*S), joint='curve')
    r = width*S/2
    for x,y in (p[0], p[-1]):
        d.ellipse((round(x-r),round(y-r),round(x+r),round(y+r)), fill)

def save(im, category, name, size, desc, screen=None):
    im = im.resize(size, Image.Resampling.LANCZOS)
    if screen:
        x,y,w,h = screen
        ImageDraw.Draw(im).rectangle((x,y,x+w-1,y+h-1),fill=(0,0,0,0))
    # Remove hidden RGB on transparent pixels for clean import and PNG compression.
    rgba = im.load()
    for y in range(im.height):
        for x in range(im.width):
            if rgba[x,y][3] == 0:
                rgba[x,y] = (0,0,0,0)
    a = im.getchannel('A')
    assert all(a.getpixel(p)==0 for p in [(0,0),(size[0]-1,0),(0,size[1]-1),(size[0]-1,size[1]-1)])
    if screen:
        x,y,w,h = screen
        assert a.crop((x,y,x+w,y+h)).getextrema() == (0,0), name
    path = ROOT/category/(name+'.png')
    path.parent.mkdir(exist_ok=True)
    im.save(path, optimize=True)
    return dict(id=name,path=f'Art/UI/{category}/{name}.png',w=size[0],h=size[1],category=category,nineSlice=None,screenRect=screen,hasText=False,description=desc)

phone, _ = canvas(600,1200)
# Side switches; broad dark silhouette with a warm paper edge to echo office stationery.
rounded(phone,(9,203,31,299),8,NAVY,None)
rounded(phone,(9,328,31,392),8,NAVY,None)
rounded(phone,(569,240,592,350),8,NAVY,None)
rounded(phone,(18,12,582,1188),76,NAVY,NAVY,6)
rounded(phone,(27,20,573,1178),68,'#415262',NAVY,5)
rounded(phone,(34,27,566,1168),62,PAPER,NAVY,5)
# Small highlight keeps the same upper-left lighting as generated illustrations.
line(phone,[(54,157),(54,97),(59,68),(75,49)],'#FFFFFF',4)
rounded(phone,(40,91,560,1109),8,NAVY,None)
# Full rectangular aperture is deliberately clear, including its four corners.
ImageDraw.Draw(phone).rectangle(box((46,106,554,1094)),fill=(0,0,0,0))
# Clearing a 2px safety margin makes the recorded screen rect exactly empty after filtering.
screen = [48,108,504,984]
rounded(phone,(231,52,369,64),6,NAVY,None)
line(phone,[(245,56),(354,56)],'#50666C',2)
ellipse(phone,(396,51,409,64),GREEN,NAVY,2)
ellipse(phone,(432,51,445,64),TEAL,NAVY,2)
ellipse(phone,(279,1122,321,1164),PAPER,NAVY,4)
rounded(phone,(293,1136,307,1150),3,MANILA,NAVY,2)
pm = save(phone,'phone','phone_frame',(600,1200),
    'Stationery-toned smartphone bezel. Place screen content in the exact transparent rectangle [48,108,504,984]; coordinates use top-left origin.',screen)
(ROOT/'phone/_metadata_frames.json').write_text(json.dumps([pm],indent=2),encoding='utf-8')

projector,_ = canvas(1600,900)
# Hanging loop and shade roller.
rounded(projector,(765,6,835,45),18,PAPER,NAVY,6)
rounded(projector,(781,15,819,39),9,(0,0,0,0),NAVY,4)
rounded(projector,(49,46,1551,838),16,PAPER,NAVY,7)
rounded(projector,(57,56,1543,826),10,MANILA,None)
rounded(projector,(68,75,1532,811),6,PAPER,NAVY,5)
ImageDraw.Draw(projector).rectangle(box((84,98,1516,792)),fill=(0,0,0,0))
projection = [86,100,1428,690]
rounded(projector,(35,30,1565,83),18,PAPER,NAVY,7)
line(projector,[(64,42),(1535,42)],'#FFFFFF',4)
line(projector,[(63,72),(1537,72)],'#C8C4B6',3)
rounded(projector,(35,806,1565,854),15,PAPER,NAVY,7)
line(projector,[(66,817),(1535,817)],'#FFFFFF',3)
line(projector,[(68,844),(1533,844)],'#C5C1B1',3)
ellipse(projector,(54,46,68,60),MANILA,NAVY,3)
ellipse(projector,(1532,46,1546,60),MANILA,NAVY,3)
line(projector,[(800,855),(800,875)],NAVY,5)
rounded(projector,(772,869,828,891),9,GREEN,NAVY,5)
pr = save(projector,'meeting','projector_frame',(1600,900),
    'Roll-down projector casing with transparent slide aperture [86,100,1428,690], top-left coordinates. Fixed aspect; do not nine-slice.',projection)

cctv,_ = canvas(1280,720)
d = ImageDraw.Draw(cctv)
for y in range(6,715,4):
    d.line(box((5,y,1275,y)),fill=(31,42,68,16),width=S)
# Restrained scanline tint leaves footage legible. Brackets are paper for contrast in dark scenes.
for pts in [ [(38,105),(38,38),(112,38)], [(1168,38),(1242,38),(1242,105)],
             [(38,615),(38,682),(112,682)], [(1168,682),(1242,682),(1242,615)] ]:
    line(cctv,pts,(31,42,68,135),7)
    line(cctv,pts,(244,239,227,225),3)
ellipse(cctv,(79,60,101,82),RED,(244,239,227,170),2)
ellipse(cctv,(84,64,90,70),(255,255,255,100))
cc = save(cctv,'meeting','cctv_overlay',(1280,720),
    'Transparent CCTV overlay: soft navy scanlines, four paper corner brackets and red recording dot. No baked-in text or timecode.')
(ROOT/'meeting/_metadata_frames.json').write_text(json.dumps([pr,cc],indent=2),encoding='utf-8')

def sheet(items, filename, heading):
    w=900; cellh=420; cols=2; rows=(len(items)+cols-1)//cols
    out=Image.new('RGB',(w,70+rows*cellh),'#787B80'); d=ImageDraw.Draw(out)
    font=ImageFont.truetype('C:/Windows/Fonts/consola.ttf',16)
    head=ImageFont.truetype('C:/Windows/Fonts/segoeuib.ttf',24)
    d.text((22,18),heading,font=head,fill=PAPER)
    for i,e in enumerate(items):
        x=(i%cols)*450;y=70+(i//cols)*cellh
        im=Image.open(ROOT/e['category']/(e['id']+'.png')).convert('RGBA')
        im.thumbnail((410,350),Image.Resampling.LANCZOS)
        out.paste(im,(x+(450-im.width)//2,y+(350-im.height)//2),im)
        d.text((x+20,y+365),e['id'],font=font,fill=PAPER)
        d.text((x+20,y+388),f"{e['w']} x {e['h']}",font=font,fill=PAPER)
    out.save(ROOT/'_sheets'/filename,optimize=True)

sheet([pm],'phone_frames.png','PHONE / FRAME REVIEW')
sheet([pr,cc],'meeting_frames.png','MEETING / FRAME REVIEW')
print('Rendered 3 overlays; corners and full recorded screen rectangles are transparent.')
