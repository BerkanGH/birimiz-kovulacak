using System;
using System.Collections.Generic;
using System.Globalization;

namespace BirimizKovulacak.Core
{
    public sealed partial class MatchEngine
    {
        private GameMode mode;
        private int weekDay;
        private readonly FeaturesView features = new FeaturesView();
        private ManagerProfileDef managerProfile;
        private int favourite = PlayerIds.None;

        private string ManagerProfileId { get { return managerProfile == null ? "" : managerProfile.Id; } }

        private void InitializeProfiles(MatchSetup setup)
        {
            mode = setup.Mode;
            weekDay = mode == GameMode.Week ? setup.WeekDay : 0;
            WeekDayDef day = null;
            if (mode == GameMode.Week)
            {
                if (weekDay < 0 || weekDay >= Config.WeekDays.Count)
                    throw new ArgumentException("Week day must refer to a configured day.", nameof(setup));
                day = Config.WeekDays[weekDay];
            }
            features.Cards = Config.Cards.Enabled && (day == null || day.Cards);
            features.Calls = Config.Calls.Enabled && (day == null || day.Calls);
            features.TipLine = Config.Calls.TipLineEnabled && (day == null || day.TipLine);
            features.Footage = Config.Footage.Enabled && (day == null || day.Footage);
            features.Mail = Config.Tasks.MailEnabled && (day == null || day.Mail);
            features.CareerMultiplier = day == null ? 1f : day.CareerMultiplier;

            if (Config.ManagerProfiles.Count == 0) return;
            string requested = setup.ManagerProfileId;
            if (string.IsNullOrEmpty(requested) && day != null) requested = day.ProfileId;
            if (!string.IsNullOrEmpty(requested))
            {
                foreach (ManagerProfileDef profile in Config.ManagerProfiles)
                    if (profile.Id == requested) { managerProfile = profile; break; }
                if (managerProfile == null) throw new ArgumentException("Unknown manager profile: " + requested, nameof(setup));
            }
            else
            {
                string previous = history.PastManagerProfiles.Count == 0 ? "" : history.PastManagerProfiles[history.PastManagerProfiles.Count - 1];
                var choices = new List<ManagerProfileDef>();
                foreach (ManagerProfileDef profile in Config.ManagerProfiles)
                    if (Config.ManagerProfiles.Count == 1 || profile.Id != previous) choices.Add(profile);
                if (choices.Count == 0) choices.AddRange(Config.ManagerProfiles);
                managerProfile = choices[random.Index(choices.Count)];
            }
            Config.QuotaTarget += managerProfile.QuotaDelta;
            if (managerProfile.VoteWeight >= 0) Config.Verdict.VoteWeight = managerProfile.VoteWeight;
            if (managerProfile.RiskWeight >= 0) Config.Verdict.RiskWeight = managerProfile.RiskWeight;
            if (managerProfile.PerformanceWeight >= 0) Config.Verdict.PerformanceWeight = managerProfile.PerformanceWeight;
            if (managerProfile.FavorWeight >= 0) Config.Verdict.FavorWeight = managerProfile.FavorWeight;
            if (managerProfile.VisionRange >= 0) Config.Manager.VisionRange = managerProfile.VisionRange;
            if (managerProfile.VisionHalfAngle >= 0) Config.Manager.VisionHalfAngle = managerProfile.VisionHalfAngle;
            if (managerProfile.HasFavourite)
            {
                favourite = Present(setup.ForceFavourite) ? setup.ForceFavourite : playerIds[random.Index(playerIds.Count)];
                players[favourite].Public.Favor = 1f;
                Emit(EventType.FavorChanged, favourite, text: "1");
            }
        }

        private float ObservationSeverity(ObservationKind kind, float severity)
        {
            if (managerProfile == null) return severity;
            if (kind == ObservationKind.InappropriateEmail) severity *= managerProfile.EmailFactor;
            foreach (ObservationFactor factor in managerProfile.ObservationFactors)
                if (factor.Kind == kind) return severity * factor.Factor;
            return severity;
        }

        private float ProfileEvidenceWeight(float weight) { return managerProfile == null ? weight : weight * managerProfile.EvidenceFactor; }

        private void AwardFavor(int player, TaskType task)
        {
            if (managerProfile == null) return;
            FavorSource source = managerProfile.FavorSource;
            if (source != FavorSource.AnyTask && !(source == FavorSource.ReportsDelivered && task == TaskType.Report) && !(source == FavorSource.CoffeeDelivered && task == TaskType.Coffee)) return;
            players[player].Public.Favor = Math.Max(0, players[player].Public.Favor + managerProfile.FavorPerAction);
            Emit(EventType.FavorChanged, player, text: players[player].Public.Favor.ToString(CultureInfo.InvariantCulture));
        }

        private float HighestFavor()
        {
            float highest = 0;
            foreach (int p in playerIds) highest = Math.Max(highest, players[p].Public.Favor);
            return highest;
        }

        private void FillProfileView(PlayerView view)
        {
            view.Mode = mode; view.WeekDay = weekDay; view.ManagerProfileId = ManagerProfileId;
            view.Favourite = favourite; view.Features = Snapshot.Copy(features);
        }

        private void FillProfileResult(MatchResultView result)
        {
            result.Mode = mode; result.WeekDay = weekDay; result.ManagerProfileId = ManagerProfileId;
            float highest = HighestFavor();
            foreach (int p in playerIds) if (highest > 0 && players[p].Public.Favor == highest) result.Favourites.Add(p);
            foreach (PlayerOutcome outcome in result.Players) outcome.Favor = players[outcome.Player].Public.Favor;
        }
    }
}
