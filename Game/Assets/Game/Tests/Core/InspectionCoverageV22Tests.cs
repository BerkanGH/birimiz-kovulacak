using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class InspectionCoverageV22Tests
    {
        [Test]
        public void SixPlayerDefaultRaidAndFinalInspectEveryAssignedDesk(
            [Values(ManagerProfileIds.Paragoz, ManagerProfileIds.Disiplinci, ManagerProfileIds.Kayirmaci, ManagerProfileIds.Paranoyak)] string profile,
            [Values(-1, 0, 1, 2, 3, 4, 5)] int tipDesk)
        {
            var match = new DefaultMatch(6, profile);
            var tipCallers = new HashSet<int>();
            foreach (MatchPhase phase in new[] { MatchPhase.Raid, MatchPhase.FinalInspection })
            {
                match.Phase(phase == MatchPhase.Raid ? MatchPhase.Work1 : MatchPhase.Work2);
                // Queue immediately before the inspection; an earlier Work2 tip could feed the surprise visit.
                match.AdvanceTo(match.View.PhaseEndTime - 0.1);
                if (tipDesk >= 0)
                {
                    int caller = match.View.Players.First(p => p.DeskIndex != tipDesk && !tipCallers.Contains(p.Id)).Id;
                    tipCallers.Add(caller);
                    ActionResult tip = match.Engine.Submit(caller,
                        new ActionRequest(match.Engine.MatchId, 1, ActionVerb.TipManager, EntityIds.Desk(tipDesk)), match.Now);
                    Assert.IsTrue(tip.Accepted, tip.Failure.ToString());
                    Assert.AreEqual(match.Now, tip.CompletesAt);
                }
                match.Phase(phase);
                match.AdvanceTo(match.View.PhaseEndTime);
                int[] inspected = match.View.Observations.Where(o => o.Phase == phase && o.Kind == ObservationKind.NotAtDesk)
                    .Select(o => o.DeskIndex).Distinct().OrderBy(d => d).ToArray();
                CollectionAssert.AreEqual(match.AssignedDesks, inspected, profile + " " + phase + " tip " + tipDesk);
                if (tipDesk >= 0)
                {
                    Assert.AreEqual(tipDesk, match.View.Observations.First(o => o.Phase == phase && o.DeskIndex >= 0).DeskIndex);
                    Assert.IsTrue(match.View.Observations.Any(o => o.Phase == phase && o.DeskIndex == tipDesk &&
                        (o.Kind == ObservationKind.TipClean || o.Kind == ObservationKind.TipFound)));
                }
            }
        }

        [TestCase(17)]
        [TestCase(701)]
        [TestCase(1977)]
        public void FivePlayerDefaultSurpriseInspectsAllDesksOrRunsUntilItsConfiguredDeadline(int seed)
        {
            var match = new DefaultMatch(5, ManagerProfileIds.Disiplinci, seed);
            match.Phase(MatchPhase.Work2);
            double phaseEnd = match.View.PhaseEndTime;
            while (!match.View.Manager.Surprise && match.Now < phaseEnd) match.AdvanceTo(Math.Min(phaseEnd, match.Now + 0.1));
            GameEvent started = match.Events.Single(e => e.Type == EventType.SurpriseInspection);
            double deadline = Math.Min(phaseEnd, started.Time + match.Config.Manager.SurpriseSeconds);
            while (match.View.Manager.Surprise && match.Now < deadline)
                match.AdvanceTo(Math.Min(deadline, match.Now + 0.1));

            int[] inspected = match.View.Observations.Where(o => o.Phase == MatchPhase.Work2 && o.Kind == ObservationKind.NotAtDesk)
                .Select(o => o.DeskIndex).Distinct().OrderBy(d => d).ToArray();
            Assert.IsNotEmpty(inspected);
            Assert.IsFalse(match.View.Manager.Surprise);
            if (match.Now < phaseEnd) Assert.AreEqual(ManagerState.InOffice, match.View.Manager.State);
            else
            {
                Assert.AreEqual(MatchPhase.FinalInspection, match.Engine.Phase);
                Assert.AreEqual(ManagerState.Warning, match.View.Manager.State);
            }
            if (inspected.Length == match.AssignedDesks.Length)
                CollectionAssert.AreEqual(match.AssignedDesks, inspected);
            else
                Assert.AreEqual(deadline, match.Now, 0.000001, "A partial surprise route must use its full configured window.");
            Assert.LessOrEqual(match.Now, deadline + 0.000001);
            Assert.AreEqual(1, match.Events.Count(e => e.Type == EventType.SurpriseInspection));
            Assert.IsFalse(match.Events.Any(e => e.Type == EventType.ManagerWarning && e.Phase == MatchPhase.Work2));
        }

        // Intentionally bypass TestMatch's compact timing, fast route and unshuffled desk fixtures.
        private sealed class DefaultMatch
        {
            internal readonly MatchConfig Config = MatchConfig.CreateDefault();
            internal readonly MatchEngine Engine;
            internal readonly List<GameEvent> Events = new List<GameEvent>();
            internal readonly int[] AssignedDesks;
            internal double Now;
            internal PlayerView View => Engine.BuildView(0, Now);

            internal DefaultMatch(int count, string profile, int seed = 701)
            {
                Config.ApplyHeadcount(count);
                MatchSetup setup = PlayersV2Tests.Setup(Enumerable.Range(0, count).ToArray());
                setup.Seed = seed; setup.ManagerProfileId = profile;
                OfficeLayout layout = DefaultOffice.Create();
                Engine = new MatchEngine(Config, layout, setup, OpenWorld.Instance);
                foreach (int p in Engine.Players)
                    Engine.UpdateAvatar(p, new AvatarSample { Position = layout.BoundsMax + new Vec3(20 + p, 0, 20) });
                AssignedDesks = View.Desks.Where(d => d.AssignedOwner != PlayerIds.None).Select(d => d.Index).OrderBy(d => d).ToArray();
                Events.AddRange(Engine.DrainEvents());
            }

            internal void AdvanceTo(double until)
            {
                Now = until;
                Engine.Tick(Now);
                Events.AddRange(Engine.DrainEvents());
            }

            internal void Phase(MatchPhase target)
            {
                for (int i = 0; Engine.Phase != target && i < 12; i++) AdvanceTo(View.PhaseEndTime);
                Assert.AreEqual(target, Engine.Phase);
            }
        }
    }
}
