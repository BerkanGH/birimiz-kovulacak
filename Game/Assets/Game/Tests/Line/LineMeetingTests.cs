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

        [TestCase(false, -1)] [TestCase(true, -1)]
        public void DailyVoteTiesGiveNobodyEvenWithDifferentBlame(bool extraBlame, int winner)
        {
            var m = Quiet(); m.Until(LinePhase.RoundPlay, 0, 1);
            if (extraBlame) m.Ok(1, LineVerb.SubmitChoice, 0, 0);
            m.Until(LinePhase.MeetingVote); m.Ok(0, LineVerb.Vote, 1); m.Ok(1, LineVerb.Vote, 0);
            Assert.IsEmpty(m.View(2).Meeting.Votes); Assert.AreEqual(1, m.View().Meeting.MyVote);
            Assert.IsTrue(m.Player(0).Voted); Assert.AreEqual(LineFailure.Self, m.Do(2, LineVerb.Vote, 2).Failure);
            m.Next(); Assert.AreEqual(LinePhase.MeetingVerdict, m.Engine.Phase); Assert.AreEqual(winner, m.View().Meeting.IhtarTo);
            Assert.IsTrue(m.View().Meeting.Votes.Any(v => v.Voter == 0 && v.Target == 1));
            Assert.AreEqual(0, m.Player(0).Ihtars);
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
            var order = m.View().Meeting.Shortlist.ToArray();
            Assert.AreEqual(2, order.Length); Assert.AreEqual(2, order[0]);
            CollectionAssert.AreEqual(order, m.View().Meeting.DefenseOrder);
            Assert.AreEqual(m.Config.Durations.DefensePerPlayer * 2, m.Engine.PhaseEndTime - m.Now);
            for (int i = 0; i < 2; i++)
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
        public void FinalVotesOutweighHigherBlameAndOnlyShortlistedNamesAreValid()
        {
            var m = Quiet(2); m.Until(LinePhase.MeetingVote); m.Ok(1, LineVerb.Vote, 0); m.Ok(2, LineVerb.Vote, 0);
            m.Until(LinePhase.FinalVote); var shortlist = m.View().Meeting.Shortlist;
            int rival = shortlist.Single(p => p != 0);
            int outside = m.View().Players.First(p => !shortlist.Contains(p.Id)).Id;
            Assert.AreEqual(LineFailure.InvalidTarget, m.Do(0, LineVerb.Vote, outside).Failure);
            Assert.AreEqual(LineFailure.Self, m.Do(0, LineVerb.Vote, 0).Failure);
            foreach (var p in m.View().Players) m.Ok(p.Id, LineVerb.Vote, p.Id == rival ? 0 : rival);
            m.Next();
            var meeting = m.View().Meeting;
            Assert.AreEqual(rival, meeting.Fired);
            Assert.IsTrue(meeting.Scores.All(p => p.Score == p.Votes));
            Assert.AreEqual(4, meeting.Scores.Count);
        }

        [TestCase(false)] [TestCase(true)]
        public void FinalVoteTiesPreferBlameThenIhtars(bool equalBlame)
        {
            var m = Quiet(2, c => { if (equalBlame) c.Profiles.ForEach(p => p.IhtarBlame = 0); });
            m.Until(LinePhase.MeetingVote); m.Ok(1, LineVerb.Vote, 0); m.Ok(2, LineVerb.Vote, 0);
            m.Until(LinePhase.FinalVote); int rival = m.View().Meeting.Shortlist.Single(p => p != 0);
            m.Ok(0, LineVerb.Vote, rival); m.Ok(rival, LineVerb.Vote, 0); m.Next();
            Assert.AreEqual(0, m.View().Meeting.Fired);
        }

        [Test]
        public void AlreadyWarnedPluralityDoesNotFallThroughToRunnerUp()
        {
            var m = Quiet(3); m.Until(LinePhase.MeetingVote);
            m.Ok(1, LineVerb.Vote, 0); m.Ok(2, LineVerb.Vote, 0); m.Next();
            Assert.AreEqual(1, m.Player(0).Ihtars); int blame = m.Player(0).Blame;
            m.Until(LinePhase.MeetingVote, 1);
            m.Ok(1, LineVerb.Vote, 0); m.Ok(2, LineVerb.Vote, 0); m.Ok(0, LineVerb.Vote, 3); m.Next();
            Assert.AreEqual(-1, m.View().Meeting.IhtarTo);
            Assert.AreEqual(1, m.Player(0).Ihtars); Assert.AreEqual(0, m.Player(3).Ihtars);
            Assert.AreEqual(blame, m.Player(0).Blame);
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
                a.Until(LinePhase.FinalVote); int expected = a.View().Meeting.Shortlist[0];
                a.Finish(); b.Finish(); Assert.AreEqual(a.Engine.Result.ResultHash, b.Engine.Result.ResultHash);
                Assert.AreEqual(expected, a.Engine.Result.Fired);
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
            int fired = m.View().Meeting.Shortlist[0];
            for (int p = 0; p < 4; p++) if (p != fired) m.Ok(p, LineVerb.Vote, fired);
            m.Until(LinePhase.WeekResults); var result = m.Engine.Result; Assert.AreEqual(fired, result.Fired);
            int minimum = result.Players.Min(p => p.Blame);
            foreach (var p in result.Players)
            {
                Assert.AreEqual(p.Player != fired, p.Survived); Assert.AreEqual(p.AgendaTarget == fired, p.AgendaHit);
                Assert.AreEqual(p.Blame == minimum && result.Players.Count(x => x.Blame == minimum) == 1, p.EmployeeOfWeek);
                int expected = p.Survived ? m.Config.SurvivePoints + p.AgendaIhtars * m.Config.AgendaPointsPerIhtar + (p.AgendaHit ? m.Config.AgendaPoints : 0) + (p.EmployeeOfWeek ? m.Config.EmployeeOfWeekPoints : 0) : 0;
                Assert.AreEqual(expected, p.Points); Assert.AreEqual(s.History.SessionPoints[p.Player] + expected, p.SessionPoints);
            }
            m.Engine.HostContinue(m.Now); var history = m.Engine.NextHistory(); Assert.AreEqual(2, history.WeeksPlayed);
            CollectionAssert.AreEqual(new[] { LineManagerIds.Paragoz, LineManagerIds.Disiplinci }, history.PastManagers);
            foreach (var p in result.Players) Assert.AreEqual(p.SessionPoints, history.SessionPoints[p.Player]);
            Assert.AreEqual(s.History.TimesFired[fired] + 1, history.TimesFired[fired]); Assert.AreEqual(1, s.History.TimesFired[0]); Assert.AreEqual(1, s.History.WeeksPlayed);
            CollectionAssert.Contains(history.SeenRounds, RoundType.PrimHavuzu); CollectionAssert.Contains(history.SeenRounds, RoundType.IhbarKutusu);
        }

        [TestCase(3, 2)] [TestCase(4, 2)] [TestCase(5, 3)] [TestCase(6, 3)]
        public void ShortlistSizeAndDefenseDurationFollowHeadCount(int players, int shortlistSize)
        {
            var m = new LineTest(LineTest.MakeSetup(players), c => c.ShortWeekDays = 1);
            m.Until(LinePhase.FinalDefense);
            Assert.AreEqual(shortlistSize, m.View().Meeting.Shortlist.Count);
            CollectionAssert.AreEqual(m.View().Meeting.Shortlist, m.View().Meeting.DefenseOrder);
            Assert.AreEqual(shortlistSize * m.Config.Durations.DefensePerPlayer, m.Engine.PhaseEndTime - m.Now);
        }

        [Test]
        public void AgendaWarningPaysSurvivorsAndEmployeeRequiresStrictMinimum()
        {
            var s = LineTest.MakeSetup(); s.ForceAgendas = new List<int> { 1, 0, 0, 1 };
            s.ForceRounds = new List<RoundType> { RoundType.PrimHavuzu, RoundType.TerfiListesi,
                RoundType.PrimHavuzu, RoundType.TerfiListesi };
            var m = new LineTest(s, c => { c.ShortWeekDays = 2; c.Rounds.PrimBothShare = 0; });
            m.Until(LinePhase.MeetingVote); m.Ok(1, LineVerb.Vote, 0); m.Ok(2, LineVerb.Vote, 0);
            m.Until(LinePhase.WeekResults);
            Assert.IsFalse(m.Engine.Result.Players.Any(p => p.EmployeeOfWeek));
            foreach (var player in m.Engine.Result.Players)
            {
                Assert.AreEqual(player.AgendaTarget == 0 ? 1 : 0, player.AgendaIhtars);
                int expected = player.Survived ? m.Config.SurvivePoints + player.AgendaIhtars * m.Config.AgendaPointsPerIhtar +
                    (player.AgendaHit ? m.Config.AgendaPoints : 0) : 0;
                Assert.AreEqual(expected, player.Points);
            }
        }

        [Test]
        public void OnlyFiredPlayerCanPickAnotherManagerDuringResultsAndHistoryCarriesIt()
        {
            var m = Quiet(1);
            Assert.AreEqual(LineFailure.WrongPhase, m.Do(0, LineVerb.PickNextManager, a: 0).Failure);
            m.Until(LinePhase.WeekResults); int fired = m.Engine.Result.Fired;
            int survivor = m.View().Players.First(p => p.Id != fired).Id;
            int current = Array.IndexOf(LineManagerIds.All, m.View().ManagerProfileId);
            int next = (current + 1) % LineManagerIds.All.Length;
            Assert.AreEqual(LineFailure.NotAvailable, m.Do(survivor, LineVerb.PickNextManager, a: next).Failure);
            Assert.AreEqual(LineFailure.BadParam, m.Do(fired, LineVerb.PickNextManager, a: current).Failure);
            Assert.AreEqual(LineFailure.BadParam, m.Do(fired, LineVerb.PickNextManager, a: LineManagerIds.All.Length).Failure);
            ulong before = m.Engine.Result.ResultHash; m.Engine.DrainEvents();
            m.Ok(fired, LineVerb.PickNextManager, a: next);
            Assert.AreEqual(LineManagerIds.All[next], m.View().Result.NextManagerPick);
            Assert.AreNotEqual(before, m.Engine.Result.ResultHash);
            var picked = m.Engine.DrainEvents().Single(e => e.Type == LineEventType.NextManagerPicked);
            Assert.AreEqual(fired, picked.Actor); Assert.AreEqual(next, picked.A); Assert.AreEqual(LineAudience.All, picked.Audience);
            m.Engine.HostContinue(m.Now);
            Assert.AreEqual(LineManagerIds.All[next], m.Engine.NextHistory().NextManager);
            var setup = LineTest.MakeSetup(); setup.ManagerProfileId = ""; setup.History = m.Engine.NextHistory();
            Assert.AreEqual(LineManagerIds.All[next], new LineTest(setup).View().ManagerProfileId);
            setup.ManagerProfileId = LineManagerIds.All[current];
            Assert.AreEqual(LineManagerIds.All[current], new LineTest(setup).View().ManagerProfileId);
        }
    }
}
