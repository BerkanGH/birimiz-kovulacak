using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BirimizKovulacak.Core;

namespace BalanceSim;

/// <summary>
/// A post-match observation. These values are never available to SimBot decisions.
/// All rates must be computed from the numerator and denominator across matches,
/// rather than averaging per-match percentages with different denominators.
/// </summary>
public sealed class MatchMetrics
{
    public MatchResultView Result = new();
    public string FiredCategory = "";
    // Competition rank of final performance: 1 + number strictly better (1 = best).
    public int FiredPerformanceRank;
    public int FootageRequests;
    public int FootageWithIncident;
    public int FootageActorHits;
    public int Calls;
    public int AnsweredCalls;
    public int Tips;
    // One evaluation per tipped desk per inspection, not per caller. Multiple tips
    // on the same desk are coalesced by the engine into a single route visit.
    public int TipInspections;
    public int TipHits;
    public int RaidInspected;
    public int FinalInspected;
    public int SurpriseInspected;
    public bool HasSurprise;
    public int ProcessedRequests;
    public int RejectedRequests;
    public string[] UnderPressureFinalPenalties = [];
}

/// <summary>
/// Read-only experiment observer. No action submission and no data returned to policies.
/// The core has no public desk-inspection event or debug field. Its exact successful
/// inspection ledger is therefore read ONCE after Results through cached reflection.
/// This intentionally depends on the private field name and fails loudly if that
/// contract changes; it does not infer successful inspections from observations,
/// which would miss clean desks that produced no observation, or from sampled walks,
/// which could miss a visit crossing a tick boundary.
/// </summary>
public sealed class MatchMetricsCollector
{
    private static readonly FieldInfo InspectedDesksField =
        typeof(MatchEngine).GetField("inspectedDesks", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Core inspection ledger changed: inspectedDesks is absent.");

    private readonly MatchEngine _engine;
    private bool _hadSurprise;
    private bool _finished;

    public MatchMetricsCollector(MatchEngine engine) => _engine = engine;

    public void Observe(IReadOnlyList<GameEvent> events)
    {
        foreach (GameEvent e in events)
            if (e.Type == EventType.SurpriseInspection) _hadSurprise = true;
    }

    public MatchMetrics Finish(double now)
    {
        if (_finished) throw new InvalidOperationException("A match may only be collected once.");
        if (_engine.Phase != MatchPhase.Results || _engine.Result == null)
            throw new InvalidOperationException("Metrics require a completed match, not a partial or abandoned run.");
        _finished = true;

        // This is the sole observer BuildView: only public evidence/observations are
        // used, and hidden actor truth comes from the normal post-match reveal.
        PlayerView view = _engine.BuildView(_engine.Players[0], now);
        MatchResultView result = view.Result;
        HostDebugView debug = _engine.BuildHostDebugView();
        PlayerOutcome fired = result.Players.Single(p => p.Player == result.FiredPlayer);
        var metrics = new MatchMetrics
        {
            Result = result,
            FiredCategory = fired.TrueMisconduct <= 0 ? "zero" :
                result.TopMisconductTier.Contains(fired.Player) ? "top tier" : "other",
            FiredPerformanceRank = 1 + result.Players.Count(p => p.FinalPerformance > fired.FinalPerformance),
            Calls = result.Calls.Count(c => !c.Tip),
            AnsweredCalls = result.Calls.Count(c => !c.Tip && c.Answered),
            Tips = result.Calls.Count(c => c.Tip),
            TipInspections = view.Observations.Count(o => o.Kind == ObservationKind.TipFound || o.Kind == ObservationKind.TipClean),
            TipHits = view.Observations.Count(o => o.Kind == ObservationKind.TipFound),
            HasSurprise = _hadSurprise,
            ProcessedRequests = debug.ProcessedRequests,
            RejectedRequests = debug.RejectedRequests
        };
        var underPressure = result.Players.FirstOrDefault(p => p.ObjectiveType == ObjectiveType.UnderPressure);
        if (underPressure != null)
            metrics.UnderPressureFinalPenalties = view.Observations.Where(o => o.Subject == underPressure.Player &&
                o.Phase == MatchPhase.FinalInspection && (o.Severity > 0 || o.Kind == ObservationKind.SuspiciousItemAtDesk))
                .Select(o => o.Kind.ToString()).Distinct().Order().ToArray();

        var assignedDesks = view.Players.Select(p => p.DeskIndex).ToHashSet();
        if (InspectedDesksField.GetValue(_engine) is not IEnumerable<string> ledger)
            throw new InvalidOperationException("Core inspection ledger changed: expected a string sequence.");
        foreach (string entry in ledger.Distinct(StringComparer.Ordinal))
        {
            string[] pair = entry.Split(':');
            if (pair.Length != 2 || !int.TryParse(pair[0], out int phase) || !int.TryParse(pair[1], out int desk))
                throw new InvalidOperationException("Core inspection ledger format changed: " + entry);
            if (!assignedDesks.Contains(desk)) continue;
            switch ((MatchPhase)phase)
            {
                case MatchPhase.Raid: metrics.RaidInspected++; break;
                case MatchPhase.FinalInspection: metrics.FinalInspected++; break;
                case MatchPhase.Work2: metrics.SurpriseInspected++; metrics.HasSurprise = true; break;
                default: throw new InvalidOperationException("Unexpected inspection phase: " + phase);
            }
        }

        // A camera request "showed the true actor" only if an actual incident falls
        // inside its window and the actor appears within one sample interval of that
        // incident. Mere presence of any misbehaving player elsewhere in the window
        // does not qualify. It is visibility, not proof/attribution by the engine.
        double sampleInterval = _engine.Config.Footage.SampleInterval;
        double tolerance = (double.IsFinite(sampleInterval) && sampleInterval > 0 ? sampleInterval : 1) + 0.000001;
        foreach (EvidenceView footage in view.Evidence.Where(e => e.Kind == EvidenceKind.CctvFootage))
        {
            metrics.FootageRequests++;
            var incidents = result.Incidents.Where(i => i.Time >= footage.WindowStart && i.Time <= footage.WindowEnd).ToArray();
            if (incidents.Length > 0) metrics.FootageWithIncident++;
            if (!footage.NoRecording && incidents.Any(i => footage.Sightings.Any(s =>
                    s.Player == i.Actor && Math.Abs(s.Time - i.Time) <= tolerance)))
                metrics.FootageActorHits++;
        }
        return metrics;
    }
}
