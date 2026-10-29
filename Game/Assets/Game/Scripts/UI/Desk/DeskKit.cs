using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Desk UI helpers: view access, names, localised texts with fallbacks (the desk area file owns desk./round./trick./
    /// mail./phone./onboard./topic./complaint. keys), art ids of the v3 kit with fallbacks to the v2 kit, small builders.
    /// </summary>
    public static class Dk
    {
        // ------------------------------------------------------------------ view access

        public static LineClient Client => LineClient.I;
        public static LineView View => LineClient.I != null ? LineClient.I.View : null;
        public static double Now => LineClient.I != null ? LineClient.I.Now : 0;
        public static LineConfig Config => LineClient.I != null ? LineClient.I.Config : LineConfig.CreateDefault();
        public static int Me => View != null ? View.Me : LineIds.None;

        public static void Send(LineVerb verb, int target = LineIds.None, int a = -1, int b = -1)
        {
            LineClient.I?.Send(verb, target, a, b);
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
            if (id == LineIds.None) return T("desk.nobody");
            if (id == LineIds.Intern) return TShared("line.intern.name", "desk.intern");
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

        public static string RoundName(RoundType t) { return T("round." + t + ".name"); }
        public static string RoundRule(RoundType t) { return T("round." + t + ".rule"); }
        public static string RoundExample(RoundType t) { return T("round." + t + ".example"); }
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

        public static bool TrickNeedsTarget(TrickType t) { return t != TrickType.InternetKes && t != TrickType.KayitSil && t != TrickType.None; }

        /// <summary>v3.1 Odd Email topic text (topic.&lt;category*4+item&gt;).</summary>
        public static string Topic(int id) { return id < 0 ? "" : T("topic." + id); }
        public static string ComplaintReason(int r) { return r < 0 ? "" : T("complaint.reason." + r); }

        /// <summary>Signed blame delta for display ("+2", "–1", "0").</summary>
        public static string Delta(int d)
        {
            if (d > 0) return "+" + Num(d);
            if (d < 0) return "–" + Num(-d);
            return "0";
        }

        public static string PhaseName(LinePhase p) { return T("desk.phase." + p); }

        // ------------------------------------------------------------------ mail

        public static bool TemplateMentions(int template) { return T("mail.personal." + template).Contains("{0}"); }

        public static string MailSubject(LineMailView m)
        {
            switch (m.Kind)
            {
                case MailKind.Manager: return T("mail.manager." + m.TemplateId + ".subject");
                case MailKind.System: return T("mail.system." + m.TemplateId + ".subject");
                default: return T("mail.personal." + m.TemplateId + ".subject");
            }
        }

        public static string MailBody(LineMailView m)
        {
            switch (m.Kind)
            {
                case MailKind.Manager: return F("mail.manager." + m.TemplateId, ManagerName(View != null ? View.ManagerProfileId : ""));
                case MailKind.System: return T("mail.system." + m.TemplateId);
                default: return PersonalBody(m.TemplateId, m.About);
            }
        }

        public static string PersonalBody(int template, int about)
        {
            return F("mail.personal." + template, about >= 0 ? Name(about) : T("desk.mail.someone"));
        }

        // ------------------------------------------------------------------ art ids

        public static string RoundArtId(RoundType t)
        {
            switch (t)
            {
                case RoundType.PrimHavuzu: return "rounds/round_prim_havuzu";
                case RoundType.FarkliEposta: return "rounds/round_farkli_eposta";
                case RoundType.KayipRapor: return "rounds/round_kayip_rapor";
                case RoundType.IhbarKutusu: return "rounds/round_ihbar_kutusu";
                case RoundType.TerfiListesi: return "rounds/round_terfi_listesi";
                case RoundType.OrtakSifre: return "rounds/round_ortak_sifre";
                default: return "rounds/round_card_back";
            }
        }

        /// <summary>(v3 icon, v2 fallback icon) of a round.</summary>
        public static string RoundIcon(RoundType t)
        {
            switch (t)
            {
                case RoundType.PrimHavuzu: return "share";
                case RoundType.FarkliEposta: return "odd_email";
                case RoundType.KayipRapor: return "lost_report";
                case RoundType.IhbarKutusu: return "complaint";
                case RoundType.TerfiListesi: return "promotion";
                case RoundType.OrtakSifre: return "keypad";
                default: return "task_app";
            }
        }

        public static string TrickArtId(TrickType t)
        {
            switch (t)
            {
                case TrickType.SahteEposta: return "cards/card_fake_email";
                case TrickType.KahveDok: return "tricks/trick_coffee_spill";
                case TrickType.FisCek: return "cards/card_pull_plug";
                case TrickType.InternetKes: return "cards/card_internet";
                case TrickType.HatDinle: return "tricks/trick_wiretap";
                case TrickType.KayitSil: return "tricks/trick_erase_tape";
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
                case TrickType.KayitSil: return "erase_tape";
                default: return "trick_app";
            }
        }

        /// <summary>v2 kit icon used while the v3 icon (icons_v3/&lt;id&gt;) does not exist yet.</summary>
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
                case "intern": return "bot";
                case "morning": return "task_coffee";
                case "task_app": return "objective";
                case "mail_app": return "email";
                case "record_app": return "contacts";
                case "trick_app": return "cards";
                case "agenda_app": return "week_calendar";
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

        /// <summary>Portrait badge for any participant (colleague, intern, manager, system).</summary>
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
            if (id == LineIds.Intern)
            {
                var b = Box("pbadge", size, "dk-intern-badge").NoPick();
                if (!UiArt.Apply(b, "icons_v3/intern"))
                {
                    b.AddToClassList("pbadge-fallback");
                    Bold(T("desk.intern_initial"), "pbadge-initial").NoPick().AddTo(b);
                }
                return b;
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

        /// <summary>Keycap + label hint ("[Y] Answer").</summary>
        public static VisualElement Hint(string key, string text, params string[] classes)
        {
            var h = Box("dk-hint").Cls(classes).NoPick();
            Key(key).AddTo(h);
            Text(text, "dk-hint-text").NoPick().AddTo(h);
            return h;
        }

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

        /// <summary>
        /// A playing card (round or trick art, 512×716 with a blank label strip at the bottom) with the localised name
        /// printed into the strip. Without art: a manila card with the icon and the name.
        /// </summary>
        public static VisualElement Card(string artId, string iconId, string name, params string[] classes)
        {
            var c = Box("dk-card").Cls(classes).NoPick();
            if (Art(c, artId))
            {
                if (!string.IsNullOrEmpty(name)) Title(name, "dk-card-label").NoPick().AddTo(c);
            }
            else
            {
                c.AddToClassList("dk-card-fallback");
                Icon(iconId, "dk-card-fallback-icon").AddTo(c);
                if (!string.IsNullOrEmpty(name)) Title(name, "dk-card-fallback-name").NoPick().AddTo(c);
            }
            return c;
        }

        public static VisualElement RoundCard(RoundType t, params string[] classes) { return Card(RoundArtId(t), RoundIcon(t), RoundName(t), classes); }
        public static VisualElement TrickCard(TrickType t, params string[] classes) { return Card(TrickArtId(t), TrickIcon(t), TrickName(t), classes); }

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
                Bold(p.Name, "dk-pick-name").NoPick().AddTo(t);
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

        /// <summary>Shake that first finishes a running slide-in (both use the "move" channel; a cut-short slide would leave the element transparent).</summary>
        public static void Shake(VisualElement e, float amplitude = 8f, float seconds = 0.35f)
        {
            if (e == null) return;
            UiMotion.Stop(e, "move");
            e.style.opacity = StyleKeyword.Null;
            e.style.translate = StyleKeyword.Null;
            UiMotion.Shake(e, amplitude, seconds);
        }

        /// <summary>Compact colleague picker (rounded chips: small badge + name). Returns the selection updater.</summary>
        public static Action<int> ChipPicker(VisualElement parent, LineView v, Action<int> onPick, bool includeMe = false, int[] disabledIds = null)
        {
            var row = Box("dk-chips").AddTo(parent);
            var chips = new List<(int id, VisualElement chip)>();
            for (int i = 0; i < v.Players.Count; i++)
            {
                var p = v.Players[i];
                if (!includeMe && p.Id == v.Me) continue;
                int id = p.Id;
                var c = Tile(() => onPick(id), "dk-chip").AddTo(row);
                Badge(id, "badge-xs").AddTo(c);
                Bold(p.Name, "dk-chip-text").NoPick().AddTo(c);
                if (disabledIds != null && Array.IndexOf(disabledIds, id) >= 0) c.SetEnabled(false);
                chips.Add((id, c));
            }
            return sel =>
            {
                for (int i = 0; i < chips.Count; i++) chips[i].chip.EnableInClassList("dk-chip-on", chips[i].id == sel);
            };
        }

        /// <summary>Section heading: stamp-style band label plus an optional hint line.</summary>
        public static VisualElement Heading(VisualElement parent, string text, string hint = null)
        {
            var h = Box("dk-heading").NoPick().AddTo(parent);
            StampText(Loc.Upper(text), "dk-heading-text").NoPick().AddTo(h);
            if (!string.IsNullOrEmpty(hint)) Text(hint, "dk-heading-hint").NoPick().AddTo(h);
            return h;
        }

        /// <summary>"az önce" / "3 dk önce" relative host time.</summary>
        public static string Ago(double t)
        {
            int s = (int)(Now - t);
            if (s < 60) return T("desk.time.now");
            return F("desk.time.min_ago", s / 60);
        }

        /// <summary>Plays a sound if the kit has it (new v3 ids fall back to an existing clip).</summary>
        public static void Play(string id, string fallback = null, float volume = 1f)
        {
            if (Sfx.Has(id)) Sfx.Play(id, volume);
            else if (fallback != null) Sfx.Play(fallback, volume);
        }
    }
}
