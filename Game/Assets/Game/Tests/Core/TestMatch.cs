using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    /// <summary>A deterministic host driver. All mutations go through the public adapter API.</summary>
    public sealed class TestMatch
    {
        public readonly MatchConfig Config;
        public readonly OfficeLayout Layout;
        public readonly MatchEngine Engine;
        public readonly List<GameEvent> Events = new List<GameEvent>();
        public double Now { get; private set; }
        private readonly uint[] requests = new uint[Protocol.MaxPlayers];

        public TestMatch(int seed = 1977, Action<MatchConfig> configure = null,
            Action<OfficeLayout> arrange = null, IWorldQueries world = null, MatchSetup setup = null)
        {
            Config = MatchConfig.CreateDefault();
            // Legacy rules fixtures explicitly opt into drawer searches and neutral profile weights.
            Config.ManagerProfiles.Clear();
            Config.ManagerProfiles.Add(new ManagerProfileDef { Id = "legacy_test", SearchAllDrawers = true });
            Config.Durations.Briefing = 1;
            Config.Durations.Work1 = 180;
            Config.Durations.Raid = 20;
            Config.Durations.RaidWarning = 1;
            Config.Durations.Work2 = 180;
            Config.Durations.FinalInspection = 20;
            Config.Durations.FinalWarning = 1;
            Config.Durations.MeetingDiscussion = 3;
            Config.Durations.MeetingDefense = 2;
            Config.Durations.MeetingVote = 2;
            Config.Durations.MeetingVerdict = 1;
            Config.Tasks.InitialCoffeeOrders = 3;
            Config.Tasks.InitialReportOrders = 3;
            Config.Tasks.CoffeeOrderInterval = 1000;
            Config.Tasks.ReportOrderInterval = 1000;
            Config.Tasks.InitialDirtyDesks = 4;
            Config.Tasks.DirtIntervalSeconds = 1000;
            Config.Tasks.PhoneIntervalMin = 12;
            Config.Tasks.PhoneIntervalMax = 12;
            Config.Manager.WalkSpeed = 30;
            if (configure != null) configure(Config);
            Layout = DefaultOffice.Create();
            Layout.ManagerRoute.Clear();
            for (int d = 0; d < Layout.Desks.Count; d++)
                Layout.ManagerRoute.Add(new ManagerStop
                {
                    Position = Layout.GetDesk(d).Position + new Vec3(0, 0, 1.6f),
                    Yaw = 180, DeskIndex = d, DwellSeconds = 1
                });
            if (arrange != null) arrange(Layout);
            if (setup == null) setup = Setup(seed);
            if (setup.ForceCards == null)
            {
                setup.ForceCards = new List<List<SabotageType>>();
                for (int p = 0; p < Protocol.MaxPlayers; p++) setup.ForceCards.Add(new List<SabotageType> { SabotageType.LockWorkstation });
            }
            Now = setup.StartTime;
            Engine = new MatchEngine(Config, Layout, setup, world ?? OpenWorld.Instance);
            foreach (int p in Engine.Players) Move(p, Layout.BoundsMax + new Vec3(20 + p * 10, 0, 20));
            Capture();
        }

        public static MatchSetup Setup(int seed = 1977, bool force = true)
        {
            var result = new MatchSetup { MatchId = 17, Seed = seed, ForceInformant = force ? 3 : PlayerIds.None };
            string[] names = { "Mavi", "Sarı", "Pembe", "Yeşil" };
            for (int p = 0; p < 4; p++)
                result.Players.Add(new PlayerSlotInfo { Id = p, Name = names[p], ColorIndex = p });
            if (force)
            {
                result.ForceObjectives = new List<string> { "OFFICE_ALL_ROUNDER", "FIXER", "SCAPEGOAT", "" };
                result.ForceTargets = new List<int> { -1, -1, 0, -1 };
            }
            return result;
        }

        public PlayerView View(int player = 0) { return Engine.BuildView(player, Now); }
        public ItemView Item(string id, int player = 0) { return View(player).Items.FirstOrDefault(i => i.Id == id); }
        public PlayerPublicView Player(int player) { return View(player).Players.Single(p => p.Id == player); }
        public void Capture() { Events.AddRange(Engine.DrainEvents()); }
        public void Move(int player, Vec3 position, float yaw = 0, float speed = 0)
        {
            Engine.UpdateAvatar(player, new AvatarSample { Position = position, Yaw = yaw, Speed = speed });
        }
        public void MoveTo(int player, string target)
        {
            SlotDef slot = Layout.GetSlot(target);
            if (slot != null) { Move(player, slot.Position); return; }
            StationDef station = Layout.GetStation(target);
            if (station != null) { Move(player, station.Position); return; }
            DeskDef desk = Layout.GetDesk(target);
            if (desk != null) { Move(player, desk.Position); return; }
            ItemView item = Item(target, player);
            Assert.IsNotNull(item, "Target not visible to P" + player + ": " + target);
            if (item.Location == ItemLocationKind.Slot) Move(player, Layout.GetSlot(item.SlotId).Position);
            else Move(player, item.FloorPosition);
        }
        public ActionResult Request(int player, ActionVerb verb, string target = "", int param = -1)
        {
            var result = Engine.Submit(player, new ActionRequest(Engine.MatchId, ++requests[player], verb, target, param), Now);
            Capture();
            return result;
        }
        public ActionResult Start(int player, ActionVerb verb, string target = "", int param = -1)
        {
            if (!string.IsNullOrEmpty(target)) MoveTo(player, target);
            ActionResult result = Request(player, verb, target, param);
            Assert.IsTrue(result.Accepted, "P" + player + " " + verb + " " + target + ": " + result.Failure);
            return result;
        }
        public void Do(int player, ActionVerb verb, string target = "", int param = -1)
        {
            var result = Start(player, verb, target, param);
            if (result.CompletesAt > Now) AdvanceTo(result.CompletesAt + 0.0001);
            Assert.IsNull(View(player).Me.ActiveInteraction, "Interaction should complete: " + verb);
        }
        public void Advance(double seconds) { AdvanceTo(Now + seconds); }
        public void AdvanceTo(double until)
        {
            while (Now + 0.0000001 < until)
            {
                Now = Math.Min(until, Now + 0.1);
                Engine.Tick(Now);
                Capture();
            }
        }
        public void Phase(MatchPhase target)
        {
            int limit = 16;
            while (Engine.Phase != target && limit-- > 0)
            {
                Assert.IsFalse(Engine.IsFinished, "Cannot advance from " + Engine.Phase + " to " + target);
                AdvanceTo(View(Engine.Players[0]).PhaseEndTime + 0.0001);
            }
            Assert.AreEqual(target, Engine.Phase);
        }
        public void Work() { Phase(MatchPhase.Work1); }
        public void Finish() { Phase(MatchPhase.Results); }
        public void Coffee(int player, int mug)
        {
            Do(player, ActionVerb.PickUp, EntityIds.Mug(mug));
            Do(player, ActionVerb.BrewCoffee, EntityIds.CoffeeMachine);
            Do(player, ActionVerb.Place, EntityIds.DeliveryTray);
        }
        public string Print(int player)
        {
            int before = Events.Count(e => e.Type == EventType.ReportPrinted);
            Do(player, ActionVerb.WriteReport, EntityIds.DeskComputer(Player(player).DeskIndex));
            for (int i = 0; i < 100 && Events.Count(e => e.Type == EventType.ReportPrinted) == before; i++) Advance(0.2);
            var reports = View(player).Items.Where(i => i.Kind == ItemKind.Report && i.ReportOwner == player && i.SlotId == EntityIds.PrinterTray).ToList();
            Assert.IsNotEmpty(reports, "A completed print job should create a physical report");
            return reports.Last().Id;
        }
        public void Plant(int actor, int desk, string variant = ItemVariants.Duck, bool drawer = true)
        {
            Do(actor, ActionVerb.PickUp, EntityIds.Suspicious(variant));
            Do(actor, ActionVerb.Place, drawer ? EntityIds.DeskDrawer(desk) : EntityIds.DeskSurface(desk));
        }
        public void SwapInto(int actor, int nameplate, int desk)
        {
            Do(actor, ActionVerb.PickUp, EntityIds.Nameplate(nameplate));
            Do(actor, ActionVerb.Place, EntityIds.DeskNameplate(desk));
        }
        public EvidenceView FindEvidence(string item, int recipient = 0)
        {
            return View(recipient).Evidence.First(e => e.ItemId == item && e.Status == EvidenceStatus.Active);
        }
    }
}
