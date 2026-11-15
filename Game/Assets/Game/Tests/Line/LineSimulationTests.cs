using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BirimizKovulacak.Core.Tests.Line;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class LineSimulationTests
    {
        /// <summary>
        /// Event-driven bot match: bots act until they return null, then time jumps to the next deadline (phase end or a
        /// bot's wake time). Returns the engine; asserts it finished without stalling and without invalid bot actions.
        /// </summary>
        internal static ILineEngine Run(int n, int seed, Func<int, LineBotPolicy> policy, double think = 0, Action<byte[]> trace = null, int disconnect = -1)
        {
            var setup = LineTest.MakeSetup(n, seed);
            var config = LineConfig.CreateDefault();
            var engine = LineEngineFactory.Create(setup, config);
            var bots = Enumerable.Range(0, n).Select(i => new LineBot(i, seed * 31 + i * 97, policy(i)) { ThinkSeconds = think }).ToArray();
            double now = 0;
            int steps = 0;
            if (disconnect >= 0) engine.SetConnected(disconnect, false, 0);
            while (!engine.IsFinished)
            {
                Assert.Less(++steps, 2000, "match stalled at " + engine.Phase);
                engine.Tick(now);
                for (int round = 0; round < 8; round++)
                {
                    bool acted = false;
                    for (int i = 0; i < n; i++)
                    {
                        var view = engine.BuildView(i, now);
                        if (trace != null) trace(LineCodec.EncodeView(view));
                        var action = bots[i].Next(view, config, now);
                        if (action == null) continue;
                        var result = engine.Apply(i, action, now);
                        if (trace != null) { trace(LineCodec.EncodeAction(action)); trace(LineCodec.EncodeResult(result)); }
                        Assert.IsTrue(result.Accepted || result.Failure == LineFailure.AlreadyResolved || result.Failure == LineFailure.Absent,
                            "bot " + i + " " + view.Phase + " " + action + " → " + result);
                        acted = true;
                    }
                    if (!acted) break;
                }
                var events = engine.DrainEvents();
                if (trace != null) trace(LineCodec.EncodeEvents(events));
                double next = engine.PhaseEndTime;
                foreach (var b in bots) if (b.WakeAt > now) next = Math.Min(next, b.WakeAt);
                if (engine.Phase == LinePhase.Results) { engine.HostContinue(now); break; }
                now = Math.Max(now, next);
            }
            Assert.IsTrue(engine.IsFinished);
            return engine;
        }

        static LineBotPolicy Seeded(int seed, int seat) { return ((seed * 7 + seat * 13) & 3) == 0 ? LineBotPolicy.Greedy : ((seed + seat) % 3 == 0 ? LineBotPolicy.Mixed : LineBotPolicy.Honest); }

        [TestCase(3), TestCase(4), TestCase(5), TestCase(6)]
        public void BotsFinishAThousandSeededMatches(int n)
        {
            int fired = 0, fewer = 0;
            var c = LineConfig.CreateDefault();
            for (int seed = 1; seed <= 1000; seed++)
            {
                var e = Run(n, seed, seat => Seeded(seed, seat));
                Assert.NotNull(e.Result, "seed " + seed);
                fired += e.Result.Fired.Count;
                Assert.AreEqual(n, e.Result.Players.Count);
                Assert.IsTrue(e.Result.Winners.Count >= 1);
                // v5.1: only players with suç are candidates, and the firings never exceed the candidates
                int candidates = e.Result.Players.Count(p => p.WasCandidate);
                Assert.IsTrue(e.Result.Players.Where(p => p.WasCandidate).All(p => p.Blame >= c.CandidateMinBlame), "seed " + seed);
                Assert.AreEqual(Math.Min(c.FiringsFor(n), candidates), e.Result.Fired.Count, "seed " + seed);
                if (e.Result.Fired.Count < c.FiringsFor(n)) fewer++;
            }
            Assert.LessOrEqual(fired, 1000 * c.FiringsFor(n));
            TestContext.WriteLine(n + " players: " + fewer + " matches fired fewer than " + c.FiringsFor(n));
        }

        [TestCase(LineBotPolicy.Honest), TestCase(LineBotPolicy.Greedy), TestCase(LineBotPolicy.Mixed)]
        public void EveryPolicyFinishesEveryHeadCountWithThinkTime(LineBotPolicy p)
        {
            for (int n = 3; n <= 6; n++) Assert.NotNull(Run(n, 77 + n, seat => p, think: 12).Result);
        }

        [Test]
        public void BotsCopeWithAnAbsentSeat()
        {
            Assert.NotNull(Run(5, 4242, seat => seat % 2 == 0 ? LineBotPolicy.Greedy : LineBotPolicy.Honest, disconnect: 3).Result);
        }

        [Test]
        public void IdenticalInputsReproduceEveryViewActionResultAndEvent()
        {
            ulong Digest(int seed)
            {
                ulong hash = 14695981039346656037UL;
                var e = Run(6, seed, seat => seat < 2 ? LineBotPolicy.Greedy : LineBotPolicy.Mixed, think: 9,
                    trace: bytes => { foreach (byte b in bytes) hash = unchecked((hash ^ b) * 1099511628211UL); });
                return hash ^ e.Result.ResultHash;
            }
            Assert.AreEqual(Digest(447), Digest(447));
            Assert.AreNotEqual(Digest(447), Digest(448));
        }

        [Test]
        public void SameSeedSameResultHash()
        {
            var a = Run(5, 31, seat => LineBotPolicy.Mixed);
            var b = Run(5, 31, seat => LineBotPolicy.Mixed);
            Assert.AreEqual(a.Result.ResultHash, b.Result.ResultHash);
        }

        [Test]
        public void SixPlayerViewsStaySmall()
        {
            var m = new LineTest(6);
            m.ToCrisis();
            m.Until(LinePhase.FinalVote);
            int bytes = LineCodec.EncodeView(m.View()).Length;
            TestContext.WriteLine("6-player final view: " + bytes + " bytes");
            Assert.Less(bytes, 6144);
        }

        [Test]
        public void BotsActOnlyThroughTheirOwnViewAndDoNotUseThePhoneOrRecorder()
        {
            var verbs = new HashSet<LineVerb>();
            Run(4, 9, seat => LineBotPolicy.Mixed, trace: bytes =>
            {
                if (bytes.Length > 3 && bytes[3] == 2) verbs.Add(LineCodec.DecodeAction(bytes).Verb);
            });
            CollectionAssert.IsSubsetOf(verbs, new[] { LineVerb.SubmitTask, LineVerb.Whistle, LineVerb.Investigate, LineVerb.Vote });
            Assert.Throws<ArgumentException>(() => new LineBot(6, 1));
        }
    }
}
