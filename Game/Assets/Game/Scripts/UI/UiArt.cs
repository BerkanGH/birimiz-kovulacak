using System.Collections.Generic;
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

        /// <summary>
        /// Unloads a big texture that is only used on one screen (menu backgrounds, how-to pictures). Callers must clear
        /// every style that references it first; the next Tex() reloads it.
        /// </summary>
        public static void Release(string path)
        {
            Texture2D t;
            if (!_cache.TryGetValue(path, out t)) return;
            _cache.Remove(path);
            if (t != null) Resources.UnloadAsset(t);
        }

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

        /// <summary>Stamp art for the current language (full art paths, e.g. "stamps/stamp_onaylandi" / "stamps_v3/stamp_approved").</summary>
        public static bool ApplyStamp(VisualElement e, string trPath, string enPath)
        {
            return Apply(e, Loc.Language == "en" ? enPath : trPath);
        }

        /// <summary>Applies an icon art path (e.g. "icons_v3/meeting" or "icons/info"); falls back to the classic kit icon, then a glyph tile.</summary>
        public static void ApplyIcon(VisualElement e, string path)
        {
            if (Apply(e, path)) { e.RemoveFromClassList("icon-fallback"); return; }
            string id = path ?? "";
            int slash = id.LastIndexOf('/');
            if (slash >= 0) id = id.Substring(slash + 1);
            UiKit.SetIcon(e, path != null && path.StartsWith("icons/") ? id : "info");
        }

        public static void Slice(VisualElement e, int left, int top, int right, int bottom, float scale = 0.5f)
        {
            e.style.unitySliceLeft = left;
            e.style.unitySliceTop = top;
            e.style.unitySliceRight = right;
            e.style.unitySliceBottom = bottom;
            e.style.unitySliceScale = scale;
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
