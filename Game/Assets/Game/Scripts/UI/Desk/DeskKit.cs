using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Desk UI helpers (v4): view access, names, localised texts with fallbacks, task fact/option rendering from the typed
    /// LineFact data, objective/record/offer texts, safe text inputs and small builders. The desk area owns the desk.,
    /// onboard., trick., desk.obj.*, desk.record.*, desk.offer.* keys (tr/en.desk.json) and the task.*, complaint.preset.*,
    /// mail.template.* keys (tr/en.tasks.json).
    ///
    /// Assumed verb argument conventions (the engine owner should confirm): SendText(target, text) · SendEmail(target or
    /// None = everyone, a = template or -1, c = replied mail id or -1, text) · FileComplaint(target, a = preset or -1, text)
    /// · Transfer(target, a = amount) · BuyEvidence(a = offer id) · StartRemedial(cosigner, a = task number) ·
    /// RespondRemedial(requester, a = 1 accept / 0 decline) · UseTrick(target, a = template or -1, b = apparent sender, text)
    /// · SubmitTask(target, a, b, c = TaskField mask) · GiveItem(receiver, a = held item index).
    /// </summary>
    public static class Dk
    {
        // ------------------------------------------------------------------ view access

        public static LineClient Client => LineClient.I;
        public static LineView View => LineClient.I != null ? LineClient.I.View : null;
        public static double Now => LineClient.I != null ? LineClient.I.Now : 0;
        public static LineConfig Config => LineClient.I != null ? LineClient.I.Config : LineConfig.CreateDefault();
        public static int Me => View != null ? View.Me : LineIds.None;

        public static void Send(LineVerb verb, int target = LineIds.None, int a = -1, int b = -1, int c = -1, string text = "")
        {
            LineClient.I?.Send(verb, target, a, b, c, text);
        }

        public static LinePlayerView Player(int id)
        {
            var v = View;
            if (v == null) return null;
            for (int i = 0; i < v.Players.Count; i++) if (v.Players[i].Id == id) return v.Players[i];
            return null;
        }

        public static bool ComputerBlocked(LineView v) { return v != null && v.Mine.ComputerBlockedUntil > Now; }
        public static bool PhoneDead(LineView v) { return v != null && v.Mine.PhoneDeadUntil > Now; }
        public static bool InRound(LinePhase p) { return p == LinePhase.RoundIntro || p == LinePhase.RoundPlay || p == LinePhase.RoundReveal; }

        public static LineProfile Profile(LineView v) { return Config.Profile(v != null ? v.ManagerProfileId : ""); }

        // ------------------------------------------------------------------ names / text

        public static string T(string key) { return Loc.T(key); }
        public static string F(string key, params object[] args) { return Loc.F(key, args); }

        /// <summary>Shared key if another area defines it, else the desk area's own copy (both languages always resolve).</summary>
        public static string TShared(string sharedKey, string ownKey) { return Loc.Has(sharedKey) ? Loc.T(sharedKey) : Loc.T(ownKey); }

        public static string Name(int id)
        {
            if (id == LineIds.None || id == LineIds.Nobody) return T("desk.nobody");
            if (id == LineIds.Manager) return ManagerName(View != null ? View.ManagerProfileId : "");
            if (id == LineIds.System) return TShared("line.system.name", "desk.system");
            var p = Player(id);
            if (p != null && !string.IsNullOrEmpty(p.Name)) return p.Name;
            return TShared("player.default." + id, "desk.player");
        }

        public static string ManagerName(string profile)
        {
            if (string.IsNullOrEmpty(profile)) return TShared("manager.default.name", "desk.mgr.default.name");
            return TShared("manager." + profile + ".name", "desk.mgr." + profile + ".name");
        }

        public static string ManagerTitle(string profile)
        {
            if (string.IsNullOrEmpty(profile)) return TShared("manager.default.title", "desk.mgr.default.title");
            return TShared("manager." + profile + ".title", "desk.mgr." + profile + ".title");
        }

        public static string DayName(int day) { return TShared("week.day." + day, "desk.day." + day); }

        public static string Pct(int permille) { return (permille / 10f).ToString(permille % 10 == 0 ? "0" : "0.0", Loc.Culture) + "%"; }

        /// <summary>Signed delta for display ("+2", "–1", "0").</summary>
        public static string Delta(int d)
        {
            if (d > 0) return "+" + Num(d);
            if (d < 0) return "–" + Num(-d);
            return "0";
        }

        public static string PhaseName(LinePhase p) { return T("desk.phase." + p); }

        /// <summary>"az önce" / "3 dk önce" relative host time.</summary>
        public static string Ago(double t)
        {
            int s = (int)(Now - t);
            if (s < 60) return T("desk.time.now");
            return F("desk.time.min_ago", s / 60);
        }

        // ------------------------------------------------------------------ tasks

        public static string TaskId(TaskKind k) { return LineTasks.Id(k); }
        public static string TaskName(TaskKind k) { return k == TaskKind.None ? T("desk.task.none") : T("task." + LineTasks.Id(k) + ".name"); }
        public static string TaskRule(TaskKind k) { return k == TaskKind.None ? "" : T("task." + LineTasks.Id(k) + ".rule"); }

        static readonly Color[] TaskColors =
        {
            new Color32(0x8A, 0x90, 0xA6, 255),
            new Color32(0x3B, 0x7D, 0xD8, 255), new Color32(0x2F, 0x9E, 0x6B, 255), new Color32(0xE0, 0x7A, 0x5F, 255), new Color32(0x8E, 0x5B, 0xC7, 255),
            new Color32(0xE0, 0xA1, 0x2E, 255), new Color32(0x5C, 0x6B, 0x8C, 255), new Color32(0xD2, 0x38, 0x2F, 255), new Color32(0x1F, 0x8F, 0x9B, 255),
            new Color32(0xC2, 0x4F, 0x8F, 255), new Color32(0x7A, 0x9A, 0x2B, 255), new Color32(0xB8, 0x6B, 0x2E, 255), new Color32(0x4A, 0x6F, 0xB0, 255),
        };

        public static Color TaskColor(TaskKind k) { int i = (int)k; return TaskColors[i >= 0 && i < TaskColors.Length ? i : 0]; }

        /// <summary>Icon id of a task (icons_v3/&lt;id&gt; with the v2 fallback glyph tile).</summary>
        public static string TaskIcon(TaskKind k)
        {
            switch (k)
            {
                case TaskKind.InvoiceReconcile: return "task_invoice";
                case TaskKind.BudgetSplit: return "task_budget";
                case TaskKind.CustomerSpec: return "task_spec";
                case TaskKind.ReportReview: return "task_report";
                case TaskKind.RoomBooking: return "task_room";
                case TaskKind.ArchiveVisitor: return "task_archive";
                case TaskKind.BonusPool: return "share";
                case TaskKind.ServerCode: return "keypad";
                case TaskKind.PhishingCheck: return "task_phish";
                case TaskKind.ShiftCover: return "task_shift";
                case TaskKind.VendorPick: return "task_vendor";
                case TaskKind.SupplySwap: return "task_swap";
                default: return "task_app";
            }
        }

        /// <summary>Resources "ui/tasks/task_&lt;id&gt;" (UiArt path without the ui/ prefix).</summary>
        public static string TaskArtId(TaskKind k) { return "tasks/task_" + LineTasks.Id(k); }

        public static string TrickName(TrickType t) { return T("trick." + t + ".name"); }

        public static string TrickHow(TrickType t)
        {
            var c = Config;
            switch (t)
            {
                case TrickType.KahveDok: return F("trick.KahveDok.how", Mathf.RoundToInt(c.CoffeeSeconds));
                case TrickType.FisCek: return F("trick.FisCek.how", Mathf.RoundToInt(c.PhoneDeadSeconds));
                case TrickType.InternetKes: return F("trick.InternetKes.how", Mathf.RoundToInt(c.InternetSeconds));
                case TrickType.HatDinle: return F("trick.HatDinle.how", Mathf.RoundToInt(c.WiretapSeconds));
                default: return T("trick." + t + ".how");
            }
        }

        public static bool TrickNeedsTarget(TrickType t) { return t != TrickType.InternetKes && t != TrickType.None; }

        public static string TrickArtId(TrickType t)
        {
            switch (t)
            {
                case TrickType.SahteEposta: return "cards/card_fake_email";
                case TrickType.KahveDok: return "tricks/trick_coffee_spill";
                case TrickType.FisCek: return "cards/card_pull_plug";
                case TrickType.InternetKes: return "cards/card_internet";
                case TrickType.HatDinle: return "tricks/trick_wiretap";
                default: return "cards/card_back";
            }
        }

        public static string TrickIcon(TrickType t)
        {
            switch (t)
            {
                case TrickType.SahteEposta: return "fake_email";
                case TrickType.KahveDok: return "coffee_spill";
                case TrickType.FisCek: return "phone_dead";
                case TrickType.InternetKes: return "wifi_off";
                case TrickType.HatDinle: return "wiretap";
                default: return "trick_app";
            }
        }

        // ---- fact / option rendering

        static string Q(int n) { return n < 0 ? "?" : n.ToString(); }

        static string Label(string key, int n, string fallbackKey = null)
        {
            if (n < 0) return "?";
            return Loc.Has(key + "." + n) ? T(key + "." + n) : (fallbackKey != null ? T(fallbackKey) : (n + 1).ToString());
        }

        /// <summary>
        /// Fills a task template: {p} player, {n1}..{n3} numbers ("?" while unknown), {i1}..{i3} = n+1, {v} = option value + 1
        /// plus the decoded extras {color} {size} {day} {title} {item} {slot} {shift} {vendor} {acct} {ok} {val}.
        /// </summary>
        public static string Fill(string tpl, TaskKind kind, string key, int p, int n1, int n2, int n3, int value = -1, bool option = false)
        {
            if (string.IsNullOrEmpty(tpl) || tpl.IndexOf('{') < 0) return tpl ?? "";
            string s = tpl;
            if (s.Contains("{p}")) s = s.Replace("{p}", p >= 0 ? Name(p) : "?");
            s = s.Replace("{n1}", Q(n1)).Replace("{n2}", Q(n2)).Replace("{n3}", Q(n3));
            s = s.Replace("{i1}", n1 < 0 ? "?" : (n1 + 1).ToString()).Replace("{i2}", n2 < 0 ? "?" : (n2 + 1).ToString()).Replace("{i3}", n3 < 0 ? "?" : (n3 + 1).ToString());
            if (s.Contains("{v}")) s = s.Replace("{v}", (value + 1).ToString());
            if (s.IndexOf('{') < 0) return s;
            string id = LineTasks.Id(kind);
            switch (kind)
            {
                case TaskKind.CustomerSpec:
                {
                    int packed = option ? n1 : key == "spec" ? n2 : key == "true_spec" ? n1 : -1;
                    if (packed >= 0)
                    {
                        s = s.Replace("{color}", T("task.customer_spec.color." + packed / 9)).Replace("{size}", T("task.customer_spec.size." + packed / 3 % 3)).Replace("{day}", T("task.customer_spec.day." + packed % 3));
                    }
                    if (key == "color" || key == "size" || key == "day") s = s.Replace("{val}", n1 < 0 ? "?" : T("task.customer_spec." + key + "." + n1));
                    break;
                }
                case TaskKind.ArchiveVisitor:
                {
                    int title = option ? n2 : key == "need_doc" ? n1 : key == "doc" ? n2 : -1;
                    if (s.Contains("{title}")) s = s.Replace("{title}", title < 0 ? "?" : T("task.archive_visitor.title." + title));
                    break;
                }
                case TaskKind.SupplySwap:
                    if (s.Contains("{item}")) s = s.Replace("{item}", n1 < 0 ? "?" : T("task.supply_swap.item." + n1));
                    break;
                case TaskKind.RoomBooking:
                    if (s.Contains("{slot}")) s = s.Replace("{slot}", n1 == 9 ? T("task.room_booking.opt.yield") : Label("task.room_booking.slot", n1));
                    if (s.Contains("{ok}")) s = s.Replace("{ok}", n2 > 0 ? T("desk.reveal.ok") : T("desk.reveal.no"));
                    break;
                case TaskKind.ShiftCover:
                    if (s.Contains("{shift}")) s = s.Replace("{shift}", n1 == 9 ? T("task.shift_cover.opt.decline") : Label("task.shift_cover.shift", n1));
                    break;
                case TaskKind.VendorPick:
                    if (s.Contains("{vendor}")) s = s.Replace("{vendor}", Label("task.vendor_pick.vendor", n1));
                    break;
                case TaskKind.PhishingCheck:
                    if (s.Contains("{acct}")) s = s.Replace("{acct}", n1 < 0 ? "????" : n1.ToString("0000"));
                    break;
                case TaskKind.BudgetSplit:
                    if (s.Contains("{ok}")) s = s.Replace("{ok}", n3 > 0 ? T("desk.reveal.ok") : T("desk.reveal.no"));
                    break;
            }
            if (s.Contains("{ok}")) s = s.Replace("{ok}", n1 > 0 ? T("desk.reveal.ok") : T("desk.reveal.no"));
            return s;
        }

        public static string FactText(TaskKind kind, LineFact f)
        {
            return Fill(T("task." + LineTasks.Id(kind) + ".fact." + f.Key), kind, f.Key, f.P, f.N1, f.N2, f.N3);
        }

        public static string ControlLabel(TaskKind kind, LineControlView c) { return T("task." + LineTasks.Id(kind) + ".control." + c.Key); }

        public static string OptionText(TaskKind kind, LineOption o)
        {
            return Fill(T("task." + LineTasks.Id(kind) + ".opt." + o.Key), kind, o.Key, o.P, o.N1, o.N2, -1, o.Value, true);
        }

        /// <summary>True when picking this option is a sabotage (option flag, or a sabotage control's non-zero value).</summary>
        public static bool IsSabotage(LineControlView c, LineOption o)
        {
            if (o.Sabotage) return true;
            if (!c.Sabotage) return false;
            for (int i = 0; i < c.Options.Count; i++) if (c.Options[i].Sabotage) return false;
            return o.Value != 0;
        }

        public static string ItemName(int type) { return T("task.supply_swap.item." + type); }

        /// <summary>Public ballot value of a task reveal (budget request, slot, flagged colleague, shift, vendor).</summary>
        public static string BallotText(TaskKind kind, int target)
        {
            switch (kind)
            {
                case TaskKind.PhishingCheck: return target >= 0 ? Name(target) : T("desk.reveal.abstain");
                case TaskKind.RoomBooking: return target == 9 ? T("task.room_booking.opt.yield") : Label("task.room_booking.slot", target);
                case TaskKind.ShiftCover: return target == 9 ? T("task.shift_cover.opt.decline") : Label("task.shift_cover.shift", target);
                case TaskKind.VendorPick: return Label("task.vendor_pick.vendor", target);
                default: return Q(target);
            }
        }

        public static string RoleText(TaskKind kind, int role)
        {
            string key = "task." + LineTasks.Id(kind) + ".role." + role;
            return role > 0 && Loc.Has(key) ? T(key) : "";
        }

        // ------------------------------------------------------------------ objectives

        public static string ObjectiveName(ObjectiveKind k) { return T("desk.obj." + k + ".name"); }

        public static string ObjectiveDesc(LineObjectiveView o)
        {
            return F("desk.obj." + o.Kind + ".desc", o.Target >= 0 ? Name(o.Target) : T("desk.nobody"), o.Threshold);
        }

        public static string ObjectiveProgress(LineObjectiveView o)
        {
            if (o.Progress < 0) return T("desk.obj.hidden");
            return F("desk.obj.progress", o.Progress, o.Threshold);
        }

        // ------------------------------------------------------------------ records and offers

        public static string RecordKindName(RecordKind k) { return T("desk.record.kind." + k + ".name"); }
        public static string SourceName(RecordSource s) { return T("desk.record.source." + s); }

        public static string RecordText(LineRecordView r)
        {
            string s = T("desk.record.kind." + r.Kind + ".text");
            s = s.Replace("{a}", r.Actor >= 0 || r.Actor == LineIds.Manager || r.Actor == LineIds.System ? Name(r.Actor) : "?")
                 .Replace("{s}", r.Subject >= 0 || r.Subject == LineIds.Manager || r.Subject == LineIds.System ? Name(r.Subject) : "?")
                 .Replace("{n1}", Q(r.N1)).Replace("{n2}", Q(r.N2)).Replace("{n3}", Q(r.N3));
            if (s.Contains("{task}")) s = s.Replace("{task}", r.Task != TaskKind.None ? TaskName(r.Task) : "?");
            if (s.Contains("{trick}")) s = s.Replace("{trick}", r.N1 > 0 && r.N1 <= 5 ? TrickName((TrickType)r.N1) : "?");
            if (s.Contains("{item}")) s = s.Replace("{item}", r.N1 >= 0 ? ItemName(r.N1) : "?");
            if (s.Contains("{mmss}")) s = s.Replace("{mmss}", ClockInt(Mathf.Max(0, r.N1)));
            return s;
        }

        /// <summary>"phone log · 2 min ago · authenticated record".</summary>
        public static string Provenance(LineRecordView r)
        {
            return F("desk.record.prov", SourceName(r.Source), F("desk.record.block", r.Day + 1) + ", " + Ago(r.Time));
        }

        static string SourceArt(RecordSource s)
        {
            switch (s)
            {
                case RecordSource.PhoneLog: return "phone_log";
                case RecordSource.Bank: return "bank";
                case RecordSource.TaskSystem: return "task_system";
                case RecordSource.ITLog: return "it_log";
                case RecordSource.BadgeLog: return "badge_log";
                default: return "hr";
            }
        }

        public static string RecordIcon(RecordKind k)
        {
            switch (k)
            {
                case RecordKind.Call: return "call";
                case RecordKind.Transfer: case RecordKind.DealPayment: case RecordKind.OfferAcceptance: return "bank_app";
                case RecordKind.TrickLog: case RecordKind.TrickEffect: case RecordKind.SpoofedMail: return "trick_app";
                case RecordKind.BadgeAccess: return "lock";
                case RecordKind.Warning: return "warning_letter";
                case RecordKind.Remedial: return "remedial";
                case RecordKind.HrFile: return "contacts";
                default: return "task_app";
            }
        }

        public static string OfferName(LineOfferView o)
        {
            string key = "desk.offer.kind." + o.Kind;
            string s = T(key);
            s = s.Replace("{b}", (o.Day + 1).ToString()).Replace("{t}", (o.TaskNumber + 1).ToString());
            if (s.Contains("{task}")) s = s.Replace("{task}", o.Task != TaskKind.None ? TaskName(o.Task) : "?");
            return s;
        }

        /// <summary>
        /// Reusable authenticated-record card (provenance line, kind, description, proof/presented tags). Used by the
        /// evidence app and the meeting UI; <paramref name="onClick"/> makes it a button. All player-derived text is
        /// rendered with rich text off.
        /// </summary>
        public static VisualElement RecordCard(LineRecordView r, Action onClick = null, params string[] classes)
        {
            VisualElement card = onClick != null ? (VisualElement)Tile(onClick, "dk-rec") : Box("dk-rec");
            card.Cls(classes);
            if (r.ProofOf >= 0) card.AddToClassList("dk-rec-proof");
            if (r.Presented) card.AddToClassList("dk-rec-presented");
            var head = Box("dk-rec-head").NoPick().AddTo(card);
            var recIcon = Icon(RecordIcon(r.Kind), "dk-rec-icon").AddTo(head);
            if (UiArt.Apply(recIcon, "evidence/evidence_" + SourceArt(r.Source))) { recIcon.RemoveFromClassList("icon-fallback"); recIcon.Clear(); recIcon.style.backgroundColor = StyleKeyword.Null; }
            var col = Box("dk-rec-col").NoPick().AddTo(head);
            Bold(RecordKindName(r.Kind), "dk-rec-kind").NoPick().AddTo(col);
            Text(Provenance(r), "dk-rec-prov").NoPick().AddTo(col);
            Text(RecordText(r), "dk-rec-text").NoPick().AddTo(card);
            if (r.ProofOf >= 0 || r.Presented)
            {
                var tags = Box("dk-rec-tags").NoPick().AddTo(card);
                if (r.ProofOf >= 0) Tag(T("desk.record.proof"), "tag-red").AddTo(tags);
                if (r.Presented) Tag(T("desk.record.presented"), "tag-grey").AddTo(tags);
            }
            NoRich(card);
            return card;
        }

        // ------------------------------------------------------------------ plain text safety

        /// <summary>Disables rich text on this label (player-authored text must never be parsed as markup).</summary>
        public static Label Plain(this Label l) { l.enableRichText = false; return l; }

        /// <summary>Disables rich text on every label under <paramref name="root"/> (cheap safety net after building a region).</summary>
        public static void NoRich(VisualElement root)
        {
            if (root == null) return;
            if (root is TextElement te) te.enableRichText = false;
            for (int i = 0; i < root.childCount; i++) NoRich(root[i]);
        }

        /// <summary>True when keyboard focus is inside a text field (hotkeys must not fire while typing).</summary>
        public static bool Typing(VisualElement any)
        {
            var fc = any != null && any.panel != null ? any.panel.focusController : null;
            var f = fc != null ? fc.focusedElement as VisualElement : null;
            return f != null && (f is TextField || f.GetFirstAncestorOfType<TextField>() != null);
        }

        /// <summary>A bound text input: text field (rich text off, length limited) with a live character counter label.</summary>
        public sealed class TextBox
        {
            public TextField Field;
            public Label Counter;
            public VisualElement Root;
            public int Max;
            public Action Changed;
            public string Value => Field.value ?? "";

            public void SetValue(string s) { Field.SetValueWithoutNotify(s ?? ""); Update(); }
            public void Clear() { SetValue(""); }
            public void Focus() { Field.Focus(); }
            public void SetEnabled(bool on) { Field.SetEnabled(on); }

            public void Update()
            {
                int n = Value.Length;
                Counter.SetText(n + " / " + Max);
                Counter.EnableInClassList("dk-count-full", n >= Max);
                Counter.EnableInClassList("dk-count-warn", n >= Max * 0.9f && n < Max);
            }
        }

        public static TextBox TextInput(VisualElement parent, int max, bool multiline, string placeholder, params string[] classes)
        {
            var b = new TextBox { Max = max };
            b.Root = Box("dk-textbox").Cls(classes).AddTo(parent);
            var tf = new TextField { multiline = multiline, maxLength = max, isDelayed = false };
            tf.AddToClassList("dk-input");
            if (multiline) tf.AddToClassList("dk-input-multi");
            UseFont(tf, BodyFont);
            tf.Query<TextElement>().ForEach(e => e.enableRichText = false);
            if (!string.IsNullOrEmpty(placeholder)) tf.textEdition.placeholder = placeholder;
            b.Field = tf;
            tf.AddTo(b.Root);
            b.Counter = Text("", "dk-count").NoPick().AddTo(b.Root);
            tf.RegisterValueChangedCallback(_ => { b.Update(); b.Changed?.Invoke(); });
            b.Update();
            return b;
        }

        // ------------------------------------------------------------------ art ids

        /// <summary>v2 kit icon used while the v3/v4 icon (icons_v3/&lt;id&gt;) does not exist yet.</summary>
        static string OldIcon(string v3)
        {
            switch (v3)
            {
                case "share": return "favor_heart";
                case "take_all": return "quota";
                case "odd_email": return "email";
                case "lost_report": return "task_report";
                case "complaint": return "accuse";
                case "promotion": return "star";
                case "keypad": return "sab_password";
                case "blame": return "warning";
                case "warning_letter": return "warning";
                case "agenda_target": return "objective";
                case "recording": return "footage";
                case "wiretap": return "speaker";
                case "erase_tape": return "footage";
                case "coffee_spill": return "sab_spill";
                case "phone_dead": return "sab_pull_plug";
                case "missed_call": return "call";
                case "wifi_off": return "sab_internet";
                case "fake_email": return "sab_fake_email";
                case "manager_line": return "manager";
                case "tip_off": return "accuse";
                case "morning": return "task_coffee";
                case "task_app": return "objective";
                case "mail_app": return "email";
                case "record_app": return "contacts";
                case "trick_app": return "cards";
                case "message_app": return "task_phone";
                case "bank_app": return "quota";
                case "remedial": return "task_clean";
                case "complaint_app": return "accuse";
                case "task_invoice": return "task_mail";
                case "task_budget": return "quota";
                case "task_spec": return "notes";
                case "task_report": return "task_report";
                case "task_room": return "week_calendar";
                case "task_archive": return "lock_screen";
                case "task_phish": return "warning";
                case "task_shift": return "timer";
                case "task_vendor": return "star";
                case "task_swap": return "cards";
                case "sticky_check": return "check";
                case "call": return "call";
                case "ringing": return "ringing";
                case "hang_up": return "hang_up";
                case "timer": return "timer";
                case "check": return "check";
                case "cross": return "cross";
                case "crown": return "crown";
                case "favourite": return "favor_heart";
                case "lock": return "lock_screen";
                case "info": return "info";
                case "text": return "task_phone";
                case "help": return "info";
                default: return v3;
            }
        }

        /// <summary>Icon tile: icons_v3/&lt;id&gt;, then the v2 icon, then a glyph tile.</summary>
        public static VisualElement Icon(string id, params string[] classes)
        {
            var e = Box("icon").Cls(classes).NoPick();
            SetIcon(e, id);
            return e;
        }

        public static void SetIcon(VisualElement e, string id)
        {
            if (UiArt.Apply(e, "icons_v3/" + id))
            {
                e.RemoveFromClassList("icon-fallback");
                if (e.childCount > 0 && e[0] is Label l && l.ClassListContains("icon-glyph")) l.text = "";
                return;
            }
            UiKit.SetIcon(e, OldIcon(id));
        }

        /// <summary>9-slice / background skin: adds .dk-skinned when the art exists, else .dk-unskinned (USS fallback look).</summary>
        public static bool Skin(VisualElement e, string id)
        {
            bool ok = UiArt.Apply(e, id);
            e.EnableInClassList("dk-skinned", ok);
            e.EnableInClassList("dk-unskinned", !ok);
            return ok;
        }

        /// <summary>Applies a v3 art id; false when missing (callers keep a USS fallback).</summary>
        public static bool Art(VisualElement e, string id) { return UiArt.Apply(e, id); }

        /// <summary>Portrait badge for any participant (colleague, manager, system).</summary>
        public static VisualElement Badge(int id, string size = "badge-m")
        {
            if (id == LineIds.Manager)
                return ManagerPortrait(View != null ? View.ManagerProfileId : "", false, size);
            if (id == LineIds.System)
            {
                var s = Box("pbadge", size, "dk-system-badge").NoPick();
                var ic = Box("icon", "dk-system-badge-icon").NoPick().AddTo(s);
                UiKit.SetIcon(ic, "settings");
                return s;
            }
            var badge = Box("pbadge", size).NoPick();
            SetBadge(badge, Client != null ? Client.ColorOf(id) : id, Name(id));
            return badge;
        }

        /// <summary>A rubber stamp: v3 stamp art when present, else bordered stamp lettering.</summary>
        public static VisualElement Stamp(string text, string artId, params string[] classes)
        {
            var s = Box("dk-stamp").Cls(classes).NoPick();
            if (!string.IsNullOrEmpty(artId) && UiArt.Apply(s, artId)) { s.AddToClassList("dk-stamp-art"); return s; }
            s.AddToClassList("dk-stamp-fallback");
            StampText(text, "dk-stamp-text").NoPick().AddTo(s);
            return s;
        }

        /// <summary>Language-specific stamp art id (stamps_v3/stamp_ihtar vs stamp_warning...).</summary>
        public static string StampArt(string tr, string en) { return "stamps_v3/" + (Loc.Language == "tr" ? tr : en); }

        public static Label Key(string label, params string[] classes) { return KeyCap(label, classes).Cls("dk-key"); }

        public static string KeyLabel(UnityEngine.InputSystem.InputAction a) { return GameInput.KeyLabel(a); }

        public static void Clear(VisualElement e) { e.Clear(); }

        /// <summary>Deterministic small tilt for paper items (-max..max degrees) from an integer seed.</summary>
        public static float Tilt(int seed, float max = 1.6f)
        {
            uint h = (uint)(seed * 2654435761u);
            h ^= h >> 13;
            return ((h % 1000) / 999f * 2f - 1f) * max;
        }

        public static void SetTilt(VisualElement e, float deg) { e.style.rotate = new Rotate(new Angle(deg, AngleUnit.Degree)); }

        /// <summary>Colleagues (everyone but me) in id order.</summary>
        public static void Colleagues(LineView v, List<LinePlayerView> into, bool includeMe = false)
        {
            into.Clear();
            if (v == null) return;
            for (int i = 0; i < v.Players.Count; i++)
                if (includeMe || v.Players[i].Id != v.Me) into.Add(v.Players[i]);
        }

        /// <summary>A playing card (art with a printed label strip) or a coloured fallback card with the task icon.</summary>
        public static VisualElement TaskCard(TaskKind k, params string[] classes)
        {
            var c = Box("dk-taskcard").Cls(classes).NoPick();
            if (UiArt.Apply(c, TaskArtId(k)))
            {
                c.AddToClassList("dk-taskcard-art");
                return c;
            }
            c.AddToClassList("dk-taskcard-fallback");
            c.style.backgroundColor = TaskColor(k);
            Icon(TaskIcon(k), "dk-taskcard-icon").AddTo(c);
            Title(TaskName(k), "dk-taskcard-name").NoPick().AddTo(c);
            return c;
        }

        public static VisualElement TrickCard(TrickType t, params string[] classes)
        {
            var c = Box("dk-card").Cls(classes).NoPick();
            if (Art(c, TrickArtId(t))) { Title(TrickName(t), "dk-card-label").NoPick().AddTo(c); }
            else
            {
                c.AddToClassList("dk-card-fallback");
                Icon(TrickIcon(t), "dk-card-fallback-icon").AddTo(c);
                Title(TrickName(t), "dk-card-fallback-name").NoPick().AddTo(c);
            }
            return c;
        }

        /// <summary>A plain clickable tile (no default button skin): pickers, choice cards, list rows.</summary>
        public static Button Tile(Action onClick, params string[] classes)
        {
            var b = new Button(() => { Sfx.Play("click"); onClick?.Invoke(); }).Cls("dk-tile").Cls(classes);
            b.RemoveFromClassList(Button.ussClassName);
            return b;
        }

        /// <summary>
        /// Colleague picker: one tile per colleague (badge + name). Returns an updater that marks the selected id
        /// (call it after the selection changes; no rebuild needed).
        /// </summary>
        public static Action<int> Picker(VisualElement parent, LineView v, Action<int> onPick, bool includeMe = false, string size = "badge-l", int[] disabledIds = null)
        {
            var row = Box("dk-picker").AddTo(parent);
            var tiles = new List<(int id, VisualElement tile)>();
            for (int i = 0; i < v.Players.Count; i++)
            {
                var p = v.Players[i];
                if (!includeMe && p.Id == v.Me) continue;
                int id = p.Id;
                var t = Tile(() => onPick(id), "dk-pick").AddTo(row);
                Badge(id, size).AddTo(t);
                Bold(p.Name, "dk-pick-name").Plain().NoPick().AddTo(t);
                if (!p.Connected) { t.AddToClassList("dk-pick-away"); Text(T("desk.away"), "dk-pick-away-tag").NoPick().AddTo(t); }
                var check = Icon("check", "dk-pick-check").AddTo(t);
                check.pickingMode = PickingMode.Ignore;
                if (disabledIds != null && Array.IndexOf(disabledIds, id) >= 0) t.SetEnabled(false);
                tiles.Add((id, t));
            }
            return sel =>
            {
                for (int i = 0; i < tiles.Count; i++)
                {
                    bool on = tiles[i].id == sel;
                    bool was = tiles[i].tile.ClassListContains("dk-pick-on");
                    tiles[i].tile.EnableInClassList("dk-pick-on", on);
                    if (on && !was) UiMotion.Pulse(tiles[i].tile, 0.07f, 0.22f);
                }
            };
        }

        /// <summary>Compact colleague picker (rounded chips: small badge + name). Returns the selection updater.</summary>
        public static Action<int> ChipPicker(VisualElement parent, LineView v, Action<int> onPick, bool includeMe = false, int[] disabledIds = null, bool everyone = false)
        {
            var row = Box("dk-chips").AddTo(parent);
            var chips = new List<(int id, VisualElement chip)>();
            if (everyone)
            {
                var c = Tile(() => onPick(LineIds.None), "dk-chip", "dk-chip-all").AddTo(row);
                Icon("mail_app", "dk-chip-icon").AddTo(c);
                Bold(T("desk.mail.everyone"), "dk-chip-text").NoPick().AddTo(c);
                chips.Add((LineIds.None, c));
            }
            for (int i = 0; i < v.Players.Count; i++)
            {
                var p = v.Players[i];
                if (!includeMe && p.Id == v.Me) continue;
                int id = p.Id;
                var c = Tile(() => onPick(id), "dk-chip").AddTo(row);
                Badge(id, "badge-xs").AddTo(c);
                Bold(p.Name, "dk-chip-text").Plain().NoPick().AddTo(c);
                if (disabledIds != null && Array.IndexOf(disabledIds, id) >= 0) c.SetEnabled(false);
                chips.Add((id, c));
            }
            return sel =>
            {
                for (int i = 0; i < chips.Count; i++) chips[i].chip.EnableInClassList("dk-chip-on", chips[i].id == sel);
            };
        }

        /// <summary>Shake that first finishes a running slide-in (both use the "move" channel; a cut-short slide would leave the element transparent).</summary>
        public static void Shake(VisualElement e, float amplitude = 8f, float seconds = 0.35f)
        {
            if (e == null) return;
            UiMotion.Stop(e, "move");
            e.style.opacity = StyleKeyword.Null;
            e.style.translate = StyleKeyword.Null;
            UiMotion.Shake(e, amplitude, seconds);
        }

        /// <summary>Section heading: stamp-style band label plus an optional hint line.</summary>
        public static VisualElement Heading(VisualElement parent, string text, string hint = null)
        {
            var h = Box("dk-heading").NoPick().AddTo(parent);
            StampText(Loc.Upper(text), "dk-heading-text").NoPick().AddTo(h);
            if (!string.IsNullOrEmpty(hint)) Text(hint, "dk-heading-hint").NoPick().AddTo(h);
            return h;
        }

        /// <summary>Plays a sound if the kit has it (new ids fall back to an existing clip).</summary>
        public static void Play(string id, string fallback = null, float volume = 1f)
        {
            if (Sfx.Has(id)) Sfx.Play(id, volume);
            else if (fallback != null) Sfx.Play(fallback, volume);
        }

        /// <summary>The colleague a message thread is "with" (the other side), from my point of view.</summary>
        public static int ThreadPartner(LineMessage m, int me) { return m.From == me ? m.To : m.From; }
    }
}
