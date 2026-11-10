using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    /// <summary>Meetings 1 and 2 (spec §7), investigations (§8), leaks and recording picks (§10).</summary>
    internal sealed partial class LineEngine
    {
        private sealed class MeetingState
        {
            internal int Index;
            internal int Voteable = -1;
            internal readonly List<TaskKind> Failed = new List<TaskKind>();
            internal readonly List<LineDossierEntry> Dossier = new List<LineDossierEntry>();
            internal readonly List<int> Roster = new List<int>();
            internal readonly List<LineBlameGainView> BlameGained = new List<LineBlameGainView>();
        }

        private MeetingState meeting;
        private readonly List<LineRecordingPick> picks = new List<LineRecordingPick>();

        public IReadOnlyList<LineRecordingPick> RecordingPicks
        {
            get
            {
                var copy = new List<LineRecordingPick>();
                foreach (var p in picks) copy.Add(CopyPick(p));
                return copy;
            }
        }

        private static LineRecordingPick CopyPick(LineRecordingPick p)
        {
            return new LineRecordingPick
            {
                Index = p.Index, CandidateCallIds = new List<int>(p.CandidateCallIds), PlayAt = p.PlayAt, Seconds = p.Seconds,
                ChosenCallId = p.ChosenCallId, Caller = p.Caller, Callee = p.Callee, CallStart = p.CallStart, Leaker = p.Leaker,
                Played = p.Played, Empty = p.Empty,
            };
        }

        private LineDossierEntry Line(DossierKind kind, double time, int player = -1, int other = -1, int n1 = -1, int n2 = -1, TaskKind task = TaskKind.None)
        {
            var entry = new LineDossierEntry { Kind = kind, Task = task, Player = player, Other = other, N1 = n1, N2 = n2, Time = time };
            meeting.Dossier.Add(entry);
            Changed();
            return entry;
        }

        /// <summary>A dossier line that also belongs to the full-disclosure incident list.</summary>
        private void Incident(DossierKind kind, double time, int player, int other = -1, int n1 = -1, int n2 = -1)
        {
            var entry = Line(kind, time, player, other, n1, n2);
            incidents.Add(new LineDossierEntry { Kind = entry.Kind, Player = player, Other = other, N1 = n1, N2 = n2, Time = time });
        }

        private void StartMeeting(int index, double time)
        {
            SetDay(index);
            meeting = new MeetingState { Index = index };
            picks.Clear();
            if (index == 0)
            {
                for (int i = 0; i < 2; i++)
                {
                    var t = tasks[i];
                    if (t.Kind == TaskKind.ClientBonus) OpenClientBonus(t, time);
                    else if (t.Kind == TaskKind.PettyCash) OpenPettyCash(t, time);
                }
            }
            else
            {
                var t = tasks[2];
                if (t.Failed)
                {
                    meeting.Failed.Add(t.Kind);
                    Line(DossierKind.TaskFailed, time, n1: t.Number, task: t.Kind);
                }
                meeting.Roster.AddRange(t.Roster);
                Line(DossierKind.ShiftRoster, time, n1: t.Roster.Count, n2: t.Needed, task: t.Kind);
            }
            Enter(LinePhase.MeetingDossier, time, c.Durations.MeetingDossier);
        }

        private void OpenClientBonus(TaskState t, double time)
        {
            if (t.Failed)
            {
                meeting.Failed.Add(t.Kind);
                Line(DossierKind.TaskFailed, time, n1: t.Number, task: t.Kind);
            }
            Line(DossierKind.BonusTakers, time, n1: t.Takers, task: t.Kind);
        }

        private void OpenPettyCash(TaskState t, double time)
        {
            int missing = 0, open = 0;
            foreach (var theft in t.Thefts) if (!theft.Resolved) { open++; missing += c.CashTake; }
            if (open > 0)
            {
                meeting.Failed.Add(t.Kind);
                Line(DossierKind.TaskFailed, time, n1: t.Number, task: t.Kind);
            }
            foreach (int id in t.Caught)
            {
                AddBlame(id, c.CashCaughtBlame, BlameReason.CashCaught, time);
                Incident(DossierKind.CashCaught, time, id, n1: c.CashCaughtBlame);
            }
            foreach (var theft in t.Thefts)
            {
                if (!theft.Resolved) continue;
                AddBlame(theft.Thief, c.CashWhistleBlame, BlameReason.Whistled, time);
                Incident(DossierKind.Whistle, theft.WhistleTime, theft.Whistler, theft.Thief, c.CashWhistleBlame);
            }
            if (open > 0) Line(DossierKind.CashMissing, time, n1: missing, n2: open, task: t.Kind);
        }

        /// <summary>Counted when the vote would start (after investigations).</summary>
        private int CountVoteable()
        {
            int count = 0;
            if (meeting.Index == 0)
            {
                for (int i = 0; i < 2; i++)
                {
                    var t = tasks[i];
                    if (t.Kind == TaskKind.ClientBonus)
                    {
                        foreach (int id in ids)
                            if (t.Choice[id] == LineChoice.Take && !exposed.Contains(id)) { count++; break; }
                    }
                    else if (t.Kind == TaskKind.PettyCash && t.Thefts.Exists(x => !x.Resolved)) count++;
                }
            }
            else if (tasks[2].Failed) count++;
            return count;
        }

        private void StartMeetingVote(double time)
        {
            NewBallots(meeting.Voteable, false);
            Enter(LinePhase.MeetingVote, time, c.Durations.MeetingVote);
        }

        private void TallyMeetingVote(double time)
        {
            var gained = new int[LineProtocol.MaxPlayers];
            foreach (int id in ids)
            {
                var s = seats[id];
                for (int i = 0; i < s.Ballots.Length; i++)
                {
                    int target = s.Ballots[i];
                    bool self = target < 0;
                    if (self)
                    {
                        if (!s.Connected) continue; // disconnected players' empty ballots are dropped
                        target = id;
                    }
                    gained[target] += c.BlamePerVote;
                    ballotLog.Add(new LineBallotView { Kind = BallotKind.Meeting, Round = meeting.Index, Voter = id, Index = i, Target = target, SelfAssigned = self });
                }
            }
            foreach (int id in ids)
            {
                meeting.BlameGained.Add(new LineBlameGainView { Player = id, Amount = gained[id] });
                AddBlame(id, gained[id], BlameReason.Vote, time);
            }
            ClearBallots();
        }

        // ---- investigation (meeting 1 dossier/discussion)

        private bool InvestigationOpen
        {
            get
            {
                return meeting != null && meeting.Index == 0 &&
                       (Phase == LinePhase.MeetingDossier || Phase == LinePhase.MeetingDiscussion) && TaskOf(TaskKind.ClientBonus) != null;
            }
        }

        private LineFailure Investigate(int id, LineAction a, double time)
        {
            if (!InvestigationOpen) return LineFailure.WrongPhase;
            var s = seats[id];
            if (s.InvestigationUsed || c.InvestigationsPerPlayer <= 0) return LineFailure.AlreadyUsed;
            var f = TargetFailure(id, a.Target);
            if (f != LineFailure.None) return f;
            var t = TaskOf(TaskKind.ClientBonus);
            if (t.GroupOf[id] != t.GroupOf[a.Target]) return LineFailure.NotEligible;
            if (investigated.Contains(a.Target)) return LineFailure.AlreadyResolved;
            if (s.Prim < c.InvestigationCost) return LineFailure.InsufficientFunds;
            s.InvestigationUsed = true;
            investigated.Add(a.Target);
            AddPrim(id, -c.InvestigationCost, PrimReason.InvestigationCost, time);
            bool guilty = t.Choice[a.Target] == LineChoice.Take;
            if (guilty)
            {
                exposed.Add(a.Target);
                AddBlame(a.Target, c.InvestigationGuiltyBlame, BlameReason.InvestigationGuilty, time);
                int paid = -AddPrim(a.Target, -c.InvestigationRefund, PrimReason.InvestigationRefund, time, id);
                AddPrim(id, paid, PrimReason.InvestigationRefund, time, a.Target);
            }
            else AddBlame(id, c.InvestigationFalseBlame, BlameReason.InvestigationFalse, time);
            Incident(DossierKind.Investigation, time, id, a.Target, guilty ? 1 : 0);
            Emit(LineEventType.Investigated, time, id, a.Target, guilty ? 1 : 0);
            return LineFailure.None;
        }

        private List<int> InvestigationTargets(int id)
        {
            var list = new List<int>();
            if (!InvestigationOpen || seats[id].InvestigationUsed || c.InvestigationsPerPlayer <= 0) return list;
            var t = TaskOf(TaskKind.ClientBonus);
            foreach (int other in t.GroupFor(id)) if (other != id && !investigated.Contains(other)) list.Add(other);
            return list;
        }

        // ---- leaks

        private bool LeakOpen
        {
            get { return Phase == LinePhase.MeetingDossier || Phase == LinePhase.MeetingDiscussion || Phase == LinePhase.FinalDiscussion; }
        }

        private LineFailure Leak(int id, LineAction a, double time)
        {
            if (!RecordingsOn) return LineFailure.NotAvailable;
            if (!LeakOpen) return LineFailure.WrongPhase;
            var call = FindCall(a.A);
            if (call == null || !call.RecordedBy.Contains(id)) return LineFailure.NotOwned;
            if (call.Leaked) return LineFailure.AlreadyResolved;
            call.Leaked = true;
            seats[id].Leaked++;
            double playAt = time + c.LeakDelay;
            if (picks.Count > 0)
            {
                var last = picks[picks.Count - 1];
                playAt = Math.Max(playAt, last.PlayAt + last.Seconds);
            }
            var pick = new LineRecordingPick
            {
                Index = picks.Count, PlayAt = playAt, Seconds = c.LeakSeconds,
                Caller = call.Caller, Callee = call.Callee, CallStart = call.Start, Leaker = id,
            };
            pick.CandidateCallIds.Add(call.Id);
            picks.Add(pick);
            Incident(DossierKind.Leak, time, id, pick.Index, call.Caller, call.Callee);
            Emit(LineEventType.Leaked, time, id, pick.Index, call.Caller, call.Callee, extra: call.Id);
            return LineFailure.None;
        }

        public void ReportRecording(int pickIndex, int chosenCallId, double time)
        {
            Tick(time);
            if (pickIndex < 0 || pickIndex >= picks.Count) return;
            var pick = picks[pickIndex];
            if (pick.Played || pick.Empty) return;
            if (chosenCallId < 0) pick.Empty = true;
            else if (pick.CandidateCallIds.Contains(chosenCallId)) { pick.Played = true; pick.ChosenCallId = chosenCallId; }
            else return;
            Emit(LineEventType.RecordingPlayed, time, a: pickIndex, b: pick.Played ? 1 : 0);
            Changed();
        }
    }
}
