using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BirimizKovulacak.Core.Tests.Line;

namespace BirimizKovulacak.Core.Tests
{
    /// <summary>
    /// Non-interference: two matches that differ only in one player's secret (choice, transfer amount, recorder mark,
    /// ballot) must produce byte-identical views for every player who is not entitled to that secret.
    /// </summary>
    [TestFixture]
    public sealed class LinePrivacyTests
    {
        static byte[] Snapshot(LineTest m, int viewer)
        {
            var v = m.View(viewer);
            v.StateVersion = 0; // the version counter moves on any accepted action; it carries no content
            return LineCodec.EncodeView(v);
        }

        static void AssertSame(LineTest a, LineTest b, IEnumerable<int> viewers, string what)
        {
            foreach (int p in viewers)
                CollectionAssert.AreEqual(Snapshot(a, p), Snapshot(b, p), what + ": seat " + p + " must not see the difference");
        }


        [Test]
        public void TaskChoicesAreSecretUntilTheRevealAndThenOnlyToTheGroup()
        {
            LineTest Run(bool take)
            {
                var m = new LineTest(4, 5150);
                m.Next();
                m.Ok(1, LineVerb.SubmitTask, a: take ? LineChoice.Take : LineChoice.Share);
                return m;
            }
            var a = Run(true); var b = Run(false);
            AssertSame(a, b, new[] { 0, 2, 3 }, "task choice during play");
            a.Until(LinePhase.TaskReveal); b.Until(LinePhase.TaskReveal);
            var group = a.Group(1);
            foreach (int p in new[] { 0, 2, 3 }.Where(p => !group.Contains(p)))
            {
                var va = a.View(p).Task; var vb = b.View(p).Task;
                CollectionAssert.AreEqual(va.Choices.Select(c => c.Player), vb.Choices.Select(c => c.Player));
                Assert.IsFalse(va.Choices.Any(c => c.Player == 1), "outside the group the choice stays private");
                Assert.AreNotEqual(va.Takers, vb.Takers, "only the public taker count moves");
            }
            Assert.AreNotEqual(a.Prim(1), b.Prim(1));
        }

        [Test]
        public void TransferAmountsAreVisibleOnlyToSenderAndReceiver()
        {
            LineTest Run(int amount)
            {
                var m = new LineTest(4, 5150);
                m.Ok(1, LineVerb.Transfer, 2, amount);
                return m;
            }
            var a = Run(3); var b = Run(5);
            AssertSame(a, b, new[] { 0, 3 }, "transfer");
            Assert.AreNotEqual(a.Prim(2), b.Prim(2));
            Assert.IsFalse(a.Events.Any(e => e.Type == LineEventType.TransferReceived && (e.IsVisibleTo(0) || e.IsVisibleTo(3))));
        }

        [Test]
        public void RecorderMarksAreVisibleOnlyToTheRecorder()
        {
            LineTest Run(bool record)
            {
                var m = new LineTest(4, 5150);
                m.Ok(1, LineVerb.Call, 2);
                m.Ok(2, LineVerb.Answer);
                if (record) m.Ok(1, LineVerb.Record);
                m.Advance(3);
                return m;
            }
            var a = Run(true); var b = Run(false);
            AssertSame(a, b, new[] { 0, 2, 3 }, "recorder mark");
        }

        [Test]
        public void MeetingCrisisAndFinalBallotsAreSecretUntilTheyAreCounted()
        {
            LineTest Run(int target)
            {
                var m = new LineTest(4, 5150);
                m.ToMeeting1();
                m.PlayTask(2, id => 0);
                m.Until(LinePhase.MeetingVote);
                m.Ok(1, LineVerb.Vote, target, 0);
                return m;
            }
            var a = Run(2); var b = Run(3);
            AssertSame(a, b, new[] { 0, 2, 3 }, "meeting ballot");
            a.Next(); b.Next();
            Assert.AreNotEqual(a.Blame(2), b.Blame(2), "verdict reveals the totals");

            LineTest Crisis(int target)
            {
                var m = new LineTest(4, 5150);
                m.ToCrisis();
                m.Next();
                m.Ok(1, LineVerb.Vote, target);
                return m;
            }
            AssertSame(Crisis(2), Crisis(3), new[] { 0, 2, 3 }, "crisis ballot");

            LineTest Final(bool vote)
            {
                var m = new LineTest(4, 5150);
                m.ToCrisis();
                m.Until(LinePhase.FinalVote);
                int target = m.View().Final.Options.First(id => id != 1);
                if (vote) m.Ok(1, LineVerb.Vote, target);
                m.Ok(1, LineVerb.Vote, -1);
                return m;
            }
            AssertSame(Final(true), Final(false), new[] { 0, 2, 3 }, "withdrawn final ballot");
        }

        [Test]
        public void PrimIsPrivateUntilResultsThenFullyDisclosed()
        {
            var m = new LineTest(4, 5150);
            for (int i = 0; i < 60 && m.Engine.Phase != LinePhase.Results; i++)
            {
                foreach (int p in m.Ids)
                {
                    var v = m.View(p);
                    Assert.IsNull(v.Results, v.Phase.ToString());
                    Assert.AreEqual(p, v.Me);
                    if (v.Task != null)
                        Assert.IsTrue(v.Task.Choices.All(c => c.Player == p ? c.Payout >= 0 : c.Payout == -1));
                }
                m.Next();
            }
            Assert.AreEqual(LinePhase.Results, m.Engine.Phase);
            var r = m.View(0).Results;
            Assert.AreEqual(4, r.Players.Count);
            foreach (int p in m.Ids) Assert.AreEqual(m.Prim(p), r.Players.Single(x => x.Player == p).Prim);
        }

        [Test]
        public void PrivateEventsReachOnlyTheirAudience()
        {
            var m = new LineTest(4, 5150);
            m.Ok(0, LineVerb.Call, 1);
            m.Ok(0, LineVerb.Transfer, 2, 1);
            foreach (var e in m.Events)
            {
                if (e.Type == LineEventType.CallIncoming) Assert.AreEqual(1, e.AudiencePlayer);
                if (e.Type == LineEventType.TransferReceived) Assert.AreEqual(2, e.AudiencePlayer);
                if (e.Type == LineEventType.MessageReceived) Assert.AreEqual(LineAudience.Player, e.Audience);
            }
            Assert.IsFalse(m.Events.Any(e => e.Audience == LineAudience.Host));
        }
    }
}
