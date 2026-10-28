"""Build consistent category reviews plus exact 48px icon comparison."""
import sys
sys.dont_write_bytecode=True
from kit import *
LAYOUT={
 'style':(1,(1000,700)), 'logo':(2,(720,420)), 'backgrounds':(2,(640,405)),
 'buttons':(5,(280,230)), 'icons':(7,(200,205)), 'cards':(4,(270,420)),
 'portraits':(5,(240,260)), 'stamps':(2,(600,235)), 'phone':(4,(280,420)),
 'meeting':(3,(480,420)), 'decals':(4,(320,420))}

def exact48():
    paths=sorted((ROOT/'icons').glob('*.png'));cols=7;cw,ch=200,115
    out=Image.new('RGB',(cols*cw,math.ceil(len(paths)/cols)*ch),(113,117,122));d=ImageDraw.Draw(out)
    for i,p in enumerate(paths):
        x=(i%cols)*cw;y=(i//cols)*ch
        im=Image.open(p).convert('RGBA').resize((48,48),Image.Resampling.LANCZOS)
        d.rounded_rectangle((x+108,y+8,x+170,y+70),radius=6,fill=P['paper'])
        out.paste(im,(x+32,y+15),im);out.paste(im,(x+115,y+15),im)
        d.text((x+100,y+83),p.stem,font=font(12),fill='white',anchor='mt')
    safe(ROOT/'_sheets/icons_48.png');out.save(ROOT/'_sheets/icons_48.png',optimize=True)

def index():
    out=Image.new('RGB',(1800,4*360),(113,117,122));d=ImageDraw.Draw(out)
    for i,c in enumerate(LAYOUT):
        p=ROOT/'_sheets'/(c+'.png')
        if not p.exists():continue
        im=Image.open(p).convert('RGB');im.thumbnail((560,300),Image.Resampling.LANCZOS)
        x=(i%3)*600;y=(i//3)*360
        out.paste(im,(x+(600-im.width)//2,y+8+(300-im.height)//2))
        d.text((x+300,y+325),f'{c} / {len(list((ROOT/c).glob("*.png")))} PNG',font=font(19,True),fill='white',anchor='mt')
    out.save(safe(ROOT/'_sheets/index.png'),optimize=True)

if __name__=='__main__':
    categories=sys.argv[1:] or list(LAYOUT)
    for c in categories:
        cols,cell=LAYOUT[c]
        sheet((ROOT/c).glob('*.png'),ROOT/'_sheets'/(c+'.png'),cols,cell)
    if 'icons' in categories:exact48()
    index()
    print('Contact sheets updated: '+', '.join(categories))
