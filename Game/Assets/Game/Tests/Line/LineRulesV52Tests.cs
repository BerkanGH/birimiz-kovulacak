using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BirimizKovulacak.Core.Tests.Line;
using P = BirimizKovulacak.Core.LinePhase;

namespace BirimizKovulacak.Core.Tests
{
    /// <summary>
    /// The v5.2 simplification (2026-11-24, docs/V5_DESIGN.md §4.1, §8.2): no meeting 2 (every consequence of the Weekend
    /// Shift lands at its reveal and the crisis follows; the old flow stays behind <see cref="LineConfig.ShiftMeeting"/>),
    /// and the lobby's talk-time option. (The prim declarations are in LineDeclarationTests, the results' story in
    /// LineStoryTests.)
    /// </summary>
    [TestFixture]
    public sealed class LineRulesV52Tests
    {
        [TestCase(3), TestCase(4), TestCase(5), TestCase(6)]
        public void AShortShiftBlamesItsSkippersAtTheRevealAndTheCrisisFollows(int n)
        {
            var m = new LineTest(n);
            m.ToMeeting1();
            m.Until(P.TaskPlay);
            int needed = LineConfig.ShiftNeeded(n);
            var prim = m.Ids.Select(m.Prim).ToArray();
            int before = m.Events.Count;
            m.PlayTask(2, id => id < needed - 1 ? LineChoice.Volunteer : LineChoice.Skip);   // one short
            Assert.AreEqual(P.TaskReveal, m.Engine.Phase);
            double reveal = m.Now;
            // right at the reveal: no prim for anyone, +2 suç for every skipper, publicly
            CollectionAssert.AreEqual(prim, m.Ids.Select(m.Prim).ToArray(), "an unstaffed shift pays nobody");
            foreach (int id in m.Ids) Assert.AreEqual(id < needed - 1 ? 0 : 2, m.Blame(id), "seat " + id);
            var blamed = m.Events.Skip(before).Where(e => e.Type == LineEventType.BlameAdded && e.B == (int)BlameReason.ShiftFailSkip).ToList();
            CollectionAssert.AreEqual(Enumerable.Range(needed - 1, n - needed + 1), blamed.Select(e => e.Target));
            Assert.IsTrue(blamed.All(e => e.A == 2 && e.Audience == LineAudience.All && Math.Abs(e.Time - reveal) < 1e-9));
            var task = m.View(n - 1).Task;
            Assert.IsTrue(task.Revealed);
            Assert.Less(task.Roster.Count, task.Needed, "the public roster says the shift was short");
            Assert.IsNull(m.View().Meeting, "no meeting is running");
            // then straight to the crisis calls; its dossier carries the roster and the penalty for the meeting room
            m.Next();
            Assert.AreEqual(P.CrisisCalls, m.Engine.Phase);
            Assert.AreEqual(2, m.Engine.Day);
            Assert.AreEqual((int)PhaseEndReason.Timer, m.Events.Last(e => e.Type == LineEventType.PhaseChanged).B, "nothing was skipped");
            foreach (int id in m.Ids) Assert.AreEqual(id < needed - 1 ? 0 : 2, m.Blame(id), "nothing more when the crisis starts");
            var meeting = m.View(0).Meeting;
            Assert.AreEqual(2, meeting.Index);
            CollectionAssert.IsEmpty(meeting.FailedTasks);
            Assert.AreEqual(-1, meeting.VoteableCount);
            CollectionAssert.AreEqual(Enumerable.Range(0, needed - 1), meeting.Roster);
            CollectionAssert.AreEqual(new[] { DossierKind.ShiftRoster, DossierKind.ShiftSkipBlame }, meeting.Dossier.Select(d => d.Kind));
            Assert.AreEqual(needed - 1, meeting.Dossier[0].N1);
            Assert.AreEqual(needed, meeting.Dossier[0].N2);
            Assert.AreEqual(2, meeting.Dossier[1].N1);
            Assert.AreEqual(n - needed + 1, meeting.Dossier[1].N2);
            // the lines stay through the crisis vote and the final
            m.Next();
            Assert.AreEqual(P.CrisisVote, m.Engine.Phase);
            Assert.AreEqual(2, m.View(1).Meeting.Dossier.Count);
            m.Finish();
            var r = m.Engine.Result;
            Assert.AreEqual(n - needed + 1, r.BlameLog.Count(b => b.Reason == BlameReason.ShiftFailSkip && b.Delta == 2 && Math.Abs(b.Time - reveal) < 1e-9));
            Assert.IsFalse(r.Ballots.Any(b => b.Kind == BallotKind.Meeting), "no meeting vote for the shift");
            CollectionAssert.DoesNotContain(m.PhaseTrace().Skip(12), P.MeetingDossier, "no meeting 2");
            foreach (var p in r.Players) Assert.AreEqual(p.Blame, r.BlameLog.Where(b => b.Player == p.Player).Sum(b => b.Delta), "the log adds up");
        }

        [Test]
        public void TheSkippersSucCountsForTheCandidatesAndTheVolunteersAreCleanedFirst()
        {
            // 4 players: seat 0 took the Client Bonus and got 2 suç in the meeting; it volunteers alone (short): −1 for it,
            // +2 for the three skippers, all at the reveal
            var m = new LineTest();
            m.PlayTask(0, id => id == 0 ? LineChoice.Take : 0);
            m.PlayTask(1, id => 0);
            m.Until(P.MeetingVote);
            int[] vote = { 3, 0, 0, 2 };
            foreach (int id in m.Ids) m.Ok(id, LineVerb.Vote, vote[id], 0);
            m.Next();
            CollectionAssert.AreEqual(new[] { 2, 0, 1, 1 }, m.Ids.Select(m.Blame).ToArray());
            m.PlayTask(2, id => id == 0 ? LineChoice.Volunteer : LineChoice.Skip);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 3 }, m.Ids.Select(m.Blame).ToArray());
            var order = m.Events.Where(e => e.Type == LineEventType.BlameAdded && (e.B == (int)BlameReason.ShiftClean || e.B == (int)BlameReason.ShiftFailSkip)).Select(e => e.A).ToList();
            CollectionAssert.AreEqual(new[] { -1, 2, 2, 2 }, order, "the cleaning first, then the skippers");
            m.Until(P.CrisisVote);
            foreach (int id in m.Ids) m.Ok(id, LineVerb.Vote, id == 1 ? 0 : 1);
            m.Next();
            CollectionAssert.AreEqual(new[] { 1, 6, 3, 3 }, m.Ids.Select(m.Blame).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, m.View().Final.Candidates, "1 leads; 2 and 3 tie for the second place");
        }

        [Test]
        public void AStaffedShiftChangesNothingButThePayAndTheCleaning()
        {
            var m = new LineTest(5);
            m.ToMeeting1();
            m.PlayTask(2, id => id < 3 ? LineChoice.Volunteer : LineChoice.Skip);
            Assert.IsTrue(m.Ids.All(id => m.Blame(id) == 0));
            Assert.IsFalse(m.Events.Any(e => e.Type == LineEventType.BlameAdded && e.B == (int)BlameReason.ShiftFailSkip));
            m.Next();
            Assert.AreEqual(P.CrisisCalls, m.Engine.Phase);
            CollectionAssert.IsEmpty(m.View().Meeting.Dossier);
            CollectionAssert.IsEmpty(m.View().Meeting.Roster);
        }

        [Test]
        public void TheOldSwitchKeepsTheV51AndV50Flows()
        {
            // v5.1: meeting 2 without a vote, the suç when it opens
            var v51 = new LineTest(4, configure: LineTest.V51);
            v51.ToMeeting1();
            v51.PlayTask(2, id => 0);
            Assert.IsTrue(v51.Ids.All(id => v51.Blame(id) == 0), "nothing at the reveal");
            v51.Next();
            Assert.AreEqual(P.MeetingDossier, v51.Engine.Phase);
            Assert.AreEqual(1, v51.Engine.Day);
            Assert.IsTrue(v51.Ids.All(id => v51.Blame(id) == 2));
            Assert.AreEqual(0, v51.View().Meeting.VoteableCount);
            v51.Until(P.CrisisCalls);
            CollectionAssert.IsEmpty(v51.View().Meeting.Dossier, "the crisis dossier is not used for the shift");
            CollectionAssert.DoesNotContain(v51.PhaseTrace(), P.MeetingVote);
            // v5.0: meeting 2 with a vote, no automatic suç, the skippers are paid
            var v50 = new LineTest(4, configure: LineTest.V50);
            v50.ToMeeting1();
            int before = v50.Prim(0);
            v50.PlayTask(2, id => 0);
            Assert.AreEqual(before + 2, v50.Prim(0));
            v50.Until(P.MeetingVote, 1);
            Assert.AreEqual(1, v50.View().Meeting.VoteableCount);
            Assert.IsTrue(v50.Ids.All(id => v50.Blame(id) == 0));
            v50.Finish();
            CollectionAssert.IsEmpty(v50.Engine.Result.Declarations);
            // the ShiftFailVote switch alone means nothing without the meeting
            var noMeeting = new LineTest(4, configure: c => LineTest.Legacy(c).ShiftFailVote = true);
            noMeeting.ToMeeting1();
            noMeeting.PlayTask(2, id => 0);
            noMeeting.Next();
            Assert.AreEqual(P.CrisisCalls, noMeeting.Engine.Phase);
        }

        // ------------------------------------------------------------------ lobby option "Konuşma süresi"

        static LineTest WithTalk(int talk, Action<LineConfig> configure = null)
        {
            return new LineTest(4, configure: configure, prepare: s => s.TalkTime = talk);
        }

        [TestCase(LineTalkTime.Short, 0.7), TestCase(LineTalkTime.Normal, 1.0), TestCase(LineTalkTime.Long, 1.5)]
        public void TalkTimeScalesOnlyTheTalkingPhases(int talk, double factor)
        {
            var m = WithTalk(talk);
            var given = m.Config.Durations;        // what the host passed in
            var d = m.Engine.Config.Durations;     // what the match plays
            Assert.AreEqual(talk, m.Engine.Setup.TalkTime);
            Assert.AreEqual(given.MeetingDiscussion * factor, d.MeetingDiscussion, 1e-4);
            Assert.AreEqual(given.CrisisCalls * factor, d.CrisisCalls, 1e-4);
            Assert.AreEqual(given.FinalDiscussion * factor, d.FinalDiscussion, 1e-4);
            Assert.AreEqual(given.FinalDefense * factor, d.FinalDefense, 1e-4);
            CollectionAssert.AreEqual(
                new[] { given.Intro, given.TaskPlay, given.TaskReveal, given.Break, given.MeetingDossier, given.MeetingVote, given.MeetingVerdict, given.CrisisVote,
                        given.CrisisVerdict, given.FinalVote, given.FinalRevote, given.FinalVerdict, given.Results, given.Ring },
                new[] { d.Intro, d.TaskPlay, d.TaskReveal, d.Break, d.MeetingDossier, d.MeetingVote, d.MeetingVerdict, d.CrisisVote,
                        d.CrisisVerdict, d.FinalVote, d.FinalRevote, d.FinalVerdict, d.Results, d.Ring }, "everything else keeps its length");
            // and the phases really last that long
            var seen = new Dictionary<P, double>();
            int defenders = 0;
            while (!m.Engine.IsFinished)
            {
                if (!seen.ContainsKey(m.Engine.Phase)) seen[m.Engine.Phase] = m.Engine.PhaseEndTime - m.Now;
                if (m.Engine.Phase == P.FinalDefense) defenders = m.View().Final.DefenseOrder.Count;
                m.Next();
            }
            Assert.AreEqual(50 * factor, seen[P.MeetingDiscussion], 1e-4);
            Assert.AreEqual(75 * factor, seen[P.CrisisCalls], 1e-4);
            Assert.AreEqual(40 * factor, seen[P.FinalDiscussion], 1e-4);
            Assert.AreEqual(15 * factor * defenders, seen[P.FinalDefense], 1e-3);
            Assert.AreEqual(15, seen[P.MeetingDossier], 1e-6);
            Assert.AreEqual(20, seen[P.CrisisVote], 1e-6);
            Assert.AreEqual(60, seen[P.TaskPlay], 1e-6);
        }

        [Test]
        public void TalkTimeIsValidatedAndUsesTheConfigFactors()
        {
            Assert.Throws<ArgumentException>(() => WithTalk(3));
            Assert.Throws<ArgumentException>(() => WithTalk(-1));
            Assert.Throws<ArgumentException>(() => WithTalk(LineTalkTime.Short, c => c.TalkTimeShort = 0f));
            var m = WithTalk(LineTalkTime.Long, c => { c.TalkTimeLong = 2f; c.Durations.Scale(0.5f); });
            Assert.AreEqual(50 * 0.5 * 2, m.Engine.Config.Durations.MeetingDiscussion, 1e-4, "on top of the duration scale");
            Assert.AreEqual(60 * 0.5, m.Engine.Config.Durations.TaskPlay, 1e-4);
            var c0 = LineConfig.CreateDefault();
            Assert.AreEqual(1f, c0.TalkFactor(7), "an unknown level is normal");
            Assert.IsTrue(LineTalkTime.Valid(0) && LineTalkTime.Valid(2) && !LineTalkTime.Valid(3));
            CollectionAssert.AreEqual(new[] { "short", "normal", "long", "" }, new[] { LineTalkTime.Id(0), LineTalkTime.Id(1), LineTalkTime.Id(2), LineTalkTime.Id(9) });
            // the declaration window follows the (scaled) final discussion but is not scaled itself
            var s = WithTalk(LineTalkTime.Short, c => c.Durations.FinalDiscussion = 10f);   // 7 s of discussion < 12 s window
            s.ToCrisis();
            s.Until(P.FinalDiscussion);
            Assert.AreEqual(s.Engine.PhaseEndTime, s.View().Final.DeclareUntil, 1e-6);
        }

        [Test]
        public void TalkTimeNeverChangesTheOutcome()
        {
            // bots with no think time decide at each phase start: the same match at every talk time
            LineWeekResult Play(int talk)
            {
                var setup = LineTest.MakeSetup(5, 77);
                setup.TalkTime = talk;
                var config = LineConfig.CreateDefault();
                var engine = LineEngineFactory.Create(setup, config);
                var bots = Enumerable.Range(0, 5).Select(i => new LineBot(i, 77 * 31 + i, (LineBotPolicy)(i % 3))).ToArray();
                double now = 0;
                for (int step = 0; step < 2000 && !engine.IsFinished; step++)
                {
                    engine.Tick(now);
                    for (int round = 0; round < 8; round++)
                    {
                        bool acted = false;
                        for (int i = 0; i < 5; i++)
                        {
                            var a = bots[i].Next(engine.BuildView(i, now), engine.Config, now);
                            if (a == null) continue;
                            engine.Apply(i, a, now);
                            acted = true;
                        }
                        if (!acted) break;
                    }
                    if (engine.Phase == P.Results) { engine.HostContinue(now); break; }
                    now = engine.PhaseEndTime;
                }
                return engine.Result;
            }
            var normal = Play(LineTalkTime.Normal);
            foreach (int talk in new[] { LineTalkTime.Short, LineTalkTime.Long })
            {
                var other = Play(talk);
                CollectionAssert.AreEqual(normal.Players.Select(p => p.Prim), other.Players.Select(p => p.Prim));
                CollectionAssert.AreEqual(normal.Fired, other.Fired);
                Assert.AreNotEqual(normal.EndTime, other.EndTime);
            }
        }
    }
}
