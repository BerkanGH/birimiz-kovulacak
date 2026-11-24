using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BirimizKovulacak.Core.Tests.Line;

namespace BirimizKovulacak.Core.Tests
{
    /// <summary>
    /// v5.2 "prim beyanı" (docs/V5_DESIGN.md §5, docs/V5_ENGINE_SPEC.md §22): candidates declare their prim once (they may
    /// lie), from the crisis verdict until a deadline in the final discussion; sealed until all are revealed together; the
    /// real prim is noted at the reveal; a firing carries the fired candidate's declaration. No effect on prim, suç or votes.
    /// </summary>
    [TestFixture]
    public sealed class LineDeclarationTests
    {
        const LineVerb Declare = LineVerb.Declare;

        /// <summary>4 players, clean tasks, a staffed shift (0 and 1 volunteer), a tied crisis vote: 0 and 1 get 2 suç each
        /// and are the two candidates. Stops when the crisis verdict starts. Prim: 0 and 1 have 9, 2 and 3 have 11.</summary>
        static LineTest TwoCandidates(Action<LineConfig> configure = null, int players = 4)
        {
            var m = new LineTest(players, configure: configure);
            m.ToCrisis(id => id < 2 ? LineChoice.Volunteer : LineChoice.Skip);
            m.Next();
            Assert.AreEqual(LinePhase.CrisisVote, m.Engine.Phase);
            foreach (int id in m.Ids) m.Ok(id, LineVerb.Vote, id % 2 == 0 ? 1 : 0);
            return m;
        }

        static LineTest AtVerdict(Action<LineConfig> configure = null)
        {
            var m = TwoCandidates(configure);
            m.Next();
            Assert.AreEqual(LinePhase.CrisisVerdict, m.Engine.Phase);
            CollectionAssert.AreEqual(new[] { 0, 1 }, m.View().Final.Candidates);
            return m;
        }

        static List<LineEvent> Reveals(LineTest m) { return m.Events.Where(e => e.Type == LineEventType.DeclarationsRevealed).ToList(); }

        [Test]
        public void OnlyCandidatesDeclareOnceWithinTheRange()
        {
            var m = TwoCandidates();
            m.Fail(LineFailure.WrongPhase, 0, Declare, a: 5);
            Assert.IsFalse(m.View(0).Features.DeclareOpen, "the candidates are not known yet");
            Assert.AreEqual(0, m.View(0).Final.DeclareUntil);
            m.Next();
            var d = m.Config.Durations;
            Assert.AreEqual(m.Engine.PhaseEndTime + m.Config.DeclareSeconds, m.View(2).Final.DeclareUntil, 1e-9, "public: the verdict's end + the window");
            Assert.IsTrue(m.View(0).Features.DeclareOpen);
            Assert.IsTrue(m.View(1).Features.DeclareOpen);
            Assert.IsFalse(m.View(2).Features.DeclareOpen, "not a candidate");
            m.Fail(LineFailure.NotEligible, 2, Declare, a: 5);
            m.Fail(LineFailure.BadParam, 0, Declare, a: -1);
            m.Fail(LineFailure.BadParam, 0, Declare, a: m.Config.DeclareMax + 1);
            Assert.AreEqual(-1, m.View(0).Final.MyDeclaration);
            m.Ok(0, Declare, a: 7);
            Assert.AreEqual(7, m.View(0).Final.MyDeclaration, "a candidate sees its own declaration at once");
            Assert.IsFalse(m.View(0).Features.DeclareOpen, "once");
            m.Fail(LineFailure.AlreadyUsed, 0, Declare, a: 8);
            Assert.AreEqual(7, m.View(0).Final.MyDeclaration, "no re-declaring");
            m.Ok(1, Declare, a: 0);
            Assert.AreEqual(0, m.View(1).Final.MyDeclaration, "0 is a declaration");
            m.Ok(1, LineVerb.SendMeetingChat, text: "ok");   // nothing else is blocked
            Assert.AreEqual(d.CrisisVerdict, m.Engine.PhaseEndTime - m.Now, 1e-9);
        }

        [Test]
        public void ADeclarationIsSealedUntilTheReveal()
        {
            var m = AtVerdict();
            // what the other candidate and a bystander see, byte for byte (but for the state counter)
            Func<int, byte[]> seen = id => { var v = m.View(id); v.StateVersion = 0; return LineCodec.EncodeView(v); };
            var before = new[] { seen(1), seen(2) };
            int events = m.Events.Count;
            m.Ok(0, Declare, a: 7);
            Assert.AreEqual(events, m.Events.Count, "a declaration announces nothing");
            CollectionAssert.AreEqual(before[0], seen(1), "the other candidate's view carries no trace of it");
            CollectionAssert.AreEqual(before[1], seen(2), "nor a bystander's");
            foreach (int id in m.Ids)
            {
                var f = m.View(id).Final;
                Assert.IsFalse(f.DeclarationsRevealed);
                CollectionAssert.IsEmpty(f.Declarations, "nobody sees whether or what a candidate declared");
                Assert.AreEqual(id == 0 ? 7 : -1, f.MyDeclaration);
            }
            CollectionAssert.IsEmpty(Reveals(m));
            // both declared: still sealed while the crisis verdict runs
            m.Ok(1, Declare, a: 9);
            Assert.IsFalse(m.View(3).Final.DeclarationsRevealed);
            CollectionAssert.IsEmpty(Reveals(m));
        }

        [Test]
        public void EveryoneDeclaredEarly_RevealedWhenTheFinalDiscussionStarts()
        {
            var m = AtVerdict();
            m.Ok(0, Declare, a: 7);
            m.Ok(1, Declare, a: 9);
            m.Next();
            Assert.AreEqual(LinePhase.FinalDiscussion, m.Engine.Phase);
            var reveal = Reveals(m).Single();
            Assert.AreEqual(m.Now, reveal.Time, 1e-9);
            Assert.AreEqual(2, reveal.A);
            Assert.AreEqual(2, reveal.B);
            Assert.AreEqual(LineAudience.All, reveal.Audience);
            int phase = m.Events.FindLastIndex(e => e.Type == LineEventType.PhaseChanged);
            Assert.Less(phase, m.Events.IndexOf(reveal), "the phase change first, then the reveal");
            foreach (int id in m.Ids)
            {
                var f = m.View(id).Final;
                Assert.IsTrue(f.DeclarationsRevealed);
                CollectionAssert.AreEqual(new[] { 0, 1 }, f.Declarations.Select(x => x.Player));
                CollectionAssert.AreEqual(new[] { 7, 9 }, f.Declarations.Select(x => x.Amount));
                Assert.IsTrue(f.Declarations.All(x => x.Actual == -1), "the truth is only in the results");
                Assert.IsFalse(m.View(id).Features.DeclareOpen);
            }
            m.Fail(LineFailure.WrongPhase, 0, Declare, a: 3);
        }

        [Test]
        public void TheLastDeclarationOfTheFinalDiscussionRevealsThemAll()
        {
            var m = AtVerdict();
            m.Ok(0, Declare, a: 7);
            m.Next();
            Assert.AreEqual(LinePhase.FinalDiscussion, m.Engine.Phase);
            Assert.IsFalse(m.View().Final.DeclarationsRevealed, "candidate 1 has not declared");
            Assert.AreEqual(m.Now + m.Config.DeclareSeconds, m.View().Final.DeclareUntil, 1e-9);
            Assert.IsTrue(m.View(1).Features.DeclareOpen);
            Assert.IsFalse(m.View(0).Features.DeclareOpen);
            m.Advance(5);
            CollectionAssert.IsEmpty(Reveals(m));
            m.Ok(1, Declare, a: 4);
            var reveal = Reveals(m).Single();
            Assert.AreEqual(m.Now, reveal.Time, 1e-9, "at the moment the last candidate declared");
            CollectionAssert.AreEqual(new[] { 7, 4 }, m.View(3).Final.Declarations.Select(x => x.Amount));
            m.Advance(m.Config.DeclareSeconds);
            Assert.AreEqual(1, Reveals(m).Count, "only once");
        }

        [Test]
        public void TheDeadlineRevealsWhateverWasDeclared()
        {
            var m = AtVerdict();
            m.Ok(0, Declare, a: 7);
            m.Next();
            double start = m.Now;
            m.Advance(m.Config.DeclareSeconds - 0.1);
            Assert.IsFalse(m.View().Final.DeclarationsRevealed);
            Assert.IsTrue(m.View(1).Features.DeclareOpen);
            m.Advance(0.2);
            var reveal = Reveals(m).Single();
            Assert.AreEqual(start + m.Config.DeclareSeconds, reveal.Time, 1e-9);
            Assert.AreEqual(1, reveal.A);
            Assert.AreEqual(2, reveal.B);
            var list = m.View(2).Final.Declarations;
            CollectionAssert.AreEqual(new[] { 7, -1 }, list.Select(x => x.Amount), "no declaration shows as none");
            m.Fail(LineFailure.WrongPhase, 1, Declare, a: 4);
            Assert.AreEqual(-1, m.View(1).Final.MyDeclaration);
            Assert.AreEqual(LinePhase.FinalDiscussion, m.Engine.Phase, "the discussion goes on");
        }

        [Test]
        public void TheDeadlineIsReachedWithinOneTickAndNeverPassesTheDiscussion()
        {
            // one big Tick over the deadline: the reveal still carries the deadline's time
            var m = AtVerdict();
            m.Next();
            double start = m.Now;
            m.Advance(m.Config.DeclareSeconds + 9);
            Assert.AreEqual(start + m.Config.DeclareSeconds, Reveals(m).Single().Time, 1e-9);
            Assert.AreEqual(0, Reveals(m).Single().A);
            // a window longer than the discussion: revealed when the discussion ends, before the defence
            var late = AtVerdict(c => c.DeclareSeconds = 500);
            late.Next();
            Assert.AreEqual(late.Engine.PhaseEndTime, late.View().Final.DeclareUntil, 1e-9);
            late.Ok(0, Declare, a: 2);
            double end = late.Engine.PhaseEndTime;
            late.Next();
            Assert.AreEqual(LinePhase.FinalDefense, late.Engine.Phase);
            var reveal = Reveals(late).Single();
            Assert.AreEqual(end, reveal.Time, 1e-9);
            int phase = late.Events.FindLastIndex(e => e.Type == LineEventType.PhaseChanged);
            Assert.Greater(phase, late.Events.IndexOf(reveal), "revealed before the defence starts");
            late.Fail(LineFailure.WrongPhase, 1, Declare, a: 2);
            // no window at all: only the crisis verdict is left to declare in
            var none = AtVerdict(c => c.DeclareSeconds = 0);
            none.Ok(1, Declare, a: 5);
            none.Next();
            Assert.AreEqual(none.Now, Reveals(none).Single().Time, 1e-9);
            CollectionAssert.AreEqual(new[] { -1, 5 }, none.View().Final.Declarations.Select(x => x.Amount));
        }

        [Test]
        public void TheRealPrimIsNotedAtTheRevealAndTheFiringCarriesTheDeclaration()
        {
            var m = AtVerdict();
            Assert.AreEqual(9, m.Prim(0));
            m.Ok(0, Declare, a: 5);     // a lie
            m.Ok(1, Declare, a: 9);     // the truth
            m.Ok(2, LineVerb.Transfer, 1, 1);   // before the reveal: counts for the snapshot
            m.Next();
            Assert.AreEqual(LinePhase.FinalDiscussion, m.Engine.Phase);
            m.Advance(2);
            m.Ok(2, LineVerb.Transfer, 0, 3);   // after the reveal: only the desk sees it
            m.Until(LinePhase.FinalVote);
            CollectionAssert.AreEqual(new[] { 5, 9 }, m.View().Final.Declarations.Select(x => x.Amount), "public through the vote");
            foreach (int id in m.Ids) if (id != 0) m.Ok(id, LineVerb.Vote, 0);
            m.Next();
            Assert.AreEqual(LinePhase.FinalVerdict, m.Engine.Phase);
            var firing = m.View(3).Final.Firings.Single();
            Assert.AreEqual(0, firing.Player);
            Assert.AreEqual(5, firing.Declared, "what the desk opening stamps");
            Assert.AreEqual(9, firing.DeclaredActual, "against the prim at the reveal");
            Assert.AreEqual(12, firing.Desk, "9 + the 3 that came in after the reveal");
            Assert.IsFalse(LineStory.Truthful(firing));
            Assert.AreEqual(m.Now, firing.Time, 1e-9);
            m.Next();
            var r = m.View().Results;
            CollectionAssert.AreEqual(new[] { 0, 1 }, r.Declarations.Select(x => x.Player));
            CollectionAssert.AreEqual(new[] { 5, 9 }, r.Declarations.Select(x => x.Amount));
            CollectionAssert.AreEqual(new[] { 9, 10 }, r.Declarations.Select(x => x.Actual), "real prim at the reveal: 9, and 9 + 1");
            Assert.AreEqual(5, r.Firings.Single().Declared);
            Assert.IsTrue(m.View().Final.Declarations.All(x => x.Actual == -1), "the final view never carries the truth");
            // the pair tasks' groups are part of the full disclosure
            Assert.AreEqual(4, r.Groups.Count);
            foreach (var kind in new[] { TaskKind.ClientBonus, TaskKind.PettyCash })
                CollectionAssert.AreEquivalent(m.Ids, r.Groups.Where(g => g.Task == kind).SelectMany(g => g.Members));
        }

        [Test]
        public void PrimThatArrivesAfterTheRevealNeverTurnsATrueDeclarationIntoALie()
        {
            var m = AtVerdict();
            m.Ok(0, Declare, a: 9);     // the truth
            m.Ok(1, Declare, a: 9);
            m.Next();
            m.Advance(2);
            Assert.IsTrue(m.View().Final.DeclarationsRevealed);
            m.Ok(2, LineVerb.Transfer, 0, 3);   // a candidate cannot send, but still receives
            m.Until(LinePhase.FinalVote);
            foreach (int id in m.Ids) if (id != 0) m.Ok(id, LineVerb.Vote, 0);
            m.Next();
            var firing = m.View(3).Final.Firings.Single();
            Assert.AreEqual(12, firing.Desk);
            Assert.AreEqual(9, firing.Declared);
            Assert.AreEqual(9, firing.DeclaredActual);
            Assert.IsTrue(LineStory.Truthful(firing), "DOĞRU: judged against the prim at the reveal, not the desk");
            m.Next();
            var r = m.View().Results;
            var back = LineCodec.DecodeView(LineCodec.EncodeView(new LineView { Results = r })).Results;
            Assert.AreEqual(9, back.Firings.Single().DeclaredActual);
            var story = LineStory.Build(r, m.Engine.Config);
            Assert.AreEqual(0, story.Awards.Single(a => a.Kind == AwardKind.WrongMan).Player, "told the truth and was fired");
            var head = story.Headlines.Single(h => h.Kind == HeadlineKind.Firing);
            CollectionAssert.AreEqual(new[] { 12, 9, 9 }, new[] { head.N1, head.N2, head.N4 });
            // without declarations nothing is stamped
            var off = new LineFiringView { Player = 0, Desk = 12 };
            Assert.IsFalse(LineStory.Truthful(off));
            Assert.IsFalse(LineStory.Truthful(null));
            // (an older / hand-made firing without the snapshot is judged against the desk)
            Assert.IsTrue(LineStory.Truthful(new LineFiringView { Player = 0, Desk = 7, Declared = 7 }));
            Assert.IsFalse(LineStory.Truthful(new LineFiringView { Player = 0, Desk = 8, Declared = 7 }));
        }

        [Test]
        public void DeclarationsStayPublicForTheSecondFiring()
        {
            // 5 players (3 candidates, 2 firings): 0, 1, 2 volunteer; the crisis ties 0 / 1 / 2 ... one vote each is not a
            // tie of all: 3 and 4 vote 0, 0 votes 1, 1 votes 2, 2 votes 1: 0 and 1 tie with 2 votes each → 2 suç each
            var m = new LineTest(5);
            m.ToCrisis(id => id < 3 ? LineChoice.Volunteer : LineChoice.Skip);
            m.Next();
            int[] vote = { 1, 2, 1, 0, 0 };
            foreach (int id in m.Ids) m.Ok(id, LineVerb.Vote, vote[id]);
            m.Next();
            CollectionAssert.AreEqual(new[] { 0, 1 }, m.View().Final.Candidates);
            Assert.AreEqual(2, m.View().Final.FiringsTotal);
            m.Ok(0, Declare, a: 3);
            m.Until(LinePhase.FinalVote);
            foreach (int id in m.Ids) if (id != 0) m.Ok(id, LineVerb.Vote, 0);
            m.Next();
            Assert.AreEqual(3, m.View().Final.Firings.Single().Declared);
            m.Next();
            Assert.AreEqual(LinePhase.FinalVote, m.Engine.Phase);
            Assert.AreEqual(1, m.View().Final.Round);
            var f = m.View(4).Final;
            Assert.IsTrue(f.DeclarationsRevealed);
            CollectionAssert.AreEqual(new[] { 3, -1 }, f.Declarations.Select(x => x.Amount), "still public, the fired one's too");
            m.Fail(LineFailure.WrongPhase, 1, Declare, a: 6);
            foreach (int id in m.Ids) if (id > 1) m.Ok(id, LineVerb.Vote, 1);
            m.Next();
            Assert.AreEqual(-1, m.View().Final.Firings[1].Declared, "no declaration: nothing to stamp");
            Assert.IsFalse(LineStory.Truthful(m.View().Final.Firings[1]));
            m.Finish();
            CollectionAssert.AreEqual(new[] { 3, -1 }, m.Engine.Result.Firings.Select(x => x.Declared));
        }

        [Test]
        public void OffByConfigAndWithoutCandidates()
        {
            var off = AtVerdict(c => c.DeclarationsEnabled = false);
            off.Fail(LineFailure.NotAvailable, 0, Declare, a: 5);
            Assert.IsFalse(off.View(0).Features.DeclareOpen);
            Assert.AreEqual(0, off.View(0).Final.DeclareUntil);
            off.Until(LinePhase.FinalVote);
            foreach (int id in off.Ids) if (id != 0) off.Ok(id, LineVerb.Vote, 0);
            off.Finish();
            CollectionAssert.IsEmpty(Reveals(off));
            CollectionAssert.IsEmpty(off.Engine.Result.Declarations);
            Assert.AreEqual(-1, off.Engine.Result.Firings.Single().Declared);
            // nobody has suç: no candidates, no final, nothing to declare
            var none = new LineTest(6);
            none.ToCrisis(id => id < 3 ? LineChoice.Volunteer : LineChoice.Skip);
            none.Next();
            none.Next();
            Assert.AreEqual(LinePhase.CrisisVerdict, none.Engine.Phase);
            CollectionAssert.IsEmpty(none.View().Final.Candidates);
            none.Fail(LineFailure.WrongPhase, 0, Declare, a: 5);
            Assert.AreEqual(0, none.View().Final.DeclareUntil);
            none.Finish();
            CollectionAssert.IsEmpty(none.Engine.Result.Declarations);
        }

        [Test]
        public void DeclaringChangesNoPrimSucVoteOrWinner()
        {
            LineWeekResult Play(bool declare)
            {
                var m = AtVerdict();
                if (declare) { m.Ok(0, Declare, a: 1); m.Ok(1, Declare, a: 99); }
                m.Until(LinePhase.FinalVote);
                foreach (int id in m.Ids) if (id != 1) m.Ok(id, LineVerb.Vote, 1);
                m.Finish();
                return m.Engine.Result;
            }
            var a = Play(false);
            var b = Play(true);
            CollectionAssert.AreEqual(a.Players.Select(p => p.Prim), b.Players.Select(p => p.Prim));
            CollectionAssert.AreEqual(a.Players.Select(p => p.Blame), b.Players.Select(p => p.Blame));
            CollectionAssert.AreEqual(a.Fired, b.Fired);
            CollectionAssert.AreEqual(a.Winners, b.Winners);
            Assert.AreEqual(a.Ballots.Count, b.Ballots.Count);
            Assert.AreEqual(a.PrimLog.Count, b.PrimLog.Count);
            Assert.AreEqual(a.EndTime, b.EndTime, 1e-9, "the reveal never changes a phase length");
            Assert.AreNotEqual(a.ResultHash, b.ResultHash, "the declarations themselves are part of the result");
            CollectionAssert.AreEqual(new[] { -1, -1 }, a.Declarations.Select(x => x.Amount), "nobody declared: revealed as none at the deadline");
            CollectionAssert.AreEqual(new[] { 1, 99 }, b.Declarations.Select(x => x.Amount));
        }
    }
}
