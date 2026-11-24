using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests.Line
{
    /// <summary>
    /// Punishments and silence deals (docs/V5_BOT_TALK.md §4.1, <see cref="LineBotPunishStyle.Deals"/>). Seat 1 is the bot;
    /// the colleague who takes is driven by hand and may pay.
    /// v5.3, files: Honest mode opens its files at the meeting's dossier and sends silence money back; Greedy mode opens
    /// at the meeting when it was not paid, and when it was paid it shreds the file at once or, on its betrayal coin, keeps
    /// the money and opens during the crisis calls; Mixed picks the mode per colleague.
    /// The one-shot rules (v5.1 / v5.2, <see cref="LineTest.OneShot"/>): Honest punishes whenever it can and sends silence
    /// money back; Greedy mode punishes only when paid, keeps the money and betrays on its coin.
    /// </summary>
    public sealed class LineBotDealTests
    {
        const int Take = LineChoice.Take;

        /// <summary>Asks the bot for actions every quarter second until <paramref name="done"/>.</summary>
        static void Drive(LineTest t, LineBot bot, int seat, Func<bool> done)
        {
            for (int i = 0; i < 4000 && !done(); i++)
            {
                for (int k = 0; k < 4; k++)
                {
                    var a = bot.Next(t.View(seat), t.Config, t.Now);
                    if (a == null) break;
                    var r = t.Do(seat, a.Verb, a.Target, a.A);
                    Assert.IsTrue(r.Accepted || r.Failure == LineFailure.RateLimited, "bot " + a + ": " + r.Failure);
                }
                t.Advance(0.25);
            }
            Assert.IsTrue(done(), "the match never got there");
        }

        sealed class Outcome
        {
            public int Victim, Witness;            // the hand-driven takers: Client Bonus (p0) and Petty Cash (p1)
            public bool Shared, Left;              // the bot shared / left (it could punish)
            public bool HonestInv, HonestWhistle, BetrayInv, BetrayWhistle, PreviewInv, PreviewWhistle;
            public bool Investigated, Whistled;
            public int Paid0, Paid1, Back0, Back1;
            public int BotPrim;
            /// <summary>Files: what became of the bot's Client Bonus / Petty Cash file and in which phase (null: no file).</summary>
            public LineFileView BonusFile, CashFile;
            public bool Previews = true;
        }

        /// <summary>The bot (seat 1) and two hand-driven takers: its Client Bonus partner takes, its Petty Cash partner takes
        /// alone; with <paramref name="pay"/> each of them sends the bot 1 prim right after the reveal.</summary>
        static Outcome Play(LineBotPolicy policy, int seed, bool pay, bool files = false)
        {
            var t = files ? new LineTest(4, seed) : LineTest.OneShot(4, seed);
            var bot = new LineBot(1, seed * 7 + 3, policy) { ThinkSeconds = 0 };
            var o = new Outcome();
            t.Next();
            o.Victim = t.Group(1).Single(x => x != 1);
            t.Ok(o.Victim, LineVerb.SubmitTask, a: Take);
            Drive(t, bot, 1, () => t.Engine.Phase == LinePhase.TaskReveal);
            o.Shared = t.View(1).Task.MyChoice == LineChoice.Share;
            if (pay) { t.Ok(o.Victim, LineVerb.Transfer, 1, 1); o.Paid0 = 1; }
            Drive(t, bot, 1, () => t.Engine.Phase == LinePhase.TaskPlay && t.View(1).TaskNumber == 1);
            o.Witness = t.Group(1).Single(x => x != 1);
            Assert.AreNotEqual(o.Victim, o.Witness);
            t.Ok(o.Witness, LineVerb.SubmitTask, a: Take);
            Drive(t, bot, 1, () => t.Engine.Phase == LinePhase.TaskReveal);
            o.Left = t.View(1).Task.MyChoice == LineChoice.Leave;
            if (pay) { t.Ok(o.Witness, LineVerb.Transfer, 1, 1); o.Paid1 = 1; }
            o.PreviewWhistle = bot.WillPunish(t.View(1), true, o.Witness);
            Drive(t, bot, 1, () => t.Engine.Phase == LinePhase.MeetingDossier);
            o.PreviewInv = bot.WillPunish(t.View(1), false, o.Victim);
            Drive(t, bot, 1, () => t.Engine.Day >= 1);
            var v = t.View(1);
            o.HonestInv = bot.HonestMode(v, false, o.Victim);
            o.HonestWhistle = bot.HonestMode(v, true, o.Witness);
            o.BetrayInv = bot.Betrays(v, false, o.Victim);
            o.BetrayWhistle = bot.Betrays(v, true, o.Witness);
            o.BotPrim = t.Prim(1);
            // (the bot plays on through the shift and the crisis calls: a file kept for money is opened there)
            Drive(t, bot, 1, () => t.Engine.Phase == LinePhase.CrisisVote);
            t.Finish();
            var r = t.Engine.Result;
            o.BonusFile = r.Files.SingleOrDefault(f => f.Holder == 1 && f.Target == o.Victim && f.Task == TaskKind.ClientBonus);
            o.CashFile = r.Files.SingleOrDefault(f => f.Holder == 1 && f.Target == o.Witness && f.Task == TaskKind.PettyCash);
            if (files)
            {
                Assert.AreEqual(o.Shared, o.BonusFile != null, "a sharer holds a file on the taker");
                Assert.AreEqual(o.Left, o.CashFile != null, "a witness holds a file on the thief");
                Assert.IsFalse(r.Incidents.Any(d => d.Kind == DossierKind.Investigation || d.Kind == DossierKind.Whistle));
                o.Investigated = r.Incidents.Any(d => d.Kind == DossierKind.FileOpened && d.Task == TaskKind.ClientBonus && d.Player == 1 && d.Other == o.Victim);
                o.Whistled = r.Incidents.Any(d => d.Kind == DossierKind.FileOpened && d.Task == TaskKind.PettyCash && d.Player == 1 && d.Other == o.Witness);
                o.Back0 = r.Transfers.Where(x => x.From == 1 && x.To == o.Victim).Sum(x => x.Amount);
                o.Back1 = r.Transfers.Where(x => x.From == 1 && x.To == o.Witness).Sum(x => x.Amount);
                return o;
            }
            CollectionAssert.IsEmpty(r.Files, "the one-shot rules have no files");
            o.Investigated = r.Incidents.Any(d => d.Kind == DossierKind.Investigation && d.Player == 1 && d.Other == o.Victim);
            o.Whistled = r.Incidents.Any(d => d.Kind == DossierKind.Whistle && d.Player == 1 && d.Other == o.Witness);
            o.Back0 = r.Transfers.Where(x => x.From == 1 && x.To == o.Victim).Sum(x => x.Amount);
            o.Back1 = r.Transfers.Where(x => x.From == 1 && x.To == o.Witness).Sum(x => x.Amount);
            return o;
        }

        // ------------------------------------------------------------------ v5.3: files

        [Test]
        public void Files_Honest_OpensAtTheMeetingsDossier_AndSendsSilenceMoneyBack()
        {
            foreach (bool pay in new[] { false, true })
                for (int seed = 1; seed <= 6; seed++)
                {
                    var o = Play(LineBotPolicy.Honest, seed, pay, files: true);
                    Assert.IsTrue(o.Shared && o.Left, "Honest shares and leaves");
                    Assert.IsTrue(o.PreviewInv && o.PreviewWhistle, "the preview: it opens");
                    foreach (var f in new[] { o.BonusFile, o.CashFile })
                    {
                        Assert.AreEqual(LineFileState.Opened, f.State, "seed " + seed);
                        Assert.AreEqual(LinePhase.MeetingDossier, f.Phase, "the first chance at the table, seed " + seed);
                    }
                    Assert.IsTrue(o.Investigated && o.Whistled);
                    Assert.AreEqual(o.Paid0, o.Back0, "returns the Client Bonus taker's money");
                    Assert.AreEqual(o.Paid1, o.Back1, "returns the thief's money");
                    Assert.AreEqual(6, o.BotPrim, "6 + 0 (robbed) + 0 (left): it kept nothing");
                }
        }

        [Test]
        public void Files_GreedyMode_UnpaidOpensAtTheMeeting_PaidShredsOrKeepsTheMoneyAndOpensInTheCrisisCalls()
        {
            int honest = 0, unpaid = 0, shredded = 0, late = 0;
            for (int seed = 1; seed <= 60; seed++)
                foreach (bool pay in new[] { false, true })
                {
                    var o = Play(LineBotPolicy.Mixed, seed, pay, files: true);
                    for (int k = 0; k < 2; k++)
                    {
                        var f = k == 0 ? o.BonusFile : o.CashFile;
                        if (f == null) continue; // the bot took as well: no file
                        string what = "seed " + seed + (k == 0 ? " bonus" : " cash") + (pay ? " paid" : " unpaid");
                        bool honestMode = k == 0 ? o.HonestInv : o.HonestWhistle, betrays = k == 0 ? o.BetrayInv : o.BetrayWhistle;
                        int back = k == 0 ? o.Back0 : o.Back1, paid = k == 0 ? o.Paid0 : o.Paid1;
                        if (honestMode)
                        {
                            honest++;
                            Assert.AreEqual(LineFileState.Opened, f.State, what);
                            Assert.AreEqual(LinePhase.MeetingDossier, f.Phase, what);
                            Assert.AreEqual(paid, back, "Honest mode returns the money: " + what);
                        }
                        else if (!pay)
                        {
                            unpaid++;
                            Assert.AreEqual(LineFileState.Opened, f.State, "not paid: opened at the meeting, " + what);
                            Assert.AreEqual(LinePhase.MeetingDiscussion, f.Phase, "late in the discussion (time to pay): " + what);
                        }
                        else if (betrays)
                        {
                            late++;
                            Assert.AreEqual(0, back, "keeps the money: " + what);
                            Assert.AreEqual(LineFileState.Opened, f.State, what);
                            Assert.AreEqual(LinePhase.CrisisCalls, f.Phase, "the betrayal comes late: " + what);
                        }
                        else
                        {
                            shredded++;
                            Assert.AreEqual(0, back, "keeps the money: " + what);
                            Assert.AreEqual(LineFileState.Shredded, f.State, what);
                            Assert.That(f.Phase, Is.EqualTo(LinePhase.TaskReveal).Or.EqualTo(LinePhase.Break), "as soon as it is paid: " + what);
                        }
                        // the preview (taken before the meeting) is the decision: it punishes unless it shreds
                        Assert.AreEqual(k == 0 ? o.PreviewInv : o.PreviewWhistle, f.State == LineFileState.Opened, what);
                    }
                }
            TestContext.WriteLine("honest " + honest + ", greedy unpaid " + unpaid + ", paid: shredded " + shredded + ", opened late " + late);
            Assert.Greater(honest, 10);
            Assert.Greater(unpaid, 5);
            Assert.Greater(shredded, 2, "a paid Greedy mode keeps the deal and shreds");
            Assert.Greater(late, 2, "or keeps the money and opens late");
        }

        [Test]
        public void Files_Plan_PreviewsTheCoins()
        {
            // Greedy always takes, so it is never a holder in play; its deal answers are the coins
            var v = new LineView { Me = 2, WeekId = 9 };
            var greedy = new LineBot(2, 11, LineBotPolicy.Greedy);
            var honest = new LineBot(2, 11, LineBotPolicy.Honest);
            for (int target = 0; target < 6; target++)
            {
                if (target == 2) continue;
                foreach (bool cash in new[] { false, true })
                {
                    Assert.AreEqual(LineBot.FileOpensAtMeeting, greedy.FilePlan(v, cash, target), "not paid: it opens at the meeting");
                    Assert.IsTrue(greedy.WillPunish(v, cash, target));
                    int paid = greedy.FilePlan(v, cash, target, assumePaid: true);
                    Assert.AreEqual(greedy.Betrays(v, cash, target) ? LineBot.FileOpensLate : LineBot.FileShreds, paid);
                    Assert.AreEqual(paid != LineBot.FileShreds, greedy.WillPunish(v, cash, target, assumePaid: true));
                    Assert.AreEqual(LineBot.FileOpensAtMeeting, honest.FilePlan(v, cash, target, assumePaid: true), "Honest opens whatever it is paid");
                    Assert.That(greedy.LateOpenAt(v, cash, target), Is.InRange(0.35, 0.75));
                }
            }
        }

        // ------------------------------------------------------------------ the one-shot rules (v5.1 / v5.2)

        [Test]
        public void Honest_PunishesEveryTime_AndSendsSilenceMoneyBack()
        {
            foreach (bool pay in new[] { false, true })
                for (int seed = 1; seed <= 6; seed++)
                {
                    var o = Play(LineBotPolicy.Honest, seed, pay);
                    Assert.IsTrue(o.Shared && o.Left, "Honest shares and leaves");
                    Assert.IsTrue(o.Investigated, "investigates the taker, seed " + seed);
                    Assert.IsTrue(o.Whistled, "whistles on the thief, seed " + seed);
                    Assert.AreEqual(o.Paid0, o.Back0, "returns the Client Bonus taker's money");
                    Assert.AreEqual(o.Paid1, o.Back1, "returns the thief's money");
                    Assert.AreEqual(6, o.BotPrim, "6 + 0 (robbed) + 0 (left): it kept nothing");
                }
        }

        [Test]
        public void Mixed_GreedyMode_PunishesOnlyWhenPaid_KeepsTheMoneyAndBetraysOnItsCoin_HonestModeAsHonest()
        {
            int honest = 0, greedyPaid = 0, greedyUnpaid = 0, betrayed = 0, kept = 0;
            for (int seed = 1; seed <= 60; seed++)
                foreach (bool pay in new[] { false, true })
                {
                    var o = Play(LineBotPolicy.Mixed, seed, pay);
                    if (o.Shared)
                    {
                        Assert.AreEqual(o.PreviewInv, o.Investigated, "the preview is the decision (investigation), seed " + seed);
                        if (o.HonestInv) { honest++; Assert.IsTrue(o.Investigated); Assert.AreEqual(o.Paid0, o.Back0); }
                        else if (pay)
                        {
                            greedyPaid++;
                            Assert.AreEqual(0, o.Back0, "Greedy mode keeps the money");
                            Assert.AreEqual(o.BetrayInv, o.Investigated, "and punishes on its coin");
                            if (o.Investigated) betrayed++; else kept++;
                        }
                        else { greedyUnpaid++; Assert.IsFalse(o.Investigated, "Greedy mode never punishes unpaid"); }
                    }
                    else Assert.IsFalse(o.Investigated, "a taker never investigates");
                    if (o.Left)
                    {
                        Assert.AreEqual(o.PreviewWhistle, o.Whistled, "the preview is the decision (whistle), seed " + seed);
                        if (o.HonestWhistle) { honest++; Assert.IsTrue(o.Whistled); Assert.AreEqual(o.Paid1, o.Back1); }
                        else if (pay)
                        {
                            greedyPaid++;
                            Assert.AreEqual(0, o.Back1);
                            Assert.AreEqual(o.BetrayWhistle, o.Whistled);
                            if (o.Whistled) betrayed++; else kept++;
                        }
                        else { greedyUnpaid++; Assert.IsFalse(o.Whistled); }
                    }
                    else Assert.IsFalse(o.Whistled);
                }
            TestContext.WriteLine("honest " + honest + ", greedy paid " + greedyPaid + " (betrayed " + betrayed + ", kept " + kept + "), greedy unpaid " + greedyUnpaid);
            Assert.Greater(honest, 10);
            Assert.Greater(greedyUnpaid, 5);
            Assert.Greater(betrayed, 2, "a paid Greedy mode sometimes punishes anyway");
            Assert.Greater(kept, 2, "and sometimes keeps the deal");
        }

        [Test]
        public void Greedy_ModeAndBetrayalCoinsAreSeededPerBotWeekKindAndTarget()
        {
            // Greedy always takes, so it is never a victim or a witness in play; its deal answers are the coins
            var v = new LineView { Me = 2, WeekId = 9 };
            var greedy = new LineBot(2, 11, LineBotPolicy.Greedy);
            int betrays = 0;
            for (int target = 0; target < 6; target++)
            {
                if (target == 2) continue;
                foreach (bool whistle in new[] { false, true })
                {
                    Assert.IsFalse(greedy.HonestMode(v, whistle, target));
                    if (greedy.Betrays(v, whistle, target)) betrays++;
                }
            }
            var honest = new LineBot(2, 11, LineBotPolicy.Honest);
            Assert.IsTrue(honest.HonestMode(v, true, 0) && honest.WillPunish(v, false, 3));
            // the coins are per bot, week, kind and target: stable, and about half say betray across many
            int many = 0;
            for (int seed = 1; seed <= 200; seed++) if (new LineBot(0, seed, LineBotPolicy.Greedy).Betrays(v, true, 1)) many++;
            Assert.That(many, Is.InRange(70, 130));
            Assert.AreEqual(greedy.Betrays(v, true, 4), new LineBot(2, 11, LineBotPolicy.Greedy).Betrays(v, true, 4));
            TestContext.WriteLine("greedy 11 betrays " + betrays + "/10");
        }

        [Test]
        public void Punishing_DrawsNothingFromTheRandomStream()
        {
            // the same seed and policy decide the same tasks and votes whether a colleague pays or not
            for (int seed = 1; seed <= 8; seed++)
            {
                var a = Play(LineBotPolicy.Mixed, seed, false);
                var b = Play(LineBotPolicy.Mixed, seed, true);
                Assert.AreEqual(a.Shared, b.Shared);
                Assert.AreEqual(a.Left, b.Left);
                Assert.AreEqual(a.HonestInv, b.HonestInv);
                Assert.AreEqual(a.HonestWhistle, b.HonestWhistle);
            }
        }

        // ------------------------------------------------------------------ the talk: silence money in bot-only matches

        static readonly Regex FileLine = new Regex(@"bottalk file seat=(\d+) policy=(\w+) task=(\w+) target=(\d+) mode=(\w+) paid=(\d+) returned=(\d+) state=(\w+) phase=(\w+) outcome=(\w+)");
        static readonly Regex Hush = new Regex(@"bottalk hush seat=(\d+) policy=(\w+) kind=(\w+) to=(\d+) amount=1 via=(\w+) punished=(yes|no) returned=(\d+) file=(\w+)");

        [Test]
        public void Talkers_PaySilenceMoney_HonestReturnsIt_GreedyModeKeepsIt()
        {
            int hushes = 0, paidDeals = 0, refused = 0, sold = 0, betrayed = 0, honestPunished = 0, reactions = 0, asks = 0;
            var rotation = new[] { LineBotPolicy.Honest, LineBotPolicy.Greedy, LineBotPolicy.Mixed };
            for (int n = 3; n <= 6; n++)
                for (int i = 0; i < 4; i++)
                {
                    var sim = LineBotTalkTests.RunForDeals(n, 3100 + n * 100 + i, s => rotation[(s + i) % 3]);
                    reactions += sim.Lines.Count(l => l.Key.StartsWith("bot.deal.", StringComparison.Ordinal));
                    foreach (var l in sim.Lines.Where(l => l.Key.StartsWith("bot.file.ask.", StringComparison.Ordinal)))
                    {
                        asks++;
                        Assert.AreNotEqual(LineBotPolicy.Honest, l.Policy, "Honest never sells a file");
                        Assert.AreEqual(BotClaimKind.Silence, l.Claim);
                        Assert.IsTrue(sim.Result.Files.Any(f => f.Holder == l.Speaker && f.Target == l.Listener), "only a holder offers: " + l);
                    }
                    foreach (var l in sim.Lines.Where(l => l.Key.StartsWith("bot.deal.", StringComparison.Ordinal)))
                    {
                        Assert.IsTrue(LineBotLines.Valid(l));
                        if (l.Policy == LineBotPolicy.Honest) Assert.AreEqual("bot.deal.refund.h", l.Key);
                        if (l.Policy == LineBotPolicy.Greedy) Assert.AreEqual("bot.deal.keep.g", l.Key);
                    }
                    foreach (var line in sim.Log)
                    {
                        var h = Hush.Match(line);
                        if (h.Success)
                        {
                            Assert.AreNotEqual("Honest", h.Groups[2].Value, "Honest never takes, so never pays for silence");
                            if (h.Groups[5].Value != "none") hushes++;
                        }
                        Assert.IsFalse(line.StartsWith("bottalk punish ", StringComparison.Ordinal), "the one-shot log line is gone with files: " + line);
                        var m = FileLine.Match(line);
                        if (!m.Success) continue;
                        string policy = m.Groups[2].Value, mode = m.Groups[5].Value, state = m.Groups[8].Value, phase = m.Groups[9].Value, outcome = m.Groups[10].Value;
                        int paid = int.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture), back = int.Parse(m.Groups[7].Value, CultureInfo.InvariantCulture);
                        if (policy == "Honest") Assert.AreEqual("honest", mode);
                        if (policy == "Greedy") Assert.AreEqual("greedy", mode);
                        if (mode == "honest")
                        {
                            Assert.AreEqual(paid, back, "Honest mode returns every prim: " + line);
                            Assert.That(state, Is.EqualTo("opened").Or.EqualTo("void"), "Honest mode opens (unless a colleague was first): " + line);
                            if (policy == "Honest" && state == "opened") honestPunished++;
                        }
                        else
                        {
                            Assert.AreEqual(0, back, "Greedy mode keeps the money: " + line);
                            if (paid == 0) Assert.That(state, Is.EqualTo("opened").Or.EqualTo("void"), "not paid: opened at the meeting: " + line);
                            else if (state == "opened") Assert.AreEqual("CrisisCalls", phase, "a paid file is only opened late: " + line);
                            else Assert.That(state, Is.EqualTo("shredded").Or.EqualTo("void"), line);
                        }
                        if (paid > 0) paidDeals++;
                        if (outcome == "refused") refused++;
                        if (outcome == "sold") sold++;
                        if (outcome == "betrayed") betrayed++;
                    }
                    Assert.IsFalse(sim.Log.Any(x => x.Contains(" ok=no")), string.Join("\n", sim.Log.Where(x => x.Contains(" ok=no"))));
                }
            TestContext.WriteLine("hush " + hushes + ", paid deals " + paidDeals + " (refused " + refused + ", sold " + sold + ", betrayed " + betrayed + "), Honest opened " + honestPunished + ", reactions " + reactions + ", asks " + asks);
            Assert.Greater(hushes, 10, "Greedy / Mixed takers pay for silence");
            Assert.Greater(refused, 3, "Honest sends it back");
            Assert.Greater(honestPunished, 10);
            Assert.Greater(reactions, 5, "the paid bot answers the money");
            Assert.Greater(sold + betrayed, 0, "a Mixed holder in Greedy mode keeps some");
        }
    }
}
