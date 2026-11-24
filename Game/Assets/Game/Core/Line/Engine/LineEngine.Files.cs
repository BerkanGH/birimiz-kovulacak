using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    /// <summary>
    /// v5.3 (spec §23): "Haftanın gündemi" (the stakes of the three tasks, drawn per match) and "Dosya" (persistent
    /// files instead of the one-shot investigation and whistle; those remain only with <see cref="LineConfig.Legacy"/>).
    /// </summary>
    internal sealed partial class LineEngine
    {
        // ---- stakes

        private readonly LineStakesView stakes = new LineStakesView();

        /// <summary>
        /// Draws the week type from the seed with a generator of its own (the pairings of a seed do not depend on the
        /// scheme) and writes the levels' amounts into this match's config, so the payouts and everything that reads
        /// <see cref="Config"/> use the match's actual numbers. With fixed stakes the config plays as it is.
        /// </summary>
        private void DrawStakes()
        {
            stakes.Scheme = c.StakesScheme;
            if (c.StakesScheme == LineStakes.Permutation)
            {
                var r = new LineRandom(unchecked(setup.Seed * 486187739 + 1013904223));
                int type = r.Index(LineStakes.WeekTypes);
                stakes.WeekType = type;
                stakes.LevelBonus = LineStakes.Level(type, TaskKind.ClientBonus);
                stakes.LevelCash = LineStakes.Level(type, TaskKind.PettyCash);
                stakes.LevelShift = LineStakes.Level(type, TaskKind.WeekendShift);
                int pot = c.StakeBonus(stakes.LevelBonus);
                c.BonusPairShare = c.BonusTrioNone = pot / 2;
                c.BonusPairTaker = c.BonusTrioTaker = Math.Max(0, pot - c.StakeTakerCut);
                c.CashTake = c.StakeCash(stakes.LevelCash);
                c.ShiftSkipPay = c.StakeSkip(stakes.LevelShift);
            }
            stakes.BonusShare = c.BonusPairShare;
            stakes.BonusPot = 2 * c.BonusPairShare;
            stakes.BonusTaker = c.BonusPairTaker;
            stakes.BonusBoth = c.BonusPairBothTake;
            stakes.CashTake = c.CashTake;
            stakes.ShiftSkipPay = c.ShiftSkipPay;
        }

        // ---- files

        private sealed class FileRec
        {
            internal TaskKind Task;
            internal int Holder, Target;
            internal LineFileState State;
            /// <summary>Created (while held), else opened / shredded / voided / expired, and the phase of that moment.</summary>
            internal double Time;
            internal LinePhase Phase;
            /// <summary>Its opening is a line of a meeting room dossier already.</summary>
            internal bool Listed;
        }

        private readonly List<FileRec> files = new List<FileRec>();
        private int filesShredded, filesExpired;

        private bool FilesOn { get { return c.Legacy == null; } }
        /// <summary>A held file can be opened or shredded: until the crisis calls end.</summary>
        private bool FilesWindow { get { return FilesOn && Phase >= LinePhase.Intro && Phase <= LinePhase.CrisisCalls; } }

        private int FilesHeld()
        {
            int n = 0;
            foreach (var f in files) if (f.State == LineFileState.Held) n++;
            return n;
        }

        /// <summary>A pair task was revealed: every Client Bonus sharer gets a file on each group-mate who took, every
        /// witness of a sole Petty Cash theft one on the thief.</summary>
        private void CreateFiles(TaskState t, double time)
        {
            if (!FilesOn) return;
            if (t.Kind == TaskKind.ClientBonus)
            {
                foreach (var g in t.Groups)
                    foreach (int holder in g)
                    {
                        if (t.Choice[holder] == LineChoice.Take) continue;
                        foreach (int target in g)
                            if (target != holder && t.Choice[target] == LineChoice.Take) AddFile(t.Kind, holder, target, time);
                    }
            }
            else if (t.Kind == TaskKind.PettyCash)
            {
                foreach (var theft in t.Thefts)
                    foreach (int witness in theft.Witnesses) AddFile(t.Kind, witness, theft.Thief, time);
            }
        }

        private void AddFile(TaskKind task, int holder, int target, double time)
        {
            files.Add(new FileRec { Task = task, Holder = holder, Target = target, Time = time, Phase = LinePhase.TaskReveal });
            Changed();
        }

        private void SetFile(FileRec f, LineFileState state, double time)
        {
            f.State = state;
            f.Time = time;
            f.Phase = Phase;
            Changed();
        }

        /// <summary><see cref="LineConfig.FileHeldBlocksVote"/>: somebody still holds a file on this offence, so the
        /// meeting does not vote on it.</summary>
        private bool FileBlocksVote(TaskKind task, int target)
        {
            return FilesOn && c.FileHeldBlocksVote && files.Exists(f => f.Task == task && f.Target == target && f.State == LineFileState.Held);
        }

        private LineFailure FileAction(int id, LineAction a, double time)
        {
            if (!FilesOn) return LineFailure.NotAvailable;
            if (!FilesWindow) return LineFailure.WrongPhase;
            var tf = TargetFailure(id, a.Target);
            if (tf != LineFailure.None) return tf;
            if (a.A != (int)TaskKind.ClientBonus && a.A != (int)TaskKind.PettyCash) return LineFailure.BadParam;
            var task = (TaskKind)a.A;
            var file = files.Find(f => f.Task == task && f.Holder == id && f.Target == a.Target);
            if (file == null) return LineFailure.NotEligible;
            if (file.State != LineFileState.Held) return LineFailure.AlreadyResolved;
            if (a.Verb == LineVerb.FileShred) ShredFile(file, time);
            else OpenFile(file, time);
            return LineFailure.None;
        }

        /// <summary>Irreversible and quiet: the holder and the target are told who; everyone else only that a file is gone.</summary>
        private void ShredFile(FileRec file, double time)
        {
            SetFile(file, LineFileState.Shredded, time);
            filesShredded++;
            Emit(LineEventType.FileShredded, time, file.Holder, file.Target, (int)file.Task, recipient: file.Holder);
            Emit(LineEventType.FileShredded, time, file.Holder, file.Target, (int)file.Task, recipient: file.Target);
            if (!c.FilePublicCount) return;
            int held = FilesHeld();
            foreach (int other in ids)
                if (other != file.Holder && other != file.Target) Emit(LineEventType.FileNotice, time, a: (int)LineFileState.Shredded, b: 1, recipient: other, extra: held);
        }

        /// <summary>The target's suç lands now, publicly; the offence is settled, so the other files on it are void.</summary>
        private void OpenFile(FileRec file, double time)
        {
            SetFile(file, LineFileState.Opened, time);
            foreach (var f in files)
                if (f != file && f.Task == file.Task && f.Target == file.Target && f.State == LineFileState.Held) SetFile(f, LineFileState.Void, time);
            int blame = c.FileBlame(file.Task);
            if (file.Task == TaskKind.ClientBonus)
            {
                if (!exposed.Contains(file.Target)) exposed.Add(file.Target);
                AddBlame(file.Target, blame, BlameReason.InvestigationGuilty, time);
            }
            else
            {
                var cash = TaskOf(TaskKind.PettyCash);
                var theft = cash == null ? null : cash.Thefts.Find(x => x.Thief == file.Target);
                if (theft != null && !theft.Resolved) { theft.Whistler = file.Holder; theft.WhistleTime = time; }
                AddBlame(file.Target, blame, BlameReason.Whistled, time);
            }
            incidents.Add(FileEntry(file));
            // a meeting room dossier is on screen (the meeting, the crisis calls): the line joins it at once
            if (meeting != null && Phase >= LinePhase.MeetingDossier && Phase <= LinePhase.CrisisCalls)
            {
                meeting.Dossier.Add(FileEntry(file));
                file.Listed = true;
            }
            Emit(LineEventType.FileOpened, time, file.Holder, file.Target, (int)file.Task, blame);
        }

        private LineDossierEntry FileEntry(FileRec f)
        {
            return new LineDossierEntry { Kind = DossierKind.FileOpened, Task = f.Task, Player = f.Holder, Other = f.Target, N1 = c.FileBlame(f.Task), Time = f.Time };
        }

        /// <summary>A meeting room dossier opens: the files opened since the last one (outside the meeting room).</summary>
        private void ListOpenedFiles()
        {
            foreach (var f in files)
            {
                if (f.State != LineFileState.Opened || f.Listed) continue;
                meeting.Dossier.Add(FileEntry(f));
                f.Listed = true;
            }
        }

        /// <summary>The crisis calls ended: whatever is still held is worth nothing any more.</summary>
        private void ExpireFiles(double time)
        {
            int n = 0;
            foreach (var f in files)
            {
                if (f.State != LineFileState.Held) continue;
                SetFile(f, LineFileState.Expired, time);
                n++;
            }
            filesExpired += n;
            if (n > 0 && c.FilePublicCount) Emit(LineEventType.FileNotice, time, a: (int)LineFileState.Expired, b: n, extra: 0);
        }

        private LineFileView FileView(FileRec f)
        {
            return new LineFileView { Task = f.Task, Holder = f.Holder, Target = f.Target, State = f.State, Time = f.Time, Phase = f.Phase, Blame = c.FileBlame(f.Task) };
        }

        /// <summary>The files a player holds and the files on them (every state).</summary>
        private List<LineFileView> FilesOf(int player)
        {
            var list = new List<LineFileView>();
            foreach (var f in files) if (f.Holder == player || f.Target == player) list.Add(FileView(f));
            return list;
        }

        private LineFilesView FilesView()
        {
            var v = new LineFilesView();
            if (!FilesOn) return v;
            foreach (var f in files) if (f.State == LineFileState.Opened) v.Opened.Add(FileView(f));
            v.Opened.Sort((x, y) => x.Time.CompareTo(y.Time));
            if (c.FilePublicCount)
            {
                v.Held = FilesHeld();
                v.Shredded = filesShredded;
                v.Expired = filesExpired;
            }
            return v;
        }

        private LineStakesView StakesView()
        {
            return new LineStakesView
            {
                Scheme = stakes.Scheme, WeekType = stakes.WeekType, LevelBonus = stakes.LevelBonus, LevelCash = stakes.LevelCash, LevelShift = stakes.LevelShift,
                BonusPot = stakes.BonusPot, BonusShare = stakes.BonusShare, BonusTaker = stakes.BonusTaker, BonusBoth = stakes.BonusBoth,
                CashTake = stakes.CashTake, ShiftSkipPay = stakes.ShiftSkipPay,
            };
        }
    }
}
