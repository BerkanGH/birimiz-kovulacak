// Shared contract: enumerations. Values are serialized as bytes; append new members at the end only.

namespace BirimizKovulacak.Core
{
    public enum MatchPhase : byte
    {
        None = 0,
        Briefing = 1,
        Work1 = 2,
        Raid = 3,
        Work2 = 4,
        FinalInspection = 5,
        MeetingDiscussion = 6,
        MeetingDefense1 = 7,
        MeetingDefense2 = 8,
        MeetingVote = 9,
        MeetingVerdict = 10,
        Results = 11,
        Abandoned = 12,
    }

    public enum Role : byte
    {
        Employee = 0,
        Informant = 1,
    }

    public enum ItemKind : byte
    {
        Mug = 0,
        Report = 1,
        Nameplate = 2,
        Suspicious = 3,
        Clutter = 4,
        Spray = 5,
    }

    public enum MugFill : byte
    {
        Empty = 0,
        Full = 1,
        Used = 2,
    }

    public enum SlotKind : byte
    {
        DeskSurface = 0,     // open, capacity 3
        Drawer = 1,          // closed, capacity 3
        NameplateHolder = 2, // open, capacity 1, nameplates only
        HidingSpot = 3,      // closed, capacity 2
        DeliveryTray = 4,    // open; placing a full mug / report here completes a task
        MugShelf = 5,        // open, mugs only
        PrinterTray = 6,     // open, reports only, filled by the printer (players can only take)
        JunkBin = 7,         // open; placing discovered evidence here disposes it
        LostAndFound = 8,    // open; suspicious items start here
        SprayRack = 9,       // open, sprays only
    }

    public enum StationKind : byte
    {
        CoffeeMachine = 0,
        Printer = 1,
        Phone = 2,
        Computer = 3,
        DeliveryPoint = 4,
        CleaningCloset = 5,
    }

    public enum ItemLocationKind : byte
    {
        Slot = 0,
        Carried = 1,
        Floor = 2,
        Archived = 3, // delivered report; no longer in the world
    }

    public enum TaskType : byte
    {
        Coffee = 0,
        Report = 1,
        Phone = 2,
        CleanDesk = 3,
    }

    public enum SabotageType : byte
    {
        NameplateSwap = 0,
        PlantItem = 1,
        LockWorkstation = 2,
        HideReport = 3,
        SpillCoffee = 4,
        MoveClutter = 5,
        EvidenceTampering = 6, // not a "designed" sabotage; recorded when active unsealed evidence is picked up
    }

    /// <summary>Everything a client can ask the host to do. Completion of timed verbs is decided by the host clock.</summary>
    public enum ActionVerb : byte
    {
        None = 0,
        PickUp = 1,            // TargetId = item id
        Place = 2,             // TargetId = slot id (desk surface, drawer, nameplate holder, hiding spot, delivery tray, shelf, ...)
        Drop = 3,              // no target; drops the carried item at the avatar position
        BrewCoffee = 4,        // TargetId = st.coffee; needs an Empty/Used mug in hand
        WriteReport = 5,       // TargetId = desk.N.pc of the actor's own desk
        AnswerPhone = 6,       // TargetId = st.phone while ringing
        CleanDesk = 7,         // TargetId = desk.N (dirty); needs spray
        CleanSpill = 8,        // TargetId = desk.N (spilled); needs spray
        SpillCoffee = 9,       // TargetId = desk.N; needs a Full mug
        LockWorkstation = 10,  // TargetId = desk.N.pc (not own desk)
        UnlockWorkstation = 11,// TargetId = desk.N.pc (locked)
        SearchContainer = 12,  // TargetId = closed slot id (drawer / hiding spot)
        SetWorkPose = 13,      // IntParam 1 = on, 0 = off
        CancelInteraction = 14,
        AuditScanEvidence = 15,// IntParam = evidence id (Informant only)
        AuditScanItem = 16,    // TargetId = item id within range (Informant only)
        SealEvidence = 17,     // IntParam = evidence id (Informant only)
        Accuse = 18,           // IntParam = accused player id (MeetingDiscussion only)
        PresentNote = 19,      // IntParam = private note id (discussion / defenses)
        CastBallot = 20,       // IntParam = candidate player id, or PlayerIds.None to abstain (MeetingVote only)
        RematchVote = 21,      // Results only
    }

    public enum FailureCode : byte
    {
        None = 0,
        WrongMatch = 1,
        WrongPhase = 2,
        NotConnected = 3,
        Busy = 4,              // actor already has an active interaction
        TooFar = 5,
        InvalidTarget = 6,
        TargetBusy = 7,        // station in use / slot reserved by another interaction
        HandsFull = 8,
        HandsEmpty = 9,
        WrongItem = 10,
        ItemUnavailable = 11,  // carried by someone else, archived, not visible to actor
        SlotFull = 12,
        Cooldown = 13,
        TargetImmune = 14,     // anti-grief limits
        NotOwnDesk = 15,
        NoDemand = 16,
        NotLocked = 17,
        AlreadyLocked = 18,
        NotAllowed = 19,       // role/permission (also used for non-Informant ability requests)
        NoCharges = 20,
        EvidenceIneligible = 21,
        AlreadyUsed = 22,      // accusation / ballot / rematch vote already submitted
        CannotTargetSelf = 23,
        NotCandidate = 24,
        Duplicate = 25,        // request id already processed
        Sealed = 26,
        LimitReached = 27,
        NotRinging = 28,
        StationLocked = 29,    // workstation is locked
        NothingToClean = 30,
    }

    /// <summary>What other players can see someone doing. Deliberately coarser than <see cref="ActionVerb"/>.</summary>
    public enum PublicActivity : byte
    {
        None = 0,
        Brewing = 1,      // coffee machine
        Typing = 2,       // any computer use: write report, lock, unlock (indistinguishable on purpose)
        OnPhone = 3,
        Cleaning = 4,     // desk or spill
        Placing = 5,      // putting an item down on an open surface / shelf / tray / bin
        Rummaging = 6,    // drawer / hiding spot: place, take or search (indistinguishable)
        Fiddling = 7,     // nameplate handling
        Spilling = 8,
        Delivering = 9,
        PickingUp = 10,
    }

    public enum EvidenceKind : byte
    {
        SuspiciousItemFound = 0, // manager found a suspicious item at a desk
        HiddenReportFound = 1,   // a report was recovered from a hiding spot / drawer
        WorkstationLockLog = 2,  // system log: desk PC locked at time T (no actor)
        WitnessStatement = 3,    // a private witness note presented in the meeting
        SearchStatement = 4,     // a private search result presented in the meeting
        ScanStatement = 5,       // an Informant scan result presented in the meeting (presenter shown)
    }

    public enum EvidenceStatus : byte
    {
        Active = 0,
        Moved = 1,     // the physical item left the recorded location (weight x0.5)
        Disposed = 2,  // thrown in the junk bin (excluded from observed risk)
    }

    public enum DiscoverySource : byte
    {
        Manager = 0,
        Player = 1,
        System = 2,
    }

    public enum ObservationKind : byte
    {
        MessyDesk = 0,          // dirt / spill / clutter; Detail lists components
        NotAtDesk = 1,
        AtWrongDesk = 2,
        MovingDuringInspection = 3,
        CarryingSuspicious = 4,
        CaughtInAct = 5,
        BehindOnWork = 6,
        WorkingHard = 7,        // positive (negative severity)
        SuspiciousItemAtDesk = 8, // mirrors a SuspiciousItemFound evidence for the feed; severity 0 (evidence carries the weight)
    }

    public enum NoteKind : byte
    {
        Witness = 0,
        SearchResult = 1,
        ScanResult = 2,
    }

    public enum Reliability : byte
    {
        Low = 0,
        Medium = 1,
        High = 2,
    }

    public enum ScanSignal : byte
    {
        NoInconsistency = 0,
        NameplateChangedAfterPlacement = 1,
        MovedRecently = 2,
        HandledByMultiple = 3,
    }

    public enum ObjectiveType : byte
    {
        DismissalPlan = 0,
        ProtectionDeal = 1,
        Scapegoat = 2,
        QuietStar = 3,
        ChaosCoordinator = 4,
        CleanHands = 5,
        OfficeAllRounder = 6,
        Fixer = 7,
        DeskGuardian = 8,
        Unnoticed = 9,
        PerformanceRival = 10,
        UnderPressure = 11,
        InformantGoal = 12, // the Informant's fixed objective
    }

    public enum ObjectiveStatus : byte
    {
        InProgress = 0,
        OnTrack = 1,       // currently satisfied but can still change
        Completed = 2,     // final: predicate true (before company/survival gates)
        Failed = 3,
        PendingVerdict = 4,
    }

    public enum ManagerState : byte
    {
        InOffice = 0,
        Warning = 1,
        Walking = 2,
        Inspecting = 3,
        Reacting = 4,
        Returning = 5,
        InMeeting = 6,
    }

    public enum EventType : byte
    {
        PhaseChanged = 0,
        ManagerWarning = 1,
        ManagerObservation = 2,  // IntArg = observation id
        TaskCompleted = 3,       // Actor, IntArg = (int)TaskType
        QuotaReached = 4,
        PhoneRinging = 5,
        PhoneMissed = 6,
        ReportPrinted = 7,       // IntArg = report owner
        WorkstationLocked = 8,   // TargetId = desk pc (no actor)
        WorkstationUnlocked = 9,
        EvidenceCreated = 10,    // IntArg = evidence id
        EvidenceUpdated = 11,    // IntArg = evidence id (status / seal)
        NoteAdded = 12,          // private; IntArg = note id
        ActionRejected = 13,     // private; IntArg = (int)FailureCode
        InteractionCompleted = 14, // private to actor; IntArg = (int)ActionVerb
        InteractionCancelled = 15, // private to actor
        AccusationFiled = 16,    // Actor = accuser, IntArg = accused
        SpeakerChanged = 17,     // IntArg = speaker
        BallotCast = 18,         // Actor = voter (who voted is public, choice is not)
        VerdictReady = 19,
        ResultsReady = 20,
        PlayerDisconnected = 21, // Actor
        PlayerReconnected = 22,  // Actor
        MatchAbandoned = 23,
        DeskDirty = 24,          // TargetId = desk
        OrderAdded = 25,         // IntArg = (int)TaskType
        RematchVote = 26,        // Actor
        ItemDropped = 27,        // Actor, ItemId (phase change / disconnect)
    }

    public enum Audience : byte
    {
        All = 0,
        Player = 1, // only AudiencePlayer
        Host = 2,   // never sent to clients (logs / tests)
    }

    public enum AbandonReason : byte
    {
        None = 0,
        PlayerTimedOut = 1,
        HostLeft = 2,
        HostAborted = 3,
    }

    public enum TieBreak : byte
    {
        None = 0,
        ObservedRisk = 1,
        Performance = 2,
        SeededDraw = 3,
    }
}
