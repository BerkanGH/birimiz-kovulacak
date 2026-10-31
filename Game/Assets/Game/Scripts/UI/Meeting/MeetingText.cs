using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Text helpers of the v4 meeting UI: names, task / trick / incident names, dossier effect lines, record sentences, the
    /// fictional office clock ("14:32"). Every sentence is built from keys in tr.meeting.json / en.meeting.json and avoids
    /// grammar that depends on a name (Turkish suffixes): "Label: Name" instead of "Name'in ...".
    /// Strings built here may contain player names: callers must put them in labels created by UiKit (rich text is off).
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

        public static string Task(TaskKind t) { return Loc.T("dossier.task." + LineTasks.Id(t)); }
        public static string Trick(TrickType t) { return Loc.T("dossier.trick." + t); }
        public static string Incident(IncidentKind k) { return Loc.T("incident.kind." + (int)k); }
        public static string Day(int day) { return Loc.DayName(day); }
        public static string Phase(LinePhase p) { return Loc.T("meeting.phase." + p); }

        /// <summary>Signed number, e.g. "+3" / "-1" (U+2212 is not in every font: plain hyphen-minus).</summary>
        public static string Signed(int d) { return d > 0 ? "+" + d : d.ToString(); }

        /// <summary>Per mille as a percentage: 725 -> "72.5%" (TR: "72,5%"), -1 -> "…".</summary>
        public static string Percent(int permille)
        {
            if (permille < 0) return Loc.T("company.measuring");
            return (permille / 10f).ToString("0.#", Loc.Culture) + "%";
        }

        public static string Money(int n) { return Loc.F("money.fmt", n); }

        public static LineProfile Profile(LineView v)
        {
            var cfg = Client != null ? Client.Config : LineConfig.CreateDefault();
            return cfg.Profile(v != null ? v.ManagerProfileId : "");
        }

        public static LineConfig Config => Client != null ? Client.Config : LineConfig.CreateDefault();

        public static LinePlayerView Player(LineView v, int id)
        {
            if (v == null) return null;
            foreach (var p in v.Players) if (p.Id == id) return p;
            return null;
        }

        // ------------------------------------------------------------------ icons

        public static string TaskIcon(TaskKind t)
        {
            switch (t)
            {
                case TaskKind.InvoiceReconcile: case TaskKind.BudgetSplit: case TaskKind.BonusPool: return "icons_v3/share";
                case TaskKind.CustomerSpec: case TaskKind.VendorPick: return "icons_v3/odd_email";
                case TaskKind.ReportReview: return "icons_v3/lost_report";
                case TaskKind.ArchiveVisitor: case TaskKind.RoomBooking: return "icons_v3/promotion";
                case TaskKind.ServerCode: return "icons_v3/keypad";
                case TaskKind.PhishingCheck: return "icons_v3/complaint";
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
                default: return "icons/warning";
            }
        }

        public static string SourceIcon(RecordSource s)
        {
            switch (s)
            {
                case RecordSource.PhoneLog: return "icons/call";
                case RecordSource.Bank: return "icons/quota";
                case RecordSource.TaskSystem: return "icons_v3/task_app";
                case RecordSource.ITLog: return "icons_v3/trick_app";
                case RecordSource.BadgeLog: return "icons/lock_screen";
                case RecordSource.HR: return "icons_v3/warning_letter";
                default: return "icons/notes";
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
                case "icons_v3/task_app": return "icons/task_report";
                case "icons_v3/trick_app": return "icons/warning";
                case "icons_v3/manager_line": return "icons/manager";
                default: return "icons/info";
            }
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

        // ------------------------------------------------------------------ dossier

        /// <summary>
        /// One observable incident effect as icon + sentence. Never names a culprit: the contract carries no actor.
        /// </summary>
        public static Line Effect(LineDossierEntry e, LineView v)
        {
            var line = new Line { Icon = TrickIcon(e.Trick), Who = e.Victim, Tone = Tone.Bad };
            string time = OfficeClock(e.Time, v);
            switch (e.Trick)
            {
                case TrickType.InternetKes:
                    line.Title = Loc.F("effect.InternetKes", time);
                    line.Who = LineIds.None;
                    break;
                case TrickType.KahveDok:
                case TrickType.FisCek:
                case TrickType.SahteEposta:
                case TrickType.HatDinle:
                    line.Title = e.Victim >= 0 ? Loc.F("effect." + e.Trick, Name(e.Victim), time) : Loc.F("effect.unknown", time);
                    break;
                default:
                    line.Title = Loc.F("effect.unknown", time);
                    break;
            }
            line.Detail = Loc.T("effect.note");
            return line;
        }

        // ------------------------------------------------------------------ records

        public static string SourceName(RecordSource s) { return Loc.T("record.source." + (int)s); }

        /// <summary>One authenticated record as a sentence (names inserted as plain text).</summary>
        public static string RecordSentence(LineRecordView r)
        {
            if (r == null) return "";
            string a = Name(r.Actor), s = Name(r.Subject);
            switch (r.Kind)
            {
                case RecordKind.Call: return Loc.F("record.kind.Call", a, s, r.N1 < 0 ? 0 : r.N1);
                case RecordKind.Transfer: return Loc.F("record.kind.Transfer", a, s, Money(r.N1 < 0 ? 0 : r.N1));
                case RecordKind.TaskEntry: return Loc.F("record.kind.TaskEntry", a, r.Task != TaskKind.None ? Task(r.Task) : "?");
                case RecordKind.TaskOutcome: return Loc.F("record.kind.TaskOutcome", r.Task != TaskKind.None ? Task(r.Task) : "?", r.N1, r.N2);
                case RecordKind.DealPayment: return Loc.F("record.kind.DealPayment", a, Money(r.N1 < 0 ? 0 : r.N1));
                case RecordKind.TrickLog:
                    return r.Subject >= 0 ? Loc.F("record.kind.TrickLog", a, Trick((TrickType)Mathf.Clamp(r.N1, 0, 5)), s) : Loc.F("record.kind.TrickLogAll", a, Trick((TrickType)Mathf.Clamp(r.N1, 0, 5)));
                case RecordKind.SpoofedMail: return Loc.F("record.kind.SpoofedMail", a, s);
                case RecordKind.BadgeAccess: return Loc.F(r.N2 == 1 ? "record.kind.BadgeCopy" : "record.kind.BadgeAccess", a);
                case RecordKind.Remedial: return Loc.F("record.kind.Remedial", a, s, r.N2 < 0 ? 0 : r.N2);
                case RecordKind.Warning: return Loc.F("record.kind.Warning", s);
                case RecordKind.AuditFigure: return Loc.F("record.kind.AuditFigure", s, r.N1);
                case RecordKind.ReportDraft: return Loc.F(r.N1 == 1 ? "record.kind.ReportDraftPolished" : "record.kind.ReportDraftAccurate", a);
                case RecordKind.HrFile: return Loc.F("record.kind.HrFile", s);
                case RecordKind.ItemTransfer: return Loc.F("record.kind.ItemTransfer", a, s);
                case RecordKind.OfferAcceptance: return Loc.F("record.kind.OfferAcceptance", a);
                case RecordKind.TrickEffect: return Loc.F("record.kind.TrickEffect", s, Trick((TrickType)Mathf.Clamp(r.N1, 0, 5)));
                default: return Loc.T("record.kind.unknown");
            }
        }

        // ------------------------------------------------------------------ office clock

        /// <summary>
        /// Host time of the dossier start (= end of the work block). Derived from the recording schedule when there is
        /// one, else from the phase.
        /// </summary>
        public static double DossierStart(LineView v)
        {
            if (v == null) return 0;
            if (v.Phase == LinePhase.MeetingDossier) return v.PhaseStartTime;
            var d = Config.Durations;
            var m = v.Meeting;
            int recs = m != null ? m.Recordings.Count : 0;
            double dossier = d.Dossier + d.DossierPerRecording * recs;
            bool final = m != null && m.IsFinal;
            if (v.Phase == LinePhase.MeetingDiscussion) return v.PhaseStartTime - dossier;
            if (v.Phase == LinePhase.MeetingVote) return v.PhaseStartTime - dossier - (final ? d.FinalDiscussion : d.Discussion);
            if (m != null && recs > 0) return m.Recordings[0].PlayAt - 4.0;
            return v.PhaseStartTime - (final ? d.FinalDiscussion : 0);
        }

        /// <summary>
        /// The fictional wall clock of the work block: 09:00 at the morning call, 17:30 when the meeting starts.
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

        /// <summary>Rough minutes of a match for the lobby ("≈ 20" / "≈ 33"), straight from the shipped design numbers.</summary>
        public static int ApproxMinutes(WeekLength len) { return len == WeekLength.Full ? 33 : 20; }

        /// <summary>Votes for a target in a revealed ballot (-2 counts "No action / Fire nobody").</summary>
        public static int VotesFor(LineMeetingView m, int target)
        {
            int n = 0;
            if (m != null) foreach (var x in m.Votes) if (x.Target == target) n++;
            return n;
        }
    }
}
