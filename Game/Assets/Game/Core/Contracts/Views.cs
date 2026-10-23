using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    /// <summary>
    /// Everything one recipient is allowed to know, built by the host per recipient.
    /// Public data is identical for all recipients; <see cref="Me"/> is owner-only; host-only truth
    /// (incident actors, item provenance, other players' roles/objectives, misconduct) never appears here
    /// except inside <see cref="Result"/> once the match reaches Results.
    /// The host's own UI consumes exactly the same view (no special access).
    /// </summary>
    [Serializable]
    public sealed class PlayerView
    {
        public int MatchId;
        public int StateVersion;
        public int Recipient;
        public MatchPhase Phase;
        public int PhaseSerial;
        public double PhaseStartTime;
        public double PhaseEndTime;
        /// <summary>Host time when the view was built.</summary>
        public double ServerTime;
        public AbandonReason AbandonReason;

        public QuotaView Quota = new QuotaView();
        public List<PlayerPublicView> Players = new List<PlayerPublicView>();
        public List<ItemView> Items = new List<ItemView>();
        public List<DeskView> Desks = new List<DeskView>();
        public List<StationView> Stations = new List<StationView>();
        public List<EvidenceView> Evidence = new List<EvidenceView>();
        public List<ObservationView> Observations = new List<ObservationView>();
        public List<LogEntry> Log = new List<LogEntry>();
        public ManagerView Manager = new ManagerView();
        public MeetingView Meeting = new MeetingView();
        public PrivateView Me = new PrivateView();
        /// <summary>Null until the Results phase; then the full post-match reveal (identical for everyone).</summary>
        public MatchResultView Result;
    }

    [Serializable]
    public sealed class QuotaView
    {
        public float Points;
        public float Target;
        public int CoffeeOrders;
        public int ReportOrders;
        public bool PhoneRinging;
        public double PhoneRingEnds;
        public int DirtyDesks;
        public bool Frozen; // true after the final inspection
    }

    [Serializable]
    public sealed class PlayerPublicView
    {
        public int Id;
        public string Name = "";
        public int ColorIndex;
        public int DeskIndex;
        public bool Connected;
        /// <summary>Host time at which a disconnected player's grace period ends (0 when connected).</summary>
        public double DisconnectDeadline;
        public float Performance;
        public int TasksCompleted;
        /// <summary>Coarse 0..4 meter derived from observed risk (never from true misconduct).</summary>
        public int SuspicionLevel;
        public string CarryingItemId = "";
        public PublicActivity Activity;
        public string ActivityTargetId = "";
        public double ActivityStart;
        public double ActivityEnd;
        public bool WorkPose;
        /// <summary>Official accusations received (only meaningful from the meeting onwards).</summary>
        public int AccusationsReceived;
    }

    [Serializable]
    public sealed class ItemView
    {
        public string Id = "";
        public ItemKind Kind;
        public string Variant = "";
        public ItemLocationKind Location;
        public string SlotId = "";
        /// <summary>Stable visual position index inside the slot (0..capacity-1).</summary>
        public int SlotIndex;
        public int CarrierId = PlayerIds.None;
        public Vec3 FloorPosition;
        public float FloorYaw;
        public MugFill Fill;
        /// <summary>For reports: the player who wrote it (shown on the paper).</summary>
        public int ReportOwner = PlayerIds.None;
        /// <summary>For nameplates: the name printed on it.</summary>
        public int NameplateOf = PlayerIds.None;
        public bool Sealed;
        /// <summary>True if this item is the subject of an Active evidence record.</summary>
        public bool IsEvidence;
    }

    [Serializable]
    public sealed class DeskView
    {
        public int Index;
        public string Id = "";
        public int AssignedOwner = PlayerIds.None;
        /// <summary>Name currently shown by the nameplate in this desk's holder (PlayerIds.None if empty).</summary>
        public int DisplayedNameplateOf = PlayerIds.None;
        public bool Dirty;
        public bool Spill;
        public bool PcLocked;
        public double PcLockedAt;
        public double PcLockedUntil;
        public int ClutterCount;
    }

    [Serializable]
    public sealed class StationView
    {
        public string Id = "";
        public StationKind Kind;
        public int UsedBy = PlayerIds.None;
        public double BusyUntil;
        /// <summary>Printer: queued jobs (owners) not yet printed. Empty for other stations.</summary>
        public List<int> PrintQueue = new List<int>();
        /// <summary>Printer: host time the current job finishes (0 if idle).</summary>
        public double PrintEndsAt;
    }

    [Serializable]
    public sealed class EvidenceView
    {
        public int Id;
        public EvidenceKind Kind;
        public double Time;
        public MatchPhase PhaseFound;
        public string ItemId = "";
        public string ItemVariant = "";
        /// <summary>Slot / station where it was found or which it concerns.</summary>
        public string LocationId = "";
        public int DeskIndex = -1;
        /// <summary>Apparent owner according to the desk nameplate at discovery (or frozen by a seal).</summary>
        public int AttributedTo = PlayerIds.None;
        public DiscoverySource Source;
        /// <summary>Player who discovered / presented it (PlayerIds.None for manager/system).</summary>
        public int SourcePlayer = PlayerIds.None;
        public EvidenceStatus Status;
        public bool Sealed;
        /// <summary>For presented notes: the subject of the statement.</summary>
        public int SubjectPlayer = PlayerIds.None;
        public PublicActivity ObservedActivity;
        public string HeldBefore = "";
        public string HeldAfter = "";
        /// <summary>For presented scan statements.</summary>
        public Reliability ScanReliability;
        public ScanSignal ScanSignal;
        /// <summary>Weight this record currently adds to its attributed/subject player's observed risk.</summary>
        public float RiskWeight;
    }

    [Serializable]
    public sealed class ObservationView
    {
        public int Id;
        public ObservationKind Kind;
        public double Time;
        public MatchPhase Phase;
        public int Subject = PlayerIds.None;
        public int DeskIndex = -1;
        /// <summary>Observed-risk contribution (negative for positive observations).</summary>
        public float Severity;
        /// <summary>Machine-readable detail, e.g. "dirty,spill,clutter:2" or an item variant.</summary>
        public string Detail = "";
    }

    [Serializable]
    public sealed class LogEntry
    {
        public double Time;
        public EventType Kind;
        public int Actor = PlayerIds.None;
        public string TargetId = "";
        public int IntArg = -1;
        public string StrArg = "";
    }

    [Serializable]
    public sealed class ManagerView
    {
        public ManagerState State;
        public Vec3 Position;
        public float Yaw;
        public int InspectingDesk = -1;
        /// <summary>Observation id that triggered the current reaction bubble (-1 if none).</summary>
        public int ReactionObservation = -1;
        public double ReactionUntil;
        /// <summary>Host time at which the manager leaves the office (during warnings).</summary>
        public double ArrivesAt;
    }

    [Serializable]
    public sealed class MeetingView
    {
        public List<AccusationView> Accusations = new List<AccusationView>();
        public List<int> Shortlist = new List<int>();
        public int Speaker = PlayerIds.None;
        public double SpeakerEnds;
        public int BallotsCast;
        public List<int> Voters = new List<int>();
        public List<int> PresentedEvidence = new List<int>();
        /// <summary>Null until MeetingVerdict.</summary>
        public VerdictView Verdict;
    }

    [Serializable]
    public sealed class AccusationView
    {
        public int Accuser;
        public int Accused;
        public double Time;
    }

    /// <summary>Owner-only state.</summary>
    [Serializable]
    public sealed class PrivateView
    {
        public Role Role;
        public ObjectiveView Objective = new ObjectiveView();
        /// <summary>Null unless the recipient is the Informant.</summary>
        public InformantView Informant;
        public List<PrivateNote> Notes = new List<PrivateNote>();
        public InteractionView ActiveInteraction;
        public bool HasAccused;
        public int AccusedPlayer = PlayerIds.None;
        public bool HasVoted;
        public int Ballot = PlayerIds.None;
        public bool RematchVoted;
        /// <summary>Host time until which the recipient cannot start another sabotage of each type (index = SabotageType).</summary>
        public List<double> SabotageCooldowns = new List<double>();
        /// <summary>Reconnect token echo (so a client that lost local state can recover it).</summary>
        public string ReconnectToken = "";
    }

    [Serializable]
    public sealed class InteractionView
    {
        public ActionVerb Verb;
        public string TargetId = "";
        public double StartTime;
        public double EndTime;
    }

    [Serializable]
    public sealed class ObjectiveView
    {
        public string CardId = "";
        public ObjectiveType Type;
        public int Target = PlayerIds.None;
        public int Progress;
        public int Required;
        public int Progress2;
        public int Required2;
        public ObjectiveStatus Status;
        /// <summary>Extra flags, e.g. "accused" (Quiet Star), "sabotaged" (Clean Hands), "penalty" (Under Pressure), "raid:clean".</summary>
        public List<string> Flags = new List<string>();
    }

    [Serializable]
    public sealed class InformantView
    {
        public int ScansLeft;
        public int SealsLeft;
        public int SealedEvidenceId = -1;
        public List<ScanResultView> Scans = new List<ScanResultView>();
    }

    [Serializable]
    public sealed class ScanResultView
    {
        public int Id;
        public double Time;
        public int EvidenceId = -1;
        public string ItemId = "";
        public string ItemVariant = "";
        public int DeskIndex = -1;
        /// <summary>Apparent owner the reliability judgement refers to (desk nameplate), or None.</summary>
        public int ApparentOwner = PlayerIds.None;
        public Reliability Reliability;
        public ScanSignal Signal;
        /// <summary>Private note id that can be presented in the meeting.</summary>
        public int NoteId = -1;
    }

    [Serializable]
    public sealed class PrivateNote
    {
        public int Id;
        public NoteKind Kind;
        public double Time;
        /// <summary>Witness: the person seen. Search: None.</summary>
        public int Subject = PlayerIds.None;
        public PublicActivity Activity;
        public string TargetId = "";
        public int DeskIndex = -1;
        public string HeldBefore = "";
        public string HeldAfter = "";
        /// <summary>Search: item variants seen in the container at that time.</summary>
        public List<string> Contents = new List<string>();
        /// <summary>Scan: result id.</summary>
        public int ScanId = -1;
        public bool Presented;
    }
}
