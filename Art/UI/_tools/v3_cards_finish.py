"""V3 round/trick cards: preserve generated illustrations; deterministic kit frame and digits.

Writes only new v3 assets and records beneath Art/UI. Does not change v1/v2 PNGs.
"""
import sys
sys.dont_write_bytecode = True
from kit import *
import hashlib, shutil

LEDGER = ROOT / '_tools/v3_cards_generation.json'
ARCHIVE = ROOT / '_tools/v3_cards_sources'
DESCRIPTIONS = {
    'round_prim_havuzu': 'Bonus Pool: two beans over one bonus envelope, orange bean grabbing the whole envelope.',
    'round_farkli_eposta': 'Odd Email: five identical cream envelopes and one red envelope in a mail sorter.',
    'round_kayip_rapor': 'Lost Report: an empty manila folder and three beans accusing one another.',
    'round_ihbar_kutusu': 'Complaint Box: pink bean inserts a folded complaint slip through a box slot.',
    'round_terfi_listesi': 'Promotion List: two beans push a colleague up a podium toward a necktie.',
    'round_ortak_sifre': 'Shared Password: server keypad, six beans each holding a deterministic single-digit card; orange bean hides crossed fingers.',
    'trick_coffee_spill': 'Coffee Spill: a tipping green mug pours coffee over a blank-key keyboard.',
    'trick_wiretap': 'Wiretap: headphones connected by a red alligator clip to a coiled desk-phone cord.',
    'trick_erase_tape': 'Erase Tape: a cream-and-green eraser scrubs the blank label of a red cassette.',
}


def frame(back=False):
    scale = 3
    im = Image.new('RGBA', (512*scale, 716*scale))
    d = ImageDraw.Draw(im)
    def rr(box, radius, fill, outline=None, width=1):
        d.rounded_rectangle(tuple(int(v*scale) for v in box), radius=radius*scale,
                            fill=fill, outline=outline, width=width*scale)
    # Exact source-kit geometry; omit the old nonsemantic red decorative dash.
    rr((16,20,498,704),28,'#B99F70',P['navy'],5)
    rr((14,12,494,696),28,P['manila'],P['navy'],6)
    rr((24,22,484,686),20,None,'#F6DEAB',2)
    if back:
        rr((36,40,472,674),16,P['green'],P['navy'],3)
    else:
        rr((36,40,472,522),16,P['paper'],P['navy'],3)
        rr((36,540,472,674),12,P['paper'],'#C5A66E',2)
    return im.resize((512,716), Image.Resampling.LANCZOS)


def digits(source):
    # The six generated cards are intentionally blank. These centers were reviewed
    # against the archived 1312x1199 source; lettering never comes from imagegen.
    centers = [(143,800),(339,810),(535,816),(730,826),(932,824),(1181,841)]
    strings = ['4','1','7','2','9','6']
    d = ImageDraw.Draw(source)
    f = font(78,True)
    rec=[]
    for center,txt in zip(centers,strings):
        assert f.getmask(txt).getbbox(), 'Missing glyph: '+txt
        bb=d.textbbox(center,txt,font=f,anchor='mm')
        d.text(center,txt,font=f,fill=P['navy'],anchor='mm')
        rec.append(dict(text=txt,codepoints=[f'U+{ord(c):04X}' for c in txt],center=center,bounds=bb))
    return rec


def main():
    ledger=json.loads(LEDGER.read_text(encoding='utf-8'))
    ARCHIVE.mkdir(parents=True,exist_ok=True)
    meta={'rounds':[],'tricks':[]}; audit_records=[]; glyphs=[]
    for rec in ledger['records']:
        archived=ARCHIVE/(rec['id']+'.png')
        if not archived.exists(): shutil.copyfile(rec['source'],archived)
        rec['archivedSource']=str(archived.relative_to(ROOT)).replace('\\','/')
        rec['sourceSha256']=hashlib.sha256(archived.read_bytes()).hexdigest()
        source=Image.open(archived).convert('RGBA')
        assert source.getchannel('A').getextrema() == (0,255), 'Generation needs real alpha'
        if rec['id']=='round_ortak_sifre': glyphs=digits(source)
        a=source.getchannel('A').point(lambda v:0 if v<24 else v)
        source.putalpha(a)
        crop=a.point(lambda v:255 if v>100 else 0).getbbox()
        source=source.crop(crop)
        source.thumbnail((410,448),Image.Resampling.LANCZOS)
        out=frame()
        out.alpha_composite(source,((512-source.width)//2,62+(448-source.height)//2))
        path=ROOT/rec['category']/(rec['id']+'.png')
        save(out,path,399999)
        m=metadata(path,rec['category'],DESCRIPTIONS[rec['id']]+' Rounded manila frame matches source kit; runtime title/body safe area [52,552,408,104].',text=rec['id']=='round_ortak_sifre')
        meta[rec['category']].append(m);audit_records.append(audit(path))
    back=frame(True)
    d=ImageDraw.Draw(back)
    d.ellipse((82,174,430,522),fill=P['manila'],outline=P['navy'],width=5)
    d.ellipse((91,183,421,513),outline='#F6DEAB',width=3)
    mark=Image.open(ROOT/'logo/logo_mark.png').convert('RGBA')
    mark=mark.crop(mark.getchannel('A').getbbox());mark.thumbnail((262,286),Image.Resampling.LANCZOS)
    back.alpha_composite(mark,((512-mark.width)//2,205+(286-mark.height)//2))
    path=ROOT/'rounds/round_card_back.png';save(back,path,399999)
    meta['rounds'].append(metadata(path,'rounds','Text-free round-deck back, office green with the existing box-carrying bean emblem inside a manila seal.'))
    audit_records.append(audit(path))
    for category,rows in meta.items():
        (ROOT/category/'_metadata.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf-8')
        sheet((ROOT/category).glob('*.png'),ROOT/'_sheets'/f'v3_{category}.png',cols=7 if category=='rounds' else 3,cell=(180,235))
    report={'assets':audit_records,'typography':{'font':'Arial Bold','sizeInSourcePixels':78,'asset':'round_ortak_sifre','glyphs':glyphs},'frameSafeArea':[52,552,408,104],'illustrationArea':[40,44,428,474]}
    (ROOT/'_tools/v3_cards_audit.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    LEDGER.write_text(json.dumps(ledger,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(audit_records,indent=2))


if __name__ == '__main__': main()
