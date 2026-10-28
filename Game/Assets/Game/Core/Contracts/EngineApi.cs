using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    /// <summary>World queries the rules engine needs from the host's 3D scene. Tests use <see cref="OpenWorld"/>.</summary>
    public interface IWorldQueries
    {
        /// <summary>True if nothing opaque blocks the segment between two eye-height points.</summary>
        bool HasLineOfSight(Vec3 from, Vec3 to);
    }

    /// <summary>A world with no occluders.</summary>
    public sealed class OpenWorld : IWorldQueries
    {
        public static readonly OpenWorld Instance = new OpenWorld();
        public bool HasLineOfSight(Vec3 from, Vec3 to) { return true; }
    }

    [Serializable]
    public sealed class PlayerSlotInfo
    {
        public int Id;
        public string Name = "";
        public int ColorIndex;
    }

    /// <summary>Everything needed to start one match.</summary>
    [Serializable]
    public sealed class MatchSetup
    {
        public int MatchId;
        public int Seed;
        public double StartTime;
        public List<PlayerSlotInfo> Players = new List<PlayerSlotInfo>();
        /// <summary>Carried across rematches in one session; may be null for the first match.</summary>
        public SessionHistory History;
        /// <summary>Test hook: force a specific Informant (PlayerIds.None = random).</summary>
        public int ForceInformant = PlayerIds.None;
        /// <summary>Test hook: force objective card ids per player (null = random). Entry for the Informant is ignored.</summary>
        public List<string> ForceObjectives;
        /// <summary>Test hook: force objective targets per player (null = random).</summary>
        public List<int> ForceTargets;
        // v2
        public GameMode Mode;
        /// <summary>Week mode day (0..4); ignored in quick play.</summary>
        public int WeekDay;
        /// <summary>Manager profile id; empty = the engine picks (week schedule, else seeded random avoiding the last one).</summary>
        public string ManagerProfileId = "";
        /// <summary>Test hook: force card hands per player id (null = deal randomly).</summary>
        public List<List<SabotageType>> ForceCards;
        /// <summary>Test hook: force the Kayirmaci favourite (PlayerIds.None = random).</summary>
        public int ForceFavourite = PlayerIds.None;
    }

    /// <summary>Cross-match memory used to reduce repetition (Informant and objective targets).</summary>
    [Serializable]
    public sealed class SessionHistory
    {
        public List<int> PastInformants = new List<int>();
        /// <summary>Encoded "holder>target" pairs from previous matches, most recent last.</summary>
        public List<string> PastTargetPairs = new List<string>();
        /// <summary>How many times each player was targeted by an objective (index = player id).</summary>
        public List<int> TimesTargeted = new List<int>();
        public List<string> PastCards = new List<string>();
        public int MatchesPlayed;
        // v2
        /// <summary>Week mode: the day the NEXT match is (0..4; wraps to 0 after Friday and CareerPoints reset).</summary>
        public int NextWeekDay;
        public int WeekNumber;
        /// <summary>Career points per player id for the current week.</summary>
        public List<int> CareerPoints = new List<int>();
        public List<string> PastManagerProfiles = new List<string>();
    }
}
