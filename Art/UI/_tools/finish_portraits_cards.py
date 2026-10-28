"""Finalize generated portraits and assemble consistent sabotage cards inside Art/UI."""
import sys
sys.dont_write_bytecode=True
from kit import *
import json

def portraits():
    records=json.loads((ROOT/'_sources/portraits_cards_prompts.json').read_text(encoding='utf-8'))
    report=[]; meta=[]
    descriptions={
      'mgr_paragoz':'Hasan Bey: golden bean, moustache, navy suit, gold tie and watch; money-hungry grin.',
      'mgr_disiplinci':'Nermin Hanım: teal bean, sharp glasses, tight bun, green suit and clipboard; stern.',
      'mgr_kayirmaci':'Selin Hanım: coral bean, flashy scarf, sunglasses on head and big smile.',
      'mgr_paranoyak':'Kemal Bey: purple bean, dark glasses, raised trench-coat collar and suspicious squint.',
      'player_0':'Mavi, player slot 0: tall blue bean with green tie.',
      'player_1':'Sarı, player slot 1: round yellow bean with glasses.',
      'player_2':'Pembe, player slot 2: pink bean with hair bun.',
      'player_3':'Yeşil, player slot 3: short green bean with headset.',
      'player_4':'Turuncu, player slot 4: orange bean with navy beanie.',
      'player_5':'Mor, player slot 5: purple bean with curly hair and teal scarf.',
      'player_unknown':'Neutral anonymous slate bean placeholder for an unoccupied player slot.'}
    for rec in records:
        if rec['category']!='portraits': continue
        im=Image.open(rec['source']).convert('RGBA')
        # Remove near-invisible alpha debris before sizing, preserve true generated transparency.
        a=im.getchannel('A').point(lambda v: 0 if v<24 else v)
        im.putalpha(a)
        box=a.point(lambda v:255 if v>128 else 0).getbbox()
        im=im.crop(box); im.thumbnail((464,464),Image.Resampling.LANCZOS)
        # A thin navy silhouette edge seals cut-shoulder contours and keeps the kit outline coherent.
        a=im.getchannel('A'); clean=a.filter(ImageFilter.MinFilter(3)).filter(ImageFilter.MaxFilter(3))
        clean=clean.point(lambda v:0 if v<16 else v)
        im.putalpha(clean)
        plate=Image.new('RGBA',(512,512)); x=(512-im.width)//2; y=488-im.height
        mask=Image.new('L',(512,512)); mask.paste(clean,(x,y)); mask=mask.filter(ImageFilter.MaxFilter(5))
        edge=Image.new('RGBA',(512,512),P['navy']); edge.putalpha(mask)
        plate.alpha_composite(edge); plate.alpha_composite(im,(x,y))
        path=ROOT/'portraits'/f"{rec['id']}.png"; save(plate,path,220000)
        Image.open(path).convert('RGBA').save(path,optimize=True)
        desc=descriptions.get(rec['id'].replace('_angry',''),'')
        if rec['id'].endswith('_angry'): desc=desc.split(';')[0]+' Angry expression variant, identity and pose preserved.'
        meta.append(metadata(path,'portraits',desc))
        report.append(audit(path))
    (ROOT/'portraits/_metadata.json').write_text(json.dumps(meta,ensure_ascii=False,indent=2),encoding='utf-8')
    (ROOT/'_sources/portraits_audit.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    sheet((ROOT/'portraits').glob('*.png'),ROOT/'_sheets/portraits.png',cols=5,cell=(236,260))
    print(json.dumps(report,indent=2))

def card_frame(empty=False,back=False):
    s=3
    im=Image.new('RGBA',(512*s,716*s)); d=ImageDraw.Draw(im)
    def rr(box,r,fill,outline=None,width=1):
        d.rounded_rectangle(tuple(int(v*s) for v in box),radius=r*s,fill=fill,outline=outline,width=width*s)
    rr((16,20,498,704),28,'#B99F70',P['navy'],5)
    rr((14,12,494,696),28,P['manila'],P['navy'],6)
    rr((24,22,484,686),20,None,'#F6DEAB',2)
    if not back:
        rr((36,40,472,522),16,P['paper'],P['navy'],3)
        rr((36,540,472,674),12,P['paper'],'#C5A66E',2)
        rr((54,529,116,536),3,P['red'])
        if empty:
            rr((40,44,468,518),12,(0,0,0,0))
    else:
        rr((36,40,472,674),16,P['green'],P['navy'],3)
    return im.resize((512,716),Image.Resampling.LANCZOS)

def cards():
    records=json.loads((ROOT/'_sources/portraits_cards_prompts.json').read_text(encoding='utf-8'))
    metadata_list=[]; report=[]
    descs={
      'card_password':'Pembe changing a workstation password: red padlock and key.',
      'card_fake_email':'Turuncu sending an impersonated email: sealed envelope and disguise mask.',
      'card_pull_plug':'Mor unplugging a coworker computer; clear unplugged cable gap.',
      'card_salt':'Yeşil secretly salting a green coffee mug.',
      'card_internet':'Sarı disabling the office router; crossed wireless pictogram.',
      'card_power':'Mavi flipping the fuse breaker; extinguished bulb.'}
    for rec in records:
        if rec['category']!='cards': continue
        source=Image.open(rec['source']).convert('RGBA')
        a=source.getchannel('A').point(lambda v:0 if v<24 else v);source.putalpha(a)
        box=a.point(lambda v:255 if v>100 else 0).getbbox();source=source.crop(box)
        if rec['id']=='card_power': source=source.crop((0,175,source.width,source.height))
        source.thumbnail((410,448),Image.Resampling.LANCZOS)
        im=card_frame(); im.alpha_composite(source,((512-source.width)//2,62+(448-source.height)//2))
        path=ROOT/'cards'/f"{rec['id']}.png";save(im,path,300000)
        Image.open(path).convert('RGBA').save(path,optimize=True)
        metadata_list.append(metadata(path,'cards',descs[rec['id']]+' Text-free card; title/body safe area [52,552,408,104], illustration area [40,44,428,474].'))
        report.append(audit(path))
    path=ROOT/'cards/card_frame_empty.png'; save(card_frame(empty=True),path)
    metadata_list.append(metadata(path,'cards','Empty manila card frame with transparent illustration window [40,44,428,474] and blank title/body safe area [52,552,408,104].'))
    report.append(audit(path))
    im=card_frame(back=True)
    mark=Image.open(ROOT/'logo/logo_mark.png').convert('RGBA'); mark=mark.crop(mark.getchannel('A').getbbox())
    small=mark.copy();small.thumbnail((52,60),Image.Resampling.LANCZOS)
    alpha=small.getchannel('A').point(lambda v:round(v*.17)); pattern=Image.new('RGBA',small.size,P['manila']);pattern.putalpha(alpha)
    for row,y in enumerate(range(62,634,82)):
        for x in range(52+(row%2)*34,432,78):
            if x+pattern.width<=460: im.alpha_composite(pattern,(x,y))
    d=ImageDraw.Draw(im)
    d.ellipse((96,190,416,510),fill=P['manila'],outline=P['navy'],width=5)
    d.ellipse((105,199,407,501),outline='#F6DEAB',width=3)
    mark.thumbnail((246,264),Image.Resampling.LANCZOS);im.alpha_composite(mark,((512-mark.width)//2,218+(264-mark.height)//2))
    path=ROOT/'cards/card_back.png';save(im,path,300000)
    metadata_list.append(metadata(path,'cards','Office-green card back with repeating company bean-in-box emblem pattern and centered emblem.'))
    report.append(audit(path))
    (ROOT/'cards/_metadata.json').write_text(json.dumps(metadata_list,ensure_ascii=False,indent=2),encoding='utf-8')
    (ROOT/'_sources/cards_audit.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    sheet((ROOT/'cards').glob('*.png'),ROOT/'_sheets/cards.png',cols=4,cell=(256,380))
    print(json.dumps(report,indent=2))

if __name__=='__main__':
    if len(sys.argv)>1 and sys.argv[1]=='cards': cards()
    else: portraits()
