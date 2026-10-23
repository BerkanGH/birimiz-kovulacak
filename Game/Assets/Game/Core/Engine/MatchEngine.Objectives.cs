using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    public sealed partial class MatchEngine
    {
        partial void AssignRoles(MatchSetup setup)
        {
            var eligibleInformants = new List<int> { 0, 1, 2, 3 };
            if (Config.Informant.AvoidRepeatInformant && history.PastInformants.Count > 0) eligibleInformants.Remove(history.PastInformants[history.PastInformants.Count - 1]);
            int informant = PlayerIds.IsValid(setup.ForceInformant) ? setup.ForceInformant : eligibleInformants[random.Index(eligibleInformants.Count)];
            var available = new List<ObjectiveDef>();
            foreach (ObjectiveDef card in Config.Objectives) if (card.Enabled && card.Type != ObjectiveType.InformantGoal) available.Add(card);
            // Reserve forced cards before assigning any random card so later slots keep their requested test card.
            var forced = new ObjectiveDef[4];
            for (int p = 0; p < 4; p++)
            {
                if (p == informant || setup.ForceObjectives == null || setup.ForceObjectives.Count <= p || string.IsNullOrEmpty(setup.ForceObjectives[p])) continue;
                string cardId = setup.ForceObjectives[p]; forced[p] = Config.Objectives.Find(c => c.Id == cardId);
                if (forced[p] == null || forced[p].Type == ObjectiveType.InformantGoal) throw new ArgumentException("Unknown employee objective: " + cardId, nameof(setup));
                available.RemoveAll(c => c.Id == cardId);
            }
            var previousCards = new List<string>();
            for (int i = Math.Max(0, history.PastCards.Count - 3); i < history.PastCards.Count; i++) previousCards.Add(history.PastCards[i]);
            for (int p = 0; p < 4; p++)
            {
                PlayerState player = players[p];
                if (p == informant)
                {
                    player.Private.Role = Role.Informant;
                    player.Private.Informant = new InformantView { ScansLeft = Math.Max(0, Config.Informant.AuditScans), SealsLeft = Math.Max(0, Config.Informant.SealedReports) };
                    player.Objective = new ObjectiveDef { Id = "INFORMANT_GOAL", Type = ObjectiveType.InformantGoal };
                }
                else
                {
                    player.Private.Role = Role.Employee;
                    ObjectiveDef card = forced[p];
                    if (card == null)
                    {
                        if (available.Count == 0) throw new ArgumentException("At least three distinct employee objectives are required.", nameof(Config));
                        List<ObjectiveDef> fresh = available.FindAll(c => !previousCards.Contains(c.Id));
                        List<ObjectiveDef> pool = fresh.Count > 0 ? fresh : available;
                        card = pool[random.Index(pool.Count)]; available.RemoveAll(c => c.Id == card.Id);
                    }
                    player.Objective = card;
                    if (card.RequiresTarget)
                    {
                        int forcedTarget = setup.ForceTargets != null && setup.ForceTargets.Count > p ? setup.ForceTargets[p] : PlayerIds.None;
                        player.ObjectiveTarget = PlayerIds.IsValid(forcedTarget) && forcedTarget != p ? forcedTarget : ChooseObjectiveTarget(p);
                    }
                }
                player.Private.Objective = new ObjectiveView { CardId = player.Objective.Id, Type = player.Objective.Type, Target = player.ObjectiveTarget, Required = player.Objective.Count, Required2 = player.Objective.Count2 };
            }
        }

        private int ChooseObjectiveTarget(int holder)
        {
            var candidates = new List<int>();
            var recentPairs = new List<string>();
            // At most three pairs can come from the previous match. Including an older pair when
            // that match had fewer targeted cards further reduces repetition without losing any recent pair.
            for (int i = Math.Max(0, history.PastTargetPairs.Count - 3); i < history.PastTargetPairs.Count; i++) recentPairs.Add(history.PastTargetPairs[i]);
            for (int target = 0; target < 4; target++) if (target != holder && !recentPairs.Contains(holder + ">" + target)) candidates.Add(target);
            if (candidates.Count == 0) for (int target = 0; target < 4; target++) if (target != holder) candidates.Add(target);
            int largest = 0;
            foreach (int target in candidates) if (target < history.TimesTargeted.Count) largest = Math.Max(largest, history.TimesTargeted[target]);
            long sum = 0;
            foreach (int target in candidates) sum += 1L + largest - (target < history.TimesTargeted.Count ? history.TimesTargeted[target] : 0);
            double draw = random.Between(0, sum);
            foreach (int target in candidates)
            {
                draw -= 1L + largest - (target < history.TimesTargeted.Count ? history.TimesTargeted[target] : 0);
                if (draw < 0) return target;
            }
            return candidates[candidates.Count - 1];
        }

        partial void FillNextHistory(SessionHistory next)
        {
            next.MatchesPlayed++;
            while (next.TimesTargeted.Count < 4) next.TimesTargeted.Add(0);
            for (int p = 0; p < 4; p++)
            {
                PlayerState player = players[p];
                if (player.Private.Role == Role.Informant) { next.PastInformants.Add(p); continue; }
                next.PastCards.Add(player.Objective.Id);
                if (PlayerIds.IsValid(player.ObjectiveTarget)) { next.PastTargetPairs.Add(p + ">" + player.ObjectiveTarget); next.TimesTargeted[player.ObjectiveTarget]++; }
            }
        }

        private int SabotageCount(int p)
        {
            int count = 0; foreach (IncidentState incident in incidents) if (incident.Reveal.Actor == p) count++; return count;
        }

        private int TaskTypesCompleted(int p)
        {
            int count = 0; foreach (int done in players[p].TaskCounts) if (done > 0) count++; return count;
        }

        private int ScapegoatProgress(int p)
        {
            int count = 0; foreach (IncidentState incident in incidents) if (incident.Reveal.Actor == p && incident.Reveal.Discovered && incident.BlamedPlayers.Contains(players[p].ObjectiveTarget)) count++; return count;
        }

        private int ChaosProgress(int p)
        {
            var foundDesks = new List<int>();
            foreach (IncidentState incident in incidents) if (incident.Reveal.Actor == p && incident.Reveal.Type == SabotageType.PlantItem && incident.Reveal.Discovered && !foundDesks.Contains(incident.Reveal.DeskIndex)) foundDesks.Add(incident.Reveal.DeskIndex);
            return foundDesks.Count;
        }

        private int FixerProgress(int p)
        {
            int count = 0; foreach (IncidentState incident in incidents) if (incident.Reveal.Actor != p && incident.Reveal.ReversedBy == p) count++; return count;
        }

        private bool GuardianClean(int target, MatchPhase phase)
        {
            if (!PlayerIds.IsValid(target)) return false;
            int desk = players[target].Public.DeskIndex;
            return !observations.Exists(o => o.Kind == ObservationKind.SuspiciousItemAtDesk && o.DeskIndex == desk && o.Phase == phase);
        }

        private bool FinalPenalty(int p) { return observations.Exists(o => o.Subject == p && o.Phase == MatchPhase.FinalInspection && o.Severity > 0); }

        private bool ObjectiveSatisfied(int p, int fired)
        {
            PlayerState player = players[p]; ObjectiveDef card = player.Objective; int target = player.ObjectiveTarget;
            switch (card.Type)
            {
                case ObjectiveType.DismissalPlan: return fired != PlayerIds.None && target == fired;
                case ObjectiveType.ProtectionDeal: return fired != PlayerIds.None && target != fired;
                case ObjectiveType.Scapegoat: return ScapegoatProgress(p) >= card.Count;
                case ObjectiveType.QuietStar:
                    if (player.Private.HasAccused) return false;
                    for (int other = 0; other < 4; other++) if (players[other].Public.Performance > player.Public.Performance) return false;
                    return true;
                case ObjectiveType.ChaosCoordinator: return ChaosProgress(p) >= card.Count;
                case ObjectiveType.CleanHands: return player.Public.TasksCompleted >= card.Count && TaskTypesCompleted(p) >= card.Count2 && SabotageCount(p) == 0;
                case ObjectiveType.OfficeAllRounder: return TaskTypesCompleted(p) == 4;
                case ObjectiveType.Fixer: return FixerProgress(p) >= card.Count;
                case ObjectiveType.DeskGuardian: return GuardianClean(target, MatchPhase.Raid) && GuardianClean(target, MatchPhase.FinalInspection);
                case ObjectiveType.Unnoticed: return player.Public.AccusationsReceived == 0;
                case ObjectiveType.PerformanceRival: return PlayerIds.IsValid(target) && player.Public.Performance > players[target].Public.Performance;
                case ObjectiveType.UnderPressure: return player.Work2Tasks >= card.Count && !FinalPenalty(p);
                default:
                    if (fired == PlayerIds.None || Misconduct(fired) <= 0) return false;
                    for (int other = 0; other < 4; other++) if (Misconduct(other) > Misconduct(fired)) return false;
                    return true;
            }
        }

        partial void RefreshObjective(int p, ref ObjectiveView view)
        {
            PlayerState player = players[p]; ObjectiveDef card = player.Objective;
            if (card == null) return;
            view = new ObjectiveView { CardId = card.Id, Type = card.Type, Target = player.ObjectiveTarget, Required = card.Count, Required2 = card.Count2 };
            switch (card.Type)
            {
                case ObjectiveType.Scapegoat: view.Progress = ScapegoatProgress(p); break;
                case ObjectiveType.ChaosCoordinator: view.Progress = ChaosProgress(p); break;
                case ObjectiveType.CleanHands:
                    view.Progress = player.Public.TasksCompleted; view.Progress2 = TaskTypesCompleted(p);
                    view.Flags.Add(SabotageCount(p) == 0 ? "clean" : "sabotaged"); break;
                case ObjectiveType.OfficeAllRounder:
                    view.Progress = TaskTypesCompleted(p);
                    string[] flags = { "coffee", "report", "phone", "clean_desk" };
                    for (int type = 0; type < 4; type++) if (player.TaskCounts[type] > 0) view.Flags.Add(flags[type]);
                    break;
                case ObjectiveType.Fixer: view.Progress = FixerProgress(p); break;
                case ObjectiveType.QuietStar:
                    view.Progress = 1; for (int other = 0; other < 4; other++) if (players[other].Public.Performance > player.Public.Performance) view.Progress++;
                    if (player.Private.HasAccused) view.Flags.Add("accused"); break;
                case ObjectiveType.PerformanceRival:
                    view.Progress = (int)player.Public.Performance;
                    view.Progress2 = PlayerIds.IsValid(player.ObjectiveTarget) ? (int)players[player.ObjectiveTarget].Public.Performance : 0; break;
                case ObjectiveType.DeskGuardian:
                    int desk = PlayerIds.IsValid(player.ObjectiveTarget) ? players[player.ObjectiveTarget].Public.DeskIndex : -1;
                    view.Flags.Add("raid:" + (inspectedDesks.Contains(((int)MatchPhase.Raid) + ":" + desk) ? GuardianClean(player.ObjectiveTarget, MatchPhase.Raid) ? "clean" : "found" : "pending"));
                    view.Flags.Add("final:" + (inspectedDesks.Contains(((int)MatchPhase.FinalInspection) + ":" + desk) ? GuardianClean(player.ObjectiveTarget, MatchPhase.FinalInspection) ? "clean" : "found" : "pending")); break;
                case ObjectiveType.Unnoticed: if (player.Public.AccusationsReceived > 0) view.Flags.Add("accused"); break;
                case ObjectiveType.UnderPressure: view.Progress = player.Work2Tasks; if (FinalPenalty(p)) view.Flags.Add("penalty"); break;
            }
            int fired = meeting.Verdict == null ? PlayerIds.None : meeting.Verdict.FiredPlayer;
            if (Phase == MatchPhase.Results) view.Status = ObjectiveSatisfied(p, fired) ? ObjectiveStatus.Completed : ObjectiveStatus.Failed;
            else if (card.Type == ObjectiveType.DismissalPlan || card.Type == ObjectiveType.ProtectionDeal || card.Type == ObjectiveType.InformantGoal || (card.Type == ObjectiveType.Unnoticed && Phase >= MatchPhase.MeetingDiscussion)) view.Status = ObjectiveStatus.PendingVerdict;
            else view.Status = ObjectiveSatisfied(p, PlayerIds.None) ? ObjectiveStatus.OnTrack : ObjectiveStatus.InProgress;
        }
    }
}
