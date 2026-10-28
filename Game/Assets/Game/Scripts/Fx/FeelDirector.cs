using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using EventType = BirimizKovulacak.Core.EventType;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Turns rules events and view state into game feel: diegetic sounds, VFX, camera juice and (through
    /// <see cref="IAvatarFeel"/>) character expressions / emotes / crouch poses. Persistent effects are state-driven
    /// (correct after a reconnect): ringtones, ring-back, blackout, sweat, salt sparkle, expressions. One-shots use the
    /// public events or view diffs. Everything here is presentation; nothing is sent to the host.
    ///
    /// Sound ownership split (so nothing plays twice): this class plays every v2 / world sound listed in
    /// docs/FEEL.md; the HUD keeps its v1 UI sounds (complete, evidence, toast, place, vote, phase, deny, pickup,
    /// manager_hm → routed by Sfx to the profile voice).
    /// Cost: a few loops over ≤ 6 players per frame, expressions every 0.2 s, no per-frame allocations.
    /// </summary>
    public sealed class FeelDirector : MonoBehaviour
    {
        public static FeelDirector I { get; private set; }

        static readonly string[] RingKeys = { "ring.0", "ring.1", "ring.2", "ring.3", "ring.4", "ring.5" };
        const string RingbackKey = "ringback";
        const int Max = Protocol.MaxPlayers;

        public float SweatRange = 6f;
        public float NervousRange = 5f;

        readonly double[] _emoteUntil = new double[Max];
        readonly PublicActivity[] _activity = new PublicActivity[Max];
        readonly byte[] _expr = new byte[Max];
        readonly sbyte[] _crouch = new sbyte[Max];
        readonly float[] _shockUntil = new float[Max];
        readonly float[] _happyUntil = new float[Max];
        readonly bool[] _ringing = new bool[Max];
        readonly bool[] _ringSeen = new bool[Max];
        const int MaxDesks = 16;
        readonly sbyte[] _deskSpill = new sbyte[MaxDesks];   // -1 unknown (first view of a match never fires)
        readonly short[] _deskClutter = new short[MaxDesks];

        int _lastMatch = int.MinValue;
        int _lastReaction = -1;
        ActionVerb _myVerb;
        CallState _callState;
        float _ringOutSince = -10f;
        bool _ringbackOn;
        string _saltItem = "";
        Transform _saltTarget;
        float _nextSaltLookup;
        float _resultsAt = -1f;
        float _nextExpr;
        ClientState _subscribed;

        void Awake()
        {
            I = this;
            if (GetComponent<FxPool>() == null) gameObject.AddComponent<FxPool>();
            if (GetComponent<ScreenFx>() == null) gameObject.AddComponent<ScreenFx>();
            ResetMatchState();
        }

        void Start() { Subscribe(); }

        void Subscribe()
        {
            var cs = ClientState.I;
            if (cs == null || cs == _subscribed) return;
            _subscribed = cs;
            cs.EventReceived += OnEvent;
            cs.PhaseChanged += OnPhaseChanged;
        }

        void OnDestroy()
        {
            if (_subscribed == null) return;
            _subscribed.EventReceived -= OnEvent;
            _subscribed.PhaseChanged -= OnPhaseChanged;
        }

        void ResetMatchState()
        {
            for (int i = 0; i < Max; i++)
            {
                _emoteUntil[i] = 0;
                _activity[i] = PublicActivity.None;
                _expr[i] = 255;
                _crouch[i] = -1;
                _shockUntil[i] = 0f;
                _happyUntil[i] = 0f;
            }
            for (int i = 0; i < MaxDesks; i++) { _deskSpill[i] = -1; _deskClutter[i] = -1; }
            _lastReaction = -1;
            _myVerb = ActionVerb.None;
            _callState = CallState.None;
            _resultsAt = -1f;
            StopLoops();
        }

        void StopLoops()
        {
            for (int i = 0; i < Max; i++)
                if (_ringing[i]) { Sfx.StopLoop(RingKeys[i]); _ringing[i] = false; }
            if (_ringbackOn) { Sfx.StopLoop(RingbackKey); _ringbackOn = false; }
        }

        // ------------------------------------------------------------------ helpers

        public void Shock(int slot, float seconds) { if (PlayerIds.IsValid(slot)) _shockUntil[slot] = Time.time + seconds; }
        public void Cheer(int slot, float seconds) { if (PlayerIds.IsValid(slot)) _happyUntil[slot] = Time.time + seconds; }

        static Vector3 V(Vec3 v) { return new Vector3(v.X, v.Y, v.Z); }

        static bool DeskPoint(string deskId, float height, out Vector3 p)
        {
            var d = GameConfig.Layout != null ? GameConfig.Layout.GetDesk(deskId) : null;
            p = d != null ? V(d.Position) + Vector3.up * height : Vector3.zero;
            return d != null;
        }

        static ItemView FindItem(PlayerView v, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < v.Items.Count; i++) if (v.Items[i].Id == id) return v.Items[i];
            return null;
        }

        /// <summary>Is <paramref name="p"/> carrying something the manager would frown at? (salt only known to its salter/taster)</summary>
        static bool CarryingSuspicious(PlayerView v, PlayerPublicView p)
        {
            var it = FindItem(v, p.CarryingItemId);
            if (it == null) return false;
            return it.Kind == ItemKind.Suspicious || (it.Kind == ItemKind.Nameplate && it.NameplateOf != p.Id) || it.KnownSalted;
        }

        static bool ManagerPatrolling(PlayerView v)
        {
            if (v.Phase != MatchPhase.Raid && v.Phase != MatchPhase.Work2 && v.Phase != MatchPhase.FinalInspection) return false;
            var s = v.Manager.State;
            return s == ManagerState.Walking || s == ManagerState.Inspecting || s == ManagerState.Reacting;
        }

        // ------------------------------------------------------------------ events

        void OnEvent(GameEvent e)
        {
            var cs = ClientState.I;
            var view = cs != null ? cs.View : null;
            if (view == null) return;
            var cam = CameraRig.I;
            Vector3 p;
            switch (e.Type)
            {
                case EventType.ManagerWarning:
                    if (cam != null) { cam.Shake(0.2f); cam.FovKick(3f); }
                    break;
                case EventType.ManagerObservation:
                    OnObservation(view, e.IntArg, cs.MySlot);
                    break;
                case EventType.TaskCompleted:
                    Cheer(e.Actor, 1.5f);
                    break;
                case EventType.SurpriseInspection:
                    Sfx.Play("inspection_sting", 0.9f);
                    if (cam != null) { cam.Shake(0.35f); cam.FovKick(5f); }
                    break;
                case EventType.CallStarted:
                    Sfx.Play("call_connect", 0.8f);
                    break;
                case EventType.CallEnded:
                    Sfx.Play("call_end", 0.7f);
                    break;
                case EventType.EmailSent:
                    Sfx.Play("email_ding", 0.6f);
                    break;
                case EventType.InternetDown:
                    Sfx.Play("internet_down", 0.8f);
                    break;
                case EventType.InternetRestored:
                    Sfx.Play("router_restore", 0.8f);
                    break;
                case EventType.PowerDown:
                    Sfx.Play("power_down"); // blackout itself is state-driven (Update), so a late view cannot restart it
                    if (cam != null) { cam.Shake(0.3f); cam.Punch(Vector3.down, 0.5f); }
                    break;
                case EventType.PowerRestored:
                    Sfx.Play("power_up", 0.9f);
                    break;
                case EventType.PlugPulled:
                    if (DeskPoint(e.TargetId, 0.3f, out p))
                    {
                        Sfx.PlayAt("spark", p);
                        if (AvatarVisibility.CanSee(p)) FxPool.Play(FxKind.Sparks, p);
                    }
                    break;
                case EventType.PlugRestored:
                    if (DeskPoint(e.TargetId, 0.3f, out p)) Sfx.PlayAt("plug_in", p, 0.8f);
                    break;
                case EventType.MailArrived:
                {
                    var a = OfficeWorld.I != null ? OfficeWorld.I.Anchor(EntityIds.Mailroom) : null;
                    var slot = GameConfig.Layout != null ? GameConfig.Layout.GetSlot(EntityIds.Mailroom) : null;
                    if (a != null) Sfx.PlayAt("mail_drop", a.position, 0.9f);
                    else if (slot != null) Sfx.PlayAt("mail_drop", V(slot.Position), 0.9f);
                    break;
                }
                case EventType.ManagerTipReceived:
                    Sfx.ManagerVoice(0.8f);
                    break;
                case EventType.FootageShown:
                    Sfx.Play("vhs", 0.8f);
                    ScreenFx.Cctv(3f);
                    break;
                case EventType.CoffeeTasted:
                    if (e.IntArg == 1)
                    {
                        Sfx.Play("bleh", 0.9f);
                        Shock(cs.MySlot, 2f);
                        if (cam != null) cam.Shake(0.15f);
                    }
                    break;
            }
        }

        // Observation events can arrive before the snapshot that contains the observation: keep the id for a few seconds.
        readonly List<int> _pendingObs = new List<int>(8);
        readonly List<float> _pendingObsUntil = new List<float>(8);

        void FlushPendingObservations(PlayerView view, int me)
        {
            for (int i = _pendingObs.Count - 1; i >= 0; i--)
            {
                int id = _pendingObs[i];
                if (Time.time > _pendingObsUntil[i] || HasObservation(view, id))
                {
                    _pendingObs.RemoveAt(i);
                    _pendingObsUntil.RemoveAt(i);
                    if (HasObservation(view, id)) OnObservation(view, id, me);
                }
            }
        }

        static bool HasObservation(PlayerView view, int id)
        {
            for (int i = 0; i < view.Observations.Count; i++) if (view.Observations[i].Id == id) return true;
            return false;
        }

        void OnObservation(PlayerView view, int observationId, int me)
        {
            ObservationView obs = null;
            for (int i = 0; i < view.Observations.Count; i++) if (view.Observations[i].Id == observationId) { obs = view.Observations[i]; break; }
            if (obs == null)
            {
                if (_pendingObs.Count < 16 && !_pendingObs.Contains(observationId)) { _pendingObs.Add(observationId); _pendingObsUntil.Add(Time.time + 3f); }
                return;
            }
            if (obs.Severity > 0f) Shock(obs.Subject, 2.2f);
            else if (obs.Severity < 0f) Cheer(obs.Subject, 2f);
            if (obs.Subject != me || obs.Severity <= 0f) return;
            bool act = obs.Kind == ObservationKind.CaughtInAct;
            Sfx.Play("caught", act ? 1f : 0.7f);
            var cam = CameraRig.I;
            if (cam == null) return;
            cam.Shake(act ? 0.55f : 0.3f);
            cam.Punch(Vector3.back, act ? 1f : 0.5f);
            cam.FovKick(act ? -6f : -3f);
        }

        void OnPhaseChanged(MatchPhase from, MatchPhase to)
        {
            switch (to)
            {
                case MatchPhase.MeetingDiscussion: Sfx.Play("gavel"); break;
                case MatchPhase.MeetingVerdict: Sfx.Play("gavel", 0.8f); break;
                case MatchPhase.Results: _resultsAt = Time.time + 1.2f; break;
            }
            if (!AvatarVisibility.AppliesIn(to)) StopLoops();
        }

        // ------------------------------------------------------------------ per frame

        void Update()
        {
            if (_subscribed == null) Subscribe();
            var cs = ClientState.I;
            var view = cs != null ? cs.View : null;
            if (view == null)
            {
                if (_lastMatch != int.MinValue) { _lastMatch = int.MinValue; ResetMatchState(); }
                ScreenFx.SetBlackout(false);
                FxPool.SetSweat(false, Vector3.zero);
                FxPool.SetSaltSparkle(false, Vector3.zero);
                return;
            }
            if (view.MatchId != _lastMatch) { _lastMatch = view.MatchId; ResetMatchState(); _pendingObs.Clear(); _pendingObsUntil.Clear(); }
            if (_pendingObs.Count > 0) FlushPendingObservations(view, cs.MySlot);

            bool office = AvatarVisibility.AppliesIn(view.Phase);
            ScreenFx.SetBlackout(office && view.Office != null && view.Office.PowerDown);
            UpdateRinging(cs, view, office);
            UpdatePlayers(cs, view);
            UpdateDesks(view, office);
            UpdateManagerReaction(cs, view);
            UpdateLocalCues(cs, view, office);
            if (Time.time >= _nextExpr)
            {
                _nextExpr = Time.time + 0.2f;
                UpdateExpressions(cs, view);
            }
            if (_resultsAt > 0f && Time.time >= _resultsAt)
            {
                _resultsAt = -1f;
                if (view.Phase == MatchPhase.Results && view.Result != null)
                {
                    if (!FeelHooks.UiDrivesResultsStamp) FeelHooks.ResultsStamp(new Vector2(Screen.width * 0.72f, Screen.height * 0.62f));
                    if (view.Result.FiredPlayer != cs.MySlot)
                    {
                        FxPool.ConfettiAtScreenEdges();
                        Sfx.Play("confetti", 0.8f);
                    }
                }
            }
        }

        void UpdateRinging(ClientState cs, PlayerView view, bool office)
        {
            int me = cs.MySlot;
            for (int i = 0; i < Max; i++) _ringSeen[i] = false;
            if (office)
                for (int i = 0; i < view.Players.Count; i++)
                {
                    var pl = view.Players[i];
                    int id = pl.Id;
                    if (!PlayerIds.IsValid(id) || !pl.Ringing || !pl.Connected) continue;
                    if (id == me) Sfx.LoopAt(RingKeys[id], "ringtone", Vector3.zero, 0.55f, false);
                    else
                    {
                        var a = PlayerAvatar.BySlot(id);
                        if (a == null) continue;
                        Sfx.LoopAt(RingKeys[id], "ringtone", a.transform.position + Vector3.up, 0.9f, true);
                    }
                    _ringSeen[id] = true;
                    _ringing[id] = true;
                }
            for (int i = 0; i < Max; i++)
                if (_ringing[i] && !_ringSeen[i]) { Sfx.StopLoop(RingKeys[i]); _ringing[i] = false; }

            // Outgoing call: DTMF dial, then ring-back until answered / declined.
            var call = view.Me != null ? view.Me.Call : null;
            var st = call != null ? call.State : CallState.None;
            if (st != _callState)
            {
                if (st == CallState.RingingOut) { Sfx.Play("tip_dial", 0.55f); _ringOutSince = Time.time; }
                _callState = st;
            }
            bool ringback = office && st == CallState.RingingOut && Time.time - _ringOutSince > 0.85f;
            if (ringback) { Sfx.LoopAt(RingbackKey, "ringback", Vector3.zero, 0.7f, false); _ringbackOn = true; }
            else if (_ringbackOn) { Sfx.StopLoop(RingbackKey); _ringbackOn = false; }
        }

        void UpdatePlayers(ClientState cs, PlayerView view)
        {
            double now = cs.Now;
            int me = cs.MySlot;
            for (int i = 0; i < view.Players.Count; i++)
            {
                var pl = view.Players[i];
                int id = pl.Id;
                if (!PlayerIds.IsValid(id)) continue;
                var a = PlayerAvatar.BySlot(id);
                var feel = FeelHooks.Of(a);

                // Emotes: state-driven, once per (emote, until) pair.
                if (pl.Emote >= 0 && pl.EmoteUntil > now && pl.EmoteUntil != _emoteUntil[id])
                {
                    _emoteUntil[id] = pl.EmoteUntil;
                    if (feel != null) feel.PlayEmote((EmoteId)pl.Emote);
                    if (a != null && (id == me || AvatarVisibility.IsVisible(id)))
                        Sfx.PlayAt("emote_pop", a.transform.position + Vector3.up * 1.4f, 0.6f, Random.Range(0.95f, 1.1f));
                }

                // Activity starts with a sound of their own (others hear it, wall-muffled).
                if (pl.Activity != _activity[id])
                {
                    if (pl.Activity == PublicActivity.Drinking && a != null)
                        Sfx.PlayAt("slurp", a.transform.position + Vector3.up * 1.1f, 0.8f);
                    _activity[id] = pl.Activity;
                }

                // Remote sneak pose from the replicated flag (the local one is pushed instantly by LocalPlayerController).
                if (a != null && id != me && feel != null)
                {
                    sbyte c = OwnerNetworkTransform.IsCrouched(a.gameObject) ? (sbyte)1 : (sbyte)0;
                    if (c != _crouch[id]) { _crouch[id] = c; feel.SetCrouched(c == 1); }
                }
            }
        }

        /// <summary>Coffee splash when a desk gets a spill, dust puff when clutter lands on it (seen → VFX, heard → sound).</summary>
        void UpdateDesks(PlayerView view, bool office)
        {
            for (int i = 0; i < view.Desks.Count; i++)
            {
                var d = view.Desks[i];
                int k = d.Index;
                if (k < 0 || k >= MaxDesks) continue;
                sbyte spill = d.Spill ? (sbyte)1 : (sbyte)0;
                short clutter = (short)d.ClutterCount;
                bool known = _deskSpill[k] >= 0;
                bool newSpill = known && spill == 1 && _deskSpill[k] == 0;
                bool newClutter = known && clutter > _deskClutter[k];
                _deskSpill[k] = spill;
                _deskClutter[k] = clutter;
                if (!office || (!newSpill && !newClutter)) continue;
                Vector3 p;
                if (!DeskPoint(d.Id, 0.8f, out p)) continue;
                bool seen = AvatarVisibility.CanSee(p);
                if (newSpill)
                {
                    Sfx.PlayAt("splash", p, 0.9f);
                    if (seen) FxPool.Play(FxKind.CoffeeSplash, p);
                }
                if (newClutter)
                {
                    Sfx.PlayAt("dust", p, 0.7f);
                    if (seen) FxPool.Play(FxKind.DustPuff, p);
                }
            }
        }

        void UpdateManagerReaction(ClientState cs, PlayerView view)
        {
            var mv = view.Manager;
            if (mv == null || mv.ReactionObservation < 0 || mv.ReactionObservation == _lastReaction || mv.ReactionUntil <= cs.Now) return;
            _lastReaction = mv.ReactionObservation;
            var mgr = OfficeWorld.I != null ? OfficeWorld.I.Manager : null;
            Vector3 p = mgr != null ? mgr.transform.position : V(mv.Position);
            FxPool.Exclaim(p + Vector3.up * 2.25f);
        }

        void UpdateLocalCues(ClientState cs, PlayerView view, bool office)
        {
            var local = PlayerAvatar.Local;
            var me = cs.Me;

            // Private action sounds (the others only see the public activity).
            var act = view.Me != null ? view.Me.ActiveInteraction : null;
            var verb = act != null ? act.Verb : ActionVerb.None;
            if (verb != _myVerb)
            {
                if (verb == ActionVerb.SaltCoffee) Sfx.Play("salt_shake", 0.7f);
                else if (verb == ActionVerb.TipManager) Sfx.Play("tip_dial", 0.6f);
                _myVerb = verb;
            }

            // Nervous sweat: the manager is patrolling close by and I carry something incriminating.
            bool sweat = false;
            if (office && local != null && me != null && ManagerPatrolling(view))
            {
                Vector3 d = V(view.Manager.Position) - local.transform.position; d.y = 0;
                sweat = d.sqrMagnitude < SweatRange * SweatRange && CarryingSuspicious(view, me);
            }
            FxPool.SetSweat(sweat, local != null ? local.transform.position + Vector3.up * 1.35f : Vector3.zero);

            // Salt sparkle on the mug I know is salted (never where I could not see it anyway).
            ItemView salted = null;
            if (office)
                for (int i = 0; i < view.Items.Count; i++)
                {
                    var it = view.Items[i];
                    if (it.KnownSalted && it.Location != ItemLocationKind.Archived) { salted = it; break; }
                }
            string sid = salted != null ? salted.Id : "";
            if (sid != _saltItem) { _saltItem = sid; _saltTarget = null; _nextSaltLookup = 0f; }
            if (salted != null && _saltTarget == null && Time.time >= _nextSaltLookup)
            {
                _nextSaltLookup = Time.time + 0.5f;
                _saltTarget = AvatarVisibility.FindItemVisual(sid);
            }
            bool sparkle = salted != null && _saltTarget != null && _saltTarget.gameObject.activeInHierarchy &&
                           !(salted.Location == ItemLocationKind.Carried && salted.CarrierId != cs.MySlot && !AvatarVisibility.IsVisible(salted.CarrierId));
            FxPool.SetSaltSparkle(sparkle, sparkle ? _saltTarget.position + Vector3.up * 0.12f : Vector3.zero);
        }

        void UpdateExpressions(ClientState cs, PlayerView view)
        {
            float t = Time.time;
            bool patrol = ManagerPatrolling(view);
            Vector3 mgr = V(view.Manager.Position);
            for (int i = 0; i < view.Players.Count; i++)
            {
                var pl = view.Players[i];
                int id = pl.Id;
                if (!PlayerIds.IsValid(id)) continue;
                var a = PlayerAvatar.BySlot(id);
                var feel = FeelHooks.Of(a);
                if (feel == null) continue;
                FeelExpression e = FeelExpression.Neutral;
                if (view.Phase == MatchPhase.Results && view.Result != null)
                {
                    if (view.Result.FiredPlayer == id) e = FeelExpression.Sad;
                    else
                        for (int k = 0; k < view.Result.Players.Count; k++)
                            if (view.Result.Players[k].Player == id && view.Result.Players[k].Won) { e = FeelExpression.Happy; break; }
                }
                else if (view.Phase == MatchPhase.MeetingVerdict && view.Meeting.Verdict != null && view.Meeting.Verdict.FiredPlayer == id) e = FeelExpression.Shocked;
                else if (t < _shockUntil[id]) e = FeelExpression.Shocked;
                else if (t < _happyUntil[id]) e = FeelExpression.Happy;
                else if (patrol)
                {
                    Vector3 d = mgr - a.transform.position; d.y = 0;
                    if (d.sqrMagnitude < NervousRange * NervousRange && CarryingSuspicious(view, pl)) e = FeelExpression.Nervous;
                }
                if (_expr[id] == (byte)e) continue;
                _expr[id] = (byte)e;
                feel.SetExpression(e);
            }
        }
    }
}
