using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    public sealed partial class MatchEngine
    {
        private sealed class SaltedMugState
        {
            internal int Actor;
            internal int Incident;
        }

        private readonly OfficeStatusView officeStatus = new OfficeStatusView();
        private readonly List<EmailView> inbox = new List<EmailView>();
        private readonly Dictionary<string, SaltedMugState> saltedMugs = new Dictionary<string, SaltedMugState>(StringComparer.Ordinal);
        private readonly Dictionary<int, int> plugIncidents = new Dictionary<int, int>();
        private readonly List<RecapBeat> sabotageRecap = new List<RecapBeat>();
        private int internetIncident, powerIncident;
        private double pausedPrintRemaining;

        private void InitializeSabotageV2(MatchSetup setup)
        {
            if (!features.Cards) return;
            var pool = new List<SabotageType>();
            foreach (SabotageDef def in Config.Sabotage)
                if (!def.Basic && def.Type != SabotageType.EvidenceTampering && !pool.Contains(def.Type)) pool.Add(def.Type);
            pool.Sort();
            foreach (int p in playerIds)
            {
                List<SabotageType> forced = setup.ForceCards != null && p < setup.ForceCards.Count ? setup.ForceCards[p] : null;
                if (forced != null)
                {
                    foreach (SabotageType type in forced)
                        if (pool.Contains(type) && !players[p].Private.Cards.Contains(type)) players[p].Private.Cards.Add(type);
                }
                else
                {
                    var hand = new List<SabotageType>(pool);
                    random.Shuffle(hand);
                    for (int n = 0; n < Math.Min(Math.Max(0, Config.Cards.CardsPerPlayer), hand.Count); n++)
                        players[p].Private.Cards.Add(hand[n]);
                }
            }
        }

        private static SabotageType? CardTypeV2(ActionVerb verb)
        {
            switch (verb)
            {
                case ActionVerb.LockWorkstation: return SabotageType.LockWorkstation;
                case ActionVerb.SendFakeEmail: return SabotageType.FakeEmail;
                case ActionVerb.PullPlug: return SabotageType.PullPlug;
                case ActionVerb.SaltCoffee: return SabotageType.SaltCoffee;
                case ActionVerb.CutInternet: return SabotageType.InternetOutage;
                case ActionVerb.CutPower: return SabotageType.PowerCut;
                default: return null;
            }
        }

        private FailureCode CheckCardAndLimitV2(int p, SabotageType type)
        {
            SabotageDef def = Config.GetSabotage(type);
            if (def == null) return FailureCode.InvalidTarget;
            if (!def.Basic && type != SabotageType.EvidenceTampering)
            {
                if (!features.Cards) return FailureCode.FeatureDisabled;
                if (!players[p].Private.Cards.Contains(type)) return FailureCode.MissingCard;
            }
            if (def.MaxPerMatch > 0)
            {
                int used = 0;
                foreach (IncidentState incident in incidents) if (incident.Reveal.Type == type) used++;
                if (used >= def.MaxPerMatch) return FailureCode.LimitReached;
            }
            return FailureCode.None;
        }

        private FailureCode ValidatePcV2(DeskState desk, ActionVerb verb)
        {
            if (desk.View.Unplugged) return FailureCode.PcUnplugged;
            if ((verb == ActionVerb.WriteReport || verb == ActionVerb.SendFakeEmail) && officeStatus.InternetDown)
                return FailureCode.NoInternet;
            return FailureCode.None;
        }

        private FailureCode ValidatePhysicalV2(int p, InteractionState a, bool completion, out double duration)
        {
            duration = 0;
            ActionVerb verb = a.Request.Verb;
            string id = a.Request.TargetId;
            StationDef station = Layout.GetStation(id);
            DeskDef deskDef = Layout.GetDesk(id);
            DeskState desk = deskDef != null ? Desk(deskDef.Index) : station != null ? Desk(station.DeskIndex) : null;
            ItemState held = Held(p);
            Vec3 target = players[p].Avatar.Position;
            a.Sabotage = CardTypeV2(verb);
            a.Victim = PlayerIds.None;
            a.DeskIndex = -1;
            if (a.Sabotage.HasValue)
            {
                FailureCode card = CheckCardAndLimitV2(p, a.Sabotage.Value);
                if (card != FailureCode.None) return card;
            }
            switch (verb)
            {
                case ActionVerb.LockScreen:
                case ActionVerb.SendFakeEmail:
                    if (station == null || station.Kind != StationKind.Computer || desk == null || !Present(desk.View.AssignedOwner))
                        return FailureCode.InvalidTarget;
                    if (verb == ActionVerb.LockScreen && desk.View.AssignedOwner != p) return FailureCode.NotOwnDesk;
                    if (verb == ActionVerb.SendFakeEmail && desk.View.AssignedOwner == p) return FailureCode.CannotTargetSelf;
                    FailureCode pc = ValidatePcV2(desk, verb);
                    if (pc != FailureCode.None) return pc;
                    if (desk.View.PcLocked) return FailureCode.StationLocked;
                    if (!desk.View.ScreenOpen) return FailureCode.ScreenLocked;
                    if (verb == ActionVerb.SendFakeEmail && (a.Request.IntParam < 0 || a.Request.IntParam >= Config.Email.TemplateCount))
                        return FailureCode.InvalidTarget;
                    a.DeskIndex = desk.View.Index; a.Victim = desk.View.AssignedOwner;
                    a.Reservation = id; a.Activity = PublicActivity.Typing; target = station.Position;
                    duration = verb == ActionVerb.LockScreen ? Config.Email.LockScreenSeconds : Config.GetSabotage(SabotageType.FakeEmail).DurationSeconds;
                    break;
                case ActionVerb.PullPlug:
                case ActionVerb.PlugIn:
                    if (deskDef == null || desk == null || !Present(desk.View.AssignedOwner)) return FailureCode.InvalidTarget;
                    if (verb == ActionVerb.PullPlug && desk.View.AssignedOwner == p) return FailureCode.CannotTargetSelf;
                    if (verb == ActionVerb.PullPlug ? desk.View.Unplugged : !desk.View.Unplugged)
                        return verb == ActionVerb.PullPlug ? FailureCode.AlreadyActive : FailureCode.NotActive;
                    a.DeskIndex = desk.View.Index; a.Victim = desk.View.AssignedOwner;
                    a.Reservation = id + ":plug"; a.Activity = PublicActivity.Plugging; target = desk.Def.Position;
                    duration = verb == ActionVerb.PullPlug ? Config.GetSabotage(SabotageType.PullPlug).DurationSeconds : Config.GetSabotage(SabotageType.PullPlug).ReverseSeconds;
                    break;
                case ActionVerb.SaltCoffee:
                case ActionVerb.TasteCoffee:
                    if (held == null) return FailureCode.HandsEmpty;
                    if (held.View.Kind != ItemKind.Mug || held.View.Fill != MugFill.Full) return FailureCode.WrongItem;
                    if (verb == ActionVerb.SaltCoffee)
                    {
                        if (station == null || station.Kind != StationKind.CoffeeMachine) return FailureCode.InvalidTarget;
                        if (saltedMugs.ContainsKey(held.View.Id)) return FailureCode.AlreadyActive;
                        a.Reservation = id; a.Activity = PublicActivity.Brewing; target = station.Position;
                        duration = Config.GetSabotage(SabotageType.SaltCoffee).DurationSeconds;
                    }
                    else { a.Activity = PublicActivity.Drinking; duration = Config.Tasks.TasteSeconds; }
                    break;
                case ActionVerb.CutInternet:
                case ActionVerb.RestartRouter:
                    if (station == null || station.Kind != StationKind.Router) return FailureCode.InvalidTarget;
                    if (verb == ActionVerb.CutInternet ? officeStatus.InternetDown : !officeStatus.InternetDown)
                        return verb == ActionVerb.CutInternet ? FailureCode.AlreadyActive : FailureCode.NotActive;
                    a.Reservation = id; a.Activity = PublicActivity.ServerWork; target = station.Position;
                    duration = verb == ActionVerb.CutInternet ? Config.GetSabotage(SabotageType.InternetOutage).DurationSeconds : Config.GetSabotage(SabotageType.InternetOutage).ReverseSeconds;
                    break;
                case ActionVerb.CutPower:
                case ActionVerb.ResetFuse:
                    if (station == null || station.Kind != StationKind.FuseBox) return FailureCode.InvalidTarget;
                    if (verb == ActionVerb.CutPower ? officeStatus.PowerDown : !officeStatus.PowerDown)
                        return verb == ActionVerb.CutPower ? FailureCode.AlreadyActive : FailureCode.NotActive;
                    a.Reservation = id; a.Activity = PublicActivity.FuseWork; target = station.Position;
                    duration = verb == ActionVerb.CutPower ? Config.GetSabotage(SabotageType.PowerCut).DurationSeconds : Config.GetSabotage(SabotageType.PowerCut).ReverseSeconds;
                    break;
                default: return FailureCode.InvalidTarget;
            }
            if (a.Sabotage.HasValue)
            {
                FailureCode limit = FailureCode.None;
                CheckSabotage(p, a, ref limit);
                if (limit != FailureCode.None) return limit;
            }
            if (a.Reservation.Length != 0 && Reserved(a.Reservation, p)) return FailureCode.TargetBusy;
            return Vec3.FlatDistance(players[p].Avatar.Position, target) <= Config.Interaction.Range + Config.Interaction.RangeSlack
                ? FailureCode.None : FailureCode.TooFar;
        }

        private void ApplyPhysicalV2(int p, InteractionState a)
        {
            DeskState desk = Desk(a.DeskIndex);
            ItemState held = Held(p);
            switch (a.Request.Verb)
            {
                case ActionVerb.LockScreen: CloseScreenV2(desk); break;
                case ActionVerb.SendFakeEmail:
                    var email = new EmailView { Id = inbox.Count + 1, Time = clock, FromPlayer = desk.View.AssignedOwner, DeskIndex = desk.View.Index, TemplateId = a.Request.IntParam };
                    inbox.Add(email);
                    IncidentState fake = RecordIncident(p, SabotageType.FakeEmail, email.FromPlayer, email.DeskIndex, "", a.Request.TargetId);
                    fake.Reveal.Discovered = true; fake.Reveal.BlamedPlayer = email.FromPlayer; fake.BlamedPlayers.Add(email.FromPlayer);
                    EvidenceState record = AddEvidence(EvidenceKind.EmailLog, null, a.Request.TargetId, email.DeskIndex, DiscoverySource.System, attribution: email.FromPlayer);
                    fake.EvidenceIds.Add(record.View.Id);
                    record.View.EmailTemplate = email.TemplateId;
                    Emit(EventType.EmailSent, arg: email.Id);
                    AddObservation(ObservationKind.InappropriateEmail, email.FromPlayer, email.DeskIndex, Config.Risk.InappropriateEmail);
                    SabotageBeatV2("recap.fake_email", p, email.FromPlayer, "", email.DeskIndex);
                    break;
                case ActionVerb.PullPlug:
                    desk.View.Unplugged = true; CloseScreenV2(desk);
                    IncidentState plug = RecordIncident(p, SabotageType.PullPlug, desk.View.AssignedOwner, desk.View.Index, "", a.Request.TargetId);
                    plugIncidents[desk.View.Index] = plug.Reveal.Id;
                    InteractionState work = players[desk.View.AssignedOwner].Interaction;
                    if (work != null && work.Request.Verb == ActionVerb.WriteReport && work.Request.TargetId == desk.Def.ComputerId)
                    { Cancel(desk.View.AssignedOwner); Consequence(plug); }
                    Emit(EventType.PlugPulled, target: desk.Def.Id);
                    SabotageBeatV2("recap.pulled_plug", p, desk.View.AssignedOwner, "", desk.View.Index);
                    break;
                case ActionVerb.PlugIn:
                    desk.View.Unplugged = false;
                    int plugId; if (plugIncidents.TryGetValue(desk.View.Index, out plugId)) ReverseEffect(p, plugId);
                    Emit(EventType.PlugRestored, p, desk.Def.Id); break;
                case ActionVerb.SaltCoffee:
                    IncidentState salt = RecordIncident(p, SabotageType.SaltCoffee, PlayerIds.None, -1, held.View.Id, a.Request.TargetId);
                    saltedMugs[held.View.Id] = new SaltedMugState { Actor = p, Incident = salt.Reveal.Id };
                    SabotageBeatV2("recap.salted", p, PlayerIds.None, held.View.Variant, -1);
                    break;
                case ActionVerb.TasteCoffee:
                    SaltedMugState tasted;
                    bool wasSalted = saltedMugs.TryGetValue(held.View.Id, out tasted);
                    if (wasSalted) { ReverseEffect(p, tasted.Incident); saltedMugs.Remove(held.View.Id); }
                    held.View.Fill = MugFill.Used;
                    AddPrivateNote(p, new PrivateNote { Kind = NoteKind.TasteResult, ItemId = held.View.Id, Salted = wasSalted });
                    Emit(EventType.CoffeeTasted, p, item: held.View.Id, arg: wasSalted ? 1 : 0, audience: Audience.Player, recipient: p);
                    break;
                case ActionVerb.CutInternet:
                    internetIncident = RecordIncident(p, SabotageType.InternetOutage, PlayerIds.None, -1, "", a.Request.TargetId).Reveal.Id;
                    officeStatus.InternetDown = true;
                    officeStatus.InternetBackAt = clock + Math.Max(0, Config.GetSabotage(SabotageType.InternetOutage).EffectSeconds);
                    pausedPrintRemaining = printQueue.Count == 0 ? 0 : Math.Max(0, printEnds - clock);
                    if (printQueue.Count > 0) printEnds = double.PositiveInfinity;
                    foreach (int worker in playerIds)
                        if (players[worker].Interaction != null && players[worker].Interaction.Request.Verb == ActionVerb.WriteReport) Cancel(worker);
                    Emit(EventType.InternetDown); SabotageBeatV2("recap.internet_down", p, PlayerIds.None, "", -1); break;
                case ActionVerb.RestartRouter: RestoreInternetV2(p); break;
                case ActionVerb.CutPower:
                    powerIncident = RecordIncident(p, SabotageType.PowerCut, PlayerIds.None, -1, "", a.Request.TargetId).Reveal.Id;
                    officeStatus.PowerDown = true;
                    officeStatus.PowerBackAt = clock + Math.Max(0, Config.GetSabotage(SabotageType.PowerCut).EffectSeconds);
                    Emit(EventType.PowerDown); SabotageBeatV2("recap.power_down", p, PlayerIds.None, "", -1); break;
                case ActionVerb.ResetFuse: RestorePowerV2(p); break;
            }
        }

        private void OpenScreenV2(int p)
        {
            DeskState desk = Desk(players[p].Public.DeskIndex);
            desk.View.ScreenOpen = true; desk.View.ScreenOpenUntil = clock + Math.Max(0, Config.Email.ScreenOpenSeconds);
        }

        private static void CloseScreenV2(DeskState desk)
        {
            desk.View.ScreenOpen = false; desk.View.ScreenOpenUntil = 0;
        }

        private bool SaltedDeliveryV2(int p, ItemState item)
        {
            SaltedMugState salt;
            if (!saltedMugs.TryGetValue(item.View.Id, out salt)) return false;
            IncidentState incident = incidents.Find(i => i.Reveal.Id == salt.Incident);
            incident.Reveal.Victim = p; incident.Reveal.Discovered = true; incident.Reveal.BlamedPlayer = p;
            if (!incident.BlamedPlayers.Contains(p)) incident.BlamedPlayers.Add(p);
            if (p != salt.Actor) Consequence(incident);
            EndEffect(salt.Incident); saltedMugs.Remove(item.View.Id);
            AddObservation(ObservationKind.SaltyCoffee, p, -1, Config.Risk.SaltyCoffee);
            SabotageBeatV2("recap.salty_delivery", p, salt.Actor, item.View.Variant, -1);
            return true;
        }

        private void ClearSaltV2(ItemState item)
        {
            SaltedMugState salt;
            if (item != null && saltedMugs.TryGetValue(item.View.Id, out salt))
            { EndEffect(salt.Incident); saltedMugs.Remove(item.View.Id); }
        }

        private bool EffectiveV2(IncidentState incident)
        {
            switch (incident.Reveal.Type)
            {
                case SabotageType.FakeEmail: return true;
                case SabotageType.PullPlug:
                    int plug; DeskState desk = Desk(incident.Reveal.DeskIndex);
                    return desk != null && desk.View.Unplugged && plugIncidents.TryGetValue(desk.View.Index, out plug) && plug == incident.Reveal.Id;
                case SabotageType.SaltCoffee:
                    SaltedMugState salt;
                    return saltedMugs.TryGetValue(incident.Reveal.ItemId, out salt) && salt.Incident == incident.Reveal.Id;
                case SabotageType.InternetOutage: return officeStatus.InternetDown && internetIncident == incident.Reveal.Id;
                case SabotageType.PowerCut: return officeStatus.PowerDown && powerIncident == incident.Reveal.Id;
                default: return false;
            }
        }

        private void NextSabotageV2Time(ref double next)
        {
            if (officeStatus.InternetDown) next = Math.Min(next, Math.Max(clock, officeStatus.InternetBackAt));
            if (officeStatus.PowerDown) next = Math.Min(next, Math.Max(clock, officeStatus.PowerBackAt));
            foreach (DeskState desk in desks)
                if (desk.View.ScreenOpen) next = Math.Min(next, Math.Max(clock, desk.View.ScreenOpenUntil));
        }

        private void AdvanceSabotageV2()
        {
            if (officeStatus.InternetDown && clock >= officeStatus.InternetBackAt) RestoreInternetV2();
            if (officeStatus.PowerDown && clock >= officeStatus.PowerBackAt) RestorePowerV2();
            foreach (DeskState desk in desks)
                if (desk.View.ScreenOpen && clock >= desk.View.ScreenOpenUntil) { CloseScreenV2(desk); Changed(); }
        }

        private void RestoreInternetV2(int player = PlayerIds.None)
        {
            if (!officeStatus.InternetDown) return;
            officeStatus.InternetDown = false; officeStatus.InternetBackAt = 0;
            if (printQueue.Count > 0) printEnds = clock + pausedPrintRemaining;
            if (Present(player)) ReverseEffect(player, internetIncident); else EndEffect(internetIncident);
            Emit(EventType.InternetRestored, player);
        }

        private void RestorePowerV2(int player = PlayerIds.None)
        {
            if (!officeStatus.PowerDown) return;
            officeStatus.PowerDown = false; officeStatus.PowerBackAt = 0;
            if (Present(player)) ReverseEffect(player, powerIncident); else EndEffect(powerIncident);
            Emit(EventType.PowerRestored, player);
        }

        private void EndOutagesV2() { RestoreInternetV2(); RestorePowerV2(); }

        private float WitnessRangeV2(int actor)
        {
            float range = Config.Witness.Range;
            if (players[actor].Avatar.Crouched) range *= Config.Witness.CrouchRangeFactor;
            return officeStatus.PowerDown ? Math.Min(range, Config.Witness.DarkRange) : range;
        }

        private float ManagerRangeV2()
        {
            return officeStatus.PowerDown ? Math.Min(Config.Manager.VisionRange, Config.Manager.DarkVisionRange) : Config.Manager.VisionRange;
        }

        private void FillSabotageV2View(PlayerView view)
        {
            view.Office = Snapshot.Copy(officeStatus);
            foreach (EmailView email in inbox) view.Inbox.Add(Snapshot.Copy(email));
            foreach (ItemView item in view.Items)
            {
                SaltedMugState salt;
                item.KnownSalted = saltedMugs.TryGetValue(item.Id, out salt) && salt.Actor == view.Recipient;
            }
        }

        private void FillSabotageV2Debug(HostDebugView view)
        {
            foreach (int p in playerIds) view.Cards.Add(new List<SabotageType>(players[p].Private.Cards));
            foreach (ItemState item in items) if (saltedMugs.ContainsKey(item.View.Id)) view.SaltedItems.Add(item.View.Id);
        }

        private void FinalizeEmailConsequencesV2(int fired)
        {
            foreach (IncidentState incident in incidents)
                if (incident.Reveal.Type == SabotageType.FakeEmail && incident.Reveal.Victim == fired) Consequence(incident);
        }

        private void SabotageBeatV2(string key, int actor, int other, string variant, int desk)
        {
            sabotageRecap.Add(new RecapBeat { Time = clock, Key = key, Actor = actor, Other = other, ItemVariant = variant, DeskIndex = desk });
        }
    }
}

