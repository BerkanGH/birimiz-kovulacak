using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BirimizKovulacak.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Localization: every player-facing string is a key resolved from StreamingAssets/Localization/&lt;lang&gt;.json.
    /// Turkish ("tr") is the shipped language; missing keys fall back to the key itself so gaps are visible.
    /// </summary>
    public static class Loc
    {
        static Dictionary<string, string> _table;
        public static string Language { get; private set; } = "tr";

        static CultureInfo _culture;
        /// <summary>Turkish casing rules (i -> İ, ı -> I) for upper-case headings.</summary>
        public static CultureInfo Culture
        {
            get
            {
                if (_culture == null)
                {
                    try { _culture = CultureInfo.GetCultureInfo("tr-TR"); }
                    catch (Exception) { _culture = CultureInfo.InvariantCulture; }
                }
                return _culture;
            }
        }

        public static string Upper(string s) { return string.IsNullOrEmpty(s) ? "" : s.ToUpper(Culture); }

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
            string path = Path.Combine(Application.streamingAssetsPath, "Localization", lang + ".json");
            try
            {
                if (File.Exists(path))
                    _table = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path)) ?? _table;
                else Debug.LogWarning("[Loc] Missing " + path);
            }
            catch (Exception e) { Debug.LogError("[Loc] " + e.Message); }
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

        public static string PlayerName(PlayerView v, int id)
        {
            if (id == PlayerIds.None) return T("common.nobody");
            if (v != null)
                for (int i = 0; i < v.Players.Count; i++)
                    if (v.Players[i].Id == id) return v.Players[i].Name;
            return T("player.default." + id);
        }

        public static string Item(string variant) { return string.IsNullOrEmpty(variant) ? T("common.nothing") : T("item." + variant); }
        public static string Phase(MatchPhase p) { return T("phase." + p); }
        public static string Failure(FailureCode c) { return T("fail." + c); }
        public static string Task(TaskType t) { return T("task." + t); }
        public static string Activity(PublicActivity a) { return T("activity." + a); }
        public static string Desk(int index) { return F("desk.name", index + 1); }
        public static string ObjectiveName(ObjectiveType t) { return T("objective." + t + ".name"); }
        public static string ObjectiveDesc(ObjectiveType t) { return T("objective." + t + ".desc"); }

        public static string CardName(SabotageType t) { return T("card." + t + ".name"); }
        public static string CardDesc(SabotageType t) { return T("card." + t + ".desc"); }

        public static string ManagerName(string profileId) { return string.IsNullOrEmpty(profileId) ? T("manager.default.name") : T("manager." + profileId + ".name"); }
        public static string ManagerTitle(string profileId) { return string.IsNullOrEmpty(profileId) ? T("manager.default.title") : T("manager." + profileId + ".title"); }

        public static string DayName(int day) { return T("week.day." + day); }
        public static string DayShort(int day) { return T("week.day_short." + day); }

        /// <summary>"room.office" -> "Açık ofis".</summary>
        public static string Room(string roomId) { return string.IsNullOrEmpty(roomId) ? "" : T(roomId); }

        /// <summary>"cam.office_w" -> "Açık ofis (batı)".</summary>
        public static string Camera(string cameraId) { return string.IsNullOrEmpty(cameraId) ? "" : T(cameraId); }

        public static string Location(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            int desk = EntityIds.DeskIndexOf(id);
            if (desk >= 0)
            {
                if (id.EndsWith(".drawer")) return F("loc.desk_drawer", desk + 1);
                if (id.EndsWith(".surface")) return F("loc.desk_surface", desk + 1);
                if (id.EndsWith(".nameplate")) return F("loc.desk_nameplate", desk + 1);
                if (id.EndsWith(".pc")) return F("loc.desk_pc", desk + 1);
                return Desk(desk);
            }
            if (id.StartsWith("item."))
            {
                if (id.StartsWith("item.nameplate."))
                {
                    int who;
                    if (int.TryParse(id.Substring("item.nameplate.".Length), out who)) return F("loc.nameplate_of", T("player.default." + who));
                }
                return ItemFromId(id);
            }
            if (id.StartsWith("cam.") || id.StartsWith("room.")) return T(id);
            return Has("loc." + id) ? T("loc." + id) : id;
        }

        public static string ItemFromId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            if (id.StartsWith("item.report")) return Item(ItemVariants.Report);
            if (id.StartsWith("item.mug")) return Item(ItemVariants.Mug);
            if (id.StartsWith("item.nameplate")) return Item(ItemVariants.Nameplate);
            if (id.StartsWith("item.spray")) return Item(ItemVariants.Spray);
            if (id.StartsWith("item.clutter")) return Item(ItemVariants.ClutterPapers);
            if (id.StartsWith("item.mail")) return Item(ItemVariants.Mail);
            int dot = id.LastIndexOf('.');
            return Item(dot >= 0 ? id.Substring(dot + 1) : id);
        }

        /// <summary>Match clock "mm:ss" since the briefing started (the in-world office clock).</summary>
        public static string Time(double hostTime, PlayerView v)
        {
            double t = Math.Max(0, hostTime - MatchClock.MatchStart);
            int m = (int)(t / 60), s = (int)(t % 60);
            return m.ToString("00") + ":" + s.ToString("00");
        }

        public static string MatchSeconds(int seconds)
        {
            if (seconds < 0) seconds = 0;
            return (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
        }
    }

    /// <summary>Remembers when the current match started (host time of the Briefing start) and when the meeting began.</summary>
    public static class MatchClock
    {
        public static double MatchStart;
        /// <summary>Host time the meeting (dossier freeze) started; 0 before the meeting.</summary>
        public static double MeetingStart;
        public static int MeetingMatchId = -1;
    }
}
