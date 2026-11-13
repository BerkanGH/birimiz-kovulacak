using System;
using System.Collections.Generic;
using NUnit.Framework;
using BirimizKovulacak.Core.Tests.Line;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class LineValidationTests
    {
        [Test]
        public void VersionsAndDefaultsMatchTheSpec()
        {
            Assert.AreEqual(6, LineProtocol.Version);
            Assert.AreEqual("v5.0-kayit", LineProtocol.RulesetVersion);
            var c = LineConfig.CreateDefault();
            Assert.AreEqual(6, c.ConfigVersion);
            Assert.AreEqual(6, c.StartPrim);
            Assert.AreEqual(new[] { 2, 5, 0, 1 }, new[] { c.BonusPairShare, c.BonusPairTaker, c.BonusPairSharer, c.BonusPairBothTake });
            Assert.AreEqual(new[] { 2, 5, 0, 1 }, new[] { c.BonusTrioNone, c.BonusTrioTaker, c.BonusTrioOthers, c.BonusTrioMany });
            Assert.AreEqual(new[] { 3, 2, 3, 2, 2 }, new[] { c.CashTake, c.CashCaughtBlame, c.CashWhistleBlame, c.CashWhistleReward, c.ShiftSkipPay });
            Assert.AreEqual(new[] { 4, 3, 3, 3 }, new[] { c.InvestigationCost, c.InvestigationGuiltyBlame, c.InvestigationRefund, c.InvestigationFalseBlame });
            Assert.AreEqual(new[] { 1, 4, 10, 1 }, new[] { c.BlamePerVote, c.CrisisBlame, c.TransferMax, c.RecorderUses });
            Assert.AreEqual(new[] { 2, 2, 3, 3 }, new[] { c.CandidatesFor(3), c.CandidatesFor(4), c.CandidatesFor(5), c.CandidatesFor(6) });
            Assert.AreEqual(new[] { 1, 1, 2, 2 }, new[] { c.FiringsFor(3), c.FiringsFor(4), c.FiringsFor(5), c.FiringsFor(6) });
            Assert.AreEqual(new[] { 2, 2, 3, 3 }, new[] { LineConfig.ShiftNeeded(3), LineConfig.ShiftNeeded(4), LineConfig.ShiftNeeded(5), LineConfig.ShiftNeeded(6) });
            var d = c.Durations;
            Assert.AreEqual(new[] { 20f, 60f, 8f, 30f, 15f, 50f, 20f, 6f, 75f, 20f, 8f, 40f, 15f, 25f, 15f, 10f, 45f, 12f },
                new[] { d.Intro, d.TaskPlay, d.TaskReveal, d.Break, d.MeetingDossier, d.MeetingDiscussion, d.MeetingVote, d.MeetingVerdict, d.CrisisCalls,
                        d.CrisisVote, d.CrisisVerdict, d.FinalDiscussion, d.FinalDefense, d.FinalVote, d.FinalRevote, d.FinalVerdict, d.Results, d.Ring });
            Assert.AreEqual(3f, c.EarlyFinishSeconds);
            Assert.AreEqual(1f, c.TransferMinInterval);
        }

        [Test]
        public void InvalidSetupsAreRejected()
        {
            Assert.Throws<ArgumentException>(() => LineEngineFactory.Create(null));
            Assert.Throws<ArgumentException>(() => LineEngineFactory.Create(LineTest.MakeSetup(2)));
            Assert.Throws<ArgumentException>(() => LineEngineFactory.Create(LineTest.MakeSetup(7)));
            var dup = LineTest.MakeSetup();
            dup.Players[1].Id = 0;
            Assert.Throws<ArgumentException>(() => LineEngineFactory.Create(dup));
            var seat = LineTest.MakeSetup();
            seat.Players[0].Id = 6;
            Assert.Throws<ArgumentException>(() => LineEngineFactory.Create(seat));
            var time = LineTest.MakeSetup();
            time.StartTime = double.NaN;
            Assert.Throws<ArgumentException>(() => LineEngineFactory.Create(time));
            var history = LineTest.MakeSetup();
            history.History = new LineSessionHistory { Wins = null };
            Assert.Throws<ArgumentException>(() => LineEngineFactory.Create(history));
            var tooMany = LineTest.MakeSetup();
            tooMany.ForceTasks = new List<TaskKind> { TaskKind.ClientBonus, TaskKind.PettyCash, TaskKind.WeekendShift, TaskKind.ClientBonus };
            Assert.Throws<ArgumentException>(() => LineEngineFactory.Create(tooMany));
        }

        [Test]
        public void GapsInSeatIdsAreFine()
        {
            var s = new LineSetup { WeekId = 1, Seed = 3 };
            foreach (int id in new[] { 0, 2, 5 }) s.Players.Add(new LinePlayerInfo { Id = id, Name = "p" + id });
            var e = LineEngineFactory.Create(s);
            Assert.AreEqual(3, e.BuildView(5, 0).Players.Count);
            Assert.Throws<ArgumentException>(() => e.BuildView(1, 0));
        }

        static void Bad(Action<LineConfig> change)
        {
            var c = LineConfig.CreateDefault();
            change(c);
            Assert.Throws<ArgumentException>(() => LineEngineFactory.Create(LineTest.MakeSetup(), c));
        }

        [Test]
        public void InvalidConfigsAreRejected()
        {
            Bad(c => c.ConfigVersion = 4);
            Bad(c => c.Durations = null);
            Bad(c => c.StartPrim = -1);
            Bad(c => c.CrisisBlame = 2000000);
            Bad(c => c.Durations.TaskPlay = 0);
            Bad(c => c.Durations.Break = float.NaN);
            Bad(c => c.Durations.FinalDefense = -1);
            Bad(c => c.EarlyFinishSeconds = float.PositiveInfinity);
            Bad(c => c.CandidatesByPlayers = new List<int> { 2, 2, 3 });
            Bad(c => c.CandidatesByPlayers = new List<int> { 4, 2, 3, 3 });
            Bad(c => c.FiringsByPlayers = new List<int> { 1, 1, 2, 4 });
            Bad(c => c.FiringsByPlayers = new List<int> { 0, 1, 2, 2 });
            Bad(c => c.TextMaxChars = 513);
            Bad(c => c.MessagesKeptPerPlayer = 0);
            Bad(c => c.PairingTries = 0);
            Assert.DoesNotThrow(() => LineEngineFactory.Create(LineTest.MakeSetup(), LineConfig.CreateDefault()));
        }

        [Test]
        public void EngineKeepsDetachedCopiesOfSetupAndConfig()
        {
            var setup = LineTest.MakeSetup();
            var config = LineConfig.CreateDefault();
            var e = LineEngineFactory.Create(setup, config);
            config.StartPrim = 99;
            setup.Players[0].Name = "changed";
            Assert.AreEqual(6, e.BuildView(0, 0).Mine.Prim);
            Assert.AreEqual("Seat 0", e.BuildView(1, 0).Players[0].Name);
            var view = e.BuildView(0, 0);
            view.Mine.Prim = 1000;
            Assert.AreEqual(6, e.BuildView(0, 0).Mine.Prim);
            e.Config.StartPrim = 1;
            Assert.AreEqual(6, e.Config.StartPrim);
        }

        [Test]
        public void UnknownVerbsAndNullActionsAreBadParams()
        {
            var m = new LineTest();
            m.Fail(LineFailure.BadParam, 0, (LineVerb)99);
            Assert.AreEqual(LineFailure.BadParam, m.Engine.Apply(0, null, 0).Failure);
            Assert.AreEqual(LineFailure.InvalidTarget, m.Engine.Apply(5, new LineAction(42, 1, LineVerb.SendText, 1, text: "x"), 0).Failure);
        }
    }
}
