"""Record visual review only after inspecting the named contact sheets."""
import sys
sys.dont_write_bytecode=True
from kit import *
import hashlib
rows=[]
for category in ['rounds','tricks']:
    for p in sorted((ROOT/category).glob('*.png')):
        im=Image.open(p).convert('RGBA')
        a=im.getchannel('A')
        assert im.size==(512,716)
        assert a.getextrema()==(0,255)
        assert all(a.getpixel(v)==0 for v in [(0,0),(511,0),(0,715),(511,715)])
        assert p.stat().st_size<400000
        rows.append({'path':str(p.relative_to(ROOT)).replace('\\','/'),'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
report={
 'reviewed':True,
 'displayCardHeight':180,
 'sheets':['_sheets/v3_rounds.png','_sheets/v3_tricks.png'],
 'observations':[
   'Manila frame contour, navy outline weight, cream text-safe area and palette match existing sabotage cards.',
   'Bonus envelope, five cream plus one red envelope, empty folder, inserted complaint slip and promotion tie read at 180px.',
   'Shared Password depicts all six colored bean identities and six deterministic digits; server keypad reads at 180px. Crossed fingers are legible in full-size card art but remain a subtle detail at thumbnail size.',
   'All three tricks are distinct at 180px: coffee over blank keyboard, clipped headphones and cassette/eraser.',
   'No ornamental side stripes, neon, sparkles or model-generated letters. Original frame decorative dash omitted.',
   'Coffee scene intentionally crops the mischievous bean at the illustration edge to keep the spill dominant.'
 ],
 'files':rows,
 'weaknesses':['Crossed fingers in the six-bean Shared Password scene are a secondary detail at 180px.'],
 'missing':[]
}
(ROOT/'_tools/v3_cards_review.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print('Reviewed and audited',len(rows),'cards')
