using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    public sealed partial class MatchEngine
    {
        private void InitializeCareer()
        {
            foreach (int p in playerIds)
                players[p].Public.CareerPoints = mode == GameMode.Week && p < history.CareerPoints.Count ? history.CareerPoints[p] : 0;
        }

        private void FillCareerResult(MatchResultView result)
        {
            result.WeekFinished = mode == GameMode.Week && weekDay == Config.WeekDays.Count - 1;
            int best = int.MinValue;
            foreach (PlayerOutcome outcome in result.Players)
            {
                outcome.Cards = new List<SabotageType>(players[outcome.Player].Private.Cards);
                outcome.Favor = players[outcome.Player].Public.Favor;
                if (mode != GameMode.Week) continue;
                CareerConfig c = Config.Career;
                double earned = features.CareerMultiplier * (result.CompanySucceeded && !outcome.Fired ? c.QuotaMet : 0)
                    + (!outcome.Fired ? c.Survived : 0)
                    + (outcome.Role == Role.Employee && outcome.Won ? c.ObjectiveWon : 0)
                    + (outcome.Role == Role.Informant && result.InformantWon ? c.InformantWon : 0)
                    + (outcome.Fired ? c.Fired : 0)
                    + (result.Favourites.Contains(outcome.Player) ? c.Favourite : 0);
                outcome.CareerDelta = (int)Math.Round(earned, MidpointRounding.AwayFromZero);
                outcome.CareerTotal = players[outcome.Player].Public.CareerPoints + outcome.CareerDelta;
                best = Math.Max(best, outcome.CareerTotal);
            }
            if (result.WeekFinished)
                foreach (PlayerOutcome outcome in result.Players)
                    if (outcome.CareerTotal == best) result.EmployeeOfTheMonth.Add(outcome.Player);
        }

        private void FillCareerHistory(SessionHistory next)
        {
            if (Phase != MatchPhase.Results || Result == null) return;
            next.PastManagerProfiles.Add(ManagerProfileId);
            if (mode != GameMode.Week) return;
            while (next.CareerPoints.Count < Protocol.MaxPlayers) next.CareerPoints.Add(0);
            foreach (PlayerOutcome outcome in Result.Players) next.CareerPoints[outcome.Player] = outcome.CareerTotal;
            next.NextWeekDay = weekDay + 1;
            if (!Result.WeekFinished) return;
            next.NextWeekDay = 0; next.WeekNumber++;
            for (int i = 0; i < next.CareerPoints.Count; i++) next.CareerPoints[i] = 0;
        }

        private void AddV2Titles(PlayerOutcome outcome, MatchResultView result)
        {
            var titles = new List<string>();
            int placed = 0, most = 0;
            foreach (int p in playerIds)
            {
                int count = 0;
                foreach (CallRecordView call in calls) if (!call.Tip && call.Caller == p) count++;
                if (p == outcome.Player) placed = count;
                most = Math.Max(most, count);
            }
            if (placed >= 3 && placed == most) titles.Add("title.scam_artist");
            if (incidents.Exists(i => i.Reveal.Actor == outcome.Player && i.Reveal.Type == SabotageType.SaltCoffee &&
                i.Reveal.Victim != PlayerIds.None && i.Reveal.Victim != outcome.Player && i.ConsequenceApplied)) titles.Add("title.salt_bae");
            if (log.Exists(e => e.Actor == outcome.Player &&
                (e.Kind == EventType.InternetRestored || e.Kind == EventType.PowerRestored))) titles.Add("title.it_hero");
            if (result.Favourites.Contains(outcome.Player)) titles.Add("title.favourite");
            titles.AddRange(outcome.Titles); outcome.Titles.Clear();
            for (int i = 0; i < Math.Min(2, titles.Count); i++) outcome.Titles.Add(titles[i]);
        }

        private void AddV2Recap(List<RecapBeat> beats)
        {
            foreach (RecapBeat beat in sabotageRecap) beats.Add(Snapshot.Copy(beat));
            foreach (CallRecordView call in calls)
                beats.Add(new RecapBeat { Time = call.Start, Key = call.Tip ? "recap.tip" : "recap.call",
                    Actor = call.Caller, Other = call.Callee, DeskIndex = call.TipDesk });
            foreach (ObservationView observation in observations)
                if (observation.Kind == ObservationKind.TipClean)
                    beats.Add(new RecapBeat { Time = observation.Time, Key = "recap.tip_clean", Other = observation.Subject, DeskIndex = observation.DeskIndex });
        }
    }
}
