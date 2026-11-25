using System;
using System.Collections.Generic;
using System.Text;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.PlayKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Local selections of a play surface (who the transfer goes to and how much, who the next text goes to) and the
    /// callbacks the shared blocks need. The meeting screen and the office computer each own one.
    /// </summary>
    public sealed class PlayState
    {
        public int TransferTarget = LineIds.None, TransferAmount = 1;
        /// <summary>-2 = nobody chosen, -1 = meeting chat (everyone), else a player id.</summary>
        public int MsgTarget = -2;
        /// <summary>v5.2: the prim the fallback screen's declaration row shows (-1 = start from my real prim).</summary>
        public int DeclareAmount = -1;
        /// <summary>v5.3 "Dosyalarım": the file whose open (1) / shred (2) is waiting for its confirmation (0 = none).</summary>
        public int FileTarget = LineIds.None, FileAction;
        public TaskKind FileTask;
        public Action Dirty;
        public Action<string> Toast;
        public Action FocusMessageField;

        public void ResetWeek() { TransferTarget = LineIds.None; TransferAmount = 1; MsgTarget = -2; DeclareAmount = -1; AskFile(null, 0); }

        /// <summary>Asks to confirm opening (1) / shredding (2) a file; null or 0 takes the question back.</summary>
        public void AskFile(LineFileView f, int action)
        {
            FileTarget = f != null && action != 0 ? f.Target : LineIds.None;
            FileTask = f != null ? f.Task : TaskKind.None;
            FileAction = f != null ? action : 0;
            MarkDirty();
        }
        public void MarkDirty() { Dirty?.Invoke(); }
    }

    /// <summary>
    /// Content blocks shared by the meeting screen (<see cref="PlayUi"/>) and the office computer
    /// (<see cref="OfficeComputer"/>): the task card (instruction, group, choices, default, reveal), "Dosyalarım" (the
    /// files I hold: open / shred with a confirmation), the task rules with the match's numbers, the intro with the
    /// week's agenda, the crisis-calls card, the transfer panel, messages and compose targets, and the call log. Every
    /// rule text comes from PlayText / the v5 tables, never duplicated.
    /// </summary>
    public static class PlayBlocks
    {
        // ---- files ("Dosya", v5.3)

        /// <summary>A file I hold that can still be opened or shredded (on <paramref name="target"/> / of
        /// <paramref name="task"/> when given), or null.</summary>
        public static LineFileView HeldFile(LineView v, int me, int target = -1, TaskKind task = TaskKind.None)
        {
            if (v == null || !v.Features.FilesOpen || v.Mine.Files == null) return null;
            foreach (var f in v.Mine.Files)
                if (f.Holder == me && f.State == LineFileState.Held && (target < 0 || f.Target == target) && (task == TaskKind.None || f.Task == task)) return f;
            return null;
        }

        public static int HeldFiles(LineView v, int me)
        {
            int n = 0;
            if (v != null && v.Features.FilesOpen && v.Mine.Files != null)
                foreach (var f in v.Mine.Files) if (f.Holder == me && f.State == LineFileState.Held) n++;
            return n;
        }

        /// <summary>My file / the file on me of a task, whatever became of it (mine: I am the holder), or null.</summary>
        public static LineFileView FileOf(LineView v, int me, TaskKind task, bool mine)
        {
            if (v == null || v.Mine.Files == null) return null;
            LineFileView best = null;
            foreach (var f in v.Mine.Files)
            {
                if (f.Task != task || (mine ? f.Holder != me : f.Target != me)) continue;
                // (a trio's thief has two files on them: the opened one tells the story, then the shredded one)
                if (best == null || f.State == LineFileState.Opened || f.State == LineFileState.Shredded && best.State != LineFileState.Opened) best = f;
            }
            return best;
        }

        public static void OpenFile(LineFileView f) { Send(LineVerb.FileOpen, f.Target, (int)f.Task); }
        public static void ShredFile(LineFileView f) { Sfx.Play("shred", 0.7f); Send(LineVerb.FileShred, f.Target, (int)f.Task); }

        /// <summary>
        /// "Dosyalarım": every file I hold (target, task, what it costs the target) with Aç / İmha et and a confirmation
        /// for each, what became of the others, and the files colleagues hold on me ("Hakkında dosya var: …"). Returns
        /// Todo while a file of mine is still to be decided.
        /// </summary>
        public static int BuildFiles(VisualElement card, LineClient lc, LineView v, PlayState st)
        {
            int me = lc.MySlot;
            var files = v.Mine.Files ?? new List<LineFileView>();
            bool open = v.Features.FilesOpen;
            int held = 0, shown = 0;
            foreach (var f in files) if (f.Holder == me && f.State == LineFileState.Held) held++;
            if (held > 0 && open) Line(card, T("files.how"));
            foreach (var f in files)
            {
                if (f.Holder != me) continue;
                shown++;
                var file = f;
                string task = PlayText.Task(f.Task);
                if (f.State != LineFileState.Held || !open) { Line(card, F("files.mine_done", Name(f.Target), task, PlayText.FileState(f)), "pl-dim"); continue; }
                if (st != null && st.FileAction != 0 && st.FileTarget == f.Target && st.FileTask == f.Task)
                {
                    bool shred = st.FileAction == 2;
                    var box = Box("pl-confirm").AddTo(card);
                    Big(box, F(shred ? "files.confirm_shred" : "files.confirm_open", Name(f.Target), task, f.Blame), shred ? "pl-warn" : "pl-bad");
                    var yesNo = Row(box);
                    Btn(T(shred ? "files.yes_shred" : "files.yes_open"), () =>
                    {
                        if (shred) ShredFile(file); else OpenFile(file);
                        st.AskFile(null, 0);
                    }, "pl-danger", "pl-big").AddTo(yesNo);
                    Btn(T("files.no"), () => st.AskFile(null, 0), "pl-big").AddTo(yesNo);
                    continue;
                }
                var row = Box("pl-row", "pl-file").AddTo(card);
                Line(row, F("files.entry", Name(f.Target), task, f.Blame), "grow");
                Btn(T("files.open_btn"), () => { if (st != null) st.AskFile(file, 1); else OpenFile(file); }, "pl-danger").AddTo(row);
                Btn(T("files.shred_btn"), () => { if (st != null) st.AskFile(file, 2); else ShredFile(file); }).AddTo(row);
            }
            foreach (var f in files)
            {
                if (f.Target != me) continue;
                shown++;
                string task = PlayText.Task(f.Task);
                if (f.State == LineFileState.Held && open) Line(card, F("files.on_me", Name(f.Holder), task, f.Blame), "pl-warn");
                else Line(card, F("files.on_me_done", Name(f.Holder), task, PlayText.FileState(f)),
                    f.State == LineFileState.Opened ? "pl-bad" : f.State == LineFileState.Shredded ? "pl-good" : "pl-dim");
            }
            if (shown == 0) Line(card, T(open ? "files.none" : "files.none_closed"), "pl-dim");
            string counter = PlayText.FilesCounter(v);
            if (counter != null) Line(card, counter + (open ? " · " + T("files.window") : ""), "pl-dim");
            return held > 0 && open ? Todo : Info;
        }

        /// <summary>The week's agenda as chips (the big task stands out); nothing with fixed stakes.</summary>
        public static void AgendaChips(VisualElement p, LineView v)
        {
            var s = v != null ? v.Stakes : null;
            if (s == null || s.Scheme != LineStakes.Permutation) return;
            Sub(p, T("stakes.title"));
            var row = Wrap(p);
            foreach (var task in new[] { TaskKind.ClientBonus, TaskKind.PettyCash, TaskKind.WeekendShift }) StakeChip(row, s, task);
            Line(p, T("stakes.note"), "pl-dim");
        }

        /// <summary>One task's level chip ("KASA DOLU: 5 prim"); nothing with fixed stakes.</summary>
        public static void StakeChip(VisualElement p, LineStakesView s, TaskKind task)
        {
            string text = PlayText.Stake(s, task);
            if (text == null) return;
            int level = s.Level(task);
            Bold(text, "pl-chip", "pl-stake", level == LineStakes.Big ? "pl-chip-warn" : level == LineStakes.Small ? "pl-chip-dim" : null).AddTo(p);
        }

        // ---- task (TaskPlay: choose; TaskReveal / Break: results and the files of the task)

        /// <summary>The task card. <paramref name="goTo"/> (office computer): Petty Cash and Weekend Shift are played on
        /// their desk objects, so instead of the two choices the card says where and offers one button that turns the
        /// view to the object (docs/V5_INTERACTION.md §1.3, §2).</summary>
        public static int FocusTask(VisualElement card, LineClient lc, LineView v, Action<TaskKind> goTo = null)
        {
            var t = v.Task;
            if (t == null) { Big(card, T("hint.TaskPlay")); return Info; }
            int me = lc.MySlot;
            var c = lc.Config;
            StakeChip(card, v.Stakes, t.Kind); // this week's level of the task; the numbers below are the match's
            switch (t.Kind)
            {
                case TaskKind.ClientBonus: Big(card, F("focus.task.client_bonus", t.Group.Count >= 3 ? c.BonusTrioNone : c.BonusPairShare, c.BonusPairTaker, c.InvestigationGuiltyBlame)); break;
                case TaskKind.PettyCash: Big(card, F("focus.task.petty_cash", c.CashTake, c.CashWhistleBlame, c.CashCaughtBlame)); break;
                case TaskKind.WeekendShift: Big(card, PlayText.ShiftFocus(c, t.Needed >= 0 ? t.Needed : LineConfig.ShiftNeeded(v.Players.Count))); break;
                default: Big(card, T("hint.TaskPlay")); break;
            }
            if (t.Kind == TaskKind.WeekendShift) Line(card, F("task.everyone", t.Needed >= 0 ? t.Needed : LineConfig.ShiftNeeded(v.Players.Count)));
            else Line(card, F("task.group", PlayText.Names(t.Group.FindAll(id => id != me))));
            if (goTo != null && (t.Kind == TaskKind.PettyCash || t.Kind == TaskKind.WeekendShift))
            {
                var kind = t.Kind;
                Line(card, t.Submitted ? F("task.submitted", PlayText.Choice(t.Kind, t.MyChoice)) : F("task.default", PlayText.Choice(t.Kind, 0)), t.Submitted ? "pl-good" : "pl-warn");
                Line(card, T("office.task_where." + LineTasks.Id(kind)), "pl-where");
                Btn(T("office.goto." + LineTasks.Id(kind)), () => goTo(kind), "pl-primary", "pl-choice").AddTo(card);
                return t.Submitted ? Done : Todo;
            }
            var row = Wrap(card);
            for (int choice = 0; choice <= 1; choice++)
            {
                int ch = choice;
                bool on = t.Submitted && t.MyChoice == ch;
                Btn(PlayText.Choice(t.Kind, ch), () => Send(LineVerb.SubmitTask, a: ch), "pl-choice", on ? "pl-on" : null).AddTo(row);
            }
            Line(card, t.Submitted ? F("task.submitted", PlayText.Choice(t.Kind, t.MyChoice)) : F("task.default", PlayText.Choice(t.Kind, 0)), t.Submitted ? "pl-good" : "pl-warn");
            return t.Submitted ? Done : Todo;
        }

        /// <summary>Reveal / break card: a witness's Petty Cash file can be opened right here (with <paramref name="goTo"/>,
        /// the office, by the cash box's button); every file is in "Dosyalarım" until the crisis calls end.</summary>
        public static int FocusReveal(VisualElement card, LineClient lc, LineView v, Action<TaskKind> goTo = null)
        {
            var t = v.Task;
            if (t == null) { Big(card, PlayText.Hint(v.Phase)); return Info; }
            int me = lc.MySlot;
            var m = v.Mine;
            var c = lc.Config;
            int state = Info;
            var cashFile = t.Kind == TaskKind.PettyCash ? HeldFile(v, me, -1, TaskKind.PettyCash) : null;
            if (cashFile != null)
            {
                var file = cashFile;
                Big(card, F("break.witness", Name(file.Target), file.Blame), "pl-bad");
                if (goTo == null) Btn(F("break.whistle_btn", Name(file.Target)), () => OpenFile(file), "pl-danger", "pl-choice").AddTo(card);
                else
                {
                    Line(card, T("office.task_where.whistle"), "pl-where");
                    Btn(T("office.goto.whistle"), () => goTo(TaskKind.PettyCash), "pl-danger", "pl-choice").AddTo(card);
                }
                Line(card, T("focus.witness_or"), "pl-dim");
                state = Todo;
            }
            else if (v.Phase == LinePhase.Break && SoleTaker(t, me) && FileOnMeHeld(v, me, TaskKind.PettyCash)) Big(card, F("focus.thief", c.CashWhistleBlame), "pl-warn");
            else if (v.Phase == LinePhase.Break) Big(card, T(t.Number == 0 ? "break.next_task" : "break.meeting")); // breaks follow tasks 1 and 2 only
            else Big(card, T("hint.TaskReveal"));
            if (!t.Revealed) { Line(card, T("task.not_revealed"), "pl-dim"); return state; }
            var row = Wrap(card);
            foreach (var ch in t.Choices)
            {
                bool bad = ch.Choice == LineChoice.Take && t.Kind != TaskKind.WeekendShift;
                Bold(Name(ch.Player) + ": " + PlayText.Choice(t.Kind, ch.Choice), "pl-chip", bad ? "pl-chip-bad" : null).AddTo(row);
            }
            if (t.MyPayout >= 0) Bold(F("task.my_payout", t.MyPayout), "pl-good", "pl-sub").AddTo(card);
            // v5.2: a short shift blames its skippers right here (there is no meeting 2)
            string shortShift = PlayText.ShiftShort(t, c, v.Players.Count);
            if (shortShift != null) Line(card, shortShift, "pl-bad");
            return state;
        }

        /// <summary>A colleague still holds a file of this task on me.</summary>
        public static bool FileOnMeHeld(LineView v, int me, TaskKind task)
        {
            if (v == null || !v.Features.FilesOpen || v.Mine.Files == null) return false;
            foreach (var f in v.Mine.Files) if (f.Target == me && f.Task == task && f.State == LineFileState.Held) return true;
            return false;
        }

        /// <summary>Petty Cash: I am the only taker in my group (the witnesses hold a file on me).</summary>
        public static bool SoleTaker(LineTaskView t, int me)
        {
            if (t.Kind != TaskKind.PettyCash || !t.Revealed) return false;
            int takers = 0;
            bool mine = false;
            foreach (var ch in t.Choices)
                if (ch.Choice == LineChoice.Take) { takers++; if (ch.Player == me) mine = true; }
            return mine && takers == 1;
        }

        /// <summary>The task's rules with the host's numbers, then what became public after the reveal.</summary>
        public static void BuildTask(VisualElement p, LineClient lc, LineView v)
        {
            var t = v.Task;
            if (t == null) return;
            Head(p, F("task.title", t.Number + 1, PlayText.Task(t.Kind)));
            Sub(p, T("task.rules_title"));
            Line(p, PlayText.TaskRules(t, lc.Config, v.Players.Count));
            if (!t.Revealed) return;
            if (t.Takers >= 0) Line(p, F("task.public_takers", t.Takers));
            if (t.Kind == TaskKind.WeekendShift) Line(p, F("task.public_roster", PlayText.Names(t.Roster), t.Roster.Count, t.Needed));
        }

        public static void BuildIntro(VisualElement p, LineClient lc, LineView v)
        {
            Head(p, T("intro.title"));
            var c = lc.Config;
            Line(p, F("intro.goal", c.StartPrim));
            Line(p, T("intro.flow"));
            Line(p, F("intro.final", c.CandidatesFor(v.Players.Count), c.FiringsFor(v.Players.Count)));
            Line(p, T("intro.phone"));
            if (v.Features.RecordingsEnabled) Line(p, F("intro.recorder", c.RecorderUses));
            Line(p, F("intro.transfer", c.TransferMax));
        }

        /// <summary>Crisis calls: what to bargain about and how many become candidates.</summary>
        public static void CrisisCalls(VisualElement card, LineClient lc, LineView v, string howKey)
        {
            Big(card, F("focus.crisis_calls", lc.Config.CrisisBlame));
            Line(card, F("crisis.candidates_rule", lc.Config.CandidatesFor(v.Players.Count)));
            if (!string.IsNullOrEmpty(howKey)) Line(card, T(howKey), "pl-dim");
        }

        /// <summary>The how-to page that explains this phase (-1 = none).</summary>
        public static int HowToPage(LineView v)
        {
            switch (v.Phase)
            {
                case LinePhase.Intro: return HowToPlayScreen.PageGoal;
                case LinePhase.TaskPlay:
                case LinePhase.TaskReveal:
                case LinePhase.Break:
                    if (v.Task == null) return HowToPlayScreen.PageFlow;
                    return v.Task.Kind == TaskKind.ClientBonus ? HowToPlayScreen.PageBonus : v.Task.Kind == TaskKind.PettyCash ? HowToPlayScreen.PageCash : HowToPlayScreen.PageShift;
                case LinePhase.MeetingDossier:
                case LinePhase.MeetingDiscussion: return v.Day == 0 ? HowToPlayScreen.PageFiles : HowToPlayScreen.PageBlame;
                case LinePhase.MeetingVote:
                case LinePhase.MeetingVerdict: return HowToPlayScreen.PageBlame;
                case LinePhase.CrisisCalls:
                case LinePhase.CrisisVote:
                case LinePhase.CrisisVerdict: return HowToPlayScreen.PageCrisis;
                case LinePhase.FinalDiscussion:
                case LinePhase.FinalDefense:
                case LinePhase.FinalVote:
                case LinePhase.FinalVerdict: return HowToPlayScreen.PageFinal;
                default: return -1;
            }
        }

        // ---- meeting, final and results details (the meeting room UI and the fallback play screen)

        public static int LeakableCount(LineView v)
        {
            if (!v.Features.LeakOpen || !v.Features.RecordingsEnabled) return 0;
            int n = 0;
            foreach (var r in v.Mine.Recorded) if (!r.Leaked) n++;
            return n;
        }

        public static void LeakButtons(VisualElement row, LineView v)
        {
            if (LeakableCount(v) == 0) return;
            foreach (var r in v.Mine.Recorded)
            {
                if (r.Leaked) continue;
                int callId = r.CallId;
                Btn(F("focus.leak_btn", Name(r.Other)), () => Send(LineVerb.Leak, a: callId), "pl-danger", "pl-big").AddTo(row);
            }
        }

        /// <summary>Desk reveal: who was fired, how much was on the desk, who voted for them and what each got, what burned.</summary>
        public static void BuildFiringCard(VisualElement p, LineFiringView f, int me, bool big)
        {
            var card = Box("pl-fire").AddTo(p);
            if (!big) card.AddToClassList("pl-fire-small");
            Title(F("fire.title", Name(f.Player)), "pl-fire-title").AddTo(card);
            Bold(F("fire.desk", f.Desk), "pl-fire-desk").AddTo(card);
            // v5.2: what they had declared, against their prim when the declarations were revealed
            if (f.Declared >= 0)
            {
                var claim = Row(card);
                Line(claim, StoryText.Claim(f.Declared));
                Bold(StoryText.Stamp(f), "pl-chip", StoryText.Truthful(f) ? "pl-chip-good" : "pl-chip-bad").AddTo(claim);
                string then = StoryText.Then(f);
                if (then != null) Line(card, then, "pl-dim");
            }
            Line(card, F("fire.decision." + f.Decision, f.Votes));
            if (f.Recipients.Count > 0)
            {
                Line(card, F("fire.voters", f.Share));
                var row = Wrap(card);
                foreach (int id in f.Recipients) Bold(F("fire.chip", Name(id), f.Share), "pl-chip", id == me ? "pl-chip-me" : null).AddTo(row);
                if (f.Removed > 0) Line(card, F("fire.burned", f.Removed), "pl-dim");
            }
            else Line(card, F("fire.nobody", f.Desk), "pl-dim");
            if (me == f.Player) Line(card, T("fire.you_fired"), "pl-bad");
            else if (f.Recipients.Contains(me)) Line(card, F("fire.you_got", f.Share), "pl-good");
            else if (me >= 0 && big) Line(card, T("fire.you_none"), "pl-dim");
        }

        /// <summary>Winners first, then the rest by prim, fired players last.</summary>
        public static List<LineWeekPlayerResult> SortedResults(LineWeekResult r)
        {
            var players = new List<LineWeekPlayerResult>(r.Players);
            players.Sort((a, b) =>
            {
                if (a.Fired != b.Fired) return a.Fired ? 1 : -1;
                if (a.Winner != b.Winner) return a.Winner ? -1 : 1;
                return b.Prim != a.Prim ? b.Prim.CompareTo(a.Prim) : a.Player.CompareTo(b.Player);
            });
            return players;
        }

        public static void BuildMeetingInfo(VisualElement p, LineClient lc, LineView v, double now)
        {
            var mt = v.Meeting;
            var m = v.Mine;
            if (mt.Index < 2)
            {
                Head(p, F("meeting.title", mt.Index + 1));
                if (v.Phase != LinePhase.MeetingDossier && v.Phase != LinePhase.MeetingDiscussion)
                    Line(p, mt.FailedTasks.Count > 0 ? F("meeting.failed", JoinTasks(mt.FailedTasks)) : T("meeting.no_failures"), mt.FailedTasks.Count > 0 ? "pl-bad" : "pl-good");
                if (mt.VoteableCount >= 0) Line(p, mt.VoteableCount > 0 ? F("meeting.voteable", mt.VoteableCount) : PlayText.NoVote(mt, lc.Config));
                if (mt.Roster.Count > 0) Line(p, F("meeting.roster", PlayText.Names(mt.Roster)));
            }
            if (mt.Dossier.Count > 0)
            {
                Sub(p, T(mt.Index < 2 ? "meeting.dossier" : "meeting.final_dossier"));
                foreach (var e in mt.Dossier) Line(p, "• " + PlayText.Dossier(e), e.Kind == DossierKind.FileOpened || e.Kind == DossierKind.Leak ? "pl-warn" : null);
            }
            if (mt.BlameGained.Exists(g => g.Amount > 0) && v.Phase != LinePhase.MeetingVerdict)
            {
                Sub(p, T("meeting.blame_gained"));
                foreach (var g in mt.BlameGained) if (g.Amount > 0) Line(p, Name(g.Player) + ": " + F("meeting.blame_plus", g.Amount));
            }
            // leaked recordings, with a live countdown / state
            if (mt.Recordings.Count > 0)
            {
                Sub(p, T("leak.list"));
                foreach (var r in mt.Recordings)
                {
                    var pick = r;
                    string head = F("leak.line", r.Index + 1, Name(r.Leaker), Name(r.Caller), Name(r.Callee)) + " · ";
                    Live(p, "leak" + r.Index + ":" + r.PlayAt + ":" + r.Played + ":" + r.Empty, now, t => head + LeakState(pick, t));
                }
            }
            // my recordings (leak buttons are in the focus card)
            if (v.Features.RecordingsEnabled && m.Recorded.Count > 0)
            {
                Sub(p, T(v.Features.LeakOpen ? "leak.mine" : "leak.mine_closed"));
                foreach (var r in m.Recorded)
                {
                    string other = Name(r.Other), state = r.Leaked ? " · " + T("leak.leaked") : "";
                    double start = r.Start;
                    Live(p, "rec" + r.CallId + ":" + r.Leaked, now, t => F("leak.recorded", other, UiKit.Clock(Math.Max(0, t - start))) + state, r.Leaked ? "pl-dim" : null);
                }
            }
            else if (v.Features.LeakOpen && v.Features.RecordingsEnabled) Line(p, T("leak.none"), "pl-dim");
            string counter = PlayText.FilesCounter(v);
            if (counter != null) Line(p, counter, "pl-dim");
        }

        /// <summary>v5.2, the fallback screen (the meeting room has its own card, <see cref="MeetingDeclare"/>): a candidate's
        /// prim declaration as a row of buttons; true while a declaration is still expected of me.</summary>
        public static bool DeclareRow(VisualElement card, LineClient lc, LineView v, PlayState st)
        {
            var fv = v.Final;
            if (fv == null || fv.DeclareUntil <= 0) return false;
            if (!v.Features.DeclareOpen)
            {
                if (fv.MyDeclaration >= 0 && !fv.DeclarationsRevealed) Line(card, F("declare.mine", fv.MyDeclaration), "pl-private");
                return false;
            }
            int max = Math.Max(0, lc.Config.DeclareMax);
            if (st.DeclareAmount < 0) st.DeclareAmount = Math.Min(max, v.Mine.Prim);
            Sub(card, T("declare.card.title"));
            Line(card, T("declare.card.info"));
            var row = Row(card);
            foreach (int step in new[] { -5, -1, 1, 5 })
            {
                int d = step;
                if (d == 1) Bold(F("declare.amount", st.DeclareAmount), "pl-chip", st.DeclareAmount != v.Mine.Prim ? "pl-chip-warn" : null).AddTo(row);
                Btn((d > 0 ? "+" : "−") + Math.Abs(d), () => { st.DeclareAmount = Math.Max(0, Math.Min(max, st.DeclareAmount + d)); st.MarkDirty(); }).AddTo(row);
            }
            Btn(T("declare.card.ok"), () => Send(LineVerb.Declare, a: st.DeclareAmount), "pl-primary").AddTo(row);
            double until = fv.DeclareUntil;
            Live(card, "declare-left:" + until.ToString("0.0"), lc.Now, t => F("declare.card.timer", UiKit.Clock(Math.Max(0, until - t))), "pl-text", "pl-warn");
            return true;
        }

        public static string LeakState(LineRecordingPick r, double now)
        {
            if (r.Played) return T("leak.played");
            if (r.Empty) return T("leak.empty");
            if (now < r.PlayAt) return F("leak.queued", Math.Max(1, (int)Math.Ceiling(r.PlayAt - now)));
            if (now < r.PlayAt + Math.Max(1f, r.Seconds) + 1) return T("leak.playing");
            return T("leak.played");
        }

        public static string JoinTasks(List<TaskKind> tasks)
        {
            var sb = new StringBuilder();
            foreach (var t in tasks) { if (sb.Length > 0) sb.Append(", "); sb.Append(PlayText.Task(t)); }
            return sb.ToString();
        }

        public static void BuildFinalInfo(VisualElement p, LineClient lc, LineView v)
        {
            var fv = v.Final;
            if (fv.Crisis.Count > 0 && v.Phase != LinePhase.CrisisVerdict)
            {
                Sub(p, T("crisis.result"));
                foreach (var g in fv.Crisis) Line(p, Name(g.Player) + ": " + F("meeting.blame_plus", g.Amount));
            }
            if (fv.Candidates.Count > 0)
            {
                Sub(p, F("final.candidates", fv.FiringsTotal));
                foreach (int id in fv.DefenseOrder)
                {
                    var pl = lc.Player(id);
                    string line = Name(id) + " · " + F("players.blame", pl != null ? pl.Blame : 0);
                    if (pl != null && pl.Fired) line += " · " + T("players.fired");
                    Line(p, line, pl != null && pl.Fired ? "pl-dim" : null);
                }
                Line(p, T("focus.sealed_note"), "pl-dim");
            }
            // v5.2 prim declarations: public once revealed (sealed before: only how long is left)
            if (fv.DeclarationsRevealed)
            {
                Sub(p, T("declare.title"));
                foreach (var d in fv.Declarations) Line(p, StoryText.Line(d), d.Amount < 0 ? "pl-dim" : null);
                Line(p, T("declare.note"), "pl-dim");
            }
            else if (fv.DeclareUntil > 0)
            {
                Sub(p, T("declare.title"));
                if (fv.MyDeclaration >= 0) Line(p, F("declare.mine", fv.MyDeclaration), "pl-private");
                double until = fv.DeclareUntil;
                Live(p, "declare:" + until.ToString("0.0"), lc.Now, t => F("declare.sealed", UiKit.Clock(Math.Max(0, until - t))), "pl-text", "pl-warn");
            }
            // earlier firings (the latest one is the focus card in FinalVerdict)
            int shown = v.Phase == LinePhase.FinalVerdict ? fv.Firings.Count - 1 : fv.Firings.Count;
            if (shown > 0)
            {
                Sub(p, T("final.firings"));
                for (int i = 0; i < shown; i++) BuildFiringCard(p, fv.Firings[i], lc.MySlot, false);
            }
        }

        /// <summary>Every vote, grouped (meeting 1, meeting 2, crisis, firing 1, …), one line per voter.</summary>
        public static void BuildBallots(VisualElement p, LineWeekResult r)
        {
            if (r.Ballots.Count == 0) { Line(p, T("common.none"), "pl-dim"); return; }
            string group = null;
            int voter = int.MinValue;
            var sb = new StringBuilder();
            Action flush = () => { if (sb.Length > 0) { Line(p, sb.ToString()); sb.Length = 0; } };
            foreach (var b in r.Ballots)
            {
                string g = b.Kind == BallotKind.Meeting ? F("results.ballots_meeting", b.Round + 1)
                    : b.Kind == BallotKind.Crisis ? T("results.ballots_crisis")
                    : F(b.Revote ? "results.ballots_final_revote" : "results.ballots_final", b.Round + 1);
                if (g != group) { flush(); group = g; voter = int.MinValue; Sub(p, g); }
                string target = b.SelfAssigned ? Name(b.Target) + " " + T("results.self_assigned") : b.Target >= 0 ? Name(b.Target) : T("results.abstain");
                if (b.Voter != voter) { flush(); voter = b.Voter; sb.Append(Name(b.Voter)).Append(" → ").Append(target); }
                else sb.Append(", ").Append(target);
            }
            flush();
        }

        // ---- transfers

        /// <summary>Send prim (sealed desks and fired players cannot) and my own transfer log.</summary>
        public static void BuildTransfer(VisualElement p, LineClient lc, LineView v, PlayState st, bool head = true)
        {
            var f = v.Features;
            var m = v.Mine;
            int me = lc.MySlot;
            if (!f.TransfersOpen && m.Transfers.Count == 0) { if (!head) Line(p, T("office.transfer_closed"), "pl-dim"); return; }
            if (head) Head(p, T("transfer.title"));
            var meP = lc.Player(me);
            if (f.TransfersOpen)
            {
                if (meP != null && meP.Fired) Line(p, T("transfer.fired"), "pl-dim");
                else if (meP != null && meP.Candidate)
                {
                    // sealed desk (docs/V5_ENGINE_SPEC.md §18): sending is disabled, receiving still works
                    Line(p, T("transfer.sealed"), "pl-bad");
                    Line(p, T("transfer.sealed_receive"), "pl-dim");
                    Btn(T("transfer.sealed_btn"), null, "pl-primary").AddTo(p).SetEnabled(false);
                }
                else
                {
                    Line(p, F("transfer.rules", lc.Config.TransferMax), "pl-dim");
                    if (v.Final != null && v.Final.Candidates.Count > 0) Line(p, T("transfer.candidates_note"), "pl-warn");
                    var targets = Wrap(p);
                    foreach (var pl in v.Players)
                    {
                        if (pl.Id == me) continue;
                        int id = pl.Id;
                        Btn(pl.Name, () => { st.TransferTarget = id; st.MarkDirty(); }, st.TransferTarget == id ? "pl-on" : null).AddTo(targets);
                    }
                    int max = Math.Max(1, Math.Min(lc.Config.TransferMax, m.Prim));
                    st.TransferAmount = Mathf.Clamp(st.TransferAmount, 1, max);
                    var row = Row(p);
                    Btn("−", () => { st.TransferAmount = Math.Max(1, st.TransferAmount - 1); st.MarkDirty(); }).AddTo(row);
                    Bold(F("transfer.amount", st.TransferAmount), "pl-amount").AddTo(row);
                    Btn("+", () =>
                    {
                        var cur = LineClient.I != null ? LineClient.I.View : null;
                        int top = Math.Max(1, Math.Min(lc.Config.TransferMax, cur != null ? cur.Mine.Prim : 1));
                        st.TransferAmount = Math.Min(top, st.TransferAmount + 1);
                        st.MarkDirty();
                    }).AddTo(row);
                    var send = Btn(st.TransferTarget >= 0 ? F("transfer.send", st.TransferAmount, Name(st.TransferTarget)) : T("transfer.pick"), () =>
                    {
                        if (st.TransferTarget >= 0) Send(LineVerb.Transfer, st.TransferTarget, st.TransferAmount);
                    }, "pl-primary").AddTo(p);
                    send.SetEnabled(st.TransferTarget >= 0 && m.Prim > 0);
                }
            }
            if (m.Transfers.Count > 0)
            {
                Sub(p, T("transfer.log"));
                foreach (var t in m.Transfers)
                    Line(p, t.From == me ? F("transfer.out", Name(t.To), t.Amount) : F("transfer.in", Name(t.From), t.Amount), t.From == me ? "pl-bad" : "pl-good");
            }
        }

        // ---- messages

        public static void BuildMessages(VisualElement p, LineClient lc, LineView v, bool head = true, int keep = 30)
        {
            if (head) Head(p, T("msg.title"));
            var msgs = lc.Messages;
            if (msgs.Count == 0) Line(p, T("msg.empty"), "pl-dim");
            int me = lc.MySlot;
            for (int i = Math.Max(0, msgs.Count - keep); i < msgs.Count; i++)
            {
                var msg = msgs[i];
                string who;
                if (msg.Kind == MessageKind.MeetingChat) who = F("msg.chat_from", Name(msg.From));
                else if (msg.Kind == MessageKind.System) who = T("msg.system");
                else if (msg.From == me) who = F("msg.text_to", Name(msg.To));
                else who = F("msg.text_from", Name(msg.From));
                Line(p, who + " " + BotSpeech.MessageText(msg), msg.Kind == MessageKind.MeetingChat ? null : "pl-private");
            }
        }

        /// <summary>Recipient buttons of the compose box; sets <paramref name="info"/> to the current recipient line.</summary>
        public static void BuildComposeTargets(VisualElement p, LineClient lc, LineView v, PlayState st, Label info)
        {
            var f = v.Features;
            var m = v.Mine;
            int me = lc.MySlot;
            int target = st.MsgTarget == -2 && f.MeetingChatOpen ? -1 : st.MsgTarget;
            if (info != null) info.SetText(target == -1 ? F("msg.to_all", m.MeetingChatLeft) : target >= 0 ? F("msg.to", Name(target), m.TextsLeft) : T("msg.pick_target"));
            var row = Wrap(p);
            if (f.MeetingChatOpen) Btn(T("msg.all"), () => { st.MsgTarget = -1; st.MarkDirty(); }, target == -1 ? "pl-on" : null).AddTo(row);
            if (f.TextsOpen)
                foreach (var pl in v.Players)
                {
                    if (pl.Id == me) continue;
                    int id = pl.Id;
                    Btn(pl.Name, () => { st.MsgTarget = id; st.MarkDirty(); st.FocusMessageField?.Invoke(); }, target == id ? "pl-on" : null).AddTo(row);
                }
        }

        /// <summary>Sends the compose text to the chosen recipient (or the meeting chat); returns true when sent.</summary>
        public static bool SendMessage(LineView v, PlayState st, string raw)
        {
            string text = (raw ?? "").Trim();
            if (v == null || text.Length == 0) return false;
            int target = st.MsgTarget == -2 && v.Features.MeetingChatOpen ? -1 : st.MsgTarget;
            if (target == -1)
            {
                if (!v.Features.MeetingChatOpen) { st.Toast?.Invoke(T("msg.chat_closed")); return false; }
                Send(LineVerb.SendMeetingChat, text: text);
            }
            else if (target >= 0) Send(LineVerb.SendText, target, text: text);
            else { st.Toast?.Invoke(T("msg.pick_target")); return false; }
            return true;
        }

        // ---- calls

        /// <summary>My calls, newest first: direction, colleague, duration or why it did not connect.</summary>
        public static void BuildCallLog(VisualElement p, LineView v, double now, int max, bool head = true)
        {
            var m = v.Mine;
            if (m.CallLog.Count == 0) { if (!head) Line(p, T("office.calllog_empty"), "pl-dim"); return; }
            if (head) Sub(p, T("phone.log"));
            for (int i = m.CallLog.Count - 1, n = 0; i >= 0 && n < max; i--, n++)
            {
                var c = m.CallLog[i];
                string head2 = T(c.Outgoing ? "phone.log_out" : "phone.log_in") + " " + Name(c.Other) + " · ";
                bool missed = !c.Connected && !c.Outgoing && c.EndReason == CallEndReason.Missed;
                if (c.Connected && c.End < 0)
                {
                    double start = c.Start;
                    Live(p, "log" + c.CallId + ":" + start, now, t => head2 + F("phone.log_active", UiKit.Clock(Math.Max(0, t - start))), "pl-dim");
                }
                else Line(p, head2 + (c.Connected ? UiKit.Clock(c.End - c.Start) : T("phone.log_missed." + c.EndReason)), missed ? "pl-bad" : "pl-dim");
            }
        }

        /// <summary>Recorded calls (leak buttons live in the meeting focus card).</summary>
        public static bool RecordingCurrentCall(LineView v)
        {
            var ph = v.Mine.Phone;
            if (ph.Status != CallStatus.Connected) return false;
            foreach (var r in v.Mine.Recorded) if (r.CallId == ph.CallId) return true;
            return false;
        }
    }
}
