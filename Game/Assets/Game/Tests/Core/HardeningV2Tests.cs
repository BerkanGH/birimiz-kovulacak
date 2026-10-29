using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class HardeningV2Tests
    {
        [Test]
        public void SixPlayerEveryVerbScriptHasIdenticalEventsAndResultHash()
        {
            HashSet<ActionVerb> firstVerbs, secondVerbs;
            TestMatch first = RunScript(60017, null, out firstVerbs);
            TestMatch second = RunScript(60017, null, out secondVerbs);
            foreach (ActionVerb verb in Enum.GetValues(typeof(ActionVerb)))
                if (verb != ActionVerb.None) Assert.IsTrue(firstVerbs.Contains(verb), "Missing successful verb: " + verb);
            foreach (TaskType task in Enum.GetValues(typeof(TaskType)))
                Assert.IsTrue(first.Events.Any(e => e.Type == EventType.TaskCompleted && e.IntArg == (int)task), task.ToString());
            foreach (SabotageType type in Enum.GetValues(typeof(SabotageType)))
                Assert.IsTrue(first.View().Result.Incidents.Any(i => i.Type == type), type.ToString());
            Assert.AreEqual(first.View().Result.ResultHash, second.View().Result.ResultHash);
            CollectionAssert.AreEqual(WireCodec.EncodeEvents(first.Events), WireCodec.EncodeEvents(second.Events));
            Assert.AreEqual(first.View().Result.ResultHash, WireCodec.HashResult(first.View().Result));
            Assert.IsTrue(first.View().Result.CompanySucceeded);
            Assert.AreEqual(6, first.View().Result.Players.Count);
            Assert.AreEqual(1, first.View().Result.Players.Count(p => p.Fired));
            Assert.IsTrue(first.Engine.AllRematchVotesIn);
            foreach (int p in first.Engine.Players)
            {
                Assert.AreEqual(first.View().Result.ResultHash, first.View(p).Result.ResultHash);
                Assert.AreEqual(0, first.View().Result.Players.Single(o => o.Player == p).CareerDelta);
            }
            Assert.IsTrue(first.View().Evidence.Any(e => e.Kind == EvidenceKind.CctvFootage && e.Sightings.Any(s => s.Player == 5 && s.Crouched)));
        }

        [Test]
        public void FullSixPlayerFiveDayWeekChainsProfilesCareerTotalsAndEmployeeOfTheMonth()
        {
            var history = new SessionHistory { WeekNumber = 3 };
            var totals = new int[Protocol.MaxPlayers];
            var allVerbs = new HashSet<ActionVerb>();
            var hashes = new List<string>();
            for (int day = 0; day < 5; day++)
            {
                Assert.AreEqual(day, history.NextWeekDay);
                MatchSetup setup = SixPlayerSetup(9700 + day);
                setup.Mode = GameMode.Week; setup.WeekDay = history.NextWeekDay; setup.History = history;
                HashSet<ActionVerb> used;
                TestMatch m = RunScript(setup.Seed, setup, out used);
                allVerbs.UnionWith(used);
                PlayerView view = m.View();
                MatchResultView result = view.Result;
                Assert.AreEqual(GameMode.Week, result.Mode);
                Assert.AreEqual(day, result.WeekDay);
                Assert.AreEqual(day, view.WeekDay);
                Assert.IsTrue(result.CompanySucceeded, "day " + day);
                Assert.AreEqual(day > 0, view.Features.Cards);
                Assert.AreEqual(day >= 2, view.Features.TipLine);
                Assert.AreEqual(day >= 2, view.Features.Footage);
                Assert.AreEqual(day == 4 ? 2 : 1, view.Features.CareerMultiplier);
                string scheduled = m.Config.WeekDays[day].ProfileId;
                if (scheduled.Length != 0) Assert.AreEqual(scheduled, result.ManagerProfileId);
                else
                {
                    Assert.IsTrue(m.Config.ManagerProfiles.Any(p => p.Id == result.ManagerProfileId));
                    Assert.AreNotEqual(history.PastManagerProfiles.Last(), result.ManagerProfileId);
                }
                Assert.AreEqual(day == 4, result.WeekFinished);
                foreach (PlayerOutcome outcome in result.Players)
                {
                    Assert.AreEqual(totals[outcome.Player], view.Players.Single(p => p.Id == outcome.Player).CareerPoints);
                    CareerConfig c = m.Config.Career;
                    double expected = view.Features.CareerMultiplier * ((result.CompanySucceeded && !outcome.Fired ? c.QuotaMet : 0)
                        + (!outcome.Fired ? c.Survived : 0)
                        + (outcome.Role == Role.Employee && outcome.Won ? c.ObjectiveWon : 0)
                        + (outcome.Role == Role.Informant && result.InformantWon ? c.InformantWon : 0)
                        + (result.Favourites.Contains(outcome.Player) ? c.Favourite : 0))
                        + (outcome.Fired ? c.Fired : 0);
                    int delta = (int)Math.Round(expected, MidpointRounding.AwayFromZero);
                    Assert.AreEqual(delta, outcome.CareerDelta, "day " + day + " player " + outcome.Player);
                    totals[outcome.Player] += delta;
                    Assert.AreEqual(totals[outcome.Player], outcome.CareerTotal);
                    Assert.AreEqual(day == 0 ? 0 : 6, outcome.Cards.Count);
                }
                if (day == 4)
                {
                    int highest = totals.Max();
                    CollectionAssert.AreEqual(Enumerable.Range(0, Protocol.MaxPlayers).Where(p => totals[p] == highest), result.EmployeeOfTheMonth);
                    Assert.IsNotEmpty(result.EmployeeOfTheMonth, "Ayin Elemani has at least one winner");
                }
                else Assert.IsEmpty(result.EmployeeOfTheMonth);
                hashes.Add(result.ResultHash);
                foreach (int p in m.Engine.Players) Assert.AreEqual(result.ResultHash, m.View(p).Result.ResultHash);
                history = m.Engine.BuildNextHistory();
                Assert.AreEqual(day + 1, history.MatchesPlayed);
                Assert.AreEqual(day + 1, history.PastManagerProfiles.Count);
                Assert.AreEqual(result.ManagerProfileId, history.PastManagerProfiles.Last());
                Assert.AreEqual(day == 4 ? 0 : day + 1, history.NextWeekDay);
                Assert.AreEqual(day == 4 ? 4 : 3, history.WeekNumber);
                CollectionAssert.AreEqual(day == 4 ? new int[Protocol.MaxPlayers] : totals, history.CareerPoints);
            }
            Assert.AreEqual(5, hashes.Distinct().Count());
            foreach (ActionVerb verb in Enum.GetValues(typeof(ActionVerb)))
                if (verb != ActionVerb.None) Assert.IsTrue(allVerbs.Contains(verb), "Week never used " + verb);
        }

        /// <summary>Populated six-recipient results fixture, also used by the performance harness.</summary>
        public static TestMatch RunSixPlayerScript(int seed = 60017)
        {
            HashSet<ActionVerb> ignored;
            return RunScript(seed, null, out ignored);
        }

        private static MatchSetup SixPlayerSetup(int seed)
        {
            MatchSetup setup = PlayersV2Tests.Setup(0, 1, 2, 3, 4, 5);
            setup.Seed = seed; setup.ForceInformant = 5;
            setup.ForceObjectives = new List<string> { "OFFICE_ALL_ROUNDER", "FIXER", "SCAPEGOAT", "CLEAN_HANDS", "QUIET_STAR", "" };
            setup.ForceTargets = new List<int> { -1, -1, 0, -1, -1, -1 };
            setup.ForceCards = new List<List<SabotageType>>();
            for (int p = 0; p < Protocol.MaxPlayers; p++)
                setup.ForceCards.Add(new List<SabotageType> { SabotageType.LockWorkstation, SabotageType.FakeEmail,
                    SabotageType.PullPlug, SabotageType.SaltCoffee, SabotageType.InternetOutage, SabotageType.PowerCut });
            return setup;
        }

        private static TestMatch RunScript(int seed, MatchSetup setup, out HashSet<ActionVerb> used)
        {
            if (setup == null) setup = SixPlayerSetup(seed);
            var m = new TestMatch(setup: setup, configure: c =>
            {
                c.ManagerProfiles = MatchConfig.CreateDefault().ManagerProfiles;
                c.QuotaTarget = 5;
                c.Tasks.InitialDirtyDesks = Protocol.MaxPlayers;
                c.Durations.Work1 = 240;
            });
            var script = new Script(m);
            script.Do(0, ActionVerb.Emote, param: (int)EmoteId.Wave);
            script.Do(0, ActionVerb.CallPlayer, param: 1);
            script.Do(1, ActionVerb.DeclineCall);
            m.Work();
            script.Do(0, ActionVerb.SetWorkPose, param: 1);
            script.Start(5, ActionVerb.WriteReport, EntityIds.DeskComputer(5));
            script.Do(5, ActionVerb.CancelInteraction);

            CameraDef camera = m.Layout.Cameras[0];
            Vec3 recorded = (camera.ZoneMin + camera.ZoneMax) * 0.5f;
            m.Engine.UpdateAvatar(5, new AvatarSample { Position = recorded, Crouched = true });
            m.Advance(1.1);
            int footageSecond = (int)(m.Now - setup.StartTime);

            Coffee(script, 0, 0);
            Coffee(script, 1, 5);
            script.Do(1, ActionVerb.WriteReport, EntityIds.DeskComputer(1));
            for (int i = 0; !m.View().Items.Any(x => x.Kind == ItemKind.Report && x.ReportOwner == 1) && i < 100; i++) m.Advance(0.2);
            string report = m.View().Items.First(x => x.Kind == ItemKind.Report && x.ReportOwner == 1).Id;
            script.Do(2, ActionVerb.PickUp, report);
            script.Do(2, ActionVerb.Place, EntityIds.HideBehindPrinter);
            script.Do(0, ActionVerb.SearchContainer, EntityIds.HideBehindPrinter);
            script.Do(0, ActionVerb.PickUp, report);
            script.Do(0, ActionVerb.Place, EntityIds.DeliveryTray);
            if (m.View().Features.Cards) script.Do(2, ActionVerb.SendFakeEmail, EntityIds.DeskComputer(1), 0);
            script.Do(1, ActionVerb.LockScreen, EntityIds.DeskComputer(1));

            for (int i = 0; !m.View().Quota.PhoneRinging && i < 100; i++) m.Advance(0.25);
            script.Do(0, ActionVerb.AnswerPhone, EntityIds.Phone);
            script.Do(0, ActionVerb.PickUp, EntityIds.Spray(0));
            script.Do(0, ActionVerb.CleanDesk, EntityIds.Desk(0));
            script.Do(0, ActionVerb.Drop);
            ItemView mail = m.View().Items.First(i => i.Kind == ItemKind.Mail);
            script.Do(0, ActionVerb.PickUp, mail.Id);
            script.Do(0, ActionVerb.Place, EntityIds.DeskSurface(mail.AddressedDesk));

            script.Do(0, ActionVerb.CallPlayer, param: 2);
            script.Do(2, ActionVerb.AnswerCall);
            script.Do(0, ActionVerb.HangUp);
            if (m.View().Features.Cards)
            {
                script.Do(4, ActionVerb.LockWorkstation, EntityIds.DeskComputer(2));
                script.Do(0, ActionVerb.UnlockWorkstation, EntityIds.DeskComputer(2));
                script.Start(3, ActionVerb.WriteReport, EntityIds.DeskComputer(3));
                script.Do(4, ActionVerb.PullPlug, EntityIds.Desk(3));
                Assert.IsNull(m.View(3).Me.ActiveInteraction);
                script.Do(0, ActionVerb.PlugIn, EntityIds.Desk(3));
                script.Do(2, ActionVerb.PickUp, EntityIds.Mug(1));
                script.Do(2, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine);
                script.Do(2, ActionVerb.SaltCoffee, EntityIds.CoffeeMachine);
                script.Do(2, ActionVerb.Drop);
                script.Do(4, ActionVerb.PickUp, EntityIds.Mug(1));
                script.Do(4, ActionVerb.TasteCoffee);
                script.Do(4, ActionVerb.Drop);
                script.Do(3, ActionVerb.PickUp, EntityIds.Mug(2));
                script.Do(3, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine);
                script.Do(3, ActionVerb.SaltCoffee, EntityIds.CoffeeMachine);
                script.Do(3, ActionVerb.Drop);
                script.Do(4, ActionVerb.PickUp, EntityIds.Mug(2));
                script.Do(4, ActionVerb.Place, EntityIds.DeliveryTray);
                script.Do(2, ActionVerb.CutInternet, EntityIds.Router);
                script.Do(0, ActionVerb.RestartRouter, EntityIds.Router);
                script.Do(3, ActionVerb.CutPower, EntityIds.FuseBox);
                script.Do(0, ActionVerb.ResetFuse, EntityIds.FuseBox);
            }

            script.Do(2, ActionVerb.PickUp, EntityIds.Suspicious(ItemVariants.Duck));
            script.Do(2, ActionVerb.Place, EntityIds.DeskSurface(0));
            script.Do(3, ActionVerb.PickUp, EntityIds.Suspicious(ItemVariants.Stapler));
            script.Do(3, ActionVerb.Place, EntityIds.DeskSurface(4));
            script.Do(1, ActionVerb.PickUp, EntityIds.Nameplate(1));
            script.Do(1, ActionVerb.Place, EntityIds.DeskNameplate(0));
            script.Do(1, ActionVerb.Drop);
            script.Do(3, ActionVerb.PickUp, EntityIds.Mug(3));
            script.Do(3, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine);
            script.Do(3, ActionVerb.SpillCoffee, EntityIds.Desk(4));
            script.Do(3, ActionVerb.Drop);
            script.Do(0, ActionVerb.PickUp, EntityIds.Spray(0));
            script.Do(0, ActionVerb.CleanSpill, EntityIds.Desk(4));
            script.Do(0, ActionVerb.Drop);
            script.Do(1, ActionVerb.PickUp, EntityIds.Clutter(0));
            script.Do(1, ActionVerb.Place, EntityIds.DeskSurface(5));
            script.Do(5, ActionVerb.AuditScanItem, EntityIds.Nameplate(5));
            if (m.View().Features.TipLine) script.Do(0, ActionVerb.TipManager, EntityIds.Desk(4));

            m.Phase(MatchPhase.Raid); m.Advance(15);
            EvidenceView duck = m.FindEvidence(EntityIds.Suspicious(ItemVariants.Duck));
            EvidenceView stapler = m.FindEvidence(EntityIds.Suspicious(ItemVariants.Stapler));
            script.Do(5, ActionVerb.AuditScanEvidence, param: duck.Id);
            script.Do(5, ActionVerb.SealEvidence, param: stapler.Id);
            script.Do(1, ActionVerb.PickUp, EntityIds.Suspicious(ItemVariants.Duck));
            script.Do(1, ActionVerb.Place, EntityIds.JunkBin);
            m.Phase(MatchPhase.Work2);
            script.Do(0, ActionVerb.CallPlayer, param: 3);
            script.Do(3, ActionVerb.AnswerCall);
            script.Do(0, ActionVerb.HangUp);

            m.Phase(MatchPhase.MeetingDiscussion);
            script.Do(5, ActionVerb.PullCallLogs);
            PrivateNote callLog = m.View(5).Me.Notes.Single(n => n.Kind == NoteKind.CallLog);
            script.Do(5, ActionVerb.PresentNote, param: callLog.Id);
            if (m.View().Features.Footage) script.Do(5, ActionVerb.RequestFootage, camera.Id, footageSecond);
            foreach (int p in m.Engine.Players) if (p != 2) script.Do(p, ActionVerb.Accuse, param: 2);
            m.Phase(MatchPhase.MeetingDefense1);
            if (m.View().Features.Footage) script.Do(m.View().Meeting.Speaker, ActionVerb.RequestFootage, camera.Id, footageSecond);
            m.Phase(MatchPhase.MeetingVote);
            foreach (int p in m.Engine.Players) script.Do(p, ActionVerb.CastBallot, param: p == 2 ? PlayerIds.None : 2);
            m.Finish();
            foreach (int p in m.Engine.Players) script.Do(p, ActionVerb.RematchVote);
            used = script.Used;
            return m;
        }

        private static void Coffee(Script script, int player, int mug)
        {
            script.Do(player, ActionVerb.PickUp, EntityIds.Mug(mug));
            script.Do(player, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine);
            script.Do(player, ActionVerb.Place, EntityIds.DeliveryTray);
        }

        private sealed class Script
        {
            private readonly TestMatch match;
            internal readonly HashSet<ActionVerb> Used = new HashSet<ActionVerb>();
            internal Script(TestMatch match) { this.match = match; }
            internal ActionResult Start(int p, ActionVerb verb, string target = "", int param = -1)
            {
                bool physical = (verb >= ActionVerb.PickUp && verb <= ActionVerb.SetWorkPose)
                    || (verb >= ActionVerb.LockScreen && verb <= ActionVerb.ResetFuse) || verb == ActionVerb.AuditScanItem;
                if (physical && target.Length != 0) match.MoveTo(p, target);
                ActionResult result = match.Request(p, verb, target, param);
                Assert.IsTrue(result.Accepted, "P" + p + " " + verb + " " + target + ": " + result.Failure);
                Used.Add(verb);
                return result;
            }
            internal void Do(int p, ActionVerb verb, string target = "", int param = -1)
            {
                ActionResult result = Start(p, verb, target, param);
                if (result.CompletesAt > match.Now)
                {
                    match.AdvanceTo(result.CompletesAt + 0.0001);
                    Assert.IsNull(match.View(p).Me.ActiveInteraction, verb.ToString());
                }
            }
        }
    }
}
