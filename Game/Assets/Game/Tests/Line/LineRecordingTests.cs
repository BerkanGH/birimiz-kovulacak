using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests.Line
{
    [TestFixture]
    public sealed class LineRecordingTests
    {
        [Test]
        public void RecordingSelectionFavorsDurationAcrossFixedSeeds()
        {
            int longPicked = 0;
            for (int seed = 0; seed < 100; seed++)
            {
                var m = new LineTest(LineTest.MakeSetup(seed: seed)); m.Until(LinePhase.RoundPlay);
                m.Ok(0, LineVerb.Call, 1); m.Ok(1, LineVerb.Answer); int longCall = m.Engine.ConnectedCallId(0);
                m.Advance(60); m.Ok(0, LineVerb.HangUp);
                m.Ok(0, LineVerb.Call, 1); m.Ok(1, LineVerb.Answer); m.Advance(6); m.Ok(0, LineVerb.HangUp);
                m.Until(LinePhase.MeetingDossier);
                if (m.Engine.RecordingPicks[0].CandidateCallIds[0] == longCall) longPicked++;
            }
            // Fixed seeds make this reproducible. A wide bound detects uniform/shortest-first selection
            // while leaving substantial room around the specified 60/(60+6) expected share.
            Assert.Greater(longPicked, 70); Assert.Less(longPicked, 100);
        }

        [Test]
        public void RecordingNoSpeechReportIsPrivateAndIdempotent()
        {
            var m = new LineTest(); m.Ok(0, LineVerb.Call, 1); m.Ok(1, LineVerb.Answer);
            m.Advance(8); m.Ok(0, LineVerb.HangUp); m.Until(LinePhase.MeetingDossier);
            int candidate = m.Engine.RecordingPicks[0].CandidateCallIds[0];
            foreach (var p in m.Setup.Players)
            {
                var pick = m.View(p.Id).Meeting.Recordings[0]; Assert.IsEmpty(pick.CandidateCallIds);
                Assert.AreEqual(-1, pick.ChosenCallId); Assert.AreEqual(-1, pick.Caller); Assert.AreEqual(-1, pick.Callee);
            }
            m.Engine.ReportRecording(0, candidate + 1000, m.Now); Assert.IsFalse(m.Engine.RecordingPicks[0].Played);
            m.Engine.DrainEvents(); m.Advance(4); m.Engine.ReportRecording(0, -1, m.Now);
            Assert.IsTrue(m.Engine.RecordingPicks[0].Played); Assert.AreEqual(-1, m.Engine.RecordingPicks[0].ChosenCallId);
            Assert.IsFalse(m.Engine.DrainEvents().Any(e => e.Type == LineEventType.RecordingStarted));
            long version = m.Engine.StateVersion; m.Engine.ReportRecording(0, candidate, m.Now);
            Assert.AreEqual(version, m.Engine.StateVersion); Assert.AreEqual(-1, m.View().Meeting.Recordings[0].Caller);
        }

        [Test]
        public void RecordingEligibilityErasureAndReport()
        {
            var m = LineTest.WithTrick(TrickType.KayitSil);
            m.Ok(0, LineVerb.Call, 1); m.Ok(1, LineVerb.Answer); int erased = m.Engine.ConnectedCallId(0);
            m.Advance(9); m.Ok(0, LineVerb.HangUp); m.Ok(0, LineVerb.UseTrick, 1);
            m.Ok(0, LineVerb.Call, 1); m.Ok(1, LineVerb.Answer); int laterErased = m.Engine.ConnectedCallId(0);
            m.Advance(9); m.Ok(1, LineVerb.HangUp);
            m.Ok(2, LineVerb.Call, 3); m.Ok(3, LineVerb.Answer); int shortCall = m.Engine.ConnectedCallId(2);
            m.Advance(5); m.Ok(2, LineVerb.HangUp);
            m.Ok(2, LineVerb.Call, 3); m.Ok(3, LineVerb.Answer); int eligible = m.Engine.ConnectedCallId(2); double start = m.Now;
            m.Advance(15); m.Ok(3, LineVerb.HangUp); m.Until(LinePhase.MeetingDossier);
            var picks = m.Engine.RecordingPicks; Assert.AreEqual(1, picks.Count);
            CollectionAssert.AreEqual(new[] { eligible }, picks[0].CandidateCallIds);
            foreach (int forbidden in new[] { erased, laterErased, shortCall }) CollectionAssert.DoesNotContain(picks[0].CandidateCallIds, forbidden);
            Assert.AreEqual(m.Now + 4, picks[0].PlayAt); Assert.AreEqual(m.Config.RecordingSnippetSeconds, picks[0].Seconds);
            Assert.AreEqual(m.Config.Durations.Dossier + m.Config.Durations.DossierPerRecording, m.Engine.PhaseEndTime - m.Now);
            Assert.AreEqual(-1, m.View().Meeting.Recordings[0].Caller); Assert.AreEqual(-1, m.View().Meeting.Recordings[0].Callee);
            m.Advance(4); m.Engine.ReportRecording(0, eligible, m.Now);
            var played = m.View().Meeting.Recordings[0]; Assert.IsTrue(played.Played); Assert.AreEqual(eligible, played.ChosenCallId);
            Assert.AreEqual(2, played.Caller); Assert.AreEqual(3, played.Callee); Assert.AreEqual(start, played.CallStart);
            Assert.IsTrue(m.Engine.DrainEvents().Any(e => e.Type == LineEventType.RecordingStarted));
            m.Next(); Assert.IsEmpty(m.Engine.RecordingPicks);
        }

        [Test]
        public void RecordingSelectionIsDeterministicDistinctAndBounded()
        {
            var a = LineTest.WithTrick(TrickType.KahveDok, c => c.Profiles.ForEach(p => p.Recordings = 2));
            var b = LineTest.WithTrick(TrickType.KahveDok, c => c.Profiles.ForEach(p => p.Recordings = 2));
            foreach (var m in new[] { a, b })
            {
                for (int i = 0; i < 7; i++)
                {
                    m.Ok(0, LineVerb.Call, 1); m.Ok(1, LineVerb.Answer); m.Advance(6 + i); m.Ok(0, LineVerb.HangUp);
                }
                m.Until(LinePhase.MeetingDossier);
            }
            Assert.AreEqual(2, a.Engine.RecordingPicks.Count);
            Assert.AreNotEqual(a.Engine.RecordingPicks[0].CandidateCallIds[0], a.Engine.RecordingPicks[1].CandidateCallIds[0]);
            for (int i = 0; i < 2; i++)
            {
                var pick = a.Engine.RecordingPicks[i]; Assert.LessOrEqual(pick.CandidateCallIds.Count, a.Config.RecordingCandidates);
                Assert.AreEqual(pick.CandidateCallIds.Count, pick.CandidateCallIds.Distinct().Count());
                CollectionAssert.AreEqual(pick.CandidateCallIds, b.Engine.RecordingPicks[i].CandidateCallIds);
                Assert.AreEqual(a.Now + 4 + i * a.Config.Durations.DossierPerRecording, pick.PlayAt);
            }
        }

        [Test]
        public void RecordingToggleRemovesPicksAndTimeExtension()
        {
            var m = new LineTest(configure: c => c.RecordingsEnabled = false);
            m.Ok(0, LineVerb.Call, 1); m.Ok(1, LineVerb.Answer); m.Advance(10); m.Ok(0, LineVerb.HangUp);
            m.Until(LinePhase.MeetingDossier); Assert.IsEmpty(m.Engine.RecordingPicks);
            Assert.AreEqual(m.Config.Durations.Dossier, m.Engine.PhaseEndTime - m.Now);
        }
    }
}
