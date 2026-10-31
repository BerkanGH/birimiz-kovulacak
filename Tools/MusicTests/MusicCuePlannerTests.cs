using BirimizKovulacak.Core;
using NUnit.Framework;

namespace BirimizKovulacak.MusicTests
{
    public class MusicCuePlannerTests
    {
        static MusicState S(LinePhase ph, double remaining = 100, double length = 120) =>
            new MusicState { HasView = true, Phase = ph, WeekId = 1, PhaseRemaining = remaining, PhaseLength = length };

        [Test] public void MenuAndNoViewPlayMenuBed()
        {
            Assert.AreEqual(MusicCues.MenuLobby, MusicCuePlanner.Plan(new MusicState()).Cue);
            Assert.AreEqual(MusicCues.MenuLobby, MusicCuePlanner.Plan(S(LinePhase.None)).Cue);
        }

        [TestCase(LinePhase.Morning)] [TestCase(LinePhase.RoundIntro)] [TestCase(LinePhase.RoundReveal)] [TestCase(LinePhase.Break)]
        public void OfficePhases(LinePhase ph) => Assert.AreEqual(MusicCues.OfficeWork, MusicCuePlanner.Plan(S(ph)).Cue);

        [Test] public void DeadlineThreshold()
        {
            Assert.AreEqual(MusicCues.OfficeWork, MusicCuePlanner.Plan(S(LinePhase.RoundPlay, 30.5, 120)).Cue);
            Assert.AreEqual(MusicCues.Deadline, MusicCuePlanner.Plan(S(LinePhase.RoundPlay, 30.0, 120)).Cue);
            Assert.AreEqual(MusicCues.Deadline, MusicCuePlanner.Plan(S(LinePhase.RoundPlay, 1, 120)).Cue);
            // short task: 25% of 60 s = 15 s
            Assert.AreEqual(MusicCues.OfficeWork, MusicCuePlanner.Plan(S(LinePhase.RoundPlay, 20, 60)).Cue);
            Assert.AreEqual(MusicCues.Deadline, MusicCuePlanner.Plan(S(LinePhase.RoundPlay, 15, 60)).Cue);
            // unknown length never triggers
            Assert.AreEqual(MusicCues.OfficeWork, MusicCuePlanner.Plan(S(LinePhase.RoundPlay, 5, 0)).Cue);
            // deadline cue only inside RoundPlay
            Assert.AreEqual(MusicCues.OfficeWork, MusicCuePlanner.Plan(S(LinePhase.RoundReveal, 5, 120)).Cue);
        }

        [Test] public void DossierIsOneShotStinger()
        {
            var p = MusicCuePlanner.Plan(S(LinePhase.MeetingDossier));
            Assert.AreEqual(MusicCues.MeetingIntro, p.Cue);
            Assert.IsFalse(p.Loop);
        }

        [TestCase(LinePhase.MeetingDiscussion)] [TestCase(LinePhase.MeetingVote)] [TestCase(LinePhase.MeetingVerdict)]
        public void MeetingBed(LinePhase ph)
        {
            var p = MusicCuePlanner.Plan(S(ph));
            Assert.AreEqual(MusicCues.MeetingDiscussion, p.Cue);
            Assert.IsTrue(p.Loop);
        }

        [TestCase(LinePhase.FinalDefense)] [TestCase(LinePhase.FinalVote)] [TestCase(LinePhase.FinalVerdict)]
        public void FinalBed(LinePhase ph) => Assert.AreEqual(MusicCues.FinalVote, MusicCuePlanner.Plan(S(ph)).Cue);

        [Test] public void ResultsSuccessAndCollapsePlayOnce()
        {
            var ok = S(LinePhase.WeekResults);
            var bad = S(LinePhase.WeekResults); bad.CompanyClosed = true;
            var a = MusicCuePlanner.Plan(ok); var b = MusicCuePlanner.Plan(bad);
            Assert.AreEqual(MusicCues.ResultsSuccess, a.Cue); Assert.IsFalse(a.Loop);
            Assert.AreEqual(MusicCues.ResultsCollapse, b.Cue); Assert.IsFalse(b.Loop);
        }

        [Test] public void AbandonedStops() => Assert.IsNull(MusicCuePlanner.Plan(S(LinePhase.Abandoned)).Cue);

        [Test] public void MenuReturnAndRematchResetToMenuBed()
        {
            Assert.AreEqual(MusicCues.ResultsSuccess, MusicCuePlanner.Plan(S(LinePhase.WeekResults)).Cue);
            var menu = new MusicState { HasView = false, Phase = LinePhase.WeekResults, RecordingActive = true }; // stale flags ignored without a view
            var p = MusicCuePlanner.Plan(menu);
            Assert.AreEqual(MusicCues.MenuLobby, p.Cue);
            Assert.AreEqual(1f, p.Duck, 1e-6);
            // rematch: the phase returns to Morning of the new week -> office bed again
            Assert.AreEqual(MusicCues.OfficeWork, MusicCuePlanner.Plan(S(LinePhase.Morning)).Cue);
        }

        [Test] public void DuckTargets()
        {
            var s = S(LinePhase.RoundPlay);
            Assert.AreEqual(1f, MusicCuePlanner.Plan(s).Duck, 1e-6);
            s.AnySpeaking = true; // speech outside meetings does not duck
            Assert.AreEqual(1f, MusicCuePlanner.Plan(s).Duck, 1e-6);
            s.PhoneConnected = true;
            Assert.AreEqual(MusicCuePlanner.DuckPhone, MusicCuePlanner.Plan(s).Duck, 1e-6);
            s.RecordingActive = true;
            Assert.AreEqual(MusicCuePlanner.DuckRecording, MusicCuePlanner.Plan(s).Duck, 1e-6);

            var m = S(LinePhase.MeetingDiscussion);
            Assert.AreEqual(1f, MusicCuePlanner.Plan(m).Duck, 1e-6);
            m.AnySpeaking = true;
            Assert.AreEqual(MusicCuePlanner.DuckMeetingSpeech, MusicCuePlanner.Plan(m).Duck, 1e-6);

            var f = S(LinePhase.FinalDefense);
            Assert.AreEqual(MusicCuePlanner.DuckFinalDefense, MusicCuePlanner.Plan(f).Duck, 1e-6);
            f.AnySpeaking = true;
            Assert.AreEqual(MusicCuePlanner.DuckFinalDefense, MusicCuePlanner.Plan(f).Duck, 1e-6);
        }
    }
}
