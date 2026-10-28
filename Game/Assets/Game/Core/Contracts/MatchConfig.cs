using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    /// <summary>
    /// Every balance value and rule definition. Plain public fields so the Unity layer can load/save it as JSON
    /// (StreamingAssets/Config/match_config.json). <see cref="CreateDefault"/> is the canonical first-version tuning.
    /// </summary>
    [Serializable]
    public sealed class MatchConfig
    {
        public int PlayerCount = 4;
        public float QuotaTarget = 22f;
        /// <summary>When true, desks are shuffled per match; when false player N sits at desk N.</summary>
        public bool ShuffleDesks = false;

        public PhaseDurations Durations = new PhaseDurations();
        public InteractionConfig Interaction = new InteractionConfig();
        public TaskConfig Tasks = new TaskConfig();
        public List<SabotageDef> Sabotage = new List<SabotageDef>();
        public AntiGriefConfig AntiGrief = new AntiGriefConfig();
        public WitnessConfig Witness = new WitnessConfig();
        public ManagerConfig Manager = new ManagerConfig();
        public RiskWeights Risk = new RiskWeights();
        public InformantConfig Informant = new InformantConfig();
        public VerdictConfig Verdict = new VerdictConfig();
        public List<ObjectiveDef> Objectives = new List<ObjectiveDef>();
        public ReconnectConfig Reconnect = new ReconnectConfig();

        // ---- v2 ----
        public CardConfig Cards = new CardConfig();
        public EmailConfig Email = new EmailConfig();
        public CallConfig Calls = new CallConfig();
        public FootageConfig Footage = new FootageConfig();
        public CareerConfig Career = new CareerConfig();
        /// <summary>Manager personalities; one is chosen per match (see MatchSetup.ManagerProfileId).</summary>
        public List<ManagerProfileDef> ManagerProfiles = new List<ManagerProfileDef>();
        /// <summary>Week mode schedule, index = day (0 = Pazartesi). Applied by the engine to its working config copy.</summary>
        public List<WeekDayDef> WeekDays = new List<WeekDayDef>();

        public static MatchConfig CreateDefault()
        {
            var c = new MatchConfig();
            c.Durations.MeetingDiscussion = 45f;
            // Basic sabotage: everyone can do it.
            c.Sabotage.Add(new SabotageDef { Type = SabotageType.NameplateSwap, DurationSeconds = 2.0f, ActorCooldownSeconds = 20f, SameTargetCooldownSeconds = 60f, BaseMisconduct = 2f, ConsequenceMisconduct = 1f, MaxActivePerDesk = 1 });
            c.Sabotage.Add(new SabotageDef { Type = SabotageType.PlantItem, DurationSeconds = 3.0f, ActorCooldownSeconds = 25f, SameTargetCooldownSeconds = 60f, BaseMisconduct = 3f, ConsequenceMisconduct = 1f, MaxActivePerDesk = 2 });
            c.Sabotage.Add(new SabotageDef { Type = SabotageType.HideReport, DurationSeconds = 2.5f, ActorCooldownSeconds = 30f, SameTargetCooldownSeconds = 60f, BaseMisconduct = 2f, ConsequenceMisconduct = 1f, MaxActivePerDesk = 3 });
            c.Sabotage.Add(new SabotageDef { Type = SabotageType.SpillCoffee, DurationSeconds = 2.0f, ActorCooldownSeconds = 30f, SameTargetCooldownSeconds = 45f, BaseMisconduct = 2f, ConsequenceMisconduct = 1f, MaxActivePerDesk = 1, ReverseSeconds = 5f });
            c.Sabotage.Add(new SabotageDef { Type = SabotageType.MoveClutter, DurationSeconds = 1.0f, ActorCooldownSeconds = 15f, SameTargetCooldownSeconds = 45f, BaseMisconduct = 1f, ConsequenceMisconduct = 1f, MaxActivePerDesk = 2 });
            c.Sabotage.Add(new SabotageDef { Type = SabotageType.EvidenceTampering, DurationSeconds = 0f, ActorCooldownSeconds = 0f, SameTargetCooldownSeconds = 0f, BaseMisconduct = 1f, ConsequenceMisconduct = 0f, MaxActivePerDesk = 99 });
            // Card sabotage: only holders of the card (CardConfig).
            c.Sabotage.Add(new SabotageDef { Type = SabotageType.LockWorkstation, Basic = false, DurationSeconds = 4.0f, ActorCooldownSeconds = 30f, SameTargetCooldownSeconds = 30f, BaseMisconduct = 2f, ConsequenceMisconduct = 1f, MaxActivePerDesk = 1, EffectSeconds = 45f, ReverseSeconds = 5f });
            c.Sabotage.Add(new SabotageDef { Type = SabotageType.FakeEmail, Basic = false, DurationSeconds = 4.0f, ActorCooldownSeconds = 35f, SameTargetCooldownSeconds = 60f, BaseMisconduct = 2f, ConsequenceMisconduct = 1f, MaxActivePerDesk = 1 });
            c.Sabotage.Add(new SabotageDef { Type = SabotageType.PullPlug, Basic = false, DurationSeconds = 2.0f, ActorCooldownSeconds = 30f, SameTargetCooldownSeconds = 60f, BaseMisconduct = 2f, ConsequenceMisconduct = 1f, MaxActivePerDesk = 1, ReverseSeconds = 2f });
            c.Sabotage.Add(new SabotageDef { Type = SabotageType.SaltCoffee, Basic = false, DurationSeconds = 2.0f, ActorCooldownSeconds = 30f, SameTargetCooldownSeconds = 0f, BaseMisconduct = 2f, ConsequenceMisconduct = 1f, MaxActivePerDesk = 99 });
            c.Sabotage.Add(new SabotageDef { Type = SabotageType.InternetOutage, Basic = false, DurationSeconds = 4.0f, ActorCooldownSeconds = 60f, SameTargetCooldownSeconds = 0f, BaseMisconduct = 3f, ConsequenceMisconduct = 0f, MaxActivePerDesk = 99, EffectSeconds = 40f, ReverseSeconds = 6f, MaxPerMatch = 2 });
            c.Sabotage.Add(new SabotageDef { Type = SabotageType.PowerCut, Basic = false, DurationSeconds = 3.0f, ActorCooldownSeconds = 60f, SameTargetCooldownSeconds = 0f, BaseMisconduct = 2f, ConsequenceMisconduct = 0f, MaxActivePerDesk = 99, EffectSeconds = 25f, ReverseSeconds = 4f, MaxPerMatch = 2 });

            c.ManagerProfiles.Add(new ManagerProfileDef
            {
                Id = ManagerProfileIds.Paragoz, QuotaDelta = 2f, VoteWeight = 0.40f, RiskWeight = 0.20f, PerformanceWeight = 0.40f,
                FavorSource = FavorSource.ReportsDelivered,
                ObservationFactors = { new ObservationFactor(ObservationKind.MessyDesk, 0f), new ObservationFactor(ObservationKind.BehindOnWork, 2f), new ObservationFactor(ObservationKind.WorkingHard, 2f) },
            });
            c.ManagerProfiles.Add(new ManagerProfileDef
            {
                Id = ManagerProfileIds.Disiplinci, SurpriseInspection = true, EmailFactor = 1.5f, FavorSource = FavorSource.AnyTask, FavorPerAction = 0.5f,
                ObservationFactors = { new ObservationFactor(ObservationKind.MessyDesk, 2f), new ObservationFactor(ObservationKind.NotAtDesk, 2f), new ObservationFactor(ObservationKind.MovingDuringInspection, 1.5f), new ObservationFactor(ObservationKind.PersonalCall, 2f) },
            });
            c.ManagerProfiles.Add(new ManagerProfileDef
            {
                Id = ManagerProfileIds.Kayirmaci, HasFavourite = true, FavorSource = FavorSource.CoffeeDelivered, FavorWeight = 0.20f,
            });
            c.ManagerProfiles.Add(new ManagerProfileDef
            {
                Id = ManagerProfileIds.Paranoyak, EvidenceFactor = 1.5f, VisionHalfAngle = 85f, SearchAllDrawers = true, ExtraFootageRequests = 1, FavorSource = FavorSource.None,
                ObservationFactors = { new ObservationFactor(ObservationKind.CaughtInAct, 1.5f), new ObservationFactor(ObservationKind.CarryingSuspicious, 1.5f) },
            });

            c.WeekDays.Add(new WeekDayDef { ProfileId = ManagerProfileIds.Paragoz, Cards = false, TipLine = false, Footage = false });
            c.WeekDays.Add(new WeekDayDef { ProfileId = ManagerProfileIds.Disiplinci, Cards = true, TipLine = false, Footage = false });
            c.WeekDays.Add(new WeekDayDef { ProfileId = ManagerProfileIds.Kayirmaci, Cards = true, TipLine = true, Footage = true });
            c.WeekDays.Add(new WeekDayDef { ProfileId = ManagerProfileIds.Paranoyak, Cards = true, TipLine = true, Footage = true });
            c.WeekDays.Add(new WeekDayDef { ProfileId = "", Cards = true, TipLine = true, Footage = true, CareerMultiplier = 2f });

            c.Objectives.Add(new ObjectiveDef { Id = "DISMISSAL_PLAN", Type = ObjectiveType.DismissalPlan, RequiresTarget = true });
            c.Objectives.Add(new ObjectiveDef { Id = "PROTECTION_DEAL", Type = ObjectiveType.ProtectionDeal, RequiresTarget = true });
            c.Objectives.Add(new ObjectiveDef { Id = "SCAPEGOAT", Type = ObjectiveType.Scapegoat, RequiresTarget = true, Count = 2 });
            c.Objectives.Add(new ObjectiveDef { Id = "QUIET_STAR", Type = ObjectiveType.QuietStar });
            c.Objectives.Add(new ObjectiveDef { Id = "CHAOS_COORDINATOR", Type = ObjectiveType.ChaosCoordinator, Count = 3 });
            c.Objectives.Add(new ObjectiveDef { Id = "CLEAN_HANDS", Type = ObjectiveType.CleanHands, Count = 4, Count2 = 2 });
            c.Objectives.Add(new ObjectiveDef { Id = "OFFICE_ALL_ROUNDER", Type = ObjectiveType.OfficeAllRounder, Count = 4 });
            c.Objectives.Add(new ObjectiveDef { Id = "FIXER", Type = ObjectiveType.Fixer, Count = 2 });
            c.Objectives.Add(new ObjectiveDef { Id = "DESK_GUARDIAN", Type = ObjectiveType.DeskGuardian, RequiresTarget = true });
            c.Objectives.Add(new ObjectiveDef { Id = "UNNOTICED", Type = ObjectiveType.Unnoticed });
            c.Objectives.Add(new ObjectiveDef { Id = "PERFORMANCE_RIVAL", Type = ObjectiveType.PerformanceRival, RequiresTarget = true });
            c.Objectives.Add(new ObjectiveDef { Id = "UNDER_PRESSURE", Type = ObjectiveType.UnderPressure, Count = 2 });
            return c;
        }

        public SabotageDef GetSabotage(SabotageType type)
        {
            for (int i = 0; i < Sabotage.Count; i++) if (Sabotage[i].Type == type) return Sabotage[i];
            return null;
        }
    }

    [Serializable]
    public sealed class PhaseDurations
    {
        public float Briefing = 30f;
        public float Work1 = 180f;
        public float Raid = 30f;
        public float RaidWarning = 8f;          // part of Raid: alarm before the manager leaves the office
        public float Work2 = 90f;
        public float FinalInspection = 30f;
        public float FinalWarning = 5f;         // part of FinalInspection
        public float MeetingDiscussion = 30f;
        public float MeetingDefense = 20f;      // each of the two defense windows
        public float MeetingVote = 10f;
        public float MeetingVerdict = 10f;
        public float Results = 30f;

        public float Total()
        {
            return Briefing + Work1 + Raid + Work2 + FinalInspection + MeetingDiscussion + 2f * MeetingDefense + MeetingVote + MeetingVerdict + Results;
        }
    }

    [Serializable]
    public sealed class InteractionConfig
    {
        /// <summary>Max horizontal distance between avatar and target position for any interaction.</summary>
        public float Range = 1.8f;
        /// <summary>Extra tolerance the host allows for network latency.</summary>
        public float RangeSlack = 0.6f;
        /// <summary>Timed interactions are cancelled when the actor moves further than this from where they started.</summary>
        public float MoveCancelDistance = 0.9f;
        public float PickUpOpenSeconds = 0f;          // instant
        public float PickUpContainerSeconds = 1.0f;   // from a drawer / hiding spot
        public float PickUpNameplateSeconds = 1.5f;
        public float PlaceSurfaceSeconds = 1.0f;
        public float PlaceDrawerSeconds = 1.5f;       // non-suspicious items; suspicious items use PlantItem duration
        public float PlaceNameplateSeconds = 2.0f;
        public float PlaceHidingSpotSeconds = 2.5f;
        public float PlaceShelfSeconds = 0.5f;
        public float PlaceBinSeconds = 1.0f;
        public float DeliverSeconds = 2.0f;
        public float SearchSeconds = 2.0f;
        public float DropForwardOffset = 0.5f;
    }

    [Serializable]
    public sealed class TaskConfig
    {
        public float BrewSeconds = 6f;
        public float WriteReportSeconds = 5f;
        public float PrintSeconds = 6f;
        public int PrinterTrayCapacity = 3;
        public float AnswerPhoneSeconds = 8f;
        public float PhoneRingSeconds = 20f;
        public float PhoneIntervalMin = 28f;
        public float PhoneIntervalMax = 42f;
        public float CleanDeskSeconds = 7f;
        public float CleanSpillSeconds = 5f;
        public float DirtIntervalSeconds = 45f;
        public int InitialDirtyDesks = 1;

        public int InitialCoffeeOrders = 1;
        public float CoffeeOrderInterval = 35f;
        public int MaxCoffeeOrders = 3;
        public int InitialReportOrders = 1;
        public float ReportOrderInterval = 40f;
        public int MaxReportOrders = 3;

        public float CoffeePoints = 1f;
        public float ReportPoints = 2f;
        public float PhonePoints = 1f;
        public float CleanPoints = 1f;

        // v2: mail (legit reason to visit other desks) and tasting
        public bool MailEnabled = true;
        public int InitialMail = 1;
        public float MailInterval = 30f;
        public int MaxOpenMail = 2;
        public float MailPoints = 1f;
        public float TasteSeconds = 1f;

        public float PointsFor(TaskType t)
        {
            switch (t)
            {
                case TaskType.Coffee: return CoffeePoints;
                case TaskType.Report: return ReportPoints;
                case TaskType.Phone: return PhonePoints;
                case TaskType.Mail: return MailPoints;
                default: return CleanPoints;
            }
        }
    }

    [Serializable]
    public sealed class SabotageDef
    {
        public SabotageType Type;
        /// <summary>Duration of the sabotaging interaction (place / lock / spill...).</summary>
        public float DurationSeconds;
        /// <summary>Actor cannot start another sabotage of this type until this many seconds after completing one.</summary>
        public float ActorCooldownSeconds;
        /// <summary>Same actor cannot hit the same victim (desk owner / report owner) with this type again within this window.</summary>
        public float SameTargetCooldownSeconds;
        public float BaseMisconduct;
        /// <summary>Bounded extra misconduct added once if the documented consequence materialises.</summary>
        public float ConsequenceMisconduct;
        public int MaxActivePerDesk;
        /// <summary>Duration of a timed effect (workstation lock).</summary>
        public float EffectSeconds;
        /// <summary>Duration of the reversal interaction where it is a dedicated verb (unlock, clean spill).</summary>
        public float ReverseSeconds;
        /// <summary>v2: true = everyone may do it; false = only players holding this card.</summary>
        public bool Basic = true;
        /// <summary>v2: office-wide cap per match, all actors combined (0 = unlimited).</summary>
        public int MaxPerMatch;
    }

    [Serializable]
    public sealed class AntiGriefConfig
    {
        /// <summary>Max simultaneously active (unreversed) sabotage effects on one victim desk, all actors combined.</summary>
        public int MaxActiveEffectsPerVictim = 3;
        /// <summary>Max simultaneously active (unreversed) sabotage effects created by one actor.</summary>
        public int MaxActiveEffectsPerActor = 4;
        /// <summary>Workstation cannot be re-locked for this long after it is unlocked or expires.</summary>
        public float WorkstationImmunitySeconds = 30f;
        public int MaxSuspiciousItemsPerDesk = 2;
        public int MaxClutterPerDesk = 2;
    }

    [Serializable]
    public sealed class WitnessConfig
    {
        public float Range = 8f;
        /// <summary>Witness must be facing within this half-angle of the actor (avatar yaw).</summary>
        public float HalfAngle = 120f;
        public bool RequireLineOfSight = true;
        /// <summary>v2: a crouched (sneaking) actor is only noticed within Range * CrouchRangeFactor.</summary>
        public float CrouchRangeFactor = 0.6f;
        /// <summary>v2: witness range while the power is out.</summary>
        public float DarkRange = 2.5f;
    }

    [Serializable]
    public sealed class ManagerConfig
    {
        public float WalkSpeed = 2.6f;
        public float VisionRange = 10f;
        public float VisionHalfAngle = 70f;
        public float AtDeskRadius = 2.5f;
        /// <summary>Avatar speed above which a player counts as moving during an inspection.</summary>
        public float MovingSpeedThreshold = 1.2f;
        /// <summary>Movement must be sustained this long inside the cone to be noted (debounce).</summary>
        public float MovingNoticeSeconds = 0.75f;
        public float ReactionSeconds = 1.5f;
        /// <summary>Expected performance per player at each inspection; below it => BehindOnWork.</summary>
        public float ExpectedPerformanceAtRaid = 1f;
        public float ExpectedPerformanceAtFinal = 3f;
        /// <summary>v2: manager vision range while the power is out.</summary>
        public float DarkVisionRange = 3f;
        /// <summary>v2: extra dwell at a tipped desk (it is also visited first and its drawer searched).</summary>
        public float TipDwellBonus = 2.5f;
        /// <summary>v2: the unannounced inspection (profiles with SurpriseInspection) starts at this fraction of Work2 and lasts this long.</summary>
        public float SurpriseAtFraction = 0.45f;
        public float SurpriseSeconds = 20f;
    }

    [Serializable]
    public sealed class RiskWeights
    {
        public float SuspiciousItem = 3f;
        public float HiddenReportInDrawer = 2f;
        public float WitnessStatement = 1.5f;
        public float MovedEvidenceFactor = 0.5f;
        public float DirtyDesk = 0.5f;
        public float SpillOnDesk = 1f;
        public float ClutterEach = 0.5f;
        public float MessyDeskCap = 2f;
        public float NotAtDesk = 1f;
        public float AtWrongDesk = 0.5f;
        public float MovingDuringInspection = 1.5f;
        public float CarryingSuspicious = 2f;
        public float CaughtInAct = 3f;
        public float BehindOnWork = 1f;
        public float WorkingHard = -0.5f;
        // v2
        public float PersonalCall = 1f;
        public float InappropriateEmail = 1.5f;
        public float SaltyCoffee = 1.5f;
        public float TipClean = -0.5f;
        /// <summary>System email-log evidence weight (0 by default: the manager's InappropriateEmail observation carries the weight).</summary>
        public float EmailLog = 0f;
        /// <summary>Suspicion meter thresholds (observed risk) for levels 1..4.</summary>
        public float[] MeterThresholds = new float[] { 1.5f, 3f, 5f, 8f };
    }

    [Serializable]
    public sealed class InformantConfig
    {
        public int AuditScans = 2;
        public int SealedReports = 1;
        public float MovedRecentlySeconds = 90f;
        public float ItemScanRange = 2.5f;
        /// <summary>When avoidable, the same player is not the Informant in two consecutive matches.</summary>
        public bool AvoidRepeatInformant = true;
        /// <summary>v2: "Arama kayitlari" charges.</summary>
        public int CallLogCharges = 1;
        /// <summary>v2: footage requests usable in any meeting phase.</summary>
        public int FootageCharges = 1;
    }

    [Serializable]
    public sealed class VerdictConfig
    {
        public float VoteWeight = 0.45f;
        public float RiskWeight = 0.35f;
        public float PerformanceWeight = 0.20f;
        /// <summary>Risk normalisation floor: risk / max(RiskScale, highest risk of any player).</summary>
        public float RiskScale = 6f;
        /// <summary>Performance normalisation floor: deficit / max(PerformanceScale, best performance).</summary>
        public float PerformanceScale = 4f;
        public float FalseAccusationPenalty = 1f;
        /// <summary>v2: firing-score discount for the player(s) with the highest positive favour.</summary>
        public float FavorWeight = 0.10f;
    }

    [Serializable]
    public sealed class ObjectiveDef
    {
        public string Id;
        public ObjectiveType Type;
        public bool RequiresTarget;
        public int Count;
        public int Count2;
        public bool Enabled = true;
    }

    [Serializable]
    public sealed class ReconnectConfig
    {
        public float GraceSeconds = 30f;
    }

    // ---------------- v2 ----------------

    [Serializable]
    public sealed class CardConfig
    {
        public bool Enabled = true;
        /// <summary>Distinct card sabotage types dealt privately to every player (Informant included).</summary>
        public int CardsPerPlayer = 2;
    }

    [Serializable]
    public sealed class EmailConfig
    {
        /// <summary>A pc's screen session opens when its owner completes a report and closes after this long.</summary>
        public float ScreenOpenSeconds = 60f;
        public float LockScreenSeconds = 1f;
        /// <summary>Number of fake email templates (loc keys "email.&lt;i&gt;.subject" / ".body").</summary>
        public int TemplateCount = 10;
    }

    [Serializable]
    public sealed class CallConfig
    {
        public bool Enabled = true;
        public float RingSeconds = 10f;
        public float MaxCallSeconds = 45f;
        /// <summary>After a call ends (or is declined), the caller cannot call again for this long.</summary>
        public float CooldownSeconds = 8f;
        public bool TipLineEnabled = true;
        public float TipSeconds = 3f;
        public int TipsPerPlayer = 1;
    }

    [Serializable]
    public sealed class FootageConfig
    {
        public bool Enabled = true;
        /// <summary>Host-only position log sampling interval.</summary>
        public float SampleInterval = 1f;
        /// <summary>Footage shows sightings within +-WindowSeconds of the requested second.</summary>
        public float WindowSeconds = 5f;
        /// <summary>Requests per defense speaker per match (during their own defense window).</summary>
        public int SpeakerRequests = 1;
    }

    [Serializable]
    public sealed class CareerConfig
    {
        public int QuotaMet = 1;       // every non-fired player when the company succeeds
        public int Survived = 1;
        public int ObjectiveWon = 3;
        public int InformantWon = 3;
        public int Fired = -2;
        public int Favourite = 1;      // highest positive favour at the verdict
    }

    [Serializable]
    public sealed class ObservationFactor
    {
        public ObservationKind Kind;
        public float Factor = 1f;
        public ObservationFactor() { }
        public ObservationFactor(ObservationKind kind, float factor) { Kind = kind; Factor = factor; }
    }

    /// <summary>A manager personality. Negative override values mean "use the base config value".</summary>
    [Serializable]
    public sealed class ManagerProfileDef
    {
        public string Id = "";
        public float QuotaDelta;
        public float VoteWeight = -1f;
        public float RiskWeight = -1f;
        public float PerformanceWeight = -1f;
        public float FavorWeight = -1f;
        /// <summary>Multiplies observation severities of the listed kinds (0 = ignored).</summary>
        public List<ObservationFactor> ObservationFactors = new List<ObservationFactor>();
        /// <summary>Multiplies evidence risk weights (manager- and player-discovered evidence alike).</summary>
        public float EvidenceFactor = 1f;
        /// <summary>Multiplies InappropriateEmail severity.</summary>
        public float EmailFactor = 1f;
        public float VisionRange = -1f;
        public float VisionHalfAngle = -1f;
        /// <summary>One unannounced inspection during Work2 (ManagerConfig.SurpriseAtFraction / SurpriseSeconds).</summary>
        public bool SurpriseInspection;
        /// <summary>A public favourite is drawn at the briefing (starts with favour 2).</summary>
        public bool HasFavourite;
        public FavorSource FavorSource = FavorSource.None;
        public float FavorPerAction = 1f;
        /// <summary>Searches every drawer at every inspection (default: raids/surprise only open tipped drawers; the final inspection opens all).</summary>
        public bool SearchAllDrawers;
        public int ExtraFootageRequests;
    }

    /// <summary>One day of the week mode. Feature flags switch config features off for that day.</summary>
    [Serializable]
    public sealed class WeekDayDef
    {
        /// <summary>Empty = random profile (avoiding the previous day's).</summary>
        public string ProfileId = "";
        public bool Cards = true;
        public bool TipLine = true;
        public bool Footage = true;
        public bool Mail = true;
        public bool Calls = true;
        public float CareerMultiplier = 1f;
    }
}
