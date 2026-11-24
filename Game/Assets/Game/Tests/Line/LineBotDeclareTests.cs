using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BirimizKovulacak.Core.Tests.Line;

namespace BirimizKovulacak.Core.Tests
{
    /// <summary>
    /// v5.2 bots (docs/V5_BOT_TALK.md §7.1): a candidate bot declares its prim (Honest the truth, Greedy an understatement,
    /// Mixed either, on hash coins that draw nothing from the random stream), repeats it in its defence, colleagues say when
    /// their own count is well above a declaration, and the owner of an award says its line at the results.
    /// </summary>
    [TestFixture]
    public sealed class LineBotDeclareTests
    {
        static readonly LineBotPolicy[] Rotation = { LineBotPolicy.Honest, LineBotPolicy.Greedy, LineBotPolicy.Mixed };

        [Test]
        public void Bots_HonestDeclaresTheTruth_GreedyUnderstates_MixedDoesEither()
        {
            int honest = 0, greedy = 0, mixedTrue = 0, mixedLow = 0, none = 0, liars = 0, wrongMen = 0;
            for (int n = 3; n <= 6; n++)
                for (int seed = 1; seed <= 70; seed++)
                {
                    // (Honest rarely has the suç to be a candidate next to takers: the last ten matches are all Honest,
                    // where the crisis makes one of them the candidate)
                    int shift = seed % 3;
                    var policy = Enumerable.Range(0, n).Select(s => seed > 60 ? LineBotPolicy.Honest : Rotation[(s + shift) % 3]).ToArray();
                    var planned = new Dictionary<int, int>();
                    var engine = LineSimulationTests.Run(n, 9000 + n * 100 + seed, s => policy[s], think: seed % 2 == 0 ? 6 : 0,
                        watch: (bots, v) => { if (v.Features.DeclareOpen) planned[v.Me] = bots[v.Me].PlannedDeclaration(v); });
                    var r = engine.Result;
                    foreach (var d in r.Declarations)
                    {
                        if (d.Amount < 0) { none++; continue; }
                        Assert.AreEqual(planned[d.Player], d.Amount, "the preview is the declaration");
                        // (LineSim-style matches have no transfers: the prim at the reveal is the prim when declaring)
                        string what = n + "p seed " + seed + " seat " + d.Player + " " + policy[d.Player] + ": " + d.Amount + " of " + d.Actual;
                        bool low = d.Amount < d.Actual;
                        switch (policy[d.Player])
                        {
                            case LineBotPolicy.Honest:
                                Assert.AreEqual(d.Actual, d.Amount, what);
                                honest++;
                                break;
                            case LineBotPolicy.Greedy:
                                AssertUnderstated(d, what);
                                greedy++;
                                break;
                            default:
                                if (low) { AssertUnderstated(d, what); mixedLow++; } else { Assert.AreEqual(d.Actual, d.Amount, what); mixedTrue++; }
                                break;
                        }
                    }
                    foreach (var f in r.Firings)
                        Assert.AreEqual(r.Declarations.Single(d => d.Player == f.Player).Amount, f.Declared);
                    // the story of the match: an understatement of 3 or more is "En Pahalı Yalan", a true claim that
                    // still got fired is "Yanlış Adam"
                    var story = LineStory.Build(r, LineConfig.CreateDefault());
                    if (r.Declarations.Any(d => d.Amount >= 0 && d.Actual - d.Amount >= LineStory.LieGap))
                    {
                        liars++;
                        var liar = story.Awards.Single(a => a.Kind == AwardKind.Liar);
                        Assert.AreNotEqual(LineBotPolicy.Honest, policy[liar.Player], "Honest is never the liar");
                    }
                    else Assert.IsFalse(story.Awards.Any(a => a.Kind == AwardKind.Liar));
                    var wrong = story.Awards.SingleOrDefault(a => a.Kind == AwardKind.WrongMan);
                    if (wrong != null) { wrongMen++; Assert.AreEqual(r.Firings.First(f => f.Declared >= 0 && f.Declared == f.Desk).Player, wrong.Player); }
                }
            Assert.Greater(liars, 100, "Greedy's understatement is the priciest lie of most matches");
            Assert.Greater(wrongMen, 3, "now and then a candidate who told the truth is fired");
            TestContext.WriteLine("declarations: honest " + honest + ", greedy " + greedy + ", mixed true " + mixedTrue + " / low " + mixedLow + ", none " + none);
            Assert.Greater(honest, 20);
            Assert.Greater(greedy, 50);
            Assert.Greater(mixedTrue, 10);
            Assert.Greater(mixedLow, 10);
            Assert.AreEqual(0, none, "a connected candidate bot always declares in time");
        }

        /// <summary>Roughly 40% less (35–45%, rounded), at least 2 less, never below 3 and never above the truth.</summary>
        static void AssertUnderstated(LineDeclarationView d, string what)
        {
            int prim = d.Actual;
            if (prim <= 3) { Assert.AreEqual(prim, d.Amount, what); return; }
            Assert.GreaterOrEqual(d.Amount, 3, what);
            Assert.LessOrEqual(d.Amount, Math.Max(3, prim - 2), what);
            if (prim >= 8)
            {
                Assert.GreaterOrEqual(d.Amount, (int)Math.Floor(prim * 0.55), what);
                Assert.LessOrEqual(d.Amount, (int)Math.Ceiling(prim * 0.65), what);
            }
        }

        [Test]
        public void Bots_DeclaringDrawsNothingFromTheRandomStream()
        {
            // the same matches with declarations off: every task choice, vote, firing and prim is identical
            for (int n = 3; n <= 6; n++)
                for (int seed = 1; seed <= 12; seed++)
                {
                    int shift = seed % 3;
                    Func<int, LineBotPolicy> policy = s => Rotation[(s + shift) % 3];
                    foreach (double think in new[] { 0.0, 9.0 })
                    {
                        var on = LineSimulationTests.Run(n, 500 + seed, policy, think: think).Result;
                        var off = LineSimulationTests.Run(n, 500 + seed, policy, think: think, configure: c => c.DeclarationsEnabled = false).Result;
                        string what = n + "p seed " + seed + " think " + think;
                        CollectionAssert.AreEqual(off.Players.Select(p => p.Prim), on.Players.Select(p => p.Prim), what);
                        CollectionAssert.AreEqual(off.Players.Select(p => p.Blame), on.Players.Select(p => p.Blame), what);
                        CollectionAssert.AreEqual(off.Fired, on.Fired, what);
                        CollectionAssert.AreEqual(off.Ballots.Select(b => b.Voter * 100 + b.Target), on.Ballots.Select(b => b.Voter * 100 + b.Target), what);
                        CollectionAssert.AreEqual(off.Choices.Select(c => c.Choice), on.Choices.Select(c => c.Choice), what);
                        CollectionAssert.IsEmpty(off.Declarations);
                    }
                }
        }

        [Test]
        public void Bots_TalkAboutDeclarations_HonestNeverLies()
        {
            int own = 0, ownHonest = 0, ownLies = 0, doubts = 0, logged = 0;
            for (int n = 4; n <= 6; n++)
                for (int i = 0; i < 8; i++)
                {
                    int seed = 6200 + n * 50 + i, shift = i % 3;
                    var sim = LineBotTalkTests.RunTalking(n, seed, s => Rotation[(s + shift) % 3]);
                    CollectionAssert.IsEmpty(sim.RejectedWhat.Where(x => x.Contains("Declare")), "a bot's declaration is always valid");
                    var r = sim.Result;
                    foreach (var l in sim.Lines)
                    {
                        Assert.IsTrue(LineBotLines.Valid(l), l.ToString());
                        if (l.Key.StartsWith("bot.declare.own.", StringComparison.Ordinal))
                        {
                            own++;
                            var d = r.Declarations.Single(x => x.Player == l.Speaker);
                            Assert.AreEqual(d.Amount, l.Args[0], "a bot repeats exactly what it declared");
                            Assert.AreEqual(BotClaimKind.Declare, l.Claim);
                            Assert.AreEqual(d.Actual == d.Amount, l.Truthful, l.Speaker + " " + l + " actual " + d.Actual);
                            if (l.Policy == LineBotPolicy.Honest) { ownHonest++; Assert.IsTrue(l.Truthful, "Honest never lies: " + l); }
                            if (!l.Truthful) ownLies++;
                        }
                        if (l.Key.StartsWith("bot.declare.doubt.", StringComparison.Ordinal))
                        {
                            doubts++;
                            var d = r.Declarations.Single(x => x.Player == l.Args[0]);
                            Assert.AreNotEqual(l.Speaker, l.Args[0]);
                            Assert.GreaterOrEqual(d.Amount, 0, "only a declaration can be doubted");
                            Assert.GreaterOrEqual(l.Args[1] - d.Amount, 3, "said only when the count is at least 3 above the claim: " + l);
                            Assert.IsTrue(l.Truthful);
                        }
                    }
                    Assert.IsFalse(sim.Log.Any(x => x.StartsWith("bottalk check ", StringComparison.Ordinal) && x.EndsWith("ok=no", StringComparison.Ordinal)),
                        string.Join("\n", sim.Log.Where(x => x.Contains("ok=no"))));
                    foreach (string line in sim.Log.Where(x => x.StartsWith("bottalk declare ", StringComparison.Ordinal)))
                    {
                        logged++;
                        if (line.Contains("policy=Honest")) StringAssert.DoesNotContain("truthful=no", line);
                    }
                    Assert.AreEqual(r.Declarations.Count, sim.Log.Count(x => x.StartsWith("bottalk declare ", StringComparison.Ordinal)), "one log line per candidate");
                }
            TestContext.WriteLine("own claims " + own + " (honest " + ownHonest + ", lies " + ownLies + "), doubts " + doubts + ", logged " + logged);
            Assert.Greater(own, 10, "candidates repeat their declaration in the defence");
            Assert.Greater(ownLies, 2, "an understated declaration is a lie when repeated");
            Assert.Greater(doubts, 2, "colleagues say when their count is well above a declaration");
        }

        static string Kinds(LineStory s) { return string.Join(",", s.Awards.Select(a => a.Kind + ":" + a.Player)); }

        [Test]
        public void TalkingBots_TheOwnerOfAnAwardSaysItsLineAtTheResults()
        {
            int shouted = 0, deals = 0;
            for (int n = 4; n <= 6; n++)
                for (int i = 0; i < 6; i++)
                {
                    int seed = 7300 + n * 40 + i, shift = i % 3;
                    var sim = LineBotTalkTests.RunTalking(n, seed, s => Rotation[(s + shift) % 3]);
                    var story = LineStory.Build(sim.Result, sim.Config);
                    var lines = sim.Lines.Where(l => l.Key.StartsWith("bot.award.", StringComparison.Ordinal)).ToList();
                    foreach (var l in lines)
                    {
                        Assert.IsTrue(l.Shout, "out loud, never in the chat");
                        Assert.IsTrue(LineBotLines.Valid(l));
                        var a = story.AwardOf(l.Speaker);
                        Assert.IsNotNull(a, n + "p seed " + seed + ": " + l.Speaker + " has no award but said " + l.Key);
                        Assert.AreEqual("bot.award." + LineAwards.Id(a.Kind), l.Key);
                        shouted++;
                    }
                    var owners = story.Awards.Select(a => a.Player).Distinct().ToList();
                    CollectionAssert.AreEquivalent(owners, lines.Select(l => l.Speaker).ToList(), n + "p seed " + seed + ": every owner once (" + Kinds(story) + ")");
                    Assert.AreEqual(lines.Count, sim.Log.Count(x => x.StartsWith("bottalk award ", StringComparison.Ordinal)));
                    deals += story.Headlines.Count(h => h.Kind == HeadlineKind.DealBroken) + story.Awards.Count(a => a.Kind == AwardKind.Loyal || a.Kind == AwardKind.Sellout);
                }
            TestContext.WriteLine("award lines " + shouted + "; deal stories " + deals);
            Assert.Greater(shouted, 30);
        }
    }
}
