using System.Linq;
using NUnit.Framework;
using BirimizKovulacak.Core.Tests.Line;

namespace BirimizKovulacak.Core.Tests
{
    /// <summary>
    /// The v5.1 rules (2026-11-15, docs/V5_DESIGN.md §6, §8.2, §12): the Weekend Shift's volunteer cleaning and the unstaffed
    /// shift's automatic suç, candidates who need suç, and the no-candidate path. (Client Bonus 3/3, the free sharer-only
    /// investigation and the whistled thief who keeps the cash are in LineTaskTests / LineMeetingTests.)
    /// </summary>
    [TestFixture]
    public sealed class LineRulesV51Tests
    {
        const int Take = LineChoice.Take;

        /// <summary>4 players: seat 0 takes the Client Bonus; meeting 1 votes 1→0, 2→0, 3→2, 0→3: suç 0:2, 1:0, 2:1, 3:1.</summary>
        static LineTest SucBeforeTheShift(System.Action<LineConfig> configure = null)
        {
            var m = new LineTest(configure: configure);
            m.PlayTask(0, id => id == 0 ? Take : 0);
            m.PlayTask(1, id => 0);
            m.Until(LinePhase.MeetingVote);
            int[] vote = { 3, 0, 0, 2 };
            foreach (int id in m.Ids) m.Ok(id, LineVerb.Vote, vote[id], 0);
            m.Next();
            CollectionAssert.AreEqual(new[] { 2, 0, 1, 1 }, m.Ids.Select(m.Blame).ToArray());
            return m;
        }

        [Test]
        public void ShiftVolunteersLoseOneSucNeverBelowZero()
        {
            var m = SucBeforeTheShift();
            int before3 = -1;
            for (int i = 0; i < 20 && !(m.Engine.Phase == LinePhase.TaskPlay && m.View().TaskNumber == 2); i++) m.Next();
            before3 = m.Prim(3);
            int before0 = m.Prim(0);
            m.PlayTask(2, id => id < 3 ? LineChoice.Volunteer : LineChoice.Skip);   // staffed: 3 of 2
            CollectionAssert.AreEqual(new[] { 1, 0, 0, 1 }, m.Ids.Select(m.Blame).ToArray(), "0: 2−1, 1: stays 0, 2: 1−1, 3 skipped");
            Assert.AreEqual(before0, m.Prim(0), "a volunteer gets no prim");
            Assert.AreEqual(before3 + 2, m.Prim(3), "staffed: the skipper gets 2");
            var cleans = m.Events.Where(e => e.Type == LineEventType.BlameAdded && e.B == (int)BlameReason.ShiftClean).ToList();
            CollectionAssert.AreEquivalent(new[] { 0, 2 }, cleans.Select(e => e.Target));
            Assert.IsTrue(cleans.All(e => e.A == -1), "announced as a negative change");
            m.Next();
            Assert.AreEqual(LinePhase.CrisisCalls, m.Engine.Phase, "staffed: the crisis follows");
            CollectionAssert.IsEmpty(m.View().Meeting.Dossier, "a staffed shift adds nothing to the crisis dossier");
            m.Finish();
            var log = m.Engine.Result.BlameLog.Where(b => b.Reason == BlameReason.ShiftClean).ToList();
            Assert.AreEqual(2, log.Count);
            Assert.IsTrue(log.All(b => b.Delta == -1));
            foreach (var p in m.Engine.Result.Players) Assert.AreEqual(p.Blame, m.Engine.Result.BlameLog.Where(b => b.Player == p.Player).Sum(b => b.Delta));
        }

        [Test]
        public void AnUnstaffedShiftStillCleansItsVolunteersAndBlamesItsSkippers()
        {
            // v5.2: everything lands at the reveal; the crisis segment's dossier carries the line
            var m = SucBeforeTheShift();
            for (int i = 0; i < 20 && !(m.Engine.Phase == LinePhase.TaskPlay && m.View().TaskNumber == 2); i++) m.Next();
            int prim3 = m.Prim(3);
            m.PlayTask(2, id => id == 0 ? LineChoice.Volunteer : LineChoice.Skip);   // 1 of 2: not staffed
            Assert.AreEqual(LinePhase.TaskReveal, m.Engine.Phase);
            Assert.AreEqual(prim3, m.Prim(3), "unstaffed: the skippers get nothing");
            CollectionAssert.AreEqual(new[] { 1, 0 + 2, 1 + 2, 1 + 2 }, m.Ids.Select(m.Blame).ToArray(), "cleaned and blamed at the reveal");
            m.Next();
            Assert.AreEqual(LinePhase.CrisisCalls, m.Engine.Phase);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 3 }, m.Ids.Select(m.Blame).ToArray(), "nothing more when the crisis starts");
            var line = m.View().Meeting.Dossier.Single(d => d.Kind == DossierKind.ShiftSkipBlame);
            Assert.AreEqual(2, line.N1);
            Assert.AreEqual(3, line.N2);
            CollectionAssert.AreEqual(new[] { 0 }, m.View().Meeting.Roster);
        }

        [Test]
        public void V51AnUnstaffedShiftBlamesItsSkippersWhenMeeting2Opens()
        {
            var m = SucBeforeTheShift(LineTest.V51);
            for (int i = 0; i < 20 && !(m.Engine.Phase == LinePhase.TaskPlay && m.View().TaskNumber == 2); i++) m.Next();
            int prim3 = m.Prim(3);
            m.PlayTask(2, id => id == 0 ? LineChoice.Volunteer : LineChoice.Skip);   // 1 of 2: not staffed
            Assert.AreEqual(1, m.Blame(0), "cleaned at the reveal, staffed or not");
            Assert.AreEqual(prim3, m.Prim(3), "unstaffed: the skippers get nothing");
            Assert.AreEqual(1, m.Blame(3), "the skippers' suç waits for meeting 2");
            m.Next();
            Assert.AreEqual(LinePhase.MeetingDossier, m.Engine.Phase);
            CollectionAssert.AreEqual(new[] { 1, 0 + 2, 1 + 2, 1 + 2 }, m.Ids.Select(m.Blame).ToArray());
            var line = m.View().Meeting.Dossier.Single(d => d.Kind == DossierKind.ShiftSkipBlame);
            Assert.AreEqual(2, line.N1);
            Assert.AreEqual(3, line.N2);
            CollectionAssert.AreEqual(new[] { 0 }, m.View().Meeting.Roster);
        }

        [Test]
        public void ZeroSucIsNeverACandidateAndFiringsNeverExceedTheCandidates()
        {
            // 5 players (3 candidates, 2 firings): clean tasks, a staffed shift, the crisis gives 4 suç to seat 0 only
            var m = new LineTest(5);
            m.ToCrisis(id => id < 3 ? LineChoice.Volunteer : LineChoice.Skip);
            m.Next();
            foreach (int id in m.Ids) m.Ok(id, LineVerb.Vote, id == 0 ? 1 : 0);
            m.Next();
            Assert.AreEqual(LinePhase.CrisisVerdict, m.Engine.Phase);
            Assert.AreEqual(4, m.Blame(0));
            Assert.IsTrue(m.Ids.Where(id => id != 0).All(id => m.Blame(id) == 0));
            var final = m.View().Final;
            CollectionAssert.AreEqual(new[] { 0 }, final.Candidates, "only seat 0 has suç");
            Assert.AreEqual(1, final.FiringsTotal, "min(2 firings, 1 candidate)");
            m.Until(LinePhase.FinalVote);
            CollectionAssert.AreEqual(new[] { 0 }, m.View().Final.Options);
            foreach (int id in m.Ids) if (id != 0) m.Ok(id, LineVerb.Vote, 0);
            m.Next();
            m.Next();
            Assert.AreEqual(LinePhase.Results, m.Engine.Phase, "one firing, then the results");
            CollectionAssert.AreEqual(new[] { 0 }, m.Engine.Result.Fired);
        }

        [Test]
        public void FewerQualifyingPlayersThanCandidateSlotsAreAllCandidates()
        {
            // 6 players, 3 candidate slots: suç 4 (crisis) and 2 (one Client Bonus taker voted in meeting 1), the rest 0
            var m = new LineTest(6);
            m.PlayTask(0, id => id == 0 ? Take : 0);
            m.PlayTask(1, id => 0);
            m.Until(LinePhase.MeetingVote);
            foreach (int id in m.Ids) m.Ok(id, LineVerb.Vote, id == 1 ? 2 : 1, 0);
            m.Until(LinePhase.TaskPlay);
            m.PlayTask(2, id => id < 3 ? LineChoice.Volunteer : LineChoice.Skip);
            m.Until(LinePhase.CrisisVote);
            foreach (int id in m.Ids) m.Ok(id, LineVerb.Vote, id == 2 ? 3 : 2);
            m.Next();
            var blame = m.Ids.Select(m.Blame).ToArray();
            Assert.AreEqual(0, blame[0]);
            Assert.AreEqual(5 - 1, blame[1], "5 ballots, minus the volunteer cleaning");
            Assert.AreEqual(1 - 1 + 4, blame[2]);
            var final = m.View().Final;
            CollectionAssert.AreEqual(new[] { 1, 2 }, final.Candidates);
            Assert.AreEqual(2, final.FiringsTotal);
        }

        [Test]
        public void NobodyWithSucMeansNoCandidatesNoFiringAndStraightToTheResults()
        {
            // 6 players: clean tasks, a staffed shift and an empty crisis vote (6-way tie: 4 / 6 = 0 suç each)
            var m = new LineTest(6);
            m.ToCrisis(id => id < 3 ? LineChoice.Volunteer : LineChoice.Skip);
            m.Next();
            m.Next();
            Assert.AreEqual(LinePhase.CrisisVerdict, m.Engine.Phase);
            Assert.IsTrue(m.Ids.All(id => m.Blame(id) == 0));
            var final = m.View().Final;
            CollectionAssert.IsEmpty(final.Candidates);
            Assert.AreEqual(0, final.FiringsTotal);
            m.Next();
            Assert.AreEqual(LinePhase.Results, m.Engine.Phase, "no final discussion, defence or vote");
            var ended = m.Events.Last(e => e.Type == LineEventType.PhaseChanged);
            Assert.AreEqual((int)LinePhase.Results, ended.A);
            Assert.AreEqual((int)PhaseEndReason.Skipped, ended.B);
            var r = m.View().Results;
            Assert.IsNotNull(r);
            CollectionAssert.IsEmpty(r.Fired);
            CollectionAssert.IsEmpty(r.Firings);
            CollectionAssert.AreEquivalent(new[] { 3, 4, 5 }, r.Winners, "the skippers of the staffed shift: 6 + 3 + 2");
            Assert.IsFalse(r.Players.Any(p => p.WasCandidate));
            m.Next();
            Assert.IsTrue(m.Engine.IsFinished);
            CollectionAssert.DoesNotContain(m.PhaseTrace(), LinePhase.FinalDiscussion);
        }

        [Test]
        public void V50CandidatesIncludeZeroSucTies()
        {
            var m = new LineTest(6, configure: LineTest.V50);
            m.ToCrisis(id => id < 3 ? LineChoice.Volunteer : LineChoice.Skip);
            m.Next();
            m.Next();
            Assert.AreEqual(6, m.View().Final.Candidates.Count, "v5.0: everyone tied at 0 suç is a candidate");
            m.Finish();
            Assert.AreEqual(2, m.Engine.Result.Fired.Count);
        }
    }
}
