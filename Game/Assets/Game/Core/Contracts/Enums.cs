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
        Mail = 6,        // v2: addressed to a desk (ItemView.AddressedDesk)
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
        Mailroom = 10,       // v2: open, mail only; filled by the engine (players can only take)
    }

    public enum StationKind : byte
    {
        CoffeeMachine = 0,
        Printer = 1,
        Phone = 2,
        Computer = 3,
        DeliveryPoint = 4,
        CleaningCloset = 5,
        Router = 6,          // v2
        FuseBox = 7,         // v2
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
        Mail = 4,            // v2: place a mail item on its addressee's desk surface
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
        // v2 card sabotage (see SabotageDef.Basic)
        FakeEmail = 7,
        PullPlug = 8,
        SaltCoffee = 9,
        InternetOutage = 10,
        PowerCut = 11,
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
        // v2
        LockScreen = 22,       // TargetId = own desk.N.pc; closes an open screen session
        SendFakeEmail = 23,    // TargetId = another desk's pc with an open screen session; IntParam = email template index (card)
        PullPlug = 24,         // TargetId = desk.N (not own) (card)
        PlugIn = 25,           // TargetId = desk.N (unplugged); anyone
        SaltCoffee = 26,       // TargetId = st.coffee; needs a Full mug in hand (card)
        TasteCoffee = 27,      // no target; needs a Full mug in hand; mug becomes Used, private TasteResult note
        CutInternet = 28,      // TargetId = st.router (card)
        RestartRouter = 29,    // TargetId = st.router while the internet is down; anyone
        CutPower = 30,         // TargetId = st.fusebox (card)
        ResetFuse = 31,        // TargetId = st.fusebox while the power is down; anyone
        CallPlayer = 32,       // IntParam = callee player id
        AnswerCall = 33,       // accept the incoming ringing call
        DeclineCall = 34,      // reject the incoming call / cancel an outgoing ringing call
        HangUp = 35,           // end the active call
        TipManager = 36,       // TargetId = desk.N; anonymous tip line call
        PullCallLogs = 37,     // Informant tool: private CallLog note
        RequestFootage = 38,   // TargetId = camera id, IntParam = second of the match (meeting only)
        Emote = 39,            // IntParam = (int)EmoteId; cosmetic
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
        // v2
        NoInternet = 31,       // internet outage blocks reports / printing / emails
        ScreenLocked = 32,     // fake email needs an open screen session
        MissingCard = 33,      // card sabotage without holding that card
        PcUnplugged = 34,
        InCall = 35,           // already ringing / in a call
        NotInCall = 36,
        NoIncomingCall = 37,
        Unavailable = 38,      // callee disconnected / busy / on cooldown
        NotSpeaker = 39,       // footage outside your own defense window (non-Informant)
        FeatureDisabled = 40,  // feature switched off for this match / week day
        AlreadyActive = 41,    // internet already down / power already out
        NotActive = 42,        // nothing to restart / reset
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
        // v2
        OnCall = 11,      // personal phone use: ringing out, in a call, tip line (indistinguishable)
        Plugging = 12,    // pull plug / plug in (indistinguishable)
        ServerWork = 13,  // cut internet / restart router (indistinguishable)
        FuseWork = 14,    // cut power / reset fuse (indistinguishable)
        Drinking = 15,    // tasting coffee
    }

    public enum EvidenceKind : byte
    {
        SuspiciousItemFound = 0, // manager found a suspicious item at a desk
        HiddenReportFound = 1,   // a report was recovered from a hiding spot / drawer
        WorkstationLockLog = 2,  // system log: desk PC locked at time T (no actor)
        WitnessStatement = 3,    // a private witness note presented in the meeting
        SearchStatement = 4,     // a private search result presented in the meeting
        ScanStatement = 5,       // an Informant scan result presented in the meeting (presenter shown)
        // v2
        EmailLog = 6,            // system: an email was sent from desk N's pc at T (attributed to that pc's owner)
        CctvFootage = 7,         // footage requested in the meeting (Sightings)
        CallLogStatement = 8,    // Informant's call log note presented in the meeting
        TasteStatement = 9,      // a tasting note presented in the meeting
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
        // v2
        PersonalCall = 9,        // seen on a personal call during an inspection
        InappropriateEmail = 10, // manager read an email "from" this player
        SaltyCoffee = 11,        // delivered a salted coffee
        TipClean = 12,           // tipped desk was clean (positive)
        TipFound = 13,           // tipped desk search found something (mirrors evidence; severity 0)
    }

    public enum NoteKind : byte
    {
        Witness = 0,
        SearchResult = 1,
        ScanResult = 2,
        // v2
        CallLog = 3,
        TasteResult = 4,
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
        // v2
        IncomingCall = 28,       // public: Actor = callee whose phone rings (caller hidden)
        CallStarted = 29,        // private to both parties: Actor = other party
        CallEnded = 30,          // private to both parties: Actor = other party, IntArg = 1 if it was answered
        EmailSent = 31,          // public: IntArg = email id
        InternetDown = 32,       // public, no actor
        InternetRestored = 33,   // public: Actor = who restarted the router
        PowerDown = 34,          // public, no actor
        PowerRestored = 35,      // public: Actor = who reset the fuse
        PlugPulled = 36,         // public: TargetId = desk (no actor)
        PlugRestored = 37,       // public: TargetId = desk, Actor = who plugged it in
        ManagerTipReceived = 38, // public: "the manager got an anonymous tip" (no tipster, no desk)
        FavorChanged = 39,       // public: Actor, StrArg = new favor value (invariant culture)
        FootageShown = 40,       // public: IntArg = evidence id
        SurpriseInspection = 41, // public
        MailArrived = 42,        // public: ItemId, IntArg = addressed desk
        Emote = 43,              // public: Actor, IntArg = (int)EmoteId
        CoffeeTasted = 44,       // private to taster: IntArg = 1 salted / 0 fine
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

    // ---- v2 ----

    public enum CallState : byte
    {
        None = 0,
        RingingOut = 1, // I am calling someone, they have not answered yet
        RingingIn = 2,  // someone is calling me
        Active = 3,
    }

    public enum EmoteId : byte
    {
        Wave = 0,
        Point = 1,
        Facepalm = 2,
        Shrug = 3,
        Laugh = 4,
    }

    public enum GameMode : byte
    {
        QuickPlay = 0, // one match, every feature on, random manager
        Week = 1,      // five-day "Is Haftasi" with career points
    }

    /// <summary>What earns a player the manager's favour (per profile).</summary>
    public enum FavorSource : byte
    {
        None = 0,
        ReportsDelivered = 1,
        CoffeeDelivered = 2,
        AnyTask = 3,
    }
}
