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

    /// <summary>
    /// Host time at which the current match started (= Briefing start; footage seconds and the "office clock" count
    /// from it). Exact when this client saw the Briefing; otherwise (reconnect mid-match) derived from the current
    /// phase start minus the configured durations of the phases before it. UIRoot calls <see cref="Update"/> every frame.
    /// </summary>
    public static class MatchClock
    {
        public static double MatchStart;
        /// <summary>Host time the meeting (dossier freeze) started; 0 before the meeting.</summary>
        public static double MeetingStart;
        public static int MeetingMatchId = -1;

        static int _briefingMatch = -1, _derivedMatch = -1, _derivedSerial = int.MinValue;
        static double _briefingEpoch, _derived;

        public static void Update(PlayerView v)
        {
            if (v == null || v.Phase == MatchPhase.None) return;
            if (v.Phase == MatchPhase.Briefing) { _briefingMatch = v.MatchId; _briefingEpoch = v.PhaseStartTime; }
            if (_derivedMatch != v.MatchId || _derivedSerial != v.PhaseSerial)
            {
                _derivedMatch = v.MatchId;
                _derivedSerial = v.PhaseSerial;
                _derived = v.PhaseStartTime - ElapsedBefore(v.Phase);
            }
            // The remembered Briefing start wins unless it obviously belongs to another session (same match id).
            double want = _briefingMatch == v.MatchId && Math.Abs(_briefingEpoch - _derived) < 90.0 ? _briefingEpoch : _derived;
            if (Math.Abs(MatchStart - want) > 0.001) MatchStart = want;
        }

        /// <summary>Configured seconds from the match start to the start of <paramref name="phase"/>.</summary>
        public static double ElapsedBefore(MatchPhase phase)
        {
            var c = GameInfo.Config;
            if (c == null || c.Durations == null) return 0;
            var d = c.Durations;
            double t = 0;
            if (phase > MatchPhase.Briefing) t += d.Briefing;
            if (phase > MatchPhase.Work1) t += d.Work1;
            if (phase > MatchPhase.Raid) t += d.Raid;
            if (phase > MatchPhase.Work2) t += d.Work2;
            if (phase > MatchPhase.FinalInspection) t += d.FinalInspection;
            if (phase > MatchPhase.MeetingDiscussion) t += d.MeetingDiscussion;
            if (phase > MatchPhase.MeetingDefense1) t += d.MeetingDefense;
            if (phase > MatchPhase.MeetingDefense2) t += d.MeetingDefense;
            if (phase > MatchPhase.MeetingVote) t += d.MeetingVote;
            if (phase > MatchPhase.MeetingVerdict) t += d.MeetingVerdict;
            return t;
        }

        /// <summary>Seconds of office time (Briefing..FinalInspection): the range footage can be requested for.</summary>
        public static int OfficeSeconds()
        {
            int s = (int)ElapsedBefore(MatchPhase.MeetingDiscussion);
            return s < 1 ? 1 : s;
        }
    }
}
