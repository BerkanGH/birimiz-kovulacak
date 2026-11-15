using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests.Line
{
    /// <summary>
    /// Bots vote to win (<see cref="LineBotVoteStyle.Richest"/>): the ledger's estimate from the bot's own views, the richest
    /// pick, previews that equal the real vote, announced votes that cannot change, and the old MostBlame rule kept as it was.
    /// </summary>
    public sealed class LineBotVoteTests
    {
        static readonly LineConfig C = LineConfig.CreateDefault();
        static readonly LineBotPolicy[] Rotation = { LineBotPolicy.Honest, LineBotPolicy.Greedy, LineBotPolicy.Mixed };

        // ------------------------------------------------------------------ hand-made views (what one seat sees)

        static LineView View(int me = 0, int players = 4, int week = 1, LinePhase phase = LinePhase.MeetingDiscussion)
        {
            var v = new LineView { Me = me, WeekId = week, Phase = phase, PhaseStartTime = 100, PhaseEndTime = 120 };
            for (int i = 0; i < players; i++) v.Players.Add(new LinePlayerView { Id = i, Name = "P" + i, Connected = true });
            v.Mine.Prim = C.StartPrim;
            return v;
        }

        static LineTaskView Revealed(TaskKind kind, int[] group, int[] choices, int takers = -1)
        {
            var t = new LineTaskView { Kind = kind, Number = kind == TaskKind.ClientBonus ? 0 : kind == TaskKind.PettyCash ? 1 : 2, Revealed = true, Takers = takers };
            t.Group.AddRange(group);
            for (int i = 0; i < group.Length; i++) t.Choices.Add(new LineChoiceView { Player = group[i], Choice = choices[i], Task = kind });
            return t;
        }

        static LineMeetingView Meeting(int index, params LineDossierEntry[] lines)
        {
            var m = new LineMeetingView { Index = index };
            m.Dossier.AddRange(lines);
            return m;
        }

        static LineDossierEntry D(DossierKind kind, int player = -1, int other = -1, int n1 = -1, int n2 = -1)
        {
            return new LineDossierEntry { Kind = kind, Player = player, Other = other, N1 = n1, N2 = n2 };
        }

        static double[] Fed(params LineView[] views)
        {
            var l = new LineBotLedger(views[0].Me);
            foreach (var v in views) l.Ingest(v);
            return l.Estimate(C);
        }

        // ------------------------------------------------------------------ the ledger

        [Test]
        public void Ledger_StartsAtStartPrim_AndKnowsMyOwnBalance()
        {
            var v = View();
            v.Mine.Prim = 9;
            var e = Fed(v);
            Assert.AreEqual(9, e[0]);
            for (int i = 1; i < 4; i++) Assert.AreEqual(C.StartPrim, e[i]);
            Assert.AreEqual(0, e[4], "unseated");
        }

        [Test]
        public void Ledger_ClientBonus_PartnerExact_OthersFromTheTakersCount()
        {
            // my pair: I shared, 1 took. One taker in the match = mine: the other pair shared.
            var v = View();
            v.Task = Revealed(TaskKind.ClientBonus, new[] { 0, 1 }, new[] { LineChoice.Share, LineChoice.Take }, takers: 1);
            var e = Fed(v);
            Assert.AreEqual(C.StartPrim + C.BonusPairTaker, e[1]);
            Assert.AreEqual(C.StartPrim + C.BonusPairShare, e[2]);
            Assert.AreEqual(C.StartPrim + C.BonusPairShare, e[3]);
            // three takers: both unknown colleagues took (both-take payout)
            v.Task.Takers = 3;
            e = Fed(v);
            Assert.AreEqual(C.StartPrim + C.BonusPairBothTake, e[2], 1e-9);
            // two takers: one of the two, 50/50 → the expected payout
            v.Task.Takers = 2;
            e = Fed(v);
            double p = 0.5, q = 0.5;
            double expected = C.StartPrim + p * (q * C.BonusPairBothTake + (1 - q) * C.BonusPairTaker) + (1 - p) * (q * C.BonusPairSharer + (1 - q) * C.BonusPairShare);
            Assert.AreEqual(expected, e[2], 1e-9);
            Assert.AreEqual(expected, e[3], 1e-9);
        }

        [Test]
        public void Ledger_ClientBonus_TrioIsExact()
        {
            var v = View(players: 3);
            v.Task = Revealed(TaskKind.ClientBonus, new[] { 0, 1, 2 }, new[] { LineChoice.Share, LineChoice.Take, LineChoice.Share }, takers: 1);
            var e = Fed(v);
            Assert.AreEqual(C.StartPrim + C.BonusTrioTaker, e[1]);
            Assert.AreEqual(C.StartPrim + C.BonusTrioOthers, e[2]);
        }

        [Test]
        public void Ledger_Investigation_PaysCostAndRefund_AndTellsTheChoice()
        {
            // takers 2: mine (1) and one of {2, 3}; 3 investigated 2 and 2 had shared → 3's group took, 2 did not
            var reveal = View();
            reveal.Task = Revealed(TaskKind.ClientBonus, new[] { 0, 1 }, new[] { LineChoice.Share, LineChoice.Take }, takers: 2);
            var meeting = View();
            meeting.Meeting = Meeting(0, D(DossierKind.BonusTakers, n1: 2), D(DossierKind.Investigation, 3, 2, n1: 0));
            var e = Fed(reveal, meeting);
            // 2 shared (known) with a partner who surely took (the only unknown takes: q = 1): 0
            Assert.AreEqual(C.StartPrim + C.BonusPairSharer, e[2], 1e-9);
            // 3 took for sure, partner averages q = 1 → both-take, then paid the investigation
            Assert.AreEqual(C.StartPrim + C.BonusPairBothTake - C.InvestigationCost, e[3], 1e-9);

            // guilty: 2 investigated 3 who took
            meeting.Meeting = Meeting(0, D(DossierKind.BonusTakers, n1: 2), D(DossierKind.Investigation, 2, 3, n1: 1));
            e = Fed(reveal, meeting);
            // 3 is the second taker (known), 2 is the only unknown and nobody is left to have taken: q = 0
            double q = 0;
            Assert.AreEqual(C.StartPrim + (q * C.BonusPairBothTake + (1 - q) * C.BonusPairTaker) - C.InvestigationRefund, e[3], 1e-9);
            Assert.AreEqual(C.StartPrim + (q * C.BonusPairSharer + (1 - q) * C.BonusPairShare) - C.InvestigationCost + C.InvestigationRefund, e[2], 1e-9);
        }

        [Test]
        public void Ledger_PettyCash_CaughtWhistleAndMyGroup()
        {
            // 6 players: my pair {0, 1} (1 took alone, I whistled), {2, 3} both took (caught), {4, 5}: 5 took alone, 4 whistled
            var reveal = View(players: 6);
            reveal.Task = Revealed(TaskKind.PettyCash, new[] { 0, 1 }, new[] { LineChoice.Leave, LineChoice.Take });
            var meeting = View(players: 6);
            meeting.Meeting = Meeting(0, D(DossierKind.CashCaught, 2, n1: 2), D(DossierKind.CashCaught, 3, n1: 2),
                D(DossierKind.Whistle, 0, 1, n1: 3), D(DossierKind.Whistle, 4, 5, n1: 3));
            var e = Fed(reveal, meeting);
            Assert.AreEqual(C.StartPrim, e[1], "a whistled thief nets 0");
            Assert.AreEqual(C.StartPrim, e[2], "caught: 0");
            Assert.AreEqual(C.StartPrim, e[3], "caught: 0");
            Assert.AreEqual(C.StartPrim + C.CashWhistleReward, e[4], "whistler");
            Assert.AreEqual(C.StartPrim, e[5], "a whistled thief nets 0");
        }

        [Test]
        public void Ledger_PettyCash_UnresolvedTheftsSpreadOverWhoCouldHaveTaken()
        {
            // my pair clean; the dossier says 1 theft is open: 3 prim spread over 2 and 3 (equal weights)
            var reveal = View();
            reveal.Task = Revealed(TaskKind.PettyCash, new[] { 0, 1 }, new[] { LineChoice.Leave, LineChoice.Leave });
            var meeting = View();
            meeting.Meeting = Meeting(0, D(DossierKind.CashMissing, n1: 3, n2: 1));
            var e = Fed(reveal, meeting);
            Assert.AreEqual(C.StartPrim, e[1]);
            Assert.AreEqual(C.StartPrim + C.CashTake / 2.0, e[2], 1e-9);
            Assert.AreEqual(C.StartPrim + C.CashTake / 2.0, e[3], 1e-9);
            // my partner took alone and nobody whistled: the open theft is theirs, not the others'
            reveal.Task = Revealed(TaskKind.PettyCash, new[] { 0, 1 }, new[] { LineChoice.Leave, LineChoice.Take });
            e = Fed(reveal, meeting);
            Assert.AreEqual(C.StartPrim + C.CashTake, e[1]);
            Assert.AreEqual(C.StartPrim, e[2]);
            Assert.AreEqual(C.StartPrim, e[3]);
            // nothing open (meeting 1 without a CashMissing line): nobody else took
            meeting.Meeting = Meeting(0);
            reveal.Task = Revealed(TaskKind.PettyCash, new[] { 0, 1 }, new[] { LineChoice.Leave, LineChoice.Leave });
            e = Fed(reveal, meeting);
            Assert.AreEqual(C.StartPrim, e[2]);
        }

        [Test]
        public void Ledger_WeekendShift_RosterSkippersGetThePay()
        {
            var v = View();
            v.Task = new LineTaskView { Kind = TaskKind.WeekendShift, Number = 2, Revealed = true };
            v.Task.Roster.AddRange(new[] { 0, 2 });
            var e = Fed(v);
            Assert.AreEqual(C.StartPrim + C.ShiftSkipPay, e[1]);
            Assert.AreEqual(C.StartPrim, e[2]);
            Assert.AreEqual(C.StartPrim + C.ShiftSkipPay, e[3]);
        }

        [Test]
        public void Ledger_DeskShares_FiredCountsZero_IngestIsIdempotent()
        {
            var v = View(phase: LinePhase.FinalVote);
            v.Final = new LineFinalView { Round = 1 };
            var f = new LineFiringView { Player = 3, Round = 0, Desk = 9, Share = 4, Removed = 1 };
            f.Recipients.AddRange(new[] { 1, 2 });
            v.Final.Firings.Add(f);
            v.Players[3].Fired = true;
            var l = new LineBotLedger(0);
            l.Ingest(v);
            l.Ingest(v);
            var e = l.Estimate(C);
            Assert.AreEqual(C.StartPrim + 4, e[1]);
            Assert.AreEqual(C.StartPrim + 4, e[2]);
            Assert.AreEqual(0, e[3]);
        }

        [Test]
        public void Ledger_MyTransfers_MoveTheEstimate()
        {
            var v = View();
            v.Mine.Transfers.Add(new LineTransferView { From = 0, To = 1, Amount = 2 });
            v.Mine.Transfers.Add(new LineTransferView { From = 2, To = 0, Amount = 3 });
            v.Mine.Transfers.Add(new LineTransferView { From = 0, To = 1, Amount = 1 });
            var l = new LineBotLedger(0);
            l.Ingest(v);
            l.Ingest(v);
            var e = l.Estimate(C);
            Assert.AreEqual(C.StartPrim + 3, e[1]);
            Assert.AreEqual(C.StartPrim - 3, e[2]);
            Assert.AreEqual(C.StartPrim, e[3]);
        }

        [Test]
        public void Ledger_PrankCosts_FromKnownActorsOnly()
        {
            var v = View();
            v.Pranks = new LinePranksView();
            v.Pranks.Log.Add(new LinePrankEntryView { Kind = PrankKind.Coffee, Actor = 2, Target = 1, Cost = C.PrankCost(PrankKind.Coffee) });
            v.Pranks.Log.Add(new LinePrankEntryView { Kind = PrankKind.PhoneRing, Actor = -1, Target = 3, Cost = C.PrankCost(PrankKind.PhoneRing) });
            v.Pranks.Log.Add(new LinePrankEntryView { Kind = PrankKind.Slurp, Actor = 3, Cost = 0 });
            v.Pranks.Log.Add(new LinePrankEntryView { Kind = PrankKind.Plane, Actor = 2, Target = 3, Cost = C.PrankCost(PrankKind.Plane) });
            var e = Fed(v);
            Assert.AreEqual(C.StartPrim - C.PrankCost(PrankKind.Coffee) - C.PrankCost(PrankKind.Plane), e[2]);
            Assert.AreEqual(C.StartPrim, e[3], "a hidden phone ring is nobody's cost; slurping is free");
            Assert.AreEqual(C.StartPrim, e[1]);
        }

        [Test]
        public void Ledger_RemembersAcrossMeetings_ResetsPerWeek_IgnoresOtherViews()
        {
            var m1 = View();
            m1.Meeting = Meeting(0, D(DossierKind.Investigation, 2, 3, n1: 0));
            var m2 = View();
            m2.Meeting = Meeting(1);
            var e = Fed(m1, m2);
            Assert.AreEqual(C.StartPrim - C.InvestigationCost, e[2], "meeting 1's dossier is gone in meeting 2, the ledger remembers");
            var l = new LineBotLedger(0);
            l.Ingest(m1);
            l.Ingest(View(week: 2));
            Assert.AreEqual(C.StartPrim, l.Estimate(C)[2], "a new week starts fresh");
            var other = View(me: 1);
            other.Mine.Transfers.Add(new LineTransferView { From = 1, To = 2, Amount = 5 });
            l.Ingest(other);
            Assert.AreEqual(C.StartPrim, l.Estimate(C)[2], "another seat's view is ignored");
        }

        // ------------------------------------------------------------------ the richest pick

        static LineView VoteView(int me = 0, int players = 4, LinePhase phase = LinePhase.CrisisVote)
        {
            var v = View(me, players, phase: phase);
            v.Mine.Ballots.Add(-1);
            return v;
        }

        static int Cast(LineBot bot, LineView v)
        {
            var a = bot.Next(v, C, v.PhaseStartTime);
            Assert.IsNotNull(a, "the bot should vote");
            Assert.AreEqual(LineVerb.Vote, a.Verb);
            return a.Target;
        }

        [Test]
        public void Richest_EveryPolicyVotesTheEstimatedRichest_PreviewAgrees()
        {
            foreach (var policy in Rotation)
                for (int seed = 1; seed <= 60; seed++)
                {
                    var v = VoteView();
                    v.Mine.Transfers.Add(new LineTransferView { From = 0, To = 2, Amount = 1 }); // 2 is one prim ahead (> the noise)
                    var bot = new LineBot(0, seed, policy);
                    Assert.AreEqual(LineBotVoteStyle.Richest, bot.VoteStyle, "the default");
                    Assert.AreEqual(2, bot.PlannedVote(v, false), policy + " seed " + seed);
                    Assert.AreEqual(2, Cast(bot, v), policy + " seed " + seed);
                    Assert.AreEqual(2, bot.RichestRival(v));
                }
        }

        [Test]
        public void Richest_EqualEstimates_TheSeededNoiseDecides()
        {
            var picks = new HashSet<int>();
            for (int seed = 1; seed <= 80; seed++)
            {
                int a = Cast(new LineBot(0, seed, LineBotPolicy.Honest), VoteView());
                int b = Cast(new LineBot(0, seed, LineBotPolicy.Honest), VoteView());
                Assert.AreEqual(a, b, "same seed, same pick");
                Assert.AreNotEqual(0, a);
                picks.Add(a);
            }
            Assert.AreEqual(3, picks.Count, "equal estimates: bots disagree (each rival picked by some seed)");
        }

        [Test]
        public void Richest_FinalVote_OnlyAmongTheCandidates_CountMe()
        {
            var v = VoteView(phase: LinePhase.FinalVote);
            v.Final = new LineFinalView { Round = 0 };
            v.Final.Options.AddRange(new[] { 1, 3 });
            v.Final.Candidates.AddRange(new[] { 1, 3 });
            v.Mine.Transfers.Add(new LineTransferView { From = 0, To = 2, Amount = 5 }); // 2 is the richest, but not a candidate
            v.Mine.Transfers.Add(new LineTransferView { From = 0, To = 3, Amount = 2 });
            v.Mine.Prim = 1;
            var bot = new LineBot(0, 7, LineBotPolicy.Mixed);
            Assert.AreEqual(3, Cast(bot, v));
            Assert.AreEqual(2, bot.RichestRival(v));
            Assert.AreEqual(3, bot.RichestRival(v, new List<int> { 1, 3 }));
            v.Mine.Prim = 20;
            Assert.AreEqual(0, bot.RichestRival(v, null, countMe: true), "the bot believes it is the richest itself");
            var est = bot.EstimatedPrim(v);
            Assert.AreEqual(20, est[0]);
            Assert.AreEqual(C.StartPrim + 5, est[2]);
        }

        [Test]
        public void Richest_MeetingBallots_AllGoToTheRichest()
        {
            var v = View(phase: LinePhase.MeetingVote);
            v.Mine.Ballots.AddRange(new[] { -1, -1 });
            v.Mine.Transfers.Add(new LineTransferView { From = 0, To = 3, Amount = 2 });
            var bot = new LineBot(0, 11, LineBotPolicy.Greedy);
            var a = bot.Next(v, C, v.PhaseStartTime);
            Assert.AreEqual(3, a.Target);
            Assert.AreEqual(0, a.A);
            v.Mine.Ballots[0] = 3;
            var b = bot.Next(v, C, v.PhaseStartTime);
            Assert.AreEqual(3, b.Target);
            Assert.AreEqual(1, b.A);
        }

        // ------------------------------------------------------------------ MostBlame: the old rule, unchanged

        [Test]
        public void MostBlame_KnownCulpritElseHighestSuc_MoneyIgnored()
        {
            var v = VoteView();
            v.Players[3].Blame = 5;
            v.Mine.Transfers.Add(new LineTransferView { From = 0, To = 2, Amount = 5 });
            for (int seed = 1; seed <= 20; seed++)
            {
                var blame = new LineBot(0, seed, LineBotPolicy.Honest) { VoteStyle = LineBotVoteStyle.MostBlame };
                Assert.AreEqual(3, blame.PlannedVote(v, false));
                Assert.AreEqual(3, Cast(blame, v), "highest suç");
                Assert.AreEqual(2, Cast(new LineBot(0, seed, LineBotPolicy.Honest), v), "Richest: the money");
            }
            // a culprit the bot saw take in its own group comes first
            var w = VoteView();
            w.Players[3].Blame = 5;
            w.Task = Revealed(TaskKind.ClientBonus, new[] { 0, 1 }, new[] { LineChoice.Share, LineChoice.Take }, takers: 1);
            var bot = new LineBot(0, 3, LineBotPolicy.Greedy) { VoteStyle = LineBotVoteStyle.MostBlame };
            Assert.AreEqual(1, Cast(bot, w));
        }

        [Test]
        public void MostBlame_IgnoresAnnouncements()
        {
            var v = VoteView();
            v.Players[3].Blame = 5;
            var bot = new LineBot(0, 9, LineBotPolicy.Honest) { VoteStyle = LineBotVoteStyle.MostBlame };
            bot.CommitVote(v, false, 1);
            Assert.AreEqual(3, bot.PlannedVote(v, false));
            Assert.AreEqual(3, Cast(bot, v));
        }

        [Test]
        public void MostBlame_MatchesAreUnchangedByTheLedgerAndPreviews()
        {
            // the ledger is fed in both styles, previews ingest views: a MostBlame match must come out the same either way
            for (int n = 3; n <= 6; n++)
                for (int seed = 1; seed <= 3; seed++)
                {
                    var plain = Match(n, seed, LineBotVoteStyle.MostBlame, think: 6, previews: false);
                    var peeked = Match(n, seed, LineBotVoteStyle.MostBlame, think: 6, previews: true);
                    Assert.AreEqual(plain.Result.ResultHash, peeked.Result.ResultHash, n + "p seed " + seed);
                }
        }

        // ------------------------------------------------------------------ previews == the real vote

        sealed class Played
        {
            public LineWeekResult Result;
            /// <summary>Every preview taken before the bot cast that vote: "c:seat" (crisis), "f:round:seat" (final).</summary>
            public readonly Dictionary<string, List<int>> Previews = new Dictionary<string, List<int>>();
            public readonly List<string> Estimates = new List<string>();
        }

        static Played Match(int n, int seed, LineBotVoteStyle style, double think, bool previews, Func<int, LineBotPolicy> policy = null)
        {
            var setup = new LineSetup { WeekId = 1, MatchId = "vote-" + n + "-" + seed, Seed = seed };
            for (int i = 0; i < n; i++) setup.Players.Add(new LinePlayerInfo { Id = i, Name = "B" + i, IsBot = true });
            var engine = LineEngineFactory.Create(setup, C);
            var bots = new LineBot[n];
            var req = new uint[n];
            for (int i = 0; i < n; i++) bots[i] = new LineBot(i, seed * 31 + i * 97, policy != null ? policy(i) : Rotation[(i + seed) % 3]) { ThinkSeconds = think, VoteStyle = style };
            var played = new Played();
            double now = 0;
            for (int step = 0; step < 20000 && !engine.IsFinished; step++)
            {
                engine.Tick(now);
                for (int i = 0; i < n; i++)
                {
                    var v = engine.BuildView(i, now);
                    if (previews)
                    {
                        string key = null;
                        int planned = -2;
                        bool unvoted = v.Mine.Ballots.Count > 0 && v.Mine.Ballots[0] < 0;
                        if (v.Phase == LinePhase.CrisisCalls) { key = "c:" + i; planned = bots[i].PlannedVote(v, false, 1); }
                        else if (v.Phase == LinePhase.CrisisVote && unvoted) { key = "c:" + i; planned = bots[i].PlannedVote(v, false); }
                        else if (v.Phase == LinePhase.FinalVote && v.Final != null && !v.Final.Revote && unvoted) { key = "f:" + v.Final.Round + ":" + i; planned = bots[i].PlannedVote(v, true); }
                        if (key != null)
                        {
                            if (!played.Previews.TryGetValue(key, out var list)) played.Previews[key] = list = new List<int>();
                            list.Add(planned);
                        }
                        if (v.Phase == LinePhase.FinalDiscussion || v.Phase == LinePhase.MeetingDiscussion)
                            played.Estimates.Add(i + ":" + string.Join(",", bots[i].EstimatedPrim(v).OrderBy(x => x.Key).Select(x => x.Value.ToString("0.###"))));
                    }
                    var a = bots[i].Next(v, C, now);
                    if (a != null) engine.Apply(i, new LineAction(a.WeekId, ++req[i], a.Verb, a.Target, a.A, a.B, a.C, a.Text), now);
                }
                now += 0.5;
            }
            Assert.IsTrue(engine.IsFinished, "match did not finish");
            played.Result = engine.Result;
            return played;
        }

        [Test]
        public void PlannedVote_EqualsTheRealVote_InEveryPhaseBeforeIt([Values(LineBotVoteStyle.Richest, LineBotVoteStyle.MostBlame)] LineBotVoteStyle style)
        {
            int checkedVotes = 0;
            foreach (double think in new[] { 0.0, 4.0, 15.0 })
                for (int n = 3; n <= 6; n++)
                    for (int seed = 1; seed <= 5; seed++)
                    {
                        var p = Match(n, seed * 17 + n, style, think, previews: true);
                        foreach (var b in p.Result.Ballots)
                        {
                            string key = b.Kind == BallotKind.Crisis ? "c:" + b.Voter : b.Kind == BallotKind.Final && !b.Revote ? "f:" + b.Round + ":" + b.Voter : null;
                            if (key == null || b.SelfAssigned || !p.Previews.TryGetValue(key, out var list)) continue;
                            foreach (int planned in list)
                                Assert.AreEqual(b.Target, planned, style + " " + key + " think " + think + " n " + n + " seed " + seed);
                            checkedVotes++;
                        }
                    }
            Assert.Greater(checkedVotes, 200);
        }

        [Test]
        public void Richest_IsDeterministic_AndPreviewsDoNotChangeIt()
        {
            for (int n = 3; n <= 6; n++)
            {
                var a = Match(n, 40 + n, LineBotVoteStyle.Richest, 9, previews: true);
                var b = Match(n, 40 + n, LineBotVoteStyle.Richest, 9, previews: true);
                var c = Match(n, 40 + n, LineBotVoteStyle.Richest, 9, previews: false);
                Assert.AreEqual(a.Result.ResultHash, b.Result.ResultHash);
                Assert.AreEqual(a.Result.ResultHash, c.Result.ResultHash, "previews and estimates change nothing");
                CollectionAssert.AreEqual(a.Estimates, b.Estimates);
            }
        }

        [Test]
        public void Richest_FiresTheRichestMoreOftenThanMostBlame()
        {
            int richest = 0, blame = 0, total = 0;
            for (int seed = 1; seed <= 40; seed++)
            {
                foreach (var style in new[] { LineBotVoteStyle.Richest, LineBotVoteStyle.MostBlame })
                {
                    var p = Match(4, 300 + seed, style, 4, previews: false);
                    var fired = p.Result.Fired[0];
                    // the richest when the final vote started: start prim + every change before the firing
                    var firing = p.Result.PrimLog.Where(x => x.Reason == PrimReason.Fired).Select(x => x.Time).DefaultIfEmpty(double.MaxValue).Min();
                    var prim = Enumerable.Range(0, 4).Select(id => C.StartPrim + p.Result.PrimLog.Where(x => x.Player == id && x.Time < firing).Sum(x => x.Delta)).ToArray();
                    bool hit = prim[fired] == prim.Max();
                    if (style == LineBotVoteStyle.Richest) { if (hit) richest++; total++; }
                    else if (hit) blame++;
                }
            }
            Assert.Greater(richest, blame, "richest " + richest + " vs blame " + blame + " of " + total);
            Assert.Greater(richest, total / 2);
        }

        // ------------------------------------------------------------------ announced votes cannot change

        [Test]
        public void Announced_CrisisVote_SticksWhenTheEstimateChanges()
        {
            foreach (bool announce in new[] { true, false })
            {
                var t = new LineTest(4, 77, c => c.Durations.Scale(0.5f));
                var bot = new LineBot(1, 5, LineBotPolicy.Honest);
                t.Until(LinePhase.CrisisCalls);
                bot.Next(t.View(1), t.Config, t.Now);
                int x = bot.PlannedVote(t.View(1), false, 1);
                Assert.GreaterOrEqual(x, 0);
                if (announce) bot.CommitVote(t.View(1), false, x);
                // the announced colleague pays the bot: now they look poorer than the others
                t.Ok(x, LineVerb.Transfer, 1, 4);
                bot.Next(t.View(1), t.Config, t.Now);
                Assert.AreEqual(announce ? x : bot.RichestRival(t.View(1)), bot.PlannedVote(t.View(1), false, 1));
                Assert.AreNotEqual(x, bot.RichestRival(t.View(1)), "the estimate moved");
                t.Until(LinePhase.CrisisVote);
                LineAction a = null;
                for (int i = 0; i < 40 && a == null; i++) { a = bot.Next(t.View(1), t.Config, t.Now); if (a == null) t.Advance(0.5); }
                Assert.IsNotNull(a);
                if (announce) Assert.AreEqual(x, a.Target, "announced: the vote stays");
                else Assert.AreNotEqual(x, a.Target, "not announced: the bot follows its estimate");
            }
        }

        [Test]
        public void Announced_ByTheTalker_HonestAnswerBindsTheVote()
        {
            // seat 0 (a person) calls the Honest bot in the crisis calls and asks "who are you voting for?"; then the named
            // colleague pays the bot so it no longer looks the richest: the vote still goes where the bot said
            var t = new LineTest(4, 77, c => c.Durations.Scale(0.5f));
            var bot = new LineBot(1, 5, LineBotPolicy.Honest) { ThinkSeconds = 0 };
            var talker = new LineBotTalker(1, 9, LineBotPolicy.Honest, bot);
            var o = new BotTalkOutput();
            var said = new List<BotLine>();
            t.Until(LinePhase.CrisisCalls);
            void Tick()
            {
                var act = bot.Next(t.View(1), t.Config, t.Now);
                if (act != null) t.Ok(1, act.Verb, act.Target, act.A);
                o.Clear();
                talker.Tick(t.View(1), t.Config, t.Now, null, o);
                foreach (var a in o.Actions) if (a.Verb == LineVerb.Answer) t.Ok(1, a.Verb);
                said.AddRange(o.Said);
            }
            t.Ok(0, LineVerb.Call, 1);
            for (int i = 0; i < 40 && t.View(1).Mine.Phone.Status != CallStatus.Connected; i++) { Tick(); t.Advance(0.25); }
            Assert.AreEqual(CallStatus.Connected, t.View(1).Mine.Phone.Status);
            talker.Hear(new BotAsk { From = 0, To = 1, Set = BotQuestionSet.Crisis, Index = 0 });
            for (int i = 0; i < 16; i++) { Tick(); t.Advance(0.25); }
            var claim = said.FirstOrDefault(l => l.Claim == BotClaimKind.Vote);
            Assert.IsNotNull(claim, string.Join(", ", said));
            StringAssert.StartsWith("bot.crisis.a1.h_", claim.Key);
            Assert.IsTrue(claim.Truthful);
            int x = claim.ClaimValue;
            t.Ok(x, LineVerb.Transfer, 1, 4);
            Tick();
            Assert.AreNotEqual(x, bot.RichestRival(t.View(1)), "the estimate moved");
            t.Until(LinePhase.CrisisVote);
            for (int i = 0; i < 8; i++) { Tick(); t.Advance(0.25); }
            t.Until(LinePhase.CrisisVerdict);
            t.Finish();
            var ballot = t.Engine.Result.Ballots.Single(b => b.Kind == BallotKind.Crisis && b.Voter == 1);
            Assert.AreEqual(x, ballot.Target, "the announced vote");
        }

        [Test]
        public void Announced_HonestKeepsItsWord_WhenTheAskerBecomesTheRichest()
        {
            // the Honest bot tells seat 0 its vote (X), then everyone but seat 0 pays it: seat 0 now looks the richest and
            // asks "don't vote for me": the bot says it keeps its word (true) and votes X
            int tested = 0;
            for (int botSeed = 1; botSeed <= 12 && tested < 2; botSeed++)
            {
                var t = new LineTest(4, 77, c => c.Durations.Scale(0.5f));
                var bot = new LineBot(1, botSeed, LineBotPolicy.Honest) { ThinkSeconds = 0 };
                var talker = new LineBotTalker(1, 9, LineBotPolicy.Honest, bot);
                var o = new BotTalkOutput();
                var said = new List<BotLine>();
                t.Until(LinePhase.CrisisCalls);
                void Tick()
                {
                    var act = bot.Next(t.View(1), t.Config, t.Now);
                    if (act != null) t.Ok(1, act.Verb, act.Target, act.A);
                    o.Clear();
                    talker.Tick(t.View(1), t.Config, t.Now, null, o);
                    foreach (var a in o.Actions) if (a.Verb == LineVerb.Answer) t.Ok(1, a.Verb);
                    said.AddRange(o.Said);
                }
                Tick();
                if (bot.PlannedVote(t.View(1), false, 1) == 0) continue; // the asker must not be the target
                t.Ok(0, LineVerb.Call, 1);
                for (int i = 0; i < 40 && t.View(1).Mine.Phone.Status != CallStatus.Connected; i++) { Tick(); t.Advance(0.25); }
                talker.Hear(new BotAsk { From = 0, To = 1, Set = BotQuestionSet.Crisis, Index = 0 });
                for (int i = 0; i < 12; i++) { Tick(); t.Advance(0.25); }
                var vote = said.First(l => l.Claim == BotClaimKind.Vote);
                int x = vote.ClaimValue;
                Assert.AreNotEqual(0, x);
                foreach (int id in new[] { 2, 3 }) t.Ok(id, LineVerb.Transfer, 1, 3);
                Tick();
                Assert.AreEqual(0, bot.RichestRival(t.View(1)), "seat 0 now looks the richest");
                talker.Hear(new BotAsk { From = 0, To = 1, Set = BotQuestionSet.Crisis, Index = 1 });
                for (int i = 0; i < 12; i++) { Tick(); t.Advance(0.25); }
                var answer = said.LastOrDefault(l => l.Claim == BotClaimKind.NotVote);
                Assert.IsNotNull(answer, string.Join(", ", said));
                Assert.AreEqual("bot.crisis.a2.h_said", answer.Key);
                Assert.IsTrue(answer.Truthful);
                t.Until(LinePhase.CrisisVote);
                for (int i = 0; i < 6; i++) { Tick(); t.Advance(0.25); }
                t.Finish();
                Assert.AreEqual(x, t.Engine.Result.Ballots.Single(b => b.Kind == BallotKind.Crisis && b.Voter == 1).Target);
                tested++;
            }
            Assert.Greater(tested, 0);
        }

        [Test]
        public void Talkers_ExplainVotesByMoney_AndLogEveryBallot()
        {
            int rich = 0, honestRich = 0, voteLines = 0;
            for (int n = 3; n <= 6; n++)
                for (int seed = 0; seed < 3; seed++)
                {
                    var setup = new LineSetup { WeekId = 1, MatchId = "money-" + n + "-" + seed, Seed = 6000 + n * 10 + seed };
                    for (int i = 0; i < n; i++) setup.Players.Add(new LinePlayerInfo { Id = i, Name = "Bot " + i, ColorIndex = i, IsBot = true });
                    var config = LineConfig.CreateDefault();
                    config.Durations.Scale(0.3f);
                    var engine = LineEngineFactory.Create(setup, config);
                    var bots = new LineBot[n];
                    var talkers = new LineBotTalker[n];
                    var req = new uint[n];
                    for (int i = 0; i < n; i++)
                    {
                        bots[i] = new LineBot(i, setup.Seed * 31 + i, Rotation[(i + seed) % 3]) { ThinkSeconds = 4 };
                        talkers[i] = new LineBotTalker(i, setup.Seed * 17 + i, Rotation[(i + seed) % 3], bots[i]);
                    }
                    var o = new BotTalkOutput();
                    var lines = new List<BotLine>();
                    var log = new List<string>();
                    double now = 0;
                    void Apply(int seat, LineAction a) => engine.Apply(seat, new LineAction(a.WeekId, ++req[seat], a.Verb, a.Target, a.A, a.B, a.C, a.Text), now);
                    for (int step = 0; step < 20000 && !engine.IsFinished; step++)
                    {
                        engine.Tick(now);
                        for (int i = 0; i < n; i++)
                        {
                            var v = engine.BuildView(i, now);
                            var a = bots[i].Next(v, config, now);
                            if (a != null) Apply(i, a);
                            o.Clear();
                            talkers[i].Tick(engine.BuildView(i, now), config, now, engine.MessagesFor(i, talkers[i].MessagesSeen), o);
                            foreach (var x in o.Actions) Apply(i, x);
                            foreach (var q in o.Asks) talkers[q.To].Hear(q);
                            lines.AddRange(o.Said);
                            log.AddRange(o.Log);
                        }
                        now += 0.25;
                    }
                    Assert.IsTrue(engine.IsFinished);
                    for (int i = 0; i < n; i++)
                    {
                        o.Clear();
                        talkers[i].Tick(engine.BuildView(i, now), config, now, null, o);
                        log.AddRange(o.Log);
                    }
                    var r = engine.Result;
                    foreach (var l in lines)
                    {
                        Assert.IsFalse(l.Key == "bot.vote.h" || l.Key == "bot.vote.h_known" || l.Key.StartsWith("bot.crisis.call.h_known", StringComparison.Ordinal) || l.Key == "bot.crisis.call.h",
                            "Richest bots do not explain by the suç board: " + l);
                        if (l.Key.StartsWith("bot.meet.rich", StringComparison.Ordinal) || l.Key.EndsWith("_rich", StringComparison.Ordinal) || l.Key.EndsWith("_win", StringComparison.Ordinal))
                        {
                            rich++;
                            if (l.Policy == LineBotPolicy.Honest) { honestRich++; Assert.IsTrue(l.Truthful, l.ToString()); }
                            Assert.AreNotEqual(l.Speaker, l.Args.Count > 0 ? l.Args[0] : -1, "names itself: " + l);
                        }
                    }
                    Assert.IsFalse(log.Any(x => x.Contains(" ok=no")), "a claim did not match: " + string.Join("\n", log.Where(x => x.Contains(" ok=no"))));
                    // one vote line per own ballot (self-assigned ones aside)
                    for (int i = 0; i < n; i++)
                    {
                        int ballots = r.Ballots.Count(b => b.Voter == i && !b.SelfAssigned && b.Target >= 0);
                        int logged = log.Count(x => x.StartsWith("bottalk vote seat=" + i + " ", StringComparison.Ordinal));
                        Assert.AreEqual(ballots, logged, "seat " + i);
                        voteLines += logged;
                    }
                }
            Assert.Greater(rich, 20, "money lines");
            Assert.Greater(honestRich, 5);
            Assert.Greater(voteLines, 30);
        }
    }
}
