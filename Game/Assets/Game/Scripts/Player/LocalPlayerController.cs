using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Local human control: camera-relative WASD movement, contextual interaction (focus + E/F), drop, work pose,
    /// and cancel-on-move for timed interactions. Disabled when the process runs as a bot.
    /// </summary>
    public sealed class LocalPlayerController : MonoBehaviour
    {
        public static LocalPlayerController I { get; private set; }

        public Transform FocusMarker;
        public string FocusedId { get; private set; } = "";
        public List<Affordance> Options { get; private set; } = new List<Affordance>();
        public bool HasControl { get; private set; }

        const float FocusRange = 2.3f;
        bool _cancelSent;
        float _nextFocus;

        void Awake() { I = this; }

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
            if (avatar == null || view == null || LaunchArgs.Current.Bot) { Options.Clear(); FocusedId = ""; return; }

            bool inOffice = OfficePhase(view.Phase) || view.Phase == MatchPhase.Briefing;
            var active = view.Me != null ? view.Me.ActiveInteraction : null;
            bool busy = active != null && active.Verb != ActionVerb.None;

            // ---- movement
            Vector2 mv = HasControl && inOffice ? GameInput.Move.ReadValue<Vector2>() : Vector2.zero;
            bool slow = HasControl && GameInput.Slow.IsPressed();
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
            avatar.Drive(dir, slow, Time.deltaTime);
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
            if (FocusMarker != null && !string.IsNullOrEmpty(FocusedId))
            {
                Vector3 tp;
                if (OfficeWorld.I.TryTargetPosition(FocusedId, out tp))
                {
                    FocusMarker.gameObject.SetActive(true);
                    FocusMarker.position = tp + Vector3.up * (0.45f + Mathf.Sin(Time.time * 4f) * 0.05f);
                    FocusMarker.Rotate(0, 120f * Time.deltaTime, 0, Space.World);
                }
            }

            if (!HasControl || !OfficePhase(view.Phase)) return;

            // ---- buttons
            if (GameInput.Interact.WasPressedThisFrame()) TryOption(0);
            if (GameInput.Secondary.WasPressedThisFrame()) TryOption(1);
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

        void TryOption(int index)
        {
            if (index >= Options.Count) return;
            var o = Options[index];
            if (o.BlockedBy != FailureCode.None)
            {
                Hud.Toast(Loc.Failure(o.BlockedBy), Hud.ToastKind.Warning);
                Sfx.Play("deny");
                return;
            }
            ClientState.I.Send(o.Verb, o.TargetId, o.IntParam);
            Sfx.Play("click");
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
                    return true;
                case ActionVerb.Place:
                    return label == "verb.deliver";
                default:
                    return false;
            }
        }

        void UpdateFocus(PlayerView view, PlayerAvatar avatar)
        {
            List<string> ids;
            try { ids = Affordances.TargetIds(view, GameConfig.Layout); }
            catch (NotImplementedException) { ids = new List<string>(); }
            Vector3 p = avatar.transform.position;
            Vector3 fwd = CameraRig.I != null ? Quaternion.Euler(0, CameraRig.I.Yaw, 0) * Vector3.forward : avatar.transform.forward;
            Vector3 bodyFwd = avatar.transform.forward;
            var scored = new List<(float score, string id)>();
            var carried = new HashSet<string>();
            foreach (var it in view.Items) if (it.Location == ItemLocationKind.Carried) carried.Add(it.Id);
            foreach (var id in ids)
            {
                if (carried.Contains(id)) continue; // never focus an item in someone's hands (including your own)
                Vector3 tp;
                if (!OfficeWorld.I.TryTargetPosition(id, out tp)) continue;
                Vector3 d = tp - p; d.y = 0;
                float dist = d.magnitude;
                if (dist > FocusRange) continue;
                float ang = dist < 0.05f ? 0f : Vector3.Angle(fwd, d);
                float angBody = dist < 0.05f ? 0f : Vector3.Angle(bodyFwd, d);
                float score = dist + Mathf.Min(ang, angBody) / 60f;
                scored.Add((score, id));
            }
            scored.Sort((a, b) => a.score.CompareTo(b.score));
            string bestId = "";
            List<Affordance> best = null, bestBlocked = null;
            string bestBlockedId = "";
            float bestScore = float.MaxValue;
            var myPos = new Vec3(p.x, p.y, p.z);
            for (int i = 0; i < scored.Count && i < 6; i++)
            {
                List<Affordance> opts;
                try { opts = Affordances.For(view, ClientState.I.Config, GameConfig.Layout, scored[i].id, myPos); }
                catch (Exception) { opts = null; }
                if (opts == null || opts.Count == 0) continue;
                var free = opts.Find(o => o.BlockedBy == FailureCode.None);
                if (free != null)
                {
                    // Prefer options that move work forward over generic take/put-back on a neighbouring shelf.
                    float score = scored[i].score - (Purposeful(free.Verb, free.LabelKey) ? 0.9f : 0f);
                    if (score < bestScore) { bestScore = score; best = opts; bestId = scored[i].id; }
                }
                else if (bestBlocked == null) { bestBlocked = opts; bestBlockedId = scored[i].id; }
            }
            if (best == null && bestBlocked != null) { best = bestBlocked; bestId = bestBlockedId; }
            FocusedId = bestId;
            Options = best ?? new List<Affordance>();
            // Keep the engine's ordering, but bring available options in front of blocked ones (stable).
            var ordered = Options.FindAll(o => o.BlockedBy == FailureCode.None);
            ordered.AddRange(Options.FindAll(o => o.BlockedBy != FailureCode.None));
            Options = ordered;
        }
    }
}
