"""Build consistent category reviews plus exact 48px icon comparison."""
import sys
sys.dont_write_bytecode=True
from kit import *
LAYOUT={
 'style':(1,(1000,700)), 'logo':(2,(720,420)), 'backgrounds':(2,(640,405)),
 'buttons':(5,(280,230)), 'icons':(7,(200,205)), 'cards':(4,(270,420)),
 'portraits':(5,(240,260)), 'stamps':(2,(600,235)), 'phone':(4,(280,420)),
 'meeting':(3,(480,420)), 'decals':(4,(320,420)),
 'howto':(2,(660,430)), 'week':(2,(660,220)), 'titles':(5,(260,300)),
 'bubbles':(4,(260,300)), 'app':(1,(600,660)), 'store':(1,(1000,610))}

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
    out=Image.new('RGB',(1800,math.ceil(len(LAYOUT)/3)*360),(113,117,122));d=ImageDraw.Draw(out)
    for i,c in enumerate(LAYOUT):
        p=ROOT/'_sheets'/(c+'.png')
        if not p.exists():continue
        im=Image.open(p).convert('RGB');im.thumbnail((560,300),Image.Resampling.LANCZOS)
        x=(i%3)*600;y=(i//3)*360
        out.paste(im,(x+(600-im.width)//2,y+8+(300-im.height)//2))
        d.text((x+300,y+325),f'{c} / {len(list((ROOT/c).glob("*.png")))} PNG',font=font(19,True),fill='white',anchor='mt')
    out.save(safe(ROOT/'_sheets/index.png'),optimize=True)

def second_index():
    cats=['howto','week','titles','bubbles','app','store']
    out=Image.new('RGB',(2000,1800),(92,98,105));d=ImageDraw.Draw(out)
    for i,c in enumerate(cats):
        p=ROOT/'_sheets'/(c+'.png')
        if not p.exists():continue
        im=Image.open(p).convert('RGB');im.thumbnail((950,535),Image.Resampling.LANCZOS)
        x=(i%2)*1000;y=(i//2)*600
        out.paste(im,(x+(1000-im.width)//2,y+10+(535-im.height)//2))
        n=len(list((ROOT/c).glob('*.png')))
        label=f'{c} / {n} PNG'+(' + 1 ICO' if c=='app' else '')
        d.text((x+500,y+560),label,font=font(24,True),fill='white',anchor='mt')
    out.save(safe(ROOT/'_sheets/second_run.png'),optimize=True)

if __name__=='__main__':
    categories=sys.argv[1:] or list(LAYOUT)
    for c in categories:
        cols,cell=LAYOUT[c]
        sheet((ROOT/c).glob('*.png'),ROOT/'_sheets'/(c+'.png'),cols,cell)
    if 'icons' in categories:exact48()
    index()
    second_index()
    print('Contact sheets updated: '+', '.join(categories))
