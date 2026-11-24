using System;

namespace BirimizKovulacak.Core
{
    // v5 "Her Şey Kayıtta" contract (normative: docs/V5_ENGINE_SPEC.md, rationale: docs/V5_DESIGN.md).
    // Enum values are wire values: from v5 on, append, never renumber.

    public static class LineProtocol
    {
        /// <summary>Wire format version (6: Yaramazlıklar added the Prank verb and the pranks view; 7: the v5.1 rules added
        /// BlameReason.ShiftClean / ShiftFailSkip and DossierKind.ShiftSkipBlame; 8: v5.2 added the Declare verb, the
        /// DeclarationsRevealed event, the declaration / group views, the talk-time option and the new config fields;
        /// 9: v5.3 added the FileOpen / FileShred verbs, the file events, views and dossier line and the week's stakes
        /// view, and moved the one-shot investigation / whistle numbers to <see cref="LineConfig.Legacy"/>).</summary>
        public const int Version = 9;
        /// <summary>Ruleset identity for analytics and results; never pool data across different values. Yaramazlıklar is a
        /// lobby option (like recordings) and does not change it. v5.1 (2026-11-15): Client Bonus 3/3, free sharer-only
        /// investigation (+4 suç), the whistled thief keeps the cash (no whistle reward), shift cleaning (−1 suç) and the
        /// failed shift's automatic suç (no meeting 2 vote), 0 suç is never a candidate. v5.2 (2026-11-24): the candidates'
        /// prim declarations ("prim beyanı"), no meeting 2 (the short shift's suç lands at its reveal): one meeting before
        /// the final. v5.3 (2026-11-24): "Dosya" (a persistent file its holder may open or shred until the crisis calls
        /// end replaces the one-shot investigation and whistle) and "Haftanın gündemi" (one task small, one normal, one
        /// big per match: the prizes change, the suç prices never do).</summary>
        public const string RulesetVersion = "v5.3-kayit";
        /// <summary>What the engine, the tests and LineSim support.</summary>
        public const int MinPlayers = 3;
        public const int MaxPlayers = 6;
        /// <summary>Occupied seats (people + bots) the lobby needs to start a match (v5.2): with three players both pair
        /// tasks are played by the same trio.</summary>
        public const int LobbyMinPlayers = 4;
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
        /// <summary>Pair/trio: Leave (0) or Take (1); the witnesses of a sole taker hold a file on them (v5.3).</summary>
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
        /// <summary>Legacy rules only (<see cref="LineConfig.Legacy"/>; v5.3 has <see cref="FileOpen"/>). Petty Cash
        /// break: Target = the sole taker of my group.</summary>
        Whistle = 10,
        /// <summary>Legacy rules only (<see cref="LineConfig.Legacy"/>; v5.3 has <see cref="FileOpen"/>). Meeting 1:
        /// Target = a member of my Client Bonus group (v5.1: I shared and the target took).</summary>
        Investigate = 11,
        /// <summary>Mark my current connected call as recorded.</summary>
        Record = 12,
        /// <summary>Meeting / final discussion: A = id of a call I recorded.</summary>
        Leak = 13,
        /// <summary>Host only, Results: end the match now.</summary>
        HostContinue = 14,
        /// <summary>Meeting room prank (docs/V5_YARAMAZLIKLAR.md): A = (int)<see cref="PrankKind"/>, Target = colleague (-1 for
        /// Slurp and Flatter, optional for StandUp), B = post-it word (0..<see cref="LinePranks.WordCount"/>-1), Text = the
        /// paper plane's note, C = the patience the sender saw (optional guard: -1 = no check).</summary>
        Prank = 15,
        /// <summary>Prim declaration ("prim beyanı", v5.2): A = the prim I claim to have (0..<see cref="LineConfig.DeclareMax"/>;
        /// it may be a lie). Candidates only, once each, from CrisisVerdict until the declaration deadline in the final
        /// discussion. No effect on prim, suç or votes.</summary>
        Declare = 16,
        /// <summary>"Dosya" (v5.3): open a file I hold. Target = the colleague it is on, A = (int)<see cref="TaskKind"/>
        /// (ClientBonus or PettyCash). From that task's reveal until the crisis calls end. Free; the target gets the
        /// file's suç at once and publicly, the other files on the same offence become void.</summary>
        FileOpen = 17,
        /// <summary>"Dosya" (v5.3): shred a file I hold (irreversible; only the target is told who did it). Target and A
        /// as <see cref="FileOpen"/>.</summary>
        FileShred = 18,
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
        /// <summary>Whistle / investigation preconditions (witness, same group) not met; I hold no such file.</summary>
        NotEligible = 21,
        /// <summary>The theft / taker / recording was already resolved, exposed or leaked; the file is no longer held.</summary>
        AlreadyResolved = 22,
        /// <summary>Fired players take no further part in votes.</summary>
        Fired = 23,
        /// <summary>Final candidates' desks are sealed: they may receive prim but not send it (or burn it on pranks).</summary>
        DeskSealed = 24,
        /// <summary>Prank: the manager's patience is no longer what the sender saw (<see cref="LineVerb.Prank"/> C guard).</summary>
        PatienceChanged = 25,
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
        /// <summary>Legacy rules only. To whistler and thief: Actor = whistler, Target = thief, A = prim taken back (0 when
        /// the thief keeps it, <see cref="LineLegacyRules.CashWhistledKeeps"/>).</summary>
        Whistled = 12,
        /// <summary>Legacy rules only. Public: Actor investigated Target; A = 1 Target had taken / 0 Target had shared.</summary>
        Investigated = 13,
        /// <summary>Public: Actor leaked call C between A (caller) and B (callee); Target = pick index.</summary>
        Leaked = 14,
        /// <summary>Public: Target's suç changed by A (B = (int)BlameReason). A is negative only for
        /// <see cref="BlameReason.ShiftClean"/> (a volunteer's suç going down).</summary>
        BlameAdded = 15,
        /// <summary>Public: crisis resolved; Target = sole receiver or -1 on a tie, A = suç per receiver, B = receivers.</summary>
        CrisisResolved = 16,
        /// <summary>Public: Target fired; A = desk prim, B = recipients, C = share per recipient.</summary>
        Fired = 17,
        /// <summary>Public: recording pick A played (B = 1) or had no speech (B = 0).</summary>
        RecordingPlayed = 18,
        /// <summary>Public: Actor pranked Target (A = (int)PrankKind, B = post-it word or -1, C = prank log index). A phone ring
        /// hides its Actor (-1) from everyone but the actor until Results.</summary>
        Prank = 19,
        /// <summary>Public: the manager's patience changed: A = patience now, B = level now, C = level before; Actor = the
        /// prank's actor (-1 when hidden), Target = who the patience was charged to.</summary>
        Patience = 20,
        /// <summary>Public: the manager's patience overflowed: Target gets a tutanak (+B suç); A = (int)PrankKind that did it,
        /// Actor = its actor (-1 when hidden).</summary>
        Tutanak = 21,
        /// <summary>Public: the candidates' prim declarations are revealed (all at once): A = declarations made,
        /// B = candidates. The amounts are in <see cref="LineFinalView.Declarations"/>.</summary>
        DeclarationsRevealed = 22,
        /// <summary>"Dosya" (v5.3). Public: Actor opened its file on Target; A = (int)TaskKind, B = suç added.</summary>
        FileOpened = 23,
        /// <summary>"Dosya". To the holder and to the target only: Actor shredded its file on Target; A = (int)TaskKind.</summary>
        FileShredded = 24,
        /// <summary>"Dosya", the anonymous public notice (<see cref="LineConfig.FilePublicCount"/>): A = what happened
        /// ((int)<see cref="LineFileState"/>: Shredded or Expired), B = how many files, C = files still held. A shredding
        /// goes to everyone but its holder and target (they get <see cref="FileShredded"/>); an expiry to everyone.</summary>
        FileNotice = 25,
    }

    public enum BlameReason : byte
    {
        None = 0,
        /// <summary>Meeting blame vote ballot.</summary>
        Vote = 1,
        /// <summary>Two or more Petty Cash takers in one group.</summary>
        CashCaught = 2,
        /// <summary>A sole Petty Cash thief: a witness opened the file on them (v5.3; legacy rules: whistled).</summary>
        Whistled = 3,
        /// <summary>A Client Bonus taker: a sharer opened the file on them (v5.3; legacy rules: investigated).</summary>
        InvestigationGuilty = 4,
        /// <summary>Legacy v5.0 rules only: the investigated colleague had shared.</summary>
        InvestigationFalse = 5,
        Crisis = 6,
        /// <summary>A prank overflowed the manager's patience (Yaramazlıklar).</summary>
        Tutanak = 7,
        /// <summary>Weekend Shift volunteer: suç removed (a negative change, never below 0) when the shift resolves.</summary>
        ShiftClean = 8,
        /// <summary>Skipped a Weekend Shift that was not staffed (v5.2: applied at the shift's reveal; with
        /// <see cref="LineConfig.ShiftMeeting"/>: when meeting 2 opens).</summary>
        ShiftFailSkip = 9,
    }

    public enum PrimReason : byte
    {
        None = 0,
        Task = 1,
        /// <summary>Whistled thief loses the stolen prim (v5.0 rules only: <see cref="LineLegacyRules.CashWhistledKeeps"/> false).</summary>
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
        /// <summary>Prim burned on a prank (removed from the game; Other = the prank's target or -1).</summary>
        Prank = 10,
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
        /// <summary>Legacy rules only. Petty Cash: Player whistled on Other; N1 = suç added to Other.</summary>
        Whistle = 4,
        /// <summary>Petty Cash: N1 = prim missing from unresolved thefts, N2 = number of unresolved thefts.</summary>
        CashMissing = 5,
        /// <summary>Weekend Shift: N1 = volunteers, N2 = needed (roster in <see cref="LineMeetingView.Roster"/>).</summary>
        ShiftRoster = 6,
        /// <summary>Legacy rules only. Player investigated Other; N1 = 1 guilty (Other took) / 0 innocent.</summary>
        Investigation = 7,
        /// <summary>Player leaked a call between N1 (caller) and N2 (callee); Other = recording pick index.</summary>
        Leak = 8,
        /// <summary>Weekend Shift not staffed: every skipper got N1 suç; N2 = skippers (everyone not in
        /// <see cref="LineMeetingView.Roster"/>). v5.2: in the crisis / final segment's dossier (the suç landed at the
        /// shift's reveal); with <see cref="LineConfig.ShiftMeeting"/>: in meeting 2's.</summary>
        ShiftSkipBlame = 9,
        /// <summary>"Dosya" (v5.3): Player opened its file on Other; Task = the task of the offence (ClientBonus: Other
        /// took while Player shared; PettyCash: Other was the sole taker), N1 = suç added to Other. In the dossier of the
        /// meeting room segment it happened in (or the next one to open) and in the results' incidents.</summary>
        FileOpened = 10,
    }

    public enum BallotKind : byte { None = 0, Meeting = 1, Crisis = 2, Final = 3 }

    /// <summary>What became of a file ("Dosya", v5.3).</summary>
    public enum LineFileState : byte
    {
        /// <summary>Still in its holder's hands: it can be opened or shredded.</summary>
        Held = 0,
        /// <summary>Opened: the target got its suç, publicly.</summary>
        Opened = 1,
        /// <summary>Shredded by its holder (only the holder and the target know who).</summary>
        Shredded = 2,
        /// <summary>Another holder opened a file on the same offence first.</summary>
        Void = 3,
        /// <summary>Still held when the crisis calls ended.</summary>
        Expired = 4,
    }

    /// <summary>"Haftanın gündemi" (v5.3): the stakes level of each task in a match and how the levels are drawn.
    /// Localisation id: <see cref="Id"/>.</summary>
    public static class LineStakes
    {
        public const int Small = 0, Normal = 1, Big = 2;
        /// <summary><see cref="LineConfig.StakesScheme"/>: every task plays at the config's own numbers (the normal level).</summary>
        public const int Fixed = 0;
        /// <summary><see cref="LineConfig.StakesScheme"/>: a seeded permutation gives one task the small level, one the
        /// normal and one the big (six week types).</summary>
        public const int Permutation = 1;
        public const int WeekTypes = 6;
        static readonly string[] Ids = { "small", "normal", "big" };
        /// <summary>Levels of { Client Bonus, Petty Cash, Weekend Shift } per week type.</summary>
        static readonly int[][] Weeks =
        {
            new[] { Small, Normal, Big }, new[] { Small, Big, Normal }, new[] { Normal, Small, Big },
            new[] { Normal, Big, Small }, new[] { Big, Small, Normal }, new[] { Big, Normal, Small },
        };
        public static bool Valid(int level) { return level >= Small && level <= Big; }
        /// <summary>Stable string id (localisation v5.stakes.&lt;task&gt;.&lt;id&gt;, logs).</summary>
        public static string Id(int level) { return Valid(level) ? Ids[level] : ""; }
        /// <summary>The level of a task (ClientBonus, PettyCash, WeekendShift) in a week type (0..5); Normal for anything else.</summary>
        public static int Level(int weekType, TaskKind task)
        {
            int i = (int)task - 1;
            return weekType >= 0 && weekType < Weeks.Length && i >= 0 && i < 3 ? Weeks[weekType][i] : Normal;
        }
    }

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

    /// <summary>Lobby option "Konuşma süresi" (v5.2): how long the talking phases last
    /// (<see cref="LineDurations.ScaleTalk"/>: MeetingDiscussion, CrisisCalls, FinalDiscussion, FinalDefense).</summary>
    public static class LineTalkTime
    {
        public const int Short = 0, Normal = 1, Long = 2;
        public const int Count = 3;
        static readonly string[] Ids = { "short", "normal", "long" };
        public static bool Valid(int level) { return level >= Short && level <= Long; }
        /// <summary>Stable string id (localisation lobby.talk.&lt;id&gt;, logs).</summary>
        public static string Id(int level) { return Valid(level) ? Ids[level] : ""; }
    }

    /// <summary>Meeting room pranks (docs/V5_YARAMAZLIKLAR.md). Localisation id: <see cref="LinePranks.Id"/>.</summary>
    public enum PrankKind : byte
    {
        None = 0,
        /// <summary>A mug flies to the target: a coffee stain for the rest of the match. Prim, once per session.</summary>
        Coffee = 1,
        /// <summary>A paper plane lands on the target's table with a one-line note only the target (and the sender) reads.</summary>
        Plane = 2,
        /// <summary>A post-it with a ready-made word on the target's forehead until the session ends.</summary>
        PostIt = 3,
        /// <summary>Secret: the target's phone rings; the manager blames the target (the actor is revealed at Results).</summary>
        PhoneRing = 4,
        /// <summary>Free, cooldown: a loud slurp of tea.</summary>
        Slurp = 5,
        /// <summary>Free, cooldown: stand up, slam the table, point at a colleague (optional target).</summary>
        StandUp = 6,
        /// <summary>Flatter the manager: patience goes down.</summary>
        Flatter = 7,
        /// <summary>Results only: a cake in the target's face (free, cooldown, a few per player).</summary>
        Cake = 8,
    }

    public static class LinePranks
    {
        public const int KindCount = 8;
        /// <summary>Post-it words (localisation v5.prank.word.&lt;id&gt;): YALANCI, HIRSIZ?, MASUM, ZENGİN, SESSİZ.</summary>
        public const int WordCount = 5;
        public const int WordLiar = 0, WordThief = 1, WordInnocent = 2, WordRich = 3, WordQuiet = 4;
        /// <summary>Sessions: the meeting (meeting 1), meeting 2 (only with <see cref="LineConfig.ShiftMeeting"/>), the crisis
        /// vote (vote + verdict), the final (discussion .. last verdict), Results.</summary>
        public const int SessionNone = -1, SessionMeeting1 = 0, SessionMeeting2 = 1, SessionCrisis = 2, SessionFinal = 3, SessionResults = 4;
        static readonly string[] Ids = { "", "coffee", "plane", "postit", "phone", "slurp", "standup", "flatter", "cake" };
        static readonly string[] Words = { "liar", "thief", "innocent", "rich", "quiet" };

        /// <summary>Stable string id (localisation v5.prank.&lt;id&gt;.*, logs).</summary>
        public static string Id(PrankKind k) { int i = (int)k; return i >= 0 && i < Ids.Length ? Ids[i] : ""; }
        public static string WordId(int word) { return word >= 0 && word < Words.Length ? Words[word] : ""; }
        public static bool Valid(PrankKind k) { return k >= PrankKind.Coffee && k <= PrankKind.Cake; }
        /// <summary>The prank needs a colleague as its target (StandUp may name one; Slurp and Flatter never do).</summary>
        public static bool NeedsTarget(PrankKind k) { return k == PrankKind.Coffee || k == PrankKind.Plane || k == PrankKind.PostIt || k == PrankKind.PhoneRing || k == PrankKind.Cake; }
        public static bool TakesTarget(PrankKind k) { return NeedsTarget(k) || k == PrankKind.StandUp; }

        /// <summary>The prank session of a phase (<paramref name="day"/> = meeting index), -1 outside the meeting room.</summary>
        public static int SessionOf(LinePhase phase, int day)
        {
            switch (phase)
            {
                case LinePhase.MeetingDossier:
                case LinePhase.MeetingDiscussion:
                case LinePhase.MeetingVote:
                case LinePhase.MeetingVerdict: return day <= 0 ? SessionMeeting1 : SessionMeeting2;
                case LinePhase.CrisisVote:
                case LinePhase.CrisisVerdict: return SessionCrisis;
                case LinePhase.FinalDiscussion:
                case LinePhase.FinalDefense:
                case LinePhase.FinalVote:
                case LinePhase.FinalVerdict: return SessionFinal;
                case LinePhase.Results: return SessionResults;
                default: return SessionNone;
            }
        }

        /// <summary>The manager's mood: 0 calm, 1 frowning, 2 red (thirds of the capacity: 0-1 / 2-3 / 4-5 for 6).</summary>
        public static int Level(int patience, int capacity)
        {
            if (capacity <= 0 || patience <= 0) return 0;
            return Math.Min(2, patience * 3 / capacity);
        }
    }
}
