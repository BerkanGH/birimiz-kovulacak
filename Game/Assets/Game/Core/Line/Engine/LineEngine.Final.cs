using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    /// <summary>Crisis, candidates, prim declarations (v5.2), defense, final votes, firings, desk split and results (spec §9).</summary>
    internal sealed partial class LineEngine
    {
        private sealed class FinalState
        {
            internal readonly List<LineBlameGainView> Crisis = new List<LineBlameGainView>();
            internal readonly List<int> Candidates = new List<int>();
            internal readonly List<int> DefenseOrder = new List<int>();
            internal readonly List<int> Options = new List<int>();
            internal readonly List<LineFiringView> Firings = new List<LineFiringView>();
            internal int Round;
            internal bool Revote;
            internal int FiringsTotal;
            /// <summary>Prim declarations per seat id: the amount (-1 none) and the real prim at the reveal (-1 before it).</summary>
            internal readonly int[] Declared = { -1, -1, -1, -1, -1, -1 };
            internal readonly int[] DeclaredActual = { -1, -1, -1, -1, -1, -1 };
            internal bool DeclarationsRevealed;
            /// <summary>Set when the final discussion starts: its start + the declaration window.</summary>
            internal double DeclareUntil;
        }

        private FinalState final = new FinalState();
        private int defenderIndex;

        private double DefenderEnd(int index) { return phaseStart + (index + 1) * c.Durations.FinalDefense; }

        private void StartCrisis(double time)
        {
            SetDay(2);
            meeting = new MeetingState { Index = 2 };
            picks.Clear();
            final = new FinalState { FiringsTotal = c.FiringsFor(PlayerCount) };
            // v5.2 (no meeting 2): an unstaffed shift's roster and the suç its skippers got at the reveal belong to this
            // segment's dossier, so the meeting room shows them with the crisis vote
            var shift = tasks[2];
            if (!c.ShiftMeeting && shift != null && shift.Kind == TaskKind.WeekendShift && shift.Failed)
            {
                meeting.Roster.AddRange(shift.Roster);
                Line(DossierKind.ShiftRoster, time, n1: shift.Roster.Count, n2: shift.Needed, task: shift.Kind);
                if (shift.SkipBlamed > 0) Line(DossierKind.ShiftSkipBlame, time, n1: c.ShiftFailSkipBlame, n2: shift.SkipBlamed, task: shift.Kind);
            }
            Enter(LinePhase.CrisisCalls, time, c.Durations.CrisisCalls);
        }

        private void StartCrisisVote(double time)
        {
            NewBallots(1, false);
            Enter(LinePhase.CrisisVote, time, c.Durations.CrisisVote);
        }

        private void ResolveCrisis(double time)
        {
            var votes = new int[LineProtocol.MaxPlayers];
            int total = 0;
            foreach (int id in ids)
            {
                var s = seats[id];
                int target = s.Ballots.Length > 0 ? s.Ballots[0] : -1;
                bool self = target < 0;
                if (self)
                {
                    if (!s.Connected) continue;
                    target = id;
                }
                votes[target]++;
                total++;
                ballotLog.Add(new LineBallotView { Kind = BallotKind.Crisis, Round = 0, Voter = id, Target = target, SelfAssigned = self });
            }
            ClearBallots();
            int top = 0;
            foreach (int id in ids) top = Math.Max(top, votes[id]);
            var tied = new List<int>();
            if (total > 0) foreach (int id in ids) if (votes[id] == top) tied.Add(id);
            int each = tied.Count == 0 ? 0 : c.CrisisBlame / tied.Count; // remainder dropped
            foreach (int id in tied)
            {
                final.Crisis.Add(new LineBlameGainView { Player = id, Amount = each });
                AddBlame(id, each, BlameReason.Crisis, time);
            }
            Emit(LineEventType.CrisisResolved, time, target: tied.Count == 1 ? tied[0] : -1, a: each, b: tied.Count);
            ChooseCandidates();
        }

        /// <summary>Top CandidatesFor(N) by suç among the players with at least <see cref="LineConfig.CandidateMinBlame"/>
        /// suç (fewer qualify: those); everyone tied with the last included candidate joins. Firings ≤ candidates.</summary>
        private void ChooseCandidates()
        {
            var order = new List<int>(ids);
            order.RemoveAll(id => seats[id].Blame < c.CandidateMinBlame);
            order.Sort((x, y) => seats[x].Blame != seats[y].Blame ? seats[y].Blame.CompareTo(seats[x].Blame) : x.CompareTo(y));
            int count = Math.Min(c.CandidatesFor(PlayerCount), order.Count);
            if (count > 0)
            {
                int threshold = seats[order[count - 1]].Blame;
                foreach (int id in order)
                {
                    if (seats[id].Blame < threshold) break;
                    final.Candidates.Add(id);
                    final.DefenseOrder.Add(id);
                    seats[id].Candidate = true;
                }
            }
            final.FiringsTotal = Math.Min(final.FiringsTotal, final.Candidates.Count);
            Changed();
        }

        private void StartFinalDiscussion(double time)
        {
            picks.Clear();
            Enter(LinePhase.FinalDiscussion, time, c.Durations.FinalDiscussion);
            if (!DeclarationsOn) return;
            final.DeclareUntil = time + DeclareWindow;
            // everyone already declared during the crisis verdict (or there is no time left to): revealed at once
            if (AllDeclared() || DeclareWindow <= 0) RevealDeclarations(time);
        }

        // ---- prim declarations ("prim beyanı", v5.2): candidates state their prim (they may lie); sealed until revealed
        // together at max(final discussion start, the last candidate's declaration), at the deadline at the latest

        /// <summary>Declarations exist in this match: the config flag, and a final with candidates.</summary>
        private bool DeclarationsOn { get { return c.DeclarationsEnabled && final.Candidates.Count > 0 && final.FiringsTotal > 0; } }
        private bool DeclarationsPending { get { return DeclarationsOn && !final.DeclarationsRevealed; } }
        /// <summary>How long into the final discussion declaring stays open (never past its end).</summary>
        private double DeclareWindow { get { return Math.Max(0, Math.Min(c.DeclareSeconds, c.Durations.FinalDiscussion)); } }
        /// <summary>The deadline as a host time: known from the crisis verdict on (its end is the final discussion's start).</summary>
        private double DeclareDeadline { get { return Phase == LinePhase.CrisisVerdict ? PhaseEndTime + DeclareWindow : final.DeclareUntil; } }
        private bool DeclareWindowOpen
        {
            get { return DeclarationsPending && (Phase == LinePhase.CrisisVerdict || Phase == LinePhase.FinalDiscussion); }
        }

        private bool AllDeclared()
        {
            foreach (int id in final.Candidates) if (final.Declared[id] < 0) return false;
            return true;
        }

        private LineFailure Declare(int id, LineAction a, double time)
        {
            if (!c.DeclarationsEnabled) return LineFailure.NotAvailable;
            if (!DeclareWindowOpen) return LineFailure.WrongPhase;
            if (!seats[id].Candidate) return LineFailure.NotEligible;
            if (final.Declared[id] >= 0) return LineFailure.AlreadyUsed;
            if (a.A < 0 || a.A > c.DeclareMax) return LineFailure.BadParam;
            final.Declared[id] = a.A;
            // the last declaration of the final discussion opens them all (during the crisis verdict they wait for it)
            if (Phase == LinePhase.FinalDiscussion && AllDeclared()) RevealDeclarations(time);
            return LineFailure.None;
        }

        /// <summary>All declarations become public at once; each candidate's real prim is noted for the results.</summary>
        private void RevealDeclarations(double time)
        {
            if (!DeclarationsPending) return;
            final.DeclarationsRevealed = true;
            int made = 0;
            foreach (int id in final.Candidates)
            {
                final.DeclaredActual[id] = seats[id].Prim;
                if (final.Declared[id] >= 0) made++;
            }
            Emit(LineEventType.DeclarationsRevealed, time, a: made, b: final.Candidates.Count);
            Changed();
        }

        private void StartDefense(double time)
        {
            defenderIndex = 0;
            Enter(LinePhase.FinalDefense, time, c.Durations.FinalDefense * final.DefenseOrder.Count);
        }

        private List<int> Remaining()
        {
            return final.Candidates.FindAll(id => !seats[id].Fired);
        }

        private void StartFinalVote(double time, int round, List<int> options, bool revote)
        {
            final.Round = round;
            final.Revote = revote;
            final.Options.Clear();
            final.Options.AddRange(options);
            NewBallots(1, true);
            Enter(LinePhase.FinalVote, time, revote ? c.Durations.FinalRevote : c.Durations.FinalVote);
        }

        private void ResolveFinalVote(double time)
        {
            var votes = new int[LineProtocol.MaxPlayers];
            var ballots = new int[LineProtocol.MaxPlayers];
            foreach (int id in ids)
            {
                var s = seats[id];
                ballots[id] = -1;
                if (s.Fired) continue;
                int target = s.Ballots.Length > 0 ? s.Ballots[0] : -1;
                ballots[id] = target;
                if (target >= 0) votes[target]++;
                ballotLog.Add(new LineBallotView { Kind = BallotKind.Final, Round = final.Round, Revote = final.Revote, Voter = id, Target = target });
            }
            ClearBallots();
            int top = 0;
            foreach (int id in final.Options) top = Math.Max(top, votes[id]);
            var tied = final.Options.FindAll(id => votes[id] == top);
            if (tied.Count > 1 && !final.Revote)
            {
                StartFinalVote(time, final.Round, tied, true);
                return;
            }
            var decision = final.Revote ? FiringDecision.Revote : FiringDecision.Plurality;
            if (tied.Count > 1)
            {
                tied = Highest(tied, id => seats[id].Blame);
                decision = FiringDecision.HigherBlame;
            }
            if (tied.Count > 1)
            {
                tied = Highest(tied, id => seats[id].Prim);
                decision = FiringDecision.HigherPrim;
            }
            if (tied.Count > 1)
            {
                tied = new List<int> { tied[rng.Index(tied.Count)] };
                decision = FiringDecision.Random;
            }
            Fire(tied[0], decision, votes[tied[0]], ballots, time);
            Enter(LinePhase.FinalVerdict, time, c.Durations.FinalVerdict);
        }

        private static List<int> Highest(List<int> from, Func<int, int> key)
        {
            int best = int.MinValue;
            foreach (int id in from) best = Math.Max(best, key(id));
            return from.FindAll(id => key(id) == best);
        }

        /// <summary>Desk reveal: the fired player's prim is split floor-equally among those who voted for them in the deciding vote.</summary>
        private void Fire(int fired, FiringDecision decision, int votes, int[] ballots, double time)
        {
            var s = seats[fired];
            var firing = new LineFiringView
            {
                Player = fired, Round = final.Round, Decision = decision, Votes = votes, Desk = s.Prim, Time = time,
                Declared = final.DeclarationsRevealed ? final.Declared[fired] : -1,
            };
            foreach (int id in ids) if (ballots[id] == fired) firing.Recipients.Add(id);
            firing.Share = firing.Recipients.Count == 0 ? 0 : firing.Desk / firing.Recipients.Count;
            firing.Removed = firing.Desk - firing.Share * firing.Recipients.Count;
            AddPrim(fired, -firing.Desk, PrimReason.Fired, time);
            foreach (int id in firing.Recipients) AddPrim(id, firing.Share, PrimReason.DeskShare, time, fired);
            s.Fired = true;
            final.Firings.Add(firing);
            Emit(LineEventType.Fired, time, target: fired, a: firing.Desk, b: firing.Recipients.Count, extra: firing.Share);
            Changed();
        }

        private void AfterVerdict(double time)
        {
            var remaining = Remaining();
            if (final.Firings.Count < final.FiringsTotal && remaining.Count > 0)
            {
                StartFinalVote(time, final.Round + 1, remaining, false);
                return;
            }
            ToResults(time);
        }

        /// <summary>The week ends: results (also straight after the crisis when nobody has the suç to be a candidate).</summary>
        private void ToResults(double time)
        {
            FinishResult(time);
            picks.Clear();
            Enter(LinePhase.Results, time, c.Durations.Results);
        }

        private void FinishResult(double time)
        {
            var r = new LineWeekResult
            {
                WeekId = setup.WeekId, MatchId = setup.MatchId, PlayerCount = PlayerCount, StartTime = setup.StartTime, EndTime = time,
            };
            int best = int.MinValue;
            foreach (int id in ids) if (!seats[id].Fired) best = Math.Max(best, seats[id].Prim);
            foreach (int id in ids)
            {
                var s = seats[id];
                bool winner = !s.Fired && s.Prim == best;
                if (winner) r.Winners.Add(id);
                r.Players.Add(new LineWeekPlayerResult
                {
                    Player = id, Prim = s.Prim, Blame = s.Blame, Fired = s.Fired, Winner = winner, WasCandidate = s.Candidate,
                    Recorded = s.Recorded, Leaked = s.Leaked,
                });
            }
            foreach (var f in final.Firings) r.Fired.Add(f.Player);
            r.Firings.AddRange(final.Firings);
            r.Ballots.AddRange(ballotLog);
            r.Transfers.AddRange(transferLog);
            r.Choices.AddRange(choiceLog);
            r.Incidents.AddRange(incidents);
            r.PrimLog.AddRange(primLog);
            r.BlameLog.AddRange(blameLog);
            if (final.DeclarationsRevealed)
                foreach (int id in final.Candidates)
                    r.Declarations.Add(new LineDeclarationView { Player = id, Amount = final.Declared[id], Actual = final.DeclaredActual[id] });
            foreach (var t in tasks)
            {
                if (t == null || LineTasks.Family(t.Kind) != TaskFamily.Pair) continue;
                foreach (var g in t.Groups) r.Groups.Add(new LineGroupView { TaskNumber = t.Number, Task = t.Kind, Members = new List<int>(g) });
            }
            result = LineCodec.Clone(r);
            result.ResultHash = LineCodec.HashResult(result);
        }
    }
}
