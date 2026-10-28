using System;

namespace BirimizKovulacak.Core
{
    public sealed partial class MatchEngine
    {
        private void LinkPhysicalEvidence(EvidenceState record, ItemState item)
        {
            EvidenceView found = record.View;
            if (found.Kind != EvidenceKind.SuspiciousItemFound && found.Kind != EvidenceKind.HiddenReportFound) return;
            foreach (IncidentState incident in incidents)
            {
                IncidentReveal reveal = incident.Reveal;
                // A unique identity also distinguishes zero-duration placements at the same host time.
                // It survives pickup/recovery, then changes on the next placement or drop.
                bool placement = item != null && reveal.Id == item.PlacementIncident;
                bool framing = reveal.Type == SabotageType.NameplateSwap && reveal.DeskIndex == found.DeskIndex &&
                    Effective(incident) && found.AttributedTo == ApparentOwner(found.DeskIndex);
                if ((placement || framing) && !incident.EvidenceIds.Contains(found.Id)) incident.EvidenceIds.Add(found.Id);
            }
        }

        private HighlightView BuildHighlight(int firedPlayer)
        {
            if (incidents.Count == 0) return null;
            IncidentState selected = null;
            float bestRisk = 0;
            foreach (IncidentState incident in incidents)
            {
                float risk = 0;
                foreach (EvidenceState record in evidence)
                {
                    EvidenceView found = record.View;
                    int blamed = found.Kind == EvidenceKind.WitnessStatement ? found.SubjectPlayer : found.AttributedTo;
                    if (blamed == firedPlayer && incident.EvidenceIds.Contains(found.Id)) risk += Math.Max(0, EvidenceWeight(found));
                }
                if (risk > 0 && (selected == null || risk > bestRisk || (risk == bestRisk && EarlierIncident(incident, selected))))
                { selected = incident; bestRisk = risk; }
            }
            if (selected == null)
                foreach (IncidentState incident in incidents)
                    if (selected == null || incident.Reveal.Misconduct > selected.Reveal.Misconduct ||
                        (incident.Reveal.Misconduct == selected.Reveal.Misconduct && EarlierIncident(incident, selected))) selected = incident;

            var highlight = new HighlightView
            {
                Key = IncidentRecapKey(selected.Reveal.Type), IncidentId = selected.Reveal.Id,
                Actor = selected.Reveal.Actor, DeskIndex = selected.Reveal.DeskIndex
            };
            // A match always records its connected starting roster, but keep the empty-log case deterministic.
            if (positionLogCount == 0) { highlight.Start = highlight.End = footageMatchStart; return highlight; }
            double first = positionLog[positionLogStart].Time;
            double last = positionLog[(positionLogStart + positionLogCount - 1) % positionLog.Length].Time;
            highlight.Start = Math.Max(first, Math.Min(last, selected.Start - 6));
            highlight.End = Math.Max(first, Math.Min(last, selected.Reveal.Time + 4));
            for (int n = 0; n < positionLogCount; n++)
            {
                PositionSample sample = positionLog[(positionLogStart + n) % positionLog.Length];
                if (sample.Time < highlight.Start || sample.Time > highlight.End) continue;
                // The Results truth reveal intentionally includes dark samples and every camera zone.
                highlight.Samples.Add(new FootageSighting
                {
                    Player = sample.Player, Time = sample.Time, Activity = sample.Activity, Carrying = sample.Carrying,
                    Crouched = sample.Crouched, Position = sample.Position, Yaw = sample.Yaw
                });
            }
            return highlight;
        }

        private static bool EarlierIncident(IncidentState candidate, IncidentState selected)
        {
            return candidate.Reveal.Time < selected.Reveal.Time ||
                (candidate.Reveal.Time == selected.Reveal.Time && candidate.Reveal.Id < selected.Reveal.Id);
        }

        private static string IncidentRecapKey(SabotageType type)
        {
            switch (type)
            {
                case SabotageType.PlantItem: return "recap.planted";
                case SabotageType.NameplateSwap: return "recap.nameplate_swapped";
                case SabotageType.LockWorkstation: return "recap.workstation_locked";
                case SabotageType.HideReport: return "recap.report_hidden";
                case SabotageType.SpillCoffee: return "recap.coffee_spilled";
                case SabotageType.MoveClutter: return "recap.clutter_moved";
                case SabotageType.FakeEmail: return "recap.fake_email";
                case SabotageType.PullPlug: return "recap.pulled_plug";
                case SabotageType.SaltCoffee: return "recap.salted";
                case SabotageType.InternetOutage: return "recap.internet_down";
                case SabotageType.PowerCut: return "recap.power_down";
                default: return "recap.evidence_tampered";
            }
        }
    }
}
