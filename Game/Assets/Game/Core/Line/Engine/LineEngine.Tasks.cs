using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    /// <summary>Tasks (spec §5): grouping, choices, early finish, payouts, Petty Cash thefts and (legacy rules) whistles.</summary>
    internal sealed partial class LineEngine
    {
        private sealed class Theft
        {
            internal int Thief;
            internal readonly List<int> Witnesses = new List<int>();
            /// <summary>Who opened the file on the thief (legacy rules: who whistled), -1 while the theft is unresolved.</summary>
            internal int Whistler = -1;
            internal double WhistleTime;
            internal bool Resolved { get { return Whistler >= 0; } }
        }

        private sealed class TaskState
        {
            internal TaskKind Kind;
            internal int Number;
            internal double PlayEnd;
            internal readonly List<List<int>> Groups = new List<List<int>>();
            internal readonly int[] GroupOf = { -1, -1, -1, -1, -1, -1 };
            internal readonly int[] Choice = new int[LineProtocol.MaxPlayers];
            internal readonly bool[] Submitted = new bool[LineProtocol.MaxPlayers];
            internal readonly int[] Payout = new int[LineProtocol.MaxPlayers];
            internal bool Resolved;
            internal bool Failed;
            /// <summary>Client Bonus: takers in the match.</summary>
            internal int Takers;
            /// <summary>Weekend Shift: volunteers needed and the public roster.</summary>
            internal int Needed = -1;
            internal readonly List<int> Roster = new List<int>();
            /// <summary>Weekend Shift: skippers who got the unstaffed shift's suç at the reveal (v5.2, no meeting 2).</summary>
            internal int SkipBlamed;
            /// <summary>Petty Cash: sole-taker thefts and caught takers (suç applied when meeting 1 opens).</summary>
            internal readonly List<Theft> Thefts = new List<Theft>();
            internal readonly List<int> Caught = new List<int>();

            internal List<int> GroupFor(int id) { return GroupOf[id] < 0 ? null : Groups[GroupOf[id]]; }
        }

        private readonly TaskKind[] kinds = new TaskKind[3];
        private readonly TaskState[] tasks = new TaskState[3];
        private int taskIndex = -1;
        private bool shortened;
        /// <summary>Client Bonus takers exposed by an opened file (legacy rules: an investigation); legacy: targets already investigated.</summary>
        private readonly List<int> exposed = new List<int>();
        private readonly List<int> investigated = new List<int>();
        private readonly List<LineChoiceView> choiceLog = new List<LineChoiceView>();

        private TaskState Current { get { return taskIndex >= 0 ? tasks[taskIndex] : null; } }

        private TaskState TaskOf(TaskKind kind)
        {
            foreach (var t in tasks) if (t != null && t.Kind == kind) return t;
            return null;
        }

        private void ChooseTasks()
        {
            kinds[0] = TaskKind.ClientBonus;
            kinds[1] = TaskKind.PettyCash;
            kinds[2] = TaskKind.WeekendShift;
            var forced = setup.ForceTasks;
            if (forced == null) return;
            if (forced.Count > 3) throw new ArgumentException("Too many forced tasks.");
            for (int i = 0; i < forced.Count; i++) kinds[i] = forced[i];
            for (int i = 0; i < 3; i++)
            {
                var want = i < 2 ? TaskFamily.Pair : TaskFamily.All;
                if (LineTasks.Family(kinds[i]) != want) throw new ArgumentException("Invalid forced task for slot " + i + ".");
                for (int j = 0; j < i; j++) if (kinds[j] == kinds[i]) throw new ArgumentException("Repeated forced task.");
            }
        }

        private void StartTask(int number, double time)
        {
            taskIndex = number;
            shortened = false;
            var t = new TaskState { Kind = kinds[number], Number = number };
            if (LineTasks.Family(t.Kind) == TaskFamily.All)
            {
                t.Groups.Add(new List<int>(ids));
                t.Needed = LineConfig.ShiftNeeded(PlayerCount);
            }
            else
            {
                List<List<int>> previous = null;
                for (int i = 0; i < number; i++) if (LineTasks.Family(tasks[i].Kind) == TaskFamily.Pair) previous = tasks[i].Groups;
                t.Groups.AddRange(MakeGroups(previous));
            }
            for (int g = 0; g < t.Groups.Count; g++) foreach (int id in t.Groups[g]) t.GroupOf[id] = g;
            tasks[number] = t;
            t.PlayEnd = time + c.Durations.TaskPlay;
            Enter(LinePhase.TaskPlay, time, c.Durations.TaskPlay);
        }

        // ---- grouping

        /// <summary>Seeded pairs (last group a trio when odd). Avoids the previous task's pairs and trio when possible.</summary>
        private List<List<int>> MakeGroups(List<List<int>> previous)
        {
            List<List<int>> best = null;
            int bestScore = int.MaxValue;
            int tries = previous == null ? 1 : c.PairingTries;
            for (int attempt = 0; attempt < tries; attempt++)
            {
                var order = new List<int>(ids);
                rng.Shuffle(order);
                var groups = new List<List<int>>();
                int i = 0;
                while (i < order.Count)
                {
                    int size = order.Count - i == 3 ? 3 : 2;
                    groups.Add(order.GetRange(i, size));
                    i += size;
                }
                if (previous == null) return groups;
                int score = GroupingScore(groups, previous);
                if (score < bestScore) { bestScore = score; best = groups; }
                if (score == 0) break;
            }
            return best;
        }

        /// <summary>Repeated pairs dominate (×100); a trio sharing members with the previous trio adds the overlap.</summary>
        private static int GroupingScore(List<List<int>> groups, List<List<int>> previous)
        {
            int repeats = 0, trioOverlap = 0;
            foreach (var g in groups)
            {
                for (int a = 0; a < g.Count; a++)
                    for (int b = a + 1; b < g.Count; b++)
                        foreach (var p in previous)
                            if (p.Contains(g[a]) && p.Contains(g[b])) repeats++;
                if (g.Count == 3)
                    foreach (var p in previous)
                        if (p.Count == 3) foreach (int id in g) if (p.Contains(id)) trioOverlap++;
            }
            return repeats * 100 + trioOverlap;
        }

        // ---- choices

        private LineFailure SubmitTask(int id, LineAction a, double time)
        {
            if (Phase != LinePhase.TaskPlay) return LineFailure.WrongPhase;
            if (a.A != 0 && a.A != 1) return LineFailure.BadParam;
            var t = Current;
            t.Choice[id] = a.A;
            t.Submitted[id] = true;
            Emit(LineEventType.TaskSubmitted, time, actor: id);
            CheckEarlyFinish(time);
            return LineFailure.None;
        }

        private void CheckEarlyFinish(double time)
        {
            var t = Current;
            int connected = 0;
            foreach (int id in ids)
            {
                if (!seats[id].Connected) continue;
                connected++;
                if (!t.Submitted[id]) return;
            }
            if (connected == 0) return;
            double end = time + c.EarlyFinishSeconds;
            if (end < PhaseEndTime) { PhaseEndTime = end; shortened = true; Changed(); }
        }

        // ---- resolution

        private void ResolveTask(double time)
        {
            var t = Current;
            // Unsubmitted (including absent) players keep the default choice 0; no blame for missing choices.
            switch (t.Kind)
            {
                case TaskKind.ClientBonus: ResolveClientBonus(t); break;
                case TaskKind.PettyCash: ResolvePettyCash(t); break;
                case TaskKind.WeekendShift: ResolveWeekendShift(t); break;
            }
            if (t.Kind == TaskKind.WeekendShift)
            {
                // v5.1: every volunteer's suç goes down, staffed or not (never below 0)
                if (c.ShiftVolunteerClean > 0)
                    foreach (int id in t.Roster) RemoveBlame(id, c.ShiftVolunteerClean, BlameReason.ShiftClean, time);
                // v5.2: there is no meeting 2, so the skippers of an unstaffed shift get their suç right here, publicly
                // (with ShiftMeeting it waits for meeting 2 to open, as in v5.1)
                if (t.Failed && !c.ShiftMeeting && c.ShiftFailSkipBlame > 0)
                    foreach (int id in ids)
                    {
                        if (t.Roster.Contains(id)) continue;
                        AddBlame(id, c.ShiftFailSkipBlame, BlameReason.ShiftFailSkip, time);
                        t.SkipBlamed++;
                    }
            }
            foreach (int id in ids)
            {
                AddPrim(id, t.Payout[id], PrimReason.Task, time);
                choiceLog.Add(new LineChoiceView { TaskNumber = t.Number, Task = t.Kind, Player = id, Choice = t.Choice[id], Payout = t.Payout[id] });
            }
            t.Resolved = true;
            CreateFiles(t, time);
            Changed();
        }

        private void ResolveClientBonus(TaskState t)
        {
            foreach (var g in t.Groups)
            {
                int takers = 0;
                foreach (int id in g) if (t.Choice[id] == LineChoice.Take) takers++;
                t.Takers += takers;
                foreach (int id in g)
                {
                    bool take = t.Choice[id] == LineChoice.Take;
                    if (g.Count == 2)
                        t.Payout[id] = takers == 0 ? c.BonusPairShare : takers == 2 ? c.BonusPairBothTake : take ? c.BonusPairTaker : c.BonusPairSharer;
                    else
                        t.Payout[id] = takers == 0 ? c.BonusTrioNone : takers >= 2 ? c.BonusTrioMany : take ? c.BonusTrioTaker : c.BonusTrioOthers;
                }
            }
            t.Failed = t.Takers > 0;
        }

        private void ResolvePettyCash(TaskState t)
        {
            foreach (var g in t.Groups)
            {
                var takers = g.FindAll(id => t.Choice[id] == LineChoice.Take);
                if (takers.Count == 1)
                {
                    var theft = new Theft { Thief = takers[0] };
                    foreach (int id in g) if (id != takers[0]) theft.Witnesses.Add(id);
                    t.Thefts.Add(theft);
                    t.Payout[takers[0]] = c.CashTake;
                }
                else if (takers.Count >= 2) t.Caught.AddRange(takers);
            }
            // Failure (an unresolved theft: no file opened on it yet) is decided when the meeting opens.
        }

        /// <summary>Volunteers get 0; skippers get <see cref="LineConfig.ShiftSkipPay"/> when the shift is staffed, else
        /// <see cref="LineConfig.ShiftFailSkipPay"/> (and their suç: at this reveal, or when meeting 2 opens with
        /// <see cref="LineConfig.ShiftMeeting"/>).</summary>
        private void ResolveWeekendShift(TaskState t)
        {
            foreach (int id in ids) if (t.Choice[id] == LineChoice.Volunteer) t.Roster.Add(id);
            t.Failed = t.Roster.Count < t.Needed;
            int skipPay = t.Failed ? c.ShiftFailSkipPay : c.ShiftSkipPay;
            foreach (int id in ids) if (t.Choice[id] != LineChoice.Volunteer) t.Payout[id] = skipPay;
        }

        // ---- whistle (legacy rules: the break after Petty Cash; v5.3: the witnesses hold files)

        private bool WhistleWindow { get { return !FilesOn && Phase == LinePhase.Break && Current != null && Current.Kind == TaskKind.PettyCash; } }

        private LineFailure Whistle(int id, LineAction a, double time)
        {
            if (!WhistleWindow) return LineFailure.WrongPhase;
            var f = TargetFailure(id, a.Target);
            if (f != LineFailure.None) return f;
            var theft = Current.Thefts.Find(x => x.Thief == a.Target && x.Witnesses.Contains(id));
            if (theft == null) return LineFailure.NotEligible;
            if (theft.Resolved) return LineFailure.AlreadyResolved;
            theft.Whistler = id;
            theft.WhistleTime = time;
            // v5.1: the thief keeps the stolen prim and the whistle pays nothing (CashWhistledKeeps, CashWhistleReward 0).
            // v5.0: the stolen prim left the game (min with balance). Suç is applied publicly when meeting 1 opens.
            int lost = c.Legacy.CashWhistledKeeps ? 0 : -AddPrim(a.Target, -c.CashTake, PrimReason.Whistled, time, id);
            AddPrim(id, c.Legacy.CashWhistleReward, PrimReason.WhistleReward, time, a.Target);
            Emit(LineEventType.Whistled, time, id, a.Target, lost, recipient: id);
            Emit(LineEventType.Whistled, time, id, a.Target, lost, recipient: a.Target);
            return LineFailure.None;
        }

        private List<int> WitnessOf(int id)
        {
            var list = new List<int>();
            if (!WhistleWindow) return list;
            foreach (var theft in Current.Thefts) if (!theft.Resolved && theft.Witnesses.Contains(id)) list.Add(theft.Thief);
            return list;
        }
    }
}
