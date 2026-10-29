using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    internal sealed partial class LineEngine
    {
        private sealed class RoundState
        {
            internal RoundType Type;
            internal int Hidden = -1, Topic = -1, Code, InternChoice, SolvedBy = -1;
            internal bool Solved;
            internal readonly int[] Partners = { -1, -1, -1, -1, -1, -1 };
            internal readonly int[] Clues = { -1, -1, -1, -1, -1, -1 };
            internal readonly int[] Digits = new int[6];
            internal readonly List<int> Order = new List<int>();
            internal readonly List<int> Options = new List<int>();
            internal readonly List<LineAttemptView> Attempts = new List<LineAttemptView>();
            internal LineRoundReveal Reveal;
        }
        private sealed class Complaint
        {
            internal int Author, Target, Reason;
        }
        private RoundState round;
        private readonly List<Complaint> complaints = new List<Complaint>();

        private void StartRound(double time)
        {
            int flat = Day * c.RoundsPerDay + roundIndex;
            round = new RoundState { Type = schedule[flat] };
            firstTimeRound = !seen.Contains(round.Type);
            if (firstTimeRound) seen.Add(round.Type);
            foreach (int id in ids)
            {
                var s = seats[id]; s.Submitted = false; s.Target = s.A = -1; s.Attempts = 0;
            }
            round.Order.AddRange(ids); rng.Shuffle(round.Order);
            if (round.Type == RoundType.PrimHavuzu)
            {
                for (int i = 0; i < round.Order.Count; i += 2)
                {
                    int a = round.Order[i], b = i + 1 < round.Order.Count ? round.Order[i + 1] : LineIds.Intern;
                    round.Partners[a] = b; if (b != LineIds.Intern) round.Partners[b] = a;
                }
                round.InternChoice = rng.Unit() < c.Rounds.InternShareChance ? 0 : 1;
            }
            if (round.Type == RoundType.FarkliEposta || round.Type == RoundType.KayipRapor || round.Type == RoundType.OrtakSifre)
            {
                round.Hidden = ids[rng.Index(ids.Count)];
                if (setup.ForceHidden != null && setup.ForceHidden.Count > flat && setup.ForceHidden[flat] != -1)
                {
                    if (!HasSeat(setup.ForceHidden[flat])) throw new ArgumentException("Invalid forced hidden player.");
                    round.Hidden = setup.ForceHidden[flat];
                }
            }
            if (round.Type == RoundType.FarkliEposta)
            {
                var available = new List<int>();
                for (int i = 0; i < c.TopicPairs; i++) if (!usedTopics.Contains(i)) available.Add(i);
                if (available.Count == 0) for (int i = 0; i < c.TopicPairs; i++) available.Add(i);
                round.Topic = available[rng.Index(available.Count)];
                if (!usedTopics.Contains(round.Topic)) usedTopics.Add(round.Topic);
                available.Clear();
                for (int i = 0; i < c.TopicPairs; i++) if (i != round.Topic) available.Add(i);
                rng.Shuffle(available); round.Options.Add(round.Topic);
                for (int i = 0; i < available.Count && round.Options.Count < c.Rounds.OddGuessOptions; i++) round.Options.Add(available[i]);
                rng.Shuffle(round.Options);
            }
            if (round.Type == RoundType.KayipRapor)
            {
                for (int i = 0; i < round.Order.Count; i++)
                {
                    int next = (i + 1) % ids.Count;
                    if (round.Order[next] == round.Hidden) next = (next + 1) % ids.Count;
                    round.Clues[round.Order[i]] = round.Order[next];
                }
            }
            if (round.Type == RoundType.OrtakSifre)
                for (int i = 0; i < ids.Count; i++) { int digit = rng.Index(10); round.Digits[round.Order[i]] = digit; round.Code = round.Code * 10 + digit; }
            Enter(LinePhase.RoundIntro, time, firstTimeRound ? c.Durations.RoundIntroFirst : c.Durations.RoundIntro);
        }

        private LineFailure RoundAction(int id, LineAction a, double time)
        {
            if (Phase != LinePhase.RoundPlay) return LineFailure.WrongPhase;
            var s = seats[id];
            if (s.BlockedUntil > time) return LineFailure.ComputerBlocked;
            if (a.Verb == LineVerb.Attempt)
            {
                if (round.Type != RoundType.OrtakSifre) return LineFailure.NotAvailable;
                if (round.Solved) return LineFailure.RoundSolved;
                if (s.Attempts >= c.Rounds.AttemptsPerPlayer) return LineFailure.NoAttemptsLeft;
                int limit = 1; for (int i = 0; i < ids.Count; i++) limit *= 10;
                if (a.A < 0 || a.A >= limit) return LineFailure.BadParam;
                int correct = 0, code = a.A;
                for (int i = ids.Count - 1; i >= 0; i--) { if (code % 10 == round.Digits[round.Order[i]]) correct++; code /= 10; }
                s.Attempts++; s.Submitted = true; s.A = a.A;
                round.Attempts.Add(new LineAttemptView { Player = id, Code = a.A, Correct = correct, Time = time });
                Emit(LineEventType.AttemptMade, time, actor: id, a: correct);
                if (correct == ids.Count) { round.Solved = true; round.SolvedBy = id; Emit(LineEventType.PasswordCracked, time, actor: id); }
            }
            else
            {
                if (round.Type == RoundType.OrtakSifre) return LineFailure.NotAvailable;
                if (round.Type == RoundType.PrimHavuzu)
                { if (a.A != 0 && a.A != 1) return LineFailure.BadParam; }
                else
                {
                    bool abstain = round.Type == RoundType.IhbarKutusu || round.Type == RoundType.TerfiListesi;
                    var target = TargetFailure(id, a.Target, false, abstain); if (target != LineFailure.None) return target;
                    if (round.Type == RoundType.IhbarKutusu && a.Target != -1 && (a.A < 0 || a.A >= c.Rounds.ComplaintReasons)) return LineFailure.BadParam;
                    if (round.Type == RoundType.FarkliEposta && a.A != -1 && (round.Hidden != id || !round.Options.Contains(a.A))) return LineFailure.BadParam;
                }
                s.Submitted = true; s.Target = round.Type == RoundType.PrimHavuzu ? -1 : a.Target;
                s.A = round.Type == RoundType.KayipRapor || round.Type == RoundType.TerfiListesi ? -1 : a.A;
                Emit(LineEventType.RoundSubmitted, time, actor: id);
            }
            Shorten(time); return LineFailure.None;
        }

        private void Shorten(double time)
        {
            if (round.Type == RoundType.OrtakSifre) { if (!round.Solved) return; }
            else foreach (int id in ids) if (seats[id].Connected && !seats[id].Submitted) return;
            double end = Math.Min(PhaseEndTime, time + c.Durations.AllSubmittedRemaining);
            if (end != PhaseEndTime) { PhaseEndTime = end; Changed(); }
        }

        private int Choice(int id) { return id == LineIds.Intern ? round.InternChoice : seats[id].Connected && seats[id].Submitted ? seats[id].A : 0; }
        private int ChoiceTarget(int id) { return seats[id].Connected && seats[id].Submitted ? seats[id].Target : -1; }
        private int Plurality(int[] counts, bool blameTie = false)
        {
            int best = -1, max = 0; bool tied = false;
            foreach (int id in ids)
            {
                if (counts[id] > max) { max = counts[id]; best = id; tied = false; }
                else if (counts[id] > 0 && counts[id] == max)
                {
                    if (blameTie && seats[id].Blame > seats[best].Blame) { best = id; tied = false; }
                    else if (!blameTie || seats[id].Blame == seats[best].Blame) tied = true;
                }
            }
            return tied ? -1 : best;
        }

        private LineBlameDelta Blame(int id, int amount, BlameReason reason, double time, RoundType type = RoundType.None, TrickType trick = TrickType.None)
        {
            var s = seats[id];
            if (amount > 0 && id == favourite) amount--;
            int applied = Math.Max(0, s.Blame + amount) - s.Blame;
            s.Blame += applied;
            if (applied != 0)
            {
                Changed();
                // The total is public, but the false-tip reason must not identify its confidential author.
                Emit(LineEventType.BlameChanged, time, target: id, a: applied, b: (int)(reason == BlameReason.FalseTip ? BlameReason.None : reason));
            }
            return new LineBlameDelta { Player = id, Delta = applied, Reason = reason, Round = type, Trick = trick, Day = Day };
        }
        private void Delta(LineRoundReveal reveal, int id, int amount, double time)
        { if (id != LineIds.Intern) reveal.Deltas.Add(Blame(id, amount, BlameReason.Round, time, round.Type)); }

        private void RevealRound(double time)
        {
            var r = new LineRoundReveal { Type = round.Type, Index = roundIndex }; var rules = c.Rounds;
            if (round.Type == RoundType.PrimHavuzu)
            {
                for (int i = 0; i < round.Order.Count; i += 2)
                {
                    int a = round.Order[i], b = round.Partners[a], ca = Choice(a), cb = Choice(b);
                    r.Pairs.Add(new LinePairReveal { A = a, B = b, ChoiceA = ca, ChoiceB = cb });
                    int da, db;
                    if (ca == 0 && cb == 0) da = db = rules.PrimBothShare;
                    else if (ca == 1 && cb == 1) da = db = rules.PrimBothTake;
                    else { int taker = rules.PrimTakerWins - profile.GreedBonus; da = ca == 1 ? taker : rules.PrimSharerLoses; db = cb == 1 ? taker : rules.PrimSharerLoses; }
                    Delta(r, a, da, time); Delta(r, b, db, time);
                }
            }
            else if (round.Type == RoundType.OrtakSifre)
            {
                r.Hidden = round.Hidden; r.Code = round.Code; r.Solved = round.Solved; r.SolvedBy = round.SolvedBy;
                foreach (int id in ids) Delta(r, id, round.Solved ? (id == round.Hidden ? rules.PasswordCrackedSaboteur : rules.PasswordCracked) :
                    (id == round.Hidden ? rules.PasswordFailedSaboteur : rules.PasswordFailed), time);
            }
            else
            {
                int[] counts = new int[6];
                foreach (int id in ids)
                {
                    int target = ChoiceTarget(id); if (target >= 0) counts[target]++;
                    if (round.Type == RoundType.IhbarKutusu)
                    {
                        if (target >= 0)
                        {
                            r.Complaints.Add(new LineComplaintView { Target = target, Reason = seats[id].A });
                            complaints.Add(new Complaint { Author = id, Target = target, Reason = seats[id].A });
                        }
                    }
                    else r.Votes.Add(new LineVoteView { Voter = id, Target = target });
                }
                r.Counts.AddRange(counts);
                if (round.Type == RoundType.IhbarKutusu)
                {
                    rng.Shuffle(r.Complaints); int max = 0;
                    foreach (int id in ids) max = Math.Max(max, counts[id]);
                    if (max > 0) foreach (int id in ids) if (counts[id] == max || counts[id] == 0)
                        Delta(r, id, counts[id] == max ? rules.ComplaintTop : rules.ComplaintNone, time);
                }
                else
                {
                    r.Winner = Plurality(counts);
                    if (round.Type == RoundType.TerfiListesi)
                    {
                        if (r.Winner >= 0)
                        {
                            Delta(r, r.Winner, rules.PromotionWinner, time);
                            foreach (int id in ids) if (ChoiceTarget(id) == r.Winner) Delta(r, id, rules.PromotionBacker, time);
                        }
                    }
                    else
                    {
                        r.Hidden = round.Hidden;
                        if (round.Type == RoundType.FarkliEposta)
                        {
                            r.TopicId = round.Topic;
                            r.OddGuess = seats[round.Hidden].Connected && seats[round.Hidden].Submitted ? seats[round.Hidden].A : -1;
                            r.OddGuessCorrect = r.OddGuess == round.Topic;
                        }
                        else r.Clues.AddRange(round.Clues);
                        if (r.Winner == round.Hidden)
                        {
                            Delta(r, round.Hidden, r.OddGuessCorrect ? rules.OddCaughtButGuessed : rules.HiddenCaught, time);
                            foreach (int id in ids) if (ChoiceTarget(id) == round.Hidden) Delta(r, id, rules.CorrectVoter, time);
                        }
                        else
                        {
                            Delta(r, round.Hidden, rules.HiddenEscaped, time);
                            if (r.Winner >= 0) Delta(r, r.Winner, rules.WrongInnocent, time);
                        }
                    }
                }
            }
            round.Reveal = r; reveals.Add(r); Changed();
        }
    }
}
