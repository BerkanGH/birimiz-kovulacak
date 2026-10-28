using System;
using System.Collections.Generic;
namespace BirimizKovulacak.Core
{
    public sealed partial class MatchEngine
    {
        private double nextCoffee = double.PositiveInfinity, nextReport = double.PositiveInfinity, nextDirt = double.PositiveInfinity, nextPhone = double.PositiveInfinity, printEnds;
        private int phoneCall, reportSerial;
        private readonly Queue<int> printQueue = new Queue<int>();
        partial void InitializeTasks()
        {
            quota.CoffeeOrders = Math.Min(Config.Tasks.InitialCoffeeOrders, Config.Tasks.MaxCoffeeOrders); quota.ReportOrders = Math.Min(Config.Tasks.InitialReportOrders, Config.Tasks.MaxReportOrders);
            var dirty = desks.FindAll(d => Present(d.View.AssignedOwner)); random.Shuffle(dirty); for (int n = 0; n < Math.Min(dirty.Count, Config.Tasks.InitialDirtyDesks); n++) dirty[n].View.Dirty = true;
        }
        partial void TasksPhaseChanged(MatchPhase previous)
        {
            if (Phase == MatchPhase.Work1) { nextCoffee = After(Config.Tasks.CoffeeOrderInterval); nextReport = After(Config.Tasks.ReportOrderInterval); nextDirt = After(Config.Tasks.DirtIntervalSeconds); nextPhone = clock + PhoneInterval(); }
            if (!Office && Phase != MatchPhase.Briefing) { quota.PhoneRinging = false; quota.PhoneRingEnds = 0; }
        }
        private double After(double interval) { return interval > 0 ? clock + interval : double.PositiveInfinity; }
        private double PhoneInterval() { return Math.Max(0.001, random.Between(Config.Tasks.PhoneIntervalMin, Config.Tasks.PhoneIntervalMax)); }
        partial void NextTaskTime(ref double next)
        {
            if (!Office) return;
            next = Math.Min(next, Math.Min(Math.Min(nextCoffee, nextReport), nextDirt));
            if (!quota.PhoneRinging) next = Math.Min(next, nextPhone);
            else if (!AnswerInProgress()) next = Math.Min(next, Math.Max(clock, quota.PhoneRingEnds));
            if (!officeStatus.InternetDown && printQueue.Count != 0 && printEnds > 0 && PrinterHasRoom()) next = Math.Min(next, Math.Max(clock, printEnds));
            foreach (DeskState d in desks) if (d.View.PcLocked) next = Math.Min(next, Math.Max(clock, d.View.PcLockedUntil));
        }
        partial void AdvanceTasks()
        {
            if (!Office) return;
            if (clock >= nextCoffee) { if (quota.CoffeeOrders < Config.Tasks.MaxCoffeeOrders) { quota.CoffeeOrders++; Emit(EventType.OrderAdded, arg: (int)TaskType.Coffee); } nextCoffee = After(Config.Tasks.CoffeeOrderInterval); }
            if (clock >= nextReport) { if (quota.ReportOrders < Config.Tasks.MaxReportOrders) { quota.ReportOrders++; Emit(EventType.OrderAdded, arg: (int)TaskType.Report); } nextReport = After(Config.Tasks.ReportOrderInterval); }
            if (clock >= nextDirt)
            {
                var clean = desks.FindAll(d => Present(d.View.AssignedOwner) && !d.View.Dirty); if (clean.Count > 0) { DeskState d = clean[random.Index(clean.Count)]; d.View.Dirty = true; Emit(EventType.DeskDirty, target: d.Def.Id); }
                nextDirt = After(Config.Tasks.DirtIntervalSeconds);
            }
            if (!quota.PhoneRinging && clock >= nextPhone) { phoneCall++; quota.PhoneRinging = true; quota.PhoneRingEnds = clock + Math.Max(0, Config.Tasks.PhoneRingSeconds); Emit(EventType.PhoneRinging); }
            if (quota.PhoneRinging && clock >= quota.PhoneRingEnds && !AnswerInProgress()) { quota.PhoneRinging = false; quota.PhoneRingEnds = 0; nextPhone = clock + PhoneInterval(); Emit(EventType.PhoneMissed); }
            foreach (DeskState d in desks) if (d.View.PcLocked && clock >= d.View.PcLockedUntil) Unlock(d);
            if (!officeStatus.InternetDown && printQueue.Count > 0 && clock >= printEnds && PrinterHasRoom())
            {
                int owner = printQueue.Dequeue(); string id = EntityIds.Report(++reportSerial);
                items.Add(new ItemState { View = new ItemView { Id = id, Kind = ItemKind.Report, Variant = ItemVariants.Report, ReportOwner = owner, Location = ItemLocationKind.Slot, SlotId = EntityIds.PrinterTray, SlotIndex = FreeIndex(EntityIds.PrinterTray) }, PlacementSlot = EntityIds.PrinterTray, PlacedAt = clock });
                Emit(EventType.ReportPrinted, item: id, arg: owner); printEnds = printQueue.Count == 0 ? 0 : clock + Math.Max(0, Config.Tasks.PrintSeconds);
            }
        }
        private bool AnswerInProgress() { foreach (int id in playerIds) { PlayerState p = players[id]; if (p.Interaction != null && p.Interaction.Request.Verb == ActionVerb.AnswerPhone && p.Interaction.PhoneCall == phoneCall) return true; } return false; }
        private bool PrinterHasRoom() { SlotDef tray = Layout.GetSlot(EntityIds.PrinterTray); return tray != null && FreeIndex(tray.Id) < Math.Min(tray.Capacity, Config.Tasks.PrinterTrayCapacity); }
        private void EnqueuePrint(int p) { printQueue.Enqueue(p); if (printQueue.Count == 1) printEnds = clock + Math.Max(0, Config.Tasks.PrintSeconds); Changed(); }
        private void AwardTask(int p, TaskType task)
        {
            if (quota.Frozen) return;
            bool reached = quota.Points >= quota.Target; float points = Config.Tasks.PointsFor(task); quota.Points += points; players[p].Public.Performance += points;
            players[p].Public.TasksCompleted++; players[p].TaskCounts[(int)task]++; if (Phase == MatchPhase.Work2) players[p].Work2Tasks++;
            AwardFavor(p, task);
            Emit(EventType.TaskCompleted, p, arg: (int)task); if (!reached && quota.Points >= quota.Target) Emit(EventType.QuotaReached);
        }
        partial void FillStation(StationView station) { if (station.Kind == StationKind.Printer) { station.PrintQueue = new List<int>(printQueue); station.PrintEndsAt = printEnds; } }
    }
}
