using System;
#nullable disable
using System.Collections.Generic;
using System.Linq;
using BirimizKovulacak.Core;

namespace BalanceSim;

/// <summary>
/// Headless adaptation of BotBrain. Decisions consume only this player's BuildView;
/// the engine validates every action. Navigation is straight-line kinematics.
/// </summary>
public sealed class SimBot
{
    sealed class Plan
    {
        public string Name = "";
        public string TargetId = "";
        public ActionVerb Verb;
        public int Param = -1;
        public Vec3 Goal;
        public double Expires;
        public bool Sent;
        public double SentAt;
    }

    readonly MatchEngine _engine;
    readonly OfficeLayout _layout;
    readonly MatchConfig _config;
    readonly int _slot;
    readonly Random _rng;
    readonly float _aggression;
    readonly Dictionary<string, double> _failedUntil = new();
    PlayerView _view;
    Plan _plan;
    Vec3 _position;
    float _yaw;
    double _now, _nextView, _nextDecision, _meetingActAt, _lastDrawerSearch = -100;
    MatchPhase _phase = MatchPhase.None;
    bool _accused, _voted, _presented;
    string _intent;
    double _intentUntil;
    uint _requestId;
    public PlayerView LastView => _view;
    public float Aggression => _aggression;
    public long AcceptedActions { get; private set; }
    public long RejectedActions { get; private set; }

    public SimBot(MatchEngine engine, OfficeLayout layout, MatchConfig config, int playerId, int seed)
    {
        _engine = engine; _layout = layout; _config = config; _slot = playerId;
        _rng = new Random(seed); _aggression = 0.2f + 0.6f * Rand();
        _view = engine.BuildView(playerId, 0);
        _position = layout.GetDesk(Me(_view).DeskIndex).SeatPosition;
        _matchStart = _view.PhaseStartTime;
    }

    public void Tick(double now, float dt)
    {
        _now = now;
        // Decision/network snapshots are 2.5 Hz; authoritative movement/time stay 10 Hz.
        if (now >= _nextView || _view.Phase != _engine.Phase ||
            (_view.Me.ActiveInteraction != null && now >= _view.Me.ActiveInteraction.EndTime))
        {
            _view = _engine.BuildView(_slot, now);
            _nextView = now + 0.4;
        }
        var v = _view;
        if (v.Phase != _phase)
        {
            _phase = v.Phase; _plan = null;
            _meetingActAt = now + 1 + Rand() * 3;
        }
        if (v.Phase >= MatchPhase.MeetingDiscussion)
        {
            MoveSample(0, false); MeetingTick(v); return;
        }
        if (v.Phase < MatchPhase.Work1 || v.Phase > MatchPhase.FinalInspection)
        { MoveSample(0, false); return; }

        var active = v.Me.ActiveInteraction;
        if (active != null && active.Verb != ActionVerb.None && now < active.EndTime)
        {
            if (TryTargetPosition(v, active.TargetId, out var target)) _yaw = Vec3.YawTo(_position, target);
            MoveSample(0, false); return;
        }

        InformantTick(v); SocialTick(v);
        if (_plan != null && (now > _plan.Expires || (_plan.Sent && now - _plan.SentAt > 1.2))) _plan = null;
        if (_plan == null && now >= _nextDecision)
        {
            _plan = Decide(v, Me(v)); _nextDecision = now + 0.4;
            if (_plan != null && (_plan.Name == "get-suspicious" || _plan.Name == "get-clutter" || _plan.Name == "take-nameplate" || _plan.Name == "steal-report"))
            { _intent = _plan.Name; _intentUntil = now + 40; }
        }
        if (_plan == null) { MoveSample(0, false); return; }
        float distance = Vec3.FlatDistance(_position, _plan.Goal);
        if (!_plan.Sent && distance > 0.55f)
        {
            bool sneak = _intent != null && now < _intentUntil && distance < 6;
            float speed = sneak ? 1.65f : 3f;
            var direction = (_plan.Goal - _position) * (1f / distance);
            _yaw = Vec3.YawTo(_position, _plan.Goal);
            _position += direction * Math.Min(speed * dt, distance);
            _position.Y = 0;
            MoveSample(speed, sneak); return;
        }
        if (TryTargetPosition(v, _plan.TargetId, out var facing)) _yaw = Vec3.YawTo(_position, facing);
        MoveSample(0, false);
        if (!_plan.Sent)
        {
            if (_plan.Verb == ActionVerb.None) { _plan = null; return; }
            var result = Send(_plan.Verb, _plan.TargetId, _plan.Param);
            if (!result.Accepted)
            { _failedUntil[_plan.TargetId + ":" + _plan.Verb] = now + 6; _plan = null; }
            else { _plan.Sent = true; _plan.SentAt = now; }
        }
    }

    void MoveSample(float speed, bool crouched) => _engine.UpdateAvatar(_slot,
        new AvatarSample { Position = _position, Yaw = _yaw, Speed = speed, Crouched = crouched });
    ActionResult Send(ActionVerb verb, string target = "", int param = -1)
    {
        var result = _engine.Submit(_slot, new ActionRequest(_engine.MatchId, ++_requestId, verb, target, param), _now);
        if (result.Accepted) AcceptedActions++; else RejectedActions++;
        _nextView = 0;
        return result;
    }
    float Rand() => (float)_rng.NextDouble();
    static Vec3 V(Vec3 value) => value;
    static float SquareDistance(Vec3 a, Vec3 b) { float d = Vec3.FlatDistance(a, b); return d * d; }
    PlayerPublicView Me(PlayerView v) => v.Players.First(p => p.Id == _slot);
    ItemView Item(PlayerView v, string id) => v.Items.FirstOrDefault(i => i.Id == id);
    DeskView Desk(PlayerView v, int index) => v.Desks.FirstOrDefault(d => d.Index == index);
    static bool RaidActive(PlayerView v) => (v.Phase == MatchPhase.Raid || v.Phase == MatchPhase.FinalInspection)
        && v.Manager.State != ManagerState.Returning && v.Manager.State != ManagerState.InOffice;

    bool TryTargetPosition(PlayerView v, string id, out Vec3 point)
    {
        point = _position;
        if (string.IsNullOrEmpty(id)) return false;
        var item = Item(v, id);
        if (item != null)
        {
            if (item.Location == ItemLocationKind.Floor) { point = item.FloorPosition; return true; }
            if (item.Location == ItemLocationKind.Carried) return item.CarrierId == _slot;
            id = item.SlotId;
        }
        var slot = _layout.GetSlot(id);
        if (slot != null) { point = slot.Position; return true; }
        var station = _layout.GetStation(id);
        if (station != null) { point = station.Position; return true; }
        int deskIndex = EntityIds.DeskIndexOf(id);
        if (deskIndex >= 0) { point = _layout.GetDesk(deskIndex).Position; return true; }
        return false;
    }
    Vec3 StandPoint(string target)
    {
        if (!TryTargetPosition(_view, target, out var p)) return _position;
        p.Y = 0;
        // Stand next to the target, facing it, retaining the Unity bot's interaction margin.
        int desk = EntityIds.DeskIndexOf(target);
        if (desk >= 0)
        {
            var d = _layout.GetDesk(desk);
            if (target.EndsWith(".nameplate", StringComparison.Ordinal)) return d.Position + new Vec3(-0.6f, 0, 0.95f);
            var seat = d.SeatPosition;
            return seat + (p - seat) * 0.25f + (target.EndsWith(".drawer", StringComparison.Ordinal) ? new Vec3(0.45f, 0, 0) : Vec3.Zero);
        }
        float distance = Vec3.FlatDistance(p, _position);
        return distance < 0.01f ? p + new Vec3(0, 0, -0.85f) : p + (_position - p) * (0.85f / distance);
    }
    Plan Go(string name, string target, ActionVerb verb, int param = -1, float timeout = 14f)
    {
        if (_failedUntil.TryGetValue(target + ":" + verb, out var until) && _now < until) return null;
        return new Plan { Name = name, TargetId = target, Verb = verb, Param = param, Goal = StandPoint(target), Expires = _now + timeout };
    }
    bool Affordable(PlayerView v, string target, ActionVerb verb, out Affordance found)
    {
        found = Affordances.For(v, _config, _layout, target, _position).FirstOrDefault(a => a.Verb == verb);
        return found != null && (found.BlockedBy == FailureCode.None || found.BlockedBy == FailureCode.TooFar);
    }
        int ObjectiveTarget(PlayerView v)
        {
            var o = v.Me.Objective;
            return o != null ? o.Target : PlayerIds.None;
        }

        int PickVictim(PlayerView v)
        {
            var o = v.Me.Objective;
            if (o != null && o.Target >= 0 && (o.Type == ObjectiveType.DismissalPlan || o.Type == ObjectiveType.Scapegoat || o.Type == ObjectiveType.PerformanceRival))
                return o.Target;
            var others = v.Players.Where(p => p.Id != _slot && (o == null || o.Type != ObjectiveType.ProtectionDeal && o.Type != ObjectiveType.DeskGuardian || p.Id != o.Target)).ToList();
            return others.Count == 0 ? PlayerIds.None : others[_rng.Next(others.Count)].Id;
        }

        bool WantsSabotage(PlayerView v)
        {
            var o = v.Me.Objective;
            if (o != null && (o.Type == ObjectiveType.CleanHands)) return false;
            float a = _aggression;
            if (o != null && (o.Type == ObjectiveType.Scapegoat || o.Type == ObjectiveType.ChaosCoordinator || o.Type == ObjectiveType.DismissalPlan)) a += 0.25f;
            if (v.Me.Role == Role.Informant) a *= 0.3f;
            return Rand() < a * 0.35f;
        }

        // ---------------------------------------------------------------- office decisions

        Plan Decide(PlayerView v, PlayerPublicView me)
        {
            if (me == null) return null;
            ItemView held = string.IsNullOrEmpty(me.CarryingItemId) ? null : Item(v, me.CarryingItemId);
            int myDesk = me.DeskIndex;

            // Inspection: go sit at the desk and look busy (most of the time).
            if (RaidActive(v) && (held == null || held.Kind != ItemKind.Suspicious) && Rand() < 0.85f)
            {
                var seat = V(_layout.GetDesk(myDesk).SeatPosition);
                if (Vec3.FlatDistance(_position, seat) > 0.9f)
                    return new Plan { Name = "raid-to-desk", Goal = seat, Verb = ActionVerb.None, Expires = _now + 12f };
                if (!me.WorkPose) return new Plan { Name = "work-pose", Goal = _position, Verb = ActionVerb.SetWorkPose, Param = 1, Expires = _now + 2f };
                return null;
            }

            if (held != null) return HandleHeld(v, me, held);

            // Answer the phone if it rings.
            Affordance a;
            if (v.Quota.PhoneRinging && Affordable(v, EntityIds.Phone, ActionVerb.AnswerPhone, out a) && Rand() < 0.7f)
                return Go("answer-phone", EntityIds.Phone, ActionVerb.AnswerPhone);

            // Fix things on my own desk / for others (also useful for Fixer).
            var myDeskView = Desk(v, myDesk);
            var surfaceSus = v.Items.FirstOrDefault(i => i.Location == ItemLocationKind.Slot && i.SlotId == EntityIds.DeskSurface(myDesk) && (i.Kind == ItemKind.Suspicious || i.Kind == ItemKind.Clutter));
            if (surfaceSus != null && !surfaceSus.Sealed) return Go("remove-from-my-desk", surfaceSus.Id, ActionVerb.PickUp);
            var drawerKnown = v.Items.FirstOrDefault(i => i.Location == ItemLocationKind.Slot && i.SlotId == EntityIds.DeskDrawer(myDesk) && i.Kind == ItemKind.Suspicious && !i.Sealed);
            if (drawerKnown != null) return Go("remove-from-my-drawer", drawerKnown.Id, ActionVerb.PickUp);
            if (_now - _lastDrawerSearch > 55f && Rand() < 0.15f)
            {
                _lastDrawerSearch = _now;
                return Go("search-my-drawer", EntityIds.DeskDrawer(myDesk), ActionVerb.SearchContainer);
            }
            var locked = v.Desks.FirstOrDefault(d => d.PcLocked && (d.Index == myDesk || v.Me.Objective.Type == ObjectiveType.Fixer || Rand() < 0.2f));
            if (locked != null) return Go("unlock", EntityIds.DeskComputer(locked.Index), ActionVerb.UnlockWorkstation);

            // v2 repairs: outages hurt everyone, an unplugged pc blocks reports.
            if (v.Office.InternetDown && Rand() < 0.5f) return Go("restart-router", EntityIds.Router, ActionVerb.RestartRouter, -1, 20f);
            if (v.Office.PowerDown && Rand() < 0.5f) return Go("reset-fuse", EntityIds.FuseBox, ActionVerb.ResetFuse, -1, 20f);
            var unplugged = v.Desks.FirstOrDefault(d => d.Unplugged && (d.Index == myDesk || Rand() < 0.25f));
            if (unplugged != null) return Go("plug-in", EntityIds.Desk(unplugged.Index), ActionVerb.PlugIn);
            if (myDeskView != null && myDeskView.ScreenOpen && Rand() < 0.35f && Vec3.FlatDistance(_position, _layout.GetDesk(myDesk).SeatPosition) < 2.5f)
                return Go("lock-screen", EntityIds.DeskComputer(myDesk), ActionVerb.LockScreen);

            var tip = TipPlan(v);
            if (tip != null) return tip;

            // Sabotage, if this bot is in the mood and its cooldowns allow.
            if (WantsSabotage(v))
            {
                var s = SabotagePlan(v, me);
                if (s != null) return s;
            }

            // Work.
            var myReport = v.Items.FirstOrDefault(i => i.Kind == ItemKind.Report && i.ReportOwner == _slot && i.Location == ItemLocationKind.Slot && i.SlotId == EntityIds.PrinterTray);
            if (myReport != null) return Go("take-my-report", myReport.Id, ActionVerb.PickUp);
            var anyReport = v.Items.FirstOrDefault(i => i.Kind == ItemKind.Report && i.Location == ItemLocationKind.Slot && i.SlotId == EntityIds.PrinterTray);
            var printer = v.Stations.FirstOrDefault(s => s.Kind == StationKind.Printer);
            bool myJobQueued = printer != null && printer.PrintQueue.Contains(_slot);
            var options = new List<(float w, Func<Plan> make)>();
            if (v.Quota.ReportOrders > 0 && !myJobQueued && myDeskView != null && !myDeskView.PcLocked)
                options.Add((1.2f, () => Go("write-report", EntityIds.DeskComputer(myDesk), ActionVerb.WriteReport)));
            if (v.Quota.ReportOrders > 0 && anyReport != null)
                options.Add((0.6f, () => Go("take-report", anyReport.Id, ActionVerb.PickUp)));
            if (v.Quota.CoffeeOrders > 0)
            {
                var mug = v.Items.Where(i => i.Kind == ItemKind.Mug && i.Location != ItemLocationKind.Carried && i.Fill != MugFill.Full)
                    .OrderBy(i => (TryTargetPosition(v, i.Id, out var p) ? SquareDistance(p, _position) : 999f)).FirstOrDefault();
                if (mug != null) options.Add((1.0f, () => Go("take-mug", mug.Id, ActionVerb.PickUp)));
            }
            if (v.Quota.DirtyDesks > 0 || v.Desks.Any(d => d.Spill))
            {
                var spray = v.Items.FirstOrDefault(i => i.Kind == ItemKind.Spray && i.Location != ItemLocationKind.Carried);
                if (spray != null) options.Add((0.9f, () => Go("take-spray", spray.Id, ActionVerb.PickUp)));
            }
            var mail = v.Items.FirstOrDefault(i => i.Kind == ItemKind.Mail && i.Location == ItemLocationKind.Slot && i.SlotId == EntityIds.Mailroom);
            if (mail != null) options.Add((0.8f, () => Go("take-mail", mail.Id, ActionVerb.PickUp)));
            if (options.Count == 0)
            {
                if (Rand() < 0.3f) return Go("idle-desk", EntityIds.Desk(myDesk), ActionVerb.None, -1, 4f);
                return null;
            }
            float total = options.Sum(o => o.w), roll = Rand() * total;
            foreach (var o in options) { roll -= o.w; if (roll <= 0) return o.make(); }
            return options[0].make();
        }

        Plan HandleHeld(PlayerView v, PlayerPublicView me, ItemView held)
        {
            int myDesk = me.DeskIndex;
            bool intent = _intent != null && _now < _intentUntil;
            switch (held.Kind)
            {
                case ItemKind.Mug:
                    if (held.Fill == MugFill.Full)
                    {
                        if (!held.KnownSalted && Has(v, SabotageType.SaltCoffee) && CardReady(v, SabotageType.SaltCoffee) && WantsSabotage(v) && Rand() < 0.6f)
                            return Go("salt", EntityIds.CoffeeMachine, ActionVerb.SaltCoffee);
                        if (held.KnownSalted && v.Quota.CoffeeOrders > 0)
                            return Go("deliver-salty", EntityIds.DeliveryTray, ActionVerb.Place);
                        if (Rand() < 0.12f)
                            return new Plan { Name = "taste", Verb = ActionVerb.TasteCoffee, Goal = _position, Expires = _now + 3f };
                        if (WantsSabotage(v) && Rand() < 0.5f)
                        {
                            int victim = PickVictim(v);
                            var vd = v.Desks.FirstOrDefault(d => d.AssignedOwner == victim);
                            if (vd != null && !vd.Spill) return Go("spill", EntityIds.Desk(vd.Index), ActionVerb.SpillCoffee);
                        }
                        if (v.Quota.CoffeeOrders > 0) return Go("deliver-coffee", EntityIds.DeliveryTray, ActionVerb.Place);
                        return Go("park-mug", EntityIds.MugShelf, ActionVerb.Place);
                    }
                    if (v.Quota.CoffeeOrders > 0) return Go("brew", EntityIds.CoffeeMachine, ActionVerb.BrewCoffee, -1, 25f);
                    return Go("return-mug", EntityIds.MugShelf, ActionVerb.Place);
                case ItemKind.Report:
                    if (held.ReportOwner != _slot && ((intent && _intent == "steal-report") || (WantsSabotage(v) && Rand() < 0.6f)))
                    {
                        string[] spots = { EntityIds.HideBehindPrinter, EntityIds.HideInPlant, EntityIds.HideInCabinet, EntityIds.HideInToilet };
                        return Go("hide-report", spots[_rng.Next(spots.Length)], ActionVerb.Place);
                    }
                    if (v.Quota.ReportOrders > 0) return Go("deliver-report", EntityIds.DeliveryTray, ActionVerb.Place);
                    return Go("park-report", EntityIds.DeskSurface(myDesk), ActionVerb.Place);
                case ItemKind.Mail:
                    if (held.AddressedDesk >= 0) return Go("deliver-mail", EntityIds.DeskSurface(held.AddressedDesk), ActionVerb.Place);
                    return new Plan { Name = "drop", Verb = ActionVerb.Drop, Goal = _position, Expires = _now + 2f };
                case ItemKind.Spray:
                {
                    var spill = v.Desks.FirstOrDefault(d => d.Spill);
                    if (spill != null) return Go("clean-spill", EntityIds.Desk(spill.Index), ActionVerb.CleanSpill);
                    var dirty = v.Desks.Where(d => d.Dirty).OrderBy(d => d.Index == myDesk ? 0 : 1).FirstOrDefault();
                    if (dirty != null) return Go("clean-desk", EntityIds.Desk(dirty.Index), ActionVerb.CleanDesk);
                    return Go("return-spray", EntityIds.SprayRack, ActionVerb.Place);
                }
                case ItemKind.Suspicious:
                {
                    // Taken from my own desk -> throw away; otherwise plant at the victim's desk.
                    int victim = PickVictim(v);
                    var vd = v.Desks.FirstOrDefault(d => d.AssignedOwner == victim);
                    bool plant = vd != null && ((intent && _intent == "get-suspicious") || (v.Me.Objective.Type != ObjectiveType.CleanHands && v.Me.Role != Role.Informant && Rand() < 0.5f));
                    if (plant)
                    {
                        bool drawer = Rand() < 0.7f;
                        // Drawers are only opened at raids when tipped: plant, then call the tip line.
                        if (drawer && Rand() < 0.6f) _tipDesk = vd.Index;
                        return Go("plant", drawer ? EntityIds.DeskDrawer(vd.Index) : EntityIds.DeskSurface(vd.Index), ActionVerb.Place);
                    }
                    return Go("dispose", EntityIds.LostAndFound, ActionVerb.Place);
                }
                case ItemKind.Clutter:
                {
                    int victim = PickVictim(v);
                    var vd = v.Desks.FirstOrDefault(d => d.AssignedOwner == victim);
                    if (vd != null && ((intent && _intent == "get-clutter") || (v.Me.Objective.Type != ObjectiveType.CleanHands && v.Me.Role != Role.Informant && Rand() < 0.3f)))
                        return Go("clutter", EntityIds.DeskSurface(vd.Index), ActionVerb.Place);
                    return Go("bin-clutter", EntityIds.JunkBin, ActionVerb.Place);
                }
                case ItemKind.Nameplate:
                {
                    var ownDesk = v.Desks.FirstOrDefault(d => d.AssignedOwner == held.NameplateOf);
                    // An empty holder somewhere else: fill it (second half of a swap leaves both desks mislabelled).
                    var emptyOther = v.Desks.FirstOrDefault(d => d.DisplayedNameplateOf == PlayerIds.None && (ownDesk == null || d.Index != ownDesk.Index));
                    if (emptyOther != null) { _intent = null; return Go("fill-nameplate", EntityIds.DeskNameplate(emptyOther.Index), ActionVerb.Place); }
                    if (intent && _intent == "take-nameplate")
                    {
                        _intent = null;
                        var other = v.Desks.Where(d => d.DisplayedNameplateOf != PlayerIds.None && (ownDesk == null || d.Index != ownDesk.Index)).OrderBy(_ => _rng.Next()).FirstOrDefault();
                        if (other != null) return Go("swap-nameplate", EntityIds.DeskNameplate(other.Index), ActionVerb.Place);
                    }
                    if (ownDesk != null) return Go("restore-nameplate", EntityIds.DeskNameplate(ownDesk.Index), ActionVerb.Place);
                    return new Plan { Name = "drop", Verb = ActionVerb.Drop, Goal = _position, Expires = _now + 2f };
                }
            }
            return new Plan { Name = "drop", Verb = ActionVerb.Drop, Goal = _position, Expires = _now + 2f };
        }

        Plan SabotagePlan(PlayerView v, PlayerPublicView me)
        {
            int victim = PickVictim(v);
            if (victim < 0) return null;
            var vd = v.Desks.FirstOrDefault(d => d.AssignedOwner == victim);
            if (vd == null) return null;
            var cds = v.Me.SabotageCooldowns;
            Func<SabotageType, bool> ready = t => cds == null || (int)t >= cds.Count || cds[(int)t] <= _now;
            var choices = new List<Func<Plan>>();
            var sus = v.Items.Where(i => i.Kind == ItemKind.Suspicious && i.Location == ItemLocationKind.Slot && i.SlotId == EntityIds.LostAndFound).ToList();
            if (sus.Count > 0 && ready(SabotageType.PlantItem)) choices.Add(() => Go("get-suspicious", sus[_rng.Next(sus.Count)].Id, ActionVerb.PickUp));
            var clutter = v.Items.Where(i => i.Kind == ItemKind.Clutter && i.Location == ItemLocationKind.Slot && i.SlotId == EntityIds.JunkBin).ToList();
            if (clutter.Count > 0 && ready(SabotageType.MoveClutter)) choices.Add(() => Go("get-clutter", clutter[_rng.Next(clutter.Count)].Id, ActionVerb.PickUp));
            // Card sabotage (only the cards this bot holds).
            if (!vd.PcLocked && !vd.Unplugged && Has(v, SabotageType.LockWorkstation) && ready(SabotageType.LockWorkstation)) choices.Add(() => Go("lock", EntityIds.DeskComputer(vd.Index), ActionVerb.LockWorkstation));
            if (vd.ScreenOpen && !vd.Unplugged && !v.Office.InternetDown && Has(v, SabotageType.FakeEmail) && ready(SabotageType.FakeEmail))
                choices.Add(() => Go("fake-email", EntityIds.DeskComputer(vd.Index), ActionVerb.SendFakeEmail, _rng.Next(Math.Max(1, _config.Email.TemplateCount))));
            if (!vd.Unplugged && Has(v, SabotageType.PullPlug) && ready(SabotageType.PullPlug)) choices.Add(() => Go("pull-plug", EntityIds.Desk(vd.Index), ActionVerb.PullPlug));
            if (!v.Office.InternetDown && Has(v, SabotageType.InternetOutage) && ready(SabotageType.InternetOutage) && Rand() < 0.4f) choices.Add(() => Go("cut-internet", EntityIds.Router, ActionVerb.CutInternet, -1, 20f));
            if (!v.Office.PowerDown && Has(v, SabotageType.PowerCut) && ready(SabotageType.PowerCut) && Rand() < 0.4f) choices.Add(() => Go("cut-power", EntityIds.FuseBox, ActionVerb.CutPower, -1, 20f));
            var plate = v.Items.FirstOrDefault(i => i.Kind == ItemKind.Nameplate && i.SlotId == EntityIds.DeskNameplate(vd.Index));
            if (plate != null && ready(SabotageType.NameplateSwap)) choices.Add(() => Go("take-nameplate", plate.Id, ActionVerb.PickUp));
            var report = v.Items.FirstOrDefault(i => i.Kind == ItemKind.Report && i.ReportOwner != _slot && i.SlotId == EntityIds.PrinterTray);
            if (report != null && ready(SabotageType.HideReport)) choices.Add(() => Go("steal-report", report.Id, ActionVerb.PickUp));
            if (choices.Count == 0) return null;
            return choices[_rng.Next(choices.Count)]();
        }

        double _nextInformantAct;
        void InformantTick(PlayerView v)
        {
            if (v.Me.Role != Role.Informant || v.Me.Informant == null || _now < _nextInformantAct) return;
            _nextInformantAct = _now + 3f;
            var inf = v.Me.Informant;
            if (inf.CallLogsLeft > 0 && v.Phase == MatchPhase.Work2 && Rand() < 0.3f) { Send(ActionVerb.PullCallLogs); return; }
            var scannedIds = new HashSet<int>(inf.Scans.Select(s => s.EvidenceId));
            var target = v.Evidence.FirstOrDefault(e => (e.Kind == EvidenceKind.SuspiciousItemFound || e.Kind == EvidenceKind.HiddenReportFound) && !scannedIds.Contains(e.Id));
            if (target != null && inf.ScansLeft > 0) { Send(ActionVerb.AuditScanEvidence, "", target.Id); return; }
            if (inf.SealsLeft > 0)
            {
                var low = inf.Scans.FirstOrDefault(s => s.EvidenceId >= 0 && s.Reliability == Reliability.High);
                var sealTarget = low != null ? v.Evidence.FirstOrDefault(e => e.Id == low.EvidenceId && e.Status == EvidenceStatus.Active && !e.Sealed) : null;
                if (sealTarget == null && v.Phase == MatchPhase.FinalInspection)
                    sealTarget = v.Evidence.FirstOrDefault(e => e.Kind == EvidenceKind.SuspiciousItemFound && e.Status == EvidenceStatus.Active && !e.Sealed);
                if (sealTarget != null) Send(ActionVerb.SealEvidence, "", sealTarget.Id);
            }
        }

        // ---------------------------------------------------------------- v2: cards, calls, tip line, emotes

        static bool Has(PlayerView v, SabotageType t) { return v.Me.Cards != null && v.Me.Cards.Contains(t); }

        bool CardReady(PlayerView v, SabotageType t)
        {
            var cds = v.Me.SabotageCooldowns;
            return cds == null || (int)t >= cds.Count || cds[(int)t] <= _now;
        }

        double _nextSocial, _hangUpAt, _answerAt = -1f;
        double _matchStart;

        /// <summary>Instant phone/emote verbs (no walking): answer or decline, call colleagues, hang up, emote.</summary>
        void SocialTick(PlayerView v)
        {
            var call = v.Me.Call;
            // Review #5: a hang-up deadline belongs to one call; start it when a call becomes active.
            if (call == null || call.State != CallState.Active) _hangUpAt = 0;
            if (call != null && v.Features.Calls)
            {
                if (call.State == CallState.RingingIn)
                {
                    if (_answerAt < 0) _answerAt = _now + 0.8f + Rand() * 2f;
                    else if (_now >= _answerAt)
                    {
                        _answerAt = -1f;
                        bool answer = Rand() < 0.75f;
                        Send(answer ? ActionVerb.AnswerCall : ActionVerb.DeclineCall);
                    }
                    return;
                }
                if (call.State == CallState.Active)
                {
                    if (_hangUpAt <= 0) _hangUpAt = _now + 5f + Rand() * 10f;
                    if (_now >= _hangUpAt) { _hangUpAt = 0; Send(ActionVerb.HangUp); }
                    return;
                }
            }
            if (_now < _nextSocial) return;
            _nextSocial = _now + 4f + Rand() * 6f;
            if (v.Features.Calls && call != null && call.State == CallState.None && v.Me.CallCooldownUntil <= _now && Rand() < 0.3f)
            {
                var others = v.Players.Where(p => p.Id != _slot && p.Connected).ToList();
                if (others.Count > 0) { Send(ActionVerb.CallPlayer, "", others[_rng.Next(others.Count)].Id); return; }
            }
            if (Rand() < 0.15f) Send(ActionVerb.Emote, "", _rng.Next(5));
        }

        /// <summary>After planting in a drawer, call the tip line on that desk (the signature combo).</summary>
        Plan TipPlan(PlayerView v)
        {
            if (_tipDesk < 0 || !v.Features.TipLine || v.Me.TipsLeft <= 0 || (v.Me.Call != null && v.Me.Call.State != CallState.None)) return null;
            if (v.Phase != MatchPhase.Work1 && v.Phase != MatchPhase.Work2) return null;
            int desk = _tipDesk;
            _tipDesk = -1;
            return new Plan { Name = "tip", Verb = ActionVerb.TipManager, TargetId = EntityIds.Desk(desk), Goal = _position, Expires = _now + 6f };
        }

        int _tipDesk = -1;

        void RequestFootage(PlayerView v)
        {
            var cams = _layout.Cameras;
            if (cams.Count == 0) return;
            // Footage IntParam = seconds since match start, within the office phases (before the dossier freeze).
            var d = _config.Durations;
            int officeSeconds = (int)(d.Briefing + d.Work1 + d.Raid + d.Work2 + d.FinalInspection);
            int second;
            var latest = _matchStart > 0 ? v.Evidence.Where(e => e.Time > _matchStart && e.Kind != EvidenceKind.CctvFootage).OrderByDescending(e => e.Time).FirstOrDefault() : null;
            if (latest != null) second = (int)(latest.Time - _matchStart);
            else second = 5 + _rng.Next(Math.Max(1, officeSeconds - 10));
            Send(ActionVerb.RequestFootage, cams[_rng.Next(cams.Count)].Id, Math.Clamp(second, 0, Math.Max(0, officeSeconds - 1)));
        }

        // ---------------------------------------------------------------- meeting

        void MeetingTick(PlayerView v)
        {
            if (_now < _meetingActAt) return;
            _meetingActAt = _now + 1f + Rand() * 1.5f;
            var o = v.Me.Objective;
            switch (v.Phase)
            {
                case MatchPhase.MeetingDefense1:
                case MatchPhase.MeetingDefense2:
                    if (v.Features.Footage && v.Me.FootageLeft > 0 && (v.Meeting.Speaker == _slot || v.Me.Role == Role.Informant) && Rand() < 0.7f)
                        RequestFootage(v);
                    break;
                case MatchPhase.MeetingDiscussion:
                    if (!_presented && v.Me.Notes.Any(n => !n.Presented && (n.Kind == NoteKind.Witness || n.Kind == NoteKind.ScanResult || n.Kind == NoteKind.CallLog || n.Kind == NoteKind.TasteResult)))
                    {
                        var note = v.Me.Notes.First(n => !n.Presented && (n.Kind == NoteKind.Witness || n.Kind == NoteKind.ScanResult || n.Kind == NoteKind.CallLog || n.Kind == NoteKind.TasteResult));
                        _presented = true;
                        Send(ActionVerb.PresentNote, "", note.Id);
                        return;
                    }
                    if (!_accused && !v.Me.HasAccused)
                    {
                        _accused = true;
                        if (o != null && (o.Type == ObjectiveType.QuietStar)) return;
                        int target = PlayerIds.None;
                        if (o != null && o.Type == ObjectiveType.DismissalPlan && o.Target >= 0) target = o.Target;
                        if (target < 0)
                        {
                            var cands = v.Players.Where(p => p.Id != _slot && !(o != null && o.Type == ObjectiveType.ProtectionDeal && p.Id == o.Target))
                                .OrderByDescending(p => p.SuspicionLevel).ThenBy(_ => _rng.Next()).ToList();
                            if (cands.Count > 0) target = cands[0].Id;
                        }
                        if (target >= 0) Send(ActionVerb.Accuse, "", target);
                    }
                    break;
                case MatchPhase.MeetingVote:
                    if (_voted || v.Me.HasVoted) return;
                    _voted = true;
                    var shortlist = v.Meeting.Shortlist.Where(id => id != _slot).ToList();
                    if (shortlist.Count == 0) { Send(ActionVerb.CastBallot, "", PlayerIds.None); return; }
                    int pick = shortlist[0];
                    if (o != null && o.Type == ObjectiveType.DismissalPlan && shortlist.Contains(o.Target)) pick = o.Target;
                    else if (o != null && o.Type == ObjectiveType.ProtectionDeal && shortlist.Count > 1 && shortlist.Contains(o.Target)) pick = shortlist.First(id => id != o.Target);
                    else pick = shortlist.OrderByDescending(id => v.Players.First(p => p.Id == id).SuspicionLevel).ThenBy(_ => _rng.Next()).First();
                    Send(ActionVerb.CastBallot, "", pick);
                    break;
            }
        }
}

