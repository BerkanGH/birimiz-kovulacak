"""Independent v4 delivery inventory and geometry."""
TASKS = 'invoice_reconcile budget_split customer_spec report_review room_booking archive_visitor bonus_pool server_code phishing_check shift_cover vendor_pick supply_swap'.split()
EVIDENCE = 'phone_log bank task_system it_log badge_log hr'.split()
CATEGORIES = ('tasks', 'evidence', 'onboarding_v4', 'results_v4')
EXPECTED = {
    **{f'tasks/task_{name}.png': (512, 716) for name in TASKS},
    **{f'evidence/evidence_{name}.png': (512, 716) for name in EVIDENCE},
    **{f'onboarding_v4/intro_{name}.png': (1280, 720) for name in ('1_company', '2_secret', '3_meeting')},
    **{f'results_v4/company_{name}.png': (1280, 720) for name in ('survived', 'closed')},
}
FIELDS = {'id', 'path', 'w', 'h', 'category', 'nineSlice', 'screenRect', 'hasText', 'description'}
SAFE_AREA = (52, 552, 408, 104)
DESCRIPTIONS = {
    'task_invoice_reconcile': 'Blue and yellow beans each hold one matching half of a torn invoice.',
    'task_budget_split': 'Three beans reach for slices of one small budget pie on a meeting table.',
    'task_customer_spec': 'Three product box colors/sizes; a customer silhouette splits specification bubbles between blue and pink beans.',
    'task_report_review': 'Yellow inspects an exaggerated report chart with a magnifier; pink holds check/cross rubber stamps.',
    'task_room_booking': 'Blue and orange collide at one meeting-room door beside a wall calendar.',
    'task_archive_visitor': 'Green visitor with a badge carries a limited folder stack while coworkers wave from outside the archive.',
    'task_bonus_pool': 'Purple takes the whole bonus envelope from an incredulous pink bean across a diagonal table; fresh v4 composition.',
    'task_server_code': 'Server rack and keypad; four beans hold single-digit cards 4, 1, 7, 2. Illustrative digits are not a fixed gameplay code.',
    'task_phishing_check': 'A fishing hook pierces a payment email on a monitor while green reacts suspiciously.',
    'task_shift_cover': 'Empty weekend calendar slots above three beans pretending to be busy.',
    'task_vendor_pick': 'Four colored supplier samples, each paired with a face-down score card; yellow inspects them.',
    'task_supply_swap': 'Blue, pink and orange trade a stapler, toner cartridge and chair across adjoining desks.',
    'evidence_phone_log': 'Desk telephone and printed call log with handset and clock pictograms.',
    'evidence_bank': 'Open ruled ledger book and blank gold coins.',
    'evidence_task_system': 'Text-free KOVA task-system checklist window on an office monitor.',
    'evidence_it_log': 'Server fanfold printout and coffee-stained blank-key keyboard.',
    'evidence_badge_log': 'Contactless badge reader, portrait lanyard and pictorial access log.',
    'evidence_hr': 'Manila personnel folder and rubber stamp with a symbol-only impression.',
    'intro_1_company': 'All six bean identities help raise one shared productivity gauge needle.',
    'intro_2_secret': 'Blue secretly reads an unsealed envelope below the desk during a phone call.',
    'intro_3_meeting': 'Six beans discuss a projected call record and point at each other around a meeting table.',
    'company_survived': 'Five beans celebrate among desks; a foreground empty chair holds a cardboard packing box.',
    'company_closed': 'Empty dark office with packing boxes, chairs on desks, dark monitors and a dangling phone cord; shared defeat.',
}
