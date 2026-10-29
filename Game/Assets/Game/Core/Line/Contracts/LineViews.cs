using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    /// <summary>
    /// Everything one player may know, rebuilt by the host per recipient (docs/CONTRACTS_V3.md §5 privacy).
    /// Hidden truths (odd one, culprit, saboteur, clues, digits, partners' choices, agendas, trick types, trick actors,
    /// tip authors, complaint authors, votes before the verdict, other players' calls) only appear where listed.
    /// </summary>
    [Serializable]
    public sealed class LineView
    {
        public int WeekId;
        public long StateVersion;
        public int Me = LineIds.None;
        /// <summary>Host time when the view was built.</summary>
        public double Now;
        public LinePhase Phase;
        public double PhaseStartTime;
        public double PhaseEndTime;
        /// <summary>0-based day of the week.</summary>
        public int Day;
        public int DayCount;
        public WeekLength Length;
        /// <summary>First week of the session (onboarding rules apply).</summary>
        public bool FirstWeek;
        /// <summary>Round index inside the day during RoundIntro/Play/Reveal, else -1.</summary>
        public int RoundIndex = -1;
        public List<RoundType> TodayRounds = new List<RoundType>();
        public string ManagerProfileId = "";
        /// <summary>Kayırmacı's favourite of the day, else -1.</summary>
        public int Favourite = LineIds.None;
        public LineFeaturesView Features = new LineFeaturesView();
        public List<LinePlayerView> Players = new List<LinePlayerView>();
        /// <summary>Private state of <see cref="Me"/>.</summary>
        public LineMeView Mine = new LineMeView();
        /// <summary>Current round (RoundIntro..RoundReveal), else null.</summary>
        public LineRoundView Round;
        /// <summary>Reveals of today's finished rounds (public), for the reveal screen and the dossier.</summary>
        public List<LineRoundReveal> TodayReveals = new List<LineRoundReveal>();
        /// <summary>Meeting phases (daily and final), else null.</summary>
        public LineMeetingView Meeting;
        /// <summary>WeekResults only.</summary>
        public LineWeekResult Result;
    }

    [Serializable]
    public sealed class LineFeaturesView
    {
        public bool TricksUnlocked;
        public bool TipLineUnlocked;
        public bool RecordingsEnabled;
        public bool IsLastDay;
        /// <summary>The current round type appears for the first time in this session (show the long rule card).</summary>
        public bool FirstTimeRound;
        /// <summary>First day of the session (step-by-step onboarding).</summary>
        public bool OnboardingDay;
    }

    /// <summary>Public facts about a colleague.</summary>
    [Serializable]
    public sealed class LinePlayerView
    {
        public int Id;
        public string Name = "";
        public int ColorIndex;
        public bool Connected;
        public bool IsBot;
        public int Blame;
        public int Ihtars;
        /// <summary>Has a choice for the current round (RoundPlay/Reveal).</summary>
        public bool Submitted;
        /// <summary>Has voted in the current vote phase.</summary>
        public bool Voted;
        /// <summary>Session points before this week.</summary>
        public int SessionPoints;
    }

    [Serializable]
    public sealed class LineMeView
    {
        /// <summary>Hidden agenda: you win AgendaPoints if this colleague is fired.</summary>
        public int AgendaTarget = LineIds.None;
        public TrickType Trick;
        public bool TrickUsedToday;
        public bool TipUsedToday;
        /// <summary>v3.1 weekly allowances.</summary>
        public int TricksLeftThisWeek;
        public int TipsLeftThisWeek;
        public int MailsSentToday;
        /// <summary>Computer unusable until this host time (coffee or internet).</summary>
        public double ComputerBlockedUntil;
        public TrickType ComputerBlockedBy;
        public double PhoneDeadUntil;
        /// <summary>Colleague you are tapping, or -1.</summary>
        public int WiretapTarget = LineIds.None;
        public double WiretapUntil;
        public LinePhoneView Phone = new LinePhoneView();
        /// <summary>Newest last.</summary>
        public List<LineMailView> Inbox = new List<LineMailView>();
        /// <summary>Your own calls (newest last).</summary>
        public List<LineCallRecordView> CallLog = new List<LineCallRecordView>();
    }

    [Serializable]
    public sealed class LinePhoneView
    {
        public CallStatus Status;
        /// <summary>The other side (caller when Ringing, callee when Dialing, partner when Connected), else -1.</summary>
        public int Other = LineIds.None;
        /// <summary>Connected call id, else -1.</summary>
        public int CallId = LineIds.None;
        public double Since;
        /// <summary>Dialing/Ringing: host time the ring times out.</summary>
        public double RingUntil;
    }

    [Serializable]
    public sealed class LineMailView
    {
        public int Id;
        public MailKind Kind;
        /// <summary>Colleague id, or LineIds.Manager / LineIds.System. Spoofed mails show the apparent sender.</summary>
        public int From = LineIds.None;
        /// <summary>Personal mails: template 0..MailTemplates-1. Manager/system mails: see CONTRACTS_V3 §6.</summary>
        public int TemplateId;
        /// <summary>Colleague mentioned in the template ({p}), or -1.</summary>
        public int About = LineIds.None;
        /// <summary>True when sent to everyone.</summary>
        public bool ToAll;
        public double Time;
    }

    [Serializable]
    public sealed class LineCallRecordView
    {
        public int CallId;
        public int Other;
        public bool Outgoing;
        public bool Connected;
        public double Start;
        /// <summary>-1 while ongoing.</summary>
        public double End = -1;
        public CallEndReason EndReason;
    }

    /// <summary>The current round as seen by one player.</summary>
    [Serializable]
    public sealed class LineRoundView
    {
        public RoundType Type;
        public int Index;
        public double PlayEndTime;

        // ---- private info (only the recipient's own)
        /// <summary>Bonus Pool partner (LineIds.Intern for the NPC in v3.0; v3.1: first partner).</summary>
        public int Partner = LineIds.None;
        /// <summary>v3.1 Bonus Pool: every partner (1, or 2 in the trio).</summary>
        public List<int> Partners = new List<int>();
        /// <summary>v3.1 Lost Report, culprit only: innocents that appear on no slip (no alibi).</summary>
        public List<int> NoAlibi = new List<int>();
        /// <summary>v3.1 Shared Password: the right-hand colleague's position and digit (also private).</summary>
        public int NeighborPosition = -1;
        public int NeighborDigit = -1;
        /// <summary>Odd Email: topic pair id; TopicVariant 0 = majority text (a), 1 = odd text (b).</summary>
        public int TopicId = -1;
        public int TopicVariant = -1;
        public bool IsOdd;
        /// <summary>Odd Email, odd player only: topic ids whose (a) text is offered as the guess.</summary>
        public List<int> GuessOptions = new List<int>();
        public bool IsCulprit;
        /// <summary>Lost Report: "this colleague is innocent".</summary>
        public int ClueInnocent = LineIds.None;
        /// <summary>Shared Password: your position (0-based) and digit.</summary>
        public int DigitPosition = -1;
        public int Digit = -1;
        public bool IsSaboteur;

        // ---- public live info
        /// <summary>Shared Password: position → player (public).</summary>
        public List<int> PositionOwners = new List<int>();
        public int CodeLength;
        public List<LineAttemptView> Attempts = new List<LineAttemptView>();
        public bool Solved;

        // ---- my submission
        public bool Submitted;
        public int MyTarget = LineIds.None;
        public int MyA = -1;
        public int AttemptsLeft;

        /// <summary>Filled from RoundReveal on.</summary>
        public LineRoundReveal Reveal;
    }

    [Serializable]
    public sealed class LineAttemptView
    {
        public int Player;
        public int Code;
        public int Correct;
        public double Time;
    }

    /// <summary>Public outcome of one finished round.</summary>
    [Serializable]
    public sealed class LineRoundReveal
    {
        public RoundType Type;
        public int Index;
        /// <summary>Bonus Pool pairs (B may be LineIds.Intern).</summary>
        public List<LinePairReveal> Pairs = new List<LinePairReveal>();
        /// <summary>Odd one / culprit / saboteur, else -1.</summary>
        public int Hidden = LineIds.None;
        public int TopicId = -1;
        /// <summary>Odd Email: the odd player's guess (topic id) and whether it was right.</summary>
        public int OddGuess = -1;
        public bool OddGuessCorrect;
        /// <summary>Lost Report: clue of each player (index = player id, -1 = absent seat).</summary>
        public List<int> Clues = new List<int>();
        /// <summary>Votes / recommendations with authors (empty for the anonymous Complaint Box).</summary>
        public List<LineVoteView> Votes = new List<LineVoteView>();
        /// <summary>Count per target (index = player id).</summary>
        public List<int> Counts = new List<int>();
        /// <summary>Complaint Box: anonymous complaints (target + reason).</summary>
        public List<LineComplaintView> Complaints = new List<LineComplaintView>();
        /// <summary>Plurality target (vote rounds) / promotion winner, else -1.</summary>
        public int Winner = LineIds.None;
        /// <summary>Shared Password: the code and whether it was cracked (and by whom).</summary>
        public int Code = -1;
        public bool Solved;
        public int SolvedBy = LineIds.None;
        /// <summary>v3.1 Shared Password: the code each player entered (index = player id, -1 none).</summary>
        public List<int> CodesByPlayer = new List<int>();
        public List<LineBlameDelta> Deltas = new List<LineBlameDelta>();
    }

    [Serializable]
    public sealed class LinePairReveal
    {
        public int A;
        public int B;
        /// <summary>0 = share, 1 = take.</summary>
        public int ChoiceA;
        public int ChoiceB;
        /// <summary>v3.1 trio member (else -1).</summary>
        public int C = LineIds.None;
        public int ChoiceC = -1;
    }

    [Serializable]
    public sealed class LineVoteView
    {
        public int Voter;
        public int Target;
    }

    [Serializable]
    public sealed class LineComplaintView
    {
        public int Target;
        public int Reason;
    }

    [Serializable]
    public sealed class LineBlameDelta
    {
        public int Player;
        public int Delta;
        public BlameReason Reason;
        public RoundType Round;
        public TrickType Trick;
        public int Day;
    }

    [Serializable]
    public sealed class LineMeetingView
    {
        public bool IsFinal;
        public List<LineDossierEntry> Dossier = new List<LineDossierEntry>();
        public List<LineRecordingPick> Recordings = new List<LineRecordingPick>();
        /// <summary>My vote in the current vote phase (-1 none/abstain).</summary>
        public int MyVote = LineIds.None;
        /// <summary>Daily verdict: warning receiver, else -1.</summary>
        public int IhtarTo = LineIds.None;
        /// <summary>Revealed at MeetingVerdict / FinalVerdict.</summary>
        public List<LineVoteView> Votes = new List<LineVoteView>();
        // final
        /// <summary>v3.1: the 2–3 players the final vote chooses between (they defend, in this order).</summary>
        public List<int> Shortlist = new List<int>();
        public List<int> DefenseOrder = new List<int>();
        public int Defender = LineIds.None;
        public double DefenderEndTime;
        public int Fired = LineIds.None;
        /// <summary>FinalVerdict: every player's fire score.</summary>
        public List<LineFireScore> Scores = new List<LineFireScore>();
    }

    [Serializable]
    public sealed class LineDossierEntry
    {
        public DossierKind Kind;
        public RoundType Round;
        public TrickType Trick;
        public int Player = LineIds.None;
        public int Victim = LineIds.None;
        public int TemplateId = -1;
        public bool Named;
        public int Arg = -1;
    }

    /// <summary>
    /// A call recording to play in the meeting. The engine schedules it; the host's voice system chooses which
    /// candidate actually has speech and reports it back (Caller/Callee/CallStart are -1 until then).
    /// </summary>
    [Serializable]
    public sealed class LineRecordingPick
    {
        public int Index;
        public List<int> CandidateCallIds = new List<int>();
        public double PlayAt;
        public float Seconds;
        public int ChosenCallId = LineIds.None;
        public int Caller = LineIds.None;
        public int Callee = LineIds.None;
        public double CallStart = -1;
        public bool Played;
    }

    [Serializable]
    public sealed class LineFireScore
    {
        public int Player;
        public int Blame;
        public int Votes;
        public int Ihtars;
        public int Score;
    }

    [Serializable]
    public sealed class LineWeekResult
    {
        public int WeekId;
        public string ManagerProfileId = "";
        public int Days;
        public int Fired = LineIds.None;
        /// <summary>v3.1: the fired player's pick for next week's manager ("" until picked).</summary>
        public string NextManagerPick = "";
        public List<LineWeekPlayerResult> Players = new List<LineWeekPlayerResult>();
        /// <summary>Deterministic hash of the outcome (all clients must agree; used by automated runs).</summary>
        public ulong ResultHash;
    }

    [Serializable]
    public sealed class LineWeekPlayerResult
    {
        public int Player;
        public int Blame;
        public int Ihtars;
        public bool Survived;
        public int AgendaTarget = LineIds.None;
        public bool AgendaHit;
        /// <summary>v3.1: İhtars the agenda target received this week (each pays AgendaPointsPerIhtar).</summary>
        public int AgendaIhtars;
        public bool EmployeeOfWeek;
        public TrickType Trick;
        public int TricksUsed;
        public int Points;
        /// <summary>Session total including this week.</summary>
        public int SessionPoints;
    }
}
