using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Local human control: camera-relative WASD movement, crouch/sneak (hold Left Ctrl or toggle C), contextual
    /// interaction (focus + E/F, held item on G), drop, work pose, cancel-on-move for timed interactions, emotes
    /// (T wheel + mouse direction, or 1-5), call keys (Y answer, N decline / hang up) and the fake-email template
    /// hand-off to the UI. Disabled when the process runs as a bot.
    ///
    /// UI-facing state (read-only): <see cref="Options"/>, <see cref="HeldOption"/>, <see cref="InteractionProgress"/>,
    /// <see cref="Crouched"/>, <see cref="EmoteWheelOpen"/> / <see cref="EmoteWheelSelection"/> /
    /// <see cref="EmoteWheelCursor"/>, <see cref="AwaitingEmailTemplate"/>.
    /// </summary>
    public sealed class LocalPlayerController : MonoBehaviour
    {
        public static LocalPlayerController I { get; private set; }

        public Transform FocusMarker;
        public string FocusedId { get; private set; } = "";
        public List<Affordance> Options { get; private set; } = new List<Affordance>();
        /// <summary>Action for the item in hand (UseItem key, default G), e.g. "Tadına bak" with a full mug; null if none.</summary>
        public Affordance HeldOption { get; private set; }
        public bool HasControl { get; private set; }

        /// <summary>Sneaking (replicated through OwnerNetworkTransform; host fills AvatarSample.Crouched).</summary>
        public bool Crouched { get; private set; }
        public bool CrouchToggled => _crouchToggled;
        /// <summary>Walk speed multiplier while crouched (3.6 m/s × 0.47 ≈ 1.7 m/s).</summary>
        public const float CrouchSpeedFactor = 0.47f;

        /// <summary>0..1 progress of my running timed interaction, -1 when idle (UI draws the radial).</summary>
        public float InteractionProgress { get; private set; } = -1f;
        public ActionVerb InteractionVerb { get; private set; }
        public float InteractionRemaining { get; private set; }
        /// <summary>True while the UI's fake-email template picker is open for a pending SendFakeEmail.</summary>
        public bool AwaitingEmailTemplate { get; private set; }

        /// <summary>Emote wheel order (clockwise from 12 o'clock) and quick-key order 1..5.</summary>
        public static readonly EmoteId[] WheelEmotes = { EmoteId.Wave, EmoteId.Point, EmoteId.Facepalm, EmoteId.Shrug, EmoteId.Laugh };
        public const float EmoteWheelRadius = 120f;   // mouse pixels
        public const float EmoteWheelDeadzone = 28f;
        public bool EmoteWheelOpen { get; private set; }
        /// <summary>Highlighted wheel index (into <see cref="WheelEmotes"/>), -1 = centre / cancel.</summary>
        public int EmoteWheelSelection { get; private set; } = -1;
        /// <summary>Virtual cursor in pixels relative to the wheel centre (+y = up).</summary>
        public Vector2 EmoteWheelCursor { get; private set; }

        const float FocusRange = 2.3f;
        bool _cancelSent;
        float _nextFocus;
        bool _crouchToggled;
        float _lastEmoteSent = -10f;
        EmoteWheelGui _wheelGui;

        readonly List<(float score, string id)> _scored = new List<(float, string)>(32);
        readonly HashSet<string> _carried = new HashSet<string>(StringComparer.Ordinal);
        readonly Affordance _taste = new Affordance { Verb = ActionVerb.TasteCoffee, LabelKey = "verb.taste_coffee" };
        static readonly Comparison<(float score, string id)> ByScore = (a, b) => a.score.CompareTo(b.score);

        void Awake()
        {
            I = this;
            if (LaunchArgs.Current.Bot) return;
            if (GetComponent<AvatarVisibility>() == null) gameObject.AddComponent<AvatarVisibility>();
            _wheelGui = gameObject.AddComponent<EmoteWheelGui>();
            _wheelGui.enabled = false;
        }

        public static bool OfficePhase(MatchPhase p)
        {
            return p == MatchPhase.Work1 || p == MatchPhase.Raid || p == MatchPhase.Work2 || p == MatchPhase.FinalInspection;
        }

        void Update()
        {
            var cs = ClientState.I;
            var avatar = PlayerAvatar.Local;
            var view = cs != null ? cs.View : null;
            HasControl = avatar != null && view != null && !LaunchArgs.Current.Bot && !UIRoot.BlocksGameplay;
            if (FocusMarker != null) FocusMarker.gameObject.SetActive(false);
            if (avatar == null || view == null || LaunchArgs.Current.Bot)
            {
                Options.Clear();
                FocusedId = "";
                HeldOption = null;
                InteractionProgress = -1f;
                InteractionVerb = ActionVerb.None;
                Crouched = false;
                _crouchToggled = false;
                CloseWheel();
                return;
            }

            bool inOffice = OfficePhase(view.Phase) || view.Phase == MatchPhase.Briefing;
            var active = view.Me != null ? view.Me.ActiveInteraction : null;
            bool busy = active != null && active.Verb != ActionVerb.None;
            UpdateProgress(cs, active, busy);

            // ---- crouch (sneak)
            if (!inOffice) _crouchToggled = false;
            else if (HasControl && GameInput.CrouchToggle.WasPressedThisFrame()) _crouchToggled = !_crouchToggled;
            bool wantCrouch = inOffice && (_crouchToggled || (HasControl && GameInput.Crouch.IsPressed()));
            if (wantCrouch != Crouched)
            {
                Crouched = wantCrouch;
                var feel = FeelHooks.Of(avatar);
                if (feel != null) feel.SetCrouched(wantCrouch);
                Sfx.Play("crouch", 0.25f);
            }
            OwnerNetworkTransform.SetCrouched(avatar.gameObject, Crouched); // no-op unless changed (also re-asserts after a respawn)

            // ---- movement
            Vector2 mv = HasControl && inOffice ? GameInput.Move.ReadValue<Vector2>() : Vector2.zero;
            if (busy)
            {
                if (mv.sqrMagnitude > 0.25f && !_cancelSent)
                {
                    cs.Send(ActionVerb.CancelInteraction);
                    _cancelSent = true;
                }
                mv = Vector2.zero;
            }
            else _cancelSent = false;
            float yaw = CameraRig.I != null ? CameraRig.I.Yaw : 0f;
            Vector3 dir = Quaternion.Euler(0, yaw, 0) * new Vector3(mv.x, 0, mv.y);
            if (Crouched) dir *= CrouchSpeedFactor;
            avatar.Drive(dir, false, Time.deltaTime);
            if (busy)
            {
                Vector3 tp;
                if (OfficeWorld.I != null && OfficeWorld.I.TryTargetPosition(active.TargetId, out tp)) avatar.FaceTowards(tp, Time.deltaTime);
            }

            // ---- focus + options
            if (!OfficePhase(view.Phase) || busy)
            {
                Options.Clear();
                FocusedId = "";
            }
            else if (Time.unscaledTime >= _nextFocus)
            {
                _nextFocus = Time.unscaledTime + 0.08f;
                UpdateFocus(view, avatar);
            }
            HeldOption = OfficePhase(view.Phase) && !busy ? HeldItemOption(cs, view) : null;
            if (FocusMarker != null && !string.IsNullOrEmpty(FocusedId) && OfficeWorld.I != null)
            {
                Vector3 tp;
                if (OfficeWorld.I.TryTargetPosition(FocusedId, out tp))
                {
                    FocusMarker.gameObject.SetActive(true);
                    FocusMarker.position = tp + Vector3.up * (0.45f + Mathf.Sin(Time.time * 4f) * 0.05f);
                    FocusMarker.Rotate(0, 120f * Time.deltaTime, 0, Space.World);
                }
            }

            UpdateEmotes(cs, view);
            UpdateCalls(cs, view);

            if (!HasControl || !OfficePhase(view.Phase)) return;

            // ---- buttons
            if (GameInput.Interact.WasPressedThisFrame()) TryOption(0);
            if (GameInput.Secondary.WasPressedThisFrame()) TryOption(1);
            if (GameInput.UseItem.WasPressedThisFrame() && HeldOption != null) Execute(HeldOption);
            if (GameInput.Drop.WasPressedThisFrame())
            {
                var me = cs.Me;
                if (me != null && !string.IsNullOrEmpty(me.CarryingItemId) && !busy) cs.Send(ActionVerb.Drop);
            }
            if (GameInput.WorkPose.WasPressedThisFrame() && !busy)
            {
                var me = cs.Me;
                cs.Send(ActionVerb.SetWorkPose, "", me != null && me.WorkPose ? 0 : 1);
            }
        }

        void UpdateProgress(ClientState cs, InteractionView active, bool busy)
        {
            if (!busy)
            {
                InteractionProgress = -1f;
                InteractionVerb = ActionVerb.None;
                InteractionRemaining = 0f;
                return;
            }
            double now = cs.Now;
            double len = active.EndTime - active.StartTime;
            InteractionProgress = len > 0.001 ? Mathf.Clamp01((float)((now - active.StartTime) / len)) : 1f;
            InteractionVerb = active.Verb;
            InteractionRemaining = Mathf.Max(0f, (float)(active.EndTime - now));
        }

        Affordance HeldItemOption(ClientState cs, PlayerView view)
        {
            var me = cs.Me;
            if (me == null || string.IsNullOrEmpty(me.CarryingItemId)) return null;
            for (int i = 0; i < view.Items.Count; i++)
            {
                var it = view.Items[i];
                if (it.Id != me.CarryingItemId) continue;
                if (it.Kind != ItemKind.Mug || it.Fill != MugFill.Full) return null;
                _taste.Seconds = cs.Config != null && cs.Config.Tasks != null ? cs.Config.Tasks.TasteSeconds : 1f;
                _taste.BlockedBy = FailureCode.None;
                return _taste;
            }
            return null;
        }

        // ---------------------------------------------------------------- options / interactions

        void TryOption(int index)
        {
            if (index >= Options.Count) return;
            Execute(Options[index]);
        }

        void Execute(Affordance o)
        {
            if (o.BlockedBy != FailureCode.None)
            {
                Hud.Toast(Loc.Failure(o.BlockedBy), Hud.ToastKind.Warning);
                Sfx.Play("deny");
                return;
            }
            if (o.Verb == ActionVerb.SendFakeEmail && o.IntParam < 0)
            {
                RequestEmailTemplate(o.TargetId);
                return;
            }
            ClientState.I.Send(o.Verb, o.TargetId, o.IntParam);
            Sfx.Play("click");
        }

        /// <summary>Fake email needs a template: the UI's picker chooses it (fallback without a UI hook: random).</summary>
        void RequestEmailTemplate(string pcTarget)
        {
            var cs = ClientState.I;
            var ui = FeelHooks.Ui;
            if (ui == null)
            {
                int count = cs.Config != null && cs.Config.Email != null ? Mathf.Max(1, cs.Config.Email.TemplateCount) : 1;
                cs.Send(ActionVerb.SendFakeEmail, pcTarget, UnityEngine.Random.Range(0, count));
                Sfx.Play("click");
                return;
            }
            if (AwaitingEmailTemplate) return;
            AwaitingEmailTemplate = true;
            int match = cs.View != null ? cs.View.MatchId : -1;
            ui.PickEmailTemplate(pcTarget, template =>
            {
                AwaitingEmailTemplate = false;
                var c = ClientState.I;
                if (template < 0 || c == null || c.View == null || c.View.MatchId != match) return;
                c.Send(ActionVerb.SendFakeEmail, pcTarget, template);
                Sfx.Play("click");
            });
        }

        static bool Purposeful(ActionVerb verb, string label)
        {
            switch (verb)
            {
                case ActionVerb.BrewCoffee:
                case ActionVerb.WriteReport:
                case ActionVerb.AnswerPhone:
                case ActionVerb.CleanDesk:
                case ActionVerb.CleanSpill:
                case ActionVerb.UnlockWorkstation:
                // v2: repairs and self-protection come before generic take/put-back
                case ActionVerb.PlugIn:
                case ActionVerb.RestartRouter:
                case ActionVerb.ResetFuse:
                case ActionVerb.LockScreen:
                    return true;
                case ActionVerb.Place:
                    return label == "verb.deliver" || label == "verb.deliver_mail";
                default:
                    return false;
            }
        }

        void UpdateFocus(PlayerView view, PlayerAvatar avatar)
        {
            List<string> ids;
            try { ids = Affordances.TargetIds(view, GameConfig.Layout); }
            catch (NotImplementedException) { ids = null; }
            Vector3 p = avatar.transform.position;
            Vector3 fwd = CameraRig.I != null ? Quaternion.Euler(0, CameraRig.I.Yaw, 0) * Vector3.forward : avatar.transform.forward;
            Vector3 bodyFwd = avatar.transform.forward;
            _scored.Clear();
            _carried.Clear();
            for (int i = 0; i < view.Items.Count; i++) if (view.Items[i].Location == ItemLocationKind.Carried) _carried.Add(view.Items[i].Id);
            if (ids != null && OfficeWorld.I != null)
                for (int i = 0; i < ids.Count; i++)
                {
                    string id = ids[i];
                    if (_carried.Contains(id)) continue; // never focus an item in someone's hands (including your own)
                    Vector3 tp;
                    if (!OfficeWorld.I.TryTargetPosition(id, out tp)) continue;
                    Vector3 d = tp - p; d.y = 0;
                    float dist = d.magnitude;
                    if (dist > FocusRange) continue;
                    float ang = dist < 0.05f ? 0f : Vector3.Angle(fwd, d);
                    float angBody = dist < 0.05f ? 0f : Vector3.Angle(bodyFwd, d);
                    _scored.Add((dist + Mathf.Min(ang, angBody) / 60f, id));
                }
            _scored.Sort(ByScore);
            string bestId = "";
            List<Affordance> best = null, bestBlocked = null;
            string bestBlockedId = "";
            float bestScore = float.MaxValue;
            var myPos = new Vec3(p.x, p.y, p.z);
            for (int i = 0; i < _scored.Count && i < 6; i++)
            {
                List<Affordance> opts;
                try { opts = Affordances.For(view, ClientState.I.Config, GameConfig.Layout, _scored[i].id, myPos); }
                catch (Exception) { opts = null; }
                if (opts == null || opts.Count == 0) continue;
                Affordance free = null;
                for (int k = 0; k < opts.Count; k++) if (opts[k].BlockedBy == FailureCode.None) { free = opts[k]; break; }
                if (free != null)
                {
                    // Prefer options that move work forward over generic take/put-back on a neighbouring shelf.
                    float score = _scored[i].score - (Purposeful(free.Verb, free.LabelKey) ? 0.9f : 0f);
                    if (score < bestScore) { bestScore = score; best = opts; bestId = _scored[i].id; }
                }
                else if (bestBlocked == null) { bestBlocked = opts; bestBlockedId = _scored[i].id; }
            }
            if (best == null && bestBlocked != null) { best = bestBlocked; bestId = bestBlockedId; }
            FocusedId = bestId;
            // Keep the engine's ordering, but bring available options in front of blocked ones (stable).
            Options.Clear();
            if (best == null) return;
            for (int k = 0; k < best.Count; k++) if (best[k].BlockedBy == FailureCode.None) Options.Add(best[k]);
            for (int k = 0; k < best.Count; k++) if (best[k].BlockedBy != FailureCode.None) Options.Add(best[k]);
        }

        // ---------------------------------------------------------------- emotes

        void UpdateEmotes(ClientState cs, PlayerView view)
        {
            bool phaseOk = view.Phase != MatchPhase.None && view.Phase != MatchPhase.Abandoned;
            bool seated = view.Phase >= MatchPhase.MeetingDiscussion && view.Phase <= MatchPhase.Results;
            // Quick keys also work in the meeting/results (modal UI, no text field focused).
            if (phaseOk && !FeelHooks.TextInputFocused && (HasControl || seated))
                for (int i = 0; i < GameInput.EmoteKeys.Length && i < WheelEmotes.Length; i++)
                    if (GameInput.EmoteKeys[i] != null && GameInput.EmoteKeys[i].WasPressedThisFrame()) SendEmote(cs, WheelEmotes[i]);

            if (!EmoteWheelOpen)
            {
                if (phaseOk && HasControl && GameInput.EmoteWheel.WasPressedThisFrame())
                {
                    EmoteWheelOpen = true;
                    EmoteWheelCursor = Vector2.zero;
                    EmoteWheelSelection = -1;
                }
            }
            else if (!HasControl || !phaseOk) CloseWheel();
            else
            {
                Vector2 c = EmoteWheelCursor + GameInput.Look.ReadValue<Vector2>();
                if (c.sqrMagnitude > EmoteWheelRadius * EmoteWheelRadius) c = c.normalized * EmoteWheelRadius;
                EmoteWheelCursor = c;
                int sel = c.sqrMagnitude < EmoteWheelDeadzone * EmoteWheelDeadzone ? -1 : Sector(c);
                if (sel != EmoteWheelSelection)
                {
                    EmoteWheelSelection = sel;
                    if (sel >= 0) Sfx.Play("wheel_tick", 0.35f);
                }
                if (!GameInput.EmoteWheel.IsPressed())
                {
                    int s = EmoteWheelSelection;
                    CloseWheel();
                    if (s >= 0) SendEmote(cs, WheelEmotes[s]);
                }
            }
            if (_wheelGui != null)
            {
                bool draw = EmoteWheelOpen && (FeelHooks.Ui == null || !FeelHooks.Ui.DrawsEmoteWheel);
                if (_wheelGui.enabled != draw) _wheelGui.enabled = draw;
            }
        }

        /// <summary>Wheel sector of a cursor offset: 0 at 12 o'clock, clockwise, 5 sectors.</summary>
        public static int Sector(Vector2 c)
        {
            float a = Mathf.Atan2(c.x, c.y) * Mathf.Rad2Deg;
            if (a < 0) a += 360f;
            float size = 360f / WheelEmotes.Length;
            return (int)((a + size * 0.5f) / size) % WheelEmotes.Length;
        }

        void CloseWheel()
        {
            EmoteWheelOpen = false;
            EmoteWheelSelection = -1;
            EmoteWheelCursor = Vector2.zero;
            if (_wheelGui != null && _wheelGui.enabled) _wheelGui.enabled = false;
        }

        void SendEmote(ClientState cs, EmoteId id)
        {
            var me = cs.Me;
            if (me != null && me.Emote >= 0 && me.EmoteUntil > cs.Now) return; // engine: Cooldown while one is playing
            if (Time.unscaledTime - _lastEmoteSent < 0.4f) return;
            _lastEmoteSent = Time.unscaledTime;
            cs.Send(ActionVerb.Emote, "", (int)id);
        }

        // ---------------------------------------------------------------- calls

        void UpdateCalls(ClientState cs, PlayerView view)
        {
            if (view.Phase < MatchPhase.Briefing || view.Phase > MatchPhase.FinalInspection || FeelHooks.TextInputFocused) return;
            var call = view.Me != null ? view.Me.Call : null;
            if (call == null) return;
            if (GameInput.AnswerCall.WasPressedThisFrame() && call.State == CallState.RingingIn)
                cs.Send(ActionVerb.AnswerCall);
            if (GameInput.HangUp.WasPressedThisFrame())
            {
                switch (call.State)
                {
                    case CallState.RingingIn:
                    case CallState.RingingOut: cs.Send(ActionVerb.DeclineCall); break;
                    case CallState.Active: cs.Send(ActionVerb.HangUp); break;
                }
            }
        }
    }
}
