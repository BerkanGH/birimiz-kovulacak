using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BirimizKovulacak.Core.Tests.Line;

namespace BirimizKovulacak.Core.Tests
{
    /// <summary>
    /// v5.2 results story (docs/V5_DESIGN.md §5, docs/V5_ENGINE_SPEC.md §22): <see cref="LineStory.Build"/> turns the full
    /// disclosure into at most 4 awards (fixed priority, one per player where possible) and at most 5 headlines. Pure and
    /// deterministic; the rules are tested on hand-made results, then on real bot matches.
    /// </summary>
    [TestFixture]
    public sealed class LineStoryTests
    {
        const TaskKind Bonus = TaskKind.ClientBonus, Cash = TaskKind.PettyCash, Shift = TaskKind.WeekendShift;
        const int Take = LineChoice.Take, Share = LineChoice.Share, Volunteer = LineChoice.Volunteer, Skip = LineChoice.Skip;

        /// <summary>A hand-made result: everyone has 9 prim and 1 suç (so nobody has a clean record by accident).</summary>
        sealed class Made
        {
            public readonly LineWeekResult R = new LineWeekResult();

            public Made(int players, int suc = 1)
            {
                R.PlayerCount = players;
                for (int i = 0; i < players; i++) R.Players.Add(new LineWeekPlayerResult { Player = i, Prim = 9, Blame = suc });
            }

            public Made Prim(int id, int prim) { R.Players[id].Prim = prim; return this; }
            public Made Suc(int id, int total) { R.Players[id].Blame = total; return this; }
            public Made Blame(int id, int delta, BlameReason reason, double time = 100) { R.BlameLog.Add(new LineBlameChange { Player = id, Delta = delta, Reason = reason, Time = time }); return this; }
            public Made Group(TaskKind task, params int[] members) { R.Groups.Add(new LineGroupView { Task = task, TaskNumber = task == Bonus ? 0 : 1, Members = members.ToList() }); return this; }
            public Made Choice(TaskKind task, int id, int choice, int payout) { R.Choices.Add(new LineChoiceView { Task = task, TaskNumber = (int)task - 1, Player = id, Choice = choice, Payout = payout }); return this; }
            public Made Send(int from, int to, int amount, LinePhase phase, double time) { R.Transfers.Add(new LineTransferView { From = from, To = to, Amount = amount, Phase = phase, Time = time }); return this; }
            public Made Whistle(int by, int thief, double time) { R.Incidents.Add(new LineDossierEntry { Kind = DossierKind.Whistle, Player = by, Other = thief, N1 = 3, Time = time }); return this; }
            public Made Investigate(int by, int target, double time) { R.Incidents.Add(new LineDossierEntry { Kind = DossierKind.Investigation, Player = by, Other = target, N1 = 1, Time = time }); return this; }
            public Made Leak(int by, double time) { R.Incidents.Add(new LineDossierEntry { Kind = DossierKind.Leak, Player = by, Other = 0, N1 = 0, N2 = 1, Time = time }); return this; }
            public Made Vote(BallotKind kind, int voter, int target, int round = 0, bool self = false) { R.Ballots.Add(new LineBallotView { Kind = kind, Voter = voter, Target = target, Round = round, SelfAssigned = self }); return this; }
            public Made Declare(int id, int amount, int actual) { R.Declarations.Add(new LineDeclarationView { Player = id, Amount = amount, Actual = actual }); return this; }

            public Made Fire(int id, int desk, int declared = -1, int round = 0, double time = 500)
            {
                R.Firings.Add(new LineFiringView { Player = id, Desk = desk, Declared = declared, Round = round, Time = time });
                R.Fired.Add(id);
                R.Players[id].Fired = true;
                R.Players[id].Prim = 0;
                return this;
            }

            /// <summary>A pair that shared the Client Bonus / left the cash / etc. is implied; these set the interesting ones.</summary>
            public Made Robbery(int taker, params int[] victims)
            {
                Group(Bonus, new[] { taker }.Concat(victims).ToArray());
                Choice(Bonus, taker, Take, 5);
                foreach (int v in victims) Choice(Bonus, v, Share, 0);
                return this;
            }

            public Made Theft(int thief, params int[] witnesses)
            {
                Group(Cash, new[] { thief }.Concat(witnesses).ToArray());
                Choice(Cash, thief, Take, 3);
                foreach (int w in witnesses) Choice(Cash, w, LineChoice.Leave, 0);
                return this;
            }

            public LineStory Story() { return LineStory.Build(R, LineConfig.CreateDefault()); }
            public LineAward Award(AwardKind kind) { return Story().Awards.SingleOrDefault(a => a.Kind == kind); }
            public List<LineHeadline> Heads(HeadlineKind kind) { return Story().Headlines.Where(h => h.Kind == kind).ToList(); }
        }

        static string Kinds(LineStory s) { return string.Join(",", s.Awards.Select(a => a.Kind + ":" + a.Player)); }

        // ------------------------------------------------------------------ the awards, one by one

        [Test]
        public void NothingHappened_NoAwardsNoHeadlines()
        {
            var story = new Made(4).Story();
            CollectionAssert.IsEmpty(story.Awards);
            CollectionAssert.IsEmpty(story.Headlines);
            Assert.IsNull(story.AwardOf(0));
            Assert.AreEqual(0, LineStory.Build(null, null).Awards.Count);
            // a result with missing lists (hand-made, older) never throws
            var bare = new LineWeekResult { Players = new List<LineWeekPlayerResult> { new LineWeekPlayerResult { Player = 0, Blame = 2 }, null }, Firings = null, Ballots = null,
                Transfers = null, Choices = null, Incidents = null, PrimLog = null, BlameLog = null, Declarations = null, Groups = null, Fired = null, Winners = null };
            Assert.DoesNotThrow(() => LineStory.Build(bare, null));
        }

        [Test]
        public void WrongMan_FiredAlthoughTheDeclarationWasTrue()
        {
            var a = new Made(4).Declare(1, 9, 9).Fire(1, desk: 9, declared: 9).Award(AwardKind.WrongMan);
            Assert.AreEqual(1, a.Player);
            Assert.AreEqual(9, a.N1);
            Assert.IsNull(new Made(4).Fire(1, desk: 9, declared: 7).Award(AwardKind.WrongMan), "a false declaration");
            Assert.IsNull(new Made(4).Fire(1, desk: 9).Award(AwardKind.WrongMan), "no declaration");
            Assert.IsNull(new Made(4).Declare(1, 9, 9).Award(AwardKind.WrongMan), "not fired");
            // two firings: the first one fired takes it
            var two = new Made(6).Fire(4, 3, 3, round: 0, time: 500).Fire(2, 8, 8, round: 1, time: 540).Story();
            Assert.AreEqual(4, two.Awards.First(x => x.Kind == AwardKind.WrongMan).Player);
        }

        [Test]
        public void Liar_TheBiggestUnderstatementOfAtLeastThree()
        {
            var a = new Made(4).Declare(0, 7, 16).Declare(1, 5, 9).Award(AwardKind.Liar);
            Assert.AreEqual(0, a.Player);
            CollectionAssert.AreEqual(new[] { 7, 16, 9 }, new[] { a.N1, a.N2, a.N3 });
            Assert.IsNull(new Made(4).Declare(0, 7, 9).Award(AwardKind.Liar), "2 under is not enough");
            Assert.IsNotNull(new Made(4).Declare(0, 6, 9).Award(AwardKind.Liar), "3 under is");
            Assert.IsNull(new Made(4).Declare(0, -1, 20).Award(AwardKind.Liar), "no declaration is not a lie");
            Assert.IsNull(new Made(4).Declare(0, 20, 9).Award(AwardKind.Liar), "an overstatement is not this award");
            Assert.AreEqual(2, new Made(4).Declare(1, 5, 10).Declare(2, 7, 12).Award(AwardKind.Liar).Player, "the same gap: the richer one");
            Assert.AreEqual(1, new Made(4).Declare(2, 5, 10).Declare(1, 5, 10).Award(AwardKind.Liar).Player, "all equal: the lower seat");
        }

        [Test]
        public void Sellout_KeptTheMoneyAndThenPunishedOrVotedAgainstThePayer()
        {
            // silence money, then the whistle anyway
            var a = new Made(4).Theft(1, 0).Send(1, 0, 2, LinePhase.Break, 50).Whistle(0, 1, 60).Award(AwardKind.Sellout);
            Assert.AreEqual(0, a.Player);
            Assert.AreEqual(1, a.Other);
            Assert.AreEqual(2, a.N1);
            Assert.AreEqual(LineStory.BreakWhistle, a.N2);
            Assert.AreEqual(LineStory.BreakInvestigation, new Made(4).Send(1, 0, 1, LinePhase.Break, 50).Investigate(0, 1, 90).Award(AwardKind.Sellout).N2);
            // sent back: nothing was kept (Honest returns silence money and punishes)
            Assert.IsNull(new Made(4).Send(1, 0, 2, LinePhase.Break, 50).Send(0, 1, 2, LinePhase.Break, 55).Whistle(0, 1, 60).Award(AwardKind.Sellout));
            Assert.AreEqual(1, new Made(4).Send(1, 0, 2, LinePhase.Break, 50).Send(0, 1, 1, LinePhase.Break, 55).Whistle(0, 1, 60).Award(AwardKind.Sellout).N1, "what was kept");
            // paid only after the punishment: no deal was broken
            Assert.IsNull(new Made(4).Whistle(0, 1, 60).Send(1, 0, 2, LinePhase.MeetingDossier, 70).Award(AwardKind.Sellout));
            // the crisis vote: money before the vote, then the ballot against the payer
            var crisis = new Made(4).Send(1, 0, 3, LinePhase.CrisisCalls, 300).Vote(BallotKind.Crisis, 0, 1);
            Assert.AreEqual(LineStory.BreakCrisisVote, crisis.Award(AwardKind.Sellout).N2);
            Assert.IsNull(new Made(4).Send(1, 0, 3, LinePhase.CrisisCalls, 300).Vote(BallotKind.Crisis, 0, 2).Award(AwardKind.Sellout), "voted for someone else");
            Assert.IsNull(new Made(4).Send(1, 0, 3, LinePhase.FinalDiscussion, 400).Vote(BallotKind.Crisis, 0, 1).Award(AwardKind.Sellout), "paid after the crisis vote");
            Assert.IsNull(new Made(4).Send(1, 0, 3, LinePhase.CrisisCalls, 300).Vote(BallotKind.Crisis, 1, 1, self: true).Award(AwardKind.Sellout), "an empty ballot is nobody's vote");
            // the firing vote: money, then the vote that fires the payer
            var final = new Made(4).Send(1, 0, 3, LinePhase.FinalDefense, 430).Vote(BallotKind.Final, 0, 1).Fire(1, 12, time: 470);
            Assert.AreEqual(LineStory.BreakFinalVote, final.Award(AwardKind.Sellout).N2);
            // two firing rounds: money during the second vote does not make the first ballot a betrayal
            var late = new Made(6).Vote(BallotKind.Final, 0, 1, round: 0).Fire(1, 5, round: 0, time: 470).Send(1, 0, 2, LinePhase.FinalVote, 480).Fire(2, 5, round: 1, time: 510);
            Assert.IsNull(late.Award(AwardKind.Sellout));
            var second = new Made(6).Fire(1, 5, round: 0, time: 470).Send(2, 0, 2, LinePhase.FinalVote, 480).Vote(BallotKind.Final, 0, 2, round: 1).Fire(2, 5, round: 1, time: 510);
            Assert.AreEqual(0, second.Award(AwardKind.Sellout).Player);
            // several: whoever kept the most
            var many = new Made(5).Send(1, 0, 1, LinePhase.Break, 50).Whistle(0, 1, 60).Send(3, 2, 4, LinePhase.Break, 50).Whistle(2, 3, 60).Story();
            Assert.AreEqual(2, many.Awards.Single(x => x.Kind == AwardKind.Sellout).Player);
        }

        [Test]
        public void Loyal_KeptSilenceMoneyAndNeverTurnedOnThePayer()
        {
            var a = new Made(4).Theft(1, 0).Send(1, 0, 1, LinePhase.Break, 50).Award(AwardKind.Loyal);
            Assert.AreEqual(0, a.Player);
            Assert.AreEqual(1, a.Other);
            Assert.AreEqual(1, a.N1);
            Assert.AreEqual(1, new Made(4).Robbery(2, 1).Send(2, 1, 2, LinePhase.Break, 50).Award(AwardKind.Loyal).Player, "the Client Bonus victim who did not investigate");
            Assert.IsNull(new Made(4).Theft(1, 0).Send(1, 0, 1, LinePhase.Break, 50).Whistle(0, 1, 60).Award(AwardKind.Loyal), "whistled anyway: a sellout");
            Assert.IsNull(new Made(4).Theft(1, 0).Send(1, 0, 1, LinePhase.Break, 50).Send(0, 1, 1, LinePhase.Break, 52).Award(AwardKind.Loyal), "sent the money back");
            Assert.IsNull(new Made(4).Theft(1, 0).Send(1, 0, 1, LinePhase.Break, 50).Vote(BallotKind.Crisis, 0, 1).Award(AwardKind.Loyal), "then voted the payer into the crisis");
            Assert.IsNull(new Made(4).Send(1, 0, 3, LinePhase.Break, 50).Award(AwardKind.Loyal), "a gift from someone it could not punish is not silence money");
            Assert.IsNull(new Made(4).Theft(1, 0).Theft(2, 3).Send(2, 0, 1, LinePhase.Break, 50).Award(AwardKind.Loyal), "not a witness of that theft");
            // a trio: the second witness whistled, the first kept its word
            var trio = new Made(5).Theft(1, 0, 2).Send(1, 0, 1, LinePhase.Break, 50).Whistle(2, 1, 60);
            Assert.AreEqual(0, trio.Award(AwardKind.Loyal).Player);
        }

        [Test]
        public void RobberAndCashRat()
        {
            var a = new Made(4).Robbery(3, 2).Award(AwardKind.Robber);
            Assert.AreEqual(3, a.Player);
            Assert.AreEqual(2, a.Other);
            Assert.AreEqual(5, a.N1);
            Assert.AreEqual(1, a.N2);
            var both = new Made(4).Group(Bonus, 0, 1).Choice(Bonus, 0, Take, 1).Choice(Bonus, 1, Take, 1);
            Assert.IsNull(both.Award(AwardKind.Robber), "both took: nobody was robbed");
            // a trio's only taker robbed two: ahead of a pair's robber
            var story = new Made(5).Robbery(4, 3).Robbery(1, 0, 2).Story();
            var robber = story.Awards.Single(x => x.Kind == AwardKind.Robber);
            Assert.AreEqual(1, robber.Player);
            Assert.AreEqual(0, robber.Other);
            Assert.AreEqual(2, robber.N2);
            // without group data the payout tells (no victim is named)
            var bare = new Made(4).Choice(Bonus, 2, Take, 5).Award(AwardKind.Robber);
            Assert.AreEqual(2, bare.Player);
            Assert.AreEqual(-1, bare.Other);
            Assert.IsNull(new Made(4).Choice(Bonus, 2, Take, 1).Award(AwardKind.Robber));

            var rat = new Made(4).Theft(2, 3).Award(AwardKind.CashRat);
            Assert.AreEqual(2, rat.Player);
            Assert.AreEqual(3, rat.N1);
            Assert.IsNull(new Made(4).Theft(2, 3).Whistle(3, 2, 60).Award(AwardKind.CashRat), "whistled on");
            Assert.IsNull(new Made(4).Group(Cash, 2, 3).Choice(Cash, 2, Take, 0).Choice(Cash, 3, Take, 0).Award(AwardKind.CashRat), "both took: caught, nothing kept");
            Assert.AreEqual(1, new Made(4).Theft(0, 2).Theft(1, 3).Prim(1, 14).Award(AwardKind.CashRat).Player, "several: the richest");
        }

        [Test]
        public void SnitchScapegoatBlackBoxTutanak()
        {
            var snitch = new Made(5).Whistle(3, 1, 60).Investigate(3, 2, 90).Whistle(0, 4, 55).Award(AwardKind.Snitch);
            Assert.AreEqual(3, snitch.Player);
            Assert.AreEqual(2, snitch.N1);
            Assert.AreEqual(0, new Made(5).Whistle(3, 1, 60).Whistle(0, 4, 55).Award(AwardKind.Snitch).Player, "a tie: whoever opened first");
            Assert.IsNull(new Made(5).Leak(2, 70).Award(AwardKind.Snitch), "a leak is not a punishment");

            var goat = new Made(4).Blame(2, 4, BlameReason.Crisis).Vote(BallotKind.Crisis, 0, 2).Vote(BallotKind.Crisis, 1, 2).Vote(BallotKind.Crisis, 3, 2).Vote(BallotKind.Crisis, 2, 0).Award(AwardKind.Scapegoat);
            Assert.AreEqual(2, goat.Player);
            Assert.AreEqual(4, goat.N1);
            Assert.AreEqual(3, goat.N2);
            Assert.IsNull(new Made(4).Blame(2, 2, BlameReason.Crisis).Blame(3, 2, BlameReason.Crisis).Award(AwardKind.Scapegoat), "a split crisis has no scapegoat");
            Assert.IsNull(new Made(4).Blame(2, 4, BlameReason.Vote).Award(AwardKind.Scapegoat), "meeting ballots are not the crisis");

            Assert.AreEqual(1, new Made(4).Leak(1, 70).Award(AwardKind.BlackBox).Player);
            var king = new Made(4).Blame(3, 1, BlameReason.Tutanak, 200).Blame(0, 1, BlameReason.Tutanak, 150).Blame(3, 1, BlameReason.Tutanak, 400).Award(AwardKind.Tutanak);
            Assert.AreEqual(3, king.Player);
            Assert.AreEqual(2, king.N1);
        }

        [Test]
        public void DodgerAndCleanRecord()
        {
            Func<Made> staffed = () => new Made(4).Choice(Shift, 0, Volunteer, 0).Choice(Shift, 1, Volunteer, 0).Choice(Shift, 2, Skip, 2).Choice(Shift, 3, Skip, 2);
            Assert.AreEqual(2, staffed().Award(AwardKind.Dodger).Player, "equal prim: the lower seat");
            var richer = staffed().Prim(3, 12).Award(AwardKind.Dodger);
            Assert.AreEqual(3, richer.Player);
            Assert.AreEqual(2, richer.N1);
            Assert.AreEqual(3, staffed().Fire(2, 9).Award(AwardKind.Dodger).Player, "a fired skipper kept nothing");
            var shortShift = new Made(4).Choice(Shift, 0, Volunteer, 0).Choice(Shift, 1, Skip, 0).Choice(Shift, 2, Skip, 0).Choice(Shift, 3, Skip, 0);
            Assert.IsNull(shortShift.Award(AwardKind.Dodger), "a short shift pays nobody");

            var clean = new Made(4).Suc(1, 0).Suc(2, 0).Prim(2, 13).Award(AwardKind.Clean);
            Assert.AreEqual(2, clean.Player, "several: the richest");
            Assert.AreEqual(13, clean.N1);
            Assert.IsNull(new Made(4).Suc(1, 0).Fire(1, 9).Award(AwardKind.Clean), "fired");
        }

        // ------------------------------------------------------------------ choosing the four

        [Test]
        public void PriorityOrder_OnePerPlayer_AtMostFour()
        {
            // 0: liar + robber; 1: snitch + clean record; 2: scapegoat; 3: dodger and clean record too
            var m = new Made(4).Declare(0, 5, 12).Robbery(0, 1).Investigate(1, 0, 90).Suc(1, 0).Blame(2, 4, BlameReason.Crisis)
                .Choice(Shift, 0, Volunteer, 0).Choice(Shift, 1, Volunteer, 0).Choice(Shift, 2, Skip, 2).Choice(Shift, 3, Skip, 2).Suc(3, 0).Prim(3, 11);
            var story = m.Story();
            Assert.AreEqual("Liar:0,Snitch:1,Scapegoat:2,Dodger:3", Kinds(story), "the robber's second award and the clean records lose to a free seat");
            Assert.AreEqual(LineStory.MaxAwards, story.Awards.Count);
            Assert.AreEqual(AwardKind.Liar, story.AwardOf(0).Kind);
            Assert.IsNull(story.AwardOf(5));
            // priority is the enum order
            CollectionAssert.AreEqual(Enumerable.Range(1, LineAwards.Count), Enum.GetValues(typeof(AwardKind)).Cast<AwardKind>().Where(k => k != AwardKind.None).Select(k => (int)k));
            Assert.AreEqual(LineAwards.Count, Enumerable.Range(1, LineAwards.Count).Select(k => LineAwards.Id((AwardKind)k)).Distinct().Count(x => x.Length > 0));
        }

        [Test]
        public void TheNextCandidateTakesAnAwardWhoseBestCandidateAlreadyHasOne()
        {
            // 0 is the biggest liar and the richest unwhistled cash thief; 1 is the other cash thief
            var story = new Made(4).Declare(0, 5, 14).Prim(0, 14).Theft(0, 2).Theft(1, 3).Story();
            Assert.AreEqual("Liar:0,CashRat:1", Kinds(story));
        }

        [Test]
        public void LeftoverAwardsFillTheFreePlaces_ASecondAwardOnlyWhenNobodyElseQualifies()
        {
            var story = new Made(3).Declare(0, 5, 14).Robbery(0, 1).Theft(0, 2).Story();
            Assert.AreEqual("Liar:0,Robber:0,CashRat:0", Kinds(story), "one player did everything: where possible, not always");
            Assert.AreEqual(AwardKind.Liar, story.AwardOf(0).Kind, "the title on a player's card is the highest one");
            // with five single-owner awards the fifth is dropped, never a fourth distinct player's
            var many = new Made(6).Declare(0, 5, 14).Robbery(1, 0).Theft(2, 3).Whistle(4, 5, 60).Blame(5, 4, BlameReason.Crisis).Leak(3, 70).Story();
            Assert.AreEqual("Liar:0,Robber:1,CashRat:2,Snitch:4", Kinds(many));
        }

        // ------------------------------------------------------------------ headlines

        [Test]
        public void Headlines_OneOfEachKindFirstThenMoreInPriorityOrder()
        {
            var m = new Made(6)
                .Send(1, 0, 1, LinePhase.Break, 50).Whistle(0, 1, 60)              // a broken deal (1 prim)
                .Send(3, 2, 2, LinePhase.Break, 50).Investigate(2, 3, 90)          // a bigger one
                .Robbery(4, 5).Robbery(1, 0)                                       // two betrayals
                .Fire(4, 11, declared: 6, round: 0, time: 470).Fire(3, 4, round: 1, time: 510)
                .Blame(4, 4, BlameReason.Crisis).Vote(BallotKind.Crisis, 0, 4).Vote(BallotKind.Crisis, 1, 4)
                .Send(5, 2, 4, LinePhase.CrisisCalls, 300);
            var h = m.Story().Headlines;
            CollectionAssert.AreEqual(new[] { HeadlineKind.DealBroken, HeadlineKind.BonusBetrayal, HeadlineKind.Firing, HeadlineKind.Crisis, HeadlineKind.BigTransfer }, h.Select(x => x.Kind));
            Assert.AreEqual(LineStory.MaxHeadlines, h.Count);
            // the bigger deal leads
            Assert.AreEqual(2, h[0].Player);
            Assert.AreEqual(3, h[0].Other);
            Assert.AreEqual(2, h[0].N1);
            Assert.AreEqual(LineStory.BreakInvestigation, h[0].N2);
            // betrayals in seat order: 1 robbed 0
            Assert.AreEqual(1, h[1].Player);
            Assert.AreEqual(0, h[1].Other);
            Assert.AreEqual(-1, h[1].Other2);
            Assert.AreEqual(5, h[1].N1);
            // the first firing, with its desk and declaration
            CollectionAssert.AreEqual(new[] { 4, 11, 6, 0 }, new[] { h[2].Player, h[2].N1, h[2].N2, h[2].N3 });
            CollectionAssert.AreEqual(new[] { 4, 4, 1, 2 }, new[] { h[3].Player, h[3].N1, h[3].N2, h[3].N3 });
            CollectionAssert.AreEqual(new[] { 5, 2, 4 }, new[] { h[4].Player, h[4].Other, h[4].N1 });
        }

        [Test]
        public void Headlines_FewKindsShowMoreOfEach()
        {
            var h = new Made(6).Fire(4, 11, 6, 0, 470).Fire(3, 4, -1, 1, 510).Blame(1, 2, BlameReason.Crisis).Blame(2, 2, BlameReason.Crisis).Story().Headlines;
            CollectionAssert.AreEqual(new[] { HeadlineKind.Firing, HeadlineKind.Firing, HeadlineKind.Crisis }, h.Select(x => x.Kind));
            Assert.AreEqual(4, h[0].Player);
            Assert.AreEqual(3, h[1].Player);
            Assert.AreEqual(-1, h[1].N2, "no declaration");
            Assert.AreEqual(-1, h[2].Player, "a split crisis names nobody");
            Assert.AreEqual(2, h[2].N1);
            Assert.AreEqual(2, h[2].N2);
            // a trio's betrayal names both victims; a 1-prim transfer is no headline, the earliest of the biggest is
            var t = new Made(5).Robbery(2, 4, 0).Send(1, 3, 1, LinePhase.Break, 50).Story().Headlines;
            Assert.AreEqual(HeadlineKind.BonusBetrayal, t.Single().Kind);
            CollectionAssert.AreEqual(new[] { 2, 0, 4 }, new[] { t[0].Player, t[0].Other, t[0].Other2 });
            var big = new Made(5).Send(1, 3, 3, LinePhase.Break, 80).Send(0, 2, 3, LinePhase.Break, 50).Send(4, 1, 2, LinePhase.Intro, 5).Story().Headlines.Single();
            CollectionAssert.AreEqual(new[] { 0, 2, 3 }, new[] { big.Player, big.Other, big.N1 });
            CollectionAssert.AreEqual(new[] { "deal", "bonus", "firing", "crisis", "transfer" },
                new[] { HeadlineKind.DealBroken, HeadlineKind.BonusBetrayal, HeadlineKind.Firing, HeadlineKind.Crisis, HeadlineKind.BigTransfer }.Select(LineHeadlines.Id));
        }

        [Test]
        public void TheSameResultAlwaysGivesTheSameStory_WhateverTheListOrder()
        {
            Func<Made> make = () => new Made(6).Declare(0, 5, 14).Declare(3, 9, 9).Robbery(1, 0).Theft(2, 3).Whistle(4, 5, 60).Send(5, 4, 2, LinePhase.Break, 50)
                .Blame(5, 4, BlameReason.Crisis).Leak(3, 70).Fire(3, 9, 9).Suc(4, 0);
            Func<LineStory, string> sig = s => Kinds(s) + "|" + string.Join(",", s.Headlines.Select(x => x.Kind + ":" + x.Player + ">" + x.Other + "=" + x.N1 + "/" + x.N2));
            var a = make();
            var b = make();
            b.R.Players.Reverse();
            b.R.Incidents.Reverse();
            b.R.Choices.Reverse();
            b.R.Groups.Reverse();
            b.R.Declarations.Reverse();
            Assert.AreEqual(sig(a.Story()), sig(b.Story()));
            Assert.AreEqual(sig(a.Story()), sig(a.Story()));
            Assert.AreEqual(sig(a.Story()), sig(LineStory.Build(LineCodec.DecodeView(LineCodec.EncodeView(new LineView { Results = a.R })).Results, LineConfig.CreateDefault())), "after a trip over the wire");
            StringAssert.StartsWith("WrongMan:3,Liar:0,Sellout:4,Robber:1", sig(a.Story()));
        }

        [Test]
        public void AwardsSlideTiming()
        {
            Assert.AreEqual(8.0, LineStory.AwardsAt(45), 1e-9);
            Assert.AreEqual(0.55 * 13.5, LineStory.AwardsAt(13.5), 1e-9, "a short results phase: after the ranking, before the end");
            Assert.AreEqual(0, LineStory.AwardsAt(-3), 1e-9);
        }

        // ------------------------------------------------------------------ real matches

        static readonly LineBotPolicy[] Rotation = { LineBotPolicy.Honest, LineBotPolicy.Greedy, LineBotPolicy.Mixed };

        [Test]
        public void RealMatches_StoriesAreConsistentWithTheResults()
        {
            var seen = new Dictionary<AwardKind, int>();
            var heads = new Dictionary<HeadlineKind, int>();
            int matches = 0;
            for (int n = 3; n <= 6; n++)
                for (int seed = 1; seed <= 80; seed++)
                {
                    int shift = seed % 3;
                    var r = LineSimulationTests.Run(n, 3100 + n * 200 + seed, s => Rotation[(s + shift) % 3], think: seed % 2 == 0 ? 5 : 0).Result;
                    var c = LineConfig.CreateDefault();
                    var story = LineStory.Build(r, c);
                    matches++;
                    string what = n + "p seed " + seed + ": " + Kinds(story);
                    Assert.LessOrEqual(story.Awards.Count, LineStory.MaxAwards, what);
                    Assert.LessOrEqual(story.Headlines.Count, LineStory.MaxHeadlines, what);
                    Assert.AreEqual(story.Awards.Count, story.Awards.Select(a => a.Kind).Distinct().Count(), "an award is given once: " + what);
                    CollectionAssert.AreEqual(story.Awards.OrderBy(a => (int)a.Kind).ToList(), story.Awards, "in priority order");
                    foreach (var a in story.Awards)
                    {
                        seen[a.Kind] = seen.TryGetValue(a.Kind, out int k) ? k + 1 : 1;
                        var p = r.Players.Single(x => x.Player == a.Player);
                        switch (a.Kind)
                        {
                            case AwardKind.WrongMan: Assert.IsTrue(p.Fired, what); break;
                            case AwardKind.Liar: Assert.IsTrue(p.WasCandidate, what); Assert.GreaterOrEqual(a.N3, LineStory.LieGap, what); break;
                            case AwardKind.Robber: Assert.AreEqual(c.BonusPairTaker, a.N1, what); Assert.AreNotEqual(a.Player, a.Other, what); break;
                            case AwardKind.CashRat: Assert.AreEqual(c.CashTake, a.N1, what); break;
                            case AwardKind.Scapegoat: Assert.AreEqual(c.CrisisBlame, a.N1, what); break;
                            case AwardKind.Dodger: Assert.IsFalse(p.Fired, what); Assert.AreEqual(c.ShiftSkipPay, a.N1, what); break;
                            case AwardKind.Clean: Assert.AreEqual(0, p.Blame, what); Assert.IsFalse(p.Fired, what); break;
                        }
                    }
                    foreach (var h in story.Headlines) heads[h.Kind] = heads.TryGetValue(h.Kind, out int k) ? k + 1 : 1;
                    Assert.AreEqual(Math.Min(r.Firings.Count, LineStory.MaxHeadlines), story.Headlines.Count(h => h.Kind == HeadlineKind.Firing), "every firing makes a headline: " + what);
                    // the groups in the results match what the engine paid
                    foreach (var g in r.Groups.Where(g => g.Task == TaskKind.ClientBonus))
                    {
                        int takers = g.Members.Count(id => r.Choices.Single(x => x.Task == TaskKind.ClientBonus && x.Player == id).Choice == LineChoice.Take);
                        foreach (int id in g.Members)
                        {
                            var ch = r.Choices.Single(x => x.Task == TaskKind.ClientBonus && x.Player == id);
                            if (takers == 1) Assert.AreEqual(ch.Choice == LineChoice.Take ? 5 : 0, ch.Payout, what);
                        }
                    }
                }
            TestContext.WriteLine(matches + " matches; awards: " + string.Join(", ", seen.OrderBy(x => (int)x.Key).Select(x => x.Key + " " + x.Value))
                                  + "; headlines: " + string.Join(", ", heads.OrderBy(x => (int)x.Key).Select(x => x.Key + " " + x.Value)));
            // (the declaration awards need bots that declare: LineBotDeclareTests)
            foreach (var kind in new[] { AwardKind.Robber, AwardKind.CashRat, AwardKind.Snitch, AwardKind.Scapegoat, AwardKind.Dodger, AwardKind.Clean })
                Assert.IsTrue(seen.ContainsKey(kind), kind + " never came up in " + matches + " bot matches");
            Assert.IsTrue(heads.ContainsKey(HeadlineKind.BonusBetrayal) && heads.ContainsKey(HeadlineKind.Firing) && heads.ContainsKey(HeadlineKind.Crisis));
        }
    }
}
