using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    /// <summary>Crisis, candidates, defense, final votes, firings, desk split and results (spec §9).</summary>
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

        /// <summary>Top CandidatesFor(N) by suç; everyone tied with the last included candidate joins.</summary>
        private void ChooseCandidates()
        {
            var order = new List<int>(ids);
            order.Sort((x, y) => seats[x].Blame != seats[y].Blame ? seats[y].Blame.CompareTo(seats[x].Blame) : x.CompareTo(y));
            int count = Math.Min(c.CandidatesFor(PlayerCount), order.Count);
            int threshold = seats[order[count - 1]].Blame;
            foreach (int id in order)
            {
                if (seats[id].Blame < threshold) break;
                final.Candidates.Add(id);
                final.DefenseOrder.Add(id);
                seats[id].Candidate = true;
            }
            final.FiringsTotal = Math.Min(final.FiringsTotal, final.Candidates.Count);
            Changed();
        }

        private void StartFinalDiscussion(double time)
        {
            picks.Clear();
            Enter(LinePhase.FinalDiscussion, time, c.Durations.FinalDiscussion);
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
            var firing = new LineFiringView { Player = fired, Round = final.Round, Decision = decision, Votes = votes, Desk = s.Prim };
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
            result = LineCodec.Clone(r);
            result.ResultHash = LineCodec.HashResult(result);
        }
    }
}
