using System;
using System.Collections.Generic;
using System.Linq;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.AI;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Automated test player. Uses only its own filtered PlayerView and sends ordinary requests through the same
    /// channel a human uses, so bot matches exercise the real network/rules path. Not an AI opponent for
    /// players — a verification tool (and a way to fill empty seats while testing).
    /// </summary>
    public sealed class BotBrain
    {
        sealed class Plan
        {
            public string Name;
            public string TargetId;
            public ActionVerb Verb;
            public int Param = -1;
            public Vector3 Goal;
            public float Expires;
            public bool Sent;
            public float SentAt;
        }

        readonly IPlayerChannel _ch;
        readonly Func<PlayerAvatar> _avatar;
        readonly System.Random _rng;
        readonly float _aggression;
        Plan _plan;
        readonly NavMeshPath _path = new NavMeshPath();
        Vector3[] _corners = new Vector3[0];
        int _corner;
        float _repathAt, _nextDecision, _stuckSince, _unstickUntil;
        Vector3 _unstickDir;
        Vector3 _lastPos;
        int _matchSeen = -1;
        float _phaseEnteredAt;
        MatchPhase _phase;
        int _matchesFinished;
        bool _accused, _voted, _presented, _rematched;
        float _meetingActAt;
        float _lastDrawerSearch = -100f;
        readonly Dictionary<string, float> _failedUntil = new Dictionary<string, float>();
        // scenario state
        int _scenarioStep;
        // sabotage intent: set when an item was picked up *for* sabotage, so the bot follows through
        string _intent;
        float _intentUntil;

        public string Status { get; private set; } = "";

        public BotBrain(IPlayerChannel channel, Func<PlayerAvatar> avatar, int seed, float aggression)
        {
            _ch = channel;
            _avatar = avatar;
            _rng = new System.Random(seed);
            _aggression = aggression >= 0 ? aggression : (float)(0.25 + _rng.NextDouble() * 0.5);
        }

        float Rand() { return (float)_rng.NextDouble(); }
        static Vector3 V(Vec3 v) { return new Vector3(v.X, v.Y, v.Z); }

        public void Tick(float dt)
        {
            var v = _ch.View;
            var avatar = _avatar();
            if (v == null || avatar == null) return;
            if (v.MatchId != _matchSeen)
            {
                _matchSeen = v.MatchId;
                _plan = null;
                _accused = _voted = _presented = _rematched = false;
                _scenarioStep = 0;
                _matchStart = 0;
                _tipDesk = -1;
                _hangUpAt = 0;
                _answerAt = -1f;
            }
            if (v.Phase != _phase)
            {
                _phase = v.Phase;
                _phaseEnteredAt = Time.time;
                _plan = null;
                _meetingActAt = Time.time + 1f + Rand() * 3f;
                if (v.Phase == MatchPhase.Results) _matchesFinished++;
            }

            if (v.Phase >= MatchPhase.MeetingDiscussion)
            {
                avatar.Drive(Vector3.zero, false, dt);
                MeetingTick(v);
                return;
            }
            if (!LocalPlayerController.OfficePhase(v.Phase))
            {
                avatar.Drive(Vector3.zero, false, dt);
                return;
            }

            var me = Me(v);
            var active = v.Me.ActiveInteraction;
            if (active != null && active.Verb != ActionVerb.None)
            {
                avatar.Drive(Vector3.zero, false, dt);
                Vector3 tp;
                if (OfficeWorld.I != null && OfficeWorld.I.TryTargetPosition(active.TargetId, out tp)) avatar.FaceTowards(tp, dt);
                if (_plan != null) _plan.Sent = true;
                return;
            }

            InformantTick(v);
            SocialTick(v);

            if (_plan != null && (Time.time > _plan.Expires || (_plan.Sent && Time.time - _plan.SentAt > 1.2f)))
            {
                if (_plan.Sent && Time.time - _plan.SentAt > 1.2f && _plan.Verb != ActionVerb.None) Remember(_plan.TargetId + ":" + _plan.Verb, 6f);
                _plan = null;
            }
            if (_plan == null && Time.time >= _nextDecision)
            {
                _plan = Decide(v, me, avatar);
                _nextDecision = Time.time + 0.4f;
                if (_plan != null)
                {
                    Status = _plan.Name;
                    Repath(avatar, _plan.Goal);
                    if (_plan.Name == "get-suspicious" || _plan.Name == "get-clutter" || _plan.Name == "take-nameplate" || _plan.Name == "steal-report")
                    {
                        _intent = _plan.Name;
                        _intentUntil = Time.time + 40f;
                    }
                    MatchLog.Write("bot" + _ch.Slot + " plan " + _plan.Name + " " + _plan.Verb + " " + _plan.TargetId);
                }
            }
            if (_plan == null) { avatar.Drive(Vector3.zero, false, dt); return; }

            // Walk to the goal, then act.
            Vector3 pos = avatar.transform.position;
            Vector3 flat = _plan.Goal - pos; flat.y = 0;
            bool raid = RaidActive(v);
            if (flat.magnitude > 0.55f && !(_plan.Verb == ActionVerb.None && flat.magnitude < 0.9f))
            {
                if (Time.time > _repathAt) Repath(avatar, _plan.Goal);
                Vector3 next = _corner < _corners.Length ? _corners[_corner] : _plan.Goal;
                Vector3 d = next - pos; d.y = 0;
                if (d.magnitude < 0.35f && _corner < _corners.Length) _corner++;
                avatar.Drive(Time.time < _unstickUntil ? _unstickDir : d.normalized, raid && Rand() < 0.02f, dt);
                if ((pos - _lastPos).magnitude < 0.01f)
                {
                    if (Time.time - _stuckSince > 1.2f && Time.time > _unstickUntil)
                    {
                        // Side-step away from whatever blocks us, then re-plan.
                        _unstickDir = Quaternion.Euler(0, Rand() < 0.5f ? 110f : -110f, 0) * d.normalized;
                        _unstickUntil = Time.time + 0.6f;
                        _repathAt = Time.time + 0.6f;
                    }
                    if (Time.time - _stuckSince > 6f) { _plan = null; _stuckSince = Time.time; }
                }
                else _stuckSince = Time.time;
                _lastPos = pos;
                return;
            }
            avatar.Drive(Vector3.zero, false, dt);
            Vector3 target;
            if (!string.IsNullOrEmpty(_plan.TargetId) && OfficeWorld.I != null && OfficeWorld.I.TryTargetPosition(_plan.TargetId, out target))
                avatar.FaceTowards(target, dt);
            if (!_plan.Sent)
            {
                if (_plan.Verb == ActionVerb.None) { _plan = null; return; }
                _ch.Send(_plan.Verb, _plan.TargetId, _plan.Param);
                _plan.Sent = true;
                _plan.SentAt = Time.time;
            }
        }

        void Remember(string key, float seconds) { _failedUntil[key] = Time.time + seconds; }
        bool Blocked(string key) { float t; return _failedUntil.TryGetValue(key, out t) && Time.time < t; }

        void Repath(PlayerAvatar avatar, Vector3 goal)
        {
            _repathAt = Time.time + 1.0f;
            NavMeshHit hit;
            Vector3 g = goal;
            if (NavMesh.SamplePosition(goal, out hit, 1.5f, NavMesh.AllAreas)) g = hit.position;
            // Next to furniture the avatar stands inside the NavMesh agent margin: snap the start point too.
            Vector3 start = avatar.transform.position;
            if (NavMesh.SamplePosition(start, out hit, 1.5f, NavMesh.AllAreas)) start = hit.position;
            if (NavMesh.CalculatePath(start, g, NavMesh.AllAreas, _path) && _path.status != NavMeshPathStatus.PathInvalid)
            {
                _corners = _path.corners;
                _corner = _corners.Length > 1 ? 1 : 0;
            }
            else { _corners = new[] { goal }; _corner = 0; }
        }

        // ---------------------------------------------------------------- helpers

        PlayerPublicView Me(PlayerView v) { return v.Players.FirstOrDefault(p => p.Id == _ch.Slot); }
        ItemView Item(PlayerView v, string id) { return v.Items.FirstOrDefault(i => i.Id == id); }
        DeskView Desk(PlayerView v, int index) { return v.Desks.FirstOrDefault(d => d.Index == index); }

        static bool RaidActive(PlayerView v)
        {
            return (v.Phase == MatchPhase.Raid || v.Phase == MatchPhase.FinalInspection) && v.Manager.State != ManagerState.Returning && v.Manager.State != ManagerState.InOffice;
        }

        Vector3 StandPoint(string targetId, PlayerAvatar avatar)
        {
            var layout = GameConfig.Layout;
            Vector3 tp;
            if (OfficeWorld.I == null || !OfficeWorld.I.TryTargetPosition(targetId, out tp)) return avatar.transform.position;
            tp.y = 0;
            // Desk sub-targets: stand on the employee side of the desk.
            int desk = EntityIds.DeskIndexOf(targetId);
            if (desk >= 0)
            {
                var dd = layout.GetDesk(desk);
                Vector3 seat = V(dd.SeatPosition);
                if (targetId.EndsWith(".nameplate")) return V(dd.Position) + new Vector3(-0.6f, 0, 0.95f);
                return Vector3.Lerp(seat, tp, 0.25f) + (targetId.EndsWith(".drawer") ? new Vector3(0.45f, 0, 0) : Vector3.zero);
            }
            Vector3 from = avatar.transform.position; from.y = 0;
            Vector3 dir = (from - tp); dir.y = 0;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.back;
            Vector3 p = tp + dir.normalized * 0.85f;
            NavMeshHit hit;
            if (NavMesh.SamplePosition(p, out hit, 1.2f, NavMesh.AllAreas)) return hit.position;
            return p;
        }

        Plan Go(string name, string target, ActionVerb verb, PlayerAvatar avatar, int param = -1, float timeout = 14f)
        {
            if (verb != ActionVerb.None && Blocked(target + ":" + verb)) return null;
            return new Plan { Name = name, TargetId = target, Verb = verb, Param = param, Goal = StandPoint(target, avatar), Expires = Time.time + timeout };
        }

        bool Affordable(PlayerView v, string target, ActionVerb verb, PlayerAvatar avatar, out Affordance found)
        {
            found = null;
            try
            {
                Vector3 p = avatar.transform.position;
                var list = Affordances.For(v, _ch.Config, GameConfig.Layout, target, new Vec3(p.x, p.y, p.z));
                found = list.FirstOrDefault(a => a.Verb == verb);
            }
            catch (Exception) { }
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
            var others = v.Players.Where(p => p.Id != _ch.Slot && (o == null || o.Type != ObjectiveType.ProtectionDeal && o.Type != ObjectiveType.DeskGuardian || p.Id != o.Target)).ToList();
            return others.Count == 0 ? PlayerIds.None : others[_rng.Next(others.Count)].Id;
        }

        bool WantsSabotage(PlayerView v)
        {
            if (LaunchArgs.Current.Scenario == ScenarioSetup.RubberDuck) return false;
            var o = v.Me.Objective;
            if (o != null && (o.Type == ObjectiveType.CleanHands)) return false;
            float a = _aggression;
            if (o != null && (o.Type == ObjectiveType.Scapegoat || o.Type == ObjectiveType.ChaosCoordinator || o.Type == ObjectiveType.DismissalPlan)) a += 0.25f;
            if (v.Me.Role == Role.Informant) a *= 0.3f;
            return Rand() < a * 0.35f;
        }

        // ---------------------------------------------------------------- office decisions

        Plan Decide(PlayerView v, PlayerPublicView me, PlayerAvatar avatar)
        {
            if (me == null) return null;
            if (LaunchArgs.Current.Scenario == ScenarioSetup.RubberDuck)
            {
                var sp = ScenarioPlan(v, me, avatar);
                if (sp != null) return sp;
                // Scripted actors wait for their cue instead of improvising.
                if ((_ch.Slot == 1 || _ch.Slot == 2) && v.Phase == MatchPhase.Work1 && _scenarioStep < 99) return null;
            }
            ItemView held = string.IsNullOrEmpty(me.CarryingItemId) ? null : Item(v, me.CarryingItemId);
            int myDesk = me.DeskIndex;

            // Inspection: go sit at the desk and look busy (most of the time).
            if (RaidActive(v) && (held == null || held.Kind != ItemKind.Suspicious) && Rand() < 0.85f)
            {
                var seat = V(GameConfig.Layout.GetDesk(myDesk).SeatPosition);
                if ((avatar.transform.position - seat).magnitude > 0.9f)
                    return new Plan { Name = "raid-to-desk", Goal = seat, Verb = ActionVerb.None, Expires = Time.time + 12f };
                if (!me.WorkPose) return new Plan { Name = "work-pose", Goal = avatar.transform.position, Verb = ActionVerb.SetWorkPose, Param = 1, Expires = Time.time + 2f };
                return null;
            }

            if (held != null) return HandleHeld(v, me, held, avatar);

            // Answer the phone if it rings.
            Affordance a;
            if (v.Quota.PhoneRinging && Affordable(v, EntityIds.Phone, ActionVerb.AnswerPhone, avatar, out a) && Rand() < 0.7f)
                return Go("answer-phone", EntityIds.Phone, ActionVerb.AnswerPhone, avatar);

            // Fix things on my own desk / for others (also useful for Fixer).
            var myDeskView = Desk(v, myDesk);
            var surfaceSus = v.Items.FirstOrDefault(i => i.Location == ItemLocationKind.Slot && i.SlotId == EntityIds.DeskSurface(myDesk) && (i.Kind == ItemKind.Suspicious || i.Kind == ItemKind.Clutter));
            if (surfaceSus != null && !surfaceSus.Sealed) return Go("remove-from-my-desk", surfaceSus.Id, ActionVerb.PickUp, avatar);
            var drawerKnown = v.Items.FirstOrDefault(i => i.Location == ItemLocationKind.Slot && i.SlotId == EntityIds.DeskDrawer(myDesk) && i.Kind == ItemKind.Suspicious && !i.Sealed);
            if (drawerKnown != null) return Go("remove-from-my-drawer", drawerKnown.Id, ActionVerb.PickUp, avatar);
            if (Time.time - _lastDrawerSearch > 55f && Rand() < 0.15f && LaunchArgs.Current.Scenario != ScenarioSetup.RubberDuck)
            {
                _lastDrawerSearch = Time.time;
                return Go("search-my-drawer", EntityIds.DeskDrawer(myDesk), ActionVerb.SearchContainer, avatar);
            }
            var locked = v.Desks.FirstOrDefault(d => d.PcLocked && (d.Index == myDesk || v.Me.Objective.Type == ObjectiveType.Fixer || Rand() < 0.2f));
            if (locked != null) return Go("unlock", EntityIds.DeskComputer(locked.Index), ActionVerb.UnlockWorkstation, avatar);

            // v2 repairs: outages hurt everyone, an unplugged pc blocks reports.
            if (v.Office.InternetDown && Rand() < 0.5f) return Go("restart-router", EntityIds.Router, ActionVerb.RestartRouter, avatar, -1, 20f);
            if (v.Office.PowerDown && Rand() < 0.5f) return Go("reset-fuse", EntityIds.FuseBox, ActionVerb.ResetFuse, avatar, -1, 20f);
            var unplugged = v.Desks.FirstOrDefault(d => d.Unplugged && (d.Index == myDesk || Rand() < 0.25f));
            if (unplugged != null) return Go("plug-in", EntityIds.Desk(unplugged.Index), ActionVerb.PlugIn, avatar);
            if (myDeskView != null && myDeskView.ScreenOpen && Rand() < 0.35f && (avatar.transform.position - V(GameConfig.Layout.GetDesk(myDesk).SeatPosition)).magnitude < 2.5f)
                return Go("lock-screen", EntityIds.DeskComputer(myDesk), ActionVerb.LockScreen, avatar);

            var tip = TipPlan(v, avatar);
            if (tip != null) return tip;

            // Sabotage, if this bot is in the mood and its cooldowns allow.
            if (WantsSabotage(v))
            {
                var s = SabotagePlan(v, me, avatar);
                if (s != null) return s;
            }

            // Work.
            var myReport = v.Items.FirstOrDefault(i => i.Kind == ItemKind.Report && i.ReportOwner == _ch.Slot && i.Location == ItemLocationKind.Slot && i.SlotId == EntityIds.PrinterTray);
            if (myReport != null) return Go("take-my-report", myReport.Id, ActionVerb.PickUp, avatar);
            var anyReport = v.Items.FirstOrDefault(i => i.Kind == ItemKind.Report && i.Location == ItemLocationKind.Slot && i.SlotId == EntityIds.PrinterTray);
            var printer = v.Stations.FirstOrDefault(s => s.Kind == StationKind.Printer);
            bool myJobQueued = printer != null && printer.PrintQueue.Contains(_ch.Slot);
            var options = new List<(float w, Func<Plan> make)>();
            if (v.Quota.ReportOrders > 0 && !myJobQueued && myDeskView != null && !myDeskView.PcLocked)
                options.Add((1.2f, () => Go("write-report", EntityIds.DeskComputer(myDesk), ActionVerb.WriteReport, avatar)));
            if (v.Quota.ReportOrders > 0 && anyReport != null)
                options.Add((0.6f, () => Go("take-report", anyReport.Id, ActionVerb.PickUp, avatar)));
            if (v.Quota.CoffeeOrders > 0)
            {
                var mug = v.Items.Where(i => i.Kind == ItemKind.Mug && i.Location != ItemLocationKind.Carried && i.Fill != MugFill.Full)
                    .OrderBy(i => (OfficeWorld.I.TryTargetPosition(i.Id, out var p) ? (p - avatar.transform.position).sqrMagnitude : 999f)).FirstOrDefault();
                if (mug != null) options.Add((1.0f, () => Go("take-mug", mug.Id, ActionVerb.PickUp, avatar)));
            }
            if (v.Quota.DirtyDesks > 0 || v.Desks.Any(d => d.Spill))
            {
                var spray = v.Items.FirstOrDefault(i => i.Kind == ItemKind.Spray && i.Location != ItemLocationKind.Carried);
                if (spray != null) options.Add((0.9f, () => Go("take-spray", spray.Id, ActionVerb.PickUp, avatar)));
            }
            var mail = v.Items.FirstOrDefault(i => i.Kind == ItemKind.Mail && i.Location == ItemLocationKind.Slot && i.SlotId == EntityIds.Mailroom);
            if (mail != null) options.Add((0.8f, () => Go("take-mail", mail.Id, ActionVerb.PickUp, avatar)));
            if (options.Count == 0)
            {
                if (Rand() < 0.3f) return Go("idle-desk", EntityIds.Desk(myDesk), ActionVerb.None, avatar, -1, 4f);
                return null;
            }
            float total = options.Sum(o => o.w), roll = Rand() * total;
            foreach (var o in options) { roll -= o.w; if (roll <= 0) return o.make(); }
            return options[0].make();
        }

        Plan HandleHeld(PlayerView v, PlayerPublicView me, ItemView held, PlayerAvatar avatar)
        {
            int myDesk = me.DeskIndex;
            bool intent = _intent != null && Time.time < _intentUntil;
            switch (held.Kind)
            {
                case ItemKind.Mug:
                    if (held.Fill == MugFill.Full)
                    {
                        if (!held.KnownSalted && Has(v, SabotageType.SaltCoffee) && CardReady(v, SabotageType.SaltCoffee) && WantsSabotage(v) && Rand() < 0.6f)
                            return Go("salt", EntityIds.CoffeeMachine, ActionVerb.SaltCoffee, avatar);
                        if (held.KnownSalted && v.Quota.CoffeeOrders > 0)
                            return Go("deliver-salty", EntityIds.DeliveryTray, ActionVerb.Place, avatar);
                        if (Rand() < 0.12f)
                            return new Plan { Name = "taste", Verb = ActionVerb.TasteCoffee, Goal = avatar.transform.position, Expires = Time.time + 3f };
                        if (WantsSabotage(v) && Rand() < 0.5f)
                        {
                            int victim = PickVictim(v);
                            var vd = v.Desks.FirstOrDefault(d => d.AssignedOwner == victim);
                            if (vd != null && !vd.Spill) return Go("spill", EntityIds.Desk(vd.Index), ActionVerb.SpillCoffee, avatar);
                        }
                        if (v.Quota.CoffeeOrders > 0) return Go("deliver-coffee", EntityIds.DeliveryTray, ActionVerb.Place, avatar);
                        return Go("park-mug", EntityIds.MugShelf, ActionVerb.Place, avatar);
                    }
                    if (v.Quota.CoffeeOrders > 0) return Go("brew", EntityIds.CoffeeMachine, ActionVerb.BrewCoffee, avatar, -1, 25f);
                    return Go("return-mug", EntityIds.MugShelf, ActionVerb.Place, avatar);
                case ItemKind.Report:
                    if (held.ReportOwner != _ch.Slot && ((intent && _intent == "steal-report") || (WantsSabotage(v) && Rand() < 0.6f)))
                    {
                        string[] spots = { EntityIds.HideBehindPrinter, EntityIds.HideInPlant, EntityIds.HideInCabinet, EntityIds.HideInToilet };
                        return Go("hide-report", spots[_rng.Next(spots.Length)], ActionVerb.Place, avatar);
                    }
                    if (v.Quota.ReportOrders > 0) return Go("deliver-report", EntityIds.DeliveryTray, ActionVerb.Place, avatar);
                    return Go("park-report", EntityIds.DeskSurface(myDesk), ActionVerb.Place, avatar);
                case ItemKind.Mail:
                    if (held.AddressedDesk >= 0) return Go("deliver-mail", EntityIds.DeskSurface(held.AddressedDesk), ActionVerb.Place, avatar);
                    return new Plan { Name = "drop", Verb = ActionVerb.Drop, Goal = avatar.transform.position, Expires = Time.time + 2f };
                case ItemKind.Spray:
                {
                    var spill = v.Desks.FirstOrDefault(d => d.Spill);
                    if (spill != null) return Go("clean-spill", EntityIds.Desk(spill.Index), ActionVerb.CleanSpill, avatar);
                    var dirty = v.Desks.Where(d => d.Dirty).OrderBy(d => d.Index == myDesk ? 0 : 1).FirstOrDefault();
                    if (dirty != null) return Go("clean-desk", EntityIds.Desk(dirty.Index), ActionVerb.CleanDesk, avatar);
                    return Go("return-spray", EntityIds.SprayRack, ActionVerb.Place, avatar);
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
                        return Go("plant", drawer ? EntityIds.DeskDrawer(vd.Index) : EntityIds.DeskSurface(vd.Index), ActionVerb.Place, avatar);
                    }
                    return Go("dispose", EntityIds.LostAndFound, ActionVerb.Place, avatar);
                }
                case ItemKind.Clutter:
                {
                    int victim = PickVictim(v);
                    var vd = v.Desks.FirstOrDefault(d => d.AssignedOwner == victim);
                    if (vd != null && ((intent && _intent == "get-clutter") || (v.Me.Objective.Type != ObjectiveType.CleanHands && v.Me.Role != Role.Informant && Rand() < 0.3f)))
                        return Go("clutter", EntityIds.DeskSurface(vd.Index), ActionVerb.Place, avatar);
                    return Go("bin-clutter", EntityIds.JunkBin, ActionVerb.Place, avatar);
                }
                case ItemKind.Nameplate:
                {
                    var ownDesk = v.Desks.FirstOrDefault(d => d.AssignedOwner == held.NameplateOf);
                    // An empty holder somewhere else: fill it (second half of a swap leaves both desks mislabelled).
                    var emptyOther = v.Desks.FirstOrDefault(d => d.DisplayedNameplateOf == PlayerIds.None && (ownDesk == null || d.Index != ownDesk.Index));
                    if (emptyOther != null) { _intent = null; return Go("fill-nameplate", EntityIds.DeskNameplate(emptyOther.Index), ActionVerb.Place, avatar); }
                    if (intent && _intent == "take-nameplate")
                    {
                        _intent = null;
                        var other = v.Desks.Where(d => d.DisplayedNameplateOf != PlayerIds.None && (ownDesk == null || d.Index != ownDesk.Index)).OrderBy(_ => _rng.Next()).FirstOrDefault();
                        if (other != null) return Go("swap-nameplate", EntityIds.DeskNameplate(other.Index), ActionVerb.Place, avatar);
                    }
                    if (ownDesk != null) return Go("restore-nameplate", EntityIds.DeskNameplate(ownDesk.Index), ActionVerb.Place, avatar);
                    return new Plan { Name = "drop", Verb = ActionVerb.Drop, Goal = avatar.transform.position, Expires = Time.time + 2f };
                }
            }
            return new Plan { Name = "drop", Verb = ActionVerb.Drop, Goal = avatar.transform.position, Expires = Time.time + 2f };
        }

        Plan SabotagePlan(PlayerView v, PlayerPublicView me, PlayerAvatar avatar)
        {
            int victim = PickVictim(v);
            if (victim < 0) return null;
            var vd = v.Desks.FirstOrDefault(d => d.AssignedOwner == victim);
            if (vd == null) return null;
            var cds = v.Me.SabotageCooldowns;
            Func<SabotageType, bool> ready = t => cds == null || (int)t >= cds.Count || cds[(int)t] <= _ch.Now;
            var choices = new List<Func<Plan>>();
            var sus = v.Items.Where(i => i.Kind == ItemKind.Suspicious && i.Location == ItemLocationKind.Slot && i.SlotId == EntityIds.LostAndFound).ToList();
            if (sus.Count > 0 && ready(SabotageType.PlantItem)) choices.Add(() => Go("get-suspicious", sus[_rng.Next(sus.Count)].Id, ActionVerb.PickUp, avatar));
            var clutter = v.Items.Where(i => i.Kind == ItemKind.Clutter && i.Location == ItemLocationKind.Slot && i.SlotId == EntityIds.JunkBin).ToList();
            if (clutter.Count > 0 && ready(SabotageType.MoveClutter)) choices.Add(() => Go("get-clutter", clutter[_rng.Next(clutter.Count)].Id, ActionVerb.PickUp, avatar));
            // Card sabotage (only the cards this bot holds).
            if (!vd.PcLocked && !vd.Unplugged && Has(v, SabotageType.LockWorkstation) && ready(SabotageType.LockWorkstation)) choices.Add(() => Go("lock", EntityIds.DeskComputer(vd.Index), ActionVerb.LockWorkstation, avatar));
            if (vd.ScreenOpen && !vd.Unplugged && !v.Office.InternetDown && Has(v, SabotageType.FakeEmail) && ready(SabotageType.FakeEmail))
                choices.Add(() => Go("fake-email", EntityIds.DeskComputer(vd.Index), ActionVerb.SendFakeEmail, avatar, _rng.Next(Mathf.Max(1, _ch.Config.Email.TemplateCount))));
            if (!vd.Unplugged && Has(v, SabotageType.PullPlug) && ready(SabotageType.PullPlug)) choices.Add(() => Go("pull-plug", EntityIds.Desk(vd.Index), ActionVerb.PullPlug, avatar));
            if (!v.Office.InternetDown && Has(v, SabotageType.InternetOutage) && ready(SabotageType.InternetOutage) && Rand() < 0.4f) choices.Add(() => Go("cut-internet", EntityIds.Router, ActionVerb.CutInternet, avatar, -1, 20f));
            if (!v.Office.PowerDown && Has(v, SabotageType.PowerCut) && ready(SabotageType.PowerCut) && Rand() < 0.4f) choices.Add(() => Go("cut-power", EntityIds.FuseBox, ActionVerb.CutPower, avatar, -1, 20f));
            var plate = v.Items.FirstOrDefault(i => i.Kind == ItemKind.Nameplate && i.SlotId == EntityIds.DeskNameplate(vd.Index));
            if (plate != null && ready(SabotageType.NameplateSwap)) choices.Add(() => Go("take-nameplate", plate.Id, ActionVerb.PickUp, avatar));
            var report = v.Items.FirstOrDefault(i => i.Kind == ItemKind.Report && i.ReportOwner != _ch.Slot && i.SlotId == EntityIds.PrinterTray);
            if (report != null && ready(SabotageType.HideReport)) choices.Add(() => Go("steal-report", report.Id, ActionVerb.PickUp, avatar));
            if (choices.Count == 0) return null;
            return choices[_rng.Next(choices.Count)]();
        }

        // ---------------------------------------------------------------- scenario (rubber duck)

        Plan ScenarioPlan(PlayerView v, PlayerPublicView me, PlayerAvatar avatar)
        {
            float t = Time.time - _phaseEnteredAt;
            var duck = v.Items.FirstOrDefault(i => i.Id == EntityIds.Suspicious(ItemVariants.Duck));
            string held = me.CarryingItemId;
            if (_ch.Slot == 2 && v.Phase == MatchPhase.Work1)
            {
                // Pembe: take the duck and plant it in Mavi's drawer.
                if (_scenarioStep == 0 && t > 4f && duck != null && duck.SlotId == EntityIds.LostAndFound && string.IsNullOrEmpty(held))
                    return Go("duck-take", duck.Id, ActionVerb.PickUp, avatar);
                if (held == duck?.Id)
                {
                    _scenarioStep = 1;
                    return Go("duck-plant", EntityIds.DeskDrawer(0), ActionVerb.Place, avatar, -1, 20f);
                }
                if (_scenarioStep >= 1) _scenarioStep = 99;
            }
            if (_ch.Slot == 1 && v.Phase == MatchPhase.Work1)
            {
                // Sarı: once the duck has disappeared from lost & found, swap Mavi's nameplate with Yeşil's.
                bool duckGone = duck == null || duck.SlotId != EntityIds.LostAndFound;
                var mavi = v.Items.FirstOrDefault(i => i.Id == EntityIds.Nameplate(0));
                if (_scenarioStep == 0 && duckGone && t > 30f && string.IsNullOrEmpty(held) && mavi != null && mavi.SlotId == EntityIds.DeskNameplate(0))
                    return Go("np-take", mavi.Id, ActionVerb.PickUp, avatar, -1, 20f);
                if (held == EntityIds.Nameplate(0)) { _scenarioStep = 1; return Go("np-swap", EntityIds.DeskNameplate(3), ActionVerb.Place, avatar, -1, 20f); }
                if (held == EntityIds.Nameplate(3) && _scenarioStep == 1) { _scenarioStep = 2; return Go("np-fill", EntityIds.DeskNameplate(0), ActionVerb.Place, avatar, -1, 20f); }
                if (_scenarioStep == 2 && string.IsNullOrEmpty(held)) _scenarioStep = 99;
                if (_scenarioStep < 99) return null;
            }
            if (_ch.Slot == 2 && v.Phase == MatchPhase.Work1 && _scenarioStep < 99) return null;
            return null;
        }

        // ---------------------------------------------------------------- informant

        float _nextInformantAct;
        void InformantTick(PlayerView v)
        {
            if (v.Me.Role != Role.Informant || v.Me.Informant == null || Time.time < _nextInformantAct) return;
            _nextInformantAct = Time.time + 3f;
            var inf = v.Me.Informant;
            if (inf.CallLogsLeft > 0 && v.Phase == MatchPhase.Work2 && Rand() < 0.3f) { _ch.Send(ActionVerb.PullCallLogs); return; }
            var scannedIds = new HashSet<int>(inf.Scans.Select(s => s.EvidenceId));
            var target = v.Evidence.FirstOrDefault(e => (e.Kind == EvidenceKind.SuspiciousItemFound || e.Kind == EvidenceKind.HiddenReportFound) && !scannedIds.Contains(e.Id));
            if (target != null && inf.ScansLeft > 0) { _ch.Send(ActionVerb.AuditScanEvidence, "", target.Id); return; }
            if (inf.SealsLeft > 0)
            {
                var low = inf.Scans.FirstOrDefault(s => s.EvidenceId >= 0 && s.Reliability == Reliability.High);
                var sealTarget = low != null ? v.Evidence.FirstOrDefault(e => e.Id == low.EvidenceId && e.Status == EvidenceStatus.Active && !e.Sealed) : null;
                if (sealTarget == null && v.Phase == MatchPhase.FinalInspection)
                    sealTarget = v.Evidence.FirstOrDefault(e => e.Kind == EvidenceKind.SuspiciousItemFound && e.Status == EvidenceStatus.Active && !e.Sealed);
                if (sealTarget != null) _ch.Send(ActionVerb.SealEvidence, "", sealTarget.Id);
            }
        }

        // ---------------------------------------------------------------- v2: cards, calls, tip line, emotes

        static bool Has(PlayerView v, SabotageType t) { return v.Me.Cards != null && v.Me.Cards.Contains(t); }

        bool CardReady(PlayerView v, SabotageType t)
        {
            var cds = v.Me.SabotageCooldowns;
            return cds == null || (int)t >= cds.Count || cds[(int)t] <= _ch.Now;
        }

        float _nextSocial, _hangUpAt, _answerAt = -1f;
        double _matchStart;

        /// <summary>Instant phone/emote verbs (no walking): answer or decline, call colleagues, hang up, emote.</summary>
        void SocialTick(PlayerView v)
        {
            if (v.Phase == MatchPhase.Briefing && _matchStart <= 0) _matchStart = v.PhaseStartTime;
            var call = v.Me.Call;
            if (call != null && v.Features.Calls)
            {
                if (call.State == CallState.RingingIn)
                {
                    if (_answerAt < 0) _answerAt = Time.time + 0.8f + Rand() * 2f;
                    else if (Time.time >= _answerAt)
                    {
                        _answerAt = -1f;
                        bool answer = Rand() < 0.75f;
                        _ch.Send(answer ? ActionVerb.AnswerCall : ActionVerb.DeclineCall);
                        if (answer) _hangUpAt = Time.time + 5f + Rand() * 10f;
                    }
                    return;
                }
                if (call.State == CallState.Active)
                {
                    if (_hangUpAt <= 0) _hangUpAt = Time.time + 5f + Rand() * 10f;
                    if (Time.time >= _hangUpAt) { _hangUpAt = 0; _ch.Send(ActionVerb.HangUp); }
                    return;
                }
            }
            if (Time.time < _nextSocial) return;
            _nextSocial = Time.time + 4f + Rand() * 6f;
            if (v.Features.Calls && call != null && call.State == CallState.None && v.Me.CallCooldownUntil <= _ch.Now && Rand() < 0.3f)
            {
                var others = v.Players.Where(p => p.Id != _ch.Slot && p.Connected).ToList();
                if (others.Count > 0) { _ch.Send(ActionVerb.CallPlayer, "", others[_rng.Next(others.Count)].Id); return; }
            }
            if (Rand() < 0.15f) _ch.Send(ActionVerb.Emote, "", _rng.Next(5));
        }

        /// <summary>After planting in a drawer, call the tip line on that desk (the signature combo).</summary>
        Plan TipPlan(PlayerView v, PlayerAvatar avatar)
        {
            if (_tipDesk < 0 || !v.Features.TipLine || v.Me.TipsLeft <= 0 || (v.Me.Call != null && v.Me.Call.State != CallState.None)) return null;
            if (v.Phase != MatchPhase.Work1 && v.Phase != MatchPhase.Work2) return null;
            int desk = _tipDesk;
            _tipDesk = -1;
            return new Plan { Name = "tip", Verb = ActionVerb.TipManager, TargetId = EntityIds.Desk(desk), Goal = avatar.transform.position, Expires = Time.time + 6f };
        }

        int _tipDesk = -1;

        void RequestFootage(PlayerView v)
        {
            var cams = GameConfig.Layout.Cameras;
            if (cams.Count == 0) return;
            var latest = v.Evidence.Where(e => e.Time > _matchStart && e.Kind != EvidenceKind.CctvFootage).OrderByDescending(e => e.Time).FirstOrDefault();
            int second = latest != null ? (int)(latest.Time - _matchStart) : 60 + _rng.Next(240);
            _ch.Send(ActionVerb.RequestFootage, cams[_rng.Next(cams.Count)].Id, Mathf.Max(0, second));
        }

        // ---------------------------------------------------------------- meeting

        void MeetingTick(PlayerView v)
        {
            if (Time.time < _meetingActAt) return;
            _meetingActAt = Time.time + 1f + Rand() * 1.5f;
            var o = v.Me.Objective;
            switch (v.Phase)
            {
                case MatchPhase.MeetingDefense1:
                case MatchPhase.MeetingDefense2:
                    if (v.Features.Footage && v.Me.FootageLeft > 0 && (v.Meeting.Speaker == _ch.Slot || v.Me.Role == Role.Informant) && Rand() < 0.7f)
                        RequestFootage(v);
                    break;
                case MatchPhase.MeetingDiscussion:
                    if (!_presented && v.Me.Notes.Any(n => !n.Presented && (n.Kind == NoteKind.Witness || n.Kind == NoteKind.ScanResult || n.Kind == NoteKind.CallLog || n.Kind == NoteKind.TasteResult)))
                    {
                        var note = v.Me.Notes.First(n => !n.Presented && (n.Kind == NoteKind.Witness || n.Kind == NoteKind.ScanResult || n.Kind == NoteKind.CallLog || n.Kind == NoteKind.TasteResult));
                        _presented = true;
                        _ch.Send(ActionVerb.PresentNote, "", note.Id);
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
                            var cands = v.Players.Where(p => p.Id != _ch.Slot && !(o != null && o.Type == ObjectiveType.ProtectionDeal && p.Id == o.Target))
                                .OrderByDescending(p => p.SuspicionLevel).ThenBy(_ => _rng.Next()).ToList();
                            if (cands.Count > 0) target = cands[0].Id;
                        }
                        if (target >= 0) _ch.Send(ActionVerb.Accuse, "", target);
                    }
                    break;
                case MatchPhase.MeetingVote:
                    if (_voted || v.Me.HasVoted) return;
                    _voted = true;
                    var shortlist = v.Meeting.Shortlist.Where(id => id != _ch.Slot).ToList();
                    if (shortlist.Count == 0) { _ch.Send(ActionVerb.CastBallot, "", PlayerIds.None); return; }
                    int pick = shortlist[0];
                    if (o != null && o.Type == ObjectiveType.DismissalPlan && shortlist.Contains(o.Target)) pick = o.Target;
                    else if (o != null && o.Type == ObjectiveType.ProtectionDeal && shortlist.Count > 1 && shortlist.Contains(o.Target)) pick = shortlist.First(id => id != o.Target);
                    else pick = shortlist.OrderByDescending(id => v.Players.First(p => p.Id == id).SuspicionLevel).ThenBy(_ => _rng.Next()).First();
                    _ch.Send(ActionVerb.CastBallot, "", pick);
                    break;
                case MatchPhase.Results:
                    if (_rematched || v.Me.RematchVoted) return;
                    if (LaunchArgs.Current.QuitAfterMatches > 0 && _matchesFinished >= LaunchArgs.Current.QuitAfterMatches) return;
                    if (_matchesFinished <= LaunchArgs.Current.Rematches)
                    {
                        _rematched = true;
                        _ch.Send(ActionVerb.RematchVote);
                    }
                    break;
            }
        }
    }
}
