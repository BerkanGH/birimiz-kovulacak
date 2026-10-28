"""PNG finishing and review helpers. All outputs must remain beneath Art/UI."""
import sys
sys.dont_write_bytecode = True
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
PIL_SITE = Path('C:/Users/agdem/AppData/Local/Packages/PythonSoftwareFoundation.Python.3.13_qbz5n2kfra8p0/LocalCache/local-packages/Python313/site-packages')
if PIL_SITE.exists(): sys.path.insert(0, str(PIL_SITE))
from PIL import Image, ImageDraw, ImageFont, ImageOps, ImageFilter
import argparse, json, math, textwrap
P = dict(paper='#F4EFE3', manila='#E8C98A', green='#2F6B4F', red='#D2382F', navy='#1F2A44', yellow='#FFE066', teal='#3BB9B0')

def safe(path):
    path = Path(path).resolve()
    if path != ROOT and ROOT not in path.parents: raise ValueError('Output outside Art/UI: '+str(path))
    path.parent.mkdir(parents=True,exist_ok=True)
    return path

def font(size, bold=False):
    return ImageFont.truetype('C:/Windows/Fonts/arialbd.ttf' if bold else 'C:/Windows/Fonts/arial.ttf', size)

def save(im, path, limit=None):
    path = safe(path)
    im = im.convert('RGBA')
    im.save(path, optimize=True)
    if limit and path.stat().st_size > limit:
        for colors in [256,192,128,96,64]:
            q = im.quantize(colors=colors, method=Image.Quantize.FASTOCTREE, dither=Image.Dither.NONE)
            q.save(path,optimize=True)
            if path.stat().st_size <= limit: break
    return path

def finish(src, dst, w, h, mode='contain', pad=0, trim=False, limit=None):
    im=Image.open(src).convert('RGBA')
    if trim:
        box=im.getchannel('A').getbbox()
        if box: im=im.crop(box)
    if mode=='cover': out=ImageOps.fit(im,(w,h),Image.Resampling.LANCZOS)
    elif mode=='stretch': out=im.resize((w,h),Image.Resampling.LANCZOS)
    else:
        im.thumbnail((w-pad*2,h-pad*2),Image.Resampling.LANCZOS)
        out=Image.new('RGBA',(w,h)); out.alpha_composite(im,((w-im.width)//2,(h-im.height)//2))
    save(out,dst,limit)
    return audit(dst)

def audit(path):
    im=Image.open(path).convert('RGBA'); a=im.getchannel('A'); w,h=im.size
    return dict(path=str(Path(path).relative_to(ROOT)) if Path(path).is_absolute() else str(path),w=w,h=h,bytes=Path(path).stat().st_size,alphaExtrema=a.getextrema(),cornerAlpha=[a.getpixel(p) for p in [(0,0),(w-1,0),(0,h-1),(w-1,h-1)]],alphaBounds=a.getbbox())

def sheet(paths, output, cols=6, cell=(210,220)):
    paths=sorted(map(Path,paths)); cw,ch=cell; rows=math.ceil(len(paths)/cols)
    out=Image.new('RGB',(cols*cw,rows*ch),(113,117,122)); d=ImageDraw.Draw(out)
    for i,path in enumerate(paths):
        im=Image.open(path).convert('RGBA'); im.thumbnail((cw-24,ch-55),Image.Resampling.LANCZOS)
        x=(i%cols)*cw; y=(i//cols)*ch
        out.paste(im,(x+(cw-im.width)//2,y+8+(ch-55-im.height)//2),im)
        lines=textwrap.wrap(path.name,25,break_long_words=True,break_on_hyphens=False)
        for j,line in enumerate(lines[:3]):
            d.text((x+cw//2,y+ch-43+j*15),line,font=font(12),fill='white',anchor='mt')
    safe(output);out.save(output,optimize=True)
    return str(output)

def metadata(path, category, description='', nine=None, screen=None, text=False):
    path=Path(path); im=Image.open(path)
    return dict(id=path.stem,path='Art/UI/'+str(path.relative_to(ROOT)).replace('\\','/'),w=im.width,h=im.height,category=category,nineSlice=nine,screenRect=screen,hasText=text,description=description)

if __name__=='__main__':
    a=argparse.ArgumentParser(); sub=a.add_subparsers(dest='command',required=True)
    f=sub.add_parser('finish');f.add_argument('src');f.add_argument('dst');f.add_argument('w',type=int);f.add_argument('h',type=int);f.add_argument('--mode',choices=['contain','cover','stretch'],default='contain');f.add_argument('--pad',type=int,default=0);f.add_argument('--trim',action='store_true');f.add_argument('--limit',type=int)
    s=sub.add_parser('sheet');s.add_argument('category');s.add_argument('--cols',type=int,default=6)
    s=sub.add_parser('audit');s.add_argument('category')
    v=a.parse_args()
    if v.command=='finish': print(json.dumps(finish(v.src,v.dst,v.w,v.h,v.mode,v.pad,v.trim,v.limit)))
    if v.command=='sheet': print(sheet((ROOT/v.category).glob('*.png'),ROOT/'_sheets'/f'{v.category}.png',v.cols))
    if v.command=='audit': print(json.dumps([audit(p) for p in (ROOT/v.category).glob('*.png')],indent=2))
