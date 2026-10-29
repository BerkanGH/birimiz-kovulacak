using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class LineSimulationTests
    {
        private static IEnumerable<TestCaseData> Weeks()
        {
            for (int players = 3; players <= 6; players++)
                foreach (WeekLength length in new[] { WeekLength.Short, WeekLength.Full })
                    foreach (int seed in new[] { 13, 179, 4261 })
                        yield return new TestCaseData(players, length, seed);
        }

        internal static LineSetup Setup(int players, int seed, WeekLength length)
        {
            var setup = new LineSetup { WeekId = 17, Seed = seed, Length = length };
            for (int p = 0; p < players; p++)
                setup.Players.Add(new LinePlayerInfo { Id = p, Name = "Colleague " + p, ColorIndex = p, IsBot = true });
            return setup;
        }

        [TestCaseSource(nameof(Weeks))]
        public void FullBotWeeksFinishAndReplayExactly(int players, WeekLength length, int seed)
        {
            var first = Simulate(players, length, seed);
            var second = Simulate(players, length, seed);
            Assert.AreEqual(first.Hash, second.Hash);
            Assert.AreEqual(first.Actions, second.Actions);
            Assert.Greater(first.Actions, players * 5);
        }

        private sealed class Run
        {
            internal ulong Hash;
            internal int Actions, MidweekBytes;
            internal double ElapsedSeconds;
        }

        private static Run Simulate(int players, WeekLength length, int seed)
        {
            var watch = Stopwatch.StartNew();
            var config = LineConfig.CreateDefault();
            ILineEngine engine = LineEngineFactory.Create(Setup(players, seed, length), config);
            var bots = new LineBot[players];
            for (int p = 0; p < players; p++) bots[p] = new LineBot(p, seed + p * 313);
            var run = new Run();
            for (double now = 0; !engine.IsFinished && now < 20000; now += 0.25)
            {
                engine.Tick(now);
                if (engine.IsFinished) break;
                for (int p = 0; p < players; p++)
                {
                    var view = engine.BuildView(p, now);
                    if (players == 6 && view.Day == 1 && view.Phase == LinePhase.RoundPlay && view.RoundIndex == 1 && run.MidweekBytes == 0)
                        run.MidweekBytes = LineCodec.EncodeView(view).Length;
                    var action = bots[p].Next(view, config, now);
                    if (action == null) continue;
                    run.Actions++;
                    var result = engine.Apply(p, action, now);
                    if (!result.Accepted)
                        Assert.That(action.Verb == LineVerb.Call && result.Failure == LineFailure.Busy,
                            "Bot sent a visibly invalid action: " + action + ", " + result + ", phase=" + view.Phase + ", time=" + now);
                }
                if (engine.Phase == LinePhase.MeetingDossier)
                {
                    var picks = engine.RecordingPicks;
                    for (int i = 0; i < picks.Count; i++)
                        if (!picks[i].Played && now >= picks[i].PlayAt)
                            engine.ReportRecording(i, picks[i].CandidateCallIds.Count > 0 ? picks[i].CandidateCallIds[0] : -1, now);
                }
                engine.DrainEvents();
            }
            Assert.IsTrue(engine.IsFinished, "Bot week did not terminate.");
            Assert.AreEqual(LinePhase.WeekResults, engine.Phase);
            Assert.NotNull(engine.Result);
            Assert.AreEqual(players, engine.Result.Players.Count);
            Assert.AreEqual(1, engine.NextHistory().WeeksPlayed);
            run.Hash = engine.Result.ResultHash;
            watch.Stop(); run.ElapsedSeconds = watch.Elapsed.TotalSeconds;
            return run;
        }

        [Test]
        public void MeasureSixPlayerTickAndShortWeek()
        {
            ILineEngine engine = LineEngineFactory.Create(Setup(6, 1927, WeekLength.Short), LineConfig.CreateDefault());
            for (int i = 0; i < 1000; i++) engine.Tick(0);
            long version = engine.StateVersion;
            const int iterations = 500000;
            var watch = new Stopwatch();
            long before = GC.GetAllocatedBytesForCurrentThread();
            watch.Start();
            for (int i = 0; i < iterations; i++) engine.Tick((double)i / iterations);
            watch.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated, "Unchanged ticks must allocate nothing.");
            Assert.AreEqual(version, engine.StateVersion);
            var run = Simulate(6, WeekLength.Short, 1927);
            Assert.Greater(run.MidweekBytes, 0);
            Assert.Less(run.MidweekBytes, 4096);
            TestContext.WriteLine("LINE BENCH: six-player idle Tick=" + (iterations / watch.Elapsed.TotalSeconds).ToString("F0") +
                "/s, allocations=" + allocated + " B; full six-player Short Week=" + (run.ElapsedSeconds * 1000).ToString("F2") +
                " ms; mid-week encoded view=" + run.MidweekBytes + " B; actions=" + run.Actions + ".");
        }

        [Test, Timeout(5000)]
        public void FractionalDefenseBoundariesAdvanceExactlyOnceAtLargeHostTimes()
        {
            var config = LineConfig.CreateDefault();
            config.ShortWeekDays = 1; config.Durations.DefensePerPlayer = 0.1f;
            var setup = Setup(6, 1927, WeekLength.Short); setup.StartTime = 1000000000.125;
            var engine = LineEngineFactory.Create(setup, config);
            double now = setup.StartTime;
            for (int phase = 0; engine.Phase != LinePhase.FinalDefense && phase < 30; phase++)
            {
                now = engine.PhaseEndTime; engine.Tick(now);
            }
            Assert.AreEqual(LinePhase.FinalDefense, engine.Phase);
            var order = engine.BuildView(0, now).Meeting.DefenseOrder;
            Assert.AreEqual(config.FinalShortlistLarge, order.Count);
            for (int i = 0; i < order.Count; i++)
            {
                var view = engine.BuildView(0, now);
                Assert.AreEqual(order[i], view.Meeting.Defender);
                for (int p = 0; p < 6; p++) Assert.AreEqual(p == order[i], engine.MaySpeak(p));
                now = view.Meeting.DefenderEndTime;
                engine.Tick(now);
            }
            Assert.AreEqual(LinePhase.FinalVote, engine.Phase);
            for (int p = 0; p < 6; p++) Assert.IsTrue(engine.MaySpeak(p));
        }

        private static LineView BotView(RoundType round = RoundType.PrimHavuzu)
        {
            var view = new LineView
            {
                WeekId = 1, Me = 0, Phase = LinePhase.RoundPlay, PhaseEndTime = 150,
                RoundIndex = 0, Round = new LineRoundView { Type = round, Index = 0 },
                Mine = new LineMeView { AgendaTarget = 1, TricksLeftThisWeek = 1, TipsLeftThisWeek = 1 },
            };
            for (int i = 0; i < 4; i++) view.Players.Add(new LinePlayerView { Id = i, Connected = true });
            return view;
        }

        [Test]
        public void BotDoesNotActForAbsentOrWrongRecipientOrExpiredView()
        {
            var bot = new LineBot(0, 9); var config = LineConfig.CreateDefault(); var view = BotView();
            view.Players[0].Connected = false;
            Assert.IsNull(bot.Next(view, config, 40));
            view.Players[0].Connected = true; view.Me = 1;
            Assert.IsNull(bot.Next(view, config, 40));
            view.Me = 0;
            Assert.IsNull(bot.Next(view, config, 150));
        }

        [Test]
        public void BotWaitsForComputerAndEntersOneCodeUsingBothKnownDigits()
        {
            var bot = new LineBot(0, 1); var config = LineConfig.CreateDefault(); var view = BotView(RoundType.OrtakSifre);
            config.MailsPerDay = 0;
            view.Mine.ComputerBlockedUntil = 140; view.Mine.PhoneDeadUntil = 140;
            view.Round.CodeLength = 4; view.Round.DigitPosition = 1; view.Round.Digit = 7;
            view.Round.NeighborPosition = 2; view.Round.NeighborDigit = 8;
            Assert.IsNull(bot.Next(view, config, 100));
            view.Mine.ComputerBlockedUntil = 0;
            var action = bot.Next(view, config, 101);
            Assert.NotNull(action); Assert.AreEqual(LineVerb.SubmitChoice, action.Verb);
            Assert.AreEqual(7, action.A / 100 % 10);
            Assert.AreEqual(8, action.A / 10 % 10);
            Assert.That(action.A, Is.InRange(1000, 9999));
            view.Round.Submitted = true;
            Assert.IsNull(bot.Next(view, config, 139));
        }

        [Test]
        public void BotFinalVotesStayInShortlistEvenWhenAgendaIsOutside()
        {
            var config = LineConfig.CreateDefault();
            for (int seed = 0; seed < 100; seed++)
            {
                var view = BotView(); view.Phase = LinePhase.FinalVote;
                view.Meeting = new LineMeetingView { Shortlist = new List<int> { 0, 2 } };
                view.Players[1].Blame = 99;
                var action = new LineBot(0, seed).Next(view, config, 1);
                Assert.AreEqual(LineVerb.Vote, action.Verb); Assert.AreEqual(2, action.Target);
            }
        }

        [Test]
        public void BotPicksANewManagerOnlyWhenFiredAndNoPickYet()
        {
            var config = LineConfig.CreateDefault(); var view = BotView(); var bot = new LineBot(0, 51);
            view.Phase = LinePhase.WeekResults; view.ManagerProfileId = LineManagerIds.Paragoz;
            view.Result = new LineWeekResult { Fired = 1 };
            Assert.IsNull(bot.Next(view, config, 1));
            view.Result.Fired = 0;
            var action = bot.Next(view, config, 2);
            Assert.AreEqual(LineVerb.PickNextManager, action.Verb);
            Assert.AreNotEqual(view.ManagerProfileId, LineManagerIds.All[action.A]);
            view.Result.NextManagerPick = LineManagerIds.All[action.A];
            Assert.IsNull(bot.Next(view, config, 3));
        }

        [Test]
        public void BotRespectsWeeklyTrickAndTipAllowances()
        {
            var config = LineConfig.CreateDefault(); config.MailsPerDay = 0;
            for (int seed = 0; seed < 100; seed++)
            {
                var view = BotView(); view.Round.Submitted = true;
                view.Features.TricksUnlocked = view.Features.TipLineUnlocked = true;
                view.Mine.TricksLeftThisWeek = view.Mine.TipsLeftThisWeek = 0;
                view.Mine.Trick = TrickType.InternetKes;
                view.Mine.Phone.Status = CallStatus.Dialing;
                var bot = new LineBot(0, seed);
                Assert.IsNull(bot.Next(view, config, 0));
                Assert.IsNull(bot.Next(view, config, 120));
            }
        }

        [Test]
        public void BotCallsOneOfItsTrioPartners()
        {
            var config = LineConfig.CreateDefault(); config.MailsPerDay = 0;
            for (int seed = 0; seed < 100; seed++)
            {
                var view = BotView(); view.Round.Submitted = true;
                view.Round.Partners.AddRange(new[] { 1, 3 });
                var bot = new LineBot(0, seed);
                Assert.IsNull(bot.Next(view, config, 0));
                var action = bot.Next(view, config, 20);
                Assert.AreEqual(LineVerb.Call, action.Verb);
                CollectionAssert.Contains(view.Round.Partners, action.Target);
            }
        }

        [Test]
        public void BotNeverAccusesTheColleagueItsSlipClears()
        {
            var config = LineConfig.CreateDefault();
            for (int seed = 0; seed < 100; seed++)
            {
                var bot = new LineBot(0, seed); var view = BotView(RoundType.KayipRapor);
                view.Round.ClueInnocent = 2; view.Mine.PhoneDeadUntil = 150;
                var action = bot.Next(view, config, 70);
                Assert.NotNull(action); Assert.AreEqual(LineVerb.SubmitChoice, action.Verb);
                Assert.AreNotEqual(0, action.Target); Assert.AreNotEqual(2, action.Target);
            }
        }

        [Test]
        public void BotWaitsForItsPhoneBeforeUsingTheManagerLine()
        {
            var config = LineConfig.CreateDefault(); int tipsAfterRecovery = 0;
            for (int seed = 0; seed < 200; seed++)
            {
                var bot = new LineBot(0, seed); var view = BotView();
                view.Phase = LinePhase.Morning; view.PhaseEndTime = 200;
                view.Features.TipLineUnlocked = true; view.Mine.PhoneDeadUntil = 200;
                Assert.IsNull(bot.Next(view, config, 0));
                Assert.IsNull(bot.Next(view, config, 120));
                view.Mine.PhoneDeadUntil = 0;
                var action = bot.Next(view, config, 121);
                if (action != null) { Assert.AreEqual(LineVerb.TipOff, action.Verb); tipsAfterRecovery++; }
            }
            Assert.Greater(tipsAfterRecovery, 0, "Exercise bots that had decided to use the manager line.");
        }

        [Test]
        public void BonusSharesAndIncomingAnswersUseSpecifiedProbabilities()
        {
            var config = LineConfig.CreateDefault(); int shares = 0, answers = 0;
            for (int seed = 0; seed < 1000; seed++)
            {
                var view = BotView(); view.Mine.PhoneDeadUntil = 150;
                var choice = new LineBot(0, seed).Next(view, config, 70);
                Assert.AreEqual(LineVerb.SubmitChoice, choice.Verb);
                if (choice.A == 0) shares++;
                view.Mine.PhoneDeadUntil = 0; view.Mine.Phone.Status = CallStatus.Ringing;
                var response = new LineBot(0, seed).Next(view, config, 5);
                if (response.Verb == LineVerb.Answer) answers++;
            }
            Assert.That(shares, Is.InRange(540, 660));
            Assert.That(answers, Is.InRange(640, 760));
        }
    }
}
