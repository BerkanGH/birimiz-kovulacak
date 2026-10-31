using System;

namespace BirimizKovulacak.Core
{
    // v4 "Hybrid Office" contract (normative rules: docs/GAMEPLAY_REDESIGN.md, wire/engine details: docs/CONTRACTS_V4.md).
    // Owned by the lead. Enum values are wire values: append, never renumber.

    public static class LineProtocol
    {
        public const int Version = 4;
        /// <summary>Ruleset identity for analytics and results; never pool data across different values.</summary>
        public const string RulesetVersion = "v4.0-hybrid";
        public const int MinPlayers = 3;
        public const int MaxPlayers = 6;
    }

    /// <summary>Special participant ids (player ids are 0..5).</summary>
    public static class LineIds
    {
        public const int None = -1;
        /// <summary>Ballot option "No action" (daily meeting) / "Fire nobody" (final); also "nobody was fired".</summary>
        public const int Nobody = -2;
        /// <summary>The manager (morning memo, mail sender).</summary>
        public const int Manager = 101;
        /// <summary>Company IT / HR system (system notices).</summary>
        public const int System = 102;
    }

    public static class LineManagerIds
    {
        public const string Paragoz = "paragoz";
        public const string Disiplinci = "disiplinci";
        public const string Kayirmaci = "kayirmaci";
        public const string Paranoyak = "paranoyak";
        public static readonly string[] All = { Paragoz, Disiplinci, Kayirmaci, Paranoyak };
    }

    public enum LinePhase : byte
    {
        None = 0,
        /// <summary>Block brief (code name "day" = work block).</summary>
        Morning = 1,
        /// <summary>Task card (code name "round" = task).</summary>
        RoundIntro = 2,
        RoundPlay = 3,
        RoundReveal = 4,
        MeetingDossier = 5,
        MeetingDiscussion = 6,
        MeetingVote = 7,
        MeetingVerdict = 8,
        FinalDefense = 9,
        FinalVote = 10,
        FinalVerdict = 11,
        WeekResults = 12,
        Abandoned = 13,
        /// <summary>v4: between the two tasks of a block (and after the last task of the match).</summary>
        Break = 14,
    }

    /// <summary>Why the previous phase ended (PhaseChanged.B).</summary>
    public enum PhaseEndReason : byte
    {
        Timer = 0,
        /// <summary>Timer that had been shortened because every connected player submitted.</summary>
        TimerShortened = 1,
        HostContinue = 2,
        Abandoned = 3,
        /// <summary>First phase of the match (no previous phase).</summary>
        Start = 4,
        /// <summary>Final defense/vote skipped because the company closed.</summary>
        Skipped = 5,
    }

    public enum WeekLength : byte { Short = 0, Full = 1 }

    /// <summary>v4 task pool (docs/GAMEPLAY_REDESIGN.md §6). Localisation id: <see cref="LineTasks.Id"/>.</summary>
    public enum TaskKind : byte
    {
        None = 0,
        InvoiceReconcile = 1,
        BudgetSplit = 2,
        CustomerSpec = 3,
        ReportReview = 4,
        RoomBooking = 5,
        ArchiveVisitor = 6,
        BonusPool = 7,
        ServerCode = 8,
        PhishingCheck = 9,
        ShiftCover = 10,
        VendorPick = 11,
        SupplySwap = 12,
    }

    public enum TaskFamily : byte { None = 0, Pair = 1, All = 2, Role = 3 }

    public static class LineTasks
    {
        public const int Count = 12;
        static readonly string[] Ids =
        {
            "", "invoice_reconcile", "budget_split", "customer_spec", "report_review", "room_booking", "archive_visitor",
            "bonus_pool", "server_code", "phishing_check", "shift_cover", "vendor_pick", "supply_swap",
        };
        static readonly TaskFamily[] Families =
        {
            TaskFamily.None, TaskFamily.Pair, TaskFamily.All, TaskFamily.Pair, TaskFamily.Role, TaskFamily.All, TaskFamily.Role,
            TaskFamily.Pair, TaskFamily.All, TaskFamily.All, TaskFamily.All, TaskFamily.All, TaskFamily.All,
        };
        /// <summary>Stable string id (localisation keys task.&lt;id&gt;.*, analytics task_id).</summary>
        public static string Id(TaskKind k) { int i = (int)k; return i >= 0 && i < Ids.Length ? Ids[i] : ""; }
        public static TaskFamily Family(TaskKind k) { int i = (int)k; return i >= 0 && i < Families.Length ? Families[i] : TaskFamily.None; }
        public static TaskKind Parse(string id) { for (int i = 1; i < Ids.Length; i++) if (Ids[i] == id) return (TaskKind)i; return TaskKind.None; }
    }

    public enum TrickType : byte
    {
        None = 0,
        SahteEposta = 1,
        KahveDok = 2,
        FisCek = 3,
        InternetKes = 4,
        HatDinle = 5,
    }

    /// <summary>The local player's phone. Dialing = I am calling and it rings at the other end; Ringing = somebody calls me.</summary>
    public enum CallStatus : byte { Idle = 0, Dialing = 1, Ringing = 2, Connected = 3 }

    public enum CallEndReason : byte
    {
        None = 0,
        HungUp = 1,
        Declined = 2,
        Missed = 3,
        /// <summary>A Pull-the-Plug trick cut one side.</summary>
        LineCut = 4,
        /// <summary>A meeting ended every call.</summary>
        PhaseEnd = 5,
        Disconnected = 6,
    }

    public enum LineVerb : byte
    {
        None = 0,
        /// <summary>Target = callee.</summary>
        Call = 1,
        Answer = 2,
        Decline = 3,
        HangUp = 4,
        /// <summary>Task controls: Target/A/B per control field; C = <see cref="TaskField"/> mask of the fields being set.</summary>
        SubmitTask = 5,
        /// <summary>Supply Swap: Target = receiver, A = index into my held items.</summary>
        GiveItem = 6,
        /// <summary>This match's dirty trick: Target = victim (fake mail: apparent sender), A = template or -1, Text = fake mail body.</summary>
        UseTrick = 7,
        /// <summary>Meeting / final ballot: Target = colleague, <see cref="LineIds.Nobody"/> (No action / Fire nobody) or -1 (withdraw/abstain).</summary>
        Vote = 8,
        /// <summary>Private text: Target = recipient, Text.</summary>
        SendText = 9,
        /// <summary>Email: Target = recipient or -1 (everyone), Text, A = template or -1, B = mentioned player or -1, C = mail id replied to or -1.</summary>
        SendEmail = 10,
        /// <summary>Public meeting chat (meeting phases): Text.</summary>
        SendMeetingChat = 11,
        /// <summary>Anonymous complaint: Target = colleague, A = preset index or -1, Text (free text when A = -1).</summary>
        FileComplaint = 12,
        /// <summary>Money: Target = receiver, A = amount.</summary>
        Transfer = 13,
        /// <summary>Evidence purchase: A = offer id.</summary>
        BuyEvidence = 14,
        /// <summary>Meeting: A = record id from my accessible records.</summary>
        PresentRecord = 15,
        /// <summary>Remedial report: Target = co-signer, A = task number (match index).</summary>
        StartRemedial = 16,
        /// <summary>Co-signer answers: Target = requester, A = 1 accept / 0 decline.</summary>
        RespondRemedial = 17,
        /// <summary>Host only, WeekResults: end the match now.</summary>
        HostContinue = 18,
        /// <summary>The fired player, during WeekResults, picks next match's manager (A = index into LineManagerIds.All).</summary>
        PickNextManager = 19,
    }

    /// <summary><see cref="LineVerb.SubmitTask"/> field mask bits.</summary>
    [Flags]
    public enum TaskField : byte { None = 0, Target = 1, A = 2, B = 4 }

    public enum LineFailure : byte
    {
        None = 0,
        WrongPhase = 1,
        StaleWeek = 2,
        Duplicate = 3,
        InvalidTarget = 4,
        Self = 5,
        Busy = 6,
        PhoneDead = 7,
        ComputerBlocked = 8,
        AlreadyUsed = 9,
        NotAvailable = 10,
        NoAttemptsLeft = 11,
        NotUnlocked = 12,
        AlreadyInCall = 13,
        NotInCall = 14,
        NotRinging = 15,
        BadParam = 16,
        Absent = 17,
        HostOnly = 18,
        LimitReached = 19,
        RoundSolved = 20,
        // v4
        TooLong = 21,
        EmptyText = 22,
        RateLimited = 23,
        InsufficientFunds = 24,
        /// <summary>The record/offer/item is not mine (not accessible).</summary>
        NotOwned = 25,
        /// <summary>A locking control was already submitted.</summary>
        Locked = 26,
        /// <summary>The control depends on another player's submission that has not arrived.</summary>
        Waiting = 27,
    }

    public enum LineAudience : byte { All = 0, Player = 1, Host = 2 }

    public enum LineEventType : byte
    {
        None = 0,
        /// <summary>Public: A = (int)LinePhase entered, B = (int)PhaseEndReason of the phase that ended.</summary>
        PhaseChanged = 1,
        /// <summary>To the callee: Actor = caller.</summary>
        CallIncoming = 2,
        /// <summary>To both parties: Actor = caller, Target = callee, A = call id.</summary>
        CallConnected = 3,
        /// <summary>To both parties: Actor = the other side, A = (int)CallEndReason, B = call id.</summary>
        CallEnded = 4,
        /// <summary>To the caller: Target = callee who is busy.</summary>
        CallBusy = 5,
        /// <summary>To the recipient: A = (int)MessageKind, B = message seq (pull text through ILineEngine.MessagesFor).</summary>
        MessageReceived = 6,
        /// <summary>Public: Actor submitted (or changed) task controls.</summary>
        TaskSubmitted = 7,
        /// <summary>To giver and receiver: Actor = giver, Target = receiver, A = item type.</summary>
        ItemGiven = 8,
        /// <summary>To the victim: A = (int)TrickType.</summary>
        TrickHit = 9,
        /// <summary>Public: the office internet went down (no actor).</summary>
        InternetDown = 10,
        /// <summary>To the actor: A = (int)TrickType, Target = victim.</summary>
        TrickUsed = 11,
        /// <summary>To sender and receiver: Actor = sender, Target = receiver, A = amount.</summary>
        TransferMade = 12,
        /// <summary>Public: Actor has voted (not for whom).</summary>
        VoteCast = 13,
        /// <summary>Public: Target receives a warning.</summary>
        WarningGiven = 14,
        /// <summary>Public: Target is fired, or LineIds.Nobody.</summary>
        Fired = 15,
        /// <summary>Public: Target's blame changed by A (B = (int)BlameReason).</summary>
        BlameChanged = 16,
        /// <summary>Public: Kayırmacı's favourite of the block is Target.</summary>
        FavouriteNamed = 17,
        /// <summary>Public: recording A (pick index) starts now.</summary>
        RecordingStarted = 18,
        /// <summary>Public: Target (re)connected (A = 1) or dropped (A = 0).</summary>
        Presence = 19,
        /// <summary>Public: task number A resolved with output B (of its max).</summary>
        TaskResolved = 20,
        /// <summary>To the tapper: wiretap on Target started (A = 1) or ended (A = 0).</summary>
        Wiretap = 21,
        /// <summary>To a busy (or dead-line) player: Actor tried to call you.</summary>
        MissedCall = 22,
        /// <summary>Public: Actor (the fired player) picked next match's manager; A = index into LineManagerIds.All.</summary>
        NextManagerPicked = 23,
        /// <summary>To the player: access to record A was granted.</summary>
        RecordGranted = 24,
        /// <summary>Public: Actor presented record A in the meeting.</summary>
        EvidencePresented = 25,
        /// <summary>Public: an incident (A = id, B = (int)IncidentKind) was proven by presented evidence; Target = its actor.</summary>
        IncidentExposed = 26,
        /// <summary>To the co-signer: Actor = requester, A = task number.</summary>
        RemedialRequested = 27,
        /// <summary>To both: Actor = requester, Target = co-signer, A = 1 accepted / 0 declined or expired, B = task number.</summary>
        RemedialResolved = 28,
        /// <summary>Public: A = provisional productivity per mille (-1 measuring), B = change in per mille.</summary>
        ProductivityChanged = 29,
        /// <summary>Public: the company closed; A = final productivity per mille.</summary>
        CompanyClosed = 30,
        /// <summary>To the buyer: A = offer id, B = price paid.</summary>
        PurchaseMade = 31,
        /// <summary>To the player: A = signed money change, B = (int)MoneyReason.</summary>
        MoneyChanged = 32,

        // ---- host-only semantic events (Audience = Host): never sent to clients; consumed by LineTelemetryObserver.
        /// <summary>Actor set task fields: Target = (int)TaskField mask, A = choice category code (docs/CONTRACTS_V4.md §8), B = task number, C = 1 when it changed an earlier submission.</summary>
        HostChoice = 40,
        /// <summary>Actor credited A money (after the block filter), B = (int)MoneyReason, C = raw amount before the filter.</summary>
        HostIncome = 41,
        /// <summary>Actor created incident B of kind A; Target = victim or -1; C = task number or -1.</summary>
        HostIncident = 42,
        /// <summary>Every connected player submitted task C: A = seconds saved (ms), B = seconds carried into the break (ms).</summary>
        HostEarlySubmit = 43,
        /// <summary>Actor's objective (A = (int)ObjectiveKind) evaluated at match end: B = 1 completed / 0 not; Target = objective target or -1.</summary>
        HostObjective = 44,
        /// <summary>Daily/final ballot resolved: Target = top nominee or -1, A = top votes, B = (int)VoteOutcome, C = No action / Fire nobody votes.</summary>
        HostBallot = 45,
        /// <summary>Recording pick A scheduled with B candidates; C = (int)RecordingSelection.</summary>
        HostRecordingPick = 46,
        /// <summary>Actor sent a message: Target = recipient (-1 everyone), A = (int)MessageKind, B = length bucket (0: 1-20, 1: 21-80, 2: 81+), C = flags (1 reply, 2 template, 4 preset, 8 spoofed).</summary>
        HostMessage = 47,
        /// <summary>Actor's submission was blocked or defaulted at resolution: A = task number, B = 1 missed (blame) / 2 blocked by a trick / 3 absent.</summary>
        HostMissedTask = 48,
    }

    public enum BlameReason : byte
    {
        None = 0,
        Warning = 1,
        MissedTask = 2,
        ExposedSabotage = 3,
        Remedial = 4,
    }

    public enum MoneyReason : byte
    {
        None = 0,
        /// <summary>Productive task income (block-filtered).</summary>
        TaskIncome = 1,
        /// <summary>Kickbacks, commissions, offers, collector bonus (bounded per task, not block-filtered).</summary>
        DealIncome = 2,
        TransferIn = 3,
        TransferOut = 4,
        Purchase = 5,
        /// <summary>Remedial work (productive, block-filtered).</summary>
        Remedial = 6,
        /// <summary>Kayırmacı's favourite bonus (productive, block-filtered).</summary>
        Favourite = 7,
    }

    public enum MessageKind : byte
    {
        None = 0,
        Text = 1,
        Email = 2,
        MeetingChat = 3,
        /// <summary>Complaints are not delivered as messages; this kind exists for telemetry (HostMessage.A).</summary>
        Complaint = 4,
        /// <summary>IT/HR notice (trick hit): TemplateId = (int)TrickType.</summary>
        System = 5,
        /// <summary>Manager memo: TemplateId 0 = morning memo.</summary>
        Manager = 6,
    }

    public enum RecordSource : byte { None = 0, PhoneLog = 1, Bank = 2, TaskSystem = 3, ITLog = 4, BadgeLog = 5, HR = 6 }

    /// <summary>Authenticated game facts. Field meaning per kind: docs/CONTRACTS_V4.md §6.</summary>
    public enum RecordKind : byte
    {
        None = 0,
        /// <summary>PhoneLog: Actor called Subject; N1 = connected seconds.</summary>
        Call = 1,
        /// <summary>Bank: Actor paid Subject N1.</summary>
        Transfer = 2,
        /// <summary>TaskSystem: Actor's submission in a task; N1/N2/N3 = Target/A/B as entered (-1 none).</summary>
        TaskEntry = 3,
        /// <summary>TaskSystem: public outcome; N1 = output, N2 = max.</summary>
        TaskOutcome = 4,
        /// <summary>Bank: Actor received deal money N1 (kickback/offer/commission); N2 = (int)IncidentKind or 0 for a commission.</summary>
        DealPayment = 5,
        /// <summary>ITLog: Actor used trick N1 (TrickType) on Subject (-1 for everyone).</summary>
        TrickLog = 6,
        /// <summary>ITLog: Actor sent a mail that appeared to come from Subject; N1 = mail id.</summary>
        SpoofedMail = 7,
        /// <summary>BadgeLog: Actor fetched/copied archive document N1; N2 = 1 copy of the HR file.</summary>
        BadgeAccess = 8,
        /// <summary>HR: Actor's remedial report co-signed by Subject; N1 = task number; N2 = output added.</summary>
        Remedial = 9,
        /// <summary>HR: Subject received a warning (Actor = -1).</summary>
        Warning = 10,
        /// <summary>TaskSystem (report review): the audit figure N1 of Subject's department, held by Actor.</summary>
        AuditFigure = 11,
        /// <summary>TaskSystem (report review): draft history — Actor submitted version N1 (0 accurate, 1 polished), true N2, shown N3.</summary>
        ReportDraft = 12,
        /// <summary>BadgeLog (archive snoop result): Subject's objective category N1 (ObjectiveKind), visible to the copier only.</summary>
        HrFile = 13,
        /// <summary>TaskSystem (supply swap): Actor gave item type N1 to Subject.</summary>
        ItemTransfer = 14,
        /// <summary>Bank: Actor accepted an offer (N1 = (int)IncidentKind) in task number TaskNumber.</summary>
        OfferAcceptance = 15,
        /// <summary>ITLog: observable trick effect on Subject (no actor): N1 = (int)TrickType.</summary>
        TrickEffect = 16,
    }

    public enum OfferKind : byte { None = 0, BankLedger = 1, TaskAudit = 2, ITLog = 3, BadgeLog = 4 }

    public enum IncidentKind : byte
    {
        None = 0,
        InvoicePadding = 1,
        CookedReport = 2,
        ArchiveSnoop = 3,
        AcceptedCompetitorOffer = 4,
        AcceptedKickback = 5,
        VendorKickback = 6,
        TrickCoffee = 7,
        TrickPhoneCut = 8,
        TrickInternet = 9,
        TrickFakeEmail = 10,
        TrickWiretap = 11,
    }

    public enum ObjectiveKind : byte
    {
        None = 0,
        Saver = 1,
        Broker = 2,
        Patron = 3,
        TeamPlayer = 4,
        CleanRecord = 5,
        Rival = 6,
        Guardian = 7,
        Archivist = 8,
        Presenter = 9,
    }

    public enum CareerItem : byte { None = 0, NotFired = 1, CompanyTarget = 2, SecretObjective = 3, EmployeeOfMonth = 4, ProvenSabotage = 5 }

    public enum ControlKind : byte { None = 0, Number = 1, Choice = 2, Player = 3, Multi = 4, Items = 5 }

    public enum DossierKind : byte
    {
        None = 0,
        /// <summary>TaskKind/TaskNumber; N1 = output, N2 = max.</summary>
        TaskOutcome = 1,
        /// <summary>Observable trick effect: Trick, Victim (-1 everyone), Time.</summary>
        IncidentEffect = 2,
        /// <summary>Anonymous complaints are in LineMeetingView.Complaints; this line only counts them (N1).</summary>
        Complaints = 3,
        /// <summary>Arg = recording pick index.</summary>
        Recording = 4,
        /// <summary>Player = the favourite of the block.</summary>
        Favourite = 5,
        /// <summary>N1 = provisional per mille, N2 = target percent.</summary>
        Productivity = 6,
        /// <summary>Player missed a required task input (TaskNumber).</summary>
        MissedTask = 7,
        /// <summary>Player's remedial report co-signed by Victim (TaskNumber, N1 = output added).</summary>
        Remedial = 8,
    }

    public enum RecordingSelection : byte { Legacy = 0, Context = 1 }

    public enum EarlySubmitPolicy : byte { Shorten = 0, CarryToBreak = 1 }

    public enum VoteOutcome : byte
    {
        None = 0,
        WarningGiven = 1,
        /// <summary>Top nominee already at MaxWarningsPerPlayer.</summary>
        Capped = 2,
        Tie = 3,
        BelowMinimum = 4,
        NoActionWon = 5,
        Fired = 6,
        NoMajority = 7,
        CompanyClosed = 8,
        NoVotes = 9,
    }

    public enum CompanyChange : byte { None = 0, TaskResolved = 1, Remedial = 2 }
}
