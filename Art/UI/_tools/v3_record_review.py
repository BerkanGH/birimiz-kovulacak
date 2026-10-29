"""Record the explicitly completed v3 visual review; rerun only after viewing changed art."""
import sys
sys.dont_write_bytecode=True
import json, hashlib
from kit import ROOT, safe
from v3_validate import EXPECTED, CATEGORIES

review={
 'allCategoriesVisuallyReviewed':True,
 'method':'Each category contact sheet inspected; cards at 180px high and icons at 48px on cream and grey. Props composited and slices stretched. This record is an explicit review attestation, not automated aesthetic scoring.',
 'reviewSheets':[f'Art/UI/_sheets/v3_{c}.png' for c in CATEGORIES]+['Art/UI/_sheets/v3_icons_48.png','Art/UI/_sheets/v3_layers.png','Art/UI/_sheets/v3_props_nineslice.png','Art/UI/_sheets/v3_computer_slice.png','Art/UI/_sheets/v3_computer_effects.png'],
 'revisions':['Onboarding rooms revised to cream oval eyes matching locked beans.','Recording scene revised to show all six employees plus manager.','Hint arrow regenerated as navy marker ink instead of a filled dimensional sign.','Computer slice borders enlarged after stretched preview to protect inner rules and corners.'],
 'typography':'All listed words use deterministic Arial Bold glyphs; dotted İ and Ö checked in font cmap and visible exports. Language is A/Ö. Shared Password has the explicitly requested digit cards (417296); no title/body text is baked into round or trick cards.',
 'independentReview':'Second agent inspected full icons, exact48 icons, overlays and layer composite; no material issue identified.',
 'limitations':['Shared Password crossed fingers are subtle at 180px card height.','EMPLOYEE OF THE WEEK stamp benefits from >=400px display width for comfortable reading.','Unity import, compression and in-game animation have not been tested.'],
 'missing':[],
 'finalSha256':{p:hashlib.sha256((ROOT.parents[1]/p).read_bytes()).hexdigest() for p in EXPECTED},
}
safe(ROOT/'_tools/v3_review.json').write_text(json.dumps(review,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('Explicit review recorded for',len(review['finalSha256']),'new PNG assets.')
