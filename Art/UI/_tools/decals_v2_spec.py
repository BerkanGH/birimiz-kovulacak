"""Exact third-run inventory. Aspect ratios use integer centimetres."""

# id, output pixels, world centimetres, exact visible text, typeset lines, description
SPECS = [
    ('poster_teamwork', (360,510), (60,85), 'TAKIM RUHU', ['TAKIM','RUHU'], 'Ironic rowing team: blue and yellow beans row while the orange bean sleeps.'),
    ('poster_motivation', (360,510), (60,85), 'BAŞARI = MESAİ', ['BAŞARI','=','MESAİ'], 'Overtime satire with an exhausted worker, paper tower and oversized clock.'),
    ('poster_safety', (360,510), (60,85), 'DİKKAT', ['DİKKAT'], 'Red rubber-stamp office safety poster with a slipping bean, coffee spill and cable.'),
    ('memo_board', (756,468), (105,65), '', [], 'Cork notice board with blank pinned memo sheets and sticky notes; no small text.'),
    ('poster_quota', (504,360), (70,50), 'KOTA', ['KOTA'], 'Declining quota bars with a dominant descending red arrow.'),
    ('portrait_manager', (392,504), (70,90), '', [], 'Gold-framed oil-painted portrait of a pompous bean manager in a suit.'),
    ('diploma', (495,374), (45,34), 'DİPLOMA', ['DİPLOMA'], 'Framed diploma with a gold seal and abstract decorative rules; no small text.'),
    ('poster_kpi', (512,384), (80,60), 'KPI', ['KPI'], 'Presentation chart with three thick bars and a three-sector pie chart.'),
    ('poster_mission', (504,364), (90,65), 'VİZYONUMUZ', ['VİZYONUMUZ'], 'Corporate mission poster with a bold sunrise above green hills.'),
    ('poster_coffee', (360,510), (60,85), 'KAHVE KURALLARI', ['KAHVE','KURALLARI'], 'Coffee rules with exactly three large icon bullets.'),
    ('sign_salt', (512,352), (32,22), 'TUZ ≠ ŞEKER', ['TUZ ≠ ŞEKER'], 'Salt-versus-sugar warning label with two contrasting shakers.'),
    ('poster_fridge_rules', (384,504), (32,42), 'YOĞURDUMU YİYEN?', ['YOĞURDUMU','YİYEN?'], 'Passive-aggressive fridge note with an irritated half-eaten yogurt pot.'),
    ('poster_printer', (384,512), (60,80), 'YAZICI: SABIR', ['YAZICI:','SABIR'], 'Printer instructions with a frustrated bean kicking a jammed printer.'),
    ('sign_out_of_order', (504,308), (36,22), 'ARIZALI', ['ARIZALI'], 'Red out-of-order sign with crossed wrench and gear.'),
    ('logo_company', (768,384), (130,65), 'KOVA HOLDİNG', ['KOVA','HOLDİNG'], 'Fictional reception company logo: simple green bucket and rising sun mark.'),
    ('poster_welcome', (504,360), (70,50), 'HOŞ GELDİNİZ', ['HOŞ GELDİNİZ'], 'Two friendly waving bean coworkers on a welcome poster.'),
    ('sign_fusebox', (512,512), (30,30), 'YÜKSEK GERİLİM', ['YÜKSEK','GERİLİM'], 'High-voltage warning with a large navy lightning bolt in a yellow triangle.'),
    ('sign_wash_hands', (384,512), (30,40), 'ELLERİNİZİ YIKAYIN', ['ELLERİNİZİ','YIKAYIN'], 'Wash-hands sign with two hands, faucet and large water droplets.'),
    ('sign_server_danger', (512,512), (35,35), 'YETKİSİZ GİRİLMEZ', ['YETKİSİZ','GİRİLMEZ'], 'Unauthorized-access sign with a red stop hand and server rack.'),
    ('poster_relax', (504,360), (70,50), 'MOLA', ['MOLA'], 'Lounge poster with bean coworkers relaxing on a beach.'),
    ('poster_vending', (384,512), (45,60), 'ATIŞTIR!', ['ATIŞTIR!'], 'Vending snack advertisement with a happy bean, crisp packet and cookie.'),
]

DECAL_SIZES = {row[0]:row[1] for row in SPECS}
DECAL_TEXT = {row[0]:row[3] for row in SPECS if row[3]}
DECAL_ASPECTS = {row[0]:row[2] for row in SPECS}
