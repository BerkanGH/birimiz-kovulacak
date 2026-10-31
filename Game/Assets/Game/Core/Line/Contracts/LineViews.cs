using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    /// <summary>
    /// Everything one player may know, rebuilt by the host per recipient (docs/CONTRACTS_V4.md §5 privacy).
    /// Hidden truths (other players' facts, choices before they are public, objectives, money, trick types and use,
    /// incident actors before exposure, complaint authors, call partners, records the player has no access to) only
    /// appear where listed. Free text travels separately (<see cref="LineMessage"/>), except anonymous complaints in the
    /// meeting view.
    /// </summary>
    [Serializable]
    public sealed class LineView
    {
        public int WeekId;
        /// <summary>Host-generated random match id (analytics correlation; no meaning to rules).</summary>
        public string MatchId = "";
        public string RulesetVersion = LineProtocol.RulesetVersion;
        public long StateVersion;
        public int Me = LineIds.None;
        /// <summary>Host time when the view was built.</summary>
        public double Now;
        public LinePhase Phase;
        public double PhaseStartTime;
        public double PhaseEndTime;
        /// <summary>0-based work block.</summary>
        public int Day;
        public int DayCount;
        public WeekLength Length;
        /// <summary>First match of the session (onboarding rules apply).</summary>
        public bool FirstWeek;
        /// <summary>Task index inside the block during RoundIntro/Play/Reveal, else -1.</summary>
        public int RoundIndex = -1;
        /// <summary>Task number inside the match (0-based) during RoundIntro/Play/Reveal, else -1.</summary>
        public int TaskNumber = -1;
        public List<TaskKind> TodayTasks = new List<TaskKind>();
        public string ManagerProfileId = "";
        /// <summary>Kayırmacı's favourite of the block, else -1.</summary>
        public int Favourite = LineIds.None;
        public LineFeaturesView Features = new LineFeaturesView();
        public List<LinePlayerView> Players = new List<LinePlayerView>();
        public LineCompanyView Company = new LineCompanyView();
        /// <summary>Private state of <see cref="Me"/>.</summary>
        public LineMeView Mine = new LineMeView();
        /// <summary>Current task (RoundIntro..RoundReveal), else null.</summary>
        public LineTaskView Task;
        /// <summary>Public outcomes of this block's resolved tasks.</summary>
        public List<LineTaskReveal> TodayReveals = new List<LineTaskReveal>();
        /// <summary>Meeting phases (daily and final), else null.</summary>
        public LineMeetingView Meeting;
        /// <summary>WeekResults only.</summary>
        public LineWeekResult Result;
    }

    [Serializable]
    public sealed class LineFeaturesView
    {
        public bool TricksUnlocked;
        public bool RecordingsEnabled;
        public bool IsLastDay;
        /// <summary>The current task appears for the first time in this session (auto-open the tutorial).</summary>
        public bool FirstTimeTask;
        /// <summary>First block of the session (step-by-step onboarding).</summary>
        public bool OnboardingDay;
        /// <summary>Seconds of early-submit time carried into the current break.</summary>
        public float CarriedSeconds;
        public bool CallsOpen;
        public bool TransfersOpen;
        public bool EvidenceOpen;
        public bool PresentationOpen;
        public bool RemedialOpen;
        public bool ComplaintsOpen;
        public bool MeetingChatOpen;
    }

    /// <summary>Public facts about a colleague (money, objective and choices are private).</summary>
    [Serializable]
    public sealed class LinePlayerView
    {
        public int Id;
        public string Name = "";
        public int ColorIndex;
        public bool Connected;
        public bool IsBot;
        public int Blame;
        public int Warnings;
        /// <summary>Has submitted every required control of the current task (RoundPlay/Reveal).</summary>
        public bool Submitted;
        /// <summary>Has voted in the current ballot.</summary>
        public bool Voted;
        /// <summary>Session career points before this match.</summary>
        public int SessionPoints;
    }

    /// <summary>Public company productivity (docs/GAMEPLAY_REDESIGN.md §3).</summary>
    [Serializable]
    public sealed class LineCompanyView
    {
        /// <summary>Σ realized / Σ max over resolved tasks, per mille; -1 = measuring (nothing resolved yet).</summary>
        public int ProvisionalPermille = -1;
        /// <summary>Σ realized / Σ max over all scheduled tasks, per mille; -1 until frozen at the final meeting.</summary>
        public int FinalPermille = -1;
        public int TargetPercent;
        public int ResolvedOutput;
        public int ResolvedMax;
        public int ScheduledMax;
        public int TasksResolved;
        public int TasksScheduled;
        public CompanyChange LastChange;
        public TaskKind LastTask;
        /// <summary>Output points added by the last change.</summary>
        public int LastDelta;
        public bool Closed;
    }

    [Serializable]
    public sealed class LineMeView
    {
        public int Money;
        /// <summary>Productive income credited this block (before the block filter shows its effect).</summary>
        public int BlockIncome;
        public LineObjectiveView Objective = new LineObjectiveView();
        public TrickType Trick;
        public bool TrickUsedToday;
        public int TricksLeftThisWeek;
        /// <summary>Computer unusable until this host time (coffee or internet).</summary>
        public double ComputerBlockedUntil;
        public TrickType ComputerBlockedBy;
        public double PhoneDeadUntil;
        /// <summary>Colleague you are tapping, or -1.</summary>
        public int WiretapTarget = LineIds.None;
        public double WiretapUntil;
        public LinePhoneView Phone = new LinePhoneView();
        /// <summary>Your own calls (newest last).</summary>
        public List<LineCallRecordView> CallLog = new List<LineCallRecordView>();
        /// <summary>Every record you can present (own, partner-visible, public, purchased), newest last.</summary>
        public List<LineRecordView> Records = new List<LineRecordView>();
        public List<LineOfferView> Offers = new List<LineOfferView>();
        public int PurchasesLeftThisBlock;
        public int PurchasesLeftThisMatch;
        /// <summary>Presentations left in the current meeting.</summary>
        public int PresentationsLeft;
        public int RemedialsLeftThisBlock;
        public int RemedialsLeftThisMatch;
        /// <summary>Task numbers you may open a remedial report for now.</summary>
        public List<int> RemedialOptions = new List<int>();
        /// <summary>A colleague asks you to co-sign, else null.</summary>
        public LineRemedialView IncomingRemedial;
        /// <summary>Your pending request, else null.</summary>
        public LineRemedialView OutgoingRemedial;
        /// <summary>Latest message sequence number for you (pull with ILineEngine.MessagesFor).</summary>
        public long MessageSeq;
        public int TextsLeftThisBlock;
        public int EmailsLeftThisBlock;
        public int ComplaintsLeftThisBlock;
        public int MeetingChatLeft;
        /// <summary>Host time from which the next message / transfer is accepted (rate limits).</summary>
        public double NextMessageAt;
        public double NextTransferAt;
    }

    [Serializable]
    public sealed class LineObjectiveView
    {
        public ObjectiveKind Kind;
        /// <summary>Rival / Guardian colleague, else -1.</summary>
        public int Target = LineIds.None;
        public int Threshold;
        /// <summary>Current progress toward Threshold (-1 = only known at the end).</summary>
        public int Progress = -1;
        /// <summary>Set at WeekResults.</summary>
        public bool Completed;
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

    /// <summary>The current task as seen by one player.</summary>
    [Serializable]
    public sealed class LineTaskView
    {
        public TaskKind Kind;
        public int Version;
        public TaskFamily Family;
        /// <summary>Index inside the block.</summary>
        public int Index;
        /// <summary>Index inside the match.</summary>
        public int Number;
        public double PlayEndTime;
        /// <summary>Task-specific role code (localisation task.&lt;id&gt;.role.&lt;n&gt;), 0 = none.</summary>
        public int Role;
        /// <summary>Colleagues in my group (pair/trio/role partners), excluding me.</summary>
        public List<int> Group = new List<int>();
        /// <summary>My private facts and partner-visible facts.</summary>
        public List<LineFact> Facts = new List<LineFact>();
        /// <summary>Facts everyone sees (e.g. who holds the visitor badge, digit positions).</summary>
        public List<LineFact> PublicFacts = new List<LineFact>();
        public List<LineControlView> Controls = new List<LineControlView>();
        /// <summary>Supply Swap: item types I hold.</summary>
        public List<int> Items = new List<int>();
        /// <summary>Every required control has a value.</summary>
        public bool Submitted;
        public int MyTarget = LineIds.None;
        public int MyA = -1;
        public int MyB = -1;
        /// <summary>Fields that were submitted (TaskField mask).</summary>
        public TaskField SubmittedFields;
        /// <summary>Filled from RoundReveal on.</summary>
        public LineTaskReveal Reveal;
    }

    /// <summary>A typed line of task information; the UI renders task.&lt;id&gt;.fact.&lt;Key&gt; with {p} {n1} {n2} {n3}.</summary>
    [Serializable]
    public sealed class LineFact
    {
        public string Key = "";
        /// <summary>0 private, 1 partner-visible, 2 public.</summary>
        public int Scope;
        public int P = LineIds.None;
        public int N1 = -1;
        public int N2 = -1;
        public int N3 = -1;
    }

    [Serializable]
    public sealed class LineControlView
    {
        public ControlKind Kind;
        public TaskField Field;
        /// <summary>Label: task.&lt;id&gt;.control.&lt;Key&gt;.</summary>
        public string Key = "";
        public List<LineOption> Options = new List<LineOption>();
        public int Min;
        public int Max;
        /// <summary>Number: exact digit count (0 = any).</summary>
        public int Digits;
        /// <summary>Multi: maximum picks.</summary>
        public int MaxPicks;
        public bool AllowSelf;
        public bool AllowNone;
        public bool Required;
        public bool Locks;
        /// <summary>The whole control is a sabotage option (UI warns: it creates an incident that evidence can expose).</summary>
        public bool Sabotage;
        /// <summary>Waiting for this colleague's submission (-1 = not waiting).</summary>
        public int WaitingFor = LineIds.None;
        public bool Enabled = true;
    }

    [Serializable]
    public sealed class LineOption
    {
        /// <summary>Label: task.&lt;id&gt;.opt.&lt;Key&gt; with {p} {n1} {n2}.</summary>
        public string Key = "";
        public int Value;
        public int P = LineIds.None;
        public int N1 = -1;
        public int N2 = -1;
        public bool Sabotage;
        public bool Enabled = true;
    }

    /// <summary>Public outcome of one resolved task.</summary>
    [Serializable]
    public sealed class LineTaskReveal
    {
        public TaskKind Kind;
        public int Index;
        public int Number;
        public int Output;
        public int MaxOutput;
        public List<LineFact> PublicFacts = new List<LineFact>();
        /// <summary>Public ballots of the task (budget requests, vendor votes, flags, bookings), with authors.</summary>
        public List<LineVoteView> Votes = new List<LineVoteView>();
        /// <summary>Players who missed a required input (public).</summary>
        public List<int> Missed = new List<int>();
        public List<LineBlameDelta> Deltas = new List<LineBlameDelta>();
    }

    [Serializable]
    public sealed class LineVoteView
    {
        public int Voter;
        /// <summary>Colleague, LineIds.Nobody (No action / Fire nobody), -1 abstained; task ballots: the value chosen.</summary>
        public int Target;
    }

    [Serializable]
    public sealed class LineBlameDelta
    {
        public int Player;
        public int Delta;
        public BlameReason Reason;
        public TaskKind Task;
        public int Day;
    }

    [Serializable]
    public sealed class LineRecordView
    {
        public int Id;
        public RecordKind Kind;
        public RecordSource Source;
        public double Time;
        public int Day;
        /// <summary>Task number inside the match, or -1.</summary>
        public int TaskNumber = -1;
        public TaskKind Task;
        public int Actor = LineIds.None;
        public int Subject = LineIds.None;
        public int N1 = -1;
        public int N2 = -1;
        public int N3 = -1;
        /// <summary>Incident this record proves, else -1 (visible to anyone who holds the record).</summary>
        public int ProofOf = LineIds.None;
        /// <summary>Already presented in a meeting (public).</summary>
        public bool Presented;
    }

    [Serializable]
    public sealed class LineOfferView
    {
        public int Id;
        public OfferKind Kind;
        public int Day;
        public int TaskNumber = -1;
        public TaskKind Task;
        public int Price;
        public bool Purchased;
        public bool Available;
    }

    [Serializable]
    public sealed class LineRemedialView
    {
        public int Requester = LineIds.None;
        public int Cosigner = LineIds.None;
        public int TaskNumber = -1;
        public TaskKind Task;
        public double ExpiresAt;
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
        /// <summary>Thread root id (email replies keep the root), else Id.</summary>
        public int ThreadId;
        /// <summary>Email replied to, else -1.</summary>
        public int ReplyTo = LineIds.None;
        /// <summary>Sender (spoofed mail shows the apparent sender), LineIds.Manager or LineIds.System.</summary>
        public int From = LineIds.None;
        /// <summary>Recipient, or -1 when sent to everyone.</summary>
        public int To = LineIds.None;
        public bool ToAll;
        /// <summary>Player-authored text (sanitised by the engine). Never logged, never sent to telemetry.</summary>
        public string Text = "";
        public int TemplateId = -1;
        public int About = LineIds.None;
        public double Time;
        public int Day;
    }

    [Serializable]
    public sealed class LineMeetingView
    {
        public bool IsFinal;
        public List<LineDossierEntry> Dossier = new List<LineDossierEntry>();
        public List<LineRecordingPick> Recordings = new List<LineRecordingPick>();
        /// <summary>Anonymous complaints filed since the previous meeting (author never included).</summary>
        public List<LineComplaintView> Complaints = new List<LineComplaintView>();
        /// <summary>Records presented in this meeting, in order.</summary>
        public List<LinePresentationView> Presentations = new List<LinePresentationView>();
        /// <summary>My ballot in the current vote (-1 none, LineIds.Nobody = No action / Fire nobody).</summary>
        public int MyVote = LineIds.None;
        /// <summary>Daily verdict: warning receiver, else -1.</summary>
        public int WarningTo = LineIds.None;
        public VoteOutcome Outcome;
        /// <summary>Revealed at MeetingVerdict / FinalVerdict.</summary>
        public List<LineVoteView> Votes = new List<LineVoteView>();
        // final
        /// <summary>Final vote roster (players connected when FinalVote began).</summary>
        public List<int> Roster = new List<int>();
        public int MajorityNeeded;
        public List<int> DefenseOrder = new List<int>();
        public int Defender = LineIds.None;
        public double DefenderEndTime;
        /// <summary>FinalVerdict: fired player, LineIds.Nobody, or -1 before the verdict.</summary>
        public int Fired = LineIds.None;
        public bool CompanyClosed;
    }

    [Serializable]
    public sealed class LineDossierEntry
    {
        public DossierKind Kind;
        public TaskKind Task;
        public int TaskNumber = -1;
        public TrickType Trick;
        public int Player = LineIds.None;
        public int Victim = LineIds.None;
        public int Arg = -1;
        public int N1 = -1;
        public int N2 = -1;
        public double Time;
    }

    [Serializable]
    public sealed class LineComplaintView
    {
        public int Target;
        /// <summary>Preset index (complaint.preset.&lt;i&gt;), or -1 for free text.</summary>
        public int Preset = -1;
        public string Text = "";
    }

    [Serializable]
    public sealed class LinePresentationView
    {
        public int Presenter;
        public LineRecordView Record = new LineRecordView();
        public double Time;
        /// <summary>Incident newly proven by this presentation, else -1.</summary>
        public int ExposedIncident = LineIds.None;
        public IncidentKind ExposedKind;
        /// <summary>The same record had already been presented (no new consequence).</summary>
        public bool Repeat;
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
        /// <summary>Task kind the chosen call was most related to (group/decision), else None.</summary>
        public TaskKind Context;
        public bool Played;
    }

    [Serializable]
    public sealed class LineIncidentView
    {
        public int Id;
        public IncidentKind Kind;
        /// <summary>Only exposed incidents appear in public results.</summary>
        public int Actor = LineIds.None;
        public int Day;
        public int TaskNumber = -1;
        public int ExposedBy = LineIds.None;
        public double Time;
    }

    [Serializable]
    public sealed class LineCareerLine
    {
        public CareerItem Item;
        public int Points;
        public int Count = 1;
    }

    [Serializable]
    public sealed class LineWeekResult
    {
        public int WeekId;
        public string MatchId = "";
        public string RulesetVersion = LineProtocol.RulesetVersion;
        public string ManagerProfileId = "";
        public int Days;
        /// <summary>Fired player, LineIds.Nobody when the final vote fired nobody, -1 when the company closed.</summary>
        public int Fired = LineIds.None;
        public VoteOutcome FinalOutcome;
        public bool CompanyClosed;
        public int FinalProductivityPermille;
        public int TargetPercent;
        /// <summary>The fired player's pick for next match's manager ("" until picked).</summary>
        public string NextManagerPick = "";
        public int IncidentsTotal;
        /// <summary>Exposed incidents only (unexposed ones are never attributed).</summary>
        public List<LineIncidentView> Exposed = new List<LineIncidentView>();
        public List<LineWeekPlayerResult> Players = new List<LineWeekPlayerResult>();
        /// <summary>Deterministic hash of the outcome (all clients must agree; used by automated runs).</summary>
        public ulong ResultHash;
    }

    [Serializable]
    public sealed class LineWeekPlayerResult
    {
        public int Player;
        public int Blame;
        public int Warnings;
        public bool Fired;
        public ObjectiveKind Objective;
        public int ObjectiveTarget = LineIds.None;
        public bool ObjectiveDone;
        public bool EmployeeOfMonth;
        public List<LineCareerLine> Career = new List<LineCareerLine>();
        public int CareerTotal;
        /// <summary>Competition rank (1, 1, 3); 0 when the company closed (no ranking).</summary>
        public int Rank;
        public int Money;
        /// <summary>Mean contribution quality 0..100 over tasks the player was connected for (-1 none).</summary>
        public int ContributionQuality = -1;
        public int TasksConnected;
        public int ExposedIncidents;
        /// <summary>Session total including this match.</summary>
        public int SessionPoints;
    }
}
