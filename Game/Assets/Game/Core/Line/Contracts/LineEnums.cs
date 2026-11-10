using System;

namespace BirimizKovulacak.Core
{
    // v5 "Her Şey Kayıtta" contract (normative: docs/V5_ENGINE_SPEC.md, rationale: docs/V5_DESIGN.md).
    // Enum values are wire values: from v5 on, append, never renumber.

    public static class LineProtocol
    {
        public const int Version = 5;
        /// <summary>Ruleset identity for analytics and results; never pool data across different values.</summary>
        public const string RulesetVersion = "v5.0-kayit";
        public const int MinPlayers = 3;
        public const int MaxPlayers = 6;
    }

    /// <summary>Special participant ids (player ids are 0..5).</summary>
    public static class LineIds
    {
        public const int None = -1;
        /// <summary>Company system notices (message sender).</summary>
        public const int System = 102;
    }

    public enum LinePhase : byte
    {
        None = 0,
        Intro = 1,
        TaskPlay = 2,
        TaskReveal = 3,
        Break = 4,
        MeetingDossier = 5,
        MeetingDiscussion = 6,
        MeetingVote = 7,
        MeetingVerdict = 8,
        CrisisCalls = 9,
        CrisisVote = 10,
        CrisisVerdict = 11,
        FinalDiscussion = 12,
        FinalDefense = 13,
        /// <summary>Also used for the revote and for the second firing (see <see cref="LineFinalView"/>).</summary>
        FinalVote = 14,
        FinalVerdict = 15,
        Results = 16,
        Abandoned = 17,
    }

    /// <summary>Why the previous phase ended (PhaseChanged.B).</summary>
    public enum PhaseEndReason : byte
    {
        Timer = 0,
        /// <summary>TaskPlay ended early because every connected player had submitted.</summary>
        TimerShortened = 1,
        HostContinue = 2,
        Abandoned = 3,
        /// <summary>First phase of the match (no previous phase).</summary>
        Start = 4,
        /// <summary>The following phase(s) were skipped (meeting without vote-able failures).</summary>
        Skipped = 5,
    }

    /// <summary>v5 tasks. Localisation id: <see cref="LineTasks.Id"/>.</summary>
    public enum TaskKind : byte
    {
        None = 0,
        /// <summary>Pair/trio: Share (0) or Take (1).</summary>
        ClientBonus = 1,
        /// <summary>Pair/trio: Leave (0) or Take (1); witnesses may whistle in the following break.</summary>
        PettyCash = 2,
        /// <summary>Everyone: Skip (0) or Volunteer (1); needs ceil(N/2) volunteers.</summary>
        WeekendShift = 3,
    }

    public enum TaskFamily : byte { None = 0, Pair = 1, All = 2 }

    public static class LineTasks
    {
        public const int Count = 3;
        static readonly string[] Ids = { "", "client_bonus", "petty_cash", "weekend_shift" };
        static readonly TaskFamily[] Families = { TaskFamily.None, TaskFamily.Pair, TaskFamily.Pair, TaskFamily.All };
        /// <summary>Stable string id (localisation keys task.&lt;id&gt;.*, analytics task_id).</summary>
        public static string Id(TaskKind k) { int i = (int)k; return i >= 0 && i < Ids.Length ? Ids[i] : ""; }
        public static TaskFamily Family(TaskKind k) { int i = (int)k; return i >= 0 && i < Families.Length ? Families[i] : TaskFamily.None; }
        public static TaskKind Parse(string id) { for (int i = 1; i < Ids.Length; i++) if (Ids[i] == id) return (TaskKind)i; return TaskKind.None; }
    }

    /// <summary>Task choice values (<see cref="LineVerb.SubmitTask"/> A). 0 is always the default.</summary>
    public static class LineChoice
    {
        public const int Share = 0;
        public const int Leave = 0;
        public const int Skip = 0;
        public const int Take = 1;
        public const int Volunteer = 1;
    }

    /// <summary>The local player's phone. Dialing = I am calling and it rings at the other end; Ringing = somebody calls me.</summary>
    public enum CallStatus : byte { Idle = 0, Dialing = 1, Ringing = 2, Connected = 3 }

    public enum CallEndReason : byte
    {
        None = 0,
        HungUp = 1,
        Declined = 2,
        Missed = 3,
        /// <summary>The phase no longer allows calls.</summary>
        PhaseEnd = 4,
        Disconnected = 5,
    }

    public enum LineVerb : byte
    {
        None = 0,
        /// <summary>Target = callee.</summary>
        Call = 1,
        Answer = 2,
        Decline = 3,
        HangUp = 4,
        /// <summary>A = choice (<see cref="LineChoice"/>); changeable until TaskPlay ends.</summary>
        SubmitTask = 5,
        /// <summary>Meeting: Target = colleague or -1 (clear), A = ballot index. Crisis/final: Target = colleague/candidate or -1.</summary>
        Vote = 6,
        /// <summary>Private text: Target = recipient, Text.</summary>
        SendText = 7,
        /// <summary>Public chat (meeting, crisis vote/verdict and final phases): Text.</summary>
        SendMeetingChat = 8,
        /// <summary>Prim: Target = receiver, A = amount.</summary>
        Transfer = 9,
        /// <summary>Petty Cash break: Target = the sole taker of my group.</summary>
        Whistle = 10,
        /// <summary>Meeting 1: Target = a member of my Client Bonus group.</summary>
        Investigate = 11,
        /// <summary>Mark my current connected call as recorded.</summary>
        Record = 12,
        /// <summary>Meeting / final discussion: A = id of a call I recorded.</summary>
        Leak = 13,
        /// <summary>Host only, Results: end the match now.</summary>
        HostContinue = 14,
    }

    public enum LineFailure : byte
    {
        None = 0,
        WrongPhase = 1,
        StaleWeek = 2,
        Duplicate = 3,
        InvalidTarget = 4,
        Self = 5,
        Busy = 6,
        AlreadyUsed = 7,
        NotAvailable = 8,
        AlreadyInCall = 9,
        NotInCall = 10,
        NotRinging = 11,
        BadParam = 12,
        Absent = 13,
        HostOnly = 14,
        LimitReached = 15,
        TooLong = 16,
        EmptyText = 17,
        RateLimited = 18,
        InsufficientFunds = 19,
        /// <summary>The call/record is not mine.</summary>
        NotOwned = 20,
        /// <summary>Whistle/investigation preconditions (witness, same group) not met.</summary>
        NotEligible = 21,
        /// <summary>The theft / taker / recording was already resolved, exposed or leaked.</summary>
        AlreadyResolved = 22,
        /// <summary>Fired players take no further part in votes.</summary>
        Fired = 23,
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
        /// <summary>To a busy player: Actor tried to call you.</summary>
        MissedCall = 6,
        /// <summary>To the recipient: A = (int)MessageKind, B = message seq (pull text through ILineEngine.MessagesFor).</summary>
        MessageReceived = 7,
        /// <summary>Public: Target (re)connected (A = 1) or dropped (A = 0).</summary>
        Presence = 8,
        /// <summary>Public: Actor has voted or changed a ballot (never for whom).</summary>
        VoteCast = 9,
        /// <summary>Public: Actor submitted or changed a task choice (never which).</summary>
        TaskSubmitted = 10,
        /// <summary>To the recipient: Actor = sender, A = amount.</summary>
        TransferReceived = 11,
        /// <summary>To whistler and thief: Actor = whistler, Target = thief, A = prim taken back.</summary>
        Whistled = 12,
        /// <summary>Public: Actor investigated Target; A = 1 Target had taken / 0 Target had shared.</summary>
        Investigated = 13,
        /// <summary>Public: Actor leaked call C between A (caller) and B (callee); Target = pick index.</summary>
        Leaked = 14,
        /// <summary>Public: Target's suç rose by A (B = (int)BlameReason).</summary>
        BlameAdded = 15,
        /// <summary>Public: crisis resolved; Target = sole receiver or -1 on a tie, A = suç per receiver, B = receivers.</summary>
        CrisisResolved = 16,
        /// <summary>Public: Target fired; A = desk prim, B = recipients, C = share per recipient.</summary>
        Fired = 17,
        /// <summary>Public: recording pick A played (B = 1) or had no speech (B = 0).</summary>
        RecordingPlayed = 18,
    }

    public enum BlameReason : byte
    {
        None = 0,
        /// <summary>Meeting blame vote ballot.</summary>
        Vote = 1,
        /// <summary>Two or more Petty Cash takers in one group.</summary>
        CashCaught = 2,
        /// <summary>Whistled Petty Cash thief.</summary>
        Whistled = 3,
        InvestigationGuilty = 4,
        InvestigationFalse = 5,
        Crisis = 6,
    }

    public enum PrimReason : byte
    {
        None = 0,
        Task = 1,
        /// <summary>Whistled thief loses the stolen prim.</summary>
        Whistled = 2,
        WhistleReward = 3,
        InvestigationCost = 4,
        /// <summary>Guilty target pays the investigator (negative for the target, positive for the investigator).</summary>
        InvestigationRefund = 5,
        TransferIn = 6,
        TransferOut = 7,
        /// <summary>Share of a fired player's desk.</summary>
        DeskShare = 8,
        /// <summary>The fired player's desk is emptied.</summary>
        Fired = 9,
    }

    public enum MessageKind : byte
    {
        None = 0,
        Text = 1,
        MeetingChat = 2,
        System = 3,
    }

    /// <summary>Typed dossier/record lines. Field meaning per kind is documented on each value.</summary>
    public enum DossierKind : byte
    {
        None = 0,
        /// <summary>Task (TaskKind) failed; N1 = task number.</summary>
        TaskFailed = 1,
        /// <summary>Client Bonus: N1 = number of takers in the match.</summary>
        BonusTakers = 2,
        /// <summary>Petty Cash: Player was caught (two or more takers in one group); N1 = suç added.</summary>
        CashCaught = 3,
        /// <summary>Petty Cash: Player whistled on Other; N1 = suç added to Other.</summary>
        Whistle = 4,
        /// <summary>Petty Cash: N1 = prim missing from unresolved thefts, N2 = number of unresolved thefts.</summary>
        CashMissing = 5,
        /// <summary>Weekend Shift: N1 = volunteers, N2 = needed (roster in <see cref="LineMeetingView.Roster"/>).</summary>
        ShiftRoster = 6,
        /// <summary>Player investigated Other; N1 = 1 guilty (Other took) / 0 innocent.</summary>
        Investigation = 7,
        /// <summary>Player leaked a call between N1 (caller) and N2 (callee); Other = recording pick index.</summary>
        Leak = 8,
    }

    public enum BallotKind : byte { None = 0, Meeting = 1, Crisis = 2, Final = 3 }

    /// <summary>How a firing was decided.</summary>
    public enum FiringDecision : byte
    {
        None = 0,
        Plurality = 1,
        Revote = 2,
        HigherBlame = 3,
        HigherPrim = 4,
        Random = 5,
    }
}
