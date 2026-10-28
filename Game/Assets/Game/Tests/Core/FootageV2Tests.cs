using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class FootageV2Tests
    {
        private static TestMatch Match(Action<MatchConfig> configure = null, MatchSetup setup = null)
        {
            return new TestMatch(setup: setup, configure: c =>
            {
                c.Footage.WindowSeconds = 0;
                c.Informant.FootageCharges = 10;
                if (configure != null) configure(c);
            });
        }

        private static Vec3 CameraCenter(CameraDef camera) { return (camera.ZoneMin + camera.ZoneMax) * 0.5f; }
        private static EvidenceView Request(TestMatch m, int player, string camera, int second)
        {
            ActionResult result = m.Request(player, ActionVerb.RequestFootage, camera, second);
            Assert.IsTrue(result.Accepted, result.Failure.ToString());
            return m.View(player).Evidence.Last(e => e.Kind == EvidenceKind.CctvFootage);
        }

        [Test]
        public void FootageIsMeetingOnlyAndFeatureDisabledDoesNotSpendAllowance()
        {
            TestMatch m = Match(c => c.Footage.Enabled = false);
            string camera = m.Layout.Cameras[0].Id;
            Assert.AreEqual(FailureCode.WrongPhase, m.Request(3, ActionVerb.RequestFootage, camera, 0).Failure);
            m.Work(); Assert.AreEqual(FailureCode.WrongPhase, m.Request(3, ActionVerb.RequestFootage, camera, 0).Failure);
            m.Phase(MatchPhase.MeetingDiscussion);
            Assert.AreEqual(FailureCode.FeatureDisabled, m.Request(3, ActionVerb.RequestFootage, camera, 0).Failure);
            Assert.AreEqual(0, m.View(3).Me.FootageLeft);
            Assert.IsFalse(m.View().Evidence.Any(e => e.Kind == EvidenceKind.CctvFootage));
            m.Phase(MatchPhase.MeetingVote); Assert.AreEqual(FailureCode.WrongPhase, m.Request(3, ActionVerb.RequestFootage, camera, 0).Failure);
        }

        [Test]
        public void WeekFeatureFlagDisablesFootage()
        {
            MatchSetup setup = TestMatch.Setup(); setup.Mode = GameMode.Week; setup.ManagerProfileId = "legacy_test";
            TestMatch m = Match(setup: setup); m.Phase(MatchPhase.MeetingDiscussion);
            Assert.AreEqual(FailureCode.FeatureDisabled, m.Request(3, ActionVerb.RequestFootage, m.Layout.Cameras[0].Id, 0).Failure);
        }

        [Test]
        public void InformantCanRequestDuringDiscussionAndChargesAreBounded()
        {
            TestMatch m = Match(c => c.Informant.FootageCharges = 1);
            Assert.AreEqual(0, m.View(3).Me.FootageLeft);
            m.Phase(MatchPhase.MeetingDiscussion);
            Assert.AreEqual(1, m.View(3).Me.FootageLeft);
            Assert.AreEqual(FailureCode.NotSpeaker, m.Request(0, ActionVerb.RequestFootage, m.Layout.Cameras[0].Id, 0).Failure);
            EvidenceView footage = Request(m, 3, m.Layout.Cameras[0].Id, 0);
            Assert.AreEqual(DiscoverySource.Player, footage.Source); Assert.AreEqual(3, footage.SourcePlayer); Assert.AreEqual(0, footage.RiskWeight);
            Assert.Contains(footage.Id, m.View().Meeting.PresentedEvidence);
            Assert.IsTrue(m.Events.Any(e => e.Type == EventType.FootageShown && e.IntArg == footage.Id && e.Audience == Audience.All));
            Assert.AreEqual(0, m.View(3).Me.FootageLeft);
            Assert.AreEqual(FailureCode.NoCharges, m.Request(3, ActionVerb.RequestFootage, m.Layout.Cameras[0].Id, 0).Failure);
        }

        [Test]
        public void SpeakersHaveIndependentAllowancesAndProfileBonus()
        {
            TestMatch m = Match(c => c.ManagerProfiles[0].ExtraFootageRequests = 1);
            m.Phase(MatchPhase.MeetingDiscussion); m.Request(2, ActionVerb.Accuse, param: 0); m.Request(3, ActionVerb.Accuse, param: 1);
            m.Phase(MatchPhase.MeetingDefense1); int first = m.View().Meeting.Speaker;
            Assert.AreNotEqual(3, first); Assert.AreEqual(2, m.View(first).Me.FootageLeft);
            Request(m, first, m.Layout.Cameras[0].Id, 0); Request(m, first, m.Layout.Cameras[0].Id, 0);
            Assert.AreEqual(FailureCode.NoCharges, m.Request(first, ActionVerb.RequestFootage, m.Layout.Cameras[0].Id, 0).Failure);
            m.Phase(MatchPhase.MeetingDefense2); int second = m.View().Meeting.Speaker;
            Assert.AreNotEqual(first, second); Assert.AreEqual(2, m.View(second).Me.FootageLeft); Assert.AreEqual(0, m.View(first).Me.FootageLeft);
            Assert.AreEqual(FailureCode.NotSpeaker, m.Request(first, ActionVerb.RequestFootage, m.Layout.Cameras[0].Id, 0).Failure);
            Request(m, second, m.Layout.Cameras[0].Id, 0);
        }

        [Test]
        public void InformantSpeakerSpendsSpeakerPoolBeforeGeneralPool()
        {
            TestMatch m = Match(c => c.Informant.FootageCharges = 1);
            m.Phase(MatchPhase.MeetingDiscussion);
            m.Request(0, ActionVerb.Accuse, param: 3); m.Request(1, ActionVerb.Accuse, param: 3); m.Request(2, ActionVerb.Accuse, param: 0);
            m.Phase(MatchPhase.MeetingDefense1); Assert.AreEqual(3, m.View().Meeting.Speaker); Assert.AreEqual(2, m.View(3).Me.FootageLeft);
            Request(m, 3, m.Layout.Cameras[0].Id, 0);
            m.Phase(MatchPhase.MeetingDefense2); Assert.AreEqual(1, m.View(3).Me.FootageLeft);
            Request(m, 3, m.Layout.Cameras[0].Id, 0);
            Assert.AreEqual(FailureCode.NoCharges, m.Request(3, ActionVerb.RequestFootage, m.Layout.Cameras[0].Id, 0).Failure);
        }

        [TestCase(-1)]
        [TestCase(int.MaxValue)]
        public void InvalidSecondsDoNotSpendAllowance(int second)
        {
            TestMatch m = Match(); m.Phase(MatchPhase.MeetingDiscussion);
            int before = m.View(3).Me.FootageLeft;
            Assert.AreEqual(FailureCode.InvalidTarget, m.Request(3, ActionVerb.RequestFootage, m.Layout.Cameras[0].Id, second).Failure);
            Assert.AreEqual(before, m.View(3).Me.FootageLeft);
        }

        [Test]
        public void UnknownCameraAndAfterFreezeFailWhileBothTimeBoundsAreValid()
        {
            TestMatch m = Match(); m.Phase(MatchPhase.MeetingDiscussion); int freeze = (int)m.View().PhaseStartTime;
            Assert.AreEqual(FailureCode.InvalidTarget, m.Request(3, ActionVerb.RequestFootage, "camera.unknown", 0).Failure);
            Assert.AreEqual(FailureCode.InvalidTarget, m.Request(3, ActionVerb.RequestFootage, m.Layout.Cameras[0].Id, freeze + 1).Failure);
            Request(m, 3, m.Layout.Cameras[0].Id, 0);
            EvidenceView empty = Request(m, 3, m.Layout.Cameras[0].Id, freeze);
            Assert.IsEmpty(empty.Sightings); Assert.IsFalse(empty.NoRecording);
        }

        [Test]
        public void SamplesUseAbsoluteStartAndZoneRectanglesAndAscendingPlayerIds()
        {
            MatchSetup setup = TestMatch.Setup(); setup.StartTime = 125.5;
            setup.Players = new List<PlayerSlotInfo> { new PlayerSlotInfo { Id = 5 }, new PlayerSlotInfo { Id = 0 }, new PlayerSlotInfo { Id = 3 } };
            TestMatch m = Match(setup: setup); CameraDef camera = m.Layout.Cameras[0];
            foreach (int p in m.Engine.Players) m.Move(p, CameraCenter(camera));
            m.AdvanceTo(setup.StartTime + 2.001); m.Phase(MatchPhase.MeetingDiscussion);
            EvidenceView footage = Request(m, 3, camera.Id, 2);
            Assert.AreEqual(setup.StartTime + 2, footage.WindowStart); Assert.AreEqual(footage.WindowStart, footage.WindowEnd);
            CollectionAssert.AreEqual(new[] { 0, 3, 5 }, footage.Sightings.Select(s => s.Player));
            Assert.IsTrue(footage.Sightings.All(s => s.Time == setup.StartTime + 2));
        }

        [Test]
        public void SamplesCapturePublicActivityCarriedVariantAndCrouch()
        {
            TestMatch m = Match(); m.Work(); m.Do(0, ActionVerb.PickUp, EntityIds.Mug(0));
            StationDef coffee = m.Layout.GetStation(EntityIds.CoffeeMachine);
            CameraDef camera = m.Layout.Cameras.First(c => c.Covers(coffee.Position));
            ActionResult brew = m.Start(0, ActionVerb.BrewCoffee, coffee.Id);
            m.Engine.UpdateAvatar(0, new AvatarSample { Position = coffee.Position, Crouched = true });
            int second = (int)Math.Ceiling(m.Now) + 1; Assert.Less(second, brew.CompletesAt);
            m.AdvanceTo(second + 0.001); m.Phase(MatchPhase.MeetingDiscussion);
            FootageSighting sighting = Request(m, 3, camera.Id, second).Sightings.Single(s => s.Player == 0);
            Assert.AreEqual(PublicActivity.Brewing, sighting.Activity); Assert.AreEqual(ItemVariants.Mug, sighting.Carrying); Assert.IsTrue(sighting.Crouched);
        }

        [Test]
        public void WindowIncludesOnlySamplesInsideCameraAndSortsTimeThenPlayer()
        {
            TestMatch m = Match(c => c.Footage.WindowSeconds = 1); CameraDef camera = m.Layout.Cameras[0];
            m.Move(0, CameraCenter(camera)); m.Move(1, camera.ZoneMin); m.Move(2, camera.ZoneMax);
            m.AdvanceTo(4.001); m.Phase(MatchPhase.MeetingDiscussion);
            EvidenceView footage = Request(m, 3, camera.Id, 2);
            Assert.AreEqual(1, footage.WindowStart); Assert.AreEqual(3, footage.WindowEnd);
            Assert.AreEqual(9, footage.Sightings.Count);
            CollectionAssert.AreEqual(new[] { 1d, 1, 1, 2, 2, 2, 3, 3, 3 }, footage.Sightings.Select(s => s.Time));
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 0, 1, 2, 0, 1, 2 }, footage.Sightings.Select(s => s.Player));
            Assert.IsFalse(footage.NoRecording);
        }

        [Test]
        public void DisconnectedPlayersHaveNoSamplesAndLogStopsAtMeeting()
        {
            TestMatch m = Match(); CameraDef camera = m.Layout.Cameras[0];
            m.Move(0, CameraCenter(camera)); m.Move(1, CameraCenter(camera));
            m.Engine.SetConnected(1, false, m.Now); m.AdvanceTo(2.001); m.Engine.SetConnected(1, true, m.Now);
            m.Phase(MatchPhase.MeetingDiscussion);
            CollectionAssert.AreEqual(new[] { 0 }, Request(m, 3, camera.Id, 1).Sightings.Select(s => s.Player));
            int freeze = (int)m.View().PhaseStartTime;
            m.Move(2, CameraCenter(camera)); m.Advance(1);
            Assert.IsEmpty(Request(m, 3, camera.Id, freeze).Sightings);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void DarkSamplesAreExcludedAndOnlyAnAllDarkWindowHasNoRecording(bool allDark)
        {
            MatchSetup setup = TestMatch.Setup(); setup.ForceCards = new List<List<SabotageType>> { new List<SabotageType> { SabotageType.PowerCut } };
            TestMatch m = Match(c => { c.Footage.WindowSeconds = 1; c.GetSabotage(SabotageType.PowerCut).EffectSeconds = allDark ? 20 : 2; }, setup);
            m.Work(); CameraDef camera = m.Layout.Cameras[0]; m.Move(1, CameraCenter(camera));
            m.Do(0, ActionVerb.CutPower, EntityIds.FuseBox);
            double darkAt = m.Events.Last(e => e.Type == EventType.PowerDown).Time;
            double lightAt = m.View().Office.PowerBackAt;
            int second = (int)Math.Ceiling(m.Now) + 1;
            m.Phase(MatchPhase.MeetingDiscussion);
            EvidenceView footage = Request(m, 3, camera.Id, second);
            Assert.AreEqual(allDark, footage.NoRecording);
            Assert.IsTrue(footage.Sightings.All(s => s.Time < darkAt || s.Time >= lightAt));
            if (allDark) Assert.IsEmpty(footage.Sightings); else Assert.IsNotEmpty(footage.Sightings);
        }

        [Test]
        public void EmptyCameraIsNotARecordingOutageAndViewsAreDeepCopied()
        {
            TestMatch m = Match(); m.AdvanceTo(2.001); m.Phase(MatchPhase.MeetingDiscussion);
            EvidenceView empty = Request(m, 3, m.Layout.Cameras[0].Id, 2);
            Assert.IsEmpty(empty.Sightings); Assert.IsFalse(empty.NoRecording);
            EvidenceView initial = Request(m, 3, m.Layout.Cameras[0].Id, 0);
            Assert.IsNotEmpty(initial.Sightings);
            initial.Sightings[0].Carrying = "changed"; initial.Sightings.Clear();
            EvidenceView unchanged = m.View(0).Evidence.Single(e => e.Id == initial.Id);
            Assert.IsNotEmpty(unchanged.Sightings); Assert.IsTrue(unchanged.Sightings.All(s => s.Carrying != "changed"));
            byte[] first = WireCodec.EncodeView(m.View(0)); byte[] second = WireCodec.EncodeView(m.View(0)); CollectionAssert.AreEqual(first, second);
        }

        [Test]
        public void NonpositiveSamplingIntervalIsSafeAndLargeTickVisitsSamplingDeadlines()
        {
            TestMatch m = Match(c => c.Footage.SampleInterval = 0); CameraDef camera = m.Layout.Cameras[0]; m.Move(0, CameraCenter(camera));
            double meetingAt = m.Config.Durations.Briefing + m.Config.Durations.Work1 + m.Config.Durations.Raid + m.Config.Durations.Work2 + m.Config.Durations.FinalInspection;
            m.Engine.Tick(meetingAt);
            ActionResult result = m.Engine.Submit(3, new ActionRequest(m.Engine.MatchId, 999, ActionVerb.RequestFootage, camera.Id, 5), meetingAt);
            Assert.IsTrue(result.Accepted, result.Failure.ToString());
            EvidenceView footage = m.Engine.BuildView(0, meetingAt).Evidence.Last(e => e.Kind == EvidenceKind.CctvFootage);
            Assert.AreEqual(5, footage.Sightings.Single(s => s.Player == 0).Time);
        }
    }
}
