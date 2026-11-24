using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    // Per-player views (docs/V5_ENGINE_SPEC.md §11). Privacy is an allow-list: a view never contains another player's
    // prim, task choices, transfers, recorder marks or ballots before the rule that reveals them.

    /// <summary>Everything one player may know right now.</summary>
    [Serializable]
    public sealed class LineView
    {
        public int WeekId;
        /// <summary>Host-generated match id (analytics correlation).</summary>
        public string MatchId = "";
        public string RulesetVersion = LineProtocol.RulesetVersion;
        public int ProtocolVersion = LineProtocol.Version;
        public long StateVersion;
        public int Me = LineIds.None;
        /// <summary>Host time the view was built.</summary>
        public double Now;
        public LinePhase Phase;
        public double PhaseStartTime;
        public double PhaseEndTime;
        /// <summary>Meeting index (0, 1, 2 = final segment).</summary>
        public int Day;
        /// <summary>Task number 0..2 of the current/last task while a task phase or its break is running, else -1.</summary>
        public int TaskNumber = -1;
        public LineFeaturesView Features = new LineFeaturesView();
        public List<LinePlayerView> Players = new List<LinePlayerView>();
        /// <summary>Public line board: active calls (never their content), while calls are allowed.</summary>
        public List<LineBoardCall> LineBoard = new List<LineBoardCall>();
        public LineMeView Mine = new LineMeView();
        /// <summary>Current task (TaskPlay) or last task (TaskReveal, Break), else null.</summary>
        public LineTaskView Task;
        /// <summary>Current meeting (meeting 1/2 phases and the crisis/final segment), else null.</summary>
        public LineMeetingView Meeting;
        /// <summary>Crisis and final state (CrisisVote onwards), else null.</summary>
        public LineFinalView Final;
        /// <summary>Full disclosure, only in Results.</summary>
        public LineWeekResult Results;
        /// <summary>Yaramazlıklar (docs/V5_YARAMAZLIKLAR.md): the manager's patience, stains, post-its, my options and the
        /// public prank log; null when pranks are off.</summary>
        public LinePranksView Pranks;
        /// <summary>"Dosya" (v5.3): the public side of the files (the opened ones, the anonymous counters). Never null;
        /// empty under the legacy rules.</summary>
        public LineFilesView Files = new LineFilesView();
        /// <summary>"Haftanın gündemi" (v5.3): the levels and the amounts of this match, public from the Intro.</summary>
        public LineStakesView Stakes = new LineStakesView();
    }

    /// <summary>A file ("Dosya", v5.3): its holder may punish the target's offence by opening it, or shred it.</summary>
    [Serializable]
    public sealed class LineFileView
    {
        /// <summary>ClientBonus (the target took while the holder shared) or PettyCash (the target was the sole taker
        /// and the holder a witness).</summary>
        public TaskKind Task;
        public int Holder = LineIds.None;
        public int Target = LineIds.None;
        public LineFileState State;
        /// <summary>When it was created (while held), else when it was opened / shredded / voided / expired.</summary>
        public double Time;
        /// <summary>The phase <see cref="Time"/> falls in (an opened file: "when").</summary>
        public LinePhase Phase;
        /// <summary>The suç it costs the target when opened (an opened file: what it added).</summary>
        public int Blame;
    }

    /// <summary>What everybody knows about the files of the match.</summary>
    [Serializable]
    public sealed class LineFilesView
    {
        /// <summary>Every file opened so far, oldest first (public: holder, target, task, suç).</summary>
        public List<LineFileView> Opened = new List<LineFileView>();
        /// <summary>Files still held ("Açılmamış dosya: N"); -1 when the count is not public
        /// (<see cref="LineConfig.FilePublicCount"/> off, or the legacy rules).</summary>
        public int Held = -1;
        /// <summary>Files shredded / expired so far (anonymous); -1 when not public.</summary>
        public int Shredded = -1;
        public int Expired = -1;
    }

    /// <summary>The week's agenda ("Haftanın gündemi", v5.3): the level of each task and the amounts they play at.</summary>
    [Serializable]
    public sealed class LineStakesView
    {
        /// <summary><see cref="LineStakes.Fixed"/> or <see cref="LineStakes.Permutation"/>.</summary>
        public int Scheme;
        /// <summary>The drawn week type 0..5 (<see cref="LineStakes.Level"/>); -1 with fixed stakes.</summary>
        public int WeekType = -1;
        /// <summary><see cref="LineStakes"/> levels (Small, Normal, Big).</summary>
        public int LevelBonus = LineStakes.Normal;
        public int LevelCash = LineStakes.Normal;
        public int LevelShift = LineStakes.Normal;
        /// <summary>Client Bonus: the pot (twice the both-share amount), each one's prim when both share / nobody takes,
        /// a sole taker's prim, and each one's when two or more take.</summary>
        public int BonusPot;
        public int BonusShare;
        public int BonusTaker;
        public int BonusBoth;
        /// <summary>Petty Cash: the box.</summary>
        public int CashTake;
        /// <summary>Weekend Shift: a skipper's pay when the shift is staffed.</summary>
        public int ShiftSkipPay;

        public int Level(TaskKind task)
        {
            return task == TaskKind.ClientBonus ? LevelBonus : task == TaskKind.PettyCash ? LevelCash : task == TaskKind.WeekendShift ? LevelShift : LineStakes.Normal;
        }
    }

    [Serializable]
    public sealed class LineFeaturesView
    {
        public bool RecordingsEnabled;
        public bool CallsOpen;
        /// <summary>Everyone hears everyone (false = calls phase, 1:1 voice only).</summary>
        public bool OpenVoice;
        public bool TransfersOpen;
        public bool TextsOpen;
        public bool MeetingChatOpen;
        public bool WhistleOpen;
        public bool InvestigationOpen;
        public bool LeakOpen;
        public bool VoteOpen;
        /// <summary>Yaramazlıklar are on in this match (lobby option).</summary>
        public bool PranksEnabled;
        /// <summary>A prank session runs (the meeting room phases; Results: cake only).</summary>
        public bool PranksOpen;
        /// <summary>I may declare my prim right now (<see cref="LineVerb.Declare"/>): I am a candidate, I have not declared
        /// and the window is open (until <see cref="LineFinalView.DeclareUntil"/>).</summary>
        public bool DeclareOpen;
        /// <summary>"Dosya" (v5.3): files are in play and a held one can still be opened or shredded (from the Intro
        /// until the crisis calls end).</summary>
        public bool FilesOpen;
    }

    [Serializable]
    public sealed class LinePlayerView
    {
        public int Id;
        public string Name = "";
        public int ColorIndex;
        public bool Connected;
        public bool IsBot;
        /// <summary>suç (public).</summary>
        public int Blame;
        /// <summary>Submitted a choice in the current task (never which).</summary>
        public bool Submitted;
        /// <summary>Finished voting in the current vote (never for whom).</summary>
        public bool Voted;
        public bool Candidate;
        public bool Fired;
    }

    [Serializable]
    public sealed class LineBoardCall
    {
        public int CallId;
        public int Caller;
        public int Callee;
        /// <summary>Ringing or Connected.</summary>
        public CallStatus Status;
        public double Start;
    }

    [Serializable]
    public sealed class LineMeView
    {
        /// <summary>My prim (private until Results).</summary>
        public int Prim;
        public LinePhoneView Phone = new LinePhoneView();
        /// <summary>My calls (connected or not), oldest first.</summary>
        public List<LineCallRecordView> CallLog = new List<LineCallRecordView>();
        /// <summary>Transfers I sent or received.</summary>
        public List<LineTransferView> Transfers = new List<LineTransferView>();
        public int RecorderUsesLeft;
        public List<LineRecordedCallView> Recorded = new List<LineRecordedCallView>();
        public bool InvestigationUsed;
        /// <summary>My ballots in the current vote (-1 = empty). Meeting: one per vote-able failure; crisis/final: one.</summary>
        public List<int> Ballots = new List<int>();
        /// <summary>My group in the current/last task (me included).</summary>
        public List<int> Group = new List<int>();
        /// <summary>Petty Cash break: thieves I can whistle on.</summary>
        public List<int> WitnessOf = new List<int>();
        /// <summary>Meeting 1: group-mates I may investigate.</summary>
        public List<int> InvestigationTargets = new List<int>();
        public long MessageSeq;
        public int TextsLeft;
        public int MeetingChatLeft;
        public double NextMessageAt;
        public double NextTransferAt;
        /// <summary>"Dosya" (v5.3): every file I hold and every file on me, whatever became of it, oldest first. (A
        /// target knows who shared or watched in their own group, so who holds a file on them is no secret; a shredding
        /// is told to nobody else.)</summary>
        public List<LineFileView> Files = new List<LineFileView>();
    }

    [Serializable]
    public sealed class LinePhoneView
    {
        public CallStatus Status;
        /// <summary>The other party (Dialing/Ringing/Connected), else -1.</summary>
        public int Other = LineIds.None;
        /// <summary>Call id once connected, else -1.</summary>
        public int CallId = LineIds.None;
        public double Since;
        /// <summary>Dialing/Ringing: when the call counts as missed.</summary>
        public double RingUntil;
    }

    [Serializable]
    public sealed class LineCallRecordView
    {
        public int CallId;
        public int Other;
        public bool Outgoing;
        public bool Connected;
        public double Start;
        /// <summary>-1 while the call is active.</summary>
        public double End = -1;
        public CallEndReason EndReason;
    }

    [Serializable]
    public sealed class LineTransferView
    {
        public int From;
        public int To;
        public int Amount;
        public double Time;
        public LinePhase Phase;
    }

    [Serializable]
    public sealed class LineRecordedCallView
    {
        public int CallId;
        public int Other;
        public double Start;
        public bool Leaked;
    }

    [Serializable]
    public sealed class LineTaskView
    {
        public TaskKind Kind;
        /// <summary>0..2.</summary>
        public int Number;
        public TaskFamily Family;
        public double PlayEndTime;
        /// <summary>My group (me included); everyone for Weekend Shift.</summary>
        public List<int> Group = new List<int>();
        /// <summary>Weekend Shift: volunteers needed (public); else -1.</summary>
        public int Needed = -1;
        public bool Submitted;
        /// <summary>My choice so far (the default 0 when not submitted).</summary>
        public int MyChoice;
        public bool Revealed;
        /// <summary>After reveal: every group member's choice (Payout -1 for others).</summary>
        public List<LineChoiceView> Choices = new List<LineChoiceView>();
        /// <summary>After reveal: my prim from this task, else -1.</summary>
        public int MyPayout = -1;
        /// <summary>Client Bonus, after reveal: takers in the whole match (public), else -1.</summary>
        public int Takers = -1;
        /// <summary>Weekend Shift, after reveal: the public volunteer roster.</summary>
        public List<int> Roster = new List<int>();
    }

    [Serializable]
    public sealed class LineChoiceView
    {
        public int TaskNumber;
        public TaskKind Task;
        public int Player;
        public int Choice;
        /// <summary>Prim from the task (-1 when not visible).</summary>
        public int Payout = -1;
    }

    [Serializable]
    public sealed class LineMeetingView
    {
        /// <summary>0 = meeting 1, 1 = meeting 2, 2 = crisis/final segment.</summary>
        public int Index;
        public List<TaskKind> FailedTasks = new List<TaskKind>();
        /// <summary>Ballots per player in the blame vote (-1 until counted when the vote starts).</summary>
        public int VoteableCount = -1;
        /// <summary>Typed public lines, in order.</summary>
        public List<LineDossierEntry> Dossier = new List<LineDossierEntry>();
        /// <summary>Weekend Shift roster (meeting 2).</summary>
        public List<int> Roster = new List<int>();
        public List<LineRecordingPick> Recordings = new List<LineRecordingPick>();
        /// <summary>MeetingVerdict onwards: suç received per player from this meeting's vote.</summary>
        public List<LineBlameGainView> BlameGained = new List<LineBlameGainView>();
    }

    [Serializable]
    public sealed class LineDossierEntry
    {
        public DossierKind Kind;
        public TaskKind Task;
        public int Player = LineIds.None;
        public int Other = LineIds.None;
        public int N1 = -1;
        public int N2 = -1;
        public double Time;
    }

    [Serializable]
    public sealed class LineBlameGainView
    {
        public int Player;
        public int Amount;
    }

    /// <summary>A leaked call recording to play. The voice system reports whether it had speech.</summary>
    [Serializable]
    public sealed class LineRecordingPick
    {
        public int Index;
        /// <summary>v5: exactly the leaked call.</summary>
        public List<int> CandidateCallIds = new List<int>();
        public double PlayAt;
        public float Seconds;
        /// <summary>Reported by the voice system (-1 until played or when empty).</summary>
        public int ChosenCallId = LineIds.None;
        public int Caller = LineIds.None;
        public int Callee = LineIds.None;
        public double CallStart = -1;
        public int Leaker = LineIds.None;
        public bool Played;
        /// <summary>Reported with no speech found ("kayıt boş").</summary>
        public bool Empty;
    }

    [Serializable]
    public sealed class LineFinalView
    {
        /// <summary>CrisisVerdict onwards: suç each receiver got from the crisis.</summary>
        public List<LineBlameGainView> Crisis = new List<LineBlameGainView>();
        /// <summary>Fixed at CrisisVerdict.</summary>
        public List<int> Candidates = new List<int>();
        /// <summary>suç desc, then seat id.</summary>
        public List<int> DefenseOrder = new List<int>();
        public int Defender = LineIds.None;
        public double DefenderEndTime;
        /// <summary>0 = first firing, 1 = second firing (5-6 players).</summary>
        public int Round;
        public bool Revote;
        /// <summary>Candidates the current final vote may name.</summary>
        public List<int> Options = new List<int>();
        public int FiringsTotal;
        public List<LineFiringView> Firings = new List<LineFiringView>();
        /// <summary>Prim declarations (v5.2): host time until which the candidates may declare (the final discussion's
        /// start + <see cref="LineConfig.DeclareSeconds"/>); 0 when declarations are off or there is no candidate.</summary>
        public double DeclareUntil;
        /// <summary>My own declaration (a candidate sees it before the reveal too), -1 = none.</summary>
        public int MyDeclaration = -1;
        /// <summary>The declarations are public (from the reveal to the end of the match).</summary>
        public bool DeclarationsRevealed;
        /// <summary>After the reveal: one entry per candidate in candidate order (Amount -1 = no declaration). Empty before
        /// it: a declaration is sealed, nobody else sees whether or what a candidate declared.</summary>
        public List<LineDeclarationView> Declarations = new List<LineDeclarationView>();
    }

    /// <summary>A candidate's prim declaration ("prim beyanı").</summary>
    [Serializable]
    public sealed class LineDeclarationView
    {
        public int Player;
        /// <summary>The prim the candidate claims to have; -1 = did not declare.</summary>
        public int Amount = -1;
        /// <summary>The candidate's real prim when the declarations were revealed. Only in
        /// <see cref="LineWeekResult.Declarations"/> (full disclosure); -1 in every view before Results.</summary>
        public int Actual = -1;
    }

    /// <summary>A task group (a pair or a trio) for the full disclosure: who played a pair task with whom.</summary>
    [Serializable]
    public sealed class LineGroupView
    {
        public int TaskNumber;
        public TaskKind Task;
        public List<int> Members = new List<int>();
    }

    [Serializable]
    public sealed class LineFiringView
    {
        public int Player;
        public int Round;
        public FiringDecision Decision;
        /// <summary>Votes received in the deciding vote.</summary>
        public int Votes;
        /// <summary>Prim found on the desk (public).</summary>
        public int Desk;
        public List<int> Recipients = new List<int>();
        public int Share;
        /// <summary>Prim removed from the game (remainder or no recipients).</summary>
        public int Removed;
        /// <summary>The prim the fired candidate had declared (v5.2); -1 = no declaration (or declarations are off).</summary>
        public int Declared = -1;
        /// <summary>The fired candidate's real prim when the declarations were revealed (v5.2): what
        /// <see cref="Declared"/> is judged against (<see cref="LineStory.Truthful"/>). The desk can hold more: a
        /// candidate still receives prim after the reveal (a share of an earlier desk, a transfer), and that never
        /// turns a true declaration into a lie. -1 = declarations were not revealed (or are off).</summary>
        public int DeclaredActual = -1;
        /// <summary>Host time of the firing (v5.2; the deciding vote's end).</summary>
        public double Time;
    }

    /// <summary>Free-text and system messages, delivered per recipient outside the view (ILineEngine.MessagesFor).</summary>
    [Serializable]
    public sealed class LineMessage
    {
        /// <summary>Per-recipient increasing sequence number (from 1).</summary>
        public long Seq;
        /// <summary>Global message id (shared by every copy of a broadcast).</summary>
        public int Id;
        public MessageKind Kind;
        /// <summary>Sender, or LineIds.System.</summary>
        public int From = LineIds.None;
        /// <summary>Recipient, or -1 when sent to everyone.</summary>
        public int To = LineIds.None;
        public bool ToAll;
        /// <summary>Player-authored text (sanitised by the engine). Never logged, never sent to telemetry.</summary>
        public string Text = "";
        public double Time;
    }

    /// <summary>Full disclosure at Results.</summary>
    [Serializable]
    public sealed class LineWeekResult
    {
        public int WeekId;
        public string MatchId = "";
        public string RulesetVersion = LineProtocol.RulesetVersion;
        public int PlayerCount;
        public double StartTime;
        /// <summary>When Results began.</summary>
        public double EndTime;
        public List<LineWeekPlayerResult> Players = new List<LineWeekPlayerResult>();
        /// <summary>Fired players in firing order.</summary>
        public List<int> Fired = new List<int>();
        /// <summary>Highest prim among the non-fired (several on equal prim).</summary>
        public List<int> Winners = new List<int>();
        public List<LineFiringView> Firings = new List<LineFiringView>();
        public List<LineBallotView> Ballots = new List<LineBallotView>();
        public List<LineTransferView> Transfers = new List<LineTransferView>();
        public List<LineChoiceView> Choices = new List<LineChoiceView>();
        /// <summary>Opened files (legacy rules: whistles, investigations), catches and leaks.</summary>
        public List<LineDossierEntry> Incidents = new List<LineDossierEntry>();
        public List<LinePrimChange> PrimLog = new List<LinePrimChange>();
        public List<LineBlameChange> BlameLog = new List<LineBlameChange>();
        /// <summary>Deterministic hash of the outcome (all clients must agree; used by automated runs).</summary>
        public ulong ResultHash;
        /// <summary>v5.2: every candidate's declaration against their real prim at the reveal (candidate order; empty when
        /// declarations are off or nobody was a candidate).</summary>
        public List<LineDeclarationView> Declarations = new List<LineDeclarationView>();
        /// <summary>v5.2: the groups of the pair tasks (who could rob whom).</summary>
        public List<LineGroupView> Groups = new List<LineGroupView>();
        /// <summary>v5.3: every file of the match and what became of it (creation order).</summary>
        public List<LineFileView> Files = new List<LineFileView>();
        /// <summary>v5.3: the week's agenda the match was played at.</summary>
        public LineStakesView Stakes = new LineStakesView();
    }

    [Serializable]
    public sealed class LineWeekPlayerResult
    {
        public int Player;
        public int Prim;
        public int Blame;
        public bool Fired;
        public bool Winner;
        public bool WasCandidate;
        /// <summary>Recordings made / leaked.</summary>
        public int Recorded;
        public int Leaked;
    }

    [Serializable]
    public sealed class LineBallotView
    {
        public BallotKind Kind;
        /// <summary>Meeting index (meeting ballots) or final round (final ballots).</summary>
        public int Round;
        public bool Revote;
        public int Voter;
        /// <summary>Ballot index (meeting) else 0.</summary>
        public int Index;
        /// <summary>-1 = abstained (final only).</summary>
        public int Target = LineIds.None;
        /// <summary>An empty ballot assigned to the voter themselves.</summary>
        public bool SelfAssigned;
    }

    [Serializable]
    public sealed class LinePrimChange
    {
        public int Player;
        public int Delta;
        public PrimReason Reason;
        /// <summary>Counterparty (transfer, refund, desk), else -1.</summary>
        public int Other = LineIds.None;
        public double Time;
    }

    [Serializable]
    public sealed class LineBlameChange
    {
        public int Player;
        public int Delta;
        public BlameReason Reason;
        public double Time;
    }

    // ---- Yaramazlıklar (docs/V5_YARAMAZLIKLAR.md)

    /// <summary>The manager's patience and the pranks of the match as one player may see them.</summary>
    [Serializable]
    public sealed class LinePranksView
    {
        /// <summary><see cref="LinePranks.SessionOf"/> of the current phase (-1 outside the meeting room).</summary>
        public int Session = -1;
        /// <summary>0..capacity-1 (resets every session and after a tutanak).</summary>
        public int Patience;
        /// <summary>0 calm, 1 frowning, 2 red (<see cref="LinePranks.Level"/>).</summary>
        public int Level;
        /// <summary>Patience costs are multiplied right now (the final defence).</summary>
        public int Multiplier = 1;
        /// <summary>Per seat id 0..5: the post-it word on that colleague's forehead this session, -1 none.</summary>
        public List<int> PostIts = new List<int>();
        /// <summary>Per seat id: coffee stains (they stay for the whole match).</summary>
        public List<int> Stains = new List<int>();
        /// <summary>Per seat id: cakes in the face (Results).</summary>
        public List<int> Cakes = new List<int>();
        /// <summary>My pranks (one per kind, Coffee..Cake): price, patience, uses left and why it is closed right now.</summary>
        public List<LinePrankOptionView> Options = new List<LinePrankOptionView>();
        /// <summary>Every prank of the match, oldest first. A phone ring's Actor is -1 for everyone but its actor until Results;
        /// a paper plane's Note is only in the target's and the sender's view (never revealed).</summary>
        public List<LinePrankEntryView> Log = new List<LinePrankEntryView>();
    }

    [Serializable]
    public sealed class LinePrankOptionView
    {
        public PrankKind Kind;
        /// <summary>Prim it burns (0 = free).</summary>
        public int Cost;
        /// <summary>Patience it costs right now (×2 in the defence); negative: the relief of flattering.</summary>
        public int Patience;
        /// <summary>Uses left in this session (Cake: in the match); -1 = no limit.</summary>
        public int UsesLeft = -1;
        /// <summary>Host time when its cooldown ends (0 = none).</summary>
        public double ReadyAt;
        /// <summary>Why it cannot be done right now regardless of the target (None = available): WrongPhase, Fired, DeskSealed,
        /// AlreadyUsed, LimitReached, RateLimited, InsufficientFunds, NotAvailable.</summary>
        public LineFailure Block;
    }

    [Serializable]
    public sealed class LinePrankEntryView
    {
        public int Index;
        public PrankKind Kind;
        /// <summary>-1 while hidden (a phone ring before Results, for everyone but its actor).</summary>
        public int Actor = LineIds.None;
        public int Target = LineIds.None;
        /// <summary>Post-it word, else -1.</summary>
        public int Word = -1;
        /// <summary>Paper plane note: only for the target and the sender, else "".</summary>
        public string Note = "";
        public int Session = -1;
        public LinePhase Phase;
        public double Time;
        /// <summary>Prim burned.</summary>
        public int Cost;
        /// <summary>Patience it added (negative: flattering's relief).</summary>
        public int Patience;
        /// <summary>The manager's patience after it (0 after a tutanak).</summary>
        public int PatienceAfter;
        /// <summary>Who the patience was charged to (the actor; the target for a phone ring), -1 when none.</summary>
        public int Culprit = LineIds.None;
        /// <summary>It overflowed the patience: the culprit got a tutanak.</summary>
        public bool Tutanak;
    }
}
