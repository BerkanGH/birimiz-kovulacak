using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Text helpers of the v3 meeting UI: names, round/trick names, dossier lines and round summaries, the fictional
    /// office clock ("14:32"). Every sentence is built from keys in tr.meeting.json / en.meeting.json and avoids
    /// grammar that depends on the name (Turkish suffixes): "Label: Name" instead of "Name'in ...".
    /// Called only when a slide is (re)built, never per frame.
    /// </summary>
    public static class MeetingText
    {
        public enum Tone { Neutral, Bad, Good, Secret }

        public struct Line
        {
            public string Icon;
            public string Title;
            public string Detail;
            public Tone Tone;
            /// <summary>Player whose badge leads the line (-1 none).</summary>
            public int Who;
        }

        public static LineClient Client => LineClient.I;

        public static string Name(int id)
        {
            var c = Client;
            if (c != null) return c.NameOf(id);
            return id >= 0 ? Loc.T("player.default." + id) : Loc.T("common.nobody");
        }

        public static string Round(RoundType t) { return Loc.T("dossier.round." + t); }
        public static string Trick(TrickType t) { return Loc.T("dossier.trick." + t); }
        public static string Day(int day) { return Loc.DayName(day); }

        public static string Phase(LinePhase p) { return Loc.T("meeting.phase." + p); }

        /// <summary>Signed blame delta, e.g. "+3" / "−1" (U+2212 is not in every font: plain hyphen-minus).</summary>
        public static string Signed(int d) { return d > 0 ? "+" + d : d.ToString(); }

        public static LineProfile Profile(LineView v)
        {
            var cfg = Client != null ? Client.Config : LineConfig.CreateDefault();
            return cfg.Profile(v != null ? v.ManagerProfileId : "");
        }

        public static LineConfig Config => Client != null ? Client.Config : LineConfig.CreateDefault();

        public static string RoundIcon(RoundType t)
        {
            switch (t)
            {
                case RoundType.PrimHavuzu: return "icons_v3/share";
                case RoundType.FarkliEposta: return "icons_v3/odd_email";
                case RoundType.KayipRapor: return "icons_v3/lost_report";
                case RoundType.IhbarKutusu: return "icons_v3/complaint";
                case RoundType.TerfiListesi: return "icons_v3/promotion";
                case RoundType.OrtakSifre: return "icons_v3/keypad";
                default: return "icons/info";
            }
        }

        public static string TrickIcon(TrickType t)
        {
            switch (t)
            {
                case TrickType.SahteEposta: return "icons/sab_fake_email";
                case TrickType.KahveDok: return "icons_v3/coffee_spill";
                case TrickType.FisCek: return "icons_v3/phone_dead";
                case TrickType.InternetKes: return "icons_v3/wifi_off";
                case TrickType.HatDinle: return "icons_v3/wiretap";
                case TrickType.KayitSil: return "icons_v3/erase_tape";
                default: return "icons/warning";
            }
        }

        /// <summary>Fallback icon (existing kit) when the v3 icon is not in yet.</summary>
        public static string FallbackIcon(string v3)
        {
            switch (v3)
            {
                case "icons_v3/share": return "icons/quota";
                case "icons_v3/odd_email": return "icons/email";
                case "icons_v3/lost_report": return "icons/task_report";
                case "icons_v3/complaint": return "icons/accuse";
                case "icons_v3/promotion": return "icons/star";
                case "icons_v3/keypad": return "icons/lock_screen";
                case "icons_v3/coffee_spill": return "icons/sab_spill";
                case "icons_v3/phone_dead": return "icons/sab_pull_plug";
                case "icons_v3/wifi_off": return "icons/sab_internet";
                case "icons_v3/wiretap": return "icons/call";
                case "icons_v3/erase_tape": return "icons/footage";
                case "icons_v3/recording": return "icons/footage";
                case "icons_v3/tip_off": return "icons/ringing";
                case "icons_v3/blame": return "icons/warning";
                case "icons_v3/warning_letter": return "icons/notes";
                case "icons_v3/agenda_target": return "icons/objective";
                case "icons_v3/intern": return "icons/contacts";
                case "icons_v3/defense": return "icons/accuse";
                case "icons_v3/meeting": return "icons/ballot";
                default: return "icons/info";
            }
        }

        // ------------------------------------------------------------------ dossier

        /// <summary>One dossier entry as an icon + title + detail line (null for entries shown elsewhere, e.g. recordings).</summary>
        public static bool Describe(LineDossierEntry e, LineView v, out Line line)
        {
            line = new Line { Who = LineIds.None, Tone = Tone.Neutral };
            switch (e.Kind)
            {
                case DossierKind.Favourite:
                    line.Icon = "icons/favor_heart";
                    line.Title = Loc.T("dossier.favourite");
                    line.Detail = Loc.F("dossier.favourite_detail", Name(e.Player));
                    line.Who = e.Player;
                    line.Tone = Tone.Good;
                    return true;
                case DossierKind.TrickTrace:
                    line.Icon = TrickIcon(e.Trick);
                    line.Title = TraceTitle(e);
                    line.Detail = e.Named ? Loc.F("dossier.culprit_named", Name(e.Player), Signed(Profile(v).TrickCaughtBlame)) : Loc.T("dossier.culprit_unknown");
                    line.Tone = e.Named ? Tone.Bad : Tone.Secret;
                    line.Who = e.Named ? e.Player : LineIds.None;
                    return true;
                case DossierKind.RecordingErased:
                    line.Icon = "icons_v3/erase_tape";
                    line.Title = Loc.T("dossier.erased");
                    line.Detail = e.Named ? Loc.F("dossier.culprit_named", Name(e.Player), Signed(Profile(v).TrickCaughtBlame)) : Loc.T("dossier.culprit_unknown");
                    line.Tone = e.Named ? Tone.Bad : Tone.Secret;
                    line.Who = e.Named ? e.Player : LineIds.None;
                    return true;
                case DossierKind.TipResult:
                    line.Icon = "icons_v3/tip_off";
                    line.Title = Loc.F("dossier.tip", Name(e.Player));
                    bool ok = e.Arg == 1;
                    line.Detail = ok ? Loc.F("dossier.tip_true", Name(e.Player), Signed(Config.ConfirmedTipBlame)) : Loc.F("dossier.tip_false", Signed(Config.FalseTipBlame));
                    line.Tone = ok ? Tone.Bad : Tone.Secret;
                    line.Who = e.Player;
                    return true;
                case DossierKind.ComplaintLeak:
                    line.Icon = "icons_v3/complaint";
                    line.Title = Loc.T("dossier.leak");
                    line.Detail = Loc.F("dossier.leak_detail", Name(e.Player), Name(e.Victim));
                    line.Tone = Tone.Bad;
                    line.Who = e.Player;
                    return true;
                case DossierKind.RoundOutcome:
                {
                    line.Icon = RoundIcon(e.Round);
                    line.Title = Round(e.Round);
                    var r = RevealFor(v, e.Arg, e.Round);
                    line.Detail = r != null ? RoundHeadline(r) : "";
                    line.Who = e.Player;
                    return true;
                }
                default:
                    return false;
            }
        }

        static string TraceTitle(LineDossierEntry e)
        {
            switch (e.Trick)
            {
                case TrickType.SahteEposta:
                {
                    string subject = e.TemplateId >= 0 ? MailSubject(e.TemplateId) : null;
                    return subject != null ? Loc.F("dossier.trace.SahteEposta_subject", subject) : Loc.T("dossier.trace.SahteEposta");
                }
                case TrickType.KahveDok:
                case TrickType.FisCek:
                    return Loc.F("dossier.trace." + e.Trick, Name(e.Victim));
                case TrickType.HatDinle:
                    return Loc.F("dossier.trace.HatDinle", Name(e.Victim));
                default:
                    return Loc.T("dossier.trace." + e.Trick);
            }
        }

        /// <summary>Subject of a personal mail template (desk track keys), if present.</summary>
        static string MailSubject(int template)
        {
            string k = "mail.personal." + template + ".subject";
            if (Loc.Has(k)) return Loc.T(k);
            k = "mail.personal." + template;
            return Loc.Has(k) ? Loc.T(k) : null;
        }

        public static LineRoundReveal RevealFor(LineView v, int index, RoundType type)
        {
            if (v == null) return null;
            for (int i = 0; i < v.TodayReveals.Count; i++)
                if (v.TodayReveals[i].Index == index && (type == RoundType.None || v.TodayReveals[i].Type == type)) return v.TodayReveals[i];
            for (int i = 0; i < v.TodayReveals.Count; i++)
                if (v.TodayReveals[i].Type == type) return v.TodayReveals[i];
            return null;
        }

        /// <summary>One sentence summary of a round outcome.</summary>
        public static string RoundHeadline(LineRoundReveal r)
        {
            switch (r.Type)
            {
                case RoundType.PrimHavuzu:
                {
                    int takers = 0;
                    foreach (var p in r.Pairs) { if (p.ChoiceA == 1) takers++; if (p.ChoiceB == 1 && p.B != LineIds.Intern) takers++; }
                    return takers == 0 ? Loc.T("dossier.prim.all_shared") : Loc.F("dossier.prim.takers", takers);
                }
                case RoundType.FarkliEposta:
                    return Loc.F(r.Winner == r.Hidden && r.Hidden >= 0 ? "dossier.odd.caught" : "dossier.odd.escaped", Name(r.Hidden));
                case RoundType.KayipRapor:
                    return Loc.F(r.Winner == r.Hidden && r.Hidden >= 0 ? "dossier.lost.caught" : "dossier.lost.escaped", Name(r.Hidden));
                case RoundType.IhbarKutusu:
                {
                    int max = 0, top = LineIds.None, tied = 0;
                    for (int i = 0; i < r.Counts.Count; i++)
                    {
                        if (r.Counts[i] > max) { max = r.Counts[i]; top = i; tied = 1; }
                        else if (r.Counts[i] == max && max > 0) tied++;
                    }
                    if (max == 0) return Loc.T("dossier.complaint.none");
                    return tied > 1 ? Loc.F("dossier.complaint.tied", max) : Loc.F("dossier.complaint.top", Name(top), max);
                }
                case RoundType.TerfiListesi:
                    return r.Winner >= 0 ? Loc.F("dossier.promo.winner", Name(r.Winner)) : Loc.T("dossier.promo.none");
                case RoundType.OrtakSifre:
                    return Loc.F(r.Solved ? "dossier.code.cracked" : "dossier.code.failed", Name(r.Hidden));
                default:
                    return "";
            }
        }

        /// <summary>Detail lines of a round (pairs, votes...), at most <paramref name="max"/>.</summary>
        public static void RoundDetails(LineRoundReveal r, List<string> into, int max = 4)
        {
            into.Clear();
            switch (r.Type)
            {
                case RoundType.PrimHavuzu:
                    foreach (var p in r.Pairs)
                    {
                        if (into.Count >= max) break;
                        into.Add(Loc.F("dossier.prim.pair", Name(p.A), Loc.T(p.ChoiceA == 1 ? "dossier.prim.take" : "dossier.prim.share"),
                            Name(p.B), Loc.T(p.ChoiceB == 1 ? "dossier.prim.take" : "dossier.prim.share")));
                    }
                    break;
                case RoundType.FarkliEposta:
                    if (r.OddGuess >= 0) into.Add(Loc.T(r.OddGuessCorrect ? "dossier.odd.guess_right" : "dossier.odd.guess_wrong"));
                    if (r.Winner >= 0 && r.Winner != r.Hidden) into.Add(Loc.F("dossier.wrong_pick", Name(r.Winner)));
                    break;
                case RoundType.KayipRapor:
                    if (r.Winner >= 0 && r.Winner != r.Hidden) into.Add(Loc.F("dossier.wrong_pick", Name(r.Winner)));
                    break;
                case RoundType.IhbarKutusu:
                    foreach (var c in r.Complaints)
                    {
                        if (into.Count >= max) break;
                        into.Add(Loc.F("dossier.complaint.line", Name(c.Target), Loc.T("dossier.reason." + c.Reason)));
                    }
                    break;
                case RoundType.TerfiListesi:
                    if (r.Winner >= 0)
                    {
                        int backers = 0;
                        foreach (var vv in r.Votes) if (vv.Target == r.Winner) backers++;
                        into.Add(Loc.F("dossier.promo.backers", backers));
                    }
                    break;
                case RoundType.OrtakSifre:
                    if (r.Solved && r.SolvedBy >= 0) into.Add(Loc.F("dossier.code.by", Name(r.SolvedBy)));
                    break;
            }
        }

        // ------------------------------------------------------------------ recordings / office clock

        /// <summary>
        /// Host time of the dossier start (= end of the office day). Derived from the recording schedule
        /// (PlayAt = start + 4 + i × DossierPerRecording) when there is one, else from the phase.
        /// </summary>
        public static double DossierStart(LineView v)
        {
            if (v == null) return 0;
            var m = v.Meeting;
            if (m != null && m.Recordings.Count > 0)
            {
                var p = m.Recordings[0];
                return p.PlayAt - 4.0 - p.Index * Config.Durations.DossierPerRecording;
            }
            return v.Phase == LinePhase.MeetingDossier ? v.PhaseStartTime : v.PhaseStartTime;
        }

        /// <summary>
        /// The fictional wall clock of the office day: 09:00 at the morning call, 17:30 when the meeting starts.
        /// Host time is mapped linearly over the configured length of the office phases.
        /// </summary>
        public static string OfficeClock(double hostTime, LineView v)
        {
            var d = Config.Durations;
            double day = d.Morning + Config.RoundsPerDay * (d.RoundIntro + d.RoundPlay + d.RoundReveal);
            if (day < 1) day = 1;
            double end = DossierStart(v);
            double t = 1.0 - Math.Max(0, Math.Min(1, (end - hostTime) / day));
            int minutes = 9 * 60 + (int)Math.Round(t * 510);
            minutes = Mathf.Clamp(minutes, 9 * 60, 17 * 60 + 29);
            return (minutes / 60).ToString("00") + ":" + (minutes % 60).ToString("00");
        }

        // ------------------------------------------------------------------ misc

        public static int CountVoted(LineView v)
        {
            int n = 0;
            for (int i = 0; i < v.Players.Count; i++) if (v.Players[i].Voted) n++;
            return n;
        }

        public static int CountConnected(LineView v)
        {
            int n = 0;
            for (int i = 0; i < v.Players.Count; i++) if (v.Players[i].Connected) n++;
            return n;
        }

        /// <summary>Applies a v3 icon id with its existing-kit fallback, then a glyph tile.</summary>
        public static void ApplyIcon(UnityEngine.UIElements.VisualElement e, string v3Path)
        {
            if (UiArt.Apply(e, v3Path)) { e.RemoveFromClassList("icon-fallback"); return; }
            string fb = FallbackIcon(v3Path);
            if (v3Path.StartsWith("icons/")) fb = v3Path;
            UiKit.SetIcon(e, fb.StartsWith("icons/") ? fb.Substring(6) : fb);
        }

        /// <summary>Stamp art for the current language (full art paths, e.g. "stamps_v3/stamp_ihtar" / "stamps_v3/stamp_warning").</summary>
        public static bool ApplyStamp(UnityEngine.UIElements.VisualElement e, string trPath, string enPath)
        {
            return UiArt.Apply(e, Loc.Language == "en" ? enPath : trPath);
        }
    }
}
