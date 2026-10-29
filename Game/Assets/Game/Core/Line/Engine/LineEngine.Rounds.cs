using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    internal sealed partial class LineEngine
    {
        private sealed class RoundState
        {
            internal RoundType Type;
            internal int Hidden = -1, Topic = -1, OddTopic = -1, Code, SolvedBy = -1;
            internal bool Solved;
            internal readonly int[] Partners = { -1, -1, -1, -1, -1, -1 };
            internal readonly int[] SecondPartners = { -1, -1, -1, -1, -1, -1 };
            internal readonly int[] Clues = { -1, -1, -1, -1, -1, -1 };
            internal readonly int[] Digits = new int[6];
            internal readonly List<int> Order = new List<int>();
            internal readonly List<int> Options = new List<int>();
            internal readonly List<int> NoAlibi = new List<int>();
            internal readonly List<LineAttemptView> Attempts = new List<LineAttemptView>();
            internal LineRoundReveal Reveal;
        }
        private sealed class Complaint
        {
            internal int Author, Target, Reason;
        }
        private RoundState round;
        private readonly int[] hiddenRoleUses = new int[6];
        private readonly int[] roundUses = new int[7];
        private readonly List<Complaint> complaints = new List<Complaint>();

        private void StartRound(double time)
        {
            int flat = Day * c.RoundsPerDay + roundIndex;
            round = new RoundState { Type = schedule[flat] };
            roundUses[(int)round.Type]++;
            firstTimeRound = !seen.Contains(round.Type);
            if (firstTimeRound) seen.Add(round.Type);
            foreach (int id in ids)
            {
                var s = seats[id]; s.Submitted = false; s.Target = s.A = -1; s.Attempts = 0;
            }
            round.Order.AddRange(ids); rng.Shuffle(round.Order);
            if (round.Type == RoundType.PrimHavuzu)
            {
                for (int i = 0; i < round.Order.Count;)
                {
                    int size = round.Order.Count - i == 3 ? 3 : 2;
                    for (int j = 0; j < size; j++)
                    {
                        int id = round.Order[i + j];
                        round.Partners[id] = round.Order[i + (j + 1) % size];
                        if (size == 3) round.SecondPartners[id] = round.Order[i + (j + 2) % size];
                    }
                    i += size;
                }
            }
            if (round.Type == RoundType.FarkliEposta || round.Type == RoundType.KayipRapor || round.Type == RoundType.OrtakSifre)
            {
                round.Hidden = ChooseHidden();
                if (setup.ForceHidden != null && setup.ForceHidden.Count > flat && setup.ForceHidden[flat] != -1)
                {
                    if (!HasSeat(setup.ForceHidden[flat])) throw new ArgumentException("Invalid forced hidden player.");
                    round.Hidden = setup.ForceHidden[flat];
                }
                hiddenRoleUses[round.Hidden]++;
            }
            if (round.Type == RoundType.FarkliEposta)
            {
                round.Topic = ChooseTopic(-1);
                int category = round.Topic / c.TopicsPerCategory;
                round.OddTopic = ChooseTopic(category);
                for (int i = 0; i < c.TopicsPerCategory; i++) round.Options.Add(category * c.TopicsPerCategory + i);
                rng.Shuffle(round.Options);
            }
            if (round.Type == RoundType.KayipRapor)
            {
                DealSlips();
            }
            if (round.Type == RoundType.OrtakSifre)
                // Codes are integer actions with exactly N digits, so the first digit is nonzero.
                for (int i = 0; i < ids.Count; i++) { int digit = i == 0 ? 1 + rng.Index(9) : rng.Index(10); round.Digits[round.Order[i]] = digit; round.Code = round.Code * 10 + digit; }
            Enter(LinePhase.RoundIntro, time, firstTimeRound ? c.Durations.RoundIntroFirst : c.Durations.RoundIntro);
        }

        private int ChooseHidden()
        {
            var options = new List<int>(); int minWeek = int.MaxValue, minSession = int.MaxValue;
            foreach (int id in ids)
            {
                int session = setup.History != null && setup.History.HiddenRoles.Count > id ? setup.History.HiddenRoles[id] : 0;
                if (hiddenRoleUses[id] < minWeek || (hiddenRoleUses[id] == minWeek && session < minSession))
                { minWeek = hiddenRoleUses[id]; minSession = session; options.Clear(); }
                if (hiddenRoleUses[id] == minWeek && session == minSession) options.Add(id);
            }
            // Normal schedules have at most N hidden rounds; forced test schedules can exhaust the seats.
            return options[rng.Index(options.Count)];
        }

        private int ChooseTopic(int excludedCategory)
        {
            var options = new List<int>();
            for (int pass = 0; pass < 2 && options.Count == 0; pass++)
                for (int topic = 0; topic < c.TopicCategories * c.TopicsPerCategory; topic++)
                    if (topic / c.TopicsPerCategory != excludedCategory && (pass != 0 || !usedTopics.Contains(topic))) options.Add(topic);
            int chosen = options[rng.Index(options.Count)];
            if (!usedTopics.Contains(chosen)) usedTopics.Add(chosen);
            return chosen;
        }

        private void DealSlips()
        {
            var named = new List<int>();
            foreach (int id in ids) if (id != round.Hidden) named.Add(id);
            rng.Shuffle(named);
            int missing = ids.Count == 6 ? 2 : 1;
            for (int i = 0; i < missing; i++) { round.NoAlibi.Add(named[0]); named.RemoveAt(0); }
            // Give each named innocent a witness first, then fill the other holders independently.
            var holders = new List<int>(ids);
            foreach (int innocent in named)
            {
                var options = new List<int>();
                foreach (int holder in holders) if (holder != innocent && holder != round.Hidden) options.Add(holder);
                int witness = options[rng.Index(options.Count)];
                round.Clues[witness] = innocent; holders.Remove(witness);
            }
            foreach (int holder in holders)
            {
                var options = new List<int>();
                foreach (int innocent in named) if (holder != innocent) options.Add(innocent);
                round.Clues[holder] = options[rng.Index(options.Count)];
            }
        }

        private LineFailure RoundAction(int id, LineAction a, double time)
        {
            if (Phase != LinePhase.RoundPlay) return LineFailure.WrongPhase;
            // v3.1 has a changeable private entry, never a live password attempt.
            if (a.Verb == LineVerb.Attempt) return LineFailure.BadParam;
            var s = seats[id];
            if (s.BlockedUntil > time) return LineFailure.ComputerBlocked;
            if (round.Type == RoundType.OrtakSifre)
            {
                int minimum = 1; for (int i = 1; i < ids.Count; i++) minimum *= 10;
                if (a.A < minimum || a.A >= minimum * 10) return LineFailure.BadParam;
            }
            else if (round.Type == RoundType.PrimHavuzu)
            { if (a.A != 0 && a.A != 1) return LineFailure.BadParam; }
            else
            {
                bool abstain = round.Type == RoundType.IhbarKutusu || round.Type == RoundType.TerfiListesi;
                var target = TargetFailure(id, a.Target, false, abstain); if (target != LineFailure.None) return target;
                if (round.Type == RoundType.IhbarKutusu && a.Target != -1 && (a.A < 0 || a.A >= c.Rounds.ComplaintReasons)) return LineFailure.BadParam;
                if (round.Type == RoundType.FarkliEposta && a.A != -1 && (round.Hidden != id || !round.Options.Contains(a.A))) return LineFailure.BadParam;
            }
            s.Submitted = true; s.Target = round.Type == RoundType.PrimHavuzu || round.Type == RoundType.OrtakSifre ? -1 : a.Target;
            s.A = round.Type == RoundType.KayipRapor || round.Type == RoundType.TerfiListesi ? -1 : a.A;
            Emit(LineEventType.RoundSubmitted, time, actor: id);
            Shorten(time); return LineFailure.None;
        }

        private void Shorten(double time)
        {
            foreach (int id in ids) if (seats[id].Connected && !seats[id].Submitted) return;
            double end = Math.Min(PhaseEndTime, time + c.Durations.AllSubmittedRemaining);
            if (end != PhaseEndTime) { PhaseEndTime = end; Changed(); }
        }

        private int Choice(int id) { return seats[id].Connected && seats[id].Submitted ? seats[id].A : 0; }
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
                Emit(LineEventType.BlameChanged, time, target: id, a: applied, b: (int)reason);
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
                for (int i = 0; i < round.Order.Count;)
                {
                    int a = round.Order[i], b = round.Partners[a], ca = Choice(a), cb = Choice(b);
                    int third = round.SecondPartners[a];
                    if (third >= 0)
                    {
                        int cc = Choice(third), takers = ca + cb + cc;
                        r.Pairs.Add(new LinePairReveal { A = a, B = b, C = third, ChoiceA = ca, ChoiceB = cb, ChoiceC = cc });
                        Delta(r, a, TrioDelta(ca, takers), time); Delta(r, b, TrioDelta(cb, takers), time); Delta(r, third, TrioDelta(cc, takers), time);
                        i += 3; continue;
                    }
                    r.Pairs.Add(new LinePairReveal { A = a, B = b, ChoiceA = ca, ChoiceB = cb });
                    int da, db;
                    if (ca == 0 && cb == 0) da = db = rules.PrimBothShare;
                    else if (ca == 1 && cb == 1) da = db = rules.PrimBothTake;
                    else { int taker = rules.PrimTakerWins - profile.GreedBonus; da = ca == 1 ? taker : rules.PrimSharerLoses; db = cb == 1 ? taker : rules.PrimSharerLoses; }
                    Delta(r, a, da, time); Delta(r, b, db, time);
                    i += 2;
                }
            }
            else if (round.Type == RoundType.OrtakSifre)
            {
                int correct = 0;
                for (int id = 0; id < seats.Length; id++)
                {
                    int code = HasSeat(id) && seats[id].Connected && seats[id].Submitted ? seats[id].A : -1;
                    r.CodesByPlayer.Add(code);
                    if (code == round.Code) correct++;
                }
                round.Solved = correct > ids.Count / 2;
                if (round.Solved) Emit(LineEventType.PasswordCracked, time);
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

        private int TrioDelta(int choice, int takers)
        {
            if (takers == 0) return c.Rounds.PrimTrioNobodyTakes;
            if (takers == 3) return c.Rounds.PrimTrioAllTake;
            if (choice == 0) return c.Rounds.PrimTrioSharer;
            return (takers == 1 ? c.Rounds.PrimTrioSoloTaker : c.Rounds.PrimTrioPairTakers) - profile.GreedBonus;
        }
    }
}
