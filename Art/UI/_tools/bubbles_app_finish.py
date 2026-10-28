"""Finish the second-run bubbles and application icon; all writes under Art/UI."""
import sys
sys.dont_write_bytecode = True
from kit import ROOT, Image, ImageDraw, ImageFont, ImageOps, P, finish, save, sheet, metadata, audit, safe, font
import json

SOURCES = ROOT / '_sources' / 'bubbles_app'
DESCRIPTIONS = {
    'bubble_ringing': 'Incoming-call speech bubble: office-green handset and red ringing arcs; text-free.',
    'bubble_on_call': 'Active private call speech bubble: office-green handset and teal conversation balloon; text-free.',
    'bubble_exclaim': 'Manager alert speech bubble containing one red ! symbol.',
    'bubble_question': 'Question speech bubble containing one teal ? symbol.',
    'bubble_angry': 'Anger speech bubble containing one red four-part cartoon vein symbol; text-free.',
    'bubble_heart': 'Manager favour speech bubble containing one red heart; text-free.',
    'bubble_zzz': 'Sleep speech bubble containing three navy uppercase Z glyphs (ZZZ), growing diagonally.',
    'bubble_sweat': 'Nervous sweat speech bubble containing one teal droplet; text-free.',
}
TEXT_IDS = {'bubble_exclaim', 'bubble_question', 'bubble_zzz'}

def json_out(path, value):
    safe(path).write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')

records=[]
for name,desc in DESCRIPTIONS.items():
    path=ROOT/'bubbles'/f'{name}.png'
    raw=Image.open(SOURCES/f'{name}_raw.png').convert('RGBA')
    alpha=raw.getchannel('A')
    box=alpha.point(lambda p:255 if p>=128 else 0).getbbox()
    box=(max(0,box[0]-4),max(0,box[1]-4),min(raw.width,box[2]+4),min(raw.height,box[3]+4))
    raw.putalpha(alpha.point(lambda p:0 if p<8 else p))
    raw=raw.crop(box)
    raw.thumbnail((224,224),Image.Resampling.LANCZOS)
    final=Image.new('RGBA',(256,256));final.alpha_composite(raw,((256-raw.width)//2,(256-raw.height)//2))
    save(final,path,80000)
    records.append(metadata(path,'bubbles',desc+' Cream stationery balloon with transparent exterior; fixed aspect, no nine-slice.',text=name in TEXT_IDS))
json_out(ROOT/'bubbles'/'_metadata.json',records)

app_png=ROOT/'app'/'app_icon_1024.png'
finish(SOURCES/'app_icon_raw.png',app_png,1024,1024,mode='stretch',limit=1500000)
app=Image.open(app_png).convert('RGBA')
app_ico=ROOT/'app'/'app_icon.ico'
ico_sizes=[(n,n) for n in (16,24,32,48,64,128,256)]
app.save(safe(app_ico),format='ICO',sizes=ico_sizes)
ico=Image.open(app_ico)
assert ico.ico.sizes()==set(ico_sizes), ico.ico.sizes()
records_app=[metadata(app_png,'app','Application icon: worried blue logo-mark bean carrying a box with mug and plant, on a rounded office-green square. Transparent corners, text-free.'),
    dict(id='app_icon',path='Art/UI/app/app_icon.ico',w=256,h=256,category='app',nineSlice=None,screenRect=None,hasText=False,description='Multi-resolution Windows application icon containing 16, 24, 32, 48, 64, 128 and 256 pixel RGBA entries. Largest entry recorded in w/h; text-free.')]
json_out(ROOT/'app'/'_metadata.json',records_app)

sheet((ROOT/'bubbles').glob('*.png'),ROOT/'_sheets'/'bubbles.png',cols=4,cell=(300,306))
out=Image.new('RGB',(880,220),'#71757A');d=ImageDraw.Draw(out)
for i,name in enumerate(DESCRIPTIONS):
    x=i*110+31
    im=Image.open(ROOT/'bubbles'/f'{name}.png').convert('RGBA').resize((48,48),Image.Resampling.LANCZOS)
    out.paste(im,(x,28),im)
    d.text((i*110+55,88),name.replace('bubble_',''),font=font(12),fill='white',anchor='mt')
    d.rectangle((i*110+23,122,i*110+87,186),fill=P['paper'])
    out.paste(im,(x,130),im)
d.text((440,205),'Actual 48px sprites: grey and paper backgrounds',font=font(12),fill='white',anchor='mm')
out.save(safe(ROOT/'_sheets'/'bubbles_48.png'),optimize=True)

out=Image.new('RGB',(1060,650),'#71757A');d=ImageDraw.Draw(out)
preview=app.resize((512,512),Image.Resampling.LANCZOS);out.paste(preview,(24,24),preview)
d.text((280,550),'app_icon_1024.png',font=font(20),fill='white',anchor='mt')
for i,n in enumerate((16,24,32,48,64,128,256)):
    im=ico.ico.getimage((n,n)).convert('RGBA')
    if n<128:
        x=576+i*82;y=70
    elif n==128:
        x=596;y=246
    else:
        x=764;y=230
    out.paste(im,(x,y),im)
    d.text((x+n/2,y+n+12),str(n)+'px',font=font(13),fill='white',anchor='mt')
d.text((790,560),'app_icon.ico / all seven native resolutions',font=font(16),fill='white',anchor='mt')
out.save(safe(ROOT/'_sheets'/'app.png'),optimize=True)

audits=[audit(ROOT/'bubbles'/f'{name}.png') for name in DESCRIPTIONS]+[audit(app_png)]
for a in audits:
    assert a['alphaExtrema']==(0,255),a
    assert a['cornerAlpha']==[0,0,0,0],a
ico_audits=[]
for size in ico_sizes:
    im=ico.ico.getimage(size).convert('RGBA'); a=im.getchannel('A')
    assert a.getextrema()==(0,255)
    ico_audits.append(dict(w=size[0],h=size[1],mode=im.mode,alphaExtrema=a.getextrema()))
json_out(ROOT/'_sources'/'bubbles_app_audit.json',dict(pngCount=9,icoCount=1,files=audits,icoEntries=ico_audits,visibleText={'bubble_exclaim':'!','bubble_question':'?','bubble_zzz':'ZZZ'},visualReview='Reviewed all individual raw generations and final category sheets, including bubbles at native 48px over grey and paper, and all seven ICO resolutions. Phone ring/call symbols distinguishable; full text checked. Tiny 16px app entry preserves bean/box silhouette but loses mug and plant detail.'))
print(json.dumps(dict(pngCount=9,icoCount=1,icoSizes=sorted(ico.ico.sizes()),files=audits),indent=2))
