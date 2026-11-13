using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BirimizKovulacak.Core.Tests.Line;

namespace BirimizKovulacak.Core.Tests
{
    /// <summary>Yaramazlıklar (docs/V5_YARAMAZLIKLAR.md, docs/V5_ENGINE_SPEC.md §19): the Prank verb, the manager's patience,
    /// tutanaks, sessions, privacy, and the bots' prank policy (driven by the talk layer, never by LineBot).</summary>
    [TestFixture]
    public sealed class LinePrankTests
    {
        static LineActionResult Prank(LineTest m, int actor, PrankKind kind, int target = -1, int word = -1, string note = "", int guard = -1)
        {
            return m.DoFull(actor, LineVerb.Prank, target, (int)kind, word, guard, note);
        }

        static void Ok(LineTest m, int actor, PrankKind kind, int target = -1, int word = -1, string note = "")
        {
            var r = Prank(m, actor, kind, target, word, note);
            Assert.IsTrue(r.Accepted, kind + " by " + actor + ": " + r);
        }

        static void Fail(LineTest m, LineFailure expected, int actor, PrankKind kind, int target = -1, int word = -1, string note = "", int guard = -1)
        {
            var r = Prank(m, actor, kind, target, word, note, guard);
            Assert.IsFalse(r.Accepted, kind + " by " + actor + " should fail");
            Assert.AreEqual(expected, r.Failure, kind + " by " + actor);
        }

        static LinePranksView P(LineTest m, int viewer = 0) { return m.View(viewer).Pranks; }
        static LinePrankOptionView Opt(LineTest m, int viewer, PrankKind k) { return P(m, viewer).Options.Single(o => o.Kind == k); }

        /// <summary>Meeting 1's dossier after clean tasks: everyone has 6 + 2 = 8 prim.</summary>
        static LineTest Meeting1(int players = 4, Action<LineConfig> configure = null, Action<LineSetup> prepare = null, int seed = 731)
        {
            var m = new LineTest(players, seed, configure, prepare);
            m.ToMeeting1();
            if (configure == null) Assert.AreEqual(8, m.Prim(0));
            return m;
        }

        /// <summary>Crisis verdict with candidates 1, 2, 3 (0 is not a candidate), as in LineFinalTests.</summary>
        static LineTest CrisisVerdict()
        {
            var m = new LineTest(4);
            var meeting2 = new[] { 1, 2, 3, 1 };
            var crisis = new[] { 1, 0, 1, 1 };
            m.ToCrisis(id => LineChoice.Skip, id => meeting2[id]);
            m.Next();
            Assert.AreEqual(LinePhase.CrisisVote, m.Engine.Phase);
            foreach (int id in m.Ids) m.Ok(id, LineVerb.Vote, crisis[id]);
            m.Next();
            Assert.AreEqual(LinePhase.CrisisVerdict, m.Engine.Phase);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, m.View().Final.Candidates);
            return m;
        }

        // ------------------------------------------------------------------ prim

        [Test]
        public void Costs_BurnPrim_NotPaidToTheTarget()
        {
            var m = Meeting1();
            var c = m.Config;
            Assert.AreEqual(new[] { 2, 1, 1, 2, 0, 0, 1, 0 }, Enumerable.Range(1, 8).Select(k => c.PrankCost((PrankKind)k)).ToArray());
            int total = m.Ids.Sum(id => m.Prim(id));
            Ok(m, 0, PrankKind.Coffee, 1);
            Assert.AreEqual(6, m.Prim(0));
            Assert.AreEqual(8, m.Prim(1), "the target gets nothing");
            Ok(m, 2, PrankKind.Plane, 3, note: "Toplantıdan sonra konuşalım.");
            Ok(m, 3, PrankKind.PostIt, 0, LinePranks.WordLiar);
            Ok(m, 1, PrankKind.Slurp);
            Assert.AreEqual(7, m.Prim(2));
            Assert.AreEqual(7, m.Prim(3));
            Assert.AreEqual(8, m.Prim(1), "slurping is free");
            Assert.AreEqual(total - 4, m.Ids.Sum(id => m.Prim(id)), "burned prim leaves the game");
            var log = P(m).Log;
            Assert.AreEqual(4, log.Count);
            Assert.AreEqual(new[] { 2, 1, 1, 0 }, log.Select(e => e.Cost).ToArray());
            Assert.AreEqual(PrankKind.Coffee, log[0].Kind);
            Assert.AreEqual(0, log[0].Actor);
            Assert.AreEqual(1, log[0].Target);
            Assert.AreEqual(1, P(m).Stains[1]);
            Assert.AreEqual(LinePranks.WordLiar, P(m).PostIts[0]);
            var e = m.Events.First(x => x.Type == LineEventType.Prank);
            Assert.AreEqual(0, e.Actor); Assert.AreEqual(1, e.Target); Assert.AreEqual((int)PrankKind.Coffee, e.A); Assert.AreEqual(0, e.C);
            m.Finish();
            var burn = m.Engine.Result.PrimLog.Where(x => x.Reason == PrimReason.Prank).ToList();
            Assert.AreEqual(3, burn.Count);
            Assert.AreEqual(-2, burn[0].Delta);
            Assert.AreEqual(1, burn[0].Other, "logged with the target for the reveal");
        }

        [Test]
        public void PrimNeverGoesNegative()
        {
            var m = Meeting1(configure: c => c.StartPrim = 1);
            Assert.AreEqual(3, m.Prim(0));
            Ok(m, 0, PrankKind.Coffee, 1);
            Assert.AreEqual(1, m.Prim(0));
            Assert.AreEqual(LineFailure.InsufficientFunds, Opt(m, 0, PrankKind.PhoneRing).Block);
            Fail(m, LineFailure.InsufficientFunds, 0, PrankKind.PhoneRing, 2);
            Ok(m, 0, PrankKind.PostIt, 2, LinePranks.WordRich);
            Assert.AreEqual(0, m.Prim(0));
            Fail(m, LineFailure.InsufficientFunds, 0, PrankKind.Plane, 2, note: "x");
            Fail(m, LineFailure.InsufficientFunds, 0, PrankKind.Flatter);
            Ok(m, 0, PrankKind.Slurp);
            Assert.AreEqual(0, m.Prim(0));
        }

        // ------------------------------------------------------------------ limits, cooldowns, sessions

        [Test]
        public void SessionLimits_AndCooldowns()
        {
            var m = Meeting1();
            Ok(m, 0, PrankKind.PostIt, 1, LinePranks.WordThief);
            Assert.AreEqual(0, Opt(m, 0, PrankKind.PostIt).UsesLeft);
            Assert.AreEqual(LineFailure.AlreadyUsed, Opt(m, 0, PrankKind.PostIt).Block);
            Fail(m, LineFailure.AlreadyUsed, 0, PrankKind.PostIt, 2, LinePranks.WordThief);
            Assert.AreEqual(-1, Opt(m, 0, PrankKind.Slurp).UsesLeft, "slurping has no session limit");
            Ok(m, 1, PrankKind.Slurp);
            Assert.AreEqual(LineFailure.RateLimited, Opt(m, 1, PrankKind.Slurp).Block);
            Assert.AreEqual(m.Now + 10, Opt(m, 1, PrankKind.Slurp).ReadyAt, 1e-6);
            Fail(m, LineFailure.RateLimited, 1, PrankKind.Slurp);
            Ok(m, 1, PrankKind.StandUp, 2);
            m.Advance(9.9);
            Fail(m, LineFailure.RateLimited, 1, PrankKind.Slurp);
            m.Advance(0.1);
            Ok(m, 1, PrankKind.Slurp);
            Assert.AreEqual(LineFailure.None, Opt(m, 2, PrankKind.PostIt).Block, "limits are per player");
        }

        [Test]
        public void NewSession_ResetsPatiencePostItsAndUses_StainsStay()
        {
            var m = Meeting1();
            Ok(m, 0, PrankKind.Coffee, 1);
            Ok(m, 2, PrankKind.PostIt, 1, LinePranks.WordQuiet);
            var p = P(m);
            Assert.AreEqual(LinePranks.SessionMeeting1, p.Session);
            Assert.AreEqual(5, p.Patience);
            Assert.AreEqual(LinePranks.WordQuiet, p.PostIts[1]);
            // the office (task 3): no session, nothing to do
            m.PlayTask(2, id => LineChoice.Skip);
            p = P(m);
            Assert.AreEqual(LinePranks.SessionNone, p.Session);
            Assert.IsFalse(m.View().Features.PranksOpen);
            Fail(m, LineFailure.WrongPhase, 0, PrankKind.Slurp);
            Assert.IsTrue(p.Options.All(o => o.Block == LineFailure.WrongPhase));
            // meeting 2 (the shift came up short)
            m.Next();
            Assert.AreEqual(LinePhase.MeetingDossier, m.Engine.Phase);
            p = P(m);
            Assert.AreEqual(LinePranks.SessionMeeting2, p.Session);
            Assert.AreEqual(0, p.Patience);
            Assert.AreEqual(-1, p.PostIts[1], "post-its last one session");
            Assert.AreEqual(1, p.Stains[1], "coffee stains stay for the match");
            Assert.AreEqual(1, Opt(m, 0, PrankKind.Coffee).UsesLeft);
            Ok(m, 0, PrankKind.Coffee, 2);
            Assert.AreEqual(3, P(m).Log.Count);
            Assert.AreEqual(LinePranks.SessionMeeting1, P(m).Log[1].Session);
            Assert.AreEqual(LinePranks.SessionMeeting2, P(m).Log[2].Session);
            // the crisis vote is a session of its own
            m.Until(LinePhase.CrisisCalls);
            Assert.AreEqual(LinePranks.SessionNone, P(m).Session);
            m.Next();
            Assert.AreEqual(LinePranks.SessionCrisis, P(m).Session);
            Assert.AreEqual(0, P(m).Patience);
            Ok(m, 0, PrankKind.Coffee, 3);
            Assert.AreEqual(3, P(m).Stains.Sum());
            m.Until(LinePhase.FinalDiscussion);
            Assert.AreEqual(LinePranks.SessionFinal, P(m).Session);
            Assert.AreEqual(0, P(m).Patience);
        }

        // ------------------------------------------------------------------ the manager's patience

        [Test]
        public void PatienceLevels_CalmFrowningRed()
        {
            Assert.AreEqual(new[] { 0, 0, 1, 1, 2, 2 }, Enumerable.Range(0, 6).Select(x => LinePranks.Level(x, 6)).ToArray());
            var m = Meeting1();
            Ok(m, 0, PrankKind.Slurp);
            Assert.AreEqual(1, P(m).Patience); Assert.AreEqual(0, P(m).Level);
            Ok(m, 1, PrankKind.Slurp);
            Assert.AreEqual(2, P(m).Patience); Assert.AreEqual(1, P(m).Level);
            var e = m.Events.Last(x => x.Type == LineEventType.Patience);
            Assert.AreEqual(2, e.A); Assert.AreEqual(1, e.B); Assert.AreEqual(0, e.C);
            Assert.AreEqual(1, e.Actor); Assert.AreEqual(1, e.Target);
            Ok(m, 2, PrankKind.Slurp);
            Ok(m, 3, PrankKind.Slurp);
            Assert.AreEqual(4, P(m).Patience); Assert.AreEqual(2, P(m).Level);
            Assert.AreEqual(4, P(m, 2).Patience, "the patience is public");
        }

        [Test]
        public void Overflow_IsATutanakForTheActor_AndThePatienceStartsAgain()
        {
            var m = Meeting1();
            Ok(m, 0, PrankKind.Coffee, 1);
            Ok(m, 1, PrankKind.Slurp);
            Ok(m, 2, PrankKind.Slurp);
            Assert.AreEqual(5, P(m).Patience);
            int blame = m.Blame(3);
            Ok(m, 3, PrankKind.PostIt, 0, LinePranks.WordLiar);
            Assert.AreEqual(blame + 1, m.Blame(3), "+1 suç, on the board at once");
            Assert.AreEqual(0, P(m).Patience);
            var last = P(m).Log.Last();
            Assert.IsTrue(last.Tutanak);
            Assert.AreEqual(3, last.Culprit);
            Assert.AreEqual(0, last.PatienceAfter);
            var t = m.Events.Single(x => x.Type == LineEventType.Tutanak);
            Assert.AreEqual(3, t.Target); Assert.AreEqual(3, t.Actor); Assert.AreEqual((int)PrankKind.PostIt, t.A); Assert.AreEqual(1, t.B);
            var b = m.Events.Last(x => x.Type == LineEventType.BlameAdded);
            Assert.AreEqual((int)BlameReason.Tutanak, b.B);
            var pe = m.Events.Last(x => x.Type == LineEventType.Patience);
            Assert.AreEqual(0, pe.A); Assert.AreEqual(0, pe.B); Assert.AreEqual(2, pe.C);
            // the prank still happened (the post-it is on, the prim is burned)
            Assert.AreEqual(LinePranks.WordLiar, P(m).PostIts[0]);
            Assert.AreEqual(7, m.Prim(3));
            m.Finish();
            Assert.AreEqual(1, m.Engine.Result.BlameLog.Count(x => x.Reason == BlameReason.Tutanak && x.Player == 3));
        }

        [Test]
        public void PhoneRing_ChargesTheTarget_AndHidesTheActorUntilResults()
        {
            var m = Meeting1();
            Ok(m, 1, PrankKind.Slurp);
            Ok(m, 2, PrankKind.Slurp);
            m.Advance(10);
            Ok(m, 1, PrankKind.Slurp);
            Assert.AreEqual(3, P(m).Patience);
            m.Events.Clear();
            Ok(m, 0, PrankKind.PhoneRing, 2);
            // 3 + 3 = 6: the tutanak goes to the target, not to the one who rang
            Assert.AreEqual(1, m.Blame(2));
            Assert.AreEqual(0, m.Blame(0));
            var mine = P(m, 0).Log.Last();
            Assert.AreEqual(0, mine.Actor, "the actor knows");
            Assert.AreEqual(2, mine.Culprit);
            Assert.IsTrue(mine.Tutanak);
            foreach (int viewer in new[] { 1, 2, 3 }) Assert.AreEqual(-1, P(m, viewer).Log.Last().Actor, "hidden from " + viewer);
            foreach (int viewer in new[] { 1, 2, 3 })
            {
                var ev = m.Events.Single(x => x.Type == LineEventType.Prank && x.IsVisibleTo(viewer));
                Assert.AreEqual(-1, ev.Actor);
                Assert.AreEqual(2, ev.Target);
            }
            Assert.AreEqual(0, m.Events.Single(x => x.Type == LineEventType.Prank && x.IsVisibleTo(0)).Actor);
            Assert.IsTrue(m.Events.Where(x => x.Type == LineEventType.Tutanak || x.Type == LineEventType.Patience).All(x => x.Actor == -1));
            Assert.AreEqual(2, m.Events.Single(x => x.Type == LineEventType.Tutanak).Target);
            m.Finish();
            foreach (int viewer in m.Ids) Assert.AreEqual(0, m.View(viewer).Pranks.Log.Single(e => e.Kind == PrankKind.PhoneRing).Actor, "revealed at Results");
        }

        [Test]
        public void Defence_DoublesThePatienceCost_ButNotFlattering()
        {
            var m = CrisisVerdict();
            m.Until(LinePhase.FinalDefense);
            Assert.AreEqual(2, P(m).Multiplier);
            Assert.AreEqual(2, Opt(m, 0, PrankKind.Slurp).Patience);
            Assert.AreEqual(6, Opt(m, 0, PrankKind.Coffee).Patience);
            Assert.AreEqual(-2, Opt(m, 0, PrankKind.Flatter).Patience);
            Ok(m, 0, PrankKind.Slurp);
            Assert.AreEqual(2, P(m).Patience);
            Ok(m, 1, PrankKind.StandUp, 0);
            Assert.AreEqual(4, P(m).Patience);
            Ok(m, 0, PrankKind.Flatter);
            Assert.AreEqual(2, P(m).Patience, "flattering relieves 2 also in the defence");
            m.Until(LinePhase.FinalVote);
            Assert.AreEqual(1, P(m).Multiplier);
            Assert.AreEqual(1, Opt(m, 0, PrankKind.Slurp).Patience);
        }

        [Test]
        public void Flattering_NeverGoesBelowZero()
        {
            var m = Meeting1();
            Ok(m, 0, PrankKind.Slurp);
            Ok(m, 1, PrankKind.Flatter);
            Assert.AreEqual(0, P(m).Patience);
            var e = P(m).Log.Last();
            Assert.AreEqual(-1, e.Patience, "only what was there");
            Assert.AreEqual(-1, e.Culprit);
            Assert.AreEqual(7, m.Prim(1));
            Fail(m, LineFailure.AlreadyUsed, 1, PrankKind.Flatter);
            Ok(m, 2, PrankKind.Flatter);
            Assert.AreEqual(0, P(m).Patience);
        }

        [Test]
        public void PatienceGuard_RejectsWhenThePatienceChanged()
        {
            var m = Meeting1();
            Ok(m, 0, PrankKind.Slurp);
            Fail(m, LineFailure.PatienceChanged, 1, PrankKind.Slurp, guard: 0);
            Assert.AreEqual(1, P(m).Patience, "nothing happened");
            var r = Prank(m, 1, PrankKind.Slurp, guard: 1);
            Assert.IsTrue(r.Accepted, r.ToString());
            Assert.AreEqual(2, P(m).Patience);
        }

        // ------------------------------------------------------------------ who may prank

        [Test]
        public void SealedCandidates_OnlyDoFreePranks()
        {
            var m = CrisisVerdict();
            Assert.IsTrue(m.Player(1).Candidate);
            int prim = m.Prim(1);
            Assert.AreEqual(LineFailure.DeskSealed, Opt(m, 1, PrankKind.Coffee).Block);
            Fail(m, LineFailure.DeskSealed, 1, PrankKind.Coffee, 0);
            Fail(m, LineFailure.DeskSealed, 1, PrankKind.PhoneRing, 0);
            Fail(m, LineFailure.DeskSealed, 1, PrankKind.Flatter);
            Fail(m, LineFailure.DeskSealed, 1, PrankKind.Plane, 0, note: "x");
            Ok(m, 1, PrankKind.Slurp);
            Ok(m, 1, PrankKind.StandUp, 0);
            Assert.AreEqual(prim, m.Prim(1), "a sealed desk never spends");
            Ok(m, 0, PrankKind.Coffee, 1);
        }

        [Test]
        public void FiredPlayers_WaitForTheResults_ThenCake()
        {
            var m = CrisisVerdict();
            m.Until(LinePhase.FinalVote);
            foreach (int id in m.Ids) if (id != 1) m.Ok(id, LineVerb.Vote, 1);
            m.Ok(1, LineVerb.Vote, 2);
            m.Next();
            Assert.AreEqual(LinePhase.FinalVerdict, m.Engine.Phase);
            Assert.IsTrue(m.Player(1).Fired);
            Assert.AreEqual(LineFailure.Fired, Opt(m, 1, PrankKind.Slurp).Block);
            Fail(m, LineFailure.Fired, 1, PrankKind.Slurp);
            Fail(m, LineFailure.Fired, 1, PrankKind.StandUp);
            Ok(m, 0, PrankKind.Coffee, 1);
            Assert.AreEqual(1, P(m).Stains[1], "everyone can be a target");
            m.Until(LinePhase.Results);
            Ok(m, 1, PrankKind.Cake, 0);
            Assert.AreEqual(1, P(m).Cakes[0]);
        }

        [Test]
        public void Cake_OnlyInTheResults_FiveEachWithACooldown()
        {
            var m = Meeting1();
            Fail(m, LineFailure.WrongPhase, 0, PrankKind.Cake, 1);
            Assert.AreEqual(LineFailure.WrongPhase, Opt(m, 0, PrankKind.Cake).Block);
            m.Until(LinePhase.Results);
            var p = P(m);
            Assert.AreEqual(LinePranks.SessionResults, p.Session);
            Assert.IsTrue(p.Options.Where(o => o.Kind != PrankKind.Cake).All(o => o.Block == LineFailure.WrongPhase));
            Fail(m, LineFailure.WrongPhase, 0, PrankKind.Coffee, 1);
            Fail(m, LineFailure.WrongPhase, 0, PrankKind.Slurp);
            int blame = m.Blame(0);
            for (int i = 0; i < 5; i++)
            {
                Ok(m, 0, PrankKind.Cake, 1 + i % 3);
                Fail(m, i < 4 ? LineFailure.RateLimited : LineFailure.LimitReached, 0, PrankKind.Cake, 2);
                m.Advance(3);
            }
            Fail(m, LineFailure.LimitReached, 0, PrankKind.Cake, 2);
            Assert.AreEqual(0, Opt(m, 0, PrankKind.Cake).UsesLeft);
            Assert.AreEqual(5, P(m).Cakes.Sum());
            Assert.AreEqual(0, P(m).Patience, "the manager does not mind the cake");
            Assert.AreEqual(blame, m.Blame(0));
            Assert.IsFalse(m.Events.Any(x => x.Type == LineEventType.Patience));
        }

        [Test]
        public void Targets_AndParameters_AreValidated()
        {
            var m = new LineTest();
            m.PlayTask(0, LineTest.Share);
            Fail(m, LineFailure.WrongPhase, 0, PrankKind.Slurp);
            m.Until(LinePhase.MeetingDossier, 0);
            Fail(m, LineFailure.Self, 0, PrankKind.Coffee, 0);
            Fail(m, LineFailure.InvalidTarget, 0, PrankKind.Coffee, 7);
            Fail(m, LineFailure.InvalidTarget, 0, PrankKind.Coffee, -1);
            Fail(m, LineFailure.InvalidTarget, 0, PrankKind.Slurp, 1);
            Fail(m, LineFailure.InvalidTarget, 0, PrankKind.Flatter, 1);
            Fail(m, LineFailure.Self, 0, PrankKind.StandUp, 0);
            Fail(m, LineFailure.BadParam, 0, PrankKind.None);
            Assert.IsFalse(m.DoFull(0, LineVerb.Prank, 1, 9, -1, -1).Accepted);
            Fail(m, LineFailure.BadParam, 0, PrankKind.PostIt, 1, 5);
            Fail(m, LineFailure.BadParam, 0, PrankKind.PostIt, 1, -1);
            Fail(m, LineFailure.EmptyText, 0, PrankKind.Plane, 1, note: "  ");
            Fail(m, LineFailure.TooLong, 0, PrankKind.Plane, 1, note: new string('a', 61));
            Ok(m, 0, PrankKind.Plane, 1, note: new string('a', 60));
            Ok(m, 0, PrankKind.StandUp);
            Assert.AreEqual(-1, P(m).Log.Last().Target, "standing up may point at nobody");
            Assert.AreEqual(2, P(m).Log.Count);
            Assert.IsTrue(m.View().Features.PranksEnabled);
            Assert.IsTrue(m.View().Features.PranksOpen);
        }

        [Test]
        public void PranksDisabled_RejectEverything()
        {
            foreach (var viaConfig in new[] { false, true })
            {
                var m = viaConfig ? Meeting1(configure: c => c.PranksEnabled = false) : Meeting1(prepare: s => s.PranksEnabled = false);
                Fail(m, LineFailure.NotAvailable, 0, PrankKind.Slurp);
                Fail(m, LineFailure.NotAvailable, 0, PrankKind.Coffee, 1);
                Assert.IsNull(m.View().Pranks);
                Assert.IsFalse(m.View().Features.PranksEnabled);
                Assert.IsFalse(m.View().Features.PranksOpen);
                m.Until(LinePhase.Results);
                Fail(m, LineFailure.NotAvailable, 0, PrankKind.Cake, 1);
            }
        }

        // ------------------------------------------------------------------ privacy

        [Test]
        public void PlaneNote_OnlyForTheTargetAndTheSender_NeverRevealed()
        {
            var m = Meeting1();
            const string note = "Kasayı biliyorum <3";
            Ok(m, 0, PrankKind.Plane, 1, note: note);
            string clean = "Kasayı biliyorum ‹3";
            Assert.AreEqual(clean, P(m, 0).Log[0].Note);
            Assert.AreEqual(clean, P(m, 1).Log[0].Note);
            Assert.AreEqual("", P(m, 2).Log[0].Note);
            Assert.AreEqual("", P(m, 3).Log[0].Note);
            Assert.AreEqual(0, P(m, 2).Log[0].Actor, "everyone sees the plane and who threw it");
            Assert.AreEqual(1, P(m, 2).Log[0].Target);
            m.Finish();
            m.Now = m.Engine.PhaseEndTime;
            var r = m.Engine.Result;
            Assert.IsFalse(LineCodec.EncodeView(m.Engine.BuildView(2, m.Now)).Length == 0);
            Assert.AreEqual("", m.Engine.BuildView(2, m.Now).Pranks.Log[0].Note, "not even at the end");
            Assert.AreEqual(clean, m.Engine.BuildView(1, m.Now).Pranks.Log[0].Note);
            Assert.IsFalse(new LineAction(1, 1, LineVerb.Prank, 1, 2, -1, -1, note).ToString().Contains("Kasa"), "never logged");
            Assert.IsNotNull(r);
        }

        [Test]
        public void ViewsWithPranks_RoundTripThroughTheCodec()
        {
            var m = Meeting1();
            Ok(m, 0, PrankKind.Coffee, 1);
            Ok(m, 1, PrankKind.PhoneRing, 2);
            Ok(m, 2, PrankKind.Plane, 0, note: "Merhaba");
            foreach (int viewer in m.Ids)
            {
                var v = m.View(viewer);
                var back = LineCodec.DecodeView(LineCodec.EncodeView(v));
                CollectionAssert.AreEqual(LineCodec.EncodeView(v), LineCodec.EncodeView(back));
                Assert.AreEqual(v.Pranks.Log.Count, back.Pranks.Log.Count);
                Assert.AreEqual(v.Pranks.Log[2].Note, back.Pranks.Log[2].Note);
                Assert.AreEqual(8, back.Pranks.Options.Count);
            }
            var setup = LineTest.MakeSetup();
            setup.PranksEnabled = false;
            var lobby = new LineLobbyView { PranksEnabled = false };
            Assert.IsFalse(LineCodec.DecodeLobby(LineCodec.EncodeLobby(lobby)).PranksEnabled);
        }

        [Test]
        public void SameActions_SameOutcome()
        {
            Func<ulong> run = () =>
            {
                var m = Meeting1();
                Ok(m, 0, PrankKind.Coffee, 1);
                Ok(m, 1, PrankKind.PhoneRing, 0);
                m.Advance(3);
                Ok(m, 2, PrankKind.Slurp);
                m.Finish();
                return m.Engine.Result.ResultHash;
            };
            Assert.AreEqual(run(), run());
        }

        // ================================================================== bots (the talk layer decides)

        static readonly LineBotPolicy[] Rotation = { LineBotPolicy.Honest, LineBotPolicy.Greedy, LineBotPolicy.Mixed };

        sealed class BotRun
        {
            public LineView Final;
            public LineBotPolicy[] Policy;
            public readonly List<string> Rejected = new List<string>();
            public readonly List<BotLine> Shouts = new List<BotLine>();
            public readonly List<string> Log = new List<string>();
            public LineWeekResult Result;
        }

        static BotRun Bots(int players, int seed, int shift)
        {
            var setup = new LineSetup { WeekId = 1, MatchId = "pranks-" + players + "-" + seed, Seed = seed };
            for (int i = 0; i < players; i++) setup.Players.Add(new LinePlayerInfo { Id = i, Name = "Bot " + i, ColorIndex = i, IsBot = true });
            var config = LineConfig.CreateDefault();
            config.Durations.Scale(0.3f);
            var engine = LineEngineFactory.Create(setup, config);
            var run = new BotRun { Policy = new LineBotPolicy[players] };
            var bots = new LineBot[players];
            var talkers = new LineBotTalker[players];
            var requests = new uint[players];
            for (int i = 0; i < players; i++)
            {
                run.Policy[i] = Rotation[(i + shift) % 3];
                bots[i] = new LineBot(i, seed * 31 + i, run.Policy[i]) { ThinkSeconds = 4 };
                talkers[i] = new LineBotTalker(i, seed * 17 + i, run.Policy[i], bots[i]);
            }
            var o = new BotTalkOutput();
            double now = 0;
            void Apply(int seat, LineAction a)
            {
                var r = engine.Apply(seat, new LineAction(a.WeekId, ++requests[seat], a.Verb, a.Target, a.A, a.B, a.C, a.Text), now);
                if (!r.Accepted && a.Verb == LineVerb.Prank) run.Rejected.Add(seat + " " + a + " " + engine.Phase + ": " + r.Failure);
            }
            for (int step = 0; step < 20000; step++)
            {
                engine.Tick(now);
                engine.DrainEvents();
                if (engine.IsFinished) break;
                for (int round = 0; round < 8; round++)
                {
                    bool acted = false;
                    for (int i = 0; i < players; i++)
                    {
                        var a = bots[i].Next(engine.BuildView(i, now), config, now);
                        if (a != null) { Apply(i, a); acted = true; }
                    }
                    if (!acted) break;
                }
                for (int i = 0; i < players; i++)
                {
                    o.Clear();
                    talkers[i].Tick(engine.BuildView(i, now), config, now, engine.MessagesFor(i, talkers[i].MessagesSeen), o);
                    foreach (var a in o.Actions) Apply(i, a);
                    run.Shouts.AddRange(o.Shouts);
                    run.Log.AddRange(o.Log);
                }
                // the results run their full length (cakes), then the match closes
                now += 0.25;
            }
            Assert.IsTrue(engine.IsFinished, "match did not finish");
            run.Result = engine.Result;
            run.Final = engine.BuildView(0, now);
            return run;
        }

        static IEnumerable<BotRun> BotMatches(int perCount, int seedBase)
        {
            for (int n = 3; n <= 6; n++)
                for (int i = 0; i < perCount; i++)
                    yield return Bots(n, seedBase + n * 1000 + i, i % 3);
        }

        [Test]
        public void Bots_FollowThePrankPolicy()
        {
            int total = 0, honest = 0, greedy = 0, mixed = 0, cakes = 0, reactions = 0, tutanak = 0;
            var kinds = new Dictionary<PrankKind, int>();
            foreach (var run in BotMatches(6, 9100))
            {
                Assert.IsEmpty(run.Rejected, "bot pranks are always valid when the bots take turns");
                var log = run.Final.Pranks.Log;
                foreach (var e in log)
                {
                    total++;
                    var policy = run.Policy[e.Actor];
                    if (policy == LineBotPolicy.Honest) honest++;
                    else if (policy == LineBotPolicy.Greedy) greedy++;
                    else mixed++;
                    if (e.Kind == PrankKind.Cake) cakes++;
                    if (e.Tutanak) tutanak++;
                    kinds[e.Kind] = kinds.TryGetValue(e.Kind, out var kc) ? kc + 1 : 1;
                    if (e.Tutanak) Assert.AreNotEqual(e.Actor, e.Culprit, "a bot never brings itself a tutanak: " + e.Kind);
                    if (e.Tutanak) Assert.IsTrue(policy == LineBotPolicy.Greedy && e.Kind == PrankKind.PhoneRing, "only Greedy's phone ring overflows the patience");
                    if (policy == LineBotPolicy.Honest && e.Kind != PrankKind.Cake) Assert.AreEqual(-1, e.Target, "Honest never targets anyone");
                    if (policy == LineBotPolicy.Honest) Assert.IsTrue(e.Kind == PrankKind.Slurp || e.Kind == PrankKind.Flatter || e.Kind == PrankKind.Cake, e.Kind.ToString());
                    if (policy == LineBotPolicy.Mixed) Assert.IsTrue(e.Kind == PrankKind.PostIt || e.Kind == PrankKind.Plane || e.Kind == PrankKind.Cake, e.Kind.ToString());
                    if (policy == LineBotPolicy.Greedy) Assert.IsTrue(e.Kind == PrankKind.Coffee || e.Kind == PrankKind.PhoneRing || e.Kind == PrankKind.Cake, e.Kind.ToString());
                    Assert.IsFalse(e.Phase == LinePhase.MeetingVote || e.Phase == LinePhase.CrisisVote || e.Phase == LinePhase.FinalVote, "no pranks while voting");
                    // a sealed desk never spends
                    bool candidate = run.Result.Players.Single(p => p.Player == e.Actor).WasCandidate;
                    if (candidate && e.Phase >= LinePhase.CrisisVerdict) Assert.AreEqual(0, e.Cost, "a sealed candidate spent prim");
                    if (e.Kind == PrankKind.Plane && e.Note != "")
                    {
                        string key;
                        List<int> args;
                        Assert.IsTrue(LineBotLines.TryParse(e.Note, out key, out args), "a bot's note is a line token: " + e.Note);
                    }
                }
                foreach (var g in log.GroupBy(e => e.Actor * 10 + e.Session))
                    Assert.LessOrEqual(g.Count(), 2, "at most two pranks per bot per session");
                foreach (var p in run.Result.Players) Assert.GreaterOrEqual(p.Prim, 0);
                foreach (var s in run.Shouts)
                {
                    Assert.IsTrue(LineBotLines.Valid(s), s.ToString());
                    Assert.IsTrue(s.Key.StartsWith("bot.prank.", StringComparison.Ordinal));
                    reactions++;
                }
                Assert.IsTrue(run.Log.Any(x => x.StartsWith("bottalk pranks ", StringComparison.Ordinal)), "prank summary at Results");
            }
            TestContext.WriteLine("pranks " + total + " (honest " + honest + ", greedy " + greedy + ", mixed " + mixed + "), cakes " + cakes + ", tutanak " + tutanak
                                  + ", reactions " + reactions + "; by kind: " + string.Join(", ", kinds.OrderBy(k => k.Key).Select(k => k.Key + " " + k.Value)));
            Assert.Greater(total, 60, "bots prank");
            Assert.Greater(honest, 5);
            Assert.Greater(greedy, 5);
            Assert.Greater(mixed, 5);
            Assert.Greater(cakes, 3);
            Assert.Greater(reactions, 10, "bots react to pranks on them");
        }

        [Test]
        public void Bots_SameSeed_SamePranks()
        {
            var a = Bots(5, 4343, 0);
            var b = Bots(5, 4343, 0);
            Func<BotRun, string> sig = r => string.Join(";", r.Final.Pranks.Log.Select(e => e.Kind + "/" + e.Actor + ">" + e.Target + "@" + e.Time.ToString("0.00")));
            Assert.AreEqual(sig(a), sig(b));
            Assert.Greater(a.Final.Pranks.Log.Count, 0);
            Assert.AreEqual(a.Result.ResultHash, b.Result.ResultHash);
            Assert.AreEqual(string.Join("|", a.Shouts.Select(s => s.Speaker + s.Key)), string.Join("|", b.Shouts.Select(s => s.Speaker + s.Key)));
        }

        [Test]
        public void Bots_LogTheirPranksAtResults()
        {
            var run = Bots(4, 5151, 1);
            var lines = run.Log.Where(x => x.StartsWith("bottalk prank ", StringComparison.Ordinal)).ToList();
            Assert.AreEqual(run.Final.Pranks.Log.Count, lines.Count, "one line per prank (cakes as they land)");
            foreach (var l in lines)
            {
                StringAssert.Contains(" tutanak=", l);
                if (l.Contains(" sealed=yes")) StringAssert.Contains(" cost=0 ", l, "a sealed desk never spends");
                Assert.IsFalse(l.Contains("prim_after=-"), l);
            }
            Assert.AreEqual(4, run.Log.Count(x => x.StartsWith("bottalk pranks ", StringComparison.Ordinal)));
        }
    }
}
