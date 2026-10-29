using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests.Line
{
    [TestFixture]
    public sealed class LineMeetingTests
    {
        private static LineTest Quiet(int days = 3, Action<LineConfig> configure = null)
        {
            var s = LineTest.MakeSetup(); s.ForceRounds = new List<RoundType>();
            for (int day = 0; day < days; day++) s.ForceRounds.AddRange(new[] { RoundType.PrimHavuzu, RoundType.IhbarKutusu });
            return new LineTest(s, c => { c.ShortWeekDays = days; c.Rounds.PrimBothShare = 0; configure?.Invoke(c); });
        }

        [TestCase(false, -1)] [TestCase(true, 0)]
        public void DailyVoteTiesUseBlameThenNobody(bool extraBlame, int winner)
        {
            var m = Quiet(); m.Until(LinePhase.RoundPlay, 0, 1);
            if (extraBlame) m.Ok(1, LineVerb.SubmitChoice, 0, 0);
            m.Until(LinePhase.MeetingVote); m.Ok(0, LineVerb.Vote, 1); m.Ok(1, LineVerb.Vote, 0);
            Assert.IsEmpty(m.View(2).Meeting.Votes); Assert.AreEqual(1, m.View().Meeting.MyVote);
            Assert.IsTrue(m.Player(0).Voted); Assert.AreEqual(LineFailure.Self, m.Do(2, LineVerb.Vote, 2).Failure);
            m.Next(); Assert.AreEqual(LinePhase.MeetingVerdict, m.Engine.Phase); Assert.AreEqual(winner, m.View().Meeting.IhtarTo);
            Assert.IsTrue(m.View().Meeting.Votes.Any(v => v.Voter == 0 && v.Target == 1));
            if (extraBlame) { Assert.AreEqual(1, m.Player(0).Ihtars); Assert.AreEqual(6, m.Player(0).Blame); }
        }

        [Test]
        public void VotesCanChangeAbstainAndTargetAbsentPlayers()
        {
            var m = Quiet(); m.Until(LinePhase.MeetingVote); m.Engine.SetConnected(3, false, m.Now);
            m.Ok(0, LineVerb.Vote, 1); m.Ok(0, LineVerb.Vote, -1); Assert.AreEqual(-1, m.View().Meeting.MyVote);
            m.Ok(0, LineVerb.Vote, 3); m.Ok(1, LineVerb.Vote, 3);
            m.Next(); Assert.AreEqual(3, m.View().Meeting.IhtarTo);
        }

        [Test]
        public void FavouriteAlsoReducesWarning()
        {
            var s = LineTest.MakeSetup(); s.ManagerProfileId = LineManagerIds.Kayirmaci;
            var m = new LineTest(s); int favourite = m.View().Favourite; m.Until(LinePhase.MeetingVote);
            for (int p = 0; p < 4; p++) if (p != favourite) m.Ok(p, LineVerb.Vote, favourite);
            int before = m.Player(favourite).Blame; m.Next();
            Assert.AreEqual(before + m.Config.Profile(LineManagerIds.Kayirmaci).IhtarBlame - 1, m.Player(favourite).Blame);
        }

        [Test]
        public void DossierOrdersRoundTraceTipLeakAndRecordings()
        {
            var m = LineTest.WithTrick(TrickType.SahteEposta, c => c.Profiles.ForEach(p => { p.LeakChance = 1; p.TraceChance = 1; }));
            m.Ok(0, LineVerb.UseTrick, 1, 3); m.Ok(2, LineVerb.TipOff, 0); m.Ok(1, LineVerb.SubmitChoice, 0, 2);
            m.Ok(1, LineVerb.Call, 3); m.Ok(3, LineVerb.Answer); m.Advance(10); m.Ok(3, LineVerb.HangUp);
            m.Until(LinePhase.MeetingDossier); var dossier = m.View().Meeting.Dossier;
            CollectionAssert.AreEqual(new[] { DossierKind.RoundOutcome, DossierKind.RoundOutcome, DossierKind.TrickTrace, DossierKind.TipResult, DossierKind.ComplaintLeak, DossierKind.Recording }, dossier.Select(d => d.Kind));
            var leak = dossier.Single(d => d.Kind == DossierKind.ComplaintLeak); Assert.AreEqual(1, leak.Player); Assert.AreEqual(0, leak.Victim);
        }

        [Test]
        public void FinalDefenseOrderFloorAndExactSpeakerBoundaries()
        {
            var m = Quiet(2); m.Until(LinePhase.MeetingVote); m.Ok(1, LineVerb.Vote, 2); m.Ok(0, LineVerb.Vote, 2);
            m.Until(LinePhase.FinalDefense);
            var players = m.View().Players; var order = players.OrderByDescending(p => p.Blame).ThenByDescending(p => p.Ihtars).ThenBy(p => p.Id).Select(p => p.Id).ToArray();
            CollectionAssert.AreEqual(order, m.View().Meeting.DefenseOrder);
            Assert.AreEqual(m.Config.Durations.DefensePerPlayer * 4, m.Engine.PhaseEndTime - m.Now);
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(order[i], m.View().Meeting.Defender);
                Assert.AreEqual(m.Now + m.Config.Durations.DefensePerPlayer, m.View().Meeting.DefenderEndTime);
                for (int p = 0; p < 4; p++) Assert.AreEqual(p == order[i], m.Engine.MaySpeak(p));
                m.Advance(m.Config.Durations.DefensePerPlayer);
            }
            Assert.AreEqual(LinePhase.FinalVote, m.Engine.Phase);
            for (int p = 0; p < 4; p++) Assert.IsTrue(m.Engine.MaySpeak(p));
        }

        [Test]
        public void FiringScoreTiePrefersMoreWarnings()
        {
            var m = Quiet(2); m.Until(LinePhase.MeetingVote); m.Ok(1, LineVerb.Vote, 0); m.Ok(2, LineVerb.Vote, 0);
            m.Until(LinePhase.FinalVote); m.Ok(0, LineVerb.Vote, 1); m.Ok(2, LineVerb.Vote, 1); m.Next();
            var meeting = m.View().Meeting;
            Assert.AreEqual(4, meeting.Scores.Single(p => p.Player == 0).Score);
            Assert.AreEqual(4, meeting.Scores.Single(p => p.Player == 1).Score);
            Assert.AreEqual(0, meeting.Fired);
        }

        [Test]
        public void FiringScoreAndWarningTiePrefersHigherBlame()
        {
            var m = Quiet(1); m.Until(LinePhase.RoundPlay, 0, 1); m.Ok(1, LineVerb.SubmitChoice, 0, 0);
            m.Until(LinePhase.FinalVote); m.Ok(0, LineVerb.Vote, 1); m.Next();
            Assert.AreEqual(0, m.View().Meeting.Fired);
            Assert.AreEqual(2, m.View().Meeting.Scores.Single(p => p.Player == 1).Score);
        }

        [Test]
        public void FullyTiedFiringIsSeededAndReachesDifferentSeats()
        {
            var fired = new HashSet<int>();
            for (int seed = 0; seed < 20; seed++)
            {
                var s = LineTest.MakeSetup(seed: seed);
                var a = new LineTest(s, c => { c.ShortWeekDays = 1; c.Rounds.PrimBothShare = 0; });
                var b = new LineTest(s, c => { c.ShortWeekDays = 1; c.Rounds.PrimBothShare = 0; });
                a.Finish(); b.Finish(); Assert.AreEqual(a.Engine.Result.ResultHash, b.Engine.Result.ResultHash);
                fired.Add(a.Engine.Result.Fired);
            }
            Assert.Greater(fired.Count, 1);
        }

        [Test]
        public void WeekPointsAndNextHistoryAccumulateWithoutMutatingOldHistory()
        {
            var s = LineTest.MakeSetup(); s.History = new LineSessionHistory { WeeksPlayed = 1 };
            s.History.SessionPoints.AddRange(new[] { 10, 20, 30, 40 }); s.History.TimesFired.AddRange(new[] { 1, 2, 3, 4 });
            s.History.PastManagers.Add(LineManagerIds.Paragoz);
            s.ForceRounds = new List<RoundType> { RoundType.PrimHavuzu, RoundType.IhbarKutusu };
            var m = new LineTest(s, c => c.ShortWeekDays = 1); m.Until(LinePhase.FinalVote);
            for (int p = 1; p < 4; p++) m.Ok(p, LineVerb.Vote, 0);
            m.Until(LinePhase.WeekResults); var result = m.Engine.Result; Assert.AreEqual(0, result.Fired);
            int minimum = result.Players.Min(p => p.Blame);
            foreach (var p in result.Players)
            {
                Assert.AreEqual(p.Player != 0, p.Survived); Assert.AreEqual(p.AgendaTarget == 0, p.AgendaHit);
                Assert.AreEqual(p.Blame == minimum, p.EmployeeOfWeek);
                int expected = p.Survived ? m.Config.SurvivePoints + (p.AgendaHit ? m.Config.AgendaPoints : 0) + (p.EmployeeOfWeek ? m.Config.EmployeeOfWeekPoints : 0) : 0;
                Assert.AreEqual(expected, p.Points); Assert.AreEqual(s.History.SessionPoints[p.Player] + expected, p.SessionPoints);
            }
            m.Engine.HostContinue(m.Now); var history = m.Engine.NextHistory(); Assert.AreEqual(2, history.WeeksPlayed);
            CollectionAssert.AreEqual(new[] { LineManagerIds.Paragoz, LineManagerIds.Disiplinci }, history.PastManagers);
            foreach (var p in result.Players) Assert.AreEqual(p.SessionPoints, history.SessionPoints[p.Player]);
            Assert.AreEqual(2, history.TimesFired[0]); Assert.AreEqual(1, s.History.TimesFired[0]); Assert.AreEqual(1, s.History.WeeksPlayed);
            CollectionAssert.Contains(history.SeenRounds, RoundType.PrimHavuzu); CollectionAssert.Contains(history.SeenRounds, RoundType.IhbarKutusu);
        }
    }
}
