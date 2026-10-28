using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// "Kimse her şeyi görmez": a remote avatar (body, carried item, name tag, any bubbles parented to it) is shown only
    /// while the LOCAL avatar has line of sight to it. Rays go from the local eye to the target's chest and head and are
    /// blocked by Walls and Partitions (glass does not block). Power cut: sight range drops to <see cref="DarkDistance"/>.
    /// The manager is never hidden; menu / meeting / results / spectating (no local avatar) show everyone.
    ///
    /// Cost: at most <see cref="ChecksPerFrame"/> avatars are ray-tested per frame (≤ 2 linecasts each, round-robin),
    /// fades are per-frame float math, renderer flags are only written when they change. No per-frame allocations
    /// (renderer arrays are collected when an avatar appears or its hierarchy changes).
    /// Presentation only: voice, sounds and rules are unaffected.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed class AvatarVisibility : MonoBehaviour
    {
        public static AvatarVisibility I { get; private set; }

        [Header("Line of sight")]
        public float EyeHeight = 1.5f;
        public float EyeHeightCrouched = 0.95f;
        public float TargetHeight = 1.2f;
        public float TargetHeightCrouched = 0.6f;
        /// <summary>Second probe above the target point (top of the head peeking over a partition).</summary>
        public float HeadOffset = 0.3f;
        public float MaxDistance = 45f;
        public float DarkDistance = 4f;

        [Header("Timing")]
        public float FadeSeconds = 0.2f;
        /// <summary>Line of sight must be lost this long before the avatar starts fading out (no edge flicker).</summary>
        public float HideGrace = 0.3f;
        public int ChecksPerFrame = 3;

        sealed class Entry
        {
            public PlayerAvatar Avatar;
            public IAvatarFeel Feel;
            public Renderer[] Renderers;
            public int HierarchyCount = -1;
            public bool RenderersHidden;
            public float Alpha;
            public float AppliedAlpha = -1f;
            public bool Target;
            public float LastSeen = -999f;
            public bool NeedsCheck = true;
            public bool Alive;
            // carried item visual (positioned by OfficeWorld, not parented to the avatar)
            public string CarriedId = "";
            public Renderer[] CarriedRenderers;
            public bool CarriedHidden;
            public float NextCarriedLookup;
        }

        readonly List<Entry> _entries = new List<Entry>(8);
        readonly List<Renderer> _rendererScratch = new List<Renderer>(32);
        int _cursor;
        bool _allVisible = true;

        /// <summary>Phases in which line of sight applies.</summary>
        public static bool AppliesIn(MatchPhase p) { return p >= MatchPhase.Briefing && p <= MatchPhase.FinalInspection; }

        void Awake() { I = this; }

        void OnDisable()
        {
            for (int i = 0; i < _entries.Count; i++) Restore(_entries[i]);
            _entries.Clear();
        }

        // ------------------------------------------------------------------ queries (UI overlays, FX gating)

        /// <summary>True when the avatar of <paramref name="slot"/> is currently shown (always true for the local player).</summary>
        public static bool IsVisible(int slot) { return Alpha(slot) > 0.5f; }

        /// <summary>Current fade of a player's avatar (1 = visible). Use it to fade screen-space overlays (name, bubbles).</summary>
        public static float Alpha(int slot)
        {
            var v = I;
            if (v == null || v._allVisible) return 1f;
            var local = PlayerAvatar.Local;
            if (local != null && local.Slot == slot) return 1f;
            for (int i = 0; i < v._entries.Count; i++)
            {
                var e = v._entries[i];
                if (e.Avatar != null && e.Avatar.Slot == slot) return e.Alpha;
            }
            return 1f;
        }

        /// <summary>Can the local avatar see this world point right now? (Used to gate one-shot VFX.)</summary>
        public static bool CanSee(Vector3 point)
        {
            var v = I;
            var local = PlayerAvatar.Local;
            if (v == null || local == null || v._allVisible) return true;
            Vector3 eye = v.EyePosition(local);
            float max = v.CurrentMaxDistance();
            Vector3 d = point - eye; d.y = 0;
            if (d.sqrMagnitude > max * max) return false;
            return !Physics.Linecast(eye, point, FeelLayers.SightBlockers, QueryTriggerInteraction.Ignore);
        }

        Vector3 EyePosition(PlayerAvatar local)
        {
            bool crouched = OwnerNetworkTransform.IsCrouched(local.gameObject);
            return local.transform.position + Vector3.up * (crouched ? EyeHeightCrouched : EyeHeight);
        }

        float CurrentMaxDistance()
        {
            var view = ClientState.I != null ? ClientState.I.View : null;
            bool dark = view != null && view.Office != null && view.Office.PowerDown;
            return dark ? DarkDistance : MaxDistance;
        }

        // ------------------------------------------------------------------ per frame

        void LateUpdate()
        {
            float now = Time.time;
            var view = ClientState.I != null ? ClientState.I.View : null;
            var local = PlayerAvatar.Local;
            Sync(local);

            _allVisible = view == null || local == null || !AppliesIn(view.Phase) || LaunchArgs.Current.Bot;
            int n = _entries.Count;
            if (_allVisible)
            {
                for (int i = 0; i < n; i++) { var e = _entries[i]; e.Target = true; e.LastSeen = now; }
            }
            else if (n > 0)
            {
                Vector3 eye = EyePosition(local);
                float max = CurrentMaxDistance();
                int budget = Mathf.Max(1, ChecksPerFrame);
                for (int i = 0; i < n; i++)
                    if (_entries[i].NeedsCheck) { Check(_entries[i], eye, max, now); budget--; }
                for (int k = 0; k < budget && k < n; k++)
                {
                    _cursor = (_cursor + 1) % n;
                    Check(_entries[_cursor], eye, max, now);
                }
            }

            float step = FadeSeconds > 0.001f ? Time.deltaTime / FadeSeconds : 1f;
            for (int i = 0; i < n; i++)
            {
                var e = _entries[i];
                e.Alpha = Mathf.MoveTowards(e.Alpha, e.Target ? 1f : 0f, step);
                Apply(e, view, now);
            }
        }

        void Check(Entry e, Vector3 eye, float maxDistance, float now)
        {
            e.NeedsCheck = false;
            if (e.Avatar == null) return;
            Vector3 p = e.Avatar.transform.position;
            Vector3 flat = p - eye; flat.y = 0;
            bool seen = false;
            if (flat.sqrMagnitude <= maxDistance * maxDistance)
            {
                bool crouched = OwnerNetworkTransform.IsCrouched(e.Avatar.gameObject);
                Vector3 t = p + Vector3.up * (crouched ? TargetHeightCrouched : TargetHeight);
                int mask = FeelLayers.SightBlockers;
                seen = !Physics.Linecast(eye, t, mask, QueryTriggerInteraction.Ignore) ||
                       !Physics.Linecast(eye, t + Vector3.up * HeadOffset, mask, QueryTriggerInteraction.Ignore);
            }
            if (seen) { e.LastSeen = now; e.Target = true; }
            else if (now - e.LastSeen > HideGrace) e.Target = false;
        }

        void Sync(PlayerAvatar local)
        {
            for (int i = 0; i < _entries.Count; i++) _entries[i].Alive = false;
            var all = PlayerAvatar.All;
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a == null || a == local) continue;
                Entry found = null;
                for (int j = 0; j < _entries.Count; j++) if (_entries[j].Avatar == a) { found = _entries[j]; break; }
                if (found == null)
                {
                    // New avatars start hidden while line of sight applies (checked this frame) so they never pop in.
                    found = new Entry { Avatar = a, Alpha = _allVisible ? 1f : 0f, Target = _allVisible };
                    _entries.Add(found);
                }
                found.Alive = true;
            }
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Alive) continue;
                Restore(_entries[i]);
                _entries.RemoveAt(i);
            }
        }

        // ------------------------------------------------------------------ applying

        void Apply(Entry e, PlayerView view, float now)
        {
            var a = e.Avatar;
            if (a == null) return;
            int hc = a.transform.hierarchyCount;
            if (e.Renderers == null || hc != e.HierarchyCount) Rebuild(e, hc);

            if (Mathf.Abs(e.Alpha - e.AppliedAlpha) > 0.0005f)
            {
                e.AppliedAlpha = e.Alpha;
                bool handled = e.Feel != null && e.Feel.SetVisibility(e.Alpha);
                SetHidden(e.Renderers, ref e.RenderersHidden, !handled && e.Alpha < 0.5f);
                if (a.NameTag != null) a.NameTag.alpha = e.Alpha;
            }
            UpdateCarried(e, view, now);
        }

        void Rebuild(Entry e, int hierarchyCount)
        {
            var a = e.Avatar;
            e.HierarchyCount = hierarchyCount;
            bool wasHidden = e.RenderersHidden;
            if (e.Renderers != null) { bool h = e.RenderersHidden; SetHidden(e.Renderers, ref h, false); }
            _rendererScratch.Clear();
            a.GetComponentsInChildren(true, _rendererScratch);
            Renderer nameTag = a.NameTag != null ? a.NameTag.GetComponent<Renderer>() : null;
            int count = 0;
            for (int i = 0; i < _rendererScratch.Count; i++) if (_rendererScratch[i] != nameTag) count++;
            e.Renderers = new Renderer[count];
            count = 0;
            for (int i = 0; i < _rendererScratch.Count; i++) if (_rendererScratch[i] != nameTag) e.Renderers[count++] = _rendererScratch[i];
            _rendererScratch.Clear();
            e.Feel = FeelHooks.Of(a);
            e.RenderersHidden = false;
            if (wasHidden) SetHidden(e.Renderers, ref e.RenderersHidden, true);
            e.AppliedAlpha = -1f; // re-apply fade (feel interface may be new)
        }

        static void SetHidden(Renderer[] renderers, ref bool current, bool hidden)
        {
            if (renderers == null || current == hidden) return;
            current = hidden;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].forceRenderingOff = hidden;
        }

        void UpdateCarried(Entry e, PlayerView view, float now)
        {
            string carried = "";
            if (view != null)
            {
                int slot = e.Avatar.Slot;
                for (int i = 0; i < view.Players.Count; i++)
                    if (view.Players[i].Id == slot) { carried = view.Players[i].CarryingItemId ?? ""; break; }
            }
            if (!string.Equals(carried, e.CarriedId, System.StringComparison.Ordinal))
            {
                EnsureHidden(e.CarriedRenderers, false);
                e.CarriedHidden = false;
                e.CarriedRenderers = null;
                e.CarriedId = carried;
                e.NextCarriedLookup = 0f;
            }
            if (e.CarriedRenderers == null && carried.Length > 0 && now >= e.NextCarriedLookup)
            {
                e.NextCarriedLookup = now + 0.5f;
                var visual = FindItemVisual(carried);
                if (visual != null) { e.CarriedRenderers = visual.GetComponentsInChildren<Renderer>(true); e.CarriedHidden = false; }
            }
            // The same item visual can move between carriers between snapshots; apply the state every frame
            // (a handful of renderers) instead of trusting a per-entry cache another entry may have undone.
            e.CarriedHidden = e.Alpha < 0.5f;
            EnsureHidden(e.CarriedRenderers, e.CarriedHidden);
        }

        static void EnsureHidden(Renderer[] renderers, bool hidden)
        {
            if (renderers == null) return;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null && renderers[i].forceRenderingOff != hidden) renderers[i].forceRenderingOff = hidden;
        }

        /// <summary>
        /// OfficeWorld pools item visuals as its children named by item id. (If the map module adds a public lookup,
        /// swap it in here.)
        /// </summary>
        public static Transform FindItemVisual(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || OfficeWorld.I == null) return null;
            return OfficeWorld.I.transform.Find(itemId);
        }

        void Restore(Entry e)
        {
            if (e.Avatar != null)
            {
                SetHidden(e.Renderers, ref e.RenderersHidden, false);
                if (e.Feel != null) e.Feel.SetVisibility(1f);
                if (e.Avatar.NameTag != null) e.Avatar.NameTag.alpha = 1f;
            }
            EnsureHidden(e.CarriedRenderers, false);
            e.CarriedHidden = false;
        }
    }
}
