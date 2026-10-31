using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    /// <summary>Phase lengths in seconds (docs/GAMEPLAY_REDESIGN.md §2). <see cref="Scale"/> multiplies all of them (tests, bots).</summary>
    [Serializable]
    public sealed class LineDurations
    {
        public float Morning = 12f;
        /// <summary>The first block of a session (onboarding).</summary>
        public float MorningFirst = 40f;
        public float RoundIntro = 12f;
        /// <summary>A task that has not appeared yet in this session (tutorial).</summary>
        public float RoundIntroFirst = 25f;
        /// <summary>Reference task length; the effective length is RoundPlayByPlayers × (RoundPlay / 75) × task PlayFactor.</summary>
        public float RoundPlay = 75f;
        /// <summary>When every connected player has submitted, RoundPlay ends at most this much later.</summary>
        public float AllSubmittedRemaining = 8f;
        public float RoundReveal = 10f;
        public float Break = 15f;
        /// <summary>The break after the last task of the match (last chance for remedial work).</summary>
        public float BreakClosing = 25f;
        /// <summary>A break never exceeds this length after carried early-submit time.</summary>
        public float BreakMax = 45f;
        public float Dossier = 15f;
        /// <summary>Added to the dossier per recording (snippet + gap).</summary>
        public float DossierPerRecording = 9f;
        public float Discussion = 50f;
        public float FinalDiscussion = 75f;
        public float Vote = 20f;
        public float Verdict = 6f;
        public float DefensePerPlayer = 15f;
        public float FinalVote = 25f;
        public float FinalVerdict = 10f;
        public float WeekResults = 45f;
        /// <summary>Calls ring this long before they count as missed.</summary>
        public float Ring = 15f;
        /// <summary>A remedial request expires after this long.</summary>
        public float RemedialTimeout = 30f;

        public void Scale(float s)
        {
            if (s <= 0f || Math.Abs(s - 1f) < 0.0001f) return;
            Morning *= s; MorningFirst *= s; RoundIntro *= s; RoundIntroFirst *= s; RoundPlay *= s; AllSubmittedRemaining *= s;
            RoundReveal *= s; Break *= s; BreakClosing *= s; BreakMax *= s; Dossier *= s; DossierPerRecording *= s; Discussion *= s;
            FinalDiscussion *= s; Vote *= s; Verdict *= s; DefensePerPlayer *= s; FinalVote *= s; FinalVerdict *= s;
            WeekResults *= s; Ring *= s; RemedialTimeout *= s;
        }
    }

    /// <summary>One task definition's scheduling data (rules per task: <see cref="LineTaskRules"/>).</summary>
    [Serializable]
    public sealed class LineTaskConfig
    {
        public TaskKind Kind;
        public int Version = 1;
        public bool Enabled = true;
        public int MaxOutput = 100;
        /// <summary>Percent applied to the head-count play length.</summary>
        public int PlayFactor = 100;
        public int MinPlayers = LineProtocol.MinPlayers;
        public int MaxPlayers = LineProtocol.MaxPlayers;
        /// <summary>Manager profile id that alone may schedule this task ("" = common pool).</summary>
        public string ExclusiveManager = "";
    }

    /// <summary>Per-task numbers (docs/GAMEPLAY_REDESIGN.md §6). Output values are points of a task's MaxOutput (100).</summary>
    [Serializable]
    public sealed class LineTaskRules
    {
        // T1 invoice_reconcile
        public int InvoiceCorrectIncome = 2;
        public int InvoicePaddingLoss = 30;
        public int InvoicePaddingKickback = 3;
        // T2 budget_split
        public int BudgetSuccessIncome = 2;
        public int BudgetSurplusIncomeMax = 2;
        // T3 customer_spec
        public int SpecCorrectIncome = 2;
        public int SpecCommission = 2;
        // T4 report_review (output per report: accurate/polished × approve/reject)
        public int ReportAccurateApproved = 100;
        public int ReportAccurateRejected = 50;
        public int ReportPolishedApproved = 40;
        public int ReportPolishedRejected = 70;
        public int ReportPolishedKickback = 3;
        public int ReportAcceptedIncome = 2;
        public int ReportCorrectDecisionIncome = 1;
        // T5 room_booking
        public int RoomSuccessIncome = 2;
        public int RoomYieldIncome = 1;
        // T6 archive_visitor
        public int ArchiveSnoopLoss = 15;
        public int ArchiveServedIncome = 2;
        public int ArchiveVisitorIncomePerServed = 1;
        // T7 bonus_pool (outputs: pair by takers 0/1/2, trio by takers 0/1/2/3)
        public List<int> BonusPairOutput = new List<int> { 100, 60, 20 };
        public List<int> BonusTrioOutput = new List<int> { 100, 70, 40, 20 };
        public int BonusAllShareIncome = 2;
        public int BonusSoloTakerIncome = 5;
        public int BonusTrioTwoTakersIncome = 2;
        public int BonusAllTakeIncome = 1;
        // T8 server_code
        public int ServerFailBase = 20;
        public int ServerFailPerExact = 10;
        public int ServerFailCap = 60;
        public int ServerExactIncome = 1;
        public int ServerCrackedIncome = 1;
        public int ServerOffer = 4;
        // T9 phishing_check
        public int PhishingBlockedOutput = 100;
        public int PhishingFailedOutput = 25;
        public int PhishingFlagIncome = 1;
        public int PhishingKickback = 4;
        // T10 shift_cover
        public int ShiftOvertime = 3;
        // T11 vendor_pick
        public int VendorBestIncome = 1;
        public int VendorKickback = 3;
        // T12 supply_swap
        public int SwapCompleteIncome = 2;
        public int SwapCollectorBonus = 2;
    }

    /// <summary>A manager personality (docs/GAMEPLAY_REDESIGN.md §14). Names/texts come from localisation: manager.&lt;id&gt;.*</summary>
    [Serializable]
    public sealed class LineProfile
    {
        public string Id = "";
        /// <summary>Blame a warning adds.</summary>
        public int WarningBlame = 3;
        public int Recordings = 1;
        public bool HasFavourite;
        /// <summary>Productive income the favourite gets per resolved task.</summary>
        public int FavouriteIncome = 1;
        /// <summary>Subtracted from bank-ledger offer prices (minimum price 1).</summary>
        public int BankLedgerDiscount;
        /// <summary>The dossier lists every missed task input by name.</summary>
        public bool ListMissedTasks;
        /// <summary>Scheduling weight per TaskKind index (0 unused); missing entries count as 1.</summary>
        public List<int> TaskWeights = new List<int>();
    }

    /// <summary>All v4 tunables. Host sends its copy to clients on welcome (JSON).</summary>
    [Serializable]
    public sealed class LineConfig
    {
        public int ConfigVersion = LineProtocol.Version;
        public LineDurations Durations = new LineDurations();
        public List<LineTaskConfig> Tasks = new List<LineTaskConfig>();
        public LineTaskRules TaskRules = new LineTaskRules();
        public int RoundsPerDay = 2;
        public int ShortWeekDays = 3;
        public int FullWeekDays = 5;
        /// <summary>RoundPlay seconds for 3, 4, 5, 6 players (scaled like every duration via Durations.RoundPlay / 75).</summary>
        public List<float> RoundPlayByPlayers = new List<float> { 65f, 75f, 85f, 95f };
        /// <summary>Eligible tasks must exceed the scheduled count by at least this many.</summary>
        public int MinSpareTasks = 1;
        public EarlySubmitPolicy EarlySubmit = EarlySubmitPolicy.CarryToBreak;

        // ---- company and career
        public int CompanyTargetPercent = 65;
        /// <summary>Productivity needed for the "company target" career award (default = CompanyTargetPercent).</summary>
        public int CareerTargetPercent = 65;
        public int CareerNotFired = 100;
        public int CareerCompanyTarget = 75;
        public int CareerObjective = 60;
        public int CareerEmployeeOfMonth = 40;
        /// <summary>Points lost per distinct exposed incident (stored positive).</summary>
        public int CareerSabotagePenalty = 50;
        public int EotmCooperationWeight = 20;

        // ---- blame, warnings, recovery
        public int StartBlame = 5;
        public int LeaderStartBonus = 0;
        public int MissedTaskBlame = 1;
        public int ExposedSabotageBlame = 2;
        public int MaxWarningsPerPlayer = 2;
        public int WarningMinVotes = 2;
        public int RemedialBlame = 2;
        public int RemedialOutput = 10;
        public int RemedialIncome = 1;
        public int RemedialsPerBlock = 1;
        public int RemedialsPerMatch = 2;

        // ---- money and evidence
        public int StartMoney = 4;
        /// <summary>Productive income per block counted at full value; beyond it at half; never above BlockIncomeCap.</summary>
        public int BlockIncomeFull = 4;
        public int BlockIncomeCap = 6;
        public int TransferMax = 10;
        public float TransferMinInterval = 2f;
        public int BankLedgerPrice = 3;
        public int TaskAuditPrice = 3;
        public int ITLogPrice = 4;
        public int BadgeLogPrice = 4;
        /// <summary>Added to a buyer's price per earlier purchase in this match.</summary>
        public int PriceStep = 1;
        public int PurchasesPerBlock = 1;
        public int PurchasesPerMatch = 3;
        public int PresentationsPerMeeting = 2;

        // ---- secret objectives
        public int SaverMargin = 8;
        public int BrokerReceived = 6;
        public int PatronEach = 2;
        public int PatronColleagues = 2;
        public int TeamPlayerTasks = 3;
        public int TeamPlayerOutput = 90;
        public int ArchivistRecords = 6;
        public int ArchivistColleagues = 2;
        public int PresenterRecords = 2;
        public float RivalGuardianSameTarget = 0.5f;

        // ---- dirty tricks
        public int TricksPerWeekShort = 1;
        public int TricksPerWeekFull = 2;
        /// <summary>First block (0-based) on which tricks work in the first match of a session; later matches: block 0.</summary>
        public int UnlockDayFirstWeek = 1;
        public float CoffeeSeconds = 25f;
        public float PhoneDeadSeconds = 30f;
        public float InternetSeconds = 20f;
        public float WiretapSeconds = 45f;

        // ---- messages
        public int TextMaxChars = 200;
        public int EmailMaxChars = 400;
        public int ComplaintMaxChars = 140;
        public int MeetingChatMaxChars = 160;
        public float MessageMinInterval = 1.5f;
        public int TextsPerBlock = 60;
        public int EmailsPerBlock = 8;
        public int ComplaintsPerBlock = 1;
        public int MeetingChatPerMeeting = 30;
        /// <summary>Complaint presets: complaint.preset.&lt;i&gt;.</summary>
        public int ComplaintPresets = 8;
        /// <summary>Optional email templates: mail.template.&lt;i&gt;.</summary>
        public int MailTemplates = 8;
        /// <summary>Messages the engine keeps per recipient (oldest dropped first).</summary>
        public int MessagesKeptPerPlayer = 200;

        // ---- recordings
        public bool RecordingsEnabled = true;
        public float RecordingMinCallSeconds = 6f;
        public float RecordingSnippetSeconds = 7f;
        public int RecordingCandidates = 3;
        public RecordingSelection RecordingSelection = RecordingSelection.Context;
        /// <summary>Legacy mode: weight multiplier for calls between task partners.</summary>
        public float RecordingFocusWeight = 3f;
        public float RecordingGroupBonus = 1f;
        public float RecordingDecisionBonus = 0.5f;
        public float RecordingTransferBonus = 0.5f;
        public float RecordingExposureDecay = 0.5f;
        public float RecordingDecisionWindow = 30f;
        public float RecordingTransferWindow = 60f;

        public List<LineProfile> Profiles = new List<LineProfile>();

        public LineProfile Profile(string id)
        {
            foreach (var p in Profiles) if (p.Id == id) return p;
            return Profiles.Count > 0 ? Profiles[0] : new LineProfile { Id = id ?? "" };
        }

        public LineTaskConfig Task(TaskKind kind)
        {
            foreach (var t in Tasks) if (t.Kind == kind) return t;
            return null;
        }

        public int DaysFor(WeekLength length) { return length == WeekLength.Full ? FullWeekDays : ShortWeekDays; }

        static List<int> Weights(params TaskKind[] doubled)
        {
            var w = new List<int>();
            for (int i = 0; i <= LineTasks.Count; i++) w.Add(i == 0 ? 0 : 1);
            foreach (var k in doubled) w[(int)k] = 2;
            return w;
        }

        public static LineConfig CreateDefault()
        {
            var c = new LineConfig();
            for (int i = 1; i <= LineTasks.Count; i++) c.Tasks.Add(new LineTaskConfig { Kind = (TaskKind)i });
            // The hub task needs a little longer: one visitor, several callers.
            c.Task(TaskKind.ArchiveVisitor).PlayFactor = 110;
            c.Task(TaskKind.SupplySwap).PlayFactor = 110;
            c.Profiles.Add(new LineProfile { Id = LineManagerIds.Paragoz, WarningBlame = 2, BankLedgerDiscount = 1,
                TaskWeights = Weights(TaskKind.BudgetSplit, TaskKind.BonusPool, TaskKind.VendorPick) });
            c.Profiles.Add(new LineProfile { Id = LineManagerIds.Disiplinci, WarningBlame = 4, ListMissedTasks = true,
                TaskWeights = Weights(TaskKind.InvoiceReconcile, TaskKind.ReportReview, TaskKind.ShiftCover) });
            c.Profiles.Add(new LineProfile { Id = LineManagerIds.Kayirmaci, HasFavourite = true, TaskWeights = Weights() });
            c.Profiles.Add(new LineProfile { Id = LineManagerIds.Paranoyak, Recordings = 2,
                TaskWeights = Weights(TaskKind.ArchiveVisitor, TaskKind.PhishingCheck) });
            return c;
        }
    }
}
