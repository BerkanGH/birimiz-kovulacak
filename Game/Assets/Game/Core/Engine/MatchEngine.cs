using System;
using System.Collections.Generic;
namespace BirimizKovulacak.Core
{
    /// <summary>Single-threaded authoritative match. All time comes from the host.</summary>
    public sealed partial class MatchEngine
    {
        public MatchConfig Config { get; private set; }
        public OfficeLayout Layout { get; private set; }
        public int MatchId { get; private set; }
        public int Seed { get; private set; }
        public MatchPhase Phase { get; private set; }
        public int PhaseSerial { get; private set; }
        public int StateVersion { get; private set; }
        public bool IsFinished { get { return Phase == MatchPhase.Results || Phase == MatchPhase.Abandoned; } }
        public MatchResultView Result { get; private set; }
        private readonly PlayerState[] players = new PlayerState[Protocol.MaxPlayers];
        private readonly List<int> playerIds = new List<int>();
        public IReadOnlyList<int> Players { get; private set; }
        public int CallPartnerOf(int player)
        {
            return Present(player) && players[player].Private.Call.State == CallState.Active ? players[player].Private.Call.Other : PlayerIds.None;
        }
        private bool Present(int player) { return PlayerIds.IsValid(player) && players[player] != null; }
        private readonly List<ItemState> items = new List<ItemState>();
        private readonly List<DeskState> desks = new List<DeskState>();
        private readonly List<IncidentState> incidents = new List<IncidentState>();
        private readonly List<EvidenceState> evidence = new List<EvidenceState>();
        private readonly List<ObservationView> observations = new List<ObservationView>();
        private readonly List<GameEvent> events = new List<GameEvent>();
        private readonly List<LogEntry> log = new List<LogEntry>();
        private readonly MeetingView meeting = new MeetingView();
        private readonly ManagerView manager = new ManagerView();
        private readonly QuotaView quota = new QuotaView();
        private readonly DeterministicRandom random;
        private readonly IWorldQueries world;
        private readonly SessionHistory history;
        private double clock, phaseStart, phaseEnd;
        private AbandonReason abandonReason;
        private int processedRequests, rejectedRequests;

        public MatchEngine(MatchConfig config, OfficeLayout layout, MatchSetup setup, IWorldQueries world)
        {
            Config = Snapshot.Copy(config ?? throw new ArgumentNullException(nameof(config))); Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            if (setup == null) throw new ArgumentNullException(nameof(setup));
            if (setup.Players == null || setup.Players.Count < Protocol.MinPlayers || setup.Players.Count > Protocol.MaxPlayers) throw new ArgumentException("Three to six players are required.", nameof(setup));
            this.world = world ?? OpenWorld.Instance; MatchId = setup.MatchId; Seed = setup.Seed;
            random = new DeterministicRandom(Seed); history = CopyHistory(setup.History); clock = setup.StartTime;
            var assignments = new List<int>(); foreach (DeskDef d in layout.Desks) assignments.Add(d.Index); assignments.Sort();
            if (assignments.Count < setup.Players.Count) throw new ArgumentException("One desk per player is required.", nameof(layout));
            var assigned = new List<int>();
            for (int k = 0; k < setup.Players.Count; k++)
            {
                if (!assignments.Contains(k)) throw new ArgumentException("Assigned desks must have contiguous indices from zero.", nameof(layout));
                assigned.Add(k);
            }
            if (config.ShuffleDesks) random.Shuffle(assigned);
            foreach (PlayerSlotInfo p in setup.Players)
            {
                if (p == null || !PlayerIds.IsValid(p.Id) || players[p.Id] != null) throw new ArgumentException("Player ids must be distinct slots 0..5.", nameof(setup));
                var state = new PlayerState(); players[p.Id] = state;
                playerIds.Add(p.Id);
                state.Public = new PlayerPublicView { Id = p.Id, Name = p.Name ?? "", ColorIndex = p.ColorIndex, Connected = true };
                for (int s = 0; s <= (int)SabotageType.PowerCut; s++) state.Private.SabotageCooldowns.Add(0);
            }
            playerIds.Sort(); Players = playerIds.AsReadOnly();
            for (int k = 0; k < playerIds.Count; k++)
            {
                PlayerState state = players[playerIds[k]];
                state.Public.DeskIndex = assigned[k];
                state.Avatar.Position = layout.GetDesk(assigned[k]).SeatPosition;
            }
            foreach (int index in assignments) { DeskDef d = layout.GetDesk(index); desks.Add(new DeskState { Def = d, View = new DeskView { Index = index, Id = d.Id, AssignedOwner = OwnerOfDesk(index) } }); }
            desks.Sort((a, b) => a.View.Index.CompareTo(b.View.Index));
            foreach (ItemDef def in layout.Items)
            {
                if (def.Kind == ItemKind.Nameplate && !Present(def.NameplateOf)) continue;
                string slot = def.InitialSlotId;
                if (def.Kind == ItemKind.Nameplate && Present(def.NameplateOf)) slot = layout.GetDesk(players[def.NameplateOf].Public.DeskIndex).NameplateSlotId;
                var item = new ItemState { View = new ItemView { Id = def.Id, Kind = def.Kind, Variant = def.Variant ?? "", NameplateOf = def.NameplateOf, SlotId = slot ?? "", Location = ItemLocationKind.Slot, SlotIndex = FreeIndex(slot) } };
                item.PlacementSlot = item.View.SlotId; items.Add(item);
            }
            InitializeProfiles(setup);
            InitializeCareer();
            RefreshDesks(); quota.Target = Config.QuotaTarget; manager.Position = layout.ManagerHome; manager.Yaw = layout.ManagerHomeYaw;
            AssignRoles(setup); InitializeSabotageV2(setup); InitializeCalls(); InitializeTasks(); ChangePhase(MatchPhase.Briefing, clock); InitializeMail(); InitializeFootage();
        }
        public void Tick(double now)
        {
            if (!Finite(now) || now < clock || Phase == MatchPhase.Abandoned) return;
            // Visit absolute deadlines so skipped host frames cannot complete work after the freeze.
            do
            {
                double next = now; if (!IsFinished) next = Math.Min(next, phaseEnd);
                foreach (int p in playerIds)
                {
                    if (players[p].Interaction != null) next = Math.Min(next, Math.Max(clock, players[p].Interaction.End));
                    if (!players[p].Public.Connected && Phase < MatchPhase.MeetingDiscussion) next = Math.Min(next, Math.Max(clock, players[p].Public.DisconnectDeadline));
                }
                NextTaskTime(ref next); NextManagerTime(ref next); NextSabotageV2Time(ref next); NextCallTime(ref next); NextMailTime(ref next); NextFootageTime(ref next);
                double previous = clock; clock = Math.Max(clock, next);
                // At an exact deadline, drain even zero-length phases before deciding whether
                // reconnect grace can abandon: reaching the meeting commits the verdict/results.
                while (!IsFinished && clock >= phaseEnd) ChangePhase((MatchPhase)((int)Phase + 1), phaseEnd);
                CheckReconnectExpiry();
                if (Phase == MatchPhase.Abandoned) break;
                AdvanceSabotageV2(); AdvanceCalls(); AdvanceTasks(); AdvanceMail();
                foreach (int p in playerIds) { InteractionState a = players[p].Interaction; if (a != null && a.End <= clock) CompleteInteraction(p, a); }
                AdvanceManager(previous); AdvanceFootage();
                if (clock >= now)
                {
                    // A zero-length phase or a print finishing at this timestamp still has to drain.
                    double due = IsFinished ? double.PositiveInfinity : phaseEnd;
                    NextTaskTime(ref due); NextManagerTime(ref due); NextSabotageV2Time(ref due); NextCallTime(ref due); NextMailTime(ref due); NextFootageTime(ref due);
                    if (due > clock) break;
                }
            } while (true);
        }
        public ActionResult Submit(int player, ActionRequest request, double now)
        {
            uint id = request == null ? 0 : request.RequestId;
            if (request == null) return Reject(player, id, FailureCode.InvalidTarget);
            if (request.MatchId != MatchId) return Reject(player, id, FailureCode.WrongMatch);
            if (!Present(player) || !players[player].Public.Connected) return Reject(player, id, FailureCode.NotConnected);
            PlayerState p = players[player]; if (p.Requests.Contains(id)) return Reject(player, id, FailureCode.Duplicate);
            p.Requests.Enqueue(id); if (p.Requests.Count > 64) p.Requests.Dequeue(); processedRequests++; Tick(now);
            if (!Allowed(request.Verb)) return Reject(player, id, FailureCode.WrongPhase);
            if (IsPhysical(request.Verb) && p.Interaction != null) return Reject(player, id, FailureCode.Busy);
            SabotageType? card = CardTypeV2(request.Verb);
            if (card.HasValue)
            {
                FailureCode permission = CheckCardAndLimitV2(player, card.Value);
                if (permission != FailureCode.None) return Reject(player, id, permission);
            }
            if (request.Verb == ActionVerb.CancelInteraction) { Cancel(player); return ActionResult.Ok(id); }
            if (request.Verb == ActionVerb.SetWorkPose) { p.Public.WorkPose = request.IntParam == 1; Changed(); return ActionResult.Ok(id); }
            if (request.Verb == ActionVerb.Emote)
            {
                FailureCode emote = SubmitEmote(player, request.IntParam);
                return emote == FailureCode.None ? ActionResult.Ok(id) : Reject(player, id, emote);
            }
            FailureCode failure = FailureCode.InvalidTarget; bool handled = false; double end = 0;
            SubmitCalls(player, request, ref handled, ref failure, ref end);
            SubmitPhysical(player, request, ref handled, ref failure, ref end);
            SubmitInformant(player, request, ref handled, ref failure); SubmitMeeting(player, request, ref handled, ref failure);
            SubmitFootage(player, request, ref handled, ref failure);
            if (!handled || failure != FailureCode.None) return Reject(player, id, failure);
            Changed(); return ActionResult.Ok(id, end);
        }
        public void UpdateAvatar(int player, AvatarSample sample)
        {
            if (!Present(player) || !Finite(sample.Position.X) || !Finite(sample.Position.Y) || !Finite(sample.Position.Z) || !Finite(sample.Yaw) || !Finite(sample.Speed)) return;
            PlayerState p = players[player]; p.Avatar = sample;
            if (p.Public.Crouched != sample.Crouched) { p.Public.Crouched = sample.Crouched; Changed(); }
            if (p.Public.WorkPose && sample.Speed > 0.5f) { p.Public.WorkPose = false; Changed(); }
            if (p.Interaction != null && Vec3.FlatDistance(p.Interaction.StartPosition, sample.Position) > Config.Interaction.MoveCancelDistance) Cancel(player);
        }
        public void SetConnected(int player, bool connected, double now)
        {
            if (!Present(player)) return; Tick(now); if (Phase == MatchPhase.Abandoned) return;
            PlayerState p = players[player]; if (p.Public.Connected == connected) return;
            p.Public.Connected = connected; p.Public.DisconnectDeadline = connected ? 0 : clock + Config.Reconnect.GraceSeconds;
            if (!connected) { EndCall(player); Cancel(player); DropItem(player, false); p.Public.WorkPose = false; }
            Emit(connected ? EventType.PlayerReconnected : EventType.PlayerDisconnected, player);
        }
        public void Abandon(AbandonReason reason, double now)
        {
            if (Phase == MatchPhase.Abandoned) return; if (Finite(now)) clock = Math.Max(clock, now);
            abandonReason = reason == AbandonReason.None ? AbandonReason.HostAborted : reason;
            Result = null; ChangePhase(MatchPhase.Abandoned, clock); Emit(EventType.MatchAbandoned, arg: (int)abandonReason);
        }
        public List<GameEvent> DrainEvents() { var result = new List<GameEvent>(events); events.Clear(); return result; }
        public bool AllRematchVotesIn
        {
            get { if (Phase != MatchPhase.Results) return false; bool any = false; foreach (int id in playerIds) { PlayerState p = players[id]; if (p.Public.Connected) { any = true; if (!p.Private.RematchVoted) return false; } } return any; }
        }
        private void ChangePhase(MatchPhase phase, double start)
        {
            MatchPhase previous = Phase; Phase = phase; phaseStart = start; phaseEnd = start + Duration(phase); PhaseSerial++;
            if (phase == MatchPhase.MeetingDiscussion || phase == MatchPhase.Abandoned)
            {
                foreach (int p in playerIds) { Cancel(p); DropItem(p, false); players[p].Public.WorkPose = false; }
                quota.Frozen = true; FreezeEvidence();
                EndOutagesV2();
            }
            CallsPhaseChanged();
            MailPhaseChanged();
            FootagePhaseChanged();
            Emit(EventType.PhaseChanged, arg: (int)phase); TasksPhaseChanged(previous); ManagerPhaseChanged(previous); MeetingPhaseChanged(previous);
        }
        private double Duration(MatchPhase phase)
        {
            PhaseDurations d = Config.Durations;
            switch (phase)
            {
                case MatchPhase.Briefing: return Math.Max(0, d.Briefing);
                case MatchPhase.Work1: return Math.Max(0, d.Work1);
                case MatchPhase.Raid: return Math.Max(0, d.Raid);
                case MatchPhase.Work2: return Math.Max(0, d.Work2);
                case MatchPhase.FinalInspection: return Math.Max(0, d.FinalInspection);
                case MatchPhase.MeetingDiscussion: return Math.Max(0, d.MeetingDiscussion);
                case MatchPhase.MeetingDefense1: case MatchPhase.MeetingDefense2: return Math.Max(0, d.MeetingDefense);
                case MatchPhase.MeetingVote: return Math.Max(0, d.MeetingVote);
                case MatchPhase.MeetingVerdict: return Math.Max(0, d.MeetingVerdict);
                case MatchPhase.Results: return Math.Max(0, d.Results);
                default: return 0;
            }
        }
        private bool Office { get { return Phase >= MatchPhase.Work1 && Phase <= MatchPhase.FinalInspection; } }
        private bool Allowed(ActionVerb verb)
        {
            if (Phase == MatchPhase.Abandoned || verb == ActionVerb.None || (int)verb > (int)ActionVerb.Emote) return false;
            if (verb == ActionVerb.Emote) return true;
            if (verb >= ActionVerb.CallPlayer && verb <= ActionVerb.HangUp) return Phase >= MatchPhase.Briefing && Phase <= MatchPhase.FinalInspection;
            if (verb == ActionVerb.TipManager) return Office;
            if (verb == ActionVerb.PullCallLogs) return Phase >= MatchPhase.Briefing && Phase <= MatchPhase.MeetingDefense2;
            if (verb == ActionVerb.RequestFootage) return FootageMeeting;
            if (verb == ActionVerb.CutInternet || verb == ActionVerb.CutPower) return Office && Phase < MatchPhase.FinalInspection;
            if (verb >= ActionVerb.LockScreen && verb <= ActionVerb.ResetFuse) return Office;
            if (verb == ActionVerb.CancelInteraction) return true;
            if (verb <= ActionVerb.SetWorkPose || verb == ActionVerb.AuditScanItem || verb == ActionVerb.SealEvidence) return Office;
            if (verb == ActionVerb.AuditScanEvidence) return Office || Phase == MatchPhase.MeetingDiscussion;
            if (verb == ActionVerb.Accuse) return Phase == MatchPhase.MeetingDiscussion;
            if (verb == ActionVerb.PresentNote) return Phase >= MatchPhase.MeetingDiscussion && Phase <= MatchPhase.MeetingDefense2;
            if (verb == ActionVerb.CastBallot) return Phase == MatchPhase.MeetingVote;
            return verb == ActionVerb.RematchVote && Phase == MatchPhase.Results;
        }
        private static bool IsPhysical(ActionVerb verb) { return (verb >= ActionVerb.PickUp && verb <= ActionVerb.SetWorkPose) || (verb >= ActionVerb.LockScreen && verb <= ActionVerb.ResetFuse) || verb == ActionVerb.TipManager; }
        private static bool Finite(double number) { return !double.IsNaN(number) && !double.IsInfinity(number); }
        private void Changed() { StateVersion++; }
        private ActionResult Reject(int player, uint id, FailureCode failure)
        {
            rejectedRequests++; if (Present(player)) Emit(EventType.ActionRejected, player, arg: (int)failure, audience: Audience.Player, recipient: player);
            return ActionResult.Fail(id, failure);
        }
        private void Emit(EventType type, int actor = PlayerIds.None, string target = "", string item = "", int arg = -1, string text = "", Audience audience = Audience.All, int recipient = PlayerIds.None)
        {
            events.Add(new GameEvent { Type = type, Time = clock, Actor = actor, TargetId = target ?? "", ItemId = item ?? "", IntArg = arg, StrArg = text ?? "", Audience = audience, AudiencePlayer = recipient, Phase = Phase });
            if (audience == Audience.All) log.Add(new LogEntry { Time = clock, Kind = type, Actor = actor, TargetId = target ?? "", IntArg = arg, StrArg = text ?? "" });
            Changed();
        }
        private void Cancel(int player)
        {
            if (players[player].Interaction == null) return; players[player].Interaction = null; ClearActivity(player);
            Emit(EventType.InteractionCancelled, player, audience: Audience.Player, recipient: player);
        }
        private void ClearActivity(int p) { players[p].Public.Activity = PublicActivity.None; players[p].Public.ActivityTargetId = ""; players[p].Public.ActivityStart = players[p].Public.ActivityEnd = 0; RefreshCallActivity(p); }
        private int OwnerOfDesk(int index) { foreach (int p in playerIds) if (players[p] != null && players[p].Public.DeskIndex == index) return p; return PlayerIds.None; }
        private DeskState Desk(int index) { foreach (DeskState desk in desks) if (desk.View.Index == index) return desk; return null; }
        private ItemState Item(string id) { foreach (ItemState item in items) if (item.View.Id == id) return item; return null; }
        private ItemState Held(int p) { return Item(players[p].Public.CarryingItemId); }
        private int FreeIndex(string slot) { for (int n = 0; ; n++) { bool occupied = false; foreach (ItemState i in items) if (i.View.Location == ItemLocationKind.Slot && i.View.SlotId == slot && i.View.SlotIndex == n) { occupied = true; break; } if (!occupied) return n; } }
        private int ApparentOwner(int desk) { DeskState d = Desk(desk); return d == null ? PlayerIds.None : d.View.DisplayedNameplateOf == PlayerIds.None ? d.View.AssignedOwner : d.View.DisplayedNameplateOf; }
        private void RefreshDesks()
        {
            foreach (DeskState d in desks) { d.View.DisplayedNameplateOf = PlayerIds.None; d.View.ClutterCount = 0;
                foreach (ItemState i in items) if (i.View.Location == ItemLocationKind.Slot) { if (i.View.SlotId == d.Def.NameplateSlotId) d.View.DisplayedNameplateOf = i.View.NameplateOf; if (i.View.SlotId == d.Def.SurfaceSlotId && i.View.Kind == ItemKind.Clutter) d.View.ClutterCount++; }
            }
        }
        private bool Visible(ItemState item, int p) { if (item.View.Location == ItemLocationKind.Archived) return false; SlotDef slot = item.View.Location == ItemLocationKind.Slot ? Layout.GetSlot(item.View.SlotId) : null; return slot == null || !slot.Closed || item.KnownBy[p]; }
        private Vec3 ItemPosition(ItemState item) { if (item.View.Location == ItemLocationKind.Carried) return players[item.View.CarrierId].Avatar.Position; SlotDef slot = Layout.GetSlot(item.View.SlotId); return item.View.Location == ItemLocationKind.Slot && slot != null ? slot.Position : item.View.FloorPosition; }
        partial void AssignRoles(MatchSetup setup);
        partial void InitializeTasks();
        partial void TasksPhaseChanged(MatchPhase previous);
        partial void NextTaskTime(ref double next);
        partial void AdvanceTasks();
        partial void SubmitPhysical(int p, ActionRequest request, ref bool handled, ref FailureCode failure, ref double end);
        partial void CompleteInteraction(int p, InteractionState interaction);
        partial void DropItem(int p, bool forward);
        partial void ManagerPhaseChanged(MatchPhase previous);
        partial void NextManagerTime(ref double next);
        partial void AdvanceManager(double previous);
        partial void FreezeEvidence();
        partial void SubmitInformant(int p, ActionRequest request, ref bool handled, ref FailureCode failure);
        partial void SubmitMeeting(int p, ActionRequest request, ref bool handled, ref FailureCode failure);
        partial void MeetingPhaseChanged(MatchPhase previous);
        partial void CheckReconnectExpiry();
    }
}
