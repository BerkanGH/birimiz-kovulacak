using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Localization: every player-facing string is a key resolved from StreamingAssets/Localization/&lt;lang&gt;.json
    /// plus every &lt;lang&gt;.&lt;area&gt;.json next to it (v3 tracks keep their keys in their own area files; later files
    /// override earlier ones). Languages: "tr" and "en". Missing keys fall back to the key itself so gaps are visible.
    /// </summary>
    public static class Loc
    {
        public const string PrefKey = "lang";
        public static readonly string[] Languages = { "tr", "en" };
        static Dictionary<string, string> _table;
        public static string Language { get; private set; } = DefaultLanguage();
        /// <summary>Raised after <see cref="SetLanguage"/> reloaded the tables (screens rebuild their texts).</summary>
        public static event Action Changed;

        static string DefaultLanguage()
        {
            string saved = PlayerPrefs.GetString(PrefKey, "");
            if (saved == "tr" || saved == "en") return saved;
            return Application.systemLanguage == SystemLanguage.Turkish ? "tr" : "en";
        }

        /// <summary>Switch language, persist it and notify listeners.</summary>
        public static void SetLanguage(string lang)
        {
            if (lang != "tr" && lang != "en") return;
            PlayerPrefs.SetString(PrefKey, lang);
            PlayerPrefs.Save();
            _culture = null;
            Load(lang);
            Changed?.Invoke();
        }

        static CultureInfo _culture;
        /// <summary>Casing rules of the current language (Turkish: i -> İ, ı -> I) for upper-case headings.</summary>
        public static CultureInfo Culture
        {
            get
            {
                if (_culture == null)
                {
                    try { _culture = CultureInfo.GetCultureInfo(Language == "tr" ? "tr-TR" : "en-US"); }
                    catch (Exception) { _culture = CultureInfo.InvariantCulture; }
                }
                return _culture;
            }
        }

        /// <summary>Upper case with the language's rules. Turkish dotted/dotless i are mapped explicitly, so a player
        /// without the tr-TR culture data (or with invariant globalization) still gets "MAVİ", never "MAVI".</summary>
        public static string Upper(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (Language == "tr") return s.Replace('i', 'İ').Replace('ı', 'I').ToUpperInvariant();
            return s.ToUpper(Culture);
        }

        static Dictionary<string, string> Table
        {
            get
            {
                if (_table == null) Load(Language);
                return _table;
            }
        }

        public static void Load(string lang)
        {
            Language = lang;
            _table = new Dictionary<string, string>();
            string dir = Path.Combine(Application.streamingAssetsPath, "Localization");
            string main = Path.Combine(dir, lang + ".json");
            if (!File.Exists(main)) Debug.LogWarning("[Loc] Missing " + main);
            var files = new List<string>();
            if (File.Exists(main)) files.Add(main);
            try
            {
                if (Directory.Exists(dir))
                {
                    var areas = Directory.GetFiles(dir, lang + ".*.json");
                    Array.Sort(areas, StringComparer.Ordinal);
                    foreach (var a in areas)
                        if (!string.Equals(Path.GetFileName(a), lang + ".json", StringComparison.OrdinalIgnoreCase)) files.Add(a);
                }
            }
            catch (Exception e) { Debug.LogError("[Loc] " + e.Message); }
            foreach (var f in files)
            {
                try
                {
                    var part = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(f));
                    if (part != null) foreach (var kv in part) _table[kv.Key] = kv.Value;
                }
                catch (Exception e) { Debug.LogError("[Loc] " + Path.GetFileName(f) + ": " + e.Message); }
            }
        }

        public static bool Has(string key) { return key != null && Table.ContainsKey(key); }

        public static string T(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            string v;
            return Table.TryGetValue(key, out v) ? v : key;
        }

        /// <summary>Value or fallback key's value when the key is missing.</summary>
        public static string TOr(string key, string fallbackKey) { return Has(key) ? T(key) : T(fallbackKey); }

        public static string F(string key, params object[] args)
        {
            try { return string.Format(T(key), args); }
            catch (FormatException) { return T(key); }
        }

        // ---- Domain helpers -------------------------------------------------------------------------

        public static string ManagerName(string profileId) { return string.IsNullOrEmpty(profileId) ? T("manager.default.name") : T("manager." + profileId + ".name"); }
        public static string ManagerTitle(string profileId) { return string.IsNullOrEmpty(profileId) ? T("manager.default.title") : T("manager." + profileId + ".title"); }

        public static string DayName(int day) { return T("week.day." + day); }
    }
}
