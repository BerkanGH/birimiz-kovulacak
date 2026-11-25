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

        /// <summary>Header title: "Görev 1/3 · Müşteri Bonusu · Seçim", "1. toplantı · …", "… · Kovma 1/2".</summary>
        public static string PhaseTitle(LineView v)
        {
            string title = Phase(v.Phase);
            if (v.Task != null && (v.Phase == LinePhase.TaskPlay || v.Phase == LinePhase.TaskReveal || v.Phase == LinePhase.Break))
                title = F("header.task", v.Task.Number + 1, LineTasks.Count, Task(v.Task.Kind)) + " · " + title;
            // (v5.2: one meeting before the final, so the phase names say it all; the old flow's meeting 2 is numbered)
            else if (v.Meeting != null && v.Meeting.Index == 1 && v.Phase >= LinePhase.MeetingDossier && v.Phase <= LinePhase.MeetingVerdict)
                title = F("header.meeting", v.Meeting.Index + 1) + " · " + title;
            if (v.Phase == LinePhase.FinalVote && v.Final != null)
                title += " · " + F(v.Final.Revote ? "header.final_revote" : "header.final_round", v.Final.Round + 1, System.Math.Max(1, v.Final.FiringsTotal));
            return title;
        }
        public static string Hint(LinePhase p) { return T("hint." + p); }
        public static string Task(TaskKind k) { return T("task." + LineTasks.Id(k) + ".name"); }
        public static string Choice(TaskKind k, int choice) { return T("task." + LineTasks.Id(k) + ".choice." + choice); }
        public static string Failure(LineFailure f) { return Loc.Has("v5.fail." + f) ? T("fail." + f) : f.ToString(); }
        public static string Signed(int d) { return d > 0 ? "+" + d : d.ToString(); }

        /// <summary>"Haftanın gündemi" (v5.3): one task's level with this match's number ("KASA DOLU: 5 prim"); null with
        /// fixed stakes (every task plays at the normal level: nothing to announce).</summary>
        public static string Stake(LineStakesView s, TaskKind task)
        {
            if (s == null || s.Scheme != LineStakes.Permutation) return null;
            int amount = task == TaskKind.ClientBonus ? s.BonusPot : task == TaskKind.PettyCash ? s.CashTake : s.ShiftSkipPay;
            return F("stakes." + LineTasks.Id(task) + "." + LineStakes.Id(s.Level(task)), amount);
        }

        /// <summary>The week's agenda in one line, the biggest stake first ("KASA DOLU: 5 prim · Müşteri bonusu orta: 6 ·
        /// Nöbet primi düşük: +1"); null with fixed stakes.</summary>
        public static string Agenda(LineStakesView s)
        {
            if (s == null || s.Scheme != LineStakes.Permutation) return null;
            var sb = new StringBuilder();
            for (int level = LineStakes.Big; level >= LineStakes.Small; level--)
                foreach (var task in AgendaTasks)
                {
                    if (s.Level(task) != level) continue;
                    if (sb.Length > 0) sb.Append(" · ");
                    sb.Append(Stake(s, task));
                }
            return sb.ToString();
        }

        static readonly TaskKind[] AgendaTasks = { TaskKind.ClientBonus, TaskKind.PettyCash, TaskKind.WeekendShift };

        /// <summary>What became of a file ("açıldı · Toplantı: dosya", "imha edildi", "süresi doldu", …).</summary>
        public static string FileState(LineFileView f)
        {
            return f.State == LineFileState.Opened ? F("files.state.Opened", Phase(f.Phase)) : T("files.state." + f.State);
        }

        /// <summary>The public counter ("Açılmamış dosya: N"); null when it is not public or the match has no file yet.</summary>
        public static string FilesCounter(LineView v)
        {
            var fs = v != null ? v.Files : null;
            if (fs == null || fs.Held < 0 || fs.Held + fs.Opened.Count + fs.Shredded + fs.Expired == 0) return null;
            return F("files.count", fs.Held);
        }

        /// <summary>The task's rules with the match's numbers (a file on whoever takes, shift cleaning and the unstaffed
        /// shift's automatic suç).</summary>
        public static string TaskRules(LineTaskView t, LineConfig c, int players)
        {
            switch (t.Kind)
            {
                case TaskKind.ClientBonus:
                    return t.Group.Count >= 3
                        ? F("task.client_bonus.rules_trio", c.BonusTrioNone, c.BonusTrioTaker, c.BonusTrioOthers, c.BonusTrioMany, c.InvestigationGuiltyBlame)
                        : F("task.client_bonus.rules_pair", c.BonusPairShare, c.BonusPairTaker, c.BonusPairSharer, c.BonusPairBothTake, c.InvestigationGuiltyBlame);
                case TaskKind.PettyCash:
                    return F("task.petty_cash.rules", c.CashTake, c.CashCaughtBlame, c.CashWhistleBlame);
                case TaskKind.WeekendShift:
                    return F("task.weekend_shift.rules", t.Needed >= 0 ? t.Needed : LineConfig.ShiftNeeded(players), c.ShiftSkipPay, c.ShiftVolunteerClean, c.ShiftFailSkipBlame);
                default:
                    return "";
            }
        }

        /// <summary>The Weekend Shift's choice card: skip pay {0}, needed {1}, the volunteers' cleaning {2}, the skippers'
        /// suç when it is short {3}.</summary>
        public static string ShiftFocus(LineConfig c, int needed)
        {
            return F("focus.task.weekend_shift", c.ShiftSkipPay, needed, c.ShiftVolunteerClean, c.ShiftFailSkipBlame);
        }

        /// <summary>The Weekend Shift came up short (v5.2: every consequence lands at its reveal): "Nöbet tutulmadı: tutmayan N
        /// kişiye +2 suç yazıldı", or null when the shift was staffed / the old flow waits for meeting 2.</summary>
        public static string ShiftShort(LineTaskView t, LineConfig c, int players)
        {
            if (t == null || t.Kind != TaskKind.WeekendShift || !t.Revealed || c.ShiftMeeting || c.ShiftFailSkipBlame <= 0) return null;
            int needed = t.Needed >= 0 ? t.Needed : LineConfig.ShiftNeeded(players);
            if (t.Roster.Count >= needed) return null;
            return F("task.shift_short", System.Math.Max(0, players - t.Roster.Count), c.ShiftFailSkipBlame);
        }

        /// <summary>A meeting without a vote: meeting 2 of a short shift (the skippers' suç is automatic) or nothing to vote on.</summary>
        public static string NoVote(LineMeetingView mt, LineConfig c)
        {
            if (mt != null && mt.Index == 1 && mt.FailedTasks.Contains(TaskKind.WeekendShift) && (c.Legacy == null || !c.Legacy.ShiftFailVote) && c.ShiftFailSkipBlame > 0)
                return F("meeting.shift_auto", c.ShiftFailSkipBlame);
            return T("meeting.no_vote");
        }

        /// <summary>One public dossier / incident line (typed entry -> sentence).</summary>
        public static string Dossier(LineDossierEntry e)
        {
            switch (e.Kind)
            {
                case DossierKind.TaskFailed: return F("dossier.task_failed", Task(e.Task));
                case DossierKind.BonusTakers: return F("dossier.bonus_takers", e.N1);
                case DossierKind.CashCaught: return F("dossier.cash_caught", Name(e.Player), e.N1);
                // (a whistle / a guilty investigation of the legacy rules reads like the file it became)
                case DossierKind.Whistle: return F("dossier.file_opened", Name(e.Player), Name(e.Other), Task(TaskKind.PettyCash), e.N1);
                case DossierKind.FileOpened: return F("dossier.file_opened", Name(e.Player), Name(e.Other), Task(e.Task), e.N1);
                case DossierKind.CashMissing: return F("dossier.cash_missing", e.N1, e.N2);
                case DossierKind.ShiftRoster: return F("dossier.shift_roster", e.N1, e.N2);
                case DossierKind.ShiftSkipBlame: return F("dossier.shift_skip_blame", e.N1, e.N2);
                case DossierKind.Investigation:
                    return e.N1 == 1 ? F("dossier.file_opened", Name(e.Player), Name(e.Other), Task(TaskKind.ClientBonus), LineClient.I != null ? LineClient.I.Config.InvestigationGuiltyBlame : 0)
                        : e.Kind.ToString();
                case DossierKind.Leak: return F("dossier.leak", Name(e.Player), Name(e.N1), Name(e.N2));
                default: return e.Kind.ToString();
            }
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
                // v5.3 files: an opening is public; a shredding is told to its holder and its target, everyone else gets
                // the anonymous notice (so does everyone when files expire)
                case LineEventType.FileOpened:
                    if (e.Target == me) return F("event.file_opened_me", Name(e.Actor), Task((TaskKind)e.A), e.B);
                    return F("event.file_opened", Name(e.Actor), Name(e.Target), Task((TaskKind)e.A), e.B);
                case LineEventType.FileShredded:
                    return e.Target == me ? F("event.file_shredded_me", Name(e.Actor)) : F("event.file_shredded_mine", Name(e.Target));
                case LineEventType.FileNotice:
                    return e.A == (int)LineFileState.Shredded ? T("event.file_notice_shredded") : F("event.file_notice_expired", e.B);
                case LineEventType.Leaked: return F("event.leaked", Name(e.Actor), Name(e.A), Name(e.B));
                case LineEventType.BlameAdded:
                    // (the suç of an opened file is announced by its FileOpened line: who opened it on whom)
                    if (e.A > 0 && ((BlameReason)e.B == BlameReason.Whistled || (BlameReason)e.B == BlameReason.InvestigationGuilty)
                        && LineClient.I != null && LineClient.I.Config.FilesOn()) return null;
                    // a volunteer's cleaning is a negative change
                    return e.A < 0 ? F("event.blame_down", Name(e.Target), -e.A, T("blame." + (BlameReason)e.B))
                        : F("event.blame", Name(e.Target), e.A, T("blame." + (BlameReason)e.B));
                case LineEventType.CrisisResolved: return e.Target >= 0 ? F("event.crisis_one", Name(e.Target), e.A) : F("event.crisis_tie", e.B, e.A);
                case LineEventType.Fired: return F("event.fired", Name(e.Target), e.A);
                case LineEventType.DeclarationsRevealed: return F("event.declarations", e.A, e.B);
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
                // v5 Yaramazlıklar (the meeting room shows them in the world; the fallback screen lists them)
                case LineEventType.Prank:
                    return e.Target >= 0 ? F("prank.log.target", e.Actor >= 0 ? Name(e.Actor) : T("prank.someone"), Name(e.Target), T("prank." + LinePranks.Id((PrankKind)e.A) + ".did"))
                        : F("prank.log.alone", e.Actor >= 0 ? Name(e.Actor) : T("prank.someone"), T("prank." + LinePranks.Id((PrankKind)e.A) + ".did"));
                case LineEventType.Tutanak: return F("prank.tutanak.who", Name(e.Target), e.B);
                case LineEventType.Patience: return null;
                default: return null;
            }
        }
    }
}
