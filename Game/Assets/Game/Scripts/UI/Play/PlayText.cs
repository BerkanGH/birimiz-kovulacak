using System.Collections.Generic;
using System.Text;
using BirimizKovulacak.Core;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 text helpers for the minimal play UI. Every sentence comes from Localization/&lt;lang&gt;.v5.json ("v5.*" keys)
    /// and avoids grammar that depends on a name (Turkish suffixes): "Label: Name" style. Strings may contain player
    /// names: show them in labels with rich text off (UiKit.Text does that).
    /// </summary>
    public static class PlayText
    {
        public static string T(string key) { return Loc.T("v5." + key); }
        public static string F(string key, params object[] args) { return Loc.F("v5." + key, args); }

        public static string Name(int id)
        {
            var c = LineClient.I;
            if (c != null) return c.NameOf(id);
            return id >= 0 ? Loc.T("player.default." + id) : Loc.T("common.nobody");
        }

        public static string Names(IEnumerable<int> ids)
        {
            var sb = new StringBuilder();
            foreach (int id in ids) { if (sb.Length > 0) sb.Append(", "); sb.Append(Name(id)); }
            return sb.Length > 0 ? sb.ToString() : T("common.none");
        }

        public static string Phase(LinePhase p) { return T("phase." + p); }
        public static string Hint(LinePhase p) { return T("hint." + p); }
        public static string Task(TaskKind k) { return T("task." + LineTasks.Id(k) + ".name"); }
        public static string Choice(TaskKind k, int choice) { return T("task." + LineTasks.Id(k) + ".choice." + choice); }
        public static string Failure(LineFailure f) { return Loc.Has("v5.fail." + f) ? T("fail." + f) : f.ToString(); }
        public static string Signed(int d) { return d > 0 ? "+" + d : d.ToString(); }

        /// <summary>The task's rules with the host's numbers.</summary>
        public static string TaskRules(LineTaskView t, LineConfig c, int players)
        {
            switch (t.Kind)
            {
                case TaskKind.ClientBonus:
                    return t.Group.Count >= 3
                        ? F("task.client_bonus.rules_trio", c.BonusTrioNone, c.BonusTrioTaker, c.BonusTrioOthers, c.BonusTrioMany)
                        : F("task.client_bonus.rules_pair", c.BonusPairShare, c.BonusPairTaker, c.BonusPairSharer, c.BonusPairBothTake);
                case TaskKind.PettyCash:
                    return F("task.petty_cash.rules", c.CashTake, c.CashCaughtBlame, c.CashWhistleBlame, c.CashWhistleReward);
                case TaskKind.WeekendShift:
                    return F("task.weekend_shift.rules", t.Needed >= 0 ? t.Needed : LineConfig.ShiftNeeded(players), c.ShiftSkipPay);
                default:
                    return "";
            }
        }

        /// <summary>One public dossier / incident line (typed entry -> sentence).</summary>
        public static string Dossier(LineDossierEntry e)
        {
            switch (e.Kind)
            {
                case DossierKind.TaskFailed: return F("dossier.task_failed", Task(e.Task));
                case DossierKind.BonusTakers: return F("dossier.bonus_takers", e.N1);
                case DossierKind.CashCaught: return F("dossier.cash_caught", Name(e.Player), e.N1);
                case DossierKind.Whistle: return F("dossier.whistle", Name(e.Player), Name(e.Other), e.N1);
                case DossierKind.CashMissing: return F("dossier.cash_missing", e.N1, e.N2);
                case DossierKind.ShiftRoster: return F("dossier.shift_roster", e.N1, e.N2);
                case DossierKind.Investigation: return F(e.N1 == 1 ? "dossier.investigation_guilty" : "dossier.investigation_innocent", Name(e.Player), Name(e.Other));
                case DossierKind.Leak: return F("dossier.leak", Name(e.Player), Name(e.N1), Name(e.N2));
                default: return e.Kind.ToString();
            }
        }

        public static string Firing(LineFiringView f)
        {
            string to = f.Recipients.Count > 0 ? F("final.desk_split", Names(f.Recipients), f.Share) : T("final.desk_nobody");
            return F("final.firing", Name(f.Player), f.Votes, T("decision." + f.Decision), f.Desk) + " " + to
                + (f.Removed > 0 ? " " + F("final.desk_removed", f.Removed) : "");
        }

        public static string Ballot(LineBallotView b)
        {
            string target = b.Target >= 0 ? Name(b.Target) : T("results.abstain");
            string where = b.Kind == BallotKind.Meeting ? F("results.ballot_meeting", b.Round + 1, b.Index + 1)
                : b.Kind == BallotKind.Crisis ? T("results.ballot_crisis")
                : F(b.Revote ? "results.ballot_final_revote" : "results.ballot_final", b.Round + 1);
            return where + ": " + Name(b.Voter) + " → " + target + (b.SelfAssigned ? " " + T("results.self_assigned") : "");
        }

        /// <summary>The current meeting's leak with this index (null when the view no longer has it).</summary>
        static LineRecordingPick Pick(int index)
        {
            var v = LineClient.I != null ? LineClient.I.View : null;
            if (v == null || v.Meeting == null) return null;
            foreach (var p in v.Meeting.Recordings) if (p.Index == index) return p;
            return null;
        }

        public static string Event(LineEvent e, int me)
        {
            switch (e.Type)
            {
                case LineEventType.TransferReceived: return F("event.transfer", Name(e.Actor), e.A);
                case LineEventType.Whistled:
                    return e.Target == me ? F("event.whistled_me", Name(e.Actor), e.A)
                        : F("event.whistled", Name(e.Target), LineClient.I != null ? LineClient.I.Config.CashWhistleReward : LineConfig.CreateDefault().CashWhistleReward);
                case LineEventType.Investigated: return F(e.A == 1 ? "event.investigated_guilty" : "event.investigated_innocent", Name(e.Actor), Name(e.Target));
                case LineEventType.Leaked: return F("event.leaked", Name(e.Actor), Name(e.A), Name(e.B));
                case LineEventType.BlameAdded: return F("event.blame", Name(e.Target), e.A, T("blame." + (BlameReason)e.B));
                case LineEventType.CrisisResolved: return e.Target >= 0 ? F("event.crisis_one", Name(e.Target), e.A) : F("event.crisis_tie", e.B, e.A);
                case LineEventType.Fired: return F("event.fired", Name(e.Target), e.A);
                case LineEventType.RecordingPlayed:
                {
                    // "played" in Turkish ("çalındı") also reads "was stolen": name the call and the leaker instead
                    var pick = Pick(e.A);
                    if (pick == null || pick.Caller < 0) return T(e.B == 1 ? "event.recording_played" : "event.recording_empty");
                    return F(e.B == 1 ? "event.recording_played_who" : "event.recording_empty_who", Name(pick.Caller), Name(pick.Callee), Name(pick.Leaker));
                }
                case LineEventType.MissedCall: return F("event.missed_call", Name(e.Actor));
                case LineEventType.CallBusy: return F("event.busy", Name(e.Target));
                case LineEventType.Presence: return F(e.A == 1 ? "event.online" : "event.offline", Name(e.Target));
                default: return null;
            }
        }
    }
}
