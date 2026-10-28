using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class HighlightCausalityTests
    {
        [TestCase(1)]
        [TestCase(2)]
        public void SameTimeReplantLinksDiscoveredEvidenceOnlyToTheFinalPlacement(int finalDesk)
        {
            var match = new TestMatch(configure: c =>
            {
                c.Interaction.PickUpOpenSeconds = c.Interaction.PickUpContainerSeconds = 0;
                SabotageDef plant = c.GetSabotage(SabotageType.PlantItem);
                plant.DurationSeconds = plant.ActorCooldownSeconds = plant.SameTargetCooldownSeconds = 0;
                c.Verdict.VoteWeight = 1;
                c.Verdict.RiskWeight = c.Verdict.PerformanceWeight = c.Verdict.FavorWeight = 0;
            });
            match.Work();
            match.Plant(0, 1);
            match.Plant(0, finalDesk);
            var incidents = match.Engine.BuildHostDebugView().Incidents;
            Assert.AreEqual(2, incidents.Count);
            Assert.AreEqual(incidents[0].Time, incidents[1].Time, "Both placements complete at the same host timestamp.");
            Assert.AreEqual(incidents[0].Actor, incidents[1].Actor);
            Assert.AreEqual(incidents[0].ItemId, incidents[1].ItemId);

            match.Phase(MatchPhase.MeetingDiscussion);
            foreach (int p in match.Engine.Players)
                if (p != finalDesk) Assert.IsTrue(match.Request(p, ActionVerb.Accuse, param: finalDesk).Accepted);
            match.Phase(MatchPhase.MeetingVote);
            foreach (int p in match.Engine.Players)
                if (p != finalDesk) Assert.IsTrue(match.Request(p, ActionVerb.CastBallot, param: finalDesk).Accepted);
            match.Finish();

            PlayerView view = match.View();
            EvidenceView evidence = view.Evidence.Single(e => e.Kind == EvidenceKind.SuspiciousItemFound);
            Assert.AreEqual(finalDesk, evidence.AttributedTo);
            Assert.Greater(evidence.RiskWeight, 0);
            Assert.AreEqual(finalDesk, view.Result.FiredPlayer);
            Assert.IsFalse(view.Result.Incidents[0].Discovered);
            Assert.IsTrue(view.Result.Incidents[1].Discovered);
            Assert.AreEqual(incidents[1].Id, view.Result.Highlight.IncidentId,
                "Only the placement actually found by the manager contributed evidence risk.");
            Assert.AreEqual(finalDesk, view.Result.Highlight.DeskIndex);
        }
    }
}
