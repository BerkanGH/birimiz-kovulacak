using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    public sealed partial class MatchEngine
    {
        partial void SubmitMeeting(int p, ActionRequest request, ref bool handled, ref FailureCode failure)
        {
            if (handled || request.Verb < ActionVerb.Accuse || request.Verb > ActionVerb.RematchVote) return;
            handled = true; failure = FailureCode.None;
            PrivateView me = players[p].Private;
            if (request.Verb == ActionVerb.Accuse)
            {
                if (me.HasAccused) { failure = FailureCode.AlreadyUsed; return; }
                if (request.IntParam == p) { failure = FailureCode.CannotTargetSelf; return; }
                if (!Present(request.IntParam)) { failure = FailureCode.InvalidTarget; return; }
                me.HasAccused = true; me.AccusedPlayer = request.IntParam;
                players[request.IntParam].Public.AccusationsReceived++;
                meeting.Accusations.Add(new AccusationView { Accuser = p, Accused = request.IntParam, Time = clock });
                Emit(EventType.AccusationFiled, p, arg: request.IntParam);
            }
            else if (request.Verb == ActionVerb.CastBallot)
            {
                if (me.HasVoted) { failure = FailureCode.AlreadyUsed; return; }
                if (request.IntParam == p) { failure = FailureCode.CannotTargetSelf; return; }
                if (request.IntParam != PlayerIds.None && !meeting.Shortlist.Contains(request.IntParam)) { failure = FailureCode.NotCandidate; return; }
                me.HasVoted = true; me.Ballot = request.IntParam;
                meeting.BallotsCast++; meeting.Voters.Add(p); Emit(EventType.BallotCast, p);
            }
            else if (request.Verb == ActionVerb.RematchVote)
            {
                if (me.RematchVoted) { failure = FailureCode.AlreadyUsed; return; }
                me.RematchVoted = true; Emit(EventType.RematchVote, p);
            }
            else
            {
                PrivateNote note = me.Notes.Find(n => n.Id == request.IntParam);
                if (note == null) { failure = FailureCode.InvalidTarget; return; }
                if (note.Presented) { failure = FailureCode.AlreadyUsed; return; }
                int presented = 0; foreach (PrivateNote n in me.Notes) if (n.Presented) presented++;
                if (presented >= 2) { failure = FailureCode.LimitReached; return; }
                EvidenceKind kind = note.Kind == NoteKind.Witness ? EvidenceKind.WitnessStatement : note.Kind == NoteKind.SearchResult ? EvidenceKind.SearchStatement : note.Kind == NoteKind.CallLog ? EvidenceKind.CallLogStatement : note.Kind == NoteKind.TasteResult ? EvidenceKind.TasteStatement : EvidenceKind.ScanStatement;
                EvidenceState record = AddEvidence(kind, null, note.TargetId, note.DeskIndex, DiscoverySource.Player, p, PlayerIds.None);
                record.View.SubjectPlayer = note.Subject; record.View.ObservedActivity = note.Activity;
                record.View.HeldBefore = note.HeldBefore; record.View.HeldAfter = note.HeldAfter;
                if (note.Kind == NoteKind.CallLog) foreach (CallRecordView call in note.Calls) record.View.Calls.Add(Snapshot.Copy(call));
                if (note.Kind == NoteKind.SearchResult) record.View.Contents = new List<string>(note.Contents);
                if (note.Kind == NoteKind.TasteResult) { record.View.ItemId = note.ItemId; record.View.Salted = note.Salted; }
                if (note.Kind == NoteKind.ScanResult && me.Informant != null)
                {
                    ScanResultView scan = me.Informant.Scans.Find(s => s.Id == note.ScanId);
                    if (scan != null) { record.View.AttributedTo = scan.ApparentOwner; record.View.ScanReliability = scan.Reliability; record.View.ScanSignal = scan.Signal; record.View.ItemVariant = scan.ItemVariant; }
                }
                record.View.RiskWeight = EvidenceWeight(record.View); note.Presented = true; meeting.PresentedEvidence.Add(record.View.Id);
                if (note.Kind == NoteKind.Witness)
                    foreach (IncidentState incident in incidents)
                        if (incident.WitnessNoteIds.Contains(note.Id)) incident.EvidenceIds.Add(record.View.Id);
            }
        }

        partial void MeetingPhaseChanged(MatchPhase previous)
        {
            meeting.Speaker = PlayerIds.None; meeting.SpeakerEnds = 0;
            if (Phase == MatchPhase.MeetingDefense1)
            {
                var ranking = new List<int>(playerIds); random.Shuffle(ranking);
                var draw = new List<int>(ranking);
                ranking.Sort((a, b) => {
                    int c = players[b].Public.AccusationsReceived.CompareTo(players[a].Public.AccusationsReceived);
                    if (c == 0) c = RiskFor(b).CompareTo(RiskFor(a));
                    if (c == 0) c = players[a].Public.Performance.CompareTo(players[b].Public.Performance);
                    return c != 0 ? c : draw.IndexOf(a).CompareTo(draw.IndexOf(b));
                });
                meeting.Shortlist.Add(ranking[0]); meeting.Shortlist.Add(ranking[1]);
            }
            if (Phase == MatchPhase.MeetingDefense1 || Phase == MatchPhase.MeetingDefense2)
            {
                meeting.Speaker = meeting.Shortlist[Phase == MatchPhase.MeetingDefense1 ? 0 : 1];
                meeting.SpeakerEnds = phaseEnd; Emit(EventType.SpeakerChanged, arg: meeting.Speaker);
            }
            if (Phase == MatchPhase.MeetingVerdict) { BuildVerdict(); Emit(EventType.VerdictReady); }
            if (Phase == MatchPhase.Results) { BuildResults(); Emit(EventType.ResultsReady); }
        }

        private void BuildVerdict()
        {
            var verdict = new VerdictView { VoteWeight = Config.Verdict.VoteWeight, RiskWeight = Config.Verdict.RiskWeight, PerformanceWeight = Config.Verdict.PerformanceWeight };
            float highestRisk = Config.Verdict.RiskScale, bestPerformance = 0;
            foreach (int p in playerIds) { highestRisk = Math.Max(highestRisk, RiskFor(p)); bestPerformance = Math.Max(bestPerformance, players[p].Public.Performance); }
            foreach (int p in meeting.Shortlist)
            {
                var score = new CandidateScore { Player = p, Accusations = players[p].Public.AccusationsReceived, ObservedRisk = RiskFor(p), Performance = players[p].Public.Performance };
                foreach (int voter in playerIds) if (players[voter].Private.HasVoted && players[voter].Private.Ballot == p) score.Votes++;
                score.VoteShare = meeting.BallotsCast == 0 ? 0 : (float)score.Votes / meeting.BallotsCast;
                score.RiskNormalized = highestRisk <= 0 ? 0 : score.ObservedRisk / highestRisk;
                float performanceScale = Math.Max(Config.Verdict.PerformanceScale, bestPerformance);
                score.PerformanceDeficit = performanceScale <= 0 ? 0 : (bestPerformance - score.Performance) / performanceScale;
                score.Score = score.VoteShare * verdict.VoteWeight + score.RiskNormalized * verdict.RiskWeight + score.PerformanceDeficit * verdict.PerformanceWeight;
                score.Favor = players[p].Public.Favor;
                score.FavorDiscount = score.Favor > 0 && score.Favor == HighestFavor() ? Config.Verdict.FavorWeight : 0;
                score.Score -= score.FavorDiscount;
                verdict.Candidates.Add(score);
            }
            CandidateScore first = verdict.Candidates[0], second = verdict.Candidates[1], fired;
            if (first.Score != second.Score) fired = first.Score > second.Score ? first : second;
            else if (first.ObservedRisk != second.ObservedRisk) { fired = first.ObservedRisk > second.ObservedRisk ? first : second; verdict.TieBreakUsed = TieBreak.ObservedRisk; }
            else if (first.Performance != second.Performance) { fired = first.Performance < second.Performance ? first : second; verdict.TieBreakUsed = TieBreak.Performance; }
            else { fired = random.Index(2) == 0 ? first : second; verdict.TieBreakUsed = TieBreak.SeededDraw; }
            verdict.FiredPlayer = fired.Player;
            FinalizeEmailConsequencesV2(fired.Player);
            var contributingEvidence = evidence.FindAll(e => (e.View.Kind == EvidenceKind.WitnessStatement ? e.View.SubjectPlayer : e.View.AttributedTo) == fired.Player && EvidenceWeight(e.View) > 0);
            contributingEvidence.Sort((a, b) => { int c = EvidenceWeight(b.View).CompareTo(EvidenceWeight(a.View)); return c == 0 ? a.View.Id.CompareTo(b.View.Id) : c; });
            for (int n = 0; n < Math.Min(3, contributingEvidence.Count); n++) verdict.KeyEvidence.Add(contributingEvidence[n].View.Id);
            var contributingObservations = observations.FindAll(o => o.Subject == fired.Player && o.Severity > 0);
            contributingObservations.Sort((a, b) => { int c = b.Severity.CompareTo(a.Severity); return c == 0 ? a.Id.CompareTo(b.Id) : c; });
            for (int n = 0; n < Math.Min(3, contributingObservations.Count); n++) verdict.KeyObservations.Add(contributingObservations[n].Id);
            meeting.Verdict = verdict;
        }

        private void BuildResults()
        {
            var result = new MatchResultView { CompanySucceeded = quota.Points >= quota.Target, QuotaPoints = quota.Points, QuotaTarget = quota.Target, FiredPlayer = meeting.Verdict.FiredPlayer, Verdict = Snapshot.Copy(meeting.Verdict), MatchSeed = Seed };
            float highestMisconduct = 0;
            foreach (int p in playerIds) highestMisconduct = Math.Max(highestMisconduct, Misconduct(p));
            foreach (int p in playerIds)
            {
                if (highestMisconduct > 0 && Misconduct(p) == highestMisconduct) result.TopMisconductTier.Add(p);
                if (players[p].Private.Role == Role.Informant) result.InformantPlayer = p;
                if (players[p].Private.HasAccused && Misconduct(players[p].Private.AccusedPlayer) == 0)
                { players[p].FalseAccusationPenalty = Config.Verdict.FalseAccusationPenalty; players[p].Public.Performance -= players[p].FalseAccusationPenalty; }
            }
            result.InformantWon = result.CompanySucceeded && result.InformantPlayer != result.FiredPlayer && result.TopMisconductTier.Contains(result.FiredPlayer);
            foreach (int p in playerIds)
            {
                PlayerState player = players[p]; bool informant = player.Private.Role == Role.Informant;
                bool completed = informant ? result.TopMisconductTier.Contains(result.FiredPlayer) : ObjectiveSatisfied(p, result.FiredPlayer);
                bool won = result.CompanySucceeded && p != result.FiredPlayer && completed;
                int rank = 1; foreach (int other in playerIds) if (Misconduct(other) > Misconduct(p)) rank++;
                string explanation = !result.CompanySucceeded ? "outcome.company_failed" : p == result.FiredPlayer ? "outcome.fired" : informant ? (highestMisconduct <= 0 ? "outcome.no_misconduct" : won ? "outcome.informant_won" : "outcome.informant_wrong_target") : won ? "outcome.won" : "outcome.objective_failed";
                var outcome = new PlayerOutcome { Player = p, Name = player.Public.Name, Role = player.Private.Role, ObjectiveCardId = player.Objective.Id, ObjectiveType = player.Objective.Type, ObjectiveTarget = player.ObjectiveTarget, ObjectiveCompleted = completed, Fired = p == result.FiredPlayer, Won = won, ExplanationKey = explanation, TrueMisconduct = Misconduct(p), MisconductRank = rank, FinalPerformance = player.Public.Performance, FalseAccusationPenalty = player.FalseAccusationPenalty, TasksCompleted = player.Public.TasksCompleted, SabotageCount = SabotageCount(p) };
                AssignTitles(outcome, result); result.Players.Add(outcome);
            }
            foreach (IncidentState incident in incidents) result.Incidents.Add(Snapshot.Copy(incident.Reveal));
            BuildRecap(result);
            FillProfileResult(result);
            FillCareerResult(result);
            foreach (PlayerOutcome outcome in result.Players) AddV2Titles(outcome, result);
            FillCalls(result.Calls);
            result.Highlight = BuildHighlight(result.FiredPlayer);
            result.ResultHash = WireCodec.HashResult(result); Result = result;
        }

        private void AssignTitles(PlayerOutcome outcome, MatchResultView result)
        {
            int p = outcome.Player;
            if (outcome.Role == Role.Informant && result.InformantWon) outcome.Titles.Add("title.informant_of_month");
            int mostCoffee = 0; foreach (int i in playerIds) mostCoffee = Math.Max(mostCoffee, players[i].TaskCounts[(int)TaskType.Coffee]);
            if (mostCoffee > 0 && players[p].TaskCounts[(int)TaskType.Coffee] == mostCoffee) outcome.Titles.Add("title.coffee_master");
            if (outcome.Titles.Count < 2 && outcome.TrueMisconduct > 0 && result.TopMisconductTier.Contains(p)) outcome.Titles.Add("title.chaos_agent");
            if (outcome.Titles.Count < 2 && incidents.Exists(i => i.Reveal.Actor == p && (i.Reveal.Type == SabotageType.HideReport || i.Reveal.Type == SabotageType.LockWorkstation))) outcome.Titles.Add("title.printer_enemy");
            if (outcome.Titles.Count < 2 && incidents.Exists(i => i.Reveal.Actor != p && i.BlamedPlayers.Contains(p))) outcome.Titles.Add("title.pro_scapegoat");
            DeskState desk = Desk(players[p].Public.DeskIndex);
            if (outcome.Titles.Count < 2 && !desk.View.Dirty && !desk.View.Spill && desk.View.ClutterCount == 0) outcome.Titles.Add("title.cleanest_desk");
        }

        private void BuildRecap(MatchResultView result)
        {
            var beats = new List<RecapBeat>();
            foreach (IncidentState incident in incidents)
            {
                IncidentReveal i = incident.Reveal; ItemState item = Item(i.ItemId);
                if (i.Type >= SabotageType.FakeEmail) continue;
                beats.Add(new RecapBeat { Time = i.Time, Key = IncidentRecapKey(i.Type), Actor = i.Actor, Other = i.Victim, DeskIndex = i.DeskIndex, ItemVariant = item == null ? "" : item.View.Variant });
            }
            foreach (EvidenceState e in evidence) if (e.View.Source == DiscoverySource.Manager) beats.Add(new RecapBeat { Time = e.View.Time, Key = "recap.manager_found", Other = e.View.AttributedTo, DeskIndex = e.View.DeskIndex, ItemVariant = e.View.ItemVariant });
            foreach (AccusationView a in meeting.Accusations) beats.Add(new RecapBeat { Time = a.Time, Key = "recap.accused", Actor = a.Accuser, Other = a.Accused });
            AddV2Recap(beats);
            // Stable insertion sort preserves deterministic creation order for simultaneous beats.
            for (int i = 1; i < beats.Count; i++) { RecapBeat value = beats[i]; int j = i - 1; while (j >= 0 && beats[j].Time > value.Time) { beats[j + 1] = beats[j]; j--; } beats[j + 1] = value; }
            for (int i = 0; i < Math.Min(9, beats.Count); i++) result.Recap.Add(beats[i]);
            result.Recap.Add(new RecapBeat { Time = phaseStart - Config.Durations.MeetingVerdict, Key = "recap.fired", Other = result.FiredPlayer });
        }
    }
}
