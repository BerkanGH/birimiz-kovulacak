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

        public static MatchConfig CreateDefault()
        {
            var c = new MatchConfig();
            c.Sabotage.Add(new SabotageDef { Type = SabotageType.NameplateSwap, DurationSeconds = 2.0f, ActorCooldownSeconds = 20f, SameTargetCooldownSeconds = 60f, BaseMisconduct = 2f, ConsequenceMisconduct = 1f, MaxActivePerDesk = 1 });
            c.Sabotage.Add(new SabotageDef { Type = SabotageType.PlantItem, DurationSeconds = 3.0f, ActorCooldownSeconds = 25f, SameTargetCooldownSeconds = 60f, BaseMisconduct = 3f, ConsequenceMisconduct = 1f, MaxActivePerDesk = 2 });
            c.Sabotage.Add(new SabotageDef { Type = SabotageType.LockWorkstation, DurationSeconds = 4.0f, ActorCooldownSeconds = 30f, SameTargetCooldownSeconds = 30f, BaseMisconduct = 2f, ConsequenceMisconduct = 1f, MaxActivePerDesk = 1, EffectSeconds = 45f, ReverseSeconds = 5f });
            c.Sabotage.Add(new SabotageDef { Type = SabotageType.HideReport, DurationSeconds = 2.5f, ActorCooldownSeconds = 30f, SameTargetCooldownSeconds = 60f, BaseMisconduct = 2f, ConsequenceMisconduct = 1f, MaxActivePerDesk = 3 });
            c.Sabotage.Add(new SabotageDef { Type = SabotageType.SpillCoffee, DurationSeconds = 2.0f, ActorCooldownSeconds = 30f, SameTargetCooldownSeconds = 45f, BaseMisconduct = 2f, ConsequenceMisconduct = 1f, MaxActivePerDesk = 1, ReverseSeconds = 5f });
            c.Sabotage.Add(new SabotageDef { Type = SabotageType.MoveClutter, DurationSeconds = 1.0f, ActorCooldownSeconds = 15f, SameTargetCooldownSeconds = 45f, BaseMisconduct = 1f, ConsequenceMisconduct = 1f, MaxActivePerDesk = 2 });
            c.Sabotage.Add(new SabotageDef { Type = SabotageType.EvidenceTampering, DurationSeconds = 0f, ActorCooldownSeconds = 0f, SameTargetCooldownSeconds = 0f, BaseMisconduct = 1f, ConsequenceMisconduct = 0f, MaxActivePerDesk = 99 });

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

        public float PointsFor(TaskType t)
        {
            switch (t)
            {
                case TaskType.Coffee: return CoffeePoints;
                case TaskType.Report: return ReportPoints;
                case TaskType.Phone: return PhonePoints;
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
}
