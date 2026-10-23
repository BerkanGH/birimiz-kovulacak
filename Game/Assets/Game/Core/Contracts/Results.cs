using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    /// <summary>Public verdict, identical for every client. Contains no hidden truth.</summary>
    [Serializable]
    public sealed class VerdictView
    {
        public int FiredPlayer = PlayerIds.None;
        public List<CandidateScore> Candidates = new List<CandidateScore>();
        public TieBreak TieBreakUsed;
        /// <summary>Evidence ids that contributed most to the fired player's observed risk (max 3).</summary>
        public List<int> KeyEvidence = new List<int>();
        /// <summary>Observation ids that contributed most to the fired player's observed risk (max 3).</summary>
        public List<int> KeyObservations = new List<int>();
        public float VoteWeight;
        public float RiskWeight;
        public float PerformanceWeight;
    }

    [Serializable]
    public sealed class CandidateScore
    {
        public int Player;
        public int Accusations;
        public int Votes;
        public float VoteShare;
        public float ObservedRisk;
        public float RiskNormalized;
        public float Performance;
        public float PerformanceDeficit;
        public float Score;
    }

    /// <summary>Full post-match reveal, identical for every client. Sent only in the Results phase.</summary>
    [Serializable]
    public sealed class MatchResultView
    {
        public bool CompanySucceeded;
        public float QuotaPoints;
        public float QuotaTarget;
        public int FiredPlayer = PlayerIds.None;
        public VerdictView Verdict = new VerdictView();
        public int InformantPlayer = PlayerIds.None;
        public bool InformantWon;
        /// <summary>Players in the highest positive true-misconduct tier (empty if everyone is at zero).</summary>
        public List<int> TopMisconductTier = new List<int>();
        public List<PlayerOutcome> Players = new List<PlayerOutcome>();
        public List<IncidentReveal> Incidents = new List<IncidentReveal>();
        public List<RecapBeat> Recap = new List<RecapBeat>();
        public int MatchSeed;
        /// <summary>Deterministic hash of the result content; clients log it to prove they all received the same result.</summary>
        public string ResultHash = "";
    }

    [Serializable]
    public sealed class PlayerOutcome
    {
        public int Player;
        public string Name = "";
        public Role Role;
        public string ObjectiveCardId = "";
        public ObjectiveType ObjectiveType;
        public int ObjectiveTarget = PlayerIds.None;
        /// <summary>Predicate satisfied (before company / survival gates).</summary>
        public bool ObjectiveCompleted;
        public bool Fired;
        /// <summary>Final personal victory after all gates.</summary>
        public bool Won;
        /// <summary>Localization key explaining the outcome, e.g. "outcome.company_failed".</summary>
        public string ExplanationKey = "";
        public float TrueMisconduct;
        public int MisconductRank;
        public float FinalPerformance;
        public float FalseAccusationPenalty;
        public int TasksCompleted;
        public int SabotageCount;
        public List<string> Titles = new List<string>();
    }

    [Serializable]
    public sealed class IncidentReveal
    {
        public int Id;
        public SabotageType Type;
        public int Actor;
        public int Victim = PlayerIds.None;
        public int DeskIndex = -1;
        public string ItemId = "";
        public double Time;
        public float Misconduct;
        public bool Discovered;
        public int ReversedBy = PlayerIds.None;
        /// <summary>Who the public evidence blamed for it (if it was discovered), to show framing.</summary>
        public int BlamedPlayer = PlayerIds.None;
    }

    [Serializable]
    public sealed class RecapBeat
    {
        public double Time;
        /// <summary>Localization key, e.g. "recap.planted", "recap.manager_found", "recap.fired".</summary>
        public string Key = "";
        public int Actor = PlayerIds.None;
        public int Other = PlayerIds.None;
        public string ItemVariant = "";
        public int DeskIndex = -1;
    }

    /// <summary>Host-only debug/truth dump for logs and automated tests. Never sent to clients.</summary>
    [Serializable]
    public sealed class HostDebugView
    {
        public int MatchId;
        public int Seed;
        public MatchPhase Phase;
        public List<Role> Roles = new List<Role>();
        public List<string> ObjectiveCards = new List<string>();
        public List<int> ObjectiveTargets = new List<int>();
        public List<float> TrueMisconduct = new List<float>();
        public List<float> ObservedRisk = new List<float>();
        public List<IncidentReveal> Incidents = new List<IncidentReveal>();
        /// <summary>Per item: ordered list of players who carried it ("item.sus.duck" -> [2, 0]).</summary>
        public Dictionary<string, List<int>> ItemHandlers = new Dictionary<string, List<int>>();
        public float QuotaPoints;
        public int ProcessedRequests;
        public int RejectedRequests;
    }
}
