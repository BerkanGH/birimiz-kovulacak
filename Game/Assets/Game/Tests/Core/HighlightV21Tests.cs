using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class HighlightV21Tests
    {
        private static MatchSetup Setup(int[] ids = null)
        {
            MatchSetup setup = TestMatch.Setup();
            if (ids != null)
            {
                setup.Players = ids.Reverse().Select(p => new PlayerSlotInfo { Id = p, Name = "P" + p }).ToList();
                setup.ForceObjectives = null;
                setup.ForceTargets = null;
            }
            setup.ForceCards = new List<List<SabotageType>>();
            for (int p = 0; p < Protocol.MaxPlayers; p++) setup.ForceCards.Add(new List<SabotageType>
            {
                SabotageType.PowerCut, SabotageType.InternetOutage, SabotageType.FakeEmail
            });
            return setup;
        }

        private static TestMatch Match(Action<MatchConfig> configure = null, MatchSetup setup = null)
        {
            return new TestMatch(setup: setup ?? Setup(), configure: c =>
            {
                c.Tasks.MailEnabled = false;
                c.Tasks.PhoneIntervalMin = c.Tasks.PhoneIntervalMax = 1000;
                c.Footage.SampleInterval = 1;
                c.Footage.WindowSeconds = 0;
                c.Verdict.VoteWeight = 1;
                c.Verdict.RiskWeight = c.Verdict.PerformanceWeight = c.Verdict.FavorWeight = 0;
                if (configure != null) configure(c);
            });
        }

        private static MatchResultView Fire(TestMatch match, int player)
        {
            match.Phase(MatchPhase.MeetingDiscussion);
            foreach (int p in match.Engine.Players)
                if (p != player) Assert.IsTrue(match.Request(p, ActionVerb.Accuse, param: player).Accepted);
            match.Phase(MatchPhase.MeetingVote);
            foreach (int p in match.Engine.Players)
                if (p != player) Assert.IsTrue(match.Request(p, ActionVerb.CastBallot, param: player).Accepted);
            match.Finish();
            MatchResultView result = match.View().Result;
            Assert.AreEqual(player, result.FiredPlayer);
            return result;
        }

        private static IncidentReveal Incident(TestMatch match, SabotageType type)
        {
            return match.Engine.BuildHostDebugView().Incidents.Last(i => i.Type == type);
        }

        private static void IsHighlight(MatchResultView result, IncidentReveal incident, string key)
        {
            Assert.IsNotNull(result.Highlight);
            Assert.AreEqual(incident.Id, result.Highlight.IncidentId);
            Assert.AreEqual(incident.Actor, result.Highlight.Actor);
            Assert.AreEqual(incident.DeskIndex, result.Highlight.DeskIndex);
            Assert.AreEqual(key, result.Highlight.Key);
        }

        private static byte[] VisibleBytes(TestMatch match, int player)
        {
            PlayerView view = match.View(player);
            view.StateVersion = 0;
            return WireCodec.EncodeView(view);
        }

        private static byte[] PositionBytes(Vec3 position)
        {
            return BitConverter.GetBytes(position.X).Concat(BitConverter.GetBytes(position.Y))
                .Concat(BitConverter.GetBytes(position.Z)).ToArray();
        }

        private static bool Contains(byte[] bytes, byte[] value)
        {
            for (int offset = 0; offset <= bytes.Length - value.Length; offset++)
            {
                int i = 0;
                while (i < value.Length && bytes[offset + i] == value[i]) i++;
                if (i == value.Length) return true;
            }
            return false;
        }

        [Test]
        public void NoIncidentsMeansNoHighlightForAnyRecipient()
        {
            TestMatch match = Match(); match.Finish();
            foreach (int p in match.Engine.Players)
            {
                Assert.IsEmpty(match.View(p).Result.Incidents);
                Assert.IsNull(match.View(p).Result.Highlight);
                Assert.IsNull(WireCodec.DecodeView(WireCodec.EncodeView(match.View(p))).Result.Highlight);
            }
        }

        [Test]
        public void EvidenceAgainstFiredPlayerBeatsGreaterMisconduct()
        {
            TestMatch match = Match(c => c.GetSabotage(SabotageType.PowerCut).BaseMisconduct = 40);
            match.Work(); match.Do(2, ActionVerb.CutPower, EntityIds.FuseBox);
            match.Plant(0, 1);
            IncidentReveal plant = Incident(match, SabotageType.PlantItem);
            MatchResultView result = Fire(match, 1);
            Assert.Greater(result.Incidents.Single(i => i.Type == SabotageType.PowerCut).Misconduct,
                result.Incidents.Single(i => i.Id == plant.Id).Misconduct);
            Assert.IsTrue(result.Incidents.Single(i => i.Id == plant.Id).Discovered);
            IsHighlight(result, plant, "recap.planted");
        }

        [TestCase(1f, SabotageType.PlantItem, "recap.planted")]
        [TestCase(5f, SabotageType.FakeEmail, "recap.fake_email")]
        public void GreatestEvidenceRiskAgainstFiredPlayerDeterminesHighlight(float emailRisk, SabotageType expected, string key)
        {
            TestMatch match = Match(c => c.Risk.EmailLog = emailRisk);
            match.Work(); match.Do(1, ActionVerb.WriteReport, EntityIds.DeskComputer(1));
            match.Do(2, ActionVerb.SendFakeEmail, EntityIds.DeskComputer(1), 0);
            match.Plant(0, 1);
            IncidentReveal chosen = Incident(match, expected);
            MatchResultView result = Fire(match, 1);
            Assert.AreEqual(emailRisk, match.View().Evidence.Single(e => e.Kind == EvidenceKind.EmailLog).RiskWeight);
            IsHighlight(result, chosen, key);
        }

        [Test]
        public void GreaterEvidenceRiskAgainstSomeoneElseDoesNotWin()
        {
            TestMatch match = Match(c => c.Risk.EmailLog = 20);
            match.Work(); match.Do(2, ActionVerb.WriteReport, EntityIds.DeskComputer(2));
            match.Do(0, ActionVerb.SendFakeEmail, EntityIds.DeskComputer(2), 0);
            match.Plant(0, 1);
            IncidentReveal plant = Incident(match, SabotageType.PlantItem);
            IsHighlight(Fire(match, 1), plant, "recap.planted");
        }

        [Test]
        public void EqualEvidenceRiskChoosesEarliestIncident()
        {
            TestMatch match = Match(); match.Work();
            match.Plant(0, 1, ItemVariants.Duck);
            IncidentReveal first = Incident(match, SabotageType.PlantItem);
            match.Plant(2, 1, ItemVariants.Stapler);
            MatchResultView result = Fire(match, 1);
            Assert.AreEqual(2, match.View().Evidence.Count(e => e.Kind == EvidenceKind.SuspiciousItemFound && e.AttributedTo == 1));
            IsHighlight(result, first, "recap.planted");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SelectionUsesRemainingRiskAfterEvidenceIsMovedOrDisposed(bool dispose)
        {
            TestMatch match = Match(c => { c.Risk.EmailLog = 1; c.Risk.MovedEvidenceFactor = .25f; });
            match.Work(); match.Do(1, ActionVerb.WriteReport, EntityIds.DeskComputer(1));
            match.Do(2, ActionVerb.SendFakeEmail, EntityIds.DeskComputer(1), 0);
            IncidentReveal email = Incident(match, SabotageType.FakeEmail);
            match.Plant(0, 1);
            match.Phase(MatchPhase.Work2);
            EvidenceView plant = match.FindEvidence(EntityIds.Suspicious(ItemVariants.Duck));
            Assert.Greater(plant.RiskWeight, 1);
            match.Do(0, ActionVerb.PickUp, plant.ItemId);
            if (dispose) match.Do(0, ActionVerb.Place, EntityIds.JunkBin);
            else match.Do(0, ActionVerb.Drop);
            MatchResultView result = Fire(match, 1);
            EvidenceView changed = match.View().Evidence.Single(e => e.Id == plant.Id);
            Assert.AreEqual(dispose ? EvidenceStatus.Disposed : EvidenceStatus.Moved, changed.Status);
            Assert.AreEqual(dispose ? 0 : .75f, changed.RiskWeight);
            IsHighlight(result, email, "recap.fake_email");
        }

        [Test]
        public void PresentedWitnessEvidenceLinksToTheIncidentAndItsTrueActor()
        {
            TestMatch match = Match(c =>
            {
                c.Risk.WitnessStatement = 8;
                c.GetSabotage(SabotageType.PowerCut).BaseMisconduct = 40;
            });
            match.Work(); match.Do(0, ActionVerb.PickUp, EntityIds.Suspicious(ItemVariants.Duck));
            match.MoveTo(2, EntityIds.DeskDrawer(1));
            match.Do(0, ActionVerb.Place, EntityIds.DeskDrawer(1));
            IncidentReveal plant = Incident(match, SabotageType.PlantItem);
            PrivateNote witness = match.View(2).Me.Notes.Single(n => n.Kind == NoteKind.Witness
                && n.Subject == 0 && n.TargetId == EntityIds.DeskDrawer(1));
            match.Do(3, ActionVerb.CutPower, EntityIds.FuseBox);
            match.Phase(MatchPhase.MeetingDiscussion);
            Assert.IsTrue(match.Request(2, ActionVerb.PresentNote, param: witness.Id).Accepted);
            MatchResultView result = Fire(match, 0);
            Assert.IsTrue(match.View().Evidence.Any(e => e.Kind == EvidenceKind.WitnessStatement && e.SubjectPlayer == 0 && e.RiskWeight == 8));
            IsHighlight(result, plant, "recap.planted");
        }

        [Test]
        public void NameplateFramingSumsAllContributingEvidenceInsteadOfTakingOneLargestRecord()
        {
            TestMatch match = Match(c => c.Risk.EmailLog = 4);
            match.Work(); match.Do(1, ActionVerb.WriteReport, EntityIds.DeskComputer(1));
            match.Do(0, ActionVerb.SendFakeEmail, EntityIds.DeskComputer(1), 0);
            match.SwapInto(2, 1, 0); match.Do(2, ActionVerb.Drop);
            IncidentReveal swap = Incident(match, SabotageType.NameplateSwap);
            match.Plant(2, 0, ItemVariants.Duck);
            match.Plant(3, 0, ItemVariants.Stapler);
            MatchResultView result = Fire(match, 1);
            EvidenceView[] framed = match.View().Evidence.Where(e => e.Kind == EvidenceKind.SuspiciousItemFound && e.AttributedTo == 1).ToArray();
            Assert.AreEqual(2, framed.Length);
            Assert.IsTrue(framed.All(e => e.RiskWeight == 3));
            Assert.AreEqual(4, match.View().Evidence.Single(e => e.Kind == EvidenceKind.EmailLog).RiskWeight);
            IsHighlight(result, swap, "recap.nameplate_swapped");
        }

        [TestCase(2f, 8f, SabotageType.InternetOutage, "recap.internet_down")]
        [TestCase(8f, 2f, SabotageType.PowerCut, "recap.power_down")]
        [TestCase(5f, 5f, SabotageType.PowerCut, "recap.power_down")]
        public void NoContributingEvidenceFallsBackToGreatestMisconductThenEarliest(float power, float internet, SabotageType expected, string key)
        {
            TestMatch match = Match(c =>
            {
                c.GetSabotage(SabotageType.PowerCut).BaseMisconduct = power;
                c.GetSabotage(SabotageType.InternetOutage).BaseMisconduct = internet;
            });
            match.Work(); match.Do(0, ActionVerb.CutPower, EntityIds.FuseBox);
            match.Do(2, ActionVerb.CutInternet, EntityIds.Router);
            IncidentReveal chosen = Incident(match, expected);
            match.Finish();
            Assert.IsEmpty(match.View().Evidence);
            IsHighlight(match.View().Result, chosen, key);
        }

        [Test]
        public void SimultaneousEqualMisconductUsesDeterministicIncidentOrder()
        {
            TestMatch match = Match(c =>
            {
                c.GetSabotage(SabotageType.PowerCut).DurationSeconds = 3;
                c.GetSabotage(SabotageType.InternetOutage).DurationSeconds = 3;
                c.GetSabotage(SabotageType.PowerCut).BaseMisconduct = 5;
                c.GetSabotage(SabotageType.InternetOutage).BaseMisconduct = 5;
            });
            match.Work();
            ActionResult first = match.Start(0, ActionVerb.CutPower, EntityIds.FuseBox);
            ActionResult second = match.Start(2, ActionVerb.CutInternet, EntityIds.Router);
            Assert.AreEqual(first.CompletesAt, second.CompletesAt);
            match.AdvanceTo(first.CompletesAt + .001); match.Finish();
            CollectionAssert.AreEqual(new[] { first.CompletesAt, first.CompletesAt }, match.View().Result.Incidents.Select(i => i.Time));
            IsHighlight(match.View().Result, match.View().Result.Incidents[0], "recap.power_down");
        }

        [Test]
        public void WindowUsesInteractionStartAndCompletionInAbsoluteHostTime()
        {
            MatchSetup setup = Setup(); setup.StartTime = 125.5;
            TestMatch match = Match(setup: setup); match.Work(); match.Advance(20);
            double start = match.Now;
            ActionResult action = match.Start(0, ActionVerb.CutPower, EntityIds.FuseBox);
            match.AdvanceTo(action.CompletesAt + .001); match.Finish();
            HighlightView highlight = match.View().Result.Highlight;
            Assert.AreEqual(start - 6, highlight.Start, .000001);
            Assert.AreEqual(action.CompletesAt + 4, highlight.End, .000001);
            double[] expectedTimes = Enumerable.Range(0, 401).Select(n => setup.StartTime + n)
                .Where(t => t >= highlight.Start && t <= highlight.End).ToArray();
            CollectionAssert.AreEqual(expectedTimes, highlight.Samples.Select(s => s.Time).Distinct());
        }

        [Test]
        public void WindowStartClampsToFirstLoggedSample()
        {
            MatchSetup setup = Setup(); setup.StartTime = 125.5;
            TestMatch match = Match(setup: setup); match.Work();
            ActionResult action = match.Start(0, ActionVerb.CutPower, EntityIds.FuseBox);
            match.AdvanceTo(action.CompletesAt + .001); match.Finish();
            HighlightView highlight = match.View().Result.Highlight;
            Assert.AreEqual(setup.StartTime, highlight.Start);
            Assert.AreEqual(setup.StartTime, highlight.Samples.Min(s => s.Time));
            Assert.AreEqual(action.CompletesAt + 4, highlight.End, .000001);
        }

        [Test]
        public void WindowEndClampsToLastSampleBeforeMeeting()
        {
            TestMatch match = Match(c =>
            {
                c.Durations.Work1 = 10; c.Durations.Raid = 1;
                c.Durations.Work2 = 10; c.Durations.FinalInspection = 3;
                c.GetSabotage(SabotageType.PlantItem).DurationSeconds = 1;
            });
            match.Work(); match.Do(0, ActionVerb.PickUp, EntityIds.Suspicious(ItemVariants.Duck));
            match.Phase(MatchPhase.FinalInspection);
            double start = match.Now;
            ActionResult action = match.Start(0, ActionVerb.Place, EntityIds.DeskDrawer(1));
            double freeze = match.View().PhaseEndTime;
            match.AdvanceTo(action.CompletesAt + .001); match.Finish();
            HighlightView highlight = match.View().Result.Highlight;
            Assert.Less(freeze, action.CompletesAt + 4);
            Assert.AreEqual(start - 6, highlight.Start, .000001);
            Assert.AreEqual(freeze - 1, highlight.End);
            Assert.AreEqual(highlight.End, highlight.Samples.Max(s => s.Time));
            Assert.IsTrue(highlight.Samples.All(s => s.Time < freeze));
        }

        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void EveryPresentPlayerIsSampledInTimeThenIdOrderIncludingSparseRosters(int count)
        {
            int[] ids = count == 3 ? new[] { 0, 3, 5 } : count == 4 ? new[] { 0, 1, 3, 5 }
                : count == 5 ? new[] { 0, 1, 3, 4, 5 } : new[] { 0, 1, 2, 3, 4, 5 };
            TestMatch match = Match(setup: Setup(ids)); match.Work(); match.Advance(10);
            match.Do(0, ActionVerb.CutPower, EntityIds.FuseBox);
            double sampledAt = Math.Ceiling(match.Now);
            foreach (int p in ids)
                match.Engine.UpdateAvatar(p, new AvatarSample { Position = new Vec3(100 + p, 2 + p, 150 + p), Yaw = 17 + p * 31, Crouched = p % 2 == 1 });
            match.AdvanceTo(sampledAt + .001); match.Finish();
            HighlightView highlight = match.View().Result.Highlight;
            foreach (var group in highlight.Samples.GroupBy(s => s.Time))
                CollectionAssert.AreEqual(ids, group.Select(s => s.Player));
            CollectionAssert.AreEqual(highlight.Samples.OrderBy(s => s.Time).ThenBy(s => s.Player), highlight.Samples);
            foreach (int p in ids)
            {
                FootageSighting sample = highlight.Samples.Single(s => s.Time == sampledAt && s.Player == p);
                Assert.AreEqual(new Vec3(100 + p, 2 + p, 150 + p), sample.Position);
                Assert.AreEqual(17 + p * 31, sample.Yaw);
                Assert.AreEqual(p % 2 == 1, sample.Crouched);
            }
        }

        [Test]
        public void DarkReplaySamplesRetainActivityCarryCrouchPositionAndYaw()
        {
            TestMatch match = Match(); match.Work();
            match.Do(3, ActionVerb.PickUp, EntityIds.Mug(0)); match.Advance(10);
            ActionResult brew = match.Start(3, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine);
            Vec3 position = match.Layout.GetStation(EntityIds.CoffeeMachine).Position;
            match.Engine.UpdateAvatar(3, new AvatarSample { Position = position, Yaw = 123.5f, Crouched = true });
            match.Do(0, ActionVerb.CutPower, EntityIds.FuseBox);
            double darkAt = match.Events.Last(e => e.Type == EventType.PowerDown).Time;
            int sampledAt = (int)Math.Ceiling(match.Now);
            Assert.Less(sampledAt, brew.CompletesAt);
            Assert.Less(sampledAt, match.View().Office.PowerBackAt);
            match.Phase(MatchPhase.MeetingDiscussion);
            CameraDef camera = match.Layout.Cameras.First(c => c.Covers(position));
            Assert.IsTrue(match.Request(3, ActionVerb.RequestFootage, camera.Id, sampledAt).Accepted);
            EvidenceView footage = match.View().Evidence.Last(e => e.Kind == EvidenceKind.CctvFootage);
            Assert.IsTrue(footage.NoRecording); Assert.IsEmpty(footage.Sightings);
            match.Finish();
            FootageSighting sample = match.View().Result.Highlight.Samples.Single(s => s.Time == sampledAt && s.Player == 3);
            Assert.Greater(sample.Time, darkAt);
            Assert.AreEqual(PublicActivity.Brewing, sample.Activity);
            Assert.AreEqual(ItemVariants.Mug, sample.Carrying);
            Assert.IsTrue(sample.Crouched);
            Assert.AreEqual(position, sample.Position);
            Assert.AreEqual(123.5f, sample.Yaw);
        }

        [Test]
        public void HighlightExistsEvenWhenFootageRequestsAreDisabled()
        {
            TestMatch match = Match(c => c.Footage.Enabled = false);
            match.Work(); match.Do(0, ActionVerb.CutPower, EntityIds.FuseBox); match.Finish();
            Assert.IsFalse(match.View().Features.Footage);
            Assert.IsNotNull(match.View().Result.Highlight);
            Assert.IsNotEmpty(match.View().Result.Highlight.Samples);
        }

        [Test]
        public void LoggedPositionsRemainBytePrivateUntilResultsAndAreThenRevealedToEveryone()
        {
            TestMatch first = Match(), second = Match();
            foreach (TestMatch match in new[] { first, second })
            {
                match.Work(); match.Advance(10); match.Do(0, ActionVerb.CutPower, EntityIds.FuseBox);
            }
            Vec3 secret = new Vec3(1234.125f, 678.625f, 901.375f);
            first.Move(1, secret, 123.25f); second.Move(1, secret + new Vec3(17, 0, 0), 219.5f);
            first.Advance(2); second.Advance(2);
            first.Move(1, new Vec3(100, 0, 100)); second.Move(1, new Vec3(100, 0, 100));
            foreach (MatchPhase phase in new[] { MatchPhase.Work1, MatchPhase.Raid, MatchPhase.Work2,
                MatchPhase.FinalInspection, MatchPhase.MeetingDiscussion, MatchPhase.MeetingDefense1,
                MatchPhase.MeetingDefense2, MatchPhase.MeetingVote, MatchPhase.MeetingVerdict })
            {
                first.Phase(phase); second.Phase(phase);
                Assert.IsNull(first.Engine.Result);
                foreach (int p in first.Engine.Players)
                {
                    Assert.IsNull(first.View(p).Result);
                    CollectionAssert.AreEqual(VisibleBytes(first, p), VisibleBytes(second, p), phase + " P" + p);
                    Assert.IsFalse(Contains(VisibleBytes(first, p), PositionBytes(secret)));
                    byte[] events = WireCodec.EncodeEvents(first.Events.Where(e => e.IsVisibleTo(p)).ToList());
                    Assert.IsFalse(Contains(events, PositionBytes(secret)));
                }
            }
            first.Finish(); second.Finish();
            Assert.AreNotEqual(first.View().Result.ResultHash, second.View().Result.ResultHash);
            foreach (int p in first.Engine.Players)
            {
                Assert.IsNotNull(first.View(p).Result.Highlight);
                Assert.IsTrue(Contains(VisibleBytes(first, p), PositionBytes(secret)));
                Assert.IsTrue(first.View(p).Result.Highlight.Samples.Any(s => s.Player == 1 && s.Position.Equals(secret)));
            }
        }

        [Test]
        public void AbandonedMatchNeverRevealsHighlight()
        {
            TestMatch match = Match(); match.Work(); match.Do(0, ActionVerb.CutPower, EntityIds.FuseBox);
            match.Engine.Abandon(AbandonReason.HostAborted, match.Now);
            Assert.IsNull(match.Engine.Result);
            foreach (int p in match.Engine.Players)
                Assert.IsNull(WireCodec.DecodeView(WireCodec.EncodeView(match.View(p))).Result);
        }

        [Test]
        public void FootagePositionsAndYawAreIncludedOnlyForSightingsInsideCameraZone()
        {
            TestMatch match = Match(); CameraDef camera = match.Layout.Cameras[0];
            Vec3 center = (camera.ZoneMin + camera.ZoneMax) * .5f;
            Vec3 outside = new Vec3(1234.125f, 678.625f, 901.375f);
            match.Move(0, center, 13.25f); match.Move(1, camera.ZoneMin, 47.75f);
            match.Move(2, camera.ZoneMax, 181.5f); match.Move(3, outside, 313.125f);
            match.AdvanceTo(5.001); match.Phase(MatchPhase.MeetingDiscussion);
            Assert.IsTrue(match.Request(3, ActionVerb.RequestFootage, camera.Id, 5).Accepted);
            EvidenceView footage = match.View().Evidence.Last(e => e.Kind == EvidenceKind.CctvFootage);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, footage.Sightings.Select(s => s.Player));
            CollectionAssert.AreEqual(new[] { center, camera.ZoneMin, camera.ZoneMax }, footage.Sightings.Select(s => s.Position));
            CollectionAssert.AreEqual(new[] { 13.25f, 47.75f, 181.5f }, footage.Sightings.Select(s => s.Yaw));
            Assert.IsTrue(footage.Sightings.All(s => camera.Covers(s.Position)));
            foreach (int p in match.Engine.Players)
            {
                byte[] bytes = VisibleBytes(match, p);
                Assert.IsFalse(Contains(bytes, PositionBytes(outside)));
                EvidenceView decoded = WireCodec.DecodeView(bytes).Evidence.Single(e => e.Id == footage.Id);
                CollectionAssert.AreEqual(footage.Sightings.Select(s => s.Position), decoded.Sightings.Select(s => s.Position));
                CollectionAssert.AreEqual(footage.Sightings.Select(s => s.Yaw), decoded.Sightings.Select(s => s.Yaw));
            }
        }

        [Test]
        public void HighlightIsDeterministicHashedDetachedAndFrozenAfterResults()
        {
            TestMatch first = Match(), second = Match();
            foreach (TestMatch match in new[] { first, second })
            {
                match.Work(); match.Advance(10); match.Do(0, ActionVerb.CutPower, EntityIds.FuseBox);
                match.Move(2, new Vec3(98.25f, 11.75f, 65.5f), 87.5f); match.Advance(2); match.Finish();
            }
            string hash = first.View().Result.ResultHash;
            Assert.AreEqual(hash, WireCodec.HashResult(first.View().Result));
            Assert.AreEqual(hash, second.View().Result.ResultHash);
            CollectionAssert.AreEqual(WireCodec.EncodeView(first.View()), WireCodec.EncodeView(second.View()));
            foreach (int p in first.Engine.Players) Assert.AreEqual(hash, first.View(p).Result.ResultHash);
            MatchResultView detached = first.View().Result;
            detached.Highlight.Samples[0].Yaw += 1;
            Assert.AreNotEqual(hash, WireCodec.HashResult(detached));
            detached.Highlight.Samples[0].Position = new Vec3(-999, -888, -777);
            detached.Highlight.Key = "changed"; detached.Highlight.Samples.Clear();
            first.Move(0, new Vec3(1000, 1000, 1000), 300); first.Advance(10);
            Assert.AreEqual(hash, first.View().Result.ResultHash);
            Assert.AreEqual(hash, WireCodec.HashResult(first.View().Result));
            Assert.IsNotEmpty(first.View().Result.Highlight.Samples);
            Assert.AreEqual("recap.power_down", first.View().Result.Highlight.Key);
        }
    }
}
