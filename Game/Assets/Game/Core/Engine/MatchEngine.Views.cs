using System;
using System.Collections.Generic;
namespace BirimizKovulacak.Core
{
    public sealed partial class MatchEngine
    {
        public PlayerView BuildView(int recipient, double now)
        {
            if (!Present(recipient)) throw new ArgumentOutOfRangeException(nameof(recipient));
            var view = new PlayerView { MatchId = MatchId, Recipient = recipient, StateVersion = StateVersion, Phase = Phase, PhaseSerial = PhaseSerial, PhaseStartTime = phaseStart, PhaseEndTime = phaseEnd, ServerTime = now, AbandonReason = abandonReason };
            view.Quota = Snapshot.Copy(quota); view.Quota.DirtyDesks = 0;
            view.Quota.OpenMail = OpenMail();
            foreach (DeskState d in desks) { view.Desks.Add(Snapshot.Copy(d.View)); if (d.View.Dirty) view.Quota.DirtyDesks++; }
            foreach (int p in playerIds)
            {
                PlayerPublicView pub = Snapshot.Copy(players[p].Public); pub.SuspicionLevel = 0;
                float risk = RiskFor(p); foreach (float t in Config.Risk.MeterThresholds) if (risk >= t) pub.SuspicionLevel++;
                view.Players.Add(pub);
            }
            foreach (ItemState item in items) if (Visible(item, recipient))
            {
                ItemView iv = Snapshot.Copy(item.View); iv.IsEvidence = evidence.Exists(e => e.View.ItemId == iv.Id && e.View.Status == EvidenceStatus.Active); view.Items.Add(iv);
            }
            var closed = new Dictionary<string, List<ItemView>>();
            foreach (ItemView iv in view.Items)
            {
                if (iv.Location != ItemLocationKind.Slot) continue;
                SlotDef sd = Layout.GetSlot(iv.SlotId);
                if (sd == null || !sd.Closed) continue;
                List<ItemView> group;
                if (!closed.TryGetValue(iv.SlotId, out group)) { group = new List<ItemView>(); closed[iv.SlotId] = group; }
                group.Add(iv);
            }
            foreach (List<ItemView> group in closed.Values)
            {
                group.Sort((x, y) => x.SlotIndex.CompareTo(y.SlotIndex));
                for (int k = 0; k < group.Count; k++) group[k].SlotIndex = k;
            }
            foreach (StationDef s in Layout.Stations)
            {
                var sv = new StationView { Id = s.Id, Kind = s.Kind };
                foreach (int p in playerIds) { InteractionState a = players[p].Interaction; if (a != null && a.Reservation == s.Id) { sv.UsedBy = p; sv.BusyUntil = a.End; } }
                FillStation(sv); view.Stations.Add(sv);
            }
            foreach (EvidenceState e in evidence) { EvidenceView ev = Snapshot.Copy(e.View); ev.RiskWeight = EvidenceWeight(ev); view.Evidence.Add(ev); }
            foreach (ObservationView o in observations) view.Observations.Add(Snapshot.Copy(o));
            for (int k = Math.Max(0, log.Count - MaxLogEntriesInView); k < log.Count; k++) view.Log.Add(Snapshot.Copy(log[k]));
            view.Manager = Snapshot.Copy(manager); view.Meeting = Snapshot.Copy(meeting);
            view.Me = Snapshot.Copy(players[recipient].Private);
            view.Me.FootageLeft = FootageLeft(recipient);
            ObjectiveView objective = view.Me.Objective; RefreshObjective(recipient, ref objective); view.Me.Objective = objective;
            InteractionState active = players[recipient].Interaction;
            view.Me.ActiveInteraction = active == null ? null : new InteractionView { Verb = active.Request.Verb, TargetId = active.Request.TargetId, StartTime = active.Start, EndTime = active.End };
            view.Result = Phase == MatchPhase.Results ? Snapshot.Copy(Result) : null;
            FillProfileView(view);
            FillSabotageV2View(view);
            return view;
        }
        public HostDebugView BuildHostDebugView()
        {
            var result = new HostDebugView { MatchId = MatchId, Seed = Seed, Phase = Phase, QuotaPoints = quota.Points, ProcessedRequests = processedRequests, RejectedRequests = rejectedRequests };
            result.ManagerProfileId = ManagerProfileId;
            FillCalls(result.Calls);
            foreach (int p in playerIds) { result.Roles.Add(players[p].Private.Role); result.ObjectiveCards.Add(players[p].Objective == null ? "" : players[p].Objective.Id); result.ObjectiveTargets.Add(players[p].ObjectiveTarget); result.TrueMisconduct.Add(Misconduct(p)); result.ObservedRisk.Add(RiskFor(p)); }
            foreach (IncidentState incident in incidents) result.Incidents.Add(Snapshot.Copy(incident.Reveal));
            foreach (ItemState item in items) result.ItemHandlers.Add(item.View.Id, new List<int>(item.Handlers));
            FillSabotageV2Debug(result);
            return result;
        }
        private const int MaxLogEntriesInView = 200;
        private float Misconduct(int p) { float sum = 0; foreach (IncidentState i in incidents) if (i.Reveal.Actor == p) sum += i.Reveal.Misconduct; return sum; }
        private float EvidenceWeight(EvidenceView e)
        {
            float weight = 0;
            if (e.Kind == EvidenceKind.SuspiciousItemFound) weight = Config.Risk.SuspiciousItem;
            else if (e.Kind == EvidenceKind.HiddenReportFound && e.DeskIndex >= 0 && Layout.GetSlot(e.LocationId)?.Kind == SlotKind.Drawer) weight = Config.Risk.HiddenReportInDrawer;
            else if (e.Kind == EvidenceKind.WitnessStatement) weight = Config.Risk.WitnessStatement;
            else if (e.Kind == EvidenceKind.EmailLog) weight = Config.Risk.EmailLog;
            if (!e.Sealed) { if (e.Status == EvidenceStatus.Disposed) return 0; if (e.Status == EvidenceStatus.Moved) weight *= Config.Risk.MovedEvidenceFactor; }
            return ProfileEvidenceWeight(weight);
        }
        private float RiskFor(int p)
        {
            float risk = 0; foreach (EvidenceState e in evidence) if ((e.View.Kind == EvidenceKind.WitnessStatement ? e.View.SubjectPlayer : e.View.AttributedTo) == p) risk += EvidenceWeight(e.View);
            foreach (ObservationView o in observations) if (o.Subject == p) risk += o.Severity;
            return Math.Max(0, risk);
        }
        private static SessionHistory CopyHistory(SessionHistory h)
        {
            if (h == null) return new SessionHistory();
            return Snapshot.Copy(h);
        }
        public SessionHistory BuildNextHistory() { SessionHistory next = CopyHistory(history); FillNextHistory(next); FillCareerHistory(next); return next; }
        partial void FillNextHistory(SessionHistory next);
        partial void FillStation(StationView station);
        partial void RefreshObjective(int p, ref ObjectiveView view);
    }
}
