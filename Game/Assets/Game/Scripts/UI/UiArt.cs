using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Optional 2D art kit (generated into Art/UI, copied to Game/UI/Resources/ui/&lt;folder&gt;/&lt;id&gt;.png).
    /// Every lookup is cached (including misses) so a texture is loaded at most once; every caller has a USS-only
    /// fallback, so the UI never depends on an image being present.
    /// </summary>
    public static class UiArt
    {
        static readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();

        /// <summary>True when the button/panel skins exist (the root then gets the ".art" class).</summary>
        public static bool KitAvailable => Has("buttons/primary_normal");

        public static Texture2D Tex(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            Texture2D t;
            if (_cache.TryGetValue(path, out t)) return t;
            t = Resources.Load<Texture2D>("ui/" + path);
            _cache[path] = t;
            return t;
        }

        public static bool Has(string path) { return Tex(path) != null; }

        /// <summary>Sets the element's background image if the texture exists.</summary>
        public static bool Apply(VisualElement e, string path)
        {
            var t = Tex(path);
            if (t == null) return false;
            e.style.backgroundImage = new StyleBackground(t);
            return true;
        }

        /// <summary>First existing path wins.</summary>
        public static bool ApplyFirst(VisualElement e, string a, string b, string c = null)
        {
            return Apply(e, a) || Apply(e, b) || (c != null && Apply(e, c));
        }

        public static void Slice(VisualElement e, int left, int top, int right, int bottom, float scale = 0.5f)
        {
            e.style.unitySliceLeft = left;
            e.style.unitySliceTop = top;
            e.style.unitySliceRight = right;
            e.style.unitySliceBottom = bottom;
            e.style.unitySliceScale = scale;
        }

        // ------------------------------------------------------------------ id mapping

        public static string Snake(string pascal)
        {
            var sb = new System.Text.StringBuilder(pascal.Length + 4);
            for (int i = 0; i < pascal.Length; i++)
            {
                char ch = pascal[i];
                if (char.IsUpper(ch) && i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }

        public static string TaskIcon(TaskType t)
        {
            switch (t)
            {
                case TaskType.Coffee: return "task_coffee";
                case TaskType.Report: return "task_report";
                case TaskType.Phone: return "task_phone";
                case TaskType.CleanDesk: return "task_clean";
                case TaskType.Mail: return "task_mail";
                default: return "quota";
            }
        }

        public static string SabotageIcon(SabotageType t)
        {
            switch (t)
            {
                case SabotageType.NameplateSwap: return "sab_nameplate";
                case SabotageType.PlantItem: return "sab_plant";
                case SabotageType.LockWorkstation: return "sab_password";
                case SabotageType.HideReport: return "sab_hide_report";
                case SabotageType.SpillCoffee: return "sab_spill";
                case SabotageType.MoveClutter: return "sab_clutter";
                case SabotageType.FakeEmail: return "sab_fake_email";
                case SabotageType.PullPlug: return "sab_pull_plug";
                case SabotageType.SaltCoffee: return "sab_salt";
                case SabotageType.InternetOutage: return "sab_internet";
                case SabotageType.PowerCut: return "sab_power";
                default: return "warning";
            }
        }

        /// <summary>Applies the card illustration (cards/card_&lt;snake&gt;, cards/card_&lt;icon&gt;) or the sabotage icon.</summary>
        public static bool ApplyCardArt(VisualElement e, SabotageType t)
        {
            string icon = SabotageIcon(t);
            return Apply(e, "cards/card_" + Snake(t.ToString())) || Apply(e, "cards/card_" + icon.Replace("sab_", "")) || Apply(e, "icons/" + icon);
        }

        public static string VerbIcon(ActionVerb v, string labelKey)
        {
            switch (v)
            {
                case ActionVerb.BrewCoffee: return "task_coffee";
                case ActionVerb.WriteReport: return "task_report";
                case ActionVerb.AnswerPhone: return "task_phone";
                case ActionVerb.CleanDesk:
                case ActionVerb.CleanSpill: return "task_clean";
                case ActionVerb.SpillCoffee: return "sab_spill";
                case ActionVerb.LockWorkstation: return "sab_password";
                case ActionVerb.UnlockWorkstation: return "lock_screen";
                case ActionVerb.SearchContainer: return "hr_scan";
                case ActionVerb.AuditScanItem:
                case ActionVerb.AuditScanEvidence: return "hr_scan";
                case ActionVerb.SealEvidence: return "hr_seal";
                case ActionVerb.LockScreen: return "lock_screen";
                case ActionVerb.SendFakeEmail: return "sab_fake_email";
                case ActionVerb.PullPlug:
                case ActionVerb.PlugIn: return "sab_pull_plug";
                case ActionVerb.SaltCoffee: return "sab_salt";
                case ActionVerb.TasteCoffee: return "task_coffee";
                case ActionVerb.CutInternet:
                case ActionVerb.RestartRouter: return "sab_internet";
                case ActionVerb.CutPower:
                case ActionVerb.ResetFuse: return "sab_power";
                case ActionVerb.Place:
                    switch (labelKey)
                    {
                        case "verb.deliver": return "quota";
                        case "verb.deliver_mail": return "task_mail";
                        case "verb.place_nameplate": return "sab_nameplate";
                        case "verb.hide": return "sab_hide_report";
                        case "verb.plant": return "sab_plant";
                        default: return "hand";
                    }
                case ActionVerb.PickUp: return "hand";
                default: return "info";
            }
        }

        public static string ActivityIcon(PublicActivity a)
        {
            switch (a)
            {
                case PublicActivity.Brewing: return "task_coffee";
                case PublicActivity.Typing: return "task_report";
                case PublicActivity.OnPhone: return "task_phone";
                case PublicActivity.Cleaning: return "task_clean";
                case PublicActivity.Spilling: return "sab_spill";
                case PublicActivity.Delivering: return "quota";
                case PublicActivity.Rummaging: return "sab_plant";
                case PublicActivity.Fiddling: return "sab_nameplate";
                case PublicActivity.OnCall: return "call";
                case PublicActivity.Plugging: return "sab_pull_plug";
                case PublicActivity.ServerWork: return "sab_internet";
                case PublicActivity.FuseWork: return "sab_power";
                case PublicActivity.Drinking: return "task_coffee";
                default: return null;
            }
        }

        public static string EmoteIcon(int emote)
        {
            switch ((EmoteId)emote)
            {
                case EmoteId.Wave: return "emote_wave";
                case EmoteId.Point: return "emote_point";
                case EmoteId.Facepalm: return "emote_facepalm";
                case EmoteId.Shrug: return "emote_shrug";
                case EmoteId.Laugh: return "emote_laugh";
                default: return "info";
            }
        }

        /// <summary>Short text shown inside an icon tile when the image is missing (Latin-1 safe glyphs only).</summary>
        public static string FallbackGlyph(string iconId)
        {
            if (string.IsNullOrEmpty(iconId)) return "";
            switch (iconId)
            {
                case "task_coffee": return "K";
                case "task_report": return "R";
                case "task_phone": return "T";
                case "task_clean": return "S";
                case "task_mail": return "P";
                case "call": case "ringing": return "T";
                case "hang_up": return "×";
                case "mic_on": case "mic_off": return "M";
                case "check": case "ready": return "+";
                case "cross": case "not_ready": return "×";
                case "warning": return "!";
                case "info": return "i";
                case "favor_heart": return "+";
                case "star": case "crown": return "*";
                case "bot": return "B";
                case "email": return "@";
                case "notes": return "N";
                case "objective": return "H";
                case "cards": return "K";
                case "contacts": return "R";
                case "manager": return "M";
                case "hand": return "»";
                case "accuse": return "!";
                case "ballot": return "O";
                case "footage": case "hr_footage": return "C";
                case "timer": return "S";
                case "quota": return "%";
            }
            if (iconId.StartsWith("sab_")) return "!";
            if (iconId.StartsWith("hr_")) return "İK";
            if (iconId.StartsWith("emote_")) return ":)";
            if (iconId.StartsWith("suspicion_")) return "?";
            return iconId.Substring(0, 1).ToUpperInvariant();
        }
    }
}
