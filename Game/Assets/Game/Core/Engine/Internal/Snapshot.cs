using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    /// <summary>Explicit deep copies keep mutable DTO graphs isolated from authoritative state.</summary>
    internal static class Snapshot
    {
        internal static ActionRequest Copy(ActionRequest value)
        {
            if (value == null) return null;
            return new ActionRequest
            {
                MatchId = value.MatchId,
                RequestId = value.RequestId,
                Verb = value.Verb,
                TargetId = value.TargetId,
                IntParam = value.IntParam,
            };
        }

        internal static ActionResult Copy(ActionResult value)
        {
            if (value == null) return null;
            return new ActionResult
            {
                RequestId = value.RequestId,
                Accepted = value.Accepted,
                Failure = value.Failure,
                CompletesAt = value.CompletesAt,
            };
        }

        internal static Affordance Copy(Affordance value)
        {
            if (value == null) return null;
            return new Affordance
            {
                Verb = value.Verb,
                TargetId = value.TargetId,
                IntParam = value.IntParam,
                Seconds = value.Seconds,
                LabelKey = value.LabelKey,
                IsSabotage = value.IsSabotage,
                BlockedBy = value.BlockedBy,
            };
        }

        internal static PlayerSlotInfo Copy(PlayerSlotInfo value)
        {
            if (value == null) return null;
            return new PlayerSlotInfo
            {
                Id = value.Id,
                Name = value.Name,
                ColorIndex = value.ColorIndex,
            };
        }

        internal static MatchSetup Copy(MatchSetup value)
        {
            if (value == null) return null;
            return new MatchSetup
            {
                MatchId = value.MatchId,
                Seed = value.Seed,
                StartTime = value.StartTime,
                Players = CopyList(value.Players, Copy),
                History = Copy(value.History),
                ForceInformant = value.ForceInformant,
                ForceObjectives = value.ForceObjectives == null ? null : new List<string>(value.ForceObjectives),
                ForceTargets = value.ForceTargets == null ? null : new List<int>(value.ForceTargets),
                Mode = value.Mode,
                WeekDay = value.WeekDay,
                ManagerProfileId = value.ManagerProfileId,
                ForceCards = CopyList(value.ForceCards, item => item == null ? null : new List<SabotageType>(item)),
                ForceFavourite = value.ForceFavourite,
            };
        }

        internal static SessionHistory Copy(SessionHistory value)
        {
            if (value == null) return null;
            return new SessionHistory
            {
                PastInformants = value.PastInformants == null ? null : new List<int>(value.PastInformants),
                PastTargetPairs = value.PastTargetPairs == null ? null : new List<string>(value.PastTargetPairs),
                TimesTargeted = value.TimesTargeted == null ? null : new List<int>(value.TimesTargeted),
                PastCards = value.PastCards == null ? null : new List<string>(value.PastCards),
                MatchesPlayed = value.MatchesPlayed,
                NextWeekDay = value.NextWeekDay,
                WeekNumber = value.WeekNumber,
                CareerPoints = value.CareerPoints == null ? null : new List<int>(value.CareerPoints),
                PastManagerProfiles = value.PastManagerProfiles == null ? null : new List<string>(value.PastManagerProfiles),
            };
        }

        internal static GameEvent Copy(GameEvent value)
        {
            if (value == null) return null;
            return new GameEvent
            {
                Type = value.Type,
                Time = value.Time,
                Audience = value.Audience,
                AudiencePlayer = value.AudiencePlayer,
                Actor = value.Actor,
                TargetId = value.TargetId,
                ItemId = value.ItemId,
                IntArg = value.IntArg,
                StrArg = value.StrArg,
                Phase = value.Phase,
            };
        }

        internal static LobbyView Copy(LobbyView value)
        {
            if (value == null) return null;
            return new LobbyView
            {
                ProtocolVersion = value.ProtocolVersion,
                BuildId = value.BuildId,
                Slots = CopyList(value.Slots, Copy),
                MatchRunning = value.MatchRunning,
                MatchId = value.MatchId,
                StatusKey = value.StatusKey,
                Mode = value.Mode,
                WeekDay = value.WeekDay,
                CareerPoints = value.CareerPoints == null ? null : new List<int>(value.CareerPoints),
            };
        }

        internal static LobbySlot Copy(LobbySlot value)
        {
            if (value == null) return null;
            return new LobbySlot
            {
                Slot = value.Slot,
                Occupied = value.Occupied,
                Connected = value.Connected,
                Ready = value.Ready,
                IsHost = value.IsHost,
                Name = value.Name,
                IsBot = value.IsBot,
            };
        }

        internal static ConnectionPayload Copy(ConnectionPayload value)
        {
            if (value == null) return null;
            return new ConnectionPayload
            {
                ProtocolVersion = value.ProtocolVersion,
                BuildId = value.BuildId,
                PlayerName = value.PlayerName,
                ReconnectToken = value.ReconnectToken,
            };
        }

        internal static WelcomeMessage Copy(WelcomeMessage value)
        {
            if (value == null) return null;
            return new WelcomeMessage
            {
                Slot = value.Slot,
                ReconnectToken = value.ReconnectToken,
                Reconnected = value.Reconnected,
                MatchId = value.MatchId,
            };
        }

        internal static MatchConfig Copy(MatchConfig value)
        {
            if (value == null) return null;
            return new MatchConfig
            {
                ConfigVersion = value.ConfigVersion,
                PlayerCount = value.PlayerCount,
                QuotaTarget = value.QuotaTarget,
                ShuffleDesks = value.ShuffleDesks,
                Durations = Copy(value.Durations),
                Interaction = Copy(value.Interaction),
                Tasks = Copy(value.Tasks),
                Sabotage = CopyList(value.Sabotage, Copy),
                AntiGrief = Copy(value.AntiGrief),
                Witness = Copy(value.Witness),
                Manager = Copy(value.Manager),
                Risk = Copy(value.Risk),
                Informant = Copy(value.Informant),
                Verdict = Copy(value.Verdict),
                Objectives = CopyList(value.Objectives, Copy),
                Reconnect = Copy(value.Reconnect),
                Cards = Copy(value.Cards),
                Email = Copy(value.Email),
                Calls = Copy(value.Calls),
                Footage = Copy(value.Footage),
                Career = Copy(value.Career),
                ManagerProfiles = CopyList(value.ManagerProfiles, Copy),
                WeekDays = CopyList(value.WeekDays, Copy),
            };
        }

        internal static PhaseDurations Copy(PhaseDurations value)
        {
            if (value == null) return null;
            return new PhaseDurations
            {
                Briefing = value.Briefing,
                Work1 = value.Work1,
                Raid = value.Raid,
                RaidWarning = value.RaidWarning,
                Work2 = value.Work2,
                FinalInspection = value.FinalInspection,
                FinalWarning = value.FinalWarning,
                MeetingDiscussion = value.MeetingDiscussion,
                MeetingDefense = value.MeetingDefense,
                MeetingVote = value.MeetingVote,
                MeetingVerdict = value.MeetingVerdict,
                Results = value.Results,
            };
        }

        internal static InteractionConfig Copy(InteractionConfig value)
        {
            if (value == null) return null;
            return new InteractionConfig
            {
                Range = value.Range,
                RangeSlack = value.RangeSlack,
                MoveCancelDistance = value.MoveCancelDistance,
                PickUpOpenSeconds = value.PickUpOpenSeconds,
                PickUpContainerSeconds = value.PickUpContainerSeconds,
                PickUpNameplateSeconds = value.PickUpNameplateSeconds,
                PlaceSurfaceSeconds = value.PlaceSurfaceSeconds,
                PlaceDrawerSeconds = value.PlaceDrawerSeconds,
                PlaceNameplateSeconds = value.PlaceNameplateSeconds,
                PlaceHidingSpotSeconds = value.PlaceHidingSpotSeconds,
                PlaceShelfSeconds = value.PlaceShelfSeconds,
                PlaceBinSeconds = value.PlaceBinSeconds,
                DeliverSeconds = value.DeliverSeconds,
                SearchSeconds = value.SearchSeconds,
                DropForwardOffset = value.DropForwardOffset,
            };
        }

        internal static TaskConfig Copy(TaskConfig value)
        {
            if (value == null) return null;
            return new TaskConfig
            {
                BrewSeconds = value.BrewSeconds,
                WriteReportSeconds = value.WriteReportSeconds,
                PrintSeconds = value.PrintSeconds,
                PrinterTrayCapacity = value.PrinterTrayCapacity,
                AnswerPhoneSeconds = value.AnswerPhoneSeconds,
                PhoneRingSeconds = value.PhoneRingSeconds,
                PhoneIntervalMin = value.PhoneIntervalMin,
                PhoneIntervalMax = value.PhoneIntervalMax,
                CleanDeskSeconds = value.CleanDeskSeconds,
                CleanSpillSeconds = value.CleanSpillSeconds,
                DirtIntervalSeconds = value.DirtIntervalSeconds,
                InitialDirtyDesks = value.InitialDirtyDesks,
                InitialCoffeeOrders = value.InitialCoffeeOrders,
                CoffeeOrderInterval = value.CoffeeOrderInterval,
                MaxCoffeeOrders = value.MaxCoffeeOrders,
                InitialReportOrders = value.InitialReportOrders,
                ReportOrderInterval = value.ReportOrderInterval,
                MaxReportOrders = value.MaxReportOrders,
                CoffeePoints = value.CoffeePoints,
                ReportPoints = value.ReportPoints,
                PhonePoints = value.PhonePoints,
                CleanPoints = value.CleanPoints,
                MailEnabled = value.MailEnabled,
                InitialMail = value.InitialMail,
                MailInterval = value.MailInterval,
                MaxOpenMail = value.MaxOpenMail,
                MailPoints = value.MailPoints,
                TasteSeconds = value.TasteSeconds,
            };
        }

        internal static SabotageDef Copy(SabotageDef value)
        {
            if (value == null) return null;
            return new SabotageDef
            {
                Type = value.Type,
                DurationSeconds = value.DurationSeconds,
                ActorCooldownSeconds = value.ActorCooldownSeconds,
                SameTargetCooldownSeconds = value.SameTargetCooldownSeconds,
                BaseMisconduct = value.BaseMisconduct,
                ConsequenceMisconduct = value.ConsequenceMisconduct,
                MaxActivePerDesk = value.MaxActivePerDesk,
                EffectSeconds = value.EffectSeconds,
                ReverseSeconds = value.ReverseSeconds,
                Basic = value.Basic,
                MaxPerMatch = value.MaxPerMatch,
            };
        }

        internal static AntiGriefConfig Copy(AntiGriefConfig value)
        {
            if (value == null) return null;
            return new AntiGriefConfig
            {
                MaxActiveEffectsPerVictim = value.MaxActiveEffectsPerVictim,
                MaxActiveEffectsPerActor = value.MaxActiveEffectsPerActor,
                WorkstationImmunitySeconds = value.WorkstationImmunitySeconds,
                MaxSuspiciousItemsPerDesk = value.MaxSuspiciousItemsPerDesk,
                MaxClutterPerDesk = value.MaxClutterPerDesk,
            };
        }

        internal static WitnessConfig Copy(WitnessConfig value)
        {
            if (value == null) return null;
            return new WitnessConfig
            {
                Range = value.Range,
                HalfAngle = value.HalfAngle,
                RequireLineOfSight = value.RequireLineOfSight,
                CrouchRangeFactor = value.CrouchRangeFactor,
                DarkRange = value.DarkRange,
            };
        }

        internal static ManagerConfig Copy(ManagerConfig value)
        {
            if (value == null) return null;
            return new ManagerConfig
            {
                WalkSpeed = value.WalkSpeed,
                VisionRange = value.VisionRange,
                VisionHalfAngle = value.VisionHalfAngle,
                AtDeskRadius = value.AtDeskRadius,
                MovingSpeedThreshold = value.MovingSpeedThreshold,
                MovingNoticeSeconds = value.MovingNoticeSeconds,
                ReactionSeconds = value.ReactionSeconds,
                ExpectedPerformanceAtRaid = value.ExpectedPerformanceAtRaid,
                ExpectedPerformanceAtFinal = value.ExpectedPerformanceAtFinal,
                DarkVisionRange = value.DarkVisionRange,
                TipDwellBonus = value.TipDwellBonus,
                SurpriseAtFraction = value.SurpriseAtFraction,
                SurpriseSeconds = value.SurpriseSeconds,
            };
        }

        internal static RiskWeights Copy(RiskWeights value)
        {
            if (value == null) return null;
            return new RiskWeights
            {
                SuspiciousItem = value.SuspiciousItem,
                HiddenReportInDrawer = value.HiddenReportInDrawer,
                WitnessStatement = value.WitnessStatement,
                MovedEvidenceFactor = value.MovedEvidenceFactor,
                DirtyDesk = value.DirtyDesk,
                SpillOnDesk = value.SpillOnDesk,
                ClutterEach = value.ClutterEach,
                MessyDeskCap = value.MessyDeskCap,
                NotAtDesk = value.NotAtDesk,
                AtWrongDesk = value.AtWrongDesk,
                MovingDuringInspection = value.MovingDuringInspection,
                CarryingSuspicious = value.CarryingSuspicious,
                CaughtInAct = value.CaughtInAct,
                BehindOnWork = value.BehindOnWork,
                WorkingHard = value.WorkingHard,
                PersonalCall = value.PersonalCall,
                InappropriateEmail = value.InappropriateEmail,
                SaltyCoffee = value.SaltyCoffee,
                TipClean = value.TipClean,
                EmailLog = value.EmailLog,
                MeterThresholds = value.MeterThresholds == null ? null : (float[])value.MeterThresholds.Clone(),
            };
        }

        internal static InformantConfig Copy(InformantConfig value)
        {
            if (value == null) return null;
            return new InformantConfig
            {
                AuditScans = value.AuditScans,
                SealedReports = value.SealedReports,
                MovedRecentlySeconds = value.MovedRecentlySeconds,
                ItemScanRange = value.ItemScanRange,
                AvoidRepeatInformant = value.AvoidRepeatInformant,
                CallLogCharges = value.CallLogCharges,
                FootageCharges = value.FootageCharges,
            };
        }

        internal static VerdictConfig Copy(VerdictConfig value)
        {
            if (value == null) return null;
            return new VerdictConfig
            {
                VoteWeight = value.VoteWeight,
                RiskWeight = value.RiskWeight,
                PerformanceWeight = value.PerformanceWeight,
                RiskScale = value.RiskScale,
                PerformanceScale = value.PerformanceScale,
                FalseAccusationPenalty = value.FalseAccusationPenalty,
                FavorWeight = value.FavorWeight,
            };
        }

        internal static ObjectiveDef Copy(ObjectiveDef value)
        {
            if (value == null) return null;
            return new ObjectiveDef
            {
                Id = value.Id,
                Type = value.Type,
                RequiresTarget = value.RequiresTarget,
                Count = value.Count,
                Count2 = value.Count2,
                Enabled = value.Enabled,
            };
        }

        internal static ReconnectConfig Copy(ReconnectConfig value)
        {
            if (value == null) return null;
            return new ReconnectConfig
            {
                GraceSeconds = value.GraceSeconds,
            };
        }

        internal static OfficeLayout Copy(OfficeLayout value)
        {
            if (value == null) return null;
            return new OfficeLayout
            {
                Desks = CopyList(value.Desks, Copy),
                Stations = CopyList(value.Stations, Copy),
                Slots = CopyList(value.Slots, Copy),
                Items = CopyList(value.Items, Copy),
                ManagerRoute = CopyList(value.ManagerRoute, Copy),
                ManagerHome = value.ManagerHome,
                ManagerHomeYaw = value.ManagerHomeYaw,
                ManagerMeetingSpot = value.ManagerMeetingSpot,
                ManagerMeetingYaw = value.ManagerMeetingYaw,
                MeetingSeats = CopyList(value.MeetingSeats, Copy),
                BoundsMin = value.BoundsMin,
                BoundsMax = value.BoundsMax,
                Rooms = CopyList(value.Rooms, Copy),
                Cameras = CopyList(value.Cameras, Copy),
            };
        }

        internal static DeskDef Copy(DeskDef value)
        {
            if (value == null) return null;
            return new DeskDef
            {
                Index = value.Index,
                Id = value.Id,
                Position = value.Position,
                Yaw = value.Yaw,
                SeatPosition = value.SeatPosition,
                SurfaceSlotId = value.SurfaceSlotId,
                DrawerSlotId = value.DrawerSlotId,
                NameplateSlotId = value.NameplateSlotId,
                ComputerId = value.ComputerId,
            };
        }

        internal static StationDef Copy(StationDef value)
        {
            if (value == null) return null;
            return new StationDef
            {
                Id = value.Id,
                Kind = value.Kind,
                Position = value.Position,
                Yaw = value.Yaw,
                DeskIndex = value.DeskIndex,
            };
        }

        internal static SlotDef Copy(SlotDef value)
        {
            if (value == null) return null;
            return new SlotDef
            {
                Id = value.Id,
                Kind = value.Kind,
                Position = value.Position,
                Yaw = value.Yaw,
                Capacity = value.Capacity,
                Closed = value.Closed,
                DeskIndex = value.DeskIndex,
            };
        }

        internal static ItemDef Copy(ItemDef value)
        {
            if (value == null) return null;
            return new ItemDef
            {
                Id = value.Id,
                Kind = value.Kind,
                Variant = value.Variant,
                InitialSlotId = value.InitialSlotId,
                NameplateOf = value.NameplateOf,
            };
        }

        internal static ManagerStop Copy(ManagerStop value)
        {
            if (value == null) return null;
            return new ManagerStop
            {
                Position = value.Position,
                Yaw = value.Yaw,
                DeskIndex = value.DeskIndex,
                DwellSeconds = value.DwellSeconds,
            };
        }

        internal static SeatDef Copy(SeatDef value)
        {
            if (value == null) return null;
            return new SeatDef
            {
                Position = value.Position,
                Yaw = value.Yaw,
            };
        }

        internal static VerdictView Copy(VerdictView value)
        {
            if (value == null) return null;
            return new VerdictView
            {
                FiredPlayer = value.FiredPlayer,
                Candidates = CopyList(value.Candidates, Copy),
                TieBreakUsed = value.TieBreakUsed,
                KeyEvidence = value.KeyEvidence == null ? null : new List<int>(value.KeyEvidence),
                KeyObservations = value.KeyObservations == null ? null : new List<int>(value.KeyObservations),
                VoteWeight = value.VoteWeight,
                RiskWeight = value.RiskWeight,
                PerformanceWeight = value.PerformanceWeight,
            };
        }

        internal static CandidateScore Copy(CandidateScore value)
        {
            if (value == null) return null;
            return new CandidateScore
            {
                Player = value.Player,
                Accusations = value.Accusations,
                Votes = value.Votes,
                VoteShare = value.VoteShare,
                ObservedRisk = value.ObservedRisk,
                RiskNormalized = value.RiskNormalized,
                Performance = value.Performance,
                PerformanceDeficit = value.PerformanceDeficit,
                Favor = value.Favor,
                FavorDiscount = value.FavorDiscount,
                Score = value.Score,
            };
        }

        internal static MatchResultView Copy(MatchResultView value)
        {
            if (value == null) return null;
            return new MatchResultView
            {
                CompanySucceeded = value.CompanySucceeded,
                QuotaPoints = value.QuotaPoints,
                QuotaTarget = value.QuotaTarget,
                FiredPlayer = value.FiredPlayer,
                Verdict = Copy(value.Verdict),
                InformantPlayer = value.InformantPlayer,
                InformantWon = value.InformantWon,
                TopMisconductTier = value.TopMisconductTier == null ? null : new List<int>(value.TopMisconductTier),
                Players = CopyList(value.Players, Copy),
                Incidents = CopyList(value.Incidents, Copy),
                Recap = CopyList(value.Recap, Copy),
                MatchSeed = value.MatchSeed,
                ResultHash = value.ResultHash,
                Mode = value.Mode,
                WeekDay = value.WeekDay,
                ManagerProfileId = value.ManagerProfileId,
                Calls = CopyList(value.Calls, Copy),
                WeekFinished = value.WeekFinished,
                EmployeeOfTheMonth = value.EmployeeOfTheMonth == null ? null : new List<int>(value.EmployeeOfTheMonth),
                Favourites = value.Favourites == null ? null : new List<int>(value.Favourites),
                Highlight = Copy(value.Highlight),
            };
        }

        internal static PlayerOutcome Copy(PlayerOutcome value)
        {
            if (value == null) return null;
            return new PlayerOutcome
            {
                Player = value.Player,
                Name = value.Name,
                Role = value.Role,
                ObjectiveCardId = value.ObjectiveCardId,
                ObjectiveType = value.ObjectiveType,
                ObjectiveTarget = value.ObjectiveTarget,
                ObjectiveCompleted = value.ObjectiveCompleted,
                Fired = value.Fired,
                Won = value.Won,
                ExplanationKey = value.ExplanationKey,
                TrueMisconduct = value.TrueMisconduct,
                MisconductRank = value.MisconductRank,
                FinalPerformance = value.FinalPerformance,
                FalseAccusationPenalty = value.FalseAccusationPenalty,
                TasksCompleted = value.TasksCompleted,
                SabotageCount = value.SabotageCount,
                Titles = value.Titles == null ? null : new List<string>(value.Titles),
                Cards = value.Cards == null ? null : new List<SabotageType>(value.Cards),
                Favor = value.Favor,
                CareerDelta = value.CareerDelta,
                CareerTotal = value.CareerTotal,
            };
        }

        internal static IncidentReveal Copy(IncidentReveal value)
        {
            if (value == null) return null;
            return new IncidentReveal
            {
                Id = value.Id,
                Type = value.Type,
                Actor = value.Actor,
                Victim = value.Victim,
                DeskIndex = value.DeskIndex,
                ItemId = value.ItemId,
                Time = value.Time,
                Misconduct = value.Misconduct,
                Discovered = value.Discovered,
                ReversedBy = value.ReversedBy,
                BlamedPlayer = value.BlamedPlayer,
            };
        }

        internal static RecapBeat Copy(RecapBeat value)
        {
            if (value == null) return null;
            return new RecapBeat
            {
                Time = value.Time,
                Key = value.Key,
                Actor = value.Actor,
                Other = value.Other,
                ItemVariant = value.ItemVariant,
                DeskIndex = value.DeskIndex,
            };
        }

        internal static HostDebugView Copy(HostDebugView value)
        {
            if (value == null) return null;
            return new HostDebugView
            {
                MatchId = value.MatchId,
                Seed = value.Seed,
                Phase = value.Phase,
                Roles = value.Roles == null ? null : new List<Role>(value.Roles),
                ObjectiveCards = value.ObjectiveCards == null ? null : new List<string>(value.ObjectiveCards),
                ObjectiveTargets = value.ObjectiveTargets == null ? null : new List<int>(value.ObjectiveTargets),
                TrueMisconduct = value.TrueMisconduct == null ? null : new List<float>(value.TrueMisconduct),
                ObservedRisk = value.ObservedRisk == null ? null : new List<float>(value.ObservedRisk),
                Incidents = CopyList(value.Incidents, Copy),
                ItemHandlers = CopyHandlers(value.ItemHandlers),
                QuotaPoints = value.QuotaPoints,
                ProcessedRequests = value.ProcessedRequests,
                RejectedRequests = value.RejectedRequests,
                ManagerProfileId = value.ManagerProfileId,
                Cards = CopyList(value.Cards, item => item == null ? null : new List<SabotageType>(item)),
                Calls = CopyList(value.Calls, Copy),
                SaltedItems = value.SaltedItems == null ? null : new List<string>(value.SaltedItems),
            };
        }

        internal static PlayerView Copy(PlayerView value)
        {
            if (value == null) return null;
            return new PlayerView
            {
                MatchId = value.MatchId,
                StateVersion = value.StateVersion,
                Recipient = value.Recipient,
                Phase = value.Phase,
                PhaseSerial = value.PhaseSerial,
                PhaseStartTime = value.PhaseStartTime,
                PhaseEndTime = value.PhaseEndTime,
                ServerTime = value.ServerTime,
                AbandonReason = value.AbandonReason,
                Quota = Copy(value.Quota),
                Players = CopyList(value.Players, Copy),
                Items = CopyList(value.Items, Copy),
                Desks = CopyList(value.Desks, Copy),
                Stations = CopyList(value.Stations, Copy),
                Evidence = CopyList(value.Evidence, Copy),
                Observations = CopyList(value.Observations, Copy),
                Log = CopyList(value.Log, Copy),
                Manager = Copy(value.Manager),
                Meeting = Copy(value.Meeting),
                Me = Copy(value.Me),
                Result = Copy(value.Result),
                Mode = value.Mode,
                WeekDay = value.WeekDay,
                ManagerProfileId = value.ManagerProfileId,
                Favourite = value.Favourite,
                Features = Copy(value.Features),
                Office = Copy(value.Office),
                Inbox = CopyList(value.Inbox, Copy),
            };
        }

        internal static QuotaView Copy(QuotaView value)
        {
            if (value == null) return null;
            return new QuotaView
            {
                Points = value.Points,
                Target = value.Target,
                CoffeeOrders = value.CoffeeOrders,
                ReportOrders = value.ReportOrders,
                PhoneRinging = value.PhoneRinging,
                PhoneRingEnds = value.PhoneRingEnds,
                DirtyDesks = value.DirtyDesks,
                Frozen = value.Frozen,
                OpenMail = value.OpenMail,
            };
        }

        internal static PlayerPublicView Copy(PlayerPublicView value)
        {
            if (value == null) return null;
            return new PlayerPublicView
            {
                Id = value.Id,
                Name = value.Name,
                ColorIndex = value.ColorIndex,
                DeskIndex = value.DeskIndex,
                Connected = value.Connected,
                DisconnectDeadline = value.DisconnectDeadline,
                Performance = value.Performance,
                TasksCompleted = value.TasksCompleted,
                SuspicionLevel = value.SuspicionLevel,
                CarryingItemId = value.CarryingItemId,
                Activity = value.Activity,
                ActivityTargetId = value.ActivityTargetId,
                ActivityStart = value.ActivityStart,
                ActivityEnd = value.ActivityEnd,
                WorkPose = value.WorkPose,
                AccusationsReceived = value.AccusationsReceived,
                Crouched = value.Crouched,
                Ringing = value.Ringing,
                Favor = value.Favor,
                Emote = value.Emote,
                EmoteUntil = value.EmoteUntil,
                CareerPoints = value.CareerPoints,
            };
        }

        internal static ItemView Copy(ItemView value)
        {
            if (value == null) return null;
            return new ItemView
            {
                Id = value.Id,
                Kind = value.Kind,
                Variant = value.Variant,
                Location = value.Location,
                SlotId = value.SlotId,
                SlotIndex = value.SlotIndex,
                CarrierId = value.CarrierId,
                FloorPosition = value.FloorPosition,
                FloorYaw = value.FloorYaw,
                Fill = value.Fill,
                ReportOwner = value.ReportOwner,
                NameplateOf = value.NameplateOf,
                Sealed = value.Sealed,
                IsEvidence = value.IsEvidence,
                AddressedDesk = value.AddressedDesk,
                KnownSalted = value.KnownSalted,
            };
        }

        internal static DeskView Copy(DeskView value)
        {
            if (value == null) return null;
            return new DeskView
            {
                Index = value.Index,
                Id = value.Id,
                AssignedOwner = value.AssignedOwner,
                DisplayedNameplateOf = value.DisplayedNameplateOf,
                Dirty = value.Dirty,
                Spill = value.Spill,
                PcLocked = value.PcLocked,
                PcLockedAt = value.PcLockedAt,
                PcLockedUntil = value.PcLockedUntil,
                ClutterCount = value.ClutterCount,
                ScreenOpen = value.ScreenOpen,
                ScreenOpenUntil = value.ScreenOpenUntil,
                Unplugged = value.Unplugged,
                PcImmuneUntil = value.PcImmuneUntil,
            };
        }

        internal static StationView Copy(StationView value)
        {
            if (value == null) return null;
            return new StationView
            {
                Id = value.Id,
                Kind = value.Kind,
                UsedBy = value.UsedBy,
                BusyUntil = value.BusyUntil,
                PrintQueue = value.PrintQueue == null ? null : new List<int>(value.PrintQueue),
                PrintEndsAt = value.PrintEndsAt,
            };
        }

        internal static EvidenceView Copy(EvidenceView value)
        {
            if (value == null) return null;
            return new EvidenceView
            {
                Id = value.Id,
                Kind = value.Kind,
                Time = value.Time,
                PhaseFound = value.PhaseFound,
                ItemId = value.ItemId,
                ItemVariant = value.ItemVariant,
                LocationId = value.LocationId,
                DeskIndex = value.DeskIndex,
                AttributedTo = value.AttributedTo,
                Source = value.Source,
                SourcePlayer = value.SourcePlayer,
                Status = value.Status,
                Sealed = value.Sealed,
                SubjectPlayer = value.SubjectPlayer,
                ObservedActivity = value.ObservedActivity,
                HeldBefore = value.HeldBefore,
                HeldAfter = value.HeldAfter,
                ScanReliability = value.ScanReliability,
                ScanSignal = value.ScanSignal,
                RiskWeight = value.RiskWeight,
                Contents = value.Contents == null ? null : new List<string>(value.Contents),
                EmailTemplate = value.EmailTemplate,
                CameraId = value.CameraId,
                WindowStart = value.WindowStart,
                WindowEnd = value.WindowEnd,
                NoRecording = value.NoRecording,
                Sightings = CopyList(value.Sightings, Copy),
                Calls = CopyList(value.Calls, Copy),
                Salted = value.Salted,
            };
        }

        internal static ObservationView Copy(ObservationView value)
        {
            if (value == null) return null;
            return new ObservationView
            {
                Id = value.Id,
                Kind = value.Kind,
                Time = value.Time,
                Phase = value.Phase,
                Subject = value.Subject,
                DeskIndex = value.DeskIndex,
                Severity = value.Severity,
                Detail = value.Detail,
            };
        }

        internal static LogEntry Copy(LogEntry value)
        {
            if (value == null) return null;
            return new LogEntry
            {
                Time = value.Time,
                Kind = value.Kind,
                Actor = value.Actor,
                TargetId = value.TargetId,
                IntArg = value.IntArg,
                StrArg = value.StrArg,
            };
        }

        internal static ManagerView Copy(ManagerView value)
        {
            if (value == null) return null;
            return new ManagerView
            {
                State = value.State,
                Position = value.Position,
                Yaw = value.Yaw,
                InspectingDesk = value.InspectingDesk,
                ReactionObservation = value.ReactionObservation,
                ReactionUntil = value.ReactionUntil,
                ArrivesAt = value.ArrivesAt,
                Surprise = value.Surprise,
            };
        }

        internal static MeetingView Copy(MeetingView value)
        {
            if (value == null) return null;
            return new MeetingView
            {
                Accusations = CopyList(value.Accusations, Copy),
                Shortlist = value.Shortlist == null ? null : new List<int>(value.Shortlist),
                Speaker = value.Speaker,
                SpeakerEnds = value.SpeakerEnds,
                BallotsCast = value.BallotsCast,
                Voters = value.Voters == null ? null : new List<int>(value.Voters),
                PresentedEvidence = value.PresentedEvidence == null ? null : new List<int>(value.PresentedEvidence),
                Verdict = Copy(value.Verdict),
            };
        }

        internal static AccusationView Copy(AccusationView value)
        {
            if (value == null) return null;
            return new AccusationView
            {
                Accuser = value.Accuser,
                Accused = value.Accused,
                Time = value.Time,
            };
        }

        internal static PrivateView Copy(PrivateView value)
        {
            if (value == null) return null;
            return new PrivateView
            {
                Role = value.Role,
                Objective = Copy(value.Objective),
                Informant = Copy(value.Informant),
                Notes = CopyList(value.Notes, Copy),
                ActiveInteraction = Copy(value.ActiveInteraction),
                HasAccused = value.HasAccused,
                AccusedPlayer = value.AccusedPlayer,
                HasVoted = value.HasVoted,
                Ballot = value.Ballot,
                RematchVoted = value.RematchVoted,
                SabotageCooldowns = value.SabotageCooldowns == null ? null : new List<double>(value.SabotageCooldowns),
                ReconnectToken = value.ReconnectToken,
                Cards = value.Cards == null ? null : new List<SabotageType>(value.Cards),
                Call = Copy(value.Call),
                CallCooldownUntil = value.CallCooldownUntil,
                TipsLeft = value.TipsLeft,
                FootageLeft = value.FootageLeft,
            };
        }

        internal static InteractionView Copy(InteractionView value)
        {
            if (value == null) return null;
            return new InteractionView
            {
                Verb = value.Verb,
                TargetId = value.TargetId,
                StartTime = value.StartTime,
                EndTime = value.EndTime,
            };
        }

        internal static ObjectiveView Copy(ObjectiveView value)
        {
            if (value == null) return null;
            return new ObjectiveView
            {
                CardId = value.CardId,
                Type = value.Type,
                Target = value.Target,
                Progress = value.Progress,
                Required = value.Required,
                Progress2 = value.Progress2,
                Required2 = value.Required2,
                Status = value.Status,
                Flags = value.Flags == null ? null : new List<string>(value.Flags),
            };
        }

        internal static InformantView Copy(InformantView value)
        {
            if (value == null) return null;
            return new InformantView
            {
                ScansLeft = value.ScansLeft,
                SealsLeft = value.SealsLeft,
                SealedEvidenceId = value.SealedEvidenceId,
                Scans = CopyList(value.Scans, Copy),
                CallLogsLeft = value.CallLogsLeft,
            };
        }

        internal static ScanResultView Copy(ScanResultView value)
        {
            if (value == null) return null;
            return new ScanResultView
            {
                Id = value.Id,
                Time = value.Time,
                EvidenceId = value.EvidenceId,
                ItemId = value.ItemId,
                ItemVariant = value.ItemVariant,
                DeskIndex = value.DeskIndex,
                ApparentOwner = value.ApparentOwner,
                Reliability = value.Reliability,
                Signal = value.Signal,
                NoteId = value.NoteId,
            };
        }

        internal static PrivateNote Copy(PrivateNote value)
        {
            if (value == null) return null;
            return new PrivateNote
            {
                Id = value.Id,
                Kind = value.Kind,
                Time = value.Time,
                Subject = value.Subject,
                Activity = value.Activity,
                TargetId = value.TargetId,
                DeskIndex = value.DeskIndex,
                HeldBefore = value.HeldBefore,
                HeldAfter = value.HeldAfter,
                Contents = value.Contents == null ? null : new List<string>(value.Contents),
                ScanId = value.ScanId,
                Presented = value.Presented,
                Calls = CopyList(value.Calls, Copy),
                ItemId = value.ItemId,
                Salted = value.Salted,
            };
        }

        internal static CallConfig Copy(CallConfig value)
        {
            if (value == null) return null;
            return new CallConfig
            {
                Enabled = value.Enabled,
                RingSeconds = value.RingSeconds,
                MaxCallSeconds = value.MaxCallSeconds,
                CooldownSeconds = value.CooldownSeconds,
                TipLineEnabled = value.TipLineEnabled,
                TipSeconds = value.TipSeconds,
                TipsPerPlayer = value.TipsPerPlayer,
            };
        }

        internal static CallRecordView Copy(CallRecordView value)
        {
            if (value == null) return null;
            return new CallRecordView
            {
                Caller = value.Caller,
                Callee = value.Callee,
                Start = value.Start,
                End = value.End,
                Answered = value.Answered,
                Tip = value.Tip,
                TipDesk = value.TipDesk,
            };
        }

        internal static CallView Copy(CallView value)
        {
            if (value == null) return null;
            return new CallView
            {
                State = value.State,
                Other = value.Other,
                Since = value.Since,
                Until = value.Until,
            };
        }

        internal static CameraDef Copy(CameraDef value)
        {
            if (value == null) return null;
            return new CameraDef
            {
                Id = value.Id,
                Position = value.Position,
                Yaw = value.Yaw,
                ZoneMin = value.ZoneMin,
                ZoneMax = value.ZoneMax,
                RoomId = value.RoomId,
            };
        }

        internal static CardConfig Copy(CardConfig value)
        {
            if (value == null) return null;
            return new CardConfig
            {
                Enabled = value.Enabled,
                CardsPerPlayer = value.CardsPerPlayer,
            };
        }

        internal static CareerConfig Copy(CareerConfig value)
        {
            if (value == null) return null;
            return new CareerConfig
            {
                QuotaMet = value.QuotaMet,
                Survived = value.Survived,
                ObjectiveWon = value.ObjectiveWon,
                InformantWon = value.InformantWon,
                Fired = value.Fired,
                Favourite = value.Favourite,
            };
        }

        internal static EmailConfig Copy(EmailConfig value)
        {
            if (value == null) return null;
            return new EmailConfig
            {
                ScreenOpenSeconds = value.ScreenOpenSeconds,
                LockScreenSeconds = value.LockScreenSeconds,
                TemplateCount = value.TemplateCount,
            };
        }

        internal static EmailView Copy(EmailView value)
        {
            if (value == null) return null;
            return new EmailView
            {
                Id = value.Id,
                Time = value.Time,
                FromPlayer = value.FromPlayer,
                DeskIndex = value.DeskIndex,
                TemplateId = value.TemplateId,
            };
        }

        internal static FeaturesView Copy(FeaturesView value)
        {
            if (value == null) return null;
            return new FeaturesView
            {
                Cards = value.Cards,
                Calls = value.Calls,
                TipLine = value.TipLine,
                Footage = value.Footage,
                Mail = value.Mail,
                CareerMultiplier = value.CareerMultiplier,
            };
        }

        internal static FootageConfig Copy(FootageConfig value)
        {
            if (value == null) return null;
            return new FootageConfig
            {
                Enabled = value.Enabled,
                SampleInterval = value.SampleInterval,
                WindowSeconds = value.WindowSeconds,
                SpeakerRequests = value.SpeakerRequests,
            };
        }

        internal static FootageSighting Copy(FootageSighting value)
        {
            if (value == null) return null;
            return new FootageSighting
            {
                Player = value.Player,
                Time = value.Time,
                Activity = value.Activity,
                Carrying = value.Carrying,
                Crouched = value.Crouched,
                Position = value.Position,
                Yaw = value.Yaw,
            };
        }

        internal static HighlightView Copy(HighlightView value)
        {
            if (value == null) return null;
            return new HighlightView
            {
                Key = value.Key,
                IncidentId = value.IncidentId,
                Actor = value.Actor,
                DeskIndex = value.DeskIndex,
                Start = value.Start,
                End = value.End,
                Samples = CopyList(value.Samples, Copy),
            };
        }

        internal static ManagerProfileDef Copy(ManagerProfileDef value)
        {
            if (value == null) return null;
            return new ManagerProfileDef
            {
                Id = value.Id,
                QuotaDelta = value.QuotaDelta,
                VoteWeight = value.VoteWeight,
                RiskWeight = value.RiskWeight,
                PerformanceWeight = value.PerformanceWeight,
                FavorWeight = value.FavorWeight,
                ObservationFactors = CopyList(value.ObservationFactors, Copy),
                EvidenceFactor = value.EvidenceFactor,
                EmailFactor = value.EmailFactor,
                VisionRange = value.VisionRange,
                VisionHalfAngle = value.VisionHalfAngle,
                SurpriseInspection = value.SurpriseInspection,
                HasFavourite = value.HasFavourite,
                FavorSource = value.FavorSource,
                FavorPerAction = value.FavorPerAction,
                SearchAllDrawers = value.SearchAllDrawers,
                ExtraFootageRequests = value.ExtraFootageRequests,
            };
        }

        internal static ObservationFactor Copy(ObservationFactor value)
        {
            if (value == null) return null;
            return new ObservationFactor
            {
                Kind = value.Kind,
                Factor = value.Factor,
            };
        }

        internal static OfficeStatusView Copy(OfficeStatusView value)
        {
            if (value == null) return null;
            return new OfficeStatusView
            {
                InternetDown = value.InternetDown,
                InternetBackAt = value.InternetBackAt,
                PowerDown = value.PowerDown,
                PowerBackAt = value.PowerBackAt,
            };
        }

        internal static RoomDef Copy(RoomDef value)
        {
            if (value == null) return null;
            return new RoomDef
            {
                Id = value.Id,
                Min = value.Min,
                Max = value.Max,
            };
        }

        internal static WeekDayDef Copy(WeekDayDef value)
        {
            if (value == null) return null;
            return new WeekDayDef
            {
                ProfileId = value.ProfileId,
                Cards = value.Cards,
                TipLine = value.TipLine,
                Footage = value.Footage,
                Mail = value.Mail,
                Calls = value.Calls,
                CareerMultiplier = value.CareerMultiplier,
            };
        }

        private static List<T> CopyList<T>(List<T> values, Func<T, T> copy)
        {
            if (values == null) return null;
            var result = new List<T>(values.Count);
            for (int i = 0; i < values.Count; i++) result.Add(copy(values[i]));
            return result;
        }

        private static Dictionary<string, List<int>> CopyHandlers(Dictionary<string, List<int>> values)
        {
            if (values == null) return null;
            var result = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var keys = new List<string>(values.Keys);
            keys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                var list = values[keys[i]];
                result.Add(keys[i], list == null ? null : new List<int>(list));
            }
            return result;
        }
    }
}
