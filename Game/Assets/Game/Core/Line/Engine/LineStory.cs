using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    // Results that tell the story (v5.2, docs/V5_DESIGN.md §5): awards ("Ödüller") and headlines ("Manşetler").
    // Everything here is derived from the full disclosure (LineWeekResult), so every client builds the same story from the
    // same result; nothing is sent over the wire and nothing here changes prim, suç or the winner.

    /// <summary>The awards, in their fixed priority order (the rarest story first). Localisation id: <see cref="LineAwards.Id"/>.</summary>
    public enum AwardKind : byte
    {
        None = 0,
        /// <summary>"Yanlış Adam": fired although the declaration was true (<see cref="LineStory.Truthful"/>). N1 = the
        /// declared prim.</summary>
        WrongMan = 1,
        /// <summary>"En Pahalı Yalan": the candidate whose declaration understated their prim the most (at least
        /// <see cref="LineStory.LieGap"/>). N1 = declared, N2 = real prim at the reveal, N3 = the gap.</summary>
        Liar = 2,
        /// <summary>"Satılmış": kept prim from Other and then punished them (whistle, investigation) or voted for them in
        /// the crisis / a firing vote. N1 = prim kept, N2 = how (<see cref="LineStory.BreakWhistle"/>..).</summary>
        Sellout = 3,
        /// <summary>"Sözünün Eri": kept silence money from Other (whom they could have punished) and never punished or
        /// voted against them. N1 = prim kept.</summary>
        Loyal = 4,
        /// <summary>"Soyguncu": took the whole Client Bonus while the group shared. Other = a victim, N1 = prim taken,
        /// N2 = victims.</summary>
        Robber = 5,
        /// <summary>"Kasa Faresi": the only taker of a Petty Cash box, never whistled on. N1 = prim taken.</summary>
        CashRat = 6,
        /// <summary>"Ayın İspiyoncusu": opened the most punishments (whistles + investigations, at least one). N1 = how many.</summary>
        Snitch = 7,
        /// <summary>"Günah Keçisi": the one colleague who got the crisis suç. N1 = suç, N2 = crisis votes received.</summary>
        Scapegoat = 8,
        /// <summary>"Kara Kutu": leaked a call recording. N1 = leaks.</summary>
        BlackBox = 9,
        /// <summary>"Tutanak Kralı": got a tutanak for a prank. N1 = tutanaks.</summary>
        Tutanak = 10,
        /// <summary>"Nöbet Kaçağı": skipped a staffed Weekend Shift, kept the pay and was not fired. N1 = the pay.</summary>
        Dodger = 11,
        /// <summary>"Temiz Sicil": ended with 0 suç and was not fired. N1 = final prim.</summary>
        Clean = 12,
    }

    public static class LineAwards
    {
        public const int Count = 12;
        static readonly string[] Ids = { "", "wrongman", "liar", "sellout", "loyal", "robber", "cashrat", "snitch", "goat", "blackbox", "tutanak", "dodger", "clean" };
        /// <summary>Stable string id (localisation v5.award.&lt;id&gt;.name / .text, the bot line bot.award.&lt;id&gt;, logs).</summary>
        public static string Id(AwardKind k) { int i = (int)k; return i >= 0 && i < Ids.Length ? Ids[i] : ""; }
    }

    public sealed class LineAward
    {
        public AwardKind Kind;
        public int Player = LineIds.None;
        /// <summary>The other colleague of the story (the payer, a victim), else -1.</summary>
        public int Other = LineIds.None;
        public int N1 = -1, N2 = -1, N3 = -1;
    }

    /// <summary>Headline kinds, in priority order. Localisation id: <see cref="LineHeadlines.Id"/>.</summary>
    public enum HeadlineKind : byte
    {
        None = 0,
        /// <summary>A broken deal: Player kept N1 prim from Other and still punished / voted against them (N2 = how).</summary>
        DealBroken = 1,
        /// <summary>Client Bonus: Player took it all (N1 prim); Other (and Other2 in a trio) had shared.</summary>
        BonusBetrayal = 2,
        /// <summary>Player was fired: N1 = the desk, N2 = the declared prim (-1 none), N3 = the firing round, N4 = the
        /// prim at the reveal (what the declaration is judged against; -1 unknown: the desk then).</summary>
        Firing = 3,
        /// <summary>The crisis: Player got it alone (N1 suç, N3 votes), or -1 when N2 colleagues split it (N1 each).</summary>
        Crisis = 4,
        /// <summary>The biggest single transfer of the match: Player sent Other N1 prim.</summary>
        BigTransfer = 5,
    }

    public static class LineHeadlines
    {
        static readonly string[] Ids = { "", "deal", "bonus", "firing", "crisis", "transfer" };
        /// <summary>Stable string id (localisation v5.headline.&lt;id&gt;*, logs).</summary>
        public static string Id(HeadlineKind k) { int i = (int)k; return i >= 0 && i < Ids.Length ? Ids[i] : ""; }
    }

    public sealed class LineHeadline
    {
        public HeadlineKind Kind;
        public int Player = LineIds.None, Other = LineIds.None, Other2 = LineIds.None;
        public int N1 = -1, N2 = -1, N3 = -1, N4 = -1;
    }

    /// <summary>
    /// The story of a finished match: at most <see cref="MaxAwards"/> awards (fixed priority order, at most one per
    /// player where possible) and at most <see cref="MaxHeadlines"/> headlines (one per kind first, then more of a kind).
    /// Pure and deterministic: the same result and config always give the same story.
    /// </summary>
    public sealed class LineStory
    {
        public const int MaxAwards = 4, MaxHeadlines = 5;
        /// <summary>A declaration at least this far under the real prim is "En Pahalı Yalan" material.</summary>
        public const int LieGap = 3;
        /// <summary>The single transfer that makes the "biggest transfer" headline is at least this big.</summary>
        public const int BigTransferMin = 2;
        /// <summary>How a deal was broken (Sellout N2, DealBroken N2).</summary>
        public const int BreakWhistle = 0, BreakInvestigation = 1, BreakCrisisVote = 2, BreakFinalVote = 3;

        public readonly List<LineAward> Awards = new List<LineAward>();
        public readonly List<LineHeadline> Headlines = new List<LineHeadline>();

        /// <summary>
        /// The desk opening's stamp: true ("DOĞRU") when the fired candidate's declaration was their real prim when the
        /// declarations were revealed (<see cref="LineFiringView.DeclaredActual"/>; a result without it is judged against
        /// the desk). False without a declaration.
        /// </summary>
        public static bool Truthful(LineFiringView f)
        {
            return f != null && f.Declared >= 0 && f.Declared == (f.DeclaredActual >= 0 ? f.DeclaredActual : f.Desk);
        }

        /// <summary>The (highest-priority) award of a player, or null.</summary>
        public LineAward AwardOf(int player)
        {
            foreach (var a in Awards) if (a.Player == player) return a;
            return null;
        }

        /// <summary>Seconds into Results at which the awards slide follows the ranking (the bots' award lines wait for it).</summary>
        public static double AwardsAt(double resultsSeconds)
        {
            return Math.Max(0, Math.Min(8.0, 0.55 * resultsSeconds));
        }

        public static LineStory Build(LineWeekResult r, LineConfig c)
        {
            var story = new LineStory();
            if (r == null || r.Players == null || r.Players.Count == 0) return story;
            var f = new Facts(r, c ?? LineConfig.CreateDefault());
            // ---- awards: by priority, the best candidate who has none yet; awards left over (every candidate already has
            // one) fill the remaining places
            var taken = new HashSet<int>();
            var leftover = new List<LineAward>();
            for (int k = 1; k <= LineAwards.Count; k++)
            {
                var list = f.Candidates((AwardKind)k);
                if (list.Count == 0) continue;
                var pick = list.Find(a => !taken.Contains(a.Player));
                if (pick == null) { leftover.Add(list[0]); continue; }
                if (story.Awards.Count >= MaxAwards) continue;
                story.Awards.Add(pick);
                taken.Add(pick.Player);
            }
            foreach (var a in leftover)
            {
                if (story.Awards.Count >= MaxAwards) break;
                story.Awards.Add(a);
            }
            story.Awards.Sort((a, b) => a.Kind != b.Kind ? ((int)a.Kind).CompareTo((int)b.Kind) : a.Player.CompareTo(b.Player));
            // ---- headlines: the first of every kind in priority order, then the rest kind by kind
            var kinds = new List<List<LineHeadline>> { f.DealHeadlines(), f.BonusHeadlines(), f.FiringHeadlines(), f.CrisisHeadlines(), f.TransferHeadlines() };
            for (int round = 0; story.Headlines.Count < MaxHeadlines; round++)
            {
                bool any = false;
                foreach (var list in kinds)
                {
                    if (round >= list.Count) continue;
                    any = true;
                    if (story.Headlines.Count < MaxHeadlines) story.Headlines.Add(list[round]);
                }
                if (!any) break;
            }
            var order = new Dictionary<LineHeadline, int>();
            foreach (var list in kinds) for (int i = 0; i < list.Count; i++) order[list[i]] = i;
            story.Headlines.Sort((a, b) => a.Kind != b.Kind ? ((int)a.Kind).CompareTo((int)b.Kind) : order[a].CompareTo(order[b]));
            return story;
        }

        /// <summary>Lookups over a result (null-safe: a hand-made or older result may miss lists).</summary>
        sealed class Facts
        {
            const int N = LineProtocol.MaxPlayers;
            readonly LineWeekResult r;
            readonly LineConfig c;
            readonly List<int> ids = new List<int>();
            readonly int[] prim = new int[N], blame = new int[N];
            readonly bool[] fired = new bool[N], seated = new bool[N];
            readonly List<Deal> deals;

            internal Facts(LineWeekResult result, LineConfig config)
            {
                r = result;
                c = config;
                foreach (var p in r.Players)
                {
                    if (p == null || p.Player < 0 || p.Player >= N || seated[p.Player]) continue;
                    seated[p.Player] = true;
                    ids.Add(p.Player);
                    prim[p.Player] = p.Prim;
                    blame[p.Player] = p.Blame;
                    fired[p.Player] = p.Fired;
                }
                ids.Sort();
                deals = BrokenDeals();
            }

            bool Seat(int id) { return id >= 0 && id < N && seated[id]; }

            LineChoiceView Choice(TaskKind task, int id)
            {
                if (r.Choices != null) foreach (var ch in r.Choices) if (ch != null && ch.Task == task && ch.Player == id) return ch;
                return null;
            }

            bool Chose(TaskKind task, int id, int choice)
            {
                var ch = Choice(task, id);
                return ch != null && ch.Choice == choice;
            }

            /// <summary>The group <paramref name="id"/> played <paramref name="task"/> in (null when the result has no groups).</summary>
            List<int> Group(TaskKind task, int id)
            {
                if (r.Groups != null) foreach (var g in r.Groups) if (g != null && g.Task == task && g.Members != null && g.Members.Contains(id)) return g.Members;
                return null;
            }

            int Takers(TaskKind task, List<int> group)
            {
                int n = 0;
                foreach (int id in group) if (Chose(task, id, LineChoice.Take)) n++;
                return n;
            }

            bool Punished(int by, int target, out int how, out double time)
            {
                how = -1;
                time = double.MaxValue;
                if (r.Incidents == null) return false;
                foreach (var d in r.Incidents)
                {
                    if (d == null || d.Player != by || d.Other != target) continue;
                    if (d.Kind != DossierKind.Whistle && d.Kind != DossierKind.Investigation) continue;
                    if (d.Time < time) { time = d.Time; how = d.Kind == DossierKind.Whistle ? BreakWhistle : BreakInvestigation; }
                }
                return how >= 0;
            }

            /// <summary>What <paramref name="to"/> kept of <paramref name="from"/>'s prim: sent minus sent back.</summary>
            int Kept(int to, int from)
            {
                int net = 0;
                if (r.Transfers != null)
                    foreach (var t in r.Transfers)
                    {
                        if (t == null) continue;
                        if (t.From == from && t.To == to) net += t.Amount;
                        else if (t.From == to && t.To == from) net -= t.Amount;
                    }
                return net;
            }

            bool Paid(int from, int to, Predicate<LineTransferView> when)
            {
                if (r.Transfers != null) foreach (var t in r.Transfers) if (t != null && t.From == from && t.To == to && when(t)) return true;
                return false;
            }

            sealed class Deal { internal int Taker, Payer, Kept, How; }

            /// <summary>Every pair where someone kept a colleague's prim and then (after a payment) punished them or voted
            /// for them in the crisis / a firing vote; the biggest first.</summary>
            List<Deal> BrokenDeals()
            {
                var list = new List<Deal>();
                foreach (int s in ids)
                    foreach (int p in ids)
                    {
                        if (s == p) continue;
                        int kept = Kept(s, p);
                        if (kept <= 0) continue;
                        int how;
                        double at;
                        if (Punished(s, p, out how, out at)) { if (!Paid(p, s, t => t.Time <= at)) how = -1; }
                        if (how < 0 && r.Ballots != null)
                            foreach (var b in r.Ballots)
                            {
                                if (b == null || b.Voter != s || b.Target != p || b.SelfAssigned) continue;
                                if (b.Kind == BallotKind.Crisis && Paid(p, s, t => t.Phase <= LinePhase.CrisisVote)) { how = BreakCrisisVote; break; }
                                if (b.Kind == BallotKind.Final && Paid(p, s, t => BeforeFiring(t, b.Round))) { how = BreakFinalVote; break; }
                            }
                        if (how >= 0) list.Add(new Deal { Taker = s, Payer = p, Kept = kept, How = how });
                    }
                list.Sort((a, b) => a.Kept != b.Kept ? b.Kept.CompareTo(a.Kept) : a.Taker != b.Taker ? a.Taker.CompareTo(b.Taker) : a.Payer.CompareTo(b.Payer));
                return list;
            }

            /// <summary>The transfer was made before the firing vote of <paramref name="round"/> was decided.</summary>
            bool BeforeFiring(LineTransferView t, int round)
            {
                if (t.Phase < LinePhase.FinalVote) return true;
                if (r.Firings != null) foreach (var f in r.Firings) if (f != null && f.Round == round) return f.Time <= 0 || t.Time <= f.Time;
                return false;
            }

            static int ByPrim(Facts f, LineAward a, LineAward b)
            {
                return f.prim[a.Player] != f.prim[b.Player] ? f.prim[b.Player].CompareTo(f.prim[a.Player]) : a.Player.CompareTo(b.Player);
            }

            /// <summary>Everyone who qualifies for an award, the best first.</summary>
            internal List<LineAward> Candidates(AwardKind kind)
            {
                var list = new List<LineAward>();
                switch (kind)
                {
                    case AwardKind.WrongMan:
                        if (r.Firings != null)
                            foreach (var f in r.Firings)
                                if (f != null && Seat(f.Player) && Truthful(f))
                                    list.Add(new LineAward { Kind = kind, Player = f.Player, N1 = f.Declared });
                        break;
                    case AwardKind.Liar:
                        if (r.Declarations != null)
                            foreach (var d in r.Declarations)
                                if (d != null && Seat(d.Player) && d.Amount >= 0 && d.Actual - d.Amount >= LieGap)
                                    list.Add(new LineAward { Kind = kind, Player = d.Player, N1 = d.Amount, N2 = d.Actual, N3 = d.Actual - d.Amount });
                        list.Sort((a, b) => a.N3 != b.N3 ? b.N3.CompareTo(a.N3) : a.N2 != b.N2 ? b.N2.CompareTo(a.N2) : a.Player.CompareTo(b.Player));
                        break;
                    case AwardKind.Sellout:
                        foreach (var d in deals) list.Add(new LineAward { Kind = kind, Player = d.Taker, Other = d.Payer, N1 = d.Kept, N2 = d.How });
                        break;
                    case AwardKind.Loyal:
                        foreach (int s in ids)
                            foreach (int p in ids)
                            {
                                if (s == p || !CouldPunish(s, p)) continue;
                                int kept = Kept(s, p), how;
                                double at;
                                if (kept <= 0 || Punished(s, p, out how, out at) || deals.Exists(d => d.Taker == s && d.Payer == p)) continue;
                                list.Add(new LineAward { Kind = kind, Player = s, Other = p, N1 = kept });
                            }
                        list.Sort((a, b) => a.N1 != b.N1 ? b.N1.CompareTo(a.N1) : a.Player != b.Player ? a.Player.CompareTo(b.Player) : a.Other.CompareTo(b.Other));
                        break;
                    case AwardKind.Robber:
                        foreach (int id in ids)
                        {
                            var victims = BonusVictims(id);
                            if (victims == null) continue;
                            var ch = Choice(TaskKind.ClientBonus, id);
                            list.Add(new LineAward { Kind = kind, Player = id, Other = victims.Count > 0 ? victims[0] : -1, N1 = ch != null ? ch.Payout : -1, N2 = victims.Count });
                        }
                        list.Sort((a, b) => a.N2 != b.N2 ? b.N2.CompareTo(a.N2) : ByPrim(this, a, b));
                        break;
                    case AwardKind.CashRat:
                        foreach (int id in ids)
                        {
                            var ch = Choice(TaskKind.PettyCash, id);
                            if (ch == null || ch.Choice != LineChoice.Take || ch.Payout <= 0 || Whistled(id)) continue;
                            list.Add(new LineAward { Kind = kind, Player = id, N1 = ch.Payout });
                        }
                        list.Sort((a, b) => ByPrim(this, a, b));
                        break;
                    case AwardKind.Snitch:
                        CountIncidents(list, kind, d => d.Kind == DossierKind.Whistle || d.Kind == DossierKind.Investigation);
                        break;
                    case AwardKind.Scapegoat:
                    {
                        int who = -1, receivers = 0, total = 0;
                        if (r.BlameLog != null)
                            foreach (int id in ids)
                            {
                                int got = 0;
                                foreach (var b in r.BlameLog) if (b != null && b.Player == id && b.Reason == BlameReason.Crisis) got += b.Delta;
                                if (got <= 0) continue;
                                receivers++;
                                who = id;
                                total = got;
                            }
                        if (receivers == 1) list.Add(new LineAward { Kind = kind, Player = who, N1 = total, N2 = CrisisVotes(who) });
                        break;
                    }
                    case AwardKind.BlackBox:
                        CountIncidents(list, kind, d => d.Kind == DossierKind.Leak);
                        break;
                    case AwardKind.Tutanak:
                    {
                        var first = new double[N];
                        var count = new int[N];
                        if (r.BlameLog != null)
                            foreach (var b in r.BlameLog)
                            {
                                if (b == null || !Seat(b.Player) || b.Reason != BlameReason.Tutanak || b.Delta <= 0) continue;
                                if (count[b.Player]++ == 0) first[b.Player] = b.Time;
                            }
                        foreach (int id in ids) if (count[id] > 0) list.Add(new LineAward { Kind = kind, Player = id, N1 = count[id] });
                        list.Sort((a, b) => a.N1 != b.N1 ? b.N1.CompareTo(a.N1) : first[a.Player] != first[b.Player] ? first[a.Player].CompareTo(first[b.Player]) : a.Player.CompareTo(b.Player));
                        break;
                    }
                    case AwardKind.Dodger:
                    {
                        int volunteers = 0;
                        foreach (int id in ids) if (Chose(TaskKind.WeekendShift, id, LineChoice.Volunteer)) volunteers++;
                        if (volunteers < LineConfig.ShiftNeeded(ids.Count)) break;
                        foreach (int id in ids)
                        {
                            var ch = Choice(TaskKind.WeekendShift, id);
                            if (ch == null || ch.Choice != LineChoice.Skip || ch.Payout <= 0 || fired[id]) continue;
                            list.Add(new LineAward { Kind = kind, Player = id, N1 = ch.Payout });
                        }
                        list.Sort((a, b) => ByPrim(this, a, b));
                        break;
                    }
                    case AwardKind.Clean:
                        foreach (int id in ids) if (blame[id] == 0 && !fired[id]) list.Add(new LineAward { Kind = kind, Player = id, N1 = prim[id] });
                        list.Sort((a, b) => ByPrim(this, a, b));
                        break;
                }
                return list;
            }

            /// <summary>Players with at least one incident of a kind: the most first, then whoever was first, then the seat.</summary>
            void CountIncidents(List<LineAward> list, AwardKind kind, Predicate<LineDossierEntry> match)
            {
                var first = new double[N];
                var count = new int[N];
                if (r.Incidents != null)
                    foreach (var d in r.Incidents)
                    {
                        if (d == null || !Seat(d.Player) || !match(d)) continue;
                        if (count[d.Player]++ == 0 || d.Time < first[d.Player]) first[d.Player] = d.Time;
                    }
                foreach (int id in ids) if (count[id] > 0) list.Add(new LineAward { Kind = kind, Player = id, N1 = count[id] });
                list.Sort((a, b) => a.N1 != b.N1 ? b.N1.CompareTo(a.N1) : first[a.Player] != first[b.Player] ? first[a.Player].CompareTo(first[b.Player]) : a.Player.CompareTo(b.Player));
            }

            bool Whistled(int thief)
            {
                if (r.Incidents != null) foreach (var d in r.Incidents) if (d != null && d.Kind == DossierKind.Whistle && d.Other == thief) return true;
                return false;
            }

            int CrisisVotes(int target)
            {
                int n = 0;
                if (r.Ballots != null) foreach (var b in r.Ballots) if (b != null && b.Kind == BallotKind.Crisis && b.Target == target) n++;
                return n;
            }

            /// <summary>The sharers <paramref name="taker"/> left with nothing in the Client Bonus (the only taker of the
            /// group), lowest seat first; null when the player did not rob anyone. Without group data the victims are unknown
            /// (an empty list) and the payout tells.</summary>
            List<int> BonusVictims(int taker)
            {
                var ch = Choice(TaskKind.ClientBonus, taker);
                if (ch == null || ch.Choice != LineChoice.Take) return null;
                var group = Group(TaskKind.ClientBonus, taker);
                if (group == null)
                    return ch.Payout >= Math.Max(c.BonusPairTaker, c.BonusTrioTaker) && ch.Payout > Math.Max(c.BonusPairBothTake, c.BonusTrioMany) ? new List<int>() : null;
                if (Takers(TaskKind.ClientBonus, group) != 1) return null;
                var victims = new List<int>();
                foreach (int id in group) if (id != taker && Seat(id)) victims.Add(id);
                victims.Sort();
                return victims.Count > 0 ? victims : null;
            }

            /// <summary><paramref name="s"/> could have punished <paramref name="p"/>: p took the Client Bonus s shared in,
            /// or was the only Petty Cash taker of a group s left the box alone in.</summary>
            bool CouldPunish(int s, int p)
            {
                var victims = BonusVictims(p);
                if (victims != null && victims.Contains(s)) return true;
                var group = Group(TaskKind.PettyCash, p);
                return group != null && group.Contains(s) && Chose(TaskKind.PettyCash, p, LineChoice.Take) && Takers(TaskKind.PettyCash, group) == 1
                       && Chose(TaskKind.PettyCash, s, LineChoice.Leave);
            }

            // ---- headlines

            internal List<LineHeadline> DealHeadlines()
            {
                var list = new List<LineHeadline>();
                foreach (var d in deals) list.Add(new LineHeadline { Kind = HeadlineKind.DealBroken, Player = d.Taker, Other = d.Payer, N1 = d.Kept, N2 = d.How });
                return list;
            }

            internal List<LineHeadline> BonusHeadlines()
            {
                var list = new List<LineHeadline>();
                foreach (int id in ids)
                {
                    var victims = BonusVictims(id);
                    if (victims == null || victims.Count == 0) continue;
                    var ch = Choice(TaskKind.ClientBonus, id);
                    list.Add(new LineHeadline
                    {
                        Kind = HeadlineKind.BonusBetrayal, Player = id, Other = victims[0], Other2 = victims.Count > 1 ? victims[1] : -1, N1 = ch != null ? ch.Payout : -1,
                    });
                }
                return list;
            }

            internal List<LineHeadline> FiringHeadlines()
            {
                var list = new List<LineHeadline>();
                if (r.Firings != null)
                    foreach (var f in r.Firings)
                        if (f != null && Seat(f.Player))
                            list.Add(new LineHeadline { Kind = HeadlineKind.Firing, Player = f.Player, N1 = f.Desk, N2 = f.Declared, N3 = f.Round, N4 = f.DeclaredActual });
                return list;
            }

            internal List<LineHeadline> CrisisHeadlines()
            {
                var list = new List<LineHeadline>();
                int who = -1, receivers = 0, each = 0;
                if (r.BlameLog != null)
                    foreach (int id in ids)
                    {
                        int got = 0;
                        foreach (var b in r.BlameLog) if (b != null && b.Player == id && b.Reason == BlameReason.Crisis) got += b.Delta;
                        if (got <= 0) continue;
                        receivers++;
                        if (who < 0) who = id;
                        each = got;
                    }
                if (receivers == 0) return list;
                list.Add(new LineHeadline { Kind = HeadlineKind.Crisis, Player = receivers == 1 ? who : -1, N1 = each, N2 = receivers, N3 = CrisisVotes(who) });
                return list;
            }

            internal List<LineHeadline> TransferHeadlines()
            {
                var list = new List<LineHeadline>();
                LineTransferView best = null;
                if (r.Transfers != null)
                    foreach (var t in r.Transfers)
                    {
                        if (t == null || !Seat(t.From) || !Seat(t.To) || t.Amount < BigTransferMin) continue;
                        if (best == null || t.Amount > best.Amount || t.Amount == best.Amount && t.Time < best.Time) best = t;
                    }
                if (best != null) list.Add(new LineHeadline { Kind = HeadlineKind.BigTransfer, Player = best.From, Other = best.To, N1 = best.Amount });
                return list;
            }
        }
    }
}
