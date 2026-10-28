using BirimizKovulacak.Core;

namespace BirimizKovulacak.Game
{
    /// <summary>Small read-only helpers over the local view / config used by several screens.</summary>
    public static class GameInfo
    {
        public static MatchConfig Config => ClientState.I != null ? ClientState.I.Config : null;

        public static ManagerProfileDef Profile(string id)
        {
            var c = Config;
            if (c == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < c.ManagerProfiles.Count; i++) if (c.ManagerProfiles[i].Id == id) return c.ManagerProfiles[i];
            return null;
        }

        /// <summary>True when favour does anything this match (profile has a favour source or a public favourite).</summary>
        public static bool FavorMatters(PlayerView v)
        {
            if (v == null) return false;
            var p = Profile(v.ManagerProfileId);
            return p != null && (p.FavorSource != FavorSource.None || p.HasFavourite);
        }

        public static bool CallPhase(MatchPhase p) { return p >= MatchPhase.Briefing && p <= MatchPhase.FinalInspection; }
        public static bool TipPhase(MatchPhase p) { return p >= MatchPhase.Work1 && p <= MatchPhase.FinalInspection; }
        public static bool OfficePhase(MatchPhase p) { return p >= MatchPhase.Briefing && p <= MatchPhase.FinalInspection; }
        public static bool MeetingPhase(MatchPhase p) { return p >= MatchPhase.MeetingDiscussion && p <= MatchPhase.MeetingVerdict; }
        public static bool PresentPhase(MatchPhase p) { return p == MatchPhase.MeetingDiscussion || p == MatchPhase.MeetingDefense1 || p == MatchPhase.MeetingDefense2; }

        public static PlayerPublicView Player(PlayerView v, int id)
        {
            if (v == null) return null;
            for (int i = 0; i < v.Players.Count; i++) if (v.Players[i].Id == id) return v.Players[i];
            return null;
        }

        public static DeskView Desk(PlayerView v, int index)
        {
            if (v == null) return null;
            for (int i = 0; i < v.Desks.Count; i++) if (v.Desks[i].Index == index) return v.Desks[i];
            return null;
        }

        public static int MyDesk(PlayerView v)
        {
            var me = Player(v, ClientState.I != null ? ClientState.I.MySlot : PlayerIds.None);
            return me != null ? me.DeskIndex : -1;
        }

        public static double Cooldown(PlayerView v, SabotageType t)
        {
            int i = (int)t;
            return v != null && v.Me != null && i < v.Me.SabotageCooldowns.Count ? v.Me.SabotageCooldowns[i] : 0;
        }

        /// <summary>Match seconds available for footage requests: [0, meeting start - match start].</summary>
        public static int FootageMaxSecond(PlayerView v)
        {
            double end = MatchClock.MeetingStart > 0 && MatchClock.MeetingMatchId == (v != null ? v.MatchId : -1) ? MatchClock.MeetingStart : (v != null ? v.PhaseStartTime : 0);
            int s = (int)(end - MatchClock.MatchStart);
            return s < 1 ? 1 : s;
        }

        public static float MaxFavor(PlayerView v)
        {
            float m = 3f;
            if (v != null) for (int i = 0; i < v.Players.Count; i++) if (v.Players[i].Favor > m) m = v.Players[i].Favor;
            return m;
        }

        public static string FeatureIcon(string feature)
        {
            switch (feature)
            {
                case "cards": return "cards";
                case "calls": return "call";
                case "tipline": return "ringing";
                case "footage": return "footage";
                case "mail": return "task_mail";
                default: return "info";
            }
        }
    }
}
